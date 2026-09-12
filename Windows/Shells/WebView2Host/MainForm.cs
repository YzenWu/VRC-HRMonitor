using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WebView2Host;

/// <summary>
/// Main form: a WebView2 host without a system title bar (the page draws a macOS-style traffic-light title bar).
/// · Rounded corners: custom SetWindowRgn radius (configurable in px, automatically disabled when maximized or fullscreen)
/// · Theme: the page sends the resolved background color (win.theme), synchronizing the form background and DWM border color
/// · Drag/resize: WM_NCHITTEST delegates edges to the system; the page triggers title-bar dragging via win.drag
/// · F5 reload / F11 fullscreen / F12 developer tools (status bar and DevTools disabled outside Debug mode)
/// · Window size, zoom level, corner radius, and theme background persist to webui-shell.json
/// </summary>
public sealed class MainForm : Form
{
    /// <summary>Default client area (DIP at 96 DPI): fits the left navigation (224), three panel columns, and the widest form page.</summary>
    private static readonly Size DefaultClient = new(1440, 900);

    /// <summary>Minimum client area (DIP): width must exceed the frontend's 860px drawer breakpoint to avoid narrow-screen layout in the desktop shell.</summary>
    private static readonly Size MinClient = new(1024, 640);

    /// <summary>Allowed aspect-ratio range (width / height).</summary>
    private const double MinAspect = 1.05;
    private const double MaxAspect = 2.60;

    /// <summary>Resize hit-zone width (DIP): the form border outside WebView used for WM_NCHITTEST hits.</summary>
    private const int BorderDip = 6;

    private readonly string _startUrl;
    private readonly WebView2 _web = new();
    private readonly ShellState _state = ShellState.Load();
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 1200 };

    private bool _fullScreen;
    private FormWindowState _preFullScreenState = FormWindowState.Normal;
    /// <summary>Whether the page is ready to receive win.close-request and display a confirmation dialog.</summary>
    private bool _pageReady;
    /// <summary>The page confirmed exit, so OnFormClosing no longer intercepts it.</summary>
    private bool _closeConfirmed;
    /// <summary>Debug mode (synchronized through page message win.debug): controls DevTools availability; the WebView status bar is always disabled (round 31 #1).</summary>
    private bool _debug;
    /// <summary>Whether DWM non-client rendering has been disabled on Win10 as a fallback when the border-color attribute is unavailable; done only once.</summary>
    private bool _ncRenderingOff;
    /// <summary>Activation signal: when the tray starts another "Open UI" process, it sets this event to show the hidden window.</summary>
    private readonly RegisteredWaitHandle? _showWait;

    /// <summary>Crash-guard broadcast message (posted by hrmdump after an abnormal engine exit).</summary>
    private uint _crashMsg;
    /// <summary>Normal engine-exit broadcast (the engine notifies the shell to close itself before exiting, round 32 #2).</summary>
    private uint _exitMsg;
    /// <summary>Whether the engine has exited on its own (win.exit / hrm-engine-exit), so closing no longer kills any backend residue.</summary>
    private bool _engineStopping;
    /// <summary>PID and executable path of the engine instance that launched this shell.</summary>
    private int _enginePid;
    private string _enginePath = "";
    private bool _restarting;
    /// <summary>Startup splash (#26): shown at process startup and dismissed by OnNavigationCompleted once the page is ready.</summary>
    private readonly SplashForm? _splash;
    /// <summary>Web port, used to probe backend reachability and as a forced-termination fallback.</summary>
    private int _port = 9460;
    /// <summary>Fallback for page responses such as close confirmation: force exit after N seconds if the page cannot respond (offline/crash overlay), preventing an unclosable window.</summary>
    private bool _waitingClose;
    private CancellationTokenSource? _closeCts;
    private const int CloseFallbackMs = 6000;
    private const int ShutdownTimeoutMs = 10000;

    public MainForm(string startUrl, bool debug = false, EventWaitHandle? showSignal = null, SplashForm? splash = null, int enginePid = 0, string? enginePath = null)
    {
        _startUrl = startUrl;
        _debug = debug;
        _splash = splash;
        _enginePid = enginePid;
        _enginePath = NormalizePath(enginePath);
        try { _port = new Uri(startUrl).Port; } catch { /* Keep 9460 */ }
        if (showSignal != null)
        {
            _showWait = ThreadPool.RegisterWaitForSingleObject(
                showSignal,
                (_, _) =>
                {
                    try { if (!IsDisposed) BeginInvoke(ShowFromTray); }
                    catch { /* Window already destroyed */ }
                },
                null,
                Timeout.Infinite,
                false);
        }
        Text = "HeartRateMonitor · Web UI";
        StartPosition = FormStartPosition.Manual;
        // No system title bar: the page draws a macOS-style title bar
        FormBorderStyle = FormBorderStyle.None;
        // Crash-guard messages and engine path (the guard may become ready only after this process starts, so obtain them early)
        _crashMsg = RegisterWindowMessage("hrm-engine-crash");
        _exitMsg = RegisterWindowMessage("hrm-engine-exit");
        FindEnginePath();
        // Reserve the border area for the form so WebView does not cover it and prevent resize hit zones from receiving mouse messages
        Padding = new Padding(BorderDip);
        // Use the last background color synchronized by the page to avoid a mismatched flash during cold start
        BackColor = ParseHex(_state.ThemeBg) ?? Color.FromArgb(0x1C, 0x21, 0x28);
        DoubleBuffered = true;

        // Icon (#34): first use the path specified by icons in the output directory's Release.json (shell role webui, falling back to app).
        // If unconfigured or missing, fall back to image/app.ico (deployed from the manifest); leave unset if neither exists.
        foreach (var p in new[] { ManifestIconPath("webui"), ManifestIconPath("app"), LegacyIconPath("app.ico") })
        {
            if (p == null || !File.Exists(p)) continue;
            try { Icon = new Icon(p); break; } catch { /* Ignore invalid icon */ }
        }

        _web.Dock = DockStyle.Fill;
        Controls.Add(_web);
        // WebView uses a white background by default during cold start, one source of white corner gaps/flashes; preload the last theme background
        try { _web.DefaultBackgroundColor = ParseHex(_state.ThemeBg) ?? Color.FromArgb(0x1C, 0x21, 0x28); }
        catch { /* Property unavailable in older SDK versions */ }

        Load += OnLoad;
        // WebView2 emits function keys through its own KeyDown event, bypassing form message preprocessing.
        // Attach both handlers while keeping KeyPreview false so one keystroke is not handled twice.
        KeyPreview = false;
        KeyDown += OnKeyDown;
        _web.KeyDown += OnKeyDown;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveState(); };
        Resize += (_, _) => { ApplyCorner(); PushWindowState(); };
        FormClosed += (_, _) =>
        {
            // Close the splash even if the window exits before the page is ready (#26)
            _splash?.RequestClose();
            try { _closeCts?.Cancel(); _closeCts?.Dispose(); } catch { /* ignore */ }
            try { _showWait?.Unregister(null); } catch { /* ignore */ }
            try { _saveTimer.Dispose(); } catch { /* ignore */ }
            try { _web.Dispose(); } catch { /* ignore */ }
        };
    }

    /// <summary>
    /// Current scaling factor. Under PerMonitorV2, ClientSize and MinimumSize are physical pixels,
    /// while WebView2 CSS pixels equal physical pixels divided by this factor, so DIP constants must be multiplied back first.
    /// </summary>
    private double Scale => DeviceDpi <= 0 ? 1.0 : DeviceDpi / 96.0;

    private int Px(int dip) => (int)Math.Round(dip * Scale);

    /// <summary>
    /// A borderless window must remain system-resizable: DefWindowProc's SC_SIZE loop requires WS_THICKFRAME,
    /// which FormBorderStyle.None removes. Restore WS_THICKFRAME and the minimize/maximize flags here
    /// (the former enables Aero Snap, while the latter preserves taskbar animations), then flatten the non-client thickness in WM_NCCALCSIZE,
    /// keeping the window visually borderless.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Borderless windows still have a 1px DWM border: Win11 22000+ later sets it to the theme color with DWMWA_BORDER_COLOR.
        // Win10 does not support this attribute (E_INVALIDARG), so disable non-client rendering here before the first frame.
        // Otherwise startup briefly shows a border in the system color (white under a light theme) until the window moves or redraws.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            DisableDwmBorder();
        ApplyMinimumSize();
        ClientSize = InitialClientSize();
        RestoreInitialLocation();
        // A maximized borderless window covers the taskbar, so constrain it to the working area
        MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
        if (_state.Maximized) WindowState = FormWindowState.Maximized;
        ApplyCorner();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // The scaling factor changed while dragging across displays; recalculate minimum size and borders instead of reusing physical pixels from the old DPI
        ApplyMinimumSize();
        MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
        ApplyCorner();
    }

    /// <summary>Physical pixel size of the non-client area (border + title bar), zero when borderless.</summary>
    private Size Frame => new(Size.Width - ClientSize.Width, Size.Height - ClientSize.Height);

    /// <summary>
    /// Minimum size: convert DIP constants to physical pixels without exceeding the display working area
    /// (high-scaling small displays lack the logical space, and forcing it would push the window off-screen).
    /// </summary>
    private void ApplyMinimumSize()
    {
        var work = Screen.FromHandle(Handle).WorkingArea;
        var frame = Frame;
        MinimumSize = new Size(
            Math.Min(Px(MinClient.Width) + frame.Width, work.Width),
            Math.Min(Px(MinClient.Height) + frame.Height, work.Height));
    }

    /// <summary>Startup size: prefer the last recorded size (stored in DIP), otherwise the default; then constrain it to the target display's working area.</summary>
    private Size InitialClientSize()
    {
        var work = InitialScreen().WorkingArea;
        var frame = Frame;
        int maxW = Math.Max(1, work.Width - frame.Width);
        int maxH = Math.Max(1, work.Height - frame.Height);
        int w = Math.Min(Px(_state.Width > 0 ? _state.Width : DefaultClient.Width), maxW);
        int h = Math.Min(Px(_state.Height > 0 ? _state.Height : DefaultClient.Height), maxH);
        return new Size(w, h);
    }

    private Screen InitialScreen() => _state.Left is { } left && _state.Top is { } top
        ? Screen.FromPoint(new Point(left, top))
        : Screen.FromHandle(Handle);

    /// <summary>Restores the last normal window position; centers on the target display when the old configuration has no coordinates or is outside all working areas.</summary>
    private void RestoreInitialLocation()
    {
        if (_state.Left is { } left && _state.Top is { } top)
        {
            var savedBounds = new Rectangle(left, top, Width, Height);
            if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(savedBounds)))
            {
                Location = savedBounds.Location;
                return;
            }
        }

        var work = InitialScreen().WorkingArea;
        Location = new Point(
            work.Left + (work.Width - Width) / 2,
            work.Top + (work.Height - Height) / 2);
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        _splash?.Step(55, ShellText.Pick("初始化 WebView2…", "Initializing WebView2…"));
        try
        {
            // Use a dedicated userDataFolder to avoid conflicts with other WebView2 applications; on failure (such as a missing Edge Runtime), prompt for installation
            string userData = Path.Combine(AppContext.BaseDirectory, "webview2-data");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            _splash?.Step(72, ShellText.Pick("初始化 WebView2…", "Initializing WebView2…"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ShellText.Pick(
                    "无法初始化 WebView2。请安装 Microsoft Edge WebView2 Runtime（Windows 10/11 通常已自带）。\n\n" + ex.Message,
                    "Cannot initialize WebView2. Please install the Microsoft Edge WebView2 Runtime (usually included with Windows 10/11).\n\n" + ex.Message),
                "HeartRateMonitor · Web UI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
            return;
        }

        // Always disable the status bar (bottom-left link/path hint, #1); ApplyDebugChrome controls DevTools according to the Debug switch
        ApplyDebugChrome();
        // The page handles context menus consistently by preventing the window contextmenu event and displaying a custom menu.
        // Keep the default menu enabled here: disabling it at the host layer prevents some WebView2 versions from dispatching the
        // DOM contextmenu event, making the custom menu impossible to open; the default remains as a fallback when preventDefault is not called.
        _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        _web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _web.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        _web.CoreWebView2.NavigationStarting += OnNavigationStarting;
        // The page-drawn title bar requests window operations through window.chrome.webview.postMessage
        _web.CoreWebView2.WebMessageReceived += OnWebMessage;
        // WebView2 does not retain zoom automatically; restore the last factor (browser shortcuts handle Ctrl +/− and Ctrl+wheel)
        if (_state.Zoom is >= 0.25 and <= 5.0) _web.ZoomFactor = _state.Zoom;
        _web.ZoomFactorChanged += (_, _) =>
        {
            // Continuous zoom emits events rapidly; debounce before persisting
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        _web.Source = new Uri(_startUrl);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!TryGetExternalHttpUri(e.Uri, out var uri)) return;
        e.Handled = true;
        OpenInDefaultBrowser(uri);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!TryGetExternalHttpUri(e.Uri, out var uri)) return;
        e.Cancel = true;
        OpenInDefaultBrowser(uri);
    }

    private bool TryGetExternalHttpUri(string value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!)
            || uri.Scheme is not ("http" or "https"))
            return false;

        return !Uri.TryCreate(_startUrl, UriKind.Absolute, out var start)
            || !string.Equals(uri.Scheme, start.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, start.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != start.Port;
    }

    private static void OpenInDefaultBrowser(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { /* Stay on the current page when no default browser is configured */ }
    }

    /// <summary>Displays a retryable error page on load failure instead of a blank screen; always dismisses the splash whether loading succeeds or fails (#26).</summary>
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _splash?.Step(100, null);
        _splash?.RequestClose();
        if (e.IsSuccess)
        {
            // Proactively push window state once the page is ready so title-bar buttons and corner settings synchronize immediately
            _pageReady = true;
            PushWindowState();
            return;
        }
        // The error page has no confirmation dialog, so closing must not wait for a page response
        _pageReady = false;
        string html =
            "<meta charset='utf-8'><body style=\"font-family:'Segoe UI','Microsoft YaHei';background:#2b2b2b;" +
            "color:#ddd;display:grid;place-items:center;height:100vh;margin:0\"><div style='text-align:center'>" +
            $"<h2 style='font-weight:600'>{ShellText.Pick("页面加载失败", "Page failed to load")}（{e.WebErrorStatus}）</h2>" +
            $"<p style='color:#999'>{_startUrl}</p>" +
            ShellText.Pick(
                "<p style='color:#999'>请确认 HeartRateMonitor 已启动且 Web UI 已开启，然后按 F5 重试。</p></div></body>",
                "<p style='color:#999'>Make sure HeartRateMonitor is running with the Web UI enabled, then press F5 to retry.</p></div></body>");
        _web.CoreWebView2.NavigateToString(html);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_web.CoreWebView2 is null) return;
        switch (e.KeyCode)
        {
            case Keys.F12 when _debug:
                _web.CoreWebView2.OpenDevToolsWindow();
                e.Handled = true;
                break;
            case Keys.F5:
                _web.CoreWebView2.Navigate(_startUrl);
                e.Handled = true;
                break;
            case Keys.F11:
                ToggleFullScreen();
                e.Handled = true;
                break;
        }
    }

    // ---- Page → host: window controls (the page contains the title bar, so operations must be sent back) ----

    /// <summary>Retains the exact engine identity supplied by the launcher, with a path-checked discovery fallback for standalone shell launches.</summary>
    private void FindEnginePath()
    {
        if (_enginePid > 0 && EngineIdentityMatches(_enginePid)) return;
        _enginePid = 0;
        try
        {
            var expected = NormalizePath(Path.Combine(AppContext.BaseDirectory, "HeartRateMonitor.exe"));
            foreach (var p in Process.GetProcessesByName("HeartRateMonitor"))
            {
                using (p)
                {
                    var path = NormalizePath(p.MainModule?.FileName);
                    if (p.HasExited || !string.Equals(path, expected, StringComparison.OrdinalIgnoreCase)) continue;
                    _enginePid = p.Id;
                    _enginePath = path;
                    return;
                }
            }
        }
        catch { /* Keep the identity empty when the engine is absent, such as while debugging the shell alone */ }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(path); }
        catch { return ""; }
    }

    private bool EngineIdentityMatches(int pid)
    {
        if (pid <= 0 || string.IsNullOrEmpty(_enginePath)) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && string.Equals(NormalizePath(p.MainModule?.FileName), _enginePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Restarts the engine backend after the page selects "Restart backend" following a crash, or enters safe mode with --safemode.
    /// Secondary verification first removes stale engine processes, since crashed or hung instances may still hold the single-instance lock and Web port,
    /// causing the new instance to exit immediately—the main reason restarts can intermittently fail—then launches the engine and polls
    /// the local Web port until it returns HTTP 200. If the process exits early or times out, retries are attempted; repeated failure is sent back to the page
    /// as engine.restart-failed so it can show an actionable error.</summary>
    private async Task RelaunchEngineAsync(bool safeMode = false, bool afterCrash = false)
    {
        if (_restarting) return;
        if (safeMode && !AskSafeModeStart()) return;
        if (string.IsNullOrEmpty(_enginePath) || !File.Exists(_enginePath))
        {
            _restarting = false;
            PostToPage(new { type = "engine.restart-failed", code = 0 });
            return;
        }
        _restarting = true;
        int port;
        try { port = new Uri(_startUrl).Port; } catch { port = 9460; }

        PostToPage(new { type = "engine.restarting" });
        await KillStaleEnginesAsync();
        const int maxTries = 4;
        for (var attempt = 1; attempt <= maxTries; attempt++)
        {
            try
            {
                var psi = new ProcessStartInfo(_enginePath)
                {
                    WorkingDirectory = Path.GetDirectoryName(_enginePath) ?? AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("--web");
                if (safeMode)
                {
                    psi.ArgumentList.Add("--safemode");
                    psi.ArgumentList.Add("--safemode-confirmed");
                }
                // Automatic restart after a crash: mark the launch reason (round 31 #27 log)
                if (afterCrash) psi.ArgumentList.Add("--after-crash");
                using var proc = Process.Start(psi);
                var up = await WaitWebUpAsync(proc, port, new Uri(_startUrl).Scheme, 15_000);
                if (up)
                {
                    PostToPage(new { type = safeMode ? "engine.safemode-ok" : "engine.restarted" });
                    _restarting = false;
                    return;
                }
            }
            catch { /* Launch exception: retry */ }
            await Task.Delay(600);
        }
        _restarting = false;
        PostToPage(new { type = "engine.restart-failed", code = 0 });
        // After restart failure, the page watchdog continues showing the offline/crash layer so the user can select "Retry" again
    }

    private static bool AskSafeModeStart()
    {
        var confirm = new TaskDialogButton("Confirm");
        var cancel = new TaskDialogButton("Cancel");
        var page = new TaskDialogPage
        {
            Caption = "HeartRateMonitor",
            Heading = "Are You Sure Kill All Instances and Start With Safe Mode?",
            Icon = TaskDialogIcon.Warning,
            AllowCancel = true,
            DefaultButton = cancel,
            Buttons = { confirm, cancel },
        };
        return TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen) == confirm;
    }

    /// <summary>Removes engine instances that are still alive or have just crashed so they do not compete with the new instance for the single-instance lock or Web port.</summary>
    private static async Task KillStaleEnginesAsync()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("HeartRateMonitor"))
            {
                using (p)
                {
                    try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
                    catch { /* Process already exited or access denied */ }
                }
            }
            // Wait for processes to exit completely; Kill is asynchronous, and releasing locks/ports takes time
            for (var i = 0; i < 20; i++)
            {
                if (Process.GetProcessesByName("HeartRateMonitor").Length == 0) break;
                await Task.Delay(150);
            }
        }
        catch { /* Process-enumeration failure does not block restart */ }
    }

    /// <summary>Polls the engine Web port until it responds; secondary verification treats process exit or timeout as failure.</summary>
    private static async Task<bool> WaitWebUpAsync(Process? proc, int port, string scheme, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var hc = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(900) };
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (proc != null && proc.HasExited) return false;
            try
            {
                using var resp = await hc.GetAsync($"{scheme}://127.0.0.1:{port}/api/config");
                if (resp.IsSuccessStatusCode) return true;
            }
            catch { /* Not ready yet; keep waiting */ }
            await Task.Delay(700);
        }
        return false;
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try { raw = e.WebMessageAsJson; }
        catch { return; }

        string cmd;
        JsonNode? node = null;
        try
        {
            node = JsonNode.Parse(raw);
            cmd = node?["cmd"]?.GetValue<string>() ?? "";
        }
        catch { return; }
        if (cmd.Length == 0) return;

        switch (cmd)
        {
            case "win.drag":
                StartDrag();
                break;
            case "win.min":
                WindowState = FormWindowState.Minimized;
                break;
            case "win.max":
                if (_fullScreen) ToggleFullScreen();
                else WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                break;
            case "win.close":
                // Follow the same flow as system close: the page displays a themed confirmation dialog, then responds with win.exit / win.tray
                Close();
                break;
            case "win.close-ready":
            case "win.close-cancel":
                // The page displayed or dismissed the confirmation dialog. It is responsive, so the
                // unresponsive-page fallback must not make the user's decision on a timer.
                CancelCloseFallback();
                break;
            case "win.exit":
                CancelCloseFallback();
                _ = CompleteShutdownAsync();
                break;
            case "win.tray":
                CancelCloseFallback();
                HideToTray();
                break;
            case "engine.restart":
                // After an engine crash, the page selects "Restart backend": relaunch HeartRateMonitor.exe and perform secondary verification (see RelaunchEngineAsync)
                _ = RelaunchEngineAsync(afterCrash: true);
                break;
            case "engine.normal":
                // Leaving safe mode is a normal restart: do not carry --safemode or the crash-recovery marker.
                _ = RelaunchEngineAsync();
                break;
            case "engine.safemode":
                // Safe-mode restart: launch with --safemode, use read-only configuration, and perform no automatic actions (recovery from accidental operations)
                _ = RelaunchEngineAsync(safeMode: true);
                break;
            case "win.fullscreen":
                ToggleFullScreen();
                break;
            case "win.corner":
                // Change corner radius (px) in page settings: apply immediately and persist
                if (node?["px"] is JsonValue pv && pv.TryGetValue<int>(out var px)) SetCorner(px);
                break;
            case "win.theme":
                // The page sends the resolved theme color: update both the form background (the Padding ring) and DWM border color
                ApplyTheme(
                    node?["bg"]?.GetValue<string>(),
                    node?["dark"] is JsonValue dv && dv.TryGetValue<bool>(out var dk) ? dk : null);
                break;
            case "win.debug":
                if (node?["on"] is JsonValue bv && bv.TryGetValue<bool>(out var on))
                {
                    _debug = on;
                    ApplyDebugChrome();
                }
                break;
            case "win.state":
                PushWindowState();
                break;
        }
    }

    /// <summary>Title-bar dragging: delegates to the system window-move loop for native title-bar behavior.</summary>
    private void StartDrag()
    {
        if (_fullScreen) return;
        // Dragging while maximized should restore the window first, matching the system title bar
        if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    /// <summary>Minimizes to tray: hides the window while keeping the process resident; a later tray "Open UI" event restores it.</summary>
    private void HideToTray()
    {
        SaveState();
        if (_fullScreen) ToggleFullScreen();
        Hide();
        ShowInTaskbar = false;
    }

    /// <summary>Restores from the tray: returns the taskbar entry and brings the window to the foreground.</summary>
    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    /// <summary>
    /// Close confirmation: asks the page to show a themed dialog, including unsaved-change checks, then the page responds with win.exit / win.tray.
    /// Do not intercept when the page is unavailable (load failure/not ready). If the page is ready but does not respond because an offline/crash overlay blocks the dialog,
    /// the fallback timer forces exit after <see cref="CloseFallbackMs"/>—the fix for windows that could not close while the backend was unreachable.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closeConfirmed && _pageReady && e.CloseReason == CloseReason.UserClosing && !_waitingClose)
        {
            e.Cancel = true;
            // First ensure the window is operable; if partially off-screen or out of bounds, move its center to the current display's working-area center, then show the confirmation dialog
            EnsureCloseConfirmVisible();
            ArmCloseFallback();
            PostToPage(new { type = "win.close-request" });
            return;
        }
        SaveState();
        base.OnFormClosing(e);
    }

    /// <summary>Before showing the close dialog, centers the window within its display's working area if it is partially off-screen or out of bounds.
    /// Otherwise, the page-centered dialog can also become unreachable (round 32 supplemental #31). Fully visible windows retain their position.</summary>
    private void EnsureCloseConfirmVisible()
    {
        try
        {
            if (_fullScreen || WindowState != FormWindowState.Normal) return;
            var wa = Screen.FromHandle(Handle).WorkingArea;
            var r = new Rectangle(Location, Size);
            if (r.Left >= wa.Left && r.Top >= wa.Top && r.Right <= wa.Right && r.Bottom <= wa.Bottom) return;
            var tx = wa.Left + wa.Width / 2 - Width / 2;
            var ty = wa.Top + wa.Height / 2 - Height / 2;
            Location = new Point(tx, ty);
        }
        catch { /* Preserve the current position if display enumeration fails */ }
    }

    /// <summary>Starts waiting for a page response: if no win.exit/win.tray arrives within N seconds, force-close as exit (frontend only; FormClosed determines backend reachability).</summary>
    private void ArmCloseFallback()
    {
        _waitingClose = true;
        _closeCts?.Cancel();
        _closeCts?.Dispose();
        _closeCts = new CancellationTokenSource();
        var ct = _closeCts.Token;
        _ = Task.Delay(CloseFallbackMs, ct).ContinueWith(_ =>
        {
            try
            {
                if (ct.IsCancellationRequested || IsDisposed || !IsHandleCreated) return;
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    _waitingClose = false;
                    _ = CompleteShutdownAsync(requestShutdown: true);
                });
            }
            catch { /* Window already destroyed */ }
        }, TaskScheduler.Default);
    }

    /// <summary>Cancels the fallback timer after the page responds with win.tray / win.exit or once the window starts closing.</summary>
    private void CancelCloseFallback()
    {
        _waitingClose = false;
        _closeCts?.Cancel();
    }

    private async Task CompleteShutdownAsync(bool requestShutdown = false)
    {
        if (_engineStopping) return;
        _engineStopping = true;
        CancelCloseFallback();
        var deadline = Stopwatch.StartNew();
        if (requestShutdown) await RequestShutdownAsync();

        while (deadline.ElapsedMilliseconds < ShutdownTimeoutMs && EngineIdentityMatches(_enginePid))
            await Task.Delay(150);

        if (EngineIdentityMatches(_enginePid))
        {
            try
            {
                using var engine = Process.GetProcessById(_enginePid);
                if (EngineIdentityMatches(engine.Id))
                {
                    engine.Kill(entireProcessTree: true);
                    engine.WaitForExit(Math.Max(0, ShutdownTimeoutMs - (int)deadline.ElapsedMilliseconds));
                }
            }
            catch { /* Engine already exited or access was denied */ }
        }

        if (IsDisposed) return;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                _closeConfirmed = true;
                Close();
            });
        }
        catch { /* Window already destroyed */ }
    }

    private async Task RequestShutdownAsync()
    {
        try
        {
            using var hc = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(1500) };
            using var content = new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var response = await hc.PostAsync($"{new Uri(_startUrl).Scheme}://127.0.0.1:{_port}/api/shutdown", content);
            response.EnsureSuccessStatusCode();
        }
        catch { /* The PID/path deadline remains authoritative when HTTP is unavailable. */ }
    }

    /// <summary>Pushes window state to the page so title-bar button icons and the corner-radius input can display accordingly.</summary>
    private void PushWindowState() => PostToPage(new
    {
        type = "win.state",
        maximized = WindowState == FormWindowState.Maximized,
        fullScreen = _fullScreen,
        corner = _state.CornerPx,
    });

    private void PostToPage(object payload)
    {
        if (_web.CoreWebView2 is null) return;
        try { _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload)); }
        catch { /* Page already unloaded */ }
    }

    // ---- Theme synchronization ----

    private static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
        return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    /// <summary>Reads icons.&lt;role&gt; from the release manifest and returns the resolved full path; returns null when undeclared or invalid.
    /// #34: the release manifest owns icon selection (relative paths are resolved against the program directory, and GetFullPath also normalizes ./).
    /// P0: the manifest is embedded into the assembly (hrm-webui.Release.json); Release.json beside the executable remains as a dev/pinned override.
    /// Lenient parsing (#23): allows leading // comment lines, such as the MIT notice, and trailing commas.
    /// internal (#26): SplashForm reuses the same resolution chain for its logo.</summary>
    internal static string? ManifestIconPath(string role)
    {
        var rel = ManifestString("icons", role);
        if (string.IsNullOrWhiteSpace(rel)) return null;
        try { return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel)); }
        catch { return null; }
    }

    /// <summary>Reads a nested two-level string from the manifest: embedded resource first, then Release.json beside the executable.</summary>
    private static string? ManifestString(string section, string key)
    {
        foreach (var root in ManifestRoots())
        {
            if (root == null) continue;
            try
            {
                if (root.Value.TryGetProperty(section, out var sec)
                    && sec.ValueKind == JsonValueKind.Object
                    && sec.TryGetProperty(key, out var v)
                    && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>Manifest sources in priority order: embedded resource, then the file beside the executable.</summary>
    private static List<JsonElement?> ManifestRoots()
    {
        var roots = new List<JsonElement?>();
        try
        {
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream("hrm-webui.Release.json");
            if (s != null)
            {
                using var r = new StreamReader(s, leaveOpen: true);
                using var doc = JsonDocument.Parse(r.ReadToEnd(), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                roots.Add(doc.RootElement.Clone()); // Detach from the document lifetime.
            }
        }
        catch { }
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "Release.json");
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                roots.Add(doc.RootElement.Clone());
            }
        }
        catch { }
        return roots;
    }

    private static string LegacyIconPath(string name) => Path.Combine(AppContext.BaseDirectory, "image", name);

    /// <summary>
    /// Synchronizes the page theme: form background plus DWM border/title colors.
    /// Windows still draws a 1px DWM border around borderless windows, using the system color while active and therefore white under a light theme,
    /// which causes the white outline when selected. Win11 22000+ can force it to the theme color using DWMWA_BORDER_COLOR;
    /// Win10 does not support this attribute (returns E_INVALIDARG), so disable DWM non-client rendering for this window instead.
    /// </summary>
    private void ApplyTheme(string? bgHex, bool? dark)
    {
        var bg = ParseHex(bgHex);
        if (bg is { } c)
        {
            BackColor = c;
            _state.ThemeBg = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            // Use the same background for unpainted WebView areas during loading to avoid any white source
            try { _web.DefaultBackgroundColor = c; } catch { /* ignore */ }
        }
        if (!IsHandleCreated) return;

        if (dark is { } d)
        {
            int on = d ? 1 : 0;
            try { DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)); } catch { /* Unsupported before Win10 1809 */ }
        }
        if (bg is { } b)
        {
            // DWM uses COLORREF (0x00BBGGRR)
            int rgb = b.R | (b.G << 8) | (b.B << 16);
            int hr = -1;
            try { hr = DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref rgb, sizeof(int)); } catch { /* Unsupported before Win11 */ }
            try { DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref rgb, sizeof(int)); } catch { /* Same as above */ }
            if (hr != 0) DisableDwmBorder();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
        // Background, border, and dark/light mode take effect immediately: repaint edges where corner gaps meet the DWM border
        RefreshFrameNow();
    }

    /// <summary>
    /// Win10 fallback: when the border-color attribute is unavailable, disable DWM non-client rendering for this window.
    /// This removes the 1px border that turns white with the system color, at the cost of also losing the system shadow, which a rounded window does not need.
    /// </summary>
    private void DisableDwmBorder()
    {
        if (_ncRenderingOff) return;
        _ncRenderingOff = true;
        int policy = DWMNCRP_DISABLED;
        try { DwmSetWindowAttribute(Handle, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int)); } catch { /* Ignore on older systems */ }
    }

    /// <summary>Debug switch: DevTools follows Debug; the bottom-left status bar with link/path hints is always disabled (round 31 #1).</summary>
    private void ApplyDebugChrome()
    {
        if (_web.CoreWebView2 is null) return;
        try
        {
            _web.CoreWebView2.Settings.AreDevToolsEnabled = _debug;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
        }
        catch { /* Setting unavailable in older SDK versions */ }
    }

    /// <summary>F11 fullscreen: fills the display, including the taskbar area, and restores window state and rounded corners on exit.</summary>
    private void ToggleFullScreen()
    {
        var screen = Screen.FromHandle(Handle);
        if (!_fullScreen)
        {
            _preFullScreenState = WindowState;
            _fullScreen = true;
            SuspendLayout();
            WindowState = FormWindowState.Normal;
            Bounds = screen.Bounds;   // Borderless: directly fill the entire display
            ApplyPadding();
            ResumeLayout();
        }
        else
        {
            _fullScreen = false;
            SuspendLayout();
            ApplyPadding();
            WindowState = _preFullScreenState;
            if (_preFullScreenState == FormWindowState.Normal) ClientSize = InitialClientSize();
            ResumeLayout();
        }
        ApplyCorner();
        PushWindowState();
    }

    // ---- Rounded corners (custom radius in px) ----

    /// <summary>Win11 22000+ supports native DWM rounded corners with compositor clipping, antialiasing, and no ghosting.</summary>
    private static bool DwmCornerAvailable => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    /// <summary>
    /// Applies rounded corners through two implementation paths, replacing the old SetWindowRgn-only path that caused white borders and self-healing ghosting after moving displays:
    ///   · Win11 22000+: DWMWA_WINDOW_CORNER_PREFERENCE provides native DWM corners without a window region;
    ///     the radius maps to two system sizes (≤6 → small corners, &gt;6 → large corners).
    ///   · Win10: still uses a window region while eliminating every white-border source:
    ///     ① square WebView child window paints into corner gaps → increase inset based on radius (ApplyPadding);
    ///     ② white WebView cold-start background → preload theme color through DefaultBackgroundColor;
    ///     ③ redirected surface retains pixels outside the region → force a full-window redraw, including child windows and borders, via RedrawWindow.
    /// </summary>
    private void ApplyCorner()
    {
        if (!IsHandleCreated) return;
        var r = _state.CornerPx;
        var rounded = r > 0 && !_fullScreen && WindowState == FormWindowState.Normal;

        if (DwmCornerAvailable)
        {
            SetWindowRgn(Handle, IntPtr.Zero, false);
            int pref = !rounded ? DWMWCP_DEFAULT : r <= 6 ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
            try { DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
            catch { /* Attribute unavailable before 22000; this branch cannot be reached there */ }
        }
        else if (!rounded)
        {
            SetWindowRgn(Handle, IntPtr.Zero, true);
        }
        else
        {
            var d = Px(r) * 2;
            var rgn = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, d, d);
            // After SetWindowRgn succeeds, the system owns the region; do not call DeleteObject
            if (SetWindowRgn(Handle, rgn, true) == 0) DeleteObject(rgn);
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
                RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN);
        }
        ApplyPadding();
    }

    /// <summary>WebView inset (DIP): 0 in fullscreen; for Win10 rounded corners, at least 0.35ρ (the arc penetrates about 0.29ρ diagonally, plus margin) so the square child window cannot paint into corner gaps; otherwise the resize hit-zone width.</summary>
    private int InsetDip()
    {
        if (_fullScreen) return 0;
        var r = _state.CornerPx;
        if (WindowState == FormWindowState.Normal && r > 0 && !DwmCornerAvailable)
            return Math.Max(BorderDip, (int)Math.Ceiling(r * 0.35));
        return BorderDip;
    }

    /// <summary>Applies the current inset to form Padding; idempotent and shared by corner, DPI, and fullscreen changes.</summary>
    private void ApplyPadding() => Padding = new Padding(Px(InsetDip()));

    /// <summary>Sets the corner radius (0 = square, maximum 24px), persists it, and applies it immediately.</summary>
    private void SetCorner(int px)
    {
        _state.CornerPx = Math.Clamp(px, 0, 24);
        ApplyCorner();
        _state.Save();
        PushWindowState();
        // Apply the corner change immediately: force a full-window redraw, including borders and child windows, and notify DWM of the frame change.
        // Otherwise edges at corner gaps/background boundaries retain the previous composed frame until another change repairs them.
        RefreshFrameNow();
    }

    /// <summary>Refreshes edge rendering immediately: invalidates and redraws the entire window, including borders and all child windows, in the current frame,
    /// then uses SWP_FRAMECHANGED so the system/DWM reevaluates the borderless non-client area and corner composition.
    /// Call after property-only changes such as corner radius or theme background that do not necessarily trigger repainting, preventing stale pixels.</summary>
    private void RefreshFrameNow()
    {
        if (!IsHandleCreated) return;
        try
        {
            if (_web.IsHandleCreated) _web.Invalidate(true);
            Invalidate(true);
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
                RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            Update();
        }
        catch { /* Ignore if the window is destroyed or its handle is invalid */ }
    }

    // ---- Window messages: resize hit zones + aspect-ratio constraints ----

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const int WM_SIZING = 0x0214;
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCACTIVATE = 0x0086;
    private const int WM_ACTIVATE = 0x0006;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4, WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6;
    private const int HTCLIENT = 1, HTCAPTION = 2;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    // DWM window attributes: non-client rendering policy (Vista+), dark mode (Win10 1809+), border/title colors (Win11 22000+),
    // and native corner preference (Win11 22000+: 0 = default square / 1 = small corners about 4px / 2 = large corners about 8px)
    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0, DWMWCP_ROUNDSMALL = 1, DWMWCP_ROUND = 2;
    private const int DWMNCRP_DISABLED = 1;

    // RedrawWindow flags: invalidate + erase + include border + include all child windows (force redraw after changing the region to clear redirected-surface ghosting)
    private const uint RDW_INVALIDATE = 0x0001, RDW_ERASE = 0x0004, RDW_ALLCHILDREN = 0x0080, RDW_FRAME = 0x0400;
    // Synchronize to the current frame (RDW_UPDATENOW): execute immediately in RefreshFrameNow instead of waiting for the next message loop
    private const uint RDW_UPDATENOW = 0x0100;

    // SetWindowPos flags (SWP_FRAMECHANGED tells the system/DWM to reevaluate the window frame and corner composition)
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004,
        SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    protected override void WndProc(ref Message m)
    {
        // hrmdump broadcast: the engine exited abnormally (exit code != 0); push the exit code to the page
        if (_crashMsg != 0 && m.Msg == _crashMsg)
        {
            PostToPage(new { type = "engine.crash", code = m.WParam.ToInt32() });
            m.Result = IntPtr.Zero;
            return;
        }

        // Engine broadcast: normal exit from tray "Exit" or engine-initiated shutdown. The shell only closes its own frontend window,
        // without waiting for page/backend responses, preventing an unclosable window when the backend is unreachable (round 32 #2).
        if (_exitMsg != 0 && m.Msg == _exitMsg)
        {
            CancelCloseFallback();
            m.Result = IntPtr.Zero;
            try { BeginInvoke(() => _ = CompleteShutdownAsync()); } catch { /* Window already destroyed */ }
            return;
        }

        // WS_THICKFRAME restores a non-client border; return 0 so the client area fills the whole window rectangle and remains visually borderless
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        // Borderless windows use hit testing for eight-direction resizing; the hit zone is the ring reserved by Padding
        if (m.Msg == WM_NCHITTEST && !_fullScreen && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if (m.Result.ToInt32() == HTCLIENT)
            {
                var p = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, m.LParam.ToInt32() >> 16));
                var b = Px(BorderDip);
                bool left = p.X <= b, right = p.X >= ClientSize.Width - b;
                bool top = p.Y <= b, bottom = p.Y >= ClientSize.Height - b;
                int hit = (left, right, top, bottom) switch
                {
                    (true, _, true, _) => HTTOPLEFT,
                    (_, true, true, _) => HTTOPRIGHT,
                    (true, _, _, true) => HTBOTTOMLEFT,
                    (_, true, _, true) => HTBOTTOMRIGHT,
                    (true, _, _, _) => HTLEFT,
                    (_, true, _, _) => HTRIGHT,
                    (_, _, true, _) => HTTOP,
                    (_, _, _, true) => HTBOTTOM,
                    _ => HTCLIENT,
                };
                if (hit != HTCLIENT) m.Result = (IntPtr)hit;
            }
            return;
        }

        if (m.Msg == WM_SIZING && !_fullScreen)
        {
            var r = Marshal.PtrToStructure<RECT>(m.LParam);
            int edge = m.WParam.ToInt32();
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;

            if (edge is WMSZ_LEFT or WMSZ_RIGHT)
            {
                // Left/right edges: keep height fixed and clamp width to the aspect-ratio range
                int clamped = Math.Clamp(w, (int)Math.Round(h * MinAspect), (int)Math.Round(h * MaxAspect));
                if (clamped != w)
                {
                    if (edge == WMSZ_LEFT) r.Left = r.Right - clamped;
                    else r.Right = r.Left + clamped;
                }
            }
            else
            {
                // Top/bottom edges and corners: keep width fixed and clamp height to the aspect-ratio range
                int clamped = Math.Clamp(h, (int)Math.Round(w / MaxAspect), (int)Math.Round(w / MinAspect));
                if (clamped != h)
                {
                    if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT) r.Top = r.Bottom - clamped;
                    else r.Bottom = r.Top + clamped;
                }
            }

            Marshal.StructureToPtr(r, m.LParam, false);
            m.Result = (IntPtr)1;
            return;
        }

        // DWM redraws the non-client area when the window gains or loses activation, which can flash white where borderless borders meet rounded corners.
        // Refresh edge rendering twice after focus changes—immediately and again after 60ms—to clear stale frames after DWM finishes composition.
        if (m.Msg is WM_NCACTIVATE or WM_ACTIVATE)
        {
            base.WndProc(ref m);
            ScheduleFrameRefresh();
            return;
        }
        base.WndProc(ref m);
    }

    private bool _frameRefreshScheduled;

    /// <summary>Performs a delayed double refresh of edge rendering after focus changes: once immediately and again after 60ms to cover DWM composition timing.</summary>
    private void ScheduleFrameRefresh()
    {
        if (_frameRefreshScheduled) return;
        _frameRefreshScheduled = true;
        BeginInvoke(() =>
        {
            _frameRefreshScheduled = false;
            RefreshFrameNow();
        });
        _ = Task.Delay(60).ContinueWith(_ =>
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(() =>
                {
                    if (!IsDisposed) RefreshFrameNow();
                });
            }
            catch { /* Window already destroyed */ }
        });
    }

    private void SaveState()
    {
        // In fullscreen or maximized state, record restore size and position rather than fullscreen dimensions; store size in DIP to preserve logical dimensions across display scaling
        var bounds = WindowState == FormWindowState.Normal && !_fullScreen
            ? Bounds
            : RestoreBounds;
        var size = new Size(
            Math.Max(1, bounds.Width - Frame.Width),
            Math.Max(1, bounds.Height - Frame.Height));
        _state.Width = (int)Math.Round(size.Width / Scale);
        _state.Height = (int)Math.Round(size.Height / Scale);
        _state.Left = bounds.Left;
        _state.Top = bounds.Top;
        _state.Maximized = WindowState == FormWindowState.Maximized && !_fullScreen;
        try { _state.Zoom = _web.CoreWebView2 is null ? _state.Zoom : _web.ZoomFactor; } catch { /* Already disposed */ }
        _state.Save();
    }

    /// <summary>Shell window state stored in webui-shell.json beside the executable; standalone releases use the same directory.</summary>
    private sealed class ShellState
    {
        private static string Path0 => Path.Combine(AppContext.BaseDirectory, "webui-shell.json");

        public int Width { get; set; }
        public int Height { get; set; }
        public int? Left { get; set; }
        public int? Top { get; set; }
        public bool Maximized { get; set; }
        public double Zoom { get; set; } = 1.0;
        /// <summary>Window corner radius in px/DIP (0 = square); affects only the outer frame, not control corners within the page.</summary>
        public int CornerPx { get; set; } = 10;
        /// <summary>Theme background color (#RRGGBB) last synchronized by the page, used directly during cold start to avoid a color flash.</summary>
        public string ThemeBg { get; set; } = "";

        public static ShellState Load()
        {
            try
            {
                if (File.Exists(Path0))
                    return JsonSerializer.Deserialize<ShellState>(File.ReadAllText(Path0)) ?? new ShellState();
            }
            catch { /* Use defaults if corrupted */ }
            return new ShellState();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(Path0, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* Ignore in read-only directories */ }
        }
    }
}
