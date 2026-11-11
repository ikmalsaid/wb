using System;
using wb.AdBlock;
using wb.Bookmarks;
using wb.Downloads;
using wb.History;
using wb.SponsorBlock;

namespace wb.Core
{
    public static class AppServices
    {
        private static SqliteDatabase _database = new SqliteDatabase();
        private static readonly BookmarkManager _bookmarks = new BookmarkManager();
        private static readonly HistoryManager _history = new HistoryManager();
        private static readonly DownloadManager _downloads = new DownloadManager();
        private static readonly AdBlockManager _adBlock = new AdBlockManager();
        private static readonly SponsorBlockManager _sponsorBlock = new SponsorBlockManager();
        private static readonly wb.Browser.FaviconManager _favicons = new wb.Browser.FaviconManager();
        private static readonly wb.Browser.SiteMuteManager _siteMute = new wb.Browser.SiteMuteManager();
        private static readonly wb.Browser.ShortcutManager _shortcuts = new wb.Browser.ShortcutManager();
        private static readonly wb.Proxy.ProxyManager _proxy = new wb.Proxy.ProxyManager();

        public static SqliteDatabase Database
        {
            get { return _database; }
        }

        public static void ResetDatabaseForTest(string customDirectory = null)
        {
            if (_database != null)
            {
                try { _database.Dispose(); } catch { }
            }
            string dir = string.IsNullOrEmpty(customDirectory) ? StorageHelper.GetDataDirectory() : customDirectory;
            _database = new SqliteDatabase(System.IO.Path.Combine(dir, "wb.db"));
        }

        public static BookmarkManager Bookmarks
        {
            get { return _bookmarks; }
        }

        public static HistoryManager History
        {
            get { return _history; }
        }

        public static DownloadManager Downloads
        {
            get { return _downloads; }
        }

        public static AdBlockManager AdBlock
        {
            get { return _adBlock; }
        }

        public static SponsorBlockManager SponsorBlock
        {
            get { return _sponsorBlock; }
        }

        public static wb.Browser.FaviconManager Favicons
        {
            get { return _favicons; }
        }

        public static wb.Browser.SiteMuteManager SiteMute
        {
            get { return _siteMute; }
        }

        public static wb.Browser.ShortcutManager Shortcuts
        {
            get { return _shortcuts; }
        }

        public static wb.Proxy.ProxyManager Proxy
        {
            get { return _proxy; }
        }
    }
}
