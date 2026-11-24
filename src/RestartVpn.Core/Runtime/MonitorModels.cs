using System;
using System.Collections.Generic;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Net;
using RestartVpn.Core.Vpn;

namespace RestartVpn.Core.Runtime
{
    public enum MonitorRunState
    {
        Stopped = 0,
        Starting,
        Monitoring,
        Snoozed,
        Restarting,
        Error
    }

    public sealed class MonitorEvent
    {
        public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
        public LogLevel Level { get; set; } = LogLevel.Info;
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }

    public sealed class TargetStatus
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public bool HasResult { get; set; }
        public bool Success { get; set; }
        public ProbeOutcome Outcome { get; set; }
        public string Detail { get; set; } = string.Empty;
        public long LatencyMs { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset? LastCheckedAt { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
    }

    public sealed class MonitorSnapshot
    {
        public MonitorRunState State { get; set; } = MonitorRunState.Stopped;
        public string StateDetail { get; set; } = string.Empty;
        public VpnStatus Vpn { get; set; }
        public List<TargetStatus> Targets { get; set; } = new List<TargetStatus>();
        public bool AutoRestartEnabled { get; set; }
        public string AutoRestartDisabledReason { get; set; } = string.Empty;
        public int RestartsLastHour { get; set; }
        public int ConsecutiveFailedRestarts { get; set; }
        public DateTimeOffset? NextActionAllowedAt { get; set; }
        public DateTimeOffset? SnoozedUntil { get; set; }
        public string LastDecision { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

        public int DownCount
        {
            get
            {
                int n = 0;
                foreach (var t in Targets)
                    if (t.Enabled && t.HasResult && !t.Success) n++;
                return n;
            }
        }
    }
}
