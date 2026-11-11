using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using wb.Installer;

namespace wb.Tests
{
    public static class InstallerTests
    {
        private static int _passed = 0;
        private static int _failed = 0;

        public static void Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("  wb Installer Standalone & Architecture Tests");
            Console.WriteLine("==================================================");

            Test("Standalone Architecture-Specific Folder Separation", () =>
            {
                InstallerConfig cfg = new InstallerConfig();
                string arch = InstallerEngine.CurrentArchitecture;
                Assert(cfg.StandaloneTempFolderName.EndsWith(arch), "StandaloneTempFolderName must end with current architecture: " + cfg.StandaloneTempFolderName);

                string standaloneDir = InstallerEngine.GetStandaloneDirectory(cfg);
                Assert(standaloneDir.EndsWith("wb_standalone_" + arch), "Standalone directory must end with wb_standalone_" + arch + ": " + standaloneDir);

                // Test with custom folder without arch suffix: should auto-append arch
                cfg.StandaloneTempFolderName = "custom_standalone";
                string customDir = InstallerEngine.GetStandaloneDirectory(cfg);
                Assert(customDir.EndsWith("custom_standalone_" + arch), "Custom folder name must auto-append architecture suffix: " + customDir);

                // Test with null or empty folder: should fallback to default with arch
                cfg.StandaloneTempFolderName = null;
                string fallbackDir = InstallerEngine.GetStandaloneDirectory(cfg);
                Assert(fallbackDir.EndsWith("wb_standalone_" + arch), "Null folder name must fallback to wb_standalone_" + arch);
            });

            Test("Standalone Existing Detection and User Data Clearing", () =>
            {
                InstallerConfig cfg = new InstallerConfig();
                string testTemp = Path.Combine(Path.GetTempPath(), "wb_test_standalone_" + Guid.NewGuid().ToString("N"));
                cfg.StandaloneTempFolderName = Path.GetFileName(testTemp);

                try
                {
                    string dir = InstallerEngine.GetStandaloneDirectory(cfg);
                    Assert(!InstallerEngine.IsStandaloneExtracted(cfg), "Non-existent standalone must report not extracted");

                    Directory.CreateDirectory(dir);
                    string mainExe = Path.Combine(dir, cfg.MainExecutable);
                    File.WriteAllText(mainExe, "mock-exe");
                    Assert(InstallerEngine.IsStandaloneExtracted(cfg), "Created executable must report standalone extracted");

                    Assert(!InstallerEngine.HasStandaloneUserData(cfg), "Empty data dir must report no user data");

                    string dataDir = Path.Combine(dir, "data");
                    Directory.CreateDirectory(dataDir);
                    File.WriteAllText(Path.Combine(dataDir, "bookmarks.json"), "{}");
                    Assert(InstallerEngine.HasStandaloneUserData(cfg), "Data dir with files must report has user data");

                    InstallerEngine.ClearStandaloneUserData(cfg);
                    Assert(!InstallerEngine.HasStandaloneUserData(cfg), "Cleared data dir must report no user data");
                    Assert(Directory.Exists(dataDir), "Cleared data dir must still exist");
                }
                finally
                {
                    string dir = InstallerEngine.GetStandaloneDirectory(cfg);
                    if (Directory.Exists(dir))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            });

            Test("StandalonePromptDialog State Transitions and Dynamic Actions", () =>
            {
                InstallerConfig cfg = new InstallerConfig();
                string testPath = Path.Combine(Path.GetTempPath(), "wb_standalone_" + InstallerEngine.CurrentArchitecture);

                using (StandalonePromptDialog dlg = new StandalonePromptDialog(testPath, cfg))
                {
                    Assert(dlg.Text.Contains("Standalone Mode"), "Dialog title must contain Standalone Mode");
                    Assert(dlg.Text.Contains(InstallerEngine.CurrentArchitecture), "Dialog title must contain current architecture: " + dlg.Text);

                    // Default state: Overwrite = true, ClearData = false
                    Assert(dlg.OverwriteFiles == true, "OverwriteFiles must default to true");
                    Assert(dlg.ClearUserData == false, "ClearUserData must default to false");
                    Assert(dlg.AcceptButton != null, "AcceptButton must be assigned");
                    Assert(dlg.CancelButton != null, "CancelButton must be assigned");
                    Assert(dlg.LaunchButtonText == "Update & Launch", "Default accept button text should be 'Update & Launch': " + dlg.LaunchButtonText);

                    // Transition 1: Overwrite = true, ClearData = true -> "Reset & Launch"
                    dlg.ClearUserData = true;
                    Assert(dlg.LaunchButtonText == "Reset & Launch", "When ClearUserData is checked, button should be 'Reset & Launch': " + dlg.LaunchButtonText);

                    // Transition 2: Overwrite = false, ClearData = false -> "Launch Existing"
                    dlg.OverwriteFiles = false;
                    dlg.ClearUserData = false;
                    Assert(dlg.LaunchButtonText == "Launch Existing", "When both unchecked, button should be 'Launch Existing': " + dlg.LaunchButtonText);

                    // Transition 3: Overwrite = false, ClearData = true -> "Clear & Launch"
                    dlg.ClearUserData = true;
                    Assert(dlg.LaunchButtonText == "Clear & Launch", "When only ClearUserData checked, button should be 'Clear & Launch': " + dlg.LaunchButtonText);

                    // Test Confirmation Dialog Behavior:
                    // 1. When ClearUserData is true and user cancels confirmation -> DialogResult remains None
                    bool confirmPrompted = false;
                    dlg.ConfirmClearDataFunc = () =>
                    {
                        confirmPrompted = true;
                        return false;
                    };
                    dlg.PerformLaunch();
                    Assert(confirmPrompted, "Confirmation prompt must be invoked when ClearUserData is checked");
                    Assert(dlg.DialogResult != DialogResult.OK, "Dialog must not accept when confirmation prompt is declined");

                    // 2. When ClearUserData is true and user confirms -> DialogResult becomes OK
                    dlg.ConfirmClearDataFunc = () => true;
                    dlg.PerformLaunch();
                    Assert(dlg.DialogResult == DialogResult.OK, "Dialog must accept when confirmation prompt is accepted");
                }
            });

            Test("InstallerEngine CleanStaleSessionAndCrashState Purges Stale Locks", () =>
            {
                InstallerConfig cfg = new InstallerConfig();
                string testFolder = "wb_test_clean_" + Guid.NewGuid().ToString("N");
                cfg.DefaultInstallFolderName = testFolder;

                try
                {
                    string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), cfg.DefaultInstallFolderName);
                    Directory.CreateDirectory(dataDir);
                    string profileDir = Path.Combine(dataDir, "profile");
                    Directory.CreateDirectory(profileDir);

                    string lockFile = Path.Combine(dataDir, "session_active.lock");
                    string crashFile = Path.Combine(dataDir, "last_crash.json");
                    string singleton = Path.Combine(profileDir, "SingletonLock");
                    File.WriteAllText(lockFile, "1234:2026-01-01T00:00:00Z");
                    File.WriteAllText(crashFile, "{\"Acknowledged\":false}");
                    File.WriteAllText(singleton, "dummy");

                    Assert(File.Exists(lockFile), "Lock file must exist before cleanup");
                    Assert(File.Exists(crashFile), "Crash file must exist before cleanup");
                    Assert(File.Exists(singleton), "Singleton lock must exist before cleanup");

                    InstallerEngine.CleanStaleSessionAndCrashState(cfg, isPortable: false);

                    Assert(!File.Exists(lockFile), "Lock file must be deleted by cleanup");
                    Assert(!File.Exists(crashFile), "Crash file must be deleted by cleanup");
                    Assert(!File.Exists(singleton), "Singleton lock must be deleted by cleanup");
                }
                finally
                {
                    string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), cfg.DefaultInstallFolderName);
                    if (Directory.Exists(dataDir))
                    {
                        try { Directory.Delete(dataDir, true); } catch { }
                    }
                }
            });

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("Results: {0} Passed, {1} Failed", _passed, _failed);
            Console.WriteLine("==================================================");

            if (_failed > 0)
            {
                Environment.Exit(1);
            }
        }

        private static void Test(string name, Action action)
        {
            try
            {
                action();
                Console.WriteLine("[PASS] " + name);
                _passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] " + name + ": " + ex.Message);
                _failed++;
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception("Assertion Failed: " + message);
            }
        }
    }
}
