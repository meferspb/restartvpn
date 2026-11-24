using System;
using System.Collections.Generic;
using System.Linq;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;

namespace RestartVpn.Core.Policy
{
    public enum DecisionKind
    {
        None = 0,
        ServiceAlert,
        VpnAlert,
        Connect,
        RestartSoft,
        RestartHard,
        WaitingBackoff,
        CircuitOpen
    }

    public sealed class TargetSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
    }

    public sealed class PolicyDecision
    {
        public DecisionKind Kind { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<string> Suspects { get; set; } = new List<string>();

        public bool IsAction => Kind == DecisionKind.Connect ||
                                Kind == DecisionKind.RestartSoft ||
                                Kind == DecisionKind.RestartHard;

        public override string ToString() => Kind + ": " + Reason;
    }

    /// <summary>
    /// Pure decision logic. No I/O, no clock of its own: every method receives "now",
    /// which makes the restart policy fully unit-testable.
    ///
    /// The legacy monitor restarted the VPN whenever each target had failed at some point in the
    /// last 30 s — even if the target had recovered meanwhile. This engine only counts targets
    /// that are consecutively down *now*, and requires a tunnel-level signal before restarting.
    /// </summary>
    public sealed class DecisionEngine
    {
        private readonly AppSettings _settings;
        private readonly Func<double> _random;

        private readonly List<DateTimeOffset> _restartTimes = new List<DateTimeOffset>();
        private DateTimeOffset? _nextAllowedAt;
        private DateTimeOffset? _lastConnectAttemptAt;
        private DateTimeOffset _circuitUntil = DateTimeOffset.MinValue;
        private int _consecutiveFailed;
        private bool _lastAttemptFailed;

        public DecisionEngine(AppSettings settings, Func<double> random01 = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _random = random01 ?? new Random().NextDouble;
        }

        public int ConsecutiveFailedRestarts => _consecutiveFailed;
        public int RestartsLastHour(DateTimeOffset now)
        {
            Prune(now);
            return _restartTimes.Count;
        }

        public DateTimeOffset? NextAllowedAt => _nextAllowedAt;
        public bool IsCircuitOpen(DateTimeOffset now) => now < _circuitUntil;

        public PolicyDecision Evaluate(IReadOnlyList<TargetSnapshot> targets, VpnStatus vpn, DateTimeOffset now)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (vpn == null) throw new ArgumentNullException(nameof(vpn));

            var policy = _settings.Policy;
            var enabled = targets.Where(t => t.Enabled).ToList();
            int freshnessSec = Math.Max(policy.CorrelationWindowSec, policy.ProbeIntervalSec * 2 + 10);
            var down = enabled.Where(t =>
                t.ConsecutiveFailures >= Math.Max(1, policy.FailureThreshold) &&
                (!t.LastFailureAt.HasValue || (now - t.LastFailureAt.Value).TotalSeconds <= freshnessSec)).ToList();

            if (down.Count == 0)
                return new PolicyDecision { Kind = DecisionKind.None, Reason = "all targets are available" };

            var suspects = down.Select(t => t.Name).ToList();

            if (!QuorumMet(down.Count, enabled.Count, policy.Quorum))
            {
                return new PolicyDecision
                {
                    Kind = DecisionKind.ServiceAlert,
                    Suspects = suspects,
                    Reason = down.Count + " of " + enabled.Count + " targets are down; quorum '" + policy.Quorum + "' is not met."
                };
            }

            if (!_settings.IsAutoRestart)
            {
                return new PolicyDecision
                {
                    Kind = DecisionKind.ServiceAlert,
                    Suspects = suspects,
                    Reason = "quorum is met, but the app runs in monitor-only mode; the VPN is not touched."
                };
            }

            if (vpn.IsFatal)
            {
                return new PolicyDecision
                {
                    Kind = DecisionKind.VpnAlert,
                    Suspects = suspects,
                    Reason = "OpenVPN reports a fatal condition: " + (string.IsNullOrEmpty(vpn.EvidenceDetail) ? vpn.Detail : vpn.EvidenceDetail)
                };
            }

            if (!vpn.ProfileProcessPresent)
            {
                if (vpn.AnyOpenVpnProcess && vpn.State == VpnState.Unknown)
                {
                    return new PolicyDecision
                    {
                        Kind = DecisionKind.VpnAlert,
                        Suspects = suspects,
                        Reason = "openvpn.exe is running, but its command line cannot be read; refusing to touch any connection."
                    };
                }

                if (IsCircuitOpen(now))
                    return new PolicyDecision { Kind = DecisionKind.CircuitOpen, Suspects = suspects, Reason = "circuit breaker is open after repeated failures." };

                // The attempt gap from RecordAttemptResult also applies to connects. Without
                // this check a failed connect was retried every connectCooldownSec (30 s),
                // bypassing the exponential backoff (60/120/... s).
                if (_nextAllowedAt.HasValue && now < _nextAllowedAt.Value)
                {
                    return new PolicyDecision
                    {
                        Kind = DecisionKind.WaitingBackoff,
                        Suspects = suspects,
                        Reason = "backoff: next connect is allowed at " + _nextAllowedAt.Value.ToLocalTime().ToString("HH:mm:ss") + "."
                    };
                }

                if (_lastConnectAttemptAt.HasValue &&
                    (now - _lastConnectAttemptAt.Value).TotalSeconds < Math.Max(1, policy.ConnectCooldownSec))
                {
                    return new PolicyDecision { Kind = DecisionKind.WaitingBackoff, Suspects = suspects, Reason = "connect cooldown is active." };
                }

                return new PolicyDecision
                {
                    Kind = DecisionKind.Connect,
                    Suspects = suspects,
                    Reason = "the profile process is not running; connecting without killing anything."
                };
            }

            bool tunnelDown = vpn.Evidence == VpnLogEvidence.Stopped ||
                              vpn.Evidence == VpnLogEvidence.Restarting ||
                              vpn.State == VpnState.Connecting;

            if (!tunnelDown && vpn.State == VpnState.Connected && policy.RequireTunnelEvidence)
            {
                if (policy.RestartOnServiceOutage)
                {
                    return GateRestart(now, suspects, "tunnel looks healthy, but all targets are down and RestartOnServiceOutage is enabled.");
                }

                return new PolicyDecision
                {
                    Kind = DecisionKind.ServiceAlert,
                    Suspects = suspects,
                    Reason = "targets are down while the tunnel is up; looks like a service or DNS problem, the VPN is not restarted."
                };
            }

            if (vpn.State == VpnState.Error)
                return new PolicyDecision { Kind = DecisionKind.VpnAlert, Suspects = suspects, Reason = vpn.Detail };

            if (vpn.State == VpnState.Unknown)
                return new PolicyDecision { Kind = DecisionKind.VpnAlert, Suspects = suspects, Reason = "VPN state is unknown: " + vpn.Detail };

            return GateRestart(now, suspects, tunnelDown
                ? "tunnel-level failure detected with the profile process still running."
                : "targets are down and the tunnel is not confirmed up.");
        }

        private PolicyDecision GateRestart(DateTimeOffset now, List<string> suspects, string reason)
        {
            if (IsCircuitOpen(now))
            {
                return new PolicyDecision
                {
                    Kind = DecisionKind.CircuitOpen,
                    Suspects = suspects,
                    Reason = "circuit breaker is open after " + _settings.Policy.FailedRestartsBeforeCircuit + " failed restarts; next attempt after " +
                             _circuitUntil.ToLocalTime().ToString("HH:mm:ss") + "."
                };
            }

            if (_nextAllowedAt.HasValue && now < _nextAllowedAt.Value)
            {
                return new PolicyDecision
                {
                    Kind = DecisionKind.WaitingBackoff,
                    Suspects = suspects,
                    Reason = "backoff: next restart is allowed at " + _nextAllowedAt.Value.ToLocalTime().ToString("HH:mm:ss") + "."
                };
            }

            bool hard = _settings.Policy.HardRestartAfterFailedSoft && _lastAttemptFailed;
            return new PolicyDecision
            {
                Kind = hard ? DecisionKind.RestartHard : DecisionKind.RestartSoft,
                Suspects = suspects,
                Reason = reason
            };
        }

        public static bool QuorumMet(int down, int enabled, string quorum)
        {
            if (enabled <= 0) return false;
            string q = (quorum ?? "All").Trim();

            if (string.Equals(q, "Any", StringComparison.OrdinalIgnoreCase)) return down >= 1;
            if (string.Equals(q, "Majority", StringComparison.OrdinalIgnoreCase)) return down * 2 > enabled;
            if (q.StartsWith("Count:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(q.Substring(6).Trim(), out int n))
                return down >= Math.Max(1, n);

            return down >= enabled; // All
        }

        public void RecordConnectAttempt(DateTimeOffset now)
        {
            _lastConnectAttemptAt = now;
        }

        public void RecordAttemptResult(bool success, DateTimeOffset now)
        {
            Prune(now);
            _restartTimes.Add(now);
            _lastAttemptFailed = !success;

            if (success)
            {
                _consecutiveFailed = 0;
                _circuitUntil = DateTimeOffset.MinValue;
            }
            else
            {
                _consecutiveFailed++;
                if (_consecutiveFailed >= Math.Max(1, _settings.Policy.FailedRestartsBeforeCircuit))
                {
                    _circuitUntil = now.AddSeconds(Math.Max(30, _settings.Policy.CircuitCooldownSec));
                    _consecutiveFailed = 0;
                }
            }

            _nextAllowedAt = now + ComputeGap(now);
        }

        public TimeSpan ComputeGap(DateTimeOffset now)
        {
            var p = _settings.Policy;
            double gap = Math.Max(1, p.MinRestartGapSec);

            for (int i = 0; i < _consecutiveFailed && i < 8; i++)
                gap = Math.Min(p.MaxRestartGapSec, gap * 2);

            if (RestartsLastHour(now) >= p.MaxRestartsPerHour)
                gap = p.MaxRestartGapSec;

            if (p.JitterPercent > 0)
            {
                double factor = 1.0 + ((_random() * 2.0) - 1.0) * (p.JitterPercent / 100.0);
                gap = gap * factor;
            }

            gap = Math.Max(p.MinRestartGapSec, Math.Min(p.MaxRestartGapSec, gap));
            return TimeSpan.FromSeconds(Math.Round(gap));
        }

        private void Prune(DateTimeOffset now)
        {
            _restartTimes.RemoveAll(t => (now - t).TotalHours >= 1);
        }
    }
}
