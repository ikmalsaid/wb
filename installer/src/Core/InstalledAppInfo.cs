using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace wb.Installer
{
    /// <summary>
    /// Detects and parses existing application installations from the Windows registry and disk.
    /// Inspects PE header machine architectures and provides structured version comparison.
    /// </summary>
    public class InstalledAppInfo
    {
        public bool IsInstalled { get; set; }
        public string InstallPath { get; set; }
        public string MainExePath { get; set; }
        public Version InstalledVersion { get; set; }
        public string InstalledVersionString { get; set; }
        public string InstalledArchitecture { get; set; }
        public string UninstallExePath { get; set; }

        /// <summary>
        /// Backward-compatible property alias for MainExePath.
        /// </summary>
        public string WbExePath
        {
            get { return MainExePath; }
            set { MainExePath = value; }
        }

        public static InstalledAppInfo Detect(InstallerConfig config = null)
        {
            config = config ?? InstallerConfig.Current;
            InstalledAppInfo info = new InstalledAppInfo();
            info.InstalledArchitecture = "Unknown";

            // 1. Check Windows Add/Remove Programs Registry
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(config.RegistryUninstallSubKey, false))
                {
                    if (key != null)
                    {
                        string loc = key.GetValue("InstallLocation") as string;
                        string verStr = key.GetValue("DisplayVersion") as string;
                        string uninst = key.GetValue("UninstallString") as string;
                        string arch = key.GetValue("Architecture") as string;

                        if (!string.IsNullOrEmpty(arch))
                        {
                            info.InstalledArchitecture = arch.Trim();
                        }

                        if (!string.IsNullOrEmpty(loc))
                        {
                            info.InstallPath = loc;
                            string candidateExe = Path.Combine(loc, config.MainExecutable);
                            if (File.Exists(candidateExe))
                            {
                                info.MainExePath = candidateExe;
                            }
                        }

                        if (!string.IsNullOrEmpty(verStr))
                        {
                            info.InstalledVersionString = verStr.Trim();
                            info.InstalledVersion = InstallerEngine.ParseVersionSafe(verStr);
                        }

                        if (!string.IsNullOrEmpty(uninst))
                        {
                            info.UninstallExePath = uninst.Trim('"', ' ');
                        }

                        if (!string.IsNullOrEmpty(loc) || !string.IsNullOrEmpty(uninst))
                        {
                            info.IsInstalled = true;
                        }
                    }
                }
            }
            catch { }

            // 2. Secondary check: Default installation directory
            if (!info.IsInstalled)
            {
                try
                {
                    string defDir = InstallerEngine.GetDefaultInstallDirectory(config);
                    string candidateExe = Path.Combine(defDir, config.MainExecutable);
                    if (File.Exists(candidateExe))
                    {
                        info.IsInstalled = true;
                        info.InstallPath = defDir;
                        info.MainExePath = candidateExe;
                    }
                }
                catch { }
            }

            // 3. Inspect binary architecture via PE header if not set by registry
            if (info.IsInstalled && (string.IsNullOrEmpty(info.InstalledArchitecture) || info.InstalledArchitecture == "Unknown") &&
                !string.IsNullOrEmpty(info.MainExePath) && File.Exists(info.MainExePath))
            {
                info.InstalledArchitecture = InstallerEngine.GetPeArchitecture(info.MainExePath);
            }

            // 4. Exact FileVersion / ProductVersion inspection
            if (info.IsInstalled && !string.IsNullOrEmpty(info.MainExePath) && File.Exists(info.MainExePath))
            {
                try
                {
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(info.MainExePath);
                    if (info.InstalledVersion == null || (info.InstalledVersion.Major == 0 && info.InstalledVersion.Minor == 0))
                    {
                        string fv = fvi.ProductVersion;
                        if (string.IsNullOrEmpty(fv)) fv = fvi.FileVersion;
                        if (!string.IsNullOrEmpty(fv))
                        {
                            info.InstalledVersionString = fv.Trim();
                            info.InstalledVersion = InstallerEngine.ParseVersionSafe(fv);
                        }
                        else if (fvi.FileMajorPart > 0 || fvi.FileMinorPart > 0)
                        {
                            info.InstalledVersion = new Version(fvi.FileMajorPart, fvi.FileMinorPart, Math.Max(0, fvi.FileBuildPart), Math.Max(0, fvi.FilePrivatePart));
                            info.InstalledVersionString = info.InstalledVersion.ToString();
                        }
                    }
                }
                catch { }
            }

            if (info.InstalledVersion == null)
            {
                info.InstalledVersion = new Version(1, 0, 0, 0);
                if (string.IsNullOrEmpty(info.InstalledVersionString))
                    info.InstalledVersionString = "1.0.0";
            }

            return info;
        }
    }

    /// <summary>
    /// Backward-compatibility class for InstalledWbInfo.
    /// </summary>
    public class InstalledWbInfo : InstalledAppInfo
    {
        public static InstalledWbInfo Detect()
        {
            InstalledAppInfo baseInfo = InstalledAppInfo.Detect(InstallerConfig.Current);
            return new InstalledWbInfo
            {
                IsInstalled = baseInfo.IsInstalled,
                InstallPath = baseInfo.InstallPath,
                MainExePath = baseInfo.MainExePath,
                InstalledVersion = baseInfo.InstalledVersion,
                InstalledVersionString = baseInfo.InstalledVersionString,
                InstalledArchitecture = baseInfo.InstalledArchitecture,
                UninstallExePath = baseInfo.UninstallExePath
            };
        }
    }
}

