using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HeartRateMonitor.Toolkit;

/// <summary>
/// Phase 10 VRChat Toolkit backend (admin-only REST family under /api/toolkit/).
///
/// Storage rules (per Windows/Skills/VRCStorage.skill + VRChatConfigText.skill + VRCPhoto.skill):
/// - every path is resolved from %LOCALAPPDATA%\..\LocalLow\VRChat\VRChat\config.json first
///   (cache_directory / picture_output_folder override the defaults); defaults are fallbacks only;
/// - config.json writes are strict JSON with a whitelist of documented fields
///   (cache_size &gt;= 30 GB, cache_expiry_delay &gt;= 30 days, paths use '/' or '\\');
/// - PNG photo metadata is decoded from iTXt chunks as UTF-8 (XMP "XML:com.adobe.xmp" + VRCX "Description" JSON);
/// - output logs and crash content are sensitive: the log endpoints stay loopback-only and
///   nothing here sends log/crash content to any remote service;
/// - cache clearing only executes while VRChat is not running (dry-run is always available).
/// </summary>
public static class VrcToolkitService
{
    private const string ConfirmPhrase = "CLEAR VRCHAT CACHE";
    private const long MaxLogBytes = 4L * 1024 * 1024;

    /// <summary>Writable config.json fields — documented VRChat parameters only, never invented ones.</summary>
    private static readonly HashSet<string> ConfigFields = new(StringComparer.Ordinal)
    {
        "cache_directory", "cache_size", "cache_expiry_delay", "disableRichPresence",
        "dynamic_bone_max_affected_transform_count", "dynamic_bone_max_collider_check_count",
        "screenshot_res_width", "screenshot_res_height", "camera_res_width", "camera_res_height",
        "picture_output_folder", "picture_output_split_by_date", "fpv_steadycam_fov",
    };

    // ---------------------------------------------------------------- paths

    /// <summary>Resolved VRChat storage layout; every value comes from config.json when the file provides one.</summary>
    public sealed record ResolvedPaths(
        string Base, string Config, string Logs, string Cache, string Photos,
        bool SplitByDate, bool CacheOverridden, bool PhotoOverridden);

    /// <summary>
    /// Resolves base/config/logs/cache/photos from config.json (cache_directory / picture_output_folder
    /// override the defaults). Logs and config always stay in the LocalLow VRChat root even when the
    /// cache is relocated. picture_output_split_by_date defaults to true (VRChat behaviour).
    /// </summary>
    public static ResolvedPaths ResolvePaths()
    {
        // %LOCALAPPDATA%\..\LocalLow\VRChat\VRChat — never assume C:\Users\<user> directly.
        var root = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "..", "LocalLow", "VRChat", "VRChat"));
        var cfgPath = Path.Combine(root, "config.json");
        var cfg = ReadConfig(cfgPath);
        var cacheOverride = cfg["cache_directory"]?.ToString();
        var photoOverride = cfg["picture_output_folder"]?.ToString();
        var cache = ResolveConfigured(cacheOverride, Path.Combine(root, "Cache-WindowsPlayer"));
        var photos = ResolveConfigured(photoOverride, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VRChat"));
        var split = cfg["picture_output_split_by_date"] is JsonValue sv && sv.TryGetValue<bool>(out var b) ? b : true;
        return new ResolvedPaths(root, cfgPath, root, cache, photos, split,
            !string.IsNullOrWhiteSpace(cacheOverride), !string.IsNullOrWhiteSpace(photoOverride));
    }

    public static object Paths()
    {
        var p = ResolvePaths();
        return new
        {
            ok = true,
            @base = p.Base,
            config = p.Config,
            logs = p.Logs,
            cache = p.Cache,
            photos = p.Photos,
            splitByDate = p.SplitByDate,
            cacheOverridden = p.CacheOverridden,
            photoOverridden = p.PhotoOverridden,
            configExists = File.Exists(p.Config),
            photoExists = Directory.Exists(p.Photos),
            cacheExists = Directory.Exists(p.Cache),
            vrchatRunning = VrchatRunning(),
        };
    }

    // ---------------------------------------------------------------- config.json

    public static object Config()
    {
        var p = ResolvePaths();
        var root = ReadConfig(p.Config);
        var values = new JsonObject();
        foreach (var k in ConfigFields)
            if (root[k] != null) values[k] = root[k]!.DeepClone();
        return new { ok = true, path = p.Config, values, vrchatRunning = VrchatRunning() };
    }

    /// <summary>Whitelisted, validated, atomic config.json write (strict JSON, UTF-8 without BOM).</summary>
    public static object SaveConfig(JsonObject? patch)
    {
        if (App.SafeMode) return new { ok = false, error = "safe mode blocks toolkit writes" };
        if (VrchatRunning()) return new { ok = false, error = "VRChat is running" };
        if (patch == null || patch.Any(x => !ConfigFields.Contains(x.Key)))
            return new { ok = false, error = "unsupported config field" };
        var p = ResolvePaths();
        var root = ReadConfig(p.Config);
        foreach (var (k, v) in patch)
        {
            Validate(k, v);
            root[k] = v?.DeepClone();
        }
        Directory.CreateDirectory(p.Base);
        var tmp = p.Config + ".hrm.tmp";
        File.WriteAllText(tmp, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
            + Environment.NewLine, new UTF8Encoding(false));
        if (File.Exists(p.Config)) File.Replace(tmp, p.Config, null); else File.Move(tmp, p.Config);
        App.Log.Info($"toolkit: VRChat config.json updated ({string.Join(",", patch.Select(x => x.Key))})");
        return new { ok = true };
    }

    // ---------------------------------------------------------------- output logs (loopback-only at the route layer)

    public static object ListLogs()
    {
        var p = ResolvePaths();
        var files = Directory.Exists(p.Logs)
            ? Directory.EnumerateFiles(p.Logs, "output_log_*.txt", SearchOption.TopDirectoryOnly)
                .Select(x => new FileInfo(x))
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .Select(x => (object)new { name = x.Name, size = x.Length, modifiedAt = x.LastWriteTimeUtc.ToString("O") })
                .ToArray()
            : Array.Empty<object>();
        return new { ok = true, path = p.Logs, files };
    }

    /// <summary>
    /// Reads one output log from the resolved log directory only (GetFullPath prefix check).
    /// Files larger than 4 MB are read from the tail (last 4 MB window); an optional substring
    /// filter is applied before taking the final <paramref name="lines"/> lines.
    /// </summary>
    public static object ReadLog(string name, string search, int lines)
    {
        var p = ResolvePaths();
        string file;
        try { file = SafeChild(p.Logs, name); }
        catch { return new { ok = false, error = "invalid log name", lines = Array.Empty<string>() }; }
        var fileName = Path.GetFileName(file);
        if (!fileName.StartsWith("output_log_", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(file))
            return new { ok = false, error = "log not found", lines = Array.Empty<string>() };

        lines = Math.Clamp(lines < 1 ? 500 : lines, 1, 5000);
        var filter = search ?? "";
        var size = new FileInfo(file).Length;
        var truncated = size > MaxLogBytes;
        List<string> tail;
        if (!truncated)
        {
            tail = new List<string>();
            foreach (var line in File.ReadLines(file)) tail.Add(line);
        }
        else
        {
            using var s = File.OpenRead(file);
            s.Seek(-MaxLogBytes, SeekOrigin.End);
            using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            if (s.Position > 0) r.ReadLine(); // drop the partial line cut by the tail window
            tail = new List<string>();
            string? line;
            while ((line = r.ReadLine()) != null) tail.Add(line);
        }
        var picked = tail
            .Where(x => filter.Length == 0 || x.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .TakeLast(lines)
            .ToArray();
        return new { ok = true, lines = picked, size, truncated, filtered = filter.Length > 0 };
    }

    // ---------------------------------------------------------------- cache

    private static readonly object CacheScanGate = new();
    private static readonly TimeSpan CacheScanTtl = TimeSpan.FromSeconds(30);
    private static Task<CacheMeasure?>? _cacheScanTask;
    private static CancellationTokenSource? _cacheScanCts;
    private static CacheMeasure? _cacheSnapshot;
    private static string _cacheSnapshotPath = "";
    private static string _cacheScanPath = "";
    private static DateTime _cacheMeasuredAtUtc = DateTime.MinValue;

    public static object CacheStats()
    {
        var p = ResolvePaths();
        CacheMeasure m;
        bool running;
        DateTime measuredAt;
        lock (CacheScanGate)
        {
            var fresh = string.Equals(_cacheSnapshotPath, p.Cache, StringComparison.OrdinalIgnoreCase)
                && DateTime.UtcNow - _cacheMeasuredAtUtc < CacheScanTtl;
            if (_cacheScanTask is not { IsCompleted: false } && !fresh)
                StartCacheScanLocked(p.Cache);
            running = _cacheScanTask is { IsCompleted: false };
            m = string.Equals(_cacheSnapshotPath, p.Cache, StringComparison.OrdinalIgnoreCase)
                ? _cacheSnapshot ?? EmptyCacheMeasure()
                : EmptyCacheMeasure();
            measuredAt = string.Equals(_cacheSnapshotPath, p.Cache, StringComparison.OrdinalIgnoreCase)
                ? _cacheMeasuredAtUtc
                : DateTime.MinValue;
        }
        return CacheStatsResult(p.Cache, m, running, measuredAt);
    }

    public static void CancelCacheScan()
    {
        lock (CacheScanGate)
        {
            try { _cacheScanCts?.Cancel(); } catch { }
        }
    }

    /// <summary>
    /// Cache clear. dryRun=true only reports what would be deleted (bytes/files).
    /// A real delete needs confirm=true + the confirmation phrase, and runs only while VRChat
    /// is not running. The cache root itself is kept; only its contents are removed.
    /// </summary>
    public static async Task<object> ClearCacheAsync(bool dryRun, bool confirm, string phrase)
    {
        if (App.SafeMode) return new { ok = false, error = "safe mode blocks toolkit writes" };
        var p = ResolvePaths();
        var running = VrchatRunning();
        if (dryRun)
        {
            var m = await Task.Run(() => MeasureCache(p.Cache, CancellationToken.None));
            return new { ok = true, dryRun = true, bytes = m.Bytes, files = m.Files, vrchatRunning = running };
        }
        if (!confirm || !string.Equals(phrase, ConfirmPhrase, StringComparison.Ordinal))
            return new { ok = false, error = "confirmation required" };
        if (running) return new { ok = false, error = "VRChat is running" };
        var m2 = await Task.Run(() => MeasureCache(p.Cache, CancellationToken.None));
        if (Directory.Exists(p.Cache))
        {
            var failed = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(p.Cache))
            {
                try
                {
                    if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry);
                }
                catch { failed++; }
            }
            lock (CacheScanGate)
            {
                _cacheScanCts?.Cancel();
                _cacheSnapshot = null;
                _cacheSnapshotPath = "";
                _cacheMeasuredAtUtc = DateTime.MinValue;
            }
            if (failed > 0) App.Log.Warn($"toolkit: cache clear finished with {failed} locked/failed entries");
            App.Log.Info($"toolkit: VRChat cache cleared ({m2.Bytes} bytes, {m2.Files} files) at {p.Cache}");
            return new { ok = true, cleared = true, bytes = m2.Bytes, files = m2.Files, failed };
        }
        return new { ok = true, cleared = false, bytes = 0L, files = 0L, failed = 0 };
    }

    // ---------------------------------------------------------------- photo index

    private static readonly object ScanGate = new();
    private static CancellationTokenSource? _scanCts;
    private static Task? _scanTask;
    private static int _scanDone, _scanTotal, _scanIndexed;
    private static string _scanError = "";
    private static long _scanLastEvtTick;

    /// <summary>Throttled scan progress (done, total, indexed); WebServer relays it over WebSocket as toolkit_index.</summary>
    public static event Action<int, int, int>? ScanProgressChanged;

    /// <summary>User-triggered parallel PNG scan (iTXt XMP + VRCX Description) into hrm.db toolkit_photos.</summary>
    public static object StartIndex(int concurrency)
    {
        if (App.SafeMode) return new { ok = false, error = "safe mode blocks toolkit scans" };
        concurrency = Math.Clamp(concurrency < 1 ? 2 : concurrency, 1, Math.Min(16, Math.Max(1, Environment.ProcessorCount)));
        lock (ScanGate)
        {
            if (_scanTask is { IsCompleted: false }) return ScanProgress();
            _scanCts = new CancellationTokenSource();
            _scanDone = _scanTotal = _scanIndexed = 0;
            _scanError = "";
            _scanLastEvtTick = 0;
            _scanTask = Task.Run(() => IndexPhotos(concurrency, _scanCts.Token));
        }
        return ScanProgress();
    }

    public static object CancelIndex()
    {
        if (App.SafeMode) return new { ok = false, error = "safe mode blocks toolkit scans" };
        lock (ScanGate) _scanCts?.Cancel();
        return ScanProgress();
    }

    public static object ScanProgress() => new
    {
        ok = true,
        running = _scanTask is { IsCompleted: false },
        done = Volatile.Read(ref _scanDone),
        total = _scanTotal,
        indexed = Volatile.Read(ref _scanIndexed),
        error = _scanError,
    };

    /// <summary>
    /// Photo search. Free keywords match every metadata field; structured tokens narrow the match:
    /// author=&lt;name&gt;, world=&lt;name&gt;, include=&lt;player/world text&gt;, date=&lt;YYYY-MM-DD or prefix&gt;.
    /// All values are bound as LIKE parameters; only metadata is returned (no image bytes).
    /// </summary>
    public static object Search(string query, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize < 1 ? 30 : pageSize, 1, 100);
        var where = new List<string>();
        var args = new List<(string Name, object? Value)>();
        foreach (var token in Tokens(query))
        {
            var eq = token.IndexOf('=');
            var key = eq > 0 ? token[..eq].ToLowerInvariant() : "";
            var val = eq > 0 ? token[(eq + 1)..] : token;
            if (val.Length == 0) continue;
            var n = "$q" + args.Count;
            args.Add((n, "%" + val + "%"));
            where.Add(key switch
            {
                "author" => $"author_name LIKE {n}",
                "world" => $"world_name LIKE {n}",
                "date" => $"captured_at LIKE {n}",
                "include" => $"(world_name LIKE {n} OR instance_id LIKE {n} OR players_json LIKE {n})",
                _ => $"(path LIKE {n} OR author_name LIKE {n} OR world_name LIKE {n} OR instance_id LIKE {n} OR players_json LIKE {n})",
            });
        }
        var clause = where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where);
        var countArgs = args.ToArray();
        var total = ToLong(HrmDb.Rows("SELECT COUNT(*) AS n FROM toolkit_photos" + clause, countArgs)
            .FirstOrDefault()?.GetValueOrDefault("n"));
        var rows = HrmDb.Rows(
            "SELECT path,file_mtime,author_id,author_name,world_id,world_name,instance_id,captured_at,players_json"
            + " FROM toolkit_photos" + clause
            + " ORDER BY captured_at DESC, file_mtime DESC LIMIT $take OFFSET $skip",
            SqlArgs(args, pageSize, (page - 1) * pageSize)
        ).Select(r => (object)new
        {
            path = Str(r, "path"),
            fileMtime = Str(r, "file_mtime"),
            authorId = Str(r, "author_id"),
            authorName = Str(r, "author_name"),
            worldId = Str(r, "world_id"),
            worldName = Str(r, "world_name"),
            instanceId = Str(r, "instance_id"),
            capturedAt = Str(r, "captured_at"),
            playersJson = Str(r, "players_json"),
            // legacy aliases kept so the existing Toolkit.vue keeps rendering until the new fields are adopted
            size = 0L,
            modifiedAt = Str(r, "file_mtime"),
            author = Str(r, "author_name"),
            metadata = Str(r, "players_json"),
        }).ToArray();
        return new { ok = true, page, pageSize, total, rows };
    }

    // ---------------------------------------------------------------- game analysis

    /// <summary>
    /// Daily aggregation: VRChat playtime estimated from vrchat_records start/stop events
    /// (written by the AppHub status sampler) plus avg/max heart rate from hr_records.
    /// Both sources are read through HrmDb.Rows (internal SQL, parameterized).
    /// </summary>
    public static object GameStats(int days)
    {
        days = Math.Clamp(days < 1 ? 30 : days, 1, 365);
        var since = DateTime.Now.AddDays(-days).ToString("O");

        // Session segments: pair 'start' with the next 'stop'; a dangling 'start' extends to now.
        // Segments spanning midnight are split across local days so per-day totals stay accurate.
        var perDay = new Dictionary<string, (long Seconds, int Sessions)>();
        DateTime? open = null;
        void Close(DateTime end)
        {
            if (open is not { } start) return;
            if (end < start) end = start;
            var cursor = start;
            while (cursor < end)
            {
                var dayEnd = cursor.Date.AddDays(1);
                var segEnd = end < dayEnd ? end : dayEnd;
                var key = cursor.ToString("yyyy-MM-dd");
                var cur = perDay.GetValueOrDefault(key);
                perDay[key] = (cur.Seconds + (long)(segEnd - cursor).TotalSeconds, cur.Sessions);
                cursor = segEnd;
            }
            open = null;
        }
        var evRows = HrmDb.Rows("SELECT ts, event FROM vrchat_records WHERE ts >= $s ORDER BY ts", ("$s", since));
        foreach (var r in evRows)
        {
            if (!DateTime.TryParse(Str(r, "ts"), out var t)) continue;
            var ev = Str(r, "event");
            if (ev == "start")
            {
                Close(t);
                open = t;
                var key = t.ToString("yyyy-MM-dd");
                var cur = perDay.GetValueOrDefault(key);
                perDay[key] = (cur.Seconds, cur.Sessions + 1);
            }
            else if (ev == "stop") Close(t);
        }
        if (open != null) Close(DateTime.Now);

        var hrByDay = HrmDb.Rows(
            """
            SELECT substr(ts, 1, 10) AS d, COUNT(*) AS n, AVG(bpm) AS avg, MAX(bpm) AS hi
            FROM hr_records WHERE ts >= $s AND bpm > 0 GROUP BY d ORDER BY d
            """, ("$s", since))
            .ToDictionary(r => Str(r, "d"), r => (
                N: ToLong(r.GetValueOrDefault("n")),
                Avg: ToDouble(r.GetValueOrDefault("avg")),
                Hi: ToLong(r.GetValueOrDefault("hi"))));

        var dates = perDay.Keys.Union(hrByDay.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var rows = dates.Select(k =>
        {
            var v = perDay.GetValueOrDefault(k);
            var h = hrByDay.GetValueOrDefault(k);
            return (object)new
            {
                date = k,
                seconds = v.Seconds,
                sessions = v.Sessions,
                hrCount = h.N,
                hrAvg = h.N > 0 ? Math.Round(h.Avg, 1) : 0.0,
                hrMax = (int)h.Hi,
            };
        }).ToArray();
        string? note = null;
        if (perDay.Count == 0 && hrByDay.Count == 0) note = "no recorded data in range (recording disabled or no sessions yet)";
        else if (perDay.Count == 0) note = "no VRChat session records in range; playtime shows 0";
        else if (hrByDay.Count == 0) note = "no heart-rate samples in range";
        return new
        {
            ok = true,
            days,
            since,
            rows,
            totalSeconds = perDay.Values.Sum(v => v.Seconds),
            totalSessions = perDay.Values.Sum(v => v.Sessions),
            note,
        };
    }

    // ---------------------------------------------------------------- VRChat process snapshot

    private static readonly object PsLock = new();
    private static DateTime _psAt = DateTime.MinValue;
    private static double _psPrevCpuMs;

    /// <summary>
    /// VRChat process snapshot (CPU % from TotalProcessorTime deltas, working set, threads, handles).
    /// Same probing model as AppHub's status sampler, re-read here on demand.
    /// </summary>
    public static object ProcessSnapshot()
    {
        Process? p = null;
        try
        {
            foreach (var proc in Process.GetProcessesByName("VRChat"))
            {
                if (proc.Id != 0) { p = proc; break; }
                proc.Dispose();
            }
        }
        catch { }
        if (p == null)
        {
            lock (PsLock) { _psAt = DateTime.MinValue; _psPrevCpuMs = 0; }
            return new { ok = true, running = false };
        }
        using (p)
        {
            var now = DateTime.UtcNow;
            double cpu = 0;
            long ws = 0;
            int threads = 0, handles = 0;
            var responding = true;
            string? start = null;
            try
            {
                var ticks = p.TotalProcessorTime.TotalMilliseconds;
                lock (PsLock)
                {
                    if (_psAt != DateTime.MinValue && _psPrevCpuMs > 0)
                    {
                        var dt = (now - _psAt).TotalMilliseconds;
                        var d = ticks - _psPrevCpuMs;
                        if (dt > 0 && d >= 0)
                            cpu = Math.Clamp(d / dt * 100.0 / Math.Max(1, Environment.ProcessorCount), 0, 100);
                    }
                    _psAt = now;
                    _psPrevCpuMs = ticks;
                }
                p.Refresh();
                ws = p.WorkingSet64;
                responding = p.Responding;
                try { threads = p.Threads.Count; } catch { }
                try { handles = p.HandleCount; } catch { }
                try { start = $"{p.StartTime:O}"; } catch { }
            }
            catch { /* process exited or access denied — return what was collected */ }
            return new
            {
                ok = true,
                running = true,
                pid = p.Id,
                cpuPct = Math.Round(cpu, 1),
                memoryMb = Math.Round(ws / 1024d / 1024d, 1),
                threads,
                handles,
                responding,
                startTime = start ?? "",
            };
        }
    }

    // ---------------------------------------------------------------- internals

    private static async Task IndexPhotos(int concurrency, CancellationToken ct)
    {
        var bag = new ConcurrentBag<PhotoRow>();
        try
        {
            var root = ResolvePaths().Photos;
            var files = Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).ToArray()
                : Array.Empty<string>();
            _scanTotal = files.Length;
            RaiseProgress();
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct },
                (file, _) =>
                {
                    try { bag.Add(ParsePng(file)); Interlocked.Increment(ref _scanIndexed); }
                    catch { /* unreadable/non-PNG file: skip, not an error */ }
                    Interlocked.Increment(ref _scanDone);
                    var tick = Environment.TickCount64;
                    if (tick - Volatile.Read(ref _scanLastEvtTick) >= 400)
                    {
                        Volatile.Write(ref _scanLastEvtTick, tick);
                        RaiseProgress();
                    }
                    return ValueTask.CompletedTask;
                });
        }
        catch (OperationCanceledException) { _scanError = "cancelled"; }
        catch (Exception e) { _scanError = e.Message; }
        finally
        {
            // Persist everything parsed so far — the index is derived data, partial saves are safe.
            SavePhotos(bag);
            RaiseProgress();
        }
    }

    private static void RaiseProgress()
    {
        try { ScanProgressChanged?.Invoke(Volatile.Read(ref _scanDone), _scanTotal, Volatile.Read(ref _scanIndexed)); }
        catch { }
    }

    private sealed record PhotoRow(
        string Path, string FileMtime, string AuthorId, string AuthorName,
        string WorldId, string WorldName, string InstanceId, string CapturedAt, string PlayersJson);

    /// <summary>Decodes PNG iTXt metadata (UTF-8; zlib-compressed blocks supported): XMP first, VRCX Description as fallback/supplement.</summary>
    private static PhotoRow ParsePng(string path)
    {
        var texts = new List<(string Key, string Text)>();
        using (var s = File.OpenRead(path))
        {
            var sig = new byte[8];
            s.ReadExactly(sig);
            if (!sig.AsSpan().SequenceEqual("\x89PNG\r\n\x1a\n"u8)) throw new InvalidDataException("not a PNG");
            var head = new byte[8];
            while (s.Position < s.Length)
            {
                s.ReadExactly(head.AsSpan(0, 4));
                var len = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(0, 4));
                if (len < 0 || len > 128L * 1024 * 1024) throw new InvalidDataException("bad chunk length");
                s.ReadExactly(head.AsSpan(4, 4));
                var type = Encoding.ASCII.GetString(head, 4, 4);
                var data = new byte[len];
                s.ReadExactly(data);
                s.ReadExactly(head.AsSpan(0, 4)); // CRC
                if (type == "iTXt") ParseITxt(data, texts);
                if (type == "IEND") break;
            }
        }

        var authorName = "";
        var authorId = "";
        var worldName = "";
        var worldId = "";
        var instanceId = "";
        var captured = "";
        var playersJson = "[]";
        foreach (var (k, v) in texts)
        {
            if (k == "XML:com.adobe.xmp")
            {
                try
                {
                    var x = XDocument.Parse(v);
                    if (Attr(x, "Author") is { Length: > 0 } a) authorName = a;
                    if (Attr(x, "AuthorID") is { Length: > 0 } ai) authorId = ai;
                    if (Attr(x, "WorldDisplayName") is { Length: > 0 } w) worldName = w;
                    if (Attr(x, "WorldID") is { Length: > 0 } wi) worldId = wi;
                    if (Attr(x, "CreateDate") is { Length: > 0 } c) captured = c;
                }
                catch { /* malformed XMP: keep what we have */ }
            }
            else if (k == "Description")
            {
                try
                {
                    var j = JsonNode.Parse(v);
                    if (j?["author"]?["displayName"]?.ToString() is { Length: > 0 } an) authorName = an;
                    if (j?["author"]?["id"]?.ToString() is { Length: > 0 } aid) authorId = aid;
                    if (j?["world"]?["name"]?.ToString() is { Length: > 0 } wn) worldName = wn;
                    if (j?["world"]?["id"]?.ToString() is { Length: > 0 } wid) worldId = wid;
                    if (j?["world"]?["instanceId"]?.ToString() is { Length: > 0 } ii) instanceId = ii;
                    if (j?["players"] is JsonArray players) playersJson = players.ToJsonString();
                }
                catch { /* malformed VRCX JSON: keep what we have */ }
            }
        }
        var fi = new FileInfo(path);
        // captured_at stored as UTC ISO-8601 so lexical order matches chronology; falls back to file mtime
        // when neither XMP CreateDate nor anything else provides a capture time.
        var capturedAt = DateTimeOffset.TryParse(captured, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
            ? dto.UtcDateTime.ToString("O")
            : fi.LastWriteTimeUtc.ToString("O");
        return new PhotoRow(path, fi.LastWriteTimeUtc.ToString("O"), authorId, authorName,
            worldId, worldName, instanceId, capturedAt, playersJson);
    }

    /// <summary>iTXt layout: keyword\0 compressionFlag compressionMethod languageTag\0 translatedKeyword\0 text (UTF-8).</summary>
    private static void ParseITxt(byte[] data, List<(string Key, string Text)> texts)
    {
        var z = Array.IndexOf(data, (byte)0);
        if (z <= 0 || z + 3 > data.Length) return;
        var key = Encoding.Latin1.GetString(data, 0, z);
        var compressed = data[z + 1] == 1;
        var langEnd = Array.IndexOf(data, (byte)0, z + 3);
        if (langEnd < 0) return;
        var transEnd = Array.IndexOf(data, (byte)0, langEnd + 1);
        if (transEnd < 0 || transEnd + 1 > data.Length) return;
        var raw = data[(transEnd + 1)..];
        if (raw.Length == 0) return;
        if (compressed)
        {
            using var zs = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            zs.CopyTo(ms);
            raw = ms.ToArray();
        }
        texts.Add((key, Encoding.UTF8.GetString(raw)));
    }

    private static string Attr(XDocument x, string name)
        => x.Descendants().Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value ?? "";

    private static void SavePhotos(ConcurrentBag<PhotoRow> rows)
    {
        const string sql = """
            INSERT INTO toolkit_photos(path,file_mtime,author_id,author_name,world_id,world_name,instance_id,captured_at,players_json)
            VALUES($p,$m,$ai,$an,$wi,$wn,$ii,$c,$pl)
            ON CONFLICT(path) DO UPDATE SET
                file_mtime=$m, author_id=$ai, author_name=$an, world_id=$wi, world_name=$wn,
                instance_id=$ii, captured_at=$c, players_json=$pl
            """;
        var logged = false;
        while (!rows.IsEmpty)
        {
            while (rows.TryTake(out var r))
            {
                try
                {
                    HrmDb.Exec(sql,
                        ("$p", r.Path), ("$m", r.FileMtime), ("$ai", r.AuthorId), ("$an", r.AuthorName),
                        ("$wi", r.WorldId), ("$wn", r.WorldName), ("$ii", r.InstanceId),
                        ("$c", r.CapturedAt), ("$pl", r.PlayersJson));
                }
                catch (Exception e)
                {
                    if (!logged) { App.Log.Debug($"toolkit photo upsert failed: {e.Message}"); logged = true; }
                }
            }
        }
    }

    private static CacheMeasure EmptyCacheMeasure()
        => new(0, 0, DateTime.MaxValue, DateTime.MinValue,
            new Dictionary<string, (long Bytes, long Files)>(StringComparer.OrdinalIgnoreCase));

    private static object CacheStatsResult(string path, CacheMeasure m, bool running, DateTime measuredAt) => new
    {
        ok = true,
        path,
        exists = Directory.Exists(path),
        running,
        measuredAt = measuredAt == DateTime.MinValue ? "" : measuredAt.ToString("O"),
        bytes = m.Bytes,
        files = m.Files,
        subdirs = m.Subdirs
            .OrderByDescending(kv => kv.Value.Bytes)
            .Select(kv => (object)new { name = kv.Key, bytes = kv.Value.Bytes, files = kv.Value.Files })
            .Take(50)
            .ToArray(),
        oldestAccess = m.Oldest == DateTime.MaxValue ? "" : m.Oldest.ToString("O"),
        newestAccess = m.Newest == DateTime.MinValue ? "" : m.Newest.ToString("O"),
    };

    private static void StartCacheScanLocked(string root)
    {
        _cacheScanCts?.Dispose();
        _cacheScanCts = new CancellationTokenSource();
        var cts = _cacheScanCts;
        _cacheScanPath = root;
        var task = Task.Run(() =>
        {
            try { return MeasureCache(root, cts.Token); }
            catch (OperationCanceledException) { return null; }
            catch (Exception e)
            {
                App.Log.Warn($"toolkit: cache scan failed at {root}: {e.Message}");
                return null;
            }
        });
        _cacheScanTask = task;
        _ = task.ContinueWith(t =>
        {
            lock (CacheScanGate)
            {
                if (!ReferenceEquals(_cacheScanTask, task)) return;
                if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                {
                    _cacheSnapshot = t.Result;
                    _cacheSnapshotPath = root;
                    _cacheMeasuredAtUtc = DateTime.UtcNow;
                }
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private sealed record CacheMeasure(
        long Bytes, long Files, DateTime Oldest, DateTime Newest,
        Dictionary<string, (long Bytes, long Files)> Subdirs);

    private static CacheMeasure MeasureCache(string root, CancellationToken cancellationToken)
    {
        long bytes = 0, files = 0;
        var oldest = DateTime.MaxValue;
        var newest = DateTime.MinValue;
        var subs = new Dictionary<string, (long Bytes, long Files)>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(root))
        {
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = 0, // size accounting must include hidden/system files
            };
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, "*", opts))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(f);
                        var length = fi.Length;
                        bytes += length;
                        files++;
                        var a = fi.LastAccessTimeUtc;
                        if (a < oldest) oldest = a;
                        if (a > newest) newest = a;
                        var rel = Path.GetRelativePath(root, f);
                        var i = rel.IndexOf(Path.DirectorySeparatorChar);
                        var bucket = i > 0 ? rel[..i] : ".";
                        var cur = subs.GetValueOrDefault(bucket);
                        subs[bucket] = (cur.Bytes + length, cur.Files + 1);
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
        return new CacheMeasure(bytes, files, oldest, newest, subs);
    }

    private static JsonObject ReadConfig(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    private static string ResolveConfigured(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return Path.GetFullPath(fallback);
        var expanded = Environment.ExpandEnvironmentVariables(value);
        return Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), expanded));
    }

    private static void Validate(string key, JsonNode? v)
    {
        if (v == null) return;
        if (key is "cache_directory" or "picture_output_folder")
        {
            _ = ResolveConfigured(v.GetValue<string>(), ""); // throws on invalid path characters
        }
        else if (key is "disableRichPresence" or "picture_output_split_by_date")
        {
            _ = v.GetValue<bool>();
        }
        else
        {
            var n = v.GetValue<int>();
            if (key is "cache_size" or "cache_expiry_delay" && n < 30) throw new ArgumentOutOfRangeException(key, $"{key} must be >= 30");
            if (key == "fpv_steadycam_fov" && (n < 30 || n > 110)) throw new ArgumentOutOfRangeException(key, "fpv_steadycam_fov must be within 30..110");
        }
    }

    /// <summary>Confines <paramref name="name"/> to a direct child of <paramref name="root"/> (GetFullPath prefix check).</summary>
    private static string SafeChild(string root, string name)
    {
        var p = Path.GetFullPath(Path.Combine(root, Path.GetFileName(name)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("path outside boundary");
        return p;
    }

    /// <summary>VRChat probe (same process-name check as the AppHub sampler); fails closed so a broken probe never allows a cache clear.</summary>
    private static bool VrchatRunning()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("VRChat"))
            {
                using (proc)
                    if (proc.Id != 0) return true;
            }
            return false;
        }
        catch { return true; }
    }

    private static IEnumerable<string> Tokens(string q)
        => Regex.Matches(q ?? "", "(?:[^\\s\"]+|\"[^\"]*\")+").Select(m => m.Value.Replace("\"", ""));

    /// <summary>Copy of the filter args plus LIMIT/OFFSET paging parameters.</summary>
    private static (string Name, object? Value)[] SqlArgs(List<(string Name, object? Value)> args, long take, long skip)
    {
        var all = new (string Name, object? Value)[args.Count + 2];
        for (var i = 0; i < args.Count; i++) all[i] = args[i];
        all[args.Count] = ("$take", take);
        all[args.Count + 1] = ("$skip", skip);
        return all;
    }

    private static string Str(Dictionary<string, object?> row, string key)
        => row.GetValueOrDefault(key) as string ?? "";

    private static long ToLong(object? v) => v switch { long l => l, int i => i, double d => (long)d, _ => 0 };
    private static double ToDouble(object? v) => v switch { double d => d, long l => l, int i => i, _ => 0 };
}
