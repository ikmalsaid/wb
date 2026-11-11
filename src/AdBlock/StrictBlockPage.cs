using System;
using System.IO;
using System.Net;
using System.Text;
using wb.Browser;
using wb.Core;

namespace wb.AdBlock
{
    /// <summary>
    /// Metadata for a page blocked by AdBlock / malware / tracking strict blocking.
    /// </summary>
    public class StrictBlockInfo
    {
        public string TargetUrl { get; set; }
        public string Domain { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string MatchedRule { get; set; }
        public string FilterSource { get; set; }
        public string RedirectUrl { get; set; }
        public string TechnicalCode { get; set; }
        public string Description { get; set; }

        public StrictBlockInfo()
        {
            Title = "Page blocked";
            Subtitle = "wb has prevented the following page from loading:";
            FilterSource = "wb";
            TechnicalCode = "STRICT_BLOCK_INTERCEPTED";
            Description = "Strict blocking intercepted this navigation before any network requests reached the remote server.";
        }
    }

    /// <summary>
    /// Dedicated strict blocking warning page generator for AdBlock.
    /// Replicates uBlock Origin / uBO Lite strict document blocking UI:
    /// displays the blocked URL with highlighted hostname, attributes the matching
    /// subscription filter list, unwraps redirect targets ($urlskip) and provides
    /// close window / proceed actions.
    /// </summary>
    public static class StrictBlockPage
    {
        public static bool IsBlockPage(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.IndexOf("strictblock_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("weberror_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static StrictBlockInfo CreateInfo(string targetUrl, string matchedRule = null, string filterSource = null, string redirectUrl = null)
        {
            string domain = ExtractHost(targetUrl);
            string sourceName = !string.IsNullOrEmpty(filterSource) ? filterSource : "wb";
            return new StrictBlockInfo
            {
                TargetUrl = targetUrl ?? "wb://newtab",
                Domain = domain,
                Title = "Page blocked",
                Subtitle = "wb has prevented the following page from loading:",
                MatchedRule = !string.IsNullOrEmpty(matchedRule) ? matchedRule : string.Format("||{0}^", domain),
                FilterSource = sourceName,
                TechnicalCode = "STRICT_BLOCK_INTERCEPTED",
                Description = "Strict blocking intercepted this navigation before any network requests reached the remote server.",
                RedirectUrl = redirectUrl
            };
        }

        public static string CreateBlockPageFile(string targetUrl, string matchedRule = null, string filterSource = null, string redirectUrl = null)
        {
            StrictBlockInfo info = CreateInfo(targetUrl, matchedRule, filterSource, redirectUrl);
            return CreateBlockPageFile(info);
        }

        public static string CreateBlockPageFile(StrictBlockInfo info)
        {
            if (info == null) info = new StrictBlockInfo();
            return StorageHelper.CreateTempHtmlFile(Path.Combine("cache", "errors"), "strictblock", GenerateHtml(info));
        }

        public static string GenerateHtml(StrictBlockInfo info)
        {
            if (info == null) info = new StrictBlockInfo();

            string rawUrl = info.TargetUrl ?? "wb://newtab";
            string safeFilter = !string.IsNullOrEmpty(info.FilterSource) ? info.FilterSource : "wb";
            string safeTitle = !string.IsNullOrEmpty(info.Title) ? info.Title : "Page blocked";
            string safeSubtitle = !string.IsNullOrEmpty(info.Subtitle) ? info.Subtitle : "wb has prevented the following page from loading:";
            string redirectUrl = info.RedirectUrl ?? "";
            string encodedTargetUrl = WebUtility.UrlEncode(rawUrl);
            string encodedRedirectUrl = WebUtility.UrlEncode(redirectUrl);
            bool hasRedirect = !string.IsNullOrEmpty(redirectUrl);

            // Bold the hostname in the blocked URL display (matching uBlock Origin / uBO Lite)
            string boldUrlHtml;
            try
            {
                Uri u;
                if (Uri.TryCreate(rawUrl, UriKind.Absolute, out u))
                {
                    string host = u.Host;
                    int hostIdx = rawUrl.IndexOf(host, StringComparison.OrdinalIgnoreCase);
                    if (hostIdx >= 0)
                    {
                        string beforeHost = WebUtility.HtmlEncode(rawUrl.Substring(0, hostIdx));
                        string hostPart = WebUtility.HtmlEncode(rawUrl.Substring(hostIdx, host.Length));
                        string afterHost = WebUtility.HtmlEncode(rawUrl.Substring(hostIdx + host.Length));
                        boldUrlHtml = beforeHost + "<b>" + hostPart + "</b>" + afterHost;
                    }
                    else
                    {
                        boldUrlHtml = WebUtility.HtmlEncode(rawUrl);
                    }
                }
                else
                {
                    boldUrlHtml = WebUtility.HtmlEncode(rawUrl);
                }
            }
            catch
            {
                boldUrlHtml = WebUtility.HtmlEncode(rawUrl);
            }

            var model = new UnifiedErrorModel
            {
                PageTitle = safeTitle,
                Title = safeTitle,
                Subtitle = safeSubtitle,
                BadgeIconSvg = PhosphorErrorIcons.ShieldWarning,
                ThemeVariant = "danger",
                TargetUrl = rawUrl,
                FormattedUrlHtml = boldUrlHtml,
                Description = info.Description ?? "Strict blocking intercepted this navigation before any network requests reached the remote server.",
                FilterSource = safeFilter,
                MatchedRule = info.MatchedRule,
                ErrorCode = info.TechnicalCode,
                RedirectUrl = redirectUrl,
                ShowPermanentWhitelistCheckbox = true,
                WhitelistCheckboxText = "Don't warn me again about this site"
            };

            model.Actions.Add(new ActionButton
            {
                Id = "bye",
                Text = "Close this window",
                OnClick = "closeWindow()",
                IsPrimary = true
            });
            model.Actions.Add(new ActionButton
            {
                Id = "proceed",
                Text = "Proceed",
                OnClick = "proceed()",
                IsPrimary = false
            });

            StringBuilder js = new StringBuilder();
            js.AppendFormat(@"
    var targetUrl = ""{0}"";
    var finalUrl = ""{1}"";
    var hasRedirect = {2};

    function onToggleWarning(cb) {{
        var byeBtn = document.getElementById('bye');
        if (byeBtn) {{
            if (cb.checked) {{
                byeBtn.classList.add('disabled');
            }} else {{
                byeBtn.classList.remove('disabled');
            }}
        }}
    }}

    function closeWindow() {{
        var cb = document.getElementById('disableWarning');
        if (cb && cb.checked) return;
        window.location.href = ""wb-action:closetab"";
    }}

    function proceed() {{
        var cb = document.getElementById('disableWarning');
        var permanent = cb ? cb.checked : false;
        if (hasRedirect && !permanent && finalUrl) {{
            window.location.href = ""wb-action:proceed?url="" + finalUrl;
            return;
        }}
        var act = ""wb-action:proceed?url="" + targetUrl;
        if (permanent) {{
            act += ""&permanent=1"";
        }}
        window.location.href = act;
    }}", encodedTargetUrl, encodedRedirectUrl, hasRedirect ? "true" : "false");

            model.CustomScript = js.ToString();

            return WebErrorPage.GenerateUnifiedHtml(model);
        }

        private static string ExtractHost(string url)
        {
            if (string.IsNullOrEmpty(url)) return "unknown host";
            string host = UrlHelper.ExtractDomain(url);
            return !string.IsNullOrEmpty(host) ? host : url;
        }
    }
}
