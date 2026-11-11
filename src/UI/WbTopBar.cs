using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using wb.Browser;
using wb.Browser.Suggestions;
using wb.Core;

namespace wb.UI
{
    /// <summary>
    /// Vertical alignment and bottom offset modes for the tab strip relative to the top bar bottom border.
    /// </summary>
    public enum TabOffsetMode
    {
        Centered = 0,
        SeatedFlush = 1,
        GroundedTaller = 2,
        GroundedFlush = 3,
        Clearance1Px = 4
    }

    /// <summary>
    /// Modern unified single-row top navigation and tab bar for wb browser
    /// (circular navigation buttons, OneBox address bar with inline search/refresh,
    /// integrated tab strip on the same row, tab overflow management and utility icons on the far right).
    /// Styled in dark mode (#202124 / #292A2D) with crisp SVG vector icons.
    /// </summary>
    public class WbTopBar : Control
    {
        private float _dpiScale = 1.0f;
        private bool _canGoBack = false;
        private bool _canGoForward = false;
        private bool _isLoading = false;
        private Timer _spinnerTimer;
        private float _spinnerAngle = 0f;
        private bool _isBookmarked = false;
        private TabOffsetMode _tabOffsetMode = TabOffsetMode.GroundedTaller;

        public TabOffsetMode CurrentTabOffsetMode
        {
            get { return _tabOffsetMode; }
            set
            {
                if (_tabOffsetMode != value)
                {
                    _tabOffsetMode = value;
                    UpdateTabs();
                    this.Invalidate();
                }
            }
        }

        // Interactive child controls
        private TextBox _addressTextBox;
        private SuggestionDropdownPopup _suggestionPopup;
        private ContextMenuStrip _addressContextMenu;
        private ToolStripMenuItem _cutMenuItem;
        private ToolStripMenuItem _copyMenuItem;
        private ToolStripMenuItem _pasteMenuItem;
        private ToolStripMenuItem _pasteAndGoMenuItem;
        private ToolStripMenuItem _deleteMenuItem;
        private ToolStripMenuItem _selectAllMenuItem;
        private ChromeTabControl _boundTabControl;
        private ToolTip _toolTip;
        private string _activeToolTipText = null;

        private void SetActiveToolTip(string tip)
        {
            if (_toolTip == null) return;
            if (string.Equals(_activeToolTipText, tip, StringComparison.Ordinal)) return;

            _activeToolTipText = tip;
            _toolTip.SetToolTip(this, tip);
        }
        private bool _needSelectAllOnMouseUp = false;
        private ContextMenuStrip _overflowMenu;
        private bool _isGearMenuOpen = false;
        private bool _isShieldMenuOpen = false;
        private bool _isAdBlockEnabled = true;
        private string _adBlockStatusSuffix = AppStrings.Browser.ShieldDefaultStatusSuffix;
        private bool? _isInternalPageExplicit = null;

        public bool IsGearMenuOpen
        {
            get { return _isGearMenuOpen; }
            set
            {
                if (_isGearMenuOpen != value)
                {
                    _isGearMenuOpen = value;
                    this.Invalidate();
                }
            }
        }

        public bool IsShieldMenuOpen
        {
            get { return _isShieldMenuOpen; }
            set
            {
                if (_isShieldMenuOpen != value)
                {
                    _isShieldMenuOpen = value;
                    this.Invalidate(GetShieldButtonRect());
                }
            }
        }

        public bool IsAdBlockEnabled
        {
            get { return _isAdBlockEnabled; }
            set
            {
                if (_isAdBlockEnabled != value)
                {
                    _isAdBlockEnabled = value;
                    this.Invalidate(GetShieldButtonRect());
                }
            }
        }

        public string AdBlockStatusSuffix
        {
            get { return _adBlockStatusSuffix; }
            set
            {
                if (_adBlockStatusSuffix != value)
                {
                    _adBlockStatusSuffix = value;
                    this.Invalidate(GetShieldButtonRect());
                }
            }
        }

        public bool IsInternalPage
        {
            get
            {
                if (_isInternalPageExplicit.HasValue)
                {
                    return _isInternalPageExplicit.Value;
                }
                if (_boundTabControl != null)
                {
                    ITabFaviconProvider tab = _boundTabControl.SelectedTab as ITabFaviconProvider;
                    if (tab != null)
                    {
                        return InternalPageHelper.IsInternalPage(tab.CurrentUrl);
                    }
                }
                return false;
            }
            set
            {
                if (!_isInternalPageExplicit.HasValue || _isInternalPageExplicit.Value != value)
                {
                    _isInternalPageExplicit = value;
                    if (_hoveredBtn == ButtonId.Shield && _toolTip != null)
                    {
                        string tip = (_blockedCount > 0 && !value)
                            ? string.Format(AppStrings.Browser.TipShieldBlockedFormat, _blockedCount)
                            : AppStrings.Browser.TipShieldPrefix + (_adBlockStatusSuffix ?? AppStrings.Browser.ShieldDefaultStatusSuffix);
                        SetActiveToolTip(tip);
                    }
                    this.Invalidate(GetShieldButtonRect());
                }
            }
        }

        private int _blockedCount = 0;

        public int BlockedCount
        {
            get { return _blockedCount; }
            set
            {
                int val = Math.Max(0, value);
                if (_blockedCount != val)
                {
                    _blockedCount = val;
                    if (_hoveredBtn == ButtonId.Shield && _toolTip != null)
                    {
                        string tip = (_blockedCount > 0 && !IsInternalPage)
                            ? string.Format(AppStrings.Browser.TipShieldBlockedFormat, _blockedCount)
                            : AppStrings.Browser.TipShieldPrefix + (_adBlockStatusSuffix ?? AppStrings.Browser.ShieldDefaultStatusSuffix);
                        SetActiveToolTip(tip);
                    }
                    this.Invalidate();
                }
            }
        }

        private int _activeDownloadCount = 0;

        public int ActiveDownloadCount
        {
            get { return _activeDownloadCount; }
            set
            {
                int val = Math.Max(0, value);
                if (_activeDownloadCount != val)
                {
                    _activeDownloadCount = val;
                    if (_hoveredBtn == ButtonId.Downloads && _toolTip != null)
                    {
                        string tip = _activeDownloadCount > 0
                            ? string.Format(AppStrings.Browser.TipDownloadsActiveFormat, _activeDownloadCount)
                            : AppStrings.Browser.TipDownloads;
                        SetActiveToolTip(tip);
                    }
                    Rectangle rect = GetDownloadsButtonRect();
                    if (!rect.IsEmpty)
                    {
                        rect.Inflate(4, 4);
                        this.Invalidate(rect);
                    }
                    else
                    {
                        this.Invalidate();
                    }
                }
            }
        }

        private bool _isIncognito = false;

        public bool IsIncognito
        {
            get { return _isIncognito; }
            set
            {
                if (_isIncognito != value)
                {
                    _isIncognito = value;
                    if (_suggestionPopup != null)
                    {
                        _suggestionPopup.IsIncognito = value;
                    }
                    this.Invalidate();
                }
            }
        }

        public event EventHandler IncognitoBadgeClicked;

        // Hover & Press states
        private enum ButtonId
        {
            None,
            Back,
            Forward,
            Go,
            Search,
            Refresh,
            NewTab,
            Overflow,
            Home,
            Star,
            Bookmarks,
            History,
            Downloads,
            Shield,
            Screenshot,
            Gear,
            IncognitoBadge
        }

        private ButtonId _hoveredBtn = ButtonId.None;
        private ButtonId _pressedBtn = ButtonId.None;
        private int _hoveredTabIndex = -1;
        private int _hoveredCloseIndex = -1;
        private int _hoveredAudioIndex = -1;
        private int _pressedTabIndex = -1;
        private int _pressedCloseIndex = -1;

        // Tab Drag & Move state
        private int _dragTabIndex = -1;
        private Point _dragStartPt = Point.Empty;
        private Point _dragCurrentPt = Point.Empty;
        private bool _isTabDragging = false;
        private int _dropTargetIndex = -1;
        private bool _dropTargetAfter = false;
        private bool _isTearOffPending = false;
        private bool _dropHighlight = false;

        // Toolbar Button Drag & Drop and Visibility state
        public static readonly string[] DefaultToolbarButtons = new string[]
        {
            "Home",
            "Star",
            "Bookmarks",
            "History",
            "Downloads",
            "Shield",
            "Screenshot"
        };

        private List<string> _toolbarButtons = new List<string>(DefaultToolbarButtons);
        private Dictionary<string, bool> _toolbarVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private bool _isToolbarDragging = false;
        private string _dragToolbarButtonId = null;
        private Point _toolbarDragStartPt = Point.Empty;
        private Point _toolbarDragCurrentPt = Point.Empty;
        private int _toolbarDropInsertIndex = -1;

        // Tab Overflow state
        private int _firstVisibleTab = 0;
        private int _visibleTabCount = 0;
        private bool _hasOverflow = false;

        // Events
        public event EventHandler BackClicked;
        public event EventHandler ForwardClicked;
        public event EventHandler BackLongPressed;
        public event EventHandler ForwardLongPressed;
        public event EventHandler RefreshClicked;
        public event EventHandler HomeClicked;
        public event EventHandler StarClicked;
        public event EventHandler BookmarksClicked;
        public event EventHandler HistoryClicked;
        public event EventHandler DownloadsClicked;
        public event EventHandler ShieldClicked;
        public event EventHandler ScreenshotClicked;
        public event EventHandler GearClicked;

        private Timer _longPressTimer;
        private ButtonId _longPressBtn = ButtonId.None;
        private bool _longPressFired = false;

        public void TriggerBackLongPress()
        {
            if (_canGoBack && BackLongPressed != null) BackLongPressed(this, EventArgs.Empty);
        }

        public void TriggerForwardLongPress()
        {
            if (_canGoForward && ForwardLongPressed != null) ForwardLongPressed(this, EventArgs.Empty);
        }
        public event EventHandler ToolbarChanged;
        public event EventHandler NewTabClicked;
        public event EventHandler OverflowClicked;
        public event EventHandler<int> TabSelected;
        public event EventHandler<int> TabCloseClicked;
        public event EventHandler<string> NavigateRequested;

        public class TabMoveEventArgs : EventArgs
        {
            public int FromIndex { get; private set; }
            public int ToIndex { get; private set; }
            public TabMoveEventArgs(int from, int to) { FromIndex = from; ToIndex = to; }
        }

        public class TabTearOffEventArgs : EventArgs
        {
            public int TabIndex { get; private set; }
            public Point ScreenPosition { get; private set; }
            public TabTearOffEventArgs(int index, Point pt) { TabIndex = index; ScreenPosition = pt; }
        }

        public class TabDroppedOntoWindowEventArgs : EventArgs
        {
            public int TabIndex { get; private set; }
            public Form TargetWindow { get; private set; }
            public Point ScreenPosition { get; private set; }
            public int TargetDropIndex { get; private set; }
            public TabDroppedOntoWindowEventArgs(int index, Form target, Point pt, int targetDropIndex = -1)
            {
                TabIndex = index;
                TargetWindow = target;
                ScreenPosition = pt;
                TargetDropIndex = targetDropIndex;
            }
        }

        public event EventHandler<TabMoveEventArgs> TabMoved;
        public event EventHandler<TabTearOffEventArgs> TabTornOff;
        public event EventHandler<TabDroppedOntoWindowEventArgs> TabDroppedOntoWindow;
        public event EventHandler<TabDroppedOnBookmarksBarEventArgs> TabDroppedOnBookmarksBar;
        public event EventHandler<TabRightClickEventArgs> TabRightClicked;
        public event EventHandler<Point> TabStripEmptyAreaRightClicked;
        public event EventHandler<int> TabAudioClicked;
        public event EventHandler<UrlDroppedEventArgs> UrlDroppedAsNewTab;

        public class UrlDroppedEventArgs : EventArgs
        {
            public string Url { get; private set; }
            public int TargetTabIndex { get; private set; }
            public UrlDroppedEventArgs(string url, int targetTabIndex = -1)
            {
                Url = url;
                TargetTabIndex = targetTabIndex;
            }
        }

        public class TabRightClickEventArgs : EventArgs
        {
            public int TabIndex { get; private set; }
            public Point ScreenPosition { get; private set; }
            public TabRightClickEventArgs(int index, Point pt)
            {
                TabIndex = index;
                ScreenPosition = pt;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        public WbTopBar()
        {
            this.SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            this.DoubleBuffered = true;
            this.Dock = DockStyle.Top;
            this.Height = 38;
            this.BackColor = MaterialColors.NavBarBackground;
            this.AllowDrop = true;

            _toolTip = new ToolTip();
            _toolTip.InitialDelay = 350;
            _toolTip.ReshowDelay = 100;
            LoadToolbarConfig();

            _longPressTimer = new Timer();
            _longPressTimer.Interval = 400;
            _longPressTimer.Tick += LongPressTimer_Tick;

            // Address TextBox
            _addressTextBox = new TextBox();
            _addressTextBox.BorderStyle = BorderStyle.None;
            _addressTextBox.BackColor = MaterialColors.AddressBarBackground;
            _addressTextBox.ForeColor = MaterialColors.TextPrimary;
            _addressTextBox.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            _addressTextBox.AllowDrop = true;
            _addressTextBox.DragEnter += AddressTextBox_DragEnter;
            _addressTextBox.DragOver += AddressTextBox_DragOver;
            _addressTextBox.DragDrop += AddressTextBox_DragDrop;
            _suggestionPopup = new SuggestionDropdownPopup();
            _suggestionPopup.Attach(
                _addressTextBox,
                () => GetAddressBoxRect(),
                this,
                _isIncognito,
                (target, newTab) =>
                {
                    ReleaseAddressBarFocus();
                    if (NavigateRequested != null)
                    {
                        NavigateRequested(this, target);
                    }
                });
            _addressTextBox.KeyDown += AddressTextBox_KeyDown;
            _addressTextBox.TextChanged += (s, e) =>
            {
                UpdateAddressBoxPosition();
                this.Invalidate(GetAddressBoxRect());
            };

            _addressTextBox.GotFocus += (s, e) =>
            {
                UpdateAddressBoxPosition();
                this.Invalidate(GetAddressBoxRect());
                if (MouseButtons == MouseButtons.None)
                {
                    // Focused via keyboard shortcut (Ctrl+L, Alt+D, Tab): select all
                    _addressTextBox.SelectAll();
                }
                else
                {
                    // Mouse click from outside: prepare to select all on mouse release if user didn't drag
                    _needSelectAllOnMouseUp = true;
                }
            };

            _addressTextBox.MouseUp += (s, e) =>
            {
                if (_needSelectAllOnMouseUp)
                {
                    _needSelectAllOnMouseUp = false;
                    // Only select all if the user did a simple click without dragging a partial selection
                    if (_addressTextBox.SelectionLength == 0)
                    {
                        _addressTextBox.SelectAll();
                    }
                }
            };

            _addressTextBox.LostFocus += (s, e) =>
            {
                _needSelectAllOnMouseUp = false;
                _addressTextBox.SelectionLength = 0;
                UpdateAddressBoxPosition();
                this.Invalidate(GetAddressBoxRect());
            };

            this.Controls.Add(_addressTextBox);
            UITheme.SetCueBanner(_addressTextBox, AppStrings.Browser.SearchPlaceholder);
            _addressContextMenu = CreateAddressBarContextMenu();
            _addressTextBox.ContextMenuStrip = _addressContextMenu;
            UpdateAddressBoxPosition();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateAddressBoxPosition();
            UpdateTabs();
            if (_suggestionPopup != null)
            {
                _suggestionPopup.UpdatePosition();
            }
            this.Invalidate();
        }

        private void UpdateAddressBoxPosition()
        {
            if (_addressTextBox == null) return;
            Rectangle addr = GetAddressBoxRect();
            int leftPad = (int)(28 * _dpiScale);
            int rightPad = IsEditingAddressBox ? (int)(54 * _dpiScale) : (int)(28 * _dpiScale);
            int x = addr.Left + leftPad;
            int w = Math.Max(10, addr.Width - leftPad - rightPad);
            int h = _addressTextBox.PreferredHeight;
            int y = addr.Top + (addr.Height - h) / 2;
            _addressTextBox.SetBounds(x, y, w, h);
        }

        public float DpiScale
        {
            get { return _dpiScale; }
            set
            {
                if (Math.Abs(_dpiScale - value) > 0.01f)
                {
                    _dpiScale = value;
                    this.Height = Math.Max(36, (int)(38 * _dpiScale));
                    if (_addressTextBox != null)
                    {
                        _addressTextBox.Font = new Font("Segoe UI", Math.Max(9F, 9.5F * _dpiScale), FontStyle.Regular);
                    }
                    UpdateAddressBoxPosition();
                    this.Invalidate();
                }
            }
        }

        public bool CanGoBack
        {
            get { return _canGoBack; }
            set
            {
                if (_canGoBack != value)
                {
                    _canGoBack = value;
                    this.Invalidate(GetBackButtonRect());
                }
            }
        }

        public bool CanGoForward
        {
            get { return _canGoForward; }
            set
            {
                if (_canGoForward != value)
                {
                    _canGoForward = value;
                    this.Invalidate(GetForwardButtonRect());
                }
            }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    CheckSpinnerAnimationState();
                    this.Invalidate(GetRefreshButtonRect());
                    if (_boundTabControl != null && _boundTabControl.SelectedIndex >= 0)
                    {
                        Rectangle r = GetTabRect(_boundTabControl.SelectedIndex);
                        if (!r.IsEmpty) this.Invalidate(r);
                    }
                }
            }
        }

        public bool IsSpinnerAnimating
        {
            get { return _spinnerTimer != null && _spinnerTimer.Enabled; }
        }

        public float SpinnerAngle
        {
            get { return _spinnerAngle; }
        }

        public bool IsBookmarked
        {
            get { return _isBookmarked; }
            set
            {
                if (_isBookmarked != value)
                {
                    _isBookmarked = value;
                    this.Invalidate(GetStarButtonRect());
                }
            }
        }

        private bool _canBookmark = true;
        public bool CanBookmark
        {
            get { return _canBookmark; }
            set
            {
                if (_canBookmark != value)
                {
                    _canBookmark = value;
                    this.Invalidate(GetStarButtonRect());
                }
            }
        }

        public string AddressText
        {
            get { return _addressTextBox != null ? _addressTextBox.Text : string.Empty; }
            set
            {
                if (_addressTextBox != null && _addressTextBox.Text != value)
                {
                    if (_suggestionPopup != null)
                    {
                        _suggestionPopup.SuppressSuggestions();
                    }
                    _addressTextBox.Text = value ?? string.Empty;
                    if (_suggestionPopup != null)
                    {
                        _suggestionPopup.ResumeSuggestions();
                    }
                }
                this.Invalidate(GetRefreshButtonRect());
            }
        }

        public TextBox AddressBox
        {
            get { return _addressTextBox; }
        }

        public bool IsAddressBoxTypingOrEmpty
        {
            get
            {
                return (_addressTextBox == null) ||
                       _addressTextBox.Focused ||
                       string.IsNullOrWhiteSpace(_addressTextBox.Text);
            }
        }

        public string GetAddressActionIconName()
        {
            if (_isLoading)
            {
                return "x";
            }
            return "arrow-clockwise";
        }

        private void HookTabEvents(Control control)
        {
            IBrowserTabState tab = control as IBrowserTabState;
            if (tab != null)
            {
                tab.AudioStateChanged += Tab_AudioOrPinChanged;
                tab.PinStateChanged += Tab_AudioOrPinChanged;
                tab.MediaCaptureStateChanged += Tab_AudioOrPinChanged;
                tab.LoadingStateChanged += Tab_AudioOrPinChanged;
            }
        }

        private void UnhookTabEvents(Control control)
        {
            IBrowserTabState tab = control as IBrowserTabState;
            if (tab != null)
            {
                tab.AudioStateChanged -= Tab_AudioOrPinChanged;
                tab.PinStateChanged -= Tab_AudioOrPinChanged;
                tab.MediaCaptureStateChanged -= Tab_AudioOrPinChanged;
                tab.LoadingStateChanged -= Tab_AudioOrPinChanged;
            }
        }

        private void Tab_AudioOrPinChanged(object sender, EventArgs e)
        {
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(UpdateTabs));
                }
                else
                {
                    UpdateTabs();
                }
            }
            catch { }
        }

        public void BindTabControl(ChromeTabControl tc)
        {
            if (_boundTabControl != null)
            {
                foreach (Control c in _boundTabControl.TabPages)
                {
                    UnhookTabEvents(c);
                }
                _boundTabControl.ControlAdded -= BoundTabControl_Added;
                _boundTabControl.ControlRemoved -= BoundTabControl_Removed;
                _boundTabControl.SelectedIndexChanged -= BoundTabControl_SelectedIndexChanged;
            }
            _boundTabControl = tc;
            if (_boundTabControl != null)
            {
                foreach (Control c in _boundTabControl.TabPages)
                {
                    HookTabEvents(c);
                }
                _boundTabControl.ControlAdded += BoundTabControl_Added;
                _boundTabControl.ControlRemoved += BoundTabControl_Removed;
                _boundTabControl.SelectedIndexChanged += BoundTabControl_SelectedIndexChanged;
            }
            this.Invalidate();
        }

        public bool HasOverflow
        {
            get
            {
                UpdateTabVisibility();
                return _hasOverflow;
            }
        }

        public int VisibleTabCount
        {
            get
            {
                UpdateTabVisibility();
                return _visibleTabCount;
            }
        }

        public int FirstVisibleTab
        {
            get
            {
                UpdateTabVisibility();
                return _firstVisibleTab;
            }
        }

        public void UpdateTabs()
        {
            UpdateTabVisibility();
            if (_boundTabControl != null && _boundTabControl.SelectedIndex >= 0)
            {
                EnsureTabVisible(_boundTabControl.SelectedIndex);
            }
            CheckSpinnerAnimationState();
            this.Invalidate();
        }

        public bool DropHighlight
        {
            get { return _dropHighlight; }
            set
            {
                if (_dropHighlight != value)
                {
                    _dropHighlight = value;
                    this.Invalidate();
                }
            }
        }

        private void BoundTabControl_Added(object sender, ControlEventArgs e)
        {
            HookTabEvents(e.Control);
            UpdateTabs();
        }

        private void BoundTabControl_Removed(object sender, ControlEventArgs e)
        {
            UnhookTabEvents(e.Control);
            UpdateTabs();
        }

        private void BoundTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            _isInternalPageExplicit = null;
            UpdateTabs();
            this.Invalidate(GetShieldButtonRect());
        }

        public void ReleaseAddressBarFocus()
        {
            if (_suggestionPopup != null)
            {
                _suggestionPopup.HidePopup();
            }
            if (_addressTextBox != null)
            {
                _addressTextBox.SelectionLength = 0;
            }
            if (_boundTabControl != null && _boundTabControl.SelectedTab != null)
            {
                Control sel = _boundTabControl.SelectedTab;
                bool childFocused = false;
                if (sel.HasChildren && sel.Controls.Count > 0)
                {
                    for (int i = 0; i < sel.Controls.Count; i++)
                    {
                        Control child = sel.Controls[i];
                        if (child != null && child.Visible && child.CanFocus)
                        {
                            child.Focus();
                            childFocused = true;
                            break;
                        }
                    }
                }
                if (!childFocused)
                {
                    if (sel.Visible && sel.CanFocus)
                    {
                        sel.Focus();
                    }
                    else
                    {
                        this.Focus();
                    }
                }
            }
            else
            {
                this.Focus();
            }
        }

        private void AddressTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || e.SuppressKeyPress) return;

            if (_suggestionPopup != null && _suggestionPopup.HandleKeyDown(e))
            {
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (_suggestionPopup != null)
                {
                    _suggestionPopup.SuppressSuggestions();
                }
                ReleaseAddressBarFocus();
                if (NavigateRequested != null)
                {
                    NavigateRequested(this, _addressTextBox.Text.Trim());
                }
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                ReleaseAddressBarFocus();
            }
        }

        private void AddressTextBox_DragEnter(object sender, DragEventArgs e)
        {
            if (WebDragDropHelper.HasUrlData(e.Data))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void AddressTextBox_DragOver(object sender, DragEventArgs e)
        {
            if (WebDragDropHelper.HasUrlData(e.Data))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void AddressTextBox_DragDrop(object sender, DragEventArgs e)
        {
            string url = WebDragDropHelper.ExtractUrlFromDataObject(e.Data);
            if (!string.IsNullOrEmpty(url))
            {
                this.AddressText = url;
                if (NavigateRequested != null)
                {
                    NavigateRequested(this, url);
                }
            }
        }

        public ContextMenuStrip AddressContextMenu
        {
            get
            {
                if (_addressContextMenu == null)
                {
                    _addressContextMenu = CreateAddressBarContextMenu();
                }
                return _addressContextMenu;
            }
        }

        public ContextMenuStrip CreateAddressBarContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(menu);
            menu.ShowImageMargin = false;

            _cutMenuItem = new ToolStripMenuItem(AppStrings.Browser.AddressMenuCut, null, (s, e) =>
            {
                if (_addressTextBox != null)
                {
                    _addressTextBox.Cut();
                }
            });
            _cutMenuItem.ShortcutKeyDisplayString = "Ctrl+X";

            _copyMenuItem = new ToolStripMenuItem(AppStrings.Browser.AddressMenuCopy, null, (s, e) =>
            {
                if (_addressTextBox != null)
                {
                    _addressTextBox.Copy();
                }
            });
            _copyMenuItem.ShortcutKeyDisplayString = "Ctrl+C";

            _pasteMenuItem = new ToolStripMenuItem(AppStrings.Browser.AddressMenuPaste, null, (s, e) =>
            {
                if (_addressTextBox != null)
                {
                    _addressTextBox.Paste();
                }
            });
            _pasteMenuItem.ShortcutKeyDisplayString = "Ctrl+V";

            _pasteAndGoMenuItem = new ToolStripMenuItem(AppStrings.Browser.AddressMenuPasteAndGo, null, (s, e) =>
            {
                PasteAndGo();
            });

            _deleteMenuItem = new ToolStripMenuItem(AppStrings.Browser.AddressMenuDelete, null, (s, e) =>
            {
                if (_addressTextBox != null)
                {
                    _addressTextBox.SelectedText = string.Empty;
                }
            });
            _deleteMenuItem.ShortcutKeyDisplayString = "Del";

            _selectAllMenuItem = new ToolStripMenuItem(AppStrings.Browser.AddressMenuSelectAll, null, (s, e) =>
            {
                if (_addressTextBox != null)
                {
                    _addressTextBox.SelectAll();
                }
            });
            _selectAllMenuItem.ShortcutKeyDisplayString = "Ctrl+A";

            menu.Items.Add(_cutMenuItem);
            menu.Items.Add(_copyMenuItem);
            menu.Items.Add(_pasteMenuItem);
            menu.Items.Add(_pasteAndGoMenuItem);
            menu.Items.Add(_deleteMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_selectAllMenuItem);

            menu.Opening += AddressContextMenu_Opening;

            return menu;
        }

        private void AddressContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_addressTextBox == null || _addressTextBox.IsDisposed)
            {
                e.Cancel = true;
                return;
            }

            if (_addressTextBox.SelectionLength == 0 && _addressTextBox.TextLength > 0)
            {
                _addressTextBox.SelectAll();
            }

            bool hasSelection = _addressTextBox.SelectionLength > 0;
            bool hasText = _addressTextBox.TextLength > 0;

            string clip = string.Empty;
            try
            {
                clip = ClipboardHelper.GetText();
            }
            catch { }

            bool hasClip = !string.IsNullOrEmpty(clip);
            bool hasClipNonEmpty = hasClip && !string.IsNullOrWhiteSpace(clip);

            if (_cutMenuItem != null) _cutMenuItem.Enabled = hasSelection;
            if (_copyMenuItem != null) _copyMenuItem.Enabled = hasSelection;
            if (_pasteMenuItem != null) _pasteMenuItem.Enabled = hasClip;
            if (_pasteAndGoMenuItem != null) _pasteAndGoMenuItem.Enabled = hasClipNonEmpty;
            if (_deleteMenuItem != null) _deleteMenuItem.Enabled = hasSelection;
            if (_selectAllMenuItem != null) _selectAllMenuItem.Enabled = hasText;
        }

        public void ShowAddressBarContextMenu(Point screenPt)
        {
            ContextMenuStrip menu = this.AddressContextMenu;
            if (menu != null)
            {
                menu.Show(screenPt);
            }
        }

        public void PasteAndGo()
        {
            string clip = ClipboardHelper.GetText();
            if (!string.IsNullOrEmpty(clip))
            {
                PasteAndGo(clip);
            }
        }

        public void PasteAndGo(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string trimmed = text.Trim();
            if (string.IsNullOrEmpty(trimmed)) return;

            _needSelectAllOnMouseUp = false;
            if (_addressTextBox != null)
            {
                _addressTextBox.Text = trimmed;
            }
            ReleaseAddressBarFocus();
            if (NavigateRequested != null)
            {
                NavigateRequested(this, trimmed);
            }
        }

        #region Geometry & Hit Testing

        private int GetAddressBarWidth()
        {
            // Adaptive width: approximately 32% of window, bounded between 260px and 440px
            int baseW = (int)(this.Width * 0.32f);
            int minW = (int)(240 * _dpiScale);
            int maxW = (int)(440 * _dpiScale);
            return Math.Max(minW, Math.Min(maxW, baseW));
        }

        public Rectangle GetBackButtonRect()
        {
            int size = (int)(30 * _dpiScale);
            int x = (int)(7 * _dpiScale);
            int y = (this.Height - size) / 2;
            return new Rectangle(x, y, size, size);
        }

        public Rectangle GetForwardButtonRect()
        {
            int size = (int)(26 * _dpiScale);
            int x = GetBackButtonRect().Right + (int)(4 * _dpiScale);
            int y = (this.Height - size) / 2;
            return new Rectangle(x, y, size, size);
        }

        public Rectangle GetAddressBoxRect()
        {
            int fwdRight = GetForwardButtonRect().Right;
            int x = fwdRight + (int)(8 * _dpiScale);
            int h = Math.Max(26, (int)(28 * _dpiScale));
            int y = (this.Height - h) / 2;
            int w = GetAddressBarWidth();
            return new Rectangle(x, y, w, h);
        }

        public bool IsEditingAddressBox
        {
            get
            {
                return _addressTextBox != null &&
                       _addressTextBox.Focused &&
                       !string.IsNullOrWhiteSpace(_addressTextBox.Text);
            }
        }

        public Rectangle GetGoButtonRect()
        {
            if (!IsEditingAddressBox) return Rectangle.Empty;

            Rectangle refRect = GetRefreshButtonRect();
            int size = Math.Max(18, (int)(22 * _dpiScale));
            int spacing = (int)(2 * _dpiScale);
            int x = refRect.Left - size - spacing;
            int y = refRect.Top;
            return new Rectangle(x, y, size, size);
        }

        public Rectangle GetSearchButtonRect()
        {
            return Rectangle.Empty;
        }

        public Rectangle GetRefreshButtonRect()
        {
            Rectangle addr = GetAddressBoxRect();
            int size = Math.Max(18, (int)(22 * _dpiScale));
            int x = addr.Right - size - (int)(3 * _dpiScale);
            int y = addr.Top + (addr.Height - size) / 2;
            return new Rectangle(x, y, size, size);
        }

        public Rectangle GetRefreshButtonHitRect()
        {
            Rectangle refRect = GetRefreshButtonRect();
            if (refRect.IsEmpty) return Rectangle.Empty;
            Rectangle addr = GetAddressBoxRect();
            int right = addr.Right;
            int left = refRect.Left - (int)(2 * _dpiScale);
            return new Rectangle(left, addr.Top, Math.Max(refRect.Width, right - left), addr.Height);
        }

        private int GetShieldButtonWidth()
        {
            return (int)(28 * _dpiScale);
        }

        #region Toolbar Buttons Configuration & Geometry

        public void LoadToolbarConfig()
        {
            try
            {
                ToolbarConfigModel model = StorageHelper.LoadObject<ToolbarConfigModel>("toolbar_config.json");
                if (model != null && model.ButtonOrder != null && model.ButtonOrder.Count > 0)
                {
                    List<string> valid = new List<string>();
                    for (int i = 0; i < model.ButtonOrder.Count; i++)
                    {
                        string b = model.ButtonOrder[i];
                        if (IsValidToolbarButton(b) && !ContainsIgnoreCase(valid, b))
                        {
                            valid.Add(b);
                        }
                    }
                    for (int i = 0; i < DefaultToolbarButtons.Length; i++)
                    {
                        string def = DefaultToolbarButtons[i];
                        if (!ContainsIgnoreCase(valid, def))
                        {
                            valid.Add(def);
                        }
                    }
                    _toolbarButtons = valid;

                    if (model.Visibility != null)
                    {
                        _toolbarVisibility = new Dictionary<string, bool>(model.Visibility, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch { }

            for (int i = 0; i < DefaultToolbarButtons.Length; i++)
            {
                string def = DefaultToolbarButtons[i];
                if (!_toolbarVisibility.ContainsKey(def))
                {
                    _toolbarVisibility[def] = true;
                }
            }
        }

        public void SaveToolbarConfig()
        {
            try
            {
                ToolbarConfigModel model = new ToolbarConfigModel
                {
                    ButtonOrder = new List<string>(_toolbarButtons),
                    Visibility = new Dictionary<string, bool>(_toolbarVisibility, StringComparer.OrdinalIgnoreCase)
                };
                StorageHelper.SaveObject("toolbar_config.json", model);
            }
            catch { }
        }

        private static bool IsValidToolbarButton(string id)
        {
            for (int i = 0; i < DefaultToolbarButtons.Length; i++)
            {
                if (string.Equals(DefaultToolbarButtons[i], id, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool ContainsIgnoreCase(List<string> list, string item)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], item, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public bool IsButtonVisible(string id)
        {
            bool visible;
            if (_toolbarVisibility != null && _toolbarVisibility.TryGetValue(id, out visible)) return visible;
            return true;
        }

        public void SetButtonVisible(string id, bool visible)
        {
            if (string.IsNullOrEmpty(id)) return;
            _toolbarVisibility[id] = visible;
            SaveToolbarConfig();
            this.Invalidate();
            if (ToolbarChanged != null) ToolbarChanged(this, EventArgs.Empty);
        }

        public void ResetToolbarToDefault()
        {
            _toolbarButtons = new List<string>(DefaultToolbarButtons);
            _toolbarVisibility.Clear();
            for (int i = 0; i < DefaultToolbarButtons.Length; i++)
            {
                _toolbarVisibility[DefaultToolbarButtons[i]] = true;
            }
            SaveToolbarConfig();
            this.Invalidate();
            if (ToolbarChanged != null) ToolbarChanged(this, EventArgs.Empty);
        }

        public List<string> GetToolbarButtons()
        {
            return new List<string>(_toolbarButtons);
        }

        public void SetToolbarButtons(List<string> buttons)
        {
            if (buttons != null && buttons.Count > 0)
            {
                _toolbarButtons = new List<string>(buttons);
                SaveToolbarConfig();
                this.Invalidate();
                if (ToolbarChanged != null) ToolbarChanged(this, EventArgs.Empty);
            }
        }

        public List<string> GetVisibleToolbarButtons()
        {
            List<string> list = new List<string>();
            if (_toolbarButtons != null)
            {
                for (int i = 0; i < _toolbarButtons.Count; i++)
                {
                    string id = _toolbarButtons[i];
                    if (IsButtonVisible(id))
                    {
                        list.Add(id);
                    }
                }
            }
            return list;
        }

        public Dictionary<string, Rectangle> GetToolbarButtonRectangles()
        {
            Dictionary<string, Rectangle> rects = new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);
            int baseSize = (int)(28 * _dpiScale);
            int spacing = (int)(6 * _dpiScale);
            int y = (this.Height - baseSize) / 2;

            List<string> visible = GetVisibleToolbarButtons();
            int rightBoundary = (_isIncognito && !GetIncognitoBadgeRect().IsEmpty) ? GetIncognitoBadgeRect().Left : GetGearButtonRect().Left;
            int curRight = rightBoundary - spacing;

            for (int i = visible.Count - 1; i >= 0; i--)
            {
                string id = visible[i];
                int w = string.Equals(id, "Shield", StringComparison.OrdinalIgnoreCase) ? GetShieldButtonWidth() : baseSize;
                int x = curRight - w;
                rects[id] = new Rectangle(x, y, w, baseSize);
                curRight = x - spacing;
            }
            return rects;
        }

        public Rectangle GetToolbarButtonRect(string id)
        {
            var rects = GetToolbarButtonRectangles();
            Rectangle r;
            if (rects.TryGetValue(id, out r)) return r;

            int baseSize = (int)(28 * _dpiScale);
            int spacing = (int)(6 * _dpiScale);
            int y = (this.Height - baseSize) / 2;
            int w = string.Equals(id, "Shield", StringComparison.OrdinalIgnoreCase) ? GetShieldButtonWidth() : baseSize;
            int rightBoundary = GetGearButtonRect().Left;
            int curRight = rightBoundary - spacing;
            return new Rectangle(curRight - w, y, w, baseSize);
        }

        public Rectangle GetHomeButtonRect() { return GetToolbarButtonRect("Home"); }
        public Rectangle GetStarButtonRect() { return GetToolbarButtonRect("Star"); }
        public Rectangle GetBookmarksButtonRect() { return GetToolbarButtonRect("Bookmarks"); }
        public Rectangle GetHistoryButtonRect() { return GetToolbarButtonRect("History"); }
        public Rectangle GetDownloadsButtonRect() { return GetToolbarButtonRect("Downloads"); }
        public Rectangle GetShieldButtonRect() { return GetToolbarButtonRect("Shield"); }
        public Rectangle GetScreenshotButtonRect() { return GetToolbarButtonRect("Screenshot"); }

        public Rectangle GetIncognitoBadgeRect()
        {
            return Rectangle.Empty;
        }

        public Rectangle GetGearButtonRect()
        {
            int size = (int)(28 * _dpiScale);
            int x = this.Width - size - (int)(6 * _dpiScale);
            int y = (this.Height - size) / 2;
            return new Rectangle(x, y, size, size);
        }

        public int GetToolbarLeftLimit()
        {
            var rects = GetToolbarButtonRectangles();
            int minLeft = GetGearButtonRect().Left;
            foreach (var r in rects.Values)
            {
                if (r.Left < minLeft) minLeft = r.Left;
            }
            return minLeft;
        }

        public static string GetButtonDisplayName(string key)
        {
            if (string.Equals(key, "Home", StringComparison.OrdinalIgnoreCase)) return AppStrings.Browser.ToolbarBtnHome;
            if (string.Equals(key, "Star", StringComparison.OrdinalIgnoreCase)) return AppStrings.Browser.ToolbarBtnStar;
            if (string.Equals(key, "Bookmarks", StringComparison.OrdinalIgnoreCase)) return AppStrings.Browser.ToolbarBtnBookmarks;
            if (string.Equals(key, "History", StringComparison.OrdinalIgnoreCase)) return AppStrings.Browser.ToolbarBtnHistory;
            if (string.Equals(key, "Downloads", StringComparison.OrdinalIgnoreCase)) return AppStrings.Browser.ToolbarBtnDownloads;
            if (string.Equals(key, "Shield", StringComparison.OrdinalIgnoreCase)) return AppStrings.Browser.ToolbarBtnShield;
            if (string.Equals(key, "Screenshot", StringComparison.OrdinalIgnoreCase)) return "Screenshot";
            return key;
        }

        public static string GetButtonIconName(string key)
        {
            if (string.Equals(key, "Home", StringComparison.OrdinalIgnoreCase)) return "house";
            if (string.Equals(key, "Star", StringComparison.OrdinalIgnoreCase)) return "star";
            if (string.Equals(key, "Bookmarks", StringComparison.OrdinalIgnoreCase)) return "bookmarks";
            if (string.Equals(key, "History", StringComparison.OrdinalIgnoreCase)) return "clock-counter-clockwise";
            if (string.Equals(key, "Downloads", StringComparison.OrdinalIgnoreCase)) return "download-simple";
            if (string.Equals(key, "Shield", StringComparison.OrdinalIgnoreCase)) return "shield-fill";
            if (string.Equals(key, "Screenshot", StringComparison.OrdinalIgnoreCase)) return "camera";
            return "gear";
        }

        private static ButtonId ToolbarIdToButtonId(string id)
        {
            if (string.Equals(id, "Home", StringComparison.OrdinalIgnoreCase)) return ButtonId.Home;
            if (string.Equals(id, "Star", StringComparison.OrdinalIgnoreCase)) return ButtonId.Star;
            if (string.Equals(id, "Bookmarks", StringComparison.OrdinalIgnoreCase)) return ButtonId.Bookmarks;
            if (string.Equals(id, "History", StringComparison.OrdinalIgnoreCase)) return ButtonId.History;
            if (string.Equals(id, "Downloads", StringComparison.OrdinalIgnoreCase)) return ButtonId.Downloads;
            if (string.Equals(id, "Shield", StringComparison.OrdinalIgnoreCase)) return ButtonId.Shield;
            if (string.Equals(id, "Screenshot", StringComparison.OrdinalIgnoreCase)) return ButtonId.Screenshot;
            return ButtonId.None;
        }

        private static string ButtonIdToToolbarId(ButtonId btn)
        {
            switch (btn)
            {
                case ButtonId.Home: return "Home";
                case ButtonId.Star: return "Star";
                case ButtonId.Bookmarks: return "Bookmarks";
                case ButtonId.History: return "History";
                case ButtonId.Downloads: return "Downloads";
                case ButtonId.Shield: return "Shield";
                case ButtonId.Screenshot: return "Screenshot";
                default: return null;
            }
        }

        private void ComputeToolbarDropTarget(int mouseX)
        {
            List<string> visible = GetVisibleToolbarButtons();
            if (visible.Count == 0)
            {
                _toolbarDropInsertIndex = 0;
                return;
            }

            var rects = GetToolbarButtonRectangles();

            for (int i = 0; i < visible.Count; i++)
            {
                string id = visible[i];
                Rectangle r;
                if (!rects.TryGetValue(id, out r)) continue;

                if (mouseX < r.Left + r.Width / 2)
                {
                    int idxInAll = _toolbarButtons.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                    _toolbarDropInsertIndex = (idxInAll >= 0) ? idxInAll : 0;
                    return;
                }
            }

            string lastVisibleId = visible[visible.Count - 1];
            int lastIdxInAll = _toolbarButtons.FindIndex(x => string.Equals(x, lastVisibleId, StringComparison.OrdinalIgnoreCase));
            _toolbarDropInsertIndex = (lastIdxInAll >= 0) ? lastIdxInAll + 1 : _toolbarButtons.Count;
        }

        private int GetToolbarDropCueX()
        {
            var rects = GetToolbarButtonRectangles();
            List<string> visible = GetVisibleToolbarButtons();
            int spacing = (int)(6 * _dpiScale);

            if (visible.Count == 0)
            {
                return GetGearButtonRect().Left - spacing / 2;
            }

            for (int i = 0; i < visible.Count; i++)
            {
                string id = visible[i];
                int idxInAll = _toolbarButtons.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                if (idxInAll >= _toolbarDropInsertIndex)
                {
                    Rectangle r;
                    if (rects.TryGetValue(id, out r))
                    {
                        return r.Left - spacing / 2;
                    }
                }
            }

            string lastId = visible[visible.Count - 1];
            Rectangle lastRect;
            if (rects.TryGetValue(lastId, out lastRect))
            {
                return lastRect.Right + spacing / 2;
            }

            return GetGearButtonRect().Left - spacing / 2;
        }

        private void CommitToolbarReorder()
        {
            if (string.IsNullOrEmpty(_dragToolbarButtonId)) return;
            int oldIdx = _toolbarButtons.FindIndex(x => string.Equals(x, _dragToolbarButtonId, StringComparison.OrdinalIgnoreCase));
            if (oldIdx < 0) return;

            int newIdx = _toolbarDropInsertIndex;
            if (newIdx < 0) return;

            if (newIdx > oldIdx)
            {
                newIdx--;
            }
            newIdx = Math.Max(0, Math.Min(_toolbarButtons.Count - 1, newIdx));

            if (newIdx != oldIdx)
            {
                string id = _toolbarButtons[oldIdx];
                _toolbarButtons.RemoveAt(oldIdx);
                _toolbarButtons.Insert(newIdx, id);
                SaveToolbarConfig();
                this.Invalidate();
                if (ToolbarChanged != null) ToolbarChanged(this, EventArgs.Empty);
            }
        }

        public ContextMenuStrip CreateToolbarContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new MaterialToolStripRenderer();
            menu.BackColor = MaterialColors.NavBarBackground;
            menu.ForeColor = MaterialColors.TextPrimary;
            menu.ShowImageMargin = true;
            menu.ShowCheckMargin = true;

            for (int i = 0; i < DefaultToolbarButtons.Length; i++)
            {
                string id = DefaultToolbarButtons[i];
                string title = GetButtonDisplayName(id);
                string iconName = GetButtonIconName(id);
                bool isVis = IsButtonVisible(id);

                ToolStripMenuItem item = new ToolStripMenuItem(title);
                item.Checked = isVis;
                item.CheckOnClick = false;
                item.Image = MaterialIcons.GetIcon(iconName, 16, isVis ? MaterialColors.AccentBlue : MaterialColors.TextSecondary);

                string buttonKey = id;
                item.Click += (s, e) =>
                {
                    SetButtonVisible(buttonKey, !IsButtonVisible(buttonKey));
                };

                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem resetItem = new ToolStripMenuItem(AppStrings.Browser.MenuResetToolbar);
            resetItem.Image = MaterialIcons.GetIcon("arrows-clockwise", 16, MaterialColors.TextSecondary);
            resetItem.Click += (s, e) =>
            {
                ResetToolbarToDefault();
            };
            menu.Items.Add(resetItem);

            return menu;
        }

        public void ShowToolbarContextMenu(Point clientPt, string clickedButtonId = null)
        {
            ContextMenuStrip menu = CreateToolbarContextMenu();
            if (menu != null)
            {
                Point screenPt = this.PointToScreen(clientPt);
                menu.Show(screenPt);
            }
        }

        #endregion

        public Rectangle GetTabStripRect()
        {
            int x = GetAddressBoxRect().Right + (int)(8 * _dpiScale);
            int rightLimit = GetToolbarLeftLimit() - (int)(8 * _dpiScale);
            int w = Math.Max(0, rightLimit - x);
            int h;
            int y;

            switch (_tabOffsetMode)
            {
                case TabOffsetMode.SeatedFlush:
                    // Height preserved (30px), shifted down so bottom touches the separator line
                    h = Math.Max(28, (int)(30 * _dpiScale));
                    y = (this.Height - (int)(1 * _dpiScale)) - h;
                    break;

                case TabOffsetMode.GroundedFlush:
                    // Option B flush with bottom separator line (y=4, bottom at y=37, 0px gap)
                    y = Math.Max(3, (int)(4 * _dpiScale));
                    h = (this.Height - (int)(1 * _dpiScale)) - y;
                    break;

                case TabOffsetMode.GroundedTaller:
                default:
                    // Option B with 1px clearance: top aligned with address bar margin (y=4), bottom has 1px clearance above separator line (bottom at y=36)
                    y = Math.Max(3, (int)(4 * _dpiScale));
                    h = (this.Height - (int)(2 * _dpiScale)) - y;
                    break;

                case TabOffsetMode.Clearance1Px:
                    // Subtle 1px clearance above bottom border (top at y=5, bottom at y=36)
                    y = Math.Max(4, (int)(5 * _dpiScale));
                    h = (this.Height - (int)(2 * _dpiScale)) - y;
                    break;

                case TabOffsetMode.Centered:
                    // Legacy vertically centered (3px gap below)
                    h = Math.Max(28, (int)(30 * _dpiScale));
                    y = (this.Height - h) / 2;
                    break;
            }

            return new Rectangle(x, y, w, h);
        }

        public int GetPinnedTabCount()
        {
            if (_boundTabControl == null) return 0;
            int count = 0;
            for (int i = 0; i < _boundTabControl.TabPages.Count; i++)
            {
                IBrowserTabState tab = _boundTabControl.TabPages[i] as IBrowserTabState;
                if (tab != null && tab.IsPinned)
                {
                    count++;
                }
            }
            return count;
        }

        public int GetPinnedTabWidth()
        {
            return Math.Max(32, (int)(38 * _dpiScale));
        }

        public void UpdateTabVisibility()
        {
            Rectangle strip = GetTabStripRect();
            int count = (_boundTabControl != null) ? _boundTabControl.TabPages.Count : 0;
            int btnSize = Math.Max(22, (int)(24 * _dpiScale));
            int spacing = (int)(4 * _dpiScale);
            int minTabW = Math.Max(40, (int)(48 * _dpiScale));

            int pinnedCount = GetPinnedTabCount();
            int unpinnedCount = Math.Max(0, count - pinnedCount);
            int totalPinnedW = pinnedCount * GetPinnedTabWidth();

            if (unpinnedCount == 0)
            {
                _visibleTabCount = 0;
                _hasOverflow = false;
                _firstVisibleTab = 0;
                return;
            }

            int unpinnedAvail = Math.Max(0, strip.Width - totalPinnedW);
            int availNoOverflow = Math.Max(0, unpinnedAvail - btnSize - spacing);
            if (unpinnedCount * minTabW <= availNoOverflow)
            {
                _hasOverflow = false;
                _visibleTabCount = unpinnedCount;
                _firstVisibleTab = 0;
                return;
            }

            _hasOverflow = true;
            int availWithOverflow = Math.Max(minTabW, unpinnedAvail - (btnSize * 2) - (spacing * 2));
            _visibleTabCount = Math.Max(1, availWithOverflow / minTabW);
            if (_visibleTabCount > unpinnedCount) _visibleTabCount = unpinnedCount;

            int sel = (_boundTabControl != null && _boundTabControl.SelectedIndex >= 0) ? _boundTabControl.SelectedIndex : 0;
            int selUnpinned = sel - pinnedCount;
            if (selUnpinned >= 0)
            {
                if (selUnpinned < _firstVisibleTab)
                {
                    _firstVisibleTab = selUnpinned;
                }
                else if (selUnpinned >= _firstVisibleTab + _visibleTabCount)
                {
                    _firstVisibleTab = selUnpinned - _visibleTabCount + 1;
                }
            }
            _firstVisibleTab = Math.Max(0, Math.Min(_firstVisibleTab, unpinnedCount - _visibleTabCount));
        }

        public int GetVisibleTabWidth()
        {
            if (_visibleTabCount <= 0) return 0;
            Rectangle strip = GetTabStripRect();
            int btnSize = Math.Max(22, (int)(24 * _dpiScale));
            int spacing = (int)(4 * _dpiScale);
            int minTabW = Math.Max(40, (int)(48 * _dpiScale));
            int maxTabW = Math.Max(160, (int)(256 * _dpiScale));

            int pinnedCount = GetPinnedTabCount();
            int unpinnedAvail = Math.Max(0, strip.Width - (pinnedCount * GetPinnedTabWidth()));

            int avail = _hasOverflow ? (unpinnedAvail - (btnSize * 2) - (spacing * 2)) : (unpinnedAvail - btnSize - spacing);
            avail = Math.Max(0, avail);
            return Math.Max(minTabW, Math.Min(maxTabW, avail / Math.Max(1, _visibleTabCount)));
        }

        public Rectangle GetTabRect(int index)
        {
            if (_boundTabControl == null || index < 0 || index >= _boundTabControl.TabPages.Count)
            {
                return Rectangle.Empty;
            }

            UpdateTabVisibility();

            int pinnedCount = GetPinnedTabCount();
            Rectangle strip = GetTabStripRect();
            int pinnedW = GetPinnedTabWidth();

            if (index < pinnedCount)
            {
                int px = strip.Left + (index * pinnedW);
                return new Rectangle(px, strip.Top, pinnedW, strip.Height);
            }

            int unpinnedIdx = index - pinnedCount;
            if (unpinnedIdx < _firstVisibleTab || unpinnedIdx >= _firstVisibleTab + _visibleTabCount)
            {
                return Rectangle.Empty;
            }

            int slot = unpinnedIdx - _firstVisibleTab;
            int tabW = GetVisibleTabWidth();
            int x = strip.Left + (pinnedCount * pinnedW) + (slot * tabW);
            int h = strip.Height;
            int y = strip.Top;

            return new Rectangle(x, y, tabW, h);
        }

        public Rectangle GetTabCloseRect(int index)
        {
            int pinnedCount = GetPinnedTabCount();
            if (index < pinnedCount) return Rectangle.Empty;

            Rectangle tabRect = GetTabRect(index);
            if (tabRect.IsEmpty) return Rectangle.Empty;
            int size = Math.Max(14, (int)(16 * _dpiScale));
            if (tabRect.Width < (int)(28 * _dpiScale) || tabRect.Height <= size) return Rectangle.Empty;
            int rightPad = (tabRect.Width < (int)(52 * _dpiScale)) ? (int)(4 * _dpiScale) : (int)(5 * _dpiScale);
            int x = tabRect.Right - size - rightPad;
            int y = tabRect.Top + (tabRect.Height - size) / 2;
            return new Rectangle(x, y, size, size);
        }

        public Rectangle GetTabAudioRect(int index)
        {
            if (_boundTabControl == null || index < 0 || index >= _boundTabControl.TabPages.Count)
                return Rectangle.Empty;

            IBrowserTabState tab = _boundTabControl.TabPages[index] as IBrowserTabState;
            if (tab == null || (!tab.IsPlayingAudio && !tab.IsAudioMuted))
                return Rectangle.Empty;

            Rectangle tabRect = GetTabRect(index);
            if (tabRect.IsEmpty) return Rectangle.Empty;

            int pinnedCount = GetPinnedTabCount();
            if (index < pinnedCount)
            {
                int aSize = Math.Max(14, (int)(16 * _dpiScale));
                int ax = tabRect.Left + (tabRect.Width - aSize) / 2;
                int ay = tabRect.Top + (tabRect.Height - aSize) / 2;
                return new Rectangle(ax, ay, aSize, aSize);
            }
            else
            {
                Rectangle closeRect = GetTabCloseRect(index);
                int aSize = Math.Max(13, (int)(15 * _dpiScale));
                int rightAnchor = (!closeRect.IsEmpty) ? closeRect.Left : (tabRect.Right - (int)(5 * _dpiScale));
                int ax = rightAnchor - aSize - (int)(3 * _dpiScale);
                if (ax < tabRect.Left + (int)(20 * _dpiScale)) return Rectangle.Empty;
                int ay = tabRect.Top + (tabRect.Height - aSize) / 2;
                return new Rectangle(ax, ay, aSize, aSize);
            }
        }

        public Rectangle GetTabMediaBadgeRect(int index)
        {
            if (_boundTabControl == null || index < 0 || index >= _boundTabControl.TabPages.Count)
                return Rectangle.Empty;

            IBrowserTabState tab = _boundTabControl.TabPages[index] as IBrowserTabState;
            if (tab == null || !tab.IsMediaCaptured)
                return Rectangle.Empty;

            Rectangle tabRect = GetTabRect(index);
            if (tabRect.IsEmpty) return Rectangle.Empty;

            int badgeSize = Math.Max(4, (int)(5 * _dpiScale));
            int bx = tabRect.Left + (int)(4 * _dpiScale);
            int by = tabRect.Top + Math.Max(5, (int)(5 * _dpiScale));
            return new Rectangle(bx, by, badgeSize, badgeSize);
        }

        public Rectangle GetNewTabButtonRect()
        {
            Rectangle strip = GetTabStripRect();
            int size = Math.Max(22, (int)(24 * _dpiScale));
            int spacing = (int)(4 * _dpiScale);
            int y = strip.Top + (strip.Height - size) / 2;

            UpdateTabVisibility();

            int pinnedCount = GetPinnedTabCount();
            int x = strip.Left + (pinnedCount * GetPinnedTabWidth());
            if (_visibleTabCount > 0)
            {
                int tabW = GetVisibleTabWidth();
                x += (_visibleTabCount * tabW) + spacing;
            }
            else
            {
                x += spacing;
            }

            int rightLimit = GetToolbarLeftLimit() - size - spacing;
            if (_hasOverflow)
            {
                rightLimit -= (size + spacing);
            }
            x = Math.Min(x, rightLimit);

            return new Rectangle(x, y, size, size);
        }

        public Rectangle GetOverflowButtonRect()
        {
            UpdateTabVisibility();
            if (!_hasOverflow) return Rectangle.Empty;

            Rectangle newTab = GetNewTabButtonRect();
            Rectangle strip = GetTabStripRect();
            int size = Math.Max(22, (int)(24 * _dpiScale));
            int spacing = (int)(4 * _dpiScale);
            int x = newTab.Right + spacing;
            int y = strip.Top + (strip.Height - size) / 2;

            int rightLimit = GetToolbarLeftLimit() - size - spacing;
            x = Math.Min(x, rightLimit);

            return new Rectangle(x, y, size, size);
        }

        public void EnsureTabVisible(int index)
        {
            if (_boundTabControl == null || index < 0 || index >= _boundTabControl.TabPages.Count) return;
            UpdateTabVisibility();
            int pinnedCount = GetPinnedTabCount();
            if (index < pinnedCount)
            {
                this.Invalidate();
                return;
            }

            int unpinnedIdx = index - pinnedCount;
            int unpinnedCount = Math.Max(0, _boundTabControl.TabPages.Count - pinnedCount);
            if (unpinnedIdx < _firstVisibleTab)
            {
                _firstVisibleTab = unpinnedIdx;
            }
            else if (unpinnedIdx >= _firstVisibleTab + _visibleTabCount)
            {
                _firstVisibleTab = unpinnedIdx - _visibleTabCount + 1;
            }
            _firstVisibleTab = Math.Max(0, Math.Min(_firstVisibleTab, unpinnedCount - _visibleTabCount));
            this.Invalidate();
        }

        public int HitTestVisibleTabAt(int x)
        {
            UpdateTabVisibility();
            if (_boundTabControl == null) return -1;
            int count = _boundTabControl.TabPages.Count;
            int pinnedCount = GetPinnedTabCount();

            for (int i = 0; i < pinnedCount; i++)
            {
                Rectangle r = GetTabRect(i);
                if (!r.IsEmpty && x >= r.Left && x <= r.Right)
                {
                    return i;
                }
            }

            for (int i = pinnedCount + _firstVisibleTab; i < pinnedCount + _firstVisibleTab + _visibleTabCount; i++)
            {
                if (i >= count) break;
                Rectangle r = GetTabRect(i);
                if (!r.IsEmpty && x >= r.Left && x <= r.Right)
                {
                    return i;
                }
            }
            return -1;
        }

        public int HitTestAudioButtonAt(Point pt)
        {
            if (_boundTabControl == null) return -1;
            int count = _boundTabControl.TabPages.Count;
            for (int i = 0; i < count; i++)
            {
                Rectangle ar = GetTabAudioRect(i);
                if (!ar.IsEmpty)
                {
                    Rectangle hit = Rectangle.Inflate(ar, 2, 2);
                    if (hit.Contains(pt)) return i;
                }
            }
            return -1;
        }

        private void ComputeTabDropTarget(int mouseX)
        {
            if (_boundTabControl == null)
            {
                _dropTargetIndex = -1;
                return;
            }

            int count = _boundTabControl.TabPages.Count;
            if (count <= 0)
            {
                _dropTargetIndex = -1;
                return;
            }

            int pinnedCount = GetPinnedTabCount();
            bool isDraggedPinned = (_dragTabIndex >= 0 && _dragTabIndex < pinnedCount);

            if (isDraggedPinned)
            {
                for (int i = 0; i < pinnedCount; i++)
                {
                    Rectangle r = GetTabRect(i);
                    if (r.IsEmpty) continue;
                    if (mouseX >= r.Left && mouseX <= r.Right)
                    {
                        _dropTargetIndex = i;
                        _dropTargetAfter = (mouseX >= r.Left + r.Width / 2);
                        return;
                    }
                }
                if (pinnedCount > 0)
                {
                    Rectangle firstRect = GetTabRect(0);
                    if (!firstRect.IsEmpty && mouseX < firstRect.Left)
                    {
                        _dropTargetIndex = 0;
                        _dropTargetAfter = false;
                        return;
                    }
                    Rectangle lastRect = GetTabRect(pinnedCount - 1);
                    if (!lastRect.IsEmpty && mouseX > lastRect.Right)
                    {
                        _dropTargetIndex = pinnedCount - 1;
                        _dropTargetAfter = true;
                        return;
                    }
                }
            }
            else
            {
                UpdateTabVisibility();
                if (_visibleTabCount <= 0)
                {
                    _dropTargetIndex = -1;
                    return;
                }
                int lastVisible = pinnedCount + _firstVisibleTab + _visibleTabCount - 1;

                for (int i = pinnedCount + _firstVisibleTab; i <= lastVisible && i < count; i++)
                {
                    Rectangle r = GetTabRect(i);
                    if (r.IsEmpty) continue;

                    if (mouseX >= r.Left && mouseX <= r.Right)
                    {
                        _dropTargetIndex = i;
                        _dropTargetAfter = (mouseX >= r.Left + r.Width / 2);
                        return;
                    }
                }

                Rectangle firstRect = GetTabRect(pinnedCount + _firstVisibleTab);
                if (!firstRect.IsEmpty && mouseX < firstRect.Left)
                {
                    _dropTargetIndex = pinnedCount + _firstVisibleTab;
                    _dropTargetAfter = false;
                    return;
                }

                Rectangle lastRect = GetTabRect(Math.Min(count - 1, lastVisible));
                if (!lastRect.IsEmpty && mouseX > lastRect.Right)
                {
                    _dropTargetIndex = Math.Min(count - 1, lastVisible);
                    _dropTargetAfter = true;
                    return;
                }
            }

            _dropTargetIndex = -1;
        }

        public int ComputeTabDropIndex(int mouseX)
        {
            if (_boundTabControl == null) return -1;
            int count = _boundTabControl.TabPages.Count;
            if (count <= 0) return 0;

            int pinnedCount = GetPinnedTabCount();
            UpdateTabVisibility();
            if (_visibleTabCount <= 0) return count;

            int lastVisible = pinnedCount + _firstVisibleTab + _visibleTabCount - 1;

            for (int i = pinnedCount + _firstVisibleTab; i <= lastVisible && i < count; i++)
            {
                Rectangle r = GetTabRect(i);
                if (r.IsEmpty) continue;

                if (mouseX >= r.Left && mouseX <= r.Right)
                {
                    return (mouseX >= r.Left + r.Width / 2) ? i + 1 : i;
                }
            }

            Rectangle firstRect = GetTabRect(pinnedCount + _firstVisibleTab);
            if (!firstRect.IsEmpty && mouseX < firstRect.Left)
            {
                return pinnedCount + _firstVisibleTab;
            }

            Rectangle lastRect = GetTabRect(Math.Min(count - 1, lastVisible));
            if (!lastRect.IsEmpty && mouseX > lastRect.Right)
            {
                return Math.Min(count, lastVisible + 1);
            }

            return count;
        }

        private static WbTopBar FindTopBar(Control parent)
        {
            if (parent == null) return null;
            foreach (Control c in parent.Controls)
            {
                WbTopBar tb = c as WbTopBar;
                if (tb != null && !tb.IsDisposed && tb.Visible) return tb;
                WbTopBar child = FindTopBar(c);
                if (child != null) return child;
            }
            return null;
        }

        private static void UpdateExternalDropHighlight(WbTopBar activeTarget)
        {
            try
            {
                FormCollection openForms = Application.OpenForms;
                if (openForms != null)
                {
                    for (int i = 0; i < openForms.Count; i++)
                    {
                        Form f = openForms[i];
                        if (f != null && !f.IsDisposed)
                        {
                            WbTopBar tb = FindTopBar(f);
                            if (tb != null && !tb.IsDisposed)
                            {
                                bool shouldHighlight = (tb == activeTarget);
                                if (tb.DropHighlight != shouldHighlight)
                                {
                                    tb.DropHighlight = shouldHighlight;
                                    tb.Update();
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void ClearAllExternalDropHighlights()
        {
            UpdateExternalDropHighlight(null);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point pt = e.Location;
            ButtonId prevBtn = _hoveredBtn;
            int prevTab = _hoveredTabIndex;
            int prevClose = _hoveredCloseIndex;
            int prevAudio = _hoveredAudioIndex;

            // Handle toolbar button dragging
            if (e.Button == MouseButtons.Left && _dragToolbarButtonId != null)
            {
                _toolbarDragCurrentPt = pt;
                int tdx = Math.Abs(e.X - _toolbarDragStartPt.X);
                int tdy = Math.Abs(e.Y - _toolbarDragStartPt.Y);
                if (!_isToolbarDragging && (tdx > 4 || tdy > 4))
                {
                    _isToolbarDragging = true;
                    this.Cursor = Cursors.SizeWE;
                }

                if (_isToolbarDragging)
                {
                    ComputeToolbarDropTarget(e.X);
                    this.Invalidate();
                    return;
                }
            }

            // Handle tab dragging
            if (e.Button == MouseButtons.Left && _dragTabIndex >= 0)
            {
                _dragCurrentPt = pt;
                int dx = Math.Abs(e.X - _dragStartPt.X);
                int dy = Math.Abs(e.Y - _dragStartPt.Y);
                int dist = (int)Math.Sqrt(dx * dx + dy * dy);

                // Require at least 10px drag movement before initiating drag state
                if (!_isTabDragging && (dx > 10 || dy > 10))
                {
                    _isTabDragging = true;
                }

                if (_isTabDragging)
                {
                    Form currentForm = this.FindForm();
                    Point screenPt = this.PointToScreen(pt);

                    WbTopBar targetTopBar = null;
                    Form targetForm = null;

                    try
                    {
                        FormCollection openForms = Application.OpenForms;
                        if (openForms != null)
                        {
                            for (int formIdx = 0; formIdx < openForms.Count; formIdx++)
                            {
                                Form f = openForms[formIdx];
                                if (f != null && f != currentForm && !f.IsDisposed && f.Visible && f.WindowState != FormWindowState.Minimized)
                                {
                                    WbTopBar tb = FindTopBar(f);
                                    if (tb != null && !tb.IsDisposed && tb.Visible)
                                    {
                                        Rectangle tbScreen = tb.RectangleToScreen(tb.ClientRectangle);
                                        Rectangle dropZone = new Rectangle(tbScreen.Left, tbScreen.Top - 8, tbScreen.Width, tbScreen.Height + 20);
                                        if (dropZone.Contains(screenPt))
                                        {
                                            targetTopBar = tb;
                                            targetForm = f;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    if (targetTopBar != null)
                    {
                        _isTearOffPending = false;
                        _dropTargetIndex = -1;
                        this.Cursor = Cursors.Hand;
                        UpdateExternalDropHighlight(targetTopBar);
                    }
                    else
                    {
                        ClearAllExternalDropHighlights();

                        bool outsideTopBar = (e.Y < -15 || e.Y > this.Height + 25 || e.X < -15 || e.X > this.Width + 15);
                        bool canTearOff = (_boundTabControl != null && _boundTabControl.TabPages.Count > 1);

                        // Check if dragged far enough outside to tear off
                        if (outsideTopBar && dist > 50)
                        {
                            if (canTearOff)
                            {
                                _isTearOffPending = true;
                                _dropTargetIndex = -1;
                                this.Cursor = Cursors.SizeAll;
                            }
                            else
                            {
                                _isTearOffPending = false;
                                _dropTargetIndex = -1;
                                this.Cursor = Cursors.Default;
                            }
                        }
                        else
                        {
                            _isTearOffPending = false;
                            this.Cursor = Cursors.Hand;
                            ComputeTabDropTarget(e.X);
                        }
                    }

                    this.Invalidate();
                    return;
                }
            }

            _hoveredBtn = HitTestButton(pt);
            _hoveredTabIndex = -1;
            _hoveredCloseIndex = -1;
            _hoveredAudioIndex = -1;

            if (_boundTabControl != null)
            {
                UpdateTabVisibility();
                int pinnedCount = GetPinnedTabCount();

                // 1. Check pinned tabs
                for (int i = 0; i < pinnedCount; i++)
                {
                    if (GetTabRect(i).Contains(pt))
                    {
                        _hoveredTabIndex = i;
                        break;
                    }
                }

                // 2. Check visible unpinned tabs
                if (_hoveredTabIndex < 0)
                {
                    for (int i = pinnedCount + _firstVisibleTab; i < pinnedCount + _firstVisibleTab + _visibleTabCount; i++)
                    {
                        if (i >= _boundTabControl.TabPages.Count) break;
                        Rectangle closeHit = Rectangle.Inflate(GetTabCloseRect(i), 3, 3);
                        Rectangle audioRect = GetTabAudioRect(i);
                        Rectangle audioHit = !audioRect.IsEmpty ? Rectangle.Inflate(audioRect, 2, 2) : Rectangle.Empty;

                        if (closeHit.Contains(pt))
                        {
                            _hoveredCloseIndex = i;
                            _hoveredTabIndex = i;
                            break;
                        }
                        else if (!audioHit.IsEmpty && audioHit.Contains(pt))
                        {
                            _hoveredAudioIndex = i;
                            _hoveredTabIndex = i;
                            break;
                        }
                        else if (GetTabRect(i).Contains(pt))
                        {
                            _hoveredTabIndex = i;
                            break;
                        }
                    }
                }
            }

            if (_longPressBtn != ButtonId.None)
            {
                Rectangle btnR = (_longPressBtn == ButtonId.Back) ? GetBackButtonRect() : GetForwardButtonRect();
                if (!btnR.Contains(pt))
                {
                    if (_longPressTimer != null) _longPressTimer.Stop();
                    _longPressBtn = ButtonId.None;
                }
            }

            if (_hoveredBtn != prevBtn || _hoveredTabIndex != prevTab || _hoveredCloseIndex != prevClose || _hoveredAudioIndex != prevAudio)
            {
                bool isHand = (_hoveredBtn != ButtonId.None || _hoveredTabIndex >= 0);
                if (_hoveredBtn == ButtonId.Star && !_canBookmark) isHand = false;
                if (_hoveredBtn == ButtonId.Back && !_canGoBack) isHand = false;
                if (_hoveredBtn == ButtonId.Forward && !_canGoForward) isHand = false;
                this.Cursor = isHand ? Cursors.Hand : Cursors.Default;

                if (_hoveredBtn != prevBtn)
                {
                    string tip = null;
                    switch (_hoveredBtn)
                    {
                        case ButtonId.Home:
                            tip = _isIncognito ? AppStrings.Browser.TipHomeIncognito : AppStrings.Browser.TipHome;
                            break;
                        case ButtonId.Star:
                            tip = _canBookmark
                                ? (_isBookmarked ? AppStrings.Browser.TipRemoveBookmark : AppStrings.Browser.TipBookmark)
                                : AppStrings.Browser.TipCannotBookmark;
                            break;
                        case ButtonId.Bookmarks:
                            tip = AppStrings.Browser.TipBookmarks;
                            break;
                        case ButtonId.History:
                            tip = AppStrings.Browser.TipHistory;
                            break;
                        case ButtonId.Downloads:
                            tip = _activeDownloadCount > 0
                                ? string.Format(AppStrings.Browser.TipDownloadsActiveFormat, _activeDownloadCount)
                                : AppStrings.Browser.TipDownloads;
                            break;
                        case ButtonId.Shield:
                            tip = (_blockedCount > 0 && !IsInternalPage)
                                ? string.Format(AppStrings.Browser.TipShieldBlockedFormat, _blockedCount)
                                : AppStrings.Browser.TipShieldPrefix + (_adBlockStatusSuffix ?? AppStrings.Browser.ShieldDefaultStatusSuffix);
                            break;
                        case ButtonId.Screenshot:
                            tip = "Screenshot (Ctrl+Shift+S)";
                            break;
                        case ButtonId.Gear:
                            tip = AppStrings.Browser.TipTools;
                            break;
                        case ButtonId.IncognitoBadge:
                            tip = AppStrings.Browser.IncognitoBadgeTooltip;
                            break;
                        case ButtonId.Back:
                            if (_canGoBack) tip = AppStrings.Browser.TipBack;
                            break;
                        case ButtonId.Forward:
                            if (_canGoForward) tip = AppStrings.Browser.TipForward;
                            break;
                        case ButtonId.Go:
                            tip = AppStrings.Browser.TipGo;
                            break;
                        case ButtonId.Refresh:
                            tip = _isLoading ? AppStrings.Browser.TipStop : AppStrings.Browser.TipReload;
                            break;
                        case ButtonId.NewTab:
                            tip = AppStrings.Browser.TipNewTab;
                            break;
                        case ButtonId.Overflow:
                            if (_hasOverflow && _boundTabControl != null)
                            {
                                int hiddenCount = Math.Max(0, _boundTabControl.TabPages.Count - _visibleTabCount);
                                tip = string.Format(hiddenCount == 1 ? AppStrings.Browser.TipHiddenTabFormatSingle : AppStrings.Browser.TipHiddenTabFormatPlural, hiddenCount);
                            }
                            break;
                    }
                    SetActiveToolTip(tip);
                }
                else if (_hoveredTabIndex >= 0 && _boundTabControl != null && _hoveredTabIndex < _boundTabControl.TabPages.Count)
                {
                    int pinnedCount = GetPinnedTabCount();
                    if (_hoveredAudioIndex >= 0 && _hoveredAudioIndex >= pinnedCount)
                    {
                        IBrowserTabState bt = _boundTabControl.TabPages[_hoveredAudioIndex] as IBrowserTabState;
                        if (bt != null)
                        {
                            SetActiveToolTip(bt.IsAudioMuted ? AppStrings.Browser.TipUnmuteSite : AppStrings.Browser.TipMuteSite);
                        }
                    }
                    else if (_hoveredCloseIndex >= 0)
                    {
                        SetActiveToolTip(AppStrings.Browser.TipCloseTab);
                    }
                    else
                    {
                        TabPage p = _boundTabControl.TabPages[_hoveredTabIndex];
                        ITabFaviconProvider tfp = p as ITabFaviconProvider;
                        IBrowserTabState bts = p as IBrowserTabState;
                        string title = (tfp != null && !string.IsNullOrEmpty(tfp.CurrentTitle)) ? tfp.CurrentTitle : p.Text;
                        string tip = TitleHelper.FormatTabToolTip(title);
                        if (bts != null && bts.IsMediaCaptured)
                        {
                            tip = (string.IsNullOrEmpty(tip) ? "" : tip + "\n") + AppStrings.Browser.TipMediaCaptured;
                        }
                        SetActiveToolTip(tip);
                    }
                }
                else
                {
                    SetActiveToolTip(null);
                }

                this.Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoveredBtn = ButtonId.None;
            _hoveredTabIndex = -1;
            _hoveredCloseIndex = -1;
            _hoveredAudioIndex = -1;
            _pressedBtn = ButtonId.None;
            SetActiveToolTip(null);
            this.Cursor = Cursors.Default;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                // Check audio button click first (unpinned tabs only; pinned tabs do not toggle mute on click)
                int audioHit = HitTestAudioButtonAt(e.Location);
                int pinnedCount = GetPinnedTabCount();
                if (audioHit >= 0 && audioHit >= pinnedCount)
                {
                    if (_boundTabControl != null && _boundTabControl.SelectedIndex != audioHit)
                    {
                        _boundTabControl.SelectedIndex = audioHit;
                    }
                    if (TabAudioClicked != null)
                    {
                        TabAudioClicked(this, audioHit);
                    }
                    this.Invalidate();
                    return;
                }

                _pressedBtn = HitTestButton(e.Location);
                if ((_pressedBtn == ButtonId.Back && _canGoBack) || (_pressedBtn == ButtonId.Forward && _canGoForward))
                {
                    _longPressBtn = _pressedBtn;
                    _longPressFired = false;
                    if (_longPressTimer != null)
                    {
                        _longPressTimer.Stop();
                        _longPressTimer.Start();
                    }
                }
                else
                {
                    _longPressBtn = ButtonId.None;
                    _longPressFired = false;
                    if (_longPressTimer != null) _longPressTimer.Stop();
                }

                string hitToolbarId = ButtonIdToToolbarId(_pressedBtn);
                if (!string.IsNullOrEmpty(hitToolbarId))
                {
                    _dragToolbarButtonId = hitToolbarId;
                    _toolbarDragStartPt = e.Location;
                    _toolbarDragCurrentPt = e.Location;
                    _isToolbarDragging = false;
                    _toolbarDropInsertIndex = -1;
                }
                else
                {
                    _dragToolbarButtonId = null;
                    _isToolbarDragging = false;
                    _toolbarDropInsertIndex = -1;
                }

                int tabHit = -1;
                if (_hoveredCloseIndex >= 0 && Rectangle.Inflate(GetTabCloseRect(_hoveredCloseIndex), 4, 4).Contains(e.Location))
                {
                    tabHit = _hoveredCloseIndex;
                    _hoveredTabIndex = tabHit;
                    _hoveredCloseIndex = tabHit;
                }
                else
                {
                    tabHit = HitTestVisibleTabAt(e.X);
                    if (tabHit >= 0)
                    {
                        _hoveredTabIndex = tabHit;
                        pinnedCount = GetPinnedTabCount();
                        if (tabHit >= pinnedCount)
                        {
                            Rectangle closeHit = Rectangle.Inflate(GetTabCloseRect(tabHit), 4, 4);
                            if (closeHit.Contains(e.Location))
                            {
                                _hoveredCloseIndex = tabHit;
                            }
                            else
                            {
                                _hoveredCloseIndex = -1;
                            }
                        }
                        else
                        {
                            _hoveredCloseIndex = -1;
                        }
                    }
                    else
                    {
                        _hoveredTabIndex = -1;
                        _hoveredCloseIndex = -1;
                    }
                }

                _pressedTabIndex = _hoveredTabIndex;
                _pressedCloseIndex = _hoveredCloseIndex;

                if (_hoveredCloseIndex >= 0)
                {
                    // User pressed tab close button: do NOT start tab drag, do NOT start window drag!
                    _dragTabIndex = -1;
                    _isTabDragging = false;
                    _dropTargetIndex = -1;
                    _dropTargetAfter = false;
                    _isTearOffPending = false;
                }
                else if (_hoveredTabIndex >= 0)
                {
                    // Select tab IMMEDIATELY on MouseDown for instant, zero-latency switching
                    if (_boundTabControl != null && _boundTabControl.SelectedIndex != _hoveredTabIndex && _hoveredTabIndex < _boundTabControl.TabPages.Count)
                    {
                        _boundTabControl.SelectedIndex = _hoveredTabIndex;
                        EnsureTabVisible(_hoveredTabIndex);
                        if (TabSelected != null)
                        {
                            TabSelected(this, _hoveredTabIndex);
                        }
                    }

                    // User pressed on tab body: prepare for tab drag
                    _dragTabIndex = _hoveredTabIndex;
                    _dragStartPt = e.Location;
                    _dragCurrentPt = e.Location;
                    _isTabDragging = false;
                    _dropTargetIndex = -1;
                    _dropTargetAfter = false;
                    _isTearOffPending = false;
                }
                else
                {
                    _dragTabIndex = -1;
                    _isTabDragging = false;
                    _dropTargetIndex = -1;
                    _dropTargetAfter = false;
                    _isTearOffPending = false;

                    // Only initiate window caption drag when clicking empty space (NOT on buttons or address box)
                    if (_pressedBtn == ButtonId.None && !GetAddressBoxRect().Contains(e.Location))
                    {
                        Form currentForm = this.FindForm();
                        if (currentForm != null)
                        {
                            ReleaseCapture();
                            SendMessage(currentForm.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                        }
                    }
                }
                this.Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                ButtonId rightHitBtn = HitTestButton(e.Location);
                if (rightHitBtn == ButtonId.Back)
                {
                    if (_canGoBack && BackLongPressed != null) BackLongPressed(this, EventArgs.Empty);
                    return;
                }
                if (rightHitBtn == ButtonId.Forward)
                {
                    if (_canGoForward && ForwardLongPressed != null) ForwardLongPressed(this, EventArgs.Empty);
                    return;
                }
                string rightToolbarId = ButtonIdToToolbarId(rightHitBtn);
                if (!string.IsNullOrEmpty(rightToolbarId) || rightHitBtn == ButtonId.Gear || (e.X >= GetToolbarLeftLimit() && e.Y < this.Height))
                {
                    ShowToolbarContextMenu(e.Location, rightToolbarId);
                    return;
                }

                int tabHit = HitTestVisibleTabAt(e.X);
                if (tabHit >= 0)
                {
                    Point screenPt = this.PointToScreen(e.Location);
                    if (TabRightClicked != null)
                    {
                        TabRightClicked(this, new TabRightClickEventArgs(tabHit, screenPt));
                    }
                    return;
                }

                if (GetAddressBoxRect().Contains(e.Location))
                {
                    ShowAddressBarContextMenu(this.PointToScreen(e.Location));
                    return;
                }

                if (e.X >= GetAddressBoxRect().Right && e.X < GetToolbarLeftLimit())
                {
                    Point screenPt = this.PointToScreen(e.Location);
                    if (TabStripEmptyAreaRightClicked != null)
                    {
                        TabStripEmptyAreaRightClicked(this, screenPt);
                    }
                    return;
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                // Middle click on tab closes it (pinned tabs are protected)
                int tabHit = HitTestVisibleTabAt(e.X);
                int pinnedCount = GetPinnedTabCount();
                if (tabHit >= pinnedCount && TabCloseClicked != null)
                {
                    TabCloseClicked(this, tabHit);
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_longPressTimer != null) _longPressTimer.Stop();
            if (_longPressFired)
            {
                _longPressFired = false;
                _longPressBtn = ButtonId.None;
                _pressedBtn = ButtonId.None;
                this.Invalidate();
                return;
            }
            _longPressBtn = ButtonId.None;

            if (e.Button == MouseButtons.Left)
            {
                if (_isToolbarDragging && !string.IsNullOrEmpty(_dragToolbarButtonId))
                {
                    CommitToolbarReorder();
                    _isToolbarDragging = false;
                    _dragToolbarButtonId = null;
                    _toolbarDropInsertIndex = -1;
                    _pressedBtn = ButtonId.None;
                    this.Cursor = Cursors.Default;
                    this.Invalidate();
                    return;
                }
                else
                {
                    _isToolbarDragging = false;
                    _dragToolbarButtonId = null;
                    _toolbarDropInsertIndex = -1;
                }

                if (_isTabDragging && _dragTabIndex >= 0)
                {
                    Point screenPt = this.PointToScreen(e.Location);
                    Form currentForm = this.FindForm();
                    Rectangle formBounds = (currentForm != null) ? currentForm.Bounds : Rectangle.Empty;

                    int dx = Math.Abs(e.X - _dragStartPt.X);
                    int dy = Math.Abs(e.Y - _dragStartPt.Y);
                    int dist = (int)Math.Sqrt(dx * dx + dy * dy);

                    int draggedIdx = _dragTabIndex;
                    int targetIdx = _dropTargetIndex;
                    bool targetAfter = _dropTargetAfter;
                    bool isTearOff = _isTearOffPending;

                    // Clean up drag states BEFORE committing actions
                    _isTabDragging = false;
                    _dragTabIndex = -1;
                    _dropTargetIndex = -1;
                    _dropTargetAfter = false;
                    _isTearOffPending = false;
                    _pressedTabIndex = -1;
                    _pressedCloseIndex = -1;
                    _pressedBtn = ButtonId.None;
                    this.Cursor = Cursors.Default;
                    this.Invalidate();

                    ClearAllExternalDropHighlights();

                    // 1. Check if dropped onto another window's tab section
                    Form targetForm = null;
                    WbTopBar targetTopBar = null;
                    try
                    {
                        FormCollection openForms = Application.OpenForms;
                        if (openForms != null)
                        {
                            for (int formIdx = 0; formIdx < openForms.Count; formIdx++)
                            {
                                Form f = openForms[formIdx];
                                if (f != null && f != currentForm && !f.IsDisposed && f.Visible && f.WindowState != FormWindowState.Minimized)
                                {
                                    WbTopBar tb = FindTopBar(f);
                                    if (tb != null && !tb.IsDisposed && tb.Visible)
                                    {
                                        Rectangle tbScreen = tb.RectangleToScreen(tb.ClientRectangle);
                                        Rectangle dropZone = new Rectangle(tbScreen.Left, tbScreen.Top - 8, tbScreen.Width, tbScreen.Height + 20);
                                        if (dropZone.Contains(screenPt))
                                        {
                                            targetForm = f;
                                            targetTopBar = tb;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    if (targetForm != null)
                    {
                        int targetDropIdx = -1;
                        if (targetTopBar != null)
                        {
                            Point targetLocalPt = targetTopBar.PointToClient(screenPt);
                            targetDropIdx = targetTopBar.ComputeTabDropIndex(targetLocalPt.X);
                        }
                        if (TabDroppedOntoWindow != null)
                        {
                            TabDroppedOntoWindow(this, new TabDroppedOntoWindowEventArgs(draggedIdx, targetForm, screenPt, targetDropIdx));
                        }
                        return;
                    }

                    if (TabDroppedOnBookmarksBar != null)
                    {
                        TabDroppedOnBookmarksBarEventArgs dropBarArgs = new TabDroppedOnBookmarksBarEventArgs(draggedIdx, screenPt);
                        TabDroppedOnBookmarksBar(this, dropBarArgs);
                        if (dropBarArgs.Handled)
                        {
                            return;
                        }
                    }

                    bool canTearOff = (_boundTabControl != null && _boundTabControl.TabPages.Count > 1);

                    // 2. Tab2Window (Tear Off onto empty desktop)
                    if (isTearOff && canTearOff && dist > 60)
                    {
                        if (TabTornOff != null)
                        {
                            TabTornOff(this, new TabTearOffEventArgs(draggedIdx, screenPt));
                        }
                        return;
                    }

                    // 2. Tab2Tab: Reorder tab within the window (commits ONLY on mouse release)
                    if (targetIdx >= 0 && _boundTabControl != null && targetIdx < _boundTabControl.TabPages.Count)
                    {
                        int destination = targetIdx;
                        if (targetAfter)
                        {
                            if (draggedIdx > targetIdx)
                            {
                                destination = targetIdx + 1;
                            }
                            else
                            {
                                destination = targetIdx;
                            }
                        }
                        else
                        {
                            if (draggedIdx < targetIdx)
                            {
                                destination = targetIdx - 1;
                            }
                            else
                            {
                                destination = targetIdx;
                            }
                        }

                        destination = Math.Max(0, Math.Min(_boundTabControl.TabPages.Count - 1, destination));
                        int pinnedCount = GetPinnedTabCount();
                        bool isDraggedPinned = (draggedIdx < pinnedCount);
                        if (isDraggedPinned)
                        {
                            destination = Math.Min(destination, pinnedCount - 1);
                        }
                        else
                        {
                            destination = Math.Max(destination, pinnedCount);
                        }

                        if (destination != draggedIdx)
                        {
                            if (TabMoved != null)
                            {
                                TabMoved(this, new TabMoveEventArgs(draggedIdx, destination));
                            }
                        }
                    }

                    return;
                }

                // Check if close button was clicked
                int clickedClose = -1;
                if (_pressedCloseIndex >= 0)
                {
                    Rectangle closeRect = GetTabCloseRect(_pressedCloseIndex);
                    Rectangle expandedClose = Rectangle.Inflate(closeRect, 6, 6);
                    if (expandedClose.Contains(e.Location))
                    {
                        clickedClose = _pressedCloseIndex;
                    }
                }

                ButtonId clickedBtn = (_pressedBtn == HitTestButton(e.Location)) ? _pressedBtn : ButtonId.None;
                int clickedTab = (_pressedTabIndex == _hoveredTabIndex && _hoveredTabIndex >= 0 && _pressedCloseIndex < 0) ? _hoveredTabIndex : -1;

                _pressedBtn = ButtonId.None;
                _pressedTabIndex = -1;
                _pressedCloseIndex = -1;
                this.Invalidate();

                if (clickedClose >= 0)
                {
                    if (TabCloseClicked != null)
                    {
                        TabCloseClicked(this, clickedClose);
                    }
                    return;
                }

                if (clickedTab >= 0 && _boundTabControl != null)
                {
                    if (clickedTab < _boundTabControl.TabPages.Count)
                    {
                        if (_boundTabControl.SelectedIndex != clickedTab)
                        {
                            _boundTabControl.SelectedIndex = clickedTab;
                            EnsureTabVisible(clickedTab);
                            if (TabSelected != null)
                            {
                                TabSelected(this, clickedTab);
                            }
                        }
                    }
                    return;
                }

                switch (clickedBtn)
                {
                    case ButtonId.Back:
                        if (_canGoBack && BackClicked != null) BackClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.Forward:
                        if (_canGoForward && ForwardClicked != null) ForwardClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.Go:
                        ReleaseAddressBarFocus();
                        if (NavigateRequested != null && _addressTextBox != null)
                        {
                            NavigateRequested(this, _addressTextBox.Text.Trim());
                        }
                        break;
                    case ButtonId.Search:
                        ReleaseAddressBarFocus();
                        if (NavigateRequested != null) NavigateRequested(this, _addressTextBox.Text.Trim());
                        break;
                    case ButtonId.Refresh:
                        ReleaseAddressBarFocus();
                        if (RefreshClicked != null) RefreshClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.NewTab:
                        if (NewTabClicked != null) NewTabClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.Overflow:
                        ShowTabOverflowMenu();
                        if (OverflowClicked != null) OverflowClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.Home:
                        if (HomeClicked != null) HomeClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.Star:
                        if (_canBookmark && StarClicked != null) StarClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.Bookmarks:
                        if (BookmarksClicked != null) BookmarksClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.History:
                        if (HistoryClicked != null) HistoryClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.Downloads:
                        if (DownloadsClicked != null) DownloadsClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.Shield:
                        if (ShieldClicked != null) ShieldClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.Screenshot:
                        if (ScreenshotClicked != null) ScreenshotClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.Gear:
                        if (GearClicked != null) GearClicked(this, EventArgs.Empty);
                        break;
                    case ButtonId.IncognitoBadge:
                        if (IncognitoBadgeClicked != null) IncognitoBadgeClicked(this, EventArgs.Empty);
                        break;
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (GetTabStripRect().Contains(e.Location) && _boundTabControl != null && _boundTabControl.TabPages.Count > 1)
            {
                int cur = _boundTabControl.SelectedIndex;
                if (cur >= 0)
                {
                    int target = (e.Delta < 0)
                        ? (cur + 1) % _boundTabControl.TabPages.Count
                        : (cur - 1 + _boundTabControl.TabPages.Count) % _boundTabControl.TabPages.Count;
                    _boundTabControl.SelectedIndex = target;
                    EnsureTabVisible(target);
                }
            }
        }

        #region Drag and Drop Handling (Hyperlinks & URLs)

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            if (WebDragDropHelper.HasUrlData(drgevent.Data))
            {
                drgevent.Effect = DragDropEffects.Copy;
            }
            else
            {
                drgevent.Effect = DragDropEffects.None;
            }
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            base.OnDragOver(drgevent);
            if (WebDragDropHelper.HasUrlData(drgevent.Data))
            {
                drgevent.Effect = DragDropEffects.Copy;
            }
            else
            {
                drgevent.Effect = DragDropEffects.None;
            }
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);
            string url = WebDragDropHelper.ExtractUrlFromDataObject(drgevent.Data);
            if (string.IsNullOrEmpty(url)) return;

            Point clientPt = this.PointToClient(new Point(drgevent.X, drgevent.Y));
            Rectangle addrRect = GetAddressBoxRect();

            if (addrRect.Contains(clientPt))
            {
                this.AddressText = url;
                if (NavigateRequested != null)
                {
                    NavigateRequested(this, url);
                }
            }
            else
            {
                int dropIdx = ComputeTabDropIndex(clientPt.X);
                if (UrlDroppedAsNewTab != null)
                {
                    UrlDroppedAsNewTab(this, new UrlDroppedEventArgs(url, dropIdx));
                }
            }
        }

        #endregion

        private ButtonId HitTestButton(Point pt)
        {
            if (GetBackButtonRect().Contains(pt)) return ButtonId.Back;
            if (GetForwardButtonRect().Contains(pt)) return ButtonId.Forward;
            if (GetGoButtonRect().Contains(pt)) return ButtonId.Go;
            if (GetRefreshButtonHitRect().Contains(pt)) return ButtonId.Refresh;
            if (GetNewTabButtonRect().Contains(pt)) return ButtonId.NewTab;
            if (GetOverflowButtonRect().Contains(pt)) return ButtonId.Overflow;

            var rects = GetToolbarButtonRectangles();
            foreach (var kvp in rects)
            {
                if (kvp.Value.Contains(pt))
                {
                    return ToolbarIdToButtonId(kvp.Key);
                }
            }

            if (_isIncognito && GetIncognitoBadgeRect().Contains(pt)) return ButtonId.IncognitoBadge;
            if (GetGearButtonRect().Contains(pt)) return ButtonId.Gear;
            return ButtonId.None;
        }

        public ContextMenuStrip CreateTabOverflowMenu()
        {
            if (_boundTabControl == null) return null;
            int count = _boundTabControl.TabPages.Count;
            if (count <= _visibleTabCount) return null;

            int hiddenCount = count - _visibleTabCount;

            ContextMenuStrip menu = new ContextMenuStrip();
            UITheme.ApplyContextMenu(menu);

            // Header showing count of hidden tabs
            ToolStripMenuItem headerItem = new ToolStripMenuItem(string.Format(AppStrings.Browser.MenuHiddenTabsFormat, hiddenCount));
            headerItem.Enabled = false;
            headerItem.Font = new Font("Segoe UI", Math.Max(8.5f, 9F * _dpiScale), FontStyle.Bold);
            headerItem.ForeColor = MaterialColors.AccentBlue;
            menu.Items.Add(headerItem);
            menu.Items.Add(new ToolStripSeparator());

            for (int i = 0; i < count; i++)
            {
                // Only show tabs that are unfit for the space
                if (i >= _firstVisibleTab && i < _firstVisibleTab + _visibleTabCount)
                {
                    continue;
                }

                TabPage page = _boundTabControl.TabPages[i];
                ITabFaviconProvider tab = page as ITabFaviconProvider;
                string title = (tab != null && !string.IsNullOrEmpty(tab.CurrentTitle)) ? tab.CurrentTitle : page.Text;
                if (string.IsNullOrEmpty(title)) title = string.Format(AppStrings.Browser.DefaultTabTitleFormat, i + 1);
                title = TitleHelper.NormalizeAndWrapTitle(title, 80);

                ToolStripMenuItem item = new ToolStripMenuItem(!string.IsNullOrEmpty(title) ? title.Replace("&", "&&") : title);
                UITheme.ApplyMenuItem(item);

                Image fav = (tab != null) ? tab.Favicon : null;
                if (fav != null)
                {
                    item.Image = fav;
                }
                else
                {
                    bool isStart = (tab != null && InternalPageHelper.IsStartPage(tab.CurrentUrl));
                    item.Image = isStart ? MaterialIcons.GetIcon("house", 16, ThemeManager.AccentColor) : MaterialIcons.GetIcon("globe", 16, ThemeManager.AccentColor);
                }

                int targetIdx = i;
                item.Click += (s, e) =>
                {
                    if (_boundTabControl != null && targetIdx >= 0 && targetIdx < _boundTabControl.TabPages.Count)
                    {
                        _boundTabControl.SelectedIndex = targetIdx;
                        EnsureTabVisible(targetIdx);
                        this.Invalidate();
                        if (TabSelected != null)
                        {
                            TabSelected(this, targetIdx);
                        }
                    }
                };

                menu.Items.Add(item);
            }

            return menu;
        }

        public void ShowTabOverflowMenu()
        {
            if (_overflowMenu != null && _overflowMenu.Visible)
            {
                _overflowMenu.Close();
                return;
            }

            _overflowMenu = CreateTabOverflowMenu();
            if (_overflowMenu == null) return;
            _overflowMenu.Opened += (s, e) => this.Invalidate();
            _overflowMenu.Closed += (s, e) => this.Invalidate();

            Rectangle overRect = GetOverflowButtonRect();
            if (!overRect.IsEmpty)
            {
                Point pt = this.PointToScreen(new Point(overRect.Left, overRect.Bottom + 2));
                _overflowMenu.Show(pt);
            }
        }

        #endregion

        #region Rendering

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Background
                Color topBg = _isIncognito ? Color.FromArgb(28, 29, 32) : MaterialColors.NavBarBackground;
                using (SolidBrush bgBrush = new SolidBrush(topBg))
                {
                    g.FillRectangle(bgBrush, this.ClientRectangle);
                }

                // Bottom border
                using (Pen borderPen = new Pen(MaterialColors.Separator, 1))
                {
                    g.DrawLine(borderPen, 0, this.Height - 1, this.Width, this.Height - 1);
                }

                // 1. Circular Back & Forward buttons
                DrawCircularNavButton(g, GetBackButtonRect(), "arrow-left", 16, _canGoBack, _hoveredBtn == ButtonId.Back, _pressedBtn == ButtonId.Back);
                DrawCircularNavButton(g, GetForwardButtonRect(), "arrow-right", 14, _canGoForward, _hoveredBtn == ButtonId.Forward, _pressedBtn == ButtonId.Forward);

                // 2. Address Box (OneBox)
                DrawAddressBox(g);

                // 3. Tab Strip
                DrawTabStrip(g);

                // 4. Dynamic Toolbar Buttons (Home, Star, Bookmarks, History, Downloads, Shield, Gear)
                DrawToolbarButtons(g);
            }
            catch
            {
                try
                {
                    using (SolidBrush bgBrush = new SolidBrush(MaterialColors.NavBarBackground))
                    {
                        e.Graphics.FillRectangle(bgBrush, this.ClientRectangle);
                    }
                }
                catch { }
            }
        }

        private void DrawCircularNavButton(Graphics g, Rectangle rect, string iconName, int iconSize, bool enabled, bool hovered, bool pressed)
        {
            if (enabled && (hovered || pressed))
            {
                Color circleBg = pressed ? MaterialColors.ButtonPressed : MaterialColors.ButtonHover;
                using (SolidBrush b = new SolidBrush(circleBg))
                {
                    g.FillEllipse(b, rect);
                }
                using (Pen p = new Pen(MaterialColors.Separator, 1))
                {
                    g.DrawEllipse(p, rect);
                }
            }

            Color iconColor = enabled ? (hovered ? MaterialColors.AccentBlue : MaterialColors.TextPrimary) : MaterialColors.TextDisabled;
            int scaledSize = Math.Max(12, (int)Math.Round(iconSize * _dpiScale));
            int ix = rect.Left + (rect.Width - scaledSize) / 2;
            int iy = rect.Top + (rect.Height - scaledSize) / 2;
            MaterialIcons.DrawIcon(g, iconName, new Rectangle(ix, iy, scaledSize, scaledSize), iconColor);
        }

        private void DrawAddressBox(Graphics g)
        {
            Rectangle rect = GetAddressBoxRect();
            int radius = Math.Max(2, (int)(3 * _dpiScale));

            // Background & Border
            using (GraphicsPath path = CreateRoundedRectangle(rect, radius))
            {
                using (SolidBrush b = new SolidBrush(MaterialColors.AddressBarBackground))
                {
                    g.FillPath(b, path);
                }
                using (Pen p = new Pen(MaterialColors.AddressBarBorder, 1))
                {
                    g.DrawPath(p, path);
                }
            }

            // Security Lock / Globe / Search icon on left
            string currentText = _addressTextBox != null ? _addressTextBox.Text : string.Empty;
            bool isHttps = currentText.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            bool isHttp = currentText.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

            string leftIconName;
            Color leftIconColor;

            if (isHttps)
            {
                leftIconName = "shield-check";
                leftIconColor = MaterialColors.AccentBlue;
            }
            else if (isHttp)
            {
                leftIconName = "globe";
                leftIconColor = ThemeManager.AccentColor;
            }
            else
            {
                leftIconName = "magnifying-glass";
                leftIconColor = MaterialColors.TextSecondary;
            }

            int iconLeft = rect.Left + (int)(6 * _dpiScale);
            int iconSide = Math.Max(12, (int)(16 * _dpiScale));
            int iconTop = rect.Top + (rect.Height - iconSide) / 2;
            MaterialIcons.DrawIcon(g, leftIconName, new Rectangle(iconLeft, iconTop, iconSide, iconSide), leftIconColor);

            // Go / Forward button (leftside of refresh button when editing)
            if (IsEditingAddressBox)
            {
                Rectangle goRect = GetGoButtonRect();
                if (!goRect.IsEmpty)
                {
                    bool gHover = (_hoveredBtn == ButtonId.Go);
                    if (gHover)
                    {
                        using (SolidBrush hb = new SolidBrush(MaterialColors.ButtonHover))
                        {
                            g.FillRectangle(hb, goRect);
                        }
                    }
                    int gSize = Math.Max(12, (int)(14 * _dpiScale));
                    int gx = goRect.Left + (goRect.Width - gSize) / 2;
                    int gy = goRect.Top + (goRect.Height - gSize) / 2;
                    MaterialIcons.DrawIcon(g, "arrow-right", new Rectangle(gx, gy, gSize, gSize), gHover ? MaterialColors.AccentBlue : MaterialColors.TextPrimary);
                }
            }

            // Refresh / Stop Icon button on right
            Rectangle refRect = GetRefreshButtonRect();
            bool rHover = (_hoveredBtn == ButtonId.Refresh);
            bool rPressed = (_pressedBtn == ButtonId.Refresh);
            if (rPressed)
            {
                using (SolidBrush pb = new SolidBrush(MaterialColors.ButtonPressed))
                {
                    g.FillRectangle(pb, refRect);
                }
            }
            else if (rHover)
            {
                using (SolidBrush hb = new SolidBrush(MaterialColors.ButtonHover))
                {
                    g.FillRectangle(hb, refRect);
                }
            }
            string rIcon = GetAddressActionIconName();
            int rSize = Math.Max(12, (int)(14 * _dpiScale));
            int rx = refRect.Left + (refRect.Width - rSize) / 2;
            int ry = refRect.Top + (refRect.Height - rSize) / 2;
            MaterialIcons.DrawIcon(g, rIcon, new Rectangle(rx, ry, rSize, rSize), (rHover || rPressed) ? MaterialColors.AccentBlue : MaterialColors.TextSecondary);
        }

        private void DrawTabStrip(Graphics g)
        {
            if (_boundTabControl == null || _boundTabControl.TabPages.Count == 0)
            {
                Rectangle emptyNewTab = GetNewTabButtonRect();
                DrawToolButton(g, emptyNewTab, "plus", 14, MaterialColors.TextSecondary, _hoveredBtn == ButtonId.NewTab, _pressedBtn == ButtonId.NewTab);
                return;
            }

            UpdateTabVisibility();

            int count = _boundTabControl.TabPages.Count;
            int selIdx = _boundTabControl.SelectedIndex;
            int pinnedCount = GetPinnedTabCount();

            using (Font tabFont = new Font("Segoe UI", Math.Max(8.5f, 9F * _dpiScale), FontStyle.Regular))
            {
                // 1. Draw Pinned Tabs (0 .. pinnedCount - 1)
                for (int i = 0; i < pinnedCount; i++)
                {
                    if (i >= count) break;

                    TabPage page = _boundTabControl.TabPages[i];
                    IBrowserTabState bTab = page as IBrowserTabState;
                    Rectangle tabRect = GetTabRect(i);
                    if (tabRect.IsEmpty) continue;

                    bool isSelected = (i == selIdx);
                    bool isHovered = (i == _hoveredTabIndex);
                    bool isDragged = (_isTabDragging && i == _dragTabIndex);

                    Color tabBg = isSelected ? MaterialColors.TabActive : (isHovered ? MaterialColors.TabHover : MaterialColors.TabInactive);
                    if (isDragged)
                    {
                        tabBg = _isTearOffPending ? Color.FromArgb(120, 60, 64, 70) : Color.FromArgb(220, 48, 54, 61);
                    }
                    using (SolidBrush tbBrush = new SolidBrush(tabBg))
                    {
                        g.FillRectangle(tbBrush, tabRect);
                    }

                    if (isDragged)
                    {
                        using (Pen dragPen = new Pen(_isTearOffPending ? MaterialColors.TextSecondary : MaterialColors.AccentBlue, 1.5f))
                        {
                            if (_isTearOffPending) dragPen.DashStyle = DashStyle.Dash;
                            g.DrawRectangle(dragPen, tabRect.Left + 1, tabRect.Top + 1, tabRect.Width - 2, tabRect.Height - 2);
                        }
                    }

                    if (isSelected)
                    {
                        using (SolidBrush accBrush = new SolidBrush(MaterialColors.AccentBlue))
                        {
                            g.FillRectangle(accBrush, tabRect.Left, tabRect.Top, tabRect.Width, Math.Max(2, (int)(2 * _dpiScale)));
                        }
                    }

                    using (Pen divPen = new Pen(MaterialColors.Separator, 1))
                    {
                        int divTop = isSelected ? tabRect.Top : tabRect.Top + (int)(4 * _dpiScale);
                        int divBottom = isSelected ? tabRect.Bottom : tabRect.Bottom - (int)(4 * _dpiScale);
                        g.DrawLine(divPen, tabRect.Left, divTop, tabRect.Left, divBottom);
                        g.DrawLine(divPen, tabRect.Right, divTop, tabRect.Right, divBottom);
                    }

                    int iconSize = Math.Max(14, (int)(16 * _dpiScale));
                    int iconX = tabRect.Left + (tabRect.Width - iconSize) / 2;
                    int iconY = tabRect.Top + (tabRect.Height - iconSize) / 2;
                    Rectangle iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);

                    bool isTabLoading = IsTabLoading(page);
                    bool hasAudio = (bTab != null && (bTab.IsPlayingAudio || bTab.IsAudioMuted));
                    if (isTabLoading)
                    {
                        DrawLoadingSpinner(g, iconRect, isSelected);
                    }
                    else if (hasAudio)
                    {
                        // Pinned tab with sound/muted shows ONLY the speaker icon instead of favicon
                        Color audioColor = bTab.IsAudioMuted ? MaterialColors.StatusDanger : (isSelected ? MaterialColors.AccentBlue : MaterialColors.TextPrimary);
                        DrawSpeakerIcon(g, iconRect, bTab.IsAudioMuted, audioColor);
                    }
                    else
                    {
                        // Pinned Favicon (centered)
                        ITabFaviconProvider tab = page as ITabFaviconProvider;
                        Image fav = (tab != null) ? tab.Favicon : null;
                        if (fav != null)
                        {
                            try
                            {
                                DpiDrawingHelper.DrawImagePixelPerfect(g, fav, iconRect);
                            }
                            catch { }
                        }
                        else
                        {
                            bool isStart = (tab != null && InternalPageHelper.IsStartPage(tab.CurrentUrl));
                            Color iconColor = ThemeManager.AccentColor;
                            MaterialIcons.DrawIcon(g, isStart ? "house" : "globe", iconRect, iconColor);
                        }
                    }

                    // Active mic/cam red badge on top left of pinned tab
                    if (bTab != null && bTab.IsMediaCaptured)
                    {
                        DrawMediaCaptureBadge(g, tabRect, tabBg, isSelected);
                    }
                }

                // 2. Draw Visible Unpinned Tabs
                for (int i = pinnedCount + _firstVisibleTab; i < pinnedCount + _firstVisibleTab + _visibleTabCount; i++)
                {
                    if (i < 0 || i >= count) continue;

                    TabPage page = _boundTabControl.TabPages[i];
                    IBrowserTabState bTab = page as IBrowserTabState;
                    Rectangle tabRect = GetTabRect(i);
                    if (tabRect.IsEmpty) continue;

                    bool isSelected = (i == selIdx);
                    bool isHovered = (i == _hoveredTabIndex);
                    bool isDragged = (_isTabDragging && i == _dragTabIndex);

                    // Tab Background & Borders
                    Color tabActiveCol = _isIncognito ? Color.FromArgb(41, 44, 49) : MaterialColors.TabActive;
                    Color tabHoverCol = _isIncognito ? Color.FromArgb(48, 51, 57) : MaterialColors.TabHover;
                    Color tabInactiveCol = _isIncognito ? Color.FromArgb(24, 25, 28) : MaterialColors.TabInactive;
                    Color tabBg = isSelected ? tabActiveCol : (isHovered ? tabHoverCol : tabInactiveCol);
                    if (isDragged)
                    {
                        tabBg = _isTearOffPending ? Color.FromArgb(120, 60, 64, 70) : Color.FromArgb(220, 48, 54, 61);
                    }
                    using (SolidBrush tbBrush = new SolidBrush(tabBg))
                    {
                        g.FillRectangle(tbBrush, tabRect);
                    }

                    // Dragged tab active/detach border visual cue
                    if (isDragged)
                    {
                        using (Pen dragPen = new Pen(_isTearOffPending ? MaterialColors.TextSecondary : MaterialColors.AccentBlue, 1.5f))
                        {
                            if (_isTearOffPending)
                            {
                                dragPen.DashStyle = DashStyle.Dash;
                            }
                            g.DrawRectangle(dragPen, tabRect.Left + 1, tabRect.Top + 1, tabRect.Width - 2, tabRect.Height - 2);
                        }
                    }

                    // Active tab top accent blue indicator line
                    if (isSelected)
                    {
                        using (SolidBrush accBrush = new SolidBrush(MaterialColors.AccentBlue))
                        {
                            g.FillRectangle(accBrush, tabRect.Left, tabRect.Top, tabRect.Width, Math.Max(2, (int)(2 * _dpiScale)));
                        }
                    }

                    // Tab vertical divider or border
                    using (Pen divPen = new Pen(MaterialColors.Separator, 1))
                    {
                        int divTop = isSelected ? tabRect.Top : tabRect.Top + (int)(4 * _dpiScale);
                        int divBottom = isSelected ? tabRect.Bottom : tabRect.Bottom - (int)(4 * _dpiScale);
                        g.DrawLine(divPen, tabRect.Left, divTop, tabRect.Left, divBottom);
                        if (i == pinnedCount + _firstVisibleTab + _visibleTabCount - 1 || isSelected)
                        {
                            g.DrawLine(divPen, tabRect.Right, divTop, tabRect.Right, divBottom);
                        }
                    }

                    // Tab Icon (Website Favicon, loading spinner, or placeholder)
                    int iconSize = Math.Max(12, (int)(16 * _dpiScale));
                    int iconPad = (tabRect.Width < (int)(52 * _dpiScale)) ? (int)(5 * _dpiScale) : (int)(8 * _dpiScale);
                    int iconX = tabRect.Left + iconPad;
                    int iconY = tabRect.Top + (tabRect.Height - iconSize) / 2;
                    Rectangle iconBounds = new Rectangle(iconX, iconY, iconSize, iconSize);

                    bool isTabLoading = IsTabLoading(page);
                    if (isTabLoading)
                    {
                        DrawLoadingSpinner(g, iconBounds, isSelected);
                    }
                    else
                    {
                        ITabFaviconProvider tab = page as ITabFaviconProvider;
                        Image fav = (tab != null) ? tab.Favicon : null;
                        if (fav != null)
                        {
                            try
                            {
                                DpiDrawingHelper.DrawImagePixelPerfect(g, fav, iconBounds);
                            }
                            catch { }
                        }
                        else
                        {
                            bool isStart = (tab != null && InternalPageHelper.IsStartPage(tab.CurrentUrl));
                            Color iconColor = ThemeManager.AccentColor;
                            MaterialIcons.DrawIcon(g, isStart ? "house" : "globe", iconBounds, iconColor);
                        }
                    }

                    // Close Button ('x')
                    Rectangle closeRect = GetTabCloseRect(i);
                    bool isCloseHover = (i == _hoveredCloseIndex);
                    if (!closeRect.IsEmpty)
                    {
                        if (isCloseHover)
                        {
                            using (SolidBrush cbBrush = new SolidBrush(Color.FromArgb(180, 242, 139, 130)))
                            {
                                int r = Math.Max(2, (int)(3 * _dpiScale));
                                using (GraphicsPath cp = CreateRoundedRectangle(closeRect, r))
                                {
                                    g.FillPath(cbBrush, cp);
                                }
                            }
                        }
                        int cSize = Math.Max(10, (int)(12 * _dpiScale));
                        int cx = closeRect.Left + (closeRect.Width - cSize) / 2;
                        int cy = closeRect.Top + (closeRect.Height - cSize) / 2;
                        Color closeColor = isCloseHover ? Color.White : (isSelected ? MaterialColors.TextPrimary : MaterialColors.TextSecondary);
                        MaterialIcons.DrawIcon(g, "x", new Rectangle(cx, cy, cSize, cSize), closeColor);
                    }

                    // Audio indicator
                    Rectangle audioRect = GetTabAudioRect(i);
                    if (!audioRect.IsEmpty && bTab != null)
                    {
                        Color audioColor = bTab.IsAudioMuted ? MaterialColors.StatusDanger : (isSelected ? MaterialColors.AccentBlue : MaterialColors.TextSecondary);
                        DrawSpeakerIcon(g, audioRect, bTab.IsAudioMuted, audioColor);
                    }

                    // Tab Title text
                    int textLeft = iconX + iconSize + (int)(6 * _dpiScale);
                    int rightAnchor = !audioRect.IsEmpty ? audioRect.Left : (!closeRect.IsEmpty ? closeRect.Left : tabRect.Right);
                    int textRight = rightAnchor - (int)(4 * _dpiScale);
                    int textW = Math.Max(0, textRight - textLeft);
                    if (textW > 0)
                    {
                        Rectangle textRect = new Rectangle(textLeft, tabRect.Top, textW, tabRect.Height);
                        Color txtColor = isSelected ? MaterialColors.TextPrimary : MaterialColors.TextSecondary;
                        TextRenderer.DrawText(g, page.Text, tabFont, textRect, txtColor,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    }

                    // Active mic/cam red badge on top left of unpinned tab
                    if (bTab != null && bTab.IsMediaCaptured)
                    {
                        DrawMediaCaptureBadge(g, tabRect, tabBg, isSelected);
                    }
                }
            }

            // 5. Draw Tab2Tab drop insertion indicator marker
            if (_isTabDragging && !_isTearOffPending && _dropTargetIndex >= 0)
            {
                Rectangle targetRect = GetTabRect(_dropTargetIndex);
                if (!targetRect.IsEmpty)
                {
                    int markerX = _dropTargetAfter ? targetRect.Right : targetRect.Left;
                    using (Pen markerPen = new Pen(MaterialColors.AccentBlue, 2.5f))
                    {
                        g.DrawLine(markerPen, markerX, targetRect.Top, markerX, targetRect.Bottom);
                    }

                    using (SolidBrush arrowBrush = new SolidBrush(MaterialColors.AccentBlue))
                    {
                        int triSize = Math.Max(4, (int)(5 * _dpiScale));
                        Point[] topTri = new Point[]
                        {
                            new Point(markerX - triSize, targetRect.Top),
                            new Point(markerX + triSize, targetRect.Top),
                            new Point(markerX, targetRect.Top + triSize + 2)
                        };
                        g.FillPolygon(arrowBrush, topTri);

                        Point[] botTri = new Point[]
                        {
                            new Point(markerX - triSize, targetRect.Bottom),
                            new Point(markerX + triSize, targetRect.Bottom),
                            new Point(markerX, targetRect.Bottom - triSize - 2)
                        };
                        g.FillPolygon(arrowBrush, botTri);
                    }
                }
            }

            // 6. Draw Window2Tab drop highlight banner
            if (_dropHighlight)
            {
                Rectangle stripRect = GetTabStripRect();
                if (!stripRect.IsEmpty)
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(240, 32, 33, 36)))
                    {
                        g.FillRectangle(bg, stripRect);
                    }
                    using (SolidBrush glow = new SolidBrush(Color.FromArgb(40, MaterialColors.AccentBlue)))
                    {
                        g.FillRectangle(glow, stripRect);
                    }
                    using (Pen dropPen = new Pen(MaterialColors.AccentBlue, 2f))
                    {
                        dropPen.DashStyle = DashStyle.Dash;
                        g.DrawRectangle(dropPen, stripRect.Left + 1, stripRect.Top + 1, stripRect.Width - 2, stripRect.Height - 2);
                    }
                    using (Font cueFont = new Font("Segoe UI", Math.Max(8.5f, 9F * _dpiScale), FontStyle.Bold))
                    {
                        TextRenderer.DrawText(g, AppStrings.Browser.DropTabCue, cueFont, stripRect,
                            MaterialColors.AccentBlue, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }
                }
            }

            // New Tab ('+') Button
            Rectangle newTabRect = GetNewTabButtonRect();
            bool ntHover = (_hoveredBtn == ButtonId.NewTab);
            bool ntPress = (_pressedBtn == ButtonId.NewTab);
            if (ntHover || ntPress)
            {
                Color ntBg = ntPress ? MaterialColors.ButtonPressed : MaterialColors.ButtonHover;
                using (SolidBrush b = new SolidBrush(ntBg))
                {
                    g.FillRectangle(b, newTabRect);
                }
                using (Pen p = new Pen(MaterialColors.Separator, 1))
                {
                    g.DrawRectangle(p, newTabRect);
                }
            }
            int plusSize = Math.Max(12, (int)(14 * _dpiScale));
            int px = newTabRect.Left + (newTabRect.Width - plusSize) / 2;
            int py = newTabRect.Top + (newTabRect.Height - plusSize) / 2;
            MaterialIcons.DrawIcon(g, "plus", new Rectangle(px, py, plusSize, plusSize), ntHover ? MaterialColors.AccentBlue : MaterialColors.TextSecondary);

            // Overflow ('caret-down') Button
            if (_hasOverflow)
            {
                Rectangle overRect = GetOverflowButtonRect();
                bool ovOpen = (_overflowMenu != null && _overflowMenu.Visible);
                bool ovHover = (_hoveredBtn == ButtonId.Overflow);
                bool ovPress = (_pressedBtn == ButtonId.Overflow);
                bool ovActive = ovHover || ovPress || ovOpen;
                if (ovActive)
                {
                    Color ovBg = (ovPress || ovOpen) ? MaterialColors.ButtonPressed : MaterialColors.ButtonHover;
                    using (SolidBrush b = new SolidBrush(ovBg))
                    {
                        g.FillRectangle(b, overRect);
                    }
                    using (Pen p = new Pen(MaterialColors.Separator, 1))
                    {
                        g.DrawRectangle(p, overRect);
                    }
                }
                int caretSize = Math.Max(10, (int)(12 * _dpiScale));
                int cx = overRect.Left + (overRect.Width - caretSize) / 2;
                int cy = overRect.Top + (overRect.Height - caretSize) / 2;
                MaterialIcons.DrawIcon(g, "caret-down", new Rectangle(cx, cy, caretSize, caretSize), ovActive ? MaterialColors.AccentBlue : MaterialColors.TextSecondary);
            }
        }

        public bool IsTabLoading(TabPage page)
        {
            if (page == null) return false;
            ITabFaviconProvider tfp = page as ITabFaviconProvider;
            if (tfp != null && tfp.IsLoading) return true;
            IBrowserTabState bts = page as IBrowserTabState;
            if (bts != null && bts.IsLoading) return true;
            if (_boundTabControl != null && page == _boundTabControl.SelectedTab && _isLoading) return true;
            return false;
        }

        public void CheckSpinnerAnimationState()
        {
            if (this.IsDisposed) return;

            bool anyLoading = false;
            if (_isLoading)
            {
                anyLoading = true;
            }
            else if (_boundTabControl != null && _boundTabControl.TabPages != null)
            {
                for (int i = 0; i < _boundTabControl.TabPages.Count; i++)
                {
                    if (IsTabLoading(_boundTabControl.TabPages[i]))
                    {
                        anyLoading = true;
                        break;
                    }
                }
            }

            if (anyLoading)
            {
                if (_spinnerTimer == null)
                {
                    _spinnerTimer = new Timer();
                    _spinnerTimer.Interval = 25; // 40 FPS smooth rotation
                    _spinnerTimer.Tick += SpinnerTimer_Tick;
                }
                if (!_spinnerTimer.Enabled)
                {
                    _spinnerTimer.Start();
                }
            }
            else
            {
                if (_spinnerTimer != null && _spinnerTimer.Enabled)
                {
                    _spinnerTimer.Stop();
                }
            }
        }

        private void SpinnerTimer_Tick(object sender, EventArgs e)
        {
            if (this.IsDisposed)
            {
                if (_spinnerTimer != null) { _spinnerTimer.Stop(); _spinnerTimer.Dispose(); _spinnerTimer = null; }
                return;
            }

            bool anyLoading = false;
            if (_isLoading)
            {
                anyLoading = true;
            }
            else if (_boundTabControl != null && _boundTabControl.TabPages != null)
            {
                for (int i = 0; i < _boundTabControl.TabPages.Count; i++)
                {
                    if (IsTabLoading(_boundTabControl.TabPages[i]))
                    {
                        anyLoading = true;
                        break;
                    }
                }
            }

            if (!anyLoading)
            {
                if (_spinnerTimer != null) _spinnerTimer.Stop();
                this.Invalidate();
                return;
            }

            _spinnerAngle = (_spinnerAngle + 12f) % 360f;

            Rectangle strip = GetTabStripRect();
            if (!strip.IsEmpty)
            {
                this.Invalidate(strip);
            }
            else
            {
                this.Invalidate();
            }
        }

        private void DrawLoadingSpinner(Graphics g, Rectangle bounds, bool isSelected)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0 || g == null) return;

            SmoothingMode oldSmooth = g.SmoothingMode;
            PixelOffsetMode oldPixel = g.PixelOffsetMode;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                float pad = Math.Max(1.0f, 1.5f * _dpiScale);
                RectangleF spinnerRect = new RectangleF(
                    bounds.X + pad,
                    bounds.Y + pad,
                    bounds.Width - pad * 2f,
                    bounds.Height - pad * 2f);

                if (spinnerRect.Width <= 2 || spinnerRect.Height <= 2) return;

                float penWidth = Math.Max(1.5f, 1.8f * _dpiScale);

                // 1. Subtle ambient circular track
                using (Pen trackPen = new Pen(Color.FromArgb(45, 255, 255, 255), penWidth))
                {
                    g.DrawEllipse(trackPen, spinnerRect);
                }

                // 2. Dynamic rotating arc
                Color arcColor = isSelected ? ThemeManager.CurrentAccentColor : Color.FromArgb(200, ThemeManager.CurrentAccentColor);
                using (Pen arcPen = new Pen(arcColor, penWidth))
                {
                    arcPen.StartCap = LineCap.Round;
                    arcPen.EndCap = LineCap.Round;
                    g.DrawArc(arcPen, spinnerRect, _spinnerAngle, 240f);
                }
            }
            finally
            {
                g.SmoothingMode = oldSmooth;
                g.PixelOffsetMode = oldPixel;
            }
        }

        private void DrawSpeakerIcon(Graphics g, Rectangle r, bool isMuted, Color color)
        {
            if (r.Width <= 4 || r.Height <= 4) return;
            string iconName = isMuted ? "speaker-slash" : "speaker-high";
            int iconSize = Math.Max(12, Math.Min(r.Width, r.Height));
            int ix = r.Left + (r.Width - iconSize) / 2;
            int iy = r.Top + (r.Height - iconSize) / 2;
            MaterialIcons.DrawIcon(g, iconName, new Rectangle(ix, iy, iconSize, iconSize), color);
        }

        private void DrawMediaCaptureBadge(Graphics g, Rectangle tabRect, Color tabBg, bool isSelected)
        {
            if (tabRect.IsEmpty || g == null) return;
            int badgeSize = Math.Max(4, (int)(5 * _dpiScale));
            int bx = tabRect.Left + (int)(4 * _dpiScale);
            int by = tabRect.Top + Math.Max(5, (int)(5 * _dpiScale));
            Rectangle badgeRect = new Rectangle(bx, by, badgeSize, badgeSize);

            SmoothingMode oldSmooth = g.SmoothingMode;
            PixelOffsetMode oldPixel = g.PixelOffsetMode;
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Subtle outer boundary matching tab background for crisp separation
                Rectangle ringRect = Rectangle.Inflate(badgeRect, 1, 1);
                using (SolidBrush ringBrush = new SolidBrush(tabBg))
                {
                    g.FillEllipse(ringBrush, ringRect);
                }

                // Clean flat Material Design recording pip (MaterialColors.StatusDanger #F28B82)
                using (SolidBrush dotBrush = new SolidBrush(MaterialColors.StatusDanger))
                {
                    g.FillEllipse(dotBrush, badgeRect);
                }
            }
            finally
            {
                g.SmoothingMode = oldSmooth;
                g.PixelOffsetMode = oldPixel;
            }
        }

        private void DrawToolbarButtons(Graphics g)
        {
            var rects = GetToolbarButtonRectangles();
            List<string> visible = GetVisibleToolbarButtons();

            for (int i = 0; i < visible.Count; i++)
            {
                string id = visible[i];
                Rectangle r;
                if (!rects.TryGetValue(id, out r)) continue;

                if (_isToolbarDragging && string.Equals(id, _dragToolbarButtonId, StringComparison.OrdinalIgnoreCase))
                {
                    int rad = Math.Max(2, (int)(3 * _dpiScale));
                    using (GraphicsPath gp = CreateRoundedRectangle(r, rad))
                    {
                        using (Pen ghostPen = new Pen(Color.FromArgb(90, MaterialColors.AccentBlue), 1f))
                        {
                            ghostPen.DashStyle = DashStyle.Dash;
                            g.DrawPath(ghostPen, gp);
                        }
                    }
                }
                else
                {
                    ButtonId bid = ToolbarIdToButtonId(id);
                    bool isHovered = (_hoveredBtn == bid);
                    bool isPressed = (_pressedBtn == bid);
                    DrawIndividualToolbarButton(g, id, r, isHovered, isPressed);
                }
            }

            // Fixed Tools (gear) button
            bool gearActive = (_hoveredBtn == ButtonId.Gear) || (_pressedBtn == ButtonId.Gear) || _isGearMenuOpen;
            Color gearColor = gearActive ? MaterialColors.AccentBlue : MaterialColors.TextPrimary;
            DrawToolButton(g, GetGearButtonRect(), "gear", 16, gearColor, gearActive, _pressedBtn == ButtonId.Gear || _isGearMenuOpen);

            // If dragging, draw insertion cue line and floating dragged button
            if (_isToolbarDragging && !string.IsNullOrEmpty(_dragToolbarButtonId))
            {
                int cueX = GetToolbarDropCueX();
                int baseSize = (int)(28 * _dpiScale);
                int cueY = (this.Height - baseSize) / 2;

                using (Pen cuePen = new Pen(MaterialColors.AccentBlue, 2.5f))
                {
                    g.DrawLine(cuePen, cueX, cueY - 2, cueX, cueY + baseSize + 2);
                }

                using (SolidBrush cueBrush = new SolidBrush(MaterialColors.AccentBlue))
                {
                    int triSize = Math.Max(3, (int)(4 * _dpiScale));
                    Point[] topTri = new Point[]
                    {
                        new Point(cueX - triSize, cueY - 2),
                        new Point(cueX + triSize, cueY - 2),
                        new Point(cueX, cueY + triSize)
                    };
                    g.FillPolygon(cueBrush, topTri);

                    Point[] botTri = new Point[]
                    {
                        new Point(cueX - triSize, cueY + baseSize + 2),
                        new Point(cueX + triSize, cueY + baseSize + 2),
                        new Point(cueX, cueY + baseSize + 2 - triSize - 2)
                    };
                    g.FillPolygon(cueBrush, botTri);
                }

                // Floating dragged button
                int floatW = string.Equals(_dragToolbarButtonId, "Shield", StringComparison.OrdinalIgnoreCase) ? GetShieldButtonWidth() : baseSize;
                int floatX = _toolbarDragCurrentPt.X - floatW / 2;
                Rectangle floatRect = new Rectangle(floatX, cueY, floatW, baseSize);

                using (SolidBrush floatBg = new SolidBrush(Color.FromArgb(235, 45, 51, 59)))
                {
                    int rad = Math.Max(2, (int)(3 * _dpiScale));
                    using (GraphicsPath gp = CreateRoundedRectangle(floatRect, rad))
                    {
                        g.FillPath(floatBg, gp);
                    }
                }
                using (Pen floatBorder = new Pen(MaterialColors.AccentBlue, 1.5f))
                {
                    int rad = Math.Max(2, (int)(3 * _dpiScale));
                    using (GraphicsPath gp = CreateRoundedRectangle(floatRect, rad))
                    {
                        g.DrawPath(floatBorder, gp);
                    }
                }
                DrawIndividualToolbarButton(g, _dragToolbarButtonId, floatRect, true, true);
            }
        }

        private void DrawIndividualToolbarButton(Graphics g, string id, Rectangle rect, bool hovered, bool pressed)
        {
            ButtonId btnId = ToolbarIdToButtonId(id);
            switch (btnId)
            {
                case ButtonId.Home:
                    string homeIcon = _isIncognito ? "incognito" : "house";
                    DrawToolButton(g, rect, homeIcon, 16, MaterialColors.TextPrimary, hovered, pressed);
                    break;
                case ButtonId.Star:
                    if (!_canBookmark)
                    {
                        DrawToolButton(g, rect, "star", 16, MaterialColors.TextDisabled, false, false);
                    }
                    else
                    {
                        Color starColor = _isBookmarked ? MaterialColors.AccentBlue : MaterialColors.TextPrimary;
                        string starIcon = _isBookmarked ? "star-fill" : "star";
                        DrawToolButton(g, rect, starIcon, 16, starColor, hovered, pressed);
                    }
                    break;
                case ButtonId.Bookmarks:
                    DrawToolButton(g, rect, "bookmarks", 16, MaterialColors.TextPrimary, hovered, pressed);
                    break;
                case ButtonId.History:
                    DrawToolButton(g, rect, "clock-counter-clockwise", 16, MaterialColors.TextPrimary, hovered, pressed);
                    break;
                case ButtonId.Downloads:
                    bool downloadsHoveredOrPressed = hovered || pressed;
                    Color downloadColor = (_activeDownloadCount > 0 || downloadsHoveredOrPressed) ? MaterialColors.AccentBlue : MaterialColors.TextPrimary;
                    DrawDownloadButton(g, rect, "download-simple", 16, downloadColor, hovered, pressed);
                    break;
                case ButtonId.Shield:
                    bool isAdBlockActive = _isAdBlockEnabled &&
                        !string.Equals(_adBlockStatusSuffix, AppStrings.Browser.ShieldDisabledSiteSuffix, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(_adBlockStatusSuffix, AppStrings.Browser.ShieldDisabledSuffix, StringComparison.OrdinalIgnoreCase);
                    bool shieldActive = hovered || pressed || _isShieldMenuOpen;
                    Color shieldColor = (shieldActive || isAdBlockActive) ? MaterialColors.AccentBlue : MaterialColors.TextPrimary;
                    string shieldIcon = isAdBlockActive ? "shield-fill" : "shield";
                    DrawShieldButton(g, rect, shieldIcon, 16, shieldColor, shieldActive, pressed || _isShieldMenuOpen);
                    break;
                case ButtonId.Screenshot:
                    DrawToolButton(g, rect, "camera", 16, MaterialColors.TextPrimary, hovered, pressed);
                    break;
                default:
                    DrawToolButton(g, rect, GetButtonIconName(id), 16, MaterialColors.TextPrimary, hovered, pressed);
                    break;
            }
        }

        private void DrawToolButton(Graphics g, Rectangle rect, string iconName, int iconSize, Color iconColor, bool hovered, bool pressed)
        {
            if (hovered || pressed)
            {
                Color bg = pressed ? MaterialColors.ButtonPressed : MaterialColors.ButtonHover;
                using (SolidBrush b = new SolidBrush(bg))
                {
                    int r = Math.Max(2, (int)(3 * _dpiScale));
                    using (GraphicsPath path = CreateRoundedRectangle(rect, r))
                    {
                        g.FillPath(b, path);
                    }
                }
            }

            int sSize = Math.Max(12, (int)Math.Round(iconSize * _dpiScale));
            int x = rect.Left + (rect.Width - sSize) / 2;
            int y = rect.Top + (rect.Height - sSize) / 2;
            MaterialIcons.DrawIcon(g, iconName, new Rectangle(x, y, sSize, sSize), hovered ? MaterialColors.AccentBlue : iconColor);
        }

        private void DrawDownloadButton(Graphics g, Rectangle rect, string iconName, int iconSize, Color iconColor, bool hovered, bool pressed)
        {
            if (hovered || pressed)
            {
                Color bg = pressed ? MaterialColors.ButtonPressed : MaterialColors.ButtonHover;
                using (SolidBrush b = new SolidBrush(bg))
                {
                    int r = Math.Max(2, (int)(3 * _dpiScale));
                    using (GraphicsPath path = CreateRoundedRectangle(rect, r))
                    {
                        g.FillPath(b, path);
                    }
                }
            }

            int sSize = Math.Max(12, (int)Math.Round(iconSize * _dpiScale));
            int iconX = rect.Left + (rect.Width - sSize) / 2;
            int iconY = rect.Top + (rect.Height - sSize) / 2;
            MaterialIcons.DrawIcon(g, iconName, new Rectangle(iconX, iconY, sSize, sSize), hovered ? MaterialColors.AccentBlue : iconColor);

            if (_activeDownloadCount > 0)
            {
                string countStr = _activeDownloadCount > 999 ? AppStrings.Browser.BadgeOverflowCount : _activeDownloadCount.ToString();
                using (Font badgeFont = new Font("Segoe UI", Math.Max(6.5f, 7f * _dpiScale), FontStyle.Bold))
                {
                    Size textSize = TextRenderer.MeasureText(g, countStr, badgeFont, new Size(rect.Width, rect.Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    int badgeH = Math.Max(10, (int)(11 * _dpiScale));
                    int badgeW = Math.Max((int)(11 * _dpiScale), textSize.Width + (int)(3 * _dpiScale));

                    int badgeRight = rect.Right - (int)(2 * _dpiScale);
                    int badgeBottom = rect.Bottom - (int)(2 * _dpiScale);
                    Rectangle badgeRect = new Rectangle(badgeRight - badgeW, badgeBottom - badgeH, badgeW, badgeH);

                    int badgeRadius = Math.Max(1, (int)(2 * _dpiScale));
                    using (GraphicsPath badgePath = CreateRoundedRectangle(badgeRect, badgeRadius))
                    {
                        using (SolidBrush badgeBg = new SolidBrush(UITheme.BadgeBackground))
                        {
                            g.FillPath(badgeBg, badgePath);
                        }
                        using (Pen badgeRing = new Pen(Color.FromArgb(32, 33, 36), 1f))
                        {
                            g.DrawPath(badgeRing, badgePath);
                        }
                    }

                    Rectangle textRect = new Rectangle(badgeRect.Left, badgeRect.Top - 1, badgeRect.Width, badgeRect.Height);
                    TextRenderer.DrawText(g, countStr, badgeFont, textRect, UITheme.BadgeText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
        }

        private void DrawShieldButton(Graphics g, Rectangle rect, string iconName, int iconSize, Color iconColor, bool hovered, bool pressed)
        {
            if (hovered || pressed)
            {
                Color bg = pressed ? MaterialColors.ButtonPressed : MaterialColors.ButtonHover;
                using (SolidBrush b = new SolidBrush(bg))
                {
                    int r = Math.Max(2, (int)(3 * _dpiScale));
                    using (GraphicsPath path = CreateRoundedRectangle(rect, r))
                    {
                        g.FillPath(b, path);
                    }
                }
            }

            int sSize = Math.Max(12, (int)Math.Round(iconSize * _dpiScale));
            int iconX = rect.Left + (rect.Width - sSize) / 2;
            int iconY = rect.Top + (rect.Height - sSize) / 2;
            MaterialIcons.DrawIcon(g, iconName, new Rectangle(iconX, iconY, sSize, sSize), hovered ? MaterialColors.AccentBlue : iconColor);

            bool showDash = _isAdBlockEnabled && IsInternalPage;
            if (_blockedCount > 0 || showDash)
            {
                string countStr = showDash ? "-" : (_blockedCount > 999 ? AppStrings.Browser.BadgeOverflowCount : _blockedCount.ToString());
                using (Font badgeFont = new Font("Segoe UI", Math.Max(6.5f, 7f * _dpiScale), FontStyle.Bold))
                {
                    Size textSize = TextRenderer.MeasureText(g, countStr, badgeFont, new Size(rect.Width, rect.Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    int badgeH = Math.Max(10, (int)(11 * _dpiScale));
                    int badgeW = Math.Max((int)(11 * _dpiScale), textSize.Width + (int)(3 * _dpiScale));

                    int badgeRight = rect.Right - (int)(2 * _dpiScale);
                    int badgeBottom = rect.Bottom - (int)(2 * _dpiScale);
                    Rectangle badgeRect = new Rectangle(badgeRight - badgeW, badgeBottom - badgeH, badgeW, badgeH);

                    int badgeRadius = Math.Max(1, (int)(2 * _dpiScale));
                    using (GraphicsPath badgePath = CreateRoundedRectangle(badgeRect, badgeRadius))
                    {
                        using (SolidBrush badgeBg = new SolidBrush(UITheme.BadgeBackground)) // uBlock #666666
                        {
                            g.FillPath(badgeBg, badgePath);
                        }
                        using (Pen badgeRing = new Pen(Color.FromArgb(32, 33, 36), 1f))
                        {
                            g.DrawPath(badgeRing, badgePath);
                        }
                    }

                    Rectangle textRect = new Rectangle(badgeRect.Left, badgeRect.Top - 1, badgeRect.Width, badgeRect.Height);
                    TextRenderer.DrawText(g, countStr, badgeFont, textRect, UITheme.BadgeText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            return UITheme.CreateRoundedPath(rect, radius);
        }

        private void DrawIncognitoBadge(Graphics g)
        {
            Rectangle rect = GetIncognitoBadgeRect();
            if (rect.IsEmpty) return;

            bool isHovered = (_hoveredBtn == ButtonId.IncognitoBadge);
            bool isPressed = (_pressedBtn == ButtonId.IncognitoBadge);
            int radius = Math.Max(2, (int)(3 * _dpiScale));

            Color bg = isPressed ? MaterialColors.ButtonPressed : (isHovered ? MaterialColors.ButtonHover : Color.FromArgb(41, 44, 49));
            Color border = isHovered ? Color.FromArgb(90, 95, 105) : Color.FromArgb(55, 58, 64);

            using (GraphicsPath path = CreateRoundedRectangle(rect, radius))
            {
                using (SolidBrush b = new SolidBrush(bg))
                {
                    g.FillPath(b, path);
                }
                using (Pen p = new Pen(border, 1f))
                {
                    g.DrawPath(p, path);
                }
            }

            Color iconColor = isHovered ? MaterialColors.AccentBlue : Color.FromArgb(232, 234, 237);
            Color textColor = isHovered ? MaterialColors.AccentBlue : Color.FromArgb(232, 234, 237);

            int iconSize = Math.Max(14, (int)(16 * _dpiScale));
            int iconX = rect.Left + (int)(6 * _dpiScale);
            int iconY = rect.Top + (rect.Height - iconSize) / 2;
            Rectangle iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);
            MaterialIcons.DrawIcon(g, "incognito", iconRect, iconColor);

            if (rect.Width > (int)(45 * _dpiScale))
            {
                int textLeft = iconX + iconSize + (int)(5 * _dpiScale);
                int textW = Math.Max(0, rect.Right - textLeft - (int)(6 * _dpiScale));
                Rectangle textRect = new Rectangle(textLeft, rect.Top, textW, rect.Height);
                using (Font font = new Font("Segoe UI", Math.Max(8.5f, 9f * _dpiScale), FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, AppStrings.Browser.IncognitoBadge, font, textRect, textColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }

        #endregion

        public string GetCurrentToolTip()
        {
            return _activeToolTipText;
        }

        private void LongPressTimer_Tick(object sender, EventArgs e)
        {
            if (_longPressTimer != null) _longPressTimer.Stop();
            if (_pressedBtn == _longPressBtn && ((_longPressBtn == ButtonId.Back && _canGoBack) || (_longPressBtn == ButtonId.Forward && _canGoForward)))
            {
                _longPressFired = true;
                ButtonId target = _longPressBtn;
                _pressedBtn = ButtonId.None;
                this.Invalidate();
                if (target == ButtonId.Back)
                {
                    if (BackLongPressed != null) BackLongPressed(this, EventArgs.Empty);
                }
                else if (target == ButtonId.Forward)
                {
                    if (ForwardLongPressed != null) ForwardLongPressed(this, EventArgs.Empty);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_spinnerTimer != null) { try { _spinnerTimer.Stop(); _spinnerTimer.Dispose(); } catch { } _spinnerTimer = null; }
                if (_suggestionPopup != null) { try { _suggestionPopup.Dispose(); } catch { } _suggestionPopup = null; }
                if (_longPressTimer != null) { try { _longPressTimer.Stop(); _longPressTimer.Dispose(); } catch { } _longPressTimer = null; }
                if (_addressContextMenu != null) { try { _addressContextMenu.Dispose(); } catch { } _addressContextMenu = null; }
                if (_toolTip != null) { try { _toolTip.Dispose(); } catch { } _toolTip = null; }
            }
            base.Dispose(disposing);
        }
    }
}
