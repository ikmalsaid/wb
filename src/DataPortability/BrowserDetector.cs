using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Web.Script.Serialization;

namespace wb.DataPortability
{
    public static class BrowserDetector
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static List<BrowserProfileInfo> DetectInstalledBrowsers()
        {
            var results = new List<BrowserProfileInfo>();

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            // 1. Google Chrome
            DetectChromiumBrowser(
                results,
                BrowserType.Chrome,
                "Google Chrome",
                Path.Combine(localAppData, @"Google\Chrome\User Data"),
                FindExistingFile(
                    Path.Combine(progFiles, @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(progFilesX86, @"Google\Chrome\Application\chrome.exe"),
                    Path.Combine(localAppData, @"Google\Chrome\Application\chrome.exe")));

            // 2. Microsoft Edge
            DetectChromiumBrowser(
                results,
                BrowserType.Edge,
                "Microsoft Edge",
                Path.Combine(localAppData, @"Microsoft\Edge\User Data"),
                FindExistingFile(
                    Path.Combine(progFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
                    Path.Combine(progFiles, @"Microsoft\Edge\Application\msedge.exe")));

            // 3. Brave Browser
            DetectChromiumBrowser(
                results,
                BrowserType.Brave,
                "Brave Browser",
                Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data"),
                FindExistingFile(
                    Path.Combine(progFiles, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                    Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\Application\brave.exe")));

            // 4. Opera
            DetectOperaBrowser(
                results,
                BrowserType.Opera,
                "Opera",
                Path.Combine(appData, @"Opera Software\Opera Stable"),
                FindExistingFile(
                    Path.Combine(localAppData, @"Programs\Opera\launcher.exe"),
                    Path.Combine(localAppData, @"Programs\Opera\opera.exe"),
                    Path.Combine(progFiles, @"Opera\launcher.exe")));

            // 5. Opera GX
            DetectOperaBrowser(
                results,
                BrowserType.OperaGX,
                "Opera GX",
                Path.Combine(appData, @"Opera Software\Opera GX Stable"),
                FindExistingFile(
                    Path.Combine(localAppData, @"Programs\Opera GX\launcher.exe"),
                    Path.Combine(localAppData, @"Programs\Opera GX\opera.exe")));

            // 6. Vivaldi
            DetectChromiumBrowser(
                results,
                BrowserType.Vivaldi,
                "Vivaldi",
                Path.Combine(localAppData, @"Vivaldi\User Data"),
                FindExistingFile(
                    Path.Combine(localAppData, @"Vivaldi\Application\vivaldi.exe"),
                    Path.Combine(progFiles, @"Vivaldi\Application\vivaldi.exe")));

            // 7. Chromium
            DetectChromiumBrowser(
                results,
                BrowserType.Chromium,
                "Chromium",
                Path.Combine(localAppData, @"Chromium\User Data"),
                FindExistingFile(
                    Path.Combine(localAppData, @"Chromium\Application\chrome.exe"),
                    Path.Combine(progFiles, @"Chromium\Application\chrome.exe")));

            // 8. Arc Browser
            DetectChromiumBrowser(
                results,
                BrowserType.Arc,
                "Arc",
                Path.Combine(localAppData, @"Arc\User Data"),
                FindExistingFile(
                    Path.Combine(localAppData, @"Arc\Arc.exe")));

            // 9. Yandex Browser
            DetectChromiumBrowser(
                results,
                BrowserType.Yandex,
                "Yandex Browser",
                Path.Combine(localAppData, @"Yandex\YandexBrowser\User Data"),
                FindExistingFile(
                    Path.Combine(localAppData, @"Yandex\YandexBrowser\Application\browser.exe")));

            // 10. Thorium Browser
            DetectChromiumBrowser(
                results,
                BrowserType.Thorium,
                "Thorium",
                Path.Combine(localAppData, @"Thorium\User Data"),
                FindExistingFile(
                    Path.Combine(progFiles, @"Thorium\Application\thorium.exe"),
                    Path.Combine(localAppData, @"Thorium\Application\thorium.exe")));

            // 11. CocCoc
            DetectChromiumBrowser(
                results,
                BrowserType.CocCoc,
                "CocCoc",
                Path.Combine(localAppData, @"CocCoc\Browser\User Data"),
                FindExistingFile(
                    Path.Combine(localAppData, @"CocCoc\Browser\Application\browser.exe")));

            // 12. CentBrowser
            DetectChromiumBrowser(
                results,
                BrowserType.CentBrowser,
                "CentBrowser",
                Path.Combine(localAppData, @"CentBrowser\User Data"),
                FindExistingFile(
                    Path.Combine(progFiles, @"CentBrowser\chrome.exe"),
                    Path.Combine(localAppData, @"CentBrowser\Application\chrome.exe")));

            // 13. Mozilla Firefox
            DetectFirefox(
                results,
                Path.Combine(appData, @"Mozilla\Firefox\Profiles"),
                FindExistingFile(
                    Path.Combine(progFiles, @"Mozilla Firefox\firefox.exe"),
                    Path.Combine(progFilesX86, @"Mozilla Firefox\firefox.exe")));

            // 14. Universal HTML file option
            results.Add(new BrowserProfileInfo
            {
                Type = BrowserType.HtmlFile,
                BrowserName = "Bookmarks HTML File (*.html)",
                ProfileName = "Custom file export",
                AppIcon = CreateFallbackIcon(BrowserType.HtmlFile)
            });

            return results;
        }

        private static void DetectChromiumBrowser(
            List<BrowserProfileInfo> results,
            BrowserType type,
            string browserName,
            string userDataPath,
            string exePath)
        {
            if (string.IsNullOrEmpty(userDataPath) || !Directory.Exists(userDataPath)) return;

            try
            {
                // Scan for profile directories: Default, Profile 1..N
                var candidateDirs = new List<string>();

                string defaultDir = Path.Combine(userDataPath, "Default");
                if (Directory.Exists(defaultDir)) candidateDirs.Add(defaultDir);

                string[] profileDirs = Directory.GetDirectories(userDataPath, "Profile *", SearchOption.TopDirectoryOnly);
                if (profileDirs != null)
                {
                    candidateDirs.AddRange(profileDirs);
                }

                if (candidateDirs.Count == 0 && File.Exists(Path.Combine(userDataPath, "Bookmarks")))
                {
                    candidateDirs.Add(userDataPath);
                }

                for (int i = 0; i < candidateDirs.Count; i++)
                {
                    string pDir = candidateDirs[i];
                    string pName = Path.GetFileName(pDir);

                    // Probe counts
                    int bmCount = ProbeChromiumBookmarkCount(pDir);
                    int histCount = File.Exists(Path.Combine(pDir, "History")) ? 1 : 0;

                    if (bmCount > 0 || histCount > 0 || File.Exists(Path.Combine(pDir, "Bookmarks")))
                    {
                        var info = new BrowserProfileInfo
                        {
                            Type = type,
                            BrowserName = browserName,
                            ProfileName = pName,
                            UserDataPath = userDataPath,
                            ProfilePath = pDir,
                            ExePath = exePath,
                            BookmarkCount = bmCount,
                            HistoryCount = histCount > 0 ? 500 : 0,
                            AppIcon = ResolveAppIcon(exePath, type)
                        };
                        results.Add(info);
                    }
                }
            }
            catch { }
        }

        private static void DetectOperaBrowser(
            List<BrowserProfileInfo> results,
            BrowserType type,
            string browserName,
            string profilePath,
            string exePath)
        {
            if (string.IsNullOrEmpty(profilePath) || !Directory.Exists(profilePath)) return;

            try
            {
                int bmCount = ProbeChromiumBookmarkCount(profilePath);
                int histCount = File.Exists(Path.Combine(profilePath, "History")) ? 1 : 0;

                if (bmCount > 0 || histCount > 0 || File.Exists(Path.Combine(profilePath, "Bookmarks")))
                {
                    results.Add(new BrowserProfileInfo
                    {
                        Type = type,
                        BrowserName = browserName,
                        ProfileName = "Default",
                        UserDataPath = profilePath,
                        ProfilePath = profilePath,
                        ExePath = exePath,
                        BookmarkCount = bmCount,
                        HistoryCount = histCount > 0 ? 500 : 0,
                        AppIcon = ResolveAppIcon(exePath, type)
                    });
                }
            }
            catch { }
        }

        private static void DetectFirefox(List<BrowserProfileInfo> results, string profilesDir, string exePath)
        {
            if (string.IsNullOrEmpty(profilesDir) || !Directory.Exists(profilesDir)) return;

            try
            {
                string[] dirs = Directory.GetDirectories(profilesDir, "*.*", SearchOption.TopDirectoryOnly);
                if (dirs == null) return;

                for (int i = 0; i < dirs.Length; i++)
                {
                    string pDir = dirs[i];
                    string placesDb = Path.Combine(pDir, "places.sqlite");
                    if (File.Exists(placesDb))
                    {
                        string pName = Path.GetFileName(pDir);
                        results.Add(new BrowserProfileInfo
                        {
                            Type = BrowserType.Firefox,
                            BrowserName = "Mozilla Firefox",
                            ProfileName = pName,
                            UserDataPath = profilesDir,
                            ProfilePath = pDir,
                            ExePath = exePath,
                            BookmarkCount = 50, // Available via places.sqlite
                            HistoryCount = 500,
                            AppIcon = ResolveAppIcon(exePath, BrowserType.Firefox)
                        });
                    }
                }
            }
            catch { }
        }

        private static int ProbeChromiumBookmarkCount(string profileDir)
        {
            try
            {
                string bmFile = Path.Combine(profileDir, "Bookmarks");
                if (!File.Exists(bmFile)) return 0;

                string json = File.ReadAllText(bmFile);
                var dict = Serializer.Deserialize<Dictionary<string, object>>(json);
                if (dict == null || !dict.ContainsKey("roots")) return 0;

                var roots = dict["roots"] as Dictionary<string, object>;
                if (roots == null) return 0;

                int count = 0;
                foreach (var kvp in roots)
                {
                    var folder = kvp.Value as Dictionary<string, object>;
                    count += CountChildrenRecursive(folder);
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static int CountChildrenRecursive(Dictionary<string, object> node)
        {
            if (node == null || !node.ContainsKey("children")) return 0;
            var children = node["children"] as ArrayList;
            if (children == null) return 0;

            int count = 0;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i] as Dictionary<string, object>;
                if (child == null) continue;
                string type = child.ContainsKey("type") ? Convert.ToString(child["type"]) : null;
                if (string.Equals(type, "url", StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
                else if (string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase))
                {
                    count += CountChildrenRecursive(child);
                }
            }
            return count;
        }

        private static string FindExistingFile(params string[] candidatePaths)
        {
            if (candidatePaths == null) return null;
            for (int i = 0; i < candidatePaths.Length; i++)
            {
                string p = candidatePaths[i];
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    return p;
                }
            }
            return null;
        }

        public static Image ResolveAppIcon(string exePath, BrowserType type)
        {
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    using (Icon ico = Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (ico != null)
                        {
                            // Produce high quality 24x24 bitmap
                            Bitmap bmp = new Bitmap(24, 24);
                            using (Graphics g = Graphics.FromImage(bmp))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.DrawIcon(ico, new Rectangle(0, 0, 24, 24));
                            }
                            return bmp;
                        }
                    }
                }
                catch { }
            }

            return CreateFallbackIcon(type);
        }

        public static Image CreateFallbackIcon(BrowserType type)
        {
            Bitmap bmp = new Bitmap(24, 24);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                switch (type)
                {
                    case BrowserType.Chrome:
                        DrawBadge(g, Color.FromArgb(66, 133, 244), "C");
                        break;
                    case BrowserType.Edge:
                        DrawBadge(g, Color.FromArgb(0, 120, 215), "E");
                        break;
                    case BrowserType.Brave:
                        DrawBadge(g, Color.FromArgb(251, 84, 43), "B");
                        break;
                    case BrowserType.Opera:
                    case BrowserType.OperaGX:
                        DrawBadge(g, Color.FromArgb(255, 27, 45), "O");
                        break;
                    case BrowserType.Vivaldi:
                        DrawBadge(g, Color.FromArgb(239, 57, 60), "V");
                        break;
                    case BrowserType.Firefox:
                        DrawBadge(g, Color.FromArgb(255, 113, 57), "F");
                        break;
                    case BrowserType.Arc:
                        DrawBadge(g, Color.FromArgb(94, 106, 210), "A");
                        break;
                    case BrowserType.HtmlFile:
                        DrawBadge(g, Color.FromArgb(34, 139, 34), "HTML");
                        break;
                    default:
                        DrawBadge(g, Color.FromArgb(70, 130, 180), "wb");
                        break;
                }
            }
            return bmp;
        }

        private static void DrawBadge(Graphics g, Color bgColor, string letter)
        {
            using (Brush b = new SolidBrush(bgColor))
            {
                g.FillEllipse(b, 2, 2, 20, 20);
            }
            using (Font f = new Font("Segoe UI", letter.Length > 2 ? 6.5f : 9f, FontStyle.Bold))
            using (Brush tb = new SolidBrush(Color.White))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(letter, f, tb, new RectangleF(0, 1, 24, 24), sf);
            }
        }
    }
}

