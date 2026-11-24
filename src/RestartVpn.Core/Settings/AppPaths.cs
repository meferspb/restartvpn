using System;
using System.IO;

namespace RestartVpn.Core.Settings
{
    /// <summary>
    /// All filesystem locations used by the application. Kept in one place so that
    /// portable mode and tests can use a different root without touching the code.
    /// </summary>
    public sealed class AppPaths
    {
        public string ExePath { get; private set; }
        public string ExeDir { get; private set; }
        public bool Portable { get; private set; }
        public string InstallDir { get; private set; }
        public string DataDir { get; private set; }
        public string LogDir { get; private set; }
        public string SettingsPath { get; private set; }
        public string StateDir { get; private set; }

        private AppPaths() { }

        public static AppPaths ForExe(string exePath, bool portable)
        {
            if (string.IsNullOrWhiteSpace(exePath)) throw new ArgumentException("exePath is required", nameof(exePath));

            string exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var p = new AppPaths
            {
                ExePath = Path.GetFullPath(exePath),
                ExeDir = exeDir,
                Portable = portable,
                InstallDir = Path.Combine(localAppData, "Programs", "RestartVPN"),
            };

            if (portable)
            {
                p.DataDir = Path.Combine(exeDir, "data");
                p.LogDir = Path.Combine(p.DataDir, "logs");
            }
            else
            {
                p.DataDir = Path.Combine(roamingAppData, "RestartVPN");
                p.LogDir = Path.Combine(localAppData, "RestartVPN", "logs");
            }

            p.SettingsPath = Path.Combine(p.DataDir, "settings.json");
            p.StateDir = Path.Combine(p.DataDir, "state");
            return p;
        }

        public static AppPaths ForCurrentProcess(bool portable)
        {
            string exe = null;
            try
            {
                var asm = System.Reflection.Assembly.GetEntryAssembly();
                if (asm != null && !string.IsNullOrEmpty(asm.Location)) exe = asm.Location;
            }
            catch { }

            if (string.IsNullOrEmpty(exe))
            {
                try { exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; }
                catch { }
            }

            if (string.IsNullOrEmpty(exe))
                exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RestartVPN.exe");

            return ForExe(exe, portable);
        }

        /// <summary>True when the running executable is the copy inside the per-user install directory.</summary>
        public bool RunningFromInstallDir
        {
            get
            {
                try
                {
                    return !Portable && string.Equals(Path.GetDirectoryName(ExePath), InstallDir, StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
        }

        /// <summary>Explicit data root; used by tests and tools that keep everything in one folder.</summary>
        public static AppPaths ForDataDirectory(string dataDirectory, string exePath = null)
        {
            string data = Path.GetFullPath(dataDirectory);
            string exe = string.IsNullOrEmpty(exePath)
                ? Path.Combine(data, "RestartVPN.exe")
                : Path.GetFullPath(exePath);

            var p = new AppPaths
            {
                ExePath = exe,
                ExeDir = Path.GetDirectoryName(exe),
                Portable = true,
                InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RestartVPN"),
                DataDir = data,
                LogDir = Path.Combine(data, "logs"),
                StateDir = Path.Combine(data, "state")
            };
            p.SettingsPath = Path.Combine(data, "settings.json");
            return p;
        }

        /// <summary>Uses a settings file from an arbitrary location (--config).</summary>
        public void OverrideSettingsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            SettingsPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            string dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) DataDir = dir;
            StateDir = Path.Combine(DataDir, "state");
        }

        public string InstalledExePath => Path.Combine(InstallDir, "RestartVPN.exe");

        public bool IsInstalled => File.Exists(InstalledExePath);

        public void EnsureDataDirectories()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LogDir);
            Directory.CreateDirectory(StateDir);
        }
    }
}
