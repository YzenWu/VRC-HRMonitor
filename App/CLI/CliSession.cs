using HeartRateMonitor.Core;

namespace HeartRateMonitor.Cli;

/// <summary>Owns CLI services, event subscriptions, local commands, and deterministic cleanup.</summary>
public sealed class CliSession : IDisposable
{
    public CliEventQueue Events { get; } = new();
    readonly Dictionary<string, string> _seen = new(StringComparer.OrdinalIgnoreCase);
    readonly object _seenLock = new();
    bool _wired;
    bool _disposed;

    public bool Start(bool interactive)
    {
        if (!CheckComponents(interactive)) return false;
        GitHubProjectService.Start();
        if (!App.SafeMode)
        {
            App.SysInfo.Start();
            App.Osc.Start();
        }
        EnsureHub();
        return true;
    }

    public void SubscribeEvents()
    {
        if (_wired) return;
        App.Ble.ScanStarted += OnScanStarted;
        App.Ble.DeviceFound += OnDeviceFound;
        App.Ble.Connected += OnConnected;
        App.Ble.Disconnected += OnDisconnected;
        App.Ble.HeartRate += OnHeartRate;
        App.Ble.Error += OnBleError;
        App.Osc.Received += OnOscReceived;
        GitHubProjectService.Updated += OnGitHubUpdated;
        _wired = true;
    }

    public List<string> Execute(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return new();
        var cmd = args[0].ToLowerInvariant();
        if (cmd == "selftest")
        {
            SelfTest.Run().GetAwaiter().GetResult();
            return new();
        }
        if (cmd == "clear") return new();
        return CommandShell.Execute(args);
    }

    public List<string> Execute(string line, Action? beforeSelfTest = null, Action? afterSelfTest = null)
    {
        var cmd = FirstToken(line);
        if (cmd == "selftest")
        {
            beforeSelfTest?.Invoke();
            try { SelfTest.Run().GetAwaiter().GetResult(); }
            finally { afterSelfTest?.Invoke(); }
            return new();
        }
        if (cmd == "clear") return new();
        return CommandShell.Execute(line);
    }

    public static bool IsExit(string line)
    {
        var cmd = FirstToken(line);
        return cmd is "exit" or "quit" && !line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(x => x.Equals("--force", StringComparison.OrdinalIgnoreCase));
    }

    static string FirstToken(string line) => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault()?.ToLowerInvariant() ?? "";

    bool CheckComponents(bool interactive)
    {
        var reports = ComponentVersions.CheckAll();
        App.ComponentsOk = reports.All(r => r.Ok);
        if (App.ComponentsOk == true)
        {
            App.Log.Info(LogText.L("log.component.ok"));
            return true;
        }
        var lines = new List<string> { Txt.T("comp.mismatch") };
        foreach (var r in reports.Where(r => !r.Ok))
        {
            App.Log.Error(LogText.L("log.component.mismatch", r.ExeName, r.Actual, r.Declared, r.Error ?? "-"));
            lines.Add(r.Exists ? Txt.T("comp.line", r.ExeName, r.Actual, r.Declared) + (r.Error != null ? $" / {r.Error}" : "")
                : Txt.T("comp.missing", r.ExeName));
        }
        CliUi.Print(lines);
        // One-shot, pipeline, and batch operation must never attempt ReadKey.
        if (!interactive || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine(Txt.T("comp.exited"));
            Environment.ExitCode = 1;
            return false;
        }
        Console.Write(Txt.T("comp.continue"));
        var key = Console.ReadKey(intercept: true);
        Console.WriteLine();
        if (key.Key is ConsoleKey.Y or ConsoleKey.Enter) return true;
        Console.Error.WriteLine(Txt.T("comp.exited"));
        Environment.ExitCode = 1;
        return false;
    }

    static void EnsureHub()
    {
        if (App.Hub != null) return;
        var hub = new AppHub();
        App.Hub = hub;
        hub.Start();
        var port = App.Config.Web.Port is > 0 and < 65536 ? App.Config.Web.Port : 9460;
        App.WebPort = port;
        var host = new Web.WebHost(hub, port, autoOpen: false);
        App.Web = host;
        hub.WebStartHook = () => host.Start(openUi: false);
        hub.WebStopHook = host.Stop;
        hub.WebRunningHook = () => host.Running;
        hub.WebPortHook = () => port;
        hub.WebSchemeHook = () => App.WebScheme;
    }

    void OnScanStarted() { lock (_seenLock) _seen.Clear(); Events.Enqueue(Txt.T("evt.found"), "scan started"); }
    void OnDeviceFound(string mac, string name, int rssi, string type)
    {
        if (!App.Ble.Scanning) return;
        bool add;
        lock (_seenLock)
        {
            _seen.TryGetValue(mac, out var previous);
            var actual = name is null or "" or "UNKNOWN" ? null : name;
            add = previous == null || (actual != null && previous == "UNKNOWN");
            if (add) _seen[mac] = actual ?? "UNKNOWN";
        }
        if (add) Events.Enqueue(Txt.T("evt.found"), $"[NEW] {name,-24} {mac}  RSSI={rssi}  {type}");
    }
    void OnConnected(string mac, string name) => Events.Enqueue(Txt.T("evt.connected"), $"{name} ({mac})");
    void OnDisconnected(string mac, string name, bool manual) => Events.Enqueue(Txt.T("evt.disconnected"), $"{name} ({mac})");
    void OnHeartRate(string mac, string name, int bpm) => Events.HeartRate(mac, Txt.T("evt.hr"), $"{name}: {bpm} bpm");
    void OnBleError(string message) => Events.Enqueue("error", Txt.T("evt.ble_error", message));
    void OnOscReceived(string address, List<Dictionary<string, object?>> args)
        => Events.Enqueue("OSC", $"{address} ({args.Count})");
    void OnGitHubUpdated() => Events.Enqueue("GitHub", "project cache updated");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_wired)
        {
            App.Ble.ScanStarted -= OnScanStarted;
            App.Ble.DeviceFound -= OnDeviceFound;
            App.Ble.Connected -= OnConnected;
            App.Ble.Disconnected -= OnDisconnected;
            App.Ble.HeartRate -= OnHeartRate;
            App.Ble.Error -= OnBleError;
            App.Osc.Received -= OnOscReceived;
            GitHubProjectService.Updated -= OnGitHubUpdated;
            _wired = false;
        }
        try { App.Ble.StopScan(); } catch { }
        try { App.Ble.DisconnectAll(); } catch { }
        try { App.Web?.Stop(); } catch { }
        try { App.Hub?.Stop(); } catch { }
        try { App.Osc.Stop(); } catch { }
        App.Web = null;
        App.Hub = null;
    }
}