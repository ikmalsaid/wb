using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CefSharp.BrowserSubprocess;

namespace wb.BrowserSubprocess
{
    /// <summary>
    /// Custom browser subprocess for wb.
    /// Handles renderer, GPU, utility and other subprocess tasks.
    /// Uses dynamic assembly resolution to support whitelisted wb.* assemblies.
    /// </summary>
    public static class Program
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(
            IntPtr hProcess,
            int processInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE processInformation,
            int processInformationSize);

        private const int ProcessPowerThrottling = 4;
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private static void OptimizeSubprocessExecution()
        {
            try
            {
                // Prioritize renderer/GPU threads so they receive prompt CPU scheduling
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;

                // Disable Windows 11 EcoQoS (Power Throttling) to prevent downclocking and E-core traps
                var throttleState = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
                    StateMask = 0
                };
                SetProcessInformation(Process.GetCurrentProcess().Handle, ProcessPowerThrottling, ref throttleState, Marshal.SizeOf(throttleState));
            }
            catch
            {
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                SetDllDirectory(baseDir);
            }
            catch { }

            // OptimizeSubprocessExecution();
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblies;
            return RunSubprocess(args);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int RunSubprocess(string[] args)
        {
            try
            {
                var browserProcessExe = new WcfBrowserSubprocessExecutable();
                return browserProcessExe.Main(args, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("wb.BrowserSubprocess error: " + ex.ToString());
                return -1;
            }
        }

        private static Assembly ResolveAssemblies(object sender, ResolveEventArgs args)
        {
            try
            {
                string shortName = new AssemblyName(args.Name).Name;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string coreDir = Path.Combine(baseDir, "core");
                string systemDir = Path.Combine(baseDir, "system");

                var searchDirs = new System.Collections.Generic.List<string>();
                searchDirs.Add(baseDir);
                if (Directory.Exists(coreDir)) searchDirs.Add(coreDir);
                if (Directory.Exists(systemDir)) searchDirs.Add(systemDir);

                foreach (string dir in searchDirs)
                {
                    // 1. Specific mappings for shortened subprocess libraries
                    if (shortName.Equals("CefSharp.BrowserSubprocess.Core", StringComparison.OrdinalIgnoreCase))
                    {
                        string subCoreCandidate = Path.Combine(dir, "wb.sub.core.dll");
                        if (File.Exists(subCoreCandidate)) return Assembly.LoadFrom(subCoreCandidate);
                        subCoreCandidate = Path.Combine(dir, "wb.sub.dll");
                        if (File.Exists(subCoreCandidate)) return Assembly.LoadFrom(subCoreCandidate);
                        subCoreCandidate = Path.Combine(dir, "wb.Subprocess.Core.dll");
                        if (File.Exists(subCoreCandidate)) return Assembly.LoadFrom(subCoreCandidate);
                        subCoreCandidate = Path.Combine(dir, "wb.BrowserSubprocess.Core.dll");
                        if (File.Exists(subCoreCandidate)) return Assembly.LoadFrom(subCoreCandidate);
                    }

                    // 2. Direct wb replacement (e.g. CefSharp -> wb.dll, CefSharp.Core -> wb.Core.dll)
                    string candidate = Path.Combine(dir, shortName.Replace("CefSharp", "wb") + ".dll");
                    if (File.Exists(candidate))
                    {
                        return Assembly.LoadFrom(candidate);
                    }

                    // 3. Prefixed fallback (e.g. wb.CefSharp.dll)
                    candidate = Path.Combine(dir, "wb." + shortName + ".dll");
                    if (File.Exists(candidate))
                    {
                        return Assembly.LoadFrom(candidate);
                    }

                    // 4. Original name fallback
                    candidate = Path.Combine(dir, shortName + ".dll");
                    if (File.Exists(candidate))
                    {
                        return Assembly.LoadFrom(candidate);
                    }
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
