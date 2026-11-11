using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using wb.Bookmarks;
using wb.Browser;
using wb.Core;
using wb.DataPortability;

namespace wb.UI
{
    public class TabDroppedOnBookmarksBarEventArgs : EventArgs
    {
        public int TabIndex { get; private set; }
        public Point ScreenPosition { get; private set; }
        public bool Handled { get; set; }

        public TabDroppedOnBookmarksBarEventArgs(int tabIndex, Point screenPt)
        {
            TabIndex = tabIndex;
            ScreenPosition = screenPt;
            Handled = false;
        }
    }

    /// <summary>
    /// Chrome-style Bookmarks Bar control docked directly below the TopBar.
    /// Supports bookmarks, folders, drop-down menus, overflow chevron, drag & drop reordering,
    /// moving items in and out of folders, dropping web links and tabs, Phosphor icons, and dynamic accent color styling.
    /// </summary>
    public class WbBookmarksBar : Control
    {
        private class BarItemSlot
        {
            public BookmarkItem Item;
            public Rectangle Bounds;
            public bool IsFolder;
            public bool IsOtherBookmarks;
            public bool IsOverflow;
        }

        private float _dpiScale = 1.0f;
        private List<BarItemSlot> _visibleSlots = new List<BarItemSlot>();
        private List<BookmarkItem> _overflowItems = new List<BookmarkItem>();
        private BarItemSlot _otherBookmarksSlot = null;
        private BarItemSlot _overflowSlot = null;

        private int _hoveredSlotIndex = -1;
        private int _pressedSlotIndex = -1;
        private bool _isOtherHovered = false;
        private bool _isOverflowHovered = false;

        // Drag & drop bookmark reordering and folder drop state
        private bool _isDraggingItem = false;
        private Point _dragStartPt = Point.Empty;
        private int _dragSlotIndex = -1;
        private int _dropInsertIndex = -1;
        private int _dragTargetFolderIndex = -1;

        // OLE URL drag drop state
        private bool _isOleDropTarget = false;
        private int _oleDropInsertIndex = -1;
        private int _oleHoveredFolderIndex = -1;

        private ToolTip _toolTip;
        private string _lastTooltipText = null;

        private bool _isChildContextMenuActive = false;
        private ContextMenuStrip _activeChildContextMenu = null;
        private readonly HashSet<string> _fetchingFaviconUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // In-folder dropmenu drag & drop reorder & folder nesting state
        private bool _isDraggingInMenu = false;
        private int _menuDropLineY = -1;
        private ToolStripItem _menuDropTargetItem = null;
        private bool _menuDropInsertAfter = false;
        private bool _menuDropIntoFolder = false;
        private ToolStripItem _menuHoveredFolderItem = null;
        private Timer _menuFolderHoverTimer = null;
        private List<ToolStripDropDown> _activeDragDropDownChain = new List<ToolStripDropDown>();
        private Dictionary<ToolStripDropDown, bool> _prevAutoCloseMap = new Dictionary<ToolStripDropDown, bool>();
        private ToolStripDropDown _activeDragDropDown = null;
        private string _activeDragFolderId = null;
        private long _lastMenuDragEndTime = 0;

        private readonly List<ToolStripDropDown> _openFolderDropDowns = new List<ToolStripDropDown>();

        public float DpiScale
        {
            get { return _dpiScale; }
            set
            {
                if (Math.Abs(_dpiScale - value) > 0.01f)
                {
                    _dpiScale = value;
                    this.Height = DpiHelper.Scale(28, _dpiScale);
                    RecomputeLayout();
                    this.Invalidate();
                }
            }
        }

        public Action<string, bool> NavigateCallback { get; set; }
        public Action<string> NewWindowCallback { get; set; }
        public Action<string> IncognitoCallback { get; set; }
        public Action OpenBookmarksManagerCallback { get; set; }
        public Func<string[]> GetCurrentTabInfoCallback { get; set; }

        public Func<string, string, bool> ConfirmDeleteHandler { get; set; }

        private bool PromptDeleteConfirmation(string message, string title)
        {
            if (ConfirmDeleteHandler != null)
            {
                return ConfirmDeleteHandler(message, title);
            }
            return MessageBox.Show(this.FindForm(), message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        public WbBookmarksBar()
        {
            this.SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            this.DoubleBuffered = true;
            this.Height = 28;
            this.Dock = DockStyle.Top;
            this.BackColor = MaterialColors.NavBarBackground;
            this.Font = UITheme.FontDefault;
            this.AllowDrop = true;

            _toolTip = new ToolTip();
            _toolTip.InitialDelay = 400;
            _toolTip.ReshowDelay = 100;

            HookServices();
            RecomputeLayout();
        }

        private void HookServices()
        {
            try
            {
                AppServices.Bookmarks.BookmarksChanged += Bookmarks_BookmarksChanged;
                AppServices.Bookmarks.BookmarksBarVisibilityChanged += Bookmarks_VisibilityChanged;
                ThemeManager.AccentColorChanged += ThemeManager_AccentColorChanged;
                PreloadBookmarkFavicons();
            }
            catch { }
        }

        private void PreloadBookmarkFavicons()
        {
            if (this.IsDisposed || !this.IsHandleCreated || !this.Visible) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (this.IsDisposed || !this.IsHandleCreated) return;
                    List<BookmarkItem> barItems = AppServices.Bookmarks.GetBarItems("bar");
                    if (barItems == null || barItems.Count == 0) return;

                    bool anyLoaded = false;
                    foreach (var item in barItems)
                    {
                        if (item == null || item.IsFolder || string.IsNullOrEmpty(item.Url)) continue;
                        Image img = AppServices.Favicons.GetFavicon(item.Url);
                        if (img != null)
                        {
                            anyLoaded = true;
                        }
                    }

                    if (anyLoaded && !this.IsDisposed && this.IsHandleCreated)
                    {
                        try
                        {
                            if (this.InvokeRequired)
                            {
                                this.BeginInvoke(new Action(() => this.Invalidate()));
                            }
                            else
                            {
                                this.Invalidate();
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            });
        }

        private void TriggerBookmarkFaviconFetch(string url)
        {
            if (string.IsNullOrEmpty(url) || this.IsDisposed || !this.IsHandleCreated) return;
            lock (_fetchingFaviconUrls)
            {
                if (_fetchingFaviconUrls.Contains(url)) return;
                _fetchingFaviconUrls.Add(url);
            }

            AppServices.Favicons.FetchFaviconAsync(url, null, img =>
            {
                if (img != null && !this.IsDisposed && this.IsHandleCreated)
                {
                    try
                    {
                        if (this.InvokeRequired)
                        {
                            this.BeginInvoke(new Action(() => this.Invalidate()));
                        }
                        else
                        {
                            this.Invalidate();
                        }
                    }
                    catch { }
                }
            });
        }

        private void Bookmarks_BookmarksChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.IsHandleCreated && this.Visible)
                {
                    PreloadBookmarkFavicons();
                }
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => { RecomputeLayout(); this.Invalidate(); }));
                }
                else
                {
                    RecomputeLayout();
                    this.Invalidate();
                }
            }
            catch { }
        }

        private void Bookmarks_VisibilityChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => { this.Visible = AppServices.Bookmarks.ShowBookmarksBar; }));
                }
                else
                {
                    this.Visible = AppServices.Bookmarks.ShowBookmarksBar;
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
                    this.BeginInvoke(new Action(() => this.Invalidate()));
                }
                else
                {
                    this.Invalidate();
                }
            }
            catch { }
        }

        public void RecomputeLayout()
        {
            _visibleSlots.Clear();
            _overflowItems.Clear();
            _otherBookmarksSlot = null;
            _overflowSlot = null;

            int clientW = this.ClientSize.Width;
            int clientH = this.ClientSize.Height;
            if (clientW <= 0 || clientH <= 0) return;

            int padY = Math.Max(2, DpiHelper.Scale(3, _dpiScale));
            int itemH = clientH - (padY * 2);

            // 1. Check Other Bookmarks
            List<BookmarkItem> otherItems = AppServices.Bookmarks.GetOtherBookmarks();
            bool hasOther = (otherItems != null && otherItems.Count > 0);
            int reservedRight = 6;

            if (hasOther)
            {
                using (Graphics g = this.CreateGraphics())
                {
                    int otherTextW = TextRenderer.MeasureText(g, AppStrings.Bookmarks.OtherBookmarks, this.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                    int otherW = otherTextW + DpiHelper.Scale(36, _dpiScale);
                    reservedRight += otherW + 4;
                    _otherBookmarksSlot = new BarItemSlot
                    {
                        Item = null,
                        Bounds = new Rectangle(clientW - reservedRight, padY, otherW, itemH),
                        IsFolder = true,
                        IsOtherBookmarks = true
                    };
                }
            }

            int overflowBtnW = DpiHelper.Scale(24, _dpiScale);
            int availableRight = clientW - reservedRight;
            int curX = DpiHelper.Scale(6, _dpiScale);

            List<BookmarkItem> barItems = AppServices.Bookmarks.GetBarItems("bar");
            if (barItems == null || barItems.Count == 0) return;

            using (Graphics g = this.CreateGraphics())
            {
                for (int i = 0; i < barItems.Count; i++)
                {
                    BookmarkItem item = barItems[i];
                    string displayTitle = !string.IsNullOrEmpty(item.Title) ? item.Title : item.Url;
                    int textW = TextRenderer.MeasureText(g, displayTitle, this.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;

                    // PadLeft(6) + Icon(16) + Gap(6) + Gap(4) + Caret(10) + PadRight(8) = 50px for folder
                    // PadLeft(6) + Icon(16) + Gap(6) + PadRight(8) = 36px for bookmark
                    int extraW = item.IsFolder ? DpiHelper.Scale(50, _dpiScale) : DpiHelper.Scale(36, _dpiScale);
                    int itemW = Math.Min(DpiHelper.Scale(200, _dpiScale), textW + extraW + DpiHelper.Scale(4, _dpiScale));
                    itemW = Math.Max(itemW, DpiHelper.Scale(32, _dpiScale));

                    // Check if this item fits with room for overflow button if there are subsequent items
                    bool isLast = (i == barItems.Count - 1);
                    int neededRight = isLast ? curX + itemW : curX + itemW + overflowBtnW + 4;

                    if (neededRight > availableRight && i > 0)
                    {
                        // Overflow starts here
                        for (int j = i; j < barItems.Count; j++)
                        {
                            _overflowItems.Add(barItems[j]);
                        }

                        _overflowSlot = new BarItemSlot
                        {
                            Item = null,
                            Bounds = new Rectangle(availableRight - overflowBtnW - 2, padY, overflowBtnW, itemH),
                            IsOverflow = true
                        };
                        break;
                    }
                    else
                    {
                        _visibleSlots.Add(new BarItemSlot
                        {
                            Item = item,
                            Bounds = new Rectangle(curX, padY, itemW, itemH),
                            IsFolder = item.IsFolder
                        });
                        curX += itemW + 2;
                    }
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecomputeLayout();
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Background
            using (SolidBrush bgBrush = new SolidBrush(MaterialColors.NavBarBackground))
            {
                g.FillRectangle(bgBrush, this.ClientRectangle);
            }

            // Bottom border separator
            using (Pen sepPen = new Pen(MaterialColors.Separator, 1))
            {
                g.DrawLine(sepPen, 0, this.Height - 1, this.Width, this.Height - 1);
            }

            // Empty state notice if no bar bookmarks
            if (_visibleSlots.Count == 0 && _otherBookmarksSlot == null)
            {
                using (Font italicFont = new Font(this.Font, FontStyle.Italic))
                using (SolidBrush textBrush = new SolidBrush(MaterialColors.TextDisabled))
                {
                    Rectangle textRect = new Rectangle(12, 0, this.Width - 24, this.Height - 1);
                    TextRenderer.DrawText(g, AppStrings.Bookmarks.BarEmptyPrompt, italicFont, textRect,
                        MaterialColors.TextDisabled, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }
                return;
            }

            Color accentColor = ThemeManager.AccentColor;

            // Render visible bookmark items
            for (int i = 0; i < _visibleSlots.Count; i++)
            {
                BarItemSlot slot = _visibleSlots[i];
                bool isHovered = (i == _hoveredSlotIndex);
                bool isPressed = (i == _pressedSlotIndex);
                bool isOleFolderTarget = (i == _oleHoveredFolderIndex);
                bool isDragFolderTarget = (_isDraggingItem && i == _dragTargetFolderIndex);

                DrawSlotButton(g, slot.Bounds, isHovered, isPressed, isOleFolderTarget || isDragFolderTarget);

                int padLeft = DpiHelper.Scale(6, _dpiScale);
                int padRight = DpiHelper.Scale(8, _dpiScale);
                int iconSize = DpiHelper.Scale(16, _dpiScale);
                int iconGap = DpiHelper.Scale(6, _dpiScale);

                int iconY = slot.Bounds.Top + (slot.Bounds.Height - iconSize) / 2;
                int iconX = slot.Bounds.Left + padLeft;
                Rectangle iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);

                if (slot.IsFolder)
                {
                    // Draw Phosphor Folder icon tinted with dynamic user accent color
                    MaterialIcons.DrawIcon(g, "folder", iconRect, accentColor);

                    int caretSize = DpiHelper.Scale(10, _dpiScale);
                    int caretGap = DpiHelper.Scale(4, _dpiScale);
                    int caretX = slot.Bounds.Right - padRight - caretSize;
                    Rectangle caretRect = new Rectangle(caretX, slot.Bounds.Top + (slot.Bounds.Height - caretSize) / 2, caretSize, caretSize);
                    MaterialIcons.DrawIcon(g, "caret-down", caretRect, MaterialColors.TextSecondary);

                    int textLeft = iconRect.Right + iconGap;
                    int textRight = caretX - caretGap;
                    Rectangle textRect = new Rectangle(textLeft, slot.Bounds.Top, Math.Max(0, textRight - textLeft), slot.Bounds.Height);

                    TextRenderer.DrawText(g, slot.Item.Title, this.Font, textRect, MaterialColors.TextPrimary,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
                else
                {
                    // Draw website Favicon or Phosphor Globe fallback
                    Image favicon = AppServices.Favicons.GetFavicon(slot.Item.Url);
                    if (favicon != null)
                    {
                        DpiDrawingHelper.DrawImagePixelPerfect(g, favicon, iconRect);
                    }
                    else
                    {
                        MaterialIcons.DrawIcon(g, "globe", iconRect, accentColor);
                        TriggerBookmarkFaviconFetch(slot.Item.Url);
                    }

                    int textLeft = iconRect.Right + iconGap;
                    int textRight = slot.Bounds.Right - padRight;
                    Rectangle textRect = new Rectangle(textLeft, slot.Bounds.Top, Math.Max(0, textRight - textLeft), slot.Bounds.Height);

                    TextRenderer.DrawText(g, slot.Item.Title, this.Font, textRect, MaterialColors.TextPrimary,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }

            // Render Overflow chevron button >>
            if (_overflowSlot != null)
            {
                DrawSlotButton(g, _overflowSlot.Bounds, _isOverflowHovered, false, false);
                TextRenderer.DrawText(g, "»", this.Font, _overflowSlot.Bounds, MaterialColors.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Render Other Bookmarks button
            if (_otherBookmarksSlot != null)
            {
                DrawSlotButton(g, _otherBookmarksSlot.Bounds, _isOtherHovered, false, _isOtherHovered && (_isDraggingItem || _isOleDropTarget));

                int iconSize = DpiHelper.Scale(16, _dpiScale);
                int iconY = _otherBookmarksSlot.Bounds.Top + (_otherBookmarksSlot.Bounds.Height - iconSize) / 2;
                int iconX = _otherBookmarksSlot.Bounds.Left + DpiHelper.Scale(6, _dpiScale);
                Rectangle iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);

                MaterialIcons.DrawIcon(g, "folder", iconRect, accentColor);

                int textLeft = iconRect.Right + DpiHelper.Scale(6, _dpiScale);
                Rectangle textRect = new Rectangle(textLeft, _otherBookmarksSlot.Bounds.Top, _otherBookmarksSlot.Bounds.Right - textLeft - DpiHelper.Scale(6, _dpiScale), _otherBookmarksSlot.Bounds.Height);
                TextRenderer.DrawText(g, AppStrings.Bookmarks.OtherBookmarks, this.Font, textRect, MaterialColors.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            // Draw Drag & Drop insertion indicator line
            int insertIdx = _isDraggingItem ? _dropInsertIndex : (_isOleDropTarget ? _oleDropInsertIndex : -1);
            if (insertIdx >= 0 && _dragTargetFolderIndex < 0 && _oleHoveredFolderIndex < 0)
            {
                int markerX;
                if (_visibleSlots.Count == 0)
                {
                    markerX = DpiHelper.Scale(6, _dpiScale);
                }
                else if (insertIdx < _visibleSlots.Count)
                {
                    markerX = _visibleSlots[insertIdx].Bounds.Left - 1;
                }
                else
                {
                    markerX = _visibleSlots[_visibleSlots.Count - 1].Bounds.Right + 1;
                }

                using (Pen markerPen = new Pen(accentColor, 2.5f))
                {
                    g.DrawLine(markerPen, markerX, 2, markerX, this.Height - 3);
                }
            }
        }

        private void DrawSlotButton(Graphics g, Rectangle bounds, bool isHovered, bool isPressed, bool isDropTarget)
        {
            if (isDropTarget)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(60, ThemeManager.AccentColor)))
                using (Pen borderPen = new Pen(ThemeManager.AccentColor, 1.5f))
                {
                    FillRoundedRect(g, brush, bounds, 3);
                    DrawRoundedRect(g, borderPen, bounds, 3);
                }
            }
            else if (isPressed)
            {
                using (SolidBrush brush = new SolidBrush(MaterialColors.ButtonPressed))
                {
                    FillRoundedRect(g, brush, bounds, 3);
                }
            }
            else if (isHovered)
            {
                using (SolidBrush brush = new SolidBrush(MaterialColors.ButtonHover))
                {
                    FillRoundedRect(g, brush, bounds, 3);
                }
            }
        }

        private void FillRoundedRect(Graphics g, Brush brush, Rectangle r, int radius)
        {
            using (GraphicsPath path = CreateRoundedPath(r, radius))
            {
                g.FillPath(brush, path);
            }
        }

        private void DrawRoundedRect(Graphics g, Pen pen, Rectangle r, int radius)
        {
            using (GraphicsPath path = CreateRoundedPath(r, radius))
            {
                g.DrawPath(pen, path);
            }
        }

        private GraphicsPath CreateRoundedPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private int HitTestSlot(Point pt)
        {
            for (int i = 0; i < _visibleSlots.Count; i++)
            {
                if (_visibleSlots[i].Bounds.Contains(pt))
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // Handle bookmark item dragging for reorder or dropping into a folder
            if (e.Button == MouseButtons.Left && _dragSlotIndex >= 0 && !_isDraggingItem)
            {
                int dx = Math.Abs(e.X - _dragStartPt.X);
                int dy = Math.Abs(e.Y - _dragStartPt.Y);
                if (dx > 6 || dy > 6)
                {
                    _isDraggingItem = true;
                    this.Cursor = Cursors.Hand;
                }
            }

            if (_isDraggingItem)
            {
                int hitSlot = HitTestSlot(e.Location);
                bool hitOther = (_otherBookmarksSlot != null && _otherBookmarksSlot.Bounds.Contains(e.Location));

                if (hitSlot >= 0 && hitSlot != _dragSlotIndex && _visibleSlots[hitSlot].IsFolder)
                {
                    _dragTargetFolderIndex = hitSlot;
                    _dropInsertIndex = -1;
                    _isOtherHovered = false;
                }
                else if (hitOther)
                {
                    _dragTargetFolderIndex = -1;
                    _dropInsertIndex = -1;
                    _isOtherHovered = true;
                }
                else
                {
                    _dragTargetFolderIndex = -1;
                    _isOtherHovered = false;
                    _dropInsertIndex = ComputeDropIndex(e.X);
                }
                this.Invalidate();
                return;
            }

            int hit = HitTestSlot(e.Location);
            bool otherHit = (_otherBookmarksSlot != null && _otherBookmarksSlot.Bounds.Contains(e.Location));
            bool overflowHit = (_overflowSlot != null && _overflowSlot.Bounds.Contains(e.Location));

            if (hit != _hoveredSlotIndex || otherHit != _isOtherHovered || overflowHit != _isOverflowHovered)
            {
                _hoveredSlotIndex = hit;
                _isOtherHovered = otherHit;
                _isOverflowHovered = overflowHit;
                this.Invalidate();

                // Update Tooltip (no tooltips for folders on bookmark bar)
                if (hit >= 0 && hit < _visibleSlots.Count)
                {
                    BookmarkItem item = _visibleSlots[hit].Item;
                    if (!item.IsFolder)
                    {
                        string tip = string.Format("{0}\n{1}", item.Title, item.Url);
                        if (_lastTooltipText != tip)
                        {
                            _lastTooltipText = tip;
                            _toolTip.SetToolTip(this, tip);
                        }
                    }
                    else
                    {
                        if (_lastTooltipText != null)
                        {
                            _lastTooltipText = null;
                            _toolTip.SetToolTip(this, null);
                        }
                    }
                }
                else
                {
                    if (_lastTooltipText != null)
                    {
                        _lastTooltipText = null;
                        _toolTip.SetToolTip(this, null);
                    }
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                int hit = HitTestSlot(e.Location);
                if (hit >= 0)
                {
                    _pressedSlotIndex = hit;
                    _dragSlotIndex = hit;
                    _dragStartPt = e.Location;
                    this.Invalidate();
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                int hit = HitTestSlot(e.Location);
                if (hit >= 0)
                {
                    BookmarkItem item = _visibleSlots[hit].Item;
                    if (item.IsFolder)
                    {
                        ShowFolderContextMenu(item, this.PointToScreen(e.Location));
                    }
                    else
                    {
                        ShowBookmarkContextMenu(item, this.PointToScreen(e.Location));
                    }
                }
                else if (_otherBookmarksSlot != null && _otherBookmarksSlot.Bounds.Contains(e.Location))
                {
                    ShowOtherBookmarksContextMenu(this.PointToScreen(e.Location));
                }
                else
                {
                    ShowEmptyBarContextMenu(this.PointToScreen(e.Location));
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                int hit = HitTestSlot(e.Location);
                if (hit >= 0)
                {
                    BookmarkItem item = _visibleSlots[hit].Item;
                    if (item.IsFolder)
                    {
                        OpenAllBookmarksInFolder(item.Id, true);
                    }
                    else if (!string.IsNullOrEmpty(item.Url))
                    {
                        if (NavigateCallback != null) NavigateCallback(item.Url, true);
                    }
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_isDraggingItem && _dragSlotIndex >= 0)
            {
                int fromIdx = _dragSlotIndex;
                int toIdx = _dropInsertIndex;
                int folderTargetIdx = _dragTargetFolderIndex;
                bool droppedOnOther = _isOtherHovered;

                _isDraggingItem = false;
                _dragSlotIndex = -1;
                _dropInsertIndex = -1;
                _dragTargetFolderIndex = -1;
                _isOtherHovered = false;
                this.Cursor = Cursors.Default;
                this.Invalidate();

                if (fromIdx >= 0 && fromIdx < _visibleSlots.Count)
                {
                    BookmarkItem itemToMove = _visibleSlots[fromIdx].Item;
                    if (itemToMove != null)
                    {
                        // 1. Dropped onto a folder on the bar -> Move INTO folder!
                        if (folderTargetIdx >= 0 && folderTargetIdx < _visibleSlots.Count && _visibleSlots[folderTargetIdx].IsFolder)
                        {
                            string targetFolderId = _visibleSlots[folderTargetIdx].Item.Id;
                            AppServices.Bookmarks.MoveItem(itemToMove.Id, -1, targetFolderId);
                            return;
                        }

                        // 2. Dropped onto "Other bookmarks" -> Move into "other"
                        if (droppedOnOther)
                        {
                            AppServices.Bookmarks.MoveItem(itemToMove.Id, -1, "other");
                            return;
                        }

                        // 3. Reordered along the bar
                        if (toIdx >= 0)
                        {
                            if (fromIdx == toIdx || toIdx == fromIdx + 1)
                            {
                                return;
                            }

                            List<BookmarkItem> allItems = AppServices.Bookmarks.GetAll();
                            int globalTargetIdx;
                            if (toIdx < _visibleSlots.Count)
                            {
                                string targetId = _visibleSlots[toIdx].Item.Id;
                                globalTargetIdx = allItems.Count;
                                for (int i = 0; i < allItems.Count; i++)
                                {
                                    if (allItems[i].Id == targetId) { globalTargetIdx = i; break; }
                                }
                            }
                            else
                            {
                                // Dropped into the last column (after the last visible slot)
                                if (_visibleSlots.Count > 0)
                                {
                                    string lastId = _visibleSlots[_visibleSlots.Count - 1].Item.Id;
                                    globalTargetIdx = allItems.Count;
                                    for (int i = 0; i < allItems.Count; i++)
                                    {
                                        if (allItems[i].Id == lastId) { globalTargetIdx = i + 1; break; }
                                    }
                                }
                                else
                                {
                                    globalTargetIdx = allItems.Count;
                                }
                            }

                            AppServices.Bookmarks.MoveItem(itemToMove.Id, globalTargetIdx);
                        }
                    }
                }
                return;
            }

            _isDraggingItem = false;
            _dragSlotIndex = -1;
            _dropInsertIndex = -1;
            _dragTargetFolderIndex = -1;

            if (e.Button == MouseButtons.Left)
            {
                int hit = HitTestSlot(e.Location);
                _pressedSlotIndex = -1;
                this.Invalidate();

                if (hit >= 0 && hit == _hoveredSlotIndex)
                {
                    BookmarkItem item = _visibleSlots[hit].Item;
                    if (item.IsFolder)
                    {
                        Point menuLocation = this.PointToScreen(new Point(_visibleSlots[hit].Bounds.Left, _visibleSlots[hit].Bounds.Bottom));
                        ShowFolderDropdown(item, menuLocation);
                    }
                    else if (!string.IsNullOrEmpty(item.Url))
                    {
                        bool newTab = (ModifierKeys == Keys.Control);
                        if (NavigateCallback != null) NavigateCallback(item.Url, newTab);
                    }
                }
                else if (_otherBookmarksSlot != null && _otherBookmarksSlot.Bounds.Contains(e.Location))
                {
                    Point menuLocation = this.PointToScreen(new Point(_otherBookmarksSlot.Bounds.Left, _otherBookmarksSlot.Bounds.Bottom));
                    ShowOtherBookmarksDropdown(menuLocation);
                }
                else if (_overflowSlot != null && _overflowSlot.Bounds.Contains(e.Location))
                {
                    Point menuLocation = this.PointToScreen(new Point(_overflowSlot.Bounds.Left, _overflowSlot.Bounds.Bottom));
                    ShowOverflowDropdown(menuLocation);
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoveredSlotIndex = -1;
            _pressedSlotIndex = -1;
            _isOtherHovered = false;
            _isOverflowHovered = false;
            _lastTooltipText = null;
            _toolTip.SetToolTip(this, null);
            this.Invalidate();
        }

        private int ComputeDropIndex(int mouseX)
        {
            if (_visibleSlots.Count == 0) return 0;
            for (int i = 0; i < _visibleSlots.Count; i++)
            {
                Rectangle bounds = _visibleSlots[i].Bounds;
                int midX = bounds.Left + bounds.Width / 2;
                if (mouseX < midX) return i;
            }
            return _visibleSlots.Count;
        }

        #region OLE Drag & Drop (Web Links, Addresses, Moving out of Folders)

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            if (drgevent.Data.GetDataPresent("wb_bookmark_id") || WebDragDropHelper.HasUrlData(drgevent.Data))
            {
                drgevent.Effect = drgevent.Data.GetDataPresent("wb_bookmark_id") ? DragDropEffects.Move : DragDropEffects.Copy;
                _isOleDropTarget = true;
                this.Invalidate();
            }
            else
            {
                drgevent.Effect = DragDropEffects.None;
            }
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            base.OnDragOver(drgevent);
            bool isInternalBookmark = drgevent.Data.GetDataPresent("wb_bookmark_id");
            if (isInternalBookmark || WebDragDropHelper.HasUrlData(drgevent.Data))
            {
                drgevent.Effect = isInternalBookmark ? DragDropEffects.Move : DragDropEffects.Copy;
                Point clientPt = this.PointToClient(new Point(drgevent.X, drgevent.Y));

                bool hitOther = (_otherBookmarksSlot != null && _otherBookmarksSlot.Bounds.Contains(clientPt));
                if (hitOther)
                {
                    _oleHoveredFolderIndex = -1;
                    _oleDropInsertIndex = -1;
                    _isOtherHovered = true;
                }
                else
                {
                    _isOtherHovered = false;
                    int hitSlot = HitTestSlot(clientPt);
                    int edgeMargin = Math.Max(4, (int)(8 * _dpiScale));
                    bool dropIntoFolder = false;
                    if (hitSlot >= 0 && _visibleSlots[hitSlot].IsFolder)
                    {
                        Rectangle bounds = _visibleSlots[hitSlot].Bounds;
                        if (clientPt.X >= bounds.Left + edgeMargin && clientPt.X <= bounds.Right - edgeMargin)
                        {
                            string targetFolderId = _visibleSlots[hitSlot].Item.Id;
                            string draggedBmId = isInternalBookmark ? (drgevent.Data.GetData("wb_bookmark_id") as string) : null;
                            if (string.IsNullOrEmpty(draggedBmId) ||
                                (!string.Equals(draggedBmId, targetFolderId, StringComparison.OrdinalIgnoreCase) &&
                                 !AppServices.Bookmarks.IsDescendantOf(targetFolderId, draggedBmId)))
                            {
                                dropIntoFolder = true;
                            }
                        }
                    }

                    if (dropIntoFolder)
                    {
                        _oleHoveredFolderIndex = hitSlot;
                        _oleDropInsertIndex = -1;
                    }
                    else
                    {
                        _oleHoveredFolderIndex = -1;
                        _oleDropInsertIndex = ComputeDropIndex(clientPt.X);
                    }
                }
                this.Invalidate();
            }
            else
            {
                drgevent.Effect = DragDropEffects.None;
            }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            _isOleDropTarget = false;
            _oleDropInsertIndex = -1;
            _oleHoveredFolderIndex = -1;
            _isOtherHovered = false;
            this.Invalidate();
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);
            int targetInsertIdx = _oleDropInsertIndex;
            int targetFolderIdx = _oleHoveredFolderIndex;
            Point dropPt = this.PointToClient(new Point(drgevent.X, drgevent.Y));
            bool droppedOnOther = (_otherBookmarksSlot != null && _otherBookmarksSlot.Bounds.Contains(dropPt));

            _isOleDropTarget = false;
            _oleDropInsertIndex = -1;
            _oleHoveredFolderIndex = -1;
            _isOtherHovered = false;
            this.Invalidate();

            // 1. Moving an existing bookmark or folder out of a folder or between folders/bar
            if (drgevent.Data.GetDataPresent("wb_bookmark_id"))
            {
                string bookmarkId = drgevent.Data.GetData("wb_bookmark_id") as string;
                if (!string.IsNullOrEmpty(bookmarkId))
                {
                    if (droppedOnOther)
                    {
                        AppServices.Bookmarks.MoveItem(bookmarkId, -1, "other");
                    }
                    else if (targetFolderIdx >= 0 && targetFolderIdx < _visibleSlots.Count && _visibleSlots[targetFolderIdx].IsFolder)
                    {
                        string targetFolderId = _visibleSlots[targetFolderIdx].Item.Id;
                        if (!string.Equals(bookmarkId, targetFolderId, StringComparison.OrdinalIgnoreCase))
                        {
                            AppServices.Bookmarks.MoveItem(bookmarkId, -1, targetFolderId);
                        }
                    }
                    else
                    {
                        List<BookmarkItem> allItems = AppServices.Bookmarks.GetAll();
                        int globalTargetIdx = allItems.Count;
                        if (targetInsertIdx >= 0 && targetInsertIdx < _visibleSlots.Count)
                        {
                            string targetId = _visibleSlots[targetInsertIdx].Item.Id;
                            for (int i = 0; i < allItems.Count; i++)
                            {
                                if (allItems[i].Id == targetId) { globalTargetIdx = i; break; }
                            }
                        }
                        else if (targetInsertIdx >= _visibleSlots.Count && _visibleSlots.Count > 0)
                        {
                            string lastId = _visibleSlots[_visibleSlots.Count - 1].Item.Id;
                            for (int i = 0; i < allItems.Count; i++)
                            {
                                if (allItems[i].Id == lastId) { globalTargetIdx = i + 1; break; }
                            }
                        }
                        AppServices.Bookmarks.MoveItem(bookmarkId, globalTargetIdx, "bar");
                    }
                    CloseAllOpenDropDowns();
                    return;
                }
            }

            // 2. Dropping a URL from an external app or web page
            if (WebDragDropHelper.HasUrlData(drgevent.Data))
            {
                string rawUrl = WebDragDropHelper.ExtractUrlFromDataObject(drgevent.Data);
                if (!string.IsNullOrEmpty(rawUrl) && InternalPageHelper.CanBookmarkPage(rawUrl))
                {
                    string title = rawUrl;
                    try
                    {
                        Uri uri = new Uri(rawUrl);
                        title = uri.Host;
                    }
                    catch { }

                    string parentId = "bar";
                    if (targetFolderIdx >= 0 && targetFolderIdx < _visibleSlots.Count && _visibleSlots[targetFolderIdx].IsFolder)
                    {
                        parentId = _visibleSlots[targetFolderIdx].Item.Id;
                    }

                    BookmarkItem added = AppServices.Bookmarks.Add(title, rawUrl, parentId);
                    if (added != null && parentId == "bar" && targetInsertIdx >= 0)
                    {
                        List<BookmarkItem> all = AppServices.Bookmarks.GetAll();
                        int globalIdx = all.Count;
                        if (targetInsertIdx < _visibleSlots.Count)
                        {
                            string targetId = _visibleSlots[targetInsertIdx].Item.Id;
                            for (int i = 0; i < all.Count; i++)
                            {
                                if (all[i].Id == targetId) { globalIdx = i; break; }
                            }
                        }
                        else if (_visibleSlots.Count > 0)
                        {
                            string lastId = _visibleSlots[_visibleSlots.Count - 1].Item.Id;
                            for (int i = 0; i < all.Count; i++)
                            {
                                if (all[i].Id == lastId) { globalIdx = i + 1; break; }
                            }
                        }
                        AppServices.Bookmarks.MoveItem(added.Id, globalIdx);
                    }
                    CloseAllOpenDropDowns();
                }
            }
        }

        /// <summary>
        /// Handles quick bookmark creation when a tab is dragged downward from the TopBar onto the Bookmarks Bar.
        /// </summary>
        public void AddBookmarkFromTab(string title, string url, Point screenPt)
        {
            if (string.IsNullOrEmpty(url) || !InternalPageHelper.CanBookmarkPage(url)) return;

            Point clientPt = this.PointToClient(screenPt);
            int hitSlot = HitTestSlot(clientPt);
            string parentId = "bar";

            if (hitSlot >= 0 && _visibleSlots[hitSlot].IsFolder)
            {
                parentId = _visibleSlots[hitSlot].Item.Id;
            }

            string bookmarkTitle = !string.IsNullOrEmpty(title) ? title : url;
            BookmarkItem item = AppServices.Bookmarks.Add(bookmarkTitle, url, parentId);

            if (item != null && parentId == "bar")
            {
                int insertIdx = ComputeDropIndex(clientPt.X);
                List<BookmarkItem> all = AppServices.Bookmarks.GetAll();
                int globalIdx = all.Count;
                if (insertIdx < _visibleSlots.Count)
                {
                    string targetId = _visibleSlots[insertIdx].Item.Id;
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i].Id == targetId) { globalIdx = i; break; }
                    }
                }
                else if (_visibleSlots.Count > 0)
                {
                    string lastId = _visibleSlots[_visibleSlots.Count - 1].Item.Id;
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i].Id == lastId) { globalIdx = i + 1; break; }
                    }
                }
                AppServices.Bookmarks.MoveItem(item.Id, globalIdx);
            }
        }

        #endregion

        #region Dropdown & Context Menus

        private void CloseAllOpenDropDowns()
        {
            if (_activeDragDropDownChain != null)
            {
                for (int i = _activeDragDropDownChain.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (_activeDragDropDownChain[i] != null && !_activeDragDropDownChain[i].IsDisposed)
                        {
                            _activeDragDropDownChain[i].Close();
                        }
                    }
                    catch { }
                }
                _activeDragDropDownChain.Clear();
            }
            if (_activeDragDropDown != null && !_activeDragDropDown.IsDisposed)
            {
                try { _activeDragDropDown.Close(); } catch { }
            }
            if (_activeChildContextMenu != null && !_activeChildContextMenu.IsDisposed)
            {
                try { _activeChildContextMenu.Close(); } catch { }
            }
        }

        private void CloseChildSubMenus(ToolStripDropDown dd)
        {
            if (dd == null) return;
            for (int i = 0; i < dd.Items.Count; i++)
            {
                ToolStripDropDownItem dropDownItem = dd.Items[i] as ToolStripDropDownItem;
                if (dropDownItem != null && dropDownItem.HasDropDownItems && dropDownItem.DropDown.Visible)
                {
                    try { dropDownItem.DropDown.Close(); } catch { }
                }
            }
        }

        private void StartMenuFolderHoverTimer(ToolStripMenuItem folderMenu)
        {
            StopMenuFolderHoverTimer();
            if (folderMenu == null || folderMenu.DropDown == null || folderMenu.DropDown.Visible) return;

            if (_menuFolderHoverTimer == null)
            {
                _menuFolderHoverTimer = new Timer();
                _menuFolderHoverTimer.Interval = 400;
                _menuFolderHoverTimer.Tick += (s, ev) =>
                {
                    StopMenuFolderHoverTimer();
                    if (_isDraggingInMenu && _menuHoveredFolderItem is ToolStripMenuItem)
                    {
                        ToolStripMenuItem item = (ToolStripMenuItem)_menuHoveredFolderItem;
                        if (item.DropDown != null && !item.DropDown.Visible && !item.DropDown.IsDisposed)
                        {
                            if (!_activeDragDropDownChain.Contains(item.DropDown))
                            {
                                _activeDragDropDownChain.Add(item.DropDown);
                                _prevAutoCloseMap[item.DropDown] = item.DropDown.AutoClose;
                                item.DropDown.AutoClose = false;
                            }
                            item.ShowDropDown();
                        }
                    }
                };
            }
            _menuFolderHoverTimer.Start();
        }

        private void StopMenuFolderHoverTimer()
        {
            if (_menuFolderHoverTimer != null)
            {
                _menuFolderHoverTimer.Stop();
            }
        }

        public void ConfigureDropDownConstraints(ToolStripDropDown menu, Point screenPt)
        {
            UITheme.ConfigureDropDownConstraints(menu, screenPt, _dpiScale);
        }

        public static void ScrollDropDown(ToolStripDropDown dd, bool up, int count = 1)
        {
            UITheme.ScrollDropDown(dd, up, count);
        }

        private void RegisterFolderDropDown(ToolStripDropDown dd)
        {
            if (dd == null) return;
            dd.AllowDrop = true;
            dd.Renderer = new BookmarksMenuRenderer(this);
            UITheme.ApplyContextMenu(dd);

            if (!_openFolderDropDowns.Contains(dd))
            {
                _openFolderDropDowns.Add(dd);
            }

            dd.Closed += (s, e) =>
            {
                _openFolderDropDowns.Remove(dd);
            };

            dd.Closing += (s, e) =>
            {
                if ((_isChildContextMenuActive || _isDraggingInMenu || _isDraggingItem) && e.CloseReason != ToolStripDropDownCloseReason.CloseCalled)
                {
                    e.Cancel = true;
                }
            };

            dd.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent("wb_bookmark_id") || WebDragDropHelper.HasUrlData(e.Data))
                {
                    e.Effect = DragDropEffects.Move;
                    _isDraggingInMenu = true;
                    _activeDragDropDown = dd;
                    _activeDragFolderId = (dd.Tag as string) ?? "bar";

                    if (!_activeDragDropDownChain.Contains(dd))
                    {
                        _activeDragDropDownChain.Add(dd);
                        _prevAutoCloseMap[dd] = dd.AutoClose;
                        dd.AutoClose = false;
                    }
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            };

            dd.DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent("wb_bookmark_id") || WebDragDropHelper.HasUrlData(e.Data))
                {
                    e.Effect = DragDropEffects.Move;
                    Point clientPt = dd.PointToClient(new Point(e.X, e.Y));

                    string draggedBmId = e.Data.GetDataPresent("wb_bookmark_id") ? (e.Data.GetData("wb_bookmark_id") as string) : null;

                    ToolStripItem targetItem = null;
                    bool insertAfter = false;
                    bool dropIntoFolder = false;
                    ToolStripItem hoveredFolder = null;
                    int dropY = -1;

                    List<ToolStripItem> validItems = new List<ToolStripItem>();
                    for (int i = 0; i < dd.Items.Count; i++)
                    {
                        ToolStripItem it = dd.Items[i];
                        if (it != null && it.Visible && it.Tag is BookmarkItem)
                        {
                            validItems.Add(it);
                        }
                    }

                    int edgeMargin = Math.Max(5, (int)(6 * _dpiScale));

                    if (validItems.Count == 0)
                    {
                        dropY = 8;
                        targetItem = null;
                        insertAfter = false;
                    }
                    else if (clientPt.Y < validItems[0].Bounds.Top)
                    {
                        targetItem = validItems[0];
                        insertAfter = false;
                        dropY = Math.Max(3, validItems[0].Bounds.Top);
                    }
                    else if (clientPt.Y >= validItems[validItems.Count - 1].Bounds.Bottom)
                    {
                        targetItem = validItems[validItems.Count - 1];
                        insertAfter = true;
                        dropY = Math.Min(dd.ClientSize.Height - 4, validItems[validItems.Count - 1].Bounds.Bottom);
                    }
                    else
                    {
                        for (int i = 0; i < validItems.Count; i++)
                        {
                            ToolStripItem it = validItems[i];
                            if (clientPt.Y >= it.Bounds.Top && clientPt.Y < it.Bounds.Bottom)
                            {
                                BookmarkItem bm = it.Tag as BookmarkItem;
                                bool canDropInto = false;
                                if (bm != null && bm.IsFolder)
                                {
                                    if (string.IsNullOrEmpty(draggedBmId))
                                    {
                                        canDropInto = true;
                                    }
                                    else if (!string.Equals(bm.Id, draggedBmId, StringComparison.OrdinalIgnoreCase) &&
                                             !AppServices.Bookmarks.IsDescendantOf(bm.Id, draggedBmId))
                                    {
                                        canDropInto = true;
                                    }
                                }

                                if (canDropInto)
                                {
                                    if (clientPt.Y >= it.Bounds.Top + edgeMargin && clientPt.Y <= it.Bounds.Bottom - edgeMargin)
                                    {
                                        dropIntoFolder = true;
                                        hoveredFolder = it;
                                        targetItem = it;
                                        dropY = -1;
                                    }
                                    else if (clientPt.Y < it.Bounds.Top + edgeMargin)
                                    {
                                        targetItem = it;
                                        insertAfter = false;
                                        dropY = it.Bounds.Top;
                                    }
                                    else
                                    {
                                        targetItem = it;
                                        insertAfter = true;
                                        dropY = it.Bounds.Bottom;
                                    }
                                }
                                else
                                {
                                    int mid = it.Bounds.Top + it.Bounds.Height / 2;
                                    if (clientPt.Y < mid)
                                    {
                                        targetItem = it;
                                        insertAfter = false;
                                        dropY = it.Bounds.Top;
                                    }
                                    else
                                    {
                                        targetItem = it;
                                        insertAfter = true;
                                        dropY = it.Bounds.Bottom;
                                    }
                                }
                                break;
                            }
                        }
                    }

                    // Hover timer to open subfolder dropdown on sustained hover
                    if (dropIntoFolder && hoveredFolder is ToolStripMenuItem)
                    {
                        if (_menuHoveredFolderItem != hoveredFolder)
                        {
                            StartMenuFolderHoverTimer((ToolStripMenuItem)hoveredFolder);
                        }
                    }
                    else
                    {
                        StopMenuFolderHoverTimer();
                    }

                    if (_menuDropLineY != dropY ||
                        _menuDropTargetItem != targetItem ||
                        _menuDropInsertAfter != insertAfter ||
                        _menuDropIntoFolder != dropIntoFolder ||
                        _menuHoveredFolderItem != hoveredFolder)
                    {
                        _menuDropLineY = dropY;
                        _menuDropTargetItem = targetItem;
                        _menuDropInsertAfter = insertAfter;
                        _menuDropIntoFolder = dropIntoFolder;
                        _menuHoveredFolderItem = hoveredFolder;
                        dd.Invalidate();
                    }
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            };

            dd.DragLeave += (s, e) =>
            {
                StopMenuFolderHoverTimer();
                _menuDropLineY = -1;
                _menuDropTargetItem = null;
                _menuDropIntoFolder = false;
                _menuHoveredFolderItem = null;
                dd.Invalidate();
            };

            dd.DragDrop += (s, e) =>
            {
                StopMenuFolderHoverTimer();

                _menuDropLineY = -1;
                ToolStripItem chosenTarget = _menuDropTargetItem;
                bool chosenInsertAfter = _menuDropInsertAfter;
                bool chosenDropIntoFolder = _menuDropIntoFolder;
                ToolStripItem chosenHoveredFolder = _menuHoveredFolderItem;

                _menuDropTargetItem = null;
                _menuDropInsertAfter = false;
                _menuDropIntoFolder = false;
                _menuHoveredFolderItem = null;
                dd.Invalidate();

                string folderId = (dd.Tag as string) ?? "bar";
                string bookmarkId = e.Data.GetData("wb_bookmark_id") as string;
                if (!string.IsNullOrEmpty(bookmarkId))
                {
                    if (chosenDropIntoFolder && chosenHoveredFolder != null && chosenHoveredFolder.Tag is BookmarkItem)
                    {
                        BookmarkItem destFolder = (BookmarkItem)chosenHoveredFolder.Tag;
                        if (destFolder.IsFolder && !string.Equals(bookmarkId, destFolder.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            AppServices.Bookmarks.MoveItem(bookmarkId, -1, destFolder.Id);
                            ToolStripMenuItem folderMenuItem = chosenHoveredFolder as ToolStripMenuItem;
                            if (folderMenuItem != null && folderMenuItem.DropDown != null && folderMenuItem.DropDown.Visible)
                            {
                                try { folderMenuItem.DropDown.Close(); } catch { }
                            }
                        }
                    }
                    else
                    {
                        BookmarkItem targetBookmark = (chosenTarget != null) ? (chosenTarget.Tag as BookmarkItem) : null;
                        if (targetBookmark != null)
                        {
                            if (!string.Equals(bookmarkId, targetBookmark.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                AppServices.Bookmarks.MoveItemRelative(bookmarkId, targetBookmark.Id, chosenInsertAfter, folderId);
                            }
                        }
                        else
                        {
                            AppServices.Bookmarks.MoveItemRelative(bookmarkId, null, false, folderId);
                        }
                        CloseChildSubMenus(dd);
                    }
                    RefreshFolderDropDown(dd, folderId);
                }
                else if (WebDragDropHelper.HasUrlData(e.Data))
                {
                    string rawUrl = WebDragDropHelper.ExtractUrlFromDataObject(e.Data);
                    if (!string.IsNullOrEmpty(rawUrl) && InternalPageHelper.CanBookmarkPage(rawUrl))
                    {
                        string title = rawUrl;
                        try
                        {
                            Uri uri = new Uri(rawUrl);
                            title = uri.Host;
                        }
                        catch { }

                        if (chosenDropIntoFolder && chosenHoveredFolder != null && chosenHoveredFolder.Tag is BookmarkItem)
                        {
                            BookmarkItem destFolder = (BookmarkItem)chosenHoveredFolder.Tag;
                            if (destFolder.IsFolder)
                            {
                                AppServices.Bookmarks.Add(title, rawUrl, destFolder.Id);
                                ToolStripMenuItem folderMenuItem = chosenHoveredFolder as ToolStripMenuItem;
                                if (folderMenuItem != null && folderMenuItem.DropDown != null && folderMenuItem.DropDown.Visible)
                                {
                                    try { folderMenuItem.DropDown.Close(); } catch { }
                                }
                            }
                        }
                        else
                        {
                            BookmarkItem newBm = AppServices.Bookmarks.Add(title, rawUrl, folderId);
                            if (newBm != null && chosenTarget != null && chosenTarget.Tag is BookmarkItem)
                            {
                                BookmarkItem targetBookmark = (BookmarkItem)chosenTarget.Tag;
                                AppServices.Bookmarks.MoveItemRelative(newBm.Id, targetBookmark.Id, chosenInsertAfter, folderId);
                            }
                            CloseChildSubMenus(dd);
                        }
                        RefreshFolderDropDown(dd, folderId);
                    }
                }
            };
        }

        private void RefreshFolderDropDown(ToolStripDropDown dd, string folderId)
        {
            if (dd == null || dd.IsDisposed) return;
            bool prevAuto = dd.AutoClose;
            dd.AutoClose = false;
            try
            {
                dd.SuspendLayout();
                dd.Items.Clear();

                List<BookmarkItem> children;
                if (string.Equals(folderId, "other", StringComparison.OrdinalIgnoreCase))
                {
                    children = AppServices.Bookmarks.GetOtherBookmarks();
                }
                else
                {
                    children = AppServices.Bookmarks.GetBarItems(folderId);
                }

                BuildFolderMenuItemsInternal(dd.Items, children, folderId, dd);
                dd.ResumeLayout(true);
                dd.Invalidate();
            }
            catch { }
            finally
            {
                dd.AutoClose = prevAuto;
            }
        }

        private void ShowFolderDropdown(BookmarkItem folder, Point screenPt)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(menu);
            menu.Tag = folder.Id;
            menu.ShowItemToolTips = true;
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;
            ConfigureDropDownConstraints(menu, screenPt);
            RegisterFolderDropDown(menu);

            List<BookmarkItem> children = AppServices.Bookmarks.GetBarItems(folder.Id);
            BuildFolderMenuItemsInternal(menu.Items, children, folder.Id, menu);

            menu.Show(screenPt);
        }

        private void ShowOtherBookmarksDropdown(Point screenPt)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(menu);
            menu.Tag = "other";
            menu.ShowItemToolTips = true;
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;
            ConfigureDropDownConstraints(menu, screenPt);
            RegisterFolderDropDown(menu);

            List<BookmarkItem> children = AppServices.Bookmarks.GetOtherBookmarks();
            BuildFolderMenuItemsInternal(menu.Items, children, "other", menu);

            menu.Show(screenPt);
        }

        private void ShowOverflowDropdown(Point screenPt)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(menu);
            menu.Tag = "bar";
            menu.ShowItemToolTips = true;
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;
            ConfigureDropDownConstraints(menu, screenPt);
            RegisterFolderDropDown(menu);

            BuildFolderMenuItemsInternal(menu.Items, _overflowItems, "bar", menu);

            menu.Show(screenPt);
        }

        private void CloseAllParentDropDowns(ToolStripItem item)
        {
            if (item == null)
            {
                if (_activeDragDropDown != null && !_activeDragDropDown.IsDisposed)
                {
                    try { _activeDragDropDown.Close(); } catch { }
                }
                return;
            }
            try
            {
                ToolStripDropDownItem dropDownItem = item as ToolStripDropDownItem;
                if (dropDownItem != null && dropDownItem.HasDropDownItems && dropDownItem.DropDown.Visible)
                {
                    dropDownItem.DropDown.Close();
                }

                ToolStripDropDown dd = item.Owner as ToolStripDropDown;
                while (dd != null)
                {
                    dd.Close();
                    if (dd.OwnerItem != null)
                    {
                        dd = dd.OwnerItem.Owner as ToolStripDropDown;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch { }
        }

        private void BuildFolderMenuItems(ToolStripItemCollection menuItems, List<BookmarkItem> items)
        {
            BuildFolderMenuItemsInternal(menuItems, items, "bar", null);
        }

        private void BuildFolderMenuItemsInternal(ToolStripItemCollection menuItems, List<BookmarkItem> items, string folderId, ToolStripDropDown ownerDropDown)
        {
            if (items == null || items.Count == 0)
            {
                ToolStripMenuItem emptyItem = new ToolStripMenuItem("(Empty)");
                emptyItem.Enabled = false;
                menuItems.Add(emptyItem);
                return;
            }

            bool lazyLoadSubmenus = (ownerDropDown != null);
            Image folderIcon = MaterialIcons.GetIcon("folder", 16, ThemeManager.AccentColor);
            Image defaultGlobe = MaterialIcons.GetIcon("globe", 16, ThemeManager.AccentColor);
            List<ToolStripItem> builtItems = new List<ToolStripItem>(items.Count);

            for (int i = 0; i < items.Count; i++)
            {
                BookmarkItem item = items[i];
                if (item == null) continue;

                if (item.IsFolder)
                {
                    string folderTitle = item.Title ?? string.Empty;
                    string displayFolderTitle = folderTitle.Length > 50 ? folderTitle.Substring(0, 47) + "..." : folderTitle;
                    ToolStripMenuItem folderMenu = new ToolStripMenuItem(displayFolderTitle);
                    UITheme.ApplyMenuItem(folderMenu);
                    folderMenu.Tag = item;
                    folderMenu.Image = folderIcon;
                    List<BookmarkItem> subChildren = AppServices.Bookmarks.GetBarItems(item.Id);
                    int childCount = (subChildren != null) ? subChildren.Count : 0;

                    if (folderMenu.DropDown != null)
                    {
                        folderMenu.DropDown.Tag = item.Id;
                        RegisterFolderDropDown(folderMenu.DropDown);
                        folderMenu.DropDown.ShowItemToolTips = true;
                        ToolStripDropDownMenu dropDownMenu = folderMenu.DropDown as ToolStripDropDownMenu;
                        if (dropDownMenu != null)
                        {
                            dropDownMenu.ShowImageMargin = true;
                            dropDownMenu.ShowCheckMargin = false;
                        }
                    }

                    if (lazyLoadSubmenus)
                    {
                        // Lazy load child items on DropDownOpening to eliminate sluggish loading on deep/large bookmark trees
                        ToolStripMenuItem dummyPlaceholder = new ToolStripMenuItem("(Loading...)");
                        dummyPlaceholder.Enabled = false;
                        folderMenu.DropDownItems.Add(dummyPlaceholder);

                        bool isSubmenuPopulated = false;
                        Action populateAction = () =>
                        {
                            if (!isSubmenuPopulated)
                            {
                                isSubmenuPopulated = true;
                                if (folderMenu.DropDown != null)
                                {
                                    folderMenu.DropDown.SuspendLayout();
                                    Point menuScreen = (folderMenu.Owner != null) ?
                                        folderMenu.Owner.PointToScreen(folderMenu.Bounds.Location) :
                                        Cursor.Position;
                                    ConfigureDropDownConstraints(folderMenu.DropDown, menuScreen);
                                }
                                folderMenu.DropDownItems.Clear();
                                List<BookmarkItem> freshChildren = AppServices.Bookmarks.GetBarItems(item.Id);
                                BuildFolderMenuItemsInternal(folderMenu.DropDownItems, freshChildren, item.Id, folderMenu.DropDown);
                                if (folderMenu.DropDown != null)
                                {
                                    Point menuScreen = (folderMenu.Owner != null) ?
                                        folderMenu.Owner.PointToScreen(folderMenu.Bounds.Location) :
                                        Cursor.Position;
                                    ConfigureDropDownConstraints(folderMenu.DropDown, menuScreen);
                                    folderMenu.DropDown.ResumeLayout(true);
                                }
                            }
                        };

                        folderMenu.DropDownOpening += (s, e) => populateAction();
                        if (folderMenu.DropDown != null)
                        {
                            folderMenu.DropDown.Opening += (s, e) => populateAction();
                        }
                    }
                    else
                    {
                        BuildFolderMenuItemsInternal(folderMenu.DropDownItems, subChildren, item.Id, null);
                    }

                    // Drag & Context Menu support for folders in menus
                    Point folderMouseDownPt = Point.Empty;
                    bool folderIsMouseDown = false;
                    bool folderHasDragged = false;

                    folderMenu.MouseDown += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Left)
                        {
                            folderIsMouseDown = true;
                            folderMouseDownPt = Cursor.Position;
                            folderHasDragged = false;
                        }
                        else if (e.Button == MouseButtons.Middle)
                        {
                            OpenAllBookmarksInFolder(item.Id, true);
                        }
                        else if (e.Button == MouseButtons.Right)
                        {
                            Point clickScreen = (folderMenu.Owner != null) ?
                                folderMenu.Owner.PointToScreen(new Point(folderMenu.Bounds.Left + Math.Max(0, e.X), folderMenu.Bounds.Top + Math.Max(0, e.Y))) :
                                Cursor.Position;
                            ShowChildFolderContextMenu(item, folderMenu, clickScreen);
                        }
                    };

                    folderMenu.MouseUp += (s, e) =>
                    {
                        folderIsMouseDown = false;
                        folderMouseDownPt = Point.Empty;
                    };

                    folderMenu.MouseMove += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Left && folderIsMouseDown && folderMouseDownPt != Point.Empty)
                        {
                            Point cur = Cursor.Position;
                            int dx = Math.Abs(cur.X - folderMouseDownPt.X);
                            int dy = Math.Abs(cur.Y - folderMouseDownPt.Y);
                            if (dx > 4 || dy > 4)
                            {
                                folderIsMouseDown = false;
                                folderMouseDownPt = Point.Empty;
                                folderHasDragged = true;

                                if (folderMenu.DropDown != null && folderMenu.DropDown.Visible)
                                {
                                    folderMenu.DropDown.Close();
                                }

                                StartMenuDrag(folderMenu, item, folderId, ownerDropDown);
                            }
                        }
                    };

                    folderMenu.Click += (s, e) =>
                    {
                        if (folderHasDragged || (Environment.TickCount - _lastMenuDragEndTime < 300))
                        {
                            folderHasDragged = false;
                            return;
                        }
                    };

                    builtItems.Add(folderMenu);
                }
                else
                {
                    string itemTitle = item.Title ?? string.Empty;
                    string displayItemTitle = itemTitle.Length > 50 ? itemTitle.Substring(0, 47) + "..." : itemTitle;
                    ToolStripMenuItem itemMenu = new ToolStripMenuItem(displayItemTitle);
                    UITheme.ApplyMenuItem(itemMenu);
                    itemMenu.Tag = item;

                    // Fast in-memory lookup with persistent disk cache fallback
                    Image icon = AppServices.Favicons.GetFavicon(item.Url);
                    itemMenu.Image = icon ?? defaultGlobe;
                    if (icon == null)
                    {
                        TriggerBookmarkFaviconFetch(item.Url);
                    }

                    itemMenu.ToolTipText = (!string.IsNullOrEmpty(itemTitle) ? itemTitle + "\n" : string.Empty) + item.Url;

                    string targetUrl = item.Url;
                    MouseButtons lastButton = MouseButtons.None;
                    Point itemMouseDownPt = Point.Empty;
                    bool itemIsMouseDown = false;
                    bool itemHasDragged = false;

                    itemMenu.MouseDown += (s, e) =>
                    {
                        lastButton = e.Button;
                        if (e.Button == MouseButtons.Left)
                        {
                            itemIsMouseDown = true;
                            itemMouseDownPt = Cursor.Position;
                            itemHasDragged = false;
                        }
                        else if (e.Button == MouseButtons.Middle)
                        {
                            CloseAllParentDropDowns(itemMenu);
                            if (NavigateCallback != null) NavigateCallback(targetUrl, true);
                        }
                        else if (e.Button == MouseButtons.Right)
                        {
                            Point clickScreen = (itemMenu.Owner != null) ?
                                itemMenu.Owner.PointToScreen(new Point(itemMenu.Bounds.Left + Math.Max(0, e.X), itemMenu.Bounds.Top + Math.Max(0, e.Y))) :
                                Cursor.Position;
                            ShowChildBookmarkContextMenu(item, itemMenu, clickScreen);
                        }
                    };

                    itemMenu.MouseUp += (s, e) =>
                    {
                        itemIsMouseDown = false;
                        itemMouseDownPt = Point.Empty;
                    };

                    itemMenu.MouseMove += (s, e) =>
                    {
                        if (e.Button == MouseButtons.Left && itemIsMouseDown && itemMouseDownPt != Point.Empty)
                        {
                            Point cur = Cursor.Position;
                            int dx = Math.Abs(cur.X - itemMouseDownPt.X);
                            int dy = Math.Abs(cur.Y - itemMouseDownPt.Y);
                            if (dx > 4 || dy > 4)
                            {
                                itemIsMouseDown = false;
                                itemMouseDownPt = Point.Empty;
                                itemHasDragged = true;

                                StartMenuDrag(itemMenu, item, folderId, ownerDropDown);
                            }
                        }
                    };

                    itemMenu.Click += (s, e) =>
                    {
                        if (itemHasDragged || (Environment.TickCount - _lastMenuDragEndTime < 300))
                        {
                            itemHasDragged = false;
                            return;
                        }

                        if (!_isChildContextMenuActive && lastButton != MouseButtons.Right && lastButton != MouseButtons.Middle)
                        {
                            CloseAllParentDropDowns(itemMenu);
                            if (NavigateCallback != null) NavigateCallback(targetUrl, false);
                        }
                    };

                    builtItems.Add(itemMenu);
                }
            }

            if (ownerDropDown != null)
            {
                ownerDropDown.SuspendLayout();
            }
            menuItems.AddRange(builtItems.ToArray());
            if (ownerDropDown != null)
            {
                ownerDropDown.ResumeLayout(false);
            }
        }

        private void StartMenuDrag(ToolStripMenuItem menuItem, BookmarkItem item, string folderId, ToolStripDropDown ownerDropDown)
        {
            if (menuItem == null || item == null) return;

            _isDraggingInMenu = true;
            _activeDragDropDown = ownerDropDown;
            _activeDragFolderId = folderId;

            _activeDragDropDownChain.Clear();
            _prevAutoCloseMap.Clear();

            // Walk up dropdown chain to prevent any parent menu from auto-closing while dragging
            ToolStripDropDown current = ownerDropDown;
            while (current != null)
            {
                _activeDragDropDownChain.Add(current);
                _prevAutoCloseMap[current] = current.AutoClose;
                current.AutoClose = false;
                if (current.OwnerItem != null)
                {
                    current = current.OwnerItem.Owner as ToolStripDropDown;
                }
                else
                {
                    break;
                }
            }

            try
            {
                DataObject dragData = new DataObject();
                dragData.SetData("wb_bookmark_id", item.Id);
                dragData.SetData("wb_source_parent_id", item.ParentId ?? folderId ?? "bar");
                dragData.SetData(DataFormats.Text, item.Url ?? item.Title);

                Control dragSource = (ownerDropDown != null && ownerDropDown.IsHandleCreated) ? (Control)ownerDropDown : (Control)this;
                dragSource.DoDragDrop(dragData, DragDropEffects.Move | DragDropEffects.Copy);
            }
            catch { }
            finally
            {
                _lastMenuDragEndTime = Environment.TickCount;
                _isDraggingInMenu = false;
                _menuDropLineY = -1;
                _menuDropTargetItem = null;
                _menuDropIntoFolder = false;
                _menuHoveredFolderItem = null;

                StopMenuFolderHoverTimer();

                // Restore AutoClose for all dropdowns in chain
                foreach (KeyValuePair<ToolStripDropDown, bool> kvp in _prevAutoCloseMap)
                {
                    if (kvp.Key != null && !kvp.Key.IsDisposed)
                    {
                        try { kvp.Key.AutoClose = kvp.Value; } catch { }
                    }
                }
                _prevAutoCloseMap.Clear();
                _activeDragDropDownChain.Clear();

                _activeDragDropDown = null;
                _activeDragFolderId = null;

                if (ownerDropDown != null && !ownerDropDown.IsDisposed)
                {
                    ownerDropDown.Invalidate();
                }
            }
        }

        private void RenderMenuDropIndicator(Graphics g, ToolStripDropDown dd)
        {
            if (!_isDraggingInMenu || dd == null || _activeDragDropDown != dd)
            {
                return;
            }

            SmoothingMode prevMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1. Folder Drop Target Highlight (when hovering directly over a folder item to drop into it)
            if (_menuDropIntoFolder && _menuHoveredFolderItem != null && _menuHoveredFolderItem.Owner == dd)
            {
                Rectangle bounds = _menuHoveredFolderItem.Bounds;
                int padX = DpiHelper.Scale(4, _dpiScale);
                Rectangle highlightRect = new Rectangle(
                    padX,
                    bounds.Top + 1,
                    dd.ClientSize.Width - padX * 2,
                    bounds.Height - 2
                );

                Color accent = ThemeManager.AccentColor;
                int radius = DpiHelper.Scale(4, _dpiScale);

                // Semi-transparent background fill
                using (SolidBrush fillBrush = new SolidBrush(Color.FromArgb(48, accent.R, accent.G, accent.B)))
                {
                    UITheme.FillRoundedRectangle(g, fillBrush, highlightRect, radius);
                }

                // Smooth accent border
                float borderWidth = Math.Max(1.5f, (float)Math.Round(1.5f * _dpiScale));
                using (Pen borderPen = new Pen(Color.FromArgb(200, accent.R, accent.G, accent.B), borderWidth))
                {
                    UITheme.DrawRoundedRectangle(g, borderPen, highlightRect, radius);
                }

                g.SmoothingMode = prevMode;
                return;
            }

            // 2. Insertion Line Highlight (when hovering between items)
            if (_menuDropLineY >= 0)
            {
                int lineY = _menuDropLineY;
                if (lineY < 3) lineY = 3;
                if (lineY > dd.ClientSize.Height - 4) lineY = dd.ClientSize.Height - 4;

                int x1 = DpiHelper.Scale(6, _dpiScale);
                int x2 = dd.ClientSize.Width - DpiHelper.Scale(6, _dpiScale);
                float penWidth = Math.Max(2f, (float)Math.Round(2f * _dpiScale));
                int dotDiameter = DpiHelper.Scale(6, _dpiScale);
                int dotRadius = dotDiameter / 2;

                Color accent = ThemeManager.AccentColor;

                // Horizontal insertion cue line across full dropdown row width (under icon + text)
                using (Pen pen = new Pen(accent, penWidth))
                {
                    g.DrawLine(pen, x1, lineY, x2 - dotRadius, lineY);
                }

                // Circular indicator dot at the right edge
                using (SolidBrush brush = new SolidBrush(accent))
                {
                    g.FillEllipse(brush, x2 - dotDiameter, lineY - dotRadius, dotDiameter, dotDiameter);
                }
            }

            g.SmoothingMode = prevMode;
        }

        private class BookmarksMenuRenderer : MaterialToolStripRenderer
        {
            private readonly WbBookmarksBar _bar;

            public BookmarksMenuRenderer(WbBookmarksBar bar)
            {
                _bar = bar;
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                base.OnRenderToolStripBorder(e);
                if (_bar != null)
                {
                    _bar.RenderMenuDropIndicator(e.Graphics, e.ToolStrip as ToolStripDropDown);
                }
            }
        }

        private void ShowChildFolderContextMenu(BookmarkItem folder, ToolStripItem ownerItem, Point screenPt)
        {
            if (_activeChildContextMenu != null && _activeChildContextMenu.Visible)
            {
                _activeChildContextMenu.Close();
            }

            _isChildContextMenuActive = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            _activeChildContextMenu = menu;
            UITheme.ApplyContextMenu(menu);
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;
            menu.Closed += (s, e) =>
            {
                if (_activeChildContextMenu == menu)
                {
                    _activeChildContextMenu = null;
                    _isChildContextMenuActive = false;
                }
            };

            int count = AppServices.Bookmarks.GetAllChildren(folder.Id).Count;
            ToolStripMenuItem openAllItem = new ToolStripMenuItem(string.Format(AppStrings.Bookmarks.OpenAllFormat, count), null, (s, e) =>
            {
                _isChildContextMenuActive = false;
                _activeChildContextMenu = null;
                CloseAllParentDropDowns(ownerItem);
                OpenAllBookmarksInFolder(folder.Id, true);
            });
            openAllItem.Enabled = (count > 0);
            menu.Items.Add(openAllItem);

            menu.Items.Add(new ToolStripSeparator());

            // Rearrange within folder
            List<BookmarkItem> siblings = AppServices.Bookmarks.GetBarItems(folder.ParentId);
            int siblingIdx = siblings.FindIndex(b => b.Id == folder.Id);
            if (siblingIdx >= 0 && siblings.Count > 1)
            {
                ToolStripMenuItem moveUpItem = new ToolStripMenuItem(AppStrings.Bookmarks.MoveUp, null, (s, e) =>
                {
                    _isChildContextMenuActive = false;
                    _activeChildContextMenu = null;
                    AppServices.Bookmarks.MoveItemInFolder(folder.Id, -1);
                    ToolStripDropDown parentDd = ownerItem != null ? (ownerItem.Owner as ToolStripDropDown) : null;
                    if (parentDd != null && !parentDd.IsDisposed)
                    {
                        RefreshFolderDropDown(parentDd, folder.ParentId);
                    }
                    else
                    {
                        CloseAllParentDropDowns(ownerItem);
                    }
                });
                moveUpItem.Enabled = (siblingIdx > 0);
                menu.Items.Add(moveUpItem);

                ToolStripMenuItem moveDownItem = new ToolStripMenuItem(AppStrings.Bookmarks.MoveDown, null, (s, e) =>
                {
                    _isChildContextMenuActive = false;
                    _activeChildContextMenu = null;
                    AppServices.Bookmarks.MoveItemInFolder(folder.Id, 1);
                    ToolStripDropDown parentDd = ownerItem != null ? (ownerItem.Owner as ToolStripDropDown) : null;
                    if (parentDd != null && !parentDd.IsDisposed)
                    {
                        RefreshFolderDropDown(parentDd, folder.ParentId);
                    }
                    else
                    {
                        CloseAllParentDropDowns(ownerItem);
                    }
                });
                moveDownItem.Enabled = (siblingIdx < siblings.Count - 1);
                menu.Items.Add(moveDownItem);

                menu.Items.Add(new ToolStripSeparator());
            }

            // Move to bookmarks bar (move OUT of Other bookmarks or subfolder)
            if (!string.Equals(folder.ParentId, "bar", StringComparison.OrdinalIgnoreCase))
            {
                menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.MoveToBar, null, (s, e) =>
                {
                    _isChildContextMenuActive = false;
                    _activeChildContextMenu = null;
                    CloseAllParentDropDowns(ownerItem);
                    AppServices.Bookmarks.MoveItem(folder.Id, -1, "bar");
                }));
            }

            // Move to folder submenu
            ToolStripMenuItem moveToFolderMenu = new ToolStripMenuItem(AppStrings.Bookmarks.MoveToFolder);
            UITheme.ApplyMenuItem(moveToFolderMenu);

            // Add Other Bookmarks destination if not already in "other"
            if (!string.Equals(folder.ParentId, "other", StringComparison.OrdinalIgnoreCase))
            {
                ToolStripMenuItem otherDest = new ToolStripMenuItem(AppStrings.Bookmarks.OtherBookmarks, null, (s, e) =>
                {
                    _isChildContextMenuActive = false;
                    _activeChildContextMenu = null;
                    CloseAllParentDropDowns(ownerItem);
                    AppServices.Bookmarks.MoveItem(folder.Id, -1, "other");
                });
                UITheme.ApplyMenuItem(otherDest);
                otherDest.Image = MaterialIcons.GetIcon("folder", 16, ThemeManager.AccentColor);
                moveToFolderMenu.DropDownItems.Add(otherDest);
            }

            List<BookmarkItem> folders = AppServices.Bookmarks.GetFolders();
            for (int f = 0; f < folders.Count; f++)
            {
                BookmarkItem targetF = folders[f];
                if (!string.Equals(targetF.Id, folder.Id, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(targetF.Id, folder.ParentId, StringComparison.OrdinalIgnoreCase))
                {
                    ToolStripMenuItem folderDest = new ToolStripMenuItem(targetF.Title, null, (s, e) =>
                    {
                        _isChildContextMenuActive = false;
                        _activeChildContextMenu = null;
                        CloseAllParentDropDowns(ownerItem);
                        AppServices.Bookmarks.MoveItem(folder.Id, -1, targetF.Id);
                    });
                    UITheme.ApplyMenuItem(folderDest);
                    folderDest.Image = MaterialIcons.GetIcon("folder", 16, ThemeManager.AccentColor);
                    moveToFolderMenu.DropDownItems.Add(folderDest);
                }
            }
            if (moveToFolderMenu.DropDownItems.Count > 0)
            {
                menu.Items.Add(moveToFolderMenu);
            }

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnEdit, null, (s, e) =>
            {
                _isChildContextMenuActive = false;
                _activeChildContextMenu = null;
                CloseAllParentDropDowns(ownerItem);
                EditFolder(folder);
            }));

            menu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnDelete, null, (s, e) =>
            {
                int childCount = AppServices.Bookmarks.GetAllChildren(folder.Id).Count;
                string msg = childCount > 0
                    ? string.Format(AppStrings.Bookmarks.ConfirmDeleteFolderWithChildrenFormat, folder.Title, childCount)
                    : string.Format(AppStrings.Bookmarks.ConfirmDeleteFolderFormat, folder.Title);
                if (PromptDeleteConfirmation(msg, AppStrings.Bookmarks.DeleteFolderTitle))
                {
                    _isChildContextMenuActive = false;
                    _activeChildContextMenu = null;
                    CloseAllParentDropDowns(ownerItem);
                    AppServices.Bookmarks.Remove(folder.Id);
                }
            }));

            if (screenPt.IsEmpty || (screenPt.X == 0 && screenPt.Y == 0))
            {
                if (ownerItem != null && ownerItem.Owner != null)
                {
                    screenPt = ownerItem.Owner.PointToScreen(new Point(ownerItem.Bounds.Left, ownerItem.Bounds.Bottom));
                }
                else
                {
                    screenPt = Cursor.Position;
                }
            }
            menu.Show(screenPt);
            if (ownerItem != null) menu.OwnerItem = ownerItem;
        }

        private void ShowChildBookmarkContextMenu(BookmarkItem item, ToolStripItem ownerItem, Point screenPt)
        {
            if (_activeChildContextMenu != null && _activeChildContextMenu.Visible)
            {
                _activeChildContextMenu.Close();
            }

            _isChildContextMenuActive = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            _activeChildContextMenu = menu;
            UITheme.ApplyContextMenu(menu);
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;
            menu.Closed += (s, e) =>
            {
                if (_activeChildContextMenu == menu)
                {
                    _activeChildContextMenu = null;
                    _isChildContextMenuActive = false;
                }
            };

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnOpenCurrent, null, (s, e) =>
            {
                _isChildContextMenuActive = false;
                _activeChildContextMenu = null;
                CloseAllParentDropDowns(ownerItem);
                if (NavigateCallback != null) NavigateCallback(item.Url, false);
            }));

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnOpenNewTab, null, (s, e) =>
            {
                _isChildContextMenuActive = false;
                _activeChildContextMenu = null;
                CloseAllParentDropDowns(ownerItem);
                if (NavigateCallback != null) NavigateCallback(item.Url, true);
            }));

            menu.Items.Add(new ToolStripSeparator());

            // Rearrange within folder
            List<BookmarkItem> siblings = AppServices.Bookmarks.GetBarItems(item.ParentId);
            int siblingIdx = siblings.FindIndex(b => b.Id == item.Id);
            if (siblingIdx >= 0 && siblings.Count > 1)
            {
                ToolStripMenuItem moveUpItem = new ToolStripMenuItem(AppStrings.Bookmarks.MoveUp, null, (s, e) =>
                {
                    _isChildContextMenuActive = false;
                    _activeChildContextMenu = null;
                    AppServices.Bookmarks.MoveItemInFolder(item.Id, -1);
                    ToolStripDropDown parentDd = ownerItem != null ? (ownerItem.Owner as ToolStripDropDown) : null;
                    if (parentDd != null && !parentDd.IsDisposed)
                    {
                        RefreshFolderDropDown(parentDd, item.ParentId);
                    }
                    else
                    {
                        CloseAllParentDropDowns(ownerItem);
                    }
                });
                moveUpItem.Enabled = (siblingIdx > 0);
                menu.Items.Add(moveUpItem);

                ToolStripMenuItem moveDownItem = new ToolStripMenuItem(AppStrings.Bookmarks.MoveDown, null, (s, e) =>
                {
                    _isChildContextMenuActive = false;
                    _activeChildContextMenu = null;
                    AppServices.Bookmarks.MoveItemInFolder(item.Id, 1);
                    ToolStripDropDown parentDd = ownerItem != null ? (ownerItem.Owner as ToolStripDropDown) : null;
                    if (parentDd != null && !parentDd.IsDisposed)
                    {
                        RefreshFolderDropDown(parentDd, item.ParentId);
                    }
                    else
                    {
                        CloseAllParentDropDowns(ownerItem);
                    }
                });
                moveDownItem.Enabled = (siblingIdx < siblings.Count - 1);
                menu.Items.Add(moveDownItem);

                menu.Items.Add(new ToolStripSeparator());
            }

            // Move to bookmarks bar (move OUT of folder)
            if (!string.Equals(item.ParentId, "bar", StringComparison.OrdinalIgnoreCase))
            {
                menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.MoveToBar, null, (s, e) =>
                {
                    _isChildContextMenuActive = false;
                    _activeChildContextMenu = null;
                    CloseAllParentDropDowns(ownerItem);
                    AppServices.Bookmarks.MoveItem(item.Id, -1, "bar");
                }));
            }

            // Move to folder submenu
            ToolStripMenuItem moveToFolderMenu = new ToolStripMenuItem(AppStrings.Bookmarks.MoveToFolder);
            UITheme.ApplyMenuItem(moveToFolderMenu);

            if (!string.Equals(item.ParentId, "other", StringComparison.OrdinalIgnoreCase))
            {
                ToolStripMenuItem otherDest = new ToolStripMenuItem(AppStrings.Bookmarks.OtherBookmarks, null, (s, e) =>
                {
                    _isChildContextMenuActive = false;
                    _activeChildContextMenu = null;
                    CloseAllParentDropDowns(ownerItem);
                    AppServices.Bookmarks.MoveItem(item.Id, -1, "other");
                });
                UITheme.ApplyMenuItem(otherDest);
                otherDest.Image = MaterialIcons.GetIcon("folder", 16, ThemeManager.AccentColor);
                moveToFolderMenu.DropDownItems.Add(otherDest);
            }

            List<BookmarkItem> folders = AppServices.Bookmarks.GetFolders();
            for (int f = 0; f < folders.Count; f++)
            {
                BookmarkItem targetF = folders[f];
                if (!string.Equals(targetF.Id, item.ParentId, StringComparison.OrdinalIgnoreCase))
                {
                    ToolStripMenuItem folderDest = new ToolStripMenuItem(targetF.Title, null, (s, e) =>
                    {
                        _isChildContextMenuActive = false;
                        _activeChildContextMenu = null;
                        CloseAllParentDropDowns(ownerItem);
                        AppServices.Bookmarks.MoveItem(item.Id, -1, targetF.Id);
                    });
                    folderDest.Image = MaterialIcons.GetIcon("folder", 16, ThemeManager.AccentColor);
                    moveToFolderMenu.DropDownItems.Add(folderDest);
                }
            }
            if (moveToFolderMenu.DropDownItems.Count > 0)
            {
                menu.Items.Add(moveToFolderMenu);
            }

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnEdit, null, (s, e) =>
            {
                _isChildContextMenuActive = false;
                _activeChildContextMenu = null;
                CloseAllParentDropDowns(ownerItem);
                EditBookmark(item);
            }));

            menu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnDelete, null, (s, e) =>
            {
                string msg = string.Format(AppStrings.Bookmarks.ConfirmDeleteBookmarkFormat, string.IsNullOrEmpty(item.Title) ? item.Url : item.Title);
                if (PromptDeleteConfirmation(msg, AppStrings.Bookmarks.DeleteBookmarkTitle))
                {
                    _isChildContextMenuActive = false;
                    _activeChildContextMenu = null;
                    CloseAllParentDropDowns(ownerItem);
                    AppServices.Bookmarks.Remove(item.Id);
                }
            }));

            if (screenPt.IsEmpty || (screenPt.X == 0 && screenPt.Y == 0))
            {
                if (ownerItem != null && ownerItem.Owner != null)
                {
                    screenPt = ownerItem.Owner.PointToScreen(new Point(ownerItem.Bounds.Left, ownerItem.Bounds.Bottom));
                }
                else
                {
                    screenPt = Cursor.Position;
                }
            }
            menu.Show(screenPt);
            if (ownerItem != null) menu.OwnerItem = ownerItem;
        }

        private void ShowBookmarkContextMenu(BookmarkItem item, Point screenPt)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(menu);
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnOpenCurrent, null, (s, e) =>
            {
                if (NavigateCallback != null) NavigateCallback(item.Url, false);
            }));

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnOpenNewTab, null, (s, e) =>
            {
                if (NavigateCallback != null) NavigateCallback(item.Url, true);
            }));

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.OpenAllNewWindow, null, (s, e) =>
            {
                if (NewWindowCallback != null) NewWindowCallback(item.Url);
            }));

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.OpenAllIncognito, null, (s, e) =>
            {
                if (IncognitoCallback != null) IncognitoCallback(item.Url);
            }));

            menu.Items.Add(new ToolStripSeparator());

            // Move to folder submenu
            ToolStripMenuItem moveToFolderMenu = new ToolStripMenuItem(AppStrings.Bookmarks.MoveToFolder);
            UITheme.ApplyMenuItem(moveToFolderMenu);

            if (!string.Equals(item.ParentId, "other", StringComparison.OrdinalIgnoreCase))
            {
                ToolStripMenuItem otherDest = new ToolStripMenuItem(AppStrings.Bookmarks.OtherBookmarks, null, (s, e) =>
                {
                    AppServices.Bookmarks.MoveItem(item.Id, -1, "other");
                });
                UITheme.ApplyMenuItem(otherDest);
                otherDest.Image = MaterialIcons.GetIcon("folder", 16, ThemeManager.AccentColor);
                moveToFolderMenu.DropDownItems.Add(otherDest);
            }

            List<BookmarkItem> folders = AppServices.Bookmarks.GetFolders();
            for (int f = 0; f < folders.Count; f++)
            {
                BookmarkItem targetF = folders[f];
                if (!string.Equals(targetF.Id, item.ParentId, StringComparison.OrdinalIgnoreCase))
                {
                    ToolStripMenuItem folderDest = new ToolStripMenuItem(targetF.Title, null, (s, e) =>
                    {
                        AppServices.Bookmarks.MoveItem(item.Id, -1, targetF.Id);
                    });
                    folderDest.Image = MaterialIcons.GetIcon("folder", 16, ThemeManager.AccentColor);
                    moveToFolderMenu.DropDownItems.Add(folderDest);
                }
            }
            if (moveToFolderMenu.DropDownItems.Count > 0)
            {
                menu.Items.Add(moveToFolderMenu);
                menu.Items.Add(new ToolStripSeparator());
            }

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnEdit, null, (s, e) =>
            {
                EditBookmark(item);
            }));

            menu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnDelete, null, (s, e) =>
            {
                string msg = string.Format(AppStrings.Bookmarks.ConfirmDeleteBookmarkFormat, string.IsNullOrEmpty(item.Title) ? item.Url : item.Title);
                if (PromptDeleteConfirmation(msg, AppStrings.Bookmarks.DeleteBookmarkTitle))
                {
                    AppServices.Bookmarks.Remove(item.Id);
                }
            }));

            menu.Items.Add(new ToolStripSeparator());

            AppendStandardBarMenuItems(menu);

            menu.Show(screenPt);
        }

        private void ShowFolderContextMenu(BookmarkItem folder, Point screenPt)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(menu);
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;

            int count = AppServices.Bookmarks.GetAllChildren(folder.Id).Count;
            ToolStripMenuItem openAllItem = new ToolStripMenuItem(string.Format(AppStrings.Bookmarks.OpenAllFormat, count), null, (s, e) =>
            {
                OpenAllBookmarksInFolder(folder.Id, true);
            });
            openAllItem.Enabled = (count > 0);
            menu.Items.Add(openAllItem);

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.BtnEdit, null, (s, e) =>
            {
                EditFolder(folder);
            }));

            menu.Items.Add(new ToolStripMenuItem(AppStrings.General.BtnDelete, null, (s, e) =>
            {
                int childCount = AppServices.Bookmarks.GetAllChildren(folder.Id).Count;
                string msg = childCount > 0
                    ? string.Format(AppStrings.Bookmarks.ConfirmDeleteFolderWithChildrenFormat, folder.Title, childCount)
                    : string.Format(AppStrings.Bookmarks.ConfirmDeleteFolderFormat, folder.Title);
                if (PromptDeleteConfirmation(msg, AppStrings.Bookmarks.DeleteFolderTitle))
                {
                    AppServices.Bookmarks.Remove(folder.Id);
                }
            }));

            menu.Items.Add(new ToolStripSeparator());

            AppendStandardBarMenuItems(menu);

            menu.Show(screenPt);
        }

        private void ShowOtherBookmarksContextMenu(Point screenPt)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(menu);
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;

            int count = AppServices.Bookmarks.GetAllChildren("other").Count;
            ToolStripMenuItem openAllItem = new ToolStripMenuItem(string.Format(AppStrings.Bookmarks.OpenAllFormat, count), null, (s, e) =>
            {
                OpenAllBookmarksInFolder("other", true);
            });
            openAllItem.Enabled = (count > 0);
            menu.Items.Add(openAllItem);

            menu.Items.Add(new ToolStripSeparator());

            AppendStandardBarMenuItems(menu);

            menu.Show(screenPt);
        }

        private void ShowEmptyBarContextMenu(Point screenPt)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(menu);
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;

            AppendStandardBarMenuItems(menu);

            menu.Show(screenPt);
        }

        private void AppendStandardBarMenuItems(ContextMenuStrip menu)
        {
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = false;

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.AddPage, null, (s, e) =>
            {
                PromptAddBookmark();
            }));

            menu.Items.Add(new ToolStripMenuItem(AppStrings.Bookmarks.AddFolder, null, (s, e) =>
            {
                PromptAddFolder();
            }));

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem toggleBarItem = new ToolStripMenuItem(AppStrings.Browser.MenuShowBookmarksBar, null, (s, e) =>
            {
                AppServices.Bookmarks.ShowBookmarksBar = !AppServices.Bookmarks.ShowBookmarksBar;
            });
            toggleBarItem.Checked = AppServices.Bookmarks.ShowBookmarksBar;
            toggleBarItem.ShortcutKeyDisplayString = "Ctrl+Shift+B";
            menu.Items.Add(toggleBarItem);

            ToolStripMenuItem viewBookmarksItem = new ToolStripMenuItem(AppStrings.Browser.MenuViewFavorites, null, (s, e) =>
            {
                if (OpenBookmarksManagerCallback != null) OpenBookmarksManagerCallback();
            });
            viewBookmarksItem.ShortcutKeyDisplayString = "Ctrl+B";
            menu.Items.Add(viewBookmarksItem);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem importDataItem = new ToolStripMenuItem(AppStrings.ImportData.MenuImportData, null, (s, e) =>
            {
                ImportDataDialog.ShowImportDialog(this.FindForm());
            });
            menu.Items.Add(importDataItem);

            ToolStripMenuItem exportDataItem = new ToolStripMenuItem(AppStrings.ExportData.MenuExportData, null, (s, e) =>
            {
                ExportDataDialog.ShowExportDialog(this.FindForm());
            });
            menu.Items.Add(exportDataItem);
        }

        private void EditBookmark(BookmarkItem item)
        {
            using (BookmarkEditDialog dlg = new BookmarkEditDialog(
                AppStrings.Bookmarks.EditTitle,
                item.Title,
                item.Url,
                showBookmarkCheckbox: false,
                isBookmarked: true,
                primaryButtonText: AppStrings.General.BtnSave,
                initialFolderId: item.ParentId,
                showRemoveButton: true))
            {
                if (dlg.ShowDialog(this.FindForm()) == DialogResult.OK)
                {
                    if (dlg.IsDeleted)
                    {
                        AppServices.Bookmarks.Remove(item.Id);
                    }
                    else
                    {
                        AppServices.Bookmarks.Update(item.Id, dlg.ItemTitle, dlg.ItemUrl, dlg.SelectedFolderId);
                    }
                }
            }
        }

        private void EditFolder(BookmarkItem folder)
        {
            using (BookmarkEditDialog dlg = new BookmarkEditDialog(
                AppStrings.Bookmarks.EditFolderTitle,
                folder.Title,
                string.Empty,
                showBookmarkCheckbox: false,
                isBookmarked: false,
                primaryButtonText: AppStrings.General.BtnSave,
                initialFolderId: folder.ParentId,
                showRemoveButton: true,
                isFolderOnly: true))
            {
                if (dlg.ShowDialog(this.FindForm()) == DialogResult.OK)
                {
                    if (dlg.IsDeleted)
                    {
                        AppServices.Bookmarks.Remove(folder.Id);
                    }
                    else
                    {
                        AppServices.Bookmarks.Update(folder.Id, dlg.ItemTitle, string.Empty, dlg.SelectedFolderId);
                    }
                }
            }
        }

        private void PromptAddBookmark()
        {
            string[] tabInfo = GetCurrentTabInfoCallback != null ? GetCurrentTabInfoCallback() : null;
            string defaultTitle = (tabInfo != null && tabInfo.Length > 0) ? tabInfo[0] : string.Empty;
            string defaultUrl = (tabInfo != null && tabInfo.Length > 1) ? tabInfo[1] : "https://";

            using (BookmarkEditDialog dlg = new BookmarkEditDialog(
                AppStrings.Bookmarks.AddTitle,
                defaultTitle,
                defaultUrl,
                showBookmarkCheckbox: false,
                isBookmarked: false,
                primaryButtonText: AppStrings.General.BtnAdd,
                initialFolderId: "bar"))
            {
                if (dlg.ShowDialog(this.FindForm()) == DialogResult.OK)
                {
                    AppServices.Bookmarks.Add(dlg.ItemTitle, dlg.ItemUrl, dlg.SelectedFolderId);
                }
            }
        }

        private void PromptAddFolder()
        {
            using (BookmarkEditDialog dlg = new BookmarkEditDialog(
                AppStrings.Bookmarks.AddFolderTitle,
                string.Empty,
                string.Empty,
                showBookmarkCheckbox: false,
                isBookmarked: false,
                primaryButtonText: AppStrings.General.BtnAdd,
                initialFolderId: "bar",
                showRemoveButton: false,
                isFolderOnly: true))
            {
                if (dlg.ShowDialog(this.FindForm()) == DialogResult.OK)
                {
                    AppServices.Bookmarks.AddFolder(dlg.ItemTitle, dlg.SelectedFolderId);
                }
            }
        }

        private void OpenAllBookmarksInFolder(string folderId, bool newTabs)
        {
            List<BookmarkItem> children = AppServices.Bookmarks.GetAllChildren(folderId);
            if (children == null || children.Count == 0) return;

            for (int i = 0; i < children.Count; i++)
            {
                if (!string.IsNullOrEmpty(children[i].Url) && NavigateCallback != null)
                {
                    NavigateCallback(children[i].Url, newTabs || i > 0);
                }
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    AppServices.Bookmarks.BookmarksChanged -= Bookmarks_BookmarksChanged;
                    AppServices.Bookmarks.BookmarksBarVisibilityChanged -= Bookmarks_VisibilityChanged;
                    ThemeManager.AccentColorChanged -= ThemeManager_AccentColorChanged;
                }
                catch { }

                if (_toolTip != null)
                {
                    _toolTip.Dispose();
                    _toolTip = null;
                }


            }
            base.Dispose(disposing);
        }
    }
}
