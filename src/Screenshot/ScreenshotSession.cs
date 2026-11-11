using System;
using System.Drawing;

namespace wb.Screenshot
{
    public class ScreenshotSession : IDisposable
    {
        private bool _disposed = false;

        public string Id { get; private set; }
        public Bitmap Image { get; set; }
        public string SourceUrl { get; set; }
        public string SourceTitle { get; set; }
        public DateTime CreatedAt { get; private set; }

        public ScreenshotSession(string id, Bitmap image, string sourceUrl, string sourceTitle)
        {
            Id = id ?? Guid.NewGuid().ToString("N");
            Image = image;
            SourceUrl = sourceUrl ?? string.Empty;
            SourceTitle = sourceTitle ?? "Screenshot";
            CreatedAt = DateTime.Now;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (Image != null)
                {
                    try { Image.Dispose(); } catch { }
                    Image = null;
                }
            }
        }
    }
}
