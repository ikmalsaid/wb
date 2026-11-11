using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using wb.Core;

namespace wb.UI
{
    /// <summary>
    /// Sleek, non-activating floating exit banner displayed during full-screen browsing.
    /// Features crisp rectangular geometry (no curved boxes), high-contrast dynamic accent coloring,
    /// high-DPI vector rendering (no pixelated assets), auto-dismissal and instant top-edge hover revelation.
    /// </summary>
    public class FullScreenExitOverlay : Form
    {
        private Form _owner;
        private readonly Action _onExitRequested;

        private float _dpiScale = 1.0f;
        private bool _isFullScreenActive = false;
        private bool _isHoveringExitBtn = false;
        private bool _isPressedExitBtn = false;
        private bool _isSliding = false;
        private int _currentSlideY = 0;
        private int _targetSlideY = 0;

        private Rectangle _exitBtnRect = Rectangle.Empty;
        private Font _messageFont;
        private Font _btnFont;

        private Timer _autoHideTimer;
        private Timer _edgePollTimer;
        private Timer _slideTimer;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public FullScreenExitOverlay(Action onExitRequested)
        {
            _onExitRequested = onExitRequested;

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopLevel = true;
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(32, 33, 36);
            this.Cursor = Cursors.Default;
            this.TabStop = false;

            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            UpdateFonts();
            UpdateLayoutDimensions();

            // Auto-hide timer: hides overlay after 3.5s of inactivity
            _autoHideTimer = new Timer();
            _autoHideTimer.Interval = 3500;
            _autoHideTimer.Tick += (s, e) =>
            {
                _autoHideTimer.Stop();
                if (!_isFullScreenActive) return;

                // Do not hide if the mouse is currently over this overlay
                Point screenCursor = Cursor.Position;
                Point clientCursor = this.PointToClient(screenCursor);
                if (this.ClientRectangle.Contains(clientCursor))
                {
                    // Delay hiding until the user moves away
                    _autoHideTimer.Interval = 1800;
                    _autoHideTimer.Start();
                    return;
                }

                StartSlide(false);
            };

            // Edge poll timer: reveals overlay when cursor touches top screen edge
            _edgePollTimer = new Timer();
            _edgePollTimer.Interval = 50;
            _edgePollTimer.Tick += (s, e) => CheckTopEdgeHover();

            // Smooth slide animation timer
            _slideTimer = new Timer();
            _slideTimer.Interval = 15;
            _slideTimer.Tick += (s, e) => OnSlideTick();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: never steals focus from full screen web canvas
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: excluded from Alt+Tab
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST: floats securely above DirectComposition surface
                return cp;
            }
        }

        public void AttachTo(Form owner)
        {
            _owner = owner;
            if (_owner != null)
            {
                _owner.LocationChanged += (s, e) => UpdatePosition();
                _owner.SizeChanged += (s, e) => UpdatePosition();
                _owner.VisibleChanged += (s, e) =>
                {
                    if (!_owner.Visible && this.Visible)
                    {
                        HideForFullScreen();
                    }
                };
                _owner.FormClosing += (s, e) =>
                {
                    try
                    {
                        if (!this.IsDisposed)
                        {
                            this.Close();
                        }
                    }
                    catch { }
                };
            }
        }

        public float DpiScale
        {
            get { return _dpiScale; }
            set
            {
                if (Math.Abs(_dpiScale - value) > 0.01f)
                {
                    _dpiScale = value;
                    UpdateFonts();
                    UpdateLayoutDimensions();
                    UpdatePosition();
                    this.Invalidate();
                }
            }
        }

        public bool IsFullScreenActive
        {
            get { return _isFullScreenActive; }
        }

        private bool _suppressed = false;

        /// <summary>
        /// When true (e.g. during windowed immersive player mode), prevents the exit banner from being shown.
        /// </summary>
        public bool Suppressed
        {
            get { return _suppressed; }
            set { _suppressed = value; }
        }

        private void UpdateFonts()
        {
            if (_messageFont != null) _messageFont.Dispose();
            if (_btnFont != null) _btnFont.Dispose();

            _messageFont = new Font("Segoe UI", Math.Max(8.5f, 9.5f * _dpiScale), FontStyle.Regular);
            _btnFont = new Font("Segoe UI", Math.Max(8.0f, 8.5f * _dpiScale), FontStyle.Bold);
        }

        private void UpdateLayoutDimensions()
        {
            int w = (int)(370 * _dpiScale);
            int h = (int)(40 * _dpiScale);
            this.Size = new Size(w, h);

            int btnW = (int)(115 * _dpiScale);
            int btnH = (int)(26 * _dpiScale);
            int btnX = w - btnW - (int)(10 * _dpiScale);
            int btnY = (h - btnH) / 2;
            _exitBtnRect = new Rectangle(btnX, btnY, btnW, btnH);
        }

        private void UpdatePosition()
        {
            if (_owner == null || _owner.IsDisposed) return;

            int targetX = _owner.Left + (_owner.Width - this.Width) / 2;
            int targetY = _isSliding ? _currentSlideY : (_isFullScreenActive && this.Visible ? _owner.Top : _owner.Top - this.Height);

            this.Location = new Point(targetX, targetY);
        }

        /// <summary>
        /// Activates full screen tracking and presents the exit banner.
        /// </summary>
        public void ShowForFullScreen()
        {
            if (Suppressed) return;

            _isFullScreenActive = true;
            UpdateLayoutDimensions();
            UpdatePosition();

            if (!this.Visible)
            {
                this.Show();
            }

            BringToTopNoActivate();
            StartSlide(true);

            _edgePollTimer.Start();
            _autoHideTimer.Stop();
            _autoHideTimer.Interval = 3500;
            _autoHideTimer.Start();
        }

        /// <summary>
        /// Dismisses full screen tracking and hides the overlay immediately.
        /// </summary>
        public void HideForFullScreen()
        {
            _isFullScreenActive = false;
            _isSliding = false;
            _autoHideTimer.Stop();
            _edgePollTimer.Stop();
            _slideTimer.Stop();
            this.Hide();
        }

        private void StartSlide(bool slideDown)
        {
            if (_owner == null || _owner.IsDisposed) return;

            int shownY = _owner.Top;
            int hiddenY = _owner.Top - this.Height;

            _targetSlideY = slideDown ? shownY : hiddenY;
            _currentSlideY = this.Top;

            if (_currentSlideY == _targetSlideY)
            {
                _isSliding = false;
                if (!slideDown)
                {
                    this.Hide();
                }
                return;
            }

            _isSliding = true;
            _slideTimer.Start();
        }

        private void OnSlideTick()
        {
            if (!_isSliding || _owner == null || _owner.IsDisposed)
            {
                _slideTimer.Stop();
                return;
            }

            int step = Math.Max(4, (int)(8 * _dpiScale));
            if (_currentSlideY < _targetSlideY)
            {
                _currentSlideY = Math.Min(_currentSlideY + step, _targetSlideY);
            }
            else if (_currentSlideY > _targetSlideY)
            {
                _currentSlideY = Math.Max(_currentSlideY - step, _targetSlideY);
            }

            int targetX = _owner.Left + (_owner.Width - this.Width) / 2;
            this.Location = new Point(targetX, _currentSlideY);

            if (_currentSlideY == _targetSlideY)
            {
                _isSliding = false;
                _slideTimer.Stop();

                if (_targetSlideY < _owner.Top)
                {
                    this.Hide();
                }
                else
                {
                    BringToTopNoActivate();
                }
            }
        }

        private void CheckTopEdgeHover()
        {
            if (Suppressed || !_isFullScreenActive || _owner == null || _owner.IsDisposed) return;

            Point cursor = Cursor.Position;
            bool withinHoriz = cursor.X >= _owner.Left && cursor.X <= _owner.Right;
            bool touchingTop = cursor.Y <= _owner.Top + 4;

            if (touchingTop && withinHoriz)
            {
                if (!this.Visible || this.Top < _owner.Top)
                {
                    if (!this.Visible) this.Show();
                    BringToTopNoActivate();
                    StartSlide(true);
                    _autoHideTimer.Stop();
                    _autoHideTimer.Interval = 3500;
                    _autoHideTimer.Start();
                }
            }
        }

        private void BringToTopNoActivate()
        {
            if (this.IsHandleCreated)
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, this.Left, this.Top, this.Width, this.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // While hovering over the banner, keep it visible
            _autoHideTimer.Stop();

            bool overBtn = _exitBtnRect.Contains(e.Location);
            if (overBtn != _isHoveringExitBtn)
            {
                _isHoveringExitBtn = overBtn;
                this.Cursor = overBtn ? Cursors.Hand : Cursors.Default;
                this.Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            if (_isHoveringExitBtn || _isPressedExitBtn)
            {
                _isHoveringExitBtn = false;
                _isPressedExitBtn = false;
                this.Cursor = Cursors.Default;
                this.Invalidate();
            }

            if (_isFullScreenActive)
            {
                _autoHideTimer.Stop();
                _autoHideTimer.Interval = 2000;
                _autoHideTimer.Start();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left && _exitBtnRect.Contains(e.Location))
            {
                _isPressedExitBtn = true;
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_isPressedExitBtn)
            {
                _isPressedExitBtn = false;
                this.Invalidate();

                if (_exitBtnRect.Contains(e.Location))
                {
                    HideForFullScreen();
                    if (_onExitRequested != null)
                    {
                        _onExitRequested();
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            Color accent = ThemeManager.CurrentAccentColor;

            // 1. Solid rectangular canvas background
            g.Clear(Color.FromArgb(32, 33, 36));

            // 2. Crisp 1px outer borders (Top, Left, Right) - strictly rectangular
            using (Pen borderPen = new Pen(Color.FromArgb(60, 64, 67), 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
            }

            // 3. Crisp 2px bottom accent border - clean flat line anchor
            int accentLineH = Math.Max(2, (int)(2 * _dpiScale));
            using (SolidBrush accentBrush = new SolidBrush(accent))
            {
                g.FillRectangle(accentBrush, 0, h - accentLineH, w, accentLineH);
            }

            // 4. Message text: "Press F11 or Esc to exit" (vertically centered, crisp Segoe UI typography)
            int padLeft = (int)(16 * _dpiScale);
            int textW = _exitBtnRect.Left - padLeft - (int)(8 * _dpiScale);
            Rectangle textRect = new Rectangle(padLeft, 0, Math.Max(0, textW), h - accentLineH);
            TextRenderer.DrawText(
                g,
                AppStrings.Browser.FullScreenExitHint,
                _messageFont,
                textRect,
                Color.FromArgb(232, 234, 237),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            // 5. Exit Button (strictly rectangular, no curved boxes, high-contrast accent hover)
            DrawExitButton(g, _exitBtnRect, accent);
        }

        private void DrawExitButton(Graphics g, Rectangle rect, Color accent)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // Determine contrast color based on accent perceived luminance via UITheme
            Color contrastTextColor = UITheme.GetContrastTextColor(accent);

            Color btnBg;
            Color btnBorder;
            Color btnTextColor;

            if (_isPressedExitBtn)
            {
                btnBg = Color.FromArgb(Math.Max(0, accent.R - 35), Math.Max(0, accent.G - 35), Math.Max(0, accent.B - 35));
                btnBorder = btnBg;
                btnTextColor = UITheme.GetContrastTextColor(btnBg);
            }
            else if (_isHoveringExitBtn)
            {
                btnBg = accent;
                btnBorder = accent;
                btnTextColor = contrastTextColor;
            }
            else
            {
                btnBg = Color.FromArgb(41, 42, 45);
                btnBorder = accent;
                btnTextColor = accent;
            }

            // Strictly rectangular button fill & border (NO curved boxes)
            using (SolidBrush bgBrush = new SolidBrush(btnBg))
            {
                g.FillRectangle(bgBrush, rect);
            }

            using (Pen borderPen = new Pen(btnBorder, 1f))
            {
                g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }

            // Crisp, centered text "Exit Full Screen" (without vector ✕ glyph)
            TextRenderer.DrawText(
                g,
                AppStrings.Browser.FullScreenBtnExit,
                _btnFont,
                rect,
                btnTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _owner = null;
                if (_autoHideTimer != null) { _autoHideTimer.Dispose(); _autoHideTimer = null; }
                if (_edgePollTimer != null) { _edgePollTimer.Dispose(); _edgePollTimer = null; }
                if (_slideTimer != null) { _slideTimer.Dispose(); _slideTimer = null; }
                if (_messageFont != null) { _messageFont.Dispose(); _messageFont = null; }
                if (_btnFont != null) { _btnFont.Dispose(); _btnFont = null; }
            }
            base.Dispose(disposing);
        }
    }
}
