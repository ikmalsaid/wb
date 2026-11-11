using System;
using System.Drawing;

namespace wb.Installer
{
    /// <summary>
    /// Fluent and Material Dark theme design tokens for controls, surfaces, borders, and typography.
    /// Developers can customize colors and fonts here to style the installer wizard.
    /// </summary>
    public static class UIThemeTokens
    {
        public static readonly Color FormBackground = Color.FromArgb(32, 33, 36);        // #202124 (Header & Canvas)
        public static readonly Color CardBackground = Color.FromArgb(41, 42, 45);        // #292A2D (Card / Surface)
        public static readonly Color CardHover = Color.FromArgb(48, 50, 54);             // #303236
        public static readonly Color CardPressed = Color.FromArgb(56, 58, 62);           // #383A3E
        public static readonly Color BorderColor = Color.FromArgb(60, 64, 67);           // #3C4043 (Separators / Outline)
        public static readonly Color BorderHover = Color.FromArgb(90, 95, 100);
        public static readonly Color WindowBorder = Color.FromArgb(80, 84, 90);          // #50545A (Crisp outer window outline)
        public static readonly Color TextPrimary = Color.FromArgb(232, 234, 237);        // #E8EAED (Light text)
        public static readonly Color TextSecondary = Color.FromArgb(154, 160, 166);     // #9AA0A6 (Muted text)
        public static readonly Color TextDisabled = Color.FromArgb(95, 99, 104);         // #5F6368
        public static readonly Color InputBackground = Color.FromArgb(28, 29, 31);
        public static readonly Color StatusSuccess = Color.FromArgb(129, 201, 149);     // #81C995 (Green)

        public static readonly Font FontTitle = new Font("Segoe UI", 18f, FontStyle.Bold);
        public static readonly Font FontHeader = new Font("Segoe UI", 11f, FontStyle.Bold);
        public static readonly Font FontDefault = new Font("Segoe UI", 9f, FontStyle.Regular);
        public static readonly Font FontDefaultBold = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.25f, FontStyle.Regular);
    }

    /// <summary>
    /// Dynamically derives fluid two-tone speed gradients matching an accent color or dynamic random generation.
    /// </summary>
    public static class BrandTheme
    {
        public static Color AccentColor { get; set; }
        public static Color AccentTextColor { get; private set; }
        public static Color AccentHoverColor { get; private set; }
        public static Color AccentPressedColor { get; private set; }
        public static Color AccentGradientStart { get; private set; }
        public static Color AccentGradientEnd { get; private set; }

        static BrandTheme()
        {
            GenerateRandomAccent();
        }

        public static void GenerateRandomAccent(Color? explicitColor = null)
        {
            if (explicitColor.HasValue && !explicitColor.Value.IsEmpty && explicitColor.Value.A > 0)
            {
                AccentColor = explicitColor.Value;
            }
            else
            {
                Random rand = new Random(Guid.NewGuid().GetHashCode());
                double hue = rand.NextDouble() * 360.0;
                // Pastel range: moderate saturation (50% - 70%) and high lightness (72% - 82%) to avoid dark tones
                double saturation = 0.50 + (rand.NextDouble() * 0.20);
                double lightness = 0.72 + (rand.NextDouble() * 0.10);
                AccentColor = HslToRgb(hue, saturation, lightness);
            }

            // Derive harmonic kinetic speed gradient
            float h, s, l;
            RgbToHsl(AccentColor, out h, out s, out l);

            float hStart = (h - 22f + 360f) % 360f;
            float lStart = Math.Min(0.90f, l * 1.08f);
            float sStart = Math.Min(0.85f, s * 1.02f);

            float hEnd = (h + 22f) % 360f;
            float lEnd = Math.Max(0.55f, l * 0.90f);
            float sEnd = Math.Min(0.85f, s * 1.05f);

            AccentGradientStart = HslToRgb(hStart, sStart, lStart);
            AccentGradientEnd = HslToRgb(hEnd, sEnd, lEnd);

            float lHover = Math.Min(0.90f, l * 1.08f);
            float lPressed = Math.Max(0.55f, l * 0.88f);
            AccentHoverColor = HslToRgb(h, s, lHover);
            AccentPressedColor = HslToRgb(h, s, lPressed);

            // Compute high-contrast readable text color
            AccentTextColor = CalculateContrastingTextColor(AccentColor);
        }

        public static Color CalculateContrastingTextColor(Color bg)
        {
            // Standard relative luminance (ITU-R BT.709 / WCAG 2.1)
            double r = bg.R / 255.0;
            double g = bg.G / 255.0;
            double b = bg.B / 255.0;

            double rLin = (r <= 0.03928) ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            double gLin = (g <= 0.03928) ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            double bLin = (b <= 0.03928) ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

            double luminance = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;

            // If background is light (> 0.20 luminance, mathematical crossover where contrast with charcoal exceeds white),
            // use crisp dark charcoal (#101114) for maximum readability. If dark/medium, use pure White (#FFFFFF).
            return luminance > 0.20 ? Color.FromArgb(16, 17, 20) : Color.White;
        }

        public static void RgbToHsl(Color rgb, out float h, out float s, out float l)
        {
            float r = rgb.R / 255f;
            float g = rgb.G / 255f;
            float b = rgb.B / 255f;

            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));

            l = (max + min) / 2f;

            if (max == min)
            {
                h = 0f;
                s = 0f;
            }
            else
            {
                float delta = max - min;
                s = (l <= 0.5f) ? (delta / (max + min)) : (delta / (2f - max - min));

                if (r == max)
                    h = ((g - b) / delta + (g < b ? 6f : 0f)) * 60f;
                else if (g == max)
                    h = ((b - r) / delta + 2f) * 60f;
                else
                    h = ((r - g) / delta + 4f) * 60f;
            }
        }

        public static Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1.0 + s) : l + s - (l * s);
                double p = 2.0 * l - q;
                double hk = h / 360.0;
                r = HueToRgb(p, q, hk + 1.0 / 3.0);
                g = HueToRgb(p, q, hk);
                b = HueToRgb(p, q, hk - 1.0 / 3.0);
            }
            return Color.FromArgb(
                (int)Math.Max(0, Math.Min(255, Math.Round(r * 255))),
                (int)Math.Max(0, Math.Min(255, Math.Round(g * 255))),
                (int)Math.Max(0, Math.Min(255, Math.Round(b * 255)))
            );
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1.0;
            if (t > 1) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }
    }

    /// <summary>
    /// Backward-compatibility alias for BrandTheme.
    /// </summary>
    public static class WbBrandTheme
    {
        public static Color AccentColor { get { return BrandTheme.AccentColor; } }
        public static Color AccentTextColor { get { return BrandTheme.AccentTextColor; } }
        public static Color AccentHoverColor { get { return BrandTheme.AccentHoverColor; } }
        public static Color AccentPressedColor { get { return BrandTheme.AccentPressedColor; } }
        public static Color AccentGradientStart { get { return BrandTheme.AccentGradientStart; } }
        public static Color AccentGradientEnd { get { return BrandTheme.AccentGradientEnd; } }
        public static void GenerateRandomAccent(Color? explicitColor = null) { BrandTheme.GenerateRandomAccent(explicitColor); }
        public static void RgbToHsl(Color rgb, out float h, out float s, out float l) { BrandTheme.RgbToHsl(rgb, out h, out s, out l); }
        public static Color HslToRgb(double h, double s, double l) { return BrandTheme.HslToRgb(h, s, l); }
    }
}

