using System;

namespace wb.Browser.ClearBrowsingData
{
    public class ClearBrowsingDataResult
    {
        public bool Success { get; set; }
        public int HistoryItemsCleared { get; set; }
        public int DownloadItemsCleared { get; set; }
        public long CacheBytesFreed { get; set; }
        public bool CookiesCleared { get; set; }
        public bool SiteSettingsCleared { get; set; }
        public bool AutofillCleared { get; set; }
        public string ErrorMessage { get; set; }

        public ClearBrowsingDataResult()
        {
            Success = true;
        }
    }
}
