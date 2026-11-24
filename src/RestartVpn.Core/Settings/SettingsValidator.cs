using System;
using System.Collections.Generic;
using System.IO;

namespace RestartVpn.Core.Settings
{
    /// <summary>Static validation with actionable messages. All checks are cheap and side-effect free.</summary>
    public static class SettingsValidator
    {
        public static List<SettingsIssue> Validate(AppSettings s)
        {
            var issues = new List<SettingsIssue>();
            if (s == null) return issues;

            ValidateMode(s, issues);
            ValidateTargets(s, issues);
            ValidateVpn(s, issues);
            ValidatePolicy(s, issues);
            ValidateProbe(s, issues);
            ValidateLogging(s, issues);
            return issues;
        }

        private static void Error(List<SettingsIssue> i, string path, string msg) =>
            i.Add(new SettingsIssue(IssueSeverity.Error, path, msg));

        private static void Warn(List<SettingsIssue> i, string path, string msg) =>
            i.Add(new SettingsIssue(IssueSeverity.Warning, path, msg));

        private static void ValidateMode(AppSettings s, List<SettingsIssue> issues)
        {
            if (!s.IsAutoRestart && !string.Equals(s.Mode, "MonitorOnly", StringComparison.OrdinalIgnoreCase))
                Error(issues, "mode", "Mode must be 'MonitorOnly' or 'AutoRestart'.");
        }

        private static void ValidateTargets(AppSettings s, List<SettingsIssue> issues)
        {
            int enabled = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int n = 0; n < s.Targets.Count; n++)
            {
                var t = s.Targets[n];
                string p = "targets[" + n + "]";

                if (t.Enabled) enabled++;

                if (string.IsNullOrWhiteSpace(t.Name))
                    Warn(issues, p + ".name", "Target has no name; the host name will be used.");
                else if (!names.Add(t.Name.Trim()))
                    Warn(issues, p + ".name", "Duplicate target name '" + t.Name + "'.");

                Uri uri;
                if (!Uri.TryCreate((t.Url ?? string.Empty).Trim(), UriKind.Absolute, out uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    Error(issues, p + ".url", "URL must be an absolute http:// or https:// address.");
                }
                else
                {
                    if (!urls.Add(uri.ToString())) Warn(issues, p + ".url", "Duplicate target URL.");
                    if (uri.Scheme == Uri.UriSchemeHttp) Warn(issues, p + ".url", "Plain HTTP cannot confirm that TLS works.");
                }

                if (t.TimeoutMs < 500 || t.TimeoutMs > 120000)
                    Error(issues, p + ".timeoutMs", "Timeout must be between 500 and 120000 ms.");
                if (t.RetryTimeoutMs < 500 || t.RetryTimeoutMs > 120000)
                    Error(issues, p + ".retryTimeoutMs", "Retry timeout must be between 500 and 120000 ms.");

                if (t.ExpectedStatus != null)
                {
                    foreach (int code in t.ExpectedStatus)
                        if (code < 100 || code > 599)
                        {
                            Error(issues, p + ".expectedStatus", "HTTP status " + code + " is not a valid code.");
                            break;
                        }
                }

                string method = (t.Method ?? string.Empty).Trim().ToUpperInvariant();
                if (method != "GET" && method != "HEAD")
                    Error(issues, p + ".method", "Method must be GET or HEAD.");

                if (!string.IsNullOrEmpty(t.BodyPattern))
                {
                    try { new System.Text.RegularExpressions.Regex(t.BodyPattern); }
                    catch (Exception ex)
                    {
                        Error(issues, p + ".bodyPattern", "Invalid regular expression: " + ex.Message);
                    }
                }
            }

            if (enabled == 0)
                Error(issues, "targets", "At least one enabled target is required.");
        }

        private static void ValidateVpn(AppSettings s, List<SettingsIssue> issues)
        {
            var v = s.Vpn;
            if (!string.Equals(v.Backend, "openvpn-gui", StringComparison.OrdinalIgnoreCase))
                Error(issues, "vpn.backend", "Only the 'openvpn-gui' backend is implemented in this version.");

            if (!string.IsNullOrWhiteSpace(v.ProfileName))
            {
                string name = v.ProfileName.Trim();
                // The name is used as a file name for the OpenVPN log and as a GUI command
                // argument; path separators or ".." must never reach it.
                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    name.Contains("..") ||
                    name.EndsWith(".", StringComparison.Ordinal))
                {
                    Error(issues, "vpn.profileName", "Profile name must be a plain file name without path separators or '..'.");
                }
            }

            bool needsProfile = s.IsAutoRestart;
            if (string.IsNullOrWhiteSpace(v.ProfileName) && string.IsNullOrWhiteSpace(v.ConfigPath))
            {
                if (needsProfile)
                    Error(issues, "vpn.profileName", "Profile name or config path is required for AutoRestart mode.");
                else
                    Warn(issues, "vpn.profileName", "No VPN profile configured; only monitoring is possible.");
            }

            if (!string.IsNullOrWhiteSpace(v.ConfigPath) && !File.Exists(Environment.ExpandEnvironmentVariables(v.ConfigPath)))
                Warn(issues, "vpn.configPath", "Config file not found: " + v.ConfigPath);

            if (!string.IsNullOrWhiteSpace(v.OpenVpnGuiPath) && !File.Exists(Environment.ExpandEnvironmentVariables(v.OpenVpnGuiPath)))
                Warn(issues, "vpn.openVpnGuiPath", "openvpn-gui.exe not found at " + v.OpenVpnGuiPath);

            if (v.ConnectTimeoutSec < 5 || v.ConnectTimeoutSec > 300)
                Error(issues, "vpn.connectTimeoutSec", "Connect timeout must be between 5 and 300 seconds.");
            if (v.CommandTimeoutSec < 3 || v.CommandTimeoutSec > 120)
                Error(issues, "vpn.commandTimeoutSec", "Command timeout must be between 3 and 120 seconds.");
        }

        private static void ValidatePolicy(AppSettings s, List<SettingsIssue> issues)
        {
            var p = s.Policy;
            string q = (p.Quorum ?? string.Empty).Trim();
            if (!string.Equals(q, "All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(q, "Majority", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(q, "Any", StringComparison.OrdinalIgnoreCase) &&
                p.CountQuorum() < 0)
            {
                Error(issues, "policy.quorum", "Quorum must be All, Majority, Any or Count:N.");
            }

            if (p.CountQuorum() > CountEnabled(s))
                Warn(issues, "policy.quorum", "Count quorum exceeds the number of enabled targets; it can never be met.");

            Range(issues, "policy.failureThreshold", p.FailureThreshold, 1, 20);
            Range(issues, "policy.correlationWindowSec", p.CorrelationWindowSec, 0, 3600);
            Range(issues, "policy.probeIntervalSec", p.ProbeIntervalSec, 1, 3600);
            Range(issues, "policy.retryDelayMs", p.RetryDelayMs, 0, 60000);
            Range(issues, "policy.recoveryTimeoutSec", p.RecoveryTimeoutSec, 5, 600);
            Range(issues, "policy.recoverySuccesses", p.RecoverySuccesses, 1, 10);
            Range(issues, "policy.minRestartGapSec", p.MinRestartGapSec, 5, 3600);
            Range(issues, "policy.maxRestartGapSec", p.MaxRestartGapSec, 5, 86400);
            Range(issues, "policy.maxRestartsPerHour", p.MaxRestartsPerHour, 1, 100);
            Range(issues, "policy.failedRestartsBeforeCircuit", p.FailedRestartsBeforeCircuit, 1, 100);
            Range(issues, "policy.circuitCooldownSec", p.CircuitCooldownSec, 0, 86400);
            Range(issues, "policy.jitterPercent", p.JitterPercent, 0, 50);
            Range(issues, "policy.connectCooldownSec", p.ConnectCooldownSec, 0, 600);
            Range(issues, "policy.alertThrottleSec", p.AlertThrottleSec, 0, 3600);

            if (p.MaxRestartGapSec < p.MinRestartGapSec)
                Error(issues, "policy.maxRestartGapSec", "Maximum restart gap must be >= minimum restart gap.");
            if (p.CorrelationWindowSec < p.ProbeIntervalSec)
                Warn(issues, "policy.correlationWindowSec", "Correlation window is shorter than the probe interval.");
        }

        private static void ValidateProbe(AppSettings s, List<SettingsIssue> issues)
        {
            Range(issues, "probe.maxRedirects", s.Probe.MaxRedirects, 0, 20);
            Range(issues, "probe.maxBodyBytes", s.Probe.MaxBodyBytes, 0, 1048576);
            if (string.IsNullOrWhiteSpace(s.Probe.UserAgent))
                Warn(issues, "probe.userAgent", "Empty User-Agent may be blocked by some sites.");
        }

        private static void ValidateLogging(AppSettings s, List<SettingsIssue> issues)
        {
            var level = (s.Logging.Level ?? string.Empty).Trim();
            if (!"Error".Equals(level, StringComparison.OrdinalIgnoreCase) &&
                !"Warn".Equals(level, StringComparison.OrdinalIgnoreCase) &&
                !"Info".Equals(level, StringComparison.OrdinalIgnoreCase) &&
                !"Debug".Equals(level, StringComparison.OrdinalIgnoreCase))
                Error(issues, "logging.level", "Log level must be Error, Warn, Info or Debug.");

            if (s.Logging.MaxFileBytes < 64 * 1024 || s.Logging.MaxFileBytes > 100L * 1024 * 1024)
                Error(issues, "logging.maxFileBytes", "Log size must be between 64 KB and 100 MB.");
            Range(issues, "logging.keepFiles", s.Logging.KeepFiles, 1, 50);
        }

        private static int CountEnabled(AppSettings s)
        {
            int n = 0;
            foreach (var t in s.Targets) if (t.Enabled) n++;
            return n;
        }

        private static void Range(List<SettingsIssue> issues, string path, int value, int min, int max)
        {
            if (value < min || value > max)
                Error(issues, path, "Value must be between " + min + " and " + max + " (got " + value + ").");
        }
    }
}
