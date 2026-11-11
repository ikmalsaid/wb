using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace wb.Installer
{
    /// <summary>
    /// Vector SVG / PNG application branding logo control with subpixel anti-aliasing and auto-scaling.
    /// Can render an SVG path from resources or disk, an embedded PNG image, or a clean fallback monogram.
    /// </summary>
    public class BrandLogoControl : Control
    {
        private Image _fallbackImage = null;
        private GraphicsPath _cachedSvgPath = null;
        private float _svgWidth = 845f;
        private float _svgHeight = 461f;
        private readonly InstallerConfig _config;

        public BrandLogoControl(InstallerConfig config = null)
        {
            _config = config ?? InstallerConfig.Current;

            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Size = new Size(68, 38);

            LoadBrandAssets();
        }

        private void LoadBrandAssets()
        {
            // 1. Try embedded SVG icon resource
            string svgContent = null;
            try
            {
                if (!string.IsNullOrEmpty(_config.SvgIconResource))
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream stream = asm.GetManifestResourceStream(_config.SvgIconResource))
                    {
                        if (stream != null)
                        {
                            using (StreamReader reader = new StreamReader(stream))
                            {
                                svgContent = reader.ReadToEnd();
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Try disk candidates
            if (string.IsNullOrEmpty(svgContent))
            {
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string curDir = Directory.GetCurrentDirectory();
                    string[] candidates = new string[]
                    {
                        Path.Combine(baseDir, "installer", "assets", "icons", _config.AppName + "_icon.svg"),
                        Path.Combine(baseDir, "assets", "icons", _config.AppName + "_icon.svg"),
                        Path.Combine(curDir, "installer", "assets", "icons", _config.AppName + "_icon.svg"),
                        Path.Combine(curDir, "assets", "icons", _config.AppName + "_icon.svg"),
                        Path.Combine(baseDir, "core", "assets", "icons", _config.AppName + "_icon.svg"),
                        Path.Combine(baseDir, "..", "assets", "icons", _config.AppName + "_icon.svg")
                    };

                    for (int i = 0; i < candidates.Length; i++)
                    {
                        if (File.Exists(candidates[i]))
                        {
                            svgContent = File.ReadAllText(candidates[i]);
                            break;
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(svgContent))
            {
                try
                {
                    Match m = Regex.Match(svgContent, "d=\"([^\"]+)\"");
                    if (m.Success)
                    {
                        Match mw = Regex.Match(svgContent, "width=\"([^\"]+)\"");
                        Match mh = Regex.Match(svgContent, "height=\"([^\"]+)\"");
                        float w, h;
                        if (mw.Success && float.TryParse(mw.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out w) && w > 0)
                            _svgWidth = w;
                        if (mh.Success && float.TryParse(mh.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out h) && h > 0)
                            _svgHeight = h;

                        _cachedSvgPath = SvgPathParser.Parse(m.Groups[1].Value);
                    }
                }
                catch { }
            }

            // 3. Fallback PNG logo resource
            if (_cachedSvgPath == null && !string.IsNullOrEmpty(_config.LogoPngResource))
            {
                try
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream stream = asm.GetManifestResourceStream(_config.LogoPngResource))
                    {
                        if (stream != null)
                        {
                            _fallbackImage = Image.FromStream(stream);
                        }
                    }
                }
                catch { }
            }
        }

        public void SetSvgPath(GraphicsPath path, float width = 845f, float height = 461f)
        {
            _cachedSvgPath = path;
            _svgWidth = width;
            _svgHeight = height;
            this.Invalidate();
        }

        public void SetImage(Image image)
        {
            _fallbackImage = image;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (_cachedSvgPath != null)
            {
                float aspect = _svgWidth / _svgHeight;
                float drawW = this.ClientRectangle.Width;
                float drawH = drawW / aspect;
                if (drawH > this.ClientRectangle.Height)
                {
                    drawH = this.ClientRectangle.Height;
                    drawW = drawH * aspect;
                }

                float drawX = this.ClientRectangle.X + (this.ClientRectangle.Width - drawW) / 2f;
                float drawY = this.ClientRectangle.Y + (this.ClientRectangle.Height - drawH) / 2f;

                using (GraphicsPath clone = (GraphicsPath)_cachedSvgPath.Clone())
                {
                    using (Matrix mat = new Matrix())
                    {
                        mat.Scale(drawW / _svgWidth, drawH / _svgHeight);
                        mat.Translate(drawX, drawY, MatrixOrder.Append);
                        clone.Transform(mat);
                    }

                    RectangleF gradRect = new RectangleF(drawX, drawY, Math.Max(1f, drawW), Math.Max(1f, drawH));
                    using (LinearGradientBrush brush = new LinearGradientBrush(gradRect, BrandTheme.AccentGradientStart, BrandTheme.AccentGradientEnd, LinearGradientMode.Horizontal))
                    {
                        g.FillPath(brush, clone);
                    }
                }
            }
            else if (_fallbackImage != null)
            {
                g.DrawImage(_fallbackImage, this.ClientRectangle);
            }
            else
            {
                // Simple typographic monogram fallback
                using (Font monogramFont = new Font("Segoe UI", 16f, FontStyle.Bold))
                {
                    string text = _config.AppName;
                    TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                    TextRenderer.DrawText(g, text, monogramFont, this.ClientRectangle, BrandTheme.AccentColor, flags);
                }
            }
        }
    }

    /// <summary>
    /// Backward-compatibility alias for BrandLogoControl.
    /// </summary>
    public class WbLogoControl : BrandLogoControl
    {
        public WbLogoControl() : base(InstallerConfig.Current) { }
    }

    /// <summary>
    /// Backward-compatibility class for WbSvgIcon.
    /// </summary>
    public static class WbSvgIcon
    {
        public static GraphicsPath GetPath()
        {
            return SetupIcons.GetPath("download-simple");
        }

        public static void Draw(Graphics g, Rectangle destRect, Color cStart, Color cEnd)
        {
            // Handled dynamically by BrandLogoControl
        }
    }
}

