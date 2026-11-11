using System;
using System.IO;
using System.Runtime.InteropServices;

namespace wb.DataPortability
{
    /// <summary>
    /// Lightweight zero-dependency SQLite reader using Windows built-in winsqlite3.dll.
    /// Safely handles file-locks by taking a temporary snapshot before querying.
    /// </summary>
    public static class SqliteReader
    {
        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;

        private static bool? _isSupported = null;

        public static bool IsSupported
        {
            get
            {
                if (!_isSupported.HasValue)
                {
                    try
                    {
                        string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                        string dllPath = Path.Combine(sysDir, "winsqlite3.dll");
                        _isSupported = File.Exists(dllPath);
                    }
                    catch
                    {
                        _isSupported = false;
                    }
                }
                return _isSupported.Value;
            }
        }

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open16", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open16(string filename, out IntPtr db);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare16_v2", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare16_v2(IntPtr db, string zSql, int nByte, out IntPtr ppStmt, IntPtr pzTail);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr pStmt);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_text16", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text16(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int64", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_int(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr pStmt);

        public static string GetString(IntPtr stmt, int colIndex)
        {
            try
            {
                IntPtr ptr = sqlite3_column_text16(stmt, colIndex);
                if (ptr == IntPtr.Zero) return string.Empty;
                return Marshal.PtrToStringUni(ptr);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static long GetInt64(IntPtr stmt, int colIndex)
        {
            try
            {
                return sqlite3_column_int64(stmt, colIndex);
            }
            catch
            {
                return 0;
            }
        }

        public static int GetInt32(IntPtr stmt, int colIndex)
        {
            try
            {
                return sqlite3_column_int(stmt, colIndex);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Creates a safe snapshot copy of a database (including WAL and SHM files) in %TEMP%
        /// using non-exclusive read permissions to avoid conflicts with active browsers.
        /// </summary>
        public static string CreateSafeDatabaseSnapshot(string sourceDbPath, out string tempDir)
        {
            tempDir = null;
            if (string.IsNullOrEmpty(sourceDbPath) || !File.Exists(sourceDbPath)) return null;

            try
            {
                tempDir = Path.Combine(Path.GetTempPath(), "wb_import_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                string destPath = Path.Combine(tempDir, Path.GetFileName(sourceDbPath));
                CopyFileSafe(sourceDbPath, destPath);

                // Copy WAL and SHM companion files if present
                string walSrc = sourceDbPath + "-wal";
                if (File.Exists(walSrc))
                {
                    CopyFileSafe(walSrc, destPath + "-wal");
                }

                string shmSrc = sourceDbPath + "-shm";
                if (File.Exists(shmSrc))
                {
                    CopyFileSafe(shmSrc, destPath + "-shm");
                }

                return destPath;
            }
            catch
            {
                CleanupTempSnapshot(tempDir);
                tempDir = null;
                return null;
            }
        }

        public static void CleanupTempSnapshot(string tempDir)
        {
            if (string.IsNullOrEmpty(tempDir)) return;
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch { }
        }

        private static void CopyFileSafe(string srcPath, string destPath)
        {
            using (var src = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                src.CopyTo(dest);
            }
        }

        /// <summary>
        /// Executes a query on a database snapshot and invokes a callback for each returned row.
        /// </summary>
        public static bool Query(string dbPath, string sqlQuery, Action<IntPtr> rowCallback)
        {
            if (!IsSupported || string.IsNullOrEmpty(dbPath) || string.IsNullOrEmpty(sqlQuery) || rowCallback == null)
            {
                return false;
            }

            string tempDir;
            string snapshotPath = CreateSafeDatabaseSnapshot(dbPath, out tempDir);
            if (string.IsNullOrEmpty(snapshotPath)) return false;

            IntPtr db = IntPtr.Zero;
            IntPtr stmt = IntPtr.Zero;

            try
            {
                int rc = sqlite3_open16(snapshotPath, out db);
                if (rc != SQLITE_OK || db == IntPtr.Zero) return false;

                rc = sqlite3_prepare16_v2(db, sqlQuery, -1, out stmt, IntPtr.Zero);
                if (rc != SQLITE_OK || stmt == IntPtr.Zero) return false;

                while (sqlite3_step(stmt) == SQLITE_ROW)
                {
                    rowCallback(stmt);
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (stmt != IntPtr.Zero)
                {
                    try { sqlite3_finalize(stmt); } catch { }
                }
                if (db != IntPtr.Zero)
                {
                    try { sqlite3_close(db); } catch { }
                }
                CleanupTempSnapshot(tempDir);
            }
        }
    }
}

