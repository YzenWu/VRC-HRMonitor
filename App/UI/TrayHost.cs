using System.Runtime.InteropServices;
using HeartRateMonitor.Core;
using HeartRateMonitor.Toolkit;
using HeartRateMonitor.Web;

namespace HeartRateMonitor.UI;

/// <summary>
/// Tray host: Keep the engine process alive. The right-key menu follows the front theme (colored/rounded),
/// It also provides common shortcut controls (opening interfaces/scans/records/suspension windows/OSC/Web/exit).
/// Also listen to WM_SETTINGCHANGE and broadcast Windows theme changes to the frontend (sys_theme event).
/// </summary>
public sealed class TrayHost : Form
{
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly AppHub _hub;
    private readonly WebHost _web;

    // Opening to update text when the status changes with the switch
    private readonly ToolStripMenuItem _scanItem;
    private readonly ToolStripMenuItem _recordItem;
    private readonly ToolStripMenuItem _lockItem;
    private readonly ToolStripMenuItem _oscItem;
    private readonly ToolStripMenuItem _webItem;
    private readonly ToolStripMenuItem _bpmItem;

    public TrayHost(AppHub hub, WebHost web)
    {
        _hub = hub;
        _web = web;

        // Registered UI linear scheduler (back-stage thread to create WinForms control to UI)
        AppHub.UiInvoke = a =>
        {
            if (IsDisposed || !IsHandleCreated) throw new InvalidOperationException("tray UI is unavailable");
            BeginInvoke(a);
        };

        Text = "OSC Pusher V1";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;
        WindowState = FormWindowState.Minimized;
        Load += (_, _) => Hide();

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "OSC Pusher V1",
            Visible = true,
        };

        _menu = new ContextMenuStrip
        {
            // Follow front theme (color + round angle)
            Renderer = new ThemedMenuRenderer(),
            ShowImageMargin = false,
            BackColor = ThemeColors.Background(),
            ForeColor = ThemeColors.Foreground(),
        };

        // Current heart rate (read-only line)
        _bpmItem = new ToolStripMenuItem("--") { Enabled = false };
        _menu.Items.Add(_bpmItem);
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add("打开界面", null, (_, _) => ProcessInfo.LaunchFrontend());
        _menu.Items.Add("打开 Web 界面", null, (_, _) =>
        {
            if (_web.Start(openUi: false)) ProcessInfo.LaunchWebUi(App.WebPort);
        });
        _menu.Items.Add(new ToolStripSeparator());

        // Common Switches: Text changes with status (updated when Opening)
        _scanItem = Add("开始扫描", () => _hub.Scan(App.Ble.Scanning ? "stop" : "start"));
        _recordItem = Add("开始记录", () => _hub.Record(HrmDb.Recording ? "stop" : "start"));
        _oscItem = Add("连接 OSC", () => _hub.OscConnect(!App.Osc.Connected));
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add("打开悬浮窗", null, (_, _) => FloatWindowHost.Open("__main__"));
        _lockItem = Add("锁定悬浮窗", () =>
        {
            var next = !App.Config.HeartRate.Window.Locked;
            App.Config.HeartRate.Window.Locked = next;
            FloatWindowHost.ToggleLockAll(next);
            return null;
        });
        _menu.Items.Add("关闭全部悬浮窗", null, (_, _) => FloatWindowHost.CloseAll());
        _menu.Items.Add(new ToolStripSeparator());

        _webItem = Add("启动 Web 服务", ToggleWeb);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => Close());

        _menu.Opening += (_, _) => RefreshMenu();
        _tray.ContextMenuStrip = _menu;
        _tray.DoubleClick += (_, _) => ProcessInfo.LaunchFrontend();
    }

    /// <summary>Adds a tray menu item that executes an action; its text can be updated to reflect status.</summary>
    private ToolStripMenuItem Add(string text, Func<object?> action)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) =>
        {
            try { action(); }
            catch (Exception e) { App.Log.Error(LogText.L("log.tray.action_fail", e.Message)); }
        });
        _menu.Items.Add(item);
        return item;
    }

    /// Refreshs the status text and theme colour (the user may have just changed in the settings) before the <summary> menu opens. </summary>
    private void RefreshMenu()
    {
        _menu.BackColor = ThemeColors.Background();
        _menu.ForeColor = ThemeColors.Foreground();

        var bpm = App.CurrentBpm;
        _bpmItem.Text = bpm > 0 ? $"心率 {bpm} BPM" : "心率 --";
        _scanItem.Text = App.Ble.Scanning ? "停止扫描" : "开始扫描";
        _recordItem.Text = HrmDb.Recording ? "停止记录" : "开始记录";
        _oscItem.Text = App.Osc.Connected ? "断开 OSC" : "连接 OSC";
        _lockItem.Text = App.Config.HeartRate.Window.Locked ? "解锁悬浮窗" : "锁定悬浮窗";
        _webItem.Text = _web.Running ? "停止 Web 服务" : "启动 Web 服务";
    }

    private object? ToggleWeb()
    {
        if (_web.Running)
        {
            _web.Stop();
            App.Log.Info(LogText.L("log.tray.web_stopped"));
        }
        else
        {
            _web.Start();
            App.Log.Info(LogText.L("log.tray.web_started"));
        }
        return null;
    }

    /// <summary> Tray Icon (#18/#34): Give priority to the icons.tray specified path for the output directory Release.json
    /// (relative to the exe directory, such as "./image/heart.ico", unconfigured or missing files back to the image\XQ4QXZ →XQ5QXZ\XQ6QXZ icon.
    /// Builds an icon that deploys the statement to the output path (image/ whole directory copy + bottom of the list path). </summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        // Absolute/relative path is available: relative path parsed by exe directory (GetFullPath sequential cleanup./)
        foreach (var f in new[] { ManifestIconPath("tray"), LegacyIconPath("tray.ico"), LegacyIconPath("app.ico") })
        {
            try
            {
                if (f != null && File.Exists(f)) return new System.Drawing.Icon(f);
            }
            catch { /*Icon file damage: continuing downgrade*/ }
        }
        return System.Drawing.SystemIcons.Application;
    }

    /// <summary>Reads icons.&lt;role&gt; from the release manifest (P0: embedded resource first, exe-dir file as override) and returns the resolved full path; null when undeclared or invalid.</summary>
    private static string? ManifestIconPath(string role)
    {
        var rel = Core.ReleaseManifest.Icon(role);
        if (string.IsNullOrWhiteSpace(rel)) return null;
        try { return Path.GetFullPath(Path.Combine(App.ExeDir, rel)); }
        catch { return null; }
    }

    private static string LegacyIconPath(string name) => Path.Combine(App.ExeDir, "image", name);

    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

    /// <summary>
    /// Windows Subject Change: Toggle Deep Light WM_SETTINGCHANGE ("ImmersiveColorSet")
    /// WM_DWMCOLORIZATIONCOLORCHANGED. The sys_theme event has been pushed to the front.
    /// mode/palette to keep up in real time.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DWMCOLORIZATIONCOLORCHANGED
            || (m.Msg == WM_SETTINGCHANGE && Marshal.PtrToStringAuto(m.LParam) == "ImmersiveColorSet"))
        {
            _hub.PushSystemTheme();
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { ProcessInfo.StopCrashWatch(); } catch { }
        try { VrcToolkitService.CancelCacheScan(); } catch { }
        try { FloatWindowHost.CloseAll(); } catch { }
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
        try { _hub.Stop(); } catch { }
        try { ProcessInfo.ShutdownFrontend(_web.Running); } catch { }
        try { _web.Stop(); } catch { }
        try { App.Osc.Stop(); } catch { }
        try { App.Ble.DisconnectAll(); } catch { }
        base.OnFormClosed(e);
    }
}
