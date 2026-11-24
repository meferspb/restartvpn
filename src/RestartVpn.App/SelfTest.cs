using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Net;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;

namespace RestartVpn.App
{
    /// <summary>
    /// Read-only diagnostics. It never sends connect/disconnect commands: safe to run while
    /// another VPN connection is in use.
    /// </summary>
    public static class SelfTest
    {
        public static int Run(AppPaths paths, SettingsLoadResult load, TextWriter output)
        {
            var s = load.Settings;
            int problems = 0;

            output.WriteLine("RestartVPN " + AppInfo.Version + " — self-test");
            output.WriteLine("OS      : " + Environment.OSVersion.VersionString);
            output.WriteLine("CLR     : " + Environment.Version);
            output.WriteLine("Exe     : " + paths.ExePath);
            output.WriteLine("Data    : " + paths.DataDir);
            output.WriteLine("Settings: " + load.Path + (load.Existed ? "" : " (not created yet)"));
            output.WriteLine();

            foreach (var issue in load.Issues)
            {
                output.WriteLine("[" + issue.Severity + "] " + issue);
                if (issue.Severity == IssueSeverity.Error) problems++;
            }
            if (load.Issues.Count == 0) output.WriteLine("Settings: OK");
            output.WriteLine();

            AppLog log = null;
            try
            {
                string logDir = string.IsNullOrWhiteSpace(s.Logging.Directory)
                    ? paths.LogDir
                    : Environment.ExpandEnvironmentVariables(s.Logging.Directory);
                log = new AppLog(logDir, LogLevel.Warn, s.Logging.MaxFileBytes, s.Logging.KeepFiles, false, s.Vpn.ProfileName);
                var backend = new OpenVpnGuiBackend(s.Vpn, log);

                output.WriteLine("Mode         : " + s.Mode);
                output.WriteLine("Profile name : " + (string.IsNullOrWhiteSpace(s.Vpn.ProfileName) ? "<not set>" : s.Vpn.ProfileName));
                output.WriteLine("Profile file : " + (string.IsNullOrEmpty(backend.ProfilePath) ? "<not found>" : backend.ProfilePath));
                output.WriteLine("OpenVPN GUI  : " + s.Vpn.OpenVpnGuiPath + (backend.GuiExists ? " (found)" : " (MISSING)"));

                var status = backend.GetStatus();
                output.WriteLine("VPN state    : " + status.State + " — " + status.Detail);
                if (status.OtherProfileProcessCount > 0)
                    output.WriteLine("Other VPN    : " + status.OtherProfileProcessCount + " openvpn.exe process(es) of other profiles are running (not touched).");
                output.WriteLine();

                using (var probe = new HttpProbe(s.Probe))
                {
                    var enabled = s.Targets.Where(t => t.Enabled).ToList();
                    if (enabled.Count == 0) output.WriteLine("No enabled targets.");
                    foreach (var target in enabled)
                    {
                        var r = probe.ProbeAsync(target, CancellationToken.None).GetAwaiter().GetResult();
                        output.WriteLine((r.Success ? "OK   " : "FAIL ") + (string.IsNullOrWhiteSpace(target.Name) ? target.Url : target.Name) +
                                         "  " + r.Detail + "  " + r.LatencyMs + " ms");
                        if (!r.Success) problems++;
                    }
                }

                if (string.IsNullOrWhiteSpace(s.Vpn.ProfileName) && s.IsAutoRestart)
                {
                    output.WriteLine();
                    output.WriteLine("AutoRestart requires a VPN profile.");
                    problems++;
                }
            }
            catch (Exception ex)
            {
                output.WriteLine("Self-test failed: " + ex.Message);
                problems++;
            }
            finally
            {
                try { log?.Dispose(); } catch { }
            }

            output.WriteLine();
            output.WriteLine(problems == 0 ? "Result: OK" : "Result: " + problems + " problem(s)");
            return problems == 0 ? 0 : 1;
        }
    }
}
