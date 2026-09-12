using System.Collections.Concurrent;
using HeartRateMonitor.Ble;
using HeartRateMonitor.Health;
using HeartRateMonitor.Osc;
using HeartRateMonitor.SysInfo;
using HeartRateMonitor.Webhook;

namespace HeartRateMonitor;

/// <summary>Global singleton context for configuration, logging, services, and heart-rate state.</summary>
public static class App
{
    public static string BaseDir = AppContext.BaseDirectory;
    public static AppConfig Config = null!;
    public static Logger Log = null!;
    public static bool DebugMode;

    /// <summary>
    /// Safe mode (started with --safemode): reads the existing configuration only and does not
    /// automatically perform any configured action (no automatic device connection, detection,
    /// or reconnection). This provides a recovery path for invalid user configurations—for example,
    /// when AutoConnect points to a broken device and causes startup to hang—so the UI can be opened
    /// safely to correct the configuration.
    /// </summary>
    public static bool SafeMode;

    /// <summary>
    /// Diagnostic Trace level, captured once from Config.Logs.TraceEnabled at startup.
    /// Runtime configuration changes do not update this field, preserving the "effective after restart"
    /// semantics and preventing intermittent trace logging in a partially enabled state.
    /// </summary>
    public static bool TraceEnabled;

    /// <summary>Web service port after startup normalization; used by the shell and tray to open the UI.</summary>
    public static int WebPort = 9460;

    public static string WebScheme => (Config?.Web?.Scheme ?? "http").Trim().ToLowerInvariant();

    public static string WebUrl(string path = "/webui/", int? port = null)
        => $"{WebScheme}://127.0.0.1:{port ?? WebPort}/{path.TrimStart('/')}";

    /// <summary>Web service host, created by both GUI and CLI for the `web start|stop` command.</summary>
    public static Web.WebHost? Web;

    /// <summary>Shared business core used by command implementations; GUI and CLI each create one.</summary>
    public static Core.AppHub? Hub;

    /// <summary>Whether a WinForms message loop is present (tray/main form). false means a CLI process where UI commands such as floating windows are unavailable.</summary>
    public static bool GuiHosted;

    /// <summary>Result of the P2 component version check: true = all four executables match the embedded
    /// manifest, false = mismatch, null = not checked yet. Install integrity only; never an update signal.</summary>
    public static bool? ComponentsOk;

    public static SysInfoService SysInfo = null!;
    public static BleManager Ble = null!;
    public static OscService Osc = null!;
    public static WebhookManager Webhooks = null!;
    /// <summary>Device registry for identifier caching, sorting, aliases, and reconnection.</summary>
    public static DeviceRegistry Devices = null!;
    /// <summary>Health-state evaluation based on heart rate and OSC posture.</summary>
    public static HealthService Health = null!;

    /// <summary>Current primary displayed heart rate (bpm; 0 means unavailable).</summary>
    public static volatile int CurrentBpm;
    public static DateTime StartTime = DateTime.Now;

    /// <summary>Application version read from the assembly.</summary>
    public static string Version => typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    /// <summary>
    /// Currently effective language for engine-side console and CLI text. Values match config.ui.lang:
    /// zh-tw / zh-hk / yue-hk / zh-cn / en / ja / es / ko / de / fr. Resolved once at startup
    /// (config → system language → en), and updated immediately when ui.lang changes at runtime
    /// (see AppHub.ApplySettings).
    /// </summary>
    public static string Lang = "zh-cn";

    /// <summary>
    /// User data directory containing config.json, logs, the database, and exports.
    /// Defaults to %AppData%\HeartRateMonitor; settings can switch it to the application directory
    /// or a custom directory (the selection is stored in data_location.txt beside the executable).
    /// Assemblies, engine DLLs, and webui static resources remain in the executable directory and
    /// are not moved with the data directory.
    /// </summary>
    public static string DataDir = AppContext.BaseDirectory;

    /// <summary>Runtime executable directory, used as the static-resource anchor and unaffected by data-directory changes.</summary>
    public static readonly string ExeDir = AppContext.BaseDirectory;

    /// <summary>Accumulated log buffer used for dumps.</summary>
    public static ConcurrentQueue<string> LogBuffer = new();
}
