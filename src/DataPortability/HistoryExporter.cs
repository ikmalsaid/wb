using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using wb.History;

namespace wb.DataPortability
{
    /// <summary>
    /// Exports browsing history to CSV or HTML format.
    /// </summary>
    public static class HistoryExporter
    {
        public static void ExportToCsvFile(HistoryManager manager, string filePath)
        {
            if (manager == null) throw new ArgumentNullException("manager");
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException("filePath");

            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                ExportToCsv(manager.GetAll(), writer);
            }
        }

        public static void ExportToCsv(List<HistoryItem> items, TextWriter writer)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            if (items == null) items = new List<HistoryItem>();

            // CSV Header
            writer.WriteLine("Title,URL,Last Visited");

            for (int i = 0; i < items.Count; i++)
            {
                HistoryItem item = items[i];
                if (item == null || string.IsNullOrEmpty(item.Url)) continue;

                string title = EscapeCsv(item.Title ?? string.Empty);
                string url = EscapeCsv(item.Url);
                string date = item.LocalVisitedAt.ToString("yyyy-MM-dd HH:mm:ss");

                writer.WriteLine(string.Format("\"{0}\",\"{1}\",\"{2}\"", title, url, date));
            }
        }

        public static void ExportToHtmlFile(HistoryManager manager, string filePath)
        {
            if (manager == null) throw new ArgumentNullException("manager");
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException("filePath");

            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                ExportToHtml(manager.GetAll(), writer);
            }
        }

        public static void ExportToHtml(List<HistoryItem> items, TextWriter writer)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            if (items == null) items = new List<HistoryItem>();

            writer.WriteLine("<!DOCTYPE html>");
            writer.WriteLine("<html lang=\"en\">");
            writer.WriteLine("<head>");
            writer.WriteLine("  <meta charset=\"utf-8\">");
            writer.WriteLine("  <title>Browsing History - wb Browser</title>");
            writer.WriteLine("  <style>");
            writer.WriteLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: #202124; color: #e8eaed; padding: 24px; margin: 0; }");
            writer.WriteLine("    h1 { font-size: 20px; font-weight: 600; margin-bottom: 8px; color: #ffffff; }");
            writer.WriteLine("    p.subtitle { color: #9aa0a6; font-size: 13px; margin-top: 0; margin-bottom: 20px; }");
            writer.WriteLine("    table { width: 100%; border-collapse: collapse; font-size: 13px; }");
            writer.WriteLine("    th { text-align: left; padding: 10px 12px; background: #292a2d; color: #9aa0a6; border-bottom: 1px solid #3c4043; font-weight: 500; }");
            writer.WriteLine("    td { padding: 10px 12px; border-bottom: 1px solid #2d2e31; word-break: break-all; }");
            writer.WriteLine("    tr:hover td { background: #282a2d; }");
            writer.WriteLine("    a { color: #8ab4f8; text-decoration: none; }");
            writer.WriteLine("    a:hover { text-decoration: underline; }");
            writer.WriteLine("    .date { color: #9aa0a6; white-space: nowrap; width: 170px; }");
            writer.WriteLine("  </style>");
            writer.WriteLine("</head>");
            writer.WriteLine("<body>");
            writer.WriteLine("  <h1>Browsing History</h1>");
            writer.WriteLine("  <p class=\"subtitle\">Exported from wb Browser on {0} &bull; Total entries: {1}</p>", DateTime.Now.ToString("yyyy-MM-dd HH:mm"), items.Count);
            writer.WriteLine("  <table>");
            writer.WriteLine("    <thead>");
            writer.WriteLine("      <tr>");
            writer.WriteLine("        <th class=\"date\">Date & Time</th>");
            writer.WriteLine("        <th>Title</th>");
            writer.WriteLine("        <th>URL</th>");
            writer.WriteLine("      </tr>");
            writer.WriteLine("    </thead>");
            writer.WriteLine("    <tbody>");

            for (int i = 0; i < items.Count; i++)
            {
                HistoryItem item = items[i];
                if (item == null || string.IsNullOrEmpty(item.Url)) continue;

                string dateStr = item.LocalVisitedAt.ToString("yyyy-MM-dd HH:mm:ss");
                string title = SecurityElement.Escape(string.IsNullOrEmpty(item.Title) ? item.Url : item.Title);
                string url = SecurityElement.Escape(item.Url);

                writer.WriteLine("      <tr>");
                writer.WriteLine("        <td class=\"date\">{0}</td>", dateStr);
                writer.WriteLine("        <td><a href=\"{0}\">{1}</a></td>", url, title);
                writer.WriteLine("        <td><a href=\"{0}\">{0}</a></td>", url);
                writer.WriteLine("      </tr>");
            }

            writer.WriteLine("    </tbody>");
            writer.WriteLine("  </table>");
            writer.WriteLine("</body>");
            writer.WriteLine("</html>");
        }

        private static string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            return field.Replace("\"", "\"\"");
        }
    }
}

