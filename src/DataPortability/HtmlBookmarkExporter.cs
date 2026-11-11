using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using wb.Bookmarks;

namespace wb.DataPortability
{
    /// <summary>
    /// Exports wb bookmarks into the universal Netscape Bookmark HTML format
    /// supported by Google Chrome, Microsoft Edge, Mozilla Firefox, Safari, and Brave.
    /// </summary>
    public static class HtmlBookmarkExporter
    {
        public static void ExportToFile(BookmarkManager manager, string filePath)
        {
            if (manager == null) throw new ArgumentNullException("manager");
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException("filePath");

            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                Export(manager.GetAll(), writer);
            }
        }

        public static string ExportToString(List<BookmarkItem> items)
        {
            var sb = new StringBuilder();
            using (var writer = new StringWriter(sb))
            {
                Export(items, writer);
            }
            return sb.ToString();
        }

        public static void Export(List<BookmarkItem> items, TextWriter writer)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            if (items == null) items = new List<BookmarkItem>();

            writer.WriteLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
            writer.WriteLine("<!-- This is an automatically generated file.");
            writer.WriteLine("     It will be read and overwritten.");
            writer.WriteLine("     DO NOT EDIT! -->");
            writer.WriteLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
            writer.WriteLine("<TITLE>Bookmarks</TITLE>");
            writer.WriteLine("<H1>Bookmarks</H1>");
            writer.WriteLine("<DL><p>");

            // Group items by parent ID
            var childrenMap = new Dictionary<string, List<BookmarkItem>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Count; i++)
            {
                BookmarkItem item = items[i];
                if (item == null) continue;

                string parent = string.IsNullOrEmpty(item.ParentId) ? "bar" : item.ParentId;
                List<BookmarkItem> list;
                if (!childrenMap.TryGetValue(parent, out list))
                {
                    list = new List<BookmarkItem>();
                    childrenMap[parent] = list;
                }
                list.Add(item);
            }

            long nowUnix = ToUnixTimeSeconds(DateTime.Now);

            // 1. Bookmarks Bar
            writer.WriteLine("    <DT><H3 ADD_DATE=\"{0}\" LAST_MODIFIED=\"{0}\" PERSONAL_TOOLBAR_FOLDER=\"true\">Bookmarks bar</H3>", nowUnix);
            writer.WriteLine("    <DL><p>");
            List<BookmarkItem> barItems;
            if (childrenMap.TryGetValue("bar", out barItems))
            {
                WriteFolderContents(barItems, childrenMap, writer, "        ");
            }
            writer.WriteLine("    </DL><p>");

            // 2. Other Bookmarks
            writer.WriteLine("    <DT><H3 ADD_DATE=\"{0}\" LAST_MODIFIED=\"{0}\">Other bookmarks</H3>", nowUnix);
            writer.WriteLine("    <DL><p>");
            List<BookmarkItem> otherItems;
            if (childrenMap.TryGetValue("other", out otherItems))
            {
                WriteFolderContents(otherItems, childrenMap, writer, "        ");
            }
            writer.WriteLine("    </DL><p>");

            // 3. Mobile Bookmarks (if any)
            List<BookmarkItem> mobileItems;
            if (childrenMap.TryGetValue("mobile", out mobileItems) && mobileItems.Count > 0)
            {
                writer.WriteLine("    <DT><H3 ADD_DATE=\"{0}\" LAST_MODIFIED=\"{0}\">Mobile bookmarks</H3>", nowUnix);
                writer.WriteLine("    <DL><p>");
                WriteFolderContents(mobileItems, childrenMap, writer, "        ");
                writer.WriteLine("    </DL><p>");
            }

            writer.WriteLine("</DL><p>");
        }

        private static void WriteFolderContents(
            List<BookmarkItem> items,
            Dictionary<string, List<BookmarkItem>> childrenMap,
            TextWriter writer,
            string indent)
        {
            if (items == null) return;

            for (int i = 0; i < items.Count; i++)
            {
                BookmarkItem item = items[i];
                if (item == null) continue;

                long dateUnix = ToUnixTimeSeconds(item.CreatedAt);

                if (item.IsFolder)
                {
                    string safeTitle = EscapeHtml(item.Title ?? "Folder");
                    writer.WriteLine("{0}<DT><H3 ADD_DATE=\"{1}\" LAST_MODIFIED=\"{1}\">{2}</H3>", indent, dateUnix, safeTitle);
                    writer.WriteLine("{0}<DL><p>", indent);

                    List<BookmarkItem> subItems;
                    if (!string.IsNullOrEmpty(item.Id) && childrenMap.TryGetValue(item.Id, out subItems))
                    {
                        WriteFolderContents(subItems, childrenMap, writer, indent + "    ");
                    }

                    writer.WriteLine("{0}</DL><p>", indent);
                }
                else
                {
                    if (string.IsNullOrEmpty(item.Url)) continue;

                    string safeUrl = EscapeHtml(item.Url);
                    string safeTitle = EscapeHtml(string.IsNullOrEmpty(item.Title) ? item.Url : item.Title);

                    writer.WriteLine("{0}<DT><A HREF=\"{1}\" ADD_DATE=\"{2}\">{3}</A>", indent, safeUrl, dateUnix, safeTitle);
                }
            }
        }

        private static long ToUnixTimeSeconds(DateTime dt)
        {
            try
            {
                DateTime utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                if (utc < epoch) return 0;
                return (long)(utc - epoch).TotalSeconds;
            }
            catch
            {
                return 0;
            }
        }

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return SecurityElement.Escape(text);
        }
    }
}

