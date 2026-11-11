using System;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Proxy
{
    public class ProxySettingsDialog : Form
    {
        private RadioButton _rbSystem;
        private RadioButton _rbDirect;
        private RadioButton _rbAutoDetect;
        private RadioButton _rbPac;
        private RadioButton _rbManual;

        // PAC controls
        private Panel _pacPanel;
        private TextBox _pacUrlTxt;

        // Manual controls
        private Panel _manualPanel;
        private ComboBox _schemeCombo;
        private TextBox _hostTxt;
        private TextBox _portTxt;
        private TextBox _bypassTxt;
        private CheckBox _authChk;
        private TextBox _userTxt;
        private TextBox _passTxt;

        // Footer
        private Label _statusLabel;
        private ToolTip _statusToolTip;
        private Button _testBtn;
        private Button _saveBtn;
        private Button _cancelBtn;

        public ProxySettingsDialog()
        {
            InitializeComponent();
            UITheme.ApplyModalDialog(this, AppStrings.Proxy.DialogTitle, new Size(520, 640), new Size(480, 560));
            this.KeyPreview = true;

            LoadCurrentConfig();
        }

        public static DialogResult ShowProxySettings(IWin32Window owner = null)
        {
            using (ProxySettingsDialog dlg = new ProxySettingsDialog())
            {
                if (owner != null)
                {
                    return dlg.ShowDialog(owner);
                }
                return dlg.ShowDialog();
            }
        }

        private void InitializeComponent()
        {
            this.Text = AppStrings.Proxy.DialogTitle;
            this.Size = new Size(520, 620);
            this.BackColor = UITheme.FormBackground;
            this.Font = UITheme.FontDefault;
            this.Padding = new Padding(0);

            // 1. Header Panel
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 64;
            headerPanel.Padding = new Padding(24, 14, 24, 0);

            Label titleLabel = new Label();
            titleLabel.UseMnemonic = false;
            titleLabel.Text = AppStrings.Proxy.HeaderTitle;
            titleLabel.Font = new Font("Segoe UI", 13.5F, FontStyle.Bold);
            titleLabel.ForeColor = UITheme.TextPrimary;
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(24, 12);

            Label subTitleLabel = new Label();
            subTitleLabel.UseMnemonic = false;
            subTitleLabel.Text = AppStrings.Proxy.HeaderSubtitle;
            subTitleLabel.Font = UITheme.FontSmall;
            subTitleLabel.ForeColor = UITheme.TextSecondary;
            subTitleLabel.AutoSize = true;
            subTitleLabel.Location = new Point(25, 38);

            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(subTitleLabel);

            // Separator under header
            Panel headerSep = new Panel();
            headerSep.Dock = DockStyle.Top;
            headerSep.Height = 1;
            headerSep.BackColor = MaterialColors.Separator;

            // 2. Footer Panel
            Panel footerPanel = new Panel();
            footerPanel.Dock = DockStyle.Bottom;
            footerPanel.Height = 104;
            footerPanel.Padding = new Padding(24, 8, 24, 8);

            Panel footerSep = new Panel();
            footerSep.Dock = DockStyle.Top;
            footerSep.Height = 1;
            footerSep.BackColor = MaterialColors.Separator;
            footerPanel.Controls.Add(footerSep);

            // Row 1: Test connection & Status
            _testBtn = new Button();
            _testBtn.UseMnemonic = false;
            _testBtn.Text = AppStrings.Proxy.BtnTest;
            _testBtn.Size = new Size(116, 32);
            _testBtn.Location = new Point(24, 12);
            UITheme.ApplyButton(_testBtn, isPrimary: false);
            _testBtn.Click += async (s, e) => await OnTestClickedAsync();
            footerPanel.Controls.Add(_testBtn);

            _statusToolTip = new ToolTip();
            _statusToolTip.InitialDelay = 300;
            _statusToolTip.ReshowDelay = 100;

            _statusLabel = new Label();
            _statusLabel.Text = string.Empty;
            _statusLabel.Font = UITheme.FontSmall;
            _statusLabel.ForeColor = UITheme.TextSecondary;
            _statusLabel.AutoSize = false;
            _statusLabel.Location = new Point(148, 12);
            _statusLabel.Size = new Size(346, 32);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.AutoEllipsis = true;
            footerPanel.Controls.Add(_statusLabel);

            // Row 2: Sub-divider between test area and action buttons
            Panel actionDivider = new Panel();
            actionDivider.Location = new Point(24, 52);
            actionDivider.Size = new Size(footerPanel.Width - 48, 1);
            actionDivider.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            actionDivider.BackColor = MaterialColors.Separator;
            footerPanel.Controls.Add(actionDivider);

            // Row 3: Cancel and Save & Apply
            _cancelBtn = new Button();
            _cancelBtn.UseMnemonic = false;
            _cancelBtn.Text = AppStrings.Proxy.BtnCancel;
            _cancelBtn.Size = new Size(88, 32);
            _cancelBtn.Location = new Point(footerPanel.Width - 228, 60);
            _cancelBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            UITheme.ApplyButton(_cancelBtn, isPrimary: false);
            _cancelBtn.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            footerPanel.Controls.Add(_cancelBtn);

            _saveBtn = new Button();
            _saveBtn.UseMnemonic = false;
            _saveBtn.Text = AppStrings.Proxy.BtnApply;
            _saveBtn.Size = new Size(108, 32);
            _saveBtn.Location = new Point(footerPanel.Width - 132, 60);
            _saveBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            UITheme.ApplyButton(_saveBtn, isPrimary: true);
            _saveBtn.Click += (s, e) => SaveAndClose();
            footerPanel.Controls.Add(_saveBtn);

            // 3. Main Content Panel
            Panel contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.AutoScroll = true;
            contentPanel.Padding = new Padding(24, 12, 24, 12);

            int y = 10;

            // Radio Options
            _rbSystem = CreateRadio(AppStrings.Proxy.ModeSystem, ref y, contentPanel);
            _rbDirect = CreateRadio(AppStrings.Proxy.ModeDirect, ref y, contentPanel);
            _rbAutoDetect = CreateRadio(AppStrings.Proxy.ModeAutoDetect, ref y, contentPanel);
            _rbPac = CreateRadio(AppStrings.Proxy.ModePacScript, ref y, contentPanel);

            // PAC Panel
            _pacPanel = new Panel();
            _pacPanel.Location = new Point(48, y);
            _pacPanel.Size = new Size(430, 48);
            _pacPanel.Visible = false;

            Label pacLabel = new Label();
            pacLabel.Text = AppStrings.Proxy.PacUrlLabel;
            pacLabel.Font = UITheme.FontSmall;
            pacLabel.ForeColor = UITheme.TextSecondary;
            pacLabel.Location = new Point(0, 2);
            pacLabel.AutoSize = true;

            _pacUrlTxt = new TextBox();
            _pacUrlTxt.Location = new Point(0, 20);
            _pacUrlTxt.Width = 410;
            UITheme.ApplyTextBox(_pacUrlTxt);

            _pacPanel.Controls.Add(pacLabel);
            _pacPanel.Controls.Add(_pacUrlTxt);
            contentPanel.Controls.Add(_pacPanel);
            y += 52;

            _rbManual = CreateRadio(AppStrings.Proxy.ModeManual, ref y, contentPanel);

            // Manual Panel
            _manualPanel = new Panel();
            _manualPanel.Location = new Point(48, y);
            _manualPanel.Size = new Size(430, 240);
            _manualPanel.Visible = false;

            int my = 0;

            // Row 1: Protocol, Server, Port
            Label protoLabel = new Label { Text = AppStrings.Proxy.ProtocolLabel, Font = UITheme.FontSmall, ForeColor = UITheme.TextSecondary, Location = new Point(0, my), AutoSize = true };
            _schemeCombo = new ComboBox { Location = new Point(0, my + 18), Width = 85, DropDownStyle = ComboBoxStyle.DropDownList };
            _schemeCombo.Items.AddRange(new object[] { "http", "https", "socks5", "socks4" });
            _schemeCombo.SelectedIndex = 0;
            UITheme.ApplyComboBox(_schemeCombo);

            Label hostLabel = new Label { Text = AppStrings.Proxy.ServerLabel, Font = UITheme.FontSmall, ForeColor = UITheme.TextSecondary, Location = new Point(95, my), AutoSize = true };
            _hostTxt = new TextBox { Location = new Point(95, my + 18), Width = 230 };
            UITheme.ApplyTextBox(_hostTxt);

            Label portLabel = new Label { Text = AppStrings.Proxy.PortLabel, Font = UITheme.FontSmall, ForeColor = UITheme.TextSecondary, Location = new Point(335, my), AutoSize = true };
            _portTxt = new TextBox { Location = new Point(335, my + 18), Width = 75, Text = "8080" };
            UITheme.ApplyTextBox(_portTxt);

            _manualPanel.Controls.Add(protoLabel);
            _manualPanel.Controls.Add(_schemeCombo);
            _manualPanel.Controls.Add(hostLabel);
            _manualPanel.Controls.Add(_hostTxt);
            _manualPanel.Controls.Add(portLabel);
            _manualPanel.Controls.Add(_portTxt);
            my += 52;

            // Row 2: Bypass List
            Label bypassLabel = new Label { Text = AppStrings.Proxy.BypassLabel, Font = UITheme.FontSmall, ForeColor = UITheme.TextSecondary, Location = new Point(0, my), AutoSize = true };
            _bypassTxt = new TextBox { Location = new Point(0, my + 18), Width = 410, Text = "<local>;localhost;127.0.0.1" };
            UITheme.ApplyTextBox(_bypassTxt);

            Label bypassHint = new Label { UseMnemonic = false, Text = AppStrings.Proxy.BypassHint, Font = new Font("Segoe UI", 7.5F), ForeColor = UITheme.TextMuted, Location = new Point(0, my + 44), AutoSize = true };

            _manualPanel.Controls.Add(bypassLabel);
            _manualPanel.Controls.Add(_bypassTxt);
            _manualPanel.Controls.Add(bypassHint);
            my += 66;

            // Row 3: Authentication
            _authChk = new CheckBox { Text = AppStrings.Proxy.AuthCheckbox, Font = UITheme.FontDefault, ForeColor = UITheme.TextPrimary, Location = new Point(0, my), AutoSize = true };
            UITheme.ApplyCheckBox(_authChk);
            _authChk.CheckedChanged += (s, e) => UpdateAuthFieldsState();
            _manualPanel.Controls.Add(_authChk);
            my += 28;

            Label userLabel = new Label { Text = AppStrings.Proxy.UsernameLabel, Font = UITheme.FontSmall, ForeColor = UITheme.TextSecondary, Location = new Point(0, my), AutoSize = true };
            _userTxt = new TextBox { Location = new Point(0, my + 18), Width = 195, Enabled = false };
            UITheme.ApplyTextBox(_userTxt);

            Label passLabel = new Label { Text = AppStrings.Proxy.PasswordLabel, Font = UITheme.FontSmall, ForeColor = UITheme.TextSecondary, Location = new Point(210, my), AutoSize = true };
            _passTxt = new TextBox { Location = new Point(210, my + 18), Width = 200, UseSystemPasswordChar = true, Enabled = false };
            UITheme.ApplyTextBox(_passTxt);

            _manualPanel.Controls.Add(userLabel);
            _manualPanel.Controls.Add(_userTxt);
            _manualPanel.Controls.Add(passLabel);
            _manualPanel.Controls.Add(_passTxt);

            contentPanel.Controls.Add(_manualPanel);

            // Hook Radio events
            _rbSystem.CheckedChanged += (s, e) => UpdatePanelsVisibility();
            _rbDirect.CheckedChanged += (s, e) => UpdatePanelsVisibility();
            _rbAutoDetect.CheckedChanged += (s, e) => UpdatePanelsVisibility();
            _rbPac.CheckedChanged += (s, e) => UpdatePanelsVisibility();
            _rbManual.CheckedChanged += (s, e) => UpdatePanelsVisibility();

            // Assemble Form
            this.Controls.Add(contentPanel);
            this.Controls.Add(footerPanel);
            this.Controls.Add(headerSep);
            this.Controls.Add(headerPanel);
        }

        private RadioButton CreateRadio(string text, ref int y, Panel parent)
        {
            RadioButton rb = new RadioButton();
            rb.Text = text;
            rb.Font = UITheme.FontDefault;
            rb.ForeColor = UITheme.TextPrimary;
            rb.Location = new Point(24, y);
            rb.AutoSize = true;
            UITheme.ApplyRadioButton(rb);
            parent.Controls.Add(rb);
            y += 32;
            return rb;
        }

        private void UpdatePanelsVisibility()
        {
            _pacPanel.Visible = _rbPac.Checked;
            _manualPanel.Visible = _rbManual.Checked;

            int y = 10;
            _rbSystem.Location = new Point(24, y); y += 30;
            _rbDirect.Location = new Point(24, y); y += 30;
            _rbAutoDetect.Location = new Point(24, y); y += 30;
            _rbPac.Location = new Point(24, y); y += 30;

            if (_rbPac.Checked)
            {
                _pacPanel.Location = new Point(48, y);
                y += _pacPanel.Height + 8;
            }

            _rbManual.Location = new Point(24, y); y += 30;

            if (_rbManual.Checked)
            {
                _manualPanel.Location = new Point(48, y);
                y += _manualPanel.Height + 8;
            }
        }

        private void UpdateAuthFieldsState()
        {
            bool auth = _authChk.Checked;
            _userTxt.Enabled = auth;
            _passTxt.Enabled = auth;
        }

        private void LoadCurrentConfig()
        {
            ProxyConfig cfg = AppServices.Proxy.CurrentConfig;

            switch (cfg.Mode)
            {
                case ProxyMode.Direct:
                    _rbDirect.Checked = true;
                    break;
                case ProxyMode.AutoDetect:
                    _rbAutoDetect.Checked = true;
                    break;
                case ProxyMode.PacScript:
                    _rbPac.Checked = true;
                    break;
                case ProxyMode.Manual:
                    _rbManual.Checked = true;
                    break;
                case ProxyMode.System:
                default:
                    _rbSystem.Checked = true;
                    break;
            }

            _pacUrlTxt.Text = cfg.PacUrl ?? string.Empty;

            string scheme = string.IsNullOrEmpty(cfg.Scheme) ? "http" : cfg.Scheme.ToLowerInvariant();
            int idx = _schemeCombo.Items.IndexOf(scheme);
            _schemeCombo.SelectedIndex = idx >= 0 ? idx : 0;

            _hostTxt.Text = cfg.Host ?? string.Empty;
            _portTxt.Text = cfg.Port > 0 ? cfg.Port.ToString() : "8080";
            _bypassTxt.Text = string.IsNullOrEmpty(cfg.BypassList) ? "<local>;localhost;127.0.0.1" : cfg.BypassList;

            _authChk.Checked = cfg.UseAuth;
            _userTxt.Text = cfg.Username ?? string.Empty;
            _passTxt.Text = cfg.Password ?? string.Empty;

            UpdatePanelsVisibility();
            UpdateAuthFieldsState();
        }

        private ProxyConfig CollectConfig()
        {
            ProxyConfig cfg = new ProxyConfig();
            cfg.IsEnabled = true;

            if (_rbDirect.Checked) cfg.Mode = ProxyMode.Direct;
            else if (_rbAutoDetect.Checked) cfg.Mode = ProxyMode.AutoDetect;
            else if (_rbPac.Checked) cfg.Mode = ProxyMode.PacScript;
            else if (_rbManual.Checked) cfg.Mode = ProxyMode.Manual;
            else cfg.Mode = ProxyMode.System;

            cfg.PacUrl = _pacUrlTxt.Text.Trim();

            cfg.Scheme = _schemeCombo.SelectedItem != null ? _schemeCombo.SelectedItem.ToString() : "http";
            cfg.Host = _hostTxt.Text.Trim();

            int port;
            if (int.TryParse(_portTxt.Text.Trim(), out port))
            {
                cfg.Port = port;
            }
            else
            {
                cfg.Port = (cfg.Scheme == "socks5" || cfg.Scheme == "socks4") ? 1080 : 8080;
            }

            cfg.BypassList = _bypassTxt.Text.Trim();
            cfg.UseAuth = _authChk.Checked;
            cfg.Username = _userTxt.Text.Trim();
            cfg.Password = _passTxt.Text;

            return cfg;
        }

        private async System.Threading.Tasks.Task OnTestClickedAsync()
        {
            _testBtn.Enabled = false;
            _statusLabel.Text = AppStrings.Proxy.TestTesting;
            _statusLabel.ForeColor = UITheme.AccentBlue;
            if (_statusToolTip != null)
            {
                _statusToolTip.SetToolTip(_statusLabel, _statusLabel.Text);
            }

            ProxyConfig cfg = CollectConfig();
            var res = await AppServices.Proxy.TestConnectionAsync(cfg);

            _testBtn.Enabled = true;
            if (res.Item1)
            {
                _statusLabel.Text = string.Format(AppStrings.Proxy.TestSuccess, res.Item3);
                _statusLabel.ForeColor = UITheme.StatusSuccess;
            }
            else
            {
                _statusLabel.Text = string.Format(AppStrings.Proxy.TestFailed, res.Item2);
                _statusLabel.ForeColor = UITheme.StatusDanger;
            }

            if (_statusToolTip != null)
            {
                _statusToolTip.SetToolTip(_statusLabel, _statusLabel.Text);
            }
        }

        private void SaveAndClose()
        {
            ProxyConfig cfg = CollectConfig();
            AppServices.Proxy.SaveAndApply(cfg);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                return true;
            }
            if (keyData == Keys.Enter && !_pacUrlTxt.Focused && !_bypassTxt.Focused)
            {
                SaveAndClose();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
