using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.DataPortability
{
    public class ExportDataView : UserControl
    {
        private Panel _centerContainer;
        private Label _bookmarksCountLabel;
        private Label _historyCountLabel;
        private Label _shortcutsCountLabel;

        private Action<string, bool> _openUrlCallback;
        private Action _closeTabCallback;

        public ExportDataView(Action<string, bool> openUrlCallback = null, Action closeTabCallback = null)
        {
            _openUrlCallback = openUrlCallback;
            _closeTabCallback = closeTabCallback;

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;

            InitializeComponent();
            RefreshCounts();
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
                Text = AppStrings.ExportData.PageTitle,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Location = new Point(0, y)
            };
            _centerContainer.Controls.Add(titleLabel);
            y += 36;

            Label subTitleLabel = new Label
            {
                Text = AppStrings.ExportData.SubTitle,
                Font = UITheme.FontDefault,
                ForeColor = UITheme.TextSecondary,
                AutoSize = false,
                Width = 700,
                Height = 32,
                Location = new Point(0, y)
            };
            _centerContainer.Controls.Add(subTitleLabel);
            y += 44;

            // 2. Card 1: Bookmarks & Folders
            Panel bmCard = CreateCard(700, 130);
            bmCard.Location = new Point(0, y);
            _centerContainer.Controls.Add(bmCard);

            Label bmTitle = new Label
            {
                Text = AppStrings.ExportData.CardBookmarksTitle,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Location = new Point(16, 14)
            };
            bmCard.Controls.Add(bmTitle);

            Label bmDesc = new Label
            {
                Text = AppStrings.ExportData.CardBookmarksDesc,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                AutoSize = false,
                Width = 660,
                Height = 32,
                Location = new Point(16, 36)
            };
            bmCard.Controls.Add(bmDesc);

            _bookmarksCountLabel = new Label
            {
                Text = "Loading bookmarks...",
                Font = UITheme.FontSmall,
                ForeColor = UITheme.AccentColor,
                AutoSize = true,
                Location = new Point(16, 80)
            };
            bmCard.Controls.Add(_bookmarksCountLabel);

            Button exportBmBtn = new Button
            {
                Text = AppStrings.ExportData.BtnExportBookmarks,
                Font = UITheme.FontDefault,
                Location = new Point(480, 74),
                Width = 196,
                Height = 32
            };
            UITheme.ApplyButton(exportBmBtn, isPrimary: true);
            exportBmBtn.Click += ExportBookmarksBtn_Click;
            bmCard.Controls.Add(exportBmBtn);

            y += bmCard.Height + 16;

            // 3. Card 2: Browsing History
            Panel histCard = CreateCard(700, 130);
            histCard.Location = new Point(0, y);
            _centerContainer.Controls.Add(histCard);

            Label histTitle = new Label
            {
                Text = AppStrings.ExportData.CardHistoryTitle,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Location = new Point(16, 14)
            };
            histCard.Controls.Add(histTitle);

            Label histDesc = new Label
            {
                Text = AppStrings.ExportData.CardHistoryDesc,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                AutoSize = false,
                Width = 660,
                Height = 32,
                Location = new Point(16, 36)
            };
            histCard.Controls.Add(histDesc);

            _historyCountLabel = new Label
            {
                Text = "Loading history...",
                Font = UITheme.FontSmall,
                ForeColor = UITheme.AccentColor,
                AutoSize = true,
                Location = new Point(16, 80)
            };
            histCard.Controls.Add(_historyCountLabel);

            Button exportHistCsvBtn = new Button
            {
                Text = AppStrings.ExportData.BtnExportHistoryCsv,
                Font = UITheme.FontDefault,
                Location = new Point(310, 74),
                Width = 176,
                Height = 32
            };
            UITheme.ApplyButton(exportHistCsvBtn);
            exportHistCsvBtn.Click += ExportHistoryCsvBtn_Click;
            histCard.Controls.Add(exportHistCsvBtn);

            Button exportHistHtmlBtn = new Button
            {
                Text = AppStrings.ExportData.BtnExportHistoryHtml,
                Font = UITheme.FontDefault,
                Location = new Point(496, 74),
                Width = 180,
                Height = 32
            };
            UITheme.ApplyButton(exportHistHtmlBtn);
            exportHistHtmlBtn.Click += ExportHistoryHtmlBtn_Click;
            histCard.Controls.Add(exportHistHtmlBtn);

            y += histCard.Height + 16;

            // 4. Card 3: New Tab Shortcuts
            Panel scCard = CreateCard(700, 130);
            scCard.Location = new Point(0, y);
            _centerContainer.Controls.Add(scCard);

            Label scTitle = new Label
            {
                Text = AppStrings.ExportData.CardShortcutsTitle,
                Font = UITheme.FontDefaultBold,
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Location = new Point(16, 14)
            };
            scCard.Controls.Add(scTitle);

            Label scDesc = new Label
            {
                Text = AppStrings.ExportData.CardShortcutsDesc,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                AutoSize = false,
                Width = 660,
                Height = 32,
                Location = new Point(16, 36)
            };
            scCard.Controls.Add(scDesc);

            _shortcutsCountLabel = new Label
            {
                Text = "Loading shortcuts...",
                Font = UITheme.FontSmall,
                ForeColor = UITheme.AccentColor,
                AutoSize = true,
                Location = new Point(16, 80)
            };
            scCard.Controls.Add(_shortcutsCountLabel);

            Button exportScBtn = new Button
            {
                Text = AppStrings.ExportData.BtnExportShortcuts,
                Font = UITheme.FontDefault,
                Location = new Point(480, 74),
                Width = 196,
                Height = 32
            };
            UITheme.ApplyButton(exportScBtn);
            exportScBtn.Click += ExportShortcutsBtn_Click;
            scCard.Controls.Add(exportScBtn);
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

        public void RefreshCounts()
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
            catch { }
        }

        private void ExportBookmarksBtn_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Export Bookmarks to HTML";
                dlg.Filter = "HTML Files (*.html)|*.html";
                dlg.FileName = string.Format("bookmarks_wb_{0}.html", DateTime.Now.ToString("yyyy-MM-dd"));

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        HtmlBookmarkExporter.ExportToFile(AppServices.Bookmarks, dlg.FileName);
                        MessageBox.Show(this, "Bookmarks successfully exported to:\n" + dlg.FileName, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to export bookmarks: " + ex.Message, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExportHistoryCsvBtn_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Export History to CSV";
                dlg.Filter = "CSV Files (*.csv)|*.csv";
                dlg.FileName = string.Format("history_wb_{0}.csv", DateTime.Now.ToString("yyyy-MM-dd"));

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        HistoryExporter.ExportToCsvFile(AppServices.History, dlg.FileName);
                        MessageBox.Show(this, "History successfully exported to:\n" + dlg.FileName, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to export history: " + ex.Message, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExportHistoryHtmlBtn_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Export History to HTML";
                dlg.Filter = "HTML Files (*.html)|*.html";
                dlg.FileName = string.Format("history_wb_{0}.html", DateTime.Now.ToString("yyyy-MM-dd"));

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        HistoryExporter.ExportToHtmlFile(AppServices.History, dlg.FileName);
                        MessageBox.Show(this, "History successfully exported to:\n" + dlg.FileName, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Failed to export history: " + ex.Message, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ExportShortcutsBtn_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Export Shortcuts to JSON";
                dlg.Filter = "JSON Files (*.json)|*.json";
                dlg.FileName = string.Format("shortcuts_wb_{0}.json", DateTime.Now.ToString("yyyy-MM-dd"));

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        ShortcutExporter.ExportToFile(AppServices.Shortcuts, dlg.FileName);
                        MessageBox.Show(this, "Shortcuts successfully exported to:\n" + dlg.FileName, AppStrings.ExportData.PageTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
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

