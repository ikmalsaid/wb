using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace wb.Installer
{
    /// <summary>
    /// ikmalsaid's Carbon Install System setup wizard form supporting Clean Install, Update, Reinstall,
    /// Downgrade, Portable Mode extraction, Standalone execution, and Uninstallation.
    /// Driven dynamically by InstallerConfig.
    /// </summary>
    public class SetupWizardForm : Form
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

        public InstallerConfig Config { get; private set; }

        private readonly bool _isUninstall;
        private bool _inUninstallMode = false;
        private InstalledAppInfo _installedInfo;
        private bool _isUpdate = false;
        private bool _isReinstall = false;
        private bool _isDowngrade = false;

        // Header controls
        private Panel _titleBar;
        private Label _titleLabel;
        private Button _closeBtn;
        private Button _minBtn;

        // Hero area
        private BrandLogoControl _logoControl;
        private Label _appTitleLbl;
        private Label _taglineLbl;

        // Decision cards (GPU-Z style)
        private ActionChoiceCard _cardInstall;
        private ActionChoiceCard _cardUninstallChoice;
        private ActionChoiceCard _cardStandalone;
        private ActionChoiceCard _cardPortable;
        private ActionChoiceCard _cardUninstall;
        private Label _backToChoicesLbl;

        // Advanced Options drawer
        private Label _advancedToggleLbl;
        private Panel _advancedPanel;
        private TextBox _installPathTxt;
        private Button _browseBtn;
        private CheckBox _chkDesktop;
        private CheckBox _chkStartMenu;
        private CheckBox _chkDefaultBrowser;
        private CheckBox _chkLaunch;
        private CheckBox _chkDeleteData; // uninstaller

        // Centered Progress controls
        private CircularProgressSpinner _spinner;
        private Label _progressPhaseLbl;
        private Label _progressDetailsLbl;

        // Centered Finished controls
        private Label _finishedTitleLbl;
        private Button _finishBtn;

        // EULA / License controls
        private Panel _eulaPanel;
        private Panel _eulaTextContainer;
        private TextBox _eulaTextBox;
        private CheckBox _chkAcceptEula;
        private Button _btnEulaAgree;
        private Button _btnEulaDecline;
        private Label _eulaReviewLink;
        private bool _isEulaAccepted = false;
        private bool _isReviewingEula = false;
        private int _eulaHeight = 429;

        private bool _isAdvancedExpanded = false;
        private int _normalHeight = 325;
        private int _expandedHeight = 490;
        private bool _isPortableFinished = false;
        private string _portableTargetDir = null;

        public SetupWizardForm(bool isUninstall)
            : this(isUninstall, null)
        {
        }

        public SetupWizardForm(bool isUninstall, InstallerConfig config)
        {
            this.Config = config ?? InstallerConfig.Current;
            _isUninstall = isUninstall;
            _installedInfo = InstalledAppInfo.Detect(this.Config);

            if (!_isUninstall && _installedInfo != null && _installedInfo.IsInstalled)
            {
                Version pkgVer = InstallerEngine.GetPackageVersion();
                int cmp = pkgVer.CompareTo(_installedInfo.InstalledVersion);
                if (cmp > 0)
                {
                    _isUpdate = true;
                }
                else if (cmp == 0)
                {
                    _isReinstall = true;
                }
                else
                {
                    _isDowngrade = true;
                }
            }

            this.ResizeRedraw = true;
            InitializeComponent();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            EnableNativeImmersiveDarkMode(this.Handle);
        }

        private static void EnableNativeImmersiveDarkMode(IntPtr hWnd)
        {
            try
            {
                int useDarkMode = 1;
                int hr = DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
                if (hr != 0)
                {
                    DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                }
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            string titleText = _isUninstall ? (Config.AppName + " Uninstaller") : (Config.AppName + " Setup");
            this.Text = titleText;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(520, _normalHeight);
            this.BackColor = UIThemeTokens.FormBackground;
            this.ForeColor = UIThemeTokens.TextPrimary;
            this.DoubleBuffered = true;

            try
            {
                Icon appIco = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIco != null) this.Icon = appIco;
            }
            catch { }

            // 1. Custom Title Bar
            _titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 34,
                Width = this.ClientSize.Width,
                BackColor = UIThemeTokens.FormBackground,
                Padding = new Padding(1, 1, 1, 1)
            };
            _titleBar.MouseDown += TitleBar_MouseDown;

            try
            {
                PropertyInfo prop = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    prop.SetValue(_titleBar, true, null);
                }
            }
            catch { }

            _titleBar.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UIThemeTokens.WindowBorder, 1))
                {
                    e.Graphics.DrawLine(p, 0, 0, _titleBar.Width - 1, 0);
                    e.Graphics.DrawLine(p, 0, 0, 0, _titleBar.Height - 1);
                    e.Graphics.DrawLine(p, _titleBar.Width - 1, 0, _titleBar.Width - 1, _titleBar.Height - 1);
                }
            };
            _titleBar.Resize += (s, e) => _titleBar.Invalidate();

            _titleLabel = new Label
            {
                Text = titleText,
                Font = UIThemeTokens.FontSmall,
                ForeColor = UIThemeTokens.TextSecondary,
                Location = new Point(14, 8),
                AutoSize = true,
                UseMnemonic = false
            };
            _titleLabel.MouseDown += TitleBar_MouseDown;
            _titleBar.Controls.Add(_titleLabel);

            _closeBtn = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 9f),
                ForeColor = UIThemeTokens.TextSecondary,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(34, 34),
                Dock = DockStyle.Right,
                Cursor = Cursors.Hand
            };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
            _closeBtn.Click += (s, e) => this.Close();
            _closeBtn.MouseLeave += (s, e) => _titleBar.Invalidate();

            _minBtn = new Button
            {
                Text = "—",
                Font = new Font("Segoe UI", 9f),
                ForeColor = UIThemeTokens.TextSecondary,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(34, 34),
                Dock = DockStyle.Right,
                Cursor = Cursors.Hand
            };
            _minBtn.FlatAppearance.BorderSize = 0;
            _minBtn.FlatAppearance.MouseOverBackColor = UIThemeTokens.CardHover;
            _minBtn.Click += (s, e) => this.WindowState = FormWindowState.Minimized;
            _minBtn.MouseLeave += (s, e) => _titleBar.Invalidate();

            _titleBar.Controls.Add(_minBtn);
            _titleBar.Controls.Add(_closeBtn);
            this.Controls.Add(_titleBar);

            // 2. Hero Brand Section
            _logoControl = new BrandLogoControl(Config)
            {
                Location = new Point(32, 48),
                Size = new Size(68, 38)
            };
            this.Controls.Add(_logoControl);

            _appTitleLbl = new Label
            {
                Text = Config.AppTitle,
                Font = UIThemeTokens.FontTitle,
                ForeColor = UIThemeTokens.TextPrimary,
                Location = new Point(114, 44),
                AutoSize = true,
                UseMnemonic = false
            };
            this.Controls.Add(_appTitleLbl);

            string taglineText = GetDefaultTaglineText();

            _taglineLbl = new Label
            {
                Text = taglineText,
                Font = UIThemeTokens.FontDefault,
                ForeColor = UIThemeTokens.TextSecondary,
                Location = new Point(116, 76),
                AutoSize = true,
                UseMnemonic = false
            };
            this.Controls.Add(_taglineLbl);

            if (!_isUninstall)
            {
                string primaryTitle = "Install " + Config.AppName;
                bool isPrimaryCard = true;

                if (_isUpdate)
                {
                    primaryTitle = "Update " + Config.AppName;
                }
                else if (_isReinstall)
                {
                    primaryTitle = "Reinstall " + Config.AppName;
                }
                else if (_isDowngrade)
                {
                    primaryTitle = "Downgrade " + Config.AppName;
                    isPrimaryCard = false;
                }

                _cardInstall = new ActionChoiceCard(
                    primaryTitle,
                    ActionChoiceCard.CardIconType.Download,
                    isPrimary: isPrimaryCard)
                {
                    Location = new Point(30, 114)
                };
                _cardInstall.Click += (s, e) => StartInstallFlow(isPortable: false);
                this.Controls.Add(_cardInstall);

                if (_installedInfo != null && _installedInfo.IsInstalled)
                {
                    // Existing installation detected: Card 2 offers to uninstall existing copy
                    _cardUninstallChoice = new ActionChoiceCard(
                        "Uninstall " + Config.AppName,
                        ActionChoiceCard.CardIconType.Trash,
                        isPrimary: _isDowngrade)
                    {
                        Location = new Point(30, 168)
                    };
                    _cardUninstallChoice.Click += (s, e) => SwitchToUninstallMode();
                    this.Controls.Add(_cardUninstallChoice);

                    // Card 3: Extract portable app (if supported)
                    if (Config.EnablePortableMode)
                    {
                        _cardPortable = new ActionChoiceCard(
                            "Extract Portable",
                            ActionChoiceCard.CardIconType.PortableFolder,
                            isPrimary: false)
                        {
                            Location = new Point(30, 222)
                        };
                        _cardPortable.Click += (s, e) => StartInstallFlow(isPortable: true);
                        this.Controls.Add(_cardPortable);
                    }
                }
                else
                {
                    // Clean install: Card 2 is Standalone, Card 3 is Portable
                    int nextY = 168;
                    if (Config.EnableStandaloneMode)
                    {
                        _cardStandalone = new ActionChoiceCard(
                            "Run Standalone",
                            ActionChoiceCard.CardIconType.Rocket,
                            isPrimary: false)
                        {
                            Location = new Point(30, nextY)
                        };
                        _cardStandalone.Click += (s, e) => StartStandaloneFlow();
                        this.Controls.Add(_cardStandalone);
                        nextY += 54;
                    }

                    if (Config.EnablePortableMode)
                    {
                        _cardPortable = new ActionChoiceCard(
                            "Extract Portable",
                            ActionChoiceCard.CardIconType.PortableFolder,
                            isPrimary: false)
                        {
                            Location = new Point(30, nextY)
                        };
                        _cardPortable.Click += (s, e) => StartInstallFlow(isPortable: true);
                        this.Controls.Add(_cardPortable);
                    }
                }

                // 4. Advanced Options Toggle
                _advancedToggleLbl = new Label
                {
                    Text = "▼ Advanced options",
                    Font = UIThemeTokens.FontSmall,
                    ForeColor = UIThemeTokens.TextSecondary,
                    Location = new Point(32, 282),
                    AutoSize = true,
                    Cursor = Cursors.Hand
                };
                _advancedToggleLbl.Click += AdvancedToggleLbl_Click;
                _advancedToggleLbl.MouseEnter += (s, e) => _advancedToggleLbl.ForeColor = BrandTheme.AccentColor;
                _advancedToggleLbl.MouseLeave += (s, e) => _advancedToggleLbl.ForeColor = UIThemeTokens.TextSecondary;
                this.Controls.Add(_advancedToggleLbl);

                _eulaReviewLink = new Label
                {
                    Text = "License terms",
                    Font = UIThemeTokens.FontSmall,
                    ForeColor = UIThemeTokens.TextSecondary,
                    Location = new Point(415, 282),
                    AutoSize = true,
                    Cursor = Cursors.Hand,
                    Visible = false
                };
                _eulaReviewLink.Click += (s, e) => ShowEulaScreen(isReviewMode: true);
                _eulaReviewLink.MouseEnter += (s, e) => _eulaReviewLink.ForeColor = BrandTheme.AccentColor;
                _eulaReviewLink.MouseLeave += (s, e) => _eulaReviewLink.ForeColor = UIThemeTokens.TextSecondary;
                this.Controls.Add(_eulaReviewLink);

                // 5. Advanced Panel
                int panelHeight = Config.IsWebBrowser ? 158 : 136;
                _expandedHeight = 310 + panelHeight + 22;

                _advancedPanel = new Panel
                {
                    Location = new Point(30, 310),
                    Size = new Size(460, panelHeight),
                    BackColor = Color.Transparent,
                    Padding = new Padding(12),
                    Visible = false
                };
                _advancedPanel.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle r = new Rectangle(0, 0, _advancedPanel.Width - 1, _advancedPanel.Height - 1);
                    using (GraphicsPath path = ActionChoiceCard.CreateRoundedRectanglePath(r, 6))
                    {
                        using (SolidBrush b = new SolidBrush(UIThemeTokens.CardBackground))
                        {
                            e.Graphics.FillPath(b, path);
                        }
                        using (Pen p = new Pen(UIThemeTokens.BorderColor, 1))
                        {
                            e.Graphics.DrawPath(p, path);
                        }
                    }
                };

                Label pathLbl = new Label
                {
                    Text = "Destination folder:",
                    Font = UIThemeTokens.FontSmall,
                    ForeColor = UIThemeTokens.TextSecondary,
                    Location = new Point(12, 10),
                    AutoSize = true,
                    BackColor = Color.Transparent
                };
                _advancedPanel.Controls.Add(pathLbl);

                string initialPath = (_installedInfo != null && _installedInfo.IsInstalled && !string.IsNullOrEmpty(_installedInfo.InstallPath))
                    ? _installedInfo.InstallPath
                    : InstallerEngine.GetDefaultInstallDirectory(Config);

                _installPathTxt = new TextBox
                {
                    Text = initialPath,
                    Font = UIThemeTokens.FontDefault,
                    BackColor = UIThemeTokens.InputBackground,
                    ForeColor = UIThemeTokens.TextPrimary,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(14, 28),
                    Size = new Size(335, 24)
                };
                _advancedPanel.Controls.Add(_installPathTxt);

                _browseBtn = new Button
                {
                    Text = "Browse...",
                    Font = UIThemeTokens.FontSmall,
                    BackColor = UIThemeTokens.CardHover,
                    ForeColor = UIThemeTokens.TextPrimary,
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(356, 27),
                    Size = new Size(88, 25),
                    Cursor = Cursors.Hand
                };
                _browseBtn.FlatAppearance.BorderColor = UIThemeTokens.BorderColor;
                _browseBtn.Click += BrowseBtn_Click;
                _advancedPanel.Controls.Add(_browseBtn);

                _chkDesktop = CreateCheckBox("Create desktop shortcut", 14, 60, true);
                _chkStartMenu = CreateCheckBox("Create Start menu shortcut", 14, 82, true);
                _advancedPanel.Controls.Add(_chkDesktop);
                _advancedPanel.Controls.Add(_chkStartMenu);

                int chkY = 104;
                if (Config.IsWebBrowser)
                {
                    _chkDefaultBrowser = CreateCheckBox("Set as default browser", 14, chkY, true);
                    _advancedPanel.Controls.Add(_chkDefaultBrowser);
                    chkY += 22;
                }

                _chkLaunch = CreateCheckBox(string.Format("Launch {0} when finished", Config.AppName), 14, chkY, true);
                _advancedPanel.Controls.Add(_chkLaunch);
                this.Controls.Add(_advancedPanel);
            }
            else
            {
                // Uninstaller UI
                _cardUninstall = new ActionChoiceCard(
                    "Confirm Uninstall",
                    ActionChoiceCard.CardIconType.Trash,
                    isPrimary: true)
                {
                    Location = new Point(30, 114)
                };
                _cardUninstall.Click += (s, e) => StartUninstallFlow();
                this.Controls.Add(_cardUninstall);

                _chkDeleteData = CreateCheckBox("Remove bookmarks, history, and cache", 30, 178, false);
                this.Controls.Add(_chkDeleteData);
                _normalHeight = 250;
                this.Size = new Size(520, _normalHeight);
            }

            // 6. Direct Centered Progress Controls
            _spinner = new CircularProgressSpinner
            {
                Location = new Point((this.ClientSize.Width - 74) / 2, 70),
                Size = new Size(74, 74),
                Value = 0,
                Visible = false
            };
            this.Controls.Add(_spinner);

            _progressPhaseLbl = new Label
            {
                Text = _isUninstall ? string.Format("Uninstalling {0}...", Config.AppTitle) : string.Format("Installing {0}...", Config.AppTitle),
                Font = UIThemeTokens.FontHeader,
                ForeColor = UIThemeTokens.TextPrimary,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 160),
                Size = new Size(480, 24),
                BackColor = Color.Transparent,
                UseMnemonic = false,
                Visible = false
            };
            this.Controls.Add(_progressPhaseLbl);

            _progressDetailsLbl = new Label
            {
                Text = "Extracting components...",
                Font = UIThemeTokens.FontSmall,
                ForeColor = UIThemeTokens.TextSecondary,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 188),
                Size = new Size(480, 20),
                BackColor = Color.Transparent,
                UseMnemonic = false,
                Visible = false
            };
            this.Controls.Add(_progressDetailsLbl);

            // 7. Direct Centered Finished Controls
            _finishedTitleLbl = new Label
            {
                Text = _isUninstall ? "Uninstall Complete" : "Install Complete",
                Font = UIThemeTokens.FontTitle,
                ForeColor = UIThemeTokens.TextPrimary,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 105),
                Size = new Size(480, 36),
                BackColor = Color.Transparent,
                UseMnemonic = false,
                Visible = false
            };
            this.Controls.Add(_finishedTitleLbl);

            _finishBtn = new Button
            {
                Text = _isUninstall ? "Close" : "Finish",
                Font = UIThemeTokens.FontDefaultBold,
                ForeColor = BrandTheme.AccentTextColor,
                BackColor = BrandTheme.AccentColor,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(150, 40),
                Location = new Point((this.ClientSize.Width - 150) / 2, 160),
                Cursor = Cursors.Hand,
                Visible = false
            };
            _finishBtn.FlatAppearance.BorderSize = 0;
            _finishBtn.FlatAppearance.MouseOverBackColor = BrandTheme.AccentHoverColor;
            _finishBtn.FlatAppearance.MouseDownBackColor = BrandTheme.AccentPressedColor;
            _finishBtn.Click += FinishBtn_Click;
            this.Controls.Add(_finishBtn);

            if (!_isUninstall)
            {
                CreateEulaControls();
                ShowEulaScreen(isReviewMode: false);
            }

            // Clean perimeter border outline on form client area
            this.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UIThemeTokens.WindowBorder, 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, this.ClientSize.Width - 1, this.ClientSize.Height - 1);
                }
            };

            this.ResumeLayout(false);
        }

        private CheckBox CreateCheckBox(string text, int x, int y, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                Font = UIThemeTokens.FontSmall,
                ForeColor = UIThemeTokens.TextPrimary,
                BackColor = Color.Transparent,
                Location = new Point(x, y),
                AutoSize = true,
                Checked = isChecked,
                Cursor = Cursors.Hand
            };
        }

        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
        }

        public void SetAdvancedExpanded(bool expanded)
        {
            _isAdvancedExpanded = expanded;
            this.SuspendLayout();
            if (_isAdvancedExpanded)
            {
                if (_advancedToggleLbl != null) _advancedToggleLbl.Text = "▲ Hide options";
                if (_advancedPanel != null) _advancedPanel.Visible = true;
                this.Size = new Size(this.Width, _expandedHeight);
            }
            else
            {
                if (_advancedToggleLbl != null) _advancedToggleLbl.Text = "▼ Advanced options";
                if (_advancedPanel != null) _advancedPanel.Visible = false;
                this.Size = new Size(this.Width, _normalHeight);
            }
            this.ResumeLayout(true);
            this.Invalidate(true);
            this.Update();
        }

        private void AdvancedToggleLbl_Click(object sender, EventArgs e)
        {
            SetAdvancedExpanded(!_isAdvancedExpanded);
        }

        private void BrowseBtn_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = string.Format("Select destination folder for {0}:", Config.AppName);
                fbd.SelectedPath = _installPathTxt.Text;
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    _installPathTxt.Text = fbd.SelectedPath;
                }
            }
        }

        private void CreateEulaControls()
        {
            _eulaPanel = new Panel
            {
                Location = new Point(0, 34),
                Size = new Size(this.ClientSize.Width, _eulaHeight - 34),
                BackColor = UIThemeTokens.FormBackground,
                Visible = false
            };
            _eulaPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UIThemeTokens.WindowBorder, 1))
                {
                    // Left, right, and bottom window outline
                    e.Graphics.DrawLine(p, 0, 0, 0, _eulaPanel.Height - 1);
                    e.Graphics.DrawLine(p, _eulaPanel.Width - 1, 0, _eulaPanel.Width - 1, _eulaPanel.Height - 1);
                    e.Graphics.DrawLine(p, 0, _eulaPanel.Height - 1, _eulaPanel.Width - 1, _eulaPanel.Height - 1);
                }
            };

            BrandLogoControl eulaLogo = new BrandLogoControl(Config)
            {
                Location = new Point(24, 12),
                Size = new Size(46, 26)
            };
            _eulaPanel.Controls.Add(eulaLogo);

            Label eulaTitleLbl = new Label
            {
                Text = "End-User License Agreement",
                Font = UIThemeTokens.FontHeader,
                ForeColor = UIThemeTokens.TextPrimary,
                Location = new Point(78, 10),
                AutoSize = true,
                UseMnemonic = false
            };
            _eulaPanel.Controls.Add(eulaTitleLbl);

            Label eulaSubtitleLbl = new Label
            {
                Text = string.Format("Please review the license terms and warranty disclaimer before installing {0}.", Config.AppName),
                Font = UIThemeTokens.FontSmall,
                ForeColor = UIThemeTokens.TextSecondary,
                Location = new Point(79, 30),
                AutoSize = true,
                UseMnemonic = false
            };
            _eulaPanel.Controls.Add(eulaSubtitleLbl);

            _eulaTextContainer = new Panel
            {
                Location = new Point(24, 52),
                Size = new Size(472, 248),
                BackColor = UIThemeTokens.InputBackground,
                Padding = new Padding(8, 8, 4, 8)
            };
            _eulaTextContainer.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UIThemeTokens.BorderColor, 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, _eulaTextContainer.Width - 1, _eulaTextContainer.Height - 1);
                }
            };

            _eulaTextBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                BackColor = UIThemeTokens.InputBackground,
                ForeColor = UIThemeTokens.TextPrimary,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                TabStop = false,
                Text = GetEulaLicenseText()
            };
            _eulaTextContainer.Controls.Add(_eulaTextBox);
            _eulaPanel.Controls.Add(_eulaTextContainer);

            _chkAcceptEula = CreateCheckBox("I accept the terms and warranty conditions in this agreement", 24, 308, true);
            _chkAcceptEula.CheckedChanged += (s, e) =>
            {
                if (!_isReviewingEula && _btnEulaAgree != null)
                {
                    _btnEulaAgree.Enabled = _chkAcceptEula.Checked;
                    _btnEulaAgree.BackColor = _chkAcceptEula.Checked ? BrandTheme.AccentColor : UIThemeTokens.CardPressed;
                    _btnEulaAgree.ForeColor = _chkAcceptEula.Checked ? BrandTheme.AccentTextColor : UIThemeTokens.TextDisabled;
                }
            };
            _eulaPanel.Controls.Add(_chkAcceptEula);

            _btnEulaDecline = new Button
            {
                Text = "Decline",
                Font = UIThemeTokens.FontDefault,
                ForeColor = UIThemeTokens.TextSecondary,
                BackColor = UIThemeTokens.CardBackground,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(90, 32),
                Location = new Point(258, 338),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnEulaDecline.FlatAppearance.BorderColor = UIThemeTokens.BorderColor;
            _btnEulaDecline.Click += (s, e) => this.Close();
            _btnEulaDecline.MouseEnter += (s, e) =>
            {
                _btnEulaDecline.BackColor = UIThemeTokens.CardHover;
                _btnEulaDecline.ForeColor = UIThemeTokens.TextPrimary;
            };
            _btnEulaDecline.MouseLeave += (s, e) =>
            {
                _btnEulaDecline.BackColor = UIThemeTokens.CardBackground;
                _btnEulaDecline.ForeColor = UIThemeTokens.TextSecondary;
            };
            _eulaPanel.Controls.Add(_btnEulaDecline);

            _btnEulaAgree = new Button
            {
                Text = "Agree & Continue",
                Font = UIThemeTokens.FontDefaultBold,
                ForeColor = BrandTheme.AccentTextColor,
                BackColor = BrandTheme.AccentColor,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 32),
                Location = new Point(356, 338),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnEulaAgree.FlatAppearance.BorderSize = 0;
            _btnEulaAgree.FlatAppearance.MouseOverBackColor = BrandTheme.AccentHoverColor;
            _btnEulaAgree.FlatAppearance.MouseDownBackColor = BrandTheme.AccentPressedColor;
            _btnEulaAgree.Click += (s, e) => OnEulaAgreeClicked();
            _eulaPanel.Controls.Add(_btnEulaAgree);

            this.Controls.Add(_eulaPanel);
        }

        private void ShowEulaScreen(bool isReviewMode)
        {
            _isReviewingEula = isReviewMode;
            this.SuspendLayout();

            SetMainInstallerControlsVisible(false);

            if (_eulaPanel != null)
            {
                _eulaPanel.Visible = true;
                _eulaPanel.BringToFront();

                if (_isReviewingEula)
                {
                    _chkAcceptEula.Visible = false;
                    _btnEulaDecline.Visible = false;
                    if (_eulaTextContainer != null)
                    {
                        _eulaTextContainer.Size = new Size(472, 276);
                    }
                    _btnEulaAgree.Text = "< Back to Setup";
                    _btnEulaAgree.UseMnemonic = false;
                    _btnEulaAgree.Size = new Size(136, 32);
                    _btnEulaAgree.Location = new Point(360, 338);
                    _btnEulaAgree.Enabled = true;
                    _btnEulaAgree.BackColor = BrandTheme.AccentColor;
                    _btnEulaAgree.ForeColor = BrandTheme.AccentTextColor;
                    if (_titleLabel != null) _titleLabel.Text = Config.AppName + " Setup - License Terms";
                }
                else
                {
                    if (_eulaTextContainer != null)
                    {
                        _eulaTextContainer.Size = new Size(472, 248);
                    }
                    _chkAcceptEula.Visible = true;
                    _btnEulaDecline.Visible = true;
                    _btnEulaAgree.Text = "Agree & Continue";
                    _btnEulaAgree.UseMnemonic = false;
                    _btnEulaAgree.Size = new Size(140, 32);
                    _btnEulaAgree.Location = new Point(356, 338);
                    _btnEulaAgree.Enabled = _chkAcceptEula.Checked;
                    _btnEulaAgree.BackColor = _chkAcceptEula.Checked ? BrandTheme.AccentColor : UIThemeTokens.CardPressed;
                    _btnEulaAgree.ForeColor = _chkAcceptEula.Checked ? BrandTheme.AccentTextColor : UIThemeTokens.TextDisabled;
                    if (_titleLabel != null) _titleLabel.Text = Config.AppName + " Setup - License Agreement";
                }

                if (_eulaTextBox != null)
                {
                    _eulaTextBox.SelectionStart = 0;
                    _eulaTextBox.SelectionLength = 0;
                }
            }

            this.Size = new Size(520, _eulaHeight);
            this.ResumeLayout(true);
            this.Invalidate(true);
            this.Update();
        }

        private void OnEulaAgreeClicked()
        {
            _isEulaAccepted = true;
            this.SuspendLayout();

            if (_eulaPanel != null)
            {
                _eulaPanel.Visible = false;
            }

            _isReviewingEula = false;
            if (_titleLabel != null)
            {
                _titleLabel.Text = Config.AppName + " Setup";
            }

            SetMainInstallerControlsVisible(true);

            int targetHeight = _isAdvancedExpanded ? _expandedHeight : _normalHeight;
            this.Size = new Size(520, targetHeight);

            this.ResumeLayout(true);
            this.Invalidate(true);
            this.Update();
        }

        private void SetMainInstallerControlsVisible(bool visible)
        {
            if (_logoControl != null) _logoControl.Visible = visible;
            if (_appTitleLbl != null) _appTitleLbl.Visible = visible;
            if (_taglineLbl != null) _taglineLbl.Visible = visible;
            if (_cardInstall != null) _cardInstall.Visible = visible;
            if (_cardUninstallChoice != null) _cardUninstallChoice.Visible = visible;
            if (_cardStandalone != null) _cardStandalone.Visible = visible;
            if (_cardPortable != null) _cardPortable.Visible = visible;
            if (_advancedToggleLbl != null) _advancedToggleLbl.Visible = visible;
            if (_eulaReviewLink != null) _eulaReviewLink.Visible = visible;
            if (_advancedPanel != null) _advancedPanel.Visible = visible && _isAdvancedExpanded;
        }

        private string GetEulaLicenseText()
        {
            string appName = Config.AppName;
            string appTitle = Config.AppTitle;
            string publisher = Config.Publisher;

            return string.Format(
@"END-USER LICENSE AGREEMENT & TERMS OF USE
Software: {0} ({1})
Publisher: {2}

====================================================
IMPORTANT NOTICE - PLEASE READ CAREFULLY:
====================================================
This End-User License Agreement (""Agreement"") is a legal agreement between you and {2} for the software product {0} (""Software""), including all associated media, documentation, binary files, components, and executables.

By clicking ""Agree & Continue"", downloading, installing, copying, or otherwise using this Software, you acknowledge that you have read, understood, and agree to be bound by all the terms and conditions set forth herein.


1. GRANT OF LICENSE (MIT LICENSE TERMS)
----------------------------------------------------
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the ""Software""), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.


2. DISCLAIMER OF ALL WARRANTIES (""AS IS"" PROVISION)
----------------------------------------------------
THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, STATUTORY OR OTHERWISE.

TO THE MAXIMUM EXTENT PERMITTED UNDER APPLICABLE LAW, THE AUTHOR, COPYRIGHT HOLDERS, CONTRIBUTORS, AND AFFILIATES SPECIFICALLY DISCLAIM ALL WARRANTIES, EXPRESS, IMPLIED, OR STATUTORY, INCLUDING WITHOUT LIMITATION:

  (a) ANY IMPLIED WARRANTIES OF MERCHANTABILITY, SATISFACTORY QUALITY, AND FITNESS FOR A PARTICULAR PURPOSE;
  (b) ANY WARRANTY OF TITLE, ACCURACY, NON-INFRINGEMENT, OR QUIET ENJOYMENT;
  (c) ANY WARRANTY THAT THE SOFTWARE WILL MEET YOUR REQUIREMENTS, RUN WITHOUT INTERRUPTION, BE SECURE, TIMELY, ERROR-FREE, OR COMPATIBLE WITH ANY THIRD-PARTY HARDWARE, DRIVERS, OPERATING SYSTEMS, OR APPLICATIONS;
  (d) ANY WARRANTY THAT DEFECTS, GLITCHES, VULNERABILITIES, OR CODE BUGS WILL BE DETECTED, REPORTED, OR CORRECTED.

YOU EXPRESSLY ACKNOWLEDGE AND AGREE THAT:
  • THE USE OF THIS SOFTWARE IS AT YOUR SOLE RISK AND DISCRETION;
  • ANY CONTENT, WEBSITES, PROTOCOLS, OR DOWNLOADS ACCESSED THROUGH THE SOFTWARE ARE SOURCED FROM THIRD PARTIES AND ARE NOT CONTROLLED BY THE SOFTWARE AUTHOR;
  • THE ENTIRE RISK AS TO SATISFACTORY QUALITY, PERFORMANCE, ACCURACY, AND EFFORT RESIDES EXCLUSIVELY WITH YOU.


3. LIMITATION OF LIABILITY
----------------------------------------------------
IN NO EVENT SHALL THE AUTHOR, COPYRIGHT HOLDERS, CONTRIBUTORS, OR DISTRIBUTORS BE LIABLE FOR ANY CLAIM, DAMAGES, LOSSES, COSTS, OR EXPENSES WHATSOEVER, WHETHER ARISING IN CONTRACT, TORT (INCLUDING NEGLIGENCE), STRICT LIABILITY, OR ANY OTHER LEGAL THEORY, ARISING FROM, OUT OF, OR IN CONNECTION WITH:

  • THE SOFTWARE, ITS USE, OR THE INABILITY TO USE THE SOFTWARE;
  • ANY LOSS OF DATA, CORRUPTION OF LOCAL STORAGE, OR FAILURE OF SYSTEM FILES;
  • LOSS OF PROFITS, COMMERCIAL REVENUE, BUSINESS GOODWILL, OR SAVINGS;
  • WORK STOPPAGE, COMPUTER HARDWARE FAILURE, OR NETWORK DOWNTIME;
  • ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, PUNITIVE, OR CONSEQUENTIAL DAMAGES, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGES.


4. USER RESPONSIBILITIES & DATA PROTECTION
----------------------------------------------------
As a user of this Software, you agree that you are solely responsible for:
  • Maintaining current backups of your computer, bookmarks, and critical data;
  • Verifying the security, authenticity, and safety of third-party websites, certificates, scripts, extensions, and downloaded files;
  • Ensuring that your installation and operation of the Software complies with all local, state, federal, and international laws and regulations.


5. TERMINATION & ACCEPTANCE OF TERMS
----------------------------------------------------
This license is effective until terminated. Your rights under this license will terminate automatically without notice if you fail to comply with any of its terms.

If you do not accept these terms and disclaimers in full, click ""Decline"" or exit this setup wizard now. Installation will not proceed without your consent.",
                appName,
                appTitle,
                publisher).Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        private void SwitchToUninstallMode()
        {
            _inUninstallMode = true;
            this.SuspendLayout();
            if (_isAdvancedExpanded) SetAdvancedExpanded(false);

            if (_cardInstall != null) _cardInstall.Visible = false;
            if (_cardUninstallChoice != null) _cardUninstallChoice.Visible = false;
            if (_cardPortable != null) _cardPortable.Visible = false;
            if (_cardStandalone != null) _cardStandalone.Visible = false;
            if (_advancedToggleLbl != null) _advancedToggleLbl.Visible = false;
            if (_advancedPanel != null) _advancedPanel.Visible = false;
            if (_eulaPanel != null) _eulaPanel.Visible = false;
            if (_eulaReviewLink != null) _eulaReviewLink.Visible = false;

            _taglineLbl.Text = string.Format("Uninstall {0} from your computer.", Config.AppName);

            if (_cardUninstall == null)
            {
                _cardUninstall = new ActionChoiceCard(
                    "Confirm Uninstall",
                    ActionChoiceCard.CardIconType.Trash,
                    isPrimary: true)
                {
                    Location = new Point(30, 114)
                };
                _cardUninstall.Click += (s, e) => StartUninstallFlow();
                this.Controls.Add(_cardUninstall);
            }
            _cardUninstall.Visible = true;

            if (_chkDeleteData == null)
            {
                _chkDeleteData = CreateCheckBox("Remove bookmarks, history, and cache", 30, 178, false);
                this.Controls.Add(_chkDeleteData);
            }
            _chkDeleteData.Visible = true;

            if (_backToChoicesLbl == null)
            {
                _backToChoicesLbl = new Label
                {
                    Text = "◀ Back",
                    Font = UIThemeTokens.FontSmall,
                    ForeColor = UIThemeTokens.TextSecondary,
                    Location = new Point(32, 215),
                    AutoSize = true,
                    Cursor = Cursors.Hand
                };
                _backToChoicesLbl.Click += (s, e) => SwitchBackFromUninstallMode();
                _backToChoicesLbl.MouseEnter += (s, e) => _backToChoicesLbl.ForeColor = BrandTheme.AccentColor;
                _backToChoicesLbl.MouseLeave += (s, e) => _backToChoicesLbl.ForeColor = UIThemeTokens.TextSecondary;
                this.Controls.Add(_backToChoicesLbl);
            }
            _backToChoicesLbl.Visible = true;

            this.Size = new Size(520, 260);
            this.ResumeLayout(true);
            this.Invalidate(true);
        }

        private void SwitchBackFromUninstallMode()
        {
            _inUninstallMode = false;
            this.SuspendLayout();
            if (_cardUninstall != null) _cardUninstall.Visible = false;
            if (_chkDeleteData != null) _chkDeleteData.Visible = false;
            if (_backToChoicesLbl != null) _backToChoicesLbl.Visible = false;

            _taglineLbl.Text = GetDefaultTaglineText();

            if (_cardInstall != null) _cardInstall.Visible = true;
            if (_cardUninstallChoice != null) _cardUninstallChoice.Visible = true;
            if (_cardPortable != null) _cardPortable.Visible = true;
            if (_cardStandalone != null) _cardStandalone.Visible = true;
            if (_advancedToggleLbl != null) _advancedToggleLbl.Visible = true;
            if (_eulaReviewLink != null) _eulaReviewLink.Visible = true;

            this.Size = new Size(520, _normalHeight);
            this.ResumeLayout(true);
            this.Invalidate(true);
        }

        private string GetDefaultTaglineText()
        {
            if (_isUninstall)
            {
                return string.Format("Uninstall {0} from your computer.", Config.AppName);
            }

            if (_isUpdate)
            {
                return string.Format("Update available: v{0} → v{1}", _installedInfo.InstalledVersionString, InstallerEngine.GetPackageVersionString());
            }
            if (_isReinstall)
            {
                return string.Format("{0} v{1} is installed.", Config.AppName, _installedInfo.InstalledVersionString);
            }
            if (_isDowngrade)
            {
                return string.Format("Newer version v{0} is installed.", _installedInfo.InstalledVersionString);
            }

            return Config.Tagline;
        }

        private void ShowProgressState(string initialPhase)
        {
            // Hide hero area and decision cards
            if (_logoControl != null) _logoControl.Visible = false;
            if (_appTitleLbl != null) _appTitleLbl.Visible = false;
            if (_taglineLbl != null) _taglineLbl.Visible = false;
            if (_cardInstall != null) _cardInstall.Visible = false;
            if (_cardUninstallChoice != null) _cardUninstallChoice.Visible = false;
            if (_cardStandalone != null) _cardStandalone.Visible = false;
            if (_cardPortable != null) _cardPortable.Visible = false;
            if (_cardUninstall != null) _cardUninstall.Visible = false;
            if (_backToChoicesLbl != null) _backToChoicesLbl.Visible = false;
            if (_advancedToggleLbl != null) _advancedToggleLbl.Visible = false;
            if (_advancedPanel != null) _advancedPanel.Visible = false;
            if (_eulaPanel != null) _eulaPanel.Visible = false;
            if (_eulaReviewLink != null) _eulaReviewLink.Visible = false;
            if (_chkDeleteData != null) _chkDeleteData.Visible = false;

            this.Size = new Size(this.Width, _normalHeight);

            // Show centered progress components directly on form
            _progressPhaseLbl.Text = initialPhase;
            _progressDetailsLbl.Text = "Preparing...";
            _spinner.Value = 0;
            _spinner.Location = new Point((this.ClientSize.Width - 74) / 2, 70);

            _spinner.Visible = true;
            _progressPhaseLbl.Visible = true;
            _progressDetailsLbl.Visible = true;

            this.Invalidate(true);
            this.Update();
        }

        private void ShowFinishedState(bool isPortable = false, string targetDir = null)
        {
            _isPortableFinished = isPortable;
            _portableTargetDir = targetDir;

            // Hide progress components
            if (_spinner != null) _spinner.Visible = false;
            if (_progressPhaseLbl != null) _progressPhaseLbl.Visible = false;
            if (_progressDetailsLbl != null) _progressDetailsLbl.Visible = false;
            if (_cardUninstall != null) _cardUninstall.Visible = false;
            if (_chkDeleteData != null) _chkDeleteData.Visible = false;
            if (_backToChoicesLbl != null) _backToChoicesLbl.Visible = false;

            // Show centered finish state
            if (_isUninstall || _inUninstallMode)
            {
                _finishedTitleLbl.Text = "Uninstall Complete";
                _finishBtn.Text = "Close";
            }
            else if (isPortable)
            {
                _finishedTitleLbl.Text = "Portable Extraction Complete";
                _finishBtn.Text = "Open Folder";
            }
            else if (_isUpdate)
            {
                _finishedTitleLbl.Text = "Update Complete";
                _finishBtn.Text = "Finish";
            }
            else if (_isReinstall)
            {
                _finishedTitleLbl.Text = "Reinstall Complete";
                _finishBtn.Text = "Finish";
            }
            else
            {
                _finishedTitleLbl.Text = "Install Complete";
                _finishBtn.Text = "Finish";
            }

            _finishBtn.Location = new Point((this.ClientSize.Width - 150) / 2, 160);

            _finishedTitleLbl.Visible = true;
            _finishBtn.Visible = true;

            this.Invalidate(true);
            this.Update();
        }

        private void StartInstallFlow(bool isPortable)
        {
            if (!_isUninstall && !_isEulaAccepted)
            {
                return;
            }

            if (!isPortable && _installedInfo != null && _installedInfo.IsInstalled)
            {
                if (InstallerEngine.CurrentArchitecture == "x86" &&
                    string.Equals(_installedInfo.InstalledArchitecture, "x64", StringComparison.OrdinalIgnoreCase))
                {
                    DialogResult dr = MessageBox.Show(this,
                        string.Format("A 64-bit version of {0} is currently installed.\n\nDo you want to replace it with this 32-bit version?", Config.AppName),
                        string.Format("{0} Setup", Config.AppName),
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (dr != DialogResult.Yes)
                    {
                        return;
                    }
                }
                else if (InstallerEngine.CurrentArchitecture == "x64" &&
                    string.Equals(_installedInfo.InstalledArchitecture, "x86", StringComparison.OrdinalIgnoreCase))
                {
                    DialogResult dr = MessageBox.Show(this,
                        string.Format("A 32-bit version of {0} is currently installed.\n\nDo you want to replace it with this 64-bit version?", Config.AppName),
                        string.Format("{0} Setup", Config.AppName),
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (dr != DialogResult.Yes)
                    {
                        return;
                    }
                }
            }

            string targetDir = _installPathTxt != null && !string.IsNullOrWhiteSpace(_installPathTxt.Text)
                ? _installPathTxt.Text.Trim()
                : ((_installedInfo != null && _installedInfo.IsInstalled && !string.IsNullOrEmpty(_installedInfo.InstallPath))
                    ? _installedInfo.InstallPath
                    : InstallerEngine.GetDefaultInstallDirectory(Config));

            if (isPortable)
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = string.Format("Select folder for Portable {0}:", Config.AppName);
                    fbd.SelectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Config.PortableFolderName);
                    if (fbd.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    targetDir = fbd.SelectedPath;
                }
            }

            string phase;
            if (isPortable)
            {
                phase = string.Format("Extracting Portable {0}...", Config.AppName);
            }
            else if (_isUpdate)
            {
                phase = string.Format("Updating {0}...", Config.AppName);
            }
            else if (_isReinstall)
            {
                phase = string.Format("Reinstalling {0}...", Config.AppName);
            }
            else
            {
                phase = string.Format("Installing {0}...", Config.AppName);
            }
            ShowProgressState(phase);

            bool desktop = !isPortable && (_chkDesktop == null || _chkDesktop.Checked);
            bool startMenu = !isPortable && (_chkStartMenu == null || _chkStartMenu.Checked);
            bool registerDefault = !isPortable && Config.IsWebBrowser && (_chkDefaultBrowser == null || _chkDefaultBrowser.Checked);

            BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += (s, ev) =>
            {
                InstallerEngine.PerformInstall(targetDir, isPortable, desktop, startMenu,
                    registerFileAssoc: (registerDefault || !Config.IsWebBrowser),
                    registerDefaultBrowser: registerDefault,
                    silent: false, progress: worker, config: Config);
            };

            worker.ProgressChanged += (s, ev) =>
            {
                _spinner.Value = Math.Max(0, Math.Min(100, ev.ProgressPercentage));
                if (ev.UserState != null)
                {
                    string msg = ev.UserState.ToString();
                    if (msg.StartsWith("Extracting") || msg.StartsWith("Configuring") || msg.StartsWith("Creating") || msg.StartsWith("Registering"))
                    {
                        int pIdx = msg.IndexOf(" (");
                        if (pIdx > 0)
                        {
                            _progressPhaseLbl.Text = msg.Substring(0, pIdx);
                            _progressDetailsLbl.Text = msg;
                        }
                        else
                        {
                            _progressPhaseLbl.Text = msg;
                            _progressDetailsLbl.Text = "Please wait...";
                        }
                    }
                    else
                    {
                        _progressDetailsLbl.Text = msg;
                    }
                }
            };

            worker.RunWorkerCompleted += (s, ev) =>
            {
                if (ev.Error != null)
                {
                    MessageBox.Show(this, "An error occurred during operation: " + ev.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SwitchBackFromUninstallMode();
                    return;
                }

                if (!isPortable && (_chkLaunch == null || _chkLaunch.Checked))
                {
                    string mainExe = Path.Combine(targetDir, Config.MainExecutable);
                    if (File.Exists(mainExe))
                    {
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo
                            {
                                FileName = mainExe,
                                WorkingDirectory = targetDir,
                                UseShellExecute = true
                            };
                            Process.Start(psi);
                        }
                        catch { }
                        this.Close();
                        return;
                    }
                }
                ShowFinishedState(isPortable, targetDir);
            };

            worker.RunWorkerAsync();
        }

        private void StartStandaloneFlow()
        {
            if (!_isUninstall && !_isEulaAccepted)
            {
                return;
            }

            string standaloneDir = InstallerEngine.GetStandaloneDirectory(Config);
            string mainExe = Path.Combine(standaloneDir, Config.MainExecutable);
            bool existingStandalone = File.Exists(mainExe);

            bool overwrite = true;
            bool clearUserData = false;

            if (existingStandalone)
            {
                using (StandalonePromptDialog prompt = new StandalonePromptDialog(standaloneDir, Config))
                {
                    DialogResult result = prompt.ShowDialog(this);
                    if (result != DialogResult.OK)
                    {
                        return; // User cancelled prompt, return to choice cards
                    }
                    overwrite = prompt.OverwriteFiles;
                    clearUserData = prompt.ClearUserData;
                }
            }

            ShowProgressState(string.Format("Launching Standalone {0}...", Config.AppName));
            _progressDetailsLbl.Text = overwrite
                ? "Unpacking runtime components to temp..."
                : "Starting existing standalone application...";

            BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true };
            string launchedExe = null;

            worker.DoWork += (s, ev) =>
            {
                launchedExe = InstallerEngine.LaunchStandalone(worker, Config, overwrite, clearUserData);
            };

            worker.ProgressChanged += (s, ev) =>
            {
                _spinner.Value = Math.Max(0, Math.Min(100, ev.ProgressPercentage));
                if (ev.UserState != null)
                {
                    _progressDetailsLbl.Text = ev.UserState.ToString();
                }
            };

            worker.RunWorkerCompleted += (s, ev) =>
            {
                if (ev.Error != null)
                {
                    MessageBox.Show(this, "Failed to launch standalone mode: " + ev.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SwitchBackFromUninstallMode();
                    return;
                }
                this.Close();
            };

            worker.RunWorkerAsync();
        }

        private void StartUninstallFlow()
        {
            ShowProgressState(string.Format("Uninstalling {0}...", Config.AppTitle));
            bool deleteData = _chkDeleteData != null && _chkDeleteData.Checked;

            BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += (s, ev) =>
            {
                InstallerEngine.PerformUninstall(silent: false, deleteProfileData: deleteData, progress: worker, config: Config);
            };

            worker.ProgressChanged += (s, ev) =>
            {
                _spinner.Value = Math.Max(0, Math.Min(100, ev.ProgressPercentage));
                if (ev.UserState != null)
                {
                    _progressDetailsLbl.Text = ev.UserState.ToString();
                }
            };

            worker.RunWorkerCompleted += (s, ev) =>
            {
                if (ev.Error != null)
                {
                    MessageBox.Show(this, "An error occurred during uninstallation: " + ev.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SwitchBackFromUninstallMode();
                    return;
                }
                ShowFinishedState();
            };

            worker.RunWorkerAsync();
        }

        private void FinishBtn_Click(object sender, EventArgs e)
        {
            if (_isPortableFinished && !string.IsNullOrEmpty(_portableTargetDir) && Directory.Exists(_portableTargetDir))
            {
                try
                {
                    Process.Start("explorer.exe", _portableTargetDir);
                }
                catch { }
                this.Close();
                return;
            }

            if (!_isUninstall && !_inUninstallMode && (_chkLaunch == null || _chkLaunch.Checked))
            {
                string targetDir = _installPathTxt != null && !string.IsNullOrWhiteSpace(_installPathTxt.Text)
                    ? _installPathTxt.Text.Trim()
                    : ((_installedInfo != null && _installedInfo.IsInstalled && !string.IsNullOrEmpty(_installedInfo.InstallPath))
                        ? _installedInfo.InstallPath
                        : InstallerEngine.GetDefaultInstallDirectory(Config));
                string mainExe = Path.Combine(targetDir, Config.MainExecutable);
                if (File.Exists(mainExe))
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = mainExe,
                            WorkingDirectory = targetDir,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch { }
                }
            }
            this.Close();
        }
    }
}

