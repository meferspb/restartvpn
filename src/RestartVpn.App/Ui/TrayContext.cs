using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Runtime;
using RestartVpn.Core.Vpn;

namespace RestartVpn.App.Ui
{
    /// <summary>
    /// Tray-only shell: no main window is shown on startup. The tray menu exposes the
    /// monitoring on/off switch, status, quick actions and settings.
    /// </summary>
    public sealed class TrayContext : ApplicationContext
    {
        private readonly AppController _controller;
        private readonly MainForm _form;
        private readonly NotifyIcon _tray;
        private ToolStripMenuItem _statusItem;
        private ToolStripMenuItem _monitoringItem;
        private ToolStripMenuItem _checkItem;
        private ToolStripMenuItem _restartItem;
        private ToolStripMenuItem _pauseItem;
        private ToolStripMenuItem _resumeItem;
        private bool _exitRequested;
        private bool _needsSetup;

        public TrayContext(AppController controller, bool showWindow = false)
        {
            _controller = controller;
            _form = new MainForm(controller);
            _form.FormClosed += (s, e) => { if (!_exitRequested) ExitApplication(); };
            _form.ExitRequested += ExitApplication;

            _tray = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "RestartVPN",
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };
            _tray.MouseClick += OnTrayMouseClick;
            _tray.BalloonTipClicked += OnBalloonClick;

            controller.EventRaised += OnEvent;
            controller.Changed += OnControllerChanged;

            if (controller.Settings.Ui.MonitoringEnabled)
            {
                if (!controller.StartMonitoring())
                {
                    _needsSetup = !controller.Settings.Targets.Any(t => t.Enabled);
                    ShowBalloon("Мониторинг не запущен: " + controller.LastError +
                                (_needsSetup ? "\nНажмите на это уведомление, чтобы открыть настройки." : ""),
                        ToolTipIcon.Warning);
                }
            }

            UpdateTrayState();
            if (showWindow) ShowWindow();
        }

        private Icon LoadTrayIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            return SystemIcons.Shield;
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Показать окно", null, (s, e) => ShowWindow());
            menu.Items.Add(new ToolStripSeparator());

            _statusItem = new ToolStripMenuItem("RestartVPN") { Enabled = false };
            menu.Items.Add(_statusItem);

            _monitoringItem = new ToolStripMenuItem("Включить мониторинг");
            _monitoringItem.Click += async (s, e) => await ToggleMonitoringAsync();
            menu.Items.Add(_monitoringItem);

            _checkItem = new ToolStripMenuItem("Проверить сейчас", null, async (s, e) =>
            {
                try
                {
                    await _controller.CheckNowAsync();
                    ShowBalloon(BuildShortStatus(_controller.Snapshot), ToolTipIcon.Info);
                }
                catch (Exception ex) { ShowBalloon(ex.Message, ToolTipIcon.Error); }
            });
            menu.Items.Add(_checkItem);

            _restartItem = new ToolStripMenuItem("Перезапустить VPN", null, async (s, e) =>
            {
                try
                {
                    string result = await _controller.RestartVpnAsync(false);
                    ShowBalloon(result, ToolTipIcon.Info);
                }
                catch (Exception ex) { ShowBalloon(ex.Message, ToolTipIcon.Error); }
            });
            menu.Items.Add(_restartItem);

            menu.Items.Add(new ToolStripSeparator());
            _pauseItem = new ToolStripMenuItem("Пауза");
            foreach (int minutes in new[] { 15, 30, 60, 120 })
            {
                int m = minutes;
                _pauseItem.DropDownItems.Add("На " + m + " мин", null, (s, e) =>
                {
                    _controller.Snooze(TimeSpan.FromMinutes(m));
                    ShowBalloon("Мониторинг на паузе " + m + " мин.", ToolTipIcon.Info);
                });
            }
            menu.Items.Add(_pauseItem);

            _resumeItem = new ToolStripMenuItem("Возобновить", null, (s, e) => _controller.Resume());
            menu.Items.Add(_resumeItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Настройки", null, (s, e) => OpenSettings());
            menu.Items.Add("Открыть журнал", null, (s, e) => OpenLogs());
            menu.Items.Add("Копировать диагностику", null, (s, e) => CopyDiagnostics());
            menu.Items.Add("О программе", null, (s, e) => ShowAbout());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, (s, e) => ExitApplication());

            menu.Opening += (s, e) => UpdateMenuState();
            return menu;
        }

        private async System.Threading.Tasks.Task ToggleMonitoringAsync()
        {
            try
            {
                if (_controller.IsMonitoring)
                {
                    await _controller.StopMonitoringAsync();
                    _controller.SetMonitoringEnabled(false);
                    ShowBalloon("Мониторинг выключен.", ToolTipIcon.Info);
                }
                else
                {
                    if (!_controller.StartMonitoring())
                    {
                        _needsSetup = !_controller.Settings.Targets.Any(t => t.Enabled);
                        ShowBalloon("Мониторинг не запущен: " + _controller.LastError +
                                    "\nПроверьте настройки профиля и сайтов.", ToolTipIcon.Warning);
                    }
                    else
                    {
                        ShowBalloon("Мониторинг включён.", ToolTipIcon.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowBalloon(ex.Message, ToolTipIcon.Error);
            }

            UpdateTrayState();
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ShowMenu();
        }

        private void OnBalloonClick(object sender, EventArgs e)
        {
            if (_needsSetup)
            {
                _needsSetup = false;
                OpenSettings();
            }
            else
            {
                ShowMenu();
            }
        }

        public void RequestActivate()
        {
            if (_exitRequested || _form.IsDisposed || !_form.IsHandleCreated) return;
            try { _form.BeginInvoke(new Action(ShowMenu)); } catch { }
        }

        private void ShowMenu()
        {
            if (_tray.ContextMenuStrip == null) return;
            UpdateMenuState();
            _tray.ContextMenuStrip.Show(Cursor.Position);
        }

        public void ShowWindow()
        {
            if (_form.IsDisposed) return;
            if (!_form.Visible) _form.Show();
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            _form.BringToFront();
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(_controller.Settings.Clone(), _controller.Paths))
            {
                if (form.ShowDialog(_form) != DialogResult.OK) return;
                if (!_controller.ApplySettings(form.Result))
                    ShowBalloon(_controller.LastError, ToolTipIcon.Warning);
            }
            UpdateTrayState();
        }

        private void OpenLogs()
        {
            try
            {
                string dir = _controller.Settings.Logging.Directory;
                if (string.IsNullOrWhiteSpace(dir)) dir = _controller.Paths.LogDir;
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { ShowBalloon(ex.Message, ToolTipIcon.Warning); }
        }

        private void CopyDiagnostics()
        {
            try
            {
                var snapshot = _controller.Snapshot;
                var sb = new StringBuilder();
                sb.AppendLine("RestartVPN " + AppInfo.Version);
                sb.AppendLine("settings: " + _controller.Paths.SettingsPath);
                sb.AppendLine("profile: " + _controller.Settings.Vpn.ProfileName);
                sb.AppendLine("monitoring: " + (_controller.IsMonitoring ? "on" : "off") +
                              " (enabled flag: " + _controller.Settings.Ui.MonitoringEnabled + ")");
                sb.AppendLine("vpn: " + snapshot.Vpn.State + " — " + snapshot.Vpn.Detail);
                foreach (var t in snapshot.Targets)
                    sb.AppendLine("target " + t.Name + ": " + (t.HasResult ? (t.Success ? "OK " : "FAIL ") + t.Detail : "not probed") +
                                  " (" + t.ConsecutiveFailures + " fails)");
                sb.AppendLine("logs: " + (string.IsNullOrWhiteSpace(_controller.Settings.Logging.Directory)
                    ? _controller.Paths.LogDir : _controller.Settings.Logging.Directory));
                Clipboard.SetText(sb.ToString());
                ShowBalloon("Диагностика скопирована в буфер обмена.", ToolTipIcon.Info);
            }
            catch (Exception ex) { ShowBalloon(ex.Message, ToolTipIcon.Warning); }
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "RestartVPN " + AppInfo.Version + "\n\n" +
                "Монитор доступности сайтов с перезапуском одного профиля OpenVPN.\n" +
                "Лицензия MIT. Репозиторий: " + AppInfo.RepositoryUrl + "\n\n" +
                "Настройки и журналы: " + _controller.Paths.DataDir,
                "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateTrayState()
        {
            var snapshot = _controller.Snapshot;
            string text = BuildStatusText(snapshot);
            if (text.Length > 63) text = text.Substring(0, 63);
            _tray.Text = text;
            if (_statusItem != null) _statusItem.Text = text;
            UpdateMenuState();
        }

        private string BuildStatusText(MonitorSnapshot snapshot)
        {
            bool on = _controller.IsMonitoring;
            string state = on ? "мониторинг включён" : "мониторинг выключен";
            string sites = BuildShortStatus(snapshot);
            string vpn = VpnText(snapshot.Vpn);
            string text = "RestartVPN — " + state + " · " + sites;
            if (vpn.Length > 0) text += " · VPN: " + vpn;
            if (snapshot.SnoozedUntil.HasValue && snapshot.SnoozedUntil.Value > DateTimeOffset.Now)
                text += " (пауза)";
            return text;
        }

        private static string BuildShortStatus(MonitorSnapshot snapshot)
        {
            int enabled = 0, ok = 0, down = 0;
            foreach (var t in snapshot.Targets)
            {
                if (!t.Enabled) continue;
                enabled++;
                if (t.HasResult && t.Success) ok++;
                else if (t.HasResult) down++;
            }
            if (enabled == 0) return "нет целей";
            string text = ok + "/" + enabled + " OK";
            if (down > 0) text += ", " + down + " недоступно";
            return text;
        }

        private static string VpnText(VpnStatus vpn)
        {
            if (vpn == null) return string.Empty;
            switch (vpn.State)
            {
                case VpnState.Connected: return "подключён";
                case VpnState.Connecting: return "подключается";
                case VpnState.Disconnected: return "отключён";
                case VpnState.Error: return "ошибка";
                default: return string.Empty;
            }
        }

        private void UpdateMenuState()
        {
            if (_monitoringItem == null) return;
            bool on = _controller.IsMonitoring;
            _monitoringItem.Text = on ? "Выключить мониторинг" : "Включить мониторинг";
            _monitoringItem.Checked = on;
            _checkItem.Enabled = on;
            _restartItem.Enabled = on;
            _pauseItem.Enabled = on;
            var snapshot = _controller.Snapshot;
            _resumeItem.Enabled = on && snapshot.SnoozedUntil.HasValue && snapshot.SnoozedUntil.Value > DateTimeOffset.Now;
        }

        private void OnControllerChanged()
        {
            if (_exitRequested || _form.IsDisposed || !_form.IsHandleCreated) return;
            try { _form.BeginInvoke(new Action(UpdateTrayState)); } catch { }
        }

        private void OnEvent(MonitorEvent ev)
        {
            if (_exitRequested || _tray == null || _form.IsDisposed || !_form.IsHandleCreated) return;
            try { _form.BeginInvoke(new Action<MonitorEvent>(ShowEventNotification), ev); } catch { }
        }

        private void ShowEventNotification(MonitorEvent ev)
        {
            if (_tray == null || !_tray.Visible) return;
            var s = _controller.Settings;
            bool important = ev.Level == LogLevel.Error ||
                             ev.Code.StartsWith("alert.", StringComparison.Ordinal) ||
                             ev.Code == "vpn.recovered" ||
                             ev.Code == "vpn.notRecovered" ||
                             ev.Code == "vpn.restart" ||
                             ev.Code == "vpn.connect";
            if (!important) return;
            if (s.Notifications.OnlyOnAction && ev.Code.StartsWith("probe.", StringComparison.Ordinal)) return;

            if (s.Notifications.Balloon)
                ShowBalloon(ev.Message, ev.Level == LogLevel.Error ? ToolTipIcon.Error : ToolTipIcon.Warning);
            if (s.Notifications.Sound)
            {
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            }
        }

        private void ShowBalloon(string text, ToolTipIcon icon)
        {
            try { _tray.ShowBalloonTip(4000, "RestartVPN", text, icon); } catch { }
        }

        private void ExitApplication()
        {
            if (_exitRequested) return;
            _exitRequested = true;
            _tray.Visible = false;
            _form.AllowExit();
            _form.Close();
            try { _controller.Dispose(); } catch { }
            _tray.Dispose();
            ExitThread();
        }
    }
}
