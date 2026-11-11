using System;
using System.IO;
using System.Net;
using System.Text;
using wb.Core;

namespace wb.Browser
{
    public static class SourceViewerTemplate
    {
        public static string GenerateHtml(string url, string rawHtml)
        {
            if (rawHtml == null)
            {
                rawHtml = string.Empty;
            }

            string encodedUrl = WebUtility.HtmlEncode(url ?? "wb://newtab");
            string[] lines = rawHtml.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            int lineCount = lines.Length;
            int charCount = rawHtml.Length;

            StringBuilder codeRows = new StringBuilder();
            for (int i = 0; i < lineCount; i++)
            {
                int lineNum = i + 1;
                string encodedLine = WebUtility.HtmlEncode(lines[i]);
                codeRows.Append("<tr id=\"L").Append(lineNum).Append("\">")
                        .Append("<td class=\"line-num\" data-line=\"").Append(lineNum).Append("\">").Append(lineNum).Append("</td>")
                        .Append("<td class=\"line-content\">").Append(encodedLine).Append("</td>")
                        .Append("</tr>\n");
            }

            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>Source: " + encodedUrl + @"</title>
    <style>
        :root {
            --bg-color: #0d1117;
            --header-bg: #161b22;
            --border-color: #30363d;
            --text-color: #c9d1d9;
            --line-num-color: #484f58;
            --accent: #58a6ff;
            --highlight-bg: rgba(56, 139, 253, 0.15);
        }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: ui-monospace, SFMono-Regular, Consolas, 'Liberation Mono', Menlo, monospace;
            background-color: var(--bg-color);
            color: var(--text-color);
            font-size: 13px;
            line-height: 1.5;
        }
        .header-bar {
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            height: 42px;
            background-color: var(--header-bg);
            border-bottom: 1px solid var(--border-color);
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 0 1rem;
            z-index: 1000;
        }
        .header-title {
            font-size: 13px;
            font-weight: 600;
            color: #f0f6fc;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
            max-width: 60%;
        }
        .header-meta {
            display: flex;
            align-items: center;
            gap: 1rem;
            font-size: 12px;
            color: #8b949e;
        }
        .btn-copy {
            background-color: #21262d;
            color: #c9d1d9;
            border: 1px solid var(--border-color);
            border-radius: 6px;
            padding: 4px 10px;
            font-size: 12px;
            font-weight: 500;
            cursor: pointer;
            transition: all 0.2s;
        }
        .btn-copy:hover {
            background-color: #30363d;
            border-color: #8b949e;
            color: #f0f6fc;
        }
        .code-container {
            margin-top: 42px;
            padding: 0.5rem 0;
            overflow-x: auto;
        }
        table {
            border-collapse: collapse;
            width: 100%;
        }
        td {
            padding: 0 8px;
            vertical-align: top;
            white-space: pre-wrap;
            word-break: break-all;
        }
        .line-num {
            width: 50px;
            min-width: 50px;
            text-align: right;
            color: var(--line-num-color);
            user-select: none;
            border-right: 1px solid var(--border-color);
            font-size: 12px;
        }
        .line-content {
            padding-left: 12px;
            tab-size: 4;
        }
        tr:hover {
            background-color: var(--highlight-bg);
        }
    </style>
</head>
<body>
    <div class=""header-bar"">
        <div class=""header-title"">Source: " + encodedUrl + @"</div>
        <div class=""header-meta"">
            <span>" + lineCount + @" lines (" + charCount + @" chars)</span>
            <button class=""btn-copy"" id=""copyBtn"">Copy Source</button>
        </div>
    </div>
    <div class=""code-container"">
        <table>
            <tbody>
" + codeRows.ToString() + @"
            </tbody>
        </table>
    </div>
    <script>
        document.getElementById('copyBtn').addEventListener('click', function() {
            var rawText = document.querySelector('tbody').innerText;
            navigator.clipboard.writeText(rawText).then(function() {
                var btn = document.getElementById('copyBtn');
                btn.textContent = 'Copied!';
                setTimeout(function() { btn.textContent = 'Copy Source'; }, 2000);
            });
        });
    </script>
</body>
</html>";
        }

        public static string SaveToTempFile(string url, string rawHtml)
        {
            try
            {
                string filePath = StorageHelper.CreateTempHtmlFile("temp_source", "source", GenerateHtml(url, rawHtml));
                if (filePath != "wb://newtab" && File.Exists(filePath))
                {
                    return new Uri(filePath).AbsoluteUri;
                }
                return "wb://newtab";
            }
            catch
            {
                return "wb://newtab";
            }
        }
    }
}
