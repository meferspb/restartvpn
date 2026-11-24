using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;

namespace RestartVpn.App.Ui
{
    public sealed class SettingsForm : Form
    {
        public AppSettings Result { get; private set; }

        private readonly AppSettings _s;
        private readonly AppPaths _paths;

        private RadioButton _monitorOnly;
        private RadioButton _autoRestart;
        private CheckBox _autostart;
        private CheckBox _startMinimized;
        private CheckBox _closeToTray;
        private CheckBox _confirmRestart;
        private CheckBox _balloon;
        private CheckBox _sound;

        private ComboBox _profiles;
        private TextBox _configPath;
        private TextBox _guiPath;
        private NumericUpDown _connectTimeout;
        private NumericUpDown _commandTimeout;
        private CheckBox _connectOnStart;
        private CheckBox _readLog;
        private CheckBox _hardRestart;
        private CheckBox _requireTunnel;
        private CheckBox _restartService;

        private DataGridView _grid;

        private ComboBox _quorum;
        private NumericUpDown _threshold;
        private NumericUpDown _interval;
        private NumericUpDown _correlation;
        private NumericUpDown _retryDelay;
        private NumericUpDown _recoveryTimeout;
        private NumericUpDown _recoverySuccesses;
        private NumericUpDown _minGap;
        private NumericUpDown _maxGap;
        private NumericUpDown _maxPerHour;
        private NumericUpDown _failedBeforeCircuit;
        private NumericUpDown _cooldown;
        private NumericUpDown _jitter;
        private NumericUpDown _connectCooldown;
        private NumericUpDown _alertThrottle;

        private ComboBox _logLevel;
        private NumericUpDown _logSizeMb;
        private NumericUpDown _logKeep;
        private CheckBox _jsonl;

        public SettingsForm(AppSettings settings, AppPaths paths)
        {
            _s = settings ?? new AppSettings();
            _paths = paths;

            Text = "Настройки RestartVPN";
            ClientSize = new Size(720, 620);
            MinimumSize = new Size(680, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = new Font("Segoe UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield; }
            catch { Icon = SystemIcons.Application; }

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildGeneralTab());
            tabs.TabPages.Add(BuildVpnTab());
            tabs.TabPages.Add(BuildTargetsTab());
            tabs.TabPages.Add(BuildPolicyTab());
            tabs.TabPages.Add(BuildLogTab());

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10, 9, 10, 0)
            };
            var save = new Button { Text = "Сохранить", Width = 110, Height = 31, DialogResult = DialogResult.None };
            save.Click += (s, e) => SaveAndClose();
            var cancel = new Button { Text = "Отмена", Width = 90, Height = 31, DialogResult = DialogResult.Cancel };
            var validate = new Button { Text = "Проверить", Width = 100, Height = 31 };
            validate.Click += (s, e) => ShowValidation(false);
            var json = new Button { Text = "Открыть JSON", Width = 120, Height = 31 };
            json.Click += (s, e) => OpenJson();

            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(validate);
            buttons.Controls.Add(json);

            Controls.Add(tabs);
            Controls.Add(buttons);
            AcceptButton = save;
            CancelButton = cancel;

            LoadValues();
        }

        private TabPage NewTab(string title)
        {
            return new TabPage(title) { BackColor = Color.White, Padding = new Padding(4) };
        }

        private static TableLayoutPanel NewGrid(int columns = 2)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = columns,
                Padding = new Padding(12, 10, 12, 10)
            };
            if (columns == 2)
            {
                t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
                t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            }
            else
            {
                while (t.ColumnStyles.Count < columns) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            }
            return t;
        }

        private static void AddRow(TableLayoutPanel t, string label, Control control, int labelWidth = 210)
        {
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) };
            if (labelWidth > 0 && t.ColumnCount == 2 && t.ColumnStyles.Count > 0)
            {
                // keep default proportional widths
            }
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(3, 4, 3, 4);
            t.Controls.Add(l, 0, row);
            t.Controls.Add(control, 1, row);
        }

        private static NumericUpDown Num(int value, int min, int max, int width = 120)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
                Width = width
            };
        }

        private TabPage BuildGeneralTab()
        {
            var page = NewTab("Общие");
            var t = NewGrid();
            _monitorOnly = new RadioButton { Text = "Только мониторинг и предупреждения (VPN не трогать)", AutoSize = true };
            _autoRestart = new RadioButton { Text = "Автоматически перезапускать выбранный профиль VPN", AutoSize = true };
            var modePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            modePanel.Controls.Add(_monitorOnly);
            modePanel.Controls.Add(_autoRestart);
            AddRow(t, "Режим работы", modePanel);

            _autostart = new CheckBox { Text = "Запускать при входе в Windows", AutoSize = true };
            _startMinimized = new CheckBox { Text = "Запускать свёрнутым в трей", AutoSize = true };
            _closeToTray = new CheckBox { Text = "Закрытие окна сворачивает в трей", AutoSize = true };
            _confirmRestart = new CheckBox { Text = "Спрашивать подтверждение перед ручным перезапуском", AutoSize = true };
            AddRow(t, "Запуск", _autostart);
            AddRow(t, "", _startMinimized);
            AddRow(t, "", _closeToTray);
            AddRow(t, "", _confirmRestart);

            _balloon = new CheckBox { Text = "Показывать уведомления в трее", AutoSize = true };
            _sound = new CheckBox { Text = "Звуковой сигнал при событии", AutoSize = true };
            AddRow(t, "Уведомления", _balloon);
            AddRow(t, "", _sound);

            var note = new Label
            {
                Text = "Данные: " + _paths.DataDir + "\nЖурнал: " + _paths.LogDir +
                       "\nПрофиль и настройки OpenVPN приложение не изменяет.",
                AutoSize = true,
                ForeColor = Color.FromArgb(100, 100, 100),
                Margin = new Padding(3, 14, 3, 3)
            };
            AddRow(t, "Расположение", note);
            if (!_paths.RunningFromInstallDir)
                _autostart.Enabled = false;

            page.Controls.Add(t);
            return page;
        }

        private TabPage BuildVpnTab()
        {
            var page = NewTab("VPN");
            var t = NewGrid();

            _profiles = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 380 };
            var discovered = VpnProfileLocator.DiscoverProfiles();
            foreach (var p in discovered) _profiles.Items.Add(p.Name);
            var browse = new Button { Text = "Обзор...", Width = 90, Height = 24 };
            browse.Click += (s, e) => BrowseProfile();
            var profilePanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            profilePanel.Controls.Add(_profiles);
            profilePanel.Controls.Add(browse);
            AddRow(t, "Профиль OpenVPN", profilePanel);

            _configPath = new TextBox { ReadOnly = true, Width = 520 };
            AddRow(t, "Файл профиля", _configPath);

            _guiPath = new TextBox { Width = 480 };
            var browseGui = new Button { Text = "Обзор...", Width = 90, Height = 24 };
            browseGui.Click += (s, e) => BrowseGui();
            var guiPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            guiPanel.Controls.Add(_guiPath);
            guiPanel.Controls.Add(browseGui);
            AddRow(t, "openvpn-gui.exe", guiPanel);

            _connectTimeout = Num(_s.Vpn.ConnectTimeoutSec, 5, 300);
            _commandTimeout = Num(_s.Vpn.CommandTimeoutSec, 3, 120);
            AddRow(t, "Ожидание подключения, с", _connectTimeout);
            AddRow(t, "Таймаут команды GUI, с", _commandTimeout);

            _connectOnStart = new CheckBox { Text = "Подключать профиль при старте, если он не поднят", AutoSize = true };
            _readLog = new CheckBox { Text = "Читать журнал OpenVPN профиля (надёжнее определяет состояние)", AutoSize = true };
            _hardRestart = new CheckBox { Text = "Разрешить жёсткий перезапуск (завершать только процесс этого профиля)", AutoSize = true };
            _requireTunnel = new CheckBox { Text = "Перезапускать только при отказе туннеля (иначе — только предупреждать)", AutoSize = true };
            _restartService = new CheckBox { Text = "Перезапускать, даже если туннель выглядит подключённым", AutoSize = true };
            AddRow(t, "Поведение", _connectOnStart);
            AddRow(t, "", _readLog);
            AddRow(t, "", _hardRestart);
            AddRow(t, "", _requireTunnel);
            AddRow(t, "", _restartService);

            var safety = new Label
            {
                Text = "Приложение сопоставляет процесс OpenVPN с файлом профиля по имени .ovpn.\n" +
                       "Похожие профили (например, ...-backup) и все остальные подключения не затрагиваются.",
                AutoSize = true,
                ForeColor = Color.FromArgb(100, 100, 100),
                Margin = new Padding(3, 14, 3, 3)
            };
            AddRow(t, "Безопасность", safety);

            page.Controls.Add(t);
            return page;
        }

        private TabPage BuildTargetsTab()
        {
            var page = NewTab("Сайты");
            var host = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 6, 0, 0) };
            var add = new Button { Text = "Добавить", Width = 96, Height = 26 };
            var remove = new Button { Text = "Удалить", Width = 96, Height = 26 };
            var up = new Button { Text = "Выше", Width = 80, Height = 26 };
            var down = new Button { Text = "Ниже", Width = 80, Height = 26 };
            tools.Controls.AddRange(new Control[] { add, remove, up, down });

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Вкл", FillWeight = 7 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Имя", FillWeight = 18 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "URL", FillWeight = 40 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Таймаут, мс", FillWeight = 12 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Коды (через запятую)", FillWeight = 13 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Шаблон в теле", FillWeight = 18 });

            add.Click += (s, e) => _grid.Rows.Add(true, "", "https://", 6000, "", "");
            remove.Click += (s, e) => { foreach (DataGridViewRow row in _grid.SelectedRows) if (!row.IsNewRow) _grid.Rows.Remove(row); };
            up.Click += (s, e) => MoveRow(-1);
            down.Click += (s, e) => MoveRow(1);

            host.Controls.Add(tools, 0, 0);
            host.Controls.Add(_grid, 0, 1);
            page.Controls.Add(host);
            return page;
        }

        private void MoveRow(int delta)
        {
            if (_grid.SelectedRows.Count == 0) return;
            int index = _grid.SelectedRows[0].Index;
            int target = index + delta;
            if (target < 0 || target >= _grid.Rows.Count) return;

            var values = new object[_grid.Columns.Count];
            for (int c = 0; c < _grid.Columns.Count; c++) values[c] = _grid.Rows[index].Cells[c].Value;

            _grid.Rows.RemoveAt(index);
            _grid.Rows.Insert(target, 1);
            for (int c = 0; c < _grid.Columns.Count; c++) _grid.Rows[target].Cells[c].Value = values[c];
            _grid.Rows[target].Selected = true;
        }

        private TabPage BuildPolicyTab()
        {
            var page = NewTab("Политика");
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(12, 10, 12, 10) };
            for (int i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, i % 2 == 0 ? 32 : 18));

            _quorum = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            _quorum.Items.AddRange(new object[] { "All", "Majority", "Any" });
            _threshold = Num(_s.Policy.FailureThreshold, 1, 20, 80);
            _interval = Num(_s.Policy.ProbeIntervalSec, 1, 3600, 90);
            _correlation = Num(_s.Policy.CorrelationWindowSec, 0, 3600, 90);
            _retryDelay = Num(_s.Policy.RetryDelayMs, 0, 60000, 90);
            _recoveryTimeout = Num(_s.Policy.RecoveryTimeoutSec, 5, 600, 90);
            _recoverySuccesses = Num(_s.Policy.RecoverySuccesses, 1, 10, 80);
            _minGap = Num(_s.Policy.MinRestartGapSec, 5, 3600, 90);
            _maxGap = Num(_s.Policy.MaxRestartGapSec, 5, 86400, 90);
            _maxPerHour = Num(_s.Policy.MaxRestartsPerHour, 1, 100, 80);
            _failedBeforeCircuit = Num(_s.Policy.FailedRestartsBeforeCircuit, 1, 100, 80);
            _cooldown = Num(_s.Policy.CircuitCooldownSec, 0, 86400, 90);
            _jitter = Num(_s.Policy.JitterPercent, 0, 50, 80);
            _connectCooldown = Num(_s.Policy.ConnectCooldownSec, 0, 600, 80);
            _alertThrottle = Num(_s.Policy.AlertThrottleSec, 0, 3600, 90);

            AddPair(t, "Кворум отказов", _quorum, "Порог неудач подряд", _threshold);
            AddPair(t, "Интервал проверки, с", _interval, "Окно корреляции, с", _correlation);
            AddPair(t, "Пауза перед повтором, мс", _retryDelay, "Ожидание восстановления, с", _recoveryTimeout);
            AddPair(t, "Успехов для восстановления", _recoverySuccesses, "Мин. пауза перезапуска, с", _minGap);
            AddPair(t, "Макс. пауза перезапуска, с", _maxGap, "Перезапусков в час", _maxPerHour);
            AddPair(t, "Неудач до предохранителя", _failedBeforeCircuit, "Остывание, с", _cooldown);
            AddPair(t, "Джиттер, %", _jitter, "Пауза между connect, с", _connectCooldown);
            AddPair(t, "Повтор тревоги, с", _alertThrottle, "", new Label());

            var hint = new Label
            {
                Text = "Кворум All: перезапуск только когда недоступны все сайты. Одиночный отказ — предупреждение.\n" +
                       "Пауза между перезапусками растёт экспоненциально; после серии неудач срабатывает предохранитель.",
                AutoSize = true,
                ForeColor = Color.FromArgb(100, 100, 100)
            };
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.SetColumnSpan(hint, 4);
            hint.Margin = new Padding(3, 16, 3, 3);
            t.Controls.Add(hint, 0, row);

            page.Controls.Add(t);
            return page;
        }

        private static void AddPair(TableLayoutPanel t, string l1, Control c1, string l2, Control c2)
        {
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = l1, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) }, 0, row);
            c1.Anchor = AnchorStyles.Left;
            t.Controls.Add(c1, 1, row);
            t.Controls.Add(new Label { Text = l2, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 6, 3, 6) }, 2, row);
            c2.Anchor = AnchorStyles.Left;
            t.Controls.Add(c2, 3, row);
        }

        private TabPage BuildLogTab()
        {
            var page = NewTab("Журнал");
            var t = NewGrid();

            _logLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            _logLevel.Items.AddRange(new object[] { "Error", "Warn", "Info", "Debug" });
            AddRow(t, "Уровень", _logLevel);

            _logSizeMb = Num(2, 1, 100, 90);
            _logKeep = Num(5, 1, 50, 90);
            _jsonl = new CheckBox { Text = "Писать машиночитаемый events.jsonl", AutoSize = true };
            AddRow(t, "Максимальный размер, МБ", _logSizeMb);
            AddRow(t, "Хранить файлов", _logKeep);
            AddRow(t, "", _jsonl);

            var open = new Button { Text = "Открыть папку журнала", Width = 180, Height = 28 };
            open.Click += (s, e) =>
            {
                string dir = _s.Logging.Directory;
                if (string.IsNullOrWhiteSpace(dir)) dir = _paths.LogDir;
                try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
            };
            AddRow(t, "Файлы", open);

            page.Controls.Add(t);
            return page;
        }

        private void LoadValues()
        {
            _monitorOnly.Checked = !_s.IsAutoRestart;
            _autoRestart.Checked = _s.IsAutoRestart;
            _autostart.Checked = _s.Ui.Autostart;
            _startMinimized.Checked = _s.Ui.StartMinimized;
            _closeToTray.Checked = _s.Ui.CloseToTray;
            _confirmRestart.Checked = _s.Ui.ConfirmRestart;
            _balloon.Checked = _s.Notifications.Balloon;
            _sound.Checked = _s.Notifications.Sound;

            if (!string.IsNullOrWhiteSpace(_s.Vpn.ProfileName) && !_profiles.Items.Contains(_s.Vpn.ProfileName))
                _profiles.Items.Add(_s.Vpn.ProfileName);
            _profiles.Text = _s.Vpn.ProfileName;
            _configPath.Text = _s.Vpn.ConfigPath;
            _guiPath.Text = _s.Vpn.OpenVpnGuiPath;
            _connectTimeout.Value = Clamp(_connectTimeout, _s.Vpn.ConnectTimeoutSec);
            _commandTimeout.Value = Clamp(_commandTimeout, _s.Vpn.CommandTimeoutSec);
            _connectOnStart.Checked = _s.Vpn.ConnectOnStart;
            _readLog.Checked = _s.Vpn.ReadOpenVpnLog;
            _hardRestart.Checked = _s.Policy.HardRestartAfterFailedSoft;
            _requireTunnel.Checked = _s.Policy.RequireTunnelEvidence;
            _restartService.Checked = _s.Policy.RestartOnServiceOutage;

            foreach (var target in _s.Targets)
            {
                _grid.Rows.Add(target.Enabled, target.Name, target.Url, target.TimeoutMs,
                    target.ExpectedStatus == null ? "" : string.Join(",", target.ExpectedStatus),
                    target.BodyPattern);
            }

            if (!string.IsNullOrWhiteSpace(_s.Policy.Quorum) && !_quorum.Items.Contains(_s.Policy.Quorum))
                _quorum.Items.Add(_s.Policy.Quorum);
            _quorum.Text = _s.Policy.Quorum;
            if (_quorum.SelectedIndex < 0) _quorum.Text = "All";
            _threshold.Value = Clamp(_threshold, _s.Policy.FailureThreshold);
            _interval.Value = Clamp(_interval, _s.Policy.ProbeIntervalSec);
            _correlation.Value = Clamp(_correlation, _s.Policy.CorrelationWindowSec);
            _retryDelay.Value = Clamp(_retryDelay, _s.Policy.RetryDelayMs);
            _recoveryTimeout.Value = Clamp(_recoveryTimeout, _s.Policy.RecoveryTimeoutSec);
            _recoverySuccesses.Value = Clamp(_recoverySuccesses, _s.Policy.RecoverySuccesses);
            _minGap.Value = Clamp(_minGap, _s.Policy.MinRestartGapSec);
            _maxGap.Value = Clamp(_maxGap, _s.Policy.MaxRestartGapSec);
            _maxPerHour.Value = Clamp(_maxPerHour, _s.Policy.MaxRestartsPerHour);
            _failedBeforeCircuit.Value = Clamp(_failedBeforeCircuit, _s.Policy.FailedRestartsBeforeCircuit);
            _cooldown.Value = Clamp(_cooldown, _s.Policy.CircuitCooldownSec);
            _jitter.Value = Clamp(_jitter, _s.Policy.JitterPercent);
            _connectCooldown.Value = Clamp(_connectCooldown, _s.Policy.ConnectCooldownSec);
            _alertThrottle.Value = Clamp(_alertThrottle, _s.Policy.AlertThrottleSec);

            _logLevel.Text = _s.Logging.Level;
            if (_logLevel.SelectedIndex < 0) _logLevel.Text = "Info";
            _logSizeMb.Value = Clamp(_logSizeMb, (int)Math.Max(1, _s.Logging.MaxFileBytes / (1024 * 1024)));
            _logKeep.Value = Clamp(_logKeep, _s.Logging.KeepFiles);
            _jsonl.Checked = _s.Logging.Jsonl;
        }

        private static decimal Clamp(NumericUpDown n, int value) =>
            Math.Max(n.Minimum, Math.Min(n.Maximum, value));

        private void BrowseProfile()
        {
            using (var dialog = new OpenFileDialog { Filter = "OpenVPN profile (*.ovpn)|*.ovpn|All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _configPath.Text = dialog.FileName;
                string name = Path.GetFileNameWithoutExtension(dialog.FileName);
                if (!_profiles.Items.Contains(name)) _profiles.Items.Add(name);
                _profiles.Text = name;
            }
        }

        private void BrowseGui()
        {
            using (var dialog = new OpenFileDialog { Filter = "openvpn-gui.exe|openvpn-gui.exe|Executable (*.exe)|*.exe" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) _guiPath.Text = dialog.FileName;
            }
        }

        private bool BuildResult()
        {
            // Commit an open cell edit; otherwise Save may read the previous value.
            try { _grid.EndEdit(); } catch { }

            _s.Mode = _autoRestart.Checked ? "AutoRestart" : "MonitorOnly";
            _s.Ui.Autostart = _autostart.Checked;
            _s.Ui.StartMinimized = _startMinimized.Checked;
            _s.Ui.CloseToTray = _closeToTray.Checked;
            _s.Ui.ConfirmRestart = _confirmRestart.Checked;
            _s.Notifications.Balloon = _balloon.Checked;
            _s.Notifications.Sound = _sound.Checked;

            string profileName = (_profiles.Text ?? string.Empty).Trim();
            string configured = (_configPath.Text ?? string.Empty).Trim();

            // A path without a profile name is valid (the backend derives the name);
            // do not erase it just because the combo box is empty.
            if (profileName.Length == 0 && configured.Length > 0)
            {
                profileName = Path.GetFileNameWithoutExtension(configured);
                _profiles.Text = profileName;
            }

            _s.Vpn.ProfileName = profileName;
            if (!string.IsNullOrEmpty(configured) &&
                !string.Equals(Path.GetFileNameWithoutExtension(configured), profileName, StringComparison.OrdinalIgnoreCase))
                configured = string.Empty;
            _s.Vpn.ConfigPath = configured;
            _s.Vpn.OpenVpnGuiPath = (_guiPath.Text ?? string.Empty).Trim();
            _s.Vpn.ConnectTimeoutSec = (int)_connectTimeout.Value;
            _s.Vpn.CommandTimeoutSec = (int)_commandTimeout.Value;
            _s.Vpn.ConnectOnStart = _connectOnStart.Checked;
            _s.Vpn.ReadOpenVpnLog = _readLog.Checked;
            _s.Policy.HardRestartAfterFailedSoft = _hardRestart.Checked;
            _s.Policy.RequireTunnelEvidence = _requireTunnel.Checked;
            _s.Policy.RestartOnServiceOutage = _restartService.Checked;

            _s.Targets.Clear();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                string url = Convert.ToString(row.Cells[2].Value ?? string.Empty).Trim();
                string name = Convert.ToString(row.Cells[1].Value ?? string.Empty).Trim();
                if (url.Length == 0) continue;
                bool enabled = row.Cells[0].Value is bool b ? b : true;

                int timeout = 6000;
                int.TryParse(Convert.ToString(row.Cells[3].Value ?? string.Empty), out timeout);
                if (timeout <= 0) timeout = 6000;

                int[] codes = null;
                string codesText = Convert.ToString(row.Cells[4].Value ?? string.Empty).Trim();
                if (codesText.Length > 0)
                {
                    var list = new List<int>();
                    foreach (string part in codesText.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        if (int.TryParse(part, out int code)) list.Add(code);
                    if (list.Count > 0) codes = list.ToArray();
                }

                _s.Targets.Add(new TargetSettings
                {
                    Enabled = enabled,
                    Name = name,
                    Url = url,
                    TimeoutMs = timeout,
                    RetryTimeoutMs = Math.Min(4000, timeout),
                    ExpectedStatus = codes,
                    BodyPattern = Convert.ToString(row.Cells[5].Value ?? string.Empty).Trim()
                });
            }

            _s.Policy.Quorum = string.IsNullOrWhiteSpace(_quorum.Text) ? "All" : _quorum.Text.Trim();
            _s.Policy.FailureThreshold = (int)_threshold.Value;
            _s.Policy.ProbeIntervalSec = (int)_interval.Value;
            _s.Policy.CorrelationWindowSec = (int)_correlation.Value;
            _s.Policy.RetryDelayMs = (int)_retryDelay.Value;
            _s.Policy.RecoveryTimeoutSec = (int)_recoveryTimeout.Value;
            _s.Policy.RecoverySuccesses = (int)_recoverySuccesses.Value;
            _s.Policy.MinRestartGapSec = (int)_minGap.Value;
            _s.Policy.MaxRestartGapSec = (int)_maxGap.Value;
            _s.Policy.MaxRestartsPerHour = (int)_maxPerHour.Value;
            _s.Policy.FailedRestartsBeforeCircuit = (int)_failedBeforeCircuit.Value;
            _s.Policy.CircuitCooldownSec = (int)_cooldown.Value;
            _s.Policy.JitterPercent = (int)_jitter.Value;
            _s.Policy.ConnectCooldownSec = (int)_connectCooldown.Value;
            _s.Policy.AlertThrottleSec = (int)_alertThrottle.Value;

            _s.Logging.Level = _logLevel.Text;
            _s.Logging.MaxFileBytes = (long)_logSizeMb.Value * 1024 * 1024;
            _s.Logging.KeepFiles = (int)_logKeep.Value;
            _s.Logging.Jsonl = _jsonl.Checked;

            _s.Normalize();
            return !SettingsValidator.Validate(_s).Any(i => i.Severity == IssueSeverity.Error);
        }

        private void ShowValidation(bool closeOnSuccess)
        {
            bool ok = BuildResult();
            var issues = SettingsValidator.Validate(_s);
            if (ok)
            {
                var warnings = issues.Where(i => i.Severity == IssueSeverity.Warning).Select(i => i.ToString()).ToList();
                MessageBox.Show(this,
                    warnings.Count == 0 ? "Настройки корректны." : "Настройки корректны.\n\n" + string.Join(Environment.NewLine, warnings.Take(10)),
                    "Проверка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (closeOnSuccess) { Result = _s; DialogResult = DialogResult.OK; Close(); }
            }
            else
            {
                var errors = issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.ToString()).ToList();
                MessageBox.Show(this, string.Join(Environment.NewLine, errors.Take(12)),
                    "Ошибки в настройках", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveAndClose()
        {
            if (!BuildResult())
            {
                var errors = SettingsValidator.Validate(_s).Where(i => i.Severity == IssueSeverity.Error).Select(i => i.ToString()).ToList();
                MessageBox.Show(this, string.Join(Environment.NewLine, errors.Take(12)),
                    "Ошибки в настройках", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = _s;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OpenJson()
        {
            try
            {
                string path = _paths.SettingsPath;
                if (!File.Exists(path))
                {
                    MessageBox.Show(this, "Файл настроек ещё не создан. Сохраните настройки.", "RestartVPN",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "RestartVPN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
