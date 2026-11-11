using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace wb.Installer
{
    /// <summary>
    /// Vector icon provider and rasterizer with subpixel anti-aliasing and runtime registration.
    /// Bundles built-in Phosphor SVG paths (256x256) and loads external SVG icons dynamically.
    /// </summary>
    public static class SetupIcons
    {
        private static readonly Dictionary<string, GraphicsPath> _cache = new Dictionary<string, GraphicsPath>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> _fallbackPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "download-simple", "M224,144v64a8,8,0,0,1-8,8H40a8,8,0,0,1-8-8V144a8,8,0,0,1,16,0v56H208V144a8,8,0,0,1,16,0Zm-101.66,5.66a8,8,0,0,0,11.32,0l40-40a8,8,0,0,0-11.32-11.32L136,124.69V32a8,8,0,0,0-16,0v92.69L93.66,98.34a8,8,0,0,0-11.32,11.32Z" },
            { "rocket-launch", "M223.85,47.12a16,16,0,0,0-15-15c-12.58-.75-44.73.4-71.41,27.07L132.69,64H74.36A15.91,15.91,0,0,0,63,68.68L28.7,103a16,16,0,0,0,9.07,27.16l38.47,5.37,44.21,44.21,5.37,38.49a15.94,15.94,0,0,0,10.78,12.92,16.11,16.11,0,0,0,5.1.83A15.91,15.91,0,0,0,153,227.3L187.32,193A15.91,15.91,0,0,0,192,181.64V123.31l4.77-4.77C223.45,91.86,224.6,59.71,223.85,47.12ZM74.36,80h42.33L77.16,119.52,40,114.34Zm74.41-9.45a76.65,76.65,0,0,1,59.11-22.47,76.46,76.46,0,0,1-22.42,59.16L128,164.68,91.32,128ZM176,181.64,141.67,216l-5.19-37.17L176,139.31Zm-74.16,9.5C97.34,201,82.29,224,40,224a8,8,0,0,1-8-8c0-42.29,23-57.34,32.86-61.85a8,8,0,0,1,6.64,14.56c-6.43,2.93-20.62,12.36-23.12,38.91,26.55-2.5,36-16.69,38.91-23.12a8,8,0,1,1,14.56,6.64Z" },
            { "folder-simple", "M216,72H131.31L104,44.69A15.86,15.86,0,0,0,92.69,40H40A16,16,0,0,0,24,56V200a16,16,0,0,0,16,16H216a16,16,0,0,0,16-16V88A16,16,0,0,0,216,72Zm0,128H40V56H92.69l27.31,27.31A15.86,15.86,0,0,0,131.31,88H216Z" },
            { "trash", "M216,48H176V40a24,24,0,0,0-24-24H104A24,24,0,0,0,80,40v8H40a8,8,0,0,0,0,16h8V208a16,16,0,0,0,16,16H192a16,16,0,0,0,16-16V64h8a8,8,0,0,0,0-16ZM96,40a8,8,0,0,1,8-8h48a8,8,0,0,1,8,8v8H96Zm96,168H64V64H192ZM112,104v64a8,8,0,0,1-16,0V104a8,8,0,0,1,16,0Zm48,0v64a8,8,0,0,1-16,0V104a8,8,0,0,1,16,0Z" },
            { "check", "M229.66,77.66l-128,128a8,8,0,0,1-11.32,0l-56-56a8,8,0,0,1,11.32-11.32L96,188.69,218.34,66.34a8,8,0,0,1,11.32,11.32Z" }
        };

        /// <summary>
        /// Registers or overrides a vector icon path for dynamic rendering.
        /// </summary>
        public static void RegisterIcon(string iconName, string svgPathData)
        {
            if (string.IsNullOrEmpty(iconName) || string.IsNullOrEmpty(svgPathData)) return;
            lock (_cache)
            {
                _fallbackPaths[iconName] = svgPathData;
                _cache.Remove(iconName);
            }
        }

        public static GraphicsPath GetPath(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return null;

            lock (_cache)
            {
                GraphicsPath cached;
                if (_cache.TryGetValue(iconName, out cached)) return cached;

                string pathData = null;

                // 1. Embedded assembly resource (icon.<name>.svg)
                try
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    string resName = "icon." + iconName.ToLowerInvariant() + ".svg";
                    using (Stream s = asm.GetManifestResourceStream(resName))
                    {
                        if (s != null)
                        {
                            using (StreamReader sr = new StreamReader(s))
                            {
                                string svg = sr.ReadToEnd();
                                Match m = Regex.Match(svg, "d=\"([^\"]+)\"");
                                if (m.Success) pathData = m.Groups[1].Value;
                            }
                        }
                    }
                }
                catch { }

                // 2. Local disk (assets\icons\<name>.svg)
                if (string.IsNullOrEmpty(pathData))
                {
                    try
                    {
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string curDir = Directory.GetCurrentDirectory();
                        string[] candidates = new string[]
                        {
                            Path.Combine(baseDir, "installer", "assets", "icons", iconName + ".svg"),
                            Path.Combine(baseDir, "assets", "icons", iconName + ".svg"),
                            Path.Combine(curDir, "installer", "assets", "icons", iconName + ".svg"),
                            Path.Combine(curDir, "assets", "icons", iconName + ".svg"),
                            Path.Combine(baseDir, "..", "installer", "assets", "icons", iconName + ".svg"),
                            Path.Combine(baseDir, "..", "assets", "icons", iconName + ".svg")
                        };
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            if (File.Exists(candidates[i]))
                            {
                                string svg = File.ReadAllText(candidates[i]);
                                Match m = Regex.Match(svg, "d=\"([^\"]+)\"");
                                if (m.Success)
                                {
                                    pathData = m.Groups[1].Value;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                // 3. Fallback dictionary
                if (string.IsNullOrEmpty(pathData))
                {
                    _fallbackPaths.TryGetValue(iconName, out pathData);
                }

                if (!string.IsNullOrEmpty(pathData))
                {
                    GraphicsPath gp = SvgPathParser.Parse(pathData);
                    _cache[iconName] = gp;
                    return gp;
                }

                return null;
            }
        }

        public static void DrawIcon(Graphics g, string iconName, Rectangle destRect, Color color)
        {
            if (g == null || destRect.Width <= 0 || destRect.Height <= 0) return;

            GraphicsPath path = GetPath(iconName);
            if (path == null) return;

            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                using (GraphicsPath clone = (GraphicsPath)path.Clone())
                using (Matrix mat = new Matrix())
                {
                    mat.Translate(destRect.X, destRect.Y);
                    mat.Scale(destRect.Width / 256f, destRect.Height / 256f);
                    clone.Transform(mat);

                    using (SolidBrush brush = new SolidBrush(color))
                    {
                        g.FillPath(brush, clone);
                    }
                }
            }
            finally
            {
                g.Restore(state);
            }
        }
    }
}

