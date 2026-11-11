using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using wb.Core;
using wb.UI;

namespace wb.Screenshot
{
    public class ScreenshotSelectionOverlayForm : Form
    {
        private float _dpiScale = 1.0f;
        private Bitmap _screenBitmap;
        private int _scrollX = 0;
        private int _scrollY = 0;
        private Point _startPoint = Point.Empty;
        private Point _currentPoint = Point.Empty;
        private bool _isDragging = false;
        private bool _hasSelection = false;
        private Rectangle _selectedRect = Rectangle.Empty;

        private Rectangle _captureButtonRect = Rectangle.Empty;
        private Rectangle _cancelButtonRect = Rectangle.Empty;
        private bool _isHoverCapture = false;
        private bool _isHoverCancel = false;

        private Timer _autoScrollTimer;
        private Point _lastMousePoint = Point.Empty;

        public Bitmap SelectedBitmap { get; private set; }
        public bool IsConfirmed { get; private set; }

        public ScreenshotSelectionOverlayForm() : this(Rectangle.Empty, null, Point.Empty)
        {
        }

        public ScreenshotSelectionOverlayForm(Rectangle canvasBounds) : this(canvasBounds, null, Point.Empty)
        {
        }

        public ScreenshotSelectionOverlayForm(Rectangle canvasBounds, Bitmap fullBitmap, Point initialScrollOffset)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Cursor = Cursors.Cross;

            _dpiScale = DpiHelper.GetDpiScale(this);

            Rectangle targetBounds = canvasBounds;
            if (targetBounds.Width <= 0 || targetBounds.Height <= 0)
            {
                targetBounds = Screen.PrimaryScreen.Bounds;
            }
            this.Bounds = targetBounds;

            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            this.DoubleBuffered = true;

            _autoScrollTimer = new Timer();
            _autoScrollTimer.Interval = 20;
            _autoScrollTimer.Tick += AutoScrollTimer_Tick;

            if (fullBitmap != null)
            {
                _screenBitmap = fullBitmap;
                int maxScrollX = Math.Max(0, _screenBitmap.Width - this.ClientSize.Width);
                int maxScrollY = Math.Max(0, _screenBitmap.Height - this.ClientSize.Height);
                _scrollX = Math.Max(0, Math.Min(maxScrollX, initialScrollOffset.X));
                _scrollY = Math.Max(0, Math.Min(maxScrollY, initialScrollOffset.Y));
            }
            else
            {
                CaptureVirtualScreen(targetBounds);
                _scrollX = 0;
                _scrollY = 0;
            }
        }

        private void CaptureVirtualScreen(Rectangle bounds)
        {
            try
            {
                _screenBitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(_screenBitmap))
                {
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }
            }
            catch
            {
                _screenBitmap = null;
            }
        }

        private void SetScroll(int newX, int newY)
        {
            int maxX = Math.Max(0, (_screenBitmap != null ? _screenBitmap.Width : 0) - this.ClientSize.Width);
            int maxY = Math.Max(0, (_screenBitmap != null ? _screenBitmap.Height : 0) - this.ClientSize.Height);

            int clampedX = Math.Max(0, Math.Min(maxX, newX));
            int clampedY = Math.Max(0, Math.Min(maxY, newY));

            if (clampedX != _scrollX || clampedY != _scrollY)
            {
                _scrollX = clampedX;
                _scrollY = clampedY;
                this.Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
            else if (e.Alt && e.KeyCode == Keys.F4)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                if (this.Owner != null && !this.Owner.IsDisposed)
                {
                    this.Owner.Close();
                }
            }
            else if (e.KeyCode == Keys.Enter && _hasSelection && _selectedRect.Width > 5 && _selectedRect.Height > 5)
            {
                ConfirmCapture();
            }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown)
            {
                int step = (e.KeyCode == Keys.PageDown) ? (int)(this.ClientSize.Height * 0.75f) : (int)(48 * _dpiScale);
                SetScroll(_scrollX, _scrollY + step);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.PageUp)
            {
                int step = (e.KeyCode == Keys.PageUp) ? (int)(this.ClientSize.Height * 0.75f) : (int)(48 * _dpiScale);
                SetScroll(_scrollX, _scrollY - step);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right)
            {
                SetScroll(_scrollX + (int)(48 * _dpiScale), _scrollY);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Left)
            {
                SetScroll(_scrollX - (int)(48 * _dpiScale), _scrollY);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Home)
            {
                SetScroll(_scrollX, 0);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.End)
            {
                int maxY = Math.Max(0, (_screenBitmap != null ? _screenBitmap.Height : 0) - this.ClientSize.Height);
                SetScroll(_scrollX, maxY);
                e.Handled = true;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_screenBitmap == null) return;

            int scrollDelta = (int)(64 * _dpiScale);
            int step = (e.Delta > 0) ? -scrollDelta : scrollDelta;
            bool isShift = (ModifierKeys & Keys.Shift) == Keys.Shift;

            if (isShift)
            {
                SetScroll(_scrollX + step, _scrollY);
            }
            else
            {
                SetScroll(_scrollX, _scrollY + step);
            }

            if (_isDragging)
            {
                int maxW = _screenBitmap != null ? _screenBitmap.Width : this.ClientSize.Width;
                int maxH = _screenBitmap != null ? _screenBitmap.Height : this.ClientSize.Height;
                int docX = Math.Max(0, Math.Min(maxW, e.X + _scrollX));
                int docY = Math.Max(0, Math.Min(maxH, e.Y + _scrollY));
                _currentPoint = new Point(docX, docY);
                _selectedRect = GetNormalizedRect(_startPoint, _currentPoint);
                _hasSelection = _selectedRect.Width > 2 && _selectedRect.Height > 2;
            }

            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                if (_hasSelection)
                {
                    if (_captureButtonRect.Contains(e.Location))
                    {
                        ConfirmCapture();
                        return;
                    }
                    if (_cancelButtonRect.Contains(e.Location))
                    {
                        this.DialogResult = DialogResult.Cancel;
                        this.Close();
                        return;
                    }
                }

                _startPoint = new Point(e.X + _scrollX, e.Y + _scrollY);
                _currentPoint = _startPoint;
                _isDragging = true;
                _hasSelection = false;
                _selectedRect = Rectangle.Empty;
                this.Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (_hasSelection)
                {
                    _hasSelection = false;
                    _selectedRect = Rectangle.Empty;
                    this.Invalidate();
                }
                else
                {
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _lastMousePoint = e.Location;

            if (_isDragging)
            {
                int maxW = _screenBitmap != null ? _screenBitmap.Width : this.ClientSize.Width;
                int maxH = _screenBitmap != null ? _screenBitmap.Height : this.ClientSize.Height;

                int docX = Math.Max(0, Math.Min(maxW, e.X + _scrollX));
                int docY = Math.Max(0, Math.Min(maxH, e.Y + _scrollY));
                _currentPoint = new Point(docX, docY);
                _selectedRect = GetNormalizedRect(_startPoint, _currentPoint);
                _hasSelection = _selectedRect.Width > 2 && _selectedRect.Height > 2;

                int edgeMargin = (int)(32 * _dpiScale);
                int maxX = Math.Max(0, (_screenBitmap != null ? _screenBitmap.Width : 0) - this.ClientSize.Width);
                int maxY = Math.Max(0, (_screenBitmap != null ? _screenBitmap.Height : 0) - this.ClientSize.Height);

                bool nearEdge = (e.X <= edgeMargin && _scrollX > 0) ||
                                (e.X >= this.ClientSize.Width - edgeMargin && _scrollX < maxX) ||
                                (e.Y <= edgeMargin && _scrollY > 0) ||
                                (e.Y >= this.ClientSize.Height - edgeMargin && _scrollY < maxY);

                if (nearEdge && !_autoScrollTimer.Enabled)
                {
                    _autoScrollTimer.Start();
                }
                else if (!nearEdge && _autoScrollTimer.Enabled)
                {
                    _autoScrollTimer.Stop();
                }

                this.Invalidate();
            }
            else if (_hasSelection)
            {
                bool prevCapture = _isHoverCapture;
                bool prevCancel = _isHoverCancel;
                _isHoverCapture = _captureButtonRect.Contains(e.Location);
                _isHoverCancel = _cancelButtonRect.Contains(e.Location);

                if (_isHoverCapture || _isHoverCancel)
                {
                    this.Cursor = Cursors.Hand;
                }
                else
                {
                    this.Cursor = Cursors.Cross;
                }

                if (prevCapture != _isHoverCapture || prevCancel != _isHoverCancel)
                {
                    this.Invalidate();
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_isDragging && e.Button == MouseButtons.Left)
            {
                _isDragging = false;
                if (_autoScrollTimer != null) _autoScrollTimer.Stop();

                int maxW = _screenBitmap != null ? _screenBitmap.Width : this.ClientSize.Width;
                int maxH = _screenBitmap != null ? _screenBitmap.Height : this.ClientSize.Height;

                int docX = Math.Max(0, Math.Min(maxW, e.X + _scrollX));
                int docY = Math.Max(0, Math.Min(maxH, e.Y + _scrollY));
                _currentPoint = new Point(docX, docY);
                _selectedRect = GetNormalizedRect(_startPoint, _currentPoint);

                if (_selectedRect.Width > 5 && _selectedRect.Height > 5)
                {
                    _hasSelection = true;
                }
                else
                {
                    _hasSelection = false;
                    _selectedRect = Rectangle.Empty;
                }
                this.Invalidate();
            }
        }

        private void AutoScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!_isDragging || _screenBitmap == null)
            {
                _autoScrollTimer.Stop();
                return;
            }

            int edgeMargin = (int)(32 * _dpiScale);
            int stepBase = Math.Max(4, (int)(14 * _dpiScale));
            int dx = 0;
            int dy = 0;

            if (_lastMousePoint.X <= edgeMargin)
            {
                int dist = Math.Max(0, edgeMargin - _lastMousePoint.X);
                dx = -Math.Max(stepBase, dist * 2);
            }
            else if (_lastMousePoint.X >= this.ClientSize.Width - edgeMargin)
            {
                int dist = Math.Max(0, _lastMousePoint.X - (this.ClientSize.Width - edgeMargin));
                dx = Math.Max(stepBase, dist * 2);
            }

            if (_lastMousePoint.Y <= edgeMargin)
            {
                int dist = Math.Max(0, edgeMargin - _lastMousePoint.Y);
                dy = -Math.Max(stepBase, dist * 2);
            }
            else if (_lastMousePoint.Y >= this.ClientSize.Height - edgeMargin)
            {
                int dist = Math.Max(0, _lastMousePoint.Y - (this.ClientSize.Height - edgeMargin));
                dy = Math.Max(stepBase, dist * 2);
            }

            if (dx != 0 || dy != 0)
            {
                int oldX = _scrollX;
                int oldY = _scrollY;
                SetScroll(_scrollX + dx, _scrollY + dy);

                int maxW = _screenBitmap.Width;
                int maxH = _screenBitmap.Height;
                int docX = Math.Max(0, Math.Min(maxW, _lastMousePoint.X + _scrollX));
                int docY = Math.Max(0, Math.Min(maxH, _lastMousePoint.Y + _scrollY));
                _currentPoint = new Point(docX, docY);
                _selectedRect = GetNormalizedRect(_startPoint, _currentPoint);
                _hasSelection = _selectedRect.Width > 2 && _selectedRect.Height > 2;

                if (oldX == _scrollX && oldY == _scrollY)
                {
                    _autoScrollTimer.Stop();
                }
                else
                {
                    this.Invalidate();
                }
            }
            else
            {
                _autoScrollTimer.Stop();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_hasSelection && _selectedRect.Contains(new Point(e.X + _scrollX, e.Y + _scrollY)) && _selectedRect.Width > 5 && _selectedRect.Height > 5)
            {
                ConfirmCapture();
            }
        }

        private void ConfirmCapture()
        {
            if (_screenBitmap == null || _selectedRect.Width <= 0 || _selectedRect.Height <= 0)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                return;
            }

            try
            {
                Rectangle clamped = Rectangle.Intersect(new Rectangle(0, 0, _screenBitmap.Width, _screenBitmap.Height), _selectedRect);
                if (clamped.Width > 0 && clamped.Height > 0)
                {
                    Bitmap cropped = new Bitmap(clamped.Width, clamped.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(cropped))
                    {
                        g.DrawImage(_screenBitmap, new Rectangle(0, 0, clamped.Width, clamped.Height), clamped, GraphicsUnit.Pixel);
                    }
                    this.SelectedBitmap = cropped;
                    this.IsConfirmed = true;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                    return;
                }
            }
            catch
            {
            }

            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private static Rectangle GetNormalizedRect(Point p1, Point p2)
        {
            int x = Math.Min(p1.X, p2.X);
            int y = Math.Min(p1.Y, p2.Y);
            int w = Math.Abs(p1.X - p2.X);
            int h = Math.Abs(p1.Y - p2.Y);
            return new Rectangle(x, y, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 1. Draw base frozen screen slice
            if (_screenBitmap != null)
            {
                int drawW = Math.Min(this.ClientSize.Width, _screenBitmap.Width - _scrollX);
                int drawH = Math.Min(this.ClientSize.Height, _screenBitmap.Height - _scrollY);
                if (drawW > 0 && drawH > 0)
                {
                    Rectangle srcRect = new Rectangle(_scrollX, _scrollY, drawW, drawH);
                    Rectangle destRect = new Rectangle(0, 0, drawW, drawH);
                    g.DrawImage(_screenBitmap, destRect, srcRect, GraphicsUnit.Pixel);
                }
            }

            // 2. Map document selected rectangle to viewport
            Rectangle viewRect = new Rectangle(_selectedRect.X - _scrollX, _selectedRect.Y - _scrollY, _selectedRect.Width, _selectedRect.Height);
            Rectangle visibleSelectedRect = Rectangle.Intersect(this.ClientRectangle, viewRect);

            // 3. Draw darkened overlay scrim
            using (SolidBrush darkBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                if (_hasSelection && !visibleSelectedRect.IsEmpty)
                {
                    using (Region darkRegion = new Region(this.ClientRectangle))
                    {
                        darkRegion.Exclude(visibleSelectedRect);
                        g.FillRegion(darkBrush, darkRegion);
                    }

                    // 4. Highlight selected rectangle border
                    using (Pen whitePen = new Pen(Color.FromArgb(160, 255, 255, 255), 1f))
                    {
                        g.DrawRectangle(whitePen, viewRect.X - 1, viewRect.Y - 1, viewRect.Width + 2, viewRect.Height + 2);
                    }

                    Color accent = MaterialColors.AccentBlue;
                    using (Pen accentPen = new Pen(accent, Math.Max(1.5f, 2f * _dpiScale)))
                    {
                        g.DrawRectangle(accentPen, viewRect);
                    }

                    // 5. Draw dimension badge
                    string dimText = string.Format("{0} × {1} px", _selectedRect.Width, _selectedRect.Height);
                    using (Font font = new Font("Segoe UI", Math.Max(9f, 9.5f * _dpiScale), FontStyle.Bold))
                    {
                        SizeF size = g.MeasureString(dimText, font);
                        int badgeW = (int)Math.Ceiling(size.Width) + (int)(16 * _dpiScale);
                        int badgeH = (int)Math.Ceiling(size.Height) + (int)(8 * _dpiScale);

                        Rectangle badgeAnchor = !visibleSelectedRect.IsEmpty ? visibleSelectedRect : viewRect;
                        int badgeX = Math.Max(10, Math.Min(this.ClientSize.Width - badgeW - 10, badgeAnchor.Left));
                        int badgeY = badgeAnchor.Top - badgeH - (int)(6 * _dpiScale);
                        if (badgeY < 10) badgeY = Math.Min(this.ClientSize.Height - badgeH - 10, badgeAnchor.Top + (int)(6 * _dpiScale));
                        if (badgeY < 10) badgeY = 10;

                        Rectangle badgeRect = new Rectangle(badgeX, badgeY, badgeW, badgeH);
                        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(245, 20, 21, 24)))
                        using (Pen bPen = new Pen(Color.FromArgb(160, accent), 1f))
                        using (SolidBrush textBrush = new SolidBrush(Color.White))
                        using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            g.FillRectangle(bgBrush, badgeRect);
                            g.DrawRectangle(bPen, badgeRect);
                            g.DrawString(dimText, font, textBrush, badgeRect, sf);
                        }
                    }

                    // 6. Draw action buttons toolbar near bottom right of visible selection
                    int btnH = (int)(28 * _dpiScale);
                    int btnW = (int)(96 * _dpiScale);
                    int btnGap = (int)(8 * _dpiScale);
                    int barPad = (int)(8 * _dpiScale);
                    int barW = btnW * 2 + btnGap + barPad * 2;
                    int barH = btnH + barPad * 2;
                    int margin = (int)(10 * _dpiScale);

                    Rectangle btnAnchor = !visibleSelectedRect.IsEmpty ? visibleSelectedRect : this.ClientRectangle;

                    int barX = btnAnchor.Right - barW;
                    if (barX < btnAnchor.Left) barX = btnAnchor.Left;

                    // Try placing below selection first
                    int barY = btnAnchor.Bottom + (int)(8 * _dpiScale);

                    // If below would overflow viewport bottom edge, place above bottom of selection
                    if (barY + barH > this.ClientSize.Height - margin)
                    {
                        barY = btnAnchor.Bottom - barH - (int)(10 * _dpiScale);
                    }

                    // Strictly clamp inside client viewport so buttons are NEVER missing or clipped
                    if (barY + barH > this.ClientSize.Height - margin)
                    {
                        barY = this.ClientSize.Height - barH - margin;
                    }
                    if (barY < margin)
                    {
                        barY = margin;
                    }

                    if (barX + barW > this.ClientSize.Width - margin)
                    {
                        barX = this.ClientSize.Width - barW - margin;
                    }
                    if (barX < margin)
                    {
                        barX = margin;
                    }

                    Rectangle barRect = new Rectangle(barX, barY, barW, barH);
                    using (SolidBrush barBg = new SolidBrush(Color.FromArgb(245, 32, 33, 36)))
                    using (Pen barPen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f))
                    {
                        g.FillRectangle(barBg, barRect);
                        g.DrawRectangle(barPen, barRect);
                    }

                    _captureButtonRect = new Rectangle(barX + barPad, barY + barPad, btnW, btnH);
                    _cancelButtonRect = new Rectangle(barX + barPad + btnW + btnGap, barY + barPad, btnW, btnH);

                    // Draw Capture Button with vector checkmark icon
                    Color baseAccent = MaterialColors.AccentBlue;
                    float baseLum = 0.299f * baseAccent.R + 0.587f * baseAccent.G + 0.114f * baseAccent.B;
                    Color capBg = _isHoverCapture
                        ? (baseLum > 110f ? ControlPaint.Dark(baseAccent, 0.1f) : ControlPaint.Light(baseAccent, 0.15f))
                        : baseAccent;
                    Color capFg = UITheme.GetContrastTextColor(capBg);

                    using (SolidBrush cb = new SolidBrush(capBg))
                    {
                        g.FillRectangle(cb, _captureButtonRect);
                    }
                    int capIconSize = (int)(14 * _dpiScale);
                    int capGap = (int)(6 * _dpiScale);
                    using (Font btnFont = new Font("Segoe UI", Math.Max(8.5f, 9.5f * _dpiScale), FontStyle.Bold))
                    {
                        SizeF tSize = g.MeasureString("Capture", btnFont);
                        int totalContentW = capIconSize + capGap + (int)Math.Ceiling(tSize.Width);
                        int startX = _captureButtonRect.X + Math.Max(0, (_captureButtonRect.Width - totalContentW) / 2);
                        int iconY = _captureButtonRect.Y + (_captureButtonRect.Height - capIconSize) / 2;
                        Rectangle iconRect = new Rectangle(startX, iconY, capIconSize, capIconSize);
                        MaterialIcons.DrawIcon(g, "check", iconRect, capFg);

                        Rectangle textRect = new Rectangle(startX + capIconSize + capGap, _captureButtonRect.Y, (int)Math.Ceiling(tSize.Width) + 4, _captureButtonRect.Height);
                        using (SolidBrush tb = new SolidBrush(capFg))
                        using (StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center })
                        {
                            g.DrawString("Capture", btnFont, tb, textRect, sf);
                        }
                    }

                    // Draw Cancel Button with vector x icon
                    Color canBg = _isHoverCancel ? Color.FromArgb(70, 72, 77) : Color.FromArgb(45, 47, 51);
                    using (SolidBrush cb = new SolidBrush(canBg))
                    {
                        g.FillRectangle(cb, _cancelButtonRect);
                    }
                    using (Pen cp = new Pen(Color.FromArgb(80, 255, 255, 255), 1f))
                    {
                        g.DrawRectangle(cp, _cancelButtonRect.X, _cancelButtonRect.Y, _cancelButtonRect.Width - 1, _cancelButtonRect.Height - 1);
                    }
                    int canIconSize = (int)(14 * _dpiScale);
                    int canGap = (int)(6 * _dpiScale);
                    using (Font btnFont = new Font("Segoe UI", Math.Max(8.5f, 9.5f * _dpiScale), FontStyle.Regular))
                    {
                        SizeF tSize = g.MeasureString("Cancel", btnFont);
                        int totalContentW = canIconSize + canGap + (int)Math.Ceiling(tSize.Width);
                        int startX = _cancelButtonRect.X + Math.Max(0, (_cancelButtonRect.Width - totalContentW) / 2);
                        int iconY = _cancelButtonRect.Y + (_cancelButtonRect.Height - canIconSize) / 2;
                        Rectangle iconRect = new Rectangle(startX, iconY, canIconSize, canIconSize);
                        MaterialIcons.DrawIcon(g, "x", iconRect, Color.White);

                        Rectangle textRect = new Rectangle(startX + canIconSize + canGap, _cancelButtonRect.Y, (int)Math.Ceiling(tSize.Width) + 4, _cancelButtonRect.Height);
                        using (SolidBrush tb = new SolidBrush(Color.White))
                        using (StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center })
                        {
                            g.DrawString("Cancel", btnFont, tb, textRect, sf);
                        }
                    }
                }
                else
                {
                    // Full darken scrim
                    g.FillRectangle(darkBrush, this.ClientRectangle);
                }
            }

            // 7. Draw mini scrollbars if document extends beyond viewport
            if (_screenBitmap != null)
            {
                int scrollbarW = (int)Math.Max(5, 6 * _dpiScale);
                int scrollbarPad = (int)(3 * _dpiScale);

                if (_screenBitmap.Height > this.ClientSize.Height)
                {
                    int trackH = this.ClientSize.Height - scrollbarPad * 2;
                    float ratio = (float)this.ClientSize.Height / _screenBitmap.Height;
                    int thumbH = Math.Max((int)(24 * _dpiScale), (int)(trackH * ratio));
                    int maxThumbY = trackH - thumbH;
                    int maxScrollY = _screenBitmap.Height - this.ClientSize.Height;
                    int thumbY = scrollbarPad + (maxScrollY > 0 ? (int)((float)_scrollY / maxScrollY * maxThumbY) : 0);

                    Rectangle trackRect = new Rectangle(this.ClientSize.Width - scrollbarW - scrollbarPad, scrollbarPad, scrollbarW, trackH);
                    Rectangle thumbRect = new Rectangle(this.ClientSize.Width - scrollbarW - scrollbarPad, thumbY, scrollbarW, thumbH);
                    using (SolidBrush trackBg = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    using (SolidBrush thumbBrush = new SolidBrush(Color.FromArgb(170, 200, 200, 200)))
                    {
                        g.FillRectangle(trackBg, trackRect);
                        g.FillRectangle(thumbBrush, thumbRect);
                    }
                }

                if (_screenBitmap.Width > this.ClientSize.Width)
                {
                    int trackW = this.ClientSize.Width - scrollbarPad * 2;
                    float ratio = (float)this.ClientSize.Width / _screenBitmap.Width;
                    int thumbW = Math.Max((int)(24 * _dpiScale), (int)(trackW * ratio));
                    int maxThumbX = trackW - thumbW;
                    int maxScrollX = _screenBitmap.Width - this.ClientSize.Width;
                    int thumbX = scrollbarPad + (maxScrollX > 0 ? (int)((float)_scrollX / maxScrollX * maxThumbX) : 0);

                    Rectangle trackRect = new Rectangle(scrollbarPad, this.ClientSize.Height - scrollbarW - scrollbarPad, trackW, scrollbarW);
                    Rectangle thumbRect = new Rectangle(thumbX, this.ClientSize.Height - scrollbarW - scrollbarPad, thumbW, scrollbarW);
                    using (SolidBrush trackBg = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    using (SolidBrush thumbBrush = new SolidBrush(Color.FromArgb(170, 200, 200, 200)))
                    {
                        g.FillRectangle(trackBg, trackRect);
                        g.FillRectangle(thumbBrush, thumbRect);
                    }
                }
            }

            // 8. Draw top banner instruction
            if (!_hasSelection)
            {
                string info = "Drag to select area  •  Scroll wheel / Arrows to pan canvas  •  Enter to confirm  •  Esc to cancel";
                using (Font infoFont = new Font("Segoe UI", Math.Max(9.5f, 10.5f * _dpiScale), FontStyle.Regular))
                {
                    SizeF s = g.MeasureString(info, infoFont);
                    int padH = (int)(28 * _dpiScale);
                    int padV = (int)(12 * _dpiScale);
                    int bw = (int)Math.Ceiling(s.Width) + padH;
                    int bh = (int)Math.Ceiling(s.Height) + padV;
                    int bx = (this.Width - bw) / 2;
                    int by = (int)(28 * _dpiScale);

                    Rectangle bannerRect = new Rectangle(bx, by, bw, bh);
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(245, 26, 27, 30)))
                    using (Pen p = new Pen(Color.FromArgb(160, MaterialColors.AccentBlue), Math.Max(1f, 1.5f * _dpiScale)))
                    using (SolidBrush textBrush = new SolidBrush(Color.White))
                    using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        g.FillRectangle(bg, bannerRect);
                        g.DrawRectangle(p, bannerRect);
                        g.DrawString(info, infoFont, textBrush, bannerRect, sf);
                    }
                }
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_CLOSE = 0x0010;
            if (m.Msg == WM_CLOSE)
            {
                if (!this.IsConfirmed)
                {
                    this.DialogResult = DialogResult.Cancel;
                }
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_autoScrollTimer != null)
                {
                    _autoScrollTimer.Stop();
                    _autoScrollTimer.Dispose();
                    _autoScrollTimer = null;
                }
                if (_screenBitmap != null)
                {
                    _screenBitmap.Dispose();
                    _screenBitmap = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
