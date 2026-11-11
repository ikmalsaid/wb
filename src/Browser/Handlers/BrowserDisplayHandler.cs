using System;
using System.Collections.Generic;
using CefSharp;
using CefSharp.Handler;
using wb.Core;

namespace wb.Browser.Handlers
{
    public class BrowserDisplayHandler : DisplayHandler
    {
        private readonly Action<string, string> _onFaviconUrl;
        private readonly Action<string> _onWbAction;
        private readonly Action<bool> _onFullscreenModeChange;

        public BrowserDisplayHandler(Action<string, string> onFaviconUrl, Action<string> onWbAction = null, Action<bool> onFullscreenModeChange = null)
        {
            _onFaviconUrl = onFaviconUrl;
            _onWbAction = onWbAction;
            _onFullscreenModeChange = onFullscreenModeChange;
        }

        protected override bool OnConsoleMessage(IWebBrowser chromiumWebBrowser, ConsoleMessageEventArgs consoleMessageArgs)
        {
            if (consoleMessageArgs != null && !string.IsNullOrEmpty(consoleMessageArgs.Message))
            {
                string msg = consoleMessageArgs.Message.Trim();
                if (msg.StartsWith("wb-action:", StringComparison.OrdinalIgnoreCase))
                {
                    if (_onWbAction != null)
                    {
                        _onWbAction(msg);
                    }
                    return true; // Suppress from developer console
                }

                try
                {
                    string level = "D";
                    switch (consoleMessageArgs.Level)
                    {
                        case LogSeverity.Error: level = "E"; break;
                        case LogSeverity.Warning: level = "W"; break;
                        case LogSeverity.Info: level = "I"; break;
                        default: level = "D"; break;
                    }
                    LogcatHelper.Log(level, "wb.JS", string.Format("{0} ({1}:{2})", msg, consoleMessageArgs.Source, consoleMessageArgs.Line));
                }
                catch { }
            }
            return false;
        }

        protected override void OnFaviconUrlChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IList<string> urls)
        {
            if (_onFaviconUrl == null || urls == null || urls.Count == 0) return;

            string pageUrl = null;
            try
            {
                if (browser != null && browser.MainFrame != null && !string.IsNullOrEmpty(browser.MainFrame.Url))
                {
                    pageUrl = browser.MainFrame.Url;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(pageUrl) && chromiumWebBrowser != null)
            {
                pageUrl = chromiumWebBrowser.Address;
            }

            if (string.IsNullOrEmpty(pageUrl) || InternalPageHelper.IsStartPage(pageUrl)) return;

            string selectedUrl = null;

            // 1. Search for dark mode favicon URL first
            for (int i = 0; i < urls.Count; i++)
            {
                string u = urls[i];
                if (!string.IsNullOrEmpty(u) && IsDarkModeFaviconUrl(u))
                {
                    selectedUrl = u;
                    break;
                }
            }

            // 2. Fallback to first non-empty URL
            if (string.IsNullOrEmpty(selectedUrl))
            {
                for (int i = 0; i < urls.Count; i++)
                {
                    if (!string.IsNullOrEmpty(urls[i]))
                    {
                        selectedUrl = urls[i];
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(selectedUrl))
            {
                _onFaviconUrl(pageUrl, selectedUrl);
            }
        }

        private static bool IsDarkModeFaviconUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string lower = url.ToLowerInvariant();
            return lower.Contains("dark") || lower.Contains("night") || lower.Contains("black") || lower.Contains("color-scheme=dark");
        }

        protected override void OnFullscreenModeChange(IWebBrowser chromiumWebBrowser, IBrowser browser, bool fullscreen)
        {
            if (_onFullscreenModeChange != null)
            {
                System.Windows.Forms.Control ctrl = chromiumWebBrowser as System.Windows.Forms.Control;
                if (ctrl != null && ctrl.InvokeRequired)
                {
                    ctrl.BeginInvoke(new Action(() => _onFullscreenModeChange(fullscreen)));
                }
                else
                {
                    _onFullscreenModeChange(fullscreen);
                }
            }
        }
    }
}
