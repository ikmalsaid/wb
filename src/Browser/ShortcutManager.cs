using System;
using System.Collections.Generic;
using wb.Core;

namespace wb.Browser
{
    /// <summary>
    /// Manages loading, adding, editing, deleting and persisting shortcuts for the wb new tab page.
    /// </summary>
    public class ShortcutManager
    {
        private const string FileName = "shortcuts.json";
        private readonly List<ShortcutItem> _shortcuts;
        private readonly object _lock = new object();

        public event EventHandler ShortcutsChanged;

        public ShortcutManager()
        {
            _shortcuts = new List<ShortcutItem>();
            LoadFromDatabaseOrMigrate();
        }

        private void LoadFromDatabaseOrMigrate()
        {
            try
            {
                if (AppServices.Database != null)
                {
                    AppServices.Database.Query(
                        "SELECT id, title, url, badge_text, color_rgb, position FROM shortcuts ORDER BY position ASC;",
                        stmt =>
                        {
                            _shortcuts.Add(new ShortcutItem
                            {
                                Id = SqliteDatabase.GetString(stmt, 0),
                                Title = SqliteDatabase.GetString(stmt, 1),
                                Url = SqliteDatabase.GetString(stmt, 2),
                                BadgeText = SqliteDatabase.GetString(stmt, 3),
                                ColorRgb = SqliteDatabase.GetInt32(stmt, 4)
                            });
                        });
                }

                string configPath = System.IO.Path.Combine(StorageHelper.GetConfigDirectory(), FileName);
                string legacyPath = System.IO.Path.Combine(StorageHelper.GetDataDirectory(), FileName);
                string jsonPath = System.IO.File.Exists(configPath) ? configPath : (System.IO.File.Exists(legacyPath) ? legacyPath : null);
                if (jsonPath != null)
                {
                    var legacy = StorageHelper.LoadList<ShortcutItem>(FileName);
                    if (legacy != null && legacy.Count > 0)
                    {
                        HashSet<string> existingUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < _shortcuts.Count; i++)
                        {
                            if (_shortcuts[i] != null && !string.IsNullOrEmpty(_shortcuts[i].Url))
                            {
                                existingUrls.Add(_shortcuts[i].Url);
                            }
                        }

                        bool anyAdded = false;
                        for (int i = 0; i < legacy.Count; i++)
                        {
                            var s = legacy[i];
                            if (s != null && !string.IsNullOrEmpty(s.Url) && !existingUrls.Contains(s.Url))
                            {
                                _shortcuts.Add(s);
                                existingUrls.Add(s.Url);
                                anyAdded = true;
                            }
                        }
                        if (anyAdded || _shortcuts.Count == legacy.Count)
                        {
                            Save();
                        }
                        try
                        {
                            string bak = jsonPath + ".bak";
                            if (System.IO.File.Exists(bak)) System.IO.File.Delete(bak);
                            System.IO.File.Move(jsonPath, bak);
                        }
                        catch { }
                    }
                }

                if (_shortcuts.Count == 0)
                {
                    _shortcuts.Clear();
                    _shortcuts.AddRange(CreateDefaultShortcuts());
                    Save();
                }
            }
            catch
            {
                if (_shortcuts.Count == 0)
                {
                    _shortcuts.Clear();
                    _shortcuts.AddRange(CreateDefaultShortcuts());
                }
            }
        }

        public static List<ShortcutItem> CreateDefaultShortcuts()
        {
            return new List<ShortcutItem>
            {
                new ShortcutItem("GitHub", "https://github.com", "G", unchecked((int)0xFF8957E5)),
                new ShortcutItem("Google", "https://www.google.com", "Go", unchecked((int)0xFF4285F4)),
                new ShortcutItem("YouTube", "https://www.youtube.com", "YT", unchecked((int)0xFFFF0000)),
                new ShortcutItem("Reddit", "https://reddit.com", "R", unchecked((int)0xFFFF4500)),
                new ShortcutItem("Wikipedia", "https://en.wikipedia.org", "W", unchecked((int)0xFF58A6FF)),
                new ShortcutItem("Stack Overflow", "https://stackoverflow.com", "SO", unchecked((int)0xFFF48024)),
                new ShortcutItem("Hacker News", "https://news.ycombinator.com", "HN", unchecked((int)0xFFFF6600))
            };
        }

        public List<ShortcutItem> GetAll()
        {
            lock (_lock)
            {
                return new List<ShortcutItem>(_shortcuts);
            }
        }

        public ShortcutItem Add(string title, string url, string badgeText = null, int colorRgb = 0)
        {
            if (string.IsNullOrEmpty(url)) return null;

            ShortcutItem item;
            lock (_lock)
            {
                if (_shortcuts.Count >= 8) return null;
                item = new ShortcutItem(title, url, badgeText, colorRgb);
                _shortcuts.Add(item);
                Save();
            }

            OnShortcutsChanged();
            return item;
        }

        public int ImportBatch(List<ShortcutItem> items, bool skipDuplicates = true)
        {
            if (items == null || items.Count == 0) return 0;
            int imported = 0;

            lock (_lock)
            {
                var existingUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _shortcuts.Count; i++)
                {
                    if (_shortcuts[i] != null && !string.IsNullOrEmpty(_shortcuts[i].Url))
                    {
                        existingUrls.Add(_shortcuts[i].Url);
                    }
                }

                for (int i = 0; i < items.Count && _shortcuts.Count < 8; i++)
                {
                    ShortcutItem item = items[i];
                    if (item == null || string.IsNullOrEmpty(item.Url)) continue;

                    if (skipDuplicates && existingUrls.Contains(item.Url))
                    {
                        continue;
                    }

                    string badge = !string.IsNullOrEmpty(item.BadgeText)
                        ? item.BadgeText
                        : ShortcutItem.DeriveBadgeText(item.Title, item.Url);

                    int color = item.ColorRgb != 0
                        ? item.ColorRgb
                        : ShortcutItem.DeriveColor(item.Url);

                    var sc = new ShortcutItem(
                        string.IsNullOrEmpty(item.Title) ? item.Url : item.Title,
                        item.Url,
                        badge,
                        color);

                    _shortcuts.Add(sc);
                    existingUrls.Add(sc.Url);
                    imported++;
                }

                if (imported > 0)
                {
                    Save();
                }
            }

            if (imported > 0)
            {
                OnShortcutsChanged();
            }

            return imported;
        }

        public bool Update(string id, string title, string url)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url)) return false;

            bool found = false;
            lock (_lock)
            {
                for (int i = 0; i < _shortcuts.Count; i++)
                {
                    if (string.Equals(_shortcuts[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        _shortcuts[i].Title = title;
                        _shortcuts[i].Url = url;
                        _shortcuts[i].BadgeText = ShortcutItem.DeriveBadgeText(title, url);
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    Save();
                }
            }

            if (found)
            {
                OnShortcutsChanged();
            }
            return found;
        }

        public bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            bool removed = false;
            lock (_lock)
            {
                for (int i = 0; i < _shortcuts.Count; i++)
                {
                    if (string.Equals(_shortcuts[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        _shortcuts.RemoveAt(i);
                        removed = true;
                        Save();
                        break;
                    }
                }
            }

            if (removed)
            {
                OnShortcutsChanged();
            }
            return removed;
        }

        public void ResetToDefaults()
        {
            lock (_lock)
            {
                _shortcuts.Clear();
                _shortcuts.AddRange(CreateDefaultShortcuts());
                Save();
            }
            OnShortcutsChanged();
        }

        private void Save()
        {
            if (AppServices.Database == null) return;
            try
            {
                AppServices.Database.InTransaction(() =>
                {
                    AppServices.Database.ExecuteNonQuery("DELETE FROM shortcuts;");
                    for (int i = 0; i < _shortcuts.Count; i++)
                    {
                        ShortcutItem item = _shortcuts[i];
                        if (item == null) continue;
                        AppServices.Database.ExecuteNonQuery(
                            "INSERT INTO shortcuts (id, title, url, badge_text, color_rgb, position) VALUES (?, ?, ?, ?, ?, ?);",
                            item.Id,
                            item.Title ?? item.Url,
                            item.Url,
                            item.BadgeText ?? string.Empty,
                            item.ColorRgb,
                            i);
                    }
                });
            }
            catch { }
        }

        protected virtual void OnShortcutsChanged()
        {
            EventHandler handler = ShortcutsChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
