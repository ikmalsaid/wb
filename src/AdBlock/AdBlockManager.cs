using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using wb.Core;
using wb.Browser;

namespace wb.AdBlock
{
    public class AdBlockStatsModel
    {
        public bool IsEnabled { get; set; }
        public long TotalBlockedCount { get; set; }

        public AdBlockStatsModel()
        {
            IsEnabled = true;
            TotalBlockedCount = 0;
        }
    }

    /// <summary>
    /// Next-generation, high-performance uBlock-style ad & tracking protection engine.
    /// Uses subscription-based filter lists (ABP/uBlock and Hosts formats), atomic lock-free rule compilation,
    /// and O(1) hash matching for zero CPU overhead during page rendering.
    /// </summary>
    public class AdBlockManager
    {
        private const string WhitelistFileName = "adblock_whitelist.json";
        private const string StatsFileName = "adblock_stats.json";
        private const string SubscriptionsFileName = "adblock_subscriptions.json";
        private const string CustomFiltersFileName = "adblock_custom_filters.json";
        private const string FiltersFolderName = "filters";

        private readonly object _stateLock = new object();
        private readonly HashSet<string> _whitelistedDomains;
        private readonly List<FilterSubscription> _subscriptions;
        private readonly List<CustomFilterRule> _customFilters;

        private volatile CompiledRules _compiledRules;
        private bool _isEnabled;
        private long _totalBlockedCount;
        private long _sessionBlockedCount;
        private DateTime _lastUpdatedUtc;
        private bool _isUpdating;
        private System.Threading.Timer _autoUpdateTimer;

        public event EventHandler StatsUpdated;
        public event EventHandler<string> WhitelistChanged;
        public event EventHandler EnabledChanged;
        public event EventHandler FilterListsUpdated;
        public event EventHandler CustomFiltersChanged;

        public bool IsEnabled
        {
            get
            {
                return _isEnabled;
            }
            set
            {
                lock (_stateLock)
                {
                    if (_isEnabled == value) return;
                    _isEnabled = value;
                    SaveStatsUnsafe();
                }
                EventHandler handler = EnabledChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public long TotalBlockedCount
        {
            get { return System.Threading.Interlocked.Read(ref _totalBlockedCount); }
        }

        public long SessionBlockedCount
        {
            get { return System.Threading.Interlocked.Read(ref _sessionBlockedCount); }
        }

        public DateTime LastUpdatedUtc
        {
            get
            {
                lock (_stateLock)
                {
                    return _lastUpdatedUtc;
                }
            }
        }

        public int TotalRuleCount
        {
            get
            {
                CompiledRules rules = _compiledRules;
                return rules != null ? rules.TotalRules : 0;
            }
        }

        public bool IsUpdating
        {
            get
            {
                lock (_stateLock)
                {
                    return _isUpdating;
                }
            }
        }

        public static void EnsureSecurityProtocol()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 |
                                                       SecurityProtocolType.Tls11 |
                                                       SecurityProtocolType.Tls |
                                                       (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            }
            catch
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls;
                    ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                }
                catch
                {
                }
            }
        }

        public AdBlockManager()
        {
            EnsureSecurityProtocol();
            _whitelistedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _subscriptions = new List<FilterSubscription>();
            _customFilters = new List<CustomFilterRule>();
            _lastUpdatedUtc = DateTime.MinValue;

            // Load custom filters (Dropper rules)
            LoadCustomFilters();

            // Load whitelist
            List<string> savedWhitelist = StorageHelper.LoadList<string>(WhitelistFileName);
            if (savedWhitelist != null)
            {
                for (int i = 0; i < savedWhitelist.Count; i++)
                {
                    string dom = NormalizeDomain(savedWhitelist[i]);
                    if (!string.IsNullOrEmpty(dom))
                    {
                        _whitelistedDomains.Add(dom);
                    }
                }
            }

            // Load stats
            AdBlockStatsModel stats = StorageHelper.LoadObject<AdBlockStatsModel>(StatsFileName);
            _isEnabled = stats != null ? stats.IsEnabled : true;
            _totalBlockedCount = stats != null ? stats.TotalBlockedCount : 0;
            _sessionBlockedCount = 0;

            // Load subscriptions
            LoadSubscriptions();

            // Compile initial rules
            CompileRulesInternal();

            // Schedule background auto-updater (first startup check after 1.5s, existing after 10s, then daily)
            bool isFirstStartup = false;
            for (int i = 0; i < _subscriptions.Count; i++)
            {
                if (_subscriptions[i].IsEnabled && _subscriptions[i].LastUpdatedUtc == DateTime.MinValue)
                {
                    isFirstStartup = true;
                    break;
                }
            }

            try
            {
                _autoUpdateTimer = new System.Threading.Timer(
                    delegate { CheckForUpdatesInBackground(false); },
                    null,
                    isFirstStartup ? TimeSpan.FromMilliseconds(1500) : TimeSpan.FromSeconds(10),
                    TimeSpan.FromHours(24));
            }
            catch
            {
            }
        }

        public static List<FilterSubscription> GetDefaultSubscriptions()
        {
            List<FilterSubscription> list = new List<FilterSubscription>();

            list.Add(new FilterSubscription(
                "ublock-filters",
                "uBlock filters",
                "https://ublockorigin.pages.dev/filters/filters.min.txt",
                "Built-in",
                "filters_ublock.txt",
                false,
                new string[]
                {
                    "https://ublockorigin.github.io/uAssets/filters/filters.txt",
                    "https://cdn.jsdelivr.net/gh/uBlockOrigin/uAssetsCDN@main/filters/filters.min.txt",
                    "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/filters.txt"
                }));

            list.Add(new FilterSubscription(
                "ublock-badware",
                "uBlock filters - Badware risks",
                "https://ublockorigin.pages.dev/filters/badware.min.txt",
                "Malware",
                "filters_badware.txt",
                false,
                new string[]
                {
                    "https://ublockorigin.github.io/uAssets/filters/badware.txt",
                    "https://cdn.jsdelivr.net/gh/uBlockOrigin/uAssetsCDN@main/filters/badware.min.txt",
                    "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/badware.txt"
                }));

            list.Add(new FilterSubscription(
                "ublock-privacy",
                "uBlock filters - Privacy",
                "https://ublockorigin.pages.dev/filters/privacy.min.txt",
                "Privacy",
                "filters_privacy.txt",
                false,
                new string[]
                {
                    "https://ublockorigin.github.io/uAssets/filters/privacy.txt",
                    "https://cdn.jsdelivr.net/gh/uBlockOrigin/uAssetsCDN@main/filters/privacy.min.txt",
                    "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/privacy.txt"
                }));

            list.Add(new FilterSubscription(
                "ublock-quick-fixes",
                "uBlock filters - Quick fixes",
                "https://ublockorigin.pages.dev/filters/quick-fixes.min.txt",
                "Built-in",
                "filters_quick_fixes.txt",
                false,
                new string[]
                {
                    "https://ublockorigin.github.io/uAssets/filters/quick-fixes.txt",
                    "https://cdn.jsdelivr.net/gh/uBlockOrigin/uAssetsCDN@main/filters/quick-fixes.min.txt",
                    "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/quick-fixes.txt"
                }));

            list.Add(new FilterSubscription(
                "ublock-unbreak",
                "uBlock filters - Unbreak",
                "https://ublockorigin.pages.dev/filters/unbreak.min.txt",
                "Built-in",
                "filters_unbreak.txt",
                false,
                new string[]
                {
                    "https://ublockorigin.github.io/uAssets/filters/unbreak.txt",
                    "https://cdn.jsdelivr.net/gh/uBlockOrigin/uAssetsCDN@main/filters/unbreak.min.txt",
                    "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/unbreak.txt"
                }));

            list.Add(new FilterSubscription(
                "easylist",
                "EasyList",
                "https://ublockorigin.pages.dev/thirdparties/easylist.txt",
                "Ads",
                "filters_easylist.txt",
                false,
                new string[]
                {
                    "https://ublockorigin.github.io/uAssets/thirdparties/easylist.txt",
                    "https://cdn.jsdelivr.net/gh/uBlockOrigin/uAssetsCDN@main/thirdparties/easylist.txt",
                    "https://easylist.to/easylist/easylist.txt"
                }));

            list.Add(new FilterSubscription(
                "easyprivacy",
                "EasyPrivacy",
                "https://ublockorigin.pages.dev/thirdparties/easyprivacy.txt",
                "Privacy",
                "filters_easyprivacy.txt",
                false,
                new string[]
                {
                    "https://ublockorigin.github.io/uAssets/thirdparties/easyprivacy.txt",
                    "https://cdn.jsdelivr.net/gh/uBlockOrigin/uAssetsCDN@main/thirdparties/easyprivacy.txt",
                    "https://easylist.to/easylist/easyprivacy.txt"
                }));

            list.Add(new FilterSubscription(
                "plowe-0",
                "Peter Lowe's Ad and tracking server list",
                "https://ublockorigin.pages.dev/thirdparties/pgl.yoyo.org/as/serverlist",
                "Ads",
                "filters_plowe.txt",
                false,
                new string[]
                {
                    "https://ublockorigin.github.io/uAssetsCDN/thirdparties/pgl.yoyo.org/as/serverlist",
                    "https://pgl.yoyo.org/adservers/serverlist.php?hostformat=hosts&showintro=1&mimetype=plaintext"
                }));

            list.Add(new FilterSubscription(
                "turtlecute",
                "Turtlecute AdBlock Suite",
                "https://adblock.turtlecute.org/d3host.adblock",
                "Test & Regional",
                "filters_turtlecute.txt",
                false,
                new string[]
                {
                    "https://adblock.turtlecute.org/d3host.adblock"
                }));

            return list;
        }

        /// <summary>
        /// Loads subscription configuration or sets up standard uBlock Origin defaults.
        /// Upgrades existing subscriptions with current CDN mirrors and resets any failed states.
        /// </summary>
        private void LoadSubscriptions()
        {
            List<FilterSubscription> defaults = GetDefaultSubscriptions();
            List<FilterSubscription> saved = StorageHelper.LoadList<FilterSubscription>(SubscriptionsFileName);

            if (saved != null && saved.Count > 0)
            {
                Dictionary<string, FilterSubscription> savedMap = new Dictionary<string, FilterSubscription>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < saved.Count; i++)
                {
                    if (saved[i] != null && !string.IsNullOrEmpty(saved[i].Id))
                    {
                        savedMap[saved[i].Id] = saved[i];
                    }
                }

                // Upgrade and sync default subscriptions with latest mirrors
                for (int i = 0; i < defaults.Count; i++)
                {
                    FilterSubscription def = defaults[i];
                    FilterSubscription existing;
                    if (savedMap.TryGetValue(def.Id, out existing))
                    {
                        def.IsEnabled = existing.IsEnabled;
                        def.RuleCount = existing.RuleCount;
                        def.ETag = existing.ETag;
                        def.LastModified = existing.LastModified;
                        def.LastUpdatedUtc = existing.LastUpdatedUtc;
                        def.LastStatus = (existing.LastStatus == "Check failed") ? AppStrings.AdBlock.StatusReady : existing.LastStatus;
                        _subscriptions.Add(def);
                        savedMap.Remove(def.Id);
                    }
                    else
                    {
                        _subscriptions.Add(def);
                    }
                }

                // Add any custom subscriptions
                foreach (KeyValuePair<string, FilterSubscription> kvp in savedMap)
                {
                    _subscriptions.Add(kvp.Value);
                }
                SaveSubscriptionsUnsafe();
            }
            else
            {
                _subscriptions.AddRange(defaults);
                SaveSubscriptionsUnsafe();
            }

            // Restore _lastUpdatedUtc from the highest timestamp among saved subscriptions
            DateTime maxUpdated = DateTime.MinValue;
            for (int i = 0; i < _subscriptions.Count; i++)
            {
                if (_subscriptions[i].LastUpdatedUtc > maxUpdated)
                {
                    maxUpdated = _subscriptions[i].LastUpdatedUtc;
                }
            }
            _lastUpdatedUtc = maxUpdated;

            // Ensure seed content is populated if files don't exist yet
            EnsureSeedFilterFiles();
        }

        /// <summary>
        /// Populates initial filter files from bundled assets if the user directory has no downloaded lists yet.
        /// </summary>
        private void EnsureSeedFilterFiles()
        {
            string filtersDir = GetFiltersDirectory();
            if (!Directory.Exists(filtersDir))
            {
                Directory.CreateDirectory(filtersDir);
            }

            string seedPath = FindAssetPath(Path.Combine("assets", "filters", "default_filters.txt"));
            if (string.IsNullOrEmpty(seedPath) || !File.Exists(seedPath))
            {
                return;
            }

            string seedContent = null;
            try
            {
                seedContent = File.ReadAllText(seedPath);
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(seedContent)) return;

            // Check if any subscription has a local file
            for (int i = 0; i < _subscriptions.Count; i++)
            {
                FilterSubscription sub = _subscriptions[i];
                if (string.IsNullOrEmpty(sub.LocalFileName)) continue;

                string filePath = Path.Combine(filtersDir, sub.LocalFileName);
                if (!File.Exists(filePath))
                {
                    try
                    {
                        // Check if a dedicated bundled asset exists for this list
                        string dedicatedAsset = FindAssetPath(Path.Combine("assets", "filters", sub.LocalFileName));
                        if (!string.IsNullOrEmpty(dedicatedAsset) && File.Exists(dedicatedAsset))
                        {
                            File.Copy(dedicatedAsset, filePath, true);
                        }
                        else
                        {
                            // Populate with default seed filters
                            File.WriteAllText(filePath, seedContent);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static string FindAssetPath(string relativePath)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path1 = Path.Combine(baseDir, relativePath);
            if (File.Exists(path1)) return path1;
            string pathCore = Path.Combine(baseDir, "core", relativePath);
            if (File.Exists(pathCore)) return pathCore;
            string pathSys = Path.Combine(baseDir, "system", relativePath);
            if (File.Exists(pathSys)) return pathSys;

            DirectoryInfo dir = new DirectoryInfo(baseDir);
            while (dir != null && dir.Exists)
            {
                string candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir.FullName, "core", relativePath);
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir.FullName, "system", relativePath);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        public string GetFiltersDirectory()
        {
            return StorageHelper.GetFiltersDirectory();
        }

        /// <summary>
        /// <summary>
        /// Compiles all enabled filter subscriptions into an immutable, lock-free CompiledRules instance.
        /// </summary>
        private void CompileRulesInternal()
        {
            HashSet<string> blockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> thirdPartyBlockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> whitelistedDomains;
            Dictionary<string, List<string>> domainPathRules = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<ExceptionRule>> domainExceptions = new Dictionary<string, List<ExceptionRule>>(StringComparer.OrdinalIgnoreCase);
            List<string> globalBlockedPaths = new List<string>();
            List<string> globalExceptionPatterns = new List<string>();
            HashSet<string> genericCosmeticSelectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> domainCosmeticRules = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> genericHideDisabledDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string> domainSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            lock (_stateLock)
            {
                whitelistedDomains = new HashSet<string>(_whitelistedDomains, StringComparer.OrdinalIgnoreCase);
            }

            // Standard ad and tracking script paths
            globalBlockedPaths.Add("/pagead.js");
            globalBlockedPaths.Add("/ads.js");
            globalBlockedPaths.Add("/pagead/");
            globalBlockedPaths.Add("/adserver/");
            globalBlockedPaths.Add("/adsystem/");
            globalBlockedPaths.Add("/partnerads/");
            globalBlockedPaths.Add("/banners/");
            globalBlockedPaths.Add("/ad_banner");
            globalBlockedPaths.Add("/ads_banner");
            globalBlockedPaths.Add("_ads_banner");
            globalBlockedPaths.Add("pr_advertising");
            globalBlockedPaths.Add("/advertising_ads_");
            globalBlockedPaths.Add("/pr_advertising_");
            globalBlockedPaths.Add("/advert/");
            globalBlockedPaths.Add("/adverts/");
            globalBlockedPaths.Add("/advertising/");
            globalBlockedPaths.Add("/advertisement/");
            globalBlockedPaths.Add("/advertisements/");
            globalBlockedPaths.Add("/advertisment/");
            globalBlockedPaths.Add("/advertisments/");
            globalBlockedPaths.Add("/ad/");
            globalBlockedPaths.Add("/ads/");

            // 1. Load bundled baseline seed filters (ensures core ad networks & safe ad script paths are always active)
            string seedPath = FindAssetPath(Path.Combine("assets", "filters", "default_filters.txt"));
            if (!string.IsNullOrEmpty(seedPath) && File.Exists(seedPath))
            {
                try
                {
                    string content = File.ReadAllText(seedPath);
                    ParseDirectRules(content, blockedDomains, thirdPartyBlockedDomains, whitelistedDomains,
                        domainPathRules, domainExceptions, globalBlockedPaths, globalExceptionPatterns,
                        genericCosmeticSelectors, domainCosmeticRules, genericHideDisabledDomains);
                }
                catch
                {
                }
            }

            // 2. Dedicated YouTube protection rules (0% CPU, eliminates video ads)
            for (int i = 0; i < AdBlockYTShield.AdditionalBlockedDomains.Length; i++)
            {
                string dom = NormalizeDomain(AdBlockYTShield.AdditionalBlockedDomains[i]);
                if (IsValidDomain(dom)) blockedDomains.Add(dom);
            }

            List<string> ytPaths;
            if (!domainPathRules.TryGetValue("youtube.com", out ytPaths))
            {
                ytPaths = new List<string>();
                domainPathRules["youtube.com"] = ytPaths;
            }
            for (int i = 0; i < AdBlockYTShield.AdditionalBlockedPaths.Length; i++)
            {
                ytPaths.Add(AdBlockYTShield.AdditionalBlockedPaths[i]);
            }

            List<string> ytBePaths;
            if (!domainPathRules.TryGetValue("youtu.be", out ytBePaths))
            {
                ytBePaths = new List<string>();
                domainPathRules["youtu.be"] = ytBePaths;
            }
            for (int i = 0; i < AdBlockYTShield.AdditionalBlockedPaths.Length; i++)
            {
                ytBePaths.Add(AdBlockYTShield.AdditionalBlockedPaths[i]);
            }

            List<string> ytNoCookiePaths;
            if (!domainPathRules.TryGetValue("youtube-nocookie.com", out ytNoCookiePaths))
            {
                ytNoCookiePaths = new List<string>();
                domainPathRules["youtube-nocookie.com"] = ytNoCookiePaths;
            }
            for (int i = 0; i < AdBlockYTShield.AdditionalBlockedPaths.Length; i++)
            {
                ytNoCookiePaths.Add(AdBlockYTShield.AdditionalBlockedPaths[i]);
            }

            // 3. Parse from subscription filter files
            string filtersDir = GetFiltersDirectory();
            List<FilterSubscription> activeSubs = new List<FilterSubscription>();
            lock (_stateLock)
            {
                for (int i = 0; i < _subscriptions.Count; i++)
                {
                    if (_subscriptions[i].IsEnabled)
                    {
                        activeSubs.Add(_subscriptions[i]);
                    }
                }
            }

            for (int i = 0; i < activeSubs.Count; i++)
            {
                FilterSubscription sub = activeSubs[i];
                if (string.IsNullOrEmpty(sub.LocalFileName)) continue;

                string filePath = Path.Combine(filtersDir, sub.LocalFileName);
                if (!File.Exists(filePath)) continue;

                int rulesInFile = 0;
                try
                {
                    using (StreamReader reader = new StreamReader(filePath, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (ParseFilterLine(line, blockedDomains, thirdPartyBlockedDomains, whitelistedDomains,
                                domainPathRules, domainExceptions, globalBlockedPaths, globalExceptionPatterns,
                                genericCosmeticSelectors, domainCosmeticRules, genericHideDisabledDomains,
                                domainSources, sub.Name))
                            {
                                rulesInFile++;
                            }
                        }
                    }

                    sub.RuleCount = rulesInFile;
                }
                catch
                {
                }
            }

            // 4. Incorporate user-defined custom filters (Dropper rules)
            lock (_stateLock)
            {
                for (int i = 0; i < _customFilters.Count; i++)
                {
                    CustomFilterRule rule = _customFilters[i];
                    if (rule == null || string.IsNullOrEmpty(rule.Selector)) continue;

                    if (string.IsNullOrEmpty(rule.Domain))
                    {
                        genericCosmeticSelectors.Add(rule.Selector);
                    }
                    else
                    {
                        string dom = NormalizeDomain(rule.Domain);
                        if (!string.IsNullOrEmpty(dom))
                        {
                            List<string> list;
                            if (!domainCosmeticRules.TryGetValue(dom, out list))
                            {
                                list = new List<string>();
                                domainCosmeticRules[dom] = list;
                            }
                            list.Add(rule.Selector);
                        }
                        else
                        {
                            genericCosmeticSelectors.Add(rule.Selector);
                        }
                    }
                }
            }

            // Atomically swap the compiled rules snapshot (0 lock overhead for ShouldBlock)
            _compiledRules = new CompiledRules(
                blockedDomains,
                thirdPartyBlockedDomains,
                whitelistedDomains,
                domainPathRules,
                domainExceptions,
                globalBlockedPaths,
                globalExceptionPatterns,
                genericCosmeticSelectors,
                domainCosmeticRules,
                genericHideDisabledDomains,
                domainSources);
        }

        public static bool ParseFilterLine(
            string rawLine,
            HashSet<string> blockedDomains,
            HashSet<string> thirdPartyBlockedDomains,
            HashSet<string> whitelistedDomains,
            Dictionary<string, List<string>> domainPathRules,
            Dictionary<string, List<ExceptionRule>> domainExceptions,
            List<string> globalBlockedPaths,
            List<string> globalExceptionPatterns,
            HashSet<string> genericCosmeticSelectors,
            Dictionary<string, List<string>> domainCosmeticRules,
            HashSet<string> genericHideDisabledDomains)
        {
            return ParseFilterLine(
                rawLine,
                blockedDomains,
                thirdPartyBlockedDomains,
                whitelistedDomains,
                domainPathRules,
                domainExceptions,
                globalBlockedPaths,
                globalExceptionPatterns,
                genericCosmeticSelectors,
                domainCosmeticRules,
                genericHideDisabledDomains,
                null,
                null);
        }

        public static bool ParseFilterLine(
            string rawLine,
            HashSet<string> blockedDomains,
            HashSet<string> thirdPartyBlockedDomains,
            HashSet<string> whitelistedDomains,
            Dictionary<string, List<string>> domainPathRules,
            Dictionary<string, List<ExceptionRule>> domainExceptions,
            List<string> globalBlockedPaths,
            List<string> globalExceptionPatterns,
            HashSet<string> genericCosmeticSelectors,
            Dictionary<string, List<string>> domainCosmeticRules,
            HashSet<string> genericHideDisabledDomains,
            Dictionary<string, string> domainSources,
            string currentSubscriptionName)
        {
            if (string.IsNullOrEmpty(rawLine)) return false;
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("!") || line.StartsWith("[")) return false;

            // 1. Cosmetic exceptions: #@# (skip; we do not treat them as hiding rules)
            int cosmeticExIdx = line.IndexOf("#@#", StringComparison.Ordinal);
            if (cosmeticExIdx >= 0)
            {
                return false;
            }

            // 2. Cosmetic filters: ##
            int hashIdx = line.IndexOf("##", StringComparison.Ordinal);
            if (hashIdx >= 0)
            {
                string prefix = line.Substring(0, hashIdx).Trim();
                string selector = line.Substring(hashIdx + 2).Trim();

                if (string.IsNullOrEmpty(selector)) return false;

                // Disallow scriptlets and procedural pseudo-classes not supported in standard CSS
                if (selector.Contains("+js(") || selector.Contains(":has-text(") ||
                    selector.Contains(":watch-attr(") || selector.Contains(":matches-") || selector.Contains(":xpath(") ||
                    selector.Contains(":style(") || selector.Contains(":remove()") || selector.Contains(":upward(") ||
                    selector.Contains(":remove-attr(") || selector.Contains(":remove-class(") || selector.Contains(":min-text-length("))
                {
                    return false;
                }

                if (string.IsNullOrEmpty(prefix))
                {
                    // Generic cosmetic filter - allow ID, class, attribute and tag-qualified selectors (e.g. object[...], embed[...])
                    if (IsValidGenericCosmeticSelector(selector))
                    {
                        genericCosmeticSelectors.Add(selector);
                        return true;
                    }
                    return false;
                }
                else
                {
                    // Domain-specific cosmetic filter (e.g. domain.com##.ad)
                    string[] doms = prefix.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    bool added = false;
                    for (int d = 0; d < doms.Length; d++)
                    {
                        string targetDom = doms[d].Trim().ToLowerInvariant();
                        if (targetDom.StartsWith("~") || string.IsNullOrEmpty(targetDom)) continue;
                        if (targetDom.StartsWith("www.")) targetDom = targetDom.Substring(4);

                        List<string> list;
                        if (!domainCosmeticRules.TryGetValue(targetDom, out list))
                        {
                            list = new List<string>();
                            domainCosmeticRules[targetDom] = list;
                        }
                        list.Add(selector);
                        added = true;
                    }
                    return added;
                }
            }

            // 3. Exception / Unbreak rules: @@
            if (line.StartsWith("@@"))
            {
                string ex = line.Substring(2).Trim();
                int dollar = ex.IndexOf('$');
                string opts = dollar >= 0 ? ex.Substring(dollar + 1).ToLowerInvariant() : "";
                if (dollar >= 0) ex = ex.Substring(0, dollar).Trim();

                if (opts.Contains("badfilter"))
                {
                    return false;
                }

                if (opts.Contains("generichide") || opts.Contains("ghide"))
                {
                    if (ex.StartsWith("||"))
                    {
                        string d = NormalizeDomain(ex.Substring(2).TrimEnd('^'));
                        if (IsValidDomain(d)) genericHideDisabledDomains.Add(d);
                    }
                    return true;
                }

                // If exception has domain=... constraint, extract source domains!
                bool hasExDomainConstraint = opts.Contains("domain=");
                string[] sourceDomains = null;
                if (hasExDomainConstraint)
                {
                    int dIdx = opts.IndexOf("domain=");
                    if (dIdx >= 0)
                    {
                        string dVal = opts.Substring(dIdx + 7);
                        int comma = dVal.IndexOf(',');
                        if (comma >= 0) dVal = dVal.Substring(0, comma);
                        string[] rawDoms = dVal.Split('|');
                        List<string> cleanDoms = new List<string>();
                        for (int i = 0; i < rawDoms.Length; i++)
                        {
                            string s = rawDoms[i].Trim();
                            if (!s.StartsWith("~") && !string.IsNullOrEmpty(s))
                            {
                                cleanDoms.Add(NormalizeDomain(s));
                            }
                        }
                        if (cleanDoms.Count > 0) sourceDomains = cleanDoms.ToArray();
                    }
                }
                bool firstPartyOnly = opts.Contains("~third-party") || opts.Contains("1p");
                bool isDoc = opts.Contains("document") || opts.Contains("doc");

                if (ex.StartsWith("||"))
                {
                    string target = ex.Substring(2);
                    int caret = target.IndexOf('^');
                    string pathAfterCaret = null;
                    if (caret >= 0)
                    {
                        if (caret < target.Length - 1)
                        {
                            pathAfterCaret = target.Substring(caret + 1).TrimStart('*');
                        }
                        target = target.Substring(0, caret);
                    }

                    int slash = target.IndexOf('/');
                    if (slash >= 0 || !string.IsNullOrEmpty(pathAfterCaret))
                    {
                        string d = NormalizeDomain(slash >= 0 ? target.Substring(0, slash) : target);
                        string p = slash >= 0 ? target.Substring(slash) : pathAfterCaret;
                        if (IsValidDomain(d) && !string.IsNullOrEmpty(p))
                        {
                            AddDomainException(domainExceptions, d, p, sourceDomains, firstPartyOnly, isDoc);
                            return true;
                        }
                    }
                    else
                    {
                        string d = NormalizeDomain(target);
                        if (IsValidDomain(d))
                        {
                            AddDomainException(domainExceptions, d, null, sourceDomains, firstPartyOnly, isDoc);
                            return true;
                        }
                    }
                }
                else if (ex.StartsWith("/"))
                {
                    globalExceptionPatterns.Add(ex);
                    return true;
                }
                return false;
            }

            // 4. Hosts file format: 127.0.0.1 domain or 0.0.0.0 domain
            if (line.StartsWith("127.0.0.1") || line.StartsWith("0.0.0.0"))
            {
                int spaceIdx = line.IndexOfAny(new char[] { ' ', '\t' });
                if (spaceIdx > 0)
                {
                    string host = line.Substring(spaceIdx).Trim();
                    string dom = NormalizeDomain(host);
                    if (IsValidDomain(dom))
                    {
                        blockedDomains.Add(dom);
                        if (domainSources != null && !string.IsNullOrEmpty(currentSubscriptionName) && !domainSources.ContainsKey(dom))
                        {
                            domainSources[dom] = currentSubscriptionName;
                        }
                        return true;
                    }
                }
                return false;
            }

            // 5. Check modifiers ($)
            int dollarIdx = line.IndexOf('$');
            string options = "";
            string optionsRaw = "";
            if (dollarIdx > 0)
            {
                optionsRaw = line.Substring(dollarIdx + 1);
                options = optionsRaw.ToLowerInvariant();
                line = line.Substring(0, dollarIdx).Trim();
            }

            // Disallow non-block modifiers
            if (options.Contains("badfilter") || options.Contains("replace=") ||
                options.Contains("redirect=") || options.Contains("redirect-rule=") ||
                options.Contains("removeparam") || options.Contains("header=") ||
                options.Contains("csp="))
            {
                return false;
            }

            // Parse urlskip directive if present
            if (options.Contains("urlskip=") && line.StartsWith("||"))
            {
                try
                {
                    int uIdx = optionsRaw.IndexOf("urlskip=", StringComparison.OrdinalIgnoreCase);
                    if (uIdx >= 0)
                    {
                        string urlskipPart = optionsRaw.Substring(uIdx + 8).Trim();
                        int commaIdx = urlskipPart.IndexOf(',');
                        if (commaIdx >= 0) urlskipPart = urlskipPart.Substring(0, commaIdx).Trim();
                        string[] steps = urlskipPart.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        string target = line.Substring(2);
                        int slash = target.IndexOf('/');
                        string d = NormalizeDomain(slash >= 0 ? target.Substring(0, slash) : target);
                        string p = slash >= 0 ? target.Substring(slash) : null;
                        if (IsValidDomain(d) && steps.Length > 0)
                        {
                            UrlSkipHelper.RegisterRule(d, p, steps);
                        }
                    }
                }
                catch
                {
                }
            }

            // Constrained to specific third-party origins
            bool hasDomainConstraint = options.Contains("domain=");
            bool isThirdPartyOnly = options.Contains("third-party") || options.Contains("3p");

            // 6. EasyList / ABP format: ||domain^ or ||domain/path
            if (line.StartsWith("||"))
            {
                string target = line.Substring(2);
                int caretIdx = target.IndexOf('^');
                string pathAfterCaret = null;
                if (caretIdx >= 0)
                {
                    if (caretIdx < target.Length - 1)
                    {
                        pathAfterCaret = target.Substring(caretIdx + 1).TrimStart('*');
                    }
                    target = target.Substring(0, caretIdx);
                }

                int slashIdx = target.IndexOf('/');
                if (slashIdx >= 0 || !string.IsNullOrEmpty(pathAfterCaret))
                {
                    string d = NormalizeDomain(slashIdx >= 0 ? target.Substring(0, slashIdx) : target);
                    string p = slashIdx >= 0 ? target.Substring(slashIdx) : pathAfterCaret;

                    if (IsValidDomain(d) && !string.IsNullOrEmpty(p) && !hasDomainConstraint)
                    {
                        List<string> pList;
                        if (!domainPathRules.TryGetValue(d, out pList))
                        {
                            pList = new List<string>();
                            domainPathRules[d] = pList;
                        }
                        pList.Add(p);
                        if (domainSources != null && !string.IsNullOrEmpty(currentSubscriptionName) && !domainSources.ContainsKey(d))
                        {
                            domainSources[d] = currentSubscriptionName;
                        }
                        return true;
                    }
                }
                else
                {
                    string d = NormalizeDomain(target);
                    if (IsValidDomain(d) && !hasDomainConstraint)
                    {
                        if (isThirdPartyOnly)
                        {
                            thirdPartyBlockedDomains.Add(d);
                        }
                        else
                        {
                            blockedDomains.Add(d);
                            if (domainSources != null && !string.IsNullOrEmpty(currentSubscriptionName))
                            {
                                if (!domainSources.ContainsKey(d) ||
                                    currentSubscriptionName.IndexOf("Privacy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    currentSubscriptionName.IndexOf("Badware", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    domainSources[d] = currentSubscriptionName;
                                }
                            }
                        }
                        return true;
                    }
                }
            }
            else if ((line.StartsWith("/") || line.StartsWith("_") || line.StartsWith("-") || line.StartsWith("?") || line.StartsWith("&") || line.Contains("advertising") || line.Contains("_ads_") || line.Contains("banner")) && IsSafeGlobalPath(line))
            {
                globalBlockedPaths.Add(line);
                return true;
            }
            else if (!line.Contains("/"))
            {
                string d = NormalizeDomain(line);
                if (IsValidDomain(d) && !hasDomainConstraint)
                {
                    if (isThirdPartyOnly)
                    {
                        thirdPartyBlockedDomains.Add(d);
                    }
                    else
                    {
                        blockedDomains.Add(d);
                        if (domainSources != null && !string.IsNullOrEmpty(currentSubscriptionName))
                        {
                            if (!domainSources.ContainsKey(d) ||
                                currentSubscriptionName.IndexOf("Privacy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                currentSubscriptionName.IndexOf("Badware", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                domainSources[d] = currentSubscriptionName;
                            }
                        }
                    }
                    return true;
                }
            }

            return false;
        }

        private static void AddDomainException(
            Dictionary<string, List<ExceptionRule>> domainExceptions,
            string domain,
            string path,
            string[] sourceDomains,
            bool firstPartyOnly,
            bool isDocument)
        {
            if (string.IsNullOrEmpty(domain)) return;
            List<ExceptionRule> list;
            if (!domainExceptions.TryGetValue(domain, out list))
            {
                list = new List<ExceptionRule>();
                domainExceptions[domain] = list;
            }
            list.Add(new ExceptionRule
            {
                Path = path,
                SourceDomains = sourceDomains,
                FirstPartyOnly = firstPartyOnly,
                IsDocument = isDocument
            });
        }

        public static bool IsValidDomain(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return false;
            if (domain.StartsWith("www.")) domain = domain.Substring(4);
            if (domain.Contains("/") || domain.Contains("*") || domain.Contains("?") ||
                domain.Contains(":") || domain.Contains("@") || domain.Contains("\\") ||
                domain.Contains("#") || domain.Contains("&"))
            {
                return false;
            }

            int dot = domain.IndexOf('.');
            if (dot <= 0 || dot == domain.Length - 1) return false;

            string[] parts = domain.Split('.');
            if (parts.Length < 2) return false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) return false;
            }

            string tld = parts[parts.Length - 1];
            if (tld.Length < 2) return false;

            return true;
        }

        public static bool IsSafeGlobalPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 4) return false;
            string lower = path.ToLowerInvariant();

            // Disallow common website routes that must never be blocked globally
            if (lower.StartsWith("/watch") || lower.StartsWith("/playlist") || lower.StartsWith("/search") ||
                lower.StartsWith("/view") || lower.StartsWith("/content") || lower.StartsWith("/article") ||
                lower.StartsWith("/video") || lower.StartsWith("/image") || lower.StartsWith("/media") ||
                lower.StartsWith("/user") || lower.StartsWith("/channel") || lower.StartsWith("/beacon") ||
                lower.StartsWith("/feed") || lower.StartsWith("/live") || lower.StartsWith("/browse") ||
                lower.StartsWith("/admin") || lower.StartsWith("/download") || lower.StartsWith("/upload"))
            {
                return false;
            }

            // Must contain ad, advert, advertising, tracking, or banner terms (with safe boundaries for "ad" to prevent false positives)
            bool hasAdOrAdvertTerm = lower.Contains("/ad/") || lower.Contains("/ads/") ||
                                     lower.Contains("_ad_") || lower.Contains("_ads_") ||
                                     lower.Contains("-ad-") || lower.Contains("-ads-") ||
                                     lower.Contains("/ad_") || lower.Contains("/ads_") ||
                                     lower.Contains("/ad-") || lower.Contains("/ads-") ||
                                     lower.Contains("pagead") || lower.Contains("adserver") ||
                                     lower.Contains("adsystem") || lower.Contains("partnerad") ||
                                     lower.Contains("adformat") || lower.Contains("advert");

            return hasAdOrAdvertTerm ||
                   lower.Contains("banner") || lower.Contains("pixel") ||
                   lower.Contains("telemetry") || lower.Contains("track") || lower.Contains("sponsor") ||
                   lower.Contains("affiliate") || lower.Contains("popup") || lower.Contains("popunder");
        }

        public static bool IsValidGenericCosmeticSelector(string selector)
        {
            if (string.IsNullOrEmpty(selector)) return false;
            // Class, ID, or attribute selector
            if (selector.StartsWith("#") || selector.StartsWith(".") || selector.StartsWith("[")) return true;
            // Allow tag-qualified or custom element selectors (e.g. object[...], embed[...], img[...], ytd-...)
            if (char.IsLetter(selector[0]))
            {
                // Custom web components like ytd-promoted-sparkles-web-renderer
                if (selector.Contains("-") && !selector.Contains(" ") && !selector.Contains(">") && !selector.Contains("["))
                {
                    return true;
                }
                // Tag-qualified selectors like object[type*="flash"], embed[src*=".swf"], img[src*="ad"]
                if (selector.Contains("[") || selector.Contains(".") || selector.Contains("#") || selector.Contains(">") || selector.Contains(":"))
                {
                    return true;
                }
                // Specifically allow legacy Flash object/embed elements
                if (string.Equals(selector, "object", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(selector, "embed", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void ParseDirectRules(
            string content,
            HashSet<string> blockedDomains,
            HashSet<string> thirdPartyBlockedDomains,
            HashSet<string> whitelistedDomains,
            Dictionary<string, List<string>> domainPathRules,
            Dictionary<string, List<ExceptionRule>> domainExceptions,
            List<string> globalBlockedPaths,
            List<string> globalExceptionPatterns,
            HashSet<string> genericCosmeticSelectors,
            Dictionary<string, List<string>> domainCosmeticRules,
            HashSet<string> genericHideDisabledDomains)
        {
            if (string.IsNullOrEmpty(content)) return;
            string[] lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                ParseFilterLine(lines[i], blockedDomains, thirdPartyBlockedDomains, whitelistedDomains,
                    domainPathRules, domainExceptions, globalBlockedPaths, globalExceptionPatterns,
                    genericCosmeticSelectors, domainCosmeticRules, genericHideDisabledDomains);
            }
        }

        private static void ParseDirectRules(
            string content,
            HashSet<string> blockedDomains,
            List<string> blockedPaths,
            HashSet<string> cosmeticSelectors)
        {
            HashSet<string> thirdParty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> whitelisted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> domainPaths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<ExceptionRule>> domainEx = new Dictionary<string, List<ExceptionRule>>(StringComparer.OrdinalIgnoreCase);
            List<string> globalEx = new List<string>();
            Dictionary<string, List<string>> domainCos = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> gHide = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ParseDirectRules(content, blockedDomains, thirdParty, whitelisted, domainPaths, domainEx,
                blockedPaths, globalEx, cosmeticSelectors, domainCos, gHide);
        }

        public List<FilterSubscription> GetSubscriptions()
        {
            lock (_stateLock)
            {
                List<FilterSubscription> list = new List<FilterSubscription>();
                for (int i = 0; i < _subscriptions.Count; i++)
                {
                    FilterSubscription s = _subscriptions[i];
                    list.Add(new FilterSubscription
                    {
                        Id = s.Id,
                        Name = s.Name,
                        Url = s.Url,
                        FallbackUrls = s.FallbackUrls != null ? new List<string>(s.FallbackUrls) : new List<string>(),
                        Group = s.Group,
                        IsEnabled = s.IsEnabled,
                        IsCustom = s.IsCustom,
                        LocalFileName = s.LocalFileName,
                        LastUpdatedUtc = s.LastUpdatedUtc,
                        RuleCount = s.RuleCount,
                        LastStatus = s.LastStatus,
                        ETag = s.ETag,
                        LastModified = s.LastModified
                    });
                }
                return list;
            }
        }

        public void ToggleSubscription(string id, bool isEnabled)
        {
            lock (_stateLock)
            {
                for (int i = 0; i < _subscriptions.Count; i++)
                {
                    if (_subscriptions[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    {
                        _subscriptions[i].IsEnabled = isEnabled;
                        break;
                    }
                }
                SaveSubscriptionsUnsafe();
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                CompileRulesInternal();
                EventHandler handler = FilterListsUpdated;
                if (handler != null) handler(this, EventArgs.Empty);
            });
        }

        public bool AddCustomSubscription(string name, string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string id = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string localFileName = id + ".txt";

            FilterSubscription sub = new FilterSubscription(id, name ?? "Custom List", url, "Custom", localFileName, true);
            lock (_stateLock)
            {
                _subscriptions.Add(sub);
                SaveSubscriptionsUnsafe();
            }

            CheckForUpdatesInBackground(true);
            return true;
        }

        public bool RemoveSubscription(string id)
        {
            bool removed = false;
            string localFileName = null;
            lock (_stateLock)
            {
                for (int i = 0; i < _subscriptions.Count; i++)
                {
                    if (_subscriptions[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    {
                        localFileName = _subscriptions[i].LocalFileName;
                        _subscriptions.RemoveAt(i);
                        removed = true;
                        break;
                    }
                }
                if (removed)
                {
                    SaveSubscriptionsUnsafe();
                }
            }

            if (removed)
            {
                if (!string.IsNullOrEmpty(localFileName))
                {
                    try
                    {
                        string path = Path.Combine(GetFiltersDirectory(), localFileName);
                        if (File.Exists(path)) File.Delete(path);
                    }
                    catch
                    {
                    }
                }

                ThreadPool.QueueUserWorkItem(delegate
                {
                    CompileRulesInternal();
                    EventHandler handler = FilterListsUpdated;
                    if (handler != null) handler(this, EventArgs.Empty);
                });
            }

            return removed;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static string NormalizeDomain(string urlOrDomain)
        {
            return UrlHelper.NormalizeDomain(urlOrDomain);
        }

        public bool IsDomainWhitelisted(string domainOrUrl)
        {
            if (string.IsNullOrEmpty(domainOrUrl)) return false;

            CompiledRules rules = _compiledRules;
            if (rules != null && rules.IsDomainWhitelisted(domainOrUrl))
            {
                return true;
            }

            // Also check temporary session bypasses in WebErrorBypassManager
            if (WebErrorBypassManager.IsUrlAllowed(domainOrUrl))
            {
                return true;
            }

            return false;
        }

        public string GetStatusSuffix(string currentUrl)
        {
            if (!_isEnabled)
            {
                return "(disabled)";
            }

            bool isInternal = InternalPageHelper.IsInternalPage(currentUrl);

            if (!isInternal)
            {
                string domain = NormalizeDomain(currentUrl);
                if (!string.IsNullOrEmpty(domain) && IsDomainWhitelisted(domain))
                {
                    return "(disabled for this site)";
                }
            }

            return "(enabled)";
        }

        public bool WhitelistDomain(string domainOrUrl)
        {
            string domain = NormalizeDomain(domainOrUrl);
            if (string.IsNullOrEmpty(domain)) return false;

            bool added = false;
            lock (_stateLock)
            {
                added = _whitelistedDomains.Add(domain);
                if (added)
                {
                    SaveWhitelistUnsafe();
                }
            }

            if (added)
            {
                CompileRulesInternal();
                EventHandler<string> handler = WhitelistChanged;
                if (handler != null) handler(this, domain);
            }
            return added;
        }

        public bool UnwhitelistDomain(string domainOrUrl)
        {
            string domain = NormalizeDomain(domainOrUrl);
            if (string.IsNullOrEmpty(domain)) return false;

            bool removed = false;
            lock (_stateLock)
            {
                // 1. Remove exact domain
                if (_whitelistedDomains.Remove(domain))
                {
                    removed = true;
                }

                // 2. Remove any parent domain that was causing this domain to be whitelisted
                string cur = domain;
                while (!string.IsNullOrEmpty(cur))
                {
                    int dotIdx = cur.IndexOf('.');
                    if (dotIdx <= 0 || dotIdx >= cur.Length - 1) break;
                    string parent = cur.Substring(dotIdx + 1);
                    if (!parent.Contains(".")) break;
                    if (_whitelistedDomains.Remove(parent))
                    {
                        removed = true;
                    }
                    cur = parent;
                }

                // 3. Remove any subdomains of this domain
                List<string> subdomainsToRemove = new List<string>();
                foreach (string d in _whitelistedDomains)
                {
                    if (d.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                    {
                        subdomainsToRemove.Add(d);
                    }
                }
                for (int i = 0; i < subdomainsToRemove.Count; i++)
                {
                    if (_whitelistedDomains.Remove(subdomainsToRemove[i]))
                    {
                        removed = true;
                    }
                }

                if (removed)
                {
                    SaveWhitelistUnsafe();
                }
            }

            // 4. Always synchronize with WebErrorBypassManager to revoke any temporary bypasses
            bool bypassRemoved = WebErrorBypassManager.RemoveDomain(domain);
            if (bypassRemoved)
            {
                removed = true;
            }

            if (removed)
            {
                CompileRulesInternal();
                EventHandler<string> handler = WhitelistChanged;
                if (handler != null) handler(this, domain);
            }
            return removed;
        }

        public bool ToggleDomain(string domainOrUrl)
        {
            if (IsDomainWhitelisted(domainOrUrl))
            {
                UnwhitelistDomain(domainOrUrl);
                return false;
            }
            else
            {
                WhitelistDomain(domainOrUrl);
                return true;
            }
        }

        public List<string> GetWhitelistedDomains()
        {
            HashSet<string> all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            lock (_stateLock)
            {
                foreach (string d in _whitelistedDomains)
                {
                    all.Add(d);
                }
            }

            // Also include active temporary bypass domains so users can see and manage all exceptions
            List<string> tempAllowed = WebErrorBypassManager.GetAllowedDomains();
            for (int i = 0; i < tempAllowed.Count; i++)
            {
                if (!string.IsNullOrEmpty(tempAllowed[i]))
                {
                    all.Add(tempAllowed[i]);
                }
            }

            List<string> list = new List<string>(all);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public void ClearWhitelist()
        {
            lock (_stateLock)
            {
                _whitelistedDomains.Clear();
                SaveWhitelistUnsafe();
            }

            WebErrorBypassManager.Clear();

            CompileRulesInternal();
            EventHandler<string> handler = WhitelistChanged;
            if (handler != null) handler(this, string.Empty);
        }

        private void LoadCustomFilters()
        {
            try
            {
                List<CustomFilterRule> loaded = StorageHelper.LoadList<CustomFilterRule>(CustomFiltersFileName);
                if (loaded != null)
                {
                    for (int i = 0; i < loaded.Count; i++)
                    {
                        if (loaded[i] != null && !string.IsNullOrEmpty(loaded[i].Selector))
                        {
                            _customFilters.Add(loaded[i]);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void SaveCustomFiltersUnsafe()
        {
            try
            {
                StorageHelper.SaveList<CustomFilterRule>(CustomFiltersFileName, _customFilters);
            }
            catch
            {
            }
        }

        public int CustomFiltersCount
        {
            get
            {
                lock (_stateLock)
                {
                    return _customFilters.Count;
                }
            }
        }

        public List<CustomFilterRule> GetCustomFilters()
        {
            lock (_stateLock)
            {
                return new List<CustomFilterRule>(_customFilters);
            }
        }

        public bool AddCustomFilter(string domain, string selector)
        {
            if (string.IsNullOrEmpty(selector)) return false;
            string s = selector.Trim();
            if (s.StartsWith("##", StringComparison.Ordinal))
            {
                s = s.Substring(2).Trim();
            }
            if (string.IsNullOrEmpty(s)) return false;

            string normDomain = !string.IsNullOrEmpty(domain) ? NormalizeDomain(domain) : string.Empty;

            bool added = false;
            lock (_stateLock)
            {
                for (int i = 0; i < _customFilters.Count; i++)
                {
                    CustomFilterRule r = _customFilters[i];
                    if (string.Equals(r.Domain, normDomain, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.Selector, s, StringComparison.OrdinalIgnoreCase))
                    {
                        return false; // Already present
                    }
                }

                CustomFilterRule rule = new CustomFilterRule(normDomain, s);
                _customFilters.Add(rule);
                SaveCustomFiltersUnsafe();
                added = true;
            }

            if (added)
            {
                CompileRulesInternal();
                EventHandler handler = CustomFiltersChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            return added;
        }

        public bool RemoveCustomFilter(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            bool removed = false;
            lock (_stateLock)
            {
                for (int i = _customFilters.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_customFilters[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        _customFilters.RemoveAt(i);
                        removed = true;
                        break;
                    }
                }
                if (removed)
                {
                    SaveCustomFiltersUnsafe();
                }
            }

            if (removed)
            {
                CompileRulesInternal();
                EventHandler handler = CustomFiltersChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            return removed;
        }

        public void ClearCustomFilters()
        {
            lock (_stateLock)
            {
                _customFilters.Clear();
                SaveCustomFiltersUnsafe();
            }

            CompileRulesInternal();
            EventHandler handler = CustomFiltersChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// Ultra-high performance, lock-free ad and tracker detection.
        /// Zero locks during request evaluation for minimal CPU usage.
        /// </summary>
        public bool ShouldBlock(string requestUrl, string pageUrl, int resourceType, out string matchedRule)
        {
            matchedRule = null;
            if (!_isEnabled) return false;

            CompiledRules rules = _compiledRules;
            if (rules == null) return false;

            return rules.ShouldBlock(requestUrl, pageUrl, resourceType, out matchedRule);
        }

        /// <summary>
        /// Evaluates whether a top-level main frame navigation should be strictly blocked
        /// due to matching security, malware, badware, or tracking blocklists.
        /// </summary>
        public bool ShouldBlockMainFrame(string requestUrl, out string matchedRule)
        {
            string matchedListName;
            return ShouldBlockMainFrame(requestUrl, out matchedRule, out matchedListName);
        }

        public bool ShouldBlockMainFrame(string requestUrl, out string matchedRule, out string matchedListName)
        {
            matchedRule = null;
            matchedListName = null;
            if (!_isEnabled) return false;

            CompiledRules rules = _compiledRules;
            if (rules == null) return false;

            return rules.ShouldBlockMainFrame(requestUrl, out matchedRule, out matchedListName);
        }

        public void RecordBlocked(string domain)
        {
            System.Threading.Interlocked.Increment(ref _totalBlockedCount);
            System.Threading.Interlocked.Increment(ref _sessionBlockedCount);

            EventHandler handler = StatsUpdated;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void FlushStats()
        {
            lock (_stateLock)
            {
                SaveStatsUnsafe();
            }
        }

        private void SaveSubscriptionsUnsafe()
        {
            StorageHelper.SaveList(SubscriptionsFileName, _subscriptions);
        }

        private void SaveWhitelistUnsafe()
        {
            StorageHelper.SaveList(WhitelistFileName, new List<string>(_whitelistedDomains));
        }

        private void SaveStatsUnsafe()
        {
            AdBlockStatsModel model = new AdBlockStatsModel();
            model.IsEnabled = _isEnabled;
            model.TotalBlockedCount = _totalBlockedCount;
            StorageHelper.SaveObject(StatsFileName, model);
        }

        public bool IsYouTubeUrl(string url)
        {
            return AdBlockYTShield.IsYouTubeDomain(url);
        }

        public string GetYouTubeShieldScript()
        {
            return AdBlockYTShield.GetAdShieldScript();
        }

        public string GetYouTubeBlockScript()
        {
            return AdBlockYTShield.GetAdShieldScript();
        }

        /// <summary>
        /// Generates the single, pre-compiled CSS injection scriptlet.
        /// Native browser engine executes cosmetic hiding with 0% C# CPU overhead.
        /// </summary>
        public string GetCosmeticHidingScript(string pageUrl)
        {
            if (!_isEnabled) return string.Empty;
            if (!string.IsNullOrEmpty(pageUrl) && IsDomainWhitelisted(pageUrl)) return string.Empty;

            CompiledRules rules = _compiledRules;
            string pageCss = rules != null ? rules.GetCosmeticCssForPage(pageUrl) : null;

            // Baseline cosmetic hiding rules for legacy Flash banners and intrusive ad elements
            const string baselineFlashAdCss = "object[type*=\\\"flash\\\"],embed[type*=\\\"flash\\\"],object[type*=\\\"shockwave\\\"],embed[type*=\\\"shockwave\\\"],object[data*=\\\".swf\\\"],embed[src*=\\\".swf\\\"],[type*=\\\"shockwave-flash\\\"],[data*=\\\"pr_advertising_ads_banner\\\"],[src*=\\\"pr_advertising_ads_banner\\\"],[data*=\\\"_ads_banner\\\"],[src*=\\\"_ads_banner\\\"],[data*=\\\"/banners/\\\"],[src*=\\\"/banners/\\\"],img[src*=\\\"pr_advertising\\\"],img[src*=\\\"_ads_banner\\\"],img[src*=\\\"/banners/\\\"],object[data*=\\\"advert\\\"],embed[src*=\\\"advert\\\"],object[data*=\\\"advertising\\\"],embed[src*=\\\"advertising\\\"],[data*=\\\"advert\\\"],[src*=\\\"advert\\\"],[data*=\\\"advertising\\\"],[src*=\\\"advertising\\\"],img[src*=\\\"advert\\\"],img[src*=\\\"advertising\\\"],iframe[src*=\\\"advert\\\"],iframe[src*=\\\"advertising\\\"],[data-ads],[data-ad-unit],.advert,.advertising,.advertisement,.advertisment,[class*=\\\"advertis\\\"],[id*=\\\"advertis\\\"],[class*=\\\"advert\\\"],[id*=\\\"advert\\\"]";

            string finalCss;
            if (string.IsNullOrEmpty(pageCss))
            {
                finalCss = baselineFlashAdCss;
            }
            else
            {
                finalCss = baselineFlashAdCss + "," + pageCss;
            }

            return "(function(){try{" +
                "if(document.getElementById('cnet-ublock-cosmetic'))return;" +
                "var s=document.createElement('style');" +
                "s.id='cnet-ublock-cosmetic';" +
                "s.type='text/css';" +
                "s.textContent=\"" + finalCss + "{display:none!important;visibility:hidden!important;height:0!important;width:0!important;max-height:0!important;max-width:0!important;opacity:0!important;pointer-events:none!important;}\";" +
                "var t=document.head||document.documentElement;" +
                "if(t){t.appendChild(s);}" +
                "else{document.addEventListener('DOMContentLoaded',function(){(document.head||document.documentElement).appendChild(s);});}" +
                "function cBlocked(){" +
                "var elms=document.querySelectorAll('object,embed,[type*=\"flash\"],[type*=\"shockwave\"],[data*=\".swf\"],[src*=\".swf\"],[data*=\"pr_advertising\"],[src*=\"pr_advertising\"],[data*=\"_ads_banner\"],[src*=\"_ads_banner\"],[data*=\"advert\"],[src*=\"advert\"],[data*=\"advertising\"],[src*=\"advertising\"],[data-ads],.advert,.advertising,.advertisement,.advertisment,[class*=\"advertis\"],[id*=\"advertis\"],[class*=\"advert\"],[id*=\"advert\"]');" +
                "for(var i=0;i<elms.length;i++){" +
                "var el=elms[i];" +
                "el.style.setProperty('display','none','important');" +
                "el.style.setProperty('height','0px','important');" +
                "el.style.setProperty('width','0px','important');" +
                "var p=el.parentElement;" +
                "if(p&&(p.classList.contains('include')||p.classList.contains('includeWrapper'))){" +
                "p.style.setProperty('display','none','important');" +
                "p.style.setProperty('height','0px','important');" +
                "p.style.setProperty('width','0px','important');" +
                "}" +
                "}" +
                "}" +
                "cBlocked();" +
                "document.addEventListener('error',function(e){" +
                "var tg=e&&e.target;" +
                "if(!tg||!tg.tagName)return;" +
                "var tag=tg.tagName.toLowerCase();" +
                "if(tag==='object'||tag==='embed'||tag==='img'||tag==='iframe'){" +
                "tg.style.setProperty('display','none','important');" +
                "tg.style.setProperty('height','0px','important');" +
                "tg.style.setProperty('width','0px','important');" +
                "var p=tg.parentElement;" +
                "if(p&&(p.classList.contains('include')||p.classList.contains('includeWrapper'))){" +
                "p.style.setProperty('display','none','important');" +
                "p.style.setProperty('height','0px','important');" +
                "p.style.setProperty('width','0px','important');" +
                "}" +
                "}" +
                "},true);" +
                "if(window.MutationObserver){" +
                "var obs=new MutationObserver(function(){cBlocked();});" +
                "var root=document.documentElement||document.body;" +
                "if(root){obs.observe(root,{childList:true,subtree:true});}" +
                "else{document.addEventListener('DOMContentLoaded',function(){var r2=document.documentElement||document.body;if(r2)obs.observe(r2,{childList:true,subtree:true});});}" +
                "}" +
                "}catch(e){}})();";
        }

        public void CheckForUpdatesInBackground(bool force)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                PerformFilterListUpdate(force);
            });
        }

        public void CheckForUpdatesInBackground()
        {
            CheckForUpdatesInBackground(false);
        }

        private void PerformFilterListUpdate(bool force)
        {
            EnsureSecurityProtocol();

            lock (_stateLock)
            {
                if (_isUpdating) return;

                bool hasUnupdatedSubs = false;
                for (int i = 0; i < _subscriptions.Count; i++)
                {
                    if (_subscriptions[i].IsEnabled && _subscriptions[i].LastUpdatedUtc == DateTime.MinValue)
                    {
                        hasUnupdatedSubs = true;
                        break;
                    }
                }

                if (!force && !hasUnupdatedSubs && _lastUpdatedUtc != DateTime.MinValue && (DateTime.UtcNow - _lastUpdatedUtc).TotalHours < 24.0)
                {
                    return;
                }
                _isUpdating = true;
            }

            string filtersDir = GetFiltersDirectory();
            if (!Directory.Exists(filtersDir))
            {
                try { Directory.CreateDirectory(filtersDir); } catch { }
            }

            List<FilterSubscription> subsToUpdate;
            lock (_stateLock)
            {
                subsToUpdate = new List<FilterSubscription>(_subscriptions);
            }

            for (int i = 0; i < subsToUpdate.Count; i++)
            {
                FilterSubscription sub = subsToUpdate[i];
                if (!sub.IsEnabled) continue;

                List<string> candidateUrls = sub.GetCandidateUrls();
                if (candidateUrls.Count == 0) continue;

                // Indicate updating state and notify UI
                sub.LastStatus = AppStrings.AdBlock.StatusUpdating;
                NotifyFilterListsUpdated();

                bool updateSuccess = false;

                for (int urlIdx = 0; urlIdx < candidateUrls.Count; urlIdx++)
                {
                    string targetUrl = candidateUrls[urlIdx];
                    try
                    {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(targetUrl);
                        req.Timeout = 15000;
                        req.ReadWriteTimeout = 15000;
                        req.UserAgent = AppStrings.UserAgent;
                        req.Accept = "text/plain, text/x-adblock, */*;q=0.8";
                        req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                        // Conditional headers for zero redundant downloads
                        if (!force && sub.LastUpdatedUtc != DateTime.MinValue)
                        {
                            if (!string.IsNullOrEmpty(sub.ETag))
                            {
                                req.Headers[HttpRequestHeader.IfNoneMatch] = sub.ETag;
                            }
                            if (!string.IsNullOrEmpty(sub.LastModified))
                            {
                                DateTime dt;
                                if (DateTime.TryParse(sub.LastModified, out dt))
                                {
                                    req.IfModifiedSince = dt;
                                }
                            }
                        }

                        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                        {
                            if (resp.StatusCode == HttpStatusCode.OK)
                            {
                                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                                {
                                    string content = reader.ReadToEnd();
                                    if (!string.IsNullOrEmpty(content) && content.Length > 50)
                                    {
                                        string localPath = Path.Combine(filtersDir, sub.LocalFileName);
                                        File.WriteAllText(localPath, content, Encoding.UTF8);

                                        sub.ETag = resp.Headers[HttpResponseHeader.ETag] ?? string.Empty;
                                        sub.LastModified = resp.Headers[HttpResponseHeader.LastModified] ?? string.Empty;
                                        sub.LastUpdatedUtc = DateTime.UtcNow;
                                        sub.LastStatus = AppStrings.AdBlock.StatusUpdated;

                                        int count = CountFilterRulesInFile(localPath);
                                        if (count > 0) sub.RuleCount = count;

                                        updateSuccess = true;
                                        break; // Successfully updated from this mirror!
                                    }
                                }
                            }
                        }
                    }
                    catch (WebException wex)
                    {
                        HttpWebResponse errorResp = wex.Response as HttpWebResponse;
                        if (errorResp != null && errorResp.StatusCode == HttpStatusCode.NotModified)
                        {
                            // 304 Not Modified: List is up to date, 0 CPU or bandwidth wasted
                            sub.LastUpdatedUtc = DateTime.UtcNow;
                            sub.LastStatus = AppStrings.AdBlock.StatusUpToDate;
                            updateSuccess = true;
                            break;
                        }
                        else
                        {
                            // Fallback to next CDN mirror
                            continue;
                        }
                    }
                    catch
                    {
                        // Fallback to next CDN mirror
                        continue;
                    }
                }

                if (!updateSuccess)
                {
                    sub.LastStatus = AppStrings.AdBlock.StatusCheckFailed;
                }

                // Incrementally save updated state and notify UI
                lock (_stateLock)
                {
                    SaveSubscriptionsUnsafe();
                }
                NotifyFilterListsUpdated();
            }

            lock (_stateLock)
            {
                _lastUpdatedUtc = DateTime.UtcNow;
                SaveSubscriptionsUnsafe();
                _isUpdating = false;
            }

            // Always re-compile rules after update pass
            CompileRulesInternal();

            NotifyFilterListsUpdated();
        }

        private void NotifyFilterListsUpdated()
        {
            EventHandler handler = FilterListsUpdated;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); } catch { }
            }
        }

        private static int CountFilterRulesInFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return 0;
            int count = 0;
            try
            {
                using (StreamReader reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length > 0 && line[0] != '!' && line[0] != '[')
                        {
                            count++;
                        }
                    }
                }
            }
            catch { }
            return count;
        }
    }
}
