using System;
using System.Globalization;

namespace wb.SponsorBlock
{
    /// <summary>
    /// Represents a single skip segment retrieved from the SponsorBlock API.
    /// </summary>
    public class SponsorSegment
    {
        public string UUID { get; set; }
        public string Category { get; set; }
        public string ActionType { get; set; }
        public double Start { get; set; }
        public double End { get; set; }
        public double VideoDuration { get; set; }
        public string Description { get; set; }

        public double Duration
        {
            get { return Math.Max(0.0, End - Start); }
        }

        public string FormattedRange
        {
            get { return string.Format("{0} - {1}", FormatTime(Start), FormatTime(End)); }
        }

        public string CategoryName
        {
            get { return SponsorCategory.GetName(Category); }
        }

        public string ColorHex
        {
            get { return SponsorCategory.GetColorHex(Category); }
        }

        public static string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}:{2:D2}",
                    (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}",
                ts.Minutes, ts.Seconds);
        }
    }
}
