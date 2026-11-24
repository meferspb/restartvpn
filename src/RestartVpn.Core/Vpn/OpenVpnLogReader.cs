using System;
using System.IO;
using System.Text;

namespace RestartVpn.Core.Vpn
{
    /// <summary>
    /// Read-only tail of the log file OpenVPN GUI writes for a profile. Gives a much better
    /// tunnel signal than "the process exists", without touching the profile or the tunnel.
    /// The tail is cached by (path, mtime, length): the monitor polls status every few
    /// seconds and must not re-read a large file when OpenVPN has written nothing.
    /// </summary>
    public sealed class OpenVpnLogReader
    {
        private const int TailBytes = 128 * 1024;
        private readonly object _gate = new object();
        private string _cachePath;
        private DateTime _cacheWriteUtc;
        private long _cacheLength = -1;
        private Evidence _cache;

        public sealed class Evidence
        {
            public VpnLogEvidence Kind { get; set; } = VpnLogEvidence.None;
            public string Detail { get; set; } = string.Empty;
            public DateTime LastWriteUtc { get; set; } = DateTime.MinValue;
        }

        public string ResolveLogPath(string profileName, string configuredDirectory)
        {
            if (string.IsNullOrWhiteSpace(profileName)) return string.Empty;

            string dir = configuredDirectory;
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OpenVPN", "log");
            else
                dir = Environment.ExpandEnvironmentVariables(dir);

            string path = Path.Combine(dir, profileName + ".log");
            return File.Exists(path) ? path : string.Empty;
        }

        public Evidence Read(string profileName, string configuredDirectory)
        {
            lock (_gate)
            {
                var result = new Evidence();
                string path = ResolveLogPath(profileName, configuredDirectory);
                if (string.IsNullOrEmpty(path)) return result;

                long length;
                DateTime writeUtc;
                try
                {
                    var info = new FileInfo(path);
                    length = info.Length;
                    writeUtc = info.LastWriteTimeUtc;
                    if (_cache != null && string.Equals(_cachePath, path, StringComparison.OrdinalIgnoreCase) &&
                        _cacheLength == length && _cacheWriteUtc == writeUtc)
                    {
                        return _cache;
                    }
                }
                catch
                {
                    return result;
                }

                string text;
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        long start = Math.Max(0, fs.Length - TailBytes);
                        fs.Seek(start, SeekOrigin.Begin);
                        using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                            text = reader.ReadToEnd();
                    }
                }
                catch
                {
                    return result;
                }

                result.LastWriteUtc = writeUtc;
                string[] lines = text.Replace("\r", string.Empty).Split('\n');
                foreach (string line in lines)
                {
                    var kind = Classify(line);
                    if (kind == VpnLogEvidence.None) continue;

                    // Later lines win: the tail reflects the current state, not history.
                    result.Kind = kind;
                    result.Detail = line.Trim();
                    if (result.Detail.Length > 300) result.Detail = result.Detail.Substring(0, 300);
                }

                _cachePath = path;
                _cacheWriteUtc = writeUtc;
                _cacheLength = length;
                _cache = result;
                return result;
            }
        }

        private static VpnLogEvidence Classify(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return VpnLogEvidence.None;

            string l = line.ToLowerInvariant();
            if (l.Contains("initialization sequence completed")) return VpnLogEvidence.Connected;
            if (l.Contains("auth_failed") || l.Contains("auth failure") || l.Contains("authentication failed")) return VpnLogEvidence.FatalAuth;
            if (l.Contains("cannot open tun") || l.Contains("cannot open tap") ||
                l.Contains("options error") || l.Contains("there is a problem with the configuration")) return VpnLogEvidence.FatalConfig;
            if (l.Contains("exiting due to fatal error") || l.Contains("fatal error")) return VpnLogEvidence.FatalOther;
            if (l.Contains("restart pause") || l.Contains("connection reset") || l.Contains("tls error") ||
                l.Contains("reconnecting")) return VpnLogEvidence.Restarting;
            if (l.Contains("sigterm") || l.Contains("process exiting") || l.Contains("tunnel closed")) return VpnLogEvidence.Stopped;
            return VpnLogEvidence.None;
        }
    }
}
