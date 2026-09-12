# HeartRateMonitor 项目说明（about.md）

> 面向 VRChat 聊天框的**心率 + 硬件信息 OSC 推送工具**。本文档描述项目（Windows 版）的架构与结构；每步改动记录见 `diff.md`，历史计划归档在 `Windows/docs/Archives/`。
> 平台范围：**仅 Windows x64**。VRChat 本身是 Windows 原生游戏，Linux 适配无实际收益，相关规划已于第二十三轮移除。

## 1. 用途

- 通过 **BLE 蓝牙心率设备**（标准 Heart Rate Service 0x180D）实时读取心率，**多设备同时连接**（上限只受蓝牙协议与硬件约束）；
- 以 **OSC 协议（UDP）**把「心率 + CPU / GPU / 内存等硬件信息」按模板推送到 VRChat 聊天框 `/chatbox/input`，模板编辑实时预览（按 Unicode 码点计数，接近 134 字提示 ChatBox 144 字上限，只提示不拦截）；
- 桌面**悬浮窗**（主窗或按设备单独开窗、锁定/点击穿透、DPI 感知缩放、几何独立持久化）；
- **Webhook** 出站推送；OSC 接收（9001）可捕获 VRChat 参数流量；
- 收集并展示 Windows 主机硬件信息（含按进程/按文件的动态变量），供 OSC 模板变量引用；
- 健康状态判定（校准 + 阈值系数）、多后端记录（SQLite / JSONL / CSV）与统计分析导出；
- VRChat 启动入口与运行状态监视（总览 / OSC / 推送页共享状态卡）；
- **Toolkit 工具集**（VRChat config 编辑、日志浏览、缓存清理、照片索引搜索、游戏分析、进程分析）；
- **远程 Web 第二前端**（手机/平板经同一端口访问，LAN/WAN 来源分级 + HTTPS 支持）。

## 2. 架构总览（C# .NET 10 引擎 + WebView2 壳 + Vue3 WebUI + C OSC 引擎）

发布物为**四个可执行组件 + 一个 C 引擎 DLL**，版本由 `Windows/Release.json` 统一声明、构建时注入四个 EXE 并做 FileVersion 交叉校验；运行时另有四组件探针校验（GUI 弹原生 TaskDialog、CLI 打印明细表，见 §7）：

| 组件 | 角色 |
|---|---|
| `HeartRateMonitor.exe` | 主引擎（C# `net10.0-windows10.0.19041`，业务/服务/托盘/悬浮窗/Web 后端） |
| `hrm-webui.exe` | 内置 WebView2 壳，主前端宿主 |
| `hrmcli.exe` | 独立 CLI/TUI（与 `HeartRateMonitor.exe --cli` 完全等价） |
| `hrmdump.exe` | 崩溃守护（引擎异常退出时通知壳 / 写崩溃记录） |
| `osc_engine.dll` | C OSC 引擎（gcc MinGW，编解码/收发热路径） |

```
┌────────────────────────────────────────────────────────────────┐
│ HeartRateMonitor.exe  (C#, net10.0-windows10.0.19041)           │
│  · 共享业务核心 AppHub：服务事件统一分发 + 全部命令实现           │
│  · 单实例守卫 SingleInstance（命名 Mutex + IPC activate）        │
│  · IPC：PipeServer 命名管道（\\\\.\\pipe\\hrm_v1，JSON 行）       │
│  · Web：WebServer 静态托管 webui/ + REST API + WS               │
│    （单端口 web.port 默认 9460，本地/远程共用，来源分级，见 §6） │
│  · 托盘宿主 TrayHost（隐藏窗体 + NotifyIcon）                    │
│  · 悬浮窗 FloatWindowHost（多窗管理）+ FloatingWindow（WinForms）│
│  · OSC 服务：定时推送 Job + 接收事件 + 可选启动即推送            │
│  · 蓝牙 BleManager（Windows.Devices.Bluetooth）                 │
│  · 硬件采集 SysInfoService（注册表 + wmic + Get-ComputerInfo     │
│     + systeminfo + PDH 实时指标 + 进程/文件动态变量）            │
│  · 健康判定 HealthService · 记录 HrmDb/RecordStore               │
│  · Toolkit/VrcToolkitService（六工具后端）                       │
│  · GitHubProjectService（项目信息/更新检查，ETag 缓存）          │
│  · AutoStartManager（计划任务/Run/启动文件夹）                   │
│  · WebhookManager、RemoteAuth、Logger（目录化多语言）            │
└──┬────────────┬──────────────┬────────────────────────────────┘
   │ HTTP/WS    │ 命名管道 IPC  │ P/Invoke (Cdecl)
┌──▼──────────┐ │              │
│hrm-webui.exe│ │              │
│(WebView2 壳) │ │              │
│主前端·无系统 │ │              │
│顶栏·仿 macOS │ │              │
│标题栏·圆角   │ │              │
└─────────────┘ │              │
        ┌───────▼────────┐     │      ┌────────────────┐
        │ hrmcli.exe     │     │      │ hrmdump.exe    │
        │ (CLI/TUI)      │     │      │ 崩溃守护：引擎 │
        │ 一次性命令/    │     │      │ 异常退出时通知 │
        │ REPL/菜单 TUI  │     │      │ 壳并落盘现场   │
        └────────────────┘     │      └────────────────┘
        ┌──────────────────────▼─┐
        │ osc_engine.dll  (C, gcc MinGW)
        │  · OSC 编码/解码（4 字节对齐，消息+bundle）
        │  · UDP 发送 socket（快速路径 send_text）
        │  · UDP 接收线程（9001）→ 回调 → JSON
        └───────────┬────────┘
                    │ OSC/UDP
        ┌───────────▼────────┐
        │ VRChat / 其他 OSC 应用（9000 收 / 9001 发）│
        └────────────────────┘
```

设计要点：
- **抛弃 Python 与解耦式多进程设计**：不再有 Launcher/osc_com/blectl/sysinfo 子进程与端口广播；
- 性能敏感热路径（OSC 编码/发送/接收）由 **C 引擎**承担，C# 只做业务编排；
- **主前端为内置 WebView2 壳 `hrm-webui.exe`**（Vue 3 Web UI，无系统标题栏 + 仿 macOS 交通灯 + 可配圆角），启动时自动带起 Web 服务；
- Rust egui 前端（`hrm-ui.exe`）已于第二十一轮整体移除：单一 Web 前端，不再有前端切换/回退分支，壳缺失仅提示；
- `hrmcli.exe` 与 `HeartRateMonitor.exe --cli` 完全等价 —— 同一份 `AppBoot.Init` 启动序列 + 同一个 `CliApp.Run` + 同一套 `CommandShell`，区别只有本进程天生带控制台且不跑 WinForms 消息循环；
- `hrmdump.exe` 由主引擎拉起并持有进程句柄，**正常退出**（托盘退出 / `exit --force` / `reboot`）显式收束；**异常崩溃**路径由它负责广播 `engine.crash` 给壳（前端 CrashOverlay 提供重启后端/退出/稍后），前后端同时阵亡时把现场写进 `%TEMP%\HeartRateMonitor-crash\`；
- 默认**单实例**：重复启动弹原生 TaskDialog 三选（Cancel / Kill / Jump，精确杀持有锁的实例不动并存 CLI）；`app.allow_multi_instance` / `--multi` 放行，`--autostart` 抢锁失败静默让位。

## 3. 目录结构（2026-09-07 起重心回 Windows，Shared 已并入）

```
C# Test/
├── build.ps1 build.bat           # 构建脚本（工作区根；--standalone/--releases/--debug/交互，含前端构建）
├── Windows/                      # 全部源码（#22：Windows 为纯净源码目录；GitHub 仓库根）
│   ├── App/                      # C# 主程序（HeartRateMonitor.exe）
│   │   ├── HeartRateMonitor.csproj / Program.cs / App.cs / Config.cs / Logger.cs / ProcessInfo.cs
│   │   ├── Core/AppHub.cs        # 共享业务核心：服务事件 + 全部命令（REST/WS/管道唯一传输层）
│   │   ├── Core/AppBoot.cs       # 公共启动序列（数据目录 data_location.txt 解析 / 配置加载 / 语言初始化）
│   │   ├── Core/SingleInstance.cs    # 单实例互斥（命名 Mutex）+ HTTP /api/activate 转到已运行实例
│   │   ├── Core/StartupOptions.cs    # 启动参数统一解析（--gui/--cli/--silent/--safemode/--reboot/…）
│   │   ├── Core/AutoStartManager.cs  # 登录自启三方式（计划任务 / HKCU Run / 启动文件夹）
│   │   ├── Core/CommandShell.cs / CommandShellApp.cs   # 文本命令唯一实现（CLI / Console Tab / Web 终端共用）
│   │   ├── Core/Trace.cs / Exporter.cs / MonitorStats.cs / LogText.cs / Text.cs
│   │   ├── Core/ComponentVersions.cs / ReleaseManifest.cs / GitHubProjectService.cs
│   │   ├── Db/HrmDb.cs RecordStore.cs       # 本地 SQLite（hrm.db）+ 多后端记录（records/ JSONL/CSV）
│   │   ├── Health/HealthService.cs   # 健康状态判定（心率 + OSC 姿态 → {HEALTH_STATUS}）+ 校准
│   │   ├── Web/WebServer.cs WebHost.cs RemoteAuth.cs  # 单端口 Web（本地+远程同一监听）/ 认证（用户·Session·来源分级）
│   │   ├── Osc/OscEngine.cs OscService.cs
│   │   ├── Ble/BleManager.cs DeviceRegistry.cs   # BLE 交互 / 标识符缓存·排序·别名·自动重连·RSSI 告警
│   │   ├── SysInfo/...           # 采集（注册表/wmic/PDH…，Windows 专属）
│   │   ├── Toolkit/VrcToolkitService.cs          # Toolkit 六工具后端
│   │   ├── Webhook/WebhookManager.cs · CLI/CliApp.cs SelfTest.cs MenuTui.cs CliUi.cs …
│   │   └── UI/                   # TrayHost / FloatWindowHost / FloatingWindow / SystemTheme / ThemeColors / ThemedMenuRenderer / MainForm(--winforms)
│   ├── Shells/WebView2Host/      # 内置 WebView2 壳（发布名 hrm-webui.exe，主前端宿主）
│   │   ├── Program.cs            # 入口：单实例守卫 → Splash → 探测端口 → 必要时带 --web 拉起后端 → 开窗
│   │   └── MainForm.cs           # 无边框窗体：WM_NCCALCSIZE/NCHITTEST + 圆角 + win.* 消息协议 + DWM 主题边框
│   ├── Shells/CliHost/           # 独立 CLI（发布名 hrmcli.exe，与 --cli 等价）
│   ├── Shells/DumpHost/          # 崩溃守护（发布名 hrmdump.exe）
│   ├── Shells/Electron/          # 备用 Electron 壳骨架（未启用）
│   ├── WebUI/                    # Vue 3 前端（主前端页面，Vite + vue-router + pinia + vue-i18n）
│   │   ├── build-webui.ps1       # 前端构建（临时无'#'根目录规避 Vite 限制）
│   │   ├── index.html vite.config.ts tsconfig.json package.json
│   │   └── src/                  # main（壳/浏览器入口分流）/ App(.vue|_web.vue) / router(.ts|_web.ts) / shell.ts（壳桥）
│   │                             #   layouts/{MainLayout,MainLayout_web,TitleBar_web,NavMenu,StatusBar} / views/（15 Tab + Toolkit，Settings 另有 _web 副本）
│   │                             #   components/ stores/ dashboard/ api/ styles/ lang.ts theme.ts prefs.ts
│   ├── Engine/                   # C OSC 引擎源码（winsock → osc_engine.dll）
│   ├── config/                   # config 模板（仅构建期参照；默认值已编译内置，产物不携带）
│   ├── image/                    # 图标/托盘/宣传图资源（构建与壳已接线）
│   ├── Skills/                   # 技能文档
│   ├── docs/                     # about.md / diff.md / Archives/（plan1~plan3.md 归档计划）/ images/（README 截图）
│   ├── VersionJson.cs            # 四组件共享 --version-json 探针（链接进四个工程）
│   ├── Release.json              # 发布元数据单一来源（图标/仓库/版本/构建时间/许可证/组件版本）
│   └── LICENSE
├── Built/                        # 产物（仓库根，不入库）
└── VRCX-2026.07.18/              # 参照源码（只读研究，不入库）
```

> 旧布局（根目录平铺 App/Engine/RustUi/Shells/WebUI/build.ps1…）与「Windows/ + Linux/ + Shared/」三段式切分均已归档（见 `docs/diff.md` 步骤 51 与后续条目）；Rust egui 前端（`hrm-ui.exe`）于第二十一轮整体移除；原 `plan.md`（Windows V3 计划）归档为 `Windows/docs/Archives/plan3.md`。

## 3.5 前端

### Web UI（Vue 3，唯一前端，Windows/WebUI/）

- **15 Tab**：总览(Overview) / 工作台(Dashboard) / 心率(HeartBeat) / 设备(Devices) / OSC 监视 / Pusher(发送+Webhook+自定义发送) / 硬件(HWInfo) / 头显(WIP) / API 服务(ApiServer) / Web(远程管理) / 设置(Settings) / 日志(Logs) / 命令行(Console) / 监测(Monitor) / 关于(About)；默认页 HeartBeat。技术栈 Vue 3 + vue-router(hash) + pinia + vue-i18n，设计语言 Clone VRCX（`styles/globals.css` 的 shadcn/zinc token）。
- **Toolkit dock**（侧栏左下角、折叠按钮上方，本地管理员可见）：点击向上弹出工具列表进入 `/toolkit?tool=<name>`，六个工具共用一页 CardGrid 分区 —— `config`（VRChat config.json 常用字段表格化编辑 + 严格 JSON 类型校验）、`logs`（VRChat 日志列表/读取/搜索）、`cache`（缓存目录占用分析与清理，支持 dry-run/确认短语）、`photos`（照片库并行索引 + 关键词搜索）、`game`（游戏时长/心率/硬件聚合统计）、`process`（VRChat 进程 CPU/内存快照）。
- i18n：**十种语言**（zh-TW / zh-CN / zh-HK / yue-HK / en / ja / es / ko / de / fr）。五张基础 JSON 由 `lang.ts` 表生成（`npm run locales`），zh-HK/yue-HK 为叠加覆盖字典，键集一致性由 `i18n.spec.ts` 锁定。引擎侧控制台/CLI/日志同理：`Core/Text.cs` 主表 + `Extra*` 补充字典、`Core/LogText.cs`（zh 中文行，其余回落英文）。
- **主题两维**（`src/theme.ts`）：明暗 `mode` = system/dark/light，配色 `palette` = system/default/forest/sunset/ocean/violet/custom（**custom = 纯色自定义**，强调色/背景/面板三色自选），两者首项都是「遵循系统」，各自独立下拉并持久化（另折叠回 `config.ui.theme` 向后兼容）。「遵循系统」深浅取 `matchMedia`，强调色由后端读注册表（`SystemTheme.cs`）随 `sys_theme` WS 事件即时跟随。
- 圆角/密度实时改写；全局动画开关 `ui.animations`；数值变化自变速动画（RollingNumber：roll/odometer/fade 三档）；心率曲线刷新补间动画。
- **布局偏好双存储**：`hrm-` / `hb-` 前缀的 localStorage 为主存储（列宽、导航折叠/宽度、卡片顺序/隐藏/占宽/折叠、曲线参数等），每次变更 800ms 防抖批量 `POST /api/prefs` **镜像到后端** config.json 的 `prefs` 段；启动时 `GET /api/prefs` 回填本地缺失键（本地已有值优先），跨设备/重装不丢。
- 布尔开关统一 `components/XSwitch.vue`；下拉统一 `XSelect.vue`（选项可带旗帜前缀）；对话框统一 `XDialog.vue`（密码不回显）；悬停提示统一 `v-bubble` 指令（600ms 延迟，全站唯一形态）。
- Debug Mode 可在设置页即时开关；关闭时壳同步关掉 DevTools。
- **壳 / 浏览器双入口**：`main.ts` 检测 `window.chrome.webview` 后分流 —— 壳用 `App_web.vue` + `router_web.ts` + `MainLayout_web.vue`（含自绘标题栏 `TitleBar_web.vue`、设置页 `Settings_web.vue`），浏览器用原 `App.vue` + `router.ts` + `MainLayout.vue`（窄屏抽屉 + 遮罩 + FAB；≤860px 底栏自动换行滚动）。可复用组件/页面/store 单份共享，只有有差异的才建 `_web` 副本。
- 壳桥 `src/shell.ts`：`postShell(cmd)` 发 `win.drag/min/max/close/exit/tray/fullscreen/corner/theme/debug/state` 等；`onShellCloseRequest()` 接宿主的 `win.close-request`。
- **关窗确认**：宿主拦下关闭 → 先查未保存更改（`dirty.ts` 登记表）→「保存并继续 / 放弃更改 / 取消」→「最小化到托盘 / 退出程序」；勾「不再询问我」写 `config.ui.closeAction`。关窗前若窗口半出屏先移回屏幕中心。
- 断连守护：`api/watchdog.ts`（30 × 100ms 探活）+ `OfflineOverlay.vue`（单层背景模糊，重连失败给可复制错误详情、退出程序、导出本地偏好）；崩溃链路 `CrashOverlay.vue`（重启后端/退出/稍后）。
- 设备页（Devices）：CSS Grid 表格化（表头/数据行共用 `grid-template-columns` 严格对齐），列宽拖拽持久化、右键连接菜单、设备详情弹窗（含迷你心率统计）；心率页设备列表为纵向列表 + 曲线区（主显示/平均/指定设备三数据源）。底栏设备胶囊：单击详情 / 双击连接、超长名 marquee 滚动、悬停弹出迷你卡（64px 心率曲线 + min/avg/max + 信号/RSSI/上报频率）。
- 页面均接真实后端：Overview/HeartBeat 定时 `sync()`；OSC 监视分「概览/数据」双子 Tab；Console 走 `POST /api/cli`（↑↓ 历史、Tab 补全、Ctrl+L 清屏，Console 页与 hrmcli 同源同一 `CommandShell`）；Logs 后端权威正则/等级过滤 + 导出；Monitor 纯 CSS 条形图 + 聚合导出。
- 实时数据走 WebSocket `/ws`（device_found / heart_rate / connected / disconnected / devices / scan / osc_status / osc_params / health_status / sysinfo / sys_theme / log / vrchat_status …）；`heart_rate` 事件带 `notifyHz/mainBpm/avg`，主显示、平均、每设备曲线分别维护历史。
- **远程第二前端（P6 单端口模型，见 §6 `remote`/`web` 段）**：与本地同一端口、同一份 UI；手机/平板访问 `http(s)://<本机IP>:<web.port>/webui/`，登录本机账号后使用（板块受角色白名单约束）。`RemoteLogin.vue` 支持强制改密与首启初始化；`views/Web.vue` 管理监听状态（绑定/来源分区）、LAN/WAN 开关（WAN 需强密码 + 不可绕过的风险确认弹窗）、用户库与会话。

### 内置 WebView2 壳（Windows/Shells/WebView2Host → hrm-webui.exe）
- 启动流程：单实例守卫（重复启动弹 TaskDialog Cancel/Kill/Jump）→ **透明 Splash**（Logo + 进度，按目标屏幕工作区与 DPI 居中，TransparencyKey 只留 Logo/文字/进度）→ 探测端口 → 未就绪则带 `--web` 拉起同目录 `HeartRateMonitor.exe` → 打开 `http://127.0.0.1:<port>/webui/`。
- **无系统标题栏**：`FormBorderStyle.None` + `CreateParams` 补回 `WS_THICKFRAME` + `WM_NCCALCSIZE` 抹平非客户区；页面内 `TitleBar_web.vue` 仿 macOS 交通灯（红/黄/绿），标题居中显示当前 BPM，空白区 `win.drag` 拖动、双击最大化。
- **自定义圆角**：Win11 22000+ 走 `DWMWA_WINDOW_CORNER_PREFERENCE` DWM 原生圆角；Win10 用窗口区域裁剪 + WebView 内缩/预置底色/强制重绘三招堵白边。半径 `CornerPx` 0~24 实时调整，存 exe 同目录 `webui-shell.json`。**只作用于窗口外框，与页面控件圆角（`ui.corner_radius`）完全独立**。
- 八向缩放（`WM_NCHITTEST`，宽高比夹 1.05~2.60）；DPI PerMonitorV2；F5 重载 / F11 全屏 / F12 开发者工具（仅 Debug Mode）；窗口尺寸/位置/缩放/圆角/主题底色持久化。
- **主题边框同步**（`win.theme{bg,dark}`）：页面取已渲染底色转 hex 发宿主，宿主同步 `BackColor` 与 DWM 暗色/边框/标题色；Win10 不支持边框色属性时退回停掉非客户区渲染。
- 外部链接交系统默认浏览器打开（`NavigationStarting`/`NewWindowRequested` 拦截，非 http(s) 不交 Shell）。
- 右键改程序内置菜单（壳已关默认菜单）；WebView 状态栏永不显示。

### IPC 命令总表（`Ipc/PipeServer.cs` → `Core/AppHub.cs`）

| 分类 | 命令 |
|---|---|
| 状态 | `status` `config` `settings` `cli{line}` `cli_help` `activate` `shutdown` |
| 设备 | `devices` `scan{action}` `connect{mac}` `disconnect{mac}` `save{mac,saved}` `block{mac}` `unblock{mac}` `batch{macs,action}` `rename{mac,alias}` `devices_config{...}` `autodetect{action}` |
| 心率/健康/记录 | `health{action:get/start/cancel}` `health_config{...}` `record{action}` `record_config{...}` `export{table,format,limit}` `monitor{hours,buckets}` `monitor_export{...}` |
| OSC | `osc_connect{connected}` `osc_config{...}` `osc_test{addr,value}` `osc_params` `osc_params_clear` |
| 硬件 | `hw` `hw_refresh` `hw_config{...}` `hw_var{action:override/unit/rename/custom,name,value,item}` |
| 日志/推送 | `logs{filter,limit,regex,levels[]}` `logs_clear` `logs_dump` `logs_export{filter,format,regex,levels[]}` `webhooks` `webhook{action,item,index}` |
| 对外接口 | `api_config{enabled,token,pushSysInfo,pushIntervalMs,webhookThrottleMs,sysInfoVars}` `heartbeat` `sysinfo{template}` |
| 窗口/服务 | `float_open{win}` `float_open_all` `float_close{win}` `float_close_all` `float_lock{locked}` `float_config{source,refreshMs,win,winSource}` `web_start` `web_stop` |

命令表的唯一实现在 `AppHub.Dispatch(cmd, req)`，`PipeServer`（命名管道）与 `WebServer` 的 WebSocket 下行命令共用同一张表（WS 侧另有按角色裁剪的白名单），两个传输层自身不含业务逻辑。

Web UI 的 REST 端点与上表一一对应（`/api/<名称>`），另有独立端点：`GET|POST /api/prefs`（前端视图偏好镜像）、`GET|POST /api/autostart`（登录自启状态/注册）、`GET /api/github` + `POST /api/github/refresh`（项目信息快照）、`GET|POST /api/vrchat` 与 `POST /api/vrchat/launch`（运行状态/启动）、`GET|POST /api/toolkit/*`（paths/config/logs/cache/photos/game-stats/process）、`GET|POST /api/remote/*`（me/login/logout/users/sessions/config，远程认证面）、`POST /api/shutdown`。日志侧 `GET /api/logs?filter=&limit=&regex=1&levels=` 与 `POST /api/logs/clear|dump|export`；OSC 参数聚合 `GET /api/osc/params`；文本命令 `POST /api/cli` 与 `GET /api/cli/help`；Monitor `GET /api/monitor` + `POST /api/monitor/export`。

### 对外 API（需 Web 服务已启动 —— 默认随程序启动）

| 端点 | 方法 | 说明 |
|---|---|---|
| `/heartbeat` | GET | 心率快照：`bpm/avg/connectedCount/devices[]/health/recording/app{version,startTime}/sysInfo.vars/ts`；`api.enabled=false` 时返回 403 |
| `/api/apiserver/config` | GET / POST | 读写 `api` 段；回读附 `varNames`（当前全部可用变量名） |
| `/api/sysinfo` | GET / POST | `template` 走 query 或 body，返回渲染后的 `text` + `sysInfo` 快照；未知变量按原样保留 |
| `/ws` | WebSocket | 上行推送 `{type, data, ts}`（含周期性 `sysinfo_vars`）；下行发 `{id, cmd, ...}` 可调用命令总表（按会话角色裁剪） |

- **鉴权**：`api.token` 非空时须带 `?token=`、`X-Api-Token` 头，或 WS 消息体 `token` 字段；**为空 = 不校验**。普通 Remote 用户读取不到 api token（角色屏蔽）。
- **系统信息回传（类似 OSC）**：`api.push_sys_info=true` 后按 `api.push_interval_ms` 周期向所有 WS/IPC 客户端推 `sysinfo_vars`，变量集受 `api.sys_info_vars` 白名单裁剪（空 = 全部）。
- **WebHook**：url / 请求头值 / body 三处都支持 `{变量}` 占位符（与 OSC 模板同一套系统信息变量），触发频率受 `api.webhook_throttle_ms` 节流。
- **安全边界**：Web 监听为单端口 `http(s)://+:{web.port}/`（绑定全部网卡，本地/远程共用）。是否需要登录**只看客户端来源分级**（见 §6 remote/web 段）：回环 = 本地管理员（无登录）；私网来源需 `remote.enabled` 且登录；公网来源额外需 `remote.wan_enabled`。**默认空 token 意味着可达来源都能读到全量系统信息**，对外暴露前必须设置 token。

## 4. C OSC 引擎 API（Windows/Engine/osc_engine.h）

| C 函数 | 说明 |
|---|---|
| `osc_encode_message(addr, args, argc, out, cap)` | OSC 编码（类型 `,sifhdbTFN`），返回字节数 |
| `osc_engine_send(ip, port, data, len)` | UDP 发送（内部持 socket，线程安全） |
| `osc_engine_send_text(ip, port, addr, text)` | 文本快速路径（模板格式化后直接推，标签 `,s`） |
| `osc_engine_send_chatbox(ip, port, addr, text, immediate, sound)` | `/chatbox/input` 专用（标签 `,sTF`）。`immediate` 必须为真，否则 VRChat 只把文本填进输入框、等用户按回车确认；`sound` 控制提示音 |
| `osc_decode_to_json(data, len, out, cap)` | 解码（消息/bundle）为 JSON 文本 |
| `osc_engine_start_receiver(port, cb, user)` / `osc_engine_stop_receiver()` | 接收线程，回调原始字节 |

## 5. 硬件信息获取（4 种标准方法）

| 方式 | 适用 | 说明 |
|---|---|---|
| 注册表 | 全部（主来源，快） | `...\Windows NT\CurrentVersion`、`HARDWARE\DESCRIPTION\System\BIOS` |
| `wmic` | 仅 Win10 及更早（按实际存在性检测） | `wmic xxx get ... /format:list`（Win11 已移除 wmic） |
| `Get-ComputerInfo` | 全部 | PowerShell，OS/BIOS/主板/内存 |
| `systeminfo` | 全部 | OS 版本/制造商/型号/总内存/网卡 |

实时指标（CPU/RAM/GPU/VRAM 占用、温度）优先用 **PDH P/Invoke**（`SysInfo/PdhCollector.cs`，`PDH_FMT_DOUBLE` 浮点，与任务管理器同源）：CPU 利用率、`% Processor Performance`（× 注册表 `~MHz` 得**实时睿频**）、进程/线程数、上下文切换、磁盘占用/读写/队列、内存可用/提交；PDH 不可用时逐指标回退 `PerformanceCounter`。温度走 `Thermal Zone Information`，GPU/VRAM 走 `GPU Engine` / `GPU Adapter Memory`（未接厂商 SDK，不伪造 GPU 温度）。另有**动态模板变量**：`CPU_USAGE_<名字|PID>`、`MEM_USAGE_<名字|PID>`（双采样差分÷逻辑核）、`USAGE_FILE_<文件|目录>`（占用 MB，5s 缓存）、每轮同步的规范键 `CPU_USAGE_VRCHAT` / `MEM_USAGE_VRCHAT`。采集节奏：fast（间隔可配，默认 1s）+ full（启动后台 + 手动，多方法补全，带超时降级）。

采集完成后由 `SysInfo/VarEngine.cs` 做变量层处理（顺序固定，保证覆写最终生效）：

| 阶段 | 内容 |
|---|---|
| 时间变量 | `TIME_LOCAL / TIME_UTC / TIME_ISO / TIME_UNIX / TIME_ZONE / TIME_ZONE_OFFSET / TIME_NTP / TIME_NTP_OFFSET_MS`（SNTP UDP 48 字节，往返中点抵消延迟，10 分钟刷新） |
| 自定义变量 | `expr` 四则运算（自写递归下降）/ `concat` 拼接 / `regex` 取第 1 捕获组 / `cmd` 命令行 stdout（缓存 5s，超时 3s） |
| 变量改名 | `Hw.Renames`：原名 → 新名（新名与原名同时可用） |
| 手动覆写 | `Hw.Overrides`：值本身支持 `{变量}` 与运算，优先于采集值 |

数值格式由 `Hw.UseFloat` / `Hw.Decimals` / `Hw.Round` 控制，单位由 `Hw.Units[变量名]` 追加。

## 6. 配置与数据目录

### 数据目录（DataDir）

- 默认 `%AppData%\HeartRateMonitor`；**exe 旁 `data_location.txt` 可重定向**（写 `__appdata__` = 回默认，或写程序目录/自定义路径；设置页 DataPathCard 提供预设，自定义路径同理由该文件落地）。首次切换执行**旧数据一次性迁移**。
- 归属数据目录的内容：`config.json`、`config_webhook.json`、`config_remote_users.json` / `config_remote_sessions.json`（远程用户库与会话，只存哈希）、`logs/`（含 `logs/auto/`、`trace.log`）、`exports/`、`hrm.db`（SQLite）、`records/`（JSONL/CSV 记录）、`github_cache.json`（项目信息缓存）。
- **产物不再携带默认 config / Release.json**：默认值编译内置，首次启动把用户配置写进数据目录；`config.json` 保存为临时文件 + `File.Replace` 原子替换并保留 `.bak`。

### config.json 主要段

| 段 | 关键项 |
|---|---|
| `app` | `debug`、`allow_multi_instance`、`auto_start_methods`（多选：task/run/startup）、`auto_start_silent`、`update_check`（默认开，含 `skipped_release_tag/name`） |
| `ui` | `lang`（十种）、`mode`/`palette`（明暗+配色两维，palette 含 custom 纯色）、`accent`/`bg`/`panel`、`corner_radius`、`density`、`animations`、`brand`（左上角标题 {} 模板）、`closeAction`（ask/exit/tray） |
| `logs` | `auto_dump_enabled`/`auto_dump_interval_min`/`dump_dir`、`trace_enabled`（诊断 Trace 开关，**下次启动生效**） |
| `devices` | `continuousScan`、`refreshThrottleMs`、`aliases`（MAC→别名）、`autoReconnect`、`reconnectIntervalSec`、`reconnectGiveUpMin`、`rssiWeakThreshold`、`history` |
| `health` | `restingBpm`/`restingSd`/`calibratedAt`（校准结果）、`sleepFactor`/`activeFactor`/`excitedFactor`、`spikeDelta`、`record` |
| `hw` | `intervalMs`、`useFloat`/`decimals`/`round`、`ntpServer`、`units`、`overrides`、`renames`、`custom[]` |
| `heart_rate.window` | `source`（主浮窗数据源，「平均」或 MAC）、`refreshMs`、`sources`（窗口标识→数据源）、各窗几何独立持久化 |
| `recording` | 总开关（默认开）+ 五类记录（avatar / vrchat 会话 / 设备连接 / 心率详情 / 硬件快照，各带保留天数，0 = 永久）+ 后端多选（db / jsonl / csv 可同时） |
| `api` | `enabled`（对外接口总开关）、`token`（访问令牌，**空 = 不校验**）、`push_sys_info` + `push_interval_ms`、`sys_info_vars`（回传白名单，空 = 全部）、`webhook_throttle_ms` |
| `web` | `port`（默认 **9460**，本地/远程单端口）、`scheme`（http/https）、`certificate_thumbprint`（HTTPS 证书指纹，见下）、`require_https_for_remote`（默认 true：非回环来源必须走 HTTPS） |
| `remote` | `enabled`（**LAN 开关**：私网来源可登录）、`wan_enabled`（**WAN 开关**：公网来源，需 admin 强密码 + 风险确认）、`idle_minutes`（会话闲置过期） |
| `prefs` | 前端视图偏好镜像（`hrm-*`/`hb-*` 键值对，与 localStorage 双写，见 §3.5） |

### 远程访问模型（P6：单端口 + 来源分级）

- **单监听** `http(s)://+:{web.port}/`（默认 9460）：本地前端与远程第二前端共用同一端口与同一份 UI；绑定全部网卡，非管理员被 URL ACL 拒绝时回退 127.0.0.1 并提示 `netsh http add urlacl`。**旧的 `remote.host` / `remote.port`(9461) / `remote.loopback_only` 字段已删除**，旧配置静默迁移（`loopbackOnly=true` → `enabled=false`）。
- **来源分级** `SourceZone`（IPv4-mapped IPv6 归一化后判定）：`loopback`（回环）→ **隐式本地管理员，无登录**；`private`（RFC1918 / 链路本地 / IPv6 ULA）→ 需 `remote.enabled` + 登录；`public` → 额外需 `remote.wan_enabled`。拒绝来源写审计日志（`log.web.source_reject`）。**不信任任何代理头**（Forwarded / X-Forwarded-For；未来支持反代需先配置 trusted proxies）。
- **WAN 门**：开启前校验 `WanReady`（所有 admin 均为强密码 ≥8 位含字母数字且非默认密码）+ 前端不可绕过的风险确认弹窗；改密成功后旧 admin 会话失效。`admin/admin` 默认账号已删除：空用户库合法，首次启用 Remote 走初始化管理员流程；默认密码账号标记 `mustChangePassword` 强制改密。
- **权限**：角色 admin/user；REST `AdminOnlyPrefixes` + `UserCapablePrefixes` 能力白名单与 WS `UserWsCommands` 白名单双层拦截；普通用户读不到 api token；`hw_var` 自定义命令、日志清理、设置、关机等始终 admin。Toolkit dock 不进远程 user 白名单（仅本地/远程 admin 可用）。
- **会话**：用户库 PBKDF2-SHA256、Session = HttpOnly Cookie（`Max-Age` + `SameSite=Lax`）+ 指纹只绑 UA（不符拒绝本次、计满 10 次销毁）、闲置过期、可列表踢除；会话表落盘 `config_remote_sessions.json`（只有哈希，重启不掉登录）。登录成败 / WAN 开关 / 拒绝来源均写审计日志。
- **HTTPS**：`web.scheme=https` + `web.certificate_thumbprint`（CurrentUser/My 或 LocalMachine/My 中带私钥的证书）；HTTP.sys SSL 绑定需管理员预先配置 `netsh http add sslcert ipport=0.0.0.0:<port> certhash=<指纹> appid={...}`。`require_https_for_remote=true`（默认）时非回环且非加密连接直接拒绝。**WAN 目前为明文 HTTP 监听**：生产公网暴露必须由受信反向代理/TLS 或 VPN 提供传输加密。

### 前端本地状态

壳窗口状态存 exe 同目录 `webui-shell.json`（尺寸 DIP/位置/最大化/缩放/圆角/主题底色）；Web UI 布局偏好存 localStorage（`hrm-` / `hb-` 前缀）并经 `/api/prefs` 镜像到后端（见 §3.5），断连面板可一键导出。

## 7. 构建与运行模式

### 构建（入口 = 工作区根 `build.ps1`；`Windows/Release.json` 为发布元数据单一来源）

`build.ps1 [--standalone | --releases | --debug] [-SkipWeb]`（`build.bat` 为兼容入口）；无参则交互询问。
- **工具链依赖**：gcc（MinGW，编译 `Windows/Engine`）→ Node.js + npm（构建 `Windows/WebUI`，`-SkipWeb` 可跳过）→ .NET 10 SDK（publish 四个 C# 工程）。
- 依次构建：C 引擎（gcc）→ Web 前端（vite）→ C# 主程序（dotnet publish，`HeartRateMonitor.exe`）→ WebView2 壳（`hrm-webui.exe`）→ CliHost（`hrmcli.exe`）→ DumpHost（`hrmdump.exe`）。
- **Release.json 单一来源**：`Windows/Release.json` 是唯一发布元数据编辑入口 —— 仓库/作者/`release_name`/`release_version`/`build_target`(x64)/`asset_pattern`/许可证/`icons`（engine/webui/cli/dump/tray 按角色）/`components`（四组件版本）。构建校验必填字段，把声明版本以 `-p:Version` 注入四个 EXE，把实际 UTC 构建时间（`release_build_time_utc=auto`）写入 AssemblyMetadata，并把清单**作为嵌入资源编译进程序集**（运行时嵌入优先、exe 旁文件兜底）；构建后读四个 EXE 的 FileVersion 与清单交叉校验。产物**不再复制** config/config_webhook/Release.json（默认值已编译内置）。
- `--standalone`：自包含免安装，**单文件**（PublishSingleFile，自带 .NET 运行时）+ `osc_engine.dll` + `hrm-webui.exe` + `hrmcli.exe` + `hrmdump.exe` + `webui/` 前端目录 + `image/`；
- `--releases`：框架依赖（需安装 .NET 10 运行时）+ 单文件，同上；release 模式按 `asset_pattern` 产出 `HeartRateMonitor-*-x64.zip` + SHA-256 sidecar；
- `--debug`：Debug 配置 + 控制台 + 详细日志，保留多文件布局。
- 产物统一输出到仓库根 `Built/<分支>-<时间戳>/`；构建末尾打印摘要（config/webui/四组件/image 是否就位）。**不要并行跑两个构建**（共享 obj/ 可能产出残缺产物；构建入口按约定不加互斥锁）。

### 运行模式

- 默认双击 → **内置 WebView2 界面**（`hrm-webui.exe`：Splash → 无系统顶栏 + 仿 macOS 标题栏，15 Tab + Toolkit dock）；Web 服务随之启动；引擎常驻系统托盘；
- `--silent` 静默启动（引擎 + 托盘 + 必要服务，不拉前端；Web 服务仍以 openUi:false 启动）；`--minimized` 最小化启动；默认 GUI，**仅显式 `--cli` 进 CLI**（不再按父进程猜测）；
- **托盘右键菜单**（`UI/TrayHost.cs` + `ThemedMenuRenderer.cs`）：跟随前端主题配色与圆角，菜单项含当前心率（只读）/ 打开界面 / 扫描 / 记录 / OSC / 悬浮窗 / Web 服务 / 退出，二元项文字随状态刷新；
- 浏览器访问同一份页面：`http://127.0.0.1:<web.port>/webui/`；远程第二前端（手机/平板）`http(s)://<本机IP>:<web.port>/webui/`，按 §6 来源分级登录；
- `hrmcli.exe` / `HeartRateMonitor.exe --cli`：**带参一次性执行**（`hrmcli status`）、`--shell` 或重定向环境文本 REPL、**无参交互终端进菜单 TUI**（`MenuTui.cs`，TestDisk 风格：alternate screen、菜单栈、上下选择/确认页/状态栏，异常与 Ctrl+C 恢复终端状态；启动打印 ASCII 标题 + 嵌入发布信息 + GitHub 缓存摘要）；命令约 39 条（`status/about/devices/score/scan/detect/connect/disconnect/save/block/rename/devcfg/bpm/health/hcfg/record/export/monitor/hw/info/var/hwcfg/sysinfo/osc/oscfg/params/send/webhook/api/web/remote/autostart/ui/float/logs/logcfg/config/crash/reboot/clear` + `help/exit/selftest`），与 Web Console Tab 同一 `CommandShell`；密码类管理在 CLI 明确拒绝（防明文进命令历史）；
- `HeartRateMonitor.exe --winforms` → 回退到旧 WinForms 主窗体；
- 重复启动默认弹 TaskDialog 三选（Cancel/Kill/Jump）转到已运行实例，`--multi` 或 `app.allow_multi_instance` 放行，`--autostart` 静默让位；
- **开机自启**三种注册方式（计划任务 / HKCU Run / 启动文件夹快捷方式，读取实际系统注册状态含陈旧检测），可搭配静默启动；
- **更新检查**（默认开）：启动后台拉 GitHub latest release（`GitHubProjectService`，ETag + 数据目录缓存、限流/离线回退），latest tag 与 release name 均不同于清单时弹原生 TaskDialog（打开发布页 / 按 `asset_pattern + build_target` 精确下载 / 复制链接 / 跳过此版本 / 关闭），每进程至多提示一次；
- **四组件版本校验**：任一组件以 `--version-json` 探针上报（5s 超时、验证组件身份）；不一致时 GUI 弹 TaskDialog（逐组件详情 + 继续确认），CLI 打印明细表并按退出码终止；
- `selftest`：模拟真实用户自检（配置/引擎/OSC/BLE 扫描连接心率/推送/硬件），约束最长单次阻塞操作 ≤ 250ms。注意是 CLI 内交互命令，脚本化跑法：`@("selftest","exit") | & .\HeartRateMonitor.exe --cli`（先设 `$OutputEncoding` 为 ASCII 避免 BOM 被 GBK 解码）。

### 安全模式（Safe Mode）

- `--safemode` 启动：**暂停**自动连接、自动检测、自动重连、OSC 推送、系统信息采集、自动推送等一切自动化行为，只保留手动操作，用于排查问题；
- 入口：设置页「安全模式」按钮（确认后异步重启引擎并传 `--safemode-confirmed`），或前端**三连 R 应急手势**；激活时顶部常驻警示条 + 「正常重启退出」；
- 安全模式下 `remote.wan_enabled` 等远程暴露配置同样不生效（`effectiveWanEnabled` 强制 false）。

### Toolkit 现状（P10，`/toolkit` 页 + `Toolkit/VrcToolkitService.cs`）

六个工具均已落地：**config**（VRChat config.json 常用字段表格化编辑，严格 JSON 类型校验 + 路径提示）、**logs**（VRChat 日志列表/读取/关键字搜索）、**cache**（缓存目录占用分析 + 分区统计 + 清理，dry-run 预览与确认短语防误删）、**photos**（VRChat 相册并行索引，用户指定并发数，索引入数据库后关键词搜索，含 XMP 元数据）、**game**（游戏时长/心率/硬件数据聚合统计与图表）、**process**（VRChat 运行时 CPU/内存快照）。REST 面为 `GET|POST /api/toolkit/*`（admin-only）。

### Trigger（尚未实现）

事件触发器（Phase P9 规划中）：HRMTRIGGER 版本化语法 + 正式 Grammar、条件（心率/健康/自定义 STATUS/OSC 参数，支持边沿/持续时间/冷却/迟滞）与动作（OSC/Webhook，`invert` 自动取反、退出时取反/恢复/保持三策略）、安全分级开关（默认全关）、Trigger Tab 与可视化编辑器。**当前版本无此功能，也无对应 Tab**；自定义心率 STATUS 分段（Phase P8）同样尚未实现。

### 诊断 Trace（运行时开关）

用于定位 UI 卡顿/命令超时；关闭时每个调用点只做一次 bool 判断，热路径开销可忽略：

| 侧 | 实现 | 输出文件 | 说明 |
|---|---|---|---|
| C# | `Core/Trace.cs` → `HrmTrace.Event/Perf` | `<DataDir>/logs/trace.log` | 由 `App.TraceEnabled` 控制（配置 `logs.trace_enabled` 或参数 `--trace`，**启动时快照，重启后生效**）；同时以 TRACE 等级进入应用内日志，可在日志页筛选；行格式 `序号 HH:mm:ss.fff [T线程] TAG 消息` |

- `HrmTrace.Perf(tag, elapsedMs, warnMs = 50)`：仅当 `elapsedMs >= warnMs` 时才写一条 `PERF tag Xms`，平时不产生噪声。
- 实测（2026-09-11）：静默模式 6 秒运行 CPU 3.8s（含启动 + SysInfo + BLE 扫描）、内存 227MB、无残留进程；trace.log 仅 8 条打点，PERF 记录集中在 sysinfo 一次性冷启动收集。
