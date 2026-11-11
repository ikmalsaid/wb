using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.DevTools;
using CefSharp.DevTools.Page;
using CefSharp.WinForms;

namespace wb.Screenshot
{
    public class FullPageScreenshotResult
    {
        public Bitmap Bitmap { get; set; }
        public Point InitialScrollOffset { get; set; }
    }

    public static class CdpScreenshotHelper
    {
        public static async Task<Bitmap> CaptureVisibleAsync(ChromiumWebBrowser browserControl, int timeoutMs = 5000, bool trimGutters = true)
        {
            if (browserControl == null) return null;

            if (browserControl.IsBrowserInitialized)
            {
                IBrowser browser = browserControl.GetBrowser();
                if (browser != null)
                {
                    try
                    {
                        using (DevToolsClient client = browser.GetDevToolsClient())
                        {
                            try
                            {
                                await client.Page.EnableAsync();
                            }
                            catch { }

                            // 1. Measure viewport and scrollbar dimensions before capture
                            int innerWidth = 0;
                            int innerHeight = 0;
                            int vScrollWidth = 0;
                            int hScrollHeight = 0;

                            try
                            {
                                string measureJs = @"(function() {
                                    var doc = document.documentElement;
                                    var body = document.body;
                                    var iw = window.innerWidth || (doc ? doc.clientWidth : 800);
                                    var ih = window.innerHeight || (doc ? doc.clientHeight : 600);
                                    var cw = (doc && doc.clientWidth > 0) ? doc.clientWidth : (body && body.clientWidth > 0 ? body.clientWidth : iw);
                                    var ch = (doc && doc.clientHeight > 0) ? doc.clientHeight : (body && body.clientHeight > 0 ? body.clientHeight : ih);
                                    return {
                                        iw: Math.round(iw),
                                        ih: Math.round(ih),
                                        vScroll: Math.max(0, Math.round(iw - cw)),
                                        hScroll: Math.max(0, Math.round(ih - ch))
                                    };
                                })()";

                                JavascriptResponse resp = await browserControl.EvaluateScriptAsync(measureJs);
                                if (resp != null && resp.Success && resp.Result is IDictionary<string, object>)
                                {
                                    IDictionary<string, object> dict = (IDictionary<string, object>)resp.Result;
                                    object val;
                                    int intVal;
                                    if (dict.TryGetValue("iw", out val) && int.TryParse(val.ToString(), out intVal)) innerWidth = intVal;
                                    if (dict.TryGetValue("ih", out val) && int.TryParse(val.ToString(), out intVal)) innerHeight = intVal;
                                    if (dict.TryGetValue("vScroll", out val) && int.TryParse(val.ToString(), out intVal)) vScrollWidth = intVal;
                                    if (dict.TryGetValue("hScroll", out val) && int.TryParse(val.ToString(), out intVal)) hScrollHeight = intVal;
                                }
                            }
                            catch { }

                            // 2. Capture exact visible viewport directly from surface (clip: null ensures natural viewport dimensions)
                            Bitmap bmp = await CaptureDevToolsTileAsync(client, timeoutMs);

                            if (bmp != null)
                            {
                                // 3. If scrollbars exist on the page and trimGutters is enabled, crop them precisely from the image edge
                                if (trimGutters && (vScrollWidth > 0 || hScrollHeight > 0))
                                {
                                    try
                                    {
                                        double scaleX = (innerWidth > 0) ? ((double)bmp.Width / innerWidth) : 1.0;
                                        double scaleY = (innerHeight > 0) ? ((double)bmp.Height / innerHeight) : 1.0;

                                        int cropW = bmp.Width;
                                        int cropH = bmp.Height;

                                        if (vScrollWidth > 0)
                                        {
                                            int scrollWPhysical = (int)Math.Round(vScrollWidth * scaleX);
                                            cropW = Math.Max(1, bmp.Width - scrollWPhysical);
                                        }

                                        if (hScrollHeight > 0)
                                        {
                                            int scrollHPhysical = (int)Math.Round(hScrollHeight * scaleY);
                                            cropH = Math.Max(1, bmp.Height - scrollHPhysical);
                                        }

                                        if (cropW < bmp.Width || cropH < bmp.Height)
                                        {
                                            Rectangle cropRect = new Rectangle(0, 0, cropW, cropH);
                                            Bitmap cropped = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
                                            using (Graphics g = Graphics.FromImage(cropped))
                                            {
                                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                                g.SmoothingMode = SmoothingMode.None;
                                                g.DrawImage(bmp, new Rectangle(0, 0, cropW, cropH), cropRect, GraphicsUnit.Pixel);
                                            }
                                            bmp.Dispose();
                                            bmp = cropped;
                                        }
                                    }
                                    catch { }
                                }

                                if (trimGutters)
                                {
                                    bmp = TrimOuterBackgroundGutter(bmp);
                                }
                                return bmp;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            Bitmap fallback = CaptureControlScreen(browserControl);
            return trimGutters ? TrimOuterBackgroundGutter(fallback) : fallback;
        }

        public static async Task<Bitmap> CaptureFullPageAsync(ChromiumWebBrowser browserControl, int timeoutMs = 30000)
        {
            FullPageScreenshotResult res = await CaptureFullPageWithOffsetAsync(browserControl, timeoutMs);
            return (res != null) ? res.Bitmap : null;
        }

        public static async Task<FullPageScreenshotResult> CaptureFullPageWithOffsetAsync(
            ChromiumWebBrowser browserControl,
            int timeoutMs = 30000,
            bool hideScrollbars = true,
            bool trimGutters = true)
        {
            if (browserControl == null) return null;

            if (!browserControl.IsBrowserInitialized)
            {
                return new FullPageScreenshotResult { Bitmap = CaptureControlScreen(browserControl), InitialScrollOffset = Point.Empty };
            }

            IBrowser browser = browserControl.GetBrowser();
            if (browser == null)
            {
                return new FullPageScreenshotResult { Bitmap = CaptureControlScreen(browserControl), InitialScrollOffset = Point.Empty };
            }

            using (DevToolsClient client = browser.GetDevToolsClient())
            {
                try
                {
                    await client.Page.EnableAsync();
                }
                catch { }

                int origX = 0;
                int origY = 0;
                int totalWidth = 1280;
                int totalHeight = 800;
                int viewportWidth = 1280;
                int viewportHeight = 800;
                double scale = 1.0;

                Bitmap result = null;

                try
                {
                    // 1. Measure dimensions and conditionally hide scrollbars during capture
                    string initJs = string.Format(
                        CultureInfo.InvariantCulture,
                        @"(function() {{
                        var doc = document.documentElement;
                        var body = document.body;
                        var w = Math.max(
                            doc ? doc.scrollWidth : 0,
                            doc ? doc.clientWidth : 0,
                            body ? body.scrollWidth : 0,
                            body ? body.clientWidth : 0
                        );
                        if (w <= 0) {{
                            w = doc ? doc.offsetWidth : (window.innerWidth || 800);
                        }}
                        var h = Math.max(
                            doc ? doc.scrollHeight : 0,
                            doc ? doc.offsetHeight : 0,
                            doc ? doc.clientHeight : 0,
                            body ? body.scrollHeight : 0,
                            body ? body.offsetHeight : 0,
                            window.innerHeight || 0
                        );
                        var vw = (doc && doc.clientWidth > 0) ? doc.clientWidth : (window.innerWidth || 800);
                        var vh = (doc && doc.clientHeight > 0) ? doc.clientHeight : (window.innerHeight || 600);
                        var sx = window.pageXOffset || window.scrollX || (doc ? doc.scrollLeft : 0);
                        var sy = window.pageYOffset || window.scrollY || (doc ? doc.scrollTop : 0);

                        if ({0}) {{
                            var style = document.getElementById('__wb_ss_style');
                            if (!style) {{
                                style = document.createElement('style');
                                style.id = '__wb_ss_style';
                                style.textContent = '::-webkit-scrollbar {{ display: none !important; width: 0 !important; height: 0 !important; }}';
                                (document.head || document.documentElement).appendChild(style);
                            }}
                        }}

                        return {{
                            totalWidth: Math.max(100, Math.round(w)),
                            totalHeight: Math.max(100, Math.round(h)),
                            viewportWidth: Math.max(100, Math.round(vw)),
                            viewportHeight: Math.max(100, Math.round(vh)),
                            scrollX: Math.round(sx),
                            scrollY: Math.round(sy)
                        }};
                    }})()",
                        hideScrollbars ? "true" : "false"
                    );

                    try
                    {
                        JavascriptResponse initResp = await browserControl.EvaluateScriptAsync(initJs);
                        if (initResp != null && initResp.Success && initResp.Result is IDictionary<string, object>)
                        {
                            IDictionary<string, object> dict = (IDictionary<string, object>)initResp.Result;
                            object val;
                            int intVal;
                            if (dict.TryGetValue("totalWidth", out val) && val != null && int.TryParse(val.ToString(), out intVal) && intVal > 0)
                                totalWidth = intVal;
                            if (dict.TryGetValue("totalHeight", out val) && val != null && int.TryParse(val.ToString(), out intVal) && intVal > 0)
                                totalHeight = intVal;
                            if (dict.TryGetValue("viewportWidth", out val) && val != null && int.TryParse(val.ToString(), out intVal) && intVal > 0)
                                viewportWidth = intVal;
                            if (dict.TryGetValue("viewportHeight", out val) && val != null && int.TryParse(val.ToString(), out intVal) && intVal > 0)
                                viewportHeight = intVal;
                            if (dict.TryGetValue("scrollX", out val) && val != null && int.TryParse(val.ToString(), out intVal))
                                origX = intVal;
                            if (dict.TryGetValue("scrollY", out val) && val != null && int.TryParse(val.ToString(), out intVal))
                                origY = intVal;
                        }
                    }
                    catch { }

                    if (hideScrollbars)
                    {
                        // Allow compositor to refresh frame without scrollbars
                        await Task.Delay(50);
                    }

                    // Clamp total height to prevent excessive memory allocation
                    int maxAllowedHeightCss = 25000;
                    if (totalHeight > maxAllowedHeightCss) totalHeight = maxAllowedHeightCss;

                    // Try single-shot captureBeyondViewport first (fastest, covers full document including width and height)
                    try
                    {
                        Viewport clip = new Viewport
                        {
                            X = 0,
                            Y = 0,
                            Width = totalWidth,
                            Height = totalHeight,
                            Scale = 1.0
                        };
                        Bitmap directFull = await CaptureDevToolsTileAsync(client, 4000, clip, true);
                        if (directFull != null && directFull.Width > 0 && directFull.Height > 0)
                        {
                            scale = (viewportWidth > 0) ? ((double)directFull.Width / viewportWidth) : 1.0;
                            result = directFull;
                        }
                    }
                    catch { }

                    if (result == null)
                    {
                        // 2. Scroll to top (0, 0)
                        await browserControl.EvaluateScriptAsync("window.scrollTo(0, 0);");
                        await Task.Delay(140);

                        // 3. Capture first slice at top directly from Chromium compositor canvas
                        Bitmap tile0 = await CaptureDevToolsTileAsync(client, 4000);
                        if (tile0 == null)
                        {
                            result = CaptureControlScreen(browserControl);
                        }
                        else
                        {
                            // Determine actual device scaling from physical tile size vs CSS viewport
                            scale = (viewportHeight > 0) ? ((double)tile0.Height / viewportHeight) : 1.0;
                            int tileWidth = tile0.Width;
                            int tileHeight = tile0.Height;

                            // If the entire page fits within the initial viewport, return tile0 directly
                            if (totalHeight <= viewportHeight + 10)
                            {
                                result = tile0;
                            }
                            else
                            {
                                int totalHeightPhysical = Math.Max(tileHeight, (int)Math.Round(totalHeight * scale));
                                int totalWidthPhysical = tileWidth;

                                Bitmap composite = new Bitmap(totalWidthPhysical, totalHeightPhysical, PixelFormat.Format32bppArgb);
                                Graphics g = Graphics.FromImage(composite);
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.SmoothingMode = SmoothingMode.None;

                                try
                                {
                                    // Draw tile0 at top
                                    g.DrawImage(tile0, 0, 0, tile0.Width, tile0.Height);
                                    tile0.Dispose();
                                    tile0 = null;

                                    // 4. Temporarily hide floating/sticky headers so they don't repeat across subsequent slices
                                    string hideFixedJs = @"(function() {
                                        try {
                                            var all = document.querySelectorAll('*');
                                            for (var i = 0; i < all.length; i++) {
                                                var el = all[i];
                                                var pos = window.getComputedStyle(el).position;
                                                if (pos === 'fixed' || pos === 'sticky') {
                                                    var r = el.getBoundingClientRect();
                                                    if (r.height < window.innerHeight * 0.4 && (r.top <= 120 || r.bottom >= window.innerHeight - 80)) {
                                                        if (!el.hasAttribute('data-wb-prev-vis')) {
                                                            el.setAttribute('data-wb-prev-vis', el.style.visibility || 'visible');
                                                            el.style.visibility = 'hidden';
                                                        }
                                                    }
                                                }
                                            }
                                        } catch(e) {}
                                    })()";
                                    try
                                    {
                                        await browserControl.EvaluateScriptAsync(hideFixedJs);
                                    }
                                    catch { }

                                    // 5. Progressively scroll from top to bottom forming a long screenshot
                                    int currentYCss = viewportHeight;
                                    int maxTiles = 40;
                                    int tileCount = 1;

                                    while (currentYCss < totalHeight && tileCount < maxTiles)
                                    {
                                        int remainingCss = totalHeight - currentYCss;
                                        bool isLastSlice = (remainingCss <= viewportHeight);

                                        int scrollTargetYCss = isLastSlice ? (totalHeight - viewportHeight) : currentYCss;
                                        if (scrollTargetYCss < 0) scrollTargetYCss = 0;

                                        // Scroll the browser window
                                        await browserControl.EvaluateScriptAsync(
                                            string.Format(CultureInfo.InvariantCulture, "window.scrollTo(0, {0});", scrollTargetYCss)
                                        );
                                        // Allow browser to render tiles and load lazy content
                                        await Task.Delay(130);

                                        // Dynamically re-check scrollHeight in case lazy-loaded elements expanded the page
                                        try
                                        {
                                            string recheckJs = @"(function() {
                                                var doc = document.documentElement;
                                                var body = document.body;
                                                return Math.max(doc ? doc.scrollHeight : 0, doc ? doc.offsetHeight : 0, body ? body.scrollHeight : 0, window.innerHeight || 0);
                                            })()";
                                            JavascriptResponse checkResp = await browserControl.EvaluateScriptAsync(recheckJs);
                                            if (checkResp != null && checkResp.Success && checkResp.Result != null)
                                            {
                                                int freshH;
                                                if (int.TryParse(checkResp.Result.ToString(), out freshH) && freshH > totalHeight && freshH <= maxAllowedHeightCss)
                                                {
                                                    totalHeight = freshH;
                                                    int newPhysicalH = (int)Math.Round(totalHeight * scale);
                                                    if (newPhysicalH > composite.Height)
                                                    {
                                                        Bitmap expanded = new Bitmap(totalWidthPhysical, newPhysicalH, PixelFormat.Format32bppArgb);
                                                        using (Graphics eg = Graphics.FromImage(expanded))
                                                        {
                                                            eg.DrawImage(composite, 0, 0);
                                                        }
                                                        g.Dispose();
                                                        composite.Dispose();
                                                        composite = expanded;
                                                        g = Graphics.FromImage(composite);
                                                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                                        g.SmoothingMode = SmoothingMode.None;

                                                        remainingCss = totalHeight - currentYCss;
                                                        isLastSlice = (remainingCss <= viewportHeight);
                                                    }
                                                }
                                            }
                                        }
                                        catch { }

                                        Bitmap tile = await CaptureDevToolsTileAsync(client, 4000);
                                        if (tile == null)
                                        {
                                            await Task.Delay(150);
                                            tile = await CaptureDevToolsTileAsync(client, 4000);
                                        }

                                        if (tile != null)
                                        {
                                            using (tile)
                                            {
                                                if (isLastSlice)
                                                {
                                                    int sliceHPhysical = (int)Math.Round(remainingCss * scale);
                                                    int srcY = tile.Height - sliceHPhysical;
                                                    if (srcY < 0) srcY = 0;
                                                    int srcH = Math.Min(sliceHPhysical, tile.Height - srcY);

                                                    int destY = (int)Math.Round(currentYCss * scale);
                                                    int destH = composite.Height - destY;

                                                    if (destH > 0 && srcH > 0)
                                                    {
                                                        Rectangle srcRect = new Rectangle(0, srcY, tile.Width, srcH);
                                                        Rectangle destRect = new Rectangle(0, destY, tile.Width, destH);
                                                        g.DrawImage(tile, destRect, srcRect, GraphicsUnit.Pixel);
                                                    }
                                                    break;
                                                }
                                                else
                                                {
                                                    int destY = (int)Math.Round(currentYCss * scale);
                                                    int nextYCss = currentYCss + viewportHeight;
                                                    int destH = (int)Math.Round(nextYCss * scale) - destY;

                                                    Rectangle srcRect = new Rectangle(0, 0, tile.Width, tile.Height);
                                                    Rectangle destRect = new Rectangle(0, destY, tile.Width, destH);
                                                    g.DrawImage(tile, destRect, srcRect, GraphicsUnit.Pixel);
                                                }
                                            }
                                        }

                                        currentYCss += viewportHeight;
                                        tileCount++;
                                    }

                                    result = composite;
                                }
                                finally
                                {
                                    if (tile0 != null) tile0.Dispose();
                                    if (g != null) g.Dispose();
                                }
                            }
                        }
                    }
                }
                catch
                {
                    result = CaptureControlScreen(browserControl);
                }

                // 6. Restore page state (outside finally block to comply with C# 5)
                try
                {
                    string cleanupJs = string.Format(
                        CultureInfo.InvariantCulture,
                        @"(function() {{
                            try {{
                                var style = document.getElementById('__wb_ss_style');
                                if (style && style.parentNode) {{
                                    style.parentNode.removeChild(style);
                                }}
                                var elements = document.querySelectorAll('[data-wb-prev-vis]');
                                for (var i = 0; i < elements.length; i++) {{
                                    var el = elements[i];
                                    var prev = el.getAttribute('data-wb-prev-vis');
                                    el.style.visibility = (prev === 'visible') ? '' : prev;
                                    el.removeAttribute('data-wb-prev-vis');
                                }}
                                void document.documentElement.offsetHeight;
                                window.scrollTo({0}, {1});
                            }} catch(e) {{}}
                        }})()",
                        origX, origY
                    );

                    await browserControl.EvaluateScriptAsync(cleanupJs);
                }
                catch { }

                Bitmap finalBmp = result ?? CaptureControlScreen(browserControl);
                if (trimGutters)
                {
                    finalBmp = TrimOuterBackgroundGutter(finalBmp);
                }
                return new FullPageScreenshotResult
                {
                    Bitmap = finalBmp,
                    InitialScrollOffset = new Point((int)Math.Round(origX * scale), (int)Math.Round(origY * scale))
                };
            }
        }

        private static bool IsBgPixel(byte b, byte g, byte r)
        {
            if (Math.Abs(r - 32) <= 8 && Math.Abs(g - 33) <= 8 && Math.Abs(b - 36) <= 8) return true;
            if (Math.Abs(r - 20) <= 6 && Math.Abs(g - 21) <= 6 && Math.Abs(b - 24) <= 6) return true;
            if (r <= 6 && g <= 6 && b <= 6) return true;
            return false;
        }

        public static Bitmap TrimOuterBackgroundGutter(Bitmap bmp)
        {
            if (bmp == null || bmp.Width < 100 || bmp.Height < 100) return bmp;

            int w = bmp.Width;
            int h = bmp.Height;

            BitmapData data = null;
            byte[] buffer = null;
            int stride = 0;

            try
            {
                data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(data.Stride) * h;
                buffer = new byte[bytes];
                Marshal.Copy(data.Scan0, buffer, 0, bytes);
                stride = data.Stride;
            }
            catch
            {
                return bmp;
            }
            finally
            {
                if (data != null)
                {
                    bmp.UnlockBits(data);
                }
            }

            int leftTrim = 0;
            int rightTrim = w - 1;
            int bottomTrim = h - 1;

            // 1. Scan left columns
            int maxScanW = w / 3;
            for (int x = 0; x < maxScanW; x++)
            {
                bool colIsBg = true;
                for (int y = 0; y < h; y += 2)
                {
                    int idx = (y * stride) + (x * 4);
                    if (!IsBgPixel(buffer[idx], buffer[idx + 1], buffer[idx + 2]))
                    {
                        colIsBg = false;
                        break;
                    }
                }
                if (colIsBg) leftTrim = x + 1;
                else break;
            }

            // 2. Scan right columns
            int minScanW = w * 2 / 3;
            for (int x = w - 1; x >= minScanW; x--)
            {
                bool colIsBg = true;
                for (int y = 0; y < h; y += 2)
                {
                    int idx = (y * stride) + (x * 4);
                    if (!IsBgPixel(buffer[idx], buffer[idx + 1], buffer[idx + 2]))
                    {
                        colIsBg = false;
                        break;
                    }
                }
                if (colIsBg) rightTrim = x - 1;
                else break;
            }

            // 3. Scan bottom rows
            int minScanH = h / 3;
            for (int y = h - 1; y >= minScanH; y--)
            {
                bool rowIsBg = true;
                for (int x = leftTrim; x <= rightTrim; x += 4)
                {
                    int idx = (y * stride) + (x * 4);
                    if (!IsBgPixel(buffer[idx], buffer[idx + 1], buffer[idx + 2]))
                    {
                        rowIsBg = false;
                        break;
                    }
                }
                if (rowIsBg) bottomTrim = y - 1;
                else break;
            }

            int cropW = rightTrim - leftTrim + 1;
            int cropH = bottomTrim + 1;
            if ((leftTrim >= 4 || (w - 1 - rightTrim) >= 4 || (h - 1 - bottomTrim) >= 4) && cropW >= 100 && cropH >= 100)
            {
                try
                {
                    Bitmap cropped = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(cropped))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.None;
                        g.DrawImage(bmp, new Rectangle(0, 0, cropW, cropH), new Rectangle(leftTrim, 0, cropW, cropH), GraphicsUnit.Pixel);
                    }
                    bmp.Dispose();
                    return cropped;
                }
                catch
                {
                    return bmp;
                }
            }

            return bmp;
        }

        private static async Task<Bitmap> CaptureDevToolsTileAsync(DevToolsClient client, int timeoutMs, Viewport clip = null, bool? captureBeyondViewport = null)
        {
            if (client == null) return null;

            try
            {
                Task<CaptureScreenshotResponse> task = client.Page.CaptureScreenshotAsync(
                    format: CaptureScreenshotFormat.Png,
                    quality: null,
                    clip: clip,
                    fromSurface: true,
                    captureBeyondViewport: captureBeyondViewport,
                    optimizeForSpeed: null
                );

                Task completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
                if (completed == task)
                {
                    CaptureScreenshotResponse resp = await task;
                    if (resp != null && resp.Data != null && resp.Data.Length > 0)
                    {
                        using (MemoryStream ms = new MemoryStream(resp.Data))
                        using (Image img = Image.FromStream(ms))
                        {
                            Bitmap copy = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                            using (Graphics g = Graphics.FromImage(copy))
                            {
                                g.DrawImage(img, 0, 0, img.Width, img.Height);
                            }
                            return copy;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public static Bitmap CaptureControlScreen(Control control)
        {
            if (control == null || control.IsDisposed) return null;

            try
            {
                Rectangle bounds = Rectangle.Empty;
                if (control.InvokeRequired)
                {
                    control.Invoke(new Action(delegate()
                    {
                        if (!control.IsDisposed)
                        {
                            bounds = control.RectangleToScreen(control.ClientRectangle);
                        }
                    }));
                }
                else
                {
                    bounds = control.RectangleToScreen(control.ClientRectangle);
                }

                if (bounds.Width <= 0 || bounds.Height <= 0) return null;

                Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public static Bitmap ParseBitmapFromCdpJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                int marker = json.IndexOf("\"data\":\"", StringComparison.Ordinal);
                if (marker < 0)
                {
                    marker = json.IndexOf("\"data\": \"", StringComparison.Ordinal);
                    if (marker >= 0) marker += 9;
                }
                else
                {
                    marker += 8;
                }

                if (marker > 0)
                {
                    int end = json.IndexOf('"', marker);
                    if (end > marker)
                    {
                        string base64 = json.Substring(marker, end - marker);
                        byte[] bytes = Convert.FromBase64String(base64);
                        using (MemoryStream ms = new MemoryStream(bytes))
                        using (Image img = Image.FromStream(ms))
                        {
                            Bitmap copy = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                            using (Graphics g = Graphics.FromImage(copy))
                            {
                                g.DrawImage(img, 0, 0, img.Width, img.Height);
                            }
                            return copy;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
