using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace wb.UI
{
    public class MaterialToolStripRenderer : ToolStripProfessionalRenderer
    {
        public MaterialToolStripRenderer() : base(new MaterialColorTable())
        {
            this.RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is StatusStrip)
            {
                using (SolidBrush b = new SolidBrush(MaterialColors.StatusBackground))
                {
                    e.Graphics.FillRectangle(b, e.AffectedBounds);
                }
            }
            else
            {
                using (SolidBrush b = new SolidBrush(MaterialColors.NavBarBackground))
                {
                    e.Graphics.FillRectangle(b, e.AffectedBounds);
                }
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is StatusStrip)
            {
                using (Pen p = new Pen(MaterialColors.Separator, 1))
                {
                    e.Graphics.DrawLine(p, 0, 0, e.ToolStrip.Width, 0);
                }
            }
            else if (e.ToolStrip is ToolStripDropDown)
            {
                using (Pen p = new Pen(MaterialColors.Separator, 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                }
            }
            else
            {
                using (Pen p = new Pen(MaterialColors.Separator, 1))
                {
                    e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
                }
            }
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripButton btn = e.Item as ToolStripButton;
            if (btn == null) return;

            Rectangle rect = new Rectangle(Point.Empty, btn.Size);
            rect.Inflate(-2, -2);

            if (btn.Pressed || btn.Checked)
            {
                using (SolidBrush b = new SolidBrush(MaterialColors.ButtonPressed))
                {
                    FillRoundedRectangle(e.Graphics, b, rect, 4);
                }
            }
            else if (btn.Selected)
            {
                using (SolidBrush b = new SolidBrush(MaterialColors.ButtonHover))
                {
                    FillRoundedRectangle(e.Graphics, b, rect, 4);
                }
            }
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripDropDownButton btn = e.Item as ToolStripDropDownButton;
            if (btn == null) return;

            Rectangle rect = new Rectangle(Point.Empty, btn.Size);
            rect.Inflate(-2, -2);

            if (btn.Pressed || (btn.DropDown != null && btn.DropDown.Visible))
            {
                using (SolidBrush b = new SolidBrush(MaterialColors.ButtonPressed))
                {
                    FillRoundedRectangle(e.Graphics, b, rect, 4);
                }
            }
            else if (btn.Selected)
            {
                using (SolidBrush b = new SolidBrush(MaterialColors.ButtonHover))
                {
                    FillRoundedRectangle(e.Graphics, b, rect, 4);
                }
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Vertical)
            {
                int midX = e.Item.Width / 2;
                int top = 4;
                int bottom = e.Item.Height - 4;
                using (Pen p = new Pen(MaterialColors.Separator))
                {
                    e.Graphics.DrawLine(p, midX, top, midX, bottom);
                }
            }
            else
            {
                int midY = e.Item.Height / 2;
                using (Pen p = new Pen(MaterialColors.Separator))
                {
                    e.Graphics.DrawLine(p, 8, midY, e.Item.Width - 8, midY);
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripStatusLabel)
            {
                e.TextColor = MaterialColors.TextSecondary;
            }
            else
            {
                e.TextColor = e.Item.Enabled ? MaterialColors.TextPrimary : MaterialColors.TextDisabled;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = (e.Item != null && e.Item.Selected && e.Item.Enabled)
                ? MaterialColors.AccentBlue
                : (e.Item.Enabled ? MaterialColors.TextPrimary : MaterialColors.TextDisabled);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                using (SolidBrush b = new SolidBrush(MaterialColors.ButtonHover))
                {
                    e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
                }
            }
            else
            {
                using (SolidBrush b = new SolidBrush(MaterialColors.NavBarBackground))
                {
                    e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
                }
            }
        }

        public static Rectangle ComputeCheckRectangle(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            ToolStripMenuItem item = e.Item as ToolStripMenuItem;
            ToolStripDropDownMenu owner = (item != null) ? item.Owner as ToolStripDropDownMenu : null;

            // In single-margin mode where check and image share the same slot, shift check to left so it never overlaps the icon
            if (item != null && item.Image != null && (owner == null || !owner.ShowCheckMargin))
            {
                r = new Rectangle(r.Left - 4, r.Top, 14, r.Height);
            }
            return r;
        }

        public static Rectangle ComputeImageRectangle(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle rect = e.ImageRectangle;
            ToolStripMenuItem item = e.Item as ToolStripMenuItem;
            ToolStripDropDownMenu owner = (item != null) ? item.Owner as ToolStripDropDownMenu : null;

            // In single-margin mode where check and image share the same slot, shift image to right so it never overlaps the checkmark
            if (item != null && item.Checked && (owner == null || !owner.ShowCheckMargin))
            {
                rect = new Rectangle(rect.Left + 14, rect.Top, rect.Width, rect.Height);
            }
            return rect;
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = ComputeCheckRectangle(e);
            if (r.Width <= 0 || r.Height <= 0)
            {
                r = new Rectangle(4, (e.Item.Height - 16) / 2, 16, 16);
            }

            int iconSize = Math.Min(16, Math.Min(r.Width, r.Height));
            Rectangle checkRect = new Rectangle(
                r.Left + (r.Width - iconSize) / 2,
                r.Top + (r.Height - iconSize) / 2,
                iconSize, iconSize);

            Color checkColor = e.Item.Enabled ? MaterialColors.AccentBlue : MaterialColors.TextDisabled;
            try
            {
                MaterialIcons.DrawIcon(e.Graphics, "check", checkRect, checkColor);
            }
            catch
            {
                using (Font font = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, "✓", font, checkRect, checkColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(MaterialColors.NavBarBackground))
            {
                e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Image == null) return;
            Rectangle rect = ComputeImageRectangle(e);

            if (e.Item.Enabled)
            {
                DpiDrawingHelper.DrawImagePixelPerfect(e.Graphics, e.Image, rect);
            }
            else
            {
                ControlPaint.DrawImageDisabled(e.Graphics, e.Image, rect.X, rect.Y, Color.Transparent);
            }
        }

        protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item != null && e.Item.GetType().Name == "ToolStripScrollButton")
            {
                Rectangle rect = new Rectangle(Point.Empty, e.Item.Size);
                Color bgColor = e.Item.Selected ? MaterialColors.ButtonHover : MaterialColors.NavBarBackground;
                using (SolidBrush b = new SolidBrush(bgColor))
                {
                    e.Graphics.FillRectangle(b, rect);
                }
                return;
            }
            base.OnRenderItemBackground(e);
        }

        private static void FillRoundedRectangle(Graphics g, Brush b, Rectangle r, int radius)
        {
            UITheme.FillRoundedRectangle(g, b, r, radius);
        }
    }

    public class MaterialColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return MaterialColors.NavBarBackground; } }
        public override Color MenuBorder { get { return MaterialColors.Separator; } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return MaterialColors.ButtonHover; } }
        public override Color ImageMarginGradientBegin { get { return MaterialColors.NavBarBackground; } }
        public override Color ImageMarginGradientMiddle { get { return MaterialColors.NavBarBackground; } }
        public override Color ImageMarginGradientEnd { get { return MaterialColors.NavBarBackground; } }
        public override Color SeparatorDark { get { return MaterialColors.Separator; } }
        public override Color SeparatorLight { get { return Color.Transparent; } }
        public override Color StatusStripGradientBegin { get { return MaterialColors.StatusBackground; } }
        public override Color StatusStripGradientEnd { get { return MaterialColors.StatusBackground; } }
    }
}
