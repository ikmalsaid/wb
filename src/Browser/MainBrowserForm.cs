using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
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
    public class MainBrowserForm : Form
    {
        private BrowserKeyboardHandler _keyboardHandler;
        private DownloadItemModel _latestDownload;
        private EventHandler<DownloadItemModel> _downloadStartedHandler;
        private EventHandler<DownloadItemModel> _downloadUpdatedHandler;
        private EventHandler<DownloadItemModel> _downloadCompletedHandler;
        private EventHandler<int> _activeDownloadsCountChangedHandler;
        private DownloadBubblePopup _downloadBubble;
        private DateTime _lastBubbleRefresh = DateTime.MinValue;
        private EventHandler<wb.Proxy.ProxyConfig> _proxyConfigChangedHandler;
        private EventHandler _sbConfigChangedHandler;

        private ChromeTabControl _tabControl;
        private WbTopBar _topBar;
        private ContextMenuStrip _gearMenu;
        private ContextMenuStrip _tabContextMenu;
        private ContextMenuStrip _tabStripContextMenu;
        private ContextMenuStrip _backHistoryMenu;
        private ContextMenuStrip _forwardHistoryMenu;

        internal ContextMenuStrip GearMenu { get { return _gearMenu; } }
        internal ContextMenuStrip TabContextMenu { get { return _tabContextMenu; } }
        internal ContextMenuStrip TabStripContextMenu { get { return _tabStripContextMenu; } }
        internal ContextMenuStrip BackHistoryMenu { get { return _backHistoryMenu; } }
        internal ContextMenuStrip ForwardHistoryMenu { get { return _forwardHistoryMenu; } }
        private readonly Stack<string> _recentlyClosedUrls = new Stack<string>();

        private Panel _findPanel;
        private TextBox _findTextBox;
        private Button _findPrevBtn;
        private Button _findNextBtn;
        private Button _findCloseBtn;
        private CheckBox _findMatchCaseChk;
        private Timer _findDebounceTimer;
        private string _lastSearchedText;

        private Panel _blockElementPanel;
        private Label _blockElementTitleLbl;
        private Label _blockElementDescLbl;
        private Button _blockElementCancelBtn;

        private TopProgressBar _topProgressBar;
        private GoogleSecurityCodeBanner _securityCodeBanner;
        private string _securityBannerDismissedUrl;
        private EventHandler _experimentalConfigChangedHandler;
        private Timer _securityBannerDebounceTimer;
        private BrowserTab _activeElementPickerTab;
        private StatusStrip _nativeStatusStrip;
        private ToolStripStatusLabel _statusLabel;
        private ToolStripStatusLabel _statusAdBlockBadge;
        private Timer _statusNotificationTimer;
        private WbBottomBar _bottomBar;
        private WbBookmarksBar _bookmarksBar;
        private TabLoadingOverlay _tabLoadingOverlay;
        private FullScreenExitOverlay _fullScreenOverlay;
        private float _currentDpiScale = 1.0f;
        private bool _isFullScreen = false;
        private bool _isBrowserFullScreen = false;
        private bool _isVideoFullScreen = false;
        private bool _isImmersiveActive = false;
        private bool _immersivePlayerMode = false;
        private bool _prevBookmarksBarVisible = false;
        private FormWindowState _prevWindowState;
        private FormBorderStyle _prevBorderStyle;

        public WbTopBar TopBar
        {
            get { return _topBar; }
        }

        public WbBookmarksBar BookmarksBar
        {
            get { return _bookmarksBar; }
        }

        public WbBottomBar BottomBar
        {
            get { return _bottomBar; }
        }

        public FullScreenExitOverlay FullScreenOverlay
        {
            get { return _fullScreenOverlay; }
        }

        public bool IsFullScreen
        {
            get { return _isFullScreen; }
        }

        public bool IsImmersiveActive
        {
            get { return _isImmersiveActive; }
        }

        public bool ImmersivePlayerMode
        {
            get { return _immersivePlayerMode; }
            set
            {
                _immersivePlayerMode = value;
                if (AppServices.Database != null)
                {
                    AppServices.Database.SetSettingBool("immersive_player_mode", value);
                }
            }
        }

        public int TabPagesCount
        {
            get { return _tabControl != null ? _tabControl.TabPages.Count : 0; }
        }

        internal ChromeTabControl TabControl
        {
            get { return _tabControl; }
        }

        public Rectangle TopBarScreenBounds
        {
            get
            {
                if (_topBar != null && _topBar.IsHandleCreated)
                {
                    return _topBar.RectangleToScreen(_topBar.ClientRectangle);
                }
                return Rectangle.Empty;
            }
        }

        private static IRequestContext _sharedIncognitoRequestContext;
        private static int _openIncognitoWindowCount = 0;
        private static readonly object _incognitoLock = new object();

        public bool IsIncognito { get; private set; }

        public static IRequestContext GetIncognitoRequestContext()
        {
            lock (_incognitoLock)
            {
                if (_sharedIncognitoRequestContext == null || _sharedIncognitoRequestContext.IsDisposed)
                {
                    RequestContextSettings rcSettings = new RequestContextSettings
                    {
                        CachePath = string.Empty,
                        PersistSessionCookies = false
                    };
                    _sharedIncognitoRequestContext = new RequestContext(rcSettings);
                }
                return _sharedIncognitoRequestContext;
            }
        }

        public MainBrowserForm()
            : this(InternalPageHelper.NewTabUrl, false)
        {
        }

        public MainBrowserForm(string initialUrl)
            : this(initialUrl, false)
        {
        }

        public MainBrowserForm(string initialUrl, bool isIncognito)
        {
            this.IsIncognito = isIncognito;
            if (isIncognito)
            {
                lock (_incognitoLock)
                {
                    GetIncognitoRequestContext();
                    _openIncognitoWindowCount++;
                }
            }
            InitializeComponent();
            string startUrl = string.IsNullOrEmpty(initialUrl)
                ? (isIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl)
                : initialUrl;
            AddNewTab(startUrl);
            HookDownloadManager();
            HookProxyManager();
            HookSponsorBlockManager();
        }

        public MainBrowserForm(BrowserTab initialTab, bool isIncognito = false)
        {
            this.IsIncognito = isIncognito || (initialTab != null && initialTab.IsIncognito);
            if (this.IsIncognito)
            {
                lock (_incognitoLock)
                {
                    GetIncognitoRequestContext();
                    _openIncognitoWindowCount++;
                }
            }
            InitializeComponent();
            if (initialTab != null)
            {
                AdoptTab(initialTab);
            }
            else
            {
                AddNewTab(this.IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl);
            }
            HookDownloadManager();
            HookProxyManager();
            HookSponsorBlockManager();
        }

        public static MainBrowserForm CreateNewWindow(string initialUrl = null)
        {
            MainBrowserForm form = new MainBrowserForm(string.IsNullOrEmpty(initialUrl) ? InternalPageHelper.NewTabUrl : initialUrl, isIncognito: false);
            BrowserApplicationContext.RegisterWindow(form);
            return form;
        }

        public static MainBrowserForm CreateNewIncognitoWindow(string initialUrl = null)
        {
            MainBrowserForm form = new MainBrowserForm(string.IsNullOrEmpty(initialUrl) ? InternalPageHelper.IncognitoUrl : initialUrl, isIncognito: true);
            BrowserApplicationContext.RegisterWindow(form);
            return form;
        }

        private void InitializeComponent()
        {
            UITheme.ApplyForm(this);
            this.Text = this.IsIncognito ? AppStrings.Browser.WindowTitleIncognito : AppStrings.Browser.WindowTitle;
            this.Size = new Size(1280, 820);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = UITheme.FontDefault;
            this.KeyPreview = true;
            this.KeyDown += MainBrowserForm_KeyDown;
            InitKeyboardHandler();

            this.Shown += (s, e) =>
            {
                WbAppIcon.UpdateFormIcon(this);

                if (!this.IsIncognito)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        CrashpadHelper.CheckAndPromptStartupCrashReminder(this);
                    }));
                }
            };

            // 1. Unified Single-Row Top Navigation & Tab Bar (IE11 layout)
            _topBar = new WbTopBar();
            _topBar.IsIncognito = this.IsIncognito;
            _topBar.IncognitoBadgeClicked += (s, e) => AddNewTab(InternalPageHelper.IncognitoUrl);
            _topBar.BackClicked += (s, e) => { BrowserTab tab = CurrentTab; if (tab != null) tab.GoBack(); };
            _topBar.BackLongPressed += (s, e) => ShowBackHistoryMenu();
            _topBar.ForwardClicked += (s, e) => { BrowserTab tab = CurrentTab; if (tab != null) tab.GoForward(); };
            _topBar.ForwardLongPressed += (s, e) => ShowForwardHistoryMenu();
            _topBar.RefreshClicked += RefreshButton_Click;
            _topBar.HomeClicked += (s, e) =>
            {
                BrowserTab tab = CurrentTab;
                if (tab != null)
                {
                    tab.GoHome();
                }
                else
                {
                    AddNewTab(this.IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl);
                }
            };
            _topBar.StarClicked += BookmarkToggleBtn_Click;
            _topBar.BookmarksClicked += (s, e) => OpenBookmarks();
            _topBar.HistoryClicked += (s, e) => OpenHistory();
            _topBar.DownloadsClicked += (s, e) => ToggleDownloadBubble();
            _topBar.ShieldClicked += (s, e) => ShowUBlockMenu();
            _topBar.ScreenshotClicked += (s, e) => ShowScreenshotMenu();
            _topBar.GearClicked += (s, e) => ShowGearMenu();
            _topBar.NewTabClicked += (s, e) => AddNewTab(this.IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl);
            _topBar.TabSelected += (s, idx) =>
            {
                if (idx >= 0 && idx < _tabControl.TabPages.Count)
                {
                    if (_tabControl.SelectedIndex != idx)
                    {
                        HideBlockElementPanel();
                        ScreenshotController.CancelActiveSelection();
                        _tabControl.SelectedIndex = idx;
                    }
                }
            };
            _topBar.TabCloseClicked += (s, idx) =>
            {
                if (idx >= 0 && idx < _tabControl.TabPages.Count)
                {
                    CloseTab(_tabControl.TabPages[idx] as BrowserTab);
                }
            };
            _topBar.TabMoved += (s, e) => MoveTabWithinWindow(e.FromIndex, e.ToIndex);
            _topBar.TabTornOff += (s, e) => TearOffTab(e.TabIndex, e.ScreenPosition);
            _topBar.TabDroppedOntoWindow += (s, e) => DropTabOntoWindow(e.TabIndex, e.TargetWindow, e.ScreenPosition, e.TargetDropIndex);
            _topBar.TabRightClicked += (s, e) => ShowTabContextMenu(e.TabIndex, e.ScreenPosition);
            _topBar.TabStripEmptyAreaRightClicked += (s, screenPt) => ShowTabStripEmptyAreaContextMenu(screenPt);
            _topBar.TabAudioClicked += (s, idx) => ToggleTabMute(idx);
            _topBar.NavigateRequested += (s, url) => NavigateFromAddressBar(url);
            _topBar.UrlDroppedAsNewTab += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Url))
                {
                    if (e.TargetTabIndex >= 0 && e.TargetTabIndex <= _tabControl.TabPages.Count)
                    {
                        AddNewTabAt(e.Url, e.TargetTabIndex);
                    }
                    else
                    {
                        AddNewTab(e.Url);
                    }
                }
            };

            // 1b. Bookmarks Bar directly below TopBar
            _bookmarksBar = new WbBookmarksBar();
            _bookmarksBar.NavigateCallback = (url, newTab) =>
            {
                if (newTab) AddNewTab(url);
                else NavigateFromAddressBar(url);
            };
            _bookmarksBar.NewWindowCallback = (url) =>
            {
                MainBrowserForm newWin = new MainBrowserForm(url, false);
                newWin.Show();
            };
            _bookmarksBar.IncognitoCallback = (url) =>
            {
                MainBrowserForm newWin = new MainBrowserForm(url, true);
                newWin.Show();
            };
            _bookmarksBar.OpenBookmarksManagerCallback = () => OpenBookmarks();
            _bookmarksBar.GetCurrentTabInfoCallback = () => CurrentTab != null ? new string[] { CurrentTab.CurrentTitle, CurrentTab.CurrentUrl } : null;
            _bookmarksBar.Visible = AppServices.Bookmarks.ShowBookmarksBar;
            _immersivePlayerMode = AppServices.Database != null && AppServices.Database.GetSettingBool("immersive_player_mode", false);

            _topBar.TabDroppedOnBookmarksBar += (s, e) =>
            {
                if (_bookmarksBar != null && _bookmarksBar.Visible)
                {
                    Rectangle barScreenRect = _bookmarksBar.RectangleToScreen(_bookmarksBar.ClientRectangle);
                    if (barScreenRect.Contains(e.ScreenPosition))
                    {
                        if (e.TabIndex >= 0 && e.TabIndex < _tabControl.TabPages.Count)
                        {
                            BrowserTab tab = _tabControl.TabPages[e.TabIndex] as BrowserTab;
                            if (tab != null && InternalPageHelper.CanBookmarkPage(tab.CurrentUrl))
                            {
                                _bookmarksBar.AddBookmarkFromTab(tab.CurrentTitle, tab.CurrentUrl, e.ScreenPosition);
                                ShowTemporaryNotification(AppStrings.Browser.NotifyBookmarkAddedPrefix + tab.CurrentTitle, 3000);
                                UpdateBookmarkStar();
                                e.Handled = true;
                            }
                        }
                    }
                }
            };

            HookAdBlockEvents();
            ThemeManager.AccentColorChanged += ThemeManager_AccentColorChanged;
            AppServices.SiteMute.MuteStateChanged += (s, domain) =>
            {
                if (this.IsDisposed) return;
                try
                {
                    if (this.IsHandleCreated && this.InvokeRequired)
                    {
                        this.BeginInvoke(new Action(() => SyncSiteMuteState(domain)));
                    }
                    else
                    {
                        SyncSiteMuteState(domain);
                    }
                }
                catch { }
            };
            this.Deactivate += (s, e) =>
            {
                ClearAllWindowMergeHighlights();
                if (_tabLoadingOverlay != null && _tabLoadingOverlay.Visible) _tabLoadingOverlay.HideOverlay();
                this.BeginInvoke(new Action(() =>
                {
                    if (!ToolStripDismissHook.HasActiveDropDownFocus())
                    {
                        ToolStripDismissHook.CloseAll();
                    }
                }));
            };

            // 2. Tab Control (web canvas host with ShowHeader = false)
            _tabControl = new ChromeTabControl();
            _tabControl.BackColor = MaterialColors.ToolbarBackground;
            _tabControl.Dock = DockStyle.Fill;
            _tabControl.Padding = new Point(18, 5);
            _tabControl.SizeMode = TabSizeMode.Normal;
            _tabControl.ShowHeader = false;
            _tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
            _tabControl.NewTabClick += (s, e) => AddNewTab(this.IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl);
            _tabControl.TabCloseClick += (s, idx) =>
            {
                if (idx >= 0 && idx < _tabControl.TabPages.Count)
                {
                    CloseTab(_tabControl.TabPages[idx] as BrowserTab);
                }
            };
            _topBar.BindTabControl(_tabControl);

            // 3. Find in Page Bar
            InitializeFindPanel();

            // 3b. Block Element Panel (docked in window similar to find in page, using accent color, without emoji)
            InitializeBlockElementPanel();

            // 3c. Google Security Code (g.co/sc) Helper Banner
            _securityCodeBanner = new GoogleSecurityCodeBanner();
            _securityCodeBanner.Dismissed += (s, e) =>
            {
                BrowserTab tab = CurrentTab;
                if (tab != null)
                {
                    _securityBannerDismissedUrl = tab.CurrentUrl;
                }
            };

            _securityBannerDebounceTimer = new Timer();
            _securityBannerDebounceTimer.Interval = 70;
            _securityBannerDebounceTimer.Tick += (s, e) =>
            {
                _securityBannerDebounceTimer.Stop();
                ExecuteCheckGoogleSecurityCodeBanner();
            };

            _experimentalConfigChangedHandler = (s, e) => SafeInvoke(delegate { CheckGoogleSecurityCodeBanner(false); });
            ExperimentalManager.ConfigChanged += _experimentalConfigChangedHandler;

            // 4. Slim Top Progress Bar
            _topProgressBar = new TopProgressBar();

            // 5. Native Status Strip with dark mode styling
            InitializeStatusStrip();

            // Layout
            this.Controls.Add(_tabControl);
            // Persistent status strip replaced by modern floating status overlay bubble
            // this.Controls.Add(_nativeStatusStrip);
            this.Controls.Add(_topProgressBar);
            this.Controls.Add(_findPanel);
            this.Controls.Add(_blockElementPanel);
            this.Controls.Add(_securityCodeBanner);
            this.Controls.Add(_bookmarksBar);
            this.Controls.Add(_topBar);

            EnsureTopBarsZOrder();

            if (_bookmarksBar != null) _bookmarksBar.VisibleChanged += (s, e) => EnsureTopBarsZOrder();
            if (_findPanel != null) _findPanel.VisibleChanged += (s, e) => EnsureTopBarsZOrder();
            if (_blockElementPanel != null) _blockElementPanel.VisibleChanged += (s, e) => EnsureTopBarsZOrder();
            if (_securityCodeBanner != null) _securityCodeBanner.VisibleChanged += (s, e) => EnsureTopBarsZOrder();
            if (_topBar != null) _topBar.VisibleChanged += (s, e) => EnsureTopBarsZOrder();

            UpdateDpiScaling();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UITheme.EnableImmersiveDarkMode(this.Handle);
            WbAppIcon.UpdateFormIcon(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_tabControl != null)
            {
                if (_tabControl.TabPages.Count == 0)
                {
                    AddNewTab(this.IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl);
                }
                else
                {
                    if (_tabControl.SelectedIndex < 0)
                    {
                        _tabControl.SelectedIndex = 0;
                    }
                    if (_tabControl.SelectedTab != null)
                    {
                        _tabControl.SelectedTab.Bounds = _tabControl.DisplayRectangle;
                        _tabControl.SelectedTab.Visible = true;
                        if (!_tabControl.SelectedTab.IsHandleCreated)
                        {
                            try { _tabControl.SelectedTab.CreateControl(); } catch { }
                        }
                        BrowserTab curTab = _tabControl.SelectedTab as BrowserTab;
                        if (curTab != null)
                        {
                            curTab.RefreshHostLayout();
                        }
                    }
                }
            }
            if (_topBar != null)
            {
                _topBar.UpdateTabs();
            }
            UpdateNavigationState();
        }

        private void SafeInvoke(Action action)
        {
            if (this.IsDisposed || action == null) return;
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(action); } catch { }
            }
            else
            {
                action();
            }
        }

        private void HookDownloadManager()
        {
            _downloadStartedHandler = (s, item) =>
            {
                _latestDownload = item;
                SafeInvoke(() =>
                {
                    UpdateTopBarDownloadCount();
                    ShowDownloadBubble(autoOpened: true);
                });
            };

            _downloadUpdatedHandler = (s, item) =>
            {
                _latestDownload = item;
                SafeInvoke(() =>
                {
                    UpdateTopBarDownloadCount();
                    if (_downloadBubble != null && !_downloadBubble.IsDisposed && _downloadBubble.Visible)
                    {
                        if ((DateTime.UtcNow - _lastBubbleRefresh).TotalMilliseconds >= 250)
                        {
                            _lastBubbleRefresh = DateTime.UtcNow;
                            _downloadBubble.RefreshContent();
                        }
                    }
                });
            };

            _downloadCompletedHandler = (s, item) =>
            {
                _latestDownload = item;
                SafeInvoke(() =>
                {
                    ShowTemporaryNotification(string.Format(AppStrings.Downloads.NotificationDownloadComplete, item.FileName), 4000);
                    UpdateTopBarDownloadCount();
                    if (_downloadBubble != null && !_downloadBubble.IsDisposed && _downloadBubble.Visible)
                    {
                        _downloadBubble.RefreshContent();
                    }
                });
            };

            _activeDownloadsCountChangedHandler = (s, count) =>
            {
                SafeInvoke(UpdateTopBarDownloadCount);
            };

            AppServices.Downloads.DownloadStarted += _downloadStartedHandler;
            AppServices.Downloads.DownloadUpdated += _downloadUpdatedHandler;
            AppServices.Downloads.DownloadCompleted += _downloadCompletedHandler;
            AppServices.Downloads.ActiveDownloadsCountChanged += _activeDownloadsCountChangedHandler;

            UpdateTopBarDownloadCount();
        }

        private void UnhookDownloadManager()
        {
            if (_downloadStartedHandler != null)
            {
                AppServices.Downloads.DownloadStarted -= _downloadStartedHandler;
                _downloadStartedHandler = null;
            }
            if (_downloadUpdatedHandler != null)
            {
                AppServices.Downloads.DownloadUpdated -= _downloadUpdatedHandler;
                _downloadUpdatedHandler = null;
            }
            if (_downloadCompletedHandler != null)
            {
                AppServices.Downloads.DownloadCompleted -= _downloadCompletedHandler;
                _downloadCompletedHandler = null;
            }
            if (_activeDownloadsCountChangedHandler != null)
            {
                AppServices.Downloads.ActiveDownloadsCountChanged -= _activeDownloadsCountChangedHandler;
                _activeDownloadsCountChangedHandler = null;
            }
        }

        private void HookProxyManager()
        {
            _proxyConfigChangedHandler = (s, cfg) =>
            {
                SafeInvoke(() => ApplyProxyConfigToOpenTabs(cfg));
            };

            AppServices.Proxy.ConfigChanged += _proxyConfigChangedHandler;
        }

        private void UnhookProxyManager()
        {
            if (_proxyConfigChangedHandler != null)
            {
                AppServices.Proxy.ConfigChanged -= _proxyConfigChangedHandler;
                _proxyConfigChangedHandler = null;
            }
        }

        private void HookSponsorBlockManager()
        {
            _sbConfigChangedHandler = (s, e) =>
            {
                SafeInvoke(() => ApplySponsorBlockConfigToOpenTabs());
            };

            AppServices.SponsorBlock.ConfigChanged += _sbConfigChangedHandler;
        }

        private void UnhookSponsorBlockManager()
        {
            if (_sbConfigChangedHandler != null)
            {
                AppServices.SponsorBlock.ConfigChanged -= _sbConfigChangedHandler;
                _sbConfigChangedHandler = null;
            }
        }

        private void ApplySponsorBlockConfigToOpenTabs()
        {
            if (this.IsDisposed) return;
            if (_tabControl != null && _tabControl.TabPages != null)
            {
                foreach (TabPage page in _tabControl.TabPages)
                {
                    BrowserTab bTab = page as BrowserTab;
                    if (bTab != null && !bTab.IsDisposed)
                    {
                        bTab.UpdateSponsorBlockConfig();
                    }
                }
            }
        }

        private void ApplyProxyConfigToOpenTabs(wb.Proxy.ProxyConfig config)
        {
            if (this.IsDisposed || config == null) return;

            var dict = config.ToChromiumDictionary();
            string webrtcPolicy = (config.IsEnabled && config.Mode != wb.Proxy.ProxyMode.System && config.Mode != wb.Proxy.ProxyMode.Direct)
                ? "disable_non_proxied_udp"
                : "default_public_interface_only";

            if (IsIncognito && _sharedIncognitoRequestContext != null && !_sharedIncognitoRequestContext.IsDisposed)
            {
                wb.Proxy.ProxyManager.ApplyContextPreferencesSafe(_sharedIncognitoRequestContext, dict, webrtcPolicy);
            }

            if (_tabControl != null && _tabControl.TabPages != null)
            {
                foreach (TabPage page in _tabControl.TabPages)
                {
                    BrowserTab bTab = page as BrowserTab;
                    if (bTab == null) continue;

                    IRequestContext rc = bTab.RequestContext;
                    if (rc != null && !rc.IsDisposed)
                    {
                        wb.Proxy.ProxyManager.ApplyContextPreferencesSafe(rc, dict, webrtcPolicy);
                    }
                    else if (bTab.Browser != null && bTab.Browser.IsBrowserInitialized)
                    {
                        try
                        {
                            var browser = bTab.Browser.GetBrowser();
                            if (browser != null)
                            {
                                var host = browser.GetHost();
                                if (host != null && host.RequestContext != null && !host.RequestContext.IsDisposed)
                                {
                                    wb.Proxy.ProxyManager.ApplyContextPreferencesSafe(host.RequestContext, dict, webrtcPolicy);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        private void UpdateTopBarDownloadCount()
        {
            if (this.IsDisposed || _topBar == null || _topBar.IsDisposed) return;
            _topBar.ActiveDownloadCount = AppServices.Downloads.GetActiveCount();
        }

        private void ToggleDownloadBubble()
        {
            if (_topBar == null || _topBar.IsDisposed) return;
            if (_downloadBubble != null && !_downloadBubble.IsDisposed)
            {
                if (_downloadBubble.Visible)
                {
                    _downloadBubble.Close();
                    return;
                }

                // If the bubble was visible and just dismissed by the mouse-down on this button,
                // do not immediately reopen it on mouse-up!
                if ((DateTime.UtcNow - _downloadBubble.LastClosedTime).TotalMilliseconds < 350)
                {
                    return;
                }
            }
            ShowDownloadBubble(autoOpened: false);
        }

        private void ShowDownloadBubble(bool autoOpened = false)
        {
            if (_topBar == null || _topBar.IsDisposed || this.IsDisposed) return;
            if (_downloadBubble == null || _downloadBubble.IsDisposed)
            {
                _downloadBubble = new DownloadBubblePopup(() => OpenDownloads());
            }

            Rectangle btnRect = _topBar.GetDownloadsButtonRect();
            if (btnRect.IsEmpty) return;

            _downloadBubble.RefreshContent();

            int bubbleWidth = _downloadBubble.Width > 0 ? _downloadBubble.Width : 360;
            Point screenPt = _topBar.PointToScreen(new Point(btnRect.Right - bubbleWidth, btnRect.Bottom + 4));

            try
            {
                Rectangle screenArea = Screen.FromControl(this).WorkingArea;
                if (screenPt.X + bubbleWidth > screenArea.Right - 8)
                {
                    screenPt.X = screenArea.Right - bubbleWidth - 8;
                }
                if (screenPt.X < screenArea.Left + 8)
                {
                    screenPt.X = screenArea.Left + 8;
                }
            }
            catch { }

            _downloadBubble.ShowBubble(screenPt, autoOpened);
        }

        private void UpdateDownloadStatus(DownloadItemModel item)
        {
            // Bottom bar notification during download ("Downloading... - Click to view") is suppressed
            // to keep the bottom status bar clean while downloads are in progress.
        }

        private void InitializeFindPanel()
        {
            _findPanel = new Panel();
            _findPanel.Dock = DockStyle.Top;
            _findPanel.Height = 36;
            _findPanel.BackColor = MaterialColors.NavBarBackground;
            _findPanel.Visible = false;
            _findPanel.Padding = new Padding(6);

            _findDebounceTimer = new Timer();
            _findDebounceTimer.Interval = 60; // 60ms debounce for responsive, smooth auto-search
            _findDebounceTimer.Tick += (s, e) =>
            {
                _findDebounceTimer.Stop();
                PerformAutoFind();
            };

            Label findLbl = new Label();
            findLbl.Text = AppStrings.Browser.FindLabel;
            findLbl.AutoSize = true;
            findLbl.Location = new Point(10, 9);
            findLbl.Font = UITheme.FontDefault;
            findLbl.ForeColor = MaterialColors.TextSecondary;
            _findPanel.Controls.Add(findLbl);

            _findTextBox = new TextBox();
            _findTextBox.Location = new Point(90, 6);
            _findTextBox.Width = 200;
            UITheme.ApplyTextBox(_findTextBox, AppStrings.Browser.FindPlaceholder);

            // Auto-search as user types
            _findTextBox.TextChanged += (s, e) =>
            {
                _findDebounceTimer.Stop();
                if (string.IsNullOrEmpty(_findTextBox.Text))
                {
                    _lastSearchedText = null;
                    BrowserTab tab = CurrentTab;
                    if (tab != null)
                    {
                        tab.StopFinding(true);
                    }
                }
                else
                {
                    _findDebounceTimer.Start();
                }
            };

            _findTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F3)
                {
                    e.SuppressKeyPress = true;
                    _findDebounceTimer.Stop();
                    bool forward = !e.Shift;
                    FindInPage(forward);
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    HideFindPanel();
                }
            };
            _findPanel.Controls.Add(_findTextBox);

            _findPrevBtn = new Button();
            _findPrevBtn.Size = new Size(28, 24);
            UITheme.ApplyButton(_findPrevBtn);
            _findPrevBtn.FlatAppearance.BorderSize = 0;
            _findPrevBtn.Location = new Point(295, 5);
            _findPrevBtn.Click += (s, e) =>
            {
                _findDebounceTimer.Stop();
                FindInPage(false);
            };
            _findPanel.Controls.Add(_findPrevBtn);

            _findNextBtn = new Button();
            _findNextBtn.Size = new Size(28, 24);
            UITheme.ApplyButton(_findNextBtn);
            _findNextBtn.FlatAppearance.BorderSize = 0;
            _findNextBtn.Location = new Point(325, 5);
            _findNextBtn.Click += (s, e) =>
            {
                _findDebounceTimer.Stop();
                FindInPage(true);
            };
            _findPanel.Controls.Add(_findNextBtn);

            _findMatchCaseChk = new CheckBox();
            _findMatchCaseChk.Text = AppStrings.Browser.FindMatchCase;
            _findMatchCaseChk.Font = UITheme.FontDefault;
            _findMatchCaseChk.ForeColor = MaterialColors.TextPrimary;
            _findMatchCaseChk.AutoSize = true;
            _findMatchCaseChk.Location = new Point(365, 8);
            _findMatchCaseChk.CheckedChanged += (s, e) =>
            {
                _findDebounceTimer.Stop();
                PerformAutoFind();
            };
            _findPanel.Controls.Add(_findMatchCaseChk);

            _findCloseBtn = new Button();
            _findCloseBtn.Size = new Size(26, 24);
            UITheme.ApplyButton(_findCloseBtn);
            _findCloseBtn.FlatAppearance.BorderSize = 0;
            _findCloseBtn.Location = new Point(460, 5);
            _findCloseBtn.Click += (s, e) => HideFindPanel();
            _findPanel.Controls.Add(_findCloseBtn);
        }

        private void InitializeBlockElementPanel()
        {
            _blockElementPanel = new Panel();
            _blockElementPanel.Dock = DockStyle.Top;
            _blockElementPanel.Height = 36;
            _blockElementPanel.BackColor = MaterialColors.NavBarBackground;
            _blockElementPanel.Visible = false;
            _blockElementPanel.Padding = new Padding(12, 6, 12, 6);

            _blockElementPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(MaterialColors.Separator))
                {
                    e.Graphics.DrawLine(p, 0, _blockElementPanel.Height - 1, _blockElementPanel.Width, _blockElementPanel.Height - 1);
                }
            };

            _blockElementTitleLbl = new Label();
            _blockElementTitleLbl.Text = AppStrings.AdBlock.BlockElementPanelTitle;
            _blockElementTitleLbl.Font = UITheme.FontDefaultBold;
            _blockElementTitleLbl.ForeColor = ThemeManager.CurrentAccentColor;
            _blockElementTitleLbl.Location = new Point(12, 9);
            _blockElementTitleLbl.AutoSize = true;
            _blockElementPanel.Controls.Add(_blockElementTitleLbl);

            _blockElementDescLbl = new Label();
            _blockElementDescLbl.Text = AppStrings.AdBlock.BlockElementPanelInstructions;
            _blockElementDescLbl.Font = UITheme.FontDefault;
            _blockElementDescLbl.ForeColor = MaterialColors.TextSecondary;
            _blockElementDescLbl.Location = new Point(110, 9);
            _blockElementDescLbl.AutoSize = true;
            _blockElementPanel.Controls.Add(_blockElementDescLbl);

            _blockElementCancelBtn = new Button();
            _blockElementCancelBtn.Text = AppStrings.General.BtnCancel;
            _blockElementCancelBtn.Size = new Size(65, 24);
            UITheme.ApplyButton(_blockElementCancelBtn);
            _blockElementCancelBtn.FlatAppearance.BorderSize = 0;
            _blockElementCancelBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _blockElementCancelBtn.Location = new Point(_blockElementPanel.Width - 75, 6);
            _blockElementCancelBtn.Click += (s, e) => HideBlockElementPanel();
            _blockElementPanel.Controls.Add(_blockElementCancelBtn);

            _blockElementPanel.Resize += (s, e) =>
            {
                _blockElementCancelBtn.Location = new Point(_blockElementPanel.Width - 75, 6);
            };
        }

        private void InitializeStatusStrip()
        {
            // Modern floating status bottom bar
            _bottomBar = new WbBottomBar();
            _bottomBar.AttachTo(this);
            _bottomBar.DpiScale = _currentDpiScale;

            // Smooth loading overlay for tab switches and internal page transitions
            _tabLoadingOverlay = new TabLoadingOverlay();
            _tabLoadingOverlay.AttachTo(this, _tabControl);
            _tabLoadingOverlay.DpiScale = _currentDpiScale;

            // Full screen exit mechanism overlay
            _fullScreenOverlay = new FullScreenExitOverlay(() =>
            {
                if (_isVideoFullScreen || _isImmersiveActive)
                {
                    ExitPlayerFullScreen();
                }
                else if (_isFullScreen)
                {
                    ExitFullScreenCore();
                }
            });
            _fullScreenOverlay.AttachTo(this);
            _fullScreenOverlay.DpiScale = _currentDpiScale;

            _nativeStatusStrip = new StatusStrip();
            _nativeStatusStrip.Dock = DockStyle.Bottom;
            _nativeStatusStrip.Height = Math.Max(22, (int)(24 * _currentDpiScale));
            _nativeStatusStrip.SizingGrip = false;
            _nativeStatusStrip.BackColor = MaterialColors.StatusBackground;
            _nativeStatusStrip.ForeColor = MaterialColors.TextPrimary;
            _nativeStatusStrip.Renderer = new MaterialToolStripRenderer();
            _nativeStatusStrip.Padding = new Padding((int)(8 * _currentDpiScale), 0, (int)(8 * _currentDpiScale), 0);
            _nativeStatusStrip.Visible = false;

            _statusLabel = new ToolStripStatusLabel();
            _statusLabel.Spring = true;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Font = UITheme.FontDefault;
            _statusLabel.ForeColor = MaterialColors.TextSecondary;
            _statusLabel.Text = "Ready";
            _nativeStatusStrip.Items.Add(_statusLabel);

            _statusAdBlockBadge = new ToolStripStatusLabel();
            _statusAdBlockBadge.Alignment = ToolStripItemAlignment.Right;
            _statusAdBlockBadge.Font = UITheme.FontDefaultBold;
            _statusAdBlockBadge.ForeColor = ThemeManager.CurrentAccentColor;
            _statusAdBlockBadge.Visible = false;
            _statusAdBlockBadge.MouseEnter += (s, e) => _nativeStatusStrip.Cursor = Cursors.Hand;
            _statusAdBlockBadge.MouseLeave += (s, e) => _nativeStatusStrip.Cursor = Cursors.Default;
            _statusAdBlockBadge.Click += (s, e) => ShowUBlockMenu();
            _nativeStatusStrip.Items.Add(_statusAdBlockBadge);

            _statusNotificationTimer = new Timer();
            _statusNotificationTimer.Tick += (s, e) =>
            {
                _statusNotificationTimer.Stop();
                if (!this.IsDisposed && _statusLabel != null)
                {
                    _statusLabel.Text = "Ready";
                    _statusLabel.ForeColor = MaterialColors.TextSecondary;
                }
            };
        }

        private void SetStatusText(string status)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(() => SetStatusText(status))); } catch { }
                return;
            }
            if (_bottomBar != null)
            {
                _bottomBar.SetStatus(status);
            }
            if (_statusNotificationTimer != null && _statusNotificationTimer.Enabled)
            {
                return;
            }
            if (_statusLabel != null)
            {
                if (string.IsNullOrEmpty(status) || string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase))
                {
                    _statusLabel.Text = "Ready";
                    _statusLabel.ForeColor = MaterialColors.TextSecondary;
                }
                else
                {
                    _statusLabel.Text = status.Trim().Replace("&", "&&");
                    _statusLabel.ForeColor = MaterialColors.TextPrimary;
                }
            }
        }

        private void ShowTemporaryNotification(string message, int durationMs = 3000)
        {
            if (this.IsDisposed || string.IsNullOrEmpty(message)) return;
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(() => ShowTemporaryNotification(message, durationMs))); } catch { }
                return;
            }
            if (_bottomBar != null)
            {
                _bottomBar.ShowTemporaryMessage(message, durationMs);
            }
            if (_statusLabel != null)
            {
                _statusLabel.Text = message.Trim().Replace("&", "&&");
                _statusLabel.ForeColor = ThemeManager.CurrentAccentColor;
            }
            if (_statusNotificationTimer != null)
            {
                _statusNotificationTimer.Stop();
                _statusNotificationTimer.Interval = Math.Max(500, durationMs);
                _statusNotificationTimer.Start();
            }
        }

        private void SetBlockedCount(int count)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(() => SetBlockedCount(count))); } catch { }
                return;
            }
            if (_bottomBar != null)
            {
                _bottomBar.BlockedCount = count;
            }
            if (_topBar != null && !_topBar.IsDisposed)
            {
                _topBar.BlockedCount = count;
            }
            if (_statusAdBlockBadge != null)
            {
                if (count > 0)
                {
                    _statusAdBlockBadge.Text = string.Format("{0} blocked", count);
                    _statusAdBlockBadge.ForeColor = ThemeManager.CurrentAccentColor;
                    _statusAdBlockBadge.Visible = true;
                }
                else
                {
                    _statusAdBlockBadge.Visible = false;
                    _statusAdBlockBadge.Text = string.Empty;
                }
            }
        }

        private void ShowBlockElementPanel()
        {
            BrowserTab tab = CurrentTab;
            if (tab == null || InternalPageHelper.IsStartPage(tab.CurrentUrl)) return;

            HideFindPanel();
            _activeElementPickerTab = tab;
            if (_blockElementPanel != null)
            {
                _blockElementPanel.Visible = true;
                EnsureTopBarsZOrder();
            }
            tab.StartElementPicker();
        }

        private void HideBlockElementPanel()
        {
            if (_activeBlockElementForm != null && !_activeBlockElementForm.IsDisposed)
            {
                try { _activeBlockElementForm.Close(); } catch { }
                _activeBlockElementForm = null;
            }

            if (_activeElementPickerTab != null && !_activeElementPickerTab.IsDisposed)
            {
                try { _activeElementPickerTab.CancelElementPicker(); } catch { }
                _activeElementPickerTab = null;
            }

            if (_tabControl != null)
            {
                foreach (TabPage p in _tabControl.TabPages)
                {
                    BrowserTab bTab = p as BrowserTab;
                    if (bTab != null && !bTab.IsDisposed && bTab.IsElementPickerActive)
                    {
                        try { bTab.CancelElementPicker(); } catch { }
                    }
                }
            }

            if (_blockElementPanel != null && _blockElementPanel.Visible)
            {
                _blockElementPanel.Visible = false;
                EnsureTopBarsZOrder();
            }
        }

        internal static string FormatShortcutDisplayString(Keys shortcut)
        {
            return UITheme.FormatShortcutDisplayString(shortcut);
        }

        private static ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick, Keys shortcut)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text, null, onClick);
            if (shortcut != Keys.None)
            {
                if (shortcut == Keys.Escape)
                {
                    item.ShortcutKeyDisplayString = "Esc";
                }
                else
                {
                    try
                    {
                        item.ShortcutKeys = shortcut;
                        item.ShowShortcutKeys = true;
                    }
                    catch
                    {
                        if (shortcut == Keys.Escape)
                        {
                            item.ShortcutKeyDisplayString = "Esc";
                        }
                    }
                }

                string displayStr = FormatShortcutDisplayString(shortcut);
                if (!string.IsNullOrEmpty(displayStr))
                {
                    item.ShortcutKeyDisplayString = displayStr;
                }
            }
            UITheme.ApplyMenuItem(item);
            return item;
        }

        private static ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick)
        {
            return CreateMenuItem(text, onClick, Keys.None);
        }

        internal void ShowGearMenu()
        {
            if (_gearMenu != null && _gearMenu.Visible)
            {
                _gearMenu.Close();
                return;
            }

            if (_gearMenu == null)
            {
                _gearMenu = new ContextMenuStrip();
                UITheme.ApplyContextMenu(_gearMenu);
                _gearMenu.Opened += (s, e) => { if (_topBar != null) _topBar.IsGearMenuOpen = true; };
                _gearMenu.Closed += (s, e) => { if (_topBar != null) _topBar.IsGearMenuOpen = false; };
            }
            _gearMenu.Items.Clear();
            _gearMenu.MaximumSize = Size.Empty;

            // Stop item reference retained for shortcut testing and consistency
            ToolStripMenuItem stopItem = CreateMenuItem(AppStrings.Browser.MenuStop, (s, e) => { if (CurrentTab != null) CurrentTab.Stop(); });
            stopItem.ShortcutKeyDisplayString = "Esc";

            // --- 1. Tabs & Windows ---
            ToolStripMenuItem newTabItem = CreateMenuItem(AppStrings.Browser.MenuNewTab, (s, e) => AddNewTab(this.IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl), Keys.Control | Keys.T);
            ToolStripMenuItem newWinItem = CreateMenuItem(AppStrings.Browser.MenuNewWindow, (s, e) => CreateNewWindow(), Keys.Control | Keys.N);
            ToolStripMenuItem newIncognitoWinItem = CreateMenuItem(AppStrings.Browser.MenuNewIncognitoWindow, (s, e) => CreateNewIncognitoWindow(), Keys.Control | Keys.Shift | Keys.N);
            ToolStripMenuItem openFileItem = CreateMenuItem(AppStrings.Browser.MenuOpenFile, (s, e) => OpenLocalFile(), Keys.Control | Keys.O);

            _gearMenu.Items.Add(newTabItem);
            _gearMenu.Items.Add(newWinItem);
            _gearMenu.Items.Add(newIncognitoWinItem);
            _gearMenu.Items.Add(openFileItem);
            _gearMenu.Items.Add(new ToolStripSeparator());

            // --- 2. Browsing Library (Favorites, History, Downloads, Clear Data) ---
            ToolStripMenuItem bookmarksMenu = new ToolStripMenuItem(AppStrings.Browser.MenuFavorites);
            UITheme.ApplyMenuItem(bookmarksMenu);
            ToolStripMenuItem openBookmarks = CreateMenuItem(AppStrings.Browser.MenuViewFavorites, (s, e) => OpenBookmarks(), Keys.Control | Keys.B);
            BrowserTab currentTab = CurrentTab;
            ToolStripMenuItem addBookmark = CreateMenuItem(AppStrings.Browser.MenuAddToFavorites, (s, e) => BookmarkToggleBtn_Click(this, EventArgs.Empty), Keys.Control | Keys.D);
            addBookmark.Enabled = currentTab != null && InternalPageHelper.CanBookmarkPage(currentTab.CurrentUrl);
            ToolStripMenuItem showBookmarksBar = CreateMenuItem(AppStrings.Browser.MenuShowBookmarksBar, (s, e) => ToggleBookmarksBar(), Keys.Control | Keys.Shift | Keys.B);
            showBookmarksBar.Checked = AppServices.Bookmarks.ShowBookmarksBar;
            bookmarksMenu.DropDownItems.Add(openBookmarks);
            bookmarksMenu.DropDownItems.Add(addBookmark);
            bookmarksMenu.DropDownItems.Add(showBookmarksBar);
            _gearMenu.Items.Add(bookmarksMenu);

            ToolStripMenuItem historyItem = CreateMenuItem(AppStrings.Browser.MenuHistory, (s, e) => OpenHistory(), Keys.Control | Keys.H);
            _gearMenu.Items.Add(historyItem);

            ToolStripMenuItem downloadsItem = CreateMenuItem(AppStrings.Browser.MenuViewDownloads, (s, e) => OpenDownloads(), Keys.Control | Keys.J);
            _gearMenu.Items.Add(downloadsItem);

            ToolStripMenuItem clearDataItem = CreateMenuItem(AppStrings.ClearBrowsingData.MenuClearBrowsingData, (s, e) => ShowClearBrowsingDataDialog(), Keys.Control | Keys.Shift | Keys.Delete);
            clearDataItem.ShortcutKeyDisplayString = "Ctrl+Shift+Del";
            _gearMenu.Items.Add(clearDataItem);

            ToolStripMenuItem importDataItem = CreateMenuItem(AppStrings.ImportData.MenuImportData, (s, e) => OpenImportData());
            ToolStripMenuItem exportDataItem = CreateMenuItem(AppStrings.ExportData.MenuExportData, (s, e) => OpenExportData());
            _gearMenu.Items.Add(importDataItem);
            _gearMenu.Items.Add(exportDataItem);
            _gearMenu.Items.Add(new ToolStripSeparator());

            // --- 3. Page & View (Zoom, Full Screen, Print, Find) ---
            ToolStripMenuItem zoomMenu = new ToolStripMenuItem(AppStrings.Browser.MenuZoom);
            UITheme.ApplyMenuItem(zoomMenu);
            int zoomPct = currentTab != null ? (int)Math.Round((1.0 + (currentTab.ZoomLevel * 0.2)) * 100) : 100;
            zoomMenu.Text = string.Format(AppStrings.Browser.MenuZoomPercentFormat, zoomPct);

            ToolStripMenuItem zoomIn = CreateMenuItem(AppStrings.Browser.MenuZoomIn, (s, e) => ZoomPage(0.5), Keys.Control | Keys.Oemplus);
            ToolStripMenuItem zoomOut = CreateMenuItem(AppStrings.Browser.MenuZoomOut, (s, e) => ZoomPage(-0.5), Keys.Control | Keys.OemMinus);
            ToolStripMenuItem zoomReset = CreateMenuItem(AppStrings.Browser.MenuZoomReset, (s, e) => { if (CurrentTab != null) CurrentTab.ResetZoom(); }, Keys.Control | Keys.D0);
            zoomMenu.DropDownItems.Add(zoomIn);
            zoomMenu.DropDownItems.Add(zoomOut);
            zoomMenu.DropDownItems.Add(zoomReset);
            _gearMenu.Items.Add(zoomMenu);

            ToolStripMenuItem fullScreenItem = CreateMenuItem(AppStrings.Browser.MenuFullScreen, (s, e) => ToggleFullScreen(), Keys.F11);
            fullScreenItem.Checked = _isFullScreen;
            _gearMenu.Items.Add(fullScreenItem);

            ToolStripMenuItem printItem = CreateMenuItem(AppStrings.Browser.MenuPrint, (s, e) => PrintPageViaSystemDialog(), Keys.Control | Keys.P);
            _gearMenu.Items.Add(printItem);

            ToolStripMenuItem findItem = CreateMenuItem(AppStrings.Browser.MenuFindOnPage, (s, e) => ShowFindPanel(), Keys.Control | Keys.F);
            _gearMenu.Items.Add(findItem);
            _gearMenu.Items.Add(new ToolStripSeparator());

            // --- 4. Tools & Developer (Screenshot, Proxy, DevTools, View Source, Optimize RAM) ---
            ToolStripMenuItem screenshotSubMenu = ScreenshotMenuHelper.CreateScreenshotSubMenu(this);
            UITheme.ApplyMenuItem(screenshotSubMenu);
            _gearMenu.Items.Add(screenshotSubMenu);

            ToolStripMenuItem proxyItem = CreateMenuItem(AppStrings.Proxy.MenuProxySettings, (s, e) => ShowProxySettingsDialog());
            _gearMenu.Items.Add(proxyItem);

            ToolStripMenuItem devTools = CreateMenuItem(AppStrings.Browser.MenuDevTools, (s, e) => { if (CurrentTab != null) CurrentTab.ShowDevTools(); }, Keys.F12);
            _gearMenu.Items.Add(devTools);

            ToolStripMenuItem viewSource = CreateMenuItem(AppStrings.Browser.MenuViewSource, (s, e) => OpenSourceViewer(), Keys.Control | Keys.U);
            _gearMenu.Items.Add(viewSource);

            long mb = MemoryUtils.GetTotalMemoryMb();
            ToolStripMenuItem trimMemItem = new ToolStripMenuItem(string.Format(AppStrings.Browser.MenuOptimizeRamFormat, mb), null, (s, e) =>
            {
                MemoryUtils.TrimMemory(flushWorkingSet: true);
                long freedMb = MemoryUtils.GetTotalMemoryMb();
                MessageBox.Show(this,
                    string.Format(AppStrings.Browser.RamFlushSuccessFormat, freedMb),
                    AppStrings.Browser.RamFlushTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
            UITheme.ApplyMenuItem(trimMemItem);
            _gearMenu.Items.Add(trimMemItem);

            // Dedicated Debug Submenu under Tools
            ToolStripMenuItem debugMenu = new ToolStripMenuItem(AppStrings.Browser.MenuDebug);
            UITheme.ApplyMenuItem(debugMenu);

            ToolStripMenuItem debugConsole = CreateMenuItem(AppStrings.Browser.MenuDebugConsole, (s, e) => LogcatHelper.ToggleDebugConsole(), Keys.Control | Keys.Shift | Keys.D);
            debugMenu.DropDownItems.Add(debugConsole);

            ToolStripMenuItem simulateCrashItem = CreateMenuItem(AppStrings.Browser.MenuSimulateCrash, (s, e) => PromptSelfInitiatedCrash());
            debugMenu.DropDownItems.Add(simulateCrashItem);

            ToolStripMenuItem saveDumpsItem = new ToolStripMenuItem(AppStrings.Browser.MenuSaveCrashDumps);
            saveDumpsItem.CheckOnClick = true;
            saveDumpsItem.Checked = CrashpadHelper.IsDumpSavingEnabled;
            saveDumpsItem.Click += (s, e) =>
            {
                CrashpadHelper.IsDumpSavingEnabled = saveDumpsItem.Checked;
            };
            UITheme.ApplyMenuItem(saveDumpsItem);
            debugMenu.DropDownItems.Add(saveDumpsItem);

            ToolStripMenuItem openCrashesItem = CreateMenuItem(AppStrings.Browser.MenuOpenCrashesFolder, (s, e) => CrashpadHelper.OpenCrashesFolder());
            debugMenu.DropDownItems.Add(openCrashesItem);

            _gearMenu.Items.Add(debugMenu);
            _gearMenu.Items.Add(new ToolStripSeparator());

            // --- 5. Appearance & Experimental ---
            ToolStripMenuItem themeMenu = BuildAccentColorMenu();
            _gearMenu.Items.Add(themeMenu);

            ToolStripMenuItem toolbarMenu = BuildToolbarButtonsMenu();
            _gearMenu.Items.Add(toolbarMenu);

            ToolStripMenuItem experimentalMenu = BuildExperimentalMenu();
            _gearMenu.Items.Add(experimentalMenu);
            _gearMenu.Items.Add(new ToolStripSeparator());

            // --- 6. About & Exit ---
            ToolStripMenuItem aboutItem = new ToolStripMenuItem(AppStrings.Browser.MenuAboutWb, null, (s, e) =>
            {
                using (Form aboutForm = new Form())
                {
                    UITheme.ApplyForm(aboutForm, AppStrings.Browser.AboutTitle, new Size(420, 210), new Size(420, 210));
                    aboutForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                    aboutForm.MaximizeBox = false;
                    aboutForm.MinimizeBox = false;
                    aboutForm.StartPosition = FormStartPosition.CenterParent;
                    aboutForm.Padding = new Padding(16);

                    TableLayoutPanel layout = new TableLayoutPanel();
                    layout.Dock = DockStyle.Fill;
                    layout.BackColor = UITheme.FormBackground;
                    layout.ColumnCount = 2;
                    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
                    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                    layout.RowCount = 2;
                    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

                    PictureBox iconBox = new PictureBox();
                    iconBox.Size = new Size(48, 48);
                    iconBox.SizeMode = PictureBoxSizeMode.CenterImage;
                    iconBox.Image = WbAppIcon.GetAppBitmap(36);
                    layout.Controls.Add(iconBox, 0, 0);

                    Label textLbl = new Label();
                    textLbl.Text = AppStrings.Browser.AboutDialogText;
                    textLbl.Font = UITheme.FontDefault;
                    textLbl.ForeColor = UITheme.TextPrimary;
                    textLbl.Dock = DockStyle.Fill;
                    layout.Controls.Add(textLbl, 1, 0);

                    FlowLayoutPanel btnFlow = new FlowLayoutPanel();
                    btnFlow.Dock = DockStyle.Fill;
                    btnFlow.FlowDirection = FlowDirection.RightToLeft;
                    btnFlow.Margin = new Padding(0);

                    Button okBtn = new Button { Text = AppStrings.General.BtnClose, Size = new Size(80, 28), DialogResult = DialogResult.OK };
                    UITheme.ApplyButton(okBtn, isPrimary: true);
                    btnFlow.Controls.Add(okBtn);
                    layout.Controls.Add(btnFlow, 1, 1);

                    aboutForm.Controls.Add(layout);
                    aboutForm.AcceptButton = okBtn;
                    aboutForm.CancelButton = okBtn;

                    aboutForm.ShowDialog(this);
                }
            });
            _gearMenu.Items.Add(aboutItem);

            ToolStripMenuItem exitItem = CreateMenuItem(AppStrings.Browser.MenuExit, (s, e) =>
            {
                try
                {
                    if (_gearMenu != null) _gearMenu.Close();
                }
                catch { }

                this.BeginInvoke(new Action(() =>
                {
                    BrowserApplicationContext.ExitApplication();
                }));
            }, Keys.Alt | Keys.F4);
            _gearMenu.Items.Add(exitItem);

            // Position menu underneath Gear button
            if (_topBar != null)
            {
                _topBar.IsGearMenuOpen = true;
                Rectangle gearRect = _topBar.GetGearButtonRect();
                Point screenPoint = _topBar.PointToScreen(new Point(gearRect.Right, gearRect.Bottom + 2));
                UITheme.ConfigureDropDownConstraints(_gearMenu, screenPoint);
                _gearMenu.Show(screenPoint.X - _gearMenu.PreferredSize.Width, screenPoint.Y);
            }
        }

        private void PromptSelfInitiatedCrash()
        {
            DialogResult res = MessageBox.Show(this,
                AppStrings.Browser.CrashConfirmMessage,
                AppStrings.Browser.CrashConfirmTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (res == DialogResult.Yes)
            {
                CrashpadHelper.TriggerSelfInitiatedCrash();
            }
        }

        internal void ShowBackHistoryMenu()
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(ShowBackHistoryMenu));
                return;
            }

            if (_backHistoryMenu != null && _backHistoryMenu.Visible)
            {
                _backHistoryMenu.Close();
                return;
            }

            if (_topBar == null || !_topBar.CanGoBack) return;

            BrowserTab tab = CurrentTab;
            if (tab == null) return;

            if (_backHistoryMenu == null)
            {
                _backHistoryMenu = new ContextMenuStrip();
                UITheme.ApplyContextMenu(_backHistoryMenu);
            }
            _backHistoryMenu.Items.Clear();

            var backList = tab.GetBackHistory();
            if (backList != null && backList.Count > 0)
            {
                int count = Math.Min(backList.Count, 12);
                for (int i = 0; i < count; i++)
                {
                    var entry = backList[i];
                    string title = string.IsNullOrEmpty(entry.Title) ? entry.Url : entry.Title;
                    string displayTitle = TitleHelper.NormalizeAndWrapTitle(title, 45);
                    Image icon = InternalPageHelper.IsInternalUrl(entry.Url)
                        ? InternalPageHelper.GetInternalFavicon(entry.Url, this.IsIncognito)
                        : (AppServices.Favicons.GetFavicon(entry.Url) ?? MaterialIcons.GetIcon("globe", 16, ThemeManager.AccentColor));

                    var item = new ToolStripMenuItem(displayTitle, icon, (s, e) =>
                    {
                        tab.NavigateToHistory(entry);
                    });
                    item.ToolTipText = entry.Url;
                    UITheme.ApplyMenuItem(item);
                    _backHistoryMenu.Items.Add(item);
                }
            }
            else
            {
                var recentHistory = AppServices.History.GetAll();
                int added = 0;
                if (recentHistory != null)
                {
                    foreach (var h in recentHistory)
                    {
                        if (added >= 8) break;
                        if (string.IsNullOrEmpty(h.Url) || InternalPageHelper.IsInternalUrl(h.Url)) continue;
                        string title = string.IsNullOrEmpty(h.Title) ? h.Url : h.Title;
                        string displayTitle = TitleHelper.NormalizeAndWrapTitle(title, 45);
                        Image icon = AppServices.Favicons.GetFavicon(h.Url) ?? MaterialIcons.GetIcon("globe", 16, ThemeManager.AccentColor);
                        string targetUrl = h.Url;
                        var item = new ToolStripMenuItem(displayTitle, icon, (s, e) =>
                        {
                            tab.LoadUrl(targetUrl);
                        });
                        item.ToolTipText = targetUrl;
                        UITheme.ApplyMenuItem(item);
                        _backHistoryMenu.Items.Add(item);
                        added++;
                    }
                }
            }

            if (_backHistoryMenu.Items.Count > 0)
            {
                _backHistoryMenu.Items.Add(new ToolStripSeparator());
            }

            ToolStripMenuItem showHistoryItem = new ToolStripMenuItem(AppStrings.Browser.MenuShowFullHistory, MaterialIcons.GetIcon("clock-counter-clockwise", 16), (s, e) => OpenHistory());
            showHistoryItem.ShortcutKeyDisplayString = "Ctrl+H";
            UITheme.ApplyMenuItem(showHistoryItem);
            _backHistoryMenu.Items.Add(showHistoryItem);

            ToolStripMenuItem clearHistoryItem = new ToolStripMenuItem(AppStrings.ClearBrowsingData.MenuClearBrowsingData, null, (s, e) => ShowClearBrowsingDataDialog());
            clearHistoryItem.ShortcutKeyDisplayString = "Ctrl+Shift+Del";
            UITheme.ApplyMenuItem(clearHistoryItem);
            _backHistoryMenu.Items.Add(clearHistoryItem);

            if (_topBar != null && _topBar.IsHandleCreated)
            {
                Rectangle btnRect = _topBar.GetBackButtonRect();
                Point screenPt = _topBar.PointToScreen(new Point(btnRect.Left, btnRect.Bottom + 2));
                _backHistoryMenu.Show(screenPt);
            }
        }

        internal void ShowForwardHistoryMenu()
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(ShowForwardHistoryMenu));
                return;
            }

            if (_forwardHistoryMenu != null && _forwardHistoryMenu.Visible)
            {
                _forwardHistoryMenu.Close();
                return;
            }

            if (_topBar == null || !_topBar.CanGoForward) return;

            BrowserTab tab = CurrentTab;
            if (tab == null) return;

            if (_forwardHistoryMenu == null)
            {
                _forwardHistoryMenu = new ContextMenuStrip();
                UITheme.ApplyContextMenu(_forwardHistoryMenu);
            }
            _forwardHistoryMenu.Items.Clear();

            var fwdList = tab.GetForwardHistory();
            if (fwdList != null && fwdList.Count > 0)
            {
                int count = Math.Min(fwdList.Count, 12);
                for (int i = 0; i < count; i++)
                {
                    var entry = fwdList[i];
                    string title = string.IsNullOrEmpty(entry.Title) ? entry.Url : entry.Title;
                    string displayTitle = TitleHelper.NormalizeAndWrapTitle(title, 45);
                    Image icon = InternalPageHelper.IsInternalUrl(entry.Url)
                        ? InternalPageHelper.GetInternalFavicon(entry.Url, this.IsIncognito)
                        : (AppServices.Favicons.GetFavicon(entry.Url) ?? MaterialIcons.GetIcon("globe", 16, ThemeManager.AccentColor));

                    var item = new ToolStripMenuItem(displayTitle, icon, (s, e) =>
                    {
                        tab.NavigateToHistory(entry);
                    });
                    item.ToolTipText = entry.Url;
                    UITheme.ApplyMenuItem(item);
                    _forwardHistoryMenu.Items.Add(item);
                }
            }

            if (_forwardHistoryMenu.Items.Count > 0)
            {
                _forwardHistoryMenu.Items.Add(new ToolStripSeparator());
            }

            ToolStripMenuItem showHistoryItem = new ToolStripMenuItem(AppStrings.Browser.MenuShowFullHistory, MaterialIcons.GetIcon("clock-counter-clockwise", 16), (s, e) => OpenHistory());
            showHistoryItem.ShortcutKeyDisplayString = "Ctrl+H";
            UITheme.ApplyMenuItem(showHistoryItem);
            _forwardHistoryMenu.Items.Add(showHistoryItem);

            ToolStripMenuItem clearHistoryItem = new ToolStripMenuItem(AppStrings.ClearBrowsingData.MenuClearBrowsingData, null, (s, e) => ShowClearBrowsingDataDialog());
            clearHistoryItem.ShortcutKeyDisplayString = "Ctrl+Shift+Del";
            UITheme.ApplyMenuItem(clearHistoryItem);
            _forwardHistoryMenu.Items.Add(clearHistoryItem);

            if (_topBar != null && _topBar.IsHandleCreated)
            {
                Rectangle btnRect = _topBar.GetForwardButtonRect();
                Point screenPt = _topBar.PointToScreen(new Point(btnRect.Left, btnRect.Bottom + 2));
                _forwardHistoryMenu.Show(screenPt);
            }
        }

        private void OpenLocalFile()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = AppStrings.Browser.OpenFileDialogTitle;
                ofd.Filter = AppStrings.Browser.OpenFileDialogFilter;
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    string url;
                    try
                    {
                        url = new Uri(ofd.FileName).AbsoluteUri;
                    }
                    catch
                    {
                        url = "file:///" + ofd.FileName.Replace('\\', '/');
                    }

                    BrowserTab tab = CurrentTab;
                    if (tab != null)
                    {
                        tab.LoadUrl(url);
                    }
                    else
                    {
                        AddNewTab(url);
                    }

                    if (_topBar != null)
                    {
                        _topBar.AddressText = InternalPageHelper.IsStartPage(url) ? string.Empty : url;
                    }
                }
            }
        }

        public void ToggleFullScreen()
        {
            if (!_isFullScreen)
            {
                _isBrowserFullScreen = true;
                EnterFullScreenCore();
            }
            else
            {
                _isBrowserFullScreen = false;
                ExitFullScreenCore();
            }
        }

        private void EnterFullScreenCore()
        {
            if (_isFullScreen) return;

            _prevWindowState = this.WindowState;
            _prevBorderStyle = this.FormBorderStyle;
            _prevBookmarksBarVisible = (_bookmarksBar != null && _bookmarksBar.Visible);

            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Normal;
            this.WindowState = FormWindowState.Maximized;

            if (_topBar != null) _topBar.Visible = false;
            if (_bookmarksBar != null) _bookmarksBar.Visible = false;

            _isFullScreen = true;
            if (_fullScreenOverlay != null) _fullScreenOverlay.ShowForFullScreen();
        }

        private void ExitFullScreenCore()
        {
            if (!_isFullScreen) return;

            this.FormBorderStyle = _prevBorderStyle;
            this.WindowState = _prevWindowState;

            if (_topBar != null) _topBar.Visible = true;
            if (_bookmarksBar != null)
            {
                _bookmarksBar.Visible = _prevBookmarksBarVisible && (AppServices.Bookmarks != null ? AppServices.Bookmarks.ShowBookmarksBar : true);
            }
            EnsureTopBarsZOrder();

            _isFullScreen = false;
            _isBrowserFullScreen = false;
            _isVideoFullScreen = false;

            if (_fullScreenOverlay != null) _fullScreenOverlay.HideForFullScreen();
        }

        private void EnterImmersivePlayer()
        {
            if (_isImmersiveActive) return;
            _isImmersiveActive = true;
            _prevBookmarksBarVisible = (_bookmarksBar != null && _bookmarksBar.Visible);

            if (_topBar != null) _topBar.Visible = false;
            if (_bookmarksBar != null) _bookmarksBar.Visible = false;

            if (_fullScreenOverlay != null)
            {
                _fullScreenOverlay.Suppressed = true;
                _fullScreenOverlay.HideForFullScreen();
            }
        }

        private void ExitImmersivePlayer()
        {
            if (!_isImmersiveActive) return;
            _isImmersiveActive = false;

            if (_topBar != null) _topBar.Visible = true;
            if (_bookmarksBar != null)
            {
                _bookmarksBar.Visible = _prevBookmarksBarVisible && (AppServices.Bookmarks != null ? AppServices.Bookmarks.ShowBookmarksBar : true);
            }
            EnsureTopBarsZOrder();

            if (_fullScreenOverlay != null)
            {
                _fullScreenOverlay.Suppressed = false;
                _fullScreenOverlay.HideForFullScreen();
            }
        }

        public void ToggleImmersiveMode()
        {
            ImmersivePlayerMode = !ImmersivePlayerMode;
            if (_isVideoFullScreen && !_isBrowserFullScreen)
            {
                if (_immersivePlayerMode && _isFullScreen)
                {
                    ExitFullScreenCore();
                    EnterImmersivePlayer();
                }
                else if (!_immersivePlayerMode && _isImmersiveActive)
                {
                    ExitImmersivePlayer();
                    EnterFullScreenCore();
                }
            }
        }

        public void ExitPlayerFullScreen()
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(ExitPlayerFullScreen));
                return;
            }

            if (CurrentTab != null)
            {
                CurrentTab.ExitFullscreen();
            }

            if (_isImmersiveActive)
            {
                ExitImmersivePlayer();
            }

            if (_isVideoFullScreen && !_isBrowserFullScreen && _isFullScreen)
            {
                ExitFullScreenCore();
            }

            _isVideoFullScreen = false;
        }

        public void HandleEscapeKey()
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(HandleEscapeKey));
                return;
            }

            if (_isVideoFullScreen || _isImmersiveActive)
            {
                ExitPlayerFullScreen();
                return;
            }

            if (_isFullScreen)
            {
                ToggleFullScreen();
                return;
            }

            if (_blockElementPanel != null && _blockElementPanel.Visible)
            {
                HideBlockElementPanel();
                return;
            }

            if (CurrentTab != null && CurrentTab.IsLoading)
            {
                CurrentTab.Stop();
                return;
            }
        }

        public BrowserTab CurrentTab
        {
            get
            {
                if (this.IsDisposed) return null;
                try
                {
                    if (_tabControl != null && !_tabControl.IsDisposed && _tabControl.SelectedTab is BrowserTab)
                    {
                        BrowserTab tab = (BrowserTab)_tabControl.SelectedTab;
                        if (tab != null && !tab.IsDisposed) return tab;
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                return null;
            }
        }

        public void AddNewTab(string url)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(AddNewTab), url);
                return;
            }
            if (_tabLoadingOverlay != null && _tabLoadingOverlay.Visible)
            {
                _tabLoadingOverlay.HideOverlay();
            }
            if (!IsIncognito && !string.IsNullOrEmpty(url) &&
                (string.Equals(url, InternalPageHelper.IncognitoUrl, StringComparison.OrdinalIgnoreCase) ||
                 InternalPageHelper.GetInternalPageType(url) == InternalPageType.Incognito))
            {
                CreateNewIncognitoWindow();
                return;
            }
            string defaultUrl = IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl;
            string targetUrl = string.IsNullOrEmpty(url) ? defaultUrl : url;
            BrowserTab tab = new BrowserTab(targetUrl, IsIncognito, IsIncognito ? GetIncognitoRequestContext() : null);
            AdoptTab(tab);
        }

        public void AdoptTabAt(BrowserTab tab, int targetIndex)
        {
            if (tab == null) return;

            WireTabEvents(tab);
            if (_keyboardHandler != null)
            {
                tab.SetKeyboardHandler(_keyboardHandler);
            }

            if (_tabControl != null && !_tabControl.IsHandleCreated)
            {
                var unused = _tabControl.Handle;
            }

            int insertIdx = Math.Max(0, Math.Min(_tabControl.TabPages.Count, targetIndex));
            if (_tabControl.TabPages.Count == 0)
            {
                _tabControl.TabPages.Add(tab);
            }
            else
            {
                _tabControl.TabPages.Insert(insertIdx, tab);
            }
            _tabControl.SelectedTab = tab;
            tab.EnsureBrowserAlive();
            tab.RefreshHostLayout();
            tab.IsTransferring = false;
            if (_topBar != null)
            {
                _topBar.UpdateTabs();
            }
            UpdateNavigationState();
        }

        public void AdoptTab(BrowserTab tab)
        {
            AdoptTabAt(tab, _tabControl != null ? _tabControl.TabPages.Count : 0);
        }

        public void DetachTab(BrowserTab tab)
        {
            if (tab == null) return;

            tab.IsTransferring = true;
            UnwireTabEvents(tab);
            int index = _tabControl.TabPages.IndexOf(tab);
            if (index >= 0)
            {
                int nextIndex = -1;
                if (_tabControl.SelectedIndex == index)
                {
                    nextIndex = (index == _tabControl.TabPages.Count - 1) ? index - 1 : index;
                }
                _tabControl.TabPages.Remove(tab);
                if (_tabControl.TabPages.Count > 0 && nextIndex >= 0)
                {
                    _tabControl.SelectedIndex = Math.Max(0, Math.Min(_tabControl.TabPages.Count - 1, nextIndex));
                }
            }

            if (_tabControl.TabPages.Count > 0 && (_tabControl.SelectedIndex < 0 || _tabControl.SelectedIndex >= _tabControl.TabPages.Count))
            {
                _tabControl.SelectedIndex = Math.Max(0, _tabControl.TabPages.Count - 1);
            }

            if (_topBar != null)
            {
                _topBar.UpdateTabs();
            }
            UpdateNavigationState();
        }

        public void MoveTabWithinWindow(int fromIndex, int toIndex)
        {
            if (fromIndex == toIndex) return;
            if (fromIndex < 0 || fromIndex >= _tabControl.TabPages.Count) return;
            if (toIndex < 0 || toIndex >= _tabControl.TabPages.Count) return;

            BrowserTab tab = _tabControl.TabPages[fromIndex] as BrowserTab;
            if (tab == null) return;

            bool wasSelected = (_tabControl.SelectedIndex == fromIndex);

            tab.IsTransferring = true;
            _tabControl.SuspendLayout();
            try
            {
                _tabControl.TabPages.RemoveAt(fromIndex);
                _tabControl.TabPages.Insert(toIndex, tab);
                if (wasSelected)
                {
                    _tabControl.SelectedIndex = toIndex;
                }
                else if (_tabControl.SelectedIndex > fromIndex && _tabControl.SelectedIndex <= toIndex)
                {
                    _tabControl.SelectedIndex--;
                }
                else if (_tabControl.SelectedIndex < fromIndex && _tabControl.SelectedIndex >= toIndex)
                {
                    _tabControl.SelectedIndex++;
                }
            }
            finally
            {
                _tabControl.ResumeLayout();
                tab.IsTransferring = false;
            }

            if (wasSelected)
            {
                tab.RefreshHostLayout();
                tab.FocusBrowser();
            }

            if (_topBar != null)
            {
                _topBar.UpdateTabs();
            }
        }

        private void TearOffTab(int tabIndex, Point screenPt)
        {
            if (tabIndex < 0 || tabIndex >= _tabControl.TabPages.Count) return;

            BrowserTab tab = _tabControl.TabPages[tabIndex] as BrowserTab;
            if (tab == null) return;

            DetachTab(tab);

            MainBrowserForm newWin = new MainBrowserForm(tab, this.IsIncognito);
            newWin.StartPosition = FormStartPosition.Manual;
            newWin.Location = new Point(Math.Max(0, screenPt.X - 100), Math.Max(0, screenPt.Y - 20));
            BrowserApplicationContext.RegisterWindow(newWin);
            newWin.Activate();
            tab.FocusBrowser();

            if (_tabControl.TabPages.Count == 0)
            {
                this.Close();
            }
        }

        private void DropTabOntoWindow(int tabIndex, Form targetWindow, Point screenPt, int targetDropIndex = -1)
        {
            MainBrowserForm targetBrowser = targetWindow as MainBrowserForm;
            if (targetBrowser == null || targetBrowser == this || targetBrowser.IsDisposed || this.IsIncognito != targetBrowser.IsIncognito) return;
            if (tabIndex < 0 || tabIndex >= _tabControl.TabPages.Count) return;

            BrowserTab tab = _tabControl.TabPages[tabIndex] as BrowserTab;
            if (tab == null) return;

            int insertIndex = targetBrowser.TabPagesCount;
            if (targetDropIndex >= 0)
            {
                insertIndex = targetDropIndex;
            }
            else if (targetBrowser.TopBar != null)
            {
                Point targetPt = targetBrowser.TopBar.PointToClient(screenPt);
                int dropIdx = targetBrowser.TopBar.ComputeTabDropIndex(targetPt.X);
                if (dropIdx >= 0)
                {
                    insertIndex = dropIdx;
                }
            }

            DetachTab(tab);
            targetBrowser.AdoptTabAt(tab, insertIndex);
            targetBrowser.Activate();
            tab.FocusBrowser();

            if (_tabControl.TabPages.Count == 0)
            {
                this.Close();
            }
        }

        public void MergeInto(MainBrowserForm targetBrowser)
        {
            if (targetBrowser == null || targetBrowser == this || targetBrowser.IsDisposed || this.IsIncognito != targetBrowser.IsIncognito) return;

            List<BrowserTab> tabsToMove = new List<BrowserTab>();
            foreach (TabPage page in _tabControl.TabPages)
            {
                BrowserTab bTab = page as BrowserTab;
                if (bTab != null)
                {
                    tabsToMove.Add(bTab);
                }
            }

            if (tabsToMove.Count == 0) return;

            foreach (BrowserTab tab in tabsToMove)
            {
                DetachTab(tab);
                targetBrowser.AdoptTab(tab);
            }

            this.Close();
            targetBrowser.Activate();
            if (targetBrowser.CurrentTab != null)
            {
                targetBrowser.CurrentTab.FocusBrowser();
            }
        }

        private static List<MainBrowserForm> GetOpenBrowserWindowsSnapshot(MainBrowserForm exclude = null, bool? incognitoFilter = null)
        {
            List<MainBrowserForm> list = new List<MainBrowserForm>();
            try
            {
                FormCollection openForms = Application.OpenForms;
                if (openForms != null)
                {
                    for (int i = 0; i < openForms.Count; i++)
                    {
                        try
                        {
                            MainBrowserForm f = openForms[i] as MainBrowserForm;
                            if (f != null && f != exclude && !f.IsDisposed)
                            {
                                if (!incognitoFilter.HasValue || f.IsIncognito == incognitoFilter.Value)
                                {
                                    list.Add(f);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }

        private void UpdateWindowMergeHighlightOnMove()
        {
            Point mousePos = Cursor.Position;
            bool anyHover = false;
            List<MainBrowserForm> openWindows = GetOpenBrowserWindowsSnapshot(this, this.IsIncognito);
            for (int i = 0; i < openWindows.Count; i++)
            {
                MainBrowserForm targetBrowser = openWindows[i];
                if (targetBrowser != null && !targetBrowser.IsDisposed && targetBrowser.Visible && targetBrowser.WindowState != FormWindowState.Minimized)
                {
                    Rectangle topBarBounds = targetBrowser.TopBarScreenBounds;
                    Rectangle dropZone = new Rectangle(topBarBounds.Left, topBarBounds.Top, topBarBounds.Width, topBarBounds.Height + 15);
                    bool hoveringTopBar = (!topBarBounds.IsEmpty && dropZone.Contains(mousePos));
                    if (targetBrowser.TopBar != null)
                    {
                        if (targetBrowser.TopBar.DropHighlight != hoveringTopBar)
                        {
                            targetBrowser.TopBar.DropHighlight = hoveringTopBar;
                            targetBrowser.TopBar.Update();
                        }
                    }
                    if (hoveringTopBar)
                    {
                        anyHover = true;
                    }
                }
            }

            // ONLY show the indicator on the target window! The dragged window must NOT show it.
            // Translucency allows the target window's indicator underneath to be clearly seen without obstruction.
            if (this.TopBar != null && this.TopBar.DropHighlight)
            {
                this.TopBar.DropHighlight = false;
            }
            this.Opacity = anyHover ? 0.65 : 1.0;
        }

        private void ClearAllWindowMergeHighlights()
        {
            this.Opacity = 1.0;
            if (this.TopBar != null)
            {
                this.TopBar.DropHighlight = false;
            }
            List<MainBrowserForm> openWindows = GetOpenBrowserWindowsSnapshot(null, this.IsIncognito);
            for (int i = 0; i < openWindows.Count; i++)
            {
                MainBrowserForm targetBrowser = openWindows[i];
                if (targetBrowser != null && !targetBrowser.IsDisposed && targetBrowser.TopBar != null)
                {
                    targetBrowser.TopBar.DropHighlight = false;
                }
            }
        }

        private void CheckWindowMergeOnMove()
        {
            Point mousePos = Cursor.Position;
            List<MainBrowserForm> openWindows = GetOpenBrowserWindowsSnapshot(this, this.IsIncognito);
            for (int i = 0; i < openWindows.Count; i++)
            {
                MainBrowserForm targetBrowser = openWindows[i];
                if (targetBrowser != null && !targetBrowser.IsDisposed && targetBrowser.Visible && targetBrowser.WindowState != FormWindowState.Minimized)
                {
                    Rectangle topBarBounds = targetBrowser.TopBarScreenBounds;
                    Rectangle dropZone = new Rectangle(topBarBounds.Left, topBarBounds.Top, topBarBounds.Width, topBarBounds.Height + 15);
                    // ONLY merge if mouse released directly over the target window's TopBar!
                    if (!topBarBounds.IsEmpty && dropZone.Contains(mousePos))
                    {
                        MergeInto(targetBrowser);
                        break;
                    }
                }
            }
        }

        private void WireTabEvents(BrowserTab tab)
        {
            if (tab == null) return;
            tab.TitleUpdated += Tab_TitleUpdated;
            tab.AddressUpdated += Tab_AddressUpdated;
            tab.LoadingStateUpdated += Tab_LoadingStateUpdated;
            tab.StatusMessageUpdated += Tab_StatusMessageUpdated;
            tab.OpenNewTabRequested += Tab_OpenNewTabRequested;
            tab.OpenNewWindowRequested += Tab_OpenNewWindowRequested;
            tab.OpenNewIncognitoWindowRequested += Tab_OpenNewIncognitoWindowRequested;
            tab.ViewSourceRequested += Tab_ViewSourceRequested;
            tab.BlockedCountChanged += Tab_BlockedCountChanged;
            tab.FaviconChanged += Tab_FaviconChanged;
            tab.CloseRequested += Tab_CloseRequested;
            tab.ElementPickerStarted += Tab_ElementPickerStarted;
            tab.ElementSelectedForBlock += Tab_ElementSelectedForBlock;
            tab.ElementPickerCancelled += Tab_ElementPickerCancelled;
            tab.SponsorBlockMenuRequested += Tab_SponsorBlockMenuRequested;
            tab.InternalPageLoading += Tab_InternalPageLoading;
            tab.NavigatingFromInternalPage += Tab_NavigatingFromInternalPage;
            tab.FullscreenModeChanged += Tab_FullscreenModeChanged;
        }

        private void UnwireTabEvents(BrowserTab tab)
        {
            if (tab == null) return;
            tab.TitleUpdated -= Tab_TitleUpdated;
            tab.AddressUpdated -= Tab_AddressUpdated;
            tab.LoadingStateUpdated -= Tab_LoadingStateUpdated;
            tab.StatusMessageUpdated -= Tab_StatusMessageUpdated;
            tab.OpenNewTabRequested -= Tab_OpenNewTabRequested;
            tab.OpenNewWindowRequested -= Tab_OpenNewWindowRequested;
            tab.OpenNewIncognitoWindowRequested -= Tab_OpenNewIncognitoWindowRequested;
            tab.ViewSourceRequested -= Tab_ViewSourceRequested;
            tab.BlockedCountChanged -= Tab_BlockedCountChanged;
            tab.FaviconChanged -= Tab_FaviconChanged;
            tab.CloseRequested -= Tab_CloseRequested;
            tab.ElementPickerStarted -= Tab_ElementPickerStarted;
            tab.ElementSelectedForBlock -= Tab_ElementSelectedForBlock;
            tab.ElementPickerCancelled -= Tab_ElementPickerCancelled;
            tab.SponsorBlockMenuRequested -= Tab_SponsorBlockMenuRequested;
            tab.InternalPageLoading -= Tab_InternalPageLoading;
            tab.NavigatingFromInternalPage -= Tab_NavigatingFromInternalPage;
            tab.FullscreenModeChanged -= Tab_FullscreenModeChanged;
        }

        private void Tab_FullscreenModeChanged(object sender, bool isFullScreen)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => Tab_FullscreenModeChanged(sender, isFullScreen)));
                return;
            }
            if (sender != CurrentTab) return;

            if (isFullScreen)
            {
                _isVideoFullScreen = true;
                if (_immersivePlayerMode && !_isBrowserFullScreen)
                {
                    EnterImmersivePlayer();
                }
                else if (!_isFullScreen)
                {
                    EnterFullScreenCore();
                }
            }
            else
            {
                _isVideoFullScreen = false;
                if (_isImmersiveActive)
                {
                    ExitImmersivePlayer();
                }
                else if (!_isBrowserFullScreen && _isFullScreen)
                {
                    ExitFullScreenCore();
                }
            }
        }

        private void Tab_InternalPageLoading(object sender, string url)
        {
            if (this.IsDisposed) return;
            if (sender == CurrentTab)
            {
                UpdateUBlockButton();
            }
            // Suppress loading screen overlay for New Tab and cached internal pages (instant native WinForms UI)
            if (InternalPageHelper.GetInternalPageType(url) == InternalPageType.NewTab ||
                InternalPageHelper.IsInternalUrl(url) ||
                InternalPageHelper.IsInternalPage(url) ||
                string.Equals(url, InternalPageHelper.NewTabUrl, StringComparison.OrdinalIgnoreCase) ||
                InternalPageHelper.IsStartPage(url))
            {
                if (_tabLoadingOverlay != null && _tabLoadingOverlay.Visible)
                {
                    _tabLoadingOverlay.HideOverlay();
                }
                return;
            }

            if (sender == CurrentTab && _tabLoadingOverlay != null)
            {
                string title = InternalPageHelper.GetInternalTitle(url);
                _tabLoadingOverlay.ShowForInternalPage(title, 200);
            }
        }

        private void Tab_NavigatingFromInternalPage(object sender, string url)
        {
            if (this.IsDisposed) return;
            BrowserTab tab = sender as BrowserTab;
            if (sender == CurrentTab)
            {
                UpdateUBlockButton();
            }
            // Navigating out from New Tab: instant transition with no loading screen
            if (_tabLoadingOverlay != null && _tabLoadingOverlay.Visible)
            {
                _tabLoadingOverlay.HideOverlay();
            }
        }

        private void Tab_ElementPickerStarted(object sender, EventArgs e)
        {
            _activeElementPickerTab = sender as BrowserTab;
            if (_blockElementPanel != null && !_blockElementPanel.Visible)
            {
                HideFindPanel();
                _blockElementPanel.Visible = true;
            }
        }

        private BlockElementForm _activeBlockElementForm;

        private void Tab_ElementSelectedForBlock(object sender, ElementSelectedEventArgs e)
        {
            BrowserTab tab = sender as BrowserTab;
            if (tab == null || tab != CurrentTab) return;

            if (_activeBlockElementForm != null && !_activeBlockElementForm.IsDisposed)
            {
                try { _activeBlockElementForm.Activate(); } catch { }
                return;
            }

            using (BlockElementForm form = new BlockElementForm(tab, e.Domain, e.Selector, e.Matches, e.CandidatesJson))
            {
                _activeBlockElementForm = form;
                try
                {
                    DialogResult res = form.ShowDialog(this);
                    if (res == DialogResult.OK || res == DialogResult.Cancel)
                    {
                        HideBlockElementPanel();
                    }
                    else if (res == DialogResult.Retry)
                    {
                        // Pick Another was clicked; keep _blockElementPanel open and resumed
                    }
                }
                finally
                {
                    _activeBlockElementForm = null;
                }
            }
        }

        private void Tab_ElementPickerCancelled(object sender, EventArgs e)
        {
            HideBlockElementPanel();
        }

        private void Tab_SponsorBlockMenuRequested(object sender, EventArgs e)
        {
            OpenSponsorBlockSettings();
        }

        public void OpenSponsorBlockSettings()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(OpenSponsorBlockSettings));
                return;
            }

            using (SponsorBlockSettingsForm form = new SponsorBlockSettingsForm())
            {
                form.ShowDialog(this);
            }
        }

        public void OpenSponsorBlockWhitelist()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(OpenSponsorBlockWhitelist));
                return;
            }

            using (SponsorBlockWhitelistForm form = new SponsorBlockWhitelistForm())
            {
                form.ShowDialog(this);
            }
        }

        private void Tab_CloseRequested(object sender, EventArgs e)
        {
            BrowserTab tab = sender as BrowserTab;
            if (tab != null)
            {
                CloseTab(tab);
            }
        }

        private void Tab_OpenNewTabRequested(object sender, string targetUrl)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<object, string>(Tab_OpenNewTabRequested), sender, targetUrl);
                return;
            }

            BrowserTab senderTab = sender as BrowserTab;
            int insertIndex = -1;
            if (senderTab != null && _tabControl != null)
            {
                int senderIdx = _tabControl.TabPages.IndexOf(senderTab);
                if (senderIdx >= 0)
                {
                    insertIndex = senderIdx + 1;
                }
            }

            string url = string.IsNullOrEmpty(targetUrl) ? InternalPageHelper.NewTabUrl : targetUrl;
            if (insertIndex >= 0)
            {
                AddNewTabAt(url, insertIndex);
            }
            else
            {
                AddNewTab(url);
            }
        }

        private void Tab_OpenNewWindowRequested(object sender, string targetUrl)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<object, string>(Tab_OpenNewWindowRequested), sender, targetUrl);
                return;
            }
            MainBrowserForm newWin = this.IsIncognito
                ? MainBrowserForm.CreateNewIncognitoWindow(string.IsNullOrEmpty(targetUrl) ? InternalPageHelper.IncognitoUrl : targetUrl)
                : MainBrowserForm.CreateNewWindow(string.IsNullOrEmpty(targetUrl) ? InternalPageHelper.NewTabUrl : targetUrl);
            newWin.Show();
        }

        private void Tab_OpenNewIncognitoWindowRequested(object sender, string targetUrl)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<object, string>(Tab_OpenNewIncognitoWindowRequested), sender, targetUrl);
                return;
            }
            MainBrowserForm newWin = MainBrowserForm.CreateNewIncognitoWindow(string.IsNullOrEmpty(targetUrl) ? InternalPageHelper.IncognitoUrl : targetUrl);
            newWin.Show();
        }

        private void Tab_ViewSourceRequested()
        {
            OpenSourceViewer();
        }

        private void Tab_BlockedCountChanged(object sender, int count)
        {
            if (this.IsDisposed) return;
            BrowserTab tab = sender as BrowserTab;
            if (tab != null && !tab.IsDisposed && tab == CurrentTab)
            {
                UpdateUBlockButton();
            }
        }

        private void Tab_FaviconChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                try
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(() => Tab_FaviconChanged(sender, e)));
                    }
                    return;
                }
                catch { }
            }

            if (!this.IsDisposed && _topBar != null && !_topBar.IsDisposed)
            {
                _topBar.UpdateTabs();
                _topBar.Invalidate();
                try { _topBar.Update(); } catch { }
            }
        }

        public void CloseTab(BrowserTab tab)
        {
            if (tab == null) return;

            // Remember closed tab URL if not blank/startpage (internal pages like bookmarks, history, downloads are eligible)
            if (!string.IsNullOrEmpty(tab.CurrentUrl) &&
                !InternalPageHelper.IsStartPage(tab.CurrentUrl) &&
                !string.Equals(tab.CurrentUrl, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                _recentlyClosedUrls.Push(tab.CurrentUrl);
            }

            if (_tabLoadingOverlay != null && _tabLoadingOverlay.Visible)
            {
                _tabLoadingOverlay.HideOverlay();
            }

            if (_tabControl.TabPages.Count > 1)
            {
                DetachTab(tab);
                tab.Dispose();
            }
            else
            {
                DetachTab(tab);
                tab.Dispose();
                AddNewTab(InternalPageHelper.NewTabUrl);
            }

            UpdateNavigationState();
            UpdateRamDisplay();
        }

        public BrowserTab AddNewTabAt(string url, int targetIndex)
        {
            if (_tabLoadingOverlay != null && _tabLoadingOverlay.Visible)
            {
                _tabLoadingOverlay.HideOverlay();
            }
            if (!IsIncognito && !string.IsNullOrEmpty(url) &&
                (string.Equals(url, InternalPageHelper.IncognitoUrl, StringComparison.OrdinalIgnoreCase) ||
                 InternalPageHelper.GetInternalPageType(url) == InternalPageType.Incognito))
            {
                CreateNewIncognitoWindow();
                return null;
            }
            string defaultUrl = IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl;
            string targetUrl = string.IsNullOrEmpty(url) ? defaultUrl : url;
            BrowserTab tab = new BrowserTab(targetUrl, IsIncognito, IsIncognito ? GetIncognitoRequestContext() : null);
            AdoptTabAt(tab, targetIndex);
            return tab;
        }

        public void DuplicateTab(BrowserTab tab, int targetIndex)
        {
            if (tab == null) return;
            string defaultUrl = IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl;
            string url = tab.CurrentUrl;
            if (string.IsNullOrEmpty(url)) url = defaultUrl;
            AddNewTabAt(url, targetIndex);
        }

        public void TogglePinTab(BrowserTab tab)
        {
            if (tab == null) return;
            int currentIdx = _tabControl.TabPages.IndexOf(tab);
            if (currentIdx < 0) return;

            bool newPinned = !tab.IsPinned;
            tab.IsPinned = newPinned;

            int pinnedCount = 0;
            for (int i = 0; i < _tabControl.TabPages.Count; i++)
            {
                BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                if (t != null && t.IsPinned && t != tab) pinnedCount++;
            }

            int destIdx = newPinned ? pinnedCount : pinnedCount;
            if (currentIdx != destIdx)
            {
                MoveTabWithinWindow(currentIdx, destIdx);
            }
            else
            {
                if (_topBar != null) _topBar.UpdateTabs();
            }
        }

        public void ToggleTabMute(int index)
        {
            if (index < 0 || index >= _tabControl.TabPages.Count) return;
            BrowserTab tab = _tabControl.TabPages[index] as BrowserTab;
            if (tab != null)
            {
                // Pinned tabs can only be muted/unmuted via right-click context menu
                if (tab.IsPinned) return;
                ToggleTabMute(tab);
            }
        }

        public void ToggleTabMute(BrowserTab tab)
        {
            if (tab == null) return;
            bool newMuted = !tab.IsAudioMuted;
            tab.SetAudioMuted(newMuted);

            // Persist domain mute state across sessions
            string domain = AppServices.SiteMute.ExtractDomain(tab.CurrentUrl);
            if (!string.IsNullOrEmpty(domain))
            {
                AppServices.SiteMute.SetSiteMuted(domain, newMuted);
                // Also update any other open tabs for this domain
                for (int i = 0; i < _tabControl.TabPages.Count; i++)
                {
                    BrowserTab otherTab = _tabControl.TabPages[i] as BrowserTab;
                    if (otherTab != null && otherTab != tab)
                    {
                        string otherDomain = AppServices.SiteMute.ExtractDomain(otherTab.CurrentUrl);
                        if (string.Equals(domain, otherDomain, StringComparison.OrdinalIgnoreCase))
                        {
                            otherTab.SetAudioMuted(newMuted);
                        }
                    }
                }
            }

            if (_topBar != null) _topBar.Invalidate();
        }

        private void SyncSiteMuteState(string domain)
        {
            if (this.IsDisposed || _tabControl == null) return;
            bool isMuted = AppServices.SiteMute.IsDomainMuted(domain);
            for (int i = 0; i < _tabControl.TabPages.Count; i++)
            {
                BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                if (t != null && !string.IsNullOrEmpty(t.CurrentUrl))
                {
                    string tabDomain = AppServices.SiteMute.ExtractDomain(t.CurrentUrl);
                    if (string.Equals(domain, tabDomain, StringComparison.OrdinalIgnoreCase))
                    {
                        if (t.IsAudioMuted != isMuted)
                        {
                            t.SetAudioMuted(isMuted);
                        }
                    }
                }
            }
            if (_topBar != null && !_topBar.IsDisposed) _topBar.Invalidate();
        }

        public void CloseOtherTabs(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= _tabControl.TabPages.Count) return;
            BrowserTab keepTab = _tabControl.TabPages[targetIndex] as BrowserTab;
            List<BrowserTab> toClose = new List<BrowserTab>();
            for (int i = 0; i < _tabControl.TabPages.Count; i++)
            {
                BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                if (t != null && t != keepTab && !t.IsPinned)
                {
                    toClose.Add(t);
                }
            }
            foreach (BrowserTab t in toClose)
            {
                CloseTab(t);
            }
        }

        public void CloseTabsToRight(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= _tabControl.TabPages.Count) return;
            List<BrowserTab> toClose = new List<BrowserTab>();
            for (int i = _tabControl.TabPages.Count - 1; i > targetIndex; i--)
            {
                BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                if (t != null && !t.IsPinned)
                {
                    toClose.Add(t);
                }
            }
            foreach (BrowserTab t in toClose)
            {
                CloseTab(t);
            }
        }

        public void CloseTabsToLeft(int targetIndex)
        {
            if (targetIndex <= 0 || targetIndex >= _tabControl.TabPages.Count) return;
            List<BrowserTab> toClose = new List<BrowserTab>();
            for (int i = 0; i < targetIndex; i++)
            {
                BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                if (t != null && !t.IsPinned)
                {
                    toClose.Add(t);
                }
            }
            foreach (BrowserTab t in toClose)
            {
                CloseTab(t);
            }
        }

        public void ReopenLastClosedTab()
        {
            if (_recentlyClosedUrls.Count > 0)
            {
                string url = _recentlyClosedUrls.Pop();
                AddNewTab(url);
            }
        }

        public void ToggleBookmarksBar()
        {
            AppServices.Bookmarks.ShowBookmarksBar = !AppServices.Bookmarks.ShowBookmarksBar;
            if (_bookmarksBar != null)
            {
                _bookmarksBar.Visible = AppServices.Bookmarks.ShowBookmarksBar && !_isFullScreen && !_isImmersiveActive;
                EnsureTopBarsZOrder();
            }
        }

        /// <summary>
        /// Enforces the strict vertical docking hierarchy for all top bars:
        /// 1. Main Top Bar (_topBar) - always at the very top (Top = 0).
        /// 2. Bookmarks Bar (_bookmarksBar) - always directly below the main TopBar (when visible/unhidden).
        /// 3. Additional bars if any (_securityCodeBanner, _blockElementPanel, _findPanel, _topProgressBar) - always below BookmarksBar.
        /// 4. TabControl (_tabControl) - fills remaining client canvas.
        /// </summary>
        public void EnsureTopBarsZOrder()
        {
            if (this.IsDisposed) return;
            try
            {
                this.SuspendLayout();

                if (_topProgressBar != null && this.Controls.Contains(_topProgressBar))
                {
                    _topProgressBar.SendToBack();
                }
                if (_findPanel != null && this.Controls.Contains(_findPanel))
                {
                    _findPanel.SendToBack();
                }
                if (_blockElementPanel != null && this.Controls.Contains(_blockElementPanel))
                {
                    _blockElementPanel.SendToBack();
                }
                if (_securityCodeBanner != null && this.Controls.Contains(_securityCodeBanner))
                {
                    _securityCodeBanner.SendToBack();
                }
                if (_bookmarksBar != null && this.Controls.Contains(_bookmarksBar))
                {
                    _bookmarksBar.SendToBack();
                }
                if (_topBar != null && this.Controls.Contains(_topBar))
                {
                    _topBar.SendToBack();
                }

                this.ResumeLayout(true);
            }
            catch { }
        }

        private void BookmarkTab(BrowserTab tab)
        {
            if (tab == null || !InternalPageHelper.CanBookmarkPage(tab.CurrentUrl)) return;

            string url = tab.CurrentUrl;
            string title = !string.IsNullOrEmpty(tab.CurrentTitle) ? tab.CurrentTitle : url;
            bool isBookmarked = AppServices.Bookmarks.IsBookmarked(url);

            if (!isBookmarked)
            {
                using (BookmarkEditDialog dialog = new BookmarkEditDialog(
                    AppStrings.Bookmarks.AddTitle,
                    title,
                    url,
                    showBookmarkCheckbox: false,
                    isBookmarked: false,
                    primaryButtonText: AppStrings.General.BtnAdd,
                    initialFolderId: "bar",
                    showRemoveButton: false))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        AppServices.Bookmarks.Add(dialog.ItemTitle, dialog.ItemUrl, dialog.SelectedFolderId);
                        ShowTemporaryNotification(AppStrings.Browser.NotifyBookmarkAddedPrefix + dialog.ItemTitle, 3000);
                        UpdateBookmarkStar();
                    }
                }
            }
            else
            {
                BookmarkItem existing = AppServices.Bookmarks.GetByUrl(url);
                string editTitle = existing != null ? existing.Title : title;
                string editUrl = existing != null ? existing.Url : url;
                string editFolder = existing != null ? existing.ParentId : "bar";

                using (BookmarkEditDialog dialog = new BookmarkEditDialog(
                    AppStrings.Bookmarks.EditTitle,
                    editTitle,
                    editUrl,
                    showBookmarkCheckbox: false,
                    isBookmarked: true,
                    primaryButtonText: AppStrings.General.BtnSave,
                    initialFolderId: editFolder,
                    showRemoveButton: true))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        if (dialog.IsDeleted)
                        {
                            if (existing != null)
                            {
                                AppServices.Bookmarks.Remove(existing.Id);
                            }
                            else
                            {
                                AppServices.Bookmarks.RemoveByUrl(url);
                            }
                            ShowTemporaryNotification(AppStrings.Browser.NotifyBookmarkRemoved, 2000);
                        }
                        else
                        {
                            if (existing != null)
                            {
                                AppServices.Bookmarks.Update(existing.Id, dialog.ItemTitle, dialog.ItemUrl, dialog.SelectedFolderId);
                            }
                            else
                            {
                                AppServices.Bookmarks.Add(dialog.ItemTitle, dialog.ItemUrl, dialog.SelectedFolderId);
                            }
                        }
                        UpdateBookmarkStar();
                    }
                }
            }
        }

        public void BookmarkAllTabs()
        {
            if (_tabControl == null || _tabControl.TabPages == null || _tabControl.TabPages.Count == 0) return;

            int addedCount = 0;
            for (int i = 0; i < _tabControl.TabPages.Count; i++)
            {
                BrowserTab tab = _tabControl.TabPages[i] as BrowserTab;
                if (tab != null && !string.IsNullOrEmpty(tab.CurrentUrl) && InternalPageHelper.CanBookmarkPage(tab.CurrentUrl))
                {
                    string url = tab.CurrentUrl;
                    string title = !string.IsNullOrEmpty(tab.CurrentTitle) ? tab.CurrentTitle : url;
                    AppServices.Bookmarks.Add(title, url);
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                string msg = string.Format(AppStrings.Browser.NotifyBookmarkAllAddedFormat, addedCount, addedCount == 1 ? "" : "s");
                ShowTemporaryNotification(msg, 3000);
            }
            UpdateBookmarkStar();
        }

        private void EditTab(BrowserTab tab)
        {
            if (tab == null || tab.IsDisposed) return;

            string origUrl = tab.CurrentUrl ?? string.Empty;
            string origTitle = tab.CurrentTitle ?? string.Empty;
            bool canBookmark = InternalPageHelper.CanBookmarkPage(origUrl);
            bool isBookmarked = canBookmark && AppServices.Bookmarks.IsBookmarked(origUrl);

            using (BookmarkEditDialog dialog = new BookmarkEditDialog(
                AppStrings.Browser.EditTabTitle,
                origTitle,
                origUrl,
                canBookmark,
                isBookmarked,
                AppStrings.General.BtnSave))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    string newTitle = dialog.ItemTitle;
                    string newUrl = dialog.ItemUrl;
                    bool bookmarkChecked = dialog.IsBookmarked;

                    // 1. Update Title if changed
                    if (!string.IsNullOrEmpty(newTitle) && !string.Equals(newTitle, origTitle, StringComparison.Ordinal))
                    {
                        tab.SetCustomTitle(newTitle);
                        if (tab == CurrentTab)
                        {
                            this.Text = TitleHelper.FormatWindowTitle(newTitle, this.IsIncognito);
                        }
                        if (_topBar != null)
                        {
                            _topBar.UpdateTabs();
                        }
                    }

                    // 2. Navigate if URL changed
                    if (!string.IsNullOrEmpty(newUrl) && !string.Equals(newUrl, origUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        string targetUrl = ResolveTargetUrl(newUrl);
                        tab.LoadUrl(targetUrl);
                        if (tab == CurrentTab && _topBar != null)
                        {
                            _topBar.AddressText = InternalPageHelper.IsStartPage(targetUrl) ? string.Empty : targetUrl;
                        }
                    }

                    // 3. Handle Bookmark checkbox if page can be bookmarked
                    if (canBookmark)
                    {
                        string effectiveUrl = !string.IsNullOrEmpty(newUrl) ? newUrl : origUrl;
                        string effectiveTitle = !string.IsNullOrEmpty(newTitle) ? newTitle : (!string.IsNullOrEmpty(origTitle) ? origTitle : effectiveUrl);

                        if (bookmarkChecked)
                        {
                            AppServices.Bookmarks.Add(effectiveTitle, effectiveUrl);
                            if (isBookmarked && !string.Equals(origUrl, effectiveUrl, StringComparison.OrdinalIgnoreCase))
                            {
                                AppServices.Bookmarks.RemoveByUrl(origUrl);
                            }
                        }
                        else if (isBookmarked && !bookmarkChecked)
                        {
                            AppServices.Bookmarks.RemoveByUrl(origUrl);
                        }

                        UpdateBookmarkStar();
                    }
                }
            }
        }

        private void ShowTabContextMenu(int tabIndex, Point screenPt)
        {
            if (tabIndex < 0 || tabIndex >= _tabControl.TabPages.Count) return;
            BrowserTab tab = _tabControl.TabPages[tabIndex] as BrowserTab;
            if (tab == null) return;

            if (_tabContextMenu == null)
            {
                _tabContextMenu = new ContextMenuStrip();
                UITheme.ApplyContextMenu(_tabContextMenu);
                _tabContextMenu.ShowImageMargin = true;
                _tabContextMenu.ShowCheckMargin = false;
            }
            _tabContextMenu.Items.Clear();

            int count = _tabControl.TabPages.Count;

            // 1. New tab to the right
            ToolStripMenuItem newTabRightItem = CreateMenuItem(AppStrings.Browser.TabMenuNewTabRight, (s, e) =>
            {
                AddNewTabAt(InternalPageHelper.NewTabUrl, tabIndex + 1);
            });
            _tabContextMenu.Items.Add(newTabRightItem);

            // 2. Reload
            ToolStripMenuItem reloadItem = CreateMenuItem(AppStrings.Browser.TabMenuReload, (s, e) =>
            {
                tab.Reload();
            }, Keys.Control | Keys.R);
            _tabContextMenu.Items.Add(reloadItem);

            // 3. Duplicate
            ToolStripMenuItem duplicateItem = CreateMenuItem(AppStrings.Browser.TabMenuDuplicate, (s, e) =>
            {
                DuplicateTab(tab, tabIndex + 1);
            });
            _tabContextMenu.Items.Add(duplicateItem);

            ToolStripMenuItem openIncognitoItem = CreateMenuItem(AppStrings.Browser.TabMenuOpenInIncognito, (s, e) =>
            {
                MainBrowserForm incWin = CreateNewIncognitoWindow(tab.CurrentUrl);
                incWin.Show();
            });
            _tabContextMenu.Items.Add(openIncognitoItem);

            _tabContextMenu.Items.Add(new ToolStripSeparator());

            // 4. Pin / Unpin Tab
            string pinText = tab.IsPinned ? AppStrings.Browser.TabMenuUnpin : AppStrings.Browser.TabMenuPin;
            ToolStripMenuItem pinItem = CreateMenuItem(pinText, (s, e) =>
            {
                TogglePinTab(tab);
            });
            _tabContextMenu.Items.Add(pinItem);

            // 5. Mute site / Unmute site
            string muteText = tab.IsAudioMuted ? AppStrings.Browser.TabMenuUnmuteSite : AppStrings.Browser.TabMenuMuteSite;
            ToolStripMenuItem muteItem = CreateMenuItem(muteText, (s, e) =>
            {
                ToggleTabMute(tab);
            });
            _tabContextMenu.Items.Add(muteItem);

            _tabContextMenu.Items.Add(new ToolStripSeparator());

            // 6. Close tab
            ToolStripMenuItem closeTabItem = CreateMenuItem(AppStrings.Browser.TabMenuClose, (s, e) =>
            {
                CloseTab(tab);
            }, Keys.Control | Keys.W);
            _tabContextMenu.Items.Add(closeTabItem);

            // 7. Close other tabs (protects pinned tabs)
            ToolStripMenuItem closeOthersItem = CreateMenuItem(AppStrings.Browser.TabMenuCloseOthers, (s, e) =>
            {
                CloseOtherTabs(tabIndex);
            });
            int otherUnpinned = 0;
            for (int i = 0; i < count; i++)
            {
                BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                if (t != null && t != tab && !t.IsPinned) otherUnpinned++;
            }
            closeOthersItem.Enabled = (otherUnpinned > 0);
            _tabContextMenu.Items.Add(closeOthersItem);

            // 8. Close tabs to the right
            ToolStripMenuItem closeRightItem = CreateMenuItem(AppStrings.Browser.TabMenuCloseRight, (s, e) =>
            {
                CloseTabsToRight(tabIndex);
            });
            int unpinnedToRight = 0;
            for (int i = tabIndex + 1; i < count; i++)
            {
                BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                if (t != null && !t.IsPinned) unpinnedToRight++;
            }
            closeRightItem.Enabled = (unpinnedToRight > 0);
            _tabContextMenu.Items.Add(closeRightItem);

            // 9. Close tabs to the left
            ToolStripMenuItem closeLeftItem = CreateMenuItem(AppStrings.Browser.TabMenuCloseLeft, (s, e) =>
            {
                CloseTabsToLeft(tabIndex);
            });
            int unpinnedToLeft = 0;
            for (int i = 0; i < tabIndex; i++)
            {
                BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                if (t != null && !t.IsPinned) unpinnedToLeft++;
            }
            closeLeftItem.Enabled = (unpinnedToLeft > 0);
            _tabContextMenu.Items.Add(closeLeftItem);

            _tabContextMenu.Items.Add(new ToolStripSeparator());

            // 10. Reopen closed tab
            ToolStripMenuItem reopenItem = CreateMenuItem(AppStrings.Browser.TabMenuReopenClosed, (s, e) =>
            {
                ReopenLastClosedTab();
            }, Keys.Control | Keys.Shift | Keys.T);
            reopenItem.Enabled = (_recentlyClosedUrls.Count > 0);
            _tabContextMenu.Items.Add(reopenItem);

            // 11. Bookmark tab
            ToolStripMenuItem bookmarkItem = CreateMenuItem(AppStrings.Browser.TabMenuBookmark, (s, e) =>
            {
                BookmarkTab(tab);
            }, Keys.Control | Keys.D);
            bookmarkItem.Enabled = InternalPageHelper.CanBookmarkPage(tab.CurrentUrl);
            _tabContextMenu.Items.Add(bookmarkItem);

            // 11b. Bookmark all tabs
            ToolStripMenuItem bookmarkAllItem = CreateMenuItem(AppStrings.Browser.TabMenuBookmarkAll, (s, e) =>
            {
                BookmarkAllTabs();
            }, Keys.Control | Keys.Shift | Keys.D);
            bool hasBookmarkableTab = false;
            for (int i = 0; i < count; i++)
            {
                BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                if (t != null && !string.IsNullOrEmpty(t.CurrentUrl) && InternalPageHelper.CanBookmarkPage(t.CurrentUrl))
                {
                    hasBookmarkableTab = true;
                    break;
                }
            }
            bookmarkAllItem.Enabled = hasBookmarkableTab;
            _tabContextMenu.Items.Add(bookmarkAllItem);

            // 12. Edit tab
            ToolStripMenuItem editTabItem = CreateMenuItem(AppStrings.Browser.TabMenuEditTab, (s, e) =>
            {
                EditTab(tab);
            });
            _tabContextMenu.Items.Add(editTabItem);

            _tabContextMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem toggleBarTabItem = CreateMenuItem(AppStrings.Browser.MenuShowBookmarksBar, (s, e) =>
            {
                ToggleBookmarksBar();
            }, Keys.Control | Keys.Shift | Keys.B);
            toggleBarTabItem.Checked = AppServices.Bookmarks.ShowBookmarksBar;
            _tabContextMenu.Items.Add(toggleBarTabItem);

            _tabContextMenu.Show(screenPt);
        }

        internal void ShowTabStripEmptyAreaContextMenu(Point screenPt)
        {
            if (_tabStripContextMenu == null)
            {
                _tabStripContextMenu = new ContextMenuStrip();
                UITheme.ApplyContextMenu(_tabStripContextMenu);
                _tabStripContextMenu.ShowImageMargin = true;
                _tabStripContextMenu.ShowCheckMargin = false;
            }
            _tabStripContextMenu.Items.Clear();

            // 1. New Tab
            ToolStripMenuItem newTabItem = CreateMenuItem(AppStrings.Browser.TabMenuNewTab, (s, e) =>
            {
                AddNewTab(this.IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl);
            }, Keys.Control | Keys.T);
            _tabStripContextMenu.Items.Add(newTabItem);

            // 2. Reopen Closed Tab
            ToolStripMenuItem reopenItem = CreateMenuItem(AppStrings.Browser.TabMenuReopenClosed, (s, e) =>
            {
                ReopenLastClosedTab();
            }, Keys.Control | Keys.Shift | Keys.T);
            reopenItem.Enabled = (_recentlyClosedUrls.Count > 0);
            _tabStripContextMenu.Items.Add(reopenItem);

            _tabStripContextMenu.Items.Add(new ToolStripSeparator());

            // 3. Bookmark All Tabs
            ToolStripMenuItem bookmarkAllItem = CreateMenuItem(AppStrings.Browser.TabMenuBookmarkAll, (s, e) =>
            {
                BookmarkAllTabs();
            }, Keys.Control | Keys.Shift | Keys.D);

            bool hasBookmarkableTab = false;
            if (_tabControl != null && _tabControl.TabPages != null)
            {
                for (int i = 0; i < _tabControl.TabPages.Count; i++)
                {
                    BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                    if (t != null && !string.IsNullOrEmpty(t.CurrentUrl) && InternalPageHelper.CanBookmarkPage(t.CurrentUrl))
                    {
                        hasBookmarkableTab = true;
                        break;
                    }
                }
            }
            bookmarkAllItem.Enabled = hasBookmarkableTab;
            _tabStripContextMenu.Items.Add(bookmarkAllItem);

            _tabStripContextMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem toggleBarEmptyItem = CreateMenuItem(AppStrings.Browser.MenuShowBookmarksBar, (s, e) =>
            {
                ToggleBookmarksBar();
            }, Keys.Control | Keys.Shift | Keys.B);
            toggleBarEmptyItem.Checked = AppServices.Bookmarks.ShowBookmarksBar;
            _tabStripContextMenu.Items.Add(toggleBarEmptyItem);

            _tabStripContextMenu.Show(screenPt);
        }

        private void TabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            HideBlockElementPanel();
            ScreenshotController.CancelActiveSelection();
            if (_tabLoadingOverlay != null && _tabLoadingOverlay.Visible)
            {
                _tabLoadingOverlay.HideOverlay();
            }
            UpdateNavigationState();
            BrowserTab cur = CurrentTab;
            if (cur != null && !cur.IsDisposed)
            {
                if (!cur.IsInternalPage && cur.Browser != null && !cur.Browser.IsDisposed && cur.Browser.IsHandleCreated)
                {
                    if (_topBar != null && !_topBar.IsEditingAddressBox)
                    {
                        try { cur.Browser.Focus(); } catch { }
                    }
                }
            }
            if (_findPanel != null && _findPanel.Visible && !string.IsNullOrEmpty(_findTextBox.Text))
            {
                PerformAutoFind();
            }
        }

        private void UpdateDpiScaling()
        {
            _currentDpiScale = DpiHelper.GetDpiScale(this);

            if (_topBar != null) _topBar.DpiScale = _currentDpiScale;
            if (_bookmarksBar != null) _bookmarksBar.DpiScale = _currentDpiScale;
            if (_topProgressBar != null) _topProgressBar.DpiScale = _currentDpiScale;
            if (_securityCodeBanner != null) _securityCodeBanner.DpiScale = _currentDpiScale;
            if (_bottomBar != null) _bottomBar.DpiScale = _currentDpiScale;
            if (_tabLoadingOverlay != null) _tabLoadingOverlay.DpiScale = _currentDpiScale;
            if (_fullScreenOverlay != null) _fullScreenOverlay.DpiScale = _currentDpiScale;
            if (_nativeStatusStrip != null)
            {
                _nativeStatusStrip.Height = Math.Max(22, (int)(24 * _currentDpiScale));
                _nativeStatusStrip.Padding = new Padding((int)(8 * _currentDpiScale), 0, (int)(8 * _currentDpiScale), 0);
            }
            if (_statusLabel != null) _statusLabel.Font = UITheme.FontDefault;
            if (_statusAdBlockBadge != null) _statusAdBlockBadge.Font = UITheme.FontDefaultBold;
            if (_tabControl != null) _tabControl.DpiScale = _currentDpiScale;

            if (_findPrevBtn != null)
                _findPrevBtn.Image = MaterialIcons.GetDpiIcon("caret-up", _currentDpiScale, 14);
            if (_findNextBtn != null)
                _findNextBtn.Image = MaterialIcons.GetDpiIcon("caret-down", _currentDpiScale, 14);
            if (_findCloseBtn != null)
                _findCloseBtn.Image = MaterialIcons.GetDpiIcon("x", _currentDpiScale, 14);

            UpdateBookmarkStar();
            UpdateUBlockButton();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        private const int WM_DPICHANGED = 0x02E0;
        private const int WM_COPYDATA = 0x004A;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED)
            {
                base.WndProc(ref m);
                UpdateDpiScaling();
                return;
            }
            else if (m.Msg == WM_COPYDATA)
            {
                try
                {
                    COPYDATASTRUCT cds = (COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(COPYDATASTRUCT));
                    if (cds.dwData == (IntPtr)0x5742 && cds.cbData > 0 && cds.lpData != IntPtr.Zero)
                    {
                        byte[] bytes = new byte[cds.cbData];
                        Marshal.Copy(cds.lpData, bytes, 0, cds.cbData);
                        string url = System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim();

                        this.BeginInvoke(new Action(() =>
                        {
                            string targetUrl = string.IsNullOrWhiteSpace(url) ? InternalPageHelper.NewTabUrl : url;
                            try
                            {
                                if (!targetUrl.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) && File.Exists(targetUrl))
                                {
                                    targetUrl = new Uri(Path.GetFullPath(targetUrl)).AbsoluteUri;
                                }
                            }
                            catch { }

                            // Open in a new tab of this window and activate
                            this.AddNewTab(targetUrl);
                            if (this.WindowState == FormWindowState.Minimized)
                            {
                                this.WindowState = FormWindowState.Normal;
                            }
                            this.Activate();
                            this.BringToFront();
                        }));
                        m.Result = (IntPtr)1;
                        return;
                    }
                }
                catch { }
            }

            base.WndProc(ref m);
        }

        private void UpdateNavigationState()
        {
            BrowserTab tab = CurrentTab;
            if (tab != null)
            {
                if (_topBar != null)
                {
                    _topBar.CanGoBack = tab.CanGoBack;
                    _topBar.CanGoForward = tab.CanGoForward;
                    _topBar.IsLoading = tab.IsLoading;
                    _topBar.AddressText = InternalPageHelper.IsStartPage(tab.CurrentUrl) ? string.Empty : tab.CurrentUrl;
                }
                this.Text = TitleHelper.FormatWindowTitle(tab.CurrentTitle, this.IsIncognito);
                if (_topProgressBar != null)
                {
                    if (tab.IsLoading) _topProgressBar.StartLoading();
                    else _topProgressBar.CompleteLoading();
                }
                UpdateBookmarkStar();
                UpdateUBlockButton();
                CheckGoogleSecurityCodeBanner(false);
            }
            else
            {
                if (_topBar != null)
                {
                    _topBar.CanGoBack = false;
                    _topBar.CanGoForward = false;
                    _topBar.IsLoading = false;
                    _topBar.AddressText = string.Empty;
                    _topBar.UpdateTabs();
                }
                this.Text = this.IsIncognito ? AppStrings.Browser.WindowTitleIncognito : AppStrings.Browser.WindowTitle;
                if (_topProgressBar != null) _topProgressBar.CompleteLoading();
                UpdateBookmarkStar();
                UpdateUBlockButton();
                CheckGoogleSecurityCodeBanner(false);
            }
        }

        private void UpdateBookmarkStar()
        {
            BrowserTab tab = CurrentTab;
            bool canBookmark = tab != null && InternalPageHelper.CanBookmarkPage(tab.CurrentUrl);
            if (_topBar != null)
            {
                _topBar.CanBookmark = canBookmark;
                if (canBookmark)
                {
                    bool isBookmarked = AppServices.Bookmarks.IsBookmarked(tab.CurrentUrl);
                    _topBar.IsBookmarked = isBookmarked;
                }
                else
                {
                    _topBar.IsBookmarked = false;
                }
            }
        }

        internal void BookmarkToggleBtn_Click(object sender, EventArgs e)
        {
            BookmarkTab(CurrentTab);
        }

        private void OpenBookmarks()
        {
            NavigateOrOpenInternalTab(InternalPageHelper.BookmarksUrl);
        }

        private void CloseDownloadBubble()
        {
            if (_downloadBubble != null && !_downloadBubble.IsDisposed && _downloadBubble.Visible)
            {
                _downloadBubble.Close();
            }
        }

        public void OpenImportData()
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(OpenImportData));
                return;
            }
            CloseDownloadBubble();
            ImportDataDialog.ShowImportDialog(this);
        }

        public void OpenExportData()
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(OpenExportData));
                return;
            }
            CloseDownloadBubble();
            ExportDataDialog.ShowExportDialog(this);
        }

        private void OpenHistory()
        {
            NavigateOrOpenInternalTab(InternalPageHelper.HistoryUrl);
        }

        public void ShowClearBrowsingDataDialog()
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(ShowClearBrowsingDataDialog));
                return;
            }
            CloseDownloadBubble();
            wb.Browser.ClearBrowsingData.ClearBrowsingDataDialog.ShowClearBrowsingData(this);
        }

        public void ShowProxySettingsDialog()
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(ShowProxySettingsDialog));
                return;
            }
            CloseDownloadBubble();
            wb.Proxy.ProxySettingsDialog.ShowProxySettings(this);
        }

        public void OpenDownloads(DownloadItemModel selectModel = null)
        {
            BrowserTab tab = NavigateOrOpenInternalTab(InternalPageHelper.DownloadsUrl);
            if (tab != null)
            {
                DownloadItemModel m = selectModel ?? _latestDownload;
                if (m != null)
                {
                    tab.SelectDownload(m.Id);
                }
            }
        }

        public BrowserTab NavigateOrOpenInternalTab(string internalUrl)
        {
            if (this.IsDisposed) return null;
            if (this.InvokeRequired)
            {
                return (BrowserTab)this.Invoke(new Func<string, BrowserTab>(NavigateOrOpenInternalTab), internalUrl);
            }

            string normUrl = InternalPageHelper.NormalizeInternalUrl(internalUrl);
            string title = InternalPageHelper.GetInternalTitle(normUrl);
            InternalPageType pageType = InternalPageHelper.GetInternalPageType(normUrl);
            if (!IsIncognito && (pageType == InternalPageType.Incognito || string.Equals(normUrl, InternalPageHelper.IncognitoUrl, StringComparison.OrdinalIgnoreCase)))
            {
                CreateNewIncognitoWindow();
                return null;
            }
            if (pageType == InternalPageType.ClearBrowsingData)
            {
                BrowserTab tab = NavigateOrOpenInternalTab(InternalPageHelper.HistoryUrl);
                ShowClearBrowsingDataDialog();
                return tab;
            }
            if (pageType == InternalPageType.ImportData)
            {
                BrowserTab tab = CurrentTab ?? NavigateOrOpenInternalTab(InternalPageHelper.BookmarksUrl);
                OpenImportData();
                return tab;
            }
            if (pageType == InternalPageType.ExportData)
            {
                BrowserTab tab = CurrentTab ?? NavigateOrOpenInternalTab(InternalPageHelper.BookmarksUrl);
                OpenExportData();
                return tab;
            }
            if (pageType == InternalPageType.Proxy)
            {
                BrowserTab tab = CurrentTab ?? NavigateOrOpenInternalTab(InternalPageHelper.NewTabUrl);
                ShowProxySettingsDialog();
                return tab;
            }
            if (pageType == InternalPageType.NewTab || InternalPageHelper.IsInternalUrl(normUrl) || InternalPageHelper.IsInternalPage(normUrl))
            {
                if (_tabLoadingOverlay != null && _tabLoadingOverlay.Visible)
                {
                    _tabLoadingOverlay.HideOverlay();
                }
            }
            else if (_tabLoadingOverlay != null)
            {
                _tabLoadingOverlay.ShowForInternalPage(title, 200);
            }

            // 1. Check if current tab is already viewing this internal url
            BrowserTab cur = CurrentTab;
            if (cur != null && string.Equals(cur.CurrentUrl, normUrl, StringComparison.OrdinalIgnoreCase))
            {
                return cur;
            }

            // 2. Check if ANY open tab is already viewing this internal url
            if (_tabControl != null)
            {
                for (int i = 0; i < _tabControl.TabPages.Count; i++)
                {
                    BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                    if (t != null && !t.IsDisposed && string.Equals(t.CurrentUrl, normUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        _tabControl.SelectedIndex = i;
                        return t;
                    }
                }
            }

            // 3. If current tab is a blank new tab, navigate it in place
            if (cur != null && !cur.IsDisposed && (string.IsNullOrEmpty(cur.CurrentUrl) || InternalPageHelper.IsStartPage(cur.CurrentUrl)))
            {
                cur.LoadUrl(normUrl);
                return cur;
            }

            // 4. Otherwise, open a new tab with the internal url
            BrowserTab newTab = new BrowserTab(normUrl);
            AdoptTab(newTab);
            return newTab;
        }

        private void Tab_TitleUpdated(object sender, string title)
        {
            if (sender == CurrentTab)
            {
                this.Text = TitleHelper.FormatWindowTitle(title, this.IsIncognito);
                CheckGoogleSecurityCodeBanner();
            }
            if (_topBar != null)
            {
                _topBar.UpdateTabs();
            }
        }

        private void Tab_AddressUpdated(object sender, string address)
        {
            if (sender == CurrentTab)
            {
                if (_topBar != null)
                {
                    _topBar.AddressText = InternalPageHelper.IsStartPage(address) ? string.Empty : address;
                }
                UpdateBookmarkStar();
                CheckGoogleSecurityCodeBanner();
            }
        }

        private void Tab_LoadingStateUpdated(object sender, LoadingStateChangedEventArgs e)
        {
            BrowserTab tab = sender as BrowserTab;
            if (sender == CurrentTab)
            {
                if (_topBar != null)
                {
                    _topBar.CanGoBack = e.CanGoBack;
                    _topBar.CanGoForward = e.CanGoForward;
                    _topBar.IsLoading = e.IsLoading;
                    _topBar.UpdateTabs();
                }
                if (_topProgressBar != null)
                {
                    if (e.IsLoading)
                        _topProgressBar.StartLoading();
                    else
                        _topProgressBar.CompleteLoading();
                }

                if (!e.IsLoading)
                {
                    if (_tabLoadingOverlay != null)
                    {
                        _tabLoadingOverlay.HideOverlay();
                    }
                    SetStatusText(string.Empty);
                    UpdateBookmarkStar();
                    UpdateUBlockButton();
                    CheckGoogleSecurityCodeBanner(false);
                }
            }
            else
            {
                if (_topBar != null)
                {
                    _topBar.UpdateTabs();
                }
            }

            if (!e.IsLoading && tab != null)
            {
                tab.RecordNavigation(tab.CurrentUrl, tab.CurrentTitle);
            }

            // Record to history once page is done loading (suppressed in Incognito)
            if (!IsIncognito && !e.IsLoading && tab != null && !InternalPageHelper.IsStartPage(tab.CurrentUrl) && !InternalPageHelper.IsInternalUrl(tab.CurrentUrl))
            {
                if (!WebErrorPage.IsErrorPage(tab.CurrentUrl) && !WebErrorPage.IsChromiumInternalErrorPage(tab.CurrentUrl))
                {
                    AppServices.History.AddEntry(tab.CurrentTitle, tab.CurrentUrl);
                }
            }
        }

        private void Tab_StatusMessageUpdated(object sender, string status)
        {
            if (sender == CurrentTab)
            {
                // Suppress link status preview on New Tab / startpage
                if (CurrentTab != null && InternalPageHelper.IsStartPage(CurrentTab.CurrentUrl))
                {
                    SetStatusText(string.Empty);
                    return;
                }

                SetStatusText(status);
            }
        }

        private void AddressTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                NavigateFromAddressBar();
            }
        }

        private string ResolveTargetUrl(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            string trimmed = input.Trim();

            if (InternalPageHelper.IsViewSourceUrl(trimmed))
            {
                return InternalPageHelper.NormalizeViewSourceUrl(trimmed);
            }
            if (InternalPageHelper.IsInternalUrl(trimmed))
            {
                return InternalPageHelper.NormalizeInternalUrl(trimmed);
            }
            if (string.Equals(trimmed, "wb://newtab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "wb:newtab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "chrome://newtab", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "about:blank", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "about:newtab", StringComparison.OrdinalIgnoreCase))
            {
                return InternalPageHelper.NewTabUrl;
            }
            if (trimmed.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase))
            {
                return "wb://" + trimmed.Substring("chrome://".Length);
            }
            if (trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                return "wb://" + trimmed.Substring("about:".Length);
            }
            return UrlHelper.ResolveNavigableUrl(trimmed);
        }

        private void NavigateFromAddressBar(string urlOverride = null)
        {
            string input = !string.IsNullOrEmpty(urlOverride) ? urlOverride.Trim() : (_topBar != null ? _topBar.AddressText : string.Empty).Trim();
            if (string.IsNullOrEmpty(input)) return;

            string targetUrl = ResolveTargetUrl(input);

            if (!IsIncognito && (string.Equals(targetUrl, InternalPageHelper.IncognitoUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetUrl, "wb://private", StringComparison.OrdinalIgnoreCase) ||
                InternalPageHelper.GetInternalPageType(targetUrl) == InternalPageType.Incognito))
            {
                CreateNewIncognitoWindow();
                if (_topBar != null)
                {
                    _topBar.ReleaseAddressBarFocus();
                }
                return;
            }

            BrowserTab tab = CurrentTab;
            if (tab != null)
            {
                tab.LoadUrl(targetUrl);
                if (tab.Browser != null && tab.Browser.IsHandleCreated && tab.Browser.Visible)
                {
                    tab.Browser.Focus();
                }
                else
                {
                    tab.Focus();
                }
            }
            if (_topBar != null)
            {
                _topBar.ReleaseAddressBarFocus();
            }
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            BrowserTab tab = CurrentTab;
            if (tab != null)
            {
                if (tab.IsLoading)
                {
                    tab.Stop();
                }
                else
                {
                    bool ignoreCache = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
                    tab.Reload(ignoreCache);
                }
            }
        }

        private void ZoomPage(double delta)
        {
            BrowserTab tab = CurrentTab;
            if (tab != null)
            {
                if (delta > 0)
                {
                    tab.ZoomIn();
                }
                else
                {
                    tab.ZoomOut();
                }
                UpdateZoomDisplay();
            }
        }

        private void UpdateZoomDisplay()
        {
            // Zoom percentage is dynamically shown in the View -> Zoom gear menu
        }

        private void UpdateRamDisplay()
        {
            // RAM consumption is dynamically shown in the Gear menu
        }

        private void ShowFindPanel()
        {
            _findPanel.Visible = true;
            EnsureTopBarsZOrder();
            _findTextBox.Focus();
            _findTextBox.SelectAll();
            if (!string.IsNullOrEmpty(_findTextBox.Text))
            {
                PerformAutoFind();
            }
        }

        private void HideFindPanel()
        {
            if (_findDebounceTimer != null)
            {
                _findDebounceTimer.Stop();
            }
            _findPanel.Visible = false;
            EnsureTopBarsZOrder();
            BrowserTab tab = CurrentTab;
            if (tab != null)
            {
                tab.StopFinding(true);
            }
        }

        private void PerformAutoFind()
        {
            BrowserTab tab = CurrentTab;
            string query = _findTextBox != null ? _findTextBox.Text : null;
            if (tab != null && !string.IsNullOrEmpty(query))
            {
                _lastSearchedText = query;
                tab.Find(query, true, _findMatchCaseChk != null && _findMatchCaseChk.Checked, false);
            }
        }

        private void FindInPage(bool forward)
        {
            BrowserTab tab = CurrentTab;
            string query = _findTextBox != null ? _findTextBox.Text : null;
            if (tab != null && !string.IsNullOrEmpty(query))
            {
                bool isNewQuery = !string.Equals(query, _lastSearchedText, StringComparison.Ordinal);
                _lastSearchedText = query;
                tab.Find(query, forward, _findMatchCaseChk != null && _findMatchCaseChk.Checked, !isNewQuery);
            }
        }

        private void OpenSourceViewer()
        {
            BrowserTab tab = CurrentTab;
            if (tab != null && tab.Browser != null)
            {
                string sourceUrl = tab.CurrentUrl;
                if (!string.IsNullOrEmpty(sourceUrl) && !InternalPageHelper.IsInternalUrl(sourceUrl))
                {
                    if (InternalPageHelper.IsViewSourceUrl(sourceUrl))
                    {
                        AddNewTab(sourceUrl);
                        return;
                    }
                    if (sourceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        sourceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        sourceUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        AddNewTab("view-source:" + sourceUrl);
                        return;
                    }
                }

                tab.GetSourceAsync().ContinueWith(t =>
                {
                    if (this.InvokeRequired)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            string html = (t.IsFaulted || t.Result == null) ? string.Empty : t.Result;
                            string viewerUrl = SourceViewerTemplate.SaveToTempFile(sourceUrl, html);
                            AddNewTab(viewerUrl);
                        }));
                    }
                    else
                    {
                        string html = (t.IsFaulted || t.Result == null) ? string.Empty : t.Result;
                        string viewerUrl = SourceViewerTemplate.SaveToTempFile(sourceUrl, html);
                        AddNewTab(viewerUrl);
                    }
                });
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_downloadBubble != null && !_downloadBubble.IsDisposed && _downloadBubble.Visible)
            {
                Point cursor = Cursor.Position;
                Rectangle bubbleBounds = _downloadBubble.Bounds;
                bubbleBounds.Inflate(10, 10);
                if (!bubbleBounds.Contains(cursor) && !_downloadBubble.ContainsFocus)
                {
                    _downloadBubble.Close();
                }
            }
            if (this.WindowState == FormWindowState.Minimized)
            {
                MemoryUtils.TrimMemory(flushWorkingSet: true);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (this.IsIncognito)
            {
                lock (_incognitoLock)
                {
                    _openIncognitoWindowCount--;
                    if (_openIncognitoWindowCount <= 0)
                    {
                        _openIncognitoWindowCount = 0;
                        if (_sharedIncognitoRequestContext != null && !_sharedIncognitoRequestContext.IsDisposed)
                        {
                            try { _sharedIncognitoRequestContext.Dispose(); } catch { }
                            _sharedIncognitoRequestContext = null;
                        }
                    }
                }
            }
            if (_tabStripContextMenu != null)
            {
                try { _tabStripContextMenu.Dispose(); } catch { }
                _tabStripContextMenu = null;
            }
            base.OnFormClosed(e);
        }

        private void MainBrowserForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.T)
            {
                e.SuppressKeyPress = true;
                ReopenLastClosedTab();
            }
            else if (e.Control && e.Shift && e.KeyCode == Keys.D)
            {
                e.SuppressKeyPress = true;
                BookmarkAllTabs();
            }
            else if (e.Control && e.Shift && e.KeyCode == Keys.N)
            {
                e.SuppressKeyPress = true;
                CreateNewIncognitoWindow();
            }
            else if (e.Control && e.KeyCode == Keys.N)
            {
                e.SuppressKeyPress = true;
                CreateNewWindow();
            }
            else if (e.Control && e.KeyCode == Keys.T)
            {
                e.SuppressKeyPress = true;
                AddNewTab(InternalPageHelper.NewTabUrl);
            }
            else if (e.Control && e.KeyCode == Keys.W)
            {
                e.SuppressKeyPress = true;
                CloseTab(CurrentTab);
            }
            else if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R))
            {
                e.SuppressKeyPress = true;
                BrowserTab tab = CurrentTab;
                if (tab != null) tab.Reload(e.Shift || e.Control);
            }
            else if (e.KeyCode == Keys.F12)
            {
                e.SuppressKeyPress = true;
                BrowserTab tab = CurrentTab;
                if (tab != null) tab.ShowDevTools();
            }
            else if (e.Control && e.KeyCode == Keys.U)
            {
                e.SuppressKeyPress = true;
                OpenSourceViewer();
            }
            else if (e.Control && e.KeyCode == Keys.B)
            {
                e.SuppressKeyPress = true;
                OpenBookmarks();
            }
            else if (e.Control && e.KeyCode == Keys.H)
            {
                e.SuppressKeyPress = true;
                OpenHistory();
            }
            else if (e.Control && e.KeyCode == Keys.J)
            {
                e.SuppressKeyPress = true;
                OpenDownloads();
            }
            else if (e.Control && e.Shift && e.KeyCode == Keys.D)
            {
                e.SuppressKeyPress = true;
                LogcatHelper.ToggleDebugConsole();
            }
            else if (e.Control && e.KeyCode == Keys.D)
            {
                e.SuppressKeyPress = true;
                BookmarkToggleBtn_Click(this, EventArgs.Empty);
            }
            else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add))
            {
                e.SuppressKeyPress = true;
                ZoomPage(0.5);
            }
            else if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract))
            {
                e.SuppressKeyPress = true;
                ZoomPage(-0.5);
            }
            else if (e.Control && (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0))
            {
                e.SuppressKeyPress = true;
                BrowserTab tab = CurrentTab;
                if (tab != null) { tab.ResetZoom(); UpdateZoomDisplay(); }
            }
            else if (e.Control && e.KeyCode == Keys.O)
            {
                e.SuppressKeyPress = true;
                OpenLocalFile();
            }
            else if (e.Control && e.KeyCode == Keys.N)
            {
                e.SuppressKeyPress = true;
                CreateNewWindow();
            }
            else if (e.KeyCode == Keys.F11)
            {
                e.SuppressKeyPress = true;
                ToggleFullScreen();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                HandleEscapeKey();
            }
            else if ((e.Control && e.KeyCode == Keys.L) || (e.Alt && e.KeyCode == Keys.D))
            {
                e.SuppressKeyPress = true;
                if (_topBar != null && _topBar.AddressBox != null)
                {
                    _topBar.AddressBox.Focus();
                    _topBar.AddressBox.SelectAll();
                }
            }
            else if (e.Control && e.KeyCode == Keys.F)
            {
                e.SuppressKeyPress = true;
                ShowFindPanel();
            }
            else if (e.Control && e.Shift && e.KeyCode == Keys.P)
            {
                e.SuppressKeyPress = true;
                PrintPageViaSystemDialog();
            }
            else if (e.Control && e.KeyCode == Keys.P)
            {
                e.SuppressKeyPress = true;
                PrintCurrentPageToPdf();
            }
            else if (e.Alt && e.KeyCode == Keys.Left)
            {
                e.SuppressKeyPress = true;
                BrowserTab tab = CurrentTab;
                if (tab != null && tab.CanGoBack) tab.GoBack();
            }
            else if (e.Alt && e.KeyCode == Keys.Right)
            {
                e.SuppressKeyPress = true;
                BrowserTab tab = CurrentTab;
                if (tab != null && tab.CanGoForward) tab.GoForward();
            }
        }

        private void InitKeyboardHandler()
        {
            _keyboardHandler = new BrowserKeyboardHandler(
                () => PrintCurrentPageToPdf(),
                () => PrintPageViaSystemDialog(),
                () => ShowFindPanel(),
                () => AddNewTab(this.IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl),
                () => CloseTab(CurrentTab),
                () => OpenDownloads(),
                () => OpenHistory(),
                () => OpenBookmarks(),
                () => OpenSourceViewer(),
                () => { BrowserTab tab = CurrentTab; if (tab != null) tab.ShowDevTools(); },
                delta => ZoomPage(delta),
                () => { BrowserTab tab = CurrentTab; if (tab != null) { tab.ResetZoom(); UpdateZoomDisplay(); } },
                () => CreateNewWindow(),
                () => ReopenLastClosedTab(),
                () => ToggleFullScreen(),
                () => _isFullScreen,
                () => SelectNextTab(),
                () => SelectPreviousTab(),
                idx => SelectTabByIndex(idx),
                () => CreateNewIncognitoWindow(),
                () => ScreenshotController.TakeScreenshot(this, ScreenshotMode.VisiblePart, ScreenshotMenuHelper.CurrentDelaySeconds),
                () => ScreenshotController.TakeScreenshot(this, ScreenshotMode.FullPage, ScreenshotMenuHelper.CurrentDelaySeconds),
                () => ScreenshotController.TakeScreenshot(this, ScreenshotMode.SelectedArea, ScreenshotMenuHelper.CurrentDelaySeconds),
                () => ShowClearBrowsingDataDialog(),
                () => BookmarkToggleBtn_Click(this, EventArgs.Empty),
                () => { if (_topBar != null && _topBar.AddressBox != null) { _topBar.AddressBox.Focus(); _topBar.AddressBox.SelectAll(); } },
                hard => { BrowserTab tab = CurrentTab; if (tab != null) tab.Reload(hard); },
                () => { BrowserTab tab = CurrentTab; if (tab != null && tab.CanGoBack) tab.GoBack(); },
                () => { BrowserTab tab = CurrentTab; if (tab != null && tab.CanGoForward) tab.GoForward(); },
                () => HandleEscapeKey(),
                () => _isFullScreen || _isImmersiveActive || _isVideoFullScreen || (_blockElementPanel != null && _blockElementPanel.Visible) || (CurrentTab != null && CurrentTab.IsLoading)
            );
        }

        private void PrintCurrentPageToPdf()
        {
            BrowserTab tab = CurrentTab;
            if (tab == null || tab.Browser == null)
            {
                MessageBox.Show(this, AppStrings.Browser.MsgNoActivePageToPrint, AppStrings.Browser.PrintToPdfTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Title = AppStrings.Browser.SavePageAsPdfTitle;
                sfd.Filter = AppStrings.Browser.PdfFileFilter;
                string title = tab.CurrentTitle ?? AppStrings.Browser.DefaultDocumentTitle;
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    title = title.Replace(c, '_');
                }
                sfd.FileName = title.Trim() + ".pdf";

                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    string pdfPath = sfd.FileName;
                    PdfPrintSettings settings = new PdfPrintSettings
                    {
                        PrintBackground = true,
                        MarginType = CefPdfPrintMarginType.Default
                    };

                    ShowTemporaryNotification(AppStrings.Browser.NotifyExportingPdf, 3000);

                    tab.Browser.PrintToPdfAsync(pdfPath, settings).ContinueWith(t =>
                    {
                        if (this.IsDisposed) return;
                        this.BeginInvoke(new Action(() =>
                        {
                            if (t.IsFaulted || !t.Result)
                            {
                                ShowTemporaryNotification(AppStrings.Browser.NotifyFailedExportPdf, 3000);
                                MessageBox.Show(this,
                                    AppStrings.Browser.MsgCouldNotGeneratePdf,
                                    AppStrings.Browser.PrintToPdfErrorTitle,
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                            }
                            else
                            {
                                ShowTemporaryNotification(AppStrings.Browser.NotifyPdfSavedPrefix + Path.GetFileName(pdfPath), 3500);
                                DialogResult openRes = MessageBox.Show(this,
                                    string.Format(AppStrings.Browser.MsgPdfSavedSuccessFormat, pdfPath),
                                    AppStrings.Browser.PrintToPdfTitle,
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Information);

                                if (openRes == DialogResult.Yes && File.Exists(pdfPath))
                                {
                                    try
                                    {
                                        Process.Start(pdfPath);
                                    }
                                    catch (Exception ex)
                                    {
                                        MessageBox.Show(this, AppStrings.Browser.MsgCouldNotOpenPdfViewerPrefix + ex.Message, AppStrings.Browser.OpenPdfTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    }
                                }
                            }
                        }));
                    });
                }
            }
        }

        private void PrintPageViaSystemDialog()
        {
            BrowserTab tab = CurrentTab;
            if (tab != null && tab.Browser != null)
            {
                tab.Browser.Print();
            }
        }

        public void SelectNextTab()
        {
            if (_tabControl == null || _tabControl.TabPages.Count <= 1) return;
            int next = (_tabControl.SelectedIndex + 1) % _tabControl.TabPages.Count;
            _tabControl.SelectedIndex = next;
        }

        public void SelectPreviousTab()
        {
            if (_tabControl == null || _tabControl.TabPages.Count <= 1) return;
            int prev = (_tabControl.SelectedIndex - 1 + _tabControl.TabPages.Count) % _tabControl.TabPages.Count;
            _tabControl.SelectedIndex = prev;
        }

        public void SelectTabByIndex(int index)
        {
            if (_tabControl == null || _tabControl.TabPages.Count == 0) return;
            if (index == 8) // Ctrl+9 jumps to the last tab in Chromium
            {
                _tabControl.SelectedIndex = _tabControl.TabPages.Count - 1;
            }
            else if (index >= 0 && index < _tabControl.TabPages.Count)
            {
                _tabControl.SelectedIndex = index;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Next))
            {
                SelectNextTab();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Tab) || keyData == (Keys.Control | Keys.Prior))
            {
                SelectPreviousTab();
                return true;
            }
            if ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.Shift) == 0 && (keyData & Keys.Alt) == 0)
            {
                Keys keyCode = keyData & Keys.KeyCode;
                if (keyCode >= Keys.D1 && keyCode <= Keys.D9)
                {
                    SelectTabByIndex((int)(keyCode - Keys.D1));
                    return true;
                }
                if (keyCode >= Keys.NumPad1 && keyCode <= Keys.NumPad9)
                {
                    SelectTabByIndex((int)(keyCode - Keys.NumPad1));
                    return true;
                }
            }
            if (keyData == (Keys.Control | Keys.P))
            {
                PrintCurrentPageToPdf();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.B))
            {
                ToggleBookmarksBar();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.D))
            {
                BookmarkAllTabs();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.P))
            {
                PrintPageViaSystemDialog();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.S))
            {
                ScreenshotController.TakeScreenshot(this, ScreenshotMode.VisiblePart, ScreenshotMenuHelper.CurrentDelaySeconds);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.F))
            {
                ScreenshotController.TakeScreenshot(this, ScreenshotMode.FullPage, ScreenshotMenuHelper.CurrentDelaySeconds);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.A))
            {
                ScreenshotController.TakeScreenshot(this, ScreenshotMode.SelectedArea, ScreenshotMenuHelper.CurrentDelaySeconds);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Delete))
            {
                ShowClearBrowsingDataDialog();
                return true;
            }
            if (keyData == Keys.F5)
            {
                BrowserTab tab = CurrentTab;
                if (tab != null) tab.Reload(false);
                return true;
            }
            if (keyData == (Keys.Control | Keys.F5) || keyData == (Keys.Shift | Keys.F5) || keyData == (Keys.Control | Keys.Shift | Keys.R))
            {
                BrowserTab tab = CurrentTab;
                if (tab != null) tab.Reload(true);
                return true;
            }
            if (keyData == (Keys.Control | Keys.R))
            {
                BrowserTab tab = CurrentTab;
                if (tab != null) tab.Reload(false);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowScreenshotMenu()
        {
            if (_topBar == null) return;
            ContextMenuStrip menu = ScreenshotMenuHelper.CreateScreenshotMenu(this);
            Rectangle btnRect = _topBar.GetScreenshotButtonRect();
            Point screenPoint = _topBar.PointToScreen(new Point(btnRect.Left, btnRect.Bottom + 2));
            menu.Show(screenPoint);
        }

        private void SetAddressBarPlaceholder(TextBox textBox, string placeholder)
        {
            UITheme.SetCueBanner(textBox, placeholder);
        }

        internal static string GetUBlockStatusSuffix(string currentUrl)
        {
            return AppServices.AdBlock.GetStatusSuffix(currentUrl);
        }

        private ContextMenuStrip _uBlockMenu;

        private void ShowUBlockMenu()
        {
            if (_uBlockMenu != null && _uBlockMenu.Visible)
            {
                _uBlockMenu.Close();
                return;
            }

            if (_uBlockMenu == null)
            {
                _uBlockMenu = new ContextMenuStrip();
                UITheme.ApplyContextMenu(_uBlockMenu);
                _uBlockMenu.Opened += (s, e) => { if (_topBar != null) _topBar.IsShieldMenuOpen = true; };
                _uBlockMenu.Closed += (s, e) => { if (_topBar != null) _topBar.IsShieldMenuOpen = false; };
            }

            BuildUBlockContextMenu(_uBlockMenu);

            if (_topBar != null)
            {
                _topBar.IsShieldMenuOpen = true;
                Rectangle shieldRect = _topBar.GetShieldButtonRect();
                Point screenPoint = _topBar.PointToScreen(new Point(shieldRect.Right, shieldRect.Bottom + 2));
                _uBlockMenu.Show(screenPoint.X - _uBlockMenu.PreferredSize.Width, screenPoint.Y);
            }
        }

        private void BuildUBlockContextMenu(ContextMenuStrip menu)
        {
            if (menu == null) return;
            menu.Items.Clear();

            BrowserTab tab = CurrentTab;
            string url = tab != null ? tab.CurrentUrl : null;
            bool isInternal = string.IsNullOrEmpty(url) || InternalPageHelper.IsInternalPage(url);
            string domain = !isInternal ? AdBlockManager.NormalizeDomain(url) : null;
            if (string.IsNullOrEmpty(domain))
            {
                isInternal = true;
                domain = null;
            }
            bool isWhitelisted = !string.IsNullOrEmpty(domain) && AppServices.AdBlock.IsDomainWhitelisted(domain);
            bool isGloballyEnabled = AppServices.AdBlock.IsEnabled;
            int blockedCount = tab != null ? tab.BlockedCount : 0;

            // 1. Status Information
            string statusText;
            if (isInternal)
            {
                statusText = AppStrings.AdBlock.StatusInternalProtected;
            }
            else if (!isGloballyEnabled)
            {
                statusText = AppStrings.AdBlock.StatusDisabledGlobally;
            }
            else if (isWhitelisted)
            {
                statusText = string.Format(AppStrings.AdBlock.StatusPausedForFormat, domain);
            }
            else
            {
                statusText = string.Format(AppStrings.AdBlock.StatusActiveForFormat, domain);
            }

            ToolStripMenuItem statusItem = new ToolStripMenuItem(statusText);
            statusItem.Enabled = false;
            statusItem.Font = new Font(UITheme.FontDefault, FontStyle.Bold);
            statusItem.ForeColor = MaterialColors.AccentBlue;
            menu.Items.Add(statusItem);

            // 2. Site Toggle (only when on a regular website and globally enabled)
            if (!isInternal && isGloballyEnabled)
            {
                string toggleText = isWhitelisted ?
                    AppStrings.AdBlock.MenuResumeSite :
                    AppStrings.AdBlock.MenuPauseSite;
                ToolStripMenuItem toggleSiteItem = new ToolStripMenuItem(toggleText);
                toggleSiteItem.Click += (s, e) =>
                {
                    AppServices.AdBlock.ToggleDomain(domain);
                    UpdateUBlockButton();
                    if (tab != null)
                    {
                        tab.Reload(true);
                    }
                    BuildUBlockContextMenu(menu);
                };
                menu.Items.Add(toggleSiteItem);
            }

            // 2b. Block Element Mode
            if (!isInternal && isGloballyEnabled)
            {
                ToolStripMenuItem blockElementItem = new ToolStripMenuItem(AppStrings.AdBlock.MenuBlockElement);
                blockElementItem.Enabled = !isWhitelisted;
                blockElementItem.Click += (s, e) => ShowBlockElementPanel();
                menu.Items.Add(blockElementItem);
            }

            menu.Items.Add(new ToolStripSeparator());

            // 3. Stats Section
            ToolStripMenuItem pageBlockedItem = new ToolStripMenuItem(string.Format(AppStrings.AdBlock.MenuBlockedOnPageFormat, blockedCount));
            pageBlockedItem.Enabled = false;
            menu.Items.Add(pageBlockedItem);

            string totalBlockedText = string.Format(AppStrings.AdBlock.MenuTotalBlockedFormat, AppServices.AdBlock.TotalBlockedCount);
            ToolStripMenuItem totalBlockedItem = new ToolStripMenuItem(!string.IsNullOrEmpty(totalBlockedText) ? totalBlockedText.Replace("&", "&&") : totalBlockedText);
            totalBlockedItem.Enabled = false;
            menu.Items.Add(totalBlockedItem);

            menu.Items.Add(new ToolStripSeparator());

            // 4. Global uBlock Switch (Auto-refreshes current page when toggled)
            ToolStripMenuItem globalToggleItem = new ToolStripMenuItem(isGloballyEnabled ? AppStrings.AdBlock.MenuGlobalEnabled : AppStrings.AdBlock.MenuGlobalDisabled);
            globalToggleItem.Checked = isGloballyEnabled;
            globalToggleItem.Click += (s, e) =>
            {
                AppServices.AdBlock.IsEnabled = !AppServices.AdBlock.IsEnabled;
                UpdateUBlockButton();
                if (tab != null && !isInternal)
                {
                    tab.Reload(true);
                }
                BuildUBlockContextMenu(menu);
            };
            menu.Items.Add(globalToggleItem);

            menu.Items.Add(new ToolStripSeparator());

            // 5. Actions: Whitelist Manager, Custom Blocked Elements, Filter Subscriptions & Reload
            ToolStripMenuItem whitelistItem = new ToolStripMenuItem(AppStrings.AdBlock.MenuManageWhitelist);
            whitelistItem.Click += (s, e) => OpenAdBlockWhitelist();
            menu.Items.Add(whitelistItem);

            ToolStripMenuItem customFiltersItem = new ToolStripMenuItem(string.Format(AppStrings.AdBlock.MenuManageBlockedElementsFormat, AppServices.AdBlock.CustomFiltersCount));
            customFiltersItem.Click += (s, e) => OpenManageCustomFilters();
            menu.Items.Add(customFiltersItem);

            ToolStripMenuItem subscriptionsItem = new ToolStripMenuItem(string.Format(AppStrings.AdBlock.MenuFilterSubscriptionsFormat, AppServices.AdBlock.TotalRuleCount));
            subscriptionsItem.Click += (s, e) => OpenAdBlockSubscriptions();
            menu.Items.Add(subscriptionsItem);

            ToolStripMenuItem updateRulesItem = new ToolStripMenuItem(AppStrings.AdBlock.MenuUpdateRules);
            updateRulesItem.Click += (s, e) =>
            {
                AppServices.AdBlock.CheckForUpdatesInBackground(true);
                MessageBox.Show(this, AppStrings.AdBlock.MsgUpdatingBackground, AppStrings.AdBlock.BtnTextNormal, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            menu.Items.Add(updateRulesItem);

            if (tab != null && !isInternal)
            {
                ToolStripMenuItem reloadItem = new ToolStripMenuItem(AppStrings.AdBlock.MenuReloadBypass);
                reloadItem.Click += (s, e) => tab.Reload(true);
                menu.Items.Add(reloadItem);
            }

            // --- SponsorBlock for YouTube Section ---
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem sbHeader = new ToolStripMenuItem(AppStrings.SponsorBlock.MenuHeader);
            sbHeader.Font = new Font(UITheme.FontDefault, FontStyle.Bold);
            sbHeader.ForeColor = MaterialColors.AccentBlue;
            sbHeader.Enabled = false;
            menu.Items.Add(sbHeader);

            bool isSbEnabled = AppServices.SponsorBlock.IsEnabled;
            ToolStripMenuItem sbToggleItem = new ToolStripMenuItem(AppStrings.SponsorBlock.MenuToggleEnable);
            sbToggleItem.Checked = isSbEnabled;
            sbToggleItem.Click += (s, e) =>
            {
                AppServices.SponsorBlock.IsEnabled = !AppServices.SponsorBlock.IsEnabled;
                BuildUBlockContextMenu(menu);
            };
            menu.Items.Add(sbToggleItem);

            bool isYt = !string.IsNullOrEmpty(url) && SponsorBlockYTShield.IsYouTubeDomain(url);
            if (isYt)
            {
                string currentVideoId = SponsorBlockManager.ExtractVideoId(url);
                if (!string.IsNullOrEmpty(currentVideoId))
                {
                    bool isVidWhitelisted = AppServices.SponsorBlock.IsWhitelisted(currentVideoId);
                    ToolStripMenuItem sbWhitelistItem = new ToolStripMenuItem(isVidWhitelisted ?
                        AppStrings.SponsorBlock.MenuUnwhitelistVideo :
                        AppStrings.SponsorBlock.MenuWhitelistVideo);
                    sbWhitelistItem.Click += (s, e) =>
                    {
                        AppServices.SponsorBlock.ToggleVideoWhitelist(currentVideoId);
                        if (tab != null) tab.Reload(true);
                        BuildUBlockContextMenu(menu);
                    };
                    menu.Items.Add(sbWhitelistItem);
                }
            }

            ToolStripMenuItem sbManageWhitelistItem = new ToolStripMenuItem(AppStrings.SponsorBlock.MenuManageWhitelist);
            sbManageWhitelistItem.Click += (s, e) => OpenSponsorBlockWhitelist();
            menu.Items.Add(sbManageWhitelistItem);

            ToolStripMenuItem sbSettingsItem = new ToolStripMenuItem(AppStrings.SponsorBlock.MenuSettings);
            sbSettingsItem.Click += (s, e) => OpenSponsorBlockSettings();
            menu.Items.Add(sbSettingsItem);
        }

        private EventHandler _adBlockStatsHandler;
        private EventHandler<string> _adBlockWhitelistHandler;
        private EventHandler _adBlockEnabledHandler;
        private EventHandler _adBlockFilterUpdatedHandler;
        private EventHandler _adBlockCustomFiltersHandler;

        private void HookAdBlockEvents()
        {
            _adBlockStatsHandler = (s, e) => UpdateUBlockButton();
            _adBlockWhitelistHandler = (s, e) => UpdateUBlockButton();
            _adBlockEnabledHandler = (s, e) => UpdateUBlockButton();
            _adBlockFilterUpdatedHandler = (s, e) => UpdateUBlockButton();
            _adBlockCustomFiltersHandler = (s, e) => UpdateUBlockButton();

            AppServices.AdBlock.WhitelistChanged += _adBlockWhitelistHandler;
            AppServices.AdBlock.EnabledChanged += _adBlockEnabledHandler;
            AppServices.AdBlock.StatsUpdated += _adBlockStatsHandler;
            AppServices.AdBlock.FilterListsUpdated += _adBlockFilterUpdatedHandler;
            AppServices.AdBlock.CustomFiltersChanged += _adBlockCustomFiltersHandler;
        }

        private void UnhookAdBlockEvents()
        {
            if (_adBlockWhitelistHandler != null) AppServices.AdBlock.WhitelistChanged -= _adBlockWhitelistHandler;
            if (_adBlockEnabledHandler != null) AppServices.AdBlock.EnabledChanged -= _adBlockEnabledHandler;
            if (_adBlockStatsHandler != null) AppServices.AdBlock.StatsUpdated -= _adBlockStatsHandler;
            if (_adBlockFilterUpdatedHandler != null) AppServices.AdBlock.FilterListsUpdated -= _adBlockFilterUpdatedHandler;
            if (_adBlockCustomFiltersHandler != null) AppServices.AdBlock.CustomFiltersChanged -= _adBlockCustomFiltersHandler;
        }

        private void OpenAdBlockWhitelist()
        {
            AdBlockWhitelistForm form = new AdBlockWhitelistForm();
            form.Show(this);
        }

        private void OpenManageCustomFilters()
        {
            BrowserTab tab = CurrentTab;
            Action reloadAction = (tab != null && !InternalPageHelper.IsInternalPage(tab.CurrentUrl)) ? new Action(() => tab.Reload(true)) : null;
            AdBlockManageFiltersForm form = new AdBlockManageFiltersForm(reloadAction);
            form.Show(this);
        }

        private void OpenAdBlockSubscriptions()
        {
            AdBlockSubscriptionsForm form = new AdBlockSubscriptionsForm();
            form.Show(this);
        }

        private void UpdateUBlockButton()
        {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            try
            {
                if (this.InvokeRequired)
                {
                    try
                    {
                        if (!this.IsDisposed && this.IsHandleCreated)
                        {
                            this.BeginInvoke(new Action(UpdateUBlockButton));
                        }
                    }
                    catch
                    {
                    }
                    return;
                }

                if (this.IsDisposed) return;

                BrowserTab tab = CurrentTab;
                string currentUrl = (tab != null && !tab.IsDisposed) ? tab.CurrentUrl : null;
                bool isInternal = (tab == null || tab.IsDisposed || InternalPageHelper.IsInternalPage(currentUrl));

                if (_topBar != null && !_topBar.IsDisposed)
                {
                    _topBar.IsInternalPage = isInternal;
                    _topBar.IsAdBlockEnabled = AppServices.AdBlock.IsEnabled;
                    _topBar.AdBlockStatusSuffix = AppServices.AdBlock.GetStatusSuffix(currentUrl);
                }

                if (isInternal)
                {
                    SetBlockedCount(0);
                    return;
                }

                if (!AppServices.AdBlock.IsEnabled)
                {
                    SetBlockedCount(0);
                }
                else
                {
                    string domain = AdBlockManager.NormalizeDomain(tab.CurrentUrl);
                    bool isWhitelisted = AppServices.AdBlock.IsDomainWhitelisted(domain);

                    if (isWhitelisted)
                    {
                        SetBlockedCount(0);
                    }
                    else
                    {
                        int blocked = tab.BlockedCount;
                        SetBlockedCount(blocked);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ThemeManager_AccentColorChanged(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(() => ThemeManager_AccentColorChanged(sender, e))); } catch { }
                return;
            }
            if (_topBar != null) _topBar.Invalidate(true);
            if (_tabControl != null)
            {
                foreach (TabPage page in _tabControl.TabPages)
                {
                    BrowserTab tab = page as BrowserTab;
                    if (tab != null && (InternalPageHelper.IsStartPage(tab.CurrentUrl) || InternalPageHelper.IsInternalUrl(tab.CurrentUrl)))
                    {
                        tab.SetFavicon(InternalPageHelper.GetInternalFavicon(tab.CurrentUrl, tab.IsIncognito));
                    }
                }
                _tabControl.Invalidate();
            }
            if (_statusAdBlockBadge != null) _statusAdBlockBadge.ForeColor = ThemeManager.CurrentAccentColor;
            if (_nativeStatusStrip != null) _nativeStatusStrip.Invalidate();
            if (_topProgressBar != null) _topProgressBar.Invalidate();
            if (_blockElementTitleLbl != null) _blockElementTitleLbl.ForeColor = ThemeManager.CurrentAccentColor;
            if (_blockElementPanel != null) _blockElementPanel.Invalidate();
            if (_fullScreenOverlay != null && _fullScreenOverlay.Visible) _fullScreenOverlay.Invalidate();
            this.Invalidate(true);
        }

        private ToolStripMenuItem BuildAccentColorMenu()
        {
            ToolStripMenuItem themeMenu = new ToolStripMenuItem(AppStrings.Browser.MenuAccentColor);
            UITheme.ApplyMenuItem(themeMenu);
            ToolStripDropDownMenu dropDown = themeMenu.DropDown as ToolStripDropDownMenu;
            if (dropDown != null)
            {
                dropDown.ShowCheckMargin = true;
                dropDown.ShowImageMargin = true;
                dropDown.Renderer = new MaterialToolStripRenderer();
            }

            // 1. Random Bright Color Option
            ToolStripMenuItem randomItem = new ToolStripMenuItem(AppStrings.Browser.MenuRandomColor, null, (s, e) =>
            {
                ThemeManager.SetRandomColor();
            });
            UITheme.ApplyMenuItem(randomItem);
            randomItem.Checked = ThemeManager.IsRandomMode;
            themeMenu.DropDownItems.Add(randomItem);

            themeMenu.DropDownItems.Add(new ToolStripSeparator());

            // 2. Preset Bright Spectrum of Colors
            foreach (AccentColorPreset preset in ThemeManager.Presets)
            {
                AccentColorPreset currentPreset = preset;
                Bitmap swatch = ThemeManager.CreateColorSwatch(currentPreset.Color, 16);
                ToolStripMenuItem presetItem = new ToolStripMenuItem(currentPreset.Name, swatch, (s, e) =>
                {
                    ThemeManager.SetPreset(currentPreset.Name);
                });
                UITheme.ApplyMenuItem(presetItem);
                if (!ThemeManager.IsRandomMode && string.Equals(ThemeManager.CurrentPresetName, currentPreset.Name, StringComparison.OrdinalIgnoreCase))
                {
                    presetItem.Checked = true;
                }
                themeMenu.DropDownItems.Add(presetItem);
            }

            themeMenu.DropDownItems.Add(new ToolStripSeparator());

            // 3. Custom Color Option
            ToolStripMenuItem customItem = new ToolStripMenuItem(AppStrings.Browser.MenuCustomColor, null, (s, e) =>
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.Color = ThemeManager.CurrentAccentColor;
                    cd.FullOpen = true;
                    if (cd.ShowDialog(this) == DialogResult.OK)
                    {
                        ThemeManager.SetAccentColor(cd.Color, AppStrings.Theme.PresetCustom);
                    }
                }
            });
            UITheme.ApplyMenuItem(customItem);
            if (!ThemeManager.IsRandomMode && string.Equals(ThemeManager.CurrentPresetName, AppStrings.Theme.PresetCustom, StringComparison.OrdinalIgnoreCase))
            {
                customItem.Checked = true;
            }
            themeMenu.DropDownItems.Add(customItem);

            return themeMenu;
        }

        private ToolStripMenuItem BuildToolbarButtonsMenu()
        {
            ToolStripMenuItem toolbarMenu = new ToolStripMenuItem(AppStrings.Browser.MenuToolbarButtons);
            UITheme.ApplyMenuItem(toolbarMenu);
            ToolStripDropDownMenu dropDown = toolbarMenu.DropDown as ToolStripDropDownMenu;
            if (dropDown != null)
            {
                dropDown.ShowCheckMargin = true;
                dropDown.ShowImageMargin = true;
                dropDown.Renderer = new MaterialToolStripRenderer();
            }

            if (_topBar == null) return toolbarMenu;

            for (int i = 0; i < WbTopBar.DefaultToolbarButtons.Length; i++)
            {
                string id = WbTopBar.DefaultToolbarButtons[i];
                string title = WbTopBar.GetButtonDisplayName(id);
                string iconName = WbTopBar.GetButtonIconName(id);
                bool isVis = _topBar.IsButtonVisible(id);

                ToolStripMenuItem item = new ToolStripMenuItem(title);
                item.Checked = isVis;
                item.Image = MaterialIcons.GetIcon(iconName, 16, isVis ? MaterialColors.AccentBlue : MaterialColors.TextSecondary);
                UITheme.ApplyMenuItem(item);

                string buttonKey = id;
                item.Click += (s, e) =>
                {
                    _topBar.SetButtonVisible(buttonKey, !_topBar.IsButtonVisible(buttonKey));
                };

                toolbarMenu.DropDownItems.Add(item);
            }

            toolbarMenu.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem resetItem = new ToolStripMenuItem(AppStrings.Browser.MenuResetToolbar);
            resetItem.Image = MaterialIcons.GetIcon("arrows-clockwise", 16, MaterialColors.TextSecondary);
            UITheme.ApplyMenuItem(resetItem);
            resetItem.Click += (s, e) =>
            {
                _topBar.ResetToolbarToDefault();
            };
            toolbarMenu.DropDownItems.Add(resetItem);

            return toolbarMenu;
        }

        private void CheckGoogleSecurityCodeBanner(bool debounce = true)
        {
            if (this.IsDisposed || _securityCodeBanner == null || _securityCodeBanner.IsDisposed) return;

            if (!debounce)
            {
                if (_securityBannerDebounceTimer != null)
                {
                    _securityBannerDebounceTimer.Stop();
                }
                ExecuteCheckGoogleSecurityCodeBanner();
                return;
            }

            if (_securityBannerDebounceTimer != null)
            {
                _securityBannerDebounceTimer.Stop();
                _securityBannerDebounceTimer.Start();
            }
            else
            {
                ExecuteCheckGoogleSecurityCodeBanner();
            }
        }

        private void ExecuteCheckGoogleSecurityCodeBanner()
        {
            if (this.IsDisposed || _securityCodeBanner == null || _securityCodeBanner.IsDisposed) return;

            if (!ExperimentalManager.EnableGoogleSecurityCodeBanner)
            {
                SetSecurityBannerVisible(false);
                return;
            }

            BrowserTab tab = CurrentTab;
            if (tab == null || tab.IsDisposed)
            {
                SetSecurityBannerVisible(false);
                return;
            }

            string url = tab.CurrentUrl;
            string title = tab.CurrentTitle;

            bool isChallenge = GoogleSecurityCodeBanner.IsGoogleSecurityCodeChallenge(url, title);
            if (!isChallenge)
            {
                // If actively loading and still on Google Accounts domain, avoid flash-hiding during intermediate redirect steps
                if (_securityCodeBanner.Visible && tab.IsLoading && !string.IsNullOrEmpty(url) &&
                    (url.IndexOf("accounts.google.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     url.IndexOf("myaccount.google.com", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return;
                }

                SetSecurityBannerVisible(false);
                if (!string.IsNullOrEmpty(_securityBannerDismissedUrl) && !string.Equals(_securityBannerDismissedUrl, url, StringComparison.OrdinalIgnoreCase))
                {
                    _securityBannerDismissedUrl = null;
                }
                return;
            }

            if (!string.IsNullOrEmpty(_securityBannerDismissedUrl) && string.Equals(_securityBannerDismissedUrl, url, StringComparison.OrdinalIgnoreCase))
            {
                SetSecurityBannerVisible(false);
                return;
            }

            SetSecurityBannerVisible(true);
        }

        private void SetSecurityBannerVisible(bool visible)
        {
            if (_securityCodeBanner == null || _securityCodeBanner.IsDisposed) return;
            if (_securityCodeBanner.Visible == visible) return;

            this.SuspendLayout();
            try
            {
                _securityCodeBanner.Visible = visible;
                EnsureTopBarsZOrder();
            }
            finally
            {
                this.ResumeLayout(true);
            }
        }

        private ToolStripMenuItem BuildExperimentalMenu()
        {
            ToolStripMenuItem experimentalMenu = new ToolStripMenuItem(AppStrings.Browser.MenuExperimental);
            UITheme.ApplyMenuItem(experimentalMenu);
            ToolStripDropDownMenu dropDown = experimentalMenu.DropDown as ToolStripDropDownMenu;
            if (dropDown != null)
            {
                dropDown.ShowCheckMargin = false;
                dropDown.ShowImageMargin = true;
                dropDown.Renderer = new MaterialToolStripRenderer();
            }

            ToolStripMenuItem stardustItem = new ToolStripMenuItem(AppStrings.Browser.MenuStardustProgressBar, null, (s, e) =>
            {
                ExperimentalManager.EnableProgressBarStardust = !ExperimentalManager.EnableProgressBarStardust;
                if (_topProgressBar != null)
                {
                    _topProgressBar.EnableStardust = ExperimentalManager.EnableProgressBarStardust;
                }
            });
            UITheme.ApplyMenuItem(stardustItem);
            stardustItem.Checked = ExperimentalManager.EnableProgressBarStardust;
            experimentalMenu.DropDownItems.Add(stardustItem);

            ToolStripMenuItem bannerItem = new ToolStripMenuItem(AppStrings.Browser.MenuGoogleSecurityCodeBanner, null, (s, e) =>
            {
                ExperimentalManager.EnableGoogleSecurityCodeBanner = !ExperimentalManager.EnableGoogleSecurityCodeBanner;
                CheckGoogleSecurityCodeBanner();
            });
            UITheme.ApplyMenuItem(bannerItem);
            bannerItem.Checked = ExperimentalManager.EnableGoogleSecurityCodeBanner;
            experimentalMenu.DropDownItems.Add(bannerItem);

            ToolStripMenuItem immersiveItem = new ToolStripMenuItem("Immersive Mode", null, (s, e) =>
            {
                ToggleImmersiveMode();
            });
            UITheme.ApplyMenuItem(immersiveItem);
            immersiveItem.Checked = _immersivePlayerMode;
            immersiveItem.ToolTipText = "When a video plays fullscreen, keep wb windowed and fill the window viewport instead of taking over the monitor.";
            experimentalMenu.DropDownItems.Add(immersiveItem);

            experimentalMenu.DropDownOpening += (s, e) =>
            {
                stardustItem.Checked = ExperimentalManager.EnableProgressBarStardust;
                bannerItem.Checked = ExperimentalManager.EnableGoogleSecurityCodeBanner;
                immersiveItem.Checked = _immersivePlayerMode;
            };

            return experimentalMenu;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 1. Check for active downloads and give user the chance to cancel exit
            try
            {
                int activeDownloads = AppServices.Downloads.GetActiveCount();
                if (activeDownloads > 0)
                {
                    DialogResult confirm = MessageBox.Show(this,
                        string.Format(AppStrings.Downloads.ExitDownloadsInProgressMessage, activeDownloads),
                        AppStrings.Downloads.ExitDownloadsInProgressTitle,
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning);

                    if (confirm != DialogResult.OK)
                    {
                        e.Cancel = true;
                        return;
                    }

                    AppServices.Downloads.PauseAllForExit();
                }
            }
            catch { }

            // 2. Allow attached FormClosing listeners to run while controls and tabs are intact
            base.OnFormClosing(e);
            if (e.Cancel)
            {
                return;
            }

            // 3. Tear down overlays, handlers, timers and tabs safely with individual exception isolation
            try
            {
                if (_downloadBubble != null && !_downloadBubble.IsDisposed)
                {
                    try { _downloadBubble.Dispose(); } catch { }
                    _downloadBubble = null;
                }
            }
            catch { }

            try { ThemeManager.AccentColorChanged -= ThemeManager_AccentColorChanged; } catch { }
            try { UnhookAdBlockEvents(); } catch { }
            try { UnhookDownloadManager(); } catch { }
            try { UnhookProxyManager(); } catch { }
            try { UnhookSponsorBlockManager(); } catch { }
            try
            {
                if (_experimentalConfigChangedHandler != null)
                {
                    ExperimentalManager.ConfigChanged -= _experimentalConfigChangedHandler;
                    _experimentalConfigChangedHandler = null;
                }
            }
            catch { }

            try
            {
                if (_findDebounceTimer != null)
                {
                    _findDebounceTimer.Stop();
                    _findDebounceTimer.Dispose();
                    _findDebounceTimer = null;
                }
            }
            catch { }

            try
            {
                if (_statusNotificationTimer != null)
                {
                    _statusNotificationTimer.Stop();
                    _statusNotificationTimer.Dispose();
                    _statusNotificationTimer = null;
                }
            }
            catch { }

            try
            {
                if (_securityBannerDebounceTimer != null)
                {
                    _securityBannerDebounceTimer.Stop();
                    _securityBannerDebounceTimer.Dispose();
                    _securityBannerDebounceTimer = null;
                }
            }
            catch { }

            try
            {
                if (_bottomBar != null && !_bottomBar.IsDisposed)
                {
                    try { _bottomBar.Dispose(); } catch { }
                    _bottomBar = null;
                }
            }
            catch { }

            try
            {
                if (_tabLoadingOverlay != null && !_tabLoadingOverlay.IsDisposed)
                {
                    try { _tabLoadingOverlay.Dispose(); } catch { }
                    _tabLoadingOverlay = null;
                }
            }
            catch { }

            try
            {
                if (_fullScreenOverlay != null && !_fullScreenOverlay.IsDisposed)
                {
                    try { _fullScreenOverlay.Dispose(); } catch { }
                    _fullScreenOverlay = null;
                }
            }
            catch { }

            try
            {
                if (_tabControl != null)
                {
                    for (int i = _tabControl.TabPages.Count - 1; i >= 0; i--)
                    {
                        BrowserTab t = _tabControl.TabPages[i] as BrowserTab;
                        if (t != null)
                        {
                            try { t.Dispose(); } catch { }
                        }
                    }
                }
            }
            catch { }

            try
            {
                AppServices.AdBlock.FlushStats();
            }
            catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_CLIPCHILDREN: Prevents the form from painting background over child controls (Browser, TabControl, ToolStrips)
                cp.Style |= 0x02000000;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Suppress background painting to eliminate flicker behind docked controls
        }
    }
}
