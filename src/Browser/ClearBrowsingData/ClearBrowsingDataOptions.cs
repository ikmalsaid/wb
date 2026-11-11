using System;

namespace wb.Browser.ClearBrowsingData
{
    public class ClearBrowsingDataOptions
    {
        public ClearBrowsingDataTimeRange TimeRange { get; set; }

        public bool ClearBrowsingHistory { get; set; }
        public bool ClearDownloadHistory { get; set; }
        public bool ClearCookiesAndSiteData { get; set; }
        public bool ClearCachedImagesAndFiles { get; set; }
        public bool ClearPasswords { get; set; }
        public bool ClearAutofillFormData { get; set; }
        public bool ClearSiteSettings { get; set; }
        public bool ClearHostedAppData { get; set; }

        public ClearBrowsingDataOptions()
        {
            TimeRange = ClearBrowsingDataTimeRange.LastHour;
            ClearBrowsingHistory = true;
            ClearDownloadHistory = true;
            ClearCookiesAndSiteData = true;
            ClearCachedImagesAndFiles = true;
            ClearPasswords = false;
            ClearAutofillFormData = false;
            ClearSiteSettings = false;
            ClearHostedAppData = false;
        }

        public static ClearBrowsingDataOptions CreateDefaultBasic()
        {
            return new ClearBrowsingDataOptions
            {
                TimeRange = ClearBrowsingDataTimeRange.LastHour,
                ClearBrowsingHistory = true,
                ClearDownloadHistory = false,
                ClearCookiesAndSiteData = true,
                ClearCachedImagesAndFiles = true,
                ClearPasswords = false,
                ClearAutofillFormData = false,
                ClearSiteSettings = false,
                ClearHostedAppData = false
            };
        }

        public static ClearBrowsingDataOptions CreateDefaultAdvanced()
        {
            return new ClearBrowsingDataOptions
            {
                TimeRange = ClearBrowsingDataTimeRange.LastHour,
                ClearBrowsingHistory = true,
                ClearDownloadHistory = true,
                ClearCookiesAndSiteData = true,
                ClearCachedImagesAndFiles = true,
                ClearPasswords = false,
                ClearAutofillFormData = false,
                ClearSiteSettings = false,
                ClearHostedAppData = false
            };
        }
    }
}
