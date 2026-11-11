using System;

namespace wb.Core
{
    public static class AppStrings
    {
        // General Brand Constants
        public const string AppName = "wb";
        public const string BaseVersion = "1.0.0";
        public const string AppVersionShort = "1.0";

        // Dynamically resolved from the runtime assembly wb was compiled with
        public static readonly string CefSharpVersion = DetectCefSharpVersion();
        public static readonly string EngineVersion = CefSharpVersion;
        public static readonly string AppVersion = string.IsNullOrEmpty(CefSharpVersion) ? BaseVersion : (BaseVersion + "-" + CefSharpVersion);
        public static readonly string AppVersionDisplay = "v" + AppVersion;
        public static readonly string AppArchitecture = Environment.Is64BitProcess ? "x64" : "x86";
        public static readonly string AppArchitectureDisplay = "(" + AppArchitecture + ")";
        public const string AppTitle = AppName + " Browser";
        public const string AppSubtitle = "High Performance Web Browser for .NET Framework";
        public const string AppFooter = "Compiled via <span>Windows csc.exe</span> &bull; Powered by <span>wb</span>";
        public const string AppErrorTitle = AppName + " Error";
        public static readonly string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 " + AppName + "/" + AppVersion + " (uBlock Engine)";
        public static readonly string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " + AppName + "/" + AppVersion + " Browser";

        private static string DetectCefSharpVersion()
        {
            try
            {
                // 1. Check referenced assemblies in the current assembly manifest (contains exact version wb was compiled with)
                System.Reflection.Assembly currentAsm = typeof(AppStrings).Assembly;
                if (currentAsm != null)
                {
                    System.Reflection.AssemblyName[] refs = currentAsm.GetReferencedAssemblies();
                    if (refs != null)
                    {
                        for (int i = 0; i < refs.Length; i++)
                        {
                            if (refs[i] != null && refs[i].Name != null &&
                                refs[i].Name.Equals("CefSharp", StringComparison.OrdinalIgnoreCase))
                            {
                                if (refs[i].Version != null)
                                {
                                    return refs[i].Version.ToString();
                                }
                            }
                        }
                    }
                }

                // 2. Check currently loaded assemblies in the AppDomain
                System.Reflection.Assembly[] loadedAsms = AppDomain.CurrentDomain.GetAssemblies();
                if (loadedAsms != null)
                {
                    for (int i = 0; i < loadedAsms.Length; i++)
                    {
                        if (loadedAsms[i] != null)
                        {
                            System.Reflection.AssemblyName name = loadedAsms[i].GetName();
                            if (name != null && name.Name != null &&
                                name.Name.Equals("CefSharp", StringComparison.OrdinalIgnoreCase))
                            {
                                if (name.Version != null)
                                {
                                    return name.Version.ToString();
                                }
                            }
                        }
                    }
                }

                // 3. Check wb.dll or CefSharp.dll alongside executable
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir))
                {
                    string localWb = System.IO.Path.Combine(baseDir, "wb.dll");
                    if (System.IO.File.Exists(localWb))
                    {
                        System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(localWb);
                        if (!string.IsNullOrEmpty(fvi.FileVersion)) return fvi.FileVersion;
                    }

                    string localCef = System.IO.Path.Combine(baseDir, "CefSharp.dll");
                    if (System.IO.File.Exists(localCef))
                    {
                        System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(localCef);
                        if (!string.IsNullOrEmpty(fvi.FileVersion)) return fvi.FileVersion;
                    }
                }

                // 4. Search parent directories for packages\CefSharp* (e.g. running from bin\tests or scratch)
                string dir = baseDir;
                for (int level = 0; level < 5 && !string.IsNullOrEmpty(dir); level++)
                {
                    string h264x64 = System.IO.Path.Combine(dir, @"packages\CefSharp.H264.x64\CefSharp.dll");
                    if (System.IO.File.Exists(h264x64))
                    {
                        System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(h264x64);
                        if (!string.IsNullOrEmpty(fvi.FileVersion)) return fvi.FileVersion;
                    }

                    string h264x86 = System.IO.Path.Combine(dir, @"packages\CefSharp.H264.x86\CefSharp.dll");
                    if (System.IO.File.Exists(h264x86))
                    {
                        System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(h264x86);
                        if (!string.IsNullOrEmpty(fvi.FileVersion)) return fvi.FileVersion;
                    }

                    string common = System.IO.Path.Combine(dir, @"packages\CefSharp.Common\lib\net462\CefSharp.dll");
                    if (System.IO.File.Exists(common))
                    {
                        System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(common);
                        if (!string.IsNullOrEmpty(fvi.FileVersion)) return fvi.FileVersion;
                    }

                    string releaseDll = System.IO.Path.Combine(dir, @"bin\x64\Release\CefSharp.dll");
                    if (System.IO.File.Exists(releaseDll))
                    {
                        System.Diagnostics.FileVersionInfo fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(releaseDll);
                        if (!string.IsNullOrEmpty(fvi.FileVersion)) return fvi.FileVersion;
                    }

                    System.IO.DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = (parent != null) ? parent.FullName : null;
                }
            }
            catch
            {
            }

            return null;
        }

        public static class General
        {
            public const string BtnClose = "Close";
            public const string BtnCancel = "Cancel";
            public const string BtnSave = "Save";
            public const string BtnAdd = "Add";
            public const string BtnDelete = "Delete";
            public const string BtnEdit = "Edit";
            public const string BtnCopyUrl = "Copy URL";
            public const string SearchLabel = "Search:";
            public const string SortByLabel = "Sort by:";
            public const string SortTitleAsc = "Title (A → Z)";
            public const string SortTitleDesc = "Title (Z → A)";
            public const string SortFolderAsc = "Location (A → Z)";
            public const string SortFolderDesc = "Location (Z → A)";
            public const string SortTypeAsc = "Type (Folders First)";
            public const string SortTypeDesc = "Type (Bookmarks First)";
            public const string SortDateDesc = "Date Added (Newest First)";
            public const string SortDateAsc = "Date Added (Oldest First)";
            public const string PlaceholderDash = "-";
            public const string PlaceholderEmpty = "--";
            public const string Unknown = "Unknown";
            public const string NewTabTitle = "New Tab";

            public static string FormatItemCount(int count)
            {
                return string.Format("{0} item{1}", count, count == 1 ? "" : "s");
            }

            public static string FormatItemizedCount(int total, int selected)
            {
                string itemsText = FormatItemCount(total);
                if (selected > 0)
                {
                    return string.Format("{0} ({1} selected)", itemsText, selected);
                }
                return itemsText;
            }
        }

        public static class Browser
        {
            public const string WindowTitle = AppName;
            public const string TabTitleWithAppFormat = "{0} - " + AppName;
            public const string TipBack = "Click to go back, hold to see history (Alt+Left Arrow)";
            public const string TipForward = "Click to go forward, hold to see history (Alt+Right Arrow)";
            public const string TipReload = "Reload this page (Ctrl+R)";
            public const string TipStop = "Stop loading (Esc)";
            public const string TipHome = "Home";
            public const string TipHomeIncognito = "Incognito home";
            public const string SearchPlaceholder = "Type URL or search here";
            public const string TipBookmark = "Bookmark this tab (Ctrl+D)";
            public const string TipRemoveBookmark = "Remove bookmark";
            public const string TipCannotBookmark = "Cannot bookmark this page";
            public const string TipGo = "Go to address (Enter)";
            public const string NavNewTab = "New Tab";
            public const string TipNewTab = "New tab (Ctrl+T)";
            public const string NavBookmarks = "Bookmarks";
            public const string TipBookmarks = "Bookmarks manager (Ctrl+B)";
            public const string NavHistory = "History";
            public const string TipHistory = "History (Ctrl+H)";
            public const string NavDownloads = "Downloads";
            public const string TipDownloads = "Downloads (Ctrl+J)";
            public const string TipDownloadsActiveFormat = "Downloads ({0} active) (Ctrl+J)";
            public const string TipTools = "Tools";
            public const string TipMenu = "Menu";
            public const string TipCloseTab = "Close tab";
            public const string TipMuteSite = "Mute site";
            public const string TipUnmuteSite = "Unmute site";
            public const string TipMediaCaptured = "Camera or microphone in use";
            public const string TipHiddenTabFormatSingle = "{0} hidden tab";
            public const string TipHiddenTabFormatPlural = "{0} hidden tabs";
            public const string TipShieldPrefix = "uBlock ";
            public const string TipShieldBlockedFormat = "uBlock: {0} blocked";
            public const string ShieldDefaultStatusSuffix = "(enabled)";
            public const string ShieldDisabledSiteSuffix = "(disabled for this site)";
            public const string ShieldDisabledSuffix = "(disabled)";
            public const string BadgeOverflowCount = "99+";
            public const string DropTabCue = "Drop tab here to open in this window";
            public const string MenuHiddenTabsFormat = "Hidden Tabs ({0})";
            public const string DefaultTabTitleFormat = "Tab {0}";
            public const string MenuResetToolbar = "Reset toolbar to default";
            public const string MenuShowFullHistory = "Show full history";

            // Toolbar Button Display Names
            public const string ToolbarBtnHome = "Home";
            public const string ToolbarBtnStar = "Bookmark current tab";
            public const string ToolbarBtnBookmarks = "Bookmarks manager";
            public const string ToolbarBtnHistory = "History";
            public const string ToolbarBtnDownloads = "Downloads";
            public const string ToolbarBtnShield = "uBlock";
            public const string StatusReady = "Ready";
            public const string RamFlushTip = "Memory footprint. Click to flush RAM.";
            public const string RamFlushSuccessFormat = "Memory flushed successfully!\nCurrent total memory: {0} MB";
            public const string RamFlushTitle = "RAM Optimization";
            public static readonly string AboutTitle = "About " + AppName + " " + AppArchitectureDisplay;
            public static readonly string AboutDialogText = AppName + " " + AppVersionDisplay + " " + AppArchitectureDisplay + "\n" +
                                                  "More Web. Less Noise.\n" +
                                                  "Developed by @ikmalsaid";

            public const string DownloadCompleteFormat = "Download complete: {0} (Click to open)";
            public const string DownloadingStatusFormat = "Downloading {0}: {1}% ({2}) - Click to view";
            public const string DownloadQueuedFormat = "Download queued: {0}";

            // Find in Page
            public const string FindLabel = "Find in Page:";
            public const string FindPlaceholder = "Find in page...";
            public const string FindMatchCase = "Match case";
            public const string FindPrev = "▲";
            public const string FindNext = "▼";
            public const string FindClose = "✕";

            // Menu Items - Main / Gear Menu
            public const string MenuNewWindow = "New &window";
            public const string MenuNewTab = "New &tab";
            public const string MenuFile = "&File";
            public const string MenuOpenFile = "&Open...";
            public const string MenuSavePdf = "&Save page as PDF...";
            public const string MenuPrint = "&Print...";
            public const string MenuCloseTab = "&Close tab";
            public const string MenuExit = "E&xit";
            public const string MenuView = "&View";
            public const string MenuFullScreen = "F&ull screen";
            public const string MenuZoom = "&Zoom";
            public const string MenuZoomPercentFormat = "&Zoom ({0}%)";
            public const string MenuZoomIn = "Zoom &in (+)";
            public const string MenuZoomOut = "Zoom &out (-)";
            public const string MenuZoomReset = "10&0%";
            public const string MenuRefresh = "&Refresh";
            public const string MenuStop = "&Stop";
            public const string MenuViewSource = "View page source";
            public const string MenuDevTools = "Developer &tools";
            public const string MenuDebug = "&Debug";
            public const string MenuDebugConsole = "&Debug Console";
            public const string MenuSimulateCrash = "Simulate &Crash";
            public const string MenuSaveCrashDumps = "&Save Crash Dumps (.dmp)";
            public const string MenuOpenCrashesFolder = "Open &Crashes Folder";
            public const string MenuOpenDumpsFolder = "Open &Dumps Folder";
            public const string CrashConfirmTitle = "Trigger Crash";
            public const string CrashConfirmMessage = "Are you sure you want to trigger a crash?";
            public const string MenuFavorites = "&Bookmarks";
            public const string MenuViewFavorites = "View &bookmarks";
            public const string MenuAddToFavorites = "&Bookmark this tab";
            public const string MenuShowBookmarksBar = "Show bookmarks bar";
            public const string MenuHistory = "&History";
            public const string MenuClearBrowsingData = "Clear &browsing data...";
            public const string MenuViewDownloads = "View &downloads";
            public const string MenuFindOnPage = "&Find on this page...";
            public const string MenuOptimizeRam = "Optimize RAM / Flush Memory";
            public const string MenuOptimizeRamFormat = "Optimize &RAM ({0} MB)";
            public const string MenuAbout = "About " + AppName;
            public const string MenuAboutWb = "&About wb";
            public const string MenuAccentColor = "Accent &Color";
            public const string MenuRandomColor = "&Random Color";
            public const string MenuCustomColor = "&Custom Color...";
            public const string MenuToolbarButtons = "&Toolbar buttons";
            public const string MenuTools = "&Tools";
            public const string MenuExperimental = "&Experimental";
            public const string MenuStardustProgressBar = "Stardust Progress Bar";
            public const string MenuGoogleSecurityCodeBanner = "Google Security Code (g.co/sc) Helper Banner";
            public const string GoogleSecurityCodeBannerTitle = "Google 2-Step Verification";
            public const string GoogleSecurityCodeBannerDesc = "Open g.co/sc in your default browser to view your 10-digit security code.";
            public const string GoogleSecurityCodeBannerText = "Google 2-Step Verification: Open g.co/sc in your default browser to view your 10-digit security code.";
            public const string GoogleSecurityCodeBannerAction = "Open g.co/sc";

            // Browser Page Context Menu Items (Right-Click Web View)
            public const string CtxAddToDictionary = "Add to Dictionary";
            public const string CtxUndo = "Undo";
            public const string CtxRedo = "Redo";
            public const string CtxCut = "Cut";
            public const string CtxCopy = "Copy";
            public const string CtxPaste = "Paste";
            public const string CtxDelete = "Delete";
            public const string CtxSelectAll = "Select All";
            public const string CtxInspectElement = "Inspect Element";
            public const string CtxOpenLinkInNewTab = "Open Link in New Tab";
            public const string CtxOpenLinkInNewWindow = "Open Link in New Window";
            public const string CtxSaveLinkAs = "Save Link As...";
            public const string CtxCopyLinkAddress = "Copy Link Address";
            public const string CtxOpenImageInNewTab = "Open Image in New Tab";
            public const string CtxSaveImageAs = "Save Image As...";
            public const string CtxCopyImage = "Copy Image";
            public const string CtxCopyImageAddress = "Copy Image Address";
            public const string CtxSearchGoogleFormat = "Search Google for \"{0}\"";
            public const string CtxPrint = "Print...";
            public const string CtxBack = "Back";
            public const string CtxForward = "Forward";
            public const string CtxReload = "Reload";
            public const string CtxSavePageAs = "Save Page As...";
            public const string CtxViewPageSource = "View Page Source";

            // Dialogs & Notifications
            public const string OpenFileDialogTitle = "Open File";
            public const string OpenFileDialogFilter = "HTML Files (*.html;*.htm)|*.html;*.htm|Image Files (*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg)|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg|All Files (*.*)|*.*";
            public const string SaveWebpageTitle = "Save Webpage";
            public const string HtmlFilesFilter = "HTML Files (*.html;*.htm)|*.html;*.htm|All Files (*.*)|*.*";
            public const string SavePageAsPdfTitle = "Save Page as PDF";
            public const string PrintToPdfTitle = "Print to PDF";
            public const string PrintToPdfErrorTitle = "Print to PDF Error";
            public const string OpenPdfTitle = "Open PDF";
            public const string PdfFileFilter = "PDF Document (*.pdf)|*.pdf";
            public const string DefaultDocumentTitle = "Document";
            public const string MsgNoActivePageToPrint = "No active page to print.";
            public const string MsgCouldNotGeneratePdf = "Could not generate PDF. Please ensure the page has loaded and you have write permissions.";
            public const string MsgPdfSavedSuccessFormat = "PDF saved successfully!\n\nFile: {0}\n\nWould you like to open it now?";
            public const string MsgCouldNotOpenPdfViewerPrefix = "Could not open PDF viewer: ";
            public const string NotifyBookmarkRemoved = "Bookmark removed.";
            public const string NotifyBookmarkAddedPrefix = "Bookmark added: ";
            public const string NotifyBookmarkAllAddedFormat = "Bookmarked {0} tab{1}.";
            public const string NotifyExportingPdf = "Exporting page to PDF...";
            public const string NotifyFailedExportPdf = "Failed to export PDF.";
            public const string NotifyPdfSavedPrefix = "PDF saved: ";
            public const string ServerNotFoundTitle = "Server Not Found - " + AppName;

            // Full Screen Overlay
            public const string FullScreenExitHint = "Press F11 or Esc to exit";
            public const string FullScreenBtnExit = "Exit Full Screen";

            // Tab Loading Overlay
            public const string TabLoadingDefault = "Loading...";
            public const string TabLoadingFormat = "Loading {0}...";

            // Tab Context Menu Items
            public const string TabMenuNewTab = "New Tab";
            public const string TabMenuNewTabRight = "New Tab to the Right";
            public const string TabMenuReload = "Reload Tab";
            public const string TabMenuDuplicate = "Duplicate Tab";
            public const string TabMenuPin = "Pin Tab";
            public const string TabMenuUnpin = "Unpin Tab";
            public const string TabMenuMuteSite = "Mute Site";
            public const string TabMenuUnmuteSite = "Unmute Site";
            public const string TabMenuClose = "Close Tab";
            public const string TabMenuCloseOthers = "Close Other Tabs";
            public const string TabMenuCloseRight = "Close Tabs to the Right";
            public const string TabMenuCloseLeft = "Close Tabs to the Left";
            public const string TabMenuReopenClosed = "Reopen Closed Tab";
            public const string TabMenuBookmark = "Bookmark Tab...";
            public const string TabMenuBookmarkAll = "Bookmark All Tabs...";
            public const string TabMenuEditTab = "Edit Tab...";
            public const string EditTabTitle = "Edit Tab";
            public const string BookmarkThisTab = "Bookmark this tab";

            // Address Bar Context Menu Items
            public const string AddressMenuCut = "Cut";
            public const string AddressMenuCopy = "Copy";
            public const string AddressMenuPaste = "Paste";
            public const string AddressMenuPasteAndGo = "Paste and go";
            public const string AddressMenuDelete = "Delete";
            public const string AddressMenuSelectAll = "Select all";

            // Incognito
            public const string WindowTitleIncognito = AppName + " (Incognito)";
            public const string MenuNewIncognitoWindow = "New &incognito window";
            public const string TipNewIncognitoWindow = "Open New Incognito Window (Ctrl+Shift+N)";
            public const string ContextMenuOpenIncognitoWindow = "Open Link in Incognito Window";
            public const string TabMenuOpenInIncognito = "Open in Incognito Window";
            public const string IncognitoTitle = "You've gone incognito";
            public const string IncognitoSubtitle = "Now you can browse privately and other people who use this device won't see your activity.";
            public const string IncognitoWontSaveHeader = "wb won't save:";
            public const string IncognitoWontSaveHistory = "Your browsing history";
            public const string IncognitoWontSaveCookies = "Cookies and site data";
            public const string IncognitoWontSaveForms = "Information entered in forms";
            public const string IncognitoVisibleHeader = "Your activity might still be visible to:";
            public const string IncognitoVisibleWebsites = "Websites that you visit";
            public const string IncognitoVisibleEmployer = "Your employer or school";
            public const string IncognitoVisibleIsp = "Your internet service provider (ISP)";
            public const string IncognitoBadge = "Incognito";
            public const string IncognitoBadgeTooltip = "Incognito";
            public const string IncognitoStatusNotice = "In-Memory Private Session • All session data discarded on close";
        }

        public static class Downloads
        {
            public const string PageTitle = "Downloads";
            public const string WindowTitle = "Downloads - " + AppName;
            public const string WbDownloadExt = ".wbdownload";
            public const string DownloadUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " + AppName + " Browser";
            public const string BtnAdd = "Add Download";
            public const string BtnCheckMissing = "Check Missing Files";
            public const string BtnClear = "Clear Finished";
            public const string SearchPlaceholder = "Filter downloads...";
            public const string NoDownloadSelected = "No download selected";
            public const string DefaultDownloadName = "Download";
            public const string UnknownFileName = "Unknown File";
            public const string SelectedFileFallback = "selected file";

            // Status Constants
            public const string StatusCompleted = "Status: Completed";
            public const string StatusPaused = "Status: Paused";
            public const string StatusQueued = "Status: Queued";
            public const string StatusNetworkError = "Failed - Network error";
            public const string StatusDiskFull = "Failed - Disk full";
            public const string StatusInterrupted = "Interrupted";
            public const string DiskFullResumeBlocked = "Cannot resume: Not enough free space on the destination drive.";

            // Exit Warning
            public const string ExitDownloadsInProgressTitle = "Exit " + AppName + "?";
            public const string ExitDownloadsInProgressMessage = "Downloads are currently in progress. Do you want to exit " + AppName + " anyway?\n\nActive download(s) will be safely paused as .wbdownload files.";

            // Bubble
            public const string BubbleTitle = "Downloads";
            public const string BubbleShowAll = "Show all downloads (Ctrl+J)";
            public const string NotificationDownloadComplete = "wb: Download complete - {0}";
            public const string BubbleNoRecentDownloads = "No recent downloads";
            public const string TimeZeroSecLeft = "0 sec left";
            public const string TimeSecLeftFormat = "{0} sec left";
            public const string TimeMinLeftFormat = "{0} min left";
            public const string TimeHoursMinsLeftFormat = "{0}h {1}m left";
            public const string TimeHoursLeftFormat = "{0} hr left";
            public const string TimeComplete = "Complete";
            public const string TimeJustNow = "Just now";
            public const string TimeMinAgoFormat = "{0} min ago";
            public const string TimeHourAgoFormat = "{0} hr ago";
            public const string TimeDayAgoFormat = "{0} day ago";
            public const string BubbleBtnCancelDownload = "Cancel download";
            public const string BubbleBtnPauseDownload = "Pause download";
            public const string BubbleBtnResumeDownload = "Resume download";
            public const string BubbleBtnRemoveFromList = "Remove from list";
            public const string BubbleBtnShowInFolder = "Show in folder";
            public const string BubbleBtnRetryDownload = "Retry download";
            public const string StatusDeleted = "Deleted";

            // Details
            public const string DetailUrlPrefix = "URL: ";
            public const string DetailPathPrefix = "Save Path: ";
            public const string DetailSizePrefix = "Size: ";
            public const string DetailSpeedPrefix = "Speed: ";
            public const string DetailEtaPrefix = "ETA: ";
            public const string FileMissing = "File Missing";
            public const string FileMissingDescription = "File was deleted or moved from disk";
            public const string WaitingInQueue = "Waiting in queue...";
            public const string FinishedAtFormat = "Finished at: {0:HH:mm}";
            public const string MultiSelectionTitleFormat = "{0} ({1} items selected)";
            public const string DeleteMultiButtonFormat = "Delete ({0})...";

            // Buttons
            public const string BtnPause = "Pause";
            public const string BtnResume = "Resume";
            public const string BtnStart = "Start";
            public const string BtnCancel = "Cancel";
            public const string BtnRetry = "Retry";
            public const string BtnOpenFile = "Open File";
            public const string BtnOpenFolder = "Open Folder";
            public const string BtnCopyLink = "Copy Link";
            public const string BtnDelete = "Delete...";

            // Dialogs & Titles
            public const string DeleteSingleTitle = "Delete Download";
            public const string DeleteMultiTitle = "Delete Downloads";
            public const string CancelTitle = "Cancel Download";
            public const string RetryTitle = "Retry Download";
            public const string OpenFileTitle = "Open File";
            public const string OpenFolderTitle = "Open Folder";
            public const string TitleCannotResume = "Cannot Resume Download";
            public const string StatusLabelPrefix = "Status: ";
            public const string CheckMissingTitle = "Check Missing Files";
            public const string CopyLinkTitle = "Copy URL";
            public const string LinkCopied = "Download link copied to clipboard.";
            public const string CannotRedownloadUrlUnknown = "Cannot re-download because original URL is unknown.";
            public const string FileNotFoundOnDisk = "The downloaded file could not be found on disk.";
            public const string CannotOpenFileFormat = "Could not open file: {0}";
            public const string CannotOpenFolderFormat = "Could not open folder: {0}";
            public const string MissingScanNone = "Scan complete: All downloaded files are present on disk!";

            // Add Download Dialog
            public const string AddTitle = "Add New Download";
            public const string AddUrlLabel = "URL:";
            public const string AddUrlPlaceholder = "https://...";
            public const string AddSaveToLabel = "Save to:";
            public const string AddBrowseBtn = "Browse...";
            public const string AddQueueBtn = "Queue";
            public const string AddDownloadNowBtn = "Download Now";

            // Columns
            public const string ColFileName = "File Name";
            public const string ColStatus = "Status";
            public const string ColProgress = "Progress";
            public const string ColSpeed = "Speed";
            public const string ColSize = "Size";
            public const string ColDateStarted = "Date Started";
            public const string ColSavedPath = "Saved Path";

            // Sort Options
            public const string SortNameAsc = "File Name (A → Z)";
            public const string SortNameDesc = "File Name (Z → A)";
            public const string SortDateDesc = "Start Time (Newest First)";
            public const string SortDateAsc = "Start Time (Oldest First)";

            // Context Menu
            public const string CtxStart = "Start / Resume Download";
            public const string CtxPause = "Pause Download";
            public const string CtxCancel = "Cancel Download";
            public const string CtxRetry = "Re-download / Retry";
            public const string CtxOpenFile = "Open File";
            public const string CtxOpenFolder = "Open Containing Folder";
            public const string CtxCopyUrl = "Copy Download Link";
            public const string CtxDeleteList = "Remove from List";
            public const string CtxDeleteDisk = "Delete from Disk";

            public static string ConfirmCancel(string fileName)
            {
                return string.Format("Are you sure you want to cancel downloading '{0}'?", fileName);
            }

            public static string ConfirmDeleteSingle(string fileName)
            {
                return string.Format("Do you also want to delete the file '{0}' from disk?\n\n• Yes: Delete from disk AND remove from list\n• No: Remove from list only\n• Cancel: Keep download", fileName);
            }

            public static string ConfirmDeleteMultiple(int count)
            {
                return string.Format("Do you also want to delete the {0} selected files from disk?\n\n• Yes: Delete from disk AND remove from list\n• No: Remove from list only\n• Cancel: Keep downloads", count);
            }

            public static string MissingPrompt(string fileName)
            {
                return string.Format("The file '{0}' is missing from disk.\n\nWould you like to re-download it now?", fileName);
            }

            public static string MissingScanResult(int missingCount)
            {
                return string.Format("Scan complete: {0} missing file{1} detected.", missingCount, missingCount == 1 ? " was" : "s were");
            }

            public static string FormatCount(int total, int inProgress)
            {
                return string.Format("{0} download{1}{2}",
                    total,
                    total == 1 ? "" : "s",
                    inProgress > 0 ? string.Format(" • {0} in progress", inProgress) : "");
            }
        }

        public static class Bookmarks
        {
            public const string PageTitle = "Bookmarks";
            public const string WindowTitle = "Bookmarks Manager - " + AppName;
            public const string BtnAdd = "Add Bookmark";
            public const string BtnAddFolder = "Add Folder";
            public const string SearchPlaceholder = "Filter bookmarks...";
            public const string ColumnTitle = "Title";
            public const string ColumnType = "Type";
            public const string ColumnFolder = "Location";
            public const string ColumnUrl = "URL";
            public const string ColumnDateAdded = "Date Added";
            public const string TypeFolder = "Folder";
            public const string TypeBookmark = "Bookmark";
            public const string AllBookmarks = "All bookmarks";

            public const string BtnOpenNewTab = "Open in New Tab";
            public const string BtnOpenCurrent = "Open in Current Tab";

            public const string AddTitle = "Add Bookmark";
            public const string AddTitleLabel = "Name";
            public const string AddTitlePlaceholder = "Bookmark title";
            public const string AddUrlLabel = "URL";
            public const string FolderLabel = "Folder:";
            public const string FolderBookmarksBar = "Bookmarks bar";
            public const string FolderOther = "Other bookmarks";
            public const string FolderMobile = "Mobile bookmarks";
            public const string EditTitle = "Edit Bookmark";
            public const string EditFolderTitle = "Edit Folder";
            public const string AddFolderTitle = "Add Folder";
            public const string BtnEdit = "Edit";
            public const string BtnRemove = "Remove";
            public const string BtnNewFolder = "New folder";

            public const string BarEmptyPrompt = "For quick access, place your bookmarks here on the bookmarks bar.";
            public const string OtherBookmarks = "Other bookmarks";
            public const string AddPage = "Add page...";
            public const string AddFolder = "Add folder...";
            public const string MoveToBar = "Move to bookmarks bar";
            public const string MoveToFolder = "Move to folder";
            public const string MoveUp = "Move up";
            public const string MoveDown = "Move down";
            public const string OpenAllFormat = "Open all ({0})";
            public const string OpenAllNewWindow = "Open all in new window";
            public const string OpenAllIncognito = "Open all in incognito window";
            public const string FolderItemsFormat = "(Folder · {0} item{1})";
            public const string FolderDisplayFormat = "{0}";
            public const string TipBookmarksBarToggle = "Show bookmarks bar (Ctrl+Shift+B)";

            public const string DeleteBookmarkTitle = "Delete Bookmark";
            public const string DeleteFolderTitle = "Delete Folder";
            public const string DeleteMultipleTitle = "Delete Bookmarks";
            public const string ConfirmDeleteBookmarkFormat = "Are you sure you want to delete bookmark \"{0}\"?";
            public const string ConfirmDeleteFolderFormat = "Are you sure you want to delete folder \"{0}\"?";
            public const string ConfirmDeleteFolderWithChildrenFormat = "Are you sure you want to delete folder \"{0}\" and all {1} item(s) inside it?";
            public const string ConfirmDeleteMultipleFormat = "Are you sure you want to delete the {0} selected items?";
            public const string DefaultItemName = "bookmark";

            public static string FormatCount(int count)
            {
                return string.Format("{0} bookmark{1}", count, count == 1 ? "" : "s");
            }
        }

        public static class History
        {
            public const string PageTitle = "History";
            public const string WindowTitle = "History Manager - " + AppName;
            public const string BtnClearAll = "Clear Entire History";
            public const string SearchPlaceholder = "Filter browsing history...";

            public const string SortVisitDesc = "Visit Time (Newest First)";
            public const string SortVisitAsc = "Visit Time (Oldest First)";

            public const string ColumnTitle = "Title";
            public const string ColumnUrl = "URL";
            public const string ColumnLastVisited = "Last Visited";

            public const string BtnOpenNewTab = "Open in New Tab";
            public const string BtnOpenCurrent = "Open in Current Tab";

            public const string ConfirmClearAll = "Are you sure you want to clear all browsing history?";
            public const string ClearHistoryTitle = "Clear History";

            public static string FormatCount(int count)
            {
                return string.Format("{0} history entr{1}", count, count == 1 ? "y" : "ies");
            }
        }

        public static class ImportData
        {
            public const string PageTitle = "Import Data";
            public const string WindowTitle = "Import Data - " + AppName;
            public const string SubTitle = "Transfer bookmarks, history, and shortcuts from other browsers or HTML files.";
            public const string SourceLabel = "Source";
            public const string SectionSelectBrowser = "SOURCE";
            public const string CategoriesLabel = "Items to import:";
            public const string SectionSelectData = "ITEMS TO IMPORT";
            public const string OptionBookmarks = "Bookmarks & folders";
            public const string OptBookmarks = "Bookmarks & folders";
            public const string OptionHistory = "Browsing history";
            public const string OptHistory = "Browsing history";
            public const string OptionShortcuts = "New Tab shortcuts";
            public const string OptShortcuts = "New Tab shortcuts";
            public const string OptionSkipDuplicates = "Skip duplicates";
            public const string OptSkipDuplicates = "Skip duplicates";
            public const string DestMerge = "Keep original folders";
            public const string OptDestinationMerge = "Keep original folders";
            public const string DestFolder = "Place in folder:";
            public const string OptDestinationDedicated = "Place in folder:";
            public const string BtnImport = "Import";
            public const string BtnBrowse = "Browse...";
            public const string StatusReady = "Ready to import.";
            public const string StatusImporting = "Importing, please wait...";
            public const string StatusDone = "Import complete.";
            public const string MenuImportData = "Import Data...";
        }

        public static class ExportData
        {
            public const string PageTitle = "Export Data";
            public const string WindowTitle = "Export Data - " + AppName;
            public const string SubTitle = "Save bookmarks, history, and shortcuts to files.";
            public const string CardBookmarksTitle = "Bookmarks";
            public const string CardBookmarksDesc = "Export all bookmarks and folders to an HTML file.";
            public const string BtnExportBookmarks = "Export HTML...";
            public const string CardHistoryTitle = "History";
            public const string CardHistoryDesc = "Export browsing history to CSV or HTML.";
            public const string BtnExportHistoryCsv = "Export CSV...";
            public const string BtnExportHistoryHtml = "Export HTML...";
            public const string CardShortcutsTitle = "Shortcuts";
            public const string CardShortcutsDesc = "Export New Tab speed-dial tiles to JSON.";
            public const string BtnExportShortcuts = "Export JSON...";
            public const string MenuExportData = "Export Data...";
        }

        public static class NewTab
        {
            public const string PageTitle = "New Tab";
            public const string SearchPlaceholder = "Search Google or enter a web address";

            // Context Menu & Options
            public const string ShortcutMenuOpenInNewTab = "Open in new tab";
            public const string ShortcutMenuEdit = "Edit shortcut";
            public const string ShortcutMenuRemove = "Remove";
            public const string SearchHintText = "Search the web or type a URL...";
            public const string AddShortcutLabel = "Add Shortcut";
            public const string DialogTitleEditShortcut = "Edit Shortcut";
            public const string DialogTitleAddShortcut = "Add Shortcut";
            public const string BgMenuShowBackground = "Show background";
            public const string BgMenuHide = "Hide";
            public const string BgMenuRandomize = "Randomize";
            public const string BgMenuToggleColorPrefix = "Toggle Color: ";
            public const string BgMenuWhiteGrayscale = "Pure White (Grayscale)";
            public const string BgMenuFullColor = "Full Color";
            public const string BgMenuPureWhite = "Pure White";
            public const string BgMenuAccent = "Accent";
            public const string BgMenuFitFill = "Fit: Fill Space (Wallpaper)";
            public const string BgMenuFitCorner = "Fit: Corner (Silhouette)";
            public const string BgMenuZoomFormat = "Zoom ({0}%)";
            public const string BgMenuZoomDefault = "85% (Default)";
            public const string BgMenuPosition = "Position";
            public const string BgMenuPosLeft = "Left";
            public const string BgMenuPosCenter = "Center";
            public const string BgMenuPosRight = "Right";
            public const string BgMenuPosRightDefault = "Right (Default)";
            public const string BgMenuMirror = "Mirror";
            public const string BgMenuFlip = "Flip";
            public const string BgMenuRescan = "Rescan files";
            public const string BgMenuBrowse = "Browse custom image...";
            public const string SelectCustomBgTitle = "Select Custom Background Image";
            public const string BgImageFilter = "Image Files (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All Files (*.*)|*.*";
            public const string BgMenuOpenFolder = "Open folder";
        }

        public static class AdBlock
        {
            public const string UBlockTitle = AppName + " uBlock";
            public const string ShieldTitle = UBlockTitle;
            public const string ShieldDescActive = "Ads & trackers blocked";
            public const string ShieldDescPaused = "Protection paused";
            public const string ProtectionForSite = "Protection for this site";
            public const string ProtectionOn = "ON";
            public const string ProtectionOff = "OFF";
            public const string StateActive = "Protection Active";
            public const string StatePaused = "Protection Paused";
            public const string StateActiveSub = "uBlock is blocking ads & trackers";
            public const string StatePausedSub = "uBlock is turned off for this site";
            public const string BlockedOnPage = "Blocked on this page";
            public const string BlockedLifetime = "Total blocked all-time";
            public const string BlockedSession = "Blocked this session";
            public const string BtnReloadPage = "Reload Page";
            public const string BtnManageWhitelist = "Manage Whitelist...";
            public const string ChkGlobalEnabled = "Global uBlock Protection";
            public const string TipShieldActiveFormat = "uBlock Active: {0} blocked on this page (Click for options)";
            public const string TipShieldPaused = "uBlock Paused for this site (Click to enable)";
            public const string TipShieldDisabled = "uBlock is disabled globally (Click to configure)";
            public const string WhitelistTitle = "uBlock Whitelist - " + AppName;
            public const string WhitelistHeader = "Websites where uBlock protection is paused:";
            public const string WhitelistColDomain = "Domain";
            public const string WhitelistBtnAdd = "Add Domain";
            public const string WhitelistBtnRemove = "Remove Selected";
            public const string WhitelistBtnClear = "Clear All";
            public const string WhitelistAddPrompt = "Enter domain to whitelist (e.g. example.com):";
            public const string WhitelistAddTitle = "Add Whitelisted Domain";
            public const string InternalPageDomain = "Local / Internal Page";

            // Button label states
            public const string BtnTextNormal = "uBlock";
            public const string BtnTextOff = "uBlock: Off";
            public const string BtnTextPaused = "uBlock: Paused";
            public const string BtnTextCountFormat = "uBlock ({0})";

            // Status items in menu
            public const string StatusInternalProtected = "Internal Page (Protected)";
            public const string StatusDisabledGlobally = "uBlock Disabled Globally";
            public const string StatusPausedForFormat = "uBlock Paused for {0}";
            public const string StatusActiveForFormat = "uBlock Active for {0}";
            public const string StatusUBlockPaused = "uBlock Paused";
            public const string DomainCountFormat = "{0} domain{1}";

            // Menu Items
            public const string MenuResumeSite = "Resume uBlock on this site";
            public const string MenuPauseSite = "Pause uBlock on this site";
            public const string MenuBlockedOnPageFormat = "Blocked on this page: {0}";
            public const string MenuTotalBlockedFormat = "Total ads & trackers blocked: {0:N0}";
            public const string MenuGlobalEnabled = "Global uBlock: Enabled";
            public const string MenuGlobalDisabled = "Global uBlock: Disabled";
            public const string MenuManageWhitelist = "Manage Whitelist...";
            public const string MenuFilterSubscriptionsFormat = "Filter Subscriptions... ({0:N0} rules)";
            public const string MenuUpdateRules = "Update Filter Lists Now";
            public const string MenuReloadBypass = "Reload Page (Bypass Cache)";
            public const string MsgUpdatingBackground = "Updating filter lists in background from remote sources...";

            // Subscriptions Form
            public const string SubscriptionsTitle = "uBlock Filter Lists - " + AppName;
            public const string SubscriptionsHeader = "Filter Subscriptions & Rules";
            public const string SubscriptionsDesc = "Enable or update subscriptions. Rules are compiled into lock-free memory tables for 0% CPU overhead.";
            public const string ColListName = "Filter List Name";
            public const string ColGroup = "Group";
            public const string ColRuleCount = "Rule Count";
            public const string ColLastUpdated = "Last Updated";
            public const string ColStatus = "Status";
            public const string StatusBundledSeed = "Bundled Seed";
            public const string StatusReady = "Ready";
            public const string StatusUpdating = "Updating...";
            public const string StatusUpdated = "Updated";
            public const string StatusUpToDate = "Up to date";
            public const string StatusCheckFailed = "Check failed";
            public const string StatusError = "Error updating";
            public const string TotalCompiledRulesFormat = "Total Compiled Rules: {0:N0}";

            // Subscriptions Action Buttons
            public const string BtnUpdateAll = "Update All Now";
            public const string BtnAddCustom = "Add Custom List";
            public const string BtnRemoveSelected = "Remove Selected";

            // Add Custom Subscription Prompt
            public const string AddCustomTitle = "Add Custom Filter Subscription";
            public const string AddCustomNameLabel = "Subscription Name:";
            public const string AddCustomDefaultName = "Custom Filter List";
            public const string AddCustomUrlLabel = "Filter List URL (ABP/uBlock or Hosts format):";
            public const string BtnSubscribe = "Subscribe";

            // Remove Subscription Dialog
            public const string RemoveConfirmFormat = "Remove subscription '{0}'?";
            public const string RemoveTitle = "Remove Subscription";

            // Element Picker / Block Element & Manage Blocked Elements
            public const string MenuBlockElement = "Block Element on Page...";
            public const string MenuManageBlockedElements = "Manage Blocked Elements...";
            public const string MenuManageBlockedElementsFormat = "Manage Blocked Elements ({0})...";
            public const string ContextMenuBlockElement = "Block Element...";
            public const string ManageFiltersTitle = "Blocked Elements - " + AppName;
            public const string ManageFiltersHeader = "Elements blocked with element picker & custom filter rules:";
            public const string ColRule = "Rule";
            public const string ColDomain = "Domain";
            public const string ColSelector = "CSS Selector";
            public const string ColDateAdded = "Date Added";
            public const string BtnAddRule = "Add Rule";
            public const string BtnRemoveRule = "Remove Selected";
            public const string BtnClearRules = "Clear All";
            public const string BtnReloadPageShort = "Reload Tab";
            public const string ConfirmClearRules = "Are you sure you want to remove all custom blocked element rules?";
            public const string ClearRulesTitle = "Clear Blocked Elements";
            public const string AddRuleTitle = "Add Custom Filter Rule";
            public const string AddRuleDomainPrompt = "Domain (optional, leave blank to apply to all websites):";
            public const string AddRuleSelectorPrompt = "CSS Selector (e.g. .advertisement or #banner):";
            public const string BlockElementSavedNotificationFormat = "Blocked element saved: {0}";
            public const string SearchPlaceholderFilters = "Filter rules by domain or selector...";
            public const string BlockElementPanelTitle = "Block Element";
            public const string BlockElementPanelInstructions = "Click an element on the page to select. Press Esc to cancel.";
            public const string BlockElementDialogTitle = "Block Element - " + AppName;
            public const string BlockElementDialogDesc = "Create a cosmetic filter rule to hide this element.";
            public const string BlockElementScopeSiteFormat = "This site only ({0})";
            public const string BlockElementScopeGlobal = "All sites (Global)";
            public const string BlockElementBtnBlock = "Block Element";
            public const string BlockElementBtnPreview = "Preview";
            public const string BlockElementBtnPickAgain = "Pick Another";
            public const string DomainGlobalAllSites = "Global (all sites)";
            public const string RuleCountFormatSingle = "{0} rule";
            public const string RuleCountFormatPlural = "{0} rules";
            public const string RuleMatchedCountFormatSingle = "{0} of {1} rule";
            public const string RuleMatchedCountFormatPlural = "{0} of {1} rules";
            public const string DomainPlaceholder = "example.com (or leave empty for global)";
            public const string SelectorPlaceholder = ".advertisement or #banner";
            public const string ConfirmClearWhitelist = "Are you sure you want to clear all whitelisted domains?";
            public const string ClearWhitelistTitle = "Clear Whitelist";
            public const string WhitelistDomainPlaceholder = "example.com";
            public const string BtnUndoPreview = "Undo Preview";
            public const string TargetNoticeSelect = "Select element";
            public const string TargetNoticeInstructions = "Pick an element on the page to block.";
            public const string CurrentSiteFallback = "current site";
            public const string TargetElementLevelLabel = "Target Element Level:";
            public const string CandidateSpecificChildElement = "Specific Child Element";
            public const string CandidateMatchCountFormatSingle = "{0} ({1} match)";
            public const string CandidateMatchCountFormatPlural = "{0} ({1} matches)";
            public const string CssSelectorLabel = "CSS Selector:";
            public const string CssSelectorPlaceholder = "CSS Selector";
            public const string MatchesCountFormatSingle = "{0} matching element on page";
            public const string MatchesCountFormatPlural = "{0} matching elements on page";
        }

        public static class Theme
        {
            public const string PresetRandom = "Random";
            public const string PresetCustom = "Custom";
            public const string PresetDefaultBlue = "Default Blue";
            public const string PresetCyberCyan = "Cyber Cyan";
            public const string PresetMintGreen = "Mint Green";
            public const string PresetVibrantLime = "Vibrant Lime";
            public const string PresetSolarAmber = "Solar Amber";
            public const string PresetCoralOrange = "Coral Orange";
            public const string PresetNeonCrimson = "Neon Crimson";
            public const string PresetHotPink = "Hot Pink";
            public const string PresetElectricPurple = "Electric Purple";
            public const string PresetSkyBlue = "Sky Blue";
        }

        public static class SponsorBlock
        {
            // Context Menu (MainBrowserForm)
            public const string MenuHeader = "SponsorBlock for YouTube";
            public const string MenuToggleEnable = "Enable SponsorBlock on YouTube";
            public const string MenuTogglePreviewBar = "Show Timeline Segment Markers";
            public const string MenuSettings = "SponsorBlock Settings...";
            public const string MenuWhitelistVideo = "Whitelist This Video (Don't Skip)";
            public const string MenuUnwhitelistVideo = "Remove Video from Whitelist";
            public const string MenuManageWhitelist = "Manage Video Whitelist...";

            // Whitelist Dialog Strings
            public const string WhitelistTitle = "SponsorBlock Whitelist - " + AppName;
            public const string WhitelistHeader = "Whitelisted YouTube Videos";
            public const string WhitelistDesc = "Videos in this list will never be skipped or muted by SponsorBlock.";
            public const string WhitelistColVideoId = "Video ID / URL";
            public const string WhitelistColStatus = "Status";
            public const string WhitelistStatusPaused = "SponsorBlock Paused";
            public const string WhitelistBtnAdd = "Add Video...";
            public const string WhitelistBtnRemove = "Remove";
            public const string WhitelistBtnClear = "Clear All";
            public const string WhitelistVideoCountFormat = "{0} whitelisted video{1}";
            public const string WhitelistAddTitle = "Add Video to Whitelist";
            public const string WhitelistAddPrompt = "Enter YouTube Video ID or Video URL:";
            public const string WhitelistPlaceholder = "e.g. dQw4w9WgXcQ or https://www.youtube.com/watch?v=dQw4w9WgXcQ";
            public const string WhitelistConfirmClear = "Remove all videos from the SponsorBlock whitelist?";
            public const string WhitelistClearTitle = "Clear Video Whitelist";
            public const string WhitelistMenuCopyUrl = "Copy Video URL";
            public const string WhitelistMenuCopyId = "Copy Video ID";
            public const string WhitelistMenuOpenInNewTab = "Open in New Tab";
            public const string BtnManageWhitelist = "Manage Whitelist...";

            // Settings Form Strings
            public const string SettingsTitle = "SponsorBlock Settings - " + AppName;
            public const string SettingsHeader = "SponsorBlock for YouTube";
            public const string SettingsDesc = "Skip sponsors, intros, outros and paid promotions automatically with zero CPU overhead.";
            public const string OptionEnableSb = "Enable SponsorBlock Protection on YouTube";
            public const string OptionShowPreviewBar = "Show colored segment markers on YouTube progress bar (Preview Bar)";
            public const string OptionShowNotice = "Show on-screen skip notification toast with Unskip button";
            public const string OptionAudioNotify = "Play subtle audio cue when a segment is skipped";
            public const string SectionCategoriesActions = "CATEGORIES & ACTIONS";
            public const string BtnResetDefaults = "Reset to Defaults";
            public const string BtnSaveApply = "Save & Apply";
            public const string MsgConfirmResetDefaults = "Reset all categories and options to default values?";
            public const string DialogResetDefaultsTitle = "Reset Defaults";

            // Action Friendly Names
            public const string ActionAutoSkip = "Auto Skip";
            public const string ActionManualSkip = "Show Notice (Manual)";
            public const string ActionMute = "Mute Audio";
            public const string ActionDisabled = "Disabled";

            // Category Names & Descriptions
            public const string CategorySponsorName = "Sponsor";
            public const string CategorySponsorDesc = "Paid promotion, product placement, or sponsored integration.";

            public const string CategoryIntroName = "Intermission / Intro";
            public const string CategoryIntroDesc = "Intro animation, title card, intermission, or bumper.";

            public const string CategoryOutroName = "Endcards / Outro";
            public const string CategoryOutroDesc = "Credits, endcards, or outro sequence at the end of the video.";

            public const string CategoryInteractionName = "Interaction Reminder";
            public const string CategoryInteractionDesc = "Reminder to like, subscribe, hit the bell, or follow on socials.";

            public const string CategorySelfPromoName = "Unpaid / Self Promotion";
            public const string CategorySelfPromoDesc = "Promoting creator's merchandise, podcasts, social channels, or other videos.";

            public const string CategoryPreviewName = "Preview / Recap";
            public const string CategoryPreviewDesc = "Collection of clips showing what will happen in this video or previous videos.";

            public const string CategoryFillerName = "Filler / Tangent";
            public const string CategoryFillerDesc = "Tangents, meme sequences, or jokes that add no context to the core topic.";

            public const string CategoryMusicOfftopicName = "Music: Non-Music Section";
            public const string CategoryMusicOfftopicDesc = "Only for music videos: non-music spoken dialogue, skits, or silent breaks.";

            public const string CategoryPoiHighlightName = "Point of Interest";
            public const string CategoryPoiHighlightDesc = "Highlight moment or most interesting part of the video.";

            // YouTube Player Tooltips and Notices
            public const string TooltipCreateSegment = "SponsorBlock: Create segment";
            public const string TooltipSettings = "SponsorBlock Settings";
            public const string TooltipActive = "SponsorBlock: Active";
            public const string TooltipSegmentsFoundSingular = "SponsorBlock: 1 segment found";
            public const string TooltipSegmentsFoundPlural = "SponsorBlock: {0} segments found";
            public const string NoticeDetectedSuffix = " detected";
            public const string NoticeSkippedPrefix = "Skipped ";
            public const string BtnSkip = "Skip";
            public const string BtnUnskip = "Unskip";
            public const string BtnUnmute = "Unmute";
            public const string StatusMuted = "Muted";

            // API & Storage Constants
            public const string ConfigFileName = "sponsorblock_config.json";
            public const string StatsFileName = "sponsorblock_stats.json";
            public const string ApiBaseUrl = "https://sponsor.ajay.app/api/skipSegments";
            public const string ApiClientHeader = "wb-browser/1.0";
        }

        public static class ClearBrowsingData
        {
            public const string DialogTitle = "Clear browsing data";
            public const string HeaderTitle = "Clear browsing data";
            public const string TabBasic = "Basic";
            public const string TabAdvanced = "Advanced";
            public const string TimeRangeLabel = "Time range";

            // Time ranges
            public const string TimeRangeLastHour = "Last hour";
            public const string TimeRangeLast24Hours = "Last 24 hours";
            public const string TimeRangeLast7Days = "Last 7 days";
            public const string TimeRangeLast4Weeks = "Last 4 weeks";
            public const string TimeRangeAllTime = "All time";

            // Basic options
            public const string BrowsingHistoryTitle = "Browsing history";
            public const string BrowsingHistoryDesc = "Clears history and autocompletions in the address bar.";
            public const string CookiesTitle = "Cookies and other site data";
            public const string CookiesDesc = "Signs you out of most sites.";
            public const string CacheTitle = "Cached images and files";
            public const string CacheDescBasic = "Frees up space. Some sites may load more slowly on your next visit.";
            public const string CacheDescAdvanced = "Frees up space.";
            public const string DeviceFootnote = "Clears browsing data from this device.";

            // Advanced options
            public const string DownloadHistoryTitle = "Download history";
            public const string DownloadHistoryDesc = "Clears download records (does not delete downloaded files).";
            public const string PasswordsTitle = "Passwords and other sign-in data";
            public const string PasswordsDesc = "Saved passwords and login session tokens.";
            public const string AutofillTitle = "Autofill form data";
            public const string AutofillDesc = "Clears address bar suggestions and search cache.";
            public const string SiteSettingsTitle = "Site settings";
            public const string SiteSettingsDesc = "Clears site permissions, muted domains and error bypasses.";
            public const string HostedAppDataTitle = "Hosted app data";
            public const string HostedAppDataDesc = "Clears offline web storage and service worker data.";

            // Action Buttons & Status
            public const string BtnCancel = "Cancel";
            public const string BtnClearData = "Clear data";
            public const string StatusClearing = "Clearing browsing data...";
            public const string StatusCleared = "Browsing data cleared.";
            public const string MenuClearBrowsingData = "Clear &browsing data...";
        }

        public static class Proxy
        {
            public const string DialogTitle = "Proxy Settings";
            public const string HeaderTitle = "Proxy connection settings";
            public const string HeaderSubtitle = "Configure how wb connects to the internet through proxy servers.";
            public const string ModeDirect = "Direct connection (No proxy)";
            public const string ModeSystem = "Use system proxy settings (Default)";
            public const string ModeAutoDetect = "Auto-detect proxy settings (WPAD)";
            public const string ModePacScript = "Automatic proxy configuration URL (PAC)";
            public const string ModeManual = "Manual proxy configuration";

            public const string ProtocolLabel = "Protocol:";
            public const string ServerLabel = "Proxy Server:";
            public const string PortLabel = "Port:";
            public const string BypassLabel = "Bypass proxy for:";
            public const string BypassHint = "Separate multiple hosts with semicolons (e.g. <local>; localhost; 127.0.0.1; *.internal)";
            public const string PacUrlLabel = "PAC Script URL:";

            public const string AuthCheckbox = "Proxy server requires authentication";
            public const string UsernameLabel = "Username:";
            public const string PasswordLabel = "Password:";

            public const string BtnTest = "Test Connection";
            public const string BtnApply = "Save & Apply";
            public const string BtnCancel = "Cancel";

            public const string TestTesting = "Testing connection...";
            public const string TestSuccess = "Connection test successful ({0}ms)!";
            public const string TestFailed = "Connection failed: {0}";
            public const string SavedNotice = "Proxy settings saved and applied.";

            public const string MenuProxySettings = "&Proxy settings...";
        }

        public static class WebError
        {
            public const string DefaultPageTitle = "Problem Loading Page";
            public const string DefaultSubtitle = "wb encountered an issue while trying to display this page.";
            public const string UnknownHost = "unknown host";
            public const string ThisSite = "this site";

            // Action Buttons & Interactive Links
            public const string BtnGoBackSafe = "Go Back (Safe)";
            public const string BtnCloseTab = "Close Tab";
            public const string BtnGoHome = "Go Home";
            public const string BtnTryAgain = "Try Again";
            public const string BtnGoBack = "Go Back";
            public const string BtnCopied = "Copied!";
            public const string AdvancedToggle = "Advanced";
            public const string ProceedUnsafeFormat = "Proceed to {0} (unsafe)";
            public const string ProceedUnsafeFallback = "Proceed (unsafe)";
            public const string DefaultWhitelistCheckboxText = "Don't warn me again about this site";
            public const string RedirectNotice = "The blocked page wants to redirect to another site:";
            public const string MetaBlockedBy = "Blocked by:";

            // SSL / Insecure Warning
            public const string SslTitle = "Your connection is not private";
            public const string SslSubtitleFormat = "Attackers might be trying to steal your information from {0} (for example, passwords, messages, or credit cards).";
            public const string SslDetailsDefault = "The security certificate presented by this server is untrusted, expired, or invalid.";
            public const string SslDescriptionFormat = "This server could not prove that it is {0}; its security certificate is not trusted by your computer's operating system. This may be caused by a misconfiguration or an attacker attempting to intercept your connection.";

            // Generic Network Error
            public const string GenericTitle = "This site can't be reached";
            public const string GenericSubtitleFormat = "wb was unable to load the webpage at {0}.";
            public const string GenericDescription = "Check your connection, verify that the address is correct, or try refreshing the page in a few moments.";

            // DNS / Server Not Found
            public const string DnsTitle = "Server Not Found";
            public const string DnsSubtitleFormat = "We couldn't resolve the domain name for {0}.";
            public const string DnsDescription = "The server's IP address could not be found. Check your DNS settings, ensure the address was spelled correctly, or try searching for the website.";

            // Internet Offline
            public const string OfflineTitle = "No Internet Connection";
            public const string OfflineSubtitle = "Your computer is currently offline.";
            public const string OfflineDescription = "Check your network cables, modem and router. If you are connected to Wi-Fi, try reconnecting or restarting your network adapter.";

            // Connection Refused
            public const string ConnectionRefusedTitle = "Connection Refused";
            public const string ConnectionRefusedSubtitleFormat = "The server at {0} refused the connection.";
            public const string ConnectionRefusedDescription = "The remote server may be temporarily down, undergoing maintenance, or rejecting incoming connections on the requested port.";

            // Connection Timed Out
            public const string TimedOutTitle = "Connection Timed Out";
            public const string TimedOutSubtitleFormat = "The server at {0} took too long to respond.";
            public const string TimedOutDescription = "The site took longer to respond than expected. The server may be experiencing high traffic or network congestion.";

            // Address Unreachable
            public const string AddressUnreachableTitle = "Address Unreachable";
            public const string AddressUnreachableSubtitleFormat = "Cannot route traffic to {0}.";
            public const string AddressUnreachableDescription = "The requested IP or host address could not be routed through your network gateway.";
        }

        public static class CrashReminder
        {
            public const string DialogTitle = "wb";
            public const string CrashNoticeWindowTitle = "Crash Notice - wb";
            public const string SimpleCrashPrompt = "wb closed unexpectedly during your previous session.\n\nWould you like to open the crashes folder?";
        }
    }
}
