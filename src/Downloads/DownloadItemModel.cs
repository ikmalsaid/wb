using System;
using System.IO;

namespace wb.Downloads
{
    [Serializable]
    public class DownloadItemModel
    {
        public const string WbDownloadExtension = ".wbdownload";

        public long Id { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string Url { get; set; }
        public long TotalBytes { get; set; }
        public long ReceivedBytes { get; set; }
        public int Percent { get; set; }
        public string State { get; set; }
        public string FailureReason { get; set; }
        public double CurrentSpeed { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        public string WbDownloadPath
        {
            get
            {
                if (string.IsNullOrEmpty(FilePath)) return null;
                return FilePath + WbDownloadExtension;
            }
        }

        public DateTime LocalStartTime
        {
            get
            {
                return StartTime.Kind == DateTimeKind.Utc ? StartTime.ToLocalTime() : StartTime;
            }
        }

        public DateTime? LocalEndTime
        {
            get
            {
                if (!EndTime.HasValue) return null;
                return EndTime.Value.Kind == DateTimeKind.Utc ? EndTime.Value.ToLocalTime() : EndTime.Value;
            }
        }

        public bool CheckFileExists()
        {
            if (string.IsNullOrEmpty(FilePath)) return false;
            try
            {
                return File.Exists(FilePath);
            }
            catch
            {
                return false;
            }
        }

        public bool CheckWbDownloadExists()
        {
            if (string.IsNullOrEmpty(WbDownloadPath)) return false;
            try
            {
                return File.Exists(WbDownloadPath);
            }
            catch
            {
                return false;
            }
        }

        public long GetWbDownloadSize()
        {
            if (CheckWbDownloadExists())
            {
                try
                {
                    return new FileInfo(WbDownloadPath).Length;
                }
                catch
                {
                }
            }
            return 0;
        }

        public string GetStatusDisplay()
        {
            if (string.Equals(State, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                if (!CheckFileExists())
                {
                    return "Missing (Deleted)";
                }
                return "Completed";
            }
            if (string.Equals(State, "Failed - Network error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(FailureReason, "NetworkError", StringComparison.OrdinalIgnoreCase))
            {
                return "Failed - Network error";
            }
            if (string.Equals(State, "Failed - Disk full", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(FailureReason, "DiskFull", StringComparison.OrdinalIgnoreCase))
            {
                return "Failed - Disk full";
            }
            if (string.Equals(State, "Interrupted", StringComparison.OrdinalIgnoreCase))
            {
                return "Interrupted";
            }
            if (string.Equals(State, "Paused", StringComparison.OrdinalIgnoreCase))
            {
                return "Paused";
            }
            if (string.Equals(State, "Queued", StringComparison.OrdinalIgnoreCase))
            {
                return "Queued";
            }
            if (string.Equals(State, "InProgress", StringComparison.OrdinalIgnoreCase))
            {
                return "Downloading";
            }
            return State ?? "Unknown";
        }

        public DownloadItemModel()
        {
            StartTime = DateTime.Now;
            State = "InProgress";
            CurrentSpeed = 0;
        }

        public static string FormatFileSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            const long kb = 1024L;
            const long mb = 1024L * 1024L;
            const long gb = 1024L * 1024L * 1024L;

            long roundedMb = (long)Math.Round((double)bytes / mb);
            if (bytes >= gb || roundedMb >= 1024)
            {
                long g = (long)Math.Round((double)bytes / gb);
                if (g < 1) g = 1;
                return g + " GB";
            }

            long roundedKb = (long)Math.Round((double)bytes / kb);
            if (bytes >= mb || roundedKb >= 1024)
            {
                long m = (long)Math.Round((double)bytes / mb);
                if (m < 1) m = 1;
                return m + " MB";
            }

            if (bytes >= kb)
            {
                long k = (long)Math.Round((double)bytes / kb);
                if (k < 1) k = 1;
                return k + " KB";
            }

            return bytes + " B";
        }

        public string GetFormattedSize()
        {
            if (TotalBytes <= 0)
            {
                return FormatFileSize(ReceivedBytes);
            }
            if (ReceivedBytes >= TotalBytes)
            {
                return FormatFileSize(TotalBytes);
            }
            return string.Format("{0} / {1}",
                FormatFileSize(ReceivedBytes),
                FormatFileSize(TotalBytes));
        }

        public string GetFormattedSpeed()
        {
            if (CurrentSpeed <= 0) return "-- KB/s";
            if (CurrentSpeed < 1024 * 1024)
            {
                return string.Format("{0:N1} KB/s", CurrentSpeed / 1024.0);
            }
            return string.Format("{0:N2} MB/s", CurrentSpeed / (1024.0 * 1024.0));
        }

        public string GetFormattedEta()
        {
            if (CurrentSpeed <= 0 || TotalBytes <= 0 || ReceivedBytes >= TotalBytes)
            {
                return "--";
            }
            long remainingBytes = TotalBytes - ReceivedBytes;
            double seconds = (double)remainingBytes / CurrentSpeed;
            if (seconds > 3600)
            {
                int hrs = (int)(seconds / 3600);
                int mins = (int)((seconds % 3600) / 60);
                return string.Format("{0}h {1}m", hrs, mins);
            }
            else if (seconds > 60)
            {
                int mins = (int)(seconds / 60);
                int secs = (int)(seconds % 60);
                return string.Format("{0}m {1}s", mins, secs);
            }
            else
            {
                return string.Format("{0}s", Math.Max(1, (int)seconds));
            }
        }
    }
}
