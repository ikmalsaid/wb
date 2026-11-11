using System;
using System.Drawing;

namespace wb.Installer
{
    /// <summary>
    /// ikmalsaid's Carbon Install System configuration settings defining application identity, branding,
    /// capabilities, and deployment options for the installer wizard and engine.
    /// Developers can customize this file to configure the installer for any Windows application.
    /// </summary>
    public class InstallerConfig
    {
        private static InstallerConfig _current;

        /// <summary>
        /// Gets or sets the active installer configuration instance.
        /// Defaults to the wb browser configuration.
        /// </summary>
        public static InstallerConfig Current
        {
            get
            {
                if (_current == null)
                {
                    _current = CreateDefault();
                }
                return _current;
            }
            set
            {
                _current = value;
            }
        }

        // --- Application Identity ---
        public string AppName { get; set; }
        public string AppTitle { get; set; }
        public string Tagline { get; set; }
        public string Publisher { get; set; }
        public string MainExecutable { get; set; }
        public string[] ProcessNamesToClose { get; set; }
        public string DefaultInstallFolderName { get; set; }

        // --- Registry & System Integration ---
        public string RegistryUninstallSubKey { get; set; }
        public string BrowserCapabilitiesSubKey { get; set; }
        public string BrowserProgId { get; set; }
        public string BrowserProgIdDescription { get; set; }
        public bool IsWebBrowser { get; set; }
        public string[] AssociatedFileExtensions { get; set; }
        public string[] AssociatedProtocols { get; set; }

        // --- Deployment Features ---
        public bool EnablePortableMode { get; set; }
        public string PortableFolderName { get; set; }
        public bool EnableStandaloneMode { get; set; }
        public string StandaloneTempFolderName { get; set; }
        public bool SafeCleanCoreSubdirectory { get; set; }
        public string CoreSubdirectoryName { get; set; }

        public string GetStandaloneTempFolderName()
        {
            if (string.IsNullOrEmpty(StandaloneTempFolderName))
            {
                return "wb_standalone_" + InstallerEngine.CurrentArchitecture;
            }
            if (!StandaloneTempFolderName.EndsWith(InstallerEngine.CurrentArchitecture, StringComparison.OrdinalIgnoreCase))
            {
                return StandaloneTempFolderName + "_" + InstallerEngine.CurrentArchitecture;
            }
            return StandaloneTempFolderName;
        }

        // --- Embedded Resource Names ---
        public string PayloadZipResource { get; set; }
        public string LogoPngResource { get; set; }
        public string SvgIconResource { get; set; }
        public string AppIconResource { get; set; }

        // --- Operating System Requirements ---
        public Version MinWindowsVersion { get; set; }
        public string MinWindowsVersionName { get; set; }

        // --- Branding & Colors ---
        public bool UseRandomAccentGradient { get; set; }
        public Color? DefaultAccentColor { get; set; }

        public InstallerConfig()
        {
            // Default configuration for wb
            AppName = "wb";
            AppTitle = "wb browser";
            Tagline = "More Web. Less Noise";
            Publisher = "ikmalsaid";
            MainExecutable = "wb.exe";
            ProcessNamesToClose = new string[] { "wb", "wb.sub", "wb.subprocess", "wb.BrowserSubprocess", "ChromeDotNet" };
            DefaultInstallFolderName = "wb";

            RegistryUninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\wb";
            BrowserCapabilitiesSubKey = @"Software\wb\Capabilities";
            BrowserProgId = "wbHTML";
            BrowserProgIdDescription = "wb HTML Document";
            IsWebBrowser = true;
            AssociatedFileExtensions = new string[] { ".html", ".htm", ".shtml", ".xhtml", ".svg", ".webp" };
            AssociatedProtocols = new string[] { "http", "https" };

            EnablePortableMode = true;
            PortableFolderName = "wb_portable";
            EnableStandaloneMode = true;
            StandaloneTempFolderName = "wb_standalone_" + InstallerEngine.CurrentArchitecture;
            SafeCleanCoreSubdirectory = true;
            CoreSubdirectoryName = "core";

            PayloadZipResource = "wb.payload.zip";
            LogoPngResource = "wb.logo.png";
            SvgIconResource = "wb.icon.svg";
            AppIconResource = "wb.icon.ico";

            MinWindowsVersion = new Version(10, 0); // Windows 10 / 11 (NT 10.0)
            MinWindowsVersionName = "Windows 10 / 11";

            UseRandomAccentGradient = true;
            DefaultAccentColor = null;
        }

        public static InstallerConfig CreateDefault()
        {
            return new InstallerConfig();
        }
    }
}

