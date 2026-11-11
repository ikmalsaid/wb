using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using CefSharp;
using wb.Core;

namespace wb.Downloads
{
    public class DownloadHandler : IDownloadHandler
    {
        public event EventHandler<DownloadItem> DownloadUpdated;
        private readonly Dictionary<int, string> _targetPaths = new Dictionary<int, string>();

        public bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod)
        {
            return true;
        }

        public bool OnBeforeDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IBeforeDownloadCallback callback)
        {
            if (callback != null && !callback.IsDisposed)
            {
                using (callback)
                {
                    string defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (!Directory.Exists(defaultFolder))
                    {
                        defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    }

                    string suggestedPath = DownloadManager.GetUniqueFilePath(defaultFolder, downloadItem.SuggestedFileName);
                    string wbdownloadPath = suggestedPath + DownloadItemModel.WbDownloadExtension;

                    lock (_targetPaths)
                    {
                        _targetPaths[downloadItem.Id] = suggestedPath;
                    }

                    // Register with central download manager
                    DownloadItemModel model = AppServices.Downloads.RegisterDownload(
                        downloadItem.Id,
                        Path.GetFileName(suggestedPath),
                        suggestedPath,
                        downloadItem.Url,
                        downloadItem.TotalBytes);

                    // Chrome/wb default: destination file is saved as .wbdownload in transit,
                    // and will be atomically renamed to suggestedPath when 100% completed.
                    callback.Continue(wbdownloadPath, showDialog: false);
                }
            }
            return true;
        }

        public void OnDownloadUpdated(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IDownloadItemCallback callback)
        {
            string state = "InProgress";
            if (downloadItem.IsComplete)
            {
                state = "Completed";

                string targetPath = null;
                lock (_targetPaths)
                {
                    if (_targetPaths.TryGetValue(downloadItem.Id, out targetPath))
                    {
                        _targetPaths.Remove(downloadItem.Id);
                    }
                }

                if (string.IsNullOrEmpty(targetPath))
                {
                    var existing = AppServices.Downloads.GetItem(downloadItem.Id);
                    if (existing != null) targetPath = existing.FilePath;
                }

                if (string.IsNullOrEmpty(targetPath) && !string.IsNullOrEmpty(downloadItem.FullPath))
                {
                    if (downloadItem.FullPath.EndsWith(DownloadItemModel.WbDownloadExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        targetPath = downloadItem.FullPath.Substring(0, downloadItem.FullPath.Length - DownloadItemModel.WbDownloadExtension.Length);
                    }
                }

                if (!string.IsNullOrEmpty(targetPath))
                {
                    string wbdownloadPath = targetPath + DownloadItemModel.WbDownloadExtension;
                    if (!File.Exists(wbdownloadPath) && !string.IsNullOrEmpty(downloadItem.FullPath) && File.Exists(downloadItem.FullPath))
                    {
                        wbdownloadPath = downloadItem.FullPath;
                    }

                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        for (int retry = 0; retry < 25; retry++)
                        {
                            try
                            {
                                if (File.Exists(wbdownloadPath))
                                {
                                    if (File.Exists(targetPath))
                                    {
                                        try { File.Delete(targetPath); } catch { }
                                    }
                                    File.Move(wbdownloadPath, targetPath);
                                }
                                break;
                            }
                            catch (IOException)
                            {
                                System.Threading.Thread.Sleep(100);
                            }
                            catch
                            {
                                break;
                            }
                        }
                    });
                }
            }
            else if (downloadItem.IsCancelled)
            {
                state = "Cancelled";
                string targetPath = null;
                lock (_targetPaths)
                {
                    if (_targetPaths.TryGetValue(downloadItem.Id, out targetPath))
                    {
                        _targetPaths.Remove(downloadItem.Id);
                    }
                }

                string wbPath = !string.IsNullOrEmpty(targetPath) ? targetPath + DownloadItemModel.WbDownloadExtension : null;
                if (string.IsNullOrEmpty(wbPath) && !string.IsNullOrEmpty(downloadItem.FullPath))
                {
                    wbPath = downloadItem.FullPath.EndsWith(DownloadItemModel.WbDownloadExtension, StringComparison.OrdinalIgnoreCase)
                        ? downloadItem.FullPath
                        : downloadItem.FullPath + DownloadItemModel.WbDownloadExtension;
                }

                if (!string.IsNullOrEmpty(wbPath))
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        for (int retry = 0; retry < 20; retry++)
                        {
                            try
                            {
                                System.Threading.Thread.Sleep(100);
                                if (File.Exists(wbPath))
                                {
                                    File.Delete(wbPath);
                                }
                                break;
                            }
                            catch (IOException) { }
                            catch { break; }
                        }
                    });
                }
            }
            else if (downloadItem.IsInProgress)
            {
                state = "InProgress";
            }
            else
            {
                // Not in progress, not complete, not cancelled -> paused, disk full, network error, or interrupted
                bool isDiskFull = false;
                try
                {
                    string target = !string.IsNullOrEmpty(downloadItem.FullPath) ? downloadItem.FullPath : downloadItem.SuggestedFileName;
                    if (!string.IsNullOrEmpty(target))
                    {
                        string root = Path.GetPathRoot(Path.GetFullPath(target));
                        if (!string.IsNullOrEmpty(root))
                        {
                            DriveInfo drive = new DriveInfo(root);
                            if (drive.IsReady && drive.AvailableFreeSpace < 4096)
                            {
                                isDiskFull = true;
                            }
                        }
                    }
                }
                catch
                {
                }

                if (isDiskFull)
                {
                    state = "Failed - Disk full";
                }
                else if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                {
                    state = "Failed - Network error";
                }
                else
                {
                    var existing = AppServices.Downloads.GetItem(downloadItem.Id);
                    if (existing != null && string.Equals(existing.State, "Paused", StringComparison.OrdinalIgnoreCase))
                    {
                        state = "Paused";
                    }
                    else
                    {
                        state = "Interrupted";
                    }
                }
            }

            // Register pause/resume/cancel callbacks
            if (callback != null && !callback.IsDisposed)
            {
                AppServices.Downloads.RegisterCallbacks(
                    downloadItem.Id,
                    () => { try { if (callback != null && !callback.IsDisposed) callback.Cancel(); } catch { } },
                    () => { try { if (callback != null && !callback.IsDisposed) callback.Pause(); } catch { } },
                    () => { try { if (callback != null && !callback.IsDisposed) callback.Resume(); } catch { } }
                );
            }

            // Update central download manager
            AppServices.Downloads.UpdateProgress(
                downloadItem.Id,
                downloadItem.ReceivedBytes,
                downloadItem.TotalBytes,
                downloadItem.PercentComplete,
                state,
                downloadItem.CurrentSpeed);

            EventHandler<DownloadItem> handler = DownloadUpdated;
            if (handler != null)
            {
                handler(this, downloadItem);
            }
        }
    }
}
