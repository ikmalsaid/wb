using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using wb.Bookmarks;
using wb.Browser;
using wb.History;

namespace wb.DataPortability
{
    /// <summary>
    /// Extracts Bookmarks, Browsing History, and Top Sites from Chromium-based browsers
    /// (Google Chrome, Microsoft Edge, Brave, Opera, Opera GX, Vivaldi, Arc, Yandex, Thorium, CocCoc).
    /// </summary>
    public static class ChromiumDataImporter
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static List<BookmarkItem> ImportBookmarks(string profileDir)
        {
            var results = new List<BookmarkItem>();
            if (string.IsNullOrEmpty(profileDir)) return results;

            string bookmarksPath = Path.Combine(profileDir, "Bookmarks");
            if (!File.Exists(bookmarksPath)) return results;

            try
            {
                string json = File.ReadAllText(bookmarksPath, Encoding.UTF8);
                var rootDict = Serializer.Deserialize<Dictionary<string, object>>(json);
                if (rootDict == null || !rootDict.ContainsKey("roots")) return results;

                var roots = rootDict["roots"] as Dictionary<string, object>;
                if (roots == null) return results;

                // 1. Bookmarks Bar
                if (roots.ContainsKey("bookmark_bar"))
                {
                    ProcessChromiumNode(roots["bookmark_bar"] as Dictionary<string, object>, "bar", results, isRoot: true);
                }

                // 2. Other Bookmarks
                if (roots.ContainsKey("other"))
                {
                    ProcessChromiumNode(roots["other"] as Dictionary<string, object>, "other", results, isRoot: true);
                }

                // 3. Mobile / Synced Bookmarks
                if (roots.ContainsKey("synced"))
                {
                    ProcessChromiumNode(roots["synced"] as Dictionary<string, object>, "mobile", results, isRoot: true);
                }
            }
            catch { }

            return results;
        }

        private static void ProcessChromiumNode(
            Dictionary<string, object> node,
            string currentParentId,
            List<BookmarkItem> results,
            bool isRoot = false)
        {
            if (node == null) return;

            string type = node.ContainsKey("type") ? Convert.ToString(node["type"]) : null;

            if (isRoot)
            {
                // Root container: recurse directly into children
                if (node.ContainsKey("children"))
                {
                    var children = node["children"] as ArrayList;
                    if (children != null)
                    {
                        for (int i = 0; i < children.Count; i++)
                        {
                            ProcessChromiumNode(children[i] as Dictionary<string, object>, currentParentId, results, isRoot: false);
                        }
                    }
                }
                return;
            }

            string name = node.ContainsKey("name") ? Convert.ToString(node["name"]) : string.Empty;
            DateTime dateAdded = ParseChromiumTimestamp(node.ContainsKey("date_added") ? node["date_added"] : null);

            if (string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase))
            {
                var folder = new BookmarkItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = string.IsNullOrEmpty(name) ? "Folder" : name,
                    Url = string.Empty,
                    CreatedAt = dateAdded,
                    ParentId = currentParentId,
                    IsFolder = true
                };
                results.Add(folder);

                if (node.ContainsKey("children"))
                {
                    var children = node["children"] as ArrayList;
                    if (children != null)
                    {
                        for (int i = 0; i < children.Count; i++)
                        {
                            ProcessChromiumNode(children[i] as Dictionary<string, object>, folder.Id, results, isRoot: false);
                        }
                    }
                }
            }
            else if (string.Equals(type, "url", StringComparison.OrdinalIgnoreCase))
            {
                string url = node.ContainsKey("url") ? Convert.ToString(node["url"]) : null;
                if (!string.IsNullOrEmpty(url))
                {
                    var bookmark = new BookmarkItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Title = string.IsNullOrEmpty(name) ? url : name,
                        Url = url,
                        CreatedAt = dateAdded,
                        ParentId = currentParentId,
                        IsFolder = false
                    };
                    results.Add(bookmark);
                }
            }
        }

        public static List<HistoryItem> ImportHistory(string profileDir, int maxItems = 5000)
        {
            var results = new List<HistoryItem>();
            if (string.IsNullOrEmpty(profileDir)) return results;

            string historyDbPath = Path.Combine(profileDir, "History");
            if (!File.Exists(historyDbPath) || !SqliteReader.IsSupported) return results;

            string query = string.Format("SELECT url, title, last_visit_time FROM urls ORDER BY last_visit_time DESC LIMIT {0};", maxItems);

            SqliteReader.Query(historyDbPath, query, stmt =>
            {
                string url = SqliteReader.GetString(stmt, 0);
                string title = SqliteReader.GetString(stmt, 1);
                long lastVisitTime = SqliteReader.GetInt64(stmt, 2);

                if (!string.IsNullOrEmpty(url))
                {
                    results.Add(new HistoryItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Title = string.IsNullOrEmpty(title) ? url : title,
                        Url = url,
                        VisitedAt = ParseChromiumTimestamp(lastVisitTime)
                    });
                }
            });

            return results;
        }

        public static List<ShortcutItem> ImportShortcuts(string profileDir, int maxItems = 8)
        {
            var results = new List<ShortcutItem>();
            if (string.IsNullOrEmpty(profileDir)) return results;

            string topSitesPath = Path.Combine(profileDir, "Top Sites");
            if (!File.Exists(topSitesPath) || !SqliteReader.IsSupported) return results;

            string query = string.Format("SELECT url, title FROM top_sites ORDER BY url_rank ASC LIMIT {0};", maxItems);

            SqliteReader.Query(topSitesPath, query, stmt =>
            {
                string url = SqliteReader.GetString(stmt, 0);
                string title = SqliteReader.GetString(stmt, 1);

                if (!string.IsNullOrEmpty(url))
                {
                    results.Add(new ShortcutItem(
                        string.IsNullOrEmpty(title) ? url : title,
                        url,
                        ShortcutItem.DeriveBadgeText(title, url),
                        ShortcutItem.DeriveColor(url)));
                }
            });

            return results;
        }

        public static DateTime ParseChromiumTimestamp(object dateObj)
        {
            if (dateObj == null) return DateTime.Now;
            long microseconds;
            if (long.TryParse(dateObj.ToString(), out microseconds) && microseconds > 0)
            {
                try
                {
                    // Chromium timestamp is microseconds since 1601-01-01 UTC (Windows FILETIME is 100-ns intervals)
                    long fileTime = microseconds * 10;
                    return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
                }
                catch { }
            }
            return DateTime.Now;
        }
    }
}

