using System;
using System.Drawing;
using System.Windows.Forms;
using RestartVpn.Core.Settings;

namespace RestartVpn.App.Ui
{
    public enum InstallChoice { Install, Portable, Cancel }

    public sealed class InstallForm : Form
    {
        public InstallChoice Choice { get; private set; } = InstallChoice.Cancel;
        public bool Autostart => _autostart.Checked;
        public bool DesktopShortcut => _desktop.Checked;

        private readonly CheckBox _autostart;
        private readonly CheckBox _desktop;

        public InstallForm(AppPaths paths)
        {
            Text = "Установка RestartVPN";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 300);
            Font = new Font("Segoe UI", 9F);

            var title = new Label
            {
                Text = "Установить RestartVPN?",
                Font = new Font("Segoe UI Semibold", 14F),
                AutoSize = true,
                Location = new Point(20, 18)
            };

            var text = new Label
            {
                Text = "Приложение будет установлено только для текущего пользователя,\n" +
                       "без прав администратора и без служб. OpenVPN и его профили не изменяются.\n\n" +
                       "Папка установки:",
                AutoSize = true,
                Location = new Point(22, 58),
                ForeColor = Color.FromArgb(70, 70, 70)
            };

            var path = new TextBox
            {
                Text = paths.InstallDir,
                ReadOnly = true,
                Location = new Point(24, 120),
                Width = 470,
                BackColor = Color.White
            };

            _autostart = new CheckBox
            {
                Text = "Запускать при входе в Windows (в трее)",
                Location = new Point(24, 160),
                AutoSize = true
            };

            _desktop = new CheckBox
            {
                Text = "Создать ярлык на рабочем столе",
                Location = new Point(24, 184),
                AutoSize = true,
                Checked = true
            };

            var install = new Button
            {
                Text = "Установить",
                DialogResult = DialogResult.OK,
                Location = new Point(250, 235),
                Size = new Size(120, 34)
            };
            install.Click += (s, e) => { Choice = InstallChoice.Install; Close(); };

            var portable = new Button
            {
                Text = "Портативно",
                DialogResult = DialogResult.Ignore,
                Location = new Point(376, 235),
                Size = new Size(118, 34)
            };
            portable.Click += (s, e) => { Choice = InstallChoice.Portable; Close(); };

            var cancel = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Location = new Point(160, 235),
                Size = new Size(84, 34)
            };
            cancel.Click += (s, e) => { Choice = InstallChoice.Cancel; Close(); };

            Controls.AddRange(new Control[] { title, text, path, _autostart, _desktop, install, portable, cancel });
            AcceptButton = install;
            CancelButton = cancel;
        }

        public static InstallChoice Ask(AppPaths paths, IWin32Window owner, out bool autostart, out bool desktop)
        {
            using (var form = new InstallForm(paths))
            {
                form.ShowDialog(owner);
                autostart = form.Autostart;
                desktop = form.DesktopShortcut;
                return form.Choice;
            }
        }
    }
}
