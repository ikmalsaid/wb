using System;

namespace wb.Bookmarks
{
    [Serializable]
    public class BookmarkItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ParentId { get; set; }
        public bool IsFolder { get; set; }

        public DateTime LocalCreatedAt
        {
            get
            {
                return CreatedAt.Kind == DateTimeKind.Utc ? CreatedAt.ToLocalTime() : CreatedAt;
            }
        }

        public BookmarkItem()
        {
            Id = Guid.NewGuid().ToString("N");
            CreatedAt = DateTime.Now;
            ParentId = "bar";
            IsFolder = false;
        }

        public BookmarkItem(string title, string url, string parentId = null)
        {
            Id = Guid.NewGuid().ToString("N");
            Title = string.IsNullOrEmpty(title) ? url : title;
            Url = url;
            CreatedAt = DateTime.Now;
            ParentId = string.IsNullOrEmpty(parentId) ? "bar" : parentId;
            IsFolder = false;
        }

        public static BookmarkItem CreateFolder(string title, string parentId = null)
        {
            return new BookmarkItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = title ?? string.Empty,
                Url = string.Empty,
                CreatedAt = DateTime.Now,
                ParentId = string.IsNullOrEmpty(parentId) ? "bar" : parentId,
                IsFolder = true
            };
        }
    }
}
