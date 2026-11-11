using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CefSharp;
using wb.Core;
using wb.History;
using wb.Downloads;

namespace wb.Browser.ClearBrowsingData
{
    public static class BrowsingDataCleaner
    {
        public static string GetCacheDirectory()
        {
            return StorageHelper.GetCacheDirectory();
        }

        public static int GetHistoryCount(ClearBrowsingDataTimeRange range)
        {
            try
            {
                DateTime cutoff = range.GetCutoffUtc();
                return AppServices.History.GetCountSince(cutoff);
            }
            catch
            {
                return 0;
            }
        }

        public static int GetDownloadsCount(ClearBrowsingDataTimeRange range)
        {
            try
            {
                DateTime cutoff = range.GetCutoffUtc();
                return AppServices.Downloads.GetCountSince(cutoff);
            }
            catch
            {
                return 0;
            }
        }

        public static long GetEstimatedCacheSizeBytes()
        {
            long total = 0;
            try
            {
                string cacheDir = GetCacheDirectory();
                if (Directory.Exists(cacheDir))
                {
                    total += CalculateDirectorySize(cacheDir);
                }

                // Also check legacy Cache_v133 if still present
                string legacyCache = Path.Combine(StorageHelper.GetDataDirectory(), "Cache_v133");
                if (Directory.Exists(legacyCache))
                {
                    string[] subdirs = new string[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "Service Worker" };
                    foreach (string sub in subdirs)
                    {
                        string dir = Path.Combine(legacyCache, sub);
                        if (Directory.Exists(dir))
                        {
                            total += CalculateDirectorySize(dir);
                        }
                    }
                }

                string faviconsDir = StorageHelper.GetFaviconsDirectory();
                if (Directory.Exists(faviconsDir))
                {
                    total += CalculateDirectorySize(faviconsDir);
                }
            }
            catch
            {
            }
            return total;
        }

        public static string FormatByteSize(long bytes)
        {
            if (bytes <= 0 || bytes < 1024L * 1024L)
            {
                return "Less than 1 MB";
            }
            if (bytes < 1024L * 1024L * 1024L)
            {
                return string.Format("{0:0.#} MB", bytes / (1024.0 * 1024.0));
            }
            return string.Format("{0:0.##} GB", bytes / (1024.0 * 1024.0 * 1024.0));
        }

        public static Task<ClearBrowsingDataResult> ClearDataAsync(ClearBrowsingDataOptions options)
        {
            if (options == null)
            {
                return Task.FromResult(new ClearBrowsingDataResult { Success = false, ErrorMessage = "Options cannot be null." });
            }

            return Task.Run(() => ClearData(options));
        }

        public static ClearBrowsingDataResult ClearData(ClearBrowsingDataOptions options)
        {
            if (options == null)
            {
                return new ClearBrowsingDataResult { Success = false, ErrorMessage = "Options cannot be null." };
            }

            ClearBrowsingDataResult result = new ClearBrowsingDataResult();
            DateTime cutoff = options.TimeRange.GetCutoffUtc();

            // 1. Browsing History
            if (options.ClearBrowsingHistory)
            {
                try
                {
                    result.HistoryItemsCleared = AppServices.History.ClearSince(cutoff);
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                }
            }

            // 2. Download History
            if (options.ClearDownloadHistory)
            {
                try
                {
                    result.DownloadItemsCleared = AppServices.Downloads.ClearSince(cutoff);
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                }
            }

            // 3. Cookies and Other Site Data
            if (options.ClearCookiesAndSiteData)
            {
                try
                {
                    ICookieManager cookieManager = GetGlobalCookieManagerSafe();
                    if (cookieManager != null && !cookieManager.IsDisposed)
                    {
                        if (options.TimeRange == ClearBrowsingDataTimeRange.AllTime)
                        {
                            cookieManager.DeleteCookies(string.Empty, string.Empty, new TaskDeleteCookiesCallback());
                        }
                        else
                        {
                            try
                            {
                                var visitor = new TaskCookieVisitor();
                                if (cookieManager.VisitAllCookies(visitor))
                                {
                                    if (visitor.Task.Wait(1500))
                                    {
                                        IList<Cookie> cookies = visitor.Task.Result;
                                        if (cookies != null)
                                        {
                                            for (int i = 0; i < cookies.Count; i++)
                                            {
                                                Cookie c = cookies[i];
                                                if (c != null && (c.Creation >= cutoff || c.LastAccess >= cutoff))
                                                {
                                                    string scheme = c.Secure ? "https://" : "http://";
                                                    string domain = (c.Domain ?? string.Empty).TrimStart('.');
                                                    string path = string.IsNullOrEmpty(c.Path) ? "/" : c.Path;
                                                    string url = scheme + domain + path;
                                                    cookieManager.DeleteCookies(url, c.Name, new TaskDeleteCookiesCallback());
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        cookieManager.FlushStore(new TaskCompletionCallback());
                    }

                    IRequestContext reqContext = GetGlobalRequestContextSafe();
                    if (reqContext != null && !reqContext.IsDisposed)
                    {
                        reqContext.ClearHttpAuthCredentials(new TaskCompletionCallback());
                        reqContext.ClearCertificateExceptions(new TaskCompletionCallback());
                    }
                    result.CookiesCleared = true;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                }
            }

            // 4. Cached Images and Files
            if (options.ClearCachedImagesAndFiles)
            {
                try
                {
                    AppServices.Favicons.ClearCache();

                    IRequestContext reqContext = GetGlobalRequestContextSafe();
                    if (reqContext != null && !reqContext.IsDisposed)
                    {
                        ClearHttpCacheSafe(reqContext);
                    }
                    result.CacheBytesFreed = GetEstimatedCacheSizeBytes();
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                }
            }

            // 5. Autofill & Search Cache
            if (options.ClearAutofillFormData)
            {
                result.AutofillCleared = true;
            }

            // 6. Site Settings & Permissions
            if (options.ClearSiteSettings)
            {
                try
                {
                    AppServices.SiteMute.Clear();
                    WebErrorBypassManager.Clear();
                    result.SiteSettingsCleared = true;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                }
            }

            // 7. Hosted App Data
            if (options.ClearHostedAppData)
            {
                try
                {
                    IRequestContext reqContext = GetGlobalRequestContextSafe();
                    if (reqContext != null && !reqContext.IsDisposed)
                    {
                        ClearHttpCacheSafe(reqContext);
                    }
                }
                catch
                {
                }
            }

            return result;
        }

        private static ICookieManager GetGlobalCookieManagerSafe()
        {
            try
            {
                Type cefType = FindCefType();
                if (cefType != null)
                {
                    PropertyInfo isInitProp = cefType.GetProperty("IsInitialized", BindingFlags.Public | BindingFlags.Static);
                    if (isInitProp != null && true.Equals(isInitProp.GetValue(null, null)))
                    {
                        MethodInfo method = cefType.GetMethod("GetGlobalCookieManager", new Type[] { typeof(ICompletionCallback) })
                                         ?? cefType.GetMethod("GetGlobalCookieManager", Type.EmptyTypes);
                        if (method != null)
                        {
                            object[] args = method.GetParameters().Length == 1 ? new object[] { null } : null;
                            return method.Invoke(null, args) as ICookieManager;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static void ClearHttpCacheSafe(IRequestContext reqContext)
        {
            if (reqContext == null) return;
            try
            {
                MethodInfo method = reqContext.GetType().GetMethod("ClearHttpCache", new Type[] { typeof(ICompletionCallback) })
                                 ?? reqContext.GetType().GetMethod("ClearHttpCache", Type.EmptyTypes);
                if (method != null)
                {
                    object[] args = method.GetParameters().Length == 1 ? new object[] { new TaskCompletionCallback() } : null;
                    method.Invoke(reqContext, args);
                }
            }
            catch { }
        }

        private static IRequestContext GetGlobalRequestContextSafe()
        {
            try
            {
                Type cefType = FindCefType();
                if (cefType != null)
                {
                    PropertyInfo isInitProp = cefType.GetProperty("IsInitialized", BindingFlags.Public | BindingFlags.Static);
                    if (isInitProp != null && true.Equals(isInitProp.GetValue(null, null)))
                    {
                        MethodInfo method = cefType.GetMethod("GetGlobalRequestContext", Type.EmptyTypes);
                        if (method != null)
                        {
                            return method.Invoke(null, null) as IRequestContext;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static Type FindCefType()
        {
            Type t = Type.GetType("CefSharp.Cef, wb.Core") ?? Type.GetType("CefSharp.Cef, CefSharp.Core");
            if (t != null) return t;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    t = assemblies[i].GetType("CefSharp.Cef");
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static long CalculateDirectorySize(string path)
        {
            long size = 0;
            try
            {
                DirectoryInfo dir = new DirectoryInfo(path);
                if (!dir.Exists) return 0;

                FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        size += files[i].Length;
                    }
                    catch { }
                }
            }
            catch { }
            return size;
        }
    }
}
