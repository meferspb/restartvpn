using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using RestartVpn.Core.Diagnostics;
using RestartVpn.Core.Runtime;
using RestartVpn.Core.Vpn;

namespace RestartVpn.App.Ui
{
    public sealed class MainForm : Form
    {
        private readonly AppController _controller;
        private readonly Timer _timer;

        private readonly Label _stateDot;
        private readonly Label _stateText;
        private readonly Label _vpnText;
        private readonly Label _modeText;
        private readonly Label _nextText;
        private readonly DataGridView _grid;
        private readonly RichTextBox _logBox;
        private readonly Button _startStop;
        private readonly Button _restart;
        private readonly Button _snooze;
        private readonly Font _boldFont = new Font("Segoe UI Semibold", 9F);
        private bool _allowExit;
        private bool _trayHintShown;
        private string _gridSignature = string.Empty;

        public event Action ExitRequested;

        public MainForm(AppController controller)
        {
            _controller = controller;

            Text = "RestartVPN " + AppInfo.Version;
            ClientSize = new Size(960, 640);
            MinimumSize = new Size(780, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            Icon = LoadAppIcon();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            // Header
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 52, 92) };
            var title = new Label
            {
                Text = "RestartVPN",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 17F),
                AutoSize = true,
                Location = new Point(18, 14)
            };
            var subtitle = new Label
            {
                Text = "мониторинг сайтов и перезапуск выбранного профиля OpenVPN",
                ForeColor = Color.FromArgb(190, 210, 235),
                AutoSize = true,
                Location = new Point(20, 52)
            };
            header.Controls.AddRange(new Control[] { title, subtitle });

            // Status
            var status = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(8, 4, 0, 0) };
            _stateDot = new Label { Text = "●", Font = new Font("Segoe UI", 14F), ForeColor = Color.Gray, AutoSize = true, Location = new Point(4, 6) };
            _stateText = new Label { Text = "Остановлено", Font = new Font("Segoe UI Semibold", 12F), AutoSize = true, Location = new Point(24, 8) };
            _vpnText = new Label { Text = "VPN: —", AutoSize = true, Location = new Point(4, 36), ForeColor = Color.FromArgb(80, 80, 80) };
            _modeText = new Label { Text = "Режим: —", AutoSize = true, Location = new Point(250, 36), ForeColor = Color.FromArgb(80, 80, 80) };
            _nextText = new Label { Text = "", AutoSize = true, Location = new Point(560, 36), ForeColor = Color.FromArgb(80, 80, 80) };
            status.Controls.AddRange(new Control[] { _stateDot, _stateText, _vpnText, _modeText, _nextText });

            // Targets grid
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _grid.Columns.Add(TextColumn("Сайт", 18));
            _grid.Columns.Add(TextColumn("Статус", 12));
            _grid.Columns.Add(TextColumn("Ответ", 26));
            _grid.Columns.Add(TextColumn("мс", 8));
            _grid.Columns.Add(TextColumn("Сбои", 7));
            _grid.Columns.Add(TextColumn("Проверено", 12));

            var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
            gridHost.Controls.Add(_grid);

            // Log
            var logLabel = new Label { Text = "Журнал", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 9F), TextAlign = ContentAlignment.BottomLeft };
            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(248, 249, 251),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5F),
                DetectUrls = false
            };

            // Buttons
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };

            _startStop = MakeButton("Старт", 92);
            _startStop.Click += async (s, e) => await ToggleStartStopAsync();

            var check = MakeButton("Проверить", 92);
            check.Click += async (s, e) => await CheckNowAsync();

            _restart = MakeButton("Перезапустить VPN", 150);
            _restart.Click += async (s, e) => await RestartAsync();

            _snooze = MakeButton("Пауза", 84);
            _snooze.Click += (s, e) => ShowSnoozeMenu();

            var settings = MakeButton("Настройки", 100);
            settings.Click += (s, e) => OpenSettings();

            var logs = MakeButton("Журнал", 84);
            logs.Click += (s, e) => OpenLogs();

            var exit = MakeButton("Выход", 84);
            exit.Click += (s, e) => { var handler = ExitRequested; if (handler != null) handler(); };

            buttons.Controls.AddRange(new Control[] { _startStop, check, _restart, _snooze, settings, logs, exit });

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(status, 0, 1);
            root.Controls.Add(gridHost, 0, 2);
            root.Controls.Add(logLabel, 0, 3);
            root.Controls.Add(_logBox, 0, 4);
            root.Controls.Add(buttons, 0, 5);
            Controls.Add(root);

            _controller.EventRaised += OnMonitorEvent;
            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) => RefreshUi();
            _timer.Start();

            AppendLog("Приложение запущено. Настройки: " + _controller.Paths.SettingsPath, LogLevel.Info);
            RefreshUi();

            // Force the window handle while we are still on the UI thread. This lets
            // BeginInvoke work even when the window stays hidden in the tray.
            try { _ = Handle; } catch { }
        }

        public void AllowExit() => _allowExit = true;

        private static DataGridViewTextBoxColumn TextColumn(string header, int fillWeight) =>
            new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = fillWeight, SortMode = DataGridViewColumnSortMode.NotSortable };

        private static Button MakeButton(string text, int width) =>
            new Button { Text = text, Width = width, Height = 34, Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.System };

        private static Icon LoadAppIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            return SystemIcons.Shield;
        }

        private async Task ToggleStartStopAsync()
        {
            if (_controller.IsMonitoring)
            {
                await _controller.StopMonitoringAsync();
                _controller.SetMonitoringEnabled(false);
                AppendLog("Мониторинг остановлен.", LogLevel.Info);
            }
            else
            {
                if (!_controller.StartMonitoring())
                    MessageBox.Show(this, _controller.LastError, "Не удалось запустить мониторинг",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshUi();
        }

        private async Task CheckNowAsync()
        {
            _restart.Enabled = false;
            try { await _controller.CheckNowAsync(); }
            catch (Exception ex) { AppendLog(ex.Message, LogLevel.Error); }
            finally { _restart.Enabled = true; }
            RefreshUi();
        }

        private async Task RestartAsync()
        {
            if (_controller.Settings.Ui.ConfirmRestart)
            {
                var answer = MessageBox.Show(this,
                    "Перезапустить профиль OpenVPN '" + _controller.Settings.Vpn.ProfileName + "'?\n\n" +
                    "Другие VPN-подключения не затрагиваются.",
                    "Перезапуск VPN", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
            }

            _restart.Enabled = false;
            try
            {
                string result = await _controller.RestartVpnAsync(false);
                AppendLog("Ручной перезапуск: " + result, LogLevel.Info);
            }
            catch (Exception ex) { AppendLog("Перезапуск не удался: " + ex.Message, LogLevel.Error); }
            finally { _restart.Enabled = true; }
            RefreshUi();
        }

        private void ShowSnoozeMenu()
        {
            var menu = new ContextMenuStrip();
            foreach (int minutes in new[] { 15, 30, 60, 120 })
            {
                int m = minutes;
                menu.Items.Add("Пауза " + m + " мин", null, (s, e) =>
                {
                    _controller.Snooze(TimeSpan.FromMinutes(m));
                    AppendLog("Пауза " + m + " мин.", LogLevel.Info);
                });
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Возобновить", null, (s, e) => _controller.Resume());
            menu.Show(_snooze, new Point(0, _snooze.Height));
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(_controller.Settings.Clone(), _controller.Paths))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                if (!_controller.ApplySettings(form.Result))
                {
                    MessageBox.Show(this, _controller.LastError, "Настройки не применены",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                AppendLog("Настройки сохранены.", LogLevel.Info);
                RefreshUi();
            }
        }

        private void OpenLogs()
        {
            try
            {
                string dir = _controller.Settings.Logging.Directory;
                if (string.IsNullOrWhiteSpace(dir)) dir = _controller.Paths.LogDir;
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { AppendLog("Не удалось открыть журнал: " + ex.Message, LogLevel.Warn); }
        }

        private void RefreshUi()
        {
            if (IsDisposed) return;
            MonitorSnapshot snapshot;
            try { snapshot = _controller.Snapshot; }
            catch { return; }

            _startStop.Text = _controller.IsMonitoring ? "Стоп" : "Старт";
            _restart.Enabled = snapshot.State != MonitorRunState.Restarting;

            Color stateColor;
            string stateText;
            switch (snapshot.State)
            {
                case MonitorRunState.Monitoring: stateColor = Color.FromArgb(39, 174, 96); stateText = "Мониторинг"; break;
                case MonitorRunState.Restarting: stateColor = Color.FromArgb(230, 126, 34); stateText = "Перезапуск VPN"; break;
                case MonitorRunState.Snoozed: stateColor = Color.FromArgb(41, 128, 185); stateText = "Пауза"; break;
                case MonitorRunState.Starting: stateColor = Color.FromArgb(41, 128, 185); stateText = "Запуск"; break;
                case MonitorRunState.Error: stateColor = Color.FromArgb(192, 57, 43); stateText = "Ошибка"; break;
                default: stateColor = Color.Gray; stateText = "Остановлено"; break;
            }
            _stateDot.ForeColor = stateColor;
            _stateText.Text = stateText + (string.IsNullOrEmpty(snapshot.StateDetail) ? "" : " — " + snapshot.StateDetail);

            string vpn;
            if (snapshot.Vpn == null) vpn = "—";
            else
            {
                switch (snapshot.Vpn.State)
                {
                    case VpnState.Connected: vpn = "подключён"; break;
                    case VpnState.Connecting: vpn = "подключается"; break;
                    case VpnState.Disconnected: vpn = "отключён"; break;
                    case VpnState.Error: vpn = "ошибка"; break;
                    default: vpn = "неизвестно"; break;
                }
            }
            _vpnText.Text = "VPN: " + vpn + "   профиль: " + (string.IsNullOrWhiteSpace(_controller.Settings.Vpn.ProfileName) ? "—" : _controller.Settings.Vpn.ProfileName);

            if (!snapshot.AutoRestartEnabled && _controller.Settings.IsAutoRestart)
                _modeText.Text = "Режим: только мониторинг (" + snapshot.AutoRestartDisabledReason + ")";
            else
                _modeText.Text = "Режим: " + (_controller.Settings.IsAutoRestart ? "автоперезапуск" : "только мониторинг");

            var parts = new System.Collections.Generic.List<string>();
            if (snapshot.SnoozedUntil.HasValue) parts.Add("пауза до " + snapshot.SnoozedUntil.Value.ToLocalTime().ToString("HH:mm"));
            if (snapshot.DownCount > 0 && snapshot.NextActionAllowedAt.HasValue && snapshot.NextActionAllowedAt.Value > DateTimeOffset.Now)
                parts.Add("след. действие после " + snapshot.NextActionAllowedAt.Value.ToLocalTime().ToString("HH:mm"));
            if (snapshot.RestartsLastHour > 0) parts.Add("перезапусков за час: " + snapshot.RestartsLastHour);
            _nextText.Text = string.Join("   ", parts);

            _grid.SuspendLayout();
            string signature = string.Join("|", snapshot.Targets.Select(t => t.Name + ":" + t.Enabled));
            if (_grid.Rows.Count != snapshot.Targets.Count || !string.Equals(signature, _gridSignature, StringComparison.Ordinal))
            {
                _gridSignature = signature;
                _grid.Rows.Clear();
                foreach (var t in snapshot.Targets) _grid.Rows.Add(t.Name, "", "", "", "", "");
            }
            for (int i = 0; i < snapshot.Targets.Count && i < _grid.Rows.Count; i++)
            {
                var t = snapshot.Targets[i];
                string status = !t.HasResult ? "—" : (t.Success ? "OK" : "НЕДОСТУПЕН");
                if (!t.Enabled) status = "выключен";
                var row = _grid.Rows[i];
                row.Cells[0].Value = t.Name;
                row.Cells[1].Value = status;
                row.Cells[2].Value = t.Detail;
                row.Cells[3].Value = t.HasResult ? t.LatencyMs.ToString() : "";
                row.Cells[4].Value = t.ConsecutiveFailures > 0 ? t.ConsecutiveFailures.ToString() : "";
                row.Cells[5].Value = t.LastCheckedAt.HasValue ? t.LastCheckedAt.Value.ToLocalTime().ToString("HH:mm:ss") : "";

                if (t.Enabled && t.HasResult)
                {
                    row.Cells[1].Style.ForeColor = t.Success ? Color.FromArgb(30, 130, 70) : Color.FromArgb(180, 40, 30);
                    row.Cells[1].Style.Font = _boldFont;
                }
            }
            _grid.ResumeLayout();
        }

        private void OnMonitorEvent(MonitorEvent ev)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                if (InvokeRequired) { BeginInvoke(new Action<MonitorEvent>(OnMonitorEvent), ev); return; }
                AppendLog(ev.Message, ev.Level, ev.At);
            }
            catch { }
        }

        private void AppendLog(string message, LogLevel level, DateTimeOffset? at = null)
        {
            var time = (at ?? DateTimeOffset.Now).ToLocalTime().ToString("HH:mm:ss");
            Color color = Color.FromArgb(70, 70, 70);
            if (level == LogLevel.Warn) color = Color.FromArgb(170, 95, 0);
            else if (level == LogLevel.Error) color = Color.FromArgb(180, 40, 30);
            else if (level == LogLevel.Debug) color = Color.FromArgb(130, 130, 130);

            if (_logBox.TextLength > 150000) _logBox.Clear();
            int start = _logBox.TextLength;
            _logBox.AppendText(time + "  " + message + Environment.NewLine);
            _logBox.Select(start, _logBox.TextLength - start);
            _logBox.SelectionColor = color;
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Never cancel a Windows shutdown/logoff just to hide in the tray.
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing &&
                _controller.Settings.Ui.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    _controller.Settings.Ui.MinimizeToTrayHintShown = true;
                }
                return;
            }
            _timer.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _boldFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
