using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeartRateMonitor;

/// <summary> program running configuration (config.json). </summary>
public class AppConfig
{
    /// <summary>Config schema version (P0): bumped when the layout changes; Load upgrades older files in place.
    /// 1 = implicit legacy layout (no field); 2 = current schema with atomic saves and DataDir-owned auxiliary files.</summary>
    public int SchemaVersion { get; set; } = 2;

    public AppSection App { get; set; } = new();
    public OscSection Osc { get; set; } = new();
    public HeartRateSection HeartRate { get; set; } = new();
    public WebhookSection Webhook { get; set; } = new();
    public LogsSection Logs { get; set; } = new();
    public WebSection Web { get; set; } = new();
    public UiSection Ui { get; set; } = new();
    public DevicesSection Devices { get; set; } = new();
    public HealthSection Health { get; set; } = new();
    public HwSection Hw { get; set; } = new();
    public ApiSection Api { get; set; } = new();
    public RemoteSection Remote { get; set; } = new();
    public RecordingSection Recording { get; set; } = new();

    /// <summary> frontend view preferences (E2 round 36: rearend mirrors for localStorage preferences in layout/card order/folding/ column width, etc.)
    /// Key = localStorage keyname (hrm-*/ hb-*), value = string. Backfills are pulled on front-end start-up, and you write it in a shaking-proof batch. </summary>
    public Dictionary<string, string> Prefs { get; set; } = new();

    public string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    public string WebhookPath = Path.Combine(AppContext.BaseDirectory, "config_webhook.json");

    /// <summary>XQ1QXZ: Properties name snake_case (keys maintained as they are) are not sensitive when reading. </summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// The old version of <summary> is PascalCase (without underlined) and the naming policy does not match and is read-only; the first Save() is automatically converted to snake_case. </summary>
    private static readonly JsonSerializerOptions LegacyOpts = new() { PropertyNameCaseInsensitive = true };

    public static AppConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<AppConfig>(text, IsLegacyLayout(text) ? LegacyOpts : JsonOpts);
                if (cfg != null)
                {
                    cfg.ConfigPath = path;
                    // P0: upgrade older files in place (currently a stamp only; add real migrations before bumping further).
                    if (cfg.SchemaVersion < 2) cfg.SchemaVersion = 2;
                    // P6: migrate the removed remote.host/port/loopbackOnly keys onto the source-zone model.
                    MigrateRemoteLegacy(text, cfg);
                    return cfg;
                }
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[config] load fail: {e.Message}");
        }
        var fallback = new AppConfig { ConfigPath = path };
        return fallback;
    }

    /// <summary>
    /// P6 single-listener migration: remote.host/port/loopbackOnly no longer exist. Old loopbackOnly=true meant
    /// "no remote access at all", which maps to Enabled=false; loopbackOnly=false + enabled=true keeps Enabled=true
    /// (LAN sources allowed). WanEnabled always starts false. The rewritten config drops the legacy keys on next Save.
    /// </summary>
    static void MigrateRemoteLegacy(string text, AppConfig cfg)
    {
        try
        {
            var root = JsonNode.Parse(text) as JsonObject;
            var rem = (root?["remote"] ?? root?["Remote"]) as JsonObject;
            if (rem is null) return;
            if (!rem.Any(p => p.Key.Equals("host", StringComparison.OrdinalIgnoreCase)
                           || p.Key.Equals("port", StringComparison.OrdinalIgnoreCase)
                           || p.Key.Equals("loopbackOnly", StringComparison.OrdinalIgnoreCase)
                           || p.Key.Equals("loopback_only", StringComparison.OrdinalIgnoreCase))) return;
            var loopbackNode = rem.FirstOrDefault(p => p.Key.Equals("loopbackOnly", StringComparison.OrdinalIgnoreCase)
                                                    || p.Key.Equals("loopback_only", StringComparison.OrdinalIgnoreCase)).Value;
            if (loopbackNode is JsonValue lv && lv.TryGetValue<bool>(out var loopback) && loopback)
                cfg.Remote.Enabled = false;
            // No logging here: AppConfig.Load runs before App.Log exists; the migrated shape is visible in the next Save.
        }
        catch
        {
            /* A malformed legacy section must not block startup; defaults apply. */
        }
    }

    private static bool IsLegacyLayout(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && !doc.RootElement.TryGetProperty("heart_rate", out _)
                && doc.RootElement.TryGetProperty("HeartRate", out _);
        }
        catch { return false; }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, JsonOpts);
        try
        {
            // P0: atomic write - serialize to a temp file, keep the previous file as .bak, then replace in one step.
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(ConfigPath))
            {
                try { File.Replace(tmp, ConfigPath, ConfigPath + ".bak"); }
                catch (IOException) { File.Move(tmp, ConfigPath, true); } // Replace needs the same volume; fall back to an overwrite move.
            }
            else
            {
                File.Move(tmp, ConfigPath);
            }
        }
        catch (Exception e)
        {
            // Fall back to a direct write when the atomic path fails (locked file, read-only directory, ...).
            try { File.WriteAllText(ConfigPath, json); }
            catch (Exception e2) { Console.Error.WriteLine($"[config] save fail: {e.Message} / {e2.Message}"); }
        }
    }

    /// <summary> currently configured JSON snapshot (take one first at the writing path entrance, apply it and match it, and skip the writing disk without change). </summary>
    public string SnapshotJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary> compares with snapshots: the disk is set down only when there are substantial changes and returns whether the disk is actually written (not written = the front end should not have been "saved"). </summary>
    public bool SaveIfChanged(string snapshot)
    {
        if (JsonSerializer.Serialize(this, JsonOpts) == snapshot) return false;
        Save();
        return true;
    }

    public class AppSection
    {
        public int Debug { get; set; } = 0;

        /// <summary> allows multiple instances to run at the same time (default false: repeated startup will go to run instance). </summary>
        public bool AllowMultiInstance { get; set; }

        /// <summary>Login autostart (P1): selected registration methods - "task" (Task Scheduler), "run" (HKCU Run), "startup" (Startup folder).
        /// Users may pick any combination; every registration launches with --gui --autostart and dedupes through the single-instance guard.</summary>
        public List<string> AutoStartMethods { get; set; } = new();

        /// <summary>Autostart launches silently (P1): engine + tray only, no frontend window (adds --silent to the registration).</summary>
        public bool AutoStartSilent { get; set; } = true;

        /// <summary>Startup update check (P3): compares the GitHub latest release against the embedded manifest.</summary>
        public bool UpdateCheck { get; set; } = true;

        /// <summary>Skipped release identity (P3): tag and name of the latest release the user chose to ignore.</summary>
        public string SkippedReleaseTag { get; set; } = "";

        /// <summary>Skipped release display name (P3).</summary>
        public string SkippedReleaseName { get; set; } = "";
    }

    public class OscSection
    {
        public string Ip { get; set; } = "127.0.0.1";
        public string Port { get; set; } = "9000";
        public string Address { get; set; } = "/chatbox/input";
        public string IntervalMs { get; set; } = "1000";
        public string ReceivePort { get; set; } = "9001";
        /// <summary> main program automatically connects and starts the OSC template transfer. </summary>
        public bool AutoStart { get; set; }
        public string Template { get; set; } = "HeartBeat {BPM0} BPM\nCPU: {CPU_PRODUCT_NAME} {CPU_USAGE}%\nGPU: {GPU_PRODUCT_NAME0}\nVRAM: {VRAM_USED0} of {VRAM_TOTAL0}\nRAM: {RAM_USED} / {RAM_TOTAL}\nFocus: {FOCUS_WINDOW_TITLE}";
    }

    public class HeartRateSection
    {
        /// <summary> saved (remember) the list of devices MAC. </summary>
        public List<string> Devices { get; set; } = new();
        /// <summary> blocked list of devices MAC (not shown anymore). </summary>
        public List<string> Blocked { get; set; } = new();
        public string DisplaySource { get; set; } = "平均";
        public WindowSection Window { get; set; } = new();
    }

    public class WindowSection
    {
        public bool Visible { get; set; }
        public bool Locked { get; set; }
        public string Geometry { get; set; } = "200x80+100+100";
        public string UnlockedColor { get; set; } = "#00FF00";
        public string LockedColor { get; set; } = "#FF6600";
        public string Format { get; set; } = "❤️{bpm}";
        public string? ImagePath { get; set; }
        /// <summary> main floating window data source: "average" or device MAC. </summary>
        public string Source { get; set; } = "平均";
        /// <summary> values refresh interval (ms., 0 = every heart rate notification). </summary>
        public int RefreshMs { get; set; } = 200;
        /// <summary> specifies data sources by window: Window identification → "average"/MAC. Unconfigured windows use the window identifier itself as a source. </summary>
        public Dictionary<string, string> Sources { get; set; } = new();
        /// <summary>Per-device floating-window logical size and screen position.</summary>
        public Dictionary<string, string> Geometries { get; set; } = new();
    }

    public class WebhookSection
    {
        public bool Enabled { get; set; }
    }

    public class LogsSection
    {
        public bool AutoDumpEnabled { get; set; } = true;
        public string AutoDumpIntervalMin { get; set; } = "30";
        public string DumpDir { get; set; } = "logs/auto";

        /// <summary> Diagnosis Trace Level: Only opened here and will be effective for the next start (asymmetric to App.TraceEnabled at start-up). </summary>
        public bool TraceEnabled { get; set; }
    }

    public class WebSection
    {
        /// <summary> local Web UI port (bound 127.0.0.1). </summary>
        public int Port { get; set; } = 9460;
        /// <summary>HTTP.sys listener scheme: http or https.</summary>
        public string Scheme { get; set; } = "http";
        /// <summary>HTTPS certificate thumbprint in CurrentUser/My or LocalMachine/My.</summary>
        public string CertificateThumbprint { get; set; } = "";
        /// <summary>Reject non-loopback HTTP requests even when remote access is configured.</summary>
        public bool RequireHttpsForRemote { get; set; } = true;
        /// When <summary> starts Web service, it automatically opens the built-in Web UI window (hrmX-webui.exe; if missing, back system browser). </summary>
        public bool OpenBrowser { get; set; } = true;
    }

    /// <summary> Second Frontend (telephone Web): Same set of UI as the local one, but with an additional layer of local authentication (user/meeting/role). </summary>
    public class RemoteSection
    {
        /// <summary>Remote (non-loopback) access switch: private-network sources may sign in when enabled; loopback stays the local admin without login.</summary>
        public bool Enabled { get; set; }
        /// <summary>WAN (public-source) switch, default off. Enabling requires an initialized, non-default, strong admin password and an explicit risk confirmation; old admin sessions are invalidated after the password change.</summary>
        public bool WanEnabled { get; set; }
        /// <summary> session expired idle (minutes, 0 = never expired). </summary>
        public int IdleMinutes { get; set; } = 240;
    }

    /// <summary>XQ1QXZZZServer:/heartbeat, WebSocket Downline Command, WebHook Trigger (with Web UI port). </summary>
    public class ApiSection
    {
        /// <summary> enabled external interface (/ heartbeat and WebSocket downline command). </summary>
        public bool Enabled { get; set; } = true;
        /// <summary> access tokens: token= or XXQ3QXZ head; empty = not verified (only return to loop canda). </summary>
        public string Token { get; set; } = "";
        /// <summary>XQ1QXZ Timeback system information (like OSC). </summary>
        public bool PushSysInfo { get; set; } = false;
        /// <summary> information return interval (ms). </summary>
        public int PushIntervalMs { get; set; } = 2000;
        /// <summary> returns the name of the system information variable (empty = all). </summary>
        public List<string> SysInfoVars { get; set; } = new();
        /// <summary>WebHook heart rate event triggers throttle (ms). </summary>
        public int WebhookThrottleMs { get; set; } = 1000;
    }

    public class RecordingSection
    {
        public bool Recording { get; set; } = true;
        public List<string> Backends { get; set; } = new() { "sqlite", "jsonl", "csv" };
        public bool Avatar { get; set; } = true;
        public bool Vrchat { get; set; } = true;
        public bool Devices { get; set; } = true;
        public bool HeartRate { get; set; } = true;
        public bool Hardware { get; set; } = true;
        public RecordRetention RetentionDays { get; set; } = new();
    }

    public class RecordRetention
    {
        public int Avatar { get; set; }
        public int Vrchat { get; set; }
        public int Devices { get; set; }
        public int HeartRate { get; set; }
        public int Hardware { get; set; }
    }

    /// <summary> interface configuration (Web frontend). </summary>
    public class UiSection
    {
        /// <summary>Language identifier: zh-tw / zh-hk / yue-hk / zh-cn / en / ja / es / ko / de / fr. Empty values are resolved from the system language and saved at startup.</summary>
        public string Lang { get; set; } = "";
        /// <summary>
        /// Theme fold identification: dark / light / forest / sunset / custom.
        /// Web frontend Mode + Palette calculates two-dimensional folding for backward compatibility and external readability.
        /// </summary>
        public string Theme { get; set; } = "dark";
        /// <summary> Shading Mode (Web Frontend): system (following Windows)/dark/light. </summary>
        public string Mode { get; set; } = "system";
        /// <summary> colour (Web frontend): system (following Windows emphasis) / default / forest / sunset / custom </summary>
        public string Palette { get; set; } = "system";
        /// <summary>Custom theme primary color (#RRGGBB).</summary>
        public string Primary { get; set; } = "#5B9DFF";
        /// <summary>Custom theme accent color (#RRGGBB).</summary>
        public string Accent { get; set; } = "#8B5CF6";
        /// <summary>When enabled, background and panel colors are derived from Primary and the effective light/dark mode.</summary>
        public bool Solid { get; set; }
        /// <summary>Custom theme background color (#RRGGBB; ignored in solid mode).</summary>
        public string Bg { get; set; } = "#14171B";
        /// <summary>Custom theme panel color (#RRGGBB; ignored in solid mode).</summary>
        public string Panel { get; set; } = "#1C2128";
        /// <summary>Body font-family CSS value.</summary>
        public string FontFamily { get; set; } = "'Segoe UI', 'Microsoft YaHei', system-ui, -apple-system, sans-serif";
        /// <summary>Monospace font-family CSS value.</summary>
        public string MonoFontFamily { get; set; } = "Consolas, 'Cascadia Mono', monospace";
        /// <summary>Control corner radius in pixels (0–65536). The slider covers 0–16; larger values can be entered manually. Windows and menus add four pixels.</summary>
        public int CornerRadius { get; set; } = 2;
        /// <summary>Interface density multiplier (0.1–65536). The slider covers 0.5–1.4 and adjusts control padding and minimum height.</summary>
        public double Density { get; set; } = 1.0;
        /// <summary> global animated switch (false = turn off the entire station transition and key frames in exchange for the most direct response). </summary>
        public bool Animations { get; set; } = true;

        /// The behaviour of <summary> to close the front window: ask (Query)/ exit (Quitation Program)/ tray (Minimalize to tray). </summary>
        public string CloseAction { get; set; } = "ask";

        /// The <summary> sidebar brand text template (} place replacement real time value; empty = default application name). </summary>
        public string Brand { get; set; } = "";
    }

    /// <summary> devices scan/ sort/reconnect (Phase 4). </summary>
    public class DevicesSection
    {
        /// Continuous scanning of <summary>: no time-consuming, back-to-back driven by Windows radio (default open, negligible utility). </summary>
        public bool ContinuousScan { get; set; } = true;
        /// <summary>XUI Refresher throttle (ms): The same device is reported only once in this window and is protected against high-speed fresh shivering. </summary>
        public int RefreshThrottleMs { get; set; } = 400;
        /// <summary> device alias: MAC→ Display name (e.g. Apple Watch ZXZ Hand). </summary>
        public Dictionary<string, string> Aliases { get; set; } = new();
        /// <summary> automatic reconnection after disconnection. </summary>
        public bool AutoReconnect { get; set; } = true;
        /// <summary> repeat attempt interval (sec). </summary>
        public int ReconnectIntervalSec { get; set; } = 15;
        /// <summary> relinquishment time limit (minutes). </summary>
        public int ReconnectGiveUpMin { get; set; } = 30;
        /// <summary>XQ1QXZ threshold for decay: rssi| + reference value exceeding this number is judged to be weak in signal and printing log. </summary>
        public int RssiWeakThreshold { get; set; } = 100;
        /// <summary>RSSI is a very weak threshold: the decline above this is judged to be very weak (the list indicates red). </summary>
        public int RssiCriticalThreshold { get; set; } = 110;
        /// Automatically connects the device (LastConnected; old configuration returns History first entry) with the last session still connected after <summary> starts. </summary>
        public bool AutoConnect { get; set; }
        /// <summary> auto-detection: The <summary> is linked in a combined sequence, and the life-centre characteristic is to maintain the connection. </summary>
        public bool AutoDetect { get; set; }
        /// <summary> automatically detects the maximum waiting (sec) per device. </summary>
        public int AutoDetectTimeoutSec { get; set; } = 12;
        /// <summary> historically connected devices (MAC in order of recent connection). </summary>
        public List<string> History { get; set; } = new();
        /// <summary> is still connected at the end of the last session (MAC): DisconnectAll snapshot before normal exit, all automatically connected at startup. </summary>
        public List<string> LastConnected { get; set; } = new();
    }

    /// <summary> health status determination (Phase 3). </summary>
    public class HealthSection
    {
        /// <summary> static reference heart rate (calibration, 0 = not calibrated). </summary>
        public double RestingBpm { get; set; }
        /// <summary> static heart rate standard deviation (from calibration). </summary>
        public double RestingSd { get; set; }
        /// <summary> calibration time stamp (ISO 8601). </summary>
        public string CalibratedAt { get; set; } = "";
        /// <summary> sleep determination threshold: below RestingBpm* This coefficient is long and inactive. </summary>
        public double SleepFactor { get; set; } = 0.88;
        /// <summary> active determination threshold: higher than RestingBpm* </summary>
        public double ActiveFactor { get; set; } = 1.15;
        /// <summary> excitement determination threshold: higher than RestingBpm* </summary>
        public double ExcitedFactor { get; set; } = 1.4;
        /// <summary> heart rate fluctuations warning threshold (neighbored sample BPM difference). </summary>
        public int SpikeDelta { get; set; } = 25;
        /// <summary> writes about health status in SQLite. </summary>
        public bool Record { get; set; } = true;
    }

    /// <summary> hardware variable engine (Phase 5). </summary>
    public class HwSection
    {
        /// <summary> collection interval (ms). </summary>
        public int IntervalMs { get; set; } = 1000;
        /// <summary> output float (false = rounded to integer string). </summary>
        public bool UseFloat { get; set; } = true;
        /// <summary> floating point decimal places. </summary>
        public int Decimals { get; set; } = 1;
        /// Whether <summary> values are rounded (false = cut). </summary>
        public bool Round { get; set; } = true;
        /// <summary>XQ1QXZ server (for TIME_NTP variables). </summary>
        public string NtpServer { get; set; } = "pool.ntp.org";
        /// <summary> variable unit overwrite: variable name → unit suffix (e.g. CPU_USAGE → "% " ). </summary>
        public Dictionary<string, string> Units { get; set; } = new();
        /// <summary> manually overwrites the variable name → fixed value (in preference to the collected value). </summary>
        public Dictionary<string, string> Overrides { get; set; } = new();
        /// <summary> variable changed name: original name → new name (new name available at the same time). </summary>
        public Dictionary<string, string> Renames { get; set; } = new();
        /// <summary> custom variable. </summary>
        public List<CustomVar> Custom { get; set; } = new();
    }

    /// <summary> customises the definition of variables (calculation/mix/register/command line). </summary>
    public class CustomVar
    {
        public string Name { get; set; } = "";
        /// <summary>XQ1QXZ (four operations)/concat (compelling)/regex (repeated)/cmd (command line stdout). </summary>
        public string Kind { get; set; } = "expr";
        /// <summary>Expression content; may contain variable placeholders in braces.</summary>
        public string Expr { get; set; } = "";
        /// <summary>Regular expression used when `Kind` is `regex`; the first capture group becomes the value.</summary>
        public string Pattern { get; set; } = "";
        /// <summary> unit suffix. </summary>
        public string Unit { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }
}
