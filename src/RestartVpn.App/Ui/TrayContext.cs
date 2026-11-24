using System;
using System.Drawing;
using System.Windows.Forms;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Runtime;

namespace RestartVpn.App.Ui
{
    /// <summary>
    /// Tray-only shell: no main window is shown on startup. The tray menu exposes the
    /// monitoring on/off switch, settings and the optional status window.
    /// </summary>
    public sealed class TrayContext : ApplicationContext
    {
        private readonly AppController _controller;
        private readonly MainForm _form;
        private readonly NotifyIcon _tray;
        private ToolStripMenuItem _monitoringItem;
        private ToolStripMenuItem _checkItem;
        private ToolStripMenuItem _restartItem;
        private ToolStripMenuItem _snoozeItem;
        private ToolStripMenuItem _resumeItem;
        private bool _exitRequested;

        public TrayContext(AppController controller)
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
            _tray.BalloonTipClicked += (s, e) => ShowMenu();

            controller.EventRaised += OnEvent;
            controller.Changed += OnControllerChanged;

            if (controller.Settings.Ui.MonitoringEnabled)
            {
                if (!controller.StartMonitoring())
                {
                    ShowBalloon("Мониторинг не запущен: " + controller.LastError +
                                "\nОткройте «Настройки» в меню значка.", ToolTipIcon.Warning);
                }
            }

            UpdateTrayState();
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

            _monitoringItem = new ToolStripMenuItem("Включить мониторинг");
            _monitoringItem.Click += async (s, e) => await ToggleMonitoringAsync();
            menu.Items.Add(_monitoringItem);

            _checkItem = new ToolStripMenuItem("Проверить сейчас", null, async (s, e) =>
            {
                try { await _controller.CheckNowAsync(); }
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
            _snoozeItem = new ToolStripMenuItem("Пауза 30 минут", null, (s, e) => _controller.Snooze(TimeSpan.FromMinutes(30)));
            menu.Items.Add(_snoozeItem);
            _resumeItem = new ToolStripMenuItem("Возобновить", null, (s, e) => _controller.Resume());
            menu.Items.Add(_resumeItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Настройки", null, (s, e) =>
            {
                using (var form = new SettingsForm(_controller.Settings.Clone(), _controller.Paths))
                {
                    if (form.ShowDialog(_form) == DialogResult.OK)
                    {
                        if (!_controller.ApplySettings(form.Result))
                            ShowBalloon(_controller.LastError, ToolTipIcon.Warning);
                    }
                }
            });
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
                }
                else
                {
                    if (!_controller.StartMonitoring())
                    {
                        ShowBalloon("Мониторинг не запущен: " + _controller.LastError +
                                    "\nПроверьте настройки профиля и сайтов.", ToolTipIcon.Warning);
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

        private void UpdateTrayState()
        {
            bool on = _controller.IsMonitoring;
            string text = on ? "RestartVPN — мониторинг включён" : "RestartVPN — мониторинг выключен";
            var snapshot = _controller.Snapshot;
            if (snapshot.SnoozedUntil.HasValue && snapshot.SnoozedUntil.Value > DateTimeOffset.Now)
                text += " (пауза)";
            if (text.Length > 63) text = text.Substring(0, 63);
            _tray.Text = text;
            UpdateMenuState();
        }

        private void UpdateMenuState()
        {
            if (_monitoringItem == null) return;
            bool on = _controller.IsMonitoring;
            _monitoringItem.Text = on ? "Выключить мониторинг" : "Включить мониторинг";
            _monitoringItem.Checked = on;
            _checkItem.Enabled = on;
            _restartItem.Enabled = on;
            _snoozeItem.Enabled = on;
            var snapshot = _controller.Snapshot;
            _resumeItem.Enabled = on && snapshot.SnoozedUntil.HasValue && snapshot.SnoozedUntil.Value > DateTimeOffset.Now;
        }

        private void OnControllerChanged()
        {
            if (_exitRequested || _form.IsDisposed || !_form.IsHandleCreated) return;
            try { _form.BeginInvoke(new Action(UpdateTrayState)); }
            catch { }
        }

        private void OnEvent(MonitorEvent ev)
        {
            if (_exitRequested || _tray == null || _form.IsDisposed || !_form.IsHandleCreated) return;
            try { _form.BeginInvoke(new Action<MonitorEvent>(ShowEventNotification), ev); }
            catch { }
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
