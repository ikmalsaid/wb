using System;
using System.Collections.Generic;
using wb.Core;
using wb.Browser;

namespace wb.History
{
    public class HistoryManager
    {
        private const string FileName = "history.json";
        private readonly List<HistoryItem> _history;
        private readonly object _lock = new object();

        private readonly Dictionary<string, HistoryItem> _urlLookup = new Dictionary<string, HistoryItem>(StringComparer.OrdinalIgnoreCase);
        private List<HistoryItem> _cachedDefaultSorted = null;

        private void InvalidateCaches()
        {
            _cachedDefaultSorted = null;
        }

        public event EventHandler HistoryChanged;

        public HistoryManager()
        {
            _history = new List<HistoryItem>();
            LoadFromDatabaseOrMigrate();
            if (SanitizeHistory())
            {
                // Sync any sanitized removals
                AppServices.Database.InTransaction(() =>
                {
                    AppServices.Database.ExecuteNonQuery("DELETE FROM history;");
                    for (int i = 0; i < _history.Count; i++)
                    {
                        var it = _history[i];
                        AppServices.Database.ExecuteNonQuery(
                            "INSERT INTO history (id, title, url, visited_at, visit_count) VALUES (?, ?, ?, ?, 1);",
                            it.Id, it.Title, it.Url, it.VisitedAt.Ticks);
                    }
                });
            }
            RebuildUrlLookup();
        }

        private void LoadFromDatabaseOrMigrate()
        {
            try
            {
                AppServices.Database.Query("SELECT id, title, url, visited_at FROM history ORDER BY visited_at DESC LIMIT 2000;", stmt =>
                {
                    _history.Add(new HistoryItem
                    {
                        Id = SqliteDatabase.GetString(stmt, 0),
                        Title = SqliteDatabase.GetString(stmt, 1),
                        Url = SqliteDatabase.GetString(stmt, 2),
                        VisitedAt = SqliteDatabase.GetDateTime(stmt, 3)
                    });
                });

                string configPath = System.IO.Path.Combine(StorageHelper.GetConfigDirectory(), FileName);
                string legacyPath = System.IO.Path.Combine(StorageHelper.GetDataDirectory(), FileName);
                string jsonPath = System.IO.File.Exists(configPath) ? configPath : (System.IO.File.Exists(legacyPath) ? legacyPath : null);
                if (_history.Count == 0 && jsonPath != null)
                    {
                        var legacyList = StorageHelper.LoadList<HistoryItem>(FileName);
                        if (legacyList != null && legacyList.Count > 0)
                        {
                            AppServices.Database.InTransaction(() =>
                            {
                                for (int i = 0; i < legacyList.Count; i++)
                                {
                                    var item = legacyList[i];
                                    if (item != null && !string.IsNullOrEmpty(item.Url))
                                    {
                                        AppServices.Database.ExecuteNonQuery(
                                            "INSERT OR IGNORE INTO history (id, title, url, visited_at, visit_count) VALUES (?, ?, ?, ?, 1);",
                                            item.Id, item.Title ?? item.Url, item.Url, item.VisitedAt.Ticks);
                                        _history.Add(item);
                                    }
                                }
                            });

                            try
                            {
                                string bakPath = jsonPath + ".bak";
                                if (System.IO.File.Exists(bakPath)) System.IO.File.Delete(bakPath);
                                System.IO.File.Move(jsonPath, bakPath);
                            }
                            catch { }
                        }
                    }
            }
            catch { }
        }

        private void RebuildUrlLookup()
        {
            _urlLookup.Clear();
            if (_history != null)
            {
                for (int i = 0; i < _history.Count; i++)
                {
                    HistoryItem item = _history[i];
                    if (item != null && !string.IsNullOrEmpty(item.Url))
                    {
                        _urlLookup[item.Url] = item;
                    }
                }
            }
        }

        private bool SanitizeHistory()
        {
            if (_history == null || _history.Count == 0) return false;
            int removed = _history.RemoveAll(item =>
                item == null ||
                string.IsNullOrEmpty(item.Url) ||
                item.Url.IndexOf("history.example.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                WebErrorPage.IsErrorPage(item.Url) ||
                WebErrorPage.IsChromiumInternalErrorPage(item.Url));
            return removed > 0;
        }

        public List<HistoryItem> GetAll()
        {
            lock (_lock)
            {
                return new List<HistoryItem>(_history);
            }
        }

        public void AddEntry(string title, string url)
        {
            if (string.IsNullOrEmpty(url) ||
                UrlHelper.IsSpecialScheme(url) ||
                url.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase) ||
                url.IndexOf("history.example.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                WebErrorPage.IsErrorPage(url) ||
                WebErrorPage.IsChromiumInternalErrorPage(url) ||
                InternalPageHelper.IsInternalUrl(url) ||
                url.IndexOf("startpage.html", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            lock (_lock)
            {
                HistoryItem existing;
                if (_urlLookup.TryGetValue(url, out existing))
                {
                    existing.VisitedAt = DateTime.Now;
                    if (!string.IsNullOrEmpty(title))
                    {
                        existing.Title = title;
                    }
                    AppServices.Database.ExecuteNonQuery(
                        "UPDATE history SET title = ?, visited_at = ?, visit_count = visit_count + 1 WHERE id = ?;",
                        existing.Title, existing.VisitedAt.Ticks, existing.Id);
                }
                else
                {
                    HistoryItem newItem = new HistoryItem(title, url);
                    _history.Add(newItem);
                    _urlLookup[url] = newItem;
                    AppServices.Database.ExecuteNonQuery(
                        "INSERT OR REPLACE INTO history (id, title, url, visited_at, visit_count) VALUES (?, ?, ?, ?, 1);",
                        newItem.Id, newItem.Title, newItem.Url, newItem.VisitedAt.Ticks);
                }

                // Maintain max in-memory cache limit
                if (_history.Count > 10000)
                {
                    int toRemove = _history.Count - 10000;
                    for (int i = 0; i < toRemove; i++)
                    {
                        if (_history[i] != null && !string.IsNullOrEmpty(_history[i].Url))
                        {
                            _urlLookup.Remove(_history[i].Url);
                        }
                    }
                    _history.RemoveRange(0, toRemove);
                }
            }

            OnHistoryChanged();
        }

        public class HistoryImportBatchResult
        {
            public int ItemsImported { get; set; }
            public int SkippedDuplicates { get; set; }
        }

        public HistoryImportBatchResult ImportBatch(List<HistoryItem> items, bool skipDuplicates = true)
        {
            var result = new HistoryImportBatchResult();
            if (items == null || items.Count == 0) return result;

            bool addedAny = false;
            lock (_lock)
            {
                var validItems = new List<HistoryItem>();
                for (int i = 0; i < items.Count; i++)
                {
                    HistoryItem item = items[i];
                    if (item == null || string.IsNullOrEmpty(item.Url)) continue;

                    string url = item.Url;
                    if (UrlHelper.IsSpecialScheme(url) ||
                        url.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase) ||
                        url.IndexOf("history.example.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        WebErrorPage.IsErrorPage(url) ||
                        WebErrorPage.IsChromiumInternalErrorPage(url) ||
                        InternalPageHelper.IsInternalUrl(url) ||
                        url.IndexOf("startpage.html", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    if (skipDuplicates && _urlLookup.ContainsKey(url))
                    {
                        result.SkippedDuplicates++;
                        continue;
                    }

                    HistoryItem entry = new HistoryItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Title = string.IsNullOrEmpty(item.Title) ? url : item.Title,
                        Url = url,
                        VisitedAt = item.VisitedAt == default(DateTime) ? DateTime.Now : item.VisitedAt
                    };

                    _history.Add(entry);
                    _urlLookup[url] = entry;
                    validItems.Add(entry);
                    result.ItemsImported++;
                    addedAny = true;
                }

                if (addedAny)
                {
                    AppServices.Database.InTransaction(() =>
                    {
                        for (int i = 0; i < validItems.Count; i++)
                        {
                            var it = validItems[i];
                            AppServices.Database.ExecuteNonQuery(
                                "INSERT OR REPLACE INTO history (id, title, url, visited_at, visit_count) VALUES (?, ?, ?, ?, 1);",
                                it.Id, it.Title, it.Url, it.VisitedAt.Ticks);
                        }
                    });
                }
            }

            if (addedAny)
            {
                OnHistoryChanged();
            }

            return result;
        }

        public bool RemoveEntry(string id)
        {
            bool removed = false;
            lock (_lock)
            {
                for (int i = 0; i < _history.Count; i++)
                {
                    if (string.Equals(_history[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(_history[i].Url))
                        {
                            _urlLookup.Remove(_history[i].Url);
                        }
                        _history.RemoveAt(i);
                        removed = true;
                        AppServices.Database.ExecuteNonQuery("DELETE FROM history WHERE id = ?;", id);
                        break;
                    }
                }
            }

            if (removed)
            {
                OnHistoryChanged();
            }
            return removed;
        }

        public int GetCountSince(DateTime cutoff)
        {
            lock (_lock)
            {
                if (cutoff == DateTime.MinValue) return _history.Count;
                int count = 0;
                for (int i = 0; i < _history.Count; i++)
                {
                    if (_history[i] != null && _history[i].VisitedAt >= cutoff)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public int ClearSince(DateTime cutoff)
        {
            int removed = 0;
            lock (_lock)
            {
                if (cutoff == DateTime.MinValue)
                {
                    removed = _history.Count;
                    _history.Clear();
                    _urlLookup.Clear();
                    AppServices.Database.ExecuteNonQuery("DELETE FROM history;");
                }
                else
                {
                    removed = _history.RemoveAll(item =>
                    {
                        if (item != null && item.VisitedAt >= cutoff)
                        {
                            if (!string.IsNullOrEmpty(item.Url))
                            {
                                _urlLookup.Remove(item.Url);
                            }
                            return true;
                        }
                        return false;
                    });
                    if (removed > 0)
                    {
                        AppServices.Database.ExecuteNonQuery("DELETE FROM history WHERE visited_at >= ?;", cutoff.Ticks);
                    }
                }
            }
            if (removed > 0)
            {
                OnHistoryChanged();
            }
            return removed;
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                _history.Clear();
                _urlLookup.Clear();
                AppServices.Database.ExecuteNonQuery("DELETE FROM history;");
            }
            OnHistoryChanged();
        }

        public void Clear()
        {
            ClearAll();
        }

        public List<HistoryItem> FindMatches(string query, int maxCount)
        {
            if (string.IsNullOrEmpty(query) || maxCount <= 0) return new List<HistoryItem>(0);
            List<HistoryItem> results = new List<HistoryItem>(Math.Min(maxCount, 8));
            lock (_lock)
            {
                // Iterate from end (most recent visits first)
                for (int i = _history.Count - 1; i >= 0 && results.Count < maxCount; i--)
                {
                    HistoryItem h = _history[i];
                    if (h == null || string.IsNullOrEmpty(h.Url)) continue;

                    bool matchTitle = !string.IsNullOrEmpty(h.Title) && h.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchUrl = h.Url.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (matchTitle || matchUrl)
                    {
                        results.Add(h);
                    }
                }
            }
            return results;
        }

        public List<HistoryItem> GetSorted(string query, SortMode sortMode)
        {
            if (string.IsNullOrEmpty(query) && (sortMode == SortMode.DateDescending || (int)sortMode == 0))
            {
                lock (_lock)
                {
                    if (_cachedDefaultSorted != null)
                    {
                        return new List<HistoryItem>(_cachedDefaultSorted);
                    }
                }
            }

            List<HistoryItem> list;
            lock (_lock)
            {
                if (string.IsNullOrEmpty(query))
                {
                    list = new List<HistoryItem>(_history);
                }
                else
                {
                    list = new List<HistoryItem>();
                    for (int i = 0; i < _history.Count; i++)
                    {
                        HistoryItem item = _history[i];
                        if (item != null &&
                            ((item.Title != null && item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                             (item.Url != null && item.Url.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
                        {
                            list.Add(item);
                        }
                    }
                }
            }

            // Sort
            switch (sortMode)
            {
                case SortMode.TitleAscending:
                    list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortMode.TitleDescending:
                    list.Sort((a, b) => string.Compare(b.Title, a.Title, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortMode.DateAscending:
                    list.Sort((a, b) => a.VisitedAt.CompareTo(b.VisitedAt));
                    break;
                case SortMode.DateDescending:
                default:
                    list.Sort((a, b) => b.VisitedAt.CompareTo(a.VisitedAt));
                    break;
            }

            if (string.IsNullOrEmpty(query) && (sortMode == SortMode.DateDescending || (int)sortMode == 0))
            {
                lock (_lock)
                {
                    _cachedDefaultSorted = new List<HistoryItem>(list);
                }
            }

            return list;
        }

        private void Save()
        {
            StorageHelper.SaveList(FileName, _history);
        }

        private void OnHistoryChanged()
        {
            lock (_lock)
            {
                InvalidateCaches();
            }
            EventHandler handler = HistoryChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
