using System.Net.Http;
using System.Text;

namespace HeartRateMonitor.Core;

/// <summary>
/// Single-instance mutex for GUI mode: claim a named Mutex, and on duplicate launch, activate the running instance before exiting.
/// Set app.allow_multi_instance = true or pass --multi to allow multiple instances for debugging.
/// CLI mode is unrestricted because it has no tray icon and may coexist with the GUI for diagnostics.
/// The GUI instance holding the lock writes its PID to %LOCALAPPDATA%\HeartRateMonitor\gui_instance.pid,
/// allowing round 32 item #1, "Kill old instance," to target the lock holder without affecting concurrent CLI diagnostic processes.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\hrm_v1_single_instance";
    private static Mutex? _mutex;

    private static string HolderPidPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeartRateMonitor",
            "gui_instance.pid");

    /// <summary>Acquire the instance lock; false means another instance is running.</summary>
    public static bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out var createdNew);
            if (createdNew)
            {
                MarkHolder();
                return true;
            }
            // A named object already exists, possibly left by a crash; if a short wait acquires the abandoned lock, continue startup.
            try
            {
                if (_mutex.WaitOne(200))
                {
                    MarkHolder();
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                MarkHolder();
                return true;
            }
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch
        {
            // Do not block startup when kernel objects are unavailable because of permissions or sandboxing.
            MarkHolder();
            return true;
        }
    }

    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
        try { File.Delete(HolderPidPath); } catch { /* Cleanup failure is harmless. */ }
    }

    /// <summary>Read the current GUI instance PID; it may be from an old version or crash residue, so callers must verify the process is alive.</summary>
    public static int? TryReadHolderPid()
    {
        try
        {
            var s = File.ReadAllText(HolderPidPath).Trim();
            return int.TryParse(s, out var pid) && pid > 0 ? pid : null;
        }
        catch
        {
            return null;
        }
    }

    private static void MarkHolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(HolderPidPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(HolderPidPath, Environment.ProcessId.ToString());
        }
        catch { /* Write failure does not affect the single-instance mutex. */ }
    }

    /// <summary>
    /// Use local HTTP POST /api/activate to ask the running instance to bring up its interface.
    /// Used only when the new process cannot find an activatable window because the old instance may exist only in the tray; its persistent Web service remains reachable.
    /// </summary>
    public static bool NotifyRunning()
    {
        try
        {
            var port = App.WebPort is > 0 and < 65536 ? App.WebPort : 9460;
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1200) };
            using var resp = client.PostAsync(App.WebUrl("/api/activate", port), new StringContent("{}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
