using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace HeartRateMonitor.SysInfo.Collectors;

/// <summary>Collects OS, BIOS, motherboard, memory, and related data through PowerShell's `Get-ComputerInfo`.</summary>
public static class PsComputerInfo
{
    /// <summary> Run GetX-ComputerInfo and parse the output "Key: value" </summary>
    static Dictionary<string, string> Run()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo("powershell",
                "-NoProfile -NonInteractive -Command \"$ErrorActionPreference='SilentlyContinue'; Get-ComputerInfo\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Unicode, // powershell Output UTF-16
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
                if (key.Length == 0) continue;
                // Group like {a, b} for first element
                if (val.StartsWith('{') && val.EndsWith('}'))
                {
                    var inner = val[1..^1].Trim();
                    val = inner.Split(',')[0].Trim();
                }
                if (!map.ContainsKey(key)) map[key] = val;
            }
        }
        catch { }
        return map;
    }

    public static void Collect(ConcurrentDictionary<string, string> d)
    {
        var m = Run();

        void Pick(string key, params string[] psNames)
        {
            if (d.ContainsKey(key)) return;
            foreach (var n in psNames)
                if (m.TryGetValue(n, out var v) && !string.IsNullOrWhiteSpace(v))
                {
                    d[key] = v;
                    return;
                }
        }

        Pick("OS_NAME", "WindowsProductName", "OsName");
        Pick("OS_VERSION", "WindowsVersion", "OsVersion");
        Pick("OS_BUILD", "OsBuildNumber", "WindowsCurrentVersion");
        Pick("OS_SERIAL", "WindowsProductId");
        Pick("OS_INSTALL_DATE", "OsInstallDate");
        Pick("OS_LAST_BOOT", "OsLastBootUpTime");
        Pick("OS_EDITION", "WindowsEditionId");
        Pick("OS_ARCH", "OsArchitecture");
        Pick("OS_SYS_DRIVE", "OsSystemDrive");
        Pick("OS_WIN_DIR", "OsWindowsDirectory");

        Pick("MB_VENDOR", "CsManufacturer");
        Pick("MB_PRODUCT", "CsModel");
        Pick("MB_SERIAL", "CsSystemSKUNumber");
        Pick("MB_FAMILY", "CsSystemFamily");

        Pick("BIOS_VENDOR", "BiosManufacturer");
        Pick("BIOS_VERSION", "BiosSMBIOSBIOSVersion");
        Pick("BIOS_RELEASE_DATE", "BiosReleaseDate");
        Pick("BIOS_SERIAL", "BiosSeralNumber");

        if (m.TryGetValue("CsProcessors", out var cpu))
            d["CPU_PRODUCT_NAME"] = cpu;
        if (m.TryGetValue("CsNumberOfLogicalProcessors", out var thr))
            d["CPU_THREADS"] = thr;

        if (!d.ContainsKey("RAM_TOTAL") && m.TryGetValue("CsTotalPhysicalMemory", out var mem))
            d["RAM_TOTAL"] = $"{double.Parse(mem) / 1073741824.0:F0} GB";
        if (m.TryGetValue("OsFreePhysicalMemory", out var free))
            d["OS_PHYS_FREE"] = $"{double.Parse(free) * 1024 / 1073741824.0:F1} GB";
        if (m.TryGetValue("OsTotalVisibleMemorySize", out var vis))
            d["OS_PHYS_TOTAL"] = $"{double.Parse(vis) * 1024 / 1073741824.0:F0} GB";
    }
}
