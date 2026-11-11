using System;
using System.Collections.Generic;
using wb.Bookmarks;
using wb.Browser;
using wb.Core;
using wb.History;

namespace wb.DataPortability
{
    public static class BrowserImportEngine
    {
        public static ImportDataResult ExecuteImport(
            BrowserProfileInfo profile,
            string customFilePath,
            ImportDataOptions options)
        {
            var result = new ImportDataResult { Success = true };
            if (profile == null)
            {
                result.Success = false;
                result.ErrorMessage = "No browser or file selected.";
                return result;
            }

            if (options == null) options = new ImportDataOptions();

            try
            {
                List<BookmarkItem> bookmarks = null;
                List<HistoryItem> history = null;
                List<ShortcutItem> shortcuts = null;

                // 1. Extraction
                if (profile.Type == BrowserType.HtmlFile)
                {
                    if (string.IsNullOrEmpty(customFilePath))
                    {
                        result.Success = false;
                        result.ErrorMessage = "Please select an HTML bookmark file.";
                        return result;
                    }
                    bookmarks = HtmlBookmarkImporter.ImportFromFile(customFilePath);
                }
                else if (profile.Type == BrowserType.Firefox)
                {
                    if (options.ImportBookmarks)
                    {
                        bookmarks = FirefoxDataImporter.ImportBookmarks(profile.ProfilePath);
                    }
                    if (options.ImportHistory)
                    {
                        history = FirefoxDataImporter.ImportHistory(profile.ProfilePath);
                    }
                }
                else // Chromium-based family (Chrome, Edge, Brave, Opera, Vivaldi, Arc, etc.)
                {
                    if (options.ImportBookmarks)
                    {
                        bookmarks = ChromiumDataImporter.ImportBookmarks(profile.ProfilePath);
                    }
                    if (options.ImportHistory)
                    {
                        history = ChromiumDataImporter.ImportHistory(profile.ProfilePath);
                    }
                    if (options.ImportShortcuts)
                    {
                        shortcuts = ChromiumDataImporter.ImportShortcuts(profile.ProfilePath);
                    }
                }

                // 2. Ingest Bookmarks
                if (bookmarks != null && bookmarks.Count > 0)
                {
                    string targetParent = null;
                    if (options.PlaceInDedicatedFolder)
                    {
                        string folderName = string.IsNullOrEmpty(options.DedicatedFolderName)
                            ? ("Imported from " + profile.BrowserName)
                            : options.DedicatedFolderName;

                        var folder = AppServices.Bookmarks.AddFolder(folderName, "bar");
                        if (folder != null)
                        {
                            targetParent = folder.Id;
                        }
                    }

                    var bmResult = AppServices.Bookmarks.ImportBatch(bookmarks, targetParent, options.SkipDuplicates);
                    result.BookmarksImported = bmResult.BookmarksImported;
                    result.FoldersImported = bmResult.FoldersImported;
                    result.SkippedDuplicates += bmResult.SkippedDuplicates;
                }

                // 3. Ingest History
                if (history != null && history.Count > 0)
                {
                    var histResult = AppServices.History.ImportBatch(history, options.SkipDuplicates);
                    result.HistoryImported = histResult.ItemsImported;
                    result.SkippedDuplicates += histResult.SkippedDuplicates;
                }

                // 4. Ingest Shortcuts
                if (shortcuts != null && shortcuts.Count > 0)
                {
                    result.ShortcutsImported = AppServices.Shortcuts.ImportBatch(shortcuts, options.SkipDuplicates);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }
    }
}

