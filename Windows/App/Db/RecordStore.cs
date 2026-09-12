using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HeartRateMonitor;

public static class RecordStore
{
    public const string Avatar = "avatar", Vrchat = "vrchat", Devices = "devices", HeartRate = "heart_rate", Hardware = "hardware";
    private static readonly object Sync = new();
    private static readonly Dictionary<string, DateOnly> LastCleanup = new();
    private static bool? _vrchatRunning;

    public static string RecordsDir => Path.Combine(App.DataDir, "records");

    public static void Write(string category, string eventName, object? data)
    {
        if (!Enabled(category)) return;
        var timestamp = DateTime.Now;
        var json = JsonSerializer.Serialize(data);
        lock (Sync)
        {
            foreach (var backend in App.Config.Recording.Backends.ToArray())
            {
                try
                {
                    switch (backend.ToLowerInvariant())
                    {
                        case "sqlite": WriteSqlite(category, timestamp, eventName, json); break;
                        case "jsonl": WriteJsonl(category, timestamp, eventName, json); break;
                        case "csv": WriteCsv(category, timestamp, eventName, json); break;
                    }
                }
                catch (Exception e) { Debug($"record {backend}/{category} write failed: {e.Message}"); }
            }
            CleanupCategory(category, false);
        }
    }

    public static void WriteHeartRate(string mac, string name, int bpm)
    {
        if (!Enabled(HeartRate)) return;
        var timestamp = DateTime.Now;
        var data = new { mac, name, bpm };
        var json = JsonSerializer.Serialize(data);
        lock (Sync)
        {
            foreach (var backend in App.Config.Recording.Backends.ToArray())
            {
                try
                {
                    if (backend.Equals("sqlite", StringComparison.OrdinalIgnoreCase)) HrmDb.InsertHeartRateRecord(timestamp, mac, name, bpm);
                    else if (backend.Equals("jsonl", StringComparison.OrdinalIgnoreCase)) WriteJsonl(HeartRate, timestamp, "heart_rate", json);
                    else if (backend.Equals("csv", StringComparison.OrdinalIgnoreCase)) WriteCsv(HeartRate, timestamp, "heart_rate", json);
                }
                catch (Exception e) { Debug($"record {backend}/{HeartRate} write failed: {e.Message}"); }
            }
            CleanupCategory(HeartRate, false);
        }
    }

    public static void WriteVrchatStatus(bool running, object snapshot)
    {
        lock (Sync)
        {
            if (!Enabled(Vrchat)) { _vrchatRunning = null; return; }
            if (_vrchatRunning == null)
            {
                _vrchatRunning = running;
                if (!running) return;
            }
            else
            {
                if (_vrchatRunning == running) return;
                _vrchatRunning = running;
            }
        }
        Write(Vrchat, running ? "start" : "stop", running ? snapshot : new { running = false });
    }

    public static void CleanupAll(bool force = true)
    {
        lock (Sync)
            foreach (var category in new[] { Avatar, Vrchat, Devices, HeartRate, Hardware }) CleanupCategory(category, force);
    }

    private static bool Enabled(string category)
    {
        if (!HrmDb.Recording) return false;
        var cfg = App.Config.Recording;
        return category switch
        {
            Avatar => cfg.Avatar, Vrchat => cfg.Vrchat, Devices => cfg.Devices,
            HeartRate => cfg.HeartRate, Hardware => cfg.Hardware, _ => false,
        };
    }

    private static void WriteSqlite(string category, DateTime ts, string eventName, string data)
    {
        var table = category switch
        {
            Avatar => "avatar_records", Vrchat => "vrchat_records", Devices => "device_records",
            Hardware => "hardware_records", _ => "",
        };
        if (table.Length > 0) HrmDb.InsertRecord(table, ts, eventName, data);
    }

    private static void WriteJsonl(string category, DateTime ts, string eventName, string data)
    {
        var file = DailyFile(category, ts, "jsonl");
        var raw = $"{{\"ts\":{JsonSerializer.Serialize(ts.ToString("O"))},\"event\":{JsonSerializer.Serialize(eventName)},\"data\":{data}}}";
        File.AppendAllText(file, raw + Environment.NewLine, new UTF8Encoding(false));
    }

    private static void WriteCsv(string category, DateTime ts, string eventName, string data)
    {
        var file = DailyFile(category, ts, "csv");
        if (!File.Exists(file)) File.AppendAllText(file, "ts,event,data" + Environment.NewLine, new UTF8Encoding(false));
        File.AppendAllText(file, $"{Csv(ts.ToString("O"))},{Csv(eventName)},{Csv(data)}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private static string DailyFile(string category, DateTime ts, string extension)
    {
        var dir = Path.Combine(RecordsDir, category);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{ts:yyyy-MM-dd}.{extension}");
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static void CleanupCategory(string category, bool force)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!force && LastCleanup.TryGetValue(category, out var last) && last == today) return;
        LastCleanup[category] = today;
        var days = Retention(category);
        if (days <= 0) return;
        var cutoff = DateTime.Today.AddDays(-days);
        try
        {
            var table = category switch { Avatar => "avatar_records", Vrchat => "vrchat_records", Devices => "device_records", HeartRate => "hr_records", Hardware => "hardware_records", _ => "" };
            if (table.Length > 0) HrmDb.DeleteOlderThan(table, cutoff);
        }
        catch (Exception e) { Debug($"record sqlite/{category} cleanup failed: {e.Message}"); }
        try
        {
            var dir = Path.Combine(RecordsDir, category);
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir))
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < cutoff)
                    File.Delete(file);
        }
        catch (Exception e) { Debug($"record files/{category} cleanup failed: {e.Message}"); }
    }

    private static int Retention(string category)
    {
        var r = App.Config.Recording.RetentionDays;
        return Math.Max(0, category switch { Avatar => r.Avatar, Vrchat => r.Vrchat, Devices => r.Devices, HeartRate => r.HeartRate, Hardware => r.Hardware, _ => 0 });
    }

    private static void Debug(string message) { try { App.Log.Debug(message); } catch { } }
}
