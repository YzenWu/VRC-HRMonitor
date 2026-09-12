using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HeartRateMonitor.SysInfo.Collectors;

/// <summary>XQ1QXZ collection (optional Windows component, only enabled when the executable is actually detected). </summary>
public static class WmicInfo
{
    /// <summary> determines the existence of the optional component WMIC on the basis of the actual file and does not depend on the system version. </summary>
    public static bool AvailableOnThisSystem() => FindWmicExecutable() != null;

    static string? FindWmicExecutable()
    {
        try
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var builtIn = Path.Combine(system, "wbem", "wmic.exe");
            if (File.Exists(builtIn)) return builtIn;

            foreach (var raw in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                var dir = raw.Trim().Trim('"');
                if (dir.Length == 0) continue;
                var candidate = Path.Combine(dir, "wmic.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { }
        return null;
    }

    /// <summary> runs wmic query, parsing /format: list output as multiple examples. </summary>
    static List<Dictionary<string, string>> RunWmic(string query)
    {
        var result = new List<Dictionary<string, string>>();
        try
        {
            var executable = FindWmicExecutable();
            if (executable == null) return result;
            var psi = new ProcessStartInfo(executable, query)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };
            using var p = Process.Start(psi);
            if (p == null) return result;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            ParseList(output, result);
        }
        catch { }
        return result;
    }

    static void ParseList(string output, List<Dictionary<string, string>> result)
    {
        Dictionary<string, string>? cur = null;
        foreach (var raw in output.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (cur != null) { result.Add(cur); cur = null; }
                continue;
            }
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            cur ??= new Dictionary<string, string>();
            cur[key] = val;
        }
        if (cur != null) result.Add(cur);
    }

    static void Set(ConcurrentDictionary<string, string> d, string key, IReadOnlyDictionary<string, string> inst, string field)
    {
        if (inst.TryGetValue(field, out var v) && !string.IsNullOrWhiteSpace(v))
            d[key] = v.Trim();
    }

    public static void Collect(ConcurrentDictionary<string, string> d)
    {
        if (!AvailableOnThisSystem()) return;

        // CPU
        foreach (var inst in RunWmic("path win32_Processor get Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed /format:list"))
        {
            Set(d, "CPU_PRODUCT_NAME", inst, "Name");
            Set(d, "CPU_CORES", inst, "NumberOfCores");
            Set(d, "CPU_THREADS", inst, "NumberOfLogicalProcessors");
            if (inst.TryGetValue("MaxClockSpeed", out var m) && int.TryParse(m, out var mhz))
            {
                d["CPU_FREQ"] = $"{mhz / 1000.0:0.##} GHz";
                // Standard unit version (QXQ0QXZ exit source when not available, with ApplyPdh name matching)
                d["CPU_FREQ_MHz"] = mhz.ToString();
                d["CPU_FREQ_GHz"] = $"{mhz / 1000.0:0.##}";
            }
        }

        // Main Board
        foreach (var inst in RunWmic("baseboard get Manufacturer,Product,Version,SerialNumber /format:list"))
        {
            Set(d, "MB_VENDOR", inst, "Manufacturer");
            Set(d, "MB_PRODUCT", inst, "Product");
            Set(d, "MB_VERSION", inst, "Version");
            Set(d, "MB_SERIAL", inst, "SerialNumber");
        }

        // BIOS
        foreach (var inst in RunWmic("bios get Manufacturer,Name,SMBIOSBIOSVersion,ReleaseDate,SerialNumber /format:list"))
        {
            Set(d, "BIOS_VENDOR", inst, "Manufacturer");
            Set(d, "BIOS_VERSION", inst, "SMBIOSBIOSVersion");
            Set(d, "BIOS_NAME", inst, "Name");
            Set(d, "BIOS_SERIAL", inst, "SerialNumber");
            if (inst.TryGetValue("ReleaseDate", out var rd) && rd.Length >= 8)
                d["BIOS_RELEASE_DATE"] = $"{rd[..4]}-{rd[4..6]}-{rd[6..8]}";
        }

        // GPU (AdapterRAM stopped at 4GB, which is known as wmic)
        var gpus = RunWmic("path win32_VideoController get Name,AdapterRAM,VideoProcessor /format:list");
        for (var i = 0; i < Math.Min(gpus.Count, 4); i++)
        {
            var g = gpus[i];
            Set(d, $"GPU_PRODUCT_NAME{i}", g, "Name");
            Set(d, $"GPU_VENDOR{i}", g, "VideoProcessor");
            if (g.TryGetValue("AdapterRAM", out var ram) && long.TryParse(ram, out var ramBytes))
                d[$"VRAM_TOTAL{i}"] = $"{ramBytes / 1073741824.0:F1} GB";
        }

        // Memory Bar
        var dimms = RunWmic("path win32_PhysicalMemory get BankLabel,DeviceLocator,Capacity,Speed,Manufacturer,PartNumber,SerialNumber /format:list");
        for (var i = 0; i < Math.Min(dimms.Count, 4); i++)
        {
            var m = dimms[i];
            Set(d, $"DIMM_SLOT{i}", m, "DeviceLocator");
            Set(d, $"DIMM_BANK{i}", m, "BankLabel");
            Set(d, $"DIMM_VENDOR{i}", m, "Manufacturer");
            Set(d, $"DIMM_PN{i}", m, "PartNumber");
            Set(d, $"DIMM_SN{i}", m, "SerialNumber");
            if (m.TryGetValue("Capacity", out var cap) && long.TryParse(cap, out var capBytes))
                d[$"DIMM_CAPACITY{i}"] = $"{capBytes / 1073741824.0:F0} GB";
            if (m.TryGetValue("Speed", out var sp))
                d[$"DIMM_SPEED{i}"] = $"{sp} MT/s";
        }

        // Disk
        var disks = RunWmic("path win32_DiskDrive get Model,InterfaceType,MediaType,Size,SerialNumber,FirmwareRevision,Status /format:list");
        for (var i = 0; i < Math.Min(disks.Count, 4); i++)
        {
            var k = disks[i];
            Set(d, $"DISK_MODEL{i}", k, "Model");
            Set(d, $"DISK_INTERFACE{i}", k, "InterfaceType");
            Set(d, $"DISK_MEDIA{i}", k, "MediaType");
            Set(d, $"DISK_SERIAL{i}", k, "SerialNumber");
            Set(d, $"DISK_FW{i}", k, "FirmwareRevision");
            Set(d, $"DISK_STATUS{i}", k, "Status");
            if (k.TryGetValue("Size", out var sz) && long.TryParse(sz, out var szBytes))
                d[$"DISK_SIZE_TOTAL{i}"] = $"{szBytes / 1073741824.0:F0} GB";
        }

        // Cybercard
        var nics = RunWmic("path win32_NetworkAdapter where \"NetConnectionStatus=2\" get Name,Manufacturer,MACAddress,AdapterType,Speed /format:list");
        for (var i = 0; i < Math.Min(nics.Count, 4); i++)
        {
            var k = nics[i];
            Set(d, $"NIC_NAME{i}", k, "Name");
            Set(d, $"NIC_VENDOR{i}", k, "Manufacturer");
            Set(d, $"NIC_MAC{i}", k, "MACAddress");
            Set(d, $"NIC_TYPE{i}", k, "AdapterType");
            if (k.TryGetValue("Speed", out var sp) && long.TryParse(sp, out var spBits))
                d[$"NIC_SPEED{i}"] = $"{spBits / 1000000} Mbps";
        }
    }
}
