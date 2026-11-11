using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Bookmarks
{
    /// <summary>
    /// Unified dark-mode dialog for Adding/Editing Bookmarks, Editing Folders, and Editing Tabs.
    /// Features a dedicated TreeView showing folder hierarchy and [New folder] creation,
    /// styled with modern rounded inputs, pill buttons, Phosphor outline folder icons,
    /// triangle chevrons, and rounded selection pill matching the Chrome bookmark editor.
    /// </summary>
    public class BookmarkEditDialog : Form
    {
        private TextBox _titleBox;
        private TextBox _urlBox;
        private TreeView _folderTree;
        private Button _newFolderBtn;
        private CheckBox _bookmarkCheckBox;
        private Button _removeBtn;
        private Button _saveBtn;
        private Button _cancelBtn;
        private string _selectedFolderId = "bar";

        private bool _isFolderOnly;
        private bool _showRemoveButton;
        private bool _showBookmarkCheckbox;

        public bool IsDeleted { get; private set; }

        public string ItemTitle
        {
            get { return _titleBox != null ? _titleBox.Text.Trim() : string.Empty; }
            set { if (_titleBox != null) _titleBox.Text = value ?? string.Empty; }
        }

        public string ItemUrl
        {
            get { return _urlBox != null ? _urlBox.Text.Trim() : string.Empty; }
            set { if (_urlBox != null) _urlBox.Text = value ?? string.Empty; }
        }

        public string SelectedFolderId
        {
            get
            {
                if (_folderTree != null && _folderTree.SelectedNode != null)
                {
                    string id = _folderTree.SelectedNode.Tag as string;
                    if (!string.IsNullOrEmpty(id)) return id;
                }
                return _selectedFolderId ?? "bar";
            }
            set
            {
                _selectedFolderId = value ?? "bar";
                if (_folderTree != null)
                {
                    SelectFolderNode(_selectedFolderId);
                }
            }
        }

        public bool IsBookmarked
        {
            get { return _bookmarkCheckBox != null && _bookmarkCheckBox.Checked; }
            set { if (_bookmarkCheckBox != null) _bookmarkCheckBox.Checked = value; }
        }

        public bool ShowBookmarkCheckbox
        {
            get { return _showBookmarkCheckbox; }
            set
            {
                _showBookmarkCheckbox = value;
                if (_bookmarkCheckBox != null) _bookmarkCheckBox.Visible = value;
            }
        }

        public BookmarkEditDialog(
            string dialogTitle = null,
            string initialTitle = null,
            string initialUrl = null,
            bool showBookmarkCheckbox = false,
            bool isBookmarked = false,
            string primaryButtonText = null,
            string initialFolderId = null,
            bool showRemoveButton = false,
            bool isFolderOnly = false)
        {
            _showBookmarkCheckbox = showBookmarkCheckbox;
            _showRemoveButton = showRemoveButton;
            _isFolderOnly = isFolderOnly;

            InitializeComponent(dialogTitle, initialTitle, initialUrl, isBookmarked, primaryButtonText, initialFolderId);
        }

        private void InitializeComponent(
            string dialogTitle,
            string initialTitle,
            string initialUrl,
            bool isBookmarked,
            string primaryButtonText,
            string initialFolderId)
        {
            int targetHeight = _showBookmarkCheckbox ? 128 : (_isFolderOnly ? 380 : 440);
            string formTitle = dialogTitle ?? (_isFolderOnly ? AppStrings.Bookmarks.EditFolderTitle : AppStrings.Bookmarks.EditTitle);
            UITheme.ApplyModalDialog(this, formTitle, new Size(460, targetHeight));
            this.Padding = new Padding(16, 12, 16, 12);
            this.BackColor = UITheme.FormBackground;
            _selectedFolderId = string.IsNullOrEmpty(initialFolderId) ? "bar" : initialFolderId;

            if (_showBookmarkCheckbox)
            {
                // Compact 2-in-1 layout for Edit Tab / Star popup
                TableLayoutPanel formLayout = new TableLayoutPanel();
                formLayout.Dock = DockStyle.Fill;
                formLayout.BackColor = UITheme.FormBackground;
                formLayout.ColumnCount = 2;
                formLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50F));
                formLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                formLayout.RowCount = 3;
                formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                formLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                formLayout.ForeColor = UITheme.TextPrimary;

                Label titleLbl = new Label
                {
                    Text = AppStrings.Bookmarks.AddTitleLabel,
                    Anchor = AnchorStyles.Left,
                    AutoSize = true,
                    Font = UITheme.FontDefault,
                    ForeColor = Color.FromArgb(196, 199, 197)
                };
                _titleBox = new TextBox
                {
                    Text = initialTitle ?? string.Empty
                };
                Panel titleContainer = CreateRoundedInputBox(_titleBox, AppStrings.Bookmarks.AddTitlePlaceholder);
                formLayout.Controls.Add(titleLbl, 0, 0);
                formLayout.Controls.Add(titleContainer, 1, 0);

                Label urlLbl = new Label
                {
                    Text = AppStrings.Bookmarks.AddUrlLabel,
                    Anchor = AnchorStyles.Left,
                    AutoSize = true,
                    Font = UITheme.FontDefault,
                    ForeColor = Color.FromArgb(196, 199, 197)
                };
                _urlBox = new TextBox
                {
                    Text = initialUrl ?? "https://"
                };
                Panel urlContainer = CreateRoundedInputBox(_urlBox);
                formLayout.Controls.Add(urlLbl, 0, 1);
                formLayout.Controls.Add(urlContainer, 1, 1);

                Panel bottomPanel = new Panel();
                bottomPanel.Dock = DockStyle.Fill;
                bottomPanel.Margin = new Padding(0);
                bottomPanel.BackColor = Color.Transparent;

                _bookmarkCheckBox = new CheckBox
                {
                    Text = AppStrings.Browser.BookmarkThisTab,
                    Anchor = AnchorStyles.Left,
                    Location = new Point(0, 8),
                    AutoSize = true,
                    Checked = isBookmarked,
                    Visible = true,
                    Font = UITheme.FontDefault,
                    ForeColor = UITheme.TextPrimary
                };
                UITheme.ApplyCheckBox(_bookmarkCheckBox);
                bottomPanel.Controls.Add(_bookmarkCheckBox);

                FlowLayoutPanel btnFlow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Right,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = false,
                    FlowDirection = FlowDirection.LeftToRight,
                    Margin = new Padding(0),
                    BackColor = Color.Transparent
                };
                UITheme.ApplyFlowPanel(btnFlow);

                _saveBtn = new Button
                {
                    Text = primaryButtonText ?? AppStrings.General.BtnSave,
                    Size = new Size(78, 30),
                    Margin = new Padding(0, 4, 8, 0)
                };
                ApplyModernPillButton(_saveBtn, true, false);
                _saveBtn.Click += SaveBtn_Click;

                _cancelBtn = new Button
                {
                    Text = AppStrings.General.BtnCancel,
                    Size = new Size(78, 30),
                    DialogResult = DialogResult.Cancel,
                    Margin = new Padding(0, 4, 0, 0)
                };
                ApplyModernPillButton(_cancelBtn, false, true);

                btnFlow.Controls.Add(_saveBtn);
                btnFlow.Controls.Add(_cancelBtn);
                bottomPanel.Controls.Add(btnFlow);

                formLayout.Controls.Add(bottomPanel, 0, 2);
                formLayout.SetColumnSpan(bottomPanel, 2);
                this.Controls.Add(formLayout);
            }
            else
            {
                // Full Layout with Folder TreeView and [New folder] creation
                TableLayoutPanel mainLayout = new TableLayoutPanel();
                mainLayout.Dock = DockStyle.Fill;
                mainLayout.BackColor = UITheme.FormBackground;
                mainLayout.Margin = new Padding(0);
                mainLayout.ColumnCount = 1;
                mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                mainLayout.RowCount = 4;
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
                float fieldsHeight = _isFolderOnly ? 36F : 72F;
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldsHeight));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

                // Row 0: In-dialog Header Title
                Label headerLabel = new Label
                {
                    Text = formTitle,
                    Font = new Font(UITheme.FontDefault.FontFamily, 13f, FontStyle.Bold),
                    ForeColor = UITheme.TextPrimary,
                    AutoSize = true,
                    Dock = DockStyle.Left,
                    Margin = new Padding(0, 0, 0, 8),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                mainLayout.Controls.Add(headerLabel, 0, 0);

                // Row 1: Name and URL
                TableLayoutPanel fieldsLayout = new TableLayoutPanel();
                fieldsLayout.Dock = DockStyle.Fill;
                fieldsLayout.BackColor = UITheme.FormBackground;
                fieldsLayout.Margin = new Padding(0);
                fieldsLayout.ColumnCount = 2;
                fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50F));
                fieldsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

                if (_isFolderOnly)
                {
                    fieldsLayout.RowCount = 1;
                    fieldsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                }
                else
                {
                    fieldsLayout.RowCount = 2;
                    fieldsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                    fieldsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                }

                Label titleLbl = new Label
                {
                    Text = AppStrings.Bookmarks.AddTitleLabel,
                    Anchor = AnchorStyles.Left,
                    AutoSize = true,
                    Font = UITheme.FontDefault,
                    ForeColor = UITheme.TextSecondary
                };
                _titleBox = new TextBox
                {
                    Text = initialTitle ?? string.Empty
                };
                Panel titleContainer = CreateRoundedInputBox(_titleBox, AppStrings.Bookmarks.AddTitlePlaceholder);
                fieldsLayout.Controls.Add(titleLbl, 0, 0);
                fieldsLayout.Controls.Add(titleContainer, 1, 0);

                if (!_isFolderOnly)
                {
                    Label urlLbl = new Label
                    {
                        Text = AppStrings.Bookmarks.AddUrlLabel,
                        Anchor = AnchorStyles.Left,
                        AutoSize = true,
                        Font = UITheme.FontDefault,
                        ForeColor = UITheme.TextSecondary
                    };
                    _urlBox = new TextBox
                    {
                        Text = initialUrl ?? "https://"
                    };
                    Panel urlContainer = CreateRoundedInputBox(_urlBox);
                    fieldsLayout.Controls.Add(urlLbl, 0, 1);
                    fieldsLayout.Controls.Add(urlContainer, 1, 1);
                }
                mainLayout.Controls.Add(fieldsLayout, 0, 1);

                // Row 2: Folder TreeView in bordered container
                Panel treeBorderPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(27, 28, 30),
                    Padding = new Padding(4),
                    Margin = new Padding(0, 4, 0, 8)
                };

                bool treeFocused = false;
                treeBorderPanel.Paint += (s, e) =>
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle r = new Rectangle(0, 0, treeBorderPanel.Width - 1, treeBorderPanel.Height - 1);
                    Color borderColor = treeFocused ? ThemeManager.CurrentAccentColor : Color.FromArgb(95, 99, 104);
                    using (Pen p = new Pen(borderColor, 1f))
                    {
                        using (GraphicsPath path = UITheme.CreateRoundedPath(r, 8))
                        {
                            g.DrawPath(p, path);
                        }
                    }
                };

                _folderTree = new TreeView
                {
                    Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None,
                    HideSelection = false,
                    BackColor = Color.FromArgb(27, 28, 30),
                    ForeColor = Color.FromArgb(232, 234, 237),
                    ItemHeight = 24,
                    ShowLines = false,
                    ShowPlusMinus = false,
                    ShowRootLines = false,
                    FullRowSelect = false,
                    DrawMode = TreeViewDrawMode.OwnerDrawAll,
                    Font = UITheme.FontDefault
                };
                UITheme.EnableDoubleBuffering(_folderTree);

                _folderTree.GotFocus += (s, e) => { treeFocused = true; treeBorderPanel.Invalidate(); };
                _folderTree.LostFocus += (s, e) => { treeFocused = false; treeBorderPanel.Invalidate(); };

                _folderTree.DrawNode += (s, e) =>
                {
                    TreeNode node = e.Node;
                    if (node == null) return;

                    Graphics g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle bounds = e.Bounds;

                    using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(27, 28, 30)))
                    {
                        g.FillRectangle(bgBrush, bounds);
                    }

                    int indent = 16;
                    int startX = node.Level * indent + 6;
                    int centerY = bounds.Y + bounds.Height / 2;

                    // 1. Chevron arrow (if node has children)
                    if (node.Nodes != null && node.Nodes.Count > 0)
                    {
                        using (SolidBrush arrowBrush = new SolidBrush(Color.FromArgb(196, 199, 197)))
                        {
                            if (node.IsExpanded)
                            {
                                PointF[] downArrow = new PointF[]
                                {
                                    new PointF(startX, centerY - 2),
                                    new PointF(startX + 8, centerY - 2),
                                    new PointF(startX + 4, centerY + 3)
                                };
                                g.FillPolygon(arrowBrush, downArrow);
                            }
                            else
                            {
                                PointF[] rightArrow = new PointF[]
                                {
                                    new PointF(startX + 2, centerY - 4),
                                    new PointF(startX + 7, centerY),
                                    new PointF(startX + 2, centerY + 4)
                                };
                                g.FillPolygon(arrowBrush, rightArrow);
                            }
                        }
                    }

                    // 2. Phosphor outline folder icon
                    int iconX = startX + 14;
                    int iconY = centerY - 8;
                    Bitmap folderBmp = MaterialIcons.GetIcon("folder", 16, ThemeManager.CurrentAccentColor);
                    if (folderBmp != null)
                    {
                        g.DrawImage(folderBmp, iconX, iconY, 16, 16);
                    }

                    // 3. Folder Name Text & Selection Pill
                    int textX = iconX + 22;
                    string text = node.Text ?? string.Empty;
                    Font font = UITheme.FontDefault;
                    Size textSize = TextRenderer.MeasureText(g, text, font);

                    int pillHeight = Math.Min(bounds.Height - 4, 20);
                    int pillY = centerY - pillHeight / 2;
                    int pillWidth = textSize.Width + 8;
                    Rectangle pillRect = new Rectangle(textX - 4, pillY, pillWidth, pillHeight);

                    if (node.IsSelected)
                    {
                        using (GraphicsPath pillPath = UITheme.CreateRoundedPath(pillRect, 4))
                        {
                            Color selBg = Color.FromArgb(70, ThemeManager.CurrentAccentColor);
                            using (SolidBrush selBrush = new SolidBrush(selBg))
                            {
                                g.FillPath(selBrush, pillPath);
                            }
                            using (Pen borderPen = new Pen(ThemeManager.CurrentAccentColor, 1f))
                            {
                                g.DrawPath(borderPen, pillPath);
                            }
                        }
                        TextRenderer.DrawText(g, text, font, new Point(textX, centerY - textSize.Height / 2),
                            Color.White, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    }
                    else
                    {
                        TextRenderer.DrawText(g, text, font, new Point(textX, centerY - textSize.Height / 2),
                            UITheme.TextPrimary, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    }
                };

                _folderTree.MouseDown += (s, e) =>
                {
                    TreeNode clicked = _folderTree.GetNodeAt(e.X, e.Y);
                    if (clicked != null)
                    {
                        int startX = clicked.Level * 16 + 6;
                        if (e.X >= startX - 4 && e.X <= startX + 12 && clicked.Nodes.Count > 0)
                        {
                            clicked.Toggle();
                            return;
                        }
                        _folderTree.SelectedNode = clicked;
                    }
                };

                _folderTree.AfterSelect += (s, e) =>
                {
                    if (e.Node != null && e.Node.Tag is string)
                    {
                        _selectedFolderId = (string)e.Node.Tag;
                    }
                    _folderTree.Invalidate();
                };

                // Context menu for TreeView
                ContextMenuStrip treeMenu = new ContextMenuStrip();
                UITheme.ApplyContextMenu(treeMenu);
                ToolStripMenuItem addFolderItem = new ToolStripMenuItem(AppStrings.Bookmarks.BtnNewFolder, null, (s, e) => NewFolderBtn_Click(s, e));
                addFolderItem.Image = MaterialIcons.GetIcon("folder", 16, Color.FromArgb(196, 199, 197));
                treeMenu.Items.Add(addFolderItem);
                _folderTree.ContextMenuStrip = treeMenu;

                PopulateFolderTree(_selectedFolderId);
                treeBorderPanel.Controls.Add(_folderTree);
                mainLayout.Controls.Add(treeBorderPanel, 0, 2);

                // Row 3: Bottom Action Panel: [New folder] [Remove] on left, [Save] [Cancel] on right
                Panel bottomPanel = new Panel();
                bottomPanel.Dock = DockStyle.Fill;
                bottomPanel.Margin = new Padding(0);
                bottomPanel.BackColor = Color.Transparent;

                FlowLayoutPanel leftFlow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Left,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = false,
                    FlowDirection = FlowDirection.LeftToRight,
                    Margin = new Padding(0),
                    BackColor = Color.Transparent
                };
                UITheme.ApplyFlowPanel(leftFlow);

                _newFolderBtn = new Button
                {
                    Text = AppStrings.Bookmarks.BtnNewFolder,
                    Size = new Size(96, 32),
                    Margin = new Padding(0, 2, 8, 0)
                };
                ApplyModernPillButton(_newFolderBtn, false, false);
                _newFolderBtn.Click += NewFolderBtn_Click;
                leftFlow.Controls.Add(_newFolderBtn);

                if (_showRemoveButton)
                {
                    _removeBtn = new Button
                    {
                        Text = AppStrings.Bookmarks.BtnRemove,
                        Size = new Size(78, 32),
                        Margin = new Padding(0, 2, 0, 0)
                    };
                    ApplyModernPillButton(_removeBtn, false, false);
                    _removeBtn.Click += RemoveBtn_Click;
                    leftFlow.Controls.Add(_removeBtn);
                }

                FlowLayoutPanel rightFlow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Right,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = false,
                    FlowDirection = FlowDirection.LeftToRight,
                    Margin = new Padding(0),
                    BackColor = Color.Transparent
                };
                UITheme.ApplyFlowPanel(rightFlow);

                string defaultPrimaryText = (dialogTitle != null && dialogTitle.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? AppStrings.General.BtnAdd
                    : AppStrings.General.BtnSave;

                _saveBtn = new Button
                {
                    Text = primaryButtonText ?? defaultPrimaryText,
                    Size = new Size(82, 32),
                    Margin = new Padding(0, 2, 8, 0)
                };
                ApplyModernPillButton(_saveBtn, true, false);
                _saveBtn.Click += SaveBtn_Click;

                _cancelBtn = new Button
                {
                    Text = AppStrings.General.BtnCancel,
                    Size = new Size(82, 32),
                    DialogResult = DialogResult.Cancel,
                    Margin = new Padding(0, 2, 0, 0)
                };
                ApplyModernPillButton(_cancelBtn, false, true);

                rightFlow.Controls.Add(_saveBtn);
                rightFlow.Controls.Add(_cancelBtn);

                bottomPanel.Controls.Add(rightFlow);
                bottomPanel.Controls.Add(leftFlow);
                mainLayout.Controls.Add(bottomPanel, 0, 3);

                this.Controls.Add(mainLayout);
            }

            this.AcceptButton = _saveBtn;
            this.CancelButton = _cancelBtn;

            ThemeManager.AccentColorChanged += ThemeManager_AccentColorChanged;

            this.Shown += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_titleBox.Text))
                {
                    _titleBox.SelectAll();
                    _titleBox.Focus();
                }
                else
                {
                    _titleBox.Focus();
                }
            };
        }

        private Panel CreateRoundedInputBox(TextBox textBox, string placeholder = null)
        {
            Panel container = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 32,
                Padding = new Padding(8, 7, 8, 7),
                BackColor = UITheme.InputBackground
            };

            bool isFocused = false;

            textBox.BorderStyle = BorderStyle.None;
            textBox.BackColor = UITheme.InputBackground;
            textBox.ForeColor = UITheme.TextPrimary;
            textBox.Font = UITheme.FontDefault;
            textBox.Dock = DockStyle.Fill;

            textBox.GotFocus += (s, e) =>
            {
                isFocused = true;
                container.Invalidate();
            };
            textBox.LostFocus += (s, e) =>
            {
                isFocused = false;
                container.Invalidate();
            };

            container.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, container.Width - 1, container.Height - 1);
                Color borderColor = isFocused ? ThemeManager.CurrentAccentColor : UITheme.ControlBorder;
                float penWidth = isFocused ? 1.5f : 1f;
                using (Pen p = new Pen(borderColor, penWidth))
                {
                    using (GraphicsPath path = UITheme.CreateRoundedPath(r, 6))
                    {
                        g.DrawPath(p, path);
                    }
                }
            };

            container.Click += (s, e) => textBox.Focus();
            container.Controls.Add(textBox);
            return container;
        }

        private void ApplyModernPillButton(Button btn, bool isPrimary, bool isSubtle = false)
        {
            if (btn == null) return;
            UITheme.ApplyButton(btn, isPrimary: isPrimary);
        }

        private void ThemeManager_AccentColorChanged(object sender, EventArgs e)
        {
            if (!this.IsDisposed)
            {
                if (_folderTree != null && !_folderTree.IsDisposed)
                {
                    _folderTree.Invalidate();
                }
                this.Invalidate(true);
            }
        }

        private void PopulateFolderTree(string initialFolderId)
        {
            if (_folderTree == null) return;
            _folderTree.BeginUpdate();
            try
            {
                _folderTree.Nodes.Clear();

                TreeNode barNode = new TreeNode(AppStrings.Bookmarks.FolderBookmarksBar);
                barNode.Tag = "bar";
                barNode.ImageKey = "folder";
                barNode.SelectedImageKey = "folder";
                _folderTree.Nodes.Add(barNode);

                TreeNode otherNode = new TreeNode(AppStrings.Bookmarks.FolderOther);
                otherNode.Tag = "other";
                otherNode.ImageKey = "folder";
                otherNode.SelectedImageKey = "folder";
                _folderTree.Nodes.Add(otherNode);

                TreeNode mobileNode = new TreeNode(AppStrings.Bookmarks.FolderMobile);
                mobileNode.Tag = "mobile";
                mobileNode.ImageKey = "folder";
                mobileNode.SelectedImageKey = "folder";
                _folderTree.Nodes.Add(mobileNode);

                List<BookmarkItem> allFolders = null;
                try
                {
                    allFolders = AppServices.Bookmarks.GetFolders();
                }
                catch { }

                if (allFolders != null)
                {
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
                            childNode.SelectedImageKey = "folder";
                            barNode.Nodes.Add(childNode);
                            AddChildFolders(childNode, folder.Id, allFolders, visited);
                        }
                    }
                }

                _folderTree.ExpandAll();

                string target = !string.IsNullOrEmpty(initialFolderId) ? initialFolderId : (_selectedFolderId ?? "bar");
                SelectFolderNode(target);
            }
            finally
            {
                _folderTree.EndUpdate();
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
                    childNode.SelectedImageKey = "folder";
                    parentNode.Nodes.Add(childNode);
                    AddChildFolders(childNode, folder.Id, allFolders, visited);
                }
            }
        }

        private void SelectFolderNode(string folderId)
        {
            if (_folderTree == null || _folderTree.Nodes.Count == 0) return;
            string target = string.IsNullOrEmpty(folderId) ? "bar" : folderId;
            TreeNode node = FindNodeByTag(_folderTree.Nodes, target);
            if (node != null)
            {
                _folderTree.SelectedNode = node;
                node.EnsureVisible();
            }
            else if (_folderTree.Nodes.Count > 0)
            {
                _folderTree.SelectedNode = _folderTree.Nodes[0];
            }
        }

        private TreeNode FindNodeByTag(TreeNodeCollection nodes, string tag)
        {
            if (nodes == null || tag == null) return null;
            for (int i = 0; i < nodes.Count; i++)
            {
                TreeNode n = nodes[i];
                if (n != null)
                {
                    if (string.Equals(n.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        return n;
                    }
                    TreeNode child = FindNodeByTag(n.Nodes, tag);
                    if (child != null) return child;
                }
            }
            return null;
        }

        private void NewFolderBtn_Click(object sender, EventArgs e)
        {
            string parentId = SelectedFolderId;
            if (string.IsNullOrEmpty(parentId)) parentId = "bar";

            using (Form prompt = new Form())
            {
                UITheme.ApplyModalDialog(prompt, AppStrings.Bookmarks.AddFolderTitle, new Size(380, 130));
                prompt.Padding = new Padding(16, 12, 16, 12);
                prompt.BackColor = UITheme.FormBackground;

                Label lbl = new Label
                {
                    Text = AppStrings.Bookmarks.AddTitleLabel,
                    Location = new Point(14, 18),
                    AutoSize = true,
                    Font = UITheme.FontDefault,
                    ForeColor = Color.FromArgb(196, 199, 197)
                };
                TextBox txt = new TextBox
                {
                    Text = AppStrings.Bookmarks.BtnNewFolder
                };
                Panel txtContainer = CreateRoundedInputBox(txt);
                txtContainer.Location = new Point(62, 12);
                txtContainer.Size = new Size(290, 32);

                Button okBtn = new Button
                {
                    Text = AppStrings.General.BtnAdd,
                    DialogResult = DialogResult.OK,
                    Location = new Point(190, 56),
                    Size = new Size(78, 30)
                };
                ApplyModernPillButton(okBtn, true, false);

                Button cancelBtn = new Button
                {
                    Text = AppStrings.General.BtnCancel,
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(274, 56),
                    Size = new Size(78, 30)
                };
                ApplyModernPillButton(cancelBtn, false, true);

                prompt.Controls.Add(lbl);
                prompt.Controls.Add(txtContainer);
                prompt.Controls.Add(okBtn);
                prompt.Controls.Add(cancelBtn);
                prompt.AcceptButton = okBtn;
                prompt.CancelButton = cancelBtn;

                prompt.Shown += (s, ev) =>
                {
                    txt.SelectAll();
                    txt.Focus();
                };

                if (prompt.ShowDialog(this) == DialogResult.OK)
                {
                    string folderName = txt.Text.Trim();
                    if (!string.IsNullOrEmpty(folderName))
                    {
                        BookmarkItem newFld = AppServices.Bookmarks.AddFolder(folderName, parentId);
                        string newId = newFld != null ? newFld.Id : parentId;
                        PopulateFolderTree(newId);
                        _selectedFolderId = newId;
                    }
                }
            }
        }

        private void RemoveBtn_Click(object sender, EventArgs e)
        {
            this.IsDeleted = true;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void SaveBtn_Click(object sender, EventArgs e)
        {
            if (!_isFolderOnly)
            {
                string url = ItemUrl;
                if (string.IsNullOrEmpty(url))
                {
                    if (_urlBox != null) _urlBox.Focus();
                    return;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(ItemTitle))
                {
                    if (_titleBox != null) _titleBox.Focus();
                    return;
                }
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.AccentColorChanged -= ThemeManager_AccentColorChanged;
            }
            base.Dispose(disposing);
        }
    }
}
