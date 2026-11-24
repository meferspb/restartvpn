using System;
using System.Collections.Generic;

namespace RestartVpn.Core.Settings
{
    /// <summary>
    /// Everything the application can be configured with. Every property has a safe default,
    /// so a partial or empty settings file is still valid (after Normalize + Validate).
    /// </summary>
    public sealed class AppSettings
    {
        public int Version { get; set; } = 1;

        /// <summary>"MonitorOnly" (default, never touches the VPN) or "AutoRestart".</summary>
        public string Mode { get; set; } = "MonitorOnly";

        public VpnSettings Vpn { get; set; } = new VpnSettings();
        public List<TargetSettings> Targets { get; set; } = new List<TargetSettings>();
        public PolicySettings Policy { get; set; } = new PolicySettings();
        public ProbeSettings Probe { get; set; } = new ProbeSettings();
        public LoggingSettings Logging { get; set; } = new LoggingSettings();
        public NotificationSettings Notifications { get; set; } = new NotificationSettings();
        public UiSettings Ui { get; set; } = new UiSettings();

        public bool IsAutoRestart => string.Equals(Mode, "AutoRestart", StringComparison.OrdinalIgnoreCase);

        /// <summary>Fills nulls left by a hand-edited file with defaults.</summary>
        public void Normalize()
        {
            if (Vpn == null) Vpn = new VpnSettings();
            if (Targets == null) Targets = new List<TargetSettings>();
            if (Policy == null) Policy = new PolicySettings();
            if (Probe == null) Probe = new ProbeSettings();
            if (Logging == null) Logging = new LoggingSettings();
            if (Notifications == null) Notifications = new NotificationSettings();
            if (Ui == null) Ui = new UiSettings();

            Vpn.Normalize();
            Policy.Normalize();
            Probe.Normalize();
            Logging.Normalize();
            Notifications.Normalize();

            for (int i = 0; i < Targets.Count; i++)
            {
                if (Targets[i] == null) Targets[i] = new TargetSettings();
                Targets[i].Normalize();
            }
        }

        public AppSettings Clone()
        {
            var json = SettingsStore.Serialize(this);
            var copy = SettingsStore.Deserialize(json);
            copy.Normalize();
            return copy;
        }
    }

    public sealed class VpnSettings
    {
        /// <summary>Only "openvpn-gui" is implemented today. The interface is prepared for more backends.</summary>
        public string Backend { get; set; } = "openvpn-gui";

        /// <summary>Profile name as OpenVPN GUI knows it (file name without .ovpn).</summary>
        public string ProfileName { get; set; } = string.Empty;

        /// <summary>Optional absolute path to the .ovpn file. Empty means auto-discovery.</summary>
        public string ConfigPath { get; set; } = string.Empty;

        public string OpenVpnGuiPath { get; set; } = @"C:\Program Files\OpenVPN\bin\openvpn-gui.exe";

        public int ConnectTimeoutSec { get; set; } = 20;
        public int CommandTimeoutSec { get; set; } = 10;

        /// <summary>In AutoRestart mode, connect the profile at startup when it is not running.</summary>
        public bool ConnectOnStart { get; set; } = true;

        /// <summary>Read the OpenVPN log written by the GUI and use it as tunnel evidence.</summary>
        public bool ReadOpenVpnLog { get; set; } = true;

        /// <summary>Optional directory overriding %USERPROFILE%\OpenVPN\log.</summary>
        public string LogDirectory { get; set; } = string.Empty;

        public void Normalize()
        {
            if (Backend == null) Backend = "openvpn-gui";
            if (ProfileName == null) ProfileName = string.Empty;
            if (ConfigPath == null) ConfigPath = string.Empty;
            if (OpenVpnGuiPath == null) OpenVpnGuiPath = string.Empty;
            if (LogDirectory == null) LogDirectory = string.Empty;
        }
    }

    public sealed class TargetSettings
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Method { get; set; } = "GET";
        public int TimeoutMs { get; set; } = 6000;
        public int RetryTimeoutMs { get; set; } = 4000;

        /// <summary>Accepted HTTP codes. Empty/null means "any 1xx-4xx, 5xx per Treat5xxAsFailure".</summary>
        public int[] ExpectedStatus { get; set; }

        public bool Treat5xxAsFailure { get; set; }

        /// <summary>Optional regex that the first N bytes of the body must match.</summary>
        public string BodyPattern { get; set; } = string.Empty;

        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void Normalize()
        {
            if (Name == null) Name = string.Empty;
            if (Url == null) Url = string.Empty;
            if (Method == null || Method.Trim().Length == 0) Method = "GET";
            if (BodyPattern == null) BodyPattern = string.Empty;
            if (Headers == null) Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class PolicySettings
    {
        /// <summary>"All" (default), "Majority", "Any" or "Count:N".</summary>
        public string Quorum { get; set; } = "All";

        public int FailureThreshold { get; set; } = 3;
        public int CorrelationWindowSec { get; set; } = 30;
        public int ProbeIntervalSec { get; set; } = 5;
        public int RetryDelayMs { get; set; } = 1000;
        public int RecoveryTimeoutSec { get; set; } = 60;
        public int RecoverySuccesses { get; set; } = 1;

        public int MinRestartGapSec { get; set; } = 30;
        public int MaxRestartGapSec { get; set; } = 300;
        public int MaxRestartsPerHour { get; set; } = 6;
        public int FailedRestartsBeforeCircuit { get; set; } = 3;
        public int CircuitCooldownSec { get; set; } = 900;
        public int JitterPercent { get; set; } = 20;
        public int ConnectCooldownSec { get; set; } = 30;

        /// <summary>
        /// When false (default) a met quorum is enough to restart: DPI blocking usually leaves
        /// the tunnel "up" while all sites are unreachable, so waiting for a tunnel failure
        /// would defeat the purpose. Set to true to restart only when OpenVPN itself reports
        /// a tunnel-level problem.
        /// </summary>
        public bool RequireTunnelEvidence { get; set; } = false;

        /// <summary>Opt-in: still restart when the tunnel looks healthy but all sites are down.</summary>
        public bool RestartOnServiceOutage { get; set; } = false;

        public bool HardRestartAfterFailedSoft { get; set; } = true;

        /// <summary>Seconds between repeated "still down" alerts for the same incident.</summary>
        public int AlertThrottleSec { get; set; } = 300;

        public void Normalize()
        {
            if (Quorum == null) Quorum = "All";
        }

        /// <summary>Returns N for "Count:N" or -1 when the value is not a count quorum.</summary>
        public int CountQuorum()
        {
            var q = (Quorum ?? string.Empty).Trim();
            if (q.StartsWith("Count:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(q.Substring(6).Trim(), out int n) && n > 0)
                return n;
            return -1;
        }
    }

    public sealed class ProbeSettings
    {
        public bool UseSystemProxy { get; set; } = false;
        public bool FollowRedirects { get; set; } = true;
        public int MaxRedirects { get; set; } = 5;
        public bool Tls12 { get; set; } = true;
        public string UserAgent { get; set; } = "RestartVPN/0.1 (+https://github.com/meferspb/restartvpn)";
        public bool Treat5xxAsFailure { get; set; } = true;
        public int MaxBodyBytes { get; set; } = 65536;

        public void Normalize()
        {
            if (UserAgent == null) UserAgent = string.Empty;
        }
    }

    public sealed class LoggingSettings
    {
        /// <summary>"Error", "Warn", "Info", "Debug".</summary>
        public string Level { get; set; } = "Info";
        public long MaxFileBytes { get; set; } = 2 * 1024 * 1024;
        public int KeepFiles { get; set; } = 5;
        public bool Jsonl { get; set; } = true;
        public string Directory { get; set; } = string.Empty;

        public void Normalize()
        {
            if (Level == null) Level = "Info";
            if (Directory == null) Directory = string.Empty;
        }
    }

    public sealed class NotificationSettings
    {
        public bool Balloon { get; set; } = true;
        public bool Sound { get; set; } = false;
        public bool OnlyOnAction { get; set; } = true;

        public void Normalize() { }
    }

    public sealed class UiSettings
    {
        public bool StartMinimized { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool Autostart { get; set; } = false;
        public bool ConfirmRestart { get; set; } = true;
        public bool MinimizeToTrayHintShown { get; set; } = false;
    }
}
