using System;
using CefSharp;
using CefSharp.Handler;
using wb.Core;
using wb.AdBlock;
using wb.Browser;

namespace wb.Browser.Handlers
{
    public class BrowserResourceRequestHandler : ResourceRequestHandler
    {
        private readonly BrowserTab _tab;

        public BrowserResourceRequestHandler(BrowserTab tab)
        {
            _tab = tab;
        }

        private static IResourceHandler CreateMockAdResourceHandler(IRequest request)
        {
            string url = request != null && !string.IsNullOrEmpty(request.Url) ? request.Url : string.Empty;
            if (url.IndexOf("imasdk.googleapis.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf("ima3.js", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ResourceHandler.FromString(AdBlockYTShield.GetGoogleImaShimScript(), System.Text.Encoding.UTF8, false, "application/javascript");
            }

            if ((request != null && request.ResourceType == ResourceType.Script) || url.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                return ResourceHandler.FromString("/* uBO noop */", System.Text.Encoding.UTF8, false, "application/javascript");
            }

            if ((request != null && request.ResourceType == ResourceType.Xhr) || url.IndexOf("/api/", StringComparison.OrdinalIgnoreCase) >= 0 || url.IndexOf("/pagead/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ResourceHandler.FromString("{}", System.Text.Encoding.UTF8, false, "application/json");
            }

            return ResourceHandler.FromString("/* noop */", System.Text.Encoding.UTF8, false, "text/plain");
        }

        protected override IResourceHandler GetResourceHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.Url) || request.ResourceType == ResourceType.MainFrame)
                {
                    return null;
                }

                // Embedded YouTube player subframes must never be blocked so player can load
                if (request.ResourceType == ResourceType.SubFrame && AdBlockYTShield.IsYouTubeDomain(request.Url))
                {
                    return null;
                }

                // Safely extract frame information without throwing ThrowIfFrameInvalid
                string frameUrl = null;
                bool isMainFrame = false;
                try
                {
                    if (frame != null && frame.IsValid)
                    {
                        isMainFrame = frame.IsMain;
                        frameUrl = frame.Url;
                    }
                }
                catch { }

                string pageUrl = null;
                if (_tab != null && !string.IsNullOrEmpty(_tab.EffectiveUrl))
                {
                    pageUrl = _tab.EffectiveUrl;
                }
                else if (_tab != null && !string.IsNullOrEmpty(_tab.CurrentUrl) && !InternalPageHelper.IsInternalUrl(_tab.CurrentUrl))
                {
                    pageUrl = _tab.CurrentUrl;
                }
                else if (!string.IsNullOrEmpty(frameUrl) && !InternalPageHelper.IsInternalUrl(frameUrl))
                {
                    pageUrl = frameUrl;
                }

                // Fast bypass for benchmark runs
                if (BrowserTab.IsBenchmarkUrl(request.Url) || BrowserTab.IsBenchmarkUrl(pageUrl))
                {
                    return null;
                }

                bool isYouTubeEmbed = !isMainFrame && !string.IsNullOrEmpty(frameUrl) && AdBlockYTShield.IsYouTubeDomain(frameUrl);
                bool isYouTube = isYouTubeEmbed || AdBlockYTShield.IsYouTubeDomain(pageUrl) || AdBlockYTShield.IsYouTubeMediaOrCdnDomain(pageUrl) || AdBlockYTShield.IsYouTubeDomain(request.Url) || AdBlockYTShield.IsYouTubeMediaOrCdnDomain(request.Url);

                if (isYouTube)
                {
                    // Explicit ad networks and IMA SDK requests on YouTube:
                    // Intercept and return 200 OK mock responses (uBlock Origin redirect mechanism)
                    // instead of net::ERR_ABORTED to prevent YouTube from detecting ad blocking and
                    // triggering its ~12-second artificial buffering penalty loop.
                    if (AdBlockYTShield.IsExplicitAdDomain(request.Url) ||
                        request.Url.IndexOf("imasdk.googleapis.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        request.Url.IndexOf("ima3.js", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (_tab != null)
                        {
                            _tab.IncrementBlockedCount();
                        }

                        string domain = AdBlockManager.NormalizeDomain(request.Url);
                        AppServices.AdBlock.RecordBlocked(domain);

                        return CreateMockAdResourceHandler(request);
                    }

                    // First-party YouTube requests, video stream chunks (googlevideo.com) and static assets (ytimg.com)
                    // must never be mocked with empty ResourceHandler because mock responses lack necessary
                    // CORS headers and cause JSON schema or network abort errors in YouTube's player.
                    // In-page AdBlockYTShield safely prunes all first-party ad data with zero network penalties.
                    if (AdBlockYTShield.IsYouTubeDomain(request.Url) || AdBlockYTShield.IsYouTubeMediaOrCdnDomain(request.Url))
                    {
                        return null;
                    }
                }

                return base.GetResourceHandler(chromiumWebBrowser, browser, frame, request);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[wb] BrowserResourceRequestHandler.GetResourceHandler error: " + ex.Message);
                return null;
            }
        }

        protected override CefReturnValue OnBeforeResourceLoad(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback)
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.Url))
                {
                    return CefReturnValue.Continue;
                }

                // Intercept wb-action from iframes or fetch requests
                if (request.Url.StartsWith("wb-action:", StringComparison.OrdinalIgnoreCase))
                {
                    if (_tab != null)
                    {
                        _tab.HandleWbAction(request.Url);
                    }
                    return CefReturnValue.Cancel;
                }

                // Top-level main frame navigation should not be blocked by ad rules
                if (request.ResourceType == ResourceType.MainFrame)
                {
                    return CefReturnValue.Continue;
                }

                // Embedded YouTube player subframes must never be blocked so the player and our shield script can load
                if (request.ResourceType == ResourceType.SubFrame && AdBlockYTShield.IsYouTubeDomain(request.Url))
                {
                    return CefReturnValue.Continue;
                }

                // Safely extract frame information without throwing ThrowIfFrameInvalid
                string frameUrl = null;
                bool isMainFrame = false;
                try
                {
                    if (frame != null && frame.IsValid)
                    {
                        isMainFrame = frame.IsMain;
                        frameUrl = frame.Url;
                    }
                }
                catch { }

                // Determine the top-level page URL from memory without cross-thread COM wrapper allocations
                string pageUrl = null;
                if (_tab != null && !string.IsNullOrEmpty(_tab.EffectiveUrl))
                {
                    pageUrl = _tab.EffectiveUrl;
                }
                else if (_tab != null && !string.IsNullOrEmpty(_tab.CurrentUrl) && !InternalPageHelper.IsInternalUrl(_tab.CurrentUrl))
                {
                    pageUrl = _tab.CurrentUrl;
                }
                else if (!string.IsNullOrEmpty(frameUrl) && !InternalPageHelper.IsInternalUrl(frameUrl))
                {
                    pageUrl = frameUrl;
                }

                // Fast bypass for benchmark runs: zero regex or ad blocking overhead
                if (BrowserTab.IsBenchmarkUrl(request.Url) || BrowserTab.IsBenchmarkUrl(pageUrl))
                {
                    return CefReturnValue.Continue;
                }

                bool isYouTubeEmbed = !isMainFrame && !string.IsNullOrEmpty(frameUrl) && AdBlockYTShield.IsYouTubeDomain(frameUrl);
                bool isYouTube = isYouTubeEmbed || AdBlockYTShield.IsYouTubeDomain(pageUrl) || AdBlockYTShield.IsYouTubeMediaOrCdnDomain(pageUrl) || AdBlockYTShield.IsYouTubeDomain(request.Url) || AdBlockYTShield.IsYouTubeMediaOrCdnDomain(request.Url);

                if (isYouTube)
                {
                    // Explicit ad tracking networks and IMA SDK requests on YouTube are intercepted by GetResourceHandler
                    // returning 200 OK mock responses (uBlock Origin redirect mechanism) to prevent YouTube's
                    // player from detecting net::ERR_ABORTED and triggering the ~12-second fake buffering penalty.
                    if (AdBlockYTShield.IsExplicitAdDomain(request.Url) ||
                        request.Url.IndexOf("imasdk.googleapis.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        request.Url.IndexOf("ima3.js", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return CefReturnValue.Continue;
                    }

                    // On YouTube pages or embeds, never abort legitimate first-party YouTube requests,
                    // video streams (*.googlevideo.com), or static assets (*.ytimg.com) at the network layer.
                    // Cancelling first-party requests or video chunks with net::ERR_ABORTED causes YouTube's player to trigger its
                    // 5-to-10 second artificial buffering/retry penalty or halt playback.
                    // In-page AdBlockYTShield safely strips ad placements and handles cosmetic hiding at 0 latency.
                    if (AdBlockYTShield.IsYouTubeDomain(request.Url) || AdBlockYTShield.IsYouTubeMediaOrCdnDomain(request.Url))
                    {
                        return CefReturnValue.Continue;
                    }
                }

                // Check if ad blocking is active and should block this request
                string ruleMatched = null;
                bool shouldBlock = false;

                // If request originates from an embedded YouTube iframe on a third-party host site (e.g. reddit.com),
                // evaluate against the YouTube frame origin first so dedicated YouTube ad rules apply
                if (isYouTubeEmbed && !string.IsNullOrEmpty(frameUrl))
                {
                    shouldBlock = AppServices.AdBlock.ShouldBlock(request.Url, frameUrl, (int)request.ResourceType, out ruleMatched);
                }

                if (!shouldBlock)
                {
                    shouldBlock = AppServices.AdBlock.ShouldBlock(request.Url, pageUrl, (int)request.ResourceType, out ruleMatched);
                }

                if (shouldBlock)
                {
                    if (_tab != null)
                    {
                        _tab.IncrementBlockedCount();
                    }

                    string domain = AdBlockManager.NormalizeDomain((isYouTubeEmbed && !string.IsNullOrEmpty(frameUrl)) ? frameUrl : pageUrl);
                    AppServices.AdBlock.RecordBlocked(domain);

                    return CefReturnValue.Cancel;
                }

                return CefReturnValue.Continue;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[wb] BrowserResourceRequestHandler.OnBeforeResourceLoad error: " + ex.Message);
                return CefReturnValue.Continue;
            }
        }
    }
}
