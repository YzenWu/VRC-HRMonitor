using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace HeartRateMonitor.SysInfo.Collectors;

/// The <summary>systeminfo command collects (method 3, with an incorrect keyname). </summary>
public static class SystemInfoExe
{
    /// <summary> (Chinese keyname, English keyname) </summary>
    static readonly (string zh, string en, string key)[] Mapping =
    {
        ("主机名",   "Host Name",               "SYSINFO_HOST"),
        ("OS 名称",  "OS Name",                 "OS_NAME"),
        ("OS 版本",  "OS Version",              "OS_VERSION"),
        ("系统制造商", "System Manufacturer",    "MB_VENDOR"),
        ("系统型号",  "System Model",            "MB_PRODUCT"),
        ("处理器",    "Processor(s)",            "CPU_PRODUCT_NAME"),
        ("BIOS 版本", "BIOS Version",            "BIOS_VERSION"),
        ("物理内存总量", "Total Physical Memory", "RAM_TOTAL"),
        ("虚拟内存: 最大值", "Virtual Memory: Max Size", "OS_VIRT_TOTAL"),
        ("系统启动时间", "System Boot Time",      "OS_LAST_BOOT"),
        ("系统目录",   "System Directory",        "SYSINFO_SYS_DIR"),
        ("热修复程序", "Hotfix(s)",               "SYSINFO_HOTFIXES"),
    };

    static Dictionary<string, string> Run()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo("systeminfo")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };
            using var p = Process.Start(psi);
            if (p == null) return map;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30000);
            foreach (var raw in output.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                var key = line[..idx].Trim();
                var val = line[(idx + 1)..].Trim();
                if (!map.ContainsKey(key)) map[key] = val;
            }
        }
        catch { }
        return map;
    }

    public static void Collect(ConcurrentDictionary<string, string> d)
    {
        var m = Run();
        foreach (var (zh, en, key) in Mapping)
        {
            if (d.ContainsKey(key)) continue;
            if (m.TryGetValue(zh, out var v) && !string.IsNullOrWhiteSpace(v))
                d[key] = v;
            else if (m.TryGetValue(en, out v) && !string.IsNullOrWhiteSpace(v))
                d[key] = v;
        }
    }
}
