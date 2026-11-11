using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace wb.Installer
{
    /// <summary>
    /// Interactive GPU-Z / Fluent style action choice card control with vector icon, bold title, and hover glow.
    /// Reusable card button component for options, actions, or menu choices in WinForms.
    /// </summary>
    public class ActionChoiceCard : Control
    {
        public enum CardIconType { Download, Rocket, PortableFolder, Trash, Custom }

        public string CardTitle { get; set; }
        public string IconName { get; set; }
        public CardIconType IconType { get; set; }
        public bool IsPrimary { get; set; }
        public bool ShowChevron { get; set; }

        private bool _isHovered = false;
        private bool _isPressed = false;

        public ActionChoiceCard(string title, string iconName, bool isPrimary = false)
        {
            this.CardTitle = title;
            this.IconName = iconName;
            this.IconType = CardIconType.Custom;
            this.IsPrimary = isPrimary;
            this.ShowChevron = true;

            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.Cursor = Cursors.Hand;
            this.Size = new Size(460, 46);
        }

        public ActionChoiceCard(string title, CardIconType iconType, bool isPrimary = false)
            : this(title, GetDefaultIconName(iconType), isPrimary)
        {
            this.IconType = iconType;
        }

        private static string GetDefaultIconName(CardIconType type)
        {
            switch (type)
            {
                case CardIconType.Rocket: return "rocket-launch";
                case CardIconType.Trash: return "trash";
                case CardIconType.PortableFolder: return "folder-simple";
                case CardIconType.Download:
                default: return "download-simple";
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            _isPressed = false;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isPressed = true;
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isPressed)
            {
                _isPressed = false;
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Rectangle rect = new Rectangle(1, 1, this.Width - 3, this.Height - 3);

            // 1. Card Background
            Color bg = _isPressed ? UIThemeTokens.CardPressed : (_isHovered ? UIThemeTokens.CardHover : UIThemeTokens.CardBackground);
            using (GraphicsPath path = CreateRoundedRectanglePath(rect, 6))
            {
                using (SolidBrush b = new SolidBrush(bg))
                {
                    g.FillPath(b, path);
                }

                // 2. Card Border (Accent speed gradient if primary, subtle border if secondary)
                if (IsPrimary)
                {
                    using (LinearGradientBrush grad = new LinearGradientBrush(rect, BrandTheme.AccentGradientStart, BrandTheme.AccentGradientEnd, LinearGradientMode.Horizontal))
                    using (Pen p = new Pen(grad, _isHovered ? 2f : 1.5f))
                    {
                        g.DrawPath(p, path);
                    }
                }
                else
                {
                    Color borderC = _isHovered ? UIThemeTokens.BorderHover : UIThemeTokens.BorderColor;
                    using (Pen p = new Pen(borderC, 1f))
                    {
                        g.DrawPath(p, path);
                    }
                }
            }

            // 3. Draw Icon (Vertically centered)
            int iconSize = 20;
            int iconX = 18;
            int iconY = (this.Height - iconSize) / 2;
            Color iconColor = IsPrimary ? BrandTheme.AccentColor : (_isHovered ? UIThemeTokens.TextPrimary : UIThemeTokens.TextSecondary);
            SetupIcons.DrawIcon(g, this.IconName, new Rectangle(iconX, iconY, iconSize, iconSize), iconColor);

            // 4. Draw Title
            int textX = 52;
            int rightPadding = ShowChevron ? 36 : 16;
            Rectangle titleRect = new Rectangle(textX, 0, this.Width - textX - rightPadding, this.Height);
            Color titleColor = UIThemeTokens.TextPrimary;
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, this.CardTitle, UIThemeTokens.FontDefaultBold, titleRect, titleColor, flags);

            // 5. Right Chevron Indicator (›)
            if (ShowChevron)
            {
                int arrowX = this.Width - 24;
                int arrowY = this.Height / 2;
                Color arrowColor = IsPrimary ? BrandTheme.AccentColor : (_isHovered ? UIThemeTokens.TextPrimary : UIThemeTokens.TextDisabled);
                using (Pen ap = new Pen(arrowColor, 1.8f))
                {
                    ap.StartCap = LineCap.Round;
                    ap.EndCap = LineCap.Round;
                    g.DrawLine(ap, arrowX - 4, arrowY - 4, arrowX, arrowY);
                    g.DrawLine(ap, arrowX, arrowY, arrowX - 4, arrowY + 4);
                }
            }
        }

        public static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

