using System;

namespace wb.History
{
    [Serializable]
    public class HistoryItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public DateTime VisitedAt { get; set; }

        public DateTime LocalVisitedAt
        {
            get
            {
                return VisitedAt.Kind == DateTimeKind.Utc ? VisitedAt.ToLocalTime() : VisitedAt;
            }
        }

        public HistoryItem()
        {
            Id = Guid.NewGuid().ToString("N");
            VisitedAt = DateTime.Now;
        }

        public HistoryItem(string title, string url)
        {
            Id = Guid.NewGuid().ToString("N");
            Title = string.IsNullOrEmpty(title) ? url : title;
            Url = url;
            VisitedAt = DateTime.Now;
        }
    }
}
