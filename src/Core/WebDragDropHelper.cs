using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace wb.Core
{
    /// <summary>
    /// Helper for web drag-and-drop operations, control UI interaction fixes,
    /// and OLE URL drag-and-drop parsing.
    /// </summary>
    public static class WebDragDropHelper
    {
        /// <summary>
        /// JavaScript engine that prevents control UI elements (sliders, scrubbers, range inputs,
        /// interactive canvases, player controls) from triggering unwanted hyperlink/page drag operations,
        /// while preserving genuine hyperlink drags, image drags and text selection drags.
        /// </summary>
        public const string DragControlFixScript = @"(function() {
    if (window.__wbDragControlFixInstalled) return;
    window.__wbDragControlFixInstalled = true;

    try {
        var style = document.createElement('style');
        style.type = 'text/css';
        style.textContent = 'input[type=""range""], [role=""slider""], [role=""scrollbar""], [role=""progressbar""], ' +
            '[aria-valuenow], [class*=""slider""], [class*=""thumb""], [class*=""handle""], ' +
            '[class*=""scrubber""], [class*=""seek""], [class*=""knob""], canvas:not([draggable=""true""]) { ' +
            '-webkit-user-drag: none !important; }';
        (document.head || document.documentElement).appendChild(style);
    } catch (ex) {}

    function isControlElement(el) {
        if (!el) return false;
        try {
            if (el.nodeType !== 1) el = el.parentElement;
            if (!el || !el.closest) return false;

            var controlSelector = 'input, textarea, select, button, ' +
                '[role=""slider""], [role=""scrollbar""], [role=""progressbar""], [role=""spinbutton""], [role=""switch""], ' +
                '[aria-valuenow], [aria-valuemin], [aria-valuemax], ' +
                '[class*=""slider""], [class*=""thumb""], [class*=""handle""], [class*=""progress""], ' +
                '[class*=""range""], [class*=""knob""], [class*=""scrubber""], [class*=""seek""], [class*=""volume""], ' +
                '[id*=""slider""], [id*=""progress""], [id*=""thumb""], [id*=""handle""], [id*=""scrubber""], [id*=""volume""]';

            if (el.closest(controlSelector)) return true;
            if (el.tagName === 'CANVAS' && !el.closest('a[href]')) return true;
        } catch (ex) {}
        return false;
    }

    function isDraggablePayload(el) {
        if (!el) return false;
        try {
            if (el.nodeType !== 1) el = el.parentElement;
            if (!el) return false;

            // Genuine hyperlink
            if (el.closest && el.closest('a[href], area[href]')) return true;

            // Image
            if (el.tagName === 'IMG' || (el.closest && el.closest('img'))) return true;

            // Explicitly draggable HTML5 element
            if (el.getAttribute && el.getAttribute('draggable') === 'true') return true;

            // User has selected text
            var sel = window.getSelection ? window.getSelection().toString() : '';
            if (sel && sel.trim().length > 0) return true;
        } catch (ex) {}
        return false;
    }

    window.addEventListener('dragstart', function(e) {
        var target = e.target;
        if (isControlElement(target)) {
            e.preventDefault();
            return;
        }
        if (!isDraggablePayload(target)) {
            // Prevent dragging arbitrary web divs/elements from falling back to dragging page URL/title
            e.preventDefault();
            return;
        }
    }, true);
})();";

        /// <summary>
        /// Checks whether the OLE drag data contains a valid URL or link payload.
        /// </summary>
        public static bool HasUrlData(IDataObject data)
        {
            if (data == null) return false;
            try
            {
                if (data.GetDataPresent("UniformResourceLocatorW") ||
                    data.GetDataPresent("UniformResourceLocator") ||
                    data.GetDataPresent(DataFormats.FileDrop))
                {
                    return true;
                }

                if (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text))
                {
                    string text = ExtractUrlFromDataObject(data);
                    return !string.IsNullOrEmpty(text);
                }

                if (data.GetDataPresent("HTML Format"))
                {
                    string text = ExtractUrlFromDataObject(data);
                    return !string.IsNullOrEmpty(text);
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// Extracts a clean URL string from Windows OLE IDataObject.
        /// Supports UniformResourceLocator (Unicode and ANSI), plain text, .url files and HTML hyperlinks.
        /// </summary>
        public static string ExtractUrlFromDataObject(IDataObject data)
        {
            if (data == null) return null;

            try
            {
                // 1. UniformResourceLocatorW (Unicode)
                if (data.GetDataPresent("UniformResourceLocatorW"))
                {
                    object raw = data.GetData("UniformResourceLocatorW");
                    string url = ReadStringFromRawData(raw, Encoding.Unicode);
                    if (IsValidWebUrl(url)) return CleanUrl(url);
                }

                // 2. UniformResourceLocator (ANSI / UTF-8)
                if (data.GetDataPresent("UniformResourceLocator"))
                {
                    object raw = data.GetData("UniformResourceLocator");
                    string url = ReadStringFromRawData(raw, Encoding.Default);
                    if (IsValidWebUrl(url)) return CleanUrl(url);
                }

                // 3. FileDrop (e.g. .url internet shortcut file or local html file)
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0 && !string.IsNullOrEmpty(files[0]))
                    {
                        string path = files[0];
                        if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                        {
                            try
                            {
                                string[] lines = File.ReadAllLines(path);
                                for (int i = 0; i < lines.Length; i++)
                                {
                                    string line = lines[i].Trim();
                                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string target = line.Substring(4).Trim();
                                        if (IsValidWebUrl(target)) return CleanUrl(target);
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }

                        if (File.Exists(path) || Directory.Exists(path))
                        {
                            try
                            {
                                return new Uri(path).AbsoluteUri;
                            }
                            catch
                            {
                            }
                        }
                    }
                }

                // 4. UnicodeText / Text
                if (data.GetDataPresent(DataFormats.UnicodeText))
                {
                    string text = data.GetData(DataFormats.UnicodeText) as string;
                    string url = ParseUrlFromText(text);
                    if (!string.IsNullOrEmpty(url)) return url;
                }

                if (data.GetDataPresent(DataFormats.Text))
                {
                    string text = data.GetData(DataFormats.Text) as string;
                    string url = ParseUrlFromText(text);
                    if (!string.IsNullOrEmpty(url)) return url;
                }

                // 5. HTML Format (extract href)
                if (data.GetDataPresent("HTML Format"))
                {
                    string html = data.GetData("HTML Format") as string;
                    if (!string.IsNullOrEmpty(html))
                    {
                        Match m = Regex.Match(html, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                        if (m.Success && IsValidWebUrl(m.Groups[1].Value))
                        {
                            return CleanUrl(m.Groups[1].Value);
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string ReadStringFromRawData(object raw, Encoding enc)
        {
            if (raw == null) return null;
            if (raw is string) return (string)raw;

            if (raw is MemoryStream)
            {
                using (MemoryStream ms = (MemoryStream)raw)
                {
                    byte[] bytes = ms.ToArray();
                    int nullIdx = -1;
                    if (enc == Encoding.Unicode)
                    {
                        for (int i = 0; i < bytes.Length - 1; i += 2)
                        {
                            if (bytes[i] == 0 && bytes[i + 1] == 0)
                            {
                                nullIdx = i;
                                break;
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < bytes.Length; i++)
                        {
                            if (bytes[i] == 0)
                            {
                                nullIdx = i;
                                break;
                            }
                        }
                    }

                    int len = (nullIdx >= 0) ? nullIdx : bytes.Length;
                    if (len > 0)
                    {
                        return enc.GetString(bytes, 0, len).Trim();
                    }
                }
            }

            return null;
        }

        private static string ParseUrlFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (IsValidWebUrl(line))
                {
                    return CleanUrl(line);
                }
            }

            return null;
        }

        /// <summary>
        /// Validates if string represents a navigable URL.
        /// </summary>
        public static bool IsValidWebUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string trimmed = url.Trim();

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("wb:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("wb-", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check if looks like domain name e.g. domain.com or domain.org/path
            if (trimmed.IndexOf('.') > 0 && !trimmed.Contains(" ") && trimmed.Length >= 4)
            {
                int dotIdx = trimmed.IndexOf('.');
                if (dotIdx > 0 && dotIdx < trimmed.Length - 1)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Normalizes URL e.g. adds https:// to www. or bare domain.
        /// </summary>
        public static string CleanUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            string trimmed = url.Trim();

            if (trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                return "https://" + trimmed;
            }

            if (!trimmed.Contains("://") &&
                !trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("wb:", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("wb-", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.IndexOf('.') > 0 && !trimmed.Contains(" "))
                {
                    return "https://" + trimmed;
                }
            }

            return trimmed;
        }
    }
}
