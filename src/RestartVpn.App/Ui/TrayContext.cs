using System;
using System.Drawing;
using System.Windows.Forms;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Runtime;

namespace RestartVpn.App.Ui
{
    public sealed class TrayContext : ApplicationContext
    {
        private readonly AppController _controller;
        private readonly MainForm _form;
        private readonly NotifyIcon _tray;
        private bool _exitRequested;

        public TrayContext(AppController controller, bool startMinimized)
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
            _tray.DoubleClick += (s, e) => ShowWindow();
            _tray.BalloonTipClicked += (s, e) => ShowWindow();

            controller.EventRaised += OnEvent;

            if (!startMinimized) ShowWindow();
            else _tray.ShowBalloonTip(2000, "RestartVPN", "Приложение работает в трее.", ToolTipIcon.Info);

            if (!controller.IsMonitoring)
            {
                if (!controller.StartMonitoring())
                {
                    controller.Log.Warn("Monitoring did not start: " + controller.LastError);
                    _tray.ShowBalloonTip(4000, "RestartVPN",
                        "Мониторинг не запущен: " + controller.LastError, ToolTipIcon.Warning);
                }
            }
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
            menu.Items.Add("Показать", null, (s, e) => ShowWindow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Проверить сейчас", null, async (s, e) =>
            {
                try { await _controller.CheckNowAsync(); }
                catch (Exception ex) { _tray.ShowBalloonTip(3000, "RestartVPN", ex.Message, ToolTipIcon.Error); }
            });
            menu.Items.Add("Перезапустить VPN", null, async (s, e) =>
            {
                try
                {
                    string result = await _controller.RestartVpnAsync(false);
                    _tray.ShowBalloonTip(3000, "RestartVPN", result, ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    _tray.ShowBalloonTip(3000, "RestartVPN", ex.Message, ToolTipIcon.Error);
                }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Пауза 30 минут", null, (s, e) => _controller.Snooze(TimeSpan.FromMinutes(30)));
            menu.Items.Add("Возобновить", null, (s, e) => _controller.Resume());
            menu.Items.Add("Настройки", null, (s, e) =>
            {
                ShowWindow();
                using (var form = new SettingsForm(_controller.Settings.Clone(), _controller.Paths))
                {
                    if (form.ShowDialog(_form) == DialogResult.OK)
                    {
                        if (!_controller.ApplySettings(form.Result))
                            _tray.ShowBalloonTip(4000, "RestartVPN", _controller.LastError, ToolTipIcon.Warning);
                    }
                }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, (s, e) => ExitApplication());
            return menu;
        }

        public void ShowWindow()
        {
            if (_form.IsDisposed) return;
            if (!_form.Visible) _form.Show();
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            _form.BringToFront();
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
            {
                try { _tray.ShowBalloonTip(4000, "RestartVPN", ev.Message, ev.Level == LogLevel.Error ? ToolTipIcon.Error : ToolTipIcon.Warning); }
                catch { }
            }
            if (s.Notifications.Sound)
            {
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            }
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
