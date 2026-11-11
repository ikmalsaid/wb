using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using wb.Browser;
using wb.UI;

namespace wb.Screenshot
{
    public enum ScreenshotMode
    {
        VisiblePart,
        FullPage,
        SelectedArea
    }

    public static class ScreenshotController
    {
        private static ScreenshotSelectionOverlayForm _activeSelectionOverlay;
        private static ScreenshotCountdownOverlay _activeCountdownOverlay;

        public static void CancelActiveSelection()
        {
            if (_activeCountdownOverlay != null)
            {
                try { _activeCountdownOverlay.Cancel(); } catch { }
                _activeCountdownOverlay = null;
            }

            if (_activeSelectionOverlay != null && !_activeSelectionOverlay.IsDisposed)
            {
                try
                {
                    _activeSelectionOverlay.DialogResult = DialogResult.Cancel;
                    _activeSelectionOverlay.Close();
                }
                catch { }
                _activeSelectionOverlay = null;
            }
        }

        public static void TakeScreenshot(MainBrowserForm form, ScreenshotMode mode, int delaySeconds = 0)
        {
            if (form == null || form.IsDisposed) return;

            if (delaySeconds > 0)
            {
                ScreenshotCountdownOverlay countdown = new ScreenshotCountdownOverlay(
                    delaySeconds,
                    () =>
                    {
                        _activeCountdownOverlay = null;
                        ExecuteCapture(form, mode);
                    },
                    () =>
                    {
                        _activeCountdownOverlay = null;
                    }
                );
                _activeCountdownOverlay = countdown;
                countdown.Start(form);
            }
            else
            {
                ExecuteCapture(form, mode);
            }
        }

        private static async void ExecuteCapture(MainBrowserForm form, ScreenshotMode mode)
        {
            if (form == null || form.IsDisposed) return;

            // Ensure any active overlay popups (e.g. countdown toast or notification) are dismissed
            OverlayPopup.DismissAll();

            // Temporarily hide BottomBar during screenshot capture so it never appears in viewport or fallback
            bool bottomBarWasVisible = false;
            try
            {
                if (form.BottomBar != null && form.BottomBar.Visible)
                {
                    bottomBarWasVisible = true;
                    form.BottomBar.Hide();
                }
            }
            catch { }

            Bitmap captured = null;
            BrowserTab currentTab = form.CurrentTab;
            string sourceUrl = (currentTab != null) ? currentTab.CurrentUrl : string.Empty;
            string sourceTitle = (currentTab != null) ? currentTab.CurrentTitle : "Screenshot";

            try
            {
                switch (mode)
                {
                    case ScreenshotMode.VisiblePart:
                        if (currentTab != null && currentTab.Browser != null && currentTab.Browser.Visible)
                        {
                            captured = await CdpScreenshotHelper.CaptureVisibleAsync(currentTab.Browser);
                        }
                        else if (currentTab != null)
                        {
                            captured = CdpScreenshotHelper.CaptureControlScreen(currentTab);
                        }
                        break;

                    case ScreenshotMode.FullPage:
                        if (currentTab != null && currentTab.Browser != null && currentTab.Browser.Visible)
                        {
                            captured = await CdpScreenshotHelper.CaptureFullPageAsync(currentTab.Browser);
                        }
                        else if (currentTab != null)
                        {
                            captured = CdpScreenshotHelper.CaptureControlScreen(currentTab);
                        }
                        break;

                    case ScreenshotMode.SelectedArea:
                        Rectangle targetBounds = Rectangle.Empty;
                        Bitmap fullDocBitmap = null;
                        Point initialScrollOffset = Point.Empty;

                        if (currentTab != null)
                        {
                            Control canvas = (currentTab.Browser != null && currentTab.Browser.Visible)
                                ? (Control)currentTab.Browser
                                : (Control)currentTab;
                            if (canvas != null && !canvas.IsDisposed && canvas.Width > 0 && canvas.Height > 0)
                            {
                                Point pt = canvas.PointToScreen(Point.Empty);
                                targetBounds = new Rectangle(pt, canvas.ClientSize);
                            }

                            if (currentTab.Browser != null && currentTab.Browser.Visible && currentTab.Browser.IsBrowserInitialized)
                            {
                                try
                                {
                                    // In Selected Area mode, do NOT hide scrollbars (prevents page from shifting/moving left)
                                    // and do NOT trim gutters (preserves 1:1 screen coordinate alignment with the live browser)
                                    FullPageScreenshotResult fullPage = await CdpScreenshotHelper.CaptureFullPageWithOffsetAsync(
                                        currentTab.Browser,
                                        30000,
                                        hideScrollbars: false,
                                        trimGutters: false);
                                    if (fullPage != null && fullPage.Bitmap != null)
                                    {
                                        fullDocBitmap = fullPage.Bitmap;
                                        initialScrollOffset = fullPage.InitialScrollOffset;
                                    }
                                }
                                catch { }

                                if (fullDocBitmap == null)
                                {
                                    try
                                    {
                                        fullDocBitmap = await CdpScreenshotHelper.CaptureVisibleAsync(
                                            currentTab.Browser,
                                            5000,
                                            trimGutters: false);
                                        initialScrollOffset = Point.Empty;
                                    }
                                    catch { }
                                }
                            }
                        }
                        if (targetBounds.Width <= 0 || targetBounds.Height <= 0)
                        {
                            targetBounds = form.RectangleToScreen(form.ClientRectangle);
                        }

                        TaskCompletionSource<Bitmap> tcs = new TaskCompletionSource<Bitmap>();
                        ScreenshotSelectionOverlayForm overlay = new ScreenshotSelectionOverlayForm(targetBounds, fullDocBitmap, initialScrollOffset);
                        _activeSelectionOverlay = overlay;

                        FormClosingEventHandler formClosingHandler = null;
                        formClosingHandler = delegate(object s, FormClosingEventArgs e)
                        {
                            tcs.TrySetResult(null);
                            try
                            {
                                if (!overlay.IsDisposed)
                                {
                                    overlay.Close();
                                }
                            }
                            catch { }
                        };
                        form.FormClosing += formClosingHandler;

                        overlay.FormClosed += delegate(object s, FormClosedEventArgs e)
                        {
                            if (_activeSelectionOverlay == overlay)
                            {
                                _activeSelectionOverlay = null;
                            }
                            try { form.FormClosing -= formClosingHandler; } catch { }
                            if ((overlay.IsConfirmed || overlay.DialogResult == DialogResult.OK) && overlay.SelectedBitmap != null)
                            {
                                tcs.TrySetResult(overlay.SelectedBitmap);
                            }
                            else
                            {
                                tcs.TrySetResult(null);
                            }
                            overlay.Dispose();
                        };

                        overlay.Show(form);
                        captured = await tcs.Task;
                        break;
                }
            }
            catch (Exception ex)
            {
                if (!form.IsDisposed)
                {
                    MessageBox.Show(form, "Screenshot error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }
            finally
            {
                if (bottomBarWasVisible)
                {
                    try
                    {
                        if (form.BottomBar != null && !form.BottomBar.IsDisposed)
                        {
                            form.BottomBar.Show();
                        }
                    }
                    catch { }
                }
            }

            if (captured != null && !form.IsDisposed)
            {
                ScreenshotSession session = ScreenshotSessionManager.CreateSession(captured, sourceUrl, sourceTitle);
                if (session != null)
                {
                    try { form.Activate(); } catch { }
                    form.AddNewTab("wb://screenshot?id=" + session.Id);
                }
            }
        }
    }
}
