using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HrmDump;

/// <summary>
/// hrmdump.exe is a standalone crash watchdog (round 26).
/// Usage: hrmdump watch &lt;enginePid&gt;
/// A normal engine (HeartRateMonitor.exe) exit (code 0) ends silently.
/// On abnormal exit (code != 0, including exit1 / null / kill crash tests):
///   1. If hrm-webui is still running, broadcast the registered hrm-engine-crash message to its main window (wParam = exit code).
///   2. If the shell is also gone, write diagnostics to %TEMP%\HeartRateMonitor-crash\crash_*.txt.
/// It does not reference the main assembly, so it remains independent after an engine crash.
/// </summary>
internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    const uint CRASH_MSG_NAME = 0; // Placeholder; obtained from RegisterWindowMessage at runtime.

    [STAThread]
    static int Main(string[] args)
    {
        // P2: cheap component probe used by the version check; exits before any watchdog logic runs.
        if (VersionJson.TryRun(args, "dump")) return 0;

        // When launched directly without watch arguments, start the main program with its frontend shell and exit.
        if (args.Length < 2 || !string.Equals(args[0], "watch", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(args[1], out var pid))
        {
            LaunchGuiAndExit();
            return 0;
        }

        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.WaitForExit();
            if (proc.ExitCode == 0) return 0; // Normal exit; stay silent.
            NotifyOrDump(pid, proc.ExitCode);
            return 0;
        }
        catch (Exception e)
        {
            // The engine disappeared before the watchdog attached (startup crash or killed).
            NotifyOrDump(pid, -1, e.Message);
            return 0;
        }
    }

    /// <summary>Delegate this launch to the main program, preferring hrm-webui.exe and then the engine, which launches its own UI.</summary>
    static void LaunchGuiAndExit()
    {
        var dir = AppContext.BaseDirectory;
        foreach (var exe in new[] { "hrm-webui.exe", "HeartRateMonitor.exe" })
        {
            var path = Path.Combine(dir, exe);
            if (!File.Exists(path)) continue;
            try
            {
                // The shell handles second instances itself; --jump silently recalls the running instance for this open-UI launch.
                Process.Start(new ProcessStartInfo(path)
                {
                    Arguments = path.EndsWith("hrm-webui.exe", StringComparison.OrdinalIgnoreCase) ? "--jump" : null,
                    WorkingDirectory = dir,
                    UseShellExecute = true,
                });
            }
            catch { /* Stay silent on launch failure; the user can start the main program manually. */ }
            break;
        }
    }

    static void NotifyOrDump(int pid, int code, string? extra = null)
    {
        var msg = RegisterWindowMessage("hrm-engine-crash");
        var notified = false;
        try
        {
            Process[] shells;
            try { shells = Process.GetProcessesByName("hrm-webui"); }
            catch { shells = Array.Empty<Process>(); }
            foreach (var p in shells)
            {
                try
                {
                    if (p.HasExited) continue;
                    var h = p.MainWindowHandle;
                    if (h != IntPtr.Zero)
                    {
                        PostMessage(h, msg, (IntPtr)code, IntPtr.Zero);
                        notified = true;
                    }
                    else
                    {
                        // If the shell has not created its window yet, such as during startup, enumerate its top-level windows.
                        EnumWindows((w, _) =>
                        {
                            GetWindowThreadProcessId(w, out var wp);
                            if (wp == (uint)p.Id) { PostMessage(w, msg, (IntPtr)code, IntPtr.Zero); notified = true; }
                            return true;
                        }, IntPtr.Zero);
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }

        // Do not write a dump while the shell can display the failure and offer restart; write to %temp% only when both sides are gone.
        if (notified) return;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "HeartRateMonitor-crash");
            Directory.CreateDirectory(dir);
            var name = extra == null ? $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt" : "crash_orphan.txt";
            File.WriteAllText(
                Path.Combine(dir, name),
                $"time   : {DateTime.Now:O}\r\nengine : pid={pid} exit={code}\r\nshell  : not running (engine and frontend both died)\r\n{(extra == null ? "" : "note   : " + extra + "\r\n")}");
        }
        catch { }
    }
}
