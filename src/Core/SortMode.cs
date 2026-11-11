using System;

namespace wb.Core
{
    /// <summary>
    /// Common sorting criteria used across Bookmarks, History and Downloads.
    /// </summary>
    public enum SortMode
    {
        TitleAscending,
        TitleDescending,
        FolderAscending,
        FolderDescending,
        TypeAscending,
        TypeDescending,
        DateDescending,
        DateAscending
    }
}
