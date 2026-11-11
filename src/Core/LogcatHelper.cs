using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace wb.Core
{
    /// <summary>
    /// Provides a real-time diagnostic logging system and out-of-process console host for wb.
    /// Supports live streaming, thread & component tagging, level coloring and a detached console
    /// window that remains open permanently even if wb.exe encounters a fatal crash or process abort.
    /// </summary>
    public static class LogcatHelper
    {
        private static readonly object _syncLock = new object();
        private static readonly List<string> _ringBuffer = new List<string>(256);
        private static readonly int _maxRingBufferCount = 200;
        private static bool _initialized = false;
        private static Process _activeConsoleProcess = null;

        #region Win32 Native Console & Window APIs

        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        #endregion

        #region Initialization & Trace Redirection

        /// <summary>
        /// Initializes the logcat subsystem, configures the log file and hooks Trace.Listeners.
        /// </summary>
        public static void Initialize()
        {
            lock (_syncLock)
            {
                if (_initialized) return;
                _initialized = true;

                try
                {
                    Trace.Listeners.Add(new LogcatTraceListener());
                }
                catch { }

                int pid = Process.GetCurrentProcess().Id;
                string arch = Environment.Is64BitProcess ? "x64" : "x86";
                I("wb.Core", string.Format("wb Browser starting up [PID: {0}, Arch: {1}, OS: {2}]", pid, arch, Environment.OSVersion.VersionString));
            }
        }

        #endregion

        #region Logging Methods

        public static void V(string tag, string message) { Log("V", tag, message); }
        public static void D(string tag, string message) { Log("D", tag, message); }
        public static void I(string tag, string message) { Log("I", tag, message); }
        public static void W(string tag, string message) { Log("W", tag, message); }
        public static void E(string tag, string message) { Log("E", tag, message); }
        public static void F(string tag, string message) { Log("F", tag, message); }

        /// <summary>
        /// Writes a structured logcat entry to the ring buffer and rolling wb.log file.
        /// Format: MM-dd HH:mm:ss.fff  PID  TID L Tag: Message
        /// </summary>
        public static void Log(string level, string tag, string message)
        {
            if (string.IsNullOrEmpty(level)) level = "D";
            if (string.IsNullOrEmpty(tag)) tag = "wb";
            if (message == null) message = string.Empty;

            int pid = Process.GetCurrentProcess().Id;
            int tid = Thread.CurrentThread.ManagedThreadId;
            string timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss.fff");

            string entry = string.Format("{0}  {1,5}  {2,5} {3} {4}: {5}",
                timestamp,
                pid,
                tid,
                level,
                tag,
                message);

            lock (_syncLock)
            {
                // 1. Maintain in-memory ring buffer
                if (_ringBuffer.Count >= _maxRingBufferCount)
                {
                    _ringBuffer.RemoveAt(0);
                }
                _ringBuffer.Add(entry);

                // 2. Append to rolling wb.log
                try
                {
                    string logFile = GetLogFilePath();
                    EnsureDirectory(Path.GetDirectoryName(logFile));

                    // Rotate if log exceeds 5 MB
                    if (File.Exists(logFile))
                    {
                        FileInfo fi = new FileInfo(logFile);
                        if (fi.Length > 5 * 1024 * 1024)
                        {
                            string oldLog = Path.Combine(Path.GetDirectoryName(logFile), "wb.old.log");
                            try
                            {
                                if (File.Exists(oldLog)) File.Delete(oldLog);
                                File.Move(logFile, oldLog);
                            }
                            catch { }
                        }
                    }

                    using (FileStream fs = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                    {
                        sw.WriteLine(entry);
                    }
                }
                catch { }
            }
        }

        public static string GetLogFilePath()
        {
            string baseDir = StorageHelper.GetDataDirectory();
            return Path.Combine(baseDir, "logs", "wb.log");
        }

        private static void EnsureDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            {
                try { Directory.CreateDirectory(path); } catch { }
            }
        }

        #endregion

        #region Debug Console Spawner (In-App Hotkey / Menu)

        /// <summary>
        /// Spawns or brings to front an out-of-process logcat debug console window.
        /// Because the console runs as an independent process, it stays open permanently even if wb.exe crashes.
        /// </summary>
        public static void ToggleDebugConsole()
        {
            try
            {
                if (_activeConsoleProcess != null && !_activeConsoleProcess.HasExited)
                {
                    IntPtr hwnd = _activeConsoleProcess.MainWindowHandle;
                    if (hwnd == IntPtr.Zero)
                    {
                        hwnd = GetProcessConsoleHwnd(_activeConsoleProcess.Id);
                    }
                    if (hwnd != IntPtr.Zero)
                    {
                        ShowWindow(hwnd, SW_RESTORE);
                        SetForegroundWindow(hwnd);
                        return;
                    }
                }

                // Launch independent debug console host targeting current process ID
                int currentPid = Process.GetCurrentProcess().Id;
                string exePath = Application.ExecutablePath;

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exePath;
                psi.Arguments = string.Format("--debug-console {0}", currentPid);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = false;

                _activeConsoleProcess = Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to launch wb Debug Console:\n\n" + ex.Message,
                    "wb Debug Console",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static IntPtr GetProcessConsoleHwnd(int pid)
        {
            // Fallback lookup if MainWindowHandle is not immediately populated
            return IntPtr.Zero;
        }

        #endregion

        #region Out-of-Process Debug Console Runner

        /// <summary>
        /// Executes the dedicated debug console loop (invoked when launched with --debug-console).
        /// Tails wb.log, highlights messages in color, monitors target PID and stays open if wb crashes.
        /// </summary>
        public static void RunLogcatConsole(int targetPid)
        {
            // 1. Attach to existing parent console (if launched from cmd/powershell) or allocate new console
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
            {
                AllocConsole();
            }

            try
            {
                // Configure standard streams for writing
                IntPtr stdOutHandle = GetConsoleWindow();
                if (stdOutHandle != IntPtr.Zero)
                {
                    ShowWindow(stdOutHandle, SW_SHOW);
                    SetForegroundWindow(stdOutHandle);
                }
            }
            catch { }

            try
            {
                Console.Title = targetPid > 0
                    ? string.Format("Debug Console [PID {0}] - wb", targetPid)
                    : "Debug Console - wb";
            }
            catch { }

            string logFile = GetLogFilePath();
            Process targetProcess = null;

            if (targetPid > 0)
            {
                try
                {
                    targetProcess = Process.GetProcessById(targetPid);
                }
                catch { }
            }

            if (targetProcess == null)
            {
                // Auto-detect running wb.exe if PID was not specified
                Process currentProc = Process.GetCurrentProcess();
                Process[] procs = Process.GetProcessesByName("wb");
                foreach (Process p in procs)
                {
                    if (p.Id != currentProc.Id)
                    {
                        targetProcess = p;
                        targetPid = p.Id;
                        break;
                    }
                }
            }

            // Print Header Banner
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.WriteLine("                           wb Debug Console                                     ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();
            Console.WriteLine(string.Format("Target Process:  {0}", targetProcess != null ? string.Format("wb.exe (PID {0})", targetPid) : "No active wb process attached"));
            Console.WriteLine(string.Format("Log File:        {0}", logFile));
            Console.WriteLine(string.Format("Crashpad Dumps:  {0}", CrashpadHelper.GetCrashReportsDirectory()));
            Console.WriteLine(string.Format("Diagnostic Logs: {0}", CrashpadHelper.GetCrashLogsDirectory()));
            Console.WriteLine("Note:            This console window will STAY OPEN if wb crashes or exits.");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.ResetColor();
            Console.WriteLine();

            // Seek to recent history (~16 KB before EOF) so the user gets context
            long lastReadPosition = 0;
            try
            {
                if (File.Exists(logFile))
                {
                    FileInfo fi = new FileInfo(logFile);
                    if (fi.Length > 16 * 1024)
                    {
                        lastReadPosition = fi.Length - (16 * 1024);
                    }
                }
            }
            catch { }

            bool targetHasExited = false;
            int exitCode = 0;

            // Main Tailing Loop
            while (true)
            {
                try
                {
                    if (File.Exists(logFile))
                    {
                        using (FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            if (lastReadPosition > fs.Length)
                            {
                                // File was truncated/rotated
                                lastReadPosition = 0;
                            }

                            if (fs.Length > lastReadPosition)
                            {
                                fs.Seek(lastReadPosition, SeekOrigin.Begin);
                                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
                                {
                                    string line;
                                    while ((line = reader.ReadLine()) != null)
                                    {
                                        PrintFormattedLogcatLine(line);
                                    }
                                    lastReadPosition = fs.Position;
                                }
                            }
                        }
                    }
                }
                catch { }

                // Check if target process has exited
                if (targetProcess != null && !targetHasExited)
                {
                    try
                    {
                        if (targetProcess.HasExited)
                        {
                            targetHasExited = true;
                            exitCode = targetProcess.ExitCode;
                        }
                    }
                    catch
                    {
                        targetHasExited = true;
                    }

                    if (targetHasExited)
                    {
                        // Sleep briefly and flush any last lines written right before termination
                        Thread.Sleep(200);
                        try
                        {
                            if (File.Exists(logFile))
                            {
                                using (FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                {
                                    if (fs.Length > lastReadPosition)
                                    {
                                        fs.Seek(lastReadPosition, SeekOrigin.Begin);
                                        using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
                                        {
                                            string line;
                                            while ((line = reader.ReadLine()) != null)
                                            {
                                                PrintFormattedLogcatLine(line);
                                            }
                                            lastReadPosition = fs.Position;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }

                        // Display prominent termination banner
                        Console.WriteLine();
                        if (exitCode != 0)
                        {
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.BackgroundColor = ConsoleColor.DarkRed;
                            Console.WriteLine(string.Format(" [FATAL] wb.exe (PID {0}) TERMINATED UNEXPECTEDLY! Exit Code: 0x{1:X8} ({1}) ", targetPid, exitCode));
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine(string.Format("[INFO] wb.exe (PID {0}) exited normally (Exit Code: 0).", targetPid));
                            Console.ResetColor();
                        }

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("================================================================================");
                        Console.WriteLine(" Console paused so you can inspect the crash log, copy details or scroll up.  ");
                        Console.WriteLine(" Press any key to close this console window...                                ");
                        Console.WriteLine("================================================================================");
                        Console.ResetColor();

                        // Block until key press
                        try
                        {
                            while (true)
                            {
                                Console.ReadKey(true);
                                break;
                            }
                        }
                        catch { }

                        return;
                    }
                }

                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Renders a single log line into the live console with clean column alignment,
        /// color-coded level badges and intelligent word-wrapping with hanging indents.
        /// Eliminates horizontal screen clutter and prevents words from breaking mid-token.
        /// </summary>
        public static void PrintFormattedLogcatLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            int consoleWidth = 100;
            try
            {
                int w = Console.WindowWidth;
                if (w > 20) consoleWidth = w;
            }
            catch { }

            int maxLineLen = Math.Max(20, consoleWidth - 1);

            string timePart;
            char levelChar;
            string tagPart;
            string messagePart;

            if (TryParseLogcatLine(line, out timePart, out levelChar, out tagPart, out messagePart))
            {
                ConsoleColor levelColor = GetLevelColor(levelChar);
                ConsoleColor msgColor = GetMessageColor(levelChar);

                // Format tag cleanly with fixed 13 characters
                string displayTag = tagPart.Length > 13 ? tagPart.Substring(0, 13) : tagPart.PadRight(13);

                // Prefix width: 12 (time) + 1 (sp) + 3 ([L]) + 1 (sp) + 13 (tag) + 3 (" : ") = 33
                const int prefixWidth = 33;
                int availWidth = Math.Max(15, maxLineLen - prefixWidth);

                List<string> wrappedLines = WrapMessage(messagePart, availWidth);
                string hangingIndent = new string(' ', prefixWidth);

                try
                {
                    // Print primary line with metadata prefix
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(timePart + " ");

                    Console.ForegroundColor = levelColor;
                    Console.Write("[" + levelChar + "] ");

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(displayTag);

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(" : ");

                    Console.ForegroundColor = msgColor;
                    Console.WriteLine(wrappedLines.Count > 0 ? wrappedLines[0] : string.Empty);

                    // Print wrapped continuation lines indented under message column
                    for (int i = 1; i < wrappedLines.Count; i++)
                    {
                        Console.Write(hangingIndent);
                        Console.ForegroundColor = msgColor;
                        Console.WriteLine(wrappedLines[i]);
                    }

                    Console.ResetColor();
                }
                catch
                {
                    try { Console.ResetColor(); } catch { }
                    Console.WriteLine(line);
                }
            }
            else
            {
                // Non-standard lines (e.g. stack traces, banners, or raw diagnostics)
                try
                {
                    bool isStackTrace = line.StartsWith("   at ") || line.StartsWith("   ---") || line.StartsWith("\tat ");
                    ConsoleColor fallbackColor = isStackTrace ? ConsoleColor.DarkRed : ConsoleColor.Gray;

                    int indent = isStackTrace ? 4 : 0;
                    int avail = Math.Max(15, maxLineLen - indent);
                    List<string> rawLines = WrapMessage(line.TrimStart(), avail);

                    Console.ForegroundColor = fallbackColor;
                    foreach (string rl in rawLines)
                    {
                        if (indent > 0) Console.Write(new string(' ', indent));
                        Console.WriteLine(rl);
                    }
                    Console.ResetColor();
                }
                catch
                {
                    try { Console.ResetColor(); } catch { }
                    Console.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Parses a raw entry from wb.log into its clean structural components.
        /// Extracts time (without date clutter), level badge, component tag and message body.
        /// </summary>
        public static bool TryParseLogcatLine(string rawLine, out string timePart, out char levelChar, out string tagPart, out string messagePart)
        {
            timePart = null;
            levelChar = 'D';
            tagPart = null;
            messagePart = null;

            if (string.IsNullOrWhiteSpace(rawLine) || rawLine.Length < 25)
            {
                return false;
            }

            // Expected raw format from LogcatHelper.Log:
            // "MM-dd HH:mm:ss.fff  PID  TID L Tag: Message"
            if (rawLine[2] == '-' && rawLine[5] == ' ' && rawLine[8] == ':' && rawLine[11] == ':' && rawLine[14] == '.')
            {
                // Clean time without MM-dd date clutter
                timePart = rawLine.Substring(6, 12);

                int colonIdx = rawLine.IndexOf(": ", 20, StringComparison.Ordinal);
                if (colonIdx > 20)
                {
                    string metaSection = rawLine.Substring(18, colonIdx - 18).Trim();
                    string[] parts = metaSection.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        for (int p = 0; p < parts.Length - 1; p++)
                        {
                            if (parts[p].Length == 1 && "VDIWEF".IndexOf(parts[p][0]) >= 0)
                            {
                                levelChar = parts[p][0];
                                tagPart = string.Join(" ", parts, p + 1, parts.Length - (p + 1));
                                messagePart = rawLine.Substring(colonIdx + 2);
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Splits a long message into cleanly wrapped lines at word boundaries.
        /// Handles long URLs, punctuation and embedded newlines without truncating text.
        /// </summary>
        public static List<string> WrapMessage(string text, int maxLineLength)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                lines.Add(string.Empty);
                return lines;
            }

            if (maxLineLength < 10) maxLineLength = 10;

            string[] rawLines = text.Replace("\r\n", "\n").Split('\n');
            foreach (string raw in rawLines)
            {
                if (raw.Length <= maxLineLength)
                {
                    lines.Add(raw);
                    continue;
                }

                int index = 0;
                while (index < raw.Length)
                {
                    int remaining = raw.Length - index;
                    if (remaining <= maxLineLength)
                    {
                        lines.Add(raw.Substring(index));
                        break;
                    }

                    int breakIndex = raw.LastIndexOf(' ', index + maxLineLength, maxLineLength);
                    if (breakIndex > index)
                    {
                        lines.Add(raw.Substring(index, breakIndex - index));
                        index = breakIndex + 1;
                    }
                    else
                    {
                        // No space break available (e.g. long URL or stack token), cut at maxLineLength
                        lines.Add(raw.Substring(index, maxLineLength));
                        index += maxLineLength;
                    }
                }
            }

            return lines;
        }

        private static ConsoleColor GetLevelColor(char level)
        {
            switch (level)
            {
                case 'V': return ConsoleColor.DarkGray;
                case 'D': return ConsoleColor.DarkCyan;
                case 'I': return ConsoleColor.Green;
                case 'W': return ConsoleColor.Yellow;
                case 'E': return ConsoleColor.Red;
                case 'F': return ConsoleColor.Magenta;
                default: return ConsoleColor.Gray;
            }
        }

        private static ConsoleColor GetMessageColor(char level)
        {
            switch (level)
            {
                case 'V': return ConsoleColor.DarkGray;
                case 'D': return ConsoleColor.Gray;
                case 'I': return ConsoleColor.White;
                case 'W': return ConsoleColor.Yellow;
                case 'E': return ConsoleColor.Red;
                case 'F': return ConsoleColor.Magenta;
                default: return ConsoleColor.Gray;
            }
        }

        #endregion

        #region Custom Trace Listener

        private class LogcatTraceListener : TraceListener
        {
            public override void Write(string message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Log("D", "wb.Trace", message.TrimEnd('\r', '\n'));
                }
            }

            public override void WriteLine(string message)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    Log("D", "wb.Trace", message.TrimEnd('\r', '\n'));
                }
            }
        }

        #endregion
    }
}
