using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace wb.Screenshot
{
    public enum AnnotationTool
    {
        Select,
        Hand,
        Pen,
        Highlighter,
        Arrow,
        Rectangle,
        Ellipse,
        Text,
        Blur,
        StepBadge,
        Crop
    }

    public abstract class ScreenshotAnnotation
    {
        public Color Color { get; set; }
        public float StrokeWidth { get; set; }

        public ScreenshotAnnotation()
        {
            Color = Color.FromArgb(234, 67, 53); // Default Google Red
            StrokeWidth = 3f;
        }

        public abstract void Draw(Graphics g, Bitmap baseImage = null);
    }

    public class PenAnnotation : ScreenshotAnnotation
    {
        public List<PointF> Points { get; private set; }

        public PenAnnotation()
        {
            Points = new List<PointF>();
        }

        public override void Draw(Graphics g, Bitmap baseImage = null)
        {
            if (Points == null || Points.Count < 2) return;

            using (Pen pen = new Pen(Color, StrokeWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, Points.ToArray());
            }
        }
    }

    public class HighlighterAnnotation : ScreenshotAnnotation
    {
        public List<PointF> Points { get; private set; }

        public HighlighterAnnotation()
        {
            Points = new List<PointF>();
            Color = Color.FromArgb(255, 235, 59); // Yellow
            StrokeWidth = 18f;
        }

        public override void Draw(Graphics g, Bitmap baseImage = null)
        {
            if (Points == null || Points.Count < 2) return;

            Color hlColor = Color.FromArgb(110, Color.R, Color.G, Color.B);
            using (Pen pen = new Pen(hlColor, StrokeWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, Points.ToArray());
            }
        }
    }

    public class ArrowAnnotation : ScreenshotAnnotation
    {
        public PointF StartPoint { get; set; }
        public PointF EndPoint { get; set; }

        public ArrowAnnotation() : this(PointF.Empty, PointF.Empty)
        {
        }

        public ArrowAnnotation(PointF start, PointF end)
        {
            StartPoint = start;
            EndPoint = end;
            StrokeWidth = 4f;
        }

        public override void Draw(Graphics g, Bitmap baseImage = null)
        {
            float dx = EndPoint.X - StartPoint.X;
            float dy = EndPoint.Y - StartPoint.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 2) return;

            using (Pen pen = new Pen(Color, StrokeWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, StartPoint, EndPoint);
            }

            // Draw clean arrow head
            float arrowAngle = (float)Math.Atan2(dy, dx);
            float headLength = Math.Max(12f, StrokeWidth * 3.5f);
            float headAngle = 0.45f; // ~26 degrees

            PointF p1 = new PointF(
                EndPoint.X - headLength * (float)Math.Cos(arrowAngle - headAngle),
                EndPoint.Y - headLength * (float)Math.Sin(arrowAngle - headAngle)
            );
            PointF p2 = new PointF(
                EndPoint.X - headLength * (float)Math.Cos(arrowAngle + headAngle),
                EndPoint.Y - headLength * (float)Math.Sin(arrowAngle + headAngle)
            );

            using (SolidBrush brush = new SolidBrush(Color))
            {
                PointF[] triangle = new PointF[] { EndPoint, p1, p2 };
                g.FillPolygon(brush, triangle);
            }
        }
    }

    public class RectAnnotation : ScreenshotAnnotation
    {
        public RectangleF Rect { get; set; }
        public bool IsFilled { get; set; }

        public RectAnnotation() : this(RectangleF.Empty, false)
        {
        }

        public RectAnnotation(RectangleF rect, bool isFilled = false)
        {
            Rect = rect;
            IsFilled = isFilled;
            StrokeWidth = 3f;
        }

        public override void Draw(Graphics g, Bitmap baseImage = null)
        {
            if (Rect.Width <= 0 || Rect.Height <= 0) return;

            if (IsFilled)
            {
                Color fillColor = Color.FromArgb(60, Color.R, Color.G, Color.B);
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    g.FillRectangle(brush, Rect);
                }
            }

            using (Pen pen = new Pen(Color, StrokeWidth))
            {
                g.DrawRectangle(pen, Rect.X, Rect.Y, Rect.Width, Rect.Height);
            }
        }
    }

    public class EllipseAnnotation : ScreenshotAnnotation
    {
        public RectangleF Rect { get; set; }
        public bool IsFilled { get; set; }

        public EllipseAnnotation(RectangleF rect, bool isFilled = false)
        {
            Rect = rect;
            IsFilled = isFilled;
            StrokeWidth = 3f;
        }

        public override void Draw(Graphics g, Bitmap baseImage = null)
        {
            if (Rect.Width <= 0 || Rect.Height <= 0) return;

            if (IsFilled)
            {
                Color fillColor = Color.FromArgb(60, Color.R, Color.G, Color.B);
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    g.FillEllipse(brush, Rect);
                }
            }

            using (Pen pen = new Pen(Color, StrokeWidth))
            {
                g.DrawEllipse(pen, Rect);
            }
        }
    }

    public class TextAnnotation : ScreenshotAnnotation
    {
        public PointF Position { get; set; }
        public string Text { get; set; }
        public float FontSize { get; set; }

        public TextAnnotation(PointF pos, string text, float fontSize = 14f)
        {
            Position = pos;
            Text = text ?? string.Empty;
            FontSize = fontSize;
            Color = Color.White;
        }

        public override void Draw(Graphics g, Bitmap baseImage = null)
        {
            if (string.IsNullOrEmpty(Text)) return;

            using (Font font = new Font("Segoe UI", FontSize, FontStyle.Bold))
            {
                SizeF size = g.MeasureString(Text, font);
                RectangleF pillRect = new RectangleF(Position.X - 4, Position.Y - 2, size.Width + 8, size.Height + 4);

                // Background pill for crisp readability on any content
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(220, 20, 21, 24)))
                using (Pen borderPen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                {
                    g.FillRectangle(bgBrush, pillRect);
                    g.DrawRectangle(borderPen, pillRect.X, pillRect.Y, pillRect.Width, pillRect.Height);
                }

                using (SolidBrush textBrush = new SolidBrush(Color))
                {
                    g.DrawString(Text, font, textBrush, Position);
                }
            }
        }
    }

    public class BlurAnnotation : ScreenshotAnnotation
    {
        public RectangleF Rect { get; set; }
        private Bitmap _cachedPixelated = null;

        public BlurAnnotation(RectangleF rect)
        {
            Rect = rect;
        }

        public override void Draw(Graphics g, Bitmap baseImage = null)
        {
            if (Rect.Width <= 2 || Rect.Height <= 2) return;

            if (baseImage != null)
            {
                Rectangle srcRect = Rectangle.Round(Rect);
                srcRect.Intersect(new Rectangle(0, 0, baseImage.Width, baseImage.Height));

                if (srcRect.Width > 2 && srcRect.Height > 2)
                {
                    if (_cachedPixelated == null || _cachedPixelated.Width != srcRect.Width || _cachedPixelated.Height != srcRect.Height)
                    {
                        if (_cachedPixelated != null) _cachedPixelated.Dispose();
                        _cachedPixelated = CreatePixelatedBitmap(baseImage, srcRect, 10);
                    }

                    if (_cachedPixelated != null)
                    {
                        g.DrawImageUnscaled(_cachedPixelated, srcRect.Location);
                    }
                }
            }

            // Draw a subtle privacy dashed border
            using (Pen p = new Pen(Color.FromArgb(160, 100, 100, 100), 1f))
            {
                p.DashStyle = DashStyle.Dash;
                g.DrawRectangle(p, Rect.X, Rect.Y, Rect.Width, Rect.Height);
            }
        }

        private static Bitmap CreatePixelatedBitmap(Bitmap source, Rectangle rect, int pixelSize)
        {
            try
            {
                int downW = Math.Max(1, rect.Width / pixelSize);
                int downH = Math.Max(1, rect.Height / pixelSize);

                using (Bitmap small = new Bitmap(downW, downH, PixelFormat.Format32bppArgb))
                {
                    using (Graphics gSmall = Graphics.FromImage(small))
                    {
                        gSmall.InterpolationMode = InterpolationMode.Bilinear;
                        gSmall.DrawImage(source, new Rectangle(0, 0, downW, downH), rect, GraphicsUnit.Pixel);
                    }

                    Bitmap pixelated = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
                    using (Graphics gBig = Graphics.FromImage(pixelated))
                    {
                        gBig.InterpolationMode = InterpolationMode.NearestNeighbor;
                        gBig.PixelOffsetMode = PixelOffsetMode.Half;
                        gBig.DrawImage(small, new Rectangle(0, 0, rect.Width, rect.Height));
                    }
                    return pixelated;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    public class StepBadgeAnnotation : ScreenshotAnnotation
    {
        public PointF Center { get; set; }
        public int StepNumber { get; set; }
        public float Radius { get; set; }

        public StepBadgeAnnotation(PointF center, int stepNumber, float radius = 14f)
        {
            Center = center;
            StepNumber = stepNumber;
            Radius = radius;
            Color = Color.FromArgb(26, 115, 232); // Accent Blue
        }

        public override void Draw(Graphics g, Bitmap baseImage = null)
        {
            RectangleF badgeRect = new RectangleF(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);

            // Shadow
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
            {
                g.FillEllipse(shadowBrush, badgeRect.X + 1.5f, badgeRect.Y + 1.5f, badgeRect.Width, badgeRect.Height);
            }

            // Circle background
            using (SolidBrush brush = new SolidBrush(Color))
            using (Pen borderPen = new Pen(Color.White, 2f))
            {
                g.FillEllipse(brush, badgeRect);
                g.DrawEllipse(borderPen, badgeRect);
            }

            // Number
            string numStr = StepNumber.ToString();
            using (Font font = new Font("Segoe UI", Radius * 0.9f, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(numStr, font, textBrush, badgeRect, sf);
            }
        }
    }
}
