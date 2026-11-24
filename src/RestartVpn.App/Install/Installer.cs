using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using RestartVpn.Core.Settings;

namespace RestartVpn.App.Install
{
    /// <summary>
    /// Per-user installation. No administrator rights, no services, no drivers.
    /// The downloadable exe is the installer: it copies itself and creates shortcuts.
    /// </summary>
    public static class Installer
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RestartVPN";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "RestartVPN";

        public static bool IsInstalled(AppPaths paths) => paths.IsInstalled;

        public static string Install(AppPaths paths, bool autostart, bool desktopShortcut, bool silent)
        {
            try
            {
                Directory.CreateDirectory(paths.InstallDir);

                string target = paths.InstalledExePath;
                if (!string.Equals(Path.GetFullPath(paths.ExePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    string old = target + ".old";
                    try { if (File.Exists(old)) File.Delete(old); } catch { }

                    try
                    {
                        File.Copy(paths.ExePath, target, true);
                    }
                    catch (IOException)
                    {
                        // The installed copy is probably running: rename it and retry.
                        try
                        {
                            if (File.Exists(target)) File.Move(target, old);
                            File.Copy(paths.ExePath, target, true);
                        }
                        catch (Exception ex)
                        {
                            return "Cannot replace " + target + ": " + ex.Message + " Close the running copy and retry.";
                        }
                    }
                }

                CreateShortcuts(paths.InstallDir, target, desktopShortcut);
                WriteUninstallEntry(paths, target);
                SetAutostart(autostart, target);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static void Uninstall(AppPaths paths, bool deleteUserData, bool cleanupProcess = true)
        {
            try { SetAutostart(false, paths.InstalledExePath); } catch { }
            try { RemoveShortcuts(); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

            if (deleteUserData)
            {
                try { if (Directory.Exists(paths.DataDir)) Directory.Delete(paths.DataDir, true); } catch { }
                try { if (Directory.Exists(paths.LogDir)) Directory.Delete(paths.LogDir, true); } catch { }
            }

            if (!cleanupProcess) return;

            // The running exe cannot delete itself. Copy a tiny uninstaller to temp and let it
            // wait for this process, delete the install directory and remove itself.
            try
            {
                string self = paths.ExePath;
                string temp = Path.Combine(Path.GetTempPath(), "RestartVPN-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(self, temp, true);

                var psi = new ProcessStartInfo(temp)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Arguments = "--uninstall-run \"" + paths.InstallDir + "\""
                };
                Process.Start(psi);
            }
            catch { }
        }

        public static void SetAutostart(bool enabled, string exePath)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) return;
                if (enabled && !string.IsNullOrEmpty(exePath))
                    key.SetValue(RunValue, "\"" + exePath + "\" --minimized", RegistryValueKind.String);
                else
                    key.DeleteValue(RunValue, false);
            }
        }

        public static bool IsAutostartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return key != null && key.GetValue(RunValue) != null;
            }
            catch { return false; }
        }

        private static void CreateShortcuts(string installDir, string exePath, bool desktop)
        {
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
            Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(startMenu, "RestartVPN.lnk"), exePath, installDir);

            if (!desktop) return;
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrEmpty(desktopDir))
                CreateShortcut(Path.Combine(desktopDir, "RestartVPN.lnk"), exePath, installDir);
        }

        private static void CreateShortcut(string linkPath, string targetPath, string workingDir)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });

                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "RestartVPN" });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath + ",0" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { }
            finally
            {
                if (shortcut != null) { try { Marshal.ReleaseComObject(shortcut); } catch { } }
                if (shell != null) { try { Marshal.ReleaseComObject(shell); } catch { } }
            }
        }

        private static void RemoveShortcuts()
        {
            try
            {
                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs\RestartVPN.lnk");
                if (File.Exists(startMenu)) File.Delete(startMenu);

                string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RestartVPN.lnk");
                if (File.Exists(desktop)) File.Delete(desktop);
            }
            catch { }
        }

        private static void WriteUninstallEntry(AppPaths paths, string exePath)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                if (key == null) return;
                key.SetValue("DisplayName", "RestartVPN", RegistryValueKind.String);
                key.SetValue("DisplayVersion", AppInfo.Version, RegistryValueKind.String);
                key.SetValue("Publisher", "RestartVPN contributors", RegistryValueKind.String);
                key.SetValue("InstallLocation", paths.InstallDir, RegistryValueKind.String);
                key.SetValue("DisplayIcon", "\"" + exePath + "\",0", RegistryValueKind.String);
                key.SetValue("UninstallString", "\"" + exePath + "\" --uninstall", RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try
                {
                    var info = new FileInfo(exePath);
                    key.SetValue("EstimatedSize", (int)(info.Length / 1024), RegistryValueKind.DWord);
                }
                catch { }
            }
        }

        /// <summary>Runs in the temporary uninstaller process: waits, deletes the install dir, self-deletes.</summary>
        public static int RunCleanup(string installDir)
        {
            try
            {
                System.Threading.Thread.Sleep(1500);
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(installDir)) Directory.Delete(installDir, true);
                        break;
                    }
                    catch { System.Threading.Thread.Sleep(500); }
                }
            }
            catch { }

            try
            {
                string self = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + self + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch { }

            return 0;
        }
    }
}
