using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RestartVpn.Core.Vpn
{
    /// <summary>
    /// Parses OpenVPN command lines. This is the safety core of the app: a profile may only be
    /// touched when the process was started with its own .ovpn file. Matching by image name or
    /// by substring is forbidden — it would let the app kill unrelated VPN connections.
    /// </summary>
    public static class OpenVpnCommandLine
    {
        public static List<string> Tokenize(string commandLine)
        {
            var args = new List<string>();
            if (string.IsNullOrEmpty(commandLine)) return args;

            var current = new StringBuilder();
            bool inQuotes = false;
            bool hasToken = false;
            int i = 0;

            while (i < commandLine.Length)
            {
                char c = commandLine[i];

                if (c == '\\')
                {
                    int slashes = 0;
                    while (i < commandLine.Length && commandLine[i] == '\\') { slashes++; i++; }

                    if (i < commandLine.Length && commandLine[i] == '"')
                    {
                        current.Append('\\', slashes / 2);
                        if (slashes % 2 == 1) { current.Append('"'); i++; }
                        hasToken = true;
                    }
                    else
                    {
                        current.Append('\\', slashes);
                        hasToken = true;
                    }
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    hasToken = true;
                    i++;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (hasToken)
                    {
                        args.Add(current.ToString());
                        current.Length = 0;
                        hasToken = false;
                    }
                    i++;
                    continue;
                }

                current.Append(c);
                hasToken = true;
                i++;
            }

            if (hasToken) args.Add(current.ToString());
            return args;
        }

        public static List<string> ExtractConfigPaths(IEnumerable<string> tokens)
        {
            var paths = new List<string>();
            if (tokens == null) return paths;

            var list = new List<string>(tokens);
            for (int i = 0; i < list.Count; i++)
            {
                string token = list[i];
                if (string.Equals(token, "--config", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < list.Count) paths.Add(list[i + 1]);
                }
                else if (token.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(token.Substring("--config=".Length));
                }
            }
            return paths;
        }

        /// <summary>
        /// True when the process was started for exactly this profile. The full path is compared
        /// when known; otherwise the config file name must equal "&lt;profileName&gt;.ovpn".
        /// "my-profile-backup.ovpn" never matches profile "my-profile".
        /// </summary>
        public static bool MatchesProfile(string commandLine, string profileName, string configPath)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return false;
            if (string.IsNullOrWhiteSpace(profileName) && string.IsNullOrWhiteSpace(configPath)) return false;

            string expectedFile = string.IsNullOrWhiteSpace(profileName) ? null : profileName.Trim() + ".ovpn";

            foreach (string raw in ExtractConfigPaths(Tokenize(commandLine)))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                if (!string.IsNullOrWhiteSpace(configPath) && PathsEqual(raw, configPath)) return true;

                if (expectedFile != null &&
                    string.Equals(Path.GetFileName(raw.Trim()), expectedFile, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                string na = Path.GetFullPath(a.Trim().Trim('"')).TrimEnd('\\', '/');
                string nb = Path.GetFullPath(b.Trim().Trim('"')).TrimEnd('\\', '/');
                return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
