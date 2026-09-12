using System.Diagnostics;
using System.Runtime.InteropServices;
using HeartRateMonitor.Core;

namespace HeartRateMonitor;

/// <summary> process/control desk test: Determines whether the control desk is drawn up, attached/distributed. </summary>
public static class ProcessInfo
{
    [DllImport("kernel32.dll")]
    static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    static extern bool FreeConsole();

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_RESTORE = 9;
    private static readonly object CrashWatchLock = new();
    private static Process? _crashWatch;

    public const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    /// Is there a console window attached to <summary>. </summary>
    public static bool HasConsole => GetConsoleWindow() != IntPtr.Zero;

    /// <summary>Ensures a console is available without replacing redirected standard handles.</summary>
    public static void EnsureConsole()
    {
        if (HasConsole) return;
        if (Console.IsInputRedirected || Console.IsOutputRedirected) return;
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
            AllocConsole();
    }

    /// <summary> Release Console (By CLI exit, avoid GUI remaining black windows). </summary>
    public static void ReleaseConsole()
    {
        FreeConsole();
    }

    /// <summary> opens URL with the system default browser (repeated only when the inner Web UI shell is missing). </summary>
    public static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.proc.open_browser_fail", e.Message));
        }
    }

    /// <summary>
    /// Opens the built-in Web UI window (hrmXQ3QXZ.exe, WebView2 shell, the same directory as the main program).
    /// When the shell is missing/starting failed, use <paramref name="fallbackBrowser"/> to decide whether to return the system browser.
    /// Returns whether the shell is successfully pulled.
    /// </summary>
    public static bool LaunchWebUi(int port, bool fallbackBrowser = true)
    {
        var url = App.WebUrl(port: port);
        var exe = Path.Combine(App.ExeDir, "hrm-webui.exe");
        if (!File.Exists(exe))
        {
            App.Log.Warn(LogText.L(fallbackBrowser ? "log.proc.webui_missing_fallback" : "log.proc.webui_missing"));
            if (fallbackBrowser) OpenBrowser(url);
            return false;
        }
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = App.ExeDir,
                UseShellExecute = true,
            };
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(port.ToString());
            psi.ArgumentList.Add("--scheme");
            psi.ArgumentList.Add(App.WebScheme);
            psi.ArgumentList.Add("--engine-pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("--engine-path");
            psi.ArgumentList.Add(Environment.ProcessPath ?? Path.Combine(App.ExeDir, "HeartRateMonitor.exe"));
            if (App.DebugMode) psi.ArgumentList.Add("--debug");
            psi.ArgumentList.Add("--jump");
            Process.Start(psi);
            return true;
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.proc.webui_start_fail", e.Message));
            if (fallbackBrowser) OpenBrowser(url);
            return false;
        }
    }

    /// <summary>
    /// Opens the main frontend by starting the web service and then launching the WebView shell.
    /// egui frontend has been removed; the shell is missing only by hint and does not retreat (the log gives reasons).
    /// </summary>
    public static void LaunchFrontend()
    {
        // The shell requires a back-end Web service; only the window is opened here and the window is pulled from below.
        // When service fails, no hull must be pulled: failure to detect the shell will in turn pull another back end into an avalanche that pulls up.
        if (App.Web?.Start(openUi: false) == false)
        {
            App.Log.Warn(LogText.L("log.proc.web_unavailable"));
            return;
        }
        LaunchWebUi(App.WebPort, fallbackBrowser: true);
    }

    /// <summary>Starts the optional `hrmdump.exe` crash watcher; silently skips it when the executable is absent.</summary>
    public static void LaunchCrashWatch()
    {
        lock (CrashWatchLock)
        {
            try
            {
                if (_crashWatch is { HasExited: false }) return;
                _crashWatch?.Dispose();
                _crashWatch = null;

                var exe = Path.Combine(App.ExeDir, "hrmdump.exe");
                if (!File.Exists(exe)) return;
                _crashWatch = Process.Start(new ProcessStartInfo(exe, $"watch {Environment.ProcessId}")
                {
                    WorkingDirectory = App.ExeDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch
            {
                _crashWatch?.Dispose();
                _crashWatch = null;
            }
        }
    }

    /// <summary>Stops the watchdog launched for this engine instance during a normal shutdown.</summary>
    public static void StopCrashWatch()
    {
        Process? watch;
        lock (CrashWatchLock)
        {
            watch = _crashWatch;
            _crashWatch = null;
        }
        if (watch == null) return;
        try
        {
            if (!watch.HasExited)
            {
                watch.Kill();
                watch.WaitForExit(1000);
            }
        }
        catch { }
        finally { watch.Dispose(); }
    }

    // ---- Thirty-second round #1/#2: Case management (Kill old example / take the front shell off on exit) -

    /// <summary>
    /// End the old GUI instance (Thirty-second round 1 branch Kill):
    /// PID marks the precise killing of a lock-held engine (an example of a CLI diagnosis that does not cause an error).
    /// Then end its front-case hrmX-webui (a new case starts pulling a new shell).
    /// Revert all other engine examples when the tag is missing/defunct (old version or fallback residue).
    /// </summary>
    public static void KillGuiInstance()
    {
        var self = Environment.ProcessId;
        var holder = Core.SingleInstance.TryReadHolderPid();
        var targeted = false;
        if (holder is { } pid && pid != self)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    targeted = true;
                }
            }
            catch { /*Target withdrawn or denied access*/ }
        }
        if (!targeted)
        {
            // Owner not found: Clear all other engine examples (Kill syntax = exit from the old program)
            foreach (var p in Process.GetProcessesByName("HeartRateMonitor"))
            {
                using (p)
                {
                    if (p.Id == self) continue;
                    try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
        KillShells();
        WaitForProcessExit("HeartRateMonitor", self);
    }

    /// <summary>Ends only frontend shells launched from this engine's installation directory.</summary>
    public static void KillShells()
    {
        var expected = Path.GetFullPath(Path.Combine(App.ExeDir, "hrm-webui.exe"));
        foreach (var p in Process.GetProcessesByName("hrm-webui"))
        {
            using (p)
            {
                try
                {
                    if (!p.HasExited && string.Equals(Path.GetFullPath(p.MainModule?.FileName ?? ""), expected, StringComparison.OrdinalIgnoreCase))
                        p.Kill(entireProcessTree: true);
                }
                catch { }
            }
        }
    }

    private static void WaitForProcessExit(string name, int exceptSelf)
    {
        for (var i = 0; i < 20; i++)
        {
            var alive = false;
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    if (p.Id != exceptSelf && !p.HasExited)
                    {
                        alive = true;
                        break;
                    }
                }
            }
            if (!alive) return;
            Thread.Sleep(150);
        }
    }

    /// <summary>Force-terminates only child processes of this engine from the same installation directory.</summary>
    public static void KillOwnedProcesses()
    {
        var self = Environment.ProcessId;
        var expectedDir = Path.GetFullPath(App.ExeDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == self || ParentProcessId(p.Id) != self) continue;
                    var path = Path.GetFullPath(p.MainModule?.FileName ?? "");
                    if (path.StartsWith(expectedDir, StringComparison.OrdinalIgnoreCase)) p.Kill(entireProcessTree: true);
                }
                catch { }
            }
        }
    }

    public static void ForceExitCurrentProcess() => TerminateProcess(GetCurrentProcess(), 0);

    private static int ParentProcessId(int pid)
    {
        var pbi = new PROCESS_BASIC_INFORMATION();
        var status = NtQueryInformationProcess(Process.GetProcessById(pid).Handle, 0, ref pbi, Marshal.SizeOf(pbi), out _);
        return status == 0 ? pbi.InheritedFromUniqueProcessId.ToInt32() : -1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

    /// <summary>Notifies this installation's frontend shell that engine cleanup is complete and process exit is imminent.</summary>
    public static void NotifyFrontendExit()
    {
        try { NotifyShellExit(); }
        catch { /* Shell failure does not stop the engine. */ }
    }

    /// <summary>
    /// Requests frontend shells to close, then force-terminates only shells from this installation after a short grace period.
    /// </summary>
    public static void ShutdownFrontend(bool webReachable, int graceMs = 2000)
    {
        try
        {
            NotifyShellExit();
            Thread.Sleep(Math.Clamp(webReachable ? graceMs : Math.Min(graceMs, 300), 0, 2000));
            KillShells();
        }
        catch { /* Shell failure does not stop the engine. */ }
    }

    /// <summary>Posts the registered `hrm-engine-exit` message only to this installation's top-level `hrm-webui` windows.</summary>
    private static void NotifyShellExit()
    {
        var msg = RegisterWindowMessage("hrm-engine-exit");
        var expected = Path.GetFullPath(Path.Combine(App.ExeDir, "hrm-webui.exe"));
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var wp);
            try
            {
                using var p = Process.GetProcessById((int)wp);
                var actual = Path.GetFullPath(p.MainModule?.FileName ?? "");
                if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    PostMessage(h, msg, IntPtr.Zero, IntPtr.Zero);
            }
            catch { /* Process exited or its executable path is inaccessible */ }
            return true;
        }, IntPtr.Zero);
    }

    /// Whether <summary> has a front-end process (including a window that is being started and has not yet been built). Use to inhibit repetition. </summary>
    public static bool FrontendRunning()
    {
        var self = Environment.ProcessId;
        try
        {
            foreach (var p in Process.GetProcessesByName("hrm-webui"))
            {
                using (p)
                {
                    if (p.Id != self && !p.HasExited) return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Brings an existing instance's UI to the foreground, preferring its frontend window.
    /// Returns false when no window is available, allowing the caller to launch or activate the UI through another path.
    /// </summary>
    public static bool ActivateExistingUi()
    {
        var self = Environment.ProcessId;
        foreach (var name in new[] { "hrm-webui", "HeartRateMonitor" })
        {
            Process[] list;
            try { list = Process.GetProcessesByName(name); }
            catch { continue; }
            foreach (var p in list)
            {
                try
                {
                    if (p.Id == self) continue;
                    var h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;
                    if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
                    if (SetForegroundWindow(h)) return true;
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        return false;
    }
}
