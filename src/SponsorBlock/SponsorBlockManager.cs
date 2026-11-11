using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using wb.Core;

namespace wb.SponsorBlock
{
    /// <summary>
    /// Central manager for YouTube SponsorBlock functionality.
    /// Manages configuration, cached skip segments, remote API queries,
    /// statistics tracking and domain/video whitelisting.
    /// </summary>
    public class SponsorBlockManager
    {
        public const string ConfigFileName = AppStrings.SponsorBlock.ConfigFileName;
        public const string StatsFileName = AppStrings.SponsorBlock.StatsFileName;
        public const string ApiBaseUrl = AppStrings.SponsorBlock.ApiBaseUrl;

        private readonly object _configLock = new object();
        private readonly object _statsLock = new object();

        private SponsorBlockConfig _config;
        private SponsorBlockStats _stats;

        // In-memory segments cache: VideoId -> CachedSegments
        private readonly ConcurrentDictionary<string, CachedSegments> _cache =
            new ConcurrentDictionary<string, CachedSegments>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<SponsorSkipEventArgs> SkipOccurred;
        public event EventHandler ConfigChanged;
        public event EventHandler StatsChanged;
        public event EventHandler WhitelistChanged;

        private class CachedSegments
        {
            public string RawJson { get; set; }
            public List<SponsorSegment> Segments { get; set; }
            public DateTime CachedAt { get; set; }
        }

        public SponsorBlockManager()
        {
            LoadSettings();
        }

        public bool IsEnabled
        {
            get
            {
                lock (_configLock)
                {
                    return _config != null && _config.IsEnabled;
                }
            }
            set
            {
                lock (_configLock)
                {
                    if (_config.IsEnabled != value)
                    {
                        _config.IsEnabled = value;
                        SaveConfigInternal();
                    }
                }
                OnConfigChanged();
            }
        }

        public SponsorBlockConfig Config
        {
            get
            {
                lock (_configLock)
                {
                    return _config;
                }
            }
        }

        public SponsorBlockStats Stats
        {
            get
            {
                lock (_statsLock)
                {
                    return _stats;
                }
            }
        }

        public long TotalSkips
        {
            get
            {
                lock (_statsLock)
                {
                    return _stats != null ? _stats.TotalSkips : 0;
                }
            }
        }

        public string FormattedTimeSaved
        {
            get
            {
                lock (_statsLock)
                {
                    return _stats != null ? _stats.FormattedTimeSaved : "0s";
                }
            }
        }

        public void LoadSettings()
        {
            lock (_configLock)
            {
                _config = StorageHelper.LoadObject<SponsorBlockConfig>(ConfigFileName);
                if (_config == null)
                {
                    _config = new SponsorBlockConfig();
                }
            }

            lock (_statsLock)
            {
                _stats = StorageHelper.LoadObject<SponsorBlockStats>(StatsFileName);
                if (_stats == null)
                {
                    _stats = new SponsorBlockStats();
                }
            }
        }

        public void SaveConfig()
        {
            lock (_configLock)
            {
                SaveConfigInternal();
            }
            OnConfigChanged();
        }

        private void SaveConfigInternal()
        {
            try
            {
                StorageHelper.SaveObject(ConfigFileName, _config);
            }
            catch
            {
            }
        }

        public void SaveStats()
        {
            lock (_statsLock)
            {
                try
                {
                    StorageHelper.SaveObject(StatsFileName, _stats);
                }
                catch
                {
                }
            }
            OnStatsChanged();
        }

        public void ResetStats()
        {
            lock (_statsLock)
            {
                if (_stats != null)
                {
                    _stats.Reset();
                }
                SaveStats();
            }
            OnStatsChanged();
        }

        public void RecordSkip(string videoId, string category, double savedSeconds)
        {
            lock (_statsLock)
            {
                if (_stats != null)
                {
                    _stats.AddSkip(category, savedSeconds);
                }
            }

            Task.Run(() => SaveStats());

            var handler = SkipOccurred;
            if (handler != null)
            {
                handler(this, new SponsorSkipEventArgs(videoId, category, savedSeconds));
            }
            OnStatsChanged();
        }

        public List<string> GetWhitelistedVideos()
        {
            lock (_configLock)
            {
                return _config != null ? _config.GetWhitelistedVideos() : new List<string>();
            }
        }

        public bool AddVideoWhitelist(string videoIdOrUrl)
        {
            if (string.IsNullOrEmpty(videoIdOrUrl)) return false;
            string vid = ExtractVideoId(videoIdOrUrl);
            if (string.IsNullOrEmpty(vid)) vid = videoIdOrUrl.Trim();
            if (string.IsNullOrEmpty(vid)) return false;

            bool added = false;
            lock (_configLock)
            {
                if (_config != null)
                {
                    added = _config.AddVideoWhitelist(vid);
                    if (added) SaveConfigInternal();
                }
            }
            if (added)
            {
                OnWhitelistChanged();
                OnConfigChanged();
            }
            return added;
        }

        public bool RemoveVideoWhitelist(string videoIdOrUrl)
        {
            if (string.IsNullOrEmpty(videoIdOrUrl)) return false;
            string vid = ExtractVideoId(videoIdOrUrl);
            if (string.IsNullOrEmpty(vid)) vid = videoIdOrUrl.Trim();

            bool removed = false;
            lock (_configLock)
            {
                if (_config != null)
                {
                    removed = _config.RemoveVideoWhitelist(vid);
                    if (removed) SaveConfigInternal();
                }
            }
            if (removed)
            {
                OnWhitelistChanged();
                OnConfigChanged();
            }
            return removed;
        }

        public void ClearVideoWhitelist()
        {
            lock (_configLock)
            {
                if (_config != null)
                {
                    _config.ClearVideoWhitelist();
                    SaveConfigInternal();
                }
            }
            OnWhitelistChanged();
            OnConfigChanged();
        }

        public bool IsWhitelisted(string urlOrVideoId)
        {
            if (string.IsNullOrEmpty(urlOrVideoId)) return false;
            string vid = ExtractVideoId(urlOrVideoId);
            lock (_configLock)
            {
                if (_config == null) return false;
                if (!string.IsNullOrEmpty(vid) && _config.IsVideoWhitelisted(vid))
                {
                    return true;
                }
            }
            return false;
        }

        public bool ToggleVideoWhitelist(string videoId)
        {
            if (string.IsNullOrEmpty(videoId)) return false;
            string vid = ExtractVideoId(videoId);
            if (string.IsNullOrEmpty(vid)) vid = videoId.Trim();
            bool result;
            lock (_configLock)
            {
                result = _config.ToggleVideoWhitelist(vid);
                SaveConfigInternal();
            }
            OnWhitelistChanged();
            OnConfigChanged();
            return result;
        }

        private void OnWhitelistChanged()
        {
            var handler = WhitelistChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public static string ExtractVideoId(string urlOrVideoId)
        {
            return UrlHelper.ExtractYouTubeVideoId(urlOrVideoId);
        }

        public string GetConfigJson()
        {
            lock (_configLock)
            {
                string json = StorageHelper.Serialize(_config);
                return !string.IsNullOrEmpty(json) ? json : "{}";
            }
        }

        public void FetchSegmentsAsync(string videoId, Action<string> callback)
        {
            if (string.IsNullOrEmpty(videoId))
            {
                if (callback != null) callback(null);
                return;
            }

            // Check in-memory cache first (valid for 30 minutes)
            CachedSegments cached;
            if (_cache.TryGetValue(videoId, out cached))
            {
                if ((DateTime.UtcNow - cached.CachedAt).TotalMinutes < 30)
                {
                    if (callback != null) callback(cached.RawJson);
                    return;
                }
            }

            Task.Run(() =>
            {
                string json = FetchSegmentsRemote(videoId);
                if (callback != null)
                {
                    callback(json);
                }
            });
        }

        private string FetchSegmentsRemote(string videoId)
        {
            try
            {
                string categories = Uri.EscapeDataString("[\"sponsor\",\"intro\",\"outro\",\"interaction\",\"selfpromo\",\"preview\",\"filler\",\"music_offtopic\",\"poi_highlight\"]");
                string actionTypes = Uri.EscapeDataString("[\"skip\",\"mute\"]");
                string url = string.Format("{0}?videoID={1}&categories={2}&actionTypes={3}",
                    ApiBaseUrl, Uri.EscapeDataString(videoId), categories, actionTypes);

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 4000;
                req.ReadWriteTimeout = 4000;
                req.UserAgent = AppStrings.UserAgent;
                req.Headers.Add("X-Client", AppStrings.SponsorBlock.ApiClientHeader);

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        using (Stream s = resp.GetResponseStream())
                        using (StreamReader reader = new StreamReader(s, Encoding.UTF8))
                        {
                            string json = reader.ReadToEnd();
                            _cache[videoId] = new CachedSegments
                            {
                                RawJson = json,
                                CachedAt = DateTime.UtcNow
                            };
                            return json;
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse httpResp = ex.Response as HttpWebResponse;
                if (httpResp != null && httpResp.StatusCode == HttpStatusCode.NotFound)
                {
                    // 404 means no segments submitted for this video
                    _cache[videoId] = new CachedSegments
                    {
                        RawJson = "[]",
                        CachedAt = DateTime.UtcNow
                    };
                    return "[]";
                }
            }
            catch
            {
            }
            return null;
        }

        private void OnConfigChanged()
        {
            var handler = ConfigChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void OnStatsChanged()
        {
            var handler = StatsChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
