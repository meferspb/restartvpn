using System;

namespace RestartVpn.Core.Vpn
{
    public enum VpnState
    {
        Unknown = 0,
        Disconnected,
        Connecting,
        Connected,
        Error
    }

    public enum VpnLogEvidence
    {
        None = 0,
        Connected,
        Stopped,
        Restarting,
        FatalAuth,
        FatalConfig,
        FatalOther
    }

    public sealed class VpnStatus
    {
        public VpnState State { get; set; } = VpnState.Unknown;
        public bool ProfileProcessPresent { get; set; }
        public bool AnyOpenVpnProcess { get; set; }
        public int OtherProfileProcessCount { get; set; }
        public string Detail { get; set; } = string.Empty;
        public string ProfilePath { get; set; } = string.Empty;
        public VpnLogEvidence Evidence { get; set; } = VpnLogEvidence.None;

        /// <summary>
        /// False when the log line is older than the current profile process; a stale
        /// AUTH_FAILED from a previous session must not block restarts forever.
        /// </summary>
        public bool EvidenceFresh { get; set; } = true;

        public string EvidenceDetail { get; set; } = string.Empty;
        public DateTimeOffset SampledAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public bool TunnelLooksDown =>
            !ProfileProcessPresent ||
            (EvidenceFresh && (Evidence == VpnLogEvidence.Stopped || Evidence == VpnLogEvidence.Restarting)) ||
            IsFatal;

        public bool IsFatal =>
            EvidenceFresh && (Evidence == VpnLogEvidence.FatalAuth ||
                              Evidence == VpnLogEvidence.FatalConfig ||
                              Evidence == VpnLogEvidence.FatalOther);
    }

    public sealed class VpnCommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public static VpnCommandResult Ok(string message) => new VpnCommandResult { Success = true, Message = message ?? string.Empty };
        public static VpnCommandResult Fail(string message) => new VpnCommandResult { Success = false, Message = message ?? string.Empty };

        public override string ToString() => (Success ? "OK: " : "FAIL: ") + Message;
    }

    public interface IVpnBackend
    {
        string Name { get; }
        string ProfileName { get; }
        string ProfilePath { get; }

        VpnStatus GetStatus();
        VpnCommandResult Connect();
        VpnCommandResult Disconnect();
        VpnCommandResult HardRestart();
    }
}
