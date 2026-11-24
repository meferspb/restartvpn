using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RestartVpn.App.Cli;
using RestartVpn.App.Install;
using RestartVpn.App.Ui;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;

namespace RestartVpn.App
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var options = CliOptions.Parse(args);

            if (options.UninstallRun)
                return Installer.RunCleanup(options.UninstallDirectory);

            if (options.Help) return ShowText(CliOptions.HelpText, 0);
            if (options.Version) return ShowText(AppInfo.Name + " " + AppInfo.Version, 0);
            if (options.Errors.Count > 0)
                return ShowText("Ошибка в аргументах:\n" + string.Join(Environment.NewLine, options.Errors) + "\n\n" + CliOptions.HelpText, 1);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            InstallGlobalErrorHandlers();

            // A settings.json inside <exe>\data means the user chose portable mode before.
            var portableProbe = AppPaths.ForCurrentProcess(true);
            bool portableDataExists = File.Exists(Path.Combine(portableProbe.DataDir, "settings.json"));
            bool portableRequested = options.Portable || portableDataExists;

            var paths = AppPaths.ForCurrentProcess(portableRequested);
            if (!string.IsNullOrWhiteSpace(options.ConfigPath))
                paths.OverrideSettingsPath(options.ConfigPath);

            var store = new SettingsStore(paths);
            var load = store.Load();

            if (options.SelfTest || options.Once)
                return RunSelfTest(paths, load);

            if (options.Uninstall)
                return RunUninstall(paths, store, load, options);

            if (options.Install)
                return RunInstall(paths, options);

            bool portable = portableRequested;

            // First launch of the downloaded exe: offer a per-user install.
            if (!portable && !options.Headless && !paths.RunningFromInstallDir && !paths.IsInstalled &&
                !options.NoInstallPrompt && !load.Existed && string.IsNullOrWhiteSpace(options.ConfigPath))
            {
                bool autostart;
                bool desktop;
                var choice = InstallForm.Ask(paths, null, out autostart, out desktop);
                if (choice == InstallChoice.Cancel) return 0;

                if (choice == InstallChoice.Install)
                {
                    string error = Installer.Install(paths, autostart, desktop, false);
                    if (error != null)
                    {
                        MessageBox.Show("Не удалось установить: " + error, "RestartVPN", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 1;
                    }
                    try { Process.Start(new ProcessStartInfo(paths.InstalledExePath) { UseShellExecute = true }); }
                    catch { }
                    return 0;
                }

                portable = true;
                paths = AppPaths.ForCurrentProcess(true);
                store = new SettingsStore(paths);
                load = store.Load();
            }

            if (options.Headless)
                return RunHeadless(paths, store, load, options);

            return RunGui(paths, store, load, options);
        }

        private static int RunSelfTest(AppPaths paths, SettingsLoadResult load)
        {
            var writer = new StringWriter();
            int code = SelfTest.Run(paths, load, writer);
            string text = writer.ToString();
            ConsoleHost.Ensure(true);
            if (ConsoleHost.HasConsole) Console.Write(text);
            else MessageBox.Show(text, "RestartVPN — self-test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return code;
        }

        private static int RunInstall(AppPaths paths, CliOptions options)
        {
            string error = Installer.Install(paths, autostart: false, desktopShortcut: false, silent: options.Silent);
            if (error != null)
            {
                if (!options.Silent)
                    MessageBox.Show("Не удалось установить: " + error, "RestartVPN", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            if (!options.Silent)
                MessageBox.Show("RestartVPN установлен в:\n" + paths.InstallDir, "RestartVPN",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        private static int RunUninstall(AppPaths paths, SettingsStore store, SettingsLoadResult load, CliOptions options)
        {
            if (AnotherInstanceRunning())
            {
                if (!options.Silent)
                    MessageBox.Show("Закройте RestartVPN и повторите удаление.",
                        "RestartVPN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            bool deleteData = false;
            if (!options.Silent)
            {
                var answer = MessageBox.Show(
                    "Удалить RestartVPN для текущего пользователя?\n\n" +
                    "Ярлыки, автозапуск и запись в списке программ будут удалены.\n" +
                    "Настройки и журналы сохранятся.",
                    "Удаление RestartVPN", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return 0;
            }

            // Do not show a modal message after scheduling the cleanup: the temporary
            // uninstaller waits only 1.5 s and would fail to delete a still-running exe.
            Installer.Uninstall(paths, deleteData, true);
            return 0;
        }

        private static int RunHeadless(AppPaths paths, SettingsStore store, SettingsLoadResult load, CliOptions options)
        {
            ConsoleHost.Ensure(true);
            if (!ConsoleHost.HasConsole)
                return 1;

            Console.WriteLine(AppInfo.Name + " " + AppInfo.Version + " — headless");
            Console.WriteLine("Settings: " + paths.SettingsPath);
            Console.WriteLine();

            var errors = load.Issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
            foreach (var issue in load.Issues) Console.WriteLine("[" + issue.Severity + "] " + issue);
            if (errors.Count > 0)
            {
                Console.WriteLine("Fix the settings first (run --selftest for details).");
                return 1;
            }

            SingleInstance instance;
            if (!SingleInstance.TryAcquire(InstanceKey(load), out instance))
            {
                Console.WriteLine("Another RestartVPN instance is already running for this profile.");
                return 2;
            }

            using (instance)
            using (var controller = new AppController(paths, store, load, options.DryRun))
            {
                controller.EventRaised += ev =>
                {
                    try { Console.WriteLine(ev.At.ToString("HH:mm:ss") + "  " + ev.Message); } catch { }
                };

                var stop = new ManualResetEventSlim(false);
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop.Set(); };

                if (!controller.StartMonitoring())
                {
                    Console.WriteLine("Cannot start: " + controller.LastError);
                    return 1;
                }

                Console.WriteLine("Monitoring started. Press Ctrl+C to stop.");
                stop.Wait();
                controller.StopMonitoringAsync().GetAwaiter().GetResult();
                Console.WriteLine("Stopped.");
            }

            return 0;
        }

        private static int RunGui(AppPaths paths, SettingsStore store, SettingsLoadResult load, CliOptions options)
        {
            SingleInstance instance;
            if (!SingleInstance.TryAcquire(InstanceKey(load), out instance))
                return 2;

            using (instance)
            using (var controller = new AppController(paths, store, load, options.DryRun))
            using (var context = new TrayContext(controller))
            {
                // Tray-only: no main window is shown on startup. Settings and the
                // monitoring on/off switch live in the tray menu.
                Application.Run(context);
            }

            return 0;
        }

        private static bool AnotherInstanceRunning()
        {
            try
            {
                using (var current = Process.GetCurrentProcess())
                {
                    int session = current.SessionId;
                    foreach (var process in Process.GetProcessesByName("RestartVPN"))
                    {
                        using (process)
                        {
                            if (process.Id != current.Id && process.SessionId == session) return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// One instance per VPN profile, not per settings file: two copies with different
        /// settings must not manage the same tunnel.
        /// </summary>
        private static string InstanceKey(SettingsLoadResult load)
        {
            string profile = load?.Settings?.Vpn?.ProfileName ?? string.Empty;
            return "profile:" + profile.Trim().ToLowerInvariant();
        }

        private static int ShowText(string text, int exitCode)
        {
            if (ConsoleHost.Ensure(false)) Console.WriteLine(text);
            else
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "RestartVPN-console.txt"), text + Environment.NewLine, Encoding.UTF8); } catch { }
                MessageBox.Show(text, "RestartVPN", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return exitCode;
        }

        private static void InstallGlobalErrorHandlers()
        {
            Application.ThreadException += (s, e) =>
            {
                try { MessageBox.Show(e.Exception.Message, "RestartVPN — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RestartVPN", "logs");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "crash.log"),
                        DateTimeOffset.Now.ToString("o") + "  " + ex + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            };
        }
    }
}
