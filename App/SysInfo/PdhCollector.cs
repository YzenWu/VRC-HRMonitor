using System.Diagnostics;
using System.Runtime.InteropServices;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.SysInfo;

/// <summary>
/// PDH (pdh.dll) Real-time counters collected and exported double. See Skills/PerfGet.skill:
/// PDH is co-sourced with the task manager, maintaining a long life-cycle query, taking Collect increments each time, and avoiding GC shaking with PerformanceCounter.
/// Wisdom frequency is converted using the `\Processor Information(_Total)\% Processor Performance` × nominal frequency.
/// </summary>
public sealed class PdhCollector : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFmtCounterValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    private IntPtr _query = IntPtr.Zero;
    private readonly Dictionary<string, IntPtr> _counters = new();
    private bool _primed;

    /// <summary> symmetrical frequency (MHz), read from the registration form and used for smart-frequency conversion. </summary>
    public double BaseMhz { get; private set; }

    public bool Available => _query != IntPtr.Zero;

    public void Init()
    {
        if (_query != IntPtr.Zero) return;
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
        {
            _query = IntPtr.Zero;
            App.Log.Debug(LogText.L("log.pdh.open_fail"));
            return;
        }
        Add("CPU_USAGE_F", @"\Processor Information(_Total)\% Processor Utility");
        Add("CPU_PERF_F", @"\Processor Information(_Total)\% Processor Performance");
        Add("CPU_QUEUE_F", @"\System\Processor Queue Length");
        Add("PROC_COUNT_F", @"\System\Processes");
        Add("THREAD_COUNT_F", @"\System\Threads");
        Add("CTX_SWITCH_F", @"\System\Context Switches/sec");
        Add("MEM_AVAILABLE_BYTES_F", @"\Memory\Available Bytes");
        Add("MEM_COMMITTED_BYTES_F", @"\Memory\Committed Bytes");
        Add("MEM_COMMIT_PERCENT_F", @"\Memory\% Committed Bytes In Use");
        Add("DISK_READ_BYTES_F", @"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
        Add("DISK_WRITE_BYTES_F", @"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
        Add("DISK_BYTES_F", @"\PhysicalDisk(_Total)\Disk Bytes/sec");
        Add("DISK_QUEUE_F", @"\PhysicalDisk(_Total)\Current Disk Queue Length");
        Add("DISK_TIME_F", @"\PhysicalDisk(_Total)\% Disk Time");
        BaseMhz = ReadBaseMhz();
        // The first collection was based on a baseline and was meaningless.
        PdhCollectQueryData(_query);
    }

    private void Add(string key, string path)
    {
        if (PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter) == 0)
            _counters[key] = counter;
        else
            App.Log.Debug(LogText.L("log.pdh.counter_unavailable", path));
    }

    private static double ReadBaseMhz()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var mhz = key?.GetValue("~MHz");
            return mhz != null ? Convert.ToDouble(mhz) : 0;
        }
        catch { return 0; }
    }

    /// <summary> collects round and writes name → double. The first round returns empty (two samples required for PDH). </summary>
    public Dictionary<string, double> Collect()
    {
        var result = new Dictionary<string, double>();
        if (_query == IntPtr.Zero) return result;
        if (PdhCollectQueryData(_query) != 0) return result;
        if (!_primed)
        {
            _primed = true;
            return result;
        }
        foreach (var (key, counter) in _counters)
        {
            if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, out _, out var v) != 0) continue;
            if (v.CStatus != 0) continue;
            result[key] = v.DoubleValue;
        }
        // Wisdom frequency: nominal frequency x percentage performance
        if (BaseMhz > 0 && result.TryGetValue("CPU_PERF_F", out var perf))
            result["CPU_FREQ_F"] = BaseMhz * perf / 100.0;
        return result;
    }

    /// <summary> system handle (NtQuerySystemInformation is too heavy, call for sum with process number; failed to return 0). </summary>
    public static double HandleCount()
    {
        try
        {
            double sum = 0;
            foreach (var p in Process.GetProcesses())
            {
                try { sum += p.HandleCount; } catch { }
                finally { p.Dispose(); }
            }
            return sum;
        }
        catch { return 0; }
    }

    public void Dispose()
    {
        if (_query == IntPtr.Zero) return;
        PdhCloseQuery(_query);
        _query = IntPtr.Zero;
        _counters.Clear();
    }
}
