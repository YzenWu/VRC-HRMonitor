# HeartRateMonitor V1 重构计划（plan.md）

> 目的：抛弃 Python 与解耦式多进程设计，改为「C# 单进程 WinForms 主程序 + C 语言 OSC 引擎 DLL」。
> 本文档是重构的执行蓝图，随实现推进同步更新；每步实际改动记录见 `diff.md`。

## 0. 背景与目标

旧版（`C# Test/`，已归档到 `OldPy/`、`Built/`、`Example/`）存在性能/核心功能小缺陷：
- Python 前端 + 4 个 C# 子进程（Launcher / osc_com / blectl / sysinfo）解耦设计，进程间 TCP/广播协调复杂；
- OSC 推送等热路径在 C# 托管代码中，编码/发送每次分配缓冲。

V1 目标：
1. **单进程**：一个 `HeartRateMonitor.exe`（C# WinForms）承载 UI + 蓝牙 + 硬件采集 + Webhook；
2. **C 语言 OSC 引擎**：`osc_engine.dll`（gcc 编译），承担 OSC 编码/解码/发送/接收热路径，C# 经 P/Invoke 调用；
3. **允许 DLL 散落**：不再强制单文件发布；
4. **硬件信息**改用标准命令 + 注册表（见 §4）；
5. **前端风格一致**：Tab 标签页 + 多窗口；
6. 新的构建脚本支持 `--standalone / --releases / --debug / 交互式`。

## 1. 目录结构（新建）

```
C# V1/
├── about.md / plan.md / diff.md      # 项目说明 / 重构计划 / 修改日志
├── build.bat / build.ps1             # 构建脚本（参数驱动）
├── config.json / config_webhook.json # 运行配置
├── Engine/                           # C OSC 引擎（gcc → osc_engine.dll）
│   ├── osc_engine.h
│   └── osc_engine.c
└── App/                              # C# 主程序（WinForms, net10.0-windows10.0.19041）
    ├── HeartRateMonitor.csproj
    ├── Program.cs                    # 入口：模式处理（debug/release 异常策略）
    ├── AppContext.cs                 # 全局单例：配置/日志/状态/心率缓存
    ├── Config.cs                     # config.json / config_webhook.json 读写
    ├── Logger.cs                     # 控制台 + 文件 + 日志Tab 三路输出
    ├── Osc/
    │   ├── OscEngine.cs              # osc_engine.dll P/Invoke 包装
    │   └── OscService.cs             # 推送 Job（定时器）+ 接收事件 + 连接状态
    ├── Ble/BleManager.cs             # Windows.Devices.Bluetooth（从 blectl 移植）
    ├── SysInfo/
    │   ├── Vars.cs                   # 模板变量表（沿用旧 sysinfo 变量名）
    │   ├── SysInfoService.cs         # 采集调度：fast（实时）+ full（静态）
    │   └── Collectors/
    │       ├── RegistryInfo.cs       # 注册表（主来源，快）
    │       ├── WmicInfo.cs           # wmic（仅 Win10 及更早；Win11 已移除）
    │       ├── PsComputerInfo.cs     # PowerShell Get-ComputerInfo
    │       └── SystemInfoExe.cs      # systeminfo 命令
    ├── Webhook/WebhookManager.cs     # Webhook 出站（从 osc_com 移植）
    └── UI/
        ├── MainForm.cs               # TabControl：OSC/心率/硬件/Webhook/日志/设置
        ├── FloatingWindow.cs         # 悬浮窗（锁定/穿透/图片/颜色）
        └── WebhookWindow.cs          # Webhook 编辑窗口
```

## 2. 组件设计

### 2.1 C OSC 引擎（Engine/osc_engine.c）

导出 C ABI（`CallingConvention.Cdecl`），全部在 C 侧持有 UDP socket：

```c
// 编码：address + 类型化参数数组 → OSC 字节流（4 字节对齐），返回长度；out 不足返回 -1
int osc_encode_message(const char* address, const osc_arg_t* args, int argc,
                       unsigned char* out, int out_cap);

// 发送（内部持有一个 UDP socket，互斥保护；失败返回 -1）
int osc_engine_send(const char* ip, int port, const unsigned char* data, int len);

// 快速路径：文本 → OSC 字符串消息直接发送（常用：/chatbox/input）
int osc_engine_send_text(const char* ip, int port, const char* address, const char* text);

// 解码：包（消息或 bundle）→ JSON 文本（{ "address":..., "args":[{"t":"s","v":"..."}] }），供 C# JsonDocument 解析
int osc_decode_to_json(const unsigned char* data, int len, char* out, int out_cap);

// 接收线程：绑定 UDP 端口，收到包后回调 (raw, len, user)；返回 0 成功
typedef void (*osc_receive_fn)(const unsigned char* data, int len, void* user);
int osc_engine_start_receiver(int port, osc_receive_fn cb, void* user);
void osc_engine_stop_receiver(void);
```

- `osc_arg_t`：`{ osc_type_t type; union{ const char* s; int32_t i; float f; int64_t h; double d; } v; const unsigned char* blob; int blob_len; }`，类型枚举 `OSC_T_STR/INT32/FLOAT/INT64/DOUBLE/BLOB/TRUE/FALSE/NIL`；
- 类型标签 `,sifhdbTFN`，编码规则与旧 `Osc.cs` 完全一致（int32/float 大端，int64/double 大端字节序，字符串 4 对齐补零）；
- bundle 解码支持（跳 timetag、递归子消息）。

### 2.2 OSC 服务（C#）

- `OscService`：`System.Threading.Timer` 推送 Job（间隔=config，最小 50ms）；每个周期：
  1. 用 `SysInfoService` 当前变量 + 心率（BPM0=首选设备，无则平均）替换模板 `{变量}`；
  2. 调 `osc_engine_send_text`（C 引擎快速路径）；
  3. 统计 发送/失败 计数，更新 UI。
- 接收：`osc_engine_start_receiver(9001)` → C 回调 → `osc_decode_to_json` → 事件 `OscReceived(address, args)`，写入日志/广播（Webhook 可引用）。

### 2.3 蓝牙（C#，从 blectl 移植）

直接移植 `BleManager.cs`（Windows.Devices.Bluetooth）：扫描（Active）、连接、GATT 0x180D/0x2A37 订阅、心率字节解析（flags bit0 → UINT8/UINT16）、多设备、断开清理。事件 → 主 UI 与心率缓存。

### 2.4 硬件信息采集（4 种标准方法，见 §4）

### 2.5 配置（config.json，V1 精简版）

```jsonc
{
  "app":     { "debug": 0 },
  "osc":     { "ip": "127.0.0.1", "port": "9000", "address": "/chatbox/input",
               "interval_ms": "1000", "receive_port": "9001", "template": "..." },
  "heart_rate": { "devices": [], "display_source": "平均",
                  "window": { "visible": false, "locked": false, "geometry": "200x80+100+100",
                              "unlocked_color": "#00FF00", "locked_color": "#FF6600",
                              "format": "❤️{bpm}", "image_path": null } },
  "webhook": { "enabled": false },
  "logs":    { "auto_dump_enabled": true, "auto_dump_interval_min": "30", "dump_dir": "logs/auto" }
}
```
- 移除旧版 `launcher / osc_com / blectl / announce` 段（单进程后不再需要控制口/广播）。

## 3. 前端（WinForms）

- `MainForm`：`TabControl` 六个页签，布局/文案沿用 Python 版（ttk.Notebook）：
  1. **OSC**：IP/端口/地址、推送间隔(ms)、模板多行编辑、连接/保存按钮、状态、发送/失败计数、预览；
  2. **心率**：设备列表（ListView：名称/MAC/心率/RSSI/类型）、扫描/停止、连接/断开、大字号心率显示、显示来源（平均/指定）；
  3. **硬件**：分组列表（CPU/主板/BIOS/OS/内存/GPU/磁盘/网卡）+ 变量值两列视图 + 手动刷新；
  4. **Webhook**：预设列表、新增/编辑/删除/测试/启用（WebhookWindow 编辑）；
  5. **日志**：自动滚动 TextBox + 过滤框；debug 模式同时输出控制台；
  6. **设置**：悬浮窗（显示/锁定/颜色/格式/图片）、自动转储、保存设置。
- 多窗口：`FloatingWindow`（置顶、锁定可点击穿透、心率或图片显示）、`WebhookWindow`。
- 全部 UI 操作经 `Invoke` 切到 UI 线程；后台任务（BLE 事件/推送/采集）不阻塞界面。

## 4. 硬件信息获取方式（按用户指定）

| 方式 | 适用 | 用途 |
|---|---|---|
| 1) `wmic` | 仅 Windows 10 及更早（Win11 2024 左右已移除 wmic） | CPU/主板/BIOS/GPU/内存条/磁盘/网卡 静态信息（`/format:list`） |
| 2) `Get-ComputerInfo`（PowerShell） | 全部 | OS/BIOS/主板/内存总量 等（`Key : value` 解析） |
| 3) `systeminfo` | 全部 | OS 版本/制造商/型号/总内存/网卡 等（`Key: value` 解析，容错本地化） |
| 4) 注册表 | 全部（最快，主来源） | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`（ProductName/版本）；`HKLM\HARDWARE\DESCRIPTION\System\BIOS`（制造商/型号/BIOS） |

实时指标（CPU/内存占用、GPU/VRAM、温度）不属于静态硬件信息，沿用标准 PDH 性能计数器（`System.Diagnostics.PerformanceCounter`），与旧版一致。
采集策略：**fast**（定时 ~2s，实时指标 + 注册表快速项）、**full**（启动后台/手动，wmic/Get-ComputerInfo/systeminfo 补全静态项），全部带超时与降级（某方法失败则跳过，不阻塞）。

变量表沿用 `Vars.cs` 清单（BPM_*、CPU_*、RAM_*、GPU_*、VRAM_*、DISK_*、DRIVE_*、DIMM_*、NIC_*、OS_*、UPTIME、FOCUS_* 等）。

## 5. 构建脚本（build.bat / build.ps1）

```
build.bat [--standalone | --releases | --debug]        # 无参 → 交互式询问序号+回车
build.ps1 [-Mode standalone|releases|debug] [-Python 不再需要] [-SkipFrontend]
```

流程（powershell）：
1. `gcc -shared -O2 -o Engine\osc_engine.dll Engine\osc_engine.c -lws2_32`（MinGW；失败则中止）；
2. `dotnet publish App -c <配置> -r win-x64 -p:UseWindowsForms ...`，输出到 `Built\<分支>\<时间戳>\`；
3. 复制 `osc_engine.dll`、`config.json`、`config_webhook.json` 到输出目录；
4. 模式差异：
   - `--standalone`：`--self-contained` 独立免安装（自带 .NET 运行时），`OutputType=WinExe`；
   - `--releases`：框架依赖（安装 .NET 10 运行时），`WinExe`（无控制台窗口），故障容忍=记录日志后继续运行/友好提示；
   - `--debug`：`Debug` 配置 + 控制台（`OutputType=Exe`），日志最详细（控制台 + 文件 + 日志Tab），未处理异常 → Error Trace 弹窗（含线程栈转储）后退出；
5. 输出构建摘要（exe/dll/配置齐全性、引擎版本）。

## 6. 验证清单

- [x] gcc 编译 `osc_engine.dll` 无告警，P/Invoke 编码与旧 `Osc.cs` 输出逐字节一致（含 s/i/f/h/d/b/T/F/N 与 bundle 解码）；
- [x] `osc_engine_send_text` 推送被测试监听收到（ctypes 验证编码字节）；
- [x] 接收 9001 收到外部 OSC 并正确解码为 JSON 事件（含去重）；
- [x] BLE 扫描/连接/心率订阅正常（BleManager 移植，待真机验证）；
- [x] 4 种硬件采集方法各自输出正确（注册表/wmic/Get-ComputerInfo/systeminfo 实测通过），变量表完整，fast/full 均不阻塞 UI；
- [x] 六页签 UI + 悬浮窗/Webhook 窗口可用；模板变量替换正确；
- [x] 三种构建分支可产出，releases 无控制台、debug 控制台+日志Tab、standalone 免安装可运行（冒烟通过）；
- [x] config.json 保存/加载往返一致；中文无乱码（build 脚本全部 ASCII）；
- [x] 单进程退出干净，无旧版进程/端口残留。

---

## 7. 第八轮：UI 重写为 11 Tab + 多语言 + 主题（先本地 Rust 前端，再 Web 前端）

> 用户要求：保留现有风格，重写 UI 逻辑，新增多语言实时切换；本地前端（Rust egui）先行，Web（SolidJS）随后。第 5 步起涉及较多后端（C# AppHub / PipeServer / 服务层）扩展，需同步设计命令与事件。
> 参考资料：`Skills/VRChat Interfaces.skill`（OSC 端点）、`Skills/PerfGet.skill`（性能获取）。

### 7.0 目标与原则
1. **12 个页面枚举**：OSC / Pusher / HeartBeat / Devices / HWInfo / HeadSet / API / Settings / Logs / Console / Monitor（外加保留「总览」由 HeartBeat 顶部承担，或单独 `Overview`，见下）；
2. **保留视觉风格**：侧边栏导航 + 顶栏（心率 pill / 刷新 / 退出）+ 卡片布局、宽松间距、控件最小 34px（Rust）/ 现有 SolidJS CSS 类（Web）；
3. **多语言**：简体中文 / 繁體中文(台湾习惯) / English / 日本語 / Español 五种，实时切换，持久化到 config；
4. **主题**：Dark（现有）/ Deep / Forest / Sunset + 自定义色，实时切换，持久化；
5. **参数自定义与持久化**：所有可配置项写入 `config.json`（新增段），前端读入再写回；
6. **导出统一**：涉及「导出/转储」的板块支持 YAML / JSON / CSV / SQLite 至少两种以上。
> 说明：本计划覆盖「11 页 Tab + i18n + theme + 后端支撑」的设计与实施顺序；`diff.md` 按步骤 26 起记录每次落地。

### 7.1 后端命令 / 事件扩展（C# 侧，前端依赖）
PipeServer (`App\Ipc\PipeServer.cs`) 与 Web API (`App\Web\WebServer.cs`) 共用 AppHub。新增以下命令与推送事件：

| 命令 | 作用 | 新增依赖 |
|---|---|---|
| `osc_events` / 推送 `osc_recv` | OSC 9001 收到的参数回传（`/input/*`、`/avatar/parameters/*`、`/avatar/change` 等，见 VRChat skill） | `App\Osc\OscService.cs` 已有接收回调 → 补结构化 `OscReceived(address, args)` 事件并广播 |
| `pusher_*`（list/save/delete/test） | Pusher 多规则引擎：每条规则 = 触发事件 + 目标端点 + 负载模板（如「数据刷新时给 `/chatbox/typing` 写 1」） | 新增 `App\Pusher\PusherManager.cs`，订阅 AppHub 事件后按规则发 OSC |
| `hr_history` / 推送 `hr_point` | 心率曲线数据（时间戳 + 设备 mac + bpm） | 新增环形缓冲 `App\Core\HrHistory.cs`，HeartBeat：采样入队（上限≈记录时长） |
| `hr_status` | HEALTH_STATUS 判定（sleep / active / excited）+ 校准基线 | 新增 `App\Core\HealthStatus.cs`（HR + 姿态 VRC 参数 + 校准），心率大幅变动打 WARN |
| `device_rename` / 推送 `device_found` 带缓存标识 | 支持为设备重命名（如 Apple Watch → Left Hand），缓存优先复用已知名称 | `App\Ble\BleManager.cs` 增加 `_knownNames` 持久化 + `Rename(mac, name)`；`AppHub._known` 落盘 |
| `device_sort` | 设备排序（名称关键词/强度/可用性）由后端排序返回 | `AppHub.AllMacs()` 重构排序规则 + `Config` 存排序权重 |
| `device_reconnect` | 自动重连（断开后按频率扫描并重连，30 分钟无响应放弃） | `BleManager`/新增 `App\Ble\ReconnectionPolicy.cs` 定时器 |
| `hw_realtime` / 推送 `hw_point` | 实时睿频/句柄/线程/进程数（浮点，单位可指定） | `SysInfoService`（PDH + `GetProcessHandleCount` + 线程进程数，见 PerfGet.skill） |
| `hw_override` | 用户覆写变量并持久化（含运算/拼接符、精度/四舍五入） | 新增 `App\Core\VarOverrides.cs`（表达式求值 + 精度） |
| `hw_time` | Local / UTC / NTP 时间、时区变量 | `SysInfoService` 时间变量；NTP 用可选 SNTP 请求 |
| `api_*`（heartbeat/ws/echo） | API Server：`/heartbeat`、WebSocket、WebHook；除心率外也回传系统信息 | `Web\WebServer.cs` 扩展路由 + WS 推送 |
| `console_exec` | Console 内建 CLI（Help/?、可滚动） | 新增 `App\CLI\ConsoleService.cs`（复用 CliApp 命令 + 历史缓冲） |
| `monitor_*`(query/export) + `db_*` | Monitor 汇总历史游戏记录/健康/睡眠；多格式导出 | 新增 `App\Data\Store.cs`（SQLite）+ 导出器（YAML/JSON/CSV） |
| `settings` 扩展 | 接受 `i18n` / `theme` 段写入 config | `Config.cs` 增加 `I18n.Lang`、`UI.Theme` |
| 推送 `log` 增强 | 带日志等级字段（`level`）供前端等级过滤 | `Logger.cs` `OnLog` 增字段 |

> 事件 `osc_recv`、`hr_point`、`hw_point`、`hr_status`、`device_found`(改)、`log`(改) 需同步加入 `state.rs`（Rust）与 `store.tsx`（Web）的 `apply_event`。

### 7.2 Rust 前端（本地，默认 UI）—— 主战场

**文件结构改造（`RustUi/src/`）**
```
└── src/
    ├── main.rs        # 入口 + 顶栏/侧栏布局 + 页面分发（枚举扩到 11+1）
    ├── state.rs       # AppState 扩充 + apply_event 新增事件 + 各响应解析
    ├── client.rs      # 管道 IPC（保持不变——已解决读线程/超时问题）
    ├── i18n.rs        # 多语言表（zh-Hans/zh-Hant/en/ja/es，key→文本）+ 当前语言
    ├── theme.rs       # 主题定义（Dark/Deep/Forest/Sunset）+ 自定义色 + 应用给 egui
    ├── chart.rs       # 心率曲线（egui_plot 或自绘）：平滑度/记录时长/分设备
    └── pages/         # 拆页文件，避免 main.rs 膨胀
        ├── osc.rs pusher.rs heartbeat.rs devices.rs hwinfo.rs headsets.rs
        ├── api.rs settings.rs logs.rs console.rs monitor.rs
```
- `Page` 枚举扩为：`Overview`(默认，或并入 HeartBeat) + 上述 11 项；侧栏图标/文本取自 `i18n.rs`。
- **i18n 实现**：`static LANG: &str`；`t(key)` 查表；所有硬编码中文改为 `t()`；语言下拉放 Settings；切换后 `ctx.set_fonts` 重载（中西文同字体，无需重载）。
- **主题实现**：`theme.rs` 提供 `fn apply(ctx, &Theme)` 把颜色写入 `ctx.set_visuals`；主题选择与自定义色存 config。
- **每页要点**（与 7.1 命令一一对应）：
  - **OSC**：运行状态卡片 + 各参数监视卡片（`/input/*`、`/avatar/parameters/*`、`/avatar/change` 等）实时显示；「记录到数据库」开关。
  - **Pusher**：规则列表 + 编辑弹窗（触发事件 / 端点 / 负载模板）＋ 启用/置顶「数据刷新时 typing=1」快捷开关。
  - **HeartBeat**：单设备 → 顶部横卡（左 ¼ 实时大数字，右 ¾ 曲线，中间 Splitter 可拖）；多设备 → 左侧设备列表（名/信号/bpm/报告频率）+ 右侧均值曲线（右上角可选设备）；底部卡：浮窗管理（并入此处）、首用校准、数据转储；HealthStatus 指示。
  - **Devices**：高刷扫描 + 参数；设备行（等宽字体）按后端排序显示；改名；缓存标识；自动重连开关；信号弱提示。
  - **HWInfo**：实时卡（睿频/句柄/线程/进程数，浮点 + 单位可调）+ 变量表 + 覆写编辑器（运算/拼接/精度）+ 时间变量 + 自定义变量（正则/CLI 管道）。
  - **HeadSet**：WIP 占位。
  - **API**：`/heartbeat`、WS、WebHook 配置 + 状态；支持像 OSC 一样回传系统信息的开关。
  - **Settings**：多语言下拉、主题选择（含自定义色）、聚合各页高级设置。
  - **Logs**：等级过滤 + 正则过滤 + 自动滚动/转储。
  - **Console**：内建 CLI（Help/?/可滚动历史），复用引擎命令。
  - **Monitor**：历史游戏记录 / 健康 / 睡眠汇总卡 + 分析 + 导出（YAML/JSON/CSV/SQLite）。
- **校验**：`cargo build`（debug/release 双分支产出）；trace 埋点沿用 `trace.rs`。

### 7.3 Web 前端（SolidJS，按需）
与 Rust 页面一一对应（`WebUI/src/pages/` 现有 Dashboard/Devices/OscPage/Hardware/Webhooks/Logs/Settings → 增 Pusher/HeartBeat/HWInfo/HeadSet/API/Console/Monitor，重命名 Dashboard→Overview 可选）。
- `store.tsx`：扩 `apply_event` 分支 + 新增查询函数；`api.ts`：新增命令封装。
- `styles.css`：新增主题变量（`--bg/--card/--accent` 等），i18n 走 `t()`（小字典）；`index.html` lang 属性动态。
- 主题/语言切换经 `settings` 命令写回 config，重载后生效。

### 7.4 config.json 新增段（方案）
```jsonc
{
  "i18n": { "lang": "zh-Hans" },
  "ui":  { "theme": "Dark", "custom": { "bg": null, "card": null, "accent": null } },
  "pusher": { "rules": [ { "event": "heart_rate", "endpoint": "/chatbox/typing",
                           "template": "1", "enabled": true } ] },
  "devices_policy": { "autoReconnect": true, "reconnectIntervalSec": 10,
                      "giveUpSec": 1800, "sortWeights": {...} },
  "hw": { "precision": 2, "round": true, "overrides": { "CPU_USAGE": "x*100" },
          "customVars": [ { "name": "...", "expr": "...", "via": "regex|cli" } ] },
  "hr_health": { "calibrated": false, "baselineRest": 70 },
  "data": { "db": "store.sqlite", "recordOsc": true, "recordHr": true }
}
```

### 7.5 分阶段实施顺序（本轮先做阶段 A/B，其余按里程碑推进）
- **阶段 A（骨架，最小可用）**：Page 枚举 11+1、侧栏与 i18n 表、主题枚举与 `apply`、页面分发；把现有 7 个页面内容迁移到新 `pages/` 并补 `t()`/主题色。先保证构建通过、`--gui` 可跑。
- **阶段 B（HeartBeat 曲线 + 浮窗并入 + HealthStatus）**：后端 `HrHistory`/`HealthStatus`/`osc_recv` 广播 + Rust 曲线卡与设备列表；校准流程；浮窗管理迁入本页；悬浮窗补解锁按钮（用户缺角提醒）。
- **阶段 C（Devices 增强）**：缓存标识、改名、排序、自动重连、信号弱日志。
- **阶段 D（Pusher + OSC 参数监视 + API）**。
- **阶段 E（HWInfo 增强 + Console + Monitor + 导出）**。
- **阶段 F（Web 前端对齐）**。

### 7.6 验证清单（本轮初）
- [ ] 阶段 A：Rust debug/release 构建通过；11+1 页可导航；i18n 五种语言实时切换生效并持久化；主题四种可切并持久化。
- [ ] 阶段 B：单/多设备 HeartBeat 卡片与曲线正确；浮窗管理在本页可解锁/移动/穿透；HealthStatus 输出到 `{HEALTH_STATUS}`；心率大幅变动命中 WARN；校准流程可用。
- [ ] 阶段 C：设备缓存标识复用、改名持久化、按权重+信号排序、自动重连、RSSI 弱告警。
- [ ] 阶段 D：Pusher 规则生效（如刷新时 typing=1）；OSC 参数监视卡显示实时回传并可记录；API `/heartbeat`+WS+WebHook 可用。
- [ ] 阶段 E：HWInfo 实时指标（睿频/句柄/线程/进程）+ 覆写/精度/时间变量；Console 内建 CLI；Monitor 汇总 + 多格式导出。
- [ ] 阶段 F：Web 前端对齐 11+1 页 + i18n + 主题；Rust 与 Web 共用同一套命令/事件协议（不重复逻辑）。
- [ ] CLI selftest 回归 PASS；三分支构建通过。
