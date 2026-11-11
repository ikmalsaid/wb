using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using wb.Core;
using wb.DataPortability;
using wb.UI;

namespace wb.Bookmarks
{
    public class BookmarksView : UserControl
    {
        private Panel _centerContainer;
        private SplitContainer _splitContainer;
        private TreeView _folderTree;
        private string _selectedFolderId = "bar";
        private bool _isPopulatingTree = false;

        private TextBox _searchBox;
        private ComboBox _sortCombo;
        private ListView _listView;
        private Button _openBtn;
        private Button _openNewTabBtn;
        private Button _deleteBtn;
        private Button _copyUrlBtn;
        private Button _editBtn;
        private Button _addBtn;
        private Button _addFolderBtn;
        private Label _countLabel;

        private Action<string, bool> _openUrlCallback;
        private Action _closeTabCallback;
        private SortMode _currentSort = SortMode.TypeAscending;

        public Func<string, string, bool> ConfirmDeleteHandler { get; set; }

        private bool PromptDeleteConfirmation(string message, string title)
        {
            if (ConfirmDeleteHandler != null)
            {
                return ConfirmDeleteHandler(message, title);
            }
            return MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        public BookmarksView(Action<string, bool> openUrlCallback, Action closeTabCallback = null)
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
            this.Size = new Size(1000, 600);
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
                Text = AppStrings.Bookmarks.PageTitle,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = UITheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 10, 0)
            };

            topLeft.Controls.Add(titleLabel);

            // Right: Actions (Search, Sort, Add Bookmark Primary)
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
                Width = 200,
                Margin = new Padding(0, 2, 8, 0)
            };
            UITheme.ApplyTextBox(_searchBox, AppStrings.Bookmarks.SearchPlaceholder);
            _searchBox.TextChanged += (s, e) => RefreshList();

            _sortCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 160,
                Margin = new Padding(0, 2, 8, 0)
            };
            _sortCombo.Items.AddRange(new object[] {
                AppStrings.General.SortTypeAsc,
                AppStrings.General.SortTypeDesc,
                AppStrings.General.SortTitleAsc,
                AppStrings.General.SortTitleDesc,
                AppStrings.General.SortFolderAsc,
                AppStrings.General.SortFolderDesc,
                AppStrings.General.SortDateDesc,
                AppStrings.General.SortDateAsc
            });
            UITheme.ApplyComboBox(_sortCombo);
            _sortCombo.SelectedIndex = 0;
            _sortCombo.SelectedIndexChanged += SortCombo_SelectedIndexChanged;

            _addFolderBtn = new Button
            {
                Text = AppStrings.Bookmarks.BtnAddFolder,
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 6, 0)
            };
            UITheme.ApplyButton(_addFolderBtn);
            _addFolderBtn.Click += AddFolderBtn_Click;

            // Primary Button (+ Add Bookmark) with strictly BLACK LABEL!
            _addBtn = new Button
            {
                Text = AppStrings.Bookmarks.BtnAdd,
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 0, 0)
            };
            UITheme.ApplyButton(_addBtn, isPrimary: true);
            _addBtn.Click += AddBtn_Click;

            Button importBtn = new Button
            {
                Text = "Import...",
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 6, 0)
            };
            UITheme.ApplyButton(importBtn);
            importBtn.Click += (s, e) =>
            {
                ImportDataDialog.ShowImportDialog(this.FindForm());
                PopulateFolderTree();
                RefreshList();
            };

            Button exportBtn = new Button
            {
                Text = "Export...",
                AutoSize = true,
                Height = 32,
                Margin = new Padding(0, 0, 6, 0)
            };
            UITheme.ApplyButton(exportBtn);
            exportBtn.Click += (s, e) =>
            {
                ExportDataDialog.ShowExportDialog(this.FindForm());
            };

            // Add in natural logical order: Search -> Sort -> Import -> Export -> + Add Folder -> + Add Bookmark
            topRight.Controls.Add(_searchBox);
            topRight.Controls.Add(_sortCombo);
            topRight.Controls.Add(importBtn);
            topRight.Controls.Add(exportBtn);
            topRight.Controls.Add(_addFolderBtn);
            topRight.Controls.Add(_addBtn);

            topBar.Controls.Add(topLeft);
            topBar.Controls.Add(topRight);

            topBar.Layout += (s, e) =>
            {
                topLeft.Location = new Point(0, Math.Max(0, (topBar.ClientSize.Height - topLeft.Height) / 2));
                int rightX = Math.Max(topLeft.Right + 16, topBar.ClientSize.Width - topRight.PreferredSize.Width);
                topRight.Location = new Point(rightX, Math.Max(0, (topBar.ClientSize.Height - topRight.Height) / 2));
            };

            // 2. Modern Dual-Pane Split Layout (Left: Folder Tree, Right: Details Table)
            _splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel1,
                BackColor = UITheme.FormBackground,
                SplitterWidth = 6,
                Panel1MinSize = 0,
                Panel2MinSize = 0
            };
            UITheme.EnableDoubleBuffering(_splitContainer);
            UITheme.EnableDoubleBuffering(_splitContainer.Panel1);
            UITheme.EnableDoubleBuffering(_splitContainer.Panel2);

            Action updateSplitter = () =>
            {
                try
                {
                    if (_splitContainer.Width > 360)
                    {
                        _splitContainer.Panel1MinSize = 120;
                        _splitContainer.Panel2MinSize = 200;
                        int desired = 220;
                        int maxDist = _splitContainer.Width - _splitContainer.Panel2MinSize;
                        if (desired > maxDist) desired = maxDist;
                        if (desired < _splitContainer.Panel1MinSize) desired = _splitContainer.Panel1MinSize;
                        _splitContainer.SplitterDistance = desired;
                    }
                    else
                    {
                        _splitContainer.Panel1MinSize = 0;
                        _splitContainer.Panel2MinSize = 0;
                    }
                }
                catch { }
            };

            _splitContainer.HandleCreated += (s, e) => updateSplitter();
            _splitContainer.SizeChanged += (s, e) => updateSplitter();

            // Left Pane: Folder Tree View inside modern border
            Panel treeBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UITheme.FormBackground,
                Padding = new Padding(1)
            };
            treeBorderPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(MaterialColors.Separator, 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, treeBorderPanel.Width - 1, treeBorderPanel.Height - 1);
                }
            };

            _folderTree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                HideSelection = false
            };
            UITheme.ApplyModernTreeView(_folderTree, 28);
            UpdateTreeIcons();
            _folderTree.AfterSelect += FolderTree_AfterSelect;
            _folderTree.NodeMouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    _folderTree.SelectedNode = e.Node;
                }
            };

            // Folder Tree Context Menu
            ContextMenuStrip treeMenu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(treeMenu);
            treeMenu.Opening += (s, e) =>
            {
                treeMenu.Items.Clear();
                TreeNode selNode = _folderTree.SelectedNode;
                if (selNode == null)
                {
                    e.Cancel = true;
                    return;
                }

                string folderId = selNode.Tag as string;
                if (!string.IsNullOrEmpty(folderId))
                {
                    treeMenu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnAdd, null, (s2, e2) => AddBookmarkToFolder(folderId)));
                    treeMenu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnAddFolder, null, (s2, e2) => AddFolderToFolder(folderId)));

                    if (!string.Equals(folderId, "bar", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(folderId, "other", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(folderId, "mobile", StringComparison.OrdinalIgnoreCase))
                    {
                        treeMenu.Items.Add(new ToolStripSeparator());
                        treeMenu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnEdit, null, (s2, e2) => EditFolderById(folderId)));
                        treeMenu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnDelete, null, (s2, e2) => DeleteFolderById(folderId)));
                    }
                }
                else
                {
                    // "All bookmarks" virtual root
                    treeMenu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnAdd, null, (s2, e2) => AddBookmarkToFolder("bar")));
                    treeMenu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnAddFolder, null, (s2, e2) => AddFolderToFolder("bar")));
                }
            };
            _folderTree.ContextMenuStrip = treeMenu;

            treeBorderPanel.Controls.Add(_folderTree);
            _splitContainer.Panel1.Controls.Add(treeBorderPanel);

            // Right Pane: Modern ListView
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

            _listView.Columns.Add(AppStrings.Bookmarks.ColumnTitle, 240);
            _listView.Columns.Add(AppStrings.Bookmarks.ColumnType, 80);
            _listView.Columns.Add(AppStrings.Bookmarks.ColumnFolder, 130);
            _listView.Columns.Add(AppStrings.Bookmarks.ColumnUrl, 300);
            _listView.Columns.Add(AppStrings.Bookmarks.ColumnDateAdded, 140);

            _listView.ColumnClick += ListView_ColumnClick;
            _listView.DoubleClick += (s, e) =>
            {
                if (_listView.SelectedItems.Count == 1)
                {
                    BookmarkItem item = _listView.SelectedItems[0].Tag as BookmarkItem;
                    if (item != null && item.IsFolder)
                    {
                        SelectFolderNode(item.Id);
                        return;
                    }
                }
                OpenSelected(true);
            };
            _listView.KeyDown += ListView_KeyDown;
            _listView.SelectedIndexChanged += (s, e) => UpdateButtonStates();
            _listView.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    ListViewHitTestInfo hit = _listView.HitTest(e.Location);
                    if (hit.Item == null)
                    {
                        _listView.SelectedItems.Clear();
                    }
                    else if (!hit.Item.Selected)
                    {
                        _listView.SelectedItems.Clear();
                        hit.Item.Selected = true;
                    }
                }
            };

            Action autoSizeColumns = () =>
            {
                if (_listView.Columns.Count >= 5 && _listView.ClientSize.Width > 0)
                {
                    int fixedWidth = _listView.Columns[0].Width + _listView.Columns[1].Width + _listView.Columns[2].Width + _listView.Columns[4].Width;
                    int available = _listView.ClientSize.Width - fixedWidth - 4;
                    if (available > 120)
                    {
                        _listView.Columns[3].Width = available;
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
                if (_listView.SelectedItems.Count == 1)
                {
                    BookmarkItem item = _listView.SelectedItems[0].Tag as BookmarkItem;
                    if (item != null)
                    {
                        if (item.IsFolder)
                        {
                            int count = AppServices.Bookmarks.GetAllChildren(item.Id).Count;
                            ToolStripMenuItem openAll = new ToolStripMenuItem(string.Format(AppStrings.Bookmarks.OpenAllFormat, count), null, (s2, e2) => OpenSelected(true));
                            openAll.Enabled = (count > 0);
                            lviMenu.Items.Add(openAll);
                            lviMenu.Items.Add(new ToolStripSeparator());
                            lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnEdit, null, (s2, e2) => EditSelected()));
                            lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnDelete, null, (s2, e2) => DeleteSelected()));
                        }
                        else
                        {
                            lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnOpenCurrent, null, (s2, e2) => OpenSelected(false)));
                            lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnOpenNewTab, null, (s2, e2) => OpenSelected(true)));
                            lviMenu.Items.Add(new ToolStripSeparator());
                            lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnCopyUrl, null, (s2, e2) => CopySelectedUrl()));
                            lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnEdit, null, (s2, e2) => EditSelected()));
                            lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnDelete, null, (s2, e2) => DeleteSelected()));
                        }

                        List<BookmarkItem> siblings = AppServices.Bookmarks.GetBarItems(item.ParentId);
                        int siblingIdx = siblings.FindIndex(b => b.Id == item.Id);
                        if (siblingIdx >= 0 && siblings.Count > 1)
                        {
                            lviMenu.Items.Add(new ToolStripSeparator());
                            ToolStripMenuItem moveUpItem = new ToolStripMenuItem(AppStrings.Bookmarks.MoveUp, null, (s2, e2) =>
                            {
                                AppServices.Bookmarks.MoveItemInFolder(item.Id, -1);
                                RefreshList();
                            });
                            moveUpItem.Enabled = (siblingIdx > 0);
                            lviMenu.Items.Add(moveUpItem);

                            ToolStripMenuItem moveDownItem = new ToolStripMenuItem(AppStrings.Bookmarks.MoveDown, null, (s2, e2) =>
                            {
                                AppServices.Bookmarks.MoveItemInFolder(item.Id, 1);
                                RefreshList();
                            });
                            moveDownItem.Enabled = (siblingIdx < siblings.Count - 1);
                            lviMenu.Items.Add(moveDownItem);
                        }
                    }
                }
                else if (_listView.SelectedItems.Count > 1)
                {
                    lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnOpenNewTab, null, (s2, e2) => OpenSelected(true)));
                    lviMenu.Items.Add(new ToolStripSeparator());
                    lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnCopyUrl, null, (s2, e2) => CopySelectedUrl()));
                    lviMenu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnDelete, null, (s2, e2) => DeleteSelected()));
                }
                else
                {
                    // Empty space right-click in details table
                    string targetFolder = !string.IsNullOrEmpty(_selectedFolderId) ? _selectedFolderId : "bar";

                    ToolStripMenuItem addBm = new ToolStripMenuItem(AppStrings.Bookmarks.BtnAdd, null, (s2, e2) => AddBookmarkToFolder(targetFolder));
                    addBm.Image = MaterialIcons.GetIcon("bookmark-simple", 16, ThemeManager.AccentColor);
                    lviMenu.Items.Add(addBm);

                    ToolStripMenuItem addFld = new ToolStripMenuItem(AppStrings.Bookmarks.BtnAddFolder, null, (s2, e2) => AddFolderToFolder(targetFolder));
                    addFld.Image = MaterialIcons.GetIcon("folder", 16, ThemeManager.AccentColor);
                    lviMenu.Items.Add(addFld);
                }
            };
            _listView.ContextMenuStrip = lviMenu;

            // Apply Modern ListView styling with custom Phosphor icon & title painter
            UITheme.ApplyModernListView(_listView, 38, true, DrawBookmarkSubItem);

            ContextMenuStrip emptySpaceMenu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(emptySpaceMenu);
            emptySpaceMenu.Opening += (s, e) =>
            {
                emptySpaceMenu.Items.Clear();
                string targetFolder = !string.IsNullOrEmpty(_selectedFolderId) ? _selectedFolderId : "bar";

                ToolStripMenuItem addBm = new ToolStripMenuItem(AppStrings.Bookmarks.BtnAdd, null, (s2, e2) => AddBookmarkToFolder(targetFolder));
                addBm.Image = MaterialIcons.GetIcon("bookmark-simple", 16, ThemeManager.AccentColor);
                emptySpaceMenu.Items.Add(addBm);

                ToolStripMenuItem addFld = new ToolStripMenuItem(AppStrings.Bookmarks.BtnAddFolder, null, (s2, e2) => AddFolderToFolder(targetFolder));
                addFld.Image = MaterialIcons.GetIcon("folder", 16, ThemeManager.AccentColor);
                emptySpaceMenu.Items.Add(addFld);
            };

            listBorderPanel.ContextMenuStrip = emptySpaceMenu;
            treeBorderPanel.ContextMenuStrip = emptySpaceMenu;
            _centerContainer.ContextMenuStrip = emptySpaceMenu;

            listBorderPanel.Controls.Add(_listView);
            _splitContainer.Panel2.Controls.Add(listBorderPanel);

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

            _editBtn = new Button { Text = AppStrings.Bookmarks.BtnEdit, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_editBtn);
            _editBtn.Click += (s, e) => EditSelected();

            _openNewTabBtn = new Button { Text = AppStrings.Bookmarks.BtnOpenNewTab, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            UITheme.ApplyButton(_openNewTabBtn);
            _openNewTabBtn.Click += (s, e) => OpenSelected(true);

            _openBtn = new Button { Text = AppStrings.Bookmarks.BtnOpenCurrent, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 0, 0) };
            UITheme.ApplyButton(_openBtn, isPrimary: true);
            _openBtn.Click += (s, e) => OpenSelected(false);

            bottomRight.Controls.Add(_deleteBtn);
            bottomRight.Controls.Add(_copyUrlBtn);
            bottomRight.Controls.Add(_editBtn);
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
            _centerContainer.Controls.Add(_splitContainer);
            _centerContainer.Controls.Add(bottomBar);
            _centerContainer.Controls.Add(topBar);

            this.Controls.Add(_centerContainer);

            PopulateFolderTree();
            UpdateButtonStates();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_centerContainer != null)
            {
                int targetWidth = Math.Min(1240, Math.Max(480, this.ClientSize.Width - 32));
                int left = Math.Max(16, (this.ClientSize.Width - targetWidth) / 2);
                int top = 16;
                int height = Math.Max(100, this.ClientSize.Height - 32);
                _centerContainer.SetBounds(left, top, targetWidth, height);
            }
        }

        private void HookEvents()
        {
            AppServices.Bookmarks.BookmarksChanged += Bookmarks_BookmarksChanged;
            ThemeManager.AccentColorChanged += ThemeManager_AccentColorChanged;
        }

        private void Bookmarks_BookmarksChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        PopulateFolderTree();
                        RefreshList();
                    }));
                }
                else
                {
                    PopulateFolderTree();
                    RefreshList();
                }
            }
            catch { }
        }

        private void ThemeManager_AccentColorChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(UpdateTreeIcons));
                }
                else
                {
                    UpdateTreeIcons();
                }
            }
            catch { }
        }

        private void FolderTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (_isPopulatingTree) return;
            _selectedFolderId = e.Node != null && e.Node.Tag != null ? e.Node.Tag as string : "bar";
            RefreshList();
        }

        public void PopulateFolderTree()
        {
            if (_folderTree == null || _folderTree.IsDisposed) return;
            _isPopulatingTree = true;
            _folderTree.BeginUpdate();
            try
            {
                string prevSelection = !string.IsNullOrEmpty(_selectedFolderId) ? _selectedFolderId : "bar";
                _folderTree.Nodes.Clear();

                TreeNode barNode = new TreeNode(AppStrings.Bookmarks.FolderBookmarksBar);
                barNode.Tag = "bar";
                barNode.ImageKey = "folder";
                barNode.SelectedImageKey = "folder-open";
                _folderTree.Nodes.Add(barNode);

                TreeNode otherNode = new TreeNode(AppStrings.Bookmarks.FolderOther);
                otherNode.Tag = "other";
                otherNode.ImageKey = "folder";
                otherNode.SelectedImageKey = "folder-open";
                _folderTree.Nodes.Add(otherNode);

                TreeNode mobileNode = new TreeNode(AppStrings.Bookmarks.FolderMobile);
                mobileNode.Tag = "mobile";
                mobileNode.ImageKey = "folder";
                mobileNode.SelectedImageKey = "folder-open";
                _folderTree.Nodes.Add(mobileNode);

                List<BookmarkItem> allFolders = AppServices.Bookmarks.GetFolders();
                HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                AddChildFolders(barNode, "bar", allFolders, visited);
                AddChildFolders(otherNode, "other", allFolders, visited);
                AddChildFolders(mobileNode, "mobile", allFolders, visited);

                for (int i = 0; i < allFolders.Count; i++)
                {
                    BookmarkItem folder = allFolders[i];
                    if (folder != null && folder.IsFolder && !visited.Contains(folder.Id))
                    {
                        visited.Add(folder.Id);
                        TreeNode childNode = new TreeNode(folder.Title ?? string.Empty);
                        childNode.Tag = folder.Id;
                        childNode.ImageKey = "folder";
                        childNode.SelectedImageKey = "folder-open";
                        barNode.Nodes.Add(childNode);
                        AddChildFolders(childNode, folder.Id, allFolders, visited);
                    }
                }

                _folderTree.ExpandAll();

                TreeNode targetNode = FindNodeByTag(_folderTree.Nodes, prevSelection);
                if (targetNode != null)
                {
                    _folderTree.SelectedNode = targetNode;
                }
                else
                {
                    _folderTree.SelectedNode = barNode;
                    _selectedFolderId = "bar";
                }
            }
            finally
            {
                _folderTree.EndUpdate();
                _isPopulatingTree = false;
            }
        }

        private void AddChildFolders(TreeNode parentNode, string parentFolderId, List<BookmarkItem> allFolders, HashSet<string> visited)
        {
            if (parentNode == null || allFolders == null) return;
            for (int i = 0; i < allFolders.Count; i++)
            {
                BookmarkItem folder = allFolders[i];
                if (folder == null || !folder.IsFolder) continue;
                if (visited.Contains(folder.Id)) continue;

                bool isDirectChild = false;
                if (string.Equals(parentFolderId, "bar", StringComparison.OrdinalIgnoreCase))
                {
                    isDirectChild = string.IsNullOrEmpty(folder.ParentId) || string.Equals(folder.ParentId, "bar", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    isDirectChild = string.Equals(folder.ParentId, parentFolderId, StringComparison.OrdinalIgnoreCase);
                }

                if (isDirectChild)
                {
                    visited.Add(folder.Id);
                    TreeNode childNode = new TreeNode(folder.Title ?? string.Empty);
                    childNode.Tag = folder.Id;
                    childNode.ImageKey = "folder";
                    childNode.SelectedImageKey = "folder-open";
                    parentNode.Nodes.Add(childNode);
                    AddChildFolders(childNode, folder.Id, allFolders, visited);
                }
            }
        }

        private TreeNode FindNodeByTag(TreeNodeCollection nodes, string tag)
        {
            if (nodes == null) return null;
            for (int i = 0; i < nodes.Count; i++)
            {
                TreeNode node = nodes[i];
                if (tag == null && node.Tag == null) return node;
                if (tag != null && string.Equals(node.Tag as string, tag, StringComparison.OrdinalIgnoreCase)) return node;

                TreeNode child = FindNodeByTag(node.Nodes, tag);
                if (child != null) return child;
            }
            return null;
        }

        private void SelectFolderNode(string folderId)
        {
            if (_folderTree == null || _folderTree.IsDisposed) return;
            TreeNode target = FindNodeByTag(_folderTree.Nodes, folderId);
            if (target != null)
            {
                _folderTree.SelectedNode = target;
            }
        }

        private void UpdateTreeIcons()
        {
            if (_folderTree == null || _folderTree.IsDisposed) return;
            try
            {
                ImageList list = new ImageList();
                list.ImageSize = new Size(16, 16);
                list.ColorDepth = ColorDepth.Depth32Bit;

                Bitmap bmAll = MaterialIcons.GetIcon("bookmarks", 16, ThemeManager.AccentColor);
                if (bmAll == null) bmAll = MaterialIcons.GetIcon("globe", 16, ThemeManager.AccentColor);
                Bitmap bmFolder = MaterialIcons.GetIcon("folder", 16, ThemeManager.AccentColor);
                Bitmap bmFolderOpen = MaterialIcons.GetIcon("folder-open", 16, ThemeManager.AccentColor);

                if (bmAll != null) list.Images.Add("all", bmAll);
                if (bmFolder != null) list.Images.Add("folder", bmFolder);
                if (bmFolderOpen != null) list.Images.Add("folder-open", bmFolderOpen);

                _folderTree.ImageList = list;
            }
            catch { }
        }

        private void SortCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (_sortCombo.SelectedIndex)
            {
                case 0: _currentSort = SortMode.TypeAscending; break;
                case 1: _currentSort = SortMode.TypeDescending; break;
                case 2: _currentSort = SortMode.TitleAscending; break;
                case 3: _currentSort = SortMode.TitleDescending; break;
                case 4: _currentSort = SortMode.FolderAscending; break;
                case 5: _currentSort = SortMode.FolderDescending; break;
                case 6: _currentSort = SortMode.DateDescending; break;
                case 7: _currentSort = SortMode.DateAscending; break;
            }
            RefreshList();
        }

        private void ListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == 0) // Title
            {
                _currentSort = (_currentSort == SortMode.TitleAscending) ? SortMode.TitleDescending : SortMode.TitleAscending;
                _sortCombo.SelectedIndex = (_currentSort == SortMode.TitleAscending) ? 2 : 3;
            }
            else if (e.Column == 1) // Type
            {
                _currentSort = (_currentSort == SortMode.TypeAscending) ? SortMode.TypeDescending : SortMode.TypeAscending;
                _sortCombo.SelectedIndex = (_currentSort == SortMode.TypeAscending) ? 0 : 1;
            }
            else if (e.Column == 2) // Folder
            {
                _currentSort = (_currentSort == SortMode.FolderAscending) ? SortMode.FolderDescending : SortMode.FolderAscending;
                _sortCombo.SelectedIndex = (_currentSort == SortMode.FolderAscending) ? 4 : 5;
            }
            else if (e.Column == 4) // Date Added
            {
                _currentSort = (_currentSort == SortMode.DateDescending) ? SortMode.DateAscending : SortMode.DateDescending;
                _sortCombo.SelectedIndex = (_currentSort == SortMode.DateDescending) ? 6 : 7;
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
                List<BookmarkItem> items = AppServices.Bookmarks.GetSorted(query, _currentSort);

                if (!string.IsNullOrEmpty(_selectedFolderId))
                {
                    List<BookmarkItem> filtered = new List<BookmarkItem>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        BookmarkItem item = items[i];
                        if (item == null) continue;
                        if (string.Equals(_selectedFolderId, "bar", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrEmpty(item.ParentId) || string.Equals(item.ParentId, "bar", StringComparison.OrdinalIgnoreCase))
                            {
                                filtered.Add(item);
                            }
                        }
                        else
                        {
                            if (string.Equals(item.ParentId, _selectedFolderId, StringComparison.OrdinalIgnoreCase))
                            {
                                filtered.Add(item);
                            }
                        }
                    }
                    items = filtered;
                }

                List<ListViewItem> lvis = new List<ListViewItem>(items.Count);
                for (int i = 0; i < items.Count; i++)
                {
                    BookmarkItem item = items[i];
                    string title = item.Title ?? string.Empty;
                    ListViewItem lvi = new ListViewItem(title);
                    lvi.SubItems.Add(item.IsFolder ? AppStrings.Bookmarks.TypeFolder : AppStrings.Bookmarks.TypeBookmark);
                    lvi.SubItems.Add(AppServices.Bookmarks.GetFolderName(item.ParentId));
                    if (item.IsFolder)
                    {
                        int count = AppServices.Bookmarks.GetChildCount(item.Id);
                        lvi.SubItems.Add(string.Format(AppStrings.Bookmarks.FolderItemsFormat, count, count == 1 ? "" : "s"));
                    }
                    else
                    {
                        lvi.SubItems.Add(item.Url ?? string.Empty);
                    }
                    lvi.SubItems.Add(item.LocalCreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
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

        private void DrawBookmarkSubItem(DrawListViewSubItemEventArgs e, bool isSelected, bool isHovered)
        {
            if (e.Item == null) return;
            BookmarkItem item = e.Item.Tag as BookmarkItem;
            Rectangle bounds = e.Bounds;

            if (e.ColumnIndex == 0)
            {
                int iconSize = 16;
                int iconX = bounds.X + 12;
                int iconY = bounds.Y + (bounds.Height - iconSize) / 2;
                Rectangle iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);

                if (item != null && item.IsFolder)
                {
                    MaterialIcons.DrawIcon(e.Graphics, "folder", iconRect, ThemeManager.CurrentAccentColor);
                }
                else
                {
                    Image fav = (item != null && !string.IsNullOrEmpty(item.Url)) ? AppServices.Favicons.GetFavicon(item.Url) : null;
                    if (fav != null)
                    {
                        DpiDrawingHelper.DrawImagePixelPerfect(e.Graphics, fav, iconRect);
                    }
                    else
                    {
                        MaterialIcons.DrawIcon(e.Graphics, "globe", iconRect, ThemeManager.CurrentAccentColor);
                    }
                }

                int textX = iconX + iconSize + 8;
                Rectangle textRect = new Rectangle(textX, bounds.Y, Math.Max(0, bounds.Width - (textX - bounds.X) - 6), bounds.Height);
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
                TextRenderer.DrawText(e.Graphics, e.SubItem != null ? e.SubItem.Text : string.Empty, UITheme.FontDefaultBold, textRect, MaterialColors.TextPrimary, flags);
            }
            else
            {
                string text = e.SubItem != null ? e.SubItem.Text : string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    Color fg = MaterialColors.TextSecondary;
                    TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.NoPrefix;
                    if (e.Header.TextAlign == HorizontalAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;
                    else if (e.Header.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
                    else flags |= TextFormatFlags.Left;

                    int pad = 10;
                    Rectangle textRect = new Rectangle(bounds.X + pad, bounds.Y, Math.Max(0, bounds.Width - pad - 6), bounds.Height);
                    TextRenderer.DrawText(e.Graphics, text, UITheme.FontDefault, textRect, fg, flags);
                }
            }
        }

        private void OpenSelected(bool newTab)
        {
            if (_listView.SelectedItems.Count > 0)
            {
                for (int i = 0; i < _listView.SelectedItems.Count; i++)
                {
                    BookmarkItem item = _listView.SelectedItems[i].Tag as BookmarkItem;
                    if (item != null)
                    {
                        if (item.IsFolder)
                        {
                            List<BookmarkItem> children = AppServices.Bookmarks.GetAllChildren(item.Id);
                            if (children != null)
                            {
                                for (int c = 0; c < children.Count; c++)
                                {
                                    if (_openUrlCallback != null && !string.IsNullOrEmpty(children[c].Url))
                                    {
                                        _openUrlCallback(children[c].Url, true);
                                    }
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(item.Url))
                        {
                            if (_openUrlCallback != null)
                            {
                                _openUrlCallback(item.Url, newTab || i > 0);
                            }
                        }
                    }
                }
            }
        }

        private void DeleteSelected()
        {
            if (_listView.SelectedItems.Count == 0) return;

            string message;
            string title;

            if (_listView.SelectedItems.Count == 1)
            {
                BookmarkItem item = _listView.SelectedItems[0].Tag as BookmarkItem;
                if (item != null && item.IsFolder)
                {
                    int childCount = AppServices.Bookmarks.GetAllChildren(item.Id).Count;
                    title = AppStrings.Bookmarks.DeleteFolderTitle;
                    message = childCount > 0
                        ? string.Format(AppStrings.Bookmarks.ConfirmDeleteFolderWithChildrenFormat, item.Title, childCount)
                        : string.Format(AppStrings.Bookmarks.ConfirmDeleteFolderFormat, item.Title);
                }
                else
                {
                    string itemTitle = (item != null && !string.IsNullOrEmpty(item.Title)) ? item.Title : AppStrings.Bookmarks.DefaultItemName;
                    title = AppStrings.Bookmarks.DeleteBookmarkTitle;
                    message = string.Format(AppStrings.Bookmarks.ConfirmDeleteBookmarkFormat, itemTitle);
                }
            }
            else
            {
                title = AppStrings.Bookmarks.DeleteMultipleTitle;
                message = string.Format(AppStrings.Bookmarks.ConfirmDeleteMultipleFormat, _listView.SelectedItems.Count);
            }

            if (!PromptDeleteConfirmation(message, title))
            {
                return;
            }

            for (int i = _listView.SelectedItems.Count - 1; i >= 0; i--)
            {
                BookmarkItem item = _listView.SelectedItems[i].Tag as BookmarkItem;
                if (item != null)
                {
                    AppServices.Bookmarks.Remove(item.Id);
                }
            }
            PopulateFolderTree();
            RefreshList();
        }

        private void AddBtn_Click(object sender, EventArgs e)
        {
            string targetFolder = !string.IsNullOrEmpty(_selectedFolderId) ? _selectedFolderId : "bar";
            AddBookmarkToFolder(targetFolder);
        }

        private void AddFolderBtn_Click(object sender, EventArgs e)
        {
            string targetFolder = !string.IsNullOrEmpty(_selectedFolderId) ? _selectedFolderId : "bar";
            AddFolderToFolder(targetFolder);
        }

        private void AddBookmarkToFolder(string targetFolder)
        {
            using (BookmarkEditDialog prompt = new BookmarkEditDialog(
                AppStrings.Bookmarks.AddTitle,
                string.Empty,
                "https://",
                showBookmarkCheckbox: false,
                isBookmarked: false,
                primaryButtonText: AppStrings.General.BtnAdd,
                initialFolderId: targetFolder))
            {
                if (prompt.ShowDialog(this) == DialogResult.OK)
                {
                    string url = prompt.ItemUrl;
                    string title = prompt.ItemTitle;
                    if (!string.IsNullOrEmpty(url))
                    {
                        AppServices.Bookmarks.Add(string.IsNullOrEmpty(title) ? url : title, url, prompt.SelectedFolderId);
                        PopulateFolderTree();
                        RefreshList();
                    }
                }
            }
        }

        private void AddFolderToFolder(string targetFolder)
        {
            using (BookmarkEditDialog prompt = new BookmarkEditDialog(
                AppStrings.Bookmarks.AddFolderTitle,
                string.Empty,
                string.Empty,
                showBookmarkCheckbox: false,
                isBookmarked: false,
                primaryButtonText: AppStrings.General.BtnAdd,
                initialFolderId: targetFolder,
                showRemoveButton: false,
                isFolderOnly: true))
            {
                if (prompt.ShowDialog(this) == DialogResult.OK)
                {
                    string title = prompt.ItemTitle;
                    if (!string.IsNullOrEmpty(title))
                    {
                        BookmarkItem newFolder = AppServices.Bookmarks.AddFolder(title, prompt.SelectedFolderId);
                        PopulateFolderTree();
                        if (newFolder != null)
                        {
                            SelectFolderNode(newFolder.Id);
                        }
                        RefreshList();
                    }
                }
            }
        }

        private void EditFolderById(string folderId)
        {
            BookmarkItem item = AppServices.Bookmarks.GetById(folderId);
            if (item != null && item.IsFolder)
            {
                using (BookmarkEditDialog dialog = new BookmarkEditDialog(
                    AppStrings.Bookmarks.EditFolderTitle,
                    item.Title,
                    string.Empty,
                    showBookmarkCheckbox: false,
                    isBookmarked: false,
                    primaryButtonText: AppStrings.General.BtnSave,
                    initialFolderId: item.ParentId,
                    showRemoveButton: true,
                    isFolderOnly: true))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        if (dialog.IsDeleted)
                        {
                            AppServices.Bookmarks.Remove(item.Id);
                        }
                        else
                        {
                            AppServices.Bookmarks.Update(item.Id, dialog.ItemTitle, string.Empty, dialog.SelectedFolderId);
                        }
                        PopulateFolderTree();
                        RefreshList();
                    }
                }
            }
        }

        private void DeleteFolderById(string folderId)
        {
            BookmarkItem item = AppServices.Bookmarks.GetById(folderId);
            if (item != null && item.IsFolder)
            {
                int childCount = AppServices.Bookmarks.GetAllChildren(item.Id).Count;
                string title = AppStrings.Bookmarks.DeleteFolderTitle;
                string message = childCount > 0
                    ? string.Format(AppStrings.Bookmarks.ConfirmDeleteFolderWithChildrenFormat, item.Title, childCount)
                    : string.Format(AppStrings.Bookmarks.ConfirmDeleteFolderFormat, item.Title);

                if (!PromptDeleteConfirmation(message, title))
                {
                    return;
                }

                AppServices.Bookmarks.Remove(item.Id);
                _selectedFolderId = "bar";
                PopulateFolderTree();
                RefreshList();
            }
        }

        private void EditSelected()
        {
            if (_listView.SelectedItems.Count == 1)
            {
                BookmarkItem item = _listView.SelectedItems[0].Tag as BookmarkItem;
                if (item != null)
                {
                    if (item.IsFolder)
                    {
                        using (BookmarkEditDialog dialog = new BookmarkEditDialog(
                            AppStrings.Bookmarks.EditFolderTitle,
                            item.Title,
                            string.Empty,
                            showBookmarkCheckbox: false,
                            isBookmarked: false,
                            primaryButtonText: AppStrings.General.BtnSave,
                            initialFolderId: item.ParentId,
                            showRemoveButton: true,
                            isFolderOnly: true))
                        {
                            if (dialog.ShowDialog(this) == DialogResult.OK)
                            {
                                if (dialog.IsDeleted)
                                {
                                    AppServices.Bookmarks.Remove(item.Id);
                                }
                                else
                                {
                                    AppServices.Bookmarks.Update(item.Id, dialog.ItemTitle, string.Empty, dialog.SelectedFolderId);
                                }
                                PopulateFolderTree();
                                RefreshList();
                            }
                        }
                    }
                    else
                    {
                        using (BookmarkEditDialog dialog = new BookmarkEditDialog(
                            AppStrings.Bookmarks.EditTitle,
                            item.Title,
                            item.Url,
                            showBookmarkCheckbox: false,
                            isBookmarked: true,
                            primaryButtonText: AppStrings.General.BtnSave,
                            initialFolderId: item.ParentId,
                            showRemoveButton: true))
                        {
                            if (dialog.ShowDialog(this) == DialogResult.OK)
                            {
                                if (dialog.IsDeleted)
                                {
                                    AppServices.Bookmarks.Remove(item.Id);
                                }
                                else
                                {
                                    AppServices.Bookmarks.Update(item.Id, dialog.ItemTitle, dialog.ItemUrl, dialog.SelectedFolderId);
                                }
                                PopulateFolderTree();
                                RefreshList();
                            }
                        }
                    }
                }
            }
        }

        private void CopySelectedUrl()
        {
            if (_listView.SelectedItems.Count == 0) return;
            List<string> urls = new List<string>();
            for (int i = 0; i < _listView.SelectedItems.Count; i++)
            {
                BookmarkItem item = _listView.SelectedItems[i].Tag as BookmarkItem;
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
            if (_editBtn != null)
            {
                _editBtn.Enabled = (_listView.SelectedItems.Count == 1);
            }

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
            else if (e.KeyCode == Keys.F2)
            {
                e.SuppressKeyPress = true;
                EditSelected();
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
                AppServices.Bookmarks.BookmarksChanged -= Bookmarks_BookmarksChanged;
                ThemeManager.AccentColorChanged -= ThemeManager_AccentColorChanged;
            }
            base.Dispose(disposing);
        }
    }
}
