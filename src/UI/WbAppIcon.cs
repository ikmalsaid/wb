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
    /// <summary>
    /// Manages the application brand icon ('wb' Aero-Stripe high-speed monogram).
    /// Dynamically derives fluid two-tone speed gradients matching ThemeManager.CurrentAccentColor
    /// and automatically updates the window titlebar and taskbar icon for all open browser forms.
    /// Uses native vector SVG asset rendering with subpixel anti-aliasing for razor-sharp display at any scale.
    /// </summary>
    public static class WbAppIcon
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint WM_SETICON = 0x0080;
        private static readonly IntPtr ICON_SMALL = IntPtr.Zero;
        private static readonly IntPtr ICON_BIG = new IntPtr(1);
        private const int SM_CXICON = 11;
        private const int SM_CYICON = 12;
        private const int SM_CXSMICON = 49;
        private const int SM_CYSMICON = 50;
        private const uint GW_OWNER = 4;

        private static GraphicsPath _cachedVectorPath = null;
        private static float _svgWidth = 845f;
        private static float _svgHeight = 461f;
        private static Bitmap _cachedMaskBitmap = null;
        private static Icon _currentAppIcon = null;
        private static Icon _currentAppIconSmall = null;
        private static Icon _currentAppIconBig = null;
        private static Color _lastAccentColor = Color.Empty;
        private static readonly object _syncLock = new object();
        private static bool _isSubscribed = false;

        static WbAppIcon()
        {
            Initialize();
        }

        public static void Initialize()
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID("wb.Browser.App");
            }
            catch { }

            lock (_syncLock)
            {
                if (!_isSubscribed)
                {
                    _isSubscribed = true;
                    ThemeManager.AccentColorChanged += OnAccentColorChanged;
                }
            }
        }

        private static void OnAccentColorChanged(object sender, EventArgs e)
        {
            UpdateAllFormIcons();
        }

        /// <summary>
        /// Gets the path to the master Aero-Stripe vector SVG asset (or fallback PNG).
        /// </summary>
        public static string MasterIconPath
        {
            get
            {
                try
                {
                    string iconsDir = MaterialIcons.IconsDirectory;
                    string candidateSvg = Path.Combine(iconsDir, "wb_icon.svg");
                    if (File.Exists(candidateSvg)) return candidateSvg;

                    string candidatePng = Path.Combine(iconsDir, "wb_icon.png");
                    if (File.Exists(candidatePng)) return candidatePng;

                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string altSvg = Path.Combine(baseDir, "assets", "icons", "wb_icon.svg");
                    if (File.Exists(altSvg)) return altSvg;
                    string coreSvg = Path.Combine(baseDir, "core", "assets", "icons", "wb_icon.svg");
                    if (File.Exists(coreSvg)) return coreSvg;
                    string sysSvg = Path.Combine(baseDir, "system", "assets", "icons", "wb_icon.svg");
                    if (File.Exists(sysSvg)) return sysSvg;

                    string altPng = Path.Combine(baseDir, "assets", "icons", "wb_icon.png");
                    if (File.Exists(altPng)) return altPng;
                    string corePng = Path.Combine(baseDir, "core", "assets", "icons", "wb_icon.png");
                    if (File.Exists(corePng)) return corePng;
                    string sysPng = Path.Combine(baseDir, "system", "assets", "icons", "wb_icon.png");
                    if (File.Exists(sysPng)) return sysPng;
                }
                catch { }
                return null;
            }
        }

        private static GraphicsPath GetVectorPath()
        {
            lock (_syncLock)
            {
                if (_cachedVectorPath != null) return _cachedVectorPath;

                string path = MasterIconPath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path) && path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string text = File.ReadAllText(path);
                        Match m = Regex.Match(text, "d=\"([^\"]+)\"");
                        if (m.Success)
                        {
                            Match mw = Regex.Match(text, "width=\"([^\"]+)\"");
                            Match mh = Regex.Match(text, "height=\"([^\"]+)\"");
                            float w, h;
                            if (mw.Success && float.TryParse(mw.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out w) && w > 0)
                                _svgWidth = w;
                            if (mh.Success && float.TryParse(mh.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out h) && h > 0)
                                _svgHeight = h;

                            _cachedVectorPath = SvgPathParser.Parse(m.Groups[1].Value);
                            return _cachedVectorPath;
                        }
                    }
                    catch { }
                }

                return null;
            }
        }

        private static Bitmap GetMaskBitmap()
        {
            lock (_syncLock)
            {
                if (_cachedMaskBitmap != null) return _cachedMaskBitmap;

                string path = MasterIconPath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path) && !path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using (Bitmap temp = new Bitmap(path))
                        {
                            _cachedMaskBitmap = new Bitmap(temp);
                            return _cachedMaskBitmap;
                        }
                    }
                    catch { }
                }

                // Fallback procedural mask if file is not found
                _cachedMaskBitmap = CreateFallbackProceduralMask(512, 280);
                return _cachedMaskBitmap;
            }
        }

        /// <summary>
        /// Generates a high-quality Bitmap of the Aero-Stripe 'wb' monogram matching the given or current accent color.
        /// </summary>
        public static Bitmap GetAppBitmap(int width = 32, int height = 32, Color? accent = null)
        {
            Color targetAccent = accent ?? ThemeManager.CurrentAccentColor;
            Color cStart, cEnd;
            DeriveHarmonicGradient(targetAccent, out cStart, out cEnd);

            GraphicsPath vectorPath = GetVectorPath();
            if (vectorPath != null)
            {
                Bitmap scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);

                    float aspect = _svgWidth / _svgHeight;
                    float drawW = width;
                    float drawH = drawW / aspect;
                    if (drawH > height)
                    {
                        drawH = height;
                        drawW = drawH * aspect;
                    }

                    float drawX = (width - drawW) / 2f;
                    float drawY = (height - drawH) / 2f;

                    lock (_syncLock)
                    {
                        using (GraphicsPath clone = (GraphicsPath)vectorPath.Clone())
                        {
                            using (Matrix mat = new Matrix())
                            {
                                mat.Scale(drawW / _svgWidth, drawH / _svgHeight);
                                mat.Translate(drawX, drawY, MatrixOrder.Append);
                                clone.Transform(mat);
                            }

                            RectangleF gradRect = new RectangleF(drawX, drawY, Math.Max(1f, drawW), Math.Max(1f, drawH));
                            using (LinearGradientBrush brush = new LinearGradientBrush(gradRect, cStart, cEnd, LinearGradientMode.Horizontal))
                            {
                                g.FillPath(brush, clone);
                            }
                        }
                    }
                }
                return scaled;
            }

            // Fallback raster rendering
            Bitmap mask = GetMaskBitmap();
            if (mask == null) return new Bitmap(width, height);

            using (Bitmap fullRes = RenderAccentedBitmap(mask, cStart, cEnd))
            {
                Bitmap scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    float aspect = (float)fullRes.Width / fullRes.Height;
                    float drawW = width;
                    float drawH = drawW / aspect;
                    if (drawH > height)
                    {
                        drawH = height;
                        drawW = drawH * aspect;
                    }

                    float drawX = (width - drawW) / 2f;
                    float drawY = (height - drawH) / 2f;

                    g.DrawImage(fullRes, new RectangleF(drawX, drawY, drawW, drawH));
                }
                return scaled;
            }
        }

        /// <summary>
        /// Creates a genuine multi-resolution managed Windows Icon (16x16, 24x24, 32x32, 48x48, 64x64) in memory.
        /// Using a standard multi-resolution ICO stream ensures .NET fully owns the GDI/User icon handles,
        /// and Windows Taskbar and Alt+Tab receive authentic crisp icons matching the current accent color.
        /// </summary>
        private static Icon CreateManagedMultiResIcon(Color targetAccent)
        {
            int[] sizes = new int[] { 16, 24, 32, 48, 64 };
            byte[][] pngBytes = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap bmp = GetAppBitmap(sizes[i], sizes[i], targetAccent))
                using (MemoryStream pms = new MemoryStream())
                {
                    bmp.Save(pms, ImageFormat.Png);
                    pngBytes[i] = pms.ToArray();
                }
            }

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                // ICONDIR header
                bw.Write((ushort)0); // Reserved
                bw.Write((ushort)1); // Resource type: 1 = Icon
                bw.Write((ushort)sizes.Length); // Image count

                int offset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    byte[] png = pngBytes[i];
                    bw.Write((byte)(s >= 256 ? 0 : s)); // Width
                    bw.Write((byte)(s >= 256 ? 0 : s)); // Height
                    bw.Write((byte)0); // Color count
                    bw.Write((byte)0); // Reserved
                    bw.Write((ushort)1); // Planes
                    bw.Write((ushort)32); // Bits per pixel
                    bw.Write((uint)png.Length); // Size of image data
                    bw.Write((uint)offset); // Offset to image data
                    offset += png.Length;
                }

                for (int i = 0; i < sizes.Length; i++)
                {
                    bw.Write(pngBytes[i]);
                }

                bw.Flush();
                ms.Position = 0;
                return new Icon(ms);
            }
        }

        /// <summary>
        /// Gets the current application Icon tuned to ThemeManager.CurrentAccentColor.
        /// </summary>
        public static Icon GetAppIcon(Color? accent = null)
        {
            Color targetAccent = accent ?? ThemeManager.CurrentAccentColor;

            lock (_syncLock)
            {
                if (_currentAppIcon != null && _lastAccentColor == targetAccent)
                {
                    return _currentAppIcon;
                }

                try
                {
                    Icon oldIcon = _currentAppIcon;
                    Icon oldSm = _currentAppIconSmall;
                    Icon oldBig = _currentAppIconBig;

                    int bigW = Math.Max(32, GetSystemMetrics(SM_CXICON));
                    int bigH = Math.Max(32, GetSystemMetrics(SM_CYICON));
                    int smW = Math.Max(16, GetSystemMetrics(SM_CXSMICON));
                    int smH = Math.Max(16, GetSystemMetrics(SM_CYSMICON));

                    _currentAppIcon = CreateManagedMultiResIcon(targetAccent);
                    _currentAppIconSmall = new Icon(_currentAppIcon, smW, smH);
                    _currentAppIconBig = new Icon(_currentAppIcon, bigW, bigH);
                    _lastAccentColor = targetAccent;

                    if (oldIcon != null) try { oldIcon.Dispose(); } catch { }
                    if (oldSm != null) try { oldSm.Dispose(); } catch { }
                    if (oldBig != null) try { oldBig.Dispose(); } catch { }

                    return _currentAppIcon;
                }
                catch
                {
                    return SystemIcons.Application;
                }
            }
        }

        /// <summary>
        /// Updates the Window and Taskbar icon of a specific Form to match the active accent color.
        /// Sends native WM_SETICON messages (ICON_BIG and ICON_SMALL) to ensure the Windows Taskbar
        /// and Alt-Tab immediately adopt the current accent color.
        /// </summary>
        public static void UpdateFormIcon(Form form, Color? accent = null)
        {
            if (form == null || form.IsDisposed) return;

            try
            {
                if (form.InvokeRequired)
                {
                    form.BeginInvoke(new Action(() => UpdateFormIcon(form, accent)));
                    return;
                }

                Color targetAccent = accent ?? ThemeManager.CurrentAccentColor;
                Icon icon = GetAppIcon(targetAccent);

                // Set WinForms form icon (WinForms automatically updates the titlebar and default taskbar icon)
                form.Icon = icon;

                if (form.IsHandleCreated)
                {
                    lock (_syncLock)
                    {
                        if (_currentAppIconBig != null)
                        {
                            SendMessage(form.Handle, WM_SETICON, ICON_BIG, _currentAppIconBig.Handle);
                        }
                        if (_currentAppIconSmall != null)
                        {
                            SendMessage(form.Handle, WM_SETICON, ICON_SMALL, _currentAppIconSmall.Handle);
                        }

                        // Also update owner window if WinForms or system created one
                        try
                        {
                            IntPtr hOwner = GetWindow(form.Handle, GW_OWNER);
                            if (hOwner != IntPtr.Zero)
                            {
                                if (_currentAppIconBig != null)
                                    SendMessage(hOwner, WM_SETICON, ICON_BIG, _currentAppIconBig.Handle);
                                if (_currentAppIconSmall != null)
                                    SendMessage(hOwner, WM_SETICON, ICON_SMALL, _currentAppIconSmall.Handle);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Updates the icon of all currently open application forms.
        /// </summary>
        public static void UpdateAllFormIcons()
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
                    UpdateFormIcon(snapshot[i]);
                }
            }
            catch { }
        }

        /// <summary>
        /// Draws the accented Aero-Stripe monogram directly onto the destination Graphics.
        /// </summary>
        public static void DrawAppIcon(Graphics g, Rectangle destRect, Color? accent = null)
        {
            if (g == null || destRect.Width <= 0 || destRect.Height <= 0) return;

            using (Bitmap iconBmp = GetAppBitmap(destRect.Width, destRect.Height, accent))
            {
                g.DrawImage(iconBmp, destRect);
            }
        }

        /// <summary>
        /// High-performance safe color remapping using Marshal.Copy and linear interpolation.
        /// </summary>
        private static Bitmap RenderAccentedBitmap(Bitmap maskBmp, Color cStart, Color cEnd)
        {
            int w = maskBmp.Width;
            int h = maskBmp.Height;

            // Pre-calculate gradient look-up table across width
            byte[] lutR = new byte[w];
            byte[] lutG = new byte[w];
            byte[] lutB = new byte[w];
            for (int x = 0; x < w; x++)
            {
                float t = (float)x / Math.Max(1, w - 1);
                int r = (int)(cStart.R + (cEnd.R - cStart.R) * t);
                int g = (int)(cStart.G + (cEnd.G - cStart.G) * t);
                int b = (int)(cStart.B + (cEnd.B - cStart.B) * t);
                lutR[x] = (byte)Math.Max(0, Math.Min(255, r));
                lutG[x] = (byte)Math.Max(0, Math.Min(255, g));
                lutB[x] = (byte)Math.Max(0, Math.Min(255, b));
            }

            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData maskData = maskBmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData resData = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = maskData.Stride;
                int totalBytes = Math.Abs(stride) * h;
                byte[] maskBytes = new byte[totalBytes];
                byte[] resBytes = new byte[totalBytes];

                Marshal.Copy(maskData.Scan0, maskBytes, 0, totalBytes);

                for (int y = 0; y < h; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int px = rowOffset + (x * 4);
                        byte alpha = maskBytes[px + 3];

                        if (alpha == 0)
                        {
                            resBytes[px + 0] = 0;
                            resBytes[px + 1] = 0;
                            resBytes[px + 2] = 0;
                            resBytes[px + 3] = 0;
                        }
                        else
                        {
                            resBytes[px + 0] = lutB[x];
                            resBytes[px + 1] = lutG[x];
                            resBytes[px + 2] = lutR[x];
                            resBytes[px + 3] = alpha;
                        }
                    }
                }

                Marshal.Copy(resBytes, 0, resData.Scan0, totalBytes);
            }
            finally
            {
                maskBmp.UnlockBits(maskData);
                result.UnlockBits(resData);
            }

            return result;
        }

        /// <summary>
        /// Derives a complementary, luminous two-tone kinetic speed gradient anchored to the given accent color.
        /// </summary>
        public static void DeriveHarmonicGradient(Color accent, out Color cStart, out Color cEnd)
        {
            float h, s, l;
            RgbToHsl(accent, out h, out s, out l);

            // Leading edge (speed stripes): slightly brighter and shifted ~22 deg
            float hStart = (h - 22f + 360f) % 360f;
            float lStart = Math.Min(0.85f, l * 1.12f);
            float sStart = Math.Min(1.0f, s * 1.05f);

            // Trailing edge ('b' loop): slightly deeper and shifted ~22 deg
            float hEnd = (h + 22f) % 360f;
            float lEnd = Math.Max(0.42f, l * 0.90f);
            float sEnd = Math.Min(1.0f, s * 1.08f);

            cStart = HslToRgb(hStart, sStart, lStart);
            cEnd = HslToRgb(hEnd, sEnd, lEnd);
        }

        private static void RgbToHsl(Color rgb, out float h, out float s, out float l)
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

        private static Color HslToRgb(float h, float s, float l)
        {
            if (s == 0)
            {
                int val = (int)(l * 255f);
                return Color.FromArgb(val, val, val);
            }

            float q = (l < 0.5f) ? (l * (1f + s)) : (l + s - l * s);
            float p = 2f * l - q;
            float hNorm = h / 360f;

            float tr = hNorm + 1f / 3f;
            float tg = hNorm;
            float tb = hNorm - 1f / 3f;

            return Color.FromArgb(
                (int)(HueToRgb(p, q, tr) * 255f),
                (int)(HueToRgb(p, q, tg) * 255f),
                (int)(HueToRgb(p, q, tb) * 255f)
            );
        }

        private static float HueToRgb(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        private static Bitmap CreateFallbackProceduralMask(int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (Font font = new Font("Segoe UI", height * 0.55f, FontStyle.Bold | FontStyle.Italic))
                using (SolidBrush brush = new SolidBrush(Color.White))
                {
                    g.DrawString("wb", font, brush, 20, 20);
                }
            }
            return bmp;
        }
    }
}
