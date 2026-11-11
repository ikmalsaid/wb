using System;
using System.Collections.Generic;
using System.Globalization;

namespace wb.SponsorBlock
{
    /// <summary>
    /// Tracks lifetime and session statistics for skipped sponsor segments and saved time.
    /// Serialized to disk using StorageHelper.
    /// </summary>
    public class SponsorBlockStats
    {
        public long TotalSkips { get; set; }
        public double TotalTimeSavedSeconds { get; set; }
        public Dictionary<string, long> CategorySkips { get; set; }

        public SponsorBlockStats()
        {
            TotalSkips = 0;
            TotalTimeSavedSeconds = 0;
            CategorySkips = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        public void AddSkip(string category, double timeSavedSeconds)
        {
            TotalSkips++;
            if (timeSavedSeconds > 0)
            {
                TotalTimeSavedSeconds += timeSavedSeconds;
            }

            if (!string.IsNullOrEmpty(category))
            {
                if (CategorySkips == null)
                {
                    CategorySkips = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                }
                long count;
                CategorySkips.TryGetValue(category, out count);
                CategorySkips[category] = count + 1;
            }
        }

        public string FormattedTimeSaved
        {
            get
            {
                double totalSec = TotalTimeSavedSeconds;
                if (totalSec <= 0) return "0s";

                TimeSpan ts = TimeSpan.FromSeconds(totalSec);
                if (ts.TotalDays >= 1)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0}d {1}h {2}m",
                        (int)ts.TotalDays, ts.Hours, ts.Minutes);
                }
                if (ts.TotalHours >= 1)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m",
                        (int)ts.TotalHours, ts.Minutes);
                }
                if (ts.TotalMinutes >= 1)
                {
                    return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s",
                        ts.Minutes, ts.Seconds);
                }
                return string.Format(CultureInfo.InvariantCulture, "{0:F0}s", totalSec);
            }
        }

        public void Reset()
        {
            TotalSkips = 0;
            TotalTimeSavedSeconds = 0;
            if (CategorySkips != null) CategorySkips.Clear();
        }
    }

    public class SponsorSkipEventArgs : EventArgs
    {
        public string VideoId { get; private set; }
        public string Category { get; private set; }
        public double TimeSavedSeconds { get; private set; }

        public SponsorSkipEventArgs(string videoId, string category, double timeSavedSeconds)
        {
            VideoId = videoId;
            Category = category;
            TimeSavedSeconds = timeSavedSeconds;
        }
    }
}
