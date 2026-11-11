using System;
using System.Windows.Forms;
using CefSharp;
using wb.Core;

namespace wb.Browser.Handlers
{
    public class BrowserKeyboardHandler : IKeyboardHandler
    {
        private readonly Action _onPrintPdf;
        private readonly Action _onPrintSystem;
        private readonly Action _onFind;
        private readonly Action _onNewTab;
        private readonly Action _onCloseTab;
        private readonly Action _onDownloads;
        private readonly Action _onHistory;
        private readonly Action _onBookmarks;
        private readonly Action _onViewSource;
        private readonly Action _onDevTools;
        private readonly Action<double> _onZoom;
        private readonly Action _onResetZoom;
        private readonly Action _onNewWindow;
        private readonly Action _onNewIncognitoWindow;
        private readonly Action _onReopenClosedTab;
        private readonly Action _onToggleFullScreen;
        private readonly Func<bool> _isFullScreenFunc;
        private readonly Action _onNextTab;
        private readonly Action _onPrevTab;
        private readonly Action<int> _onSelectTab;
        private readonly Action _onScreenshotVisible;
        private readonly Action _onScreenshotFullPage;
        private readonly Action _onScreenshotSelectedArea;
        private readonly Action _onClearBrowsingData;
        private readonly Action _onToggleBookmark;
        private readonly Action _onFocusAddressBar;
        private readonly Action<bool> _onReload;
        private readonly Action _onGoBack;
        private readonly Action _onGoForward;
        private readonly Action _onEscape;
        private readonly Func<bool> _canHandleEscapeFunc;

        public BrowserKeyboardHandler(
            Action onPrintPdf,
            Action onPrintSystem,
            Action onFind,
            Action onNewTab,
            Action onCloseTab,
            Action onDownloads,
            Action onHistory,
            Action onBookmarks,
            Action onViewSource,
            Action onDevTools,
            Action<double> onZoom,
            Action onResetZoom,
            Action onNewWindow = null,
            Action onReopenClosedTab = null,
            Action onToggleFullScreen = null,
            Func<bool> isFullScreenFunc = null,
            Action onNextTab = null,
            Action onPrevTab = null,
            Action<int> onSelectTab = null,
            Action onNewIncognitoWindow = null,
            Action onScreenshotVisible = null,
            Action onScreenshotFullPage = null,
            Action onScreenshotSelectedArea = null,
            Action onClearBrowsingData = null,
            Action onToggleBookmark = null,
            Action onFocusAddressBar = null,
            Action<bool> onReload = null,
            Action onGoBack = null,
            Action onGoForward = null,
            Action onEscape = null,
            Func<bool> canHandleEscapeFunc = null)
        {
            _onPrintPdf = onPrintPdf;
            _onPrintSystem = onPrintSystem;
            _onFind = onFind;
            _onNewTab = onNewTab;
            _onCloseTab = onCloseTab;
            _onDownloads = onDownloads;
            _onHistory = onHistory;
            _onBookmarks = onBookmarks;
            _onViewSource = onViewSource;
            _onDevTools = onDevTools;
            _onZoom = onZoom;
            _onResetZoom = onResetZoom;
            _onNewWindow = onNewWindow;
            _onNewIncognitoWindow = onNewIncognitoWindow;
            _onReopenClosedTab = onReopenClosedTab;
            _onToggleFullScreen = onToggleFullScreen;
            _isFullScreenFunc = isFullScreenFunc;
            _onNextTab = onNextTab;
            _onPrevTab = onPrevTab;
            _onSelectTab = onSelectTab;
            _onScreenshotVisible = onScreenshotVisible;
            _onScreenshotFullPage = onScreenshotFullPage;
            _onScreenshotSelectedArea = onScreenshotSelectedArea;
            _onClearBrowsingData = onClearBrowsingData;
            _onToggleBookmark = onToggleBookmark;
            _onFocusAddressBar = onFocusAddressBar;
            _onReload = onReload;
            _onGoBack = onGoBack;
            _onGoForward = onGoForward;
            _onEscape = onEscape;
            _canHandleEscapeFunc = canHandleEscapeFunc;
        }

        public bool OnPreKeyEvent(IWebBrowser chromiumWebBrowser, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers, bool isSystemKey, ref bool isKeyboardShortcut)
        {
            if (type != KeyType.RawKeyDown && type != KeyType.KeyDown) return false;

            bool ctrl = (modifiers & CefEventFlags.ControlDown) != 0;
            bool shift = (modifiers & CefEventFlags.ShiftDown) != 0;
            bool alt = (modifiers & CefEventFlags.AltDown) != 0;

            if (alt && !ctrl && !shift && windowsKeyCode == (int)Keys.Left)
            {
                if (_onGoBack != null) InvokeOnUi(chromiumWebBrowser, _onGoBack);
                return true;
            }
            if (alt && !ctrl && !shift && windowsKeyCode == (int)Keys.Right)
            {
                if (_onGoForward != null) InvokeOnUi(chromiumWebBrowser, _onGoForward);
                return true;
            }
            if ((ctrl && !shift && !alt && windowsKeyCode == (int)Keys.L) || (alt && !ctrl && !shift && windowsKeyCode == (int)Keys.D))
            {
                if (_onFocusAddressBar != null) InvokeOnUi(chromiumWebBrowser, _onFocusAddressBar);
                return true;
            }
            if (ctrl && !shift && !alt && windowsKeyCode == (int)Keys.D)
            {
                if (_onToggleBookmark != null) InvokeOnUi(chromiumWebBrowser, _onToggleBookmark);
                return true;
            }
            if (!ctrl && !shift && !alt && windowsKeyCode == (int)Keys.F5)
            {
                isKeyboardShortcut = true;
                if (_onReload != null) InvokeOnUi(chromiumWebBrowser, () => _onReload(false));
                else if (browser != null && browser.IsValid) browser.Reload(false);
                return true;
            }
            if (ctrl && !shift && !alt && windowsKeyCode == (int)Keys.R)
            {
                isKeyboardShortcut = true;
                if (_onReload != null) InvokeOnUi(chromiumWebBrowser, () => _onReload(false));
                else if (browser != null && browser.IsValid) browser.Reload(false);
                return true;
            }
            if ((ctrl && windowsKeyCode == (int)Keys.F5) || (shift && windowsKeyCode == (int)Keys.F5) || (ctrl && shift && (windowsKeyCode == (int)Keys.R || windowsKeyCode == (int)Keys.F5)))
            {
                isKeyboardShortcut = true;
                if (_onReload != null) InvokeOnUi(chromiumWebBrowser, () => _onReload(true));
                else if (browser != null && browser.IsValid) browser.Reload(true);
                return true;
            }
            if (ctrl && shift && windowsKeyCode == (int)Keys.Delete)
            {
                if (_onClearBrowsingData != null) InvokeOnUi(chromiumWebBrowser, _onClearBrowsingData);
                return true;
            }
            if (ctrl && shift && windowsKeyCode == (int)Keys.T)
            {
                if (_onReopenClosedTab != null) InvokeOnUi(chromiumWebBrowser, _onReopenClosedTab);
                return true;
            }
            if (ctrl && shift && windowsKeyCode == (int)Keys.S)
            {
                if (_onScreenshotVisible != null) InvokeOnUi(chromiumWebBrowser, _onScreenshotVisible);
                return true;
            }
            if (ctrl && shift && windowsKeyCode == (int)Keys.F)
            {
                if (_onScreenshotFullPage != null) InvokeOnUi(chromiumWebBrowser, _onScreenshotFullPage);
                return true;
            }
            if (ctrl && shift && windowsKeyCode == (int)Keys.A)
            {
                if (_onScreenshotSelectedArea != null) InvokeOnUi(chromiumWebBrowser, _onScreenshotSelectedArea);
                return true;
            }
            if (ctrl && shift && windowsKeyCode == (int)Keys.N)
            {
                if (_onNewIncognitoWindow != null) InvokeOnUi(chromiumWebBrowser, _onNewIncognitoWindow);
                return true;
            }
            if (ctrl && !shift && windowsKeyCode == (int)Keys.N)
            {
                if (_onNewWindow != null) InvokeOnUi(chromiumWebBrowser, _onNewWindow);
                return true;
            }
            if (ctrl && !shift && windowsKeyCode == (int)Keys.P)
            {
                if (_onPrintPdf != null) InvokeOnUi(chromiumWebBrowser, _onPrintPdf);
                return true;
            }
            if (ctrl && shift && windowsKeyCode == (int)Keys.P)
            {
                if (_onPrintSystem != null) InvokeOnUi(chromiumWebBrowser, _onPrintSystem);
                return true;
            }
            if (ctrl && !shift && windowsKeyCode == (int)Keys.T)
            {
                if (_onNewTab != null) InvokeOnUi(chromiumWebBrowser, _onNewTab);
                return true;
            }
            if (ctrl && !shift && windowsKeyCode == (int)Keys.W)
            {
                if (_onCloseTab != null) InvokeOnUi(chromiumWebBrowser, _onCloseTab);
                return true;
            }
            if (ctrl && !shift && windowsKeyCode == (int)Keys.F)
            {
                if (_onFind != null) InvokeOnUi(chromiumWebBrowser, _onFind);
                return true;
            }
            if (ctrl && !shift && windowsKeyCode == (int)Keys.J)
            {
                if (_onDownloads != null) InvokeOnUi(chromiumWebBrowser, _onDownloads);
                return true;
            }
            if (ctrl && !shift && windowsKeyCode == (int)Keys.H)
            {
                if (_onHistory != null) InvokeOnUi(chromiumWebBrowser, _onHistory);
                return true;
            }
            if (ctrl && !shift && windowsKeyCode == (int)Keys.B)
            {
                if (_onBookmarks != null) InvokeOnUi(chromiumWebBrowser, _onBookmarks);
                return true;
            }
            if (ctrl && shift && windowsKeyCode == (int)Keys.D)
            {
                InvokeOnUi(chromiumWebBrowser, () => LogcatHelper.ToggleDebugConsole());
                return true;
            }
            if (ctrl && !shift && windowsKeyCode == (int)Keys.U)
            {
                if (_onViewSource != null) InvokeOnUi(chromiumWebBrowser, _onViewSource);
                return true;
            }
            if (windowsKeyCode == (int)Keys.F12)
            {
                if (_onDevTools != null) InvokeOnUi(chromiumWebBrowser, _onDevTools);
                return true;
            }
            if (windowsKeyCode == (int)Keys.F11)
            {
                if (_onToggleFullScreen != null) InvokeOnUi(chromiumWebBrowser, _onToggleFullScreen);
                return true;
            }
            if (windowsKeyCode == (int)Keys.Escape)
            {
                if (_canHandleEscapeFunc != null && _canHandleEscapeFunc())
                {
                    if (_onEscape != null) InvokeOnUi(chromiumWebBrowser, _onEscape);
                    else if (_onToggleFullScreen != null) InvokeOnUi(chromiumWebBrowser, _onToggleFullScreen);
                    return true;
                }
                if (_isFullScreenFunc != null && _isFullScreenFunc())
                {
                    if (_onEscape != null) InvokeOnUi(chromiumWebBrowser, _onEscape);
                    else if (_onToggleFullScreen != null) InvokeOnUi(chromiumWebBrowser, _onToggleFullScreen);
                    return true;
                }
            }
            if (ctrl && (windowsKeyCode == (int)Keys.Oemplus || windowsKeyCode == (int)Keys.Add))
            {
                if (_onZoom != null) InvokeOnUi(chromiumWebBrowser, () => _onZoom(0.5));
                return true;
            }
            if (ctrl && (windowsKeyCode == (int)Keys.OemMinus || windowsKeyCode == (int)Keys.Subtract))
            {
                if (_onZoom != null) InvokeOnUi(chromiumWebBrowser, () => _onZoom(-0.5));
                return true;
            }
            if (ctrl && (windowsKeyCode == (int)Keys.D0 || windowsKeyCode == (int)Keys.NumPad0))
            {
                if (_onResetZoom != null) InvokeOnUi(chromiumWebBrowser, _onResetZoom);
                return true;
            }
            if (ctrl && !shift && (windowsKeyCode == (int)Keys.Tab || windowsKeyCode == (int)Keys.PageDown || windowsKeyCode == (int)Keys.Next))
            {
                if (_onNextTab != null) InvokeOnUi(chromiumWebBrowser, _onNextTab);
                return true;
            }
            if ((ctrl && shift && windowsKeyCode == (int)Keys.Tab) || (ctrl && (windowsKeyCode == (int)Keys.PageUp || windowsKeyCode == (int)Keys.Prior)))
            {
                if (_onPrevTab != null) InvokeOnUi(chromiumWebBrowser, _onPrevTab);
                return true;
            }
            if (ctrl && !shift)
            {
                if (windowsKeyCode >= (int)Keys.D1 && windowsKeyCode <= (int)Keys.D9)
                {
                    int tabIdx = windowsKeyCode - (int)Keys.D1;
                    if (_onSelectTab != null) InvokeOnUi(chromiumWebBrowser, () => _onSelectTab(tabIdx));
                    return true;
                }
                if (windowsKeyCode >= (int)Keys.NumPad1 && windowsKeyCode <= (int)Keys.NumPad9)
                {
                    int tabIdx = windowsKeyCode - (int)Keys.NumPad1;
                    if (_onSelectTab != null) InvokeOnUi(chromiumWebBrowser, () => _onSelectTab(tabIdx));
                    return true;
                }
            }

            return false;
        }

        public bool OnKeyEvent(IWebBrowser chromiumWebBrowser, IBrowser browser, KeyType type, int windowsKeyCode, int nativeKeyCode, CefEventFlags modifiers, bool isSystemKey)
        {
            if (type != KeyType.RawKeyDown && type != KeyType.KeyDown) return false;
            bool ctrl = (modifiers & CefEventFlags.ControlDown) != 0;
            bool shift = (modifiers & CefEventFlags.ShiftDown) != 0;
            bool alt = (modifiers & CefEventFlags.AltDown) != 0;

            if (!ctrl && !shift && !alt && windowsKeyCode == (int)Keys.F5)
            {
                if (_onReload != null) InvokeOnUi(chromiumWebBrowser, () => _onReload(false));
                else if (browser != null && browser.IsValid) browser.Reload(false);
                return true;
            }
            if ((ctrl && windowsKeyCode == (int)Keys.F5) || (shift && windowsKeyCode == (int)Keys.F5))
            {
                if (_onReload != null) InvokeOnUi(chromiumWebBrowser, () => _onReload(true));
                else if (browser != null && browser.IsValid) browser.Reload(true);
                return true;
            }
            return false;
        }

        private void InvokeOnUi(IWebBrowser browser, Action action)
        {
            Control ctrl = browser as Control;
            if (ctrl != null && ctrl.InvokeRequired)
            {
                ctrl.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
    }
}
