using System;
using System.Collections.Generic;

namespace wb.Core
{
    /// <summary>
    /// Centralized URL and domain manipulation utility providing high-performance,
    /// allocation-conscious domain normalization, scheme detection, YouTube domain matching,
    /// and omnibox/search navigation resolution.
    /// </summary>
    public static class UrlHelper
    {
        /// <summary>
        /// Normalizes a given URL, domain name, or adblock filter pattern into a lowercase host string.
        /// Strips filter rule markers (@@, ||, *.), paths, ports, query strings and leading "www.".
        /// Returns string.Empty for non-web schemes (about:, chrome:, data:, javascript:, file:, wb:).
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static string NormalizeDomain(string urlOrDomain)
        {
            if (string.IsNullOrEmpty(urlOrDomain)) return string.Empty;

            string s = urlOrDomain;
            int len = s.Length;

            // Trim leading whitespace
            int start = 0;
            while (start < len && char.IsWhiteSpace(s[start])) start++;
            if (start >= len) return string.Empty;

            // Trim trailing whitespace
            int end = len - 1;
            while (end > start && char.IsWhiteSpace(s[end])) end--;

            // Fast-path: Check view-source: prefix
            int remLen = end - start + 1;
            if (remLen >= 12 && StartsWithOrdinalIgnoreCase(s, start, "view-source:"))
            {
                start += 12;
                while (start <= end && char.IsWhiteSpace(s[start])) start++;
                if (start > end) return string.Empty;
                remLen = end - start + 1;
            }

            // Reject special non-web schemes
            if (remLen >= 3)
            {
                if (StartsWithOrdinalIgnoreCase(s, start, "about:") ||
                    StartsWithOrdinalIgnoreCase(s, start, "data:") ||
                    StartsWithOrdinalIgnoreCase(s, start, "javascript:") ||
                    StartsWithOrdinalIgnoreCase(s, start, "chrome:") ||
                    StartsWithOrdinalIgnoreCase(s, start, "file:") ||
                    StartsWithOrdinalIgnoreCase(s, start, "wb:"))
                {
                    return string.Empty;
                }
            }

            // Strip filter rule prefixes if entered by user (e.g. @@||domain^ or ||domain^ or *.domain)
            while (start <= end)
            {
                if (end - start + 1 >= 2 && s[start] == '@' && s[start + 1] == '@') { start += 2; continue; }
                if (end - start + 1 >= 2 && s[start] == '|' && s[start + 1] == '|') { start += 2; continue; }
                if (end - start + 1 >= 2 && s[start] == '*' && s[start + 1] == '.') { start += 2; continue; }
                if (s[start] == '.') { start++; continue; }
                if (char.IsWhiteSpace(s[start])) { start++; continue; }
                break;
            }

            // Strip filter rule suffixes
            while (end >= start)
            {
                if (s[end] == '^') { end--; continue; }
                if (end - start + 1 >= 2 && s[end - 1] == '/' && s[end] == '*') { end -= 2; continue; }
                if (s[end] == '/') { end--; continue; }
                if (char.IsWhiteSpace(s[end])) { end--; continue; }
                break;
            }

            if (start > end) return string.Empty;

            // Extract host without allocating Uri objects on hot network paths
            int schemeIdx = IndexOfOrdinal(s, start, end, "://");
            if (schemeIdx >= 0)
            {
                start = schemeIdx + 3;
            }

            int slash = IndexOfChar(s, start, end, '/');
            if (slash >= 0)
            {
                end = slash - 1;
            }

            // Strip port, preserving IPv6 bracketed addresses
            int closeBracket = IndexOfChar(s, start, end, ']');
            int colonSearchStart = closeBracket >= 0 ? closeBracket + 1 : start;
            int colon = IndexOfChar(s, colonSearchStart, end, ':');
            if (colon >= 0)
            {
                end = colon - 1;
            }

            // Trim any whitespace
            while (start <= end && char.IsWhiteSpace(s[start])) start++;
            while (end >= start && char.IsWhiteSpace(s[end])) end--;

            if (start > end) return string.Empty;

            // Strip leading "www." if remaining length > 4
            int hostLen = end - start + 1;
            if (hostLen > 4 && StartsWithOrdinalIgnoreCase(s, start, "www."))
            {
                start += 4;
                hostLen -= 4;
            }

            // Fast-path: if host is already lowercase and matches original string boundaries exactly
            bool hasUpper = false;
            for (int i = start; i <= end; i++)
            {
                char c = s[i];
                if (c >= 'A' && c <= 'Z')
                {
                    hasUpper = true;
                    break;
                }
            }

            if (!hasUpper)
            {
                if (start == 0 && end == len - 1)
                {
                    return s; // Zero allocation!
                }
                return s.Substring(start, hostLen);
            }

            // Lowercase without intermediate allocations
            char[] chars = new char[hostLen];
            for (int i = 0; i < hostLen; i++)
            {
                char c = s[start + i];
                chars[i] = (c >= 'A' && c <= 'Z') ? (char)(c + 32) : c;
            }
            return new string(chars);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static bool StartsWithOrdinalIgnoreCase(string s, int start, string prefix)
        {
            int pLen = prefix.Length;
            if (s.Length - start < pLen) return false;
            for (int i = 0; i < pLen; i++)
            {
                char c1 = s[start + i];
                char c2 = prefix[i];
                if (c1 == c2) continue;
                char c1Lower = (c1 >= 'A' && c1 <= 'Z') ? (char)(c1 + 32) : c1;
                char c2Lower = (c2 >= 'A' && c2 <= 'Z') ? (char)(c2 + 32) : c2;
                if (c1Lower != c2Lower)
                {
                    return false;
                }
            }
            return true;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int IndexOfOrdinal(string s, int start, int end, string val)
        {
            int vLen = val.Length;
            int max = end - vLen + 1;
            for (int i = start; i <= max; i++)
            {
                bool match = true;
                for (int j = 0; j < vLen; j++)
                {
                    if (s[i + j] != val[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int IndexOfChar(string s, int start, int end, char c)
        {
            for (int i = start; i <= end; i++)
            {
                if (s[i] == c) return i;
            }
            return -1;
        }

        /// <summary>
        /// Extracts the normalized host domain from a given URL or host string.
        /// Alias for <see cref="NormalizeDomain"/>.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static string ExtractDomain(string urlOrDomain)
        {
            return NormalizeDomain(urlOrDomain);
        }

        /// <summary>
        /// Determines whether a given URL or domain belongs to YouTube (e.g. youtube.com, youtu.be, youtube-nocookie.com).
        /// </summary>
        public static bool IsYouTubeDomain(string urlOrDomain)
        {
            if (string.IsNullOrEmpty(urlOrDomain)) return false;

            string domain = NormalizeDomain(urlOrDomain);
            if (string.IsNullOrEmpty(domain)) return false;

            return domain.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
                   domain.Equals("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith(".youtube-nocookie.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a given URL or domain belongs to YouTube core media streaming (googlevideo.com) or static asset delivery (ytimg.com).
        /// </summary>
        public static bool IsYouTubeMediaOrCdnDomain(string urlOrDomain)
        {
            if (string.IsNullOrEmpty(urlOrDomain)) return false;

            string domain = NormalizeDomain(urlOrDomain);
            if (string.IsNullOrEmpty(domain)) return false;

            return domain.Equals("googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.Equals("ytimg.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.EndsWith(".ytimg.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether user text input in an address box or search pill appears to be a direct URL rather than a search query.
        /// </summary>
        public static bool IsLikelyUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            string s = input.Trim();

            if (s.IndexOf(' ') >= 0) return false;

            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("wb://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (s.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int dotIdx = s.IndexOf('.');
            if (dotIdx > 0 && dotIdx < s.Length - 1)
            {
                // Has a dot with at least two characters after it (before any path slash)
                string afterDot = s.Substring(dotIdx + 1);
                int slashIdx = afterDot.IndexOf('/');
                if (slashIdx > 0)
                {
                    afterDot = afterDot.Substring(0, slashIdx);
                }
                if (afterDot.Length >= 2)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Formats direct URL user input with an appropriate scheme prefix (e.g. adds https:// or http:// for localhost).
        /// Preserves existing explicit schemes (http, https, wb, about, chrome, file, view-source).
        /// </summary>
        public static string FormatDirectUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string s = input.Trim();

            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("wb://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                return s;
            }

            if (s.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                return "http://" + s;
            }

            return "https://" + s;
        }

        /// <summary>
        /// Resolves raw address box or search pill input into a navigable URL or search query URL.
        /// </summary>
        public static string ResolveNavigableUrl(string input, out bool isSearch)
        {
            isSearch = false;
            if (string.IsNullOrEmpty(input)) return string.Empty;
            string trimmed = input.Trim();
            if (string.IsNullOrEmpty(trimmed)) return string.Empty;

            if (trimmed.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                string inner = trimmed.Substring("view-source:".Length).Trim();
                if (string.IsNullOrEmpty(inner))
                {
                    return "view-source:about:blank";
                }
                if (inner.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    inner.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    inner.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                    inner.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                    inner.StartsWith("wb://", StringComparison.OrdinalIgnoreCase))
                {
                    return "view-source:" + inner;
                }
                if (inner.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || inner.StartsWith("127.0.0.1"))
                {
                    return "view-source:http://" + inner;
                }
                return "view-source:https://" + inner;
            }

            if (trimmed.StartsWith("wb://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (trimmed.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                return "http://" + trimmed;
            }

            if (trimmed.IndexOf('.') > 0 && !trimmed.Contains(" ") && !trimmed.Contains("?"))
            {
                return "https://" + trimmed;
            }

            isSearch = true;
            return "https://www.google.com/search?q=" + Uri.EscapeDataString(trimmed);
        }

        /// <summary>
        /// Resolves raw address box or search pill input into a navigable URL or search query URL.
        /// </summary>
        public static string ResolveNavigableUrl(string input)
        {
            bool isSearch;
            return ResolveNavigableUrl(input, out isSearch);
        }

        /// <summary>
        /// Returns true if the URL uses an internal, browser-specific, or non-HTTP/HTTPS pseudo scheme.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool IsSpecialScheme(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string trimmed = url.Trim();
            return trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("edge:", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("wb:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts and unescapes the value of a specific query parameter from a full URL or raw query string.
        /// </summary>
        public static string GetQueryParam(string urlOrQuery, string key)
        {
            if (string.IsNullOrEmpty(urlOrQuery) || string.IsNullOrEmpty(key)) return string.Empty;

            string query = urlOrQuery;
            int qIdx = query.IndexOf('?');
            if (qIdx >= 0)
            {
                query = query.Substring(qIdx + 1);
            }

            int hashIdx = query.IndexOf('#');
            if (hashIdx >= 0)
            {
                query = query.Substring(0, hashIdx);
            }

            if (string.IsNullOrEmpty(query)) return string.Empty;

            string[] pairs = query.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i];
                if (string.IsNullOrEmpty(p)) continue;

                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    string k = p.Substring(0, eq).Trim();
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    {
                        string val = p.Substring(eq + 1).Trim();
                        try
                        {
                            return Uri.UnescapeDataString(val);
                        }
                        catch
                        {
                            return val;
                        }
                    }
                }
                else if (string.Equals(p.Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Parses all query parameters from a URL or raw query string into a dictionary.
        /// </summary>
        public static Dictionary<string, string> ParseQueryParams(string urlOrQuery)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(urlOrQuery)) return dict;

            string query = urlOrQuery;
            int qIdx = query.IndexOf('?');
            if (qIdx >= 0)
            {
                query = query.Substring(qIdx + 1);
            }

            int hashIdx = query.IndexOf('#');
            if (hashIdx >= 0)
            {
                query = query.Substring(0, hashIdx);
            }

            if (string.IsNullOrEmpty(query)) return dict;

            string[] pairs = query.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i];
                if (string.IsNullOrEmpty(p)) continue;

                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    string k = p.Substring(0, eq).Trim();
                    string val = p.Substring(eq + 1).Trim();
                    try
                    {
                        val = Uri.UnescapeDataString(val);
                    }
                    catch
                    {
                    }
                    dict[k] = val;
                }
                else
                {
                    dict[p.Trim()] = string.Empty;
                }
            }

            return dict;
        }

        /// <summary>
        /// Extracts the YouTube video ID from watch URLs, shorts, embed URLs, youtu.be links, or raw IDs.
        /// </summary>
        public static string ExtractYouTubeVideoId(string urlOrVideoId)
        {
            if (string.IsNullOrEmpty(urlOrVideoId)) return null;
            string trimmed = urlOrVideoId.Trim();
            if (!trimmed.Contains("/") && !trimmed.Contains("?"))
            {
                return trimmed;
            }

            try
            {
                string v = GetQueryParam(trimmed, "v");
                if (!string.IsNullOrEmpty(v))
                {
                    return v;
                }

                Uri uri;
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out uri))
                {
                    string path = uri.AbsolutePath;
                    if (path.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase))
                    {
                        return path.Substring("/shorts/".Length).Split('/')[0];
                    }
                    if (path.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase))
                    {
                        return path.Substring("/embed/".Length).Split('/')[0];
                    }
                    if (uri.Host.IndexOf("youtu.be", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return path.TrimStart('/').Split('/')[0];
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
