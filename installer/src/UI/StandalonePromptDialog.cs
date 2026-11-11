using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace wb.Installer
{
    /// <summary>
    /// Dark-themed dialog displayed when an existing standalone installation is detected.
    /// Allows the user to choose whether to overwrite application files, clear user data,
    /// or launch existing components.
    /// </summary>
    public class StandalonePromptDialog : Form
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

        private CheckBox _chkOverwrite;
        private CheckBox _chkClearData;
        private Button _btnLaunch;
        private Button _btnCancel;

        /// <summary>
        /// Optional delegate for intercepting clear data confirmation (useful for unit testing).
        /// If null, a standard MessageBox is shown.
        /// </summary>
        public Func<bool> ConfirmClearDataFunc { get; set; }

        public bool OverwriteFiles
        {
            get { return _chkOverwrite != null && _chkOverwrite.Checked; }
            set { if (_chkOverwrite != null) _chkOverwrite.Checked = value; }
        }

        public bool ClearUserData
        {
            get { return _chkClearData != null && _chkClearData.Checked; }
            set { if (_chkClearData != null) _chkClearData.Checked = value; }
        }

        public string LaunchButtonText
        {
            get { return _btnLaunch != null ? _btnLaunch.Text : string.Empty; }
        }

        public void PerformLaunch()
        {
            if (_chkClearData != null && _chkClearData.Checked)
            {
                if (!ConfirmClearData())
                {
                    return;
                }
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        public StandalonePromptDialog(string standalonePath, InstallerConfig config = null)
        {
            config = config ?? InstallerConfig.Current;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            InitializeComponent(standalonePath, config);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int useDarkMode = 1;
                int hr = DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
                if (hr != 0)
                {
                    DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                }
            }
            catch { }
        }

        private void InitializeComponent(string standalonePath, InstallerConfig config)
        {
            this.Text = string.Format("{0} Standalone Mode ({1})", config.AppName, InstallerEngine.CurrentArchitecture);
            this.ClientSize = new Size(480, 196);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = UIThemeTokens.FormBackground;
            this.ForeColor = UIThemeTokens.TextPrimary;
            this.Font = UIThemeTokens.FontDefault;
            this.ShowInTaskbar = false;

            try
            {
                Icon appIco = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIco != null) this.Icon = appIco;
            }
            catch { }

            // Header title
            Label titleLbl = new Label
            {
                Text = "Existing Standalone Found",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = UIThemeTokens.TextPrimary,
                Location = new Point(20, 16),
                AutoSize = true
            };
            this.Controls.Add(titleLbl);

            // Path container card
            Panel pathCard = new Panel
            {
                Location = new Point(20, 44),
                Size = new Size(440, 28),
                BackColor = UIThemeTokens.CardBackground
            };
            pathCard.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UIThemeTokens.BorderColor, 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, pathCard.Width - 1, pathCard.Height - 1);
                }
            };
            Label pathLbl = new Label
            {
                Text = standalonePath,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                ForeColor = UIThemeTokens.TextSecondary,
                Location = new Point(8, 6),
                Size = new Size(424, 16),
                AutoEllipsis = true
            };
            pathCard.Controls.Add(pathLbl);
            this.Controls.Add(pathCard);

            // Checkbox 1: Overwrite application files
            _chkOverwrite = new CheckBox
            {
                Text = "Overwrite application files",
                Font = UIThemeTokens.FontDefault,
                ForeColor = UIThemeTokens.TextPrimary,
                Location = new Point(22, 80),
                Size = new Size(436, 22),
                Checked = true,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _chkOverwrite.CheckedChanged += (s, e) => UpdateState();
            this.Controls.Add(_chkOverwrite);

            // Checkbox 2: Clear existing user data
            _chkClearData = new CheckBox
            {
                Text = "Clear existing user data",
                Font = UIThemeTokens.FontDefault,
                ForeColor = UIThemeTokens.TextPrimary,
                Location = new Point(22, 106),
                Size = new Size(436, 22),
                Checked = false,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _chkClearData.CheckedChanged += (s, e) => UpdateState();
            this.Controls.Add(_chkClearData);

            // Separator line
            Panel sep = new Panel
            {
                Location = new Point(20, 138),
                Size = new Size(440, 1),
                BackColor = UIThemeTokens.BorderColor
            };
            this.Controls.Add(sep);

            // Cancel button
            _btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(90, 32),
                Location = new Point(236, 150),
                FlatStyle = FlatStyle.Flat,
                BackColor = UIThemeTokens.CardBackground,
                ForeColor = UIThemeTokens.TextPrimary,
                Font = UIThemeTokens.FontDefault,
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel,
                UseMnemonic = false
            };
            _btnCancel.FlatAppearance.BorderColor = UIThemeTokens.BorderColor;
            _btnCancel.FlatAppearance.BorderSize = 1;
            _btnCancel.FlatAppearance.MouseOverBackColor = UIThemeTokens.CardHover;
            _btnCancel.FlatAppearance.MouseDownBackColor = UIThemeTokens.CardPressed;
            this.Controls.Add(_btnCancel);

            // Launch / Continue button
            _btnLaunch = new Button
            {
                Size = new Size(130, 32),
                Location = new Point(330, 150),
                FlatStyle = FlatStyle.Flat,
                BackColor = BrandTheme.AccentColor,
                ForeColor = BrandTheme.AccentTextColor,
                Font = UIThemeTokens.FontDefaultBold,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnLaunch.FlatAppearance.BorderSize = 0;
            _btnLaunch.FlatAppearance.MouseOverBackColor = BrandTheme.AccentHoverColor;
            _btnLaunch.FlatAppearance.MouseDownBackColor = BrandTheme.AccentPressedColor;
            _btnLaunch.Click += (s, e) => PerformLaunch();
            this.Controls.Add(_btnLaunch);

            this.AcceptButton = _btnLaunch;
            this.CancelButton = _btnCancel;

            UpdateState();
        }

        private bool ConfirmClearData()
        {
            if (ConfirmClearDataFunc != null)
            {
                return ConfirmClearDataFunc();
            }

            DialogResult result = MessageBox.Show(
                this,
                "Are you sure you want to clear existing user data?",
                "Confirm",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            return result == DialogResult.Yes;
        }

        private void UpdateState()
        {
            bool overwrite = _chkOverwrite.Checked;
            bool clearData = _chkClearData.Checked;

            if (overwrite && clearData)
            {
                _btnLaunch.Text = "Reset & Launch";
            }
            else if (overwrite)
            {
                _btnLaunch.Text = "Update & Launch";
            }
            else if (clearData)
            {
                _btnLaunch.Text = "Clear & Launch";
            }
            else
            {
                _btnLaunch.Text = "Launch Existing";
            }
        }
    }
}
