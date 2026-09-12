using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.SysInfo;

/// <summary>
/// Variable engines: superimpose user layer processing on the results of the collection - units, floating point precision, manual overwrite, name change,
/// Custom Variable (Account / Collapse / Regular / Command Line stdout), Time Variable (local / UTC / NTP/ Timezone).
/// All configurations from AppConfig.HwSection are sustainable.
/// </summary>
public static class VarEngine
{
    /// <summary>XQ1QXZ offsets (ms) with local clocks, and the backstage is refreshed and failed with 0. </summary>
    private static long _ntpOffsetMs;
    private static long _ntpAt;

    /// <summary> command line variable Cache: Avoids a process at each round. </summary>
    private static readonly ConcurrentDictionary<string, (long ts, string val)> _cmdCache = new();

    /// <summary> formats a double by configuration. </summary>
    public static string Format(double v)
    {
        var cfg = App.Config.Hw;
        if (!cfg.UseFloat)
        {
            var i = cfg.Round ? Math.Round(v, MidpointRounding.AwayFromZero) : Math.Truncate(v);
            return ((long)i).ToString(CultureInfo.InvariantCulture);
        }
        // #11 ceiling contracted to 15:.NET Math.Round upper limit 15, double effective ~ 17
        // Fifteen decimal places is the `Math.Round` limit and the hard boundary of this formatting layer.
        var d = Math.Clamp(cfg.Decimals, 0, 15);
        var x = cfg.Round ? Math.Round(v, d, MidpointRounding.AwayFromZero) : Truncate(v, d);
        return x.ToString("F" + d, CultureInfo.InvariantCulture);
    }

    static double Truncate(double v, int decimals)
    {
        var f = Math.Pow(10, decimals);
        return Math.Truncate(v * f) / f;
    }

    /// <summary> writes a numerical variable (applying format and unit). </summary>
    public static void SetNumber(ConcurrentDictionary<string, string> vars, string name, double value)
    {
        var text = Format(value);
        if (App.Config.Hw.Units.TryGetValue(name, out var unit) && !string.IsNullOrEmpty(unit))
            text += unit;
        vars[name] = text;
    }

    /// <summary>
    /// Collected after processing: Time variable →Standard unit derived custom variable →change name overwrite.
    /// The order ensures that the overwriting is always valid (last step) and the custom variable can refer to the capture and derivative values.
    /// </summary>
    public static void PostProcess(ConcurrentDictionary<string, string> vars)
    {
        ApplyTime(vars);
        ApplyStandardUnits(vars);
        ApplyCustom(vars);
        ApplyRenames(vars);
        ApplyOverrides(vars);
    }

    // ------------------------------------------------------------------ Standard units

    /// <summary>
    /// Standard unit derivative table: source variable → (target suffix, conversion factor).
    /// The source value is analysed by "Check the first number" and therefore "24 GB", "3.85", "1000 Mbps" can be eaten.
    /// `#` indicates that the source is a multi-example variable (GPU/RAM/disc/net) with the number 0~3.
    /// </summary>
    private static readonly (string Src, string Suffix, double Factor)[] UnitRules =
    {
        // Memory: Collection value already GB
        ("RAM_TOTAL", "GB", 1.0),
        ("RAM_TOTAL", "MB", 1024.0),
        ("RAM_USED", "GB", 1.0),
        ("RAM_USED", "MB", 1024.0),
        ("RAM_AVAILABLE", "GB", 1.0),
        ("RAM_AVAILABLE", "MB", 1024.0),
        // Temperature: degrees Celsius
        ("CPU_TEMP_MAX", "C", 1.0),
        ("CPU_TEMP_MIN", "C", 1.0),
        ("CPU_TEMP_AVG", "C", 1.0),
        ("CPU_TEMP_PACKAGE", "C", 1.0),
        // Run time: Collect value in seconds
        ("UPTIME_SECONDS", "MINUTES", 1.0 / 60),
        ("UPTIME_SECONDS", "HOURS", 1.0 / 3600),
        ("UPTIME_SECONDS", "DAYS", 1.0 / 86400),
        // Multiple instances (subscript 0~3): visible / memory bar / disk / partition / netcard
        ("VRAM_TOTAL#", "GB", 1.0),
        ("VRAM_TOTAL#", "MB", 1024.0),
        ("VRAM_USED#", "GB", 1.0),
        ("VRAM_USED#", "MB", 1024.0),
        ("DIMM_CAPACITY#", "GB", 1.0),
        ("DIMM_SPEED#", "MHz", 1.0),
        ("DISK_SIZE_TOTAL#", "GB", 1.0),
        ("DISK_SIZE_TOTAL#", "TB", 1.0 / 1024),
        ("DRIVE_TOTAL#", "GB", 1.0),
        ("DRIVE_FREE#", "GB", 1.0),
        ("DRIVE_USED#", "GB", 1.0),
        ("NIC_SPEED#", "Mbps", 1.0),
        ("NIC_SPEED#", "Gbps", 1.0 / 1000),
    };

    /// <summary>
    /// Press <see cref="UnitRules"/> to generate variables with standard unit suffixes (e.g. CPU_FREQ_GHz, RAM_USED_GB).
    /// Values <see cref="SetNumber"/> are also affected by floating point/decimal/rounded and custom unit settings.
    /// Skip silently when the source variable is missing or unable to resolve the number.
    /// </summary>
    static void ApplyStandardUnits(ConcurrentDictionary<string, string> vars)
    {
        foreach (var (src, suffix, factor) in UnitRules)
        {
            if (src.EndsWith('#'))
            {
                var stem = src[..^1];
                for (var i = 0; i < 4; i++) Derive(vars, $"{stem}{i}", $"{stem}_{suffix}{i}", factor);
            }
            else Derive(vars, src, $"{src}_{suffix}", factor);
        }
    }

    static void Derive(ConcurrentDictionary<string, string> vars, string from, string to, double factor)
    {
        if (!vars.TryGetValue(from, out var raw)) return;
        if (!TryNumber(raw, out var v)) return;
        SetNumber(vars, to, v * factor);
    }

    /// <summary> takes the first number in the string (permissible for lead and decimal points), "24ZGB" →24. </summary>
    static bool TryNumber(string s, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var m = Regex.Match(s, @"-?\d+(\.\d+)?");
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // ------------------------------------------------------------------ Time

    static void ApplyTime(ConcurrentDictionary<string, string> vars)
    {
        var now = DateTime.Now;
        var utc = DateTime.UtcNow;
        vars["TIME_LOCAL"] = now.ToString("yyyy-MM-dd HH:mm:ss");
        vars["TIME_LOCAL_HMS"] = now.ToString("HH:mm:ss");
        vars["TIME_UTC"] = utc.ToString("yyyy-MM-dd HH:mm:ss");
        vars["TIME_UTC_HMS"] = utc.ToString("HH:mm:ss");
        vars["TIME_ISO"] = now.ToString("O");
        vars["TIME_UNIX"] = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
        vars["TIME_ZONE"] = TimeZoneInfo.Local.Id;
        vars["TIME_ZONE_OFFSET"] = TimeZoneInfo.Local.GetUtcOffset(now).ToString(@"hh\:mm");
        var ntp = utc.AddMilliseconds(_ntpOffsetMs);
        vars["TIME_NTP"] = ntp.ToString("yyyy-MM-dd HH:mm:ss");
        vars["TIME_NTP_OFFSET_MS"] = _ntpOffsetMs.ToString();

        // Refresh NTP offsets per 10 minute (backstage, unblocked collection)
        var tick = Environment.TickCount64;
        if (tick - _ntpAt > 600_000)
        {
            _ntpAt = tick;
            _ = Task.Run(RefreshNtp);
        }
    }

    /// <summary>XQ1QXZ query (UDP 123, 48 byte), offset only. </summary>
    static void RefreshNtp()
    {
        var server = App.Config.Hw.NtpServer;
        if (string.IsNullOrWhiteSpace(server)) return;
        try
        {
            var data = new byte[48];
            data[0] = 0x1B;   // LI=0, VN=3, Mode=3 (client)
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 2000;
            udp.Client.SendTimeout = 2000;
            udp.Connect(server, 123);
            var t0 = DateTime.UtcNow;
            udp.Send(data, data.Length);
            var ep = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
            var resp = udp.Receive(ref ep);
            var t3 = DateTime.UtcNow;
            if (resp.Length < 48) return;
            // Transfer time stamp (bytes 40..47): sec + decimal seconds, all large
            ulong secs = ((ulong)resp[40] << 24) | ((ulong)resp[41] << 16) | ((ulong)resp[42] << 8) | resp[43];
            ulong frac = ((ulong)resp[44] << 24) | ((ulong)resp[45] << 16) | ((ulong)resp[46] << 8) | resp[47];
            var ms = secs * 1000.0 + frac * 1000.0 / 4294967296.0;
            var ntpTime = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
            // One-way delay offset by mid-turn point
            var mid = t0.AddMilliseconds((t3 - t0).TotalMilliseconds / 2);
            _ntpOffsetMs = (long)(ntpTime - mid).TotalMilliseconds;
            App.Log.Debug(LogText.L("log.var.ntp_offset", server, _ntpOffsetMs));
        }
        catch (Exception e)
        {
            App.Log.Debug(LogText.L("log.var.ntp_fail", server, e.Message));
        }
    }

    // ------------------------------------------------------------------ Custom Variables

    static void ApplyCustom(ConcurrentDictionary<string, string> vars)
    {
        foreach (var cv in App.Config.Hw.Custom)
        {
            if (!cv.Enabled || string.IsNullOrWhiteSpace(cv.Name)) continue;
            try
            {
                var value = cv.Kind switch
                {
                    "concat" => Substitute(cv.Expr, vars),
                    "regex" => RegexPick(Substitute(cv.Expr, vars), cv.Pattern),
                    "cmd" => RunCommand(Substitute(cv.Expr, vars)),
                    _ => EvalExprText(Substitute(cv.Expr, vars)),
                };
                vars[cv.Name] = string.IsNullOrEmpty(cv.Unit) ? value : value + cv.Unit;
            }
            catch (Exception e)
            {
                App.Log.Debug(LogText.L("log.var.calc_fail", cv.Name, e.Message));
            }
        }
    }

    /// <summary> Replace {变量} with the current value. </summary>
    public static string Substitute(string template, ConcurrentDictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(template)) return "";
        return Regex.Replace(template, @"\{([A-Za-z0-9_]+)\}", m =>
            vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    static string RegexPick(string input, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return input;
        var m = Regex.Match(input, pattern);
        if (!m.Success) return "";
        return m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
    }

    /// <summary> executes the command line stdout (result cache 5 seconds, time over 3 seconds). </summary>
    static string RunCommand(string cmdline)
    {
        if (string.IsNullOrWhiteSpace(cmdline)) return "";
        var now = Environment.TickCount64;
        if (_cmdCache.TryGetValue(cmdline, out var hit) && now - hit.ts < 5000) return hit.val;
        var text = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                // /d Skip AutoRun,/ c Execute / c
                Arguments = "/d /c " + cmdline,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                text = p.StandardOutput.ReadToEnd().Trim();
                if (!p.WaitForExit(3000)) { try { p.Kill(true); } catch { } }
            }
        }
        catch (Exception e)
        {
            App.Log.Debug(LogText.L("log.var.cmd_fail", e.Message));
        }
        _cmdCache[cmdline] = (now, text);
        return text;
    }

    /// <summary> requires four strings for the replaced variable; it is not a pure algorithm to return as it is. </summary>
    public static string EvalExprText(string expr)
    {
        var v = EvalExpr(expr);
        return v.HasValue ? Format(v.Value) : expr;
    }

    /// <summary>
    /// An extremely simple four operations (+ - */ %() supports decimals and one dollar negative).
    /// Accomplishment on its own, rather than introducing an expression library: A matrix that supports only hardware variables.
    /// </summary>
    public static double? EvalExpr(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;
        var pos = 0;
        try
        {
            var v = ParseAddSub(expr, ref pos);
            SkipWs(expr, ref pos);
            return pos == expr.Length ? v : null;
        }
        catch { return null; }
    }

    static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    static double ParseAddSub(string s, ref int i)
    {
        var v = ParseMulDiv(s, ref i);
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return v;
            var c = s[i];
            if (c != '+' && c != '-') return v;
            i++;
            var rhs = ParseMulDiv(s, ref i);
            v = c == '+' ? v + rhs : v - rhs;
        }
    }

    static double ParseMulDiv(string s, ref int i)
    {
        var v = ParseUnary(s, ref i);
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return v;
            var c = s[i];
            if (c != '*' && c != '/' && c != '%') return v;
            i++;
            var rhs = ParseUnary(s, ref i);
            v = c switch
            {
                '*' => v * rhs,
                '/' => rhs == 0 ? 0 : v / rhs,
                _ => rhs == 0 ? 0 : v % rhs,
            };
        }
    }

    static double ParseUnary(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            var neg = s[i] == '-';
            i++;
            var v = ParseUnary(s, ref i);
            return neg ? -v : v;
        }
        return ParseAtom(s, ref i);
    }

    static double ParseAtom(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == '(')
        {
            i++;
            var v = ParseAddSub(s, ref i);
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ')') i++;
            else throw new FormatException("括号不匹配");
            return v;
        }
        var start = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
        if (start == i) throw new FormatException($"意外字符于 {i}");
        return double.Parse(s[start..i], CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ Change Name / Overwrite

    static void ApplyRenames(ConcurrentDictionary<string, string> vars)
    {
        foreach (var (from, to) in App.Config.Hw.Renames)
        {
            if (string.IsNullOrWhiteSpace(to) || from == to) continue;
            if (vars.TryGetValue(from, out var v)) vars[to] = v;
        }
    }

    static void ApplyOverrides(ConcurrentDictionary<string, string> vars)
    {
        foreach (var (name, raw) in App.Config.Hw.Overrides)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            // The overwrite value itself supports {变量} and operations to facilitate metametric conversion
            var text = Substitute(raw ?? "", vars);
            var num = EvalExpr(text);
            vars[name] = num.HasValue ? Format(num.Value) : text;
        }
    }

    // ------------------------------------------------------------------ Configure Operations (for Command Layer)

    public static void SetOverride(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (value == null) App.Config.Hw.Overrides.Remove(name);
        else App.Config.Hw.Overrides[name] = value;
        App.Config.Save();
    }

    public static void SetUnit(string name, string? unit)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (string.IsNullOrEmpty(unit)) App.Config.Hw.Units.Remove(name);
        else App.Config.Hw.Units[name] = unit;
        App.Config.Save();
    }

    public static void SetRename(string from, string? to)
    {
        if (string.IsNullOrWhiteSpace(from)) return;
        if (string.IsNullOrWhiteSpace(to)) App.Config.Hw.Renames.Remove(from);
        else App.Config.Hw.Renames[from] = to!;
        App.Config.Save();
    }

    public static void UpsertCustom(AppConfig.CustomVar cv)
    {
        if (string.IsNullOrWhiteSpace(cv.Name)) return;
        var list = App.Config.Hw.Custom;
        var i = list.FindIndex(x => x.Name == cv.Name);
        if (i >= 0) list[i] = cv;
        else list.Add(cv);
        App.Config.Save();
    }

    public static void RemoveCustom(string name)
    {
        App.Config.Hw.Custom.RemoveAll(x => x.Name == name);
        App.Config.Save();
    }
}
