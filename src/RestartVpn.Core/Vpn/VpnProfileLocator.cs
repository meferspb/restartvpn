using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RestartVpn.Core.Vpn
{
    public sealed class VpnProfileInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    /// <summary>
    /// Finds .ovpn profiles the way OpenVPN GUI does, without modifying anything.
    /// </summary>
    public static class VpnProfileLocator
    {
        public static string ResolveConfigPath(string profileName, string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string p = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
                if (File.Exists(p)) return Path.GetFullPath(p);
            }

            if (string.IsNullOrWhiteSpace(profileName)) return string.Empty;

            foreach (string candidate in Candidates(profileName))
            {
                try { if (File.Exists(candidate)) return Path.GetFullPath(candidate); } catch { }
            }
            return string.Empty;
        }

        private static IEnumerable<string> Candidates(string profileName)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var roots = new List<string>();
            if (!string.IsNullOrEmpty(userProfile)) roots.Add(Path.Combine(userProfile, "OpenVPN", "config"));
            if (!string.IsNullOrEmpty(programFiles)) roots.Add(Path.Combine(programFiles, "OpenVPN", "config"));
            if (!string.IsNullOrEmpty(programFilesX86)) roots.Add(Path.Combine(programFilesX86, "OpenVPN", "config"));

            foreach (string root in roots)
            {
                yield return Path.Combine(root, profileName + ".ovpn");
                yield return Path.Combine(root, profileName, profileName + ".ovpn");
            }
        }

        public static List<VpnProfileInfo> DiscoverProfiles()
        {
            var found = new List<VpnProfileInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(userProfile)) return found;

            string root = Path.Combine(userProfile, "OpenVPN", "config");
            if (!Directory.Exists(root)) return found;

            try
            {
                foreach (string file in Directory.GetFiles(root, "*.ovpn", SearchOption.AllDirectories))
                {
                    // Keep the scan shallow: config root and one profile directory are enough.
                    string relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
                    if (relative.Count(c => c == Path.DirectorySeparatorChar) > 1) continue;

                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!seen.Add(name)) continue;
                    found.Add(new VpnProfileInfo { Name = name, Path = file });
                }
            }
            catch { }

            found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return found;
        }
    }
}
