using System;
using wb.Core;

namespace wb.Browser.ClearBrowsingData
{
    public enum ClearBrowsingDataTimeRange
    {
        LastHour = 0,
        Last24Hours = 1,
        Last7Days = 2,
        Last4Weeks = 3,
        AllTime = 4
    }

    public static class ClearBrowsingDataTimeRangeExtensions
    {
        public static DateTime GetCutoffUtc(this ClearBrowsingDataTimeRange range)
        {
            DateTime now = DateTime.Now;
            switch (range)
            {
                case ClearBrowsingDataTimeRange.LastHour:
                    return now.AddHours(-1);
                case ClearBrowsingDataTimeRange.Last24Hours:
                    return now.AddDays(-1);
                case ClearBrowsingDataTimeRange.Last7Days:
                    return now.AddDays(-7);
                case ClearBrowsingDataTimeRange.Last4Weeks:
                    return now.AddDays(-28);
                case ClearBrowsingDataTimeRange.AllTime:
                default:
                    return DateTime.MinValue;
            }
        }

        public static string GetDisplayName(this ClearBrowsingDataTimeRange range)
        {
            switch (range)
            {
                case ClearBrowsingDataTimeRange.LastHour:
                    return AppStrings.ClearBrowsingData.TimeRangeLastHour;
                case ClearBrowsingDataTimeRange.Last24Hours:
                    return AppStrings.ClearBrowsingData.TimeRangeLast24Hours;
                case ClearBrowsingDataTimeRange.Last7Days:
                    return AppStrings.ClearBrowsingData.TimeRangeLast7Days;
                case ClearBrowsingDataTimeRange.Last4Weeks:
                    return AppStrings.ClearBrowsingData.TimeRangeLast4Weeks;
                case ClearBrowsingDataTimeRange.AllTime:
                default:
                    return AppStrings.ClearBrowsingData.TimeRangeAllTime;
            }
        }
    }
}
