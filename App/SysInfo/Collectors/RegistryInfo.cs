using System.Collections.Concurrent;
using Microsoft.Win32;

namespace HeartRateMonitor.SysInfo.Collectors;

/// <summary> registration form acquisition (method 4, fastest, as the main source of static hardware information). </summary>
public static class RegistryInfo
{
    public static void Collect(ConcurrentDictionary<string, string> d)
    {
        try
        {
            using var ntv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (ntv != null)
            {
                Set(d, "OS_NAME", ntv.GetValue("ProductName")?.ToString());
                Set(d, "OS_VERSION", ntv.GetValue("DisplayVersion")?.ToString() ?? ntv.GetValue("ReleaseId")?.ToString());
                Set(d, "OS_BUILD", ntv.GetValue("CurrentBuildNumber")?.ToString());
                Set(d, "OS_EDITION", ntv.GetValue("EditionID")?.ToString());
                Set(d, "OS_SERIAL", ntv.GetValue("ProductId")?.ToString());
                if (ntv.GetValue("InstallDate") is long || ntv.GetValue("InstallDate") is int)
                {
                    var secs = Convert.ToInt64(ntv.GetValue("InstallDate"));
                    Set(d, "OS_INSTALL_DATE", DateTimeOffset.FromUnixTimeSeconds(secs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
        }
        catch { }

        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (bios != null)
            {
                Set(d, "MB_VENDOR", bios.GetValue("BaseBoardManufacturer")?.ToString());
                Set(d, "MB_PRODUCT", bios.GetValue("BaseBoardProduct")?.ToString());
                Set(d, "MB_VERSION", bios.GetValue("BaseBoardVersion")?.ToString());
                Set(d, "MB_SERIAL", bios.GetValue("BaseBoardSerialNumber")?.ToString());
                Set(d, "MB_FAMILY", bios.GetValue("SystemFamily")?.ToString());
                Set(d, "BIOS_VENDOR", bios.GetValue("BIOSVendor")?.ToString());
                Set(d, "BIOS_NAME", bios.GetValue("BIOSVersion") is string[] bv ? bv.FirstOrDefault() : null);
                Set(d, "BIOS_VERSION", bios.GetValue("BIOSVersion") is string[] bv2 ? string.Join(" / ", bv2.Take(2)) : null);
                Set(d, "BIOS_RELEASE_DATE", bios.GetValue("BIOSReleaseDate")?.ToString());
                Set(d, "BIOS_SERIAL", bios.GetValue("SystemSerialNumber")?.ToString());
            }
        }
        catch { }

        try
        {
            using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (cpu != null)
            {
                Set(d, "CPU_PRODUCT_NAME", cpu.GetValue("ProcessorNameString")?.ToString()?.Trim());
                Set(d, "CPU_VENDOR", cpu.GetValue("VendorIdentifier")?.ToString()?.Trim());
                if (cpu.GetValue("~MHz") is int mhz)
                {
                    Set(d, "CPU_FREQ", mhz.ToString());
                    // Standard unit version (QXQ0QXZ returned source when not available)
                    Set(d, "CPU_FREQ_MHz", mhz.ToString());
                    Set(d, "CPU_FREQ_GHz", $"{mhz / 1000.0:0.##}");
                }
                Set(d, "CPU_IDENTIFIER", cpu.GetValue("Identifier")?.ToString());
            }
        }
        catch { }
    }

    static void Set(ConcurrentDictionary<string, string> d, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            d[key] = value.Trim();
    }
}
