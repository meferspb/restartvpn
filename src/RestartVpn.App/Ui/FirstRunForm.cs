using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RestartVpn.Core.Settings;
using RestartVpn.Core.Vpn;

namespace RestartVpn.App.Ui
{
    /// <summary>Minimal first-run setup: profile + targets + explicit auto-restart consent.</summary>
    public sealed class FirstRunForm : Form
    {
        public AppSettings Result { get; private set; }

        private readonly ComboBox _profiles;
        private readonly TextBox _targets;
        private readonly CheckBox _auto;
        private readonly Label _hint;
        private readonly AppSettings _current;

        public FirstRunForm(AppSettings current, IReadOnlyList<VpnProfileInfo> profiles)
        {
            _current = current;
            Text = "RestartVPN — первый запуск";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(600, 470);
            Font = new Font("Segoe UI", 9F);

            var title = new Label
            {
                Text = "Настроим мониторинг",
                Font = new Font("Segoe UI Semibold", 14F),
                AutoSize = true,
                Location = new Point(20, 16)
            };

            var intro = new Label
            {
                Text = "Приложение проверяет сайты и при необходимости перезапускает\n" +
                       "только выбранный профиль OpenVPN. Другие подключения не затрагиваются.",
                AutoSize = true,
                Location = new Point(22, 52),
                ForeColor = Color.FromArgb(70, 70, 70)
            };

            var profileLabel = new Label { Text = "Профиль OpenVPN:", Location = new Point(24, 100), AutoSize = true };
            _profiles = new ComboBox
            {
                Location = new Point(24, 122),
                Width = 540,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var p in profiles) _profiles.Items.Add(new ProfileItem(p));
            if (current != null && !string.IsNullOrWhiteSpace(current.Vpn.ProfileName))
            {
                for (int i = 0; i < _profiles.Items.Count; i++)
                {
                    var item = (ProfileItem)_profiles.Items[i];
                    if (string.Equals(item.Profile.Name, current.Vpn.ProfileName, StringComparison.OrdinalIgnoreCase))
                    { _profiles.SelectedIndex = i; break; }
                }
            }
            if (_profiles.SelectedIndex < 0 && _profiles.Items.Count > 0) _profiles.SelectedIndex = 0;

            var targetsLabel = new Label { Text = "Сайты (по одному адресу в строке):", Location = new Point(24, 158), AutoSize = true };
            _targets = new TextBox
            {
                Location = new Point(24, 180),
                Width = 540,
                Height = 120,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Text = current != null
                    ? string.Join(Environment.NewLine, current.Targets.Where(t => t.Enabled).Select(t => t.Url))
                    : string.Empty
            };

            _auto = new CheckBox
            {
                Text = "Разрешить автоматический перезапуск VPN, когда все сайты недоступны",
                Location = new Point(24, 312),
                AutoSize = true,
                Checked = current != null && current.IsAutoRestart
            };

            _hint = new Label
            {
                Text = "По умолчанию приложение только наблюдает и предупреждает.",
                Location = new Point(44, 336),
                AutoSize = true,
                ForeColor = Color.FromArgb(120, 120, 120)
            };

            var save = new Button
            {
                Text = "Сохранить и продолжить",
                Location = new Point(336, 390),
                Size = new Size(228, 36)
            };
            save.Click += (s, e) => SaveAndClose();

            var cancel = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Location = new Point(240, 390),
                Size = new Size(84, 36)
            };

            Controls.AddRange(new Control[] { title, intro, profileLabel, _profiles, targetsLabel, _targets, _auto, _hint, save, cancel });
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void SaveAndClose()
        {
            var profile = _profiles.SelectedItem as ProfileItem;
            bool auto = _auto.Checked;

            if (auto && profile == null)
            {
                MessageBox.Show(this, "Для автоматического перезапуска выберите профиль OpenVPN.",
                    "RestartVPN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var urls = _targets.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (urls.Count == 0)
            {
                MessageBox.Show(this, "Добавьте хотя бы один сайт.",
                    "RestartVPN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Start from the loaded settings: this preserves installer choices such as
            // Ui.Autostart, which the controller read from the registry before this dialog.
            var settings = _current != null ? _current.Clone() : new AppSettings();
            settings.Mode = auto ? "AutoRestart" : "MonitorOnly";
            settings.Targets.Clear();
            if (profile != null)
            {
                settings.Vpn.ProfileName = profile.Profile.Name;
                settings.Vpn.ConfigPath = profile.Profile.Path;
            }

            foreach (string url in urls)
            {
                string name;
                try { name = new Uri(url).Host.Replace("www.", string.Empty); }
                catch { name = url; }
                settings.Targets.Add(new TargetSettings { Name = name, Url = url });
            }

            // The user just finished setup: show the main window instead of hiding in the tray.
            settings.Ui.StartMinimized = false;
            settings.Normalize();
            var issues = SettingsValidator.Validate(settings);
            var errors = issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors.Select(e => e.ToString())),
                    "Проверьте настройки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = settings;
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class ProfileItem
        {
            public ProfileItem(VpnProfileInfo profile) { Profile = profile; }
            public VpnProfileInfo Profile { get; }
            public override string ToString() => Profile.Name + "  —  " + Profile.Path;
        }
    }
}
