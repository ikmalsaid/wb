using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using wb.Core;

namespace wb.Browser
{
    public class FaviconManager
    {
        private readonly Dictionary<string, Image> _cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Action<Image>>> _pendingCallbacks = new Dictionary<string, List<Action<Image>>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _activeDirectUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activeWorkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directCachedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _darkCachedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _negativeCache = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly string _diskCacheFolder;
        private readonly object _lock = new object();

        private string DiskCacheFolder
        {
            get
            {
                try
                {
                    return StorageHelper.GetFaviconsDirectory();
                }
                catch
                {
                    return _diskCacheFolder;
                }
            }
        }

        public FaviconManager()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls | (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslErrors) => true;
            }
            catch
            {
            }

            try
            {
                _diskCacheFolder = StorageHelper.GetFaviconsDirectory();
            }
            catch
            {
            }
        }

        public void ClearCache()
        {
            lock (_lock)
            {
                _cache.Clear();
                _negativeCache.Clear();
                _directCachedDomains.Clear();
                _darkCachedDomains.Clear();
            }
            try
            {
                string folder = DiskCacheFolder;
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    string[] files = Directory.GetFiles(folder);
                    foreach (string file in files)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        public Image GetFavicon(string url)
        {
            if (string.IsNullOrEmpty(url) || InternalPageHelper.IsStartPage(url))
            {
                return null;
            }

            if (url.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring("view-source:".Length).Trim();
            }

            string domain = ExtractDomain(url);
            lock (_lock)
            {
                Image cached;
                if (!string.IsNullOrEmpty(domain) && _cache.TryGetValue(domain, out cached) && cached != null)
                {
                    return cached;
                }
                if (_cache.TryGetValue(url, out cached) && cached != null)
                {
                    return cached;
                }
            }

            // Check persistent disk cache
            if (!string.IsNullOrEmpty(domain))
            {
                Image diskImg = LoadDiskFavicon(domain);
                if (diskImg != null)
                {
                    lock (_lock)
                    {
                        _cache[domain] = diskImg;
                        _cache[url] = diskImg;
                    }
                    return diskImg;
                }
            }

            return null;
        }

        public Image GetFaviconInMemory(string url)
        {
            if (string.IsNullOrEmpty(url) || InternalPageHelper.IsStartPage(url))
            {
                return null;
            }

            if (url.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring("view-source:".Length).Trim();
            }

            string domain = ExtractDomain(url);
            lock (_lock)
            {
                Image cached;
                if (!string.IsNullOrEmpty(domain) && _cache.TryGetValue(domain, out cached) && cached != null)
                {
                    return cached;
                }
                if (_cache.TryGetValue(url, out cached) && cached != null)
                {
                    return cached;
                }
            }

            // Fall back to persistent disk cache so restarting wb does not lose favicons
            return GetFavicon(url);
        }

        public void FetchFaviconAsync(string pageUrl, string directFaviconUrl, Action<Image> onLoaded)
        {
            if (!string.IsNullOrEmpty(pageUrl) && pageUrl.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                pageUrl = pageUrl.Substring("view-source:".Length).Trim();
            }

            if (string.IsNullOrEmpty(pageUrl) || InternalPageHelper.IsStartPage(pageUrl))
            {
                if (!string.IsNullOrEmpty(directFaviconUrl) && directFaviconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    pageUrl = directFaviconUrl;
                }
                else
                {
                    return;
                }
            }

            string domain = ExtractDomain(pageUrl);
            if (string.IsNullOrEmpty(domain))
            {
                return;
            }

            bool hasDirect = !string.IsNullOrEmpty(directFaviconUrl);
            if (hasDirect && !IsValidFaviconSource(domain, directFaviconUrl))
            {
                directFaviconUrl = null;
                hasDirect = false;
            }

            bool isDarkDirect = hasDirect && (directFaviconUrl.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              directFaviconUrl.IndexOf("night", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              directFaviconUrl.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              directFaviconUrl.IndexOf("color-scheme=dark", StringComparison.OrdinalIgnoreCase) >= 0);

            // 1. Check existing cached icon
            Image existing = GetFavicon(pageUrl);
            if (existing != null)
            {
                bool needsDirectUpgrade = hasDirect && !_directCachedDomains.Contains(domain);
                bool needsDarkUpgrade = isDarkDirect && !_darkCachedDomains.Contains(domain);

                if (!needsDirectUpgrade && !needsDarkUpgrade)
                {
                    if (onLoaded != null)
                    {
                        try { onLoaded(existing); } catch { }
                    }
                    return;
                }

                // Deliver existing cached icon immediately while high-res/dark upgrade downloads
                if (onLoaded != null)
                {
                    try { onLoaded(existing); } catch { }
                }
            }

            // Check negative cache
            lock (_lock)
            {
                DateTime expires;
                if (!hasDirect && _negativeCache.TryGetValue(domain, out expires) && DateTime.UtcNow < expires)
                {
                    return;
                }
            }

            bool shouldStartWorker = false;
            lock (_lock)
            {
                if (hasDirect)
                {
                    _activeDirectUrls[domain] = directFaviconUrl;
                }

                List<Action<Image>> callbacks;
                if (!_pendingCallbacks.TryGetValue(domain, out callbacks))
                {
                    callbacks = new List<Action<Image>>();
                    _pendingCallbacks[domain] = callbacks;
                }

                if (onLoaded != null)
                {
                    callbacks.Add(onLoaded);
                }

                if (!_activeWorkers.Contains(domain))
                {
                    _activeWorkers.Add(domain);
                    shouldStartWorker = true;
                }
            }

            if (!shouldStartWorker)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                Image img = null;
                string currentDirect = null;
                lock (_lock)
                {
                    _activeDirectUrls.TryGetValue(domain, out currentDirect);
                }

                try
                {
                    // 1. Try direct favicon URL if provided from HTML <link rel="icon">
                    if (!string.IsNullOrEmpty(currentDirect))
                    {
                        string resolvedUrl = ResolveFaviconUrl(pageUrl, currentDirect);
                        if (!string.IsNullOrEmpty(resolvedUrl))
                        {
                            if (resolvedUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                            {
                                img = DecodeDataUriImage(resolvedUrl);
                            }
                            else
                            {
                                img = DownloadFaviconImage(resolvedUrl);
                            }
                        }
                    }

                    // 2. High-speed Google S2 Favicon service (~50-100ms)
                    if (img == null)
                    {
                        string googleS2Url = string.Format("https://www.google.com/s2/favicons?domain={0}&sz=32", Uri.EscapeDataString(domain));
                        img = DownloadFaviconImage(googleS2Url);
                    }

                    // 3. Fallback to direct domain root /favicon.ico (e.g. https://domain.com/favicon.ico)
                    if (img == null)
                    {
                        try
                        {
                            string safePageUrl = pageUrl;
                            if (!safePageUrl.Contains("://"))
                            {
                                safePageUrl = "https://" + safePageUrl;
                            }
                            Uri uri = new Uri(safePageUrl);
                            string rootFavicon = uri.Scheme + "://" + uri.Authority + "/favicon.ico";
                            img = DownloadFaviconImage(rootFavicon);
                        }
                        catch
                        {
                        }
                    }

                    // 4. Fallback to DuckDuckGo icon service
                    if (img == null)
                    {
                        string ddgUrl = string.Format("https://icons.duckduckgo.com/ip3/{0}.ico", Uri.EscapeDataString(domain));
                        img = DownloadFaviconImage(ddgUrl);
                    }
                }
                catch
                {
                }

                List<Action<Image>> callbacksToInvoke = null;
                lock (_lock)
                {
                    _activeWorkers.Remove(domain);
                    _activeDirectUrls.Remove(domain);

                    if (img != null)
                    {
                        _cache[domain] = img;
                        _cache[pageUrl] = img;
                        if (!string.IsNullOrEmpty(currentDirect))
                        {
                            _directCachedDomains.Add(domain);
                            if (isDarkDirect)
                            {
                                _darkCachedDomains.Add(domain);
                            }
                        }
                        _negativeCache.Remove(domain);
                    }
                    else
                    {
                        _negativeCache[domain] = DateTime.UtcNow.AddSeconds(45);
                    }

                    List<Action<Image>> pending;
                    if (_pendingCallbacks.TryGetValue(domain, out pending) && pending != null)
                    {
                        callbacksToInvoke = new List<Action<Image>>(pending);
                        _pendingCallbacks.Remove(domain);
                    }
                }

                // Save to persistent disk cache
                if (img != null)
                {
                    SaveDiskFavicon(domain, img);
                }

                if (callbacksToInvoke != null)
                {
                    foreach (Action<Image> cb in callbacksToInvoke)
                    {
                        if (cb != null)
                        {
                            try
                            {
                                cb(img);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            });
        }

        private static string ResolveFaviconUrl(string pageUrl, string faviconUrl)
        {
            if (string.IsNullOrEmpty(faviconUrl)) return null;
            faviconUrl = faviconUrl.Trim();

            if (faviconUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return faviconUrl;
            }

            if (faviconUrl.StartsWith("//"))
            {
                return "https:" + faviconUrl;
            }

            if (faviconUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                faviconUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return faviconUrl;
            }

            try
            {
                string safePageUrl = pageUrl;
                if (!safePageUrl.Contains("://"))
                {
                    safePageUrl = "https://" + safePageUrl;
                }
                Uri baseUri = new Uri(safePageUrl);
                return new Uri(baseUri, faviconUrl).AbsoluteUri;
            }
            catch
            {
                return null;
            }
        }

        private static Image DecodeDataUriImage(string dataUri)
        {
            try
            {
                int commaIdx = dataUri.IndexOf(',');
                if (commaIdx >= 0)
                {
                    string base64 = dataUri.Substring(commaIdx + 1);
                    byte[] bytes = Convert.FromBase64String(base64);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    using (Image raw = Image.FromStream(ms))
                    {
                        return ResizeToFavicon(raw);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private Image DownloadFaviconImage(string url)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 3000;
                req.ReadWriteTimeout = 3000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";
                req.AllowAutoRedirect = true;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        using (Stream stream = resp.GetResponseStream())
                        using (MemoryStream ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            if (ms.Length == 0) return null;
                            ms.Position = 0;

                            // 1. Try Image.FromStream (PNG, JPEG, GIF, BMP and PNG-compressed ICO)
                            try
                            {
                                using (Image raw = Image.FromStream(ms))
                                {
                                    return ResizeToFavicon(raw);
                                }
                            }
                            catch
                            {
                            }

                            // 2. Try as classic Windows Icon (.ico)
                            ms.Position = 0;
                            try
                            {
                                using (Icon icon = new Icon(ms))
                                {
                                    using (Bitmap bmp = icon.ToBitmap())
                                    {
                                        return ResizeToFavicon(bmp);
                                    }
                                }
                            }
                            catch
                            {
                            }

                            // 3. Scan for embedded PNG header in ICO stream
                            byte[] data = ms.ToArray();
                            int pngIdx = FindPngHeader(data);
                            if (pngIdx >= 0)
                            {
                                using (MemoryStream pngMs = new MemoryStream(data, pngIdx, data.Length - pngIdx))
                                using (Image raw = Image.FromStream(pngMs))
                                {
                                    return ResizeToFavicon(raw);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static int FindPngHeader(byte[] data)
        {
            if (data == null || data.Length < 8) return -1;
            for (int i = 0; i <= data.Length - 8; i++)
            {
                if (data[i] == 0x89 && data[i + 1] == 0x50 && data[i + 2] == 0x4E && data[i + 3] == 0x47 &&
                    data[i + 4] == 0x0D && data[i + 5] == 0x0A && data[i + 6] == 0x1A && data[i + 7] == 0x0A)
                {
                    return i;
                }
            }
            return -1;
        }

        private static Bitmap ResizeToFavicon(Image raw)
        {
            if (raw == null) return null;
            int size = 32;
            Bitmap copy = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(copy))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(raw, new Rectangle(0, 0, size, size));
            }
            EnsureDarkModeContrast(copy);
            return copy;
        }

        /// <summary>
        /// Automatically detects dark/black silhouette favicons with transparency and brightens/inverts them
        /// so they remain clearly distinguishable on dark tabs, dark bookmarks bar and menus.
        /// </summary>
        public static bool EnsureDarkModeContrast(Bitmap bmp)
        {
            if (bmp == null || bmp.Width == 0 || bmp.Height == 0) return false;

            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int totalPixels = bmp.Width * bmp.Height;
                int byteCount = data.Stride * bmp.Height;
                byte[] pixels = new byte[byteCount];
                Marshal.Copy(data.Scan0, pixels, 0, byteCount);

                int transparentCount = 0;
                int visibleCount = 0;
                int darkCount = 0;
                int brightCount = 0;
                long totalLum = 0;

                for (int i = 0; i < byteCount; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];

                    if (a < 40)
                    {
                        transparentCount++;
                    }
                    else
                    {
                        visibleCount++;
                        int lum = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                        totalLum += lum;
                        if (lum < 75) darkCount++;
                        if (lum > 140) brightCount++;
                    }
                }

                if (visibleCount == 0) return false;

                double transRatio = (double)transparentCount / totalPixels;
                double darkRatio = (double)darkCount / visibleCount;
                double brightRatio = (double)brightCount / visibleCount;
                double avgLum = (double)totalLum / visibleCount;

                // Detect dark silhouette glyph / logo on transparent background
                if (transRatio >= 0.15 && darkRatio >= 0.70 && brightRatio <= 0.08 && avgLum < 70)
                {
                    for (int i = 0; i < byteCount; i += 4)
                    {
                        byte a = pixels[i + 3];
                        if (a >= 40)
                        {
                            byte b = pixels[i];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            int lum = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                            if (lum < 110)
                            {
                                pixels[i] = (byte)(255 - b);
                                pixels[i + 1] = (byte)(255 - g);
                                pixels[i + 2] = (byte)(255 - r);
                            }
                        }
                    }
                    Marshal.Copy(pixels, 0, data.Scan0, byteCount);
                    return true;
                }
                return false;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private string GetDiskCachePath(string domain)
        {
            string folder = DiskCacheFolder;
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(domain)) return null;
            string safe = SanitizeFileName(domain) + ".png";
            return Path.Combine(folder, safe);
        }

        private Image LoadDiskFavicon(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return null;
            try
            {
                string folder = DiskCacheFolder;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

                // 1. Try exact domain (e.g. youtube.com -> youtube.com.png)
                string path = GetDiskCachePath(domain);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return LoadImageFromFile(path);
                }

                // 2. Try with or without www.
                if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                {
                    string noWww = domain.Substring(4);
                    path = GetDiskCachePath(noWww);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        return LoadImageFromFile(path);
                    }
                }
                else
                {
                    string withWww = "www." + domain;
                    path = GetDiskCachePath(withWww);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        return LoadImageFromFile(path);
                    }
                }

                // 3. Fallback to parent domains if subdomain (e.g. colab.research.google.com -> google.com)
                string cur = domain;
                while (true)
                {
                    int dotIdx = cur.IndexOf('.');
                    if (dotIdx <= 0) break;
                    cur = cur.Substring(dotIdx + 1);
                    if (!cur.Contains(".")) break; // Stop at top-level domain (e.g. .com)
                    path = GetDiskCachePath(cur);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        return LoadImageFromFile(path);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static Image LoadImageFromFile(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes != null && bytes.Length > 0)
                {
                    using (MemoryStream ms = new MemoryStream(bytes))
                    using (Image raw = Image.FromStream(ms))
                    {
                        return ResizeToFavicon(raw);
                    }
                }
            }
            catch { }
            return null;
        }

        private void SaveDiskFavicon(string domain, Image img)
        {
            try
            {
                string path = GetDiskCachePath(domain);
                if (!string.IsNullOrEmpty(path) && img != null)
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    using (Bitmap bmp = new Bitmap(img))
                    {
                        bmp.Save(path, ImageFormat.Png);
                    }

                    // If domain had www., also save base domain without www. so both match instantly
                    if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    {
                        string noWwwPath = GetDiskCachePath(domain.Substring(4));
                        if (!string.IsNullOrEmpty(noWwwPath))
                        {
                            using (Bitmap bmp = new Bitmap(img))
                            {
                                bmp.Save(noWwwPath, ImageFormat.Png);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "fav";
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }
            return new string(chars);
        }

        public static string ExtractDomain(string url)
        {
            return UrlHelper.ExtractDomain(url);
        }

        private static bool IsValidFaviconSource(string pageDomain, string directFaviconUrl)
        {
            if (string.IsNullOrEmpty(directFaviconUrl)) return false;
            directFaviconUrl = directFaviconUrl.Trim();
            if (directFaviconUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return true;
            if (directFaviconUrl.StartsWith("/") && !directFaviconUrl.StartsWith("//")) return true;

            string directDomain = ExtractDomain(directFaviconUrl);
            if (string.IsNullOrEmpty(directDomain)) return true;

            if (string.Equals(pageDomain, directDomain, StringComparison.OrdinalIgnoreCase)) return true;
            if (directDomain.EndsWith("." + pageDomain, StringComparison.OrdinalIgnoreCase)) return true;
            if (pageDomain.EndsWith("." + directDomain, StringComparison.OrdinalIgnoreCase)) return true;

            string rootPage = GetRootDomainName(pageDomain);
            string rootDirect = GetRootDomainName(directDomain);
            if (!string.IsNullOrEmpty(rootPage) && !string.IsNullOrEmpty(rootDirect))
            {
                if (string.Equals(rootPage, rootDirect, StringComparison.OrdinalIgnoreCase)) return true;
                if (rootDirect.StartsWith(rootPage, StringComparison.OrdinalIgnoreCase)) return true;
            }

            if (IsKnownCdn(directDomain)) return true;

            return false;
        }

        private static string GetRootDomainName(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return string.Empty;
            string[] parts = domain.Split('.');
            if (parts.Length >= 2)
            {
                return parts[parts.Length - 2];
            }
            return domain;
        }

        private static bool IsKnownCdn(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return false;
            return domain.EndsWith("gstatic.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("googleusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("githubassets.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("cloudflare.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("cloudfront.net", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("jsdelivr.net", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("unpkg.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("wp.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("akamaihd.net", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith("fastly.net", StringComparison.OrdinalIgnoreCase);
        }
    }
}
