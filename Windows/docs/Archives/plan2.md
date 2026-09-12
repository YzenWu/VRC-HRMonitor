# HeartRateMonitor V2 重构计划（plan.md）

> 目标：保留现有"共享核心 AppHub + C 引擎 + Rust egui 本地前端 + 按需 Web 前端"的总体架构与风格，
> 把前端重写为 **11 Tab 分类**，落地多语言（繁简英日西）与多主题，持久化引入 **SQLite**，
> 并**同步补齐**各 Tab 所需的 C# 后端命令。本文档是执行蓝图，随实现推进更新；每次改动记录见 `diff.md`。

旧版计划已归档到 `Archives/plan1.md`。改动启动前已备份到 `Backup/<时间戳>/`（排除可再生目录）。

---

## 0. 项目核心

一句话：**把戴上 BLE 心率带后的人体状态，推送到 VRChat 的聊天框与相关 OSC 参数**，并围绕它提供
监控/记录/分析/导出。

恒定的设计原则：

1. **性能热路径在 C 引擎**（OSC 编解码/发送/接收），C# 只做业务编排，Rust egui 只做呈现与交互。
2. **业务逻辑集中在 `AppHub` 一处**，Web（HTTP/WS）与本地（Pipe）两个传输层共用，避免逻辑重复。
3. **本地前端（Rust egui）为主**，Web 前端（SolidJS）按需启动（`--web` 或设置页手动开启）。
4. **一切可调参数尽量用户可见、可持久化**；涉及"导出"的板块都支持多格式（JSON / YAML / CSV / SQLite 导出）。
5. **前端保持现有宽松卡片风格**（控件最小高度 34、圆角卡片、侧边栏导航），本次只重排逻辑与扩充功能，不大改视觉语言。

---

## 1. 技术决策（本次新增）

| 关注点 | 决策 | 理由 |
|---|---|---|
| 持久化 | **SQLite**：本地 Rust 前端用 `rusqlite`（内嵌）；C# 侧用 `Microsoft.Data.Sqlite`，共用同一 `hrm.db` | 内嵌免服务，支持高频心率/OSC 记录、历史查询、多格式导出 |
| 多语言 | **手写翻译表 + `tr()` 宏**：`lang.rs` 中 `Map<Lang, Map<key, text>>`，`tr(ctx, "key")` 取当前语言；实时切换；五语言繁简英日西（繁体以台湾习惯） | 零新依赖，egui 字体由现有 `setup_fonts` 覆盖，热切换简单 |
| 主题 | `themes.rs`：预设深色/暗色/森林/落日 + 自定义（主色/背景/圆角/密度），作用于 `egui::Context` visuals | egui 原生可视化样式表，改动集中一处 |
| 头部头显 | 新建 `HeadSet/` 目录，Tab 显示 WIP；后续用内网 UDP 实时传头显硬件信息 | 前期占位，接口留白 |
| 前后端同步 | 本地前端每个 Tab 所需的命令，**同步**在 `PipeServer`/`AppHub` 补齐 | 避免空壳前端 |
| 步进/帧 | 保持 `client.rs` 单线程 + `PeekNamedPipe` 轮询、`request_repaint_after(33ms)` 限帧 | 沿用第七轮已验证方案，杜绝冻结 |

---

## 2. 目标：本地前端 12 Tab

导航侧边栏（序号即顺序）：

| # | Tab | 职责摘要 |
|---|---|---|
| - | Overview | 总览页（第十一轮新增，侧栏首位）：把各功能 Tab 的核心功能聚成独立卡片（心率 / 设备 / 悬浮窗 / 推送 / OSC 监视 / 硬件 / Web UI / 日志），卡片**右上角**「前往 ▸」直接跳到对应 Tab；顶部为连接与健康状态 + 7 个统计小卡 |
| 0 | OSC | 显示 VRChat 是否运行及 OSC 回传数据；多卡片接收/监视各参数；支持实时记录到数据库等载体。端点参见 `Skills/VRChat Interfaces.skill` |
| 1 | Pusher | 配置向 VRChat OSC 端点发出的内容；参考 Skill 扩展（如数据刷新时给 `/chatbox/typing` 写 1）；Webhook 出站也归于此 |
| 2 | HeartBeat | BLE 心率展示 + **悬浮窗管理并入此 Tab**；单设备时顶部"横长条"卡片（左 1/4 心率 + 右 3/4 曲线，两区可拖拽调整）；多设备时左侧改列表（设备名/信号/心率/报告频率）、右侧曲线默认平均值、右上角可指定某设备绘图；心率曲线平滑度/记录时长/高级设置可调；**健康状态算法**（心率+OSC 姿态→睡眠/活跃/兴奋→`{HEALTH_STATUS}`，波动大则 Warn 一次，首次 30s 静息校准+10s 对照）；其余区域放其他卡片（浮窗、数据记录导出等） |
| 3 | Devices | 实时扫描 BLE 设备（默认高频扫描不计功耗，可调）；扫到带标识符的设备先缓存，再次扫到未及时取到标识符则用缓存；每列数据等宽间隔防刷新抖动；按"名称优先级 + 信号强度"排序（广播含自身名/标识符、名称含 Apple/Xiaomi/REDMI/HUAWEI/Garmin/Polar/Honor/Amazfit/Band 等、延迟低/信号强、无耳机音箱特征 AirPods/Pods/Buds/Enco、无智能家居 Midea/Mesh）；保存历史连接设备；自动重连（断开后按指定频率扫描重连，30 分钟内无回应则放弃）；RSSI 衰减 >100 时日志打印信号弱；支持设备重命名（如 Apple Watch → Left Hand） |
| 4 | HWInfo | 参考 `Skills/PerfGet.skill` 扩展现有采集：实时变量（睿频/句柄/线程与进程数，尽量浮点，单位可由用户指定，接口与获取方式可手动指定）；支持用户手动覆写变量并持久化（启用运算/拼接符）；支持选择写浮点、运算对齐精度、是否四舍五入；新增实时本地/UTC 时间、NTP 时间、时区变量；支持手动修改变量名、自增变量（运算/拼接/正则/命令行标准输入输出） |
| 5 | HeadSet | WIP 占位；后续内网 UDP 实时传头显硬件信息 |
| 6 | API Server | 多种调用方式（`/heartbeat`、Socket、WebHook 等）；不只推心率，也能像 OSC 那样回传系统信息 |
| 7 | Settings | 多语言实时切换与映射（繁简英日西）；深色/暗色/森林/落日主题切换与自定义；集成其他板块高级设置 |
| 8 | Logs | 沿旧版增强：正则过滤、按日志等级过滤等 |
| 9 | Console | 内建 CLI 命令行调试；`help`/`?` 返回帮助；可滚动 |
| 10 | Monitor | 集合历史游戏记录、健康数据、睡眠分析；支持数据导出与多格式分析 |

> 各 Tab 内涉及"导出"的统一支持 JSON / YAML / CSV / 数据库导出；参数尽量提供自定义 + 持久化。

---

## 3. 起步（本次先做）

**Phase 0 — 环境与备份**（已完成）
- `Backup/<时间戳>/`（robocopy 整树，排除 `Built/target/node_modules/obj/dist`）。
- 旧 `plan.md` 归档为 `Archives/plan1.md`。

**Phase 1 — 前端骨架 + i18n + 主题（本次第一步）**
- Rust `Cargo.toml` 加 `rusqlite`；建 `lang.rs`（五语言翻译表 + `tr()`）、`themes.rs`（4 主题预设 + 自定义）。
- `main.rs`：`Page` 枚举扩为 11 项（心率为默认落地页），侧边栏导航按新顺序；`label()` 接入 `tr()`。
- 事件/状态先行接入：`state.rs` 增加语言、主题、`health_status`、曲线缓冲、DB 状态等字段与解析。
- 现有功能迁入对应 Tab（OSC 接收卡片、Pusher 模板、HeartBeat 悬浮窗卡片、Devices、HWInfo、Settings 语言/主题、Logs 增强、Console 占位、Monitor 占位、HeadSet WIP）。
- `PipeServer`/`AppHub` 补：`lang`/`theme` 读写、`health_status`、`heartbeat.record start/stop`、`devices sort/rename/reconnect/rssi`、`hw variable override`、`api` 相关、`console`（`exec`）。

**Phase 2+ — 逐 Tab 深挖**（大纲见 §4，后续每个 Tab 完成一段就更新 `diff.md`）。

---

## 4. 分阶段修改大纲

### Phase 1：骨架 + i18n + 主题
- [x] 备份 / 归档（上节）
- [x] `rusqlite` 依赖；`lang.rs` + `themes.rs`
- [x] `main.rs` 11 Tab 枚举 + 导航 + 默认页=HeartBeat
- [x] `state.rs` 新字段/解析（lang/theme/health_status/curve/db）
- [x] 现有功能迁入新 Tab
- [x] `PipeServer`/`AppHub` 同步补命令
- [x] 构建 + 冒烟（11 Tab 可切换、语言/主题实时切换、悬浮窗管理在 HeartBeat）

### Phase 2：OSC + Pusher
- [x] OSC：VRChat 回传参数多卡片（按 `/首段/` 分组）；实时记录入库（`record` 开关 + 表结构）。
- [x] Pusher：模板编辑 + 变量预览 + 发送测试；Webhook 出站 CRUD 移入。
- [ ] `/chatbox/typing` 联动（心跳刷新时写 1）：需 C 引擎 int 编码的 P/Invoke 联合体封送，延期。

### Phase 3：HeartBeat（核心）
- [x] 单设备横长条卡片（可拖分隔条，左侧心率 1/4 + 右侧曲线 3/4）；曲线平滑度/记录时长/高级参数。
- [x] 多设备：左侧设备列表（设备名/信号/实时心率/报告频率），右侧曲线默认平均值 + 右上角选设备。
- [x] 健康状态算法：心率 + OSC 姿态 → `{HEALTH_STATUS}`；波动大 Warn；首次 30s 静息 + 10s 对照校准。
- [x] 浮窗管理并入；数据导出卡片（JSON/YAML/CSV/SQLite）。

### Phase 4：Devices
- [x] 持续扫描（可调节流）；标识符缓存；等宽列；排序（名称优先级 + 信号，含品牌白名单/黑名单特征词）；保存历史；自动重连（30 分钟放弃）；RSSI 衰减 > 阈值告警；设备重命名（别名持久化）。
- [x] 排序口径细化：有标识符（非 UNKNOWN）/ 品牌白名单 / 低延迟高信号 / 排除耳机音箱 / 排除智能家居。（第十六轮，步骤 36A：三张词表扩到 27+18+18 项，权重常量化，新增「广播频率」作通信延迟代理量，品牌分与降权互斥（`HUAWEI FreeBuds` 不再被品牌分抵消），别名与广播名一并参与匹配；`Score()` 改为 `Explain()` 明细求和并经 `scoreParts` 透出，Console `score` 命令可查。真机实测小米手环 4415 / 华为手环 4410 / 手机 1493 / 无名 830 / midea -3）

### Phase 5：HWInfo
- [x] 按 PerfGet.skill 扩展（PDH 实时睿频/句柄/线程/进程数，浮点）；单位可指定。
- [x] 变量覆写（运算/拼接符，持久化）；浮点写/对齐精度/四舍五入开关。
- [x] 实时本地/UTC/NTP 时间、时区变量；变量改名、自定义变量（运算/拼接/正则/命令 stdin/stdout）。
- [x] 内置多个预设NTP服务器。如清华大学、阿里云计算。（第十四轮，步骤 34：`AppHub.NtpPresets` 8 条常量（阿里云 ×2 / 清华 TUNA / 国家授时中心 / NTP Pool 中国区 / Microsoft / Apple / NTP Pool 全球）经 `HwConfig()` 透出 `ntpPresets`，前端 `hw_settings_card` 加预设下拉，选中即填入输入框，仍需手动保存。预设为常量不入 `config.json`）

### Phase 6：API Server
- [x] `/heartbeat`、WebSocket、WebHook 多调用方式；回传系统信息（类似 OSC）。（第十二轮，步骤 32：`config.api` 六项 + `AppHub.ApiConfig/Heartbeat/SysInfoQuery/SysInfoPayload/ApplyPushTimer` + `Dispatch` 统一命令表（IPC 与 WS 共用）+ `WebServer` `/heartbeat`、`/api/apiserver/config`、`/api/sysinfo`、WS 下行命令与 token 校验 + `WebhookManager` 占位符扩到全部系统信息变量 + 前端 API Server 页（端点复制/令牌/回传间隔/节流/变量白名单）。实测 `/heartbeat`、`/api/apiserver/config`、`/api/sysinfo?template=` 均 200 且模板渲染正常。**注意：`api.token` 为空 = 不校验，目前仅靠 `127.0.0.1` 回环绑定保护**）

### Phase 7：Settings
- [x] 多语言实时切换（繁简英日西，繁体台湾习惯）；深色/暗色/森林/落日 + 自定义主题；集中其余板块高级设置。（第十三轮，步骤 33：`UiSection` 扩 7 字段（`accent`/`bg`/`panel`/`corner_radius`/`density`）+ `ApplySettings` `ui` 分段 clamp + `UiJson()` 透出；`themes.rs` 五态圆角、密度按固定基线倍率、`Light` 接自定义配色、新增 `parse_hex`/`to_hex`；前端 `pull_ui_palette` 回填 + `save_ui` 扩 7 字段 + Settings 页新增背景/面板/圆角/密度控件；页尾集中复用 `hb_health_card`/`hb_curve_card`/`dev_settings_card`/`hw_settings_card` 四张卡片并在 `tick()` 预拉三份配置；`lang.rs` 补约 60 键 × 5 语言，`main.rs` 硬编码中文替换至 Settings/HeartBeat/Devices/HWInfo/Logs/顶栏/退出窗/`draw_curve`。**未做 GUI 目视与五语言实机核对**）

### Phase 8：Logs
- [x] 正则 / 等级多条件过滤；保留实时跟随；导出过滤结果。（第十四轮，步骤 34：后端 `AppHub.Logs(filter,limit,regex,levels)` + `FilterLogs` 内核（.NET `Regex` 权威执行，`IgnoreCase|CultureInvariant`；语法错误降级为不过滤并回传 `error`）+ `LogsExport` + `Exporter.ExportLogs`/`SplitLogs`/`LogFormats`（txt/json/yaml/csv → `exports/`）+ `Dispatch` 的 `A()` 数组取值器与 `logs_export` 命令 + `/api/logs?regex=&levels=` 与 `POST /api/logs/export`；前端 `page_logs` 加正则复选、四等级筛选、`命中 X/Y` 计数、正则错误红字、导出格式下拉 + 导出按钮，`stick_to_bottom` 实时跟随保留，条件变化即重新向后端查询）

### Phase 9：Console
- [x] 内建 CLI：`help`/`?`；命令历史；可滚动；复用后端 `CliApp` 命令集。（第十六轮，步骤 36B：新建 `Core/CommandShell.cs` 作为唯一文本命令实现（20 条命令，`Commands` 表带 usage/desc，`Pad()` 按 CJK 双宽对齐），`CliApp` 瘦身 200 → 59 行只留 REPL 与 `selftest`，`AppHub` 加 `cli{line}`/`cli_help` 命令与 `POST /api/cli`、`GET /api/cli/help`；前端 `console_exec` 106 → 35 行改为纯转发 + 状态回填，新增 ↑↓ 命令历史（上限 200，方向键须在 `TextEdit` 之前取走）+ `console.failed`/`console.history` 两键 × 5 语言）

### Phase 10：Monitor
- [x] 历史 HR/健康/睡眠数据聚合；多格式导出与统计分析。（第十七轮，步骤 37：`Core/MonitorStats.cs` 一次性聚合 hr 统计（min/avg/median/max/sd/trend/hist）+ health 状态计数 + 设备分布 + OSC 地址 Top + db 信息，`hours/buckets` 参数化；`AppHub.Monitor()/MonitorExport()` + Dispatch `monitor`/`monitor_export` + REST `GET /api/monitor`、`POST /api/monitor/export`；本地 `page_monitor` 六统计卡 + 区间下拉 + 趋势/直方/健康/设备/OSC Top + 导出；`lang.rs` 补 `mon.*` 25 键。实测 `/api/monitor` 返回 hr/health/devices/osc/db 四段）

### Phase 11：Web 前端对齐
- [x] 参照本地前端同结构重写 WebUI 12 Tab + i18n + 主题（`--web` 时生效）。（第十七轮，步骤 38：App.tsx 12 Tab（Overview/OSC 监视/Pusher/HeartBeat/Devices/HWInfo/HeadSet/ApiServer/Settings/Logs/Console/Monitor）全部接真实 REST/WS；`lang.ts` 移植 lang.rs 全量 key×5 语言、`theme.css`/`theme.ts` dark/light/forest/sunset/custom + 圆角/密度；语言/主题/外观设置即时保存（`POST /api/settings`）；页面批 1-3：Overview/OSC 监视/Pusher/ApiServer/HeadSet/Console/Monitor/Logs/Settings 重写 + Devices 重命名 + HWInfo 采集设置卡。`npm run tsc`/`build-webui.ps1`/`build.ps1 -Mode debug` 全过，端口 8228 端到端实测通过）

### Phase 0: 潜在问题修复与完善
- [x] 缺少单独为设备打开心率浮窗、为主浮窗指定数据来源的控件。（第十一轮：`WindowSection.Source/RefreshMs/Sources` + `FloatWindowHost.SourceOf/BpmOfSource/ApplySources` + IPC `float_config` + REST `/api/float/config`；前端 `hb_overlay_card` 与 Overview 均可按设备开窗、按窗指定数据源、调刷新间隔。顺带修复 `App.CurrentBpm` 无人赋值导致主浮窗与 `{BPM}` 恒 0 的根因）
- [x] 悬浮窗的Windows原生顶栏菜单应被隐藏。使用右键菜单、左键移动、双击打开主窗口进行操作。（第十七轮，步骤 39：`FloatingWindow` 用 `FormBorderStyle.None` 永不显示原生顶栏 + `WM_NCHITTEST` 自绘右下角 grip 缩放热区；右键 `ContextMenuStrip`、左键 `HookMouse` 拖拽、双击回主窗、锁定 `WS_EX_TRANSPARENT` 穿透。**实机手感待人工目视**）
- [x] 添加CPU_FREQ_GHz等标准单位的变量。（第十七轮，步骤 39：`SysInfoService` PDH 实时频率双写 `CPU_FREQ_MHz`/`CPU_FREQ_GHz`（睿频小数），`RegistryInfo`/`WmicInfo` fallback 同步双写；`VarEngine.ApplyStandardUnits` UnitRules 派生 `RAM_*_GB/MB`、`CPU_TEMP_*_C`、`UPTIME_*`、VRAM/DIMM/DISK/DRIVE/NIC 等；`Vars.All` 白名单收录全部派生变量。实测 `/api/hw` 含 `CPU_FREQ_MHz`/`CPU_FREQ_GHz`）
- [x] BLE设备的刷新速率计算未完善。按上一次收到广播数据的时间和本次收到的时间计算差值。使用此值作为刷新速率。（第十一轮：`DeviceRegistry.Observe()` 按 `now - LastSeen` 求 hz 并 EMA 平滑）
- [x] `App.LogBuffer` 在默认启动路径下恒空（入队只写在 `MainForm` 的 `OnLog` 里，而 hub + 托盘路径不构造 `MainForm`），导致 `logs`/`logs_dump`/Console `logs` 自第五轮起一直读空队列。（第十四轮，步骤 34D：改由 `AppHub.WireEvents` 入队 + 5000 行上限，与 `MainForm` 一致且两路径互斥不重复）
- [x] `/chatbox/input` 只发单字符串，缺少 VRChat 要求的两个 bool（立即发送 / 提示音），导致每秒弹一次待确认输入框。（第十五轮，步骤 35A：C 引擎加 `osc_engine_send_chatbox`（`",sTF"`）+ `OscService.SendOne` 统一分流，`Tick`/`SendTest`/`SelfTest` 三处调用点同改）
- [x] OSC 回传以逐条事件 + 逐条 Debug 日志呈现，每 4 秒约 800 条，打满 IPC 与日志缓冲。（第十五轮，步骤 35C/D：`AppHub` 按地址聚合 + 200ms 批量下发 `osc_params`（事件数降至约 1/38），前端换 `BTreeMap` 聚合表卡片化，去掉逐条日志）
- [x] `T`/`F` 类型标签在解码后取不到值，`AFK`/`Seated` 健康判定通道恒 false。（第十五轮，步骤 35B：`OnPacket` 按标签定值）

### Phase 12：Overview 总览
- [x] 新增 Overview Tab（侧栏首位）：8 张核心功能卡片 + 右上角「前往 ▸」跳转 + 7 个统计小卡；进入时一次性拉 `status`/`record`/`webhooks`/`logs`。

---

## 5. 收尾（每个 Phase 结束时执行）

1. **构建三分支**：`build.ps1 -Mode debug|releases|standalone` 全过。
2. **CLI selftest 回归**：PASS（真机 HUAWEI Band HR-FD6，最长阻塞 ≤ 250ms）。跑法：
   `$OutputEncoding=[System.Text.Encoding]::ASCII; @("selftest","exit") | & .\HeartRateMonitor.exe --cli`。
3. **GUI 冒烟**：12 Tab 逐个切换无卡顿、对应命令 RTT ≤ 基线；语言/主题实时切换即时生效；
   每个新增命令至少触发一次并观察事件/日志正确。
4. **SQLite 验证**：表结构与记录正确写入，导出（JSON/YAML/CSV）内容一致。
5. **文档**：更新 `diff.md`（按步骤）、`about.md`（架构/目录/运行模式）。
6. **清理**：临时诊断脚本 / 输出 / 截图删除。
7. 若某项依赖外部条件（真机、VRChat 运行）不可复现，则在 `diff.md` 标注"待人工复测"并给出复现步骤。

---

## 6. 目录约定（新增）

```
C# Test/
├── Backup/<时间戳>/            # 每次大改前的整树快照（排除 Built/target/node_modules/obj/dist）
├── Archives/                   # 旧版/旧阶段文档归档（plan1.md 等）
├── HeadSet/                    # 头显（V2 新增，Tab5 WIP；后续 udp 服务）
├── RustUi/src/
│   ├── main.rs                 # 应用入口 + 12 Tab 布局（含 Overview 总览）
│   ├── state.rs                # AppState + 事件/响应解析
│   ├── client.rs               # 管道 IPC（单线程轮询）
│   ├── trace.rs                # debug 落盘 trace
│   ├── lang.rs                 # 五语言翻译表 + tr()
│   └── themes.rs               # 主题预设 + 自定义
├── App/
│   ├── Core/AppHub.cs          # 业务核心（命令全部集中）
│   ├── Ipc/PipeServer.cs       # 管道 JSON 行服务器
│   ├── Db/ (新增)              # SQLite 读写（Microsoft.Data.Sqlite）
│   └── ...其余不变
└── WebUI/                      # 后续按 11 Tab + i18n 对齐（Phase 11）
```

> DB 文件：`hrm.db`（程序目录）。记录表：`hr_records(id, ts, mac, bpm)`、`osc_records(id, ts, addr, value)`、
> `health_records(id, ts, status)`、`variables(name, value)` 等，具体随 Phase 增补。
