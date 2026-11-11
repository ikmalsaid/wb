using System;
using System.Drawing;

namespace wb.DataPortability
{
    public enum BrowserType
    {
        Chrome,
        Edge,
        Brave,
        Opera,
        OperaGX,
        Vivaldi,
        Chromium,
        Arc,
        Yandex,
        Thorium,
        CocCoc,
        CentBrowser,
        Firefox,
        HtmlFile,
        CustomFolder
    }

    public class BrowserProfileInfo
    {
        public BrowserType Type { get; set; }
        public string BrowserName { get; set; }
        public string ProfileName { get; set; }
        public string UserDataPath { get; set; }
        public string ProfilePath { get; set; }
        public string ExePath { get; set; }
        public Image AppIcon { get; set; }

        public int BookmarkCount { get; set; }
        public int HistoryCount { get; set; }
        public int ShortcutCount { get; set; }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(ProfileName) || string.Equals(ProfileName, "Default", StringComparison.OrdinalIgnoreCase))
                {
                    return BrowserName;
                }
                return string.Format("{0} ({1})", BrowserName, ProfileName);
            }
        }

        public string SummaryText
        {
            get
            {
                return string.Empty;
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class ImportDataOptions
    {
        public bool ImportBookmarks { get; set; }
        public bool ImportHistory { get; set; }
        public bool ImportShortcuts { get; set; }
        public bool SkipDuplicates { get; set; }

        public bool PlaceInDedicatedFolder { get; set; }
        public string DedicatedFolderName { get; set; }
        public string TargetParentId { get; set; }

        public ImportDataOptions()
        {
            ImportBookmarks = true;
            ImportHistory = true;
            ImportShortcuts = true;
            SkipDuplicates = true;
            PlaceInDedicatedFolder = false;
            DedicatedFolderName = "Imported Bookmarks";
            TargetParentId = "bar";
        }
    }

    public class ImportDataResult
    {
        public bool Success { get; set; }
        public int BookmarksImported { get; set; }
        public int FoldersImported { get; set; }
        public int HistoryImported { get; set; }
        public int ShortcutsImported { get; set; }
        public int SkippedDuplicates { get; set; }
        public string ErrorMessage { get; set; }

        public string FormatSummary()
        {
            return string.Format("Imported {0} bookmark{1}, {2} folder{3}, {4} history item{5}, and {6} shortcut{7}.",
                BookmarksImported, BookmarksImported == 1 ? "" : "s",
                FoldersImported, FoldersImported == 1 ? "" : "s",
                HistoryImported, HistoryImported == 1 ? "" : "s",
                ShortcutsImported, ShortcutsImported == 1 ? "" : "s");
        }
    }

    public class ExportDataResult
    {
        public bool Success { get; set; }
        public int Count { get; set; }
        public string FilePath { get; set; }
        public string ErrorMessage { get; set; }
    }
}

