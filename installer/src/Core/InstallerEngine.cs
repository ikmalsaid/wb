using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace wb.Installer
{
    /// <summary>
    /// ikmalsaid's Carbon Install System engine: payload extraction, process lifecycle management,
    /// safe cross-architecture wiping, Windows shortcuts, registry registrations, and self-cleaning.
    /// Fully configurable through InstallerConfig.
    /// </summary>
    public static class InstallerEngine
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        public static Version ParseVersionSafe(string vStr)
        {
            if (string.IsNullOrEmpty(vStr)) return new Version(0, 0, 0, 0);
            string clean = vStr.Trim().TrimStart('v', 'V');
            int dashIdx = clean.IndexOf('-');
            if (dashIdx > 0) clean = clean.Substring(0, dashIdx);
            int spaceIdx = clean.IndexOf(' ');
            if (spaceIdx > 0) clean = clean.Substring(0, spaceIdx);

            string[] parts = clean.Split('.');
            System.Collections.Generic.List<int> nums = new System.Collections.Generic.List<int>();
            for (int i = 0; i < parts.Length && i < 4; i++)
            {
                int n;
                if (int.TryParse(parts[i], out n)) nums.Add(Math.Max(0, n));
                else break;
            }
            while (nums.Count < 4) nums.Add(0);

            return new Version(nums[0], nums[1], nums[2], nums[3]);
        }

        public static Version GetPackageVersion()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                Version v = asm.GetName().Version;
                if (v != null && (v.Major > 0 || v.Minor > 0 || v.Build > 0))
                {
                    return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
                }
            }
            catch { }

            try
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Application.ExecutablePath);
                string vStr = fvi.ProductVersion;
                if (string.IsNullOrEmpty(vStr)) vStr = fvi.FileVersion;
                if (!string.IsNullOrEmpty(vStr))
                {
                    return ParseVersionSafe(vStr);
                }
            }
            catch { }

            return new Version(1, 0, 0, 0);
        }

        public static string GetPackageVersionString()
        {
            Version v = GetPackageVersion();
            if (v.Revision > 0)
                return string.Format("{0}.{1}.{2}.{3}", v.Major, v.Minor, Math.Max(0, v.Build), v.Revision);
            return string.Format("{0}.{1}.{2}", v.Major, v.Minor, Math.Max(0, v.Build));
        }

        public static string CurrentArchitecture
        {
            get { return IntPtr.Size == 8 ? "x64" : "x86"; }
        }

        public static string GetPeArchitecture(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return "Unknown";
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (fs.Length < 64) return "Unknown";
                    ushort dosHeader = br.ReadUInt16();
                    if (dosHeader != 0x5A4D) return "Unknown"; // 'MZ'
                    fs.Seek(0x3C, SeekOrigin.Begin);
                    int peOffset = br.ReadInt32();
                    if (peOffset < 0 || peOffset + 6 > fs.Length) return "Unknown";
                    fs.Seek(peOffset, SeekOrigin.Begin);
                    uint peSig = br.ReadUInt32(); // 'PE\0\0'
                    if (peSig != 0x00004550) return "Unknown";
                    ushort machine = br.ReadUInt16();
                    if (machine == 0x014c) return "x86";
                    if (machine == 0x8664) return "x64";
                    if (machine == 0xAA64) return "ARM64";
                }
            }
            catch { }
            return "Unknown";
        }

        public static string GetDefaultInstallDirectory(InstallerConfig config = null)
        {
            config = config ?? InstallerConfig.Current;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Programs", config.DefaultInstallFolderName);
        }

        public static string GetStandaloneDirectory(InstallerConfig config = null)
        {
            config = config ?? InstallerConfig.Current;
            string folderName = config != null ? config.GetStandaloneTempFolderName() : ("wb_standalone_" + CurrentArchitecture);
            return Path.Combine(Path.GetTempPath(), folderName);
        }

        public static bool IsStandaloneExtracted(InstallerConfig config = null)
        {
            config = config ?? InstallerConfig.Current;
            string tempDir = GetStandaloneDirectory(config);
            string mainExe = Path.Combine(tempDir, config.MainExecutable);
            return File.Exists(mainExe);
        }

        public static bool HasStandaloneUserData(InstallerConfig config = null)
        {
            config = config ?? InstallerConfig.Current;
            string tempDir = GetStandaloneDirectory(config);
            string dataDir = Path.Combine(tempDir, "data");
            if (!Directory.Exists(dataDir)) return false;
            try
            {
                return Directory.GetFileSystemEntries(dataDir).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static void ClearStandaloneUserData(InstallerConfig config = null)
        {
            config = config ?? InstallerConfig.Current;
            string tempDir = GetStandaloneDirectory(config);
            string dataDir = Path.Combine(tempDir, "data");
            if (Directory.Exists(dataDir))
            {
                try
                {
                    Directory.Delete(dataDir, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Installer] Note on clear data delete: " + ex.Message);
                    try
                    {
                        foreach (string f in Directory.GetFiles(dataDir, "*.*", SearchOption.AllDirectories))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                    catch { }
                }
            }
            if (!Directory.Exists(dataDir))
            {
                try { Directory.CreateDirectory(dataDir); } catch { }
            }
        }

        public static string LaunchStandalone(
            BackgroundWorker progress,
            InstallerConfig config = null,
            bool overwrite = false,
            bool clearUserData = false)
        {
            config = config ?? InstallerConfig.Current;
            try
            {
                string tempDir = GetStandaloneDirectory(config);
                string mainExe = Path.Combine(tempDir, config.MainExecutable);
                bool mainExeExists = File.Exists(mainExe);

                // Stop any running application processes if modifying files or data
                if (overwrite || clearUserData || !mainExeExists)
                {
                    CloseRunningProcesses(config);
                }

                // If user requested to clear existing user data
                if (clearUserData)
                {
                    ReportProgress(progress, 10, "Clearing existing user data...");
                    ClearStandaloneUserData(config);
                }

                // If overwrite requested OR standalone executable does not exist yet: extract
                if (overwrite || !mainExeExists)
                {
                    PerformInstall(tempDir, isPortable: true, createDesktopShortcut: false, createStartMenuShortcut: false,
                        registerFileAssoc: false, registerDefaultBrowser: false, silent: true, progress: progress, config: config);
                }
                else
                {
                    ReportProgress(progress, 100, string.Format("Launching standalone {0}...", config.AppName));
                }

                if (File.Exists(mainExe))
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = mainExe,
                        WorkingDirectory = tempDir,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                return mainExe;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Installer] Error launching standalone: " + ex.Message);
                return null;
            }
        }

        public static void PerformInstall(string targetDir, bool isPortable, bool createDesktopShortcut, bool createStartMenuShortcut,
            bool registerFileAssoc, bool registerDefaultBrowser, bool silent, BackgroundWorker progress, InstallerConfig config = null)
        {
            config = config ?? InstallerConfig.Current;
            try
            {
                ReportProgress(progress, 5, "Preparing destination directory...");
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Stop any running application processes
                CloseRunningProcesses(config);

                // Clean up any stale session locks or unacknowledged crash state from killed/prior sessions
                CleanStaleSessionAndCrashState(config, isPortable, targetDir);

                // Safely clean core\ directory to prevent architecture-mismatched DLLs/runtimes
                if (config.SafeCleanCoreSubdirectory && !string.IsNullOrEmpty(config.CoreSubdirectoryName))
                {
                    string coreDir = Path.Combine(targetDir, config.CoreSubdirectoryName);
                    if (Directory.Exists(coreDir))
                    {
                        try
                        {
                            Directory.Delete(coreDir, true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Note for core directory cleanup: " + ex.Message);
                            try
                            {
                                foreach (string f in Directory.GetFiles(coreDir, "*.*", SearchOption.AllDirectories))
                                {
                                    try { File.Delete(f); } catch { }
                                }
                            }
                            catch { }
                        }
                    }
                }

                // Extract embedded payload archive
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream zipStream = asm.GetManifestResourceStream(config.PayloadZipResource))
                {
                    if (zipStream != null)
                    {
                        using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                        {
                            int total = archive.Entries.Count;
                            int count = 0;

                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                count++;
                                string destPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));

                                if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                                {
                                    if (!Directory.Exists(destPath)) Directory.CreateDirectory(destPath);
                                    continue;
                                }

                                string dir = Path.GetDirectoryName(destPath);
                                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                                try
                                {
                                    entry.ExtractToFile(destPath, overwrite: true);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine("File extraction warning for " + destPath + ": " + ex.Message);
                                }

                                int percent = 10 + (int)((count / (float)total) * 65);
                                ReportProgress(progress, percent, string.Format("Extracting {0} ({1}/{2})...", Path.GetFileName(entry.FullName), count, total));
                            }
                        }
                    }
                }

                string mainExe = Path.Combine(targetDir, config.MainExecutable);

                // Portable mode marker
                if (isPortable)
                {
                    ReportProgress(progress, 80, "Configuring portable mode...");
                    try
                    {
                        File.WriteAllText(Path.Combine(targetDir, "portable.dat"), config.AppName + "-portable");
                        string dataDir = Path.Combine(targetDir, "data");
                        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                    }
                    catch { }
                }
                else
                {
                    // Deploy uninstaller (copying current setup wizard)
                    ReportProgress(progress, 80, "Creating uninstaller...");
                    string uninstallExe = Path.Combine(targetDir, "uninstall.exe");
                    try
                    {
                        File.Copy(Application.ExecutablePath, uninstallExe, true);
                    }
                    catch { }

                    // Create Shortcuts
                    if (createDesktopShortcut || createStartMenuShortcut)
                    {
                        ReportProgress(progress, 86, "Creating shortcuts...");
                        string lnkName = config.AppName + ".lnk";
                        string shortcutDesc = string.Format("{0} - {1}", config.AppName, config.Tagline);

                        if (createDesktopShortcut)
                        {
                            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                            CreateShortcut(Path.Combine(desktopPath, lnkName), mainExe, targetDir, shortcutDesc);
                        }

                        if (createStartMenuShortcut)
                        {
                            string programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                            CreateShortcut(Path.Combine(programsPath, lnkName), mainExe, targetDir, shortcutDesc);
                        }
                    }

                    // File Associations
                    if (registerFileAssoc && config.AssociatedFileExtensions != null && config.AssociatedFileExtensions.Length > 0)
                    {
                        ReportProgress(progress, 90, "Configuring file associations...");
                        RegisterFileAssociations(config, mainExe);
                    }

                    // Default Browser Registration (if applicable)
                    if (registerDefaultBrowser && config.IsWebBrowser)
                    {
                        ReportProgress(progress, 94, "Registering browser capabilities...");
                        RegisterDefaultBrowser(config, mainExe);
                    }

                    // Add/Remove Programs entry
                    ReportProgress(progress, 97, "Registering application in Windows...");
                    RegisterUninstallEntry(config, targetDir, mainExe, uninstallExe);
                }

                ReportProgress(progress, 100, "Installation complete!");
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Installer] Error during install: " + ex.Message);
                if (!silent)
                {
                    MessageBox.Show("An error occurred during installation: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public static void PerformUninstall(bool silent, bool deleteProfileData, BackgroundWorker progress, InstallerConfig config = null)
        {
            config = config ?? InstallerConfig.Current;
            try
            {
                ReportProgress(progress, 10, string.Format("Closing running {0} processes...", config.AppName));
                CloseRunningProcesses(config);
                CleanStaleSessionAndCrashState(config, isPortable: false);

                ReportProgress(progress, 30, "Removing shortcuts...");
                try
                {
                    string lnkName = config.AppName + ".lnk";
                    string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), lnkName);
                    if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                    string startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), lnkName);
                    if (File.Exists(startLnk)) File.Delete(startLnk);
                }
                catch { }

                ReportProgress(progress, 50, "Cleaning registry and capabilities...");
                if (config.IsWebBrowser)
                {
                    UnregisterDefaultBrowser(config);
                }
                UnregisterFileAssociations(config);
                UnregisterUninstallEntry(config);

                if (deleteProfileData)
                {
                    ReportProgress(progress, 70, "Removing user profile and cache data...");
                    try
                    {
                        string profileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), config.DefaultInstallFolderName);
                        if (Directory.Exists(profileDir)) Directory.Delete(profileDir, true);
                    }
                    catch { }
                }

                ReportProgress(progress, 85, "Scheduling cleanup of application files...");
                string installDir = null;
                InstalledAppInfo info = InstalledAppInfo.Detect(config);
                if (info.IsInstalled && !string.IsNullOrEmpty(info.InstallPath))
                {
                    installDir = info.InstallPath;
                }
                else
                {
                    installDir = GetDefaultInstallDirectory(config);
                }

                if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                {
                    string runningDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                    string targetCleanDir = installDir.TrimEnd('\\');
                    if (string.Equals(runningDir, targetCleanDir, StringComparison.OrdinalIgnoreCase))
                    {
                        ScheduleDirectoryDeletion(installDir);
                    }
                    else
                    {
                        try
                        {
                            Directory.Delete(installDir, true);
                        }
                        catch
                        {
                            ScheduleDirectoryDeletion(installDir);
                        }
                    }
                }

                ReportProgress(progress, 100, "Uninstall complete!");
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Installer] Error during uninstall: " + ex.Message);
                if (!silent)
                {
                    MessageBox.Show("An error occurred during uninstallation: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static void CloseRunningProcesses(InstallerConfig config)
        {
            if (config == null || config.ProcessNamesToClose == null) return;

            for (int i = 0; i < config.ProcessNamesToClose.Length; i++)
            {
                string procName = config.ProcessNamesToClose[i];
                try
                {
                    Process[] procs = Process.GetProcessesByName(procName);
                    for (int j = 0; j < procs.Length; j++)
                    {
                        try
                        {
                            if (procs[j].MainWindowHandle != IntPtr.Zero)
                            {
                                procs[j].CloseMainWindow();
                                if (procs[j].WaitForExit(1500))
                                {
                                    continue;
                                }
                            }
                        }
                        catch { }

                        try
                        {
                            procs[j].Kill();
                            procs[j].WaitForExit(1000);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Cleans up stale session lock files, unacknowledged crash records, and Chromium singleton locks
        /// left behind by terminated processes or prior installations, ensuring the newly installed
        /// browser starts cleanly without false-positive crash checks.
        /// </summary>
        public static void CleanStaleSessionAndCrashState(InstallerConfig config = null, bool isPortable = false, string targetDir = null)
        {
            config = config ?? InstallerConfig.Current;
            try
            {
                string dataDir;
                if (isPortable && !string.IsNullOrEmpty(targetDir))
                {
                    dataDir = Path.Combine(targetDir, "data");
                }
                else
                {
                    dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), config.DefaultInstallFolderName);
                }

                if (Directory.Exists(dataDir))
                {
                    // 1. Remove stale session lock
                    string lockFile = Path.Combine(dataDir, "session_active.lock");
                    if (File.Exists(lockFile))
                    {
                        try { File.Delete(lockFile); } catch { }
                    }

                    // 2. Clear unacknowledged crash state from killed/prior session
                    string crashState = Path.Combine(dataDir, "last_crash.json");
                    if (File.Exists(crashState))
                    {
                        try { File.Delete(crashState); } catch { }
                    }

                    // Also check config/last_crash.json if migrated
                    string configCrashState = Path.Combine(dataDir, "config", "last_crash.json");
                    if (File.Exists(configCrashState))
                    {
                        try { File.Delete(configCrashState); } catch { }
                    }

                    // 3. Clear stale Chromium singleton locks so CEF doesn't fail on launch
                    string profileDir = Path.Combine(dataDir, "profile");
                    if (Directory.Exists(profileDir))
                    {
                        string[] singletonFiles = new string[] { "SingletonLock", "SingletonCookie", "SingletonSocket" };
                        foreach (string sf in singletonFiles)
                        {
                            try
                            {
                                string p = Path.Combine(profileDir, sf);
                                if (File.Exists(p)) File.Delete(p);
                            }
                            catch { }
                        }

                        // Also clean stale LevelDB LOCK files
                        try
                        {
                            var lockFiles = Directory.GetFiles(profileDir, "LOCK", SearchOption.AllDirectories);
                            foreach (var lf in lockFiles)
                            {
                                try { File.Delete(lf); } catch { }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Installer] CleanStaleSessionAndCrashState note: " + ex.Message);
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetExe, string workingDir, string description)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetExe;
                    shortcut.WorkingDirectory = workingDir;
                    shortcut.Description = description ?? "";
                    shortcut.IconLocation = targetExe + ",0";
                    shortcut.Save();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Installer] Error creating shortcut " + shortcutPath + ": " + ex.Message);
            }
        }

        private static void RegisterFileAssociations(InstallerConfig config, string mainExe)
        {
            try
            {
                string progId = config.BrowserProgId ?? (config.AppName + "File");
                string iconPath = mainExe + ",0";
                string openCommand = "\"" + mainExe + "\" \"%1\"";

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + progId))
                {
                    if (key != null)
                    {
                        key.SetValue("", config.BrowserProgIdDescription ?? (config.AppTitle + " Document"));
                        using (RegistryKey iconKey = key.CreateSubKey("DefaultIcon"))
                        {
                            if (iconKey != null) iconKey.SetValue("", iconPath);
                        }
                        using (RegistryKey cmdKey = key.CreateSubKey(@"shell\open\command"))
                        {
                            if (cmdKey != null) cmdKey.SetValue("", openCommand);
                        }
                    }
                }

                if (config.AssociatedFileExtensions != null)
                {
                    for (int i = 0; i < config.AssociatedFileExtensions.Length; i++)
                    {
                        string ext = config.AssociatedFileExtensions[i];
                        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ext + @"\OpenWithProgids"))
                        {
                            if (key != null) key.SetValue(progId, "");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Installer] Error registering file associations: " + ex.Message);
            }
        }

        private static void UnregisterFileAssociations(InstallerConfig config)
        {
            try
            {
                string progId = config.BrowserProgId ?? (config.AppName + "File");
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + progId, false);

                if (config.AssociatedFileExtensions != null)
                {
                    for (int i = 0; i < config.AssociatedFileExtensions.Length; i++)
                    {
                        string ext = config.AssociatedFileExtensions[i];
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ext + @"\OpenWithProgids", true))
                        {
                            if (key != null)
                            {
                                try { key.DeleteValue(progId, false); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void RegisterDefaultBrowser(InstallerConfig config, string mainExe)
        {
            try
            {
                string progId = config.BrowserProgId ?? "wbHTML";
                string iconPath = mainExe + ",0";
                string openCommand = "\"" + mainExe + "\" \"%1\"";

                using (RegistryKey capKey = Registry.CurrentUser.CreateSubKey(config.BrowserCapabilitiesSubKey))
                {
                    if (capKey != null)
                    {
                        capKey.SetValue("ApplicationDescription", string.Format("{0} - {1}", config.AppTitle, config.Tagline));
                        capKey.SetValue("ApplicationName", config.AppName);
                        capKey.SetValue("ApplicationIcon", iconPath);

                        using (RegistryKey urlKey = capKey.CreateSubKey("URLAssociations"))
                        {
                            if (urlKey != null)
                            {
                                urlKey.SetValue("http", progId);
                                urlKey.SetValue("https", progId);
                                urlKey.SetValue("ftp", progId);
                            }
                        }

                        using (RegistryKey faKey = capKey.CreateSubKey("FileAssociations"))
                        {
                            if (faKey != null && config.AssociatedFileExtensions != null)
                            {
                                for (int i = 0; i < config.AssociatedFileExtensions.Length; i++)
                                {
                                    faKey.SetValue(config.AssociatedFileExtensions[i], progId);
                                }
                            }
                        }

                        using (RegistryKey startmenu = capKey.CreateSubKey("Startmenu"))
                        {
                            if (startmenu != null)
                            {
                                startmenu.SetValue("StartMenuInternet", config.MainExecutable);
                            }
                        }
                    }
                }

                using (RegistryKey regApps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                {
                    if (regApps != null)
                    {
                        regApps.SetValue(config.AppName, config.BrowserCapabilitiesSubKey);
                    }
                }

                // Protocols
                string[] protocols = config.AssociatedProtocols ?? new string[] { "http", "https" };
                for (int i = 0; i < protocols.Length; i++)
                {
                    string p = protocols[i];
                    using (RegistryKey pKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + p + @"\shell\open\command"))
                    {
                        if (pKey != null) pKey.SetValue("", openCommand);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Installer] Error registering default browser: " + ex.Message);
            }
        }

        private static void UnregisterDefaultBrowser(InstallerConfig config)
        {
            try
            {
                string rootSubKey = config.BrowserCapabilitiesSubKey;
                int slash = rootSubKey.IndexOf('\\');
                if (slash > 0)
                {
                    int secondSlash = rootSubKey.IndexOf('\\', slash + 1);
                    if (secondSlash > 0) rootSubKey = rootSubKey.Substring(0, secondSlash);
                }

                Registry.CurrentUser.DeleteSubKeyTree(rootSubKey, false);
                using (RegistryKey regApps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
                {
                    if (regApps != null)
                    {
                        try { regApps.DeleteValue(config.AppName, false); } catch { }
                    }
                }
            }
            catch { }
        }

        private static void RegisterUninstallEntry(InstallerConfig config, string installDir, string mainExe, string uninstallExe)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(config.RegistryUninstallSubKey))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", string.Format("{0} ({1})", config.AppTitle, CurrentArchitecture));
                        key.SetValue("DisplayVersion", GetPackageVersionString());
                        key.SetValue("DisplayIcon", mainExe + ",0");
                        key.SetValue("Publisher", config.Publisher);
                        key.SetValue("InstallLocation", installDir);
                        key.SetValue("Architecture", CurrentArchitecture);
                        key.SetValue("UninstallString", "\"" + uninstallExe + "\" /uninstall");
                        key.SetValue("QuietUninstallString", "\"" + uninstallExe + "\" /uninstall /silent");
                        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        private static void UnregisterUninstallEntry(InstallerConfig config)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(config.RegistryUninstallSubKey, false);
            }
            catch { }
        }

        private static void ScheduleDirectoryDeletion(string dirPath)
        {
            try
            {
                string cmd = string.Format("/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{0}\"", dirPath.TrimEnd('\\'));
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd)
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
            catch { }
        }

        private static void ReportProgress(BackgroundWorker progress, int percent, string status)
        {
            if (progress != null)
            {
                progress.ReportProgress(percent, status);
            }
        }
    }
}

