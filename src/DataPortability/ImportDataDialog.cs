using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.DataPortability
{
    /// <summary>
    /// Modal dialog window for importing bookmarks, history, and shortcuts from
    /// installed browsers or an HTML bookmarks file.
    /// Follows the SponsorBlock settings form architecture and design language.
    /// </summary>
    public class ImportDataDialog : Form
    {
        private ComboBox _sourceCombo;
        private Button _browseBtn;
        private Label _filePathLabel;

        private CheckBox _bookmarksCheck;
        private RadioButton _mergeRadio;
        private RadioButton _dedicatedRadio;
        private TextBox _dedicatedFolderBox;

        private CheckBox _historyCheck;
        private CheckBox _shortcutsCheck;
        private CheckBox _skipDuplicatesCheck;

        private ProgressBar _progressBar;
        private Label _statusLabel;
        private Button _importBtn;
        private Button _cancelBtn;

        private List<BrowserProfileInfo> _detectedBrowsers;
        private string _selectedHtmlPath;
        private bool _isImporting;

        public static void ShowImportDialog(IWin32Window owner = null)
        {
            using (var dlg = new ImportDataDialog())
            {
                if (owner != null)
                {
                    dlg.ShowDialog(owner);
                }
                else
                {
                    dlg.ShowDialog();
                }
            }
        }

        public ImportDataDialog()
        {
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            this.DoubleBuffered = true;

            InitializeComponent();
            LoadBrowsersAsync();
        }

        private void InitializeComponent()
        {
            UITheme.ApplyForm(this, AppStrings.ImportData.WindowTitle, new Size(520, 460), new Size(460, 400));
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 3;
            mainLayout.ColumnCount = 1;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));   // Top Header
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Main Scrollable Content
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));   // Bottom Bar
            this.Controls.Add(mainLayout);

            // 1. Top Header
            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Fill;
            UITheme.ApplyTopToolbar(topPanel);
            topPanel.Padding = new Padding(20, 10, 20, 8);

            Label headerLabel = new Label();
            headerLabel.UseMnemonic = false;
            headerLabel.Text = AppStrings.ImportData.PageTitle;
            headerLabel.Font = new Font(UITheme.FontDefault.FontFamily, 13F, FontStyle.Bold);
            headerLabel.ForeColor = UITheme.TextPrimary;
            headerLabel.AutoSize = true;
            headerLabel.Dock = DockStyle.Top;
            topPanel.Controls.Add(headerLabel);

            Label subLabel = new Label();
            subLabel.UseMnemonic = false;
            subLabel.Text = AppStrings.ImportData.SubTitle;
            subLabel.Font = UITheme.FontSmall;
            subLabel.ForeColor = UITheme.TextSecondary;
            subLabel.AutoSize = true;
            subLabel.Dock = DockStyle.Top;
            subLabel.Padding = new Padding(0, 3, 0, 0);
            topPanel.Controls.Add(subLabel);
            subLabel.BringToFront();

            mainLayout.Controls.Add(topPanel, 0, 0);

            // 2. Main Scrollable Content
            Panel scrollContainer = new Panel();
            scrollContainer.Dock = DockStyle.Fill;
            scrollContainer.AutoScroll = true;
            scrollContainer.Padding = new Padding(20, 12, 20, 8);
            scrollContainer.BackColor = UITheme.FormBackground;

            // --- Section A: Browser Selection ---
            Label secBrowser = new Label();
            secBrowser.UseMnemonic = false;
            secBrowser.Text = AppStrings.ImportData.SectionSelectBrowser;
            secBrowser.Font = new Font(UITheme.FontDefault.FontFamily, 9F, FontStyle.Bold);
            secBrowser.ForeColor = UITheme.TextSecondary;
            secBrowser.AutoSize = true;
            secBrowser.Dock = DockStyle.Top;
            secBrowser.Padding = new Padding(0, 0, 0, 6);
            scrollContainer.Controls.Add(secBrowser);

            TableLayoutPanel browserRowTable = new TableLayoutPanel();
            browserRowTable.Dock = DockStyle.Top;
            browserRowTable.AutoSize = true;
            browserRowTable.ColumnCount = 2;
            browserRowTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            browserRowTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            browserRowTable.RowCount = 1;
            browserRowTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _sourceCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                ItemHeight = 22,
                Font = UITheme.FontDefault,
                Width = 320,
                Margin = new Padding(0, 0, 8, 0)
            };
            UITheme.ApplyComboBox(_sourceCombo);
            _sourceCombo.SelectedIndexChanged += SourceCombo_SelectedIndexChanged;
            browserRowTable.Controls.Add(_sourceCombo, 0, 0);

            _browseBtn = new Button
            {
                Text = AppStrings.ImportData.BtnBrowse,
                Size = new Size(84, 26),
                Margin = new Padding(0, 1, 0, 0),
                Visible = false
            };
            UITheme.ApplyButton(_browseBtn, isPrimary: false);
            _browseBtn.Click += BrowseBtn_Click;
            browserRowTable.Controls.Add(_browseBtn, 1, 0);

            scrollContainer.Controls.Add(browserRowTable);
            browserRowTable.BringToFront();

            _filePathLabel = new Label
            {
                Text = string.Empty,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                AutoEllipsis = true,
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(2, 4, 0, 4),
                Visible = false
            };
            scrollContainer.Controls.Add(_filePathLabel);
            _filePathLabel.BringToFront();

            // --- Section B: Data Options ---
            Label secData = new Label();
            secData.UseMnemonic = false;
            secData.Text = AppStrings.ImportData.SectionSelectData;
            secData.Font = new Font(UITheme.FontDefault.FontFamily, 9F, FontStyle.Bold);
            secData.ForeColor = UITheme.TextSecondary;
            secData.AutoSize = true;
            secData.Dock = DockStyle.Top;
            secData.Padding = new Padding(0, 20, 0, 6);
            scrollContainer.Controls.Add(secData);
            secData.BringToFront();

            FlowLayoutPanel optionsFlow = new FlowLayoutPanel();
            optionsFlow.Dock = DockStyle.Top;
            optionsFlow.AutoSize = true;
            optionsFlow.FlowDirection = FlowDirection.TopDown;
            optionsFlow.WrapContents = false;
            optionsFlow.Padding = new Padding(0);

            _bookmarksCheck = new CheckBox
            {
                Text = AppStrings.ImportData.OptBookmarks,
                Font = UITheme.FontDefault,
                ForeColor = UITheme.TextPrimary,
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 2)
            };
            UITheme.ApplyCheckBox(_bookmarksCheck);
            optionsFlow.Controls.Add(_bookmarksCheck);

            // Sub-panel for destination radios (clean TableLayoutPanel for perfect mutual exclusion & no overlap)
            TableLayoutPanel destTable = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(22, 2, 0, 8),
                Dock = DockStyle.Top
            };
            destTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            destTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            destTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            destTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _mergeRadio = new RadioButton
            {
                Text = AppStrings.ImportData.OptDestinationMerge,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 4)
            };
            UITheme.ApplyRadioButton(_mergeRadio);
            destTable.Controls.Add(_mergeRadio, 0, 0);
            destTable.SetColumnSpan(_mergeRadio, 2);

            _dedicatedRadio = new RadioButton
            {
                Text = AppStrings.ImportData.OptDestinationDedicated,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                AutoSize = true,
                Margin = new Padding(0, 4, 6, 2)
            };
            UITheme.ApplyRadioButton(_dedicatedRadio);
            destTable.Controls.Add(_dedicatedRadio, 0, 1);

            _dedicatedFolderBox = new TextBox
            {
                Text = "Imported from Browser",
                Width = 200,
                Font = UITheme.FontSmall,
                Enabled = false,
                Margin = new Padding(0, 2, 0, 2)
            };
            UITheme.ApplyTextBox(_dedicatedFolderBox);
            destTable.Controls.Add(_dedicatedFolderBox, 1, 1);

            // Group radio button interactions and sync with bookmarks checkbox
            _mergeRadio.CheckedChanged += (s, e) =>
            {
                if (_mergeRadio.Checked)
                {
                    _dedicatedFolderBox.Enabled = false;
                }
            };
            _dedicatedRadio.CheckedChanged += (s, e) =>
            {
                _dedicatedFolderBox.Enabled = _dedicatedRadio.Checked && _bookmarksCheck.Checked;
            };
            _bookmarksCheck.CheckedChanged += (s, e) =>
            {
                _mergeRadio.Enabled = _bookmarksCheck.Checked;
                _dedicatedRadio.Enabled = _bookmarksCheck.Checked;
                _dedicatedFolderBox.Enabled = _bookmarksCheck.Checked && _dedicatedRadio.Checked;
            };

            optionsFlow.Controls.Add(destTable);

            _historyCheck = new CheckBox
            {
                Text = AppStrings.ImportData.OptHistory,
                Font = UITheme.FontDefault,
                ForeColor = UITheme.TextPrimary,
                Checked = false,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 2)
            };
            UITheme.ApplyCheckBox(_historyCheck);
            optionsFlow.Controls.Add(_historyCheck);

            _shortcutsCheck = new CheckBox
            {
                Text = AppStrings.ImportData.OptShortcuts,
                Font = UITheme.FontDefault,
                ForeColor = UITheme.TextPrimary,
                Checked = false,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 2)
            };
            UITheme.ApplyCheckBox(_shortcutsCheck);
            optionsFlow.Controls.Add(_shortcutsCheck);

            _skipDuplicatesCheck = new CheckBox
            {
                Text = AppStrings.ImportData.OptSkipDuplicates,
                Font = UITheme.FontDefault,
                ForeColor = UITheme.TextPrimary,
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 2)
            };
            UITheme.ApplyCheckBox(_skipDuplicatesCheck);
            optionsFlow.Controls.Add(_skipDuplicatesCheck);

            scrollContainer.Controls.Add(optionsFlow);
            optionsFlow.BringToFront();

            // Progress bar
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 4,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Margin = new Padding(0, 8, 0, 0),
                Visible = false
            };
            scrollContainer.Controls.Add(_progressBar);
            _progressBar.BringToFront();

            mainLayout.Controls.Add(scrollContainer, 0, 1);

            // 3. Bottom Buttons Bar
            TableLayoutPanel bottomLayout = new TableLayoutPanel();
            UITheme.ApplyBottomBar(bottomLayout);
            bottomLayout.Dock = DockStyle.Fill;
            bottomLayout.RowCount = 1;
            bottomLayout.ColumnCount = 3;
            bottomLayout.ColumnStyles.Clear();
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomLayout.Padding = new Padding(12, 0, 12, 0);

            _statusLabel = new Label
            {
                AutoSize = true,
                ForeColor = UITheme.TextSecondary,
                Font = UITheme.FontSmall,
                Margin = new Padding(8, 12, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            bottomLayout.Controls.Add(_statusLabel, 0, 0);

            FlowLayoutPanel rightButtons = new FlowLayoutPanel();
            rightButtons.Dock = DockStyle.Fill;
            rightButtons.AutoSize = true;
            rightButtons.FlowDirection = FlowDirection.RightToLeft;
            rightButtons.WrapContents = false;

            _cancelBtn = new Button
            {
                Text = AppStrings.General.BtnCancel,
                Size = new Size(80, 30),
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(4, 11, 0, 11)
            };
            UITheme.ApplyButton(_cancelBtn, isPrimary: false);
            _cancelBtn.Click += (s, e) => { if (!_isImporting) this.Close(); };
            rightButtons.Controls.Add(_cancelBtn);

            _importBtn = new Button
            {
                Text = AppStrings.ImportData.BtnImport,
                Size = new Size(110, 30),
                Margin = new Padding(4, 11, 4, 11)
            };
            UITheme.ApplyButton(_importBtn, isPrimary: true);
            _importBtn.Click += ImportBtn_Click;
            rightButtons.Controls.Add(_importBtn);

            bottomLayout.Controls.Add(rightButtons, 2, 0);
            mainLayout.Controls.Add(bottomLayout, 0, 2);

            this.AcceptButton = _importBtn;
            this.CancelButton = _cancelBtn;
        }

        private void LoadBrowsersAsync()
        {
            Task.Run(() =>
            {
                var detected = BrowserDetector.DetectInstalledBrowsers();
                this.BeginInvoke(new Action(() =>
                {
                    _detectedBrowsers = detected;
                    _sourceCombo.Items.Clear();
                    for (int i = 0; i < _detectedBrowsers.Count; i++)
                    {
                        _sourceCombo.Items.Add(_detectedBrowsers[i]);
                    }
                    if (_sourceCombo.Items.Count > 0)
                    {
                        _sourceCombo.SelectedIndex = 0;
                    }
                }));
            });
        }


        private void SourceCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            var profile = _sourceCombo.SelectedItem as BrowserProfileInfo;
            if (profile == null) return;

            bool isHtml = profile.Type == BrowserType.HtmlFile;
            _browseBtn.Visible = isHtml;
            _filePathLabel.Visible = isHtml;

            _historyCheck.Enabled = !isHtml;
            _shortcutsCheck.Enabled = !isHtml && profile.Type != BrowserType.Firefox;

            if (isHtml)
            {
                _historyCheck.Checked = false;
                _shortcutsCheck.Checked = false;
                _filePathLabel.Text = string.IsNullOrEmpty(_selectedHtmlPath)
                    ? "Click 'Browse...' to select an HTML bookmarks file."
                    : string.Format("Selected: {0}", Path.GetFileName(_selectedHtmlPath));
                _filePathLabel.Visible = true;
            }
            else
            {
                _filePathLabel.Visible = false;
                _dedicatedFolderBox.Text = "Imported from " + profile.BrowserName;
            }
        }

        private void BrowseBtn_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select Bookmarks HTML File";
                dlg.Filter = "HTML Files (*.html;*.htm)|*.html;*.htm|All Files (*.*)|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _selectedHtmlPath = dlg.FileName;
                    _filePathLabel.Text = string.Format("Selected: {0}", Path.GetFileName(_selectedHtmlPath));
                    _filePathLabel.Visible = true;
                }
            }
        }

        private void ImportBtn_Click(object sender, EventArgs e)
        {
            if (_isImporting) return;

            var profile = _sourceCombo.SelectedItem as BrowserProfileInfo;
            if (profile == null) return;

            if (profile.Type == BrowserType.HtmlFile && string.IsNullOrEmpty(_selectedHtmlPath))
            {
                MessageBox.Show(this, "Please select an HTML bookmark file first.", AppStrings.ImportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var options = new ImportDataOptions
            {
                ImportBookmarks = _bookmarksCheck.Checked,
                ImportHistory = _historyCheck.Checked,
                ImportShortcuts = _shortcutsCheck.Checked,
                SkipDuplicates = _skipDuplicatesCheck.Checked,
                PlaceInDedicatedFolder = _dedicatedRadio.Checked,
                DedicatedFolderName = _dedicatedFolderBox.Text.Trim()
            };

            _isImporting = true;
            _importBtn.Enabled = false;
            _progressBar.Visible = true;
            _statusLabel.Text = AppStrings.ImportData.StatusImporting;
            _statusLabel.ForeColor = UITheme.TextSecondary;

            Task.Run(() =>
            {
                var result = BrowserImportEngine.ExecuteImport(profile, _selectedHtmlPath, options);

                this.BeginInvoke(new Action(() =>
                {
                    _isImporting = false;
                    _importBtn.Enabled = true;
                    _progressBar.Visible = false;

                    if (result.Success)
                    {
                        _statusLabel.Text = result.FormatSummary();
                        _statusLabel.ForeColor = UITheme.AccentColor;
                        _cancelBtn.Text = AppStrings.General.BtnClose;
                        MessageBox.Show(this, result.FormatSummary(), AppStrings.ImportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        _statusLabel.Text = "Import failed: " + result.ErrorMessage;
                        _statusLabel.ForeColor = UITheme.StatusDanger;
                        MessageBox.Show(this, "Failed to import: " + result.ErrorMessage, AppStrings.ImportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }));
            });
        }
    }
}
