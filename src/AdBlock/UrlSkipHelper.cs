using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace wb.AdBlock
{
    /// <summary>
    /// Represents a parsed uBlock Origin $urlskip directive rule.
    /// Extracts a destination URL from tracking/redirect URLs to skip intermediate network trackers.
    /// </summary>
    public class UrlSkipRule
    {
        public string Hostname { get; set; }
        public string PathPrefix { get; set; }
        public string[] Steps { get; set; }

        public UrlSkipRule(string hostname, string pathPrefix, string[] steps)
        {
            Hostname = hostname;
            PathPrefix = pathPrefix;
            Steps = steps;
        }
    }

    /// <summary>
    /// Helper system implementing uBlock Origin's urlskip engine and intelligent redirect unwrapping.
    /// Ref: https://github.com/gorhill/uBlock/blob/master/src/js/urlskip.js
    /// </summary>
    public static class UrlSkipHelper
    {
        private static readonly List<UrlSkipRule> _rules = new List<UrlSkipRule>();
        private static readonly object _ruleLock = new object();

        static UrlSkipHelper()
        {
            // Seed default known uBlock urlskip rules
            RegisterRule("click.redditmail.com", "/CL0/", new string[] {
                @"/\/CL0\/(http.*?)\/\d\/[a-f0-9-]+\//",
                "-uricomponent"
            });
            RegisterRule("out.reddit.com", null, new string[] { "?url", "-uricomponent" });
            RegisterRule("l.facebook.com", null, new string[] { "?u", "-uricomponent" });
            RegisterRule("lm.facebook.com", null, new string[] { "?u", "-uricomponent" });
            RegisterRule("t.co", null, new string[] { "?url" });
            RegisterRule("www.google.com", "/url", new string[] { "?q", "-uricomponent" });
            RegisterRule("safelinks.protection.outlook.com", null, new string[] { "?url", "-uricomponent" });
        }

        public static void ClearRules()
        {
            lock (_ruleLock)
            {
                _rules.Clear();
                // Re-seed essential defaults
                _rules.Add(new UrlSkipRule("click.redditmail.com", "/CL0/", new string[] {
                    @"/\/CL0\/(http.*?)\/\d\/[a-f0-9-]+\//",
                    "-uricomponent"
                }));
                _rules.Add(new UrlSkipRule("out.reddit.com", null, new string[] { "?url", "-uricomponent" }));
                _rules.Add(new UrlSkipRule("www.google.com", "/url", new string[] { "?q", "-uricomponent" }));
                _rules.Add(new UrlSkipRule("safelinks.protection.outlook.com", null, new string[] { "?url", "-uricomponent" }));
            }
        }

        public static void RegisterRule(string hostname, string pathPrefix, string[] steps)
        {
            if (string.IsNullOrEmpty(hostname) || steps == null || steps.Length == 0) return;
            string normHost = hostname.Trim().ToLowerInvariant();
            lock (_ruleLock)
            {
                _rules.Add(new UrlSkipRule(normHost, pathPrefix, steps));
            }
        }

        /// <summary>
        /// Attempts to unwrap a tracking or intermediate URL into its destination URL.
        /// Returns the clean final URL, or null if no redirect could be extracted.
        /// </summary>
        public static string TryUnwrap(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            Uri sourceUri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out sourceUri))
            {
                return null;
            }

            string sourceHost = sourceUri.Host.ToLowerInvariant();

            // 1. Check against registered $urlskip rules
            lock (_ruleLock)
            {
                for (int i = 0; i < _rules.Count; i++)
                {
                    UrlSkipRule rule = _rules[i];
                    if (MatchesHost(sourceHost, rule.Hostname))
                    {
                        if (string.IsNullOrEmpty(rule.PathPrefix) ||
                            sourceUri.AbsolutePath.IndexOf(rule.PathPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string unwrapped = ExecuteSteps(url, rule.Steps);
                            if (IsValidDestination(unwrapped, sourceHost))
                            {
                                return unwrapped;
                            }
                        }
                    }
                }
            }

            // 2. Smart heuristic extraction (query parameters and path patterns)
            string heuristic = SmartHeuristicUnwrap(sourceUri);
            if (IsValidDestination(heuristic, sourceHost))
            {
                return heuristic;
            }

            return null;
        }

        /// <summary>
        /// Executes a series of uBlock urlskip transformation steps.
        /// </summary>
        public static string ExecuteSteps(string inputUrl, string[] steps)
        {
            if (string.IsNullOrEmpty(inputUrl) || steps == null) return null;

            string current = inputUrl;

            for (int i = 0; i < steps.Length; i++)
            {
                string step = steps[i];
                if (string.IsNullOrEmpty(step)) continue;

                char c0 = step[0];

                // # -> extract hash
                if (c0 == '#' && step == "#")
                {
                    int hashIdx = current.IndexOf('#');
                    current = hashIdx >= 0 ? current.Substring(hashIdx + 1) : "";
                    continue;
                }

                // &i -> extract query parameter at position i (1-based)
                if (c0 == '&' && step.Length > 1)
                {
                    int pos;
                    if (int.TryParse(step.Substring(1), out pos) && pos > 0)
                    {
                        Uri u;
                        if (Uri.TryCreate(current, UriKind.Absolute, out u))
                        {
                            string query = u.Query;
                            if (query.StartsWith("?")) query = query.Substring(1);
                            string[] pairs = query.Split('&');
                            if (pos <= pairs.Length)
                            {
                                string targetPair = pairs[pos - 1];
                                int eqIdx = targetPair.IndexOf('=');
                                current = eqIdx >= 0 ? targetPair.Substring(0, eqIdx) : targetPair;
                                current = Uri.UnescapeDataString(current);
                                continue;
                            }
                        }
                    }
                    return null;
                }

                // +https -> ensure https scheme
                if (c0 == '+' && step == "+https")
                {
                    if (current.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    {
                        current = "https://" + current.Substring(7);
                    }
                    else if (!current.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        current = "https://" + current.TrimStart('/');
                    }
                    continue;
                }

                // Decode directives
                if (c0 == '-')
                {
                    if (step == "-base64")
                    {
                        string decoded = DecodeBase64(current);
                        if (decoded == null) return null;
                        current = decoded;
                        continue;
                    }
                    if (step == "-safebase64")
                    {
                        string safe = current.Replace('-', '+').Replace('_', '/');
                        string decoded = DecodeBase64(safe);
                        if (decoded == null) return null;
                        current = decoded;
                        continue;
                    }
                    if (step == "-uricomponent")
                    {
                        try
                        {
                            current = Uri.UnescapeDataString(current);
                        }
                        catch
                        {
                            return null;
                        }
                        continue;
                    }
                    if (step == "-blocked")
                    {
                        // Directive allowing redirect of blocked resources
                        continue;
                    }
                }

                // /regex/ -> Regex first capture group extraction
                if (c0 == '/' && step.Length >= 2 && step[step.Length - 1] == '/')
                {
                    string pattern = step.Substring(1, step.Length - 2);
                    try
                    {
                        Regex re = new Regex(pattern, RegexOptions.IgnoreCase);
                        Match m = re.Match(current);
                        if (m.Success && m.Groups.Count > 1)
                        {
                            current = m.Groups[1].Value;
                            continue;
                        }
                    }
                    catch
                    {
                        return null;
                    }
                    return null;
                }

                // ?paramName -> Extract named query parameter
                if (c0 == '?' && step.Length > 1)
                {
                    string paramName = step.Substring(1);
                    string val = ExtractQueryParameter(current, paramName);
                    if (val == null) return null;
                    current = val;
                    continue;
                }

                // Unknown directive
                return null;
            }

            return current;
        }

        /// <summary>
        /// Smart fallback detector: searches query parameters and path for embedded HTTP/HTTPS URLs.
        /// </summary>
        private static string SmartHeuristicUnwrap(Uri uri)
        {
            if (uri == null) return null;

            // 1. Check path for embedded URL (e.g. /CL0/https%3A... or /redirect/http...)
            string rawPath = uri.PathAndQuery;
            int cl0Idx = rawPath.IndexOf("/CL0/", StringComparison.OrdinalIgnoreCase);
            if (cl0Idx >= 0)
            {
                string afterCl0 = rawPath.Substring(cl0Idx + 5);
                // Regex capture up to trailing /1/<guid> or next slash block
                Regex cl0Regex = new Regex(@"^(http[^\s/]+|https%3A[^\s/]+)(?:/\d/[a-f0-9-]+/.*)?", RegexOptions.IgnoreCase);
                Match m = cl0Regex.Match(afterCl0);
                if (m.Success && m.Groups.Count > 1)
                {
                    string candidate = Uri.UnescapeDataString(m.Groups[1].Value);
                    if (IsValidHttpUrl(candidate)) return candidate;
                }
                else
                {
                    // Fallback: look for the end of the encoded URL before the next /1/ or /<digit>/
                    int nextSlashNum = Regex.Match(afterCl0, @"/\d/").Index;
                    string slice = nextSlashNum > 0 ? afterCl0.Substring(0, nextSlashNum) : afterCl0;
                    string candidate = Uri.UnescapeDataString(slice);
                    if (IsValidHttpUrl(candidate)) return candidate;
                }
            }

            // 2. Check query string parameters
            string query = uri.Query;
            if (!string.IsNullOrEmpty(query))
            {
                if (query.StartsWith("?")) query = query.Substring(1);
                string[] pairs = query.Split('&');

                // Priority keys
                string[] priorityKeys = new string[] {
                    "url", "redirect", "redirect_url", "redirecturl", "target", "targeturl",
                    "destination", "dest", "to", "link", "q", "u", "r", "next", "continue",
                    "out", "href", "uri", "goods_url", "lurl", "vc_url", "pc", "af_web_dp"
                };

                // Check priority keys first
                for (int p = 0; p < priorityKeys.Length; p++)
                {
                    string key = priorityKeys[p];
                    for (int i = 0; i < pairs.Length; i++)
                    {
                        string pair = pairs[i];
                        int eqIdx = pair.IndexOf('=');
                        if (eqIdx > 0)
                        {
                            string k = pair.Substring(0, eqIdx);
                            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                            {
                                string v = pair.Substring(eqIdx + 1);
                                string candidate = CleanCandidateUrl(v);
                                if (IsValidHttpUrl(candidate)) return candidate;
                            }
                        }
                    }
                }

                // Check any key for embedded URL or Base64 URL
                for (int i = 0; i < pairs.Length; i++)
                {
                    string pair = pairs[i];
                    int eqIdx = pair.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        string v = pair.Substring(eqIdx + 1);
                        string candidate = CleanCandidateUrl(v);
                        if (IsValidHttpUrl(candidate)) return candidate;
                    }
                }
            }

            return null;
        }

        private static string ExtractQueryParameter(string url, string paramName)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(paramName)) return null;

            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u))
            {
                int qIdx = url.IndexOf('?');
                if (qIdx < 0) return null;
                url = "https://dummy.local" + url.Substring(qIdx);
                if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            }

            string query = u.Query;
            if (string.IsNullOrEmpty(query)) return null;
            if (query.StartsWith("?")) query = query.Substring(1);

            string[] pairs = query.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                string pair = pairs[i];
                int eqIdx = pair.IndexOf('=');
                if (eqIdx > 0)
                {
                    string k = pair.Substring(0, eqIdx);
                    if (string.Equals(k, paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        string val = pair.Substring(eqIdx + 1);
                        return val;
                    }
                }
            }

            return null;
        }

        private static string CleanCandidateUrl(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            // Check if base64 encoded
            if (raw.StartsWith("aHR0c", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("ey", StringComparison.OrdinalIgnoreCase))
            {
                string b64 = DecodeBase64(raw);
                if (b64 != null && IsValidHttpUrl(b64))
                {
                    return b64;
                }
            }

            // Check if URL encoded
            if (raw.IndexOf("%2f", StringComparison.OrdinalIgnoreCase) >= 0 ||
                raw.IndexOf("%3a", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    raw = Uri.UnescapeDataString(raw);
                    // Check if double encoded
                    if (raw.IndexOf("%2f", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        raw = Uri.UnescapeDataString(raw);
                    }
                }
                catch
                {
                }
            }

            return raw;
        }

        private static bool IsValidDestination(string candidate, string sourceHost)
        {
            if (string.IsNullOrEmpty(candidate)) return false;

            Uri destUri;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out destUri))
            {
                if (destUri.Scheme == Uri.UriSchemeHttp || destUri.Scheme == Uri.UriSchemeHttps)
                {
                    // Destination must not simply loop back to the same tracking host
                    if (!string.Equals(destUri.Host, sourceHost, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsValidHttpUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            }
            return false;
        }

        private static bool MatchesHost(string requestHost, string ruleHost)
        {
            if (string.Equals(requestHost, ruleHost, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (requestHost.Length > ruleHost.Length &&
                requestHost[requestHost.Length - ruleHost.Length - 1] == '.' &&
                requestHost.EndsWith(ruleHost, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private static string DecodeBase64(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try
            {
                // Pad base64 if needed
                int mod4 = s.Length % 4;
                if (mod4 > 0)
                {
                    s = s.PadRight(s.Length + (4 - mod4), '=');
                }
                byte[] bytes = Convert.FromBase64String(s);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }
    }
}
