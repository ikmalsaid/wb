using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace wb.UI
{
    public static class MaterialColors
    {
        public static readonly Color ToolbarBackground = Color.FromArgb(32, 33, 36);      // #202124 (Header / tabstrip)
        public static readonly Color NavBarBackground = Color.FromArgb(41, 42, 45);        // #292A2D (Navigation bar)
        public static readonly Color TabActive = Color.FromArgb(41, 42, 45);               // #292A2D (Active tab matches navbar)
        public static readonly Color TabInactive = Color.FromArgb(32, 33, 36);             // #202124 (Inactive tab)
        public static readonly Color TabHover = Color.FromArgb(48, 50, 54);                // #303236
        public static readonly Color TextPrimary = Color.FromArgb(232, 234, 237);          // #E8EAED (Light text)
        public static readonly Color TextSecondary = Color.FromArgb(154, 160, 166);       // #9AA0A6 (Muted text)
        public static readonly Color TextDisabled = Color.FromArgb(95, 99, 104);           // #5F6368
        public static readonly Color AddressBarBackground = Color.FromArgb(32, 33, 36);   // #202124
        public static readonly Color AddressBarBorder = Color.FromArgb(60, 64, 67);        // #3C4043
        public static readonly Color ButtonHover = Color.FromArgb(56, 58, 62);             // #383A3E
        public static readonly Color ButtonPressed = Color.FromArgb(70, 72, 77);           // #46484D
        public static readonly Color Separator = Color.FromArgb(60, 64, 67);               // #3C4043
        public static Color AccentBlue
        {
            get { return ThemeManager.CurrentAccentColor; }
            set { ThemeManager.SetAccentColor(value, "Custom"); }
        }
        public static readonly Color AccentYellow = Color.FromArgb(253, 214, 99);         // #FDD663 (Star filled)
        public static readonly Color StatusSuccess = Color.FromArgb(129, 201, 149);       // #81C995 (Green)
        public static readonly Color StatusDanger = Color.FromArgb(242, 139, 130);        // #F28B82 (Red)
        public static readonly Color StatusWarning = Color.FromArgb(253, 214, 99);        // #FDD663 (Yellow)
        public static readonly Color StatusBackground = Color.FromArgb(26, 26, 28);        // #1A1A1C
        public static readonly Color BadgeBackground = Color.FromArgb(102, 102, 102);     // #666666 (uBlock / Toolbar badge)
        public static readonly Color BadgeText = Color.White;
    }

    public static class DpiHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        public static float GetDpiScale(Control control)
        {
            if (control != null && control.IsHandleCreated)
            {
                try
                {
                    uint dpi = GetDpiForWindow(control.Handle);
                    if (dpi > 0) return dpi / 96.0f;
                }
                catch
                {
                }
            }

            try
            {
                using (Graphics g = (control != null && control.IsHandleCreated) ? control.CreateGraphics() : Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / 96.0f;
                }
            }
            catch
            {
                return 1.0f;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static int Scale(int basePixels, float dpiScale)
        {
            return Math.Max(1, (int)Math.Round(basePixels * dpiScale));
        }
    }

    /// <summary>
    /// Universal GDI+ High-DPI Drawing Helper.
    /// Completely eliminates GDI+ automatic DPI scaling blur, subpixel edge blur and interpolation artifacts
    /// by drawing with exact source pixel coordinates, HighQualityBicubic interpolation and HighQuality pixel offset mode.
    /// </summary>
    public static class DpiDrawingHelper
    {
        public static void DrawImagePixelPerfect(Graphics g, Image img, Rectangle destRect)
        {
            if (g == null || img == null || destRect.Width <= 0 || destRect.Height <= 0) return;

            InterpolationMode oldInterp = g.InterpolationMode;
            PixelOffsetMode oldOffset = g.PixelOffsetMode;
            SmoothingMode oldSmooth = g.SmoothingMode;

            try
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;

                g.DrawImage(img, destRect, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
            }
            finally
            {
                g.InterpolationMode = oldInterp;
                g.PixelOffsetMode = oldOffset;
                g.SmoothingMode = oldSmooth;
            }
        }

        public static void DrawImagePixelPerfect(Graphics g, Image img, int x, int y, int width, int height)
        {
            DrawImagePixelPerfect(g, img, new Rectangle(x, y, width, height));
        }

        public static void DrawImageUnscaled(Graphics g, Image img, int x, int y)
        {
            if (img == null) return;
            DrawImagePixelPerfect(g, img, new Rectangle(x, y, img.Width, img.Height));
        }
    }

    public static class SvgPathParser
    {
        public static GraphicsPath Parse(string pathData)
        {
            GraphicsPath path = new GraphicsPath();
            path.FillMode = FillMode.Winding;

            List<PathToken> tokens = Tokenize(pathData);
            int idx = 0;

            float curX = 0, curY = 0;
            float startX = 0, startY = 0;
            float lastControlX = 0, lastControlY = 0;
            char lastCmd = ' ';

            while (idx < tokens.Count)
            {
                PathToken token = tokens[idx++];
                if (token.Type != TokenType.Command) continue;
                char cmd = token.Command;

                switch (cmd)
                {
                    case 'M':
                    case 'm':
                        {
                            bool first = true;
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 'm') { x += curX; y += curY; }
                                if (first)
                                {
                                    path.StartFigure();
                                    curX = x; curY = y;
                                    startX = x; startY = y;
                                    first = false;
                                }
                                else
                                {
                                    path.AddLine(curX, curY, x, y);
                                    curX = x; curY = y;
                                }
                            }
                            lastControlX = curX; lastControlY = curY;
                            lastCmd = cmd;
                            break;
                        }
                    case 'L':
                    case 'l':
                        {
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 'l') { x += curX; y += curY; }
                                path.AddLine(curX, curY, x, y);
                                curX = x; curY = y;
                            }
                            lastControlX = curX; lastControlY = curY;
                            lastCmd = cmd;
                            break;
                        }
                    case 'H':
                    case 'h':
                        {
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x = tokens[idx++].Number;
                                if (cmd == 'h') x += curX;
                                path.AddLine(curX, curY, x, curY);
                                curX = x;
                            }
                            lastControlX = curX; lastControlY = curY;
                            lastCmd = cmd;
                            break;
                        }
                    case 'V':
                    case 'v':
                        {
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float y = tokens[idx++].Number;
                                if (cmd == 'v') y += curY;
                                path.AddLine(curX, curY, curX, y);
                                curY = y;
                            }
                            lastControlX = curX; lastControlY = curY;
                            lastCmd = cmd;
                            break;
                        }
                    case 'C':
                    case 'c':
                        {
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x1 = tokens[idx++].Number;
                                float y1 = tokens[idx++].Number;
                                float x2 = tokens[idx++].Number;
                                float y2 = tokens[idx++].Number;
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 'c')
                                {
                                    x1 += curX; y1 += curY;
                                    x2 += curX; y2 += curY;
                                    x += curX; y += curY;
                                }
                                path.AddBezier(curX, curY, x1, y1, x2, y2, x, y);
                                lastControlX = x2; lastControlY = y2;
                                curX = x; curY = y;
                            }
                            lastCmd = cmd;
                            break;
                        }
                    case 'S':
                    case 's':
                        {
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float x2 = tokens[idx++].Number;
                                float y2 = tokens[idx++].Number;
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 's') { x2 += curX; y2 += curY; x += curX; y += curY; }
                                float x1 = curX;
                                float y1 = curY;
                                if (lastCmd == 'C' || lastCmd == 'c' || lastCmd == 'S' || lastCmd == 's')
                                {
                                    x1 = 2 * curX - lastControlX;
                                    y1 = 2 * curY - lastControlY;
                                }
                                path.AddBezier(curX, curY, x1, y1, x2, y2, x, y);
                                lastControlX = x2; lastControlY = y2;
                                curX = x; curY = y;
                                lastCmd = cmd;
                            }
                            break;
                        }
                    case 'A':
                    case 'a':
                        {
                            while (idx < tokens.Count && tokens[idx].Type == TokenType.Number)
                            {
                                float rx = Math.Abs(tokens[idx++].Number);
                                float ry = Math.Abs(tokens[idx++].Number);
                                float angle = tokens[idx++].Number;
                                bool largeArc = tokens[idx++].Number != 0;
                                bool sweep = tokens[idx++].Number != 0;
                                float x = tokens[idx++].Number;
                                float y = tokens[idx++].Number;
                                if (cmd == 'a') { x += curX; y += curY; }

                                if (rx > 0.0001f && ry > 0.0001f)
                                {
                                    AddArcToBeziers(path, curX, curY, rx, ry, angle, largeArc, sweep, x, y);
                                }
                                else
                                {
                                    path.AddLine(curX, curY, x, y);
                                }
                                curX = x; curY = y;
                                lastControlX = curX; lastControlY = curY;
                            }
                            lastCmd = cmd;
                            break;
                        }
                    case 'Z':
                    case 'z':
                        {
                            path.CloseFigure();
                            curX = startX; curY = startY;
                            lastControlX = curX; lastControlY = curY;
                            lastCmd = cmd;
                            break;
                        }
                }
            }
            return path;
        }

        private static void AddArcToBeziers(GraphicsPath path, float x0, float y0, float rx, float ry, float angleDegrees, bool largeArc, bool sweep, float x, float y)
        {
            if (Math.Abs(x0 - x) < 0.0001f && Math.Abs(y0 - y) < 0.0001f) return;
            double angleRad = angleDegrees * Math.PI / 180.0;
            double cosAngle = Math.Cos(angleRad);
            double sinAngle = Math.Sin(angleRad);

            double dx2 = (x0 - x) / 2.0;
            double dy2 = (y0 - y) / 2.0;
            double x1p = cosAngle * dx2 + sinAngle * dy2;
            double y1p = -sinAngle * dx2 + cosAngle * dy2;

            double prx = rx, pry = ry;
            double P = (x1p * x1p) / (prx * prx) + (y1p * y1p) / (pry * pry);
            if (P > 1)
            {
                double s = Math.Sqrt(P);
                prx *= s;
                pry *= s;
            }

            double sign = (largeArc == sweep) ? -1.0 : 1.0;
            double num = (prx * prx * pry * pry) - (prx * prx * y1p * y1p) - (pry * pry * x1p * x1p);
            double den = (prx * prx * y1p * y1p) + (pry * pry * x1p * x1p);
            double coef = 0;
            if (den > 0.000001 && num > 0)
            {
                coef = sign * Math.Sqrt(num / den);
            }
            double cxp = coef * (prx * y1p / pry);
            double cyp = coef * -(pry * x1p / prx);

            double cx = cosAngle * cxp - sinAngle * cyp + (x0 + x) / 2.0;
            double cy = sinAngle * cxp + cosAngle * cyp + (y0 + y) / 2.0;

            double ux = (x1p - cxp) / prx;
            double uy = (y1p - cyp) / pry;
            double vx = (-x1p - cxp) / prx;
            double vy = (-y1p - cyp) / pry;

            double theta1 = AngleBetween(1, 0, ux, uy);
            double deltaTheta = AngleBetween(ux, uy, vx, vy);
            if (!sweep && deltaTheta > 0) deltaTheta -= 2.0 * Math.PI;
            else if (sweep && deltaTheta < 0) deltaTheta += 2.0 * Math.PI;

            int segments = (int)Math.Ceiling(Math.Abs(deltaTheta) / (Math.PI / 2.0));
            if (segments < 1) segments = 1;
            double segDelta = deltaTheta / segments;

            double curTheta = theta1;
            for (int i = 0; i < segments; i++)
            {
                double nextTheta = curTheta + segDelta;
                AddArcSegmentToBezier(path, cx, cy, prx, pry, cosAngle, sinAngle, curTheta, nextTheta);
                curTheta = nextTheta;
            }
        }

        private static double AngleBetween(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double len = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy);
            if (len < 0.000001) return 0;
            double cos = Math.Max(-1.0, Math.Min(1.0, dot / len));
            double angle = Math.Acos(cos);
            if ((ux * vy - uy * vx) < 0) angle = -angle;
            return angle;
        }

        private static void AddArcSegmentToBezier(GraphicsPath path, double cx, double cy, double rx, double ry,
            double cosAngle, double sinAngle, double theta1, double theta2)
        {
            double alpha = Math.Sin(theta2 - theta1) * (Math.Sqrt(4 + 3 * Math.Pow(Math.Tan((theta2 - theta1) / 2), 2)) - 1) / 3;
            double cos1 = Math.Cos(theta1), sin1 = Math.Sin(theta1);
            double cos2 = Math.Cos(theta2), sin2 = Math.Sin(theta2);

            double ep1x = rx * cos1, ep1y = ry * sin1;
            double ep2x = rx * cos2, ep2y = ry * sin2;

            double ep1dx = -rx * sin1, ep1dy = ry * cos1;
            double ep2dx = -rx * sin2, ep2dy = ry * cos2;

            double cp1x = ep1x + alpha * ep1dx;
            double cp1y = ep1y + alpha * ep1dy;
            double cp2x = ep2x - alpha * ep2dx;
            double cp2y = ep2y - alpha * ep2dy;

            PointF p1 = new PointF((float)(cosAngle * ep1x - sinAngle * ep1y + cx), (float)(sinAngle * ep1x + cosAngle * ep1y + cy));
            PointF c1 = new PointF((float)(cosAngle * cp1x - sinAngle * cp1y + cx), (float)(sinAngle * cp1x + cosAngle * cp1y + cy));
            PointF c2 = new PointF((float)(cosAngle * cp2x - sinAngle * cp2y + cx), (float)(sinAngle * cp2x + cosAngle * cp2y + cy));
            PointF p2 = new PointF((float)(cosAngle * ep2x - sinAngle * ep2y + cx), (float)(sinAngle * ep2x + cosAngle * ep2y + cy));

            path.AddBezier(p1, c1, c2, p2);
        }

        private enum TokenType { Command, Number }
        private struct PathToken
        {
            public TokenType Type;
            public char Command;
            public float Number;
        }

        private static List<PathToken> Tokenize(string d)
        {
            List<PathToken> list = new List<PathToken>();
            int i = 0, len = d.Length;
            while (i < len)
            {
                char c = d[i];
                if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
                if (char.IsLetter(c))
                {
                    list.Add(new PathToken { Type = TokenType.Command, Command = c });
                    i++;
                    continue;
                }
                if (c == '+' || c == '-' || c == '.' || char.IsDigit(c))
                {
                    int start = i;
                    bool hasDot = (c == '.');
                    i++;
                    while (i < len)
                    {
                        char next = d[i];
                        if (char.IsDigit(next)) i++;
                        else if (next == '.' && !hasDot) { hasDot = true; i++; }
                        else if ((next == 'e' || next == 'E') && i + 1 < len)
                        {
                            i++;
                            if (d[i] == '+' || d[i] == '-') i++;
                        }
                        else break;
                    }
                    string numStr = d.Substring(start, i - start);
                    float val;
                    if (float.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                    {
                        list.Add(new PathToken { Type = TokenType.Number, Number = val });
                    }
                    continue;
                }
                i++;
            }
            return list;
        }
    }

    public static class MaterialIcons
    {
        private static readonly object _syncLock = new object();
        private static readonly Dictionary<string, GraphicsPath> _pathCache = new Dictionary<string, GraphicsPath>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bitmap> _bitmapCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static string _iconsDir;

        public static string IconsDirectory
        {
            get
            {
                if (_iconsDir == null)
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string candidate1 = Path.Combine(baseDir, "assets", "icons");
                    string candidateCore = Path.Combine(baseDir, "core", "assets", "icons");
                    string candidateSys = Path.Combine(baseDir, "system", "assets", "icons");
                    string candidate2 = Path.Combine(baseDir, "..", "..", "assets", "icons");
                    string candidate3 = Path.Combine(Directory.GetCurrentDirectory(), "assets", "icons");

                    if (Directory.Exists(candidate1)) _iconsDir = Path.GetFullPath(candidate1);
                    else if (Directory.Exists(candidateCore)) _iconsDir = Path.GetFullPath(candidateCore);
                    else if (Directory.Exists(candidateSys)) _iconsDir = Path.GetFullPath(candidateSys);
                    else if (Directory.Exists(candidate2)) _iconsDir = Path.GetFullPath(candidate2);
                    else if (Directory.Exists(candidate3)) _iconsDir = Path.GetFullPath(candidate3);
                    else _iconsDir = Directory.Exists(candidateCore) ? candidateCore : candidate1;
                }
                return _iconsDir;
            }
        }

        public static void DrawIcon(Graphics g, string name, Rectangle destRect, Color? color = null)
        {
            if (g == null || destRect.Width <= 0 || destRect.Height <= 0) return;

            Color c = color ?? (string.Equals(name, "globe", StringComparison.OrdinalIgnoreCase) ? ThemeManager.AccentColor : MaterialColors.TextPrimary);

            // Fast-path: For standard square icons (99.9% of UI icon usage), draw pre-rendered cached bitmap
            if (destRect.Width == destRect.Height)
            {
                Bitmap bmp = GetIcon(name, destRect.Width, c, g.DpiX);
                if (bmp != null)
                {
                    DpiDrawingHelper.DrawImagePixelPerfect(g, bmp, destRect);
                    return;
                }
            }

            GraphicsPath gp = GetPath(name);
            if (gp == null) return;
            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                using (GraphicsPath clone = (GraphicsPath)gp.Clone())
                {
                    using (Matrix mat = new Matrix())
                    {
                        mat.Translate(destRect.X, destRect.Y);
                        mat.Scale((float)destRect.Width / 256f, (float)destRect.Height / 256f);
                        clone.Transform(mat);
                    }
                    using (SolidBrush brush = new SolidBrush(c))
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

        public static void DrawIcon(Graphics g, string name, int x, int y, int size, Color? color = null)
        {
            DrawIcon(g, name, new Rectangle(x, y, size, size), color);
        }

        public static Bitmap GetIcon(string name, int size = 16, Color? color = null)
        {
            return GetIcon(name, size, color, 96f);
        }

        public static Bitmap GetIcon(string name, int size, Color? color, float dpi)
        {
            Color c = color ?? ((string.Equals(name, "globe", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "house", StringComparison.OrdinalIgnoreCase)) ? ThemeManager.AccentColor : MaterialColors.TextPrimary);
            int dpiKey = (int)Math.Round(dpi > 0 ? dpi : 96f);
            string key = string.Format("{0}_{1}_{2}_{3}", name, size, c.ToArgb(), dpiKey);

            lock (_syncLock)
            {
                Bitmap cached;
                if (_bitmapCache.TryGetValue(key, out cached))
                {
                    try
                    {
                        if (cached != null)
                        {
                            int testWidth = cached.Width;
                            return cached;
                        }
                    }
                    catch
                    {
                        _bitmapCache.Remove(key);
                    }
                }

                GraphicsPath gp = GetPath(name);
                if (gp == null) return null;

                Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                try
                {
                    if (dpi > 0) bmp.SetResolution(dpi, dpi);
                }
                catch { }

                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);

                    using (GraphicsPath clone = (GraphicsPath)gp.Clone())
                    {
                        using (Matrix mat = new Matrix())
                        {
                            mat.Scale((float)size / 256f, (float)size / 256f);
                            clone.Transform(mat);
                        }
                        using (SolidBrush brush = new SolidBrush(c))
                        {
                            g.FillPath(brush, clone);
                        }
                    }
                }

                _bitmapCache[key] = bmp;
                return bmp;
            }
        }

        public static Bitmap GetDpiIcon(string name, float dpiScale, int baseSize = 16, Color? color = null)
        {
            int scaledSize = Math.Max(12, (int)Math.Round(baseSize * dpiScale));
            float targetDpi = 96.0f * (dpiScale > 0 ? dpiScale : 1.0f);
            return GetIcon(name, scaledSize, color, targetDpi);
        }

        private static readonly Dictionary<string, string> _fallbackSvgPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "warning", "M236.8,188,148.8,36a24,24,0,0,0-41.6,0L19.2,188A23.68,23.68,0,0,0,40,224H216a23.68,23.68,0,0,0,20.8-36ZM120,96a8,8,0,0,1,16,0v48a8,8,0,0,1-16,0Zm8,96a12,12,0,1,1,12-12A12,12,0,0,1,128,192Z" },
            { "warning-circle", "M128,24A104,104,0,1,0,232,128,104.11,104.11,0,0,0,128,24Zm0,192a88,88,0,1,1,88-88A88.1,88.1,0,0,1,128,216Zm-8-80V80a8,8,0,0,1,16,0v56a8,8,0,0,1-16,0Zm20,36a12,12,0,1,1-12-12A12,12,0,0,1,140,172Z" },
            { "stop", "M229.66,74.34l-48-48A8,8,0,0,0,176,24H80a8,8,0,0,0-5.66,2.34l-48,48A8,8,0,0,0,24,80v96a8,8,0,0,0,2.34,5.66l48,48A8,8,0,0,0,80,232h96a8,8,0,0,0,5.66-2.34l48-48A8,8,0,0,0,232,176V80A8,8,0,0,0,229.66,74.34ZM216,172.69,172.69,216H83.31L40,172.69V83.31L83.31,40h89.38L216,83.31ZM120,80v56a8,8,0,0,0,16,0V80a8,8,0,0,0-16,0Zm20,92a12,12,0,1,1-12-12A12,12,0,0,1,140,172Z" },
            { "warning-octagon", "M229.66,74.34l-48-48A8,8,0,0,0,176,24H80a8,8,0,0,0-5.66,2.34l-48,48A8,8,0,0,0,24,80v96a8,8,0,0,0,2.34,5.66l48,48A8,8,0,0,0,80,232h96a8,8,0,0,0,5.66-2.34l48-48A8,8,0,0,0,232,176V80A8,8,0,0,0,229.66,74.34ZM216,172.69,172.69,216H83.31L40,172.69V83.31L83.31,40h89.38L216,83.31ZM120,80v56a8,8,0,0,0,16,0V80a8,8,0,0,0-16,0Zm20,92a12,12,0,1,1-12-12A12,12,0,0,1,140,172Z" },
            { "shield-warning", "M208,40H48A16,16,0,0,0,32,56v56c0,52.72,25.52,84.67,46.93,102.19,23.06,18.86,46,25.27,47,25.53a8,8,0,0,0,4.2,0c1-.26,23.91-6.67,47-25.53C198.48,196.67,224,164.72,224,112V56A16,16,0,0,0,208,40Zm0,72c0,37.07-13.66,67.16-40.6,89.42A129.3,129.3,0,0,1,128,223.62a128.25,128.25,0,0,1-38.92-21.81C61.82,179.51,48,149.3,48,112l0-56,160,0ZM120,88v48a8,8,0,0,0,16,0V88a8,8,0,0,0-16,0Zm8,80a10,10,0,1,0,10,10A10,10,0,0,0,128,168Z" },
            { "pause", "M200,32a16,16,0,0,0-16,16V208a16,16,0,0,0,32,0V48A16,16,0,0,0,200,32Zm-128,0A16,16,0,0,0,56,48V208a16,16,0,0,0,32,0V48A16,16,0,0,0,72,32Z" },
            { "play", "M240,128a15.74,15.74,0,0,1-7.6,13.51L88.32,229.65a16,16,0,0,1-16.2.3A15.86,15.86,0,0,1,64,216.13V39.87a15.86,15.86,0,0,1,8.12-13.82,16,16,0,0,1,16.2.3L232.4,114.49A15.74,15.74,0,0,1,240,128Z" },
            { "folder", "M216,72H131.31L104,44.69A15.86,15.86,0,0,0,92.69,40H40A16,16,0,0,0,24,56V200a16,16,0,0,0,16,16H216a16,16,0,0,0,16-16V88A16,16,0,0,0,216,72Zm0,128H40V56H92.69l27.31,27.31A15.86,15.86,0,0,0,131.31,88H216Z" },
            { "folder-simple", "M216,72H131.31L104,44.69A15.86,15.86,0,0,0,92.69,40H40A16,16,0,0,0,24,56V200a16,16,0,0,0,16,16H216a16,16,0,0,0,16-16V88A16,16,0,0,0,216,72Zm0,128H40V56H92.69l27.31,27.31A15.86,15.86,0,0,0,131.31,88H216Z" },
            { "folder-open", "M216,72H131.31L104,44.69A15.86,15.86,0,0,0,92.69,40H40A16,16,0,0,0,24,56V200a16,16,0,0,0,16,16H216a16,16,0,0,0,16-16V88A16,16,0,0,0,216,72Zm0,128H40V56H92.69l27.31,27.31A15.86,15.86,0,0,0,131.31,88H216Z" },
            { "x", "M205.66,194.34a8,8,0,0,1-11.32,11.32L128,139.31,61.66,205.66a8,8,0,0,1-11.32-11.32L116.69,128,50.34,61.66A8,8,0,0,1,61.66,50.34L128,116.69l66.34-66.35a8,8,0,0,1,11.32,11.32L139.31,128Z" },
            { "x-circle", "M128,24A104,104,0,1,0,232,128,104.11,104.11,0,0,0,128,24Zm37.66,130.34a8,8,0,0,1-11.32,11.32L128,139.31l-26.34,26.35a8,8,0,0,1-11.32-11.32L116.69,128,90.34,101.66a8,8,0,0,1,11.32-11.32L128,116.69l26.34-26.35a8,8,0,0,1,11.32,11.32L139.31,128Z" },
            { "arrow-clockwise", "M224,128a96,96,0,0,1-96,96A95.8,95.8,0,0,1,38.16,168.4a8,8,0,0,1,14.68-6.38A79.86,79.86,0,0,0,128,208a80,80,0,0,0,80-80,8,8,0,0,1,16,0ZM48,128A80,80,0,0,1,128,48a79.86,79.86,0,0,1,75.16,52.02,8,8,0,0,1-14.68,6.38A95.8,95.8,0,0,0,128,32a96,96,0,0,0-96,96,8,8,0,0,1-16,0Z" },
            { "download-simple", "M224,144v64a8,8,0,0,1-8,8H40a8,8,0,0,1-8-8V144a8,8,0,0,1,16,0v56H208V144a8,8,0,0,1,16,0Zm-101.66,5.66a8,8,0,0,0,11.32,0l40-40a8,8,0,0,0-11.32-11.32L136,124.69V32a8,8,0,0,0-16,0v92.69L93.66,98.34a8,8,0,0,0-11.32,11.32Z" },
            { "check", "M229.66,77.66l-128,128a8,8,0,0,1-11.32,0l-56-56a8,8,0,0,1,11.32-11.32L96,188.69,218.34,66.34a8,8,0,0,1,11.32,11.32Z" },
            { "camera", "M208,56H180.28L166.58,35.45A16,16,0,0,0,153.28,28H102.72a16,16,0,0,0-13.3,7.45L75.72,56H48A24,24,0,0,0,24,80V192a24,24,0,0,0,24,24H208a24,24,0,0,0,24-24V80A24,24,0,0,0,208,56Zm8,136a8,8,0,0,1-8,8H48a8,8,0,0,1-8-8V80a8,8,0,0,1,8-8H80a8,8,0,0,0,6.66-3.63L100.28,48h55.44l13.62,20.37A8,8,0,0,0,176,72h32a8,8,0,0,1,8,8ZM128,96a44,44,0,1,0,44,44A44,44,0,0,0,128,96Zm0,72a28,28,0,1,1,28-28A28,28,0,0,1,128,168Z" },
            { "clock-counter-clockwise", "M136,80v48a8,8,0,0,1-8,8H88a8,8,0,0,1,0-16h32V80a8,8,0,0,1,16,0Zm-8-48A96,96,0,1,0,224,128,96.11,96.11,0,0,0,128,32Zm0,176a80,80,0,1,1,80-80A80.09,80.09,0,0,1,128,208Z" },
            { "palette", "M200.77,53.89A103.27,103.27,0,0,0,128,24h-1.07A104,104,0,0,0,24,128c0,43,26.58,79.06,69.36,94.17A32,32,0,0,0,136,192a16,16,0,0,1,16-16h46.21a31.81,31.81,0,0,0,31.2-24.88,104.43,104.43,0,0,0,2.59-24A103.28,103.28,0,0,0,200.77,53.89Zm13,93.71A15.89,15.89,0,0,1,198.21,160H152a32,32,0,0,0-32,32,16,16,0,0,1-21.31,15.07C62.49,194.3,40,164,40,128a88,88,0,0,1,87.09-88h.9a88.35,88.35,0,0,1,88,87.25A88.86,88.86,0,0,1,213.81,147.6ZM140,76a12,12,0,1,1-12-12A12,12,0,0,1,140,76ZM96,100A12,12,0,1,1,84,88,12,12,0,0,1,96,100Zm0,56a12,12,0,1,1-12-12A12,12,0,0,1,96,156Zm88-56a12,12,0,1,1-12-12A12,12,0,0,1,184,100Z" },
            { "pencil-simple", "M227.31,73.37,182.63,28.68a16,16,0,0,0-22.63,0L36.69,152A15.86,15.86,0,0,0,32,163.31V208a16,16,0,0,0,16,16H92.69A15.86,15.86,0,0,0,104,219.31L227.31,96a16,16,0,0,0,0-22.63ZM92.69,208H48V163.31l88-88L180.69,120ZM192,108.68,147.31,64l24-24L216,84.68Z" },
            { "highlighter", "M253.66,106.34a8,8,0,0,0-11.32,0L192,156.69,107.31,72l50.35-50.34a8,8,0,1,0-11.32-11.32L96,60.69A16,16,0,0,0,93.18,79.5L72,100.69a16,16,0,0,0,0,22.62L76.69,128,18.34,186.34a8,8,0,0,0,3.13,13.25l72,24A7.88,7.88,0,0,0,96,224a8,8,0,0,0,5.66-2.34L136,187.31l4.69,4.69a16,16,0,0,0,22.62,0l21.19-21.18A16,16,0,0,0,203.31,168l50.35-50.34A8,8,0,0,0,253.66,106.34ZM93.84,206.85l-55-18.35L88,139.31,124.69,176ZM152,180.69,83.31,112,104,91.31,172.69,160Z" },
            { "hand", "M188,48a27.75,27.75,0,0,0-12,2.71V44a28,28,0,0,0-54.65-8.6A28,28,0,0,0,80,60v64l-3.82-6.13a28,28,0,0,0-48.6,27.82c16,33.77,28.93,57.72,43.72,72.69C86.24,233.54,103.2,240,128,240a88.1,88.1,0,0,0,88-88V76A28,28,0,0,0,188,48Zm12,104a72.08,72.08,0,0,1-72,72c-20.38,0-33.51-4.88-45.33-16.85C69.44,193.74,57.26,171,41.9,138.58a6.36,6.36,0,0,0-.3-.58,12,12,0,0,1,20.79-12,1.76,1.76,0,0,0,.14.23l18.67,30A8,8,0,0,0,96,152V60a12,12,0,0,1,24,0v60a8,8,0,0,0,16,0V44a12,12,0,0,1,24,0v76a8,8,0,0,0,16,0V76a12,12,0,0,1,24,0Z" },
            { "arrow-up-right", "M200,64V168a8,8,0,0,1-16,0V83.31L69.66,197.66a8,8,0,0,1-11.32-11.32L172.69,72H88a8,8,0,0,1,0-16H192A8,8,0,0,1,200,64Z" },
            { "arrow-u-up-left", "M232,144a64.07,64.07,0,0,1-64,64H80a8,8,0,0,1,0-16h88a48,48,0,0,0,0-96H51.31l34.35,34.34a8,8,0,0,1-11.32,11.32l-48-48a8,8,0,0,1,0-11.32l48-48A8,8,0,0,1,85.66,45.66L51.31,80H168A64.07,64.07,0,0,1,232,144Z" },
            { "arrow-u-up-right", "M170.34,130.34,204.69,96H88a48,48,0,0,0,0,96h88a8,8,0,0,1,0,16H88A64,64,0,0,1,88,80H204.69L170.34,45.66a8,8,0,0,1,11.32-11.32l48,48a8,8,0,0,1,0,11.32l-48,48a8,8,0,0,1-11.32-11.32Z" },
            { "copy", "M216,32H88a8,8,0,0,0-8,8V80H40a8,8,0,0,0-8,8V216a8,8,0,0,0,8,8H168a8,8,0,0,0,8-8V176h40a8,8,0,0,0,8-8V40A8,8,0,0,0,216,32ZM160,208H48V96H160Zm48-48H176V88a8,8,0,0,0-8-8H96V48H208Z" },
            { "floppy-disk", "M219.31,72,184,36.69A15.86,15.86,0,0,0,172.69,32H48A16,16,0,0,0,32,48V208a16,16,0,0,0,16,16H208a16,16,0,0,0,16-16V83.31A15.86,15.86,0,0,0,219.31,72ZM168,208H88V152h80Zm40,0H184V152a16,16,0,0,0-16-16H88a16,16,0,0,0-16,16v56H48V48H172.69L208,83.31ZM160,72a8,8,0,0,1-8,8H96a8,8,0,0,1,0-16h56A8,8,0,0,1,160,72Z" },
            { "square", "M208,32H48A16,16,0,0,0,32,48V208a16,16,0,0,0,16,16H208a16,16,0,0,0,16-16V48A16,16,0,0,0,208,32Zm0,176H48V48H208V208Z" },
            { "crop", "M240,192a8,8,0,0,1-8,8H200v32a8,8,0,0,1-16,0V200H64a8,8,0,0,1-8-8V72H24a8,8,0,0,1,0-16H56V24a8,8,0,0,1,16,0V184H232A8,8,0,0,1,240,192ZM96,72h88v88a8,8,0,0,0,16,0V64a8,8,0,0,0-8-8H96a8,8,0,0,0,0,16Z" }
        };

        private static GraphicsPath GetPath(string name)
        {
            GraphicsPath path;
            lock (_syncLock)
            {
                if (_pathCache.TryGetValue(name, out path))
                {
                    return path;
                }
            }

            string cleanName = name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
            if (string.Equals(cleanName, "history", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = "clock-counter-clockwise";
            }
            string filePath = Path.Combine(IconsDirectory, cleanName + ".svg");
            if (!File.Exists(filePath))
            {
                string fallbackPath;
                if (_fallbackSvgPaths.TryGetValue(cleanName, out fallbackPath))
                {
                    try
                    {
                        path = SvgPathParser.Parse(fallbackPath);
                        lock (_syncLock)
                        {
                            _pathCache[name] = path;
                        }
                        return path;
                    }
                    catch { }
                }
                return null;
            }

            try
            {
                string text = File.ReadAllText(filePath);
                Match m = Regex.Match(text, "d=\"([^\"]+)\"");
                if (m.Success)
                {
                    path = SvgPathParser.Parse(m.Groups[1].Value);

                    // If SVG has non-standard dimensions (such as wb_icon at 845x461),
                    // normalize it into the standard 256x256 coordinate box expected by MaterialIcons.DrawIcon / GetIcon
                    Match mw = Regex.Match(text, "width=\"([^\"]+)\"");
                    Match mh = Regex.Match(text, "height=\"([^\"]+)\"");
                    float svgW = 256f, svgH = 256f;
                    if (mw.Success) float.TryParse(mw.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out svgW);
                    if (mh.Success) float.TryParse(mh.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out svgH);

                    if (svgW > 0 && svgH > 0 && (Math.Abs(svgW - 256f) > 1f || Math.Abs(svgH - 256f) > 1f))
                    {
                        float scale = Math.Min(256f / svgW, 256f / svgH);
                        float ox = (256f - svgW * scale) / 2f;
                        float oy = (256f - svgH * scale) / 2f;
                        using (Matrix normMat = new Matrix())
                        {
                            normMat.Scale(scale, scale);
                            normMat.Translate(ox, oy, MatrixOrder.Append);
                            path.Transform(normMat);
                        }
                    }

                    lock (_syncLock)
                    {
                        _pathCache[name] = path;
                    }
                    return path;
                }
            }
            catch
            {
            }
            return null;
        }

        public static void ClearCache()
        {
            lock (_syncLock)
            {
                _bitmapCache.Clear();
                _pathCache.Clear();
            }
        }
    }
}
