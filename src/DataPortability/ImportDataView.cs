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
    public class ImportDataView : UserControl
    {
        private Panel _centerContainer;
        private ComboBox _sourceCombo;
        private Button _browseBtn;
        private Label _filePathLabel;
        private Label _detectedBadge;

        private CheckBox _bookmarksCheck;
        private RadioButton _mergeRadio;
        private RadioButton _dedicatedRadio;
        private TextBox _dedicatedFolderBox;

        private CheckBox _historyCheck;
        private CheckBox _shortcutsCheck;
        private CheckBox _skipDuplicatesCheck;

        private Button _importBtn;
        private Label _statusLabel;
        private ProgressBar _progressBar;

        private List<BrowserProfileInfo> _detectedBrowsers;
        private string _selectedHtmlPath = null;
        private bool _isImporting = false;

        private Action<string, bool> _openUrlCallback;
        private Action _closeTabCallback;

        public ImportDataView(Action<string, bool> openUrlCallback = null, Action closeTabCallback = null)
        {
            _openUrlCallback = openUrlCallback;
            _closeTabCallback = closeTabCallback;

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;

            InitializeComponent();
            LoadBrowsersAsync();
        }

        private void InitializeComponent()
        {
            this.BackColor = UITheme.FormBackground;
            this.Font = UITheme.FontDefault;
            this.Size = new Size(900, 650);

            _centerContainer = new Panel
            {
                BackColor = UITheme.FormBackground,
                Width = 720,
                AutoScroll = true
            };
            this.Controls.Add(_centerContainer);

            this.Resize += (s, e) =>
            {
                _centerContainer.Location = new Point(
                    Math.Max(20, (this.ClientSize.Width - _centerContainer.Width) / 2),
                    20);
                _centerContainer.Height = Math.Max(100, this.ClientSize.Height - 40);
            };

            int y = 0;

            // 1. Header (Title + Subtitle)
            Label titleLabel = new Label
            {
                Text = AppStrings.ImportData.PageTitle,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Location = new Point(0, y)
            };
            _centerContainer.Controls.Add(titleLabel);
            y += 36;

            Label subTitleLabel = new Label
            {
                Text = AppStrings.ImportData.SubTitle,
                Font = UITheme.FontDefault,
                ForeColor = UITheme.TextSecondary,
                AutoSize = false,
                Width = 700,
                Height = 32,
                Location = new Point(0, y)
            };
            _centerContainer.Controls.Add(subTitleLabel);
            y += 44;

            // 2. Card 1: Source Selection
            Panel sourceCard = CreateCard(700, 150);
            sourceCard.Location = new Point(0, y);
            _centerContainer.Controls.Add(sourceCard);

            Label sourceHeader = new Label
            {
                Text = AppStrings.ImportData.SourceLabel,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Location = new Point(16, 14)
            };
            sourceCard.Controls.Add(sourceHeader);

            _sourceCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 28,
                Location = new Point(16, 40),
                Width = 520,
                Font = UITheme.FontDefault
            };
            _sourceCombo.DrawItem += SourceCombo_DrawItem;
            _sourceCombo.SelectedIndexChanged += SourceCombo_SelectedIndexChanged;
            UITheme.ApplyComboBox(_sourceCombo);
            sourceCard.Controls.Add(_sourceCombo);

            _browseBtn = new Button
            {
                Text = AppStrings.ImportData.BtnBrowse,
                Location = new Point(544, 39),
                Width = 100,
                Height = 30,
                Visible = false
            };
            UITheme.ApplyButton(_browseBtn);
            _browseBtn.Click += BrowseBtn_Click;
            sourceCard.Controls.Add(_browseBtn);

            _filePathLabel = new Label
            {
                Text = string.Empty,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                AutoSize = true,
                Location = new Point(16, 76),
                Visible = false
            };
            sourceCard.Controls.Add(_filePathLabel);

            _detectedBadge = new Label
            {
                Text = "Scanning for installed browsers...",
                Font = UITheme.FontSmall,
                ForeColor = UITheme.AccentColor,
                AutoSize = true,
                Location = new Point(16, 106)
            };
            sourceCard.Controls.Add(_detectedBadge);

            y += sourceCard.Height + 16;

            // 3. Card 2: Categories to Import
            Panel optionsCard = CreateCard(700, 260);
            optionsCard.Location = new Point(0, y);
            _centerContainer.Controls.Add(optionsCard);

            Label optionsHeader = new Label
            {
                Text = AppStrings.ImportData.CategoriesLabel,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Location = new Point(16, 14)
            };
            optionsCard.Controls.Add(optionsHeader);

            _bookmarksCheck = new CheckBox
            {
                Text = AppStrings.ImportData.OptionBookmarks,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                Checked = true,
                AutoSize = true,
                Location = new Point(16, 44)
            };
            UITheme.ApplyCheckBox(_bookmarksCheck);
            _bookmarksCheck.CheckedChanged += (s, e) =>
            {
                _mergeRadio.Enabled = _bookmarksCheck.Checked;
                _dedicatedRadio.Enabled = _bookmarksCheck.Checked;
                _dedicatedFolderBox.Enabled = _bookmarksCheck.Checked && _dedicatedRadio.Checked;
            };
            optionsCard.Controls.Add(_bookmarksCheck);

            _mergeRadio = new RadioButton
            {
                Text = AppStrings.ImportData.DestMerge,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                Checked = true,
                AutoSize = true,
                Location = new Point(40, 72)
            };
            UITheme.ApplyRadioButton(_mergeRadio);
            optionsCard.Controls.Add(_mergeRadio);

            _dedicatedRadio = new RadioButton
            {
                Text = AppStrings.ImportData.DestFolder,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                Checked = false,
                AutoSize = true,
                Location = new Point(40, 96)
            };
            UITheme.ApplyRadioButton(_dedicatedRadio);
            _dedicatedRadio.CheckedChanged += (s, e) =>
            {
                _dedicatedFolderBox.Enabled = _dedicatedRadio.Checked && _bookmarksCheck.Checked;
            };
            optionsCard.Controls.Add(_dedicatedRadio);

            _dedicatedFolderBox = new TextBox
            {
                Text = "Imported Bookmarks",
                Font = UITheme.FontDefault,
                Location = new Point(220, 93),
                Width = 260,
                Enabled = false
            };
            UITheme.ApplyTextBox(_dedicatedFolderBox);
            optionsCard.Controls.Add(_dedicatedFolderBox);

            _historyCheck = new CheckBox
            {
                Text = AppStrings.ImportData.OptionHistory,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                Checked = true,
                AutoSize = true,
                Location = new Point(16, 134)
            };
            UITheme.ApplyCheckBox(_historyCheck);
            optionsCard.Controls.Add(_historyCheck);

            _shortcutsCheck = new CheckBox
            {
                Text = AppStrings.ImportData.OptionShortcuts,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                Checked = true,
                AutoSize = true,
                Location = new Point(16, 168)
            };
            UITheme.ApplyCheckBox(_shortcutsCheck);
            optionsCard.Controls.Add(_shortcutsCheck);

            _skipDuplicatesCheck = new CheckBox
            {
                Text = AppStrings.ImportData.OptionSkipDuplicates,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                Checked = true,
                AutoSize = true,
                Location = new Point(16, 210)
            };
            UITheme.ApplyCheckBox(_skipDuplicatesCheck);
            optionsCard.Controls.Add(_skipDuplicatesCheck);

            y += optionsCard.Height + 16;

            // 4. Action Panel
            Panel actionPanel = new Panel
            {
                Width = 700,
                Height = 80,
                Location = new Point(0, y),
                BackColor = Color.Transparent
            };
            _centerContainer.Controls.Add(actionPanel);

            _importBtn = new Button
            {
                Text = AppStrings.ImportData.BtnImport,
                Font = UITheme.FontDefaultBold,
                Location = new Point(0, 0),
                Width = 140,
                Height = 36
            };
            UITheme.ApplyButton(_importBtn, isPrimary: true);
            _importBtn.Click += ImportBtn_Click;
            actionPanel.Controls.Add(_importBtn);

            _progressBar = new ProgressBar
            {
                Location = new Point(156, 8),
                Width = 240,
                Height = 20,
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };
            actionPanel.Controls.Add(_progressBar);

            _statusLabel = new Label
            {
                Text = AppStrings.ImportData.StatusReady,
                Font = UITheme.FontDefault,
                ForeColor = UITheme.TextSecondary,
                AutoSize = true,
                Location = new Point(0, 46)
            };
            actionPanel.Controls.Add(_statusLabel);
        }

        private Panel CreateCard(int width, int height)
        {
            Panel card = new Panel
            {
                Width = width,
                Height = height,
                BackColor = UITheme.PanelBackground
            };
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(UITheme.BorderColor, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };
            return card;
        }

        private void LoadBrowsersAsync()
        {
            Task.Run(() =>
            {
                var browsers = BrowserDetector.DetectInstalledBrowsers();
                this.BeginInvoke(new Action(() =>
                {
                    _detectedBrowsers = browsers;
                    _sourceCombo.Items.Clear();
                    for (int i = 0; i < browsers.Count; i++)
                    {
                        _sourceCombo.Items.Add(browsers[i]);
                    }
                    if (_sourceCombo.Items.Count > 0)
                    {
                        _sourceCombo.SelectedIndex = 0;
                    }
                }));
            });
        }

        private void SourceCombo_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || _detectedBrowsers == null || e.Index >= _detectedBrowsers.Count) return;

            var profile = _detectedBrowsers[e.Index];
            Graphics g = e.Graphics;

            // Draw app icon
            if (profile.AppIcon != null)
            {
                int iconY = e.Bounds.Top + (e.Bounds.Height - 20) / 2;
                g.DrawImage(profile.AppIcon, new Rectangle(e.Bounds.Left + 4, iconY, 20, 20));
            }

            // Draw text
            string text = profile.DisplayName;
            Rectangle textRect = new Rectangle(e.Bounds.Left + 28, e.Bounds.Top, e.Bounds.Width - 28, e.Bounds.Height);
            Color textColor = (e.State & DrawItemState.Selected) != 0 ? Color.White : UITheme.TextPrimary;

            TextRenderer.DrawText(g, text, UITheme.FontDefault, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
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
                _detectedBadge.Visible = true;
                _detectedBadge.Text = string.IsNullOrEmpty(_selectedHtmlPath)
                    ? "Click 'Browse...' to select an HTML bookmarks file."
                    : string.Format("Selected file: {0}", Path.GetFileName(_selectedHtmlPath));
            }
            else
            {
                _detectedBadge.Visible = false;
                _detectedBadge.Text = string.Empty;
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
                    _filePathLabel.Text = _selectedHtmlPath;
                    _filePathLabel.Visible = true;
                    _detectedBadge.Text = string.Format("Selected: {0}", Path.GetFileName(_selectedHtmlPath));
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
                MessageBox.Show(this, "Please select an HTML bookmark file first.", "Import Data", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

