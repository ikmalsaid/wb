using System;
using System.Drawing;
using wb.Core;
using wb.UI;

namespace wb.Browser
{
    public enum InternalPageType
    {
        None,
        Bookmarks,
        History,
        Downloads,
        NewTab,
        Incognito,
        Screenshot,
        ClearBrowsingData,
        Proxy,
        ImportData,
        ExportData
    }

    public static class InternalPageHelper
    {
        public const string BookmarksUrl = "wb://bookmarks";
        public const string HistoryUrl = "wb://history";
        public const string DownloadsUrl = "wb://downloads";
        public const string NewTabUrl = "wb://newtab";
        public const string IncognitoUrl = "wb://incognito";
        public const string ScreenshotUrl = "wb://screenshot";
        public const string ClearBrowsingDataUrl = "wb://clear-browsing-data";
        public const string SettingsClearBrowsingDataUrl = "wb://settings/clearBrowserData";
        public const string ProxyUrl = "wb://proxy";
        public const string SettingsProxyUrl = "wb://settings/proxy";
        public const string ImportDataUrl = "wb://import";
        public const string ExportDataUrl = "wb://export";

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static string PreNormalize(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            string s = url.Trim();
            while (s.EndsWith("/")) s = s.Substring(0, s.Length - 1).Trim();

            // Transform legacy/whitelisted prefixes into canonical wb:// scheme
            if (s.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase))
            {
                s = "wb://" + s.Substring("chrome://".Length);
            }
            else if (s.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase))
            {
                s = "wb://" + s.Substring("chrome:".Length).TrimStart('/');
            }
            else if (s.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                s = "wb://" + s.Substring("about:".Length).TrimStart('/');
            }
            else if (s.StartsWith("wb:", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("wb://", StringComparison.OrdinalIgnoreCase))
            {
                s = "wb://" + s.Substring("wb:".Length).TrimStart('/');
            }
            return s;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool IsInternalUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (url.IndexOf("startpage.html", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string norm = PreNormalize(url);
            return string.Equals(norm, BookmarksUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://favorites", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, HistoryUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, DownloadsUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, NewTabUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://new-tab", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://start", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, IncognitoUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://private", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, ClearBrowsingDataUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, SettingsClearBrowsingDataUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://clearbrowserdata", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://clear-data", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://settings/clear-browsing-data", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, ProxyUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, SettingsProxyUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://settings/network", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, ImportDataUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://import-data", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://importer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, ExportDataUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://export-data", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(norm, "wb://exporter", StringComparison.OrdinalIgnoreCase) ||
                   norm.StartsWith(ScreenshotUrl, StringComparison.OrdinalIgnoreCase);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool IsInternalPage(string url)
        {
            if (string.IsNullOrEmpty(url)) return true;
            string trimmed = url.Trim();
            if (IsStartPage(trimmed) || IsInternalUrl(trimmed)) return true;
            if (trimmed.StartsWith("wb:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("wb://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool CanBookmarkPage(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string trimmed = url.Trim();
            if (IsStartPage(trimmed) || IsInternalUrl(trimmed) || IsViewSourceUrl(trimmed)) return false;
            if (string.Equals(trimmed, "about:blank", StringComparison.OrdinalIgnoreCase)) return false;
            if (UrlHelper.IsSpecialScheme(trimmed)) return false;
            return true;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static string NormalizeInternalUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (url.IndexOf("startpage.html", StringComparison.OrdinalIgnoreCase) >= 0) return NewTabUrl;
            string norm = PreNormalize(url);
            if (string.Equals(norm, BookmarksUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://favorites", StringComparison.OrdinalIgnoreCase))
            {
                return BookmarksUrl;
            }
            if (string.Equals(norm, HistoryUrl, StringComparison.OrdinalIgnoreCase))
            {
                return HistoryUrl;
            }
            if (string.Equals(norm, DownloadsUrl, StringComparison.OrdinalIgnoreCase))
            {
                return DownloadsUrl;
            }
            if (string.Equals(norm, NewTabUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://new-tab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://start", StringComparison.OrdinalIgnoreCase))
            {
                return NewTabUrl;
            }
            if (string.Equals(norm, IncognitoUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://private", StringComparison.OrdinalIgnoreCase))
            {
                return IncognitoUrl;
            }
            if (string.Equals(norm, ClearBrowsingDataUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, SettingsClearBrowsingDataUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://clearbrowserdata", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://clear-data", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://settings/clear-browsing-data", StringComparison.OrdinalIgnoreCase))
            {
                return ClearBrowsingDataUrl;
            }
            if (string.Equals(norm, ProxyUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, SettingsProxyUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://settings/network", StringComparison.OrdinalIgnoreCase))
            {
                return SettingsProxyUrl;
            }
            if (string.Equals(norm, ImportDataUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://import-data", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://importer", StringComparison.OrdinalIgnoreCase))
            {
                return ImportDataUrl;
            }
            if (string.Equals(norm, ExportDataUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://export-data", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, "wb://exporter", StringComparison.OrdinalIgnoreCase))
            {
                return ExportDataUrl;
            }
            if (norm.StartsWith(ScreenshotUrl, StringComparison.OrdinalIgnoreCase))
            {
                return norm;
            }
            return norm;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool IsViewSourceUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.Trim().StartsWith("view-source:", StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeViewSourceUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            string trimmed = url.Trim();
            if (!trimmed.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            string inner = trimmed.Substring("view-source:".Length).Trim();
            if (string.IsNullOrEmpty(inner))
            {
                return "view-source:about:blank";
            }

            if (inner.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                inner.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                inner.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                inner.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                return "view-source:" + inner;
            }

            if (inner.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                inner.StartsWith("127.0.0.1"))
            {
                return "view-source:http://" + inner;
            }

            return "view-source:https://" + inner;
        }

        public static string ExtractViewSourceTarget(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            string trimmed = url.Trim();
            if (trimmed.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring("view-source:".Length).Trim();
            }
            return trimmed;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static InternalPageType GetInternalPageType(string url)
        {
            string norm = NormalizeInternalUrl(url);
            if (string.Equals(norm, BookmarksUrl, StringComparison.OrdinalIgnoreCase))
                return InternalPageType.Bookmarks;
            if (string.Equals(norm, HistoryUrl, StringComparison.OrdinalIgnoreCase))
                return InternalPageType.History;
            if (string.Equals(norm, DownloadsUrl, StringComparison.OrdinalIgnoreCase))
                return InternalPageType.Downloads;
            if (string.Equals(norm, NewTabUrl, StringComparison.OrdinalIgnoreCase))
                return InternalPageType.NewTab;
            if (string.Equals(norm, IncognitoUrl, StringComparison.OrdinalIgnoreCase))
                return InternalPageType.Incognito;
            if (norm.StartsWith(ScreenshotUrl, StringComparison.OrdinalIgnoreCase))
                return InternalPageType.Screenshot;
            if (string.Equals(norm, ClearBrowsingDataUrl, StringComparison.OrdinalIgnoreCase))
                return InternalPageType.ClearBrowsingData;
            if (string.Equals(norm, SettingsProxyUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(norm, ProxyUrl, StringComparison.OrdinalIgnoreCase))
                return InternalPageType.Proxy;
            if (string.Equals(norm, ImportDataUrl, StringComparison.OrdinalIgnoreCase))
                return InternalPageType.ImportData;
            if (string.Equals(norm, ExportDataUrl, StringComparison.OrdinalIgnoreCase))
                return InternalPageType.ExportData;
            return InternalPageType.None;
        }

        public static string GetInternalTitle(string url)
        {
            InternalPageType type = GetInternalPageType(url);
            switch (type)
            {
                case InternalPageType.Bookmarks:
                    return "Bookmarks";
                case InternalPageType.History:
                    return "History";
                case InternalPageType.Downloads:
                    return "Downloads";
                case InternalPageType.NewTab:
                case InternalPageType.Incognito:
                    return "New Tab";
                case InternalPageType.Screenshot:
                    return "Screenshot";
                case InternalPageType.ClearBrowsingData:
                    return AppStrings.ClearBrowsingData.DialogTitle;
                case InternalPageType.Proxy:
                    return AppStrings.Proxy.DialogTitle;
                case InternalPageType.ImportData:
                    return AppStrings.ImportData.PageTitle;
                case InternalPageType.ExportData:
                    return AppStrings.ExportData.PageTitle;
                default:
                    return "Internal Page";
            }
        }

        public static Image GetInternalFavicon(string url, bool isIncognito = false)
        {
            InternalPageType type = GetInternalPageType(url);
            switch (type)
            {
                case InternalPageType.Bookmarks:
                    return MaterialIcons.GetIcon("bookmarks", 16) ?? MaterialIcons.GetIcon("star-fill", 16);
                case InternalPageType.History:
                    return MaterialIcons.GetIcon("clock-counter-clockwise", 16);
                case InternalPageType.Downloads:
                    return MaterialIcons.GetIcon("download-simple", 16);
                case InternalPageType.NewTab:
                    return isIncognito
                        ? (MaterialIcons.GetIcon("incognito", 16) ?? MaterialIcons.GetIcon("house", 16, ThemeManager.AccentColor))
                        : (MaterialIcons.GetIcon("house", 16, ThemeManager.AccentColor) ?? WbAppIcon.GetAppBitmap(16, 16));
                case InternalPageType.Incognito:
                    return MaterialIcons.GetIcon("incognito", 16) ?? WbAppIcon.GetAppBitmap(16, 16);
                case InternalPageType.Screenshot:
                    return MaterialIcons.GetIcon("camera", 16) ?? WbAppIcon.GetAppBitmap(16, 16);
                case InternalPageType.ClearBrowsingData:
                    return MaterialIcons.GetIcon("trash", 16) ?? MaterialIcons.GetIcon("clock-counter-clockwise", 16);
                case InternalPageType.Proxy:
                    return MaterialIcons.GetIcon("globe", 16, ThemeManager.AccentColor) ?? MaterialIcons.GetIcon("gear-six", 16);
                case InternalPageType.ImportData:
                    return MaterialIcons.GetIcon("download-simple", 16) ?? MaterialIcons.GetIcon("folder-open", 16);
                case InternalPageType.ExportData:
                    return MaterialIcons.GetIcon("arrow-right", 16) ?? MaterialIcons.GetIcon("rocket-launch", 16);
                default:
                    return null;
            }
        }

        public static string ExtractScreenshotSessionId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int idx = url.IndexOf("id=", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string sub = url.Substring(idx + 3);
                int amp = sub.IndexOf('&');
                if (amp >= 0) sub = sub.Substring(0, amp);
                return sub.Trim();
            }
            return null;
        }

        public static string StartPageUrl
        {
            get { return NewTabUrl; }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool IsStartPage(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (string.Equals(url, NewTabUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url, "wb:newtab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url, "chrome://newtab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url, "about:newtab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url, IncognitoUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url, "wb:incognito", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url, "chrome://incognito", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url, "about:incognito", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url, "wb://private", StringComparison.OrdinalIgnoreCase) ||
                GetInternalPageType(url) == InternalPageType.NewTab ||
                GetInternalPageType(url) == InternalPageType.Incognito)
            {
                return true;
            }
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.Replace('\\', '/').TrimEnd('/');
            string normUrl = url.Replace('\\', '/');
            if (normUrl.IndexOf(baseDir, StringComparison.OrdinalIgnoreCase) >= 0 &&
                normUrl.EndsWith("/startpage.html", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        public static string EnsureStartPageFile()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string filePath = System.IO.Path.Combine(baseDir, "startpage.html");
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
            catch
            {
            }
            return NewTabUrl;
        }
    }
}
