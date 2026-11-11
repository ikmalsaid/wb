using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using wb.Core;

namespace wb.UI
{
    /// <summary>
    /// Represents a named accent color preset in the bright spectrum.
    /// </summary>
    public class AccentColorPreset
    {
        public string Name { get; private set; }
        public Color Color { get; private set; }

        public AccentColorPreset(string name, Color color)
        {
            Name = name;
            Color = color;
        }
    }

    /// <summary>
    /// Persistent theme configuration model stored in JSON.
    /// </summary>
    public class ThemeConfigModel
    {
        public string PresetName { get; set; }
        public int ColorArgb { get; set; }
        public bool IsRandomMode { get; set; }
        public bool IsExplicitUserChoice { get; set; }

        public ThemeConfigModel()
        {
            PresetName = AppStrings.Theme.PresetRandom;
            ColorArgb = 0;
            IsRandomMode = true;
            IsExplicitUserChoice = false;
        }
    }

    /// <summary>
    /// Central manager for browser accent color themes (bright spectrum palette,
    /// dynamic random color generator, custom color picking and persistent storage).
    /// </summary>
    public static class ThemeManager
    {
        private const string ConfigFileName = "theme_config.json";
        private static readonly Color DefaultColor = Color.FromArgb(138, 180, 248); // #8AB4F8

        private static readonly List<AccentColorPreset> _presets = new List<AccentColorPreset>
        {
            new AccentColorPreset(AppStrings.Theme.PresetDefaultBlue, Color.FromArgb(138, 180, 248)),   // #8AB4F8 (Google Material blue)
            new AccentColorPreset(AppStrings.Theme.PresetCyberCyan, Color.FromArgb(112, 239, 222)),     // #70EFDE (Electric turquoise)
            new AccentColorPreset(AppStrings.Theme.PresetMintGreen, Color.FromArgb(129, 201, 149)),     // #81C995 (Fresh green)
            new AccentColorPreset(AppStrings.Theme.PresetVibrantLime, Color.FromArgb(204, 255, 144)),   // #CCFF90 (Punchy lime)
            new AccentColorPreset(AppStrings.Theme.PresetSolarAmber, Color.FromArgb(253, 214, 99)),     // #FDD663 (Golden yellow)
            new AccentColorPreset(AppStrings.Theme.PresetCoralOrange, Color.FromArgb(255, 138, 101)),   // #FF8A65 (Sunset orange)
            new AccentColorPreset(AppStrings.Theme.PresetNeonCrimson, Color.FromArgb(242, 139, 130)),   // #F28B82 (Bright coral red)
            new AccentColorPreset(AppStrings.Theme.PresetHotPink, Color.FromArgb(255, 128, 171)),       // #FF80AB (Vivid magenta)
            new AccentColorPreset(AppStrings.Theme.PresetElectricPurple, Color.FromArgb(197, 138, 249)), // #C58AF9 (Neon violet)
            new AccentColorPreset(AppStrings.Theme.PresetSkyBlue, Color.FromArgb(79, 195, 247))         // #4FC3F7 (Radiant sky blue)
        };

        private static Color _currentAccentColor = DefaultColor;
        private static string _currentPresetName = AppStrings.Theme.PresetRandom;
        private static bool _isRandomMode = true;
        private static bool _isInitialized = false;

        public static event EventHandler AccentColorChanged;

        static ThemeManager()
        {
            _currentAccentColor = GenerateRandomBrightColor(Color.Empty);
            _currentPresetName = "Random";
            _isRandomMode = true;
            Initialize();
        }

        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            try
            {
                ThemeConfigModel config = StorageHelper.LoadObject<ThemeConfigModel>(ConfigFileName);
                // BY DEFAULT, random color accent is always used!
                // Only use a fixed color if the user EXPLICITLY selected a custom preset or color from the menu.
                bool hasExplicitFixedChoice = (config != null &&
                                               config.IsExplicitUserChoice &&
                                               !config.IsRandomMode &&
                                               config.ColorArgb != 0 &&
                                               !string.IsNullOrEmpty(config.PresetName) &&
                                               !string.Equals(config.PresetName, AppStrings.Theme.PresetRandom, StringComparison.OrdinalIgnoreCase) &&
                                               !string.Equals(config.PresetName, AppStrings.Theme.PresetDefaultBlue, StringComparison.OrdinalIgnoreCase));

                if (hasExplicitFixedChoice)
                {
                    _isRandomMode = false;
                    _currentAccentColor = Color.FromArgb(config.ColorArgb);
                    _currentPresetName = config.PresetName;
                }
                else
                {
                    _isRandomMode = true;
                    _currentPresetName = AppStrings.Theme.PresetRandom;
                    Color previousColor = (config != null && config.ColorArgb != 0) ? Color.FromArgb(config.ColorArgb) : _currentAccentColor;
                    _currentAccentColor = GenerateRandomBrightColor(previousColor);
                    SaveConfig(isExplicit: (config != null && config.IsExplicitUserChoice && config.IsRandomMode));
                }
            }
            catch
            {
                _isRandomMode = true;
                _currentAccentColor = GenerateRandomBrightColor(Color.Empty);
                _currentPresetName = AppStrings.Theme.PresetRandom;
            }
            MaterialIcons.ClearCache();
        }

        public static void ResetInitialization()
        {
            _isInitialized = false;
        }

        public static Color CurrentAccentColor
        {
            get { return _currentAccentColor; }
        }

        public static Color AccentColor
        {
            get { return _currentAccentColor; }
        }

        public static string CurrentPresetName
        {
            get { return _currentPresetName; }
        }

        public static bool IsRandomMode
        {
            get { return _isRandomMode; }
        }

        public static IList<AccentColorPreset> Presets
        {
            get { return _presets.AsReadOnly(); }
        }

        public static void SetPreset(string presetName)
        {
            foreach (AccentColorPreset preset in _presets)
            {
                if (string.Equals(preset.Name, presetName, StringComparison.OrdinalIgnoreCase))
                {
                    _isRandomMode = false;
                    _currentPresetName = preset.Name;
                    _currentAccentColor = preset.Color;
                    SaveConfig(isExplicit: true);
                    NotifyColorChanged();
                    return;
                }
            }
        }

        public static void SetAccentColor(Color color, string presetName = AppStrings.Theme.PresetCustom)
        {
            _isRandomMode = false;
            _currentPresetName = presetName;
            _currentAccentColor = color;
            SaveConfig(isExplicit: true);
            NotifyColorChanged();
        }

        public static void SetRandomColor()
        {
            _isRandomMode = true;
            _currentPresetName = AppStrings.Theme.PresetRandom;
            _currentAccentColor = GenerateRandomBrightColor(_currentAccentColor);
            SaveConfig(isExplicit: true);
            NotifyColorChanged();
        }

        private static void SaveConfig(bool isExplicit = false)
        {
            try
            {
                ThemeConfigModel config = new ThemeConfigModel
                {
                    PresetName = _currentPresetName,
                    ColorArgb = _currentAccentColor.ToArgb(),
                    IsRandomMode = _isRandomMode,
                    IsExplicitUserChoice = isExplicit
                };
                StorageHelper.SaveObject<ThemeConfigModel>(ConfigFileName, config);
            }
            catch
            {
            }
        }

        private static void NotifyColorChanged()
        {
            MaterialIcons.ClearCache();
            if (AccentColorChanged != null)
            {
                AccentColorChanged(null, EventArgs.Empty);
            }
            ApplyToOpenForms();
        }

        public static void ApplyToOpenForms()
        {
            try
            {
                FormCollection openForms = Application.OpenForms;
                if (openForms == null) return;
                List<Form> snapshot = new List<Form>(openForms.Count);
                for (int i = 0; i < openForms.Count; i++)
                {
                    try
                    {
                        Form f = openForms[i];
                        if (f != null && !f.IsDisposed) snapshot.Add(f);
                    }
                    catch { }
                }
                for (int i = 0; i < snapshot.Count; i++)
                {
                    try
                    {
                        if (!snapshot[i].IsDisposed)
                        {
                            snapshot[i].Invalidate(true);
                            WbAppIcon.UpdateFormIcon(snapshot[i]);
                        }
                    }
                    catch { }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Generates a pleasant pastel color across the 360-degree hue spectrum.
        /// Uses moderate saturation (50-70%) and tuned lightness (72-82%) to ensure optimal contrast
        /// against dark backgrounds (#202124 / #292A2D) without harsh glare or dark tones. When
        /// previousColor is provided, ensures the new hue is at least 45 degrees away so each launch
        /// or random pick produces an instantly noticeable color change.
        /// </summary>
        public static Color GenerateRandomBrightColor(Color? previousColor = null)
        {
            Random rand = new Random(Guid.NewGuid().GetHashCode());
            double hue = rand.NextDouble() * 360.0;
            if (previousColor.HasValue && !previousColor.Value.IsEmpty && previousColor.Value.A > 0)
            {
                float prevHue = previousColor.Value.GetHue();
                double diff = Math.Abs(hue - prevHue);
                if (diff > 180.0) diff = 360.0 - diff;
                if (diff < 45.0)
                {
                    hue = (prevHue + 45.0 + (rand.NextDouble() * 270.0)) % 360.0;
                }
            }
            // Pastel range: moderate saturation (50% - 70%) and high lightness (72% - 82%) to eliminate dark, ugly colors
            double saturation = 0.50 + (rand.NextDouble() * 0.20);
            double lightness = 0.72 + (rand.NextDouble() * 0.10);
            return HslToRgb(hue, saturation, lightness);
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

        public static Bitmap CreateColorSwatch(Color color, int size = 16)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (SolidBrush b = new SolidBrush(color))
                {
                    g.FillEllipse(b, 2, 2, size - 4, size - 4);
                }
                using (Pen p = new Pen(Color.FromArgb(140, 255, 255, 255), 1f))
                {
                    g.DrawEllipse(p, 2, 2, size - 4, size - 4);
                }
            }
            return bmp;
        }
    }
}
