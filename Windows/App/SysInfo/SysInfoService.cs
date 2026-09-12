using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HeartRateMonitor.Core;
using HeartRateMonitor.SysInfo.Collectors;

namespace HeartRateMonitor.SysInfo;

/// <summary> hardware/system information acquisition schedule: fast (real-time indicators)+full (static hardware information, multi-method). </summary>
public class SysInfoService
{
    public readonly ConcurrentDictionary<string, string> Vars = new();
    public event Action? Updated;
    public volatile bool FullCollected;
    private System.Threading.Timer? _fastTimer;
    private readonly PdhCollector _pdh = new();

    public void Start()
    {
        if (App.SafeMode) return;
        CollectFull();
        _pdh.Init();
        // #11 upper limit released: lower limit only 200ms
        var interval = Math.Max(200, App.Config.Hw.IntervalMs);
        _fastTimer = new System.Threading.Timer(_ =>
        {
            try { CollectFast(); Updated?.Invoke(); } catch { }
        }, null, 300, interval);
    }

    /// Reset timers after <summary> collect interval changes. </summary>
    public void UpdateInterval()
    {
        var interval = Math.Max(200, App.Config.Hw.IntervalMs);
        _fastTimer?.Change(interval, interval);
    }

    private long _lastFull;

    /// Recollect all static hardware information (registration form + wmic + GetXQ3QZ + systeminfo overtime downgrade). 30s protection. </summary>
    public void CollectFull()
    {
        if (App.SafeMode) return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastFull) < 30000) return;
        Interlocked.Exchange(ref _lastFull, now);
        HrmTrace.Event("sysinfo.collect_full");
        // 4 all static collections are long-range, using a special thread to avoid starvation.
        _ = Task.Factory.StartNew(() =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                RegistryInfo.Collect(Vars);
                CollectNativeSystemInfo(Vars);
                CollectNativeVolumes(Vars);
            }
            catch { }
            sw.Stop(); HrmTrace.Perf("sysinfo.registry", sw.ElapsedMilliseconds, 30);
            App.Log.Info(LogText.L("log.hwinfo.reg", Get("CPU_PRODUCT_NAME"), Get("MB_VENDOR"), Get("MB_PRODUCT"), Get("OS_NAME"), Get("OS_BUILD")));
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (WmicInfo.AvailableOnThisSystem())
            _ = Task.Factory.StartNew(() =>
            {
                var sw = Stopwatch.StartNew();
                try { WmicInfo.Collect(Vars); } catch { }
                sw.Stop(); HrmTrace.Perf("sysinfo.wmic", sw.ElapsedMilliseconds, 30);
                App.Log.Info(LogText.L("log.hwinfo.wmic", Get("CPU_PRODUCT_NAME"), Get("GPU_PRODUCT_NAME0"), Get("DIMM_CAPACITY0")));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _ = Task.Factory.StartNew(() =>
        {
            var sw = Stopwatch.StartNew();
            try { PsComputerInfo.Collect(Vars); } catch { }
            sw.Stop(); HrmTrace.Perf("sysinfo.pscomputerinfo", sw.ElapsedMilliseconds, 30);
            App.Log.Info(LogText.L("log.hwinfo.pci", Get("OS_NAME"), Get("OS_VERSION"), Get("MB_VENDOR"), Get("MB_PRODUCT")));
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _ = Task.Factory.StartNew(() =>
        {
            var sw = Stopwatch.StartNew();
            try { SystemInfoExe.Collect(Vars); } catch { }
            sw.Stop(); HrmTrace.Perf("sysinfo.systeminfo", sw.ElapsedMilliseconds, 30);
            App.Log.Info(LogText.L("log.hwinfo.sysinfo", Get("OS_NAME"), Get("RAM_TOTAL"), Get("CPU_PRODUCT_NAME")));
            FullCollected = true;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    string Get(string key) => Vars.TryGetValue(key, out var v) ? v : "?";

    /// <summary> injects the heart rate variable into the variable table (calls when BLE data change). </summary>
    public void UpdateHeartRateVars()
    {
        var devices = App.Ble.ConnectedDevices();
        Vars["BPM_AVG"] = App.Ble.AverageBpm().ToString();
        for (var i = 0; i < 4; i++)
            Vars[$"BPM{i}"] = i < devices.Count ? devices[i].Bpm.ToString() : "0";
        Vars["BPM"] = App.CurrentBpm > 0 ? App.CurrentBpm.ToString() : "0";
    }

    /// <summary> replaces the template {变量} with the current variable table; undefined/miswritten {变量} is considered as an empty string (without brackets text). </summary>
    public string FormatTemplate(string tpl)
    {
        if (string.IsNullOrEmpty(tpl)) return "";
        // The variable name consists of a hybrid case by physical unit (e.g. CPU_FREQ_GHz) and the user often writes full capital (CPU_FREQ_GHZ);
        // Therefore, case-by-case insensitive resolution: Precise hits, then type-by-key ignores case-to-case matching, so that the valid variable is not empty.
        var sb = new StringBuilder(tpl.Length);
        var pos = 0;
        while (pos < tpl.Length)
        {
            var open = tpl.IndexOf('{', pos);
            if (open < 0) { sb.Append(tpl, pos, tpl.Length - pos); break; }
            sb.Append(tpl, pos, open - pos);
            var close = tpl.IndexOf('}', open + 1);
            if (close < 0) { sb.Append(tpl, open, tpl.Length - open); break; }
            var name = tpl.Substring(open + 1, close - open - 1);
            var val = ResolveVar(name) ?? ResolveDynamic(name);
            if (val != null)
            {
                sb.Append(val);
            }
            else if (name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                // Undefined Variable: Empty string, do not send {XXX} original
            }
            else
            {
                // Preserve braces containing special characters, such as JSON objects, when they are not dynamic variables.
                sb.Append('{').Append(name).Append('}');
            }
            pos = close + 1;
        }
        return sb.ToString();
    }

    /// The <summary> variable takes values: First case is accurate and then the case is left out (there are no conflict keys for case only in the variable table). </summary>
    private string? ResolveVar(string name)
    {
        if (Vars.TryGetValue(name, out var v)) return v;
        foreach (var kv in Vars)
            if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    // ---------------------------------------------------------------- Dynamic variable (Thirty-second round supplement #33)
    // CPU_USAGE_XQ1QXZZZ/MEM_USAGE_XQ3QXZ: Any process CPU%/RAM; USAGE_FILE_XQ6QXZ: Occupation MB.
    // The name matching process ProcessName (case insensitive, with .exe); the template is on demand and does not have to preset to the variable table.

    /// <summary> resolves dynamic variables distributed by prefix (case insensitive). </summary>
    private string? ResolveDynamic(string name)
    {
        const string cpuP = "CPU_USAGE_", memP = "MEM_USAGE_", fileP = "USAGE_FILE_";
        if (name.StartsWith(cpuP, StringComparison.OrdinalIgnoreCase))
            return DynamicProcess(name[cpuP.Length..], cpu: true);
        if (name.StartsWith(memP, StringComparison.OrdinalIgnoreCase))
            return DynamicProcess(name[memP.Length..], cpu: false);
        if (name.StartsWith(fileP, StringComparison.OrdinalIgnoreCase))
            return FileUsage(name[fileP.Length..]);
        return null;
    }

    private readonly ConcurrentDictionary<string, (long CpuTicks, long WallMs)> _procCpu = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (long AtMs, long Bytes)> _fileUsage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>CPU%/RAM Dynamic Variable; return null if the target process is not present (undefined in the template). </summary>
    private string? DynamicProcess(string target, bool cpu)
    {
        List<Process> list;
        if (int.TryParse(target, out var pid))
        {
            list = new List<Process>(1);
            try { list.Add(Process.GetProcessById(pid)); } catch { }
        }
        else
        {
            var want = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target[..^4] : target;
            try { list = Process.GetProcessesByName(want).ToList(); } catch { list = new List<Process>(0); }
        }
        try
        {
            if (list.Count == 0 || list.All(p => p.HasExited))
            {
                _procCpu.TryRemove("cpu:" + target, out _);
                return null;
            }
            long mem = 0, cpuTicks = 0;
            foreach (var p in list)
            {
                try
                {
                    if (p.HasExited) continue;
                    mem += p.WorkingSet64;
                    cpuTicks += p.TotalProcessorTime.Ticks;
                }
                catch { /*Process exited*/ }
            }
            if (!cpu) return $"{Math.Max(0, mem / (1024 * 1024))}";
            var key = "cpu:" + target;
            var now = Environment.TickCount64;
            var prev = _procCpu.TryGetValue(key, out var pr) ? pr : (CpuTicks: 0L, WallMs: now);
            _procCpu[key] = (cpuTicks, now);
            var dc = cpuTicks - prev.CpuTicks;
            var dt = now - prev.WallMs;
            if (dc <= 0 || dt <= 0) return "0";
            var cores = Environment.ProcessorCount;
            if (cores < 1) cores = 1;
            var pct = (dc / 10_000_000.0) / (dt / 1000.0) * 100.0 / cores;
            return $"{Math.Round(Math.Clamp(pct, 0, 100))}";
        }
        finally
        {
            foreach (var p in list) { try { p.Dispose(); } catch { } }
        }
    }

    /// <summary> file/directory occupation (MB, 5s cache to avoid the template being rendered in by-product to the full sweep). </summary>
    private static string? FileUsage(string raw)
    {
        var path = raw.Trim();
        if (path.Length == 0) return null;
        try
        {
            var now = Environment.TickCount64;
            if (_fileUsage.TryGetValue(path, out var hit) && now - hit.AtMs < 5000)
                return Mb(hit.Bytes);
            long bytes;
            if (Directory.Exists(path)) bytes = DirSize(new DirectoryInfo(path));
            else if (File.Exists(path)) bytes = new FileInfo(path).Length;
            else return null;
            _fileUsage[path] = (now, bytes);
            return Mb(bytes);
        }
        catch { return null; }
    }

    private static string Mb(long bytes) => bytes <= 0 ? "0" : Math.Max(1, (long)Math.Ceiling(bytes / 1048576.0)).ToString();

    private static long DirSize(DirectoryInfo dir)
    {
        long sum = 0;
        try
        {
            foreach (var f in dir.EnumerateFiles())
                try { sum += f.Length; } catch { }
        }
        catch { /*Unpermitted Directory Skipping*/ }
        try
        {
            foreach (var sub in dir.EnumerateDirectories())
                try { sum += DirSize(sub); } catch { }
        }
        catch { /*Unauthorized Subdirectories Skip*/ }
        return sum;
    }

    /// <summary>Refreshes variables for common processes, currently VRChat, and removes their keys when the process is absent.</summary>
    private void RefreshProcessVars()
    {
        var cpu = DynamicProcess("VRChat", cpu: true);
        var mem = DynamicProcess("VRChat", cpu: false);
        if (cpu == null || mem == null)
        {
            Vars.TryRemove("CPU_USAGE_VRCHAT", out _);
            Vars.TryRemove("MEM_USAGE_VRCHAT", out _);
        }
        else
        {
            Vars["CPU_USAGE_VRCHAT"] = cpu;
            Vars["MEM_USAGE_VRCHAT"] = mem;
        }
    }

    // ---------------------------------------------------------------- fast

    private int _collecting;

    void CollectFast()
    {
        // Overlap: Skip the current wheel if the last collection is not completed (avoiding Timer to build back into a full-line pool)
        if (Interlocked.Exchange(ref _collecting, 1) != 0) return;
        var sw = Stopwatch.StartNew();
        try
        {
            // Evaluate each PDH metric independently so one available value does not prevent fallbacks for missing metrics.
            var pdh = _pdh.Collect();
            ApplyPdh(pdh);
            if (!pdh.ContainsKey("CPU_USAGE_F")) CpuUsage(Vars);
            // PDH does not give a frequency under a nominal frequency to ensure the persistence of CPU_FREQ / CPU_FREQ_MHz / CPU_FREQ_GHz
            // (`% Processor Performance` counter is not available in some systems; no PDH machine goes here)
            if (!pdh.ContainsKey("CPU_FREQ_F")) FallbackCpuFreq(Vars);
            CpuTemp(Vars);
            Ram(Vars);
            Uptime(Vars);
            // The number of processes and the number of threads have to be reversed; the number of handles always needs to be counted.
            ProcessStats(Vars, pdh.ContainsKey("PROC_COUNT_F"), pdh.ContainsKey("THREAD_COUNT_F"));
            Focus(Vars);
            RefreshProcessVars();
            Gpu(Vars);
            UpdateHeartRateVars();
            // Time Variable / Custom Variable / Change Name / Overwrite
            VarEngine.PostProcess(Vars);
        }
        finally
        {
            Interlocked.Exchange(ref _collecting, 0);
        }
        sw.Stop();
        HrmTrace.Perf("sysinfo.fast", sw.ElapsedMilliseconds, 100);
    }

    /// <summary> writes the PDH float result into the variable table (and retains integer versions to fit the old template). </summary>
    private void ApplyPdh(Dictionary<string, double> pdh)
    {
        if (pdh.TryGetValue("CPU_USAGE_F", out var usage))
        {
            VarEngine.SetNumber(Vars, "CPU_USAGE_FLOAT", usage);
            Vars["CPU_USAGE"] = $"{Math.Round(usage)}";
        }
        if (pdh.TryGetValue("CPU_FREQ_F", out var freq))
        {
            // Standard unit name (case according to physical unit usage); PDH is a wise-frequency decimal with better precision than inverse from CPU_FREQ
            VarEngine.SetNumber(Vars, "CPU_FREQ_MHz", freq);
            VarEngine.SetNumber(Vars, "CPU_FREQ_GHz", freq / 1000.0);
            Vars["CPU_FREQ"] = $"{Math.Round(freq)}";
        }
        if (pdh.TryGetValue("CPU_PERF_F", out var perf)) VarEngine.SetNumber(Vars, "CPU_PERF_PERCENT", perf);
        if (_pdh.BaseMhz > 0)
        {
            VarEngine.SetNumber(Vars, "CPU_BASE_MHz", _pdh.BaseMhz);
            VarEngine.SetNumber(Vars, "CPU_BASE_GHz", _pdh.BaseMhz / 1000.0);
        }
        if (pdh.TryGetValue("CPU_QUEUE_F", out var q)) VarEngine.SetNumber(Vars, "CPU_QUEUE", q);
        if (pdh.TryGetValue("PROC_COUNT_F", out var procs))
        {
            VarEngine.SetNumber(Vars, "PROCESS_COUNT_FLOAT", procs);
            Vars["PROCESS_COUNT"] = $"{Math.Round(procs)}";
        }
        if (pdh.TryGetValue("THREAD_COUNT_F", out var threads))
        {
            VarEngine.SetNumber(Vars, "THREAD_COUNT_FLOAT", threads);
            Vars["THREAD_COUNT"] = $"{Math.Round(threads)}";
        }
        if (pdh.TryGetValue("CTX_SWITCH_F", out var ctx)) VarEngine.SetNumber(Vars, "CONTEXT_SWITCHES", ctx);
        if (pdh.TryGetValue("MEM_AVAILABLE_BYTES_F", out var memAvail)) VarEngine.SetNumber(Vars, "MEMORY_AVAILABLE_BYTES", memAvail);
        if (pdh.TryGetValue("MEM_COMMITTED_BYTES_F", out var committed)) VarEngine.SetNumber(Vars, "MEMORY_COMMITTED_BYTES", committed);
        if (pdh.TryGetValue("MEM_COMMIT_PERCENT_F", out var commitPct)) VarEngine.SetNumber(Vars, "MEMORY_COMMITTED_PERCENT", commitPct);
        if (pdh.TryGetValue("DISK_READ_BYTES_F", out var diskRead)) VarEngine.SetNumber(Vars, "DISK_READ_BYTES_PER_SEC", diskRead);
        if (pdh.TryGetValue("DISK_WRITE_BYTES_F", out var diskWrite)) VarEngine.SetNumber(Vars, "DISK_WRITE_BYTES_PER_SEC", diskWrite);
        if (pdh.TryGetValue("DISK_BYTES_F", out var diskBytes)) VarEngine.SetNumber(Vars, "DISK_BYTES_PER_SEC", diskBytes);
        if (pdh.TryGetValue("DISK_QUEUE_F", out var diskQueue)) VarEngine.SetNumber(Vars, "DISK_QUEUE_LENGTH", diskQueue);
        if (pdh.TryGetValue("DISK_TIME_F", out var disk)) VarEngine.SetNumber(Vars, "DISK_TIME_PERCENT", disk);
    }

    /// <summary> nominal frequency round (registration form ~MHz): CPU_FREQ (MHz)/_MHz/_GHz three-key unified writing. </summary>
    static void FallbackCpuFreq(ConcurrentDictionary<string, string> d)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("~MHz") is not int mhz || mhz <= 0) return;
            VarEngine.SetNumber(d, "CPU_FREQ_MHz", mhz);
            VarEngine.SetNumber(d, "CPU_FREQ_GHz", mhz / 1000.0);
            d["CPU_FREQ"] = mhz.ToString();
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    static void Ram(ConcurrentDictionary<string, string> d)
    {
        var ms = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref ms))
        {
            var total = ms.ullTotalPhys;
            var avail = ms.ullAvailPhys;
            var used = total - avail;
            var pct = total > 0 ? (int)Math.Round(used * 100.0 / total) : 0;
            d["RAM_TOTAL"] = Gb(total);
            d["RAM_USED"] = Gb(used);
            d["RAM_AVAILABLE"] = Gb(avail);
            d["RAM_PERCENT"] = pct.ToString();
            d["OS_PHYS_TOTAL"] = Gb(total);
            d["OS_PHYS_FREE"] = Gb(avail);

            var commitLimit = ms.ullTotalPageFile;
            var commitAvailable = ms.ullAvailPageFile;
            var commitUsed = commitLimit >= commitAvailable ? commitLimit - commitAvailable : 0;
            d["OS_COMMIT_LIMIT"] = Gb(commitLimit);
            d["OS_COMMIT_AVAILABLE"] = Gb(commitAvailable);
            d["OS_COMMIT_USED"] = Gb(commitUsed);

            var virtualTotal = ms.ullTotalVirtual;
            var virtualFree = ms.ullAvailVirtual;
            var virtualUsed = virtualTotal >= virtualFree ? virtualTotal - virtualFree : 0;
            d["OS_VIRT_TOTAL"] = Gb(virtualTotal);
            d["OS_VIRT_FREE"] = Gb(virtualFree);
            d["OS_VIRT_USED"] = Gb(virtualUsed);
        }
    }

    static string Gb(ulong bytes) => bytes >= 10ul * 1024 * 1024 * 1024
        ? $"{bytes / 1073741824.0:F0}" : $"{bytes / 1073741824.0:F1}";

    [StructLayout(LayoutKind.Sequential)]
    struct SystemInfo
    {
        public ushort wProcessorArchitecture;
        public ushort wReserved;
        public uint dwPageSize;
        public IntPtr lpMinimumApplicationAddress;
        public IntPtr lpMaximumApplicationAddress;
        public UIntPtr dwActiveProcessorMask;
        public uint dwNumberOfProcessors;
        public uint dwProcessorType;
        public uint dwAllocationGranularity;
        public ushort wProcessorLevel;
        public ushort wProcessorRevision;
    }

    [DllImport("kernel32.dll")]
    static extern void GetSystemInfo(out SystemInfo lpSystemInfo);
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailable,
        out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);

    static void CollectNativeSystemInfo(ConcurrentDictionary<string, string> d)
    {
        GetSystemInfo(out var si);
        d["CPU_THREADS"] = si.dwNumberOfProcessors.ToString();
        d["SYSTEM_PROCESSOR_ARCHITECTURE"] = si.wProcessorArchitecture switch
        {
            0 => "x86",
            5 => "ARM",
            6 => "IA64",
            9 => "x64",
            12 => "ARM64",
            _ => si.wProcessorArchitecture.ToString(),
        };
        d["SYSTEM_PROCESSOR_TYPE"] = si.dwProcessorType.ToString();
        d["SYSTEM_PAGE_SIZE"] = si.dwPageSize.ToString();
        d["SYSTEM_ALLOCATION_GRANULARITY"] = si.dwAllocationGranularity.ToString();
        d["SCREEN_WIDTH"] = GetSystemMetrics(0).ToString();
        d["SCREEN_HEIGHT"] = GetSystemMetrics(1).ToString();
        d["VIRTUAL_SCREEN_WIDTH"] = GetSystemMetrics(78).ToString();
        d["VIRTUAL_SCREEN_HEIGHT"] = GetSystemMetrics(79).ToString();
        d["MONITOR_COUNT"] = GetSystemMetrics(80).ToString();
    }

    static void CollectNativeVolumes(ConcurrentDictionary<string, string> d)
    {
        var drives = DriveInfo.GetDrives()
            .Where(x => x.DriveType != DriveType.NoRootDirectory && x.IsReady)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        for (var i = 0; i < drives.Length; i++)
        {
            var drive = drives[i];
            if (!GetDiskFreeSpaceEx(drive.RootDirectory.FullName, out _, out var total, out var free) || total == 0)
                continue;
            var used = total - free;
            d[$"DRIVE_LETTER{i}"] = drive.Name.TrimEnd('\\');
            d[$"DRIVE_VOLUME{i}"] = drive.VolumeLabel;
            d[$"DRIVE_FS{i}"] = drive.DriveFormat;
            d[$"DRIVE_TOTAL{i}"] = $"{total / 1073741824.0:F1} GB";
            d[$"DRIVE_FREE{i}"] = $"{free / 1073741824.0:F1} GB";
            d[$"DRIVE_USED{i}"] = $"{used / 1073741824.0:F1} GB";
            d[$"DRIVE_PERCENT{i}"] = $"{Math.Round(used * 100.0 / total)}";
        }
    }

    static void Uptime(ConcurrentDictionary<string, string> d)
    {
        var seconds = Environment.TickCount64 / 1000;
        d["UPTIME_SECONDS"] = seconds.ToString();
        var ts = TimeSpan.FromSeconds(seconds);
        d["UPTIME"] = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    static void CpuUsage(ConcurrentDictionary<string, string> d)
    {
        try
        {
            using var c = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            c.NextValue();
            Thread.Sleep(40);
            var v = c.NextValue();
            d["CPU_USAGE"] = $"{Math.Round(v)}";
        }
        catch { }
    }

    static void CpuTemp(ConcurrentDictionary<string, string> d)
    {
        try
        {
            var cat = new PerformanceCounterCategory("Thermal Zone Information");
            var instances = cat.GetInstanceNames();
            var temps = new List<double>();
            double? pkg = null;
            foreach (var inst in instances.Take(8))
            {
                using var c = new PerformanceCounter("Thermal Zone Information", "Temperature", inst, true);
                c.NextValue();
                Thread.Sleep(2);
                var raw = c.NextValue();
                var celsius = raw / 10.0 - 273.15;
                if (celsius < -50 || celsius > 150) continue;
                if (inst.ToLowerInvariant().Contains("package") || inst.ToLowerInvariant().Contains("cpu"))
                    pkg = celsius;
                temps.Add(celsius);
            }
            if (temps.Count == 0) return;
            d["CPU_TEMP_MAX"] = $"{Math.Round(temps.Max())}";
            d["CPU_TEMP_MIN"] = $"{Math.Round(temps.Min())}";
            d["CPU_TEMP_AVG"] = $"{Math.Round(temps.Average())}";
            if (pkg is not null) d["CPU_TEMP_PACKAGE"] = $"{Math.Round(pkg.Value)}";
            for (var i = 0; i < Math.Min(temps.Count, 4); i++)
                d[$"CPU_TEMP{i}"] = $"{Math.Round(temps[i])}";
        }
        catch { }
    }

    /// <summary> process/wire/string; PDH has provided a separate count. </summary>
    static void ProcessStats(ConcurrentDictionary<string, string> d, bool skipProcesses, bool skipThreads)
    {
        try
        {
            var processes = Process.GetProcesses();
            long threads = 0, handles = 0;
            foreach (var p in processes)
            {
                try
                {
                    if (!skipThreads) threads += p.Threads.Count;
                    handles += p.HandleCount;
                }
                catch { }
                finally { p.Dispose(); }
            }
            if (!skipProcesses) d["PROCESS_COUNT"] = processes.Length.ToString();
            if (!skipThreads) d["THREAD_COUNT"] = threads.ToString();
            d["HANDLE_COUNT"] = handles.ToString();
            VarEngine.SetNumber(d, "HANDLE_COUNT_FLOAT", handles);
        }
        catch { }
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    static void Focus(ConcurrentDictionary<string, string> d)
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return;
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, 256);
            d["FOCUS_WINDOW_TITLE"] = sb.ToString();
            GetWindowThreadProcessId(h, out var pid);
            try
            {
                using var p = Process.GetProcessById((int)pid);
                d["FOCUS_PROCESS_NAME"] = p.ProcessName;
            }
            catch { }
        }
        catch { }
    }

    static void Gpu(ConcurrentDictionary<string, string> d)
    {
        try
        {
            // GPU Engine may have hundreds (per process x engine type) only representative examples of 4 before sampling to avoid a performance explosion
            var cat = new PerformanceCounterCategory("GPU Engine");
            var instances = cat.GetInstanceNames()
                .Where(i => i.Contains("engtype_3D") || i.Contains("engtype_Compute")
                            || i.Contains("engtype_Copy") || i.Contains("engtype_VideoDecode"))
                .Take(4)
                .ToArray();
            float sum = 0;
            foreach (var inst in instances)
            {
                using var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                c.NextValue();
                Thread.Sleep(1);
                sum += c.NextValue();
            }
            d["GPU_USAGE0"] = $"{Math.Round(sum)}";
        }
        catch { }

        try
        {
            var cat = new PerformanceCounterCategory("GPU Adapter Memory");
            var names = cat.GetInstanceNames();
            for (var i = 0; i < Math.Min(names.Length, 4); i++)
            {
                float used = 0, total = 0;
                using (var cu = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", names[i], true))
                {
                    cu.NextValue(); Thread.Sleep(1); used = cu.NextValue();
                }
                using (var cl = new PerformanceCounter("GPU Adapter Memory", "Dedicated Limit", names[i], true))
                {
                    cl.NextValue(); Thread.Sleep(1); total = cl.NextValue();
                }
                if (total <= 0) continue;
                d[$"VRAM_USED{i}"] = $"{Math.Round(used / 1048576f)}";
                d[$"VRAM_TOTAL{i}"] = $"{Math.Round(total / 1073741824f)}";
                d[$"VRAM_PERCENT{i}"] = $"{Math.Round(used * 100f / total)}%";
            }
        }
        catch { }
    }
}
