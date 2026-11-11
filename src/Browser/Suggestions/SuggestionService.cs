using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using wb.Core;

namespace wb.Browser.Suggestions
{
    public class SuggestionService
    {
        private const string GoogleSuggestUrl = "https://suggestqueries.google.com/complete/search?client=chrome&q=";

        public async Task<List<SuggestionItem>> GetSuggestionsAsync(string query, bool isIncognito, int maxItems = 7, CancellationToken cancellationToken = default(CancellationToken), bool fetchRemote = true)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<SuggestionItem>();
            }

            string cleanQuery = query.Trim();
            List<SuggestionItem> results = new List<SuggestionItem>();
            HashSet<string> seenActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool isUrl = IsLikelyUrl(cleanQuery);
            if (isUrl)
            {
                string directUrl = FormatDirectUrl(cleanQuery);
                bool isIncogInternal = !isIncognito && (InternalPageHelper.GetInternalPageType(directUrl) == InternalPageType.Incognito ||
                                                        string.Equals(directUrl, InternalPageHelper.IncognitoUrl, StringComparison.OrdinalIgnoreCase) ||
                                                        string.Equals(directUrl, "wb://private", StringComparison.OrdinalIgnoreCase));
                if (!isIncogInternal)
                {
                    SuggestionItem directItem = new SuggestionItem(
                        SuggestionType.DirectUrl,
                        cleanQuery,
                        directUrl,
                        directUrl,
                        cleanQuery
                    );
                    results.Add(directItem);
                    seenActions.Add(directUrl);
                }
                else
                {
                    // Fallback to search query if user types incognito in normal mode to prevent suggesting internal incognito page
                    string searchUrl = "https://www.google.com/search?q=" + Uri.EscapeDataString(cleanQuery);
                    SuggestionItem searchItem = new SuggestionItem(
                        SuggestionType.Search,
                        cleanQuery,
                        "Search Google",
                        searchUrl,
                        cleanQuery
                    );
                    results.Add(searchItem);
                    seenActions.Add(cleanQuery);
                }
            }
            else
            {
                // Top search query
                string searchUrl = "https://www.google.com/search?q=" + Uri.EscapeDataString(cleanQuery);
                SuggestionItem searchItem = new SuggestionItem(
                    SuggestionType.Search,
                    cleanQuery,
                    "Search Google",
                    searchUrl,
                    cleanQuery
                );
                results.Add(searchItem);
                seenActions.Add(cleanQuery);
            }

            // Note: Internal wb:// pages are strictly suppressed from address bar suggestions

            // 1. Bookmarks Match
            AddBookmarkMatches(cleanQuery, results, seenActions);

            // 2. History Match (strictly excluded in Incognito mode for privacy)
            if (!isIncognito)
            {
                AddHistoryMatches(cleanQuery, results, seenActions);
            }

            // 3. Remote Search Suggestions from Google Suggest API
            if (fetchRemote)
            {
                try
                {
                    List<string> remoteQueries = await FetchGoogleSuggestionsAsync(cleanQuery, cancellationToken);
                if (remoteQueries != null)
                {
                    for (int i = 0; i < remoteQueries.Count && results.Count < maxItems; i++)
                    {
                        string rq = remoteQueries[i];
                        if (string.IsNullOrWhiteSpace(rq)) continue;

                        if (IsLikelyUrl(rq))
                        {
                            string targetUrl = FormatDirectUrl(rq);
                            if (InternalPageHelper.IsInternalUrl(targetUrl) || InternalPageHelper.IsStartPage(targetUrl)) continue;

                            if (seenActions.Add(targetUrl))
                            {
                                results.Add(new SuggestionItem(
                                    SuggestionType.DirectUrl,
                                    rq,
                                    targetUrl,
                                    targetUrl,
                                    cleanQuery
                                ));
                            }
                        }
                        else
                        {
                            if (seenActions.Add(rq))
                            {
                                string targetUrl = "https://www.google.com/search?q=" + Uri.EscapeDataString(rq);
                                results.Add(new SuggestionItem(
                                    SuggestionType.Search,
                                    rq,
                                    null,
                                    targetUrl,
                                    cleanQuery
                                ));
                            }
                        }
                    }
                }
                }
                catch
                {
                    // Graceful fallback to local suggestions if offline or timed out
                }
            }

            // Cap results to maxItems
            if (results.Count > maxItems)
            {
                results = results.GetRange(0, maxItems);
            }

            return results;
        }

        private void AddBookmarkMatches(string query, List<SuggestionItem> results, HashSet<string> seenActions)
        {
            if (AppServices.Bookmarks == null) return;
            var bookmarks = AppServices.Bookmarks.FindMatches(query, 2);
            if (bookmarks == null || bookmarks.Count == 0) return;

            for (int i = 0; i < bookmarks.Count; i++)
            {
                var b = bookmarks[i];
                if (b == null || string.IsNullOrEmpty(b.Url)) continue;
                if (InternalPageHelper.IsInternalUrl(b.Url) || InternalPageHelper.IsStartPage(b.Url)) continue;

                if (seenActions.Add(b.Url))
                {
                    results.Add(new SuggestionItem(
                        SuggestionType.Bookmark,
                        !string.IsNullOrEmpty(b.Title) ? b.Title : b.Url,
                        b.Url,
                        b.Url,
                        query
                    ));
                }
            }
        }

        private void AddHistoryMatches(string query, List<SuggestionItem> results, HashSet<string> seenActions)
        {
            if (AppServices.History == null) return;
            var history = AppServices.History.FindMatches(query, 3);
            if (history == null || history.Count == 0) return;

            for (int i = 0; i < history.Count; i++)
            {
                var h = history[i];
                if (h == null || string.IsNullOrEmpty(h.Url)) continue;
                if (InternalPageHelper.IsInternalUrl(h.Url) || InternalPageHelper.IsStartPage(h.Url)) continue;

                if (seenActions.Add(h.Url))
                {
                    results.Add(new SuggestionItem(
                        SuggestionType.History,
                        !string.IsNullOrEmpty(h.Title) ? h.Title : h.Url,
                        h.Url,
                        h.Url,
                        query
                    ));
                }
            }
        }

        public async Task<List<string>> FetchGoogleSuggestionsAsync(string query, CancellationToken cancellationToken)
        {
            string url = GoogleSuggestUrl + Uri.EscapeDataString(query);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 1500;
            req.ReadWriteTimeout = 1500;
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/133.0.0.0 Safari/537.36";

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(1500))
            using (CancellationTokenSource linkedCts = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
                : null)
            {
                CancellationToken effectiveToken = linkedCts != null ? linkedCts.Token : timeoutCts.Token;
                using (effectiveToken.Register(() => { try { req.Abort(); } catch { } }))
                {
                    using (WebResponse resp = await req.GetResponseAsync().ConfigureAwait(false))
                    using (Stream stream = resp.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string json = await reader.ReadToEndAsync().ConfigureAwait(false);
                        return ParseGoogleSuggestJson(json);
                    }
                }
            }
        }

        public List<string> ParseGoogleSuggestJson(string json)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            try
            {
                object parsed = StorageHelper.DeserializeObject(json);
                object[] arr = parsed as object[];
                if (arr != null && arr.Length >= 2)
                {
                    object[] suggestions = arr[1] as object[];
                    if (suggestions != null)
                    {
                        for (int i = 0; i < suggestions.Length; i++)
                        {
                            if (suggestions[i] != null)
                            {
                                string text = suggestions[i].ToString();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    list.Add(text);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return list;
        }

        public static bool IsLikelyUrl(string input)
        {
            return UrlHelper.IsLikelyUrl(input);
        }

        public static string FormatDirectUrl(string input)
        {
            return UrlHelper.FormatDirectUrl(input);
        }
    }
}
