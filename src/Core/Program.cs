using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using wb.Browser;
using wb.UI;

namespace wb.Core
{
    static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("shcore.dll", SetLastError = true)]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const uint WM_COPYDATA = 0x004A;

        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

        private static void EnableHighDpiAwareness()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10)
                {
                    if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                    {
                        return;
                    }
                }
            }
            catch { }

            try
            {
                SetProcessDpiAwareness(2); // PROCESS_PER_MONITOR_DPI_AWARE
                return;
            }
            catch { }

            try
            {
                SetProcessDPIAware();
            }
            catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            // Explicit Application User Model ID decouples window from raw EXE icon heuristics
            try
            {
                SetCurrentProcessExplicitAppUserModelID("wb.Browser.App");
            }
            catch { }

            // Early OS compatibility check
            if (Environment.OSVersion.Version.Major < 10)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MessageBox.Show(
                    "wb browser requires Windows 10 or newer. Windows 7, 8, and 8.1 are not supported.",
                    "Unsupported Operating System",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // 0. Early check for out-of-process debug console runner
            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--debug-console", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args[i], "--console", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args[i], "--logcat", StringComparison.OrdinalIgnoreCase))
                    {
                        int targetPid = 0;
                        if (i + 1 < args.Length)
                        {
                            int.TryParse(args[i + 1], out targetPid);
                        }
                        LogcatHelper.RunLogcatConsole(targetPid);
                        return;
                    }
                }
            }

            StorageHelper.Initialize(args);

            // Single-instance protection: prevent multiple instances from locking the same profile directory.
            // If another instance of wb is already running with this profile, activate its window, forward URL and exit cleanly.
            string profileDir = StorageHelper.GetProfileDirectory();
            string mutexSuffix = profileDir.ToLowerInvariant().Replace('\\', '_').Replace(':', '_').Replace('/', '_');
            string mutexName = @"Local\wb_SingleInstance_" + mutexSuffix;

            bool isFirstInstance = false;
            System.Threading.Mutex singleInstanceMutex = null;
            try
            {
                singleInstanceMutex = new System.Threading.Mutex(true, mutexName, out isFirstInstance);
            }
            catch (System.Threading.AbandonedMutexException)
            {
                // Previous instance terminated unexpectedly without releasing mutex; current process takes ownership.
                isFirstInstance = true;
            }
            catch
            {
                isFirstInstance = true;
            }

            if (!isFirstInstance)
            {
                // Another instance is already running with this profile.
                // Bring existing instance to front, forward any requested URL and exit cleanly without crashing.
                if (TryActivateExistingInstance(args))
                {
                    if (singleInstanceMutex != null)
                    {
                        try { singleInstanceMutex.Close(); } catch { }
                    }
                    return;
                }
            }

            try
            {
                // Register process-wide crash handlers immediately to capture unhandled exceptions and native crashes
                CrashpadHelper.RegisterCrashHandlers();

                CrashpadHelper.OnSessionStart();
                LogcatHelper.Initialize();

                // Ensure Chromium's internal Crashpad and crash_reporter.cfg are disabled/removed
                CrashpadHelper.RemoveCrashReporterConfig();
                CrashpadHelper.RemoveChromiumCrashpadDirectory();

                // Check for --console or --debug startup switch to open debug console immediately
                if (args != null && args.Length > 0)
                {
                    foreach (string arg in args)
                    {
                        if (string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(arg, "--debug-console", StringComparison.OrdinalIgnoreCase))
                        {
                            LogcatHelper.ToggleDebugConsole();
                            break;
                        }
                    }
                }

                EnableHighDpiAwareness();
                UITheme.InitializeApplicationTheme();

                // Configure native and managed search paths for core/system subfolder
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string coreDir = Path.Combine(baseDir, "core");
                    if (!Directory.Exists(coreDir))
                    {
                        coreDir = Path.Combine(baseDir, "system");
                    }
                    if (Directory.Exists(coreDir))
                    {
                        SetDllDirectory(coreDir);
                        string pathEnv = Environment.GetEnvironmentVariable("PATH");
                        if (string.IsNullOrEmpty(pathEnv) || !pathEnv.Contains(coreDir))
                        {
                            Environment.SetEnvironmentVariable("PATH", coreDir + ";" + pathEnv);
                        }
                    }
                }
                catch { }

                AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblies;
                RunApplication(args);
            }
            finally
            {
                if (singleInstanceMutex != null)
                {
                    try
                    {
                        singleInstanceMutex.ReleaseMutex();
                        singleInstanceMutex.Close();
                    }
                    catch { }
                }
            }
        }

        private static bool TryActivateExistingInstance(string[] args)
        {
            try
            {
                System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess();
                System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName("wb");

                foreach (System.Diagnostics.Process p in processes)
                {
                    if (p.Id != current.Id)
                    {
                        IntPtr hWnd = p.MainWindowHandle;
                        if (hWnd == IntPtr.Zero)
                        {
                            // Enumerate top-level windows for this process if MainWindowHandle is not yet cached
                            IntPtr foundHwnd = IntPtr.Zero;
                            EnumWindows((wnd, lparam) =>
                            {
                                uint pid;
                                GetWindowThreadProcessId(wnd, out pid);
                                if (pid == (uint)p.Id && IsWindowVisible(wnd))
                                {
                                    foundHwnd = wnd;
                                    return false; // Stop enumeration
                                }
                                return true;
                            }, IntPtr.Zero);
                            hWnd = foundHwnd;
                        }

                        if (hWnd != IntPtr.Zero)
                        {
                            string urlToSend = ExtractStartupUrl(args);
                            if (string.Equals(urlToSend, InternalPageHelper.NewTabUrl, StringComparison.OrdinalIgnoreCase))
                            {
                                urlToSend = "";
                            }

                            byte[] bytes = System.Text.Encoding.UTF8.GetBytes((urlToSend ?? "") + "\0");
                            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                            try
                            {
                                COPYDATASTRUCT cds = new COPYDATASTRUCT
                                {
                                    dwData = (IntPtr)0x5742, // 'WB' magic
                                    cbData = bytes.Length,
                                    lpData = handle.AddrOfPinnedObject()
                                };
                                SendMessage(hWnd, WM_COPYDATA, current.Handle, ref cds);
                            }
                            finally
                            {
                                handle.Free();
                            }

                            if (IsIconic(hWnd))
                            {
                                ShowWindow(hWnd, SW_RESTORE);
                            }
                            else
                            {
                                ShowWindow(hWnd, SW_SHOW);
                            }
                            SetForegroundWindow(hWnd);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[wb] TryActivateExistingInstance error: " + ex.Message);
            }
            return false;
        }

        internal static string ExtractStartupUrl(string[] args)
        {
            if (args == null || args.Length == 0) return InternalPageHelper.NewTabUrl;

            // Collect non-switch arguments
            System.Collections.Generic.List<string> nonSwitchTokens = new System.Collections.Generic.List<string>();
            string candidate = null;

            for (int i = 0; i < args.Length; i++)
            {
                string token = (args[i] ?? "").Trim().Trim('"', '\'');
                if (string.IsNullOrEmpty(token)) continue;

                if (string.Equals(token, "--single-argument", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        candidate = args[i + 1];
                        break;
                    }
                    continue;
                }

                if (token.StartsWith("-", StringComparison.Ordinal) || token.StartsWith("/", StringComparison.Ordinal))
                {
                    continue; // Skip command line flags
                }

                nonSwitchTokens.Add(token);
                if (candidate == null)
                {
                    candidate = token;
                }
            }

            // Check if combined non-switch tokens form an existing file (e.g. unquoted path with spaces)
            if (nonSwitchTokens.Count > 1)
            {
                string joined = string.Join(" ", nonSwitchTokens.ToArray()).Trim().Trim('"', '\'');
                try
                {
                    if (File.Exists(joined))
                    {
                        return new Uri(Path.GetFullPath(joined)).AbsoluteUri;
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return NormalizeStartupUrl(candidate);
            }

            return InternalPageHelper.NewTabUrl;
        }

        internal static string NormalizeStartupUrl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return InternalPageHelper.NewTabUrl;
            string trimmed = raw.Trim().Trim('"', '\'', ' ');
            if (trimmed.StartsWith("-", StringComparison.Ordinal) || trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                return InternalPageHelper.NewTabUrl;
            }

            try
            {
                if (trimmed.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed;
                }

                if (File.Exists(trimmed))
                {
                    return new Uri(Path.GetFullPath(trimmed)).AbsoluteUri;
                }
            }
            catch { }

            return trimmed;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RunApplication(string[] args)
        {
            // Configure modern TLS 1.2 / TLS 1.3 protocols for all .NET web and CDN requests
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 |
                                                       SecurityProtocolType.Tls11 |
                                                       SecurityProtocolType.Tls |
                                                       (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
            }
            catch
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls;
                }
                catch
                {
                }
            }

            // Request 1ms Windows timer resolution for smooth animations and prompt timer dispatch
            try { TimeBeginPeriod(1); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                if (e.Exception is ObjectDisposedException || e.Exception is InvalidOperationException)
                {
                    return;
                }
            };

            // Cleanly remove active session lock on OS shutdown or logoff so next boot is not flagged as a crash
            try
            {
                Microsoft.Win32.SystemEvents.SessionEnding += (s, e) =>
                {
                    CrashpadHelper.OnSessionCleanExit();
                };
            }
            catch { }

            try
            {
                Application.ApplicationExit += (s, e) =>
                {
                    CrashpadHelper.OnSessionCleanExit();
                };
            }
            catch { }

            // Initialize theme accent color immediately on startup so all components, startpage and icons use the current theme
            ThemeManager.Initialize();

            // Clean up any legacy startpage.html file on launch
            InternalPageHelper.EnsureStartPageFile();

            string initialUrl = ExtractStartupUrl(args);

            try
            {
                // Configure CefSettings for responsive, smooth web browsing
                CefSettings settings = new CefSettings();

                // Whitelisted browser subprocess path (wb.sub.exe)
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string coreDir = Path.Combine(baseDir, "core");
                if (!Directory.Exists(coreDir))
                {
                    coreDir = Path.Combine(baseDir, "system");
                }
                string depDir = Directory.Exists(coreDir) ? coreDir : baseDir;

                string subprocessPath = Path.Combine(depDir, "wb.sub.exe");
                if (!File.Exists(subprocessPath))
                {
                    subprocessPath = Path.Combine(baseDir, "wb.sub.exe");
                }
                if (!File.Exists(subprocessPath))
                {
                    subprocessPath = Path.Combine(depDir, "wb.Subprocess.exe");
                }
                if (!File.Exists(subprocessPath))
                {
                    subprocessPath = Path.Combine(baseDir, "wb.Subprocess.exe");
                }
                if (!File.Exists(subprocessPath))
                {
                    subprocessPath = Path.Combine(depDir, "wb.BrowserSubprocess.exe");
                }
                if (File.Exists(subprocessPath))
                {
                    settings.BrowserSubprocessPath = subprocessPath;
                }

                // If running with a core or system directory, set locales and resources paths
                if (Directory.Exists(coreDir))
                {
                    string localesPath = Path.Combine(coreDir, "locales");
                    if (Directory.Exists(localesPath))
                    {
                        settings.LocalesDirPath = localesPath;
                    }
                    if (File.Exists(Path.Combine(coreDir, "resources.pak")))
                    {
                        settings.ResourcesDirPath = coreDir;
                    }
                }

                string profilePath = StorageHelper.GetProfileDirectory();
                string cachePath = StorageHelper.GetCacheDirectory();

                // Clean up stale lock files or corrupted state from prior crashes or forced terminations
                EnsureCleanProfileState(profilePath);

                settings.RootCachePath = profilePath;
                settings.CachePath = profilePath;
                SetCommandLineArg(settings.CefCommandLineArgs, "disk-cache-dir", cachePath);
                settings.MultiThreadedMessageLoop = true;
                settings.LogSeverity = LogSeverity.Verbose;
                settings.BackgroundColor = Cef.ColorSetARGB(255, 32, 33, 36);

                // Apply wb Crashpad crash reporting flags and report directories
                CrashpadHelper.ApplyCefCommandLineArgs(settings.CefCommandLineArgs);

                // File URL access permissions: allow local html/svg/image files to load and access sub-resources
                SetCommandLineArg(settings.CefCommandLineArgs, "allow-file-access-from-files", "1");
                SetCommandLineArg(settings.CefCommandLineArgs, "allow-universal-access-from-files", "1");
                SetCommandLineArg(settings.CefCommandLineArgs, "enable-local-file-accesses", "1");

                // Base display and media settings
                SetCommandLineArg(settings.CefCommandLineArgs, "enable-media-stream", "1");
                SetCommandLineArg(settings.CefCommandLineArgs, "force-dark-mode", "1");
                SetCommandLineArg(settings.CefCommandLineArgs, "blink-settings", "preferredColorScheme=0");
                SetCommandLineArg(settings.CefCommandLineArgs, "default-background-color", "ff202124");
                SetCommandLineArg(settings.CefCommandLineArgs, "disable-variations-safe-mode", "1");
                SetCommandLineArg(settings.CefCommandLineArgs, "hide-crash-restore-bubble", "1");
                SetCommandLineArg(settings.CefCommandLineArgs, "disable-session-crashed-bubble", "1");

                // WebRTC and network security settings:
                // Disable mDNS local IP hiding to prevent Chromium from binding UDP port 5353,
                // which triggers Windows Defender Firewall alerts and causes anti-bot verification timeouts
                SetCommandLineArg(settings.CefCommandLineArgs, "disable-features", "WebRtcHideLocalIpsWithMdns");
                SetCommandLineArg(settings.CefCommandLineArgs, "webrtc-ip-handling-policy", "default_public_interface_only");
                SetCommandLineArg(settings.CefCommandLineArgs, "force-webrtc-ip-handling-policy", "default_public_interface_only");

                // GPU & Hardware Compositing (hardware accelerated Direct3D 11 rendering)
                SetCommandLineArg(settings.CefCommandLineArgs, "enable-gpu-rasterization", "1");
                SetCommandLineArg(settings.CefCommandLineArgs, "ignore-gpu-blocklist", "1");
                SetCommandLineArg(settings.CefCommandLineArgs, "enable-accelerated-video-decode", "1");
                SetCommandLineArg(settings.CefCommandLineArgs, "disable-direct-composition-video-overlays", "1");
                SetCommandLineArg(settings.CefCommandLineArgs, "enable-smooth-scrolling", "1");
                
                // V8 Engine Tuning: Balanced perfmax without rammaxxing
                // 64MB semi-space gives ample nursery for heavy JS frameworks (React, Vue, Lit) without ballooning RAM
                SetCommandLineArg(settings.CefCommandLineArgs, "js-flags", "--max-semi-space-size=64 --concurrent-recompilation --turbo-fast-api-calls");

                // Standard Chrome User-Agent prevents YouTube from injecting intentional 5-second non-Chrome artificial throttling
                settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";

                // Apply startup proxy configuration if enabled
                AppServices.Proxy.ApplyCommandLineArgs(settings.CefCommandLineArgs);

                // Terminate browser subprocesses immediately if main process terminates
                CefSharpSettings.SubprocessExitIfParentProcessClosed = true;

                // Initialize CEF with self-healing recovery if stale locks or database corruption are present
                if (Cef.IsInitialized != true)
                {
                    bool initialized = false;
                    try
                    {
                        initialized = Cef.Initialize(settings, performDependencyCheck: false);
                    }
                    catch (Exception initEx)
                    {
                        System.Diagnostics.Debug.WriteLine("Cef.Initialize attempt failed: " + initEx.Message);
                    }

                    if (!initialized || Cef.IsInitialized != true)
                    {
                        TryRepairProfileOnFailure(profilePath);
                        try
                        {
                            initialized = Cef.Initialize(settings, performDependencyCheck: false);
                        }
                        catch (Exception retryEx)
                        {
                            System.Diagnostics.Debug.WriteLine("Cef.Initialize retry failed: " + retryEx.Message);
                        }
                    }

                    if (!initialized || Cef.IsInitialized != true)
                    {
                        throw new InvalidOperationException(
                            "Failed to initialize Chromium Embedded Framework (CEF).\n" +
                            "Please ensure all required CEF runtime files are present in the 'core' directory " +
                            "and that no background process is holding a lock on your profile directory.");
                    }
                }

                // Initialize wb Crashpad keys (prod, ver, channel, platform)
                CrashpadHelper.InitializeCefCrashKeys();

                // Run main browser window with multi-window application context
                Application.Run(new BrowserApplicationContext(new MainBrowserForm(initialUrl)));
            }
            catch (Exception ex)
            {
                CrashpadHelper.HandleFatalException(ex, "Program.RunApplication", true);
            }
            finally
            {
                try { TimeEndPeriod(1); } catch { }
                if (Cef.IsInitialized == true)
                {
                    try
                    {
                        Cef.Shutdown();
                    }
                    catch { }
                }
                try
                {
                    string profileDir = StorageHelper.GetProfileDirectory();
                    EnsureCleanProfileState(profileDir);
                    CrashpadHelper.OnSessionCleanExit();
                }
                catch { }
            }
        }

        private static void SetCommandLineArg(System.Collections.Generic.IDictionary<string, string> dict, string key, string value)
        {
            if (dict != null && key != null)
            {
                dict[key] = value;
            }
        }

        private static void EnsureCleanProfileState(string profilePath)
        {
            if (string.IsNullOrEmpty(profilePath) || !Directory.Exists(profilePath)) return;
            try
            {
                // Check whether another wb instance is currently running
                bool anotherInstanceRunning = false;
                try
                {
                    var current = System.Diagnostics.Process.GetCurrentProcess();
                    var allWb = System.Diagnostics.Process.GetProcessesByName("wb");
                    foreach (var p in allWb)
                    {
                        if (p.Id != current.Id)
                        {
                            anotherInstanceRunning = true;
                            break;
                        }
                    }
                }
                catch { }

                if (!anotherInstanceRunning)
                {
                    // Clean up stale Chromium single-instance locks left behind from prior crashes or forced terminations
                    string[] singletonFiles = new string[] { "SingletonLock", "SingletonCookie", "SingletonSocket" };
                    foreach (string sf in singletonFiles)
                    {
                        try
                        {
                            string p = Path.Combine(profilePath, sf);
                            if (File.Exists(p)) File.Delete(p);
                        }
                        catch { }
                    }

                    // Clean up stale LevelDB LOCK files that cause Chromium initialization and CHECK assertion failures
                    try
                    {
                        var lockFiles = Directory.GetFiles(profilePath, "LOCK", SearchOption.AllDirectories);
                        foreach (var lf in lockFiles)
                        {
                            try
                            {
                                File.Delete(lf);
                            }
                            catch { }
                        }
                    }
                    catch { }

                    // Guard against Chromium Variations Safe-Mode brick state if an unexpected shutdown occurred
                    try
                    {
                        string localStatePath = Path.Combine(profilePath, "Local State");
                        if (File.Exists(localStatePath))
                        {
                            string lsContent = File.ReadAllText(localStatePath);
                            if (lsContent.Contains("variations_crash_streak"))
                            {
                                lsContent = System.Text.RegularExpressions.Regex.Replace(
                                    lsContent,
                                    "\"variations_crash_streak\":\\s*\\d+",
                                    "\"variations_crash_streak\":0");
                                File.WriteAllText(localStatePath, lsContent);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void TryRepairProfileOnFailure(string profilePath)
        {
            if (string.IsNullOrEmpty(profilePath) || !Directory.Exists(profilePath)) return;
            try
            {
                // Remove all stale LOCK files
                var lockFiles = Directory.GetFiles(profilePath, "LOCK", SearchOption.AllDirectories);
                foreach (var lf in lockFiles)
                {
                    try
                    {
                        File.Delete(lf);
                    }
                    catch { }
                }

                // Remove singleton locks
                string[] singletons = new string[] { "SingletonLock", "SingletonCookie", "SingletonSocket" };
                foreach (string s in singletons)
                {
                    try
                    {
                        string sp = Path.Combine(profilePath, s);
                        if (File.Exists(sp)) File.Delete(sp);
                    }
                    catch { }
                }

                // If LevelDB transient state was corrupted, remove transient session/sync databases
                string defaultDir = Path.Combine(profilePath, "Default");
                string[] transientDirs = new string[] {
                    Path.Combine(defaultDir, "Session Storage"),
                    Path.Combine(defaultDir, "Sync Data"),
                    Path.Combine(defaultDir, "GPUCache"),
                    Path.Combine(defaultDir, "DawnGraphiteCache"),
                    Path.Combine(defaultDir, "DawnWebGPUCache"),
                    Path.Combine(defaultDir, "Code Cache")
                };
                foreach (string td in transientDirs)
                {
                    try
                    {
                        if (Directory.Exists(td)) Directory.Delete(td, true);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static System.Reflection.Assembly ResolveAssemblies(object sender, ResolveEventArgs args)
        {
            try
            {
                string shortName = new System.Reflection.AssemblyName(args.Name).Name;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string coreDir = Path.Combine(baseDir, "core");
                string systemDir = Path.Combine(baseDir, "system");

                var searchDirs = new System.Collections.Generic.List<string>();
                if (Directory.Exists(coreDir)) searchDirs.Add(coreDir);
                if (Directory.Exists(systemDir)) searchDirs.Add(systemDir);
                searchDirs.Add(baseDir);

                foreach (string dir in searchDirs)
                {
                    // 1. Specific mappings for shortened subprocess libraries
                    if (shortName.Equals("CefSharp.BrowserSubprocess.Core", StringComparison.OrdinalIgnoreCase))
                    {
                        string subCoreCandidate = Path.Combine(dir, "wb.sub.core.dll");
                        if (File.Exists(subCoreCandidate)) return System.Reflection.Assembly.LoadFrom(subCoreCandidate);
                        subCoreCandidate = Path.Combine(dir, "wb.sub.dll");
                        if (File.Exists(subCoreCandidate)) return System.Reflection.Assembly.LoadFrom(subCoreCandidate);
                        subCoreCandidate = Path.Combine(dir, "wb.Subprocess.Core.dll");
                        if (File.Exists(subCoreCandidate)) return System.Reflection.Assembly.LoadFrom(subCoreCandidate);
                        subCoreCandidate = Path.Combine(dir, "wb.BrowserSubprocess.Core.dll");
                        if (File.Exists(subCoreCandidate)) return System.Reflection.Assembly.LoadFrom(subCoreCandidate);
                    }

                    // 2. Direct wb replacement (e.g. CefSharp -> wb.dll, CefSharp.Core -> wb.Core.dll, CefSharp.WinForms -> wb.WinForms.dll)
                    string candidate = Path.Combine(dir, shortName.Replace("CefSharp", "wb") + ".dll");
                    if (File.Exists(candidate))
                    {
                        return System.Reflection.Assembly.LoadFrom(candidate);
                    }

                    // 3. Prefixed fallback (e.g. wb.CefSharp.dll)
                    candidate = Path.Combine(dir, "wb." + shortName + ".dll");
                    if (File.Exists(candidate))
                    {
                        return System.Reflection.Assembly.LoadFrom(candidate);
                    }

                    // 4. Original name fallback
                    candidate = Path.Combine(dir, shortName + ".dll");
                    if (File.Exists(candidate))
                    {
                        return System.Reflection.Assembly.LoadFrom(candidate);
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }

    public class BrowserApplicationContext : ApplicationContext
    {
        public BrowserApplicationContext(Form initialForm)
        {
            RegisterWindow(initialForm);
        }

        public static void RegisterWindow(Form form)
        {
            if (form == null) return;
            form.FormClosed += OnFormClosed;
            if (!form.Visible)
            {
                form.Show();
            }
        }

        public static void ExitApplication()
        {
            List<Form> openBrowserForms = new List<Form>();
            try
            {
                FormCollection openForms = Application.OpenForms;
                if (openForms != null)
                {
                    for (int i = 0; i < openForms.Count; i++)
                    {
                        try
                        {
                            Form f = openForms[i];
                            if (f != null && f is MainBrowserForm && !f.IsDisposed)
                            {
                                openBrowserForms.Add(f);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // Close each active MainBrowserForm
            foreach (Form form in openBrowserForms)
            {
                try
                {
                    if (!form.IsDisposed)
                    {
                        form.Close();
                    }
                }
                catch { }
            }

            // Verify if any MainBrowserForm is still open (e.g. user cancelled active downloads dialog)
            bool hasOpenWindows = false;
            try
            {
                FormCollection openForms = Application.OpenForms;
                if (openForms != null)
                {
                    for (int i = 0; i < openForms.Count; i++)
                    {
                        try
                        {
                            Form f = openForms[i];
                            if (f != null && f is MainBrowserForm && !f.IsDisposed)
                            {
                                hasOpenWindows = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // If all MainBrowserForms have closed, terminate the application loop
            if (!hasOpenWindows)
            {
                try
                {
                    Application.ExitThread();
                }
                catch { }
            }
        }

        private static void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            bool hasOpenWindows = false;
            try
            {
                FormCollection openForms = Application.OpenForms;
                if (openForms != null)
                {
                    for (int i = 0; i < openForms.Count; i++)
                    {
                        try
                        {
                            Form f = openForms[i];
                            if (f != null && f != sender && f is MainBrowserForm && !f.IsDisposed)
                            {
                                hasOpenWindows = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (!hasOpenWindows)
            {
                Application.ExitThread();
            }
        }
    }
}
