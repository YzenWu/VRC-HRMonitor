# HeartRateMonitor V3 前端重构计划（Release，plan.md）

> **归档说明（2026-09-04，平台切分轮）**：V3 计划全部落地（Phase A1~A6 / Phase Ext / Phase Web / Phase Theme 均已完成），
> 本文件归档至 `Shared/docs/Archives/plan3.md`。（原「下一阶段 = Linux 适配」的规划已于第二十三轮撤销：VRChat 是 Windows 原生游戏，Linux 适配无实际收益。）

> 目标：以 V2 已完成的「共享核心 AppHub + C 引擎 + 双前端」为基座，**克隆开源项目 VRCX（工作区 `VRCX-2026.07.18/`，只读参照）的设计风格与前端实现逻辑**：
> ① 把 WebUI（SolidJS + 手写 CSS）整体重构为 **Vue 3 + TypeScript** 单页应用（工程分层、组件库、i18n、主题全部对齐 VRCX 范式）；
> ② 仿 VRCX 的宿主架构构建 **内嵌浏览器壳**（默认 **Microsoft Edge WebView2**，备选 Electron；Cef 仅评估不默认），壳加载同一 Vue 前端，构成 Release 主 UI；
> ③ 后端 REST/WS 契约与 C# 业务代码**保持不动**，Rust egui 前端**保留**供后期调试（V3 默认仍可启动）。
> 本文档是执行蓝图，随实现推进更新；每次改动记录见 `diff.md`。**本文件在 git 版本控制之外**（已加入 `.gitignore`，不入库）。

V2 旧计划已归档到 `Archives/plan2.md`（已在库）。改动启动前已在 `e:\WorkSpace` 建立精简快照 `C# Test Bak_20260904_021212/`；项目已 `git init`，基线提交 `993dad0`（89 文件）。

---

## 0. 项目核心

一句话：**把戴上 BLE 心率带后的人体状态，推送到 VRChat 的聊天框与相关 OSC 参数**，并围绕它提供监控/记录/分析/导出。

恒定的设计原则：

1. **性能热路径在 C 引擎**（OSC 编解码/发送/接收），C# 只做业务编排；V3 前端只做呈现与交互。
2. **业务集中在 `AppHub` 一处**，前端统一消费 REST（`/api/*`）+ WS（`/ws`）；V3 不改契约、只换消费端与壳。
3. **前后端解耦开发**：Vue 前端可 `vite dev`（proxy→8228）独立开发；壳只是"浏览器"加载同一 URL。
4. **Clone VRCX**：设计风格（shadcn-vue 组件语言 + CSS 变量主题）、工程分层（api/stores/components/views/localization）、宿主壳（内嵌浏览器）都对齐 VRCX。
5. **两套 UI 并存**：Rust egui 保留为"调试 UI"（常驻可用）；Vue+壳为 Release 主 UI。
6. **Release 取向**：单目录产物、图标资源入 `image/`、语言主题开箱即用、无调试残留。

---

## 1. VRCX 前端方案分析（调研结论，参考源 `VRCX-2026.07.18/`）

### 1.1 技术底座

| 层 | VRCX 采用 | V3 取舍 |
|---|---|---|
| 框架 | Vue 3.5（SFC）+ Vite 8 + vue-router 5 | 同：Vue 3 + TS + Vite（`vue-tsc`） |
| 状态 | Pinia 3（`stores/` 按域，`settings/` 再分片） | 同：Pinia，域 store |
| 服务端状态 | TanStack Vue Query（`queries/`） | **不用**（本地 API 简单，避免过度设计） |
| 样式 | Tailwind CSS v4 + CSS 变量（`styles/globals.css`+`themes/*.css`） | **风格克隆**：沿用 V2 CSS 变量主题体系（五态），组件实现克隆 shadcn 视觉（不整套引 Tailwind） |
| 组件 | 自移植 shadcn-vue：`components/ui/*`（button/card/dialog/select/switch/tabs/table/sonner/sidebar/tooltip…，reka-ui+CVA+lucide） | **克隆**：自建等价轻组件（Button/Card/Input/Select/Switch/Tabs/Table/Dialog/Toast/Sidebar/…），lucide 图标 |
| 字体 | Inter + Noto SC/TC/JP/KR 可变字体内嵌 | 不内嵌；系统栈（Segoe UI + Microsoft YaHei） |
| 图表 | echarts（`stores/charts.js`） | 先 `<canvas>` 自绘（Monitor/心率曲线），需求扩张再引 echarts |
| 拖拽 | dnd-kit（Dashboard 面板） | 可选；Dashboard 试点时评估 |
| i18n | vue-i18n + `localization/*.json`（15 语言） | 同：vue-i18n + 5 语言 JSON（由 `lang.ts` 一次性生成，同 key 同文案） |
| 测试 | vitest + @vue/test-utils（遍布 `__tests__`） | 同：vitest 保障（i18n 快照/store/组件冒烟） |

### 1.2 目录分层（V3 目标结构，放 `WebUI/`）

```
src/
├─ api/           # 后端 REST/WS 封装（每域一模块；契约同 V2 api.ts）
├─ stores/        # Pinia：app/devices/osc/heartbeat/hw/logs/monitor/record/health/float/web
├─ composables/   # 可复用逻辑（usePolling/useWsSubscribe/useIntervalSync…）
├─ components/    # ui/（设计系统原语）+ 业务组件（BpmHero/DeviceRow/ParamGrid…）+ dialogs/
├─ views/         # 12 路由视图 + Layout（MainLayout/Sidebar）
├─ locales/       # zh-TW/zh-CN/en/ja/es.json
├─ styles/        # theme.css（五态 CSS 变量，承接 V2）+ 组件覆盖层
├─ types/         # 领域模型（承接 V2 types.ts）
└─ main.ts/router.ts/app.ts
```

### 1.3 VRCX 可借鉴形态 → 我们的映射

1. Sidebar 导航（可折叠/分组）→ 12 Tab 侧栏（顺序同本地，可固定常用）。
2. Dashboard 面板化工作台 → Overview/HeartBeat 卡片自由摆放试点。
3. 域 store 自治（WS 事件自订阅）→ 单页单 store，WS 消息分发器。
4. **宿主壳**（VRCX：Dotnet/Cef + src-electron 双壳共享同一前端）→ **WebView2 主壳 + Electron 备壳**共享同一 Vue 前端，是本计划核心（§2.1）。

---

## 2. 技术决策（本次新增）

| 关注点 | 决策 | 理由 |
|---|---|---|
| UI 并存 | **Rust egui 保留**（用户调试常用，V3 全程可用）；Vue+壳为新主 UI | 用户指令 1；双前端共用后端，互不干扰 |
| 前端目录 | **`WebUI/` 原地换 Vue 3 + TS**（SolidJS 实现移除，git 历史可回） | `build-webui.ps1`/`/webui/` 静态目录与后端复制链路不变 |
| 状态 | Pinia 按域；WS 事件分发到各 store | 12 域隔离，避免 V2 单大 store |
| 组件 | 自建轻组件（克隆 shadcn 视觉 + V2 CSS 变量主题） | 组件层收敛样式；V2 `styles.css` 卡片/表格类拆入组件 |
| i18n | vue-i18n；脚本由 `lang.ts` 表生成 5 个 JSON（232+ 键） | 零手抄差异，开发期加 key 走回写脚本 |
| 图表 | 先 canvas 自绘曲线/条形图 | 避免 echarts 体积；Monitor/HeartBeat 共用组件 |
| 测试 | vitest：i18n 快照 + store + 组件冒烟 | Release 最低保障 |
| 壳架构 | 见 §2.1 矩阵：**WebView2 默认**、Electron 备选、Cef 评估不默认 | 用户指令 4（msedgewebview2） |
| 主程序集成 | V3 主 UI 由壳窗口承担；壳启动时确保后端 `--web` 在跑（先探测 8228，再拉起 `HeartRateMonitor --web`） | 单入口体验 |
| 参照源码 | `VRCX-2026.07.18/` 已入 `.gitignore` | 只读研究，不入库 |

### 2.1 壳架构矩阵（Clone VRCX 宿主模型）

| 壳 | 技术 | 与 VRCX 对应 | 取舍 | 状态 |
|---|---|---|---|---|
| **Shell-WebView2（默认）** | C# WinForms + `Microsoft.Web.WebView2`（Edge Runtime，Win10/11 普遍预装） | ~Dotnet/Cef（内置浏览器） | 包体小（复用系统 Edge）、官方维护、可直接写 C# 与窗体/托盘/悬浮窗联动 | **先建** |
| Shell-Electron | Electron + BrowserWindow（加载同一 URL） | src-electron | 跨平台/独立 Chromium；体积 ~200MB；需 npm install electron | 骨架后建 |
| Shell-Cef | CefSharp（内置 Chromium） | Dotnet/Cef 详细实现 | 仅当需要 Offscreen/Overlay/进程级抓帧或 WebView2 不满足时评估 | **不默认** |

壳职责边界：**纯加载** `http://127.0.0.1:8228/webui/`（或打包后的 `webui/index.html`），不做业务；C# 联动（托盘/悬浮窗/系统 API）留在主程序。壳与主程序通过「探测端口 → 若无则带 `--web` 拉起主程序」协作。

---

## 3. 目标：V3 前端结构（12 路由 + 壳）

| 路由 | 视图 | 说明 |
|---|---|---|
| `/overview` | Overview | 面板式总览试点 |
| `/heartbeat` | HeartBeat（默认） | 心率大卡 + canvas 曲线 + 设备行 + 健康 + 悬浮窗 + 导出 |
| `/devices` / `/osc` / `/pusher` / `/hwinfo` / `/headset` / `/apiserver` | Devices / OscMonitor / Pusher / Hardware / HeadSet(WIP) / ApiServer | 语义与 V2 一致，纯重构 |
| `/settings` | Settings | 外观（语言/主题/圆角/密度）+ 高级入口 |
| `/logs` / `/console` / `/monitor` | Logs / Console / Monitor | 语义与 V2 一致 |
| `/dashboard` | Dashboard | 拖拽面板试点，成功回填 Overview/HeartBeat |

壳端能力：窗口标题/图标（`image/` 资源）、可缩放、深色标题栏、`F12` 开发者工具（仅 debug）、前端可达时直连。

---

## 4. 起步（Phase 编排，随 diff.md 记录）

**[x] Phase A0 — 归档与备份（已完成，diff.md 步骤 40）**
- [x] V2 `plan.md` → `Archives/plan2.md`；新 `plan.md`（V3）**不入库**。
- [x] 精简备份 + `git init` + 基线 `993dad0`；`image/` 创建并接入 `build.ps1`。
- [x] Rust 设置页新增「启动测试前端」按钮（`settings.testfront` key ×5 语言，同步 `lang.ts`；`web_start` + 优先拉起 `hrm-webui.exe`，缺失回退浏览器）。

**[x] Phase A1 — Vue 工程搭建 + 骨架 + 组件层（WebUI/ 原地迁移；diff.md 步骤 42）**
- [x] 换 `package.json`（vue3/vite/vue-router/pinia/vue-i18n/lucide）+ `vite.config.ts`（dev proxy→8228）+ TS 配置。
- [x] `MainLayout` + `Sidebar`（12 项）；路由表；`styles/globals.css` 承接 V2 五态并克隆 shadcn zinc token；轻组件层 `.x-*`。
- [x] `locales/*.json` 由 `lang.ts` 脚本生成（234 键 × 5 语言）。
- [x] 快照测试锁 key 集（Phase A6 已引入 vitest：`src/i18n.spec.ts` 校验五语言 JSON 与 `lang.ts` 键集一致、无空值）。

**[x] Phase A2 — 数据层与核心页（diff.md 步骤 44、45）**
- [x] `api/`（REST + WS 总线）、`stores/`（app/osc/ui，WS 分发）。
- [x] 12 页全部交付并接真实后端：HeartBeat → Devices → OSC → Pusher → Hardware → Monitor → Overview → Logs → Console → Settings → ApiServer → HeadSet(WIP)。
- [x] 契约复检与修复：WS `{type,data}` 信封解包、7 处 REST 路径、`oscConnect{connected}`、`oscTest{ip,port,address,text}`、`floatOpen __main__`、OSC 端口字段 string、`health.statusKey`、`wsConnected` 响应式、i18n `messageCompiler`（详见 diff.md 步骤 45）。
- [x] 每页验收：与 V2 WebUI 同功能、空/错/载三态、WS 实时、语言主题即时生效。
- [ ] `composables/`（usePolling/useWsSubscribe）尚未抽出——当前逻辑集中在 store，等出现第三处复用再抽。

**[x] Phase A3 — Shell-WebView2（主壳；diff.md 步骤 41、43）**
- [x] `Shells/WebView2Host/`（AssemblyName `hrm-webui`）：WinForms + WebView2，加载 `/webui/`；启动探测 8228 / 必要时以 `--web` 拉起主程序；`image/app.ico` 图标；F12 devtools、F5 重载、失败错误页。
- [x] 与 `build.ps1` 集成（3e 段）：壳随产物分发；`--web` 启动链改由 `ProcessInfo.LaunchWebUi` 打开壳窗口而非系统浏览器。
- [x] 托盘图标与主程序托盘联动（托盘仍由主程序独占，但壳已可被托盘唤回：壳单实例事件 `Local\hrm-webui-show` + `AppHub.Activate()` 重拉壳；托盘菜单跟随前端主题，详见 Phase Theme）。

**[~] Phase A4 — Shell-Electron（备壳）**
- [x] `Shells/Electron/`：`package.json` + `main.js`（BrowserWindow + contextIsolation + 端口探测 + 错误页）骨架。
- [ ] `npm install` 与打包脚本（体积原因暂缓，需要跨平台或独立 Chromium 时再启用）。

**[x] Phase A5 — 视觉收敛 + Dashboard + 图表（diff.md 步骤 46、47）**
- [x] canvas 曲线（`BpmChart`，支持平滑度/窗口点数 props）/条形图（`BarChart`）组件统一，不引 echarts。
- [x] 心率页高级设置补齐：健康校准/参数、悬浮窗数据源与刷新、数据导出、曲线平滑/窗口。
- [x] 设备页高级设置补齐：设备策略（持续扫描/节流/重连/弱信号阈值）。
- [x] Dashboard 拖拽面板（`dashboard/panelRegistry.ts` 8 面板 + HTML5 原生拖放排序 + 隐藏/重置 + localStorage 持久化；侧栏 13 项）。
- [x] 全主题走查：脚本化（Edge headless + CDP）采样 5 主题 × 13 路由 = 65 组，WCAG 对比度 7 组配对全过；13 路由内容量核对无空页。
- [x] lucide 图标统一（无 img/svg/emoji）；`index.html` 内联 SVG favicon；最后两处硬编码中文入 i18n（`nav.collapse`/`nav.expand`）→ 249 keys。
- [x] 硬件页覆写/自定义变量面板（`hwVar` 的 override/unit/rename/custom + NTP 预设 + 留空即清除）。
- [ ] `image/app.ico` 仍为空目录（图标资源待补；壳与 build.ps1 已就绪，找不到就静默跳过）。

**[x] Phase A6 — 测试与 Release**
- [x] `vue-tsc` 0 error；`build-webui.ps1`；`build.ps1 -Mode debug` 全链路通过 + 端到端冒烟（REST 20/20、WS 事件、真机心率、曲线像素、12 路由 DOM）。
- [x] vitest：`test-webui.ps1`（robocopy 到 `%TEMP%\hrm-webui-test` 规避 `#` 路径）+ 5 个 spec 共 **29 个测试**全绿（i18n 键集/panelRegistry、theme 五态、app store WS 事件、BpmChart smooth/points、Dashboard 拖放排序）。
- [x] `build.ps1 -Mode releases|standalone` 复验 + 独立目录运行冒烟：各 27 项（REST 20 + 静态 4 + 资源 2 + WS 1）全过，壳窗口标题正常，13 路由 DOM 有内容；`cargo build --release` 通过。

**[x] Phase Ext - Extra 剩余功能实现**
- [x] 网页和内置WebView实际上走两套不同的路径。分为LocalWeb和ExtWeb。
      LocalWeb不会开放任何外部端口，直接走FS + 内部端口来进行浏览和控制。
      ExtWeb模式可使用--web参数强制启动或在LocalWeb设置中单独启动。
      （已由三方工具实现，用户确认打勾）

- [x] 内置WebView的默认窗口宽度必须能容下所有内容，包括标题栏、菜单栏、工具栏等。限制最小宽高与比例。允许F11全屏运行。
      用户的缩放大小是持久化的 下次启动会自动恢复。
      （默认 1440×900 DIP / 最小 1024×640 DIP / 宽高比 1.05–2.60；F11 全屏；尺寸+Zoom 存 exe 同目录 webui-shell.json）

- [x] 为前端设计CSS动画，包括侧边栏滑入滑出、扫描到新设备等。
      （侧栏文字 max-width 收缩 + 窄屏 translateX 抽屉、路由 out-in 淡入、设备行 TransitionGroup；reduced-motion 全关）

- [x] 设备连接优化
      当前点击连接设备后 设备的名称会被MAC覆盖。当某个设备首次广播了自己的设备名 立即将它的名字缓存到内存并在稍后保持以缓存的名字显示(除非发现名字有变动或手动重启了一轮新扫描)。
      当前点击连接设备后 不会显示状态。修改为当点击连接设备后，状态显示为"连接中"。
      修改设备的状态指示色块。当前为未连接设备空心圆，已连接设备实心绿。修改为:
                                                                  未连接设备 - 空心圆
                                                                  连接中设备 - 实心圆
                                                                  已连接设备 - 实心绿
                                                                  已连接且信号弱的设备 - 实心橙
                                                                  信号极弱的设备 - 实心红
      修改设备名称逻辑。按之前的排序算法(品牌名、关键词匹配)修改设备的显示名称。如"HUAWEI Band 9"中的"HUAWEI"和"Band"应该被加粗斜体显示。
      添加自动检测功能，批量按权重依次连接列表中的设备。排除拒绝连接的设备，排除找不到心率特征的设备，如果发现匹配的设备(报告了心率特征)就自动保持连接(不测试广播自身为智能家居、音频外设等类型的设备)。
      为设备列表添加图标显示。根据设备名称和设备自身上报的类型进行图标显示。
      添加自动连接功能，连接上次连接的设备。

- [x] 新增日志等级 "Trace" 用于诊断难以发现的性能问题。仅在设置中开启并重启后生效。
      （去掉 HrmTrace 的 [Conditional("DEBUG")] 改运行时开关；App.TraceEnabled 启动快照；日志页 TRACE 等级 + 开关 + 重启提示）

- [x] 修改OSC页的内容。OSC页本意是用于列出玩家当前所在的世界、玩家所在的World Transform、玩家使用的Avatar等信息。把OSC修改为两个子Tab(在顶部切换)，分别为概览和数据。默认打开概览，以分区卡片的形式显示当前数据。

- [x] 让前端适配手机、平板等多端UI方便远程调试与控制。
      （860px 抽屉断点 + 560px 手机收紧；壳 in-shell 例外；headless 实测 420/820 宽无横向溢出）

- [x] 当后端因为各种原因断开连接时 前端使用半透明遮罩覆盖全局并在中心播放加载动画尝试重连（同时显示错误信息） 每次必须在100ms内完成 尝试30次。若30次无一成功，弹出三角错误提示并显示错误信息(点击可复制)并显示两个按钮控件: 关闭标签页(本地前端不显示)和配置导出(可将前端内未来得及落盘的配置导出到本地文件)。

- [x] 允许更改心率曲线的数据来源、显示模式(平滑度、折线图模式)。主心率显示器也一样。

- [x] 左下角心率显示器不再显示主心率 而是使用设备细分。不限制设备连接数，蓝牙协议与硬件最大支持数即为上限。如果超出底栏显示范围就用滚动的方式展示。

- [x] 默认不允许多个instance同时运行，当检测到重复运行自动转到旧的Instance.
      （命名 Mutex + SetForegroundWindow，只剩托盘时走 IPC activate；config.app.allow_multi_instance / --multi 放行）

- [x] 在Build脚本中添加多个Release模式，为所有Build模式添加一个二级菜单，分为Default和Standalone。移除一级菜单中的Standalone。Standalone只允许产出一个独立.exe文件，在缓存或内存中临时释放并把配置释放到exe同目录下实现运行。
      如果以Release模式构建，启动后不应该显示任何控制台。
      （已由三方工具实现，用户确认打勾）

- [x] (计划)新建Images目录用于存放icon等文件。
      （已建 `image/`，build.ps1 3d 段随产物复制）

**[x] Phase Web - WebView 转正为主前端（第十九轮）**
- [x] EGUI 降为回退备用前端，WebView 为主前端；内置 WebView 不再显示 Windows 默认风格顶栏，改仿 Mac 设计 + 圆角边框（不影响控件，px 可配）。
      （`config.app.frontend=webview|egui` + `--egui/--webview`；`ProcessInfo.LaunchFrontend()` 统一入口，壳/服务不可用自动回退 EGUI；托盘加「打开 Web 界面 / 打开 EGUI 界面」）
      （宿主：`FormBorderStyle.None` + `CreateParams` 补 `WS_THICKFRAME` + `WM_NCCALCSIZE` 归零 + `WM_NCHITTEST` 八向缩放；圆角 `CreateRoundRectRgn`+`SetWindowRgn`，`CornerPx` 0~24 默认 10 存 `webui-shell.json`）
      （页面：`TitleBar_web.vue` 三枚交通灯 + 居中标题，`win.drag/min/max/close/fullscreen/corner/state` 消息协议；窗口圆角与页面控件圆角完全独立）
- [x] 前端可复用部分保留，有差别的部分建 `_web` 副本。
      （`shell.ts` 壳桥 + `App_web.vue` / `router_web.ts` / `MainLayout_web.vue` / `TitleBar_web.vue` / `Settings_web.vue`；`main.ts` 按 `inShell` 分流入口）
- [x] 根据最近的修改更新 about.md。
- [x] 修复 RSSI 锁死 -127dBm；把频率拆成「广播频率」与「报告频率」（后者实时刷新）。
      （WinRT -127 为哨兵值且连接后不再投递广播 → `HasRssi` 标记，无效时三端显示 `—`/`--`，评分跳过该项；`AdvHz`/`NotifyHz` 拆分，`notifyHz` 随 `heart_rate` WS 事件下发）
- [x] 设备页顶行表头 + 全部信息按列对齐不溢出 + 超宽自动滚动 + 列宽鼠标拖放可改并持久化。
      （CSS Grid 表头与数据行共用 `grid-template-columns`；10 列；7px 拖拽手柄 + `setPointerCapture`；`localStorage['hrm-dev-cols']` + 重置按钮）

**[x] Phase Theme - 主题两维化 / 边框同步 / Debug Mode / Switch / 关窗确认 / 托盘菜单（第二十轮，diff.md 步骤 50）**
- [x] 窗口边框与主题背景色同步（含「被 Windows 框选时变白」）。
      （根因是无边框窗口仍有 1px DWM 边框取系统色；页面经 `win.theme` 传解析后的 rgb，宿主设 `DWMWA_USE_IMMERSIVE_DARK_MODE/BORDER_COLOR/CAPTION_COLOR`；Win10 不支持边框色属性时退回 `DWMNCRP_DISABLED` 关掉非客户区渲染）
- [x] 非 debug 版不显示前端左下角路径提示。
      （即 WebView2 `IsStatusBarEnabled`，与 `AreDevToolsEnabled`、F12 一并受 Debug Mode 控制）
- [x] release 版设置中添加 Debug Mode 开关。
      （设置页「调试」卡片 → `POST /api/settings {app:{debug}}`，即时生效 + 落盘；壳侧经 `win.debug` 同步，启动初值由 `--debug` 给）
- [x] 修复设备 Tab 蓝牙设备列表 UI 不协调（BPM、状态列）。
      （表头与数据行共用 `align`；数字列 `tabular-nums`；行高 `min-height: calc(30px * var(--sp))`；列宽重排）
- [x] 二元布尔控件统一换 Switch Toggle，遵循全局主题与圆角。
      （新增 `components/XSwitch.vue`，替换 11 处；真复选语义保留原生 checkbox）
- [x] 设置中添加主题「遵循 Windows 主题色」。
      （拆成明暗 `mode` 与配色 `palette` 两个独立下拉，首项都是「遵循系统」并持久化；后端 `SystemTheme.cs` 读注册表 + `WM_SETTINGCHANGE`/`WM_DWMCOLORIZATIONCOLORCHANGED` → `sys_theme` WS 事件）
- [x] 关闭前端窗口时询问「退出进程」还是「最小化到 Tray」，可勾「不再询问我」（设置可改）。
      （`dirty.ts` 未保存登记表 + `CloseDialog_web.vue` 两步确认；退出走 `/api/shutdown` 由后端结束其余组件；`config.ui.closeAction` = ask/exit/tray）
- [x] Tray 右键菜单同步前端主题风格，可快速控制常用功能。
      （`ThemeColors.cs` + `ThemedMenuRenderer.cs` 主题化渲染 + 圆角裁剪；菜单扩到 12 项，二元项文字随状态刷新；壳单实例事件 `Local\hrm-webui-show` 支持从托盘唤回已最小化的窗口）


**[ ] Phase Mobile - 准备着手头显/Android侧的APP实现**

---

## 5. 验证与遗留

- 每页 V2 功能对照 + WS 推送即时更新 + 切语言/主题即时生效；壳与浏览器双载体同页面一致。
- 构建链：`npm run typecheck` → `build-webui.ps1` → `build.ps1 -Mode debug/releases` → 端到端冒烟（复用第十七轮端点清单）。
- 已验证（步骤 45，`Built\debug-20260904_043346`）：REST 20/20 200；WS 六类事件字段可读；真机 Xiaomi Smart Band 9 Pro 心率 84 → 曲线 canvas 像素持续增长；12 路由 headless DOM 有内容；`t()` 126 key 全命中。
- 已验证（步骤 47，`Built\releases-20260904_070043` + `Built\standalone-20260904_072051`）：vitest 29/29；五主题 × 13 路由 = 65 组对比度采样全过；两种 Release 产物独立目录冒烟各 27 项全过（REST/静态 SPA 回退/打包资源/WS）；`hrm-webui.exe` 壳窗口正常；`cargo build --release` 通过。
- 已验证（步骤 50，`Built\debug-20260904_230319`）：locales 336 keys；`vue-tsc` 0 error；vitest 31/31；`dotnet build`（App + 壳）0 error；实机 Win10 19045 上 `sysTheme` ABGR 解码正确、`ThemeBg` 由 `#000000` 修为 `#171717`、`DWMWA_BORDER_COLOR` 不支持时兜底关掉非客户区渲染、Debug Mode 与主题两维往返读写一致。
- 遗留：
  - ① Cef 壳是否引入（Overlay 需求出现再评估）；
  - ② Rust egui 后续是否退役（不在 V3 范围）；
  - ③ `lang.ts` 回写脚本与 Vue JSON 的长期维护路径；
  - ④ `composables/` 未抽出（等第三处复用）；
  - ⑤ `image/app.ico` 图标资源仍缺（目录已建、构建与壳已接线）；
  - ⑥ Electron 备壳未 `npm install`/打包（Phase A4）；
  - ⑦ egui 前端只读折叠后的 `ui.theme`，不跟随系统主题（Web 侧已支持）。
