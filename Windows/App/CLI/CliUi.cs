using HeartRateMonitor.Core;

namespace HeartRateMonitor.Cli;

/// <summary>CLI appearance: single-line banner, prompt, and section headings.
/// Performs plain-text rendering only and does not touch business state, making it easy to unit test and reuse in any terminal.</summary>
public static class CliUi
{
    /// <summary>Whether ANSI is available; disabled when redirecting to a file or pipe to avoid writing escape sequences into logs.</summary>
    public static bool Ansi { get; private set; }

    const string Reset = "\u001b[0m";
    const string Dim = "\u001b[2m";
    const string Bold = "\u001b[1m";
    const string Cyan = "\u001b[36m";
    const string Red = "\u001b[31m";
    const string Yellow = "\u001b[33m";
    const string Green = "\u001b[32m";

    /// <summary>Initialize plain rendering after the caller acquired an output-mode scope.</summary>
    public static void Init(bool ansiAvailable)
    {
        Ansi = ansiAvailable && !Console.IsOutputRedirected;
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
    }

    public static void Init() => Init(!Console.IsOutputRedirected && NativeConsole.EnableVirtualTerminal());

    static string C(string code, string s) => Ansi ? code + s + Reset : s;
    public static string Accent(string s) => C(Cyan, s);
    public static string Muted(string s) => C(Dim, s);
    public static string Strong(string s) => C(Bold, s);
    public static string Bad(string s) => C(Red, s);
    public static string Warn(string s) => C(Yellow, s);
    public static string Good(string s) => C(Green, s);

    /// <summary>Startup ASCII art stored as base64 because it contains special characters such as \ and /$ that are easily escaped or reformatted in source.
    /// It is the same image used by the Web console; trailing padding spaces are removed after decoding for consistent rendering.</summary>
    const string ArtB64 =
        "IF9fICAgIF9fIF9fX19fX18gICAgICAgICBfXyAgICAgICBfXyAgICAgICAgICAgICAgICAgICBfXyAgIF9fICAgICAgICAgICAgICAgICAgICAgICAKfCAgXCAgfCAgfCAgICAgICBcICAgICAgIHwgIFwgICAgIC8gIFwgICAgICAgICAgICAgICAgIHwgIFwgfCAgXCAgICAgICAgICAgICAgICAgICAgICAKfCAkJCAgfCAkfCAkJCQkJCQkXCAgICAgIHwgJCRcICAgLyAgJCQgX19fX19fICBfX19fX19fICBcJCRffCAkJF8gICAgX19fX19fICAgX19fX19fICAKfCAkJF9ffCAkfCAkJF9ffCAkJCAgICAgIHwgJCQkXCAvICAkJCQvICAgICAgXHwgICAgICAgXHwgIHwgICAkJCBcICAvICAgICAgXCAvICAgICAgXCAKfCAkJCAgICAkfCAkJCAgICAkJCAgICAgIHwgJCQkJFwgICQkJHwgICQkJCQkJHwgJCQkJCQkJHwgJCRcJCQkJCQkIHwgICQkJCQkJHwgICQkJCQkJFwKfCAkJCQkJCQkfCAkJCQkJCQkXCAgICAgIHwgJCRcJCQgJCQgJHwgJCQgIHwgJHwgJCQgIHwgJHwgJCQgfCAkJCBfX3wgJCQgIHwgJHwgJCQgICBcJCQKfCAkJCAgfCAkfCAkJCAgfCAkJCAgICAgIHwgJCQgXCQkJHwgJHwgJCRfXy8gJHwgJCQgIHwgJHwgJCQgfCAkJHwgIHwgJCRfXy8gJHwgJCQgICAgICAKfCAkJCAgfCAkfCAkJCAgfCAkJCAgICAgIHwgJCQgIFwkIHwgJCRcJCQgICAgJHwgJCQgIHwgJHwgJCQgIFwkJCAgJCRcJCQgICAgJHwgJCQgICAgICAKIFwkJCAgIFwkJFwkJCAgIFwkJCAgICAgICBcJCQgICAgICBcJCQgXCQkJCQkJCBcJCQgICBcJCRcJCQgICBcJCQkJCAgXCQkJCQkJCBcJCQgICAgICAKICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAKICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAKICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICA=";

    static readonly string[] Art = DecodeArt();

    static string[] DecodeArt()
    {
        try
        {
            var rows = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(ArtB64)).Split('\n');
            for (var i = 0; i < rows.Length; i++) rows[i] = rows[i].TrimEnd();
            return rows;
        }
        catch { return new string[0]; }
    }

    /// <summary>CLI banner: wide terminals show the ASCII art, engine summary, and usage hint; narrow terminals show only the summary.</summary>
    public static void Banner()
    {
        var w = SafeWidth();
        Console.WriteLine();
        var artW = 0;
        foreach (var a in Art) if (a.Length > artW) artW = a.Length;
        if (w >= artW + 4)
        {
            foreach (var a in Art) Console.WriteLine(Accent(a));
            Console.WriteLine();
        }
        Console.WriteLine("  " + Accent("HeartRateMonitor · OSC Pusher") + Muted($"   v{App.Version}   {(App.DebugMode ? "DEBUG" : "RELEASE")}"));
        Console.WriteLine("  " + Muted(Txt.T("cli.banner.engine",
            Txt.T(Osc.OscEngine.Available ? "st.loaded" : "st.not_loaded"), App.BaseDir)));
        Console.WriteLine();
        Console.WriteLine("  " + Muted(Txt.T("cli.banner.hint")));
        Console.WriteLine("  " + Muted(new string('─', Math.Clamp(w - 4, 20, 72))));
        Console.WriteLine();
    }

    /// <summary>Single-part prompt containing heart rate and OSC status, similar to a shell PS1.</summary>
    public static void Prompt()
    {
        var bpm = App.CurrentBpm;
        var hr = bpm > 0 ? $" {bpm}bpm" : "";
        var osc = App.Osc.Connected ? " osc" : "";
        Console.Write(Accent("hrm") + Muted(hr + osc) + Accent("> "));
    }

    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("  " + Strong(title));
    }

    /// <summary>Command output indented by two spaces and colored by content: red for errors and yellow for warnings.
    /// Colors are selected using keywords from each language's messages; Chinese uses the original markers, while other languages use approximate translated keywords.</summary>
    public static void Print(IEnumerable<string> lines)
    {
        var (bad, warn) = Markers();
        foreach (var l in lines)
        {
            var s = "  " + l;
            if (ContainsAny(l, bad)) Console.WriteLine(Bad(s));
            else if (ContainsAny(l, warn)) Console.WriteLine(Warn(s));
            else Console.WriteLine(s);
        }
    }

    static bool ContainsAny(string l, string[] words)
    {
        foreach (var w in words)
            if (l.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Error and warning keywords for the current language, matching the wording in Txt translations and used only for coloring.</summary>
    static (string[] Bad, string[] Warn) Markers() => App.Lang switch
    {
        "en" => (new[] { "[Error]", "failed", "fail" }, new[] { "not loaded", "not connected", "not started", "not running", "unavailable" }),
        "ja" => (new[] { "[エラー]", "失敗" }, new[] { "未読み込み", "未接続", "未起動", "利用できません" }),
        "es" => (new[] { "[Error]", "error", "fall" }, new[] { "no cargado", "no conectado", "no iniciado", "no disponible" }),
        _ => (new[] { "[错误]", "失败" }, new[] { "警告", "未加载", "未连接" }),
    };

    public static void Event(string tag, string text)
        => Console.WriteLine("  " + Muted($"[{tag}]") + " " + text);

    static int SafeWidth()
    {
        try { return Console.WindowWidth; } catch { return 80; }
    }
}
