using System.Diagnostics;
using System.Text.Json;
using HeartRateMonitor.Ble;
using HeartRateMonitor.Cli;

namespace HeartRateMonitor.Core;

/// <summary>
/// Text-command core for Phase 9, shared by the CLI REPL (<c>CliApp</c>),
/// the local frontend Console tab through the IPC <c>cli</c> command, and the web terminal.
/// Returns lines of text without touching the console or UI, so it may run on any thread.
/// Configuration commands, implemented partly in CommandShellApp.cs, call matching <see cref="AppHub"/> methods
/// whenever possible to share validation, persistence, and logging.
/// User-facing text is resolved through <see cref="Txt.T"/> according to App.Lang.
/// </summary>
public static partial class CommandShell
{
    /// <summary>Command name and one-line description shared by help and frontend completion; Desc stores a key resolved through Txt.T.</summary>
    public static readonly (string Name, string Usage, string Desc)[] Commands =
    {
        ("help",       "help | ?",                    "dsc.help"),
        ("status",     "status",                      "dsc.status"),
        ("about",      "about",                       "dsc.about"),
        ("devices",    "devices",                     "dsc.devices"),
        ("score",      "score [mac]",                 "dsc.score"),
        ("scan",       "scan [on|off|start|stop|<sec>]", "dsc.scan"),
        ("detect",     "detect [start|stop]",         "dsc.detect"),
        ("connect",    "connect <mac>",               "dsc.connect"),
        ("disconnect", "disconnect <mac>",            "dsc.disconnect"),
        ("save",       "save <mac> [on|off]",         "dsc.save"),
        ("block",      "block <mac>",                 "dsc.block"),
        ("rename",     "rename <mac> [alias]",        "dsc.rename"),
        ("devcfg",     "devcfg [key value]",          "dsc.devcfg"),
        ("bpm",        "bpm",                         "dsc.bpm"),
        ("health",     "health [calibrate|cancel]",   "dsc.health"),
        ("hcfg",       "hcfg [key value]",            "dsc.hcfg"),
        ("record",     "record [start|stop|get]",     "dsc.record"),
        ("export",     "export <table> <format> [rows]", "dsc.export"),
        ("monitor",    "monitor [hours] [export <format>]", "dsc.monitor"),
        ("hw",         "hw [filter]",                 "dsc.hw"),
        ("info",       "info [fast|full]",            "dsc.info"),
        ("var",        "var <name> [value]",          "dsc.var"),
        ("hwcfg",      "hwcfg [key value]",           "dsc.hwcfg"),
        ("sysinfo",    "sysinfo <template>",          "dsc.sysinfo"),
        ("osc",        "osc [on|off]",                "dsc.osc"),
        ("oscfg",      "oscfg [key value]",           "dsc.oscfg"),
        ("params",     "params [clear]",              "dsc.params"),
        ("send",       "send <address> <text>",       "dsc.send"),
        ("webhook",    "webhook [list|on|off|test <n>]", "dsc.webhook"),
        ("api",        "api [on|off|token <value>|push on|off|interval ms|throttle ms]", "dsc.api"),
        ("web",        "web [status|start|ui|stop]",  "dsc.web"),
        ("remote",     "remote [status|on|off|wan|idle|users|sessions|kick|deluser|role|tabs]", "dsc.remote"),
        ("autostart",  "autostart [on|off] [task|run|startup] [silent]", "dsc.autostart"),
        ("ui",         "ui [key value]",              "dsc.ui"),
        ("float",      "float [open|close|lock|unlock]", "dsc.float"),
        ("logs",       "logs [rows|clear|dump|export <format>] [keyword]", "dsc.logs"),
        ("logcfg",     "logcfg [key value]",          "dsc.logcfg"),
        ("config",     "config",                      "dsc.config"),
        ("selftest",   "selftest",                    "dsc.selftest"),
        ("crash",      "crash <exit1|null|kill>",     "dsc.crash"),
        ("reboot",     "reboot",                      "dsc.reboot"),
        ("clear",      "clear",                       "dsc.clear"),
        ("exit",       "exit | quit",                 "dsc.exit"),
    };

    /// <summary>Execute pre-tokenized command arguments without losing argument boundaries.</summary>
    public static List<string> Execute(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return new();
        var cmd = args[0].ToLowerInvariant();
        var a = args.Skip(1).ToArray();
        return Execute(cmd, a);
    }

    /// <summary>Execute one command line and return output lines without throwing exceptions.</summary>
    public static List<string> Execute(string line)
    {
        var parts = (line ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return new();
        return Execute(parts[0].ToLowerInvariant(), parts.Skip(1).ToArray());
    }

    static List<string> Execute(string cmd, string[] a)
    {
        // The CLI REPL intercepts exit and quit earlier; without --force they only leave the REPL.
        // GUI and web consoles reaching this point receive a prompt rather than terminating the application accidentally.
        // Only --force exits the entire application, from either a local or remote administrator console.
        if (cmd is "exit" or "quit")
        {
            var force = a.Any(x => x.Equals("--force", StringComparison.OrdinalIgnoreCase));
            if (!force)
                return new() { App.GuiHosted ? Txt.T("exit.gui") : Txt.T("exit.repl") };
            _ = Task.Run(async () =>
            {
                await Task.Delay(300); // Allow the HTTP response to be sent first.
                try { App.Config.Save(); } catch { }
                try { App.Log.Info(LogText.L("log.shell.exit_force")); } catch { }
                ProcessInfo.StopCrashWatch();
                AppHub.UiInvoke(() => { try { Application.Exit(); } catch { } });
                Environment.Exit(0);
            });
            return new() { Txt.T("exit.force") };
        }

        try { return Dispatch(cmd, a); }
        catch (Exception e) { return new() { Txt.T("err.prefix", e.Message) }; }
    }

    static List<string> Dispatch(string cmd, string[] a) => cmd switch
    {
        "help" or "?" => Help(),
        "status" => Status(),
        "about" => About(),
        "devices" => DevicesCmd(),
        "score" => ScoreCmd(a),
        "scan" => ScanCmd(a),
        "detect" => DetectCmd(a),
        "connect" => ConnectCmd(a),
        "disconnect" => DisconnectCmd(a),
        "save" => SaveCmd(a),
        "block" => BlockCmd(a),
        "rename" => RenameCmd(a),
        "devcfg" => DevCfgCmd(a),
        "bpm" => new() { Txt.T("cmd.bpm", App.CurrentBpm, App.Ble.AverageBpm()) },
        "health" => HealthCmd(a),
        "hcfg" => HealthCfgCmd(a),
        "record" => RecordCmd(a),
        "export" => ExportCmd(a),
        "monitor" => MonitorCmd(a),
        "hw" => HwCmd(a),
        "info" => InfoCmd(a),
        "var" => VarCmd(a),
        "hwcfg" => HwCfgCmd(a),
        "sysinfo" => SysInfoCmd(a),
        "osc" => OscCmd(a),
        "oscfg" => OscCfgCmd(a),
        "params" => ParamsCmd(a),
        "send" => SendCmd(a),
        "webhook" => WebhookCmd(a),
        "api" => ApiCmd(a),
        "web" => WebCmd(a),
        "remote" => RemoteCmd(a),
        "autostart" => AutoStartCmd(a),
        "ui" => UiCmd(a),
        "float" => FloatCmd(a),
        "logs" => LogsCmd(a),
        "logcfg" => LogCfgCmd(a),
        "config" => ConfigCmd(),
        "crash" => CrashCmd(a),
        "reboot" => RebootCmd(),
        "clear" => new(),
        _ => new() { Txt.T("cmd.unknown", cmd) },
    };

    /// <summary>Reboot command from round 31 item #27: snapshot connected devices, relaunch with --reboot, and quickly reconnect them.</summary>
    static List<string> RebootCmd()
    {
        if (!App.GuiHosted) return new() { Txt.T("reboot.gui_only") };
        _ = Task.Run(async () =>
        {
            await Task.Delay(300); // Allow the HTTP response to be sent first.
            try { App.Log.Info(LogText.L("log.shell.reboot")); } catch { }
            HeartRateMonitor.Program.RequestReboot();
            await Task.Delay(400);
            try { App.Config.Save(); } catch { }
            ProcessInfo.StopCrashWatch();
            Environment.Exit(0);
        });
        return new() { Txt.T("reboot.ok") };
    }

    /// <summary>Crash tests from round 26: exit1, null, and kill verify the crash-dump and watchdog path.</summary>
    static List<string> CrashCmd(string[] a)
    {
        var mode = a.Length > 0 ? a[0].ToLowerInvariant() : "";
        switch (mode)
        {
            case "exit1":
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300);
                    try { App.Config.Save(); } catch { }
                    try { App.Log.Error(LogText.L("log.crash.start", "exit1")); } catch { }
                    Environment.Exit(1);
                });
                return new() { Txt.T("crash.started", "exit1") };
            case "null":
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300);
                    try { App.Log.Error(LogText.L("log.crash.start", "null")); } catch { }
                    // An unhandled exception flows through AppDomain.UnhandledException, writes crash_dump.txt, and exits with code 1.
                    throw new NullReferenceException("crash test: int* ptr = null (null dereference)");
                });
                return new() { Txt.T("crash.started", "null") };
            case "kill":
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300);
                    try { App.Log.Error(LogText.L("log.crash.start", "kill")); } catch { }
                    Environment.FailFast("crash test: hard kill (FailFast)");
                });
                return new() { Txt.T("crash.started", "kill") };
            default:
                return new() { Txt.T("usage.crash") };
        }
    }

    static List<string> Help()
    {
        var out_ = new List<string> { Txt.T("help.title") };
        var w = Commands.Max(c => c.Usage.Length);
        out_.AddRange(Commands.Select(c => $"  {c.Usage.PadRight(w)}  {Txt.T(c.Desc)}"));
        return out_;
    }

    static List<string> Status() => new()
    {
        Txt.T("status.engine",
            Txt.T(Osc.OscEngine.Available ? "st.loaded" : "st.not_loaded"),
            App.DebugMode ? "DEBUG" : "RELEASE",
            App.Version),
        Txt.T("status.osc",
            Txt.T(App.Osc.Connected ? "st.connected" : "st.not_connected"),
            App.Osc.SentCount, App.Osc.FailCount, App.Osc.RecvCount),
        Txt.T("status.ble",
            Txt.T(App.Ble.AnyConnected() ? "st.conn_devices" : "st.no_conn"),
            Txt.T(App.Ble.Scanning ? "st.scanning" : "st.scan_stopped"),
            App.CurrentBpm),
        Txt.T("status.health",
            App.Health.StatusText,
            Txt.T(HrmDb.Recording ? "st.on" : "st.off"),
            App.SysInfo.Vars.Count),
    };

    static List<string> ConfigCmd() => new()
    {
        Txt.T("cfg.osc", App.Config.Osc.Ip, App.Config.Osc.Port, App.Config.Osc.Address,
            App.Config.Osc.IntervalMs, App.Config.Osc.ReceivePort),
        Txt.T("cfg.hw", App.Config.Hw.IntervalMs, App.Config.Hw.UseFloat, App.Config.Hw.Decimals, App.Config.Hw.NtpServer),
        Txt.T("cfg.devices", App.Config.Devices.ContinuousScan, App.Config.Devices.RefreshThrottleMs, App.Config.Devices.AutoReconnect),
        Txt.T("cfg.api", App.Config.Api.Enabled,
            Txt.T(App.Config.Api.Token.Length > 0 ? "v.set" : "v.empty_no_check"),
            App.Config.Web.Port),
        Txt.T("cfg.dir", App.BaseDir),
    };

    // ------------------------------------------------------------------ Devices

    static List<string> DevicesCmd()
    {
        var macs = App.Ble.AllDevices().Keys
            .Concat(App.Devices.Macs)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(m => App.Devices.Score(m))
            .ToArray();
        if (macs.Length == 0) return new() { Txt.T("dev.none_hint") };
        var out_ = new List<string> { Txt.T("dev.count", macs.Length) };
        foreach (var mac in macs)
        {
            var d = App.Ble.Get(mac);
            var e = App.Devices.Get(mac);
            var name = DeviceRegistry.DisplayName(mac, d?.Name ?? e?.Name);
            var state = d?.Connected == true ? Txt.T("dev.connected_hr", d.Bpm) : Txt.T("st.not_connected");
            // RSSI is valid only while scanning; Windows stops delivering advertisements after connection, so display --.
            var rssi = e is { HasRssi: true } ? $"{e.Rssi,4}" : "  --";
            out_.Add($"  {mac}  {Pad(name, 22)} RSSI={rssi}  {Pad(state, 14)} {Txt.T("dev.score")}={App.Devices.Score(mac)}");
        }
        return out_;
    }

    static List<string> ScoreCmd(string[] a)
    {
        var macs = a.Length > 0
            ? new[] { a[0] }
            : App.Ble.AllDevices().Keys.Concat(App.Devices.Macs)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(m => App.Devices.Score(m)).ToArray();
        if (macs.Length == 0) return new() { Txt.T("dev.none") };
        var out_ = new List<string>();
        foreach (var mac in macs)
        {
            var e = App.Devices.Get(mac);
            var name = DeviceRegistry.DisplayName(mac, App.Ble.Get(mac)?.Name ?? e?.Name);
            var parts = App.Devices.Explain(mac);
            out_.Add($"{name} ({mac})  {Txt.T("score.total", parts.Sum(p => p.Value))}");
            out_.AddRange(parts.Select(p => $"    {Pad(p.Key, 12)} {p.Value,+6}"));
        }
        return out_;
    }

    static List<string> ScanCmd(string[] a)
    {
        var arg = a.FirstOrDefault()?.ToLowerInvariant() ?? (App.Ble.Scanning ? "stop" : "start");
        if (arg is "stop" or "off" or "0")
        {
            App.Ble.StopScan();
            return new() { Txt.T("scan.stopped") };
        }
        // bluetoothctl semantics for item #16: on/start scans continuously until scan off without blocking the REPL,
        // printing discoveries quietly; a numeric argument scans for that many seconds and then stops.
        int? sec = int.TryParse(arg, out var s) && s > 0 ? s : null;
        App.Ble.StartScan(sec);
        return new() { sec == null ? Txt.T("scan.continuous") : Txt.T("scan.sec", sec) };
    }

    static List<string> DetectCmd(string[] a)
    {
        var arg = a.FirstOrDefault()?.ToLowerInvariant() ?? (App.Devices.AutoDetecting ? "stop" : "start");
        if (arg is "stop" or "off" or "0")
        {
            App.Devices.StopAutoDetect();
            return new() { Txt.T("detect.stop_requested") };
        }
        if (App.Devices.AutoDetecting)
        {
            var (done, total, current) = App.Devices.AutoDetectProgress;
            return new() { Txt.T("detect.progress", done, total, current ?? "") };
        }
        var count = App.Devices.Candidates().Count;
        App.Devices.StartAutoDetect();
        return new() { Txt.T("detect.started", count) };
    }

    static List<string> ConnectCmd(string[] a)
    {
        if (a.Length == 0) return Usage("connect <mac>");
        // Do not wait: connection is asynchronous and reports its result through events and logs.
        _ = App.Ble.ConnectAsync(a[0]);
        return new() { Txt.T("connect.connecting", a[0]) };
    }

    static List<string> DisconnectCmd(string[] a)
    {
        if (a.Length == 0) return Usage("disconnect <mac>");
        App.Ble.Disconnect(a[0]);
        return new() { Txt.T("disconnect.done", a[0]) };
    }

    static List<string> RenameCmd(string[] a)
    {
        if (a.Length == 0) return new() { Txt.T("usage.rename") };
        var alias = a.Length > 1 ? string.Join(' ', a[1..]) : "";
        DeviceRegistry.Rename(a[0], alias);
        return new() { alias.Length == 0 ? Txt.T("rename.cleared", a[0]) : Txt.T("rename.done", a[0], alias) };
    }

    // ------------------------------------------------------------------ Health, recording, and export

    static List<string> HealthCmd(string[] a)
    {
        switch (a.FirstOrDefault()?.ToLowerInvariant())
        {
            case "calibrate": App.Health.StartCalibration(); return new() { Txt.T("health.cal_start") };
            case "cancel": App.Health.CancelCalibration(); return new() { Txt.T("health.cal_cancel") };
        }
        var h = App.Health.Snapshot();
        return new() { Txt.T("health.state", App.Health.StatusText), "  " + JsonSerializer.Serialize(h) };
    }

    static List<string> RecordCmd(string[] a)
    {
        var action = a.FirstOrDefault()?.ToLowerInvariant();
        if (action is "start" or "stop")
        {
            var before = App.Config.SnapshotJson();
            HrmDb.Recording = App.Config.Recording.Recording = action == "start";
            App.Config.SaveIfChanged(before);
        }
        return new()
        {
            Txt.T("record.state",
                Txt.T(HrmDb.Recording ? "st.on" : "st.off"),
                HrmDb.Count("hr_records"), HrmDb.Count("osc_records")),
            $"  {HrmDb.PathOf()}",
        };
    }

    static List<string> ExportCmd(string[] a)
    {
        var table = a.ElementAtOrDefault(0) ?? "hr_records";
        var format = a.ElementAtOrDefault(1) ?? "json";
        var limit = int.TryParse(a.ElementAtOrDefault(2), out var l) && l > 0 ? l : 5000;
        var (ok, file, error) = Exporter.Export(table, format, limit);
        return new() { ok ? Txt.T("export.ok", file) : Txt.T("export.fail", error) };
    }

    /// <summary>Summarize a statistics report by reading key fields from JSON, avoiding coupling to MonitorStats anonymous types.</summary>
    static List<string> MonitorCmd(string[] a)
    {
        var hours = int.TryParse(a.ElementAtOrDefault(0), out var h) && h >= 0 ? h : 24;
        var report = MonitorStats.Build(hours, 12);
        // monitor [hours] export [format]
        var exportAt = Array.FindIndex(a, x => x.Equals("export", StringComparison.OrdinalIgnoreCase));
        if (exportAt >= 0)
        {
            var fmt = a.ElementAtOrDefault(exportAt + 1) ?? "json";
            var (ok, file, error) = Exporter.ExportReport(report, fmt);
            return new() { ok ? Txt.T("monitor.exported", file) : Txt.T("export.fail", error) };
        }
        var doc = JsonSerializer.SerializeToDocument(report).RootElement;
        var hr = doc.GetProperty("hr");
        var range = hours == 0 ? Txt.T("mon.hours.all") : Txt.T("mon.hours.recent", hours);
        var out_ = new List<string>
        {
            Txt.T("monitor.range", range, hr.GetProperty("count").GetInt64()),
        };
        if (hr.GetProperty("count").GetInt64() > 0)
        {
            out_.Add(Txt.T("monitor.hr_stats",
                hr.GetProperty("min"), hr.GetProperty("avg"), hr.GetProperty("median"), hr.GetProperty("max"),
                hr.GetProperty("sd")));
            out_.Add(Txt.T("monitor.period", hr.GetProperty("first").GetString(), hr.GetProperty("last").GetString()));
        }
        foreach (var d in doc.GetProperty("devices").EnumerateArray())
            out_.Add(Txt.T("monitor.device",
                Pad(d.GetProperty("name").GetString() ?? "", 22), d.GetProperty("count").GetInt64(),
                d.GetProperty("avg"), d.GetProperty("min"), d.GetProperty("max")));
        foreach (var s in doc.GetProperty("health").GetProperty("items").EnumerateArray())
            out_.Add(Txt.T("monitor.health_row",
                Pad(s.GetProperty("status").GetString() ?? "", 10), s.GetProperty("percent"),
                s.GetProperty("seconds").GetInt64() / 60, s.GetProperty("times").GetInt64()));
        out_.Add(Txt.T("monitor.osc",
            doc.GetProperty("osc").GetProperty("count").GetInt64(),
            doc.GetProperty("osc").GetProperty("top").GetArrayLength()));
        return out_;
    }

    // ------------------------------------------------------------------ Hardware
    static List<string> HwCmd(string[] a)
    {
        var f = a.FirstOrDefault() ?? "";
        var rows = App.SysInfo.Vars
            .Where(kv => f.Length == 0 || kv.Key.Contains(f, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (rows.Length == 0) return new() { Txt.T("hw.none") };
        var w = Math.Min(28, rows.Max(r => r.Key.Length));
        return rows.Select(kv => $"  {Pad(kv.Key, w)} = {kv.Value}").ToList();
    }

    static List<string> InfoCmd(string[] a)
    {
        if ((a.FirstOrDefault() ?? "fast").ToLowerInvariant() == "full")
        {
            App.SysInfo.CollectFull();
            return new() { Txt.T("info.full_done") };
        }
        var sw = Stopwatch.StartNew();
        App.SysInfo.UpdateHeartRateVars();
        sw.Stop();
        var keys = new[] { "CPU_USAGE", "RAM_PERCENT", "GPU_USAGE0", "VRAM_USED0", "UPTIME", "FOCUS_WINDOW_TITLE" };
        var out_ = keys.Select(k => $"  {Pad(k, 20)} = {App.SysInfo.Vars.GetValueOrDefault(k, "-")}").ToList();
        out_.Add(Txt.T("info.fast_done", sw.ElapsedMilliseconds));
        return out_;
    }

    static List<string> VarCmd(string[] a)
    {
        if (a.Length == 0) return new() { Txt.T("usage.var") };
        var name = a[0];
        var value = a.Length > 1 ? string.Join(' ', a[1..]) : "";
        if (value.Length == 0)
        {
            App.Config.Hw.Overrides.Remove(name);
            App.Config.Save();
            return new() { Txt.T("var.cleared", name) };
        }
        App.Config.Hw.Overrides[name] = value;
        App.Config.Save();
        App.SysInfo.UpdateHeartRateVars();
        return new() { Txt.T("var.overridden", name, App.SysInfo.Vars.GetValueOrDefault(name, value)) };
    }

    // ------------------------------------------------------------------ OSC and logs

    static List<string> OscCmd(string[] a)
    {
        switch (a.FirstOrDefault()?.ToLowerInvariant())
        {
            case "on" or "1" or "start": App.Osc.SetConnected(true); return new() { Txt.T("osc.on_done") };
            case "off" or "0" or "stop": App.Osc.SetConnected(false); return new() { Txt.T("osc.off_done") };
        }
        return new() { Txt.T("osc.state", Txt.T(App.Osc.Connected ? "st.on" : "st.off")) };
    }

    static List<string> SendCmd(string[] a)
    {
        if (a.Length < 2) return new() { Txt.T("usage.send") };
        var sw = Stopwatch.StartNew();
        var ok = App.Osc.SendTest(App.Config.Osc.Ip, App.Config.Osc.Port, a[0], string.Join(' ', a[1..]));
        sw.Stop();
        return new() { Txt.T("send.result", Txt.T(ok ? "send.ok" : "send.fail"), a[0], sw.ElapsedMilliseconds) };
    }

    static List<string> LogsCmd(string[] a)
    {
        switch (a.FirstOrDefault()?.ToLowerInvariant())
        {
            case "clear":
                while (App.LogBuffer.TryDequeue(out _)) { }
                return new() { Txt.T("logs.cleared") };
            case "dump":
                return new() { Txt.T("logs.dumped", App.Log.Dump(App.LogBuffer.ToList())) };
            case "export":
            {
                var fmt = a.ElementAtOrDefault(1) ?? "json";
                var kw = a.Length > 2 ? string.Join(' ', a[2..]) : "";
                var lines = App.LogBuffer.ToArray()
                    .Where(x => kw.Length == 0 || x.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var (ok, file, error) = Exporter.ExportLogs(lines, fmt);
                return new() { ok ? Txt.T("logs.exported", lines.Count, file) : Txt.T("export.fail", error) };
            }
        }
        var n = int.TryParse(a.ElementAtOrDefault(0), out var v) && v > 0 ? v : 20;
        var skip = int.TryParse(a.ElementAtOrDefault(0), out _) ? 1 : 0;
        var filter = a.Length > skip ? string.Join(' ', a[skip..]) : "";
        var rows = App.LogBuffer.ToArray()
            .Where(x => filter.Length == 0 || x.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rows.Length == 0) return new() { Txt.T("logs.none") };
        return rows.Skip(Math.Max(0, rows.Length - n)).ToList();
    }

    /// <summary>Pad to a display width, counting CJK characters as two columns, so terminal columns align.</summary>
    static string Pad(string s, int width)
    {
        var w = s.Sum(c => c >= 0x1100 && c <= 0xFFE6 ? 2 : 1);
        return w >= width ? s : s + new string(' ', width - w);
    }
}
