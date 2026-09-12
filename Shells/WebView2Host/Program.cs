using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;

namespace WebView2Host;

/// <summary>
/// WebView2 shell entry point: probes the backend Web service (port 9460 by default), starts the main program with --web if necessary, then opens the main form.
/// Usage: hrm-webui.exe [--port 9460] [--scheme http|https] [--launch-exe &lt;path&gt;] [--engine-pid &lt;pid&gt;] [--engine-path &lt;path&gt;] [--debug] [--jump]
/// Single instance: when a shell is already running, displays the native three-option prompt (Cancel/Kill/Jump, round 32 #1).
/// --jump = internal activation (tray "Open UI" / engine-initiated launch): silently activates the existing shell and exits.
/// </summary>
internal static class Program
{
    private const int DefaultPort = 9460;

    /// <summary>Activation signal: the tray "Open UI" action starts another shell, whose process sets this event so the existing process displays its window.</summary>
    internal const string ShowEventName = @"Local\hrm-webui-show";

    private const string JumpArg = "--jump";

    [STAThread]
    private static void Main(string[] args)
    {
        // P2: cheap component probe used by the version check; exits before any window or backend probing.
        if (VersionJson.TryRun(args, "webui")) return;

        int port = DefaultPort;
        string? launchExe = null;
        int enginePid = 0;
        string? enginePath = null;
        string scheme = "http";
        bool debug = false;
        bool jump = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length && int.TryParse(args[++i], out int p):
                    port = p;
                    break;
                case "--launch-exe" when i + 1 < args.Length:
                    launchExe = args[++i];
                    break;
                case "--engine-pid" when i + 1 < args.Length && int.TryParse(args[++i], out int pid):
                    enginePid = pid;
                    break;
                case "--engine-path" when i + 1 < args.Length:
                    enginePath = args[++i];
                    break;
                case "--scheme" when i + 1 < args.Length:
                    scheme = string.Equals(args[++i], "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                    break;
                case "--debug":
                    debug = true;
                    break;
                case JumpArg:
                    jump = true;
                    break;
            }
        }

        // The single-instance prompt uses TaskDialog: initialize visual styles (Common-Controls v6) before entering the guard logic
        ApplicationConfiguration.Initialize();

        // If a shell is already running: --jump (internal activation) silently activates it and exits; a manual second launch shows the native three-option prompt
        bool killOld = false;
        if (EventWaitHandle.TryOpenExisting(ShowEventName, out var running))
        {
            using (running)
            {
                if (jump)
                {
                    running.Set();
                    return;
                }
                switch (AskSecondInstance())
                {
                    case SecondAction.Cancel:
                        return;
                    case SecondAction.Jump:
                        running.Set();
                        return;
                    case SecondAction.Kill:
                        killOld = true;
                        break;
                }
            }
        }
        if (killOld)
        {
            // Terminate the old shell (including its WebView child processes), wait for a clean exit, then continue as a fresh launch (the event is released with the old process)
            KillShellProcesses();
        }
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        // Show the splash at process startup; it closes after NavigationCompleted.
        // A 20-second timeout changes it to an explicit, user-dismissible error state instead of silently hiding it.
        using var splash = new SplashForm();
        splash.Step(5, ShellText.Pick("启动中…", "Starting…"));
        splash.Show();
        splash.Refresh(); // The message loop is not running yet, so draw the first frame synchronously

        var startUrl = $"{scheme}://127.0.0.1:{port}/webui/";
        if (!ProbePort(port))
        {
            splash.Step(18, ShellText.Pick("等待后端引擎…", "Waiting for backend engine…"));
            if (!TryLaunchBackend(port, launchExe, splash))
            {
                MessageBox.Show(
                    ShellText.F(
                        $"后端 Web 服务未运行（{startUrl}）。\n\n请先启动 HeartRateMonitor（--web），或把 hrm-webui.exe 与 HeartRateMonitor.exe 放在同一目录后重试。",
                        $"The backend Web service is not running ({startUrl}).\n\nStart HeartRateMonitor (--web) first, or put hrm-webui.exe next to HeartRateMonitor.exe and retry."),
                    "HeartRateMonitor · Web UI",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        splash.Step(45, ShellText.Pick("初始化 WebView2…", "Initializing WebView2…"));

        Application.Run(new MainForm(startUrl, debug, showSignal, splash, enginePid, enginePath));
    }

    private enum SecondAction { Cancel, Kill, Jump }

    /// <summary>Uses the native Windows TaskDialog to ask how to handle a duplicate launch (round 32 #1).</summary>
    private static SecondAction AskSecondInstance()
    {
        var zh = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var jump = new TaskDialogButton(zh ? "转到已有实例" : "Jump to existing instance");
        var kill = new TaskDialogButton(zh ? "结束旧实例并重开" : "Kill old instance and reopen");
        var cancel = new TaskDialogButton(zh ? "取消" : "Cancel");
        var page = new TaskDialogPage
        {
            Caption = "HeartRateMonitor · Web UI",
            Heading = zh ? "程序已在运行" : "Already running",
            Text = zh
                ? "检测到已有界面实例在运行。\n\n· 转到已有实例：显示现有窗口，本进程退出；\n· 结束旧实例并重开：关闭旧的界面进程后打开全新窗口（不影响已在运行的后端）；\n· 取消：什么都不做。"
                : "Another UI instance is already running.\n\n· Jump to existing: bring the current window up and exit this process.\n· Kill and reopen: close the running UI process and open a fresh window (the backend keeps running).\n· Cancel: do nothing.",
            Icon = TaskDialogIcon.Warning,
            AllowCancel = true,
            DefaultButton = jump,
            Buttons = { jump, kill, cancel },
        };
        var clicked = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
        if (clicked == kill) return SecondAction.Kill;
        if (clicked == jump) return SecondAction.Jump;
        return SecondAction.Cancel;
    }

    /// <summary>Terminates other hrm-webui shell processes, including their entire process trees, and waits for them to exit cleanly.</summary>
    private static void KillShellProcesses()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("hrm-webui"))
            {
                using (p)
                {
                    if (p.Id == Environment.ProcessId || p.HasExited) continue;
                    try { p.Kill(entireProcessTree: true); } catch { /* Already exited or access denied */ }
                }
            }
            for (var i = 0; i < 30; i++)
            {
                bool alive = false;
                foreach (var p in Process.GetProcessesByName("hrm-webui"))
                {
                    using (p)
                    {
                        if (p.Id != Environment.ProcessId && !p.HasExited)
                        {
                            alive = true;
                            break;
                        }
                    }
                }
                if (!alive) break;
                Thread.Sleep(150);
            }
        }
        catch { /* Enumeration failure does not block this launch */ }
    }

    private static bool ProbePort(int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync("127.0.0.1", port);
            return task.Wait(1200) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLaunchBackend(int port, string? launchExe, SplashForm? splash)
    {
        string exe = launchExe
            ?? Path.Combine(AppContext.BaseDirectory, "HeartRateMonitor.exe");
        if (!File.Exists(exe)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--web") { UseShellExecute = true });
            for (int i = 0; i < 50; i++)
            {
                Thread.Sleep(300);
                // While waiting without a message loop, pump messages to keep the splash painted and advance progress with elapsed time (capped at 40%)
                splash?.Pump();
                if (splash != null && (i % 3) == 0) splash.Step(Math.Min(40, 18 + i), null);
                if (ProbePort(port)) return true;
            }
        }
        catch
        {
            // Treat launch failure as not ready; the main form's error page handles the fallback
        }
        return ProbePort(port);
    }
}
