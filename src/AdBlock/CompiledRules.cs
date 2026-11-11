using System;
using System.Collections.Generic;

namespace wb.AdBlock
{
    /// <summary>
    /// Represents an unbreak exception rule with optional source domain constraint, path pattern and first-party restriction.
    /// </summary>
    public class ExceptionRule
    {
        public string Path { get; set; }
        public string[] SourceDomains { get; set; }
        public bool FirstPartyOnly { get; set; }
        public bool IsDocument { get; set; }

        public ExceptionRule()
        {
        }

        public ExceptionRule(string path, bool isDocument = false, string[] sourceDomains = null, bool firstPartyOnly = false)
        {
            Path = path;
            IsDocument = isDocument;
            SourceDomains = sourceDomains;
            FirstPartyOnly = firstPartyOnly;
        }

        public bool Matches(string requestDomain, string requestUrl, string pageUrl)
        {
            if (FirstPartyOnly)
            {
                if (CompiledRules.IsThirdParty(requestDomain, pageUrl))
                {
                    return false;
                }
            }

            if (SourceDomains != null && SourceDomains.Length > 0)
            {
                if (string.IsNullOrEmpty(pageUrl)) return false;
                string pageDomain = AdBlockManager.NormalizeDomain(pageUrl);
                if (string.IsNullOrEmpty(pageDomain)) return false;

                bool matched = false;
                for (int i = 0; i < SourceDomains.Length; i++)
                {
                    string src = SourceDomains[i];
                    if (pageDomain.Equals(src, StringComparison.OrdinalIgnoreCase) ||
                        pageDomain.EndsWith("." + src, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched) return false;
            }

            if (!string.IsNullOrEmpty(Path))
            {
                if (requestUrl.IndexOf(Path, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Immutable compiled snapshot of ad-block rules for ultra-high performance lock-free matching.
    /// Thread-safe for simultaneous reads across any number of IO threads with 0% CPU lock contention.
    /// Includes O(1) domain lookup, domain-scoped path matching, unbreak exception rules, third-party
    /// origin checking and scoped cosmetic hiding.
    /// </summary>
    public class CompiledRules
    {
        public HashSet<string> BlockedDomains { get; private set; }
        public HashSet<string> ThirdPartyBlockedDomains { get; private set; }
        public HashSet<string> WhitelistedDomains { get; private set; }
        public Dictionary<string, string> DomainSources { get; private set; }
        public string[] BlockedPathPatterns { get; private set; }
        public string[] GlobalExceptionPatterns { get; private set; }
        public string GenericCosmeticCss { get; private set; }
        public int TotalRules { get; private set; }
        public DateTime CompiledAt { get; private set; }

        private readonly Dictionary<string, string[]> _domainPathRules;
        private readonly Dictionary<string, ExceptionRule[]> _domainExceptions;
        private readonly Dictionary<string, string> _domainCosmeticCss;
        private readonly HashSet<string> _genericHideDisabledDomains;

        /// <summary>
        /// Backward-compatible property returning the global generic cosmetic CSS.
        /// </summary>
        public string CompiledCosmeticCss
        {
            get { return GenericCosmeticCss; }
        }

        public CompiledRules(
            HashSet<string> blockedDomains,
            HashSet<string> whitelistedDomains,
            List<string> blockedPathPatterns,
            HashSet<string> cosmeticSelectors)
            : this(
                blockedDomains,
                null,
                whitelistedDomains,
                null,
                null,
                blockedPathPatterns,
                null,
                cosmeticSelectors,
                null,
                null,
                null)
        {
        }

        public CompiledRules(
            HashSet<string> blockedDomains,
            HashSet<string> thirdPartyBlockedDomains,
            HashSet<string> whitelistedDomains,
            Dictionary<string, List<string>> domainPathRules,
            Dictionary<string, List<ExceptionRule>> domainExceptions,
            List<string> blockedPathPatterns,
            List<string> globalExceptionPatterns,
            HashSet<string> genericCosmeticSelectors,
            Dictionary<string, List<string>> domainCosmeticRules,
            HashSet<string> genericHideDisabledDomains)
            : this(
                blockedDomains,
                thirdPartyBlockedDomains,
                whitelistedDomains,
                domainPathRules,
                domainExceptions,
                blockedPathPatterns,
                globalExceptionPatterns,
                genericCosmeticSelectors,
                domainCosmeticRules,
                genericHideDisabledDomains,
                null)
        {
        }

        public CompiledRules(
            HashSet<string> blockedDomains,
            HashSet<string> thirdPartyBlockedDomains,
            HashSet<string> whitelistedDomains,
            Dictionary<string, List<string>> domainPathRules,
            Dictionary<string, List<ExceptionRule>> domainExceptions,
            List<string> blockedPathPatterns,
            List<string> globalExceptionPatterns,
            HashSet<string> genericCosmeticSelectors,
            Dictionary<string, List<string>> domainCosmeticRules,
            HashSet<string> genericHideDisabledDomains,
            Dictionary<string, string> domainSources)
        {
            BlockedDomains = blockedDomains != null ?
                new HashSet<string>(blockedDomains, StringComparer.OrdinalIgnoreCase) :
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ThirdPartyBlockedDomains = thirdPartyBlockedDomains != null ?
                new HashSet<string>(thirdPartyBlockedDomains, StringComparer.OrdinalIgnoreCase) :
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            WhitelistedDomains = whitelistedDomains != null ?
                new HashSet<string>(whitelistedDomains, StringComparer.OrdinalIgnoreCase) :
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            DomainSources = domainSources != null ?
                new Dictionary<string, string>(domainSources, StringComparer.OrdinalIgnoreCase) :
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            BlockedPathPatterns = blockedPathPatterns != null ?
                blockedPathPatterns.ToArray() :
                new string[0];

            GlobalExceptionPatterns = globalExceptionPatterns != null ?
                globalExceptionPatterns.ToArray() :
                new string[0];

            _domainPathRules = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            int domainPathCount = 0;
            if (domainPathRules != null)
            {
                foreach (KeyValuePair<string, List<string>> kvp in domainPathRules)
                {
                    if (kvp.Value != null && kvp.Value.Count > 0)
                    {
                        _domainPathRules[kvp.Key] = kvp.Value.ToArray();
                        domainPathCount += kvp.Value.Count;
                    }
                }
            }

            _domainExceptions = new Dictionary<string, ExceptionRule[]>(StringComparer.OrdinalIgnoreCase);
            int exCount = 0;
            if (domainExceptions != null)
            {
                foreach (KeyValuePair<string, List<ExceptionRule>> kvp in domainExceptions)
                {
                    if (kvp.Value != null && kvp.Value.Count > 0)
                    {
                        _domainExceptions[kvp.Key] = kvp.Value.ToArray();
                        exCount += kvp.Value.Count;
                    }
                }
            }

            // Pre-compile generic cosmetic CSS
            if (genericCosmeticSelectors != null && genericCosmeticSelectors.Count > 0)
            {
                List<string> list = new List<string>(genericCosmeticSelectors);
                string joined = string.Join(",", list.ToArray());
                GenericCosmeticCss = joined.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");
            }
            else
            {
                GenericCosmeticCss = string.Empty;
            }

            // Pre-compile domain-scoped cosmetic CSS
            _domainCosmeticCss = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int domainCosmeticCount = 0;
            if (domainCosmeticRules != null)
            {
                foreach (KeyValuePair<string, List<string>> kvp in domainCosmeticRules)
                {
                    if (kvp.Value != null && kvp.Value.Count > 0)
                    {
                        string joined = string.Join(",", kvp.Value.ToArray());
                        _domainCosmeticCss[kvp.Key] = joined.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");
                        domainCosmeticCount += kvp.Value.Count;
                    }
                }
            }

            _genericHideDisabledDomains = genericHideDisabledDomains != null ?
                new HashSet<string>(genericHideDisabledDomains, StringComparer.OrdinalIgnoreCase) :
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int genericCosmeticCount = genericCosmeticSelectors != null ? genericCosmeticSelectors.Count : 0;
            TotalRules = BlockedDomains.Count + ThirdPartyBlockedDomains.Count + domainPathCount +
                         BlockedPathPatterns.Length + exCount + genericCosmeticCount + domainCosmeticCount;
            CompiledAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Lock-free domain whitelist test with parent hierarchy walking that safely stops at TLD boundary.
        /// </summary>
        public bool IsDomainWhitelisted(string domainOrUrl)
        {
            if (WhitelistedDomains.Count == 0) return false;
            string domain = AdBlockManager.NormalizeDomain(domainOrUrl);
            if (string.IsNullOrEmpty(domain)) return false;

            string cur = domain;
            while (!string.IsNullOrEmpty(cur))
            {
                if (WhitelistedDomains.Contains(cur)) return true;

                int dotIdx = cur.IndexOf('.');
                if (dotIdx <= 0 || dotIdx >= cur.Length - 1) break;
                string parent = cur.Substring(dotIdx + 1);
                // Stop walking when parent has no dots (TLD boundary)
                if (!parent.Contains(".")) break;
                cur = parent;
            }
            return false;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int GetDomainHierarchy(string domain, string[] levels)
        {
            if (string.IsNullOrEmpty(domain)) return 0;
            levels[0] = domain;
            int count = 1;
            string cur = domain;
            while (count < levels.Length)
            {
                int dotIdx = cur.IndexOf('.');
                if (dotIdx <= 0 || dotIdx >= cur.Length - 1) break;
                string parent = cur.Substring(dotIdx + 1);
                // Stop walking when parent has no dots (TLD boundary)
                if (!parent.Contains(".")) break;
                levels[count++] = parent;
                cur = parent;
            }
            return count;
        }

        public bool ShouldBlockMainFrame(string requestUrl, out string matchedRule)
        {
            string matchedListName;
            return ShouldBlockMainFrame(requestUrl, out matchedRule, out matchedListName);
        }

        /// <summary>
        /// Evaluates whether a top-level main frame navigation should be strictly blocked
        /// due to matching security, malware, badware, or tracking blocklists and identifies
        /// which filter subscription list triggered the block.
        /// </summary>
        public bool ShouldBlockMainFrame(string requestUrl, out string matchedRule, out string matchedListName)
        {
            matchedRule = null;
            matchedListName = null;
            if (string.IsNullOrEmpty(requestUrl)) return false;

            if (IsDomainWhitelisted(requestUrl))
            {
                return false;
            }

            string requestDomain = AdBlockManager.NormalizeDomain(requestUrl);
            if (string.IsNullOrEmpty(requestDomain)) return false;

            string[] reqLevels = new string[8];
            int reqLevelCount = GetDomainHierarchy(requestDomain, reqLevels);

            if (WhitelistedDomains.Count > 0)
            {
                for (int i = 0; i < reqLevelCount; i++)
                {
                    if (WhitelistedDomains.Contains(reqLevels[i])) return false;
                }
            }

            // Exception rules (Unbreak system): only $document exceptions can cancel strict blocking
            if (_domainExceptions.Count > 0)
            {
                for (int i = 0; i < reqLevelCount; i++)
                {
                    ExceptionRule[] rules;
                    if (_domainExceptions.TryGetValue(reqLevels[i], out rules) && rules != null)
                    {
                        for (int r = 0; r < rules.Length; r++)
                        {
                            if (rules[r].IsDocument && rules[r].Matches(reqLevels[i], requestUrl, requestUrl))
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            if (GlobalExceptionPatterns.Length > 0)
            {
                for (int i = 0; i < GlobalExceptionPatterns.Length; i++)
                {
                    if (requestUrl.IndexOf(GlobalExceptionPatterns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return false;
                    }
                }
            }

            // 1. Direct domain match or parent domain match against BlockedDomains
            for (int i = 0; i < reqLevelCount; i++)
            {
                string cur = reqLevels[i];
                if (BlockedDomains.Contains(cur))
                {
                    matchedRule = "||" + cur + "^";
                    string src;
                    if (DomainSources != null && DomainSources.TryGetValue(cur, out src) && !string.IsNullOrEmpty(src))
                    {
                        matchedListName = src;
                    }
                    else
                    {
                        matchedListName = "wb";
                    }
                    return true;
                }
            }

            // 2. Check domain path rules
            string[] pathRules;
            if (_domainPathRules.TryGetValue(requestDomain, out pathRules) && pathRules != null)
            {
                for (int i = 0; i < pathRules.Length; i++)
                {
                    if (requestUrl.IndexOf(pathRules[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matchedRule = "||" + requestDomain + pathRules[i];
                        string src;
                        if (DomainSources != null && DomainSources.TryGetValue(requestDomain, out src) && !string.IsNullOrEmpty(src))
                        {
                            matchedListName = src;
                        }
                        else
                        {
                            matchedListName = "wb";
                        }
                        return true;
                    }
                }
            }

            // 3. Check global path patterns
            for (int i = 0; i < BlockedPathPatterns.Length; i++)
            {
                if (requestUrl.IndexOf(BlockedPathPatterns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchedRule = BlockedPathPatterns[i];
                    matchedListName = "EasyList";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Lock-free, sub-microsecond ad and tracker test with O(1) domain lookup, exception handling,
        /// third-party origin validation and domain-scoped path pattern checks.
        /// </summary>
        public bool ShouldBlock(string requestUrl, string pageUrl, int resourceType, out string matchedRule)
        {
            matchedRule = null;
            if (string.IsNullOrEmpty(requestUrl)) return false;

            // Never block the top-level main frame navigation itself (resourceType 0)
            if (resourceType == 0)
            {
                return false;
            }

            // Check if page domain is whitelisted
            if (!string.IsNullOrEmpty(pageUrl) && IsDomainWhitelisted(pageUrl))
            {
                return false;
            }

            string requestDomain = AdBlockManager.NormalizeDomain(requestUrl);
            if (string.IsNullOrEmpty(requestDomain)) return false;

            string[] reqLevels = new string[8];
            int reqLevelCount = GetDomainHierarchy(requestDomain, reqLevels);

            // Check if request domain itself is whitelisted
            if (WhitelistedDomains.Count > 0)
            {
                for (int i = 0; i < reqLevelCount; i++)
                {
                    if (WhitelistedDomains.Contains(reqLevels[i])) return false;
                }
            }

            // 1. Exception rules (Unbreak system - always takes precedence over block rules)
            if (_domainExceptions.Count > 0)
            {
                for (int i = 0; i < reqLevelCount; i++)
                {
                    ExceptionRule[] rules;
                    if (_domainExceptions.TryGetValue(reqLevels[i], out rules) && rules != null)
                    {
                        for (int r = 0; r < rules.Length; r++)
                        {
                            if (rules[r].Matches(requestDomain, requestUrl, pageUrl))
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            if (GlobalExceptionPatterns.Length > 0)
            {
                for (int i = 0; i < GlobalExceptionPatterns.Length; i++)
                {
                    if (requestUrl.IndexOf(GlobalExceptionPatterns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return false;
                    }
                }
            }

            // 2. Whole Domain Blocking with hierarchical parent walk (stopping at TLD boundary)
            for (int i = 0; i < reqLevelCount; i++)
            {
                string cur = reqLevels[i];
                if (BlockedDomains.Contains(cur))
                {
                    matchedRule = cur;
                    return true;
                }

                if (ThirdPartyBlockedDomains.Contains(cur))
                {
                    if (IsThirdParty(requestDomain, pageUrl))
                    {
                        matchedRule = cur + " (3p)";
                        return true;
                    }
                }
            }

            // 3. Domain-Specific Path Blocking (e.g. google.com/pagead/, youtube.com/api/stats/ads)
            if (_domainPathRules.Count > 0)
            {
                for (int i = 0; i < reqLevelCount; i++)
                {
                    string cur = reqLevels[i];
                    string[] paths;
                    if (_domainPathRules.TryGetValue(cur, out paths) && paths != null)
                    {
                        for (int p = 0; p < paths.Length; p++)
                        {
                            if (requestUrl.IndexOf(paths[p], StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matchedRule = cur + paths[p];
                                return true;
                            }
                        }
                    }
                }
            }

            // 4. Safe Global Path Pattern Checks (generic ad scripts like /pagead.js, /ads.js)
            for (int i = 0; i < BlockedPathPatterns.Length; i++)
            {
                string pattern = BlockedPathPatterns[i];
                if (requestUrl.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchedRule = pattern;
                    return true;
                }
            }

            return false;
        }

        private bool CheckBlockedDomain(string requestDomain, string pageUrl, out string matchedRule)
        {
            matchedRule = null;
            string cur = requestDomain;
            while (!string.IsNullOrEmpty(cur))
            {
                if (BlockedDomains.Contains(cur))
                {
                    matchedRule = cur;
                    return true;
                }

                if (ThirdPartyBlockedDomains.Contains(cur))
                {
                    if (IsThirdParty(requestDomain, pageUrl))
                    {
                        matchedRule = cur + " (3p)";
                        return true;
                    }
                }

                int dotIdx = cur.IndexOf('.');
                if (dotIdx <= 0 || dotIdx >= cur.Length - 1) break;
                string parent = cur.Substring(dotIdx + 1);
                // CRITICAL: Stop walking when parent has no dots (prevents matching TLDs like com, net, org)
                if (!parent.Contains(".")) break;
                cur = parent;
            }
            return false;
        }

        private bool CheckDomainPaths(string requestDomain, string requestUrl, out string matchedRule)
        {
            matchedRule = null;
            string cur = requestDomain;
            while (!string.IsNullOrEmpty(cur))
            {
                string[] paths;
                if (_domainPathRules.TryGetValue(cur, out paths) && paths != null)
                {
                    for (int i = 0; i < paths.Length; i++)
                    {
                        if (requestUrl.IndexOf(paths[i], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matchedRule = cur + paths[i];
                            return true;
                        }
                    }
                }

                int dotIdx = cur.IndexOf('.');
                if (dotIdx <= 0 || dotIdx >= cur.Length - 1) break;
                string parent = cur.Substring(dotIdx + 1);
                if (!parent.Contains(".")) break;
                cur = parent;
            }
            return false;
        }

        private bool CheckDomainExceptions(string requestDomain, string requestUrl, string pageUrl)
        {
            string cur = requestDomain;
            while (!string.IsNullOrEmpty(cur))
            {
                ExceptionRule[] rules;
                if (_domainExceptions.TryGetValue(cur, out rules) && rules != null)
                {
                    for (int i = 0; i < rules.Length; i++)
                    {
                        if (rules[i].Matches(requestDomain, requestUrl, pageUrl))
                        {
                            return true;
                        }
                    }
                }

                int dotIdx = cur.IndexOf('.');
                if (dotIdx <= 0 || dotIdx >= cur.Length - 1) break;
                string parent = cur.Substring(dotIdx + 1);
                if (!parent.Contains(".")) break;
                cur = parent;
            }
            return false;
        }

        private bool CheckDocumentExceptions(string requestDomain, string requestUrl)
        {
            string cur = requestDomain;
            while (!string.IsNullOrEmpty(cur))
            {
                ExceptionRule[] rules;
                if (_domainExceptions.TryGetValue(cur, out rules) && rules != null)
                {
                    for (int i = 0; i < rules.Length; i++)
                    {
                        if (rules[i].IsDocument && rules[i].Matches(cur, requestUrl, requestUrl))
                        {
                            return true;
                        }
                    }
                }

                int dotIdx = cur.IndexOf('.');
                if (dotIdx <= 0 || dotIdx >= cur.Length - 1) break;
                string parent = cur.Substring(dotIdx + 1);
                if (!parent.Contains(".")) break;
                cur = parent;
            }
            return false;
        }

        /// <summary>
        /// Retrieves the target CSS selectors for a specific web page.
        /// Combines generic cosmetic rules with site-specific rules, respecting @@||domain^$generichide.
        /// </summary>
        public string GetCosmeticCssForPage(string pageUrl)
        {
            if (string.IsNullOrEmpty(pageUrl)) return GenericCosmeticCss;
            string pageDomain = AdBlockManager.NormalizeDomain(pageUrl);
            if (string.IsNullOrEmpty(pageDomain)) return GenericCosmeticCss;

            bool allowGeneric = !_genericHideDisabledDomains.Contains(pageDomain);
            string domainCss = null;

            // Check domain and parent domains for domain-scoped CSS
            string cur = pageDomain;
            while (!string.IsNullOrEmpty(cur))
            {
                string specific;
                if (_domainCosmeticCss.TryGetValue(cur, out specific) && !string.IsNullOrEmpty(specific))
                {
                    domainCss = (domainCss == null) ? specific : (domainCss + "," + specific);
                }

                int dot = cur.IndexOf('.');
                if (dot <= 0 || dot >= cur.Length - 1) break;
                string parent = cur.Substring(dot + 1);
                if (!parent.Contains(".")) break;
                cur = parent;
            }

            if (!allowGeneric)
            {
                return domainCss ?? string.Empty;
            }

            if (string.IsNullOrEmpty(domainCss))
            {
                return GenericCosmeticCss;
            }

            if (string.IsNullOrEmpty(GenericCosmeticCss))
            {
                return domainCss;
            }

            return GenericCosmeticCss + "," + domainCss;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool IsThirdParty(string requestDomain, string pageUrl)
        {
            if (string.IsNullOrEmpty(pageUrl)) return true;
            string pageDomain = AdBlockManager.NormalizeDomain(pageUrl);
            if (string.IsNullOrEmpty(pageDomain)) return true;

            string regReq = GetRegistrableDomain(requestDomain);
            string regPage = GetRegistrableDomain(pageDomain);
            return !regReq.Equals(regPage, StringComparison.OrdinalIgnoreCase);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static string GetRegistrableDomain(string host)
        {
            if (string.IsNullOrEmpty(host)) return string.Empty;
            host = host.ToLowerInvariant().Trim();
            if (host.StartsWith("www.", StringComparison.Ordinal) && host.Length > 4)
            {
                host = host.Substring(4);
            }

            int lastDot = host.LastIndexOf('.');
            if (lastDot <= 0) return host;
            int prevDot = host.LastIndexOf('.', lastDot - 1);
            if (prevDot < 0) return host; // 2 parts (e.g. example.com)

            // Check two-part TLD endings (.co.uk, .com.au, .org.uk, .co.jp, etc.)
            int secondLen = lastDot - prevDot - 1;
            bool isTwoPart = false;
            if (secondLen <= 3)
            {
                if (secondLen == 2 && host[prevDot + 1] == 'c' && host[prevDot + 2] == 'o')
                {
                    isTwoPart = true;
                }
                else if (secondLen == 3)
                {
                    char c1 = host[prevDot + 1], c2 = host[prevDot + 2], c3 = host[prevDot + 3];
                    if ((c1 == 'c' && c2 == 'o' && c3 == 'm') ||
                        (c1 == 'o' && c2 == 'r' && c3 == 'g') ||
                        (c1 == 'n' && c2 == 'e' && c3 == 't') ||
                        (c1 == 'e' && c2 == 'd' && c3 == 'u') ||
                        (c1 == 'g' && c2 == 'o' && c3 == 'v'))
                    {
                        isTwoPart = true;
                    }
                }
            }

            if (isTwoPart)
            {
                int thirdDot = host.LastIndexOf('.', prevDot - 1);
                if (thirdDot >= 0)
                {
                    return host.Substring(thirdDot + 1);
                }
                return host;
            }

            return host.Substring(prevDot + 1);
        }
    }
}
