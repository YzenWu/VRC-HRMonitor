using System.Collections.Concurrent;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.UI;

/// <summary>Manages floating windows requested by the Web UI, other frontends, or the tray. Window operations must run on the UI thread.</summary>
public static class FloatWindowHost
{
    /// Fixed identification of <summary> main window (polyheart ratio). </summary>
    public const string MainId = "__main__";

    // Count will be read by a conduit/Web thread and will be used and distributed as a dictionary
    private static readonly ConcurrentDictionary<string, FloatingWindow> Windows = new();

    // Refresh current timetamps (UI thread reading and writing only)
    private static long _lastRefresh;

    public static int Count => Windows.Count;

    /// <summary>Ids of all currently open windows (main window + one per device); pushed in the status snapshot so the frontend can render per-device toggles (E1).</summary>
    public static string[] OpenIds => Windows.Keys.ToArray();

    /// <summary>E4 Round 36: window additions and subtractions/post-closing notification (the AppHub conversion to "float " WS event, front-end switch instant roundback, no longer waiting for 5s inquiry). </summary>
    public static event Action? Changed;
    static void RaiseChanged() { try { Changed?.Invoke(); } catch { } }

    /// <summary>Persists a floating window's logical size and screen position.</summary>
    static void PersistGeometry(FloatingWindow w, bool closed)
    {
        var win = App.Config.HeartRate.Window;
        if (w.Id == MainId)
        {
            win.Geometry = w.Geometry;
            win.Visible = !closed;
        }
        else if (!closed)
        {
            win.Geometries[w.Id] = w.Geometry;
        }
        App.Config.Save();
    }

    /// <summary> specifies whether the window (main window MainId) is currently open. </summary>
    public static bool IsOpen(string id) => Windows.ContainsKey(id);

    /// <summary> closes the specified window; main window passes MainId. </summary>
    public static void Close(string id)
    {
        if (Windows.TryGetValue(id, out var w)) w.Close();
    }

    /// Data source for the <summary> window: priority is given to the window configuration, the main window is returned to Window.Source and the other windows are returned to the window identifier (MAC) itself. </summary>
    public static string SourceOf(string id)
    {
        var w = App.Config.HeartRate.Window;
        if (w.Sources.TryGetValue(id, out var s) && !string.IsNullOrWhiteSpace(s)) return s;
        if (id == MainId) return string.IsNullOrWhiteSpace(w.Source) ? "平均" : w.Source;
        return id;
    }

    static string TitleOf(string id)
    {
        var src = SourceOf(id);
        if (src == "平均") return id == MainId ? "心率" : "心率-平均";
        var name = App.Ble.Get(src)?.Name;
        return string.IsNullOrEmpty(name) ? $"心率-{src}" : $"心率-{name}";
    }

    /// <summary> takes the average of all connected devices according to the data source heart rate: 'average', otherwise you specify MAC. </summary>
    public static int BpmOfSource(string source) =>
        string.IsNullOrWhiteSpace(source) || source == "平均"
            ? App.Ble.AverageBpm()
            : (App.Ble.Get(source)?.Bpm ?? 0);

    static int BpmOf(string id) => BpmOfSource(SourceOf(id));

    public static void Open(string id)
    {
        if (Windows.TryGetValue(id, out var existing))
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var geometry = id == MainId
            ? App.Config.HeartRate.Window.Geometry
            : App.Config.HeartRate.Window.Geometries.GetValueOrDefault(id, App.Config.HeartRate.Window.Geometry);
        var w = new FloatingWindow(id, TitleOf(id), () => BpmOf(id), _ => { });
        w.ApplyGeometry(geometry);
        w.FormClosed += (_, _) =>
        {
            Windows.TryRemove(id, out _);
            PersistGeometry(w, true);
            RaiseChanged(); // E4: Local closing of windows (Alt+F4/right) also immediately notify frontend
        };
        // Persist position and size after the native resize or custom drag loop completes.
        w.ResizeEnd += (_, _) => PersistGeometry(w, false);
        w.GeometryChanged += () => PersistGeometry(w, false);
        w.SetStyle(
            App.Config.HeartRate.Window.Format,
            App.Config.HeartRate.Window.UnlockedColor,
            App.Config.HeartRate.Window.LockedColor,
            App.Config.HeartRate.Window.ImagePath);
        // Multiple windows share the same geometry, staggered one by one, avoiding a complete overlap that only seems to open one.
        var offset = Windows.Count * 28;
        if (offset > 0) w.Location = new Point(w.Left + offset, w.Top + offset);
        Windows[id] = w;
        w.Show();
        // The lock state must be applied after the handle has been created: click through dependency IsHandleCreated
        w.ToggleLock(App.Config.HeartRate.Window.Locked);
        w.Activate();
        if (id == MainId) PersistGeometry(w, false);
        else if (!App.Config.HeartRate.Window.Geometries.ContainsKey(id)) PersistGeometry(w, false);
        App.Log.Info(LogText.L("log.fhost.open", id));
    }

    public static void CloseAll()
    {
        foreach (var w in Windows.Values.ToList()) w.Close();
        Windows.Clear();
        RaiseChanged();
    }

    public static void ToggleLockAll(bool locked)
    {
        foreach (var w in Windows.Values) w.ToggleLock(locked);
    }

    public static void ApplyStyle()
    {
        foreach (var kv in Windows)
        {
            kv.Value.SetStyle(
                App.Config.HeartRate.Window.Format,
                App.Config.HeartRate.Window.UnlockedColor,
                App.Config.HeartRate.Window.LockedColor,
                App.Config.HeartRate.Window.ImagePath);
            // E3: Geometry effective immediately when the main window has been opened (the original note self-identifys "the next time the window is open before new configuration")
            if (kv.Key == MainId) kv.Value.ApplyGeometry(App.Config.HeartRate.Window.Geometry);
        }
    }

    public static void RefreshAll()
    {
        // Refresh interval between configurations: avoid redrawing every multi-equipment HF notification
        var interval = Math.Max(0, App.Config.HeartRate.Window.RefreshMs);
        if (interval > 0)
        {
            var now = Environment.TickCount64;
            if (now - _lastRefresh < interval) return;
            _lastRefresh = now;
        }
        foreach (var w in Windows.Values) w.UpdateHeartRate();
    }

    /// <summary> recalculates the title after a change in the data source and refreshs it immediately (without the throttle). </summary>
    public static void ApplySources()
    {
        _lastRefresh = 0;
        foreach (var kv in Windows)
        {
            kv.Value.Text = TitleOf(kv.Key);
            kv.Value.UpdateHeartRate();
        }
    }
}
