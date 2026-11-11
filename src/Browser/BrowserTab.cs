using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using wb.Core;
using wb.AdBlock;
using wb.Bookmarks;
using wb.Downloads;
using wb.History;
using wb.SponsorBlock;
using wb.Browser.Handlers;
using wb.UI;
using wb.Screenshot;
using wb.DataPortability;

namespace wb.Browser
{
    public class TabNavigationEntry
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public DateTime Timestamp { get; set; }

        public TabNavigationEntry(string url, string title)
        {
            Url = url ?? string.Empty;
            Title = title ?? string.Empty;
            Timestamp = DateTime.Now;
        }
    }

    public class BrowserTab : TabPage, IBrowserTabState, IElementPickerTarget
    {
        public ChromiumWebBrowser Browser { get; private set; }
        public string CurrentUrl { get; private set; }
        public string CurrentTitle { get; private set; }
        public bool CanGoBack { get; private set; }
        public bool CanGoForward { get; private set; }
        public bool IsLoading { get; private set; }
        public double ZoomLevel { get; private set; }
        public Image Favicon { get; private set; }
        private int _blockedCount;

        public int BlockedCount
        {
            get { return _blockedCount; }
        }

        public event EventHandler<string> TitleUpdated;
        public event EventHandler<string> AddressUpdated;
        public event EventHandler<LoadingStateChangedEventArgs> LoadingStateUpdated;
        public event EventHandler<string> StatusMessageUpdated;
        public event EventHandler<string> OpenNewTabRequested;
        public event EventHandler<string> OpenNewWindowRequested;
        public event EventHandler<int> BlockedCountChanged;
        public event EventHandler FaviconChanged;
        public event Action ViewSourceRequested;
        public event EventHandler CloseRequested;
        public event EventHandler ElementPickerStarted;
        public event EventHandler<ElementSelectedEventArgs> ElementSelectedForBlock;
        public event EventHandler ElementPickerCancelled;
        public event EventHandler SponsorBlockMenuRequested;
        public event EventHandler PinStateChanged;
        public event EventHandler AudioStateChanged;
        public event EventHandler MediaCaptureStateChanged;
        public event EventHandler LoadingStateChanged;
        public event EventHandler<string> InternalPageLoading;
        public event EventHandler<string> NavigatingFromInternalPage;
        public event EventHandler<bool> FullscreenModeChanged;

        private BookmarksView _bookmarksView;
        private HistoryView _historyView;
        private DownloadsView _downloadsView;
        private NewTabView _newTabView;
        private IncognitoNewTabView _incognitoNewTabView;
        private ScreenshotView _screenshotView;
        private ImportDataView _importDataView;
        private ExportDataView _exportDataView;
        private bool _isViewSource = false;
        private readonly EventHandler _accentChangedHandler;

        private readonly System.Collections.Generic.List<TabNavigationEntry> _sessionHistory = new System.Collections.Generic.List<TabNavigationEntry>();
        private int _historyIndex = -1;
        private bool _isNavigatingHistory = false;
        private string _lastElementSelectedSig;
        private DateTime _lastElementSelectedTime;

        public bool IsViewSource
        {
            get { return _isViewSource; }
        }

        public bool IsIncognito { get; private set; }
        public IRequestContext RequestContext { get; private set; }
        public event EventHandler<string> OpenNewIncognitoWindowRequested;

        public bool IsInternalPage
        {
            get { return InternalPageHelper.IsInternalPage(CurrentUrl); }
        }

        public static bool IsBenchmarkUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.IndexOf("browserbench.org", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("speedometer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("web.basemark.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("krakenbenchmark.mozilla.org", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("octane", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("motionmark", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("jetstream", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public bool IsBenchmark
        {
            get { return IsBenchmarkUrl(CurrentUrl); }
        }

        private bool _isPinned;
        private bool _isAudioMuted;
        private bool _isPlayingAudio;
        private readonly HashSet<string> _activeAudioSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _audioStateLock = new object();
        private string _activeErrorOriginalUrl;
        private string _pendingUrlToLoad;
        private string _pendingNavigationUrl;
        private bool _isTransferring;
        private bool _darkEmulationConfigured = false;
        private bool _hasInternalFavicon;
        private bool _expectingNavigationFromInternal;

        public void SetPendingNavigationUrl(string url)
        {
            _pendingNavigationUrl = url;
        }

        public string EffectiveUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(_pendingNavigationUrl) && !InternalPageHelper.IsInternalUrl(_pendingNavigationUrl))
                {
                    return _pendingNavigationUrl;
                }
                if (!string.IsNullOrEmpty(CurrentUrl) && !InternalPageHelper.IsInternalUrl(CurrentUrl))
                {
                    return CurrentUrl;
                }
                return string.Empty;
            }
        }

        public bool IsTransferring
        {
            get { return _isTransferring; }
            set { _isTransferring = value; }
        }

        public bool IsPinned
        {
            get { return _isPinned; }
            set
            {
                if (_isPinned != value)
                {
                    _isPinned = value;
                    EventHandler h = PinStateChanged;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
        }

        public bool IsPlayingAudio
        {
            get { return _isPlayingAudio; }
            private set
            {
                if (_isPlayingAudio != value)
                {
                    _isPlayingAudio = value;
                    EventHandler h = AudioStateChanged;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
        }

        public bool IsAudioMuted
        {
            get { return _isAudioMuted; }
        }

        private bool _isMediaCaptured;
        private string _mediaCaptureType;

        public bool IsMediaCaptured
        {
            get { return _isMediaCaptured; }
            private set
            {
                if (_isMediaCaptured != value)
                {
                    _isMediaCaptured = value;
                    EventHandler h = MediaCaptureStateChanged;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
        }

        public string MediaCaptureType
        {
            get { return _mediaCaptureType; }
            private set { _mediaCaptureType = value; }
        }

        public void SetMediaCaptureState(bool isCaptured, string captureType = null)
        {
            if (isCaptured && (string.IsNullOrEmpty(CurrentUrl) || InternalPageHelper.IsStartPage(CurrentUrl)))
            {
                isCaptured = false;
            }
            _mediaCaptureType = isCaptured ? captureType : null;
            this.IsMediaCaptured = isCaptured;
        }

        public void SetAudioPlayingState(bool isPlaying)
        {
            if (isPlaying && (string.IsNullOrEmpty(CurrentUrl) || InternalPageHelper.IsStartPage(CurrentUrl)))
            {
                isPlaying = false;
            }
            if (!isPlaying)
            {
                lock (_audioStateLock)
                {
                    _activeAudioSources.Clear();
                }
            }
            this.IsPlayingAudio = isPlaying;
        }

        public void SetAudioMuted(bool muted)
        {
            _isAudioMuted = muted;
            try
            {
                if (Browser != null && Browser.IsBrowserInitialized)
                {
                    var host = Browser.GetBrowserHost();
                    if (host != null)
                    {
                        host.SetAudioMuted(muted);
                    }
                }
            }
            catch { }

            EventHandler h = AudioStateChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void CheckSiteMuteStatus(string url)
        {
            if (string.IsNullOrEmpty(url) || InternalPageHelper.IsStartPage(url))
            {
                if (_isAudioMuted)
                {
                    SetAudioMuted(false);
                }
                return;
            }
            bool shouldMute = AppServices.SiteMute.IsDomainMuted(url);
            if (shouldMute != _isAudioMuted)
            {
                SetAudioMuted(shouldMute);
            }
            else if (_isAudioMuted && Browser != null && Browser.IsBrowserInitialized)
            {
                try
                {
                    var host = Browser.GetBrowserHost();
                    if (host != null)
                    {
                        host.SetAudioMuted(true);
                    }
                }
                catch { }
            }
        }

        public BrowserTab(string initialUrl)
            : this(initialUrl, false, null)
        {
        }

        public BrowserTab(string initialUrl, bool isIncognito, IRequestContext requestContext = null)
        {
            IsIncognito = isIncognito;
            RequestContext = requestContext;

            if (string.IsNullOrEmpty(initialUrl) || string.Equals(initialUrl, InternalPageHelper.NewTabUrl, StringComparison.OrdinalIgnoreCase) || string.Equals(initialUrl, InternalPageHelper.IncognitoUrl, StringComparison.OrdinalIgnoreCase) || InternalPageHelper.IsStartPage(initialUrl))
            {
                CurrentUrl = isIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl;
                CurrentTitle = "New Tab";
                this.Text = "New Tab";
                _hasInternalFavicon = true;
                Favicon = InternalPageHelper.GetInternalFavicon(CurrentUrl, isIncognito);
            }
            else if (InternalPageHelper.IsViewSourceUrl(initialUrl))
            {
                _isViewSource = true;
                string normUrl = InternalPageHelper.NormalizeViewSourceUrl(initialUrl);
                CurrentUrl = normUrl;
                CurrentTitle = TitleHelper.NormalizeAndWrapTitle("view-source:" + InternalPageHelper.ExtractViewSourceTarget(normUrl));
                this.Text = CurrentTitle;
                Image cached = AppServices.Favicons.GetFavicon(normUrl);
                if (cached != null)
                {
                    Favicon = cached;
                }
                else
                {
                    AppServices.Favicons.FetchFaviconAsync(normUrl, null, img => SetFaviconForUrl(normUrl, img));
                }
            }
            else if (InternalPageHelper.IsInternalUrl(initialUrl))
            {
                string normUrl = InternalPageHelper.NormalizeInternalUrl(initialUrl);
                if (isIncognito && (InternalPageHelper.GetInternalPageType(normUrl) == InternalPageType.NewTab || string.Equals(normUrl, InternalPageHelper.NewTabUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    normUrl = InternalPageHelper.IncognitoUrl;
                }
                else if (!isIncognito && (InternalPageHelper.GetInternalPageType(normUrl) == InternalPageType.Incognito || string.Equals(normUrl, InternalPageHelper.IncognitoUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    normUrl = InternalPageHelper.NewTabUrl;
                }
                CurrentUrl = normUrl;
                CurrentTitle = InternalPageHelper.GetInternalTitle(normUrl);
                this.Text = CurrentTitle;
                _hasInternalFavicon = true;
                Favicon = InternalPageHelper.GetInternalFavicon(normUrl, isIncognito);
            }
            else
            {
                CurrentUrl = initialUrl;
                CurrentTitle = AppStrings.Browser.TabLoadingDefault;
                this.Text = AppStrings.Browser.TabLoadingDefault;
                Image cached = AppServices.Favicons.GetFavicon(initialUrl);
                if (cached != null)
                {
                    Favicon = cached;
                }
                else
                {
                    AppServices.Favicons.FetchFaviconAsync(initialUrl, null, img => SetFaviconForUrl(initialUrl, img));
                }
            }

            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            this.DoubleBuffered = true;
            this.UseVisualStyleBackColor = false;
            this.BackColor = MaterialColors.ToolbarBackground;
            this.Margin = Padding.Empty;
            this.Padding = Padding.Empty;
            InitializeBrowser();

            if (InternalPageHelper.IsInternalUrl(CurrentUrl))
            {
                if (Browser != null) Browser.Visible = false;
                ShowInternalPage(CurrentUrl);
            }
            else
            {
                _pendingUrlToLoad = CurrentUrl;
                if (Browser != null && Browser.IsBrowserInitialized)
                {
                    Browser.LoadUrl(CurrentUrl);
                }
                CheckSiteMuteStatus(CurrentUrl);
            }

            _accentChangedHandler = (s, e) =>
            {
                if (this.IsDisposed) return;
                if (this.InvokeRequired)
                {
                    try { this.BeginInvoke(new Action(() => _accentChangedHandler(s, e))); } catch { }
                    return;
                }
                if (InternalPageHelper.IsStartPage(CurrentUrl) || InternalPageHelper.IsInternalUrl(CurrentUrl))
                {
                    _hasInternalFavicon = true;
                    SetFavicon(InternalPageHelper.GetInternalFavicon(CurrentUrl, IsIncognito));
                }
            };
            ThemeManager.AccentColorChanged += _accentChangedHandler;
        }

        private void InitializeBrowser()
        {
            string startBrowserUrl = InternalPageHelper.IsInternalUrl(CurrentUrl) ? "about:blank" : CurrentUrl;
            if (RequestContext != null)
            {
                Browser = new ChromiumWebBrowser(startBrowserUrl, RequestContext);
            }
            else
            {
                Browser = new ChromiumWebBrowser(startBrowserUrl);
            }
            Browser.Dock = DockStyle.Fill;
            Browser.Margin = Padding.Empty;
            Browser.Padding = Padding.Empty;
            Browser.BackColor = MaterialColors.ToolbarBackground;
            if (Browser.BrowserSettings == null)
            {
                Browser.BrowserSettings = new BrowserSettings();
            }
            Browser.BrowserSettings.BackgroundColor = Cef.ColorSetARGB(255, 32, 33, 36);
            this.Controls.Add(Browser);

            if (InternalPageHelper.IsInternalUrl(CurrentUrl))
            {
                Browser.Visible = false;
            }

            Browser.LifeSpanHandler = new LifeSpanHandler(OnNewTabFromPopup);
            Browser.DisplayHandler = new BrowserDisplayHandler(OnFaviconUrlFound, HandleWbAction, isFull =>
            {
                var handler = FullscreenModeChanged;
                if (handler != null) handler(this, isFull);
            });
            Browser.MenuHandler = new CustomMenuHandler(OnNewTabFromPopup, OnNewWindowFromPopup, OnNewIncognitoWindowFromPopup, OnViewSourceRequested, StartElementPicker);
            Browser.DownloadHandler = new DownloadHandler();
            Browser.RequestHandler = new BrowserRequestHandler(this);
            Browser.PermissionHandler = new BrowserPermissionHandler(this);

            Browser.TitleChanged += Browser_TitleChanged;
            Browser.AddressChanged += Browser_AddressChanged;
            Browser.LoadingStateChanged += Browser_LoadingStateChanged;
            Browser.StatusMessage += Browser_StatusMessage;
            Browser.FrameLoadStart += Browser_FrameLoadStart;
            Browser.FrameLoadEnd += Browser_FrameLoadEnd;
            Browser.LoadError += Browser_LoadError;
            Browser.JavascriptMessageReceived += Browser_JavascriptMessageReceived;
            Browser.Enter += (s, e) =>
            {
                if (!ToolStripDismissHook.HasActiveDropDownFocus())
                {
                    ToolStripDismissHook.CloseAll();
                }
            };
            Browser.IsBrowserInitializedChanged += (s, e) =>
            {
                if (Browser != null && Browser.IsBrowserInitialized)
                {
                    SafeInvoke(() =>
                    {
                        ApplyDarkColorSchemeEmulation();
                        if (_isAudioMuted)
                        {
                            try
                            {
                                var host = Browser.GetBrowserHost();
                                if (host != null) host.SetAudioMuted(true);
                            }
                            catch { }
                        }
                        string toLoad = _pendingUrlToLoad;
                        _pendingUrlToLoad = null;

                        if (!string.IsNullOrEmpty(toLoad))
                        {
                            Browser.LoadUrl(toLoad);
                        }
                    });
                }
            };
            if (Browser != null && Browser.IsBrowserInitialized)
            {
                SafeInvoke(() =>
                {
                    ApplyDarkColorSchemeEmulation();
                    if (_isAudioMuted)
                    {
                        try
                        {
                            var host = Browser.GetBrowserHost();
                            if (host != null) host.SetAudioMuted(true);
                        }
                        catch { }
                    }
                    string toLoad = _pendingUrlToLoad;
                    _pendingUrlToLoad = null;

                    if (!string.IsNullOrEmpty(toLoad))
                    {
                        Browser.LoadUrl(toLoad);
                    }
                });
            }
        }

        private Control GetCachedInternalView(InternalPageType type)
        {
            switch (type)
            {
                case InternalPageType.Bookmarks:
                    return _bookmarksView;
                case InternalPageType.History:
                    return _historyView;
                case InternalPageType.Downloads:
                    return _downloadsView;
                case InternalPageType.NewTab:
                    return IsIncognito ? (Control)_incognitoNewTabView : (Control)_newTabView;
                case InternalPageType.Incognito:
                    return _incognitoNewTabView;
                case InternalPageType.Screenshot:
                    return _screenshotView;
                case InternalPageType.ImportData:
                    return _importDataView;
                case InternalPageType.ExportData:
                    return _exportDataView;
                default:
                    return null;
            }
        }

        public void EnsureBrowserAlive()
        {
            if (InternalPageHelper.IsInternalUrl(CurrentUrl))
            {
                InternalPageType type = InternalPageHelper.GetInternalPageType(CurrentUrl);
                Control cachedView = GetCachedInternalView(type);
                if (cachedView != null && !cachedView.IsDisposed && cachedView.Visible)
                {
                    // Pre-warm the underlying browser control so navigating out is instant
                    if (Browser != null && !Browser.IsDisposed && this.IsHandleCreated && !Browser.IsHandleCreated)
                    {
                        try { Browser.CreateControl(); } catch { }
                    }
                    return;
                }
                ShowInternalPage(CurrentUrl);
                if (Browser != null && !Browser.IsDisposed && this.IsHandleCreated && !Browser.IsHandleCreated)
                {
                    try { Browser.CreateControl(); } catch { }
                }
                return;
            }
            if (Browser == null || Browser.IsDisposed)
            {
                ReinitializeBrowser();
            }
            else if (this.IsHandleCreated && !Browser.IsHandleCreated)
            {
                try { Browser.CreateControl(); } catch { }
            }
        }

        /// <summary>
        /// Smoothly refreshes the tab and browser hosting layout without reloading or resetting website state.
        /// </summary>
        public void RefreshHostLayout()
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.Parent is TabControl)
                {
                    this.Bounds = ((TabControl)this.Parent).DisplayRectangle;
                }
                this.Visible = true;
                if (!this.IsHandleCreated)
                {
                    try { this.CreateControl(); } catch { }
                }

                if (InternalPageHelper.IsInternalUrl(CurrentUrl))
                {
                    if (Browser != null && !Browser.IsDisposed)
                    {
                        Browser.Visible = false;
                    }
                    InternalPageType type = InternalPageHelper.GetInternalPageType(CurrentUrl);
                    Control cachedView = GetCachedInternalView(type);
                    if (cachedView != null && !cachedView.IsDisposed && cachedView.Visible)
                    {
                        cachedView.BringToFront();
                        return;
                    }
                    ShowInternalPage(CurrentUrl);
                    return;
                }

                if (Browser != null && !Browser.IsDisposed)
                {
                    Browser.Bounds = this.ClientRectangle;
                    Browser.Visible = true;
                    Browser.BringToFront();
                    if (!Browser.IsHandleCreated && this.IsHandleCreated)
                    {
                        try { Browser.CreateControl(); } catch { }
                    }
                    Browser.Invalidate();
                    try
                    {
                        if (Browser.IsBrowserInitialized)
                        {
                            var host = Browser.GetBrowserHost();
                            if (host != null)
                            {
                                host.WasResized();
                            }
                            if (!InternalPageHelper.IsInternalUrl(CurrentUrl) &&
                                (string.IsNullOrEmpty(Browser.Address) || string.Equals(Browser.Address, "about:blank", StringComparison.OrdinalIgnoreCase)))
                            {
                                Browser.LoadUrl(CurrentUrl);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Safely transfers input focus to the hosted web browser or tab.
        /// </summary>
        public void FocusBrowser()
        {
            if (this.IsDisposed) return;
            try
            {
                if (InternalPageHelper.IsInternalUrl(CurrentUrl))
                {
                    if (_bookmarksView != null && _bookmarksView.Visible) _bookmarksView.Focus();
                    else if (_historyView != null && _historyView.Visible) _historyView.Focus();
                    else if (_downloadsView != null && _downloadsView.Visible) _downloadsView.Focus();
                    else if (_importDataView != null && _importDataView.Visible) _importDataView.Focus();
                    else if (_exportDataView != null && _exportDataView.Visible) _exportDataView.Focus();
                    else if (_incognitoNewTabView != null && _incognitoNewTabView.Visible) _incognitoNewTabView.FocusSearchBox();
                    else if (_newTabView != null && _newTabView.Visible) _newTabView.FocusSearchBox();
                    else if (this.IsHandleCreated) this.Focus();
                    return;
                }

                if (Browser != null && !Browser.IsDisposed && Browser.IsHandleCreated)
                {
                    Browser.Focus();
                }
                else if (this.IsHandleCreated)
                {
                    this.Focus();
                }
            }
            catch { }
        }

        public void ReinitializeBrowser()
        {
            string targetUrl = CurrentUrl;
            if (Browser != null && !string.IsNullOrEmpty(Browser.Address) && !InternalPageHelper.IsStartPage(Browser.Address))
            {
                targetUrl = Browser.Address;
            }
            if (string.IsNullOrEmpty(targetUrl) || InternalPageHelper.IsStartPage(targetUrl))
            {
                targetUrl = InternalPageHelper.NewTabUrl;
            }

            if (Browser != null)
            {
                try
                {
                    this.Controls.Remove(Browser);
                    Browser.TitleChanged -= Browser_TitleChanged;
                    Browser.AddressChanged -= Browser_AddressChanged;
                    Browser.LoadingStateChanged -= Browser_LoadingStateChanged;
                    Browser.StatusMessage -= Browser_StatusMessage;
                    Browser.FrameLoadStart -= Browser_FrameLoadStart;
                    Browser.FrameLoadEnd -= Browser_FrameLoadEnd;
                    Browser.LoadError -= Browser_LoadError;
                    Browser.JavascriptMessageReceived -= Browser_JavascriptMessageReceived;
                    Browser.Dispose();
                }
                catch { }
                Browser = null;
            }

            CurrentUrl = targetUrl;
            _darkEmulationConfigured = false;
            try
            {
                InitializeBrowser();
            }
            catch { }
        }

        private void OnFaviconUrlFound(string pageUrl, string faviconUrl)
        {
            if (string.IsNullOrEmpty(pageUrl) || InternalPageHelper.IsStartPage(pageUrl)) return;
            if (string.IsNullOrEmpty(faviconUrl)) return;
            if (InternalPageHelper.IsInternalUrl(CurrentUrl) || InternalPageHelper.IsStartPage(CurrentUrl)) return;

            string pageDomain = FaviconManager.ExtractDomain(pageUrl);
            string curDomain = FaviconManager.ExtractDomain(CurrentUrl);
            if (!string.IsNullOrEmpty(pageDomain) &&
                !string.Equals(pageDomain, curDomain, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AppServices.Favicons.FetchFaviconAsync(pageUrl, faviconUrl, img => SetFaviconForUrl(pageUrl, img));
        }

        private static string StripUrlFragment(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            int hashIdx = url.IndexOf('#');
            return hashIdx >= 0 ? url.Substring(0, hashIdx) : url;
        }

        private void SetInternalFavicon(Image img)
        {
            if (this.IsDisposed) return;
            _hasInternalFavicon = true;
            SetFavicon(img);
        }

        private void UpdateFaviconOnNavigation(string oldUrl, string newUrl, bool wasInternal)
        {
            if (this.IsDisposed || string.IsNullOrEmpty(newUrl)) return;
            if (InternalPageHelper.IsInternalUrl(newUrl) || InternalPageHelper.IsStartPage(newUrl) ||
                WebErrorPage.IsErrorPage(newUrl) || WebErrorPage.IsChromiumInternalErrorPage(newUrl))
            {
                return;
            }

            string oldDomain = FaviconManager.ExtractDomain(oldUrl);
            string newDomain = FaviconManager.ExtractDomain(newUrl);
            bool clearInternal = wasInternal || _hasInternalFavicon ||
                                 (!string.IsNullOrEmpty(oldUrl) && WebErrorPage.IsErrorPage(oldUrl));

            if (!string.IsNullOrEmpty(newDomain))
            {
                bool domainChanged = !string.Equals(newDomain, oldDomain, StringComparison.OrdinalIgnoreCase);
                if (clearInternal || domainChanged || Favicon == null)
                {
                    Image cached = AppServices.Favicons.GetFavicon(newUrl);
                    if (clearInternal || domainChanged)
                    {
                        _hasInternalFavicon = false;
                        SetFavicon(cached);
                    }
                    else if (cached != null)
                    {
                        _hasInternalFavicon = false;
                        SetFavicon(cached);
                    }

                    if (cached == null)
                    {
                        AppServices.Favicons.FetchFaviconAsync(newUrl, null, img => SetFaviconForUrl(newUrl, img));
                    }
                }
            }
            else
            {
                bool urlChanged = !string.Equals(StripUrlFragment(oldUrl), StripUrlFragment(newUrl), StringComparison.OrdinalIgnoreCase);
                if (clearInternal || !string.IsNullOrEmpty(oldDomain) || urlChanged)
                {
                    _hasInternalFavicon = false;
                    SetFavicon(null);
                }
            }
        }

        public void SetFaviconForUrl(string forUrl, Image img)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                SafeInvoke(() => SetFaviconForUrl(forUrl, img));
                return;
            }

            if (InternalPageHelper.IsInternalUrl(CurrentUrl) || InternalPageHelper.IsStartPage(CurrentUrl))
            {
                if (img == null)
                {
                    _hasInternalFavicon = true;
                    Favicon = InternalPageHelper.GetInternalFavicon(CurrentUrl, IsIncognito);
                    RaiseFaviconChanged();
                }
                return;
            }

            // If user has navigated away to a different domain, ignore stale favicon
            if (!string.IsNullOrEmpty(forUrl) && !string.IsNullOrEmpty(CurrentUrl))
            {
                string forDomain = FaviconManager.ExtractDomain(forUrl);
                string curDomain = FaviconManager.ExtractDomain(CurrentUrl);
                if (!string.IsNullOrEmpty(forDomain) &&
                    !string.Equals(forDomain, curDomain, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            if (img == null)
            {
                if (_hasInternalFavicon && !WebErrorPage.IsErrorPage(CurrentUrl) && string.IsNullOrEmpty(_activeErrorOriginalUrl))
                {
                    SetFavicon(null);
                }
                return;
            }

            _hasInternalFavicon = false;
            if (Favicon == img) return;
            Favicon = img;
            RaiseFaviconChanged();
        }

        public void SetFavicon(Image img)
        {
            if (this.IsDisposed) return;
            if (img == null)
            {
                _hasInternalFavicon = false;
            }
            if (Favicon == img) return;
            Favicon = img;
            RaiseFaviconChanged();
        }

        private void RaiseFaviconChanged()
        {
            if (this.IsDisposed) return;
            Control host = this.FindForm() ?? this.Parent ?? this;
            if (host != null && !host.IsDisposed && host.IsHandleCreated && host.InvokeRequired)
            {
                try
                {
                    host.BeginInvoke(new Action(delegate
                    {
                        if (!this.IsDisposed)
                        {
                            try
                            {
                                EventHandler handler = FaviconChanged;
                                if (handler != null) handler(this, EventArgs.Empty);
                            }
                            catch { }
                        }
                    }));
                    return;
                }
                catch (ObjectDisposedException) { return; }
                catch { }
            }

            try
            {
                EventHandler h = FaviconChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
            catch { }
        }

        private int _notifyBlockedPending = 0;

        public void IncrementBlockedCount()
        {
            if (this.IsDisposed) return;
            System.Threading.Interlocked.Increment(ref _blockedCount);
            if (System.Threading.Interlocked.CompareExchange(ref _notifyBlockedPending, 1, 0) == 0)
            {
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    try
                    {
                        this.BeginInvoke(new Action(DeliverBlockedCountChanged));
                    }
                    catch
                    {
                        System.Threading.Interlocked.Exchange(ref _notifyBlockedPending, 0);
                    }
                }
                else
                {
                    System.Threading.Interlocked.Exchange(ref _notifyBlockedPending, 0);
                }
            }
        }

        public void ResetBlockedCount()
        {
            if (this.IsDisposed) return;
            System.Threading.Interlocked.Exchange(ref _blockedCount, 0);
            try
            {
                if (this.InvokeRequired)
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(DeliverBlockedCountChanged));
                    }
                }
                else
                {
                    DeliverBlockedCountChanged();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void DeliverBlockedCountChanged()
        {
            System.Threading.Interlocked.Exchange(ref _notifyBlockedPending, 0);
            if (this.IsDisposed) return;
            try
            {
                EventHandler<int> handler = BlockedCountChanged;
                if (handler != null)
                {
                    handler(this, _blockedCount);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void RequestOpenNewTab(string url)
        {
            OnNewTabFromPopup(url);
        }

        public void RequestOpenNewWindow(string url)
        {
            OnNewWindowFromPopup(url);
        }

        private void OnNewTabFromPopup(string url)
        {
            if (this.IsDisposed) return;
            try
            {
                Control syncControl = null;
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    syncControl = this;
                }
                else if (this.Parent != null && !this.Parent.IsDisposed && this.Parent.IsHandleCreated)
                {
                    syncControl = this.Parent;
                }
                else if (Application.OpenForms != null && Application.OpenForms.Count > 0)
                {
                    for (int i = 0; i < Application.OpenForms.Count; i++)
                    {
                        Form f = Application.OpenForms[i];
                        if (f != null && !f.IsDisposed && f.IsHandleCreated)
                        {
                            syncControl = f;
                            break;
                        }
                    }
                }

                if (syncControl != null && syncControl.InvokeRequired)
                {
                    syncControl.BeginInvoke(new Action<string>(OnNewTabFromPopup), url);
                    return;
                }

                if (this.IsDisposed) return;

                EventHandler<string> handler = OpenNewTabRequested;
                if (handler != null)
                {
                    handler(this, url);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch
            {
            }
        }

        private void OnNewWindowFromPopup(string url)
        {
            if (this.IsDisposed) return;
            try
            {
                Control syncControl = null;
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    syncControl = this;
                }
                else if (this.Parent != null && !this.Parent.IsDisposed && this.Parent.IsHandleCreated)
                {
                    syncControl = this.Parent;
                }
                else if (Application.OpenForms != null && Application.OpenForms.Count > 0)
                {
                    for (int i = 0; i < Application.OpenForms.Count; i++)
                    {
                        Form f = Application.OpenForms[i];
                        if (f != null && !f.IsDisposed && f.IsHandleCreated)
                        {
                            syncControl = f;
                            break;
                        }
                    }
                }

                if (syncControl != null && syncControl.InvokeRequired)
                {
                    syncControl.BeginInvoke(new Action<string>(OnNewWindowFromPopup), url);
                    return;
                }

                if (this.IsDisposed) return;

                EventHandler<string> handler = OpenNewWindowRequested;
                if (handler != null)
                {
                    handler(this, url);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch
            {
            }
        }

        private void OnNewIncognitoWindowFromPopup(string url)
        {
            if (this.IsDisposed) return;
            try
            {
                Control syncControl = null;
                if (!this.IsDisposed && this.IsHandleCreated)
                {
                    syncControl = this;
                }
                else if (this.Parent != null && !this.Parent.IsDisposed && this.Parent.IsHandleCreated)
                {
                    syncControl = this.Parent;
                }
                else if (Application.OpenForms != null && Application.OpenForms.Count > 0)
                {
                    for (int i = 0; i < Application.OpenForms.Count; i++)
                    {
                        Form f = Application.OpenForms[i];
                        if (f != null && !f.IsDisposed && f.IsHandleCreated)
                        {
                            syncControl = f;
                            break;
                        }
                    }
                }

                if (syncControl != null && syncControl.InvokeRequired)
                {
                    syncControl.BeginInvoke(new Action<string>(OnNewIncognitoWindowFromPopup), url);
                    return;
                }

                if (this.IsDisposed) return;

                EventHandler<string> handler = OpenNewIncognitoWindowRequested;
                if (handler != null)
                {
                    handler(this, url);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch
            {
            }
        }

        private void OnViewSourceRequested()
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(OnViewSourceRequested));
                    }
                    return;
                }

                if (this.IsDisposed) return;

                Action handler = ViewSourceRequested;
                if (handler != null)
                {
                    handler();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Browser_TitleChanged(object sender, TitleChangedEventArgs e)
        {
            if (this.IsDisposed) return;
            if (_isTransferring) return;
            try
            {
                if (this.InvokeRequired)
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action<object, TitleChangedEventArgs>(Browser_TitleChanged), sender, e);
                    }
                    return;
                }

                if (this.IsDisposed) return;
                if (_isTransferring) return;
                if (InternalPageHelper.IsInternalUrl(CurrentUrl)) return;

                if (InternalPageHelper.IsStartPage(CurrentUrl))
                {
                    CurrentTitle = AppStrings.General.NewTabTitle;
                    this.Text = AppStrings.General.NewTabTitle;
                }
                else if (WebErrorPage.IsErrorPage(CurrentUrl) || !string.IsNullOrEmpty(_activeErrorOriginalUrl))
                {
                    if (e.Title != null && (e.Title.IndexOf("chrome-error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            e.Title.IndexOf("chromewebdata", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            e.Title.IndexOf("NXDOMAIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            e.Title.IndexOf("can't be reached", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        CurrentTitle = AppStrings.Browser.ServerNotFoundTitle;
                        this.Text = AppStrings.Browser.ServerNotFoundTitle;
                    }
                    else if (!string.IsNullOrEmpty(e.Title))
                    {
                        string cleanTitle = e.Title;
                        if (cleanTitle.EndsWith(" - wb", StringComparison.OrdinalIgnoreCase))
                        {
                            cleanTitle = cleanTitle.Substring(0, cleanTitle.Length - 5).TrimEnd();
                        }
                        else if (cleanTitle.EndsWith(" - wb Browser", StringComparison.OrdinalIgnoreCase))
                        {
                            cleanTitle = cleanTitle.Substring(0, cleanTitle.Length - 13).TrimEnd();
                        }
                        CurrentTitle = TitleHelper.NormalizeAndWrapTitle(cleanTitle);
                        this.Text = CurrentTitle;
                    }
                }
                else
                {
                    string raw = string.IsNullOrEmpty(e.Title) ? (string.IsNullOrEmpty(CurrentTitle) ? AppStrings.General.NewTabTitle : CurrentTitle) : e.Title;
                    CurrentTitle = TitleHelper.NormalizeAndWrapTitle(raw);
                    this.Text = CurrentTitle;
                    UpdateCurrentHistoryTitle(CurrentTitle);
                }

                if (e.Title != null && e.Title.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
                {
                    _isViewSource = true;
                    if (!string.IsNullOrEmpty(CurrentUrl) && !CurrentUrl.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
                    {
                        CurrentUrl = "view-source:" + CurrentUrl;
                        EventHandler<string> addrHandler = AddressUpdated;
                        if (addrHandler != null)
                        {
                            addrHandler(this, CurrentUrl);
                        }
                    }
                }
                else if (_isViewSource && !string.IsNullOrEmpty(e.Title) && e.Title != AppStrings.Browser.TabLoadingDefault && e.Title != AppStrings.General.NewTabTitle && !WebErrorPage.IsErrorPage(CurrentUrl))
                {
                    _isViewSource = false;
                    if (!string.IsNullOrEmpty(CurrentUrl) && CurrentUrl.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
                    {
                        CurrentUrl = CurrentUrl.Substring("view-source:".Length);
                        EventHandler<string> addrHandler = AddressUpdated;
                        if (addrHandler != null)
                        {
                            addrHandler(this, CurrentUrl);
                        }
                    }
                }

                EventHandler<string> handler = TitleUpdated;
                if (handler != null)
                {
                    handler(this, CurrentTitle);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Browser_AddressChanged(object sender, AddressChangedEventArgs e)
        {
            if (this.IsDisposed) return;
            if (_isTransferring) return;
            try
            {
                if (this.InvokeRequired)
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action<object, AddressChangedEventArgs>(Browser_AddressChanged), sender, e);
                    }
                    return;
                }

                if (this.IsDisposed) return;
                if (_isTransferring) return;
                if (string.Equals(e.Address, "about:blank", StringComparison.OrdinalIgnoreCase)) return;

                bool navigatingOutFromInternal = false;
                if (InternalPageHelper.IsInternalUrl(CurrentUrl))
                {
                    if (!_expectingNavigationFromInternal ||
                        InternalPageHelper.IsInternalUrl(e.Address) ||
                        InternalPageHelper.IsStartPage(e.Address) ||
                        WebErrorPage.IsErrorPage(e.Address) ||
                        WebErrorPage.IsChromiumInternalErrorPage(e.Address))
                    {
                        return;
                    }

                    _expectingNavigationFromInternal = false;
                    navigatingOutFromInternal = true;
                    EventHandler<string> navHandler = NavigatingFromInternalPage;
                    if (navHandler != null) navHandler(this, e.Address);
                    HideInternalPages();
                    if (Browser != null && !Browser.IsDisposed)
                    {
                        Browser.Visible = true;
                        Browser.BringToFront();
                    }
                }

                if (WebErrorPage.IsChromiumInternalErrorPage(e.Address))
                {
                    if (!string.IsNullOrEmpty(_activeErrorOriginalUrl))
                    {
                        CurrentUrl = _activeErrorOriginalUrl;
                    }
                    EventHandler<string> errorAddrHandler = AddressUpdated;
                    if (errorAddrHandler != null)
                    {
                        errorAddrHandler(this, CurrentUrl);
                    }

                    string targetUrl = !string.IsNullOrEmpty(_activeErrorOriginalUrl) ? _activeErrorOriginalUrl : CurrentUrl;
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
                    LoadErrorPage(targetUrl, errorFilePath, WebErrorPage.GetFavicon(info.Kind));
                    return;
                }

                if (InternalPageHelper.IsStartPage(e.Address))
                {
                    _activeErrorOriginalUrl = null;
                    CurrentUrl = IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl;
                    SetInternalFavicon(InternalPageHelper.GetInternalFavicon(CurrentUrl, IsIncognito));
                    SetAudioPlayingState(false);
                    SetAudioMuted(false);
                    LoadUrl(CurrentUrl);
                    return;
                }
                else if (WebErrorPage.IsErrorPage(e.Address))
                {
                    if (!string.IsNullOrEmpty(_activeErrorOriginalUrl))
                    {
                        CurrentUrl = _activeErrorOriginalUrl;
                    }
                    EventHandler<string> errorAddrHandler = AddressUpdated;
                    if (errorAddrHandler != null)
                    {
                        errorAddrHandler(this, CurrentUrl);
                    }
                    return;
                }
                else
                {
                    _activeErrorOriginalUrl = null;
                    string addressToUse = e.Address;
                    if (_isViewSource && !string.IsNullOrEmpty(addressToUse) && !addressToUse.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
                    {
                        addressToUse = "view-source:" + addressToUse;
                    }
                    _pendingNavigationUrl = null;
                    string oldUrl = CurrentUrl;
                    bool wasInternal = navigatingOutFromInternal || InternalPageHelper.IsInternalUrl(oldUrl) || InternalPageHelper.IsStartPage(oldUrl);
                    CurrentUrl = addressToUse;
                    CrashpadHelper.SetCrashUrl(addressToUse);
                    CheckSiteMuteStatus(addressToUse);
                    SetAudioPlayingState(false);
                    SetMediaCaptureState(false);
                    UpdateFaviconOnNavigation(oldUrl, addressToUse, wasInternal);
                }

                EventHandler<string> handler = AddressUpdated;
                if (handler != null)
                {
                    handler(this, CurrentUrl);
                }

                if (!string.IsNullOrEmpty(e.Address))
                {
                    try
                    {
                        IFrame f = Browser != null && !Browser.IsDisposed ? Browser.GetMainFrame() : null;
                        if (f != null && f.IsValid)
                        {
                            f.ExecuteJavaScriptAsync("if (window.__wbCheckAudio) { window.__wbCheckAudio(true); }");
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(e.Address) && SponsorBlockYTShield.IsYouTubeDomain(e.Address))
                {
                    OnYouTubeAddressChanged(e.Address);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Browser_LoadingStateChanged(object sender, LoadingStateChangedEventArgs e)
        {
            if (this.IsDisposed) return;
            if (_isTransferring) return;
            try
            {
                if (this.InvokeRequired)
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action<object, LoadingStateChangedEventArgs>(Browser_LoadingStateChanged), sender, e);
                    }
                    return;
                }

                if (this.IsDisposed) return;
                if (_isTransferring) return;
                if (InternalPageHelper.IsInternalUrl(CurrentUrl)) return;

                CanGoBack = e.CanGoBack;
                CanGoForward = e.CanGoForward;
                IsLoading = e.IsLoading;
                if (LoadingStateChanged != null)
                {
                    try { LoadingStateChanged(this, EventArgs.Empty); } catch { }
                }

                if (!e.IsLoading)
                {
                    string curAddr = Browser != null ? Browser.Address : null;
                    bool isBlankOrInternal = string.IsNullOrEmpty(curAddr) ||
                                             string.Equals(curAddr, "about:blank", StringComparison.OrdinalIgnoreCase) ||
                                             InternalPageHelper.IsInternalUrl(curAddr) ||
                                             InternalPageHelper.IsInternalUrl(CurrentUrl);

                    if (!isBlankOrInternal)
                    {
                        if (_newTabView != null || _incognitoNewTabView != null || _bookmarksView != null || _historyView != null || _downloadsView != null || _screenshotView != null)
                        {
                            SafeInvoke(() =>
                            {
                                if (!InternalPageHelper.IsInternalUrl(CurrentUrl))
                                {
                                    HideInternalPages();
                                    if (Browser != null && !Browser.IsDisposed)
                                    {
                                        Browser.Visible = true;
                                        Browser.BringToFront();
                                    }
                                }
                            });
                        }
                    }
                }

                if (!e.IsLoading && !IsBenchmark && !_isViewSource && Browser != null && !Browser.IsDisposed)
                {
                    try
                    {
                        IFrame mainFrame = Browser.GetMainFrame();
                        if (mainFrame != null && mainFrame.IsValid)
                        {
                            string effUrl = !string.IsNullOrEmpty(Browser.Address) ? Browser.Address : EffectiveUrl;
                            if (!string.IsNullOrEmpty(effUrl) && !InternalPageHelper.IsInternalUrl(effUrl))
                            {
                                TryInjectCosmeticUBlock(mainFrame, effUrl);
                                TryInjectYouTubeUBlock(mainFrame, effUrl);
                                TryInjectYouTubeSponsorBlock(mainFrame, effUrl);
                            }
                        }
                    }
                    catch { }
                }

                EventHandler<LoadingStateChangedEventArgs> handler = LoadingStateUpdated;
                if (handler != null)
                {
                    handler(this, e);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Browser_StatusMessage(object sender, StatusMessageEventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action<object, StatusMessageEventArgs>(Browser_StatusMessage), sender, e);
                    }
                    return;
                }

                if (this.IsDisposed) return;

                EventHandler<string> handler = StatusMessageUpdated;
                if (handler != null)
                {
                    handler(this, e.Value);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Browser_FrameLoadStart(object sender, FrameLoadStartEventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                EventHandler<string> statusHandler = StatusMessageUpdated;
                if (statusHandler != null)
                {
                    try { statusHandler(this, string.Empty); } catch { }
                }

                if (e.Frame != null && e.Frame.IsValid && e.Frame.IsMain)
                {
                    bool isBlankOrInternal = string.IsNullOrEmpty(e.Url) ||
                                             string.Equals(e.Url, "about:blank", StringComparison.OrdinalIgnoreCase) ||
                                             InternalPageHelper.IsInternalUrl(e.Url) ||
                                             InternalPageHelper.IsInternalUrl(CurrentUrl);

                    // Seamless internal page transition: dismiss internal page once real web frame starts loading
                    if (!isBlankOrInternal)
                    {
                        if (_newTabView != null || _incognitoNewTabView != null || _bookmarksView != null || _historyView != null || _downloadsView != null || _screenshotView != null)
                        {
                            SafeInvoke(() =>
                            {
                                if (!InternalPageHelper.IsInternalUrl(CurrentUrl))
                                {
                                    HideInternalPages();
                                    if (Browser != null && !Browser.IsDisposed)
                                    {
                                        Browser.Visible = true;
                                        Browser.BringToFront();
                                        try { Browser.Focus(); } catch { }
                                    }
                                }
                            });
                        }
                    }

                    if (WebErrorPage.IsChromiumInternalErrorPage(e.Url))
                    {
                        string failedUrl = !string.IsNullOrEmpty(_activeErrorOriginalUrl) ? _activeErrorOriginalUrl : CurrentUrl;
                        if (string.IsNullOrEmpty(failedUrl) || WebErrorPage.IsChromiumInternalErrorPage(failedUrl))
                        {
                            failedUrl = "wb://newtab";
                        }
                        WebErrorInfo info = WebErrorPage.CreateFromCefError(
                            "DNS_PROBE_FINISHED_NXDOMAIN",
                            -105,
                            failedUrl,
                            "DNS_PROBE_FINISHED_NXDOMAIN"
                        );
                        string errorFilePath = WebErrorPage.CreateErrorFile(info);
                        LoadErrorPage(failedUrl, errorFilePath, WebErrorPage.GetFavicon(info.Kind));
                        return;
                    }

                    ApplyDarkColorSchemeEmulation();
                    if (!WebErrorPage.IsErrorPage(e.Url))
                    {
                        _activeErrorOriginalUrl = null;
                    }
                    ResetBlockedCount();
                    if (!_isViewSource)
                    {
                        ViewportScrollManager.InjectViewportScrollAnchor(e.Frame, e.Url);
                        TryInjectDragControlFix(e.Frame, e.Url);
                        TryInjectCosmeticUBlock(e.Frame, e.Url);
                        TryInjectYouTubeUBlock(e.Frame, e.Url);
                        TryInjectYouTubeSponsorBlock(e.Frame, e.Url);
                        PasskeyFallbackHelper.InjectIntoFrame(e.Frame, e.Url);
                        TryInjectAudioObserver(e.Frame, e.Url);
                    }

                    // When navigating to a new site, immediately update favicon or clear old site's favicon
                    string frameUrl = e.Url;
                    if (!string.IsNullOrEmpty(frameUrl) && !WebErrorPage.IsErrorPage(frameUrl))
                    {
                        SafeInvoke(() =>
                        {
                            if (this.IsDisposed || _isTransferring) return;

                            if (InternalPageHelper.IsStartPage(frameUrl))
                            {
                                bool isPlaceholderBlank = string.Equals(frameUrl, "about:blank", StringComparison.OrdinalIgnoreCase);
                                if (!string.IsNullOrEmpty(_pendingUrlToLoad) ||
                                    (isPlaceholderBlank && !InternalPageHelper.IsStartPage(CurrentUrl)))
                                {
                                    // Ignore asynchronous about:blank placeholder frame loads once tab is navigating to a real site or non-start page
                                    return;
                                }

                                SetInternalFavicon(InternalPageHelper.GetInternalFavicon(IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl, IsIncognito));
                                SetAudioPlayingState(false);
                                SetMediaCaptureState(false);
                                SetAudioMuted(false);
                            }
                            else if (!InternalPageHelper.IsInternalUrl(frameUrl))
                            {
                                CheckSiteMuteStatus(frameUrl);
                                UpdateFaviconOnNavigation(CurrentUrl, frameUrl,
                                    InternalPageHelper.IsInternalUrl(CurrentUrl) || InternalPageHelper.IsStartPage(CurrentUrl));
                            }
                        });
                    }

                    // Early YouTube shield injection on main frame so hooks are active before YouTube scripts execute
                    if (!_isViewSource && (AdBlockYTShield.IsYouTubeDomain(e.Url) || AdBlockYTShield.IsYouTubeDomain(CurrentUrl)))
                    {
                        TryInjectYouTubeUBlock(e.Frame, e.Url);
                    }
                }
                else if (e.Frame != null && e.Frame.IsValid && !e.Frame.IsMain)
                {
                    // Embedded players / widgets / YouTube in subframes (e.g. reddit.com, twitter.com, blogs)
                    string subUrl = null;
                    try { if (e.Frame != null && e.Frame.IsValid) subUrl = e.Frame.Url; } catch { }
                    if (!_isViewSource)
                    {
                        string frameUrl = !string.IsNullOrEmpty(e.Url) ? e.Url : subUrl;
                        TryInjectAudioObserver(e.Frame, frameUrl);
                        if (AdBlockYTShield.IsYouTubeDomain(e.Url) || AdBlockYTShield.IsYouTubeDomain(subUrl))
                        {
                            TryInjectYouTubeUBlock(e.Frame, frameUrl);
                            TryInjectYouTubeSponsorBlock(e.Frame, frameUrl);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void Browser_FrameLoadEnd(object sender, FrameLoadEndEventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                if (e.Frame != null && e.Frame.IsValid)
                {
                    bool isBenchmark = IsBenchmarkUrl(e.Url) || IsBenchmarkUrl(CurrentUrl);

                    // Only inject extensions/fixes on the main frame and bypass all script injection on benchmark runs
                    if (e.Frame.IsMain)
                    {
                        if (!isBenchmark && !_isViewSource)
                        {
                            TryInjectCosmeticUBlock(e.Frame, e.Url);
                            TryInjectDragControlFix(e.Frame, e.Url);
                            ViewportScrollManager.InjectViewportScrollAnchor(e.Frame, e.Url);
                            TryInjectYouTubeUBlock(e.Frame, e.Url);
                            TryInjectYouTubeSponsorBlock(e.Frame, e.Url);
                            PushYouTubeSponsorSegmentsIfAvailable(e.Frame, e.Url);
                            CheckSiteMuteStatus(e.Url);
                            TryInjectAudioObserver(e.Frame, e.Url);
                            TryInjectMediaCaptureObserver(e.Frame, e.Url);
                            PasskeyFallbackHelper.InjectIntoFrame(e.Frame, e.Url);
                            DarkFallbackHelper.InjectIntoFrame(e.Frame, e.Url);
                        }

                        // Fetch real website favicon from the page DOM (preferring dark mode icons)
                        if (!isBenchmark && !string.IsNullOrEmpty(e.Url) && !InternalPageHelper.IsStartPage(e.Url))
                        {
                            if (_isViewSource)
                            {
                                AppServices.Favicons.FetchFaviconAsync(e.Url, null, img => SetFaviconForUrl(e.Url, img));
                            }
                            else
                            {
                            string script = @"(function() {
                            var links = document.querySelectorAll('link[rel*=""icon""]');
                            if (!links || links.length === 0) return '';

                            var isSvg = function(link) {
                                var href = (link.getAttribute('href') || '').toLowerCase();
                                var type = (link.getAttribute('type') || '').toLowerCase();
                                return href.indexOf('.svg') !== -1 || type.indexOf('svg') !== -1;
                            };

                            // 1. First priority: raster link with explicit prefers-color-scheme: dark media query
                            for (var i = 0; i < links.length; i++) {
                                var media = (links[i].getAttribute('media') || '').toLowerCase();
                                if ((media.indexOf('prefers-color-scheme: dark') !== -1 || media.indexOf('prefers-color-scheme:dark') !== -1) && !isSvg(links[i])) {
                                    if (links[i].href) return links[i].href;
                                }
                            }

                            // 2. Second priority: raster link with 'dark' in href, id, or className
                            for (var i = 0; i < links.length; i++) {
                                var href = (links[i].href || '').toLowerCase();
                                var id = (links[i].id || '').toLowerCase();
                                var cl = (links[i].className || '').toLowerCase();
                                if ((href.indexOf('dark') !== -1 || id.indexOf('dark') !== -1 || cl.indexOf('dark') !== -1) && !isSvg(links[i])) {
                                    if (links[i].href) return links[i].href;
                                }
                            }

                            // 3. Third priority: raster link not restricted to light media query
                            for (var i = 0; i < links.length; i++) {
                                var media = (links[i].getAttribute('media') || '').toLowerCase();
                                if (media.indexOf('prefers-color-scheme: light') === -1 && media.indexOf('prefers-color-scheme:light') === -1 && !isSvg(links[i])) {
                                    if (links[i].href) return links[i].href;
                                }
                            }

                            // 4. Default fallback: first non-svg icon link or first link
                            for (var i = 0; i < links.length; i++) {
                                if (!isSvg(links[i]) && links[i].href) return links[i].href;
                            }
                            return links[0].href || '';
                        })()";

                        try
                        {
                            e.Frame.EvaluateScriptAsync(script).ContinueWith(t =>
                            {
                                try
                                {
                                    string foundUrl = null;
                                    if (!t.IsFaulted && t.Result != null && t.Result.Success && t.Result.Result != null)
                                    {
                                        foundUrl = t.Result.Result.ToString();
                                    }
                                    AppServices.Favicons.FetchFaviconAsync(e.Url, foundUrl, img => SetFaviconForUrl(e.Url, img));
                                }
                                catch
                                {
                                }
                            });
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }
                }
            }
            else if (!e.Frame.IsMain)
            {
                // Subframe finished loading
                string subUrl = null;
                try { if (e.Frame != null && e.Frame.IsValid) subUrl = e.Frame.Url; } catch { }
                if (!isBenchmark && !_isViewSource)
                {
                    string frameUrl = !string.IsNullOrEmpty(e.Url) ? e.Url : subUrl;
                    TryInjectAudioObserver(e.Frame, frameUrl);
                    if (AdBlockYTShield.IsYouTubeDomain(e.Url) || AdBlockYTShield.IsYouTubeDomain(subUrl))
                    {
                        TryInjectYouTubeUBlock(e.Frame, frameUrl);
                        TryInjectYouTubeSponsorBlock(e.Frame, frameUrl);
                        PushYouTubeSponsorSegmentsIfAvailable(e.Frame, frameUrl);
                    }
                }
            }
        }
    }
        catch (Exception)
        {
        }
    }

        private void TryInjectCosmeticUBlock(IFrame frame, string url)
        {
            if (frame == null || !frame.IsValid) return;
            if (!AppServices.AdBlock.IsEnabled) return;
            if (string.IsNullOrEmpty(url)) return;
            if (AppServices.AdBlock.IsDomainWhitelisted(url)) return;
            if (InternalPageHelper.IsStartPage(url)) return;
            if (AdBlockYTShield.IsYouTubeDomain(url)) return;

            try
            {
                string script = AppServices.AdBlock.GetCosmeticHidingScript(url);
                if (!string.IsNullOrEmpty(script))
                {
                    frame.ExecuteJavaScriptAsync(script);
                }
            }
            catch
            {
            }
        }

        private void TryInjectYouTubeUBlock(IFrame frame, string url)
        {
            if (frame == null || !frame.IsValid) return;
            if (!AppServices.AdBlock.IsEnabled) return;
            string targetUrl = !string.IsNullOrEmpty(url) ? url : (frame.IsValid ? frame.Url : null);
            if (string.IsNullOrEmpty(targetUrl)) return;
            if (AppServices.AdBlock.IsDomainWhitelisted(targetUrl)) return;
            if (!string.IsNullOrEmpty(CurrentUrl) && AppServices.AdBlock.IsDomainWhitelisted(CurrentUrl)) return;
            if (!AdBlockYTShield.IsYouTubeDomain(targetUrl)) return;

            try
            {
                string script = AdBlockYTShield.GetAdShieldScript();
                frame.ExecuteJavaScriptAsync(script);
            }
            catch
            {
            }
        }

        private void TryInjectYouTubeSponsorBlock(IFrame frame, string url)
        {
            if (frame == null || !frame.IsValid) return;
            if (!AppServices.SponsorBlock.IsEnabled) return;
            string targetUrl = !string.IsNullOrEmpty(url) ? url : (frame.IsValid ? frame.Url : null);
            if (string.IsNullOrEmpty(targetUrl)) return;
            if (AppServices.SponsorBlock.IsWhitelisted(targetUrl)) return;
            if (!string.IsNullOrEmpty(CurrentUrl) && AppServices.SponsorBlock.IsWhitelisted(CurrentUrl)) return;
            if (!SponsorBlockYTShield.IsYouTubeDomain(targetUrl)) return;

            try
            {
                string script = SponsorBlockYTShield.GetSponsorBlockScript(AppServices.SponsorBlock.GetConfigJson());
                frame.ExecuteJavaScriptAsync(script);
            }
            catch
            {
            }
        }

        public void UpdateSponsorBlockConfig()
        {
            if (this.IsDisposed) return;
            try
            {
                IFrame frame = Browser != null && !Browser.IsDisposed ? Browser.GetMainFrame() : null;
                if (frame != null && frame.IsValid)
                {
                    string targetUrl = !string.IsNullOrEmpty(CurrentUrl) ? CurrentUrl : frame.Url;
                    if (SponsorBlockYTShield.IsYouTubeDomain(targetUrl))
                    {
                        string configJson = AppServices.SponsorBlock.GetConfigJson();
                        string script = "if (typeof window.__wbSponsorBlockUpdateConfig === 'function') { window.__wbSponsorBlockUpdateConfig(" + configJson + "); }";
                        frame.ExecuteJavaScriptAsync(script);
                    }
                }
            }
            catch
            {
            }
        }

        private void OnYouTubeAddressChanged(string url)
        {
            if (this.IsDisposed) return;

            try
            {
                IFrame frame = Browser != null && !Browser.IsDisposed ? Browser.GetMainFrame() : null;
                if (frame != null && frame.IsValid)
                {
                    TryInjectYouTubeUBlock(frame, url);
                    if (AppServices.SponsorBlock.IsEnabled && !AppServices.SponsorBlock.IsWhitelisted(url))
                    {
                        TryInjectYouTubeSponsorBlock(frame, url);
                        PushYouTubeSponsorSegmentsIfAvailable(frame, url);
                    }
                }
            }
            catch
            {
            }
        }

        private void PushYouTubeSponsorSegmentsIfAvailable(IFrame frame, string url)
        {
            if (frame == null || !frame.IsValid || this.IsDisposed) return;
            if (!AppServices.SponsorBlock.IsEnabled) return;
            string targetUrl = !string.IsNullOrEmpty(url) ? url : (frame.IsValid ? frame.Url : null);
            if (string.IsNullOrEmpty(targetUrl) || !SponsorBlockYTShield.IsYouTubeDomain(targetUrl)) return;
            if (AppServices.SponsorBlock.IsWhitelisted(targetUrl)) return;
            if (!string.IsNullOrEmpty(CurrentUrl) && AppServices.SponsorBlock.IsWhitelisted(CurrentUrl)) return;

            string vid = SponsorBlockManager.ExtractVideoId(targetUrl);
            if (string.IsNullOrEmpty(vid)) return;

            AppServices.SponsorBlock.FetchSegmentsAsync(vid, segmentsJson =>
            {
                if (this.IsDisposed) return;
                try
                {
                    if (frame != null && frame.IsValid && !string.IsNullOrEmpty(segmentsJson))
                    {
                        string pushScript = SponsorBlockYTShield.FormatReceiveSegmentsScript(vid, segmentsJson);
                        frame.ExecuteJavaScriptAsync(pushScript);
                    }
                }
                catch
                {
                }
            });
        }

        private void TryInjectDragControlFix(IFrame frame, string url)
        {
            if (frame == null || !frame.IsValid) return;
            if (string.IsNullOrEmpty(url)) return;
            if (url.StartsWith("wb://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                frame.ExecuteJavaScriptAsync(WebDragDropHelper.DragControlFixScript);
            }
            catch
            {
            }
        }

        private void ApplyDarkColorSchemeEmulation()
        {
            if (_darkEmulationConfigured) return;
            try
            {
                if (Browser == null || Browser.IsDisposed) return;
                IBrowser b = Browser.GetBrowser();
                if (b != null && !b.IsDisposed)
                {
                    IBrowserHost host = b.GetHost();
                    if (host != null && !host.IsDisposed)
                    {
                        int msgId = host.GetNextDevToolsMessageId();
                        host.ExecuteDevToolsMethod(msgId, "Emulation.setEmulatedMedia", "{\"media\":\"\",\"features\":[{\"name\":\"prefers-color-scheme\",\"value\":\"dark\"}]}");

                        int bgMsgId = host.GetNextDevToolsMessageId();
                        host.ExecuteDevToolsMethod(bgMsgId, "Emulation.setDefaultBackgroundColorOverride", "{\"color\":{\"r\":32,\"g\":33,\"b\":36,\"a\":1}}");

                        // Register WebAuthn passkey fallback script via CDP so it evaluates before page scripts
                        PasskeyFallbackHelper.RegisterOnBrowserHost(host);

                        // Register Audio observer script via CDP so all frames evaluate it on new document
                        RegisterAudioObserverOnBrowserHost(host);
                        _darkEmulationConfigured = true;
                    }
                }
            }
            catch
            {
            }
        }

        private void RegisterAudioObserverOnBrowserHost(IBrowserHost host)
        {
            if (host == null || host.IsDisposed) return;
            try
            {
                int msgId = host.GetNextDevToolsMessageId();
                string escapedScript = JsHelper.EscapeJson(JsHelper.AudioObserverScript);
                string jsonParams = "{\"source\":\"" + escapedScript + "\"}";
                host.ExecuteDevToolsMethod(msgId, "Page.addScriptToEvaluateOnNewDocument", jsonParams);
            }
            catch
            {
            }
        }

        private void TryInjectAudioObserver(IFrame frame, string url)
        {
            if (frame == null || !frame.IsValid) return;
            string targetUrl = !string.IsNullOrEmpty(url) ? url : frame.Url;
            if (!string.IsNullOrEmpty(targetUrl) && (InternalPageHelper.IsStartPage(targetUrl) || WebErrorPage.IsErrorPage(targetUrl)))
            {
                return;
            }

            try
            {
                frame.ExecuteJavaScriptAsync(JsHelper.AudioObserverScript);
            }
            catch
            {
            }
        }

        private void TryInjectMediaCaptureObserver(IFrame frame, string url)
        {
            if (frame == null || !frame.IsValid) return;
            if (string.IsNullOrEmpty(url) || InternalPageHelper.IsStartPage(url)) return;

            try
            {
                frame.ExecuteJavaScriptAsync(JsHelper.MediaCaptureObserverScript);
            }
            catch
            {
            }
        }

        public bool IsElementPickerActive { get; private set; }

        public void StartElementPicker()
        {
            if (Browser == null || Browser.IsDisposed) return;
            if (string.IsNullOrEmpty(CurrentUrl) || InternalPageHelper.IsStartPage(CurrentUrl)) return;

            try
            {
                IFrame frame = Browser.GetMainFrame();
                if (frame != null && frame.IsValid)
                {
                    IsElementPickerActive = true;
                    string script = DropperScriptlet.GetDropperScript();
                    frame.ExecuteJavaScriptAsync(script);
                    EventHandler handler = ElementPickerStarted;
                    if (handler != null) handler(this, EventArgs.Empty);
                }
            }
            catch
            {
            }
        }

        public void CancelElementPicker()
        {
            IsElementPickerActive = false;
            if (Browser == null || Browser.IsDisposed) return;
            try
            {
                IFrame frame = Browser.GetMainFrame();
                if (frame != null && frame.IsValid)
                {
                    frame.ExecuteJavaScriptAsync("if (window.__wbElementPickerCleanup) window.__wbElementPickerCleanup();");
                }
            }
            catch
            {
            }
        }

        public void ResumeElementPicker()
        {
            if (Browser == null || Browser.IsDisposed) return;
            try
            {
                IFrame frame = Browser.GetMainFrame();
                if (frame != null && frame.IsValid)
                {
                    IsElementPickerActive = true;
                    frame.ExecuteJavaScriptAsync("if (window.__wbElementPickerResume) window.__wbElementPickerResume();");
                }
            }
            catch
            {
            }
        }

        public void SetElementPickerPreview(bool hide, string selector)
        {
            if (Browser == null || Browser.IsDisposed) return;
            try
            {
                IFrame frame = Browser.GetMainFrame();
                if (frame != null && frame.IsValid)
                {
                    string script = DropperScriptlet.FormatSetPreviewScript(hide, selector);
                    frame.ExecuteJavaScriptAsync(script);
                }
            }
            catch
            {
            }
        }

        public void CommitElementPickerBlock(string selector)
        {
            IsElementPickerActive = false;
            if (Browser == null || Browser.IsDisposed) return;
            try
            {
                IFrame frame = Browser.GetMainFrame();
                if (frame != null && frame.IsValid)
                {
                    string script = DropperScriptlet.FormatCommitScript(selector);
                    frame.ExecuteJavaScriptAsync(script);
                }
            }
            catch
            {
            }
        }

        public void NotifyElementSelectedForBlock(string domain, string selector, int matches, string candidatesJson = null)
        {
            EventHandler<ElementSelectedEventArgs> handler = ElementSelectedForBlock;
            if (handler != null)
            {
                handler(this, new ElementSelectedEventArgs(domain, selector, matches, candidatesJson));
            }
        }

        public void NotifyElementPickerCancelled()
        {
            IsElementPickerActive = false;
            EventHandler handler = ElementPickerCancelled;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void NotifySponsorBlockMenuRequested()
        {
            EventHandler handler = SponsorBlockMenuRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void HandleWbAction(string actionUrl)
        {
            if (string.IsNullOrEmpty(actionUrl) || this.IsDisposed) return;
            if (!actionUrl.StartsWith("wb-action:", StringComparison.OrdinalIgnoreCase)) return;

            string action = actionUrl.Substring("wb-action:".Length);
            string query = "";
            int qIdx = action.IndexOf('?');
            if (qIdx >= 0)
            {
                query = action.Substring(qIdx + 1);
                action = action.Substring(0, qIdx);
            }
            action = action.Trim().ToLowerInvariant();

            SafeInvoke(() =>
            {
                if (action == "goback")
                {
                    GoBack();
                }
                else if (action == "reload")
                {
                    Reload();
                }
                else if (action == "closetab" || action == "close" || action == "close-window" || action == "bye")
                {
                    RequestCloseTab();
                }
                else if (action == "home")
                {
                    GoHome();
                }
                else if (action == "proceed")
                {
                    string targetUrl = ParseQueryParam(query, "url");
                    string permanentStr = ParseQueryParam(query, "permanent");
                    if (!string.IsNullOrEmpty(targetUrl))
                    {
                        if (permanentStr == "true" || permanentStr == "1")
                        {
                            try
                            {
                                Uri u;
                                if (Uri.TryCreate(targetUrl, UriKind.Absolute, out u))
                                {
                                    AppServices.AdBlock.WhitelistDomain(u.Host);
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            WebErrorBypassManager.AllowUrlTemporarily(targetUrl);
                        }
                        LoadUrl(targetUrl);
                    }
                }
                else if (action == "proceed-ssl")
                {
                    string targetUrl = ParseQueryParam(query, "url");
                    if (!string.IsNullOrEmpty(targetUrl))
                    {
                        try
                        {
                            Uri u;
                            if (Uri.TryCreate(targetUrl, UriKind.Absolute, out u))
                            {
                                WebErrorBypassManager.AllowSslHostTemporarily(u.Host);
                            }
                        }
                        catch { }
                        LoadUrl(targetUrl);
                    }
                }
                else if (action == "whitelist")
                {
                    string domain = ParseQueryParam(query, "domain");
                    string targetUrl = ParseQueryParam(query, "url");
                    if (!string.IsNullOrEmpty(domain))
                    {
                        AppServices.AdBlock.WhitelistDomain(domain);
                    }
                    if (!string.IsNullOrEmpty(targetUrl))
                    {
                        LoadUrl(targetUrl);
                    }
                }
                else if (action == "block-element")
                {
                    string domain = ParseQueryParam(query, "domain");
                    string selector = ParseQueryParam(query, "selector");
                    if (!string.IsNullOrEmpty(selector))
                    {
                        AppServices.AdBlock.AddCustomFilter(domain, selector);
                    }
                }
                else if (action == "element-selected")
                {
                    string domain = ParseQueryParam(query, "domain");
                    string selector = ParseQueryParam(query, "selector");
                    string matchesStr = ParseQueryParam(query, "matches");
                    string candidates = ParseQueryParam(query, "candidates");
                    int matches = 1;
                    int.TryParse(matchesStr, out matches);

                    string sig = string.Format("{0}|{1}", domain ?? string.Empty, selector ?? string.Empty);
                    DateTime now = DateTime.UtcNow;
                    if (string.Equals(_lastElementSelectedSig, sig, StringComparison.Ordinal) &&
                        (now - _lastElementSelectedTime).TotalMilliseconds < 1500)
                    {
                        return;
                    }
                    _lastElementSelectedSig = sig;
                    _lastElementSelectedTime = now;

                    NotifyElementSelectedForBlock(domain, selector, matches, candidates);
                }
                else if (action == "cancel-element-picker")
                {
                    NotifyElementPickerCancelled();
                }
                else if (action == "audio-state")
                {
                    string playing = ParseQueryParam(query, "playing");
                    string source = ParseQueryParam(query, "source");
                    if (string.IsNullOrEmpty(source)) source = "main";
                    bool isPlaying = (playing == "1" || string.Equals(playing, "true", StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrEmpty(CurrentUrl) || InternalPageHelper.IsStartPage(CurrentUrl))
                    {
                        lock (_audioStateLock)
                        {
                            _activeAudioSources.Clear();
                        }
                        SetAudioPlayingState(false);
                    }
                    else
                    {
                        bool hasActiveAudio;
                        lock (_audioStateLock)
                        {
                            if (isPlaying)
                            {
                                _activeAudioSources.Add(source);
                            }
                            else
                            {
                                _activeAudioSources.Remove(source);
                            }
                            hasActiveAudio = _activeAudioSources.Count > 0;
                        }
                        SetAudioPlayingState(hasActiveAudio);
                    }
                }
                else if (action == "media-capture-state")
                {
                    string active = ParseQueryParam(query, "active");
                    string type = ParseQueryParam(query, "type");
                    bool isCaptured = (active == "1" || string.Equals(active, "true", StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrEmpty(CurrentUrl) || InternalPageHelper.IsStartPage(CurrentUrl))
                    {
                        SetMediaCaptureState(false);
                    }
                    else
                    {
                        SetMediaCaptureState(isCaptured, type);
                    }
                }
                else if (action == "sponsorblock-skip")
                {
                    string videoId = ParseQueryParam(query, "videoid");
                    string category = ParseQueryParam(query, "category");
                    string savedStr = ParseQueryParam(query, "saved");
                    double saved = 0;
                    double.TryParse(savedStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out saved);
                    AppServices.SponsorBlock.RecordSkip(videoId, category, saved);
                }
                else if (action == "sponsorblock-fetch")
                {
                    string videoId = ParseQueryParam(query, "videoid");
                    if (!string.IsNullOrEmpty(videoId))
                    {
                        AppServices.SponsorBlock.FetchSegmentsAsync(videoId, segmentsJson =>
                        {
                            if (this.IsDisposed || string.IsNullOrEmpty(segmentsJson)) return;
                            try
                            {
                                if (Browser != null && !Browser.IsDisposed)
                                {
                                    string script = SponsorBlockYTShield.FormatReceiveSegmentsScript(videoId, segmentsJson);
                                    var browserInstance = Browser.GetBrowser();
                                    if (browserInstance != null && !browserInstance.IsDisposed)
                                    {
                                        var allFrames = browserInstance.GetAllFrames();
                                        if (allFrames != null)
                                        {
                                            foreach (var f in allFrames)
                                            {
                                                if (f != null && f.IsValid && (f.IsMain || SponsorBlockYTShield.IsYouTubeDomain(f.Url)))
                                                {
                                                    f.ExecuteJavaScriptAsync(script);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch
                            {
                            }
                        });
                    }
                }
                else if (action == "sponsorblock-menu")
                {
                    NotifySponsorBlockMenuRequested();
                }
            });
        }

        private static string ParseQueryParam(string query, string key)
        {
            return UrlHelper.GetQueryParam(query, key);
        }

        private void SafeInvoke(Action action)
        {
            if (action == null || this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    if (this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (!this.IsDisposed)
                            {
                                try { action(); } catch { }
                            }
                        }));
                    }
                    else
                    {
                        Control parent = this.Parent;
                        if (parent != null && parent.IsHandleCreated && !parent.IsDisposed)
                        {
                            parent.BeginInvoke(new Action(() =>
                            {
                                if (!this.IsDisposed)
                                {
                                    try { action(); } catch { }
                                }
                            }));
                        }
                    }
                }
                else
                {
                    action();
                }
            }
            catch { }
        }

        private void Browser_JavascriptMessageReceived(object sender, JavascriptMessageReceivedEventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                string msg = e.Message as string;
                if (!string.IsNullOrEmpty(msg) && msg.StartsWith("wb-action:", StringComparison.OrdinalIgnoreCase))
                {
                    HandleWbAction(msg);
                }
            }
            catch { }
        }

        public void GoHome()
        {
            SetAudioPlayingState(false);
            SetAudioMuted(false);
            SetMediaCaptureState(false);
            if (Browser != null && Browser.IsBrowserInitialized)
            {
                try { Browser.Stop(); } catch { }
                try { Browser.LoadUrl("about:blank"); } catch { }
                Browser.Visible = false;
                try { Browser.SendToBack(); } catch { }
            }
            LoadUrl(IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl);
        }

        public void LoadUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            _activeErrorOriginalUrl = null;
            _expectingNavigationFromInternal = false;

            if (InternalPageHelper.IsViewSourceUrl(url))
            {
                _isViewSource = true;
                url = InternalPageHelper.NormalizeViewSourceUrl(url);
            }
            else
            {
                _isViewSource = false;
            }

            if (InternalPageHelper.IsInternalUrl(url))
            {
                _pendingUrlToLoad = null;
                string normUrl = InternalPageHelper.NormalizeInternalUrl(url);
                if (IsIncognito && (InternalPageHelper.GetInternalPageType(normUrl) == InternalPageType.NewTab || string.Equals(normUrl, InternalPageHelper.NewTabUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    normUrl = InternalPageHelper.IncognitoUrl;
                }
                else if (!IsIncognito && (InternalPageHelper.GetInternalPageType(normUrl) == InternalPageType.Incognito || string.Equals(normUrl, InternalPageHelper.IncognitoUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    EventHandler<string> incHandler = OpenNewIncognitoWindowRequested;
                    if (incHandler != null)
                    {
                        incHandler(this, InternalPageHelper.IncognitoUrl);
                        return;
                    }
                    normUrl = InternalPageHelper.NewTabUrl;
                }
                CurrentUrl = normUrl;
                CurrentTitle = InternalPageHelper.GetInternalTitle(normUrl);
                this.Text = CurrentTitle;
                _hasInternalFavicon = true;
                Favicon = InternalPageHelper.GetInternalFavicon(normUrl, IsIncognito);
                CanGoBack = false;
                CanGoForward = false;
                IsLoading = false;
                if (LoadingStateChanged != null)
                {
                    try { LoadingStateChanged(this, EventArgs.Empty); } catch { }
                }

                if (Browser != null)
                {
                    try { Browser.Stop(); } catch { }
                    if (Browser.IsBrowserInitialized)
                    {
                        try { Browser.LoadUrl("about:blank"); } catch { }
                    }
                    Browser.Visible = false;
                    try { Browser.SendToBack(); } catch { }
                }

                ShowInternalPage(normUrl);
                RecordNavigation(normUrl, CurrentTitle);

                EventHandler<string> titleHandler = TitleUpdated;
                if (titleHandler != null) titleHandler(this, CurrentTitle);

                EventHandler<string> addrHandler = AddressUpdated;
                if (addrHandler != null) addrHandler(this, CurrentUrl);

                EventHandler favHandler = FaviconChanged;
                if (favHandler != null) favHandler(this, EventArgs.Empty);

                EventHandler<LoadingStateChangedEventArgs> loadHandler = LoadingStateUpdated;
                if (loadHandler != null) loadHandler(this, new LoadingStateChangedEventArgs(Browser != null ? Browser.GetBrowser() : null, false, false, false));

                return;
            }

            string oldUrl = CurrentUrl;
            bool wasInternal = InternalPageHelper.IsInternalUrl(oldUrl) || InternalPageHelper.IsStartPage(oldUrl);
            if (wasInternal)
            {
                EventHandler<string> navHandler = NavigatingFromInternalPage;
                if (navHandler != null) navHandler(this, url);
            }

            // Smooth internal page transition: keep internal page active while background connection is in flight
            if (!wasInternal)
            {
                HideInternalPages();
            }

            if (Browser != null)
            {
                Browser.Visible = true;
                if (!wasInternal)
                {
                    Browser.BringToFront();
                }
            }

            _pendingNavigationUrl = null;
            CurrentUrl = url;

            if (InternalPageHelper.IsStartPage(url))
            {
                SetInternalFavicon(InternalPageHelper.GetInternalFavicon(IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl, IsIncognito));
            }
            else
            {
                UpdateFaviconOnNavigation(oldUrl, url, wasInternal);
            }

            if (!InternalPageHelper.IsInternalUrl(url))
            {
                CheckSiteMuteStatus(url);
            }

            EventHandler<string> handler = AddressUpdated;
            if (handler != null)
            {
                handler(this, CurrentUrl);
            }

            if (Browser != null)
            {
                if (Browser.IsBrowserInitialized)
                {
                    _pendingUrlToLoad = null;
                    Browser.LoadUrl(url);
                }
                else
                {
                    _pendingUrlToLoad = url;
                    if (!Browser.IsHandleCreated)
                    {
                        try { Browser.CreateControl(); } catch { }
                    }
                }
            }
        }

        public void SetCustomTitle(string title)
        {
            if (this.IsDisposed) return;
            string formatted = string.IsNullOrEmpty(title) ? AppStrings.General.NewTabTitle : TitleHelper.NormalizeAndWrapTitle(title);
            CurrentTitle = formatted;
            this.Text = formatted;
            EventHandler<string> handler = TitleUpdated;
            if (handler != null)
            {
                handler(this, CurrentTitle);
            }
        }

        public void Reload()
        {
            Reload(false);
        }

        public void Reload(bool ignoreCache)
        {
            if (!string.IsNullOrEmpty(_activeErrorOriginalUrl))
            {
                string target = _activeErrorOriginalUrl;
                _activeErrorOriginalUrl = null;
                LoadUrl(target);
                return;
            }

            if (InternalPageHelper.IsInternalUrl(CurrentUrl))
            {
                InternalPageType type = InternalPageHelper.GetInternalPageType(CurrentUrl);
                if (type == InternalPageType.Bookmarks && _bookmarksView != null && !_bookmarksView.IsDisposed)
                {
                    _bookmarksView.RefreshList();
                }
                else if (type == InternalPageType.History && _historyView != null && !_historyView.IsDisposed)
                {
                    _historyView.RefreshList();
                }
                else if (type == InternalPageType.Downloads && _downloadsView != null && !_downloadsView.IsDisposed)
                {
                    _downloadsView.RefreshList();
                }
                else if (type == InternalPageType.ExportData && _exportDataView != null && !_exportDataView.IsDisposed)
                {
                    _exportDataView.RefreshCounts();
                }
                return;
            }

            if (Browser != null)
            {
                Browser.Reload(ignoreCache);
            }
        }

        public void Stop()
        {
            if (Browser != null)
            {
                Browser.Stop();
            }
        }

        public void GoBack()
        {
            if (Browser != null && Browser.CanGoBack)
            {
                _isNavigatingHistory = true;
                if (_historyIndex > 0) _historyIndex--;
                Browser.Back();
            }
            else if (_historyIndex > 0)
            {
                _historyIndex--;
                _isNavigatingHistory = true;
                LoadUrl(_sessionHistory[_historyIndex].Url);
            }
            else
            {
                LoadUrl(InternalPageHelper.NewTabUrl);
            }
        }

        public void RequestCloseTab()
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(RequestCloseTab));
                    }
                    return;
                }
                EventHandler handler = CloseRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void LoadErrorPage(string originalUrl, string errorFilePath, Image customFavicon = null)
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action<string, string, Image>(LoadErrorPage), originalUrl, errorFilePath, customFavicon);
                    }
                    return;
                }

                _expectingNavigationFromInternal = false;
                _hasInternalFavicon = false;
                _activeErrorOriginalUrl = originalUrl;
                CurrentUrl = originalUrl;
                CurrentTitle = AppStrings.WebError.DefaultPageTitle;
                this.Text = AppStrings.WebError.DefaultPageTitle;

                Image fav = customFavicon;
                if (fav == null)
                {
                    bool isWarning = (errorFilePath != null && (errorFilePath.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 || errorFilePath.IndexOf("ssl", StringComparison.OrdinalIgnoreCase) >= 0));
                    fav = isWarning ? WebErrorPage.GetWarningFavicon() : WebErrorPage.GetStopFavicon();
                }
                SetFavicon(fav);

                HideInternalPages();
                if (Browser != null && !Browser.IsDisposed)
                {
                    Browser.Visible = true;
                    Browser.BringToFront();
                    Browser.LoadUrl(new Uri(errorFilePath).AbsoluteUri);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void Browser_LoadError(object sender, LoadErrorEventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                // Synchronously determine if this is the main frame while frame reference is alive in CEF thread
                bool isMain = false;
                try
                {
                    isMain = (e.Frame == null || e.Frame.IsMain);
                }
                catch
                {
                    isMain = true;
                }

                if (!isMain) return;

                if (e.ErrorCode == CefErrorCode.Aborted)
                {
                    return;
                }

                string failedUrl = e.FailedUrl;
                if (string.IsNullOrEmpty(failedUrl) || 
                    WebErrorPage.IsChromiumInternalErrorPage(failedUrl) ||
                    WebErrorPage.IsErrorPage(failedUrl))
                {
                    return;
                }

                string codeName = (e.ErrorCode == CefErrorCode.NameNotResolved)
                    ? "DNS_PROBE_FINISHED_NXDOMAIN"
                    : e.ErrorCode.ToString();

                WebErrorInfo info = WebErrorPage.CreateFromCefError(
                    codeName,
                    (int)e.ErrorCode,
                    failedUrl,
                    e.ErrorText
                );

                string errorFilePath = WebErrorPage.CreateErrorFile(info);

                try
                {
                    if (e.Frame != null && e.Frame.IsValid)
                    {
                        e.Frame.LoadUrl(new Uri(errorFilePath).AbsoluteUri);
                    }
                }
                catch { }

                SafeInvoke(() =>
                {
                    LoadErrorPage(failedUrl, errorFilePath, WebErrorPage.GetFavicon(info.Kind));
                });
            }
            catch (ObjectDisposedException)
            {
            }
            catch
            {
            }
        }

        public void GoForward()
        {
            if (InternalPageHelper.IsInternalUrl(CurrentUrl))
            {
                if (_historyIndex >= 0 && _historyIndex < _sessionHistory.Count - 1)
                {
                    _historyIndex++;
                    _isNavigatingHistory = true;
                    LoadUrl(_sessionHistory[_historyIndex].Url);
                    return;
                }
                if (Browser != null && Browser.CanGoForward)
                {
                    _expectingNavigationFromInternal = true;
                    _isNavigatingHistory = true;
                    Browser.Forward();
                    return;
                }
            }

            if (Browser != null && Browser.CanGoForward)
            {
                _isNavigatingHistory = true;
                if (_historyIndex < _sessionHistory.Count - 1) _historyIndex++;
                Browser.Forward();
            }
            else if (_historyIndex < _sessionHistory.Count - 1)
            {
                _historyIndex++;
                _isNavigatingHistory = true;
                LoadUrl(_sessionHistory[_historyIndex].Url);
            }
        }

        public void RecordNavigation(string url, string title)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (_isNavigatingHistory)
            {
                _isNavigatingHistory = false;
                if (_historyIndex >= 0 && _historyIndex < _sessionHistory.Count)
                {
                    if (!string.IsNullOrEmpty(title) && (string.IsNullOrEmpty(_sessionHistory[_historyIndex].Title) || _sessionHistory[_historyIndex].Title == "New Tab"))
                    {
                        _sessionHistory[_historyIndex].Title = title;
                    }
                }
                return;
            }

            if (_historyIndex >= 0 && _historyIndex < _sessionHistory.Count)
            {
                if (string.Equals(_sessionHistory[_historyIndex].Url, url, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(title))
                    {
                        _sessionHistory[_historyIndex].Title = title;
                    }
                    return;
                }
            }

            if (_historyIndex >= 0 && _historyIndex < _sessionHistory.Count - 1)
            {
                _sessionHistory.RemoveRange(_historyIndex + 1, _sessionHistory.Count - (_historyIndex + 1));
            }

            _sessionHistory.Add(new TabNavigationEntry(url, title));
            _historyIndex = _sessionHistory.Count - 1;
        }

        public void UpdateCurrentHistoryTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return;
            if (_historyIndex >= 0 && _historyIndex < _sessionHistory.Count)
            {
                _sessionHistory[_historyIndex].Title = title;
            }
        }

        public System.Collections.Generic.List<TabNavigationEntry> GetBackHistory()
        {
            System.Collections.Generic.List<TabNavigationEntry> list = new System.Collections.Generic.List<TabNavigationEntry>();
            if (_sessionHistory.Count == 0 || _historyIndex <= 0) return list;
            for (int i = _historyIndex - 1; i >= 0; i--)
            {
                list.Add(_sessionHistory[i]);
            }
            return list;
        }

        public System.Collections.Generic.List<TabNavigationEntry> GetForwardHistory()
        {
            System.Collections.Generic.List<TabNavigationEntry> list = new System.Collections.Generic.List<TabNavigationEntry>();
            if (_sessionHistory.Count == 0 || _historyIndex >= _sessionHistory.Count - 1) return list;
            for (int i = _historyIndex + 1; i < _sessionHistory.Count; i++)
            {
                list.Add(_sessionHistory[i]);
            }
            return list;
        }

        public void NavigateToHistory(TabNavigationEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Url)) return;
            int idx = _sessionHistory.IndexOf(entry);
            if (idx >= 0)
            {
                _historyIndex = idx;
                _isNavigatingHistory = true;
            }
            LoadUrl(entry.Url);
        }

        public void ShowDevTools()
        {
            if (Browser != null)
            {
                Browser.ShowDevTools();
            }
        }

        public void CloseDevTools()
        {
            if (Browser != null)
            {
                Browser.CloseDevTools();
            }
        }

        public void ZoomIn()
        {
            if (Browser != null)
            {
                ZoomLevel += 0.5;
                Browser.SetZoomLevel(ZoomLevel);
            }
        }

        public void ZoomOut()
        {
            if (Browser != null)
            {
                ZoomLevel -= 0.5;
                Browser.SetZoomLevel(ZoomLevel);
            }
        }

        public void ResetZoom()
        {
            if (Browser != null)
            {
                ZoomLevel = 0.0;
                Browser.SetZoomLevel(ZoomLevel);
            }
        }

        public void ExitFullscreen()
        {
            if (this.IsDisposed) return;
            try
            {
                if (Browser != null && Browser.CanExecuteJavascriptInMainFrame)
                {
                    string js = @"(function() {
                        try {
                            if (document.fullscreenElement || document.webkitFullscreenElement || document.mozFullScreenElement || document.msFullscreenElement) {
                                if (document.exitFullscreen) document.exitFullscreen();
                                else if (document.webkitExitFullscreen) document.webkitExitFullscreen();
                                else if (document.mozCancelFullScreen) document.mozCancelFullScreen();
                                else if (document.msExitFullscreen) document.msExitFullscreen();
                            }
                            var escEvt = new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, which: 27, bubbles: true, cancelable: true });
                            (document.activeElement || document.body || document).dispatchEvent(escEvt);
                            var escUp = new KeyboardEvent('keyup', { key: 'Escape', code: 'Escape', keyCode: 27, which: 27, bubbles: true, cancelable: true });
                            (document.activeElement || document.body || document).dispatchEvent(escUp);
                        } catch(e) {}
                    })();";
                    Browser.ExecuteScriptAsync(js);
                }
            }
            catch { }
        }

        public Task<string> GetSourceAsync()
        {
            if (Browser != null)
            {
                return Browser.GetSourceAsync();
            }
            return null;
        }

        public void Find(string text, bool forward, bool matchCase, bool findNext)
        {
            if (Browser != null && !string.IsNullOrEmpty(text))
            {
                Browser.Find(text, forward, matchCase, findNext);
            }
        }

        public void StopFinding(bool clearSelection)
        {
            if (Browser != null)
            {
                Browser.StopFinding(clearSelection);
            }
        }

        public void SetKeyboardHandler(IKeyboardHandler keyboardHandler)
        {
            if (Browser != null)
            {
                Browser.KeyboardHandler = keyboardHandler;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_CLIPCHILDREN: Prevents parent TabPage from erasing or painting over the docked ChromiumWebBrowser
                cp.Style |= 0x02000000;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) // WM_ERASEBKGND
            {
                // Suppress background erasing to guarantee 100% flicker-free painting behind the web view
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Suppress background erasing when web browser is active to eliminate flickering behind the web view
            if (Browser == null || !Browser.Visible)
            {
                using (SolidBrush brush = new SolidBrush(MaterialColors.ToolbarBackground))
                {
                    e.Graphics.FillRectangle(brush, e.ClipRectangle);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Browser == null || !Browser.Visible)
            {
                using (SolidBrush brush = new SolidBrush(MaterialColors.ToolbarBackground))
                {
                    e.Graphics.FillRectangle(brush, e.ClipRectangle);
                }
            }
            base.OnPaint(e);
        }

        private void HideOtherInternalViews(Control keep)
        {
            if (_bookmarksView != null && _bookmarksView != keep && _bookmarksView.Visible) _bookmarksView.Visible = false;
            if (_historyView != null && _historyView != keep && _historyView.Visible) _historyView.Visible = false;
            if (_downloadsView != null && _downloadsView != keep && _downloadsView.Visible) _downloadsView.Visible = false;
            if (_newTabView != null && _newTabView != keep && _newTabView.Visible) _newTabView.Visible = false;
            if (_incognitoNewTabView != null && _incognitoNewTabView != keep && _incognitoNewTabView.Visible) _incognitoNewTabView.Visible = false;
            if (_screenshotView != null && _screenshotView != keep && _screenshotView.Visible) _screenshotView.Visible = false;
            if (_importDataView != null && _importDataView != keep && _importDataView.Visible) _importDataView.Visible = false;
            if (_exportDataView != null && _exportDataView != keep && _exportDataView.Visible) _exportDataView.Visible = false;
        }

        private void ShowInternalPage(string url)
        {
            InternalPageType type = InternalPageHelper.GetInternalPageType(url);
            if (type == InternalPageType.ClearBrowsingData)
            {
                ShowInternalPage(InternalPageHelper.HistoryUrl);
                wb.Browser.ClearBrowsingData.ClearBrowsingDataDialog.ShowClearBrowsingData((IWin32Window)this.FindForm() ?? this);
                return;
            }
            if (type == InternalPageType.ImportData)
            {
                ShowInternalPage(InternalPageHelper.BookmarksUrl);
                wb.DataPortability.ImportDataDialog.ShowImportDialog((IWin32Window)this.FindForm() ?? this);
                return;
            }
            if (type == InternalPageType.ExportData)
            {
                ShowInternalPage(InternalPageHelper.BookmarksUrl);
                wb.DataPortability.ExportDataDialog.ShowExportDialog((IWin32Window)this.FindForm() ?? this);
                return;
            }
            if (type == InternalPageType.Proxy)
            {
                wb.Proxy.ProxySettingsDialog.ShowProxySettings((IWin32Window)this.FindForm() ?? this);
                return;
            }
            if (type != InternalPageType.NewTab)
            {
                EventHandler<string> loadingHandler = InternalPageLoading;
                if (loadingHandler != null) loadingHandler(this, url);
            }

            if (Browser != null && !Browser.IsDisposed)
            {
                Browser.Visible = false;
            }

            if (type == InternalPageType.Bookmarks)
            {
                if (_bookmarksView != null && !_bookmarksView.IsDisposed && _bookmarksView.Visible)
                {
                    _bookmarksView.BringToFront();
                    return;
                }

                if (_bookmarksView == null || _bookmarksView.IsDisposed)
                {
                    _bookmarksView = new BookmarksView(
                        (targetUrl, openInNewTab) => HandleInternalOpenUrl(targetUrl, openInNewTab),
                        () => RequestCloseTab()
                    );
                    _bookmarksView.Dock = DockStyle.Fill;
                    this.Controls.Add(_bookmarksView);
                }
                HideOtherInternalViews(_bookmarksView);
                _bookmarksView.Visible = true;
                _bookmarksView.BringToFront();
            }
            else if (type == InternalPageType.History)
            {
                if (_historyView != null && !_historyView.IsDisposed && _historyView.Visible)
                {
                    _historyView.BringToFront();
                    return;
                }

                if (_historyView == null || _historyView.IsDisposed)
                {
                    _historyView = new HistoryView(
                        (targetUrl, openInNewTab) => HandleInternalOpenUrl(targetUrl, openInNewTab),
                        () => RequestCloseTab()
                    );
                    _historyView.Dock = DockStyle.Fill;
                    this.Controls.Add(_historyView);
                }
                HideOtherInternalViews(_historyView);
                _historyView.Visible = true;
                _historyView.BringToFront();
            }
            else if (type == InternalPageType.Downloads)
            {
                if (_downloadsView != null && !_downloadsView.IsDisposed && _downloadsView.Visible)
                {
                    _downloadsView.BringToFront();
                    return;
                }

                if (_downloadsView == null || _downloadsView.IsDisposed)
                {
                    _downloadsView = new DownloadsView(
                        () => RequestCloseTab()
                    );
                    _downloadsView.Dock = DockStyle.Fill;
                    this.Controls.Add(_downloadsView);
                }
                HideOtherInternalViews(_downloadsView);
                _downloadsView.Visible = true;
                _downloadsView.BringToFront();
            }
            else if (type == InternalPageType.NewTab || type == InternalPageType.Incognito)
            {
                if (IsIncognito)
                {
                    if (_incognitoNewTabView != null && !_incognitoNewTabView.IsDisposed && _incognitoNewTabView.Visible)
                    {
                        _incognitoNewTabView.BringToFront();
                        return;
                    }
                    if (_incognitoNewTabView == null || _incognitoNewTabView.IsDisposed)
                    {
                        _incognitoNewTabView = new IncognitoNewTabView(
                            (targetUrl, openInNewTab) => HandleInternalOpenUrl(targetUrl, openInNewTab),
                            () => RequestCloseTab()
                        );
                        _incognitoNewTabView.Dock = DockStyle.Fill;
                        this.Controls.Add(_incognitoNewTabView);
                    }
                    HideOtherInternalViews(_incognitoNewTabView);
                    _incognitoNewTabView.Visible = true;
                    _incognitoNewTabView.BringToFront();
                    _incognitoNewTabView.ResetSearchFocus();
                }
                else
                {
                    if (_newTabView != null && !_newTabView.IsDisposed && _newTabView.Visible)
                    {
                        _newTabView.BringToFront();
                        return;
                    }
                    if (_newTabView == null || _newTabView.IsDisposed)
                    {
                        _newTabView = new NewTabView(
                            (targetUrl, openInNewTab) => HandleInternalOpenUrl(targetUrl, openInNewTab),
                            () => RequestCloseTab()
                        );
                        _newTabView.Dock = DockStyle.Fill;
                        this.Controls.Add(_newTabView);
                    }
                    HideOtherInternalViews(_newTabView);
                    _newTabView.Visible = true;
                    _newTabView.BringToFront();
                    _newTabView.ResetSearchFocus();
                }
            }
            else if (type == InternalPageType.Screenshot)
            {
                string sessionId = InternalPageHelper.ExtractScreenshotSessionId(url);
                ScreenshotSession session = ScreenshotSessionManager.GetSession(sessionId);
                if (_screenshotView == null || _screenshotView.IsDisposed || _screenshotView.SessionId != sessionId)
                {
                    if (_screenshotView != null && !_screenshotView.IsDisposed)
                    {
                        try { this.Controls.Remove(_screenshotView); _screenshotView.Dispose(); } catch { }
                    }
                    _screenshotView = new ScreenshotView(
                        session,
                        () => RequestCloseTab()
                    );
                    _screenshotView.Dock = DockStyle.Fill;
                    this.Controls.Add(_screenshotView);
                }
                HideOtherInternalViews(_screenshotView);
                _screenshotView.Visible = true;
                _screenshotView.BringToFront();
                if (this.ClientSize.Width > 50 && this.ClientSize.Height > 50)
                {
                    _screenshotView.Size = this.ClientSize;
                    _screenshotView.FitImageToWindow();
                }

                if (session != null && !string.IsNullOrEmpty(session.SourceTitle))
                {
                    CurrentTitle = "Screenshot - " + session.SourceTitle;
                    this.Text = CurrentTitle;
                }
            }
            else if (type == InternalPageType.ImportData)
            {
                if (_importDataView != null && !_importDataView.IsDisposed && _importDataView.Visible)
                {
                    _importDataView.BringToFront();
                    return;
                }
                if (_importDataView == null || _importDataView.IsDisposed)
                {
                    _importDataView = new ImportDataView(
                        (targetUrl, openInNewTab) => HandleInternalOpenUrl(targetUrl, openInNewTab),
                        () => RequestCloseTab()
                    );
                    _importDataView.Dock = DockStyle.Fill;
                    this.Controls.Add(_importDataView);
                }
                HideOtherInternalViews(_importDataView);
                _importDataView.Visible = true;
                _importDataView.BringToFront();
            }
            else if (type == InternalPageType.ExportData)
            {
                if (_exportDataView != null && !_exportDataView.IsDisposed && _exportDataView.Visible)
                {
                    _exportDataView.BringToFront();
                    return;
                }
                if (_exportDataView == null || _exportDataView.IsDisposed)
                {
                    _exportDataView = new ExportDataView(
                        (targetUrl, openInNewTab) => HandleInternalOpenUrl(targetUrl, openInNewTab),
                        () => RequestCloseTab()
                    );
                    _exportDataView.Dock = DockStyle.Fill;
                    this.Controls.Add(_exportDataView);
                }
                HideOtherInternalViews(_exportDataView);
                _exportDataView.Visible = true;
                _exportDataView.BringToFront();
            }
        }

        public void CloseInternalPages()
        {
            if (_screenshotView != null)
            {
                try
                {
                    _screenshotView.Visible = false;
                    this.Controls.Remove(_screenshotView);
                    _screenshotView.Dispose();
                }
                catch { }
                _screenshotView = null;
            }
            if (_importDataView != null)
            {
                try
                {
                    _importDataView.Visible = false;
                    this.Controls.Remove(_importDataView);
                    _importDataView.Dispose();
                }
                catch { }
                _importDataView = null;
            }
            if (_exportDataView != null)
            {
                try
                {
                    _exportDataView.Visible = false;
                    this.Controls.Remove(_exportDataView);
                    _exportDataView.Dispose();
                }
                catch { }
                _exportDataView = null;
            }
            if (_bookmarksView != null)
            {
                try
                {
                    _bookmarksView.Visible = false;
                    this.Controls.Remove(_bookmarksView);
                    _bookmarksView.Dispose();
                }
                catch { }
                _bookmarksView = null;
            }
            if (_historyView != null)
            {
                try
                {
                    _historyView.Visible = false;
                    this.Controls.Remove(_historyView);
                    _historyView.Dispose();
                }
                catch { }
                _historyView = null;
            }
            if (_downloadsView != null)
            {
                try
                {
                    _downloadsView.Visible = false;
                    this.Controls.Remove(_downloadsView);
                    _downloadsView.Dispose();
                }
                catch { }
                _downloadsView = null;
            }
            if (_newTabView != null)
            {
                try
                {
                    _newTabView.Visible = false;
                    this.Controls.Remove(_newTabView);
                    _newTabView.Dispose();
                }
                catch { }
                _newTabView = null;
            }
            if (_incognitoNewTabView != null)
            {
                try
                {
                    _incognitoNewTabView.Visible = false;
                    this.Controls.Remove(_incognitoNewTabView);
                    _incognitoNewTabView.Dispose();
                }
                catch { }
                _incognitoNewTabView = null;
            }
        }

        private void HideInternalPages()
        {
            HideOtherInternalViews(null);
        }

        private void HandleInternalOpenUrl(string targetUrl, bool openInNewTab)
        {
            InternalPageType curType = InternalPageHelper.GetInternalPageType(CurrentUrl);
            bool shouldOpenInNewTab = openInNewTab ||
                                      curType == InternalPageType.Bookmarks ||
                                      curType == InternalPageType.History;

            if (shouldOpenInNewTab)
            {
                EventHandler<string> handler = OpenNewTabRequested;
                if (handler != null)
                {
                    handler(this, targetUrl);
                    if (curType == InternalPageType.NewTab)
                    {
                        RequestCloseTab();
                    }
                    return;
                }
            }

            LoadUrl(targetUrl);
        }

        public void SelectDownload(long downloadId)
        {
            if (InternalPageHelper.GetInternalPageType(CurrentUrl) == InternalPageType.Downloads)
            {
                ShowInternalPage(CurrentUrl);
                if (_downloadsView != null && !_downloadsView.IsDisposed)
                {
                    _downloadsView.SelectAndDisplayDownload(downloadId);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SetAudioPlayingState(false);
                SetAudioMuted(false);
                SetMediaCaptureState(false);
                if (_bookmarksView != null)
                {
                    try { _bookmarksView.Dispose(); } catch { }
                    _bookmarksView = null;
                }
                if (_historyView != null)
                {
                    try { _historyView.Dispose(); } catch { }
                    _historyView = null;
                }
                if (_downloadsView != null)
                {
                    try { _downloadsView.Dispose(); } catch { }
                    _downloadsView = null;
                }
                if (_newTabView != null)
                {
                    try { _newTabView.Dispose(); } catch { }
                    _newTabView = null;
                }
                if (_incognitoNewTabView != null)
                {
                    try { _incognitoNewTabView.Dispose(); } catch { }
                    _incognitoNewTabView = null;
                }
                if (_screenshotView != null)
                {
                    try { _screenshotView.Dispose(); } catch { }
                    _screenshotView = null;
                }
                if (_importDataView != null)
                {
                    try { _importDataView.Dispose(); } catch { }
                    _importDataView = null;
                }
                if (_exportDataView != null)
                {
                    try { _exportDataView.Dispose(); } catch { }
                    _exportDataView = null;
                }
                if (Browser != null)
                {
                    try { Browser.TitleChanged -= Browser_TitleChanged; } catch { }
                    try { Browser.AddressChanged -= Browser_AddressChanged; } catch { }
                    try { Browser.LoadingStateChanged -= Browser_LoadingStateChanged; } catch { }
                    try { Browser.StatusMessage -= Browser_StatusMessage; } catch { }
                    try { Browser.FrameLoadStart -= Browser_FrameLoadStart; } catch { }
                    try { Browser.FrameLoadEnd -= Browser_FrameLoadEnd; } catch { }
                    try { Browser.LoadError -= Browser_LoadError; } catch { }
                    try { Browser.JavascriptMessageReceived -= Browser_JavascriptMessageReceived; } catch { }
                    try
                    {
                        Browser.Stop();
                    }
                    catch
                    {
                    }
                    try
                    {
                        Browser.Dispose();
                    }
                    catch
                    {
                    }
                    Browser = null;
                }

                // Immediately trim working set after closing tab
                MemoryUtils.TrimMemory(flushWorkingSet: true);
            }
            if (_accentChangedHandler != null)
            {
                try { ThemeManager.AccentColorChanged -= _accentChangedHandler; } catch { }
            }
            base.Dispose(disposing);
        }
    }

    public class ElementSelectedEventArgs : EventArgs
    {
        public string Domain { get; private set; }
        public string Selector { get; private set; }
        public int Matches { get; private set; }
        public string CandidatesJson { get; private set; }

        public ElementSelectedEventArgs(string domain, string selector, int matches, string candidatesJson = null)
        {
            Domain = domain;
            Selector = selector;
            Matches = matches;
            CandidatesJson = candidatesJson;
        }
    }
}
