using System.Globalization;
using HeartRateMonitor.Cli;
using HeartRateMonitor.Core;

namespace HeartRateMonitor;

internal static class Program
{
    private enum SecondAction { Cancel, Kill, Jump }

    /// <summary>P3: the update dialog shows at most once per process, and only when the check is enabled.</summary>
    static int _updatePrompted;

    [STAThread]
    static void Main(string[] args)
    {
        // P2: cheap component probe used by the version check; exits before any service starts.
        if (VersionJson.TryRun(args, "engine")) return;

        // P1: one authoritative option model; the GUI is the default and --cli is the only CLI selector.
        var opt = Core.StartupOptions.Parse(args);
        AppBoot.Init(args);

        // Start detailed log observation (round 31 #27): who launched us / whether this is a reboot relay.
        if (opt.AfterCrash) App.Log.Warn(LogText.L("log.program.after_crash"));
        if (opt.Reboot) App.Log.Info(LogText.L("log.program.reboot_note"));

        // Mode resolution (P1): --cli selects the CLI; the legacy parent-console heuristic was removed,
        // so launching from cmd/PowerShell now behaves like a double click (GUI).
        if (opt.Cli)
        {
            App.Log.Info(LogText.L("log.program.cli_mode"));
            ProcessInfo.EnsureConsole();
            try
            {
                CliApp.Run(opt);
            }
            finally
            {
                try { App.Ble.StopScan(); } catch { }
                try { App.Ble.DisconnectAll(); } catch { }
                try { App.Web?.Stop(); } catch { }
                try { App.Hub?.Stop(); } catch { }
                try { App.Osc.Stop(); } catch { }
                ProcessInfo.ReleaseConsole();
                App.Log.Info(LogText.L("log.program.cli_exit"));
            }
            return;
        }

        // ---- GUI mode ----
        App.GuiHosted = true;
        // Single instance: one GUI instance by default; a duplicate launch asks Cancel/Kill/Jump (round 32 #1).
        var allowMulti = App.Config.App.AllowMultiInstance || opt.Multi;
        // Initialize visual styles for TaskDialog before entering the single-instance guard.
        ApplicationConfiguration.Initialize();
        if (opt.SafeMode)
        {
            var confirmedByShell = args.Any(a => string.Equals(a?.Trim(), "--safemode-confirmed", StringComparison.OrdinalIgnoreCase));
            if (!TryAcquireSafeModeInstance(confirmedByShell))
            {
                App.Log.Info(LogText.L("log.program.single_instance"));
                return;
            }
        }
        else if (!allowMulti && !TryAcquireGuiInstance(opt.AutoStart))
        {
            App.Log.Info(LogText.L("log.program.single_instance"));
            return;
        }

        // P2: verify every shipped component against the embedded manifest before starting any service.
        // Install integrity only - never confused with the online update check (which compares GitHub releases).
        var components = Core.ComponentVersions.CheckAll();
        App.ComponentsOk = components.All(c => c.Ok);
        if (App.ComponentsOk == true)
        {
            App.Log.Info(LogText.L("log.component.ok"));
        }
        else
        {
            foreach (var c in components.Where(c => !c.Ok))
                App.Log.Error(LogText.L("log.component.mismatch", c.ExeName, c.Actual, c.Declared, c.Error ?? "-"));
            // Debug builds keep iterating; release builds ask the user how to proceed.
            if (!App.DebugMode && !AskComponentVersions(components))
            {
                App.Log.Info(LogText.L("log.program.exit_normal"));
                return;
            }
        }

        // Anomalous policy: debug → Bullet Error Trace + Write Repository; release → Continue as much as possible after recording
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => OnFatal(e.Exception, "UI");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => OnFatal(e.ExceptionObject as Exception, "AppDomain", exit: true);

        if (!App.SafeMode)
        {
            App.SysInfo.Start();
            App.Osc.Start();
            if (App.Config.Osc.AutoStart)
                App.Osc.SetConnected(true);
        }

        // Old WinForms main form fallback
        if (opt.WinForms)
        {
            Application.Run(new UI.MainForm());
            App.Log.Info(LogText.L("log.program.exit_normal"));
            return;
        }

        // Shared Business Core: Subscription Service Event + Achieve All Commands
        var hub = new Core.AppHub();
        App.Hub = hub;
        hub.Start();

        // Web service: Host of the only frontend (inline WebView shell), started as required by LaunchFrontend
        var port = App.Config.Web.Port is > 0 and < 65536 ? App.Config.Web.Port : 9460;
        App.WebPort = port;
        var webHost = new Web.WebHost(hub, port, App.Config.Web.OpenBrowser);
        App.Web = webHost;
        hub.WebStartHook = () => webHost.Start();
        hub.WebStopHook = () => webHost.Stop();
        hub.WebRunningHook = () => webHost.Running;
        hub.WebPortHook = () => port;
        hub.WebSchemeHook = () => App.WebScheme;

        var tray = new UI.TrayHost(hub, webHost);

        // Safe mode may read the cached project snapshot but must not perform automatic network access.
        Core.GitHubProjectService.Updated += OnGitHubUpdated;
        Core.GitHubProjectService.Start(allowNetwork: !App.SafeMode);

        // (a) E3 MVP found: recovery is initiated when the last session main suspended window is visible (the locking state is applied by configuration within Open);
        // The security mode does not automatically execute any actions (including open windows) consistent with the semantics.
        tray.Load += (_, _) =>
        {
            if (!App.SafeMode && App.Config.HeartRate.Window.Visible)
                UI.FloatWindowHost.Open(UI.FloatWindowHost.MainId);
        };

        // P1: --silent keeps the engine, tray, and services running without launching the frontend;
        // the web service still starts (without opening the UI) so tray/remote paths stay available.
        if (opt.Silent)
        {
            App.Log.Info(LogText.L("log.program.silent_start"));
            _ = App.Web?.Start(openUi: false);
        }
        else
        {
            ProcessInfo.LaunchFrontend();
        }
        ProcessInfo.LaunchCrashWatch();

        // Device automation: prefer reconnecting the last session's devices, else auto-detect (never both).
        // Safe mode performs no automatic action; the user drives everything from the UI.
        // The reboot command relays with --reboot and reconnects the last devices regardless of AutoConnect (#27).
        if (!App.SafeMode)
        {
            var wantReconnect = App.Config.Devices.AutoConnect || opt.Reboot;
            if (wantReconnect) App.Devices.AutoConnectLast();
            else if (App.Config.Devices.AutoDetect) App.Devices.StartAutoDetect();
        }

        Application.Run(tray);
        ProcessInfo.StopCrashWatch();
        Core.SingleInstance.Release();
        App.Log.Info(LogText.L("log.program.exit_normal"));
    }

    /// <summary>
    /// GUI single-instance guard (round 32 #1): a lock contention asks for Cancel/Kill/Jump.
    /// Cancel → this process exits; Jump → switch to the old instance and exit; Kill → terminate the old
    /// instance (engine + shell), retry the lock, and start fresh. An autostart launch (P1) never disturbs
    /// the running instance: no dialog, no foreground steal, silent exit.
    /// </summary>
    static bool TryAcquireGuiInstance(bool autoStart)
    {
        while (true)
        {
            if (Core.SingleInstance.TryAcquire()) return true;
            App.Log.Info(LogText.L("log.program.single_instance"));
            if (autoStart) return false;
            switch (AskInstanceAction())
            {
                case SecondAction.Cancel:
                    return false;
                case SecondAction.Jump:
                    if (!ProcessInfo.ActivateExistingUi()) Core.SingleInstance.NotifyRunning();
                    return false;
                case SecondAction.Kill:
                    // Terminate the old GUI instance (engine + its shell), then loop to retry the lock.
                    ProcessInfo.KillGuiInstance();
                    continue;
            }
        }
    }

    /// <summary>Safe mode always owns the normal single-instance lock. External launches require confirmation before either continuing or replacing an existing instance.</summary>
    static bool TryAcquireSafeModeInstance(bool confirmedByShell)
    {
        if (Core.SingleInstance.TryAcquire())
            return confirmedByShell || AskSafeModeStart(killExisting: false);

        if (!confirmedByShell && !AskSafeModeStart(killExisting: true)) return false;
        if (!confirmedByShell) ProcessInfo.KillGuiInstance();

        // Process termination and mutex release are asynchronous. Keep retrying the real lock instead of bypassing it with --multi.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (Core.SingleInstance.TryAcquire()) return true;
            Thread.Sleep(150);
        }
        return false;
    }

    /// <summary>Shows the mandatory English safe-mode confirmation. Closing the dialog is equivalent to Cancel.</summary>
    static bool AskSafeModeStart(bool killExisting)
    {
        var confirm = new TaskDialogButton("Confirm");
        var cancel = new TaskDialogButton("Cancel");
        var page = new TaskDialogPage
        {
            Caption = "HeartRateMonitor",
            Heading = killExisting
                ? "Are You Sure Kill All Instances and Start With Safe Mode?"
                : "Are You Sure Start With Safe Mode?",
            Icon = TaskDialogIcon.Warning,
            AllowCancel = true,
            DefaultButton = cancel,
            Buttons = { confirm, cancel },
        };
        return TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen) == confirm;
    }

    /// <summary>Asks how to handle a duplicate launch with a Windows TaskDialog (round 32 #1).</summary>
    static SecondAction AskInstanceAction()
    {
        var zh = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var jump = new TaskDialogButton(zh ? "转到已有实例" : "Jump to existing instance");
        var kill = new TaskDialogButton(zh ? "结束旧实例并重开" : "Kill old instance and reopen");
        var cancel = new TaskDialogButton(zh ? "取消" : "Cancel");
        var page = new TaskDialogPage
        {
            Caption = "HeartRateMonitor",
            Heading = zh ? "程序已在运行" : "Already running",
            Text = zh
                ? "检测到已有一个实例在运行。\n\n· 转到已有实例：显示现有界面，本进程退出；\n· 结束旧实例并重开：关闭旧实例（含前端壳与后端引擎）后重新启动；\n· 取消：什么都不做。"
                : "Another instance is already running.\n\n· Jump to existing: bring the running app up and exit this process.\n· Kill and reopen: terminate the old instance (frontend shell and engine) and start fresh.\n· Cancel: do nothing.",
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

    /// <summary>
    /// Component version mismatch dialog (P2): lists every component's declared/actual version and
    /// offers Cancel / Continue / Repository / Report. Repository and Report open links and re-show the
    /// dialog so the user still decides how to proceed; closing the dialog counts as Cancel.
    /// </summary>
    static bool AskComponentVersions(List<Core.ComponentVersions.Report> reports)
    {
        var zh = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var cancel = new TaskDialogButton(zh ? "取消" : "Cancel");
        var cont = new TaskDialogButton(zh ? "仍然继续" : "Continue anyway");
        var repo = new TaskDialogButton(zh ? "打开仓库" : "Repository");
        var report = new TaskDialogButton(zh ? "报告问题" : "Report an issue");
        var details = string.Join("\n", reports.Select(r => ComponentLine(r, zh)));
        while (true)
        {
            var page = new TaskDialogPage
            {
                Caption = "HeartRateMonitor",
                Heading = zh ? "组件版本不一致" : "Component version mismatch",
                Text = (zh
                        ? "检测到安装的组件版本不一致，继续运行可能出现异常。\n\n"
                        : "The installed component versions do not match; continuing may misbehave.\n\n")
                    + details
                    + (zh ? "\n\n建议重新下载完整安装包后再运行。" : "\n\nRedownloading the full package is recommended."),
                Icon = TaskDialogIcon.Warning,
                AllowCancel = true,
                DefaultButton = cont,
                Buttons = { cont, cancel, repo, report },
            };
            var clicked = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
            if (clicked == cont) return true;
            if (clicked == repo)
            {
                if (ReleaseManifest.Repo.Length > 0) ProcessInfo.OpenBrowser(ReleaseManifest.Repo);
                continue;
            }
            if (clicked == report)
            {
                try { Clipboard.SetText(ComponentDiagnostics(reports)); } catch { }
                if (ReleaseManifest.Repo.Length > 0)
                    ProcessInfo.OpenBrowser(ReleaseManifest.Repo.TrimEnd('/') + "/issues/new");
                continue;
            }
            return false; // Cancel or dialog closed.
        }
    }

    /// <summary>One dialog line per component: declared vs actual version with the failure reason.</summary>
    static string ComponentLine(Core.ComponentVersions.Report r, bool zh)
    {
        string state;
        if (r.Ok)
        {
            state = r.Actual;
        }
        else if (!r.Exists)
        {
            state = zh ? "文件缺失" : "file missing";
        }
        else
        {
            state = (zh ? $"实际 {r.Actual} ≠ 清单 {r.Declared}" : $"actual {r.Actual} != manifest {r.Declared}")
                + (r.Error != null ? $" / {r.Error}" : "");
        }
        return $"· {r.ExeName} ({r.Role}): {state}";
    }

    /// <summary>Plain-text diagnostics copied to the clipboard by the Report button.</summary>
    static string ComponentDiagnostics(List<Core.ComponentVersions.Report> reports) => string.Join("\n",
        new[]
        {
            "HeartRateMonitor component version report",
            $"os       : {Environment.OSVersion.VersionString}",
            $"engine   : {Environment.ProcessPath}",
            $"manifest : {ReleaseManifest.ReleaseName} v{ReleaseManifest.Version} build {ReleaseManifest.BuildTimeUtc} target {ReleaseManifest.BuildTarget}",
        }.Concat(reports.Select(r =>
            $"{r.Role,-7}: declared={r.Declared} actual={r.Actual} file={r.FileVersion} exists={r.Exists} error={r.Error ?? "-"}")));

    /// <summary>
    /// P3: fired by the background GitHub refresh; shows the update dialog once when the latest release
    /// differs from the manifest identity and was not skipped. Runs on the UI thread via UiInvoke.
    /// </summary>
    static void OnGitHubUpdated()
    {
        try
        {
            if (Volatile.Read(ref _updatePrompted) != 0 || !App.Config.App.UpdateCheck) return;
            var rel = Core.GitHubProjectService.Current?.LatestRelease;
            if (rel == null || rel.Tag.Length == 0) return;
            if (Core.GitHubProjectService.IsCurrentRelease(rel)) return;
            if (Core.GitHubProjectService.IsSkipped(rel)) return;
            AppHub.UiInvoke(() =>
            {
                if (Interlocked.CompareExchange(ref _updatePrompted, 1, 0) != 0) return;
                App.Log.Info(LogText.L("log.github.update_available", rel.Tag));
                AskUpdateAvailable(rel);
            });
        }
        catch { }
    }

    /// <summary>
    /// Startup update dialog (P3): open the release page, download the asset matched by
    /// asset_pattern + build_target, copy the link, skip exactly this release, or close.
    /// </summary>
    static void AskUpdateAvailable(Core.GitHubProjectService.Release rel)
    {
        var zh = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var pageBtn = new TaskDialogButton(zh ? "打开发布页" : "Release page");
        var dlBtn = new TaskDialogButton(zh ? "下载" : "Download");
        var copyBtn = new TaskDialogButton(zh ? "复制链接" : "Copy link");
        var skipBtn = new TaskDialogButton(zh ? "跳过此版本" : "Skip this version");
        var closeBtn = new TaskDialogButton(zh ? "关闭" : "Close");
        var published = rel.PublishedAt.Length > 0 ? rel.PublishedAt : "?";
        var page = new TaskDialogPage
        {
            Caption = "HeartRateMonitor",
            Heading = zh ? "发现新版本" : "Update available",
            Text = (zh
                    ? $"当前: {ReleaseManifest.ReleaseName}（{ReleaseManifest.Version}）\n最新: {rel.Name}（{rel.Tag}）  发布于 {published}\n\n"
                    : $"Current: {ReleaseManifest.ReleaseName} ({ReleaseManifest.Version})\nLatest: {rel.Name} ({rel.Tag})  published {published}\n\n")
                + (zh
                    ? "· 发布页/下载：在浏览器中打开对应页面（下载自动匹配本机构建平台的资产）。\n· 复制链接：把发布页地址复制到剪贴板。\n· 跳过此版本：仅忽略本次提示的版本，之后出现新版本仍会提醒。"
                    : "· Release page / Download: open the matching page in the browser (download picks the asset for this build target).\n· Copy link: copy the release URL to the clipboard.\n· Skip this version: ignore exactly this release; newer ones still prompt."),
            Icon = TaskDialogIcon.Information,
            AllowCancel = true,
            DefaultButton = pageBtn,
            Buttons = { pageBtn, dlBtn, copyBtn, skipBtn, closeBtn },
        };
        var clicked = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
        if (clicked == pageBtn)
        {
            if (rel.Url.Length > 0) ProcessInfo.OpenBrowser(rel.Url);
        }
        else if (clicked == dlBtn)
        {
            var asset = Core.GitHubProjectService.MatchAsset(rel);
            var url = asset != null && rel.AssetUrls.TryGetValue(asset, out var u) ? u : rel.Url;
            if (url.Length > 0) ProcessInfo.OpenBrowser(url);
        }
        else if (clicked == copyBtn)
        {
            try { Clipboard.SetText(rel.Url); } catch { }
        }
        else if (clicked == skipBtn)
        {
            App.Config.App.SkippedReleaseTag = rel.Tag;
            App.Config.App.SkippedReleaseName = rel.Name;
            App.Config.Save();
            App.Log.Info(LogText.L("log.github.skipped", rel.Tag));
        }
    }

    /// <summary>
    /// Restarts the backend after saving the current device snapshot.
    /// The new process receives `--reboot` so it can reconnect the devices from the previous session.
    /// </summary>
    public static void RequestReboot()
    {
        try { App.Devices.SnapshotSessionDevices(); } catch { }
        try { App.Config.Save(); } catch { }
        var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            App.Log.Error(LogText.L("log.shell.reboot_fail", exe ?? "?"));
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                WorkingDirectory = App.ExeDir,
                UseShellExecute = true,
                Arguments = "--reboot",
            });
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.shell.reboot_fail", e.Message));
        }
    }

    static void OnFatal(Exception? ex, string where, bool exit = false)
    {
        var msg = LogText.L("log.program.fatal", where, ex);
        try { App.Log.Error(msg); } catch { }

        if (App.DebugMode)
        {
            try
            {
                File.WriteAllText(Path.Combine(App.BaseDir, "crash_dump.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{msg}");
                MessageBox.Show($"Error Trace ({where})\n\n{ex}", "Error Trace", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
        else
        {
            try
            {
                File.AppendAllText(Path.Combine(App.BaseDir, "crash_dump.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{msg}\n");
            }
            catch { }
        }

        if (exit) Environment.Exit(1);
    }
}
