using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.DataPortability;
using wb.UI;

namespace wb.History
{
    public class HistoryView : UserControl
    {
        private Panel _centerContainer;
        private TextBox _searchBox;
        private ComboBox _sortCombo;
        private ListView _listView;
        private Button _openBtn;
        private Button _openNewTabBtn;
        private Button _deleteBtn;
        private Button _copyUrlBtn;
        private Button _clearBrowsingDataBtn;
        private Button _clearAllBtn;
        private Label _countLabel;

        private Action<string, bool> _openUrlCallback;
        private Action _closeTabCallback;
        private SortMode _currentSort = SortMode.DateDescending;

        public HistoryView(Action<string, bool> openUrlCallback, Action closeTabCallback = null)
        {
            _openUrlCallback = openUrlCallback;
            _closeTabCallback = closeTabCallback;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            this.DoubleBuffered = true;
            InitializeComponent();
            RefreshList();
            HookEvents();
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

            // 1. Top Bar Layout
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
                Text = AppStrings.History.PageTitle,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 10, 0)
            };

            topLeft.Controls.Add(titleLabel);

            // Right: Actions (Search, Sort, Clear All, Clear Browsing Data Primary)
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
                Width = 190,
                Margin = new Padding(0, 2, 8, 0)
            };
            UITheme.ApplyTextBox(_searchBox, AppStrings.History.SearchPlaceholder);
            _searchBox.TextChanged += (s, e) => RefreshList();

            _sortCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 160,
                Margin = new Padding(0, 2, 8, 0)
            };
            _sortCombo.Items.AddRange(new object[] {
                AppStrings.General.SortTitleAsc,
                AppStrings.General.SortTitleDesc,
                AppStrings.History.SortVisitDesc,
                AppStrings.History.SortVisitAsc
            });
            UITheme.ApplyComboBox(_sortCombo);
            _sortCombo.SelectedIndex = 2;
            _sortCombo.SelectedIndexChanged += SortCombo_SelectedIndexChanged;

            _clearAllBtn = new Button
            {
                Text = AppStrings.History.BtnClearAll,
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0)
            };
            UITheme.ApplyButton(_clearAllBtn);
            _clearAllBtn.Click += ClearAllBtn_Click;

            // Primary Button with strictly BLACK LABEL!
            _clearBrowsingDataBtn = new Button
            {
                Text = AppStrings.ClearBrowsingData.HeaderTitle + "...",
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 0, 0)
            };
            UITheme.ApplyButton(_clearBrowsingDataBtn, isPrimary: true);
            _clearBrowsingDataBtn.Click += ClearBrowsingDataBtn_Click;

            Button exportBtn = new Button
            {
                Text = "Export...",
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0)
            };
            UITheme.ApplyButton(exportBtn);
            exportBtn.Click += (s, e) => { ExportDataDialog.ShowExportDialog(this.FindForm()); };

            // Add in natural logical order: Search -> Sort -> Export -> Clear All -> Clear Browsing Data...
            topRight.Controls.Add(_searchBox);
            topRight.Controls.Add(_sortCombo);
            topRight.Controls.Add(exportBtn);
            topRight.Controls.Add(_clearAllBtn);
            topRight.Controls.Add(_clearBrowsingDataBtn);

            topBar.Controls.Add(topLeft);
            topBar.Controls.Add(topRight);

            topBar.Layout += (s, e) =>
            {
                topLeft.Location = new Point(0, Math.Max(0, (topBar.ClientSize.Height - topLeft.Height) / 2));
                int rightX = Math.Max(topLeft.Right + 16, topBar.ClientSize.Width - topRight.PreferredSize.Width);
                topRight.Location = new Point(rightX, Math.Max(0, (topBar.ClientSize.Height - topRight.Height) / 2));
            };

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
                HideSelection = false
            };

            _listView.Columns.Add(AppStrings.History.ColumnTitle, 340);
            _listView.Columns.Add(AppStrings.History.ColumnUrl, 460);
            _listView.Columns.Add(AppStrings.History.ColumnLastVisited, 200);

            _listView.ColumnClick += ListView_ColumnClick;
            _listView.DoubleClick += (s, e) => OpenSelected(true);
            _listView.KeyDown += ListView_KeyDown;
            _listView.SelectedIndexChanged += (s, e) => UpdateButtonStates();

            Action autoSizeColumns = () =>
            {
                if (_listView.Columns.Count >= 3 && _listView.ClientSize.Width > 0)
                {
                    int fixedWidth = _listView.Columns[0].Width + _listView.Columns[2].Width;
                    int available = _listView.ClientSize.Width - fixedWidth - 4;
                    if (available > 150)
                    {
                        _listView.Columns[1].Width = available;
                    }
                }
            };
            _listView.Resize += (s, e) => autoSizeColumns();

            // Context Menu
            ContextMenuStrip lviMenu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(lviMenu);
            lviMenu.Opening += (s, e) =>
            {
                lviMenu.Items.Clear();
                if (_listView.SelectedItems.Count > 0)
                {
                    lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.History.BtnOpenCurrent, null, (s2, e2) => OpenSelected(false)));
                    lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.History.BtnOpenNewTab, null, (s2, e2) => OpenSelected(true)));
                    lviMenu.Items.Add(new ToolStripSeparator());
                    lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnCopyUrl, null, (s2, e2) => CopySelectedUrl()));
                    lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnDelete, null, (s2, e2) => DeleteSelected()));
                }
                else
                {
                    e.Cancel = true;
                }
            };
            _listView.ContextMenuStrip = lviMenu;

            // Apply Modern ListView styling (38px row height, hover state, accent indicator)
            UITheme.ApplyModernListView(_listView, 38, true);

            listBorderPanel.Controls.Add(_listView);

            // 3. Bottom Bar Layout Panel
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

            _deleteBtn = new Button { Text = AppStrings.General.BtnDelete, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_deleteBtn);
            _deleteBtn.Click += (s, e) => DeleteSelected();

            _copyUrlBtn = new Button { Text = AppStrings.General.BtnCopyUrl, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_copyUrlBtn);
            _copyUrlBtn.Click += (s, e) => CopySelectedUrl();

            _openNewTabBtn = new Button { Text = AppStrings.History.BtnOpenNewTab, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_openNewTabBtn);
            _openNewTabBtn.Click += (s, e) => OpenSelected(true);

            _openBtn = new Button { Text = AppStrings.History.BtnOpenCurrent, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 0, 0) };
            UITheme.ApplyButton(_openBtn, isPrimary: true);
            _openBtn.Click += (s, e) => OpenSelected(false);

            bottomRight.Controls.Add(_deleteBtn);
            bottomRight.Controls.Add(_copyUrlBtn);
            bottomRight.Controls.Add(_openNewTabBtn);
            bottomRight.Controls.Add(_openBtn);

            if (_closeTabCallback != null)
            {
                Button closeBtn = new Button { Text = AppStrings.General.BtnClose, AutoSize = true, Height = 30, Margin = new Padding(6, 0, 0, 0) };
                UITheme.ApplyButton(closeBtn);
                closeBtn.Click += (s, e) => _closeTabCallback();
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

            UpdateButtonStates();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_centerContainer != null)
            {
                int targetWidth = Math.Min(1020, Math.Max(400, this.ClientSize.Width - 32));
                int left = Math.Max(16, (this.ClientSize.Width - targetWidth) / 2);
                int top = 16;
                int height = Math.Max(100, this.ClientSize.Height - 32);
                _centerContainer.SetBounds(left, top, targetWidth, height);
            }
        }

        private void HookEvents()
        {
            AppServices.History.HistoryChanged += History_HistoryChanged;
        }

        private void History_HistoryChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(RefreshList));
                }
                else
                {
                    RefreshList();
                }
            }
            catch { }
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
            if (e.Column == 0) // Title
            {
                _currentSort = (_currentSort == SortMode.TitleAscending) ? SortMode.TitleDescending : SortMode.TitleAscending;
                _sortCombo.SelectedIndex = (_currentSort == SortMode.TitleAscending) ? 0 : 1;
            }
            else if (e.Column == 2) // Date
            {
                _currentSort = (_currentSort == SortMode.DateDescending) ? SortMode.DateAscending : SortMode.DateDescending;
                _sortCombo.SelectedIndex = (_currentSort == SortMode.DateDescending) ? 2 : 3;
            }
            RefreshList();
        }

        public void RefreshList()
        {
            if (this.IsDisposed || _listView == null || _listView.IsDisposed) return;
            _listView.BeginUpdate();
            try
            {
                _listView.Items.Clear();
                string query = _searchBox != null ? _searchBox.Text.Trim() : string.Empty;
                List<HistoryItem> items = AppServices.History.GetSorted(query, _currentSort);

                List<ListViewItem> lvis = new List<ListViewItem>(items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    HistoryItem item = items[i];
                    ListViewItem lvi = new ListViewItem(item.Title ?? string.Empty);
                    lvi.SubItems.Add(item.Url ?? string.Empty);
                    lvi.SubItems.Add(item.LocalVisitedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    lvi.Tag = item;
                    lvis.Add(lvi);
                }
                if (lvis.Count > 0)
                {
                    _listView.Items.AddRange(lvis.ToArray());
                }

                UpdateButtonStates();
            }
            finally
            {
                _listView.EndUpdate();
            }
        }

        private void OpenSelected(bool newTab)
        {
            if (_listView.SelectedItems.Count > 0)
            {
                for (int i = 0; i < _listView.SelectedItems.Count; i++)
                {
                    HistoryItem item = _listView.SelectedItems[i].Tag as HistoryItem;
                    if (item != null && !string.IsNullOrEmpty(item.Url))
                    {
                        if (_openUrlCallback != null)
                        {
                            _openUrlCallback(item.Url, newTab || i > 0);
                        }
                    }
                }
            }
        }

        private void DeleteSelected()
        {
            if (_listView.SelectedItems.Count > 0)
            {
                for (int i = _listView.SelectedItems.Count - 1; i >= 0; i--)
                {
                    HistoryItem item = _listView.SelectedItems[i].Tag as HistoryItem;
                    if (item != null)
                    {
                        AppServices.History.RemoveEntry(item.Id);
                    }
                }
                RefreshList();
            }
        }

        private void ClearAllBtn_Click(object sender, EventArgs e)
        {
            DialogResult res = MessageBox.Show(this,
                AppStrings.History.ConfirmClearAll,
                AppStrings.History.ClearHistoryTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res == DialogResult.Yes)
            {
                AppServices.History.Clear();
                RefreshList();
            }
        }

        private void ClearBrowsingDataBtn_Click(object sender, EventArgs e)
        {
            wb.Browser.ClearBrowsingData.ClearBrowsingDataDialog.ShowClearBrowsingData((IWin32Window)this.FindForm() ?? this);
            RefreshList();
        }

        private void CopySelectedUrl()
        {
            if (_listView.SelectedItems.Count == 0) return;
            List<string> urls = new List<string>();
            for (int i = 0; i < _listView.SelectedItems.Count; i++)
            {
                HistoryItem item = _listView.SelectedItems[i].Tag as HistoryItem;
                if (item != null && !string.IsNullOrEmpty(item.Url))
                {
                    urls.Add(item.Url);
                }
            }
            if (urls.Count > 0)
            {
                ClipboardHelper.SetText(string.Join(Environment.NewLine, urls.ToArray()));
            }
        }

        private void UpdateButtonStates()
        {
            bool hasSelection = _listView.SelectedItems.Count > 0;
            _deleteBtn.Enabled = hasSelection;
            if (_copyUrlBtn != null) _copyUrlBtn.Enabled = hasSelection;
            _openBtn.Enabled = hasSelection;
            _openNewTabBtn.Enabled = hasSelection;
            _clearAllBtn.Enabled = _listView.Items.Count > 0;

            if (_countLabel != null)
            {
                int total = _listView.Items.Count;
                int sel = _listView.SelectedItems.Count;
                _countLabel.Text = AppStrings.General.FormatItemizedCount(total, sel);
            }
        }

        private void ListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                OpenSelected(true);
            }
            else if (e.KeyCode == Keys.Delete)
            {
                e.SuppressKeyPress = true;
                DeleteSelected();
            }
            else if (e.KeyCode == Keys.C && e.Control)
            {
                e.SuppressKeyPress = true;
                CopySelectedUrl();
            }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                e.SuppressKeyPress = true;
                _listView.BeginUpdate();
                for (int i = 0; i < _listView.Items.Count; i++)
                {
                    _listView.Items[i].Selected = true;
                }
                _listView.EndUpdate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AppServices.History.HistoryChanged -= History_HistoryChanged;
            }
            base.Dispose(disposing);
        }
    }
}
