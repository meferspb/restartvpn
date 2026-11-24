using System;
using System.Collections.Generic;
using System.Text;

namespace RestartVpn.App.Cli
{
    public sealed class CliOptions
    {
        public bool Help { get; private set; }
        public bool Version { get; private set; }
        public bool SelfTest { get; private set; }
        public bool Headless { get; private set; }
        public bool Once { get; private set; }
        public bool Install { get; private set; }
        public bool Silent { get; private set; }
        public bool Uninstall { get; private set; }
        public bool UninstallRun { get; private set; }
        public bool Portable { get; private set; }
        public bool Minimized { get; private set; }
        public bool Show { get; private set; }
        public bool Status { get; private set; }
        public bool DryRun { get; private set; }
        public bool NoInstallPrompt { get; private set; }
        public string ConfigPath { get; private set; }
        public string UninstallDirectory { get; private set; }
        public List<string> Errors { get; } = new List<string>();

        public static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? string.Empty;
                switch (a.ToLowerInvariant())
                {
                    case "-h":
                    case "--help":
                    case "/?": o.Help = true; break;
                    case "--version": o.Version = true; break;
                    case "--selftest": o.SelfTest = true; break;
                    case "--headless": o.Headless = true; break;
                    case "--once": o.Once = true; break;
                    case "--install": o.Install = true; break;
                    case "--silent": o.Silent = true; break;
                    case "--uninstall": o.Uninstall = true; break;
                    case "--uninstall-run":
                        o.UninstallRun = true;
                        if (i + 1 < args.Length) { o.UninstallDirectory = args[++i]; }
                        break;
                    case "--portable": o.Portable = true; break;
                    case "--minimized": o.Minimized = true; break; // legacy autostart entries; tray is the default
                    case "--show": o.Show = true; break;
                    case "--status": o.Status = true; break;
                    case "--dry-run": o.DryRun = true; break;
                    case "--no-install-prompt": o.NoInstallPrompt = true; break;
                    case "--config":
                        if (i + 1 < args.Length) o.ConfigPath = args[++i];
                        else o.Errors.Add("--config requires a path.");
                        break;
                    default:
                        if (a.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
                            o.ConfigPath = a.Substring("--config=".Length);
                        else
                            o.Errors.Add("Unknown argument: " + a);
                        break;
                }
            }
            return o;
        }

        public static string HelpText
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine("RestartVPN " + AppInfo.Version);
                sb.AppendLine();
                sb.AppendLine("Usage:");
                sb.AppendLine("  RestartVPN.exe                 start in the tray (no window)");
                sb.AppendLine("  RestartVPN.exe --show          start and open the status window");
                sb.AppendLine("  RestartVPN.exe --status        print JSON status and exit");
                sb.AppendLine("  RestartVPN.exe --headless      run the monitor in this console");
                sb.AppendLine("  RestartVPN.exe --selftest      read-only check of settings, VPN state and targets");
                sb.AppendLine("  RestartVPN.exe --once          one probe cycle, no VPN actions");
                sb.AppendLine("  RestartVPN.exe --install [--silent]");
                sb.AppendLine("  RestartVPN.exe --uninstall");
                sb.AppendLine("  RestartVPN.exe --portable      keep settings next to the exe");
                sb.AppendLine("  RestartVPN.exe --dry-run       monitor only, never touch the VPN");
                sb.AppendLine("  RestartVPN.exe --config <path> use an alternative settings file");
                sb.AppendLine();
                sb.AppendLine("Exit codes: 0 ok, 1 error, 2 another instance is running.");
                return sb.ToString();
            }
        }
    }
}