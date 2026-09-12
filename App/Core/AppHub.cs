using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HeartRateMonitor.Ble;
using HeartRateMonitor.SysInfo;
using HeartRateMonitor.UI;
using Microsoft.Win32;

namespace HeartRateMonitor.Core;

/// <summary>
/// Shared business core that subscribes to service events, dispatches them consistently, and implements all commands.
/// Web is the sole transport layer: WebServer exposes REST and WebSocket connections directly to the frontend,
/// while business logic remains centralized in <see cref="AppHub"/>.
/// </summary>
public sealed class AppHub
{
    /// <summary>Discovered devices; RSSI and type come from scan events, while BLE state is stored separately.</summary>
    private readonly ConcurrentDictionary<string, (string name, int rssi, string type)> _known = new();

    /// <summary>Client events, expressed as type and data, for Web broadcasts or IPC delivery.</summary>
    public event Action<string, object?>? ClientEvent;

    /// <summary>Dispatch work to the WinForms UI thread through the host; execute directly by default.</summary>
    public static Action<Action> UiInvoke = a => a();
    private static int _shutdownStarted;

    // ------------------------------------------------------------------ Lifecycle

    public void Start()
    {
        HrmDb.Init();
        HrmDb.Recording = App.Config.Recording.Recording;
        RecordStore.CleanupAll();
        WireEvents();
        if (!App.SafeMode)
        {
            ApplyPushTimer();
            StartVrchatTimer();
        }
    }

    public void Stop()
    {
        UnwireEvents();
        _pushTimer?.Dispose();
        _pushTimer = null;
        _vrTimer?.Dispose();
        _vrTimer = null;
        _oscFlushTimer?.Dispose();
        _oscFlushTimer = null;
    }

    private void WireEvents()
    {
        App.Ble.DeviceFound += OnDeviceFound;
        App.Ble.HeartRate += OnHeartRate;
        App.Ble.Connecting += OnConnecting;
        App.Ble.Connected += OnConnected;
        App.Ble.Disconnected += OnDisconnected;
        App.Ble.ScanStarted += () => Push("scan", new { scanning = true });
        App.Ble.ScanStopped += () => Push("scan", new { scanning = false });
        App.Osc.ConnectionChanged += v => Push("osc_status", new { connected = v });
        App.Osc.Received += OnOscReceived;
        App.SysInfo.Updated += OnSysInfoUpdated;
        // Round 36 E4: push local floating-window additions and removals immediately, including tray, context-menu, CLI, and manual-close actions,
        // so the frontend FloatToggle updates at once instead of relying on the five-second polling fallback.
        UI.FloatWindowHost.Changed += () => Push("float", null);
        App.Log.OnLog += line =>
        {
            // MainForm used to be the only producer for this buffer, but the default hub-and-tray path never creates MainForm.
            // That left log queries and exports empty, so the hub now enqueues them; the two paths are mutually exclusive and cannot duplicate entries.
            App.LogBuffer.Enqueue(line);
            while (App.LogBuffer.Count > 5000) App.LogBuffer.TryDequeue(out _);
            Push("log", new { line });
        };
        App.Health.StatusChanged += OnHealthChanged;
        // P3: broadcast the GitHub snapshot (including update state) whenever a refresh completes.
        GitHubProjectService.Updated += PushGitHub;
    }

    void PushGitHub()
    {
        try { Push("github", GitHubInfo()); } catch { }
    }

    // ---- P3: GitHub project info ----

    /// <summary>Read-only project snapshot plus update state for the About page and the settings toggle.</summary>
    public object GitHubInfo()
    {
        var s = GitHubProjectService.Current;
        return new
        {
            ok = true,
            snapshot = s == null ? null : new
            {
                repo = s.Repo,
                stars = s.Stars,
                forks = s.Forks,
                latestCommitSha = s.LatestCommitSha,
                latestCommitTime = s.LatestCommitTime,
                issuesTotal = s.IssuesTotal,
                issuesOpen = s.IssuesOpen,
                fetchedAt = s.FetchedAt,
                error = s.Error,
                stale = s.Stale,
                latestRelease = s.LatestRelease == null ? null : new
                {
                    tag = s.LatestRelease.Tag,
                    name = s.LatestRelease.Name,
                    body = s.LatestRelease.Body,
                    url = s.LatestRelease.Url,
                    publishedAt = s.LatestRelease.PublishedAt,
                    assets = s.LatestRelease.AssetNames,
                },
            },
            update = UpdateState(),
        };
    }

    /// <summary>Update-check state: local identity, latest identity, availability, and skip status.</summary>
    object UpdateState()
    {
        var rel = GitHubProjectService.Current?.LatestRelease;
        var has = rel != null && rel.Tag.Length > 0;
        return new
        {
            checkEnabled = App.Config.App.UpdateCheck,
            hasLatest = has,
            current = has && GitHubProjectService.IsCurrentRelease(rel!),
            available = has && !GitHubProjectService.IsCurrentRelease(rel!) && !GitHubProjectService.IsSkipped(rel!),
            skipped = has && GitHubProjectService.IsSkipped(rel!),
            localTag = ReleaseManifest.Version,
            localName = ReleaseManifest.ReleaseName,
            latestTag = has ? rel!.Tag : "",
            latestName = has ? rel!.Name : "",
            assetPattern = ReleaseManifest.AssetPattern,
            buildTarget = ReleaseManifest.BuildTarget,
        };
    }

    /// <summary>Notify the frontend that a connection has started immediately, without waiting for the five-second polling cycle.</summary>
    private void OnConnecting(string mac, string name)
    {
        Push("connecting", new { mac, name = DeviceRegistry.DisplayName(mac, name) });
        BroadcastDevices();
    }

    private void OnConnected(string mac, string name)
    {
        DeviceRegistry.RecordHistory(mac);
        App.Devices.CancelReconnect(mac);
        // Windows stops delivering advertisements after connection, so mark RSSI and advertisement frequency unavailable rather than presenting stale values as live data.
        App.Devices.OnConnected(mac);
        var displayName = DeviceRegistry.DisplayName(mac, name);
        RecordStore.Write(RecordStore.Devices, "connected", new { mac, name = displayName, manual = false });
        Push("connected", new { mac, name = displayName });
        BroadcastDevices();
        FireWebhook("connected");
    }

    private void OnDisconnected(string mac, string name, bool manual)
    {
        // Notification frequency is meaningful only while connected, so clear it on disconnection.
        App.Devices.OnDisconnected(mac);
        var displayName = DeviceRegistry.DisplayName(mac, name);
        RecordStore.Write(RecordStore.Devices, "disconnected", new { mac, name = displayName, manual });
        Push("disconnected", new { mac, name = displayName, manual });
        BroadcastDevices();
        FireWebhook("disconnected");
        // Automatically reconnect only after an unexpected disconnection.
        if (!manual) App.Devices.ScheduleReconnect(mac);
    }

    private void OnHealthChanged(string statusKey)
    {
        Push("health_status", new { status = App.Health.StatusText, statusKey });
    }

    private void OnOscReceived(string addr, List<Dictionary<string, object?>> args)
    {
        HrmDb.InsertOsc(addr, JsonSerializer.Serialize(args));
        if (addr.Equals("/avatar/change", StringComparison.OrdinalIgnoreCase))
            RecordStore.Write(RecordStore.Avatar, "change", new { value = args.Count > 0 ? args[0].GetValueOrDefault("v") : null });
        // Feed body-state parameters to health evaluation: AFK, Seated, and VelocityMagnitude.
        App.Health.OnOscParam(addr, args.Count > 0 ? args[0].GetValueOrDefault("v") : null);
        BufferOscParam(addr, args);
    }

    // ---- OSC receive buffer: VRChat can send dozens of parameters per frame, so pushing each one would saturate IPC and the frontend. ----

    /// <summary>Latest arguments and cumulative count grouped by address.</summary>
    private readonly Dictionary<string, (List<Dictionary<string, object?>> Args, long Count)> _oscAgg = new();
    /// <summary>Addresses updated since the previous dispatch, used for incremental updates.</summary>
    private readonly HashSet<string> _oscDirty = new();
    private readonly object _oscBufLock = new();
    private System.Threading.Timer? _oscFlushTimer;
    /// <summary>Interval between aggregate snapshots in milliseconds; 200 ms is approximately five updates per second and appears smooth.</summary>
    private const int OscFlushMs = 200;

    private void BufferOscParam(string addr, List<Dictionary<string, object?>> args)
    {
        lock (_oscBufLock)
        {
            // The address set is bounded: VRChat parameter tables usually contain a few hundred entries, so after the limit only known addresses are updated.
            if (!_oscAgg.ContainsKey(addr) && _oscAgg.Count >= 2000) return;
            var count = _oscAgg.TryGetValue(addr, out var old) ? old.Count : 0;
            _oscAgg[addr] = (args, count + 1);
            _oscDirty.Add(addr);
            // Start the timer only when the first item arrives so idle periods consume no thread-pool work.
            _oscFlushTimer ??= new System.Threading.Timer(_ => FlushOscBuffer(), null, OscFlushMs, OscFlushMs);
        }
    }

    /// <summary>Dispatch all changed addresses as one osc_params event; do nothing when no values changed.</summary>
    private void FlushOscBuffer()
    {
        object? payload;
        lock (_oscBufLock)
        {
            if (_oscDirty.Count == 0) return;
            payload = OscParamsPayload(_oscDirty);
            _oscDirty.Clear();
        }
        Push("osc_params", payload);
    }

    /// <summary>Build an aggregate payload; keys=null requests a full snapshot when the frontend opens the page.</summary>
    private object OscParamsPayload(IEnumerable<string>? keys)
    {
        var src = keys ?? _oscAgg.Keys.ToArray();
        var items = src
            .Where(_oscAgg.ContainsKey)
            .Select(k => (object)new { addr = k, args = _oscAgg[k].Args, count = _oscAgg[k].Count })
            .ToArray();
        return new { items, recv = App.Osc.RecvCount };
    }

    /// <summary>Return the full OSC parameter aggregation table.</summary>
    public object OscParams()
    {
        lock (_oscBufLock) return OscParamsPayload(null);
    }

    /// <summary>Clear the OSC parameter aggregation table when the frontend Clear button is pressed.</summary>
    public object OscParamsClear()
    {
        lock (_oscBufLock)
        {
            _oscAgg.Clear();
            _oscDirty.Clear();
        }
        return new { ok = true };
    }

    private void UnwireEvents()
    {
        App.Ble.DeviceFound -= OnDeviceFound;
        App.Ble.HeartRate -= OnHeartRate;
        App.Ble.Connecting -= OnConnecting;
        App.Ble.Connected -= OnConnected;
        App.Ble.Disconnected -= OnDisconnected;
        App.Osc.Received -= OnOscReceived;
        App.SysInfo.Updated -= OnSysInfoUpdated;
        App.Health.StatusChanged -= OnHealthChanged;
        GitHubProjectService.Updated -= PushGitHub;
    }

    private void OnDeviceFound(string mac, string name, int rssi, string type)
    {
        if (IsBlocked(mac)) return;
        // The registry handles identifier caching and refresh throttling; do not disturb the frontend when throttling rejects an update.
        if (!App.Devices.Observe(mac, name, rssi, type, out var e)) return;
        _known[mac] = (e.Name, e.Rssi, e.Type);
        Push("device_found", new { mac, name = DeviceRegistry.DisplayName(mac, e.Name), rssi = e.Rssi, type = e.Type });
    }

    private void OnHeartRate(string mac, string name, int bpm)
    {
        RecordStore.WriteHeartRate(mac, name, bpm);
        App.Devices.OnReport(mac);
        App.Health.OnHeartRate(bpm);
        // Derive the main displayed heart rate from the primary floating-window source, shared by {BPM}, status.bpm, and that window.
        App.CurrentBpm = FloatWindowHost.BpmOfSource(FloatWindowHost.SourceOf(FloatWindowHost.MainId));
        App.SysInfo.UpdateHeartRateVars();
        // Include source aggregates and live counters so every frontend can update curves and device cards without polling.
        var entry = App.Devices.Get(mac);
        Push("heart_rate", new
        {
            mac,
            name = DeviceRegistry.DisplayName(mac, name),
            bpm,
            mainBpm = App.CurrentBpm,
            avg = App.Ble.AverageBpm(),
            notifyHz = Math.Round(entry?.NotifyHz ?? 0, 2),
            reports = entry?.Reports ?? 0,
        });
        UiInvoke(FloatWindowHost.RefreshAll);
        FireWebhook("heart_rate_updated");
    }

    private void OnSysInfoUpdated()
    {
        Push("sysinfo", null);
        RecordStore.Write(RecordStore.Hardware, "snapshot", App.SysInfo.Vars.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private void Push(string type, object? data) => ClientEvent?.Invoke(type, data);

    /// <summary>
    /// Push a Windows theme snapshot to every frontend through the sys_theme event.
    /// TrayHost calls this on WM_SETTINGCHANGE and WM_DWMCOLORIZATIONCOLORCHANGED
    /// so frontends following the system update their light/dark mode and accent color immediately.
    /// </summary>
    public void PushSystemTheme() => Push("sys_theme", UI.SystemTheme.Snapshot());

    // ---- Phase 6: WebHook triggers, throttled by Api.WebhookThrottleMs----

    private DateTime _lastWebhook = DateTime.MinValue;
    private System.Threading.Timer? _pushTimer;

    /// <summary>Convert hub events to outbound WebHooks; throttle heart-rate events according to configuration but never throttle connection events.</summary>
    private void FireWebhook(string eventType)
    {
        if (App.SafeMode) return;
        if (!App.Config.Webhook.Enabled || App.Webhooks.Webhooks.Count == 0) return;
        if (eventType == "heart_rate_updated")
        {
            // Item #11 removes the upper bound and retains only zero as the minimum; users control the risk of extreme intervals.
            var throttle = Math.Max(0, App.Config.Api.WebhookThrottleMs);
            if (throttle > 0)
            {
                if (DateTime.Now - _lastWebhook < TimeSpan.FromMilliseconds(throttle)) return;
                _lastWebhook = DateTime.Now;
            }
        }
        try { App.Webhooks.Trigger(eventType, App.CurrentBpm); } catch { }
    }

    /// <summary>Rebuild the system-information push timer according to Api.PushSysInfo and PushIntervalMs.</summary>
    private void ApplyPushTimer()
    {
        _pushTimer?.Dispose();
        _pushTimer = null;
        if (App.SafeMode) return;
        if (!App.Config.Api.Enabled || !App.Config.Api.PushSysInfo) return;
        // Item #11 removes the upper bound but keeps the 200 ms minimum to prevent excessive WebSocket pushes.
        var period = Math.Max(200, App.Config.Api.PushIntervalMs);
        _pushTimer = new System.Threading.Timer(_ =>
        {
            try { Push("sysinfo_vars", SysInfoPayload()); } catch { }
        }, null, period, period);
    }

    /// <summary>System-information payload filtered by the Api.SysInfoVars allowlist; an empty list includes all variables.</summary>
    private static object SysInfoPayload()
    {
        var want = App.Config.Api.SysInfoVars;
        var vars = want.Count > 0
            ? want.Where(k => App.SysInfo.Vars.ContainsKey(k)).ToDictionary(k => k, k => App.SysInfo.Vars[k])
            : App.SysInfo.Vars.ToDictionary(kv => kv.Key, kv => kv.Value);
        return new { vars, bpm = App.CurrentBpm, ts = $"{DateTime.Now:O}" };
    }

    // ---- VRChat runtime status for item #17: push a five-second sample over WebSocket and serve direct REST queries at /api/vrchat ----
    private System.Threading.Timer? _vrTimer;
    private readonly object _vrLock = new();
    private DateTime _vrSampleAt = DateTime.MinValue;
    private double _vrPrevCpuMs;

    private void StartVrchatTimer()
    {
        _vrTimer?.Dispose();
        _vrTimer = null;
        if (App.SafeMode) return;
        _vrTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                var snapshot = VrchatStatusSnapshot();
                Push("vrchat_status", snapshot);
                RecordStore.WriteVrchatStatus(snapshot.Running, snapshot);
            }
            catch { }
        }, null, 3000, 5000);
    }

    /// <summary>VRChat process status: PID, CPU percentage from TotalProcessorTime deltas, working set, start time, and responsiveness.
    /// Return running=false when the process is absent so the frontend can display its empty state.</summary>
    public object VrchatStatus() => VrchatStatusSnapshot();

    private VrchatSnapshot VrchatStatusSnapshot()
    {
        lock (_vrLock)
        {
            Process? p = null;
            try
            {
                foreach (var proc in Process.GetProcessesByName("VRChat"))
                {
                    if (proc.Id != 0) { p = proc; break; }
                }
            }
            catch { /* Treat access-denied errors as not running. */ }

            if (p == null)
            {
                _vrSampleAt = DateTime.MinValue;
                _vrPrevCpuMs = 0;
                return new VrchatSnapshot(false);
            }

            var now = DateTime.UtcNow;
            double cpu = 0;
            long wsBytes = 0;
            bool responding = true;
            string? start = null;
            try
            {
                var ticks = p.TotalProcessorTime.TotalMilliseconds;
                if (_vrSampleAt != DateTime.MinValue && _vrPrevCpuMs > 0)
                {
                    var dt = (now - _vrSampleAt).TotalMilliseconds;
                    var d = ticks - _vrPrevCpuMs;
                    if (dt > 0 && d >= 0)
                        cpu = Math.Clamp(d / dt * 100.0 / Math.Max(1, Environment.ProcessorCount), 0, 100);
                }
                _vrSampleAt = now;
                _vrPrevCpuMs = ticks;
                p.Refresh();
                wsBytes = p.WorkingSet64;
                responding = p.Responding;
                try { start = $"{p.StartTime:O}"; } catch { }
            }
            catch { /* Return the current snapshot if the process has exited or another process error occurs. */ }

            return new VrchatSnapshot(
                true,
                p.Id,
                Math.Round(cpu, 1),
                Math.Round(wsBytes / 1024d / 1024d, 1),
                responding,
                start ?? "");
        }
    }

    private sealed record VrchatSnapshot(
        [property: JsonPropertyName("running")] bool Running,
        [property: JsonPropertyName("pid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Pid = null,
        [property: JsonPropertyName("cpuPct"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? CpuPct = null,
        [property: JsonPropertyName("memoryMb"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? MemoryMb = null,
        [property: JsonPropertyName("responding"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Responding = null,
        [property: JsonPropertyName("startTime"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StartTime = null);

    /// <summary>Launch VRChat (plan #674/E1): resolve the installed executable and start it, falling back to the
    /// Steam URL when no local install is found. Refuses to launch a second copy while the process is running and
    /// pushes a fresh status snapshot so the frontend reflects the new process immediately.</summary>
    public object VrchatLaunch()
    {
        HrmTrace.Event("cmd.vrchat.launch");
        if (App.SafeMode) return new { ok = false, launched = false, error = "safe mode blocks external actions" };
        if (VrchatStatusSnapshot().Running)
            return new { ok = true, running = true, launched = false };
        var target = FindVrchatExe() ?? "steam://rungameid/438100";
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            App.Log.Info(LogText.L("log.vrchat.launch", target));
            Push("vrchat_status", VrchatStatusSnapshot());
            return new { ok = true, running = false, launched = true };
        }
        catch (Exception e)
        {
            App.Log.Warn(LogText.L("log.vrchat.launch_fail", e.Message));
            return new { ok = false, error = e.Message };
        }
    }

    /// <summary>Locate VRChat.exe through the uninstall registry (HKLM/HKCU, 64/32-bit views) and common Steam library paths.</summary>
    private static string? FindVrchatExe()
    {
        try
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (var sub in uninstall.GetSubKeyNames())
                {
                    using var k = uninstall.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is not string name
                        || !name.Contains("VRChat", StringComparison.OrdinalIgnoreCase)) continue;
                    if (k.GetValue("InstallLocation") is not string dir || dir.Length == 0) continue;
                    var exe = Path.Combine(dir, "VRChat.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
        }
        catch { /* Registry unavailable: fall through to the Steam path probe. */ }
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (root.Length == 0) continue;
            var exe = Path.Combine(root, "Steam", "steamapps", "common", "VRChat", "VRChat.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    // ------------------------------------------------------------------ Commands

    public object Status() => StatusSnapshot();

    public object Devices()
    {
        var macs = AllMacs();
        return macs.Select(DeviceJson).ToArray();
    }

    public object Scan(string action)
    {
        HrmTrace.Event("cmd.scan", action);
        if (App.SafeMode && action == "start") return new { ok = false, scanning = false, error = "safe mode blocks external actions" };
        // Continuous scanning has no duration limit; advertisement callbacks drive it with negligible power use.
        if (action == "start") App.Ble.StartScan(App.Config.Devices.ContinuousScan ? null : 30);
        else if (action == "stop") App.Ble.StopScan();
        return new { ok = true, scanning = App.Ble.Scanning };
    }

    public object Connect(string mac)
    {
        if (App.SafeMode) return new { ok = false, error = "safe mode blocks external actions" };
        if (mac.Length == 0 || IsBlocked(mac)) return new { ok = false };
        _ = App.Ble.ConnectAsync(mac);
        return new { ok = true };
    }

    public object Disconnect(string mac)
    {
        if (mac.Length > 0) App.Ble.Disconnect(mac);
        return new { ok = true };
    }

    public object Save(string mac, bool? want)
    {
        if (mac.Length > 0)
        {
            var before = App.Config.SnapshotJson();
            var list = App.Config.HeartRate.Devices;
            var wasSaved = IsSaved(mac);
            var target = want ?? !wasSaved;
            if (target && !wasSaved) list.Add(mac);
            if (!target && wasSaved) list.RemoveAll(m => m.Equals(mac, StringComparison.OrdinalIgnoreCase));
            App.Config.SaveIfChanged(before);
            BroadcastDevices();
        }
        return new { ok = true };
    }

    public object Block(string mac)
    {
        if (mac.Length > 0)
        {
            var before = App.Config.SnapshotJson();
            BlockDevice(mac);
            App.Config.SaveIfChanged(before);
        }
        return new { ok = true };
    }

    public object Unblock(string mac)
    {
        if (mac.Length > 0)
        {
            var before = App.Config.SnapshotJson();
            UnblockDevice(mac);
            App.Config.SaveIfChanged(before);
        }
        return new { ok = true };
    }

    public object Batch(string action, List<string> macs)
    {
        if (App.SafeMode && action == "connect") return new { ok = false, error = "safe mode blocks external actions" };
        var persist = action is "save" or "unsave" or "block" or "unblock";
        var before = persist ? App.Config.SnapshotJson() : null;
        foreach (var mac in macs)
        {
            switch (action)
            {
                case "connect":
                    if (!IsBlocked(mac)) _ = App.Ble.ConnectAsync(mac);
                    break;
                case "disconnect": App.Ble.Disconnect(mac); break;
                case "save":
                    if (!IsSaved(mac) && !IsBlocked(mac)) App.Config.HeartRate.Devices.Add(mac);
                    break;
                case "unsave":
                    if (IsSaved(mac)) App.Config.HeartRate.Devices.RemoveAll(m => m.Equals(mac, StringComparison.OrdinalIgnoreCase));
                    break;
                case "block": BlockDevice(mac); break;
                case "unblock": UnblockDevice(mac); break;
            }
        }
        if (persist)
        {
            App.Config.SaveIfChanged(before!);
            BroadcastDevices();
        }
        App.Log.Info(LogText.L("log.hub.batch", action, macs.Count));
        return new { ok = true };
    }

    public object OscConnect(bool connected)
    {
        if (App.SafeMode && connected) return new { ok = false, error = "safe mode blocks external actions" };
        App.Osc.SetConnected(connected);
        return new { ok = true };
    }

    public object OscConfig(JsonObject? body)
    {
        if (body != null)
        {
            var before = App.Config.SnapshotJson();
            ApplyOsc(body);
            // Avoid disk writes when content is unchanged, such as the same value submitted during heartbeat-page initialization, and avoid flashing Saved.
            if (App.Config.SaveIfChanged(before)) App.Log.Info(LogText.L("log.hub.osc_saved"));
        }
        return new { ok = true };
    }

    public object OscTest(string ip, string port, string address, string text)
    {
        if (App.SafeMode) return new { ok = false, error = "safe mode blocks external actions" };
        var ok = App.Osc.SendTest(ip, port, address, text);
        App.Log.Info(LogText.L(ok ? "log.hub.osc_test_ok" : "log.hub.osc_test_fail", address, text));
        return new { ok };
    }

    /// <summary>
    /// Custom send for the remote frontend and OSC page: first render variables such as {BPM} in the text.
    /// When pauseMs is positive, pause the pusher so the template does not overwrite the new chat text.
    /// Item #32 supports typed writes: kind=bool uses a fixed flag and kind=float parses the rendered text.
    /// Omitting kind preserves text mode.
    /// </summary>
    public object OscCustom(JsonObject? body)
    {
        if (App.SafeMode) return new { ok = false, error = "safe mode blocks external actions" };
        var ip = body?["ip"]?.GetValue<string>() ?? App.Config.Osc.Ip;
        var port = body?["port"]?.GetValue<string>() ?? App.Config.Osc.Port;
        var address = body?["address"]?.GetValue<string>() ?? "/chatbox/input";
        var text = body?["text"]?.GetValue<string>() ?? "";
        var kind = (body?["kind"]?.GetValue<string>() ?? "text").ToLowerInvariant();
        var pauseMs = body?["pauseMs"] is JsonValue pv && pv.TryGetValue<int>(out var p) ? Math.Max(0, p) : 0; // Item #11 removes the upper bound.
        bool ok;
        if (kind == "bool")
        {
            var flag = body?["flag"] is JsonValue fv && fv.GetValue<bool>();
            ok = App.Osc.SendParam(ip, port, address, "bool", flag, 0);
        }
        else if (kind == "float")
        {
            text = App.SysInfo.FormatTemplate(text);
            ok = float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var num)
                 && App.Osc.SendParam(ip, port, address, "float", false, num);
        }
        else
        {
            text = App.SysInfo.FormatTemplate(text);
            ok = App.Osc.SendTest(ip, port, address, text);
        }
        if (ok && pauseMs > 0 && App.Osc.Connected)
        {
            App.Osc.PausePush(pauseMs);
            App.Log.Info(LogText.L("log.hub.osc_custom_pause", pauseMs));
        }
        else App.Log.Info(LogText.L(ok ? "log.hub.osc_custom_ok" : "log.hub.osc_custom_fail", address));
        return new { ok };
    }

    public object Hw() => new { vars = App.SysInfo.Vars.ToDictionary(kv => kv.Key, kv => kv.Value) };

    /// <summary>Text commands for Phase 9, shared with the CLI REPL through CommandShell, returning output lines.</summary>
    public object Cli(string line)
    {
        HrmTrace.Event("cmd.cli", line);
        var lines = CommandShell.Execute(line);
        return new { ok = true, lines };
    }

    /// <summary>Build the historical aggregate report for Monitor in Phase 10.</summary>
    public object Monitor(int hours, int buckets)
    {
        HrmTrace.Event("cmd.monitor", $"{hours}h/{buckets}");
        return MonitorStats.Build(hours, buckets <= 0 ? 12 : buckets);
    }

    /// <summary>Export a statistics report to exports/ as TXT, JSON, YAML, or CSV.</summary>
    public object MonitorExport(int hours, int buckets, string format)
    {
        var report = MonitorStats.Build(hours, buckets <= 0 ? 12 : buckets);
        var (ok, file, error) = Exporter.ExportReport(report, string.IsNullOrWhiteSpace(format) ? "json" : format);
        return new { ok, file, error };
    }

    public object HwRefresh()
    {
        App.SysInfo.CollectFull();
        return new { ok = true };
    }

    /// <summary>
    /// Query logs using a keyword substring or regular expression and optional severity filters, returning the most recent limit lines.
    /// Invalid regular expressions do not throw; filtering is skipped and the error is returned for the frontend to display.
    /// </summary>
    public object Logs(string filter, int limit, bool regex, List<string>? levels)
    {
        var (lines, error) = FilterLogs(filter, regex, levels);
        var total = App.LogBuffer.Count;
        var matched = lines.Count;
        if (matched > limit) lines = lines.GetRange(matched - limit, limit);
        return new { lines, total, matched, error };
    }

    /// <summary>Export the current filtered log result to exports/ as TXT, JSON, YAML, or CSV.</summary>
    public object LogsExport(string filter, string format, bool regex, List<string>? levels)
    {
        var (lines, error) = FilterLogs(filter, regex, levels);
        if (error.Length > 0) return new { ok = false, file = "", error, count = 0 };
        var (ok, file, err) = Exporter.ExportLogs(lines, format);
        return new { ok, file, error = err, count = lines.Count };
    }

    /// <summary>Log filtering core: match severities by [LEVEL] prefix and keywords using Regex or a case-insensitive substring according to the regex switch.</summary>
    static (List<string> lines, string error) FilterLogs(string filter, bool regex, List<string>? levels)
    {
        IEnumerable<string> q = App.LogBuffer.ToArray();
        if (levels is { Count: > 0 })
        {
            var tags = levels.Select(x => $"[{x.Trim().ToUpperInvariant()}]").ToArray();
            q = q.Where(x => tags.Any(t => x.Contains(t, StringComparison.Ordinal)));
        }
        if (filter.Length > 0)
        {
            if (regex)
            {
                try
                {
                    var re = new Regex(filter, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    q = q.Where(x => re.IsMatch(x));
                }
                catch (ArgumentException e)
                {
                    return (q.ToList(), e.Message);
                }
            }
            else q = q.Where(x => x.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        return (q.ToList(), "");
    }

    public object LogsClear()
    {
        while (App.LogBuffer.TryDequeue(out _)) { }
        return new { ok = true };
    }

    public object LogsDump()
    {
        var f = App.Log.Dump(App.LogBuffer.ToList());
        App.Log.Info(LogText.L("log.hub.log_dump", f));
        return new { ok = true, file = f };
    }

    /// <summary>
    /// Log-section configuration. traceEnabled takes effect on the next startup;
    /// traceActive reports the current process state so the frontend can request a restart.
    /// </summary>
    public object LogsConfig() => new
    {
        autoDumpEnabled = App.Config.Logs.AutoDumpEnabled,
        autoDumpIntervalMin = App.Config.Logs.AutoDumpIntervalMin,
        traceEnabled = App.Config.Logs.TraceEnabled,
        traceActive = App.TraceEnabled,
    };

    public object Webhooks() => new { list = App.Webhooks.Webhooks };

    public object WebhookAction(string action, JsonObject? item, int index) => HandleWebhook(action, item, index);

    public object Settings(JsonObject? body)
    {
        HrmTrace.Event("cmd.settings");
        // The storage location is data_location.txt beside the executable rather than config.json; process it first and fail the whole request on error.
        if (body?["paths"] is JsonObject pth && pth["dataDir"] is JsonValue pd)
        {
            var (ok, error) = AppBoot.SetDataLocation(pd.GetValue<string>() ?? "");
            if (!ok) return new { ok = false, error };
            ApplySettings(body);
            return new { ok = true, restart = true };
        }
        ApplySettings(body);
        return new { ok = true };
    }

    // ---- P1: login autostart ----

    /// <summary>Autostart state and control: GET (null body) returns the actual system registrations;
    /// POST applies the selected methods (empty list disables autostart) and persists the intent.</summary>
    public object AutoStart(JsonObject? body)
    {
        HrmTrace.Event("cmd.autostart");
        var silent = App.Config.App.AutoStartSilent;
        if (body != null)
        {
            if (!body.ContainsKey("methods"))
                return new { ok = false, error = "methods required", methods = App.Config.App.AutoStartMethods, silent, status = AutoStartManager.Status(silent) };
            if (body["silent"] is JsonValue sv && sv.TryGetValue<bool>(out var s)) silent = s;
            var methods = new List<AutoStartMethod>();
            IEnumerable<string?> keys;
            if (body["methods"] is JsonArray arr)
            {
                keys = arr.Select(v => v is JsonValue jv && jv.TryGetValue<string>(out var key) ? key : null);
            }
            else if (body["methods"] is JsonValue mv && mv.TryGetValue<string>(out var csv))
            {
                keys = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else
            {
                return new { ok = false, error = "invalid methods", methods = App.Config.App.AutoStartMethods, silent, status = AutoStartManager.Status(silent) };
            }
            foreach (var key in keys)
            {
                var m = AutoStartManager.ParseMethod(key);
                if (m == null)
                    return new { ok = false, error = "invalid methods", methods = App.Config.App.AutoStartMethods, silent, status = AutoStartManager.Status(silent) };
                methods.Add(m.Value);
            }
            methods = methods.Distinct().ToList();
            var configuredSilent = App.Config.App.AutoStartSilent;
            var error = AutoStartManager.Apply(methods, silent);
            var actual = AutoStartManager.CurrentMethods(silent);
            if (error != null && actual.Count == 0 && silent != configuredSilent)
            {
                silent = configuredSilent;
                actual = AutoStartManager.CurrentMethods(silent);
            }
            App.Config.App.AutoStartSilent = silent;
            App.Config.App.AutoStartMethods = actual.Select(AutoStartManager.MethodKey).ToList();
            App.Config.Save();
            var names = string.Join(",", methods.Select(AutoStartManager.MethodKey));
            if (error != null)
            {
                App.Log.Error(LogText.L("log.autostart.apply_fail", error));
                return new { ok = false, error, methods = App.Config.App.AutoStartMethods, silent = App.Config.App.AutoStartSilent, status = AutoStartManager.Status(App.Config.App.AutoStartSilent) };
            }
            App.Config.App.AutoStartSilent = silent;
            App.Log.Info(LogText.L("log.autostart.applied", names.Length > 0 ? names : "-"));
        }
        return new { ok = true, methods = App.Config.App.AutoStartMethods, silent = App.Config.App.AutoStartSilent, status = AutoStartManager.Status(App.Config.App.AutoStartSilent) };
    }

    public object Config() => new
    {
        osc = OscJson(),
        hr = HrJson(),
        webhookEnabled = App.Config.Webhook.Enabled,
        logs = LogsConfig(),
        app = new { debug = App.DebugMode, version = App.Version, startTime = $"{App.StartTime:O}", baseDir = App.BaseDir, componentsOk = App.ComponentsOk, updateCheck = App.Config.App.UpdateCheck },
        // Release manifest for item #23: repository, version, build time, and license used by the About page.
        release = ReleaseManifest.Json(),
        web = WebJson(),
        ui = UiJson(),
        // Windows theme snapshot used by frontends that follow the system light/dark mode and accent color.
        sysTheme = UI.SystemTheme.Snapshot(),
        // Data and executable directories displayed and saved by the storage-location card on the Settings page.
        paths = new { dataDir = App.DataDir, exeDir = App.ExeDir, defaultDir = AppBoot.DefaultDataDir() },
        devices = DevicesConfig(null),
        health = HealthConfig(null),
        hw = HwConfig(null),
        api = ApiConfig(null),
        recording = RecordingConfig(null),
    };

    /// <summary>Round 36 E2 view preferences: GET returns all preferences so the frontend can initialize localStorage at startup.</summary>
    public object PrefsGet() => new { prefs = App.Config.Prefs };

    /// <summary>Round 36 E2 view preferences: POST merges a debounced frontend batch; keys and values are non-sensitive strings.</summary>
    public object PrefsSet(JsonObject? body)
    {
        HrmTrace.Event("cmd.prefs");
        if (body?["prefs"] is not JsonObject po) return new { ok = false, error = "no prefs" };
        var snapshot = App.Config.SnapshotJson();
        lock (App.Config.Prefs)
        {
            foreach (var kv in po)
            {
                var v = kv.Value?.GetValue<string>();
                if (v == null) App.Config.Prefs.Remove(kv.Key);
                else App.Config.Prefs[kv.Key] = v;
            }
        }
        App.Config.SaveIfChanged(snapshot);
        return new { ok = true };
    }

    public object FloatOpen(string? id)
    {
        HrmTrace.Event("cmd.float_open", id ?? "__main__");
        UiInvoke(() => FloatWindowHost.Open(id ?? "__main__"));
        return new { ok = true };
    }

    /// <summary>Open one floating window for every connected device; windows already open are brought forward.</summary>
    public object FloatOpenAll()
    {
        HrmTrace.Event("cmd.float_open_all");
        var macs = App.Ble.ConnectedDevices().Select(d => d.Mac).ToList();
        UiInvoke(() =>
        {
            foreach (var mac in macs) FloatWindowHost.Open(mac);
        });
        return new { ok = true };
    }

    public object FloatCloseAll()
    {
        HrmTrace.Event("cmd.float_close_all");
        UiInvoke(FloatWindowHost.CloseAll);
        return new { ok = true };
    }

    public object FloatClose(string id)
    {
        if (id.Length == 0) id = FloatWindowHost.MainId;
        HrmTrace.Event("cmd.float_close", id);
        UiInvoke(() => FloatWindowHost.Close(id));
        return new { ok = true };
    }

    public object FloatLock(bool locked)
    {
        HrmTrace.Event("cmd.float_lock", locked.ToString());
        App.Config.HeartRate.Window.Locked = locked;
        UiInvoke(() => FloatWindowHost.ToggleLockAll(locked));
        return new { ok = true };
    }

    /// <summary>Configure floating-window sources, refresh rate, and style, or read them when body is null. source is the average-source identifier or a device MAC, and sources maps window IDs to sources.
    /// Changes to format, unlockedColor, lockedColor, imagePath, or geometry apply immediately to all open windows.</summary>
    public object FloatConfig(JsonObject? body)
    {
        var w = App.Config.HeartRate.Window;
        var styleKeys = new[] { "format", "unlockedColor", "lockedColor", "imagePath", "geometry" };
        if (body != null && (body.ContainsKey("source") || body.ContainsKey("refreshMs") || body.ContainsKey("win")
                             || styleKeys.Any(body.ContainsKey)))
        {
            var before = App.Config.SnapshotJson();
            var touched = false;
            if (body["source"] is JsonValue s)
            {
                w.Source = s.GetValue<string>();
                App.Config.HeartRate.DisplaySource = w.Source;
                touched = true;
            }
            if (body["refreshMs"] is JsonValue r) w.RefreshMs = r.GetValue<int>(); // No upper bound; FloatWindowHost throttling applies Math.Max(0).
            // win and winSource set one window's source; an empty winSource removes the override and restores the default.
            if (body["win"] is JsonValue wv)
            {
                var win = wv.GetValue<string>();
                var src = body["winSource"] is JsonValue sv ? sv.GetValue<string>() : "";
                if (win.Length > 0)
                {
                    if (src.Length == 0) w.Sources.Remove(win);
                    else w.Sources[win] = src;
                    touched = true;
                }
            }
            // Preserve the core customization from the legacy WinForms window: text format, unlocked and locked colors, background image, and geometry.
            if (body["format"] is JsonValue f) { w.Format = f.GetValue<string>(); touched = true; }
            if (body["unlockedColor"] is JsonValue uc) { w.UnlockedColor = uc.GetValue<string>(); touched = true; }
            if (body["lockedColor"] is JsonValue lc) { w.LockedColor = lc.GetValue<string>(); touched = true; }
            if (body["imagePath"] is JsonValue ip) { w.ImagePath = ip.GetValue<string>(); touched = true; }
            if (body["geometry"] is JsonValue g) { w.Geometry = g.GetValue<string>(); touched = true; }
            if (App.Config.SaveIfChanged(before)) App.Log.Info(LogText.L("log.hub.float_src_saved"));
            App.CurrentBpm = FloatWindowHost.BpmOfSource(FloatWindowHost.SourceOf(FloatWindowHost.MainId));
            App.SysInfo.UpdateHeartRateVars();
            if (touched)
            {
                UiInvoke(FloatWindowHost.ApplySources);
                // Redraw open windows immediately for color, text, image, and geometry changes; closed windows use them next time they open.
                UiInvoke(FloatWindowHost.ApplyStyle);
            }
        }
        return new
        {
            ok = true,
            source = w.Source,
            refreshMs = w.RefreshMs,
            sources = w.Sources,
            count = FloatWindowHost.Count,
            format = w.Format,
            unlockedColor = w.UnlockedColor,
            lockedColor = w.LockedColor,
            imagePath = w.ImagePath ?? "",
            geometry = w.Geometry,
        };
    }

    public object Record(string action)
    {
        HrmTrace.Event("cmd.record", action);
        var before = App.Config.SnapshotJson();
        switch (action)
        {
            case "start": HrmDb.Recording = App.Config.Recording.Recording = true; break;
            case "stop": HrmDb.Recording = App.Config.Recording.Recording = false; break;
            case "get": break;
        }
        App.Config.SaveIfChanged(before);
        App.Log.Info(LogText.L(HrmDb.Recording ? "log.hub.rec_started" : "log.hub.rec_stopped"));
        return new { ok = true, recording = HrmDb.Recording, file = HrmDb.PathOf(), directory = RecordStore.RecordsDir, backends = App.Config.Recording.Backends, hrCount = HrmDb.Count("hr_records"), oscCount = HrmDb.Count("osc_records") };
    }

    public object RecordingConfig(JsonObject? body)
    {
        var cfg = App.Config.Recording;
        if (body != null)
        {
            var before = App.Config.SnapshotJson();
            if (body["recording"] is JsonValue recording) cfg.Recording = recording.GetValue<bool>();
            if (body["backends"] is JsonArray backends)
            {
                var allowed = new HashSet<string>(new[] { "sqlite", "jsonl", "csv" }, StringComparer.OrdinalIgnoreCase);
                cfg.Backends = backends.Select(x => x?.GetValue<string>()?.ToLowerInvariant() ?? "")
                    .Where(allowed.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            if (body["enabled"] is JsonObject enabled)
            {
                if (enabled["avatar"] is JsonValue avatar) cfg.Avatar = avatar.GetValue<bool>();
                if (enabled["vrchat"] is JsonValue vrchat) cfg.Vrchat = vrchat.GetValue<bool>();
                if (enabled["devices"] is JsonValue devices) cfg.Devices = devices.GetValue<bool>();
                if (enabled["heartRate"] is JsonValue heartRate) cfg.HeartRate = heartRate.GetValue<bool>();
                if (enabled["hardware"] is JsonValue hardware) cfg.Hardware = hardware.GetValue<bool>();
            }
            if (body["retentionDays"] is JsonObject retention)
            {
                if (retention["avatar"] is JsonValue a) cfg.RetentionDays.Avatar = Math.Max(0, a.GetValue<int>());
                if (retention["vrchat"] is JsonValue v) cfg.RetentionDays.Vrchat = Math.Max(0, v.GetValue<int>());
                if (retention["devices"] is JsonValue d) cfg.RetentionDays.Devices = Math.Max(0, d.GetValue<int>());
                if (retention["heartRate"] is JsonValue h) cfg.RetentionDays.HeartRate = Math.Max(0, h.GetValue<int>());
                if (retention["hardware"] is JsonValue w) cfg.RetentionDays.Hardware = Math.Max(0, w.GetValue<int>());
            }
            HrmDb.Recording = cfg.Recording;
            App.Config.SaveIfChanged(before);
            RecordStore.CleanupAll();
        }
        return new
        {
            ok = true,
            recording = HrmDb.Recording,
            backends = cfg.Backends,
            enabled = new
            {
                avatar = cfg.Avatar,
                vrchat = cfg.Vrchat,
                devices = cfg.Devices,
                heartRate = cfg.HeartRate,
                hardware = cfg.Hardware,
            },
            retentionDays = new
            {
                avatar = cfg.RetentionDays.Avatar,
                vrchat = cfg.RetentionDays.Vrchat,
                devices = cfg.RetentionDays.Devices,
                heartRate = cfg.RetentionDays.HeartRate,
                hardware = cfg.RetentionDays.Hardware,
            },
            directory = RecordStore.RecordsDir,
        };
    }

    // ---- Phase 3: health status ----

    /// <summary>Health status actions: get returns a snapshot, calibrate starts calibration, and cancel stops it.</summary>
    public object Health(string action)
    {
        HrmTrace.Event("cmd.health", action);
        switch (action)
        {
            case "calibrate": App.Health.StartCalibration(); break;
            case "cancel": App.Health.CancelCalibration(); break;
        }
        return new { ok = true, health = App.Health.Snapshot() };
    }

    /// <summary>Health-evaluation settings: threshold factors, fluctuation threshold, and database recording.</summary>
    public object HealthConfig(JsonObject? body)
    {
        if (body != null)
        {
            var before = App.Config.SnapshotJson();
            var h = App.Config.Health;
            if (body["sleepFactor"] is JsonValue a) h.SleepFactor = a.GetValue<double>();
            if (body["activeFactor"] is JsonValue b) h.ActiveFactor = b.GetValue<double>();
            if (body["excitedFactor"] is JsonValue c) h.ExcitedFactor = c.GetValue<double>();
            if (body["spikeDelta"] is JsonValue d) h.SpikeDelta = d.GetValue<int>();
            if (body["record"] is JsonValue e) h.Record = e.GetValue<bool>();
            if (body["restingBpm"] is JsonValue f) h.RestingBpm = f.GetValue<double>();
            if (App.Config.SaveIfChanged(before)) App.Log.Info(LogText.L("log.hub.health_saved"));
        }
        return new
        {
            ok = true,
            sleepFactor = App.Config.Health.SleepFactor,
            activeFactor = App.Config.Health.ActiveFactor,
            excitedFactor = App.Config.Health.ExcitedFactor,
            spikeDelta = App.Config.Health.SpikeDelta,
            record = App.Config.Health.Record,
            restingBpm = App.Config.Health.RestingBpm,
            restingSd = App.Config.Health.RestingSd,
            calibratedAt = App.Config.Health.CalibratedAt,
        };
    }

    /// <summary>Export records from hr_records, osc_records, health_records, or variables as JSON, YAML, CSV, or SQLite.</summary>
    public object Export(string table, string format, int limit)
    {
        HrmTrace.Event("cmd.export", $"{table}/{format}");
        var (ok, file, error) = Exporter.Export(
            string.IsNullOrWhiteSpace(table) ? "hr_records" : table,
            string.IsNullOrWhiteSpace(format) ? "json" : format,
            limit <= 0 ? 5000 : limit);
        return new { ok, file, error, formats = Exporter.Formats };
    }

    // ---- Phase 4: devices ----

    /// <summary>Rename a device using an alias; an empty alias clears it.</summary>
    public object Rename(string mac, string alias)
    {
        if (mac.Length == 0) return new { ok = false, error = "mac required" };
        DeviceRegistry.Rename(mac, alias);
        BroadcastDevices();
        return new { ok = true };
    }

    /// <summary>Configure device scanning and reconnection policies, or read them when body is null.</summary>
    public object DevicesConfig(JsonObject? body)
    {
        if (body != null)
        {
            var before = App.Config.SnapshotJson();
            var d = App.Config.Devices;
            if (body["continuousScan"] is JsonValue a) d.ContinuousScan = a.GetValue<bool>();
            if (body["refreshThrottleMs"] is JsonValue b) d.RefreshThrottleMs = b.GetValue<int>();
            if (body["autoReconnect"] is JsonValue c) d.AutoReconnect = c.GetValue<bool>();
            if (body["reconnectIntervalSec"] is JsonValue e) d.ReconnectIntervalSec = e.GetValue<int>();
            if (body["reconnectGiveUpMin"] is JsonValue f) d.ReconnectGiveUpMin = f.GetValue<int>();
            if (body["rssiWeakThreshold"] is JsonValue g) d.RssiWeakThreshold = g.GetValue<int>();
            if (body["rssiCriticalThreshold"] is JsonValue h) d.RssiCriticalThreshold = h.GetValue<int>();
            if (body["autoConnect"] is JsonValue i) d.AutoConnect = i.GetValue<bool>();
            if (body["autoDetect"] is JsonValue j) d.AutoDetect = j.GetValue<bool>();
            if (body["autoDetectTimeoutSec"] is JsonValue k) d.AutoDetectTimeoutSec = k.GetValue<int>();
            if (App.Config.SaveIfChanged(before)) App.Log.Info(LogText.L("log.hub.devices_saved"));
        }
        var cfg = App.Config.Devices;
        var (done, total, current) = App.Devices.AutoDetectProgress;
        return new
        {
            ok = true,
            continuousScan = cfg.ContinuousScan,
            refreshThrottleMs = cfg.RefreshThrottleMs,
            autoReconnect = cfg.AutoReconnect,
            reconnectIntervalSec = cfg.ReconnectIntervalSec,
            reconnectGiveUpMin = cfg.ReconnectGiveUpMin,
            rssiWeakThreshold = cfg.RssiWeakThreshold,
            rssiCriticalThreshold = cfg.RssiCriticalThreshold,
            autoConnect = cfg.AutoConnect,
            autoDetect = cfg.AutoDetect,
            autoDetectTimeoutSec = cfg.AutoDetectTimeoutSec,
            // Auto-detection runtime state and progress used by frontend button text and hints.
            autoDetecting = App.Devices.AutoDetecting,
            detectDone = done,
            detectTotal = total,
            detectCurrent = current ?? "",
            history = cfg.History,
            lastConnected = cfg.LastConnected,
            aliases = cfg.Aliases,
            // Blocklist management exposed to the frontend for listing and unblocking devices.
            blocked = App.Config.HeartRate.Blocked,
        };
    }

    /// <summary>Automatic detection actions: start, stop, or status.</summary>
    public object AutoDetect(string action)
    {
        HrmTrace.Event("cmd.autodetect", action);
        if (App.SafeMode && action == "start") return new
        {
            ok = false,
            running = false,
            done = 0,
            total = 0,
            current = "",
            candidates = App.Devices.Candidates().Count,
            error = "safe mode blocks external actions",
        };
        switch (action)
        {
            case "start": App.Devices.StartAutoDetect(); break;
            case "stop": App.Devices.StopAutoDetect(); break;
        }
        var (done, total, current) = App.Devices.AutoDetectProgress;
        return new
        {
            ok = true,
            running = App.Devices.AutoDetecting,
            done,
            total,
            current = current ?? "",
            candidates = App.Devices.Candidates().Count,
        };
    }

    // ---- Phase 5: hardware variables ----

    /// <summary>Built-in NTP server presets used directly by the frontend dropdown and not stored in configuration.</summary>
    public static readonly string[] NtpPresets =
    {
        "ntp.aliyun.com",                 // Alibaba Cloud
        "ntp1.aliyun.com",                // Alibaba Cloud backup
        "ntp.tuna.tsinghua.edu.cn",       // Tsinghua University TUNA
        "ntp.ntsc.ac.cn",                 // National Time Service Center
        "cn.pool.ntp.org",                // NTP Pool China
        "time.windows.com",               // Microsoft
        "time.apple.com",                 // Apple
        "pool.ntp.org",                   // Global NTP Pool
    };

    /// <summary>Configure the hardware-variable engine: precision, units, overrides, renames, and custom variables.</summary>
    public object HwConfig(JsonObject? body)
    {
        if (body != null)
        {
            var before = App.Config.SnapshotJson();
            var h = App.Config.Hw;
            if (body["intervalMs"] is JsonValue a) { h.IntervalMs = a.GetValue<int>(); App.SysInfo.UpdateInterval(); }
            if (body["useFloat"] is JsonValue b) h.UseFloat = b.GetValue<bool>();
            if (body["decimals"] is JsonValue c) h.Decimals = c.GetValue<int>();
            if (body["round"] is JsonValue d) h.Round = d.GetValue<bool>();
            if (body["ntpServer"] is JsonValue e) h.NtpServer = e.GetValue<string>();
            if (App.Config.SaveIfChanged(before)) App.Log.Info(LogText.L("log.hub.hw_saved"));
        }
        var cfg = App.Config.Hw;
        return new
        {
            ok = true,
            intervalMs = cfg.IntervalMs,
            useFloat = cfg.UseFloat,
            decimals = cfg.Decimals,
            round = cfg.Round,
            ntpServer = cfg.NtpServer,
            ntpPresets = NtpPresets,
            units = cfg.Units,
            overrides = cfg.Overrides,
            renames = cfg.Renames,
            custom = cfg.Custom,
        };
    }

    /// <summary>Variable actions: override, unit, rename, custom, or remove.</summary>
    public object HwVar(string action, string name, string? value, JsonObject? item)
    {
        HrmTrace.Event("cmd.hw_var", $"{action}:{name}");
        switch (action)
        {
            case "override":
                VarEngine.SetOverride(name, value);
                break;
            case "unit":
                VarEngine.SetUnit(name, value);
                break;
            case "rename":
                VarEngine.SetRename(name, value);
                break;
            case "custom" when item != null:
                VarEngine.UpsertCustom(new AppConfig.CustomVar
                {
                    Name = item["name"]?.GetValue<string>() ?? name,
                    Kind = item["kind"]?.GetValue<string>() ?? "expr",
                    Expr = item["expr"]?.GetValue<string>() ?? "",
                    Pattern = item["pattern"]?.GetValue<string>() ?? "",
                    Unit = item["unit"]?.GetValue<string>() ?? "",
                    Enabled = item["enabled"]?.GetValue<bool>() ?? true,
                });
                break;
            case "remove":
                VarEngine.RemoveCustom(name);
                break;
            default:
                return new { ok = false, error = $"unknown hw_var action: {action}" };
        }
        // Recompute immediately so the frontend sees the result at once.
        VarEngine.PostProcess(App.SysInfo.Vars);
        return new { ok = true, vars = App.SysInfo.Vars.ToDictionary(kv => kv.Key, kv => kv.Value) };
    }

    public object Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return new { ok = true };

        HrmTrace.Event("cmd.shutdown");
        App.Log.Info(LogText.L("log.hub.frontend_exit"));
        ProcessInfo.StopCrashWatch();

        var exitThread = new Thread(() =>
        {
            Thread.Sleep(200);
            var watchdog = new Thread(() =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(8));
                try { ProcessInfo.KillOwnedProcesses(); } catch { }
                ProcessInfo.ForceExitCurrentProcess();
                Environment.FailFast("shutdown deadline exceeded");
            })
            {
                IsBackground = true,
                Name = "HRM shutdown watchdog",
            };
            watchdog.Start();
            try
            {
                UiInvoke(() =>
                {
                    Application.Exit();
                    ProcessInfo.ShutdownFrontend(App.Web?.Running == true);
                    Environment.Exit(0);
                });
            }
            catch
            {
                try { ProcessInfo.ShutdownFrontend(App.Web?.Running == true, 300); } catch { }
                Environment.Exit(0);
            }
        })
        {
            IsBackground = true,
            Name = "HRM shutdown dispatcher",
        };
        exitThread.Start();
        return new { ok = true };
    }

    /// <summary>
    /// Activate this instance interface when a second process sends the single-instance command.
    /// Bring an existing frontend window forward. A shell process still creating its window also counts as an existing frontend;
    /// in that case, start only the Web service while the shell retries, avoiding repeated shell launches.
    /// </summary>
    public object Activate()
    {
        HrmTrace.Event("cmd.activate");
        if (ProcessInfo.ActivateExistingUi()) return new { ok = true };
        // The shell may be minimized to the tray with no visible window, so ActivateExistingUi cannot find it.
        // Launch once more; the shell single-instance guard forwards the wake-up signal to the running process.
        ProcessInfo.LaunchFrontend();
        return new { ok = true };
    }

    // ---- Phase 6: API Server ----

    /// <summary>External API configuration, or read it when body is null. An empty token disables authentication, leaving access restricted only by loopback binding.</summary>
    public object ApiConfig(JsonObject? body)
    {
        var a = App.Config.Api;
        if (body != null && (body.ContainsKey("enabled") || body.ContainsKey("token") || body.ContainsKey("pushSysInfo")
            || body.ContainsKey("pushIntervalMs") || body.ContainsKey("sysInfoVars") || body.ContainsKey("webhookThrottleMs")))
        {
            var before = App.Config.SnapshotJson();
            if (body["enabled"] is JsonValue v1) a.Enabled = v1.GetValue<bool>();
            if (body["token"] is JsonValue v2) a.Token = v2.GetValue<string>();
            if (body["pushSysInfo"] is JsonValue v3) a.PushSysInfo = v3.GetValue<bool>();
            if (body["pushIntervalMs"] is JsonValue v4) a.PushIntervalMs = v4.GetValue<int>(); // Upper bound removed; the timer consumer applies its own minimum clamp.
            if (body["webhookThrottleMs"] is JsonValue v5) a.WebhookThrottleMs = v5.GetValue<int>(); // Likewise, the sender enforces the throttle minimum.
            if (body["sysInfoVars"] is JsonArray arr)
                a.SysInfoVars = arr.Select(x => x?.GetValue<string>() ?? "").Where(s => s.Length > 0).Distinct().ToList();
            if (App.Config.SaveIfChanged(before)) App.Log.Info(LogText.L("log.hub.api_saved"));
            ApplyPushTimer();
        }
        return new
        {
            ok = true,
            enabled = a.Enabled,
            token = a.Token,
            pushSysInfo = a.PushSysInfo,
            pushIntervalMs = a.PushIntervalMs,
            sysInfoVars = a.SysInfoVars,
            webhookThrottleMs = a.WebhookThrottleMs,
            varNames = App.SysInfo.Vars.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    /// <summary>/heartbeat payload containing heart rate, devices, health, and system information for external polling.</summary>
    public object Heartbeat()
    {
        var devices = App.Ble.ConnectedDevices();
        return new
        {
            ok = true,
            bpm = App.CurrentBpm,
            avg = App.Ble.AverageBpm(),
            connectedCount = devices.Count,
            devices = devices.Select(d => new
            {
                mac = d.Mac,
                name = DeviceRegistry.DisplayName(d.Mac, d.Name),
                bpm = d.Bpm,
            }).ToArray(),
            health = App.Health.Snapshot(),
            recording = HrmDb.Recording,
            app = new { version = App.Version, startTime = $"{App.StartTime:O}" },
            sysInfo = SysInfoPayload(),
            ts = $"{DateTime.Now:O}",
        };
    }

    /// <summary>Retrieve system information through template rendering and a variable snapshot for external callers and inbound WebSocket commands.</summary>
    public object SysInfoQuery(string template)
        => new { ok = true, text = template.Length > 0 ? App.SysInfo.FormatTemplate(template) : "", sysInfo = SysInfoPayload() };

    /// <summary>Unified command dispatch shared by IPC pipes and inbound WebSocket commands.
    /// P6: <paramref name="admin"/> false (remote user-role WS session) keeps token-bearing payloads unreachable.</summary>
    public object Dispatch(string cmd, JsonObject? req, bool admin = true)
    {
        // Treat non-string fields as empty strings so a type mismatch does not turn the whole command into an error response.
        string S(string key) => req?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s ?? "" : "";
        int I(string key, int def) => req?[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : def;
        bool B(string key) => req?[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        List<string> A(string key) =>
            (req?[key] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList() ?? new();
        switch (cmd)
        {
            case "status": return Status();
            case "devices": return Devices();
            case "scan": return Scan(S("action"));
            case "connect": return Connect(S("mac"));
            case "disconnect": return Disconnect(S("mac"));
            case "save": return Save(S("mac"), B("saved"));
            case "block": return Block(S("mac"));
            case "unblock": return Unblock(S("mac"));
            case "batch":
            {
                var macs = (req?["macs"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").Where(m => m.Length > 0).ToList() ?? new();
                return Batch(S("action"), macs);
            }
            case "osc_connect": return OscConnect(B("connected"));
            case "osc_config": return OscConfig(req);
            case "osc_test":
                return OscTest(
                    S("ip") is { Length: > 0 } ip ? ip : App.Config.Osc.Ip,
                    S("port") is { Length: > 0 } pt ? pt : App.Config.Osc.Port,
                    S("address") is { Length: > 0 } ad ? ad : "/test/echo",
                    S("text") is { Length: > 0 } tx ? tx : "hello");
            case "osc_params": return OscParams();
            case "osc_params_clear": return OscParamsClear();
            case "cli": return Cli(S("line"));
            case "cli_help": return new { commands = CommandShell.Commands.Select(c => new { name = c.Name, usage = c.Usage, desc = Txt.T(c.Desc) }).ToArray() };
            case "monitor": return Monitor(I("hours", 24), I("buckets", 12));
            case "monitor_export": return MonitorExport(I("hours", 24), I("buckets", 12), S("format"));
            case "hw": return Hw();
            case "hw_refresh": return HwRefresh();
            case "logs": return Logs(S("filter"), Math.Clamp(I("limit", 800), 1, 5000), B("regex"), A("levels"));
            case "logs_clear": return LogsClear();
            case "logs_dump": return LogsDump();
            case "logs_export": return LogsExport(S("filter"), S("format"), B("regex"), A("levels"));
            case "webhooks": return Webhooks();
            case "webhook": return WebhookAction(S("action"), req?["item"] as JsonObject, I("index", -1));
            case "settings": return Settings(req);
            case "config": return Config();
            case "record": return Record(S("action"));
            case "record_config": return RecordingConfig(req);
            case "health": return Health(S("action"));
            case "health_config": return HealthConfig(req);
            case "export": return Export(S("table"), S("format"), I("limit", 5000));
            case "rename": return Rename(S("mac"), S("alias"));
            case "devices_config": return DevicesConfig(req);
            case "autodetect": return AutoDetect(S("action"));
            case "hw_config": return HwConfig(req);
            case "hw_var":
            {
                // value may be null to remove an override, unit, or rename.
                var hasValue = req?["value"] is JsonValue;
                return HwVar(S("action"), S("name"), hasValue ? S("value") : null, req?["item"] as JsonObject);
            }
            // Use win for the window identifier because id is reserved by the protocol as the request sequence number.
            case "float_open": return FloatOpen(S("win") is { Length: > 0 } f ? f : null);
            case "float_open_all": return FloatOpenAll();
            case "float_close_all": return FloatCloseAll();
            case "float_close": return FloatClose(S("win"));
            case "float_lock": return FloatLock(B("locked"));
            case "float_config": return FloatConfig(req);
            case "api_config":
                // P6: the API token is a host secret — remote user-role sessions never receive it.
                if (!admin) return new { ok = false, error = "forbidden" };
                return ApiConfig(req);
            case "heartbeat": return Heartbeat();
            case "sysinfo": return SysInfoQuery(S("template"));
            case "web_start": return WebStart();
            case "web_stop": return WebStop();
            case "activate": return Activate();
            case "shutdown": return Shutdown();
            default: return new { ok = false, error = $"unknown cmd: {cmd}" };
        }
    }

    // ---- On-demand Web lifecycle injected by the host and controlled manually by the frontend----

    /// <summary>Start the Web UI, or open the browser when already running. Injected by Program.</summary>
    public Action? WebStartHook;

    /// <summary>Stop the Web UI. Injected by Program.</summary>
    public Action? WebStopHook;

    /// <summary>Whether the Web UI is running. Injected by Program.</summary>
    public Func<bool>? WebRunningHook;

    /// <summary>Web UI port. Injected by Program.</summary>
    public Func<int>? WebPortHook;

    public Func<string>? WebSchemeHook;

    public object WebStart()
    {
        HrmTrace.Event("cmd.web_start");
        WebStartHook?.Invoke();
        return new { ok = true };
    }

    public object WebStop()
    {
        HrmTrace.Event("cmd.web_stop");
        WebStopHook?.Invoke();
        return new { ok = true };
    }

    private object WebJson() => new
    {
        running = WebRunningHook?.Invoke() ?? false,
        port = WebPortHook?.Invoke() ?? 0,
        scheme = WebSchemeHook?.Invoke() ?? App.WebScheme,
    };

    /// <summary>Local frontend settings for language, theme, custom colors, corner radius, and density.</summary>
    private static object UiJson() => new
    {
        lang = App.Config.Ui.Lang,
        theme = App.Config.Ui.Theme,
        mode = App.Config.Ui.Mode,
        palette = App.Config.Ui.Palette,
        primary = App.Config.Ui.Primary,
        accent = App.Config.Ui.Accent,
        solid = App.Config.Ui.Solid,
        bg = App.Config.Ui.Bg,
        panel = App.Config.Ui.Panel,
        fontFamily = App.Config.Ui.FontFamily,
        monoFontFamily = App.Config.Ui.MonoFontFamily,
        cornerRadius = App.Config.Ui.CornerRadius,
        density = App.Config.Ui.Density,
        animations = App.Config.Ui.Animations,
        closeAction = App.Config.Ui.CloseAction,
        brand = App.Config.Ui.Brand,
    };

    // ------------------------------------------------------------------ Data assembly

    private object StatusSnapshot()
    {
        var macs = AllMacs();
        return new
        {
            bpm = App.CurrentBpm,
            avg = App.Ble.AverageBpm(),
            connectedCount = App.Ble.ConnectedDevices().Count,
            scanning = App.Ble.Scanning,
            devices = macs.Select(DeviceJson).ToArray(),
            osc = OscJson(),
            hr = HrJson(),
            webhookEnabled = App.Config.Webhook.Enabled,
            app = new { debug = App.DebugMode, version = App.Version, startTime = $"{App.StartTime:O}", baseDir = App.BaseDir, componentsOk = App.ComponentsOk, updateCheck = App.Config.App.UpdateCheck },
            // Release manifest for item #23: repository, version, build time, and license used by the About page.
            release = ReleaseManifest.Json(),
            safeMode = App.SafeMode,
            floatCount = FloatWindowHost.Count,
            floatMain = FloatWindowHost.IsOpen(FloatWindowHost.MainId),
            floatIds = FloatWindowHost.OpenIds,
            health = App.Health.Snapshot(),
            recording = HrmDb.Recording,
        };
    }

    private object DeviceJson(string mac)
    {
        var dev = App.Ble.Get(mac);
        _known.TryGetValue(mac, out var k);
        var e = App.Devices.Get(mac);
        // Identifier precedence: advertised name cached by the registry, BLE state name, scan snapshot name, then MAC.
        // Observe updates the registry only for non-empty advertised names, making it the most reliable source.
        var raw = FirstNamed(mac, e?.Name, dev?.Name, k.name) ?? mac;
        var display = DeviceRegistry.DisplayName(mac, raw);
        var connected = dev?.Connected ?? false;
        // RSSI is available only from advertisements during scanning; Windows stops delivering them after connection,
        // and WinRT reports the -127 sentinel. Treat it as unavailable, displayed as an em dash, rather than presenting a stale value as live.
        var hasRssi = e?.HasRssi ?? false;
        var rssi = hasRssi ? e!.Rssi : 0;
        return new
        {
            mac,
            name = display,
            rawName = raw,
            // Tokenize the name and mark brand/model keyword segments with hit=true for bold italic rendering.
            nameSegs = DeviceRegistry.Segments(display).Select(s => new { text = s.Text, hit = s.Hit }).ToArray(),
            alias = App.Config.Devices.Aliases.GetValueOrDefault(mac, ""),
            type = e?.Type ?? k.type ?? dev?.Type ?? "BLE Device",
            rssi,
            // Valid-RSSI flag; when false, the frontend hides the value because advertising strength is unavailable after connection.
            hasRssi,
            connected,
            // Connecting state after an attempt starts but before a result arrives; the frontend displays a solid indicator.
            connecting = App.Ble.IsConnecting(mac),
            // Signal level unknown, ok, weak, or critical controls gray, green, orange, or red display for connected devices.
            signal = DeviceRegistry.SignalLevel(rssi, hasRssi),
            // Device category: hr, audio, home, or generic; the frontend selects the icon accordingly.
            category = App.Devices.Category(mac),
            // Heart-rate icon marker: a heart-rate service or at least two wearable keywords overrides other categories.
            hrMarked = App.Devices.HrMarked(mac),
            noHrChar = e?.NoHrChar ?? false,
            bpm = dev?.Bpm ?? 0,
            saved = IsSaved(mac),
            // Advertisement frequency while scanning.
            advHz = Math.Round(e?.AdvHz ?? 0, 2),
            // Notification frequency while connected.
            notifyHz = connected ? Math.Round(e?.NotifyHz ?? 0, 2) : 0,
            reports = e?.Reports ?? 0,
            reconnecting = App.Devices.IsReconnecting(mac),
            score = App.Devices.Score(mac),
            // Ranking details as name-to-score entries for the frontend tooltip.
            scoreParts = App.Devices.Explain(mac).Select(p => new { name = p.Key, value = p.Value }).ToArray(),
        };
    }

    /// <summary>Return the first valid name, skipping empty, UNKNOWN, and MAC placeholder values.</summary>
    private static string? FirstNamed(string mac, params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var n = c.Trim();
            if (n == "UNKNOWN" || n.Equals(mac, StringComparison.OrdinalIgnoreCase)) continue;
            return n;
        }
        return null;
    }

    private static object OscJson() => new
    {
        connected = App.Osc.Connected,
        sent = App.Osc.SentCount,
        fail = App.Osc.FailCount,
        recv = App.Osc.RecvCount,
        ip = App.Config.Osc.Ip,
        port = App.Config.Osc.Port,
        address = App.Config.Osc.Address,
        intervalMs = App.Config.Osc.IntervalMs,
        receivePort = App.Config.Osc.ReceivePort,
        autoStart = App.Config.Osc.AutoStart,
        template = App.Config.Osc.Template,
    };

    private static object HrJson() => new
    {
        displaySource = App.Config.HeartRate.DisplaySource,
        window = new
        {
            visible = App.Config.HeartRate.Window.Visible,
            locked = App.Config.HeartRate.Window.Locked,
            geometry = App.Config.HeartRate.Window.Geometry,
            unlockedColor = App.Config.HeartRate.Window.UnlockedColor,
            lockedColor = App.Config.HeartRate.Window.LockedColor,
            format = App.Config.HeartRate.Window.Format,
            imagePath = App.Config.HeartRate.Window.ImagePath,
            source = App.Config.HeartRate.Window.Source,
            refreshMs = App.Config.HeartRate.Window.RefreshMs,
            sources = App.Config.HeartRate.Window.Sources,
        }
    };

    /// <summary>Sort by the combined name-priority and signal-strength score from DeviceRegistry.Score; blocked devices never appear.</summary>
    private string[] AllMacs() => _known.Keys
        .Concat(App.Ble.AllDevices().Keys)
        .Concat(App.Devices.Macs)
        .Where(m => !IsBlocked(m))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(m => App.Devices.Score(m))
        .ThenBy(m => m, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private void BroadcastDevices()
    {
        var macs = AllMacs();
        Push("devices", macs.Select(DeviceJson).ToArray());
    }

    // ------------------------------------------------------------------ Business logic

    private static bool IsSaved(string mac) => App.Config.HeartRate.Devices.Contains(mac, StringComparer.OrdinalIgnoreCase);
    private static bool IsBlocked(string mac) => App.Config.HeartRate.Blocked.Contains(mac, StringComparer.OrdinalIgnoreCase);

    private void BlockDevice(string mac)
    {
        if (!IsBlocked(mac)) App.Config.HeartRate.Blocked.Add(mac);
        // Blocking and saving are mutually exclusive; remove the device from saved entries to avoid a saved-and-blocked state.
        App.Config.HeartRate.Devices.RemoveAll(m => m.Equals(mac, StringComparison.OrdinalIgnoreCase));
        // Block and Batch persist together through SaveIfChanged; this method changes memory only.
        App.Devices.CancelReconnect(mac);
        _known.TryRemove(mac, out _);
        try { App.Ble.Disconnect(mac); } catch { }
        App.Log.Info(LogText.L("log.hub.device_blocked", mac));
        BroadcastDevices();
    }

    private void UnblockDevice(string mac)
    {
        App.Config.HeartRate.Blocked.RemoveAll(m => m.Equals(mac, StringComparison.OrdinalIgnoreCase));
        // Unblock and Batch persist together through SaveIfChanged; this method changes memory only.
        App.Log.Info(LogText.L("log.hub.device_unblocked", mac));
        BroadcastDevices();
    }

    private static void ApplyOsc(JsonObject? body)
    {
        if (body == null) return;
        if (body["ip"] is JsonValue v1) App.Config.Osc.Ip = v1.GetValue<string>();
        if (body["port"] is JsonValue v2) App.Config.Osc.Port = v2.GetValue<string>();
        if (body["address"] is JsonValue v3) App.Config.Osc.Address = v3.GetValue<string>();
        if (body["intervalMs"] is JsonValue v4) App.Config.Osc.IntervalMs = v4.GetValue<string>();
        if (body["receivePort"] is JsonValue v5) App.Config.Osc.ReceivePort = v5.GetValue<string>();
        if (body["template"] is JsonValue v6) App.Config.Osc.Template = v6.GetValue<string>();
        if (body["autoStart"] is JsonValue v7) App.Config.Osc.AutoStart = v7.GetValue<bool>();
        App.Osc.UpdateInterval();
    }

    private static void ApplySettings(JsonObject? body)
    {
        if (body == null) return;
        var before = App.Config.SnapshotJson();
        if (body["osc"] is JsonObject osc) ApplyOsc(osc);
        if (body["webhookEnabled"] is JsonValue we) App.Config.Webhook.Enabled = we.GetValue<bool>();
        if (body["logs"] is JsonObject logs)
        {
            if (logs["autoDumpEnabled"] is JsonValue ad) App.Config.Logs.AutoDumpEnabled = ad.GetValue<bool>();
            if (logs["autoDumpIntervalMin"] is JsonValue ai) App.Config.Logs.AutoDumpIntervalMin = ai.GetValue<string>();
            // Write only the trace configuration, not App.TraceEnabled; it takes effect after restart.
            if (logs["traceEnabled"] is JsonValue tr) App.Config.Logs.TraceEnabled = tr.GetValue<bool>();
        }
        if (body["hr"] is JsonObject hr)
        {
            if (hr["displaySource"] is JsonValue ds)
            {
                App.Config.HeartRate.DisplaySource = ds.GetValue<string>();
                App.Config.HeartRate.Window.Source = App.Config.HeartRate.DisplaySource;
            }
            if (hr["window"] is JsonObject win)
            {
                if (win["format"] is JsonValue f) App.Config.HeartRate.Window.Format = f.GetValue<string>();
                if (win["unlockedColor"] is JsonValue uc) App.Config.HeartRate.Window.UnlockedColor = uc.GetValue<string>();
                if (win["lockedColor"] is JsonValue lc) App.Config.HeartRate.Window.LockedColor = lc.GetValue<string>();
                if (win["imagePath"] is JsonValue ip) App.Config.HeartRate.Window.ImagePath = ip.GetValue<string>();
                if (win["geometry"] is JsonValue g) App.Config.HeartRate.Window.Geometry = g.GetValue<string>();
                if (win["refreshMs"] is JsonValue rm) App.Config.HeartRate.Window.RefreshMs = rm.GetValue<int>(); // No upper bound; the throttling consumer already applies protection.
            }
        }
        if (body["ui"] is JsonObject ui)
        {
            if (ui["lang"] is JsonValue ul)
            {
                var v = ul.GetValue<string>()?.Trim().ToLowerInvariant() ?? "";
                App.Config.Ui.Lang = v;
                // Engine-side console and CLI messages follow config.ui.lang immediately.
                if (v is "zh-tw" or "zh-hk" or "yue-hk" or "zh-cn" or "en" or "ja" or "es" or "ko" or "de" or "fr") App.Lang = v;
            }
            if (ui["theme"] is JsonValue ut) App.Config.Ui.Theme = ut.GetValue<string>();
            if (ui["mode"] is JsonValue um) App.Config.Ui.Mode = um.GetValue<string>();
            if (ui["palette"] is JsonValue upl) App.Config.Ui.Palette = upl.GetValue<string>();
            if (ui["primary"] is JsonValue upr) App.Config.Ui.Primary = upr.GetValue<string>();
            if (ui["accent"] is JsonValue ua) App.Config.Ui.Accent = ua.GetValue<string>();
            if (ui["solid"] is JsonValue us) App.Config.Ui.Solid = us.GetValue<bool>();
            if (ui["bg"] is JsonValue ub) App.Config.Ui.Bg = ub.GetValue<string>();
            if (ui["panel"] is JsonValue up) App.Config.Ui.Panel = up.GetValue<string>();
            if (ui["fontFamily"] is JsonValue uff) App.Config.Ui.FontFamily = uff.GetValue<string>() ?? "";
            if (ui["monoFontFamily"] is JsonValue umf) App.Config.Ui.MonoFontFamily = umf.GetValue<string>() ?? "";
            if (ui["cornerRadius"] is JsonValue ucr) App.Config.Ui.CornerRadius = ucr.GetValue<int>();
            if (ui["density"] is JsonValue ud) App.Config.Ui.Density = ud.GetValue<double>();
            if (ui["animations"] is JsonValue uan) App.Config.Ui.Animations = uan.GetValue<bool>();
            if (ui["closeAction"] is JsonValue uca)
            {
                var v = uca.GetValue<string>();
                if (v is "ask" or "exit" or "tray") App.Config.Ui.CloseAction = v;
            }
            if (ui["brand"] is JsonValue ubrand) App.Config.Ui.Brand = ubrand.GetValue<string>() ?? "";
        }
        if (body["app"] is JsonObject appSec)
        {
            // Debug mode takes effect immediately for logging and frontend diagnostics and is persisted; the console window still requires a restart.
            if (appSec["debug"] is JsonValue ad)
            {
                App.DebugMode = ad.GetValue<bool>();
                App.Config.App.Debug = App.DebugMode ? 1 : 0;
                App.Log.Info(LogText.L(App.DebugMode ? "log.hub.debug_on" : "log.hub.debug_off"));
            }
            // P3: startup update-check toggle; the change applies from the next background refresh on.
            if (appSec["updateCheck"] is JsonValue uc) App.Config.App.UpdateCheck = uc.GetValue<bool>();
        }
        if (App.Config.SaveIfChanged(before)) App.Log.Info(LogText.L("log.hub.settings_saved"));
        App.Osc.UpdateInterval();
        UiInvoke(FloatWindowHost.ApplyStyle);
    }

    private static object HandleWebhook(string action, JsonObject? item, int index)
    {
        if (App.SafeMode && action == "test") return new { ok = false, error = "safe mode blocks external actions" };
        switch (action)
        {
            case "add" when item != null:
            {
                var el = item.Deserialize<JsonElement>();
                App.Webhooks.Webhooks.Add(el);
                App.Webhooks.Save();
                return new { ok = true };
            }
            case "update" when item != null:
            {
                var el = item.Deserialize<JsonElement>();
                if (index >= 0 && index < App.Webhooks.Webhooks.Count)
                {
                    App.Webhooks.Webhooks[index] = el;
                    App.Webhooks.Save();
                    return new { ok = true };
                }
                return new { ok = false, error = "index out of range" };
            }
            case "delete":
            {
                if (index >= 0 && index < App.Webhooks.Webhooks.Count)
                {
                    App.Webhooks.Webhooks.RemoveAt(index);
                    App.Webhooks.Save();
                    return new { ok = true };
                }
                return new { ok = false, error = "index out of range" };
            }
            case "test" when item != null:
            {
                var el = item.Deserialize<JsonElement>();
                var (ok, status, response, err) = App.Webhooks.Test(el);
                return new { ok, status, response, error = err };
            }
            default:
                return new { ok = false, error = "unknown webhook action" };
        }
    }
}
