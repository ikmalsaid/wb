using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace wb.UI
{
    /// <summary>
    /// Modern floating status bottom bar (auto-hiding bar docked flush to the bottom of the web canvas)
    /// that displays hyperlink destination previews and temporary browser status notifications without taking up
    /// persistent screen space (Chrome/Edge-style).
    /// Implemented as a lightweight, non-activating owned tool window to guarantee
    /// rendering above the native DirectComposition surface without stealing focus.
    /// Supports instant corner dodging when the mouse touches the active corner and
    /// smooth horizontal width expansion on long-hover to reveal full URLs without truncation.
    /// </summary>
    public class WbBottomBar : Form
    {
        private Form _owner;
        private string _statusText = string.Empty;
        private bool _isFlippedRight = false;
        private float _dpiScale = 1.0f;
        private int _blockedCount = 0;
        private Timer _hideTimer;
        private Font _statusFont;

        private Timer _mousePollTimer;
        private Timer _hoverExpandTimer;
        private Timer _expandAnimTimer;
        private bool _isHoverExpanded = false;
        private int _currentAnimatedW = 0;
        private int _targetW = 0;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int SW_SHOWNOACTIVATE = 4;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private void BringToFrontNoActivate()
        {
            if (this.IsHandleCreated)
            {
                SetWindowPos(this.Handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        public WbBottomBar()
        {
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

            _statusFont = new Font("Segoe UI", 9F, FontStyle.Regular);

            _hideTimer = new Timer();
            _hideTimer.Interval = 250;
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                if (_mousePollTimer != null) _mousePollTimer.Stop();
                if (_hoverExpandTimer != null) _hoverExpandTimer.Stop();
                if (_expandAnimTimer != null) _expandAnimTimer.Stop();
                _isHoverExpanded = false;
                _statusText = string.Empty;
                _currentAnimatedW = 0;
                _targetW = 0;
                this.Hide();
            };

            _mousePollTimer = new Timer();
            _mousePollTimer.Interval = 40;
            _mousePollTimer.Tick += (s, e) =>
            {
                if (!this.Visible)
                {
                    _mousePollTimer.Stop();
                    return;
                }

                Control targetHost = _owner ?? this.Parent;
                if (targetHost == null || targetHost.IsDisposed)
                {
                    this.Hide();
                    return;
                }

                Form formHost = targetHost as Form;
                if (formHost != null && (formHost.WindowState == FormWindowState.Minimized || !formHost.Visible))
                {
                    this.Hide();
                    return;
                }

                Point cursorClient = targetHost.PointToClient(Cursor.Position);
                // If cursor has moved outside the host's client bounds, auto-hide the bar
                // (Chromium drops empty status messages when the cursor leaves the window bounds)
                if (!targetHost.ClientRectangle.Contains(cursorClient))
                {
                    if (!_hideTimer.Enabled)
                    {
                        _hideTimer.Interval = 100;
                        _hideTimer.Start();
                    }
                    return;
                }

                CheckCursorDodge();
            };

            _hoverExpandTimer = new Timer();
            _hoverExpandTimer.Interval = 600;
            _hoverExpandTimer.Tick += (s, e) =>
            {
                _hoverExpandTimer.Stop();
                if (this.Visible && !string.IsNullOrEmpty(_statusText))
                {
                    _isHoverExpanded = true;
                    StartExpandAnimation();
                }
            };

            _expandAnimTimer = new Timer();
            _expandAnimTimer.Interval = 15;
            _expandAnimTimer.Tick += (s, e) => OnAnimTick();
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

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;
            // Returning HTTRANSPARENT makes this status indicator completely mouse-transparent.
            // All mouse movement, enter and click messages pass directly to Chromium underneath,
            // preventing spurious WM_MOUSELEAVE events that disrupt hyperlink hover state.
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        private EventHandler _ownerLocationChanged;
        private EventHandler _ownerSizeChanged;
        private EventHandler _ownerVisibleChanged;
        private EventHandler _ownerDeactivate;
        private FormClosingEventHandler _ownerFormClosing;

        /// <summary>
        /// Attaches this floating bottom bar to the owner form so it stays synchronized
        /// with window movements, resize and state changes.
        /// </summary>
        public void AttachTo(Form owner)
        {
            DetachFromOwner();
            _owner = owner;
            if (_owner != null)
            {
                _ownerLocationChanged = (s, e) => { if (this.Visible) UpdateBubbleLayout(); };
                _ownerSizeChanged = (s, e) => { if (this.Visible) UpdateBubbleLayout(); };
                _ownerVisibleChanged = (s, e) => { if (!_owner.Visible && this.Visible) this.Hide(); };
                _ownerDeactivate = (s, e) => { if (this.Visible) this.Hide(); };
                _ownerFormClosing = (s, e) =>
                {
                    try
                    {
                        if (!this.IsDisposed) this.Close();
                    }
                    catch { }
                };

                _owner.LocationChanged += _ownerLocationChanged;
                _owner.SizeChanged += _ownerSizeChanged;
                _owner.VisibleChanged += _ownerVisibleChanged;
                _owner.Deactivate += _ownerDeactivate;
                _owner.FormClosing += _ownerFormClosing;
            }
        }

        private void DetachFromOwner()
        {
            if (_owner != null)
            {
                try
                {
                    if (_ownerLocationChanged != null) _owner.LocationChanged -= _ownerLocationChanged;
                    if (_ownerSizeChanged != null) _owner.SizeChanged -= _ownerSizeChanged;
                    if (_ownerVisibleChanged != null) _owner.VisibleChanged -= _ownerVisibleChanged;
                    if (_ownerDeactivate != null) _owner.Deactivate -= _ownerDeactivate;
                    if (_ownerFormClosing != null) _owner.FormClosing -= _ownerFormClosing;
                }
                catch { }
                _owner = null;
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
                    if (_statusFont != null) _statusFont.Dispose();
                    _statusFont = new Font("Segoe UI", Math.Max(8.5f, 9F * _dpiScale), FontStyle.Regular);
                    UpdateBubbleLayout();
                }
            }
        }

        public string StatusText
        {
            get { return _statusText; }
        }

        public bool IsFlippedRight
        {
            get { return _isFlippedRight; }
            set
            {
                if (_isFlippedRight != value)
                {
                    _isFlippedRight = value;
                    UpdateBubbleLayout();
                }
            }
        }

        public bool IsHoverExpanded
        {
            get { return _isHoverExpanded; }
            set
            {
                if (_isHoverExpanded != value)
                {
                    _isHoverExpanded = value;
                    if (_hoverExpandTimer != null) _hoverExpandTimer.Stop();
                    if (_expandAnimTimer != null) _expandAnimTimer.Stop();
                    UpdateBubbleLayout();
                }
            }
        }

        public void TriggerHoverExpand()
        {
            if (_hoverExpandTimer != null) _hoverExpandTimer.Stop();
            _isHoverExpanded = true;
            UpdateBubbleLayout();
        }

        public int BlockedCount
        {
            get { return _blockedCount; }
            set
            {
                _blockedCount = Math.Max(0, value);
            }
        }

        /// <summary>
        /// Updates the status text (e.g. hovered hyperlink or page state).
        /// Hides smoothly when set to empty or 'Ready'.
        /// </summary>
        public void SetStatus(string text)
        {
            if (string.IsNullOrEmpty(text) || string.Equals(text, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                // Debounce empty pulses from DOM traversal:
                // Do NOT kill the hover timer or reset expanded state immediately.
                if (this.Visible && !_hideTimer.Enabled)
                {
                    _hideTimer.Interval = 250;
                    _hideTimer.Start();
                }
                return;
            }

            // Valid status text received -> cancel any scheduled hiding
            _hideTimer.Stop();

            string clean = text.Trim();
            Control targetHost = _owner ?? this.Parent;
            int hostW = targetHost != null ? targetHost.ClientSize.Width : 800;
            int baseMaxW = Math.Min(hostW - 20, (int)(480 * _dpiScale));
            int paddingH = (int)(10 * _dpiScale);
            Size textSize = TextRenderer.MeasureText(clean, _statusFont, Size.Empty, TextFormatFlags.NoPrefix);
            bool canExpand = (textSize.Width + (paddingH * 2)) > baseMaxW;

            if (_statusText != clean)
            {
                _statusText = clean;
                _isHoverExpanded = false;
                if (_hoverExpandTimer != null) _hoverExpandTimer.Stop();
                if (_expandAnimTimer != null) _expandAnimTimer.Stop();

                UpdateBubbleLayout();
                CheckCursorDodge();

                if (canExpand && _hoverExpandTimer != null)
                {
                    _hoverExpandTimer.Interval = 600;
                    _hoverExpandTimer.Start();
                }
            }
            else
            {
                // Same URL continuation (e.g. minor cursor movement over link or recovery from transient empty pulse)
                if (!this.Visible)
                {
                    _isHoverExpanded = false;
                    UpdateBubbleLayout();
                    CheckCursorDodge();
                    if (canExpand && _hoverExpandTimer != null)
                    {
                        _hoverExpandTimer.Interval = 600;
                        _hoverExpandTimer.Start();
                    }
                }
                else
                {
                    if (!_isHoverExpanded && canExpand && _hoverExpandTimer != null && !_hoverExpandTimer.Enabled)
                    {
                        _hoverExpandTimer.Interval = 600;
                        _hoverExpandTimer.Start();
                    }
                    CheckCursorDodge();
                }
            }

            EnsureVisible();
            BringToFrontNoActivate();
            this.Invalidate();
        }

        /// <summary>
        /// Shows a temporary notification message that automatically auto-hides after the specified duration.
        /// </summary>
        public void ShowTemporaryMessage(string message, int durationMs = 3000)
        {
            if (string.IsNullOrEmpty(message)) return;

            _hideTimer.Stop();
            _statusText = message.Trim();

            UpdateBubbleLayout();
            EnsureVisible();
            BringToFrontNoActivate();
            this.Invalidate();

            _hideTimer.Interval = Math.Max(500, durationMs);
            _hideTimer.Start();
        }

        private void EnsureVisible()
        {
            if (_owner != null && !_owner.IsDisposed && this.Owner != _owner)
            {
                try { this.Owner = _owner; } catch { }
            }

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

            if (_mousePollTimer != null && !_mousePollTimer.Enabled)
            {
                _mousePollTimer.Start();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!this.Visible)
            {
                if (_mousePollTimer != null && _mousePollTimer.Enabled) _mousePollTimer.Stop();
                if (_hoverExpandTimer != null && _hoverExpandTimer.Enabled) _hoverExpandTimer.Stop();
                if (_expandAnimTimer != null && _expandAnimTimer.Enabled) _expandAnimTimer.Stop();
                _statusText = string.Empty;
                _isHoverExpanded = false;
            }
        }

        /// <summary>
        /// Checks the current cursor position against the host client area and
        /// flips the bar to the opposite corner if the cursor enters the active corner.
        /// </summary>
        public bool CheckCursorDodge()
        {
            Control targetHost = _owner ?? this.Parent;
            if (targetHost == null || targetHost.IsDisposed) return false;
            Point cursorClient = targetHost.PointToClient(Cursor.Position);
            return UpdateCursorDodge(cursorClient);
        }

        /// <summary>
        /// Evaluates cursor client coordinates and flips the bar between
        /// default bottom-left and dodging bottom-right.
        /// </summary>
        public bool UpdateCursorDodge(Point cursorClient)
        {
            Control targetHost = _owner ?? this.Parent;
            if (targetHost == null) return false;

            int hostW = targetHost.ClientSize.Width;
            int hostH = targetHost.ClientSize.Height;
            int w = this.Width;
            int h = this.Height;
            if (hostW <= 0 || hostH <= 0 || w <= 0 || h <= 0) return false;

            int dodgeMargin = (int)(24 * _dpiScale);
            int dodgeTop = hostH - h - dodgeMargin;

            // Stable non-overlapping corner danger zones that prevent wide-box flip-flops
            int dodgeW = Math.Min(w, (int)(320 * _dpiScale));
            int leftThresholdX = dodgeW + dodgeMargin;
            int rightThresholdX = hostW - dodgeW - dodgeMargin;

            bool cursorInLeftArea = (cursorClient.X < leftThresholdX) && (cursorClient.Y > dodgeTop);
            bool cursorInRightArea = (cursorClient.X > rightThresholdX) && (cursorClient.Y > dodgeTop);

            bool targetFlipped = _isFlippedRight;
            if (_isFlippedRight)
            {
                if (cursorInRightArea)
                {
                    targetFlipped = false; // Cursor entered right corner -> dodge back to left
                }
                else if (!cursorInLeftArea)
                {
                    targetFlipped = false; // Cursor left the bottom-left danger zone -> return to default left
                }
            }
            else
            {
                if (cursorInLeftArea)
                {
                    targetFlipped = true; // Cursor entered bottom-left danger zone -> dodge to right
                }
            }

            if (targetFlipped != _isFlippedRight)
            {
                _isFlippedRight = targetFlipped;
                UpdateBubbleLayout();
                return true;
            }
            return false;
        }

        private void StartExpandAnimation()
        {
            Control targetHost = _owner ?? this.Parent;
            if (targetHost == null || string.IsNullOrEmpty(_statusText)) return;

            int hostW = targetHost.ClientSize.Width;
            int paddingH = (int)(10 * _dpiScale);
            Size textSize = TextRenderer.MeasureText(_statusText, _statusFont, Size.Empty, TextFormatFlags.NoPrefix);
            int desiredW = textSize.Width + (paddingH * 2);
            int fullMaxW = Math.Max(100, hostW - 20);
            _targetW = Math.Min(fullMaxW, desiredW);

            if (_targetW > this.Width)
            {
                _currentAnimatedW = this.Width;
                if (_expandAnimTimer != null) _expandAnimTimer.Start();
            }
            else
            {
                UpdateBubbleLayout();
            }
        }

        private void OnAnimTick()
        {
            if (!this.Visible || !_isHoverExpanded)
            {
                if (_expandAnimTimer != null) _expandAnimTimer.Stop();
                return;
            }

            int diff = _targetW - _currentAnimatedW;
            if (diff <= 3)
            {
                _currentAnimatedW = _targetW;
                if (_expandAnimTimer != null) _expandAnimTimer.Stop();
            }
            else
            {
                int step = Math.Max(8, (int)(diff * 0.35f));
                _currentAnimatedW += step;
            }

            ApplyBoundsForWidth(_currentAnimatedW);
            this.Invalidate();
        }

        private void ApplyBoundsForWidth(int w)
        {
            Control targetHost = _owner ?? this.Parent;
            if (targetHost == null) return;
            int hostW = targetHost.ClientSize.Width;
            int hostH = targetHost.ClientSize.Height;
            int h = Math.Max(22, (int)(24 * _dpiScale));

            int localX = _isFlippedRight ? (hostW - w) : 0;
            int localY = hostH - h;

            if (_owner != null)
            {
                Point screenPos = _owner.PointToScreen(new Point(localX, localY));
                this.SetBounds(screenPos.X, screenPos.Y, w, h);
            }
            else
            {
                this.SetBounds(localX, localY, w, h);
            }
        }

        /// <summary>
        /// Recalculates size and position based on text content and parent dimensions,
        /// docked flush to the bottom edge and flipping corners based on cursor position.
        /// </summary>
        public void UpdateBubbleLayout()
        {
            Control targetHost = _owner ?? this.Parent;
            if (targetHost == null || string.IsNullOrEmpty(_statusText)) return;

            Form formHost = targetHost as Form;
            if (formHost != null && formHost.WindowState == FormWindowState.Minimized)
            {
                if (this.Visible) this.Hide();
                return;
            }

            int hostW = targetHost.ClientSize.Width;
            int hostH = targetHost.ClientSize.Height;
            if (hostW <= 0 || hostH <= 0) return;

            int paddingH = (int)(10 * _dpiScale);
            int h = Math.Max(22, (int)(24 * _dpiScale));

            // Measure text without stripping '&' or mnemonic symbols
            Size textSize = TextRenderer.MeasureText(_statusText, _statusFont, Size.Empty, TextFormatFlags.NoPrefix);
            int desiredW = textSize.Width + (paddingH * 2);

            int baseMaxW = Math.Min(hostW - 20, (int)(480 * _dpiScale));
            int fullMaxW = Math.Max(100, hostW - 20);

            int w = _isHoverExpanded ? Math.Min(fullMaxW, desiredW) : Math.Min(baseMaxW, desiredW);
            _targetW = w;

            if (_expandAnimTimer != null && _expandAnimTimer.Enabled)
            {
                ApplyBoundsForWidth(_currentAnimatedW);
            }
            else
            {
                _currentAnimatedW = w;
                ApplyBoundsForWidth(w);
            }

            if (this.Region != null)
            {
                this.Region.Dispose();
                this.Region = null;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            CheckCursorDodge();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // Click to dismiss
            this.Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (string.IsNullOrEmpty(_statusText)) return;

            Graphics g = e.Graphics;

            // Flat rectangular box docked flush to bottom (as in sketch)
            Rectangle bounds = new Rectangle(0, 0, this.Width, this.Height);

            // 1. Sleek dark background
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(245, 32, 33, 36)))
            {
                g.FillRectangle(bgBrush, bounds);
            }

            // 2. Crisp 1px border
            using (Pen borderPen = new Pen(MaterialColors.Separator, 1))
            {
                g.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
            }

            int curX = (int)(10 * _dpiScale);

            // 3. Draw status / preview URL text (preserving '&' and special symbols)
            int textW = this.Width - (curX * 2);
            if (textW > 0)
            {
                Rectangle textRect = new Rectangle(curX, 0, textW, this.Height);
                TextRenderer.DrawText(g, _statusText, _statusFont, textRect, MaterialColors.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DetachFromOwner();
                if (_hideTimer != null) { try { _hideTimer.Dispose(); } catch { } _hideTimer = null; }
                if (_mousePollTimer != null) { try { _mousePollTimer.Dispose(); } catch { } _mousePollTimer = null; }
                if (_hoverExpandTimer != null) { try { _hoverExpandTimer.Dispose(); } catch { } _hoverExpandTimer = null; }
                if (_expandAnimTimer != null) { try { _expandAnimTimer.Dispose(); } catch { } _expandAnimTimer = null; }
                if (_statusFont != null) { try { _statusFont.Dispose(); } catch { } _statusFont = null; }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Backwards compatibility alias for WbBottomBar.
    /// </summary>
    public class FloatingStatusBubble : WbBottomBar
    {
    }
}
