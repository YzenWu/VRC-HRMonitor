using Microsoft.Data.Sqlite;
using HeartRateMonitor.Core;

namespace HeartRateMonitor;

/// <summary>
/// Local SQLite (hrm.db) - Heartrate/OSC real-time recording, state of health, custom variable.
/// Common with Rust frontend (rusqlite) All writing operations are chained.
/// </summary>
public static class HrmDb
{
    private static readonly object _lock = new();
    private static SqliteConnection? _conn;
    private static string? _dbPath;

    /// Whether <summary> is being recorded (the front-end record command controls). </summary>
    public static volatile bool Recording = true;

    public static string DbPath => _dbPath ??= System.IO.Path.Combine(App.DataDir, "hrm.db");

    public static void Init()
    {
        lock (_lock)
        {
            if (_conn != null) return;
            _conn = new SqliteConnection($"Data Source={DbPath}");
            _conn.Open();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS hr_records(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ts TEXT NOT NULL, mac TEXT, name TEXT, bpm INTEGER);
                CREATE TABLE IF NOT EXISTS osc_records(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ts TEXT NOT NULL, addr TEXT NOT NULL, value TEXT);
                CREATE TABLE IF NOT EXISTS health_records(
                    id INTEGER PRIMARY KEY AUTOINCREMENT, ts TEXT NOT NULL, status TEXT);
                CREATE TABLE IF NOT EXISTS variables(
                    name TEXT PRIMARY KEY, value TEXT);
                CREATE TABLE IF NOT EXISTS avatar_records(
                    id INTEGER PRIMARY KEY AUTOINCREMENT, ts TEXT NOT NULL, event TEXT NOT NULL, data TEXT);
                CREATE TABLE IF NOT EXISTS vrchat_records(
                    id INTEGER PRIMARY KEY AUTOINCREMENT, ts TEXT NOT NULL, event TEXT NOT NULL, data TEXT);
                CREATE TABLE IF NOT EXISTS device_records(
                    id INTEGER PRIMARY KEY AUTOINCREMENT, ts TEXT NOT NULL, event TEXT NOT NULL, data TEXT);
                CREATE TABLE IF NOT EXISTS hardware_records(
                    id INTEGER PRIMARY KEY AUTOINCREMENT, ts TEXT NOT NULL, event TEXT NOT NULL, data TEXT);
                CREATE TABLE IF NOT EXISTS toolkit_photos(
                    path TEXT PRIMARY KEY,
                    file_mtime TEXT NOT NULL,
                    author_id TEXT NOT NULL,
                    author_name TEXT NOT NULL,
                    world_id TEXT NOT NULL,
                    world_name TEXT NOT NULL,
                    instance_id TEXT NOT NULL,
                    captured_at TEXT NOT NULL,
                    players_json TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS idx_hr_ts  ON hr_records(ts);
                CREATE INDEX IF NOT EXISTS idx_osc_ts ON osc_records(ts);
                CREATE INDEX IF NOT EXISTS idx_avatar_ts ON avatar_records(ts);
                CREATE INDEX IF NOT EXISTS idx_vrchat_ts ON vrchat_records(ts);
                CREATE INDEX IF NOT EXISTS idx_device_ts ON device_records(ts);
                CREATE INDEX IF NOT EXISTS idx_hardware_ts ON hardware_records(ts);
                CREATE INDEX IF NOT EXISTS idx_toolkit_photos_captured ON toolkit_photos(captured_at);
                """;
            cmd.ExecuteNonQuery();
            // Phase 10 toolkit photos: the previous toolkit layout (size/modified_at/author/metadata columns)
            // is derived, rebuildable index data — drop and recreate when it lacks the new players_json column.
            using var probe = _conn.CreateCommand();
            probe.CommandText = "SELECT COUNT(*) FROM pragma_table_info('toolkit_photos') WHERE name='players_json'";
            if (Convert.ToInt64(probe.ExecuteScalar()) == 0)
            {
                using var migrate = _conn.CreateCommand();
                migrate.CommandText = """
                    DROP TABLE IF EXISTS toolkit_photos;
                    CREATE TABLE toolkit_photos(
                        path TEXT PRIMARY KEY,
                        file_mtime TEXT NOT NULL,
                        author_id TEXT NOT NULL,
                        author_name TEXT NOT NULL,
                        world_id TEXT NOT NULL,
                        world_name TEXT NOT NULL,
                        instance_id TEXT NOT NULL,
                        captured_at TEXT NOT NULL,
                        players_json TEXT NOT NULL);
                    CREATE INDEX idx_toolkit_photos_captured ON toolkit_photos(captured_at);
                    """;
                migrate.ExecuteNonQuery();
            }
        }
    }

    /// <summary> writes a heart rate record (without opening the record). </summary>
    public static void InsertHeartRate(string mac, string name, int bpm)
    {
        if (!Recording) return;
        try
        {
            lock (_lock)
            {
                Ensure();
                using var cmd = _conn!.CreateCommand();
                cmd.CommandText = "INSERT INTO hr_records(ts,mac,name,bpm) VALUES($t,$m,$n,$b)";
                cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString("O"));
                cmd.Parameters.AddWithValue("$m", mac ?? "");
                cmd.Parameters.AddWithValue("$n", name ?? "");
                cmd.Parameters.AddWithValue("$b", bpm);
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception e) { App.Log.Debug(LogText.L("log.db.hr_write_fail", e.Message)); }
    }

    /// <summary> writes a OSC acceptance record (without opening the record). </summary>
    public static void InsertOsc(string address, string value)
    {
        if (!Recording) return;
        try
        {
            lock (_lock)
            {
                Ensure();
                using var cmd = _conn!.CreateCommand();
                cmd.CommandText = "INSERT INTO osc_records(ts,addr,value) VALUES($t,$a,$v)";
                cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString("O"));
                cmd.Parameters.AddWithValue("$a", address ?? "");
                cmd.Parameters.AddWithValue("$v", value ?? "");
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception e) { App.Log.Debug(LogText.L("log.db.osc_write_fail", e.Message)); }
    }

    /// <summary> writes a health status record (not subject to Recording: state change is thin and necessary for analysis). </summary>
    public static void InsertHealth(string status)
    {
        try
        {
            lock (_lock)
            {
                Ensure();
                using var cmd = _conn!.CreateCommand();
                cmd.CommandText = "INSERT INTO health_records(ts,status) VALUES($t,$s)";
                cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString("O"));
                cmd.Parameters.AddWithValue("$s", status ?? "");
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception e) { App.Log.Debug(LogText.L("log.db.health_write_fail", e.Message)); }
    }

    /// <summary> reads the most recent line of a table (listed value) for export analysis with Monitor. </summary>
    public static List<Dictionary<string, object?>> Recent(string table, int limit)
    {
        var rows = new List<Dictionary<string, object?>>();
        // You can't paraphrase it, you can't inject it with a white list.
        if (table is not ("hr_records" or "osc_records" or "health_records" or "variables"
            or "avatar_records" or "vrchat_records" or "device_records" or "hardware_records")) return rows;
        try
        {
            lock (_lock)
            {
                Ensure();
                using var cmd = _conn!.CreateCommand();
                var order = table == "variables" ? "name" : "id DESC";
                cmd.CommandText = $"SELECT * FROM {table} ORDER BY {order} LIMIT $n";
                // #11 upper limit released: only lower limit 1 guaranteed. Bank level data is exported in sqlite format (File.Copy zero memory)
                cmd.Parameters.AddWithValue("$n", Math.Max(1, limit));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var row = new Dictionary<string, object?>();
                    for (var i = 0; i < r.FieldCount; i++)
                        row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                    rows.Add(row);
                }
            }
        }
        catch (Exception e) { App.Log.Debug(LogText.L("log.db.query_fail", table, e.Message)); }
        return rows;
    }

    /// <summary> writes/ updates a custom variable value. </summary>
    public static void SetVariable(string name, string value)
    {
        try
        {
            lock (_lock)
            {
                Ensure();
                using var cmd = _conn!.CreateCommand();
                cmd.CommandText = "INSERT INTO variables(name,value) VALUES($n,$v) ON CONFLICT(name) DO UPDATE SET value=$v";
                cmd.Parameters.AddWithValue("$n", name ?? "");
                cmd.Parameters.AddWithValue("$v", value ?? "");
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception e) { App.Log.Debug(LogText.L("log.db.variable_write_fail", e.Message)); }
    }

    /// <summary> parsies the absolute path of hrm.db (for export/statement). </summary>
    public static string PathOf() => DbPath;

    /// <summary>
    /// Performs read-only aggregate queries. <b>sql allows only </b> (recommended in MonitorStats) from the translation period.
    /// args parameterisation of all external inputs.
    /// </summary>
    internal static List<Dictionary<string, object?>> Rows(string sql, params (string Name, object? Value)[] args)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            lock (_lock)
            {
                Ensure();
                using var cmd = _conn!.CreateCommand();
                cmd.CommandText = sql;
                foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var row = new Dictionary<string, object?>(r.FieldCount);
                    for (var i = 0; i < r.FieldCount; i++)
                        row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                    rows.Add(row);
                }
            }
        }
        catch (Exception e) { App.Log.Debug(LogText.L("log.db.agg_query_fail", e.Message)); }
        return rows;
    }

    /// <summary>
    /// Executes a parameterized write statement (internal callers: toolkit photo upserts).
    /// The SQL text stays an internal constant; every external value must arrive as a bound parameter.
    /// </summary>
    internal static int Exec(string sql, params (string Name, object? Value)[] args)
    {
        lock (_lock)
        {
            Ensure();
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }
    }

    internal static void InsertRecord(string table, DateTime timestamp, string eventName, string data)
    {
        if (table is not ("avatar_records" or "vrchat_records" or "device_records" or "hardware_records")) return;
        lock (_lock)
        {
            Ensure();
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = $"INSERT INTO {table}(ts,event,data) VALUES($t,$e,$d)";
            cmd.Parameters.AddWithValue("$t", timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("$e", eventName);
            cmd.Parameters.AddWithValue("$d", data);
            cmd.ExecuteNonQuery();
        }
    }

    internal static void InsertHeartRateRecord(DateTime timestamp, string mac, string name, int bpm)
    {
        lock (_lock)
        {
            Ensure();
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = "INSERT INTO hr_records(ts,mac,name,bpm) VALUES($t,$m,$n,$b)";
            cmd.Parameters.AddWithValue("$t", timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("$m", mac ?? "");
            cmd.Parameters.AddWithValue("$n", name ?? "");
            cmd.Parameters.AddWithValue("$b", bpm);
            cmd.ExecuteNonQuery();
        }
    }

    internal static void DeleteOlderThan(string table, DateTime cutoff)
    {
        if (table is not ("hr_records" or "avatar_records" or "vrchat_records" or "device_records" or "hardware_records")) return;
        lock (_lock)
        {
            Ensure();
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = $"DELETE FROM {table} WHERE ts < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary> calculates the number of table lines (for UI display records). </summary>
    public static long Count(string table)
    {
        try
        {
            lock (_lock)
            {
                Ensure();
                using var cmd = _conn!.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }
        catch { return 0; }
    }

    private static void Ensure()
    {
        if (_conn == null) Init();
    }
}
