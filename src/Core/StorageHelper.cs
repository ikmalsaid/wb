using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace wb.Core
{
    public static class StorageHelper
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly object _tempHtmlLock = new object();
        private static readonly object _initLock = new object();
        private static string _cachedDataDir;
        private static bool _initialized = false;

        public static string CustomDataDirectory { get; set; }
        public static bool IsPortableMode { get; private set; }

        public static void Initialize(string[] args)
        {
            lock (_initLock)
            {
                if (_initialized) return;

                // 1. Parse command-line switches for user-data-dir and portable mode
                if (args != null && args.Length > 0)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        string arg = args[i];
                        if (string.IsNullOrEmpty(arg)) continue;

                        if (arg.StartsWith("--user-data-dir=", StringComparison.OrdinalIgnoreCase) ||
                            arg.StartsWith("-user-data-dir=", StringComparison.OrdinalIgnoreCase))
                        {
                            int eqIdx = arg.IndexOf('=');
                            string path = arg.Substring(eqIdx + 1).Trim('"', '\'');
                            if (!string.IsNullOrEmpty(path))
                            {
                                CustomDataDirectory = Path.GetFullPath(path);
                                IsPortableMode = true;
                                break;
                            }
                        }
                        else if ((arg.Equals("--user-data-dir", StringComparison.OrdinalIgnoreCase) ||
                                  arg.Equals("-user-data-dir", StringComparison.OrdinalIgnoreCase)) &&
                                 i + 1 < args.Length)
                        {
                            string path = args[i + 1].Trim('"', '\'');
                            if (!string.IsNullOrEmpty(path))
                            {
                                CustomDataDirectory = Path.GetFullPath(path);
                                IsPortableMode = true;
                                i++;
                                break;
                            }
                        }
                        else if (arg.Equals("--portable", StringComparison.OrdinalIgnoreCase) ||
                                 arg.Equals("-portable", StringComparison.OrdinalIgnoreCase))
                        {
                            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                            CustomDataDirectory = Path.Combine(baseDir, "data");
                            IsPortableMode = true;
                            break;
                        }
                    }
                }

                // 2. Auto-detection: If portable marker (portable.dat) or local data/ folder exists next to executable
                if (string.IsNullOrEmpty(CustomDataDirectory))
                {
                    try
                    {
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        string localDataDir = Path.Combine(baseDir, "data");
                        string portableDat = Path.Combine(baseDir, "portable.dat");

                        if (Directory.Exists(localDataDir) || File.Exists(portableDat))
                        {
                            CustomDataDirectory = localDataDir;
                            IsPortableMode = true;
                        }
                    }
                    catch { }
                }

                _cachedDataDir = null; // Invalidate cached directory
                EnsureSubdirectories();
                MigrateLegacyData();
                _initialized = true;
            }
        }

        public static string GetDataDirectory()
        {
            if (!string.IsNullOrEmpty(CustomDataDirectory))
            {
                if (!Directory.Exists(CustomDataDirectory))
                {
                    Directory.CreateDirectory(CustomDataDirectory);
                }
                return CustomDataDirectory;
            }

            if (_cachedDataDir != null)
            {
                return _cachedDataDir;
            }

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppStrings.AppName);

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _cachedDataDir = dir;
            return dir;
        }

        public static string GetConfigDirectory()
        {
            string dir = Path.Combine(GetDataDirectory(), "config");
            EnsureDirectoryExists(dir);
            return dir;
        }

        public static string GetProfileDirectory()
        {
            string dir = Path.Combine(GetDataDirectory(), "profile");
            EnsureDirectoryExists(dir);
            return dir;
        }

        public static string GetCacheDirectory()
        {
            string dir = Path.Combine(GetDataDirectory(), "cache");
            EnsureDirectoryExists(dir);
            return dir;
        }

        public static string GetFaviconsDirectory()
        {
            string dir = Path.Combine(GetDataDirectory(), "favicons");
            EnsureDirectoryExists(dir);
            return dir;
        }

        public static string GetFiltersDirectory()
        {
            string dir = Path.Combine(GetDataDirectory(), "filters");
            EnsureDirectoryExists(dir);
            return dir;
        }

        private static void EnsureSubdirectories()
        {
            try
            {
                EnsureDirectoryExists(GetConfigDirectory());
                EnsureDirectoryExists(GetProfileDirectory());
                EnsureDirectoryExists(GetCacheDirectory());
                EnsureDirectoryExists(GetFaviconsDirectory());
                EnsureDirectoryExists(GetFiltersDirectory());
            }
            catch { }
        }

        public static void MigrateLegacyData()
        {
            try
            {
                string rootDir = GetDataDirectory();
                string configDir = GetConfigDirectory();
                string profileDir = GetProfileDirectory();
                string cacheDir = GetCacheDirectory();
                string faviconsDir = GetFaviconsDirectory();
                string filtersDir = GetFiltersDirectory();

                // 1. Move legacy root JSON configuration files into config/
                if (Directory.Exists(rootDir))
                {
                    string[] jsonFiles = Directory.GetFiles(rootDir, "*.json", SearchOption.TopDirectoryOnly);
                    foreach (string srcJson in jsonFiles)
                    {
                        string fileName = Path.GetFileName(srcJson);
                        string destJson = Path.Combine(configDir, fileName);
                        try
                        {
                            if (!File.Exists(destJson))
                            {
                                File.Move(srcJson, destJson);
                            }
                            else
                            {
                                File.Delete(srcJson);
                            }
                        }
                        catch { }
                    }
                }

                // 2. Normalize legacy uppercase Favicons directory to favicons/
                string legacyFavicons = Path.Combine(rootDir, "Favicons");
                if (Directory.Exists(legacyFavicons) && !legacyFavicons.Equals(faviconsDir, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string[] favFiles = Directory.GetFiles(legacyFavicons);
                        foreach (string f in favFiles)
                        {
                            string dest = Path.Combine(faviconsDir, Path.GetFileName(f));
                            if (!File.Exists(dest)) File.Move(f, dest);
                        }
                        Directory.Delete(legacyFavicons, true);
                    }
                    catch { }
                }

                // 3. Migrate legacy Cache_v133 if profile/ does not yet have session databases
                string legacyCache = Path.Combine(rootDir, "Cache_v133");
                if (Directory.Exists(legacyCache))
                {
                    try
                    {
                        string localState = Path.Combine(profileDir, "Local State");
                        if (!File.Exists(localState))
                        {
                            string legacyLocalState = Path.Combine(legacyCache, "Local State");
                            if (File.Exists(legacyLocalState))
                            {
                                File.Copy(legacyLocalState, localState, true);
                            }
                        }

                        // Move cache subdirectories to cache/
                        string[] cacheFolders = new string[] { "Cache", "Code Cache", "GPUCache", "DawnCache" };
                        foreach (string sub in cacheFolders)
                        {
                            string srcSub = Path.Combine(legacyCache, sub);
                            string dstSub = Path.Combine(cacheDir, sub);
                            if (Directory.Exists(srcSub) && !Directory.Exists(dstSub))
                            {
                                Directory.Move(srcSub, dstSub);
                            }
                        }

                        // Move profile subdirectories to profile/
                        string[] profileFolders = new string[] { "Local Storage", "IndexedDB", "Session Storage", "Network" };
                        foreach (string sub in profileFolders)
                        {
                            string srcSub = Path.Combine(legacyCache, sub);
                            string dstSub = Path.Combine(profileDir, sub);
                            if (Directory.Exists(srcSub) && !Directory.Exists(dstSub))
                            {
                                Directory.Move(srcSub, dstSub);
                            }
                        }

                        // Move top-level database files like Cookies, Preferences
                        string[] profileFiles = new string[] { "Cookies", "Cookies-journal", "Preferences" };
                        foreach (string pf in profileFiles)
                        {
                            string srcPf = Path.Combine(legacyCache, pf);
                            string dstPf = Path.Combine(profileDir, pf);
                            if (File.Exists(srcPf) && !File.Exists(dstPf))
                            {
                                File.Move(srcPf, dstPf);
                            }
                        }
                    }
                    catch { }
                }

                // 4. Remove unwanted Chromium-layout "User Data" directory (e.g. User Data\Crashpad).
                // wb strictly uses the %LOCALAPPDATA%\wb\crashes directory layout instead of Chromium's layout.
                string userDataDir = Path.Combine(rootDir, "User Data");
                if (Directory.Exists(userDataDir))
                {
                    try
                    {
                        Directory.Delete(userDataDir, true);
                    }
                    catch { }
                }

                // 5. Clean up any accidental or legacy "cache" folder created in application binary directory
                try
                {
                    string binCacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");
                    if (Directory.Exists(binCacheDir))
                    {
                        Directory.Delete(binCacheDir, true);
                    }
                }
                catch { }
            }
            catch { }
        }

        public static bool EnsureDirectoryExists(string dirPath)
        {
            if (string.IsNullOrEmpty(dirPath)) return false;
            try
            {
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool WriteTextSafe(string filePath, string content, Encoding encoding = null)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            if (encoding == null) encoding = Encoding.UTF8;

            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tempPath, content ?? string.Empty, encoding);

                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                    File.Move(tempPath, filePath);
                }
                catch
                {
                    File.WriteAllText(filePath, content ?? string.Empty, encoding);
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }

                return true;
            }
            catch
            {
                try
                {
                    File.WriteAllText(filePath, content ?? string.Empty, encoding);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static string CreateTempHtmlFile(string subfolder, string prefix, string htmlContent)
        {
            try
            {
                string tempRoot = Path.Combine(Path.GetTempPath(), AppStrings.AppName ?? "wb");
                string targetDir = !string.IsNullOrEmpty(subfolder) ? Path.Combine(tempRoot, subfolder) : tempRoot;
                lock (_tempHtmlLock)
                {
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    else
                    {
                        // Periodically purge stale temporary html files older than 1 hour to prevent accumulation
                        try
                        {
                            string[] existingFiles = Directory.GetFiles(targetDir, "*.html");
                            DateTime cutoff = DateTime.UtcNow.AddHours(-1);
                            foreach (string f in existingFiles)
                            {
                                try
                                {
                                    if (File.GetLastWriteTimeUtc(f) < cutoff)
                                    {
                                        File.Delete(f);
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }

                string safePrefix = !string.IsNullOrEmpty(prefix) ? prefix : "temp";
                string fileName = string.Format("{0}_{1}.html", safePrefix, Guid.NewGuid().ToString("N"));
                string filePath = Path.Combine(targetDir, fileName);
                File.WriteAllText(filePath, htmlContent ?? string.Empty, Encoding.UTF8);
                return filePath;
            }
            catch
            {
                try
                {
                    string safePrefix = !string.IsNullOrEmpty(prefix) ? prefix : "temp";
                    string tempRoot = Path.Combine(Path.GetTempPath(), AppStrings.AppName ?? "wb");
                    if (!Directory.Exists(tempRoot)) Directory.CreateDirectory(tempRoot);
                    string filePath = Path.Combine(tempRoot, string.Format("{0}_{1}.html", safePrefix, Guid.NewGuid().ToString("N")));
                    File.WriteAllText(filePath, htmlContent ?? string.Empty, Encoding.UTF8);
                    return filePath;
                }
                catch
                {
                    return "wb://newtab";
                }
            }
        }

        public static string Serialize<T>(T obj)
        {
            if (obj == null) return string.Empty;
            try
            {
                return Serializer.Serialize(obj);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default(T);
            try
            {
                return Serializer.Deserialize<T>(json);
            }
            catch
            {
                return default(T);
            }
        }

        public static object DeserializeObject(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return Serializer.DeserializeObject(json);
            }
            catch
            {
                return null;
            }
        }

        public static List<T> LoadList<T>(string fileName)
        {
            try
            {
                // Check in config subfolder first
                string configPath = Path.Combine(GetConfigDirectory(), fileName);
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    if (!string.IsNullOrEmpty(json))
                    {
                        T[] array = Serializer.Deserialize<T[]>(json);
                        if (array != null) return new List<T>(array);
                    }
                }

                // Fallback to legacy root folder and migrate if found
                string legacyPath = Path.Combine(GetDataDirectory(), fileName);
                if (File.Exists(legacyPath))
                {
                    string json = File.ReadAllText(legacyPath);
                    if (!string.IsNullOrEmpty(json))
                    {
                        T[] array = Serializer.Deserialize<T[]>(json);
                        if (array != null)
                        {
                            List<T> list = new List<T>(array);
                            SaveList(fileName, list); // Saves to config/ and deletes legacy
                            return list;
                        }
                    }
                }
            }
            catch { }
            return new List<T>();
        }

        public static bool SaveList<T>(string fileName, List<T> list)
        {
            try
            {
                string configPath = Path.Combine(GetConfigDirectory(), fileName);
                string json = Serializer.Serialize(list);
                bool success = WriteTextSafe(configPath, json);

                // Clean legacy root file if present
                try
                {
                    string legacyPath = Path.Combine(GetDataDirectory(), fileName);
                    if (File.Exists(legacyPath)) File.Delete(legacyPath);
                }
                catch { }

                return success;
            }
            catch
            {
                return false;
            }
        }

        public static T LoadObject<T>(string fileName) where T : class, new()
        {
            try
            {
                // Check in config subfolder first
                string configPath = Path.Combine(GetConfigDirectory(), fileName);
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    if (!string.IsNullOrEmpty(json))
                    {
                        T obj = Serializer.Deserialize<T>(json);
                        if (obj != null) return obj;
                    }
                }

                // Fallback to legacy root folder and migrate if found
                string legacyPath = Path.Combine(GetDataDirectory(), fileName);
                if (File.Exists(legacyPath))
                {
                    string json = File.ReadAllText(legacyPath);
                    if (!string.IsNullOrEmpty(json))
                    {
                        T obj = Serializer.Deserialize<T>(json);
                        if (obj != null)
                        {
                            SaveObject(fileName, obj); // Saves to config/ and deletes legacy
                            return obj;
                        }
                    }
                }
            }
            catch { }
            return new T();
        }

        public static bool SaveObject<T>(string fileName, T obj)
        {
            try
            {
                string configPath = Path.Combine(GetConfigDirectory(), fileName);
                string json = Serializer.Serialize(obj);
                bool success = WriteTextSafe(configPath, json);

                // Clean legacy root file if present
                try
                {
                    string legacyPath = Path.Combine(GetDataDirectory(), fileName);
                    if (File.Exists(legacyPath)) File.Delete(legacyPath);
                }
                catch { }

                return success;
            }
            catch
            {
                return false;
            }
        }
    }
}
