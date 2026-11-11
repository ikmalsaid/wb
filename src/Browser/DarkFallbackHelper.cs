using System;
using CefSharp;
using wb.Core;

namespace wb.Browser
{
    /// <summary>
    /// Provides conditional dark-fallback styling for web pages and protocols that do not define
    /// an explicit background color (e.g. legacy HTML, plain text, RFCs, local server directory
    /// listings like Apache mod_autoindex, Nginx index, Python http.server, or FTP-over-HTTP gateways).
    ///
    /// Architecture &amp; Non-Conflicting Design:
    /// 1. Targeted Condition: Only activates when both `html` and `body` backgrounds are completely
    ///    transparent (and no background-image is present) AND the computed text color is default black.
    /// 2. Native color-scheme standard: Injects low-specificity `:root { color-scheme: dark; }` and
    ///    `body { color: #e8eaed; }` rather than brute-force `!important` overrides, allowing real author
    ///    styles to take natural precedence.
    /// 3. Directory Listings: Automatically themes directory indexes (`Index of /...`) into a clean,
    ///    modern dark UI matching wb's Material dark theme.
    /// 4. Zero Conflict: Explicitly excludes internal pages (`wb://`, `about:`) and video domains
    ///    (`youtube.com`), preserving full compatibility with AdBlock cosmetic rules and SponsorBlock.
    /// </summary>
    public static class DarkFallbackHelper
    {
        public const string GuardScriptId = "wb-dark-fallback-guard";

        public const string GuardScript = @"(function() {
    try {
        if (document.getElementById('wb-dark-fallback-guard')) return;

        var url = (location.href || '').toLowerCase();
        if (url.indexOf('wb://') === 0 || url.indexOf('about:') === 0 || url.indexOf('chrome://') === 0) return;

        var hostname = (location.hostname || '').toLowerCase();
        if (hostname.indexOf('youtube.com') !== -1 || hostname.indexOf('googlevideo.com') !== -1) return;

        // 1. Directory Listings (Apache mod_autoindex, Nginx index, Python http.server, FTP gateways)
        var isDirectoryIndex = false;
        var title = (document.title || '').toLowerCase();
        if (title.indexOf('index of') === 0 || title.indexOf('directory listing') !== -1) {
            isDirectoryIndex = true;
        } else {
            var h1 = document.querySelector('h1');
            if (h1 && (h1.textContent || '').toLowerCase().indexOf('index of') === 0) {
                isDirectoryIndex = true;
            }
        }

        if (isDirectoryIndex) {
            var dirStyle = document.createElement('style');
            dirStyle.id = 'wb-dark-fallback-guard';
            dirStyle.textContent = 
                'html, body { background-color: #202124 !important; color: #e8eaed !important; color-scheme: dark; font-family: Segoe UI, -apple-system, BlinkMacSystemFont, Arial, sans-serif; padding: 16px; margin: 0; }' +
                'h1 { color: #ffffff !important; border-bottom: 1px solid #303236; padding-bottom: 8px; font-size: 1.5em; font-weight: 500; }' +
                'a { color: #8ab4f8 !important; text-decoration: none; }' +
                'a:hover { text-decoration: underline; color: #aecbfa !important; }' +
                'table { border-collapse: collapse; width: 100%; max-width: 1200px; margin-top: 12px; }' +
                'th { color: #9aa0a6 !important; text-align: left; padding: 8px 12px; border-bottom: 2px solid #3c4043 !important; font-weight: 500; }' +
                'td { padding: 8px 12px; border-bottom: 1px solid #303236 !important; color: #e8eaed !important; }' +
                'tr:hover { background-color: #292a2d !important; }' +
                'hr { border: 0; border-top: 1px solid #303236; margin: 16px 0; }' +
                'pre { color: #e8eaed !important; font-family: Consolas, monospace; font-size: 0.95em; line-height: 1.5; }' +
                'ul { list-style-type: none; padding-left: 0; }' +
                'li { padding: 4px 0; }' +
                'address { color: #9aa0a6 !important; margin-top: 24px; font-style: normal; font-size: 0.85em; border-top: 1px solid #303236; padding-top: 8px; }';
            (document.head || document.documentElement).appendChild(dirStyle);
            return;
        }

        // 2. Transparent / Unstyled Documents (raw minimal HTML, legacy indexes, RFCs, plain text)
        var html = document.documentElement;
        var body = document.body;
        if (!html || !body) return;

        var htmlStyle = window.getComputedStyle(html);
        var bodyStyle = window.getComputedStyle(body);

        var hasHtmlBg = (htmlStyle.backgroundColor !== 'rgba(0, 0, 0, 0)' && htmlStyle.backgroundColor !== 'transparent') || htmlStyle.backgroundImage !== 'none';
        var hasBodyBg = (bodyStyle.backgroundColor !== 'rgba(0, 0, 0, 0)' && bodyStyle.backgroundColor !== 'transparent') || bodyStyle.backgroundImage !== 'none';

        // Only act if the document does not define any background
        if (!hasHtmlBg && !hasBodyBg) {
            var textColor = bodyStyle.color;
            // Check if text color is default unstyled black
            if (textColor === 'rgb(0, 0, 0)' || textColor === 'rgba(0, 0, 0, 1)') {
                html.style.colorScheme = 'dark';
                var fallbackStyle = document.createElement('style');
                fallbackStyle.id = 'wb-dark-fallback-guard';
                fallbackStyle.textContent = ':root { color-scheme: dark; } body { color: #e8eaed; } pre { color: #e8eaed; }';
                (document.head || document.documentElement).appendChild(fallbackStyle);

                // Unstyled link readability enhancement for transparent documents
                var links = document.querySelectorAll('a:not([class])');
                for (var i = 0; i < links.length; i++) {
                    var linkColor = window.getComputedStyle(links[i]).color;
                    if (linkColor === 'rgb(0, 0, 238)' || linkColor === 'rgb(0, 0, 255)') {
                        links[i].style.color = '#8ab4f8';
                    }
                }
            }
        }
    } catch(e) {}
})();";

        /// <summary>
        /// Determines whether the given URL is eligible for dark fallback inspection.
        /// Excludes internal wb:// pages and video streaming platforms.
        /// </summary>
        public static bool ShouldInject(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (InternalPageHelper.IsInternalPage(url)) return false;

            if (url.StartsWith("wb://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("chrome-devtools://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                Uri uri = new Uri(url);
                string host = uri.Host.ToLowerInvariant();
                if (host.Contains("youtube.com") || host.Contains("googlevideo.com"))
                {
                    return false;
                }
            }
            catch
            {
            }

            return true;
        }

        /// <summary>
        /// Injects the conditional dark-fallback guard into the specified frame if eligible.
        /// </summary>
        public static void InjectIntoFrame(IFrame frame, string url)
        {
            if (frame == null || !frame.IsValid) return;
            if (!ShouldInject(url)) return;

            try
            {
                frame.ExecuteJavaScriptAsync(GuardScript);
            }
            catch
            {
            }
        }
    }
}

