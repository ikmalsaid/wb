using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace wb.Installer
{
    /// <summary>
    /// Reusable Windows 11 Fluent-style animated circular progress spinner with centered percentage.
    /// Can be dropped into any WinForms dialog or custom status UI.
    /// </summary>
    public class CircularProgressSpinner : Control
    {
        private int _value = 0;
        private float _animatedAngle = 0;
        private System.Windows.Forms.Timer _animTimer;

        public int Value
        {
            get { return _value; }
            set
            {
                _value = Math.Max(0, Math.Min(100, value));
                this.Invalidate();
            }
        }

        public Color TrackColor { get; set; }
        public float StrokeWidth { get; set; }
        public Color? AccentColor { get; set; }
        public Color? GradientStart { get; set; }
        public Color? GradientEnd { get; set; }
        public bool ShowPercentageText { get; set; }
        public Color TextColor { get; set; }

        public CircularProgressSpinner()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Size = new Size(74, 74);

            TrackColor = Color.FromArgb(48, 50, 54);
            StrokeWidth = 5f;
            ShowPercentageText = true;
            TextColor = UIThemeTokens.TextPrimary;

            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 30;
            _animTimer.Tick += (s, e) =>
            {
                _animatedAngle = (_animatedAngle + 4f) % 360f;
                this.Invalidate();
            };
            _animTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
                _animTimer = null;
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            RectangleF rect = new RectangleF(StrokeWidth / 2f + 2f, StrokeWidth / 2f + 2f, this.Width - StrokeWidth - 4f, this.Height - StrokeWidth - 4f);

            // 1. Background ring track
            using (Pen trackPen = new Pen(TrackColor, StrokeWidth))
            {
                g.DrawEllipse(trackPen, rect);
            }

            // 2. Active progress arc
            Color cStart = GradientStart.HasValue ? GradientStart.Value : BrandTheme.AccentGradientStart;
            Color cEnd = GradientEnd.HasValue ? GradientEnd.Value : BrandTheme.AccentGradientEnd;
            Color cAccent = AccentColor.HasValue ? AccentColor.Value : BrandTheme.AccentColor;

            float sweepAngle = (_value / 100f) * 360f;
            if (sweepAngle > 0)
            {
                using (LinearGradientBrush grad = new LinearGradientBrush(rect, cStart, cEnd, LinearGradientMode.ForwardDiagonal))
                using (Pen arcPen = new Pen(grad, StrokeWidth))
                {
                    arcPen.StartCap = LineCap.Round;
                    arcPen.EndCap = LineCap.Round;
                    g.DrawArc(arcPen, rect, -90f + (_animatedAngle * 0.1f), sweepAngle);
                }
            }
            else
            {
                using (Pen arcPen = new Pen(cAccent, StrokeWidth))
                {
                    arcPen.StartCap = LineCap.Round;
                    arcPen.EndCap = LineCap.Round;
                    g.DrawArc(arcPen, rect, _animatedAngle, 60f);
                }
            }

            // 3. Centered percentage text inside the spinner
            if (ShowPercentageText)
            {
                string pctText = _value.ToString() + "%";
                Font fontPct = UIThemeTokens.FontDefaultBold;
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                TextRenderer.DrawText(g, pctText, fontPct, new Rectangle(0, 0, this.Width, this.Height), TextColor, flags);
            }
        }
    }
}

