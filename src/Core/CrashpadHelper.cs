using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace wb.Core
{
    /// <summary>
    /// Provides integrated Crashpad crash reporting, native minidump generation,
    /// crash_reporter.cfg configuration and unhandled exception diagnostics for wb.
    /// Ensures that when an unmanaged or managed crash occurs, diagnostic details
    /// and minidump files (.dmp) are saved and displayed rather than crashing silently.
    /// </summary>
    public static class CrashpadHelper
    {
        private static readonly object _crashLock = new object();
        private static bool _hasCrashed = false;
        private static string _lastKnownUrl = "wb://newtab";
        private static bool _handlersRegistered = false;

        #region Native MiniDump Definitions

        [Flags]
        public enum MINIDUMP_TYPE : uint
        {
            MiniDumpNormal = 0x00000000,
            MiniDumpWithDataSegs = 0x00000001,
            MiniDumpWithFullMemory = 0x00000002,
            MiniDumpWithHandleData = 0x00000004,
            MiniDumpFilterMemory = 0x00000008,
            MiniDumpScanMemory = 0x00000010,
            MiniDumpWithUnloadedModules = 0x00000020,
            MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
            MiniDumpFilterModulePaths = 0x00000080,
            MiniDumpWithProcessThreadData = 0x00000100,
            MiniDumpWithPrivateReadWriteMemory = 0x00000200,
            MiniDumpWithoutOptionalData = 0x00000400,
            MiniDumpWithFullMemoryInfo = 0x00000800,
            MiniDumpWithThreadInfo = 0x00001000,
            MiniDumpWithCodeSegs = 0x00002000
        }

        [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            SafeFileHandle hFile,
            uint dumpType,
            IntPtr expParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);

        [DllImport("kernel32.dll", SetLastError = false)]
        private static extern ulong GetTickCount64();

        #endregion

        #region Properties & State

        public static string LastKnownUrl
        {
            get { return _lastKnownUrl; }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    _lastKnownUrl = value;
                }
            }
        }

        public static bool IsCrashReportingEnabled
        {
            get
            {
                try
                {
                    Type cefType = FindCefType();
                    if (cefType != null)
                    {
                        PropertyInfo prop = cefType.GetProperty("CrashReportingEnabled", BindingFlags.Public | BindingFlags.Static);
                        if (prop != null)
                        {
                            object val = prop.GetValue(null, null);
                            if (val is bool) return (bool)val;
                        }
                    }
                }
                catch { }
                return false;
            }
        }

        private static readonly object _configLock = new object();
        private static bool? _isDumpSavingEnabled = null;

        /// <summary>
        /// Gets or sets whether crash minidumps (.dmp) should be automatically generated and saved.
        /// Defaults to false to conserve disk space. When false, only lightweight text crash reports (.txt)
        /// in the crashes folder are saved and any leftover .dmp files are purged.
        /// </summary>
        public static bool IsDumpSavingEnabled
        {
            get
            {
                lock (_configLock)
                {
                    if (!_isDumpSavingEnabled.HasValue)
                    {
                        try
                        {
                            var cfg = StorageHelper.LoadObject<CrashConfigModel>("crash_config.json");
                            _isDumpSavingEnabled = cfg != null ? cfg.SaveCrashDumps : false;
                        }
                        catch
                        {
                            _isDumpSavingEnabled = false;
                        }
                    }
                    return _isDumpSavingEnabled.Value;
                }
            }
            set
            {
                lock (_configLock)
                {
                    _isDumpSavingEnabled = value;
                    try
                    {
                        var cfg = new CrashConfigModel { SaveCrashDumps = value };
                        StorageHelper.SaveObject("crash_config.json", cfg);
                    }
                    catch { }

                    if (!value)
                    {
                        PurgeDumpsToSaveSpace();
                    }
                }
            }
        }

        /// <summary>
        /// Deletes all .dmp minidump files in the dumps directory to save disk space.
        /// </summary>
        public static void PurgeDumpsToSaveSpace()
        {
            try
            {
                string crashesDir = GetCrashReportsDirectory();
                if (Directory.Exists(crashesDir))
                {
                    var dmpFiles = new DirectoryInfo(crashesDir).GetFiles("*.dmp");
                    foreach (var f in dmpFiles)
                    {
                        try { f.Delete(); } catch { }
                    }
                }

                // Also clean up any legacy 'dumps' directory if present
                string oldDumps = Path.Combine(StorageHelper.GetDataDirectory(), "dumps");
                if (Directory.Exists(oldDumps))
                {
                    var oldFiles = new DirectoryInfo(oldDumps).GetFiles("*.dmp");
                    foreach (var f in oldFiles)
                    {
                        try { f.Delete(); } catch { }
                    }
                    try { Directory.Delete(oldDumps, true); } catch { }
                }

                // Clean up any unwanted Chromium 'User Data\Crashpad' directory
                RemoveChromiumCrashpadDirectory();
            }
            catch { }
        }

        #endregion

        #region Directory & File Helpers

        /// <summary>
        /// Gets the local Crashpad database directory where minidumps and crash metrics are stored.
        /// Defaults to: %LOCALAPPDATA%\wb\crashes (unified with crash logs)
        /// </summary>
        public static string GetCrashpadDirectory()
        {
            string baseData = StorageHelper.GetDataDirectory();
            string crashpadDir = Path.Combine(baseData, "crashes");
            try
            {
                if (!Directory.Exists(crashpadDir))
                {
                    Directory.CreateDirectory(crashpadDir);
                }
            }
            catch { }
            return crashpadDir;
        }

        /// <summary>
        /// Gets the directory where active/pending Crashpad minidump reports (.dmp) are stored.
        /// Defaults to: %LOCALAPPDATA%\wb\crashes (unified with crash logs)
        /// </summary>
        public static string GetCrashReportsDirectory()
        {
            return GetCrashpadDirectory();
        }

        /// <summary>
        /// Gets the directory where human-readable crash logs (.txt) and diagnostic reports are saved.
        /// Defaults to: %LOCALAPPDATA%\wb\crashes
        /// </summary>
        public static string GetCrashLogsDirectory()
        {
            return GetCrashpadDirectory();
        }

        #endregion

        #region Crash Reporter Configuration & Chromium Crashpad Suppression

        /// <summary>
        /// Ensures any crash_reporter.cfg configuration file is removed from the application
        /// and core subdirectories to disable Chromium's internal Crashpad handler.
        /// Chromium's Crashpad unconditionally creates the unwanted "User Data\Crashpad" directory
        /// layout. wb uses its own native crash handler layout in %LOCALAPPDATA%\wb\crashes.
        /// </summary>
        public static void EnsureCrashReporterConfig()
        {
            RemoveCrashReporterConfig();
        }

        /// <summary>
        /// Removes crash_reporter.cfg from baseDir, core/ and system/ directories
        /// to ensure Chromium's early-loading framework (chrome_elf) does not activate
        /// Chromium's Crashpad or generate "User Data\Crashpad".
        /// </summary>
        public static void RemoveCrashReporterConfig()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string coreDir = Path.Combine(baseDir, "core");
                string systemDir = Path.Combine(baseDir, "system");

                string[] searchDirs = new string[] { baseDir, coreDir, systemDir };
                foreach (string dir in searchDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        string cfg = Path.Combine(dir, "crash_reporter.cfg");
                        if (File.Exists(cfg))
                        {
                            try { File.Delete(cfg); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.RemoveCrashReporterConfig error: " + ex.Message);
            }
        }

        /// <summary>
        /// Removes unwanted Chromium Crashpad database directories ("User Data\Crashpad")
        /// to ensure only wb's unified crash directory layout (%LOCALAPPDATA%\wb\crashes) is used.
        /// </summary>
        public static void RemoveChromiumCrashpadDirectory()
        {
            try
            {
                string baseData = StorageHelper.GetDataDirectory();
                string userDataDir = Path.Combine(baseData, "User Data");
                if (Directory.Exists(userDataDir))
                {
                    Directory.Delete(userDataDir, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.RemoveChromiumCrashpadDirectory error: " + ex.Message);
            }
        }

        #endregion

        #region CEF Configuration & Crash Keys

        /// <summary>
        /// Configures CefSettings / command-line arguments to disable Chromium's internal
        /// Crashpad handler. wb uses its own native crash handler and minidump generator
        /// storing reports directly in %LOCALAPPDATA%\wb\crashes.
        /// </summary>
        public static void ApplyCefCommandLineArgs(System.Collections.Generic.IDictionary<string, string> args)
        {
            try
            {
                RemoveCrashReporterConfig();
                RemoveChromiumCrashpadDirectory();

                if (args != null)
                {
                    // Disable Chromium's internal Crashpad to prevent the unwanted
                    // "User Data\Crashpad" directory layout. wb uses its own native crash handler
                    // in %LOCALAPPDATA%\wb\crashes.
                    args["disable-crash-reporter"] = "1";
                    if (args.ContainsKey("enable-crash-reporter"))
                    {
                        args.Remove("enable-crash-reporter");
                    }
                    if (args.ContainsKey("crash-dumps-dir"))
                    {
                        args.Remove("crash-dumps-dir");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.ApplyCefCommandLineArgs error: " + ex.Message);
            }
        }

        /// <summary>
        /// Called immediately after Cef.Initialize to check native Crashpad status and register initial crash keys.
        /// </summary>
        public static void InitializeCefCrashKeys()
        {
            try
            {
                bool active = IsCrashReportingEnabled;
                Debug.WriteLine(string.Format("[wb] Crashpad active: {0}", active));

                SetCrashKeyValue("prod", "wb");
                SetCrashKeyValue("ver", "1.0.0");
                SetCrashKeyValue("channel", "release");
                SetCrashKeyValue("platform", Environment.Is64BitProcess ? "x64" : "x86");
                if (!string.IsNullOrEmpty(_lastKnownUrl))
                {
                    SetCrashKeyValue("url", _lastKnownUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.InitializeCefCrashKeys error: " + ex.Message);
            }
        }

        /// <summary>
        /// Updates the current URL crash key and in-memory tracker.
        /// </summary>
        public static void SetCrashUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            _lastKnownUrl = url;
            SetCrashKeyValue("url", url);
        }

        /// <summary>
        /// Invokes Cef.SetCrashKeyValue(key, value) via reflection to avoid hard compile dependencies.
        /// </summary>
        public static void SetCrashKeyValue(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            try
            {
                Type cefType = FindCefType();
                if (cefType != null)
                {
                    MethodInfo mi = cefType.GetMethod("SetCrashKeyValue", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(string) }, null);
                    if (mi != null)
                    {
                        mi.Invoke(null, new object[] { key, value ?? string.Empty });
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Unhandled Exception Handlers

        /// <summary>
        /// Registers process-wide unhandled exception handlers so any uncaught exception
        /// on any thread (including CEF worker threads) generates diagnostic logs and a minidump.
        /// </summary>
        public static void RegisterCrashHandlers()
        {
            if (_handlersRegistered) return;
            _handlersRegistered = true;

            try
            {
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Exception ex = e.ExceptionObject as Exception;
                    if (ex == null && e.ExceptionObject != null)
                    {
                        ex = new Exception("Unhandled non-Exception object: " + e.ExceptionObject.ToString());
                    }
                    HandleFatalException(ex, "AppDomain.CurrentDomain.UnhandledException", e.IsTerminating);
                };

                Application.ThreadException += (s, e) =>
                {
                    if (e.Exception is ObjectDisposedException || e.Exception is InvalidOperationException)
                    {
                        // Harmless asynchronous control teardowns during form close
                        return;
                    }
                    HandleFatalException(e.Exception, "Application.ThreadException", false);
                };

                TaskScheduler.UnobservedTaskException += (s, e) =>
                {
                    try
                    {
                        Debug.WriteLine("[wb] UnobservedTaskException: " + (e.Exception != null ? e.Exception.Message : "Unknown"));
                        e.SetObserved();
                    }
                    catch { }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.RegisterCrashHandlers error: " + ex.Message);
            }
        }

        /// <summary>
        /// Translates internal or framework crash context identifiers into clean, human-readable labels.
        /// </summary>
        public static string FormatCrashContext(string rawContext, Exception ex = null)
        {
            if (string.IsNullOrWhiteSpace(rawContext))
            {
                return "Unknown Crash Context";
            }

            // If it's a self-initiated crash from tools menu or exception indicates so
            if (rawContext.IndexOf("SimulateCrash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (ex != null && ex.Message != null && ex.Message.IndexOf("Self-Initiated Crash", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "Simulated Crash (Debug)";
            }

            switch (rawContext)
            {
                case "AppDomain.CurrentDomain.UnhandledException":
                case "BackgroundThread":
                    if (ex != null && ex.TargetSite != null && !string.IsNullOrEmpty(ex.TargetSite.Name))
                    {
                        string declaring = ex.TargetSite.DeclaringType != null ? ex.TargetSite.DeclaringType.Name : "System";
                        return string.Format("Background Thread ({0}.{1})", declaring, ex.TargetSite.Name);
                    }
                    return "Background Thread Crash";

                case "Application.ThreadException":
                case "UiThread":
                    return "UI Thread Crash (Windows Forms)";

                case "Program.RunApplication":
                case "Startup":
                    return "Application Startup Failure";

                case "SessionAbruptTermination":
                    return "Abrupt Session Termination";

                case "RendererCrash":
                case "CefRenderProcessTerminated":
                    return "Web Renderer Subprocess Crash";

                case "SelfInitiatedCrash.Fallback":
                    return "Simulated Crash";

                default:
                    if (!rawContext.Contains("."))
                    {
                        return rawContext;
                    }
                    return rawContext.Replace(".", " ");
            }
        }

        /// <summary>
        /// Handles a fatal or unhandled exception by writing a human-readable crash report,
        /// generating a native .dmp minidump, outputting to console and displaying an informative crash dialog.
        /// </summary>
        public static void HandleFatalException(Exception ex, string context, bool isTerminating)
        {
            lock (_crashLock)
            {
                if (_hasCrashed && isTerminating)
                {
                    return;
                }
                _hasCrashed = true;
            }

            string readableContext = FormatCrashContext(context, ex);

            try
            {
                // Immediately hide and dispose active browser windows so the application does not continue functioning
                CloseAllBrowserWindows();

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string logsDir = GetCrashLogsDirectory();
                string reportsDir = GetCrashReportsDirectory();

                string reportFileName = string.Format("wb_crash_{0}.txt", timestamp);
                string reportPath = Path.Combine(logsDir, reportFileName);

                string dumpFileName = string.Format("wb_crash_{0}.dmp", timestamp);
                string dumpPath = Path.Combine(reportsDir, dumpFileName);

                // 1. Generate native minidump (.dmp) ONLY if user chose to save dumps (saves space)
                bool dumpCreated = false;
                if (IsDumpSavingEnabled)
                {
                    dumpCreated = CreateMiniDump(dumpPath);
                }

                // Log fatal error immediately to logcat
                try
                {
                    LogcatHelper.F("wb.Crash", string.Format("FATAL: {0} in {1}", ex != null ? ex.Message : "Unknown", readableContext));
                    if (ex != null && !string.IsNullOrEmpty(ex.StackTrace))
                    {
                        LogcatHelper.F("wb.Crash", ex.StackTrace);
                    }
                }
                catch { }

                // 2. Generate human-readable crash report
                string reportContent = BuildCrashReportText(ex, readableContext, isTerminating, dumpPath, dumpCreated);
                try
                {
                    File.WriteAllText(reportPath, reportContent, Encoding.UTF8);

                    // Also append to rolling crash.log
                    string rollingLog = Path.Combine(StorageHelper.GetDataDirectory(), "crash.log");
                    File.AppendAllText(rollingLog, string.Format("[{0}] {1}: {2}\nDump: {3}\nReport: {4}\n\n",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        readableContext,
                        ex != null ? ex.Message : "Unknown",
                        dumpCreated ? dumpPath : "None",
                        reportPath));

                    // Record unacknowledged crash state for startup reminder
                    RecordCrashState(ex != null ? ex.Message : "Unknown fatal error", readableContext, dumpCreated ? dumpPath : null, reportPath);
                }
                catch { }

                // 3. Print to Console.Error for terminal visibility
                try
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("==========================================================");
                    Console.Error.WriteLine("               wb Browser Fatal Crash Report              ");
                    Console.Error.WriteLine("==========================================================");
                    Console.Error.WriteLine(string.Format("Context:      {0}", readableContext));
                    Console.Error.WriteLine(string.Format("Error:        {0}", ex != null ? ex.Message : "Unknown"));
                    Console.Error.WriteLine(string.Format("Report File:  {0}", reportPath));
                    Console.Error.WriteLine(string.Format("Minidump:     {0}", dumpCreated ? dumpPath : "Disabled"));
                    Console.Error.WriteLine(string.Format("Crashpad Dir: {0}", reportsDir));
                    Console.Error.WriteLine("==========================================================");
                    if (ex != null && !string.IsNullOrEmpty(ex.StackTrace))
                    {
                        Console.Error.WriteLine(ex.StackTrace);
                        Console.Error.WriteLine("==========================================================");
                    }
                }
                catch { }

                // 4. Show graphical crash dialog so the user can immediately see what went wrong
                ShowCrashDialog(ex, readableContext, reportPath, dumpPath, dumpCreated, reportsDir);
            }
            catch (Exception fatalEx)
            {
                try
                {
                    MessageBox.Show(
                        string.Format("wb Browser Fatal Crash:\n\n{0}\n\nCrash Reporter Error:\n{1}",
                            ex != null ? ex.ToString() : "Unknown error",
                            fatalEx.Message),
                        AppStrings.CrashReminder.CrashNoticeWindowTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch { }
            }
            finally
            {
                // 5. Terminate the process with non-zero exit code so the OS and Debug Console recognize a real crash
                try
                {
                    LogcatHelper.F("wb.Crash", string.Format("Process terminating with exit code 1 due to crash in {0}.", readableContext));
                }
                catch { }

                try
                {
                    Environment.ExitCode = 1;
                    Environment.Exit(1);
                }
                catch
                {
                    try
                    {
                        Process.GetCurrentProcess().Kill();
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Logs a subprocess / renderer termination (crash, kill, or out-of-memory).
        /// </summary>
        public static void LogRenderProcessCrash(int status, string url)
        {
            try
            {
                string statusText = status.ToString();
                switch (status)
                {
                    case 0: statusText = "NormalTermination"; break;
                    case 1: statusText = "AbnormalTermination"; break;
                    case 2: statusText = "ProcessCrashed"; break;
                    case 3: statusText = "ProcessWasKilled"; break;
                    case 4: statusText = "OutOfMemory"; break;
                }

                string logsDir = GetCrashLogsDirectory();
                string logFile = Path.Combine(logsDir, "render_process_crashes.log");
                string entry = string.Format("[{0}] Renderer Terminated: {1} (Status code {2}) | URL: {3} | Crashpad Dumps: {4}\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    statusText,
                    status,
                    !string.IsNullOrEmpty(url) ? url : _lastKnownUrl,
                    GetCrashReportsDirectory());

                File.AppendAllText(logFile, entry);
                Debug.WriteLine("[wb] " + entry);
                LogcatHelper.E("wb.Renderer", string.Format("Renderer Terminated: {0} (Status {1}) | URL: {2}", statusText, status, !string.IsNullOrEmpty(url) ? url : _lastKnownUrl));
            }
            catch { }
        }

        #endregion

        #region MiniDump Generation

        /// <summary>
        /// Creates a native Windows minidump file (.dmp) for the current process using dbghelp.dll.
        /// </summary>
        public static bool CreateMiniDump(string dumpFilePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(dumpFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (FileStream fs = new FileStream(dumpFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    Process currentProcess = Process.GetCurrentProcess();
                    MINIDUMP_TYPE dumpType = MINIDUMP_TYPE.MiniDumpWithDataSegs |
                                            MINIDUMP_TYPE.MiniDumpWithHandleData |
                                            MINIDUMP_TYPE.MiniDumpWithUnloadedModules |
                                            MINIDUMP_TYPE.MiniDumpWithProcessThreadData;

                    return MiniDumpWriteDump(
                        currentProcess.Handle,
                        (uint)currentProcess.Id,
                        fs.SafeFileHandle,
                        (uint)dumpType,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.CreateMiniDump error: " + ex.Message);
                return false;
            }
        }

        #endregion

        #region Diagnostic Text Generation

        private static string BuildCrashReportText(Exception ex, string context, bool isTerminating, string dumpPath, bool dumpCreated)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("                        wb Browser Crash Diagnostic Report                      ");
            sb.AppendLine("================================================================================");
            sb.AppendLine(string.Format("Timestamp (UTC):     {0}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")));
            sb.AppendLine(string.Format("Timestamp (Local):   {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")));
            sb.AppendLine(string.Format("Crash Context:       {0}", context));
            sb.AppendLine(string.Format("Process Terminating: {0}", isTerminating));
            sb.AppendLine(string.Format("Last Known URL:      {0}", _lastKnownUrl));
            sb.AppendLine(string.Format("Save Dumps Enabled:  {0}", IsDumpSavingEnabled));
            sb.AppendLine(string.Format("wb Crashpad Active:  {0}", IsCrashReportingEnabled));
            sb.AppendLine(string.Format("Crashpad Reports:    {0}", GetCrashReportsDirectory()));
            sb.AppendLine(string.Format("Minidump File:       {0} (Created: {1})", dumpCreated ? dumpPath : "Disabled", dumpCreated));
            sb.AppendLine("================================================================================");
            sb.AppendLine();

            sb.AppendLine("--- Exception Details ---");
            if (ex != null)
            {
                sb.AppendLine(string.Format("Type:       {0}", ex.GetType().FullName));
                sb.AppendLine(string.Format("Message:    {0}", ex.Message));
                sb.AppendLine(string.Format("Source:     {0}", ex.Source));
                if (ex.TargetSite != null)
                {
                    sb.AppendLine(string.Format("TargetSite: {0}", ex.TargetSite.ToString()));
                }
                sb.AppendLine();
                sb.AppendLine("--- Stack Trace ---");
                sb.AppendLine(ex.StackTrace ?? "No stack trace available");

                int innerLevel = 1;
                Exception cur = ex.InnerException;
                while (cur != null)
                {
                    sb.AppendLine();
                    sb.AppendLine(string.Format("--- Inner Exception [{0}] ---", innerLevel++));
                    sb.AppendLine(string.Format("Type:       {0}", cur.GetType().FullName));
                    sb.AppendLine(string.Format("Message:    {0}", cur.Message));
                    sb.AppendLine(cur.StackTrace ?? "No inner stack trace");
                    cur = cur.InnerException;
                }
            }
            else
            {
                sb.AppendLine("No Exception object provided.");
            }

            sb.AppendLine();
            sb.AppendLine("--- System & Environment ---");
            try
            {
                Process proc = Process.GetCurrentProcess();
                sb.AppendLine(string.Format("OS Version:          {0}", Environment.OSVersion.VersionString));
                sb.AppendLine(string.Format("64-bit OS:           {0}", Environment.Is64BitOperatingSystem));
                sb.AppendLine(string.Format("64-bit Process:      {0}", Environment.Is64BitProcess));
                sb.AppendLine(string.Format(".NET CLR Version:    {0}", Environment.Version.ToString()));
                sb.AppendLine(string.Format("Process ID:          {0}", proc.Id));
                sb.AppendLine(string.Format("Working Set Memory:  {0:N0} KB", proc.WorkingSet64 / 1024));
                sb.AppendLine(string.Format("Private Memory:      {0:N0} KB", proc.PrivateMemorySize64 / 1024));
                sb.AppendLine(string.Format("Process Uptime:      {0}", DateTime.Now - proc.StartTime));
                sb.AppendLine(string.Format("Application BaseDir: {0}", AppDomain.CurrentDomain.BaseDirectory));
            }
            catch { }

            sb.AppendLine();
            sb.AppendLine("--- Loaded Modules ---");
            try
            {
                Process proc = Process.GetCurrentProcess();
                foreach (ProcessModule mod in proc.Modules)
                {
                    try
                    {
                        string name = mod.ModuleName;
                        if (name.IndexOf("cef", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("wb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            sb.AppendLine(string.Format("  - {0} ({1}) => {2}", mod.ModuleName, mod.FileVersionInfo.FileVersion, mod.FileName));
                        }
                    }
                    catch { }
                }
            }
            catch { }

            sb.AppendLine();
            sb.AppendLine("================================================================================");
            sb.AppendLine("End of Crash Diagnostic Report");
            sb.AppendLine("================================================================================");
            return sb.ToString();
        }

        #endregion

        #region User Interface Crash Dialog

        private static void ShowCrashDialog(Exception ex, string context, string reportPath, string dumpPath, bool dumpCreated, string reportsDir)
        {
            try
            {
                Form dialog = new Form();
                dialog.Tag = "CrashNoticeDialog";
                dialog.Text = AppStrings.CrashReminder.CrashNoticeWindowTitle;
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.ClientSize = new Size(680, 480);
                dialog.MinimumSize = new Size(540, 380);
                dialog.BackColor = Color.FromArgb(32, 33, 36);
                dialog.ForeColor = Color.FromArgb(232, 234, 237);
                dialog.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
                dialog.FormBorderStyle = FormBorderStyle.Sizable;
                dialog.MaximizeBox = false;
                dialog.ShowIcon = true;

                try
                {
                    dialog.Icon = wb.UI.WbAppIcon.GetAppIcon();
                    wb.UI.WbAppIcon.UpdateFormIcon(dialog);
                }
                catch { }

                dialog.HandleCreated += (s, e) =>
                {
                    try { wb.UI.UITheme.EnableImmersiveDarkMode(dialog.Handle); } catch { }
                };
                if (dialog.IsHandleCreated)
                {
                    try { wb.UI.UITheme.EnableImmersiveDarkMode(dialog.Handle); } catch { }
                }

                // Bottom Panel for Buttons (Docked to bottom, height 52)
                Panel btnPanel = new Panel();
                btnPanel.Dock = DockStyle.Bottom;
                btnPanel.Height = 52;
                btnPanel.BackColor = Color.FromArgb(28, 29, 31);
                dialog.Controls.Add(btnPanel);

                // Header Label
                Label header = new Label();
                header.Text = "The browser encountered an unexpected error.";
                header.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
                header.ForeColor = Color.FromArgb(242, 139, 130); // Soft red
                header.Location = new Point(20, 14);
                header.Size = new Size(640, 24);
                header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                dialog.Controls.Add(header);

                // Subtitle
                Label subtitle = new Label();
                subtitle.Text = string.Format("Crashpad report and minidump have been saved so you can see what went wrong.\nContext: {0}", context);
                subtitle.Location = new Point(20, 42);
                subtitle.Size = new Size(640, 36);
                subtitle.ForeColor = Color.FromArgb(189, 193, 198);
                subtitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                dialog.Controls.Add(subtitle);

                // Error Details Text Box
                TextBox txtDetails = new TextBox();
                txtDetails.Multiline = true;
                txtDetails.ReadOnly = true;
                txtDetails.ScrollBars = ScrollBars.Both;
                txtDetails.WordWrap = false;
                txtDetails.Font = new Font("Consolas", 8.75f);
                txtDetails.BackColor = Color.FromArgb(24, 25, 27);
                txtDetails.ForeColor = Color.FromArgb(218, 220, 224);
                txtDetails.Location = new Point(20, 96);
                txtDetails.Size = new Size(640, 320);
                txtDetails.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== ERROR SUMMARY ===");
                sb.AppendLine(string.Format("Message: {0}", ex != null ? ex.Message : "Unknown error"));
                if (ex != null)
                {
                    sb.AppendLine(string.Format("Type:    {0}", ex.GetType().FullName));
                }
                sb.AppendLine(string.Format("URL:     {0}", _lastKnownUrl));
                sb.AppendLine(string.Format("Minidump: {0}", dumpCreated ? dumpPath : "Disabled"));
                sb.AppendLine(string.Format("Report:   {0}", reportPath));
                sb.AppendLine();
                sb.AppendLine("=== STACK TRACE ===");
                if (ex != null && !string.IsNullOrEmpty(ex.StackTrace))
                {
                    sb.AppendLine(ex.StackTrace);
                }
                else
                {
                    sb.AppendLine("No stack trace available.");
                }
                txtDetails.Text = sb.ToString();
                dialog.Controls.Add(txtDetails);

                // Open Crash Folder Button
                Button btnOpenFolder = new Button();
                btnOpenFolder.Text = "Open Crash Folder";
                btnOpenFolder.Size = new Size(150, 32);
                btnOpenFolder.Location = new Point(20, 10);
                btnOpenFolder.FlatStyle = FlatStyle.Flat;
                btnOpenFolder.FlatAppearance.BorderColor = Color.FromArgb(95, 99, 104);
                btnOpenFolder.BackColor = Color.FromArgb(48, 49, 52);
                btnOpenFolder.ForeColor = Color.White;
                btnOpenFolder.Click += (s, e) =>
                {
                    try
                    {
                        string target = File.Exists(reportPath) ? reportPath : (File.Exists(dumpPath) ? dumpPath : reportsDir);
                        Process.Start("explorer.exe", string.Format("/select,\"{0}\"", target));
                    }
                    catch { }
                };
                btnPanel.Controls.Add(btnOpenFolder);

                // Copy Details Button
                Button btnCopy = new Button();
                btnCopy.Text = "Copy Details";
                btnCopy.Size = new Size(120, 32);
                btnCopy.Location = new Point(180, 10);
                btnCopy.FlatStyle = FlatStyle.Flat;
                btnCopy.FlatAppearance.BorderColor = Color.FromArgb(95, 99, 104);
                btnCopy.BackColor = Color.FromArgb(48, 49, 52);
                btnCopy.ForeColor = Color.White;
                btnCopy.Click += (s, e) =>
                {
                    try
                    {
                        Clipboard.SetText(txtDetails.Text);
                        btnCopy.Text = "Copied!";
                    }
                    catch { }
                };
                btnPanel.Controls.Add(btnCopy);

                // Close Button
                Button btnClose = new Button();
                btnClose.Text = "Close";
                btnClose.Size = new Size(100, 32);
                btnClose.Location = new Point(btnPanel.ClientSize.Width - 120, 10);
                btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                btnClose.FlatStyle = FlatStyle.Flat;
                btnClose.FlatAppearance.BorderColor = Color.FromArgb(138, 180, 248);
                btnClose.BackColor = Color.FromArgb(138, 180, 248);
                btnClose.ForeColor = Color.FromArgb(32, 33, 36);
                btnClose.Font = new Font("Segoe UI", 9.25f, FontStyle.Bold);
                btnClose.Click += (s, e) => dialog.Close();
                btnPanel.Controls.Add(btnClose);

                dialog.ShowDialog();
            }
            catch
            {
                try
                {
                    MessageBox.Show(
                        string.Format("wb Browser Encountered an Unhandled Error:\n\n{0}\n\nCrash Report:\n{1}\n\nCrashpad Minidump:\n{2}",
                            ex != null ? ex.Message : "Unknown",
                            reportPath,
                            dumpCreated ? dumpPath : reportsDir),
                        AppStrings.CrashReminder.CrashNoticeWindowTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch { }
            }
        }

        #endregion

        #region Crash State & Startup Reminder

        private static bool _startupReminderChecked = false;

        public static string GetCrashStateFilePath()
        {
            return Path.Combine(StorageHelper.GetDataDirectory(), "last_crash.json");
        }

        public static string GetSessionLockFilePath()
        {
            return Path.Combine(StorageHelper.GetDataDirectory(), "session_active.lock");
        }

        /// <summary>
        /// Called at application startup to detect whether a prior session terminated abruptly without clean exit.
        /// </summary>
        public static void OnSessionStart()
        {
            try
            {
                string lockFile = GetSessionLockFilePath();
                if (File.Exists(lockFile))
                {
                    bool isObsoleteOrStaleLock = false;

                    // 1. Check if the current browser executable was installed or updated AFTER the lock was written
                    try
                    {
                        string exePath = Application.ExecutablePath;
                        if (File.Exists(exePath))
                        {
                            DateTime exeWriteUtc = File.GetLastWriteTimeUtc(exePath);
                            DateTime lockWriteUtc = File.GetLastWriteTimeUtc(lockFile);
                            if (exeWriteUtc > lockWriteUtc.AddSeconds(2))
                            {
                                // Newly installed or updated browser executable: the lock belonged to a prior installation
                                isObsoleteOrStaleLock = true;
                            }
                        }
                    }
                    catch { }

                    // 2. Check if the lock was written before the current Windows boot session (normal system shutdown/restart)
                    if (!isObsoleteOrStaleLock)
                    {
                        try
                        {
                            ulong uptimeMs = GetTickCount64();
                            if (uptimeMs > 0)
                            {
                                DateTime bootTimeUtc = DateTime.UtcNow.AddMilliseconds(-(double)uptimeMs);
                                DateTime lockWriteUtc = File.GetLastWriteTimeUtc(lockFile);
                                if (lockWriteUtc < bootTimeUtc.AddMinutes(-1))
                                {
                                    // Lock file was written prior to current machine boot (clean system reboot/shutdown)
                                    isObsoleteOrStaleLock = true;
                                }
                            }
                        }
                        catch { }
                    }

                    if (isObsoleteOrStaleLock)
                    {
                        try { File.Delete(lockFile); } catch { }
                    }
                    else
                    {
                        bool prevProcRunning = false;
                        string lockContent = "";
                        try { lockContent = File.ReadAllText(lockFile); } catch { }

                        int prevPid = 0;
                        if (!string.IsNullOrEmpty(lockContent))
                        {
                            string[] parts = lockContent.Split(':');
                            if (parts.Length > 0) int.TryParse(parts[0], out prevPid);
                        }

                        if (prevPid > 0 && prevPid != Process.GetCurrentProcess().Id)
                        {
                            try
                            {
                                Process prev = Process.GetProcessById(prevPid);
                                if (prev != null && !prev.HasExited && prev.ProcessName.IndexOf("wb", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    prevProcRunning = true;
                                }
                            }
                            catch { }
                        }

                        if (!prevProcRunning)
                        {
                            CrashInfo currentInfo = GetPendingCrashReminder();
                            if (currentInfo == null)
                            {
                                string latestDmp = null;
                                if (IsDumpSavingEnabled)
                                {
                                    try
                                    {
                                        string dumpsDir = GetCrashReportsDirectory();
                                        if (Directory.Exists(dumpsDir))
                                        {
                                            var dmpFiles = new DirectoryInfo(dumpsDir).GetFiles("*.dmp");
                                            if (dmpFiles.Length > 0)
                                            {
                                                Array.Sort(dmpFiles, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                                                latestDmp = dmpFiles[0].FullName;
                                            }
                                        }
                                    }
                                    catch { }
                                }

                                RecordCrashState("Browser session terminated unexpectedly (abnormal termination or crash).", "SessionAbruptTermination", latestDmp, null);
                            }
                        }
                    }
                }

                // Clean up any obsolete Chromium crash_reporter.cfg or User Data\Crashpad directory
                RemoveCrashReporterConfig();
                RemoveChromiumCrashpadDirectory();

                // If user chose not to save dumps, purge any leftover dumps to save disk space
                if (!IsDumpSavingEnabled)
                {
                    PurgeDumpsToSaveSpace();
                }

                EnsureDirectory(Path.GetDirectoryName(lockFile));
                File.WriteAllText(lockFile, string.Format("{0}:{1}", Process.GetCurrentProcess().Id, DateTime.UtcNow.ToString("o")), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.OnSessionStart error: " + ex.Message);
            }
        }

        /// <summary>
        /// Called when the application is shutting down cleanly to release the active session lock.
        /// </summary>
        public static void OnSessionCleanExit()
        {
            try
            {
                string lockFile = GetSessionLockFilePath();
                if (File.Exists(lockFile))
                {
                    File.Delete(lockFile);
                }
            }
            catch { }
        }

        /// <summary>
        /// Records diagnostic details of a crash to last_crash.json with Acknowledged = false.
        /// </summary>
        public static void RecordCrashState(string errorMessage, string context, string dumpPath = null, string reportPath = null)
        {
            try
            {
                string filePath = GetCrashStateFilePath();
                EnsureDirectory(Path.GetDirectoryName(filePath));

                var dict = new Dictionary<string, object>
                {
                    { "Timestamp", DateTime.UtcNow.ToString("o") },
                    { "Error", errorMessage ?? "Unknown error" },
                    { "Context", context ?? "Unknown" },
                    { "DumpPath", dumpPath ?? string.Empty },
                    { "ReportPath", reportPath ?? string.Empty },
                    { "Acknowledged", false }
                };

                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(dict);
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.RecordCrashState error: " + ex.Message);
            }
        }

        /// <summary>
        /// Retrieves pending crash reminder information if a prior crash occurred and has not yet been acknowledged.
        /// If no crash occurred or if the crash was already acknowledged, returns null ("if no crash then dont show").
        /// </summary>
        public static CrashInfo GetPendingCrashReminder()
        {
            try
            {
                string filePath = GetCrashStateFilePath();
                if (!File.Exists(filePath))
                {
                    return null;
                }

                string json = File.ReadAllText(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var serializer = new JavaScriptSerializer();
                var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                if (dict == null) return null;

                bool acknowledged = false;
                if (dict.ContainsKey("Acknowledged") && dict["Acknowledged"] is bool)
                {
                    acknowledged = (bool)dict["Acknowledged"];
                }

                if (acknowledged)
                {
                    return null;
                }

                CrashInfo info = new CrashInfo
                {
                    Acknowledged = false,
                    ErrorMessage = dict.ContainsKey("Error") ? dict["Error"] as string : "Unknown error",
                    Context = dict.ContainsKey("Context") ? dict["Context"] as string : "Unknown",
                    DumpPath = dict.ContainsKey("DumpPath") ? dict["DumpPath"] as string : string.Empty,
                    ReportPath = dict.ContainsKey("ReportPath") ? dict["ReportPath"] as string : string.Empty
                };

                if (dict.ContainsKey("Timestamp"))
                {
                    DateTime ts;
                    if (DateTime.TryParse(dict["Timestamp"] as string, null, System.Globalization.DateTimeStyles.RoundtripKind, out ts))
                    {
                        info.TimestampUtc = ts;
                    }
                    else
                    {
                        info.TimestampUtc = DateTime.UtcNow;
                    }
                }
                else
                {
                    info.TimestampUtc = DateTime.UtcNow;
                }

                return info;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.GetPendingCrashReminder error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Marks the current crash state as acknowledged so subsequent startups will not prompt the user again.
        /// </summary>
        public static void AcknowledgeCrashReminder()
        {
            try
            {
                string filePath = GetCrashStateFilePath();
                if (!File.Exists(filePath)) return;

                string json = File.ReadAllText(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return;

                var serializer = new JavaScriptSerializer();
                var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                if (dict != null)
                {
                    dict["Acknowledged"] = true;
                    string updated = serializer.Serialize(dict);
                    File.WriteAllText(filePath, updated, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.AcknowledgeCrashReminder error: " + ex.Message);
            }
        }

        /// <summary>
        /// Clears the crash state file and session lock (primarily for automated testing).
        /// </summary>
        public static void ClearCrashState()
        {
            try
            {
                string filePath = GetCrashStateFilePath();
                if (File.Exists(filePath)) File.Delete(filePath);
                string lockFile = GetSessionLockFilePath();
                if (File.Exists(lockFile)) File.Delete(lockFile);
            }
            catch { }
        }

        /// <summary>
        /// Initiates a deliberate, real unhandled crash to test Crashpad minidumps, crash logs,
        /// and startup crash recovery.
        /// </summary>
        public static void TriggerSelfInitiatedCrash()
        {
            try
            {
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string dumpPath = IsDumpSavingEnabled ? Path.Combine(GetCrashReportsDirectory(), string.Format("wb_crash_{0}.dmp", timestamp)) : null;
                string reportPath = Path.Combine(GetCrashLogsDirectory(), string.Format("wb_crash_{0}.txt", timestamp));

                RecordCrashState("wb Self-Initiated Crash", "ToolsMenu.SimulateCrash", dumpPath, reportPath);

                // Immediately hide and dispose all browser windows so wb ceases functioning
                CloseAllBrowserWindows();

                // Terminating background thread throwing an unhandled exception to trigger real unhandled exception pipeline
                new System.Threading.Thread(() =>
                {
                    throw new ApplicationException("wb Self-Initiated Crash");
                })
                {
                    IsBackground = true,
                    Name = "SelfInitiatedCrashThread"
                }.Start();
            }
            catch (Exception ex)
            {
                HandleFatalException(ex, "SelfInitiatedCrash.Fallback", true);
            }
        }

        /// <summary>
        /// Immediately hides and disposes all active browser windows so the application stops functioning upon crash.
        /// </summary>
        public static void CloseAllBrowserWindows()
        {
            try
            {
                if (Application.OpenForms != null && Application.OpenForms.Count > 0)
                {
                    List<Form> forms = new List<Form>();
                    foreach (Form f in Application.OpenForms)
                    {
                        if (f != null && !f.IsDisposed && !(f.Tag is string && (string)f.Tag == "CrashNoticeDialog"))
                        {
                            forms.Add(f);
                        }
                    }

                    foreach (Form f in forms)
                    {
                        try
                        {
                            if (f.InvokeRequired)
                            {
                                f.BeginInvoke(new Action(() =>
                                {
                                    try
                                    {
                                        f.Hide();
                                        f.Dispose();
                                    }
                                    catch { }
                                }));
                            }
                            else
                            {
                                f.Hide();
                                f.Dispose();
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Checks whether the previous browser session crashed. If so, displays a simple
        /// reminder dialog asking if the user wants to open the crashes folder. If no crash occurred, does nothing.
        /// </summary>
        public static void CheckAndPromptStartupCrashReminder(Form parentForm)
        {
            if (_startupReminderChecked) return;
            _startupReminderChecked = true;

            try
            {
                CrashInfo crash = GetPendingCrashReminder();
                if (crash == null)
                {
                    // No crash or already acknowledged: do nothing!
                    return;
                }

                // If dump saving is disabled, ensure any dump created during the crash is purged to save space
                if (!IsDumpSavingEnabled)
                {
                    if (!string.IsNullOrEmpty(crash.DumpPath) && File.Exists(crash.DumpPath))
                    {
                        try { File.Delete(crash.DumpPath); } catch { }
                    }
                    PurgeDumpsToSaveSpace();
                }

                DialogResult result = MessageBox.Show(
                    parentForm != null && !parentForm.IsDisposed && parentForm.Visible ? parentForm : null,
                    AppStrings.CrashReminder.SimpleCrashPrompt,
                    AppStrings.CrashReminder.DialogTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (result == DialogResult.Yes)
                {
                    OpenCrashesFolder(crash.ReportPath);
                }

                AcknowledgeCrashReminder();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[wb] CrashpadHelper.CheckAndPromptStartupCrashReminder error: " + ex.Message);
            }
        }

        /// <summary>
        /// Opens the human-readable crashes folder (%LOCALAPPDATA%\wb\crashes) in Windows Explorer,
        /// selecting the specified report file if it exists.
        /// </summary>
        public static void OpenCrashesFolder(string reportFilePath = null)
        {
            try
            {
                string crashesDir = GetCrashLogsDirectory();
                if (Directory.Exists(crashesDir))
                {
                    if (!string.IsNullOrEmpty(reportFilePath) && File.Exists(reportFilePath))
                    {
                        Process.Start("explorer.exe", string.Format("/select,\"{0}\"", reportFilePath));
                    }
                    else
                    {
                        var files = new DirectoryInfo(crashesDir).GetFiles("wb_crash_*.txt");
                        if (files.Length > 0)
                        {
                            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                            Process.Start("explorer.exe", string.Format("/select,\"{0}\"", files[0].FullName));
                        }
                        else
                        {
                            Process.Start("explorer.exe", crashesDir);
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Opens the unified crashes folder (%LOCALAPPDATA%\wb\crashes) in Windows Explorer.
        /// Retained for backwards compatibility.
        /// </summary>
        public static void OpenDumpsFolder()
        {
            OpenCrashesFolder();
        }

        private static void EnsureDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            {
                try { Directory.CreateDirectory(path); } catch { }
            }
        }

        #endregion

        #region Reflection Helper

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

        #endregion
    }

    /// <summary>
    /// Represents structured metadata about a detected browser crash.
    /// </summary>
    public class CrashInfo
    {
        public DateTime TimestampUtc { get; set; }
        public string ErrorMessage { get; set; }
        public string Context { get; set; }
        public string DumpPath { get; set; }
        public string ReportPath { get; set; }
        public bool Acknowledged { get; set; }
    }

    /// <summary>
    /// Configuration model for crash dump settings. Persisted to %LOCALAPPDATA%\wb\crash_config.json.
    /// </summary>
    public class CrashConfigModel
    {
        public bool SaveCrashDumps { get; set; }

        public CrashConfigModel()
        {
            SaveCrashDumps = false; // Disabled by default to save disk space
        }
    }
}
