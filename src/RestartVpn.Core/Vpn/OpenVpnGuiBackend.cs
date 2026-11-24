using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Settings;

namespace RestartVpn.Core.Vpn
{
    /// <summary>
    /// Controls exactly one OpenVPN GUI profile. It never enumerates-and-kills openvpn.exe by
    /// image name, never edits .ovpn files, never touches routes, DNS, proxy or firewall.
    /// </summary>
    public sealed class OpenVpnGuiBackend : IVpnBackend
    {
        private readonly VpnSettings _settings;
        private readonly AppLog _log;
        private readonly OpenVpnLogReader _logReader = new OpenVpnLogReader();
        private readonly object _cacheGate = new object();

        private VpnStatus _cachedStatus;
        private DateTime _cachedAtUtc = DateTime.MinValue;
        private string _resolvedProfilePath;
        private string _lastError = string.Empty;

        private static readonly TimeSpan CacheTtl = TimeSpan.FromMilliseconds(800);

        public OpenVpnGuiBackend(VpnSettings settings, AppLog log)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = log;
        }

        public string Name => "openvpn-gui";

        public string ProfileName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_settings.ProfileName)) return _settings.ProfileName.Trim();
                if (!string.IsNullOrWhiteSpace(_settings.ConfigPath))
                {
                    try { return Path.GetFileNameWithoutExtension(_settings.ConfigPath.Trim()); }
                    catch { }
                }
                return string.Empty;
            }
        }

        public string ProfilePath
        {
            get
            {
                if (_resolvedProfilePath != null) return _resolvedProfilePath;
                _resolvedProfilePath = VpnProfileLocator.ResolveConfigPath(ProfileName, _settings.ConfigPath) ?? string.Empty;
                return _resolvedProfilePath;
            }
        }

        public string LastError => _lastError;

        public bool GuiExists
        {
            get { try { return File.Exists(GuiPath); } catch { return false; } }
        }

        public bool ProfileExists
        {
            get { try { return !string.IsNullOrEmpty(ProfilePath) && File.Exists(ProfilePath); } catch { return false; } }
        }

        private string GuiPath
        {
            get
            {
                string configured = _settings.OpenVpnGuiPath;
                if (!string.IsNullOrWhiteSpace(configured))
                    return Environment.ExpandEnvironmentVariables(configured.Trim());
                return @"C:\Program Files\OpenVPN\bin\openvpn-gui.exe";
            }
        }

        public VpnStatus GetStatus() => GetStatus(false);

        public VpnStatus GetStatus(bool forceRefresh)
        {
            lock (_cacheGate)
            {
                if (!forceRefresh && _cachedStatus != null &&
                    (DateTime.UtcNow - _cachedAtUtc) < CacheTtl)
                    return _cachedStatus;
            }

            var status = QueryStatus();
            lock (_cacheGate)
            {
                _cachedStatus = status;
                _cachedAtUtc = DateTime.UtcNow;
            }
            return status;
        }

        private VpnStatus QueryStatus()
        {
            var status = new VpnStatus
            {
                ProfilePath = ProfilePath,
                SampledAtUtc = DateTimeOffset.UtcNow
            };

            int anyCount = CountProcesses("openvpn");
            status.AnyOpenVpnProcess = anyCount > 0;
            status.OtherProfileProcessCount = anyCount;

            if (anyCount == 0)
            {
                status.State = VpnState.Disconnected;
                status.Detail = "openvpn.exe is not running";
                return status;
            }

            List<ProcessInfo> processes;
            string wmiError;
            if (!TryQueryProcesses(out processes, out wmiError))
            {
                status.State = VpnState.Unknown;
                status.Detail = "cannot read process command lines: " + wmiError;
                status.OtherProfileProcessCount = anyCount;
                return status;
            }

            var matched = processes.Where(p => Matches(p.CommandLine)).ToList();
            status.OtherProfileProcessCount = Math.Max(0, processes.Count - matched.Count);

            if (matched.Count == 0)
            {
                status.State = VpnState.Disconnected;
                status.Detail = string.IsNullOrEmpty(ProfilePath)
                    ? "no process matches profile '" + ProfileName + "'"
                    : "no process uses " + ProfilePath;
                return status;
            }

            status.ProfileProcessPresent = true;
            status.State = VpnState.Connected;
            status.Detail = "profile process is running (pid " + string.Join(", ", matched.Select(m => m.Pid)) + ")";

            if (_settings.ReadOpenVpnLog && !string.IsNullOrWhiteSpace(ProfileName))
            {
                var evidence = _logReader.Read(ProfileName, _settings.LogDirectory);
                bool fresh = evidence.Kind != VpnLogEvidence.None && IsFresh(evidence.LastWriteUtc, matched);
                status.EvidenceFresh = fresh;
                if (fresh)
                {
                    status.Evidence = evidence.Kind;
                    status.EvidenceDetail = evidence.Detail;
                    switch (evidence.Kind)
                    {
                        case VpnLogEvidence.FatalAuth:
                        case VpnLogEvidence.FatalConfig:
                        case VpnLogEvidence.FatalOther:
                            status.State = VpnState.Error;
                            status.Detail = evidence.Detail;
                            break;
                        case VpnLogEvidence.Stopped:
                        case VpnLogEvidence.Restarting:
                            status.State = VpnState.Connecting;
                            status.Detail = evidence.Detail;
                            break;
                        case VpnLogEvidence.Connected:
                            status.State = VpnState.Connected;
                            status.Detail = "tunnel is up";
                            break;
                    }
                }
                else if (evidence.Kind != VpnLogEvidence.None)
                {
                    status.Detail = "profile process is running; the OpenVPN log is stale, so tunnel evidence is ignored";
                }
            }

            return status;
        }

        private static bool IsFresh(DateTime logWriteUtc, List<ProcessInfo> matched)
        {
            if (logWriteUtc == DateTime.MinValue) return false;
            DateTime started = DateTime.MaxValue;
            foreach (var p in matched)
                if (p.StartedUtc.HasValue && p.StartedUtc.Value < started) started = p.StartedUtc.Value;

            if (started == DateTime.MaxValue) return (DateTime.UtcNow - logWriteUtc).TotalHours < 12;
            return logWriteUtc >= started.AddSeconds(-30);
        }

        private bool Matches(string commandLine) =>
            OpenVpnCommandLine.MatchesProfile(commandLine, ProfileName, ProfilePath);

        public VpnCommandResult Connect()
        {
            if (string.IsNullOrWhiteSpace(ProfileName))
                return VpnCommandResult.Fail("VPN profile name is not configured.");

            if (!File.Exists(GuiPath))
                return VpnCommandResult.Fail("openvpn-gui.exe not found at " + GuiPath);

            var status = GetStatus(forceRefresh: true);
            if (status.ProfileProcessPresent)
                return VpnCommandResult.Ok("profile is already connected.");

            _log.Info("Connecting OpenVPN profile '" + ProfileName + "' via GUI.");
            var result = StartOrCommand(new[] { "--command", "connect", ProfileName },
                                        new[] { "--connect", ProfileName });

            if (!result.Success)
                return result;

            if (!WaitForProfileProcess(_settings.ConnectTimeoutSec, true))
            {
                _log.Warn("openvpn.exe for profile '" + ProfileName + "' did not appear within " +
                          _settings.ConnectTimeoutSec + " s.");
                return VpnCommandResult.Fail("connect command sent, but no matching openvpn.exe appeared.");
            }

            _log.Info("Profile '" + ProfileName + "' process is running.");
            return VpnCommandResult.Ok("connected.");
        }

        public VpnCommandResult Disconnect()
        {
            if (string.IsNullOrWhiteSpace(ProfileName))
                return VpnCommandResult.Fail("VPN profile name is not configured.");

            var status = GetStatus(forceRefresh: true);
            if (!status.ProfileProcessPresent)
                return VpnCommandResult.Ok("profile is not connected.");

            if (!IsGuiRunning())
                return VpnCommandResult.Fail("OpenVPN GUI is not running; refusing to disconnect a tunnel it does not own.");

            _log.Info("Disconnecting OpenVPN profile '" + ProfileName + "'.");
            var result = RunGuiCommand(new[] { "--command", "disconnect", ProfileName });
            if (!result.Success) return result;

            if (!WaitForProfileProcess(_settings.CommandTimeoutSec, false))
            {
                _log.Warn("Profile process did not exit within " + _settings.CommandTimeoutSec + " s after disconnect.");
                return VpnCommandResult.Fail("disconnect sent, but the profile process is still running.");
            }

            return VpnCommandResult.Ok("disconnected.");
        }

        public VpnCommandResult HardRestart()
        {
            _log.Info("Hard restart requested for profile '" + ProfileName + "'.");

            List<ProcessInfo> all;
            string wmiError;
            if (!TryQueryProcesses(out all, out wmiError))
            {
                // Safety rule: without command lines we cannot prove a process is ours.
                return VpnCommandResult.Fail("refusing to kill processes: cannot read command lines (" + wmiError + ").");
            }

            var matched = all.Where(p => Matches(p.CommandLine)).ToList();
            foreach (var proc in matched)
            {
                try
                {
                    using (var p = Process.GetProcessById(proc.Pid))
                    {
                        _log.Warn("Killing openvpn.exe pid " + proc.Pid + " (" + Path.GetFileName(SafePath(proc.CommandLine)) + ") only.");
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Could not stop pid " + proc.Pid + ": " + ex.Message);
                    try
                    {
                        using (var kill = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + proc.Pid + " /F")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }))
                        {
                            kill?.WaitForExit(5000);
                        }
                    }
                    catch { }
                }
            }

            if (matched.Count > 0)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    var now = GetStatus(forceRefresh: true);
                    if (!now.ProfileProcessPresent) break;
                    Thread.Sleep(300);
                }
            }

            return Connect();
        }

        private VpnCommandResult StartOrCommand(string[] commandArgs, string[] startArgs)
        {
            if (IsGuiRunning())
                return RunGuiCommand(commandArgs);

            _log.Info("OpenVPN GUI is not running; starting it with --connect.");
            try
            {
                var psi = new ProcessStartInfo(GuiPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal,
                    Arguments = BuildArguments(startArgs)
                };
                var p = Process.Start(psi);
                if (p == null) return VpnCommandResult.Fail("could not start openvpn-gui.exe.");
                return VpnCommandResult.Ok("openvpn-gui.exe started.");
            }
            catch (Exception ex)
            {
                return VpnCommandResult.Fail("could not start openvpn-gui.exe: " + ex.Message);
            }
        }

        private VpnCommandResult RunGuiCommand(string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(GuiPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = BuildArguments(args)
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null) return VpnCommandResult.Fail("could not start openvpn-gui.exe.");

                    int timeoutMs = Math.Max(3000, _settings.CommandTimeoutSec * 1000);
                    if (!p.WaitForExit(timeoutMs))
                    {
                        // Do not kill it: when the GUI was not running this process may BE the GUI.
                        _log.Warn("openvpn-gui.exe command did not exit within " + _settings.CommandTimeoutSec +
                                  " s; leaving it running.");
                        return VpnCommandResult.Fail("openvpn-gui.exe did not return in time.");
                    }

                    if (p.ExitCode != 0)
                        return VpnCommandResult.Fail("openvpn-gui.exe exited with code " + p.ExitCode + ".");

                    return VpnCommandResult.Ok("command accepted.");
                }
            }
            catch (Exception ex)
            {
                return VpnCommandResult.Fail("openvpn-gui.exe failed: " + ex.Message);
            }
        }

        private static string BuildArguments(IEnumerable<string> args)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (a.IndexOf(' ') >= 0 || a.IndexOf('"') >= 0)
                    sb.Append('"').Append(a.Replace("\"", "\\\"")).Append('"');
                else
                    sb.Append(a);
            }
            return sb.ToString();
        }

        private bool WaitForProfileProcess(int timeoutSec, bool expected)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(3, timeoutSec));
            while (DateTime.UtcNow < deadline)
            {
                var status = GetStatus(forceRefresh: true);
                if (status.ProfileProcessPresent == expected) return true;
                Thread.Sleep(700);
            }
            return GetStatus(forceRefresh: true).ProfileProcessPresent == expected;
        }

        private bool IsGuiRunning()
        {
            try
            {
                var procs = Process.GetProcessesByName("openvpn-gui");
                try { return procs.Length > 0; }
                finally { foreach (var p in procs) p.Dispose(); }
            }
            catch { return false; }
        }

        private static int CountProcesses(string name)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                try { return procs.Length; }
                finally { foreach (var p in procs) p.Dispose(); }
            }
            catch { return 0; }
        }

        private sealed class ProcessInfo
        {
            public int Pid;
            public string CommandLine;
            public DateTime? StartedUtc;
        }

        private bool TryQueryProcesses(out List<ProcessInfo> processes, out string error)
        {
            processes = new List<ProcessInfo>();
            error = string.Empty;

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine, CreationDate FROM Win32_Process WHERE Name='openvpn.exe'"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementBaseObject item in results)
                    {
                        try
                        {
                            int pid = Convert.ToInt32(item["ProcessId"], CultureInfo.InvariantCulture);
                            string commandLine = item["CommandLine"] as string;
                            DateTime? started = null;
                            try
                            {
                                var created = item["CreationDate"] as string;
                                if (!string.IsNullOrEmpty(created))
                                    started = ManagementDateTimeConverter.ToDateTime(created).ToUniversalTime();
                            }
                            catch { }

                            processes.Add(new ProcessInfo { Pid = pid, CommandLine = commandLine, StartedUtc = started });
                        }
                        catch { }
                        finally { item.Dispose(); }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _lastError = error;
                return false;
            }
        }

        private static string SafePath(string commandLine)
        {
            try
            {
                var paths = OpenVpnCommandLine.ExtractConfigPaths(OpenVpnCommandLine.Tokenize(commandLine));
                return paths.Count > 0 ? paths[0] : "unknown";
            }
            catch { return "unknown"; }
        }
    }
}
