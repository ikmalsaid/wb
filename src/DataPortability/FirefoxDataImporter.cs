using System;
using System.Collections.Generic;
using System.IO;
using wb.Bookmarks;
using wb.History;

namespace wb.DataPortability
{
    /// <summary>
    /// Extracts Bookmarks and Browsing History from Mozilla Firefox profiles via places.sqlite.
    /// </summary>
    public static class FirefoxDataImporter
    {
        private class RawFirefoxBookmark
        {
            public long Id;
            public int Type; // 1 = Bookmark, 2 = Folder
            public long Parent;
            public string Title;
            public long DateAdded;
            public string Url;
        }

        public static List<BookmarkItem> ImportBookmarks(string profileDir)
        {
            var results = new List<BookmarkItem>();
            if (string.IsNullOrEmpty(profileDir)) return results;

            string placesDb = Path.Combine(profileDir, "places.sqlite");
            if (!File.Exists(placesDb) || !SqliteReader.IsSupported) return results;

            var rawItems = new List<RawFirefoxBookmark>();
            string query = "SELECT b.id, b.type, b.parent, b.title, b.dateAdded, p.url " +
                           "FROM moz_bookmarks b " +
                           "LEFT JOIN moz_places p ON b.fk = p.id " +
                           "WHERE b.type IN (1, 2) " +
                           "ORDER BY b.parent, b.position;";

            SqliteReader.Query(placesDb, query, stmt =>
            {
                rawItems.Add(new RawFirefoxBookmark
                {
                    Id = SqliteReader.GetInt64(stmt, 0),
                    Type = SqliteReader.GetInt32(stmt, 1),
                    Parent = SqliteReader.GetInt64(stmt, 2),
                    Title = SqliteReader.GetString(stmt, 3),
                    DateAdded = SqliteReader.GetInt64(stmt, 4),
                    Url = SqliteReader.GetString(stmt, 5)
                });
            });

            if (rawItems.Count == 0) return results;

            // Map Firefox parent IDs:
            // 3 = Toolbar (Bookmarks bar)
            // 5 = Unfiled (Other bookmarks)
            // 6 = Mobile
            // 2 = Menu (Mapped to Other bookmarks)
            var idMap = new Dictionary<long, string>();

            for (int i = 0; i < rawItems.Count; i++)
            {
                var raw = rawItems[i];
                if (raw.Type == 2) // Folder
                {
                    if (raw.Id == 1 || raw.Id == 2 || raw.Id == 3 || raw.Id == 5 || raw.Id == 6)
                    {
                        continue;
                    }

                    string parentId;
                    if (raw.Parent == 3) parentId = "bar";
                    else if (raw.Parent == 5 || raw.Parent == 2) parentId = "other";
                    else if (raw.Parent == 6) parentId = "mobile";
                    else if (!idMap.TryGetValue(raw.Parent, out parentId)) parentId = "bar";

                    var folder = new BookmarkItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Title = string.IsNullOrEmpty(raw.Title) ? "Folder" : raw.Title,
                        Url = string.Empty,
                        CreatedAt = ParseFirefoxTimestamp(raw.DateAdded),
                        ParentId = parentId,
                        IsFolder = true
                    };

                    idMap[raw.Id] = folder.Id;
                    results.Add(folder);
                }
            }

            for (int i = 0; i < rawItems.Count; i++)
            {
                var raw = rawItems[i];
                if (raw.Type == 1 && !string.IsNullOrEmpty(raw.Url)) // Bookmark
                {
                    string parentId;
                    if (raw.Parent == 3) parentId = "bar";
                    else if (raw.Parent == 5 || raw.Parent == 2) parentId = "other";
                    else if (raw.Parent == 6) parentId = "mobile";
                    else if (!idMap.TryGetValue(raw.Parent, out parentId)) parentId = "bar";

                    var bookmark = new BookmarkItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Title = string.IsNullOrEmpty(raw.Title) ? raw.Url : raw.Title,
                        Url = raw.Url,
                        CreatedAt = ParseFirefoxTimestamp(raw.DateAdded),
                        ParentId = parentId,
                        IsFolder = false
                    };

                    results.Add(bookmark);
                }
            }

            return results;
        }

        public static List<HistoryItem> ImportHistory(string profileDir, int maxItems = 5000)
        {
            var results = new List<HistoryItem>();
            if (string.IsNullOrEmpty(profileDir)) return results;

            string placesDb = Path.Combine(profileDir, "places.sqlite");
            if (!File.Exists(placesDb) || !SqliteReader.IsSupported) return results;

            string query = string.Format(
                "SELECT url, title, last_visit_date " +
                "FROM moz_places " +
                "WHERE last_visit_date IS NOT NULL AND hidden = 0 " +
                "ORDER BY last_visit_date DESC LIMIT {0};",
                maxItems);

            SqliteReader.Query(placesDb, query, stmt =>
            {
                string url = SqliteReader.GetString(stmt, 0);
                string title = SqliteReader.GetString(stmt, 1);
                long lastVisitDate = SqliteReader.GetInt64(stmt, 2);

                if (!string.IsNullOrEmpty(url))
                {
                    results.Add(new HistoryItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Title = string.IsNullOrEmpty(title) ? url : title,
                        Url = url,
                        VisitedAt = ParseFirefoxTimestamp(lastVisitDate)
                    });
                }
            });

            return results;
        }

        public static DateTime ParseFirefoxTimestamp(long microseconds)
        {
            if (microseconds <= 0) return DateTime.Now;
            try
            {
                // Firefox timestamps are microseconds since Unix epoch (1970-01-01 UTC)
                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return epoch.AddMilliseconds(microseconds / 1000.0).ToLocalTime();
            }
            catch
            {
                return DateTime.Now;
            }
        }
    }
}

