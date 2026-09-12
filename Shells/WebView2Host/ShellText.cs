using System.Text.Json;
using System.Text.Json.Nodes;

namespace WebView2Host;

/// <summary>
/// Shell-side text language resolution (round 27): the shell does not reference the engine assembly, while error dialogs
/// for WebView2 initialization, backend availability, and page loading were previously hardcoded in Chinese. This mirrors
/// the engine data-directory rules (data_location.txt beside the executable, then %AppData%\HeartRateMonitor) and reads
/// config.json ui.lang. Chinese and Cantonese locales use Chinese text; all other languages fall back to English.
/// </summary>
internal static class ShellText
{
    private static readonly string Lang;

    static ShellText()
    {
        try { Lang = ResolveLang(); }
        catch { Lang = ""; }
    }

    private static bool Chinese => Lang is "zh-cn" or "zh-tw" or "zh-hk" or "yue-hk" or "zh" or "";

    /// <summary>Select Chinese or English: zh uses the first value; all others use the second, including unreadable configuration.</summary>
    public static string Pick(string zh, string en) => Chinese ? zh : en;

    /// <summary>Placeholder variant: {0}, {1}, etc. are populated through <see cref="string.Format"/>.</summary>
    public static string F(string zh, string en, params object?[] args)
        => string.Format(Chinese ? zh : en, args);

    private static string ResolveLang()
    {
        string? cfg = FindConfig();
        if (cfg is null) return "";
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(cfg)) as JsonObject;
            var lang = root?["ui"]?["lang"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(lang)) return lang!.Trim().ToLowerInvariant();
        }
        catch { /* Use the default for invalid configuration. */ }
        return "";
    }

    /// <summary>Find config.json using engine rules: data_location.txt, then %AppData%\HeartRateMonitor.</summary>
    private static string? FindConfig()
    {
        var exeDir = AppContext.BaseDirectory;
        var candidates = new List<string>();
        try
        {
            var loc = Path.Combine(exeDir, "data_location.txt");
            if (File.Exists(loc))
            {
                var v = File.ReadAllText(loc).Trim();
                if (v.Length > 0)
                {
                    var dir = Path.IsPathRooted(v) ? v : Path.Combine(exeDir, v);
                    candidates.Add(Path.Combine(dir, "config.json"));
                }
            }
        }
        catch { /* ignore */ }
        candidates.Add(Path.Combine(exeDir, "config.json"));
        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HeartRateMonitor", "config.json"));
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }
}
