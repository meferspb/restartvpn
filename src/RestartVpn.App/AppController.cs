using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Runtime;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;

namespace RestartVpn.App
{
    /// <summary>
    /// Owns the settings, log, VPN backend and monitor service. The UI talks only to this class.
    /// </summary>
    public sealed class AppController : IDisposable
    {
        private readonly SettingsStore _store;
        private AppSettings _settings;
        private AppLog _log;
        private IVpnBackend _vpn;
        private MonitorService _service;
        private bool _disposed;

        public AppPaths Paths { get; }
        public SettingsLoadResult LoadResult { get; private set; }
        public bool DryRun { get; set; }
        public string LastError { get; private set; } = string.Empty;

        public event Action<MonitorEvent> EventRaised;
        public event Action Changed;

        public AppSettings Settings => _settings;
        public AppLog Log => _log;
        public IVpnBackend Vpn => _vpn;
        public MonitorService Service => _service;

        public AppController(AppPaths paths, SettingsStore store, SettingsLoadResult load, bool dryRun = false)
        {
            Paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            LoadResult = load;
            DryRun = dryRun;

            // The installer can enable autostart before any settings file exists; keep the
            // checkbox in sync with the registry instead of silently disabling it on save.
            if (paths.RunningFromInstallDir)
            {
                try { load.Settings.Ui.Autostart = Install.Installer.IsAutostartEnabled(); } catch { }
            }

            Rebuild(load.Settings);
        }

        public bool IsMonitoring => _service != null && _service.IsRunning;

        public MonitorSnapshot Snapshot
        {
            get
            {
                try { return _service?.GetSnapshot() ?? new MonitorSnapshot(); }
                catch { return new MonitorSnapshot(); }
            }
        }

        public bool HasSettingsErrors
        {
            get { return SettingsValidator.Validate(_settings).Any(i => i.Severity == IssueSeverity.Error); }
        }

        public List<SettingsIssue> Validate() => SettingsValidator.Validate(_settings);

        public bool StartMonitoring()
        {
            if (_settings == null) return false;
            var errors = Validate().Where(i => i.Severity == IssueSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                LastError = string.Join("; ", errors.Select(e => e.ToString()));
                return false;
            }

            try
            {
                _service.Start();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public async Task StopMonitoringAsync()
        {
            if (_service != null) await _service.StopAsync().ConfigureAwait(false);
            RaiseChanged();
        }

        public bool ApplySettings(AppSettings settings)
        {
            if (settings == null) return false;
            LastError = string.Empty;
            settings.Normalize();

            bool wasRunning = IsMonitoring;
            try { _service?.StopAsync().GetAwaiter().GetResult(); } catch { }

            if (_service != null && _service.IsRunning)
            {
                // A long VPN action survived the stop timeout. Applying settings now would
                // create a second monitor while the old loop may still touch the tunnel.
                LastError = "Действие с VPN ещё выполняется; настройки не применены. Повторите через несколько секунд.";
                return false;
            }

            Rebuild(settings);
            try { _store.Save(settings); } catch (Exception ex) { LastError = "Cannot save settings: " + ex.Message; }

            try
            {
                if (Paths.RunningFromInstallDir)
                    Install.Installer.SetAutostart(settings.Ui.Autostart, Paths.InstalledExePath);
            }
            catch { }

            // Saving settings must not start a monitor the user had stopped.
            if (wasRunning && !StartMonitoring()) return false;
            RaiseChanged();
            return true;
        }

        public Task CheckNowAsync()
        {
            return _service != null ? _service.CheckNowAsync() : Task.CompletedTask;
        }

        public async Task<string> RestartVpnAsync(bool hard)
        {
            if (DryRun) return "Режим --dry-run: действия с VPN отключены.";
            if (_service == null) return "Сервис не создан.";
            return await _service.RestartNowAsync(hard).ConfigureAwait(false);
        }

        public void Snooze(TimeSpan duration)
        {
            if (_service == null) return;
            _service.Snooze(duration);
            RaiseChanged();
        }

        public void Resume()
        {
            if (_service == null) return;
            _service.ClearSnooze();
            RaiseChanged();
        }

        private void Rebuild(AppSettings settings)
        {
            if (_service != null)
            {
                try { _service.EventRaised -= OnServiceEvent; } catch { }
                try { _service.SnapshotChanged -= RaiseChanged; } catch { }
            }
            try { _service?.Dispose(); } catch { }
            try { _log?.Dispose(); } catch { }

            _settings = settings;
            _settings.Normalize();
            if (DryRun) _settings.Mode = "MonitorOnly";

            string logDir = string.IsNullOrWhiteSpace(_settings.Logging.Directory)
                ? Paths.LogDir
                : Environment.ExpandEnvironmentVariables(_settings.Logging.Directory);

            _log = new AppLog(logDir,
                AppLog.ParseLevel(_settings.Logging.Level),
                _settings.Logging.MaxFileBytes,
                _settings.Logging.KeepFiles,
                _settings.Logging.Jsonl,
                _settings.Vpn.ProfileName);

            _vpn = new OpenVpnGuiBackend(_settings.Vpn, _log);
            _service = new MonitorService(_settings, _log, _vpn);
            _service.EventRaised += OnServiceEvent;
            _service.SnapshotChanged += RaiseChanged;

            if (DryRun)
                _log.Info("Dry-run mode: VPN actions are disabled.");
        }

        private void OnServiceEvent(MonitorEvent ev)
        {
            var handler = EventRaised;
            if (handler == null) return;
            try { handler(ev); } catch { }
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;
            try { handler(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _service?.Dispose(); } catch { }
            try { _log?.Dispose(); } catch { }
        }
    }
}
