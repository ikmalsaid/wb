using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace wb.Core
{
    public static class MemoryUtils
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

        private static Timer _trimTimer;
        private static bool _isTrimming = false;

        public static void StartAutoTrimming(int intervalSeconds)
        {
            if (_trimTimer == null)
            {
                _trimTimer = new Timer(
                    new TimerCallback(TimerCallbackMethod),
                    null,
                    TimeSpan.FromSeconds(intervalSeconds),
                    TimeSpan.FromSeconds(intervalSeconds));
            }
        }

        private static void TimerCallbackMethod(object state)
        {
            TrimMemory();
        }

        public static void TrimMemory()
        {
            TrimMemory(false);
        }

        public static void TrimMemory(bool flushWorkingSet)
        {
            if (_isTrimming)
            {
                return;
            }

            _isTrimming = true;
            try
            {
                // 1. Collect managed .NET garbage without locking threads
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);

                if (flushWorkingSet)
                {
                    // 2. Reduce working set of the main browser process
                    using (Process currentProc = Process.GetCurrentProcess())
                    {
                        try
                        {
                            EmptyWorkingSet(currentProc.Handle);
                            SetProcessWorkingSetSize(currentProc.Handle, -1, -1);
                        }
                        catch
                        {
                        }
                    }

                    // 3. Reduce working set of all browser sub-processes
                    Process[] subProcesses = GetSubProcesses();
                    for (int i = 0; i < subProcesses.Length; i++)
                    {
                        Process p = subProcesses[i];
                        try
                        {
                            EmptyWorkingSet(p.Handle);
                            SetProcessWorkingSetSize(p.Handle, -1, -1);
                        }
                        catch
                        {
                        }
                        finally
                        {
                            p.Dispose();
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _isTrimming = false;
            }
        }

        public static long GetTotalMemoryMb()
        {
            long totalBytes = 0;
            try
            {
                using (Process currentProc = Process.GetCurrentProcess())
                {
                    totalBytes += currentProc.WorkingSet64;
                }

                Process[] subProcesses = GetSubProcesses();
                for (int i = 0; i < subProcesses.Length; i++)
                {
                    Process p = subProcesses[i];
                    try
                    {
                        totalBytes += p.WorkingSet64;
                    }
                    catch
                    {
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch
            {
            }
            return totalBytes / (1024 * 1024);
        }

        private static Process[] GetSubProcesses()
        {
            var list = new System.Collections.Generic.List<Process>();
            Process[] all = Process.GetProcesses();
            for (int i = 0; i < all.Length; i++)
            {
                Process p = all[i];
                try
                {
                    string name = p.ProcessName;
                    if (string.Equals(name, "wb.sub", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "wb.subprocess", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "wb.BrowserSubprocess", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "CefSharp.BrowserSubprocess", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(p);
                        continue;
                    }
                }
                catch { }
                p.Dispose();
            }
            return list.ToArray();
        }
    }
}
