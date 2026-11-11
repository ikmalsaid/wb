using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Browser
{
    /// <summary>
    /// Flicker-free, fully double-buffered TabControl with UserPaint and WS_CLIPCHILDREN
    /// for hosted browser tabs, with integrated Material Design tab titles,
    /// close ('×') buttons with hover effects and a '+' (New Tab) button beside the tabs.
    /// </summary>
    public class ChromeTabControl : TabControl
    {
        private bool _isNewTabHover = false;
        private bool _isNewTabPressed = false;
        private int _hoveredCloseIndex = -1;
        private ToolTip _toolTip;
        private bool _toolTipShown = false;
        private float _dpiScale = 1.0f;

        public event EventHandler NewTabClick;
        public event EventHandler<int> TabCloseClick;

        public ChromeTabControl()
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            this.DoubleBuffered = true;
            this.BackColor = MaterialColors.ToolbarBackground;

            _toolTip = new ToolTip();
            _toolTip.InitialDelay = 350;
            _toolTip.ReshowDelay = 100;
        }

        public float DpiScale
        {
            get { return _dpiScale; }
            set
            {
                if (Math.Abs(_dpiScale - value) > 0.01f)
                {
                    _dpiScale = value;
                    InvalidateHeader();
                }
            }
        }

        private bool _showHeader = true;

        public bool ShowHeader
        {
            get { return _showHeader; }
            set
            {
                if (_showHeader != value)
                {
                    _showHeader = value;
                    if (this.SelectedTab != null)
                    {
                        this.SelectedTab.Bounds = this.DisplayRectangle;
                    }
                    this.Invalidate();
                }
            }
        }

        public int HeaderHeight
        {
            get
            {
                if (!_showHeader) return 0;
                if (this.TabPages.Count > 0)
                {
                    return Math.Max(28, this.GetTabRect(0).Bottom + 4);
                }
                return Math.Max(28, (int)(30 * _dpiScale));
            }
        }

        /// <summary>
        /// Overrides DisplayRectangle to eliminate Win32 SysTabControl32's default 4-pixel
        /// border margins, docking tab pages completely flush with the left, right and bottom bounds.
        /// </summary>
        public override Rectangle DisplayRectangle
        {
            get
            {
                int headerH = HeaderHeight;
                return new Rectangle(0, headerH, this.Width, Math.Max(0, this.Height - headerH));
            }
        }

        /// <summary>
        /// Gets the bounding rectangle of the '+' (New Tab) button located beside the last tab.
        /// </summary>
        public Rectangle NewTabButtonRect
        {
            get
            {
                int btnSize = Math.Max(22, (int)(24 * _dpiScale));
                int x = (int)(6 * _dpiScale);
                int y = (HeaderHeight - btnSize) / 2;

                if (this.TabPages.Count > 0)
                {
                    Rectangle lastTab = this.GetTabRect(this.TabPages.Count - 1);
                    x = lastTab.Right + (int)(5 * _dpiScale);
                    y = lastTab.Top + (lastTab.Height - btnSize) / 2;
                }

                // Ensure the button stays within visible tab strip bounds
                if (x + btnSize > this.Width - (int)(10 * _dpiScale))
                {
                    x = this.Width - btnSize - (int)(10 * _dpiScale);
                }

                return new Rectangle(x, y, btnSize, btnSize);
            }
        }

        /// <summary>
        /// Gets the bounding rectangle of the '×' (Close) button for a given tab index.
        /// </summary>
        public Rectangle GetCloseButtonRect(int tabIndex)
        {
            if (tabIndex < 0 || tabIndex >= this.TabPages.Count) return Rectangle.Empty;
            Rectangle bounds = this.GetTabRect(tabIndex);
            int size = Math.Max(14, (int)(16 * _dpiScale));
            int x = bounds.Right - size - (int)(6 * _dpiScale);
            int y = bounds.Top + (bounds.Height - size) / 2;
            return new Rectangle(x, y, size, size);
        }

        /// <summary>
        /// Checks if a client coordinate is within or slightly padded around the New Tab button.
        /// </summary>
        public bool IsPointOverNewTabButton(Point pt)
        {
            Rectangle rect = NewTabButtonRect;
            if (rect.IsEmpty) return false;
            Rectangle hitRect = new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4);
            return hitRect.Contains(pt);
        }

        public void InvalidateHeader()
        {
            this.Invalidate(new Rectangle(0, 0, this.Width, HeaderHeight + 2));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_CLIPCHILDREN: Prevents the tab control from painting over child TabPages/Browser controls
                cp.Style |= 0x02000000;
                return cp;
            }
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            TabPage tp = e.Control as TabPage;
            if (tp != null)
            {
                tp.Bounds = this.DisplayRectangle;
            }
            UpdateHoverStateUnderCursor();
            InvalidateHeader();
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);
            _hoveredCloseIndex = -1;
            UpdateHoverStateUnderCursor();
            InvalidateHeader();
        }

        private void UpdateHoverStateUnderCursor()
        {
            Point pt = this.PointToClient(Cursor.Position);
            if (this.ClientRectangle.Contains(pt))
            {
                _isNewTabHover = IsPointOverNewTabButton(pt);
                int closeIndex = -1;
                for (int i = 0; i < this.TabPages.Count; i++)
                {
                    if (GetCloseButtonRect(i).Contains(pt))
                    {
                        closeIndex = i;
                        break;
                    }
                }
                _hoveredCloseIndex = closeIndex;
                this.Cursor = (_isNewTabHover || closeIndex >= 0) ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            if (this.SelectedTab != null)
            {
                this.SelectedTab.Bounds = this.DisplayRectangle;
            }
            InvalidateHeader();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.SelectedTab != null)
            {
                this.SelectedTab.Bounds = this.DisplayRectangle;
            }
            InvalidateHeader();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool isOverNewTab = IsPointOverNewTabButton(e.Location);
            int closeIndex = -1;
            for (int i = 0; i < this.TabPages.Count; i++)
            {
                if (GetCloseButtonRect(i).Contains(e.Location))
                {
                    closeIndex = i;
                    break;
                }
            }

            bool needsRepaint = false;

            if (isOverNewTab != _isNewTabHover)
            {
                _isNewTabHover = isOverNewTab;
                needsRepaint = true;
            }

            if (closeIndex != _hoveredCloseIndex)
            {
                _hoveredCloseIndex = closeIndex;
                needsRepaint = true;
            }

            this.Cursor = (isOverNewTab || closeIndex >= 0) ? Cursors.Hand : Cursors.Default;

            if (needsRepaint)
            {
                InvalidateHeader();
            }

            if (isOverNewTab && !_toolTipShown)
            {
                Rectangle rect = NewTabButtonRect;
                if (_toolTip != null)
                {
                    _toolTip.Show(AppStrings.Browser.TipNewTab, this, rect.Left, rect.Bottom + 4, 2500);
                }
                _toolTipShown = true;
            }
            else if (!isOverNewTab && _toolTipShown)
            {
                if (_toolTip != null)
                {
                    _toolTip.Hide(this);
                }
                _toolTipShown = false;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_isNewTabHover || _hoveredCloseIndex >= 0)
            {
                _isNewTabHover = false;
                _hoveredCloseIndex = -1;
                this.Cursor = Cursors.Default;
                if (_toolTipShown && _toolTip != null)
                {
                    _toolTip.Hide(this);
                    _toolTipShown = false;
                }
                InvalidateHeader();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // 1. '+' New Tab Button click
            if (e.Button == MouseButtons.Left && IsPointOverNewTabButton(e.Location))
            {
                _isNewTabPressed = true;
                if (_toolTipShown && _toolTip != null)
                {
                    _toolTip.Hide(this);
                    _toolTipShown = false;
                }
                InvalidateHeader();

                if (NewTabClick != null)
                {
                    NewTabClick(this, EventArgs.Empty);
                }
                return;
            }

            // 2. Tab header and Close ('×') button hit-testing
            for (int i = 0; i < this.TabPages.Count; i++)
            {
                Rectangle tabRect = this.GetTabRect(i);
                Rectangle closeRect = GetCloseButtonRect(i);

                if (closeRect.Contains(e.Location) && e.Button == MouseButtons.Left)
                {
                    if (TabCloseClick != null)
                    {
                        TabCloseClick(this, i);
                    }
                    return;
                }

                if (tabRect.Contains(e.Location))
                {
                    if (e.Button == MouseButtons.Middle)
                    {
                        if (TabCloseClick != null)
                        {
                            TabCloseClick(this, i);
                        }
                        return;
                    }

                    if (e.Button == MouseButtons.Left)
                    {
                        if (this.SelectedIndex != i)
                        {
                            this.SelectedIndex = i;
                        }
                        base.OnMouseDown(e);
                    }
                    return;
                }
            }

            // 3. Double-click on unoccupied tab strip area opens new tab
            if (e.Button == MouseButtons.Left && e.Clicks == 2 && e.Y <= HeaderHeight)
            {
                if (NewTabClick != null)
                {
                    NewTabClick(this, EventArgs.Empty);
                }
                return;
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isNewTabPressed)
            {
                _isNewTabPressed = false;
                InvalidateHeader();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_showHeader)
            {
                using (SolidBrush bgBrush = new SolidBrush(MaterialColors.ToolbarBackground))
                {
                    e.Graphics.FillRectangle(bgBrush, this.ClientRectangle);
                }
                return;
            }

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int headerH = HeaderHeight;
            Rectangle headerRect = new Rectangle(0, 0, this.Width, headerH);

            // 1. Draw entire background (header and behind web canvas)
            using (SolidBrush bgBrush = new SolidBrush(MaterialColors.ToolbarBackground))
            {
                g.FillRectangle(bgBrush, this.ClientRectangle);
            }

            // Draw subtle horizontal separator between header and web canvas
            using (Pen sepPen = new Pen(MaterialColors.Separator, 1))
            {
                g.DrawLine(sepPen, 0, headerH - 1, this.Width, headerH - 1);
            }

            // 2. Draw each tab
            for (int i = 0; i < this.TabPages.Count; i++)
            {
                TabPage page = this.TabPages[i];
                Rectangle bounds = this.GetTabRect(i);
                bool isSelected = (this.SelectedIndex == i);

                Color tabBg = isSelected ? MaterialColors.TabActive : MaterialColors.TabInactive;
                Color textColor = isSelected ? MaterialColors.TextPrimary : MaterialColors.TextSecondary;

                Rectangle tabBounds = bounds;
                if (isSelected)
                {
                    tabBounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, headerH - bounds.Y);
                }

                using (SolidBrush b = new SolidBrush(tabBg))
                {
                    g.FillRectangle(b, tabBounds);
                }

                if (isSelected)
                {
                    using (Pen p = new Pen(MaterialColors.AccentBlue, 2))
                    {
                        g.DrawLine(p, bounds.Left, bounds.Top + 1, bounds.Right, bounds.Top + 1);
                    }
                }

                // Tab Title Text
                Rectangle closeRect = GetCloseButtonRect(i);
                int titleWidth = closeRect.Left - bounds.Left - (int)(10 * _dpiScale);
                if (titleWidth > 0)
                {
                    Rectangle textRect = new Rectangle(
                        bounds.Left + (int)(8 * _dpiScale),
                        bounds.Top + (int)(4 * _dpiScale),
                        titleWidth,
                        bounds.Height - (int)(8 * _dpiScale));
                    TextRenderer.DrawText(g, page.Text, this.Font, textRect, textColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }

                // Close Button ('×')
                bool isCloseHover = (_hoveredCloseIndex == i);
                if (isCloseHover)
                {
                    using (SolidBrush hoverBg = new SolidBrush(MaterialColors.ButtonHover))
                    {
                        g.FillEllipse(hoverBg, closeRect);
                    }
                }

                Color closeColor = isCloseHover ? MaterialColors.TextPrimary : MaterialColors.TextSecondary;
                using (Font closeFont = new Font("Segoe UI", 9F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, "×", closeFont, closeRect, closeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }

            // 3. Draw '+' New Tab Button
            DrawNewTabButton(g);
        }

        public void DrawNewTabButton(Graphics g)
        {
            Rectangle rect = NewTabButtonRect;
            if (rect.IsEmpty) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw background rounded rectangle on hover or pressed
            Color bg = _isNewTabPressed ? MaterialColors.ButtonPressed :
                       _isNewTabHover ? MaterialColors.ButtonHover :
                       Color.Transparent;

            if (bg != Color.Transparent)
            {
                using (SolidBrush b = new SolidBrush(bg))
                using (GraphicsPath path = CreateRoundedRectangle(rect, Math.Max(2, (int)(4 * _dpiScale))))
                {
                    g.FillPath(b, path);
                }
            }

            // Draw Material '+' icon
            int iconSize = Math.Max(12, (int)(14 * _dpiScale));
            Color iconColor = _isNewTabHover ? MaterialColors.TextPrimary : MaterialColors.TextSecondary;
            int ix = rect.X + (rect.Width - iconSize) / 2;
            int iy = rect.Y + (rect.Height - iconSize) / 2;
            MaterialIcons.DrawIcon(g, "plus", new Rectangle(ix, iy, iconSize, iconSize), iconColor);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) // WM_ERASEBKGND
            {
                // Suppress background erasing to guarantee 100% flicker-free painting
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == 0x0084) // WM_NCHITTEST
            {
                base.WndProc(ref m);
                // Windows SysTabControl32 returns HTTRANSPARENT (-1) for unoccupied tab strip area.
                // Intercept and return HTCLIENT (1) across header so mouse clicks/moves are received directly.
                if ((int)m.Result == -1)
                {
                    int lParam = m.LParam.ToInt32();
                    int x = (short)(lParam & 0xFFFF);
                    int y = (short)((lParam >> 16) & 0xFFFF);
                    Point clientPt = this.PointToClient(new Point(x, y));
                    if (clientPt.Y <= HeaderHeight)
                    {
                        m.Result = (IntPtr)1; // HTCLIENT
                    }
                }
                return;
            }

            base.WndProc(ref m);
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            return UITheme.CreateRoundedPath(rect, radius);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _toolTip != null)
            {
                _toolTip.Dispose();
                _toolTip = null;
            }
            base.Dispose(disposing);
        }
    }
}
