using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// ============================================================================
// ikmalsaid's Carbon Install System
// Main Application Entry Point & CLI Dispatcher
//
// Modular component structure:
// - Config/InstallerConfig.cs              - Application identity, branding & capabilities
// - Core/InstallerEngine.cs                - Payload extraction, shortcuts, registry & uninstaller
// - Core/InstalledAppInfo.cs               - Registry discovery & PE header machine architecture
// - Graphics/SvgPathParser.cs              - Zero-dependency SVG tokenizer & vector parser
// - Graphics/SetupIcons.cs                 - Phosphor vector icons provider & rasterizer
// - UI/UITheme.cs                          - Dark / Fluent design tokens & kinetic gradients
// - UI/SetupWizardForm.cs                  - Adaptive setup wizard form driven by InstallerConfig
// - UI/Controls/ActionChoiceCard.cs        - Interactive GPU-Z / Fluent style action card
// - UI/Controls/BrandLogoControl.cs        - Vector SVG & raster logo presentation control
// - UI/Controls/CircularProgressSpinner.cs - Fluent animated circular percentage spinner
// ============================================================================

[assembly: AssemblyTitle("wb Installer")]
[assembly: AssemblyDescription("ikmalsaid's Carbon Install System")]
[assembly: AssemblyCompany("ikmalsaid")]
[assembly: AssemblyProduct("wb")]
[assembly: AssemblyCopyright("Copyright 2026 ikmalsaid")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace wb.Installer
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            InstallerConfig config = InstallerConfig.Current;

            // Configure branding accent if specified
            if (config.DefaultAccentColor.HasValue)
            {
                BrandTheme.GenerateRandomAccent(config.DefaultAccentColor.Value);
            }
            else if (!config.UseRandomAccentGradient)
            {
                BrandTheme.GenerateRandomAccent(Color.FromArgb(10, 132, 255));
            }

            // Windows Version Compatibility Check
            if (!CheckWindowsVersionCompatibility(config))
            {
                return;
            }

            bool isUninstall = false;
            bool isSilent = false;
            bool isStandalone = false;
            bool isPortable = false;
            bool cmdOverwrite = true;
            bool cmdClearData = false;
            bool hasExplicitOverwrite = false;
            string customTargetDir = null;

            string exeName = Path.GetFileName(Application.ExecutablePath).ToLowerInvariant();
            if (exeName.Contains("uninstall"))
            {
                isUninstall = true;
            }

            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i].Trim().ToLowerInvariant();
                    if (a == "/uninstall" || a == "--uninstall" || a == "-u")
                    {
                        isUninstall = true;
                    }
                    else if (a == "/silent" || a == "--silent" || a == "-s")
                    {
                        isSilent = true;
                    }
                    else if (a == "/standalone" || a == "--standalone" || a == "-standalone")
                    {
                        isStandalone = true;
                    }
                    else if (a == "/portable" || a == "--portable" || a == "-portable")
                    {
                        isPortable = true;
                    }
                    else if (a == "/overwrite" || a == "--overwrite" || a == "-overwrite")
                    {
                        cmdOverwrite = true;
                        hasExplicitOverwrite = true;
                    }
                    else if (a == "/no-overwrite" || a == "--no-overwrite")
                    {
                        cmdOverwrite = false;
                        hasExplicitOverwrite = true;
                    }
                    else if (a == "/cleardata" || a == "--cleardata" || a == "--clear-data" || a == "/clear-data")
                    {
                        cmdClearData = true;
                    }
                    else if (a.StartsWith("/dir=") || a.StartsWith("--dir="))
                    {
                        int eq = a.IndexOf('=');
                        customTargetDir = a.Substring(eq + 1).Trim('"', '\'');
                    }
                }
            }

            if (isStandalone)
            {
                string standaloneDir = InstallerEngine.GetStandaloneDirectory(config);
                string mainExe = Path.Combine(standaloneDir, config.MainExecutable);
                bool existingStandalone = File.Exists(mainExe);

                if (isSilent)
                {
                    bool overwrite = hasExplicitOverwrite ? cmdOverwrite : true;
                    InstallerEngine.LaunchStandalone(progress: null, config: config, overwrite: overwrite, clearUserData: cmdClearData);
                    return;
                }
                else
                {
                    bool overwrite = true;
                    bool clearUserData = false;

                    if (existingStandalone)
                    {
                        using (StandalonePromptDialog prompt = new StandalonePromptDialog(standaloneDir, config))
                        {
                            if (prompt.ShowDialog() != DialogResult.OK)
                            {
                                return;
                            }
                            overwrite = prompt.OverwriteFiles;
                            clearUserData = prompt.ClearUserData;
                        }
                    }

                    InstallerEngine.LaunchStandalone(progress: null, config: config, overwrite: overwrite, clearUserData: clearUserData);
                    return;
                }
            }

            if (isSilent)
            {
                if (isUninstall)
                {
                    InstallerEngine.PerformUninstall(silent: true, deleteProfileData: false, progress: null, config: config);
                }
                else if (isPortable)
                {
                    string target = string.IsNullOrEmpty(customTargetDir) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, config.PortableFolderName) : customTargetDir;
                    InstallerEngine.PerformInstall(target, isPortable: true, createDesktopShortcut: false, createStartMenuShortcut: false,
                        registerFileAssoc: false, registerDefaultBrowser: false, silent: true, progress: null, config: config);
                }
                else
                {
                    InstalledAppInfo info = InstalledAppInfo.Detect(config);
                    string target = !string.IsNullOrEmpty(customTargetDir)
                        ? customTargetDir
                        : (info.IsInstalled && !string.IsNullOrEmpty(info.InstallPath) ? info.InstallPath : InstallerEngine.GetDefaultInstallDirectory(config));

                    InstallerEngine.PerformInstall(target, isPortable: false, createDesktopShortcut: true, createStartMenuShortcut: true,
                        registerFileAssoc: true, registerDefaultBrowser: config.IsWebBrowser, silent: true, progress: null, config: config);
                }
                return;
            }

            Application.Run(new SetupWizardForm(isUninstall, config));
        }

        private static bool CheckWindowsVersionCompatibility(InstallerConfig config)
        {
            try
            {
                Version osVer = Environment.OSVersion.Version;
                if (config.MinWindowsVersion != null)
                {
                    if (osVer.Major < config.MinWindowsVersion.Major ||
                        (osVer.Major == config.MinWindowsVersion.Major && osVer.Minor < config.MinWindowsVersion.Minor))
                    {
                        string msg = string.Format("{0} requires {1} or newer. Windows 7, 8, and 8.1 are not supported.",
                            config.AppTitle, config.MinWindowsVersionName ?? "Windows 10 / 11");
                        MessageBox.Show(msg, "Unsupported Operating System", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                }
            }
            catch { }
            return true;
        }
    }
}
