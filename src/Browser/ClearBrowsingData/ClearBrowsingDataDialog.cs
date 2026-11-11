using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Browser.ClearBrowsingData
{
    public class ClearBrowsingDataDialog : Form
    {
        private Button _basicTabBtn;
        private Button _advancedTabBtn;
        private Panel _tabIndicator;
        private ComboBox _timeRangeCombo;
        private Panel _contentPanel;

        // Basic Tab Controls
        private Panel _basicContainer;
        private CheckBox _basicHistoryChk;
        private Label _basicHistorySub;
        private CheckBox _basicCookiesChk;
        private Label _basicCookiesSub;
        private CheckBox _basicCacheChk;
        private Label _basicCacheSub;

        // Advanced Tab Controls
        private Panel _advancedContainer;
        private CheckBox _advHistoryChk;
        private Label _advHistorySub;
        private CheckBox _advDownloadsChk;
        private Label _advDownloadsSub;
        private CheckBox _advCookiesChk;
        private Label _advCookiesSub;
        private CheckBox _advCacheChk;
        private Label _advCacheSub;
        private CheckBox _advPasswordsChk;
        private Label _advPasswordsSub;
        private CheckBox _advAutofillChk;
        private Label _advAutofillSub;
        private CheckBox _advSiteSettingsChk;
        private Label _advSiteSettingsSub;
        private CheckBox _advHostedAppChk;
        private Label _advHostedAppSub;

        // Footer Controls
        private Label _statusLabel;
        private Button _cancelBtn;
        private Button _clearDataBtn;

        private bool _isAdvancedTab = false;
        private bool _isClearing = false;

        public ClearBrowsingDataDialog()
        {
            InitializeComponent();
            UITheme.ApplyModalDialog(this, AppStrings.ClearBrowsingData.DialogTitle, new Size(520, 600), new Size(480, 520));
            this.KeyPreview = true;

            UpdateDataSummaryAsync();
        }

        public static DialogResult ShowClearBrowsingData(IWin32Window owner = null)
        {
            using (ClearBrowsingDataDialog dlg = new ClearBrowsingDataDialog())
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
            this.Text = AppStrings.ClearBrowsingData.DialogTitle;
            this.Size = new Size(520, 600);
            this.BackColor = UITheme.FormBackground;
            this.Font = UITheme.FontDefault;
            this.Padding = new Padding(0);

            // 1. Header & Title Panel
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 52;
            headerPanel.Padding = new Padding(24, 16, 24, 0);

            Label titleLabel = new Label();
            titleLabel.Text = AppStrings.ClearBrowsingData.HeaderTitle;
            titleLabel.Font = new Font("Segoe UI", 13.5F, FontStyle.Bold);
            titleLabel.ForeColor = UITheme.TextPrimary;
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(24, 14);
            headerPanel.Controls.Add(titleLabel);

            // 2. Tab Navigation Bar (Basic / Advanced)
            Panel tabNavPanel = new Panel();
            tabNavPanel.Dock = DockStyle.Top;
            tabNavPanel.Height = 42;
            tabNavPanel.Padding = new Padding(24, 0, 24, 0);

            _basicTabBtn = CreateTabButton(AppStrings.ClearBrowsingData.TabBasic, 0);
            _advancedTabBtn = CreateTabButton(AppStrings.ClearBrowsingData.TabAdvanced, 1);

            _basicTabBtn.Click += (s, e) => SwitchTab(false);
            _advancedTabBtn.Click += (s, e) => SwitchTab(true);

            _tabIndicator = new Panel();
            _tabIndicator.Height = 3;
            _tabIndicator.BackColor = UITheme.AccentBlue;

            tabNavPanel.Controls.Add(_tabIndicator);
            tabNavPanel.Controls.Add(_advancedTabBtn);
            tabNavPanel.Controls.Add(_basicTabBtn);

            // Bottom border for the tab nav strip
            Panel tabBorder = new Panel();
            tabBorder.Dock = DockStyle.Bottom;
            tabBorder.Height = 1;
            tabBorder.BackColor = MaterialColors.Separator;
            tabNavPanel.Controls.Add(tabBorder);

            // 3. Time Range Selector Row
            Panel timeRangePanel = new Panel();
            timeRangePanel.Dock = DockStyle.Top;
            timeRangePanel.Height = 48;
            timeRangePanel.Padding = new Padding(24, 10, 24, 6);

            Label timeRangeLabel = new Label();
            timeRangeLabel.Text = AppStrings.ClearBrowsingData.TimeRangeLabel;
            timeRangeLabel.ForeColor = UITheme.TextSecondary;
            timeRangeLabel.Font = UITheme.FontDefault;
            timeRangeLabel.AutoSize = true;
            timeRangeLabel.Location = new Point(24, 14);

            _timeRangeCombo = new ComboBox();
            _timeRangeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _timeRangeCombo.Width = 160;
            _timeRangeCombo.Location = new Point(110, 11);
            _timeRangeCombo.Items.AddRange(new object[] {
                AppStrings.ClearBrowsingData.TimeRangeLastHour,
                AppStrings.ClearBrowsingData.TimeRangeLast24Hours,
                AppStrings.ClearBrowsingData.TimeRangeLast7Days,
                AppStrings.ClearBrowsingData.TimeRangeLast4Weeks,
                AppStrings.ClearBrowsingData.TimeRangeAllTime
            });
            UITheme.ApplyComboBox(_timeRangeCombo);
            _timeRangeCombo.SelectedIndex = 0; // Default: Last hour
            _timeRangeCombo.SelectedIndexChanged += (s, e) => UpdateDataSummaryAsync();

            timeRangePanel.Controls.Add(timeRangeLabel);
            timeRangePanel.Controls.Add(_timeRangeCombo);

            // 4. Content Area Hosting Options
            _contentPanel = new Panel();
            _contentPanel.Dock = DockStyle.Fill;
            _contentPanel.AutoScroll = true;
            _contentPanel.Padding = new Padding(24, 8, 24, 12);

            BuildBasicTab();
            BuildAdvancedTab();

            _contentPanel.Controls.Add(_basicContainer);
            _contentPanel.Controls.Add(_advancedContainer);

            // 5. Footer Panel with Action Buttons
            Panel footerPanel = new Panel();
            footerPanel.Dock = DockStyle.Bottom;
            footerPanel.Height = 56;
            footerPanel.Padding = new Padding(24, 10, 24, 10);

            Panel footerBorder = new Panel();
            footerBorder.Dock = DockStyle.Top;
            footerBorder.Height = 1;
            footerBorder.BackColor = MaterialColors.Separator;
            footerPanel.Controls.Add(footerBorder);

            _statusLabel = new Label();
            _statusLabel.Text = string.Empty;
            _statusLabel.ForeColor = UITheme.AccentBlue;
            _statusLabel.Font = UITheme.FontSmall;
            _statusLabel.AutoSize = true;
            _statusLabel.Location = new Point(24, 20);
            footerPanel.Controls.Add(_statusLabel);

            _clearDataBtn = new Button();
            _clearDataBtn.Text = AppStrings.ClearBrowsingData.BtnClearData;
            _clearDataBtn.Size = new Size(100, 32);
            _clearDataBtn.Location = new Point(footerPanel.Width - 124, 12);
            _clearDataBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            UITheme.ApplyButton(_clearDataBtn, isPrimary: true);
            _clearDataBtn.Click += async (s, e) =>
            {
                try
                {
                    await ExecuteClearAsync();
                }
                catch { }
            };

            _cancelBtn = new Button();
            _cancelBtn.Text = AppStrings.ClearBrowsingData.BtnCancel;
            _cancelBtn.Size = new Size(90, 32);
            _cancelBtn.Location = new Point(footerPanel.Width - 224, 12);
            _cancelBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            UITheme.ApplyButton(_cancelBtn, isPrimary: false);
            _cancelBtn.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            footerPanel.Controls.Add(_cancelBtn);
            footerPanel.Controls.Add(_clearDataBtn);

            // Add all containers to Form
            this.Controls.Add(_contentPanel);
            this.Controls.Add(timeRangePanel);
            this.Controls.Add(tabNavPanel);
            this.Controls.Add(headerPanel);
            this.Controls.Add(footerPanel);

            SwitchTab(false);
        }

        private Button CreateTabButton(string text, int index)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Font = UITheme.FontDefaultBold;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseDownBackColor = Color.Transparent;
            btn.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btn.BackColor = Color.Transparent;
            btn.Size = new Size(90, 38);
            btn.Location = new Point(24 + (index * 96), 0);
            btn.Cursor = Cursors.Hand;
            return btn;
        }

        private void SwitchTab(bool isAdvanced)
        {
            _isAdvancedTab = isAdvanced;

            if (!isAdvanced)
            {
                _basicTabBtn.ForeColor = UITheme.AccentBlue;
                _advancedTabBtn.ForeColor = UITheme.TextSecondary;
                _tabIndicator.Bounds = new Rectangle(_basicTabBtn.Left, _basicTabBtn.Bottom - 3, _basicTabBtn.Width, 3);
                _basicContainer.Visible = true;
                _advancedContainer.Visible = false;
            }
            else
            {
                _basicTabBtn.ForeColor = UITheme.TextSecondary;
                _advancedTabBtn.ForeColor = UITheme.AccentBlue;
                _tabIndicator.Bounds = new Rectangle(_advancedTabBtn.Left, _advancedTabBtn.Bottom - 3, _advancedTabBtn.Width, 3);
                _basicContainer.Visible = false;
                _advancedContainer.Visible = true;
            }

            UpdateDataSummaryAsync();
        }

        private void BuildBasicTab()
        {
            _basicContainer = new Panel();
            _basicContainer.Dock = DockStyle.Fill;
            _basicContainer.AutoScroll = true;

            int y = 4;
            CreateOptionRow(_basicContainer, ref y, AppStrings.ClearBrowsingData.BrowsingHistoryTitle, AppStrings.ClearBrowsingData.BrowsingHistoryDesc, true, out _basicHistoryChk, out _basicHistorySub);
            CreateOptionRow(_basicContainer, ref y, AppStrings.ClearBrowsingData.CookiesTitle, AppStrings.ClearBrowsingData.CookiesDesc, true, out _basicCookiesChk, out _basicCookiesSub);
            CreateOptionRow(_basicContainer, ref y, AppStrings.ClearBrowsingData.CacheTitle, AppStrings.ClearBrowsingData.CacheDescBasic, true, out _basicCacheChk, out _basicCacheSub);

            // Footnote
            Label footnote = new Label();
            footnote.Text = AppStrings.ClearBrowsingData.DeviceFootnote;
            footnote.Font = UITheme.FontSmall;
            footnote.ForeColor = UITheme.TextMuted;
            footnote.AutoSize = true;
            footnote.Location = new Point(8, y + 16);
            _basicContainer.Controls.Add(footnote);
        }

        private void BuildAdvancedTab()
        {
            _advancedContainer = new Panel();
            _advancedContainer.Dock = DockStyle.Fill;
            _advancedContainer.AutoScroll = true;
            _advancedContainer.Visible = false;

            int y = 4;
            CreateOptionRow(_advancedContainer, ref y, AppStrings.ClearBrowsingData.BrowsingHistoryTitle, AppStrings.ClearBrowsingData.BrowsingHistoryDesc, true, out _advHistoryChk, out _advHistorySub);
            CreateOptionRow(_advancedContainer, ref y, AppStrings.ClearBrowsingData.DownloadHistoryTitle, AppStrings.ClearBrowsingData.DownloadHistoryDesc, true, out _advDownloadsChk, out _advDownloadsSub);
            CreateOptionRow(_advancedContainer, ref y, AppStrings.ClearBrowsingData.CookiesTitle, AppStrings.ClearBrowsingData.CookiesDesc, true, out _advCookiesChk, out _advCookiesSub);
            CreateOptionRow(_advancedContainer, ref y, AppStrings.ClearBrowsingData.CacheTitle, AppStrings.ClearBrowsingData.CacheDescAdvanced, true, out _advCacheChk, out _advCacheSub);
            CreateOptionRow(_advancedContainer, ref y, AppStrings.ClearBrowsingData.PasswordsTitle, AppStrings.ClearBrowsingData.PasswordsDesc, false, out _advPasswordsChk, out _advPasswordsSub);
            CreateOptionRow(_advancedContainer, ref y, AppStrings.ClearBrowsingData.AutofillTitle, AppStrings.ClearBrowsingData.AutofillDesc, false, out _advAutofillChk, out _advAutofillSub);
            CreateOptionRow(_advancedContainer, ref y, AppStrings.ClearBrowsingData.SiteSettingsTitle, AppStrings.ClearBrowsingData.SiteSettingsDesc, false, out _advSiteSettingsChk, out _advSiteSettingsSub);
            CreateOptionRow(_advancedContainer, ref y, AppStrings.ClearBrowsingData.HostedAppDataTitle, AppStrings.ClearBrowsingData.HostedAppDataDesc, false, out _advHostedAppChk, out _advHostedAppSub);
        }

        private void CreateOptionRow(Panel parent, ref int y, string title, string defaultDesc, bool defaultChecked, out CheckBox chk, out Label subLabel)
        {
            Panel rowPanel = new Panel();
            rowPanel.Location = new Point(0, y);
            rowPanel.Width = parent.ClientSize.Width > 0 ? parent.ClientSize.Width - 10 : 450;
            rowPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            rowPanel.AutoSize = false;
            rowPanel.Height = 52;

            chk = new CheckBox();
            chk.Text = title;
            chk.Font = UITheme.FontDefaultBold;
            chk.ForeColor = UITheme.TextPrimary;
            chk.Checked = defaultChecked;
            chk.AutoSize = true;
            chk.Location = new Point(8, 4);
            UITheme.ApplyCheckBox(chk);

            subLabel = new Label();
            subLabel.Text = defaultDesc;
            subLabel.Font = UITheme.FontSmall;
            subLabel.ForeColor = UITheme.TextSecondary;
            subLabel.AutoSize = true;
            subLabel.Location = new Point(32, 26);

            rowPanel.Controls.Add(chk);
            rowPanel.Controls.Add(subLabel);
            parent.Controls.Add(rowPanel);

            y += 56;
        }

        private ClearBrowsingDataTimeRange SelectedTimeRange
        {
            get
            {
                int idx = _timeRangeCombo != null ? _timeRangeCombo.SelectedIndex : 0;
                if (idx < 0 || idx > 4) idx = 0;
                return (ClearBrowsingDataTimeRange)idx;
            }
        }

        private async void UpdateDataSummaryAsync()
        {
            if (this.IsDisposed) return;
            ClearBrowsingDataTimeRange range = SelectedTimeRange;

            await Task.Run(() =>
            {
                int historyCount = BrowsingDataCleaner.GetHistoryCount(range);
                int downloadCount = BrowsingDataCleaner.GetDownloadsCount(range);
                long cacheBytes = BrowsingDataCleaner.GetEstimatedCacheSizeBytes();
                string cacheText = BrowsingDataCleaner.FormatByteSize(cacheBytes);

                if (this.IsDisposed) return;
                this.BeginInvoke(new Action(() =>
                {
                    if (this.IsDisposed) return;

                    string historyDesc = historyCount > 0
                        ? string.Format("{0} ({1} {2})", AppStrings.ClearBrowsingData.BrowsingHistoryDesc, historyCount, historyCount == 1 ? "item" : "items")
                        : string.Format("{0} (None)", AppStrings.ClearBrowsingData.BrowsingHistoryDesc);

                    string downloadDesc = downloadCount > 0
                        ? string.Format("{0} ({1} {2})", AppStrings.ClearBrowsingData.DownloadHistoryDesc, downloadCount, downloadCount == 1 ? "item" : "items")
                        : string.Format("{0} (None)", AppStrings.ClearBrowsingData.DownloadHistoryDesc);

                    string basicCacheDesc = string.Format("Frees up {0}. Some sites may load more slowly on your next visit.", cacheText);
                    string advCacheDesc = string.Format("Frees up {0}.", cacheText);

                    if (_basicHistorySub != null) _basicHistorySub.Text = historyDesc;
                    if (_basicCacheSub != null) _basicCacheSub.Text = basicCacheDesc;

                    if (_advHistorySub != null) _advHistorySub.Text = historyDesc;
                    if (_advDownloadsSub != null) _advDownloadsSub.Text = downloadDesc;
                    if (_advCacheSub != null) _advCacheSub.Text = advCacheDesc;
                }));
            });
        }

        private ClearBrowsingDataOptions CollectOptions()
        {
            ClearBrowsingDataOptions opt = new ClearBrowsingDataOptions();
            opt.TimeRange = SelectedTimeRange;

            if (!_isAdvancedTab)
            {
                opt.ClearBrowsingHistory = _basicHistoryChk != null && _basicHistoryChk.Checked;
                opt.ClearDownloadHistory = false;
                opt.ClearCookiesAndSiteData = _basicCookiesChk != null && _basicCookiesChk.Checked;
                opt.ClearCachedImagesAndFiles = _basicCacheChk != null && _basicCacheChk.Checked;
                opt.ClearPasswords = false;
                opt.ClearAutofillFormData = false;
                opt.ClearSiteSettings = false;
                opt.ClearHostedAppData = false;
            }
            else
            {
                opt.ClearBrowsingHistory = _advHistoryChk != null && _advHistoryChk.Checked;
                opt.ClearDownloadHistory = _advDownloadsChk != null && _advDownloadsChk.Checked;
                opt.ClearCookiesAndSiteData = _advCookiesChk != null && _advCookiesChk.Checked;
                opt.ClearCachedImagesAndFiles = _advCacheChk != null && _advCacheChk.Checked;
                opt.ClearPasswords = _advPasswordsChk != null && _advPasswordsChk.Checked;
                opt.ClearAutofillFormData = _advAutofillChk != null && _advAutofillChk.Checked;
                opt.ClearSiteSettings = _advSiteSettingsChk != null && _advSiteSettingsChk.Checked;
                opt.ClearHostedAppData = _advHostedAppChk != null && _advHostedAppChk.Checked;
            }

            return opt;
        }

        private async Task ExecuteClearAsync()
        {
            if (_isClearing) return;
            _isClearing = true;

            try
            {
                _clearDataBtn.Enabled = false;
                _cancelBtn.Enabled = false;
                _clearDataBtn.Text = "Clearing...";
                _statusLabel.Text = AppStrings.ClearBrowsingData.StatusClearing;

                ClearBrowsingDataOptions options = CollectOptions();
                ClearBrowsingDataResult result = await BrowsingDataCleaner.ClearDataAsync(options);

                _statusLabel.Text = (result != null && result.Success) ? AppStrings.ClearBrowsingData.StatusCleared : "Failed to clear data";
                await Task.Delay(350);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Error: " + ex.Message;
            }
            finally
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && !_isClearing)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                return true;
            }
            if (keyData == Keys.Enter && !_isClearing)
            {
                if (_clearDataBtn != null && _clearDataBtn.Enabled)
                {
                    _clearDataBtn.PerformClick();
                }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
