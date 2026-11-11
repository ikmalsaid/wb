using System;
using System.Collections.Generic;
using wb.Core;

namespace wb.Bookmarks
{
    [Serializable]
    public class BookmarkConfigModel
    {
        public bool ShowBookmarksBar { get; set; }

        public BookmarkConfigModel()
        {
            ShowBookmarksBar = true;
        }
    }

    public class BookmarkManager
    {
        private const string FileName = "bookmarks.json";
        private const string ConfigFileName = "bookmarks_config.json";
        private readonly List<BookmarkItem> _bookmarks;
        private readonly HashSet<string> _urlIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        // High-performance caching layers
        private List<BookmarkItem> _cachedFolders = null;
        private Dictionary<string, List<BookmarkItem>> _cachedBarItemsByFolder = null;
        private Dictionary<string, BookmarkItem> _cachedById = null;
        private Dictionary<SortMode, List<BookmarkItem>> _cachedSorted = null;

        private void InvalidateCaches()
        {
            _cachedFolders = null;
            _cachedBarItemsByFolder = null;
            _cachedById = null;
            _cachedSorted = null;
        }

        public event EventHandler BookmarksChanged;
        public event EventHandler BookmarksBarVisibilityChanged;

        public bool ShowBookmarksBar
        {
            get
            {
                lock (_lock)
                {
                    return AppServices.Database != null
                        ? AppServices.Database.GetSettingBool("bookmarks_show_bar", true)
                        : true;
                }
            }
            set
            {
                bool changed = false;
                lock (_lock)
                {
                    if (AppServices.Database != null)
                    {
                        bool current = AppServices.Database.GetSettingBool("bookmarks_show_bar", true);
                        if (current != value)
                        {
                            AppServices.Database.SetSettingBool("bookmarks_show_bar", value);
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    OnBookmarksBarVisibilityChanged();
                }
            }
        }

        public BookmarkManager()
        {
            _bookmarks = new List<BookmarkItem>();
            LoadFromDatabaseOrMigrate();
        }

        private void LoadFromDatabaseOrMigrate()
        {
            try
            {
                // Check if legacy config exists to migrate
                string configPath = System.IO.Path.Combine(StorageHelper.GetDataDirectory(), ConfigFileName);
                if (System.IO.File.Exists(configPath))
                {
                    var cfg = StorageHelper.LoadObject<BookmarkConfigModel>(ConfigFileName);
                    if (cfg != null && AppServices.Database != null)
                    {
                        AppServices.Database.SetSettingBool("bookmarks_show_bar", cfg.ShowBookmarksBar);
                    }
                    try
                    {
                        string bakConfig = configPath + ".bak";
                        if (System.IO.File.Exists(bakConfig)) System.IO.File.Delete(bakConfig);
                        System.IO.File.Move(configPath, bakConfig);
                    }
                    catch { }
                }

                if (AppServices.Database != null)
                {
                    AppServices.Database.Query(
                        "SELECT id, title, url, created_at, parent_id, is_folder, position FROM bookmarks ORDER BY position ASC, created_at ASC;",
                        stmt =>
                        {
                            _bookmarks.Add(new BookmarkItem
                            {
                                Id = SqliteDatabase.GetString(stmt, 0),
                                Title = SqliteDatabase.GetString(stmt, 1),
                                Url = SqliteDatabase.GetString(stmt, 2),
                                CreatedAt = SqliteDatabase.GetDateTime(stmt, 3),
                                ParentId = SqliteDatabase.GetString(stmt, 4),
                                IsFolder = SqliteDatabase.GetBool(stmt, 5)
                            });
                        });
                }

                // If SQLite bookmarks empty, check legacy bookmarks.json
                string jsonConfigPath = System.IO.Path.Combine(StorageHelper.GetConfigDirectory(), FileName);
                string jsonLegacyPath = System.IO.Path.Combine(StorageHelper.GetDataDirectory(), FileName);
                string jsonPath = System.IO.File.Exists(jsonConfigPath) ? jsonConfigPath : (System.IO.File.Exists(jsonLegacyPath) ? jsonLegacyPath : null);
                if (jsonPath != null)
                {
                    var legacy = StorageHelper.LoadList<BookmarkItem>(FileName);
                    if (legacy != null && legacy.Count > 0)
                    {
                        HashSet<string> existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < _bookmarks.Count; i++)
                        {
                            if (_bookmarks[i] != null && _bookmarks[i].Id != null) existingIds.Add(_bookmarks[i].Id);
                        }

                        bool anyAdded = false;
                        for (int i = 0; i < legacy.Count; i++)
                        {
                            var b = legacy[i];
                            if (b != null && !existingIds.Contains(b.Id))
                            {
                                _bookmarks.Add(b);
                                existingIds.Add(b.Id);
                                anyAdded = true;
                            }
                        }
                        if (anyAdded || _bookmarks.Count == legacy.Count)
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

                if (_bookmarks.Count == 0)
                {
                    SeedDefaultBookmarks();
                }
                else
                {
                    for (int i = 0; i < _bookmarks.Count; i++)
                    {
                        BookmarkItem item = _bookmarks[i];
                        if (item != null && !item.IsFolder && !string.IsNullOrEmpty(item.Url))
                        {
                            _urlIndex.Add(item.Url);
                        }
                    }
                }
            }
            catch
            {
                if (_bookmarks.Count == 0)
                {
                    SeedDefaultBookmarks();
                }
            }
        }

        private void SeedDefaultBookmarks()
        {
            BookmarkItem gh = new BookmarkItem("GitHub", "https://github.com/", "bar");
            BookmarkItem yt = new BookmarkItem("YouTube", "https://www.youtube.com/", "bar");
            BookmarkItem wp = new BookmarkItem("Wikipedia", "https://www.wikipedia.org/", "bar");
            BookmarkItem tools = BookmarkItem.CreateFolder("Tools", "bar");
            BookmarkItem google = new BookmarkItem("Google", "https://www.google.com/", tools.Id);
            BookmarkItem chatgpt = new BookmarkItem("ChatGPT", "https://chatgpt.com/", tools.Id);
            BookmarkItem archive = new BookmarkItem("Internet Archive", "https://archive.org/", "other");

            _bookmarks.Add(gh);
            _bookmarks.Add(yt);
            _bookmarks.Add(wp);
            _bookmarks.Add(tools);
            _bookmarks.Add(google);
            _bookmarks.Add(chatgpt);
            _bookmarks.Add(archive);

            _urlIndex.Add(gh.Url);
            _urlIndex.Add(yt.Url);
            _urlIndex.Add(wp.Url);
            _urlIndex.Add(google.Url);
            _urlIndex.Add(chatgpt.Url);
            _urlIndex.Add(archive.Url);

            Save();
        }

        public List<BookmarkItem> GetAll()
        {
            lock (_lock)
            {
                return new List<BookmarkItem>(_bookmarks);
            }
        }

        public bool IsBookmarked(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            lock (_lock)
            {
                return _urlIndex.Contains(url);
            }
        }

        public BookmarkItem GetByUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            lock (_lock)
            {
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    BookmarkItem item = _bookmarks[i];
                    if (item != null && !item.IsFolder && string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase))
                    {
                        return item;
                    }
                }
            }
            return null;
        }

        public BookmarkItem GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_lock)
            {
                if (_cachedById == null)
                {
                    _cachedById = new Dictionary<string, BookmarkItem>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < _bookmarks.Count; i++)
                    {
                        BookmarkItem b = _bookmarks[i];
                        if (b != null && !string.IsNullOrEmpty(b.Id))
                        {
                            _cachedById[b.Id] = b;
                        }
                    }
                }

                BookmarkItem item;
                return _cachedById.TryGetValue(id, out item) ? item : null;
            }
        }

        public BookmarkItem Add(string title, string url)
        {
            return Add(title, url, "bar");
        }

        public BookmarkItem Add(string title, string url, string parentId)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string targetParent = string.IsNullOrEmpty(parentId) ? "bar" : parentId;

            BookmarkItem item;
            lock (_lock)
            {
                // Check if already bookmarked
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    if (!_bookmarks[i].IsFolder && string.Equals(_bookmarks[i].Url, url, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(title))
                        {
                            _bookmarks[i].Title = title;
                        }
                        if (!string.IsNullOrEmpty(parentId))
                        {
                            _bookmarks[i].ParentId = targetParent;
                        }
                        Save();
                        item = _bookmarks[i];
                        goto FinalizeAdd;
                    }
                }

                item = new BookmarkItem(title, url, targetParent);
                _bookmarks.Add(item);
                _urlIndex.Add(url);
                Save();
            }

        FinalizeAdd:
            if (string.Equals(targetParent, "bar", StringComparison.OrdinalIgnoreCase) && !ShowBookmarksBar)
            {
                ShowBookmarksBar = true;
            }
            OnBookmarksChanged();
            return item;
        }

        public BookmarkItem AddFolder(string title, string parentId = null)
        {
            string targetParent = string.IsNullOrEmpty(parentId) ? "bar" : parentId;
            BookmarkItem folder = BookmarkItem.CreateFolder(title, targetParent);

            lock (_lock)
            {
                _bookmarks.Add(folder);
                Save();
            }

            if (string.Equals(targetParent, "bar", StringComparison.OrdinalIgnoreCase) && !ShowBookmarksBar)
            {
                ShowBookmarksBar = true;
            }
            OnBookmarksChanged();
            return folder;
        }

        public class BookmarkImportBatchResult
        {
            public int BookmarksImported { get; set; }
            public int FoldersImported { get; set; }
            public int SkippedDuplicates { get; set; }
        }

        public BookmarkImportBatchResult ImportBatch(
            List<BookmarkItem> items,
            string targetParentId = null,
            bool skipDuplicates = true)
        {
            var result = new BookmarkImportBatchResult();
            if (items == null || items.Count == 0) return result;

            bool addedAny = false;
            lock (_lock)
            {
                // 1. Pre-generate new unique IDs for all imported folders to prevent ID collisions
                var folderIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < items.Count; i++)
                {
                    BookmarkItem it = items[i];
                    if (it != null && it.IsFolder && !string.IsNullOrEmpty(it.Id))
                    {
                        folderIdMap[it.Id] = Guid.NewGuid().ToString("N");
                    }
                }

                // 2. Ingest folders first, then bookmarks
                for (int i = 0; i < items.Count; i++)
                {
                    BookmarkItem item = items[i];
                    if (item == null) continue;

                    if (item.IsFolder)
                    {
                        string newId;
                        if (string.IsNullOrEmpty(item.Id) || !folderIdMap.TryGetValue(item.Id, out newId))
                        {
                            newId = Guid.NewGuid().ToString("N");
                        }

                        string parent = item.ParentId;
                        if (!string.IsNullOrEmpty(parent) && folderIdMap.ContainsKey(parent))
                        {
                            parent = folderIdMap[parent];
                        }
                        else if (!string.IsNullOrEmpty(targetParentId))
                        {
                            parent = targetParentId;
                        }
                        else if (string.IsNullOrEmpty(parent))
                        {
                            parent = "bar";
                        }

                        BookmarkItem folder = new BookmarkItem
                        {
                            Id = newId,
                            Title = string.IsNullOrEmpty(item.Title) ? "Folder" : item.Title,
                            Url = string.Empty,
                            CreatedAt = item.CreatedAt == default(DateTime) ? DateTime.Now : item.CreatedAt,
                            ParentId = parent,
                            IsFolder = true
                        };

                        _bookmarks.Add(folder);
                        result.FoldersImported++;
                        addedAny = true;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(item.Url)) continue;

                        if (skipDuplicates && _urlIndex.Contains(item.Url))
                        {
                            result.SkippedDuplicates++;
                            continue;
                        }

                        string parent = item.ParentId;
                        if (!string.IsNullOrEmpty(parent) && folderIdMap.ContainsKey(parent))
                        {
                            parent = folderIdMap[parent];
                        }
                        else if (!string.IsNullOrEmpty(targetParentId))
                        {
                            parent = targetParentId;
                        }
                        else if (string.IsNullOrEmpty(parent))
                        {
                            parent = "bar";
                        }

                        BookmarkItem bookmark = new BookmarkItem
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Title = string.IsNullOrEmpty(item.Title) ? item.Url : item.Title,
                            Url = item.Url,
                            CreatedAt = item.CreatedAt == default(DateTime) ? DateTime.Now : item.CreatedAt,
                            ParentId = parent,
                            IsFolder = false
                        };

                        _bookmarks.Add(bookmark);
                        _urlIndex.Add(bookmark.Url);
                        result.BookmarksImported++;
                        addedAny = true;
                    }
                }

                if (addedAny)
                {
                    Save();
                }
            }

            if (addedAny)
            {
                if (!ShowBookmarksBar && (string.IsNullOrEmpty(targetParentId) || string.Equals(targetParentId, "bar", StringComparison.OrdinalIgnoreCase)))
                {
                    ShowBookmarksBar = true;
                }
                OnBookmarksChanged();
            }

            return result;
        }

        public bool Update(string id, string newTitle, string newUrl)
        {
            return Update(id, newTitle, newUrl, null);
        }

        public bool Update(string id, string newTitle, string newUrl, string newParentId)
        {
            if (string.IsNullOrEmpty(id)) return false;

            bool updated = false;
            lock (_lock)
            {
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    BookmarkItem item = _bookmarks[i];
                    if (item != null && string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        if (item.IsFolder)
                        {
                            if (!string.IsNullOrEmpty(newTitle))
                            {
                                item.Title = newTitle;
                            }
                            if (!string.IsNullOrEmpty(newParentId))
                            {
                                item.ParentId = newParentId;
                            }
                            updated = true;
                            Save();
                            break;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(item.Url))
                            {
                                _urlIndex.Remove(item.Url);
                            }
                            item.Title = string.IsNullOrEmpty(newTitle) ? (newUrl ?? item.Title) : newTitle;
                            if (!string.IsNullOrEmpty(newUrl))
                            {
                                item.Url = newUrl;
                                _urlIndex.Add(newUrl);
                            }
                            if (!string.IsNullOrEmpty(newParentId))
                            {
                                item.ParentId = newParentId;
                            }
                            updated = true;
                            Save();
                            break;
                        }
                    }
                }
            }

            if (updated)
            {
                OnBookmarksChanged();
            }
            return updated;
        }

        public bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            bool removed = false;
            lock (_lock)
            {
                HashSet<string> idsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                idsToRemove.Add(id);

                // Find all descendant items recursively
                bool addedAny;
                do
                {
                    addedAny = false;
                    for (int i = 0; i < _bookmarks.Count; i++)
                    {
                        BookmarkItem item = _bookmarks[i];
                        if (item != null && !string.IsNullOrEmpty(item.ParentId) && idsToRemove.Contains(item.ParentId))
                        {
                            if (idsToRemove.Add(item.Id))
                            {
                                addedAny = true;
                            }
                        }
                    }
                } while (addedAny);

                for (int i = _bookmarks.Count - 1; i >= 0; i--)
                {
                    BookmarkItem item = _bookmarks[i];
                    if (item != null && idsToRemove.Contains(item.Id))
                    {
                        if (!string.IsNullOrEmpty(item.Url))
                        {
                            _urlIndex.Remove(item.Url);
                        }
                        _bookmarks.RemoveAt(i);
                        removed = true;
                    }
                }

                if (removed)
                {
                    Save();
                }
            }

            if (removed)
            {
                OnBookmarksChanged();
            }
            return removed;
        }

        public bool RemoveByUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            bool removed = false;
            lock (_lock)
            {
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    if (!_bookmarks[i].IsFolder && string.Equals(_bookmarks[i].Url, url, StringComparison.OrdinalIgnoreCase))
                    {
                        _urlIndex.Remove(_bookmarks[i].Url);
                        _bookmarks.RemoveAt(i);
                        removed = true;
                        Save();
                        break;
                    }
                }
            }

            if (removed)
            {
                OnBookmarksChanged();
            }
            return removed;
        }

        public List<BookmarkItem> GetBarItems(string parentId = null)
        {
            string target = string.IsNullOrEmpty(parentId) ? "bar" : parentId;
            lock (_lock)
            {
                if (_cachedBarItemsByFolder == null)
                {
                    _cachedBarItemsByFolder = new Dictionary<string, List<BookmarkItem>>(StringComparer.OrdinalIgnoreCase);
                }

                List<BookmarkItem> cached;
                if (_cachedBarItemsByFolder.TryGetValue(target, out cached) && cached != null)
                {
                    return new List<BookmarkItem>(cached);
                }

                List<BookmarkItem> results = new List<BookmarkItem>();
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    BookmarkItem item = _bookmarks[i];
                    if (item == null) continue;

                    if (string.Equals(target, "bar", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(item.ParentId) || string.Equals(item.ParentId, "bar", StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(item);
                        }
                    }
                    else
                    {
                        if (string.Equals(item.ParentId, target, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(item);
                        }
                    }
                }

                _cachedBarItemsByFolder[target] = results;
                return new List<BookmarkItem>(results);
            }
        }

        public List<BookmarkItem> GetOtherBookmarks()
        {
            return GetBarItems("other");
        }

        public List<BookmarkItem> GetMobileBookmarks()
        {
            return GetBarItems("mobile");
        }

        public List<BookmarkItem> GetFolders()
        {
            lock (_lock)
            {
                if (_cachedFolders != null)
                {
                    return new List<BookmarkItem>(_cachedFolders);
                }

                List<BookmarkItem> folders = new List<BookmarkItem>();
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    if (_bookmarks[i] != null && _bookmarks[i].IsFolder)
                    {
                        folders.Add(_bookmarks[i]);
                    }
                }
                _cachedFolders = folders;
                return new List<BookmarkItem>(folders);
            }
        }

        public string GetFolderName(string parentId)
        {
            if (string.IsNullOrEmpty(parentId) || string.Equals(parentId, "bar", StringComparison.OrdinalIgnoreCase))
            {
                return AppStrings.Bookmarks.FolderBookmarksBar;
            }
            if (string.Equals(parentId, "other", StringComparison.OrdinalIgnoreCase))
            {
                return AppStrings.Bookmarks.FolderOther;
            }
            if (string.Equals(parentId, "mobile", StringComparison.OrdinalIgnoreCase))
            {
                return AppStrings.Bookmarks.FolderMobile;
            }
            lock (_lock)
            {
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    if (_bookmarks[i] != null && string.Equals(_bookmarks[i].Id, parentId, StringComparison.OrdinalIgnoreCase))
                    {
                        return _bookmarks[i].Title ?? AppStrings.Bookmarks.FolderBookmarksBar;
                    }
                }
            }
            return AppStrings.Bookmarks.FolderBookmarksBar;
        }

        public int GetChildCount(string folderId)
        {
            if (string.IsNullOrEmpty(folderId)) return 0;
            int count = 0;
            lock (_lock)
            {
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    BookmarkItem item = _bookmarks[i];
                    if (item != null && string.Equals(item.ParentId, folderId, StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        public List<BookmarkItem> GetAllChildren(string folderId)
        {
            List<BookmarkItem> list = new List<BookmarkItem>();
            if (string.IsNullOrEmpty(folderId)) return list;
            lock (_lock)
            {
                CollectChildrenRecursive(folderId, list);
            }
            return list;
        }

        private void CollectChildrenRecursive(string parentId, List<BookmarkItem> list)
        {
            for (int i = 0; i < _bookmarks.Count; i++)
            {
                BookmarkItem item = _bookmarks[i];
                if (item == null) continue;

                bool isMatch = false;
                if (string.Equals(parentId, "bar", StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = string.IsNullOrEmpty(item.ParentId) || string.Equals(item.ParentId, "bar", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    isMatch = string.Equals(item.ParentId, parentId, StringComparison.OrdinalIgnoreCase);
                }

                if (isMatch)
                {
                    if (item.IsFolder)
                    {
                        CollectChildrenRecursive(item.Id, list);
                    }
                    else if (!string.IsNullOrEmpty(item.Url))
                    {
                        list.Add(item);
                    }
                }
            }
        }

        public bool IsDescendantOf(string candidateDescendantId, string ancestorId)
        {
            if (string.IsNullOrEmpty(candidateDescendantId) || string.IsNullOrEmpty(ancestorId)) return false;

            string current = candidateDescendantId;
            while (!string.IsNullOrEmpty(current) &&
                   !string.Equals(current, "bar", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(current, "other", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(current, "mobile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(current, ancestorId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                BookmarkItem item = null;
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    if (_bookmarks[i] != null && string.Equals(_bookmarks[i].Id, current, StringComparison.OrdinalIgnoreCase))
                    {
                        item = _bookmarks[i];
                        break;
                    }
                }
                if (item == null) break;
                current = item.ParentId;
            }
            return false;
        }

        public bool MoveItem(string id, int targetIndex, string targetParentId = null)
        {
            if (string.IsNullOrEmpty(id)) return false;

            bool moved = false;
            lock (_lock)
            {
                int currentIndex = -1;
                BookmarkItem targetItem = null;
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    if (_bookmarks[i] != null && string.Equals(_bookmarks[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        currentIndex = i;
                        targetItem = _bookmarks[i];
                        break;
                    }
                }

                if (targetItem != null)
                {
                    if (targetParentId != null)
                    {
                        if (string.Equals(id, targetParentId, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                        if (targetItem.IsFolder && IsDescendantOf(targetParentId, id))
                        {
                            return false;
                        }
                        targetItem.ParentId = targetParentId;
                    }

                    if (targetIndex >= 0 && currentIndex >= 0)
                    {
                        _bookmarks.RemoveAt(currentIndex);

                        int insertIndex = targetIndex;
                        if (currentIndex < targetIndex)
                        {
                            insertIndex = targetIndex - 1;
                        }

                        if (insertIndex < 0) insertIndex = 0;
                        if (insertIndex > _bookmarks.Count) insertIndex = _bookmarks.Count;

                        _bookmarks.Insert(insertIndex, targetItem);
                    }
                    moved = true;
                    Save();
                }
            }

            if (moved)
            {
                if (string.Equals(targetParentId, "bar", StringComparison.OrdinalIgnoreCase) && !ShowBookmarksBar)
                {
                    ShowBookmarksBar = true;
                }
                OnBookmarksChanged();
            }
            return moved;
        }

        public bool MoveItemInFolder(string id, int direction)
        {
            if (string.IsNullOrEmpty(id) || direction == 0) return false;

            bool moved = false;
            lock (_lock)
            {
                BookmarkItem targetItem = null;
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    if (_bookmarks[i] != null && string.Equals(_bookmarks[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        targetItem = _bookmarks[i];
                        break;
                    }
                }

                if (targetItem == null) return false;

                string parent = string.IsNullOrEmpty(targetItem.ParentId) ? "bar" : targetItem.ParentId;

                List<int> siblingIndices = new List<int>();
                int mySiblingPos = -1;
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    BookmarkItem it = _bookmarks[i];
                    if (it == null) continue;
                    string itParent = string.IsNullOrEmpty(it.ParentId) ? "bar" : it.ParentId;
                    if (string.Equals(itParent, parent, StringComparison.OrdinalIgnoreCase))
                    {
                        if (it == targetItem)
                        {
                            mySiblingPos = siblingIndices.Count;
                        }
                        siblingIndices.Add(i);
                    }
                }

                if (mySiblingPos < 0) return false;

                int targetSiblingPos = mySiblingPos + direction;
                if (targetSiblingPos < 0 || targetSiblingPos >= siblingIndices.Count) return false;

                int fromIndex = siblingIndices[mySiblingPos];
                int toIndex = siblingIndices[targetSiblingPos];

                BookmarkItem temp = _bookmarks[fromIndex];
                _bookmarks[fromIndex] = _bookmarks[toIndex];
                _bookmarks[toIndex] = temp;

                moved = true;
                Save();
            }

            if (moved)
            {
                OnBookmarksChanged();
            }
            return moved;
        }

        /// <summary>
        /// Moves an item relative to a sibling item (before or after), optionally changing folder.
        /// If relativeToId is null, moves to the end of targetParentId.
        /// </summary>
        public bool MoveItemRelative(string id, string relativeToId, bool insertAfter = false, string targetParentId = null)
        {
            if (string.IsNullOrEmpty(id)) return false;

            bool moved = false;
            lock (_lock)
            {
                int currentIndex = -1;
                BookmarkItem targetItem = null;
                for (int i = 0; i < _bookmarks.Count; i++)
                {
                    if (_bookmarks[i] != null && string.Equals(_bookmarks[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        currentIndex = i;
                        targetItem = _bookmarks[i];
                        break;
                    }
                }

                if (targetItem == null) return false;

                string newParent = targetParentId;
                int refIndex = -1;
                if (!string.IsNullOrEmpty(relativeToId) && !string.Equals(id, relativeToId, StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < _bookmarks.Count; i++)
                    {
                        if (_bookmarks[i] != null && string.Equals(_bookmarks[i].Id, relativeToId, StringComparison.OrdinalIgnoreCase))
                        {
                            refIndex = i;
                            if (newParent == null)
                            {
                                newParent = _bookmarks[i].ParentId;
                            }
                            break;
                        }
                    }
                }

                if (newParent != null)
                {
                    if (string.Equals(id, newParent, StringComparison.OrdinalIgnoreCase)) return false;
                    if (targetItem.IsFolder && IsDescendantOf(newParent, id)) return false;
                    targetItem.ParentId = newParent;
                }

                if (refIndex >= 0)
                {
                    _bookmarks.RemoveAt(currentIndex);
                    if (currentIndex < refIndex)
                    {
                        refIndex--;
                    }

                    int insertIndex = insertAfter ? (refIndex + 1) : refIndex;
                    if (insertIndex < 0) insertIndex = 0;
                    if (insertIndex > _bookmarks.Count) insertIndex = _bookmarks.Count;

                    _bookmarks.Insert(insertIndex, targetItem);
                    moved = true;
                    Save();
                }
                else if (newParent != null)
                {
                    _bookmarks.RemoveAt(currentIndex);

                    int lastSibling = -1;
                    for (int i = 0; i < _bookmarks.Count; i++)
                    {
                        BookmarkItem it = _bookmarks[i];
                        if (it != null)
                        {
                            string itP = string.IsNullOrEmpty(it.ParentId) ? "bar" : it.ParentId;
                            string targetP = string.IsNullOrEmpty(newParent) ? "bar" : newParent;
                            if (string.Equals(itP, targetP, StringComparison.OrdinalIgnoreCase))
                            {
                                lastSibling = i;
                            }
                        }
                    }

                    int insertIndex = (lastSibling >= 0) ? (lastSibling + 1) : _bookmarks.Count;
                    if (insertIndex < 0) insertIndex = 0;
                    if (insertIndex > _bookmarks.Count) insertIndex = _bookmarks.Count;

                    _bookmarks.Insert(insertIndex, targetItem);
                    moved = true;
                    Save();
                }
            }

            if (moved)
            {
                OnBookmarksChanged();
            }
            return moved;
        }

        public List<BookmarkItem> FindMatches(string query, int maxCount)
        {
            var results = new List<BookmarkItem>();
            if (string.IsNullOrEmpty(query)) return results;

            lock (_lock)
            {
                for (int i = 0; i < _bookmarks.Count && results.Count < maxCount; i++)
                {
                    BookmarkItem item = _bookmarks[i];
                    if (item == null) continue;

                    bool matchTitle = !string.IsNullOrEmpty(item.Title) && item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchUrl = !item.IsFolder && !string.IsNullOrEmpty(item.Url) && item.Url.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (matchTitle || matchUrl)
                    {
                        results.Add(item);
                    }
                }
            }
            return results;
        }

        public List<BookmarkItem> GetSorted(string query, SortMode sortMode)
        {
            if (string.IsNullOrEmpty(query))
            {
                lock (_lock)
                {
                    if (_cachedSorted != null && _cachedSorted.ContainsKey(sortMode))
                    {
                        return new List<BookmarkItem>(_cachedSorted[sortMode]);
                    }
                }
            }

            List<BookmarkItem> list = GetAll();

            // Filter without temporary string allocations
            if (!string.IsNullOrEmpty(query))
            {
                List<BookmarkItem> filtered = new List<BookmarkItem>();
                for (int i = 0; i < list.Count; i++)
                {
                    BookmarkItem item = list[i];
                    if (item == null) continue;
                    if ((item.Title != null && item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!item.IsFolder && item.Url != null && item.Url.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        filtered.Add(item);
                    }
                }
                list = filtered;
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
                case SortMode.FolderAscending:
                    list.Sort((a, b) =>
                    {
                        string fA = GetFolderName(a.ParentId);
                        string fB = GetFolderName(b.ParentId);
                        int cmp = string.Compare(fA, fB, StringComparison.OrdinalIgnoreCase);
                        return cmp != 0 ? cmp : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
                case SortMode.FolderDescending:
                    list.Sort((a, b) =>
                    {
                        string fA = GetFolderName(a.ParentId);
                        string fB = GetFolderName(b.ParentId);
                        int cmp = string.Compare(fB, fA, StringComparison.OrdinalIgnoreCase);
                        return cmp != 0 ? cmp : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
                case SortMode.TypeAscending:
                    list.Sort((a, b) =>
                    {
                        if (a.IsFolder != b.IsFolder)
                        {
                            return a.IsFolder ? -1 : 1;
                        }
                        return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
                case SortMode.TypeDescending:
                    list.Sort((a, b) =>
                    {
                        if (a.IsFolder != b.IsFolder)
                        {
                            return b.IsFolder ? -1 : 1;
                        }
                        return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
                case SortMode.DateAscending:
                    list.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
                    break;
                case SortMode.DateDescending:
                    list.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
                    break;
                default:
                    // By default, folder always on top followed by bookmarks
                    list.Sort((a, b) =>
                    {
                        if (a.IsFolder != b.IsFolder)
                        {
                            return a.IsFolder ? -1 : 1;
                        }
                        return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
            }

            if (string.IsNullOrEmpty(query))
            {
                lock (_lock)
                {
                    if (_cachedSorted == null)
                    {
                        _cachedSorted = new Dictionary<SortMode, List<BookmarkItem>>();
                    }
                    _cachedSorted[sortMode] = new List<BookmarkItem>(list);
                }
            }

            return list;
        }

        private void Save()
        {
            if (AppServices.Database == null) return;
            try
            {
                AppServices.Database.InTransaction(() =>
                {
                    AppServices.Database.ExecuteNonQuery("DELETE FROM bookmarks;");
                    for (int i = 0; i < _bookmarks.Count; i++)
                    {
                        BookmarkItem item = _bookmarks[i];
                        if (item == null) continue;
                        AppServices.Database.ExecuteNonQuery(
                            "INSERT INTO bookmarks (id, title, url, created_at, parent_id, is_folder, position) VALUES (?, ?, ?, ?, ?, ?, ?);",
                            item.Id,
                            item.Title ?? string.Empty,
                            item.Url ?? string.Empty,
                            item.CreatedAt.Ticks,
                            item.ParentId ?? "bar",
                            item.IsFolder ? 1 : 0,
                            i);
                    }
                });
            }
            catch { }
        }

        private void OnBookmarksChanged()
        {
            lock (_lock)
            {
                InvalidateCaches();
            }
            EventHandler handler = BookmarksChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void OnBookmarksBarVisibilityChanged()
        {
            EventHandler handler = BookmarksBarVisibilityChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
