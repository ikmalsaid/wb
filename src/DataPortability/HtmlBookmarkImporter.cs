using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using wb.Bookmarks;

namespace wb.DataPortability
{
    /// <summary>
    /// Parses standard Netscape Bookmark HTML files exported from Google Chrome,
    /// Microsoft Edge, Mozilla Firefox, Safari, and Brave.
    /// </summary>
    public static class HtmlBookmarkImporter
    {
        private static readonly Regex FolderRegex = new Regex(
            @"<DT><H3([^>]*)>(.*?)</H3>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BookmarkRegex = new Regex(
            @"<DT><A\s+([^>]*?)>(.*?)</A>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HrefRegex = new Regex(
            @"HREF\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AddDateRegex = new Regex(
            @"ADD_DATE\s*=\s*[""']?(\d+)[""']?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ToolbarAttrRegex = new Regex(
            @"PERSONAL_TOOLBAR_FOLDER\s*=\s*[""']?true[""']?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<BookmarkItem> ImportFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return new List<BookmarkItem>();
            }

            string html = File.ReadAllText(filePath, Encoding.UTF8);
            return ImportFromString(html);
        }

        public static List<BookmarkItem> ImportFromString(string html)
        {
            var results = new List<BookmarkItem>();
            if (string.IsNullOrEmpty(html)) return results;

            // Stack to track folder hierarchy
            var folderStack = new Stack<string>();
            folderStack.Push("bar"); // Default root is Bookmarks bar

            using (var reader = new StringReader(html))
            {
                string line;
                string pendingFolderId = null;

                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    // 1. Check for folder closing </DL>
                    if (trimmed.IndexOf("</DL>", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (folderStack.Count > 1)
                        {
                            folderStack.Pop();
                        }
                        continue;
                    }

                    // 2. Check for folder opening <DL> after a folder header
                    if (trimmed.IndexOf("<DL", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!string.IsNullOrEmpty(pendingFolderId))
                        {
                            folderStack.Push(pendingFolderId);
                            pendingFolderId = null;
                        }
                        continue;
                    }

                    // 3. Check for folder header <DT><H3>...</H3>
                    Match folderMatch = FolderRegex.Match(trimmed);
                    if (folderMatch.Success)
                    {
                        string attrs = folderMatch.Groups[1].Value;
                        string title = DecodeHtml(folderMatch.Groups[2].Value.Trim());
                        long addDate = ParseAddDate(attrs);
                        DateTime dt = FromUnixTime(addDate);

                        bool isToolbar = ToolbarAttrRegex.IsMatch(attrs) ||
                                         string.Equals(title, "Bookmarks bar", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(title, "Bookmarks Toolbar", StringComparison.OrdinalIgnoreCase);

                        bool isOther = string.Equals(title, "Other bookmarks", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(title, "Unfiled Bookmarks", StringComparison.OrdinalIgnoreCase);

                        bool isMobile = string.Equals(title, "Mobile bookmarks", StringComparison.OrdinalIgnoreCase);

                        if (isToolbar)
                        {
                            pendingFolderId = "bar";
                        }
                        else if (isOther)
                        {
                            pendingFolderId = "other";
                        }
                        else if (isMobile)
                        {
                            pendingFolderId = "mobile";
                        }
                        else
                        {
                            string currentParent = folderStack.Count > 0 ? folderStack.Peek() : "bar";
                            var folder = new BookmarkItem
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                Title = string.IsNullOrEmpty(title) ? "Folder" : title,
                                Url = string.Empty,
                                CreatedAt = dt,
                                ParentId = currentParent,
                                IsFolder = true
                            };
                            results.Add(folder);
                            pendingFolderId = folder.Id;
                        }
                        continue;
                    }

                    // 4. Check for bookmark <DT><A HREF="...">Title</A>
                    Match bmMatch = BookmarkRegex.Match(trimmed);
                    if (bmMatch.Success)
                    {
                        string attrs = bmMatch.Groups[1].Value;
                        string title = DecodeHtml(bmMatch.Groups[2].Value.Trim());

                        Match hrefMatch = HrefRegex.Match(attrs);
                        if (!hrefMatch.Success) continue;

                        string url = hrefMatch.Groups[1].Value.Trim();
                        if (string.IsNullOrEmpty(url)) continue;

                        long addDate = ParseAddDate(attrs);
                        DateTime dt = FromUnixTime(addDate);

                        string currentParent = folderStack.Count > 0 ? folderStack.Peek() : "bar";
                        var bookmark = new BookmarkItem
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Title = string.IsNullOrEmpty(title) ? url : title,
                            Url = url,
                            CreatedAt = dt,
                            ParentId = currentParent,
                            IsFolder = false
                        };
                        results.Add(bookmark);
                    }
                }
            }

            return results;
        }

        private static long ParseAddDate(string attrs)
        {
            if (string.IsNullOrEmpty(attrs)) return 0;
            Match m = AddDateRegex.Match(attrs);
            if (m.Success)
            {
                long val;
                if (long.TryParse(m.Groups[1].Value, out val))
                {
                    return val;
                }
            }
            return 0;
        }

        private static DateTime FromUnixTime(long unixSeconds)
        {
            if (unixSeconds <= 0) return DateTime.Now;
            try
            {
                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return epoch.AddSeconds(unixSeconds).ToLocalTime();
            }
            catch
            {
                return DateTime.Now;
            }
        }

        private static string DecodeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return WebUtility.HtmlDecode(text);
        }
    }
}

