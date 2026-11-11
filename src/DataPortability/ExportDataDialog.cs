using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.DataPortability
{
    /// <summary>
    /// Modal dialog window for exporting bookmarks, history, and shortcuts.
    /// Follows the SponsorBlock settings form architecture and design language.
    /// </summary>
    public class ExportDataDialog : Form
    {
        private Label _bookmarksCountLabel;
        private Label _historyCountLabel;
        private Label _shortcutsCountLabel;
        private Label _statusLabel;
        private Button _closeBtn;

        private Button _exportBookmarksBtn;
        private Button _exportHistoryCsvBtn;
        private Button _exportHistoryHtmlBtn;
        private Button _exportShortcutsBtn;

        public static void ShowExportDialog(IWin32Window owner = null)
        {
            using (var dlg = new ExportDataDialog())
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

        public ExportDataDialog()
        {
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            this.DoubleBuffered = true;

            InitializeComponent();
            UpdateCounts();
        }

        private void InitializeComponent()
        {
            UITheme.ApplyForm(this, AppStrings.ExportData.WindowTitle, new Size(580, 500), new Size(520, 460));
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;

            TableLayoutPanel mainLayout = new TableLayoutPanel();
            mainLayout.Dock = DockStyle.Fill;
            mainLayout.RowCount = 3;
            mainLayout.ColumnCount = 1;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));   // Top Header
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Main Scrollable Content
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));   // Bottom Bar
            this.Controls.Add(mainLayout);

            // 1. Top Header
            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Fill;
            UITheme.ApplyTopToolbar(topPanel);
            topPanel.Padding = new Padding(20, 10, 20, 10);

            Label headerLabel = new Label();
            headerLabel.UseMnemonic = false;
            headerLabel.Text = AppStrings.ExportData.PageTitle;
            headerLabel.Font = new Font(UITheme.FontDefault.FontFamily, 13F, FontStyle.Bold);
            headerLabel.ForeColor = UITheme.TextPrimary;
            headerLabel.AutoSize = true;
            headerLabel.Dock = DockStyle.Top;
            topPanel.Controls.Add(headerLabel);

            Label subLabel = new Label();
            subLabel.UseMnemonic = false;
            subLabel.Text = AppStrings.ExportData.SubTitle;
            subLabel.Font = UITheme.FontSmall;
            subLabel.ForeColor = UITheme.TextSecondary;
            subLabel.AutoSize = true;
            subLabel.Dock = DockStyle.Top;
            subLabel.Padding = new Padding(0, 3, 0, 10);
            topPanel.Controls.Add(subLabel);
            subLabel.BringToFront();

            mainLayout.Controls.Add(topPanel, 0, 0);

            // 2. Main Scrollable Content: Categories Table
            Panel scrollContainer = new Panel();
            scrollContainer.Dock = DockStyle.Fill;
            scrollContainer.AutoScroll = true;
            scrollContainer.Padding = new Padding(20, 14, 20, 8);
            scrollContainer.BackColor = UITheme.FormBackground;

            Label secTitle = new Label();
            secTitle.UseMnemonic = false;
            secTitle.Text = "ITEMS TO EXPORT";
            secTitle.Font = new Font(UITheme.FontDefault.FontFamily, 9F, FontStyle.Bold);
            secTitle.ForeColor = UITheme.TextSecondary;
            secTitle.AutoSize = true;
            secTitle.Dock = DockStyle.Top;
            secTitle.Padding = new Padding(0, 2, 0, 8);
            scrollContainer.Controls.Add(secTitle);

            TableLayoutPanel exportTable = new TableLayoutPanel();
            exportTable.Dock = DockStyle.Top;
            exportTable.BackColor = UITheme.FormBackground;
            exportTable.AutoSize = true;
            exportTable.ColumnCount = 3;
            exportTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));  // Icon
            exportTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // Info (Name, Desc, Count)
            exportTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // Action Buttons

            // --- Row 0: Bookmarks ---
            exportTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Panel bmIconPanel = CreateIconPanel("folder");
            exportTable.Controls.Add(bmIconPanel, 0, 0);

            FlowLayoutPanel bmInfoFlow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0, 3, 12, 14)
            };

            Label bmTitle = new Label
            {
                Text = AppStrings.ExportData.CardBookmarksTitle,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2)
            };
            bmInfoFlow.Controls.Add(bmTitle);

            Label bmDesc = new Label
            {
                Text = AppStrings.ExportData.CardBookmarksDesc,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                AutoSize = true,
                MaximumSize = Size.Empty,
                Margin = new Padding(0, 0, 0, 4)
            };
            bmInfoFlow.Controls.Add(bmDesc);

            _bookmarksCountLabel = new Label
            {
                Text = "Loading bookmark counts...",
                Font = UITheme.FontSmall,
                ForeColor = UITheme.AccentColor,
                AutoSize = true,
                Margin = Padding.Empty
            };
            bmInfoFlow.Controls.Add(_bookmarksCountLabel);
            exportTable.Controls.Add(bmInfoFlow, 1, 0);

            FlowLayoutPanel bmButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 0),
                Dock = DockStyle.Fill
            };

            _exportBookmarksBtn = new Button
            {
                Text = AppStrings.ExportData.BtnExportBookmarks,
                AutoSize = true,
                Height = 30,
                Margin = Padding.Empty
            };
            UITheme.ApplyButton(_exportBookmarksBtn, isPrimary: false);
            _exportBookmarksBtn.Click += ExportBookmarks_Click;
            bmButtons.Controls.Add(_exportBookmarksBtn);
            exportTable.Controls.Add(bmButtons, 2, 0);

            // --- Row 1: Browsing History ---
            exportTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Panel histIconPanel = CreateIconPanel("clock-counter-clockwise");
            exportTable.Controls.Add(histIconPanel, 0, 1);

            FlowLayoutPanel histInfoFlow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0, 3, 12, 14)
            };

            Label histTitle = new Label
            {
                Text = AppStrings.ExportData.CardHistoryTitle,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2)
            };
            histInfoFlow.Controls.Add(histTitle);

            Label histDesc = new Label
            {
                Text = AppStrings.ExportData.CardHistoryDesc,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                AutoSize = true,
                MaximumSize = Size.Empty,
                Margin = new Padding(0, 0, 0, 4)
            };
            histInfoFlow.Controls.Add(histDesc);

            _historyCountLabel = new Label
            {
                Text = "Loading history counts...",
                Font = UITheme.FontSmall,
                ForeColor = UITheme.AccentColor,
                AutoSize = true,
                Margin = Padding.Empty
            };
            histInfoFlow.Controls.Add(_historyCountLabel);
            exportTable.Controls.Add(histInfoFlow, 1, 1);

            FlowLayoutPanel histButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 0),
                Dock = DockStyle.Fill
            };

            _exportHistoryHtmlBtn = new Button
            {
                Text = AppStrings.ExportData.BtnExportHistoryHtml,
                AutoSize = true,
                Height = 30,
                Margin = Padding.Empty
            };
            UITheme.ApplyButton(_exportHistoryHtmlBtn, isPrimary: false);
            _exportHistoryHtmlBtn.Click += ExportHistoryHtml_Click;
            histButtons.Controls.Add(_exportHistoryHtmlBtn);

            _exportHistoryCsvBtn = new Button
            {
                Text = AppStrings.ExportData.BtnExportHistoryCsv,
                AutoSize = true,
                Height = 30,
                Margin = new Padding(0, 0, 6, 0)
            };
            UITheme.ApplyButton(_exportHistoryCsvBtn, isPrimary: false);
            _exportHistoryCsvBtn.Click += ExportHistoryCsv_Click;
            histButtons.Controls.Add(_exportHistoryCsvBtn);

            exportTable.Controls.Add(histButtons, 2, 1);

            // --- Row 2: New Tab Shortcuts ---
            exportTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Panel scIconPanel = CreateIconPanel("globe");
            exportTable.Controls.Add(scIconPanel, 0, 2);

            FlowLayoutPanel scInfoFlow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0, 3, 12, 14)
            };

            Label scTitle = new Label
            {
                Text = AppStrings.ExportData.CardShortcutsTitle,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2)
            };
            scInfoFlow.Controls.Add(scTitle);

            Label scDesc = new Label
            {
                Text = AppStrings.ExportData.CardShortcutsDesc,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                AutoSize = true,
                MaximumSize = Size.Empty,
                Margin = new Padding(0, 0, 0, 4)
            };
            scInfoFlow.Controls.Add(scDesc);

            _shortcutsCountLabel = new Label
            {
                Text = "Loading shortcuts count...",
                Font = UITheme.FontSmall,
                ForeColor = UITheme.AccentColor,
                AutoSize = true,
                Margin = Padding.Empty
            };
            scInfoFlow.Controls.Add(_shortcutsCountLabel);
            exportTable.Controls.Add(scInfoFlow, 1, 2);

            FlowLayoutPanel scButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 0),
                Dock = DockStyle.Fill
            };

            _exportShortcutsBtn = new Button
            {
                Text = AppStrings.ExportData.BtnExportShortcuts,
                AutoSize = true,
                Height = 30,
                Margin = Padding.Empty
            };
            UITheme.ApplyButton(_exportShortcutsBtn, isPrimary: false);
            _exportShortcutsBtn.Click += ExportShortcuts_Click;
            scButtons.Controls.Add(_exportShortcutsBtn);
            exportTable.Controls.Add(scButtons, 2, 2);

            scrollContainer.Controls.Add(exportTable);
            exportTable.BringToFront();

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
            bottomLayout.Padding = new Padding(16, 0, 16, 0);

            _statusLabel = new Label
            {
                AutoSize = true,
                ForeColor = UITheme.AccentColor,
                Font = UITheme.FontSmall,
                Margin = new Padding(0, 14, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };
            bottomLayout.Controls.Add(_statusLabel, 0, 0);

            FlowLayoutPanel rightButtons = new FlowLayoutPanel();
            rightButtons.Dock = DockStyle.Fill;
            rightButtons.AutoSize = true;
            rightButtons.FlowDirection = FlowDirection.RightToLeft;
            rightButtons.WrapContents = false;

            _closeBtn = new Button
            {
                Text = AppStrings.General.BtnClose,
                Size = new Size(84, 28),
                DialogResult = DialogResult.OK,
                Margin = new Padding(0, 8, 4, 8)
            };
            UITheme.ApplyButton(_closeBtn, isPrimary: false);
            _closeBtn.Click += (s, e) => this.Close();
            rightButtons.Controls.Add(_closeBtn);

            bottomLayout.Controls.Add(rightButtons, 2, 0);
            mainLayout.Controls.Add(bottomLayout, 0, 2);

            this.CancelButton = _closeBtn;
        }

        private Panel CreateIconPanel(string iconName)
        {
            Panel p = new Panel
            {
                Size = new Size(22, 22),
                Margin = new Padding(2, 5, 6, 0)
            };

            p.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                MaterialIcons.DrawIcon(e.Graphics, iconName, new Rectangle(0, 1, 20, 20), ThemeManager.CurrentAccentColor);
            };

            return p;
        }

        private void UpdateCounts()
        {
            try
            {
                var bm = AppServices.Bookmarks.GetAll();
                int bmCount = 0;
                int folderCount = 0;
                for (int i = 0; i < bm.Count; i++)
                {
                    if (bm[i] != null && bm[i].IsFolder) folderCount++;
                    else bmCount++;
                }
                _bookmarksCountLabel.Text = string.Format("Currently saved: {0} bookmarks across {1} folders", bmCount, folderCount);

                int histCount = AppServices.History.GetAll().Count;
                _historyCountLabel.Text = string.Format("Currently recorded: {0} visits", histCount);

                int scCount = AppServices.Shortcuts.GetAll().Count;
                _shortcutsCountLabel.Text = string.Format("Currently configured: {0} shortcut tiles", scCount);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Count update: " + ex.Message;
            }
        }

        private void ExportBookmarks_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export Bookmarks to HTML";
                sfd.Filter = "HTML Files (*.html)|*.html|All Files (*.*)|*.*";
                sfd.FileName = string.Format("wb_bookmarks_{0:yyyyMMdd}.html", DateTime.Now);
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        HtmlBookmarkExporter.ExportToFile(AppServices.Bookmarks, sfd.FileName);
                        _statusLabel.Text = "Bookmarks exported to " + Path.GetFileName(sfd.FileName);
                        MessageBox.Show(this, "Bookmarks exported successfully!\n\nSaved to: " + sfd.FileName, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to export bookmarks: " + ex.Message, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExportHistoryCsv_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export History to CSV";
                sfd.Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*";
                sfd.FileName = string.Format("wb_history_{0:yyyyMMdd}.csv", DateTime.Now);
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        HistoryExporter.ExportToCsvFile(AppServices.History, sfd.FileName);
                        _statusLabel.Text = "History exported to " + Path.GetFileName(sfd.FileName);
                        MessageBox.Show(this, "History exported successfully!\n\nSaved to: " + sfd.FileName, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to export history: " + ex.Message, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExportHistoryHtml_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export History to HTML";
                sfd.Filter = "HTML Files (*.html)|*.html|All Files (*.*)|*.*";
                sfd.FileName = string.Format("wb_history_{0:yyyyMMdd}.html", DateTime.Now);
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        HistoryExporter.ExportToHtmlFile(AppServices.History, sfd.FileName);
                        _statusLabel.Text = "History exported to " + Path.GetFileName(sfd.FileName);
                        MessageBox.Show(this, "History exported successfully!\n\nSaved to: " + sfd.FileName, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to export history: " + ex.Message, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExportShortcuts_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export Shortcuts to JSON";
                sfd.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";
                sfd.FileName = string.Format("wb_shortcuts_{0:yyyyMMdd}.json", DateTime.Now);
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        ShortcutExporter.ExportToFile(AppServices.Shortcuts, sfd.FileName);
                        _statusLabel.Text = "Shortcuts exported to " + Path.GetFileName(sfd.FileName);
                        MessageBox.Show(this, "Shortcuts exported successfully!\n\nSaved to: " + sfd.FileName, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to export shortcuts: " + ex.Message, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}
