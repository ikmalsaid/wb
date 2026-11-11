using System;

namespace wb.Browser
{
    /// <summary>
    /// Represents a customizable site shortcut on the wb new tab page.
    /// </summary>
    public class ShortcutItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string BadgeText { get; set; }
        public int ColorRgb { get; set; }

        public ShortcutItem()
        {
            Id = Guid.NewGuid().ToString("N");
        }

        public ShortcutItem(string title, string url, string badgeText = null, int colorRgb = 0)
        {
            Id = Guid.NewGuid().ToString("N");
            Title = title;
            Url = url;
            BadgeText = string.IsNullOrEmpty(badgeText) ? DeriveBadgeText(title, url) : badgeText;
            ColorRgb = colorRgb;
        }

        /// <summary>
        /// Derives a 1 or 2 letter badge abbreviation from site title or URL.
        /// </summary>
        public static string DeriveBadgeText(string title, string url)
        {
            if (!string.IsNullOrEmpty(title))
            {
                string t = title.Trim();
                if (t.Length <= 2) return t;

                string[] parts = t.Split(new char[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0)
                {
                    return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
                }
                return t.Substring(0, 1).ToUpperInvariant();
            }

            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    Uri uri = new Uri(url.Contains("://") ? url : "https://" + url);
                    string host = uri.Host.Replace("www.", "").Trim();
                    if (host.Length > 0)
                    {
                        return host.Substring(0, 1).ToUpperInvariant();
                    }
                }
                catch { }
            }

            return "?";
        }

        private static readonly int[] DefaultPalette = new int[]
        {
            unchecked((int)0xFF4285F4), // Blue
            unchecked((int)0xFFEA4335), // Red
            unchecked((int)0xFFFBBC05), // Yellow
            unchecked((int)0xFF34A853), // Green
            unchecked((int)0xFF8957E5), // Purple
            unchecked((int)0xFFFF6600)  // Orange
        };

        public static int DeriveColor(string url)
        {
            if (string.IsNullOrEmpty(url)) return DefaultPalette[0];
            int hash = Math.Abs(url.ToLowerInvariant().GetHashCode());
            return DefaultPalette[hash % DefaultPalette.Length];
        }
    }
}
