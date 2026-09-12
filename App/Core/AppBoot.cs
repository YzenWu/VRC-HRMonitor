using System.Text;
using HeartRateMonitor.Ble;
using HeartRateMonitor.Health;
using HeartRateMonitor.Osc;
using HeartRateMonitor.SysInfo;
using HeartRateMonitor.Webhook;

namespace HeartRateMonitor.Core;

/// <summary>
/// Shared startup sequence: resolve the data directory, register encodings, resolve the language, load configuration, initialize logging, and construct services.
/// Used by all three entry points: HeartRateMonitor.exe (GUI), HeartRateMonitor.exe --cli, and hrmcli.exe.
/// Only constructs services without starting them; each entry point controls its own service lifecycle.
/// </summary>
public static class AppBoot
{
    /// <summary>Data-directory locator beside the executable. Its absolute path takes precedence when present and is updated from the storage-location setting.
    /// Falls back to %AppData%\HeartRateMonitor when the file is missing or invalid.</summary>
    public static readonly string DataLocFile = Path.Combine(AppContext.BaseDirectory, "data_location.txt");

    /// <summary>Legacy user-data files migrated from the executable directory when switching to a new data directory for the first time.</summary>
    static readonly string[] MigratableFiles =
    {
        "config.json", "config_webhook.json",
        "config_remote_users.json", "config_remote_sessions.json",
        "hrm.db",
    };

    public static void Init(string[] args)
    {
        // The data directory defaults to %AppData%\HeartRateMonitor and can be overridden by data_location.txt beside the executable.
        ResolveDataDir();

        // Support GBK and OEM encodings used by wmic and systeminfo output.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        App.Config = AppConfig.Load(Path.Combine(App.DataDir, "config.json"));
        // P0: auxiliary config files belong to the data directory (the field default pointed at the program directory,
        // which broke webhook persistence for custom data locations).
        App.Config.WebhookPath = Path.Combine(App.DataDir, "config_webhook.json");
        App.DebugMode = args.Any(a => string.Equals(a, "--debug", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, "-d", StringComparison.OrdinalIgnoreCase)) || App.Config.App.Debug == 1;
        // Safe mode skips all automatic startup actions and blocks manual external actions.
        App.SafeMode = args.Any(a => string.Equals(a, "--safemode", StringComparison.OrdinalIgnoreCase));
        // Read the trace level from configuration only at startup; setting changes require a restart.
        App.TraceEnabled = App.Config.Logs.TraceEnabled
            || args.Any(a => string.Equals(a, "--trace", StringComparison.OrdinalIgnoreCase));
#if DEBUG
        // Debug builds, selected with -c Debug, force verbose logging and a console.
        App.DebugMode = true;
#endif

        // Resolve config.ui.lang, then the system language, then English; persist the resolved value when missing or unsupported.
        ResolveLang();

        App.Log = new Logger(Path.Combine(App.DataDir, "logs"));
        App.Log.Info(LogText.L("log.boot.banner", App.DebugMode ? "DEBUG" : "RELEASE"));
        if (App.SafeMode) App.Log.Warn(LogText.L("log.boot.safemode"));
        if (App.TraceEnabled) App.Log.Info(LogText.L("log.boot.trace_on"));
        App.Log.Info(LogText.L(OscEngine.Available ? "log.boot.osc_loaded" : "log.boot.osc_missing"));

        // Construct services; each mode is responsible for starting them.
        App.SysInfo = new SysInfoService();
        App.Ble = new BleManager();
        App.Osc = new OscService();
        App.Webhooks = new WebhookManager(App.Config.WebhookPath);
        App.Devices = new DeviceRegistry();
        App.Health = new HealthService();
    }

    /// <summary>
    /// Resolve the data directory from <see cref="DataLocFile"/>, falling back to %AppData%\HeartRateMonitor.
    /// Point App.BaseDir to the same location, preserving its legacy meaning as the user-data directory.
    /// Static resources such as assemblies, webui, and osc_engine.dll remain under App.ExeDir.
    /// Perform a one-time migration when the new directory is empty and legacy data exists beside the executable.
    /// </summary>
    static void ResolveDataDir()
    {
        var exe = App.ExeDir;
        var dir = DefaultDataDir();
        string? chosen = null;
        try
        {
            if (File.Exists(DataLocFile))
            {
                var v = File.ReadAllText(DataLocFile).Trim();
                if (v.Length > 0)
                {
                    chosen = v;
                    dir = Path.IsPathRooted(v) ? v : Path.Combine(exe, v);
                }
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[data] data_location.txt 读取失败: {e.Message}");
        }

        try { Directory.CreateDirectory(dir); }
        catch (Exception e)
        {
            // Fall back to AppData if the custom directory is unavailable because it was deleted or access is denied; retain the locator so the user can repair it.
            Console.Error.WriteLine($"[data] 目录不可用 ({dir}): {e.Message}，回退 {DefaultDataDir()}");
            dir = DefaultDataDir();
            Directory.CreateDirectory(dir);
        }

        App.DataDir = dir;
        App.BaseDir = dir;

        // One-time migration when the data directory lacks config.json but the legacy executable directory contains it.
        if (!string.Equals(Path.GetFullPath(dir), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)
            && !File.Exists(Path.Combine(dir, "config.json")))
        {
            MigrateLegacy(exe, dir);
        }

        // Keep the logs and exports directories with the selected data directory.
        if (chosen == null)
        {
            try { File.WriteAllText(DataLocFile, dir); } catch { } // Write the default once so users can see the current location.
        }
    }

    public static string DefaultDataDir()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeartRateMonitor");

    /// <summary>Set the data directory from the storage-location settings page, accepting __appdata__, __exedir__, or an absolute path.
    /// Write data_location.txt beside the executable for the next startup, update the in-memory path, and create the directory immediately.
    /// Returns a success flag and an error message.</summary>
    public static (bool ok, string error) SetDataLocation(string v)
    {
        v = (v ?? "").Trim();
        string dir;
        switch (v)
        {
            case "__appdata__": dir = DefaultDataDir(); break;
            case "__exedir__": dir = App.ExeDir; break;
            default: dir = Path.IsPathRooted(v) ? v : Path.Combine(App.ExeDir, v); break;
        }
        try { Directory.CreateDirectory(dir); }
        catch (Exception e) { return (false, e.Message); }
        try { File.WriteAllText(DataLocFile, dir); }
        catch (Exception e) { return (false, e.Message); }
        App.DataDir = dir;
        App.BaseDir = dir;
        return (true, "");
    }

    static void MigrateLegacy(string from, string to)
    {
        try
        {
            var copied = 0;
            foreach (var f in MigratableFiles)
            {
                var src = Path.Combine(from, f);
                var dst = Path.Combine(to, f);
                if (File.Exists(src) && !File.Exists(dst))
                {
                    File.Copy(src, dst);
                    copied++;
                }
            }
            // Move the entire logs directory.
            var srcLogs = Path.Combine(from, "logs");
            var dstLogs = Path.Combine(to, "logs");
            if (Directory.Exists(srcLogs) && !Directory.Exists(dstLogs))
            {
                Directory.CreateDirectory(dstLogs);
                foreach (var f in Directory.EnumerateFiles(srcLogs))
                    File.Copy(f, Path.Combine(dstLogs, Path.GetFileName(f)));
            }
            if (copied > 0 || Directory.Exists(dstLogs))
                Console.WriteLine($"[data] 已从程序目录迁移旧数据到 {to}（{copied} 个文件）");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[data] 旧数据迁移失败: {e.Message}");
        }
    }

    /// <summary>Resolve the language from a supported config.ui.lang value or the system UI language.
    /// Fall back to English for unsupported languages and persist every result derived without explicit configuration.
    /// The supported set matches the frontend, including Hong Kong Traditional Chinese and Cantonese.</summary>
    public static void ResolveLang()
    {
        var cfgLang = (App.Config.Ui.Lang ?? "").Trim().ToLowerInvariant();
        var supported = new[] { "zh-tw", "zh-hk", "yue-hk", "zh-cn", "en", "ja", "es", "ko", "de", "fr" };
        if (supported.Contains(cfgLang))
        {
            App.Lang = cfgLang;
            return; // Preserve an explicit configuration value.
        }
        var lang = SystemLang();
        if (!supported.Contains(lang)) lang = "en"; // Fall back to English when the system language is unsupported.
        App.Lang = lang;
        App.Config.Ui.Lang = lang;
        try { App.Config.Save(); } catch { }
    }

    /// <summary>Map the system UI language to a supported application language identifier and let the caller handle all others.</summary>
    public static string SystemLang()
    {
        try
        {
            var ci = System.Globalization.CultureInfo.CurrentUICulture;
            var name = ci.Name;
            if (name.StartsWith("yue", StringComparison.OrdinalIgnoreCase)) return "yue-hk";
            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("HK", StringComparison.OrdinalIgnoreCase)) return "zh-hk";
                return name.Contains("TW", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("MO", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                    ? "zh-tw" : "zh-cn";
            }
            var two = ci.TwoLetterISOLanguageName.ToLowerInvariant();
            return two switch
            {
                "en" => "en",
                "ja" => "ja",
                "es" => "es",
                "ko" => "ko",
                "de" => "de",
                "fr" => "fr",
                _ => two,
            };
        }
        catch { return "en"; }
    }
}
