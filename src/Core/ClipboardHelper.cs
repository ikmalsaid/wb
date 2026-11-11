using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace wb.Core
{
    /// <summary>
    /// Thread-safe and STA-safe helper for Windows Clipboard operations.
    /// </summary>
    public static class ClipboardHelper
    {
        public static void SetText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            RunInSta(delegate()
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                }
                catch
                {
                }
            });
        }

        public static string GetText()
        {
            string result = string.Empty;
            RunInSta(delegate()
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        result = Clipboard.GetText();
                    }
                }
                catch
                {
                }
            });
            return result;
        }

        public static bool ContainsText()
        {
            bool result = false;
            RunInSta(delegate()
            {
                try
                {
                    result = Clipboard.ContainsText();
                }
                catch
                {
                }
            });
            return result;
        }

        public static void SetImage(Image image)
        {
            if (image == null) return;
            RunInSta(delegate()
            {
                try
                {
                    Clipboard.SetImage(image);
                }
                catch
                {
                }
            });
        }

        public static void CopyImageFromUrl(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl)) return;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    using (WebClient wc = new WebClient())
                    {
                        byte[] bytes = wc.DownloadData(imageUrl);
                        using (MemoryStream ms = new MemoryStream(bytes))
                        using (Image img = Image.FromStream(ms))
                        {
                            Bitmap bmp = new Bitmap(img);
                            SetImage(bmp);
                        }
                    }
                }
                catch
                {
                }
            });
        }

        public static void RunInSta(Action action)
        {
            if (action == null) return;
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                try
                {
                    action();
                }
                catch
                {
                }
            }
            else
            {
                Thread staThread = new Thread(delegate()
                {
                    try
                    {
                        action();
                    }
                    catch
                    {
                    }
                });
                staThread.SetApartmentState(ApartmentState.STA);
                staThread.IsBackground = true;
                staThread.Start();
                staThread.Join(500);
            }
        }
    }
}
