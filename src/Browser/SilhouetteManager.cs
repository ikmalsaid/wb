using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using wb.Core;

namespace wb.Browser
{
    /// <summary>
    /// Persistent configuration model for start page background silhouette or wallpaper.
    /// </summary>
    public class SilhouetteConfigModel
    {
        public bool IsRandomMode { get; set; }
        public string SelectedFileName { get; set; }
        public string ColorStyle { get; set; } // "Accent" or "White"
        public int ZoomPercent { get; set; }
        public string Alignment { get; set; } // "Right", "Center", "Left"
        public bool IsMirrored { get; set; } // Flip X
        public bool IsFlipped { get; set; } // Flip Y
        public string DisplayMode { get; set; } // "Auto", "Wallpaper", "Silhouette"
        public bool IsHidden { get; set; }

        public SilhouetteConfigModel()
        {
            IsRandomMode = true;
            SelectedFileName = string.Empty;
            ColorStyle = "Accent";
            ZoomPercent = 85;
            Alignment = "Right";
            IsMirrored = false;
            IsFlipped = false;
            DisplayMode = "Auto";
            IsHidden = false;
        }
    }

    /// <summary>
    /// Central manager for Start Page background silhouettes and wallpapers:
    /// - Scans dedicated assets/images directory and custom/ subfolder
    /// - Custom files stored in assets/images/custom/ as WebP format
    /// - Auto-detects if an image is a silhouette (cutout) or wallpaper (full bleed cover)
    /// - Dynamic random cycling or locked "keep one" selection
    /// - 1-click Accent Color vs Pure White color style toggle
    /// - Interactive silhouette zoom scale (30% to 150%)
    /// - Direction (Left, Center, Right), mirror and flip
    /// </summary>
    public static class SilhouetteManager
    {
        private const string ConfigFileName = "silhouette_config.json";
        private static readonly object _syncLock = new object();

        private static string _silhouettesDir;
        private static List<string> _availableFiles = new List<string>();
        private static string _activeFilePath;
        private static Bitmap _cachedActiveBitmap;

        private static bool _isRandomMode = true;
        private static bool _isHidden = false;
        private static string _colorStyle = "Accent"; // "Accent" or "White"
        private static int _zoomPercent = 85;
        private static string _alignment = "Right"; // "Right", "Center", "Left"
        private static bool _isMirrored = false;
        private static bool _isFlipped = false;
        private static string _displayMode = "Auto"; // "Auto", "Wallpaper", "Silhouette"
        private static bool _isActiveWallpaper = false;
        private static bool _isInitialized = false;

        public static event EventHandler SilhouetteChanged;

        static SilhouetteManager()
        {
            Initialize();
        }

        public static void Initialize()
        {
            lock (_syncLock)
            {
                if (_isInitialized) return;
                _isInitialized = true;

                ResolveDirectory();
                EnsureCustomDirectory();
                RescanInternal();

                try
                {
                    SilhouetteConfigModel config = StorageHelper.LoadObject<SilhouetteConfigModel>(ConfigFileName);
                    if (config != null)
                    {
                        _isRandomMode = config.IsRandomMode;
                        _isHidden = config.IsHidden;
                        _colorStyle = string.Equals(config.ColorStyle, "White", StringComparison.OrdinalIgnoreCase) ? "White" : "Accent";
                        _zoomPercent = Math.Max(30, Math.Min(150, config.ZoomPercent > 0 ? config.ZoomPercent : 85));

                        if (!string.IsNullOrEmpty(config.Alignment))
                        {
                            if (string.Equals(config.Alignment, "Left", StringComparison.OrdinalIgnoreCase))
                                _alignment = "Left";
                            else if (string.Equals(config.Alignment, "Center", StringComparison.OrdinalIgnoreCase))
                                _alignment = "Center";
                            else
                                _alignment = "Right";
                        }
                        _isMirrored = config.IsMirrored;
                        _isFlipped = config.IsFlipped;

                        if (!string.IsNullOrEmpty(config.DisplayMode))
                        {
                            _displayMode = config.DisplayMode;
                        }

                        if (!_isRandomMode && !string.IsNullOrEmpty(config.SelectedFileName))
                        {
                            string match = FindFileByExactOrBaseName(config.SelectedFileName);
                            if (!string.IsNullOrEmpty(match))
                            {
                                _activeFilePath = match;
                            }
                        }
                    }
                }
                catch { }

                if (_isRandomMode || string.IsNullOrEmpty(_activeFilePath))
                {
                    PickRandomSilhouetteInternal();
                }

                // Startup Safeguard: Bundled silhouettes and random mode must always default to Auto
                if (IsBundledSilhouette(_activeFilePath) || _isRandomMode)
                {
                    _displayMode = "Auto";
                }
            }
        }

        public static string ImagesDirectory
        {
            get { return SilhouettesDirectory; }
        }

        public static string SilhouettesDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(_silhouettesDir))
                {
                    ResolveDirectory();
                }
                return _silhouettesDir;
            }
        }

        public static string CustomDirectory
        {
            get
            {
                string dir = Path.Combine(SilhouettesDirectory, "custom");
                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); } catch { }
                }
                return dir;
            }
        }

        public static bool IsRandomMode
        {
            get { return _isRandomMode; }
        }

        public static bool IsHidden
        {
            get { return _isHidden; }
        }

        public static string ColorStyle
        {
            get { return _colorStyle; }
        }

        public static int ZoomPercent
        {
            get { return _zoomPercent; }
        }

        public static string Alignment
        {
            get { return _alignment; }
        }

        public static bool IsMirrored
        {
            get { return _isMirrored; }
        }

        public static bool IsFlipped
        {
            get { return _isFlipped; }
        }

        public static string DisplayMode
        {
            get { return _displayMode; }
        }

        public static bool IsActiveWallpaper
        {
            get
            {
                // Bundled silhouettes are always character cutouts, never wallpapers
                if (IsBundledSilhouette(_activeFilePath))
                    return false;

                if (string.Equals(_displayMode, "Silhouette", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (_cachedActiveBitmap == null && !string.IsNullOrEmpty(_activeFilePath))
                {
                    GetActiveSilhouetteBitmap();
                }

                // Portrait images are silhouettes/cutouts, never stretched as widescreen wallpapers
                if (_cachedActiveBitmap != null && ((float)_cachedActiveBitmap.Width / _cachedActiveBitmap.Height) < 1.15f)
                    return false;

                if (string.Equals(_displayMode, "Wallpaper", StringComparison.OrdinalIgnoreCase))
                    return true;

                return _isActiveWallpaper;
            }
        }

        public static string ActiveFilePath
        {
            get { return _activeFilePath; }
        }

        public static string ActiveDisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(_activeFilePath)) return "None";
                try
                {
                    return Path.GetFileNameWithoutExtension(_activeFilePath);
                }
                catch
                {
                    return "None";
                }
            }
        }

        public static IList<string> GetAvailableFiles()
        {
            lock (_syncLock)
            {
                return _availableFiles.AsReadOnly();
            }
        }

        public static bool IsCustomFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            try
            {
                string customDir = CustomDirectory;
                return filePath.StartsWith(customDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsBundledSilhouette(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return true;
            try
            {
                // Bundled silhouettes are any supported images located directly in the root SilhouettesDirectory (not in custom/)
                string sillsDir = SilhouettesDirectory;
                if (string.IsNullOrEmpty(sillsDir) || !Directory.Exists(sillsDir))
                    return false;

                string fullFilePath = Path.GetFullPath(filePath);
                string dir = Path.GetDirectoryName(fullFilePath);
                string fullSillsDir = Path.GetFullPath(sillsDir);

                if (string.Equals(dir, fullSillsDir, StringComparison.OrdinalIgnoreCase))
                {
                    string ext = Path.GetExtension(fullFilePath);
                    return SupportedImageExtensions.Contains(ext);
                }

                // If only filename was passed, check if file exists in SilhouettesDirectory
                string candidate = Path.Combine(fullSillsDir, Path.GetFileName(filePath));
                if (File.Exists(candidate))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureCustomDirectory()
        {
            try
            {
                string customDir = Path.Combine(SilhouettesDirectory, "custom");
                if (!Directory.Exists(customDir))
                {
                    Directory.CreateDirectory(customDir);
                }
            }
            catch { }
        }

        private static void ResolveDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string cand1 = Path.Combine(baseDir, "assets", "images");
            string candCore = Path.Combine(baseDir, "core", "assets", "images");
            string candSys = Path.Combine(baseDir, "system", "assets", "images");
            string cand2 = Path.Combine(baseDir, "..", "..", "assets", "images");
            string cand3 = Path.Combine(Directory.GetCurrentDirectory(), "assets", "images");

            // Legacy folder fallbacks
            string legacy1 = Path.Combine(baseDir, "assets", "silhouettes");
            string legacyCore = Path.Combine(baseDir, "core", "assets", "silhouettes");
            string legacySys = Path.Combine(baseDir, "system", "assets", "silhouettes");
            string legacy2 = Path.Combine(baseDir, "..", "..", "assets", "silhouettes");
            string legacy3 = Path.Combine(Directory.GetCurrentDirectory(), "assets", "silhouettes");

            string[] candidates = new string[] { cand3, cand2, candCore, candSys, cand1, legacy3, legacy2, legacyCore, legacySys, legacy1 };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    if (Directory.Exists(candidates[i]))
                    {
                        string[] testFiles = Directory.GetFiles(candidates[i], "*.png");
                        if (testFiles.Length > 0)
                        {
                            _silhouettesDir = Path.GetFullPath(candidates[i]);
                            return;
                        }
                    }
                }
                catch { }
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (Directory.Exists(candidates[i]))
                {
                    _silhouettesDir = Path.GetFullPath(candidates[i]);
                    return;
                }
            }

            try
            {
                Directory.CreateDirectory(cand1);
                _silhouettesDir = Path.GetFullPath(cand1);
            }
            catch
            {
                _silhouettesDir = cand1;
            }
        }

        public static void Rescan()
        {
            lock (_syncLock)
            {
                RescanInternal();
                if (_isRandomMode || string.IsNullOrEmpty(_activeFilePath) || !File.Exists(_activeFilePath))
                {
                    PickRandomSilhouetteInternal();
                }
                NotifySilhouetteChanged();
            }
        }

        private static readonly HashSet<string> SupportedImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".webp", ".jpg", ".jpeg", ".bmp"
        };

        private static void RescanInternal()
        {
            _availableFiles.Clear();
            string dir = SilhouettesDirectory;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            try
            {
                // 1. Root / Bundled files (e.g. 2B Hero, 2B Maid, Chun-Li Pose)
                List<string> rootFiles = new List<string>();
                try
                {
                    string[] allFiles = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
                    for (int j = 0; j < allFiles.Length; j++)
                    {
                        if (SupportedImageExtensions.Contains(Path.GetExtension(allFiles[j])))
                        {
                            rootFiles.Add(allFiles[j]);
                        }
                    }
                }
                catch { }
                rootFiles.Sort(StringComparer.OrdinalIgnoreCase);

                // 2. Custom files inside custom/ subfolder
                List<string> customFiles = new List<string>();
                string customDir = Path.Combine(dir, "custom");
                if (Directory.Exists(customDir))
                {
                    try
                    {
                        string[] allCustom = Directory.GetFiles(customDir, "*.*", SearchOption.TopDirectoryOnly);
                        for (int j = 0; j < allCustom.Length; j++)
                        {
                            if (SupportedImageExtensions.Contains(Path.GetExtension(allCustom[j])))
                            {
                                customFiles.Add(allCustom[j]);
                            }
                        }
                    }
                    catch { }
                    customFiles.Sort(StringComparer.OrdinalIgnoreCase);
                }

                _availableFiles.AddRange(rootFiles);
                _availableFiles.AddRange(customFiles);
            }
            catch { }
        }

        public static void PickRandomSilhouette()
        {
            lock (_syncLock)
            {
                PickRandomSilhouetteInternal();
                NotifySilhouetteChanged();
            }
        }

        private static void PickRandomSilhouetteInternal()
        {
            _displayMode = "Auto";
            if (_availableFiles.Count == 0)
            {
                _activeFilePath = null;
                return;
            }

            if (_availableFiles.Count == 1)
            {
                _activeFilePath = _availableFiles[0];
                return;
            }

            Random rand = new Random(Guid.NewGuid().GetHashCode());
            string pick = _activeFilePath;
            int attempts = 0;
            while ((pick == _activeFilePath || string.IsNullOrEmpty(pick)) && attempts < 10)
            {
                int index = rand.Next(_availableFiles.Count);
                pick = _availableFiles[index];
                attempts++;
            }
            _activeFilePath = pick;
        }

        public static void SetRandomMode(bool isRandom)
        {
            lock (_syncLock)
            {
                _isRandomMode = isRandom;
                if (_isRandomMode)
                {
                    PickRandomSilhouetteInternal();
                }
                SaveConfig();
                NotifySilhouetteChanged();
            }
        }

        public static void SetHidden(bool hidden)
        {
            lock (_syncLock)
            {
                _isHidden = hidden;
                if (!_isHidden && (string.IsNullOrEmpty(_activeFilePath) || !File.Exists(_activeFilePath)))
                {
                    PickRandomSilhouetteInternal();
                }
                SaveConfig();
                NotifySilhouetteChanged();
            }
        }

        public static void ToggleHidden()
        {
            SetHidden(!_isHidden);
        }

        public static void SelectSilhouette(string filePath)
        {
            lock (_syncLock)
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
                _isRandomMode = false;
                _isHidden = false;
                _activeFilePath = filePath;
                _displayMode = "Auto"; // Reset to auto-detection for freshly chosen image
                SaveConfig();
                NotifySilhouetteChanged();
            }
        }

        public static void ToggleColorStyle()
        {
            lock (_syncLock)
            {
                _colorStyle = string.Equals(_colorStyle, "Accent", StringComparison.OrdinalIgnoreCase) ? "White" : "Accent";
                SaveConfig();
                NotifySilhouetteChanged();
            }
        }

        public static void ToggleDisplayMode()
        {
            lock (_syncLock)
            {
                if (IsBundledSilhouette(_activeFilePath))
                {
                    _displayMode = "Auto";
                    SaveConfig();
                    NotifySilhouetteChanged();
                    return;
                }

                if (IsActiveWallpaper)
                {
                    _displayMode = "Silhouette";
                }
                else
                {
                    _displayMode = "Wallpaper";
                }
                SaveConfig();
                NotifySilhouetteChanged();
            }
        }

        public static void SetDisplayMode(string mode)
        {
            lock (_syncLock)
            {
                _displayMode = mode ?? "Auto";
                SaveConfig();
                NotifySilhouetteChanged();
            }
        }

        public static void SetZoomPercent(int zoom)
        {
            lock (_syncLock)
            {
                _zoomPercent = Math.Max(30, Math.Min(150, zoom));
                SaveConfig();
                NotifySilhouetteChanged();
            }
        }

        public static void AdjustZoom(int delta)
        {
            lock (_syncLock)
            {
                int next = _zoomPercent + delta;
                SetZoomPercent(next);
            }
        }

        public static void SetAlignment(string align)
        {
            lock (_syncLock)
            {
                if (string.Equals(align, "Left", StringComparison.OrdinalIgnoreCase))
                    _alignment = "Left";
                else if (string.Equals(align, "Center", StringComparison.OrdinalIgnoreCase))
                    _alignment = "Center";
                else
                    _alignment = "Right";

                SaveConfig();
                NotifySilhouetteChanged();
            }
        }

        public static void ToggleMirror()
        {
            lock (_syncLock)
            {
                _isMirrored = !_isMirrored;
                SaveConfig();
                NotifySilhouetteChanged();
            }
        }

        public static void ToggleFlip()
        {
            lock (_syncLock)
            {
                _isFlipped = !_isFlipped;
                SaveConfig();
                NotifySilhouetteChanged();
            }
        }

        public static Bitmap GetActiveSilhouetteBitmap()
        {
            lock (_syncLock)
            {
                if (_isHidden)
                {
                    return null;
                }

                if (string.IsNullOrEmpty(_activeFilePath) || !File.Exists(_activeFilePath))
                {
                    if (_cachedActiveBitmap != null)
                    {
                        _cachedActiveBitmap.Dispose();
                        _cachedActiveBitmap = null;
                    }
                    _isActiveWallpaper = false;
                    return null;
                }

                if (_cachedActiveBitmap != null)
                {
                    return _cachedActiveBitmap;
                }

                try
                {
                    _cachedActiveBitmap = LoadBitmapFromFile(_activeFilePath);
                    if (_cachedActiveBitmap != null)
                    {
                        _isActiveWallpaper = IsWallpaperImage(_cachedActiveBitmap, _activeFilePath);

                        if (_isMirrored && _isFlipped)
                        {
                            _cachedActiveBitmap.RotateFlip(RotateFlipType.RotateNoneFlipXY);
                        }
                        else if (_isMirrored)
                        {
                            _cachedActiveBitmap.RotateFlip(RotateFlipType.RotateNoneFlipX);
                        }
                        else if (_isFlipped)
                        {
                            _cachedActiveBitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
                        }
                    }
                }
                catch
                {
                    _cachedActiveBitmap = null;
                    _isActiveWallpaper = false;
                }

                return _cachedActiveBitmap;
            }
        }

        public static string ImportCustomImage(string sourceFilePath)
        {
            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath)) return null;

            try
            {
                string targetDir = CustomDirectory;
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
                baseName = baseName.Replace("_silhouette", "").Replace("silhouette", "").Trim();
                if (string.IsNullOrEmpty(baseName)) baseName = "Custom Image";

                string targetFileName = baseName + ".webp";
                string targetFilePath = Path.Combine(targetDir, targetFileName);

                int counter = 1;
                while (File.Exists(targetFilePath))
                {
                    targetFileName = string.Format("{0} ({1}).webp", baseName, counter);
                    targetFilePath = Path.Combine(targetDir, targetFileName);
                    counter++;
                }

                using (Bitmap raw = LoadBitmapFromFile(sourceFilePath))
                {
                    if (raw == null) return null;

                    bool isWallpaper = IsWallpaperImage(raw, sourceFilePath);

                    if (isWallpaper)
                    {
                        // Wallpaper: preserve full scene / illustration, downscale to max 2560x1440 if huge
                        int maxW = 2560;
                        int maxH = 1440;
                        int origW = raw.Width;
                        int origH = raw.Height;
                        int targetW = origW;
                        int targetH = origH;

                        if (origW > maxW || origH > maxH)
                        {
                            float scale = Math.Min((float)maxW / origW, (float)maxH / origH);
                            targetW = Math.Max(1, (int)(origW * scale));
                            targetH = Math.Max(1, (int)(origH * scale));
                        }

                        using (Bitmap processed = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics g = Graphics.FromImage(processed))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.SmoothingMode = SmoothingMode.AntiAlias;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.DrawImage(raw, new Rectangle(0, 0, targetW, targetH));
                            }

                            bool converted = ConvertToWebP(processed, targetFilePath, true);
                            if (!converted)
                            {
                                string fallbackPath = Path.ChangeExtension(targetFilePath, ".png");
                                processed.Save(fallbackPath, ImageFormat.Png);
                                targetFilePath = fallbackPath;
                            }
                        }
                    }
                    else
                    {
                        // Silhouette: cutout character, downscale max 1200, smart transparency, crop margins
                        int maxDim = 1200;
                        int origW = raw.Width;
                        int origH = raw.Height;
                        int targetW = origW;
                        int targetH = origH;

                        if (origW > maxDim || origH > maxDim)
                        {
                            if (origW >= origH)
                            {
                                targetW = maxDim;
                                targetH = Math.Max(1, (int)(origH * ((float)maxDim / origW)));
                            }
                            else
                            {
                                targetH = maxDim;
                                targetW = Math.Max(1, (int)(origW * ((float)maxDim / origH)));
                            }
                        }

                        using (Bitmap processed = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics g = Graphics.FromImage(processed))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.SmoothingMode = SmoothingMode.AntiAlias;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.DrawImage(raw, new Rectangle(0, 0, targetW, targetH));
                            }

                            ApplySmartTransparency(processed);
                            using (Bitmap cropped = CropTransparentMargins(processed))
                            {
                                Bitmap finalBmp = cropped ?? processed;
                                bool converted = ConvertToWebP(finalBmp, targetFilePath, false);
                                if (!converted)
                                {
                                    string fallbackPath = Path.ChangeExtension(targetFilePath, ".png");
                                    finalBmp.Save(fallbackPath, ImageFormat.Png);
                                    targetFilePath = fallbackPath;
                                }
                            }
                        }
                    }
                }

                Rescan();
                _displayMode = "Auto";
                SelectSilhouette(targetFilePath);
                return targetFilePath;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsWallpaperImage(Bitmap bmp, string filePath = null)
        {
            if (bmp == null) return false;
            if (!string.IsNullOrEmpty(filePath) && IsBundledSilhouette(filePath)) return false;

            try
            {
                // 1. Aspect ratio guard: desktop wallpapers are widescreen/landscape (16:9 = 1.78, 16:10 = 1.6, 4:3 = 1.33).
                // Silhouettes and character cutouts are portrait or square (ratio < 1.15).
                // Stretching a portrait image to cover a widescreen display crops heads and feet.
                float aspectRatio = (float)bmp.Width / bmp.Height;
                if (aspectRatio < 1.15f)
                {
                    return false;
                }

                // 2. Minimum resolution guard: desktop wallpapers must be sufficiently sized.
                if (bmp.Width < 600 || bmp.Height < 350)
                {
                    return false;
                }

                Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
                byte[] buffer = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, buffer, 0, bytes);
                int stride = bmpData.Stride;
                bmp.UnlockBits(bmpData);

                int w = bmp.Width;
                int h = bmp.Height;

                // 3. Corner transparency guard: cutouts have transparent corners; wallpapers are full-bleed opaque
                if (buffer[3] < 200 ||
                    buffer[(w - 1) * 4 + 3] < 200 ||
                    buffer[(h - 1) * stride + 3] < 200 ||
                    buffer[(h - 1) * stride + (w - 1) * 4 + 3] < 200)
                {
                    return false;
                }

                // 4. Transparency check across a distributed grid
                int transparentCount = 0;
                int totalSampled = 0;

                int stepY = Math.Max(1, h / 100);
                int stepX = Math.Max(1, w / 100);

                for (int y = 0; y < h; y += stepY)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < w; x += stepX)
                    {
                        totalSampled++;
                        byte a = buffer[rowOffset + x * 4 + 3];
                        if (a < 220)
                        {
                            transparentCount++;
                        }
                    }
                }

                float transparentRatio = totalSampled > 0 ? ((float)transparentCount / totalSampled) : 0f;
                // Cutout silhouettes have transparent pixels; wallpapers are fully opaque
                if (transparentRatio > 0.04f)
                {
                    return false;
                }

                // 5. Studio background check: flat white or black border
                double c0 = GetPixelLuminance(buffer, 2, 2, stride);
                double c1 = GetPixelLuminance(buffer, w - 3, 2, stride);
                double c2 = GetPixelLuminance(buffer, 2, h - 3, stride);
                double c3 = GetPixelLuminance(buffer, w - 3, h - 3, stride);

                bool whiteCorners = (c0 > 235 && c1 > 235 && c2 > 235 && c3 > 235);
                bool blackCorners = (c0 < 20 && c1 < 20 && c2 < 20 && c3 < 20);

                if (whiteCorners || blackCorners)
                {
                    int borderFlatMatches = 0;
                    int borderSamples = 0;
                    int bStep = Math.Max(1, w / 30);
                    for (int x = 0; x < w; x += bStep)
                    {
                        borderSamples += 2;
                        double topLum = GetPixelLuminance(buffer, x, 1, stride);
                        double botLum = GetPixelLuminance(buffer, x, h - 2, stride);
                        if (whiteCorners)
                        {
                            if (topLum > 230) borderFlatMatches++;
                            if (botLum > 230) borderFlatMatches++;
                        }
                        else
                        {
                            if (topLum < 25) borderFlatMatches++;
                            if (botLum < 25) borderFlatMatches++;
                        }
                    }

                    if (borderSamples > 0 && ((float)borderFlatMatches / borderSamples) > 0.70f)
                    {
                        return false; // Silhouette on flat studio background
                    }
                }

                // High resolution, widescreen, full-bleed opaque scene -> Wallpaper
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static Bitmap LoadBitmapFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

            string ext = Path.GetExtension(filePath);
            if (string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase))
            {
                Bitmap wicBmp = LoadWebPBitmap(filePath);
                if (wicBmp != null) return wicBmp;

                return LoadWebPViaFfmpeg(filePath);
            }

            using (Bitmap temp = new Bitmap(filePath))
            {
                return new Bitmap(temp);
            }
        }

        private static Bitmap LoadWebPBitmap(string filePath)
        {
            try
            {
                Uri uri = new Uri(Path.GetFullPath(filePath));
                System.Windows.Media.Imaging.BitmapDecoder decoder =
                    System.Windows.Media.Imaging.BitmapDecoder.Create(
                        uri,
                        System.Windows.Media.Imaging.BitmapCreateOptions.None,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

                if (decoder == null || decoder.Frames.Count == 0) return null;
                System.Windows.Media.Imaging.BitmapFrame frame = decoder.Frames[0];

                int width = frame.PixelWidth;
                int height = frame.PixelHeight;

                System.Windows.Media.Imaging.FormatConvertedBitmap converted =
                    new System.Windows.Media.Imaging.FormatConvertedBitmap(
                        frame,
                        System.Windows.Media.PixelFormats.Bgra32,
                        null,
                        0);

                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                Rectangle rect = new Rectangle(0, 0, width, height);
                BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
                bmp.UnlockBits(bmpData);

                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap LoadWebPViaFfmpeg(string filePath)
        {
            string tempBmp = Path.Combine(Path.GetTempPath(), "wb_dec_" + Guid.NewGuid().ToString("N") + ".bmp");
            try
            {
                string ffmpeg = FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpeg)) return null;

                string args = string.Format("-y -i \"{0}\" \"{1}\"", filePath, tempBmp);
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(ffmpeg, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (System.Diagnostics.Process proc = System.Diagnostics.Process.Start(psi))
                {
                    proc.WaitForExit(5000);
                    if (proc.ExitCode == 0 && File.Exists(tempBmp))
                    {
                        using (Bitmap temp = new Bitmap(tempBmp))
                        {
                            return new Bitmap(temp);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                try { if (File.Exists(tempBmp)) File.Delete(tempBmp); } catch { }
            }
            return null;
        }

        public static bool ConvertToWebP(Bitmap bmp, string targetWebpPath, bool isWallpaper)
        {
            if (bmp == null || string.IsNullOrEmpty(targetWebpPath)) return false;

            string tempPng = Path.Combine(Path.GetTempPath(), "wb_conv_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                bmp.Save(tempPng, ImageFormat.Png);

                string ffmpeg = FindFfmpeg();
                if (!string.IsNullOrEmpty(ffmpeg))
                {
                    string args = isWallpaper
                        ? string.Format("-y -i \"{0}\" -c:v libwebp -q:v 88 \"{1}\"", tempPng, targetWebpPath)
                        : string.Format("-y -i \"{0}\" -c:v libwebp -lossless 1 \"{1}\"", tempPng, targetWebpPath);

                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(ffmpeg, args)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (System.Diagnostics.Process proc = System.Diagnostics.Process.Start(psi))
                    {
                        proc.WaitForExit(15000);
                        if (proc.ExitCode == 0 && File.Exists(targetWebpPath))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
            }

            return false;
        }

        private static string FindFfmpeg()
        {
            string[] directPaths = new string[]
            {
                @"C:\Windows\ffmpeg\bin\ffmpeg.exe",
                @"C:\ffmpeg\bin\ffmpeg.exe"
            };
            for (int i = 0; i < directPaths.Length; i++)
            {
                if (File.Exists(directPaths[i])) return directPaths[i];
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                string[] parts = pathEnv.Split(';');
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (!string.IsNullOrEmpty(p))
                    {
                        string cand = Path.Combine(p, "ffmpeg.exe");
                        if (File.Exists(cand)) return cand;
                    }
                }
            }
            return "ffmpeg.exe";
        }

        private static void ApplySmartTransparency(Bitmap bmp)
        {
            try
            {
                Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
                byte[] buffer = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, buffer, 0, bytes);

                bool hasExistingAlpha = false;
                for (int i = 3; i < bytes; i += 4)
                {
                    if (buffer[i] < 240)
                    {
                        hasExistingAlpha = true;
                        break;
                    }
                }

                if (!hasExistingAlpha)
                {
                    double cornerLum = (GetPixelLuminance(buffer, 0, 0, bmpData.Stride) +
                                        GetPixelLuminance(buffer, bmp.Width - 1, 0, bmpData.Stride) +
                                        GetPixelLuminance(buffer, 0, bmp.Height - 1, bmpData.Stride) +
                                        GetPixelLuminance(buffer, bmp.Width - 1, bmp.Height - 1, bmpData.Stride)) / 4.0;

                    bool isWhiteBackground = cornerLum > 180.0;

                    for (int i = 0; i < bytes; i += 4)
                    {
                        byte b = buffer[i];
                        byte g = buffer[i + 1];
                        byte r = buffer[i + 2];
                        double lum = (r * 0.299 + g * 0.587 + b * 0.114);

                        byte alpha;
                        if (isWhiteBackground)
                        {
                            if (lum > 220.0) alpha = 0;
                            else if (lum < 35.0) alpha = 255;
                            else alpha = (byte)((220.0 - lum) * 255.0 / (220.0 - 35.0));
                        }
                        else
                        {
                            if (lum < 35.0) alpha = 0;
                            else if (lum > 220.0) alpha = 255;
                            else alpha = (byte)((lum - 35.0) * 255.0 / (220.0 - 35.0));
                        }

                        buffer[i] = 255;
                        buffer[i + 1] = 255;
                        buffer[i + 2] = 255;
                        buffer[i + 3] = alpha;
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, bmpData.Scan0, bytes);
                bmp.UnlockBits(bmpData);
            }
            catch { }
        }

        private static double GetPixelLuminance(byte[] buffer, int x, int y, int stride)
        {
            int index = y * stride + x * 4;
            if (index + 2 < buffer.Length)
            {
                byte b = buffer[index];
                byte g = buffer[index + 1];
                byte r = buffer[index + 2];
                return (r * 0.299 + g * 0.587 + b * 0.114);
            }
            return 0;
        }

        private static Bitmap CropTransparentMargins(Bitmap bmp)
        {
            try
            {
                Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(bmpData.Stride) * bmp.Height;
                byte[] buffer = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, buffer, 0, bytes);
                int stride = bmpData.Stride;
                bmp.UnlockBits(bmpData);

                int minX = bmp.Width;
                int maxX = 0;
                int minY = bmp.Height;
                int maxY = 0;

                for (int y = 0; y < bmp.Height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        byte alpha = buffer[rowOffset + x * 4 + 3];
                        if (alpha > 15)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }

                if (minX >= maxX || minY >= maxY)
                {
                    return null;
                }

                int pad = 4;
                int cropX = Math.Max(0, minX - pad);
                int cropY = Math.Max(0, minY - pad);
                int cropW = Math.Min(bmp.Width - cropX, (maxX - minX + 1) + (pad * 2));
                int cropH = Math.Min(bmp.Height - cropY, (maxY - minY + 1) + (pad * 2));

                Bitmap cropped = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(cropped))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(bmp, new Rectangle(0, 0, cropW, cropH), cropX, cropY, cropW, cropH, GraphicsUnit.Pixel);
                }
                return cropped;
            }
            catch
            {
                return null;
            }
        }

        private static string FindFileByExactOrBaseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            for (int i = 0; i < _availableFiles.Count; i++)
            {
                string f = _availableFiles[i];
                if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f, name, StringComparison.OrdinalIgnoreCase))
                {
                    return f;
                }
            }
            return null;
        }

        private static void SaveConfig()
        {
            try
            {
                SilhouetteConfigModel model = new SilhouetteConfigModel
                {
                    IsRandomMode = _isRandomMode,
                    SelectedFileName = !string.IsNullOrEmpty(_activeFilePath) ? Path.GetFileName(_activeFilePath) : string.Empty,
                    ColorStyle = _colorStyle,
                    ZoomPercent = _zoomPercent,
                    Alignment = _alignment,
                    IsMirrored = _isMirrored,
                    IsFlipped = _isFlipped,
                    DisplayMode = _displayMode,
                    IsHidden = _isHidden
                };
                StorageHelper.SaveObject<SilhouetteConfigModel>(ConfigFileName, model);
            }
            catch { }
        }

        private static void NotifySilhouetteChanged()
        {
            if (_cachedActiveBitmap != null)
            {
                _cachedActiveBitmap.Dispose();
                _cachedActiveBitmap = null;
            }
            _isActiveWallpaper = false;

            if (SilhouetteChanged != null)
            {
                SilhouetteChanged(null, EventArgs.Empty);
            }
        }
    }
}
