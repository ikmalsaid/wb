using System;
using System.Collections.Generic;
using System.Drawing;

namespace wb.Screenshot
{
    public static class ScreenshotSessionManager
    {
        private static readonly object _syncLock = new object();
        private static readonly Dictionary<string, ScreenshotSession> _sessions = new Dictionary<string, ScreenshotSession>(StringComparer.OrdinalIgnoreCase);

        public static ScreenshotSession CreateSession(Bitmap image, string sourceUrl, string sourceTitle)
        {
            if (image == null) return null;
            string id = Guid.NewGuid().ToString("N");
            ScreenshotSession session = new ScreenshotSession(id, image, sourceUrl, sourceTitle);
            lock (_syncLock)
            {
                _sessions[id] = session;
            }
            return session;
        }

        public static ScreenshotSession GetSession(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_syncLock)
            {
                ScreenshotSession session;
                if (_sessions.TryGetValue(id, out session))
                {
                    return session;
                }
            }
            return null;
        }

        public static void RemoveSession(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            ScreenshotSession session = null;
            lock (_syncLock)
            {
                if (_sessions.TryGetValue(id, out session))
                {
                    _sessions.Remove(id);
                }
            }
            if (session != null)
            {
                session.Dispose();
            }
        }
    }
}
