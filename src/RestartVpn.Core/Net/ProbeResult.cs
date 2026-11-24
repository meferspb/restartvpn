using System;

namespace RestartVpn.Core.Net
{
    public enum ProbeOutcome
    {
        Ok = 0,
        HttpStatus,
        BodyMismatch,
        Timeout,
        DnsFailure,
        TlsFailure,
        ConnectionFailure,
        InvalidResponse,
        Unknown
    }

    public sealed class ProbeResult
    {
        public string TargetName { get; set; }
        public string Url { get; set; }
        public bool Success { get; set; }
        public ProbeOutcome Outcome { get; set; }
        public int? StatusCode { get; set; }
        public long LatencyMs { get; set; }
        public string Detail { get; set; }
        public DateTimeOffset TimestampUtc { get; set; }

        public static ProbeResult Fail(string name, string url, ProbeOutcome outcome, string detail, long latencyMs) =>
            new ProbeResult
            {
                TargetName = name,
                Url = url,
                Success = false,
                Outcome = outcome,
                Detail = detail,
                LatencyMs = latencyMs,
                TimestampUtc = DateTimeOffset.UtcNow
            };

        public override string ToString() =>
            (Success ? "OK" : "FAIL") + " " + (TargetName ?? Url) + " " + Detail + " (" + LatencyMs + " ms)";
    }
}
