using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace wb.Core
{
    /// <summary>
    /// Thread-safe, high-performance native SQLite database engine for wb Browser,
    /// powered by Windows built-in winsqlite3.dll. Operates in Write-Ahead Logging (WAL)
    /// mode for atomic, non-blocking concurrent reads and microsecond write latency.
    /// </summary>
    public class SqliteDatabase : IDisposable
    {
        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;

        private static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        private IntPtr _db = IntPtr.Zero;
        private readonly object _lock = new object();
        private string _dbPath;
        private bool _isDisposed = false;

        #region P/Invoke Signatures for winsqlite3.dll

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open16", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open16(string filename, out IntPtr db);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_exec", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, string sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare16_v2", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare16_v2(IntPtr db, string zSql, int nByte, out IntPtr ppStmt, IntPtr pzTail);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr pStmt);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr pStmt);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_changes", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_changes(IntPtr db);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_null", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_null(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_int", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int(IntPtr pStmt, int iCol, int val);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_int64", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int64(IntPtr pStmt, int iCol, long val);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_double", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_double(IntPtr pStmt, int iCol, double val);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_bind_text16", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_text16(IntPtr pStmt, int iCol, string val, int len, IntPtr dtor);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_text16", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text16(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int64", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_int(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_double", CallingConvention = CallingConvention.Cdecl)]
        private static extern double sqlite3_column_double(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_type", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_type(IntPtr pStmt, int iCol);

        #endregion

        public string DatabasePath
        {
            get { return _dbPath; }
        }

        public bool IsOpen
        {
            get { return _db != IntPtr.Zero && !_isDisposed; }
        }

        public SqliteDatabase(string databasePath = null)
        {
            Initialize(databasePath);
        }

        public void Initialize(string databasePath = null)
        {
            lock (_lock)
            {
                if (IsOpen)
                {
                    Close();
                }

                if (string.IsNullOrEmpty(databasePath))
                {
                    databasePath = Path.Combine(StorageHelper.GetDataDirectory(), "wb.db");
                }

                _dbPath = databasePath;
                string dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                int rc = sqlite3_open16(_dbPath, out _db);
                if (rc != SQLITE_OK || _db == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to open SQLite database at: " + _dbPath);
                }

                // Enable Write-Ahead Logging (WAL), memory mapping, and large page cache for maximum throughput
                ExecutePragma("journal_mode = WAL");
                ExecutePragma("synchronous = NORMAL");
                ExecutePragma("temp_store = MEMORY");
                ExecutePragma("cache_size = -64000"); // 64MB memory page cache
                ExecutePragma("mmap_size = 268435456"); // 256MB memory-mapped I/O

                int version = 0;
                Query("PRAGMA user_version;", stmt =>
                {
                    version = SqliteDatabase.GetInt32(stmt, 0);
                });

                if (version < 1)
                {
                    InTransaction(() =>
                    {
                        EnsureSchemaCreated();
                        ExecutePragma("user_version = 1");
                    });
                }
            }
        }

        private void ExecutePragma(string pragmaStatement)
        {
            IntPtr err;
            sqlite3_exec(_db, "PRAGMA " + pragmaStatement + ";", IntPtr.Zero, IntPtr.Zero, out err);
        }

        private void EnsureSchemaCreated()
        {
            // 1. History Table
            ExecuteNonQuery(
                "CREATE TABLE IF NOT EXISTS history (" +
                "  id TEXT PRIMARY KEY," +
                "  title TEXT," +
                "  url TEXT NOT NULL," +
                "  visited_at INTEGER NOT NULL," +
                "  visit_count INTEGER NOT NULL DEFAULT 1" +
                ");");
            ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_history_url ON history(url);");
            ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_history_visited ON history(visited_at DESC);");

            // 2. Bookmarks Table
            ExecuteNonQuery(
                "CREATE TABLE IF NOT EXISTS bookmarks (" +
                "  id TEXT PRIMARY KEY," +
                "  title TEXT NOT NULL," +
                "  url TEXT," +
                "  created_at INTEGER NOT NULL," +
                "  parent_id TEXT NOT NULL DEFAULT 'bar'," +
                "  is_folder INTEGER NOT NULL DEFAULT 0," +
                "  position INTEGER NOT NULL DEFAULT 0" +
                ");");
            ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_bookmarks_parent ON bookmarks(parent_id, position);");
            ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_bookmarks_url ON bookmarks(url);");

            // 3. Shortcuts Table
            ExecuteNonQuery(
                "CREATE TABLE IF NOT EXISTS shortcuts (" +
                "  id TEXT PRIMARY KEY," +
                "  title TEXT NOT NULL," +
                "  url TEXT NOT NULL," +
                "  badge_text TEXT," +
                "  color_rgb INTEGER NOT NULL," +
                "  position INTEGER NOT NULL DEFAULT 0" +
                ");");

            // 4. Downloads Table
            ExecuteNonQuery(
                "CREATE TABLE IF NOT EXISTS downloads (" +
                "  id TEXT PRIMARY KEY," +
                "  url TEXT NOT NULL," +
                "  full_path TEXT," +
                "  file_name TEXT NOT NULL," +
                "  total_bytes INTEGER NOT NULL DEFAULT 0," +
                "  received_bytes INTEGER NOT NULL DEFAULT 0," +
                "  state TEXT NOT NULL," +
                "  start_time INTEGER NOT NULL," +
                "  end_time INTEGER," +
                "  mime_type TEXT," +
                "  error_message TEXT" +
                ");");
            ExecuteNonQuery("CREATE INDEX IF NOT EXISTS idx_downloads_start ON downloads(start_time DESC);");

            // 5. General Settings (Key-Value store)
            ExecuteNonQuery(
                "CREATE TABLE IF NOT EXISTS settings (" +
                "  key TEXT PRIMARY KEY," +
                "  value TEXT NOT NULL" +
                ");");
        }

        #region Query and Execution Methods

        public int ExecuteNonQuery(string sql, params object[] parameters)
        {
            lock (_lock)
            {
                if (!IsOpen) return 0;

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    int rc = sqlite3_prepare16_v2(_db, sql, -1, out stmt, IntPtr.Zero);
                    if (rc != SQLITE_OK || stmt == IntPtr.Zero) return 0;

                    BindParameters(stmt, parameters);
                    sqlite3_step(stmt);
                    return sqlite3_changes(_db);
                }
                finally
                {
                    if (stmt != IntPtr.Zero)
                    {
                        try { sqlite3_finalize(stmt); } catch { }
                    }
                }
            }
        }

        public T ExecuteScalar<T>(string sql, params object[] parameters)
        {
            lock (_lock)
            {
                if (!IsOpen) return default(T);

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    int rc = sqlite3_prepare16_v2(_db, sql, -1, out stmt, IntPtr.Zero);
                    if (rc != SQLITE_OK || stmt == IntPtr.Zero) return default(T);

                    BindParameters(stmt, parameters);
                    if (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        return ReadColumn<T>(stmt, 0);
                    }
                    return default(T);
                }
                finally
                {
                    if (stmt != IntPtr.Zero)
                    {
                        try { sqlite3_finalize(stmt); } catch { }
                    }
                }
            }
        }

        public void Query(string sql, Action<IntPtr> rowCallback, params object[] parameters)
        {
            if (rowCallback == null) return;

            lock (_lock)
            {
                if (!IsOpen) return;

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    int rc = sqlite3_prepare16_v2(_db, sql, -1, out stmt, IntPtr.Zero);
                    if (rc != SQLITE_OK || stmt == IntPtr.Zero) return;

                    BindParameters(stmt, parameters);
                    while (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        rowCallback(stmt);
                    }
                }
                finally
                {
                    if (stmt != IntPtr.Zero)
                    {
                        try { sqlite3_finalize(stmt); } catch { }
                    }
                }
            }
        }

        public void InTransaction(Action action)
        {
            if (action == null) return;

            lock (_lock)
            {
                if (!IsOpen) return;

                ExecuteNonQuery("BEGIN IMMEDIATE TRANSACTION;");
                try
                {
                    action();
                    ExecuteNonQuery("COMMIT;");
                }
                catch
                {
                    try { ExecuteNonQuery("ROLLBACK;"); } catch { }
                    throw;
                }
            }
        }

        #endregion

        #region Parameter Binding and Column Reading Helpers

        private void BindParameters(IntPtr stmt, object[] parameters)
        {
            if (parameters == null || parameters.Length == 0) return;

            for (int i = 0; i < parameters.Length; i++)
            {
                int col = i + 1; // 1-based index in SQLite
                object p = parameters[i];

                if (p == null || p == DBNull.Value)
                {
                    sqlite3_bind_null(stmt, col);
                }
                else if (p is int)
                {
                    sqlite3_bind_int(stmt, col, (int)p);
                }
                else if (p is long)
                {
                    sqlite3_bind_int64(stmt, col, (long)p);
                }
                else if (p is bool)
                {
                    sqlite3_bind_int(stmt, col, (bool)p ? 1 : 0);
                }
                else if (p is double)
                {
                    sqlite3_bind_double(stmt, col, (double)p);
                }
                else if (p is float)
                {
                    sqlite3_bind_double(stmt, col, (double)(float)p);
                }
                else if (p is DateTime)
                {
                    sqlite3_bind_int64(stmt, col, ((DateTime)p).Ticks);
                }
                else
                {
                    string s = p.ToString();
                    sqlite3_bind_text16(stmt, col, s, -1, SQLITE_TRANSIENT);
                }
            }
        }

        public static string GetString(IntPtr stmt, int col)
        {
            try
            {
                IntPtr ptr = sqlite3_column_text16(stmt, col);
                if (ptr == IntPtr.Zero) return string.Empty;
                return Marshal.PtrToStringUni(ptr);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static long GetInt64(IntPtr stmt, int col)
        {
            try { return sqlite3_column_int64(stmt, col); } catch { return 0; }
        }

        public static int GetInt32(IntPtr stmt, int col)
        {
            try { return sqlite3_column_int(stmt, col); } catch { return 0; }
        }

        public static double GetDouble(IntPtr stmt, int col)
        {
            try { return sqlite3_column_double(stmt, col); } catch { return 0; }
        }

        public static bool GetBool(IntPtr stmt, int col)
        {
            try { return sqlite3_column_int(stmt, col) != 0; } catch { return false; }
        }

        public static DateTime GetDateTime(IntPtr stmt, int col)
        {
            try
            {
                long ticks = sqlite3_column_int64(stmt, col);
                if (ticks > 0)
                {
                    return new DateTime(ticks);
                }
            }
            catch { }
            return DateTime.Now;
        }

        public static DateTime? GetNullableDateTime(IntPtr stmt, int col)
        {
            try
            {
                if (sqlite3_column_type(stmt, col) == 5) return null; // SQLITE_NULL
                long ticks = sqlite3_column_int64(stmt, col);
                if (ticks > 0)
                {
                    return new DateTime(ticks);
                }
            }
            catch { }
            return null;
        }

        private T ReadColumn<T>(IntPtr stmt, int col)
        {
            Type t = typeof(T);
            if (t == typeof(string)) return (T)(object)GetString(stmt, col);
            if (t == typeof(int)) return (T)(object)GetInt32(stmt, col);
            if (t == typeof(long)) return (T)(object)GetInt64(stmt, col);
            if (t == typeof(bool)) return (T)(object)GetBool(stmt, col);
            if (t == typeof(double)) return (T)(object)GetDouble(stmt, col);
            if (t == typeof(DateTime)) return (T)(object)GetDateTime(stmt, col);
            return default(T);
        }

        #endregion

        #region Key-Value Settings API

        public string GetSetting(string key, string defaultValue = null)
        {
            string val = ExecuteScalar<string>("SELECT value FROM settings WHERE key = ?;", key);
            return val != null ? val : defaultValue;
        }

        public void SetSetting(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            ExecuteNonQuery("INSERT OR REPLACE INTO settings (key, value) VALUES (?, ?);", key, value ?? string.Empty);
        }

        public bool GetSettingBool(string key, bool defaultValue = false)
        {
            string val = GetSetting(key, null);
            if (val == null) return defaultValue;
            bool res;
            return bool.TryParse(val, out res) ? res : defaultValue;
        }

        public void SetSettingBool(string key, bool value)
        {
            SetSetting(key, value ? "true" : "false");
        }

        public int GetSettingInt(string key, int defaultValue = 0)
        {
            string val = GetSetting(key, null);
            if (val == null) return defaultValue;
            int res;
            return int.TryParse(val, out res) ? res : defaultValue;
        }

        public void SetSettingInt(string key, int value)
        {
            SetSetting(key, value.ToString());
        }

        #endregion

        public void Close()
        {
            lock (_lock)
            {
                if (_db != IntPtr.Zero)
                {
                    try { sqlite3_close(_db); } catch { }
                    _db = IntPtr.Zero;
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (!_isDisposed)
                {
                    Close();
                    _isDisposed = true;
                }
            }
        }
    }
}
