using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace wb.UI
{
    public enum ScrollBarOrientation
    {
        Vertical,
        Horizontal
    }

    /// <summary>
    /// A sleek, modern dark-mode scrollbar component with rounded pill thumb,
    /// smooth hover and dragging states, mouse wheel support and standalone / attached modes.
    /// Can also be used as a renderer/calculator for custom canvas controls.
    /// </summary>
    public class UniversalScrollBar : Control
    {
        private ScrollBarOrientation _orientation = ScrollBarOrientation.Vertical;
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 0;
        private int _largeChange = 10;
        private int _smallChange = 1;

        private int _thickness = 10;
        private int _marginFromEdge = 3;
        private int _minThumbSize = 24;

        private bool _isHoverTrack = false;
        private bool _isHoverThumb = false;
        private bool _isDragging = false;
        private int _dragGrabOffset = 0;

        private Rectangle _trackRect = Rectangle.Empty;
        private Rectangle _thumbRect = Rectangle.Empty;

        private Color _trackHoverColor = Color.FromArgb(45, 0, 0, 0);
        private Color _thumbColor = Color.FromArgb(110, 160, 165, 170);
        private Color _thumbHoverColor = Color.FromArgb(170, 200, 205, 210);
        private Color _thumbDraggingColor = Color.FromArgb(210, 225, 230, 235);

        private ScrollableControl _attachedControl = null;

        public event EventHandler ValueChanged;
        public event ScrollEventHandler Scroll;

        [Category("Behavior")]
        [DefaultValue(ScrollBarOrientation.Vertical)]
        public ScrollBarOrientation Orientation
        {
            get { return _orientation; }
            set
            {
                if (_orientation != value)
                {
                    _orientation = value;
                    UpdateGeometry();
                    this.Invalidate();
                }
            }
        }

        [Category("Behavior")]
        [DefaultValue(0)]
        public int Minimum
        {
            get { return _minimum; }
            set
            {
                _minimum = value;
                if (_maximum < _minimum) _maximum = _minimum;
                Value = _value; // Re-clamp
                UpdateGeometry();
                this.Invalidate();
            }
        }

        [Category("Behavior")]
        [DefaultValue(100)]
        public int Maximum
        {
            get { return _maximum; }
            set
            {
                _maximum = value;
                if (_minimum > _maximum) _minimum = _maximum;
                Value = _value; // Re-clamp
                UpdateGeometry();
                this.Invalidate();
            }
        }

        [Category("Behavior")]
        [DefaultValue(0)]
        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value != clamped)
                {
                    int oldVal = _value;
                    _value = clamped;
                    UpdateGeometry();
                    OnValueChanged();
                    this.Invalidate();
                }
            }
        }

        [Category("Behavior")]
        [DefaultValue(10)]
        public int LargeChange
        {
            get { return _largeChange; }
            set
            {
                if (_largeChange != value && value >= 0)
                {
                    _largeChange = value;
                    UpdateGeometry();
                    this.Invalidate();
                }
            }
        }

        [Category("Behavior")]
        [DefaultValue(1)]
        public int SmallChange
        {
            get { return _smallChange; }
            set
            {
                if (_smallChange != value && value >= 1)
                {
                    _smallChange = value;
                }
            }
        }

        [Category("Appearance")]
        [DefaultValue(10)]
        public int Thickness
        {
            get { return _thickness; }
            set
            {
                if (_thickness != value && value >= 4)
                {
                    _thickness = value;
                    UpdateGeometry();
                    this.Invalidate();
                }
            }
        }

        [Category("Appearance")]
        [DefaultValue(3)]
        public int MarginFromEdge
        {
            get { return _marginFromEdge; }
            set
            {
                if (_marginFromEdge != value && value >= 0)
                {
                    _marginFromEdge = value;
                    UpdateGeometry();
                    this.Invalidate();
                }
            }
        }

        [Category("Appearance")]
        [DefaultValue(24)]
        public int MinThumbSize
        {
            get { return _minThumbSize; }
            set
            {
                if (_minThumbSize != value && value >= 8)
                {
                    _minThumbSize = value;
                    UpdateGeometry();
                    this.Invalidate();
                }
            }
        }

        public Rectangle TrackRectangle
        {
            get { return _trackRect; }
        }

        public Rectangle ThumbRectangle
        {
            get { return _thumbRect; }
        }

        public UniversalScrollBar()
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable |
                ControlStyles.SupportsTransparentBackColor,
                true);
            this.DoubleBuffered = true;
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Default;
            this.Size = new Size(_thickness + _marginFromEdge * 2, 100);
        }

        protected virtual void OnValueChanged()
        {
            if (ValueChanged != null)
            {
                ValueChanged(this, EventArgs.Empty);
            }
        }

        protected virtual void OnScroll(ScrollEventType type, int oldValue, int newValue)
        {
            if (Scroll != null)
            {
                Scroll(this, new ScrollEventArgs(type, oldValue, newValue,
                    _orientation == ScrollBarOrientation.Vertical ? System.Windows.Forms.ScrollOrientation.VerticalScroll : System.Windows.Forms.ScrollOrientation.HorizontalScroll));
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateGeometry();
        }

        private void UpdateGeometry()
        {
            CalculateGeometry(
                this.ClientRectangle,
                _orientation,
                _minimum,
                _maximum,
                _value,
                _largeChange,
                _thickness,
                _marginFromEdge,
                _minThumbSize,
                out _trackRect,
                out _thumbRect
            );
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                UpdateGeometry();

                if (_thumbRect.Contains(e.Location))
                {
                    _isDragging = true;
                    _dragGrabOffset = (_orientation == ScrollBarOrientation.Vertical)
                        ? (e.Y - _thumbRect.Y)
                        : (e.X - _thumbRect.X);
                    this.Capture = true;
                    this.Invalidate();
                }
                else if (_trackRect.Contains(e.Location))
                {
                    // Clicked on track above or below thumb -> Page Up or Page Down
                    int clickPos = (_orientation == ScrollBarOrientation.Vertical) ? e.Y : e.X;
                    int thumbPos = (_orientation == ScrollBarOrientation.Vertical) ? _thumbRect.Y : _thumbRect.X;

                    int oldVal = _value;
                    if (clickPos < thumbPos)
                    {
                        Value = _value - _largeChange;
                        OnScroll(ScrollEventType.LargeDecrement, oldVal, _value);
                    }
                    else
                    {
                        Value = _value + _largeChange;
                        OnScroll(ScrollEventType.LargeIncrement, oldVal, _value);
                    }

                    // Directly begin dragging with thumb centered
                    UpdateGeometry();
                    _isDragging = true;
                    int thumbSize = (_orientation == ScrollBarOrientation.Vertical) ? _thumbRect.Height : _thumbRect.Width;
                    _dragGrabOffset = thumbSize / 2;
                    this.Capture = true;
                    this.Invalidate();
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_isDragging)
            {
                int oldVal = _value;
                int mousePos = (_orientation == ScrollBarOrientation.Vertical) ? (e.Y - _dragGrabOffset) : (e.X - _dragGrabOffset);
                int newVal = ComputeValueFromPosition(_trackRect, _thumbRect, _orientation, _minimum, _maximum, _largeChange, mousePos);
                Value = newVal;
                OnScroll(ScrollEventType.ThumbTrack, oldVal, _value);
                return;
            }

            bool prevTrack = _isHoverTrack;
            bool prevThumb = _isHoverThumb;
            _isHoverTrack = _trackRect.Contains(e.Location);
            _isHoverThumb = _thumbRect.Contains(e.Location);

            if (prevTrack != _isHoverTrack || prevThumb != _isHoverThumb)
            {
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                this.Capture = false;
                OnScroll(ScrollEventType.EndScroll, _value, _value);
                this.Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_isHoverTrack || _isHoverThumb)
            {
                _isHoverTrack = false;
                _isHoverThumb = false;
                this.Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int step = _smallChange * 3;
            int delta = (e.Delta > 0) ? -step : step;
            int oldVal = _value;
            Value = _value + delta;
            OnScroll((e.Delta > 0) ? ScrollEventType.SmallDecrement : ScrollEventType.SmallIncrement, oldVal, _value);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UpdateGeometry();

            Draw(
                e.Graphics,
                _trackRect,
                _thumbRect,
                _orientation,
                _isHoverTrack,
                _isHoverThumb,
                _isDragging
            );
        }

        /// <summary>
        /// Attach this scrollbar to automatically synchronize with a ScrollableControl (like Panel or ContainerControl).
        /// </summary>
        public void AttachTo(ScrollableControl target)
        {
            if (_attachedControl != null)
            {
                _attachedControl.Resize -= Target_Sync;
            }

            _attachedControl = target;
            if (_attachedControl != null)
            {
                _attachedControl.Resize += Target_Sync;
                SyncFromTarget();
            }
        }

        private void Target_Sync(object sender, EventArgs e)
        {
            SyncFromTarget();
        }

        public void SyncFromTarget()
        {
            if (_attachedControl == null || _attachedControl.IsDisposed) return;

            if (_orientation == ScrollBarOrientation.Vertical)
            {
                this.Minimum = 0;
                this.Maximum = _attachedControl.VerticalScroll.Maximum;
                this.LargeChange = _attachedControl.VerticalScroll.LargeChange;
                this.Value = _attachedControl.VerticalScroll.Value;
                this.Visible = _attachedControl.VerticalScroll.Visible && this.Maximum > this.LargeChange;
            }
            else
            {
                this.Minimum = 0;
                this.Maximum = _attachedControl.HorizontalScroll.Maximum;
                this.LargeChange = _attachedControl.HorizontalScroll.LargeChange;
                this.Value = _attachedControl.HorizontalScroll.Value;
                this.Visible = _attachedControl.HorizontalScroll.Visible && this.Maximum > this.LargeChange;
            }
        }

        // ==========================================
        // Static Calculation and Rendering Helpers
        // ==========================================

        public static void CalculateGeometry(
            Rectangle bounds,
            ScrollBarOrientation orientation,
            int min,
            int max,
            int val,
            int largeChange,
            int thickness,
            int margin,
            int minThumbSize,
            out Rectangle trackRect,
            out Rectangle thumbRect)
        {
            trackRect = Rectangle.Empty;
            thumbRect = Rectangle.Empty;

            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            int totalRange = Math.Max(1, max - min);
            float viewRatio = (float)largeChange / totalRange;
            if (viewRatio > 1.0f) viewRatio = 1.0f;

            int maxScrollable = Math.Max(0, max - largeChange - min);
            float scrollProgress = (maxScrollable > 0) ? (float)(val - min) / maxScrollable : 0f;
            scrollProgress = Math.Max(0f, Math.Min(1f, scrollProgress));

            if (orientation == ScrollBarOrientation.Vertical)
            {
                int trackX = bounds.Right - thickness - margin;
                int trackY = bounds.Y + margin;
                int trackW = thickness;
                int trackH = Math.Max(10, bounds.Height - (margin * 2));

                trackRect = new Rectangle(trackX, trackY, trackW, trackH);

                int thumbH = Math.Max(minThumbSize, (int)(trackH * viewRatio));
                if (thumbH > trackH) thumbH = trackH;

                int availTrack = Math.Max(0, trackH - thumbH);
                int thumbY = trackY + (int)(scrollProgress * availTrack);

                thumbRect = new Rectangle(trackX, thumbY, trackW, thumbH);
            }
            else
            {
                int trackX = bounds.X + margin;
                int trackY = bounds.Bottom - thickness - margin;
                int trackW = Math.Max(10, bounds.Width - (margin * 2));
                int trackH = thickness;

                trackRect = new Rectangle(trackX, trackY, trackW, trackH);

                int thumbW = Math.Max(minThumbSize, (int)(trackW * viewRatio));
                if (thumbW > trackW) thumbW = trackW;

                int availTrack = Math.Max(0, trackW - thumbW);
                int thumbX = trackX + (int)(scrollProgress * availTrack);

                thumbRect = new Rectangle(thumbX, trackY, thumbW, trackH);
            }
        }

        public static int ComputeValueFromPosition(
            Rectangle trackRect,
            Rectangle thumbRect,
            ScrollBarOrientation orientation,
            int min,
            int max,
            int largeChange,
            int thumbPos)
        {
            int maxScrollable = Math.Max(0, max - largeChange - min);
            if (maxScrollable <= 0) return min;

            float t;
            if (orientation == ScrollBarOrientation.Vertical)
            {
                int avail = trackRect.Height - thumbRect.Height;
                if (avail <= 0) return min;
                int relative = Math.Max(0, Math.Min(avail, thumbPos - trackRect.Y));
                t = (float)relative / avail;
            }
            else
            {
                int avail = trackRect.Width - thumbRect.Width;
                if (avail <= 0) return min;
                int relative = Math.Max(0, Math.Min(avail, thumbPos - trackRect.X));
                t = (float)relative / avail;
            }

            t = Math.Max(0f, Math.Min(1f, t));
            return min + (int)Math.Round(t * maxScrollable);
        }

        public static void Draw(
            Graphics g,
            Rectangle trackRect,
            Rectangle thumbRect,
            ScrollBarOrientation orientation,
            bool isHoverTrack,
            bool isHoverThumb,
            bool isDragging,
            Color? customThumbColor = null)
        {
            if (trackRect.Width <= 0 || trackRect.Height <= 0 || thumbRect.Width <= 0 || thumbRect.Height <= 0)
                return;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Track background on hover or dragging
            if (isHoverTrack || isDragging)
            {
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
                {
                    g.FillPath(tb, CreateRoundedPath(trackRect, 4));
                }
            }

            // Thumb Color
            Color thumbCol;
            if (customThumbColor.HasValue)
            {
                thumbCol = customThumbColor.Value;
            }
            else if (isDragging)
            {
                thumbCol = Color.FromArgb(210, 225, 230, 235);
            }
            else if (isHoverThumb)
            {
                thumbCol = Color.FromArgb(170, 200, 205, 210);
            }
            else
            {
                thumbCol = Color.FromArgb(110, 160, 165, 170);
            }

            // Thumb pill with slight inner margin for refined aesthetics
            Rectangle thumbPill;
            if (orientation == ScrollBarOrientation.Vertical)
            {
                thumbPill = new Rectangle(thumbRect.X + 1, thumbRect.Y, Math.Max(2, thumbRect.Width - 2), thumbRect.Height);
            }
            else
            {
                thumbPill = new Rectangle(thumbRect.X, thumbRect.Y + 1, thumbRect.Width, Math.Max(2, thumbRect.Height - 2));
            }

            using (SolidBrush b = new SolidBrush(thumbCol))
            {
                g.FillPath(b, CreateRoundedPath(thumbPill, 3));
            }
        }

        public static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            return UITheme.CreateRoundedPath(rect, radius);
        }
    }
}
