using System;
using RestartVpn.Core.Policy;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;

namespace RestartVpn.Tests
{
    internal static class TestHelpers
    {
        public static AppSettings AutoSettings()
        {
            var s = new AppSettings { Mode = "AutoRestart" };
            s.Policy.FailureThreshold = 3;
            s.Policy.Quorum = "All";
            s.Policy.MinRestartGapSec = 30;
            s.Policy.MaxRestartGapSec = 300;
            s.Policy.MaxRestartsPerHour = 6;
            s.Policy.FailedRestartsBeforeCircuit = 3;
            s.Policy.CircuitCooldownSec = 900;
            s.Policy.JitterPercent = 0;
            s.Policy.RequireTunnelEvidence = false;
            s.Vpn.ProfileName = "my-profile";
            return s;
        }

        public static TargetSnapshot Target(string name, int failures, bool enabled = true) =>
            new TargetSnapshot { Name = name, Enabled = enabled, ConsecutiveFailures = failures };

        public static VpnStatus Connected() => new VpnStatus
        {
            State = VpnState.Connected,
            ProfileProcessPresent = true,
            Evidence = VpnLogEvidence.Connected,
            Detail = "tunnel is up"
        };

        public static VpnStatus NoProcess() => new VpnStatus
        {
            State = VpnState.Disconnected,
            ProfileProcessPresent = false,
            Detail = "not running"
        };

        public static DateTimeOffset T0 = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    }
}