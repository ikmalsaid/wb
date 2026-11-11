using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CefSharp;
using wb.UI;

namespace wb.Browser.Handlers
{
    public class LifeSpanHandler : ILifeSpanHandler
    {
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        private const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOW = 5;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private const uint WM_SETICON = 0x80;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const uint WS_CLIPCHILDREN = 0x02000000;
        private const uint WS_CLIPSIBLINGS = 0x04000000;

        private readonly Action<string> _openNewTabAction;

        public LifeSpanHandler(Action<string> openNewTabAction)
        {
            _openNewTabAction = openNewTabAction;
        }

        public bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser newBrowser)
        {
            newBrowser = null;

            if (browserSettings != null)
            {
                browserSettings.BackgroundColor = 0xFF202124U;
            }

            // Determine if this request is an explicit popup window (e.g. window.open, OAuth dialog, Age verification, SSO login)
            bool isPopup = targetDisposition == WindowOpenDisposition.NewPopup ||
                           targetDisposition == WindowOpenDisposition.NewWindow ||
                           (popupFeatures != null && (popupFeatures.IsPopup || popupFeatures.Width.HasValue || popupFeatures.Height.HasValue));

            if (isPopup)
            {
                // Crucial: Allow JavaScript cross-window access so window.opener and postMessage work
                noJavascriptAccess = false;

                // Ensure ParentWindowHandle remains IntPtr.Zero so CEF creates an independent top-level window.
                // Specify Style explicitly without WS_VISIBLE (0x10000000) so CEF creates the window initially hidden,
                // allowing DWM immersive dark mode to be applied during OnAfterCreated before initial display.
                if (windowInfo != null)
                {
                    windowInfo.ParentWindowHandle = IntPtr.Zero;
                    windowInfo.Style = WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN | WS_CLIPSIBLINGS;
                }

                // Apply popup dimensions and position if specified by the webpage
                if (popupFeatures != null && windowInfo != null)
                {
                    if (popupFeatures.Width.HasValue) windowInfo.Width = popupFeatures.Width.Value;
                    if (popupFeatures.Height.HasValue) windowInfo.Height = popupFeatures.Height.Value;
                    if (popupFeatures.X.HasValue) windowInfo.X = popupFeatures.X.Value;
                    if (popupFeatures.Y.HasValue) windowInfo.Y = popupFeatures.Y.Value;
                }

                if (!string.IsNullOrEmpty(targetFrameName) && windowInfo != null && string.IsNullOrEmpty(windowInfo.WindowName))
                {
                    windowInfo.WindowName = targetFrameName;
                }

                // Return false to allow CEF to create the native popup window with window.opener intact
                return false;
            }

            // Normal link navigations (e.g. <a href="..." target="_blank">, middle-click, Ctrl+click)
            if (_openNewTabAction != null && !string.IsNullOrEmpty(targetUrl))
            {
                _openNewTabAction(targetUrl);
                return true; // Suppress default OS popup window, handled as internal tab
            }

            return false;
        }

        public void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser)
        {
            if (browser != null && browser.IsPopup)
            {
                try
                {
                    IBrowserHost host = browser.GetHost();
                    if (host != null && !host.IsDisposed)
                    {
                        PasskeyFallbackHelper.RegisterOnBrowserHost(host);
                    }

                    IntPtr hwnd = host != null ? host.GetWindowHandle() : IntPtr.Zero;
                    if (hwnd != IntPtr.Zero)
                    {
                        IntPtr rootHwnd = GetAncestor(hwnd, GA_ROOT);
                        if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;

                        // Apply immersive dark mode to popup window frame
                        UITheme.EnableImmersiveDarkMode(rootHwnd);

                        // Ensure popup window is shown
                        ShowWindow(rootHwnd, SW_SHOW);

                        // Apply application icon to popup window
                        Icon appIcon = WbAppIcon.GetAppIcon();
                        if (appIcon != null)
                        {
                            SendMessage(rootHwnd, WM_SETICON, (IntPtr)ICON_BIG, appIcon.Handle);
                            SendMessage(rootHwnd, WM_SETICON, (IntPtr)ICON_SMALL, appIcon.Handle);
                        }
                    }
                }
                catch { }
            }
        }

        public bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
        {
            // Allow CEF to close the popup window when window.close() is called or user clicks close
            return false;
        }

        public void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
        {
        }
    }
}

