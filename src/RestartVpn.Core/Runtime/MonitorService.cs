using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Net;
using RestartVpn.Core.Policy;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;

namespace RestartVpn.Core.Runtime
{
    /// <summary>
    /// Orchestrates probing, policy evaluation and VPN control. Every VPN action is serialized
    /// through a semaphore, and the loop never acts when the app is in monitor-only mode.
    /// </summary>
    public sealed class MonitorService : IDisposable
    {
        private sealed class TargetRuntime
        {
            public TargetSettings Settings;
            public int ConsecutiveFailures;
            public ProbeResult LastResult;
            public DateTimeOffset? LastCheckedAt;
            public DateTimeOffset? LastFailureAt;
        }

        private readonly AppSettings _settings;
        private readonly AppLog _log;
        private readonly IVpnBackend _vpn;
        private readonly HttpProbe _probe;
        private readonly DecisionEngine _engine;
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _cycleGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _actionGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, DateTimeOffset> _alertTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        private readonly List<TargetRuntime> _targets = new List<TargetRuntime>();

        private CancellationTokenSource _cts;
        private Task _loopTask;
        private MonitorRunState _state = MonitorRunState.Stopped;
        private string _stateDetail = string.Empty;
        private VpnStatus _vpnStatus;
        private DateTimeOffset? _snoozeUntil;
        private string _lastDecision = string.Empty;
        private bool _autoEnabled;
        private string _autoDisabledReason = string.Empty;
        private bool _disposed;

        public MonitorService(AppSettings settings, AppLog log, IVpnBackend vpn)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = log;
            _vpn = vpn ?? throw new ArgumentNullException(nameof(vpn));
            _probe = new HttpProbe(settings.Probe);
            _engine = new DecisionEngine(settings);

            foreach (var t in settings.Targets)
                _targets.Add(new TargetRuntime { Settings = t });

            _vpnStatus = new VpnStatus { State = VpnState.Unknown, Detail = "not sampled yet" };
        }

        public event Action<MonitorEvent> EventRaised;
        public event Action SnapshotChanged;

        public bool IsRunning => _loopTask != null && !_loopTask.IsCompleted;
        public DecisionEngine Engine => _engine;

        public MonitorSnapshot GetSnapshot()
        {
            lock (_gate) return BuildSnapshotLocked();
        }

        public void Start()
        {
            if (IsRunning) return;

            Prepare();
            if (_state == MonitorRunState.Error) return;

            _cts = new CancellationTokenSource();
            SetState(MonitorRunState.Starting, "starting");
            Emit(LogLevel.Info, "monitor.started", "Monitoring started.");
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            var cts = _cts;
            var task = _loopTask;
            if (cts == null) return;

            try { cts.Cancel(); } catch { }
            bool completed = task == null || task.IsCompleted;
            if (task != null && !completed)
            {
                try
                {
                    var finished = await Task.WhenAny(task, Task.Delay(15000)).ConfigureAwait(false);
                    completed = ReferenceEquals(finished, task);
                    if (!completed) _log.Warn("Monitor loop did not stop within 15 s; it will finish in the background.");
                }
                catch { }
            }

            // Keep the task reference while it still runs: Dispose and a later Start must
            // see that the loop is alive instead of disposing objects it is using.
            _loopTask = completed ? null : task;
            _cts = null;
            SetState(MonitorRunState.Stopped, "stopped");
            Emit(LogLevel.Info, "monitor.stopped", "Monitoring stopped.");
            RaiseSnapshot();
        }

        private void Prepare()
        {
            _autoEnabled = _settings.IsAutoRestart;
            _autoDisabledReason = string.Empty;

            int enabled = _targets.Count(t => t.Settings.Enabled);
            if (enabled == 0)
            {
                SetState(MonitorRunState.Error, "no enabled targets configured");
                Emit(LogLevel.Error, "config.noTargets", "At least one enabled target is required.");
                return;
            }

            if (!_autoEnabled) return;

            var backend = _vpn as OpenVpnGuiBackend;
            if (backend != null)
            {
                if (!backend.ProfileExists)
                {
                    _autoEnabled = false;
                    _autoDisabledReason = "VPN profile not found: " + (string.IsNullOrEmpty(backend.ProfilePath)
                        ? _settings.Vpn.ProfileName
                        : backend.ProfilePath);
                }
                else if (!backend.GuiExists)
                {
                    _autoEnabled = false;
                    _autoDisabledReason = "openvpn-gui.exe not found: " + _settings.Vpn.OpenVpnGuiPath;
                }
            }

            if (!_autoEnabled)
            {
                Emit(LogLevel.Warn, "config.autoDisabled",
                    "AutoRestart was requested but is disabled: " + _autoDisabledReason + ". Monitoring continues in alert-only mode.");
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            try
            {
                bool startupConnectDone = false;

                while (!ct.IsCancellationRequested)
                {
                    if (_snoozeUntil.HasValue && DateTimeOffset.Now < _snoozeUntil.Value)
                    {
                        SetState(MonitorRunState.Snoozed, "snoozed until " + _snoozeUntil.Value.ToLocalTime().ToString("HH:mm"));
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (!startupConnectDone)
                    {
                        startupConnectDone = true;
                        if (_autoEnabled && _settings.Vpn.ConnectOnStart)
                        {
                            try
                            {
                                var startupStatus = _vpn.GetStatus();
                                if (!startupStatus.ProfileProcessPresent)
                                {
                                    Emit(LogLevel.Info, "vpn.autostart", "Profile is not running; connecting at startup.");
                                    _engine.RecordConnectAttempt(DateTimeOffset.Now);
                                    var result = _vpn.Connect();
                                    Emit(result.Success ? LogLevel.Info : LogLevel.Warn,
                                        result.Success ? "vpn.autostart.ok" : "vpn.autostart.failed",
                                        result.Message);
                                }
                            }
                            catch (Exception ex)
                            {
                                _log.Error("Startup connect failed.", ex);
                                Emit(LogLevel.Warn, "vpn.autostart.error", "Startup connect failed: " + ex.Message);
                            }
                        }
                    }

                    var started = DateTimeOffset.Now;
                    bool gotGate = false;
                    try
                    {
                        await _cycleGate.WaitAsync(ct).ConfigureAwait(false);
                        gotGate = true;
                        await RunCycleAsync(true, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _log.Error("Monitor cycle failed.", ex);
                        Emit(LogLevel.Error, "cycle.error", "Monitor cycle failed: " + ex.Message);
                        SetState(MonitorRunState.Error, ex.Message);
                    }
                    finally
                    {
                        if (gotGate) _cycleGate.Release();
                    }

                    var elapsed = DateTimeOffset.Now - started;
                    var delay = TimeSpan.FromSeconds(Math.Max(1, _settings.Policy.ProbeIntervalSec)) - elapsed;
                    if (delay < TimeSpan.FromMilliseconds(500)) delay = TimeSpan.FromMilliseconds(500);

                    try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
            finally
            {
                SetState(MonitorRunState.Stopped, "stopped");
                RaiseSnapshot();
            }
        }

        private async Task RunCycleAsync(bool allowActions, CancellationToken ct)
        {
            SetState(MonitorRunState.Monitoring, "probing targets");
            var results = await ProbeAllAsync(ct).ConfigureAwait(false);
            var vpnStatus = _vpn.GetStatus();

            lock (_gate)
            {
                foreach (var r in results)
                {
                    var runtime = _targets.FirstOrDefault(t => ReferenceEquals(t.Settings, r.Target));
                    if (runtime == null) continue;

                    runtime.LastResult = r.Result;
                    runtime.LastCheckedAt = DateTimeOffset.Now;
                    runtime.ConsecutiveFailures = r.ConsecutiveFailures;
                    if (!r.Result.Success) runtime.LastFailureAt = DateTimeOffset.Now;
                }
                _vpnStatus = vpnStatus;
            }

            var decision = _engine.Evaluate(ToSnapshots(), _vpnStatus, DateTimeOffset.Now);
            _lastDecision = decision.Kind + " — " + decision.Reason;
            RaiseSnapshot();

            if (decision.Kind != DecisionKind.None)
                _log.Debug("Decision: " + _lastDecision);

            if (!allowActions) return;

            switch (decision.Kind)
            {
                case DecisionKind.None:
                case DecisionKind.WaitingBackoff:
                    return;

                case DecisionKind.ServiceAlert:
                case DecisionKind.VpnAlert:
                case DecisionKind.CircuitOpen:
                    RaiseAlert(decision);
                    return;

                case DecisionKind.Connect:
                case DecisionKind.RestartSoft:
                case DecisionKind.RestartHard:
                    if (!_autoEnabled)
                    {
                        RaiseAlert(new PolicyDecision
                        {
                            Kind = DecisionKind.ServiceAlert,
                            Reason = "action suppressed: " + _autoDisabledReason,
                            Suspects = decision.Suspects
                        });
                        return;
                    }
                    await ExecuteActionAsync(decision, ct).ConfigureAwait(false);
                    return;
            }
        }

        private async Task ExecuteActionAsync(PolicyDecision decision, CancellationToken ct)
        {
            bool gotGate = false;
            try
            {
                await _actionGate.WaitAsync(ct).ConfigureAwait(false);
                gotGate = true;

                SetState(MonitorRunState.Restarting, decision.Kind + ": " + decision.Reason);
                Emit(LogLevel.Info, "decision." + decision.Kind, decision.Reason);

                var now = DateTimeOffset.Now;
                bool success;
                string detail;

                if (decision.Kind == DecisionKind.Connect)
                {
                    _engine.RecordConnectAttempt(now);
                    Emit(LogLevel.Info, "vpn.connect", "Connecting OpenVPN profile '" + _vpn.ProfileName + "'.");
                    var r = _vpn.Connect();
                    detail = r.Message;
                    success = r.Success;
                    if (!success)
                        Emit(LogLevel.Warn, "vpn.connect.failed", "Connect failed: " + r.Message);
                }
                else
                {
                    bool hard = decision.Kind == DecisionKind.RestartHard;
                    Emit(LogLevel.Info, "vpn.restart", "Restarting VPN (" + (hard ? "hard" : "soft") + "): " + decision.Reason);

                    if (hard)
                    {
                        var r = _vpn.HardRestart();
                        success = r.Success;
                        detail = r.Message;
                    }
                    else
                    {
                        var d = _vpn.Disconnect();
                        if (!d.Success) _log.Warn("Soft restart: disconnect reported: " + d.Message);
                        var c = _vpn.Connect();
                        success = c.Success;
                        detail = c.Message;
                    }

                    if (!success)
                        Emit(LogLevel.Warn, "vpn.restart.failed", "Restart command failed: " + detail);
                }

                bool recovered = success && await WaitRecoveryAsync(decision.Suspects, ct).ConfigureAwait(false);
                bool aborted = ct.IsCancellationRequested;

                if (aborted)
                {
                    Emit(LogLevel.Warn, "vpn.aborted", "VPN action was interrupted by stop.");
                }
                else
                {
                    _engine.RecordAttemptResult(recovered, DateTimeOffset.Now);
                    lock (_gate)
                    {
                        foreach (var t in _targets) { t.ConsecutiveFailures = 0; t.LastFailureAt = null; }
                    }

                    if (recovered)
                        Emit(LogLevel.Info, "vpn.recovered", "VPN action completed and the affected targets are reachable again.");
                    else
                        Emit(LogLevel.Warn, "vpn.notRecovered",
                            "VPN action finished, but targets did not recover within " + _settings.Policy.RecoveryTimeoutSec + " s.");
                }

                lock (_gate) _vpnStatus = _vpn.GetStatus();
            }
            catch (OperationCanceledException)
            {
                Emit(LogLevel.Warn, "vpn.aborted", "VPN action cancelled.");
            }
            catch (Exception ex)
            {
                _log.Error("VPN action failed.", ex);
                Emit(LogLevel.Error, "vpn.error", "VPN action failed: " + ex.Message);
            }
            finally
            {
                if (gotGate) _actionGate.Release();
                SetState(MonitorRunState.Monitoring, "monitoring");
                RaiseSnapshot();
            }
        }

        private async Task<bool> WaitRecoveryAsync(List<string> suspects, CancellationToken ct)
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(5, _settings.Policy.RecoveryTimeoutSec));
            var deadline = DateTimeOffset.Now + timeout;
            var targets = _targets.Where(t => t.Settings.Enabled).ToList();
            if (suspects != null && suspects.Count > 0)
            {
                var filtered = targets.Where(t => suspects.Contains(t.Settings.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                if (filtered.Count > 0) targets = filtered;
            }
            if (targets.Count == 0) return false;

            int needed = Math.Max(1, _settings.Policy.RecoverySuccesses);
            int consecutive = 0;

            while (DateTimeOffset.Now < deadline)
            {
                ct.ThrowIfCancellationRequested();

                bool allOk = true;
                foreach (var t in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    var probe = await _probe.ProbeAsync(t.Settings, ct).ConfigureAwait(false);
                    lock (_gate)
                    {
                        t.LastResult = probe;
                        t.LastCheckedAt = DateTimeOffset.Now;
                    }

                    if (!probe.Success) { allOk = false; break; }
                }

                if (allOk)
                {
                    consecutive++;
                    if (consecutive >= needed)
                    {
                        RaiseSnapshot();
                        return true;
                    }
                }
                else consecutive = 0;

                RaiseSnapshot();
                await Task.Delay(3000, ct).ConfigureAwait(false);
            }

            return false;
        }

        private sealed class ProbeOutcomeInternal
        {
            public TargetSettings Target;
            public ProbeResult Result;
            public int ConsecutiveFailures;
            public bool Retried;
            public string FirstFailure;
        }

        private async Task<List<ProbeOutcomeInternal>> ProbeAllAsync(CancellationToken ct)
        {
            var enabled = _targets.Where(t => t.Settings.Enabled).ToList();
            var tasks = enabled.Select(t => ProbeOneAsync(t, ct)).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            return new List<ProbeOutcomeInternal>(results);
        }

        private async Task<ProbeOutcomeInternal> ProbeOneAsync(TargetRuntime runtime, CancellationToken ct)
        {
            var target = runtime.Settings;
            var first = await _probe.ProbeAsync(target, ct).ConfigureAwait(false);

            if (first.Success)
            {
                return new ProbeOutcomeInternal
                {
                    Target = target,
                    Result = first,
                    ConsecutiveFailures = 0
                };
            }

            Emit(LogLevel.Debug, "probe.retry", "First check failed (" + first.Detail + "), retrying once.", target.Name);

            int delay = Math.Max(0, _settings.Policy.RetryDelayMs);
            if (delay > 0) await Task.Delay(delay, ct).ConfigureAwait(false);

            var retryTarget = CloneWithTimeout(target, target.RetryTimeoutMs);
            var second = await _probe.ProbeAsync(retryTarget, ct).ConfigureAwait(false);

            if (second.Success)
            {
                Emit(LogLevel.Info, "probe.transient",
                    "Transient failure (" + first.Detail + "), retry succeeded; not counted.", target.Name);
                return new ProbeOutcomeInternal
                {
                    Target = target,
                    Result = second,
                    ConsecutiveFailures = 0,
                    Retried = true,
                    FirstFailure = first.Detail
                };
            }

            int failures = runtime.ConsecutiveFailures + 1;
            var level = (failures <= Math.Max(1, _settings.Policy.FailureThreshold) || failures % 10 == 0)
                ? LogLevel.Warn
                : LogLevel.Debug;
            Emit(level, "probe.fail",
                "Check failed (" + failures + "): " + second.Detail, target.Name);
            return new ProbeOutcomeInternal
            {
                Target = target,
                Result = second,
                ConsecutiveFailures = failures,
                Retried = true,
                FirstFailure = first.Detail
            };
        }

        private static TargetSettings CloneWithTimeout(TargetSettings t, int timeoutMs)
        {
            return new TargetSettings
            {
                Name = t.Name,
                Url = t.Url,
                Enabled = t.Enabled,
                Method = t.Method,
                TimeoutMs = timeoutMs,
                RetryTimeoutMs = t.RetryTimeoutMs,
                ExpectedStatus = t.ExpectedStatus,
                Treat5xxAsFailure = t.Treat5xxAsFailure,
                BodyPattern = t.BodyPattern,
                Headers = t.Headers
            };
        }

        private void RaiseAlert(PolicyDecision decision)
        {
            string key = decision.Kind + "|" + string.Join(",", decision.Suspects);
            var now = DateTimeOffset.Now;
            lock (_gate)
            {
                if (_alertTimes.TryGetValue(key, out var last) &&
                    (now - last).TotalSeconds < Math.Max(10, _settings.Policy.AlertThrottleSec))
                    return;
                _alertTimes[key] = now;
            }

            Emit(LogLevel.Warn, "alert." + decision.Kind, decision.Reason);
        }

        private List<TargetSnapshot> ToSnapshots()
        {
            return _targets.Select(t => new TargetSnapshot
            {
                Name = t.Settings.Name,
                Enabled = t.Settings.Enabled,
                ConsecutiveFailures = t.ConsecutiveFailures,
                LastFailureAt = t.LastFailureAt
            }).ToList();
        }

        public async Task CheckNowAsync()
        {
            if (_state == MonitorRunState.Stopped) return;
            bool gotGate = false;
            try
            {
                await _cycleGate.WaitAsync().ConfigureAwait(false);
                gotGate = true;
                await RunCycleAsync(false, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (gotGate) _cycleGate.Release();
            }
        }

        public async Task<string> RestartNowAsync(bool hard)
        {
            if (_vpn == null) return "No VPN backend.";

            bool gotCycle = false;
            bool gotAction = false;
            try
            {
                // Serialize with probe cycles: otherwise a cycle that started before the
                // manual restart could overwrite the reset failure counters with stale values.
                if (!await _cycleGate.WaitAsync(0).ConfigureAwait(false))
                    return "A check is already running; try again in a few seconds.";
                gotCycle = true;

                if (!await _actionGate.WaitAsync(0).ConfigureAwait(false))
                    return "Another VPN action is already running.";
                gotAction = true;

                SetState(MonitorRunState.Restarting, "manual restart");
                Emit(LogLevel.Info, "vpn.manual", "Manual " + (hard ? "hard" : "soft") + " restart requested.");
                VpnCommandResult result;
                if (hard)
                {
                    result = _vpn.HardRestart();
                }
                else
                {
                    var disconnect = _vpn.Disconnect();
                    if (!disconnect.Success) _log.Warn("Manual soft restart: " + disconnect.Message);
                    result = _vpn.Connect();
                }

                if (result.Success)
                {
                    var suspects = _targets.Where(t => t.ConsecutiveFailures > 0).Select(t => t.Settings.Name).ToList();
                    bool recovered = await WaitRecoveryAsync(suspects, CancellationToken.None).ConfigureAwait(false);
                    lock (_gate) foreach (var t in _targets) { t.ConsecutiveFailures = 0; t.LastFailureAt = null; }
                    return recovered ? result.Message + " Targets are reachable again." : result.Message + " Targets did not recover in time.";
                }
                return result.Message;
            }
            catch (Exception ex)
            {
                return "Restart failed: " + ex.Message;
            }
            finally
            {
                if (gotAction) _actionGate.Release();
                if (gotCycle) _cycleGate.Release();
                SetState(IsRunning ? MonitorRunState.Monitoring : MonitorRunState.Stopped,
                    IsRunning ? "monitoring" : "stopped");
                RaiseSnapshot();
            }
        }

        public void Snooze(TimeSpan duration)
        {
            _snoozeUntil = DateTimeOffset.Now + duration;
            SetState(MonitorRunState.Snoozed, "snoozed until " + _snoozeUntil.Value.ToLocalTime().ToString("HH:mm"));
            Emit(LogLevel.Info, "snooze", "Monitoring snoozed for " + (int)duration.TotalMinutes + " minutes.");
            RaiseSnapshot();
        }

        public void ClearSnooze()
        {
            _snoozeUntil = null;
            SetState(IsRunning ? MonitorRunState.Monitoring : MonitorRunState.Stopped,
                IsRunning ? "monitoring" : "stopped");
            RaiseSnapshot();
        }

        private void SetState(MonitorRunState state, string detail)
        {
            lock (_gate)
            {
                _state = state;
                _stateDetail = detail ?? string.Empty;
            }
        }

        private MonitorSnapshot BuildSnapshotLocked()
        {
            return new MonitorSnapshot
            {
                State = _state,
                StateDetail = _stateDetail,
                Vpn = _vpnStatus ?? new VpnStatus(),
                AutoRestartEnabled = _autoEnabled,
                AutoRestartDisabledReason = _autoDisabledReason,
                RestartsLastHour = _engine.RestartsLastHour(DateTimeOffset.Now),
                ConsecutiveFailedRestarts = _engine.ConsecutiveFailedRestarts,
                NextActionAllowedAt = _engine.NextAllowedAt,
                SnoozedUntil = _snoozeUntil,
                LastDecision = _lastDecision,
                UpdatedAt = DateTimeOffset.Now,
                Targets = _targets.Select(t => new TargetStatus
                {
                    Name = t.Settings.Name,
                    Url = t.Settings.Url,
                    Enabled = t.Settings.Enabled,
                    HasResult = t.LastResult != null,
                    Success = t.LastResult != null && t.LastResult.Success,
                    Outcome = t.LastResult == null ? ProbeOutcome.Unknown : t.LastResult.Outcome,
                    Detail = t.LastResult == null ? "not probed yet" : t.LastResult.Detail,
                    LatencyMs = t.LastResult == null ? 0 : t.LastResult.LatencyMs,
                    ConsecutiveFailures = t.ConsecutiveFailures,
                    LastCheckedAt = t.LastCheckedAt,
                    LastFailureAt = t.LastFailureAt
                }).ToList()
            };
        }

        private void RaiseSnapshot()
        {
            var handler = SnapshotChanged;
            if (handler == null) return;
            try { handler(); } catch { }
        }

        private void Emit(LogLevel level, string code, string message, string target = "")
        {
            var ev = new MonitorEvent
            {
                At = DateTimeOffset.Now,
                Level = level,
                Code = code,
                Message = message,
                Target = target ?? string.Empty
            };

            if (level == LogLevel.Error) _log.Error(message);
            else if (level == LogLevel.Warn) _log.Warn(message);
            else if (level == LogLevel.Debug) _log.Debug(message);
            else _log.Info(message);

            _log.Event("monitor", message, new Dictionary<string, object>
            {
                ["code"] = code,
                ["target"] = target ?? string.Empty
            });

            var handler = EventRaised;
            if (handler == null) return;
            try { handler(ev); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts?.Cancel(); } catch { }

            bool finished = _loopTask == null || _loopTask.IsCompleted;
            if (!finished)
            {
                try { finished = _loopTask.Wait(7000); } catch { }
            }

            // Do not dispose objects that a still-running loop may touch; the process is
            // exiting anyway and a leaked probe is better than an unobserved exception.
            if (finished)
            {
                try { _probe.Dispose(); } catch { }
                try { _cycleGate.Dispose(); } catch { }
                try { _actionGate.Dispose(); } catch { }
                try { _cts?.Dispose(); } catch { }
            }
        }
    }
}
