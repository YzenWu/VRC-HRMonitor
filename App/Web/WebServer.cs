using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeartRateMonitor.Core;
using HeartRateMonitor.Toolkit;

namespace HeartRateMonitor.Web;

/// <summary>
/// Local Web UI server (SolidJS frontend +REST API + WebSocket export).
/// Business logic is fully delegated to <see cref="AppHub"/>, which is responsible only for HTTP/WS transmissions.
/// </summary>
public sealed class WebServer : IDisposable
{
    public int Port { get; }
    private readonly AppHub _hub;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _webRoot;
    private readonly List<WebSocket> _clients = new();
    private readonly object _wsLock = new();
    private readonly object _sendLock = new();
    private bool _started;
    private readonly RemoteAuth _auth;
    private readonly string _scheme;
    /// <summary>P6: true when the single listener bound all interfaces; false means a loopback-only fallback (LAN/WAN sources cannot reach the port).</summary>
    private bool _boundAll;

    public WebServer(int port, AppHub hub, RemoteAuth? auth = null, string? scheme = null)
    {
        Port = port;
        _hub = hub;
        _auth = auth ?? new RemoteAuth(App.DataDir);
        _scheme = (scheme ?? "http").Trim().ToLowerInvariant();
        _webRoot = Path.Combine(App.ExeDir, "webui");
        // Phase 10 toolkit: relay throttled photo-scan progress to every WS client (toolkit_index events).
        VrcToolkitService.ScanProgressChanged += OnToolkitScanProgress;
    }

    private void OnToolkitScanProgress(int done, int total, int indexed)
        => Broadcast("toolkit_index", new { done, total, indexed });

    /// <summary> remote certifier (CLI sub-command for remote to read user/session; read only references and operate RemoteAuth with its own lock). </summary>
    public RemoteAuth Auth => _auth;

    /// <summary>P6: whether the single listener is reachable from the network (all-interface bind); a loopback fallback means LAN/WAN cannot connect until a URL ACL exists.</summary>
    public bool BoundAll => _boundAll;

    /// <summary>
    /// P6 single listener: bind every interface on the local web port so loopback, LAN, and WAN sources share one
    /// endpoint; authorization is decided per request by source zone (see SourceZone below). Binding "+" needs a URL
    /// reservation for non-admin processes — on refusal the server falls back to loopback-only and stays usable.
    /// </summary>
    public bool Start()
    {
        if (_started) return true;
        if (_scheme is not ("http" or "https"))
        {
            App.Log.Error($"Web startup refused: unsupported scheme '{_scheme}'. Use http or https.");
            return false;
        }
        var certLocation = StoreLocation.CurrentUser;
        if (_scheme == "https" && !TryFindHttpsCertificate(out certLocation))
        {
            App.Log.Error($"HTTPS startup refused: certificate '{NormalizeThumbprint(App.Config.Web.CertificateThumbprint)}' was not found with a private key in CurrentUser/My or LocalMachine/My. Configure HTTP.sys as administrator before retrying: netsh http add sslcert ipport=0.0.0.0:{Port} certhash=THUMBPRINT appid={{YOUR-APP-GUID}}");
            return false;
        }
        try
        {
            var host = App.SafeMode ? "127.0.0.1" : "+";
            var allPrefix = $"{_scheme}://{host}:{Port}/";
            if (!_listener.Prefixes.Contains(allPrefix)) _listener.Prefixes.Add(allPrefix);
            _listener.Start();
            _boundAll = !App.SafeMode;
            App.Log.Info(App.SafeMode
                ? $"Safe mode Web listener bound loopback only ({_scheme}://127.0.0.1:{Port}/)"
                : LogText.L("log.web.bind_lan", Port));
            if (_scheme == "https") App.Log.Info($"HTTPS certificate validated in {certLocation}/My; HTTP.sys SSL binding is administrator-managed.");
        }
        catch (Exception e)
        {
            if (_scheme == "https")
            {
                try { _listener.Close(); } catch { }
                App.Log.Error($"HTTPS listener failed and HTTP fallback is disabled: {e.Message}. Configure the HTTP.sys binding as administrator: netsh http add sslcert ipport=0.0.0.0:{Port} certhash={NormalizeThumbprint(App.Config.Web.CertificateThumbprint)} appid={{YOUR-APP-GUID}}");
                return false;
            }
            // All-interface HTTP bind refused (needs URL ACL). Retry on loopback only; LAN/WAN stay unreachable but the local UI survives.
            try { _listener.Close(); } catch { }
            _listener.Prefixes.Clear();
            var prefix = $"{_scheme}://127.0.0.1:{Port}/";
            _listener.Prefixes.Add(prefix);
            try
            {
                _listener.Start();
            }
            catch (Exception e2)
            {
                App.Log.Error(LogText.L("log.web.start_fail", Port, e2.Message));
                return false;
            }
            _boundAll = false;
            App.Log.Warn(LogText.L("log.web.bind_loopback_fallback", e.Message, Port));
        }
        _started = true;
        _hub.ClientEvent += OnHubEvent;
        App.Log.Info(LogText.L(_webRoot.Length > 0 && Directory.Exists(_webRoot) ? "log.web.start_ok" : "log.web.start_noui", Port));
        // Long-cycle dedicated threads to avoid occupation of the pool
        _ = Task.Factory.StartNew(async () => await Loop(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return true;
    }

    private static string NormalizeThumbprint(string? value)
        => new((value ?? "").Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool TryFindHttpsCertificate(out StoreLocation location)
    {
        location = StoreLocation.CurrentUser;
        var thumbprint = NormalizeThumbprint(App.Config.Web.CertificateThumbprint);
        if (thumbprint.Length == 0) return false;
        foreach (var candidate in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.My, candidate);
                store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
                if (store.Certificates.Cast<X509Certificate2>().Any(c =>
                        string.Equals(NormalizeThumbprint(c.Thumbprint), thumbprint, StringComparison.Ordinal)
                        && c.HasPrivateKey))
                {
                    location = candidate;
                    return true;
                }
            }
            catch { }
        }
        return false;
    }

    /// <summary>Kept for the CLI/web lifecycle callers: with one listener there is nothing to restart — LAN/WAN switches apply per request.</summary>
    public void RefreshRemote()
    {
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _hub.ClientEvent -= OnHubEvent;
        try { _listener.Stop(); } catch { }
        _cts.Cancel();
        lock (_wsLock)
        {
            foreach (var ws in _clients.ToList())
            {
                try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "server stop", CancellationToken.None).GetAwaiter().GetResult(); } catch { }
                try { ws.Dispose(); } catch { }
            }
            _clients.Clear();
        }
    }

    private void OnHubEvent(string type, object? data) => Broadcast(type, data);

    // ------------------------------------------------------------ Request Loop

    private async Task Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Handle(ctx);
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            var raw = ctx.Request.Url?.AbsolutePath ?? "/";
            var path = Uri.UnescapeDataString(raw);
            // P6 source zones: authorization comes from the request source, not the port — the single listener
            // serves loopback (local admin), private (LAN), and public (WAN) sources from one endpoint.
            // Proxy headers (Forwarded / X-Forwarded-For) are deliberately NOT trusted; only RemoteEndPoint is used
            // until an explicit trusted-proxy configuration exists.
            var ip = RemoteIp(ctx);
            var zone = SourceZone.Of(ip);
            var trusted = zone == SourceZone.Zone.Loopback;
            if (!trusted && App.SafeMode)
            {
                Json(ctx.Response, new { ok = false, error = "safe mode is loopback only" }, 403);
                return;
            }
            if (!trusted && App.Config.Web.RequireHttpsForRemote && !ctx.Request.IsSecureConnection)
            {
                Json(ctx.Response, new { ok = false, error = "HTTPS is required for remote access" }, 403);
                return;
            }
            RemoteAuth.Session? sess;
            if (trusted)
            {
                sess = RemoteAuth.LocalSession(ip);
            }
            else
            {
                // A damaged credential store is an authentication failure, not an empty first-run store.
                if (_auth.UsersLoadFailed)
                {
                    App.Log.Warn(LogText.L("log.web.source_reject", "credential-store-error", ip));
                    Json(ctx.Response, new { ok = false, error = "remote credential store unavailable" }, 503);
                    return;
                }
                // Non-loopback sources require remote access enabled and, for public sources, the WAN switch.
                if (!App.Config.Remote.Enabled)
                {
                    App.Log.Warn(LogText.L("log.web.source_reject", "remote-disabled", ip));
                    Json(ctx.Response, new { ok = false, error = "remote disabled" }, 403);
                    return;
                }
                if (zone == SourceZone.Zone.Public && !App.Config.Remote.WanEnabled)
                {
                    App.Log.Warn(LogText.L("log.web.source_reject", "wan-disabled", ip));
                    Json(ctx.Response, new { ok = false, error = "wan disabled" }, 403);
                    return;
                }
                sess = _auth.Resolve(GetSessionToken(ctx.Request), ctx.Request.UserAgent ?? "", ip);
            }
            if (path == "/ws")
            {
                if (sess == null) { Json(ctx.Response, new { ok = false, error = "unauthorized" }, 401); return; }
                await HandleWs(ctx, sess, trusted);
                return;
            }
            // /heartbeat must be determined before a static file, otherwise SPA will be returned as a page request
            if (path.Equals("/heartbeat", StringComparison.OrdinalIgnoreCase))
            {
                if (sess == null) { Json(ctx.Response, new { ok = false, error = "unauthorized" }, 401); return; }
                HandleHeartbeat(ctx);
                return;
            }
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                if (sess == null)
                {
                    // Login/ Session query interface itself does not require login (and me → frontend 401 bullet login without login)
                    if (!path.StartsWith("/api/remote/", StringComparison.OrdinalIgnoreCase))
                    {
                        Json(ctx.Response, new { ok = false, error = "unauthorized" }, 401);
                        return;
                    }
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await HandleApi(ctx, path, sess);
                sw.Stop();
                HrmTrace.Perf($"web.api{path}", sw.ElapsedMilliseconds, 30);
                return;
            }
            // Static file (SPA/entry page) release: remote unlogged-in also loads the logout boundary Noodles.
            ServeStatic(ctx, path);
        }
        catch (Exception e)
        {
            App.Log.Debug(LogText.L("log.web.request_fail", e.Message));
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    /// <summary>
    /// P6 request-source classification. Loopback keeps the local admin; RFC1918, link-local, and IPv6 ULA are private
    /// (LAN); everything else is public (WAN) and requires the explicit WAN switch.
    /// </summary>
    public static class SourceZone
    {
        public enum Zone { Loopback, Private, Public }

        public static Zone Of(string? ip)
        {
            if (string.IsNullOrEmpty(ip)) return Zone.Public;
            if (!IPAddress.TryParse(ip, out var raw)) return Zone.Public;
            // Normalize IPv4-mapped IPv6 (::ffff:a.b.c.d) so zone checks see the real IPv4 address.
            if (raw.IsIPv4MappedToIPv6) raw = raw.MapToIPv4();
            if (IPAddress.IsLoopback(raw)) return Zone.Loopback;
            if (raw.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = raw.GetAddressBytes();
                if (b[0] == 10) return Zone.Private;                                   // 10.0.0.0/8
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return Zone.Private;       // 172.16.0.0/12
                if (b[0] == 192 && b[1] == 168) return Zone.Private;                   // 192.168.0.0/16
                if (b[0] == 169 && b[1] == 254) return Zone.Private;                   // link-local
                return Zone.Public;
            }
            var nb = raw.GetAddressBytes();
            if (nb[0] == 0xFE && (nb[1] & 0xC0) == 0x80) return Zone.Private;           // fe80::/10 link-local
            if ((nb[0] & 0xFE) == 0xFC) return Zone.Private;                            // fc00::/7 ULA
            return Zone.Public;
        }
    }

    private static string RemoteIp(HttpListenerContext ctx)
        // Missing endpoints are unknown/untrusted; never fail open as loopback administrator.
        => ctx.Request.RemoteEndPoint?.Address?.ToString() ?? "";

    private static string? GetSessionToken(HttpListenerRequest req)
    {
        var cookie = req.Headers["Cookie"];
        if (string.IsNullOrEmpty(cookie)) return null;
        var prefix = RemoteAuth.CookieName + "=";
        var i = cookie.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var v = cookie[(i + prefix.Length)..];
        var end = v.IndexOf(';');
        return (end >= 0 ? v[..end] : v).Trim();
    }

    // ------------------------------------------------------------ Static File

    static readonly Dictionary<string, string> Mime = new()
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".map"] = "application/json",
    };

    private void ServeStatic(HttpListenerContext ctx, string path)
    {
        var rel = path.TrimStart('/').Replace('\\', '/');
        string root;
        if (rel.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            root = Path.Combine(App.ExeDir, "image");
            rel = rel["image/".Length..];
        }
        else
        {
            root = _webRoot;
        }
        if (rel.Length == 0) rel = "index.html";
        var full = Path.GetFullPath(Path.Combine(root, rel));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            if (!ReferenceEquals(root, _webRoot) && !string.Equals(root, _webRoot, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }
            // One page retreat: Resource request for unhit and non-extension
            full = Path.Combine(_webRoot, "index.html");
            if (!File.Exists(full))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }
        }
        var ext = Path.GetExtension(full).ToLowerInvariant();
        ctx.Response.ContentType = Mime.TryGetValue(ext, out var m) ? m : "application/octet-stream";
        ctx.Response.Headers["Cache-Control"] = ext is ".html" ? "no-cache" : "max-age=3600";
        var bytes = File.ReadAllBytes(full);
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    // ------------------------------------------------------------ Phase 6: External interface

    /// <summary>XApi.Token is empty = not verified (only return ring can be reached); non-empty mustache = token= or X-Api-Token head. </summary>
    private static bool AuthOk(HttpListenerRequest req)
        => TokenOk(req.QueryString["token"]) || TokenOk(req.Headers["X-Api-Token"]);

    private static bool TokenOk(string? given)
    {
        var token = App.Config.Api.Token;
        return token.Length == 0 || string.Equals(token, given, StringComparison.Ordinal);
    }

    /// <summary>XQ1QXZ/heartbeat: Heartrate + Device + Health + System information for external rotation. </summary>
    private void HandleHeartbeat(HttpListenerContext ctx)
    {
        var resp = ctx.Response;
        if (!App.Config.Api.Enabled) { Json(resp, new { ok = false, error = "api disabled" }, 403); return; }
        if (!AuthOk(ctx.Request)) { Json(resp, new { ok = false, error = "unauthorized" }, 401); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        object data;
        try { data = _hub.Heartbeat(); }
        catch (Exception e) { Json(resp, new { ok = false, error = e.Message }, 500); return; }
        sw.Stop();
        HrmTrace.Perf("web.heartbeat", sw.ElapsedMilliseconds, 30);
        Json(resp, data);
    }

    // ------------------------------------------------------------ WebSocket

    private async Task HandleWs(HttpListenerContext ctx, RemoteAuth.Session sess, bool trusted)
    {
        // Shake hands unstopped: WebUI relies on WS for delivery; the next line command also press Api.Enabled + token to turn off separately
        var authed = AuthOk(ctx.Request);
        // Keep the raw token so every message can re-resolve role/account changes and session kicks.
        var sessionToken = trusted ? null : GetSessionToken(ctx.Request);
        var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
        lock (_wsLock) _clients.Add(ws);
        var buf = new byte[4096];
        var sb = new StringBuilder();
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var res = await ws.ReceiveAsync(buf, CancellationToken.None);
                if (res.MessageType == WebSocketMessageType.Close)
                {
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                    break;
                }
                if (res.MessageType != WebSocketMessageType.Text) continue;
                sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                if (!res.EndOfMessage) continue;
                var text = sb.ToString();
                sb.Clear();
                if (text.Trim().Length == 0) continue;
                var active = trusted ? sess : _auth.Resolve(sessionToken, ctx.Request.UserAgent ?? "", RemoteIp(ctx));
                var zone = SourceZone.Of(RemoteIp(ctx));
                if (active == null || (!trusted && (!App.Config.Remote.Enabled
                    || (zone == SourceZone.Zone.Public && !App.Config.Remote.WanEnabled))))
                {
                    try { await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "session revoked", CancellationToken.None); } catch { }
                    break;
                }
                HandleWsCommand(ws, text, authed, trusted || active.Role == "admin");
            }
        }
        catch { }
        finally
        {
            lock (_wsLock) _clients.Remove(ws);
            try { ws.Dispose(); } catch { }
        }
    }

    /// <summary>WS Downline Command {"id":N,"cmd":"..."}: Share AppHub.Dispatch with IPC, responding to the id orientation return. </summary>
    private void HandleWsCommand(WebSocket ws, string text, bool authed, bool admin)
    {
        JsonObject? req;
        try { req = JsonNode.Parse(text) as JsonObject; }
        catch { SendTo(ws, "", 0, new { ok = false, error = "invalid json" }); return; }
        var cmd = req?["cmd"] is JsonValue cv && cv.TryGetValue<string>(out var cs) ? cs ?? "" : "";
        var id = req?["id"] is JsonValue iv && iv.TryGetValue<int>(out var i) ? i : 0;
        if (cmd.Length == 0) { SendTo(ws, cmd, id, new { ok = false, error = "missing cmd" }); return; }
        if (!App.Config.Api.Enabled) { SendTo(ws, cmd, id, new { ok = false, error = "api disabled" }); return; }
        // Allow clients that cannot set authorization headers to provide the token in the message.
        var tok = req?["token"] is JsonValue tv && tv.TryGetValue<string>(out var ts) ? ts : null;
        if (!authed && !TokenOk(tok)) { SendTo(ws, cmd, id, new { ok = false, error = "unauthorized" }); return; }
        // P6 capability whitelist: remote user-role sessions only dispatch commands matching their REST capabilities.
        if (!admin && !UserWsCommands.Contains(cmd))
        { SendTo(ws, cmd, id, new { ok = false, error = "forbidden" }); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        object data;
        try { data = _hub.Dispatch(cmd, req, admin); }
        catch (Exception e) { data = new { ok = false, error = e.Message }; }
        sw.Stop();
        HrmTrace.Perf($"web.ws.{cmd}", sw.ElapsedMilliseconds, 30);
        SendTo(ws, cmd, id, data);
    }

    /// <summary>P6: commands a remote user-role session may dispatch over WS — mirrors the REST capability whitelist.</summary>
    private static readonly HashSet<string> UserWsCommands = new(StringComparer.Ordinal)
    {
        "status", "devices", "connect", "disconnect", "save", "rename", "unblock",
        "osc_connect", "osc_config", "osc_test", "osc_custom", "osc_params", "osc_params_clear",
        "heartbeat", "health", "health_config", "monitor", "monitor_export",
        "hw", "hw_refresh", "sysinfo", "logs", "export", "record",
    };

    /// <summary> directs the return of a single connection with an envelope consistent with Broadcast (type=response). </summary>
    private void SendTo(WebSocket ws, string cmd, int id, object? data)
    {
        string json;
        try { json = JsonSerializer.Serialize(new { type = "response", id, cmd, data, ts = $"{DateTime.Now:O}" }); }
        catch { return; }
        var bytes = Encoding.UTF8.GetBytes(json);
        lock (_sendLock)
        {
            try
            {
                if (ws.State != WebSocketState.Open) return;
                ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                lock (_wsLock) _clients.Remove(ws);
            }
        }
    }

    private void Broadcast(string type, object? data)
    {
        string json;
        try { json = JsonSerializer.Serialize(new { type, data, ts = $"{DateTime.Now:O}" }); }
        catch { return; }
        List<WebSocket> snap;
        lock (_wsLock) snap = _clients.ToList();
        if (snap.Count == 0) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        lock (_sendLock)
        {
            foreach (var ws in snap)
            {
                try
                {
                    if (ws.State != WebSocketState.Open)
                    {
                        lock (_wsLock) _clients.Remove(ws);
                        continue;
                    }
                    ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch
                {
                    lock (_wsLock) _clients.Remove(ws);
                }
            }
        }
    }

    // ------------------------------------------------------------ REST API

    private async Task HandleApi(HttpListenerContext ctx, string path, RemoteAuth.Session? sess)
    {
        var method = ctx.Request.HttpMethod;
        var resp = ctx.Response;

        if (path.StartsWith("/api/remote/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleRemoteApi(ctx, path, sess);
            return;
        }
        // Capability whitelist applies to every non-admin role; unknown/migrated roles fail closed.
        if (sess is { Role: not "admin" } && !ApiOpAllowed(path))
        {
            Json(resp, new { ok = false, error = "forbidden" }, 403);
            return;
        }

        if (path.StartsWith("/api/toolkit/", StringComparison.OrdinalIgnoreCase))
        {
            if (sess is not { Role: "admin" }) { Json(resp, new { ok = false, error = "forbidden" }, 403); return; }
            var q = ctx.Request.QueryString;
            var localOnly = RemoteAuth.IsLoopback(RemoteIp(ctx));
            if (path == "/api/toolkit/paths" && method == "GET") { Json(resp, VrcToolkitService.Paths()); return; }
            if (path == "/api/toolkit/config" && method == "GET") { Json(resp, VrcToolkitService.Config()); return; }
            if (path == "/api/toolkit/config" && method == "POST") { try { Json(resp, VrcToolkitService.SaveConfig(await ReadJson(ctx.Request))); } catch (Exception e) { Json(resp, new { ok = false, error = e.Message }, 400); } return; }
            if (path == "/api/toolkit/photos/index" && method == "POST") { var b = await ReadJson(ctx.Request); Json(resp, VrcToolkitService.StartIndex(b?["concurrency"]?.GetValue<int>() ?? 2)); return; }
            if (path == "/api/toolkit/photos/cancel" && method == "POST") { Json(resp, VrcToolkitService.CancelIndex()); return; }
            if (path == "/api/toolkit/photos/progress" && method == "GET") { Json(resp, VrcToolkitService.ScanProgress()); return; }
            if (path == "/api/toolkit/photos" && method == "GET") { Json(resp, VrcToolkitService.Search(q["q"] ?? "", int.TryParse(q["page"], out var pg) ? pg : 1, int.TryParse(q["pageSize"], out var ps) ? ps : 30)); return; }
            // Log/crash content is sensitive: these two stay loopback-only (remote admin included).
            if (path == "/api/toolkit/logs" && method == "GET") { if (!localOnly) { Json(resp, new { ok = false, error = "local only" }, 403); return; } Json(resp, VrcToolkitService.ListLogs()); return; }
            if (path == "/api/toolkit/logs/read" && method == "GET") { if (!localOnly) { Json(resp, new { ok = false, error = "local only" }, 403); return; } Json(resp, VrcToolkitService.ReadLog(q["name"] ?? "", q["search"] ?? "", int.TryParse(q["lines"], out var ln) ? ln : 500)); return; }
            if (path == "/api/toolkit/cache" && method == "GET") { Json(resp, VrcToolkitService.CacheStats()); return; }
            if (path == "/api/toolkit/cache/clear" && method == "POST")
            {
                var b = await ReadJson(ctx.Request);
                Json(resp, await VrcToolkitService.ClearCacheAsync(
                    b?["dryRun"]?.GetValue<bool>() ?? false,
                    b?["confirm"]?.GetValue<bool>() ?? false,
                    b?["phrase"]?.GetValue<string>() ?? ""));
                return;
            }
            if (path == "/api/toolkit/game-stats" && method == "GET") { Json(resp, VrcToolkitService.GameStats(int.TryParse(q["days"], out var d) ? d : 30)); return; }
            if (path == "/api/toolkit/process" && method == "GET") { Json(resp, VrcToolkitService.ProcessSnapshot()); return; }
            Json(resp, new { ok = false, error = "not found" }, 404); return;
        }

        if (path == "/api/status" && method == "GET") { Json(resp, _hub.Status()); return; }
        if (path == "/api/devices" && method == "GET") { Json(resp, _hub.Devices()); return; }
        if (path == "/api/scan" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Scan(body?["action"]?.GetValue<string>() ?? ""));
            return;
        }
        if (path == "/api/device/connect" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Connect(body?["mac"]?.GetValue<string>() ?? ""));
            return;
        }
        if (path == "/api/device/disconnect" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Disconnect(body?["mac"]?.GetValue<string>() ?? ""));
            return;
        }
        if (path == "/api/device/save" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            var want = body?.TryGetPropertyValue("saved", out var sv) == true && sv is JsonValue v ? v.GetValue<bool>() : (bool?)null;
            Json(resp, _hub.Save(body?["mac"]?.GetValue<string>() ?? "", want));
            return;
        }
        if (path == "/api/device/block" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Block(body?["mac"]?.GetValue<string>() ?? ""));
            return;
        }
        if (path == "/api/device/unblock" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Unblock(body?["mac"]?.GetValue<string>() ?? ""));
            return;
        }
        if (path == "/api/devices/batch" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            var macs = (body?["macs"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").Where(m => m.Length > 0).ToList() ?? new();
            Json(resp, _hub.Batch(body?["action"]?.GetValue<string>() ?? "", macs));
            return;
        }
        if (path == "/api/osc/connect" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.OscConnect(body?["connected"]?.GetValue<bool>() ?? false));
            return;
        }
        if (path == "/api/osc/config" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.OscConfig(body));
            return;
        }
        if (path == "/api/osc/test" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.OscTest(
                body?["ip"]?.GetValue<string>() ?? App.Config.Osc.Ip,
                body?["port"]?.GetValue<string>() ?? App.Config.Osc.Port,
                body?["address"]?.GetValue<string>() ?? "/test/echo",
                body?["text"]?.GetValue<string>() ?? "hello"));
            return;
        }
        if (path == "/api/osc/custom" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.OscCustom(body));
            return;
        }
        if (path == "/api/osc/params" && method == "GET") { Json(resp, _hub.OscParams()); return; }
        if (path == "/api/osc/params/clear" && method == "POST") { Json(resp, _hub.OscParamsClear()); return; }
        if (path == "/api/cli" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Cli(body?["line"]?.GetValue<string>() ?? ""));
            return;
        }
        if (path == "/api/cli/help" && method == "GET")
        {
            Json(resp, new { commands = Core.CommandShell.Commands.Select(c => new { name = c.Name, usage = c.Usage, desc = Txt.T(c.Desc) }).ToArray() });
            return;
        }
        if (path == "/api/monitor" && method == "GET")
        {
            var q = ctx.Request.QueryString;
            var hours = int.TryParse(q["hours"], out var h) ? h : 24;
            var buckets = int.TryParse(q["buckets"], out var b) ? b : 12;
            Json(resp, _hub.Monitor(hours, buckets));
            return;
        }
        if (path == "/api/monitor/export" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.MonitorExport(
                body?["hours"]?.GetValue<int>() ?? 24,
                body?["buckets"]?.GetValue<int>() ?? 12,
                body?["format"]?.GetValue<string>() ?? "json"));
            return;
        }
        if (path == "/api/hw" && method == "GET") { Json(resp, _hub.Hw()); return; }
        if (path == "/api/hw/refresh" && method == "POST") { Json(resp, _hub.HwRefresh()); return; }
        if (path == "/api/vrchat" && method == "GET") { Json(resp, _hub.VrchatStatus()); return; }
        if (path == "/api/logs" && method == "GET")
        {
            var q = ctx.Request.QueryString;
            var filter = q["filter"] ?? "";
            var limit = int.TryParse(q["limit"], out var l) ? Math.Clamp(l, 1, 5000) : 800;
            var rx = q["regex"] is "1" or "true";
            // levels=ERROR, WARN (coma-separated)
            var levels = (q["levels"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            Json(resp, _hub.Logs(filter, limit, rx, levels));
            return;
        }
        if (path == "/api/logs/clear" && method == "POST") { Json(resp, _hub.LogsClear()); return; }
        if (path == "/api/logs/dump" && method == "POST") { Json(resp, _hub.LogsDump()); return; }
        if (path == "/api/logs/export" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            var levels = (body?["levels"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList() ?? new();
            Json(resp, _hub.LogsExport(
                body?["filter"]?.GetValue<string>() ?? "",
                body?["format"]?.GetValue<string>() ?? "txt",
                body?["regex"]?.GetValue<bool>() ?? false,
                levels));
            return;
        }
        if (path == "/api/webhooks" && method == "GET") { Json(resp, _hub.Webhooks()); return; }
        if (path == "/api/webhooks" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.WebhookAction(
                body?["action"]?.GetValue<string>() ?? "",
                body?["item"] as JsonObject,
                body?["index"]?.GetValue<int>() ?? -1));
            return;
        }
        if (path == "/api/settings" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Settings(body));
            return;
        }
        // P1: login autostart (machine-level registration; GET = actual system state, POST = apply).
        if (path == "/api/autostart" && method == "GET") { Json(resp, _hub.AutoStart(null)); return; }
        if (path == "/api/autostart" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.AutoStart(body));
            return;
        }
        // P3: GitHub project snapshot (read-only) and manual refresh.
        if (path == "/api/github" && method == "GET") { Json(resp, _hub.GitHubInfo()); return; }
        if (path == "/api/github/refresh" && method == "POST")
        {
            if (App.SafeMode)
            {
                Json(resp, new { ok = false, error = "safe mode blocks external network requests" }, 403);
                return;
            }
            await Core.GitHubProjectService.RefreshAsync();
            Json(resp, _hub.GitHubInfo());
            return;
        }
        if (path == "/api/config" && method == "GET") { Json(resp, _hub.Config()); return; }
        // ---- Phase 3~5 Add (corresponding to IPC command, share AppHub) -
        if (path == "/api/health" && method == "GET") { Json(resp, _hub.Health("get")); return; }
        if (path == "/api/health" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Health(body?["action"]?.GetValue<string>() ?? "get"));
            return;
        }
        if (path == "/api/health/config" && method == "GET") { Json(resp, _hub.HealthConfig(null)); return; }
        if (path == "/api/health/config" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.HealthConfig(body));
            return;
        }
        if (path == "/api/export" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Export(
                body?["table"]?.GetValue<string>() ?? "hr_records",
                body?["format"]?.GetValue<string>() ?? "json",
                body?["limit"]?.GetValue<int>() ?? 5000));
            return;
        }
        if (path == "/api/record" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Record(body?["action"]?.GetValue<string>() ?? "get"));
            return;
        }
        if (path == "/api/record/config" && method == "GET") { Json(resp, _hub.RecordingConfig(null)); return; }
        if (path == "/api/record/config" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.RecordingConfig(body));
            return;
        }
        if (path == "/api/device/rename" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.Rename(body?["mac"]?.GetValue<string>() ?? "", body?["alias"]?.GetValue<string>() ?? ""));
            return;
        }
        if (path == "/api/devices/config" && method == "GET") { Json(resp, _hub.DevicesConfig(null)); return; }
        if (path == "/api/devices/config" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.DevicesConfig(body));
            return;
        }
        if (path == "/api/devices/autodetect" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.AutoDetect(body?["action"]?.GetValue<string>() ?? "status"));
            return;
        }
        if (path == "/api/hw/config" && method == "GET") { Json(resp, _hub.HwConfig(null)); return; }
        if (path == "/api/hw/config" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.HwConfig(body));
            return;
        }
        if (path == "/api/hw/var" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            // value default indicates the deletion of the overwrite/unit/change of name
            var hasValue = body?["value"] is JsonValue;
            Json(resp, _hub.HwVar(
                body?["action"]?.GetValue<string>() ?? "",
                body?["name"]?.GetValue<string>() ?? "",
                hasValue ? body!["value"]!.GetValue<string>() : null,
                body?["item"] as JsonObject));
            return;
        }
        if (path == "/api/float/open" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.FloatOpen(body?["id"]?.GetValue<string>()));
            return;
        }
        if (path == "/api/float/open_all" && method == "POST") { Json(resp, _hub.FloatOpenAll()); return; }
        if (path == "/api/float/close_all" && method == "POST") { Json(resp, _hub.FloatCloseAll()); return; }
        if (path == "/api/float/close" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.FloatClose(body?["id"]?.GetValue<string>() ?? ""));
            return;
        }
        if (path == "/api/float/config" && method == "GET") { Json(resp, _hub.FloatConfig(null)); return; }
        if (path == "/api/float/config" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.FloatConfig(body));
            return;
        }
        if (path == "/api/float/lock" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.FloatLock(body?["locked"]?.GetValue<bool>() ?? false));
            return;
        }
        if (path == "/api/web/start" && method == "POST") { Json(resp, _hub.WebStart()); return; }
        if (path == "/api/web/stop" && method == "POST") { Json(resp, _hub.WebStop()); return; }
        // ---- Phase 6: External interface configuration and system information -
        if (path == "/api/apiserver/config" && method == "GET") { Json(resp, _hub.ApiConfig(null)); return; }
        if (path == "/api/apiserver/config" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.ApiConfig(body));
            return;
        }
        if (path == "/api/sysinfo" && method == "GET") { Json(resp, _hub.SysInfoQuery(ctx.Request.QueryString["template"] ?? "")); return; }
        if (path == "/api/sysinfo" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            Json(resp, _hub.SysInfoQuery(body?["template"]?.GetValue<string>() ?? ""));
            return;
        }
        if (path == "/api/shutdown" && method == "POST") { Json(resp, _hub.Shutdown()); return; }
        // Single instance handover: Go here when the old instance window is not found in the second process that is started over and over again, and bring the old example to the bottom Noodles.
        if (path == "/api/activate" && method == "POST") { Json(resp, _hub.Activate()); return; }

        Json(resp, new { error = $"unknown api: {method} {path}" }, 404);
    }

    // ------------------------------------------------------------ Second front-end (remote) authentication

    /// <summary>
    /// P6 capability whitelist (replaces the old path deny list): remote user-role sessions may only reach these
    /// read/control families. Loopback (local admin) and remote admin sessions are unrestricted. Sub-paths that must
    /// stay admin-only are carved out first, so a broad family never grants a sensitive endpoint.
    /// </summary>
    private static bool ApiOpAllowed(string path)
    {
        foreach (var p in AdminOnlyPrefixes)
            if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var p in UserCapablePrefixes)
            if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Machine settings, secrets, exports, and floating-window controls stay admin-only even inside capable families.</summary>
    private static readonly string[] AdminOnlyPrefixes =
    {
        "/api/toolkit/",
        "/api/hw/config", "/api/hw/var", "/api/vrchat/launch", "/api/logs/clear", "/api/logs/dump", "/api/logs/export",
        "/api/devices/config", "/api/devices/autodetect", "/api/devices/batch", "/api/device/block",
        "/api/scan", "/api/record/config", "/api/web/",
    };

    /// <summary>Everything a remote user role may call directly (mobile monitoring, device connect, OSC push control).</summary>
    private static readonly string[] UserCapablePrefixes =
    {
        "/api/remote/",
        "/api/status", "/heartbeat",
        "/api/devices", "/api/device/connect", "/api/device/disconnect", "/api/device/rename", "/api/device/save", "/api/device/unblock",
        "/api/osc/",
        "/api/health", "/api/monitor", "/api/export", "/api/record",
        "/api/logs", "/api/hw", "/api/vrchat", "/api/github", "/api/sysinfo",
    };

    private async Task HandleRemoteApi(HttpListenerContext ctx, string path, RemoteAuth.Session? sess)
    {
        var method = ctx.Request.HttpMethod;
        var resp = ctx.Response;
        // "Whether or not to remote session" determined by client IP: loop = host (remote:false, constant administrator)
        var remote = !RemoteAuth.IsLoopback(RemoteIp(ctx));

        // ---- me: returns 401 without login, where front-end login; returns ring requesting constant admins -
        if (path == "/api/remote/me" && method == "GET")
        {
            if (sess == null) { Json(resp, new { ok = false, error = "unauthorized" }, 401); return; }
            var zone = SourceZone.Of(RemoteIp(ctx));
            Json(resp, new
            {
                ok = true,
                remote,
                authed = true,
                username = sess.Username,
                role = sess.Role,
                admin = sess.Role == "admin",
                tabs = _auth.AllowedTabs(sess),
                sourceZone = zone.ToString().ToLowerInvariant(),
                // P6: an admin still on the default password must be migrated before using remote features.
                mustChangePassword = sess.Role == "admin" && _auth.AdminDefaultPassword,
                adminInitialized = _auth.HasUsers,
            });
            return;
        }

        // ---- login: Username + Password Query Session (HttpOnly ZXZCookie save token; service-end only Hash) - -
        if (path == "/api/remote/login" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            var name = body?["username"]?.GetValue<string>() ?? "";
            var pass = body?["password"]?.GetValue<string>() ?? "";
            // Initialization is a local-admin operation. A remote client must never be able to claim
            // the first administrator simply because LAN access was enabled before setup completed.
            if (!_auth.HasUsers)
            {
                Json(resp, new { ok = false, error = "initialize admin locally", init = true }, 403);
                return;
            }
            var sess2 = _auth.Login(name, pass, ctx.Request.UserAgent ?? "", RemoteIp(ctx), out var token);
            if (sess2 == null) { Json(resp, new { ok = false, error = "bad credentials" }, 401); return; }
            SetSessionCookie(resp, token);
            var zone2 = SourceZone.Of(RemoteIp(ctx));
            Json(resp, new
            {
                ok = true,
                remote,
                authed = true,
                username = sess2.Username,
                role = sess2.Role,
                admin = sess2.Role == "admin",
                tabs = _auth.AllowedTabs(sess2),
                sourceZone = zone2.ToString().ToLowerInvariant(),
                mustChangePassword = sess2.Role == "admin" && _auth.AdminDefaultPassword,
                adminInitialized = true,
            });
            return;
        }

        // ---- logout: Destroy Current Session -
        if (path == "/api/remote/logout" && method == "POST")
        {
            _auth.Logout(GetSessionToken(ctx.Request) ?? "");
            ClearSessionCookie(resp);
            Json(resp, new { ok = true });
            return;
        }

        // The following operations require an administrator (loopback or an authenticated admin session).
        var admin = sess != null && sess.Role == "admin";
        if (!admin) { Json(resp, new { ok = false, error = "forbidden" }, 403); return; }
        // A migrated default-password administrator may only log out or change its own password.
        var defaultPasswordAdmin = remote && _auth.AdminDefaultPassword;
        if (defaultPasswordAdmin
            && !(path == "/api/remote/users" && method == "POST"))
        {
            Json(resp, new { ok = false, error = "password change required" }, 403);
            return;
        }

        if (path == "/api/remote/sessions" && method == "GET")
        {
            var cur = GetSessionToken(ctx.Request);
            Json(resp, new { ok = true, sessions = _auth.Sessions().Select(s => new
            {
                // id = token Hash: The management used it to kick people and the leak could not be used as Cookie
                id = s.TokenHash,
                tokenHint = s.TokenHash.Length > 8 ? s.TokenHash[..8] : s.TokenHash,
                username = s.Username,
                role = s.Role,
                remoteIp = s.RemoteIp,
                created = s.Created,
                lastSeen = s.LastSeen,
                self = RemoteAuth.IsSame(s, cur),
            }).ToArray() });
            return;
        }
        if (path == "/api/remote/sessions/kick" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            // token Hash (id/remote/sessions)
            _auth.Kick(body?["token"]?.GetValue<string>() ?? "");
            Json(resp, new { ok = true });
            return;
        }
        if (path == "/api/remote/users" && method == "GET")
        {
            Json(resp, new { ok = true, users = _auth.Users().Select(RemoteAuth.PublicUser).ToArray() });
            return;
        }
        if (path == "/api/remote/users" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            var tabs = (body?["tabs"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "")
                .Where(t => t.Length > 0).ToList();
            var action = body?["action"]?.GetValue<string>() ?? "";
            var username = body?["username"]?.GetValue<string>() ?? "";
            if (string.Equals(action, "init", StringComparison.OrdinalIgnoreCase) && remote)
            {
                Json(resp, new { ok = false, error = "initialize admin locally" }, 403);
                return;
            }
            // A default-password remote admin may change only its own password; it cannot use the
            // otherwise-admin endpoint for account management before completing migration.
            if (defaultPasswordAdmin && (action != "pass"
                || !string.Equals(username, sess!.Username, StringComparison.OrdinalIgnoreCase)))
            {
                Json(resp, new { ok = false, error = "password change required" }, 403);
                return;
            }
            var list = _auth.Manage(action, username,
                body?["password"]?.GetValue<string>(), body?["role"]?.GetValue<string>(), tabs);
            if (list == null) { Json(resp, new { ok = false, error = "bad request" }, 400); return; }
            if (action is "pass" or "remove" or "role")
            {
                var keep = action == "pass" && string.Equals(username, sess!.Username, StringComparison.OrdinalIgnoreCase)
                    ? GetSessionToken(ctx.Request) is { Length: > 0 } tok ? RemoteAuth.Sha256(tok) : null
                    : null;
                _auth.InvalidateUserSessions(username, keep);
            }
            Json(resp, new { ok = true, users = list.Select(RemoteAuth.PublicUser).ToArray() });
            return;
        }
        if (path == "/api/remote/config" && method == "GET")
        {
            Json(resp, new { ok = true, config = RemoteConfigPayload() });
            return;
        }
        if (path == "/api/remote/config" && method == "POST")
        {
            var body = await ReadJson(ctx.Request);
            var cfg = App.Config.Remote;
            var nextEnabled = cfg.Enabled;
            if (body?["enabled"] is JsonValue ev)
            {
                var next = ev.GetValue<bool>();
                if (next && !_auth.HasUsers)
                {
                    Json(resp, new { ok = false, error = "initialize admin first", config = RemoteConfigPayload() }, 400);
                    return;
                }
                if (next && !cfg.Enabled) App.Log.Info(LogText.L("log.web.lan_changed", "ON"));
                if (!next && cfg.Enabled) App.Log.Info(LogText.L("log.web.lan_changed", "OFF"));
                nextEnabled = next;
            }
            if (body?["idleMinutes"] is JsonValue iv && iv.TryGetValue<int>(out var idl)) cfg.IdleMinutes = Math.Max(0, idl);
            if (body?["scheme"] is JsonValue sv && sv.TryGetValue<string>(out var requestedScheme))
            {
                requestedScheme = requestedScheme.Trim().ToLowerInvariant();
                if (requestedScheme is not ("http" or "https"))
                {
                    Json(resp, new { ok = false, error = "scheme must be http or https", config = RemoteConfigPayload() }, 400);
                    return;
                }
                App.Config.Web.Scheme = requestedScheme;
            }
            if (body?["certificateThumbprint"] is JsonValue tv && tv.TryGetValue<string>(out var thumbprint))
                App.Config.Web.CertificateThumbprint = NormalizeThumbprint(thumbprint);
            if (body?["requireHttpsForRemote"] is JsonValue rhv && rhv.TryGetValue<bool>(out var requireHttps))
                App.Config.Web.RequireHttpsForRemote = requireHttps;
            if (body?["wanEnabled"] is JsonValue wv && wv.TryGetValue<bool>(out var wantWan))
            {
                if (wantWan && !cfg.WanEnabled)
                {
                    // P6: WAN requires an initialized, non-default, strong admin password — otherwise the caller
                    // must run the password dialog first; saving stays blocked until the gate passes.
                    if (!_auth.WanReady)
                    {
                        App.Log.Warn(LogText.L("log.web.wan_blocked_weak_password"));
                        Json(resp, new { ok = false, error = "weak admin password", config = RemoteConfigPayload() }, 400);
                        return;
                    }
                    cfg.WanEnabled = true;
                    App.Log.Warn(LogText.L("log.web.wan_changed", "ON"));
                }
                else if (!wantWan && cfg.WanEnabled)
                {
                    cfg.WanEnabled = false;
                    App.Log.Info(LogText.L("log.web.wan_changed", "OFF"));
                }
            }
            cfg.Enabled = nextEnabled;
            App.Config.Save();
            Json(resp, new { ok = true, config = RemoteConfigPayload() });
            return;
        }

        Json(resp, new { ok = false, error = "unknown api" }, 404);
    }

    private object RemoteConfigPayload() => new
    {
        enabled = App.Config.Remote.Enabled,
        wanEnabled = App.Config.Remote.WanEnabled,
        effectiveEnabled = !App.SafeMode && App.Config.Remote.Enabled,
        effectiveWanEnabled = !App.SafeMode && App.Config.Remote.WanEnabled,
        safeMode = App.SafeMode,
        scheme = _scheme,
        url = $"{_scheme}://127.0.0.1:{Port}/webui/",
        certificateThumbprint = App.Config.Web.CertificateThumbprint,
        requireHttpsForRemote = App.Config.Web.RequireHttpsForRemote,
        restartRequired = !string.Equals(_scheme, App.WebScheme, StringComparison.OrdinalIgnoreCase),
        idleMinutes = App.Config.Remote.IdleMinutes,
        // P6 single listener: one port serves local and remote; the LAN/WAN switches below decide who may connect.
        port = Port,
        boundAll = _boundAll,
        adminInitialized = _auth.HasUsers,
        adminDefaultPassword = _auth.AdminDefaultPassword,
        wanReady = _auth.WanReady,
        note = _boundAll
            ? (_scheme == "https" ? "HTTPS requires an administrator-preconfigured HTTP.sys SSL certificate binding." : "")
            : $"Loopback-only fallback: reserve the URL prefix (netsh http add urlacl url={_scheme}://+:{Port}/ user=Users)",
    };

    /// <summary>
    /// Write session Cookie. Elements of sustainability:
    ///   • Must take MaxXQ1QXZ - without "session Cookie" and cut a App on the phone, and the browser recovers backstage labels
    ///     They'll be discarded, and they'll be "Crunch Tab and drop the login."
    ///   • SameSite=Lax (not Strict) — Strict does not send Cookie when jumping in from an external link/bookmark;
    ///   • HttpOnly: JS cannot read token, preventing scripts from being injected.
    /// </summary>
    private void SetSessionCookie(HttpListenerResponse resp, string token)
    {
        // The idle maximum is the life of Cookie; 0 (never expired) is given 30 days to avoid writing into session Cookie
        var minutes = App.Config.Remote.IdleMinutes;
        var maxAge = minutes > 0 ? minutes * 60 : 30 * 24 * 3600;
        var secure = _scheme == "https" ? "; Secure" : "";
        resp.Headers["Set-Cookie"] =
            $"{RemoteAuth.CookieName}={token}; Path=/; HttpOnly; SameSite=Lax; Max-Age={maxAge}{secure}";
    }

    private void ClearSessionCookie(HttpListenerResponse resp)
    {
        var secure = _scheme == "https" ? "; Secure" : "";
        resp.Headers["Set-Cookie"] = $"{RemoteAuth.CookieName}=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0{secure}";
    }

    // ------------------------------------------------------------ Tools

    static async Task<JsonObject?> ReadJson(HttpListenerRequest req)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            var text = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(text)) return null;
            return JsonNode.Parse(text) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    static void Json(HttpListenerResponse resp, object? obj, int code = 200)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
        resp.StatusCode = code;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        resp.OutputStream.Write(bytes, 0, bytes.Length);
        resp.Close();
    }

    public void Dispose()
    {
        VrcToolkitService.ScanProgressChanged -= OnToolkitScanProgress;
        VrcToolkitService.CancelCacheScan();
        Stop();
        _cts.Dispose();
        try { _listener.Close(); } catch { }
    }
}
