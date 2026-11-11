using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using wb.Core;
using wb.AdBlock;

namespace wb.Downloads
{
    public class DownloadManager
    {
        private const string FileName = "downloads.json";
        private readonly List<DownloadItemModel> _downloads;
        private readonly object _lock = new object();

        // High-performance lookup & caching
        private readonly Dictionary<long, DownloadItemModel> _idLookup = new Dictionary<long, DownloadItemModel>();
        private List<DownloadItemModel> _cachedDefaultSorted = null;

        private void InvalidateCache()
        {
            _cachedDefaultSorted = null;
        }

        private void RebuildIdLookup()
        {
            _idLookup.Clear();
            for (int i = 0; i < _downloads.Count; i++)
            {
                if (_downloads[i] != null)
                {
                    _idLookup[_downloads[i].Id] = _downloads[i];
                }
            }
        }

        private readonly Dictionary<long, Action> _cancelCallbacks = new Dictionary<long, Action>();
        private readonly Dictionary<long, Action> _pauseCallbacks = new Dictionary<long, Action>();
        private readonly Dictionary<long, Action> _resumeCallbacks = new Dictionary<long, Action>();

        private readonly Queue<long> _queue = new Queue<long>();
        private bool _isQueueWorkerRunning = false;

        public event EventHandler<DownloadItemModel> DownloadStarted;
        public event EventHandler<DownloadItemModel> DownloadUpdated;
        public event EventHandler<DownloadItemModel> DownloadCompleted;
        public event EventHandler<int> ActiveDownloadsCountChanged;
        public event EventHandler DownloadsCleared;

        private int _lastActiveCount = -1;
        private DateTime _lastProgressDiskSave = DateTime.MinValue;

        public DownloadManager()
        {
            _downloads = new List<DownloadItemModel>();
            LoadFromDatabaseOrMigrate();
            ReconcileColdLaunchState();
            _lastActiveCount = GetActiveCount();
        }

        private void LoadFromDatabaseOrMigrate()
        {
            try
            {
                if (AppServices.Database != null)
                {
                    AppServices.Database.Query(
                        "SELECT id, url, full_path, file_name, total_bytes, received_bytes, state, start_time, end_time, mime_type, error_message FROM downloads ORDER BY start_time DESC;",
                        stmt =>
                        {
                            long idVal = SqliteDatabase.GetInt64(stmt, 0);
                            if (idVal == 0)
                            {
                                long.TryParse(SqliteDatabase.GetString(stmt, 0), out idVal);
                            }
                            var item = new DownloadItemModel
                            {
                                Id = idVal,
                                Url = SqliteDatabase.GetString(stmt, 1),
                                FilePath = SqliteDatabase.GetString(stmt, 2),
                                FileName = SqliteDatabase.GetString(stmt, 3),
                                TotalBytes = SqliteDatabase.GetInt64(stmt, 4),
                                ReceivedBytes = SqliteDatabase.GetInt64(stmt, 5),
                                State = SqliteDatabase.GetString(stmt, 6),
                                StartTime = SqliteDatabase.GetDateTime(stmt, 7),
                                EndTime = SqliteDatabase.GetNullableDateTime(stmt, 8),
                                FailureReason = SqliteDatabase.GetString(stmt, 10)
                            };
                            if (item.TotalBytes > 0)
                            {
                                item.Percent = (int)Math.Min(100, Math.Max(0, (item.ReceivedBytes * 100) / item.TotalBytes));
                            }
                            _downloads.Add(item);
                        });
                }

                string configPath = System.IO.Path.Combine(StorageHelper.GetConfigDirectory(), FileName);
                string legacyPath = System.IO.Path.Combine(StorageHelper.GetDataDirectory(), FileName);
                string jsonPath = System.IO.File.Exists(configPath) ? configPath : (System.IO.File.Exists(legacyPath) ? legacyPath : null);
                if (jsonPath != null)
                {
                    var legacy = StorageHelper.LoadList<DownloadItemModel>(FileName);
                    if (legacy != null && legacy.Count > 0)
                    {
                        HashSet<long> existingIds = new HashSet<long>();
                        for (int i = 0; i < _downloads.Count; i++)
                        {
                            if (_downloads[i] != null) existingIds.Add(_downloads[i].Id);
                        }

                        bool anyAdded = false;
                        for (int i = 0; i < legacy.Count; i++)
                        {
                            var leg = legacy[i];
                            if (leg != null && !existingIds.Contains(leg.Id))
                            {
                                _downloads.Add(leg);
                                existingIds.Add(leg.Id);
                                anyAdded = true;
                            }
                        }
                        if (anyAdded)
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
            }
            catch { }

            lock (_lock)
            {
                RebuildIdLookup();
                InvalidateCache();
            }
        }

        public void ReconcileColdLaunchState()
        {
            lock (_lock)
            {
                bool changed = false;
                for (int i = 0; i < _downloads.Count; i++)
                {
                    DownloadItemModel item = _downloads[i];
                    string state = item.State;
                    if (string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase))
                    {
                        // Abnormal shutdown or crash while download was active
                        item.State = "Interrupted";
                        item.CurrentSpeed = 0;
                        changed = true;
                    }

                    // Synchronize with physical .wbdownload container on disk if present
                    if (item.CheckWbDownloadExists())
                    {
                        long diskBytes = item.GetWbDownloadSize();
                        if (diskBytes > 0 && diskBytes != item.ReceivedBytes)
                        {
                            item.ReceivedBytes = diskBytes;
                            if (item.TotalBytes > 0)
                            {
                                item.Percent = (int)Math.Min(99, (diskBytes * 100) / item.TotalBytes);
                            }
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    InvalidateCache();
                    Save();
                }
            }
        }

        public void PauseAllForExit()
        {
            lock (_lock)
            {
                foreach (var kvp in _pauseCallbacks)
                {
                    try { kvp.Value(); } catch { }
                }

                for (int i = 0; i < _downloads.Count; i++)
                {
                    DownloadItemModel item = _downloads[i];
                    string state = item.State;
                    if (string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase))
                    {
                        item.State = "Paused";
                        item.CurrentSpeed = 0;
                        if (item.CheckWbDownloadExists())
                        {
                            long diskBytes = item.GetWbDownloadSize();
                            if (diskBytes > 0) item.ReceivedBytes = diskBytes;
                        }
                    }
                }
                Save();
            }
            CheckAndNotifyActiveCount();
        }

        public bool CanResumeDownload(DownloadItemModel item, out string reason)
        {
            reason = null;
            if (item == null)
            {
                reason = "Download item is null.";
                return false;
            }

            if (!string.IsNullOrEmpty(item.FilePath))
            {
                try
                {
                    string root = Path.GetPathRoot(Path.GetFullPath(item.FilePath));
                    if (!string.IsNullOrEmpty(root))
                    {
                        DriveInfo drive = new DriveInfo(root);
                        if (drive.IsReady)
                        {
                            long remaining = item.TotalBytes > 0 ? (item.TotalBytes - item.ReceivedBytes) : 10 * 1024 * 1024;
                            if (drive.AvailableFreeSpace < remaining)
                            {
                                reason = AppStrings.Downloads.DiskFullResumeBlocked;
                                return false;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            return true;
        }

        public static string GetUniqueFilePath(string folder, string fileName)
        {
            if (string.IsNullOrEmpty(folder))
            {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(folder))
                {
                    folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
            }
            if (!Directory.Exists(folder))
            {
                try { Directory.CreateDirectory(folder); } catch { }
            }

            if (string.IsNullOrEmpty(fileName))
            {
                fileName = AppStrings.Downloads.DefaultDownloadName;
            }

            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            string targetPath = Path.Combine(folder, fileName);

            int counter = 1;
            while (File.Exists(targetPath) || File.Exists(targetPath + DownloadItemModel.WbDownloadExtension))
            {
                targetPath = Path.Combine(folder, string.Format("{0} ({1}){2}", nameWithoutExt, counter, ext));
                counter++;
            }
            return targetPath;
        }

        public int GetActiveCount()
        {
            lock (_lock)
            {
                int count = 0;
                for (int i = 0; i < _downloads.Count; i++)
                {
                    string state = _downloads[i].State;
                    if (string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        private void CheckAndNotifyActiveCount()
        {
            int current = GetActiveCount();
            if (current != _lastActiveCount)
            {
                _lastActiveCount = current;
                EventHandler<int> handler = ActiveDownloadsCountChanged;
                if (handler != null)
                {
                    try
                    {
                        handler(this, current);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public List<DownloadItemModel> GetAll()
        {
            lock (_lock)
            {
                return new List<DownloadItemModel>(_downloads);
            }
        }

        public DownloadItemModel GetItem(long id)
        {
            lock (_lock)
            {
                DownloadItemModel item;
                if (_idLookup.TryGetValue(id, out item))
                {
                    return item;
                }
            }
            return null;
        }

        public DownloadItemModel RegisterDownload(long id, string fileName, string filePath, string url, long totalBytes)
        {
            DownloadItemModel model;
            lock (_lock)
            {
                model = new DownloadItemModel
                {
                    Id = id,
                    FileName = fileName,
                    FilePath = filePath,
                    Url = url,
                    TotalBytes = totalBytes,
                    ReceivedBytes = 0,
                    Percent = 0,
                    State = "InProgress",
                    StartTime = DateTime.Now
                };
                _downloads.Insert(0, model);
                _idLookup[id] = model;
                InvalidateCache();
                Save();
            }

            CheckAndNotifyActiveCount();

            EventHandler<DownloadItemModel> handler = DownloadStarted;
            if (handler != null)
            {
                handler(this, model);
            }

            return model;
        }

        public void RegisterCallbacks(long id, Action cancelAction, Action pauseAction, Action resumeAction)
        {
            lock (_lock)
            {
                if (cancelAction != null) _cancelCallbacks[id] = cancelAction;
                if (pauseAction != null) _pauseCallbacks[id] = pauseAction;
                if (resumeAction != null) _resumeCallbacks[id] = resumeAction;
            }
        }

        public void UpdateProgress(long id, long receivedBytes, long totalBytes, int percent, string state, double speed = 0)
        {
            DownloadItemModel item = null;
            bool newlyCompleted = false;

            lock (_lock)
            {
                if (_idLookup.TryGetValue(id, out item) && item != null)
                {
                    item.ReceivedBytes = receivedBytes;
                    if (totalBytes > 0)
                    {
                        item.TotalBytes = totalBytes;
                    }
                    item.Percent = percent;
                    item.CurrentSpeed = speed;

                    string oldState = item.State;
                    item.State = state;

                    bool stateChanged = !string.Equals(oldState, state, StringComparison.OrdinalIgnoreCase);

                    if (string.Equals(state, "Completed", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(oldState, "Completed", StringComparison.OrdinalIgnoreCase))
                    {
                        item.EndTime = DateTime.Now;
                        newlyCompleted = true;
                    }

                    if (stateChanged || newlyCompleted)
                    {
                        InvalidateCache();
                    }

                    DateTime now = DateTime.UtcNow;
                    if (stateChanged || newlyCompleted || (now - _lastProgressDiskSave).TotalSeconds >= 5.0)
                    {
                        _lastProgressDiskSave = now;
                        Save();
                    }
                }
            }

            CheckAndNotifyActiveCount();

            if (item != null)
            {
                EventHandler<DownloadItemModel> updateHandler = DownloadUpdated;
                if (updateHandler != null)
                {
                    updateHandler(this, item);
                }

                if (newlyCompleted)
                {
                    EventHandler<DownloadItemModel> completeHandler = DownloadCompleted;
                    if (completeHandler != null)
                    {
                        completeHandler(this, item);
                    }
                }
            }
        }

        public DownloadItemModel QueueDownload(string url, string targetPath = null)
        {
            if (string.IsNullOrEmpty(url)) return null;

            long id = DateTime.UtcNow.Ticks;
            string fileName = "download";
            try
            {
                Uri uri = new Uri(url);
                fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = "download_" + id;
                }
            }
            catch
            {
                fileName = "download_" + id;
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(folder))
                {
                    folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
                targetPath = Path.Combine(folder, fileName);
            }

            DownloadItemModel model;
            lock (_lock)
            {
                model = new DownloadItemModel
                {
                    Id = id,
                    FileName = fileName,
                    FilePath = targetPath,
                    Url = url,
                    TotalBytes = 0,
                    ReceivedBytes = 0,
                    Percent = 0,
                    State = "Queued",
                    StartTime = DateTime.Now
                };
                _downloads.Insert(0, model);
                _idLookup[id] = model;
                InvalidateCache();
                Save();
            }

            CheckAndNotifyActiveCount();

            EventHandler<DownloadItemModel> handler = DownloadStarted;
            if (handler != null)
            {
                handler(this, model);
            }

            lock (_lock)
            {
                _queue.Enqueue(id);
            }
            StartQueueWorker();

            return model;
        }

        public void StartDownload(long id)
        {
            DownloadItemModel item = GetItem(id);
            if (item == null) return;

            lock (_lock)
            {
                item.State = "Queued";
                Save();
                if (!_queue.Contains(id))
                {
                    _queue.Enqueue(id);
                }
            }

            CheckAndNotifyActiveCount();

            EventHandler<DownloadItemModel> handler = DownloadUpdated;
            if (handler != null)
            {
                handler(this, item);
            }

            StartQueueWorker();
        }

        public void PauseDownload(long id)
        {
            Action pauseAction = null;
            lock (_lock)
            {
                if (_pauseCallbacks.ContainsKey(id))
                {
                    pauseAction = _pauseCallbacks[id];
                }
            }

            if (pauseAction != null)
            {
                try { pauseAction(); } catch { }
            }

            DownloadItemModel item = GetItem(id);
            if (item != null)
            {
                item.State = "Paused";
                item.CurrentSpeed = 0;
                Save();

                CheckAndNotifyActiveCount();

                EventHandler<DownloadItemModel> handler = DownloadUpdated;
                if (handler != null)
                {
                    handler(this, item);
                }
            }
        }

        public void ResumeDownload(long id)
        {
            Action resumeAction = null;
            lock (_lock)
            {
                if (_resumeCallbacks.ContainsKey(id))
                {
                    resumeAction = _resumeCallbacks[id];
                }
            }

            if (resumeAction != null)
            {
                try { resumeAction(); } catch { }
            }
            else
            {
                DownloadItemModel item = GetItem(id);
                if (item != null)
                {
                    string blockReason;
                    if (!CanResumeDownload(item, out blockReason))
                    {
                        item.State = "Failed - Disk full";
                        item.FailureReason = "DiskFull";
                        Save();
                        EventHandler<DownloadItemModel> updateHandler = DownloadUpdated;
                        if (updateHandler != null) updateHandler(this, item);
                        return;
                    }
                }
                StartDownload(id);
            }
        }

        public void CancelDownload(long id)
        {
            Action cancelAction = null;
            lock (_lock)
            {
                if (_cancelCallbacks.ContainsKey(id))
                {
                    cancelAction = _cancelCallbacks[id];
                }
            }

            if (cancelAction != null)
            {
                try { cancelAction(); } catch { }
            }

            DownloadItemModel item = GetItem(id);
            if (item != null)
            {
                item.State = "Cancelled";
                item.CurrentSpeed = 0;
                Save();

                // Requirement: if download entry cancelled, delete wbdownload file
                DeleteWbDownloadFile(item);

                CheckAndNotifyActiveCount();

                EventHandler<DownloadItemModel> handler = DownloadUpdated;
                if (handler != null)
                {
                    handler(this, item);
                }
            }
        }

        private void DeleteWbDownloadFile(DownloadItemModel item)
        {
            if (item == null) return;
            string wbPath = item.WbDownloadPath;
            if (string.IsNullOrEmpty(wbPath)) return;

            try
            {
                if (File.Exists(wbPath))
                {
                    File.Delete(wbPath);
                }
            }
            catch (IOException)
            {
                // In case file handle is still releasing asynchronously, retry with backoff
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    for (int retry = 0; retry < 20; retry++)
                    {
                        try
                        {
                            System.Threading.Thread.Sleep(100);
                            if (File.Exists(wbPath))
                            {
                                File.Delete(wbPath);
                            }
                            break;
                        }
                        catch (IOException) { }
                        catch { break; }
                    }
                });
            }
            catch { }
        }

        public void RetryDownload(long id)
        {
            DownloadItemModel item = GetItem(id);
            if (item == null) return;

            lock (_lock)
            {
                item.ReceivedBytes = 0;
                item.Percent = 0;
                item.CurrentSpeed = 0;
                item.State = "Queued";
                item.StartTime = DateTime.Now;
                item.EndTime = null;
                Save();

                if (!_queue.Contains(id))
                {
                    _queue.Enqueue(id);
                }
            }

            CheckAndNotifyActiveCount();

            EventHandler<DownloadItemModel> handler = DownloadUpdated;
            if (handler != null)
            {
                handler(this, item);
            }

            StartQueueWorker();
        }

        public int CheckMissingFiles()
        {
            int missingCount = 0;
            lock (_lock)
            {
                for (int i = 0; i < _downloads.Count; i++)
                {
                    DownloadItemModel item = _downloads[i];
                    bool exists = item.CheckFileExists();

                    if (string.Equals(item.State, "Completed", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!exists)
                        {
                            item.State = "Missing (Deleted)";
                            missingCount++;
                        }
                    }
                    else if (string.Equals(item.State, "Missing (Deleted)", StringComparison.OrdinalIgnoreCase))
                    {
                        if (exists)
                        {
                            item.State = "Completed";
                        }
                        else
                        {
                            missingCount++;
                        }
                    }
                }
                Save();
            }
            return missingCount;
        }

        public void DeleteDownload(long id, bool deleteFromDisk)
        {
            CancelDownload(id);

            DownloadItemModel toRemove = null;
            lock (_lock)
            {
                if (_idLookup.TryGetValue(id, out toRemove))
                {
                    _downloads.Remove(toRemove);
                    _idLookup.Remove(id);
                    InvalidateCache();
                    Save();
                }
            }

            CheckAndNotifyActiveCount();

            if (toRemove != null)
            {
                // Requirement: if download entry removed, always delete wbdownload file
                DeleteWbDownloadFile(toRemove);

                if (deleteFromDisk && toRemove.CheckFileExists())
                {
                    try { File.Delete(toRemove.FilePath); } catch { }
                }
            }
        }

        public int GetCountSince(DateTime cutoff)
        {
            lock (_lock)
            {
                int count = 0;
                for (int i = 0; i < _downloads.Count; i++)
                {
                    DownloadItemModel item = _downloads[i];
                    if (item == null) continue;
                    string state = item.State;
                    if (string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (cutoff == DateTime.MinValue || item.StartTime >= cutoff)
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
                List<DownloadItemModel> remaining = new List<DownloadItemModel>();
                for (int i = 0; i < _downloads.Count; i++)
                {
                    DownloadItemModel item = _downloads[i];
                    if (item == null) continue;
                    string state = item.State;
                    if (string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase))
                    {
                        remaining.Add(item);
                        continue;
                    }

                    if (cutoff == DateTime.MinValue || item.StartTime >= cutoff)
                    {
                        DeleteWbDownloadFile(item);
                        removed++;
                    }
                    else
                    {
                        remaining.Add(item);
                    }
                }

                if (removed > 0)
                {
                    _downloads.Clear();
                    _downloads.AddRange(remaining);
                    RebuildIdLookup();
                    InvalidateCache();
                    Save();
                }
            }

            if (removed > 0)
            {
                OnDownloadsCleared();
            }
            return removed;
        }

        public void ClearHistory()
        {
            lock (_lock)
            {
                List<DownloadItemModel> remaining = new List<DownloadItemModel>();
                for (int i = 0; i < _downloads.Count; i++)
                {
                    string state = _downloads[i].State;
                    if (string.Equals(state, "InProgress", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase))
                    {
                        remaining.Add(_downloads[i]);
                    }
                    else
                    {
                        DeleteWbDownloadFile(_downloads[i]);
                    }
                }
                _downloads.Clear();
                _downloads.AddRange(remaining);
                RebuildIdLookup();
                InvalidateCache();
                Save();
            }
            OnDownloadsCleared();
        }

        private void OnDownloadsCleared()
        {
            EventHandler handler = DownloadsCleared;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch { }
            }
        }

        public List<DownloadItemModel> GetSorted(string query, SortMode sortMode)
        {
            if (string.IsNullOrEmpty(query) && (sortMode == SortMode.DateDescending || (int)sortMode == 0))
            {
                lock (_lock)
                {
                    if (_cachedDefaultSorted != null)
                    {
                        return new List<DownloadItemModel>(_cachedDefaultSorted);
                    }
                }
            }

            List<DownloadItemModel> list;
            lock (_lock)
            {
                if (string.IsNullOrEmpty(query))
                {
                    list = new List<DownloadItemModel>(_downloads);
                }
                else
                {
                    list = new List<DownloadItemModel>();
                    for (int i = 0; i < _downloads.Count; i++)
                    {
                        DownloadItemModel item = _downloads[i];
                        if (item != null &&
                            ((item.FileName != null && item.FileName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                             (item.FilePath != null && item.FilePath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                             (item.Url != null && item.Url.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
                        {
                            list.Add(item);
                        }
                    }
                }
            }

            switch (sortMode)
            {
                case SortMode.TitleAscending:
                    list.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortMode.TitleDescending:
                    list.Sort((a, b) => string.Compare(b.FileName, a.FileName, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortMode.DateAscending:
                    list.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
                    break;
                case SortMode.DateDescending:
                default:
                    list.Sort((a, b) => b.StartTime.CompareTo(a.StartTime));
                    break;
            }

            if (string.IsNullOrEmpty(query) && (sortMode == SortMode.DateDescending || (int)sortMode == 0))
            {
                lock (_lock)
                {
                    _cachedDefaultSorted = new List<DownloadItemModel>(list);
                }
            }

            return list;
        }

        private void StartQueueWorker()
        {
            lock (_lock)
            {
                if (_isQueueWorkerRunning) return;
                _isQueueWorkerRunning = true;
            }

            ThreadPool.QueueUserWorkItem(state => RunQueueLoop());
        }

        private void RunQueueLoop()
        {
            while (true)
            {
                long nextId = -1;
                lock (_lock)
                {
                    if (_queue.Count > 0)
                    {
                        nextId = _queue.Dequeue();
                    }
                    else
                    {
                        _isQueueWorkerRunning = false;
                        break;
                    }
                }

                DownloadItemModel item = GetItem(nextId);
                if (item == null || !string.Equals(item.State, "Queued", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ExecuteHttpDownload(item);
            }
        }

        private void ExecuteHttpDownload(DownloadItemModel item)
        {
            item.State = "InProgress";
            item.StartTime = DateTime.Now;
            Save();

            EventHandler<DownloadItemModel> updateHandler = DownloadUpdated;
            if (updateHandler != null) updateHandler(this, item);

            bool cancelRequested = false;
            bool pauseRequested = false;
            HttpWebRequest request = null;

            Action cancelAction = () =>
            {
                cancelRequested = true;
                if (request != null)
                {
                    try { request.Abort(); } catch { }
                }
            };

            Action pauseAction = () =>
            {
                pauseRequested = true;
                if (request != null)
                {
                    try { request.Abort(); } catch { }
                }
            };

            RegisterCallbacks(item.Id, cancelAction, pauseAction, null);

            FileStream fs = null;
            Stream responseStream = null;
            HttpWebResponse response = null;

            try
            {
                AdBlockManager.EnsureSecurityProtocol();
                request = (HttpWebRequest)WebRequest.Create(item.Url);
                request.UserAgent = AppStrings.Downloads.DownloadUserAgent;
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;

                string wbPath = item.WbDownloadPath ?? (item.FilePath + DownloadItemModel.WbDownloadExtension);
                string dir = Path.GetDirectoryName(wbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                long existingBytes = 0;
                if (File.Exists(wbPath))
                {
                    try { existingBytes = new FileInfo(wbPath).Length; } catch { }
                }

                if (existingBytes > 0)
                {
                    request.AddRange(existingBytes);
                }

                response = (HttpWebResponse)request.GetResponse();

                bool isPartial = response.StatusCode == HttpStatusCode.PartialContent;
                if (isPartial)
                {
                    // Append to existing .wbdownload container
                    fs = new FileStream(wbPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    if (response.ContentLength > 0)
                    {
                        item.TotalBytes = existingBytes + response.ContentLength;
                    }
                }
                else
                {
                    // Server returned 200 OK or does not support range; reset to 0
                    fs = new FileStream(wbPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    existingBytes = 0;
                    if (response.ContentLength > 0)
                    {
                        item.TotalBytes = response.ContentLength;
                    }
                }

                responseStream = response.GetResponseStream();

                byte[] buffer = new byte[65536];
                int bytesRead;
                long totalRead = existingBytes;
                DateTime lastSpeedCheck = DateTime.UtcNow;
                DateTime lastDiskSave = DateTime.UtcNow;
                long bytesSinceSpeedCheck = 0;

                while ((bytesRead = responseStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (cancelRequested || pauseRequested) break;

                    fs.Write(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    bytesSinceSpeedCheck += bytesRead;

                    item.ReceivedBytes = totalRead;
                    if (item.TotalBytes > 0)
                    {
                        item.Percent = (int)Math.Min(100, (totalRead * 100) / item.TotalBytes);
                    }

                    DateTime now = DateTime.UtcNow;
                    double elapsedSec = (now - lastSpeedCheck).TotalSeconds;
                    if (elapsedSec >= 0.5)
                    {
                        item.CurrentSpeed = bytesSinceSpeedCheck / elapsedSec;
                        lastSpeedCheck = now;
                        bytesSinceSpeedCheck = 0;

                        if ((now - lastDiskSave).TotalSeconds >= 5.0)
                        {
                            lastDiskSave = now;
                            Save();
                        }
                        if (updateHandler != null) updateHandler(this, item);
                    }
                }

                fs.Flush();

                if (cancelRequested)
                {
                    item.State = "Cancelled";
                    item.CurrentSpeed = 0;
                }
                else if (pauseRequested)
                {
                    item.State = "Paused";
                    item.CurrentSpeed = 0;
                }
                else
                {
                    // 100% finished! Close fs first to release file lock before moving
                    fs.Close();
                    fs.Dispose();
                    fs = null;

                    if (File.Exists(wbPath))
                    {
                        if (File.Exists(item.FilePath))
                        {
                            try { File.Delete(item.FilePath); } catch { }
                        }
                        File.Move(wbPath, item.FilePath);
                    }

                    item.State = "Completed";
                    item.Percent = 100;
                    item.EndTime = DateTime.Now;
                    item.CurrentSpeed = 0;
                }
            }
            catch (Exception ex)
            {
                if (cancelRequested)
                {
                    item.State = "Cancelled";
                }
                else if (pauseRequested)
                {
                    item.State = "Paused";
                }
                else
                {
                    if (ex is WebException)
                    {
                        item.State = "Failed - Network error";
                        item.FailureReason = "NetworkError";
                    }
                    else if (ex is IOException)
                    {
                        int hr = System.Runtime.InteropServices.Marshal.GetHRForException(ex) & 0xFFFF;
                        if (hr == 0x70 || hr == 0x27 || ex.Message.IndexOf("disk", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            item.State = "Failed - Disk full";
                            item.FailureReason = "DiskFull";
                        }
                        else
                        {
                            item.State = "Failed";
                        }
                    }
                    else
                    {
                        item.State = "Failed";
                    }
                }
                item.CurrentSpeed = 0;
            }
            finally
            {
                if (fs != null) { try { fs.Close(); fs.Dispose(); } catch { } }
                if (responseStream != null) { try { responseStream.Close(); responseStream.Dispose(); } catch { } }
                if (response != null) { try { response.Close(); } catch { } }

                Save();

                CheckAndNotifyActiveCount();

                if (updateHandler != null) updateHandler(this, item);

                if (string.Equals(item.State, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    EventHandler<DownloadItemModel> completeHandler = DownloadCompleted;
                    if (completeHandler != null) completeHandler(this, item);
                }
            }
        }

        private void Save()
        {
            if (AppServices.Database == null) return;
            try
            {
                AppServices.Database.InTransaction(() =>
                {
                    AppServices.Database.ExecuteNonQuery("DELETE FROM downloads;");
                    for (int i = 0; i < _downloads.Count; i++)
                    {
                        DownloadItemModel item = _downloads[i];
                        if (item == null) continue;
                        AppServices.Database.ExecuteNonQuery(
                            "INSERT INTO downloads (id, url, full_path, file_name, total_bytes, received_bytes, state, start_time, end_time, mime_type, error_message) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);",
                            item.Id.ToString(),
                            item.Url ?? string.Empty,
                            item.FilePath ?? string.Empty,
                            item.FileName ?? "download",
                            item.TotalBytes,
                            item.ReceivedBytes,
                            item.State ?? "Completed",
                            item.StartTime.Ticks,
                            item.EndTime.HasValue ? (object)item.EndTime.Value.Ticks : null,
                            string.Empty,
                            item.FailureReason ?? string.Empty);
                    }
                });
            }
            catch { }
        }
    }
}
