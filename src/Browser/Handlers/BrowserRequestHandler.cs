using System;
using System.Net;
using CefSharp;
using CefSharp.Handler;
using wb.Core;
using wb.Browser;

namespace wb.Browser.Handlers
{
    public class BrowserRequestHandler : RequestHandler
    {
        private readonly BrowserTab _tab;
        private readonly BrowserResourceRequestHandler _resourceRequestHandler;

        public BrowserRequestHandler(BrowserTab tab)
        {
            _tab = tab;
            _resourceRequestHandler = new BrowserResourceRequestHandler(tab);
        }

        private void SafeInvokeTab(Action action)
        {
            if (_tab == null || action == null) return;
            try
            {
                if (!_tab.IsDisposed && _tab.IsHandleCreated)
                {
                    _tab.BeginInvoke(new Action(() =>
                    {
                        if (!_tab.IsDisposed)
                        {
                            try
                            {
                                action();
                            }
                            catch (ObjectDisposedException) { }
                        }
                    }));
                }
            }
            catch (ObjectDisposedException) { }
            catch { }
        }

        protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
        {
            if (request == null || string.IsNullOrEmpty(request.Url))
            {
                return false;
            }

            // 1. Handle interactive error page actions
            if (request.Url.StartsWith("wb-action:", StringComparison.OrdinalIgnoreCase))
            {
                HandleWbAction(request.Url);
                return true; // Cancel navigation to pseudo-scheme
            }

            // 2. Intercept Chromium internal error pages (e.g. chrome-error://chromewebdata/)
            if (WebErrorPage.IsChromiumInternalErrorPage(request.Url))
            {
                string targetUrl = _tab != null ? _tab.CurrentUrl : null;
                if (string.IsNullOrEmpty(targetUrl) || WebErrorPage.IsChromiumInternalErrorPage(targetUrl))
                {
                    targetUrl = "wb://newtab";
                }
                WebErrorInfo info = WebErrorPage.CreateFromCefError(
                    "DNS_PROBE_FINISHED_NXDOMAIN",
                    -105,
                    targetUrl,
                    "DNS_PROBE_FINISHED_NXDOMAIN"
                );
                string errorFilePath = WebErrorPage.CreateErrorFile(info);
                SafeInvokeTab(() =>
                {
                    _tab.LoadErrorPage(targetUrl, errorFilePath, WebErrorPage.GetFavicon(info.Kind));
                });
                return true; // Cancel navigation to chrome-error://
            }

            // 3. Main frame strict blocking & security check
            if (frame != null && frame.IsMain)
            {
                if (_tab != null && !WebErrorPage.IsErrorPage(request.Url) && !InternalPageHelper.IsStartPage(request.Url))
                {
                    _tab.SetPendingNavigationUrl(request.Url);
                }

                // Always allow local error pages, start page and local file URLs
                if (WebErrorPage.IsErrorPage(request.Url) || InternalPageHelper.IsStartPage(request.Url) ||
                    request.Url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // If user bypassed warning for this URL/domain, let it load
                if (WebErrorBypassManager.IsUrlAllowed(request.Url))
                {
                    return false;
                }

                // Check ad & malware blocker for top-level risky site detection
                string ruleMatched;
                string listMatched;
                if (AppServices.AdBlock.ShouldBlockMainFrame(request.Url, out ruleMatched, out listMatched))
                {
                    string targetUrl = request.Url;
                    string redirectUrl = wb.AdBlock.UrlSkipHelper.TryUnwrap(targetUrl);
                    string errorFilePath = wb.AdBlock.StrictBlockPage.CreateBlockPageFile(targetUrl, ruleMatched, listMatched, redirectUrl);

                    SafeInvokeTab(() =>
                    {
                        _tab.LoadErrorPage(targetUrl, errorFilePath, WebErrorPage.GetStopFavicon());
                    });

                    return true; // Cancel navigation to malicious site
                }
            }

            return false;
        }

        protected override bool OnOpenUrlFromTab(
            IWebBrowser chromiumWebBrowser,
            IBrowser browser,
            IFrame frame,
            string targetUrl,
            WindowOpenDisposition targetDisposition,
            bool userGesture)
        {
            if (targetDisposition == WindowOpenDisposition.NewForegroundTab ||
                targetDisposition == WindowOpenDisposition.NewBackgroundTab ||
                targetDisposition == WindowOpenDisposition.NewPopup ||
                targetDisposition == WindowOpenDisposition.NewWindow)
            {
                if (_tab != null && !string.IsNullOrEmpty(targetUrl))
                {
                    _tab.RequestOpenNewTab(targetUrl);
                    return true;
                }
            }

            return base.OnOpenUrlFromTab(chromiumWebBrowser, browser, frame, targetUrl, targetDisposition, userGesture);
        }

        protected override bool OnCertificateError(IWebBrowser chromiumWebBrowser, IBrowser browser, CefErrorCode errorCode, string requestUrl, ISslInfo sslInfo, IRequestCallback callback)
        {
            string host = requestUrl;
            try
            {
                Uri u;
                if (Uri.TryCreate(requestUrl, UriKind.Absolute, out u))
                {
                    host = u.Host;
                }
            }
            catch
            {
            }

            // If user previously chose to proceed to this SSL host
            if (WebErrorBypassManager.IsSslHostAllowed(host))
            {
                callback.Continue(true);
                return true;
            }

            // Otherwise, block and show unified insecure warning
            string certDetails = string.Format("Error: {0} (SSL Status: {1})", errorCode, sslInfo != null ? sslInfo.CertStatus.ToString() : "Invalid");
            WebErrorInfo info = WebErrorPage.CreateForSslError(requestUrl, errorCode.ToString(), certDetails);
            string errorFilePath = WebErrorPage.CreateErrorFile(info);

            SafeInvokeTab(() =>
            {
                _tab.LoadErrorPage(requestUrl, errorFilePath, WebErrorPage.GetWarningFavicon());
            });

            callback.Dispose();
            return false;
        }

        protected override IResourceRequestHandler GetResourceRequestHandler(
            IWebBrowser chromiumWebBrowser,
            IBrowser browser,
            IFrame frame,
            IRequest request,
            bool isNavigation,
            bool isDownload,
            string requestInitiator,
            ref bool disableDefaultHandling)
        {
            return _resourceRequestHandler;
        }

        protected override bool GetAuthCredentials(
            IWebBrowser chromiumWebBrowser,
            IBrowser browser,
            string originUrl,
            bool isProxy,
            string host,
            int port,
            string realm,
            string scheme,
            IAuthCallback callback)
        {
            if (isProxy && AppServices.Proxy.HasCredentials)
            {
                callback.Continue(AppServices.Proxy.Username, AppServices.Proxy.Password);
                return true;
            }
            return base.GetAuthCredentials(chromiumWebBrowser, browser, originUrl, isProxy, host, port, realm, scheme, callback);
        }

        protected override void OnRenderProcessTerminated(IWebBrowser chromiumWebBrowser, IBrowser browser, CefTerminationStatus status, int errorCode, string errorMessage)
        {
            try
            {
                string currentUrl = _tab != null ? _tab.CurrentUrl : null;
                CrashpadHelper.LogRenderProcessCrash((int)status, currentUrl);

                if (_tab != null)
                {
                    SafeInvokeTab(() =>
                    {
                        string targetUrl = !string.IsNullOrEmpty(currentUrl) ? currentUrl : "wb://newtab";
                        WebErrorInfo info = WebErrorPage.CreateFromCefError(
                            "RENDERER_CRASHED",
                            (int)status,
                            targetUrl,
                            string.Format("The webpage process terminated unexpectedly ({0}: {1}). Minidump and diagnostic logs have been recorded.", status, errorMessage ?? string.Empty)
                        );
                        string errorFilePath = WebErrorPage.CreateErrorFile(info);
                        _tab.LoadErrorPage(targetUrl, errorFilePath, WebErrorPage.GetFavicon(info.Kind));
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[wb] OnRenderProcessTerminated error: " + ex.Message);
            }
            base.OnRenderProcessTerminated(chromiumWebBrowser, browser, status, errorCode, errorMessage);
        }

        private void HandleWbAction(string actionUrl)
        {
            if (_tab != null)
            {
                _tab.HandleWbAction(actionUrl);
            }
        }
    }
}
