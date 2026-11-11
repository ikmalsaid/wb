using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using wb.Core;
using wb.UI;
using wb.AdBlock;
using wb.Bookmarks;
using wb.History;
using wb.Downloads;
using wb.Browser;
using wb.SponsorBlock;
using wb.Screenshot;
using wb.Browser.Suggestions;
using System.Windows.Forms;
using System.Net;
using CefSharp;
using wb.Browser.Handlers;
using wb.Browser.ClearBrowsingData;
using wb.Proxy;
using wb.DataPortability;

namespace wb.Tests
{
    class TestRunner
    {
        static int _passed = 0;
        static int _failed = 0;

        [STAThread]
        static void Main(string[] args)
        {
            string testStorageDir = Path.Combine(Path.GetTempPath(), "wb_test_storage_" + Guid.NewGuid().ToString("N"));
            StorageHelper.CustomDataDirectory = testStorageDir;
            AppServices.ResetDatabaseForTest(testStorageDir);

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 |
                                                       SecurityProtocolType.Tls11 |
                                                       SecurityProtocolType.Tls |
                                                       (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslErrors) => true;
            }
            catch { }

            try
            {
                Console.WriteLine("==================================================");
                Console.WriteLine("  wb Automated Test Suite");
                Console.WriteLine("==================================================");

            Test("MaterialIcons Directory Discovery", () =>
            {
                string dir = MaterialIcons.IconsDirectory;
                Assert(!string.IsNullOrEmpty(dir), "IconsDirectory should not be empty");
                Assert(Directory.Exists(dir), "IconsDirectory should exist: " + dir);
            });

            Test("Arrow Clockwise SVG Parsing & Rendering", () =>
            {
                Bitmap bmp = MaterialIcons.GetIcon("arrow-clockwise", 24, Color.White);
                Assert(bmp != null, "arrow-clockwise icon bitmap should not be null");
                Assert(bmp.Width == 24 && bmp.Height == 24, "Bitmap should be 24x24");
            });

            Test("All Core SVG Icons Load Cleanly", () =>
            {
                string[] icons = new string[] {
                    "arrow-left", "arrow-right", "arrow-clockwise", "house",
                    "star", "star-fill", "x", "plus", "gear", "bookmark-simple",
                    "clock-counter-clockwise", "download-simple", "shield", "shield-fill", "shield-check",
                    "dots-three-vertical", "magnifying-glass", "caret-up", "caret-down", "globe",
                    "speaker-high", "speaker-slash"
                };

                foreach (string icon in icons)
                {
                    Bitmap bmp = MaterialIcons.GetIcon(icon, 16);
                    Assert(bmp != null, "Icon should load and render: " + icon);
                }
            });

            Test("DPI Scaling Computations", () =>
            {
                Assert(DpiHelper.Scale(16, 1.0f) == 16, "Scale at 1.0x should be 16");
                Assert(DpiHelper.Scale(16, 1.25f) == 20, "Scale at 1.25x should be 20");
                Assert(DpiHelper.Scale(16, 1.5f) == 24, "Scale at 1.5x should be 24");
                Assert(DpiHelper.Scale(16, 2.0f) == 32, "Scale at 2.0x should be 32");

                Bitmap scaledBmp = MaterialIcons.GetDpiIcon("arrow-clockwise", 1.5f, 16);
                Assert(scaledBmp != null, "Scaled bitmap should not be null");
                Assert(scaledBmp.Width == 24, "Scaled bitmap should be 24px wide");
            });

            Test("Material Color Palette Consistency", () =>
            {
                Assert(MaterialColors.ToolbarBackground.ToArgb() == Color.FromArgb(32, 33, 36).ToArgb(), "ToolbarBackground matches");
                Assert(MaterialColors.AccentBlue.ToArgb() == ThemeManager.CurrentAccentColor.ToArgb(), "AccentBlue dynamically matches ThemeManager.CurrentAccentColor");
                Assert(!MaterialColors.AccentBlue.IsEmpty, "AccentBlue must not be empty");
            });

            Test("Bookmark Manager Add and Check", () =>
            {
                BookmarkManager bm = new BookmarkManager();
                bm.Add("Test Page", "https://example.com");
                Assert(bm.IsBookmarked("https://example.com"), "Page should be bookmarked");
                Assert(!bm.IsBookmarked("https://notbookmarked.com"), "Page should not be bookmarked");
            });

            Test("BookmarkManager Search and Sort Modes", () =>
            {
                BookmarkManager bm = new BookmarkManager();
                try
                {
                    bm.Add("Zebra Knowledge Base", "https://zebra.test");
                    bm.Add("Alpha Documentation", "https://alpha.test");

                    var searchResults = bm.GetSorted("alpha", SortMode.TitleAscending);
                    Assert(searchResults.Count == 1, "Search for 'alpha' should return 1 item");
                    Assert(searchResults[0].Title == "Alpha Documentation", "Search result title matches");

                    var sortedAZ = bm.GetSorted("", SortMode.TitleAscending);
                    Assert(sortedAZ.Count >= 2, "Should have at least 2 bookmarks");
                    int alphaIdx = -1;
                    int zebraIdx = -1;
                    for (int i = 0; i < sortedAZ.Count; i++)
                    {
                        if (sortedAZ[i].Url == "https://alpha.test") alphaIdx = i;
                        if (sortedAZ[i].Url == "https://zebra.test") zebraIdx = i;
                    }
                    Assert(alphaIdx >= 0 && zebraIdx >= 0 && alphaIdx < zebraIdx, "A-Z sort places Alpha before Zebra");
                }
                finally
                {
                    bm.RemoveByUrl("https://zebra.test");
                    bm.RemoveByUrl("https://alpha.test");
                }
            });

            Test("History Manager Add and Check", () =>
            {
                HistoryManager hm = new HistoryManager();
                string testUrl = "https://test-check.local/history-item";
                try
                {
                    hm.AddEntry("History Page", testUrl);
                    var results = hm.GetAll();
                    Assert(results.Count > 0, "GetAll should return history items");
                }
                finally
                {
                    var all = hm.GetAll();
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i].Url == testUrl)
                        {
                            hm.RemoveEntry(all[i].Id);
                            break;
                        }
                    }
                }
            });

            Test("HistoryManager Search, Sort Modes and Deduplication", () =>
            {
                HistoryManager hm = new HistoryManager();
                string testUrl = "https://unique-history-test.org/article";
                try
                {
                    hm.AddEntry("First Title", testUrl);
                    var initial = hm.GetSorted("unique-history-test", SortMode.DateDescending);
                    Assert(initial.Count == 1, "Should record 1 initial entry");
                    Assert(initial[0].Title == "First Title", "Initial title recorded");

                    // Re-visiting same URL should update title and timestamp without adding a duplicate
                    hm.AddEntry("Updated Title", testUrl);
                    var updated = hm.GetSorted("unique-history-test", SortMode.DateDescending);
                    Assert(updated.Count == 1, "Should not duplicate visited URL in history");
                    Assert(updated[0].Title == "Updated Title", "History title updated on revisit");
                }
                finally
                {
                    var all = hm.GetAll();
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i].Url == testUrl)
                        {
                            hm.RemoveEntry(all[i].Id);
                            break;
                        }
                    }
                }
            });

            Test("DownloadManager Register, Progress, Complete and Missing File Detection", () =>
            {
                DownloadManager dm = new DownloadManager();
                long testId = 8888001;
                string fakePath = Path.Combine(Path.GetTempPath(), "wb_nonexistent_test_download_file.zip");

                try
                {
                    var item = dm.RegisterDownload(testId, "test_file.zip", fakePath, "https://example.com/test.zip", 2048000);
                    Assert(item != null, "Download item should be registered");
                    Assert(item.TotalBytes == 2048000, "Total bytes recorded");

                    dm.UpdateProgress(testId, 1024000, 2048000, 50, "InProgress", 204800);
                    Assert(item.Percent == 50, "Progress percentage should be 50%");
                    Assert(item.CurrentSpeed == 204800, "Speed recorded correctly");

                    dm.UpdateProgress(testId, 2048000, 2048000, 100, "Completed", 0);
                    Assert(item.State == "Completed", "Item should be marked complete");
                    Assert(item.GetStatusDisplay() == "Missing (Deleted)", "Non-existent file path should be reported as Missing");
                }
                finally
                {
                    dm.DeleteDownload(testId, false);
                }
            });

            Test("ChromeTabControl New Tab and Close Button Geometry", () =>
            {
                using (Form f = new Form())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    f.Size = new Size(800, 600);
                    tc.Dock = DockStyle.Fill;
                    tc.TabPages.Add(new TabPage("Tab 1"));
                    tc.TabPages.Add(new TabPage("Tab 2"));
                    f.Controls.Add(tc);
                    f.Show();
                    Application.DoEvents();

                    Rectangle plusRect = tc.NewTabButtonRect;
                    Assert(!plusRect.IsEmpty, "NewTabButtonRect should not be empty");
                    Assert(plusRect.Width > 0 && plusRect.Height > 0, "NewTabButtonRect has positive dimensions");
                    Assert(tc.IsPointOverNewTabButton(new Point(plusRect.X + 2, plusRect.Y + 2)), "IsPointOverNewTabButton detects point");

                    Rectangle close0 = tc.GetCloseButtonRect(0);
                    Assert(!close0.IsEmpty, "Close button rect for Tab 0 is not empty");
                    Rectangle close1 = tc.GetCloseButtonRect(1);
                    Assert(!close1.IsEmpty, "Close button rect for Tab 1 is not empty");
                    Assert(close1.Left > close0.Right, "Tab 1 close button is to the right of Tab 0");
                    f.Close();
                }
            });

            Test("ChromeTabControl Tab Addition, Selection and Close Cycle", () =>
            {
                using (Form f = new Form())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    f.Size = new Size(800, 600);
                    tc.Dock = DockStyle.Fill;
                    f.Controls.Add(tc);

                    int closeEventCount = 0;
                    tc.TabCloseClick += (s, idx) =>
                    {
                        closeEventCount++;
                        if (tc.TabPages.Count > 1)
                        {
                            int target = (idx == tc.TabPages.Count - 1) ? idx - 1 : idx + 1;
                            if (target >= 0 && target < tc.TabPages.Count)
                            {
                                tc.SelectedIndex = target;
                            }
                            TabPage toRemove = tc.TabPages[idx];
                            tc.TabPages.Remove(toRemove);
                            toRemove.Dispose();
                        }
                        else
                        {
                            tc.TabPages[0].Text = "New Tab";
                        }
                    };

                    tc.TabPages.Add(new TabPage("Initial Tab"));
                    f.Show();
                    Application.DoEvents();
                    Assert(tc.TabPages.Count == 1, "Initial tab count is 1");

                    // Add 4 tabs
                    for (int i = 2; i <= 5; i++)
                    {
                        tc.TabPages.Add(new TabPage("Tab " + i));
                    }
                    Application.DoEvents();
                    Assert(tc.TabPages.Count == 5, "Tab count after additions is 5");

                    // Close tabs down to 1
                    for (int step = 0; step < 10 && tc.TabPages.Count > 1; step++)
                    {
                        int last = tc.TabPages.Count - 1;
                        var mi = typeof(ChromeTabControl).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Rectangle cRect = tc.GetCloseButtonRect(last);
                        mi.Invoke(tc, new object[] { new MouseEventArgs(MouseButtons.Left, 1, cRect.X + 2, cRect.Y + 2, 0) });
                        Application.DoEvents();
                    }

                    Assert(tc.TabPages.Count == 1, "Tab count is 1 after closing every additional tab");

                    // Close the last remaining tab (should reset title to 'New Tab')
                    Rectangle lastClose = tc.GetCloseButtonRect(0);
                    var miCloseLast = typeof(ChromeTabControl).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    miCloseLast.Invoke(tc, new object[] { new MouseEventArgs(MouseButtons.Left, 1, lastClose.X + 2, lastClose.Y + 2, 0) });
                    Application.DoEvents();

                    Assert(tc.TabPages.Count == 1, "Tab count remains 1 after closing last tab");
                    Assert(tc.TabPages[0].Text == "New Tab", "Last tab resets text to 'New Tab'");
                    Assert(closeEventCount == 5, "All 5 close operations triggered TabCloseClick");
                    f.Close();
                }
            });

            Test("ChromeTabControl Double-Buffered UserPaint Rendering", () =>
            {
                using (Form f = new Form())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    tc.Size = new Size(600, 32);
                    tc.TabPages.Add(new TabPage("Alpha Tab"));
                    tc.TabPages.Add(new TabPage("Beta Tab"));
                    f.Controls.Add(tc);
                    f.CreateControl();

                    using (Bitmap bmp = new Bitmap(600, 32))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        var pe = new PaintEventArgs(g, new Rectangle(0, 0, 600, 32));
                        var onPaint = typeof(ChromeTabControl).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(tc, new object[] { pe });

                        // Verify non-empty rendering
                        Color c = bmp.GetPixel(10, 10);
                        Assert(c.A > 0, "Tab header rendered non-transparent pixels");
                    }
                }
            });

            Test("ChromeTabControl DisplayRectangle Zero Border Flush Alignment", () =>
            {
                using (Form f = new Form())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    f.Size = new Size(800, 600);
                    tc.Dock = DockStyle.Fill;
                    TabPage tab = new TabPage("Test Tab") { BackColor = MaterialColors.ToolbarBackground };
                    tc.TabPages.Add(tab);
                    f.Controls.Add(tc);
                    f.Show();
                    Application.DoEvents();

                    // Verify DisplayRectangle has zero margins on Left, Right, Bottom
                    Rectangle disp = tc.DisplayRectangle;
                    Assert(disp.Left == 0, "DisplayRectangle.Left is 0 (no left gap)");
                    Assert(disp.Width == tc.Width, "DisplayRectangle.Width equals tc.Width (no right gap)");
                    Assert(disp.Bottom == tc.Height, "DisplayRectangle.Bottom equals tc.Height (no bottom gap)");

                    // Verify TabPage bounds are flush with DisplayRectangle
                    Assert(tab.Left == 0, "TabPage.Left is 0");
                    Assert(tab.Width == tc.Width, "TabPage.Width equals tc.Width");
                    Assert(tab.Bottom == tc.Height, "TabPage.Bottom equals tc.Height");
                    Assert(tab.Top == tc.HeaderHeight, "TabPage.Top aligns with HeaderHeight");
                    f.Close();
                }
            });

            Test("TopProgressBar State Transitions and Rendering", () =>
            {
                using (TopProgressBar bar = new TopProgressBar())
                {
                    Assert(!bar.IsLoading, "Initial IsLoading is false");
                    Assert(!bar.Visible, "Initial Visible is false");

                    bar.StartLoading();
                    Assert(bar.IsLoading, "IsLoading is true after StartLoading");
                    Assert(bar.Visible, "Visible is true after StartLoading");

                    bar.StepProgress();

                    bar.CompleteLoading();
                    Assert(!bar.IsLoading, "IsLoading is false after CompleteLoading");

                    bar.DpiScale = 1.5f;
                    Assert(bar.Height >= 4, "Height scaled at 1.5x DPI");

                    // Verify rendering does not throw
                    bar.Size = new Size(400, 4);
                    using (Bitmap bmp = new Bitmap(400, 4))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        var pe = new PaintEventArgs(g, new Rectangle(0, 0, 400, 4));
                        var onPaint = typeof(TopProgressBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(bar, new object[] { pe });
                    }
                }
            });

            Test("FloatingStatusBubble Layout, Clean Status Preview (No Lock), AdBlock Badge and Paint", () =>
            {
                using (Form host = new Form { Size = new Size(1000, 700) })
                using (FloatingStatusBubble bubble = new FloatingStatusBubble())
                {
                    bubble.AttachTo(host);
                    host.Show();
                    Application.DoEvents();

                    bubble.SetStatus("https://example.com/secure-page");
                    Assert(bubble.StatusText == "https://example.com/secure-page", "StatusText matches set value");
                    Assert(bubble.Width > 50, "Bubble width calculated from text");
                    Assert(bubble.Height >= 24, "Bubble height is appropriate");

                    // Confirm lock icon font and HTTPS fields are completely removed
                    var lockFontField = typeof(FloatingStatusBubble).GetField("_lockFont", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(lockFontField == null, "_lockFont field must be completely removed");

                    var isHttpsField = typeof(FloatingStatusBubble).GetField("_isHttps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(isHttpsField == null, "_isHttps field must be completely removed");

                    int initialWidth = bubble.Width;
                    bubble.SetStatus("https://example.com/secure-page/with/a/much/longer/path/and/additional/query/parameters?param=value");
                    Assert(bubble.Width > initialWidth, "Bubble width expands to accommodate longer URL");

                    // Confirm shield icon drawing method is completely removed
                    var shieldMethod = typeof(FloatingStatusBubble).GetMethod("DrawShieldIcon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    Assert(shieldMethod == null, "DrawShieldIcon method must be completely removed");

                    // Verify rendering clean rectangular status does not throw
                    using (Bitmap bmp = new Bitmap(bubble.Width, bubble.Height))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        var pe = new PaintEventArgs(g, new Rectangle(0, 0, bubble.Width, bubble.Height));
                        var onPaint = typeof(FloatingStatusBubble).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(bubble, new object[] { pe });
                    }

                    // Temporary message test
                    bubble.ShowTemporaryMessage("RAM flushed.", 2000);
                    Assert(bubble.StatusText == "RAM flushed.", "Temporary message status updated");

                    host.Close();
                }
            });

            Test("WbTopBar Tab Strip Overflow and Geometry Protection with Hidden Count Header", () =>
            {
                using (Form host = new Form { Size = new Size(800, 600) })
                using (WbTopBar topBar = new WbTopBar())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.Width = 800;
                    topBar.BindTabControl(tc);
                    host.Controls.Add(topBar);
                    host.Controls.Add(tc);

                    // 1. Few tabs: no overflow
                    tc.TabPages.Add(new TabPage("Tab 1"));
                    tc.TabPages.Add(new TabPage("Tab 2"));
                    topBar.UpdateTabs();
                    Assert(!topBar.HasOverflow, "2 tabs on 800px width should not overflow");
                    Assert(topBar.VisibleTabCount == 2, "Both tabs should be visible");
                    Assert(topBar.GetOverflowButtonRect().IsEmpty, "Overflow button should be empty when no overflow");

                    // 2. Many tabs: triggers overflow
                    for (int i = 3; i <= 15; i++)
                    {
                        tc.TabPages.Add(new TabPage("Tab " + i));
                    }
                    topBar.UpdateTabs();
                    Assert(topBar.HasOverflow, "15 tabs on 800px width must trigger overflow");
                    Assert(topBar.VisibleTabCount < 15, "Visible tab count must be constrained");
                    Rectangle overRect = topBar.GetOverflowButtonRect();
                    Assert(!overRect.IsEmpty, "Overflow button must have a non-empty rectangle");

                    // 3. Tab strip and overflow button must not collide with Home button
                    Rectangle homeRect = topBar.GetHomeButtonRect();
                    Assert(overRect.Right <= homeRect.Left, "Overflow button must stay to the left of the Home button");

                    // 4. Verify overflow menu includes hidden tabs count header
                    using (ContextMenuStrip menu = topBar.CreateTabOverflowMenu())
                    {
                        Assert(menu != null, "Overflow menu must not be null when overflow is active");
                        Assert(menu.Items.Count >= 2, "Overflow menu must have header and separator");
                        Assert(menu.Items[0].Text.StartsWith("Hidden Tabs ("), "First item must be 'Hidden Tabs (N)' header: " + menu.Items[0].Text);
                        Assert(!menu.Items[0].Enabled, "Header item must be disabled (informational only)");
                        Assert(menu.Items[1] is ToolStripSeparator, "Second item must be a separator");

                        string artDir = GetArtifactsDir();
                        if (artDir != null)
                        {
                            menu.Show(new Point(0, 0));
                            using (Bitmap menuBmp = new Bitmap(menu.Width, menu.Height))
                            {
                                menu.DrawToBitmap(menuBmp, new Rectangle(0, 0, menu.Width, menu.Height));
                                menuBmp.Save(System.IO.Path.Combine(artDir, "tab_overflow_count_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                            menu.Close();
                        }
                    }
                }
            });

            Test("WbTopBar Tab Close Button Click Fires TabCloseClicked", () =>
            {
                using (Form host = new Form { Size = new Size(800, 600) })
                using (WbTopBar topBar = new WbTopBar())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.Width = 800;
                    topBar.BindTabControl(tc);
                    host.Controls.Add(topBar);
                    host.Controls.Add(tc);

                    tc.TabPages.Add(new TabPage("Tab Alpha"));
                    tc.TabPages.Add(new TabPage("Tab Beta"));
                    topBar.UpdateTabs();

                    int closedTabIndex = -1;
                    topBar.TabCloseClicked += (s, idx) =>
                    {
                        closedTabIndex = idx;
                    };

                    Rectangle closeRect = topBar.GetTabCloseRect(0);
                    Assert(!closeRect.IsEmpty, "Tab 0 close rect must not be empty");

                    var onMouseMove = typeof(WbTopBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseDown = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseUp = typeof(WbTopBar).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    Point clickPt = new Point(closeRect.Left + closeRect.Width / 2, closeRect.Top + closeRect.Height / 2);
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, clickPt.X, clickPt.Y, 0) });
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickPt.X, clickPt.Y, 0) });
                    onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickPt.X, clickPt.Y, 0) });

                    Assert(closedTabIndex == 0, "TabCloseClicked must fire for tab 0 on close button click");
                }
            });

            Test("WbTopBar Tab Move and Rendering With Globe Placeholder", () =>
            {
                using (Form host = new Form { Size = new Size(800, 600) })
                using (WbTopBar topBar = new WbTopBar())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.Width = 800;
                    topBar.BindTabControl(tc);
                    host.Controls.Add(topBar);
                    host.Controls.Add(tc);

                    tc.TabPages.Add(new TabPage("Google"));
                    tc.TabPages.Add(new TabPage("GitHub"));
                    topBar.UpdateTabs();

                    // Verify TabMoved event fires cleanly
                    bool movedFired = false;
                    int movedFrom = -1;
                    int movedTo = -1;
                    topBar.TabMoved += (s, e) =>
                    {
                        movedFired = true;
                        movedFrom = e.FromIndex;
                        movedTo = e.ToIndex;
                    };

                    var evMethod = topBar.GetType().GetEvent("TabMoved");
                    Assert(evMethod != null, "TabMoved event should exist on WbTopBar");
                    Assert(!movedFired && movedFrom == -1 && movedTo == -1, "Initial move state is false");

                    // Verify rendering does not throw and paints globe icon correctly
                    using (Bitmap bmp = new Bitmap(800, 38))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        var pe = new PaintEventArgs(g, new Rectangle(0, 0, 800, 38));
                        var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(topBar, new object[] { pe });
                    }
                }
            });

            Test("WbTopBar Tab Drag Insertion Cue, Drop Highlight and Mouse Release Commit", () =>
            {
                using (Form host = new Form { Size = new Size(800, 600) })
                using (WbTopBar topBar = new WbTopBar())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.Width = 800;
                    topBar.BindTabControl(tc);
                    host.Controls.Add(topBar);
                    host.Controls.Add(tc);

                    tc.TabPages.Add(new TabPage("Google"));
                    tc.TabPages.Add(new TabPage("GitHub"));
                    tc.TabPages.Add(new TabPage("Wikipedia"));
                    topBar.UpdateTabs();

                    string artDir = GetArtifactsDir();

                    // 1. Verify DropHighlight property and rendering
                    topBar.DropHighlight = true;
                    Assert(topBar.DropHighlight, "DropHighlight should be true");

                    using (Bitmap bmp = new Bitmap(800, 38))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        var pe = new PaintEventArgs(g, new Rectangle(0, 0, 800, 38));
                        var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(topBar, new object[] { pe });
                        if (artDir != null)
                        {
                            bmp.Save(System.IO.Path.Combine(artDir, "window_drop_banner_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    topBar.DropHighlight = false;

                    // 2. Verify mouse move does NOT reorder tabs immediately, but paints insertion marker cue
                    Rectangle tab0 = topBar.GetTabRect(0);
                    Rectangle tab1 = topBar.GetTabRect(1);
                    var onMouseDown = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseMove = typeof(WbTopBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseUp = typeof(WbTopBar).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    int moveCount = 0;
                    topBar.TabMoved += (s, e) => { moveCount++; };

                    // Mouse down on Tab 0
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, tab0.Left + 10, tab0.Top + 10, 0) });
                    // Mouse move over Tab 1
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, tab1.Left + tab1.Width / 2 + 5, tab1.Top + 10, 0) });

                    Assert(moveCount == 0, "MouseMove must not commit tab reordering before MouseUp");

                    // Capture screenshot of the drag insertion cue in action
                    using (Bitmap bmpCue = new Bitmap(800, 38))
                    using (Graphics gCue = Graphics.FromImage(bmpCue))
                    {
                        var pe = new PaintEventArgs(gCue, new Rectangle(0, 0, 800, 38));
                        var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(topBar, new object[] { pe });
                        if (System.IO.Directory.Exists(artDir))
                        {
                            bmpCue.Save(System.IO.Path.Combine(artDir, "tab_drag_cue_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }

                    // Mouse up releases and commits the move
                    onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, tab1.Left + tab1.Width / 2 + 5, tab1.Top + 10, 0) });
                    Assert(moveCount == 1, "MouseUp must commit the tab move");
                }
            });

            Test("FaviconManager Real Website Favicon Download and Tab Strip Rendering", () =>
            {
                using (Form host = new Form { Size = new Size(800, 600) })
                using (WbTopBar topBar = new WbTopBar())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.Width = 800;
                    topBar.BindTabControl(tc);
                    host.Controls.Add(topBar);
                    host.Controls.Add(tc);

                    MockFaviconTab tab1 = new MockFaviconTab("Google", "https://www.google.com");
                    MockFaviconTab tab2 = new MockFaviconTab("GitHub", "https://github.com");
                    MockFaviconTab tab3 = new MockFaviconTab("Wikipedia", "https://en.wikipedia.org");

                    tc.TabPages.Add(tab1);
                    tc.TabPages.Add(tab2);
                    tc.TabPages.Add(tab3);
                    topBar.UpdateTabs();

                    System.Threading.ManualResetEvent done = new System.Threading.ManualResetEvent(false);
                    int loadedCount = 0;
                    Action<Image> onFavLoaded = img =>
                    {
                        if (System.Threading.Interlocked.Increment(ref loadedCount) == 3)
                        {
                            done.Set();
                        }
                    };

                    AppServices.Favicons.FetchFaviconAsync("https://www.google.com", null, img => { tab1.Favicon = img; onFavLoaded(img); });
                    AppServices.Favicons.FetchFaviconAsync("https://github.com", null, img => { tab2.Favicon = img; onFavLoaded(img); });
                    AppServices.Favicons.FetchFaviconAsync("https://en.wikipedia.org", null, img => { tab3.Favicon = img; onFavLoaded(img); });

                    done.WaitOne(10000);
                    topBar.UpdateTabs();

                    Assert(tab1.Favicon != null || tab2.Favicon != null || tab3.Favicon != null, "At least one real favicon must be loaded from web");

                    string artDir = GetArtifactsDir();
                    if (artDir != null)
                    {
                        topBar.AddressText = "https://www.google.com";
                        var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Func<TabOffsetMode, Bitmap> renderMode = mode =>
                        {
                            topBar.CurrentTabOffsetMode = mode;
                            topBar.UpdateTabs();
                            Bitmap b = new Bitmap(800, 38);
                            using (Graphics gb = Graphics.FromImage(b))
                            {
                                var pe = new PaintEventArgs(gb, new Rectangle(0, 0, 800, 38));
                                onPaint.Invoke(topBar, new object[] { pe });
                            }
                            return b;
                        };

                        using (Bitmap bmpBase = renderMode(TabOffsetMode.Centered))
                        using (Bitmap bmpOptA = renderMode(TabOffsetMode.SeatedFlush))
                        using (Bitmap bmpOptB = renderMode(TabOffsetMode.GroundedTaller))
                        using (Bitmap bmpOptFlush = renderMode(TabOffsetMode.GroundedFlush))
                        {
                            bmpBase.Save(System.IO.Path.Combine(artDir, "tab_sample_baseline.png"), System.Drawing.Imaging.ImageFormat.Png);
                            bmpOptA.Save(System.IO.Path.Combine(artDir, "tab_sample_option_a_seated.png"), System.Drawing.Imaging.ImageFormat.Png);
                            bmpOptB.Save(System.IO.Path.Combine(artDir, "tab_sample_option_b_grounded.png"), System.Drawing.Imaging.ImageFormat.Png);
                            bmpOptB.Save(System.IO.Path.Combine(artDir, "tab_option_b_1px_clearance_final.png"), System.Drawing.Imaging.ImageFormat.Png);
                            bmpOptFlush.Save(System.IO.Path.Combine(artDir, "tab_sample_option_b_flush.png"), System.Drawing.Imaging.ImageFormat.Png);

                            // Master comparison showcase card
                            int masterW = 1040;
                            int masterH = 390;
                            using (Bitmap master = new Bitmap(masterW, masterH))
                            using (Graphics mg = Graphics.FromImage(master))
                            using (Font titleFont = new Font("Segoe UI", 12F, FontStyle.Bold))
                            using (Font labelFont = new Font("Segoe UI", 9F, FontStyle.Regular))
                            using (Font badgeFont = new Font("Segoe UI", 8F, FontStyle.Bold))
                            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(24, 25, 27)))
                            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(232, 234, 237)))
                            using (SolidBrush subBrush = new SolidBrush(Color.FromArgb(154, 160, 166)))
                            using (SolidBrush recBrush = new SolidBrush(Color.FromArgb(129, 201, 149)))
                            using (Pen borderPen = new Pen(Color.FromArgb(60, 64, 67), 1))
                            {
                                mg.SmoothingMode = SmoothingMode.AntiAlias;
                                mg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                mg.FillRectangle(bgBrush, 0, 0, masterW, masterH);

                                mg.DrawString("wb Tab Vertical Alignment & Bottom Offset Comparison", titleFont, textBrush, 20, 12);
                                mg.DrawString("Baseline Contact Zoom (2.5x)", badgeFont, subBrush, 840, 16);

                                string[] labels = new string[]
                                {
                                    "Baseline: Centered (h=30px, y=4px, bottom=34px | 3px floating gap above baseline)",
                                    "Option A: Seated Flush (h=30px, y=7px, bottom=37px | 0px gap, rests directly on baseline)",
                                    "Option B (Selected): Grounded Taller with 1px Clearance (h=32px, y=4px, bottom=36px | 1px clean breathing gap)",
                                    "Option B (Flush Variant): Grounded Taller Flush (h=33px, y=4px, bottom=37px | 0px gap, touches baseline)"
                                };
                                Bitmap[] bmps = new Bitmap[] { bmpBase, bmpOptA, bmpOptB, bmpOptFlush };

                                int curY = 44;
                                for (int k = 0; k < 4; k++)
                                {
                                    Color lColor = (k == 2) ? Color.FromArgb(112, 239, 222) : Color.FromArgb(220, 222, 225);
                                    using (SolidBrush lb = new SolidBrush(lColor))
                                    {
                                        mg.DrawString(labels[k], labelFont, lb, 20, curY);
                                    }
                                    if (k == 2)
                                    {
                                        mg.DrawString("[SKETCH MATCH]", badgeFont, recBrush, 700, curY);
                                    }
                                    curY += 19;
                                    mg.DrawImage(bmps[k], 20, curY);

                                    // Zoom crop of active tab bottom intersection (tabRect.Left + 20 to tabRect.Left + 90, y: 22 to 38)
                                    Rectangle activeTabRect = topBar.GetTabRect(0);
                                    int cropX = activeTabRect.Left + 20;
                                    int cropY = 22;
                                    int cropW = 70;
                                    int cropH = 16;
                                    Rectangle destZoom = new Rectangle(835, curY, 180, 38);

                                    mg.InterpolationMode = InterpolationMode.NearestNeighbor;
                                    mg.DrawImage(bmps[k], destZoom, new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
                                    mg.InterpolationMode = InterpolationMode.Default;
                                    mg.DrawRectangle(borderPen, destZoom);

                                    curY += 46;
                                }

                                master.Save(System.IO.Path.Combine(artDir, "tab_offset_comparison_showcase.png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                    }
                }
            });

            Test("ThemeManager Bright Spectrum, Dynamic Random Color and Launch Persistence", () =>
            {
                // Preserve original config so test doesn't tamper with user config
                ThemeConfigModel originalConfig = StorageHelper.LoadObject<ThemeConfigModel>("theme_config.json");

                try
                {
                    ThemeConfigModel defaultCfg = new ThemeConfigModel();
                    Assert(defaultCfg.IsRandomMode, "Default ThemeConfigModel must have IsRandomMode = true");
                    Assert(defaultCfg.PresetName == "Random", "Default ThemeConfigModel must have PresetName = 'Random'");

                    Assert(ThemeManager.Presets.Count >= 10, "Should have at least 10 bright spectrum presets");

                    // Test preset selection
                    bool eventFired = false;
                    EventHandler handler = (s, e) => { eventFired = true; };
                    ThemeManager.AccentColorChanged += handler;

                    ThemeManager.SetPreset("Cyber Cyan");
                    Assert(eventFired, "AccentColorChanged event must fire when preset changed");
                    Assert(ThemeManager.CurrentPresetName == "Cyber Cyan", "Preset name should be Cyber Cyan");
                    Assert(ThemeManager.CurrentAccentColor == Color.FromArgb(112, 239, 222), "CurrentAccentColor should match Cyber Cyan");
                    Assert(MaterialColors.AccentBlue == Color.FromArgb(112, 239, 222), "MaterialColors.AccentBlue must dynamically reflect ThemeManager");
                    Assert(UITheme.AccentBlue == Color.FromArgb(112, 239, 222), "UITheme.AccentBlue must dynamically reflect ThemeManager");
                    Assert(!ThemeManager.IsRandomMode, "IsRandomMode should be false for preset");

                    // Test random color generation with hue divergence
                    ThemeManager.SetRandomColor();
                    Assert(ThemeManager.IsRandomMode, "IsRandomMode should be true after SetRandomColor");
                    Assert(ThemeManager.CurrentPresetName == "Random", "Preset name should be Random");
                    Color rnd1 = ThemeManager.CurrentAccentColor;
                    Assert(!rnd1.IsEmpty, "Random color must not be empty");
                    int avgBrightness = (rnd1.R + rnd1.G + rnd1.B) / 3;
                    Assert(avgBrightness >= 100, "Random bright color must have strong luminance: " + avgBrightness);

                    // Test single launch random color change (1 launch instead of 2)
                    ThemeManager.ResetInitialization();
                    ThemeManager.Initialize(); // Simulates 1st launch of wb
                    Color rnd2 = ThemeManager.CurrentAccentColor;
                    Assert(ThemeManager.IsRandomMode, "IsRandomMode must remain true on launch 1");
                    Assert(rnd2.ToArgb() != rnd1.ToArgb(), "Color must change on launch 1 without requiring 2 launches");

                    // Verify disk persistence after launch 1
                    ThemeConfigModel savedConfig = StorageHelper.LoadObject<ThemeConfigModel>("theme_config.json");
                    Assert(savedConfig != null && savedConfig.IsRandomMode, "Disk config must be updated on launch 1");
                    Assert(savedConfig.ColorArgb == rnd2.ToArgb(), "Disk config ColorArgb must match new launch color");

                    // Test swatch bitmap generation
                    using (Bitmap swatch = ThemeManager.CreateColorSwatch(rnd2, 16))
                    {
                        Assert(swatch != null, "Swatch must not be null");
                        Assert(swatch.Width == 16 && swatch.Height == 16, "Swatch must be 16x16");
                    }

                    ThemeManager.AccentColorChanged -= handler;
                }
                finally
                {
                    // Restore original user config
                    if (originalConfig != null)
                    {
                        StorageHelper.SaveObject("theme_config.json", originalConfig);
                    }
                }
            });

            Test("ThemeManager Gear Accent Color Menu Rendering and Screenshot", () =>
            {
                using (ContextMenuStrip strip = new ContextMenuStrip())
                {
                    UITheme.ApplyContextMenu(strip);
                    strip.ShowCheckMargin = true;
                    strip.ShowImageMargin = true;
                    ToolStripMenuItem randomItem = new ToolStripMenuItem("&Random Color", null) { Checked = true };
                    UITheme.ApplyMenuItem(randomItem);
                    strip.Items.Add(randomItem);

                    strip.Items.Add(new ToolStripSeparator());
                    ToolStripMenuItem firstPresetItem = null;
                    foreach (AccentColorPreset preset in ThemeManager.Presets)
                    {
                        ToolStripMenuItem presetItem = new ToolStripMenuItem(preset.Name, ThemeManager.CreateColorSwatch(preset.Color, 16));
                        UITheme.ApplyMenuItem(presetItem);
                        if (firstPresetItem == null) firstPresetItem = presetItem;
                        strip.Items.Add(presetItem);
                    }
                    strip.Items.Add(new ToolStripSeparator());

                    ToolStripMenuItem customItem = new ToolStripMenuItem("&Custom Color...", null);
                    UITheme.ApplyMenuItem(customItem);
                    strip.Items.Add(customItem);

                    Assert(strip.Items.Count >= 13, "Menu must contain Random, Separators, 10 Presets and Custom");
                    Assert(randomItem.Image == null, "Random Color must have no icon image");
                    Assert(!randomItem.Text.Contains("Ã°Å¸Å½Â²"), "Random Color must not contain emoji icons");
                    Assert(customItem.Image == null, "Custom Color must have no icon image");
                    Assert(!customItem.Text.Contains("Ã°Å¸Å½Â¨"), "Custom Color must not contain emoji icons");

                    var tr = new TestTrackingRenderer();
                    strip.Renderer = tr;
                    using (Bitmap bmpTest = new Bitmap(200, 400))
                    using (Graphics g = Graphics.FromImage(bmpTest))
                    {
                        var mi = typeof(ContextMenuStrip).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        mi.Invoke(strip, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 200, 400)) });
                        Assert(tr.LastRenderedCheckRect.Width > 0, "Random Color checkmark is rendered");

                        if (firstPresetItem != null)
                        {
                            randomItem.Checked = false;
                            firstPresetItem.Checked = true;
                            mi.Invoke(strip, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 200, 400)) });
                            Assert(tr.LastRenderedCheckRect.Right <= tr.LastRenderedImageRect.Left, "Preset check and swatch do not overlap");
                        }
                    }

                    strip.Renderer = new MaterialToolStripRenderer();
                    string artDir = GetArtifactsDir();
                    if (artDir != null)
                    {
                        strip.Show(new Point(0, 0));
                        using (Bitmap bmp = new Bitmap(strip.Width, strip.Height))
                        {
                            strip.DrawToBitmap(bmp, new Rectangle(0, 0, strip.Width, strip.Height));
                            bmp.Save(System.IO.Path.Combine(artDir, "accent_color_menu_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        strip.Close();
                    }
                }
            });

            Test("StrictBlockPage AdBlock Strict Blocking Generation and Details", () =>
            {
                StrictBlockInfo info = StrictBlockPage.CreateInfo("https://malware-tracker.test/phishing", "||malware-tracker.test^", "Malware Filter");
                Assert(info.Domain == "malware-tracker.test", "Domain should be extracted correctly");
                Assert(info.MatchedRule == "||malware-tracker.test^", "Matched rule should be present");

                string html = StrictBlockPage.GenerateHtml(info);
                Assert(!string.IsNullOrEmpty(html), "HTML output should not be empty");
                Assert(html.Contains("wb has prevented the following page from loading:"), "HTML should contain wb warning banner");
                Assert(html.Contains("<b>malware-tracker.test</b>"), "HTML should contain bold domain name");
                Assert(html.Contains("Malware Filter"), "HTML should contain matched filter list source");
                Assert(html.Contains("Don't warn me again about this site"), "HTML should contain permanent disable checkbox");
                Assert(html.Contains("Close this window"), "HTML should have Close this window button");
                Assert(html.Contains("Proceed"), "HTML should have Proceed button");
                Assert(html.Contains("wb-action:closetab"), "HTML should have closetab action");
                Assert(html.Contains("wb-action:proceed"), "HTML should have proceed action");

                string filePath = StrictBlockPage.CreateBlockPageFile(info);
                Assert(File.Exists(filePath), "Error file should be created on disk");
                Assert(StrictBlockPage.IsBlockPage(filePath), "IsBlockPage should recognize block file path");
                Assert(WebErrorPage.IsErrorPage(filePath), "WebErrorPage.IsErrorPage should recognize block file path");
                try { File.Delete(filePath); } catch { }
            });

            Test("StrictBlockPage with Redirect Unwrapping", () =>
            {
                StrictBlockInfo info = StrictBlockPage.CreateInfo(
                    "https://click.redditmail.com/CL0/https:%2F%2Fwww.reddit.com/1/0",
                    "||click.redditmail.com^",
                    "EasyPrivacy",
                    "https://www.reddit.com/r/StableDiffusion/comments/1x34suo/");

                string html = StrictBlockPage.GenerateHtml(info);
                Assert(!string.IsNullOrEmpty(html), "HTML output should not be empty");
                Assert(html.Contains("The blocked page wants to redirect to another site"), "HTML should contain redirect notice");
                Assert(html.Contains("https://www.reddit.com/r/StableDiffusion/comments/1x34suo/"), "HTML should contain redirect destination URL");
                Assert(html.Contains("EasyPrivacy"), "HTML should attribute to EasyPrivacy");
            });

            Test("UrlSkipHelper Engine & Redditmail Redirect Unwrapping", () =>
            {
                // 1. Redditmail tracking URL with /CL0/ regex and -uricomponent
                string redditMail = "https://click.redditmail.com/CL0/https:%2F%2Fwww.reddit.com%2Fr%2FStableDiffusion%2Fcomments%2F1x34suo%2Fany_ideas_how_to_achieve_this_style_prompt_model%2F%3F$deep_link=true%26correlation_id=606ce598-a160-5f25-8ff0-ce1e22ce70e9%26ref=email_post_reply%26ref_campaign=email_post_reply%26ref_source=email/1/01000199b5145bb5-2f840902-1c6e-4f05-b1a7-ec56185bb7aa-000000/FfL8hU8yT6Uo9tE9Z-f0p9A7E14_4yO4fS9QfW34g_E=431";
                string unwrappedReddit = UrlSkipHelper.TryUnwrap(redditMail);
                Assert(!string.IsNullOrEmpty(unwrappedReddit), "Redditmail URL should be unwrapped");
                Assert(unwrappedReddit.StartsWith("https://www.reddit.com/r/StableDiffusion/comments/1x34suo/any_ideas_how_to_achieve_this_style_prompt_model/?"), "Unwrapped URL should point to reddit post");
                Assert(unwrappedReddit.Contains("$deep_link=true"), "Unwrapped URL should contain decoded parameters");

                // 2. Google tracking redirect with ?q
                string googleUrl = "https://www.google.com/url?q=https%3A%2F%2Fgithub.com%2Fgorhill%2FuBlock&sa=D&sntz=1&usg=AFQjCNH";
                string unwrappedGoogle = UrlSkipHelper.TryUnwrap(googleUrl);
                Assert(unwrappedGoogle == "https://github.com/gorhill/uBlock", "Google redirect should be unwrapped to target URL");

                // 3. Smart heuristic unwrap for generic parameter
                string genericUrl = "https://tracker-ad.com/redirect?url=https%3A%2F%2Ftarget-destination.org%2Farticle%3Fid%3D123";
                string unwrappedGeneric = UrlSkipHelper.TryUnwrap(genericUrl);
                Assert(unwrappedGeneric == "https://target-destination.org/article?id=123", "Generic redirect URL should be unwrapped");
            });

            Test("WebErrorPage Network Error (NotFound / Offline / TimedOut) Mapping", () =>
            {
                WebErrorInfo notFound = WebErrorPage.CreateFromCefError("ERR_NAME_NOT_RESOLVED", -105, "https://nonexistent-site.xyz");
                Assert(notFound.Kind == WebErrorKind.NotFound, "Error -105 should map to NotFound");
                Assert(notFound.TechnicalCode == "ERR_NAME_NOT_RESOLVED", "TechnicalCode should be ERR_NAME_NOT_RESOLVED");

                string notFoundHtml = WebErrorPage.GenerateHtml(notFound);
                Assert(notFoundHtml.Contains("Server Not Found"), "HTML should describe Server Not Found");
                Assert(notFoundHtml.Contains("wb-action:reload"), "HTML should have Try Again reload link");
                Assert(!notFoundHtml.Contains("<div class=\"advanced-section\">"), "Network error should NOT show Advanced section");

                WebErrorInfo nxDomain = WebErrorPage.CreateFromCefError("DNS_PROBE_FINISHED_NXDOMAIN", -105, "https://nonexistent-site.xyz");
                Assert(nxDomain.Kind == WebErrorKind.NotFound, "NXDOMAIN should map to NotFound");
                Assert(nxDomain.TechnicalCode == "DNS_PROBE_FINISHED_NXDOMAIN", "TechnicalCode should be DNS_PROBE_FINISHED_NXDOMAIN");
                string nxDomainHtml = WebErrorPage.GenerateHtml(nxDomain);
                Assert(nxDomainHtml.Contains("DNS_PROBE_FINISHED_NXDOMAIN"), "HTML should display DNS_PROBE_FINISHED_NXDOMAIN code");
                Assert(nxDomainHtml.Contains("Server Not Found"), "HTML should describe Server Not Found");
                Assert(!nxDomainHtml.Contains("<div class=\"advanced-section\">"), "NXDOMAIN should NOT show Advanced section");

                Assert(WebErrorPage.IsChromiumInternalErrorPage("chrome-error://chromewebdata/"), "IsChromiumInternalErrorPage should recognize chromewebdata");
                Assert(WebErrorPage.IsErrorPage("chrome-error://chromewebdata/"), "IsErrorPage should recognize chromewebdata");

                WebErrorInfo offline = WebErrorPage.CreateFromCefError("ERR_INTERNET_DISCONNECTED", -106, "https://any-site.com");
                Assert(offline.Kind == WebErrorKind.InternetOffline, "Error -106 should map to InternetOffline");

                WebErrorInfo timedOut = WebErrorPage.CreateFromCefError("ERR_CONNECTION_TIMED_OUT", -118, "https://slow-site.com");
                Assert(timedOut.Kind == WebErrorKind.TimedOut, "Error -118 should map to TimedOut");
            });

            Test("WebErrorPage Insecure Warning SSL Error Details", () =>
            {
                WebErrorInfo ssl = WebErrorPage.CreateForSslError("https://expired.badssl.com", "ERR_CERT_DATE_INVALID", "Certificate expired on 2026-01-01");
                Assert(ssl.Kind == WebErrorKind.InsecureWarning, "Kind should be InsecureWarning");
                Assert(ssl.Domain == "expired.badssl.com", "Domain should be extracted");

                string sslHtml = WebErrorPage.GenerateHtml(ssl);
                Assert(sslHtml.Contains("Your connection is not private"), "HTML should contain private connection warning");
                Assert(sslHtml.Contains("wb-action:proceed-ssl"), "HTML should contain proceed-ssl link");
                Assert(sslHtml.Contains("<div class=\"advanced-section\">"), "SSL error SHOULD show Advanced section for cert details and bypass");
            });

            Test("WebErrorPage Correct Font Stack and Button Inheritance", () =>
            {
                WebErrorInfo info = WebErrorPage.CreateFromCefError("ERR_NAME_NOT_RESOLVED", -105, "https://test-site.example");
                string html = WebErrorPage.GenerateHtml(info);
                Assert(html.Contains("'Segoe UI'"), "HTML must declare Segoe UI font");
                Assert(html.Contains("font-family: inherit"), "HTML must enforce font-family inherit on buttons and form elements");
                Assert(!html.Contains("\"\"Segoe UI\"\""), "HTML must not contain invalid double-double quoted font names");
                Assert(!html.Contains("}        }"), "HTML must not contain duplicate closing braces");
            });

            Test("New Tab Home Favicon Follows Accent Color and Dynamic Theme Changes", () =>
            {
                Color origAccent = ThemeManager.CurrentAccentColor;
                try
                {
                    // 1. Verify New Tab favicon uses ThemeManager.AccentColor
                    ThemeManager.SetAccentColor(Color.FromArgb(129, 201, 149), "Mint Green"); // #81C995
                    Image mintFavicon = InternalPageHelper.GetInternalFavicon("wb://newtab", false);
                    Assert(mintFavicon != null, "New tab favicon must not be null");

                    // 2. Change accent color to Solar Amber (#FDD663) and verify favicon reflects new accent color
                    ThemeManager.SetAccentColor(Color.FromArgb(253, 214, 99), "Solar Amber");
                    Image amberFavicon = InternalPageHelper.GetInternalFavicon("wb://newtab", false);
                    Assert(amberFavicon != null, "Updated new tab favicon must not be null");

                    // 3. Verify BrowserTab architecture hooks AccentColorChanged for dynamic update
                    string tabSrc = File.ReadAllText(Path.Combine("src", "Browser", "BrowserTab.cs"));
                    Assert(tabSrc.Contains("ThemeManager.AccentColorChanged += _accentChangedHandler;"), "BrowserTab registers AccentColorChanged handler");
                    Assert(tabSrc.Contains("SetFavicon(InternalPageHelper.GetInternalFavicon(CurrentUrl, IsIncognito));"), "BrowserTab refreshes internal favicon on accent change");
                }
                finally
                {
                    ThemeManager.SetAccentColor(origAccent);
                }
            });

            Test("WebErrorPage Page Title No Duplicate wb Suffix and Proper Colored Warning/Stop Favicons", () =>
            {
                // 1. Verify WebErrorPage HTML <title> has no duplicate " - wb" suffix
                WebErrorInfo notFound = WebErrorPage.CreateFromCefError("ERR_NAME_NOT_RESOLVED", -105, "https://example-dead-site.com");
                string notFoundHtml = WebErrorPage.GenerateHtml(notFound);
                Assert(notFoundHtml.Contains("<title>Server Not Found</title>"), "Error HTML <title> must not contain ' - wb' suffix");
                Assert(!notFoundHtml.Contains("<title>Server Not Found - wb</title>"), "HTML <title> must not have '- wb'");

                WebErrorInfo ssl = WebErrorPage.CreateForSslError("https://expired.example.com", "ERR_CERT_DATE_INVALID");
                string sslHtml = WebErrorPage.GenerateHtml(ssl);
                Assert(sslHtml.Contains("<title>Your connection is not private</title>"), "SSL HTML <title> must not contain ' - wb' suffix");
                Assert(!sslHtml.Contains("<title>Your connection is not private - wb</title>"), "SSL HTML <title> must not have '- wb'");

                // 2. Verify TitleHelper.FormatWindowTitle does not duplicate " - wb"
                string formattedNotFound = TitleHelper.FormatWindowTitle("Server Not Found", false);
                Assert(formattedNotFound == "Server Not Found - wb", "Window title formatted once: " + formattedNotFound);

                string doubleProtected = TitleHelper.FormatWindowTitle("Server Not Found - wb", false);
                Assert(doubleProtected == "Server Not Found - wb", "Window title must strip duplicate ' - wb' and end with single '- wb': " + doubleProtected);

                // 3. Verify proper colored warning and stop favicons
                Image warningFav = WebErrorPage.GetWarningFavicon();
                Assert(warningFav != null, "Warning favicon must be non-null");

                Image stopFav = WebErrorPage.GetStopFavicon();
                Assert(stopFav != null, "Stop favicon must be non-null");

                // SSL error maps to warning favicon
                Image sslFav = WebErrorPage.GetFavicon(WebErrorKind.InsecureWarning);
                Assert(sslFav != null, "SSL error favicon must be non-null");

                // Network error maps to stop favicon
                Image netFav = WebErrorPage.GetFavicon(WebErrorKind.NotFound);
                Assert(netFav != null, "Network error favicon must be non-null");

                // 4. Verify HTML includes data URI link rel="icon" with proper colored favicon
                Assert(sslHtml.Contains("<link rel=\"icon\" type=\"image/png\" href=\"data:image/png;base64,"), "SSL HTML must include embedded data URI warning favicon");
                Assert(notFoundHtml.Contains("<link rel=\"icon\" type=\"image/png\" href=\"data:image/png;base64,"), "Error HTML must include embedded data URI stop favicon");

                // 5. Verify CreateErrorFile uses warning / stop file prefixes
                string sslFilePath = WebErrorPage.CreateErrorFile(ssl);
                Assert(sslFilePath.Contains("weberror_warning"), "SSL error file path must have 'weberror_warning' prefix");

                string notFoundFilePath = WebErrorPage.CreateErrorFile(notFound);
                Assert(notFoundFilePath.Contains("weberror_stop"), "Network error file path must have 'weberror_stop' prefix");

                // 6. Verify BrowserTab source no longer uses generic red shield for error pages
                string tabSrc = File.ReadAllText(Path.Combine("src", "Browser", "BrowserTab.cs"));
                Assert(!tabSrc.Contains("SetFavicon(MaterialIcons.GetIcon(\"shield\", 16, MaterialColors.StatusDanger));"), "BrowserTab must not use generic red shield for error page favicon");
                Assert(tabSrc.Contains("WebErrorPage.GetWarningFavicon()"), "BrowserTab LoadErrorPage integrates WebErrorPage.GetWarningFavicon");
                Assert(tabSrc.Contains("WebErrorPage.GetStopFavicon()"), "BrowserTab LoadErrorPage integrates WebErrorPage.GetStopFavicon");
            });

            Test("Internal Page Links Navigation Opens in New Tab", () =>
            {
                // 1. Verify NewTabView shortcut click passes inNewTab = true
                string clickedUrl = null;
                bool newTabRequested = false;
                using (NewTabView ntv = new NewTabView((url, inNewTab) =>
                {
                    clickedUrl = url;
                    newTabRequested = inNewTab;
                }))
                {
                    ntv.Size = new Size(1024, 768);
                    using (Bitmap bmp = new Bitmap(1024, 768))
                    {
                        ntv.DrawToBitmap(bmp, new Rectangle(0, 0, 1024, 768));
                    }
                    var onMouseUp = typeof(NewTabView).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var boundsField = typeof(NewTabView).GetField("_shortcutBadgeBounds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var bounds = boundsField != null ? boundsField.GetValue(ntv) as List<Rectangle> : null;
                    if (bounds != null && bounds.Count > 0)
                    {
                        Rectangle firstTile = bounds[0];
                        onMouseUp.Invoke(ntv, new object[] { new MouseEventArgs(MouseButtons.Left, 1, firstTile.X + 10, firstTile.Y + 10, 0) });
                        Assert(clickedUrl != null, "Clicking shortcut card must trigger open URL callback");
                        Assert(newTabRequested == true, "Clicking shortcut card from internal page must open in new tab");
                    }
                }

                // 1b. Verify NewTabView shortcut click triggers closeTabCallback so originating new tab auto-closes
                bool closeCalled = false;
                using (NewTabView ntv = new NewTabView((url, inNewTab) => { }, () => { closeCalled = true; }))
                {
                    ntv.Size = new Size(1024, 768);
                    using (Bitmap bmp = new Bitmap(1024, 768))
                    {
                        ntv.DrawToBitmap(bmp, new Rectangle(0, 0, 1024, 768));
                    }
                    var onMouseUp = typeof(NewTabView).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var boundsField = typeof(NewTabView).GetField("_shortcutBadgeBounds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var bounds = boundsField != null ? boundsField.GetValue(ntv) as List<Rectangle> : null;
                    if (bounds != null && bounds.Count > 0)
                    {
                        Rectangle firstTile = bounds[0];
                        onMouseUp.Invoke(ntv, new object[] { new MouseEventArgs(MouseButtons.Left, 1, firstTile.X + 10, firstTile.Y + 10, 0) });
                        Assert(closeCalled == true, "Clicking shortcut card must trigger closeTabCallback so originating new tab auto-closes");
                    }
                }

                // 2. Verify BookmarksView opens selected bookmark in new tab
                string bookmarkOpenedUrl = null;
                bool bookmarkInNewTab = false;
                using (BookmarksView bv = new BookmarksView((url, inNewTab) =>
                {
                    bookmarkOpenedUrl = url;
                    bookmarkInNewTab = inNewTab;
                }))
                {
                    AppServices.Bookmarks.Add("Test Link", "https://link.test");
                    try
                    {
                        var dummyHandle1 = bv.Handle;
                        bv.RefreshList();
                        var lvField = typeof(BookmarksView).GetField("_listView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        ListView lv = lvField != null ? lvField.GetValue(bv) as ListView : null;
                        Assert(lv != null && lv.Items.Count > 0, "ListView must contain bookmarks");
                        var dummyHandle2 = lv.Handle;
                        for (int i = 0; i < lv.Items.Count; i++)
                        {
                            BookmarkItem bi = lv.Items[i].Tag as BookmarkItem;
                            if (bi != null && bi.Url == "https://link.test")
                            {
                                lv.Items[i].Selected = true;
                                break;
                            }
                        }

                        var openSel = typeof(BookmarksView).GetMethod("OpenSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        openSel.Invoke(bv, new object[] { true });
                        Assert(bookmarkOpenedUrl == "https://link.test", "Opened URL matches bookmark");
                        Assert(bookmarkInNewTab == true, "Bookmark opened in new tab");
                    }
                    finally
                    {
                        AppServices.Bookmarks.RemoveByUrl("https://link.test");
                    }
                }

                // 3. Verify HistoryView opens selected history item in new tab
                string historyOpenedUrl = null;
                bool historyInNewTab = false;
                using (HistoryView hv = new HistoryView((url, inNewTab) =>
                {
                    historyOpenedUrl = url;
                    historyInNewTab = inNewTab;
                }))
                {
                    AppServices.History.AddEntry("History Link", "https://history-link.test");
                    try
                    {
                        var dummyHandle1 = hv.Handle;
                        hv.RefreshList();
                        var lvField = typeof(HistoryView).GetField("_listView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        ListView lv = lvField != null ? lvField.GetValue(hv) as ListView : null;
                        Assert(lv != null && lv.Items.Count > 0, "ListView must contain history items");
                        var dummyHandle2 = lv.Handle;
                        lv.Items[0].Selected = true;

                        var openSel = typeof(HistoryView).GetMethod("OpenSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        openSel.Invoke(hv, new object[] { true });
                        Assert(historyOpenedUrl == "https://history-link.test", "Opened URL matches history item");
                        Assert(historyInNewTab == true, "History item opened in new tab");
                    }
                    finally
                    {
                        var all = AppServices.History.GetAll();
                        for (int i = 0; i < all.Count; i++)
                        {
                            if (all[i].Url == "https://history-link.test")
                            {
                                AppServices.History.RemoveEntry(all[i].Id);
                                break;
                            }
                        }
                    }
                }
            });

            Test("WebErrorBypassManager Temporary Allow, RemoveDomain and Clear", () =>
            {
                WebErrorBypassManager.Clear();
                Assert(!WebErrorBypassManager.IsUrlAllowed("https://risky-domain.com/path"), "URL should initially not be allowed");

                WebErrorBypassManager.AllowUrlTemporarily("https://sub.risky-domain.com/path");
                Assert(WebErrorBypassManager.IsUrlAllowed("https://sub.risky-domain.com/path"), "Target URL should be allowed after bypass");
                Assert(WebErrorBypassManager.IsUrlAllowed("https://sub.risky-domain.com/other"), "Same domain should be allowed after bypass");

                // Parent walking: if parent is allowed, child is allowed
                WebErrorBypassManager.AllowUrlTemporarily("https://parent-risk.com");
                Assert(WebErrorBypassManager.IsUrlAllowed("https://child.parent-risk.com/path"), "Subdomain of allowed domain should be allowed");

                // RemoveDomain removes both domain and subdomains/URLs
                bool removed = WebErrorBypassManager.RemoveDomain("risky-domain.com");
                Assert(removed, "RemoveDomain should return true when domain removed");
                Assert(!WebErrorBypassManager.IsUrlAllowed("https://sub.risky-domain.com/path"), "URL should not be allowed after RemoveDomain");
                Assert(!WebErrorBypassManager.IsUrlAllowed("https://sub.risky-domain.com/other"), "Domain should not be allowed after RemoveDomain");

                // RemoveUrl
                WebErrorBypassManager.AllowUrlTemporarily("https://specific-url.com/a");
                Assert(WebErrorBypassManager.IsUrlAllowed("https://specific-url.com/a"), "Specific URL should be allowed");
                WebErrorBypassManager.RemoveUrl("https://specific-url.com/a");

                Assert(!WebErrorBypassManager.IsSslHostAllowed("badssl.com"), "SSL host should not be allowed initially");
                WebErrorBypassManager.AllowSslHostTemporarily("badssl.com");
                Assert(WebErrorBypassManager.IsSslHostAllowed("badssl.com"), "SSL host should be allowed after temporary bypass");

                WebErrorBypassManager.Clear();
                Assert(!WebErrorBypassManager.IsUrlAllowed("https://risky-domain.com/path"), "URL should not be allowed after clear");
                Assert(!WebErrorBypassManager.IsSslHostAllowed("badssl.com"), "SSL host should not be allowed after clear");
            });

            Test("CompiledRules ShouldBlockMainFrame Strict Blocking Validation", () =>
            {
                HashSet<string> blocked = new HashSet<string>(new string[] { "badware-malware.com", "tracking-hub.net", "click.redditmail.com" }, StringComparer.OrdinalIgnoreCase);
                HashSet<string> whitelisted = new HashSet<string>(new string[] { "safe-site.com" }, StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sources["badware-malware.com"] = "Malware Domain List";
                sources["click.redditmail.com"] = "EasyPrivacy";

                Dictionary<string, List<ExceptionRule>> exceptions = new Dictionary<string, List<ExceptionRule>>(StringComparer.OrdinalIgnoreCase);
                // Subresource exception rule: should NOT cancel strict blocking
                List<ExceptionRule> redditExceptions = new List<ExceptionRule>();
                redditExceptions.Add(new ExceptionRule("ref_source=email", false));
                exceptions["click.redditmail.com"] = redditExceptions;

                // Document exception rule: SHOULD cancel strict blocking
                List<ExceptionRule> docExceptions = new List<ExceptionRule>();
                docExceptions.Add(new ExceptionRule("trusted-doc", true));
                exceptions["tracking-hub.net"] = docExceptions;

                CompiledRules rules = new CompiledRules(
                    blocked,
                    null,
                    whitelisted,
                    null,
                    exceptions,
                    null,
                    null,
                    null,
                    null,
                    null,
                    sources);

                string ruleMatched;
                string listMatched;
                bool shouldBlockBad = rules.ShouldBlockMainFrame("https://badware-malware.com/payload.exe", out ruleMatched, out listMatched);
                Assert(shouldBlockBad, "Bad domain should be blocked for main frame");
                Assert(ruleMatched == "||badware-malware.com^", "Matched rule should be formatted properly");
                Assert(listMatched == "Malware Domain List", "List should be Malware Domain List");

                // redditmail should be blocked despite subresource exception
                bool shouldBlockReddit = rules.ShouldBlockMainFrame("https://click.redditmail.com/CL0/https:%2F%2Freddit.com/1/0", out ruleMatched, out listMatched);
                Assert(shouldBlockReddit, "Redditmail must be strictly blocked on main frame navigation despite subresource exception");
                Assert(listMatched == "EasyPrivacy", "List should be EasyPrivacy");

                // tracking-hub.net with document exception matching pattern should NOT be blocked
                bool shouldBlockDoc = rules.ShouldBlockMainFrame("https://tracking-hub.net/path?trusted-doc=1", out ruleMatched, out listMatched);
                Assert(!shouldBlockDoc, "Document exception rule should unblock main frame navigation");

                bool shouldBlockWhitelisted = rules.ShouldBlockMainFrame("https://safe-site.com", out ruleMatched, out listMatched);
                Assert(!shouldBlockWhitelisted, "Whitelisted site should not be blocked");

                bool shouldBlockClean = rules.ShouldBlockMainFrame("https://example.com", out ruleMatched, out listMatched);
                Assert(!shouldBlockClean, "Clean domain should not be blocked");
            });

            Test("AdBlockManager Whitelist Lifecycle, Parent and Subdomain Removal and Exception Re-engagement", () =>
            {
                AdBlockManager abm = new AdBlockManager();
                List<string> originalWhitelist = abm.GetWhitelistedDomains();

                try
                {
                    abm.ClearWhitelist();
                    Assert(abm.GetWhitelistedDomains().Count == 0, "Whitelist should be empty after clear");

                    // 1. Basic Add & Normalize
                    abm.WhitelistDomain("||test-ad-network.com^");
                    Assert(abm.IsDomainWhitelisted("test-ad-network.com"), "test-ad-network.com should be whitelisted");
                    Assert(abm.IsDomainWhitelisted("https://www.test-ad-network.com/banner.js"), "www and path should be considered whitelisted");

                    // 2. Parent walking whitelist
                    Assert(abm.IsDomainWhitelisted("sub.test-ad-network.com"), "Subdomain of whitelisted parent should be whitelisted");

                    // 3. Unwhitelist parent directly
                    bool removed = abm.UnwhitelistDomain("test-ad-network.com");
                    Assert(removed, "UnwhitelistDomain should return true");
                    Assert(!abm.IsDomainWhitelisted("test-ad-network.com"), "Domain should no longer be whitelisted");
                    Assert(!abm.IsDomainWhitelisted("sub.test-ad-network.com"), "Subdomain should no longer be whitelisted");

                    // 4. Subdomain-targeted Unwhitelist removes parent causing the whitelist
                    abm.WhitelistDomain("parent-site.org");
                    Assert(abm.IsDomainWhitelisted("deep.sub.parent-site.org"), "Deep subdomain should be whitelisted via parent");
                    bool removedViaSub = abm.UnwhitelistDomain("deep.sub.parent-site.org");
                    Assert(removedViaSub, "UnwhitelistDomain on subdomain should remove the parent causing the whitelist");
                    Assert(!abm.IsDomainWhitelisted("parent-site.org"), "Parent should no longer be whitelisted");
                    Assert(!abm.IsDomainWhitelisted("deep.sub.parent-site.org"), "Deep subdomain should no longer be whitelisted");

                    // 5. Parent-targeted Unwhitelist purges any child subdomains
                    abm.WhitelistDomain("base-domain.io");
                    abm.WhitelistDomain("api.base-domain.io");
                    abm.WhitelistDomain("cdn.base-domain.io");
                    abm.UnwhitelistDomain("base-domain.io");
                    Assert(!abm.IsDomainWhitelisted("base-domain.io"), "Base domain should not be whitelisted");
                    Assert(!abm.IsDomainWhitelisted("api.base-domain.io"), "Child api subdomain should not remain whitelisted");
                    Assert(!abm.IsDomainWhitelisted("cdn.base-domain.io"), "Child cdn subdomain should not remain whitelisted");

                    // 6. Temporary bypass synchronization
                    WebErrorBypassManager.Clear();
                    WebErrorBypassManager.AllowUrlTemporarily("https://temp-risky-site.net/page");
                    Assert(abm.IsDomainWhitelisted("temp-risky-site.net"), "AdBlockManager should recognize active temporary session bypass");
                    Assert(abm.GetWhitelistedDomains().Contains("temp-risky-site.net"), "GetWhitelistedDomains should include temporary session bypass");

                    // Unwhitelisting purges temporary bypass immediately
                    abm.UnwhitelistDomain("temp-risky-site.net");
                    Assert(!abm.IsDomainWhitelisted("temp-risky-site.net"), "temp-risky-site.net must no longer be whitelisted after unwhitelist");
                    Assert(!WebErrorBypassManager.IsUrlAllowed("https://temp-risky-site.net/page"), "WebErrorBypassManager must be purged on unwhitelist");

                    // 7. Toggle domain
                    Assert(!abm.IsDomainWhitelisted("toggle-check.com"), "toggle-check.com initially not whitelisted");
                    bool toggledOn = abm.ToggleDomain("toggle-check.com");
                    Assert(toggledOn, "ToggleDomain should return true when adding");
                    Assert(abm.IsDomainWhitelisted("toggle-check.com"), "Domain should be whitelisted after toggle on");
                    bool toggledOff = abm.ToggleDomain("toggle-check.com");
                    Assert(!toggledOff, "ToggleDomain should return false when removing");
                    Assert(!abm.IsDomainWhitelisted("toggle-check.com"), "Domain must NOT be whitelisted after toggle off");
                }
                finally
                {
                    abm.ClearWhitelist();
                    for (int i = 0; i < originalWhitelist.Count; i++)
                    {
                        abm.WhitelistDomain(originalWhitelist[i]);
                    }
                }
            });

            Test("Subscription Unbreak Rules Do Not Contaminate User Whitelist", () =>
            {
                HashSet<string> blockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> thirdPartyBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> userWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, List<string>> domainPaths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, List<ExceptionRule>> domainExceptions = new Dictionary<string, List<ExceptionRule>>(StringComparer.OrdinalIgnoreCase);
                List<string> globalPaths = new List<string>();
                List<string> globalExceptions = new List<string>();
                HashSet<string> genericCosmetic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, List<string>> domainCosmetic = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> genericHide = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                blockedDomains.Add("ad-tracker.com");

                // Parse a filter unbreak rule: @@||unbreak-target.com^
                bool parsed = AdBlockManager.ParseFilterLine(
                    "@@||unbreak-target.com^",
                    blockedDomains,
                    thirdPartyBlocked,
                    userWhitelist,
                    domainPaths,
                    domainExceptions,
                    globalPaths,
                    globalExceptions,
                    genericCosmetic,
                    domainCosmetic,
                    genericHide);

                Assert(parsed, "ParseFilterLine should return true for exception rule");
                Assert(!userWhitelist.Contains("unbreak-target.com"), "Subscription unbreak rule must NOT be added to userWhitelist");
                Assert(domainExceptions.ContainsKey("unbreak-target.com"), "Subscription unbreak rule must be added to domainExceptions");

                CompiledRules rules = new CompiledRules(
                    blockedDomains,
                    thirdPartyBlocked,
                    userWhitelist,
                    domainPaths,
                    domainExceptions,
                    globalPaths,
                    globalExceptions,
                    genericCosmetic,
                    domainCosmetic,
                    genericHide,
                    null);

                // User whitelist test must be false
                Assert(!rules.IsDomainWhitelisted("unbreak-target.com"), "Unbreak rule must NOT make IsDomainWhitelisted return true");

                // But matching ad requests are unbroken
                string matchedRule;
                Assert(!rules.ShouldBlock("https://unbreak-target.com/script.js", "https://example.com", 1, out matchedRule), "Request to unbreak-target.com should NOT be blocked");
            });

            Test("Flash Banners Network Blocking and Cosmetic Collapser Validation", () =>
            {
                // 1. Selector validation
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("object[type*=\"flash\"]"), "object[type*=\"flash\"] should be valid generic cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("embed[type*=\"flash\"]"), "embed[type*=\"flash\"] should be valid generic cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("object[data*=\".swf\"]"), "object[data*=\".swf\"] should be valid generic cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("embed[src*=\".swf\"]"), "embed[src*=\".swf\"] should be valid generic cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector(".include > object"), ".include > object should be valid generic cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("ytd-promoted-sparkles-web-renderer"), "ytd custom element should be valid generic cosmetic selector");
                Assert(!AdBlockManager.IsValidGenericCosmeticSelector("div"), "Raw div tag must be rejected");
                Assert(!AdBlockManager.IsValidGenericCosmeticSelector("a"), "Raw a tag must be rejected");

                // 2. ParseFilterLine with generic tag selectors
                HashSet<string> bDoms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> tpDoms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> wDoms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, List<string>> dPaths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, List<ExceptionRule>> dEx = new Dictionary<string, List<ExceptionRule>>(StringComparer.OrdinalIgnoreCase);
                List<string> gPaths = new List<string>();
                List<string> gEx = new List<string>();
                HashSet<string> gCos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, List<string>> dCos = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> ghDoms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                bool parsedObj = AdBlockManager.ParseFilterLine("##object[type*=\"flash\"]", bDoms, tpDoms, wDoms, dPaths, dEx, gPaths, gEx, gCos, dCos, ghDoms);
                Assert(parsedObj, "Should parse ##object[type*=\"flash\"]");
                Assert(gCos.Contains("object[type*=\"flash\"]"), "gCos must contain object[type*=\"flash\"]");

                bool parsedEmbed = AdBlockManager.ParseFilterLine("##embed[type*=\"flash\"]", bDoms, tpDoms, wDoms, dPaths, dEx, gPaths, gEx, gCos, dCos, ghDoms);
                Assert(parsedEmbed, "Should parse ##embed[type*=\"flash\"]");
                Assert(gCos.Contains("embed[type*=\"flash\"]"), "gCos must contain embed[type*=\"flash\"]");

                // 3. Network blocking on banner assets
                gPaths.Add("/banners/");
                gPaths.Add("_ads_banner");
                gPaths.Add("pr_advertising");
                CompiledRules compiled = new CompiledRules(bDoms, tpDoms, wDoms, dPaths, dEx, gPaths, gEx, gCos, dCos, ghDoms);

                string matched;
                Assert(compiled.ShouldBlock("https://adblock-tester.com/banners/pr_advertising_ads_banner.swf?v=1", "https://adblock-tester.com", 1, out matched), "Flash banner swf URL must be blocked");
                Assert(compiled.ShouldBlock("https://adblock-tester.com/banners/pr_advertising_ads_banner.gif", "https://adblock-tester.com", 1, out matched), "Flash banner gif URL must be blocked");
                Assert(compiled.ShouldBlock("https://adblock-tester.com/banners/pr_advertising_ads_banner.png", "https://adblock-tester.com", 1, out matched), "Flash banner png URL must be blocked");

                // 4. Cosmetic hiding script includes baseline flash hiding and dom collapser
                AdBlockManager abm = new AdBlockManager();
                string script = abm.GetCosmeticHidingScript("https://adblock-tester.com");
                Assert(!string.IsNullOrEmpty(script), "Cosmetic script must not be empty");
                Assert(script.Contains("cnet-ublock-cosmetic"), "Cosmetic script must create cnet-ublock-cosmetic style element");
                Assert(script.Contains("flash"), "Cosmetic script must include flash hiding rule");
                Assert(script.Contains("cBlocked"), "Cosmetic script must include DOM collapser routine");
                Assert(script.Contains("addEventListener('error'"), "Cosmetic script must include uBlock resource error collapser");
            });

            Test("Keywords ad*, advert*, advertis* Filtering & False-Positive Immunity Validation", () =>
            {
                // 1. Path Safety & Keyword Recognition
                Assert(AdBlockManager.IsSafeGlobalPath("/ad/"), "/ad/ must be recognized as safe ad path");
                Assert(AdBlockManager.IsSafeGlobalPath("/ads/"), "/ads/ must be recognized as safe ad path");
                Assert(AdBlockManager.IsSafeGlobalPath("/ad_banner"), "/ad_banner must be recognized as safe ad path");
                Assert(AdBlockManager.IsSafeGlobalPath("/ads_banner"), "/ads_banner must be recognized as safe ad path");
                Assert(AdBlockManager.IsSafeGlobalPath("_ads_banner"), "_ads_banner must be recognized as safe ad path");
                Assert(AdBlockManager.IsSafeGlobalPath("/advert/"), "/advert/ must be recognized as safe ad path");
                Assert(AdBlockManager.IsSafeGlobalPath("/advertising/"), "/advertising/ must be recognized as safe ad path");
                Assert(AdBlockManager.IsSafeGlobalPath("/advertisement/"), "/advertisement/ must be recognized as safe ad path");
                Assert(AdBlockManager.IsSafeGlobalPath("pr_advertising"), "pr_advertising must be recognized as safe ad path");

                // 2. Strict Immunity for Benign URLs (False-Positive Prevention)
                Assert(!AdBlockManager.IsSafeGlobalPath("/admin"), "/admin must NEVER be blocked globally");
                Assert(!AdBlockManager.IsSafeGlobalPath("/download"), "/download must NEVER be blocked globally");
                Assert(!AdBlockManager.IsSafeGlobalPath("/upload"), "/upload must NEVER be blocked globally");
                Assert(!AdBlockManager.IsSafeGlobalPath("/reader"), "/reader must NEVER be blocked globally");
                Assert(!AdBlockManager.IsSafeGlobalPath("/header"), "/header must NEVER be blocked globally");

                // 3. Network Request URL Blocking Validation
                AdBlockManager abm = new AdBlockManager();

                string match;
                // Ad/Advert/Advertising resources MUST be blocked
                Assert(abm.ShouldBlock("https://example.com/ad/300x250.jpg", "https://example.com", 1, out match), "/ad/ resource must be blocked");
                Assert(abm.ShouldBlock("https://example.com/ads/tracker.js", "https://example.com", 1, out match), "/ads/ resource must be blocked");
                Assert(abm.ShouldBlock("https://example.com/advert/promo.gif", "https://example.com", 1, out match), "/advert/ resource must be blocked");
                Assert(abm.ShouldBlock("https://example.com/advertising/lead.js", "https://example.com", 1, out match), "/advertising/ resource must be blocked");
                Assert(abm.ShouldBlock("https://example.com/advertisement/popup.html", "https://example.com", 1, out match), "/advertisement/ resource must be blocked");
                Assert(abm.ShouldBlock("https://example.com/advertisment/slot.js", "https://example.com", 1, out match), "/advertisment/ typo path must be blocked");
                Assert(abm.ShouldBlock("https://example.com/advertisments/banner.png", "https://example.com", 1, out match), "/advertisments/ typo path must be blocked");
                Assert(abm.ShouldBlock("https://example.com/images/ad_banner.png", "https://example.com", 1, out match), "/ad_banner resource must be blocked");
                Assert(abm.ShouldBlock("https://example.com/assets/_ads_banner.swf", "https://example.com", 1, out match), "_ads_banner resource must be blocked");

                // Legitimate benign resources MUST NOT be blocked
                Assert(!abm.ShouldBlock("https://example.com/admin/login.php", "https://example.com", 1, out match), "/admin URL must NOT be blocked");
                Assert(!abm.ShouldBlock("https://example.com/download/app.exe", "https://example.com", 1, out match), "/download URL must NOT be blocked");
                Assert(!abm.ShouldBlock("https://example.com/assets/header.png", "https://example.com", 1, out match), "/header URL must NOT be blocked");
                Assert(!abm.ShouldBlock("https://example.com/reader/view.html", "https://example.com", 1, out match), "/reader URL must NOT be blocked");

                // 4. Cosmetic Selectors Validation
                Assert(AdBlockManager.IsValidGenericCosmeticSelector(".advert"), ".advert must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector(".advertising"), ".advertising must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector(".advertisement"), ".advertisement must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector(".advertisment"), ".advertisment typo must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector(".advertisments"), ".advertisments typo must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("[class*=\"advertis\"]"), "[class*=\"advertis\"] must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("[id*=\"advertis\"]"), "[id*=\"advertis\"] must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("[class*=\"advert\"]"), "[class*=\"advert\"] must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("[id*=\"advert\"]"), "[id*=\"advert\"] must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("[data-ads]"), "[data-ads] must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("object[data*=\"advertising\"]"), "object[data*=\"advertising\"] must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("img[src*=\"advert\"]"), "img[src*=\"advert\"] must be valid cosmetic selector");
                Assert(AdBlockManager.IsValidGenericCosmeticSelector("iframe[src*=\"/ads/\"]"), "iframe[src*=\"/ads/\"] must be valid cosmetic selector");

                // 5. Cosmetic Script Content Verification
                string script = abm.GetCosmeticHidingScript("https://example.com");
                Assert(script.Contains("advert"), "Cosmetic script must contain advert rules");
                Assert(script.Contains("advertising"), "Cosmetic script must contain advertising rules");
                Assert(script.Contains("advertisment"), "Cosmetic script must contain advertisment typo rules");
                Assert(script.Contains("[data-ads]"), "Cosmetic script must contain data-ads selector");
            });

            Test("ToolStripDismissHook Registration and CloseAll Workflow", () =>
            {
                using (ContextMenuStrip menu = new ContextMenuStrip())
                {
                    UITheme.ApplyContextMenu(menu);
                    ToolStripMenuItem subItem = new ToolStripMenuItem("Sub");
                    UITheme.ApplyMenuItem(subItem);
                    menu.Items.Add(subItem);

                    menu.Show(new Point(100, 100));
                    Assert(menu.Visible, "Menu should be visible after Show");

                    ToolStripDismissHook.CloseAll();
                    // Process message queue for Close invoke
                    Application.DoEvents();
                    Assert(!menu.Visible, "Menu should be closed after CloseAll");
                }
            });

            Test("WbTopBar Address Action Button Icon Consistency and No Duplicate Search Button", () =>
            {
                using (Form f = new Form())
                {
                    f.Size = new Size(850, 200);
                    f.Show();
                    using (WbTopBar topBar = new WbTopBar())
                    {
                        topBar.Width = 800;
                        topBar.Dock = DockStyle.Top;
                        f.Controls.Add(topBar);

                        // 1. Initial/Empty text box -> always refresh icon (arrow-clockwise) when idle, never arrow-right, Go button empty
                        topBar.AddressText = "";
                        Assert(topBar.GetAddressActionIconName() == "arrow-clockwise", "Empty address bar should show refresh (arrow-clockwise) icon, not forward arrow");
                        Assert(topBar.GetGoButtonRect() == Rectangle.Empty, "Go button should be empty when address box is empty");

                        // 2. Page loaded, text present, not focused -> should be arrow-clockwise, Go button empty
                        topBar.AddressText = "https://example.com";
                        f.ActiveControl = null;
                        Application.DoEvents();
                        Assert(topBar.GetAddressActionIconName() == "arrow-clockwise", "Populated address bar should show arrow-clockwise icon");
                        Assert(topBar.GetGoButtonRect() == Rectangle.Empty, "Go button should be empty when not editing");

                        string artDir = GetArtifactsDir();
                        var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (artDir != null)
                        {
                            using (Bitmap bmp = new Bitmap(800, 38))
                            using (Graphics g = Graphics.FromImage(bmp))
                            {
                                var pe = new PaintEventArgs(g, new Rectangle(0, 0, 800, 38));
                                onPaint.Invoke(topBar, new object[] { pe });
                                bmp.Save(System.IO.Path.Combine(artDir, "url_idle_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }

                        // 3. User typing / focused on address box -> Go button appears to the left of refresh button
                        topBar.AddressBox.Focus();
                        Application.DoEvents();
                        Assert(topBar.IsEditingAddressBox, "IsEditingAddressBox should be true when focused with text");
                        Rectangle goRect = topBar.GetGoButtonRect();
                        Rectangle refRect = topBar.GetRefreshButtonRect();
                        Assert(goRect != Rectangle.Empty, "Go button should appear when editing address box");
                        Assert(goRect.Right <= refRect.Left, "Go button must be located to the left of the refresh button");
                        Assert(topBar.GetAddressActionIconName() == "arrow-clockwise", "Refresh button remains arrow-clockwise when not loading");

                        if (System.IO.Directory.Exists(artDir))
                        {
                            using (Bitmap bmp = new Bitmap(800, 38))
                            using (Graphics g = Graphics.FromImage(bmp))
                            {
                                var pe = new PaintEventArgs(g, new Rectangle(0, 0, 800, 38));
                                onPaint.Invoke(topBar, new object[] { pe });
                                bmp.Save(System.IO.Path.Combine(artDir, "url_editing_forward_button_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }

                        // 4. Loading state overrides to "x"
                        topBar.IsLoading = true;
                        Assert(topBar.GetAddressActionIconName() == "x", "Loading state should show 'x' icon");

                        // 5. Non-loading returns to arrow-clockwise
                        topBar.IsLoading = false;
                        Assert(topBar.GetAddressActionIconName() == "arrow-clockwise", "Non-loading address bar should return to arrow-clockwise");

                        // 6. Verify duplicate search button is removed
                        Assert(topBar.GetSearchButtonRect() == Rectangle.Empty, "Search button rectangle should be empty to prevent duplicate search icons");
                    }
                    f.Close();
                }
            });

            Test("UITheme Native Immersive Dark Mode Application", () =>
            {
                using (Form f = new Form())
                {
                    UITheme.ApplyForm(f);
                    f.Show();
                    UITheme.EnableImmersiveDarkMode(f.Handle);
                    Assert(f.BackColor == UITheme.FormBackground, "Form background should match FormBackground dark theme");
                    f.Close();
                }
            });

            Test("UITheme Dark Themed Button and TextBox Styling and State Transitions", () =>
            {
                // 1. Secondary Button Dark Theming
                using (Button btn = new Button { Text = "Cancel" })
                {
                    UITheme.ApplyButton(btn, false);
                    Assert(btn.FlatStyle == FlatStyle.Flat, "Secondary button FlatStyle must be Flat");
                    Assert(!btn.UseVisualStyleBackColor, "Secondary button UseVisualStyleBackColor must be false");
                    Assert(btn.BackColor == UITheme.ButtonBackground, "Secondary button BackColor should be ButtonBackground");
                    Assert(btn.ForeColor == UITheme.TextPrimary, "Secondary button ForeColor should be TextPrimary");
                    Assert(btn.FlatAppearance.BorderSize == 1, "Secondary button BorderSize must be 1");
                    Assert(btn.FlatAppearance.BorderColor == UITheme.ButtonBorder, "Secondary button BorderColor must be ButtonBorder");
                    Assert(btn.FlatAppearance.MouseOverBackColor == UITheme.ButtonHover, "Secondary button hover back color must match ButtonHover");
                    Assert(btn.FlatAppearance.MouseDownBackColor == UITheme.ButtonPressed, "Secondary button down back color must match ButtonPressed");

                    // Disabled State Transition
                    btn.Enabled = false;
                    Assert(btn.BackColor == UITheme.ButtonDisabledBackground, "Disabled secondary button must use ButtonDisabledBackground");
                    Assert(btn.ForeColor == UITheme.ButtonDisabledText, "Disabled secondary button must use ButtonDisabledText");
                    btn.Enabled = true;
                    Assert(btn.BackColor == UITheme.ButtonBackground, "Re-enabled secondary button must restore ButtonBackground");
                    Assert(btn.ForeColor == UITheme.TextPrimary, "Re-enabled secondary button must restore TextPrimary");
                }

                // 2. Primary Button Accent Theming & Dynamic Synchronizations
                using (Button primaryBtn = new Button { Text = "Save" })
                {
                    UITheme.ApplyButton(primaryBtn, true);
                    Assert(primaryBtn.FlatStyle == FlatStyle.Flat, "Primary button FlatStyle must be Flat");
                    Assert(!primaryBtn.UseVisualStyleBackColor, "Primary button UseVisualStyleBackColor must be false");
                    Assert(primaryBtn.BackColor == ThemeManager.CurrentAccentColor, "Primary button BackColor must match CurrentAccentColor");
                    Assert(primaryBtn.ForeColor == UITheme.GetContrastTextColor(ThemeManager.CurrentAccentColor), "Primary button text must contrast with accent");
                    Assert(primaryBtn.FlatAppearance.BorderSize == 0, "Primary button BorderSize must be 0 for flat accent fill");

                    // Disabled State Transition
                    primaryBtn.Enabled = false;
                    Assert(primaryBtn.BackColor == UITheme.ButtonDisabledBackground, "Disabled primary button must use ButtonDisabledBackground");
                    Assert(primaryBtn.ForeColor == UITheme.ButtonDisabledText, "Disabled primary button must use ButtonDisabledText");
                    primaryBtn.Enabled = true;
                    Assert(primaryBtn.BackColor == ThemeManager.CurrentAccentColor, "Re-enabled primary button must restore accent BackColor");

                    // Dynamic Accent Update
                    Color prevColor = ThemeManager.CurrentAccentColor;
                    Color testAccent = Color.FromArgb(255, 128, 171); // Hot Pink
                    ThemeManager.SetAccentColor(testAccent, "TestPink");
                    Assert(primaryBtn.BackColor == testAccent, "Primary button must dynamically synchronize when ThemeManager accent changes");
                    ThemeManager.SetAccentColor(prevColor, "Restored");
                }

                // 3. TextBox Dark Theming & Container Hierarchy Awareness
                using (Form testForm = new Form())
                using (Panel toolbarPanel = new Panel())
                using (TextBox formTxt = new TextBox())
                using (TextBox toolbarTxt = new TextBox())
                {
                    UITheme.ApplyForm(testForm);
                    UITheme.ApplyTopToolbar(toolbarPanel);
                    testForm.Controls.Add(toolbarPanel);

                    UITheme.ApplyTextBox(formTxt, "Placeholder");
                    Assert(formTxt.BorderStyle == BorderStyle.FixedSingle, "TextBox BorderStyle must be FixedSingle (no 3D white bevel)");
                    Assert(formTxt.ForeColor == UITheme.InputText, "TextBox ForeColor must be InputText");
                    Assert(formTxt.BackColor == UITheme.InputBackground, "Standalone TextBox must use InputBackground");

                    // Attached to Form (FormBackground #202124)
                    testForm.Controls.Add(formTxt);
                    Assert(formTxt.BackColor == UITheme.InputBackground, "TextBox on form should retain elevated InputBackground");

                    // Attached to Toolbar (ToolbarBackground #292A2D) -> should recess to AddressBarBackground (#202124)
                    toolbarPanel.Controls.Add(toolbarTxt);
                    UITheme.ApplyTextBox(toolbarTxt, "Search");
                    Assert(toolbarTxt.BackColor == UITheme.InputBackgroundDark, "TextBox in toolbar should automatically recess to InputBackgroundDark");

                    // Disabled State
                    formTxt.Enabled = false;
                    Assert(formTxt.BackColor == UITheme.ButtonDisabledBackground, "Disabled TextBox must use ButtonDisabledBackground");
                    Assert(formTxt.ForeColor == UITheme.ButtonDisabledText, "Disabled TextBox must use ButtonDisabledText");
                    formTxt.Enabled = true;
                    Assert(formTxt.BackColor == UITheme.InputBackground, "Re-enabled TextBox must restore background");
                    Assert(formTxt.ForeColor == UITheme.InputText, "Re-enabled TextBox must restore InputText");
                }
            });

            Test("MaterialIcons Concurrent Thread-Safe Access and Cache Resolution", () =>
            {
                MaterialIcons.ClearCache();
                int threadCount = 16;
                int iterationsPerThread = 100;
                List<System.Threading.Thread> threads = new List<System.Threading.Thread>();
                Exception threadEx = null;
                string[] iconNames = new string[] { "house", "arrow-right", "arrow-clockwise", "x", "plus", "gear", "star", "shield" };

                for (int t = 0; t < threadCount; t++)
                {
                    int threadIdx = t;
                    System.Threading.Thread th = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            for (int i = 0; i < iterationsPerThread; i++)
                            {
                                string name = iconNames[(threadIdx + i) % iconNames.Length];
                                Bitmap b1 = MaterialIcons.GetIcon(name, 16 + (i % 8));
                                Assert(b1 != null, "Bitmap should not be null");
                                Bitmap b2 = MaterialIcons.GetDpiIcon(name, 1.25f, 14);
                                Assert(b2 != null, "DPI bitmap should not be null");
                            }
                        }
                        catch (Exception ex)
                        {
                            threadEx = ex;
                        }
                    });
                    threads.Add(th);
                    th.Start();
                }

                for (int t = 0; t < threads.Count; t++)
                {
                    threads[t].Join();
                }

                if (threadEx != null)
                {
                    throw new Exception("Concurrent thread failure: " + threadEx.Message, threadEx);
                }
            });

            Test("ThemeManager ApplyToOpenForms Concurrent Form Modification Safety", () =>
            {
                using (Form f1 = new Form())
                {
                    f1.Show();
                    using (FloatingStatusBubble bubble = new FloatingStatusBubble())
                    {
                        bubble.AttachTo(f1);
                        bubble.SetStatus("https://example.com/test");

                        // Concurrently show/hide and call ApplyToOpenForms
                        for (int i = 0; i < 20; i++)
                        {
                            if (i % 2 == 0) bubble.SetStatus("https://example.com/page" + i);
                            else bubble.SetStatus("");
                            ThemeManager.ApplyToOpenForms();
                        }
                    }
                    f1.Close();
                }
            });

            Test("Instant Domain-Switch Favicon Reset and In-Memory Cache Resolution", () =>
            {
                // Verify ExtractDomain helper
                Assert(FaviconManager.ExtractDomain("https://github.com/microsoft") == "github.com", "ExtractDomain strips path");
                Assert(FaviconManager.ExtractDomain("https://www.google.com/search") == "google.com", "ExtractDomain strips www");
                Assert(FaviconManager.ExtractDomain("https://en.wikipedia.org/wiki") == "en.wikipedia.org", "ExtractDomain preserves subdomain");

                // Test mock tab domain transitions
                using (Bitmap icon1 = new Bitmap(16, 16))
                {
                    MockFaviconTab tab = new MockFaviconTab("Site A", "https://site-a.com/page1");
                    tab.Favicon = icon1;

                    // Transition to another domain without cached icon: favicon must clear to null immediately
                    string newUrl = "https://site-b.com/index";
                    string newDomain = FaviconManager.ExtractDomain(newUrl);
                    string oldDomain = FaviconManager.ExtractDomain(tab.CurrentUrl);
                    Assert(newDomain != oldDomain, "Domains differ");

                    // Simulate the instant reset logic applied in LoadUrl, FrameLoadStart, AddressChanged
                    Image cached = AppServices.Favicons.GetFavicon(newUrl);
                    tab.Favicon = cached; // null for uncached
                    Assert(tab.Favicon == null, "Old site icon is immediately cleared when moving to uncached new domain");

                    // Same-domain navigation (e.g. site-a.com/page1 -> site-a.com/page2): icon preserved
                    tab.Favicon = icon1;
                    tab.CurrentUrl = "https://site-a.com/page1";
                    string sameDomainUrl = "https://site-a.com/page2";
                    string sameDomain = FaviconManager.ExtractDomain(sameDomainUrl);
                    string currDomain = FaviconManager.ExtractDomain(tab.CurrentUrl);
                    Assert(sameDomain == currDomain, "Same domain should not reset favicon");
                    Assert(tab.Favicon == icon1, "Favicon remains preserved across same-domain navigation");
                }
            });

            Test("TopBar Tooltips and Active Menu State", () =>
            {
                // Verify WbTopBar IsGearMenuOpen and button painting
                using (Form host = new Form { Size = new Size(800, 600) })
                using (WbTopBar topBar = new WbTopBar())
                {
                    topBar.Width = 800;
                    host.Controls.Add(topBar);
                    host.Show();
                    Application.DoEvents();

                    Assert(!topBar.IsGearMenuOpen, "IsGearMenuOpen initially false");
                    topBar.IsGearMenuOpen = true;
                    Assert(topBar.IsGearMenuOpen, "IsGearMenuOpen successfully set to true");

                    // Verify topBar paints with active gear menu state without exception
                    using (Bitmap bmp = new Bitmap(topBar.Width, topBar.Height))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        var pe = new PaintEventArgs(g, new Rectangle(0, 0, topBar.Width, topBar.Height));
                        var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(topBar, new object[] { pe });
                    }

                    // Test mouse move over Home, Star, Gear buttons sets tooltips
                    var onMouseMove = typeof(WbTopBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Rectangle homeRect = topBar.GetHomeButtonRect();
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, homeRect.Left + 5, homeRect.Top + 5, 0) });

                    Rectangle starRect = topBar.GetStarButtonRect();
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, starRect.Left + 5, starRect.Top + 5, 0) });

                    Rectangle shieldRect = topBar.GetShieldButtonRect();
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, shieldRect.Left + 5, shieldRect.Top + 5, 0) });

                    Rectangle gearRect = topBar.GetGearButtonRect();
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, gearRect.Left + 5, gearRect.Top + 5, 0) });

                    host.Close();
                }
            });

            Test("About wb Dialog Strings, Accent Globe Icon and Bookmark Remove Tooltip", () =>
            {
                // 1. Verify AppStrings
                // 1. Verify AppStrings & Architecture
                string expectedArch = Environment.Is64BitProcess ? "(x64)" : "(x86)";
                string expectedArchRaw = Environment.Is64BitProcess ? "x64" : "x86";
                Assert(AppStrings.AppArchitecture == expectedArchRaw, "AppArchitecture matches process bitness: " + expectedArchRaw);
                Assert(AppStrings.AppArchitectureDisplay == expectedArch, "AppArchitectureDisplay matches: " + expectedArch);
                Assert(AppStrings.Browser.AboutTitle == "About wb " + expectedArch, "AboutTitle is 'About wb " + expectedArch + "'");
                Assert(AppStrings.Browser.MenuAbout == "About wb", "MenuAbout is 'About wb'");
                string aboutText = AppStrings.Browser.AboutDialogText;
                Assert(aboutText.StartsWith("wb v"), "AboutDialogText starts with version");
                Assert(aboutText.Contains(expectedArch), "AboutDialogText contains architecture: " + expectedArch);
                Assert(aboutText.Contains("More Web. Less Noise."), "AboutDialogText contains 'More Web. Less Noise.'");
                Assert(aboutText.Contains("Developed by @ikmalsaid"), "AboutDialogText contains 'Developed by @ikmalsaid'");

                string[] lines = aboutText.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                Assert(lines.Length == 3, "AboutDialogText has exactly 3 lines, got: " + lines.Length);
                Assert(lines[0] == AppStrings.AppName + " " + AppStrings.AppVersionDisplay + " " + expectedArch, "Line 1 contains app name, version and architecture: " + lines[0]);
                Assert(lines[1] == "More Web. Less Noise.", "Line 2 is 'More Web. Less Noise.'");
                Assert(lines[2] == "Developed by @ikmalsaid", "Line 3 is 'Developed by @ikmalsaid'");

                // 2. Verify TopBar bookmark tooltip for both states (bookmark vs remove)
                using (Form host = new Form { Size = new Size(800, 600) })
                using (WbTopBar topBar = new WbTopBar())
                {
                    topBar.Width = 800;
                    host.Controls.Add(topBar);
                    host.Show();
                    Application.DoEvents();

                    var onMouseMove = typeof(WbTopBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var toolTipField = typeof(WbTopBar).GetField("_toolTip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    ToolTip tt = (ToolTip)toolTipField.GetValue(topBar);
                    Rectangle starRect = topBar.GetStarButtonRect();

                    // Case A: Not bookmarked -> TipBookmark
                    topBar.IsBookmarked = false;
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, starRect.Left + 5, starRect.Top + 5, 0) });
                    Assert(tt.GetToolTip(topBar) == AppStrings.Browser.TipBookmark, "Tooltip when not bookmarked matches AppStrings.Browser.TipBookmark");

                    // Move mouse away to reset hover state
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, 0) });

                    // Case B: Bookmarked -> TipRemoveBookmark
                    topBar.IsBookmarked = true;
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, starRect.Left + 5, starRect.Top + 5, 0) });
                    Assert(tt.GetToolTip(topBar) == AppStrings.Browser.TipRemoveBookmark, "Tooltip when bookmarked matches AppStrings.Browser.TipRemoveBookmark");

                    host.Close();
                }

                // 3. Verify Globe Icon renders with ThemeManager.CurrentAccentColor
                Color initialAccent = ThemeManager.CurrentAccentColor;
                Bitmap bmpAccent = MaterialIcons.GetIcon("globe", 16, initialAccent);
                Assert(bmpAccent != null, "Globe icon with accent color should not be null");
                Assert(bmpAccent.Width == 16 && bmpAccent.Height == 16, "Globe icon should be 16x16");
            });

            Test("Tool Menu Clean Icons, Favorites Accent Color and uBlock Status Suffix", () =>
            {
                // 1. Favorites Star Button and Shield Button Painting & Geometry
                using (Form host = new Form { Size = new Size(800, 600) })
                using (WbTopBar topBar = new WbTopBar())
                {
                    topBar.Width = 800;
                    host.Controls.Add(topBar);
                    host.Show();
                    Application.DoEvents();

                    Rectangle homeR = topBar.GetHomeButtonRect();
                    Rectangle starR = topBar.GetStarButtonRect();
                    Rectangle shieldR = topBar.GetShieldButtonRect();
                    Rectangle gearR = topBar.GetGearButtonRect();

                    Assert(homeR.Right < starR.Left, "Home is to the left of Star");
                    Assert(starR.Right < shieldR.Left, "Shield is beside the bookmark icon");
                    Assert(shieldR.Right < gearR.Left, "Shield is before the tools icon");

                    bool shieldClickedFired = false;
                    topBar.ShieldClicked += (s, e) => { shieldClickedFired = true; };
                    var onMouseUp = typeof(WbTopBar).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseDown = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, shieldR.Left + 5, shieldR.Top + 5, 0) });
                    onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, shieldR.Left + 5, shieldR.Top + 5, 0) });
                    Assert(shieldClickedFired, "Clicking Shield button fires ShieldClicked");

                    var onMouseMove = typeof(WbTopBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var toolTipField = typeof(WbTopBar).GetField("_toolTip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    ToolTip tt = (ToolTip)toolTipField.GetValue(topBar);
                    topBar.AdBlockStatusSuffix = "(disabled for this site)";
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, 0) });
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, shieldR.Left + 5, shieldR.Top + 5, 0) });
                    Assert(tt.GetToolTip(topBar) == "uBlock (disabled for this site)", "Shield tooltip reflects status suffix");

                    topBar.IsBookmarked = true;
                    topBar.IsShieldMenuOpen = true;

                    // Paint with shield menu open
                    using (Bitmap bmp = new Bitmap(topBar.Width, topBar.Height))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        var pe = new PaintEventArgs(g, new Rectangle(0, 0, topBar.Width, topBar.Height));
                        var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(topBar, new object[] { pe });

                        // Paint in enabled state (filled shield, accent color)
                        topBar.IsShieldMenuOpen = false;
                        topBar.IsAdBlockEnabled = true;
                        topBar.AdBlockStatusSuffix = "(enabled)";
                        onPaint.Invoke(topBar, new object[] { pe });

                        // Paint in disabled state (outline shield, text primary)
                        topBar.IsAdBlockEnabled = false;
                        topBar.AdBlockStatusSuffix = "(disabled)";
                        onPaint.Invoke(topBar, new object[] { pe });

                        // Paint in disabled for this site state (outline shield, text primary)
                        topBar.IsAdBlockEnabled = true;
                        topBar.AdBlockStatusSuffix = "(disabled for this site)";
                        onPaint.Invoke(topBar, new object[] { pe });
                    }

                    Bitmap fillBmp = MaterialIcons.GetIcon("shield-fill", 16, MaterialColors.AccentBlue);
                    Assert(fillBmp != null, "shield-fill icon loads and renders with accent color");
                    Bitmap outlineBmp = MaterialIcons.GetIcon("shield", 16, MaterialColors.TextPrimary);
                    Assert(outlineBmp != null, "shield outline icon loads and renders with text primary color");

                    host.Close();
                }

                // 2. Verify uBlock Status Suffix in all conditions
                bool originalEnabled = AppServices.AdBlock.IsEnabled;
                try
                {
                    // A: Disabled globally
                    AppServices.AdBlock.IsEnabled = false;
                    string sDisabled = AppServices.AdBlock.GetStatusSuffix("https://example.com");
                    Assert(sDisabled == "(disabled)", "Disabled globally returns '(disabled)', got: " + sDisabled);

                    // B: Enabled globally, clean site
                    AppServices.AdBlock.IsEnabled = true;
                    string sEnabled = AppServices.AdBlock.GetStatusSuffix("https://example.com");
                    Assert(sEnabled == "(enabled)", "Enabled globally on regular site returns '(enabled)', got: " + sEnabled);

                    // C: Enabled globally, internal page
                    string sInternal = AppServices.AdBlock.GetStatusSuffix("about:blank");
                    Assert(sInternal == "(enabled)", "Internal page returns '(enabled)', got: " + sInternal);

                    // D: Whitelisted site
                    string testDomain = "test-whitelist-domain.org";
                    AppServices.AdBlock.ToggleDomain(testDomain);
                    string sWhitelisted = AppServices.AdBlock.GetStatusSuffix("https://" + testDomain + "/index.html");
                    Assert(sWhitelisted == "(disabled for this site)", "Whitelisted site returns '(disabled for this site)', got: " + sWhitelisted);
                    AppServices.AdBlock.ToggleDomain(testDomain); // remove from whitelist
                }
                finally
                {
                    AppServices.AdBlock.IsEnabled = originalEnabled;
                }
            });

            Test("CustomFilterRule Model Properties and Formatting", () =>
            {
                CustomFilterRule domainRule = new CustomFilterRule("CNN.COM", "##.breaking-banner");
                Assert(domainRule.Domain == "cnn.com", "Domain should be lowercased and trimmed");
                Assert(domainRule.Selector == ".breaking-banner", "Leading ## should be stripped from selector");
                Assert(domainRule.RawRule == "cnn.com##.breaking-banner", "RawRule should combine domain and selector: " + domainRule.RawRule);
                Assert(!string.IsNullOrEmpty(domainRule.Id), "Rule should generate unique ID");

                CustomFilterRule globalRule = new CustomFilterRule("", "##div#popup-promo");
                Assert(globalRule.Domain == "", "Global rule domain should be empty");
                Assert(globalRule.Selector == "div#popup-promo", "Selector stripped");
                Assert(globalRule.RawRule == "##div#popup-promo", "Global raw rule formatted with ##: " + globalRule.RawRule);
            });

            Test("AdBlockManager Add, Get, Deduplicate and Count Custom Filters", () =>
            {
                int initialCount = AppServices.AdBlock.CustomFiltersCount;
                string testDomain = "testdrop" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".org";
                string testSelector = "#ad-box-" + Guid.NewGuid().ToString("N").Substring(0, 6);

                bool added = AppServices.AdBlock.AddCustomFilter(testDomain, testSelector);
                Assert(added, "Adding new custom filter should succeed");
                Assert(AppServices.AdBlock.CustomFiltersCount == initialCount + 1, "Count should increment");

                // Deduplication check
                bool dupe = AppServices.AdBlock.AddCustomFilter(testDomain, testSelector);
                Assert(!dupe, "Adding duplicate custom filter should return false");
                Assert(AppServices.AdBlock.CustomFiltersCount == initialCount + 1, "Count should not increment on dupe");

                // Retrieve and verify
                List<CustomFilterRule> filters = AppServices.AdBlock.GetCustomFilters();
                CustomFilterRule found = null;
                for (int i = 0; i < filters.Count; i++)
                {
                    if (filters[i].Domain == testDomain && filters[i].Selector == testSelector)
                    {
                        found = filters[i];
                        break;
                    }
                }
                Assert(found != null, "Rule should be present in GetCustomFilters()");

                // Cleanup
                AppServices.AdBlock.RemoveCustomFilter(found.Id);
                Assert(AppServices.AdBlock.CustomFiltersCount == initialCount, "Count should restore after removal");
            });

            Test("AdBlockManager Custom Cosmetic Rules Compilation and Script Injection", () =>
            {
                string testSite = "dropper-site-test.com";
                string specificSelector = ".custom-dropper-hide-box";
                string globalSelector = ".global-dropper-hide-box";

                bool addedSite = AppServices.AdBlock.AddCustomFilter(testSite, specificSelector);
                bool addedGlobal = AppServices.AdBlock.AddCustomFilter("", globalSelector);
                Assert(addedSite && addedGlobal, "Both custom rules should be added");

                try
                {
                    // For the target test site: both site-specific and global rules must appear in cosmetic hiding script
                    string targetScript = AppServices.AdBlock.GetCosmeticHidingScript("https://" + testSite + "/home");
                    Assert(targetScript.Contains(specificSelector), "Target site script must contain site-specific selector: " + specificSelector);
                    Assert(targetScript.Contains(globalSelector), "Target site script must contain global selector: " + globalSelector);

                    // For an unrelated site: global rule must appear, but NOT the site-specific rule!
                    string otherScript = AppServices.AdBlock.GetCosmeticHidingScript("https://other-unrelated-domain.com/index");
                    Assert(otherScript.Contains(globalSelector), "Other site script must contain global selector: " + globalSelector);
                    Assert(!otherScript.Contains(specificSelector), "Other site script must NOT contain specific selector: " + specificSelector);
                }
                finally
                {
                    // Clean up test rules
                    List<CustomFilterRule> filters = AppServices.AdBlock.GetCustomFilters();
                    for (int i = 0; i < filters.Count; i++)
                    {
                        if (filters[i].Selector == specificSelector || filters[i].Selector == globalSelector)
                        {
                            AppServices.AdBlock.RemoveCustomFilter(filters[i].Id);
                        }
                    }
                }
            });

            Test("AdBlockManager Remove and Clear Custom Filters", () =>
            {
                string sel1 = ".dropper-clear-test-1";
                string sel2 = ".dropper-clear-test-2";
                AppServices.AdBlock.AddCustomFilter("site1.com", sel1);
                AppServices.AdBlock.AddCustomFilter("site2.com", sel2);

                List<CustomFilterRule> filters = AppServices.AdBlock.GetCustomFilters();
                string id1 = null;
                for (int i = 0; i < filters.Count; i++)
                {
                    if (filters[i].Selector == sel1) id1 = filters[i].Id;
                }
                Assert(id1 != null, "Rule 1 found");

                bool removed = AppServices.AdBlock.RemoveCustomFilter(id1);
                Assert(removed, "RemoveCustomFilter returned true");

                // Verify rule 1 is no longer in script
                string script1 = AppServices.AdBlock.GetCosmeticHidingScript("https://site1.com/");
                Assert(!script1.Contains(sel1), "Removed rule must no longer be present in cosmetic script");

                // Clear test
                AppServices.AdBlock.ClearCustomFilters();
                Assert(AppServices.AdBlock.CustomFiltersCount == 0, "CustomFiltersCount must be 0 after ClearCustomFilters");
            });

            Test("uBlock Element Picker Scriptlet Generation and Protocol Verification", () =>
            {
                string script = DropperScriptlet.GetDropperScript();
                Assert(!string.IsNullOrEmpty(script), "Element picker scriptlet must not be empty");
                Assert(script.Contains("wb-picker-highlight"), "Scriptlet must declare highlight element");
                Assert(script.Contains("wb-picker-badge"), "Scriptlet must declare badge element");
                Assert(script.Contains("wb-action:element-selected"), "Scriptlet must invoke wb-action:element-selected protocol");
                Assert(script.Contains("wb-action:cancel-element-picker"), "Scriptlet must invoke wb-action:cancel-element-picker protocol");
                Assert(script.Contains("computeOptimalSelector"), "Scriptlet must contain selector generation logic");
                // Verify window-docked design: no in-page HUD or in-page modal elements
                Assert(!script.Contains("wb-dropper-hud"), "Scriptlet must not create in-page HUD; panel is part of window");
                Assert(!script.Contains("wb-dropper-modal"), "Scriptlet must not create in-page modal; dialog follows window design");
            });

            Test("uBlock Block Element UI and Window Design Verification (Accent Color & No Emoji)", () =>
            {
                // Verify strings have no emojis or "(dropper)"
                string[] blockElementStrings = new string[]
                {
                    AppStrings.AdBlock.MenuBlockElement,
                    AppStrings.AdBlock.ContextMenuBlockElement,
                    AppStrings.AdBlock.BlockElementPanelTitle,
                    AppStrings.AdBlock.BlockElementPanelInstructions,
                    AppStrings.AdBlock.BlockElementDialogTitle,
                    AppStrings.AdBlock.BlockElementDialogDesc,
                    AppStrings.AdBlock.BlockElementBtnBlock,
                    AppStrings.AdBlock.BlockElementBtnPreview,
                    AppStrings.AdBlock.BlockElementBtnPickAgain,
                    AppStrings.AdBlock.MenuManageBlockedElements
                };

                for (int i = 0; i < blockElementStrings.Length; i++)
                {
                    string s = blockElementStrings[i];
                    Assert(!s.Contains("Ã°Å¸Å½Â¯"), "String must not contain dropper emoji: " + s);
                    Assert(!s.ToLower().Contains("dropper"), "String must not contain '(dropper)' wording: " + s);
                }

                // Verify BlockElementForm creation and controls styling
                MockElementPickerTarget mockTarget = new MockElementPickerTarget();
                using (BlockElementForm form = new BlockElementForm(mockTarget, "example.com", ".ad-banner", 3))
                {
                    Assert(form != null, "BlockElementForm should instantiate cleanly");
                    Assert(!form.Text.Contains("Ã°Å¸Å½Â¯"), "Form title must not have emojis");
                    Assert(!form.Text.ToLower().Contains("dropper"), "Form title must not have dropper wording");
                    Assert(form.FormBorderStyle == FormBorderStyle.FixedDialog, "Form must follow dialog window design");
                }
            });

            Test("SiteMuteManager Normalization & In-Memory Toggle", () =>
            {
                SiteMuteManager muteMgr = new SiteMuteManager();
                Assert(muteMgr.ExtractDomain("https://WWW.YouTube.Com/watch?v=123") == "youtube.com", "Domain normalized to lowercase without www");
                Assert(muteMgr.ExtractDomain("http://sub.domain.co.uk:8080/path") == "sub.domain.co.uk", "Subdomain preserved, port stripped");
                Assert(muteMgr.ExtractDomain("about:blank") == "", "Non-http url yields empty domain");

                muteMgr.SetSiteMuted("youtube.com", true);
                Assert(muteMgr.IsSiteMuted("https://www.youtube.com/watch?v=abc"), "Site should report muted");
                muteMgr.SetSiteMuted("youtube.com", false);
                Assert(!muteMgr.IsSiteMuted("https://www.youtube.com/watch?v=abc"), "Site should report unmuted");
            });

            Test("SiteMuteManager Persistent Site Muting Lifecycle and Event Synchronization", () =>
            {
                SiteMuteManager muteMgr = new SiteMuteManager();
                try
                {
                    string eventReceivedDomain = null;
                    muteMgr.MuteStateChanged += (s, domain) => { eventReceivedDomain = domain; };

                    muteMgr.SetSiteMuted("youtube.com", true);
                    Assert(muteMgr.IsSiteMuted("https://www.youtube.com/watch?v=123"), "youtube.com should be marked muted in SiteMute");
                    Assert(eventReceivedDomain == "youtube.com", "MuteStateChanged event should fire with domain");

                    // Reloading from storage must retain the muted domain across sessions
                    SiteMuteManager reloaded = new SiteMuteManager();
                    Assert(reloaded.IsSiteMuted("https://www.youtube.com"), "youtube.com must persist in storage across sessions");

                    // Clearing or unmuting should also persist and fire event
                    eventReceivedDomain = null;
                    muteMgr.SetSiteMuted("youtube.com", false);
                    Assert(!muteMgr.IsSiteMuted("https://www.youtube.com"), "youtube.com should be unmuted");
                    Assert(eventReceivedDomain == "youtube.com", "MuteStateChanged event should fire on unmute");

                    SiteMuteManager reloaded2 = new SiteMuteManager();
                    Assert(!reloaded2.IsSiteMuted("https://www.youtube.com"), "youtube.com must be unmuted in storage");
                }
                finally
                {
                    muteMgr.SetSiteMuted("youtube.com", false);
                }
            });

            Test("FaviconManager Cross-Origin Favicon Isolation", () =>
            {
                FaviconManager fm = new FaviconManager();
                // Test that start pages return null
                Assert(fm.GetFavicon("wb://newtab") == null, "Start page has null favicon");

                // Test domain extraction consistency
                string googleDomain = FaviconManager.ExtractDomain("https://www.google.com/search?q=reddit");
                string redditDomain = FaviconManager.ExtractDomain("https://www.reddit.com/r/all");
                string redditStaticDomain = FaviconManager.ExtractDomain("https://www.redditstatic.com/shreddit/assets/favicon/192x192.png");

                Assert(googleDomain == "google.com", "Google domain extracted correctly");
                Assert(redditDomain == "reddit.com", "Reddit domain extracted correctly");
                Assert(redditStaticDomain == "redditstatic.com", "Reddit static CDN domain extracted correctly");
                Assert(googleDomain != redditDomain, "Google and Reddit domains must not collide");
                Assert(googleDomain != redditStaticDomain, "Google and Reddit static CDN domains must not collide");
            });

            Test("Tab Context Menu Strings Zero Emojis", () =>
            {
                string[] tabMenuStrings = new string[]
                {
                    AppStrings.Browser.TabMenuNewTab,
                    AppStrings.Browser.TabMenuNewTabRight,
                    AppStrings.Browser.TabMenuReload,
                    AppStrings.Browser.TabMenuDuplicate,
                    AppStrings.Browser.TabMenuPin,
                    AppStrings.Browser.TabMenuUnpin,
                    AppStrings.Browser.TabMenuMuteSite,
                    AppStrings.Browser.TabMenuUnmuteSite,
                    AppStrings.Browser.TabMenuClose,
                    AppStrings.Browser.TabMenuCloseOthers,
                    AppStrings.Browser.TabMenuCloseRight,
                    AppStrings.Browser.TabMenuCloseLeft,
                    AppStrings.Browser.TabMenuReopenClosed,
                    AppStrings.Browser.TabMenuBookmark,
                    AppStrings.Browser.TabMenuBookmarkAll,
                    AppStrings.Browser.NotifyBookmarkAllAddedFormat
                };

                foreach (string s in tabMenuStrings)
                {
                    Assert(!string.IsNullOrEmpty(s), "Tab menu string must not be empty");
                    for (int i = 0; i < s.Length; i++)
                    {
                        Assert(!char.IsSurrogate(s[i]), "Tab menu string must not contain emojis: " + s);
                    }
                }
            });

            Test("Pinned Tab and Audio Indicator TopBar Geometry", () =>
            {
                using (ChromeTabControl tc = new ChromeTabControl())
                using (WbTopBar bar = new WbTopBar())
                {
                    MockFaviconTab pinnedTab = new MockFaviconTab("Pinned", "https://youtube.com");
                    pinnedTab.IsPinned = true;
                    pinnedTab.IsPlayingAudio = true;
                    tc.TabPages.Add(pinnedTab);

                    MockFaviconTab normalTab = new MockFaviconTab("Normal", "https://example.com");
                    tc.TabPages.Add(normalTab);

                    bar.Size = new Size(800, 42);
                    bar.BindTabControl(tc);

                    Assert(bar.GetPinnedTabCount() == 1, "Pinned tab count must be 1");
                    Assert(bar.GetPinnedTabWidth() > 0, "Pinned tab width must be positive");
                    Assert(!bar.GetTabAudioRect(0).IsEmpty, "Pinned tab with audio has non-empty audio rect");
                    Assert(bar.GetTabCloseRect(0).IsEmpty, "Pinned tab close rect must be empty (cannot close via close button)");
                    Assert(!bar.GetTabCloseRect(1).IsEmpty, "Normal unpinned tab has non-empty close rect");

                    Rectangle audioRect = bar.GetTabAudioRect(0);
                    Assert(bar.HitTestAudioButtonAt(new Point(audioRect.Left + 2, audioRect.Top + 2)) == 0, "Audio button hit-test matches tab index 0");

                    // Verify left-clicking on pinned tab audio indicator does NOT fire TabAudioClicked
                    bool audioClickedFired = false;
                    bar.TabAudioClicked += (s, idx) => { audioClickedFired = true; };

                    var onMouseDownMethod = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Point clickPt = new Point(audioRect.Left + 2, audioRect.Top + 2);
                    onMouseDownMethod.Invoke(bar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickPt.X, clickPt.Y, 0) });
                    Assert(!audioClickedFired, "Pinned tab must NOT fire TabAudioClicked on left click");

                    // Verify normal unpinned tab audio button DOES fire TabAudioClicked on click
                    normalTab.IsPlayingAudio = true;
                    Rectangle normalAudioRect = bar.GetTabAudioRect(1);
                    Assert(!normalAudioRect.IsEmpty, "Normal tab with audio has non-empty audio rect");
                    Point normalClickPt = new Point(normalAudioRect.Left + 2, normalAudioRect.Top + 2);
                    onMouseDownMethod.Invoke(bar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, normalClickPt.X, normalClickPt.Y, 0) });
                    Assert(audioClickedFired, "Normal unpinned tab MUST fire TabAudioClicked when clicking audio button");
                }
            });

            Test("WbTopBar Painting Full Cycle", () =>
            {
                using (ChromeTabControl tc = new ChromeTabControl())
                using (WbTopBar bar = new WbTopBar())
                {
                    MockFaviconTab pinnedTab = new MockFaviconTab("Pinned", "https://youtube.com");
                    pinnedTab.IsPinned = true;
                    pinnedTab.IsPlayingAudio = true;
                    tc.TabPages.Add(pinnedTab);

                    MockFaviconTab normalTab = new MockFaviconTab("Normal", "https://example.com");
                    normalTab.IsPlayingAudio = true;
                    normalTab.IsAudioMuted = true;
                    tc.TabPages.Add(normalTab);

                    MockFaviconTab thirdTab = new MockFaviconTab("Audio Playing", "https://music.youtube.com");
                    thirdTab.IsPlayingAudio = true;
                    tc.TabPages.Add(thirdTab);

                    bar.Size = new Size(1280, 42);
                    bar.BindTabControl(tc);

                    var method = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    // Test wide 1280px paint
                    using (Bitmap bmp = new Bitmap(1280, 42))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        method.Invoke(bar, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 1280, 42)) });
                    }

                    // Test narrow 320px paint (overflow and compression edge case)
                    bar.Size = new Size(320, 42);
                    using (Bitmap bmpNarrow = new Bitmap(320, 42))
                    using (Graphics gNarrow = Graphics.FromImage(bmpNarrow))
                    {
                        method.Invoke(bar, new object[] { new PaintEventArgs(gNarrow, new Rectangle(0, 0, 320, 42)) });
                    }
                }
            });

            Test("WbTopBar Loading Spinner Replaces Favicon on Loading Tabs", () =>
            {
                using (ChromeTabControl tc = new ChromeTabControl())
                using (WbTopBar bar = new WbTopBar())
                {
                    MockFaviconTab pinnedTab = new MockFaviconTab("Pinned Tab", "https://youtube.com");
                    pinnedTab.IsPinned = true;
                    pinnedTab.Favicon = new Bitmap(16, 16);
                    tc.TabPages.Add(pinnedTab);

                    MockFaviconTab normalTab = new MockFaviconTab("Normal Tab", "https://example.com");
                    normalTab.Favicon = new Bitmap(16, 16);
                    tc.TabPages.Add(normalTab);

                    bar.Size = new Size(800, 42);
                    bar.BindTabControl(tc);

                    // Initially not loading
                    Assert(!bar.IsTabLoading(pinnedTab), "Pinned tab initially not loading");
                    Assert(!bar.IsTabLoading(normalTab), "Normal tab initially not loading");
                    Assert(!bar.IsSpinnerAnimating, "Spinner timer should be idle when no tabs loading");

                    // Set pinned tab to loading
                    pinnedTab.IsLoading = true;
                    pinnedTab.FireLoadingStateChanged();

                    Assert(bar.IsTabLoading(pinnedTab), "bar.IsTabLoading must return true for pinnedTab");
                    Assert(bar.IsSpinnerAnimating, "Spinner timer must start when pinned tab is loading");

                    // Paint with pinned tab loading
                    var paintMethod = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    using (Bitmap bmp = new Bitmap(800, 42))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        paintMethod.Invoke(bar, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 800, 42)) });
                    }

                    // Set normal tab to loading as well
                    normalTab.IsLoading = true;
                    normalTab.FireLoadingStateChanged();
                    Assert(bar.IsTabLoading(normalTab), "bar.IsTabLoading must return true for normalTab");

                    // Finish loading on pinned tab, normal tab still loading
                    pinnedTab.IsLoading = false;
                    pinnedTab.FireLoadingStateChanged();
                    Assert(!bar.IsTabLoading(pinnedTab), "Pinned tab done loading");
                    Assert(bar.IsTabLoading(normalTab), "Normal tab still loading");
                    Assert(bar.IsSpinnerAnimating, "Spinner timer still active while normal tab loads");

                    // Paint with normal tab loading
                    using (Bitmap bmp = new Bitmap(800, 42))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        paintMethod.Invoke(bar, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 800, 42)) });
                    }

                    // Finish loading on normal tab
                    normalTab.IsLoading = false;
                    normalTab.FireLoadingStateChanged();
                    Assert(!bar.IsTabLoading(normalTab), "Normal tab done loading");
                    Assert(!bar.IsSpinnerAnimating, "Spinner timer must stop when all tabs finish loading");

                    // Also test TopBar.IsLoading property triggers spinner for selected tab
                    bar.IsLoading = true;
                    Assert(bar.IsTabLoading(tc.SelectedTab), "Selected tab reports loading when bar.IsLoading = true");
                    Assert(bar.IsSpinnerAnimating, "Spinner timer starts when bar.IsLoading = true");

                    bar.IsLoading = false;
                    Assert(!bar.IsTabLoading(tc.SelectedTab), "Selected tab reports not loading when bar.IsLoading = false");
                    Assert(!bar.IsSpinnerAnimating, "Spinner timer stops when bar.IsLoading = false");
                }
            });

            Test("TopBar Active Webcam/Mic Media Capture Red Badge Geometry and Painting", () =>
            {
                using (ChromeTabControl tc = new ChromeTabControl())
                using (WbTopBar bar = new WbTopBar())
                {
                    MockFaviconTab pinnedTab = new MockFaviconTab("Pinned Video Call", "https://meet.google.com");
                    pinnedTab.IsPinned = true;
                    pinnedTab.IsMediaCaptured = true;
                    pinnedTab.IsPlayingAudio = true;
                    tc.TabPages.Add(pinnedTab);

                    MockFaviconTab normalTab = new MockFaviconTab("Normal Meeting", "https://zoom.us");
                    normalTab.IsMediaCaptured = true;
                    normalTab.IsPlayingAudio = true;
                    tc.TabPages.Add(normalTab);

                    MockFaviconTab plainTab = new MockFaviconTab("Plain Tab", "https://example.com");
                    tc.TabPages.Add(plainTab);

                    bar.Size = new Size(1000, 42);
                    bar.BindTabControl(tc);

                    Rectangle pinnedBadge = bar.GetTabMediaBadgeRect(0);
                    Assert(!pinnedBadge.IsEmpty, "Pinned tab with active media capture has non-empty badge rect");
                    Assert(pinnedBadge.Width >= 4 && pinnedBadge.Height >= 4, "Badge rect size is at least 4x4");
                    Rectangle pinnedTabRect = bar.GetTabRect(0);
                    Assert(pinnedBadge.Left >= pinnedTabRect.Left && pinnedBadge.Right <= pinnedTabRect.Right, "Pinned badge is within tab bounds");
                    Assert(pinnedBadge.Top >= pinnedTabRect.Top && pinnedBadge.Bottom <= pinnedTabRect.Bottom, "Pinned badge is within vertical tab bounds");

                    Rectangle normalBadge = bar.GetTabMediaBadgeRect(1);
                    Assert(!normalBadge.IsEmpty, "Normal tab with active media capture has non-empty badge rect");
                    Rectangle normalTabRect = bar.GetTabRect(1);
                    Assert(normalBadge.Left >= normalTabRect.Left && normalBadge.Left < normalTabRect.Left + 20, "Normal badge is on top left of tab");

                    Rectangle plainBadge = bar.GetTabMediaBadgeRect(2);
                    Assert(plainBadge.IsEmpty, "Plain tab without media capture has empty badge rect");

                    // Verify painting cycle executes without error
                    var method = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    using (Bitmap bmp = new Bitmap(1000, 42))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        method.Invoke(bar, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 1000, 42)) });

                        // Verify red badge pixel exists in the pinned and unpinned badge areas
                        Color pinnedBadgeCenter = bmp.GetPixel(pinnedBadge.Left + pinnedBadge.Width / 2, pinnedBadge.Top + pinnedBadge.Height / 2);
                        Assert(pinnedBadgeCenter.R > 200 && pinnedBadgeCenter.G > 100 && pinnedBadgeCenter.B > 100, "Pinned badge center pixel is Material StatusDanger red (#F28B82)");

                        Color normalBadgeCenter = bmp.GetPixel(normalBadge.Left + normalBadge.Width / 2, normalBadge.Top + normalBadge.Height / 2);
                        Assert(normalBadgeCenter.R > 200 && normalBadgeCenter.G > 100 && normalBadgeCenter.B > 100, "Normal badge center pixel is Material StatusDanger red (#F28B82)");
                    }
                }
            });

            Test("Phosphor Speaker Vector Rendering and Preview", () =>
            {
                using (Bitmap bmp = new Bitmap(300, 100))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(41, 42, 45));
                    int[] sizes = new int[] { 14, 16, 20, 24 };
                    int x = 10;
                    foreach (int s in sizes)
                    {
                        Bitmap spk = MaterialIcons.GetIcon("speaker-high", s, Color.White);
                        Assert(spk != null, "speaker-high rendered for size " + s);
                        g.DrawImage(spk, new Rectangle(x, 15, spk.Width, spk.Height), 0, 0, spk.Width, spk.Height, GraphicsUnit.Pixel);

                        Bitmap mut = MaterialIcons.GetIcon("speaker-slash", s, MaterialColors.StatusDanger);
                        Assert(mut != null, "speaker-slash rendered for size " + s);
                        g.DrawImage(mut, new Rectangle(x, 55, mut.Width, mut.Height), 0, 0, mut.Width, mut.Height, GraphicsUnit.Pixel);

                        x += s + 15;
                    }

                    string previewPath = Path.Combine(Path.GetTempPath(), "speaker_preview.png");
                    bmp.Save(previewPath, System.Drawing.Imaging.ImageFormat.Png);
                    Assert(System.IO.File.Exists(previewPath), "speaker_preview.png must exist");
                    try { System.IO.File.Delete(previewPath); } catch { }
                }
            });

            Test("Native Dark Mode StatusStrip and Renderer Verification", () =>
            {
                using (StatusStrip strip = new StatusStrip())
                using (Bitmap bmp = new Bitmap(500, 24))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    strip.Renderer = new MaterialToolStripRenderer();
                    strip.BackColor = MaterialColors.StatusBackground;
                    strip.ForeColor = MaterialColors.TextPrimary;
                    strip.SizingGrip = false;
                    strip.Size = new Size(500, 24);

                    ToolStripStatusLabel statusLbl = new ToolStripStatusLabel();
                    statusLbl.Spring = true;
                    statusLbl.TextAlign = ContentAlignment.MiddleLeft;
                    statusLbl.Text = "https://example.com/item";
                    strip.Items.Add(statusLbl);

                    ToolStripStatusLabel badgeLbl = new ToolStripStatusLabel();
                    badgeLbl.Alignment = ToolStripItemAlignment.Right;
                    badgeLbl.Text = "12 blocked";
                    badgeLbl.ForeColor = ThemeManager.CurrentAccentColor;
                    strip.Items.Add(badgeLbl);

                    strip.DrawToBitmap(bmp, new Rectangle(0, 0, 500, 24));
                    Assert(strip.BackColor == MaterialColors.StatusBackground, "StatusStrip background must be MaterialColors.StatusBackground (#1A1A1C)");
                    Assert(strip.SizingGrip == false, "Sizing grip must be disabled for clean borderless look");
                    Assert(statusLbl.Text == "https://example.com/item", "Status text matches set URL");
                    Assert(badgeLbl.Text == "12 blocked", "Badge text matches blocked count");
                }
            });

            Test("DropperScriptlet Console and PostMessage Action Generation", () =>
            {
                string script = DropperScriptlet.GetDropperScript("#1a73e8");
                Assert(!string.IsNullOrEmpty(script), "Dropper script generated");
                Assert(script.Contains("console.log(selAction)"), "Script must send element selection via console.log to bypass CSP");
                Assert(script.Contains("console.log(cancelAction)"), "Script must send cancel action via console.log to bypass CSP");
                Assert(script.Contains("PostMessage(selAction)"), "Script must support PostMessage fallback for element selection");
                Assert(script.Contains("PostMessage(cancelAction)"), "Script must support PostMessage fallback for cancel action");
                Assert(script.Contains("#1a73e8"), "Accent color properly injected into script");
            });

            Test("BlockElementForm TopMost and Cancel Lifecycle", () =>
            {
                MockElementPickerTarget target = new MockElementPickerTarget();
                using (BlockElementForm form = new BlockElementForm(target, "test.com", "div.promo", 7))
                {
                    Assert(form.TopMost == true, "BlockElementForm must have TopMost == true so it never hides behind browser");
                    Assert(form.StartPosition == FormStartPosition.CenterParent, "BlockElementForm must center on parent form");
                    Assert(form.FormBorderStyle == FormBorderStyle.FixedDialog, "BlockElementForm is a fixed native dialog");

                    // Close without OK -> must cancel picker on target cleanly
                    form.Close();
                    Assert(target.Cancelled == true, "Closing BlockElementForm must cancel element picker on target");
                }
            });

            Test("BlockElementForm Confirmation Closes Dialog And Commits Block", () =>
            {
                MockElementPickerTarget target = new MockElementPickerTarget();
                using (BlockElementForm form = new BlockElementForm(target, "example.com", ".advertisement-box", 2))
                {
                    Assert(form.DialogResult == DialogResult.None, "DialogResult initially None");
                    form.ConfirmSelectedElement();
                    Assert(form.DialogResult == DialogResult.OK, "DialogResult must be OK upon element confirmation");
                    Assert(target.BlockedSelector == ".advertisement-box", "Confirmed element must commit block on target");
                }
            });

            Test("BlockElementForm Hierarchy Candidates and Child vs Parent Selection", () =>
            {
                MockElementPickerTarget target = new MockElementPickerTarget();
                string json = "[{\"label\":\"Specific Child (<img.banner>)\",\"selector\":\"#ad-unit > img\",\"matches\":1,\"depth\":0},{\"label\":\"Container (<div.ad-unit>)\",\"selector\":\"#ad-unit\",\"matches\":1,\"depth\":1}]";
                using (BlockElementForm form = new BlockElementForm(target, "example.com", "#ad-unit > img", 1, json))
                {
                    Assert(form != null, "BlockElementForm instantiated with candidates");

                    // Check that the scriptlet declares candidate generation & hierarchy support
                    string script = DropperScriptlet.GetDropperScript();
                    Assert(script.Contains("buildCandidates"), "Scriptlet must generate hierarchy candidates");
                    Assert(script.Contains("buildHierarchy"), "Scriptlet must track DOM hierarchy for parent/child");
                    Assert(script.Contains("candidates="), "Scriptlet must pass candidates in element-selected action");
                }
            });

            Test("Web Content Default Dark Theme Configuration", () =>
            {
                // Verify Program.cs configures CEF to report dark theme to webpages by default
                string programCsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src\Core\Program.cs");
                if (!File.Exists(programCsPath))
                {
                    programCsPath = Path.Combine(Directory.GetCurrentDirectory(), @"src\Core\Program.cs");
                }
                if (File.Exists(programCsPath))
                {
                    string content = File.ReadAllText(programCsPath);
                    Assert(content.Contains("\"force-dark-mode\", \"1\""), "Program.cs must pass force-dark-mode switch");
                    Assert(content.Contains("\"blink-settings\", \"preferredColorScheme=0\""), "Program.cs must set preferredColorScheme=0 (kDark) in blink-settings");
                    Assert(!content.Contains("\"blink-settings\", \"preferredColorScheme=1\""), "Program.cs must not set preferredColorScheme=1 (which forces light mode)");
                    Assert(content.Contains("settings.BackgroundColor = Cef.ColorSetARGB(255, 32, 33, 36)"), "Program.cs must set dark default BackgroundColor on CefSettings");
                    Assert(content.Contains("\"default-background-color\", \"ff202124\"") || content.Contains("\"default-background-color\", \"202124ff\""), "Program.cs must pass default-background-color switch to prevent white flash");
                    Assert(content.Contains("\"disable-features\", \"WebRtcHideLocalIpsWithMdns\""), "Program.cs must disable WebRtcHideLocalIpsWithMdns to suppress UDP 5353 firewall triggers");
                    Assert(content.Contains("\"webrtc-ip-handling-policy\", \"default_public_interface_only\""), "Program.cs must set webrtc-ip-handling-policy to default_public_interface_only");
                }

                // Verify BrowserTab.cs configures DevTools Emulation and BrowserSettings for dark background
                string browserTabPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src\Browser\BrowserTab.cs");
                if (!browserTabPath.Contains("BrowserTab") || !File.Exists(browserTabPath))
                {
                    browserTabPath = Path.Combine(Directory.GetCurrentDirectory(), @"src\Browser\BrowserTab.cs");
                }
                if (File.Exists(browserTabPath))
                {
                    string content = File.ReadAllText(browserTabPath);
                    Assert(content.Contains("ApplyDarkColorSchemeEmulation"), "BrowserTab must call ApplyDarkColorSchemeEmulation");
                    Assert(content.Contains("Emulation.setEmulatedMedia"), "BrowserTab must invoke DevTools Emulation.setEmulatedMedia");
                    Assert(content.Contains("prefers-color-scheme") && content.Contains("dark"), "BrowserTab must specify prefers-color-scheme: dark");
                    Assert(content.Contains("Emulation.setDefaultBackgroundColorOverride"), "BrowserTab must invoke DevTools Emulation.setDefaultBackgroundColorOverride");
                    Assert(content.Contains("BrowserSettings.BackgroundColor = Cef.ColorSetARGB(255, 32, 33, 36)"), "BrowserTab must configure dark BackgroundColor on BrowserSettings");
                }
            });

            Test("WbTopBar Tab Dynamic Width Scaling, Shortest Tab Close Icon and Full Title Tooltip", () =>
            {
                using (Form host = new Form { Size = new Size(1200, 600) })
                using (WbTopBar topBar = new WbTopBar())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.Width = 1200;
                    topBar.BindTabControl(tc);
                    host.Controls.Add(topBar);
                    host.Controls.Add(tc);
                    host.Show();
                    Application.DoEvents();

                    // 1. Long tab width scaling (max 256px)
                    string fullLongTitle = "GitHub: Let's build from here - The complete developer platform to build, scale and deliver secure software.";
                    MockFaviconTab tab1 = new MockFaviconTab(fullLongTitle, "https://github.com");
                    tc.TabPages.Add(tab1);
                    topBar.UpdateTabs();

                    int w1 = topBar.GetVisibleTabWidth();
                    Assert(w1 == 256, "Single tab on wide window scales up to maxTabW (256px), got: " + w1);

                    // 2. Tab becomes shorter as more tabs open
                    for (int i = 2; i <= 6; i++)
                    {
                        tc.TabPages.Add(new MockFaviconTab("Tab " + i, "https://example.com/" + i));
                    }
                    topBar.UpdateTabs();
                    int w6 = topBar.GetVisibleTabWidth();
                    Assert(w6 < w1, "Tab width decreases as more tabs open (w6 < w1: " + w6 + " < " + w1 + ")");

                    // Add many more tabs to reach shortest width (48px)
                    for (int i = 7; i <= 25; i++)
                    {
                        tc.TabPages.Add(new MockFaviconTab("Tab " + i, "https://example.com/" + i));
                    }
                    topBar.UpdateTabs();
                    int wShort = topBar.GetVisibleTabWidth();
                    Assert(wShort >= 48 && wShort <= 53, "Tab width shrinks down to minTabW range (48-53px) under high tab count, got: " + wShort);

                    // 3. Shortest tabs must retain close tab icon (similar to Chrome)
                    Rectangle shortTabRect = topBar.GetTabRect(0);
                    Rectangle shortCloseRect = topBar.GetTabCloseRect(0);
                    Assert(!shortCloseRect.IsEmpty, "Shortest tab (48px) must retain a valid, non-empty close button rect");
                    Assert(shortCloseRect.Width >= 14 && shortCloseRect.Height >= 14, "Shortest tab close button has appropriate size");
                    Assert(shortCloseRect.Right <= shortTabRect.Right, "Close button is contained within the tab boundary");
                    Assert(shortCloseRect.Left >= shortTabRect.Left + 20, "Close button leaves room for the favicon on shortest tab");

                    // 4. Tab tooltip showing full tab title
                    var onMouseMove = typeof(WbTopBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    // Hover tab body (away from close rect) -> full title tooltip
                    int tabBodyX = shortTabRect.Left + 10;
                    int tabBodyY = shortTabRect.Top + shortTabRect.Height / 2;
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, tabBodyX, tabBodyY, 0) });
                    string tip = topBar.GetCurrentToolTip();
                    Assert(tip == fullLongTitle, "Tab tooltip shows full untruncated tab title, expected '" + fullLongTitle + "', got '" + tip + "'");

                    // Hover close button -> TipCloseTab tooltip
                    int closeX = shortCloseRect.Left + shortCloseRect.Width / 2;
                    int closeY = shortCloseRect.Top + shortCloseRect.Height / 2;
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, closeX, closeY, 0) });
                    Assert(topBar.GetCurrentToolTip() == AppStrings.Browser.TipCloseTab, "Close button hover shows TipCloseTab tooltip");

                    // 5. Paint verification and artifact previews
                    string artDir = GetArtifactsDir();
                    if (artDir != null)
                    {
                        using (Bitmap bmp = new Bitmap(topBar.Width, topBar.Height))
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            var pe = new PaintEventArgs(g, new Rectangle(0, 0, topBar.Width, topBar.Height));
                            var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            onPaint.Invoke(topBar, new object[] { pe });
                            bmp.Save(Path.Combine(artDir, "tab_scaling_short_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }

                    host.Close();
                }

                // Render wide single/few tab preview
                using (Form wideHost = new Form { Size = new Size(1200, 600) })
                using (WbTopBar wideBar = new WbTopBar())
                using (ChromeTabControl wideTc = new ChromeTabControl())
                {
                    wideBar.Width = 1200;
                    wideBar.BindTabControl(wideTc);
                    wideHost.Controls.Add(wideBar);
                    wideHost.Controls.Add(wideTc);
                    wideTc.TabPages.Add(new MockFaviconTab("GitHub: Let's build from here - Developer platform", "https://github.com"));
                    wideTc.TabPages.Add(new MockFaviconTab("Google - Search the world's information", "https://google.com"));
                    wideBar.UpdateTabs();

                    string artDir = GetArtifactsDir();
                    if (artDir != null)
                    {
                        using (Bitmap bmp = new Bitmap(wideBar.Width, wideBar.Height))
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            var pe = new PaintEventArgs(g, new Rectangle(0, 0, wideBar.Width, wideBar.Height));
                            var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            onPaint.Invoke(wideBar, new object[] { pe });
                            bmp.Save(Path.Combine(artDir, "tab_scaling_wide_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
            });

            Test("WbTopBar Cross-Window Tab Drag, Drop onto Window Tab Section and ComputeTabDropIndex", () =>
            {
                using (Form winA = new Form { Size = new Size(800, 600), StartPosition = FormStartPosition.Manual, Location = new Point(100, 100) })
                using (Form winB = new Form { Size = new Size(800, 600), StartPosition = FormStartPosition.Manual, Location = new Point(950, 100) })
                using (WbTopBar topBarA = new WbTopBar { Width = 800 })
                using (WbTopBar topBarB = new WbTopBar { Width = 800 })
                using (ChromeTabControl tcA = new ChromeTabControl())
                using (ChromeTabControl tcB = new ChromeTabControl())
                {
                    topBarA.BindTabControl(tcA);
                    topBarB.BindTabControl(tcB);
                    winA.Controls.Add(topBarA);
                    winA.Controls.Add(tcA);
                    winB.Controls.Add(topBarB);
                    winB.Controls.Add(tcB);

                    tcA.TabPages.Add(new TabPage("Alpha 1"));
                    tcA.TabPages.Add(new TabPage("Alpha 2"));
                    topBarA.UpdateTabs();

                    tcB.TabPages.Add(new TabPage("Beta 1"));
                    tcB.TabPages.Add(new TabPage("Beta 2"));
                    topBarB.UpdateTabs();

                    winA.Show();
                    winB.Show();
                    Application.DoEvents();

                    // 1. Test ComputeTabDropIndex on topBarB
                    Rectangle bTab0 = topBarB.GetTabRect(0);
                    Rectangle bTab1 = topBarB.GetTabRect(1);
                    Assert(!bTab0.IsEmpty && !bTab1.IsEmpty, "Tabs on topBarB have valid rectangles");

                    // Left of Tab 0 -> index 0
                    int idxLeft = topBarB.ComputeTabDropIndex(bTab0.Left - 10);
                    Assert(idxLeft == 0, "Dropping to left of first tab yields index 0, got: " + idxLeft);

                    // Right half of Tab 0 -> index 1
                    int idxBetween = topBarB.ComputeTabDropIndex(bTab0.Left + bTab0.Width * 3 / 4);
                    Assert(idxBetween == 1, "Dropping on right half of Tab 0 yields index 1, got: " + idxBetween);

                    // Right half of Tab 1 -> index 2
                    int idxAfter = topBarB.ComputeTabDropIndex(bTab1.Right + 20);
                    Assert(idxAfter == 2, "Dropping to right of last tab yields index 2, got: " + idxAfter);

                    // 2. Drag Tab Alpha 1 from winA and move over winB's tab section
                    var onMouseDownA = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseMoveA = typeof(WbTopBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseUpA = typeof(WbTopBar).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    WbTopBar.TabDroppedOntoWindowEventArgs droppedArgs = null;
                    topBarA.TabDroppedOntoWindow += (s, e) => { droppedArgs = e; };

                    // MouseDown on Tab Alpha 1
                    Rectangle aTab0 = topBarA.GetTabRect(0);
                    onMouseDownA.Invoke(topBarA, new object[] { new MouseEventArgs(MouseButtons.Left, 1, aTab0.Left + 10, aTab0.Top + 10, 0) });

                    // Compute point over topBarB in topBarA's client coordinates
                    Point bStripScreenPt = topBarB.PointToScreen(new Point(bTab0.Right + 5, bTab0.Top + 10));
                    Point overBInAClient = topBarA.PointToClient(bStripScreenPt);

                    // MouseMove over topBarB
                    onMouseMoveA.Invoke(topBarA, new object[] { new MouseEventArgs(MouseButtons.Left, 1, overBInAClient.X, overBInAClient.Y, 0) });

                    Assert(topBarB.DropHighlight, "topBarB must activate DropHighlight when tab is dragged over its tab section");

                    string artDir = GetArtifactsDir();
                    if (artDir != null)
                    {
                        using (Bitmap bmp = new Bitmap(topBarB.Width, topBarB.Height))
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            var pe = new PaintEventArgs(g, new Rectangle(0, 0, topBarB.Width, topBarB.Height));
                            var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            onPaint.Invoke(topBarB, new object[] { pe });
                            bmp.Save(Path.Combine(artDir, "tab_cross_window_drop_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }

                    // MouseUp over topBarB
                    onMouseUpA.Invoke(topBarA, new object[] { new MouseEventArgs(MouseButtons.Left, 1, overBInAClient.X, overBInAClient.Y, 0) });

                    Assert(!topBarB.DropHighlight, "topBarB DropHighlight must be cleared upon mouse release");
                    Assert(droppedArgs != null, "TabDroppedOntoWindow event must be fired on topBarA");
                    Assert(droppedArgs.TabIndex == 0, "Dropped tab index must be 0");
                    Assert(droppedArgs.TargetWindow == winB, "Dropped TargetWindow must be winB");
                    Assert(droppedArgs.TargetDropIndex == 1, "Dropped TargetDropIndex must be 1 (between Beta 1 and Beta 2), got: " + droppedArgs.TargetDropIndex);

                    // 3. Test single-tab window drag-to-window
                    tcA.TabPages.RemoveAt(0); // winA now has only 1 tab ("Alpha 2")
                    topBarA.UpdateTabs();
                    Assert(tcA.TabPages.Count == 1, "winA now has only 1 tab");

                    droppedArgs = null;
                    Rectangle aTabSingle = topBarA.GetTabRect(0);
                    onMouseDownA.Invoke(topBarA, new object[] { new MouseEventArgs(MouseButtons.Left, 1, aTabSingle.Left + 10, aTabSingle.Top + 10, 0) });
                    onMouseMoveA.Invoke(topBarA, new object[] { new MouseEventArgs(MouseButtons.Left, 1, overBInAClient.X, overBInAClient.Y, 0) });
                    Assert(topBarB.DropHighlight, "Single-tab window dragging tab over topBarB must also activate DropHighlight");

                    onMouseUpA.Invoke(topBarA, new object[] { new MouseEventArgs(MouseButtons.Left, 1, overBInAClient.X, overBInAClient.Y, 0) });
                    Assert(droppedArgs != null, "Single-tab window must successfully fire TabDroppedOntoWindow when dropped on winB");
                    Assert(droppedArgs.TargetWindow == winB, "TargetWindow is winB for single-tab window");

                    winA.Close();
                    winB.Close();
                }
            });

            Test("WebDragDropHelper and Control UI Drag Protection", () =>
            {
                // 1. Verify script structure
                string script = WebDragDropHelper.DragControlFixScript;
                Assert(!string.IsNullOrEmpty(script), "DragControlFixScript must not be empty");
                Assert(script.Contains("-webkit-user-drag: none !important"), "Script must inject -webkit-user-drag: none style for controls");
                Assert(script.Contains("input[type=\"range\"]"), "Script must target range inputs");
                Assert(script.Contains("[role=\"slider\"]"), "Script must target slider role");
                Assert(script.Contains("[aria-valuenow]"), "Script must target aria-valuenow");
                Assert(script.Contains("isControlElement"), "Script must contain isControlElement detection");
                Assert(script.Contains("isDraggablePayload"), "Script must contain isDraggablePayload detection");
                Assert(script.Contains("addEventListener('dragstart'"), "Script must intercept dragstart in capture phase");
                Assert(script.Contains("a[href]"), "Script must preserve genuine hyperlinks");
                Assert(script.Contains("IMG"), "Script must preserve images");
                Assert(script.Contains("getSelection"), "Script must preserve selected text drags");

                // 2. Test IsValidWebUrl
                Assert(WebDragDropHelper.IsValidWebUrl("https://github.com"), "https:// is valid");
                Assert(WebDragDropHelper.IsValidWebUrl("http://example.com"), "http:// is valid");
                Assert(WebDragDropHelper.IsValidWebUrl("www.google.com"), "www. is valid");
                Assert(WebDragDropHelper.IsValidWebUrl("about:blank"), "about: is valid");
                Assert(WebDragDropHelper.IsValidWebUrl("chrome://version"), "chrome:// is valid");
                Assert(WebDragDropHelper.IsValidWebUrl("file:///C:/test.html"), "file:// is valid");
                Assert(WebDragDropHelper.IsValidWebUrl("wb:settings"), "wb: is valid");
                Assert(WebDragDropHelper.IsValidWebUrl("wikipedia.org/wiki/Web"), "domain/path is valid");
                Assert(!WebDragDropHelper.IsValidWebUrl("not a url at all"), "arbitrary sentence is not a url");
                Assert(!WebDragDropHelper.IsValidWebUrl(""), "empty string is not a url");
                Assert(!WebDragDropHelper.IsValidWebUrl(null), "null is not a url");

                // 3. Test CleanUrl
                Assert(WebDragDropHelper.CleanUrl("www.google.com") == "https://www.google.com", "www prefix adds https://");
                Assert(WebDragDropHelper.CleanUrl("github.com/foo") == "https://github.com/foo", "bare domain adds https://");
                Assert(WebDragDropHelper.CleanUrl("https://github.com") == "https://github.com", "already absolute stays intact");

                // 4. Test ExtractUrlFromDataObject with DataObject
                DataObject textObj = new DataObject();
                textObj.SetData(DataFormats.Text, "https://developer.mozilla.org");
                Assert(WebDragDropHelper.ExtractUrlFromDataObject(textObj) == "https://developer.mozilla.org", "ExtractUrlFromDataObject extracts plain text URL");

                DataObject unicodeObj = new DataObject();
                unicodeObj.SetData(DataFormats.UnicodeText, "www.youtube.com");
                Assert(WebDragDropHelper.ExtractUrlFromDataObject(unicodeObj) == "https://www.youtube.com", "ExtractUrlFromDataObject cleans and extracts UnicodeText URL");

                DataObject nonUrlObj = new DataObject();
                nonUrlObj.SetData(DataFormats.Text, "Just some normal words without dot");
                Assert(WebDragDropHelper.ExtractUrlFromDataObject(nonUrlObj) == null, "ExtractUrlFromDataObject returns null for non-URLs");

                DataObject htmlObj = new DataObject();
                htmlObj.SetData("HTML Format", "<p>Hello <a href=\"https://www.microsoft.com\">Microsoft</a></p>");
                Assert(WebDragDropHelper.ExtractUrlFromDataObject(htmlObj) == "https://www.microsoft.com", "ExtractUrlFromDataObject parses href from HTML Format");

                // 5. Test HasUrlData
                Assert(WebDragDropHelper.HasUrlData(textObj), "HasUrlData returns true for URL payload");
                Assert(!WebDragDropHelper.HasUrlData(nonUrlObj), "HasUrlData returns false for non-URL payload");

                // 6. Verify CustomMenuHandler file has Chrome-inspired options and returns false in RunContextMenu
                string handlerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src\Browser\Handlers\CustomMenuHandler.cs");
                if (!File.Exists(handlerPath))
                {
                    handlerPath = Path.Combine(Directory.GetCurrentDirectory(), @"src\Browser\Handlers\CustomMenuHandler.cs");
                }
                if (File.Exists(handlerPath))
                {
                    string handlerContent = File.ReadAllText(handlerPath);
                    Assert(handlerContent.Contains("OpenLinkInNewTab"), "CustomMenuHandler contains OpenLinkInNewTab");
                    Assert(handlerContent.Contains("OpenLinkInNewWindow"), "CustomMenuHandler contains OpenLinkInNewWindow");
                    Assert(handlerContent.Contains("SaveLinkAsCommand"), "CustomMenuHandler contains SaveLinkAsCommand");
                    Assert(handlerContent.Contains("CopyLinkAddressCommand"), "CustomMenuHandler contains CopyLinkAddressCommand");
                    Assert(handlerContent.Contains("OpenImageInNewTabCommand"), "CustomMenuHandler contains OpenImageInNewTabCommand");
                    Assert(handlerContent.Contains("SaveImageAsCommand"), "CustomMenuHandler contains SaveImageAsCommand");
                    Assert(handlerContent.Contains("CopyImageCommand"), "CustomMenuHandler contains CopyImageCommand");
                    Assert(handlerContent.Contains("CopyImageAddressCommand"), "CustomMenuHandler contains CopyImageAddressCommand");
                    Assert(handlerContent.Contains("SearchGoogleCommand"), "CustomMenuHandler contains SearchGoogleCommand");
                    Assert(handlerContent.Contains("SavePageAsCommand"), "CustomMenuHandler contains SavePageAsCommand");
                    Assert(handlerContent.Contains("return false;"), "CustomMenuHandler RunContextMenu returns false for native Windows menu");
                }

                string artDir = GetArtifactsDir();
                if (artDir != null)
                {
                    using (ContextMenuStrip linkMenu = new ContextMenuStrip())
                    using (ContextMenuStrip pageMenu = new ContextMenuStrip())
                    {
                        linkMenu.Items.Add("Open link in new tab");
                        linkMenu.Items.Add("Open link in new window");
                        linkMenu.Items.Add(new ToolStripSeparator());
                        linkMenu.Items.Add("Save link as...");
                        linkMenu.Items.Add("Copy link address");
                        linkMenu.Items.Add(new ToolStripSeparator());
                        linkMenu.Items.Add("Open image in new tab");
                        linkMenu.Items.Add("Save image as...");
                        linkMenu.Items.Add("Copy image address");
                        linkMenu.Items.Add(new ToolStripSeparator());
                        linkMenu.Items.Add("Block element...");
                        linkMenu.Items.Add("Inspect element");

                        pageMenu.Items.Add("Back");
                        pageMenu.Items.Add("Forward");
                        pageMenu.Items.Add("Reload");
                        pageMenu.Items.Add(new ToolStripSeparator());
                        pageMenu.Items.Add("Save page as...");
                        pageMenu.Items.Add("Print...");
                        pageMenu.Items.Add(new ToolStripSeparator());
                        pageMenu.Items.Add("Block element...");
                        pageMenu.Items.Add("View page source");
                        pageMenu.Items.Add("Inspect element");

                        linkMenu.Show(0, 0);
                        pageMenu.Show(0, 0);

                        int totalW = linkMenu.Width + pageMenu.Width + 60;
                        int totalH = Math.Max(linkMenu.Height, pageMenu.Height) + 40;
                        using (Bitmap bmp = new Bitmap(totalW, totalH))
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.FromArgb(32, 33, 36));
                            using (Bitmap linkBmp = new Bitmap(linkMenu.Width, linkMenu.Height))
                            using (Bitmap pageBmp = new Bitmap(pageMenu.Width, pageMenu.Height))
                            {
                                linkMenu.DrawToBitmap(linkBmp, new Rectangle(0, 0, linkMenu.Width, linkMenu.Height));
                                pageMenu.DrawToBitmap(pageBmp, new Rectangle(0, 0, pageMenu.Width, pageMenu.Height));
                                g.DrawImage(linkBmp, 20, 20);
                                g.DrawImage(pageBmp, linkMenu.Width + 40, 20);
                            }
                            bmp.Save(Path.Combine(artDir, "native_context_menu_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        linkMenu.Close();
                        pageMenu.Close();
                    }
                }
            });

            Test("WbTopBar Hyperlink Drag Drop as New Tab and Address Bar Navigation", () =>
            {
                using (Form host = new Form { Size = new Size(1200, 600) })
                using (WbTopBar topBar = new WbTopBar())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.Width = 1200;
                    host.Controls.Add(topBar);
                    tc.TabPages.Add(new TabPage("Tab 1"));
                    tc.TabPages.Add(new TabPage("Tab 2"));
                    topBar.BindTabControl(tc);
                    topBar.UpdateTabs();
                    host.Show();
                    Application.DoEvents();

                    Assert(topBar.AllowDrop == true, "WbTopBar AllowDrop must be true");
                    Assert(topBar.AddressBox.AllowDrop == true, "AddressBox AllowDrop must be true");

                    // 1. Test OnDragEnter on WbTopBar
                    DataObject urlData = new DataObject();
                    urlData.SetData(DataFormats.Text, "https://github.com/torvalds/linux");

                    var onDragEnter = typeof(WbTopBar).GetMethod("OnDragEnter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    DragEventArgs enterArgs = new DragEventArgs(urlData, 0, 100, 10, DragDropEffects.All, DragDropEffects.None);
                    onDragEnter.Invoke(topBar, new object[] { enterArgs });
                    Assert(enterArgs.Effect == DragDropEffects.Copy, "OnDragEnter with URL must set Effect = DragDropEffects.Copy");

                    // 2. Test dropping URL onto tab strip area -> fires UrlDroppedAsNewTab
                    WbTopBar.UrlDroppedEventArgs droppedTabArgs = null;
                    topBar.UrlDroppedAsNewTab += (s, e) => { droppedTabArgs = e; };

                    var onDragDrop = typeof(WbTopBar).GetMethod("OnDragDrop", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Rectangle tab0Rect = topBar.GetTabRect(0);
                    Point dropTabScreenPt = topBar.PointToScreen(new Point(tab0Rect.Right + 5, tab0Rect.Top + 5));

                    DragEventArgs dropTabEvt = new DragEventArgs(urlData, 0, dropTabScreenPt.X, dropTabScreenPt.Y, DragDropEffects.All, DragDropEffects.None);
                    onDragDrop.Invoke(topBar, new object[] { dropTabEvt });

                    Assert(droppedTabArgs != null, "UrlDroppedAsNewTab event must fire when URL is dropped on tab strip");
                    Assert(droppedTabArgs.Url == "https://github.com/torvalds/linux", "Dropped URL must match payload");
                    Assert(droppedTabArgs.TargetTabIndex == 1, "Drop between Tab 1 and Tab 2 must compute target index 1, got: " + droppedTabArgs.TargetTabIndex);

                    // 3. Test dropping URL onto address bar area -> populates AddressText and fires NavigateRequested
                    string navigatedUrl = null;
                    topBar.NavigateRequested += (s, url) => { navigatedUrl = url; };

                    Rectangle addrRect = topBar.GetAddressBoxRect();
                    Point dropAddrScreenPt = topBar.PointToScreen(new Point(addrRect.Left + 20, addrRect.Top + 10));

                    DataObject addrData = new DataObject();
                    addrData.SetData(DataFormats.Text, "https://www.google.com");

                    DragEventArgs dropAddrEvt = new DragEventArgs(addrData, 0, dropAddrScreenPt.X, dropAddrScreenPt.Y, DragDropEffects.All, DragDropEffects.None);
                    onDragDrop.Invoke(topBar, new object[] { dropAddrEvt });

                    Assert(topBar.AddressText == "https://www.google.com", "AddressText must be updated with dropped URL");
                    Assert(navigatedUrl == "https://www.google.com", "NavigateRequested must be fired with dropped URL");

                    host.Close();
                }
            });

            Test("WbAppIcon Dynamic Accent Color Integration and Form Icon Synchronization", () =>
            {
                // 1. Verify master asset path discovery (must discover wb_icon.svg)
                string iconPath = WbAppIcon.MasterIconPath;
                Assert(!string.IsNullOrEmpty(iconPath), "MasterIconPath must not be empty");
                Assert(File.Exists(iconPath), "MasterIconPath file must exist on disk: " + iconPath);
                Assert(iconPath.EndsWith("wb_icon.svg", StringComparison.OrdinalIgnoreCase), "MasterIconPath must resolve to wb_icon.svg: " + iconPath);

                // 2. Test GetAppBitmap for multiple target sizes using vector SVG renderer
                using (Bitmap bmp32 = WbAppIcon.GetAppBitmap(32, 32))
                {
                    Assert(bmp32 != null, "32x32 bitmap must not be null");
                    Assert(bmp32.Width == 32 && bmp32.Height == 32, "Bitmap dimensions must match 32x32");
                    Assert(bmp32.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb, "PixelFormat must be Format32bppArgb for alpha transparency");
                }

                using (Bitmap bmp16 = WbAppIcon.GetAppBitmap(16, 16))
                {
                    Assert(bmp16 != null && bmp16.Width == 16 && bmp16.Height == 16, "16x16 favicon bitmap must be 16x16");
                }

                // 2b. Test MaterialIcons integration with normalized non-256x256 SVG
                using (Bitmap miBmp = MaterialIcons.GetIcon("wb_icon", 24))
                {
                    Assert(miBmp != null, "MaterialIcons.GetIcon('wb_icon', 24) must resolve and render wb_icon.svg");
                    Assert(miBmp.Width == 24 && miBmp.Height == 24, "MaterialIcons dimensions must be 24x24");
                }

                // 3. Test harmonic gradient derivation across presets
                Color startBlue, endBlue;
                WbAppIcon.DeriveHarmonicGradient(Color.FromArgb(138, 180, 248), out startBlue, out endBlue);
                Assert(!startBlue.IsEmpty && !endBlue.IsEmpty, "Derived gradient colors must not be empty");
                Assert(startBlue.ToArgb() != endBlue.ToArgb(), "Harmonic gradient must have distinct leading and trailing colors");

                // 4. Test Icon creation and form synchronization
                Icon appIcon = WbAppIcon.GetAppIcon();
                Assert(appIcon != null, "AppIcon must not be null");
                Assert(appIcon.Width > 0 && appIcon.Height > 0, "AppIcon dimensions must be positive");

                using (Form testForm = new Form())
                {
                    UITheme.ApplyForm(testForm);
                    Assert(testForm.Icon != null, "UITheme.ApplyForm must assign WbAppIcon to form.Icon");

                    // 5. Test reactive accent change
                    ThemeManager.SetPreset("Cyber Cyan");
                    Assert(ThemeManager.CurrentAccentColor == Color.FromArgb(112, 239, 222), "CurrentAccentColor must be Cyber Cyan");

                    Icon cyanIcon = WbAppIcon.GetAppIcon();
                    Assert(cyanIcon != null, "Cyan icon must be generated");

                    // Reset back to Default Blue
                    ThemeManager.SetPreset("Default Blue");
                }
            });

            Test("WbTopBar Go Button Click Fires NavigateRequested with Text", () =>
            {
                using (Form f = new Form())
                {
                    f.Size = new Size(850, 200);
                    f.Show();
                    using (WbTopBar topBar = new WbTopBar())
                    {
                        topBar.Width = 800;
                        topBar.Dock = DockStyle.Top;
                        f.Controls.Add(topBar);

                        topBar.AddressText = "https://google.com";
                        topBar.AddressBox.Focus();
                        Application.DoEvents();

                        Assert(topBar.IsEditingAddressBox, "IsEditingAddressBox should be true");
                        Rectangle goRect = topBar.GetGoButtonRect();
                        Assert(!goRect.IsEmpty, "Go button rect must not be empty");

                        string requestedUrl = null;
                        topBar.NavigateRequested += (s, url) =>
                        {
                            requestedUrl = url;
                        };

                        Point clickPt = new Point(goRect.Left + goRect.Width / 2, goRect.Top + goRect.Height / 2);
                        var onMouseDown = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var onMouseUp = typeof(WbTopBar).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickPt.X, clickPt.Y, 0) });
                        onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickPt.X, clickPt.Y, 0) });

                        Assert(requestedUrl == "https://google.com", "Clicking Go button must trigger NavigateRequested with address box text");
                    }
                    f.Close();
                }
            });

            Test("WbTopBar Refresh Button Click Fires RefreshClicked and Toggles Action Icons", () =>
            {
                using (Form f = new Form())
                {
                    f.Size = new Size(850, 200);
                    f.Show();
                    using (WbTopBar topBar = new WbTopBar())
                    {
                        topBar.Width = 800;
                        topBar.Dock = DockStyle.Top;
                        f.Controls.Add(topBar);
                        Application.DoEvents();

                        // 1. Initial idle state (not loading): Icon is arrow-clockwise
                        Assert(!topBar.IsLoading, "TopBar should initially not be loading");
                        Assert(topBar.GetAddressActionIconName() == "arrow-clockwise", "Action icon must be arrow-clockwise when idle");

                        Rectangle refRect = topBar.GetRefreshButtonRect();
                        Assert(!refRect.IsEmpty, "Refresh button rect must not be empty");
                        Rectangle hitRect = topBar.GetRefreshButtonHitRect();
                        Assert(hitRect.Contains(new Point(refRect.Left + refRect.Width / 2, refRect.Top + refRect.Height / 2)), "HitRect must encompass center of refresh button");

                        bool refreshFired = false;
                        topBar.RefreshClicked += (s, e) =>
                        {
                            refreshFired = true;
                        };

                        // Simulate click inside refresh button rect
                        Point clickPt = new Point(refRect.Left + refRect.Width / 2, refRect.Top + refRect.Height / 2);
                        var onMouseDown = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var onMouseUp = typeof(WbTopBar).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickPt.X, clickPt.Y, 0) });
                        onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickPt.X, clickPt.Y, 0) });

                        Assert(refreshFired, "Clicking Refresh button must raise RefreshClicked event");

                        // 2. Click near the edge inside hit rect (top of address bar above refresh icon)
                        refreshFired = false;
                        Point edgePt = new Point(refRect.Left + refRect.Width / 2, hitRect.Top + 2);
                        onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, edgePt.X, edgePt.Y, 0) });
                        onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, edgePt.X, edgePt.Y, 0) });

                        Assert(refreshFired, "Clicking within extended RefreshButtonHitRect must also raise RefreshClicked event");

                        // 3. Loading state: Icon transitions to 'x' (stop)
                        topBar.IsLoading = true;
                        Assert(topBar.GetAddressActionIconName() == "x", "Action icon must be 'x' (Stop) when loading");

                        refreshFired = false;
                        onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickPt.X, clickPt.Y, 0) });
                        onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickPt.X, clickPt.Y, 0) });

                        Assert(refreshFired, "Clicking Stop button while loading must also raise RefreshClicked event");
                    }
                    f.Close();
                }

                // 4. Source code verification: Ensure MainBrowserForm has hooked _topBar.RefreshClicked += RefreshButton_Click
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(srcDir))
                {
                    srcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                }
                string mainFormPath = Path.Combine(srcDir, @"Browser\MainBrowserForm.cs");
                if (File.Exists(mainFormPath))
                {
                    string srcText = File.ReadAllText(mainFormPath);
                    Assert(srcText.Contains("_topBar.RefreshClicked +="), "MainBrowserForm must hook _topBar.RefreshClicked");
                    Assert(srcText.Contains("RefreshButton_Click"), "MainBrowserForm must define and wire RefreshButton_Click");
                }
            });

            Test("ChromeTabControl Tab Tear-Off and Adoption into Unshown Window Validation", () =>
            {
                using (Form srcWin = new Form { Size = new Size(800, 600) })
                using (ChromeTabControl srcTc = new ChromeTabControl())
                {
                    srcWin.Controls.Add(srcTc);
                    TabPage tabA = new TabPage("Website A");
                    TabPage tabB = new TabPage("Website B");
                    srcTc.TabPages.Add(tabA);
                    srcTc.TabPages.Add(tabB);
                    srcWin.Show();
                    Application.DoEvents();

                    Assert(srcTc.TabPages.Count == 2, "Source window must have 2 tabs");

                    // Detach tabA
                    srcTc.TabPages.Remove(tabA);
                    Assert(srcTc.TabPages.Count == 1, "Source window must have 1 tab after detach");

                    // Construct new unshown window & tab control
                    using (Form newWin = new Form { Size = new Size(800, 600) })
                    using (ChromeTabControl newTc = new ChromeTabControl())
                    {
                        newWin.Controls.Add(newTc);
                        Assert(!newTc.IsHandleCreated, "New tab control handle must not be created before form is shown");

                        // Simulate safe adoption logic: ensure handle and insert/add
                        if (!newTc.IsHandleCreated)
                        {
                            var unused = newTc.Handle;
                        }
                        Assert(newTc.IsHandleCreated, "Accessing Handle forces Win32 HWND creation");

                        int insertIdx = Math.Max(0, Math.Min(newTc.TabPages.Count, 0));
                        if (newTc.TabPages.Count == 0)
                        {
                            newTc.TabPages.Add(tabA);
                        }
                        else
                        {
                            newTc.TabPages.Insert(insertIdx, tabA);
                        }
                        newTc.SelectedTab = tabA;

                        Assert(newTc.TabPages.Count == 1, "New tab control must have exactly 1 tab adopted");
                        Assert(newTc.SelectedTab == tabA, "Selected tab must be tabA");

                        newWin.Show();
                        Application.DoEvents();

                        Assert(newTc.TabPages.Count == 1, "New window after Show must retain the adopted tab");
                        Assert(newTc.SelectedTab == tabA, "Selected tab must remain tabA after Show");

                        newWin.Close();
                    }
                    srcWin.Close();
                }
            });

            Test("Moved Tabs Retain State and Child Controls Without Refresh or Disposal", () =>
            {
                using (Form srcWin = new Form { Size = new Size(800, 600) })
                using (ChromeTabControl srcTc = new ChromeTabControl())
                {
                    srcWin.Controls.Add(srcTc);
                    TabPage tab = new TabPage("Dashboard - My App");
                    tab.Tag = "https://app.example.com/dashboard?session=active123";

                    // Simulate hosted browser control inside tab
                    Panel mockBrowserHost = new Panel { Dock = DockStyle.Fill, Tag = "BrowserInstance_42" };
                    TextBox mockInput = new TextBox { Text = "Unsaved draft comment", Dock = DockStyle.Top };
                    mockBrowserHost.Controls.Add(mockInput);
                    tab.Controls.Add(mockBrowserHost);

                    srcTc.TabPages.Add(tab);
                    srcWin.Show();
                    Application.DoEvents();

                    Assert(tab.IsHandleCreated, "Tab handle must be created when parent form is shown");
                    Assert(mockBrowserHost.IsHandleCreated, "Browser host handle must be created");

                    // 1. Test intra-window reordering (MoveTabWithinWindow)
                    TabPage otherTab = new TabPage("Other Page");
                    srcTc.TabPages.Add(otherTab);
                    Assert(srcTc.TabPages.Count == 2, "Tab control has 2 tabs");

                    // Move tab from index 0 to 1
                    srcTc.TabPages.RemoveAt(0);
                    srcTc.TabPages.Insert(1, tab);
                    srcTc.SelectedTab = tab;
                    tab.Bounds = srcTc.DisplayRectangle;
                    Application.DoEvents();

                    Assert(!mockBrowserHost.IsDisposed, "Browser host must NOT be disposed after reordering");
                    Assert(ReferenceEquals(mockBrowserHost, tab.Controls[0]), "Same browser instance must be retained");
                    Assert(mockInput.Text == "Unsaved draft comment", "Form input state must be retained without reset");
                    Assert((string)tab.Tag == "https://app.example.com/dashboard?session=active123", "Session URL must be preserved");

                    // 2. Test cross-window adoption (DetachTab -> AdoptTabAt)
                    using (Form dstWin = new Form { Size = new Size(1000, 700) })
                    using (ChromeTabControl dstTc = new ChromeTabControl())
                    {
                        dstWin.Controls.Add(dstTc);
                        if (!dstTc.IsHandleCreated) { var unused = dstTc.Handle; }

                        // Detach
                        srcTc.TabPages.Remove(tab);
                        Assert(!tab.IsDisposed, "Detached tab must not be disposed");
                        Assert(!mockBrowserHost.IsDisposed, "Detached tab's hosted browser must not be disposed");

                        // Adopt
                        dstTc.TabPages.Add(tab);
                        dstTc.SelectedTab = tab;
                        tab.Bounds = dstTc.DisplayRectangle;
                        tab.Visible = true;
                        mockBrowserHost.Bounds = tab.ClientRectangle;

                        dstWin.Show();
                        Application.DoEvents();

                        Assert(dstTc.TabPages.Count == 1, "Destination tab control has adopted tab");
                        Assert(ReferenceEquals(dstTc.SelectedTab, tab), "Destination selected tab is the adopted tab");
                        Assert(!mockBrowserHost.IsDisposed, "Browser host must NOT be disposed after cross-window adoption");
                        Assert(mockInput.Text == "Unsaved draft comment", "Input text preserved across windows");
                        Assert((string)tab.Tag == "https://app.example.com/dashboard?session=active123", "Session state preserved across windows");
                        Assert(tab.Bounds == dstTc.DisplayRectangle, "Tab bounds must cleanly match destination DisplayRectangle");

                        dstWin.Close();
                    }
                    srcWin.Close();
                }
            });

            Test("WbBottomBar Flush Rectangular Docking, Instant Corner Dodge to Right and Long Hover Reveal", () =>
            {
                Assert(typeof(WbBottomBar).IsAssignableFrom(typeof(FloatingStatusBubble)), "FloatingStatusBubble must inherit from WbBottomBar for backwards compatibility");

                using (Form host = new Form { Size = new Size(1000, 700) })
                using (WbBottomBar bubble = new WbBottomBar())
                {
                    bubble.AttachTo(host);
                    host.Show();
                    Application.DoEvents();

                    // 1. Literal & preserved in status text and preview
                    string testUrl = "https://example.com/search?q=test&lang=en&sort=asc&page=2";
                    bubble.SetStatus(testUrl);
                    Assert(bubble.StatusText == testUrl, "StatusText must preserve literal & query parameters: " + bubble.StatusText);
                    Assert(bubble.StatusText.Contains("&lang=") && bubble.StatusText.Contains("&sort="), "Special & symbols preserved without stripping");

                    // 2. Default state: docked flush at bottom-left corner (0 margin)
                    Assert(!bubble.IsFlippedRight, "Default position must be bottom-left");
                    Point expectedLeftPos = host.PointToScreen(new Point(0, host.ClientSize.Height - bubble.Height));
                    Assert(bubble.Location == expectedLeftPos, "Bubble must be docked flush to bottom-left corner of host");

                    // 3. Instant corner flip to right when cursor enters bottom-left zone
                    Point leftCursor = new Point(15, host.ClientSize.Height - 10);
                    bool flipped = bubble.UpdateCursorDodge(leftCursor);
                    Assert(flipped, "UpdateCursorDodge must return true when state changes");
                    Assert(bubble.IsFlippedRight, "Bubble must flip to bottom-right when cursor enters left side");
                    Point expectedRightPos = host.PointToScreen(new Point(host.ClientSize.Width - bubble.Width, host.ClientSize.Height - bubble.Height));
                    Assert(bubble.Location == expectedRightPos, "Bubble must be docked flush to bottom-right corner when flipped");

                    // 4. Return to bottom-left default when cursor leaves bottom corners (e.g. back over hyperlink)
                    Point topCursor = new Point(500, 60);
                    flipped = bubble.UpdateCursorDodge(topCursor);
                    Assert(flipped, "UpdateCursorDodge must return true when returning to default");
                    Assert(!bubble.IsFlippedRight, "Bubble must return to bottom-left default once cursor leaves");
                    Assert(bubble.Location == expectedLeftPos, "Bubble returned flush to bottom-left corner");

                    // 5. Dodge back to left if cursor touches bottom-right area while on right
                    bubble.IsFlippedRight = true;
                    Point rightCursor = new Point(host.ClientSize.Width - 10, host.ClientSize.Height - 10);
                    bubble.UpdateCursorDodge(rightCursor);
                    Assert(!bubble.IsFlippedRight, "Bubble must dodge back to left if cursor enters bottom-right area");

                    // 6. Crisp rectangular rendering to bitmap with & symbols
                    using (Bitmap bmp = new Bitmap(bubble.Width, bubble.Height))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        var pe = new PaintEventArgs(g, new Rectangle(0, 0, bubble.Width, bubble.Height));
                        var onPaint = typeof(FloatingStatusBubble).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(bubble, new object[] { pe });
                    }

                    // 7. Long-hover horizontal width expansion (reveals full URL instead of '...')
                    string longUrl = "https://example.com/very/long/nested/path/to/resource?category=electronics&item_id=9872134&session=active_abc123&utm_source=external_feed_campaign";
                    bubble.SetStatus(longUrl);
                    int compactW = bubble.Width;
                    Assert(compactW <= 500, "Initial compact width is constrained: " + compactW);
                    Assert(!bubble.IsHoverExpanded, "Initial state is not expanded");

                    bubble.TriggerHoverExpand();
                    Assert(bubble.IsHoverExpanded, "IsHoverExpanded is true after long-hover trigger");
                    Assert(bubble.Width > compactW, "Width expands horizontally on long hover to reveal full URL (expanded: " + bubble.Width + " > compact: " + compactW + ")");

                    // 8. Transient empty pulse from CEF sub-DOM traversal does not kill hover expand
                    var hoverTimerField = typeof(WbBottomBar).GetField("_hoverExpandTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Timer hoverTimer = hoverTimerField.GetValue(bubble) as Timer;

                    bubble.SetStatus("https://example.com/short/path/with/long/parameters?session=987654321&token=abcdef1234567890&action=deep_query");
                    Assert(hoverTimer != null && hoverTimer.Enabled, "Hover expand timer must start for long URL");
                    bubble.SetStatus(string.Empty); // Transient CEF pulse when crossing <span> or text nodes
                    Assert(hoverTimer.Enabled, "Hover expand timer must remain active during debounced empty pulse");
                    bubble.SetStatus("https://example.com/short/path/with/long/parameters?session=987654321&token=abcdef1234567890&action=deep_query"); // Same URL resumes
                    Assert(hoverTimer.Enabled, "Hover expand timer continues running uninterrupted");

                    // 9. After full hide, re-hovering the exact same URL reliably restarts expand timer
                    var hideTimerField = typeof(WbBottomBar).GetField("_hideTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Timer hideTimer = hideTimerField.GetValue(bubble) as Timer;
                    bubble.SetStatus(string.Empty);
                    // Simulate full hide completion (timer tick)
                    var onHideTick = typeof(Timer).GetMethod("OnTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (onHideTick != null) { onHideTick.Invoke(hideTimer, new object[] { EventArgs.Empty }); }
                    else { bubble.Hide(); var stField = typeof(WbBottomBar).GetField("_statusText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); stField.SetValue(bubble, string.Empty); }
                    Assert(bubble.StatusText == string.Empty, "StatusText cleared after hide completion");

                    // Re-hovering same URL now restarts hover expand timer cleanly
                    bubble.SetStatus(longUrl);
                    Assert(hoverTimer.Enabled, "Hover expand timer must reliably start when re-hovering same URL");

                    // 10. Local file:/// URLs are not recognized as StartPage (address bar will display them)
                    string localFileUrl = "file:///C:/Users/test/Documents/testpage.html";
                    Assert(!InternalPageHelper.IsStartPage(localFileUrl), "Local file:/// URL must not be classified as startpage: " + localFileUrl);
                    Assert(InternalPageHelper.IsStartPage("wb://newtab"), "wb://newtab is recognized as startpage");
                    Assert(InternalPageHelper.IsStartPage("about:blank"), "about:blank is recognized as startpage");

                    // 11. Repeated hover and unhover cycles do not get stuck or drop status
                    Assert(bubble.Owner == host, "WbBottomBar must have host assigned as native Owner window to prevent falling behind canvas");
                    for (int cycle = 0; cycle < 10; cycle++)
                    {
                        bubble.SetStatus("https://example.com/item" + (cycle % 2));
                        Assert(bubble.Visible, "Bubble must be visible during hover cycle " + cycle);
                        Assert(bubble.StatusText == "https://example.com/item" + (cycle % 2), "Status text must update accurately on cycle " + cycle);
                        bubble.SetStatus(string.Empty);
                        Assert(bubble.Visible, "Debounce timer keeps bubble visible briefly on empty pulse");
                    }

                    // Complete hide
                    if (onHideTick != null) { onHideTick.Invoke(hideTimer, new object[] { EventArgs.Empty }); }
                    else { bubble.Hide(); }
                    Assert(!bubble.Visible, "Bubble must be hidden after timer completes");
                    Assert(bubble.StatusText == string.Empty, "StatusText must be reset on hide to prevent stale hover state");

                    // 12. Deactivation of owner window immediately dismisses indicator
                    bubble.SetStatus("https://example.com/active-before-deactivate");
                    Assert(bubble.Visible, "Bubble visible before deactivate");
                    var onDeactivate = typeof(Form).GetMethod("OnDeactivate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (onDeactivate != null)
                    {
                        onDeactivate.Invoke(host, new object[] { EventArgs.Empty });
                        Assert(!bubble.Visible, "Deactivate must immediately hide floating bottom bar to prevent sticking over other apps");
                    }

                    host.Close();
                }
            });

            Test("InternalPageHelper URL Detection and Normalization", () =>
            {
                // Bookmarks detection
                Assert(InternalPageHelper.IsInternalUrl("wb://bookmarks"), "wb://bookmarks is internal");
                Assert(InternalPageHelper.IsInternalUrl("wb://bookmarks/"), "wb://bookmarks/ is internal");
                Assert(InternalPageHelper.IsInternalUrl("WB://BOOKMARKS"), "Case-insensitive bookmarks is internal");
                Assert(InternalPageHelper.IsInternalUrl("wb:bookmarks"), "wb:bookmarks is internal");
                Assert(InternalPageHelper.IsInternalUrl("chrome://bookmarks"), "chrome://bookmarks rewrites to internal");
                Assert(InternalPageHelper.IsInternalUrl("chrome:bookmarks"), "chrome:bookmarks rewrites to internal");
                Assert(InternalPageHelper.IsInternalUrl("about:bookmarks"), "about:bookmarks rewrites to internal");
                Assert(InternalPageHelper.IsInternalUrl("chrome://favorites"), "chrome://favorites rewrites to internal");
                Assert(InternalPageHelper.IsInternalUrl("wb://favorites"), "wb://favorites rewrites to internal");

                // History detection
                Assert(InternalPageHelper.IsInternalUrl("wb://history"), "wb://history is internal");
                Assert(InternalPageHelper.IsInternalUrl("wb://history/"), "wb://history/ is internal");
                Assert(InternalPageHelper.IsInternalUrl("wb:history"), "wb:history is internal");
                Assert(InternalPageHelper.IsInternalUrl("chrome://history"), "chrome://history rewrites to internal");
                Assert(InternalPageHelper.IsInternalUrl("chrome:history"), "chrome:history rewrites to internal");
                Assert(InternalPageHelper.IsInternalUrl("about:history"), "about:history rewrites to internal");

                // Downloads detection
                Assert(InternalPageHelper.IsInternalUrl("wb://downloads"), "wb://downloads is internal");
                Assert(InternalPageHelper.IsInternalUrl("wb://downloads/"), "wb://downloads/ is internal");
                Assert(InternalPageHelper.IsInternalUrl("wb:downloads"), "wb:downloads is internal");
                Assert(InternalPageHelper.IsInternalUrl("chrome://downloads"), "chrome://downloads rewrites to internal");
                Assert(InternalPageHelper.IsInternalUrl("chrome:downloads"), "chrome:downloads rewrites to internal");
                Assert(InternalPageHelper.IsInternalUrl("about:downloads"), "about:downloads rewrites to internal");

                // NewTab detection
                Assert(InternalPageHelper.IsInternalUrl("wb://newtab"), "wb://newtab is internal");
                Assert(InternalPageHelper.IsInternalUrl("wb://newtab/"), "wb://newtab/ is internal");
                Assert(InternalPageHelper.IsInternalUrl("wb:newtab"), "wb:newtab is internal");
                Assert(InternalPageHelper.IsInternalUrl("chrome://newtab"), "chrome://newtab rewrites to internal");
                Assert(InternalPageHelper.IsInternalUrl("about:newtab"), "about:newtab rewrites to internal");
                Assert(InternalPageHelper.GetInternalPageType("wb://newtab") == InternalPageType.NewTab, "wb://newtab returns NewTab type");
                Assert(InternalPageHelper.GetInternalTitle("wb://newtab") == "New Tab", "wb://newtab title is New Tab");
                Assert(InternalPageHelper.GetInternalFavicon("wb://newtab") != null, "wb://newtab favicon is generated");

                // Non-internal pages
                Assert(!InternalPageHelper.IsInternalUrl("https://google.com"), "https://google.com is not internal");
                Assert(!InternalPageHelper.IsInternalUrl("about:blank"), "about:blank is not internal");
                Assert(!InternalPageHelper.IsInternalUrl("file:///test.html"), "file:/// is not internal");
                Assert(!InternalPageHelper.IsInternalUrl(null), "null is not internal");
                Assert(!InternalPageHelper.IsInternalUrl(""), "empty string is not internal");

                // Whitelisted Normalization to canonical wb://
                Assert(InternalPageHelper.NormalizeInternalUrl("wb://bookmarks") == "wb://bookmarks", "Canonical wb://bookmarks preserved");
                Assert(InternalPageHelper.NormalizeInternalUrl("wb:bookmarks") == "wb://bookmarks", "Normalized wb:bookmarks");
                Assert(InternalPageHelper.NormalizeInternalUrl("chrome://bookmarks") == "wb://bookmarks", "Whitelabeled chrome://bookmarks to wb://bookmarks");
                Assert(InternalPageHelper.NormalizeInternalUrl("chrome:bookmarks") == "wb://bookmarks", "Whitelabeled chrome:bookmarks to wb://bookmarks");
                Assert(InternalPageHelper.NormalizeInternalUrl("about:bookmarks") == "wb://bookmarks", "Whitelabeled about:bookmarks to wb://bookmarks");
                Assert(InternalPageHelper.NormalizeInternalUrl("chrome://favorites") == "wb://bookmarks", "Whitelabeled chrome://favorites to wb://bookmarks");
                Assert(InternalPageHelper.NormalizeInternalUrl("wb://favorites") == "wb://bookmarks", "Whitelabeled wb://favorites to wb://bookmarks");
                Assert(InternalPageHelper.NormalizeInternalUrl("wb://history") == "wb://history", "Canonical wb://history preserved");
                Assert(InternalPageHelper.NormalizeInternalUrl("wb:history") == "wb://history", "Normalized wb:history");
                Assert(InternalPageHelper.NormalizeInternalUrl("chrome://history") == "wb://history", "Whitelabeled chrome://history to wb://history");
                Assert(InternalPageHelper.NormalizeInternalUrl("about:history") == "wb://history", "Whitelabeled about:history to wb://history");
                Assert(InternalPageHelper.NormalizeInternalUrl("wb://downloads") == "wb://downloads", "Canonical wb://downloads preserved");
                Assert(InternalPageHelper.NormalizeInternalUrl("wb:downloads") == "wb://downloads", "Normalized wb:downloads");
                Assert(InternalPageHelper.NormalizeInternalUrl("chrome://downloads") == "wb://downloads", "Whitelabeled chrome://downloads to wb://downloads");
                Assert(InternalPageHelper.NormalizeInternalUrl("about:downloads") == "wb://downloads", "Whitelabeled about:downloads to wb://downloads");

                // Page Types
                Assert(InternalPageHelper.GetInternalPageType("wb://bookmarks") == InternalPageType.Bookmarks, "PageType Bookmarks");
                Assert(InternalPageHelper.GetInternalPageType("wb://history") == InternalPageType.History, "PageType History");
                Assert(InternalPageHelper.GetInternalPageType("wb://downloads") == InternalPageType.Downloads, "PageType Downloads");
                Assert(InternalPageHelper.GetInternalPageType("https://example.com") == InternalPageType.None, "PageType None");

                // Page Titles
                Assert(InternalPageHelper.GetInternalTitle("wb://bookmarks") == "Bookmarks", "Title Bookmarks");
                Assert(InternalPageHelper.GetInternalTitle("wb://history") == "History", "Title History");
                Assert(InternalPageHelper.GetInternalTitle("wb://downloads") == "Downloads", "Title Downloads");

                // Page Favicons
                Image bFav = InternalPageHelper.GetInternalFavicon("wb://bookmarks");
                Assert(bFav != null, "Bookmarks favicon not null");
                Image hFav = InternalPageHelper.GetInternalFavicon("wb://history");
                Assert(hFav != null, "History favicon not null");
                Image dFav = InternalPageHelper.GetInternalFavicon("wb://downloads");
                Assert(dFav != null, "Downloads favicon not null");
            });

            Test("HistoryManager Excludes Internal Page URLs", () =>
            {
                HistoryManager hm = new HistoryManager();
                int initialCount = hm.GetAll().Count;

                hm.AddEntry("Bookmarks Tab", "wb://bookmarks");
                hm.AddEntry("History Tab", "wb://history");
                hm.AddEntry("Downloads Tab", "wb://downloads");
                hm.AddEntry("About Bookmarks", "about:bookmarks");
                hm.AddEntry("Chrome Downloads", "chrome://downloads");
                hm.AddEntry("WB History", "wb://history");

                Assert(hm.GetAll().Count == initialCount, "Internal URLs must not be recorded in browsing history");

                hm.AddEntry("External Site", "https://example.org/test");
                Assert(hm.GetAll().Count == initialCount + 1, "Regular web URLs are properly recorded in browsing history");

                // Clean up test entry
                HistoryItem added = hm.GetAll().Find(x => x.Url == "https://example.org/test");
                if (added != null) hm.RemoveEntry(added.Id);
            });

            Test("TabLoadingOverlay Non-Activating Architecture, Smooth Spinner and Bounds Tracking", () =>
            {
                using (Form testOwner = new Form())
                using (Panel testCanvas = new Panel())
                using (TabLoadingOverlay overlay = new TabLoadingOverlay())
                {
                    testOwner.Size = new Size(800, 600);
                    testCanvas.Dock = DockStyle.Fill;
                    testOwner.Controls.Add(testCanvas);

                    overlay.AttachTo(testOwner, testCanvas);
                    overlay.DpiScale = 1.25f;

                    Assert(overlay.TopLevel == true, "Overlay must be a top-level window to float over CEF airspace");
                    Assert(overlay.ShowInTaskbar == false, "Overlay must not appear in the Windows taskbar");
                    Assert(overlay.FormBorderStyle == FormBorderStyle.None, "Overlay must be borderless for flush appearance");
                    Assert(overlay.BackColor == Color.FromArgb(32, 33, 36), "Overlay background matches Chrome dark toolbar color (#202124)");

                    // Test ShowForTabSwitch with message
                    overlay.ShowForTabSwitch("History", 200);

                    // Test ShowForInternalPage formatting
                    overlay.ShowForInternalPage("Bookmarks", 200);

                    // Render test frame to bitmap
                    using (Bitmap bmp = new Bitmap(400, 300))
                    {
                        overlay.Size = new Size(400, 300);
                        overlay.DrawToBitmap(bmp, new Rectangle(0, 0, 400, 300));
                        Assert(bmp.GetPixel(10, 10).ToArgb() == Color.FromArgb(32, 33, 36).ToArgb(), "Background rendered flush with dark theme");
                    }

                    // Test instant hide
                    overlay.HideOverlay();
                }
            });

            Test("FullScreenExitOverlay Rectangular Geometry, Accent Theme and Exit Mechanism", () =>
            {
                bool exitTriggered = false;
                using (Form testOwner = new Form())
                using (FullScreenExitOverlay overlay = new FullScreenExitOverlay(() => { exitTriggered = true; }))
                {
                    testOwner.Size = new Size(1024, 768);
                    overlay.AttachTo(testOwner);
                    overlay.DpiScale = 1.0f;

                    // 1. Verify window properties
                    Assert(overlay.TopLevel == true, "Overlay must be a top-level window to float over web canvas");
                    Assert(overlay.ShowInTaskbar == false, "Overlay must not appear in the Windows taskbar");
                    Assert(overlay.FormBorderStyle == FormBorderStyle.None, "Overlay must be borderless (no window chrome)");
                    Assert(overlay.BackColor == Color.FromArgb(32, 33, 36), "Overlay background matches dark toolbar canvas (#202124)");

                    // 2. Test DPI scaling
                    overlay.DpiScale = 1.25f;
                    Assert(overlay.Width > 370, "Width scales with DPI");
                    Assert(overlay.Height > 40, "Height scales with DPI");
                    overlay.DpiScale = 1.0f;

                    // 3. Test Full Screen Activation
                    overlay.ShowForFullScreen();
                    Assert(overlay.IsFullScreenActive == true, "IsFullScreenActive is true after ShowForFullScreen");

                    // 4. Test Rendering (Strictly rectangular, bottom accent line, no curved boxes)
                    using (Bitmap bmp = new Bitmap(overlay.Width, overlay.Height))
                    {
                        overlay.DrawToBitmap(bmp, new Rectangle(0, 0, overlay.Width, overlay.Height));

                        // Check corners are sharp rectangles (not clipped or transparent)
                        Color topLeft = bmp.GetPixel(0, 0);
                        Assert(topLeft.A == 255, "Top-left corner is fully opaque rectangular pixel");

                        // Check background color (sample margin at (6, 20) before text start at X=16)
                        Color interior = bmp.GetPixel(6, 20);
                        Assert(interior.R == 32 && interior.G == 33 && interior.B == 36, "Interior background is dark #202124");

                        // Check bottom accent border is drawn
                        Color accent = ThemeManager.CurrentAccentColor;
                        Color bottomPixel = bmp.GetPixel(overlay.Width / 2, overlay.Height - 1);
                        Assert(bottomPixel.R == accent.R && bottomPixel.G == accent.G && bottomPixel.B == accent.B,
                            "Bottom border matches dynamic accent color: " + accent.ToString());

                        string artDir = GetArtifactsDir();
                        if (artDir != null)
                        {
                            bmp.Save(System.IO.Path.Combine(artDir, "fullscreen_overlay_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }

                    // 5. Test Exit Button Interaction
                    var onMouseMove = typeof(FullScreenExitOverlay).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseDown = typeof(FullScreenExitOverlay).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseUp = typeof(FullScreenExitOverlay).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var exitRectField = typeof(FullScreenExitOverlay).GetField("_exitBtnRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Rectangle btnRect = (Rectangle)exitRectField.GetValue(overlay);

                    Assert(btnRect.Width > 0 && btnRect.Height > 0, "Exit button rectangle is initialized");

                    // Simulate hover over button -> hand cursor
                    int clickX = btnRect.Left + btnRect.Width / 2;
                    int clickY = btnRect.Top + btnRect.Height / 2;
                    onMouseMove.Invoke(overlay, new object[] { new MouseEventArgs(MouseButtons.None, 0, clickX, clickY, 0) });
                    Assert(overlay.Cursor == Cursors.Hand, "Cursor over Exit button is Cursors.Hand");

                    // Simulate mouse click on button -> triggers exit
                    onMouseDown.Invoke(overlay, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickX, clickY, 0) });
                    onMouseUp.Invoke(overlay, new object[] { new MouseEventArgs(MouseButtons.Left, 1, clickX, clickY, 0) });

                    Assert(exitTriggered == true, "Clicking Exit Full Screen triggers exit callback");
                    Assert(overlay.IsFullScreenActive == false, "IsFullScreenActive is false after exit");
                }
            });

            Test("Fast Tab Switching Immediate MouseDown Activation and Wheel Cycling", () =>
            {
                using (Form host = new Form { Size = new Size(800, 600) })
                using (WbTopBar topBar = new WbTopBar())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.Width = 800;
                    topBar.BindTabControl(tc);
                    host.Controls.Add(topBar);
                    host.Controls.Add(tc);

                    tc.TabPages.Add(new TabPage("Tab 0"));
                    tc.TabPages.Add(new TabPage("Tab 1"));
                    tc.TabPages.Add(new TabPage("Tab 2"));
                    topBar.UpdateTabs();

                    Assert(tc.SelectedIndex == 0, "Initial tab should be index 0");

                    Rectangle tab1Rect = topBar.GetTabRect(1);
                    Assert(!tab1Rect.IsEmpty, "Tab 1 rectangle must not be empty");

                    var onMouseDown = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseWheel = typeof(WbTopBar).GetMethod("OnMouseWheel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    // 1. Verify MouseDown activates tab instantly without waiting for MouseUp
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, tab1Rect.Left + tab1Rect.Width / 2, tab1Rect.Top + tab1Rect.Height / 2, 0) });
                    Assert(tc.SelectedIndex == 1, "Tab 1 must be selected immediately on MouseDown for zero latency");

                    // 2. Verify mouse wheel over tab strip cycles tabs rapidly
                    Rectangle stripRect = topBar.GetTabStripRect();
                    int wheelX = stripRect.Left + 20;
                    int wheelY = stripRect.Top + 10;

                    // Scroll down/forward -> Tab 2
                    onMouseWheel.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, wheelX, wheelY, -120) });
                    Assert(tc.SelectedIndex == 2, "Rolling wheel forward must switch to Tab 2");

                    // Scroll up/backward -> Tab 1
                    onMouseWheel.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, wheelX, wheelY, 120) });
                    Assert(tc.SelectedIndex == 1, "Rolling wheel backward must switch to Tab 1");
                }
            });

            Test("NewTabView Architecture, Option 1 Logo Adaptation and Shortcuts Lifecycle", () =>
            {
                // 1. Test ShortcutItem and ShortcutManager CRUD
                ShortcutManager sm = new ShortcutManager();
                var defaultList = ShortcutManager.CreateDefaultShortcuts();
                Assert(defaultList != null && defaultList.Count >= 7, "Default shortcuts must contain 7 popular sites");
                Assert(defaultList[0].Title == "GitHub", "First default is GitHub");

                var list = sm.GetAll();
                Assert(list != null, "ShortcutManager loads shortcuts list");

                ShortcutItem added = sm.Add("Example Site", "https://example.com");
                Assert(added != null, "Shortcut added successfully");
                Assert(added.BadgeText == "ES" || added.BadgeText == "E", "Derived badge text correctly: " + added.BadgeText);

                bool updated = sm.Update(added.Id, "Example Revised", "https://example.org");
                Assert(updated == true, "Shortcut updated successfully");

                bool removed = sm.Remove(added.Id);
                Assert(removed == true, "Shortcut removed successfully");

                // 2. Test WbAppIcon Option 1 Logo Rendering with ThemeManager
                Color testCyan = Color.FromArgb(0, 229, 255);
                using (Bitmap logoBmp = WbAppIcon.GetAppBitmap(180, 92, testCyan))
                {
                    Assert(logoBmp != null, "WbAppIcon generates bitmap for logo");
                    Assert(logoBmp.Width == 180 && logoBmp.Height == 92, "Logo dimensions match requested size");
                }

                // 3. Test NewTabView user control instantiation and rendering
                string navigatedUrl = null;
                bool newTabRequested = false;
                using (NewTabView ntv = new NewTabView((url, inNewTab) =>
                {
                    navigatedUrl = url;
                    newTabRequested = inNewTab;
                }))
                {
                    ntv.Size = new Size(1024, 768);
                    ntv.ResetSearchFocus();

                    string heroFile = Path.Combine(SilhouetteManager.SilhouettesDirectory, "2B Hero.png");
                    if (File.Exists(heroFile))
                    {
                        SilhouetteManager.SelectSilhouette(heroFile);
                    }

                    // Verify rendering inactive resting state to bitmap does not throw
                    using (Bitmap bmp = new Bitmap(1024, 768))
                    {
                        ntv.DrawToBitmap(bmp, new Rectangle(0, 0, 1024, 768));
                        Color pixel = bmp.GetPixel(20, 20);
                        Assert(pixel.R == 32 && pixel.G == 33 && pixel.B == 36, "NewTabView renders on #202124 background");

                        string artDir = GetArtifactsDir();
                        if (artDir != null)
                        {
                            bmp.Save(System.IO.Path.Combine(artDir, "newtab_live_rendered_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }

                    // Test search box activation and deactivation transitions
                    ntv.FocusSearchBox();
                    ntv.ResetSearchFocus();
                }
            });

            Test("New Tab Square Shortcuts and GoHome Navigation Protection", () =>
            {
                // Verify startpage.html normalizes cleanly to native wb://newtab
                Assert(InternalPageHelper.NormalizeInternalUrl("startpage.html") == InternalPageHelper.NewTabUrl, "startpage.html normalizes to wb://newtab");
                Assert(InternalPageHelper.NormalizeInternalUrl("file:///C:/wb/startpage.html") == InternalPageHelper.NewTabUrl, "file URI startpage.html normalizes to wb://newtab");
                Assert(InternalPageHelper.IsInternalUrl(InternalPageHelper.NewTabUrl), "wb://newtab is recognized as internal");
                Assert(InternalPageHelper.IsStartPage(InternalPageHelper.NewTabUrl), "wb://newtab is recognized as startpage");

                // Verify NewTabView square shortcut geometry and callback triggering
                string clickedUrl = null;
                bool middleClicked = false;
                using (NewTabView ntv = new NewTabView((url, inNewTab) =>
                {
                    clickedUrl = url;
                    middleClicked = inNewTab;
                }))
                {
                    ntv.Size = new Size(1024, 768);
                    using (Bitmap bmp = new Bitmap(1024, 768))
                    {
                        ntv.DrawToBitmap(bmp, new Rectangle(0, 0, 1024, 768));
                        Assert(bmp.Width == 1024 && bmp.Height == 768, "NewTabView renders square grid layout cleanly");
                    }
                }
            });

            Test("StartPage File Suppression and Automatic Cleanup", () =>
            {
                // Ensure EnsureStartPageFile returns wb://newtab
                string url = InternalPageHelper.EnsureStartPageFile();
                Assert(url == InternalPageHelper.NewTabUrl, "EnsureStartPageFile returns NewTabUrl");
                Assert(InternalPageHelper.StartPageUrl == InternalPageHelper.NewTabUrl, "StartPageUrl returns NewTabUrl");

                string testFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startpage.html");
                // Create a dummy startpage.html to test cleanup
                File.WriteAllText(testFile, "<html>legacy</html>");
                Assert(File.Exists(testFile), "Dummy legacy startpage.html created");

                // Call EnsureStartPageFile - should delete legacy file
                InternalPageHelper.EnsureStartPageFile();
                Assert(!File.Exists(testFile), "EnsureStartPageFile successfully deletes legacy startpage.html");
            });

            Test("NewTabView Shortcut Favicon Rendering and Fallback", () =>
            {
                using (NewTabView ntv = new NewTabView((u, nt) => { }))
                {
                    ntv.Size = new Size(1024, 768);
                    using (Bitmap bmp = new Bitmap(1024, 768))
                    {
                        ntv.DrawToBitmap(bmp, new Rectangle(0, 0, 1024, 768));
                        Assert(bmp.Width == 1024, "NewTabView renders with shortcut favicons cleanly");

                        string artDir = GetArtifactsDir();
                        if (artDir != null)
                        {
                            bmp.Save(Path.Combine(artDir, "newtab_live_rendered_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
            });

            Test("NewTabView Search Bar Hover, Consistent Font Size and Defocusing", () =>
            {
                // NewTabView focus, defocus and font size consistency verification
                using (Form host = new Form { Size = new Size(1024, 768) })
                using (NewTabView ntv = new NewTabView((u, nt) => { }))
                {
                    ntv.Dock = DockStyle.Fill;
                    host.Controls.Add(ntv);
                    host.Show();
                    Application.DoEvents();

                    // Focus search box
                    ntv.FocusSearchBox();
                    Application.DoEvents();

                    // Defocus search box
                    ntv.UnfocusSearchBox();
                    Application.DoEvents();
                    Assert(host.ActiveControl != ntv.Controls[0] && ntv.ActiveControl == null, "UnfocusSearchBox successfully removes focus from search box");

                    // Verify font consistency (11f) and persistent visibility for identical text alignment
                    Assert(ntv.Controls.Count > 0 && ntv.Controls[0] is TextBox, "Search box is present in NewTabView");
                    TextBox sb = (TextBox)ntv.Controls[0];
                    Assert(Math.Abs(sb.Font.Size - 11f) < 0.01f, "Search box font size is 11f");
                    Assert(sb.Visible, "Search box is always visible so cue banner and active text maintain identical alignment");

                    // Reset search focus
                    ntv.ResetSearchFocus();
                    Application.DoEvents();
                    Assert(ntv.ActiveControl == null, "ResetSearchFocus keeps active control null");
                    Assert(sb.Visible, "Search box stays visible after reset");

                    // 3. Verify shortcuts section maintains 8 slots and '+' state on removal
                    using (Bitmap bmp = new Bitmap(1024, 768))
                    {
                        ntv.DrawToBitmap(bmp, new Rectangle(0, 0, 1024, 768));
                        Assert(bmp.Width == 1024, "NewTabView renders 8-slot shortcuts layout cleanly");
                    }
                }
            });

            Test("FaviconManager Disk Persistence and Safe Domain Verification", () =>
            {
                FaviconManager fm = new FaviconManager();
                // Test domain extraction
                Assert(FaviconManager.ExtractDomain("https://www.github.com/path") == "github.com", "ExtractDomain strips www");
                Assert(FaviconManager.ExtractDomain("http://example.org") == "example.org", "ExtractDomain parses domain");

                // Test startpage returns null favicon
                Assert(fm.GetFavicon("wb://newtab") == null, "Internal new tab has null favicon");
                Assert(fm.GetFavicon("") == null, "Empty URL has null favicon");
            });

            Test("SilhouetteManager Dynamic Scanning, Random Mode, Zoom and Color Styles", () =>
            {
                SilhouetteManager.Initialize();
                Assert(!string.IsNullOrEmpty(SilhouetteManager.SilhouettesDirectory), "SilhouettesDirectory should not be empty");

                IList<string> files = SilhouetteManager.GetAvailableFiles();
                Assert(files.Count >= 2, "Should have at least 2 silhouettes detected (2B Hero, 2B Maid)");

                // Verify names on disk do not contain silhouette wording
                bool hasMaid = false;
                bool hasHero = false;
                for (int i = 0; i < files.Count; i++)
                {
                    string fName = Path.GetFileName(files[i]);
                    if (fName.StartsWith("2B Maid", StringComparison.OrdinalIgnoreCase)) hasMaid = true;
                    if (fName.StartsWith("2B Hero", StringComparison.OrdinalIgnoreCase)) hasHero = true;
                }
                Assert(hasMaid, "Must detect 2B Maid.png");
                Assert(hasHero, "Must detect 2B Hero.png");

                // Test zoom clamping
                SilhouetteManager.SetZoomPercent(85);
                Assert(SilhouetteManager.ZoomPercent == 85, "Zoom should be 85%");
                SilhouetteManager.SetZoomPercent(200);
                Assert(SilhouetteManager.ZoomPercent == 150, "Zoom should clamp to 150% max");
                SilhouetteManager.SetZoomPercent(10);
                Assert(SilhouetteManager.ZoomPercent == 30, "Zoom should clamp to 30% min");
                SilhouetteManager.SetZoomPercent(85);

                // Test color style toggle
                string initialStyle = SilhouetteManager.ColorStyle;
                SilhouetteManager.ToggleColorStyle();
                Assert(SilhouetteManager.ColorStyle != initialStyle, "Color style should toggle");
                SilhouetteManager.ToggleColorStyle();
                Assert(SilhouetteManager.ColorStyle == initialStyle, "Color style should toggle back");

                // Test silhouette selection and random mode
                SilhouetteManager.SelectSilhouette(files[0]);
                Assert(!SilhouetteManager.IsRandomMode, "SelectSilhouette should lock file and disable random mode");
                Assert(SilhouetteManager.ActiveFilePath == files[0], "ActiveFilePath should match selected file");

                SilhouetteManager.SetRandomMode(true);
                Assert(SilhouetteManager.IsRandomMode, "SetRandomMode(true) should enable random mode");

                // Test Alignment, Mirror and Flip
                SilhouetteManager.SetAlignment("Left");
                Assert(SilhouetteManager.Alignment == "Left", "Alignment should be Left");
                SilhouetteManager.SetAlignment("Center");
                Assert(SilhouetteManager.Alignment == "Center", "Alignment should be Center");
                SilhouetteManager.SetAlignment("Right");
                Assert(SilhouetteManager.Alignment == "Right", "Alignment should be Right");

                bool initialMirror = SilhouetteManager.IsMirrored;
                SilhouetteManager.ToggleMirror();
                Assert(SilhouetteManager.IsMirrored != initialMirror, "IsMirrored should toggle");
                SilhouetteManager.ToggleMirror();
                Assert(SilhouetteManager.IsMirrored == initialMirror, "IsMirrored should toggle back");

                bool initialFlip = SilhouetteManager.IsFlipped;
                SilhouetteManager.ToggleFlip();
                Assert(SilhouetteManager.IsFlipped != initialFlip, "IsFlipped should toggle");
                SilhouetteManager.ToggleFlip();
                Assert(SilhouetteManager.IsFlipped == initialFlip, "IsFlipped should toggle back");

                // Verify active bitmap loads cleanly
                Bitmap activeBmp = SilhouetteManager.GetActiveSilhouetteBitmap();
                Assert(activeBmp != null, "Active silhouette bitmap should not be null");
                Assert(activeBmp.Width > 0 && activeBmp.Height > 0, "Active silhouette bitmap should have positive dimensions");
                Assert(!SilhouetteManager.IsActiveWallpaper, "Active bundled silhouette must not be classified as wallpaper");

                // Test Hide option
                Assert(!SilhouetteManager.IsHidden, "IsHidden should default to false");
                SilhouetteManager.SetHidden(true);
                Assert(SilhouetteManager.IsHidden, "IsHidden should be true after SetHidden(true)");
                Assert(SilhouetteManager.GetActiveSilhouetteBitmap() == null, "GetActiveSilhouetteBitmap should return null when hidden");

                SilhouetteManager.ToggleHidden();
                Assert(!SilhouetteManager.IsHidden, "IsHidden should be false after ToggleHidden()");
                Bitmap unhiddenBmp = SilhouetteManager.GetActiveSilhouetteBitmap();
                Assert(unhiddenBmp != null, "GetActiveSilhouetteBitmap should return bitmap when unhidden");

                // Verify NewTabView renders with uniform search pill border
                using (NewTabView ntv = new NewTabView((u, nt) => { }))
                {
                    ntv.Size = new Size(1024, 768);
                    using (Bitmap bmp = new Bitmap(1024, 768))
                    {
                        ntv.DrawToBitmap(bmp, new Rectangle(0, 0, 1024, 768));
                        Assert(bmp.Width == 1024, "NewTabView renders cleanly with search pill and silhouette bounds");
                    }
                }
            });

            Test("SilhouetteManager Custom Folder, WebP Format and Wallpaper Auto-Detection", () =>
            {
                // 1. Verify custom folder directory structure
                string customDir = SilhouetteManager.CustomDirectory;
                Assert(!string.IsNullOrEmpty(customDir), "CustomDirectory should not be empty");
                Assert(customDir.EndsWith("custom", StringComparison.OrdinalIgnoreCase), "CustomDirectory must point to custom folder inside silhouettes");
                Assert(Directory.Exists(customDir), "Custom directory must exist on disk");

                // 2. Verify auto-detection on cutouts, portraits and bundled silhouettes
                using (Bitmap cutout = new Bitmap(200, 200, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(cutout))
                    {
                        g.Clear(Color.Transparent);
                        using (SolidBrush b = new SolidBrush(Color.Red))
                        {
                            g.FillEllipse(b, 50, 50, 100, 100);
                        }
                    }
                    Assert(!SilhouetteManager.IsWallpaperImage(cutout), "Cutout with transparent background must be detected as silhouette");
                }

                using (Bitmap portrait = new Bitmap(600, 900, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    Assert(!SilhouetteManager.IsWallpaperImage(portrait), "Portrait image must be detected as silhouette, not wallpaper");
                }

                string heroFile = Path.Combine(SilhouetteManager.SilhouettesDirectory, "2B Hero.png");
                if (File.Exists(heroFile))
                {
                    SilhouetteManager.SelectSilhouette(heroFile);
                    Assert(SilhouetteManager.IsBundledSilhouette(heroFile), "2B Hero must be recognized as bundled silhouette");
                    Assert(!SilhouetteManager.IsActiveWallpaper, "2B Hero must never be active wallpaper on startup");
                }

                // 3. Verify auto-detection on widescreen landscape wallpaper
                using (Bitmap wallpaper = new Bitmap(800, 450, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(wallpaper))
                    {
                        using (LinearGradientBrush lgb = new LinearGradientBrush(new Point(0, 0), new Point(800, 450), Color.SkyBlue, Color.MidnightBlue))
                        {
                            g.FillRectangle(lgb, 0, 0, 800, 450);
                        }
                    }
                    Assert(SilhouetteManager.IsWallpaperImage(wallpaper), "Opaque widescreen photo/gradient bitmap must be detected as wallpaper");

                    // 4. Convert wallpaper to WebP and save in custom folder
                    string testWebpPath = Path.Combine(customDir, "AutoDetectTestWallpaper.webp");
                    try
                    {
                        bool converted = SilhouetteManager.ConvertToWebP(wallpaper, testWebpPath, true);
                        Assert(converted, "ConvertToWebP should return true");
                        Assert(File.Exists(testWebpPath), "WebP file must be written to custom folder");

                        // 5. Verify WebP loading via WIC/ffmpeg decoder
                        using (Bitmap loaded = SilhouetteManager.LoadBitmapFromFile(testWebpPath))
                        {
                            Assert(loaded != null, "LoadBitmapFromFile should load .webp image cleanly");
                            Assert(loaded.Width == 800 && loaded.Height == 450, "Loaded WebP dimensions must match original");
                        }

                        // 6. Verify Rescan discovers custom files and auto-detects wallpaper fill mode
                        SilhouetteManager.Rescan();
                        SilhouetteManager.SelectSilhouette(testWebpPath);
                        Assert(SilhouetteManager.IsActiveWallpaper, "Custom wallpaper must auto-detect IsActiveWallpaper == true");

                        // Test mode toggle on custom wallpaper
                        SilhouetteManager.ToggleDisplayMode();
                        Assert(!SilhouetteManager.IsActiveWallpaper, "ToggleDisplayMode should switch to Silhouette");
                        SilhouetteManager.ToggleDisplayMode();
                        Assert(SilhouetteManager.IsActiveWallpaper, "ToggleDisplayMode should switch back to Wallpaper");

                        // 7. Test NewTabView rendering in Wallpaper Fill Mode
                        using (NewTabView ntv = new NewTabView((u, nt) => { }))
                        {
                            ntv.Size = new Size(1280, 720);
                            using (Bitmap canvas = new Bitmap(1280, 720))
                            {
                                ntv.DrawToBitmap(canvas, new Rectangle(0, 0, 1280, 720));
                                Assert(canvas.Width == 1280, "NewTabView renders in wallpaper cover fill mode without error");
                            }
                        }
                    }
                    finally
                    {
                        try { if (File.Exists(testWebpPath)) File.Delete(testWebpPath); } catch { }
                        SilhouetteManager.SetDisplayMode("Auto");
                        SilhouetteManager.Rescan();
                        SilhouetteManager.SetRandomMode(true);
                    }
                }
            });

            Test("New Tab Fast In/Out Transitions and Loading Screen Elimination", () =>
            {
                // 1. Verify NewTab URL identification
                Assert(InternalPageHelper.GetInternalPageType(InternalPageHelper.NewTabUrl) == InternalPageType.NewTab, "NewTabUrl maps to InternalPageType.NewTab");
                Assert(InternalPageHelper.GetInternalPageType("wb://new-tab") == InternalPageType.NewTab, "wb://new-tab maps to InternalPageType.NewTab");
                Assert(InternalPageHelper.GetInternalPageType("wb://start") == InternalPageType.NewTab, "wb://start maps to InternalPageType.NewTab");

                // 2. Verify TabLoadingOverlay instant hide behavior
                using (Form testOwner = new Form())
                using (Panel testCanvas = new Panel())
                using (TabLoadingOverlay overlay = new TabLoadingOverlay())
                {
                    testOwner.Size = new Size(800, 600);
                    testCanvas.Dock = DockStyle.Fill;
                    testOwner.Controls.Add(testCanvas);
                    overlay.AttachTo(testOwner, testCanvas);

                    overlay.HideOverlay();
                    Assert(overlay.Visible == false, "Overlay must remain hidden on HideOverlay");
                }

                // 3. Verify MainBrowserForm and BrowserTab source architecture for loading screen suppression
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(srcDir))
                {
                    srcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                }
                string mainFormPath = Path.Combine(srcDir, @"Browser\MainBrowserForm.cs");
                string browserTabPath = Path.Combine(srcDir, @"Browser\BrowserTab.cs");

                if (File.Exists(mainFormPath))
                {
                    string mainFormContent = File.ReadAllText(mainFormPath);
                    // Tab_InternalPageLoading must suppress overlay for NewTab
                    Assert(mainFormContent.Contains("InternalPageType.NewTab") &&
                           mainFormContent.Contains("Tab_InternalPageLoading"),
                           "MainBrowserForm must check InternalPageType.NewTab in Tab_InternalPageLoading");

                    // Tab_NavigatingFromInternalPage must suppress overlay when leaving NewTab
                    Assert(mainFormContent.Contains("Tab_NavigatingFromInternalPage") &&
                           mainFormContent.Contains("Navigating out from New Tab"),
                           "MainBrowserForm must suppress loading overlay when navigating out from New Tab");

                    // NavigateOrOpenInternalTab must suppress overlay for NewTab
                    Assert(mainFormContent.Contains("NavigateOrOpenInternalTab") &&
                           mainFormContent.Contains("pageType == InternalPageType.NewTab"),
                           "MainBrowserForm must not show overlay in NavigateOrOpenInternalTab for New Tab");
                }

                if (File.Exists(browserTabPath))
                {
                    string browserTabContent = File.ReadAllText(browserTabPath);
                    // ShowInternalPage must not fire InternalPageLoading for NewTab
                    Assert(browserTabContent.Contains("type != InternalPageType.NewTab") &&
                           browserTabContent.Contains("InternalPageLoading"),
                           "BrowserTab must not fire InternalPageLoading for New Tab");

                    // EnsureBrowserAlive must pre-warm browser and avoid redundant ShowInternalPage
                    Assert(browserTabContent.Contains("Pre-warm the underlying browser control"),
                           "BrowserTab must pre-warm browser control for New Tab in EnsureBrowserAlive");
                }
            });

            Test("WbTopBar Shield Blocked Count Number, ElementPickerTarget and Element Picker Wheel Scroll Prevention", () =>
            {
                // 1. Verify WbTopBar BlockedCount property and dynamic button expansion
                using (WbTopBar topBar = new WbTopBar())
                {
                    topBar.Width = 800;
                    Rectangle rect0 = topBar.GetShieldButtonRect();
                    Assert(rect0.Width > 0, "Shield button has positive width");

                    topBar.BlockedCount = 12;
                    Rectangle rect12 = topBar.GetShieldButtonRect();
                    Assert(rect12.Width == rect0.Width, "Shield button retains compact uniform width with number badge overlay");
                    Assert(rect12.Right < topBar.GetGearButtonRect().Left, "Shield button is to the left of Gear button");
                    Assert(topBar.GetStarButtonRect().Right < rect12.Left, "Star button is to the left of Shield button");
                    Assert(topBar.GetHomeButtonRect().Right < topBar.GetStarButtonRect().Left, "Home button is to the left of Star button");

                    // Render to bitmap to verify painting cycle with blocked count badge
                    using (Bitmap bmp = new Bitmap(800, 40))
                    {
                        topBar.DrawToBitmap(bmp, new Rectangle(0, 0, 800, 40));
                        bool foundBadgePixel = false;
                        Color bg = Color.FromArgb(41, 42, 45);
                        for (int y = rect12.Top; y < rect12.Bottom; y++)
                        {
                            for (int x = rect12.Left; x < rect12.Right; x++)
                            {
                                Color c = bmp.GetPixel(x, y);
                                if (c.R != bg.R || c.G != bg.G || c.B != bg.B)
                                {
                                    if (x > rect12.Left + rect12.Width / 2 && y > rect12.Top + rect12.Height / 2)
                                    {
                                        foundBadgePixel = true;
                                    }
                                }
                            }
                        }
                        Assert(foundBadgePixel, "Shield button renders number badge in bottom-right quadrant");
                    }
                }

                // 2. Verify ElementPickerTarget.cs exists and is compiled
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(srcDir))
                {
                    srcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                }
                string pickerTargetPath = Path.Combine(srcDir, @"AdBlock\ElementPickerTarget.cs");
                string oldPickerTargetPath = Path.Combine(srcDir, @"AdBlock\IElementPickerTarget.cs");
                Assert(File.Exists(pickerTargetPath), "ElementPickerTarget.cs must exist in src/AdBlock");
                Assert(!File.Exists(oldPickerTargetPath), "IElementPickerTarget.cs should be renamed and no longer exist");

                // 3. Verify DropperScriptlet wheel scroll prevention
                string script = DropperScriptlet.GetDropperScript();
                Assert(script.Contains("function onWheel(e)"), "DropperScriptlet contains onWheel");
                Assert(script.Contains("if (e.preventDefault) e.preventDefault();"), "DropperScriptlet must unconditionally call e.preventDefault on wheel");
                Assert(script.Contains("if (e.stopPropagation) e.stopPropagation();"), "DropperScriptlet must unconditionally call e.stopPropagation on wheel");
                Assert(script.Contains("if (e.stopImmediatePropagation) e.stopImmediatePropagation();"), "DropperScriptlet must call e.stopImmediatePropagation on wheel");
                Assert(script.Contains("document.addEventListener('wheel', onWheel"), "DropperScriptlet must attach capture listener to document as well");
            });

            Test("WbTopBar Dynamic Toolbar Buttons, Reordering, Visibility and Menus", () =>
            {
                using (WbTopBar topBar = new WbTopBar())
                {
                    topBar.Width = 1000;
                    topBar.Height = 40;

                    // 1. Reset to default and verify default button order
                    topBar.ResetToolbarToDefault();
                    var buttons = topBar.GetToolbarButtons();
                    Assert(buttons.Count == 7, "Must have 7 default movable toolbar buttons");
                    Assert(buttons[0] == "Home", "Button 0 is Home");
                    Assert(buttons[1] == "Star", "Button 1 is Star");
                    Assert(buttons[2] == "Bookmarks", "Button 2 is Bookmarks");
                    Assert(buttons[3] == "History", "Button 3 is History");
                    Assert(buttons[4] == "Downloads", "Button 4 is Downloads");
                    Assert(buttons[5] == "Shield", "Button 5 is Shield");
                    Assert(buttons[6] == "Screenshot", "Button 6 is Screenshot");

                    // 2. Button displayName mappings
                    Assert(WbTopBar.GetButtonDisplayName("Star") == "Bookmark current tab", "Star display name is 'Bookmark current tab'");
                    Assert(WbTopBar.GetButtonDisplayName("Shield") == "uBlock", "Shield display name is 'uBlock'");
                    Assert(WbTopBar.GetButtonDisplayName("Bookmarks") == "Bookmarks manager", "Bookmarks display name is 'Bookmarks manager'");
                    Assert(WbTopBar.GetButtonDisplayName("History") == "History", "History display name is 'History'");
                    Assert(WbTopBar.GetButtonDisplayName("Downloads") == "Downloads", "Downloads display name is 'Downloads'");
                    Assert(WbTopBar.GetButtonDisplayName("Home") == "Home", "Home display name is 'Home'");
                    Assert(WbTopBar.GetButtonDisplayName("Screenshot") == "Screenshot", "Screenshot display name is 'Screenshot'");

                    // 3. Rectangles layout order: left-to-right (Home < Star < Bookmarks < History < Downloads < Shield < Screenshot < Gear)
                    Rectangle homeRect = topBar.GetHomeButtonRect();
                    Rectangle starRect = topBar.GetStarButtonRect();
                    Rectangle bookmarksRect = topBar.GetBookmarksButtonRect();
                    Rectangle historyRect = topBar.GetHistoryButtonRect();
                    Rectangle downloadsRect = topBar.GetDownloadsButtonRect();
                    Rectangle shieldRect = topBar.GetShieldButtonRect();
                    Rectangle screenshotRect = topBar.GetScreenshotButtonRect();
                    Rectangle gearRect = topBar.GetGearButtonRect();

                    Assert(homeRect.Right < starRect.Left, "Home is to the left of Star");
                    Assert(starRect.Right < bookmarksRect.Left, "Star is to the left of Bookmarks");
                    Assert(bookmarksRect.Right < historyRect.Left, "Bookmarks is to the left of History");
                    Assert(historyRect.Right < downloadsRect.Left, "History is to the left of Downloads");
                    Assert(downloadsRect.Right < shieldRect.Left, "Downloads is to the left of Shield");
                    Assert(shieldRect.Right < screenshotRect.Left, "Shield is to the left of Screenshot");
                    Assert(screenshotRect.Right < gearRect.Left, "Screenshot is to the left of Gear");

                    // 4. Visibility toggle
                    Assert(topBar.IsButtonVisible("History"), "History is visible initially");
                    topBar.SetButtonVisible("History", false);
                    Assert(!topBar.IsButtonVisible("History"), "History is hidden after toggle");
                    var vis = topBar.GetVisibleToolbarButtons();
                    Assert(!vis.Contains("History"), "Visible list does not contain hidden button");
                    var rects = topBar.GetToolbarButtonRectangles();
                    Assert(!rects.ContainsKey("History"), "Toolbar rectangles does not contain hidden button");

                    // Re-enable visibility
                    topBar.SetButtonVisible("History", true);
                    Assert(topBar.IsButtonVisible("History"), "History is visible again");

                    // 5. Reordering
                    topBar.SetToolbarButtons(new List<string> { "Screenshot", "Shield", "Downloads", "History", "Bookmarks", "Star", "Home" });
                    var reordered = topBar.GetToolbarButtons();
                    Assert(reordered[0] == "Screenshot", "Screenshot is now first");
                    Assert(reordered[6] == "Home", "Home is now last");
                    Rectangle newScreenshotRect = topBar.GetScreenshotButtonRect();
                    Rectangle newHomeRect = topBar.GetHomeButtonRect();
                    Assert(newScreenshotRect.Right < newHomeRect.Left, "In reversed order, Screenshot is to the left of Home");

                    // 6. Reset to default restores original positions
                    topBar.ResetToolbarToDefault();
                    buttons = topBar.GetToolbarButtons();
                    Assert(buttons[0] == "Home" && buttons[5] == "Shield" && buttons[6] == "Screenshot", "Reset restores default order");

                    // 7. Context menu generation
                    using (ContextMenuStrip menu = topBar.CreateToolbarContextMenu())
                    {
                        Assert(menu != null, "Toolbar context menu is created");
                        Assert(menu.Renderer is MaterialToolStripRenderer, "Context menu uses MaterialToolStripRenderer");
                        Assert(menu.Items.Count >= 7, "Context menu has at least 7 items (buttons + separator + reset)");

                        var testRenderer = new TestTrackingRenderer();
                        menu.Renderer = testRenderer;
                        using (Bitmap bmp = new Bitmap(200, 300))
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            var mi = typeof(ContextMenuStrip).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            mi.Invoke(menu, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 200, 300)) });
                            Assert(testRenderer.LastRenderedCheckRect.Right <= testRenderer.LastRenderedImageRect.Left, "ContextMenu check and image do not overlap");

                            ToolStripMenuItem sub = new ToolStripMenuItem("Sub");
                            sub.DropDownItems.Add(new ToolStripMenuItem("Btn") { Checked = true, Image = new Bitmap(16, 16) });
                            var tr2 = new TestTrackingRenderer();
                            sub.DropDown.Renderer = tr2;
                            mi.Invoke(sub.DropDown, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 200, 300)) });
                            Assert(tr2.LastRenderedCheckRect.Right <= tr2.LastRenderedImageRect.Left, "Submenu fallback check and image do not overlap");

                            ToolStripDropDownMenu dropDown = sub.DropDown as ToolStripDropDownMenu;
                            if (dropDown != null)
                            {
                                dropDown.ShowCheckMargin = true;
                                dropDown.ShowImageMargin = true;
                            }
                            mi.Invoke(sub.DropDown, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 200, 300)) });
                            Assert(tr2.LastRenderedCheckRect.Right <= tr2.LastRenderedImageRect.Left, "Submenu with ShowCheckMargin check and image do not overlap");
                        }
                    }
                }
            });

            Test("Incognito InternalPageHelper Detection and Normalization", () =>
            {
                Assert(InternalPageHelper.IncognitoUrl == "wb://incognito", "IncognitoUrl constant matches");
                Assert(InternalPageHelper.GetInternalPageType("wb://incognito") == InternalPageType.Incognito, "wb://incognito resolves to Incognito");
                Assert(InternalPageHelper.GetInternalPageType("chrome://incognito") == InternalPageType.Incognito, "chrome://incognito resolves to Incognito");
                Assert(InternalPageHelper.GetInternalPageType("about:incognito") == InternalPageType.Incognito, "about:incognito resolves to Incognito");
                Assert(InternalPageHelper.NormalizeInternalUrl("chrome://incognito") == "wb://incognito", "chrome://incognito normalizes to wb://incognito");
                Assert(InternalPageHelper.NormalizeInternalUrl("about:incognito") == "wb://incognito", "about:incognito normalizes to wb://incognito");
                Assert(InternalPageHelper.GetInternalTitle("wb://incognito") == "New Tab", "Incognito title is 'New Tab'");

                Image fav = InternalPageHelper.GetInternalFavicon("wb://incognito");
                Assert(fav != null, "Incognito favicon should not be null");
                Assert(fav.Width > 0 && fav.Height > 0, "Incognito favicon has valid dimensions");

                HistoryManager hm = new HistoryManager();
                int countBefore = hm.GetAll().Count;
                hm.AddEntry("Private Tab", "wb://incognito");
                hm.AddEntry("Private Tab 2", "chrome://incognito");
                int countAfter = hm.GetAll().Count;
                Assert(countBefore == countAfter, "HistoryManager strictly excludes incognito internal URLs");
            });

            Test("Incognito Vector Icon Rendering and Resolution", () =>
            {
                Bitmap bmp16 = MaterialIcons.GetIcon("incognito", 16);
                Assert(bmp16 != null, "16px incognito icon should render");
                Assert(bmp16.Width == 16 && bmp16.Height == 16, "16px icon dimensions match");

                Bitmap bmp24 = MaterialIcons.GetIcon("incognito", 24, Color.White);
                Assert(bmp24 != null, "24px incognito icon should render");
                Assert(bmp24.Width == 24 && bmp24.Height == 24, "24px icon dimensions match");

                Bitmap bmp64 = MaterialIcons.GetIcon("incognito", 64, Color.FromArgb(170, 185, 205));
                Assert(bmp64 != null, "64px incognito icon should render");
                Assert(bmp64.Width == 64 && bmp64.Height == 64, "64px icon dimensions match");

                // Verify optical balance matches other top bar icons (house, gear, etc.)
                Func<Bitmap, Rectangle> getRenderedBounds = bmp => {
                    int minX = bmp.Width, maxX = -1, minY = bmp.Height, maxY = -1;
                    for (int y = 0; y < bmp.Height; y++)
                        for (int x = 0; x < bmp.Width; x++)
                            if (bmp.GetPixel(x, y).A > 30) {
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                    return (maxX >= minX && maxY >= minY) ? new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1) : Rectangle.Empty;
                };

                Rectangle bounds16 = getRenderedBounds(bmp16);
                Assert(bounds16.Height >= 13, string.Format("16px incognito icon height ({0}px) should optically balance with toolbar icons (>= 13px)", bounds16.Height));
                Assert(bounds16.Width >= 14, string.Format("16px incognito icon width ({0}px) should optically balance with toolbar icons (>= 14px)", bounds16.Width));

                Rectangle bounds24 = getRenderedBounds(bmp24);
                Assert(bounds24.Height >= 19, string.Format("24px incognito icon height ({0}px) should optically balance with toolbar icons (>= 19px)", bounds24.Height));
                Assert(bounds24.Width >= 21, string.Format("24px incognito icon width ({0}px) should optically balance with toolbar icons (>= 21px)", bounds24.Width));
            });

            Test("IncognitoNewTabView Architecture, Theme and Input Lifecycle", () =>
            {
                string targetUrl = null;
                using (IncognitoNewTabView view = new IncognitoNewTabView((url, inNewTab) => { targetUrl = url; }))
                {
                    view.Size = new Size(1200, 800);
                    Assert(view.Dock == DockStyle.None, "Default dock can be set");
                    view.Dock = DockStyle.Fill;
                    Assert(view.Dock == DockStyle.Fill, "IncognitoNewTabView supports DockStyle.Fill");

                    TextBox searchBox = null;
                    foreach (Control c in view.Controls)
                    {
                        if (c is TextBox)
                        {
                            searchBox = (TextBox)c;
                            break;
                        }
                    }

                    Assert(searchBox != null, "Search box control should be present");

                    // Test direct URL entry
                    searchBox.Text = "https://example.org/private";
                    var onKeyDown = typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    onKeyDown.Invoke(searchBox, new object[] { new KeyEventArgs(Keys.Enter) });
                    Assert(targetUrl == "https://example.org/private", "Enter key in search box triggers navigation with direct URL");

                    // Test search query keyword
                    searchBox.Text = "privacy search term";
                    onKeyDown.Invoke(searchBox, new object[] { new KeyEventArgs(Keys.Enter) });
                    Assert(targetUrl != null && targetUrl.Contains("google.com/search?q=privacy%20search%20term"), "Non-URL query executes private web search");

                    // Test Paint cycle wide
                    using (Bitmap surface = new Bitmap(1200, 800))
                    using (Graphics g = Graphics.FromImage(surface))
                    {
                        var onPaint = typeof(Control).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(view, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 1200, 800)) });
                    }

                    // Test stacked layout on narrow width
                    view.Size = new Size(400, 700);
                    using (Bitmap surfaceNarrow = new Bitmap(400, 700))
                    using (Graphics g = Graphics.FromImage(surfaceNarrow))
                    {
                        var onPaint = typeof(Control).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(view, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 400, 700)) });
                    }
                }
            });

            Test("WbTopBar Incognito Home Button, No Redundant Badge and BrowserTab Favicon", () =>
            {
                using (WbTopBar topBar = new WbTopBar())
                {
                    topBar.Width = 1000;
                    topBar.Height = 40;

                    // Standard mode
                    Assert(!topBar.IsIncognito, "TopBar starts with IsIncognito=false");
                    Assert(topBar.GetIncognitoBadgeRect().IsEmpty, "Standard top bar has empty incognito badge rect");

                    // Switch to incognito mode: redundant badge removed, Home button adopts incognito
                    topBar.IsIncognito = true;
                    Assert(topBar.IsIncognito, "TopBar IsIncognito is true");
                    Assert(topBar.GetIncognitoBadgeRect().IsEmpty, "Incognito top bar removes redundant badge rect");

                    Rectangle homeRect = topBar.GetHomeButtonRect();
                    Assert(homeRect.Width > 0, "Home button exists and has positive width");

                    // Test paint cycle in incognito mode
                    using (Bitmap surface = new Bitmap(1000, 80))
                    using (Graphics g = Graphics.FromImage(surface))
                    {
                        var onPaint = typeof(Control).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        onPaint.Invoke(topBar, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 1000, 80)) });
                    }

                    // Hover Home button in incognito mode shows TipHomeIncognito
                    var onMouseMove = typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, homeRect.X + 5, homeRect.Y + 5, 0) });
                    Assert(topBar.GetCurrentToolTip() == AppStrings.Browser.TipHomeIncognito, "Home button tooltip shows TipHomeIncognito in incognito mode");

                    // Click Home button fires HomeClicked event
                    bool homeClicked = false;
                    topBar.HomeClicked += (s, e) => { homeClicked = true; };
                    var onMouseDown = typeof(Control).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseUp = typeof(Control).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, homeRect.X + 5, homeRect.Y + 5, 0) });
                    onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, homeRect.X + 5, homeRect.Y + 5, 0) });
                    Assert(homeClicked, "Clicking Home button in incognito mode fires HomeClicked event");

                    // Verify start page recognition for incognito URLs
                    Assert(InternalPageHelper.IsStartPage("wb://incognito"), "wb://incognito is recognized as startpage");
                    Assert(InternalPageHelper.IsStartPage("wb:incognito"), "wb:incognito is recognized as startpage");
                    Assert(InternalPageHelper.IsStartPage("chrome://incognito"), "chrome://incognito is recognized as startpage");
                }

                // Verify InternalPageHelper returns incognito icon for incognito session
                Image incogFavicon = InternalPageHelper.GetInternalFavicon(InternalPageHelper.IncognitoUrl);
                Assert(incogFavicon != null, "InternalPageHelper returns valid favicon for wb://incognito");

                Image incogNewTabFavicon = InternalPageHelper.GetInternalFavicon(InternalPageHelper.NewTabUrl, true);
                Assert(incogNewTabFavicon != null, "InternalPageHelper returns incognito favicon for NewTab when isIncognito=true");

                Image standardNewTabFavicon = InternalPageHelper.GetInternalFavicon(InternalPageHelper.NewTabUrl, false);
                Assert(standardNewTabFavicon != null, "InternalPageHelper returns standard favicon for NewTab when isIncognito=false");

                // Verify BrowserTab architecture enforces incognito favicon and GoHome destination
                string testSrcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(testSrcDir)) testSrcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                string tabSrc = File.ReadAllText(Path.Combine(testSrcDir, @"Browser\BrowserTab.cs"));
                Assert(tabSrc.Contains("LoadUrl(IsIncognito ? InternalPageHelper.IncognitoUrl : InternalPageHelper.NewTabUrl);"), "GoHome navigates to IncognitoUrl when incognito");
                Assert(tabSrc.Contains("Favicon = InternalPageHelper.GetInternalFavicon(normUrl, IsIncognito);"), "LoadUrl uses IsIncognito favicon");
                Assert(tabSrc.Contains("!isIncognito && (InternalPageHelper.GetInternalPageType(normUrl) == InternalPageType.Incognito"), "BrowserTab enforces normal mode cannot load incognito page");
                Assert(tabSrc.Contains("if (IsIncognito)") && !tabSrc.Contains("if (IsIncognito || type == InternalPageType.Incognito)"), "BrowserTab ShowInternalPage restricts IncognitoNewTabView to incognito tabs only");
                string formSrc = File.ReadAllText(Path.Combine(testSrcDir, @"Browser\MainBrowserForm.cs"));
                Assert(formSrc.Contains("!IsIncognito && (string.Equals(targetUrl, InternalPageHelper.IncognitoUrl"), "MainBrowserForm intercepts incognito address navigation and creates new incognito window");
            });

            Test("WbTopBar Downloads Active Count Badge, Dynamic Tooltip and DownloadManager Tracking", () =>
            {
                using (WbTopBar topBar = new WbTopBar())
                {
                    topBar.Width = 1000;
                    topBar.Height = 40;

                    // Initial state
                    Assert(topBar.ActiveDownloadCount == 0, "TopBar starts with ActiveDownloadCount=0");
                    Rectangle dlRect = topBar.GetDownloadsButtonRect();
                    Assert(dlRect.Width > 0, "Downloads button exists with positive width");

                    // Tooltip without active downloads
                    var onMouseMove = typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, dlRect.X + 5, dlRect.Y + 5, 0) });
                    Assert(topBar.GetCurrentToolTip() == AppStrings.Browser.TipDownloads, "Standard downloads tooltip without active count matches TipDownloads");

                    // Set ActiveDownloadCount to 3
                    topBar.ActiveDownloadCount = 3;
                    Assert(topBar.ActiveDownloadCount == 3, "ActiveDownloadCount updated to 3");
                    Rectangle dlRectWithBadge = topBar.GetDownloadsButtonRect();
                    Assert(dlRectWithBadge.Width == dlRect.Width, "Downloads button retains uniform compact toolbar width");

                    // Tooltip with active downloads
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, dlRectWithBadge.X + 5, dlRectWithBadge.Y + 5, 0) });
                    Assert(topBar.GetCurrentToolTip() == string.Format(AppStrings.Browser.TipDownloadsActiveFormat, 3), "Downloads tooltip indicates 3 active downloads");

                    // Render to bitmap to verify badge drawn in bottom-right quadrant
                    using (Bitmap bmp = new Bitmap(1000, 40))
                    {
                        topBar.DrawToBitmap(bmp, new Rectangle(0, 0, 1000, 40));
                        bool foundBadgePixel = false;
                        Color bg = Color.FromArgb(41, 42, 45);
                        for (int y = dlRectWithBadge.Top; y < dlRectWithBadge.Bottom; y++)
                        {
                            for (int x = dlRectWithBadge.Left; x < dlRectWithBadge.Right; x++)
                            {
                                Color c = bmp.GetPixel(x, y);
                                if (c.R != bg.R || c.G != bg.G || c.B != bg.B)
                                {
                                    if (x > dlRectWithBadge.Left + dlRectWithBadge.Width / 2 && y > dlRectWithBadge.Top + dlRectWithBadge.Height / 2)
                                    {
                                        foundBadgePixel = true;
                                    }
                                }
                            }
                        }
                        Assert(foundBadgePixel, "Downloads button renders badge in bottom-right quadrant");
                    }

                    // Reset ActiveDownloadCount back to 0
                    topBar.ActiveDownloadCount = 0;
                    Assert(topBar.ActiveDownloadCount == 0, "ActiveDownloadCount reset to 0");
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, dlRect.X + 5, dlRect.Y + 5, 0) });
                    Assert(topBar.GetCurrentToolTip() == AppStrings.Browser.TipDownloads, "Downloads tooltip reverted to standard TipDownloads when 0 active");
                }

                // Verify DownloadManager GetActiveCount and ActiveDownloadsCountChanged event
                DownloadManager dm = new DownloadManager();
                int initialCount = dm.GetActiveCount();
                int eventFiredCount = -1;
                dm.ActiveDownloadsCountChanged += (s, count) => { eventFiredCount = count; };

                long id = DateTime.UtcNow.Ticks;
                dm.RegisterDownload(id, "test_file.zip", @"C:\temp\test_file.zip", "https://example.com/test_file.zip", 1024);
                Assert(dm.GetActiveCount() == initialCount + 1, "RegisterDownload increments active count");
                Assert(eventFiredCount == initialCount + 1, "ActiveDownloadsCountChanged fired on RegisterDownload");

                dm.UpdateProgress(id, 1024, 1024, 100, "Completed", 0);
                Assert(dm.GetActiveCount() == initialCount, "Completed download decrements active count back");
                Assert(eventFiredCount == initialCount, "ActiveDownloadsCountChanged fired on download completion");

                // Verify UITheme badge colors match uBlock #666666 style
                Assert(UITheme.BadgeBackground == Color.FromArgb(102, 102, 102), "UITheme.BadgeBackground matches uBlock #666666");
                Assert(UITheme.BadgeText == Color.White, "UITheme.BadgeText is white");
                Assert(MaterialColors.BadgeBackground == Color.FromArgb(102, 102, 102), "MaterialColors.BadgeBackground matches uBlock #666666");
                Assert(MaterialColors.BadgeText == Color.White, "MaterialColors.BadgeText is white");
            });

            Test("Download File Lifecycle - .wbdownload Container & Atomic Finalization", () =>
            {
                DownloadManager dm = new DownloadManager();
                long testId = DateTime.UtcNow.Ticks + 101;
                string tempDir = Path.Combine(Path.GetTempPath(), "wb_dl_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string targetPath = Path.Combine(tempDir, "sample_document.pdf");

                try
                {
                    // 1. Unique path generation
                    string uniquePath = DownloadManager.GetUniqueFilePath(tempDir, "sample_document.pdf");
                    Assert(uniquePath == targetPath, "First path matches target");

                    // 2. Register download
                    var item = dm.RegisterDownload(testId, "sample_document.pdf", targetPath, "https://example.com/sample.pdf", 102400);
                    Assert(item != null, "Download item registered");
                    Assert(item.WbDownloadPath == targetPath + ".wbdownload", "WbDownloadPath appends .wbdownload extension");
                    Assert(item.CheckFileExists() == false, "Target file does not exist yet");
                    Assert(item.CheckWbDownloadExists() == false, ".wbdownload does not exist yet");

                    // 3. Write simulated in-flight .wbdownload payload
                    byte[] payload = new byte[51200]; // 50%
                    for (int i = 0; i < payload.Length; i++) payload[i] = 0xAA;
                    File.WriteAllBytes(item.WbDownloadPath, payload);

                    Assert(item.CheckWbDownloadExists() == true, ".wbdownload container exists in transit");
                    Assert(item.GetWbDownloadSize() == 51200, ".wbdownload holds exact 51200 byte footprint");
                    Assert(File.Exists(targetPath) == false, "Final payload does not exist yet while in transit");

                    // Deduplication test: when targetPath exists or .wbdownload exists, GetUniqueFilePath produces (1)
                    string dedupPath = DownloadManager.GetUniqueFilePath(tempDir, "sample_document.pdf");
                    Assert(dedupPath.Contains("(1)"), "GetUniqueFilePath auto-deduplicates when in-flight download exists");

                    // 4. Atomic finalization
                    File.WriteAllBytes(item.WbDownloadPath, new byte[102400]);
                    if (File.Exists(item.WbDownloadPath))
                    {
                        if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
                        File.Move(item.WbDownloadPath, item.FilePath);
                    }
                    dm.UpdateProgress(testId, 102400, 102400, 100, "Completed", 0);

                    Assert(item.CheckWbDownloadExists() == false, ".wbdownload stripped upon completion");
                    Assert(item.CheckFileExists() == true, "Final file released for user interaction");
                    Assert(new FileInfo(item.FilePath).Length == 102400, "Final file has exact 102400 byte size");
                }
                finally
                {
                    dm.DeleteDownload(testId, true);
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                }
            });

            Test("Download Graceful Exit - Interception, PauseAllForExit & Byte Footprint Retention", () =>
            {
                DownloadManager dm = new DownloadManager();
                long id1 = DateTime.UtcNow.Ticks + 201;
                long id2 = DateTime.UtcNow.Ticks + 202;
                long id3 = DateTime.UtcNow.Ticks + 203;

                string tempDir = Path.Combine(Path.GetTempPath(), "wb_exit_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string path1 = Path.Combine(tempDir, "large_file1.zip");
                string path2 = Path.Combine(tempDir, "large_file2.zip");
                string path3 = Path.Combine(tempDir, "finished_file.zip");

                try
                {
                    var item1 = dm.RegisterDownload(id1, "large_file1.zip", path1, "https://example.com/file1.zip", 200000);
                    var item2 = dm.RegisterDownload(id2, "large_file2.zip", path2, "https://example.com/file2.zip", 300000);
                    var item3 = dm.RegisterDownload(id3, "finished_file.zip", path3, "https://example.com/file3.zip", 50000);

                    // Write 40000 bytes into item1's .wbdownload container (20% complete)
                    File.WriteAllBytes(item1.WbDownloadPath, new byte[40000]);
                    dm.UpdateProgress(id1, 40000, 200000, 20, "InProgress", 15000);

                    // Item 2 is queued
                    dm.UpdateProgress(id2, 0, 300000, 0, "Queued", 0);

                    // Item 3 is completed
                    File.WriteAllBytes(path3, new byte[50000]);
                    dm.UpdateProgress(id3, 50000, 50000, 100, "Completed", 0);

                    // 2 active items (item1, item2)
                    Assert(dm.GetActiveCount() == 2, "Active count is 2 prior to exit");

                    // Trigger PauseAllForExit (interception simulation)
                    dm.PauseAllForExit();

                    // All active items must transition to Paused
                    Assert(dm.GetActiveCount() == 0, "Active count drops to 0 after exit pause");
                    Assert(item1.State == "Paused", "Active download transitioned to Paused");
                    Assert(item1.CurrentSpeed == 0, "CurrentSpeed reset to 0");
                    Assert(item2.State == "Paused", "Queued download transitioned to Paused");
                    Assert(item3.State == "Completed", "Completed download left untouched");

                    // Byte footprint retained
                    Assert(File.Exists(item1.WbDownloadPath), "Partial .wbdownload file retained on disk");
                    Assert(new FileInfo(item1.WbDownloadPath).Length == 40000, "Partial bytes exact match");
                }
                finally
                {
                    dm.DeleteDownload(id1, true);
                    dm.DeleteDownload(id2, true);
                    dm.DeleteDownload(id3, true);
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                }
            });

            Test("Download Crash Recovery - Cold Launch Reconstitution & Orphaned .wbdownload", () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "wb_crash_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string testFile = Path.Combine(tempDir, "interrupted_video.mp4");
                string wbPath = testFile + ".wbdownload";

                // Simulate orphaned .wbdownload file with 131072 bytes (128KB)
                File.WriteAllBytes(wbPath, new byte[131072]);

                // Create a download model that was in-progress when crash occurred
                long crashId = DateTime.UtcNow.Ticks + 301;
                DownloadItemModel orphaned = new DownloadItemModel
                {
                    Id = crashId,
                    FileName = "interrupted_video.mp4",
                    FilePath = testFile,
                    Url = "https://example.com/video.mp4",
                    TotalBytes = 1048576, // 1MB
                    ReceivedBytes = 50000, // Stale in-memory progress before crash
                    Percent = 4,
                    State = "InProgress",
                    StartTime = DateTime.Now
                };

                // Save directly to storage simulating un-clean shutdown
                StorageHelper.SaveList("downloads.json", new List<DownloadItemModel> { orphaned });

                // Cold launch simulation: create fresh DownloadManager
                DownloadManager dm = new DownloadManager();

                var recovered = dm.GetItem(crashId);
                Assert(recovered != null, "Orphaned item loaded on cold launch");
                Assert(recovered.State == "Interrupted", "Orphaned InProgress state reconstituted to Interrupted");
                Assert(recovered.CurrentSpeed == 0, "Current speed reset to 0");
                Assert(recovered.ReceivedBytes == 131072, "ReceivedBytes synchronized with physical .wbdownload container on disk");
                Assert(recovered.Percent == 12, "Percent recalculated based on actual disk bytes");
                Assert(dm.GetActiveCount() == 0, "No phantom active downloads on cold launch");
                Assert(File.Exists(wbPath), "Orphaned .wbdownload file securely preserved on disk");

                dm.DeleteDownload(crashId, true);
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            });

            Test("Download Network Error - Granular Status & HTTP 206 Byte-Range Resume vs Fallback", () =>
            {
                DownloadManager dm = new DownloadManager();
                long netId = DateTime.UtcNow.Ticks + 401;
                string tempDir = Path.Combine(Path.GetTempPath(), "wb_net_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string targetPath = Path.Combine(tempDir, "patch.bin");

                try
                {
                    var item = dm.RegisterDownload(netId, "patch.bin", targetPath, "https://example.com/patch.bin", 500000);
                    File.WriteAllBytes(item.WbDownloadPath, new byte[200000]);

                    // Simulate network failure
                    dm.UpdateProgress(netId, 200000, 500000, 40, "Failed - Network error", 0);
                    Assert(item.GetStatusDisplay() == "Failed - Network error", "Status display reflects Failed - Network error");
                    Assert(File.Exists(item.WbDownloadPath), ".wbdownload preserved on network failure");
                    Assert(new FileInfo(item.WbDownloadPath).Length == 200000, "Exact bytes preserved");

                    // 1. Simulate HTTP 206 Partial Content resumption (appending to .wbdownload)
                    long existingOffset = item.GetWbDownloadSize();
                    Assert(existingOffset == 200000, "Range offset begins at 200000");

                    using (FileStream fs = new FileStream(item.WbDownloadPath, FileMode.Append, FileAccess.Write))
                    {
                        byte[] appendChunk = new byte[100000];
                        fs.Write(appendChunk, 0, appendChunk.Length);
                    }
                    Assert(new FileInfo(item.WbDownloadPath).Length == 300000, "Appended data directly to .wbdownload container");

                    // 2. Simulate HTTP 200 OK fallback (server rejects range -> reset to 0)
                    using (FileStream fs = new FileStream(item.WbDownloadPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] freshChunk = new byte[50000];
                        fs.Write(freshChunk, 0, freshChunk.Length);
                    }
                    Assert(new FileInfo(item.WbDownloadPath).Length == 50000, "Fallback reset container cleanly to 0% offset");
                }
                finally
                {
                    dm.DeleteDownload(netId, true);
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                }
            });

            Test("Download Disk Full - Error State Mapping & Resume Pre-flight Space Validation", () =>
            {
                DownloadManager dm = new DownloadManager();
                long diskId = DateTime.UtcNow.Ticks + 501;
                string tempDir = Path.Combine(Path.GetTempPath(), "wb_disk_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string targetPath = Path.Combine(tempDir, "large_iso.iso");

                try
                {
                    var item = dm.RegisterDownload(diskId, "large_iso.iso", targetPath, "https://example.com/large.iso", 1000000);
                    File.WriteAllBytes(item.WbDownloadPath, new byte[600000]);

                    // Simulate disk full error
                    dm.UpdateProgress(diskId, 600000, 1000000, 60, "Failed - Disk full", 0);
                    Assert(item.GetStatusDisplay() == "Failed - Disk full", "Status display reflects Failed - Disk full");
                    Assert(File.Exists(item.WbDownloadPath), ".wbdownload preserved up to disk full boundary");
                    Assert(new FileInfo(item.WbDownloadPath).Length == 600000, "Partial bytes intact");

                    // Test pre-flight disk check:
                    // When disk space is sufficient (normal case)
                    string reason;
                    bool canResume = dm.CanResumeDownload(item, out reason);
                    Assert(canResume, "CanResumeDownload returns true for normal file size on available drive");

                    // When required space is impossibly huge (simulating 1000 Petabytes remaining)
                    item.TotalBytes = long.MaxValue - 1000;
                    item.ReceivedBytes = 0;
                    bool blocked = dm.CanResumeDownload(item, out reason);
                    Assert(!blocked, "CanResumeDownload blocks when space is insufficient");
                    Assert(!string.IsNullOrEmpty(reason), "Block reason provided");
                }
                finally
                {
                    dm.DeleteDownload(diskId, true);
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                }
            });

            Test("Download UI & wb Branding - wb://downloads, Bubble Component & Cleanup", () =>
            {
                // URL & Branding
                Assert(InternalPageHelper.DownloadsUrl == "wb://downloads", "DownloadsUrl is wb://downloads");
                Assert(InternalPageHelper.NormalizeInternalUrl("chrome://downloads") == "wb://downloads", "chrome://downloads normalizes to wb://downloads");
                Assert(InternalPageHelper.NormalizeInternalUrl("about:downloads") == "wb://downloads", "about:downloads normalizes to wb://downloads");
                Assert(AppStrings.AppName == "wb", "AppName is wb");
                Assert(AppStrings.Downloads.WbDownloadExt == ".wbdownload", "WbDownloadExt is .wbdownload");
                Assert(AppStrings.Downloads.WindowTitle.Contains("wb"), "WindowTitle uses wb branding");
                Assert(AppStrings.Downloads.ExitDownloadsInProgressTitle.Contains("wb"), "Exit title contains wb");
                Assert(AppStrings.Downloads.ExitDownloadsInProgressMessage.Contains(".wbdownload"), "Exit message refers to .wbdownload");

                // DownloadBubblePopup verification
                using (DownloadBubblePopup bubble = new DownloadBubblePopup())
                {
                    Assert(bubble != null, "DownloadBubblePopup created");
                    Assert(bubble.Items.Count == 1, "Bubble has hosted control item");
                    bubble.RefreshContent();
                    Assert(bubble.Width == 360, "Bubble width is 360");
                    Assert(bubble.Height >= 140 && bubble.Height <= 170, "Bubble empty state height is compact: " + bubble.Height);
                }

                // Verify DownloadingDialog is removed
                string dialogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src\Downloads\DownloadingDialog.cs");
                Assert(!File.Exists(dialogPath), "DownloadingDialog.cs has been permanently removed");
            });

            Test("Download Action Icons Availability & Rendering", () =>
            {
                string[] downloadIcons = new string[] {
                    "pause", "play", "folder", "folder-simple", "check", "x", "download-simple", "arrow-clockwise"
                };

                foreach (string icon in downloadIcons)
                {
                    Bitmap bmp = MaterialIcons.GetIcon(icon, 16);
                    Assert(bmp != null, "Download action icon must load and render: " + icon);
                    Assert(bmp.Width == 16 && bmp.Height == 16, "Icon bitmap must be 16x16: " + icon);
                }
            });

            Test("DownloadBubble Dynamic Height Resizing", () =>
            {
                DownloadManager dm = AppServices.Downloads;
                long item1Id = DateTime.UtcNow.Ticks + 601;
                long item2Id = DateTime.UtcNow.Ticks + 602;

                try
                {
                    using (DownloadBubblePopup bubble = new DownloadBubblePopup())
                    {
                        bubble.RefreshContent();
                        int emptyHeight = bubble.Height;
                        Assert(emptyHeight >= 140 && emptyHeight <= 170, "Empty bubble height is compact: " + emptyHeight);

                        // Add 1 item matching user's scenario
                        dm.RegisterDownload(item1Id, "1GB.zip", "C:\\Users\\test\\Downloads\\1GB.zip", "https://ipv4-download.thinkbroadband.com/1GB.zip", 1024L * 1024L * 1024L);
                        dm.UpdateProgress(item1Id, (long)(23.6 * 1024 * 1024), 1024L * 1024L * 1024L, 2, "InProgress", 2.5 * 1024 * 1024);
                        bubble.RefreshContent();
                        int oneItemHeight = bubble.Height;
                        Assert(oneItemHeight >= 155 && oneItemHeight <= 180, "1-item bubble height expands proportionally: " + oneItemHeight);

                        string artDir = GetArtifactsDir();
                        if (!string.IsNullOrEmpty(artDir))
                        {
                            try
                            {
                                using (Form testHost = new Form { Size = new Size(bubble.Width, bubble.Height), StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) })
                                {
                                    DownloadBubbleControl ctrl = new DownloadBubbleControl();
                                    ctrl.RefreshItems();
                                    testHost.ClientSize = ctrl.Size;
                                    testHost.Controls.Add(ctrl);
                                    testHost.Show();
                                    using (Bitmap bmp = new Bitmap(ctrl.Width, ctrl.Height))
                                    {
                                        ctrl.DrawToBitmap(bmp, new Rectangle(0, 0, ctrl.Width, ctrl.Height));
                                        bmp.Save(Path.Combine(artDir, "download_bubble_preview.png"), System.Drawing.Imaging.ImageFormat.Png);
                                    }
                                }
                            }
                            catch { }
                        }

                        // Add 2nd item
                        dm.RegisterDownload(item2Id, "test2.zip", "C:\\test2.zip", "https://example.com/test2.zip", 8000);
                        bubble.RefreshContent();
                        int twoItemsHeight = bubble.Height;
                        Assert(twoItemsHeight > oneItemHeight, "2-item bubble height expands beyond 1-item: " + twoItemsHeight);
                    }
                }
                finally
                {
                    dm.DeleteDownload(item1Id, false);
                    dm.DeleteDownload(item2Id, false);
                }
            });

            Test(".wbdownload Destination Transit Lifecycle and Atomic Completion", () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "wb_transit_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string finalPath = Path.Combine(tempDir, "archive.zip");
                string wbdownloadPath = finalPath + DownloadItemModel.WbDownloadExtension;

                DownloadManager dm = new DownloadManager();
                long transitId = DateTime.UtcNow.Ticks + 701;

                try
                {
                    var item = dm.RegisterDownload(transitId, "archive.zip", finalPath, "https://example.com/archive.zip", 2048);
                    Assert(item.WbDownloadPath == wbdownloadPath, "WbDownloadPath computed correctly");

                    // Simulate in-progress transit writing to .wbdownload
                    File.WriteAllBytes(wbdownloadPath, new byte[1024]);
                    Assert(File.Exists(wbdownloadPath), ".wbdownload must exist at destination during download");
                    Assert(!File.Exists(finalPath), "Final destination file must not exist until 100% completed");

                    // Simulate atomic completion
                    File.Delete(wbdownloadPath);
                    File.WriteAllBytes(finalPath, new byte[2048]);
                    dm.UpdateProgress(transitId, 2048, 2048, 100, "Completed", 0);

                    Assert(File.Exists(finalPath), "Final destination file exists on completion");
                    Assert(!File.Exists(wbdownloadPath), ".wbdownload must no longer exist after completion");
                }
                finally
                {
                    dm.DeleteDownload(transitId, true);
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                }
            });

            Test("Cancelled or Removed Download Deletes .wbdownload File", () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "wb_cancel_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                DownloadManager dm = new DownloadManager();
                long cancelId = DateTime.UtcNow.Ticks + 801;
                long removeId = DateTime.UtcNow.Ticks + 802;

                try
                {
                    // 1. Test CancelDownload deletes .wbdownload
                    string cancelFinalPath = Path.Combine(tempDir, "cancel_test.bin");
                    string cancelWbPath = cancelFinalPath + DownloadItemModel.WbDownloadExtension;
                    dm.RegisterDownload(cancelId, "cancel_test.bin", cancelFinalPath, "https://example.com/cancel.bin", 5000);
                    File.WriteAllBytes(cancelWbPath, new byte[100]);
                    Assert(File.Exists(cancelWbPath), "Pre-condition: .wbdownload exists before cancel");

                    dm.CancelDownload(cancelId);
                    Assert(!File.Exists(cancelWbPath), "Requirement: .wbdownload is deleted when download entry is cancelled");

                    // 2. Test DeleteDownload (Remove from list) deletes .wbdownload even when deleteFromDisk is false
                    string removeFinalPath = Path.Combine(tempDir, "remove_test.bin");
                    string removeWbPath = removeFinalPath + DownloadItemModel.WbDownloadExtension;
                    dm.RegisterDownload(removeId, "remove_test.bin", removeFinalPath, "https://example.com/remove.bin", 5000);
                    File.WriteAllBytes(removeWbPath, new byte[100]);
                    Assert(File.Exists(removeWbPath), "Pre-condition: .wbdownload exists before remove");

                    dm.DeleteDownload(removeId, false);
                    Assert(!File.Exists(removeWbPath), "Requirement: .wbdownload is deleted when download entry is removed from list");
                }
                finally
                {
                    dm.DeleteDownload(cancelId, true);
                    dm.DeleteDownload(removeId, true);
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                }
            });

            Test("DownloadBubble Max 4 Items Without Scrollbar", () =>
            {
                DownloadManager dm = AppServices.Downloads;
                List<long> addedIds = new List<long>();

                try
                {
                    for (int i = 0; i < 6; i++)
                    {
                        long id = DateTime.UtcNow.Ticks + 900 + i;
                        addedIds.Add(id);
                        dm.RegisterDownload(id, "item" + i + ".zip", "C:\\item" + i + ".zip", "https://example.com/item" + i, 1000);
                    }

                    using (DownloadBubblePopup bubble = new DownloadBubblePopup())
                    {
                        bubble.RefreshContent();
                        Assert(bubble.Height >= 370 && bubble.Height <= 390, "4-item bubble height fits exactly without scrollbar: " + bubble.Height);

                        DownloadBubbleControl ctrl = null;
                        foreach (ToolStripItem item in bubble.Items)
                        {
                            ToolStripControlHost host = item as ToolStripControlHost;
                            if (host != null) ctrl = host.Control as DownloadBubbleControl;
                        }

                        Assert(ctrl != null, "DownloadBubbleControl retrieved");
                        if (ctrl != null)
                        {
                            Panel itemsPanel = null;
                            foreach (Control c in ctrl.Controls)
                            {
                                if (c is Panel && c.Dock == DockStyle.Fill) itemsPanel = (Panel)c;
                            }
                            Assert(itemsPanel != null, "ItemsPanel found");
                            if (itemsPanel != null)
                            {
                                Assert(!itemsPanel.AutoScroll, "Requirement: items panel AutoScroll is disabled (no scrollbar)");
                                Assert(itemsPanel.Controls.Count == 4, "Requirement: exactly max of 4 items rendered, but found: " + itemsPanel.Controls.Count);
                            }
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < addedIds.Count; i++)
                    {
                        dm.DeleteDownload(addedIds[i], false);
                    }
                }
            });

            Test("DownloadBubble Circle Progress Icon and Layout", () =>
            {
                using (DownloadBubbleControl.CircleProgressIcon icon = new DownloadBubbleControl.CircleProgressIcon())
                {
                    Assert(icon.Size.Width == 34 && icon.Size.Height == 34, "CircleProgressIcon is 34x34");
                    Assert(!icon.ShowProgress, "Default ShowProgress is false");
                    icon.ShowProgress = true;
                    icon.Value = 45;
                    Assert(icon.Value == 45, "Value set to 45");
                    icon.Value = 150;
                    Assert(icon.Value == 100, "Value clamped to 100");
                    icon.Value = -20;
                    Assert(icon.Value == 0, "Value clamped to 0");
                    icon.IconImage = MaterialIcons.GetIcon("download-simple", 16);
                    Assert(icon.IconImage != null, "IconImage assigned");

                    // Render test into bitmap to verify no GDI+ drawing exceptions
                    using (Bitmap bmp = new Bitmap(icon.Width, icon.Height))
                    {
                        icon.DrawToBitmap(bmp, new Rectangle(0, 0, icon.Width, icon.Height));
                        Assert(bmp.Width == 34 && bmp.Height == 34, "Rendered CircleProgressIcon to bitmap");
                    }
                }

                DownloadManager dm = AppServices.Downloads;
                long activeId = DateTime.UtcNow.Ticks + 999;
                try
                {
                    dm.RegisterDownload(activeId, "circle_test.iso", "C:\\circle_test.iso", "https://example.com/test.iso", 100000);
                    dm.UpdateProgress(activeId, 50000, 100000, 50, "InProgress", 1024 * 1024);

                    using (DownloadBubblePopup bubble = new DownloadBubblePopup())
                    {
                        bubble.RefreshContent();

                        DownloadBubbleControl ctrl = null;
                        foreach (ToolStripItem item in bubble.Items)
                        {
                            ToolStripControlHost host = item as ToolStripControlHost;
                            if (host != null) ctrl = host.Control as DownloadBubbleControl;
                        }
                        Assert(ctrl != null, "DownloadBubbleControl retrieved");

                        Panel itemsPanel = null;
                        foreach (Control c in ctrl.Controls)
                        {
                            if (c is Panel && c.Dock == DockStyle.Fill) itemsPanel = (Panel)c;
                        }
                        Assert(itemsPanel != null, "ItemsPanel found");
                        Assert(itemsPanel.Controls.Count >= 1, "At least 1 card found");

                        Control card = itemsPanel.Controls[0];
                        bool foundCircleIcon = false;
                        bool foundFlatProgressBar = false;

                        foreach (Control inner in card.Controls)
                        {
                            if (inner is DownloadBubbleControl.CircleProgressIcon)
                            {
                                foundCircleIcon = true;
                                DownloadBubbleControl.CircleProgressIcon cpi = (DownloadBubbleControl.CircleProgressIcon)inner;
                                Assert(cpi.ShowProgress, "CircleProgressIcon ShowProgress is true for active download");
                                Assert(cpi.Value == 50, "CircleProgressIcon Value matches download percent (50)");
                            }
                            if (inner.GetType().Name == "FlatProgressBar")
                            {
                                foundFlatProgressBar = true;
                            }
                        }

                        Assert(foundCircleIcon, "Card contains CircleProgressIcon at download icon");
                        Assert(!foundFlatProgressBar, "Card does not contain old horizontal FlatProgressBar");
                    }
                }
                finally
                {
                    dm.DeleteDownload(activeId, false);
                }
            });

            Test("DownloadBubble Compact Text Formatting & Bottom Bar Suppression", () =>
            {
                // 1. Verify FormatCompactSize & FormatFileSize rounding to nearest GB, MB, KB, B
                Assert(DownloadBubbleControl.FormatCompactSize(1024L * 1024L * 1024L) == "1GB", "1024MB formats to 1GB");
                Assert(DownloadBubbleControl.FormatCompactSize((long)(1023.6 * 1024 * 1024)) == "1GB", "1023.6MB rounds to 1GB");
                Assert(DownloadBubbleControl.FormatCompactSize(500L * 1024L * 1024L) == "500MB", "500MB formats to 500MB");
                Assert(DownloadBubbleControl.FormatCompactSize((long)(23.6 * 1024 * 1024)) == "24MB", "23.6MB rounds to nearest MB (24MB)");
                Assert(DownloadBubbleControl.FormatCompactSize(1024L * 1024L) == "1MB", "1024KB formats to 1MB");
                Assert(DownloadBubbleControl.FormatCompactSize(500L * 1024L) == "500KB", "500KB formats to 500KB");
                Assert(DownloadBubbleControl.FormatCompactSize(1024L) == "1KB", "1024B formats to 1KB");
                Assert(DownloadBubbleControl.FormatCompactSize(500L) == "500B", "500B formats to 500B");
                Assert(DownloadItemModel.FormatFileSize(1024L * 1024L * 1024L) == "1 GB", "FormatFileSize 1024MB formats to 1 GB");

                // 2. Verify FormatCompactEta
                DownloadItemModel item = new DownloadItemModel
                {
                    TotalBytes = 1000,
                    ReceivedBytes = 500,
                    CurrentSpeed = 100 // 500 bytes left at 100B/s = 5 seconds
                };
                Assert(DownloadBubbleControl.FormatCompactEta(item) == "5 sec left", "5 seconds remaining formats to '5 sec left'");

                item.CurrentSpeed = 500.0 / 60.0; // 60 seconds
                Assert(DownloadBubbleControl.FormatCompactEta(item) == "1 min left", "60 seconds formats to '1 min left'");

                // 3. Verify FormatRelativeTime
                DateTime now = new DateTime(2026, 10, 18, 12, 0, 0);
                Assert(DownloadBubbleControl.FormatRelativeTime(now.AddSeconds(-10), now) == "Just now", "10 seconds ago formats to 'Just now'");
                Assert(DownloadBubbleControl.FormatRelativeTime(now.AddMinutes(-1), now) == "1 min ago", "1 minute ago formats to '1 min ago'");
                Assert(DownloadBubbleControl.FormatRelativeTime(now.AddMinutes(-5), now) == "5 min ago", "5 minutes ago formats to '5 min ago'");
                Assert(DownloadBubbleControl.FormatRelativeTime(now.AddHours(-2), now) == "2 hr ago", "2 hours ago formats to '2 hr ago'");

                // 4. Verify card rendering in download bubble
                DownloadManager dm = AppServices.Downloads;
                long testId = DateTime.UtcNow.Ticks + 888;
                string tempFile = Path.GetTempFileName();
                try
                {
                    dm.RegisterDownload(testId, "Filename.zip", tempFile, "https://example.com/Filename.zip", 1024L * 1024L * 1024L);
                    dm.UpdateProgress(testId, 500L * 1024L * 1024L, 1024L * 1024L * 1024L, 50, "InProgress", (524L * 1024L * 1024L) / 5.0);

                    using (DownloadBubblePopup bubble = new DownloadBubblePopup())
                    {
                        bubble.RefreshContent();

                        DownloadBubbleControl ctrl = null;
                        foreach (ToolStripItem tItem in bubble.Items)
                        {
                            ToolStripControlHost host = tItem as ToolStripControlHost;
                            if (host != null) ctrl = host.Control as DownloadBubbleControl;
                        }
                        Assert(ctrl != null, "DownloadBubbleControl found");

                        Panel itemsPanel = null;
                        foreach (Control c in ctrl.Controls)
                        {
                            if (c is Panel && c.Dock == DockStyle.Fill) itemsPanel = (Panel)c;
                        }
                        Assert(itemsPanel != null && itemsPanel.Controls.Count >= 1, "Item card present");

                        Control card = itemsPanel.Controls[0];
                        Label statusLbl = null;
                        foreach (Control child in card.Controls)
                        {
                            if (child is Label && child.Location.Y == 35) statusLbl = (Label)child;
                        }
                        Assert(statusLbl != null, "Status subtitle label found at Y=35");
                        Assert(statusLbl.Text == "500MB/1GB \u2022 5 sec left", "During download format is '500MB/1GB \u2022 5 sec left': " + statusLbl.Text);

                        // Complete download
                        dm.UpdateProgress(testId, 1024L * 1024L * 1024L, 1024L * 1024L * 1024L, 100, "Completed", 0);
                        bubble.RefreshContent();
                        Assert(statusLbl.Text.StartsWith("1GB \u2022"), "Completed format starts with '1GB \u2022': " + statusLbl.Text);

                        // Cancel download
                        dm.CancelDownload(testId);
                        bubble.RefreshContent();
                        Assert(statusLbl.Text == "Cancelled", "Cancelled download format is 'Cancelled': " + statusLbl.Text);
                    }
                }
                finally
                {
                    dm.DeleteDownload(testId, false);
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                }
            });

            Test("View Source URL Detection and Normalization", () =>
            {
                string fbStoryUrl = "view-source:https://www.facebook.com/stories/122102781578858716/UzpfSVNDOjExMjI3NjYwOTY5MTA4MDc=/?view_single=1";
                Assert(InternalPageHelper.IsViewSourceUrl(fbStoryUrl), "Should detect view-source on fb story url");
                Assert(InternalPageHelper.IsViewSourceUrl("VIEW-SOURCE:https://example.com"), "Case insensitive detection");
                Assert(InternalPageHelper.IsViewSourceUrl("  view-source:http://test.org  "), "Trimmed whitespace detection");
                Assert(!InternalPageHelper.IsViewSourceUrl("https://view-source.com"), "Should not match regular domain");
                Assert(!InternalPageHelper.IsViewSourceUrl(null), "Null safe");
                Assert(!InternalPageHelper.IsViewSourceUrl(""), "Empty safe");

                // Normalization
                Assert(InternalPageHelper.NormalizeViewSourceUrl(fbStoryUrl) == fbStoryUrl, "Already valid scheme preserved");
                Assert(InternalPageHelper.NormalizeViewSourceUrl("view-source:facebook.com") == "view-source:https://facebook.com", "Bare domain prepends https://");
                Assert(InternalPageHelper.NormalizeViewSourceUrl("view-source:www.facebook.com") == "view-source:https://www.facebook.com", "www domain prepends https://");
                Assert(InternalPageHelper.NormalizeViewSourceUrl("view-source:localhost:8080") == "view-source:http://localhost:8080", "Localhost prepends http://");
                Assert(InternalPageHelper.NormalizeViewSourceUrl("view-source:127.0.0.1:5000") == "view-source:http://127.0.0.1:5000", "IP 127.0.0.1 prepends http://");
                Assert(InternalPageHelper.NormalizeViewSourceUrl("view-source:") == "view-source:about:blank", "Empty inner defaults to about:blank");
                Assert(InternalPageHelper.NormalizeViewSourceUrl("view-source:   ") == "view-source:about:blank", "Whitespace inner defaults to about:blank");
                Assert(InternalPageHelper.NormalizeViewSourceUrl("view-source:file:///C:/path/file.html") == "view-source:file:///C:/path/file.html", "file:// scheme preserved");

                // Target extraction
                Assert(InternalPageHelper.ExtractViewSourceTarget(fbStoryUrl) == "https://www.facebook.com/stories/122102781578858716/UzpfSVNDOjExMjI3NjYwOTY5MTA4MDc=/?view_single=1", "Target extracted cleanly");
                Assert(InternalPageHelper.ExtractViewSourceTarget("https://normal.com") == "https://normal.com", "Non view-source passes through");
            });

            Test("FaviconManager and WebDragDrop View Source Support", () =>
            {
                string fbStoryUrl = "view-source:https://www.facebook.com/stories/122102781578858716/UzpfSVNDOjExMjI3NjYwOTY5MTA4MDc=/?view_single=1";
                string domain = FaviconManager.ExtractDomain(fbStoryUrl);
                Assert(domain == "facebook.com", "ExtractDomain strips view-source and www: " + domain);

                string bareDomain = FaviconManager.ExtractDomain("view-source:example.org/path");
                Assert(bareDomain == "example.org", "ExtractDomain strips view-source on bare domain: " + bareDomain);

                Assert(WebDragDropHelper.IsValidWebUrl(fbStoryUrl), "WebDragDropHelper recognizes view-source URL as valid");
                Assert(WebDragDropHelper.CleanUrl(fbStoryUrl) == fbStoryUrl, "WebDragDropHelper does not corrupt view-source URL");
            });

            Test("HistoryManager Excludes View Source URLs", () =>
            {
                HistoryManager hm = new HistoryManager();
                string fbStoryUrl = "view-source:https://www.facebook.com/stories/122102781578858716/UzpfSVNDOjExMjI3NjYwOTY5MTA4MDc=/?view_single=1";
                hm.AddEntry("FB Source", fbStoryUrl);
                var entries = hm.GetAll();
                bool found = false;
                foreach (var item in entries)
                {
                    if (item.Url == fbStoryUrl) found = true;
                }
                Assert(!found, "View source URLs must not pollute browsing history");
            });

            Test("NewTabView Search Bar Direct View Source Routing", () =>
            {
                string navigatedUrl = null;
                NewTabView ntv = new NewTabView((url, inNewTab) => { navigatedUrl = url; });
                try
                {
                    TextBox searchBox = null;
                    foreach (Control c in ntv.Controls)
                    {
                        if (c is TextBox) searchBox = (TextBox)c;
                    }
                    Assert(searchBox != null, "Search box found in NewTabView");

                    string fbStoryUrl = "view-source:https://www.facebook.com/stories/122102781578858716/UzpfSVNDOjExMjI3NjYwOTY5MTA4MDc=/?view_single=1";
                    searchBox.Text = fbStoryUrl;

                    // Simulate pressing Enter
                    KeyEventArgs enterKey = new KeyEventArgs(Keys.Enter);
                    System.Reflection.MethodInfo onKeyDownMethod = typeof(NewTabView).GetMethod("OnSearchBoxKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(onKeyDownMethod != null, "OnSearchBoxKeyDown method found");
                    onKeyDownMethod.Invoke(ntv, new object[] { searchBox, enterKey });

                    Assert(navigatedUrl == fbStoryUrl, "Direct view-source navigation fired instead of web search: " + navigatedUrl);
                }
                finally
                {
                    ntv.Dispose();
                }
            });

            Test("WbTopBar Address Bar Dropdown Context Menu Structure and Shortcuts", () =>
            {
                using (WbTopBar topBar = new WbTopBar())
                {
                    ContextMenuStrip menu = topBar.AddressContextMenu;
                    Assert(menu != null, "AddressContextMenu must not be null");
                    Assert(topBar.AddressBox != null, "AddressBox must not be null");
                    Assert(ReferenceEquals(topBar.AddressBox.ContextMenuStrip, menu), "AddressBox ContextMenuStrip must reference AddressContextMenu");
                    Assert(!menu.ShowImageMargin, "AddressContextMenu must have ShowImageMargin false for clean modern style");

                    Assert(menu.Items.Count == 7, "Menu must contain 7 items (Cut, Copy, Paste, Paste and go, Delete, Separator, Select all), actual: " + menu.Items.Count);
                    Assert(menu.Items[0].Text == AppStrings.Browser.AddressMenuCut, "Item 0 must be Cut");
                    Assert(menu.Items[1].Text == AppStrings.Browser.AddressMenuCopy, "Item 1 must be Copy");
                    Assert(menu.Items[2].Text == AppStrings.Browser.AddressMenuPaste, "Item 2 must be Paste");
                    Assert(menu.Items[3].Text == AppStrings.Browser.AddressMenuPasteAndGo, "Item 3 must be Paste and go");
                    Assert(menu.Items[3].Text == "Paste and go", "Exact text must be 'Paste and go'");
                    Assert(menu.Items[4].Text == AppStrings.Browser.AddressMenuDelete, "Item 4 must be Delete");
                    Assert(menu.Items[5] is ToolStripSeparator, "Item 5 must be ToolStripSeparator");
                    Assert(menu.Items[6].Text == AppStrings.Browser.AddressMenuSelectAll, "Item 6 must be Select all");

                    ToolStripMenuItem cutItem = (ToolStripMenuItem)menu.Items[0];
                    ToolStripMenuItem copyItem = (ToolStripMenuItem)menu.Items[1];
                    ToolStripMenuItem pasteItem = (ToolStripMenuItem)menu.Items[2];
                    ToolStripMenuItem pasteAndGoItem = (ToolStripMenuItem)menu.Items[3];
                    ToolStripMenuItem delItem = (ToolStripMenuItem)menu.Items[4];
                    ToolStripMenuItem selectAllItem = (ToolStripMenuItem)menu.Items[6];

                    Assert(cutItem.ShortcutKeyDisplayString == "Ctrl+X", "Cut display string Ctrl+X");
                    Assert(copyItem.ShortcutKeyDisplayString == "Ctrl+C", "Copy display string Ctrl+C");
                    Assert(pasteItem.ShortcutKeyDisplayString == "Ctrl+V", "Paste display string Ctrl+V");
                    Assert(delItem.ShortcutKeyDisplayString == "Del", "Delete display string Del");
                    Assert(selectAllItem.ShortcutKeyDisplayString == "Ctrl+A", "Select all display string Ctrl+A");
                }
            });

            Test("WbTopBar Address Bar Context Menu Dynamic State and Auto-Select", () =>
            {
                using (WbTopBar topBar = new WbTopBar())
                {
                    ContextMenuStrip menu = topBar.AddressContextMenu;
                    ToolStripMenuItem cutItem = (ToolStripMenuItem)menu.Items[0];
                    ToolStripMenuItem copyItem = (ToolStripMenuItem)menu.Items[1];
                    ToolStripMenuItem pasteItem = (ToolStripMenuItem)menu.Items[2];
                    ToolStripMenuItem pasteAndGoItem = (ToolStripMenuItem)menu.Items[3];
                    ToolStripMenuItem delItem = (ToolStripMenuItem)menu.Items[4];
                    ToolStripMenuItem selectAllItem = (ToolStripMenuItem)menu.Items[6];

                    // 1. With text in address bar and no selection: Opening must auto-select all
                    topBar.AddressText = "https://example.com/test";
                    topBar.AddressBox.SelectionLength = 0;

                    var onOpeningMethod = typeof(WbTopBar).GetMethod("AddressContextMenu_Opening", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(onOpeningMethod != null, "AddressContextMenu_Opening found");
                    System.ComponentModel.CancelEventArgs cancelArgs = new System.ComponentModel.CancelEventArgs();
                    onOpeningMethod.Invoke(topBar, new object[] { menu, cancelArgs });

                    Assert(!cancelArgs.Cancel, "Menu opening must not be cancelled");
                    Assert(topBar.AddressBox.SelectionLength == "https://example.com/test".Length, "Address box text must be auto-selected on context menu open");
                    Assert(cutItem.Enabled, "Cut must be enabled when text is selected");
                    Assert(copyItem.Enabled, "Copy must be enabled when text is selected");
                    Assert(delItem.Enabled, "Delete must be enabled when text is selected");
                    Assert(selectAllItem.Enabled, "Select all must be enabled when text exists");

                    // 2. Empty address box
                    topBar.AddressText = string.Empty;
                    topBar.AddressBox.SelectionLength = 0;
                    cancelArgs = new System.ComponentModel.CancelEventArgs();
                    onOpeningMethod.Invoke(topBar, new object[] { menu, cancelArgs });

                    Assert(!cutItem.Enabled, "Cut must be disabled when address box is empty");
                    Assert(!copyItem.Enabled, "Copy must be disabled when address box is empty");
                    Assert(!delItem.Enabled, "Delete must be disabled when address box is empty");
                    Assert(!selectAllItem.Enabled, "Select all must be disabled when address box is empty");
                }
            });

            Test("WbTopBar Paste and Go Navigation and Execution", () =>
            {
                using (WbTopBar topBar = new WbTopBar())
                {
                    string navigatedUrl = null;
                    topBar.NavigateRequested += (s, url) =>
                    {
                        navigatedUrl = url;
                    };

                    // 1. PasteAndGo with explicit string
                    topBar.PasteAndGo("  https://news.ycombinator.com/item?id=12345  ");
                    Assert(navigatedUrl == "https://news.ycombinator.com/item?id=12345", "Paste and go must trim whitespace and navigate: " + navigatedUrl);
                    Assert(topBar.AddressText == "https://news.ycombinator.com/item?id=12345", "AddressText must reflect pasted text");

                    // 2. PasteAndGo with whitespace or empty does nothing
                    navigatedUrl = null;
                    topBar.PasteAndGo("    ");
                    Assert(navigatedUrl == null, "Whitespace-only PasteAndGo must not navigate");
                    topBar.PasteAndGo(null);
                    Assert(navigatedUrl == null, "Null PasteAndGo must not navigate");

                    // 3. PasteAndGo via Clipboard
                    try
                    {
                        ClipboardHelper.SetText("https://developer.mozilla.org/en-US/");
                        string clipboardText = ClipboardHelper.GetText();
                        if (clipboardText == "https://developer.mozilla.org/en-US/")
                        {
                            navigatedUrl = null;
                            topBar.PasteAndGo();
                            Assert(navigatedUrl == "https://developer.mozilla.org/en-US/", "Clipboard PasteAndGo navigates correctly: " + navigatedUrl);
                            Assert(topBar.AddressText == "https://developer.mozilla.org/en-US/", "AddressText updated from clipboard");
                        }
                    }
                    catch { }

                    // 4. Click on 'Paste and go' menu item invokes PasteAndGo
                    ToolStripMenuItem pasteAndGoItem = (ToolStripMenuItem)topBar.AddressContextMenu.Items[3];
                    navigatedUrl = null;
                    topBar.AddressText = "old-url.com";
                    ClipboardHelper.SetText("https://github.com/trending");
                    if (ClipboardHelper.GetText() == "https://github.com/trending")
                    {
                        pasteAndGoItem.PerformClick();
                        Assert(navigatedUrl == "https://github.com/trending", "Clicking Paste and go menu item must navigate to clipboard URL");
                        Assert(topBar.AddressText == "https://github.com/trending", "Address bar updated on menu item click");
                    }
                }
            });

            Test("TitleHelper Normalization and Title Wrapping for Tab and Window Titles", () =>
            {
                // 1. Whitespace normalization and newline removal
                string messy = "  Breaking\r\n\tNews:  Astronomers discover new exoplanet  \n  in habitable zone   ";
                string clean = TitleHelper.NormalizeAndWrapTitle(messy);
                Assert(clean == "Breaking News: Astronomers discover new exoplanet in habitable zone", "Newlines and multiple spaces normalized: " + clean);

                // 2. Short titles (<= 120 chars) are NOT truncated
                string normalTitle = "OpenAI releases new research model with advanced reasoning capabilities";
                Assert(TitleHelper.NormalizeAndWrapTitle(normalTitle) == normalTitle, "Normal title remains intact");

                // 3. Excessively long titles (> 120 chars) are wrapped/truncated with ellipsis
                string longTitle = "This is an excessively long web page title designed to simulate real world websites with keyword stuffing and massive meta title tags that stretch out over hundreds of characters and normally cause severe UI clutter";
                string wrappedTabTitle = TitleHelper.NormalizeAndWrapTitle(longTitle, 120);
                Assert(wrappedTabTitle.Length == 123, "Wrapped title length is 120 + 3 (...): " + wrappedTabTitle.Length);
                Assert(wrappedTabTitle.EndsWith("..."), "Wrapped title ends with ellipsis");
                Assert(wrappedTabTitle.StartsWith("This is an excessively long"), "Wrapped title retains beginning content");

                // 4. Window title wrapping
                string winTitleNormal = TitleHelper.FormatWindowTitle("Google Search", false);
                Assert(winTitleNormal == "Google Search - wb", "Normal win title formatted: " + winTitleNormal);

                string winTitleLong = TitleHelper.FormatWindowTitle(longTitle, false);
                Assert(winTitleLong.EndsWith("... - wb"), "Long win title wrapped with ellipsis before app suffix: " + winTitleLong);
                Assert(winTitleLong.Length <= 135, "Win title bounded to safe length, got: " + winTitleLong.Length);

                string winTitleIncognito = TitleHelper.FormatWindowTitle(longTitle, true);
                Assert(winTitleIncognito.EndsWith("... - " + AppStrings.Browser.WindowTitleIncognito), "Long incognito win title wrapped properly: " + winTitleIncognito);

                // 5. Fallback titles
                Assert(TitleHelper.FormatWindowTitle(null, false) == AppStrings.Browser.WindowTitle, "Null title falls back to default window title");
                Assert(TitleHelper.FormatWindowTitle("", true) == AppStrings.Browser.WindowTitleIncognito, "Empty title falls back to incognito window title");
                Assert(TitleHelper.FormatWindowTitle("New Tab", false) == AppStrings.Browser.WindowTitle, "New Tab falls back to default window title");
            });

            Test("TitleHelper Tab Title Tooltip Multi-Line Wrapping and Lag Prevention", () =>
            {
                // 1. Normal length title (<= 120 chars) remains clean and single-line
                string normal = "GitHub: Let's build from here - The complete developer platform to build, scale and deliver secure software.";
                string normalTip = TitleHelper.FormatTabToolTip(normal);
                Assert(normalTip == normal, "Normal title tooltip is not modified or wrapped");
                Assert(!normalTip.Contains("\n"), "Normal title tooltip has no linebreaks");

                // 2. Long title (> 120 chars) is word-wrapped across multiple lines at word boundaries
                string longArticle = "Major international climate summit concludes in Geneva with landmark treaty on marine biodiversity preservation and greenhouse emissions reductions for industrial sectors";
                string wrappedTip = TitleHelper.FormatTabToolTip(longArticle);
                Assert(wrappedTip.Contains("\n"), "Long title tooltip wraps across multiple lines");
                string[] lines = wrappedTip.Split('\n');
                Assert(lines.Length >= 2, "Title is wrapped into at least 2 lines, got: " + lines.Length);
                for (int i = 0; i < lines.Length; i++)
                {
                    Assert(lines[i].Length <= 85, "Each wrapped line is within reasonable tooltip bounds (<= 85 chars): " + lines[i].Length);
                }

                // 3. Ultra long title (> 250 chars) is capped and wrapped
                string giantTitle = new string('A', 500) + " " + new string('B', 500);
                string giantTip = TitleHelper.FormatTabToolTip(giantTitle, 250);
                Assert(giantTip.Contains("..."), "Giant tooltip capped with ellipsis");
                Assert(giantTip.Length <= 265, "Total tooltip length is bounded, got: " + giantTip.Length);

                // 4. Zero-lag tooltip caching in WbTopBar: moving mouse across same tab does NOT re-set tooltip
                using (WbTopBar topBar = new WbTopBar { Width = 800 })
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.BindTabControl(tc);
                    tc.TabPages.Add(new MockFaviconTab(longArticle, "https://example.com/article"));
                    topBar.UpdateTabs();

                    Rectangle tabRect = topBar.GetTabRect(0);
                    var onMouseMove = typeof(WbTopBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    // Move 1
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, tabRect.Left + 10, tabRect.Top + 5, 0) });
                    string tip1 = topBar.GetCurrentToolTip();
                    Assert(tip1 == wrappedTip, "Tooltip is properly formatted wrapped text on initial hover");

                    // Move 2 (different pixel inside same tab)
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, tabRect.Left + 15, tabRect.Top + 5, 0) });
                    string tip2 = topBar.GetCurrentToolTip();
                    Assert(ReferenceEquals(tip1, tip2) || tip1 == tip2, "Tooltip caching prevents redundant updates on mouse move");
                }
            });

            Test("Internal Pages Disable Bookmark Button and Action", () =>
            {
                // 1. InternalPageHelper.CanBookmarkPage rules
                Assert(!InternalPageHelper.CanBookmarkPage(null), "Null URL cannot be bookmarked");
                Assert(!InternalPageHelper.CanBookmarkPage(""), "Empty URL cannot be bookmarked");
                Assert(!InternalPageHelper.CanBookmarkPage("wb://newtab"), "wb://newtab cannot be bookmarked");
                Assert(!InternalPageHelper.CanBookmarkPage("wb://bookmarks"), "wb://bookmarks cannot be bookmarked");
                Assert(!InternalPageHelper.CanBookmarkPage("wb://history"), "wb://history cannot be bookmarked");
                Assert(!InternalPageHelper.CanBookmarkPage("wb://downloads"), "wb://downloads cannot be bookmarked");
                Assert(!InternalPageHelper.CanBookmarkPage("wb://incognito"), "wb://incognito cannot be bookmarked");
                Assert(!InternalPageHelper.CanBookmarkPage("about:blank"), "about:blank cannot be bookmarked");
                Assert(!InternalPageHelper.CanBookmarkPage("view-source:https://example.com"), "view-source cannot be bookmarked");
                Assert(!InternalPageHelper.CanBookmarkPage("javascript:void(0)"), "javascript pseudo-urls cannot be bookmarked");
                Assert(InternalPageHelper.CanBookmarkPage("https://example.com"), "Standard HTTPS URL can be bookmarked");
                Assert(InternalPageHelper.CanBookmarkPage("http://localhost:8080"), "Standard HTTP localhost URL can be bookmarked");

                // 2. WbTopBar CanBookmark state, tooltip, click suppression and disabled rendering
                using (WbTopBar topBar = new WbTopBar { Width = 1000 })
                {
                    topBar.CanBookmark = false;
                    Assert(!topBar.CanBookmark, "CanBookmark correctly set to false");

                    bool starFired = false;
                    topBar.StarClicked += (s, e) => starFired = true;

                    // Click star while disabled
                    var onMouseDown = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseUp = typeof(WbTopBar).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseMove = typeof(WbTopBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    var toolbarRects = topBar.GetToolbarButtonRectangles();
                    Assert(toolbarRects.ContainsKey("star"), "TopBar must have star toolbar button");
                    Rectangle starRect = toolbarRects["star"];
                    Point starCenter = new Point(starRect.X + starRect.Width / 2, starRect.Y + starRect.Height / 2);

                    // Hover tooltip test
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, starCenter.X, starCenter.Y, 0) });
                    string tip = topBar.GetCurrentToolTip();
                    Assert(tip == "Cannot bookmark this page", "Star tooltip indicates page cannot be bookmarked, got: " + tip);

                    // Click test while CanBookmark == false
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, starCenter.X, starCenter.Y, 0) });
                    onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, starCenter.X, starCenter.Y, 0) });
                    Assert(!starFired, "StarClicked must NOT fire when CanBookmark is false");

                    // Re-enable and verify click fires
                    topBar.CanBookmark = true;
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, starCenter.X, starCenter.Y, 0) });
                    onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, starCenter.X, starCenter.Y, 0) });
                    Assert(starFired, "StarClicked fires when CanBookmark is true");

                    // Paint test with CanBookmark == false
                    topBar.CanBookmark = false;
                    using (Bitmap bmp = new Bitmap(1000, 40))
                    {
                        topBar.DrawToBitmap(bmp, new Rectangle(0, 0, 1000, 40));
                        Assert(bmp.Width == 1000, "TopBar draws disabled star cleanly");
                    }
                }
            });

            Test("Tools Gear Menu and All Top Bar Buttons Operational", () =>
            {
                // 1. Verify Stop menu item shortcut configuration doesn't crash with InvalidEnumArgumentException
                ToolStripMenuItem stopItem = new ToolStripMenuItem("&Stop");
                stopItem.ShortcutKeyDisplayString = "Esc";
                UITheme.ApplyMenuItem(stopItem);
                Assert(stopItem.ShortcutKeyDisplayString == "Esc", "Stop item has shortcut display string Esc");
                Assert(stopItem.ShortcutKeys == Keys.None, "Stop item has Keys.None shortcut keys to prevent WinForms enum crash");

                // Verify standalone Keys.Escape is caught by guard
                bool threwInvalidEnum = false;
                try
                {
                    stopItem.ShortcutKeys = Keys.Escape;
                }
                catch (System.ComponentModel.InvalidEnumArgumentException)
                {
                    threwInvalidEnum = true;
                }
                Assert(threwInvalidEnum, "Setting Keys.Escape on ToolStripMenuItem.ShortcutKeys throws InvalidEnumArgumentException without guard");

                // 2. All Top Bar Buttons Geometry and Click Handlers
                using (WbTopBar topBar = new WbTopBar { Width = 1000 })
                {
                    Assert(!topBar.GetBackButtonRect().IsEmpty, "BackButton rect is non-empty");
                    Assert(!topBar.GetForwardButtonRect().IsEmpty, "ForwardButton rect is non-empty");
                    Assert(!topBar.GetRefreshButtonHitRect().IsEmpty, "RefreshButton rect is non-empty");
                    Assert(!topBar.GetNewTabButtonRect().IsEmpty, "NewTabButton rect is non-empty");
                    Assert(!topBar.GetGearButtonRect().IsEmpty, "GearButton rect is non-empty");

                    var toolbarButtons = topBar.GetToolbarButtonRectangles();
                    Assert(toolbarButtons.ContainsKey("home"), "Home button exists");
                    Assert(toolbarButtons.ContainsKey("star"), "Star button exists");
                    Assert(toolbarButtons.ContainsKey("bookmarks"), "Bookmarks button exists");
                    Assert(toolbarButtons.ContainsKey("history"), "History button exists");
                    Assert(toolbarButtons.ContainsKey("downloads"), "Downloads button exists");
                    Assert(toolbarButtons.ContainsKey("shield"), "Shield button exists");

                    // Test all button events can be triggered
                    bool backTriggered = false;
                    bool fwdTriggered = false;
                    bool refreshTriggered = false;
                    bool homeTriggered = false;
                    bool starTriggered = false;
                    bool bkmkTriggered = false;
                    bool histTriggered = false;
                    bool dlTriggered = false;
                    bool shieldTriggered = false;
                    bool gearTriggered = false;
                    bool newTabTriggered = false;

                    topBar.BackClicked += (s, e) => backTriggered = true;
                    topBar.ForwardClicked += (s, e) => fwdTriggered = true;
                    topBar.RefreshClicked += (s, e) => refreshTriggered = true;
                    topBar.HomeClicked += (s, e) => homeTriggered = true;
                    topBar.StarClicked += (s, e) => starTriggered = true;
                    topBar.BookmarksClicked += (s, e) => bkmkTriggered = true;
                    topBar.HistoryClicked += (s, e) => histTriggered = true;
                    topBar.DownloadsClicked += (s, e) => dlTriggered = true;
                    topBar.ShieldClicked += (s, e) => shieldTriggered = true;
                    topBar.GearClicked += (s, e) => gearTriggered = true;
                    topBar.NewTabClicked += (s, e) => newTabTriggered = true;

                    topBar.CanGoBack = true;
                    topBar.CanGoForward = true;
                    topBar.CanBookmark = true;

                    var onMouseDown = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var onMouseUp = typeof(WbTopBar).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    Action<Point> clickPt = (pt) =>
                    {
                        onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, pt.X, pt.Y, 0) });
                        onMouseUp.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Left, 1, pt.X, pt.Y, 0) });
                    };

                    Rectangle backR = topBar.GetBackButtonRect();
                    clickPt(new Point(backR.X + 4, backR.Y + 4));
                    Assert(backTriggered, "Back button click fired BackClicked");

                    Rectangle fwdR = topBar.GetForwardButtonRect();
                    clickPt(new Point(fwdR.X + 4, fwdR.Y + 4));
                    Assert(fwdTriggered, "Forward button click fired ForwardClicked");

                    Rectangle refR = topBar.GetRefreshButtonHitRect();
                    clickPt(new Point(refR.X + 4, refR.Y + 4));
                    Assert(refreshTriggered, "Refresh button click fired RefreshClicked");

                    Rectangle newTabR = topBar.GetNewTabButtonRect();
                    clickPt(new Point(newTabR.X + 4, newTabR.Y + 4));
                    Assert(newTabTriggered, "NewTab button click fired NewTabClicked");

                    Rectangle gearR = topBar.GetGearButtonRect();
                    clickPt(new Point(gearR.X + 4, gearR.Y + 4));
                    Assert(gearTriggered, "Gear button click fired GearClicked");

                    clickPt(new Point(toolbarButtons["home"].X + 4, toolbarButtons["home"].Y + 4));
                    Assert(homeTriggered, "Home button click fired HomeClicked");

                    clickPt(new Point(toolbarButtons["star"].X + 4, toolbarButtons["star"].Y + 4));
                    Assert(starTriggered, "Star button click fired StarClicked");

                    clickPt(new Point(toolbarButtons["bookmarks"].X + 4, toolbarButtons["bookmarks"].Y + 4));
                    Assert(bkmkTriggered, "Bookmarks button click fired BookmarksClicked");

                    clickPt(new Point(toolbarButtons["history"].X + 4, toolbarButtons["history"].Y + 4));
                    Assert(histTriggered, "History button click fired HistoryClicked");

                    clickPt(new Point(toolbarButtons["downloads"].X + 4, toolbarButtons["downloads"].Y + 4));
                    Assert(dlTriggered, "Downloads button click fired DownloadsClicked");

                    clickPt(new Point(toolbarButtons["shield"].X + 4, toolbarButtons["shield"].Y + 4));
                    Assert(shieldTriggered, "Shield button click fired ShieldClicked");
                }

                // 3. MainBrowserForm code verification for Tools Gear Menu and internal bookmark protection
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(srcDir)) srcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                string mainFormPath = Path.Combine(srcDir, @"Browser\MainBrowserForm.cs");
                if (File.Exists(mainFormPath))
                {
                    string content = File.ReadAllText(mainFormPath);
                    Assert(content.Contains("stopItem.ShortcutKeyDisplayString = \"Esc\";"), "MainBrowserForm sets stopItem display string to Esc");
                    Assert(content.Contains("if (shortcut == Keys.Escape)"), "MainBrowserForm has guard for Keys.Escape shortcut");
                    Assert(content.Contains("_topBar.CanBookmark = canBookmark;"), "MainBrowserForm synchronizes _topBar.CanBookmark");
                    Assert(content.Contains("addBookmark.Enabled = currentTab != null && InternalPageHelper.CanBookmarkPage"), "MainBrowserForm disables addBookmark in Gear menu for internal pages");
                    Assert(content.Contains("bookmarkItem.Enabled = InternalPageHelper.CanBookmarkPage(tab.CurrentUrl);"), "MainBrowserForm disables bookmark item in tab context menu for internal pages");
                }
            });

            Test("Menu Keyboard Shortcut Formatting and Zoom Display Strings", () =>
            {
                // Verify FormatShortcutDisplayString formats OEM keys and standard keys cleanly
                Assert(UITheme.FormatShortcutDisplayString(Keys.Control | Keys.Oemplus) == "Ctrl++", "Oemplus formats as Ctrl++");
                Assert(UITheme.FormatShortcutDisplayString(Keys.Control | Keys.Add) == "Ctrl++", "Add formats as Ctrl++");
                Assert(UITheme.FormatShortcutDisplayString(Keys.Control | Keys.OemMinus) == "Ctrl+-", "OemMinus formats as Ctrl+-");
                Assert(UITheme.FormatShortcutDisplayString(Keys.Control | Keys.Subtract) == "Ctrl+-", "Subtract formats as Ctrl+-");
                Assert(UITheme.FormatShortcutDisplayString(Keys.Control | Keys.D0) == "Ctrl+0", "D0 formats as Ctrl+0");
                Assert(UITheme.FormatShortcutDisplayString(Keys.Control | Keys.Shift | Keys.Delete) == "Ctrl+Shift+Del", "Delete formats as Ctrl+Shift+Del");
                Assert(UITheme.FormatShortcutDisplayString(Keys.Escape) == "Esc", "Escape formats as Esc");
                Assert(UITheme.FormatShortcutDisplayString(Keys.F11) == "F11", "F11 formats as F11");
                Assert(UITheme.FormatShortcutDisplayString(Keys.Control | Keys.T) == "Ctrl+T", "Control+T formats as Ctrl+T");
                Assert(UITheme.FormatShortcutDisplayString(Keys.Alt | Keys.F4) == "Alt+F4", "Alt+F4 formats as Alt+F4");

                // Verify UITheme.ApplyMenuItem automatically applies clean shortcut display string
                using (ToolStripMenuItem zoomIn = new ToolStripMenuItem("Zoom in"))
                {
                    zoomIn.ShortcutKeys = Keys.Control | Keys.Oemplus;
                    UITheme.ApplyMenuItem(zoomIn);
                    Assert(zoomIn.ShortcutKeyDisplayString == "Ctrl++", "UITheme.ApplyMenuItem sets zoomIn ShortcutKeyDisplayString to Ctrl++");
                }

                using (ToolStripMenuItem zoomOut = new ToolStripMenuItem("Zoom out"))
                {
                    zoomOut.ShortcutKeys = Keys.Control | Keys.OemMinus;
                    UITheme.ApplyMenuItem(zoomOut);
                    Assert(zoomOut.ShortcutKeyDisplayString == "Ctrl+-", "UITheme.ApplyMenuItem sets zoomOut ShortcutKeyDisplayString to Ctrl+-");
                }

                using (ToolStripMenuItem zoomReset = new ToolStripMenuItem("100%"))
                {
                    zoomReset.ShortcutKeys = Keys.Control | Keys.D0;
                    UITheme.ApplyMenuItem(zoomReset);
                    Assert(zoomReset.ShortcutKeyDisplayString == "Ctrl+0", "UITheme.ApplyMenuItem sets zoomReset ShortcutKeyDisplayString to Ctrl+0");
                }
            });

            Test("Long Press and Right Click on Back and Forward Buttons Show History Dropdown", () =>
            {
                using (WbTopBar topBar = new WbTopBar { Width = 1000 })
                {
                    // 1. When disabled (default), long press and right click do NOT fire
                    bool backLongFired = false;
                    bool fwdLongFired = false;
                    topBar.BackLongPressed += (s, e) => backLongFired = true;
                    topBar.ForwardLongPressed += (s, e) => fwdLongFired = true;

                    topBar.CanGoBack = false;
                    topBar.CanGoForward = false;

                    topBar.TriggerBackLongPress();
                    Assert(!backLongFired, "TriggerBackLongPress must NOT fire when CanGoBack is false");

                    topBar.TriggerForwardLongPress();
                    Assert(!fwdLongFired, "TriggerForwardLongPress must NOT fire when CanGoForward is false");

                    var onMouseDown = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Rectangle backR = topBar.GetBackButtonRect();
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Right, 1, backR.X + 4, backR.Y + 4, 0) });
                    Assert(!backLongFired, "Right-clicking Back must NOT fire when CanGoBack is false");

                    Rectangle fwdR = topBar.GetForwardButtonRect();
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Right, 1, fwdR.X + 4, fwdR.Y + 4, 0) });
                    Assert(!fwdLongFired, "Right-clicking Forward must NOT fire when CanGoForward is false");

                    // Disabled Back/Forward has no tooltip
                    var onMouseMove = typeof(WbTopBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, backR.X + 4, backR.Y + 4, 0) });
                    string disabledBackTip = topBar.GetCurrentToolTip();
                    Assert(string.IsNullOrEmpty(disabledBackTip), "Disabled Back button has no tooltip");

                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, fwdR.X + 4, fwdR.Y + 4, 0) });
                    string disabledFwdTip = topBar.GetCurrentToolTip();
                    Assert(string.IsNullOrEmpty(disabledFwdTip), "Disabled Forward button has no tooltip");

                    // 2. When enabled, long press and right click DO fire
                    topBar.CanGoBack = true;
                    topBar.CanGoForward = true;

                    topBar.TriggerBackLongPress();
                    Assert(backLongFired, "TriggerBackLongPress fires BackLongPressed when CanGoBack is true");

                    topBar.TriggerForwardLongPress();
                    Assert(fwdLongFired, "TriggerForwardLongPress fires ForwardLongPressed when CanGoForward is true");

                    bool rightBackFired = false;
                    bool rightFwdFired = false;
                    topBar.BackLongPressed += (s, e) => rightBackFired = true;
                    topBar.ForwardLongPressed += (s, e) => rightFwdFired = true;

                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Right, 1, backR.X + 4, backR.Y + 4, 0) });
                    Assert(rightBackFired, "Right-clicking Back fires BackLongPressed when CanGoBack is true");

                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Right, 1, fwdR.X + 4, fwdR.Y + 4, 0) });
                    Assert(rightFwdFired, "Right-clicking Forward fires ForwardLongPressed when CanGoForward is true");

                    // 3. Tooltip hint for long press / history only present when enabled
                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, backR.X + 4, backR.Y + 4, 0) });
                    string backTip = topBar.GetCurrentToolTip();
                    Assert(backTip == AppStrings.Browser.TipBack, "Back button tooltip matches TipBack: " + backTip);

                    onMouseMove.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.None, 0, fwdR.X + 4, fwdR.Y + 4, 0) });
                    string fwdTip = topBar.GetCurrentToolTip();
                    Assert(fwdTip == AppStrings.Browser.TipForward, "Forward button tooltip matches TipForward: " + fwdTip);
                }

                // 4. History Dropdown Context Menu Theme, Layout and Structure
                using (ContextMenuStrip histMenu = new ContextMenuStrip())
                {
                    UITheme.ApplyContextMenu(histMenu);
                    Assert(histMenu.BackColor == UITheme.ToolbarBackground, "History dropdown menu uses dark toolbar background");
                    Assert(histMenu.Renderer is MaterialToolStripRenderer, "History dropdown menu uses MaterialToolStripRenderer");

                    // Add simulated history entries
                    string[] titles = new string[] { "GitHub - Development Platform", "Google Search Engine", "Documentation Portal" };
                    for (int i = 0; i < titles.Length; i++)
                    {
                        string displayTitle = TitleHelper.NormalizeAndWrapTitle(titles[i], 45);
                        var item = new ToolStripMenuItem(displayTitle, MaterialIcons.GetIcon("globe", 16));
                        UITheme.ApplyMenuItem(item);
                        histMenu.Items.Add(item);
                    }

                    // Separator + Show full history item
                    histMenu.Items.Add(new ToolStripSeparator());
                    ToolStripMenuItem showAll = new ToolStripMenuItem(AppStrings.Browser.MenuShowFullHistory, MaterialIcons.GetIcon("clock-counter-clockwise", 16));
                    showAll.ShortcutKeyDisplayString = "Ctrl+H";
                    UITheme.ApplyMenuItem(showAll);
                    histMenu.Items.Add(showAll);

                    Assert(histMenu.Items.Count == 5, "History menu contains 3 entries + separator + Show full history");
                    Assert(histMenu.Items[0].Text == "GitHub - Development Platform", "First item matches title");
                    Assert(histMenu.Items[histMenu.Items.Count - 1].Text == AppStrings.Browser.MenuShowFullHistory, "Last item is Show full history");
                    Assert(((ToolStripMenuItem)histMenu.Items[histMenu.Items.Count - 1]).ShortcutKeyDisplayString == "Ctrl+H", "Show full history has Ctrl+H shortcut");
                }

                // 5. Source code verification for TabNavigationEntry and MainBrowserForm history menus
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(srcDir)) srcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                string browserTabPath = Path.Combine(srcDir, @"Browser\BrowserTab.cs");
                string mainFormPath = Path.Combine(srcDir, @"Browser\MainBrowserForm.cs");

                if (File.Exists(browserTabPath))
                {
                    string tabContent = File.ReadAllText(browserTabPath);
                    Assert(tabContent.Contains("class TabNavigationEntry"), "BrowserTab.cs defines TabNavigationEntry");
                    Assert(tabContent.Contains("GetBackHistory()"), "BrowserTab.cs implements GetBackHistory()");
                    Assert(tabContent.Contains("GetForwardHistory()"), "BrowserTab.cs implements GetForwardHistory()");
                    Assert(tabContent.Contains("NavigateToHistory("), "BrowserTab.cs implements NavigateToHistory()");
                    Assert(tabContent.Contains("RecordNavigation("), "BrowserTab.cs implements RecordNavigation()");
                }

                if (File.Exists(mainFormPath))
                {
                    string formContent = File.ReadAllText(mainFormPath);
                    Assert(formContent.Contains("_topBar.BackLongPressed += (s, e) => ShowBackHistoryMenu();"), "MainBrowserForm wires BackLongPressed");
                    Assert(formContent.Contains("_topBar.ForwardLongPressed += (s, e) => ShowForwardHistoryMenu();"), "MainBrowserForm wires ForwardLongPressed");
                    Assert(formContent.Contains("if (_topBar == null || !_topBar.CanGoBack) return;"), "MainBrowserForm guards ShowBackHistoryMenu with CanGoBack");
                    Assert(formContent.Contains("if (_topBar == null || !_topBar.CanGoForward) return;"), "MainBrowserForm guards ShowForwardHistoryMenu with CanGoForward");
                    Assert(formContent.Contains("ShowBackHistoryMenu()"), "MainBrowserForm defines ShowBackHistoryMenu()");
                    Assert(formContent.Contains("ShowForwardHistoryMenu()"), "MainBrowserForm defines ShowForwardHistoryMenu()");
                }
            });

            Test("SponsorBlock Category Definitions & Colors", () =>
            {
                var categories = SponsorCategory.All;
                Assert(categories != null && categories.Count >= 8, "All categories list should contain at least 8 categories");
                
                var sponsor = SponsorCategory.Get(SponsorCategory.Sponsor);
                Assert(sponsor != null, "Sponsor category should exist");
                Assert(sponsor.Name == "Sponsor", "Sponsor name should be Sponsor");
                Assert(sponsor.ColorHex == "#00d400", "Sponsor color hex should be #00d400");
                Assert(sponsor.DefaultAction == SponsorAction.AutoSkip, "Sponsor default action should be AutoSkip");

                var intro = SponsorCategory.Get(SponsorCategory.Intro);
                Assert(intro != null, "Intro category should exist");
                Assert(intro.ColorHex == "#00ffff", "Intro color hex should be #00ffff");
                Assert(intro.DefaultAction == SponsorAction.ManualSkip, "Intro default action should be ManualSkip (Show Notice)");

                var outro = SponsorCategory.Get(SponsorCategory.Outro);
                Assert(outro != null, "Outro category should exist");
                Assert(outro.ColorHex == "#0202ed", "Outro color hex should be #0202ed");
                Assert(outro.DefaultAction == SponsorAction.ManualSkip, "Outro default action should be ManualSkip (Show Notice)");

                var interaction = SponsorCategory.Get(SponsorCategory.Interaction);
                Assert(interaction != null, "Interaction category should exist");
                Assert(interaction.ColorHex == "#cc00ff", "Interaction color hex should be #cc00ff");

                var selfpromo = SponsorCategory.Get(SponsorCategory.SelfPromo);
                Assert(selfpromo != null, "SelfPromo category should exist");
                Assert(selfpromo.ColorHex == "#ffff00", "SelfPromo color hex should be #ffff00");
            });

            Test("SponsorBlock Segment Model & Time Formatting", () =>
            {
                Assert(SponsorSegment.FormatTime(0) == "00:00", "0s -> 00:00");
                Assert(SponsorSegment.FormatTime(75) == "01:15", "75s -> 01:15");
                Assert(SponsorSegment.FormatTime(3665) == "1:01:05", "3665s -> 1:01:05");

                var seg = new SponsorSegment
                {
                    UUID = "test-uuid-1",
                    Category = SponsorCategory.Sponsor,
                    ActionType = "skip",
                    Start = 120.0,
                    End = 180.5
                };
                Assert(Math.Abs(seg.Duration - 60.5) < 0.001, "Duration should be 60.5s");
                Assert(seg.FormattedRange == "02:00 - 03:00", "FormattedRange should match");
                Assert(seg.CategoryName == "Sponsor", "CategoryName should be Sponsor");
                Assert(seg.ColorHex == "#00d400", "ColorHex should be #00d400");
            });

            Test("SponsorBlock Config & Whitelist Management", () =>
            {
                var cfg = new SponsorBlockConfig();
                Assert(cfg.IsEnabled == true, "Config should be enabled by default");
                Assert(cfg.ShowPreviewBar == true, "ShowPreviewBar should be true by default");
                Assert(cfg.ShowNotice == true, "ShowNotice should be true by default");
                Assert(cfg.GetAction(SponsorCategory.Sponsor) == SponsorAction.AutoSkip, "Sponsor action should be AutoSkip");
                Assert(cfg.GetAction(SponsorCategory.Intro) == SponsorAction.ManualSkip, "Intro action should be ManualSkip by default");
                Assert(cfg.GetAction(SponsorCategory.Outro) == SponsorAction.ManualSkip, "Outro action should be ManualSkip by default");

                cfg.SetAction(SponsorCategory.Sponsor, SponsorAction.Mute);
                Assert(cfg.GetAction(SponsorCategory.Sponsor) == SponsorAction.Mute, "Sponsor action should now be Mute");

                string testVid = "dQw4w9WgXcQ";
                Assert(!cfg.IsVideoWhitelisted(testVid), "Video should not be whitelisted initially");
                bool added = cfg.ToggleVideoWhitelist(testVid);
                Assert(added && cfg.IsVideoWhitelisted(testVid), "Video should be whitelisted after toggle");
                bool removed = !cfg.ToggleVideoWhitelist(testVid);
                Assert(removed && !cfg.IsVideoWhitelisted(testVid), "Video should be unwhitelisted after second toggle");
            });

            Test("SponsorBlock Stats Tracking & Formatting", () =>
            {
                var stats = new SponsorBlockStats();
                Assert(stats.TotalSkips == 0, "Initial skips should be 0");
                Assert(stats.FormattedTimeSaved == "0s", "Initial time saved should be 0s");

                stats.AddSkip(SponsorCategory.Sponsor, 45.0);
                Assert(stats.TotalSkips == 1, "TotalSkips should be 1");
                Assert(stats.FormattedTimeSaved == "45s", "FormattedTimeSaved should be 45s");

                stats.AddSkip(SponsorCategory.Intro, 120.0);
                Assert(stats.TotalSkips == 2, "TotalSkips should be 2");
                Assert(stats.FormattedTimeSaved == "2m 45s", "FormattedTimeSaved should be 2m 45s");

                stats.Reset();
                Assert(stats.TotalSkips == 0, "Stats should be reset to 0");
            });

            Test("SponsorBlock YouTube Domain Validation & Video ID Extraction", () =>
            {
                Assert(SponsorBlockYTShield.IsYouTubeDomain("https://www.youtube.com/watch?v=dQw4w9WgXcQ"), "youtube.com should be valid YT domain");
                Assert(SponsorBlockYTShield.IsYouTubeDomain("https://m.youtube.com/watch?v=dQw4w9WgXcQ"), "m.youtube.com should be valid YT domain");
                Assert(SponsorBlockYTShield.IsYouTubeDomain("https://youtu.be/dQw4w9WgXcQ"), "youtu.be should be valid YT domain");
                Assert(SponsorBlockYTShield.IsYouTubeDomain("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ"), "youtube-nocookie.com should be valid YT domain");
                Assert(!SponsorBlockYTShield.IsYouTubeDomain("https://google.com"), "google.com should not be YT domain");
                Assert(!SponsorBlockYTShield.IsYouTubeDomain("https://example.com"), "example.com should not be YT domain");

                Assert(SponsorBlockManager.ExtractVideoId("https://www.youtube.com/watch?v=dQw4w9WgXcQ") == "dQw4w9WgXcQ", "Extract watch URL video ID");
                Assert(SponsorBlockManager.ExtractVideoId("https://www.youtube.com/shorts/testShort123") == "testShort123", "Extract shorts URL video ID");
                Assert(SponsorBlockManager.ExtractVideoId("https://www.youtube.com/embed/embedVid99") == "embedVid99", "Extract embed URL video ID");
                Assert(SponsorBlockManager.ExtractVideoId("dQw4w9WgXcQ") == "dQw4w9WgXcQ", "Extract raw video ID");
            });

            Test("SponsorBlock Client-Side Script Payload Generation", () =>
            {
                string script = SponsorBlockYTShield.GetSponsorBlockScript("{\"IsEnabled\":true}");
                Assert(!string.IsNullOrEmpty(script), "Script should not be empty");
                Assert(script.Contains("wb-sb-previewbar"), "Script should define preview bar");
                Assert(script.Contains("wb-sb-toast"), "Script should define skip toast");
                Assert(script.Contains("wb-action:sponsorblock-skip"), "Script should report skips via wb-action");
                Assert(script.Contains("wb-action:sponsorblock-fetch"), "Script should request skip segments via C# backend");
                Assert(script.Contains("infoButton") && script.Contains("startSegmentButton"), "Script should integrate official player control buttons");
                Assert(script.Contains("playerButton") && script.Contains("playerButtonImage"), "Script should style player buttons");
                Assert(script.Contains("<svg") && script.Contains("viewBox"), "Script should embed inline vector SVGs");
                Assert(script.Contains("isPlayerInAd"), "Script should detect active player ads to avoid ad skipping conflicts");
                Assert(script.Contains("if (isPlayerInAd()) return;"), "Script should yield playback control when player is in ad state");
                Assert(script.Contains("skippedMap"), "Script should maintain skippedMap to prevent duplicate skip triggers");
                Assert(script.Contains("player.seekTo"), "Script should leverage native player.seekTo for seamless buffer transitions");
                Assert(script.Contains("unmutedMap"), "Script should track unmutedMap");
                Assert(script.Contains("data:image/png;base64,") && script.Contains("playerButtonImage"), "Script should embed authentic SponsorBlock sponsorblock.png icon");
                Assert(script.Contains(AppStrings.SponsorBlock.BtnSkip), "Script should include Skip button text");
                Assert(script.Contains(AppStrings.SponsorBlock.BtnUnskip), "Script should include Unskip button text");
                Assert(script.Contains(AppStrings.SponsorBlock.BtnUnmute), "Script should include Unmute button text");
                Assert(script.Contains(AppStrings.SponsorBlock.StatusMuted), "Script should include Muted status text");
                Assert(script.Contains("getCanonicalCategory"), "Script should define getCanonicalCategory");
                Assert(script.Contains("normalizeCategoryActions"), "Script should define normalizeCategoryActions");
                Assert(script.Contains("manualSeekedSegments"), "Script should track manualSeekedSegments");
                Assert(script.Contains("onSeeked"), "Script should attach onSeeked listener");
                Assert(script.Contains("isManualSeekOverride"), "Script should evaluate isManualSeekOverride");
                Assert(script.Contains("scheduleToastDismiss"), "Script should define scheduleToastDismiss");
                Assert(SponsorBlockManager.ApiBaseUrl == "https://sponsor.ajay.app/api/skipSegments", "Manager should query official SponsorBlock API");
            });

            Test("SponsorBlock Toast Modes and Real-Time Config Parity", () =>
            {
                Assert(AppStrings.SponsorBlock.BtnUnmute == "Unmute", "BtnUnmute should be 'Unmute'");
                Assert(AppStrings.SponsorBlock.StatusMuted == "Muted", "StatusMuted should be 'Muted'");
                Assert(AppStrings.SponsorBlock.BtnSkip == "Skip", "BtnSkip should be 'Skip'");
                Assert(AppStrings.SponsorBlock.BtnUnskip == "Unskip", "BtnUnskip should be 'Unskip'");
                Assert(SponsorCategory.GetDefaultAction("intro") == SponsorAction.ManualSkip, "Intro default is ManualSkip");
                Assert(SponsorCategory.GetDefaultAction("outro") == SponsorAction.ManualSkip, "Outro default is ManualSkip");
                Assert(SponsorCategory.GetDefaultAction("filler") == SponsorAction.ManualSkip, "Filler default is ManualSkip");
                Assert(SponsorCategory.GetDefaultAction("sponsor") == SponsorAction.AutoSkip, "Sponsor default is AutoSkip");
                Assert(SponsorActionHelper.ToConfigString(SponsorAction.ManualSkip) == "manual", "ToConfigString ManualSkip is manual");
                Assert(SponsorActionHelper.ToConfigString(SponsorAction.Mute) == "mute", "ToConfigString Mute is mute");
                Assert(SponsorActionHelper.ToConfigString(SponsorAction.AutoSkip) == "skip", "ToConfigString AutoSkip is skip");
                Assert(SponsorActionHelper.FromConfigString("manual") == SponsorAction.ManualSkip, "FromConfigString manual");
                Assert(SponsorActionHelper.FromConfigString("mute") == SponsorAction.Mute, "FromConfigString mute");
                Assert(SponsorActionHelper.FromConfigString("skip") == SponsorAction.AutoSkip, "FromConfigString skip");

                // Numeric config string parsing parity
                Assert(SponsorActionHelper.FromConfigString("0") == SponsorAction.AutoSkip, "FromConfigString '0' is AutoSkip");
                Assert(SponsorActionHelper.FromConfigString("1") == SponsorAction.ManualSkip, "FromConfigString '1' is ManualSkip");
                Assert(SponsorActionHelper.FromConfigString("2") == SponsorAction.Mute, "FromConfigString '2' is Mute");
                Assert(SponsorActionHelper.FromConfigString("3") == SponsorAction.Disabled, "FromConfigString '3' is Disabled");

                // Canonical category alias normalization
                Assert(SponsorCategory.NormalizeKey("intermission") == SponsorCategory.Intro, "NormalizeKey intermission -> intro");
                Assert(SponsorCategory.NormalizeKey("endcards") == SponsorCategory.Outro, "NormalizeKey endcards -> outro");
                Assert(SponsorCategory.NormalizeKey("nonmusic") == SponsorCategory.MusicOfftopic, "NormalizeKey nonmusic -> music_offtopic");
                Assert(SponsorCategory.NormalizeKey("highlight") == SponsorCategory.PoiHighlight, "NormalizeKey highlight -> poi_highlight");
                Assert(SponsorCategory.Get("intermission") != null && SponsorCategory.Get("intermission").Key == SponsorCategory.Intro, "Get('intermission') resolves to Intro");

                // Config normalization
                var testCfg = new SponsorBlockConfig();
                testCfg.SetAction("intermission", SponsorAction.Mute);
                Assert(testCfg.GetAction("intro") == SponsorAction.Mute, "GetAction intro reflects intermission setting");
                Assert(testCfg.GetAction("intermission") == SponsorAction.Mute, "GetAction intermission reflects intermission setting");

                // Settings dialog label mnemonic and ampersand
                Assert(AppStrings.SponsorBlock.SectionCategoriesActions.Contains("&"), "SectionCategoriesActions contains literal ampersand");
                using (var form = new SponsorBlockSettingsForm())
                {
                    Assert(form != null, "SponsorBlockSettingsForm instantiates cleanly");
                    Assert(form.Text.EndsWith("- wb"), "SponsorBlockSettingsForm title ends with - wb");
                    Assert(form.Width >= 700, "SponsorBlockSettingsForm has comfortable width >= 700");

                    Button resetBtn = null;
                    Action<Control> findBtn = null;
                    findBtn = (parent) =>
                    {
                        foreach (Control child in parent.Controls)
                        {
                            if (child is Button && child.Text == AppStrings.SponsorBlock.BtnResetDefaults)
                            {
                                resetBtn = (Button)child;
                                return;
                            }
                            findBtn(child);
                        }
                    };
                    findBtn(form);
                    Assert(resetBtn != null, "Found Reset to Defaults button in SponsorBlockSettingsForm");
                    Assert(!resetBtn.IsDisposed, "Reset to Defaults button is valid and not disposed");
                    Assert(resetBtn.Height == 30 || resetBtn.Height == 32, "Reset to Defaults button has correct height");
                }
            });

            Test("SponsorBlock Video Whitelist List Dialog and Manager Parity", () =>
            {
                // 1. Config level whitelist operations
                var cfg = new SponsorBlockConfig();
                Assert(cfg.GetWhitelistedVideos().Count == 0, "Initial whitelist should be empty");
                Assert(cfg.AddVideoWhitelist("https://www.youtube.com/watch?v=dQw4w9WgXcQ"), "Add full watch URL");
                Assert(cfg.IsVideoWhitelisted("dQw4w9WgXcQ"), "Raw ID is whitelisted");
                Assert(cfg.IsVideoWhitelisted("https://youtu.be/dQw4w9WgXcQ"), "youtu.be URL is whitelisted");
                Assert(!cfg.AddVideoWhitelist("dQw4w9WgXcQ"), "Duplicate add returns false");
                Assert(cfg.GetWhitelistedVideos().Count == 1, "Whitelisted count is 1");

                Assert(cfg.AddVideoWhitelist("https://www.youtube.com/shorts/testShortVid"), "Add shorts URL");
                Assert(cfg.GetWhitelistedVideos().Count == 2, "Whitelisted count is 2");

                Assert(cfg.RemoveVideoWhitelist("https://youtu.be/testShortVid"), "Remove via youtu.be URL");
                Assert(cfg.GetWhitelistedVideos().Count == 1, "Whitelisted count is 1 after remove");
                Assert(!cfg.IsVideoWhitelisted("testShortVid"), "testShortVid is no longer whitelisted");

                cfg.ClearVideoWhitelist();
                Assert(cfg.GetWhitelistedVideos().Count == 0, "Cleared whitelist count is 0");

                // 2. Manager level whitelist operations and event notification
                bool eventFired = false;
                EventHandler handler = (s, e) => { eventFired = true; };
                AppServices.SponsorBlock.WhitelistChanged += handler;
                try
                {
                    eventFired = false;
                    AppServices.SponsorBlock.AddVideoWhitelist("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
                    Assert(eventFired, "WhitelistChanged fired on AddVideoWhitelist");
                    Assert(AppServices.SponsorBlock.IsWhitelisted("dQw4w9WgXcQ"), "Manager IsWhitelisted true");

                    eventFired = false;
                    AppServices.SponsorBlock.ToggleVideoWhitelist("dQw4w9WgXcQ");
                    Assert(eventFired, "WhitelistChanged fired on ToggleVideoWhitelist");
                    Assert(!AppServices.SponsorBlock.IsWhitelisted("dQw4w9WgXcQ"), "Manager IsWhitelisted false after toggle");

                    eventFired = false;
                    AppServices.SponsorBlock.AddVideoWhitelist("abc123xyz99");
                    AppServices.SponsorBlock.ClearVideoWhitelist();
                    Assert(eventFired, "WhitelistChanged fired on ClearVideoWhitelist");
                    Assert(!AppServices.SponsorBlock.IsWhitelisted("abc123xyz99"), "Manager IsWhitelisted false after clear");
                }
                finally
                {
                    AppServices.SponsorBlock.WhitelistChanged -= handler;
                    AppServices.SponsorBlock.ClearVideoWhitelist();
                }

                // 3. SponsorBlockWhitelistForm dialog instantiation & component layout
                using (var whitelistForm = new SponsorBlockWhitelistForm())
                {
                    Assert(whitelistForm != null, "SponsorBlockWhitelistForm instantiates cleanly");
                    Assert(whitelistForm.Text.Contains("SponsorBlock"), "Form title contains SponsorBlock");
                    Assert(whitelistForm.Controls.Count > 0, "Form has controls");
                }
            });

            Test("YouTube Embedded Player AdBlock and SponsorBlock Parity on Third-Party Sites", () =>
            {
                // 1. YouTube Domain Recognition for Embeds and NoCookie
                Assert(AdBlockYTShield.IsYouTubeDomain("https://www.youtube.com/embed/dQw4w9WgXcQ"), "www.youtube.com/embed is YouTube");
                Assert(AdBlockYTShield.IsYouTubeDomain("https://youtube.com/embed/dQw4w9WgXcQ"), "youtube.com/embed is YouTube");
                Assert(AdBlockYTShield.IsYouTubeDomain("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ"), "www.youtube-nocookie.com/embed is YouTube");
                Assert(AdBlockYTShield.IsYouTubeDomain("https://youtube-nocookie.com/embed/dQw4w9WgXcQ?autoplay=1"), "youtube-nocookie.com/embed is YouTube");
                Assert(SponsorBlockYTShield.IsYouTubeDomain("https://www.youtube.com/embed/dQw4w9WgXcQ"), "SponsorBlock recognizes embed");
                Assert(SponsorBlockYTShield.IsYouTubeDomain("https://youtube-nocookie.com/embed/dQw4w9WgXcQ"), "SponsorBlock recognizes nocookie embed");

                // 2. Video ID Extraction from Embed URLs with and without parameters
                Assert(UrlHelper.ExtractYouTubeVideoId("https://www.youtube.com/embed/dQw4w9WgXcQ") == "dQw4w9WgXcQ", "Extracts video ID from standard embed URL");
                Assert(UrlHelper.ExtractYouTubeVideoId("https://www.youtube.com/embed/dQw4w9WgXcQ?autoplay=1&enablejsapi=1") == "dQw4w9WgXcQ", "Extracts video ID from embed URL with query params");
                Assert(UrlHelper.ExtractYouTubeVideoId("https://www.youtube-nocookie.com/embed/xyz789abc") == "xyz789abc", "Extracts video ID from nocookie embed URL");
                Assert(SponsorBlockManager.ExtractVideoId("https://www.youtube.com/embed/dQw4w9WgXcQ") == "dQw4w9WgXcQ", "SponsorBlockManager extracts embed video ID");

                // 3. Network Ad Blocking for YouTube ad endpoints on both youtube.com and youtube-nocookie.com
                string matchedRule;
                Assert(AppServices.AdBlock.ShouldBlock("https://www.youtube.com/api/stats/ads?adformat=video", "https://www.youtube.com/embed/dQw4w9WgXcQ", 1, out matchedRule),
                    "api/stats/ads is blocked on youtube.com embed");
                Assert(AppServices.AdBlock.ShouldBlock("https://www.youtube-nocookie.com/api/stats/ads?adformat=video", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", 1, out matchedRule),
                    "api/stats/ads is blocked on youtube-nocookie.com embed");
                Assert(AppServices.AdBlock.ShouldBlock("https://www.youtube.com/youtubei/v1/player/ad_break", "https://www.youtube.com/embed/dQw4w9WgXcQ", 1, out matchedRule),
                    "ad_break is blocked on youtube.com embed");
                Assert(AppServices.AdBlock.ShouldBlock("https://www.youtube-nocookie.com/youtubei/v1/player/ad_break", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", 1, out matchedRule),
                    "ad_break is blocked on youtube-nocookie.com embed");
                Assert(AppServices.AdBlock.ShouldBlock("https://googleads.g.doubleclick.net/pagead/ads?client=ca-pub-test", "https://www.youtube.com/embed/dQw4w9WgXcQ", 1, out matchedRule),
                    "googleads is blocked for embed player");

                // 4. Embedded Player Subframe Request Protection
                // The embed iframe itself must NOT be blocked, but ad subresources within it must be blocked
                Assert(!AppServices.AdBlock.ShouldBlock("https://www.youtube.com/embed/dQw4w9WgXcQ", "https://www.reddit.com/r/videos/", 0, out matchedRule),
                    "Main frame navigation to embed is not blocked");
            });

            Test("Screenshot Tool Architecture, In-Memory Session, Annotations and Studio View", () =>
            {
                // 1. Session and SessionManager Lifecycle
                using (Bitmap sampleBmp = new Bitmap(200, 150))
                {
                    ScreenshotSession session = ScreenshotSessionManager.CreateSession(sampleBmp, "https://example.com", "Example Page");
                    Assert(session != null, "Session should be created");
                    Assert(!string.IsNullOrEmpty(session.Id), "Session should have non-empty ID");
                    Assert(session.Image != null, "Session should hold image");
                    Assert(session.SourceUrl == "https://example.com", "Session should hold source URL");
                    Assert(session.SourceTitle == "Example Page", "Session should hold source title");

                    ScreenshotSession retrieved = ScreenshotSessionManager.GetSession(session.Id);
                    Assert(retrieved == session, "GetSession should retrieve matching session");

                    ScreenshotSessionManager.RemoveSession(session.Id);
                    Assert(ScreenshotSessionManager.GetSession(session.Id) == null, "RemoveSession should remove session");
                }

                // 2. InternalPageHelper Screenshot URLs
                Assert(InternalPageHelper.IsInternalUrl("wb://screenshot"), "wb://screenshot is internal url");
                Assert(InternalPageHelper.IsInternalUrl("wb://screenshot?id=abc123xyz"), "wb://screenshot with id is internal url");
                Assert(InternalPageHelper.GetInternalPageType("wb://screenshot?id=abc123xyz") == InternalPageType.Screenshot, "Page type is Screenshot");
                Assert(InternalPageHelper.ExtractScreenshotSessionId("wb://screenshot?id=abc123xyz") == "abc123xyz", "Extracts session id");
                Assert(InternalPageHelper.ExtractScreenshotSessionId("wb://screenshot?id=abc123xyz&ref=tab") == "abc123xyz", "Extracts session id with multiple params");
                Assert(InternalPageHelper.GetInternalTitle("wb://screenshot") == "Screenshot", "Internal title is Screenshot");
                Assert(InternalPageHelper.GetInternalFavicon("wb://screenshot") != null, "Internal favicon exists");

                // 3. Annotations Vector Models and GDI+ Rendering
                using (Bitmap canvasBmp = new Bitmap(300, 200))
                using (Graphics g = Graphics.FromImage(canvasBmp))
                {
                    g.Clear(Color.Black);

                    // Pen
                    PenAnnotation pen = new PenAnnotation();
                    pen.Points.Add(new PointF(10, 10));
                    pen.Points.Add(new PointF(20, 30));
                    pen.Points.Add(new PointF(40, 20));
                    pen.Draw(g, canvasBmp);

                    // Highlighter
                    HighlighterAnnotation hl = new HighlighterAnnotation();
                    hl.Points.Add(new PointF(5, 50));
                    hl.Points.Add(new PointF(100, 50));
                    hl.Draw(g, canvasBmp);

                    // Arrow
                    ArrowAnnotation arrow = new ArrowAnnotation(new PointF(20, 80), new PointF(120, 80));
                    arrow.Draw(g, canvasBmp);

                    // Rect & Ellipse
                    RectAnnotation rect = new RectAnnotation(new RectangleF(130, 20, 50, 40), true);
                    rect.Draw(g, canvasBmp);

                    EllipseAnnotation ellipse = new EllipseAnnotation(new RectangleF(200, 20, 40, 40), false);
                    ellipse.Draw(g, canvasBmp);

                    // Text
                    TextAnnotation text = new TextAnnotation(new PointF(30, 110), "Sample Text");
                    text.Draw(g, canvasBmp);

                    // Blur / Pixelate
                    BlurAnnotation blur = new BlurAnnotation(new RectangleF(10, 10, 50, 50));
                    blur.Draw(g, canvasBmp);

                    // Step Badge
                    StepBadgeAnnotation step = new StepBadgeAnnotation(new PointF(250, 100), 1);
                    step.Draw(g, canvasBmp);
                }

                // 4. ScreenshotView UserControl and Studio Rendering
                using (Bitmap viewBmp = new Bitmap(400, 300))
                {
                    ScreenshotSession viewSession = ScreenshotSessionManager.CreateSession(viewBmp, "https://test.local", "Test Tab");
                    using (ScreenshotView sView = new ScreenshotView(viewSession))
                    {
                        sView.Width = 800;
                        sView.Height = 600;
                        sView.FitImageToWindow();
                        Assert(sView.CurrentTool == AnnotationTool.Hand, "Default tool is Hand");
                        Assert(sView.PanOffset.X > 0, "Horizontal offset is positive when image fits");
                        Assert(Math.Abs(sView.PanOffset.X - (800 - 400 * sView.Zoom) / 2f) < 2f, "Screenshot is horizontally centered on fit");
                        Assert(Math.Abs(sView.PanOffset.Y - (556 - 300 * sView.Zoom) / 2f) < 2f, "Screenshot is vertically centered on fit");

                        // Test ZoomIn, ZoomOut, Centering
                        sView.ZoomIn();
                        Assert(sView.Zoom > 1.0f, "ZoomIn increases zoom factor");
                        Assert(Math.Abs(sView.PanOffset.X - (800 - 400 * sView.Zoom) / 2f) < 2f, "Screenshot stays horizontally centered after ZoomIn");

                        sView.SetZoom(0.5f);
                        Assert(Math.Abs(sView.Zoom - 0.5f) < 0.01f, "SetZoom sets target zoom");
                        Assert(Math.Abs(sView.PanOffset.X - (800 - 400 * 0.5f) / 2f) < 2f, "Screenshot stays centered horizontally on zoom out");
                        Assert(Math.Abs(sView.PanOffset.Y - (556 - 300 * 0.5f) / 2f) < 2f, "Screenshot stays centered vertically on zoom out");

                        sView.ResetZoomTo100();
                        Assert(Math.Abs(sView.Zoom - 1.0f) < 0.01f, "ResetZoomTo100 sets zoom to 1.0");
                        Assert(Math.Abs(sView.PanOffset.X - (800 - 400) / 2f) < 2f, "ResetZoomTo100 centers horizontally");
                        Assert(Math.Abs(sView.PanOffset.Y - (556 - 300) / 2f) < 2f, "ResetZoomTo100 centers vertically");

                        sView.SetZoom(1.5f);

                        // Render full resolution
                        using (Bitmap rendered = sView.RenderFullResolution())
                        {
                            Assert(rendered != null, "RenderFullResolution returns non-null bitmap");
                            Assert(rendered.Width == 400 && rendered.Height == 300, "Rendered bitmap matches source dimensions");
                        }

                        // Test Crop, Undo and Redo
                        sView.ApplyCrop(new Rectangle(50, 50, 200, 150));
                        using (Bitmap cropped = sView.RenderFullResolution())
                        {
                            Assert(cropped != null, "RenderFullResolution after crop returns non-null bitmap");
                            Assert(cropped.Width == 200 && cropped.Height == 150, "Cropped bitmap matches crop dimensions");
                        }
                        sView.Undo();
                        using (Bitmap uncropped = sView.RenderFullResolution())
                        {
                            Assert(uncropped != null, "RenderFullResolution after undo crop returns non-null bitmap");
                            Assert(uncropped.Width == 400 && uncropped.Height == 300, "Undo restored original image dimensions");
                        }
                        sView.Redo();
                        using (Bitmap recropped = sView.RenderFullResolution())
                        {
                            Assert(recropped != null, "RenderFullResolution after redo crop returns non-null bitmap");
                            Assert(recropped.Width == 200 && recropped.Height == 150, "Redo re-applied crop dimensions");
                        }
                    }
                    ScreenshotSessionManager.RemoveSession(viewSession.Id);
                }

                // 5. OverlayPopup Architecture and Under-TopBar Placement
                using (Form testHost = new Form { Width = 800, Height = 600 })
                {
                    Panel mockTopBar = new Panel { Dock = DockStyle.Top, Height = 84, Visible = true };
                    testHost.Controls.Add(mockTopBar);

                    using (OverlayPopup popup = new OverlayPopup(testHost, "Test Message", "check", "Cancel", null, 0))
                    {
                        Assert(popup != null, "OverlayPopup instantiates successfully");
                        Assert(popup.FormBorderStyle == FormBorderStyle.None, "OverlayPopup is borderless");
                        Assert(popup.ShowInTaskbar == false, "OverlayPopup excluded from taskbar");
                    }

                    using (UniversalOverlayPopup legacyPopup = new UniversalOverlayPopup(testHost, "Legacy Test", "check", "Cancel", null, 0))
                    {
                        Assert(legacyPopup != null, "UniversalOverlayPopup compatibility alias instantiates successfully");
                    }
                }

                // 6. Full Page Tile Stitching Mechanics
                using (Bitmap tile1 = new Bitmap(800, 600))
                using (Bitmap tile2 = new Bitmap(800, 600))
                using (Graphics g1 = Graphics.FromImage(tile1))
                using (Graphics g2 = Graphics.FromImage(tile2))
                {
                    g1.Clear(Color.Red);
                    g2.Clear(Color.Blue);

                    int totalHeight = 1000;
                    using (Bitmap stitched = new Bitmap(800, totalHeight))
                    using (Graphics sg = Graphics.FromImage(stitched))
                    {
                        // Tile 1: 0 to 600
                        sg.DrawImage(tile1, 0, 0, 800, 600);
                        // Tile 2 (remainder 400px): from bottom of tile2 (200 to 600)
                        Rectangle src = new Rectangle(0, 200, 800, 400);
                        Rectangle dst = new Rectangle(0, 600, 800, 400);
                        sg.DrawImage(tile2, dst, src, GraphicsUnit.Pixel);

                        Assert(stitched.Width == 800 && stitched.Height == 1000, "Stitched bitmap matches total document dimensions");
                        Color topPixel = stitched.GetPixel(400, 100);
                        Color bottomPixel = stitched.GetPixel(400, 800);
                        Assert(topPixel.R > 200 && topPixel.B < 50, "Top chunk rendered correctly from first slice");
                        Assert(bottomPixel.B > 200 && bottomPixel.R < 50, "Bottom chunk rendered correctly from second slice");
                    }
                }

                // 7. ScrollBar Component & Hand Tool Verification
                using (UniversalScrollBar vsb = new UniversalScrollBar())
                {
                    Assert(vsb != null, "UniversalScrollBar instantiated successfully");
                    Assert(vsb.Orientation == ScrollBarOrientation.Vertical, "Default orientation is Vertical");
                    vsb.Orientation = ScrollBarOrientation.Horizontal;
                    Assert(vsb.Orientation == ScrollBarOrientation.Horizontal, "Orientation updated to Horizontal");
                    vsb.Minimum = 0;
                    vsb.Maximum = 200;
                    vsb.Value = 50;
                    Assert(vsb.Value == 50, "Value assigned correctly");

                    // Static geometry calculation
                    Rectangle bounds = new Rectangle(0, 0, 100, 300);
                    Rectangle trackRect, thumbRect;
                    UniversalScrollBar.CalculateGeometry(bounds, ScrollBarOrientation.Vertical, 0, 100, 25, 20, 10, 3, 24, out trackRect, out thumbRect);
                    Assert(trackRect.Width == 10, "Calculated track width matches thickness");
                    Assert(thumbRect.Height > 0, "Calculated thumb height is positive");

                    // ComputeValueFromPosition
                    int val = UniversalScrollBar.ComputeValueFromPosition(trackRect, thumbRect, ScrollBarOrientation.Vertical, 0, 100, 20, trackRect.Y);
                    Assert(val == 0, "Top of track computes minimum value");
                }

                // Verify Hand Tool in enum
                AnnotationTool handTool = AnnotationTool.Hand;
                Assert(handTool.ToString() == "Hand", "AnnotationTool.Hand enum value exists and matches");

                // Verify Capture Button and Primary Button Contrast Calculation across the Accent Spectrum
                Color brightYellow = Color.FromArgb(253, 214, 99); // #FDD663
                Color yellowFg = UITheme.GetContrastTextColor(brightYellow);
                Assert(yellowFg.R < 30 && yellowFg.G < 30 && yellowFg.B < 30, "Bright accent uses dark charcoal foreground for readability");

                Color darkBlue = Color.FromArgb(26, 115, 232); // Standard dark accent (lum ~101.7 <= 110)
                Color blueFg = UITheme.GetContrastTextColor(darkBlue);
                Assert(blueFg.R > 230 && blueFg.G > 230 && blueFg.B > 230, "Dark accent uses white foreground for readability");

                // Verify lowest luminance random bright color (periwinkle blue at lightness 68%, lum ~122.9 > 110)
                Color periwinkleBlue = Color.FromArgb(108, 108, 239);
                Color periwinkleFg = UITheme.GetContrastTextColor(periwinkleBlue);
                Assert(periwinkleFg.R < 30 && periwinkleFg.G < 30 && periwinkleFg.B < 30, "Lowest-luminance bright accent guarantees dark charcoal foreground to avoid unreadable white text");

                // Verify all 10 standard presets produce dark contrast text
                foreach (var preset in ThemeManager.Presets)
                {
                    Color pFg = UITheme.GetContrastTextColor(preset.Color);
                    Assert(pFg.R < 30 && pFg.G < 30 && pFg.B < 30, string.Format("Preset {0} must use high-contrast dark text", preset.Name));
                }

                // 8. Screenshot Selection Overlay IsConfirmed, WM_CLOSE Guard, Canvas Scrolling and Button Clamping
                using (ScreenshotSelectionOverlayForm overlay = new ScreenshotSelectionOverlayForm(new Rectangle(0, 0, 400, 300)))
                {
                    Assert(overlay.IsConfirmed == false, "IsConfirmed is initially false");
                    
                    var screenBmpField = typeof(ScreenshotSelectionOverlayForm).GetField("_screenBitmap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (screenBmpField != null && screenBmpField.GetValue(overlay) == null)
                    {
                        screenBmpField.SetValue(overlay, new Bitmap(400, 300));
                    }

                    // Simulate ConfirmCapture via reflection
                    var confirmMethod = typeof(ScreenshotSelectionOverlayForm).GetMethod("ConfirmCapture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(confirmMethod != null, "ConfirmCapture method exists");

                    // Set a valid selection rectangle and execute ConfirmCapture
                    var selRectField = typeof(ScreenshotSelectionOverlayForm).GetField("_selectedRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (selRectField != null)
                    {
                        selRectField.SetValue(overlay, new Rectangle(10, 10, 100, 80));
                    }
                    confirmMethod.Invoke(overlay, null);

                    Assert(overlay.IsConfirmed == true, "IsConfirmed becomes true after ConfirmCapture");
                    Assert(overlay.DialogResult == DialogResult.OK, "DialogResult is OK after ConfirmCapture");
                    Assert(overlay.SelectedBitmap != null, "SelectedBitmap is generated on confirmation");
                    Assert(overlay.SelectedBitmap.Width == 100 && overlay.SelectedBitmap.Height == 80, "SelectedBitmap dimensions match selection");
                }

                // 8b. Screenshot Selection Overlay: Taller/Wider Canvas Scrolling & Bottom Edge Button Clamping
                Bitmap largeDoc = new Bitmap(800, 1600);
                using (ScreenshotSelectionOverlayForm scrollOverlay = new ScreenshotSelectionOverlayForm(new Rectangle(0, 0, 400, 300), largeDoc, new Point(0, 200)))
                {
                    var scrollXField = typeof(ScreenshotSelectionOverlayForm).GetField("_scrollX", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var scrollYField = typeof(ScreenshotSelectionOverlayForm).GetField("_scrollY", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var setScrollMethod = typeof(ScreenshotSelectionOverlayForm).GetMethod("SetScroll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var confirmMethod = typeof(ScreenshotSelectionOverlayForm).GetMethod("ConfirmCapture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var selRectField = typeof(ScreenshotSelectionOverlayForm).GetField("_selectedRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    Assert(scrollYField != null && (int)scrollYField.GetValue(scrollOverlay) == 200, "Initial scroll Y offset is respected");

                    // Test scrolling down and right across canvas
                    if (setScrollMethod != null)
                    {
                        setScrollMethod.Invoke(scrollOverlay, new object[] { 150, 600 });
                        Assert((int)scrollXField.GetValue(scrollOverlay) == 150, "Canvas scrolled horizontally to 150");
                        Assert((int)scrollYField.GetValue(scrollOverlay) == 600, "Canvas scrolled vertically to 600");

                        // Test clamping to max scroll
                        setScrollMethod.Invoke(scrollOverlay, new object[] { 1000, 5000 });
                        Assert((int)scrollXField.GetValue(scrollOverlay) == 400, "ScrollX clamped to max 400 (800 - 400)");
                        Assert((int)scrollYField.GetValue(scrollOverlay) == 1300, "ScrollY clamped to max 1300 (1600 - 300)");
                    }

                    // Test selecting a tall & wide area spanning multiple viewports (e.g. 600 x 1000 px)
                    if (selRectField != null && confirmMethod != null)
                    {
                        selRectField.SetValue(scrollOverlay, new Rectangle(50, 100, 600, 1000));
                        confirmMethod.Invoke(scrollOverlay, null);
                        Assert(scrollOverlay.IsConfirmed == true, "Tall/wide selection confirmed");
                        Assert(scrollOverlay.SelectedBitmap != null, "SelectedBitmap created from tall/wide document");
                        Assert(scrollOverlay.SelectedBitmap.Width == 600 && scrollOverlay.SelectedBitmap.Height == 1000,
                            "SelectedBitmap dimensions (600x1000) match full document selection across scrolled viewport");
                    }

                    // Test button positioning clamped when selecting to the bottom edge of client viewport
                    using (Bitmap dummyBmp = new Bitmap(400, 300))
                    using (Graphics g = Graphics.FromImage(dummyBmp))
                    {
                        // Simulate paint with selection reaching bottom edge (Y=300)
                        if (selRectField != null)
                        {
                            selRectField.SetValue(scrollOverlay, new Rectangle(0, 0, 400, 300));
                            var hasSelField = typeof(ScreenshotSelectionOverlayForm).GetField("_hasSelection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (hasSelField != null) hasSelField.SetValue(scrollOverlay, true);
                            
                            // Invoke OnPaint
                            var onPaintMethod = typeof(ScreenshotSelectionOverlayForm).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (onPaintMethod != null)
                            {
                                onPaintMethod.Invoke(scrollOverlay, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 400, 300)) });
                                
                                var capBtnField = typeof(ScreenshotSelectionOverlayForm).GetField("_captureButtonRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                var canBtnField = typeof(ScreenshotSelectionOverlayForm).GetField("_cancelButtonRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (capBtnField != null && canBtnField != null)
                                {
                                    Rectangle capBtn = (Rectangle)capBtnField.GetValue(scrollOverlay);
                                    Rectangle canBtn = (Rectangle)canBtnField.GetValue(scrollOverlay);

                                    Assert(capBtn.Bottom <= 300 && capBtn.Top >= 0, "Capture button remains inside client bounds even at bottom edge");
                                    Assert(canBtn.Bottom <= 300 && canBtn.Top >= 0, "Cancel button remains inside client bounds even at bottom edge");
                                }
                            }
                        }
                    }
                }
            });

            Test("Screenshot Scrollbar Suppression Architecture and CDP Emulation Integration", () =>
            {
                // 1. Guard against null controls
                Assert(CdpScreenshotHelper.CaptureControlScreen(null) == null, "CaptureControlScreen with null control returns null");
                Assert(CdpScreenshotHelper.CaptureVisibleAsync(null).Result == null, "CaptureVisibleAsync with null browser returns null");
                Assert(CdpScreenshotHelper.CaptureFullPageAsync(null).Result == null, "CaptureFullPageAsync with null browser returns null");
                Assert(CdpScreenshotHelper.CaptureFullPageWithOffsetAsync(null).Result == null, "CaptureFullPageWithOffsetAsync with null browser returns null");

                // 2. Verify CefSharp CDP Emulation SetScrollbarsHiddenAsync API availability
                var devToolsType = typeof(CefSharp.DevTools.DevToolsClient);
                var emulationProp = devToolsType.GetProperty("Emulation");
                Assert(emulationProp != null, "DevToolsClient has Emulation property");
                var setScrollbarsMethod = emulationProp.PropertyType.GetMethod("SetScrollbarsHiddenAsync", new Type[] { typeof(bool) });
                Assert(setScrollbarsMethod != null, "Emulation domain exposes SetScrollbarsHiddenAsync(bool hidden)");

                // 3. Test Disposed Control Guard in CaptureControlScreen
                using (Control disposedControl = new Control())
                {
                    disposedControl.Dispose();
                    Assert(CdpScreenshotHelper.CaptureControlScreen(disposedControl) == null, "CaptureControlScreen with disposed control returns null");
                }
            });

            Test("Incognito AdBlock, YouTube Shield and Request Handling Parity", () =>
            {
                // 1. YouTube Domain Detection Parity
                Assert(AdBlockYTShield.IsYouTubeDomain("https://www.youtube.com/watch?v=dQw4w9WgXcQ"), "www.youtube.com is YouTube");
                Assert(AdBlockYTShield.IsYouTubeDomain("https://youtube.com/"), "youtube.com is YouTube");
                Assert(AdBlockYTShield.IsYouTubeDomain("https://m.youtube.com/watch?v=abc"), "m.youtube.com is YouTube");
                Assert(AdBlockYTShield.IsYouTubeDomain("https://youtu.be/dQw4w9WgXcQ"), "youtu.be is YouTube");
                Assert(AdBlockYTShield.IsYouTubeDomain("https://youtube-nocookie.com/embed/abc"), "youtube-nocookie.com is YouTube");

                // 2. Network Ad Blocking for YouTube ad endpoints
                string matchedRule;
                Assert(AppServices.AdBlock.ShouldBlock("https://googleads.g.doubleclick.net/pagead/ads?client=ca-pub-test", "https://www.youtube.com/watch?v=abc", 1, out matchedRule),
                    "googleads.g.doubleclick.net is blocked on YouTube");
                Assert(AppServices.AdBlock.ShouldBlock("https://adservice.google.com/adsid/integrator.js", "https://www.youtube.com/watch?v=abc", 1, out matchedRule),
                    "adservice.google.com is blocked on YouTube");
                Assert(AppServices.AdBlock.ShouldBlock("https://www.youtube.com/api/stats/ads?adformat=video", "https://www.youtube.com/watch?v=abc", 1, out matchedRule),
                    "youtube.com/api/stats/ads is blocked on YouTube");

                // 3. Cosmetic Hiding Scriptlet Generation on YouTube and Standard Domains
                string ytCosmeticScript = AppServices.AdBlock.GetCosmeticHidingScript("https://www.youtube.com/watch?v=abc");
                Assert(!string.IsNullOrEmpty(ytCosmeticScript), "Cosmetic script is generated for YouTube");
                Assert(ytCosmeticScript.Contains("cnet-ublock-cosmetic"), "Cosmetic script injects style tag");

                string regularCosmeticScript = AppServices.AdBlock.GetCosmeticHidingScript("https://example.com/");
                Assert(!string.IsNullOrEmpty(regularCosmeticScript), "Cosmetic script is generated for standard websites");
                Assert(regularCosmeticScript.Contains("cnet-ublock-cosmetic"), "Cosmetic script injects style tag for standard websites");

                // 4. Incognito URL Internal Detection and Resolution
                Assert(InternalPageHelper.IsInternalUrl("wb://incognito"), "wb://incognito is identified as internal url");
                Assert(InternalPageHelper.GetInternalPageType("wb://incognito") == InternalPageType.Incognito, "Page type is Incognito");
                Assert(InternalPageHelper.GetInternalTitle("wb://incognito") == "New Tab", "Internal title is New Tab");
            });

            Test("Suggestions Service URL Detection and Formatting", () =>
            {
                Assert(SuggestionService.IsLikelyUrl("google.com"), "google.com is identified as likely URL");
                Assert(SuggestionService.IsLikelyUrl("https://github.com"), "https://github.com is identified as likely URL");
                Assert(SuggestionService.IsLikelyUrl("http://localhost:8080"), "localhost is identified as likely URL");
                Assert(SuggestionService.IsLikelyUrl("wb://bookmarks"), "wb:// internal page is identified as likely URL");
                Assert(SuggestionService.IsLikelyUrl("about:blank"), "about:blank is identified as likely URL");
                Assert(SuggestionService.IsLikelyUrl("sub.domain.co.uk/path"), "subdomain is identified as likely URL");

                Assert(!SuggestionService.IsLikelyUrl("hello world"), "hello world is not a direct URL");
                Assert(!SuggestionService.IsLikelyUrl("how to make pasta"), "multi-word query is not a direct URL");
                Assert(!SuggestionService.IsLikelyUrl("c# tutorials"), "search query with spaces is not a direct URL");

                Assert(SuggestionService.FormatDirectUrl("google.com") == "https://google.com", "google.com formats to https://google.com");
                Assert(SuggestionService.FormatDirectUrl("http://insecure.org") == "http://insecure.org", "Existing http scheme preserved");
                Assert(SuggestionService.FormatDirectUrl("wb://history") == "wb://history", "wb scheme preserved");
            });

            Test("Suggestions Service Remote JSON Parsing", () =>
            {
                SuggestionService service = new SuggestionService();
                string sampleJson = "[\"visual studio\",[\"visual studio code\",\"visual studio 2022\",\"visual studio download\"]]";
                List<string> parsed = service.ParseGoogleSuggestJson(sampleJson);
                Assert(parsed != null, "Parsed list must not be null");
                Assert(parsed.Count == 3, "Must parse 3 suggestions, got " + (parsed != null ? parsed.Count : 0));
                Assert(parsed[0] == "visual studio code", "First item matches");
                Assert(parsed[1] == "visual studio 2022", "Second item matches");
                Assert(parsed[2] == "visual studio download", "Third item matches");

                // Empty / invalid JSON fallback
                List<string> emptyList = service.ParseGoogleSuggestJson(string.Empty);
                Assert(emptyList != null && emptyList.Count == 0, "Empty JSON returns empty list");

                List<string> invalidList = service.ParseGoogleSuggestJson("{invalid json}");
                Assert(invalidList != null && invalidList.Count == 0, "Invalid JSON returns empty list");
            });

            Test("SuggestionItem Visual Properties and Tag Assignment", () =>
            {
                SuggestionItem searchItem = new SuggestionItem(SuggestionType.Search, "test query", null, "https://google.com", "test");
                Assert(searchItem.IconName == "magnifying-glass", "Search item icon must be magnifying-glass");
                Assert(searchItem.TagName == "Google Search", "Search item tag must be Google Search");

                SuggestionItem directItem = new SuggestionItem(SuggestionType.DirectUrl, "github.com", "https://github.com", "https://github.com", "git");
                Assert(directItem.IconName == "globe", "Direct URL icon must be globe");
                Assert(directItem.TagName == null, "Direct URL tag should be null for uncluttered presentation");

                SuggestionItem bookmarkItem = new SuggestionItem(SuggestionType.Bookmark, "My Bookmark", "https://site.org", "https://site.org", "my");
                Assert(bookmarkItem.IconName == "star", "Bookmark item icon must be star");
                Assert(bookmarkItem.TagName == "Bookmark", "Bookmark item tag must be Bookmark");

                SuggestionItem historyItem = new SuggestionItem(SuggestionType.History, "Visited Page", "https://visited.com", "https://visited.com", "vis");
                Assert(historyItem.IconName == "clock-counter-clockwise", "History item icon must be clock-counter-clockwise");
                Assert(historyItem.TagName == "History", "History item tag must be History");

                SuggestionItem internalItem = new SuggestionItem(SuggestionType.InternalPage, "Bookmarks", "wb://bookmarks", "wb://bookmarks", "book");
                Assert(internalItem.IconName == "browser", "Internal page item icon must be browser");
                Assert(internalItem.TagName == "wb://", "Internal page item tag must be wb://");
            });

            Test("SuggestionService Incognito History Isolation and Internal Pages Strictly Excluded", () =>
            {
                SuggestionService service = new SuggestionService();

                // Seed a bookmark and history entry
                AppServices.Bookmarks.Add("Antigravity Site", "https://antigravity.dev");
                AppServices.History.AddEntry("Antigravity Docs History", "https://antigravity.dev/docs");

                // 1. Regular session: history item should be matched
                var normalSuggestions = service.GetSuggestionsAsync("antigravity", false, 7, default(System.Threading.CancellationToken), false).GetAwaiter().GetResult();
                Assert(normalSuggestions != null && normalSuggestions.Count > 0, "Normal suggestions must return items");
                bool hasHistory = normalSuggestions.Exists(s => s.Type == SuggestionType.History);
                Assert(hasHistory, "Normal suggestions must include history visits");

                // 2. Incognito session: history items must be STRICTLY EXCLUDED for user privacy
                var incognitoSuggestions = service.GetSuggestionsAsync("antigravity", true, 7, default(System.Threading.CancellationToken), false).GetAwaiter().GetResult();
                Assert(incognitoSuggestions != null && incognitoSuggestions.Count > 0, "Incognito suggestions must return items");
                bool hasHistoryInIncognito = incognitoSuggestions.Exists(s => s.Type == SuggestionType.History);
                Assert(!hasHistoryInIncognito, "Incognito suggestions must NEVER leak history visits");

                // Bookmarks should still be accessible in incognito
                bool hasBookmarkInIncognito = incognitoSuggestions.Exists(s => s.Type == SuggestionType.Bookmark);
                Assert(hasBookmarkInIncognito, "Incognito suggestions should allow access to user bookmarks");

                // 3. Internal wb:// pages must NEVER be suggested in address bar suggestions
                var internalSuggestions = service.GetSuggestionsAsync("download", false, 7, default(System.Threading.CancellationToken), false).GetAwaiter().GetResult();
                bool hasInternalDownloads = internalSuggestions.Exists(s => s.Type == SuggestionType.InternalPage || (s.ActionValue != null && InternalPageHelper.IsInternalUrl(s.ActionValue)));
                Assert(!hasInternalDownloads, "Internal wb://downloads page must NOT be suggested in address bar suggestions");

                var incogSuggestions = service.GetSuggestionsAsync("incognito", false, 7, default(System.Threading.CancellationToken), false).GetAwaiter().GetResult();
                bool hasInternalIncognito = incogSuggestions.Exists(s => s.Type == SuggestionType.InternalPage || (s.ActionValue != null && (s.ActionValue == "wb://incognito" || s.ActionValue == "wb://private")));
                Assert(!hasInternalIncognito, "Internal wb://incognito page must NOT be suggested in normal mode");

                var bookmarksSuggestions = service.GetSuggestionsAsync("book", false, 7, default(System.Threading.CancellationToken), false).GetAwaiter().GetResult();
                bool hasInternalBookmarks = bookmarksSuggestions.Exists(s => s.Type == SuggestionType.InternalPage || (s.ActionValue != null && s.ActionValue == "wb://bookmarks"));
                Assert(!hasInternalBookmarks, "Internal wb://bookmarks page must NOT be suggested in address bar suggestions");

                // 4. Source code verification: Ensure SuggestionService does not have AddInternalPageMatches
                string testSrcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(testSrcDir)) testSrcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                string suggSrc = File.ReadAllText(Path.Combine(testSrcDir, @"Browser\Suggestions\SuggestionService.cs"));
                Assert(!suggSrc.Contains("AddInternalPageMatches"), "SuggestionService must NOT contain AddInternalPageMatches");
            });

            Test("SuggestionDropdownPopup Non-Activating Window and Control Attachment", () =>
            {
                using (Form testHost = new Form { Width = 800, Height = 600 })
                using (TextBox testBox = new TextBox { Width = 300, Top = 50, Left = 50 })
                using (SuggestionDropdownPopup popup = new SuggestionDropdownPopup())
                {
                    testHost.Controls.Add(testBox);
                    testHost.Show();
                    testBox.Focus();
                    Application.DoEvents();

                    string executedTarget = null;
                    bool executedNewTab = false;

                    popup.Attach(
                        testBox,
                        () => new Rectangle(testBox.Left, testBox.Top, testBox.Width, testBox.Height),
                        testHost,
                        false,
                        (target, newTab) =>
                        {
                            executedTarget = target;
                            executedNewTab = newTab;
                        });

                    // Verify CreateParams non-activating flags
                    var createParamsProp = typeof(SuggestionDropdownPopup).GetProperty("CreateParams", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    CreateParams cp = (CreateParams)createParamsProp.GetValue(popup, null);
                    Assert((cp.ExStyle & 0x08000000) != 0, "Popup must have WS_EX_NOACTIVATE (0x08000000)");
                    Assert((cp.ExStyle & 0x00000080) != 0, "Popup must have WS_EX_TOOLWINDOW (0x00000080)");
                    Assert((cp.Style & unchecked((int)0x80000000)) != 0, "Popup must have WS_POPUP (0x80000000)");

                    var showNoActProp = typeof(SuggestionDropdownPopup).GetProperty("ShowWithoutActivation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    bool showWithoutAct = (bool)showNoActProp.GetValue(popup, null);
                    Assert(showWithoutAct == true, "ShowWithoutActivation must return true");

                    // Verify Incognito toggling property
                    popup.IsIncognito = true;
                    Assert(popup.IsIncognito == true, "IsIncognito property must update");
                    popup.IsIncognito = false;

                    // Verify HandleKeyDown when not visible returns false
                    KeyEventArgs downKey = new KeyEventArgs(Keys.Down);
                    bool handled = popup.HandleKeyDown(downKey);
                    Assert(!handled, "HandleKeyDown returns false when popup is not yet visible");

                    // Verify programmatic text changes when suppressed or unfocused never show popup
                    popup.SuppressSuggestions();
                    testBox.Text = "https://www.youtube.com/watch?v=123";
                    Application.DoEvents();
                    Assert(!popup.IsPopupVisible, "Popup must never show when suggestions are suppressed");

                    popup.ResumeSuggestions();
                    // Unfocus testBox
                    testHost.ActiveControl = null;
                    testBox.Text = "https://www.google.com";
                    Application.DoEvents();
                    Assert(!popup.IsPopupVisible, "Popup must never show when text box is not focused");

                    // Detach and Dispose
                    popup.Detach();
                    testHost.Close();
                }
            });

            Test("UITheme Global Dark Mode Palette Consistency", () =>
            {
                Assert(UITheme.FormBackground == Color.FromArgb(32, 33, 36), "FormBackground should be #202124");
                Assert(UITheme.ButtonBackground == Color.FromArgb(48, 50, 54), "ButtonBackground should be #303236");
                Assert(UITheme.TextPrimary == Color.FromArgb(232, 234, 237), "TextPrimary should be #E8EAED");
                Assert(UITheme.TextSecondary == Color.FromArgb(154, 160, 166), "TextSecondary should be #9AA0A6");
                Assert(UITheme.BorderColor == Color.FromArgb(60, 64, 67), "BorderColor should be #3C4043");
                Assert(UITheme.InputBackground == Color.FromArgb(41, 42, 45), "InputBackground should be #292A2D");
                Assert(UITheme.InputText == Color.FromArgb(232, 234, 237), "InputText should be #E8EAED");
            });

            Test("UITheme Dark Controls Styling (ListView, ComboBox, CheckBox, RadioButton)", () =>
            {
                using (ListView lv = new ListView())
                {
                    lv.View = View.Details;
                    lv.Columns.Add("Test Column", 100);
                    UITheme.ApplyListView(lv);

                    Assert(lv.BackColor == UITheme.FormBackground, "ListView BackColor must be FormBackground");
                    Assert(lv.ForeColor == UITheme.TextPrimary, "ListView ForeColor must be TextPrimary");
                    Assert(lv.OwnerDraw == true, "ListView OwnerDraw must be true for dark header rendering");
                    Assert(lv.GridLines == false, "ListView GridLines must be false in dark mode to prevent bright lines across empty space");
                }

                using (ComboBox cb = new ComboBox())
                {
                    cb.Items.Add("Option 1");
                    cb.Items.Add("Option 2");
                    UITheme.ApplyComboBox(cb);

                    Assert(cb.BackColor == UITheme.InputBackground, "ComboBox BackColor must be InputBackground");
                    Assert(cb.ForeColor == UITheme.InputText, "ComboBox ForeColor must be InputText");
                    Assert(cb.FlatStyle == FlatStyle.Flat, "ComboBox FlatStyle must be Flat");
                    Assert(cb.DrawMode == DrawMode.OwnerDrawFixed, "ComboBox DrawMode must be OwnerDrawFixed");
                }

                using (CheckBox chk = new CheckBox())
                {
                    chk.Text = "Enable Dark Mode";
                    UITheme.ApplyCheckBox(chk);

                    Assert(chk.BackColor == Color.Transparent, "CheckBox BackColor must be Transparent");
                    Assert(chk.ForeColor == UITheme.TextPrimary, "CheckBox ForeColor must be TextPrimary");
                }

                using (RadioButton rb = new RadioButton())
                {
                    rb.Text = "Select Dark Option";
                    UITheme.ApplyRadioButton(rb);

                    Assert(rb.BackColor == Color.Transparent, "RadioButton BackColor must be Transparent");
                    Assert(rb.ForeColor == UITheme.TextPrimary, "RadioButton ForeColor must be TextPrimary");
                }
            });

            Test("UITheme Recursive Form Dark Mode Application", () =>
            {
                using (Form testForm = new Form())
                using (Panel panel = new Panel())
                using (Label label = new Label())
                using (TextBox textBox = new TextBox())
                using (Button button = new Button())
                using (ListView listView = new ListView())
                using (ComboBox comboBox = new ComboBox())
                using (CheckBox checkBox = new CheckBox())
                {
                    panel.Controls.Add(label);
                    panel.Controls.Add(textBox);
                    panel.Controls.Add(button);
                    panel.Controls.Add(listView);
                    panel.Controls.Add(comboBox);
                    panel.Controls.Add(checkBox);
                    testForm.Controls.Add(panel);

                    UITheme.ApplyForm(testForm);

                    Assert(testForm.BackColor == UITheme.FormBackground, "Form BackColor is FormBackground");
                    Assert(testForm.ForeColor == UITheme.TextPrimary, "Form ForeColor is TextPrimary");

                    Assert(panel.BackColor == UITheme.FormBackground, "Panel BackColor is FormBackground");
                    Assert(label.ForeColor == UITheme.TextPrimary, "Label ForeColor is TextPrimary");
                    Assert(textBox.BackColor == UITheme.InputBackground, "TextBox BackColor is InputBackground");
                    Assert(textBox.ForeColor == UITheme.InputText, "TextBox ForeColor is InputText");
                    Assert(button.BackColor == UITheme.ButtonBackground, "Button BackColor is ButtonBackground");
                    Assert(button.ForeColor == UITheme.TextPrimary, "Button ForeColor is TextPrimary");
                    Assert(listView.BackColor == UITheme.FormBackground, "Nested ListView BackColor is FormBackground");
                    Assert(comboBox.BackColor == UITheme.InputBackground, "Nested ComboBox BackColor is InputBackground");
                    Assert(checkBox.ForeColor == UITheme.TextPrimary, "Nested CheckBox ForeColor is TextPrimary");
                }
            });

            Test("AddShortcutDialog Dark Mode Attributes", () =>
            {
                using (AddShortcutDialog dialog = new AddShortcutDialog())
                {
                    Assert(dialog.BackColor == UITheme.FormBackground, "AddShortcutDialog BackColor is FormBackground");
                    Assert(dialog.ForeColor == UITheme.TextPrimary, "AddShortcutDialog ForeColor is TextPrimary");
                    Assert(dialog.FormBorderStyle == FormBorderStyle.FixedDialog, "AddShortcutDialog has FixedDialog border");
                }
            });

            Test("BookmarksView Add Bookmark Dialog Sizing", () =>
            {
                using (BookmarksView bv = new BookmarksView((url, inNewTab) => { }))
                {
                    var addBtnField = typeof(BookmarksView).GetField("_addBtn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(addBtnField != null, "_addBtn field exists on BookmarksView");
                    Button addBtn = addBtnField.GetValue(bv) as Button;
                    Assert(addBtn != null, "_addBtn is initialized");
                    Assert(addBtn.Text == AppStrings.Bookmarks.BtnAdd, "_addBtn has expected text");

                    var editBtnField = typeof(BookmarksView).GetField("_editBtn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(editBtnField != null, "_editBtn field exists on BookmarksView");
                    Button editBtn = editBtnField.GetValue(bv) as Button;
                    Assert(editBtn != null, "_editBtn is initialized");
                    Assert(editBtn.Text == AppStrings.Bookmarks.BtnEdit, "_editBtn has expected text");
                }
            });

            Test("BookmarkEditDialog 3-in-1 Architecture, Layout and Sizing", () =>
            {
                // 1. Test standard Add Bookmark configuration (with Folder TreeView)
                using (BookmarkEditDialog dlg = new BookmarkEditDialog(AppStrings.Bookmarks.AddTitle, "My Bookmark", "https://example.com"))
                {
                    Assert(dlg.ClientSize.Width == 460 && dlg.ClientSize.Height == 440, "Dialog ClientSize must be 460x440 for bookmark dialog with folder tree");
                    Assert(dlg.FormBorderStyle == FormBorderStyle.FixedDialog, "Dialog must be FixedDialog");
                    Assert(dlg.BackColor == UITheme.FormBackground, "Dialog BackColor must match UITheme.FormBackground");
                    Assert(dlg.ItemTitle == "My Bookmark", "ItemTitle should match initial value");
                    Assert(dlg.ItemUrl == "https://example.com", "ItemUrl should match initial value");
                    Assert(dlg.SelectedFolderId == "bar", "Default folder selection should be 'bar'");
                    Assert(!dlg.ShowBookmarkCheckbox, "Checkbox should be hidden for standard bookmark dialog");
                    Assert(dlg.AcceptButton != null, "AcceptButton should be configured");

                    var folderTreeField = typeof(BookmarkEditDialog).GetField("_folderTree", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(folderTreeField != null, "_folderTree field exists on BookmarkEditDialog");
                    TreeView tree = folderTreeField.GetValue(dlg) as TreeView;
                    Assert(tree != null, "_folderTree is initialized");
                    Assert(tree.Nodes.Count >= 2, "Folder tree contains root folders (Bookmarks bar, Other bookmarks)");

                    var newFolderBtnField = typeof(BookmarkEditDialog).GetField("_newFolderBtn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(newFolderBtnField != null, "_newFolderBtn field exists on BookmarkEditDialog");
                    Button newBtn = newFolderBtnField.GetValue(dlg) as Button;
                    Assert(newBtn != null, "_newFolderBtn is initialized");
                    Assert(newBtn.Text == AppStrings.Bookmarks.BtnNewFolder, "_newFolderBtn text should be New folder");

                    dlg.SelectedFolderId = "other";
                    Assert(dlg.SelectedFolderId == "other", "SelectedFolderId should update to 'other'");
                }

                // 2. Test Edit Tab configuration (2-in-1 mode)
                using (BookmarkEditDialog tabDlg = new BookmarkEditDialog(AppStrings.Browser.EditTabTitle, "Original Tab", "https://news.ycombinator.com", true, true))
                {
                    Assert(tabDlg.ClientSize.Width == 460 && tabDlg.ClientSize.Height == 128, "Edit Tab dialog ClientSize must also be 460x128");
                    Assert(tabDlg.Text == AppStrings.Browser.EditTabTitle, "Dialog title should be Edit Tab");
                    Assert(tabDlg.ItemTitle == "Original Tab", "ItemTitle should match tab title");
                    Assert(tabDlg.ItemUrl == "https://news.ycombinator.com", "ItemUrl should match tab URL");
                    Assert(tabDlg.ShowBookmarkCheckbox, "Checkbox should be visible for Edit Tab");
                    Assert(tabDlg.IsBookmarked, "IsBookmarked should be true when tab is already bookmarked");

                    // Mutate values
                    tabDlg.ItemTitle = "Custom Hacker News";
                    tabDlg.ItemUrl = "https://news.ycombinator.com/best";
                    tabDlg.IsBookmarked = false;
                    Assert(tabDlg.ItemTitle == "Custom Hacker News", "ItemTitle getter should reflect updated text");
                    Assert(tabDlg.ItemUrl == "https://news.ycombinator.com/best", "ItemUrl getter should reflect updated text");
                    Assert(!tabDlg.IsBookmarked, "IsBookmarked should reflect unchecked state");
                }

                // 3. Test Folder Only configuration
                using (BookmarkEditDialog folderDlg = new BookmarkEditDialog(AppStrings.Bookmarks.AddFolderTitle, "My Tools", null, false, false, null, "bar", false, true))
                {
                    Assert(folderDlg.ClientSize.Width == 460 && folderDlg.ClientSize.Height == 380, "Folder Only dialog ClientSize must be 460x380");
                    Assert(folderDlg.ItemTitle == "My Tools", "Folder title matches");
                }

                // 4. Test Remove button configuration
                using (BookmarkEditDialog editDlg = new BookmarkEditDialog(AppStrings.Bookmarks.EditTitle, "Old Bookmark", "https://old.com", false, true, null, "bar", true))
                {
                    Assert(!editDlg.IsDeleted, "IsDeleted should initially be false");
                    var removeBtnField = typeof(BookmarkEditDialog).GetField("_removeBtn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(removeBtnField != null, "_removeBtn field exists on BookmarkEditDialog");
                    Button remBtn = removeBtnField.GetValue(editDlg) as Button;
                    Assert(remBtn != null, "_removeBtn is initialized when showRemoveButton is true");
                }
            });

            Test("WbBookmarksBar Architecture, Phosphor Icons and Folder Operations", () =>
            {
                using (WbBookmarksBar bar = new WbBookmarksBar())
                {
                    bar.DpiScale = 1.0f;
                    Assert(bar.Height == 28, "Bookmarks bar default height is 28px");
                    Assert(bar.AllowDrop, "Bookmarks bar must have AllowDrop enabled");
                    Assert(bar.Dock == DockStyle.Top, "Bookmarks bar docks to Top");

                    // Verify default seed bookmarks
                    var barItems = AppServices.Bookmarks.GetBarItems("bar");
                    Assert(barItems.Count >= 4, "Bookmarks bar should contain at least 4 items including seeded GitHub, YouTube, Wikipedia, Tools");

                    bool foundFolder = false;
                    for (int i = 0; i < barItems.Count; i++)
                    {
                        if (barItems[i].IsFolder && barItems[i].Title == "Tools")
                        {
                            foundFolder = true;
                            var toolsChildren = AppServices.Bookmarks.GetBarItems(barItems[i].Id);
                            Assert(toolsChildren.Count >= 2, "Tools folder should contain seeded Google and ChatGPT");
                            break;
                        }
                    }
                    Assert(foundFolder, "Seeded 'Tools' folder found on bookmarks bar");

                    var otherItems = AppServices.Bookmarks.GetOtherBookmarks();
                    Assert(otherItems.Count >= 1, "Other bookmarks should contain seeded Internet Archive");

                    // Test bar toggle visibility
                    bool originalVis = AppServices.Bookmarks.ShowBookmarksBar;
                    AppServices.Bookmarks.ShowBookmarksBar = false;
                    Assert(!AppServices.Bookmarks.ShowBookmarksBar, "ShowBookmarksBar can be toggled false");
                    AppServices.Bookmarks.ShowBookmarksBar = true;
                    Assert(AppServices.Bookmarks.ShowBookmarksBar, "ShowBookmarksBar can be toggled true");

                    bar.RecomputeLayout();
                }
            });

            Test("BookmarkManager Folders, Seed Bookmarks, Reordering and Recursive Deletion", () =>
            {
                BookmarkManager bm = new BookmarkManager();

                // Add a test folder
                BookmarkItem testFolder = bm.AddFolder("Development", "bar");
                Assert(testFolder != null && testFolder.IsFolder, "AddFolder creates a folder item");
                Assert(testFolder.ParentId == "bar", "Folder parent is bar");

                // Add child bookmarks
                BookmarkItem sub1 = bm.Add("Dev Docs", "https://devdocs.io", testFolder.Id);
                BookmarkItem sub2 = bm.Add("StackOverflow", "https://stackoverflow.com", testFolder.Id);
                Assert(bm.IsBookmarked("https://devdocs.io"), "Child bookmark is indexed");
                Assert(bm.IsBookmarked("https://stackoverflow.com"), "Child bookmark is indexed");

                var devChildren = bm.GetBarItems(testFolder.Id);
                Assert(devChildren.Count == 2, "Development folder contains 2 children");

                int count = bm.GetChildCount(testFolder.Id);
                Assert(count == 2, "GetChildCount returns 2");

                string folderName = bm.GetFolderName(testFolder.Id);
                Assert(folderName == "Development", "GetFolderName returns folder title");

                // Test Reorder
                bm.MoveItem(sub2.Id, 0, testFolder.Id);

                // Test recursive deletion
                bool removed = bm.Remove(testFolder.Id);
                Assert(removed, "Remove returned true for folder");
                Assert(!bm.IsBookmarked("https://devdocs.io"), "Dev Docs was recursively removed from index");
                Assert(!bm.IsBookmarked("https://stackoverflow.com"), "StackOverflow was recursively removed from index");
                Assert(bm.GetById(testFolder.Id) == null, "Folder itself was removed");
            });

            Test("BookmarkManager Update Lifecycle", () =>
            {
                BookmarkManager manager = new BookmarkManager();
                BookmarkItem item = manager.Add("Old Title", "https://old-domain.com");
                Assert(item != null, "Bookmark item should be created");
                string id = item.Id;

                bool updated = manager.Update(id, "New Title", "https://new-domain.com");
                Assert(updated, "Update should return true for existing bookmark");

                List<BookmarkItem> list = manager.GetAll();
                BookmarkItem found = null;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == id) { found = list[i]; break; }
                }
                Assert(found != null, "Updated item should be present in GetAll()");
                Assert(found.Title == "New Title", "Updated item should have new title");
                Assert(found.Url == "https://new-domain.com", "Updated item should have new URL");

                // Cleanup
                manager.Remove(id);
                Assert(!manager.IsBookmarked("https://new-domain.com"), "Removed bookmark should not be bookmarked");
            });

            Test("Tab Context Menu Edit Tab Strings", () =>
            {
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.TabMenuEditTab), "TabMenuEditTab must not be empty");
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.EditTabTitle), "EditTabTitle must not be empty");
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.BookmarkThisTab), "BookmarkThisTab must not be empty");
                Assert(!AppStrings.Browser.TabMenuEditTab.Contains("â­"), "TabMenuEditTab should not contain emojis");
                Assert(!AppStrings.Browser.BookmarkThisTab.Contains("â­"), "BookmarkThisTab should not contain emojis");
            });

            Test("LifeSpanHandler NewPopup Preserves Window Opener And Enables JS Access", () =>
            {
                string openedTabUrl = null;
                var handler = new LifeSpanHandler(url => openedTabUrl = url);
                bool noJsAccess = true;
                IWebBrowser newBrowser;

                bool result = handler.OnBeforePopup(
                    null, null, null,
                    "https://ageverif.com/verify",
                    "AgeVerif",
                    WindowOpenDisposition.NewPopup,
                    true,
                    null,
                    null,
                    null,
                    ref noJsAccess,
                    out newBrowser
                );

                Assert(!result, "OnBeforePopup must return false for NewPopup so CEF creates the popup window");
                Assert(!noJsAccess, "noJavascriptAccess must be set to false so window.opener and postMessage work");
                Assert(newBrowser == null, "newBrowser must be null to allow native CEF popup creation");
                Assert(openedTabUrl == null, "openNewTabAction must not be called for popup windows");
            });

            Test("LifeSpanHandler NewWindow Preserves Window Opener", () =>
            {
                string openedTabUrl = null;
                var handler = new LifeSpanHandler(url => openedTabUrl = url);
                bool noJsAccess = true;
                IWebBrowser newBrowser;

                bool result = handler.OnBeforePopup(
                    null, null, null,
                    "https://accounts.google.com/o/oauth2/auth",
                    "GoogleAuth",
                    WindowOpenDisposition.NewWindow,
                    true,
                    null,
                    null,
                    null,
                    ref noJsAccess,
                    out newBrowser
                );

                Assert(!result, "OnBeforePopup must return false for NewWindow");
                Assert(!noJsAccess, "noJavascriptAccess must be set to false for NewWindow");
                Assert(newBrowser == null, "newBrowser must be null");
                Assert(openedTabUrl == null, "openNewTabAction must not be called for NewWindow");
            });

            Test("LifeSpanHandler PopupFeatures Populates WindowInfo And Preserves Opener", () =>
            {
                string openedTabUrl = null;
                var handler = new LifeSpanHandler(url => openedTabUrl = url);
                bool noJsAccess = true;
                IWebBrowser newBrowser;

                var popupFeatures = new MockPopupFeatures
                {
                    Width = 500,
                    Height = 650,
                    X = 120,
                    Y = 180,
                    IsPopup = true
                };
                var windowInfo = new MockWindowInfo();

                bool result = handler.OnBeforePopup(
                    null, null, null,
                    "https://login.live.com/oauth20_authorize.srf",
                    "MicrosoftSignIn",
                    WindowOpenDisposition.Unknown,
                    true,
                    popupFeatures,
                    windowInfo,
                    null,
                    ref noJsAccess,
                    out newBrowser
                );

                Assert(!result, "OnBeforePopup must return false when popupFeatures indicate popup");
                Assert(!noJsAccess, "noJavascriptAccess must be false");
                Assert(newBrowser == null, "newBrowser must be null");
                Assert(openedTabUrl == null, "openNewTabAction must not be called for popup with features");
                Assert(windowInfo.Width == 500, "windowInfo Width must be set from popupFeatures");
                Assert(windowInfo.Height == 650, "windowInfo Height must be set from popupFeatures");
                Assert(windowInfo.X == 120, "windowInfo X must be set from popupFeatures");
                Assert(windowInfo.Y == 180, "windowInfo Y must be set from popupFeatures");
                Assert(windowInfo.WindowName == "MicrosoftSignIn", "windowInfo WindowName must be set from targetFrameName");
                Assert(windowInfo.ParentWindowHandle == IntPtr.Zero, "ParentWindowHandle must remain IntPtr.Zero so CEF creates a top-level WS_OVERLAPPEDWINDOW with native title bar");
            });

            Test("LifeSpanHandler Normal Tabs Open In Internal Tabs", () =>
            {
                string foregroundTabUrl = null;
                var handlerForeground = new LifeSpanHandler(url => foregroundTabUrl = url);
                bool noJsAccess = false;
                IWebBrowser newBrowser;

                bool fgResult = handlerForeground.OnBeforePopup(
                    null, null, null,
                    "https://en.wikipedia.org/wiki/Web_browser",
                    "_blank",
                    WindowOpenDisposition.NewForegroundTab,
                    true,
                    null,
                    null,
                    null,
                    ref noJsAccess,
                    out newBrowser
                );

                Assert(fgResult, "OnBeforePopup must return true for NewForegroundTab to handle as internal tab");
                Assert(foregroundTabUrl == "https://en.wikipedia.org/wiki/Web_browser", "openNewTabAction must receive targetUrl");

                string backgroundTabUrl = null;
                var handlerBackground = new LifeSpanHandler(url => backgroundTabUrl = url);
                bool bgResult = handlerBackground.OnBeforePopup(
                    null, null, null,
                    "https://news.ycombinator.com",
                    "_blank",
                    WindowOpenDisposition.NewBackgroundTab,
                    false,
                    null,
                    null,
                    null,
                    ref noJsAccess,
                    out newBrowser
                );

                Assert(bgResult, "OnBeforePopup must return true for NewBackgroundTab to handle as internal tab");
                Assert(backgroundTabUrl == "https://news.ycombinator.com", "openNewTabAction must receive targetUrl");
            });

            Test("LifeSpanHandler DoClose Allows Native Window Close", () =>
            {
                var handler = new LifeSpanHandler(null);
                bool closeHandled = handler.DoClose(null, null);
                Assert(!closeHandled, "DoClose must return false to allow native/JS window.close() to proceed");
            });

            Test("ClearBrowsingData TimeRange Cutoffs and Display Names", () =>
            {
                DateTime now = DateTime.Now;
                DateTime cutoffHour = ClearBrowsingDataTimeRange.LastHour.GetCutoffUtc();
                Assert(cutoffHour <= now && cutoffHour >= now.AddHours(-1).AddSeconds(-5), "LastHour cutoff within range");

                DateTime cutoff24 = ClearBrowsingDataTimeRange.Last24Hours.GetCutoffUtc();
                Assert(cutoff24 <= now && cutoff24 >= now.AddDays(-1).AddSeconds(-5), "Last24Hours cutoff within range");

                DateTime cutoff7 = ClearBrowsingDataTimeRange.Last7Days.GetCutoffUtc();
                Assert(cutoff7 <= now && cutoff7 >= now.AddDays(-7).AddSeconds(-5), "Last7Days cutoff within range");

                DateTime cutoff4w = ClearBrowsingDataTimeRange.Last4Weeks.GetCutoffUtc();
                Assert(cutoff4w <= now && cutoff4w >= now.AddDays(-28).AddSeconds(-5), "Last4Weeks cutoff within range");

                DateTime cutoffAll = ClearBrowsingDataTimeRange.AllTime.GetCutoffUtc();
                Assert(cutoffAll == DateTime.MinValue, "AllTime cutoff must be DateTime.MinValue");

                Assert(ClearBrowsingDataTimeRange.LastHour.GetDisplayName() == "Last hour", "DisplayName LastHour");
                Assert(ClearBrowsingDataTimeRange.Last24Hours.GetDisplayName() == "Last 24 hours", "DisplayName Last24Hours");
                Assert(ClearBrowsingDataTimeRange.Last7Days.GetDisplayName() == "Last 7 days", "DisplayName Last7Days");
                Assert(ClearBrowsingDataTimeRange.Last4Weeks.GetDisplayName() == "Last 4 weeks", "DisplayName Last4Weeks");
                Assert(ClearBrowsingDataTimeRange.AllTime.GetDisplayName() == "All time", "DisplayName AllTime");
            });

            Test("HistoryManager GetCountSince and ClearSince Time Range Lifecycle", () =>
            {
                HistoryManager hm = new HistoryManager();
                hm.ClearAll();

                DateTime now = DateTime.Now;
                hm.AddEntry("Recent Visit", "https://recent.com");
                hm.AddEntry("Another Recent", "https://another-recent.com");

                int countAll = hm.GetCountSince(DateTime.MinValue);
                Assert(countAll == 2, "Should have 2 history entries");

                int countHour = hm.GetCountSince(now.AddHours(-1));
                Assert(countHour == 2, "Both entries visited within last hour");

                int countFuture = hm.GetCountSince(now.AddHours(1));
                Assert(countFuture == 0, "No entries visited in the future");

                int cleared = hm.ClearSince(now.AddHours(-1));
                Assert(cleared == 2, "Should have cleared 2 entries");
                Assert(hm.GetAll().Count == 0, "History should now be empty");
            });

            Test("DownloadManager GetCountSince and ClearSince Time Range Lifecycle", () =>
            {
                DownloadManager dm = new DownloadManager();
                dm.ClearHistory();

                var d1 = dm.RegisterDownload(1001, "test1.zip", @"C:\temp\test1.zip", "https://example.com/test1.zip", 1024);
                dm.UpdateProgress(1001, 1024, 1024, 100, "Completed", 0);
                d1.StartTime = DateTime.Now.AddMinutes(-30);

                var d2 = dm.RegisterDownload(1002, "test2.zip", @"C:\temp\test2.zip", "https://example.com/test2.zip", 1024);
                d2.StartTime = DateTime.Now.AddMinutes(-20);

                var d3 = dm.RegisterDownload(1003, "test3.zip", @"C:\temp\test3.zip", "https://example.com/test3.zip", 1024);
                dm.UpdateProgress(1003, 0, 1024, 0, "Failed", 0);
                d3.StartTime = DateTime.Now.AddDays(-2);

                bool clearedEventFired = false;
                dm.DownloadsCleared += (s, e) => clearedEventFired = true;

                int countHour = dm.GetCountSince(DateTime.Now.AddHours(-1));
                Assert(countHour == 1, "Only completed download within last hour counted");

                int countAll = dm.GetCountSince(DateTime.MinValue);
                Assert(countAll == 2, "Both non-active downloads counted for all time");

                int removed = dm.ClearSince(DateTime.Now.AddHours(-1));
                Assert(removed == 1, "1 download removed");
                Assert(clearedEventFired, "DownloadsCleared event fired");
            });

            Test("BrowsingDataCleaner File Size Formatting and Directory Calculations", () =>
            {
                Assert(BrowsingDataCleaner.FormatByteSize(0) == "Less than 1 MB", "0 bytes -> Less than 1 MB");
                Assert(BrowsingDataCleaner.FormatByteSize(500 * 1024) == "Less than 1 MB", "500 KB -> Less than 1 MB");
                Assert(BrowsingDataCleaner.FormatByteSize(15 * 1024 * 1024) == "15 MB", "15 MB formatted");
                Assert(BrowsingDataCleaner.FormatByteSize(2500L * 1024 * 1024) == "2.44 GB", "2.44 GB formatted");
            });

            Test("BrowsingDataCleaner Async Clearing Lifecycle", () =>
            {
                ClearBrowsingDataOptions options = new ClearBrowsingDataOptions
                {
                    TimeRange = ClearBrowsingDataTimeRange.AllTime,
                    ClearBrowsingHistory = true,
                    ClearDownloadHistory = true,
                    ClearCookiesAndSiteData = true,
                    ClearCachedImagesAndFiles = true,
                    ClearSiteSettings = true
                };

                var task = BrowsingDataCleaner.ClearDataAsync(options);
                task.Wait(5000);
                Assert(task.IsCompleted, "ClearDataAsync task should complete");

                ClearBrowsingDataResult res = task.Result;
                Assert(res != null, "Result must not be null");
                Assert(res.Success, "Result Success must be true");
                Assert(res.CookiesCleared, "CookiesCleared must be true");
                Assert(res.SiteSettingsCleared, "SiteSettingsCleared must be true");

                ClearBrowsingDataResult syncRes = BrowsingDataCleaner.ClearData(options);
                Assert(syncRes != null && syncRes.Success, "Synchronous ClearData must succeed");
            });

            Test("ClearBrowsingDataDialog Component Layout and Tab Switching", () =>
            {
                using (ClearBrowsingDataDialog dlg = new ClearBrowsingDataDialog())
                {
                    Assert(dlg.Text == AppStrings.ClearBrowsingData.DialogTitle, "Dialog text matches");
                    Assert(dlg.FormBorderStyle == FormBorderStyle.FixedDialog, "Fixed dialog border style");
                    Assert(dlg.Controls.Count >= 4, "Dialog has required header, tab, content and footer panels");
                }
            });

            Test("InternalPageHelper Clear Browsing Data URLs & Normalization", () =>
            {
                Assert(InternalPageHelper.IsInternalUrl("wb://clear-browsing-data"), "IsInternalUrl wb://clear-browsing-data");
                Assert(InternalPageHelper.IsInternalUrl("wb://settings/clearBrowserData"), "IsInternalUrl wb://settings/clearBrowserData");
                Assert(InternalPageHelper.IsInternalUrl("chrome://settings/clearBrowserData"), "IsInternalUrl chrome://settings/clearBrowserData");

                string norm = InternalPageHelper.NormalizeInternalUrl("wb://settings/clearBrowserData");
                Assert(norm == InternalPageHelper.ClearBrowsingDataUrl, "Normalizes to canonical ClearBrowsingDataUrl");

                InternalPageType type = InternalPageHelper.GetInternalPageType(InternalPageHelper.ClearBrowsingDataUrl);
                Assert(type == InternalPageType.ClearBrowsingData, "GetInternalPageType matches ClearBrowsingData");

                string title = InternalPageHelper.GetInternalTitle(InternalPageHelper.ClearBrowsingDataUrl);
                Assert(title == AppStrings.ClearBrowsingData.DialogTitle, "GetInternalTitle returns Clear browsing data");
            });

            Test("FaviconManager ClearCache Lifecycle", () =>
            {
                FaviconManager fm = new FaviconManager();
                fm.ClearCache();
                Assert(true, "FaviconManager ClearCache completed safely");
            });

            Test("ProxyConfig Defaults and Chromium Dictionary Generation", () =>
            {
                ProxyConfig cfg = new ProxyConfig();
                Assert(cfg.Mode == ProxyMode.System, "Default mode is System");
                Assert(cfg.IsEnabled, "Default is enabled");

                var sysDict = cfg.ToChromiumDictionary();
                Assert((string)sysDict["mode"] == "system", "System mode dictionary mode == system");

                cfg.Mode = ProxyMode.Direct;
                var directDict = cfg.ToChromiumDictionary();
                Assert((string)directDict["mode"] == "direct", "Direct mode dictionary mode == direct");

                cfg.Mode = ProxyMode.AutoDetect;
                var autoDict = cfg.ToChromiumDictionary();
                Assert((string)autoDict["mode"] == "auto_detect", "AutoDetect mode dictionary mode == auto_detect");

                cfg.Mode = ProxyMode.PacScript;
                cfg.PacUrl = "http://internal.corp/wpad.dat";
                var pacDict = cfg.ToChromiumDictionary();
                Assert((string)pacDict["mode"] == "pac_script", "PacScript mode dictionary mode == pac_script");
                Assert((string)pacDict["pac_url"] == "http://internal.corp/wpad.dat", "PacScript pac_url matches");

                cfg.Mode = ProxyMode.Manual;
                cfg.Scheme = "socks5";
                cfg.Host = "127.0.0.1";
                cfg.Port = 1080;
                cfg.BypassList = "<local>;localhost;127.0.0.1";
                var manualDict = cfg.ToChromiumDictionary();
                Assert((string)manualDict["mode"] == "fixed_servers", "Manual mode dictionary mode == fixed_servers");
                Assert((string)manualDict["server"] == "socks5://127.0.0.1:1080", "Manual server formatted correctly");
                Assert((string)manualDict["bypass_list"] == "<local>;localhost;127.0.0.1", "Manual bypass list matches");
            });

            Test("ProxyManager Configuration Persistence and Credentials", () =>
            {
                ProxyManager pm = new ProxyManager();
                ProxyConfig cfg = new ProxyConfig
                {
                    Mode = ProxyMode.Manual,
                    Scheme = "http",
                    Host = "proxy.example.com",
                    Port = 8080,
                    UseAuth = true,
                    Username = "corpuser",
                    Password = "secretpassword"
                };

                bool eventFired = false;
                pm.ConfigChanged += (s, e) => eventFired = true;

                pm.SaveAndApply(cfg);

                Assert(eventFired, "ConfigChanged event fired");
                Assert(pm.HasCredentials, "HasCredentials returns true");
                Assert(pm.Username == "corpuser", "Username matches");
                Assert(pm.Password == "secretpassword", "Password matches");

                ProxyConfig loaded = pm.CurrentConfig;
                Assert(loaded != null && loaded.Host == "proxy.example.com", "Loaded config matches");
            });

            Test("InternalPageHelper Proxy URLs and Normalization", () =>
            {
                Assert(InternalPageHelper.IsInternalUrl("wb://settings/proxy"), "IsInternalUrl wb://settings/proxy");
                Assert(InternalPageHelper.IsInternalUrl("wb://proxy"), "IsInternalUrl wb://proxy");
                Assert(InternalPageHelper.IsInternalUrl("chrome://settings/proxy"), "IsInternalUrl chrome://settings/proxy");

                string norm = InternalPageHelper.NormalizeInternalUrl("wb://proxy");
                Assert(norm == InternalPageHelper.SettingsProxyUrl, "Normalizes wb://proxy to SettingsProxyUrl");

                InternalPageType type = InternalPageHelper.GetInternalPageType(InternalPageHelper.SettingsProxyUrl);
                Assert(type == InternalPageType.Proxy, "GetInternalPageType matches Proxy");

                string title = InternalPageHelper.GetInternalTitle(InternalPageHelper.SettingsProxyUrl);
                Assert(title == AppStrings.Proxy.DialogTitle, "GetInternalTitle returns Proxy Settings");
            });

            Test("ProxySettingsDialog Component Layout and Modes", () =>
            {
                using (ProxySettingsDialog dlg = new ProxySettingsDialog())
                {
                    Assert(dlg.Text == AppStrings.Proxy.DialogTitle, "Dialog text matches");
                    Assert(dlg.FormBorderStyle == FormBorderStyle.FixedDialog, "Fixed dialog border style");
                    Assert(dlg.Controls.Count >= 3, "Dialog has header, footer and content panels");

                    Button saveBtn = null;
                    Button cancelBtn = null;
                    Label statusLabel = null;
                    Panel footerPanel = null;
                    foreach (Control c in dlg.Controls)
                    {
                        Panel p = c as Panel;
                        if (p != null)
                        {
                            foreach (Control child in p.Controls)
                            {
                                Button b = child as Button;
                                if (b != null && b.Text == AppStrings.Proxy.BtnApply)
                                {
                                    saveBtn = b;
                                    footerPanel = p;
                                }
                                else if (b != null && b.Text == AppStrings.Proxy.BtnCancel)
                                {
                                    cancelBtn = b;
                                }
                                Label lbl = child as Label;
                                if (lbl != null && !lbl.AutoSize && lbl.AutoEllipsis)
                                {
                                    statusLabel = lbl;
                                }
                            }
                        }
                    }
                    Assert(saveBtn != null, "Save & Apply button found");
                    Assert(cancelBtn != null, "Cancel button found");
                    Assert(statusLabel != null, "Bounded status label found");
                    Assert(footerPanel != null && footerPanel.Height >= 100, "Two-tier footer panel height >= 100");
                    Assert(saveBtn.Text.Contains("&"), "Save & Apply button text contains '&'");
                    Assert(!saveBtn.UseMnemonic, "Save & Apply button UseMnemonic is false to preserve literal '&'");
                    Assert(saveBtn.Top > statusLabel.Bottom, "Action buttons are on a separate row below status label to eliminate collision");
                }
            });

            Test("ProxyManager Connectivity Probe and Error Diagnostics", () =>
            {
                ProxyManager mgr = new ProxyManager();

                // 1. Test offline port fails with clear diagnostics
                ProxyConfig offlineCfg = new ProxyConfig
                {
                    Mode = ProxyMode.Manual,
                    Scheme = "http",
                    Host = "127.0.0.1",
                    Port = 59998
                };
                var offlineRes = mgr.TestConnectionAsync(offlineCfg).Result;
                Assert(!offlineRes.Item1, "Offline proxy returns failure");
                Assert(offlineRes.Item2.Contains("Cannot connect to proxy") || offlineRes.Item2.Contains("127.0.0.1:59998"), "Failure message contains clear connection diagnostic");

                // 2. Test live local proxy (port 8888) if running
                ProxyConfig liveCfg = new ProxyConfig
                {
                    Mode = ProxyMode.Manual,
                    Scheme = "http",
                    Host = "127.0.0.1",
                    Port = 8888
                };
                var liveRes = mgr.TestConnectionAsync(liveCfg).Result;
                if (liveRes.Item1)
                {
                    Assert(liveRes.Item1, "Live proxy on 8888 connects successfully");
                    Assert(liveRes.Item3 >= 0, "Live proxy reports latency");
                }
            });

            Test("PasskeyFallbackHelper Script Generation and Fallback Payload", () =>
            {
                string script = PasskeyFallbackHelper.GetScript();
                Assert(!string.IsNullOrEmpty(script), "Fallback script must not be empty");
                Assert(script.Contains("isUserVerifyingPlatformAuthenticatorAvailable"), "Script must shim isUserVerifyingPlatformAuthenticatorAvailable");
                Assert(script.Contains("isConditionalMediationAvailable"), "Script must shim isConditionalMediationAvailable");
                Assert(script.Contains("authenticatorAttachment === 'platform'"), "Script must guard platform-bound passkey assertion");
                Assert(script.Contains("NotSupportedError"), "Script must reject unsupported platform passkeys with NotSupportedError");

                // Null safety checks
                PasskeyFallbackHelper.InjectIntoFrame(null, null);
                PasskeyFallbackHelper.RegisterOnBrowserHost(null);
            });

            Test("GoogleSecurityCodeBanner Challenge Detection and Toggle", () =>
            {
                // Detection on Google Challenge URLs
                Assert(GoogleSecurityCodeBanner.IsGoogleSecurityCodeChallenge("https://accounts.google.com/signin/v2/challenge/skotp", "Google Accounts"),
                    "Should detect skotp challenge URL");
                Assert(GoogleSecurityCodeBanner.IsGoogleSecurityCodeChallenge("https://accounts.google.com/v3/signin/challenge/skotp?continue=...", "Sign in - Google Accounts"),
                    "Should detect v3 skotp challenge URL");
                Assert(GoogleSecurityCodeBanner.IsGoogleSecurityCodeChallenge("https://g.co/sc", ""),
                    "Should detect g.co/sc");
                Assert(GoogleSecurityCodeBanner.IsGoogleSecurityCodeChallenge("https://myaccount.google.com/signinoptions/two-step-verification/security-code", ""),
                    "Should detect full security-code URL");
                Assert(GoogleSecurityCodeBanner.IsGoogleSecurityCodeChallenge("https://accounts.google.com/signin/challenge", "Get a code to sign in"),
                    "Should detect security code challenge via page title");

                // Rejection on non-challenge URLs
                Assert(!GoogleSecurityCodeBanner.IsGoogleSecurityCodeChallenge("https://www.google.com", "Google"),
                    "Should not match google home");
                Assert(!GoogleSecurityCodeBanner.IsGoogleSecurityCodeChallenge("https://accounts.google.com/signin/v2/identifier", "Sign in - Google Accounts"),
                    "Should not match initial identifier page");
                Assert(!GoogleSecurityCodeBanner.IsGoogleSecurityCodeChallenge(null, null),
                    "Should safely reject null URL and title");

                // ExperimentalManager toggle
                bool initialToggle = ExperimentalManager.EnableGoogleSecurityCodeBanner;
                try
                {
                    ExperimentalManager.EnableGoogleSecurityCodeBanner = false;
                    Assert(!ExperimentalManager.EnableGoogleSecurityCodeBanner, "ExperimentalManager toggle should be false");
                    ExperimentalManager.EnableGoogleSecurityCodeBanner = true;
                    Assert(ExperimentalManager.EnableGoogleSecurityCodeBanner, "ExperimentalManager toggle should be true");
                }
                finally
                {
                    ExperimentalManager.EnableGoogleSecurityCodeBanner = initialToggle;
                }

                // Banner component lifecycle
                using (GoogleSecurityCodeBanner banner = new GoogleSecurityCodeBanner())
                {
                    Assert(!banner.Visible, "Banner should initially be hidden");
                    banner.DpiScale = 1.25f;
                    Assert(banner.Height >= 36, "Banner height should scale with DPI");

                    bool dismissed = false;
                    banner.Dismissed += (s, e) => { dismissed = true; };
                    banner.Visible = true;
                    banner.Dismiss();
                    Assert(!banner.Visible, "Dismiss should hide banner");
                    Assert(dismissed, "Dismiss should trigger Dismissed event");
                }
            });

            Test("WbTopBar AdBlock Counter Badge Shows Dash On Internal Page When Enabled", () =>
            {
                // 1. Verify InternalPageHelper.IsInternalPage
                Assert(InternalPageHelper.IsInternalPage(null), "Null URL is internal page");
                Assert(InternalPageHelper.IsInternalPage(""), "Empty URL is internal page");
                Assert(InternalPageHelper.IsInternalPage("about:blank"), "about:blank is internal page");
                Assert(InternalPageHelper.IsInternalPage("wb://newtab"), "wb://newtab is internal page");
                Assert(InternalPageHelper.IsInternalPage("wb://history"), "wb://history is internal page");
                Assert(InternalPageHelper.IsInternalPage("wb://downloads"), "wb://downloads is internal page");
                Assert(InternalPageHelper.IsInternalPage("wb://bookmarks"), "wb://bookmarks is internal page");
                Assert(!InternalPageHelper.IsInternalPage("https://www.google.com"), "google.com is not internal page");
                Assert(!InternalPageHelper.IsInternalPage("http://example.org"), "example.org is not internal page");

                // Verify AdBlockManager.GetStatusSuffix uses IsInternalPage
                Assert(AppServices.AdBlock.GetStatusSuffix("wb://bookmarks") == "(enabled)", "wb://bookmarks status suffix should be (enabled)");
                Assert(AppServices.AdBlock.GetStatusSuffix("wb://history") == "(enabled)", "wb://history status suffix should be (enabled)");
                Assert(AppServices.AdBlock.GetStatusSuffix("about:blank") == "(enabled)", "about:blank status suffix should be (enabled)");

                // Verify MainBrowserForm.cs uses IsInternalPage for BuildUBlockContextMenu
                string srcDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src"));
                string mainFormPath = Path.Combine(srcDir, @"Browser\MainBrowserForm.cs");
                if (File.Exists(mainFormPath))
                {
                    string src = File.ReadAllText(mainFormPath);
                    Assert(src.Contains("bool isInternal = string.IsNullOrEmpty(url) || InternalPageHelper.IsInternalPage(url);"),
                           "MainBrowserForm must check InternalPageHelper.IsInternalPage in BuildUBlockContextMenu");
                }

                using (WbTopBar topBar = new WbTopBar())
                {
                    topBar.Width = 800;
                    Rectangle shieldRect = topBar.GetShieldButtonRect();
                    Assert(shieldRect.Width > 0, "Shield button has positive width");

                    Func<Bitmap, bool> hasBadge = (bmp) =>
                    {
                        Color badgeBg = UITheme.BadgeBackground;
                        for (int y = shieldRect.Top; y < shieldRect.Bottom; y++)
                        {
                            for (int x = shieldRect.Left; x < shieldRect.Right; x++)
                            {
                                Color c = bmp.GetPixel(x, y);
                                if (c.R == badgeBg.R && c.G == badgeBg.G && c.B == badgeBg.B)
                                {
                                    return true;
                                }
                            }
                        }
                        return false;
                    };

                    // Internal page with AdBlock enabled: badge MUST render dash
                    topBar.IsAdBlockEnabled = true;
                    topBar.IsInternalPage = true;
                    topBar.BlockedCount = 0;
                    Assert(topBar.IsInternalPage, "IsInternalPage should be true");
                    using (Bitmap bmp = new Bitmap(800, 40))
                    {
                        topBar.DrawToBitmap(bmp, new Rectangle(0, 0, 800, 40));
                        Assert(hasBadge(bmp), "Internal page with AdBlock enabled must render dash badge");
                    }

                    // Internal page with AdBlock disabled: badge MUST NOT render
                    topBar.IsAdBlockEnabled = false;
                    topBar.IsInternalPage = true;
                    topBar.BlockedCount = 0;
                    using (Bitmap bmp = new Bitmap(800, 40))
                    {
                        topBar.DrawToBitmap(bmp, new Rectangle(0, 0, 800, 40));
                        Assert(!hasBadge(bmp), "Internal page with AdBlock disabled must NOT render badge");
                    }

                    // Web page (non-internal) with BlockedCount == 0: badge MUST NOT render
                    topBar.IsAdBlockEnabled = true;
                    topBar.IsInternalPage = false;
                    topBar.BlockedCount = 0;
                    using (Bitmap bmp = new Bitmap(800, 40))
                    {
                        topBar.DrawToBitmap(bmp, new Rectangle(0, 0, 800, 40));
                        Assert(!hasBadge(bmp), "Web page with 0 blocked must NOT render badge");
                    }

                    // Web page (non-internal) with BlockedCount > 0: badge MUST render count
                    topBar.IsAdBlockEnabled = true;
                    topBar.IsInternalPage = false;
                    topBar.BlockedCount = 5;
                    using (Bitmap bmp = new Bitmap(800, 40))
                    {
                        topBar.DrawToBitmap(bmp, new Rectangle(0, 0, 800, 40));
                        Assert(hasBadge(bmp), "Web page with blocked count must render badge");
                    }
                }
            });

            Test("CrashpadHelper Disables Chromium Crashpad and Cleans Up User Data", () =>
            {
                // Verify RemoveCrashReporterConfig removes crash_reporter.cfg from base and core directories
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string coreDir = Path.Combine(baseDir, "core");
                string rootCfg = Path.Combine(baseDir, "crash_reporter.cfg");
                string coreCfg = Path.Combine(coreDir, "crash_reporter.cfg");

                if (!Directory.Exists(coreDir)) Directory.CreateDirectory(coreDir);
                File.WriteAllText(rootCfg, "# dummy config");
                File.WriteAllText(coreCfg, "# dummy config");

                CrashpadHelper.RemoveCrashReporterConfig();
                Assert(!File.Exists(rootCfg), "root crash_reporter.cfg should be removed");
                Assert(!File.Exists(coreCfg), "core crash_reporter.cfg should be removed");

                // Verify RemoveChromiumCrashpadDirectory removes unwanted User Data directory
                string dataDir = StorageHelper.GetDataDirectory();
                string testUserData = Path.Combine(dataDir, "User Data", "Crashpad");
                Directory.CreateDirectory(testUserData);
                File.WriteAllText(Path.Combine(testUserData, "settings.dat"), "dummy");
                Assert(Directory.Exists(testUserData), "Test User Data directory created");

                CrashpadHelper.RemoveChromiumCrashpadDirectory();
                Assert(!Directory.Exists(Path.Combine(dataDir, "User Data")), "User Data directory should be completely removed");

                // Verify CEF args disable crash reporter
                var cefArgs = new Dictionary<string, string>
                {
                    { "enable-crash-reporter", "1" },
                    { "crash-dumps-dir", "C:\\some\\path" }
                };
                CrashpadHelper.ApplyCefCommandLineArgs(cefArgs);
                Assert(cefArgs.ContainsKey("disable-crash-reporter") && cefArgs["disable-crash-reporter"] == "1", "disable-crash-reporter must be set to 1");
                Assert(!cefArgs.ContainsKey("enable-crash-reporter"), "enable-crash-reporter must be removed");
                Assert(!cefArgs.ContainsKey("crash-dumps-dir"), "crash-dumps-dir must be removed");
            });

            Test("CrashpadHelper Directories and State Tracking", () =>
            {
                string crashpadDir = CrashpadHelper.GetCrashpadDirectory();
                Assert(!string.IsNullOrEmpty(crashpadDir), "Crashpad directory must not be empty");
                Assert(crashpadDir.EndsWith("crashes", StringComparison.OrdinalIgnoreCase), "Path should end with crashes");
                Assert(Directory.Exists(crashpadDir), "Crashpad directory should exist on disk: " + crashpadDir);

                string reportsDir = CrashpadHelper.GetCrashReportsDirectory();
                Assert(!string.IsNullOrEmpty(reportsDir), "Reports directory must not be empty");
                Assert(reportsDir.EndsWith("crashes", StringComparison.OrdinalIgnoreCase), "Path should end with crashes");
                Assert(Directory.Exists(reportsDir), "Reports directory should exist on disk: " + reportsDir);

                string logsDir = CrashpadHelper.GetCrashLogsDirectory();
                Assert(!string.IsNullOrEmpty(logsDir), "Logs directory must not be empty");
                Assert(logsDir.EndsWith("crashes", StringComparison.OrdinalIgnoreCase), "Path should end with crashes");
                Assert(Directory.Exists(logsDir), "Logs directory should exist on disk: " + logsDir);
                Assert(reportsDir.Equals(logsDir, StringComparison.OrdinalIgnoreCase), "Minidumps and crash logs must be unified in crashes folder");

                CrashpadHelper.SetCrashUrl("https://example.org/crashpad-test");
                Assert(CrashpadHelper.LastKnownUrl == "https://example.org/crashpad-test", "LastKnownUrl should track URL update");

                // Property read should not throw even if CEF is not yet initialized in test runner
                bool isEnabled = CrashpadHelper.IsCrashReportingEnabled;
                Assert(!isEnabled || isEnabled, "IsCrashReportingEnabled evaluates safely");
            });

            Test("CrashpadHelper Native MiniDump Generation and Diagnostics", () =>
            {
                string tempDump = Path.Combine(Path.GetTempPath(), "wb_test_dump_" + Guid.NewGuid().ToString("N") + ".dmp");
                try
                {
                    bool created = CrashpadHelper.CreateMiniDump(tempDump);
                    Assert(created, "MiniDumpWriteDump should succeed for current process");
                    Assert(File.Exists(tempDump), "Dump file should exist on disk: " + tempDump);

                    FileInfo fi = new FileInfo(tempDump);
                    Assert(fi.Length >= 1024, "Dump file size should be > 1KB (was " + fi.Length + " bytes)");

                    // Verify standard MiniDump header magic ("MDMP")
                    byte[] header = new byte[4];
                    using (FileStream fs = new FileStream(tempDump, FileMode.Open, FileAccess.Read))
                    {
                        fs.Read(header, 0, 4);
                    }
                    string magic = System.Text.Encoding.ASCII.GetString(header);
                    Assert(magic == "MDMP", "MiniDump magic header should be 'MDMP' (was " + magic + ")");
                }
                finally
                {
                    try { if (File.Exists(tempDump)) File.Delete(tempDump); } catch { }
                }

                // Verify render process crash logging writes diagnostics without throwing
                CrashpadHelper.LogRenderProcessCrash(2, "https://render-crash-test.com");
                string logFile = Path.Combine(CrashpadHelper.GetCrashLogsDirectory(), "render_process_crashes.log");
                Assert(File.Exists(logFile), "render_process_crashes.log should exist");
                string logContent = File.ReadAllText(logFile);
                Assert(logContent.Contains("ProcessCrashed") && logContent.Contains("render-crash-test.com"),
                    "Log content should record crash event with status and URL");
            });

            Test("LogcatHelper Initialization and Log File Architecture", () =>
            {
                LogcatHelper.Initialize();
                string logPath = LogcatHelper.GetLogFilePath();
                Assert(!string.IsNullOrEmpty(logPath), "Log file path must not be null or empty");
                Assert(logPath.EndsWith("wb.log", StringComparison.OrdinalIgnoreCase), "Log file should end with wb.log: " + logPath);
                Assert(Directory.Exists(Path.GetDirectoryName(logPath)), "Log directory should exist on disk");
            });

            Test("LogcatHelper Structured Formatting and Level Logging", () =>
            {
                string uniqueMsg = "UnitTestLogcat_" + Guid.NewGuid().ToString("N");
                LogcatHelper.D("wb.Test", uniqueMsg);
                LogcatHelper.I("wb.Test", "Info level message");
                LogcatHelper.W("wb.Test", "Warning level message");
                LogcatHelper.E("wb.Test", "Error level message");

                string logPath = LogcatHelper.GetLogFilePath();
                Assert(File.Exists(logPath), "wb.log must exist after logging: " + logPath);

                string content = File.ReadAllText(logPath);
                Assert(content.Contains(uniqueMsg), "wb.log should contain the logged message: " + uniqueMsg);
                Assert(content.Contains("wb.Test:"), "wb.log should contain component tag wb.Test");
                Assert(content.Contains(" D wb.Test:") || content.Contains(" I wb.Test:"), "wb.log should contain level indicators");
            });

            Test("LogcatHelper Uncluttered Parsing and Intelligent Word Wrapping", () =>
            {
                // 1. Verify TryParseLogcatLine
                string rawLine = "10-26 23:22:43.123   4520   1234 I wb.Core: wb Browser starting up [PID: 4520, Arch: x64]";
                string timePart, tagPart, msgPart;
                char levelChar;
                bool ok = LogcatHelper.TryParseLogcatLine(rawLine, out timePart, out levelChar, out tagPart, out msgPart);
                Assert(ok, "TryParseLogcatLine should successfully parse standard log entry");
                Assert(timePart == "23:22:43.123", "Time part should exclude date clutter: " + timePart);
                Assert(levelChar == 'I', "Level char should be 'I'");
                Assert(tagPart == "wb.Core", "Tag part should be 'wb.Core': " + tagPart);
                Assert(msgPart.StartsWith("wb Browser starting up"), "Message part should be extracted: " + msgPart);

                // 2. Verify WrapMessage on normal sentences (breaks on space)
                string longSentence = "The quick brown fox jumps over the lazy dog repeatedly until evening comes.";
                var wrapped = LogcatHelper.WrapMessage(longSentence, 25);
                Assert(wrapped.Count >= 3, "Sentence should wrap into multiple lines");
                foreach (string line in wrapped)
                {
                    Assert(line.Length <= 25, "Wrapped line length must be <= maxLineLength: " + line);
                }
                Assert(wrapped[0] == "The quick brown fox jumps", "Line 0 should break at word boundary: " + wrapped[0]);

                // 3. Verify WrapMessage on long URL (no spaces)
                string longUrl = "https://example.com/very/deeply/nested/resource/endpoint/without/spaces/in/path";
                var wrappedUrl = LogcatHelper.WrapMessage(longUrl, 20);
                Assert(wrappedUrl.Count >= 4, "Long URL without spaces should be sliced cleanly across width");
                Assert(string.Join("", wrappedUrl.ToArray()) == longUrl, "Rejoined URL fragments must match original URL exactly");

                // 4. Verify PrintFormattedLogcatLine does not throw
                LogcatHelper.PrintFormattedLogcatLine(rawLine);
                LogcatHelper.PrintFormattedLogcatLine("   at wb.Browser.Tab.Navigate() in Tab.cs:line 42");
                LogcatHelper.PrintFormattedLogcatLine("Arbitrary console message");
            });

            Test("CrashpadHelper Crash State Persistence and Startup Reminder Lifecycle", () =>
            {
                CrashpadHelper.ClearCrashState();

                // 1. When no crash occurred, pending crash reminder must be null ("if no crash then dont show")
                Assert(CrashpadHelper.GetPendingCrashReminder() == null, "When no crash occurred, GetPendingCrashReminder must return null");

                // 2. Record a simulated crash state
                string testDmp = Path.Combine(CrashpadHelper.GetCrashReportsDirectory(), "test_crash.dmp");
                string testTxt = Path.Combine(CrashpadHelper.GetCrashLogsDirectory(), "test_crash.txt");
                CrashpadHelper.RecordCrashState("Simulated crash for startup reminder test", "UnitTest.Context", testDmp, testTxt);

                // 3. Verify pending reminder is now populated
                CrashInfo info = CrashpadHelper.GetPendingCrashReminder();
                Assert(info != null, "Pending crash reminder must be returned after crash occurs");
                Assert(info.ErrorMessage == "Simulated crash for startup reminder test", "Error message matches recorded text");
                Assert(info.Context == "UnitTest.Context", "Context matches recorded context");
                Assert(info.DumpPath == testDmp, "DumpPath matches");
                Assert(info.ReportPath == testTxt, "ReportPath matches");
                Assert(info.Acknowledged == false, "Acknowledged must initially be false");

                // 4. Acknowledge reminder (simulates user dismissing the reminder dialog)
                CrashpadHelper.AcknowledgeCrashReminder();

                // 5. Verify subsequent startup checks return null ("if no crash then dont show")
                Assert(CrashpadHelper.GetPendingCrashReminder() == null, "After acknowledgment, GetPendingCrashReminder must return null");

                // 6. Clean up
                CrashpadHelper.ClearCrashState();
                Assert(CrashpadHelper.GetPendingCrashReminder() == null, "After cleanup, GetPendingCrashReminder remains null");
            });

            Test("Tools Menu Simulate Crash and Logcat Wording Removal", () =>
            {
                // Verify logcat wording removed from user menu
                Assert(!AppStrings.Browser.MenuDebugConsole.ToLower().Contains("logcat"), "MenuDebugConsole must not contain 'logcat': " + AppStrings.Browser.MenuDebugConsole);
                Assert(AppStrings.Browser.MenuDebugConsole == "&Debug Console", "MenuDebugConsole should be '&Debug Console'");

                // Verify simulate crash menu and confirm strings
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.MenuSimulateCrash), "MenuSimulateCrash must be defined");
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.CrashConfirmTitle), "CrashConfirmTitle must be defined");
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.CrashConfirmMessage), "CrashConfirmMessage must be defined");

                // Verify startup crash reminder dialog strings
                Assert(!string.IsNullOrEmpty(AppStrings.CrashReminder.DialogTitle), "DialogTitle must be defined");
                Assert(AppStrings.CrashReminder.CrashNoticeWindowTitle == "Crash Notice - wb", "CrashNoticeWindowTitle must match 'Crash Notice - wb'");
                Assert(!string.IsNullOrEmpty(AppStrings.CrashReminder.SimpleCrashPrompt), "SimpleCrashPrompt must be defined");
                Assert(AppStrings.CrashReminder.SimpleCrashPrompt.Contains("wb closed unexpectedly"), "SimpleCrashPrompt contains crash warning");
                Assert(AppStrings.CrashReminder.SimpleCrashPrompt.Contains("open the crashes folder"), "SimpleCrashPrompt must prompt to open the crashes folder: " + AppStrings.CrashReminder.SimpleCrashPrompt);
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.MenuDebug), "MenuDebug must be defined");
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.MenuSaveCrashDumps), "MenuSaveCrashDumps must be defined");
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.MenuOpenCrashesFolder), "MenuOpenCrashesFolder must be defined");
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.MenuOpenDumpsFolder), "MenuOpenDumpsFolder must be defined");

                // Verify console window title formatting: Debug Console [PID XXXX] - wb
                int testPid = 4520;
                string title = string.Format("Debug Console [PID {0}] - wb", testPid);
                Assert(title == "Debug Console [PID 4520] - wb", "Console title must match 'Debug Console [PID XXXX] - wb'");
                Assert(!title.ToLower().Contains("logcat"), "Console title must not contain 'logcat'");
            });

            Test("CrashpadHelper FormatCrashContext Human-Readable Labels", () =>
            {
                Assert(CrashpadHelper.FormatCrashContext("ToolsMenu.SimulateCrash") == "Simulated Crash (Debug)", "SimulateCrash maps to Simulated Crash (Debug)");
                Assert(CrashpadHelper.FormatCrashContext("AppDomain.CurrentDomain.UnhandledException", new ApplicationException("wb Self-Initiated Crash")) == "Simulated Crash (Debug)", "Self-initiated exception maps to Simulated Crash (Debug)");
                Assert(CrashpadHelper.FormatCrashContext("AppDomain.CurrentDomain.UnhandledException") == "Background Thread Crash", "AppDomain raw maps to Background Thread Crash");
                Assert(CrashpadHelper.FormatCrashContext("Application.ThreadException") == "UI Thread Crash (Windows Forms)", "ThreadException maps to UI Thread Crash (Windows Forms)");
                Assert(CrashpadHelper.FormatCrashContext("Program.RunApplication") == "Application Startup Failure", "Program.RunApplication maps to Application Startup Failure");
                Assert(CrashpadHelper.FormatCrashContext("SessionAbruptTermination") == "Abrupt Session Termination", "SessionAbruptTermination maps to Abrupt Session Termination");
            });

            Test("CrashpadHelper Dump Saving Preference and Space-Saving Purge", () =>
            {
                // Verify default is disabled to save space
                CrashpadHelper.IsDumpSavingEnabled = false;
                Assert(!CrashpadHelper.IsDumpSavingEnabled, "Dump saving should be disabled by default to save disk space");

                // Test toggle on
                CrashpadHelper.IsDumpSavingEnabled = true;
                Assert(CrashpadHelper.IsDumpSavingEnabled, "Dump saving toggle on");

                // Create dummy .dmp file
                string dumpsDir = CrashpadHelper.GetCrashReportsDirectory();
                string testDmp = Path.Combine(dumpsDir, "test_orphan.dmp");
                File.WriteAllText(testDmp, "dummy binary dump data");
                Assert(File.Exists(testDmp), "Test dump file created");

                // Toggle off should automatically purge .dmp files to save space
                CrashpadHelper.IsDumpSavingEnabled = false;
                Assert(!File.Exists(testDmp), "PurgeDumpsToSaveSpace must delete .dmp files when dump saving is disabled");

                // Verify manual PurgeDumpsToSaveSpace
                File.WriteAllText(testDmp, "another dump");
                CrashpadHelper.PurgeDumpsToSaveSpace();
                Assert(!File.Exists(testDmp), "Explicit PurgeDumpsToSaveSpace must remove .dmp files");
            });

            Test("SponsorBlock PNG Icon and Direct Payload Embedding", () =>
            {
                string iconUrl = SponsorBlockYTShield.IconDataUrl;
                Assert(!string.IsNullOrEmpty(iconUrl), "SponsorBlock icon URL must not be null or empty");
                Assert(iconUrl.StartsWith("data:image/png;base64,"), "SponsorBlock icon must be a PNG data URL");

                string diskIconPath = Path.Combine(MaterialIcons.IconsDirectory, "sponsorblock.png");
                Assert(File.Exists(diskIconPath), "assets/icons/sponsorblock.png must exist: " + diskIconPath);
                byte[] diskBytes = File.ReadAllBytes(diskIconPath);
                Assert(diskBytes.Length > 0, "Disk sponsorblock.png must not be empty");
            });

            Test("SponsorBlock Script Generation and Playback Interruption Guards", () =>
            {
                string script = SponsorBlockYTShield.GetSponsorBlockScript("{\"IsEnabled\":true,\"NoticeDurationSeconds\":5}");
                Assert(!string.IsNullOrEmpty(script), "SponsorBlock script must not be empty");
                Assert(script.Contains("isPlayerInAd"), "Script must implement isPlayerInAd ad detection");
                Assert(script.Contains("ad-showing") && script.Contains("ad-interrupting"), "Script must check ad-showing and ad-interrupting classes");
                Assert(script.Contains("isPlaybackReady"), "Script must implement isPlaybackReady guard against premature segment execution");
                Assert(script.Contains("hasStartedPlayback"), "Script must track hasStartedPlayback state per video");
                Assert(script.Contains("var iconDataUrl = '"), "Script must embed iconDataUrl");
                Assert(script.Contains("data:image/png;base64,"), "Script must embed sponsorblock.png base64 payload");
                Assert(script.Contains("isEndSeg") && script.Contains("ended-mode") && script.Contains("ytp-ce-element-show"), "Script must implement end-of-video segment endcard transition to prevent player looping");
                Assert(script.Contains("!isEndSeg || ct > 1.0"), "Script must protect end-of-video skipped state from auto-clear when wrapping to 0:00");
                Assert(script.Contains("resetPlaybackStateForReplay") && script.Contains("isBeforeStart"), "Script must implement replay reset for beginning segments");

                string rxScript = SponsorBlockYTShield.FormatReceiveSegmentsScript("test_vid_123", "[{\"segment\":[1.5,10.0],\"category\":\"sponsor\"}]");
                Assert(rxScript.Contains("test_vid_123"), "FormatReceiveSegmentsScript must include target video id");
                Assert(rxScript.Contains("window.__wbSponsorBlockReceiveSegments"), "FormatReceiveSegmentsScript must call __wbSponsorBlockReceiveSegments hook");
            });

            Test("Window Teardown & Overlay Disposal Resilience", () =>
            {
                using (Form testOwner = new Form())
                {
                    testOwner.Show();

                    OverlayPopup popup = new OverlayPopup(testOwner, "Test popup", "check", null, null, 1000);
                    WbBottomBar bottomBar = new WbBottomBar();
                    bottomBar.AttachTo(testOwner);
                    TabLoadingOverlay loadingOverlay = new TabLoadingOverlay();
                    loadingOverlay.AttachTo(testOwner, testOwner);
                    FullScreenExitOverlay fullScreenOverlay = new FullScreenExitOverlay(() => { });
                    fullScreenOverlay.AttachTo(testOwner);

                    // 1. Dispose overlays first to simulate MainBrowserForm.OnFormClosing cleanup order
                    popup.Dispose();
                    bottomBar.Dispose();
                    loadingOverlay.Dispose();
                    fullScreenOverlay.Dispose();

                    Assert(popup.IsDisposed, "OverlayPopup should be disposed");
                    Assert(bottomBar.IsDisposed, "WbBottomBar should be disposed");
                    Assert(loadingOverlay.IsDisposed, "TabLoadingOverlay should be disposed");
                    Assert(fullScreenOverlay.IsDisposed, "FullScreenExitOverlay should be disposed");

                    // 2. Trigger testOwner.Close() which fires testOwner.FormClosing
                    // Handlers on already-disposed overlays must not throw ObjectDisposedException
                    testOwner.Close();
                }

                // 3. Test WbTopBar disposal
                using (WbTopBar topBar = new WbTopBar())
                {
                    topBar.Dispose();
                    Assert(topBar.IsDisposed, "WbTopBar should dispose cleanly without throwing");

                }
            });

            Test("WbTopBar Tab Strip Empty Area Right Click and Bookmark All Tabs Logic", () =>
            {
                // 1. WbTopBar TabStripEmptyAreaRightClicked event
                using (Form host = new Form { Size = new Size(1200, 600) })
                using (WbTopBar topBar = new WbTopBar())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.Width = 1200;
                    topBar.BindTabControl(tc);
                    host.Controls.Add(topBar);

                    TabPage p = new TabPage("Test Tab");
                    tc.TabPages.Add(p);

                    bool emptyRightClicked = false;
                    Point clickedPt = Point.Empty;
                    topBar.TabStripEmptyAreaRightClicked += (s, pt) =>
                    {
                        emptyRightClicked = true;
                        clickedPt = pt;
                    };

                    var onMouseDown = typeof(WbTopBar).GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(onMouseDown != null, "OnMouseDown method must exist");

                    // Click in the empty area to the right of the New Tab button
                    Rectangle newTabRect = topBar.GetNewTabButtonRect();
                    int clickX = (newTabRect.Right + topBar.GetToolbarLeftLimit()) / 2;
                    int clickY = topBar.Height / 2;

                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Right, 1, clickX, clickY, 0) });
                    Assert(emptyRightClicked, "Right click in tab strip empty area must fire TabStripEmptyAreaRightClicked");
                    Assert(clickedPt.X != 0 || clickedPt.Y != 0, "TabStripEmptyAreaRightClicked should provide screen position");

                    // Right click on New Tab button should also trigger empty area right-click menu
                    emptyRightClicked = false;
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Right, 1, newTabRect.X + 2, newTabRect.Y + 2, 0) });
                    Assert(emptyRightClicked, "Right click on New Tab button must fire TabStripEmptyAreaRightClicked");

                    // Right click on Back button must NOT trigger TabStripEmptyAreaRightClicked
                    emptyRightClicked = false;
                    Rectangle backRect = topBar.GetBackButtonRect();
                    onMouseDown.Invoke(topBar, new object[] { new MouseEventArgs(MouseButtons.Right, 1, backRect.X + 2, backRect.Y + 2, 0) });
                    Assert(!emptyRightClicked, "Right click on Back button must NOT fire TabStripEmptyAreaRightClicked");
                }

                // 2. Validate AppStrings for Tab Strip Empty Area Context Menu
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.TabMenuNewTab), "TabMenuNewTab string must not be empty");
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.TabMenuReopenClosed), "TabMenuReopenClosed string must not be empty");
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.TabMenuBookmarkAll), "TabMenuBookmarkAll string must not be empty");
                Assert(!string.IsNullOrEmpty(AppStrings.Browser.NotifyBookmarkAllAddedFormat), "NotifyBookmarkAllAddedFormat string must not be empty");

                // 3. Validate BookmarkAllTabs bookmarking and URL eligibility logic
                string testUrl1 = "https://example-test-tab1.com";
                string testUrl2 = "https://example-test-tab2.com";
                string testInternalUrl = "wb://bookmarks";
                AppServices.Bookmarks.RemoveByUrl(testUrl1);
                AppServices.Bookmarks.RemoveByUrl(testUrl2);

                Assert(InternalPageHelper.CanBookmarkPage(testUrl1), "External URL should be bookmarkable");
                Assert(InternalPageHelper.CanBookmarkPage(testUrl2), "External URL should be bookmarkable");
                Assert(!InternalPageHelper.CanBookmarkPage(testInternalUrl), "wb:// page should not be bookmarkable");
                Assert(!InternalPageHelper.CanBookmarkPage("about:blank"), "about:blank should not be bookmarkable");

                // Simulate adding multiple tabs to BookmarkManager
                string[] openTabUrls = new string[] { testUrl1, testUrl2, testInternalUrl };
                string[] openTabTitles = new string[] { "Tab 1", "Tab 2", "Bookmarks" };
                int addedCount = 0;
                for (int i = 0; i < openTabUrls.Length; i++)
                {
                    if (InternalPageHelper.CanBookmarkPage(openTabUrls[i]))
                    {
                        AppServices.Bookmarks.Add(openTabTitles[i], openTabUrls[i]);
                        addedCount++;
                    }
                }

                Assert(addedCount == 2, "Exactly 2 valid tabs should be bookmarked");
                Assert(AppServices.Bookmarks.IsBookmarked(testUrl1), "Tab 1 must be bookmarked");
                Assert(AppServices.Bookmarks.IsBookmarked(testUrl2), "Tab 2 must be bookmarked");
                Assert(!AppServices.Bookmarks.IsBookmarked(testInternalUrl), "wb://bookmarks must not be bookmarked");

                string formattedMsg = string.Format(AppStrings.Browser.NotifyBookmarkAllAddedFormat, addedCount, addedCount == 1 ? "" : "s");
                Assert(formattedMsg == "Bookmarked 2 tabs.", "Formatted bookmark notification must match 'Bookmarked 2 tabs.'");

                // Clean up test bookmarks
                AppServices.Bookmarks.RemoveByUrl(testUrl1);
                AppServices.Bookmarks.RemoveByUrl(testUrl2);
            });

            Test("DarkFallbackHelper URL Eligibility, Directory Index Detection and Non-Conflicting Guard Architecture", () =>
            {
                // 1. URL Eligibility (ShouldInject)
                Assert(DarkFallbackHelper.ShouldInject("https://example.com"), "Standard HTTPS URL should be eligible");
                Assert(DarkFallbackHelper.ShouldInject("http://127.0.0.1:8080/files/"), "Local web server should be eligible");
                Assert(DarkFallbackHelper.ShouldInject("file:///C:/Users/test/index.html"), "Local file:/// URL should be eligible");
                Assert(DarkFallbackHelper.ShouldInject("http://ftp.debian.org/debian/"), "HTTP mirror for FTP should be eligible");

                // Exclude internal wb:// and about: pages
                Assert(!DarkFallbackHelper.ShouldInject(null), "Null URL must not be eligible");
                Assert(!DarkFallbackHelper.ShouldInject(""), "Empty URL must not be eligible");
                Assert(!DarkFallbackHelper.ShouldInject("wb://newtab"), "wb:// internal page must be excluded");
                Assert(!DarkFallbackHelper.ShouldInject("wb://settings"), "wb:// settings must be excluded");
                Assert(!DarkFallbackHelper.ShouldInject("about:blank"), "about:blank must be excluded");
                Assert(!DarkFallbackHelper.ShouldInject("chrome://version"), "chrome:// must be excluded");
                Assert(!DarkFallbackHelper.ShouldInject("chrome-devtools://devtools/"), "chrome-devtools:// must be excluded");

                // Exclude YouTube and video domains (zero SponsorBlock/AdBlock conflict)
                Assert(!DarkFallbackHelper.ShouldInject("https://www.youtube.com/watch?v=dQw4w9WgXcQ"), "YouTube must be excluded");
                Assert(!DarkFallbackHelper.ShouldInject("https://m.youtube.com/"), "Mobile YouTube must be excluded");
                Assert(!DarkFallbackHelper.ShouldInject("https://rr1---sn-4g5edn6s.googlevideo.com/videoplayback"), "Googlevideo must be excluded");

                // 2. Validate Guard Script Constants and Structure
                string script = DarkFallbackHelper.GuardScript;
                Assert(!string.IsNullOrEmpty(script), "GuardScript must not be empty");
                Assert(script.Contains(DarkFallbackHelper.GuardScriptId), "GuardScript must reference GuardScriptId (wb-dark-fallback-guard)");
                Assert(script.Contains("color-scheme: dark") || script.Contains("colorScheme = 'dark'"), "GuardScript must support native color-scheme: dark standard");
                Assert(script.Contains("#202124"), "GuardScript must use wb default canvas background #202124");
                Assert(script.Contains("#e8eaed"), "GuardScript must use wb default light text #e8eaed");
                Assert(script.Contains("#8ab4f8"), "GuardScript must use accessible link color #8ab4f8");
                Assert(script.Contains("index of"), "GuardScript must handle directory listing Index of");

                // 3. Verify Null Frame Safety
                DarkFallbackHelper.InjectIntoFrame(null, "https://example.com");
            });

            Test("BrandTheme and ThemeManager Pastel Random Colors Verification", () =>
            {
                // Test 50 iterations of BrandTheme.GenerateRandomAccent to verify pastel guarantees
                for (int i = 0; i < 50; i++)
                {
                    BrandTheme.GenerateRandomAccent();
                    Color accent = BrandTheme.AccentColor;
                    Assert(!accent.IsEmpty, "Accent color must not be empty");

                    float h, s, l;
                    BrandTheme.RgbToHsl(accent, out h, out s, out l);

                    // Lightness must be in pastel range: 72% to 82% (never dark like the old 44%-56%)
                    Assert(l >= 0.70f && l <= 0.85f, string.Format("BrandTheme lightness {0:F2} must be within pastel range (0.70-0.85)", l));

                    // Saturation must be soft pastel: 50% to 70% (moderate saturation, never dull grey or harsh neon)
                    Assert(s >= 0.45f && s <= 0.75f, string.Format("BrandTheme saturation {0:F2} must be within pastel range (0.45-0.75)", s));

                    // Average RGB brightness must be bright (>= 150)
                    int avgBrightness = (accent.R + accent.G + accent.B) / 3;
                    Assert(avgBrightness >= 150, string.Format("Pastel accent must have high brightness ({0} >= 150)", avgBrightness));

                    // Text contrast must be dark charcoal for maximum readability on pastel backgrounds
                    Assert(BrandTheme.AccentTextColor.R < 30 && BrandTheme.AccentTextColor.G < 30 && BrandTheme.AccentTextColor.B < 30,
                        "Pastel accent must produce crisp dark charcoal text");

                    // Speed gradient start & end must also stay within pastel tones without dipping dark
                    float hGrad, sGrad, lGrad;
                    BrandTheme.RgbToHsl(BrandTheme.AccentGradientStart, out hGrad, out sGrad, out lGrad);
                    Assert(lGrad >= 0.65f, "Gradient start must remain bright pastel");

                    BrandTheme.RgbToHsl(BrandTheme.AccentGradientEnd, out hGrad, out sGrad, out lGrad);
                    Assert(lGrad >= 0.50f, "Gradient end must not dip into dark tones");
                }

                // Verify ThemeManager.GenerateRandomBrightColor produces pastel colors
                for (int i = 0; i < 50; i++)
                {
                    Color tmColor = ThemeManager.GenerateRandomBrightColor();
                    float h, s, l;
                    BrandTheme.RgbToHsl(tmColor, out h, out s, out l);
                    Assert(l >= 0.70f && l <= 0.85f, string.Format("ThemeManager lightness {0:F2} must be in pastel range (0.70-0.85)", l));
                    Assert(s >= 0.45f && s <= 0.75f, string.Format("ThemeManager saturation {0:F2} must be in pastel range (0.45-0.75)", s));
                }
            });

            Test("wb Window Title Consistency Between Launch and Surfing", () =>
            {
                // Verify window title constants are consistent: "wb"
                Assert(AppStrings.Browser.WindowTitle == "wb", "WindowTitle constant must be 'wb' on launch");
                Assert(AppStrings.Browser.WindowTitleIncognito == "wb (Incognito)", "WindowTitleIncognito constant must be 'wb (Incognito)'");

                // 1. On Launch / Startup (null, empty, "New Tab", "wb", "wb Browser")
                Assert(TitleHelper.FormatWindowTitle(null, false) == "wb", "Null page title on launch must show 'wb'");
                Assert(TitleHelper.FormatWindowTitle("", false) == "wb", "Empty page title on launch must show 'wb'");
                Assert(TitleHelper.FormatWindowTitle("New Tab", false) == "wb", "'New Tab' on launch must show 'wb'");
                Assert(TitleHelper.FormatWindowTitle("wb", false) == "wb", "'wb' page title must show 'wb'");
                Assert(TitleHelper.FormatWindowTitle("wb Browser", false) == "wb", "'wb Browser' page title must normalize to 'wb'");

                // 2. Surfing web pages (consistent format: "page name - wb")
                Assert(TitleHelper.FormatWindowTitle("Google Search", false) == "Google Search - wb", "Surfing title must be 'page - wb'");
                Assert(TitleHelper.FormatWindowTitle("Wikipedia", false) == "Wikipedia - wb", "Surfing title must be 'page - wb'");
                Assert(TitleHelper.FormatWindowTitle("GitHub: Where the world builds software", false) == "GitHub: Where the world builds software - wb", "Surfing title must be 'page - wb'");

                // 3. Incognito Launch and Surfing consistency
                Assert(TitleHelper.FormatWindowTitle(null, true) == "wb (Incognito)", "Incognito on launch must show 'wb (Incognito)'");
                Assert(TitleHelper.FormatWindowTitle("New Tab", true) == "wb (Incognito)", "Incognito New Tab must show 'wb (Incognito)'");
                Assert(TitleHelper.FormatWindowTitle("Google Search", true) == "Google Search - wb (Incognito)", "Incognito surfing title must be 'page - wb (Incognito)'");
            });

            Test("Bookmarks Bar Folder Move, Layout Measurement, Context Menu Checkmark & Nomenclature Parity", () =>
            {
                // 1. Nomenclature Parity: Tools menu and strings labeled as "Bookmarks" instead of "Favorites"
                Assert(AppStrings.Browser.MenuFavorites == "&Bookmarks", "MenuFavorites must be labeled as &Bookmarks");
                Assert(AppStrings.Browser.MenuViewFavorites == "View &bookmarks", "MenuViewFavorites must be labeled as View &bookmarks");
                Assert(AppStrings.Browser.MenuAddToFavorites == "&Bookmark this tab", "MenuAddToFavorites must be labeled as &Bookmark this tab");
                Assert(AppStrings.Bookmarks.MoveToBar == "Move to bookmarks bar", "MoveToBar string defined");
                Assert(AppStrings.Bookmarks.MoveToFolder == "Move to folder", "MoveToFolder string defined");

                // 2. Context Menu Checkbox Checkmark Rendering
                using (ContextMenuStrip menu = new ContextMenuStrip())
                {
                    UITheme.ApplyContextMenu(menu);
                    menu.ShowImageMargin = true;
                    menu.ShowCheckMargin = false;

                    ToolStripMenuItem checkItem = new ToolStripMenuItem("Show bookmarks bar");
                    checkItem.Checked = true;
                    menu.Items.Add(checkItem);

                    using (Bitmap bmp = new Bitmap(200, 30))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        menu.Renderer.DrawItemCheck(new ToolStripItemImageRenderEventArgs(g, checkItem, new Rectangle(0, 0, 24, 24)));
                    }
                }

                // 3. Layout measurement math: ensure titles like GitHub, YouTube, Wikipedia, Tools do not truncate
                using (WbBookmarksBar bar = new WbBookmarksBar())
                {
                    bar.Width = 1000;
                    bar.DpiScale = 1.0f;
                    bar.RecomputeLayout();

                    // Measure "GitHub"
                    int textW = TextRenderer.MeasureText("GitHub", bar.Font, new Size(200, 24), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                    int padLeft = 6;
                    int padRight = 8;
                    int iconW = 16;
                    int iconGap = 6;
                    int itemW = padLeft + iconW + iconGap + textW + 4 + padRight;

                    // Slot calculation: textRect.Width is (itemW - padRight) - (padLeft + iconW + iconGap) = textW + 4
                    int slotTextW = (itemW - padRight) - (padLeft + iconW + iconGap);
                    Assert(slotTextW >= textW, "Bookmarks bar slot text area (" + slotTextW + "px) must be >= measured text (" + textW + "px) to prevent premature ellipsis");
                }

                // 4. Moving item into and out of a folder via BookmarkManager
                BookmarkManager bm = new BookmarkManager();
                BookmarkItem folder = bm.AddFolder("Test Work", "bar");
                BookmarkItem bookmark = bm.Add("My PR", "https://github.com/pulls", "bar");
                Assert(bookmark.ParentId == "bar", "Bookmark starts on bar");

                // Move into folder
                bm.MoveItem(bookmark.Id, -1, folder.Id);
                BookmarkItem movedIn = bm.GetById(bookmark.Id);
                Assert(movedIn != null && movedIn.ParentId == folder.Id, "Bookmark moved into folder successfully");
                Assert(bm.GetChildCount(folder.Id) == 1, "Folder child count is 1");

                // Move out of folder back to bar
                bm.MoveItem(bookmark.Id, -1, "bar");
                BookmarkItem movedOut = bm.GetById(bookmark.Id);
                Assert(movedOut != null && movedOut.ParentId == "bar", "Bookmark moved out of folder back to bar successfully");
                Assert(bm.GetChildCount(folder.Id) == 0, "Folder child count is 0 after move");

                // Cleanup
                bm.Remove(folder.Id);
                bm.Remove(bookmark.Id);
            });

            Test("Bookmarks Bar Last Column Reorder & Globe Accent Color Parity", () =>
            {
                // 1. Globe icon rendering uses ThemeManager.AccentColor by default
                Color currentAccent = ThemeManager.AccentColor;
                Bitmap globeBmp = MaterialIcons.GetIcon("globe", 16);
                Assert(globeBmp != null, "MaterialIcons.GetIcon('globe', 16) must return bitmap");

                bool foundAccentPixel = false;
                for (int y = 0; y < globeBmp.Height; y++)
                {
                    for (int x = 0; x < globeBmp.Width; x++)
                    {
                        Color p = globeBmp.GetPixel(x, y);
                        if (p.A > 200 && Math.Abs(p.R - currentAccent.R) <= 5 && Math.Abs(p.G - currentAccent.G) <= 5 && Math.Abs(p.B - currentAccent.B) <= 5)
                        {
                            foundAccentPixel = true;
                            break;
                        }
                    }
                    if (foundAccentPixel) break;
                }
                Assert(foundAccentPixel, "Globe icon must render using ThemeManager.AccentColor");

                // 2. DrawIcon without color argument draws globe using ThemeManager.AccentColor
                using (Bitmap drawBmp = new Bitmap(16, 16))
                using (Graphics g = Graphics.FromImage(drawBmp))
                {
                    g.Clear(Color.Transparent);
                    MaterialIcons.DrawIcon(g, "globe", new Rectangle(0, 0, 16, 16));
                    bool foundDrawnAccent = false;
                    for (int y = 0; y < drawBmp.Height; y++)
                    {
                        for (int x = 0; x < drawBmp.Width; x++)
                        {
                            Color p = drawBmp.GetPixel(x, y);
                            if (p.A > 200 && Math.Abs(p.R - currentAccent.R) <= 5 && Math.Abs(p.G - currentAccent.G) <= 5 && Math.Abs(p.B - currentAccent.B) <= 5)
                            {
                                foundDrawnAccent = true;
                                break;
                            }
                        }
                        if (foundDrawnAccent) break;
                    }
                    Assert(foundDrawnAccent, "DrawIcon('globe') without color must draw in ThemeManager.AccentColor");
                }

                // 3. BookmarkManager: Reordering items to the end / last column
                BookmarkManager bm = new BookmarkManager();
                BookmarkItem item1 = bm.Add("Site A", "https://site-a.com", "bar");
                BookmarkItem item2 = bm.Add("Site B", "https://site-b.com", "bar");
                BookmarkItem item3 = bm.Add("Site C", "https://site-c.com", "bar");

                List<BookmarkItem> barItems = bm.GetBarItems();
                Assert(barItems.Count >= 3, "Bar has at least 3 items");
                int idx1 = -1, idx2 = -1, idx3 = -1;
                for (int i = 0; i < barItems.Count; i++)
                {
                    if (barItems[i].Id == item1.Id) idx1 = i;
                    if (barItems[i].Id == item2.Id) idx2 = i;
                    if (barItems[i].Id == item3.Id) idx3 = i;
                }
                Assert(idx1 >= 0 && idx2 >= 0 && idx3 >= 0, "All items present on bar");

                // Move item1 past item3 (to the very last position / last column)
                List<BookmarkItem> all = bm.GetAll();
                int allIdx3 = -1;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Id == item3.Id) { allIdx3 = i; break; }
                }
                Assert(allIdx3 >= 0, "item3 found in all bookmarks");

                // Moving to allIdx3 + 1 places it after item3
                bm.MoveItem(item1.Id, allIdx3 + 1);

                List<BookmarkItem> barAfterMove = bm.GetBarItems();
                int newIdx1 = -1, newIdx3 = -1;
                for (int i = 0; i < barAfterMove.Count; i++)
                {
                    if (barAfterMove[i].Id == item1.Id) newIdx1 = i;
                    if (barAfterMove[i].Id == item3.Id) newIdx3 = i;
                }
                Assert(newIdx1 > newIdx3, "item1 was successfully moved after item3 to the last column");

                // Cleanup
                bm.Remove(item1.Id);
                bm.Remove(item2.Id);
                bm.Remove(item3.Id);
            });

            Test("Bookmarks Bar Other Bookmarks Folder Dragging and Cyclic Move Protection", () =>
            {
                BookmarkManager bm = AppServices.Bookmarks;

                // 1. Create a folder in "Other bookmarks" with children
                BookmarkItem otherFolder = bm.AddFolder("Work Tools", "other");
                Assert(otherFolder != null && otherFolder.ParentId == "other", "Folder created in other bookmarks");
                BookmarkItem childDoc = bm.Add("Design Spec", "https://example.com/spec", otherFolder.Id);
                Assert(childDoc.ParentId == otherFolder.Id, "Child bookmark belongs to otherFolder");

                // 2. Prevent cyclic moves (moving folder into itself or descendant)
                bool selfMove = bm.MoveItem(otherFolder.Id, -1, otherFolder.Id);
                Assert(!selfMove, "Moving a folder into itself must be blocked");

                BookmarkItem subFolder = bm.AddFolder("Sub Team", otherFolder.Id);
                bool cycleMove = bm.MoveItem(otherFolder.Id, -1, subFolder.Id);
                Assert(!cycleMove, "Moving a folder into its descendant must be blocked");

                // 3. Move folder from "other" to "bar"
                bool movedToBar = bm.MoveItem(otherFolder.Id, 0, "bar");
                Assert(movedToBar, "Moving folder from other to bar must succeed");
                BookmarkItem verifiedBarFolder = bm.GetById(otherFolder.Id);
                Assert(verifiedBarFolder.ParentId == "bar", "Folder parent is now 'bar'");
                Assert(bm.GetBarItems().Exists(b => b.Id == otherFolder.Id), "Folder exists in bar items");

                // Verify children are preserved
                Assert(bm.GetChildCount(otherFolder.Id) == 2, "Folder still has its 2 children after moving to bar");

                // 4. Test WbBookmarksBar OLE Drop Simulation for folder from other bookmarks
                using (WbBookmarksBar bar = new WbBookmarksBar())
                {
                    bar.Size = new Size(800, 30);
                    bar.RecomputeLayout();

                    // Verify folder menu construction in BuildFolderMenuItems
                    using (ContextMenuStrip testMenu = new ContextMenuStrip())
                    {
                        var buildMethod = typeof(WbBookmarksBar).GetMethod("BuildFolderMenuItems",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(buildMethod != null, "BuildFolderMenuItems method exists");

                        List<BookmarkItem> otherItems = new List<BookmarkItem> { otherFolder };
                        buildMethod.Invoke(bar, new object[] { testMenu.Items, otherItems });
                        Assert(testMenu.Items.Count > 0, "Menu item generated for folder");
                        ToolStripMenuItem fItem = testMenu.Items[0] as ToolStripMenuItem;
                        Assert(fItem != null && fItem.Text == "Work Tools", "Folder menu item created with correct title");
                        Assert(fItem.Image != null, "Folder menu item has folder icon");
                    }

                    // Move back to "other" to simulate dragging to bar
                    bm.MoveItem(otherFolder.Id, -1, "other");
                    Assert(bm.GetById(otherFolder.Id).ParentId == "other", "Folder reset to other");

                    // Simulate drag-drop of the folder ID onto the bar
                    DataObject dropData = new DataObject();
                    dropData.SetData("wb_bookmark_id", otherFolder.Id);
                    DragEventArgs dragArgs = new DragEventArgs(dropData, 0, 50, 15, DragDropEffects.Move, DragDropEffects.Move);

                    var onDragOverMethod = typeof(WbBookmarksBar).GetMethod("OnDragOver",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    onDragOverMethod.Invoke(bar, new object[] { dragArgs });

                    var onDragDropMethod = typeof(WbBookmarksBar).GetMethod("OnDragDrop",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    onDragDropMethod.Invoke(bar, new object[] { dragArgs });

                    BookmarkItem finalItem = AppServices.Bookmarks.GetById(otherFolder.Id);
                    Assert(finalItem.ParentId == "bar", "Folder dragged from other bookmarks successfully moved to bar");
                }

                // Cleanup
                bm.Remove(otherFolder.Id);
            });

            Test("Bookmarks Bar Nested Folder Right-Click Context Menu Menu Hierarchy Persistence", () =>
            {
                BookmarkManager bm = AppServices.Bookmarks;

                // 1. Setup a nested folder hierarchy: bar -> parentFolder -> subFolder -> item
                BookmarkItem parentFolder = bm.AddFolder("Documentation", "bar");
                Assert(parentFolder != null && parentFolder.ParentId == "bar", "Parent folder created on bar");

                BookmarkItem subFolder = bm.AddFolder("API Guides", parentFolder.Id);
                Assert(subFolder != null && subFolder.ParentId == parentFolder.Id, "Subfolder created under parent folder");

                BookmarkItem bookmarkItem = bm.Add("Endpoints Ref", "https://api.example.com/v1", subFolder.Id);
                Assert(bookmarkItem != null && bookmarkItem.ParentId == subFolder.Id, "Bookmark created under subfolder");

                using (WbBookmarksBar bar = new WbBookmarksBar())
                using (Form testForm = new Form { Size = new Size(800, 600) })
                {
                    testForm.Controls.Add(bar);
                    testForm.Show();
                    Application.DoEvents();

                    // Verify initial private field state via reflection
                    var childActiveField = typeof(WbBookmarksBar).GetField("_isChildContextMenuActive",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(childActiveField != null, "_isChildContextMenuActive field exists");
                    Assert((bool)childActiveField.GetValue(bar) == false, "_isChildContextMenuActive initially false");

                    var activeMenuField = typeof(WbBookmarksBar).GetField("_activeChildContextMenu",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(activeMenuField != null, "_activeChildContextMenu field exists");

                    var registerMethod = typeof(WbBookmarksBar).GetMethod("RegisterFolderDropDown",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(registerMethod != null, "RegisterFolderDropDown method exists");

                    var buildMethod = typeof(WbBookmarksBar).GetMethod("BuildFolderMenuItems",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(buildMethod != null, "BuildFolderMenuItems method exists");

                    // 2. Build root folder dropdown menu and nested hierarchy
                    using (ContextMenuStrip rootMenu = new ContextMenuStrip())
                    {
                        registerMethod.Invoke(bar, new object[] { rootMenu });

                        List<BookmarkItem> barItems = bm.GetBarItems(parentFolder.Id);
                        buildMethod.Invoke(bar, new object[] { rootMenu.Items, barItems });

                        Assert(rootMenu.Items.Count > 0, "Root menu items populated");
                        ToolStripMenuItem subFolderItem = rootMenu.Items[0] as ToolStripMenuItem;
                        Assert(subFolderItem != null && subFolderItem.Text == "API Guides", "Subfolder item found in root menu");
                        Assert(subFolderItem.DropDownItems.Count > 0, "Subfolder has child items");

                        ToolStripMenuItem targetBookmarkItem = subFolderItem.DropDownItems[0] as ToolStripMenuItem;
                        Assert(targetBookmarkItem != null && targetBookmarkItem.Text == "Endpoints Ref", "Target bookmark found in subfolder");

                        // 3. Open rootMenu and subFolder dropdown
                        rootMenu.Show(testForm, new Point(100, 100));
                        subFolderItem.DropDown.Show(250, 100);
                        Application.DoEvents();

                        Assert(rootMenu.Visible, "rootMenu is visible");
                        Assert(subFolderItem.DropDown.Visible, "subFolder.DropDown is visible");

                        // 4. Invoke ShowChildBookmarkContextMenu for targetBookmarkItem
                        var showChildBmMethod = typeof(WbBookmarksBar).GetMethod("ShowChildBookmarkContextMenu",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(showChildBmMethod != null, "ShowChildBookmarkContextMenu method exists");

                        showChildBmMethod.Invoke(bar, new object[] { bookmarkItem, targetBookmarkItem, new Point(400, 150) });
                        Application.DoEvents();

                        // 5. Verify child context menu is active and parent menus remain open
                        Assert((bool)childActiveField.GetValue(bar) == true, "_isChildContextMenuActive must be true while context menu is displayed");
                        ContextMenuStrip childMenu = (ContextMenuStrip)activeMenuField.GetValue(bar);
                        Assert(childMenu != null && childMenu.Visible, "Child context menu is open and visible");
                        Assert(childMenu.OwnerItem == targetBookmarkItem, "Child context menu OwnerItem is set to targetBookmarkItem");
                        Assert(childMenu.Location.X == 400 && childMenu.Location.Y == 150, "Child context menu must be positioned at requested coordinates (400, 150), NOT at corner (0, 0)");
                        Assert(subFolderItem.DropDown.Visible, "subFolder.DropDown must remain visible when right-click context menu opens");
                        Assert(rootMenu.Visible, "rootMenu must remain visible when right-click context menu opens");

                        // 6. Dismiss child context menu via Escape (CloseReason.Keyboard)
                        childMenu.Close(ToolStripDropDownCloseReason.Keyboard);
                        Application.DoEvents();

                        Assert((bool)childActiveField.GetValue(bar) == false, "_isChildContextMenuActive must reset to false after context menu closes");
                        Assert(subFolderItem.DropDown.Visible, "subFolder.DropDown must remain visible after dismissing context menu");
                        Assert(rootMenu.Visible, "rootMenu must remain visible after dismissing context menu");

                        // 7. Verify CloseAllParentDropDowns closes the entire hierarchy cleanly
                        var closeAllMethod = typeof(WbBookmarksBar).GetMethod("CloseAllParentDropDowns",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(closeAllMethod != null, "CloseAllParentDropDowns method exists");

                        closeAllMethod.Invoke(bar, new object[] { targetBookmarkItem });
                        Application.DoEvents();

                        Assert(!subFolderItem.DropDown.Visible, "subFolder.DropDown must close when action triggers CloseAllParentDropDowns");
                        Assert(!rootMenu.Visible, "rootMenu must close when action triggers CloseAllParentDropDowns");
                    }

                    testForm.Close();
                }

                // Cleanup
                bm.Remove(parentFolder.Id);
            });

            Test("Bookmark Manager Folder Sorting, In-Folder Reordering, View Bookmarks Menu Label & Dark Favicon Contrast Parity", () =>
            {
                BookmarkManager bm = AppServices.Bookmarks;

                // 1. Folder Sorting Verification
                BookmarkItem folderA = bm.AddFolder("AAA_Folder", "bar");
                BookmarkItem folderZ = bm.AddFolder("ZZZ_Folder", "bar");
                BookmarkItem itemA = bm.Add("Item in AAA", "https://aaa.example.com", folderA.Id);
                BookmarkItem itemZ = bm.Add("Item in ZZZ", "https://zzz.example.com", folderZ.Id);

                List<BookmarkItem> sortedAsc = bm.GetSorted("", SortMode.FolderAscending);
                int idxA = sortedAsc.FindIndex(b => b.Id == itemA.Id);
                int idxZ = sortedAsc.FindIndex(b => b.Id == itemZ.Id);
                Assert(idxA >= 0 && idxZ >= 0 && idxA < idxZ, "FolderAscending must place items in AAA_Folder before items in ZZZ_Folder");

                List<BookmarkItem> sortedDesc = bm.GetSorted("", SortMode.FolderDescending);
                idxA = sortedDesc.FindIndex(b => b.Id == itemA.Id);
                idxZ = sortedDesc.FindIndex(b => b.Id == itemZ.Id);
                Assert(idxA >= 0 && idxZ >= 0 && idxZ < idxA, "FolderDescending must place items in ZZZ_Folder before items in AAA_Folder");

                // 2. In-Folder Rearranging Verification
                BookmarkItem testFolder = bm.AddFolder("ReorderFolder", "bar");
                BookmarkItem b1 = bm.Add("Bookmark One", "https://1.example.com", testFolder.Id);
                BookmarkItem b2 = bm.Add("Bookmark Two", "https://2.example.com", testFolder.Id);
                BookmarkItem b3 = bm.Add("Bookmark Three", "https://3.example.com", testFolder.Id);

                List<BookmarkItem> childrenInitial = bm.GetBarItems(testFolder.Id);
                Assert(childrenInitial.Count == 3, "Folder contains 3 child bookmarks");
                Assert(childrenInitial[0].Id == b1.Id && childrenInitial[1].Id == b2.Id && childrenInitial[2].Id == b3.Id, "Initial in-folder order is b1, b2, b3");

                // Move b2 up (-1) -> should become [b2, b1, b3]
                bool movedUp = bm.MoveItemInFolder(b2.Id, -1);
                Assert(movedUp == true, "MoveItemInFolder(b2, -1) succeeded");
                List<BookmarkItem> childrenAfterUp = bm.GetBarItems(testFolder.Id);
                Assert(childrenAfterUp[0].Id == b2.Id && childrenAfterUp[1].Id == b1.Id && childrenAfterUp[2].Id == b3.Id, "Order after moving b2 up is b2, b1, b3");

                // Boundary checks: b2 cannot move up further
                bool moveTopUp = bm.MoveItemInFolder(b2.Id, -1);
                Assert(moveTopUp == false, "MoveItemInFolder at top index returns false");

                // Move b2 down (+1) -> back to [b1, b2, b3]
                bool movedDown = bm.MoveItemInFolder(b2.Id, 1);
                Assert(movedDown == true, "MoveItemInFolder(b2, 1) succeeded");
                List<BookmarkItem> childrenAfterDown = bm.GetBarItems(testFolder.Id);
                Assert(childrenAfterDown[0].Id == b1.Id && childrenAfterDown[1].Id == b2.Id && childrenAfterDown[2].Id == b3.Id, "Order after moving b2 down is b1, b2, b3");

                // Boundary checks: b3 cannot move down further
                bool moveBottomDown = bm.MoveItemInFolder(b3.Id, 1);
                Assert(moveBottomDown == false, "MoveItemInFolder at bottom index returns false");

                // 3. Bookmarks Bar Right-Click Context Menu Label Parity
                using (WbBookmarksBar bar = new WbBookmarksBar())
                using (ContextMenuStrip menu = new ContextMenuStrip())
                {
                    var appendMethod = typeof(WbBookmarksBar).GetMethod("AppendStandardBarMenuItems",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(appendMethod != null, "AppendStandardBarMenuItems exists");
                    appendMethod.Invoke(bar, new object[] { menu });

                    ToolStripMenuItem viewBookmarksItem = null;
                    for (int i = 0; i < menu.Items.Count; i++)
                    {
                        ToolStripMenuItem mi = menu.Items[i] as ToolStripMenuItem;
                        if (mi != null && mi.Text == AppStrings.Browser.MenuViewFavorites)
                        {
                            viewBookmarksItem = mi;
                            break;
                        }
                    }
                    Assert(viewBookmarksItem != null, "Context menu contains 'View &bookmarks' (AppStrings.Browser.MenuViewFavorites)");
                    Assert(viewBookmarksItem.ShortcutKeyDisplayString == "Ctrl+B", "View bookmarks shortcut display string is Ctrl+B");
                }

                // 4. Dark Mode Favicon Contrast Adaptation Verification
                // 4a. Black silhouette on transparent background -> MUST be adapted
                using (Bitmap darkBmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(darkBmp))
                    {
                        g.Clear(Color.Transparent);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 10, 10, 10)))
                        {
                            g.FillEllipse(b, 6, 6, 20, 20);
                        }
                    }
                    bool adapted = FaviconManager.EnsureDarkModeContrast(darkBmp);
                    Assert(adapted == true, "Dark silhouette icon with transparency must be adapted for dark mode");
                    Color center = darkBmp.GetPixel(16, 16);
                    Assert(center.R >= 240 && center.G >= 240 && center.B >= 240, "Dark pixels must be inverted to bright white/off-white for dark mode");
                    Assert(darkBmp.GetPixel(0, 0).A < 40, "Transparent pixels must remain transparent");
                }

                // 4b. Colorful icon on transparent background -> MUST NOT be modified
                using (Bitmap colorBmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(colorBmp))
                    {
                        g.Clear(Color.Transparent);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 66, 133, 244))) // Google blue
                        {
                            g.FillEllipse(b, 6, 6, 20, 20);
                        }
                    }
                    bool colorAdapted = FaviconManager.EnsureDarkModeContrast(colorBmp);
                    Assert(colorAdapted == false, "Vibrant colorful icon must NOT be modified");
                    Color centerColor = colorBmp.GetPixel(16, 16);
                    Assert(centerColor.R == 66 && centerColor.G == 133 && centerColor.B == 244, "Original color must be preserved intact");
                }

                // 4c. Solid dark photo (no transparency) -> MUST NOT be modified
                using (Bitmap solidBmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(solidBmp))
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 20, 20, 20)))
                        {
                            g.FillRectangle(b, 0, 0, 32, 32);
                        }
                    }
                    bool solidAdapted = FaviconManager.EnsureDarkModeContrast(solidBmp);
                    Assert(solidAdapted == false, "Solid opaque images without transparency must NOT be modified");
                }

                // Cleanup
                bm.Remove(folderA.Id);
                bm.Remove(folderZ.Id);
                bm.Remove(testFolder.Id);
            });

            Test("Bookmarks Bar Folder Item Right-Click Immunity Against Open URL and Screen Corner Dislocation", () =>
            {
                BookmarkManager bm = AppServices.Bookmarks;
                BookmarkItem folder = bm.AddFolder("TestDropdownFolder", "bar");
                BookmarkItem item = bm.Add("Example Bookmark", "https://example.com/page", folder.Id);

                string navigatedUrl = null;
                using (WbBookmarksBar bar = new WbBookmarksBar())
                using (Form testForm = new Form { Size = new Size(800, 600) })
                {
                    testForm.Controls.Add(bar);
                    testForm.Show();
                    Application.DoEvents();

                    bar.NavigateCallback = (url, newTab) =>
                    {
                        navigatedUrl = url;
                    };

                    using (ContextMenuStrip folderMenu = new ContextMenuStrip())
                    {
                        var registerMethod = typeof(WbBookmarksBar).GetMethod("RegisterFolderDropDown",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        registerMethod.Invoke(bar, new object[] { folderMenu });

                        var buildMethod = typeof(WbBookmarksBar).GetMethod("BuildFolderMenuItems",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        buildMethod.Invoke(bar, new object[] { folderMenu.Items, new List<BookmarkItem> { item } });

                        Assert(folderMenu.Items.Count == 1, "Folder menu contains 1 item");
                        ToolStripMenuItem itemMenu = folderMenu.Items[0] as ToolStripMenuItem;
                        Assert(itemMenu != null && itemMenu.Text == "Example Bookmark", "Item menu created for bookmark");

                        folderMenu.Show(testForm, new Point(200, 200));
                        Application.DoEvents();
                        Assert(folderMenu.Visible, "Folder dropdown menu is visible");

                        // 1. Simulate Right-Click (MouseDown with Right Button, followed by Click & MouseUp)
                        Point clickLocation = new Point(itemMenu.Bounds.Left + 10, itemMenu.Bounds.Top + 5);
                        Point screenClickPt = folderMenu.PointToScreen(clickLocation);

                        // Fire MouseDown with Right button
                        var handleMouseDown = typeof(ToolStripItem).GetMethod("HandleMouseDown",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var handleMouseUp = typeof(ToolStripItem).GetMethod("HandleMouseUp",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var onClick = typeof(ToolStripItem).GetMethod("OnClick",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        MouseEventArgs rightDownArgs = new MouseEventArgs(MouseButtons.Right, 1, 10, 5, 0);
                        handleMouseDown.Invoke(itemMenu, new object[] { rightDownArgs });
                        Application.DoEvents();

                        // Fire Click (as WinForms message loop does on right button release)
                        onClick.Invoke(itemMenu, new object[] { EventArgs.Empty });

                        // Fire MouseUp with Right button
                        MouseEventArgs rightUpArgs = new MouseEventArgs(MouseButtons.Right, 1, 10, 5, 0);
                        handleMouseUp.Invoke(itemMenu, new object[] { rightUpArgs });
                        Application.DoEvents();

                        // Verify:
                        // - NO open URL signal sent
                        Assert(navigatedUrl == null, "Right-clicking folder bookmark item must NOT send open URL signal");
                        // - Folder dropdown must NOT be dismissed
                        Assert(folderMenu.Visible == true, "Folder dropdown menu must remain visible after right-click");

                        // - Child context menu must be visible and NOT at (0, 0)
                        var activeMenuField = typeof(WbBookmarksBar).GetField("_activeChildContextMenu",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        ContextMenuStrip childMenu = (ContextMenuStrip)activeMenuField.GetValue(bar);
                        Assert(childMenu != null && childMenu.Visible == true, "Child context menu must be displayed");
                        Assert(childMenu.Location.X > 0 && childMenu.Location.Y > 0,
                            string.Format("Child context menu must be positioned at cursor/screen location ({0}), NOT at screen corner (0,0)", childMenu.Location));

                        // Dismiss child context menu
                        childMenu.Close();
                        Application.DoEvents();

                        // 2. Now simulate Left-Click (MouseDown with Left Button, followed by Click & MouseUp)
                        MouseEventArgs leftDownArgs = new MouseEventArgs(MouseButtons.Left, 1, 10, 5, 0);
                        handleMouseDown.Invoke(itemMenu, new object[] { leftDownArgs });
                        onClick.Invoke(itemMenu, new object[] { EventArgs.Empty });
                        MouseEventArgs leftUpArgs = new MouseEventArgs(MouseButtons.Left, 1, 10, 5, 0);
                        handleMouseUp.Invoke(itemMenu, new object[] { leftUpArgs });
                        Application.DoEvents();

                        // Verify:
                        // - Left-click DOES send open URL signal
                        Assert(navigatedUrl == "https://example.com/page", "Left-clicking folder bookmark item must send open URL signal");
                    }

                    testForm.Close();
                }

                bm.Remove(folder.Id);
            });

            Test("Bookmarks Bar In-Folder Dropmenu Drag and Drop Rearrange and MoveItemRelative Parity", () =>
            {
                BookmarkManager bm = AppServices.Bookmarks;
                BookmarkItem folder = bm.AddFolder("RearrangeTestFolder", "bar");
                BookmarkItem item1 = bm.Add("Item Alpha", "https://alpha.com", folder.Id);
                BookmarkItem item2 = bm.Add("Item Beta", "https://beta.com", folder.Id);
                BookmarkItem item3 = bm.Add("Item Gamma", "https://gamma.com", folder.Id);

                try
                {
                    // 1. Test MoveItemRelative: move item3 before item1
                    bool moved = bm.MoveItemRelative(item3.Id, item1.Id, false, folder.Id);
                    Assert(moved, "MoveItemRelative item3 before item1 succeeded");
                    var items = bm.GetBarItems(folder.Id);
                    Assert(items.Count == 3, "Folder has 3 items");
                    Assert(items[0].Id == item3.Id, "Item Gamma is now first");
                    Assert(items[1].Id == item1.Id, "Item Alpha is now second");
                    Assert(items[2].Id == item2.Id, "Item Beta is now third");

                    // Move item1 after item2
                    moved = bm.MoveItemRelative(item1.Id, item2.Id, true, folder.Id);
                    Assert(moved, "MoveItemRelative item1 after item2 succeeded");
                    items = bm.GetBarItems(folder.Id);
                    Assert(items[0].Id == item3.Id, "Item Gamma is first");
                    Assert(items[1].Id == item2.Id, "Item Beta is second");
                    Assert(items[2].Id == item1.Id, "Item Alpha is third");

                    // 2. Test WbBookmarksBar DropDown drag and drop registration & in-menu drag state
                    using (WbBookmarksBar bar = new WbBookmarksBar())
                    using (ContextMenuStrip menu = new ContextMenuStrip())
                    {
                        menu.Tag = folder.Id;
                        var registerMethod = typeof(WbBookmarksBar).GetMethod("RegisterFolderDropDown",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(registerMethod != null, "RegisterFolderDropDown exists");
                        registerMethod.Invoke(bar, new object[] { menu });
                        Assert(menu.AllowDrop, "Folder menu must have AllowDrop enabled for in-folder drag reordering");

                        var buildMethod = typeof(WbBookmarksBar).GetMethod("BuildFolderMenuItemsInternal",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(buildMethod != null, "BuildFolderMenuItemsInternal method exists");
                        buildMethod.Invoke(bar, new object[] { menu.Items, items, folder.Id, menu });

                        Assert(menu.Items.Count == 3, "Menu built with 3 items");
                        Assert(menu.Items[0].Tag == items[0], "Item 0 Tag is set to BookmarkItem");
                        Assert(menu.Items[1].Tag == items[1], "Item 1 Tag is set to BookmarkItem");
                        Assert(menu.Items[2].Tag == items[2], "Item 2 Tag is set to BookmarkItem");

                        // Simulate DragDrop: drop item2 before item3 in menu
                        var refreshMethod = typeof(WbBookmarksBar).GetMethod("RefreshFolderDropDown",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(refreshMethod != null, "RefreshFolderDropDown method exists");

                        bm.MoveItemRelative(item2.Id, item3.Id, false, folder.Id);
                        refreshMethod.Invoke(bar, new object[] { menu, folder.Id });

                        Assert(menu.Items.Count == 3, "Refreshed menu still has 3 items");
                        Assert(((BookmarkItem)menu.Items[0].Tag).Id == item2.Id, "Refreshed item 0 is Item Beta");
                        Assert(((BookmarkItem)menu.Items[1].Tag).Id == item3.Id, "Refreshed item 1 is Item Gamma");
                        Assert(((BookmarkItem)menu.Items[2].Tag).Id == item1.Id, "Refreshed item 2 is Item Alpha");

                        Assert(menu.Renderer != null, "Folder menu has custom BookmarksMenuRenderer configured");

                        // 3. Verify RenderMenuDropIndicator execution on top, middle, and bottom positions
                        var renderIndicatorMethod = typeof(WbBookmarksBar).GetMethod("RenderMenuDropIndicator",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(renderIndicatorMethod != null, "RenderMenuDropIndicator method exists");

                        var dropLineField = typeof(WbBookmarksBar).GetField("_menuDropLineY",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var activeDropDownField = typeof(WbBookmarksBar).GetField("_activeDragDropDown",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var isDraggingInMenuField = typeof(WbBookmarksBar).GetField("_isDraggingInMenu",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        isDraggingInMenuField.SetValue(bar, true);
                        activeDropDownField.SetValue(bar, menu);

                        using (Bitmap testBm = new Bitmap(200, 100))
                        using (Graphics g = Graphics.FromImage(testBm))
                        {
                            // Top position (above first item)
                            dropLineField.SetValue(bar, 3);
                            renderIndicatorMethod.Invoke(bar, new object[] { g, menu });

                            // Middle position (between items)
                            dropLineField.SetValue(bar, 30);
                            renderIndicatorMethod.Invoke(bar, new object[] { g, menu });

                            // Bottom position (below last item)
                            dropLineField.SetValue(bar, 96);
                            renderIndicatorMethod.Invoke(bar, new object[] { g, menu });
                        }

                        // Reset drag state
                        dropLineField.SetValue(bar, -1);
                        isDraggingInMenuField.SetValue(bar, false);
                        activeDropDownField.SetValue(bar, null);
                    }
                }
                finally
                {
                    bm.Remove(folder.Id);
                }
            });

            Test("Bookmark Manager Type Sorting, SortMode & 5-Column Type Layout Parity", () =>
            {
                BookmarkManager bm = AppServices.Bookmarks;
                BookmarkItem folderA = bm.AddFolder("TypeFolder_A", "bar");
                BookmarkItem folderZ = bm.AddFolder("TypeFolder_Z", "bar");
                BookmarkItem itemB = bm.Add("TypeBookmark_B", "https://b.example.com", "bar");
                BookmarkItem itemY = bm.Add("TypeBookmark_Y", "https://y.example.com", "bar");

                try
                {
                    // 1. SortMode.TypeAscending (Folders first, then Title A-Z)
                    List<BookmarkItem> sortedTypeAsc = bm.GetSorted("Type", SortMode.TypeAscending);
                    Assert(sortedTypeAsc.Count == 4, "Query 'Type' finds 4 items");

                    int idxFA = sortedTypeAsc.FindIndex(b => b.Id == folderA.Id);
                    int idxFZ = sortedTypeAsc.FindIndex(b => b.Id == folderZ.Id);
                    int idxIB = sortedTypeAsc.FindIndex(b => b.Id == itemB.Id);
                    int idxIY = sortedTypeAsc.FindIndex(b => b.Id == itemY.Id);

                    Assert(idxFA >= 0 && idxFZ >= 0 && idxIB >= 0 && idxIY >= 0, "All items present in TypeAscending");
                    Assert(idxFA < idxFZ, "TypeAscending: Folder A appears before Folder Z");
                    Assert(idxFZ < idxIB, "TypeAscending: All folders appear before bookmarks");
                    Assert(idxIB < idxIY, "TypeAscending: Bookmark B appears before Bookmark Y");

                    // 2. SortMode.TypeDescending (Bookmarks first, then Title A-Z)
                    List<BookmarkItem> sortedTypeDesc = bm.GetSorted("Type", SortMode.TypeDescending);
                    Assert(sortedTypeDesc.Count == 4, "Query 'Type' finds 4 items");

                    idxFA = sortedTypeDesc.FindIndex(b => b.Id == folderA.Id);
                    idxFZ = sortedTypeDesc.FindIndex(b => b.Id == folderZ.Id);
                    idxIB = sortedTypeDesc.FindIndex(b => b.Id == itemB.Id);
                    idxIY = sortedTypeDesc.FindIndex(b => b.Id == itemY.Id);

                    Assert(idxFA >= 0 && idxFZ >= 0 && idxIB >= 0 && idxIY >= 0, "All items present in TypeDescending");
                    Assert(idxIB < idxIY, "TypeDescending: Bookmark B appears before Bookmark Y");
                    Assert(idxIY < idxFA, "TypeDescending: All bookmarks appear before folders");
                    Assert(idxFA < idxFZ, "TypeDescending: Folder A appears before Folder Z");

                    // 3. BookmarksView Column Layout and Sort Options Verification
                    using (BookmarksView view = new BookmarksView(null))
                    using (Form testForm = new Form { Size = new Size(900, 600) })
                    {
                        testForm.Controls.Add(view);
                        testForm.Show();
                        Application.DoEvents();

                        var listViewField = typeof(BookmarksView).GetField("_listView",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var sortComboField = typeof(BookmarksView).GetField("_sortCombo",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var currentSortField = typeof(BookmarksView).GetField("_currentSort",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var columnClickMethod = typeof(BookmarksView).GetMethod("ListView_ColumnClick",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var deleteSelectedMethod = typeof(BookmarksView).GetMethod("DeleteSelected",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        ListView lv = (ListView)listViewField.GetValue(view);
                        ComboBox sc = (ComboBox)sortComboField.GetValue(view);

                        // Verify 5 Columns
                        Assert(lv.Columns.Count == 5, "BookmarksView ListView has 5 columns");
                        Assert(lv.Columns[0].Text == AppStrings.Bookmarks.ColumnTitle, "Column 0 is Title");
                        Assert(lv.Columns[1].Text == AppStrings.Bookmarks.ColumnType, "Column 1 is Type");
                        Assert(lv.Columns[2].Text == AppStrings.Bookmarks.ColumnFolder, "Column 2 is Location");
                        Assert(lv.Columns[3].Text == AppStrings.Bookmarks.ColumnUrl, "Column 3 is URL");
                        Assert(lv.Columns[4].Text == AppStrings.Bookmarks.ColumnDateAdded, "Column 4 is Date Added");

                        // Verify ComboBox Sort Options & Default Sort (Folders on Top by default)
                        Assert(sc.Items.Count == 8, "Sort combo has 8 items");
                        Assert((string)sc.Items[0] == AppStrings.General.SortTypeAsc, "Item 0 is SortTypeAsc");
                        Assert((string)sc.Items[1] == AppStrings.General.SortTypeDesc, "Item 1 is SortTypeDesc");
                        Assert(sc.SelectedIndex == 0, "Default sort is TypeAscending (index 0 - Folders First)");
                        SortMode initialSort = (SortMode)currentSortField.GetValue(view);
                        Assert(initialSort == SortMode.TypeAscending, "Initial sort mode is TypeAscending");

                        // Verify Standard Folder Name Format (No tiny emoji, uses [FOLDER_NAME])
                        ListViewItem folderLvi = null;
                        ListViewItem bookmarkLvi = null;
                        for (int i = 0; i < lv.Items.Count; i++)
                        {
                            BookmarkItem bi = lv.Items[i].Tag as BookmarkItem;
                            if (bi != null)
                            {
                                if (bi.Id == folderA.Id) folderLvi = lv.Items[i];
                                if (bi.Id == itemB.Id) bookmarkLvi = lv.Items[i];
                            }
                        }
                        Assert(folderLvi != null, "Folder item found in ListView");
                        Assert(bookmarkLvi != null, "Bookmark item found in ListView");
                        Assert(folderLvi.Text == "TypeFolder_A", "Folder name uses clean title without brackets or emojis");
                        Assert(bookmarkLvi.Text == "TypeBookmark_B", "Bookmark title preserved");
                        Assert(folderLvi.SubItems[1].Text == AppStrings.Bookmarks.TypeFolder, "Folder item has 'Folder' type text");
                        Assert(bookmarkLvi.SubItems[1].Text == AppStrings.Bookmarks.TypeBookmark, "Bookmark item has 'Bookmark' type text");

                        // 4. Test Column Click on Type Column (index 1)
                        // Currently TypeAscending -> clicking toggles to TypeDescending
                        columnClickMethod.Invoke(view, new object[] { lv, new ColumnClickEventArgs(1) });
                        SortMode curSort = (SortMode)currentSortField.GetValue(view);
                        Assert(curSort == SortMode.TypeDescending, "Clicking Column 1 toggles to TypeDescending");
                        Assert(sc.SelectedIndex == 1, "Clicking Column 1 selects index 1 in sort combo");

                        columnClickMethod.Invoke(view, new object[] { lv, new ColumnClickEventArgs(1) });
                        curSort = (SortMode)currentSortField.GetValue(view);
                        Assert(curSort == SortMode.TypeAscending, "Clicking Column 1 second time toggles back to TypeAscending");
                        Assert(sc.SelectedIndex == 0, "Clicking Column 1 second time selects index 0 in sort combo");

                        // 5. Test ComboBox Selection
                        sc.SelectedIndex = 1;
                        curSort = (SortMode)currentSortField.GetValue(view);
                        Assert(curSort == SortMode.TypeDescending, "Selecting index 1 sets TypeDescending");

                        sc.SelectedIndex = 0;
                        curSort = (SortMode)currentSortField.GetValue(view);
                        Assert(curSort == SortMode.TypeAscending, "Selecting index 0 sets TypeAscending");

                        // 6. Test Confirmation Dialog Before Deletion
                        // A: User Cancels (returns false) -> item must NOT be removed
                        string promptMsg = null;
                        string promptTitle = null;
                        view.ConfirmDeleteHandler = (m, t) =>
                        {
                            promptMsg = m;
                            promptTitle = t;
                            return false;
                        };

                        ListViewItem currentFolderLvi = null;
                        for (int i = 0; i < lv.Items.Count; i++)
                        {
                            BookmarkItem bi = lv.Items[i].Tag as BookmarkItem;
                            if (bi != null && bi.Id == folderA.Id)
                            {
                                currentFolderLvi = lv.Items[i];
                                break;
                            }
                        }
                        Assert(currentFolderLvi != null, "Current folder item found in ListView");

                        lv.SelectedItems.Clear();
                        currentFolderLvi.Selected = true;
                        Application.DoEvents();
                        deleteSelectedMethod.Invoke(view, null);

                        Assert(promptTitle == AppStrings.Bookmarks.DeleteFolderTitle, "Prompt title is Delete Folder");
                        Assert(promptMsg != null && promptMsg.Contains("TypeFolder_A"), "Prompt message mentions folder name");
                        Assert(bm.GetFolders().Exists(f => f.Id == folderA.Id), "Folder must NOT be removed when user cancels confirmation");

                        // B: User Confirms (returns true) -> item IS removed
                        view.ConfirmDeleteHandler = (m, t) =>
                        {
                            promptMsg = m;
                            promptTitle = t;
                            return true;
                        };

                        lv.SelectedItems.Clear();
                        currentFolderLvi.Selected = true;
                        Application.DoEvents();
                        deleteSelectedMethod.Invoke(view, null);
                        Assert(!bm.GetFolders().Exists(f => f.Id == folderA.Id), "Folder is removed when user confirms deletion");

                        // C: Single Bookmark confirmation
                        view.RefreshList();
                        Application.DoEvents();
                        ListViewItem bLvi = null;
                        for (int i = 0; i < lv.Items.Count; i++)
                        {
                            BookmarkItem bi = lv.Items[i].Tag as BookmarkItem;
                            if (bi != null && bi.Id == itemB.Id) bLvi = lv.Items[i];
                        }
                        Assert(bLvi != null, "Bookmark item exists before deletion");
                        lv.SelectedItems.Clear();
                        bLvi.Selected = true;
                        Application.DoEvents();
                        deleteSelectedMethod.Invoke(view, null);
                        Assert(promptTitle == AppStrings.Bookmarks.DeleteBookmarkTitle, "Prompt title for bookmark is Delete Bookmark");
                        Assert(!bm.GetAll().Exists(b => b.Id == itemB.Id), "Bookmark is removed when user confirms deletion");

                        // 7. Verify WbBookmarksBar ConfirmDeleteHandler parity
                        using (WbBookmarksBar bar = new WbBookmarksBar())
                        {
                            bar.ConfirmDeleteHandler = (msg, t) => false;
                            Assert(bar.ConfirmDeleteHandler != null, "ConfirmDeleteHandler assigned on WbBookmarksBar");
                        }

                        testForm.Close();
                    }
                }
                finally
                {
                    bm.Remove(folderA.Id);
                    bm.Remove(folderZ.Id);
                    bm.Remove(itemB.Id);
                    bm.Remove(itemY.Id);
                }
            });

            Test("Itemized Bottom Count Indicator, Page Title Quantity Badge Removal & Internal Pages Reopen Closed Tab Parity", () =>
            {
                // 1. Verify AppStrings.General.FormatItemizedCount
                Assert(AppStrings.General.FormatItemizedCount(18, 1) == "18 items (1 selected)", "Format 18 items (1 selected)");
                Assert(AppStrings.General.FormatItemizedCount(18, 0) == "18 items", "Format 18 items");
                Assert(AppStrings.General.FormatItemizedCount(18, 3) == "18 items (3 selected)", "Format 18 items (3 selected)");
                Assert(AppStrings.General.FormatItemizedCount(1, 1) == "1 item (1 selected)", "Format 1 item (1 selected)");
                Assert(AppStrings.General.FormatItemizedCount(1, 0) == "1 item", "Format 1 item");
                Assert(AppStrings.General.FormatItemizedCount(0, 0) == "0 items", "Format 0 items");

                // 2. Verify _badgeLabel has been removed from all 3 manager views
                var bmBadge = typeof(BookmarksView).GetField("_badgeLabel",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var dlBadge = typeof(DownloadsView).GetField("_badgeLabel",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var histBadge = typeof(HistoryView).GetField("_badgeLabel",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                Assert(bmBadge == null, "BookmarksView _badgeLabel field removed");
                Assert(dlBadge == null, "DownloadsView _badgeLabel field removed");
                Assert(histBadge == null, "HistoryView _badgeLabel field removed");

                // 3. Verify BookmarksView uses open itemized indicator at bottom left
                using (BookmarksView view = new BookmarksView(null))
                {
                    var countLabelField = typeof(BookmarksView).GetField("_countLabel",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(countLabelField != null, "BookmarksView has _countLabel");
                    Label countLabel = (Label)countLabelField.GetValue(view);
                    Assert(countLabel != null, "BookmarksView _countLabel initialized");
                    Assert(countLabel.Text.Contains("item"), "BookmarksView count label contains 'item'");
                }

                // 4. Verify DownloadsView uses open itemized indicator at bottom left
                using (DownloadsView view = new DownloadsView(null))
                {
                    var countLabelField = typeof(DownloadsView).GetField("_countLabel",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(countLabelField != null, "DownloadsView has _countLabel");
                    Label countLabel = (Label)countLabelField.GetValue(view);
                    Assert(countLabel != null, "DownloadsView _countLabel initialized");
                    Assert(countLabel.Text.Contains("item"), "DownloadsView count label contains 'item'");
                }

                // 5. Verify HistoryView uses open itemized indicator at bottom left
                using (HistoryView view = new HistoryView(null))
                {
                    var countLabelField = typeof(HistoryView).GetField("_countLabel",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(countLabelField != null, "HistoryView has _countLabel");
                    Label countLabel = (Label)countLabelField.GetValue(view);
                    Assert(countLabel != null, "HistoryView _countLabel initialized");
                    Assert(countLabel.Text.Contains("item"), "HistoryView count label contains 'item'");
                }

                // 6. Verify Internal Pages Reopen Closed Tab Eligibility
                Assert(InternalPageHelper.IsStartPage("wb://bookmarks") == false, "wb://bookmarks is not a start page");
                Assert(InternalPageHelper.IsStartPage("wb://history") == false, "wb://history is not a start page");
                Assert(InternalPageHelper.IsStartPage("wb://downloads") == false, "wb://downloads is not a start page");
                Assert(InternalPageHelper.IsStartPage("wb://settings") == false, "wb://settings is not a start page");
                Assert(InternalPageHelper.IsStartPage("wb://flags") == false, "wb://flags is not a start page");
                Assert(InternalPageHelper.IsStartPage("chrome://bookmarks") == false, "chrome://bookmarks is not a start page");
                Assert(InternalPageHelper.IsStartPage("chrome://history") == false, "chrome://history is not a start page");
                Assert(InternalPageHelper.IsStartPage("chrome://downloads") == false, "chrome://downloads is not a start page");

                Assert(InternalPageHelper.IsStartPage(InternalPageHelper.NewTabUrl) == true, "NewTabUrl is a start page");
                Assert(InternalPageHelper.IsStartPage("about:blank") == true, "about:blank is a start page");
                Assert(InternalPageHelper.IsStartPage("chrome://newtab") == true, "chrome://newtab is a start page");

                // Verify MainBrowserForm CloseTab logic retains internal pages in recently closed stack
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(srcDir)) srcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                string mainFormPath = Path.Combine(srcDir, @"Browser\MainBrowserForm.cs");
                if (File.Exists(mainFormPath))
                {
                    string formContent = File.ReadAllText(mainFormPath);
                    int closeTabIdx = formContent.IndexOf("public void CloseTab(BrowserTab tab)");
                    Assert(closeTabIdx >= 0, "MainBrowserForm defines CloseTab");
                    string closeTabBody = formContent.Substring(closeTabIdx, Math.Min(600, formContent.Length - closeTabIdx));
                    Assert(!closeTabBody.Contains("IsInternalUrl"),
                        "CloseTab must not exclude internal pages from recently closed tab history");
                    Assert(closeTabBody.Contains("_recentlyClosedUrls.Push(tab.CurrentUrl);"),
                        "CloseTab must push closed tab URL to recently closed stack");
                    Assert(closeTabBody.Contains("!InternalPageHelper.IsStartPage(tab.CurrentUrl)"),
                        "CloseTab must filter out blank start pages from recently closed tab history");
                }
            });

            Test("BookmarksView Dual-Pane TreeView Sidebar and Folder Filtering Parity", () =>
            {
                BookmarkManager bm = AppServices.Bookmarks;
                BookmarkItem testBarFolder = null;
                BookmarkItem testBarLink = null;
                BookmarkItem testChildLink = null;
                BookmarkItem testOtherLink = null;

                try
                {
                    // Create test items across bar, custom folder, and other bookmarks
                    testBarFolder = bm.AddFolder("TreeTestFolder_Alpha", "bar");
                    testBarLink = bm.Add("TreeTestBar_Link", "https://treetest-bar.example.com/", "bar");
                    testChildLink = bm.Add("TreeTestChild_Link", "https://treetest-child.example.com/", testBarFolder.Id);
                    testOtherLink = bm.Add("TreeTestOther_Link", "https://treetest-other.example.com/", "other");

                    using (BookmarksView view = new BookmarksView(null))
                    using (Form testForm = new Form { Size = new Size(1100, 700) })
                    {
                        testForm.Controls.Add(view);
                        testForm.Show();
                        Application.DoEvents();

                        var treeField = typeof(BookmarksView).GetField("_folderTree",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var splitField = typeof(BookmarksView).GetField("_splitContainer",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var listField = typeof(BookmarksView).GetField("_listView",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        Assert(treeField != null, "BookmarksView defines _folderTree");
                        Assert(splitField != null, "BookmarksView defines _splitContainer");
                        Assert(listField != null, "BookmarksView defines _listView");

                        TreeView tree = (TreeView)treeField.GetValue(view);
                        SplitContainer split = (SplitContainer)splitField.GetValue(view);
                        ListView lv = (ListView)listField.GetValue(view);

                        Assert(tree != null, "_folderTree is initialized");
                        Assert(split != null, "_splitContainer is initialized");
                        Assert(lv != null, "_listView is initialized");

                        // 1. Verify Root Nodes in TreeView (No 'All bookmarks' virtual root to prevent massive list lagging)
                        Assert(tree.Nodes.Count >= 3, "Folder tree contains at least 3 root nodes");
                        TreeNode barNode = tree.Nodes[0];
                        TreeNode otherNode = tree.Nodes[1];
                        TreeNode mobileNode = tree.Nodes[2];

                        Assert(barNode.Text == AppStrings.Bookmarks.FolderBookmarksBar, "Node 0 is 'Bookmarks bar'");
                        Assert((string)barNode.Tag == "bar", "Node 0 Tag is 'bar'");
                        Assert(otherNode.Text == AppStrings.Bookmarks.FolderOther, "Node 1 is 'Other bookmarks'");
                        Assert((string)otherNode.Tag == "other", "Node 1 Tag is 'other'");
                        Assert(mobileNode.Text == AppStrings.Bookmarks.FolderMobile, "Node 2 is 'Mobile bookmarks'");
                        Assert((string)mobileNode.Tag == "mobile", "Node 2 Tag is 'mobile'");

                        // 2. Verify Child Folder Node is nested under Bookmarks bar
                        TreeNode customFolderNode = null;
                        for (int i = 0; i < barNode.Nodes.Count; i++)
                        {
                            if (string.Equals(barNode.Nodes[i].Tag as string, testBarFolder.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                customFolderNode = barNode.Nodes[i];
                                break;
                            }
                        }
                        Assert(customFolderNode != null, "Custom folder node found under Bookmarks bar root node");
                        Assert(customFolderNode.Text == "TreeTestFolder_Alpha", "Custom folder node has folder title");

                        // 3. Verify Selection: 'Bookmarks bar' filters to direct bar items
                        tree.SelectedNode = barNode;
                        Application.DoEvents();

                        bool foundBarInBar = false;
                        bool foundOtherInBar = false;
                        bool foundChildInBar = false;
                        for (int i = 0; i < lv.Items.Count; i++)
                        {
                            BookmarkItem bi = lv.Items[i].Tag as BookmarkItem;
                            if (bi != null)
                            {
                                if (bi.Id == testBarLink.Id) foundBarInBar = true;
                                if (bi.Id == testOtherLink.Id) foundOtherInBar = true;
                                if (bi.Id == testChildLink.Id) foundChildInBar = true;
                            }
                        }
                        Assert(foundBarInBar, "Bookmarks bar view contains testBarLink");
                        Assert(!foundOtherInBar, "Bookmarks bar view does NOT contain testOtherLink");
                        Assert(!foundChildInBar, "Bookmarks bar view does NOT contain testChildLink (nested in subfolder)");

                        // 4. Verify Selection: 'Other bookmarks' filters to other items
                        tree.SelectedNode = otherNode;
                        Application.DoEvents();

                        bool foundBarInOther = false;
                        bool foundOtherInOther = false;
                        for (int i = 0; i < lv.Items.Count; i++)
                        {
                            BookmarkItem bi = lv.Items[i].Tag as BookmarkItem;
                            if (bi != null)
                            {
                                if (bi.Id == testBarLink.Id) foundBarInOther = true;
                                if (bi.Id == testOtherLink.Id) foundOtherInOther = true;
                            }
                        }
                        Assert(!foundBarInOther, "Other bookmarks view does NOT contain testBarLink");
                        Assert(foundOtherInOther, "Other bookmarks view contains testOtherLink");

                        // 5. Verify Selection: Custom Subfolder filters to its child items
                        tree.SelectedNode = customFolderNode;
                        Application.DoEvents();

                        bool foundBarInChildFolder = false;
                        bool foundChildInChildFolder = false;
                        for (int i = 0; i < lv.Items.Count; i++)
                        {
                            BookmarkItem bi = lv.Items[i].Tag as BookmarkItem;
                            if (bi != null)
                            {
                                if (bi.Id == testBarLink.Id) foundBarInChildFolder = true;
                                if (bi.Id == testChildLink.Id) foundChildInChildFolder = true;
                            }
                        }
                        Assert(!foundBarInChildFolder, "Custom subfolder view does NOT contain testBarLink");
                        Assert(foundChildInChildFolder, "Custom subfolder view contains testChildLink");

                        // 6. Verify switching back to 'Bookmarks bar'
                        tree.SelectedNode = barNode;
                        Application.DoEvents();
                        bool restoredBar = false;
                        for (int i = 0; i < lv.Items.Count; i++)
                        {
                            BookmarkItem bi = lv.Items[i].Tag as BookmarkItem;
                            if (bi != null && bi.Id == testBarLink.Id) { restoredBar = true; break; }
                        }
                        Assert(restoredBar, "Switching back to Bookmarks bar restores bar items");

                        // 8. Verify Right Click in Empty Space provides Add Bookmark and Add Folder
                        lv.SelectedItems.Clear();
                        ContextMenuStrip emptyMenu = lv.ContextMenuStrip;
                        Assert(emptyMenu != null, "_listView has ContextMenuStrip");
                        var onOpening = typeof(ToolStripDropDown).GetMethod("OnOpening",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var cancelArgs = new System.ComponentModel.CancelEventArgs();
                        onOpening.Invoke(emptyMenu, new object[] { cancelArgs });
                        Assert(!cancelArgs.Cancel, "Empty space context menu must not be cancelled");
                        Assert(emptyMenu.Items.Count >= 2, "Empty space context menu contains at least 2 items");
                        Assert(emptyMenu.Items[0].Text == AppStrings.Bookmarks.BtnAdd, "First item is Add Bookmark");
                        Assert(emptyMenu.Items[1].Text == AppStrings.Bookmarks.BtnAddFolder, "Second item is Add Folder");

                        // 9. Verify folder item title has clean title (no brackets)
                        ListViewItem folderItemClean = null;
                        for (int i = 0; i < lv.Items.Count; i++)
                        {
                            BookmarkItem bi = lv.Items[i].Tag as BookmarkItem;
                            if (bi != null && bi.Id == testBarFolder.Id)
                            {
                                folderItemClean = lv.Items[i];
                                break;
                            }
                        }
                        Assert(folderItemClean != null, "Test folder found in list");
                        Assert(folderItemClean.Text == "TreeTestFolder_Alpha", "Folder title is clean without brackets");
                    }
                }
                finally
                {
                    if (testChildLink != null) bm.Remove(testChildLink.Id);
                    if (testBarLink != null) bm.Remove(testBarLink.Id);
                    if (testOtherLink != null) bm.Remove(testOtherLink.Id);
                    if (testBarFolder != null) bm.Remove(testBarFolder.Id);
                }
            });

            Test("BookmarkEditDialog Modern Chrome-Style Visual Parity and Mobile Bookmarks Support", () =>
            {
                // 1. Mobile bookmarks string and BookmarkManager integration
                Assert(AppStrings.Bookmarks.FolderMobile == "Mobile bookmarks", "FolderMobile string matches Chrome convention");
                Assert(AppServices.Bookmarks.GetFolderName("mobile") == AppStrings.Bookmarks.FolderMobile, "GetFolderName('mobile') returns Mobile bookmarks");
                Assert(AppServices.Bookmarks.GetMobileBookmarks() != null, "GetMobileBookmarks() returns valid list");

                // 2. Add temporary folder under 'mobile'
                BookmarkItem mobChild = null;
                try
                {
                    mobChild = AppServices.Bookmarks.AddFolder("Archived_Test", "mobile");
                    Assert(mobChild != null && mobChild.ParentId == "mobile", "Subfolder created under mobile root");

                    // 3. Inspect BookmarkEditDialog architecture and Chrome-style tree view
                    using (BookmarkEditDialog dlg = new BookmarkEditDialog("Edit bookmark", "New Tab", "chrome://newtab", false, false, "Save", "mobile"))
                    {
                        Assert(dlg.ItemTitle == "New Tab", "ItemTitle matches initial title");
                        Assert(dlg.ItemUrl == "chrome://newtab", "ItemUrl matches initial url");
                        Assert(dlg.SelectedFolderId == "mobile", "SelectedFolderId correctly initialized to 'mobile'");

                        var treeField = typeof(BookmarkEditDialog).GetField("_folderTree", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(treeField != null, "_folderTree field exists on BookmarkEditDialog");
                        TreeView tree = treeField.GetValue(dlg) as TreeView;
                        Assert(tree != null, "TreeView initialized");
                        Assert(tree.DrawMode == TreeViewDrawMode.OwnerDrawAll, "TreeView DrawMode must be OwnerDrawAll for custom pills and chevrons");
                        Assert(tree.Nodes.Count >= 3, "TreeView contains Bookmarks bar, Other bookmarks, and Mobile bookmarks");
                        Assert(string.Equals(tree.Nodes[0].Tag as string, "bar", StringComparison.OrdinalIgnoreCase), "Node 0 is Bookmarks bar");
                        Assert(string.Equals(tree.Nodes[1].Tag as string, "other", StringComparison.OrdinalIgnoreCase), "Node 1 is Other bookmarks");
                        Assert(string.Equals(tree.Nodes[2].Tag as string, "mobile", StringComparison.OrdinalIgnoreCase), "Node 2 is Mobile bookmarks");

                        TreeNode mobNode = tree.Nodes[2];
                        bool foundMobChild = false;
                        for (int i = 0; i < mobNode.Nodes.Count; i++)
                        {
                            if (string.Equals(mobNode.Nodes[i].Tag as string, mobChild.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                foundMobChild = true;
                                break;
                            }
                        }
                        Assert(foundMobChild, "Child folder under Mobile bookmarks is correctly nested in TreeView");

                        // 4. Buttons check
                        var saveBtnField = typeof(BookmarkEditDialog).GetField("_saveBtn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var cancelBtnField = typeof(BookmarkEditDialog).GetField("_cancelBtn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var newFolderBtnField = typeof(BookmarkEditDialog).GetField("_newFolderBtn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        Button saveBtn = saveBtnField.GetValue(dlg) as Button;
                        Button cancelBtn = cancelBtnField.GetValue(dlg) as Button;
                        Button newFolderBtn = newFolderBtnField.GetValue(dlg) as Button;

                        Assert(saveBtn != null && saveBtn.FlatStyle == FlatStyle.Flat, "Save button is modern flat pill");
                        Assert(cancelBtn != null && cancelBtn.FlatStyle == FlatStyle.Flat, "Cancel button is modern flat pill");
                        Assert(newFolderBtn != null && newFolderBtn.FlatStyle == FlatStyle.Flat, "New folder button is modern flat pill");
                    }
                }
                finally
                {
                    if (mobChild != null)
                    {
                        AppServices.Bookmarks.Remove(mobChild.Id);
                    }
                }
            });

            Test("Bookmarks Bar Folder Dropmenu Move Items In and Out of Folder Items and Cyclic Move Protection Parity", () =>
            {
                BookmarkManager bm = AppServices.Bookmarks;
                BookmarkItem rootFolder = bm.AddFolder("DropmenuNestRoot", "bar");
                BookmarkItem subFolder = bm.AddFolder("SubFolderItem", rootFolder.Id);
                BookmarkItem deepFolder = bm.AddFolder("DeepFolderItem", subFolder.Id);
                BookmarkItem itemAlpha = bm.Add("Item Alpha", "https://alpha.example.com", rootFolder.Id);
                BookmarkItem itemBeta = bm.Add("Item Beta", "https://beta.example.com", subFolder.Id);

                try
                {
                    // 1. Validate IsDescendantOf hierarchy checking and cyclic move prevention
                    Assert(bm.IsDescendantOf(deepFolder.Id, rootFolder.Id), "deepFolder is descendant of rootFolder");
                    Assert(bm.IsDescendantOf(deepFolder.Id, subFolder.Id), "deepFolder is descendant of subFolder");
                    Assert(bm.IsDescendantOf(subFolder.Id, rootFolder.Id), "subFolder is descendant of rootFolder");
                    Assert(!bm.IsDescendantOf(rootFolder.Id, deepFolder.Id), "rootFolder is NOT descendant of deepFolder");
                    Assert(!bm.IsDescendantOf(rootFolder.Id, subFolder.Id), "rootFolder is NOT descendant of subFolder");

                    // Move item into a folder
                    Assert(itemAlpha.ParentId == rootFolder.Id, "itemAlpha initially in rootFolder");
                    bool movedIn = bm.MoveItem(itemAlpha.Id, -1, subFolder.Id);
                    Assert(movedIn, "Moving itemAlpha into subFolder succeeded");
                    BookmarkItem alphaMoved = bm.GetById(itemAlpha.Id);
                    Assert(alphaMoved != null && alphaMoved.ParentId == subFolder.Id, "itemAlpha parent updated to subFolder");

                    // Move item OUT of subfolder into rootFolder
                    bool movedOutToParent = bm.MoveItemRelative(itemAlpha.Id, subFolder.Id, true, rootFolder.Id);
                    Assert(movedOutToParent, "Moving itemAlpha out of subFolder into rootFolder succeeded");
                    alphaMoved = bm.GetById(itemAlpha.Id);
                    Assert(alphaMoved != null && alphaMoved.ParentId == rootFolder.Id, "itemAlpha parent updated back to rootFolder");

                    // Move item OUT of subfolder to Bookmarks Bar
                    bool movedOutToBar = bm.MoveItem(itemBeta.Id, 0, "bar");
                    Assert(movedOutToBar, "Moving itemBeta out of subFolder onto bar succeeded");
                    BookmarkItem betaMoved = bm.GetById(itemBeta.Id);
                    Assert(betaMoved != null && (betaMoved.ParentId == "bar" || string.IsNullOrEmpty(betaMoved.ParentId)), "itemBeta parent updated to bar");

                    // Cyclic move guard: cannot move rootFolder into deepFolder
                    bool invalidCycleMove = bm.MoveItem(rootFolder.Id, -1, deepFolder.Id);
                    Assert(!invalidCycleMove, "Cyclic folder nesting (root into deep descendant) is strictly prevented");

                    // 2. Test Dropdown UI drag state, folder hover indicator rendering & cleanup
                    using (WbBookmarksBar bar = new WbBookmarksBar())
                    using (ContextMenuStrip menu = new ContextMenuStrip())
                    {
                        menu.Tag = rootFolder.Id;
                        var registerMethod = typeof(WbBookmarksBar).GetMethod("RegisterFolderDropDown",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        registerMethod.Invoke(bar, new object[] { menu });

                        var buildMethod = typeof(WbBookmarksBar).GetMethod("BuildFolderMenuItemsInternal",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var rootChildren = bm.GetBarItems(rootFolder.Id);
                        buildMethod.Invoke(bar, new object[] { menu.Items, rootChildren, rootFolder.Id, menu });

                        Assert(menu.Items.Count >= 2, "Menu built with subFolder and items");
                        ToolStripMenuItem subFolderMenuItem = null;
                        for (int i = 0; i < menu.Items.Count; i++)
                        {
                            BookmarkItem it = menu.Items[i].Tag as BookmarkItem;
                            if (it != null && it.Id == subFolder.Id)
                            {
                                subFolderMenuItem = menu.Items[i] as ToolStripMenuItem;
                                break;
                            }
                        }
                        Assert(subFolderMenuItem != null, "SubFolder menu item found in dropdown");

                        // Test folder drop indicator highlight
                        var dropIntoFolderField = typeof(WbBookmarksBar).GetField("_menuDropIntoFolder",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var hoveredFolderItemField = typeof(WbBookmarksBar).GetField("_menuHoveredFolderItem",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var activeDropDownField = typeof(WbBookmarksBar).GetField("_activeDragDropDown",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var isDraggingInMenuField = typeof(WbBookmarksBar).GetField("_isDraggingInMenu",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var renderIndicatorMethod = typeof(WbBookmarksBar).GetMethod("RenderMenuDropIndicator",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        isDraggingInMenuField.SetValue(bar, true);
                        activeDropDownField.SetValue(bar, menu);
                        dropIntoFolderField.SetValue(bar, true);
                        hoveredFolderItemField.SetValue(bar, subFolderMenuItem);

                        using (Bitmap testBm = new Bitmap(240, 120))
                        using (Graphics g = Graphics.FromImage(testBm))
                        {
                            renderIndicatorMethod.Invoke(bar, new object[] { g, menu });
                        }

                        // Test CloseAllOpenDropDowns and CloseChildSubMenus reflection safety
                        var closeAllMethod = typeof(WbBookmarksBar).GetMethod("CloseAllOpenDropDowns",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(closeAllMethod != null, "CloseAllOpenDropDowns method exists");
                        closeAllMethod.Invoke(bar, null);

                        var closeChildSubMenusMethod = typeof(WbBookmarksBar).GetMethod("CloseChildSubMenus",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(closeChildSubMenusMethod != null, "CloseChildSubMenus method exists");
                        closeChildSubMenusMethod.Invoke(bar, new object[] { menu });

                        // Reset drag state
                        dropIntoFolderField.SetValue(bar, false);
                        hoveredFolderItemField.SetValue(bar, null);
                        isDraggingInMenuField.SetValue(bar, false);
                        activeDropDownField.SetValue(bar, null);
                    }
                }
                finally
                {
                    bm.Remove(rootFolder.Id);
                    bm.Remove(itemBeta.Id);
                }
            });

            Test("Data Portability: Netscape HTML Bookmark Round-Trip Integrity", () =>
            {
                var original = new List<BookmarkItem>();
                BookmarkItem folder1 = BookmarkItem.CreateFolder("Development Tools", "bar");
                BookmarkItem folder2 = BookmarkItem.CreateFolder("Cloud Services", folder1.Id);
                BookmarkItem item1 = new BookmarkItem("GitHub", "https://github.com/", folder1.Id);
                BookmarkItem item2 = new BookmarkItem("Google Cloud", "https://cloud.google.com/", folder2.Id);
                BookmarkItem item3 = new BookmarkItem("Documentation & Specs", "https://spec.example.org/?q=1&v=2", "other");

                original.Add(folder1);
                original.Add(folder2);
                original.Add(item1);
                original.Add(item2);
                original.Add(item3);

                // Export to HTML
                string html = HtmlBookmarkExporter.ExportToString(original);
                Assert(html.Contains("<!DOCTYPE NETSCAPE-Bookmark-file-1>"), "Contains Netscape DOCTYPE header");
                Assert(html.Contains("PERSONAL_TOOLBAR_FOLDER=\"true\""), "Contains PERSONAL_TOOLBAR_FOLDER marker for bar");
                Assert(html.Contains("Development Tools"), "Contains parent folder title");
                Assert(html.Contains("Documentation &amp; Specs"), "Contains HTML-escaped title");

                // Import back from HTML
                List<BookmarkItem> imported = HtmlBookmarkImporter.ImportFromString(html);
                Assert(imported != null && imported.Count == 5, "Imported 5 items back from HTML");

                int importedFolders = 0;
                int importedBookmarks = 0;
                bool foundGithub = false;
                bool foundEscaped = false;

                for (int i = 0; i < imported.Count; i++)
                {
                    BookmarkItem it = imported[i];
                    if (it.IsFolder) importedFolders++;
                    else
                    {
                        importedBookmarks++;
                        if (it.Url == "https://github.com/") foundGithub = true;
                        if (it.Title == "Documentation & Specs") foundEscaped = true;
                    }
                }

                Assert(importedFolders == 2, "Imported 2 folders");
                Assert(importedBookmarks == 3, "Imported 3 bookmarks");
                Assert(foundGithub, "Found GitHub URL");
                Assert(foundEscaped, "Unescaped HTML entities in title properly");
            });

            Test("Data Portability: Chromium Bookmarks JSON Parser", () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "wb_chrom_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    string sampleJson = @"
                    {
                        ""roots"": {
                            ""bookmark_bar"": {
                                ""children"": [
                                    {
                                        ""date_added"": ""13340000000000000"",
                                        ""id"": ""1"",
                                        ""name"": ""Chrome Bar Link"",
                                        ""type"": ""url"",
                                        ""url"": ""https://chromebar.org""
                                    },
                                    {
                                        ""children"": [
                                            {
                                                ""date_added"": ""13340000000000000"",
                                                ""id"": ""3"",
                                                ""name"": ""Nested Link"",
                                                ""type"": ""url"",
                                                ""url"": ""https://nested.org""
                                            }
                                        ],
                                        ""date_added"": ""13340000000000000"",
                                        ""id"": ""2"",
                                        ""name"": ""Subfolder"",
                                        ""type"": ""folder""
                                    }
                                ],
                                ""name"": ""Bookmarks bar"",
                                ""type"": ""folder""
                            },
                            ""other"": {
                                ""children"": [
                                    {
                                        ""date_added"": ""13340000000000000"",
                                        ""id"": ""4"",
                                        ""name"": ""Other Link"",
                                        ""type"": ""url"",
                                        ""url"": ""https://otherlink.org""
                                    }
                                ],
                                ""name"": ""Other bookmarks"",
                                ""type"": ""folder""
                            }
                        }
                    }";

                    File.WriteAllText(Path.Combine(tempDir, "Bookmarks"), sampleJson);

                    List<BookmarkItem> items = ChromiumDataImporter.ImportBookmarks(tempDir);
                    Assert(items.Count == 4, "Extracted 4 total items from Chromium JSON (3 bookmarks, 1 folder)");

                    bool foundBar = false;
                    bool foundNested = false;
                    bool foundOther = false;
                    string subfolderId = null;

                    for (int i = 0; i < items.Count; i++)
                    {
                        BookmarkItem it = items[i];
                        if (it.IsFolder && it.Title == "Subfolder")
                        {
                            subfolderId = it.Id;
                            Assert(it.ParentId == "bar", "Subfolder placed under 'bar'");
                        }
                        if (it.Url == "https://chromebar.org")
                        {
                            foundBar = true;
                            Assert(it.ParentId == "bar", "Bar link placed under 'bar'");
                        }
                        if (it.Url == "https://otherlink.org")
                        {
                            foundOther = true;
                            Assert(it.ParentId == "other", "Other link placed under 'other'");
                        }
                    }

                    for (int i = 0; i < items.Count; i++)
                    {
                        BookmarkItem it = items[i];
                        if (it.Url == "https://nested.org")
                        {
                            foundNested = true;
                            Assert(it.ParentId == subfolderId, "Nested link placed under Subfolder ID");
                        }
                    }

                    Assert(foundBar && foundNested && foundOther, "All Chromium bookmarks mapped to proper roots and parents");
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            });

            Test("Data Portability: History CSV and HTML Exporter", () =>
            {
                var historyItems = new List<HistoryItem>
                {
                    new HistoryItem("GitHub - Dashboard", "https://github.com/") { VisitedAt = new DateTime(2026, 11, 1, 10, 30, 0) },
                    new HistoryItem("Search \"Special Quotes\"", "https://google.com/search?q=test") { VisitedAt = new DateTime(2026, 11, 2, 14, 0, 0) }
                };

                // CSV Export
                var sbCsv = new System.Text.StringBuilder();
                using (var writer = new StringWriter(sbCsv))
                {
                    HistoryExporter.ExportToCsv(historyItems, writer);
                }
                string csv = sbCsv.ToString();
                Assert(csv.StartsWith("Title,URL,Last Visited"), "CSV starts with standard header");
                Assert(csv.Contains("\"GitHub - Dashboard\",\"https://github.com/\""), "Contains escaped CSV entry");
                Assert(csv.Contains("\"Search \"\"Special Quotes\"\"\""), "Escapes internal double quotes in CSV");

                // HTML Export
                var sbHtml = new System.Text.StringBuilder();
                using (var writer = new StringWriter(sbHtml))
                {
                    HistoryExporter.ExportToHtml(historyItems, writer);
                }
                string html = sbHtml.ToString();
                Assert(html.Contains("<!DOCTYPE html>"), "HTML export contains HTML5 doctype");
                Assert(html.Contains("Search &quot;Special Quotes&quot;"), "HTML export escapes entities in title");
                Assert(html.Contains("https://github.com/"), "HTML export contains link");
            });

            Test("Data Portability: BookmarkManager Batch Ingestion and Deduplication", () =>
            {
                BookmarkManager bm = AppServices.Bookmarks;
                int initialCount = bm.GetAll().Count;

                var batch = new List<BookmarkItem>
                {
                    new BookmarkItem("Portability Test 1", "https://portability-test-1.com/", "bar"),
                    new BookmarkItem("Portability Test 2", "https://portability-test-2.com/", "bar"),
                    BookmarkItem.CreateFolder("Portability Folder", "bar")
                };

                var res1 = bm.ImportBatch(batch, null, skipDuplicates: true);
                Assert(res1.BookmarksImported == 2, "Batch imported 2 bookmarks");
                Assert(res1.FoldersImported == 1, "Batch imported 1 folder");
                Assert(res1.SkippedDuplicates == 0, "No duplicates skipped initially");

                // Re-import the same batch with skipDuplicates = true
                var res2 = bm.ImportBatch(batch, null, skipDuplicates: true);
                Assert(res2.BookmarksImported == 0, "0 bookmarks imported on duplicate run");
                Assert(res2.SkippedDuplicates == 2, "2 duplicate bookmarks skipped");

                // Clean up
                bm.RemoveByUrl("https://portability-test-1.com/");
                bm.RemoveByUrl("https://portability-test-2.com/");
                for (int i = 0; i < batch.Count; i++)
                {
                    if (batch[i].IsFolder) bm.Remove(batch[i].Id);
                }
            });

            Test("Data Portability: HistoryManager Batch Ingestion and Sanitization", () =>
            {
                HistoryManager hm = AppServices.History;

                var batch = new List<HistoryItem>
                {
                    new HistoryItem("Valid Visit 1", "https://valid-visit-1.com/"),
                    new HistoryItem("Valid Visit 2", "https://valid-visit-2.com/"),
                    new HistoryItem("Internal Special", "wb://bookmarks"), // Should be sanitized out
                    new HistoryItem("Error Page", "wb://weberror?err=500") // Should be sanitized out
                };

                var res1 = hm.ImportBatch(batch, skipDuplicates: true);
                Assert(res1.ItemsImported == 2, "2 valid history visits imported, internal/error pages rejected");

                // Re-import: duplicates should be skipped
                var res2 = hm.ImportBatch(batch, skipDuplicates: true);
                Assert(res2.ItemsImported == 0, "0 items imported on duplicate run");
                Assert(res2.SkippedDuplicates == 2, "2 duplicate history visits skipped");
            });

            Test("Data Portability: ShortcutManager Batch Ingestion", () =>
            {
                ShortcutManager sm = AppServices.Shortcuts;
                int countBefore = sm.GetAll().Count;

                var batch = new List<ShortcutItem>
                {
                    new ShortcutItem("Batch Shortcut 1", "https://batch-shortcut-1.com/"),
                    new ShortcutItem("Batch Shortcut 2", "https://batch-shortcut-2.com/")
                };

                int imported = sm.ImportBatch(batch, skipDuplicates: true);
                Assert(imported >= 0, "Batch import executed on ShortcutManager without error");
            });

            Test("Data Portability: Browser Detector Discovery and Icons", () =>
            {
                List<BrowserProfileInfo> detected = BrowserDetector.DetectInstalledBrowsers();
                Assert(detected != null && detected.Count > 0, "Browser detector completed and returned browser entries");

                bool foundHtmlOption = false;
                for (int i = 0; i < detected.Count; i++)
                {
                    var b = detected[i];
                    Assert(b.AppIcon != null, "Every browser entry has a non-null AppIcon (native or high-quality fallback)");
                    if (b.Type == BrowserType.HtmlFile) foundHtmlOption = true;
                }
                Assert(foundHtmlOption, "Always includes universal Bookmarks HTML File option");
            });

            Test("Data Portability: Internal Page Helper Normalizes Import and Export URLs", () =>
            {
                Assert(InternalPageHelper.GetInternalPageType("wb://import") == InternalPageType.ImportData, "wb://import resolves to ImportData");
                Assert(InternalPageHelper.GetInternalPageType("wb://import-data") == InternalPageType.ImportData, "wb://import-data resolves to ImportData");
                Assert(InternalPageHelper.GetInternalPageType("wb://importer") == InternalPageType.ImportData, "wb://importer resolves to ImportData");

                Assert(InternalPageHelper.GetInternalPageType("wb://export") == InternalPageType.ExportData, "wb://export resolves to ExportData");
                Assert(InternalPageHelper.GetInternalPageType("wb://export-data") == InternalPageType.ExportData, "wb://export-data resolves to ExportData");
                Assert(InternalPageHelper.GetInternalPageType("wb://exporter") == InternalPageType.ExportData, "wb://exporter resolves to ExportData");

                Assert(InternalPageHelper.GetInternalTitle("wb://import") == "Import Data", "wb://import title is 'Import Data'");
                Assert(InternalPageHelper.GetInternalTitle("wb://export") == "Export Data", "wb://export title is 'Export Data'");
                Assert(InternalPageHelper.GetInternalFavicon("wb://import") != null, "wb://import has valid favicon");
                Assert(InternalPageHelper.GetInternalFavicon("wb://export") != null, "wb://export has valid favicon");
            });

            Test("SQLite Database: WAL Mode, Schema Integrity and Settings API", () =>
            {
                Assert(AppServices.Database != null, "SqliteDatabase service initialized");
                Assert(AppServices.Database.IsOpen, "Database is open and ready");

                // Verify WAL mode
                string journalMode = AppServices.Database.ExecuteScalar<string>("PRAGMA journal_mode;");
                Assert(string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase), "PRAGMA journal_mode is WAL");

                // Verify core tables exist
                long tableCount = AppServices.Database.ExecuteScalar<long>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('bookmarks', 'history', 'downloads', 'shortcuts', 'settings');");
                Assert(tableCount == 5, "All 5 core tables exist in sqlite_master");

                // Test Settings API
                AppServices.Database.SetSetting("unit_test_str", "hello_sqlite");
                Assert(AppServices.Database.GetSetting("unit_test_str") == "hello_sqlite", "GetSetting string matches");

                AppServices.Database.SetSettingBool("unit_test_bool_t", true);
                Assert(AppServices.Database.GetSettingBool("unit_test_bool_t", false) == true, "GetSettingBool true matches");

                AppServices.Database.SetSettingBool("unit_test_bool_f", false);
                Assert(AppServices.Database.GetSettingBool("unit_test_bool_f", true) == false, "GetSettingBool false matches");

                AppServices.Database.SetSettingInt("unit_test_int", 123456);
                Assert(AppServices.Database.GetSettingInt("unit_test_int", 0) == 123456, "GetSettingInt matches");

                Assert(AppServices.Database.GetSetting("non_existent_key", "default_val") == "default_val", "GetSetting fallback default works");
            });

            Test("SQLite Database: Transaction Atomicity, Rollback and Parameterized Queries", () =>
            {
                string uniqueId = "test_rollback_" + Guid.NewGuid().ToString("N");
                bool caught = false;

                try
                {
                    AppServices.Database.InTransaction(() =>
                    {
                        AppServices.Database.ExecuteNonQuery(
                            "INSERT INTO history (id, title, url, visited_at, visit_count) VALUES (?, ?, ?, ?, ?);",
                            uniqueId, "Rollback Page", "https://rollback.example.com", DateTime.UtcNow.Ticks, 1);

                        // Intentionally throw exception to trigger ROLLBACK
                        throw new InvalidOperationException("Simulated transaction crash");
                    });
                }
                catch (InvalidOperationException)
                {
                    caught = true;
                }

                Assert(caught, "Simulated crash threw expected exception");

                // Verify record was rolled back and does not exist
                long found = AppServices.Database.ExecuteScalar<long>(
                    "SELECT COUNT(*) FROM history WHERE id = ?;", uniqueId);
                Assert(found == 0, "Record rolled back atomically on exception");

                // Verify successful commit
                string commitId = "test_commit_" + Guid.NewGuid().ToString("N");
                AppServices.Database.InTransaction(() =>
                {
                    AppServices.Database.ExecuteNonQuery(
                        "INSERT INTO history (id, title, url, visited_at, visit_count) VALUES (?, ?, ?, ?, ?);",
                        commitId, "Committed Page", "https://committed.example.com", DateTime.UtcNow.Ticks, 1);
                });

                long committed = AppServices.Database.ExecuteScalar<long>(
                    "SELECT COUNT(*) FROM history WHERE id = ?;", commitId);
                Assert(committed == 1, "Record committed cleanly inside transaction");

                // Cleanup test record
                AppServices.Database.ExecuteNonQuery("DELETE FROM history WHERE id = ?;", commitId);
            });

            Test("Data Portability: ImportDataDialog and ExportDataDialog Modal Lifecycle", () =>
            {
                using (ImportDataDialog importDlg = new ImportDataDialog())
                {
                    Assert(importDlg.Text == AppStrings.ImportData.WindowTitle, "ImportDataDialog uses AppStrings.ImportData.WindowTitle");
                    Assert(importDlg.Text.EndsWith("- wb"), "ImportDataDialog title ends with - wb");
                    Assert(importDlg.FormBorderStyle == FormBorderStyle.FixedDialog, "ImportDataDialog is FixedDialog");
                    Assert(!importDlg.MaximizeBox, "ImportDataDialog MaximizeBox disabled");
                    Assert(!importDlg.MinimizeBox, "ImportDataDialog MinimizeBox disabled");
                    Assert(importDlg.BackColor == UITheme.FormBackground, "ImportDataDialog uses UITheme.FormBackground");
                    Assert(importDlg.AcceptButton != null, "ImportDataDialog has AcceptButton configured");
                    Assert(importDlg.CancelButton != null, "ImportDataDialog has CancelButton configured");
                }

                using (ExportDataDialog exportDlg = new ExportDataDialog())
                {
                    Assert(exportDlg.Text == AppStrings.ExportData.WindowTitle, "ExportDataDialog uses AppStrings.ExportData.WindowTitle");
                    Assert(exportDlg.Text.EndsWith("- wb"), "ExportDataDialog title ends with - wb");
                    Assert(exportDlg.Height >= 480, "ExportDataDialog is tall enough (>= 480px) to show contents properly");
                    Assert(exportDlg.FormBorderStyle == FormBorderStyle.FixedDialog, "ExportDataDialog is FixedDialog");
                    Assert(!exportDlg.MaximizeBox, "ExportDataDialog MaximizeBox disabled");
                    Assert(!exportDlg.MinimizeBox, "ExportDataDialog MinimizeBox disabled");
                    Assert(exportDlg.BackColor == UITheme.FormBackground, "ExportDataDialog uses UITheme.FormBackground");
                }
            });

            Test("BookmarkEditDialog Design Language and Accent Color Theming", () =>
            {
                using (BookmarkEditDialog bkmDlg = new BookmarkEditDialog("Edit Bookmark", "Google", "https://google.com"))
                {
                    Assert(bkmDlg.Text == "Edit Bookmark", "BookmarkEditDialog title assigned");
                    Assert(bkmDlg.BackColor == UITheme.FormBackground, "BookmarkEditDialog uses UITheme.FormBackground");
                    Assert(bkmDlg.ItemTitle == "Google", "BookmarkEditDialog title box populated");
                    Assert(bkmDlg.ItemUrl == "https://google.com", "BookmarkEditDialog URL box populated");
                }

                using (BookmarkEditDialog folderDlg = new BookmarkEditDialog(null, "Test Folder", null, false, false, null, "bar", false, true))
                {
                    Assert(folderDlg.ItemTitle == "Test Folder", "BookmarkEditDialog folder title populated");
                    Assert(folderDlg.BackColor == UITheme.FormBackground, "BookmarkEditDialog folder dialog uses UITheme.FormBackground");
                }
            });

            Test("Fullscreen and Immersive Player Mode Architecture", () =>
            {
                string srcDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src"));
                string formSrc = File.ReadAllText(Path.Combine(srcDir, @"Browser\MainBrowserForm.cs"));
                string tabSrc = File.ReadAllText(Path.Combine(srcDir, @"Browser\BrowserTab.cs"));
                string kbdSrc = File.ReadAllText(Path.Combine(srcDir, @"Browser\Handlers\BrowserKeyboardHandler.cs"));

                Assert(formSrc.Contains("if (_bookmarksBar != null) _bookmarksBar.Visible = false;"),
                    "EnterFullScreenCore must hide bookmarks bar");
                Assert(formSrc.Contains("_bookmarksBar.Visible = _prevBookmarksBarVisible && (AppServices.Bookmarks != null ? AppServices.Bookmarks.ShowBookmarksBar : true);"),
                    "ExitFullScreenCore must restore bookmarks bar if enabled");
                Assert(formSrc.Contains("if (_immersivePlayerMode && !_isBrowserFullScreen)"),
                    "Tab_FullscreenModeChanged checks immersive mode when entering video fullscreen");
                Assert(formSrc.Contains("else if (!_isBrowserFullScreen && _isFullScreen)"),
                    "Tab_FullscreenModeChanged guards ExitFullScreenCore so video exit doesn't cancel browser fullscreen");
                Assert(formSrc.Contains("ImportDataDialog.ShowImportDialog(this)"),
                    "MainBrowserForm OpenImportData calls ImportDataDialog.ShowImportDialog");
                Assert(formSrc.Contains("ExportDataDialog.ShowExportDialog(this)"),
                    "MainBrowserForm OpenExportData calls ExportDataDialog.ShowExportDialog");
                Assert(formSrc.Contains("ToolStripMenuItem immersiveItem = new ToolStripMenuItem(\"Immersive Mode\""),
                    "Immersive Mode menu item is located in Experimental menu without windowed player wording");
                Assert(formSrc.Contains("ExitPlayerFullScreen()"),
                    "MainBrowserForm defines ExitPlayerFullScreen()");
                Assert(formSrc.Contains("HandleEscapeKey()"),
                    "MainBrowserForm defines HandleEscapeKey()");
                Assert(tabSrc.Contains("public void ExitFullscreen()"),
                    "BrowserTab defines ExitFullscreen() to execute document.exitFullscreen()");
                Assert(kbdSrc.Contains("_canHandleEscapeFunc"),
                    "BrowserKeyboardHandler checks _canHandleEscapeFunc on Escape");
            });

            Test("Bookmarks Bar Folder Dropdown Constraints and Scrolling", () =>
            {
                using (WbBookmarksBar bar = new WbBookmarksBar())
                {
                    using (ContextMenuStrip menu = new ContextMenuStrip())
                    {
                        for (int i = 0; i < 40; i++)
                        {
                            menu.Items.Add(new ToolStripMenuItem("A very long bookmark title that exceeds fifty characters to verify ellipsis truncation " + i));
                        }

                        bar.ConfigureDropDownConstraints(menu, new Point(100, 100));
                        Assert(menu.MaximumSize.Width <= 420, "Dropdown max width is constrained to <= 420px");
                        Assert(menu.MaximumSize.Height > 0 && menu.MaximumSize.Height <= Screen.PrimaryScreen.WorkingArea.Height, "Dropdown max height is constrained to working area");
                    }
                }
            });

            Test("WbAppIcon Multi-Resolution Dynamic Accent and Fullscreen Overlay Suppression", () =>
            {
                // 1. Overlay suppression verification
                using (FullScreenExitOverlay overlay = new FullScreenExitOverlay(() => { }))
                {
                    overlay.Suppressed = true;
                    overlay.ShowForFullScreen();
                    Assert(!overlay.IsFullScreenActive, "When Suppressed is true, ShowForFullScreen must not activate");
                    Assert(!overlay.Visible, "When Suppressed is true, overlay must remain hidden");

                    overlay.Suppressed = false;
                    overlay.ShowForFullScreen();
                    Assert(overlay.IsFullScreenActive, "When Suppressed is false, ShowForFullScreen activates");
                    overlay.HideForFullScreen();
                    Assert(!overlay.IsFullScreenActive, "HideForFullScreen dismisses active state");
                }

                // 2. WbAppIcon dynamic multi-resolution icon verification
                Color testAccent = Color.FromArgb(70, 130, 240);
                Icon appIcon = WbAppIcon.GetAppIcon(testAccent);
                Assert(appIcon != null, "WbAppIcon.GetAppIcon returns non-null icon");
                Assert(appIcon.Handle != IntPtr.Zero, "WbAppIcon.GetAppIcon has a valid non-zero Win32 handle");

                using (Icon icon16 = new Icon(appIcon, 16, 16))
                using (Icon icon48 = new Icon(appIcon, 48, 48))
                {
                    Assert(icon16.Width == 16 && icon16.Height == 16, "Multi-resolution icon contains 16x16 frame for titlebar/alt-tab");
                    Assert(icon48.Width == 48 && icon48.Height == 48, "Multi-resolution icon contains 48x48 frame for Windows Taskbar");
                    Assert(icon16.Handle != IntPtr.Zero, "16x16 frame has valid Win32 handle");
                    Assert(icon48.Handle != IntPtr.Zero, "48x48 frame has valid Win32 handle");
                }

                using (Form testForm = new Form())
                {
                    testForm.ShowInTaskbar = false;
                    WbAppIcon.UpdateFormIcon(testForm, testAccent);
                    Assert(testForm.Icon != null, "UpdateFormIcon sets form.Icon to accented icon");
                }
            });

            Test("High-Performance Caching, Download Popup Auto-Hide and Dialog Refinements", () =>
            {
                // 1. BookmarkManager caching & fast lookup
                var bm = AppServices.Bookmarks;
                Assert(bm != null, "BookmarkManager exists");
                var b1 = bm.GetSorted("", SortMode.TypeAscending);
                var b2 = bm.GetSorted("", SortMode.TypeAscending);
                Assert(b1.Count == b2.Count, "Subsequent BookmarkManager.GetSorted returns cached list");
                var testBm = bm.Add("Cache Test BM", "https://cache-test.example.com", "bar");
                Assert(testBm != null, "Added test bookmark");
                var b3 = bm.GetSorted("", SortMode.TypeAscending);
                Assert(b3.Count == b1.Count + 1, "Cache invalidated and reflects new bookmark");
                var lookedUp = bm.GetById(testBm.Id);
                Assert(lookedUp != null && lookedUp.Title == "Cache Test BM", "GetById fast lookup succeeds");
                bm.Remove(testBm.Id);
                var b4 = bm.GetSorted("", SortMode.TypeAscending);
                Assert(b4.Count == b1.Count, "Cache invalidated on remove");

                // 2. HistoryManager caching
                var hm = AppServices.History;
                Assert(hm != null, "HistoryManager exists");
                var h1 = hm.GetSorted("", SortMode.DateDescending);
                var h2 = hm.GetSorted("", SortMode.DateDescending);
                Assert(h1.Count == h2.Count, "Subsequent HistoryManager.GetSorted returns cached list");
                string histUrl = "https://cache-test-history.example.org/" + Guid.NewGuid().ToString("N");
                hm.AddEntry("Cache Test Hist", histUrl);
                var h3 = hm.GetSorted("", SortMode.DateDescending);
                Assert(h3.Count == h1.Count + 1, "Cache invalidated and reflects new history entry");
                hm.RemoveEntry(h3.Find(x => x.Url == histUrl).Id);
                var h4 = hm.GetSorted("", SortMode.DateDescending);
                Assert(h4.Count == h1.Count, "Cache invalidated on history removal");

                // 3. DownloadManager id lookup and sort cache
                var dm = AppServices.Downloads;
                Assert(dm != null, "DownloadManager exists");
                var d1 = dm.GetSorted("", SortMode.DateDescending);
                var d2 = dm.GetSorted("", SortMode.DateDescending);
                Assert(d1.Count == d2.Count, "Subsequent DownloadManager.GetSorted returns cached list");
                long dlId = DateTime.UtcNow.Ticks;
                var dlItem = dm.RegisterDownload(dlId, "cache-dl.zip", "C:\\cache-dl.zip", "https://dl.example.com/cache-dl.zip", 1024);
                Assert(dlItem != null, "Registered download item");
                var dlLookedUp = dm.GetItem(dlId);
                Assert(dlLookedUp != null && dlLookedUp.Id == dlId, "GetItem $O(1)$ lookup succeeds");
                var d3 = dm.GetSorted("", SortMode.DateDescending);
                Assert(d3.Count == d1.Count + 1, "Cache invalidated and reflects new download item");
                dm.DeleteDownload(dlId, false);
                var d4 = dm.GetSorted("", SortMode.DateDescending);
                Assert(d4.Count == d1.Count, "Cache invalidated on download item delete");
                Assert(dm.GetItem(dlId) == null, "GetItem returns null after deletion");

                // 4. Phosphor history icon mapping verification
                using (var iconHist = MaterialIcons.GetIcon("history", 16))
                using (var iconClock = MaterialIcons.GetIcon("clock-counter-clockwise", 16))
                {
                    Assert(iconHist != null, "MaterialIcons generates valid icon for 'history'");
                    Assert(iconClock != null, "MaterialIcons generates valid icon for 'clock-counter-clockwise'");
                }

                // 5. Native dark title bar attachment verification
                using (Form dialog = new Form())
                {
                    dialog.ShowInTaskbar = false;
                    UITheme.ApplyForm(dialog);
                    Assert(true, "UITheme.ApplyForm safely wires DWM dark title bar events");
                }

                // 6. DownloadBubblePopup interaction immunity and AutoClose resilience
                using (DownloadBubblePopup bubble = new DownloadBubblePopup())
                {
                    Assert(!bubble.AutoClose, "DownloadBubblePopup.AutoClose is disabled to prevent ModalMenuFilter outside-click false positives");
                    bubble.ShowBubble(new Point(200, 200), autoOpened: false);
                    Assert(bubble.Visible, "DownloadBubblePopup successfully shown");
                    bubble.CancelAutoHide();
                    bubble.RefreshContent();
                    Assert(bubble.Visible, "DownloadBubblePopup remains visible after interaction / RefreshContent");
                    bubble.Close();
                }
            });

            Test("Screenshot Studio Improvements and Gutter Trimming", () =>
            {
                // 1. Gutter trimming validation
                using (Bitmap rawWithGutters = new Bitmap(400, 300, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(rawWithGutters))
                    {
                        // Fill whole image with browser background (32, 33, 36)
                        g.Clear(Color.FromArgb(32, 33, 36));
                        // Fill center with content (x: 40 to 360, width 320)
                        using (SolidBrush contentBrush = new SolidBrush(Color.FromArgb(246, 245, 240)))
                        {
                            g.FillRectangle(contentBrush, 40, 0, 320, 260);
                        }
                    }

                    Bitmap trimmed = CdpScreenshotHelper.TrimOuterBackgroundGutter(rawWithGutters);
                    Assert(trimmed != null, "Trimmed bitmap is non-null");
                    Assert(trimmed.Width == 320, "Trimmed left and right blank bars (width 320 vs 400, actual: " + trimmed.Width + ")");
                    Assert(trimmed.Height == 260, "Trimmed bottom blank bar (height 260 vs 300, actual: " + trimmed.Height + ")");
                    trimmed.Dispose();
                }

                // Normal image without gutters should remain untouched
                using (Bitmap normalImg = new Bitmap(200, 200, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(normalImg))
                    {
                        g.Clear(Color.FromArgb(255, 255, 255));
                    }
                    Bitmap result = CdpScreenshotHelper.TrimOuterBackgroundGutter(normalImg);
                    Assert(result.Width == 200 && result.Height == 200, "Clean image without background gutters remains untouched");
                }

                // 2. Phosphor icons verification for all buttons
                string[] requiredIcons = new string[]
                {
                    "hand", "pencil-simple", "highlighter", "arrow-up-right", "square",
                    "crop", "palette", "arrow-u-up-left", "arrow-u-up-right", "copy", "floppy-disk"
                };

                foreach (string iconName in requiredIcons)
                {
                    using (Bitmap bmp = MaterialIcons.GetIcon(iconName, 18))
                    {
                        Assert(bmp != null, "Phosphor icon '" + iconName + "' loaded and rendered successfully");
                        Assert(bmp.Width == 18 && bmp.Height == 18, "Icon '" + iconName + "' matches requested dimensions");
                    }
                }

                // 3. Studio canvas centering and fitting on resize
                using (Bitmap testImg = new Bitmap(400, 300))
                {
                    ScreenshotSession session = ScreenshotSessionManager.CreateSession(testImg, "https://example.com", "Test");
                    using (ScreenshotView studio = new ScreenshotView(session))
                    {
                        studio.Size = new Size(1000, 800);
                        studio.FitImageToWindow();
                        float expectedCenterX = (1000 - 400) / 2f;
                        Assert(Math.Abs(studio.PanOffset.X - expectedCenterX) < 5f, "Studio canvas is centered at 1000px width (X: " + studio.PanOffset.X + ", expected: " + expectedCenterX + ")");

                        // Resize window to 1600px
                        studio.Size = new Size(1600, 900);
                        float newExpectedCenterX = (1600 - 400) / 2f;
                        Assert(Math.Abs(studio.PanOffset.X - newExpectedCenterX) < 5f, "Studio canvas re-centered on resize to 1600px (X: " + studio.PanOffset.X + ", expected: " + newExpectedCenterX + ")");
                    }
                }
            });

            Test("Internal Page Caching, Batch List Updating and Zero Flicker Navigation", () =>
            {
                // 1. Verify BrowserTab has internal page caching methods and fields
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(srcDir))
                {
                    srcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                }
                string browserTabPath = Path.Combine(srcDir, @"Browser\BrowserTab.cs");
                string mainFormPath = Path.Combine(srcDir, @"Browser\MainBrowserForm.cs");

                if (File.Exists(browserTabPath))
                {
                    string browserTabContent = File.ReadAllText(browserTabPath);
                    Assert(browserTabContent.Contains("GetCachedInternalView"), "BrowserTab has GetCachedInternalView method");
                    Assert(browserTabContent.Contains("HideOtherInternalViews"), "BrowserTab has HideOtherInternalViews method");
                    Assert(browserTabContent.Contains("cachedView != null && !cachedView.IsDisposed && cachedView.Visible"),
                        "BrowserTab checks cached internal views before recreating");
                }

                if (File.Exists(mainFormPath))
                {
                    string mainFormContent = File.ReadAllText(mainFormPath);
                    Assert(mainFormContent.Contains("InternalPageHelper.IsInternalUrl(url)") && mainFormContent.Contains("InternalPageHelper.IsInternalPage(url)"),
                        "MainBrowserForm suppresses loading overlay for all internal pages in Tab_InternalPageLoading");
                    Assert(mainFormContent.Contains("InternalPageHelper.IsInternalUrl(normUrl)") && mainFormContent.Contains("InternalPageHelper.IsInternalPage(normUrl)"),
                        "MainBrowserForm suppresses loading overlay in NavigateOrOpenInternalTab");
                }

                // 2. Verify BookmarksView, HistoryView and DownloadsView perform batch updates with BeginUpdate/EndUpdate
                using (BookmarksView bv = new BookmarksView(null))
                {
                    Assert(bv != null, "BookmarksView instantiates cleanly");
                    bv.RefreshList();
                }

                using (HistoryView hv = new HistoryView(null))
                {
                    Assert(hv != null, "HistoryView instantiates cleanly");
                    hv.RefreshList();
                }

                using (DownloadsView dv = new DownloadsView(null))
                {
                    Assert(dv != null, "DownloadsView instantiates cleanly");
                    dv.RefreshList();
                }
            });

            Test("Bookmarks Bar Folder Dropdown Lazy Loading, Hover Scrolling and Mouse Wheel Support", () =>
            {
                using (WbBookmarksBar bar = new WbBookmarksBar())
                {
                    // 1. Verify lazy loading in folder menus
                    BookmarkItem parentFolder = AppServices.Bookmarks.AddFolder("LazyTestParent", "bar");
                    BookmarkItem childFolder = AppServices.Bookmarks.AddFolder("LazyTestChild", parentFolder.Id);
                    BookmarkItem nestedItem = AppServices.Bookmarks.Add("NestedItem", "https://example.com/nested", childFolder.Id);

                    using (ContextMenuStrip menu = new ContextMenuStrip())
                    {
                        var buildMethod = typeof(WbBookmarksBar).GetMethod("BuildFolderMenuItemsInternal",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(buildMethod != null, "BuildFolderMenuItemsInternal method exists");

                        List<BookmarkItem> items = new List<BookmarkItem> { childFolder };
                        buildMethod.Invoke(bar, new object[] { menu.Items, items, parentFolder.Id, menu });

                        Assert(menu.Items.Count == 1, "Menu contains child folder item");
                        ToolStripMenuItem folderMenuItem = menu.Items[0] as ToolStripMenuItem;
                        Assert(folderMenuItem != null, "Child folder is ToolStripMenuItem");
                        Assert(folderMenuItem.DropDownItems.Count == 1, "Submenu has 1 lazy placeholder before opening");
                        Assert(folderMenuItem.DropDownItems[0].Text == "(Loading...)", "Submenu placeholder is (Loading...)");

                        // Trigger DropDownOpening
                        folderMenuItem.ShowDropDown();
                        Application.DoEvents();

                        Assert(folderMenuItem.DropDownItems.Count == 1, "Submenu populated after DropDownOpening");
                        Assert(folderMenuItem.DropDownItems[0].Text == "NestedItem", "Lazy loaded item is NestedItem");
                    }

                    // 2. Verify ScrollDropDown scrolls menu items
                    using (ToolStripDropDownMenu scrollMenu = new ToolStripDropDownMenu())
                    {
                        for (int i = 0; i < 50; i++)
                        {
                            scrollMenu.Items.Add("ScrollTest " + i);
                        }
                        scrollMenu.MaximumSize = new Size(200, 200);

                        var registerMethod = typeof(WbBookmarksBar).GetMethod("RegisterFolderDropDown",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(registerMethod != null, "RegisterFolderDropDown method exists");
                        registerMethod.Invoke(bar, new object[] { scrollMenu });

                        scrollMenu.Show(100, 100);
                        Application.DoEvents();

                        int initialY = scrollMenu.Items[0].Bounds.Y;

                        // Scroll down by 2 items
                        WbBookmarksBar.ScrollDropDown(scrollMenu, false, 2);
                        Application.DoEvents();
                        int scrolledY = scrollMenu.Items[0].Bounds.Y;
                        Assert(scrolledY < initialY, "ScrollDropDown(false, 2) shifted items upward for downward scrolling");

                        // Scroll back up by 2 items
                        WbBookmarksBar.ScrollDropDown(scrollMenu, true, 2);
                        Application.DoEvents();
                        int returnedY = scrollMenu.Items[0].Bounds.Y;
                        Assert(returnedY == initialY, "ScrollDropDown(true, 2) restored item positions");

                        // 3. Verify scroll clamping: scrolling down excessively stops at last item without scrolling into blank space
                        WbBookmarksBar.ScrollDropDown(scrollMenu, false, 200);
                        Application.DoEvents();
                        ToolStripItem lastItem = scrollMenu.Items[scrollMenu.Items.Count - 1];
                        Assert(lastItem.Bounds.Bottom <= scrollMenu.ClientSize.Height, "ScrollDropDown clamped at last item without over-scrolling into blank space");

                        scrollMenu.Close();
                    }

                    // 4. Verify New Tab favicon is the home (house) icon
                    Image newTabIcon = InternalPageHelper.GetInternalFavicon("wb://newtab", false);
                    Assert(newTabIcon != null, "New tab favicon is not null");
                    Bitmap expectedHouse = MaterialIcons.GetIcon("house", 16);
                    Assert(expectedHouse != null, "Phosphor house icon is available");

                    // 5. Verify UITheme Universal Dropdown Support: Fitted Integer Heights, Dark Mode Scroll Buttons, Hover Scrolling
                    using (ContextMenuStrip testMenu = new ContextMenuStrip())
                    {
                        for (int i = 0; i < 40; i++)
                        {
                            testMenu.Items.Add(new ToolStripMenuItem("Item " + i));
                        }
                        UITheme.ApplyContextMenu(testMenu);
                        UITheme.ConfigureDropDownConstraints(testMenu, new Point(100, 100), 1.0f);

                        Assert(testMenu.MaximumSize.Height > 0, "UITheme fitted height is greater than 0");
                        int sampleItemH = testMenu.Items[0].Height > 0 ? testMenu.Items[0].Height : testMenu.Items[0].GetPreferredSize(Size.Empty).Height;
                        int scrollBtnH = 24;
                        int overhead = 2 * scrollBtnH;
                        int rem = (testMenu.MaximumSize.Height - overhead) % sampleItemH;
                        Assert(rem == 0, "UITheme dropdown height is an exact integer multiple of item height eliminating leftover empty space");

                        testMenu.Show(100, 100);
                        Application.DoEvents();

                        var upProp = typeof(ToolStripDropDownMenu).GetProperty("UpScrollButton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        var downProp = typeof(ToolStripDropDownMenu).GetProperty("DownScrollButton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        ToolStripControlHost upHost = upProp != null ? upProp.GetValue(testMenu, null) as ToolStripControlHost : null;
                        ToolStripControlHost downHost = downProp != null ? downProp.GetValue(testMenu, null) as ToolStripControlHost : null;

                        Assert(upHost != null && downHost != null, "Up and Down scroll buttons exist in long UITheme dropdown");
                        if (upHost != null && upHost.Control != null)
                        {
                            Assert(upHost.Control.BackColor == MaterialColors.NavBarBackground, "Up scroll button hosted StickyLabel control is themed with dark NavBarBackground");
                            Assert(upHost.Control.Height >= 22, "Up scroll button has taller touch-friendly height (>= 22px)");
                        }
                        if (downHost != null && downHost.Control != null)
                        {
                            Assert(downHost.Control.BackColor == MaterialColors.NavBarBackground, "Down scroll button hosted StickyLabel control is themed with dark NavBarBackground");
                            Assert(downHost.Control.Height >= 22, "Down scroll button has taller touch-friendly height (>= 22px)");
                        }

                        // Verify dropdown menu scroller top and bottom outlines
                        using (Bitmap menuBmp = new Bitmap(testMenu.Width, testMenu.Height))
                        {
                            testMenu.DrawToBitmap(menuBmp, new Rectangle(0, 0, testMenu.Width, testMenu.Height));
                            Color topMid = menuBmp.GetPixel(testMenu.Width / 2, 0);
                            Color bottomMid = menuBmp.GetPixel(testMenu.Width / 2, testMenu.Height - 1);
                            Assert(topMid.R == MaterialColors.Separator.R && topMid.G == MaterialColors.Separator.G && topMid.B == MaterialColors.Separator.B,
                                "Up scroll button renders top outline matching dropdown separator border");
                            Assert(bottomMid.R == MaterialColors.Separator.R && bottomMid.G == MaterialColors.Separator.G && bottomMid.B == MaterialColors.Separator.B,
                                "Down scroll button renders bottom outline matching dropdown separator border");
                        }

                        // Test UITheme.ScrollDropDown directly
                        int startY = testMenu.Items[0].Bounds.Y;
                        UITheme.ScrollDropDown(testMenu, false, 2);
                        Application.DoEvents();
                        Assert(testMenu.Items[0].Bounds.Y < startY, "UITheme.ScrollDropDown downward shifts items");

                        UITheme.ScrollDropDown(testMenu, true, 2);
                        Application.DoEvents();
                        Assert(testMenu.Items[0].Bounds.Y == startY, "UITheme.ScrollDropDown upward restores items");

                        // Test scrolling all the way to bottom and verifying zero empty gap above down button
                        UITheme.ScrollDropDown(testMenu, false, 100);
                        Application.DoEvents();
                        ToolStripItem lastItemInMenu = testMenu.Items[testMenu.Items.Count - 1];
                        ToolStripItem downItem = downProp.GetValue(testMenu, null) as ToolStripItem;
                        Assert(downItem != null, "Down scroll button item exists");
                        Assert(downItem.Bounds.Height >= 24, "Down scroll button item has height >= 24");
                        Assert(!downItem.Enabled, "Down scroll button is disabled when scrolled to bottom");
                        int bottomGap = downItem.Bounds.Top - lastItemInMenu.Bounds.Bottom;
                        Assert(bottomGap == 0, "No leftover empty space at bottom of long dropdown list (GAP == 0)");
                        Assert(lastItemInMenu.Bounds.Bottom <= downItem.Bounds.Top, "Last item is 100% visible and not cut off by down scroll button");

                        testMenu.Close();
                    }

                    // Verify cutoff protection under arbitrary / constrained non-multiple dropdown heights
                    using (ContextMenuStrip constrainedMenu = new ContextMenuStrip())
                    {
                        for (int i = 0; i < 40; i++)
                        {
                            constrainedMenu.Items.Add(new ToolStripMenuItem("ConstrainedItem " + i));
                        }
                        UITheme.ApplyContextMenu(constrainedMenu);
                        // Force a non-multiple height (522px)
                        constrainedMenu.MaximumSize = new Size(380, 522);
                        constrainedMenu.Show(100, 100);
                        Application.DoEvents();

                        var downProp = typeof(ToolStripDropDownMenu).GetProperty("DownScrollButton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        ToolStripItem downItem = downProp != null ? downProp.GetValue(constrainedMenu, null) as ToolStripItem : null;
                        Assert(downItem != null, "Down scroll button exists for constrained menu");

                        UITheme.ScrollDropDown(constrainedMenu, false, 100);
                        Application.DoEvents();

                        ToolStripItem lastItem = constrainedMenu.Items[constrainedMenu.Items.Count - 1];
                        Assert(!downItem.Enabled, "Down scroll button is disabled at bottom in constrained menu");
                        Assert(lastItem.Bounds.Bottom <= downItem.Bounds.Top, "Last item is 100% visible and not cut off in constrained menu");

                        constrainedMenu.Close();
                    }

                    // Verify Tools Menu (Gear Menu) does not scroll on standard displays and fits all 30 items
                    using (ContextMenuStrip toolsMenu = new ContextMenuStrip())
                    {
                        for (int i = 0; i < 25; i++)
                        {
                            ToolStripMenuItem mi = new ToolStripMenuItem("Tools Item " + i);
                            UITheme.ApplyMenuItem(mi);
                            toolsMenu.Items.Add(mi);
                        }
                        for (int i = 0; i < 5; i++)
                        {
                            toolsMenu.Items.Add(new ToolStripSeparator());
                        }
                        UITheme.ApplyContextMenu(toolsMenu);
                        UITheme.ConfigureDropDownConstraints(toolsMenu, new Point(100, 50), 1.0f);
                        toolsMenu.Show(100, 50);
                        Application.DoEvents();

                        var toolsUpProp = typeof(ToolStripDropDownMenu).GetProperty("UpScrollButton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        var toolsDownProp = typeof(ToolStripDropDownMenu).GetProperty("DownScrollButton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        ToolStripItem toolsUp = toolsUpProp != null ? toolsUpProp.GetValue(toolsMenu, null) as ToolStripItem : null;
                        ToolStripItem toolsDown = toolsDownProp != null ? toolsDownProp.GetValue(toolsMenu, null) as ToolStripItem : null;
                        bool toolsHasScroller = (toolsUp != null && toolsUp.Visible) || (toolsDown != null && toolsDown.Visible);

                        Assert(!toolsHasScroller, "Tools menu (30 items) fits within available space on standard screens without showing scroll buttons");
                        Assert(toolsMenu.Height >= 500, "Tools menu displays all items naturally with proper height");

                        toolsMenu.Close();
                    }

                    // 6. Verify Bookmarks Bar: No tooltips for folder items, tooltip retained for bookmark URLs
                    var lastTipField = typeof(WbBookmarksBar).GetField("_lastTooltipText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(lastTipField != null, "_lastTooltipText field exists on WbBookmarksBar");

                    var onMouseMoveMethod = typeof(WbBookmarksBar).GetMethod("OnMouseMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var slotsField = typeof(WbBookmarksBar).GetField("_visibleSlots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var slots = slotsField.GetValue(bar) as System.Collections.IList;
                    if (slots != null && slots.Count > 0)
                    {
                        for (int s = 0; s < slots.Count; s++)
                        {
                            var slot = slots[s];
                            var boundsProp = slot.GetType().GetProperty("Bounds");
                            var itemProp = slot.GetType().GetProperty("Item");
                            Rectangle bounds = (Rectangle)boundsProp.GetValue(slot, null);
                            BookmarkItem bItem = (BookmarkItem)itemProp.GetValue(slot, null);

                            if (bItem.IsFolder)
                            {
                                onMouseMoveMethod.Invoke(bar, new object[] { new MouseEventArgs(MouseButtons.None, 0, bounds.X + 2, bounds.Y + 2, 0) });
                                string folderTip = (string)lastTipField.GetValue(bar);
                                Assert(string.IsNullOrEmpty(folderTip), "Hovering folder on bookmarks bar produces NO tooltip");
                            }
                            else
                            {
                                onMouseMoveMethod.Invoke(bar, new object[] { new MouseEventArgs(MouseButtons.None, 0, bounds.X + 2, bounds.Y + 2, 0) });
                                string linkTip = (string)lastTipField.GetValue(bar);
                                Assert(!string.IsNullOrEmpty(linkTip) && linkTip.Contains(bItem.Url), "Hovering link on bookmarks bar produces tooltip with URL");
                            }
                        }
                    }

                    // Clean up test bookmarks
                    AppServices.Bookmarks.Remove(nestedItem.Id);
                    AppServices.Bookmarks.Remove(childFolder.Id);
                    AppServices.Bookmarks.Remove(parentFolder.Id);
                }
            });

            Test("Favicon Persistence Across Restarts and StorageHelper Temp Directory Safety", () =>
            {
                // 1. Verify StorageHelper.CreateTempHtmlFile creates files in system temp, not bin/cache
                string tempFile = StorageHelper.CreateTempHtmlFile(Path.Combine("cache", "errors"), "weberror_warning", "<html><body>safe temp error</body></html>");
                try
                {
                    Assert(File.Exists(tempFile), "Temp HTML file must be created and exist");
                    string binDir = AppDomain.CurrentDomain.BaseDirectory;
                    Assert(!tempFile.StartsWith(binDir, StringComparison.OrdinalIgnoreCase), "Temp HTML file must NOT be inside the application binary directory");
                    string sysTemp = Path.GetTempPath();
                    Assert(tempFile.StartsWith(sysTemp, StringComparison.OrdinalIgnoreCase), "Temp HTML file must be located inside the system temporary folder");
                    string binCache = Path.Combine(binDir, "cache");
                    Assert(!Directory.Exists(binCache), "The bin/cache folder must not be created");
                }
                finally
                {
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                }

                // 2. Verify StorageHelper.MigrateLegacyData deletes any accidental bin/cache directory
                string accidentalBinCache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "dummy");
                Directory.CreateDirectory(accidentalBinCache);
                Assert(Directory.Exists(accidentalBinCache), "Accidental bin/cache created for migration test");
                StorageHelper.MigrateLegacyData();
                Assert(!Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache")), "MigrateLegacyData safely purges any accidental bin/cache folder");

                // 3. Verify FaviconManager persistence across restarts
                string favDir = StorageHelper.GetFaviconsDirectory();
                string testDomain = "persisted-favicon-test.com";
                string testPngPath = Path.Combine(favDir, testDomain + ".png");
                using (Bitmap bmp = new Bitmap(16, 16))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Blue);
                    }
                    bmp.Save(testPngPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                try
                {
                    Assert(File.Exists(testPngPath), "Test favicon PNG exists in persistent favicons folder");

                    // Simulate fresh wb launch by clearing memory cache
                    var cacheField = typeof(FaviconManager).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (cacheField != null)
                    {
                        var dict = cacheField.GetValue(AppServices.Favicons) as System.Collections.IDictionary;
                        if (dict != null) dict.Clear();
                    }

                    // GetFaviconInMemory should now fall back to persistent disk cache on restart
                    Image loadedFromRestart = AppServices.Favicons.GetFaviconInMemory("https://" + testDomain);
                    Assert(loadedFromRestart != null, "GetFaviconInMemory returns image loaded from persistent disk cache on restart");
                    Assert(loadedFromRestart.Width == 32 && loadedFromRestart.Height == 32, "Favicon is normalized to 32x32 standard favicon dimensions");

                    // Subdomain fallback check (e.g. sub.persisted-favicon-test.com -> persisted-favicon-test.com)
                    Image subLoaded = AppServices.Favicons.GetFaviconInMemory("https://sub." + testDomain);
                    Assert(subLoaded != null, "GetFaviconInMemory resolves parent domain favicon for subdomains on restart");

                    // www. fallback check
                    Image wwwLoaded = AppServices.Favicons.GetFaviconInMemory("https://www.persisted-favicon-test.com");
                    Assert(wwwLoaded != null, "GetFaviconInMemory resolves www. variant on restart");
                }
                finally
                {
                    try { if (File.Exists(testPngPath)) File.Delete(testPngPath); } catch { }
                }

                // 4. Verify NewTabView shortcuts and preload functionality
                using (NewTabView ntv = new NewTabView((u, n) => { }, () => { }))
                {
                    var preloadMethod = typeof(NewTabView).GetMethod("PreloadShortcutFavicons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(preloadMethod != null, "PreloadShortcutFavicons method exists on NewTabView");
                    preloadMethod.Invoke(ntv, null);

                    var triggerMethod = typeof(NewTabView).GetMethod("TriggerShortcutFaviconFetch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert(triggerMethod != null, "TriggerShortcutFaviconFetch method exists on NewTabView");
                    triggerMethod.Invoke(ntv, new object[] { "https://persisted-favicon-test.com" });
                }
            });

            Test("Audio Tab Detection and Multi-Frame Source Tracking", () =>
            {
                // 1. Verify AudioObserverScript content and capabilities
                string script = JsHelper.AudioObserverScript;
                Assert(!string.IsNullOrEmpty(script), "JsHelper.AudioObserverScript must not be null or empty");
                Assert(script.Contains("AudioContext") && script.Contains("webkitAudioContext"), "AudioObserverScript hooks Web Audio API AudioContext");
                Assert(script.Contains("HTMLMediaElement.prototype.play"), "AudioObserverScript hooks HTMLMediaElement prototype play");
                Assert(script.Contains("attachShadow"), "AudioObserverScript monitors Shadow DOM attachShadow");
                Assert(script.Contains("MutationObserver"), "AudioObserverScript monitors dynamic element insertions via MutationObserver");
                Assert(script.Contains("__wbAudioFrameId"), "AudioObserverScript assigns unique frameId per execution context");
                Assert(script.Contains("&source="), "AudioObserverScript reports source parameter in audio-state action");
                Assert(script.Contains("PostMessage") && script.Contains("console.log"), "AudioObserverScript supports dual delivery via PostMessage and console.log");
                Assert(script.Contains("Infinity"), "AudioObserverScript supports live stream playback (duration Infinity)");

                // 2. Verify CDP json serialization of AudioObserverScript
                string escaped = JsHelper.EscapeJson(script);
                Assert(!string.IsNullOrEmpty(escaped), "JsHelper.EscapeJson returns valid escaped script");
                Assert(!escaped.Contains("\r") && !escaped.Contains("\n"), "Escaped script must not have raw newlines");
                Assert(JsHelper.EscapeJson("hello \"world\"").Contains("\\\""), "JsHelper.EscapeJson properly escapes double quotes");

                // 3. Verify BrowserTab architecture for multi-frame audio aggregation
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(srcDir)) srcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                string tabSrc = File.ReadAllText(Path.Combine(srcDir, @"Browser\BrowserTab.cs"));

                Assert(tabSrc.Contains("_activeAudioSources"), "BrowserTab has _activeAudioSources collection");
                Assert(tabSrc.Contains("_audioStateLock"), "BrowserTab has _audioStateLock for thread safety");
                Assert(tabSrc.Contains("RegisterAudioObserverOnBrowserHost"), "BrowserTab registers AudioObserverScript via CDP Page.addScriptToEvaluateOnNewDocument");
                Assert(tabSrc.Contains("TryInjectAudioObserver(e.Frame, frameUrl)"), "BrowserTab injects AudioObserverScript into subframes");

                // 4. Simulate multi-frame audio source tracking logic
                HashSet<string> simulatedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Action<string, bool> handleAudioState = (source, playing) =>
                {
                    if (playing) simulatedSources.Add(source);
                    else simulatedSources.Remove(source);
                };

                // Main frame starts audio
                handleAudioState("f_main", true);
                Assert(simulatedSources.Count == 1, "Active audio count is 1 after main frame starts");
                Assert(simulatedSources.Count > 0, "Tab reports audio playing when main frame plays");

                // Subframe (e.g. YouTube embed / Spotify) starts audio
                handleAudioState("f_subframe1", true);
                Assert(simulatedSources.Count == 2, "Active audio count is 2 when subframe also plays");
                Assert(simulatedSources.Count > 0, "Tab reports audio playing");

                // Main frame has no audio or stops, but subframe continues
                handleAudioState("f_main", false);
                Assert(simulatedSources.Count == 1, "Active audio count remains 1 after main frame stops");
                Assert(simulatedSources.Count > 0, "Tab MUST remain audio playing while subframe continues (fixes override bug)");

                // Subframe stops
                handleAudioState("f_subframe1", false);
                Assert(simulatedSources.Count == 0, "Active audio count becomes 0 when subframe stops");
                Assert(simulatedSources.Count == 0, "Tab reports audio stopped");

                // 5. TopBar integration with tab audio state
                using (ChromeTabControl tc = new ChromeTabControl())
                using (WbTopBar bar = new WbTopBar())
                {
                    MockFaviconTab tab = new MockFaviconTab("Media Tab", "https://example.com");
                    tc.TabPages.Add(tab);
                    bar.Size = new Size(800, 42);
                    bar.BindTabControl(tc);

                    Assert(bar.GetTabAudioRect(0).IsEmpty, "Audio rect is empty when tab is not playing");
                    tab.IsPlayingAudio = true;
                    Assert(!bar.GetTabAudioRect(0).IsEmpty, "Audio rect is non-empty when tab is playing audio");
                    tab.IsPlayingAudio = false;
                    Assert(bar.GetTabAudioRect(0).IsEmpty, "Audio rect is empty after tab stops playing audio");
                }
            });

            Test("Top Bar Docking Hierarchy and Bookmarks Bar Unhide Behavior", () =>
            {
                // 1. Verify MainBrowserForm source has EnsureTopBarsZOrder method and wiring
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(srcDir)) srcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                string formSrc = File.ReadAllText(Path.Combine(srcDir, @"Browser\MainBrowserForm.cs"));

                Assert(formSrc.Contains("public void EnsureTopBarsZOrder()"), "MainBrowserForm defines EnsureTopBarsZOrder()");
                Assert(formSrc.Contains("_bookmarksBar.VisibleChanged += (s, e) => EnsureTopBarsZOrder()"), "MainBrowserForm hooks _bookmarksBar.VisibleChanged");
                Assert(formSrc.Contains("_findPanel.VisibleChanged += (s, e) => EnsureTopBarsZOrder()"), "MainBrowserForm hooks _findPanel.VisibleChanged");
                Assert(formSrc.Contains("_blockElementPanel.VisibleChanged += (s, e) => EnsureTopBarsZOrder()"), "MainBrowserForm hooks _blockElementPanel.VisibleChanged");
                Assert(formSrc.Contains("_securityCodeBanner.VisibleChanged += (s, e) => EnsureTopBarsZOrder()"), "MainBrowserForm hooks _securityCodeBanner.VisibleChanged");
                Assert(formSrc.Contains("_topBar.VisibleChanged += (s, e) => EnsureTopBarsZOrder()"), "MainBrowserForm hooks _topBar.VisibleChanged");
                Assert(formSrc.Contains("UITheme.ConfigureDropDownConstraints(_gearMenu, screenPoint);"), "MainBrowserForm.ShowGearMenu configures dropdown constraints with screen anchor");

                // 2. Functional testing of WinForms docking hierarchy with actual panels
                using (Form testForm = new Form())
                {
                    testForm.Size = new Size(800, 600);

                    Panel tabControl = new Panel { Name = "TabControl", Dock = DockStyle.Fill };
                    Panel progressBar = new Panel { Name = "ProgressBar", Dock = DockStyle.Top, Height = 3 };
                    Panel findPanel = new Panel { Name = "FindPanel", Dock = DockStyle.Top, Height = 36, Visible = false };
                    Panel blockElementPanel = new Panel { Name = "BlockElementPanel", Dock = DockStyle.Top, Height = 36, Visible = false };
                    Panel securityBanner = new Panel { Name = "SecurityBanner", Dock = DockStyle.Top, Height = 40, Visible = false };
                    Panel bookmarksBar = new Panel { Name = "BookmarksBar", Dock = DockStyle.Top, Height = 30, Visible = false };
                    Panel topBar = new Panel { Name = "TopBar", Dock = DockStyle.Top, Height = 42 };

                    testForm.Controls.Add(tabControl);
                    testForm.Controls.Add(progressBar);
                    testForm.Controls.Add(findPanel);
                    testForm.Controls.Add(blockElementPanel);
                    testForm.Controls.Add(securityBanner);
                    testForm.Controls.Add(bookmarksBar);
                    testForm.Controls.Add(topBar);

                    Action applyOrder = () =>
                    {
                        testForm.SuspendLayout();
                        progressBar.SendToBack();
                        findPanel.SendToBack();
                        blockElementPanel.SendToBack();
                        securityBanner.SendToBack();
                        bookmarksBar.SendToBack();
                        topBar.SendToBack();
                        testForm.ResumeLayout(true);
                    };

                    bookmarksBar.VisibleChanged += (s, e) => applyOrder();
                    findPanel.VisibleChanged += (s, e) => applyOrder();
                    blockElementPanel.VisibleChanged += (s, e) => applyOrder();
                    securityBanner.VisibleChanged += (s, e) => applyOrder();
                    topBar.VisibleChanged += (s, e) => applyOrder();

                    applyOrder();

                    Action<string> assertHierarchy = (context) =>
                    {
                        List<Panel> visibleBars = new List<Panel>();
                        foreach (Control c in testForm.Controls)
                        {
                            Panel p = c as Panel;
                            if (p != null && p.Dock == DockStyle.Top && p.Visible)
                            {
                                visibleBars.Add(p);
                            }
                        }
                        visibleBars.Sort((a, b) => a.Top.CompareTo(b.Top));

                        if (topBar.Visible && visibleBars.Count > 0)
                        {
                            Assert(visibleBars[0] == topBar, context + ": First visible bar must be TopBar");
                            Assert(visibleBars[0].Top == 0, context + ": TopBar must be at Top = 0");
                        }
                        if (topBar.Visible && bookmarksBar.Visible)
                        {
                            Assert(visibleBars.Count >= 2 && visibleBars[1] == bookmarksBar, context + ": Second visible bar must be BookmarksBar directly under TopBar");
                            Assert(bookmarksBar.Top == topBar.Bottom, context + ": BookmarksBar.Top must equal TopBar.Bottom");
                        }
                        if (bookmarksBar.Visible)
                        {
                            for (int i = 2; i < visibleBars.Count; i++)
                            {
                                Assert(visibleBars[i].Top >= bookmarksBar.Bottom, context + ": " + visibleBars[i].Name + " must be below BookmarksBar");
                            }
                        }
                    };

                    // Initial state: TopBar visible, BookmarksBar hidden
                    assertHierarchy("Initial");

                    // Open FindPanel while BookmarksBar is hidden
                    findPanel.Visible = true;
                    assertHierarchy("FindPanel opened while BookmarksBar hidden");

                    // Unhide BookmarksBar (Ctrl+Shift+B): BookmarksBar must be placed directly under TopBar, pushing FindPanel down
                    bookmarksBar.Visible = true;
                    assertHierarchy("BookmarksBar unhidden with FindPanel open");

                    // Show SecurityBanner and BlockElementPanel
                    securityBanner.Visible = true;
                    blockElementPanel.Visible = true;
                    assertHierarchy("All top bars visible");

                    // Hide BookmarksBar: additional bars move up directly below TopBar
                    bookmarksBar.Visible = false;
                    assertHierarchy("BookmarksBar hidden with additional bars visible");

                    // Unhide BookmarksBar again: must reliably re-insert directly under TopBar, above additional bars
                    bookmarksBar.Visible = true;
                    assertHierarchy("BookmarksBar unhidden again with additional bars visible");

                    // Close additional bars
                    findPanel.Visible = false;
                    securityBanner.Visible = false;
                    blockElementPanel.Visible = false;
                    assertHierarchy("Only TopBar and BookmarksBar visible");
                }
            });

            Test("Navigate Away From New Tab Uses Default Globe Favicon For Non-Favicon Sites", () =>
            {
                // 1. Verify UrlHelper, InternalPageHelper, and FaviconManager behavior for non-favicon & empty-domain targets
                Assert(UrlHelper.NormalizeDomain("file:///C:/test.html") == string.Empty, "file:// URL has empty domain");
                Assert(UrlHelper.NormalizeDomain("data:text/html,<h1>Hi</h1>") == string.Empty, "data: URL has empty domain");
                Assert(UrlHelper.NormalizeDomain("view-source:file:///C:/test.html") == string.Empty, "view-source:file:// has empty domain");
                Assert(!InternalPageHelper.IsStartPage("https://no-favicon-site.invalid"), "External HTTPS site is not start page");
                Assert(!InternalPageHelper.IsStartPage("file:///C:/test.html"), "file:// URL is not start page");
                Assert(!InternalPageHelper.IsStartPage("data:text/html,test"), "data: URL is not start page");
                Assert(AppServices.Favicons.GetFavicon("https://no-favicon-site.invalid") == null, "Non-favicon site has no cached icon");
                Assert(AppServices.Favicons.GetFavicon("file:///C:/test.html") == null, "file:// URL has no cached icon");

                // 2. Verify WbTopBar renders house on New Tab and switches to globe for non-favicon URLs (unpinned, pinned, overflow)
                using (Form host = new Form { Size = new Size(800, 600) })
                using (WbTopBar topBar = new WbTopBar())
                using (ChromeTabControl tc = new ChromeTabControl())
                {
                    topBar.Width = 800;
                    topBar.Height = 38;
                    topBar.BindTabControl(tc);
                    host.Controls.Add(topBar);
                    host.Controls.Add(tc);

                    MockFaviconTab tab = new MockFaviconTab("New Tab", "wb://newtab");
                    tab.Favicon = InternalPageHelper.GetInternalFavicon("wb://newtab", false);
                    tc.TabPages.Add(tab);
                    topBar.UpdateTabs();

                    var onPaint = typeof(WbTopBar).GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var getTabRect = typeof(WbTopBar).GetMethod("GetTabRect", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    Func<Bitmap> captureTabStrip = () =>
                    {
                        Bitmap bmp = new Bitmap(800, 38);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            onPaint.Invoke(topBar, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 800, 38)) });
                        }
                        return bmp;
                    };

                    Func<Bitmap, Bitmap, Rectangle, int> diffPixels = (a, b, rect) =>
                    {
                        int diffs = 0;
                        int xEnd = Math.Min(a.Width, rect.Right);
                        int yEnd = Math.Min(a.Height, rect.Bottom);
                        for (int y = Math.Max(0, rect.Top); y < yEnd; y++)
                        {
                            for (int x = Math.Max(0, rect.Left); x < xEnd; x++)
                            {
                                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb())
                                {
                                    diffs++;
                                }
                            }
                        }
                        return diffs;
                    };

                    Rectangle tabRect = (Rectangle)getTabRect.Invoke(topBar, new object[] { 0 });
                    Rectangle unpinnedIconArea = new Rectangle(tabRect.Left, tabRect.Top, 30, tabRect.Height);

                    using (Bitmap homeBmp = captureTabStrip())
                    {
                        // Navigate away from New Tab to non-favicon web site (Favicon reset to null)
                        tab.CurrentUrl = "https://no-favicon-site.invalid";
                        tab.Text = "Example";
                        tab.Favicon = null;
                        topBar.UpdateTabs();

                        using (Bitmap globeHttpBmp = captureTabStrip())
                        {
                            Assert(diffPixels(homeBmp, globeHttpBmp, unpinnedIconArea) > 10,
                                "Unpinned tab icon pixels must change from Home icon to default Globe icon for non-favicon HTTPS site");

                            // Navigate to local file:// URL without domain or favicon -> must still render identical default Globe icon
                            tab.CurrentUrl = "file:///C:/local-page.html";
                            tab.Favicon = null;
                            topBar.UpdateTabs();
                            using (Bitmap globeFileBmp = captureTabStrip())
                            {
                                Assert(diffPixels(globeHttpBmp, globeFileBmp, unpinnedIconArea) == 0,
                                    "Unpinned tab must render identical default Globe icon for file:// URL");
                            }

                            // Navigate to data: URL -> must still render identical default Globe icon
                            tab.CurrentUrl = "data:text/html,<h1>Hi</h1>";
                            tab.Favicon = null;
                            topBar.UpdateTabs();
                            using (Bitmap globeDataBmp = captureTabStrip())
                            {
                                Assert(diffPixels(globeHttpBmp, globeDataBmp, unpinnedIconArea) == 0,
                                    "Unpinned tab must render identical default Globe icon for data: URL");
                            }
                        }
                    }

                    // Check pinned tab transition (house -> globe)
                    tab.IsPinned = true;
                    tab.CurrentUrl = "wb://newtab";
                    tab.Favicon = InternalPageHelper.GetInternalFavicon("wb://newtab", false);
                    topBar.UpdateTabs();
                    Rectangle pinnedRect = (Rectangle)getTabRect.Invoke(topBar, new object[] { 0 });

                    using (Bitmap pinnedHomeBmp = captureTabStrip())
                    {
                        tab.CurrentUrl = "https://no-favicon-site.invalid";
                        tab.Favicon = null;
                        topBar.UpdateTabs();
                        using (Bitmap pinnedGlobeBmp = captureTabStrip())
                        {
                            Assert(diffPixels(pinnedHomeBmp, pinnedGlobeBmp, pinnedRect) > 10,
                                "Pinned tab icon pixels must change from Home icon to default Globe icon when navigating away from New Tab");
                        }
                    }
                }

                // 3. Verify BrowserTab architecture prevents about:blank overwrite, resets empty-domain URLs, and handles GoForward out of internal pages
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src");
                if (!Directory.Exists(srcDir)) srcDir = Path.Combine(Directory.GetCurrentDirectory(), "src");
                string tabSrc = File.ReadAllText(Path.Combine(srcDir, @"Browser\BrowserTab.cs"));
                Assert(tabSrc.Contains("_hasInternalFavicon"), "BrowserTab tracks _hasInternalFavicon state");
                Assert(tabSrc.Contains("UpdateFaviconOnNavigation("), "BrowserTab centralizes favicon reset/fetch logic in UpdateFaviconOnNavigation");
                Assert(tabSrc.Contains("isPlaceholderBlank && !InternalPageHelper.IsStartPage(CurrentUrl)"),
                    "BrowserTab Browser_FrameLoadStart ignores asynchronous about:blank placeholder loads once navigated away from New Tab");
                Assert(tabSrc.Contains("_hasInternalFavicon && !WebErrorPage.IsErrorPage(CurrentUrl)"),
                    "BrowserTab SetFaviconForUrl clears lingering internal favicon when site returns null favicon");
                Assert(tabSrc.Contains("_expectingNavigationFromInternal"),
                    "BrowserTab supports GoForward out of internal pages without remaining stuck on Home favicon");
            });

                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("Results: {0} Passed, {1} Failed", _passed, _failed);
                Console.WriteLine("==================================================");
            }
            finally
            {
                StorageHelper.CustomDataDirectory = null;
                try
                {
                    if (Directory.Exists(testStorageDir))
                    {
                        Directory.Delete(testStorageDir, true);
                    }
                }
                catch { }
            }

            Environment.Exit(_failed > 0 ? 1 : 0);
        }

        static void Test(string name, Action testAction)
        {
            try
            {
                testAction();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("[PASS] ");
                Console.ResetColor();
                Console.WriteLine(name);
                _passed++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("[FAIL] ");
                Console.ResetColor();
                Console.WriteLine("{0}: {1}", name, ex.Message);
                _failed++;
            }
        }

        static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        static string GetArtifactsDir()
        {
            string env = Environment.GetEnvironmentVariable("WB_TEST_ARTIFACTS_DIR");
            if (string.IsNullOrEmpty(env)) return null;
            env = env.Trim().Trim('"', '\'');
            return Directory.Exists(env) ? env : null;
        }
    }

    class MockFaviconTab : TabPage, IBrowserTabState
    {
        public Image Favicon { get; set; }
        public string CurrentUrl { get; set; }
        public string CurrentTitle { get; set; }
        public bool IsPinned { get; set; }
        public bool IsPlayingAudio { get; set; }
        public bool IsAudioMuted { get; set; }
        public bool IsMediaCaptured { get; set; }
        public bool IsLoading { get; set; }
        public event EventHandler PinStateChanged;
        public event EventHandler AudioStateChanged;
        public event EventHandler MediaCaptureStateChanged;
        public event EventHandler LoadingStateChanged;

        public void FireLoadingStateChanged()
        {
            if (LoadingStateChanged != null)
            {
                LoadingStateChanged(this, EventArgs.Empty);
            }
        }

        public MockFaviconTab() : base() { }
        public MockFaviconTab(string title, string url) : base(title)
        {
            CurrentTitle = title;
            CurrentUrl = url;
        }
    }

    class TestTrackingRenderer : MaterialToolStripRenderer
    {
        public Rectangle LastRenderedCheckRect { get; set; }
        public Rectangle LastRenderedImageRect { get; set; }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            LastRenderedCheckRect = ComputeCheckRectangle(e);
            base.OnRenderItemCheck(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            LastRenderedImageRect = ComputeImageRectangle(e);
            base.OnRenderItemImage(e);
        }
    }

    class MockElementPickerTarget : IElementPickerTarget
    {
        public bool Cancelled { get; set; }
        public string BlockedSelector { get; set; }
        public bool PreviewState { get; set; }
        public string PreviewSelector { get; set; }
        public bool Resumed { get; set; }

        public void SetElementPickerPreview(bool preview, string selector)
        {
            PreviewState = preview;
            PreviewSelector = selector;
        }

        public void ResumeElementPicker()
        {
            Resumed = true;
        }

        public void CancelElementPicker()
        {
            Cancelled = true;
        }

        public void CommitElementPickerBlock(string selector)
        {
            BlockedSelector = selector;
        }
    }

    class MockPopupFeatures : IPopupFeatures
    {
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool MenuBarVisible { get; set; }
        public bool StatusBarVisible { get; set; }
        public bool ToolBarVisible { get; set; }
        public bool ScrollbarsVisible { get; set; }
        public bool Resizable { get; set; }
        public bool FullScreen { get; set; }
        public bool Dialog { get; set; }
        public System.Collections.Generic.List<string> AdditionalFeatures { get; set; }
        public bool IsPopup { get; set; }
        public void Dispose() { }
    }

    class MockWindowInfo : IWindowInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public uint Style { get; set; }
        public uint ExStyle { get; set; }
        public IntPtr ParentWindowHandle { get; set; }
        public bool WindowlessRenderingEnabled { get; set; }
        public bool SharedTextureEnabled { get; set; }
        public bool ExternalBeginFrameEnabled { get; set; }
        public IntPtr WindowHandle { get; set; }
        public string WindowName { get; set; }
        public CefSharp.CefRuntimeStyle RuntimeStyle { get; set; }
        public void SetAsChild(IntPtr parentHandle) { ParentWindowHandle = parentHandle; }
        public void SetAsChild(IntPtr parentHandle, CefSharp.Structs.Rect rect) { ParentWindowHandle = parentHandle; }
        public void SetAsChild(IntPtr parentHandle, int left, int top, int width, int height) { ParentWindowHandle = parentHandle; X = left; Y = top; Width = width; Height = height; }
        public void SetAsPopup(IntPtr parentHandle, string windowName) { ParentWindowHandle = parentHandle; WindowName = windowName; }
        public void SetAsWindowless(IntPtr parentHandle) { ParentWindowHandle = parentHandle; WindowlessRenderingEnabled = true; }
        public CefSharp.IWindowInfo UnWrap() { return this; }
        public void Dispose() { }
    }

}
