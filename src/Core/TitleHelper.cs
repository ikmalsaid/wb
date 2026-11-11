using System;
using System.Text;
using System.Text.RegularExpressions;
using wb.Core;

namespace wb.Core
{
    /// <summary>
    /// Helper for normalizing and wrapping titles across window title bar, tab strip and tooltips.
    /// Eliminates tooltip rendering lag on long titles and ensures clean single/multi-line presentation.
    /// </summary>
    public static class TitleHelper
    {
        public const int DefaultMaxTitleLength = 120;
        public const int DefaultMaxTooltipLength = 250;
        public const int DefaultTooltipWrapThreshold = 120;
        public const int DefaultTooltipWrapLineLength = 80;

        /// <summary>
        /// Normalizes whitespace: strips leading/trailing whitespace, replaces line breaks and tabs with spaces,
        /// and collapses multiple consecutive spaces into a single space without expensive Regex allocations.
        /// </summary>
        public static string NormalizeWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            int len = s.Length;
            bool needsNormalization = false;

            if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[len - 1]))
            {
                needsNormalization = true;
            }
            else
            {
                for (int i = 0; i < len; i++)
                {
                    char c = s[i];
                    if (c == '\r' || c == '\n' || c == '\t')
                    {
                        needsNormalization = true;
                        break;
                    }
                    if (c == ' ' && i + 1 < len && s[i + 1] == ' ')
                    {
                        needsNormalization = true;
                        break;
                    }
                }
            }

            if (!needsNormalization)
            {
                return s; // Zero allocation for clean titles!
            }

            StringBuilder sb = new StringBuilder(len);
            bool inWhitespace = false;
            bool started = false;

            for (int i = 0; i < len; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    if (started)
                    {
                        inWhitespace = true;
                    }
                }
                else
                {
                    if (inWhitespace)
                    {
                        sb.Append(' ');
                        inWhitespace = false;
                    }
                    sb.Append(c);
                    started = true;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Normalizes whitespace and wraps/truncates long titles to a safe length.
        /// Replaces line breaks and tabs with spaces, collapses multiple spaces,
        /// and appends ellipsis (...) if the title exceeds maxLength.
        /// </summary>
        public static string NormalizeAndWrapTitle(string title, int maxLength = DefaultMaxTitleLength)
        {
            if (string.IsNullOrEmpty(title)) return string.Empty;

            // 1. Normalize linebreaks, tabs and multiple spaces
            string clean = NormalizeWhitespace(title);

            if (string.IsNullOrEmpty(clean)) return string.Empty;

            // 2. Wrap/truncate if too long
            if (maxLength > 0 && clean.Length > maxLength)
            {
                return clean.Substring(0, maxLength).TrimEnd() + "...";
            }

            return clean;
        }

        /// <summary>
        /// Formats a clean window title, wrapping long page titles to prevent Windows taskbar/titlebar clutter.
        /// </summary>
        public static string FormatWindowTitle(string pageTitle, bool isIncognito, int maxLength = DefaultMaxTitleLength)
        {
            if (string.IsNullOrEmpty(pageTitle) || pageTitle == "wb" || pageTitle == "wb Browser" || pageTitle == "New Tab" || pageTitle == AppStrings.AppTitle || pageTitle == AppStrings.Browser.WindowTitle)
            {
                return isIncognito ? AppStrings.Browser.WindowTitleIncognito : AppStrings.Browser.WindowTitle;
            }

            string wrapped = NormalizeAndWrapTitle(pageTitle, maxLength);

            if (wrapped.EndsWith(" - wb", StringComparison.OrdinalIgnoreCase))
            {
                wrapped = wrapped.Substring(0, wrapped.Length - 5).TrimEnd();
            }
            else if (wrapped.EndsWith(" - wb Browser", StringComparison.OrdinalIgnoreCase))
            {
                wrapped = wrapped.Substring(0, wrapped.Length - 13).TrimEnd();
            }

            if (string.IsNullOrEmpty(wrapped) || wrapped == "wb" || wrapped == "wb Browser")
            {
                return isIncognito ? AppStrings.Browser.WindowTitleIncognito : AppStrings.Browser.WindowTitle;
            }

            return isIncognito
                ? string.Format("{0} - " + AppStrings.Browser.WindowTitleIncognito, wrapped)
                : string.Format(AppStrings.Browser.TabTitleWithAppFormat, wrapped);
        }

        /// <summary>
        /// Formats a tab title tooltip safely:
        /// 1. Cleans whitespace.
        /// 2. Caches and limits maximum length to eliminate Win32 tooltip measurement lag.
        /// 3. Wraps into multi-line text at word boundaries when title exceeds threshold.
        /// </summary>
        public static string FormatTabToolTip(string title, int maxTotalLength = DefaultMaxTooltipLength, int wrapLineLength = DefaultTooltipWrapLineLength)
        {
            if (string.IsNullOrEmpty(title)) return string.Empty;

            // 1. Normalize whitespace
            string clean = NormalizeWhitespace(title);

            if (string.IsNullOrEmpty(clean)) return string.Empty;

            // 2. Cap maximum length to prevent massive GDI measurement overhead
            if (maxTotalLength > 0 && clean.Length > maxTotalLength)
            {
                clean = clean.Substring(0, maxTotalLength).TrimEnd() + "...";
            }

            // 3. Only wrap into multiple lines if the title exceeds wrap threshold
            if (clean.Length <= DefaultTooltipWrapThreshold)
            {
                return clean;
            }

            // Word wrap every wrapLineLength characters
            StringBuilder sb = new StringBuilder();
            int currentLineLen = 0;
            string[] words = clean.Split(' ');

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (string.IsNullOrEmpty(word)) continue;

                if (currentLineLen > 0 && currentLineLen + 1 + word.Length > wrapLineLength)
                {
                    sb.Append('\n');
                    currentLineLen = 0;
                }
                else if (currentLineLen > 0)
                {
                    sb.Append(' ');
                    currentLineLen++;
                }

                // If a single word is longer than wrapLineLength, chunk it
                while (word.Length > wrapLineLength)
                {
                    string part = word.Substring(0, wrapLineLength);
                    sb.Append(part);
                    sb.Append('\n');
                    word = word.Substring(wrapLineLength);
                    currentLineLen = 0;
                }

                sb.Append(word);
                currentLineLen += word.Length;
            }

            return sb.ToString();
        }
    }
}
