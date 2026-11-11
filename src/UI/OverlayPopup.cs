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
    /// Non-activating floating overlay popup component.
    /// Shares the identical sleek rectangular design language of FullScreenExitOverlay:
    /// - Solid rectangular canvas (#202124)
    /// - Crisp 1px outer borders (#3C4043)
    /// - 2px bottom accent border (ThemeManager.CurrentAccentColor)
    /// - Non-activating topmost window (WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST)
    /// - Smooth slide down/up animation from top center of owner form
    /// </summary>
    public class OverlayPopup : Form
    {
        private Form _owner;
        private string _message;
        private string _iconName;
        private string _buttonText;
        private Action _onButtonClicked;
        private int _autoDismissMs;

        // Countdown Mode Fields
        private bool _isCountdown = false;
        private int _remainingSeconds = 0;
        private Action _onCountdownComplete;
        private Action _onCountdownCancel;
        private Timer _countdownTimer;

        // Visual & Layout
        private float _dpiScale = 1.0f;
        private Rectangle _actionBtnRect = Rectangle.Empty;
        private bool _isHoverActionBtn = false;
        private bool _isPressedActionBtn = false;

        private Font _messageFont;
        private Font _btnFont;

        // Slide Animation
        private bool _isSliding = false;
        private int _currentSlideY = 0;
        private int _targetSlideY = 0;
        private Timer _slideTimer;
        private Timer _dismissTimer;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public OverlayPopup(
            Form owner,
            string message,
            string iconName = "check",
            string buttonText = null,
            Action onButtonClicked = null,
            int autoDismissMs = 0)
        {
            _owner = owner;
            _message = message ?? string.Empty;
            _iconName = iconName;
            _buttonText = buttonText;
            _onButtonClicked = onButtonClicked;
            _autoDismissMs = autoDismissMs;

            InitializeForm();
        }

        public OverlayPopup(
            Form owner,
            int countdownSeconds,
            Action onComplete,
            Action onCancel = null)
        {
            _owner = owner;
            _isCountdown = true;
            _remainingSeconds = Math.Max(1, countdownSeconds);
            _onCountdownComplete = onComplete;
            _onCountdownCancel = onCancel;
            _iconName = "camera";
            _buttonText = "Cancel";
            _message = string.Format("Taking screenshot in {0}s...", _remainingSeconds);

            InitializeForm();

            _countdownTimer = new Timer();
            _countdownTimer.Interval = 1000;
            _countdownTimer.Tick += CountdownTimer_Tick;
        }

        private void InitializeForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(32, 33, 36);
            this.Cursor = Cursors.Default;

            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            _dpiScale = DpiHelper.GetDpiScale(this);

            float msgFontSize = Math.Max(9.5f, 10.5f * _dpiScale);
            _messageFont = new Font("Segoe UI", msgFontSize, FontStyle.Regular);
            _btnFont = new Font("Segoe UI", Math.Max(8.5f, 9.5f * _dpiScale), FontStyle.Bold);

            MeasureAndSetBounds();

            if (_owner != null)
            {
                _owner.LocationChanged += Owner_BoundsChanged;
                _owner.SizeChanged += Owner_BoundsChanged;
                _owner.VisibleChanged += Owner_VisibleChanged;
                _owner.FormClosing += Owner_FormClosing;
            }

            lock (_activePopups)
            {
                _activePopups.Add(this);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOPMOST = 0x00000008;

                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        private int GetTopBarHeight()
        {
            if (_owner != null)
            {
                int totalHeight = 0;
                foreach (Control c in _owner.Controls)
                {
                    if (c != null && c.Visible && c.Dock == DockStyle.Top)
                    {
                        totalHeight += c.Height;
                    }
                }
                if (totalHeight > 0) return totalHeight;
            }
            return 0;
        }

        private void MeasureAndSetBounds()
        {
            int h = (int)Math.Round(38 * _dpiScale);

            int iconWidth = !string.IsNullOrEmpty(_iconName) ? (int)(18 * _dpiScale + 10 * _dpiScale) : 0;
            int padLeft = (int)(14 * _dpiScale);
            int padRight = (int)(14 * _dpiScale);

            int btnWidth = 0;
            if (!string.IsNullOrEmpty(_buttonText))
            {
                using (Graphics g = this.CreateGraphics())
                {
                    SizeF btnTextSize = g.MeasureString(_buttonText, _btnFont);
                    btnWidth = (int)Math.Ceiling(btnTextSize.Width) + (int)(22 * _dpiScale);
                }
            }

            int msgWidth = 0;
            using (Graphics g = this.CreateGraphics())
            {
                SizeF msgSize = g.MeasureString(_message, _messageFont);
                msgWidth = (int)Math.Ceiling(msgSize.Width) + 8;
            }

            int w = padLeft + iconWidth + msgWidth + (btnWidth > 0 ? btnWidth + (int)(14 * _dpiScale) : 0) + padRight;
            w = Math.Max((int)(200 * _dpiScale), w);

            int x = 0;
            int y = 0;
            if (_owner != null && !_owner.IsDisposed)
            {
                Rectangle ownerScreen = _owner.RectangleToScreen(_owner.ClientRectangle);
                x = ownerScreen.Left + (ownerScreen.Width - w) / 2;

                int topBarH = GetTopBarHeight();
                int gapUnderTopBar = (int)(6 * _dpiScale);
                y = ownerScreen.Top + topBarH + gapUnderTopBar;
            }
            else
            {
                Rectangle scr = Screen.PrimaryScreen.Bounds;
                x = (scr.Width - w) / 2;
                y = scr.Top + (int)(60 * _dpiScale);
            }

            _targetSlideY = y;
            if (!_isSliding)
            {
                _currentSlideY = y;
            }

            this.Bounds = new Rectangle(x, _currentSlideY, w, h);

            if (btnWidth > 0)
            {
                int btnH = (int)(24 * _dpiScale);
                int btnX = w - padRight - btnWidth;
                int btnY = (h - 2 - btnH) / 2;
                _actionBtnRect = new Rectangle(btnX, btnY, btnWidth, btnH);
            }
            else
            {
                _actionBtnRect = Rectangle.Empty;
            }
        }

        public void ShowPopup()
        {
            if (this.IsDisposed) return;

            int topBarH = GetTopBarHeight();
            int startHiddenY = (_owner != null) ? _owner.RectangleToScreen(_owner.ClientRectangle).Top + topBarH - this.Height : _targetSlideY - this.Height;
            _currentSlideY = startHiddenY;
            this.Location = new Point(this.Location.X, _currentSlideY);

            this.Show();
            EnsureTopMost();

            _isSliding = true;
            _slideTimer = new Timer();
            _slideTimer.Interval = 15;
            _slideTimer.Tick += SlideInTimer_Tick;
            _slideTimer.Start();

            if (_isCountdown && _countdownTimer != null)
            {
                _countdownTimer.Start();
            }
            else if (_autoDismissMs > 0)
            {
                _dismissTimer = new Timer();
                _dismissTimer.Interval = _autoDismissMs;
                _dismissTimer.Tick += DismissTimer_Tick;
                _dismissTimer.Start();
            }
        }

        private void EnsureTopMost()
        {
            try
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }

        private void SlideInTimer_Tick(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;

            int diff = _targetSlideY - _currentSlideY;
            if (Math.Abs(diff) <= 2)
            {
                _currentSlideY = _targetSlideY;
                this.Location = new Point(this.Location.X, _currentSlideY);
                _slideTimer.Stop();
                _slideTimer.Dispose();
                _slideTimer = null;
                _isSliding = false;
            }
            else
            {
                int step = Math.Max(2, (int)(diff * 0.35f));
                _currentSlideY += step;
                this.Location = new Point(this.Location.X, _currentSlideY);
            }
        }

        private void SlideOutAndClose()
        {
            if (this.IsDisposed) return;

            if (_slideTimer != null)
            {
                _slideTimer.Stop();
                _slideTimer.Dispose();
                _slideTimer = null;
            }

            int topBarH = GetTopBarHeight();
            int hideY = (_owner != null) ? _owner.RectangleToScreen(_owner.ClientRectangle).Top + topBarH - this.Height : _targetSlideY - this.Height;

            _slideTimer = new Timer();
            _slideTimer.Interval = 15;
            _slideTimer.Tick += delegate
            {
                if (this.IsDisposed) return;
                int diff = hideY - _currentSlideY;
                if (Math.Abs(diff) <= 3)
                {
                    _slideTimer.Stop();
                    _slideTimer.Dispose();
                    _slideTimer = null;
                    this.Close();
                }
                else
                {
                    int step = Math.Min(-3, (int)(diff * 0.4f));
                    _currentSlideY += step;
                    this.Location = new Point(this.Location.X, _currentSlideY);
                }
            };
            _slideTimer.Start();
        }

        private void DismissTimer_Tick(object sender, EventArgs e)
        {
            if (_dismissTimer != null)
            {
                _dismissTimer.Stop();
                _dismissTimer.Dispose();
                _dismissTimer = null;
            }
            SlideOutAndClose();
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;

            _remainingSeconds--;
            if (_remainingSeconds <= 0)
            {
                if (_countdownTimer != null)
                {
                    _countdownTimer.Stop();
                    _countdownTimer.Dispose();
                    _countdownTimer = null;
                }

                Action cb = _onCountdownComplete;
                this.Close();

                if (cb != null)
                {
                    cb();
                }
            }
            else
            {
                _message = string.Format("Taking screenshot in {0}s...", _remainingSeconds);
                MeasureAndSetBounds();
                this.Invalidate();
            }
        }

        public void CancelCountdown()
        {
            if (_countdownTimer != null)
            {
                _countdownTimer.Stop();
                _countdownTimer.Dispose();
                _countdownTimer = null;
            }

            Action cb = _onCountdownCancel;
            SlideOutAndClose();

            if (cb != null)
            {
                cb();
            }
        }

        private void Owner_BoundsChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            MeasureAndSetBounds();
        }

        private void Owner_VisibleChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed || _owner == null) return;
            this.Visible = _owner.Visible;
            if (this.Visible) EnsureTopMost();
        }

        private void Owner_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (!this.IsDisposed)
                {
                    this.Close();
                }
            }
            catch { }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && _actionBtnRect.Contains(e.Location))
            {
                _isPressedActionBtn = true;
                this.Invalidate(_actionBtnRect);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _isPressedActionBtn)
            {
                _isPressedActionBtn = false;
                this.Invalidate(_actionBtnRect);

                if (_actionBtnRect.Contains(e.Location))
                {
                    if (_isCountdown)
                    {
                        CancelCountdown();
                    }
                    else if (_onButtonClicked != null)
                    {
                        _onButtonClicked();
                        SlideOutAndClose();
                    }
                    else
                    {
                        SlideOutAndClose();
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hoverBtn = _actionBtnRect.Contains(e.Location);
            if (hoverBtn != _isHoverActionBtn)
            {
                _isHoverActionBtn = hoverBtn;
                this.Cursor = hoverBtn ? Cursors.Hand : Cursors.Default;
                this.Invalidate(_actionBtnRect);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_isHoverActionBtn || _isPressedActionBtn)
            {
                _isHoverActionBtn = false;
                _isPressedActionBtn = false;
                this.Cursor = Cursors.Default;
                this.Invalidate(_actionBtnRect);
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

            // 2. Crisp 1px outer borders
            using (Pen borderPen = new Pen(Color.FromArgb(60, 64, 67), 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
            }

            // 3. Crisp 2px bottom accent border
            int accentLineH = Math.Max(2, (int)(2 * _dpiScale));
            using (SolidBrush accentBrush = new SolidBrush(accent))
            {
                g.FillRectangle(accentBrush, 0, h - accentLineH, w, accentLineH);
            }

            // 4. Icon
            int curX = (int)(14 * _dpiScale);
            if (!string.IsNullOrEmpty(_iconName))
            {
                int iconSize = (int)(18 * _dpiScale);
                int iconY = (h - accentLineH - iconSize) / 2;
                Rectangle iconRect = new Rectangle(curX, iconY, iconSize, iconSize);

                Color iconColor = string.Equals(_iconName, "check", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(129, 201, 149) // Green
                    : accent;

                MaterialIcons.DrawIcon(g, _iconName, iconRect, iconColor);
                curX += iconSize + (int)(10 * _dpiScale);
            }

            // 5. Message Text (centered vertically)
            int textRight = _actionBtnRect.Width > 0 ? _actionBtnRect.Left - (int)(10 * _dpiScale) : w - (int)(14 * _dpiScale);
            Rectangle textRect = new Rectangle(curX, 0, Math.Max(0, textRight - curX), h - accentLineH);

            TextRenderer.DrawText(
                g,
                _message,
                _messageFont,
                textRect,
                Color.FromArgb(232, 234, 237),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            // 6. Action Button (Cancel)
            if (_actionBtnRect.Width > 0 && !string.IsNullOrEmpty(_buttonText))
            {
                DrawActionButton(g, _actionBtnRect, accent);
            }
        }

        private void DrawActionButton(Graphics g, Rectangle rect, Color accent)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;

            Color btnBg;
            Color btnBorder;
            Color btnTextColor;

            if (_isPressedActionBtn)
            {
                btnBg = Color.FromArgb(Math.Max(0, accent.R - 35), Math.Max(0, accent.G - 35), Math.Max(0, accent.B - 35));
                btnBorder = btnBg;
                btnTextColor = UITheme.GetContrastTextColor(btnBg);
            }
            else if (_isHoverActionBtn)
            {
                btnBg = accent;
                btnBorder = accent;
                btnTextColor = UITheme.GetContrastTextColor(btnBg);
            }
            else
            {
                btnBg = Color.FromArgb(41, 42, 45);
                btnBorder = Color.FromArgb(70, 72, 77);
                btnTextColor = Color.FromArgb(232, 234, 237);
            }

            using (SolidBrush b = new SolidBrush(btnBg))
            {
                g.FillRectangle(b, rect);
            }

            using (Pen p = new Pen(btnBorder, 1f))
            {
                g.DrawRectangle(p, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }

            TextRenderer.DrawText(
                g,
                _buttonText,
                _btnFont,
                rect,
                btnTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        // ==========================================
        // Static Helpers for Reusable Invocations
        // ==========================================

        private static readonly System.Collections.Generic.List<OverlayPopup> _activePopups = new System.Collections.Generic.List<OverlayPopup>();

        public static void DismissAll()
        {
            OverlayPopup[] popups;
            lock (_activePopups)
            {
                popups = _activePopups.ToArray();
                _activePopups.Clear();
            }
            for (int i = 0; i < popups.Length; i++)
            {
                try
                {
                    if (popups[i] != null && !popups[i].IsDisposed)
                    {
                        popups[i].Hide();
                        popups[i].Close();
                    }
                }
                catch { }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            lock (_activePopups)
            {
                _activePopups.Remove(this);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_owner != null)
                {
                    try
                    {
                        _owner.LocationChanged -= Owner_BoundsChanged;
                        _owner.SizeChanged -= Owner_BoundsChanged;
                        _owner.VisibleChanged -= Owner_VisibleChanged;
                        _owner.FormClosing -= Owner_FormClosing;
                    }
                    catch { }
                    _owner = null;
                }
                if (_dismissTimer != null) { try { _dismissTimer.Stop(); _dismissTimer.Dispose(); } catch { } _dismissTimer = null; }
                if (_countdownTimer != null) { try { _countdownTimer.Stop(); _countdownTimer.Dispose(); } catch { } _countdownTimer = null; }
                if (_slideTimer != null) { try { _slideTimer.Stop(); _slideTimer.Dispose(); } catch { } _slideTimer = null; }
                if (_messageFont != null) { try { _messageFont.Dispose(); } catch { } _messageFont = null; }
                if (_btnFont != null) { try { _btnFont.Dispose(); } catch { } _btnFont = null; }
                lock (_activePopups)
                {
                    _activePopups.Remove(this);
                }
            }
            base.Dispose(disposing);
        }

        public static OverlayPopup ShowToast(Control target, string message, string iconName = "check", int durationMs = 2000)
        {
            Form owner = target != null ? target.FindForm() : Form.ActiveForm;
            if (owner == null) return null;

            OverlayPopup popup = new OverlayPopup(owner, message, iconName, null, null, durationMs);
            popup.ShowPopup();
            return popup;
        }

        public static OverlayPopup ShowCountdown(Form owner, int seconds, Action onComplete, Action onCancel = null)
        {
            if (owner == null) return null;

            OverlayPopup popup = new OverlayPopup(owner, seconds, onComplete, onCancel);
            popup.ShowPopup();
            return popup;
        }
    }

    /// <summary>
    /// Backwards compatibility alias for OverlayPopup.
    /// </summary>
    public class UniversalOverlayPopup : OverlayPopup
    {
        public UniversalOverlayPopup(
            Form owner,
            string message,
            string iconName = "check",
            string buttonText = null,
            Action onButtonClicked = null,
            int autoDismissMs = 0)
            : base(owner, message, iconName, buttonText, onButtonClicked, autoDismissMs)
        {
        }

        public UniversalOverlayPopup(
            Form owner,
            int countdownSeconds,
            Action onComplete,
            Action onCancel = null)
            : base(owner, countdownSeconds, onComplete, onCancel)
        {
        }
    }
}
