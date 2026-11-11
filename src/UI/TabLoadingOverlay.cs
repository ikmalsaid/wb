using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using wb.Core;

namespace wb.UI
{
    /// <summary>
    /// Smooth, non-activating loading spinner overlay displayed above the tab canvas
    /// during tab switching and internal page navigation to mask unpainted elements
    /// and provide a fluid, modern transition experience.
    /// </summary>
    public class TabLoadingOverlay : Form
    {
        private Form _owner;
        private Control _targetCanvas;
        private Timer _animTimer;
        private Timer _dismissTimer;
        private float _angle = 0f;
        private float _sweepAngle = 90f;
        private float _dpiScale = 1.0f;
        private string _message = null;
        private Font _messageFont;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int SW_SHOWNOACTIVATE = 4;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        public TabLoadingOverlay()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.TopLevel = true;
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(32, 33, 36); // Match MaterialColors.ToolbarBackground
            this.Cursor = Cursors.Default;
            this.TabStop = false;

            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            _messageFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // 60 FPS animation timer for butter-smooth spinner rotation
            _animTimer = new Timer();
            _animTimer.Interval = 16;
            _animTimer.Tick += (s, e) =>
            {
                _angle = (_angle + 8.5f) % 360f;
                if (this.Visible)
                {
                    this.Invalidate();
                }
            };

            // Auto-dismiss safety timer
            _dismissTimer = new Timer();
            _dismissTimer.Tick += (s, e) =>
            {
                _dismissTimer.Stop();
                HideOverlay();
            };

            // Instant dismiss if user clicks overlay
            this.MouseDown += (s, e) => HideOverlay();
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
                // WS_EX_NOACTIVATE (0x08000000): prevents stealing focus from active page/input
                cp.ExStyle |= 0x08000000;
                // WS_EX_TOOLWINDOW (0x00000080): keeps it out of Alt-Tab and taskbar
                cp.ExStyle |= 0x00000080;
                return cp;
            }
        }

        public float DpiScale
        {
            get { return _dpiScale; }
            set
            {
                if (value > 0.1f && Math.Abs(_dpiScale - value) > 0.01f)
                {
                    _dpiScale = value;
                    if (_messageFont != null)
                    {
                        try { _messageFont.Dispose(); } catch { }
                    }
                    _messageFont = new Font("Segoe UI", 9.5f * _dpiScale, FontStyle.Regular);
                    this.Invalidate();
                }
            }
        }

        /// <summary>
        /// Attaches this overlay to the owner browser form and target canvas control.
        /// </summary>
        public void AttachTo(Form owner, Control targetCanvas)
        {
            _owner = owner;
            _targetCanvas = targetCanvas;

            if (_owner != null)
            {
                _owner.LocationChanged += (s, e) => { if (this.Visible) UpdateOverlayBounds(); };
                _owner.SizeChanged += (s, e) => { if (this.Visible) UpdateOverlayBounds(); };
                _owner.VisibleChanged += (s, e) => { if (!_owner.Visible && this.Visible) this.Hide(); };
                _owner.FormClosing += (s, e) =>
                {
                    try
                    {
                        if (!this.IsDisposed) this.Close();
                    }
                    catch { }
                };
            }

            if (_targetCanvas != null)
            {
                _targetCanvas.Resize += (s, e) => { if (this.Visible) UpdateOverlayBounds(); };
            }
        }

        public void UpdateOverlayBounds()
        {
            if (_owner == null || _targetCanvas == null || this.IsDisposed) return;
            if (_owner.WindowState == FormWindowState.Minimized || !_owner.Visible)
            {
                if (this.Visible) this.Hide();
                return;
            }

            try
            {
                Rectangle targetRect;
                if (_targetCanvas is TabControl)
                {
                    targetRect = ((TabControl)_targetCanvas).DisplayRectangle;
                }
                else
                {
                    targetRect = _targetCanvas.ClientRectangle;
                }

                if (targetRect.Width <= 0 || targetRect.Height <= 0) return;

                Point screenPt = _targetCanvas.PointToScreen(targetRect.Location);
                this.Bounds = new Rectangle(screenPt, targetRect.Size);

                if (this.IsHandleCreated)
                {
                    SetWindowPos(this.Handle, HWND_TOP, screenPt.X, screenPt.Y, targetRect.Width, targetRect.Height, SWP_NOACTIVATE);
                }
            }
            catch { }
        }

        private void EnsureVisible()
        {
            if (!this.Visible)
            {
                if (this.IsHandleCreated)
                {
                    ShowWindow(this.Handle, SW_SHOWNOACTIVATE);
                }
                else if (_owner != null && !_owner.IsDisposed && _owner.Visible)
                {
                    this.Show(_owner);
                }
                else
                {
                    this.Show();
                }
            }
        }

        /// <summary>
        /// Displays the spinner overlay for tab switching transitions.
        /// </summary>
        public void ShowForTabSwitch(string message = null, int durationMs = 220)
        {
            ShowLoading(message, durationMs);
        }

        /// <summary>
        /// Displays the spinner overlay for internal page transitions.
        /// </summary>
        public void ShowForInternalPage(string pageTitle = null, int durationMs = 200)
        {
            string msg = string.IsNullOrEmpty(pageTitle) ? AppStrings.Browser.TabLoadingDefault : string.Format(AppStrings.Browser.TabLoadingFormat, pageTitle);
            ShowLoading(msg, durationMs);
        }

        /// <summary>
        /// Displays the spinner overlay with an optional status message and auto-dismiss duration.
        /// </summary>
        public void ShowLoading(string message = null, int durationMs = 250)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(() => ShowLoading(message, durationMs))); } catch { }
                return;
            }
            if (_owner == null || _targetCanvas == null) return;
            if (_owner.WindowState == FormWindowState.Minimized || !_owner.Visible) return;

            _message = message;
            UpdateOverlayBounds();
            EnsureVisible();

            if (this.IsHandleCreated)
            {
                SetWindowPos(this.Handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            if (!_animTimer.Enabled)
            {
                _animTimer.Start();
            }

            // Reset dismiss timer
            _dismissTimer.Stop();
            if (durationMs > 0)
            {
                _dismissTimer.Interval = Math.Max(50, durationMs);
                _dismissTimer.Start();
            }

            this.Invalidate();
        }

        /// <summary>
        /// Immediately dismisses the loading overlay.
        /// </summary>
        public void HideOverlay()
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(HideOverlay)); } catch { }
                return;
            }
            try
            {
                _dismissTimer.Stop();
                _animTimer.Stop();
                _message = null;
                if (this.Visible)
                {
                    this.Hide();
                }
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            // 1. Solid canvas background flush with dark theme
            g.Clear(Color.FromArgb(32, 33, 36));

            int centerX = w / 2;
            int centerY = h / 2 - (!string.IsNullOrEmpty(_message) ? (int)(12 * _dpiScale) : 0);
            int spinnerSize = Math.Max(24, (int)(36 * _dpiScale));
            Rectangle spinnerRect = new Rectangle(centerX - spinnerSize / 2, centerY - spinnerSize / 2, spinnerSize, spinnerSize);

            // 2. Subtle ambient circular track
            float penWidth = Math.Max(2.5f, 3.5f * _dpiScale);
            using (Pen trackPen = new Pen(Color.FromArgb(30, 255, 255, 255), penWidth))
            {
                g.DrawEllipse(trackPen, spinnerRect);
            }

            // 3. Dynamic rotating accent arc
            Color accent = ThemeManager.CurrentAccentColor;
            using (Pen arcPen = new Pen(accent, penWidth))
            {
                arcPen.StartCap = LineCap.Round;
                arcPen.EndCap = LineCap.Round;
                g.DrawArc(arcPen, spinnerRect, _angle, _sweepAngle);
            }

            // 4. Centered status text beneath spinner
            if (!string.IsNullOrEmpty(_message))
            {
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Near,
                    Trimming = StringTrimming.EllipsisCharacter
                })
                using (Brush textBrush = new SolidBrush(Color.FromArgb(180, 232, 234, 237)))
                {
                    int textTop = centerY + spinnerSize / 2 + (int)(14 * _dpiScale);
                    Rectangle textRect = new Rectangle(20, textTop, Math.Max(100, w - 40), (int)(26 * _dpiScale));
                    g.DrawString(_message, _messageFont, textBrush, textRect, sf);
                }
            }
        }

        private void DetachFromOwner()
        {
            _owner = null;
            _targetCanvas = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachFromOwner();
                if (_animTimer != null)
                {
                    try { _animTimer.Stop(); _animTimer.Dispose(); } catch { }
                    _animTimer = null;
                }
                if (_dismissTimer != null)
                {
                    try { _dismissTimer.Stop(); _dismissTimer.Dispose(); } catch { }
                    _dismissTimer = null;
                }
                if (_messageFont != null)
                {
                    try { _messageFont.Dispose(); } catch { }
                    _messageFont = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
