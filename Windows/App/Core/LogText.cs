using System.Collections.Generic;

namespace HeartRateMonitor.Core;

/// <summary>
/// Catalog of engine-operation log messages used by all App.Log calls.
/// Logging follows the configured language in two tiers: all Chinese and Cantonese locales use Chinese messages,
/// while en, ja, es, ko, de, and fr fall back to English to keep diagnostic logs consistent and easy to share.
/// Each row contains Chinese and English text; a missing key is returned unchanged to expose omissions.
/// </summary>
public static class LogText
{
    private static readonly Dictionary<string, string[]> Table = new(StringComparer.Ordinal)
    {
        // ---- BLE / device registry ----
        ["log.ble.scan_start"] = new[] { "开始扫描蓝牙设备...", "Start scanning for Bluetooth devices..." },
        ["log.ble.scan_stop"] = new[] { "停止扫描蓝牙设备", "Stop scanning for Bluetooth devices" },
        ["log.ble.resolve_name_fail"] = new[] { "解析设备名失败 ({0}): {1}", "Failed to resolve device name ({0}): {1}" },
        ["log.ble.connecting"] = new[] { "正在连接设备: {0} ({1})", "Connecting to device: {0} ({1})" },
        ["log.ble.gatt_limited"] = new[] { "GATT 访问受限 ({0})，尝试配对...", "GATT access limited ({0}), trying to pair..." },
        ["log.ble.no_hr_char"] = new[] { "设备无心率特征: {0} ({1})", "Device has no heart-rate characteristic: {0} ({1})" },
        ["log.ble.connected"] = new[] { "设备连接成功: {0} ({1})", "Device connected: {0} ({1})" },
        ["log.ble.connect_fail"] = new[] { "连接失败 ({0}): {1}", "Connection failed ({0}): {1}" },
        ["log.ble.hr_parse_fail"] = new[] { "解析心率数据失败: {0}", "Failed to parse heart-rate data: {0}" },
        ["log.ble.disconnected"] = new[] { "设备已断开: {0} ({1})", "Device disconnected: {0} ({1})" },
        ["log.ble.device_missing"] = new[] { "无法获取设备", "Cannot get device" },
        ["log.ble.gatt_status"] = new[] { "GATT 访问失败: {0}（请在 Windows 设置中配对后再试）", "GATT access failed: {0} (pair the device in Windows Settings and retry)" },
        ["log.ble.no_hr_char_evt"] = new[] { "{0}: 未找到心率特征 (0x2A37)", "{0}: heart-rate characteristic (0x2A37) not found" },
        ["log.ble.subscribe_fail"] = new[] { "订阅心率通知失败: {0}", "Failed to subscribe to heart-rate notifications: {0}" },
        ["log.dev.weak_signal"] = new[] { "设备信号弱: {0} 衰减度 {1} > {2}", "Weak device signal: {0}, attenuation {1} > {2}" },
        ["log.dev.alias_cleared"] = new[] { "已清除别名: {0}", "Alias cleared: {0}" },
        ["log.dev.renamed"] = new[] { "设备重命名: {0} → {1}", "Device renamed: {0} → {1}" },
        ["log.dev.reconnect_start"] = new[] { "自动重连已启动: {0}（每 {1}s 重试，{2} 分钟无响应放弃）", "Auto-reconnect started: {0} (retry every {1}s, give up after {2} min)" },
        ["log.dev.reconnect_ok"] = new[] { "自动重连成功: {0}", "Auto-reconnect succeeded: {0}" },
        ["log.dev.reconnect_giveup"] = new[] { "自动重连放弃: {0}（{1} 分钟内无响应）", "Auto-reconnect abandoned: {0} (no response within {1} minutes)" },
        ["log.dev.autodetect_start"] = new[] { "自动检测开始：{0} 台候选设备（单台最长 {1:0} 秒）", "Auto-detection started: {0} candidate device(s) (max {1:0} s each)" },
        ["log.dev.autodetect_timeout"] = new[] { "自动检测超时跳过: {0}", "Auto-detection timed out, skipping: {0}" },
        ["log.dev.autodetect_hit"] = new[] { "自动检测命中: {0}（已保持连接）", "Auto-detection hit: {0} (connection kept)" },
        ["log.dev.autodetect_none"] = new[] { "自动检测结束：没有找到可用的心率设备", "Auto-detection finished: no usable heart-rate device found" },
        ["log.dev.autodetect_done"] = new[] { "自动检测完成：已连接 {0} 台设备", "Auto-detection done: {0} device(s) connected" },
        ["log.dev.autodetect_cancel"] = new[] { "自动检测已取消", "Auto-detection cancelled" },
        ["log.dev.autodetect_error"] = new[] { "自动检测异常: {0}", "Auto-detection error: {0}" },
        ["log.dev.autoconnect_last"] = new[] { "自动连接上次设备: {0}", "Auto-connecting to last session device(s): {0}" },
        ["log.dev.autoconnect_fail"] = new[] { "自动连接失败: {0}（设备可能不在范围内）", "Auto-connect failed: {0} (device may be out of range)" },

        // ---- Database ----
        ["log.db.hr_write_fail"] = new[] { "[db] hr 写入失败: {0}", "[db] hr write failed: {0}" },
        ["log.db.osc_write_fail"] = new[] { "[db] osc 写入失败: {0}", "[db] osc write failed: {0}" },
        ["log.db.health_write_fail"] = new[] { "[db] health 写入失败: {0}", "[db] health write failed: {0}" },
        ["log.db.query_fail"] = new[] { "[db] 查询失败 ({0}): {1}", "[db] query failed ({0}): {1}" },
        ["log.db.variable_write_fail"] = new[] { "[db] variable 写入失败: {0}", "[db] variable write failed: {0}" },
        ["log.db.agg_query_fail"] = new[] { "[db] 聚合查询失败: {0}", "[db] aggregate query failed: {0}" },

        // ---- Health evaluation and calibration ----
        ["log.health.spike_warn"] = new[] { "心率波动较大: {0} → {1} BPM（阈值 {2}）", "Large heart-rate fluctuation: {0} → {1} BPM (threshold {2})" },
        ["log.health.calib_start"] = new[] { "健康校准开始：请站立静息 {0} 秒，随后 {1} 秒对照", "Health calibration started: stand still for {0} s, then {1} s of reference" },
        ["log.health.calib_cancel"] = new[] { "健康校准已取消", "Health calibration cancelled" },
        ["log.health.calib_insufficient"] = new[] { "健康校准样本不足（{0} 条），已放弃", "Insufficient health-calibration samples ({0}); aborted" },
        ["log.health.calib_done"] = new[] { "健康校准完成：静息 {0} BPM（SD {1}，样本 {2}+{3}）", "Health calibration complete: resting {0} BPM (SD {1}, samples {2}+{3})" },
        ["log.health.status_calibrated"] = new[] { "健康状态: {0}（{1} BPM，静息基准 {2}）", "Health status: {0} ({1} BPM, resting baseline {2})" },
        ["log.health.status_uncalibrated"] = new[] { "健康状态: {0}（{1} BPM，静息基准 未校准）", "Health status: {0} ({1} BPM, resting baseline not calibrated)" },

        // ---- OSC ----
        ["log.osc.dll_load_fail"] = new[] { "osc_engine.dll 加载失败，OSC 功能不可用", "osc_engine.dll failed to load; OSC unavailable" },
        ["log.osc.listen_start"] = new[] { "OSC 接收监听 127.0.0.1:{0}", "OSC receive listener on 127.0.0.1:{0}" },
        ["log.osc.listen_fail"] = new[] { "OSC 接收启动失败（端口 {0} 可能被占用）", "Failed to start OSC receiver (port {0} may be in use)" },
        ["log.osc.parse_fail"] = new[] { "OSC 解析失败: {0}", "OSC parse failed: {0}" },
        ["log.osc.conn_on"] = new[] { "OSC 已连接（开始推送）", "OSC connected (pushing started)" },
        ["log.osc.conn_off"] = new[] { "OSC 已断开（停止推送）", "OSC disconnected (pushing stopped)" },
        ["log.osc.push_resume"] = new[] { "OSC 推送器已自动恢复", "OSC pusher automatically resumed" },
        ["log.osc.push_fail"] = new[] { "OSC 推送失败: {0}", "OSC push failed: {0}" },

        // ---- AppBoot, Program, and shell startup ----
        ["log.boot.banner"] = new[] { "=== OSC Pusher V1 启动 ({0}) ===", "=== OSC Pusher V1 started ({0}) ===" },
        ["log.boot.safemode"] = new[] { "安全模式：不自动连接/检测/重连，等待手动操作", "Safe mode: auto-connect/detect/reconnect disabled, awaiting manual action" },
        ["log.boot.trace_on"] = new[] { "Trace 等级已开启（logs/trace.log）", "Trace level enabled (logs/trace.log)" },
        ["log.boot.osc_loaded"] = new[] { "osc_engine.dll: 已加载", "osc_engine.dll: loaded" },
        ["log.boot.osc_missing"] = new[] { "osc_engine.dll: 未找到（OSC 不可用）", "osc_engine.dll: not found (OSC unavailable)" },
        ["log.program.cli_mode"] = new[] { "进入 CLI 命令行模式", "Entering CLI command-line mode" },
        ["log.program.cli_exit"] = new[] { "CLI 退出", "CLI exiting" },
        ["log.program.silent_start"] = new[] { "静默启动：仅引擎与托盘，不拉起前端", "Silent start: engine and tray only, frontend not launched" },
        ["log.autostart.applied"] = new[] { "开机自启已更新：{0}", "Login autostart updated: {0}" },
        ["log.autostart.apply_fail"] = new[] { "开机自启部分方式应用失败：{0}", "Login autostart partially failed: {0}" },
        ["log.component.ok"] = new[] { "四组件版本与发布清单一致", "All four components match the release manifest" },
        ["log.component.mismatch"] = new[] { "组件版本不一致: {0} 实际 {1}，清单 {2}，{3}", "Component version mismatch: {0} actual {1}, manifest {2}, {3}" },
        ["log.github.update_available"] = new[] { "发现新版本: {0}（更新提示已弹出）", "Update available: {0} (notification shown)" },
        ["log.github.skipped"] = new[] { "已跳过此版本: {0}", "Skipped this release: {0}" },
        ["log.program.single_instance"] = new[] { "检测到已有实例在运行，转到旧实例", "Another instance is already running; switching to it" },
        ["log.program.exit_normal"] = new[] { "程序正常退出", "Program exited normally" },
        ["log.program.fatal"] = new[] { "未处理异常[{0}]: {1}", "Unhandled exception [{0}]: {1}" },
        ["log.proc.open_browser_fail"] = new[] { "打开浏览器失败: {0}", "Failed to open browser: {0}" },
        ["log.proc.webui_missing"] = new[] { "hrm-webui.exe 未找到", "hrm-webui.exe not found" },
        ["log.proc.webui_missing_fallback"] = new[] { "hrm-webui.exe 未找到，回退系统浏览器打开 Web UI", "hrm-webui.exe not found; opening Web UI in the system browser instead" },
        ["log.proc.webui_start_fail"] = new[] { "启动内置 Web UI 失败: {0}", "Failed to launch the built-in Web UI: {0}" },
        ["log.proc.web_unavailable"] = new[] { "Web 服务不可用（端口被占用？），内置界面无法打开", "Web service unavailable (port in use?); the built-in UI cannot be opened" },
        ["log.shell.exit_force"] = new[] { "收到 exit --force，程序退出", "Received exit --force; program exiting" },
        ["log.shell.reboot"] = new[] { "收到 reboot 命令，快照设备并重启引擎", "Received reboot; snapshotting devices and restarting engine" },
        ["log.shell.reboot_fail"] = new[] { "reboot 重启失败: {0}", "reboot relaunch failed: {0}" },
        ["log.program.reboot_note"] = new[] { "收到 reboot：本次启动自动回连上次设备", "Started by reboot: reconnecting last device(s)" },
        ["log.program.after_crash"] = new[] { "由壳在崩溃后自动拉起（自动恢复启动）", "Launched by the shell after a crash (auto-recovery start)" },
        ["log.crash.start"] = new[] { "崩溃测试触发: {0}", "Crash test triggered: {0}" },

        // ---- Webhook / Web / Remote / Auth ----
        ["log.webhook.load_fail"] = new[] { "webhook 加载失败: {0}", "Failed to load webhook: {0}" },
        ["log.webhook.save_fail"] = new[] { "webhook 保存失败: {0}", "Failed to save webhook: {0}" },
        ["log.web.start_fail"] = new[] { "Web UI 启动失败 (端口 {0}): {1}", "Web UI failed to start (port {0}): {1}" },
        ["log.web.start_ok"] = new[] { "Web UI 已启动: http://127.0.0.1:{0}  (前端: ok)", "Web UI started: http://127.0.0.1:{0}  (frontend: ok)" },
        ["log.web.start_noui"] = new[] { "Web UI 已启动: http://127.0.0.1:{0}  (前端: 缺失, 请重新构建 webui)", "Web UI started: http://127.0.0.1:{0}  (frontend missing; rebuild webui)" },
        // ---- P6 single listener and per-request source zones ----
        ["log.web.bind_lan"] = new[] { "单监听已绑定全部网卡 (端口 {0})；LAN/WAN 来源按请求分级授权", "Single listener bound all interfaces (port {0}); LAN/WAN sources authorized per request" },
        ["log.web.bind_loopback_fallback"] = new[] { "绑定全部网卡失败（{0}），回退仅回环 (端口 {1})；LAN/WAN 需要 URL 预留: netsh http add urlacl url=http://+:{1}/ user=Users", "All-interface bind refused ({0}); fell back to loopback (port {1}); LAN/WAN needs a URL ACL: netsh http add urlacl url=http://+:{1}/ user=Users" },
        ["log.web.source_reject"] = new[] { "已拒绝来源 {1}（原因: {0}）", "Rejected source {1} (reason: {0})" },
        ["log.web.lan_changed"] = new[] { "远程(LAN)访问开关: {0}", "Remote (LAN) access switch: {0}" },
        ["log.web.wan_changed"] = new[] { "WAN(公网)访问开关: {0}", "WAN (public) access switch: {0}" },
        ["log.web.wan_blocked_weak_password"] = new[] { "WAN 开启被拒绝：管理员密码未初始化/为默认/强度不足", "WAN enable refused: admin password uninitialized, default, or too weak" },
        ["log.web.remote_migrated"] = new[] { "旧 remote.host/port/loopbackOnly 配置已迁移到单端口来源分级模型", "Legacy remote.host/port/loopbackOnly migrated to the single-port source-zone model" },
        ["log.web.request_fail"] = new[] { "Web 请求处理失败: {0}", "Web request handling failed: {0}" },
        ["log.vrchat.launch"] = new[] { "启动 VRChat: {0}", "Launching VRChat: {0}" },
        ["log.vrchat.launch_fail"] = new[] { "启动 VRChat 失败: {0}", "Failed to launch VRChat: {0}" },
        ["log.auth.users_load_fail"] = new[] { "远程用户库加载失败: {0}", "Remote user store load failed: {0}" },
        ["log.auth.no_users"] = new[] { "远程用户库为空（路径 {0}）；首次启用 Remote 时将引导初始化管理员", "Remote user store is empty (path {0}); the first Remote enable will initialize the admin account" },
        ["log.auth.admin_initialized"] = new[] { "管理员已初始化: {0}", "Admin initialized: {0}" },
        ["log.auth.login_ok"] = new[] { "远程登录成功: {0} @ {1}", "Remote login succeeded: {0} @ {1}" },
        ["log.auth.login_fail"] = new[] { "远程登录失败: {0} @ {1}", "Remote login failed: {0} @ {1}" },
        ["log.auth.users_save_fail"] = new[] { "远程用户库保存失败: {0}", "Remote user store save failed: {0}" },
        ["log.auth.sessions_load_fail"] = new[] { "远程会话表加载失败: {0}", "Remote session store load failed: {0}" },
        ["log.auth.sessions_save_fail"] = new[] { "远程会话表保存失败: {0}", "Remote session store save failed: {0}" },
        ["log.auth.fingerprint_mismatch"] = new[] { "远程会话 UA 指纹不符（{0} @ {1}，第 {2} 次）", "Remote session UA fingerprint mismatch ({0} @ {1}, attempt {2})" },
        ["log.auth.fingerprint_destroy"] = new[] { "远程会话指纹连续不符达 {0} 次，已销毁（{1}）", "Remote session destroyed after {0} consecutive fingerprint mismatches ({1})" },

        // ---- AppHub settings and action logs ----
        ["log.hub.batch"] = new[] { "批量[{0}]: {1} 个设备", "Batch [{0}]: {1} device(s)" },
        ["log.hub.osc_test_ok"] = new[] { "OSC 测试发送: {0} '{1}' -> OK", "OSC test send: {0} '{1}' -> OK" },
        ["log.hub.osc_test_fail"] = new[] { "OSC 测试发送: {0} '{1}' -> 失败", "OSC test send: {0} '{1}' -> failed" },
        ["log.hub.osc_custom_pause"] = new[] { "OSC 自定义发送已发出，推送器暂停 {0}ms 防覆盖", "Custom OSC send sent; pusher paused {0} ms to avoid overwrite" },
        ["log.hub.osc_custom_ok"] = new[] { "OSC 自定义发送: {0} -> OK", "Custom OSC send: {0} -> OK" },
        ["log.hub.osc_custom_fail"] = new[] { "OSC 自定义发送: {0} -> 失败", "Custom OSC send: {0} -> failed" },
        ["log.hub.log_dump"] = new[] { "日志已转储: {0}", "Log dumped: {0}" },
        ["log.hub.float_src_saved"] = new[] { "浮窗数据源设置已保存", "Floating-window source saved" },
        ["log.hub.rec_started"] = new[] { "记录已开启", "Recording started" },
        ["log.hub.rec_stopped"] = new[] { "记录已关闭", "Recording stopped" },
        ["log.hub.health_saved"] = new[] { "健康判定参数已保存", "Health parameters saved" },
        ["log.hub.devices_saved"] = new[] { "设备策略已保存", "Device policy saved" },
        ["log.hub.hw_saved"] = new[] { "硬件变量设置已保存", "Hardware settings saved" },
        ["log.hub.frontend_exit"] = new[] { "前端请求退出", "Frontend requested exit" },
        ["log.hub.api_saved"] = new[] { "API Server 配置已保存", "API server config saved" },
        ["log.hub.device_blocked"] = new[] { "设备已屏蔽: {0}", "Device blocked: {0}" },
        ["log.hub.device_unblocked"] = new[] { "设备已解除屏蔽: {0}", "Device unblocked: {0}" },
        ["log.hub.osc_saved"] = new[] { "OSC 配置已保存到 config.json", "OSC config saved to config.json" },
        ["log.hub.debug_on"] = new[] { "Debug Mode 已开启", "Debug mode enabled" },
        ["log.hub.debug_off"] = new[] { "Debug Mode 已关闭", "Debug mode disabled" },
        ["log.hub.settings_saved"] = new[] { "设置已保存到 config.json", "Settings saved to config.json" },

        // ---- Legacy WinForms main window, tray, and floating windows ----
        ["log.main.loaded"] = new[] { "主窗体已加载", "Main form loaded" },
        ["log.main.hw_refresh"] = new[] { "硬件信息刷新已触发", "Hardware info refresh triggered" },
        ["log.main.autodump"] = new[] { "自动转储: {0}", "Auto dump: {0}" },
        ["log.main.webhook_enabled"] = new[] { "Webhook 已启用", "Webhook enabled" },
        ["log.main.webhook_disabled"] = new[] { "Webhook 已禁用", "Webhook disabled" },
        ["log.main.save_state"] = new[] { "设备保存状态已更新: {0}", "Device save state updated: {0}" },
        ["log.main.device_blocked"] = new[] { "设备已屏蔽: {0}", "Device blocked: {0}" },
        ["log.main.batch_connect"] = new[] { "批量连接: {0} 个设备", "Batch connect: {0} device(s)" },
        ["log.main.batch_save"] = new[] { "批量保存: {0} 个设备", "Batch save: {0} device(s)" },
        ["log.main.wh_test_ok"] = new[] { "Webhook 测试成功 (HTTP {0}): {1}", "Webhook test OK (HTTP {0}): {1}" },
        ["log.main.wh_test_fail"] = new[] { "Webhook 测试失败: {0}", "Webhook test failed: {0}" },
        ["log.main.osc_test_ok"] = new[] { "OSC 测试发送: {0} '{1}' -> OK", "OSC test send: {0} '{1}' -> OK" },
        ["log.main.osc_test_fail"] = new[] { "OSC 测试发送: {0} '{1}' -> 失败", "OSC test send: {0} '{1}' -> failed" },
        ["log.main.api_started"] = new[] { "心率API服务器已启动 :{0}/heartrate", "Heart-rate API server started on :{0}/heartrate" },
        ["log.main.api_start_fail"] = new[] { "API服务器启动失败: {0}", "API server failed to start: {0}" },
        ["log.main.api_stopped"] = new[] { "心率API服务器已停止", "Heart-rate API server stopped" },
        ["log.main.settings_saved"] = new[] { "设置已保存到 config.json", "Settings saved to config.json" },
        ["log.float.locked"] = new[] { "悬浮窗已锁定 ({0})", "Floating window locked ({0})" },
        ["log.float.unlocked"] = new[] { "悬浮窗已解锁 ({0})", "Floating window unlocked ({0})" },
        ["log.fhost.open"] = new[] { "悬浮窗已打开: {0}", "Floating window opened: {0}" },
        ["log.tray.action_fail"] = new[] { "托盘操作失败: {0}", "Tray action failed: {0}" },
        ["log.tray.web_stopped"] = new[] { "Web UI 已停止", "Web UI stopped" },
        ["log.tray.web_started"] = new[] { "Web UI 已启动", "Web UI started" },
        ["log.logger.dump_fail"] = new[] { "转储失败: {0}", "Dump failed: {0}" },

        // ---- Hardware collection, variables, and PDH ----
        ["log.hwinfo.reg"] = new[] { "硬件(注册表): CPU={0} | MB={1} {2} | OS={3} {4}", "Hardware (registry): CPU={0} | MB={1} {2} | OS={3} {4}" },
        ["log.hwinfo.wmic"] = new[] { "硬件(wmic): CPU={0} | GPU0={1} | DIMM0={2}", "Hardware (wmic): CPU={0} | GPU0={1} | DIMM0={2}" },
        ["log.hwinfo.pci"] = new[] { "硬件(Get-ComputerInfo): OS={0} {1} | MB={2} {3}", "Hardware (Get-ComputerInfo): OS={0} {1} | MB={2} {3}" },
        ["log.hwinfo.sysinfo"] = new[] { "硬件(systeminfo): OS={0} | 内存={1} | CPU={2}", "Hardware (systeminfo): OS={0} | RAM={1} | CPU={2}" },
        ["log.var.ntp_offset"] = new[] { "[ntp] {0} 偏移 {1} ms", "[ntp] {0} offset {1} ms" },
        ["log.var.ntp_fail"] = new[] { "[ntp] 查询失败 ({0}): {1}", "[ntp] query failed ({0}): {1}" },
        ["log.var.calc_fail"] = new[] { "[var] {0} 计算失败: {1}", "[var] {0} computation failed: {1}" },
        ["log.var.cmd_fail"] = new[] { "[var] 命令执行失败: {0}", "[var] command execution failed: {0}" },
        ["log.pdh.open_fail"] = new[] { "[pdh] PdhOpenQuery 失败，回退 PerformanceCounter", "[pdh] PdhOpenQuery failed; falling back to PerformanceCounter" },
        ["log.pdh.counter_unavailable"] = new[] { "[pdh] 计数器不可用: {0}", "[pdh] counter unavailable: {0}" },

        // ---- Export ----
        ["log.export.db_done"] = new[] { "已导出数据库: {0}", "Database exported: {0}" },
        ["log.export.done"] = new[] { "已导出 {0} ({1} 行, {2}): {3}", "Exported {0} ({1} rows, {2}): {3}" },
        ["log.export.fail"] = new[] { "导出失败 ({0}/{1}): {2}", "Export failed ({0}/{1}): {2}" },
        ["log.export.logs_done"] = new[] { "已导出日志 ({0} 行, {1}): {2}", "Logs exported ({0} rows, {1}): {2}" },
        ["log.export.logs_fail"] = new[] { "日志导出失败 ({0}): {1}", "Log export failed ({0}): {1}" },
        ["log.export.report_done"] = new[] { "已导出统计报表 ({0}): {1}", "Statistics report exported ({0}): {1}" },
        ["log.export.report_fail"] = new[] { "报表导出失败 ({0}): {1}", "Report export failed ({0}): {1}" },

        // ---- Standalone CLI entry point: hrmcli.exe ----
        ["log.cli.start"] = new[] { "hrmcli 启动（CLI 模式）", "hrmcli started (CLI mode)" },
        ["log.cli.unhandled"] = new[] { "hrmcli 未处理异常: {0}", "hrmcli unhandled exception: {0}" },
        ["log.cli.exit"] = new[] { "hrmcli 退出", "hrmcli exited" },
    };

    /// <summary>Resolve a log message for the current language, fill placeholders such as {0}, and return the key unchanged when missing.</summary>
    public static string L(string key, params object?[] args)
    {
        if (!Table.TryGetValue(key, out var row)) return key;
        var s = row[App.Lang is "zh-cn" or "zh-tw" or "zh-hk" or "yue-hk" ? 0 : 1];
        return args.Length == 0 ? s : string.Format(s, args);
    }
}
