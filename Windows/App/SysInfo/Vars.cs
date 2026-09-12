namespace HeartRateMonitor.SysInfo;

/// <summary> is all available template variable names (using the old sysinfo variable table). </summary>
public static class Vars
{
    public static List<string> All()
    {
        var list = new List<string>
        {
            "BPM_AVG", "BPM0", "BPM1", "BPM2", "BPM3",
        };
        foreach (var k in new[]
                 {
                     "CPU_USAGE", "CPU_FREQ", "CPU_CORES", "CPU_THREADS", "CPU_QUEUE",
                     "CPU_TEMP_MAX", "CPU_TEMP_MIN", "CPU_TEMP_AVG", "CPU_TEMP_PACKAGE",
                     "RAM_TOTAL", "RAM_USED", "RAM_AVAILABLE", "RAM_PERCENT",
                     "MEMORY_AVAILABLE_BYTES", "MEMORY_COMMITTED_BYTES", "MEMORY_COMMITTED_PERCENT",
                     "DISK_READ_BYTES_PER_SEC", "DISK_WRITE_BYTES_PER_SEC", "DISK_BYTES_PER_SEC",
                     "DISK_QUEUE_LENGTH", "DISK_TIME_PERCENT",
                     "UPTIME", "UPTIME_SECONDS",
                     "PROCESS_COUNT", "THREAD_COUNT", "HANDLE_COUNT", "CONTEXT_SWITCHES",
                     "SYSTEM_PROCESSOR_ARCHITECTURE", "SYSTEM_PROCESSOR_TYPE", "SYSTEM_PAGE_SIZE", "SYSTEM_ALLOCATION_GRANULARITY",
                     "SCREEN_WIDTH", "SCREEN_HEIGHT", "VIRTUAL_SCREEN_WIDTH", "VIRTUAL_SCREEN_HEIGHT", "MONITOR_COUNT",
                     "FOCUS_WINDOW_TITLE", "FOCUS_PROCESS_NAME",
                 })
            list.Add(k);
        // Standard unit derivative variable (VarEngine.ApplyStandardUnits generation; single instance section)
        foreach (var k in new[]
                 {
                     "CPU_FREQ_MHz", "CPU_FREQ_GHz", "CPU_BASE_MHz", "CPU_BASE_GHz",
                     "RAM_TOTAL_GB", "RAM_TOTAL_MB", "RAM_USED_GB", "RAM_USED_MB",
                     "RAM_AVAILABLE_GB", "RAM_AVAILABLE_MB",
                     "CPU_TEMP_MAX_C", "CPU_TEMP_MIN_C", "CPU_TEMP_AVG_C", "CPU_TEMP_PACKAGE_C",
                     "UPTIME_SECONDS_MINUTES", "UPTIME_SECONDS_HOURS", "UPTIME_SECONDS_DAYS",
                 })
            list.Add(k);
        foreach (var k in new[]
                 {
                     "CPU_PRODUCT_NAME", "CPU_VENDOR", "CPU_FAMILY", "CPU_CACHE", "CPU_GENERATION",
                     "MB_VENDOR", "MB_FAMILY", "MB_PRODUCT", "MB_VERSION", "MB_SERIAL",
                     "BIOS_VENDOR", "BIOS_NAME", "BIOS_VERSION", "BIOS_RELEASE_DATE", "BIOS_SERIAL",
                     "OS_NAME", "OS_VERSION", "OS_BUILD", "OS_ARCH", "OS_SERIAL",
                     "OS_SYS_DRIVE", "OS_WIN_DIR", "OS_INSTALL_DATE", "OS_LAST_BOOT",
                     "OS_PHYS_TOTAL", "OS_PHYS_FREE", "OS_COMMIT_LIMIT", "OS_COMMIT_USED", "OS_COMMIT_AVAILABLE",
                     "OS_VIRT_TOTAL", "OS_VIRT_USED", "OS_VIRT_FREE",
                 })
            list.Add(k);
        foreach (var prefix in new[]
                 {
                     "CPU_TEMP", "GPU_VENDOR", "GPU_PRODUCT_NAME", "GPU_PRODUCT_CODE", "VRAM_TOTAL", "VRAM_USED", "VRAM_PERCENT", "GPU_USAGE",
                     "DISK_MODEL", "DISK_INTERFACE", "DISK_MEDIA", "DISK_SIZE_TOTAL", "DISK_SERIAL", "DISK_FW", "DISK_STATUS",
                     "DRIVE_LETTER", "DRIVE_VOLUME", "DRIVE_FS", "DRIVE_TOTAL", "DRIVE_FREE", "DRIVE_USED", "DRIVE_PERCENT", "DRIVE_SERIAL",
                     "DIMM_SLOT", "DIMM_BANK", "DIMM_CAPACITY", "DIMM_SPEED", "DIMM_VENDOR", "DIMM_PN", "DIMM_SN", "DIMM_TYPE",
                     "NIC_NAME", "NIC_VENDOR", "NIC_MAC", "NIC_TYPE", "NIC_SPEED",
                     // Standard unit derived (multi-example, same 0~3)
                     "VRAM_TOTAL_GB", "VRAM_TOTAL_MB", "VRAM_USED_GB", "VRAM_USED_MB",
                     "DIMM_CAPACITY_GB", "DIMM_SPEED_MHz",
                     "DISK_SIZE_TOTAL_GB", "DISK_SIZE_TOTAL_TB",
                     "DRIVE_TOTAL_GB", "DRIVE_FREE_GB", "DRIVE_USED_GB",
                     "NIC_SPEED_Mbps", "NIC_SPEED_Gbps",
                 })
        {
            for (var i = 0; i < 4; i++) list.Add($"{prefix}{i}");
        }
        return list;
    }
}
