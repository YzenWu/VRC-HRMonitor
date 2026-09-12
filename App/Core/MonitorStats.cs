namespace HeartRateMonitor.Core;

/// <summary>
/// Monitor (Phase 10): historical data aggregation and statistical analysis.
/// Entirely read-only and based on hrm.db; time ranges compare ISO-8601 strings because the ts column uses DateTime.ToString("O"), whose lexical order matches chronological order.
/// </summary>
public static class MonitorStats
{
    /// <summary>Optional time range in hours; 0 means all data.</summary>
    public static readonly int[] Ranges = { 1, 6, 24, 72, 168, 720, 0 };

    /// <summary>Build an aggregate report; hours=0 means no time limit.</summary>
    public static object Build(int hours, int buckets)
    {
        hours = hours < 0 ? 24 : hours;
        buckets = Math.Max(4, buckets); // #11: remove the upper limit because bucket count only controls array size; retain minimum 4
        // Use year 0001 as the lower bound for all-time ranges to avoid branching on whether a WHERE clause exists.
        var since = (hours == 0 ? DateTime.MinValue : DateTime.Now.AddHours(-hours)).ToString("O");

        return new
        {
            hours,
            since,
            hr = HeartRate(since, buckets),
            health = Health(since),
            devices = Devices(since),
            osc = Osc(since),
            db = new { file = HrmDb.PathOf(), recording = HrmDb.Recording },
        };
    }

    /// <summary>Heart rate: count, minimum, maximum, mean, median, standard deviation, histogram, and hourly trend.</summary>
    static object HeartRate(string since, int buckets)
    {
        var agg = HrmDb.Rows(
            """
            SELECT COUNT(*) AS n, MIN(bpm) AS lo, MAX(bpm) AS hi, AVG(bpm) AS avg,
                   MIN(ts) AS first, MAX(ts) AS last
            FROM hr_records WHERE ts >= $s AND bpm > 0
            """, ("$s", since)).FirstOrDefault();
        var n = ToLong(agg?.GetValueOrDefault("n"));
        if (n == 0)
            return new { count = 0, min = 0, max = 0, avg = 0.0, median = 0, sd = 0.0, first = "", last = "", histogram = Array.Empty<object>(), trend = Array.Empty<object>() };

        var lo = (int)ToLong(agg?.GetValueOrDefault("lo"));
        var hi = (int)ToLong(agg?.GetValueOrDefault("hi"));
        var avg = ToDouble(agg?.GetValueOrDefault("avg"));

        // SQLite has no built-in median, so select the middle row with OFFSET; for an even count, the upper middle value is sufficient.
        var median = (int)ToLong(HrmDb.Rows(
            "SELECT bpm FROM hr_records WHERE ts >= $s AND bpm > 0 ORDER BY bpm LIMIT 1 OFFSET $k",
            ("$s", since), ("$k", n / 2)).FirstOrDefault()?.GetValueOrDefault("bpm"));

        // Population standard deviation: sqrt(E[x²] - E[x]²)
        var sq = ToDouble(HrmDb.Rows(
            "SELECT AVG(CAST(bpm AS REAL) * bpm) AS v FROM hr_records WHERE ts >= $s AND bpm > 0",
            ("$s", since)).FirstOrDefault()?.GetValueOrDefault("v"));
        var sd = Math.Sqrt(Math.Max(0, sq - avg * avg));

        // Equal-width histogram buckets; calculate bucket indices in SQLite to avoid loading all samples into memory.
        var width = Math.Max(1.0, (hi - lo + 1) / (double)buckets);
        var hist = HrmDb.Rows(
            """
            SELECT CAST((bpm - $lo) / $w AS INTEGER) AS b, COUNT(*) AS n
            FROM hr_records WHERE ts >= $s AND bpm > 0 GROUP BY b ORDER BY b
            """, ("$s", since), ("$lo", lo), ("$w", width));
        var counts = new long[buckets];
        foreach (var r in hist)
        {
            var b = (int)Math.Clamp(ToLong(r.GetValueOrDefault("b")), 0, buckets - 1);
            counts[b] += ToLong(r.GetValueOrDefault("n"));
        }
        var histogram = counts.Select((c, i) => (object)new
        {
            from = (int)Math.Round(lo + i * width),
            to = (int)Math.Round(lo + (i + 1) * width) - 1,
            count = c,
        }).ToArray();

        // Trend: group by the first 13 characters of ts, yyyy-MM-ddTHH, producing one point per hour.
        var trend = HrmDb.Rows(
            """
            SELECT substr(ts, 1, 13) AS h, COUNT(*) AS n, AVG(bpm) AS avg, MIN(bpm) AS lo, MAX(bpm) AS hi
            FROM hr_records WHERE ts >= $s AND bpm > 0 GROUP BY h ORDER BY h
            """, ("$s", since))
            .Select(r => (object)new
            {
                hour = r.GetValueOrDefault("h") as string ?? "",
                count = ToLong(r.GetValueOrDefault("n")),
                avg = Math.Round(ToDouble(r.GetValueOrDefault("avg")), 1),
                min = (int)ToLong(r.GetValueOrDefault("lo")),
                max = (int)ToLong(r.GetValueOrDefault("hi")),
            }).ToArray();

        return new
        {
            count = n,
            min = lo,
            max = hi,
            avg = Math.Round(avg, 1),
            median,
            sd = Math.Round(sd, 2),
            first = agg?.GetValueOrDefault("first") as string ?? "",
            last = agg?.GetValueOrDefault("last") as string ?? "",
            histogram,
            trend,
        };
    }

    /// <summary>
    /// Health/sleep: occurrence count and cumulative duration for each state.
    /// Duration is the difference between adjacent state records because health_records writes only when the state changes,
    /// so each record lasts until the next one, with the final record extending to the current time.
    /// </summary>
    static object Health(string since)
    {
        var rows = HrmDb.Rows(
            "SELECT ts, status FROM health_records WHERE ts >= $s ORDER BY ts",
            ("$s", since));
        if (rows.Count == 0) return new { count = 0, items = Array.Empty<object>() };

        var seconds = new Dictionary<string, double>();
        var times = new Dictionary<string, long>();
        for (var i = 0; i < rows.Count; i++)
        {
            var status = rows[i].GetValueOrDefault("status") as string ?? "";
            if (status.Length == 0) continue;
            times[status] = times.GetValueOrDefault(status) + 1;
            if (!DateTime.TryParse(rows[i].GetValueOrDefault("ts") as string, out var t0)) continue;
            var t1 = i + 1 < rows.Count && DateTime.TryParse(rows[i + 1].GetValueOrDefault("ts") as string, out var nx)
                ? nx : DateTime.Now;
            var dur = (t1 - t0).TotalSeconds;
            if (dur is > 0 and < 86400) seconds[status] = seconds.GetValueOrDefault(status) + dur;
        }
        var total = Math.Max(1, seconds.Values.Sum());
        var items = seconds
            .OrderByDescending(kv => kv.Value)
            .Select(kv => (object)new
            {
                status = kv.Key,
                times = times.GetValueOrDefault(kv.Key),
                seconds = (long)kv.Value,
                percent = Math.Round(kv.Value / total * 100, 1),
            }).ToArray();
        return new { count = rows.Count, items };
    }

    /// <summary>Device distribution: sample count and heart-rate range for each device.</summary>
    static object Devices(string since) => HrmDb.Rows(
        """
        SELECT mac, MAX(name) AS name, COUNT(*) AS n, AVG(bpm) AS avg, MIN(bpm) AS lo, MAX(bpm) AS hi, MAX(ts) AS last
        FROM hr_records WHERE ts >= $s AND bpm > 0 GROUP BY mac ORDER BY n DESC
        """, ("$s", since))
        .Select(r => (object)new
        {
            mac = r.GetValueOrDefault("mac") as string ?? "",
            name = r.GetValueOrDefault("name") as string ?? "",
            count = ToLong(r.GetValueOrDefault("n")),
            avg = Math.Round(ToDouble(r.GetValueOrDefault("avg")), 1),
            min = (int)ToLong(r.GetValueOrDefault("lo")),
            max = (int)ToLong(r.GetValueOrDefault("hi")),
            last = r.GetValueOrDefault("last") as string ?? "",
        }).ToArray();

    /// <summary>OSC records: total count and the 20 most frequent addresses.</summary>
    static object Osc(string since)
    {
        var top = HrmDb.Rows(
            """
            SELECT addr, COUNT(*) AS n, MAX(ts) AS last FROM osc_records
            WHERE ts >= $s GROUP BY addr ORDER BY n DESC LIMIT 20
            """, ("$s", since))
            .Select(r => (object)new
            {
                addr = r.GetValueOrDefault("addr") as string ?? "",
                count = ToLong(r.GetValueOrDefault("n")),
                last = r.GetValueOrDefault("last") as string ?? "",
            }).ToArray();
        var n = ToLong(HrmDb.Rows("SELECT COUNT(*) AS n FROM osc_records WHERE ts >= $s", ("$s", since))
            .FirstOrDefault()?.GetValueOrDefault("n"));
        return new { count = n, top };
    }

    static long ToLong(object? v) => v switch
    {
        long l => l,
        int i => i,
        double d => (long)d,
        _ => 0,
    };

    static double ToDouble(object? v) => v switch
    {
        double d => d,
        long l => l,
        int i => i,
        _ => 0,
    };
}
