using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Downloads
{
    public class DownloadsView : UserControl
    {
        private Panel _centerContainer;
        private TextBox _searchBox;
        private ComboBox _sortCombo;
        private ListView _listView;
        private Button _addDownloadBtn;
        private Button _checkMissingBtn;
        private Button _clearBtn;
        private Label _countLabel;
        private ContextMenuStrip _contextMenu;

        private Button _pauseResumeBtn;
        private Button _cancelBtn;
        private Button _retryBtn;
        private Button _openFileBtn;
        private Button _openFolderBtn;
        private Button _copyUrlBtn;
        private Button _deleteBtn;
        private Timer _refreshTimer;

        private DownloadItemModel _currentSelectedModel;
        private SortMode _currentSort = SortMode.DateDescending;
        private Action _closeAction;

        public DownloadsView(Action closeAction = null)
        {
            _closeAction = closeAction;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            InitializeComponent();
            RefreshList();
            HookDownloadEvents();

            _refreshTimer = new Timer();
            _refreshTimer.Interval = 500;
            _refreshTimer.Tick += (s, e) => UpdateDetailsPanel();
            _refreshTimer.Start();
        }

        private void InitializeComponent()
        {
            this.BackColor = UITheme.FormBackground;
            this.Font = UITheme.FontDefault;
            this.Margin = Padding.Empty;
            this.Padding = Padding.Empty;

            // Centered layout container
            _centerContainer = new Panel
            {
                BackColor = UITheme.FormBackground,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            // 1. Top Controls Bar
            Panel topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = UITheme.FormBackground
            };

            // Left: Title + Hard Count Badge
            FlowLayoutPanel topLeft = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            Label titleLabel = new Label
            {
                Text = AppStrings.Downloads.PageTitle,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 10, 0)
            };

            topLeft.Controls.Add(titleLabel);

            // Right: Actions (Search, Sort, Check Missing, Clear Inactive, Add Download Primary)
            FlowLayoutPanel topRight = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _searchBox = new TextBox
            {
                Width = 160,
                Margin = new Padding(0, 2, 6, 0)
            };
            UITheme.ApplyTextBox(_searchBox, AppStrings.Downloads.SearchPlaceholder);
            _searchBox.TextChanged += (s, e) => RefreshList();

            _sortCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 145,
                Margin = new Padding(0, 2, 6, 0)
            };
            _sortCombo.Items.AddRange(new object[] {
                AppStrings.Downloads.SortNameAsc,
                AppStrings.Downloads.SortNameDesc,
                AppStrings.Downloads.SortDateDesc,
                AppStrings.Downloads.SortDateAsc
            });
            UITheme.ApplyComboBox(_sortCombo);
            _sortCombo.SelectedIndex = 2;
            _sortCombo.SelectedIndexChanged += SortCombo_SelectedIndexChanged;

            _checkMissingBtn = new Button
            {
                Text = AppStrings.Downloads.BtnCheckMissing,
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 6, 0)
            };
            UITheme.ApplyButton(_checkMissingBtn);
            _checkMissingBtn.Click += CheckMissingBtn_Click;

            _clearBtn = new Button
            {
                Text = AppStrings.Downloads.BtnClear,
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 6, 0)
            };
            UITheme.ApplyButton(_clearBtn);
            _clearBtn.Click += ClearBtn_Click;

            // Primary Button (+ Add Download) with strictly BLACK LABEL!
            _addDownloadBtn = new Button
            {
                Text = AppStrings.Downloads.BtnAdd,
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 0, 0)
            };
            UITheme.ApplyButton(_addDownloadBtn, isPrimary: true);
            _addDownloadBtn.Click += AddDownloadBtn_Click;

            // Add in natural logical order: Search -> Sort -> Check Missing -> Clear Finished -> + Add Download
            topRight.Controls.Add(_searchBox);
            topRight.Controls.Add(_sortCombo);
            topRight.Controls.Add(_checkMissingBtn);
            topRight.Controls.Add(_clearBtn);
            topRight.Controls.Add(_addDownloadBtn);

            topBar.Controls.Add(topLeft);
            topBar.Controls.Add(topRight);

            topBar.Layout += (s, e) =>
            {
                topLeft.Location = new Point(0, Math.Max(0, (topBar.ClientSize.Height - topLeft.Height) / 2));
                int rightX = Math.Max(topLeft.Right + 16, topBar.ClientSize.Width - topRight.PreferredSize.Width);
                topRight.Location = new Point(rightX, Math.Max(0, (topBar.ClientSize.Height - topRight.Height) / 2));
            };

            // Context Menu
            _contextMenu = new ContextMenuStrip();
            ToolStripMenuItem ctxStart = new ToolStripMenuItem(AppStrings.Downloads.CtxStart, null, (s, e) => StartSelectedDownload());
            ToolStripMenuItem ctxPause = new ToolStripMenuItem(AppStrings.Downloads.CtxPause, null, (s, e) => PauseSelectedDownload());
            ToolStripMenuItem ctxCancel = new ToolStripMenuItem(AppStrings.Downloads.CtxCancel, null, (s, e) => CancelSelectedDownload());
            ToolStripMenuItem ctxRetry = new ToolStripMenuItem(AppStrings.Downloads.CtxRetry, null, (s, e) => RetrySelectedDownload());
            ToolStripMenuItem ctxOpenFile = new ToolStripMenuItem(AppStrings.Downloads.CtxOpenFile, null, (s, e) => OpenSelectedFile());
            ToolStripMenuItem ctxOpenFolder = new ToolStripMenuItem(AppStrings.Downloads.CtxOpenFolder, null, (s, e) => OpenSelectedFolder());
            ToolStripMenuItem ctxCopyUrl = new ToolStripMenuItem(AppStrings.Downloads.CtxCopyUrl, null, (s, e) => CopySelectedUrl());
            ToolStripMenuItem ctxDeleteList = new ToolStripMenuItem(AppStrings.Downloads.CtxDeleteList, null, (s, e) => DeleteSelected(false));
            ToolStripMenuItem ctxDeleteDisk = new ToolStripMenuItem(AppStrings.Downloads.CtxDeleteDisk, null, (s, e) => DeleteSelected(true));

            _contextMenu.Opening += (s, e) =>
            {
                bool hasSelection = _listView.SelectedItems.Count > 0;
                ctxStart.Enabled = hasSelection;
                ctxPause.Enabled = hasSelection;
                ctxCancel.Enabled = hasSelection;
                ctxRetry.Enabled = hasSelection;
                ctxOpenFile.Enabled = hasSelection;
                ctxOpenFolder.Enabled = hasSelection;
                ctxCopyUrl.Enabled = hasSelection;
                ctxDeleteList.Enabled = hasSelection;
                ctxDeleteDisk.Enabled = hasSelection;
            };

            _contextMenu.Items.AddRange(new ToolStripItem[] {
                ctxStart, ctxPause, ctxCancel, ctxRetry,
                new ToolStripSeparator(),
                ctxOpenFile, ctxOpenFolder, ctxCopyUrl,
                new ToolStripSeparator(),
                ctxDeleteList, ctxDeleteDisk
            });
            UITheme.ApplyContextMenu(_contextMenu);

            // 2. Modern Center ListView
            Panel listBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UITheme.FormBackground,
                Padding = new Padding(1)
            };
            listBorderPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(MaterialColors.Separator, 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, listBorderPanel.Width - 1, listBorderPanel.Height - 1);
                }
            };

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = true,
                HideSelection = false,
                ContextMenuStrip = _contextMenu
            };

            _listView.Columns.Add(AppStrings.Downloads.ColFileName, 260);
            _listView.Columns.Add(AppStrings.Downloads.ColStatus, 140);
            _listView.Columns.Add(AppStrings.Downloads.ColProgress, 160);
            _listView.Columns.Add(AppStrings.Downloads.ColSpeed, 110);
            _listView.Columns.Add(AppStrings.Downloads.ColSize, 140);
            _listView.Columns.Add(AppStrings.Downloads.ColDateStarted, 180);

            _listView.ColumnClick += ListView_ColumnClick;
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
            _listView.DoubleClick += (s, e) => ListView_ItemDoubleClicked();
            _listView.KeyDown += ListView_KeyDown;

            // Apply Modern ListView styling with custom drawing for Status Badge and Inline Progress Bar
            UITheme.ApplyModernListView(_listView, 40, true, DrawDownloadsSubItem);

            listBorderPanel.Controls.Add(_listView);

            // 3. Bottom Controls Bar
            Panel bottomBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                BackColor = UITheme.FormBackground
            };

            _countLabel = new Label
            {
                AutoSize = true,
                Font = UITheme.FontDefault,
                ForeColor = UITheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = Padding.Empty
            };

            FlowLayoutPanel bottomRight = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _deleteBtn = new Button { Text = AppStrings.Downloads.BtnDelete, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_deleteBtn);
            _deleteBtn.Click += (s, e) => DeleteBtn_Click(s, e);

            _copyUrlBtn = new Button { Text = AppStrings.Downloads.BtnCopyLink, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_copyUrlBtn);
            _copyUrlBtn.Click += (s, e) => CopySelectedUrl();

            _openFolderBtn = new Button { Text = AppStrings.Downloads.BtnOpenFolder, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_openFolderBtn);
            _openFolderBtn.Click += (s, e) => OpenSelectedFolder();

            _retryBtn = new Button { Text = AppStrings.Downloads.BtnRetry, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_retryBtn);
            _retryBtn.Click += (s, e) => RetrySelectedDownload();

            _cancelBtn = new Button { Text = AppStrings.Downloads.BtnCancel, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_cancelBtn);
            _cancelBtn.Click += (s, e) => CancelSelectedDownload();

            _pauseResumeBtn = new Button { Text = AppStrings.Downloads.BtnPause, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_pauseResumeBtn);
            _pauseResumeBtn.Click += (s, e) => TogglePauseResumeSelected();

            _openFileBtn = new Button { Text = AppStrings.Downloads.BtnOpenFile, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 0, 0) };
            UITheme.ApplyButton(_openFileBtn, isPrimary: true);
            _openFileBtn.Click += (s, e) => OpenSelectedFile();

            bottomRight.Controls.Add(_deleteBtn);
            bottomRight.Controls.Add(_copyUrlBtn);
            bottomRight.Controls.Add(_openFolderBtn);
            bottomRight.Controls.Add(_retryBtn);
            bottomRight.Controls.Add(_cancelBtn);
            bottomRight.Controls.Add(_pauseResumeBtn);
            bottomRight.Controls.Add(_openFileBtn);

            if (_closeAction != null)
            {
                Button closeBtn = new Button { Text = AppStrings.General.BtnClose, AutoSize = true, Height = 30, Margin = new Padding(6, 0, 0, 0) };
                UITheme.ApplyButton(closeBtn);
                closeBtn.Click += (s, e) => _closeAction();
                bottomRight.Controls.Add(closeBtn);
            }

            bottomBar.Controls.Add(_countLabel);
            bottomBar.Controls.Add(bottomRight);

            bottomBar.Layout += (s, e) =>
            {
                _countLabel.Location = new Point(0, Math.Max(0, (bottomBar.ClientSize.Height - _countLabel.Height) / 2));
                int rightX = Math.Max(_countLabel.Right + 16, bottomBar.ClientSize.Width - bottomRight.PreferredSize.Width);
                bottomRight.Location = new Point(rightX, Math.Max(0, (bottomBar.ClientSize.Height - bottomRight.Height) / 2));
            };

            // Assemble centered container
            _centerContainer.Controls.Add(listBorderPanel);
            _centerContainer.Controls.Add(bottomBar);
            _centerContainer.Controls.Add(topBar);

            this.Controls.Add(_centerContainer);

            UpdateDetailsPanel();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_centerContainer != null)
            {
                int targetWidth = Math.Min(1020, Math.Max(500, this.ClientSize.Width - 32));
                int left = Math.Max(16, (this.ClientSize.Width - targetWidth) / 2);
                int top = 16;
                int height = Math.Max(100, this.ClientSize.Height - 32);
                _centerContainer.SetBounds(left, top, targetWidth, height);
            }
        }

        private void DrawDownloadsSubItem(DrawListViewSubItemEventArgs e, bool isSelected, bool isHovered)
        {
            if (e.Item == null) return;
            DownloadItemModel item = e.Item.Tag as DownloadItemModel;
            string status = item != null ? item.GetStatusDisplay() : (e.Item.SubItems.Count > 1 ? e.Item.SubItems[1].Text : string.Empty);
            if (string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase)) status = "Downloading";

            Rectangle bounds = e.Bounds;
            Color bg = (e.ItemIndex % 2 == 0) ? Color.FromArgb(32, 33, 36) : Color.FromArgb(36, 37, 40);
            if (isHovered) bg = Color.FromArgb(48, 50, 54);
            if (isSelected) bg = Color.FromArgb(28, 45, 70);

            if (e.ColumnIndex == 1) // STATUS BADGE (HARD SHAPE)
            {
                Color borderCol = ThemeManager.CurrentAccentColor;
                Color bgCol = Color.FromArgb(20, 35, 55);
                Color fgCol = ThemeManager.CurrentAccentColor;
                string label = status.ToUpperInvariant();

                if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    borderCol = MaterialColors.StatusSuccess;
                    bgCol = Color.FromArgb(20, 40, 28);
                    fgCol = MaterialColors.StatusSuccess;
                }
                else if (string.Equals(status, "Paused", StringComparison.OrdinalIgnoreCase))
                {
                    borderCol = MaterialColors.StatusWarning;
                    bgCol = Color.FromArgb(45, 40, 20);
                    fgCol = MaterialColors.StatusWarning;
                }
                else if (status.StartsWith("Missing", StringComparison.OrdinalIgnoreCase))
                {
                    borderCol = MaterialColors.StatusDanger;
                    bgCol = Color.FromArgb(45, 25, 25);
                    fgCol = MaterialColors.StatusDanger;
                    label = "MISSING";
                }
                else if (status.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Interrupted", StringComparison.OrdinalIgnoreCase))
                {
                    borderCol = MaterialColors.StatusDanger;
                    bgCol = Color.FromArgb(45, 25, 25);
                    fgCol = MaterialColors.StatusDanger;
                }
                else if (string.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase))
                {
                    borderCol = UITheme.StatusInfo;
                    bgCol = Color.FromArgb(20, 35, 50);
                    fgCol = UITheme.StatusInfo;
                }

                Size sz = TextRenderer.MeasureText(label, UITheme.FontSmall, Size.Empty, TextFormatFlags.NoPrefix);
                int badgeW = Math.Min(bounds.Width - 16, sz.Width + 16);
                int badgeH = 22;
                int badgeY = bounds.Y + (bounds.Height - badgeH) / 2;
                Rectangle badgeRect = new Rectangle(bounds.X + 8, badgeY, badgeW, badgeH);

                using (SolidBrush b = new SolidBrush(bgCol))
                {
                    e.Graphics.FillRectangle(b, badgeRect); // Hard shape (0 radius)
                }
                using (Pen p = new Pen(borderCol, 1))
                {
                    e.Graphics.DrawRectangle(p, badgeRect);
                }
                TextRenderer.DrawText(e.Graphics, label, UITheme.FontSmall, badgeRect, fgCol, bgCol,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else if (e.ColumnIndex == 2) // PROGRESS (COMPACT 6PX BAR + PERCENT TEXT)
            {
                int pct = item != null ? Math.Min(100, Math.Max(0, item.Percent)) : 0;
                int barW = Math.Max(20, bounds.Width - 56);
                int barH = 6;
                int barY = bounds.Y + (bounds.Height - barH) / 2;

                Rectangle trackRect = new Rectangle(bounds.X + 8, barY, barW, barH);
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(48, 50, 54)))
                {
                    e.Graphics.FillRectangle(tb, trackRect); // Hard shape
                }
                using (Pen tp = new Pen(MaterialColors.Separator, 1))
                {
                    e.Graphics.DrawRectangle(tp, trackRect);
                }

                Color fillCol = ThemeManager.CurrentAccentColor;
                if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)) fillCol = MaterialColors.StatusSuccess;
                if (string.Equals(status, "Paused", StringComparison.OrdinalIgnoreCase)) fillCol = MaterialColors.StatusWarning;
                if (status.StartsWith("Missing", StringComparison.OrdinalIgnoreCase) || status.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)) fillCol = MaterialColors.StatusDanger;

                int fillW = (int)Math.Round((barW * pct) / 100.0);
                if (fillW > 0)
                {
                    using (SolidBrush fb = new SolidBrush(fillCol))
                    {
                        e.Graphics.FillRectangle(fb, trackRect.X, trackRect.Y, fillW, barH); // Hard shape
                    }
                }

                Rectangle pctRect = new Rectangle(bounds.X + barW + 14, bounds.Y, 40, bounds.Height);
                TextRenderer.DrawText(e.Graphics, pct + "%", UITheme.FontSmall, pctRect, MaterialColors.TextPrimary, bg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                string text = e.SubItem != null ? e.SubItem.Text : string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    Font font = (e.ColumnIndex == 0) ? UITheme.FontDefaultBold : UITheme.FontSmall;
                    Color fg = (e.ColumnIndex == 0) ? MaterialColors.TextPrimary : MaterialColors.TextSecondary;
                    int pad = (e.ColumnIndex == 0) ? 14 : 8;
                    Rectangle textRect = new Rectangle(bounds.X + pad, bounds.Y, Math.Max(0, bounds.Width - pad - 6), bounds.Height);
                    TextRenderer.DrawText(e.Graphics, text, font, textRect, fg, bg,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.NoPrefix);
                }
            }
        }

        private void HookDownloadEvents()
        {
            AppServices.Downloads.DownloadStarted += Downloads_Changed;
            AppServices.Downloads.DownloadUpdated += Downloads_Changed;
            AppServices.Downloads.DownloadCompleted += Downloads_Changed;
            AppServices.Downloads.DownloadsCleared += Downloads_Cleared;
        }

        private void UnhookDownloadEvents()
        {
            AppServices.Downloads.DownloadStarted -= Downloads_Changed;
            AppServices.Downloads.DownloadUpdated -= Downloads_Changed;
            AppServices.Downloads.DownloadCompleted -= Downloads_Changed;
            AppServices.Downloads.DownloadsCleared -= Downloads_Cleared;
        }

        private void Downloads_Cleared(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired) this.BeginInvoke(new Action(RefreshList));
                else RefreshList();
            }
            catch { }
        }

        private void Downloads_Changed(object sender, DownloadItemModel item)
        {
            if (this.IsDisposed) return;
            try
            {
                Action updateAction = () =>
                {
                    if (_currentSelectedModel != null && item != null && _currentSelectedModel.Id == item.Id)
                    {
                        _currentSelectedModel = item;
                    }
                    RefreshList();
                    UpdateDetailsPanel();
                };

                if (this.InvokeRequired) this.BeginInvoke(updateAction);
                else updateAction();
            }
            catch { }
        }

        public void SelectAndDisplayDownload(long id)
        {
            if (this.IsDisposed || _listView == null || _listView.IsDisposed) return;
            for (int i = 0; i < _listView.Items.Count; i++)
            {
                DownloadItemModel m = _listView.Items[i].Tag as DownloadItemModel;
                if (m != null && m.Id == id)
                {
                    _listView.Items[i].Selected = true;
                    _listView.Items[i].EnsureVisible();
                    _currentSelectedModel = m;
                    UpdateDetailsPanel();
                    return;
                }
            }

            DownloadItemModel direct = AppServices.Downloads.GetItem(id);
            if (direct != null)
            {
                _currentSelectedModel = direct;
                UpdateDetailsPanel();
            }
        }

        private void ListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count > 0)
            {
                DownloadItemModel m = _listView.SelectedItems[0].Tag as DownloadItemModel;
                _currentSelectedModel = (m != null) ? AppServices.Downloads.GetItem(m.Id) : null;
            }
            else
            {
                _currentSelectedModel = null;
            }
            UpdateDetailsPanel();
        }

        private void UpdateDetailsPanel()
        {
            int selCount = _listView.SelectedItems.Count;
            int totalCount = _listView.Items.Count;

            if (_countLabel != null)
            {
                _countLabel.Text = AppStrings.General.FormatItemizedCount(totalCount, selCount);
            }

            if (selCount == 0 || _currentSelectedModel == null)
            {
                _openFileBtn.Enabled = false;
                _pauseResumeBtn.Enabled = false;
                _cancelBtn.Enabled = false;
                _retryBtn.Enabled = false;
                _openFolderBtn.Enabled = false;
                _copyUrlBtn.Enabled = false;
                _deleteBtn.Enabled = false;
                return;
            }

            string status = _currentSelectedModel.GetStatusDisplay();
            if (string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase)) status = "Downloading";

            _copyUrlBtn.Enabled = !string.IsNullOrEmpty(_currentSelectedModel.Url);
            _deleteBtn.Enabled = true;

            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                _openFileBtn.Visible = true;
                _openFileBtn.Enabled = _currentSelectedModel.CheckFileExists();
                _pauseResumeBtn.Visible = false;
                _cancelBtn.Visible = false;
                _retryBtn.Visible = true;
                _retryBtn.Enabled = true;
                _openFolderBtn.Enabled = true;
            }
            else if (string.Equals(status, "Paused", StringComparison.OrdinalIgnoreCase))
            {
                _openFileBtn.Visible = false;
                _pauseResumeBtn.Visible = true;
                _pauseResumeBtn.Text = AppStrings.Downloads.BtnResume;
                _pauseResumeBtn.Enabled = true;
                _cancelBtn.Visible = true;
                _cancelBtn.Enabled = true;
                _retryBtn.Visible = false;
                _openFolderBtn.Enabled = true;
            }
            else if (string.Equals(status, "Downloading", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(_currentSelectedModel.State, "InProgress", StringComparison.OrdinalIgnoreCase))
            {
                _openFileBtn.Visible = false;
                _pauseResumeBtn.Visible = true;
                _pauseResumeBtn.Text = AppStrings.Downloads.BtnPause;
                _pauseResumeBtn.Enabled = true;
                _cancelBtn.Visible = true;
                _cancelBtn.Enabled = true;
                _retryBtn.Visible = false;
                _openFolderBtn.Enabled = true;
            }
            else
            {
                _openFileBtn.Visible = false;
                _pauseResumeBtn.Visible = false;
                _cancelBtn.Visible = false;
                _retryBtn.Visible = true;
                _retryBtn.Enabled = true;
                _openFolderBtn.Enabled = Directory.Exists(Path.GetDirectoryName(_currentSelectedModel.FilePath));
            }
        }

        private void TogglePauseResumeSelected()
        {
            if (_currentSelectedModel == null) return;
            string state = _currentSelectedModel.State;

            if (string.Equals(state, "Paused", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "Interrupted", StringComparison.OrdinalIgnoreCase) ||
                state.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
            {
                string blockReason;
                if (!AppServices.Downloads.CanResumeDownload(_currentSelectedModel, out blockReason))
                {
                    MessageBox.Show(this, blockReason, AppStrings.Downloads.TitleCannotResume, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                AppServices.Downloads.ResumeDownload(_currentSelectedModel.Id);
            }
            else if (string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase))
            {
                AppServices.Downloads.StartDownload(_currentSelectedModel.Id);
            }
            else
            {
                AppServices.Downloads.PauseDownload(_currentSelectedModel.Id);
            }

            RefreshList();
            UpdateDetailsPanel();
        }

        private void SortCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (_sortCombo.SelectedIndex)
            {
                case 0: _currentSort = SortMode.TitleAscending; break;
                case 1: _currentSort = SortMode.TitleDescending; break;
                case 2: _currentSort = SortMode.DateDescending; break;
                case 3: _currentSort = SortMode.DateAscending; break;
            }
            RefreshList();
        }

        private void ListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == 0) // File Name
            {
                _currentSort = (_currentSort == SortMode.TitleAscending) ? SortMode.TitleDescending : SortMode.TitleAscending;
                _sortCombo.SelectedIndex = (_currentSort == SortMode.TitleAscending) ? 0 : 1;
            }
            else if (e.Column == 5) // Date
            {
                _currentSort = (_currentSort == SortMode.DateDescending) ? SortMode.DateAscending : SortMode.DateDescending;
                _sortCombo.SelectedIndex = (_currentSort == SortMode.DateDescending) ? 2 : 3;
            }
            RefreshList();
        }

        public void RefreshList()
        {
            if (this.IsDisposed || _listView == null || _listView.IsDisposed) return;
            long selectedId = _currentSelectedModel != null ? _currentSelectedModel.Id : -1;

            _listView.BeginUpdate();
            try
            {
                _listView.Items.Clear();
                string query = _searchBox != null ? _searchBox.Text.Trim() : string.Empty;
                List<DownloadItemModel> items = AppServices.Downloads.GetSorted(query, _currentSort);

                ListViewItem selectedItem = null;
                List<ListViewItem> lvis = new List<ListViewItem>(items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    DownloadItemModel item = items[i];
                    ListViewItem lvi = new ListViewItem(item.FileName ?? AppStrings.General.Unknown);

                    string status = item.GetStatusDisplay();
                    if (string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase)) status = "Downloading";

                    lvi.SubItems.Add(status);
                    lvi.SubItems.Add(string.Format("{0}%", item.Percent));
                    lvi.SubItems.Add(item.GetFormattedSpeed());
                    lvi.SubItems.Add(item.GetFormattedSize());
                    lvi.SubItems.Add(item.LocalStartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    lvi.Tag = item;

                    lvis.Add(lvi);

                    if (item.Id == selectedId)
                    {
                        selectedItem = lvi;
                    }
                }
                if (lvis.Count > 0)
                {
                    _listView.Items.AddRange(lvis.ToArray());
                }
                if (selectedItem != null)
                {
                    selectedItem.Selected = true;
                }

                int inProgress = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (string.Equals(items[i].State, "InProgress", StringComparison.OrdinalIgnoreCase)) inProgress++;
                }

                int clearable = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    string st = items[i].State;
                    if (!string.Equals(st, "InProgress", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(st, "Queued", StringComparison.OrdinalIgnoreCase))
                    {
                        clearable++;
                    }
                }
                if (_clearBtn != null) _clearBtn.Enabled = (clearable > 0);
                if (_checkMissingBtn != null) _checkMissingBtn.Enabled = (items.Count > 0);

                UpdateDetailsPanel();
            }
            finally
            {
                _listView.EndUpdate();
            }
        }

        private void ListView_ItemDoubleClicked()
        {
            if (_currentSelectedModel == null) return;

            string status = _currentSelectedModel.GetStatusDisplay();
            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                OpenSelectedFile();
            }
            else if (string.Equals(status, "Missing (Deleted)", StringComparison.OrdinalIgnoreCase))
            {
                DialogResult res = MessageBox.Show(this,
                    AppStrings.Downloads.MissingPrompt(_currentSelectedModel.FileName),
                    AppStrings.Downloads.FileMissing,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (res == DialogResult.Yes)
                {
                    RetrySelectedDownload();
                }
            }
            else
            {
                TogglePauseResumeSelected();
            }
        }

        private void StartSelectedDownload()
        {
            if (_currentSelectedModel != null)
            {
                AppServices.Downloads.StartDownload(_currentSelectedModel.Id);
                RefreshList();
                UpdateDetailsPanel();
            }
        }

        private void PauseSelectedDownload()
        {
            if (_currentSelectedModel != null)
            {
                AppServices.Downloads.PauseDownload(_currentSelectedModel.Id);
                RefreshList();
                UpdateDetailsPanel();
            }
        }

        private void CancelSelectedDownload()
        {
            if (_currentSelectedModel == null) return;

            DialogResult confirm = MessageBox.Show(this,
                AppStrings.Downloads.ConfirmCancel(_currentSelectedModel.FileName),
                AppStrings.Downloads.CancelTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                AppServices.Downloads.CancelDownload(_currentSelectedModel.Id);
                RefreshList();
                UpdateDetailsPanel();
            }
        }

        private void RetrySelectedDownload()
        {
            if (_currentSelectedModel != null)
            {
                if (string.IsNullOrEmpty(_currentSelectedModel.Url))
                {
                    MessageBox.Show(this, AppStrings.Downloads.CannotRedownloadUrlUnknown, AppStrings.Downloads.RetryTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                AppServices.Downloads.RetryDownload(_currentSelectedModel.Id);
                RefreshList();
                UpdateDetailsPanel();
            }
        }

        private void CopySelectedUrl()
        {
            if (_currentSelectedModel != null && !string.IsNullOrEmpty(_currentSelectedModel.Url))
            {
                try
                {
                    Clipboard.SetText(_currentSelectedModel.Url);
                    MessageBox.Show(this, AppStrings.Downloads.LinkCopied, AppStrings.Downloads.CopyLinkTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch
                {
                }
            }
        }

        private void OpenSelectedFile()
        {
            if (_currentSelectedModel != null)
            {
                if (!string.IsNullOrEmpty(_currentSelectedModel.FilePath) && File.Exists(_currentSelectedModel.FilePath))
                {
                    try
                    {
                        Process.Start(_currentSelectedModel.FilePath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, string.Format(AppStrings.Downloads.CannotOpenFileFormat, ex.Message), AppStrings.Downloads.OpenFileTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show(this, AppStrings.Downloads.FileNotFoundOnDisk, AppStrings.Downloads.FileMissing, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    AppServices.Downloads.CheckMissingFiles();
                    RefreshList();
                    UpdateDetailsPanel();
                }
            }
        }

        private void OpenSelectedFolder()
        {
            if (_currentSelectedModel != null && !string.IsNullOrEmpty(_currentSelectedModel.FilePath))
            {
                try
                {
                    if (File.Exists(_currentSelectedModel.FilePath))
                    {
                        Process.Start("explorer.exe", "/select,\"" + _currentSelectedModel.FilePath + "\"");
                    }
                    else
                    {
                        string dir = Path.GetDirectoryName(_currentSelectedModel.FilePath);
                        if (Directory.Exists(dir))
                        {
                            Process.Start(dir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(AppStrings.Downloads.CannotOpenFolderFormat, ex.Message), AppStrings.Downloads.OpenFolderTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DeleteBtn_Click(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;

            string message;
            if (_listView.SelectedItems.Count == 1)
            {
                DownloadItemModel m = _listView.SelectedItems[0].Tag as DownloadItemModel;
                message = AppStrings.Downloads.ConfirmDeleteSingle(m != null ? m.FileName : AppStrings.Downloads.SelectedFileFallback);
            }
            else
            {
                message = AppStrings.Downloads.ConfirmDeleteMultiple(_listView.SelectedItems.Count);
            }

            DialogResult res = MessageBox.Show(this,
                message,
                _listView.SelectedItems.Count > 1 ? AppStrings.Downloads.DeleteMultiTitle : AppStrings.Downloads.DeleteSingleTitle,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (res == DialogResult.Yes)
            {
                DeleteSelected(true);
            }
            else if (res == DialogResult.No)
            {
                DeleteSelected(false);
            }
        }

        private void DeleteSelected(bool deleteFromDisk)
        {
            if (_listView.SelectedItems.Count > 0)
            {
                List<long> ids = new List<long>();
                for (int i = 0; i < _listView.SelectedItems.Count; i++)
                {
                    DownloadItemModel m = _listView.SelectedItems[i].Tag as DownloadItemModel;
                    if (m != null) ids.Add(m.Id);
                }

                _currentSelectedModel = null;
                _listView.SelectedItems.Clear();

                for (int i = 0; i < ids.Count; i++)
                {
                    AppServices.Downloads.DeleteDownload(ids[i], deleteFromDisk);
                }

                RefreshList();
                UpdateDetailsPanel();
            }
        }

        private void CheckMissingBtn_Click(object sender, EventArgs e)
        {
            int missing = AppServices.Downloads.CheckMissingFiles();
            RefreshList();
            UpdateDetailsPanel();

            if (missing > 0)
            {
                MessageBox.Show(this,
                    AppStrings.Downloads.MissingScanResult(missing),
                    AppStrings.Downloads.CheckMissingTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this,
                    AppStrings.Downloads.MissingScanNone,
                    AppStrings.Downloads.CheckMissingTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void AddDownloadBtn_Click(object sender, EventArgs e)
        {
            using (Form addForm = new Form())
            {
                UITheme.ApplyForm(addForm);
                addForm.Text = AppStrings.Downloads.AddTitle;
                addForm.ClientSize = new Size(540, 128);
                addForm.StartPosition = FormStartPosition.CenterParent;
                addForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                addForm.MaximizeBox = false;
                addForm.MinimizeBox = false;
                addForm.Font = UITheme.FontDefault;
                addForm.Padding = new Padding(12, 10, 12, 10);

                TableLayoutPanel formLayout = new TableLayoutPanel();
                formLayout.Dock = DockStyle.Fill;
                formLayout.BackColor = UITheme.FormBackground;
                formLayout.ColumnCount = 2;
                formLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
                formLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                formLayout.RowCount = 3;
                formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                formLayout.ForeColor = UITheme.TextPrimary;

                Label urlLbl = new Label { Text = AppStrings.Downloads.AddUrlLabel, Anchor = AnchorStyles.Left, AutoSize = true, Font = UITheme.FontDefault, ForeColor = UITheme.TextPrimary };
                TextBox urlBox = new TextBox { Dock = DockStyle.Fill };
                UITheme.ApplyTextBox(urlBox, AppStrings.Downloads.AddUrlPlaceholder);
                try
                {
                    string clip = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(clip) && (clip.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || clip.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                    {
                        urlBox.Text = clip;
                    }
                }
                catch { }

                Label saveLbl = new Label { Text = AppStrings.Downloads.AddSaveToLabel, Anchor = AnchorStyles.Left, AutoSize = true, Font = UITheme.FontDefault, ForeColor = UITheme.TextPrimary };

                TableLayoutPanel pathRow = new TableLayoutPanel();
                pathRow.Dock = DockStyle.Fill;
                pathRow.BackColor = UITheme.FormBackground;
                pathRow.ColumnCount = 2;
                pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85F));
                pathRow.RowCount = 1;
                pathRow.Margin = new Padding(0);
                pathRow.ForeColor = UITheme.TextPrimary;

                string defDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(defDir)) defDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                TextBox saveBox = new TextBox { Dock = DockStyle.Fill, Text = defDir };
                UITheme.ApplyTextBox(saveBox);
                Button browseBtn = new Button { Text = AppStrings.Downloads.AddBrowseBtn, Size = new Size(80, 26), Margin = new Padding(4, 0, 0, 0) };
                UITheme.ApplyButton(browseBtn);
                browseBtn.Click += (bs, be) =>
                {
                    using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                    {
                        fbd.SelectedPath = saveBox.Text;
                        if (fbd.ShowDialog(addForm) == DialogResult.OK)
                        {
                            saveBox.Text = fbd.SelectedPath;
                        }
                    }
                };

                pathRow.Controls.Add(saveBox, 0, 0);
                pathRow.Controls.Add(browseBtn, 1, 0);

                FlowLayoutPanel btnFlow = new FlowLayoutPanel();
                btnFlow.Dock = DockStyle.Fill;
                UITheme.ApplyFlowPanel(btnFlow);
                btnFlow.FlowDirection = FlowDirection.RightToLeft;
                btnFlow.Margin = new Padding(0);

                Button cancelBtn = new Button { Text = AppStrings.General.BtnCancel, Size = new Size(70, 28), DialogResult = DialogResult.Cancel, Margin = new Padding(3, 4, 0, 0) };
                UITheme.ApplyButton(cancelBtn);
                Button queueBtn = new Button { Text = AppStrings.Downloads.AddQueueBtn, Size = new Size(75, 28), DialogResult = DialogResult.Yes, Margin = new Padding(3, 4, 3, 0) };
                UITheme.ApplyButton(queueBtn);
                Button startBtn = new Button { Text = AppStrings.Downloads.AddDownloadNowBtn, Size = new Size(110, 28), DialogResult = DialogResult.OK, Margin = new Padding(3, 4, 3, 0) };
                UITheme.ApplyButton(startBtn, true);

                btnFlow.Controls.Add(cancelBtn);
                btnFlow.Controls.Add(queueBtn);
                btnFlow.Controls.Add(startBtn);

                formLayout.Controls.Add(urlLbl, 0, 0);
                formLayout.Controls.Add(urlBox, 1, 0);
                formLayout.Controls.Add(saveLbl, 0, 1);
                formLayout.Controls.Add(pathRow, 1, 1);
                formLayout.Controls.Add(btnFlow, 1, 2);

                addForm.Controls.Add(formLayout);
                addForm.AcceptButton = startBtn;
                addForm.CancelButton = cancelBtn;

                DialogResult result = addForm.ShowDialog(this);
                if (result == DialogResult.OK || result == DialogResult.Yes)
                {
                    string targetUrl = urlBox.Text.Trim();
                    if (string.IsNullOrEmpty(targetUrl)) return;

                    string fileName = AppStrings.Downloads.DefaultDownloadName;
                    try
                    {
                        Uri uri = new Uri(targetUrl);
                        fileName = Path.GetFileName(uri.LocalPath);
                        if (string.IsNullOrEmpty(fileName)) fileName = "download_" + DateTime.UtcNow.Ticks;
                    }
                    catch
                    {
                        fileName = "download_" + DateTime.UtcNow.Ticks;
                    }

                    string targetPath = Path.Combine(saveBox.Text.Trim(), fileName);
                    DownloadItemModel model = AppServices.Downloads.QueueDownload(targetUrl, targetPath);
                    RefreshList();

                    if (model != null)
                    {
                        SelectAndDisplayDownload(model.Id);
                    }
                }
            }
        }

        private void ClearBtn_Click(object sender, EventArgs e)
        {
            AppServices.Downloads.ClearHistory();
            _currentSelectedModel = null;
            _listView.SelectedItems.Clear();
            RefreshList();
            UpdateDetailsPanel();
        }

        private void ListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelected(false);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                _listView.BeginUpdate();
                for (int i = 0; i < _listView.Items.Count; i++)
                {
                    _listView.Items[i].Selected = true;
                }
                _listView.EndUpdate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                ListView_ItemDoubleClicked();
                e.Handled = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_refreshTimer != null)
                {
                    _refreshTimer.Stop();
                    _refreshTimer.Dispose();
                    _refreshTimer = null;
                }
                UnhookDownloadEvents();
            }
            base.Dispose(disposing);
        }
    }
}
