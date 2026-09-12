using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeartRateMonitor.Core;

/// <summary>
/// Configuration and service commands for CommandShell, mapping actions from each frontend tab to commands.
/// Each command calls the corresponding <see cref="AppHub"/> method, sharing the REST entry point so validation,
/// persistence, and logging exactly match the frontend and cannot produce values the frontend would reject.
/// User-facing text is resolved through <see cref="Txt.T"/> according to App.Lang.
/// </summary>
public static partial class CommandShell
{
    /// <summary>Hub used by commands; both GUI and CLI startup assign App.Hub.</summary>
    static AppHub Hub => App.Hub ?? throw new InvalidOperationException("业务核心未初始化");

    /// <summary>Login autostart command (P1): status by default; on/off applies methods and mirrors the settings card.</summary>
    static List<string> AutoStartCmd(string[] a)
    {
        var cfg = App.Config.App;
        var arg = a.Length > 0 ? a[0].ToLowerInvariant() : "";
        if (arg is "on" or "off")
        {
            var silent = a.Any(x => x.Equals("silent", StringComparison.OrdinalIgnoreCase)) ? true
                : a.Any(x => x.Equals("nosilent", StringComparison.OrdinalIgnoreCase)) ? false
                : cfg.AutoStartSilent;
            var keys = a.Length > 1
                ? a[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Array.Empty<string>();
            var parsed = keys.Select(AutoStartManager.ParseMethod).ToList();
            if (parsed.Any(m => m == null)) return new() { Txt.T("autostart.badmethods") };
            var listed = parsed.Where(m => m != null).Select(m => m!.Value).ToList();

            if (arg == "off")
            {
                // off removes the listed methods (every method when no list is given) and leaves the rest alone.
                var remove = listed.Count > 0 ? listed : AutoStartManager.All().ToList();
                string? firstError = null;
                foreach (var m in remove)
                {
                    var err = AutoStartManager.Unregister(m);
                    if (err != null && firstError == null) firstError = $"{AutoStartManager.MethodKey(m)}: {err}";
                }
                var effectiveSilent = firstError == null ? silent : cfg.AutoStartSilent;
                var remaining = AutoStartManager.CurrentMethods(effectiveSilent);
                cfg.AutoStartMethods = remaining.Select(AutoStartManager.MethodKey).ToList();
                cfg.AutoStartSilent = effectiveSilent;
                App.Config.Save();
                if (firstError != null) return new() { Txt.T("err.prefix", firstError) };
                return new() { Txt.T("autostart.applied", string.Join(",", cfg.AutoStartMethods)) };
            }

            // on registers the listed methods (or re-applies the configured set, e.g. after moving the install);
            // unlisted methods are removed so the selection is exactly the desired combination.
            IEnumerable<AutoStartMethod> selected = listed;
            if (listed.Count == 0)
            {
                if (cfg.AutoStartMethods.Count == 0) return new() { Txt.T("autostart.badmethods") };
                selected = cfg.AutoStartMethods
                    .Select(AutoStartManager.ParseMethod)
                    .Where(m => m != null)
                    .Select(m => m!.Value);
            }
            var sel = selected.Distinct().ToList();
            var configuredSilent = cfg.AutoStartSilent;
            var error = AutoStartManager.Apply(sel, silent);
            var actual = AutoStartManager.CurrentMethods(silent);
            if (error != null && actual.Count == 0 && silent != configuredSilent)
            {
                silent = configuredSilent;
                actual = AutoStartManager.CurrentMethods(silent);
            }
            cfg.AutoStartSilent = silent;
            cfg.AutoStartMethods = actual.Select(AutoStartManager.MethodKey).ToList();
            App.Config.Save();
            if (error != null) return new() { Txt.T("err.prefix", error) };
            var names = string.Join(",", sel.Select(AutoStartManager.MethodKey));
            return new() { Txt.T("autostart.applied", names.Length > 0 ? names : "-") };
        }

        // Status: per-method actual registration state plus the silent flag.
        var lines = new List<string> { Txt.T("autostart.silentflag", Txt.T(cfg.AutoStartSilent ? "v.enabled" : "v.disabled")) };
        foreach (var m in AutoStartManager.All())
        {
            var read = AutoStartManager.TryRegisteredTarget(m);
            var line = $"  {AutoStartManager.MethodKey(m),-8} ";
            if (read.Error != null) line += Txt.T("err.prefix", read.Error);
            else if (read.Target == null) line += Txt.T("autostart.unreg");
            else
            {
                line += Txt.T("autostart.reg");
                if (!AutoStartManager.IsCurrent(m, cfg.AutoStartSilent)) line += Txt.T("autostart.staleitem");
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Convert an anonymous hub response into key-value rows without duplicating field lists for every command.</summary>
    static List<string> Rows(object payload, params string[] keys)
    {
        var el = JsonSerializer.SerializeToDocument(payload).RootElement;
        var pick = keys.Length > 0
            ? keys
            : el.EnumerateObject().Select(p => p.Name).Where(n => n != "ok").ToArray();
        var w = pick.Length == 0 ? 0 : pick.Max(k => k.Length);
        var out_ = new List<string>();
        foreach (var k in pick)
        {
            if (!el.TryGetProperty(k, out var v)) continue;
            out_.Add($"  {k.PadRight(w)} = {Scalar(v)}");
        }
        return out_;
    }

    static string Scalar(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Array => $"[{Txt.T("unit.items", v.GetArrayLength())}]",
        JsonValueKind.Object => "{...}",
        _ => v.ToString(),
    };

    static JsonObject Obj(params (string Key, JsonNode? Value)[] fields)
    {
        var o = new JsonObject();
        foreach (var (k, v) in fields) o[k] = v;
        return o;
    }

    static bool OnOff(string? s) => s is "on" or "1" or "true" or "start" or "yes";

    /// <summary>Use the generic prefix for purely syntactic usage lines; natural-language variants use a complete translation key.</summary>
    static List<string> Usage(string usage) => new() { Txt.T("cli.usage", usage) };

    // ---------------------------------------------------------------- Overview

    static List<string> About()
    {
        // Prefer release_version and UTC build time from Release.json, falling back to the assembly version when absent.
        var ver = ReleaseManifest.Version.Length > 0 ? ReleaseManifest.Version : App.Version;
        var build = ReleaseManifest.BuildTimeUtc;
        var lines = new List<string>
        {
            $"HeartRateMonitor · OSC Pusher   v{ver}   {(App.DebugMode ? "DEBUG" : "RELEASE")}"
                + (build.Length > 0 && build != "auto" ? $"   (build {build})" : ""),
            Txt.T("about.start", $"{App.StartTime:yyyy-MM-dd HH:mm:ss}", App.GuiHosted ? "GUI" : "CLI",
                Txt.T(Osc.OscEngine.Available ? "st.loaded" : "st.not_loaded")),
            Txt.T("about.web", App.WebPort, Txt.T(App.Web?.Running == true ? "st.running" : "st.not_started")),
            Txt.T("about.remote", $"http://+:{App.WebPort}/",
                Txt.T(App.Config.Remote.Enabled ? "v.enabled" : "v.disabled")),
            Txt.T("about.dir", App.BaseDir),
        };
        // Show the license and repository only when declared in the manifest.
        if (ReleaseManifest.License.Length > 0)
            lines.Add($"license: {ReleaseManifest.License}   {ReleaseManifest.LicenseContext}");
        if (ReleaseManifest.Repo.Length > 0)
            lines.Add($"repo: {ReleaseManifest.Repo}");
        return lines;
    }

    // ---------------------------------------------------------------- Devices

    static List<string> SaveCmd(string[] a)
    {
        if (a.Length == 0) return Usage("save <mac> [on|off]");
        var want = a.Length > 1 ? OnOff(a[1].ToLowerInvariant()) : true;
        Hub.Save(a[0], want);
        return new() { want ? Txt.T("save.remembered", a[0]) : Txt.T("save.forgotten", a[0]) };
    }

    static List<string> BlockCmd(string[] a)
    {
        if (a.Length == 0) return Usage("block <mac>");
        Hub.Block(a[0]);
        return new() { Txt.T("block.done", a[0]) };
    }

    static List<string> DevCfgCmd(string[] a)
    {
        if (a.Length == 0) return Rows(Hub.DevicesConfig(null),
            "continuousScan", "refreshThrottleMs", "autoReconnect", "reconnectIntervalSec",
            "reconnectGiveUpMin", "rssiWeakThreshold", "rssiCriticalThreshold",
            "autoConnect", "autoDetect", "autoDetectTimeoutSec");
        if (a.Length < 2) return new() { Txt.T("usage.devcfg") };
        var v = a[1];
        var body = a[0].ToLowerInvariant() switch
        {
            "scan" => Obj(("continuousScan", OnOff(v.ToLowerInvariant()))),
            "throttle" => Obj(("refreshThrottleMs", Int(v))),
            "reconnect" => Obj(("autoReconnect", OnOff(v.ToLowerInvariant()))),
            "interval" => Obj(("reconnectIntervalSec", Int(v))),
            "giveup" => Obj(("reconnectGiveUpMin", Int(v))),
            "weak" => Obj(("rssiWeakThreshold", Int(v))),
            "critical" => Obj(("rssiCriticalThreshold", Int(v))),
            "autoconnect" => Obj(("autoConnect", OnOff(v.ToLowerInvariant()))),
            "autodetect" => Obj(("autoDetect", OnOff(v.ToLowerInvariant()))),
            "timeout" => Obj(("autoDetectTimeoutSec", Int(v))),
            _ => null,
        };
        if (body == null) return new() { Txt.T("cli.unknown_key", a[0]) };
        return Rows(Hub.DevicesConfig(body));
    }

    static int Int(string s) => int.TryParse(s, out var v) ? v : 0;
    static double Dbl(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    // ---------------------------------------------------------------- Health

    static List<string> HealthCfgCmd(string[] a)
    {
        if (a.Length == 0) return Rows(Hub.HealthConfig(null));
        if (a.Length < 2) return new() { Txt.T("usage.hcfg") };
        var v = a[1];
        var body = a[0].ToLowerInvariant() switch
        {
            "sleep" => Obj(("sleepFactor", Dbl(v))),
            "active" => Obj(("activeFactor", Dbl(v))),
            "excited" => Obj(("excitedFactor", Dbl(v))),
            "spike" => Obj(("spikeDelta", Int(v))),
            "record" => Obj(("record", OnOff(v.ToLowerInvariant()))),
            "resting" => Obj(("restingBpm", Dbl(v))),
            _ => null,
        };
        if (body == null) return new() { Txt.T("cli.unknown_key", a[0]) };
        return Rows(Hub.HealthConfig(body));
    }

    // ---------------------------------------------------------------- Hardware

    static List<string> HwCfgCmd(string[] a)
    {
        if (a.Length == 0) return Rows(Hub.HwConfig(null), "intervalMs", "useFloat", "decimals", "round", "ntpServer");
        if (a.Length < 2) return new() { Txt.T("usage.hwcfg") };
        var v = a[1];
        var body = a[0].ToLowerInvariant() switch
        {
            "interval" => Obj(("intervalMs", Int(v))),
            "float" => Obj(("useFloat", OnOff(v.ToLowerInvariant()))),
            "decimals" => Obj(("decimals", Int(v))),
            "round" => Obj(("round", OnOff(v.ToLowerInvariant()))),
            "ntp" => Obj(("ntpServer", v)),
            _ => null,
        };
        if (body == null) return new() { Txt.T("cli.unknown_key", a[0]) };
        return Rows(Hub.HwConfig(body), "intervalMs", "useFloat", "decimals", "round", "ntpServer");
    }

    static List<string> SysInfoCmd(string[] a)
    {
        if (a.Length == 0) return new() { Txt.T("usage.sysinfo") };
        return new() { App.SysInfo.FormatTemplate(string.Join(' ', a)) };
    }

    // ---------------------------------------------------------------- OSC

    static List<string> OscCfgCmd(string[] a)
    {
        var o = App.Config.Osc;
        if (a.Length == 0)
            return new()
            {
                $"  ip       = {o.Ip}",
                $"  port     = {o.Port}",
                $"  address  = {o.Address}",
                $"  interval = {o.IntervalMs} ms",
                $"  recv     = {o.ReceivePort}",
                $"  template = {o.Template.Replace("\n", "\\n")}",
            };
        if (a.Length < 2) return new() { Txt.T("usage.oscfg") };
        var v = string.Join(' ', a[1..]);
        var body = a[0].ToLowerInvariant() switch
        {
            "ip" => Obj(("ip", v)),
            "port" => Obj(("port", v)),
            "address" => Obj(("address", v)),
            "interval" => Obj(("intervalMs", v)),
            "recv" => Obj(("receivePort", v)),
            // Convert literal \n sequences in templates to actual newlines, matching the frontend multiline text box.
            "template" => Obj(("template", v.Replace("\\n", "\n"))),
            _ => null,
        };
        if (body == null) return new() { Txt.T("cli.unknown_key", a[0]) };
        Hub.OscConfig(body);
        return new() { Txt.T("oscfg.saved") };
    }

    static List<string> ParamsCmd(string[] a)
    {
        if (a.FirstOrDefault()?.ToLowerInvariant() == "clear")
        {
            Hub.OscParamsClear();
            return new() { Txt.T("params.cleared") };
        }
        var el = JsonSerializer.SerializeToDocument(Hub.OscParams()).RootElement;
        if (!el.TryGetProperty("items", out var arr) || arr.GetArrayLength() == 0)
            return new() { Txt.T("params.none") };
        var out_ = new List<string> { Txt.T("params.count", arr.GetArrayLength(), el.GetProperty("recv")) };
        foreach (var p in arr.EnumerateArray())
        {
            var addr = p.TryGetProperty("addr", out var ad) ? ad.GetString() ?? "" : "";
            var count = p.TryGetProperty("count", out var c) ? c.ToString() : "0";
            var args = p.TryGetProperty("args", out var ag) ? ag.ToString() : "";
            out_.Add($"  {Pad(addr, 34)} x{count,-6} {args}");
        }
        return out_;
    }

    // ---------------------------------------------------------------- WebHook / API

    static List<string> WebhookCmd(string[] a)
    {
        switch (a.FirstOrDefault()?.ToLowerInvariant())
        {
            case "on":
                App.Config.Webhook.Enabled = true;
                App.Config.Save();
                return new() { Txt.T("wh.master", Txt.T("st.on")) };
            case "off":
                App.Config.Webhook.Enabled = false;
                App.Config.Save();
                return new() { Txt.T("wh.master", Txt.T("st.off")) };
            case "test":
            {
                if (!int.TryParse(a.ElementAtOrDefault(1), out var idx)) return new() { Txt.T("usage.webhook_test") };
                if (idx < 0 || idx >= App.Webhooks.Webhooks.Count) return new() { Txt.T("wh.idx_range", App.Webhooks.Webhooks.Count - 1) };
                // The test branch of HandleWebhook needs the complete item, so retrieve it from the list by index.
                var item = JsonNode.Parse(App.Webhooks.Webhooks[idx].GetRawText()) as JsonObject;
                var r = JsonSerializer.SerializeToDocument(Hub.WebhookAction("test", item, idx)).RootElement;
                var ok = r.TryGetProperty("ok", out var okv) && okv.GetBoolean();
                var status = r.TryGetProperty("status", out var st) ? st.ToString() : "";
                var err = r.TryGetProperty("error", out var ev) ? ev.ToString() : "";
                return new() { ok ? Txt.T("wh.triggered", idx, status) : Txt.T("wh.trigger_fail", err) };
            }
        }
        var list = App.Webhooks.Webhooks;
        var out_ = new List<string> { Txt.T("wh.list_head", Txt.T(App.Config.Webhook.Enabled ? "st.on" : "st.off"), list.Count) };
        for (var i = 0; i < list.Count; i++)
        {
            var w = list[i];
            var name = w.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var url = w.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var on = !(w.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False);
            out_.Add($"  #{i} {Txt.T(on ? "wh.item_on" : "wh.item_off")} {Pad(name, 18)} {url}");
        }
        return out_;
    }

    static List<string> ApiCmd(string[] a)
    {
        if (a.Length == 0) return Rows(Hub.ApiConfig(null),
            "enabled", "token", "pushSysInfo", "pushIntervalMs", "webhookThrottleMs");
        var key = a[0].ToLowerInvariant();
        if (key is "on" or "off") return Rows(Hub.ApiConfig(Obj(("enabled", key == "on"))),
            "enabled", "token", "pushSysInfo", "pushIntervalMs", "webhookThrottleMs");
        if (a.Length < 2) return new() { Txt.T("usage.api") };
        var v = a[1];
        var body = key switch
        {
            "token" => Obj(("token", v)),
            "push" => Obj(("pushSysInfo", OnOff(v.ToLowerInvariant()))),
            "interval" => Obj(("pushIntervalMs", Int(v))),
            "throttle" => Obj(("webhookThrottleMs", Int(v))),
            _ => null,
        };
        if (body == null) return new() { Txt.T("cli.unknown_key", a[0]) };
        return Rows(Hub.ApiConfig(body), "enabled", "token", "pushSysInfo", "pushIntervalMs", "webhookThrottleMs");
    }

    // ---------------------------------------------------------------- Web and remote access

    static List<string> WebCmd(string[] a)
    {
        var host = App.Web;
        switch (a.FirstOrDefault()?.ToLowerInvariant())
        {
            case "start":
                if (host == null) return new() { Txt.T("web.err_no_host") };
                return new() { host.Start(openUi: false) ? Txt.T("web.started", host.Url) : Txt.T("web.start_failed") };
            case "ui":
                if (host == null) return new() { Txt.T("web.err_no_host") };
                return new() { host.Start(openUi: true) ? Txt.T("web.started_ui", host.Url) : Txt.T("web.start_failed") };
            case "stop":
                host?.Stop();
                return new() { Txt.T("web.stopped") };
        }
        return new() { Txt.T("web.state", Txt.T(host?.Running == true ? "st.running" : "st.not_started"), host?.Url ?? App.WebUrl()) };
    }

    static List<string> RemoteCmd(string[] a)
    {
        var cfg = App.Config.Remote;
        var auth = App.Web?.Server?.Auth;
        switch (a.FirstOrDefault()?.ToLowerInvariant())
        {
            case null or "" or "status":
                return new()
                {
                    $"  enabled      = {cfg.Enabled}",
                    $"  wanEnabled   = {cfg.WanEnabled}",
                    $"  port         = {App.WebPort}   boundAll = {App.Web?.Server?.BoundAll}",
                    $"  idleMinutes  = {cfg.IdleMinutes}",
                    $"  sessions     = {auth?.Sessions().Count ?? 0}   users = {auth?.Users().Count ?? 0}",
                };
            case "on" or "off":
                cfg.Enabled = a[0].Equals("on", StringComparison.OrdinalIgnoreCase);
                App.Config.Save();
                App.Log.Info(LogText.L("log.web.lan_changed", cfg.Enabled ? "ON" : "OFF"));
                return new() { Txt.T("remote.state", Txt.T(cfg.Enabled ? "v.enabled" : "remote.off")) };
            case "wan":
                if (a.Length < 2) return Usage("remote wan <on|off>");
                if (a[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                {
                    // P6: the WAN switch refuses to save without a strong, non-default admin password.
                    if (auth == null || !auth.WanReady)
                        return new() { Txt.T("remote.wan_blocked") };
                    cfg.WanEnabled = true;
                }
                else
                {
                    cfg.WanEnabled = false;
                }
                App.Config.Save();
                App.Log.Warn(LogText.L("log.web.wan_changed", cfg.WanEnabled ? "ON" : "OFF"));
                return new() { Txt.T("remote.wan", Txt.T(cfg.WanEnabled ? "sw.on" : "sw.off")) };
            case "idle":
                if (a.Length < 2) return new() { Txt.T("usage.remote_idle") };
                cfg.IdleMinutes = Math.Max(0, Int(a[1]));
                App.Config.Save();
                return new() { Txt.T("remote.idle", cfg.IdleMinutes) };
            case "users":
            {
                if (auth == null) return new() { Txt.T("remote.users_none") };
                var us = auth.Users();
                var out_ = new List<string> { Txt.T("remote.users_count", us.Count) };
                out_.AddRange(us.Select(u =>
                    $"  {Pad(u.Username, 18)} {Pad(u.Role, 6)} {Txt.T("remote.tab_label")} {(u.Tabs.Count == 0 ? Txt.T("v.default") : string.Join(',', u.Tabs))}"));
                return out_;
            }
            case "sessions":
            {
                if (auth == null) return new() { Txt.T("web.off") };
                var ss = auth.Sessions();
                if (ss.Count == 0) return new() { Txt.T("remote.sessions_none") };
                var out_ = new List<string> { Txt.T("remote.sessions_count", ss.Count) };
                out_.AddRange(ss.Select(s =>
                    $"  {s.TokenHash[..8]}  {Pad(s.Username, 16)} {Pad(s.Role, 6)} {s.RemoteIp,-16} {Txt.T("remote.last_active")} {s.LastSeen:MM-dd HH:mm:ss}"));
                return out_;
            }
            case "kick":
                if (a.Length < 2) return new() { Txt.T("usage.remote_kick") };
                if (auth == null) return new() { Txt.T("web.off") };
                var hit = auth.Sessions().FirstOrDefault(s => s.TokenHash.StartsWith(a[1], StringComparison.OrdinalIgnoreCase));
                if (hit == null) return new() { Txt.T("remote.session_not_found", a[1]) };
                return new() { auth.Kick(hit.TokenHash) ? Txt.T("remote.session_killed", hit.TokenHash[..8], hit.Username) : Txt.T("remote.kill_fail") };
            // User creation and password changes are available only in the frontend because CLI history and echo would expose plaintext credentials.
            case "adduser" or "passwd" or "pass":
                return new() { Txt.T("remote.cred_error") };
            case "deluser":
                if (a.Length < 2) return new() { Txt.T("usage.remote_deluser") };
                if (auth == null) return new() { Txt.T("web.off") };
                return new() { auth.Manage("remove", a[1], null, null, null) != null ? Txt.T("remote.user_deleted", a[1]) : Txt.T("remote.user_del_fail") };
            case "role":
                if (a.Length < 3) return new() { Txt.T("usage.remote_role") };
                if (auth == null) return new() { Txt.T("web.off") };
                return new() { auth.Manage("role", a[1], null, a[2].ToLowerInvariant(), null) != null ? Txt.T("remote.role_changed", a[1], a[2]) : Txt.T("remote.change_fail") };
            case "tabs":
                if (a.Length < 2) return new() { Txt.T("usage.remote_tabs") };
                if (auth == null) return new() { Txt.T("web.off") };
                var tabs = a.Length > 2
                    ? a[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                    : new List<string>();
                return new() { auth.Manage("tabs", a[1], null, null, tabs) != null ? Txt.T("remote.tabs_set", a[1], tabs.Count == 0 ? Txt.T("v.default") : string.Join(',', tabs)) : Txt.T("remote.change_fail") };
        }
        return new() { Txt.T("cli.unknown_subcmd", a[0]) };
    }

    // ---------------------------------------------------------------- UI, floating windows, and log settings

    static List<string> UiCmd(string[] a)
    {
        var u = App.Config.Ui;
        if (a.Length == 0)
            return new()
            {
                $"  lang    = {u.Lang}",
                $"  mode    = {u.Mode}",
                $"  palette = {u.Palette}",
                $"  corner  = {u.CornerRadius}",
                $"  density = {u.Density}",
                $"  anim    = {u.Animations}",
                $"  close   = {u.CloseAction}",
                $"  brand   = {(u.Brand.Length == 0 ? Txt.T("ui.brand_default") : u.Brand)}",
            };
        if (a.Length < 2) return new() { Txt.T("usage.ui") };
        var v = string.Join(' ', a[1..]);
        var body = a[0].ToLowerInvariant() switch
        {
            "lang" => Obj(("lang", v.ToLowerInvariant())),
            "mode" => Obj(("mode", v.ToLowerInvariant())),
            "palette" => Obj(("palette", v.ToLowerInvariant())),
            "corner" => Obj(("cornerRadius", Int(v))),
            "density" => Obj(("density", Dbl(v))),
            "anim" => Obj(("animations", OnOff(v.ToLowerInvariant()))),
            "close" => Obj(("closeAction", v.ToLowerInvariant())),
            "brand" => Obj(("brand", v)),
            _ => null,
        };
        if (body == null) return new() { Txt.T("cli.unknown_key", a[0]) };
        Hub.Settings(Obj(("ui", body)));
        return UiCmd(Array.Empty<string>());
    }

    static List<string> FloatCmd(string[] a)
    {
        if (!App.GuiHosted) return new() { Txt.T("float.err_no_gui") };
        switch (a.FirstOrDefault()?.ToLowerInvariant())
        {
            case "open": Hub.FloatOpen(a.ElementAtOrDefault(1)); return new() { Txt.T("float.opened") };
            case "close": Hub.FloatCloseAll(); return new() { Txt.T("float.all_closed") };
            case "lock": Hub.FloatLock(true); return new() { Txt.T("float.locked") };
            case "unlock": Hub.FloatLock(false); return new() { Txt.T("float.unlocked") };
        }
        return Rows(Hub.FloatConfig(null));
    }

    static List<string> LogCfgCmd(string[] a)
    {
        var l = App.Config.Logs;
        if (a.Length == 0)
            return new()
            {
                $"  autodump = {l.AutoDumpEnabled}",
                Txt.T("logcfg.interval", l.AutoDumpIntervalMin),
                Txt.T("logcfg.trace", l.TraceEnabled, Txt.T(App.TraceEnabled ? "v.enabled" : "v.disabled")),
            };
        if (a.Length < 2) return new() { Txt.T("usage.logcfg") };
        var v = a[1];
        var body = a[0].ToLowerInvariant() switch
        {
            "autodump" => Obj(("autoDumpEnabled", OnOff(v.ToLowerInvariant()))),
            "interval" => Obj(("autoDumpIntervalMin", v)),
            "trace" => Obj(("traceEnabled", OnOff(v.ToLowerInvariant()))),
            _ => null,
        };
        if (body == null) return new() { Txt.T("cli.unknown_key", a[0]) };
        Hub.Settings(Obj(("logs", body)));
        return LogCfgCmd(Array.Empty<string>());
    }
}
