# HeartRateMonitor V1 修改日志（diff.md）

> 按时间顺序记录每次实际改动，便于回溯与定位。

## 2026-09-12 Pre 0.0.1 最终打磨 / Toolkit / 发布验收
- **界面已知问题收口**：日志页 Trace 与 Regex 已位于同一工具行，完整 Trace 说明仅放在 `v-bubble`；Hardware 搜索同时匹配 name/value/context，支持 `name=x`、`context=x`，多选筛选使用 `XMultiSelect`，变量行左键弹窗与右键菜单提供同一动作集合；推送预览已提供 134/144 字提示；设备操作已统一为开启/关闭目标悬浮窗。主前端已通过 `UiLook.fontFamily`、Settings、CSS 变量和持久化支持全局字体；第二前端 `/font` hoster 端点尚未实现。
- **About / GitHub**：About 已消费后端嵌入清单与 GitHub snapshot，显示 stars、commit、issues、cache/stale/error、current release、latest release body、四组件一致性，支持刷新并以安全纯文本展示；`about.ts` 仅保留空兼容模块。目前仅提供 latest release，近期多条 releases 与 About 内资产按钮延期；资产下载仍由更新提示链按 `asset_pattern` 实现，About 只打开 release。
- **Toolkit 实装范围**：完成左下 dock 与 6 个工具入口、config 编辑、日志高亮、缓存管理、照片元数据并行索引与关键词/`author`/`include`/`date` 搜索、游戏/进程基础分析及 Skill 读取。社交网络仅有元数据基础，尚无图谱；编辑器跳转、Explorer 高级交互、图片压缩、数据库导出/自定义相册、EXIF 编辑及高级元信息侧栏/右键关联均延期，未将 Phase 10 标记为全部完成。
- **阻塞根因修复**：Toolkit cache 面对约 66 万文件时不再随 HTTP 请求同步扫描，改为后台单例快照并使用 30 秒 TTL；shutdown 改用独立 8 秒硬超时，实测 8.322 秒完成全链退出且无残留。SQLite 更新为 `Microsoft.Data.Sqlite 10.0.12` + bundle `3.0.5`，`dotnet list package --vulnerable` 扫描为 0。
- **发布与验证**：`Windows/Release.json` 为 `HeartRateMonitor Pre 0.0.1` / `0.0.1-pre`，四组件均为 `0.0.1`。Vue typecheck、55 项测试、C# build、C engine、Debug、Releases、Standalone、四组件版本、REST smoke 与退出链均通过；最新完整 Releases 产物为 `Built/releases-20260912_200501`，Standalone 为 `Built/standalone-20260912_203447`。Release 产物不含默认 config/Release.json，包含四个 EXE、WebUI、engine、icons、LICENSE、ZIP 与 SHA-256 sidecar。README 十语言与 `about.md` 已更新。
- **未实现 / 待人工验收**：P8 Custom STATUS 与 P9 Trigger 均未实现；本次 Pre 0.0.1 不包含 Trigger，相关 Trigger 门禁不得视为完成。尚未完成 Win10/Win11、全 DPI、多显示器、LAN 真机等实机矩阵，以及上述 Toolkit 延后项和第二前端 `/font`。
- **First Release 记录**：`2026-09-12 20:45:00 CST (UTC+08:00)` — **Pre 0.0.1 source snapshot First Release record**；仅记录源码快照，不声称远端 GitHub 已发布。

## 2026-09-12 Tab 指示器可见性修复与最近两轮安全复核
- **Tab 指示器（`layouts/NavMenu.vue`）**：修复首次挂载时路由尚未命中 active 项会令 `indReady` 永久停在 false、后续路由更新仍走动画分支但 opacity 继续为 0 的问题；首次可用 active 项现在强制 snap 后再置 ready。补齐 `.nav-ind`/`.nav-item` z-index 分层并将指示器左右边界对齐导航项 padding，确保主题色底背景可见、内容位于其上且后续切换保持 180ms linear 可打断滑动。
- **P6 安全二轮复核（`WebServer.cs`、`RemoteAuth.cs`、`Config.cs`）**：`/api/hw/var` 与 WS `hw_var` 改为 admin-only，堵住普通 Remote 用户经 custom `kind=cmd` 执行系统命令；`/api/logs/clear` 改 admin-only；所有非 admin/未知角色统一按能力白名单失败关闭；缺失 RemoteEndPoint 不再回退为 loopback admin。WS 每条消息重解析 session/角色并复核 LAN/WAN 开关，账号删除、踢出、降权、改密和关闭访问立即生效。改密/删号/角色变更主动失效目标用户旧会话（当前改密请求可保留）；`WanReady` 改为所有 admin 均须强密码且非默认密码，弱密码用户不能直接晋升 admin。默认密码远程 admin 在后端强制只允许修改本人密码，不能绕过前端继续管理；空用户库仅允许本机管理员初始化，开启 LAN 前必须已有 admin，损坏凭据库保留原文件并对 Remote 503 失败关闭；PascalCase `Remote/LoopbackOnly` 旧配置亦可正确迁移。
- **Web 设置页（`views/Web.vue`）**：补回此前仅有状态变量但模板遗漏的 WAN 风险确认 XDialog（init/pass/confirm、双密码、不回显、确认勾选、不可绕过）；未初始化管理员时禁用 LAN/WAN 并提示必须先在本机创建强密码管理员。新增 `web.initlocal` 并同步 10 种语言（生成表 580 keys）。
- **构建脚本（`build-webui.ps1`）**：vite stderr 统一逐行 Write-Host，消除成功构建时仍显示 PowerShell `NativeCommandError` 的误报，同时继续严格按 `$LASTEXITCODE` 判失败。
- **重新验证**：`npm run locales` 580 keys；`vue-tsc --noEmit` 0 error；Vitest 7 files / 55 tests 全过；IDE diagnostics 0；`dotnet build` 成功（仅既有 SQLitePCLRaw NU1903）；`build-webui.ps1` 生产构建通过且不再出现 NativeCommandError。完整 `build.ps1 -Mode debug` 四组件版本一致，产物 `Built\debug-20260912_002707`（该次运行早于最后一轮脚本输出净化，但代码与 WebUI 均已单独复验）。
- **仍需部署层保证**：WAN 当前为 HTTP，生产公网暴露必须由受信反向代理/TLS 或 VPN 提供传输加密；应用按计划不信任未配置代理头。该项不是本轮代码可在现有单 HTTP 监听架构内安全补齐的内容。

## 2026-09-11 P6 单端口 Remote、LAN/WAN 安全边界、设备迷你卡、Tab 滑动指示器与性能验证
- **P6 后端（`Web/WebServer.cs`、`Web/RemoteAuth.cs`、`Config.cs`、`Core/AppHub.cs`、`Core/CommandShellApp.cs`）**：
  - Local/Remote 合并为单监听 `http://+:{port}/`（绑定全部网卡；URL ACL 拒绝时回退 127.0.0.1 并提示），删除独立 Remote 监听器与 `StartRemote/StopRemote` 全链路；新增 `BoundAll` 暴露绑定状态。
  - 新增 `SourceZone`（Loopback/Private/Public）：IPv4-mapped IPv6 归一化后按回环/RFC1918/链路本地/IPv6 ULA/公网分级；回环隐式本地 admin，非回环需 Remote.Enabled，Public 额外需 WanEnabled，拒绝写 `log.web.source_reject`；明确不信任 Forwarded/X-Forwarded-For（反代需未来 trusted proxies 配置）。
  - `RemoteAuth`：删除 admin/admin 自动创建（空用户库合法，日志 `log.auth.no_users`）；新增 `PasswordStrong`（≥8 位含字母数字）、`HasUsers`、`AdminDefaultPassword`、`WanReady`（强密码且非默认）、`KickAdminSessions(keepTokenHash)`、`Sha256`；`UserRow.Strong` 标记；`Manage` 新 case "init"（仅空库+强密码）、add/改密 admin 强制强度门并踢旧 admin 会话（保留当前 token）；登录成败审计日志。
  - WS 与 REST 统一授权：WS `UserWsCommands` 白名单（普通用户仅 status/devices/connect/osc_*/monitor*/logs/export 等读侧与设备操作），REST `AdminOnlyPrefixes`（hw/config、vrchat/launch、logs/dump、devices/config、device/block、scan、record/config、web/）+ `UserCapablePrefixes` 能力白名单取代路径拒绝表；`AppHub.Dispatch(cmd, req, admin)` 与 `Config(admin)` 按角色屏蔽 api token（普通用户 `api_config` 返回 forbidden）。
  - `/api/remote/me`、`login` 返回 `sourceZone/mustChangePassword/adminInitialized`；空库 login 走弱密码 init 拒绝；`/api/remote/users` 的 pass 改密即踢旧 admin 会话；`/api/remote/config` 开 WAN 前检查 `WanReady`（弱密码 400），LAN/WAN 切换写审计日志；`RemoteConfigPayload` 附 port/boundAll/wanReady/note（回退绑定时给 netsh urlacl 提示）。
  - `Config.RemoteSection` 精简为 Enabled/WanEnabled/IdleMinutes，旧 `remote.host/port/loopbackOnly` 静默迁移（`loopbackOnly=true`→`Enabled=false`；迁移发生在 Logger 创建前故不写日志）；CLI `remote` 子命令改 `wan on|off`（弱密码阻断 `remote.wan_blocked`）+ status 显示 wanEnabled/port/boundAll；`VrchatLaunch()`（注册表 HKLM/HKCU×64/32 + Steam 库 + `steam://rungameid/438100` 回退）。
- **P6 前端（`views/Web.vue`、`components/RemoteLogin.vue`、`stores/app.ts`、`api/index.ts`）**：会话卡正文完整显示用户名/角色/来源 IP + `web.zone.*` 分区标签/登入时间/后端启动时间 + 登出；配置卡改为同端口说明、LAN/WAN 开关、绑定与来源状态、管理员密码三态、boundAll=false 时显示 urlacl 提示；WAN 开启流程按 adminInitialized/wanReady 分 init（首次设密）/pass（改密）/confirm（风险确认）三模式，XDialog 风险确认不可绕过；RemoteLogin 支持 `mustChangePassword` 强制改密双表单与 `init` 弱密码提示。
- **设备迷你卡（`layouts/StatusBar.vue`，plan L723）**：底栏设备胶囊悬停 350ms 弹出 Teleport 到 body 的迷你卡（避开滚动容器裁剪）：BpmChart 64px 心率曲线（复用 deviceCurves）、min/avg/max 统计、信号点/MAC/RSSI/上报频率；视口 clamp 定位（不越界），120ms linear 显隐，替代原 chip 上的 v-bubble。
- **Tab 滑动指示器（`layouts/NavMenu.vue`，plan L724）**：指示器改为独立 `.nav-ind` 滑动条，active 项 `offsetTop/offsetHeight` 驱动 `top/height 180ms linear` 过渡（垂直滑动 + 线性插值；CSS transition 天然支持中途打断重定向）；首帧与列表变化 snap 不滑动（`indReady` 门），`ResizeObserver` 跟随重排；`prefers-reduced-motion` 与 `html[data-anim=off]` 时关闭。
- **性能验证（plan L708）**：`build.ps1 -Mode debug` 全量构建（gcc 引擎 + vite 前端 + dotnet 四组件）通过且版本校验一致 → `Built\debug-20260911_202626`；顺带修复 `build-webui.ps1` 在非交互宿主下 vite stderr 触发 NativeCommandError 终止构建的问题（合并 stderr 放宽偏好、以退出码判定）。实测 `--trace --cli web start` 输出"单监听已绑定全部网卡 (端口 9460)"；curl `GET /api/remote/me` 返回 `sourceZone:"loopback"/mustChangePassword:false/adminInitialized:true`、`GET /api/status` 设备列表正常；trace.log 共 8 条打点，PERF 仅 sysinfo 一次性冷启动收集（registry 49-96ms、systeminfo/pscomputerinfo 秒级后台）与 `web.api/api/remote/me 33ms` 热路径，无循环打点风暴；静默模式 6 秒 CPU 3.8s（含启动+SysInfo+BLE 扫描）、内存 227MB、无残留进程。
- 新增约 25 个 lang.ts 键（web.portinfo/lan/wan/wantitle/wanconfirm/wanpass/zone.*、boundall/adminpass*、remote.mustchange/initweak/newpass*、web.err.mismatch 等）并同步 ko/de/fr/yue-HK；门禁：locales 579 keys、vue-tsc 0 error、Vitest 55/55、dotnet build + 全量 debug 构建、四组件版本校验、REST 实机 curl 验证通过；保留既有 SQLite 安全公告与 WebView2Host WindowsBase 警告。

## 2026-09-11 顶行二次清理、粤语翻译、移动端底栏与 E1 收口
- 顶行二次排查（plan L666）：Web 会话卡正文显示用户名/角色/来源 IP/登入时间/后端启动时间（本机会话以页面打开时间与 127.0.0.1 兜底），登出按钮移入正文；Web 新增用户、Settings 前往 About、Settings debugHint、Heartbeat 曲线重设/导出结果、Overview 扫描记录与 VRChat 状态、Devices 重设列宽全部移出标题行，标题只留名称与计数徽标。
- 粤语实际翻译（L667）：yue-HK 覆盖词典扩至约 280 键，覆盖导航、通用词、安全模式、设置、记录、Web/远程、扫描、OSC、推送、VRChat、设备、悬浮窗、日志、心率、硬件、API、控制台、监测、关于全部高频界面文案。
- Trace 说明（L668）：日志页删除底部整行状态/说明文字，运行状态、重启提示与完整说明只保留在开关悬停 Bubble 中。
- 移动端底栏（L669）：≤860px 时底栏改自动高度+换行+纵向滚动，设备胶囊条独占一行保持横向可达，隐藏高度拖拽柄避免与触摸滚动冲突。
- E1 收口：推送预览按 Unicode 码点计数（代理对计一）并显示 n/144，超 134 字显示仅提示不拦截的警告行；`FloatWindowHost.OpenIds` 经 `/api/status` 的 `floatIds` 下发，设备弹窗、右键菜单、心率设备行统一为"开启/关闭悬浮窗"切换，关闭只作用于目标设备；`float.opendev` 语义改为开启悬浮窗并新增 `float.closedev`。
- 新增 web.loginat/web.bootat、push.charwarn、float.closedev 四键并同步全部 locale；门禁：locales 554 keys、vue-tsc 0 error、Vitest 55/55、App 构建、编辑器诊断通过；保留既有 SQLite 安全公告警告。

## 2026-09-11 VRChat 启动入口、底栏胶囊与日志页收尾
- 新增 `POST /api/vrchat/launch`（远程 user 角色禁用）：优先经卸载注册表（HKLM/HKCU × 64/32 位视图）与常见 Steam 库目录定位 `VRChat.exe`，找不到时回退 `steam://rungameid/438100`；进程已运行时拒绝重复启动，启动成功立即推送一次 `vrchat_status` 快照。
- 新增共享 `VrchatStatus` 组件（状态点、运行状态、PID/CPU/内存/响应/启动时间 + 启动按钮，compact 模式只留状态与 CPU；带 10 秒冷却防 WS 延迟期双击）：推送页原状态卡改用共享组件，OSC 页并入工具栏，总览并入心率曲线卡头。
- 底栏设备胶囊：`.hr-strip` 滚动容器以 padding+负边距为悬停上浮/放大预留裁剪空间，悬停胶囊可完整显示；`position+z-index` 让悬停中的胶囊覆盖两侧相邻控件。复核 Bubble 链路（600ms 延迟、statusPop 默认开）确认无回归。
- 日志页：Trace 说明全文只保留在开关悬停 Bubble（状态+重启提示留在底部），删除底部整行说明文字。
- 新增 `vrc.launch/launchhint/launchfail` 三键并同步全部 locale（含粤语覆盖）；门禁：locales 550 keys、Vue typecheck、Vitest 55/55、App 构建、编辑器诊断通过；保留既有 SQLite 安全公告警告。

## 2026-09-11 右键菜单、卡片顶列与语言选择修复
- 全局右键菜单加入 120ms 线性透明度/位移动画，并继续服从全局动画禁用设置。
- 主悬浮窗、OSC 自动启动及 API 变量筛选控件从卡片标题列移入正文，标题仅保留名称和状态。
- XSelect 支持选项前缀，语言选择显示旗帜；新增繁体中文（香港）与粤语（香港），粤语以完整香港繁体字典叠加粤语覆盖项。
- 前后端语言 ID、系统语言识别、持久化映射、壳层文本及 CLI/日志回退同步扩展到 10 种 locale；新增香港字典生成和粤语覆盖完整性测试。
- 门禁：locale 547 keys、Vue typecheck、Vitest 55/55、App 与 WebView2Host 构建、编辑器诊断通过；保留既有 SQLite 安全公告及 WindowsBase/Scale 警告。

## 2026-09-10 Phase P5：透明启动页与卡片折叠重构
- Splash 按启动目标屏幕工作区和实际 DPI 手工计算尺寸与居中，使用 TransparencyKey 保留 Logo、文字和进度，避免整窗透明；保留 500ms 最短展示，20 秒超时改为明确且可点击关闭的错误状态。
- CardGrid 为卡片输出稳定 `view:id`，About、Dashboard、OSC 分组和数据目录卡补齐稳定 ID；折叠状态不再依赖翻译标题，切换语言和重排后不串卡。
- 整条标题支持鼠标及 Enter/Space，补齐 role、tabindex、aria-expanded/controls；一张卡的多个直属 body 同步折叠，并在页面卸载后清理失效引用。
- 标题内按钮、链接、输入、Switch、Range、Select 等交互控件不会误折叠或启动长按；CardGrid/Dashboard 长按拖拽使用抑制标记，移动超阈值取消且保留移动端纵向滚动。
- 门禁：Vue typecheck、Vitest 53/53、WebView2Host 构建、编辑器诊断和定向 diff 检查通过；既有 WindowsBase 引用冲突警告仍存在。

## 2026-09-10 默认多后端数据记录
- 新增统一记录配置：总开关默认启用，Avatar、VRChat 会话、设备连接、心率详情和硬件快照五类默认全开，并支持每类独立保留天数（0 为永久）。
- 新增 SQLite 分表、按日 JSONL、按日 CSV 三种可同时选择的后端；数据库与记录目录统一归属用户数据目录，按分类执行每日过期清理。
- 记录链路接入 OSC Avatar 变更、VRChat 启停状态转换、BLE 连接/断开与心率事件、每轮硬件采集快照；VRChat 相同状态不会重复写入。
- 双设置页新增共享记录卡片，可多选后端、启停分类、调整保留策略并显示实际目录；REST 使用嵌套 `enabled` 契约，CLI/托盘总开关与持久配置保持一致。
- 生命周期补齐托盘正常退出时的 AppHub 事件与计时器释放；旧 OSC 明细和健康状态表继续保留其独立兼容语义。
- 门禁：App 构建、Vue typecheck、547 个 locale key 生成、Vitest 50/50、编辑器诊断通过；实机 GET/POST 配置回读及 SQLite/JSONL/CSV 硬件快照写入通过。

## 2026-09-09 已知问题收口：心率曲线与实时设备参数
- 平均曲线：`heart_rate` 事件新增 `mainBpm/avg`，前端按主显示、平均、设备 MAC 分别维护曲线历史，选择平均时不再复用最后上报设备的数据。
- 实时设备卡：事件同时下发 `notifyHz/reports`，设备详情中的 BPM、上报频率和总上报数无需等待轮询即可刷新。
- 日志布局：Trace 诊断开关移至正则开关同列，日志区域下方仅展示本次运行状态和重启提示。
- 硬件卡片：过滤无名自定义变量，移除裸 `[]` 样式，修复空内容高度，并识别后端字符串形式数值以启用实时数字动画。
- 数值控制复核：曲线记录窗口已无旧 180 点硬上限；全站只保留协议/缓冲区/.NET API/系统外观等逻辑硬上限，同时放开健康因子、波动阈值和 RSSI 阈值的非必要下限。
- 门禁：Vue typecheck、Vitest 50/50、App 构建及编辑器诊断通过。

## 2026-09-09 UI 与安全模式修复
- 安全模式：外部启动根据是否已有实例显示指定英文确认；内嵌壳三次 R/设置入口先确认再异步重启，并传递 `--safemode-confirmed`，新引擎只等待单实例锁释放，不再二次杀壳造成卡顿和新窗口闪现。
- Bubble/弹窗：统一 Bubble 悬停延迟由 3 秒改为 600ms，底栏设备胶囊可及时显示状态；普通 `XDialog` 调用补齐遮罩空白关闭，高级主题确认关闭等价回滚。
- 卡片与布局：心率页设备卡片使用固定宽度横向滑轨、滚动吸附和前后页动画，不再随窄布局压缩；边栏折叠/宽度、顶栏、底栏、壳窗口尺寸/位置/Zoom 以及其他 `hrm-*` 偏好均在启动回填后即时套用。
- 间距：密度防御下限统一为设置滑块下限 0.8，清理历史 0.1 导致的卡片纵向间距近零问题；未改动 range thumb 所需的负 margin。
- 悬浮窗：解锁时右下角提供真实系统缩放尺寸柄；主窗及每设备窗分别持久化逻辑尺寸/位置，锁定时隐藏尺寸柄并维持点击穿透。
- 例行检查：清除 `CommandShell.cs`、`SysInfoService.cs`、WebView2Host `Program.cs/MainForm.cs` 文件头累积的异常 BOM；Windows 源码未再检出 ZWSP/ZWNJ/ZWJ/重复 BOM。
- 门禁：locales 530 keys、Vue typecheck、vitest 50/50、临时 Vite build、App 与 WebView2Host 构建、编辑器诊断全部通过。

## 2026-09-09 最终打磨 P3：GitHub 项目信息、缓存与启动更新提示（plan.md §4.1）
- 新增 `GitHubProjectService`：后台顺序获取 repository、latest release、latest commit 与 Issues Search 总数/open 数（`type:issue` 排除 PR）；数据目录缓存响应体与 ETag，跨进程复用 304，10 秒超时或部分端点失败时保留上一份字段并标记 stale/error，latest 404 作为有效“无 Release”。
- AppHub/REST/WS 暴露项目快照与更新状态，提供 `/api/github` 和 `/api/github/refresh`；GUI 与 CLI 启动均不等待网络，CLI Banner 后输出嵌入发布信息及缓存摘要。
- 更新检查默认开启；严格同时比较 latest tag（忽略单个 `v` 前缀）和 release name；原生 TaskDialog 支持打开发布页、按 `asset_pattern + build_target` 精确下载、复制链接、跳过该 tag/name、关闭，并用原子门保证每进程至多提示一次。
- 双设置页新增更新检查开关，失败后回读服务端；补齐八语言。修复审计发现的缓存字段未序列化、部分失败覆盖旧值、304 丢 ETag、跨架构资产回退和并发双弹。
- 同步修复 P0–P2 中断遗漏：四组件 publish 各自注入版本/统一构建时间并由 `build_target` 选择 RID；组件探针使用真实 5 秒超时且验证 component/exe 身份；`hrmcli` 无参恢复 CLI REPL；自启始终指向主引擎，REST/CLI 按实际注册回写并在前端串行提交/失败刷新。
- 门禁：Vue locales 530 keys、`vue-tsc` 0 error、vitest 50/50、临时路径 Vite build、四个 C# 工程、完整 debug 构建与四组件交叉校验通过；`hrmcli` 实机注册 HKCU Run 验证命令严格为 `HeartRateMonitor.exe --gui --autostart --silent` 并成功清理。

## 2026-09-08 最终打磨 P2：四组件运行时版本校验（plan.md §4.1）
- 新增共享源 `Windows/VersionJson.cs`（链接进四个工程）：`--version-json` 在任何服务/窗口/单实例守卫之前打印组件元数据 JSON 并退出（engine/webui/cli/dump 四入口均接入）。
- 新增 `Core/ComponentVersions`：并行校验四组件（引擎自查 FileVersion，其余三个以管道探针 + 5s 超时）；比较前剥离 .NET SDK 追加的 `+githash` 后缀并忽略尾随 `.0` 段；产出逐组件 Report（存在/探针/声明版本）。
- GUI：业务服务启动前校验，不一致弹原生 TaskDialog（逐组件详情 + 仍然继续/取消/打开仓库/报告问题；报告=复制诊断到剪贴板并打开 issues/new，仓库/报告后重新弹窗等待决定）；Debug 构建只记日志不打断。
- CLI：`CliApp.Run` 启动即校验；不一致打印明细表，交互终端询问 `[Y/n]`，重定向 stdin（脚本调用）写 stderr 并以退出码 1 终止；`CliHost.Main` 改为透传 `Environment.ExitCode`。
- 状态暴露：`App.ComponentsOk`（true/false/null）进 `/api/status` 与 `/api/config` 的 app 段，前端 `AppInfo.componentsOk?`（P7 About 呈现）。
- 实机验证：四探针输出（WinExe 子系统直跑控制台无输出属正常，管道探针不受影响）；故意声明 webui=9.9.9 后构建期交叉校验与运行时 CLI 门均正确报错（明细表+提示+stderr+退出）；恢复 1.0.0 后干净路径零输出 EXIT=0；`+githash` 误报与 CliHost 固定 return 0 两处测试中发现并修复。
- 门禁：4 工程 0 error、vue-tsc 0 error、完整 debug 构建全绿。

## 2026-09-08 最终打磨 P1：启动选项统一、静默模式与多方式开机自启（plan.md §4.1）
- 新增 `Core/StartupOptions`：集中解析 `--gui/--web/--winforms/--cli/--silent/--minimized/--autostart/--startup/--safemode/--reboot/--after-crash/--debug/-d/--trace/--multi` 与一次性命令词；默认 GUI，仅显式 `--cli` 进 CLI，**删除“父进程为控制台则进 CLI”的猜测逻辑**（连同 ProcessInfo 的 NtQueryInformationProcess/GetParentProcessId/ParentIsConsoleShell 一并移除）。
- `--silent`：仅引擎+托盘+必要服务，不调用 LaunchFrontend（Web 服务仍以 openUi:false 启动，托盘/远程路径可用）；`--autostart` 抢锁失败时静默让位（不弹三选窗、不抢前台）。
- 新增 `Core/AutoStartManager`：三种登录自启注册方式 —— Task Scheduler（COM Schedule.Service，当前用户 Logon 触发器 + InteractiveToken，免提权）、HKCU Run、启动文件夹快捷方式（WScript.Shell）；读取**实际系统注册状态**（含路径/参数陈旧检测），Apply 按期望集合增删/重写。
- 配置新增 `app.auto_start_methods`（多选）与 `app.auto_start_silent`（默认 true）；`AppHub.AutoStart` + `/api/autostart` GET/POST（remote user 角色拒绝，机器级设置仅管理员）；设置页双版新增「登录自启动」卡（新组件 `XMultiSelect` 多选下拉 + 共享 `AutoStartCard`，含状态/陈旧提示/刷新），12 个新 key ×8 语言。
- CLI 新增 `autostart [on|off] [task,run,startup] [silent|nosilent]` 命令（Text.cs 7 个 key，主表四语）。
- 实机验证：run/startup/task 三方式注册、多选切换互斥、陈旧检测（指向旧目录提示修复）、off 清理（注册表/快捷方式/计划任务全部实际移除）；`--autostart` 抢锁 858ms 静默退出 code 0；`--silent` 引擎存活且 0 前端壳、9460 可达；dotnet/vue-tsc/vitest 50/50 全绿。
- 测试中发现并修复：`autostart off` 初版误把全部方式当期望集合重注册，改为“off 移除列出方式（无列表=全部），不动其余”。

## 2026-09-08 最终打磨 P0：发布清单单一来源 + 默认配置内建（plan.md §4.1）
- `Windows/Release.json` 扩展为唯一发布元数据入口：新增 `project_name`、`repo_name`、`author`、`author_url`、`release_name`、`build_note`、`build_target`（x64）、`asset_pattern`、`components.{engine,webui,cli,dump}`；保留 MIT 注释头与既有字段。
- 四个 csproj 删除各自 `<Version>`；构建统一以清单版本注入 `-p:Version`，并把实际 UTC 构建时间以 `-p:ReleaseBuildTimeUtc` 写入 AssemblyMetadata。
- App 与 hrm-webui 把仓库 Release.json 作为嵌入资源编译进程序集（LogicalName）；`ReleaseManifest` 改为「嵌入资源优先、exe 旁文件兜底」，新增全部新字段与 `Icon(role)`/`ComponentVersion(role)` 查询；TrayHost 与 WebView2Host 的图标解析同步改走嵌入清单。
- `build.ps1`：清单必填字段与四组件版本缺失即失败；releases/standalone 下任一组件 publish 失败立即失败（debug 仅警告）；构建后读取四个 EXE 的 FileVersion 与清单交叉校验（尾随 .0 归一化）；release 模式产出 `asset_pattern` 命名的 ZIP + SHA-256 sidecar；产物不再复制 config/config_webhook/Release.json。
- `AppConfig`：新增 `schema_version`（当前 2，旧文件 Load 时原地升级）；`Save()` 改为临时文件 + `File.Replace` 原子替换并保留 `.bak`，失败回退直写。
- `AppBoot`：`WebhookPath` 归属修正到数据目录（原字段默认指向程序目录，自定义数据目录下 webhook 配置丢失）。
- 前端去重：`about.ts` 缩减为本地头像资产；About 页仓库/作者/项目名全部消费后端嵌入清单（`ReleaseInfo` 扩展 14 字段含 components）。
- 验证：4 个 C# 工程 0 error；vue-tsc 0 error；vitest 50/50；完整 debug 构建全绿，四 EXE 版本 1.0.0.0 与清单交叉校验通过；产物无 config/Release.json；`hrmcli about` 在无外部清单的产物目录正确读取嵌入清单（含注入的构建时间与 license/repo 行）。

## 2026-09-08 最终打磨：正常退出收束崩溃守护进程
- 主程序现在持有本实例启动的 `hrmdump` 进程句柄，避免启动后立即丢失所有权。
- 托盘/GUI 正常关闭、Console `exit --force` 与 `reboot` 路径都会显式停止对应 watchdog；异常崩溃路径不执行该清理，仍由 dumper 负责通知前端或写入崩溃记录。

## 2026-09-02 第一轮：V1 重构（详见 V1 存档版 diff.md）

## 2026-09-02 第二轮：迁回 C# Test + UI 重排 + CLI/自检 + DLL 缩减

### 步骤 7：目录迁回
- 用户自行重组了上级目录（`C# Test/` 被清空、`OldPy/` 移到上级、新建空 `Old/`、`Example/` 并入 `C# V1/`）。
- 将 V1 工作文件从 `C# V1/` 移回 `C# Test/`：`App/`、`Engine/`、`Built/`、`build.bat`、`build.ps1`、`config.json`、`config_webhook.json`、`about.md`、`plan.md`、`diff.md`；旧 Python（`Frontend/`）与旧解耦 C#（`Backfront/`）作为归档保留在 `C# V1/`。
- 成功删除 `C# Test/` 中被终端 cwd 锁定的空 `Backfront` 壳目录。

### 步骤 8：UI 重写（MainForm.cs）
- 原布局"异常紧凑"，OSC 模板无法多行编辑。重排为宽松布局：
  - 窗体 1180x780（最小 1000x680），各页签 Padding=12；
  - OSC 页：连接区改宽松表格（行高 30、列间距 12），模板编辑框 `Multiline + AcceptsReturn + AcceptsTab + WordWrap`（可多行换行），右侧预览/统计/测试；SplitContainer 分隔。
  - 心率页：左=设备列表+连接控制；右=心率监控（大字号）/已连接设备/API+Webhook/悬浮窗（格式/颜色/图片分列）。
  - 新增 `_webhookEnabled` 开关并写入配置。

### 步骤 9：CLI 模式（--cli / 控制台拉起自动进入）
- 新增 `App/ProcessInfo.cs`：`GetConsoleWindow` / `NtQueryInformationProcess`（父进程 PID）/ 父进程为控制台 shell（cmd/powershell/terminal 等）判定 / `AttachConsole+AllocConsole`。
- 新增 `App/CLI/CliApp.cs`：命令循环 `help/status/info/scan/devices/connect/disconnect/bpm/osc/send/config/selftest/exit`；服务事件输出到控制台。
- `Program.cs` 模式判定：`--cli` 强制 CLI；`--gui` 强制 GUI；否则父进程为 shell → CLI（满足"控制台拉起自动进入 CLI"）。
- 修复：WinExe 重定向 stdout 时 `EnsureConsole` 不申请控制台（避免覆盖重定向句柄）。

### 步骤 10：CLI 自检（SelfTest.cs）
- 10 步模拟真实用户：config 加载 → 引擎加载 → OSC 接收启动 → BLE 扫描 → 发现 `D4:DA:C6:CE:5F:D6` → 连接 → 首条心率 → OSC 推送 → 硬件 fast → 注册表采集；记录每步延迟，阻塞型操作 ≤ 250ms。
- 实测（HUAWEI Band HR-FD6 真机）：全部 PASS，最长阻塞 106ms ≤ 250ms；设备 1.3~4.1s 发现、~2.5s 连接、0.6s 首条心率（72/80/81 bpm）。
- 修复（C 引擎）：接收 socket 无 SO_RCVTIMEO 时，`StopReceiver` 的线程 join 阻塞最长 2s → 超 250ms 预算。加 `SO_RCVTIMEO=100ms` 后启动/停止均 ≤ ~110ms。

### 步骤 11：缩减 DLL（PublishSingleFile）
- build.ps1：standalone = `--self-contained true + PublishSingleFile + IncludeNativeLibrariesForSelfExtract`；releases = `--self-contained false + PublishSingleFile`；debug 保持多文件。
- 移除 csproj 中 `osc_engine.dll` 的 Content 项（单文件会把 Content 打进 bundle，P/Invoke 找不到）；改由 build.ps1 发布后单独复制。
- 结果：standalone/releases 输出目录 **4 个文件**（exe + osc_engine.dll + config + webhook），从 200+ DLL 缩减；standalone 自包含 exe 132MB。
- 摘要新增 `dll count` 输出。

## 2026-09-02 第三轮：UI 重排 + 设备行操作 + 悬浮窗修复

### 步骤 13：UI 再次重写（MainForm.cs 全量）
- 窗体 1240x820（最小 1060x700），全局字体 Microsoft YaHei UI 10/10.5，页签 Padding 16，控件显式高度 34。
- 全部按钮统一工厂（FlatStyle.Flat、白字、34px 高、间距 4）；Label 工厂统一左边距。

### 步骤 14：设备列表行式化（DeviceRowControl.cs）
- 原 ListView 无法选择/连接（连接按钮 Enabled 未随选择更新）。重做为行式控件：
  - 每行：复选框 + 两行信息（名称/MAC·类型·RSSI·bpm）+ 行内按钮 **连接(已连接)/断开/保存(删除)/屏蔽**；
  - 顶部批量工具栏：**连接所选 / 断开所选 / 反选 / 保存所选 / 屏蔽所选**（作用于勾选行）；
  - **保存**：MAC 写入 `config.json` 的 `heart_rate.devices`，已保存显示"删除"；
  - **屏蔽**：MAC 写入 `heart_rate.blocked`，扫描结果不再显示该设备（DeviceFound 过滤）。
- 设备行新发现置顶，随面板宽度自适应（Resize 更新行宽）。

### 步骤 15：悬浮窗修复（FloatingWindow.cs）
- 字号缩放上限：bpm 字号 clamp(h*45%, 12, 72)、文本 clamp(h*24%, 9, 36) —— 避免放大到吃满屏幕；
- 默认几何从 config `heart_rate.window.geometry`（"200x80+100+100"）解析并定位，不再 0 尺寸/不可见；
- 最小 120x40，`_flow` 加内边距。

### 步骤 16：验证
- 三分支构建通过（debug 9 文件 / releases、standalone 各 4 文件）；
- GUI 冒烟：新 UI 主窗体加载、引擎/4 采集方法正常、无异常；
- CLI selftest 复测 **PASS**：HUAWEI Band HR-FD6 71 bpm，发现 0.9s、连接 2.4s，最长阻塞 98ms ≤ 250ms。

## 2026-09-02 第四轮：SolidJS Web 前端（替换 WinForms 主界面）

> 背景：WinForms UI 连续三轮被反馈"异常紧凑、几乎无法操作"。决定改用 **SolidJS Web 前端**（浏览器渲染，天然宽松可缩放），C# 作为本地后端引擎，通过本地 HTTP + WebSocket 提供数据。

### 步骤 17：C# Web 后端（App/Web/WebServer.cs 新建）
- HttpListener 静态托管 `webui/`（SolidJS 构建产物）+ REST API + WebSocket 推送，端口默认 8228（config `web.port`）。
- API：`/api/status`（全量快照）、`/api/devices`、`/api/device/{connect|disconnect|save|block}`、`/api/devices/batch`、`/api/scan`、`/api/osc/{connect|config|test}`、`/api/hw[/refresh]`、`/api/logs[/clear|dump]`、`/api/webhooks`（add/update/delete/test）、`/api/settings`、`/api/config`、`/api/float/{open|close_all|lock}`、`/api/shutdown`。
- WebSocket `/ws` 推送：`device_found / heart_rate / connected / disconnected / devices / scan / osc_status / sysinfo / log`；发送按全局锁串行化（WS 同 socket 并发发送非法）。
- 屏蔽名单在 DeviceFound 事件过滤（与旧 UI 行为一致）。
- 悬浮窗迁移：`UI/FloatWindowHost.cs`（静态管理器，UI 线程执行），WebServer 用 `UiInvoke` 调度跨线程。

### 步骤 18：托盘宿主 + 模式路由（UI/TrayHost.cs、Program.cs）
- GUI 默认改为 Web 模式：启动 WebServer → 打开默认浏览器 → `Application.Run(TrayHost)`（隐藏窗体 + NotifyIcon：打开界面/悬浮窗/锁定/退出）。
- `--winforms` 保留旧 WinForms MainForm 作为回退；`--cli`/控制台拉起 → CLI 不变。
- 退出链：Web `/api/shutdown` → Application.Exit → TrayHost.OnFormClosed 停止 Web/OSC/BLE。

### 步骤 19：SolidJS 前端（WebUI/）
- 技术栈：Vite 6 + solid-js 1.9 + 手写 CSS（无重型依赖）；`base: "./"`，hash 导航（无路由库）。
- 布局：左侧 236px 侧边栏（品牌 + 7 页导航 + 实时通道状态）+ 顶栏（心率 pill/刷新/退出）+ 主内容（Padding 28px，卡片半径 12、内边距 24）。
- 页面：仪表盘（BPM 大数字 + 统计卡 + 设备列表）、设备（扫描 + 批量工具栏：连接所选/断开所选/反选/保存所选/屏蔽所选 + 行内 复选框/连接/断开/保存(删除)/屏蔽）、OSC（连接表单/模板多行编辑/变量实时预览/测试发送）、硬件（筛选/搜索/表格）、Webhook（CRUD 弹窗）、日志（实时过滤/清除/转储）、设置（悬浮窗样式 + 关于）。
- 状态：模块级 createStore + WS 事件增量更新（心率就地更新设备行、设备列表防抖刷新、日志限 1200 条）。
- 关键修复：项目路径含 `#`（"C# Test"）导致 Vite 模块 id 被当 URL 片段解析失败 → `WebUI/build-webui.ps1` 把 src/config 复制到 `%TEMP%\hrm-webui`（无 `#`），node_modules 用目录联接复用，在临时根构建后把 dist 交给发布脚本。
- config 新增 `web` 段（port/open_browser）。

### 步骤 20：build.ps1 集成前端
- 构建顺序：gcc 引擎 → `WebUI/build-webui.ps1`（vite build）→ dotnet publish → 拷贝 config/引擎/`webui/`（dist → 输出目录）。
- 支持 `-SkipWeb`（后端调试快速迭代）。
- 摘要新增 `webui` 行。

### 步骤 21：验证（第四轮）
- 三分支构建通过：`debug-20260902_040320` / `releases-20260902_040808` / `standalone-20260902_040824`（均含 webui/，顶层 4 文件 + webui）。
- Web 冒烟（debug --gui）：`/`、`/api/status`、`/api/config`、`/api/hw` 全部 200；POST 扫描启动成功（发现 5 台真机 BLE 设备）；批量保存写入 config.json 生效；WebSocket 收到 `device_found/sysinfo` 推送。
- 无头 Edge 渲染验证：`--headless=new --dump-dom` 输出侧边栏/导航/顶栏完整渲染，`ws-dot on`（实时通道已连接），无 JS 控制台错误。
- CLI selftest 复测 **PASS**：HUAWEI Band HR-FD6 82 bpm，最长阻塞 51ms ≤ 250ms（Web 改造未影响 CLI）。

### 步骤 22：前端类型修正 + 三分支重建（收尾）
- TS 诊断批量修复：`createSignal(() => ...)` 改先算初始值再传入（App.tsx）；Signal 访问器调用补 `()`（Devices/OscPage/Webhooks）；`setState` 用 `prev.map`；api.ts 补齐泛型返回（config/logsDump/webhookAction）；JSX 字符串字面量 `{"{变量名}"}`。
- 剩余 `Cannot find module './pages/...'` / `api.config` 缺失均为 TS Server 陈旧缓存——vite 构建 18 模块全部解析成功（esbuild 权威验证），无真实错误。
- 三分支重建：`debug-20260902_042109` / `releases-20260902_042116` / `standalone-20260902_042120`（均含 SolidJS webui，新 CSS 11.32 kB / JS 49.88 kB）。
- 复验：CLI selftest **PASS**（10 项全过、最长阻塞 59ms ≤ 250ms、真机 HUAWEI Band 74 bpm、OSC 推送成功）；Web 模式（--gui）`/`、`/api/status`、`assets CSS` 全部 200；无头 Edge 渲染 sidebar/品牌/导航/ws-dot 正常。


### 步骤 23：Rust + egui 本地前端（第五轮，默认 UI）
- 需求：`使用Rust + EGUI来制作本地前端。保留Web，但只有在添加--web或在前端手动开启时才启动web服务`。
- **C# 侧**：
  - 新增 `Core/AppHub.cs`：共享业务核心——订阅服务事件（device_found/heart_rate/connected/disconnected/scan/osc_status/sysinfo/log）统一分发；实现全部命令（status/devices/scan/connect/disconnect/save/block/batch/osc_*/hw/logs*/webhooks/settings/config/float_*/web_*/shutdown）。Web 与 IPC 两个传输层共用，消除业务逻辑重复。
  - 新增 `Ipc/PipeServer.cs`：命名管道 `\\\\.\\pipe\\hrm_v1`，UTF-8 JSON 行协议（请求带 `id`、响应回显 `id`、推送 `{"event","data"}`），多客户端。
  - 新增 `Web/WebHost.cs`：Web 按需启停宿主（Running/Url）；`--web` 或前端手动开启才启动，已运行时再次 Start 改为打开浏览器。
  - 重构 `Web/WebServer.cs`：业务全部委托 AppHub，自身只做 HTTP/WS 传输；`/api/config` 现在由 Hub 附加 `web.running/port`。
  - `Program.cs`：GUI 默认启动 hub + pipe，`LaunchUi()` 拉起 hrm-ui.exe；`--web` 才 `webHost.Start()`；`--winforms` 仍回退旧窗体。
  - `TrayHost.cs`：接入 hub/pipe/webHost，注册 `AppHub.UiInvoke`；菜单新增「Web UI 开关」。
  - `ProcessInfo.cs`：新增 `LaunchUi()`（同目录 hrm-ui.exe，UseShellExecute 分离启动）。
- **Rust 侧（RustUi/，cargo 1.98 + eframe 0.31）**：
  - `src/client.rs`：命名管道客户端（std 文件 API 打开 `\\.\pipe\hrm_v1`），后台线程读 JSON 行；`send_cmd` 带自增 id 同步等匹配响应，期间事件暂存。
  - `src/state.rs`：AppState + 事件应用 + status/config/hw/logs/webhooks 响应解析。
  - `src/main.rs`：eframe 应用——侧边栏导航（7 页）+ 顶栏（心率 pill/刷新/退出）+ 中文 CJK 字体（msyh/simhei 候选加载）+ 宽松间距（interact 高 34px）。页面：仪表盘（BPM 大字 + 统计卡）、设备（扫描/批量工具栏/行内 连接断开保存屏蔽）、OSC（表单+模板+测试）、硬件（vars 表格）、Webhook（CRUD 弹窗）、日志（过滤/清除/转储/自动滚动）、设置（悬浮窗样式/Web UI 开关/关于/退出确认）。断线 1s 自动重连。
- **build.ps1**：步骤 1c `cargo build --release`；3c 拷贝 `hrm-ui.exe`；摘要新增 `hrm-ui` 行。
- **验证**：C# 编译通过；Rust release 构建通过；debug/releases/standalone 三分支均含 hrm-ui.exe + webui/。
  - 默认 `--gui`：进程 = 引擎 + hrm-ui（自动拉起），**8228 关闭**（Web 默认不启动）；管道 status/config/devices 命令全通；scan 发现 4 台真机设备。
  - `web_start` → 8228 LISTENING + `/api/status` 200；`web_stop` → 关闭。
  - `--gui --web` → 启动即 8228 监听 + API 200。
  - `shutdown` 命令 → 引擎干净退出；hrm-ui 重启后自动重连管道。
  - CLI selftest **PASS**：HUAWEI Band 74 bpm、最长阻塞 53ms ≤ 250ms（WebServer 重构未影响 CLI）。

### 步骤 24：卡顿修复 + Web 启停 + `--web` 判定 + trace 模块（第六轮）
- 需求：`1:: 点击部分前端控件后出现严重卡顿(如设备扫描、设置)` / `2:: 点击启动Web服务后程序立即退出且不再启动` / `3:: 在cmd中使用--web启动时程序跑完检测直接退出` / `4:: 创建trace模块加入程序的所有部分(仅在debug构建模式下被用到)用于诊断卡顿原因`。
- **Rust 侧（问题 1 主因）**：`client.rs` 弃用后台读线程。Windows 同步管道句柄上的 I/O 由内核串行化，后台线程挂起的读会阻塞 UI 线程的写 → 每次点按冻结 1~2s。改为单线程 + `PeekNamedPipe` 轮询（有数据才读，永不留挂起读）；`main.rs` 加 `ctx.request_repaint_after(33ms)` 限帧，避免持续满速重绘。文件头写入注释说明"为何不用读线程"，防止后人改回。
- **C# 侧（问题 1 次因 + 问题 2/3）**：
  - `Ipc/PipeServer.cs`：accept 循环 + **每客户端 `BlockingCollection` 写队列**（`QueueLimit = 2000`，积压超限踢该客户端），广播只入队不做 I/O —— 慢客户端不再拖住事件源线程。
  - `Web/WebHost.cs` 启停加固：启动异常不再冒泡到 UI 线程终结进程；已运行时再次 Start 改为打开浏览器（问题 2）。
  - `Program.cs`：`forceGui` 判定补上 `--web`（原先只认 `--gui`/`--winforms`）→ 从 cmd 启动 `--web` 时被误判为 CLI、跑完检测即 `ReadLine()==null` 退出（问题 3）。
- **trace 模块（问题 4，双侧、release 零开销）**：
  - 新增 `App/Core/Trace.cs`：`HrmTrace.Event(tag, msg)` / `HrmTrace.Perf(tag, elapsedMs, warnMs = 50)`，`[Conditional("DEBUG")]` 使 release 构建下调用点被编译器整体剔除；加锁追加写 `logs/trace.log`，行含自增序号 + `HH:mm:ss.fff` + 线程号。`Perf` 仅在超过阈值时落一行，平时无噪声。
  - 新增 `RustUi/src/trace.rs`：`#[cfg(debug_assertions)]` 真实现追加写 `ui_trace.log`（`{启动后ms} {tag} {msg}`），`#[cfg(not(debug_assertions))]` 空实现。
  - 埋点：C# 25 处（`AppHub` 8 / `PipeServer` 7 / `SysInfoService` 6 / `BleManager` 2 / `OscService` 1 / `WebServer` 1）；Rust 4 处（`cmd` / `events batch` / `frame` / `boot`）。
  - 修复：自定义类原名 `Trace` 与 `System.Diagnostics.Trace` 冲突（CS0104）→ 改名 `HrmTrace`。
- **验证**：三分支重建通过；trace 日志显示命令 RTT 回落至毫秒级，扫描/设置点击无冻结；`--web`（cmd 启动）常驻不退出、8228 监听；Web 反复启停正常。

### 步骤 25：悬浮窗三问题（第七轮：IPC 字段冲突 / 多窗 / 高 DPI）
- 需求：`1:: 点击打开悬浮窗后命令超时` / `2:: 无法为每个设备单独打开悬浮窗` / `3:: 悬浮窗打开后默认是半锁定状态 无法移动但顶栏也没隐藏`。
- **问题 1 —— 协议字段撞名**：`float_open` 用 `id` 传窗口标识，与协议层请求序号 `id` 同名 → Rust `send_cmd` 的 `c["id"] = json!(seq)` 覆盖窗口标识；C# 侧 `S("id")` 对整型调 `GetValue<string>()` 抛异常，且错误响应不带 `id` → 前端无法认领响应，死等至 3s 超时。
  - 修复：窗口标识改名 **`win`**（`PipeServer.cs` `case "float_open"` 取 `S("win")`，Rust `main.rs` 两处发 `("win", ...)`）；`Handle` 的错误分支**必须回显请求 id**；`S(key)` 改为对任意 JSON 类型容错取值。
- **问题 2 —— 按设备多窗**：`UI/FloatWindowHost.cs` 由单窗改 `ConcurrentDictionary<string, FloatingWindow>`；主窗键 `MainId = "__main__"`，设备窗键为 MAC；`TitleOf` 用设备名、回退 `心率-{id}`；新窗按 `offset = Windows.Count * 28` 错开位置；已存在则复用并前置。Rust 设备行新增「悬浮窗」按钮传该行 MAC，设置页「打开主悬浮窗」传 `"__main__"`。
- **问题 3 —— 高 DPI 客户区归零**：系统 DPI = 288（300%）。原 `ApplyGeometry` 直接赋 `Width/Height = 200/80`，`AutoScaleMode.Font` 又二次改写尺寸，实测窗口 505x103、**客户区 469x0** —— 非客户区（标题栏+边框按 288 DPI 放大）把客户区吃光，于是"看着像有顶栏但内容不可见、也拖不动"。`FloatingWindow.cs` 三处修复：
  - `ApplyGeometry` 改设 **`ClientSize`** 并乘 `DeviceDpi / 96.0`（`Location` 仍用配置坐标）；
  - `MinimumSize` 同样按 DPI 缩放（`120x40 → * DeviceDpi/96`）；
  - `ToggleLock` 切 `FormBorderStyle` 后**复位 `ClientSize`**，并用切换前后 `PointToScreen(Point.Empty)` 的差值回填 `Location` 补偿客户区原点漂移；
  - `UpdateFonts` 先把客户区高度折算回逻辑像素（`* 96 / DeviceDpi`）再做 clamp(45%,12,72) / clamp(24%,9,36)。
  - 另：`ToggleLock` 必须在 `Show()` **之后**调用（`ApplyClickThrough` 依赖 `IsHandleCreated`）—— `FloatWindowHost.Open` 与 `MainForm.OpenFloating` 两条路径统一调整。
  - 透明与穿透：`TransparencyKey` 由 WinForms 自行维护 `WS_EX_LAYERED`，只切换 `WS_EX_TRANSPARENT`（手动清 LAYERED 会破坏色键透明）；`FormBorderStyle` 变化会重建句柄 → 在 `OnHandleCreated` 重新套用穿透位。
- **验证**（`debug-20260902_083946` / `releases-20260902_084940` / `standalone-20260902_085104`）：
  - 问题 1：11 条命令零超时，`float_open` RTT 40ms / 0ms。
  - 问题 2：`floatCount` 1 → 3 → 3（复用不新增）→ 0；三窗标题 `心率` / `心率-AA:BB:CC:DD:EE:01` / `心率-AA:BB:CC:DD:EE:02`，位置错开 33/43/52。
  - 问题 3：客户区 **469x0 → 600x240**；解锁态三次拖动位移 `(120,90)/(-70,-50)/(-80,-60)` 与目标完全一致；锁定态命中测试 `rootIsForm=False`（穿透生效）、`move=(0,0)`、窗口尺寸 = 客户区尺寸（顶栏已隐藏）；双态截图视觉确认。
  - 诊断脚本踩坑：`WindowFromPoint == form` 恒 False（命中子控件）→ 改 `GetAncestor(h, GA_ROOT)` 比较；PowerShell 5.1 按 ANSI 解码 BOM-less `.ps1` → 脚本保持纯 ASCII、中文标题用码点拼装。
  - CLI selftest 回归（10 步全 PASS，真机 HUAWEI Band HR-FD6）：`releases` 最长阻塞 **96ms**（OSC 接收启动）、发现 432ms / 连接 2946ms / 首条心率 540ms @82bpm；`standalone` 最长阻塞 **59ms**（BLE 扫描启动）、发现 1993ms / 连接 9626ms / 首条心率 780ms @96bpm。均 ≤ 250ms 预算（连接耗时属 BLE GATT 异步等待，不计入阻塞预算，未超 20s 超时）。
  - selftest 跑法勘误：`selftest` 是 CLI 内命令而非 argv，须 `@("selftest","exit") | & .\HeartRateMonitor.exe --cli`，并先设 `$OutputEncoding = [System.Text.Encoding]::ASCII`（否则 stdin 的 UTF-8 BOM 被按 GBK 解成 `锘縮elftest` → 未知命令）。


### 步骤 26：V2 Phase 1 — 11 Tab 骨架 + 多语言 + 主题（第八轮起步）
- 需求：`重写UI逻辑但保留目前风格` / `添加多语言支持` / 11 Tab 分类（OSC/Pusher/HeartBeat/Devices/HWInfo/HeadSet/API Server/Settings/Logs/Console/Monitor）/ `先计划本地前端再计划Web前端`。
- **准备**：`Backup/20260902_104002/`（robocopy 整树，排除 Built/target/node_modules/obj/dist/build 输出除外即 App/bin，共 300 文件 / 182.85MB）；旧 `plan.md` 归档为 `Archives/plan1.md`；重写 `plan.md`（新 V2 蓝图：核心/技术决策/11 Tab 职责/分阶段大纲/收尾流程/目录约定）。新建 `HeadSet/`（WIP 占位）。
- **Rust 前端**：
  - 新增 `lang.rs`：`Lang`（ZhHant/ZhHans/En/Ja/Es）+ `tr(lang,key)` 翻译表（Tab 标签/顶栏/导航/设置文案；繁以台湾习惯）。
  - 新增 `themes.rs`：`Theme`（Dark/Light/Forest/Sunset/Custom）+ `apply(ctx,theme,custom)`，含 `CustomPalette`（强调色/背景/面板）。
  - `main.rs`：`Page` 扩为 11 项（默认落地=HeartBeat），`label(lang)` 接入 `tr()`；新增 `page_osc_monitor`（OSC 接收监视 WIP）、`page_pusher`（原 OSC 模板/连接/测试 + Webhook 合并）、`page_console`（内建 CLI：help/status/devices/hw/logs/config/webhooks/scan 复用后端命令回显 JSON）、`page_placeholder`（HeadSet/API Server/Monitor WIP）；设置页顶部新增**语言+主题**分组，变更即保存。
  - `state.rs`：`AppState` 增 `lang/theme/health_status/hr_curve/console_lines`；`heart_rate` 事件填充 `hr_curve`（上限 1800），新增 `health_status` 事件；`parse_config` 读取 `ui.lang/ui.theme`。
- **C# 后端**（最小同步）：`Config.cs` 新增 `UiSection`（`lang`/`theme`）；`AppHub.Config()` 返回 `ui`，`ApplySettings` 处理 `body["ui"]` 持久化。
- **验证**：`cargo check` / `cargo build --release` 干净通过（`hrm-ui.exe` 5.16MB）；`dotnet build -c Debug` 0 警告 0 错误。变化提交产物待三分支构建 + GUI 冒烟后补测。
- 说明：SQLite（持久化）按计划在 Phase 2 随「OSC 实时记录」落地时再引入 `rusqlite`；本 Phase 骨架不新增原生依赖。

### 步骤 27：V2 Phase 2 — SQLite 持久化 + OSC 接收监视（第八轮）
- 需求：`支持实时记录到数据库等载体` / `OSC 回传参数多卡片监视`。
- **C# 后端**：
  - csproj 引入 `Microsoft.Data.Sqlite` 9.0.0；新建 `App/Db/HrmDb.cs`：打开 `App.BaseDir/hrm.db`，建表 `hr_records`/`osc_records`/`health_records`/`variables`（含索引），`volatile bool Recording` 开关，写操作用锁串行化；`InsertHeartRate`/`InsertOsc`/`Count`/`PathOf`。
  - `AppHub.Start()` 调 `HrmDb.Init()`；`OnHeartRate` 与新增的 `OnOscReceived`（订阅 `App.Osc.Received`）在开启记录时入库；`OnOscReceived` 还向 IPC 推 `osc_recv {addr,args}` 事件。
  - 新增 `Record(action)` 命令（start/stop/get，返回 `recording/file/hrCount/oscCount`）；`PipeServer` 增加 `case "record"`。
- **Rust 前端**：
  - `Cargo.toml` 加 `rusqlite = { version="0.31", features=["bundled"] }`（本机 MSVC 编译通过）。
  - `state.rs`：`AppState` 增 `osc_recv: Vec<(addr,args)>`（上限 400）、`recording`、`hr_count`、`osc_count`；新增 `osc_recv` 事件解析与 `parse_record`。
  - `main.rs`：OSC 监视 Tab 改写 —— 顶部 stat 卡（接收/已发/失败/记录/心率记录/OSC 记录）+ 记录开关（开始/停止，即时写 hrm.db）+ 按 `/profile/` 首段分组、显示每地址最后值与出现次数的参数卡片；进入该页自动拉 `record get`。
- **验证**：`cargo check` / `cargo build --release`（hrm-ui.exe 含 rusqlite，11:17 生成）干净通过；`dotnet build -c Debug` 0 警告 0 错误。
- **待后续细化**：`/chatbox/typing` 联动（心率刷新写 1）需 C 引擎 `osc_encode_message` 的 int 编码 P/Invoke（联合体封送），另行实现；本阶段 Pusher 保持 模板编辑/变量预览/测试/Webhook。GUI 冒烟与 CLI selftest 在 Phase 收尾时补测。

### 步骤 28：Console 终端化 + 导航三角指示与滑动动画（第九轮）
- 需求：`1:: 修改Console为类似浏览器终端的形式，把光标和输出融合在一个框内` / `2:: 当前选中的Tab会用红色标注，但这导致红色色块直接把Tab标题给盖住了。改用三角形来指示当前Tab，并在切换Tab时实现上下滑动的动画`。
- **Console 终端化**（`RustUi/src/main.rs::page_console`）：
  - 原「上方输出框 + 下方独立输入框」改为单个 `Frame::group`（填 `extreme_bg_color`）内的 `ScrollArea::stick_to_bottom`：历史输出、`hrm>` 提示符与 `TextEdit::singleline`（`frame(false)` + Monospace + `desired_width(INFINITY)`）同处一框，视觉上等同浏览器终端。
  - 输出按行着色：命令回显用主题强调色，含 `error/失败/超时/未知命令` 用红色，其余灰白。
  - 焦点语义：`console_focus` 标志 + 帧首 `ui.memory(|m| m.focused()).is_none()` 快照 —— 无其他控件占用键盘时光标始终留在输入行；点击终端框任意处（`ui.interact(frame.response.rect, ..)`）重新聚焦；「清空」按钮点击后也把焦点交回。
  - 回车提交：`resp.lost_focus() && ui.input(|i| i.key_pressed(Key::Enter))`。**顺序是关键** —— egui 单行 `TextEdit` 收到 Enter 会主动 `surrender_focus`，若在判定前先调 `request_focus()`，焦点 id 被写回 memory 会使 `lost_focus()` 复位，Enter 就永远读不到。必须先算 `entered` 再抢回焦点。
- **导航三角指示 + 滑动动画**（`main.rs` 侧栏）：
  - 弃用 `SelectableLabel` 的整块高亮底色（红色块盖住标题），改为：`allocate_exact_size(.., Sense::click())` 取整行点击区 + `painter().text()` 手绘标题（选中行文字用强调色），hover 时才画一层 30% 透明底。**不能用嵌套 `Label` + 外层 `interact`** —— 内部 Label 占据区域会吃掉点击（实测点「命令行」顶栏仍停在「心率」）。
  - 选中行记录 `target_y`，`nav_marker_y` 每帧向其收敛 25%（差值 <0.5px 时吸附），未到位时 `request_repaint()`；用 `Shape::convex_polygon` 在侧栏左缘画 8x12 三角。
- **验证**（release 二进制，300% DPI 实机）：
  - Console：自动化发键（`VkKeyScan` 逐字符 + `keybd_event(VK_RETURN)`）输入 `help` 后回车 → 终端框内出现 `hrm> help` 与 8 行帮助，末尾新提示符 `hrm>` 等待输入；`abc` → 红色 `未知命令: abc`。
  - 导航动画：像素扫描连拍确认三角 Y 在点击后由 1114 → 1590（心率→命令行）、632 → 734 逐帧移动后稳定，即缓动生效且终点对齐目标行。
  - `cargo build --release` 干净（仅 trace.rs 3 个既有 unused import 警告）。
- **踩坑**：`cargo` 在 `hrm-ui.exe` 被占用时报 `failed to remove file ... Access is denied (os error 5)`，而 PowerShell 会把 cargo stderr 记为 `NativeCommandError`，容易误判为构建成功而实际测的是旧二进制 —— 构建前必须 `Stop-Process -Name hrm-ui` 并核对 exe 的 `LastWriteTime`。

### 步骤 29：下拉框选中指示 + 二元开关合并 + Phase 3~5（第十轮）
- 需求：`1:: 语言切换的指示器使用的也是红色色块 将当前选择盖住了 使用下拉选择框+三角指示器修改。` / `2:: 所有以布尔二元形式为状态的开关(如开始/停止扫描、锁定/解锁浮窗)都合并为一个按钮，根据当前状态决定按钮显示的文字和行为。` / `3:: 随后在本轮直接完成至Phase5并运行一次调试构建。`

**A. 选中指示统一（需求 1）**
- `main.rs` 新增三个复用件：`triangle()`（画实心三角）、`combo_item()`（下拉项 = 左侧三角 + 手绘文字，选中行文字用强调色）、`toggle_button()/toggle_small()`（二元按钮）。
- Settings 的语言/主题从 `selectable_label` 横排改为 `ComboBox::from_id_salt(...).selected_text(...)`，弹出项用 `combo_item`。侧栏导航三角改为调用 `triangle()`，消除重复代码。
- 同样换成"下拉 + 三角"的还有：HeartBeat 多设备的绘制设备选择、导出的数据表/格式、HWInfo 自定义变量类型。

**B. 二元开关合并（需求 2）**
- 扫描（开始/停止）、记录（开始/停止）、OSC（连接/断开）、悬浮窗（锁定/解除锁定）、Web UI（启动/停止）、Webhook（启用/停用）、设备行的连接/断开与保存/移除 —— 全部合并成单个按钮，文字与行为由当前状态决定，`▶`/`■` 前缀提示动作方向，开启态用强调色。
- 点击后本地状态就地翻转（不等下一次事件推送），避免按钮文字闪回。

**C. Phase 3 — HeartBeat（心率 + 健康状态）**
- 新增 `App/Health/HealthService.cs`：心率滑动窗口（最近 5 分钟，判定用最近 30 秒均值抗噪）+ OSC 姿态（`/avatar/parameters/AFK`、`Seated`、`VelocityMagnitude`）→ 睡眠/静息/活跃/兴奋，写入 `{HEALTH_STATUS}` 变量并推 `health_status` 事件。
  - 校准：`StartCalibration()` 前 30 秒采基准、后 10 秒采对照，均值按 0.7/0.3 加权得 `RestingBpm` 与 `RestingSd`，持久化到 `config.json`。未校准时退化为绝对区间粗判且不报睡眠。
  - 波动告警：相邻样本跳变 ≥ `SpikeDelta`（默认 25）时 `App.Log.Warn` 一次，60 秒内不重复（`Logger` 补 `Warn` 级别）。
  - 坐姿修正：`Seated` 且未达兴奋阈值时把"活跃"回落为"静息"。
- 前端 `page_dashboard` 重写：
  - 单设备 = 顶部横长条，左侧实时心率（约 1/4）+ 右侧曲线（3/4），中间 8px 分隔条可拖（`Sense::drag()` + `drag_delta().x / avail`，比例夹在 0.15~0.6）。
  - 多设备 = 左侧等宽列表（名称/信号/BPM/报告频率）+ 右侧曲线，右上角下拉选"平均"或指定设备。
  - 曲线 `draw_curve()`：自适应纵轴（上下各留 8 BPM）、4 等分网格、半透明填充、末点圆点 + 当前值标注。`smoothed()` 做滑动平均（平滑度 1~30）并只取最近 N 点（30~1800）。
  - 其余卡片：健康状态（含校准按钮、倒计时、AFK/Seated/Velocity、高级阈值编辑）、曲线参数、悬浮窗管理（并入 HeartBeat）、数据导出。
- 新增 `App/Core/Exporter.cs`：JSON / YAML（手写最小序列化）/ CSV / SQLite 整库副本，输出到 `exports/`；`HrmDb` 补 `InsertHealth`、`Recent(table, limit)`（表名白名单防注入）、`SetVariable`。
- 后端新增命令：`health`（get/calibrate/cancel）、`health_config`、`export`。

**D. Phase 4 — Devices**
- 新增 `App/Ble/DeviceRegistry.cs`：
  - 标识符缓存：只在拿到非空广播名时更新 `Entry.Name`，后续拿不到名字沿用缓存。
  - 刷新节流：同一设备 `RefreshThrottleMs`（默认 400ms）内只上报一次，抑制列表抖动。
  - 排序打分 `Score()`：已连接 +10000 / 已保存 +4000 / 广播含心率服务 +3000 / 有标识符 +800 / 品牌白名单（apple/xiaomi/redmi/huawei/garmin/polar/honor/amazfit/band…）+600 / 历史连接 +300 递减；耳机（airpods/pods/buds/enco…）-1200、智能家居（midea/mesh…）-900；RSSI 映射 0~60；久未出现按秒衰减。`AppHub.AllMacs()` 改按此分数排序。
  - 别名：`Aliases[mac]`，`DisplayName()` 在所有出站 JSON 上生效（列表/事件/悬浮窗标题）；前端双击设备名进入行内编辑，Enter 提交。
  - 自动重连：非人为断开触发，按 `ReconnectIntervalSec` 重试（扫描未开时自动开启），`ReconnectGiveUpMin`（默认 30 分钟）后放弃并 Warn。
  - RSSI 衰减度 > `RssiWeakThreshold`（默认 100）打印信号弱日志，每设备 5 分钟一次。
  - 报告频率：指数滑动平均得 `reportHz`，前端等宽列显示。
- 前端 Devices 从卡片列表改为 8 列等宽 `Grid`（`min_col_width(70)`），列宽不随刷新跳动；顶部「高级设置」折叠出扫描/重连策略表单。
- 后端新增命令：`rename`、`devices_config`；`scan` 响应带 `scanning`，持续扫描模式不再传 30 秒时长。

**E. Phase 5 — HWInfo**
- 新增 `App/SysInfo/PdhCollector.cs`（按 `Skills/PerfGet.skill`）：PDH P/Invoke（`PdhOpenQuery`/`PdhAddEnglishCounter`/`PdhCollectQueryData`/`PdhGetFormattedCounterValue`，`PDH_FMT_DOUBLE`）长生命周期查询，首帧作基线。计数器：`% Processor Utility`、`% Processor Performance`、`Processor Queue Length`、`System\Processes`、`System\Threads`、`Context Switches/sec`、`PhysicalDisk % Disk Time`。**睿频** = 注册表 `~MHz` × `% Processor Performance`（实测标称 3686 MHz → 实时 5336.7 MHz）。
- 新增 `App/SysInfo/VarEngine.cs`：
  - 精度控制：`UseFloat` / `Decimals` / `Round`（四舍五入 vs 截断），`SetNumber()` 统一走格式化并追加单位。
  - 时间变量：`TIME_LOCAL/TIME_UTC/TIME_ISO/TIME_UNIX/TIME_ZONE/TIME_ZONE_OFFSET/TIME_NTP/TIME_NTP_OFFSET_MS`；NTP 用 48 字节 SNTP UDP 报文，取往返中点抵消单程延迟，每 10 分钟后台刷新。
  - 自定义变量四种：`expr`（自写递归下降四则求值，支持 `+ - * / % ( )` 与一元负号）/ `concat`（仅替换 `{变量}`）/ `regex`（对拼接结果取第 1 捕获组）/ `cmd`（`cmd.exe /d /c` 取 stdout，结果缓存 5 秒、超时 3 秒 kill）。
  - 覆写/单位/改名：`Overrides`（值本身也支持 `{变量}` 与运算）、`Units`、`Renames`；顺序为 时间 → 自定义 → 改名 → 覆写，保证覆写最终生效。
- `SysInfoService`：采集间隔改为可配置（`Hw.IntervalMs`，默认 1000ms，`UpdateInterval()` 热更新）；PDH 可用时不再走 `PerformanceCounter`，`ProcessStats` 跳过重复的进程/线程计数只算句柄；每轮末调用 `VarEngine.PostProcess`。
- 前端 HWInfo 改为 6 列等宽表（变量名/当前值/覆写/单位/别名/操作）+ 关键字筛选 + 行内一键覆写/清除，「高级设置」内含精度设置、覆写表单、自定义变量表单与已有自定义变量列表。
- 后端新增命令：`hw_config`、`hw_var`（override/unit/rename/custom/remove）。
- Console 命令集同步扩充：`hw [过滤]`、`record`、`health`、`export`、`rename`、`var`。
- `Config.cs` 新增 `Devices` / `Health` / `Hw` 三段与 `CustomVar`；`WebServer` 补齐 11 个对应 REST 端点（与 IPC 共用 AppHub，Phase 11 Web 前端可直接调用）。
- `lang.rs` 重构为表驱动（`key → [繁, 简, En, Ja, Es]` 单表），key 从 26 个扩到 ~110 个，覆盖新增全部界面文案。

**验证**（`build.ps1 -Mode debug` 产物 + 真机双设备 HUAWEI Band HR-FD6 / Xiaomi Smart Band 9 Pro）
- 构建：`cargo build` 0 error（仅 trace.rs 3 个既有 unused import 警告）、`dotnet build -c Debug` 0 warning 0 error、`build.ps1 -Mode debug` 全过（gcc + vite + cargo + publish）。
- 截图确认：语言/主题下拉展开后当前项为「红三角 + 红字」无遮挡；HeartBeat 单/多设备布局与曲线正常（双设备时列表 + 平均曲线）；Devices 8 列等宽含报告频率；HWInfo 168 个变量 + 高级设置三段表单；Console `help` 列出 13 条命令。
- IPC 逐命令实测：`record start` → `recording:true`；`hw_var custom`（`{CPU_FREQ_MHZ}/1000` + 单位 ` GHz`）→ `CPU_GHZ_TEST = 5.3 GHz`；`hw_var override OS_NAME` → `Windows-Override`；`hw_var unit CPU_USAGE_FLOAT %` → `14.9%`；`rename` → 日志 `设备重命名: D4:DA:C6:CE:5F:D6 → Left Hand`；`devices_config rssiWeakThreshold=70` → 扫描后连续 3 条 `设备信号弱: ... 衰减度 94 > 70`；`export` 四格式全部 ok（YAML/CSV 内容抽查列名与值正确，SQLite 副本 98KB）；`health get` → `活跃 / hb.status.active`。
- 健康算法：日志出现 `健康状态: 兴奋（100 BPM，静息基准 未校准）`、`健康状态: 活跃（99 BPM...）`，以及 3 条 `[WARN] 心率波动较大: 98 → 123 BPM（阈值 25）`。
- 时间变量：`TIME_LOCAL 2026-09-02 23:50:38` / `TIME_NTP 2026-09-02 15:50:38`（UTC）/ `TIME_ZONE Singapore Standard Time` / `TIME_NTP_OFFSET_MS 23`。
- 测试后已把 `rssiWeakThreshold` 复位 100、清除临时覆写/单位/自定义变量/别名，`config.json` 恢复默认。
- **踩坑**：
  - `volatile double` 在 C# 非法（CS0677）→ 速度字段改 `float`。
  - `Shape::convex_polygon` 填充折线下方区域会画成错误三角（折线整体是凹多边形）→ 改为逐段梯形（每段四边形自身是凸的）。
  - egui `Grid` 单元格首帧宽度只有 `interact_size.x`，`TextEdit::desired_width` 被 `min(available)` 夹住后再也撑不开（NTP 输入框显示成 `pool.ntp.`）→ 该行改用 `horizontal`，其余表单加 `min_col_width`。
  - 自动化 `VkKeyScan` 发键对空格/下划线的处理会串字符（`export hr_records yaml 20` 变成 `exporthr_records压马路20`），Console 的复杂命令改用 IPC 直连验证。
- **收尾**：`plan.md` Phase 1~5 勾选（Phase 2 `/chatbox/typing` 留空并注明延期原因）；`about.md` 同步目录树、§3.5 交互约定与三页描述、新增 IPC 命令总表、§5 PDH + VarEngine 四阶段表、§6 V2 四段配置表，并把「7 页」旧描述改为 11 Tab（Web UI 仍为旧 7 页，对齐归入 Phase 11）；`%TEMP%` 诊断脚本/截图/日志（6 个 ps1 + 19 张 png + 41 个 log/txt）已删除。
- **未验证**（依赖外部条件，待人工复测）：① 30s 静息 + 10s 对照的完整校准流程（需静立约 40 秒，跑法：HeartBeat 页点「校准」后保持静息，观察日志 `校准完成`）；② 自动重连 30 分钟放弃（跑法：连接后关闭手环电源，等 `reconnectGiveUpMin` 到时观察 `[WARN] 放弃重连`）；③ `build.ps1 -Mode releases|standalone` 两分支与 CLI `selftest` 回归（本轮仅按要求跑 debug）。

### 步骤 30：Overview 总览页 + 浮窗数据源/刷新速率 + BLE 速率口径（第十一轮）
- 需求：`1:: 暂无明显核心问题，继续向后推进。` / `2:: 添加Tab"Overview"，集成所有功能类Tab的核心功能到独立卡片。点击卡片右上角可直接转到此Tab。` / `3:: 本轮提前修复"缺少单独为设备打开心率浮窗、为主浮窗指定数据来源的控件。"和悬浮窗缺少数据来源和刷新速率控制的问题`

**A. 浮窗数据源与刷新速率（Phase 0 提前项）**
- `Config.cs` 的 `WindowSection` 增三字段：`Source`（主浮窗数据源，默认「平均」）、`RefreshMs`（默认 200，0 = 不节流）、`Sources`（`窗口标识 → 数据源` 字典，按窗口覆盖）。
- `FloatWindowHost` 重构取值链路：`SourceOf(win)`（先查 `Sources[win]`，主窗回落 `Window.Source`，设备窗回落窗口标识即 MAC）→ `BpmOfSource(src)`（「平均」= 所有已连接设备均值，否则取指定 MAC）→ `BpmOf(win)`；`TitleOf()` 让标题跟随数据源；`RefreshAll()` 按 `RefreshMs` 用 `Environment.TickCount64` 节流；新增 `ApplySources()` 绕过节流立即重算标题与数值。
- **根因修复**：`App.CurrentBpm` 此前只在旧 WinForms 路径赋值，导致主浮窗与 `{BPM}` 变量恒为 0。现由 `AppHub.OnHeartRate` 按主浮窗数据源维护，并调用 `SysInfo.UpdateHeartRateVars()`，使主浮窗 / `status.bpm` / `{BPM}` 三者一致。
- 新增命令 `float_config`：无参为读取，带 `source` / `refreshMs` / `win`+`winSource` 任一 key 才算写入（避免纯读取误触发落盘）；写入后 `Config.Save()` + 刷新 `CurrentBpm` + `UiInvoke(ApplySources)`，返回 `{ ok, source, refreshMs, sources, count }`。`refreshMs` 两条写入路径（`float_config`、`settings{hr.window.refreshMs}`）统一 `Math.Clamp(0,10000)`。
- 通道补齐：`PipeServer` 增 `case "float_config"`；`WebServer` 增 `/api/float/config`（GET 读 + POST 写）；`AppHub.HrJson().window` 增补 `source` / `refreshMs` / `sources`。
- 前端：`state.rs` 增 `FloatCfg{source, refresh_ms, sources}` 与 `parse_float_cfg`，`parse_config` 与 `parse_status` 两处都接入（进任意页面即刻拿到真实数据源）；`main.rs` 增 `float_set_source(win, src)`，`hb_overlay_card` 重写为：主窗数据源下拉（平均 / 指定设备）、刷新间隔 `DragValue`（`drag_stopped() || lost_focus()` 时提交）、按设备「开浮窗」按钮 + 每窗独立数据源下拉。`lang.rs` 补 `common.goto/source/default`、`float.source/refresh/perwindow/opendev` 五语言。

**B. BLE 刷新速率口径（plan.md:131）**
- `DeviceRegistry.Observe()` 改按「上次收到广播 ↔ 本次」时间差算频率：`hz = 1 / (now - LastSeen)`（`dt > 0.01s` 才计入），再 EMA 平滑 `ReportHz = ReportHz*0.7 + hz*0.3`，替换原先的固定近似值。

**C. Overview 总览 Tab（需求 2）**
- `Page` 枚举增 `Overview` 并置为侧栏首位，`match self.page` 补分支（共 12 Tab）。
- 新增 `page_overview()`：顶部标题行（引擎连接指示 + 右侧健康状态）→ 7 个统计小卡（当前心率 / 平均 / 已连接设备数 / 浮窗数 / 记录状态 / OSC 已发-接收 / 硬件变量数）→ 双栏卡片区。
  - 左栏 3 卡：**HeartBeat**（大号 BPM + 扫描/记录二元按钮 + 平滑曲线）、**Devices**（前 6 台，状态点 `●◐○` + BPM + 单设备开浮窗 + 连接/断开）、**Overlay**（开主浮窗 / 锁定 / 全部关闭 + 主窗数据源下拉 + 刷新间隔）。
  - 右栏 5 卡：**Pusher**（OSC 连接开关 + 目标端点 + 已发/失败/接收 + Webhook 数）、**OSC 监视**（心率/OSC 记录量 + 最近 5 条回传地址）、**HWInfo**（刷新按钮 + 变量数 + 前 5 项）、**Web UI**（启停 + 访问地址）、**Logs**（最近 8 行按等级着色）。
- 新增辅助 `ov_head(ui, title, goto) -> bool`：卡片标题行左侧标题、**右上角 `前往 ▸`** 按钮，返回是否点击。跳转采用「闭包内收集 `Option<Page>`、闭包外赋值 `self.page`」范式规避借用冲突；卡片闭包里需要遍历的列表先克隆成局部快照，避免与 `self.call` 双重借用。
- `tick()` 增 Overview 分支：进入该页一次性拉 `status` / `record` / `webhooks` / `logs`，避免卡片显示默认值。
- 新建 id 统一 `ov_*` 前缀（`ov_float` / `ov_float_src`）避免与既有 Grid/ComboBox id 冲突。

**验证**
- `build.ps1 -Mode debug` 全过：gcc → vite → `cargo build`（`Finished dev profile`，0 error 0 warning）→ `dotnet publish`（0 error）→ 产物 `Built\debug-20260903_055405`，summary `config/webui/hrm-ui` 均 ok，15 个文件。
- 顺带确认 `Response::drag_stopped()` 在 egui 0.31.1 可用（浮窗刷新间隔与 Overview 均依赖）。
- **IPC 实测 `float_config`**（命名管道逐条发 JSON，产物目录实机运行）：纯读取返回当前值且 `config.json` mtime 不变（确认「无写入 key 不落盘」）；`source` / `refreshMs` / `win`+`winSource` 三类写入均正确回显并落盘；`refreshMs:99999` 被 clamp 为 `10000`；`winSource:""` 正确删除该窗配置；`float_open{win}` → `float_close_all` 期间 `count` 依次 `0→1→0`。测后已复位 `source:"平均"` / `refreshMs:200` / `sources:{}`。
- **真实设备侧已被使用验证**（`logs\app.log`）：06:07:43 `浮窗数据源设置已保存` 对应 UI 里把主浮窗数据源切到 HUAWEI Band（`D4:DA:C6:CE:5F:D6`）并落盘，06:07:47 / 06:07:49 `悬浮窗已打开: D4:DA:C6:CE:5F:D6` / `48:7E:25:F9:3C:15` 对应「按设备开窗」控件对两台真实手环生效 —— 即数据源选择与单设备开窗两条链路在真机 BLE 下可用。新起干净实例复读为 `source:"平均" / refreshMs:200 / sources:{} / count:0`（与默认值一致，无残留）。
- **未验证**：300% DPI 实机下 Overview 双栏宽度与 7 个统计小卡换行表现、8 个 `前往 ▸` 跳转、刷新间隔调大后的视觉变化（跑法：把刷新间隔调到 1000ms 观察浮窗数值更新变慢）。
- **顺带发现并已修**：根目录 `config.json` 为 snake_case（`heart_rate`/`display_source`），而 `Config.Save()` 未设命名策略、输出 PascalCase，`Load()` 亦按 PascalCase 匹配 → 根模板加载时被整段忽略、退回硬编码默认值。修法见步骤 31。

### 步骤 31：config.json 命名口径统一为 snake_case（第十一轮收尾）
- `Config.cs` 抽出静态 `JsonOpts`（`WriteIndented` + `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` + `PropertyNameCaseInsensitive` + `Encoder = UnsafeRelaxedJsonEscaping`），`Load()`/`Save()` 共用，中文与 `+` 不再被转成 `\uXXXX`。21 处 `App.Config.Save()` 调用点无需改动。
- 兼容旧产物：新增 `IsLegacyLayout(text)`——根对象只有 `HeartRate` 没有 `heart_rate` 时判为旧版 PascalCase 文件，改用 `LegacyOpts`（仅大小写不敏感、不带命名策略）反序列化；首次 `Save()` 后自动转为 snake_case。这条分支是必要的：`SnakeCaseLower` 会把 `HeartRate` 映射成 `heart_rate`，与旧文件的 `HeartRate` 键不匹配，若不分流则旧产物配置会被整段丢弃。
- 根模板 `config.json` 补齐缺失段与字段：`heart_rate.window` 增 `source`/`refresh_ms`/`sources`，新增 `ui`/`devices`/`health`/`hw` 四段（值取各 section 默认值，`devices.history` 留空）；`webhook`/`web`/`logs` 保持原样。至此模板与 `AppConfig` 的字段集一一对应，用户可直接手改。
- IPC/REST 契约不受影响：`AppHub.Config()`/`HrJson()` 等是手写 camelCase 匿名对象，与 `AppConfig` 的序列化命名无关；`config_webhook.json` 走 `WebhookManager` 的 `JsonNode.ToJsonString`，也不经 `JsonOpts`。

**验证**
- `build.ps1 -Mode debug` 全过 → `Built\debug-20260903_072338`（15 文件，config/webui/hrm-ui 均 ok）；产物 `config.json` 即根模板原文（snake_case，中文明文）。
- 新模板加载回归（IPC `cmd:"config"`）：`web.port=8228`、`ui.theme=dark`、`devices.refreshThrottleMs=400`、`health.spikeDelta=25`、`hw.ntpServer=pool.ntp.org`、`hr.window.refreshMs=200`、`hr.window.source=平均`、`hr.displaySource=平均`，`float_config` 读回 `sources:{} count:0` —— 十段全部读入，无段被忽略。
- 旧产物兼容回归：把上一版 PascalCase 的 `Built\debug-20260903_055405\config.json`（含两台真机 MAC 的 `Devices.History`）拷入新产物启动，读回 `devices.history = D4:DA:C6:CE:5F:D6|48:7E:25:F9:3C:15`（未丢），发一次 `float_config` 写入后文件自动重写为 snake_case（`root keys = app,osc,heart_rate,…`，`hr window keys = …,source,refresh_ms,sources`，`display_source` 仍为「平均」）。
- 实测进程与临时脚本已清理（`REMAIN=0` / `LEFTOVER=0`）。

### 步骤 32：Phase 6 — API Server（`/heartbeat` + WebSocket 下行 + WebHook 变量 + 系统信息回传）
- 需求（`plan.md` Phase 6）：`/heartbeat`、WebSocket、WebHook 多调用方式；回传系统信息（类似 OSC）。

**A. 配置层**
- `Config.cs` 新增 `ApiSection` 与 `AppConfig.Api`：`Enabled`（默认 true）、`Token`（默认空 = 不校验）、`PushSysInfo`（默认 false）、`PushIntervalMs`（默认 2000，clamp 200~600000）、`SysInfoVars`（回传白名单，空 = 全部）、`WebhookThrottleMs`（默认 1000，clamp 0~600000）。
- 根 `config.json` 补 snake_case `api` 段 6 键，与 `ApiSection` 字段一一对应。

**B. 业务核心（`Core/AppHub.cs`）**
- 新增三条命令实现：`ApiConfig(body)`（读写合一：`body` 含任一 key 才落盘 → `Save()` + `ApplyPushTimer()` + 日志，回读时附 `varNames` = 当前可用变量名排序表，供前端画白名单）、`Heartbeat()`（`bpm/avg/connectedCount/devices[]/health/recording/app/sysInfo/ts`）、`SysInfoQuery(template)`（`text` = `App.SysInfo.FormatTemplate(template)` + `sysInfo` 快照）。
  - 命名冲突：`SysInfo(string)` 与 `App.SysInfo` 属性同名 → 定名 `SysInfoQuery`。
- 新增 `SysInfoPayload()`（按 `Api.SysInfoVars` 白名单裁剪变量表，空则全量）与 `ApplyPushTimer()`（`Timer` 按 `PushIntervalMs` 周期 `Push("sysinfo_vars", ...)`，关闭回传或禁用 API 时销毁定时器）——这就是「类似 OSC」的系统信息主动回传。
- WebHook 触发加节流（`WebhookThrottleMs`），避免高频心率把外部端点打满。
- `WebPortHook` 注入后 `WebJson()` 返回 `{running, port}`（原先只有 `running`），前端不再硬编码 8228。
- **`Dispatch(cmd, req)`**：把原先散落在 `PipeServer` 的 35 个 case 收敛成唯一命令表（现 38 条，新增 `api_config` / `heartbeat` / `sysinfo`），IPC 与 WebSocket 下行命令共用。取值用容错取值器 `S/I/B`（类型不匹配退化为默认值，不再让单个字段异常打挂整条命令）。

**C. 传输层**
- `Web/WebServer.cs`：
  - 新增 `GET /heartbeat`（`Api.Enabled` 为假返回 403）。**路由顺序是关键**：必须排在 `ServeStatic` 之前，否则被 SPA 回退当页面请求吞掉。
  - 鉴权 `AuthOk(req)` / `TokenOk(given)`：token 为空 = 放行；非空则须匹配 `?token=` 或 `X-Api-Token` 头。
  - `/ws` 重写：**握手不拦**（Web UI 依赖 WS 收推送），只在下行命令处校验；新增 `HandleWsCommand` → `_hub.Dispatch`，响应信封 `{type:"response", id, cmd, data, ts}`，与推送信封 `{type, data, ts}` 区分；token 也可放在消息体 `token` 字段。新增 `SendTo(ws, obj)` 单播。
  - 新增 `GET|POST /api/apiserver/config`、`GET|POST /api/sysinfo`（GET 走 query `template`，POST 走 body）。
- `Ipc/PipeServer.cs`：`Handle` 改为整体委托 `AppHub.Dispatch`，删掉 35 个重复 case（292 → 约 213 行）。
- `Webhook/WebhookManager.cs`：`Build()` 的 url / headers value / body 三处渲染统一过 `Vars(text)` → `App.SysInfo.FormatTemplate`，占位符从原来的少量心率字段扩展到全部系统信息变量（与 OSC 模板同一套）。

**D. Rust 前端（`RustUi/`）**
- `state.rs`：新增 `ApiCfg`（`enabled/token/push_sys_info/push_interval_ms/webhook_throttle_ms/sys_info_vars/var_names/filter/last_push/last_push_count`）+ `parse_api_cfg`（读 camelCase，`varNames` 缺省时保留旧列表）；`AppState` 增 `web_port`（默认 8228），`parse_config` 解析 `config.api` 与 `config.web`（`port > 0` 才覆盖，0 表示宿主未注入）；`sysinfo_vars` 事件只更新 `last_push`/`last_push_count`，不覆盖 `hw.vars`（白名单会裁剪，否则硬件页数据被削）。
- `lang.rs`：新增 `// ---- API Server ----` 分区 19 键 × 5 语言。
- `main.rs`：新增 `page_api_server`（约 234 行），替换原 `page_placeholder`：
  - 标题行：API 启停二元按钮 + 刷新 + Web 运行状态灯（`● 127.0.0.1:{port}` / `○ Web UI 关闭`）+ Web 启停；
  - 端点卡片：`GET /heartbeat`、`GET /api/sysinfo?template={HR}`、`WS /ws` 各带「复制」；
  - 令牌/回传/节流卡片：token 输入、`pushSysInfo` 开关、回传间隔、Webhook 节流、最近回传时间与变量数；
  - 白名单卡片：关键字筛选 + 全选/清空 + 三列 checkbox 阵列（`api_vars_grid`）；
  - 说明卡片：WS/WebHook 用法 + 橙色安全提示。
  - `tick()` 增 `Page::ApiServer` 分支预拉 `api_config`（白名单依赖后端 `varNames`）。
- 顺带去掉 Settings 页 Web UI 卡片里硬编码的 8228，改读 `state.web_port`；Webhook 编辑弹窗 Body 文案接 `api.webhookhint`；`sync_all` 删掉与 `parse_config` 重复的 `web.running` 解析。
- **egui 借用范式**：`ScrollArea`/`horizontal` 闭包内只置 `bool` 标志与 `Option<String>`，闭包结束后再统一 `self.call(...)` / `self.notify(...)`，规避 `&mut self` 与闭包捕获冲突。

**验证**
- `cargo check` 0 error 0 warning；`dotnet build -c Debug` 0 error；`build.ps1 -Mode debug` 全过 → `Built\debug-20260903_084544`（15 文件，`HeartRateMonitor.exe` 162304 B、`osc_engine.dll` 83354 B，config/webui/hrm-ui 均 ok）。
- 产物实机 `--web` HTTP 实测：
  - `GET /heartbeat` → 200，含 `bpm/avg/connectedCount/devices/health/recording/app{version,startTime}/sysInfo.vars`（抽查 `FOCUS_PROCESS_NAME=hrm-ui`、`DIMM_SPEED3=7000 MT/s`）。
  - `GET /api/apiserver/config` → 200，`enabled:true, token:"", pushSysInfo:false, pushIntervalMs:2000, webhookThrottleMs:1000, sysInfoVars:[]`，`varNames` 含 `BPM`/`BPM_AVG`/`CPU_BASE_MHZ`/`BIOS_NAME` 等。
  - `GET /api/sysinfo?template=CPU {CPU_USAGE}% RAM {RAM_PERCENT}% BPM {BPM}` → `text = "CPU 40% RAM 41% BPM 0"`，模板渲染正常。
- **测例勘误**：首轮用 `{CPU_LOAD}` 测出「占位符原样返回」，一度疑为渲染链路故障。核对变量表后确认本项目 CPU 占用变量名为 `CPU_USAGE`（另有 `CPU_USAGE_FLOAT`），**不存在 `CPU_LOAD`**；`FormatTemplate` 对未知变量按设计原样保留（不替换、不报错）。改用真实变量名复测即通过，`SysInfoQuery` 无缺陷。
- 实测进程与临时文件已清理（`REMAIN=0`，`_build.out`/`_build.err` 已删）。
- **安全须知**：`Api.Token` 默认空串 = **不校验**，实测无凭据即可拿到 `/heartbeat` 全量系统信息与 `/api/apiserver/config`（含 token 字段本身）。当前唯一保护是 `HttpListener` 只绑 `http://127.0.0.1:{Port}/` 回环前缀，且 Web UI 未启动时端点不可达。若要局域网/公网暴露，必须先设 token 并改绑定前缀（另需考虑 HTTPS 与速率限制）。
- **未验证**：API Server 页 GUI 目视（三列白名单布局、复制按钮、300% DPI 换行）；WS 下行命令与 `sysinfo_vars` 周期回传的端到端实测（需 WS 客户端）；`releases`/`standalone` 两分支与 CLI `selftest` 回归。

### 步骤 33：Phase 7 — Settings（自定义主题：配色/圆角/密度 + 集中高级设置 + 硬编码中文清零）
- 需求（`plan.md` Phase 7）：多语言实时切换（繁简英日西，繁体台湾习惯）；深色/暗色/森林/落日 + 自定义主题；集中其余板块高级设置。

**A. 配置层（C#）**
- `Config.cs` 的 `UiSection` 由 2 属性扩到 7：`Lang`/`Theme` + `Accent`/`Bg`/`Panel`（`#RRGGBB` 字符串）+ `CornerRadius`（默认 2）+ `Density`（默认 1.0）。落盘仍走 `JsonOpts` 的 snake_case（`corner_radius`/`density`）。
- `AppHub.ApplySettings` 的 `ui` 分段逐字段读 camelCase（`cornerRadius`/`density`），写入前 `Math.Clamp(cornerRadius, 0, 16)`、`Math.Clamp(density, 0.8, 1.4)`；`UiJson()` 与 `Config()` 同步透出 7 字段，前端保存后再发一次 `config` 即可读回（`Settings` 命令本身仍只回 `{ok:true}`，无回读）。
- **命名口径**：磁盘 snake_case、IPC/REST camelCase 的既有分层不变。

**B. `themes.rs`（主题引擎）**
- `CustomPalette` 扩 `corner: u8`（默认 2）/ `density: f32`（默认 1.0）；新增 `parse_hex(&str) -> Option<Color32>` / `to_hex(Color32) -> String` / `lighten(Color32, f32)`。
- `apply()` 重写：删掉原先与 `accent()` 重复的 4 臂 `match`，改直接调 `accent(theme, custom)`；`Widgets` 五态（`noninteractive/inactive/hovered/active/open`）统一设 `corner_radius = CornerRadius::same(r)`，`window_corner_radius`/`menu_corner_radius` 用 `r.saturating_add(4)`（`CornerRadius::same` 参数是 `u8`，`WidgetVisuals::rounding()` 在 egui 0.31 已 deprecated）。
- `Light` 主题接自定义配色：`panel` 用 `lighten(custom.panel, 0.86)`、`bg` 用 `lighten(custom.bg, 0.92)`、`window_fill` 纯白，因此配色控件的可见条件是 `Theme::Custom | Theme::Light`。
- **密度不累乘**：`apply()` 每帧调用，spacing 一律以 `BASE_ITEM_SPACING/BASE_BUTTON_PAD_X/BASE_BUTTON_PAD_Y/BASE_INTERACT_H` 常量 × `density` **直接赋值**（不是 `*=`），否则逐帧放大；顺序是先 `ctx.set_visuals(v)` 再 `ctx.style_mut`（`set_visuals` 只覆盖 `style.visuals`，不动 spacing）。

**C. Rust 前端状态与落地**
- `state.rs` 保持零 egui 依赖（`#RRGGBB ↔ Color32` 全部放 `themes.rs`）；配置回填改走 `HrmApp::pull_ui_palette(&Value)`，在 `sync_all()` 的 `config` 分支尾部调用——同时绕开 `json_str` 只能取字符串、拿不到 `cornerRadius`/`density` 数值的限制。`CustomPalette` 仍留在 `HrmApp`（迁进 `AppState` 要改 13 处 `themes::accent` 签名）。
- `save_ui()` 由 2 字段扩到 7（`lang/theme/accent/bg/panel/cornerRadius/density`）。
- `main.rs` Settings 页：`ui_prefs` Grid 加「外观」小标题，新增背景色、面板色、圆角（`DragValue` 0~16）、密度（`DragValue` 0.8~1.4，步进 0.01）四组控件，任一变更置 `ui_dirty` → 闭包后统一 `save_ui()`。
- **集中其余板块高级设置**：不新写控件，页尾直接复用四张既有卡片（`hb_health_card`/`hb_curve_card`/`dev_settings_card`/`hw_settings_card`），前置 Tab 名小标题分隔；因此 `tick()` 换页 match 补 `Page::Settings` 分支预拉 `health_config`/`devices_config`/`hw_config`，否则首帧显示默认值。项目未用 `CollapsingHeader`，沿用 `ui.group` 卡片风格。
- 整页因此变长 → 外套 `ScrollArea::vertical().auto_shrink([false,false]).id_salt("settings_scroll")`。**借用冲突**：闭包内不能同时用 `self` 字段与 `&mut self` 方法，故把页面主体抽成独立方法 `settings_body(&mut self, ui)`，闭包体仅 `self.settings_body(ui)`。

**D. 多语言补齐（`lang.rs` + 硬编码替换）**
- `lang.rs` 新增约 60 键 × 5 语言（`common.*`/`quit.*`/`settings.*`/`dev.*`/`float.*`/`logs.*`/`hb.*`/`hw.*`），繁体按台湾习惯（「儲存」「懸浮視窗」「正規表示式」「介面」）。
- `main.rs` 替换硬编码中文：`page_settings` 全部 12 处（Web 运行/停止、版本/模式/启动时间/目录、转储提示、悬浮窗五标签与保存提示）、`hb_health_card` 五标签 + 保存提示、`hb_curve_card` 缓存提示、`dev_settings_card` 四标签 + 保存/说明/历史设备、`hw_settings_card` 采集间隔/保存/覆写提示、`page_logs` 过滤 hint 与转储提示、顶栏「心率」、退出确认窗（标题/正文/确定/取消）。
- `hw_settings_card` 的 `KINDS` 常量由 `[(后端值, 显示名)]` 改为 `[(后端值, i18n key)]`，`selected_text` 与 `combo_item` 两处消费方同批改为 `tr(lang, n)`，`unwrap_or_else` 兜底取 `hw.kind.expr`。
- `draw_curve` 签名加 `lang: Lang` 形参（空数据文案 `hb.waiting`），三处调用点（Overview、`hb_single`、`hb_multi`）同步传参。

**验证**
- `cargo check` 0 error 0 warning（先 `Stop-Process -Name hrm-ui` 至 `REMAIN=0`，再改 `LastWriteTime` 强制重编，确认输出含 `Checking hrm-ui v1.0.0`）。
- `build.ps1 -Mode debug` 全过 → `Built\debug-20260903_105449`（15 文件，`HeartRateMonitor.exe` 162304 B、`osc_engine.dll` 83354 B，config/webui/hrm-ui 均 ok）。构建脚本内部即执行 `dotnet publish`，C# 侧改动一并编译通过。
- **未验证**：Settings 页 GUI 目视（新增四组控件在 300% DPI 下的布局、圆角/密度实时生效观感、Light 主题接自定义配色后的对比度）；五语言实机逐一切换核对；重启后 `ui` 段 7 字段回填的往返实测。
- **遗留硬编码**：`page_osc`（连接配置/模板区）、`page_osc_monitor`（`stat_card` 三列）、`page_devices` 批量按钮、`page_webhooks` 弹窗、`hb_export_card` 表名与 `hb_overlay_card` 说明等仍为中文字面量，键未备，不在 Phase 7 范围内。
- **安全须知（沿用步骤 32，未变更）**：`Api.Token` 默认空串 = 不校验，无凭据即可取 `/heartbeat` 全量系统信息与 `/api/apiserver/config`（含 token 字段本身）；唯一保护是 `HttpListener` 只绑 `http://127.0.0.1:{Port}/` 且 Web UI 未启动时端点不可达。局域网暴露前必须先设 token 并改绑定前缀（另需 HTTPS 与速率限制）。

### 步骤 34：Phase 8 — Logs 正则/等级过滤 + 导出过滤结果（含 Phase 5 遗留：预设 NTP 服务器）
- 需求：`plan.md` Phase 8「正则 / 等级多条件过滤；保留实时跟随；导出过滤结果」+ Phase 5 唯一未勾项「内置多个预设 NTP 服务器」。

**A. 过滤内核归属（决策）**
- 正则交给后端 .NET `System.Text.RegularExpressions`（`RegexOptions.IgnoreCase | CultureInvariant`）权威执行，Rust 侧**不加 `regex` crate**（依赖表保持 5 项），前端只保留 `to_lowercase().contains()` 作即时预览；正则模式下前端不做二次筛选，一律以后端返回为准。
- 等级过滤用 `[LEVEL]` 子串匹配（`StringComparison.Ordinal`），与 `Logger.Write` 的 `[HH:mm:ss][LEVEL] msg` 固定格式、以及前端三色上色规则同源，不写解析器。
- **正则语法错误不抛异常**：`FilterLogs` 捕获 `ArgumentException` 后降级为「不过滤」，把 `e.Message` 通过响应 `error` 字段回传前端显示，命令本身照常 200。

**B. 后端（C#）**
- `AppHub.cs`：`Logs(filter, limit)` → `Logs(filter, limit, regex, levels)`，响应由 `{lines}` 扩为 `{lines, total, matched, error}`（`total` = 缓冲区总行数，`matched` = 过滤命中数，截断前统计）；新增 `LogsExport(filter, format, regex, levels)` 与私有 `FilterLogs` 内核；`Dispatch` 新增数组取值器 `A(key)`（与 `S`/`I`/`B` 并列，`batch` 原先内联同样写法）+ `case "logs_export"`。
- `Exporter.cs`：新增 `LogFormats = {txt,json,yaml,csv}`（无 sqlite——日志不在库里）、`ExportLogs(List<string>, string)` 落到 `App.BaseDir/exports/hrm_log_{yyyyMMdd_HHmmss}.{ext}`（与既有数据导出同目录，**不**复用 `Logger.Dump` 的 `logs/auto`）、`SplitLogs` 把每行拆成 `time`/`level`/`message` 三列以复用既有 `ToJson`/`ToYaml`/`ToCsv`（不匹配的整行落到 `message`）。
- `WebServer.cs`：`GET /api/logs` 支持 `regex=1|true` 与 `levels=ERROR,WARN`（逗号分隔）；新增 `POST /api/logs/export`（body `{filter,format,regex,levels[]}`）。REST 与 IPC 命令表保持一一对应，供 Phase 11 Web 前端对齐。
- Phase 5 预设：`AppHub.NtpPresets` 8 条 `static readonly string[]`（阿里云 `ntp.aliyun.com`/`ntp1.aliyun.com`、清华 TUNA、国家授时中心、NTP Pool 中国区、Microsoft、Apple、NTP Pool 全球），经 `HwConfig()` 透出 `ntpPresets`。**是常量不是用户数据**：不改 `Config.cs`、不入 `config.json`、只读不可写。`VarEngine.RefreshNtp()` 未改（仍是单服务器、失败仅 Debug 日志，不做自动切换）。

**C. 前端（Rust）**
- `state.rs`：`HwCfg` 加 `ntp_presets: Vec<String>`（`parse_hw_cfg` 缺省时保留旧值）；`AppState` 加 `log_total`/`log_matched`/`log_error`，`parse_logs` 同步读三字段（`matched` 缺省回退 `lines.len()`）。
- `main.rs`：模块级新增 `LOG_LEVELS = [ERROR,WARN,INFO,DEBUG]`（下标与 `log_lv: [bool;4]` 一一对应）与 `LOG_FORMATS`（对齐 `Exporter.LogFormats`）；`HrmApp` 加 `log_regex`/`log_lv`/`log_export_format`；抽出 `logs_request()` 统一拼 `filter`/`regex`/`levels`，`tick()` 两处调用点（Logs 页拉取、Overview 聚合）改用它。**借用注意**：`self.call(self.logs_request())` 会双借 `&mut self`，须先 `let req = self.logs_request();` 再传。
- `page_logs` 第二行工具栏：四等级复选（全不勾 = 不按等级过滤）、`命中 X/Y` 弱字号计数、导出格式下拉（`from_id_salt("log_export_format")` + `combo_item`）+ 导出按钮（notify 带行数与路径）；正则复选放第一行紧随关键字框，**关键字/正则/等级任一变化即置 `logs_synced=false`** 触发重查。`state.log_error` 非空时在分隔线上方红字提示。
- 前端预览过滤改为「等级 tag 命中 + （非正则时）关键字子串」两级 `filter`，`ScrollArea` 的 `stick_to_bottom(follow)` 原样保留，满足「保留实时跟随」。
- `hw_settings_card` NTP 行插入预设下拉（`from_id_salt("hw_ntp_preset")`），选中即填入左侧输入框、仍需点保存；列表为空时整个控件不渲染。控件留在既有 `horizontal` 内（该处注释已说明：Grid 单元格首帧宽度只有 `interact_size.x`，`TextEdit` 的 `desired_width` 会被夹死）。
- `lang.rs` 新增 9 键 × 5 语言：`logs.regex`/`logs.level`/`logs.matched`/`logs.badregex`/`logs.exportfiltered`/`logs.exported`/`logs.exportfail`/`logs.exporthint` + `hw.ntppreset`。

**D. 顺带修复：`App.LogBuffer` 在默认路径下恒空**
- 实测 `/api/logs` 返回 `total=0` 才发现：入队逻辑原先只写在 `MainForm.cs:479`（旧 WinForms 窗体的 `OnLog` 订阅里），而默认启动路径是 `hub + PipeServer + TrayHost`、**根本不构造 `MainForm`**（只有 `--winforms` 才会）。于是从第五轮 UI 改造起，`logs` 查询、`logs_dump`、Console 的 `logs` 命令一直读的是空队列——本次的过滤/导出功能同样会恒空，属必修根因。
- 改 `AppHub.WireEvents()` 的 `App.Log.OnLog` 订阅：入队 + `Count > 5000` 出队后再 `Push("log", …)`，上限与 `MainForm` 一致。两条启动路径互斥（`--winforms` 直接 `return`，不会构造 hub），不会重复入队。

**验证**
- `cargo check` 0 error 0 warning（先改 `LastWriteTime` 破缓存，输出含 `Checking hrm-ui v1.0.0`）。
- `build.ps1 -Mode debug` 两轮全过 → 最终 `Built\debug-20260903_114536`（15 文件，`HeartRateMonitor.exe` 162304 B、`osc_engine.dll` 83354 B，config/webui/hrm-ui 均 ok）；C# 侧四文件改动随脚本内的 `dotnet publish` 一并编译通过。
- **REST 端到端实测**（`--web` 启动后打 `127.0.0.1:8228`）：
  - `GET /api/logs?limit=5` → `total=7 matched=7`，行内容正常（修复前为 `total=0`）。
  - `GET /api/logs?levels=INFO&regex=1&filter=Web|NTP|启动` → `matched=2/7`，正则与等级**组合**生效（含 UTF-8 中文关键字）。
  - `GET /api/logs?levels=ERROR` → `matched=0/7`（当时无 ERROR 行），等级筛选独立生效。
  - `GET /api/logs?regex=1&filter=[(` → `error="Invalid pattern '[(' at offset 2. Unterminated [] set."` 且 `matched=7`，确认**降级为不过滤 + 回传错误**符合设计。
  - `POST /api/logs/export` 四格式全 `ok=true`（txt 551 B / json 1201 B / yaml 1121 B / csv 1008 B）；抽查 csv 表头为 `time,level,message` 且含逗号的消息被正确加引号，yaml 的 `\\.\pipe\hrm_v1` 转义正确。
  - `GET /api/hw/config` → `ntpPresets` 8 条与 `ntpServer=pool.ntp.org` 一并回读。
  - 实测产物（`exports/hrm_log_*` 4 个文件、构建日志）已清理。
- **未验证**：GUI 目视（两行工具栏在 300% DPI 下的换行观感、五语言下的按钮宽度）；hrm-ui 侧 IPC 路径的过滤/导出交互（后端同一份 `Dispatch`，风险低）。
- **安全须知（新增面）**：`POST /api/logs/export` 与 `GET /api/logs` 同样落在**无鉴权**路由下（`Api.Token` 空串 = 不校验的现状未变），前者会按调用方给的过滤条件把日志正文写入磁盘并回传绝对路径。日志可能含设备 MAC、路径等信息，局域网暴露前必须先设 token。

### 步骤 35：/chatbox/input 参数补齐（T/F）+ OSC 接收聚合缓冲（第十五轮）
- 需求：① 默认发送模式第一个 bool 必须 T、第二个 F，否则每秒弹一次待确认输入框；② OSC 汇报极快且当前以日志形式呈现，须改卡片图形化 + 加缓冲区。

**A. `/chatbox/input` 只发了一个字符串（问题 1 根因）**
- 旧链路 `OscService.Tick → OscEngine.SendText → osc_engine_send_text` 固定编码 `",s"` 单参数。VRChat 的 `/chatbox/input` 签名是 `string, bool, bool?`：第 1 个 bool = 立即发送、第 2 个 = 提示音。**缺省时 VRChat 按「填入输入框等用户确认」处理**，于是 1000ms 推送间隔下每秒弹一次待确认框（`Skills\VRChat Interfaces.skill:182~189` 已写明 `"...", T, F`）。
- `osc_engine.c/.h` 新增 `osc_engine_send_chatbox(ip, port, address, text, immediate, sound)`：复用既有 `osc_encode_message`，三参数 `OSC_T_STR + OSC_T_TRUE/FALSE ×2`，标签为 `",sTF"`（T/F 无数据段，只占标签位）。
- `OscEngine.cs` 加对应 P/Invoke 与 `SendChatbox(..., immediate: true, sound: false)`。
- **统一发送入口** `OscService.SendOne(ip, port, address, text)`：地址以 `/chatbox/input` 开头（`OrdinalIgnoreCase`）走三参数版，其余地址仍走单字符串版。`Tick()`、`SendTest()`、`CLI/SelfTest.cs` 第 8 步三处调用点全部改走它，避免下次再漏。

**B. `T`/`F` 解码丢值（顺带修）**
- `OscService.OnPacket` 原先只在 `arg` 含 `"v"` 时取值，而解码器对 `T`/`F` 只产出 `{"t":"T"}`（无数据段本就没有值域）。结果 `/avatar/parameters/AFK`、`Seated` 这类布尔参数一律取到 `null`，`HealthService.OnOscParam` 的 `Truthy(null)` 恒 false —— **健康判定的 AFK/Seated 通道从未真正生效**。改为按标签定值：`t=="T"` → `true`、`t=="F"` → `false`。

**C. OSC 接收缓冲（问题 2）**
- 旧链路：每条 OSC 消息 → `Push("osc_recv", {addr,args})` 一次 IPC/WS 下行 + 前端 `Vec` 追加（上限 400 条，靠丢弃保命）+ `App.Log.Debug($"OSC 收到: {addr}")` 逐条写日志。实测每 4 秒约 **806 条**消息，等于 806 次下行 + 806 行日志；配上步骤 34D 刚修好的 `LogBuffer` 入队，5000 行缓冲会在几秒内被 OSC 日志冲干净。
- `AppHub` 新增聚合缓冲：`_oscAgg`（addr → (最后一次参数, 累计条数)）+ `_oscDirty`（增量键集）+ `_oscFlushTimer`（**首条到达才起表，空闲不占线程池**），每 `OscFlushMs = 200` 毫秒把有变化的地址合并成**一条** `osc_params` 事件下发；地址数上限 2000，超出后只更新已知地址。
- 去掉 `OscService.OnPacket` 里的逐条 `App.Log.Debug`（统计交给聚合表与 `RecvCount`）。
- 新增命令 `osc_params`（全量快照，供前端进页补齐）与 `osc_params_clear`（清空），REST 对应 `GET /api/osc/params`、`POST /api/osc/params/clear`。
- `AppHub.Stop()` 同步释放 `_oscFlushTimer`。

**D. 前端（Rust）**
- `state.rs`：`osc_recv: Vec<(String, Vec<Value>)>` 换成 `osc_params: BTreeMap<String, (String, u64)>`（地址有序、天然去重，内存随参数数量而非消息数增长）；新增 `merge_osc_params()` 增量合并 + `scalar_text()` 取标量文本；`apply_event` 的 `"osc_recv"` 分支换成 `"osc_params"`。
- `main.rs`：`page_osc_monitor` 的分组卡片改读聚合表（前端不再自己 `HashMap` 统计），顶部加「N 个参数 + 清空」行；`tick()` 进入 OSC 页时拉一次 `osc_params` 全量快照；Overview 的 OSC 卡改显示 `addr = value` 前 5 项。
- `lang.rs` 补 `osc.paramcount`、`osc.noparams` 两键 × 5 语言（顺带清掉这两处硬编码中文）。

**验证**
- `cargo check` 0 error 0 warning；`build.ps1 -Mode debug` 三轮全过 → 最终 `Built\debug-20260903_185906`（`osc_engine.dll` 83414 B，比改前 83354 B 多 60 B = 新导出函数）。
- **真机实测**（VRChat 正在运行，OSC 回传 9001）：
  - `POST /api/osc/test` 打 `/chatbox/input` → 环回收到 `args=[{"t":"s","v":"..."},{"t":"T","v":true},{"t":"F","v":false}]`，**三参数与 T/F 定值同时确认**；打 `/avatar/parameters/AFK` 仍为单字符串，分流正确。
  - WS 订阅 4.1 秒：`osc_params` 事件 **21 条**（≈5/秒，与 200ms 周期一致）、含 726 个地址项；同期 `osc.recv` 增长 **806 条**原始消息。即下行事件数从 806 降到 21（**约 1/38**），前端每帧只需读一张 map。
  - `logs\app.log` 中 `OSC 收到` 计数 **0**，`/api/logs` `total=10`（仅启动信息），日志刷屏消失。
- **未验证**：VRChat 内是否真的不再弹待确认输入框（需在 VR 里目视，但字节层已与 Skill 文档一致）；提示音关闭效果；聚合表 2000 上限的触发路径。

### 步骤 36：设备排序评分重做（可解释）+ Phase 9 Console（命令内核统一）
- 需求：① 后端实现「名称优先级 + 信号强度」排序，含 5 条特征口径（有标识符 / 品牌白名单 / 低延迟高信号 / 排除耳机音箱 / 排除智能家居）；② 推进 Phase 9 Console。

**A. 排序评分（`DeviceRegistry`）**
- 三张词表扩容：`Preferred` 14 → 27 项（补 samsung/suunto/magene/decathlon/fitbit/forerunner/vivoactive/ticwatch/gtr/gts 等型号词）；`Audio` 8 → 18（补 headphone/earphone/freelace/qcy/edifier/jbl/bose/sony wh|wf）；`Home` 7 → 18（补 yeelight/aqara/tuya/switch/curtain/printer/scale/mouse/keyboard/tv/projector）。
- 权重全部提为命名常量（`WConnected`/`WSaved`/`WHrService`/`WNamed`/`WBrand`/`WHistoryTop`/`WRssiMax`/`WLatencyMax`/`PAudio`/`PHome`/`PIdleMax`），调参不用再翻函数体。
- **新增「通信延迟」维度**：未连接设备拿不到真实 RTT，用广播间隔的倒数（`Entry.ReportHz`，第十一轮已 EMA 平滑）作代理量，`Hz × 8` 上限 +80。
- **品牌分与降权互斥**：`HUAWEI FreeBuds`、`Xiaomi Smart Speaker` 这类名字同时命中厂商词与音频词，若各自独立计分会被品牌分抵消掉一半降权。改为只有非音频且非家居时才给品牌分。
- **别名参与匹配**：`NameCandidates()` 把别名与广播名一起纳入关键字匹配（用户重命名后不该丢掉品牌特征），原先只用 `DisplayName()` 的单一结果，别名一设置就把广播名的特征盖掉了。
- **`Score` 改为明细求和**：新增 `Explain(mac) → List<(项名, 分值)>`（仅非零项），`Score()` 即 `Explain().Sum()`，两者不可能走偏。`DeviceJson` 增加 `scoreParts` 字段透出，Console 的 `score` 命令直接打印明细。

**B. Phase 9 Console：命令内核统一到 `CommandShell`**
- 原先是**两套**实现：`CliApp.Handle`（CLI REPL，15 个命令，直写 `Console`）与 `main.rs::console_exec`（前端 Console Tab，13 个命令，把 IPC 响应的原始 JSON 整行糊出来）。行为不一致，`hw` 的过滤/对齐逻辑还各写了一遍。
- 新建 `App/Core/CommandShell.cs`：**唯一的文本命令实现**，只吐 `List<string>`、不碰 Console/UI，可在任意线程调用。命令表 20 条（`Commands` 常量带 name/usage/desc，`help` 与前端补全共用）：新增 `score`/`disconnect`/`bpm`/`info`/`connect`，`logs` 支持 `[行数] [关键字]`，`hw`/`status`/`config` 改为对齐好的多行表格。`Pad()` 按 CJK 双宽计算列宽，中文列不再错位。
- `CliApp` 瘦身 200 → 59 行：只保留事件回显、REPL 循环、`selftest`（长阻塞且直写 Console）与 `clear`，其余全部 `CommandShell.Execute`。顺带删掉它自己维护的 `Devices` 字典与 `_scanning` 标志——前者与 `DeviceRegistry` 重复，后者与 `App.Ble.Scanning` 不同步（用 REPL 的 `scan` 之外的途径开扫描时它一直是 false，导致发现日志不打印）。
- `AppHub` 新增命令 `cli{line}`（返回 `{ok,lines}`）与 `cli_help`（返回命令表）；REST 对应 `POST /api/cli`、`GET /api/cli/help`。
- 前端 `console_exec` 从 106 行降到 35 行：除 `clear`（纯终端行为）外一律下发 `cli`，再按命令名回填本地状态（`record`/`health` 重拉快照，`var`/`info` 置 `hw_dirty`）。删掉只在此处使用的 `cmd_to_lines`。
- **命令历史**：`console_hist`（上限 200，连续重复只记一条）+ `console_hist_pos`（`None` = 编辑新行）+ `history_step(up)`。**按键顺序有坑**：`TextEdit` 拿到焦点后会吞掉方向键，所以 ↑↓ 必须在 `ui.add(TextEdit)` **之前**从 `ui.input` 取走。
- `lang.rs` 补 `console.failed`、`console.history` 两键 × 5 语言。

**验证**
- `cargo check` 0 error 0 warning；`build.ps1 -Mode debug` → `Built\debug-20260903_225344`。
- **REST 实测**（`POST /api/cli`）：`help` 20 条命令对齐输出；`status`/`config` 四行摘要正确（含「令牌 空（不校验）」）；`foobar` → 未知命令提示；`GET /api/cli/help` 返回 20 项 name/usage/desc。
- **真机排序实测**（5 台在场设备，按分降序）：
  - `Xiaomi Smart Band 9 Pro 3C15` 4415（心率服务 3000 + 有标识符 800 + 品牌 600 + 信号 15 + 广播频率 80）
  - `HUAWEI Band HR-FD6` 4410
  - `Redmi K70` 1493（手机：有标识符 + 品牌，无心率服务）
  - `Bluetooth f8:53:84:b9:3c:bb` 830（无名设备）
  - `midea` **-3**（有标识符 800 + 智能家居 -900 + 信号 23 + 广播频率 74）→ 智能家居成功压到末位
  - `score <mac>` 明细与合计对得上。
- `logs 3`、`hw CPU_USAGE`、`scan start|stop` 均正常。测试产物与构建日志已清理。
- **未验证**：前端 Console Tab 的 ↑↓ 历史需 GUI 目视；`selftest` 未回归（它只在 CLI 且会长阻塞）。

### 步骤 37：Phase 10 Monitor（第十七轮）
- 需求：历史 HR/健康/睡眠数据聚合 + 统计分析 + 多格式导出。
- 后端：新建 `Core/MonitorStats.cs` 作为唯一实现：`Monitor(hours,buckets)` 输出 hr（min/avg/median/max/sd/samples + `trend[]`/`hist[]` 直方）+ health（状态计数）+ devices（每设备统计）+ osc（地址 Top20 计数）+ db（文件/录制状态）；`AppHub.Monitor()/MonitorExport()` + Dispatch `monitor`/`monitor_export` + CLI `monitor` + REST `GET /api/monitor?hours=&buckets=`、`POST /api/monitor/export {hours,buckets,format}`（导出复用 Exporter 多格式 → `exports/`）。
- 前端（Rust，先前轮次写好并随 debug 构建复验）：`state.rs` report 解析、`page_monitor` 统计六卡 + 区间下拉（`mon.r1h…rall`）+ 趋势/直方/健康分布/设备分布/OSC Top20 + 导出按钮；`lang.rs` 补 `mon.*` 25 键 × 5 语言。
- Web（本步新接）：WebUI `pages/Monitor.tsx` 同构（RANGES/FORMATS + `.grid-6` 六统计卡 + 纯 CSS `.bars` 条形图 + 健康 `hb.status.*` 分布 + 设备分布 + OSC Top20 + 导出走 `POST /api/monitor/export`）。
- 验证：`GET /api/monitor?hours=24&buckets=12` 200，返回 hr/health/devices/osc/db 四段；空库 `trend=0/hist=0`，Web 端用 `mon.nodata` 空态兜底。`build.ps1 -Mode debug` 全链路通过 → `Built\debug-20260904_012756`。

### 步骤 38：Phase 11 Web 前端对齐 12 Tab（第十七轮）
- 需求：WebUI 壳与本地 12 Tab 同序同结构、每页接真实后端、i18n（全 5 语言）与主题对齐。
- 基底：`lang.ts` 移植 lang.rs 全部 key × 5 语言（`tr()` 按列取，缺 key 回退原键）；`theme.ts`/`theme.css` 主题 5 态（dark/light/forest/sunset/custom，custom 由 accent/bg/panel 派生 CSS 变量）+ 圆角/密度改写 `--radius*`/`--sp-*`；`types.ts`/`api.ts`/`store.tsx` 扩展（FullConfig/oscParams/health/hwConfig/hwVar/logs/logsExport/monitor/monitorExport/cli/record/apiConfig/webStart/webStop/export/rename…）。
- 壳：`App.tsx` 12 Tab NAV（overview/osc/pusher/heartbeat/devices/hwinfo/headset/apiserver/settings/logs/console/monitor）+ 顶栏 BPM + 语言/主题下拉入口 + 退出确认；侧栏连接状态。
- 页面批 1-3：
  - Overview：统计卡/心率大卡/设备历史/快捷跳转，5s `sync()`；
  - HeartBeat：心率大卡 + 扫描/记录 + 设备行（连接/浮窗）+ 健康状态 + 导出卡，4s sync；
  - Monitor：六统计卡 + 纯 CSS 趋势/直方 + 健康/设备/OSC Top + 区间/桶/格式导出；
  - Console：本地终端壳 + `POST /api/cli` + ↑↓ 历史 + help/清空；
  - OSC 监视：3s 拉 `GET /api/osc/params`（replace 合并，不重复累加）+ 记录开关/清空/分组卡片；
  - Pusher：合并 OSC 发送节 + Webhook 节（hideHeader 复用）；
  - ApiServer：启用/令牌/定时回传/节流 + 端点（`/heartbeat`、`/api/sysinfo`、WS）复制 + 变量白名单多选 + 保存；
  - HeadSet：WIP 占位；
  - Logs：后端权威过滤（正则/四级）+ `命中 X/Y` + txt/json/yaml/csv 导出 + WS `log` 跟随（createEffect 追加本地行）；
  - Settings：外观块（语言/主题/accent/bg/panel/圆角/密度）即时保存生效 `POST /api/settings {ui}`；
  - Devices：加「重命名」（`POST /api/device/rename`）+ 关键按钮接 `dev.*`/`scan.*` key；
  - HWInfo：变量表保留 + 采集设置卡（intervalMs/decimals/useFloat/round → `GET|POST /api/hw/config`）。
- 验证：`npm run tsc` 0 error；`build-webui.ps1`（28 modules，dist JS 120 KB）；`build.ps1 -Mode debug` 全链路 → `Built\debug-20260904_012756`；端到端（`127.0.0.1:8228`）`/api/config`（ui 段 lang/theme）、`/api/hw`（含 CPU_FREQ_MHz/GHz）、`/api/monitor`、`/api/osc/params`、`POST /api/cli`（status 4 行）、`/api/record`、`/api/logs`、`/webui/`（200）全部通过。

### 步骤 39：Phase 0 收尾 — 悬浮窗无原生顶栏 + 标准单位变量（第十七轮）
- 需求（补勾 Phase 0）：① 悬浮窗 Windows 原生顶栏菜单应隐藏；② 添加 CPU_FREQ_GHz 等标准单位变量。
- `FloatingWindow.cs`：`FormBorderStyle.None` 永不显示原生顶栏（代码注释即"缩放交给 WM_NCHITTEST 自绘热区"）；右下角 `GripSize` 热区返回 `HTBOTTOMRIGHT` 缩放；`ContextMenuStrip` 右键菜单、`HookMouse` 左键拖拽、`DoubleClick` 回主窗、锁定走 `WS_EX_TRANSPARENT` 点击穿透（`TopMost` 同步）。此前阶段已实现（与浮窗来源/多窗/DPI 修复同批），本步做代码核对与验收记录。**实机手感待人工目视**（拖拽/右键菜单/双击/右下角缩放/锁定穿透）。
- 标准单位变量：`SysInfoService.cs` PDH 实时频率（含睿频小数）双写 `CPU_FREQ_MHz`/`CPU_FREQ_GHz`；`RegistryInfo`/`WmicInfo` fallback 同样双写；`VarEngine.ApplyStandardUnits` 按 `UnitRules`（内存 GB/MB、温度 C、Uptime 分时天、VRAM/DIMM/DISK/DRIVE/NIC 多实例）派生后缀变量，先 `ApplyTime → ApplyStandardUnits → ApplyCustom → ApplyRenames → ApplyOverrides`；`Vars.All` 白名单收录全部派生变量名（含 `CPU_FREQ_MHz/GHz`、`RAM_*_GB/MB`、`*_C`、`UPTIME_SECONDS_*`、多实例 `VRAM_*_GB`/`DIMM_SPEED_MHz`/`NIC_SPEED_Gbps` 等）。
- 验证：实测 `GET /api/hw` 同时含 `CPU_FREQ_MHz` 与 `CPU_FREQ_GHz`（文本匹配 True）；CLI `status` 变量 215 项。

## 2026-09-04 第十八轮：V3 前端重构（Clone VRCX 的 Vue 3 + WebView2 壳）

### 步骤 40：Phase A0 — 目录整理 + 精简备份 + git 基线 + image 资源目录
- 需求：`核心功能没问题，接下来是时候重构UI准备做Release了。先整理项目目录并创建一个image目录用于后期放图标等资源` / `先cp -r备份到上级目录 创建C# Test Bak_xxx。随后git init` / 备份策略选「精简快照」。
- 目录整理：文档归档到 `Archives/`（V2 `plan.md` → `Archives/plan2.md`）；新建 `image/`（app 图标/托盘/宣传图），`build.ps1` 加 3d 段随产物复制。
- 备份：`robocopy` 精简快照 → `e:\WorkSpace\C# Test Bak_20260904_021212`（78MB）。**踩坑**：首次未排除 `Built/` 得到 687MB 目录，补 `/XD Built` 重做；旧目录 `C# Test Bak_20260904_021144` 因占用/权限无法命令行删除，已告知用户手动清理。
- 版本控制：`git init` + `.gitignore`（`Built/ bin/ obj/ target/ node_modules/ dist/ logs/ *.log *.db exports/ Backup/ VRCX-2026.07.18/ plan.md`）+ 基线提交 `993dad0`（89 文件）。**踩坑**：`Author identity unknown` → 用进程级 `git -c user.name=... -c user.email=... commit`，不改用户全局 git 配置。
- V3 计划：新 `plan.md`（§1 VRCX 方案分析、§2 决策表 + §2.1 壳矩阵、§3 12 路由、§4 Phase A0–A6、§5 验证与遗留），按用户要求**不入版本控制**。

### 步骤 41：内嵌浏览器壳（hrm-webui / WebView2）+ Rust 设置页「拉起测试前端」
- 需求：`保留当前UI方便后期调试` / `在当前UI设置中添加"拉起测试前端"button` / `Clone VRCX的设计风格和前端实现逻辑，比如msedgewebview2` / `开始构建Electron、Cef实现`。
- `Shells/WebView2Host/`（AssemblyName = **hrm-webui**）：
  - `Program.cs`：`--port` / `--launch-exe` 参数；`ProbePort` TCP 探测 1.2s；未就绪时 `TryLaunchBackend` 以 `--web` 拉起 `HeartRateMonitor.exe` 并轮询 50×300ms；始终打开窗口（失败由错误页兜底）。
  - `MainForm.cs`：`WebView2` Dock=Fill；独立 `webview2-data` userDataFolder（避免与其它 WebView2 应用抢占）；图标取相邻 `image/app.ico`；`F12` DevTools、`F5` 重载；`NavigationCompleted` 失败渲染中文错误页而非白屏。
- `Shells/Electron/`：备壳骨架（`package.json` + `main.js`，BrowserWindow + contextIsolation + 端口探测 + 错误页），未 `npm install`（体积原因，按需再启用）。Cef 按 plan §2.1 评估不默认。
- Rust 侧（保留调试 UI）：`RustUi/src/main.rs` 设置页 Web UI 卡片新增按钮 `settings.testfront`（`lang.rs`/`lang.ts` 同步 5 语言）—— 先 `web_start`，再优先启动同目录 `hrm-webui.exe --port N`，缺失时回退 `start <url>`。
- `build.ps1` 3e 段：`dotnet publish` 壳到临时 `_shell`，只挑 `hrm-webui*` 与 `Microsoft.Web.WebView2*` + `runtimes/` 复制回产物，随后删除 `_shell`；摘要新增 `hrm-webui` / `image` 行。

### 步骤 42：Phase A1 — WebUI 由 SolidJS 原地迁移为 Vue 3 + TS（Clone VRCX 设计语言）
- 需求：`开始构建新UI布局。读取VRCX的布局文件，参考其设计语言构建新的相同风格前端。`
- 工程：`package.json` 换 Vue 3.5 + vite 6 + vue-router 4 + pinia 2 + vue-i18n 11 + lucide-vue-next；`vite.config.ts`（`@` 别名、dev proxy `/api`→8228、`/ws` ws proxy）；`tsconfig.json`（strict + bundler 解析）；`npm run typecheck` = `vue-tsc --noEmit`。
- 设计系统（对齐 VRCX 而不引 Tailwind/reka-ui）：`src/styles/globals.css` 克隆 shadcn zinc oklch token（`:root` 亮 / `.dark`+`[data-theme=dark]` 暗，另有 forest/sunset/custom 覆盖）；布局类 `.hrm-app/.hrm-nav/.hrm-main/.hrm-page/.x-container/.page-host/.page-head/.page-body/.hrm-status`；轻组件 `.x-btn(.primary/.danger/.ghost/.sm)/.x-card/.x-input/.x-select/.x-muted/.x-mono/.x-gap/.x-grow/.x-hover-card`；密度/圆角走 `--gap-*`/`--ctl-h`/`--radius`。
- 骨架：`MainLayout`（侧栏 + 主区 + StatusBar，折叠态 localStorage 记忆）、`NavMenu`（12 Tab + lucide 图标 + 当前路由高亮）、`StatusBar`（引擎在线/BPM/已连设备/WS/版本）、`router.ts`（hash 路由 12 项，默认 `/heartbeat`）、`theme.ts`（五态主题 + accent/bg/panel/圆角/密度注入 CSS 变量）。
- i18n：`src/lang.ts`（V2 五语言表，与 `RustUi/src/lang.rs` 同 key）为唯一事实源，`scripts/lang2json.mjs` 生成 `locales/{zh-TW,zh-CN,en,ja,es}.json`。**踩坑**：正则只认带引号 key，裸 key `wip` 漏解析导致 HeadSet 页显示原 key → 正则改为 `(?:"([^"]+)"|([A-Za-z_$][\w$]*))`，键数 232 → **234**。
- 构建脚本：`build-webui.ps1` / `dev-webui.ps1` 在 `%TEMP%\hrm-webui(-dev)` 临时根构建/开发（项目路径含 `#`，Vite 直接在项目内会 `Failed to resolve /src/main.ts`）；`node_modules` 用目录联接复用。**踩坑**：`vue-tsc` 报 `Cannot find module 'node:url'` → 补 `-D @types/node`。

### 步骤 43：内置 WebUI 窗口白屏修复 + 不再自动拉起系统浏览器
- 需求：`不再自动启动浏览器，使用内置WebUI渲染页面。(目前WebUI窗口没渲染任何内容)`
- 白屏根因：`vite.config.ts` 用 `base: './'` → 产物引用 `./assets/*`，页面在 `/webui/` 下被解析成 `/webui/assets/*`，而后端 `_webRoot` 本身已是 `<发布目录>\webui` → 404 → SPA 回退返回 `index.html`（`text/html`, 436B），浏览器按模块脚本加载 HTML 直接失败，页面全白。
  - 修复：`base: '/'`。实测资源变为 `/assets/index-*.js`：`200 text/javascript 219111B`、CSS `200 text/css 8073B`。
- 启动链改为内置壳：`App/ProcessInfo.cs` 新增 `LaunchWebUi(port)`（启动同目录 `hrm-webui.exe --port N`，缺失/异常回退 `OpenBrowser`）；`App/Web/WebHost.cs` 的 `_openBrowser` 改名 `_autoOpen`、`Start()` 调 `LaunchWebUi`、`Url` 改为 `http://127.0.0.1:{port}/webui/`；`Config.cs` 的 `WebSection.OpenBrowser` 注释改为"自动打开内置 Web UI 窗口"。
- 验证：`--web` 启动后进程为 `HeartRateMonitor=1 + hrm-webui=1 + msedgewebview2=12`（WebView2 多进程模型正常）。

### 步骤 44：Phase A2 — 数据层 + 12 页全量接后端
- `api/index.ts`（REST 封装）、`api/ws.ts`（单连接 WS + 指数退避重连 + 按 type 订阅）、`stores/app.ts`（全局快照 + WS 订阅 + 5s 轮询）、`stores/osc.ts`（参数聚合表，count 为累计值故 replace 合并 + 600 上限）、`stores/ui.ts`（外观本地即时生效 + `POST /api/settings {ui}` 持久化）。
- 12 页：Overview（六统计卡 + 曲线 + 8 快捷卡 + 设备速览）、HeartBeat（BPM 大字 + `BpmChart` + 健康 + 扫描/记录/浮窗 + 设备行）、Devices（勾选批量 connect/disconnect/save/block + 重命名 + RSSI/reportHz/重连态）、OscMonitor（统计卡 + 按地址首段分组表）、Pusher（OSC 表单 + 测试 + Webhook CRUD/启停/测试）、Hardware（变量表 + 前缀筛选 + 采集设置）、HeadSet（WIP）、ApiServer（启用/令牌/回传/节流 + 端点复制 + 变量白名单）、Settings（语言/主题/配色/圆角/密度 + 关于）、Logs（正则/等级/跟随/导出）、Console（真实 `/api/cli` + ↑↓ 历史）、Monitor（六卡 + `BarChart` 趋势/直方 + 健康/设备/OSC Top + 导出）。
- 组件：`BpmChart.vue`（canvas 折线，DPR 自适应 + ResizeObserver + 网格 + 渐变面积 + 末点 + 极值标注）、`BarChart.vue`（等宽竖条 + `highlightLast`）—— 不引 echarts。
- **踩坑**：`Settings.vue` 的 `savedHint` 用普通对象不响应 → 改 `ref('')` + 1.6s 自动清除；`DeleteFile` 工具删不掉被占用文件（`Logs.tsx`）→ 用 PowerShell `Remove-Item -Force`；`build.ps1` 直跑不能加 `2>&1 | Select-String` 管道（cargo/dotnet 写 stderr 会被 PowerShell 判定 `NativeCommandError`）。

### 步骤 45：前后端契约复检与修复（"无法连接 / 心率无曲线图"）
- 需求：`目前新前端的功能并未全部正确接入后端(如无法连接 心率无曲线图)` / `复检关键点和可能存在的潜在bug、对比前端和后端引擎逻辑，修复后再正常推进下一步`。
- 复检方法：逐条对照 `App/Web/WebServer.cs`（REST 路由表 + WS 信封）、`App/Core/AppHub.cs`（各命令签名 / `Push` 事件载荷 / `ApplyOsc` 取值类型）、`App/Health/HealthService.cs`（`Snapshot()` 字段），并用 `git show 993dad0:WebUI/src/api.ts` 取 V2 已验证正确的路径做交叉验证。
- **Bug 1 —— WS 事件字段全部读到 undefined（"心率无曲线图"根因）**：后端 `WebServer.Broadcast` 发的是信封 `{type, data, ts}`，事件字段在 **`data` 内层**；`api/ws.ts` 却把顶层对象直接分发，于是 `m.bpm`/`m.line`/`m.vars`/`m.items` 恒为 `undefined` —— 心率曲线永不追加点、日志/OSC/硬件实时流全部失效。
  - 修复：`onmessage` 解包 `dispatch(env.type, env.data)`，订阅回调签名改为 `(data, type)`；`app.ts`/`osc.ts` 各订阅点按 `data` 内层取字段。
- **Bug 2 —— 7 处 REST 路径 + 3 处参数名不匹配（"无法连接"根因）**：`/api/connect`→`/api/device/connect`、`/api/disconnect`→`/api/device/disconnect`、`/api/save`→`/api/device/save`、`/api/block`→`/api/device/block`、`/api/batch`→`/api/devices/batch`、`/api/webhook`→`/api/webhooks`、`/api/float/close`→`/api/float/close_all`；`oscConnect` 参数 `{connect}`→**`{connected}`**（后端 `body["connected"].GetValue<bool>()`）；`oscTest()` 空体 → `{ip,port,address,text}`（后端签名 `OscTest(ip, port, address, text)`，缺参会落到默认值而不是当前配置）；`floatOpen(id)` → `{ id: id ?? '__main__' }`（`FloatWindowHost.MainId`；原先主窗按钮传 `'hr'` 会开出一个源为 `hr` 的野窗口）。`/api/heartbeat` 实际路径是 **`/heartbeat`**（在 `/api/` 分支之前判定），同步纠正；补 `logsDump()` → `POST /api/logs/dump`（Logs 页加「转储」按钮）。
- **Bug 3 —— OSC 端口/间隔类型不符**：`Config.Osc.Port/IntervalMs/ReceivePort` 在 C# 里是 **string**，`ApplyOsc` 用 `JsonValue.GetValue<string>()` 读取；前端 `types.ts` 声明为 `number`、`Pusher.vue` 用 `v-model.number`，保存会让后端抛异常（配置静默不生效）。改为 `string` 全链路（表单去掉 `.number`，回填用 `String(...)`）。
- **Bug 4 —— 健康状态永远显示"未知"**：后端 `health.status` 是固定中文文本（睡眠/静息/活跃/兴奋），前端却按英文 key 判定。改用后端已给的 `health.statusKey`（本身就是 `hb.status.*`）；`Monitor` 页读的是 `health_records.status` 历史文本，保留中英双向文本匹配。`types.ts` 的 `HealthSnapshot` 补 `statusKey/restingSd/calibratedAt/calibrating/calibrateRemain`，`HrWindow.sources` 由 `string[]` 改 `Record<string,string>`（后端是 `Dictionary<string,string>`）。
- **Bug 5 —— `wsConnected` 不是响应式**：原为普通对象 `{ value: false }`，`computed(() => wsConnected.value)` 不会重算，状态栏 WS 指示恒定。改 `ref(false)`。
- **Bug 6 —— ApiServer 页整页空白（复检时新发现）**：`api.wshint` 文案含 `{"id":1,"cmd":"status"}`，vue-i18n 默认把 `{...}` 当插值编译 → 运行时 `SyntaxError: 2`，该页 render 直接失败（headless DOM 只剩 `<!---->`）；`{bpm}`/`{变量}` 等占位符也会被替换成空串。修复：`i18n.ts` 传 `messageCompiler` 直接返回原文（本项目文案是纯静态文本，不用插值/复数）。
- 顺带对齐：`OscMonitor` 的值格式化按后端真实结构 `{t,v}` 取值（原先 `Object.values()[0]` 会取到类型标记 `f`）；HeartBeat 加「关闭全部悬浮窗」；`BpmChart` 空态文案改用 i18n `hb.waiting` 并在切语言时重绘；`app.ts` 增 `device_found` 300ms 合并刷新、`sysinfo` 1s 合并拉 `/api/hw`、WS 断线时用轮询值补曲线点。
- 验证（`Built\debug-20260904_043346`，`--web`）：
  - `vue-tsc` 0 error；`build-webui.ps1` 1630 modules；`build.ps1 -Mode debug` 全链路 ok（webui/hrm-ui/hrm-webui/image 全 ok）。
  - REST 契约烟测 **20/20 200**（含 device/connect、devices/batch、osc/test、webhooks、float/open、float/close_all、logs/dump）。
  - WS 解包烟测：`scan{scanning}`、`log{line}`、`osc_status{connected}`、`device_found{mac,rssi}`、`osc_params{items[{addr,args,count}],recv}`、`sysinfo` 字段全部可读。
  - 真机链路：扫到 5 台设备 → 连接 Xiaomi Smart Band 9 Pro → `connected` 事件 + `heart_rate{bpm:84}`、`/api/status bpm=85 connected=1`。
  - 曲线（CDP 读 canvas 像素）：`px=10026 → 14361`（12s 内持续增长），BPM 大字 75 → 74 实时更新 —— 曲线确实在画。
  - 12 路由 headless DOM 渲染全部有内容（apiserver 修复后 8099B/0 卡 → 37581B/9 卡）；OSC 监视页显示 `/avatar/parameters/HrTest 0.75 ×3`、`/tracking/head/y -1.5 ×2`；i18n 占位符 `{bpm}`/`{"id":1,"cmd":"status"}` 原样呈现；`t()` 用到的 126 个 key 全部命中 234 键词表。

### 步骤 46：Phase A5 首批 — 心率/设备高级设置补齐（V2 功能对齐）
- 需求：把 V2 WebUI / Rust UI 已提供、V3 尚缺的本页高级设置补齐（对应 plan Phase A5 的子项）。
- `BpmChart.vue`：新增 `smooth`（移动平均窗口，1=不平滑，对应 Rust `hb_smooth`）与 `points`（只画最近 N 点，对应 `hb_points`）两个 props；`series()` 先截窗再均值（窗口不足时用已有点求均值不丢头部）；`draw()` 消费 `series()`，watch 同时监听 smooth/points。
- `views/Heartbeat.vue` 扩为「主卡 + 操作行 + 四设置卡 + 设备列表」：
  - 曲线卡：平滑度 1–30 / 窗口 30–180（localStorage 记忆 `hb-smooth`/`hb-points`，重置按钮）。
  - 健康卡：开始/取消校准（`POST /api/health {action:calibrate|cancel}`，校准中显示剩余秒数 `calibrateRemain`）、静息基准展示；睡眠/活跃/兴奋系数、波动告警、状态入库（`GET|POST /api/health/config`）。
  - 悬浮窗卡：主窗数据源下拉（平均 + 已连/已存设备）、刷新间隔、锁定/解锁、关闭全部（`POST /api/float/config {source,refreshMs}`、`/api/float/lock`）；数据源来自 `status.hr.window`。
  - 数据导出卡：表（hr_records/health_records/osc_records/variables）× 格式（json/yaml/csv/sqlite）× 行数 → `POST /api/export`，回显导出文件路径。
- `views/Devices.vue` 加「设备策略」卡：持续扫描 / 刷新节流 / 信号弱阈值 / 自动重连 / 重试间隔 / 放弃时限 → `GET|POST /api/devices/config`。
- `api/index.ts`：`health` 拆 `healthAction`（get/calibrate/cancel）；`healthConfig`/`devicesConfig` 支持无 body 读、带 body 写；`export` 返回类型补 `file/error`。
- i18n：lang.ts 加 `hb.cancelcal`、`dev.policy` 两个 key ×5 语言，`lang2json` 重新生成 → **236 keys**。
- 验证（`Built\debug-20260904_054305`）：`vue-tsc` 0 error；headless DOM 确认 heartbeat 页含「开始校准/睡眠系数/主窗数据源/数据导出/静息基准」、devices 页含「设备策略/持续扫描/自动重连/信号弱阈值」；后端 `GET /api/health/config`、`/api/devices/config`、`/api/float/config` 均返回与表单对应的字段（sleepFactor 0.88 / refreshThrottleMs 400 / source 平均）。

### 步骤 47：Phase A5 收尾 + Phase A6 测试与 Release
- 需求：`完成A5和A6的剩余部分`（A5 = Dashboard 面板化 / 硬件变量管理 / 五态主题走查 / 图标图片收敛；A6 = vitest / releases+standalone 构建复验与独立目录冒烟）。

**A5-1 硬件页变量管理面板（`views/Hardware.vue` 重写）**
- 补齐 `POST /api/hw/var` 的四类操作 UI（V2 Rust UI 有、V3 之前缺）：覆写值 `override` / 单位后缀 `unit` / 显示改名 `rename` / 自定义变量 `custom`（表达式 expr、拼接 concat、正则 regex、命令 cmd 四种 kind，regex 才显示 pattern 输入）。
- 关键契约：`value` 字段**缺省即表示删除该项**，所以表单留空时不传 `value`（提示文案 `hw.clearhint`）；`GET /api/hw/config` 返回 `ntpPresets/units/overrides/renames/custom`，采集设置卡因此多了 NTP 服务器 + 预设下拉。
- 变量表每行加铅笔按钮 `pickVar(k)` 一键把该变量装载进表单；被覆写/改名/加单位的变量在表格里打 `●` 标记（`tweaked` 计算属性取三个 map 的 key 并集）。

**A5-2 Dashboard 面板化工作台（新路由 `/dashboard`）**
- `dashboard/panelRegistry.ts`：`PanelDef { id, titleKey, component, span:1|2|3 }` + 8 个面板（bpm/curve/actions/health/devices/osc/hw/log）+ `DEFAULT_LAYOUT`；新增面板只改这一处。
- `components/panels/*.vue` 8 个：BPM 大字、曲线（复用 `BpmChart`，读心率页的 `hb-smooth`/`hb-points` 偏好）、快捷操作、健康、设备前 6 台、OSC 开关与计数、硬件六项、日志尾 12 行。
- `views/Dashboard.vue`：拖面板标题排序用 **HTML5 原生拖放**（不引 dnd 库），`onDrop` 用 `splice` 把源面板插到目标位；隐藏/恢复/重置布局；顺序与隐藏项存 localStorage（`hrm-dash-order`/`hrm-dash-hidden`），读取时过滤已下线 id 并把新注册面板补到末尾。
- 3 列网格（1100px → 2 列，720px → 1 列），`gridColumn: span N` 取自面板定义；拖拽中 `opacity .5`、目标虚线 outline。
- 侧栏 NAV 由 12 → **13 项**（`/dashboard` 插在 overview 之后），图标 `Grid2x2`。

**A5-3 五态主题走查（脚本化，非肉眼）**
- 走查方式：Edge `--headless=new` + CDP `Runtime.evaluate`，逐主题 `POST /api/settings {ui:{theme}}` → 重载页面（走的是 Settings 页同一条 `config.ui → ui.pull()` 链）→ 逐路由采样。
- 每个采样点读 `data-theme` / `.dark` class / 正文字数 / 卡片数 / 导航高亮数，并用 canvas 取 token 实色算 **WCAG 对比度**（7 组配对）：`fg/bg`、`cardfg/card`、`muted/card`、`primaryfg/primary`、`navfg/nav`、`primary/card`、`border/card`。
- 结果 **65 组（5 主题 × 13 路由）全通过**，正文/侧栏文字 ≥ 4.5:1（AA），次要文字与强调色 ≥ 3:1：

  | 主题 | fg/bg | cardfg | muted | pfg/p | navfg | p/card |
  |---|---|---|---|---|---|---|
  | dark | 17.18 | 17.18 | 6.94 | 14.23 | 17.65 | 14.23 |
  | light | 19.80 | 19.80 | 4.74 | 17.18 | 18.97 | 17.93 |
  | forest | 16.81 | 15.83 | 6.40 | 9.12 | 17.67 | 7.81 |
  | sunset | 17.10 | 16.05 | 6.48 | 8.73 | 17.70 | 7.51 |
  | custom | 17.22 | 15.51 | 6.26 | 7.14 | 15.51 | 5.94 |

  （light 主题 `border/card` 只有 1.26 —— 这是分隔线不是文字，shadcn zinc light 本身就这样，保留原设计。）
- 13 路由内容量核对（dark）：overview 403 字/16 卡、dashboard 421/8、heartbeat 770/6、devices 752/2、hwinfo 5651/4、apiserver 3170/3、logs 1128/1 …… 无空页、无半渲染，导航高亮恒为 1 项。

**A5-4 图标 / 图片资源收敛**
- 全量扫描确认 Vue 侧**没有** `<img>`/`<svg>`/`background-image`/emoji，图标 100% 走 `lucide-vue-next`（13 个文件按需具名导入，NavMenu 13 图标一一对应）。
- `index.html` 补站点图标：内联 `data:image/svg+xml` 的 lucide heart-pulse。原因是后端静态服务对未命中路径会回退 `index.html`，没有 favicon 时浏览器请求 `/webui/favicon.ico` 会拿到一份 HTML（`Content-Type: text/html`, 434B）当图标。
- 清掉最后两处硬编码中文：`NavMenu.vue` 折叠按钮 `title`（新增 `nav.collapse`/`nav.expand`）、`MainLayout.vue` 品牌文字（改用已有 `nav.brand`）→ lang 表 **247 → 249 keys**，`lang.rs` 同步。
- `image/` 目录存在但为空（`app.ico` 尚未放入），`build.ps1` 3d 段照常复制，壳 `MainForm` 找不到图标时静默跳过 —— 属预期，非缺陷。

**A6-1 vitest 引入（29 个测试）**
- 依赖：`vitest@3.2.7` + `@vue/test-utils@2.5.0` + `jsdom@26.1.0`（devDeps）；`vitest.config.ts` 单独一份（测试不要 base/proxy，要显式 jsdom）。
- **踩坑（同 '#' 路径老问题的新变体）**：直接在项目内跑 vitest 报 `Cannot find module '/@vite/env'` —— vite-node 把含 `#` 的路径当 URL 片段（栈里能看到 `C%23%20Test`），一个测试也收集不到。目录联接也不行（解析出的真实路径仍含 `#`）。
- 解决：新增 `WebUI/test-webui.ps1`，用 `robocopy /MIR` 把 `src` + `node_modules` **实体同步**到 `%TEMP%\hrm-webui-test` 再跑（与 `build-webui.ps1` 同一套规避思路，增量同步所以第二次很快）；`npm run test` 改为调这个脚本。
- 测试文件：
  - `src/i18n.spec.ts`（10）：`lang.ts` 键集无重复；五个 locale JSON 与 `lang.ts` 同键同数且无空值；`hb.status.*` 五个状态齐全；13 个 Tab 与 8 个面板标题都有译文；面板 id 唯一且 span ∈ 1~3；`DEFAULT_LAYOUT` 与注册表一一对应。为此 `lang.ts` 导出 `TABLE_KEYS`。
  - `src/theme.spec.ts`（5）：五主题都有五语言标签；只有 light 不加 `.dark`（其余四态深色底）；custom 注入 accent/bg/panel 与 rgb 三元组；非法 accent 回退默认蓝、圆角 2px 下限、density 写入 `--sp`；`storedLook` 合并默认值且坏 JSON 不炸。
  - `src/stores/app.spec.ts`（5）：mock `api`/`api/ws`，验证 `sync()` 填充、WS 断开时补曲线点、`heart_rate` 更新 bpm/曲线/对应设备且忽略 `bpm<=0`、`devices/scan/osc_status/health_status/sysinfo_vars/log` 各自生效（`osc_status` 必须局部合并不丢 ip）、曲线上限 180 点。
  - `src/components/BpmChart.spec.ts`（4）：stub 2d 上下文记录 moveTo/lineTo/fillText，验证空态文案、`points` 截窗后点数、`smooth` 改变取值但不改点数、非正数/NaN 被过滤、极值标注取平滑后的 max/min。
  - `src/views/Dashboard.spec.ts`（5）：mock panelRegistry 为三个哑面板，验证默认顺序、隐藏/恢复、拖 A 放 C 后顺序变 B C A、重置布局、localStorage 里的过期 id 被丢弃且新面板补到末尾。
- **踩坑**：`vi.mock` 工厂会被提升到文件顶部，引用顶层变量报 `There was an error when mocking a module` → 桩数据全部挪进 `vi.hoisted()`；`api.status()` 返回值要 `structuredClone`，否则 store 直接改写共享的设备对象导致用例互相污染。
- `tsconfig.json` 的 include 补 `vitest.config.ts`；`vue-tsc --noEmit` 仍 0 error。

**A6-2 releases / standalone 构建复验 + 独立目录端到端冒烟**
- `build.ps1 -Mode releases`：HeartRateMonitor.exe 26,240,384B（框架依赖单文件）、16 个文件，webui/hrm-ui/hrm-webui/image 全 ok。
- `build.ps1 -Mode standalone`：HeartRateMonitor.exe 134,840,316B（self-contained 单文件）、15 个文件，同样全 ok。
- 两个产物各自改 `config.web.port`（8231 / 8232）后 `--web` 起在独立目录跑同一套冒烟（`node smoke-release.mjs`），各 **27 项全过**：
  - REST 17 条 GET（status/devices/config/health/health-config/devices-config/hw/hw-config/osc-params/logs/webhooks/float-config/apiserver-config/cli-help/monitor/sysinfo/heartbeat）+ 3 条 POST 全 200；
  - 静态 4 条：`/webui/`、`/webui/index.html`、`/webui/#/dashboard`、`/webui/no-such-route` 都返回带 `<div id="app">` 的 index.html（SPA 回退正确）；
  - 打包资源 2 个（index js/css）实拉非空；
  - WS `/ws` 连上后 6s 内收到 `response`（下行命令回执）与 `sysinfo`（周期广播）。
- 壳窗口：两个目录各自 `hrm-webui.exe --port N` 都拿到 `MainWindowTitle = HeartRateMonitor · Web UI`（窗口句柄非 0）。
- 13 路由 DOM 内容在 standalone 目录二次核对通过（hwinfo 5643 字/229 控件、apiserver 3170 字，其余同上）。
- `cargo build --release` 通过（`lang.rs` 新增 15 个 key 后无编译问题，仅 3 个既有 `trace.rs` unused import 警告）。
- 收尾：两个测试用产物的进程已停止，主开发实例（`debug-20260904_054708`）保持运行。

### 步骤 48：Phase Ext — 设备连接优化 / 断连遮罩 / 曲线数据源 / OSC 双 Tab / Trace 等级 / 单实例 / 动画与多端 / WebView 壳
- 需求：`开始实现Phase Ext中的功能`（plan.md 第 155–193 行 13 项，本轮完成 9 项，剩 LocalWeb/ExtWeb 双路径与 Build 二级菜单）。

**Ext-1 设备连接优化（plan ④）**
- `BleManager`：新增 `ConnectResult { Ok, NoHeartRate, Failed }` 与 `TryConnectAsync`（原 `ConnectAsync` 变成它的 bool 包装）；`_connecting` 并发字典 + `IsConnecting(mac)` + `Connecting` 事件，点连接立刻有「连接中」态；名称走 `Named(name, mac)` 过滤 —— 空 / `UNKNOWN` / 等于 MAC 都算「没拿到标识符」，不覆盖已缓存的广播名（这就是「连接后名字变 MAC」的根因）；无心率特征改为返回 `NoHeartRate` 而不是 throw。
- `DeviceRegistry`：`Segments(name)` 按空格切词并对品牌/关键词命中打 `Hit`（前端渲染成加粗斜体）；`SignalLevel(rssi)` → `ok/weak/critical`；`Category(mac)` → `hr/audio/home/generic`（决定图标）；`StartAutoDetect/StopAutoDetect` 按综合分降序逐台试连，单台超时用 `Task.WhenAny(connect, Task.Delay(timeout))`，命中 `NoHeartRate` 的设备置 `NoHrChar=true` 永久跳过，音频/智能家居类不测；`AutoConnectLast()` 开扫描等 2.5s 后试连历史首项。
- `AppHub.DeviceJson` 用 `FirstNamed(mac, e?.Name, dev?.Name, k.name)` 定优先级，并补 `nameSegs/connecting/signal/category/noHrChar` 五个字段；新增 `AutoDetect(action)` 命令、`POST /api/devices/autodetect` 路由、CLI `detect [start|stop]`。
- `Config.DevicesSection` 加 `RssiCriticalThreshold=110 / AutoConnect / AutoDetect / AutoDetectTimeoutSec=12`；`Program.cs` 启动时二选一执行（AutoConnect 优先，避免两者抢扫描）。
- 前端：`DeviceDot`（五态：空心 / 实心+呼吸 / 绿 / 橙 / 红）、`DeviceName`（按 nameSegs 渲染 `<em class="hit">`）、`DeviceIcon`（category → lucide 图标）三个组件，Devices/Heartbeat/DevicesPanel 共用；设备行套 `TransitionGroup name="dev-row"` 做「扫描到新设备」淡入下滑。

**Ext-2 后端断连遮罩（plan ⑧）**
- `api/watchdog.ts`：`PROBE_TIMEOUT_MS=100`（`AbortController` 硬截断打 `/api/status`）、`MAX_ATTEMPTS=30`、`RETRY_GAP_MS=400`，导出 `offline/gaveUp/attempt/lastError` 与 `notifyDown/notifyUp/retryNow/setRecoverHandler`。
- `api/index.ts` 的 `req()` 包 try/catch → `notifyDown`，成功 `notifyUp`；**只有 5xx 才进离线态**（4xx 是业务错误，不能触发全局遮罩）；`ws.onclose` 也报 down；`stores/app.ts` 注册 `setRecoverHandler(() => { connectWs(); void sync(); })`。
- `OfflineOverlay.vue`：重连中 `Loader2` 旋转 + `attempt/30` + 错误行；30 次失败后 `AlertTriangle` + 可点击复制的诊断块（time/url/attempts/error/ua）+ 三按钮（重试 / 配置导出 / 关闭标签页）；`window.chrome.webview` 存在时隐藏「关闭标签页」（壳里没有标签页可关）。

**Ext-3 曲线数据源与显示模式（plan ⑨）+ 底栏设备细分（plan ⑩）**
- `prefs.ts`（新）集中管理 `hbSmooth/hbPoints/hbMode/hbSource/hbMainSource`（localStorage 持久化），心率页与 Dashboard 的 BpmPanel/CurvePanel 共用同一份偏好。
- `stores/app.ts` 增 `deviceCurves`（mac → 最近 180 点）+ `bpmOf(source)` / `curveOf(source)`，`heart_rate` 事件与 WS 断线轮询都补设备曲线；数据源可选「主显示 / 平均 / 每台设备」。
- `BpmChart` 增 `mode` prop：`area/line/bars/dots` 四种画法（bars 用 `fillRect`、dots 用 `arc`），末点与极值标注四种模式共用。
- `StatusBar` 中段改 `.hr-strip`（`flex:1; overflow-x:auto; scrollbar-width:none`）逐台渲染 chip（DeviceDot + 截断名 + BPM），>1 台追加虚线「平均」chip；两侧固定项 `flex-shrink:0`，设备数不设上限、超宽即横向滚动。

**Ext-4 OSC 页概览/数据双 Tab（plan ⑥）**
- `OscMonitor.vue` 顶部 `.x-subtabs`（新增到 globals.css）两个子 Tab，选择存 `localStorage['hrm-osc-tab']`，默认概览。
- 概览 6 张分区卡：Avatar（id/参数总数）、世界房间、玩家状态（AFK/Seated/Grounded/Upright/MuteSelf/InStation/VRMode/TrackingType/Voice）、移动速度、手势表情、World Transform，未收到的地址显示 `—`；跨列自定义参数 chips。数据 Tab 保留原按首段分组的地址表。
- `stores/osc.ts` 补 `value(addr)`（取末位参数的 `.v`）/ `has(addr)` / `count(addr)`。

**Ext-5 Trace 日志等级（plan ⑤）**
- 关键前提：`HrmTrace` 原来挂 `[Conditional("DEBUG")]`，**release 构建下所有调用点会被编译器整个剔除**，所以「设置里开启」在 release 永远无效。改为去掉 `Conditional`、方法首行判 `App.TraceEnabled` 的运行时开关（关闭时每个点只剩一次静态 bool 读）。
- `App.TraceEnabled` 在 `Program.Main` 启动时从 `Config.Logs.TraceEnabled`（或 `--trace`）快照一次，运行期改配置不改这个字段 —— 这就是「重启后生效」的实现方式，避免半开状态下 trace 日志断续。
- `Logger.Trace(msg)` 走等级 `TRACE`，`HrmTrace.Event` 除写 `logs/trace.log` 外同时经 `App.Log.Trace` 进入应用内日志缓冲，日志页可筛。
- `AppHub`：新增 `LogsConfig()`（`traceEnabled` 配置值 + `traceActive` 当前进程实际状态），`Config()` 的 `logs` 段改用它；`ApplySettings` 支持写 `logs.traceEnabled`。
- 前端 `Logs.vue`：`LEVELS` 加 `'TRACE'`、`lineColor` 把 TRACE 归到弱化色、底部加 Trace 开关 + 本次运行状态 + 「需重启生效」提示；`RustUi` 的 `LOG_LEVELS` 与 `log_lv` 数组同步 4 → 5。i18n 新增 `logs.trace/tracehint/tracerestart/traceactive/traceinactive`（291 → **296 keys**）。

**Ext-6 单实例互斥（plan ⑪）**
- 新增 `App/Core/SingleInstance.cs`：命名 Mutex `Local\hrm_v1_single_instance`；`createdNew=false` 时再 `WaitOne(200)` 兜一次崩溃残留的弃置锁（`AbandonedMutexException` 也视为可用）；内核对象不可用（权限/沙箱）时不拦启动。
- `ProcessInfo.ActivateExistingUi()`：按 `hrm-ui → hrm-webui → HeartRateMonitor` 顺序找有窗口的进程，`IsIconic` 则 `ShowWindow(SW_RESTORE)`，再 `SetForegroundWindow`；一个窗口都没有（旧实例只剩托盘）时返回 false，由 `SingleInstance.NotifyRunning()` 通过 IPC 管道发 `{"cmd":"activate"}` 让旧实例自己拉起前端（`AppHub.Activate()`）。
- `Program.cs` 在进入 GUI 分支时抢锁，失败即转旧实例后 return；`config.app.allow_multi_instance` 或 `--multi` 放行多实例（调试用）；CLI 模式不受限（不占管道/托盘，允许与 GUI 并存诊断）。

**Ext-7 CSS 动画 + 手机/平板适配（plan ③⑦）**
- 侧栏折叠动画：`nav-label` / `brand-title` 从 `display:none` 改为 `max-width: 160px → 0` + `opacity` 过渡，文字随宽度一起「滑进去」而不是瞬间消失。
- 路由切换：`MainLayout` 的 `RouterView` 外层套 `<Transition name="page" mode="out-in">`，0.16s 淡入上移（再长手感就钝了）。
- 响应式：`@media (max-width: 860px)` 侧栏改 `position: fixed` 抽屉（`transform: translateX(-100%)` 收起）+ `.nav-scrim` 遮罩（点击收起）+ `.nav-fab` 浮动菜单按钮（收起时的唯一入口）；`--ctl-h` 提到 36px 满足触摸目标。`@media (max-width: 560px)` 再收紧卡片内距/状态条字号，并用 `.x-input,.x-select,textarea { max-width:100% !important }` 压掉各页面内联写死的 240px 等宽度（内联样式优先级更高，只能 `!important`），`.x-gap` 强制 `flex-wrap`。
- `MainLayout` 脚本侧同步：`narrow` 响应 resize，窄屏首次进入默认收起、切路由后自动收起、宽窄切换时联动 `collapsed`。
- `prefers-reduced-motion: reduce` 下所有新增过渡/动画一并关掉。

**Ext-8 WebView2 壳窗口（plan ②）**
- `Shells/WebView2Host/MainForm.cs`：默认客户区 1440×900 DIP（能完整容下 224px 侧栏 + 三列面板），最小 1024×640 DIP；`WM_SIZING` 里把宽高比夹在 1.05–2.60（左右边调宽度、上下边与四角调高度），避免拖成不可用的细长条。
- F11 全屏（去边框 + 最大化，退出还原原边框与窗口状态）、F5 重载、F12 DevTools；`KeyPreview=false` 且同时挂 `Form.KeyDown` 与 `_web.KeyDown` —— WebView2 焦点在网页内时窗体收不到 KeyDown，而 `CoreWebView2` 在当前 SDK（1.0.4191.47）里**没有** `AcceleratorKeyPressed`（那是 `CoreWebView2Controller` 的事件），所以只能走控件自己的 KeyDown。
- 尺寸 + 缩放持久化到 exe 同目录 `webui-shell.json`（`Width/Height/Maximized/Zoom`）：`ZoomFactorChanged` debounce 1.2s 落盘，关闭时再存一次；最大化/全屏时记 `RestoreBounds` 换算的还原尺寸。
- **DPI 踩坑（实测 288 DPI / 300% 缩放）**：PerMonitorV2 下 `ClientSize`/`MinimumSize` 都是物理像素，而 WebView2 的 CSS 像素 = 物理像素 / 缩放倍率。第一版直接把 1440 当物理像素写，结果窗口只有 480 CSS 像素宽，前端直接掉进 860px 窄屏抽屉布局。改为：DIP 常量经 `Px(dip) = dip * DeviceDpi / 96` 换算，尺寸放到 `OnHandleCreated`（构造期 `DeviceDpi` 还是 96），`OnDpiChanged` 重算最小尺寸，存盘时再除回 DIP。修完实测 `clientPhysical=4320x2700, dpi=288 → cssPx=1440x900`。
- 兜底：`main.ts` 检测 `window.chrome.webview` 给 `<html>` 加 `in-shell` class，860px 抽屉断点整体加 `html:not(.in-shell)` 前缀 —— 高 DPI + 用户手动缩放下壳的 CSS 宽度仍可能低于 860，但它是桌面窗口，不该退化成手机抽屉。

**验证（`Built\debug-20260904_092653`）**
- `npm run locales` 296 keys；`vue-tsc --noEmit` 0 error；`npm test` **29/29**；`dotnet build`（App + WebView2Host）0 error；`cargo build --release` 通过。
- 后端契约冒烟（node，`hrm-ext-rest.js`）**31/31**：14 条 GET + 3 条 POST 全 200；`config.logs` 含 `traceEnabled/traceActive`；真机扫描后 `device.nameSegs=[{Redmi,hit:true},{K70,hit:false}]`、`signal=ok`、`category=hr`、`connecting/noHrChar` 齐全；`devices/config` 五个新字段齐全；WS `cmd=activate` 返回 `{ok:true}`（未落到 `unknown cmd`）并收到推送事件。
- 前端冒烟（Edge headless + CDP，`hrm-ext-smoke.js`）**14/14**：桌面 1440 宽侧栏为固定列且无遮罩/FAB；Logs 页五个等级按钮含 TRACE、Trace 开关勾选态与后端一致、有「重启生效」提示；420 宽下侧栏 `translateX(-232)` 移出、出现 FAB、`scrollWidth == innerWidth`（无横向溢出）、点 FAB 展开出遮罩且侧栏回到 `transform:none`、点遮罩收起；820 宽 Dashboard 降为 2 列。
- Trace 端到端：`--trace` 启动 → `traceActive=true`，`GET /api/logs?levels=TRACE` 命中 29 行（`cmd.health get` 等），`logs/trace.log` 落盘；`POST /api/settings {logs:{traceEnabled:true}}` 后 `config.json` 写入 `"trace_enabled": true` 而 `traceActive` 不变（重启才生效的语义正确）。
- 单实例：已有实例运行时再启一个 → 新进程退出、`instances=1`、日志 `检测到已有实例在运行，转到旧实例`。
- 壳持久化：删 `webui-shell.json` 冷启动 → `cssPx=1440x900`；写入 `{Width:1180,Height:760,Zoom:1.25}` 重启 → `cssPx=1180x760` 且关闭后原样回写。

### 步骤 49：WebView 转正为主前端 + 仿 macOS 无边框壳 + `_web` 副本拆分 + RSSI/双频率修正 + 设备页表格化
- 需求：`1::` EGUI 降为回退、WebView 为主前端、壳去掉 Windows 顶栏改仿 Mac + 可配圆角（不影响控件）；`2::` 有差异的前端部分建 `_web` 副本；`3::` 更新 about.md；`4::` 修 RSSI 锁死 -127dBm、拆出「报告频率」；`5::` 设备页表头 + 列对齐 + 可拖拽列宽（持久化）。

**49-1 RSSI 锁死 -127dBm（需求 4 前半）**
- 根因不是硬件：WinRT `RawSignalStrengthInDBm` 拿不到信号强度时返回哨兵值 **-127**，而设备一旦连接，Windows 就不再向应用投递它的广播包，于是 RSSI 永久停在 -127。
- `DeviceRegistry`：加 `RssiUnavailable = -127` 与 `Entry.HasRssi`；`Observe()` 只在 `rssi != 0 && rssi > RssiUnavailable` 时写入；`OnConnected(mac)` 直接把 `HasRssi=false`（连接后广播不再来）；`WarnIfWeak()` / `Explain()` 在无有效 RSSI 时既不判弱也不给分（否则恒定 0 分会把设备排到底）；`SignalLevel(rssi, hasRssi)` 多一个 `unknown` 档。
- 三端显示统一：Web 侧 `hasRssi ? '${rssi} dBm' : '—'`（Devices/Heartbeat/Overview/DevicesPanel），Rust 侧 `rssi_text()` 给 `--`，CLI `devices` 命令给 `  --`；`dev.rssihint` 解释为什么是 `—`。

**49-2 广播频率 / 报告频率拆分（需求 4 后半）**
- 原来只有一个 `ReportHz`，算的其实是**广播包间隔**却被标成「报告频率」。现拆成两个量：`AdvHz`（广播间隔倒数，仅扫描期可得）与 `NotifyHz`（GATT 心率通知间隔倒数，仅连接期可得，EMA 0.7/0.3）。
- `OnReport(mac)` 更新 `NotifyHz`，`OnDisconnected(mac)` 清零；`AppHub.DeviceJson` 下发 `advHz` / `notifyHz`（未连接时 notifyHz 归 0）。
- 实时刷新不靠 5s 轮询：`OnHeartRate` 的 `heart_rate` WS 事件直接带上 `notifyHz`，前端 store 收到就更新对应设备。
- i18n 新增 `dev.advrate` / `dev.notifyrate` / 两条 hint（与 `lang.rs` 同步）。

**49-3 设备页表格化 + 可拖拽列宽（需求 5）**
- `Devices.vue` 改 CSS Grid：表头与数据行共用同一份 `grid-template-columns`（`:style` 注入像素宽）才能严格对齐；容器 `width: max-content; min-width: 100%` 撑开父元素触发 `.dev-scroll` 的横向滚动；单元格 `overflow:hidden; text-overflow:ellipsis` 防溢出。
- 10 列：勾选 / 状态 / 图标 / 名称 / MAC / 信号 / 广播频率 / 报告频率 / BPM / 操作。表头分隔线是 7px 命中区（`cursor:col-resize`），拖拽用 Pointer Events + `setPointerCapture`（拖出表头也不断连），每列有 `min` 下限；列宽写 `localStorage['hrm-dev-cols']`，另给「重置列宽」按钮。
- 代价：原先整行的 `TransitionGroup name="dev-row"`（扫描到新设备淡入）去掉了 —— grid 直接子元素是单元格而不是行，整行过渡无从附着。

**49-4 无系统顶栏 + 仿 macOS 标题栏 + 可配圆角（需求 1 前半）**
- 宿主 `Shells/WebView2Host/MainForm.cs`：`FormBorderStyle.None` 去掉系统顶栏；`Padding = Px(6)` 让出一圈作为 `WM_NCHITTEST` 的八向缩放热区；`CreateRoundRectRgn + SetWindowRgn` 做自定义圆角（`CornerPx` 默认 10、0~24，落盘 `webui-shell.json`），最大化/全屏时取消圆角。
- **坑：`FormBorderStyle.None` 会同时去掉 `WS_THICKFRAME`**，而 `DefWindowProc` 的 `SC_SIZE` 缩放循环与 Aero Snap 都依赖它 —— 只返回 `HTLEFT` 之类命中码并不足以真的能拖动缩放。解法是重写 `CreateParams` 把 `WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX` 加回去，再用 `WM_NCCALCSIZE` 返回 0 把它带来的非客户区厚度抹平（视觉上仍是完全无边框）。
- 消息协议（页面 → 宿主）：`win.drag`（`ReleaseCapture` + `WM_NCLBUTTONDOWN(HTCAPTION)`，把拖动交给系统移动循环，手感与原生一致）/ `win.min` / `win.max` / `win.close` / `win.fullscreen` / `win.corner{px}` / `win.state`；宿主 → 页面回推 `{type:'win.state', maximized, fullScreen, corner}`。
- 页面侧 `layouts/TitleBar_web.vue`：34px 高，左侧三枚交通灯（红 `#ff5f57` 关闭 / 黄 `#febc2e` 最小化 / 绿 `#28c840` 最大化，悬停整组才显符号），标题绝对居中并挂当前 BPM，空白区 `pointerdown` 发 `win.drag`、双击发 `win.max`。
- 「不影响控件」的落实方式：窗口圆角只作用于 `SetWindowRgn` 的窗口外框，与页面内 `--radius`（`ui.corner_radius`）完全两套；壳设置页的滑块调的是前者，外观卡片的圆角调的是后者。

**49-5 `_web` 副本拆分（需求 2）**
- 新增壳桥 `src/shell.ts`：`inShell` / `postShell(cmd, extra?)` / `winState`（响应式窗口状态）/ `setShellCorner(px)`；不在壳里时全部静默失效，浏览器构建也不会因缺少 `chrome.webview` 报错。
- 按用户选择「连 views 也建副本」：`App_web.vue`、`router_web.ts`、`layouts/MainLayout_web.vue`、`layouts/TitleBar_web.vue`、`views/Settings_web.vue`。可复用的（13 个页面里的 12 个、全部 components/stores/api/styles）保持单份共享，`router_web.ts` 直接复用 `router.ts` 导出的 `NAV`，只把 settings 指到 `_web` 副本。
- `main.ts` 做入口分流：`createApp(inShell ? App_web : App)` + `app.use(inShell ? routerWeb : router)`。壳版布局没有窄屏抽屉/遮罩/FAB（桌面窗口最小宽度已高于断点），浏览器版保持原样。
- 壳设置页多一张「窗口」卡片：窗口圆角滑块（0~24，实时生效并落盘）+ 全屏切换 + F5/F11/F12 说明。
- i18n 新增 10 个 `win.*` key（`lang.ts` + `lang.rs` 同步，296 → **317 keys**）。

**49-6 WebView 转正为主前端（需求 1 后半）**
- `config.app.frontend`：`webview`（默认）/ `egui`；参数 `--egui` / `--webview` 优先于配置，且两者都算「显式指定 UI」→ 强制 GUI 模式。
- `ProcessInfo.LaunchFrontend()`：webview 分支先 `App.Web.Start(openUi:false)` 起服务再拉壳；`LaunchWebUi` / `LaunchUi` 改为返回 bool，壳缺失或起不来就日志说明原因并回退 EGUI。`Program.cs`、`TrayHost`（双击 + 「打开界面」）、`FloatingWindow`（右键菜单 + 双击）、`AppHub.Activate()` 全部改走它。
- 托盘菜单加「打开 Web 界面 / 打开 EGUI 界面」两项，可无视配置直接指定。
- **坑：壳与后端互相拉起造成进程雪崩。** 实测端口 8228 被本机其它程序占用时，`WebServer.Start()` 只写了条 ERROR 就返回 void，`WebHost.Server` 仍被赋值 → `Running` 假报 true → 壳被拉起 → 壳探测不到端口 → 壳带 `--web` 再拉一个后端 → 新后端撞单实例锁 → 发 `activate` → 旧实例又拉一个壳……20 秒内堆了 40 多个 `hrm-webui` 进程。修法三处：① `WebServer.Start()` / `WebHost.Start()` 返回 bool，监听失败时 `Server` 保持 null；② `LaunchFrontend()` 在服务不可用时直接回退 EGUI，绝不拉壳；③ `AppHub.Activate()` 新增 `ProcessInfo.FrontendRunning()` 判定 —— 壳进程存在但还没建出窗口时只补启 Web 服务，不再拉新壳。

**验证（`Built\debug-20260904_124339`）**
- `npm run locales` **317 keys**；`vue-tsc --noEmit` 0 error；`npm test` **29/29**；`dotnet build`（App + WebView2Host，`-r win-x64`）0 error；`cargo build --release` 通过（仅 3 个既有 `trace.rs` unused import 警告）；`build.ps1 -Mode debug` 全链路 ok（config / webui / hrm-ui / hrm-webui / image 全部就位）。
- 默认启动（`--gui`）：进程数 `hrm-webui: 1 / hrm-ui: 0`，日志 `主前端: webview` + `Web UI 已启动`，窗口标题 `HeartRateMonitor · Web UI`；`--egui` 启动：`hrm-webui: 0 / hrm-ui: 1`，日志 `主前端: egui`。
- 壳窗口实测（Win32 校验）：`style=0x16070000` → `WS_THICKFRAME=True`、`WS_CAPTION=False`（无系统顶栏但可缩放）；`GetWindowRect == GetClientRect`（1451×911，`WM_NCCALCSIZE` 生效，非客户区为 0）；`GetWindowRgn` 返回 3（COMPLEX，圆角区域已应用）。
- 端口占用回退路径：把 `web.port` 指到被占用端口 → 日志 `Web 服务不可用（端口被占用？），回退 Rust egui 前端`，`hrm-webui: 0 / hrm-ui: 1`，**不再出现进程雪崩**。
- 关窗持久化：`CloseMainWindow()` 后 `webui-shell.json` = `{Width:1451, Height:911, Maximized:false, Zoom:1, CornerPx:10}`；`hrm-webui.exe --port 8231` 单独启动也能正常连上已运行的后端。
- 页面产物校验：`webui/assets/index-*.js` 里能搜到 `mac-bar` / `win.drag` / `win.state`（壳专属副本确实进了构建）。

### 步骤 50：主题两维化（遵循系统）+ DWM 边框同步 + Debug Mode 开关 + Switch Toggle 统一 + 关窗确认 + 托盘主题化菜单
- 需求：`1::` ①窗口边框与主题不一致（被框选变白）②非 debug 不显示左下角路径提示 ③release 设置里加 Debug Mode 开关 ④设备列表 BPM/状态列不协调 ⑤二元布尔控件换 Switch Toggle ⑥主题加「遵循 Windows 主题色」；`2::` 独立 exe 已用三方工具实现，plan 剩余项打勾；`3::` 关窗询问「退出 / 最小化到托盘」+「不再询问我」+ 托盘菜单主题化。
- 用户明确的四项决策：退出语义 = 先查未保存更改、保存后向后端发结束信号由后端 kill 其余组件；「遵循系统」= 深浅与主题色**拆成两个独立 ComboBox**，两边首项都是「遵循系统」并持久化；Debug Mode = 即时生效 + 落盘。

**50-1 主题两维化（需求 ⑥）**
- `theme.ts` 把单一 `theme` 拆成 `mode`（system/dark/light）与 `palette`（system/default/forest/sunset/custom），CSS 选择器从 `[data-theme]` 改为 `[data-mode]` + `[data-palette]`。**配色覆盖块必须写在明暗块之后**，否则 zinc 的中性 `--primary` 会盖掉强调色。
- 老配置迁移：`fromLegacyTheme()` 把 `dark/light/forest/sunset/custom` 映射成两维；`toLegacyTheme()` 折叠回 `config.ui.theme`，egui 前端不改代码也能读。
- 系统值来源：深浅走 `matchMedia('(prefers-color-scheme: dark)')`，强调色由后端 `App/UI/SystemTheme.cs` 读注册表下发（`/api/config` 的 `sysTheme`）。**坑：DWM 的 `AccentColor` 是 ABGR 打包的 DWORD**（本机 `0xFF2311E8` → `#E81123`），按 ARGB 解会把红蓝反过来；回退键 `ColorizationColor` 才是 AARRGGBB。
- 系统改主题的两个消息在 `TrayHost.WndProc` 里接：切深浅是 `WM_SETTINGCHANGE(0x001A)` 且 lParam 字符串为 `"ImmersiveColorSet"`，改强调色是 `WM_DWMCOLORIZATIONCOLORCHANGED(0x0320)`；两者都转 `AppHub.PushSystemTheme()` → `sys_theme` WS 事件。
- i18n key 冲突：新加的 `settings.mode`（明暗）与「关于」卡片已有的 `settings.mode`（运行模式）重名，改名 `settings.thememode`。

**50-2 窗口边框与主题同步（需求 ①）**
- 根因：无边框窗口 Windows 仍画一圈 1px DWM 边框，激活时取系统色（浅色主题下就是白）——这就是「被框选时变白」。
- 页面 → 宿主传色（`win.theme{bg,dark}`）：取 `getComputedStyle(document.body).backgroundColor`。**坑：主题变量是 `oklch()`，Chromium 的 computed value 原样保留不降级成 rgb**，第一版用正则抽数字，把亮度 `0.205` 当成 R 通道，实测传给宿主的是 `#000000`。改用 1×1 canvas 让浏览器自己做色彩空间转换（先涂哨兵色判断解析是否成功），修完实测 `ThemeBg=#171717`（= `oklch(0.205 0 0)`）。
- 宿主侧 `ApplyTheme()`：同步 `BackColor` + `DWMWA_USE_IMMERSIVE_DARK_MODE(20)` / `DWMWA_BORDER_COLOR(34)` / `DWMWA_CAPTION_COLOR(35)`（COLORREF = `R | G<<8 | B<<16`）。**坑：34/35 是 Win11 22000+ 专有**，本机 Win10 19045 实测返回 `E_INVALIDARG(0x80070057)`；据此加兜底 —— 边框色属性不可用时改设 `DWMWA_NCRENDERING_POLICY(2) = DWMNCRP_DISABLED`，直接停掉本窗口的非客户区渲染（实测 `DWMWA_NCRENDERING_ENABLED` 由 1 变 0），那圈会变白的边框随之消失。
- 底色另存 `webui-shell.json` 的 `ThemeBg`，冷启动直接当 `BackColor`，避免先闪一下不搭的颜色。

**50-3 Debug Mode（需求 ②③）**
- 左下角那条「路径提示」就是 WebView2 的状态栏：`CoreWebView2.Settings.IsStatusBarEnabled`，与 `AreDevToolsEnabled` 一并由 `_debug` 控制（`ApplyDebugChrome()`），F12 也加 `when _debug` 守卫。
- 开关落在设置页「调试」卡片 → `POST /api/settings {app:{debug}}` → `App.DebugMode` 即时生效 + 写 `config.app.debug`；页面再经 `win.debug{on}` 通知宿主。壳启动初值由 `--debug` 参数给（`ProcessInfo.LaunchWebUi` 按 `App.DebugMode` 决定是否附加）。

**50-4 设备表对齐（需求 ④）**
- `COLS` 每列加 `align`：勾选/状态/图标居中，名称/MAC/操作左对齐，信号/广播频率/报告频率/BPM 右对齐；表头与数据行共用同一份 `a-*` class。
- 数字列加 `font-variant-numeric: tabular-nums`（等宽数字，数值跳动时不抖），`.dev-td` 加 `min-height: calc(30px * var(--sp))` 固定行高；列宽重排为 34/52/32/190/148/88/96/96/74/250。

**50-5 Switch Toggle 统一（需求 ⑤）**
- 新增 `components/XSwitch.vue`：`defineModel<boolean>` + 隐藏的原生 `input[role=switch]`（保留键盘可达性）+ `.sw-track`/`.sw-thumb`；轨道圆角 `min(9px, calc(var(--radius) + 3px))` 跟随主题，开态用 `--primary`。
- 替换 11 处：设备策略 4 项、API 服务 2 项、硬件采集 3 项、心率记录、日志 3 项。保留原生 checkbox 的是真复选语义：设备行勾选、API 变量白名单、关闭对话框的「不再询问我」。
- 踩坑：`Hardware.vue` 的 `cv.enabled` 类型是 `boolean | undefined`，`v-model` 不兼容 `defineModel<boolean>`，改 `:model-value="cv.enabled !== false"` + 显式 `@update:model-value`。

**50-6 关窗确认 + 托盘主题化菜单（`3::`）**
- 未保存登记表 `src/dirty.ts`：`registerDirty/dirtyCount/saveAllDirty` + `useDirtyForm(id, getState, save)`，判脏方式是「当前状态 JSON 快照 != 上次保存的基线」；已接入 devices / apiserver / hardware。
- `CloseDialog_web.vue` 两步：①有未保存 → 保存并继续 / 放弃更改 / 取消；②最小化到托盘 / 退出程序 / 取消 +「不再询问我」（写 `config.ui.closeAction`，设置页「窗口」卡片可改回 ask）。宿主 `OnFormClosing` 在页面就绪且是用户关闭时 `e.Cancel` 并发 `win.close-request`；页面确认后回 `win.exit`（先 `POST /api/shutdown`）或 `win.tray`。
- 退出链路闭合确认：`/api/shutdown` → `AppHub.Shutdown()` → `Application.Exit()` → `TrayHost.OnFormClosed` 依次关悬浮窗 / 托盘 / 管道 / Web / OSC / BLE，符合「后端 kill 其余组件」的语义。
- 托盘菜单主题化：新增 `App/UI/ThemeColors.cs`（与前端同口径算 `IsDark/Accent/Background/Foreground/Border/Muted/Hover/OnAccent/MenuRadius`，`mode=system` 时取 `SystemTheme`）与 `ThemedMenuRenderer.cs`（`ToolStripProfessionalRenderer` 子类重绘项背景/工具条背景/边框/文字/分隔线，`GraphicsPath` 圆角赋给 `ToolStripDropDown.Region`；颜色每次绘制实时读取，改主题无需重建菜单）。
- `TrayHost` 菜单扩到 12 项（心率只读行 / 打开界面 / 打开 Web 界面 / 打开 EGUI 界面 / 扫描 / 记录 / OSC / 打开悬浮窗 / 锁定悬浮窗 / 关闭全部悬浮窗 / Web 服务 / 退出），二元项文字在 `Opening` 时按当前状态刷新。
- **托盘唤回已隐藏的壳**：壳 `Program.cs` 加命名事件 `Local\hrm-webui-show` 做单实例守卫（重复启动只 `Set()` 后退出），`MainForm` 用 `ThreadPool.RegisterWaitForSingleObject` 等信号并 `ShowFromTray()`；`AppHub.Activate()` 里 webview 前端遇到「有壳进程但没有可见窗口」时改为再拉一次壳（原来只补启 Web 服务，托盘点「打开界面」没反应）。

**50-7 `2::` plan 打勾**
- Build 二级菜单 + 单 exe Standalone、LocalWeb/ExtWeb 两项按用户说明（已用三方工具实现）直接勾上。

**验证**
- `npm run locales` **336 keys**；`vue-tsc --noEmit` 0 error；`npm test` **31/31**；`dotnet build`（App + WebView2Host）0 error；`build.ps1 -Mode debug` 全链路 ok（`Built\debug-20260904_230319`，config / webui / hrm-ui / hrm-webui / image 全部就位）。
- 实机（Win10 19045，系统深色 + 强调色 `#E81123`）：`/api/config` 返回 `sysTheme={dark:true,accent:"#E81123"}`（ABGR 解码正确）、`ui.mode=system` / `ui.palette=system` / `ui.closeAction=ask`；`webui-shell.json` 的 `ThemeBg` 由错误的 `#000000` 修为 `#171717`。
- DWM 属性实测：`DWMWA_USE_IMMERSIVE_DARK_MODE=1` 生效；`BORDER_COLOR`/`CAPTION_COLOR` 返回 `0x80070057`（Win10 不支持）→ 兜底路径触发，`DWMWA_NCRENDERING_ENABLED` 变 0；`style=0x16070000`（`WS_THICKFRAME` 在、`WS_CAPTION` 无）。
- Debug Mode 往返：`POST {app:{debug:false}}` → `/api/config` 的 `app.debug=False` 且 `config.json` 的 `app.debug=0`；改回 true 同样即时生效（即时 + 落盘都成立）。
- 主题往返：`POST {ui:{mode:"light",palette:"forest"}}` → 读回 `mode=light palette=forest`，`ui.theme` 仍保留旧值供 egui 读。

### 步骤 51：整棵树按平台切分 Windows/ + Linux/ + Shared/（第二十轮收尾）
- 需求（`4::` + `5::`）：commit 并归档 V3 计划；把 Windows 相关资源整理进 `Windows/`，创建 `Linux/` 目录准备适配 Linux 版；先规划后行动。用户确认三项：目录布局 **Windows/ + Linux/ + Shared/**、本轮**直接完成迁移 + 改路径**、Linux 技术方向**复用 .NET + BlueZ**。

**51-1 目录切分（git mv 保历史）**
- 目标布局（详见 `Shared/docs/about.md` §3 目录结构）：
  - `Windows/`：`App/`（C# 主程序 + WinForms/WebView2 壳调用等平台代码）、`RustUi/`、`Shells/WebView2Host/`、`build.ps1` / `build.bat`；
  - `Shared/`：`WebUI/`（Vue 前端，平台无关）、`Engine/`（C OSC 引擎源码）、`config/`（config.json 模板）、`image/`、`Skills/`、`docs/`（about.md / diff.md / Archives/）；
  - `Linux/`：`Plan.md`（Linux 适配规划，新建）；
  - 根目录仅剩 `.gitignore`、`Built/`、`Backup/`、`VRCX-2026.07.18/` 等非代码目录。
- `HeadSet/`（空目录，从未入库）与根 `config.json` / `build.*` / 平铺 `App/Engine/RustUi/Shells/WebUI` 一并清出根目录。
- **踩坑：`git mv` 目录权限被拒**。根因是曾有 `dotnet build`/运行中的 HeartRateMonitor 进程句柄 + 残留 `bin/obj`；处理顺序：先停进程 → `dotnet build-server shutdown` → 手动清 `bin/obj` → 逐子项 `Move-Item` 到目标再删空壳（git 靠内容相似度识别为 rename）。迁移后根目录残留一个空的 `WebUI/` 壳（被某个停在旧目录的终端进程 CWD 占住句柄，删不掉，需关掉该终端后再清）。

**51-2 路径改接**
- `Windows/build.ps1`：脚本自身即 Windows 根（`$Win = Split-Path`），`$Root` = 仓库根，`$Shared = Root/Shared`；`Engine/WebUi/App/RustUi/Shells/config/image` 全部改指新位置；`OutBase` 仍是仓库根 `Built/`。
- `Windows/build.bat` 逻辑不变（`%~dp0` 同级调 build.ps1），仅注释更新。
- WebUI 各脚本（`build-webui/dev-webui/test-webui.ps1`、`package.json`）用自身 `Split-Path` 定位，随目录整体迁移自动成立，无需改。
- 代码层无需改路径：`App` 读 config / webui / osc_engine 全在 `AppContext.BaseDirectory`（运行期产物同目录）；csproj/rust 无跨目录相对引用（已 grep 复核）。

**51-3 归档与 Linux 规划**
- V3 计划 `plan.md` → 归档为 `Shared/docs/Archives/plan3.md`（补归档头说明），根 `plan.md` 删除；Archives 三份按 `plan1/plan2/plan3` 排序。
- 新建 `Linux/Plan.md`：技术映射表（`net10.0-windows` → `net10.0` + BlueZ D-Bus 替代 WinRT BLE；C 引擎补 POSIX socket 版 `.so`；SysInfo 采集改 `/proc` + sysfs + lm-sensors；Tray/悬浮窗 P2 用 AppIndicator/GTK；主 UI = 浏览器访问 WebUI，WebKitGTK 壳后置评估）与四阶段路线 L1~L4（先跑起来 → BLE → 桌面集成 → 收尾）。

**验证**
- `Windows\build.ps1 -Mode debug` 全链路通过（`Built\debug-20260904_235849`）：exe / hrm-webui / hrm-ui / webui / config / osc_engine.dll / image 全部就位 —— 平台切分后的构建链完好。
- `git status`：全部移动被识别为 rename（`R`），无内容丢失；根目录代码文件清零。
- WebUI 代码零改动（仅换目录），此前 336 keys / vue-tsc 0 error / vitest 31/31 结论继续成立。

### 步骤 52：Phase Web2 — 单一 Web 化（移除 egui/端口 9460）+ 标题模板 + 动画 + 第二前端认证（第二十一轮）
- 需求（`1::`~`9::`）：状态列背景修复 / 端口 9460 + 移除 Rust egui / 新增 Web 页（第二前端：用户·Session·角色·板块白名单）/ OSC 自定义发送 / Pusher 未定义变量空串 / 左上角标题 {} 模板 / ComboBox 弹出与 Tab 方向动画 / 浅色主题补全与控件配色统一 / 圆角边缘白边查因。

**1. 端口 9460 + 彻底移除 Rust egui（代码先期完成，本轮记录）**
- 配置/常量默认 9460（`Config.WebSection.Port`、`App.WebPort`、壳 `Program.DefaultPort`、vite proxy、模板 `config.json`）。
- 删除 `Windows/RustUi/`、`Ipc/PipeServer.cs`；`config.app.frontend`、`--egui/--webview`、`ProcessInfo.LaunchUi/FrontendRunning` 回退、托盘「打开 EGUI 界面」、build.ps1 cargo 段全部移除；单实例移交从命名管道改为 HTTP `POST /api/activate`（`SingleInstance.NotifyRunning` → `WebServer` 新路由）。
- 托盘/悬浮窗/主程序统一走 `ProcessInfo.LaunchFrontend()`（webview 唯一前端）。

**2. 状态列背景修复 + Pusher 未定义变量空串**
- `Devices.vue`：表头与数据行拆成两个 grid（`.dev-head`/`.dev-body` 共用 `gridCols`），表头底色由整行连续覆盖 → 列宽合计不足容器宽度时不再出现「状态列表头底色带宽度异常」。
- `SysInfoService.FormatTemplate`：变量替换后残留的 `{XXX}` 用正则清空 → 模板指向不存在变量时不发原文（Pusher/Webhook/浮窗/预览同一套逻辑）。已同步注释与 using。

**3. 圆角边缘白边查因（待实机目验）**
- 根因：无边框窗口仍有 1px DWM 边框，取系统色（浅色主题=白）；Win10 不支持 `DWMWA_BORDER_COLOR`，此前只在页面主题消息到达后才停非客户区渲染，白边已画出、要等移出/重绘才自愈。
- 修复：`WebView2Host/MainForm.OnHandleCreated` 在**第一帧前**（Win10 22000 前）就 `DWMWA_NCRENDERING_POLICY=disabled`，杜绝白边首帧残留；Win11 维持 `DWMWA_BORDER_COLOR` 压成主题色。**视觉确认待 Release 实测**。

**4. 左上角标题 → {} 模板（ui.brand）**
- `config.ui.brand`（Config.cs + AppHub settings 读写）；前端新 `composables/useBrand.ts`（第 3 处复用，`composables/` 目录由此建立）：默认 `nav.brand`，模板支持系统变量（{BPM}/{CPU_USAGE}/{TIME_LOCAL_HMS}…）与实时指标 `{ping}`（REST RTT，app store 新增 EMA）/`{oscRx}/{oscSent}/{oscFail}/{bpm}/{devices}/{connected}/{version}`，未定义占位符为空串。
- 接入 MainLayout / MainLayout_web（侧栏顶）/ TitleBar_web（居中标题）；Settings(+_web) 外观卡新增「品牌标题」输入（保存走 settings）。

**5. 动画细化：ComboBox 弹出 + Tab 切换方向**
- 新增自绘下拉 `components/XSelect.vue`（替换全站 18 处原生 `<select>`：设置 语言/明暗/配色/关闭行为、硬件 kind/NTP 预设、心率 4 项与悬浮窗/导出、日志/监测导出）：Teleport 弹层 + 淡入/沿方向展开动画 + 视口不足自动向上 + 方向键/Esc + 点外关闭 + 跟随主题变量；`prefers-reduced-motion` 关闭动画。
- 路由切换按侧栏相邻方向滑动：MainLayout(+_web) 记录上一路径在 `NAV` 的次序，`page-fwd`（向下翻页：新页自右滑入）/`page-back` 反向，区间外回退淡入（globals.css 新增两组过渡 + reduced-motion 清单）。
- `nav` 折叠/展开、选中底色等既有过渡未动。

**6. 主题补全与控件配色统一**
- 新增两套配色 `ocean`（海蓝）/`violet`（紫罗兰），明暗各一份（globals.css + theme.ts PALETTE_IDS/LABEL + 头注释）；亮色中性分割线加深（`--border/--input/--sidebar-border` 0.87）白底更清晰；`settings.palettehint` 文案改为「配色与明暗相互独立」五语言同步；`theme.spec` 断言更新为 7 配色。

**7. Web 第二前端（认证全本地）+ OSC 自定义发送**
- `Config.RemoteSection`：enabled/host(0.0.0.0)/port(9461)/loopbackOnly/idleMinutes；模板 config.json 同步。
- 新 `Web/RemoteAuth.cs`：`config_remote_users.json` 本地用户库（PBKDF2-SHA256 20 万次 + 盐，首启建 admin/admin 并警示改密）、角色 admin/user、Session 随机 32 字节 token（HttpOnly Cookie）**带浏览器指纹（UA+Cookie+时区+语言+来源 IP，任一变化即失效）**、闲置过期、列表/销毁；回环请求视为本机管理员免登录。
- `WebServer`：第二监听按配置热启停（`http://+:{port}/`；非管理员需一次 `netsh http add urlacl`，失败只记日志给出命令不拖垮本地）；请求按 `LocalEndPoint.Port` 区分是否远程 → 远程 API/WS 必须带有效会话，user 角色另有**接口级兜底名单**（cli/shutdown/settings/apiserver/logs dump-export/float 开关/hw config/scan 等一律 403）。`/api/remote/me|login|logout|sessions|sessions/kick|users|config` 一组新路由；`/api/osc/custom`（文本先过 FormatTemplate；pauseMs>0 暂停推送器防模板覆盖，`OscService.PausePush` 到点自愈）。
- 前端：api 层注入 `X-Hrm-Tz/X-Hrm-Lang` 头并监听 401（会话失效→回登录）；app store 新增 `remote` 状态/`checkRemote`/`tabAllowed`/`afterRemoteLogin`/`logout`，WS 仅在已登录后连接（ws URL 带 tz/lang，会话失效即断开不再空转重连）；NavMenu 按白名单过滤；`views/Web.vue`（监听配置/用户 CRUD/改密/角色/板块白名单/在线 Session 列表与销毁 + 当前会话登出），路由插在 settings 前（第 14 个 Tab `web`，含图标与 5 语言 key）；`components/RemoteLogin.vue` 登录遮罩；Pusher 页新增「自定义发送」卡（地址/文本/暂停秒），手机登录远程后即由此发文字/参数。
- 白名单默认：user = overview/dashboard/heartbeat/devices/osc/pusher/hwinfo/headset/logs/monitor；console/apiserver/settings/web 仅 admin（可对单个用户另行放行）。

**验证**
- `dotnet build`（App）0 error；`vue-tsc` 0 error；vitest 31/31（theme.spec 断言 7 配色、i18n.spec 14 Tab）；locales 379 keys。
- 留待 Release 实机目验：圆角白边首帧修复、远程绑定（管理员/urlacl）与登录/角色/指纹全流程。

### 步骤 53：Phase Web2 热修 — 本地前端被认证锁死（回环铁律）+ 圆角换实现路径（第二十一轮补）
- 需求（`0::`、`1::`）：127.0.0.1 的流量本不该受任何拦截，现有认证逻辑完全忽视该关键点导致本地前端无法控制；圆角边缘仍有白边，要求换实现路径。

**1. 回环铁律（严重错误修复）**
- 事故链：`Handle` 按 `LocalEndPoint.Port` 判定“是否远程”→ 本地 127.0.0.1:9460 的请求从不构造会话（sess=null）→ `/api/remote/me` 对 sess==null 一律 401 → 前端 `checkRemote` 的 catch 把 `tabs` 置成 `[]` → `NavMenu` 过滤后**导航为空**、`tabAllowed` 全 false → **每个页面都显示“无权限”占位且无登录入口（loginNeeded=false）→ 本地前端整体锁死**。
- 修复（信任判定重写）：
  - `WebServer.Handle`：删掉 `IsRemoteCtx`（LocalEndPoint 端口判定不可靠且方向错了）。**唯一判定依据 = 客户端 IP**：`RemoteAuth.IsLoopback(ip)` 为真 → 无条件放行并直接构造 `admin` 会话（无论落在 9460 还是 9461 监听）；非回环才走 Session 解析，未登录仅放行 `/api/remote/*` 与静态资源（登录页要能加载）。`loopbackOnly` 检查同样只针对非回环客户端。
  - `RemoteAuth.IsLoopback`：改用 `IPAddress.TryParse + IPAddress.IsLoopback`（覆盖 127.x 段与 ::1），另兼容 `::ffff:127.0.0.1` 映射写法。
  - `HandleRemoteApi` 的 `remote` 载荷标志同步改为“客户端非回环”；`me` 对回环恒返回 admin/tabs=null。
  - 前端 `app.ts`：`tabAllowed` 第一条即“非远程恒放行”；`checkRemote` 失败兜底按主机名回环 → 视为本机管理员（`tabs:null`），**本地前端任何情况下不可被锁死**。
- 结论：本地（壳/浏览器 127.0.0.1）流量从路由层就不存在 401/403 的可能；远程（局域网 IP）未登录只看到登录页。

**2. 圆角白边换实现路径**
- 三个白边来源逐一分析：① WebView2 方形子窗口画进区域外的圆角缺口（冷启动默认白底/浅色主题页面白底）；② 重定向面在 `SetWindowRgn` 区域外残留旧像素（“移出屏幕再移回恢复”的典型症状）；③ Win10 DWM 1px 边框（NCRP 已关，非主因）。
- 新实现（`MainForm.ApplyCorner` 双路径）：
  - **Win11 22000+**：`DWMWA_WINDOW_CORNER_PREFERENCE(33)` 走 DWM 原生圆角（组合层裁剪子窗口、抗锯齿、无重定向面残影），**完全弃用 SetWindowRgn**；半径映射系统两档（≤6 → ROUNDSMALL ≈4px，>6 → ROUND ≈8px），最大化/全屏/0 → DEFAULT。
  - **Win10**：保留区域裁剪但堵死全部白边来源 —— WebView 内缩按半径加大（`InsetDip = max(6, ⌈0.35ρ⌉)`，圆弧对角深入约 0.29ρ，方形子窗口永远够不到缺口）；`WebView2.DefaultBackgroundColor` 预置上次主题底色（构造期 + `win.theme` 同步，消灭冷启动白闪）；`RedrawWindow(RDW_INVALIDATE|ERASE|FRAME|ALLCHILDREN)` 强制整窗重绘清残影。
  - 统一 `ApplyPadding()`（全屏 0 / 其余按 InsetDip），`OnHandleCreated`/`OnDpiChanged`/`ToggleFullScreen` 的 Padding 直设全部改走它。
- 设置页圆角提示文案更新（Win11 两档 / Win10 像素裁剪），五语言同步。

**验证**
- `dotnet build`（App + 壳）0 error；`vue-tsc` 0 error；vitest 31/31；locales 379 keys。
- 待实机复验：本地前端恢复控制；Win10（19045）圆角无白边（含最大化↔还原、F11、DPI 切换、冷启动）；Win11 走 DWM 原生圆角。

### 步骤 54：第二前端实机联调 — 会话指纹自毁修复 + 回环/角色全流程实测（第二十一轮收尾）
- **会话指纹把自己算死（实机才暴露的阻断级 bug）**：`Fingerprint(ua, cookie, tz, lang, ip)` 把**整个 Cookie 头**计入哈希，而登录响应正是往 Cookie 里写 `hrm_session=<token>`。于是登录时指纹用「无会话 Cookie」算，登录后每个请求都带着 `hrm_session` → 指纹必然不同 → `Resolve` 判为劫持并销毁会话，日志刷「远程会话指纹不符，已销毁」。表现：远程登录 200 成功，紧接着的每个 `/api/*` 全 401，第二前端永远进不去。
  - 修复：`RemoteAuth.StripSessionCookie()` 在计算指纹前剔除 `hrm_session=` 这一项，其余 Cookie 的变化仍会让会话失效（防劫持能力不变）。
- **实机验证（`Built\debug-20260905_092820`，Win10 19045 22H2）**
  - 本地直通（回环铁律）：`127.0.0.1:9460` 上 `/api/status`、`/api/remote/me`、`/api/devices`、`/api/logs`、`/api/hw/config`、`/heartbeat`、`/api/remote/{sessions,users,config}` 全 200；`me` 返回 `remote:false, username:local, role:admin, tabs:null` —— 本地前端零拦截。
  - 远程未登录：局域网 IP `:9461` 上 `/api/remote/me`、`/api/status` = 401，`/`（登录页静态资源）= 200。
  - 远程 admin 登录：`/api/remote/login` 200 → `me`/`status`/`sessions`/`users` 全 200。
  - 远程 user 角色（新建 `phone`）：`me`/`status` 200；`/api/settings`、`/api/cli`、`/api/hw/config`、`/api/remote/sessions` 全 403（接口级兜底白名单生效）。
  - 指纹防劫持：同 UA 带 token → 200；换 UA 带同一 token → 401，且原会话被销毁（原 UA 再请求也 401）。
  - 远程监听热启停：`POST /api/remote/config` 改 `enabled/host/port` 后 `active:true` 且新端口立即可连，无需重启。
- 结论：`0::`（本机流量直通）与第二前端认证全链路在实机上闭环。圆角白边（`1::`）已按双路径重做并随本次构建产出，留用户在窗口上目验。

### 步骤 55：安全加固（凭据脱敏 / 会话持久化）+ 设计语言统一 + 实时保存（第二十二轮）
需求 10 项（用户 `1::`~`10::`）。

**1. 安全：凭据全链路脱敏（`6::`，重大错误）**
- 事故点：改密走 `window.prompt()` —— 原生框把新密码**明文回显**在输入行，且不受主题控制、无法遮蔽、可被截屏/录屏直接读到；设备重命名同样用 prompt。
- 修复：新增 `components/XDialog.vue`（统一对话框外壳），`views/Web.vue` 的新增用户/改密/删除、`views/Devices.vue` 的重命名全部改走页面内对话框；密码字段 `type="password"` + `autocomplete="new-password"` + 二次确认，关闭对话框即清空明文 ref。
- 全仓禁用 `window.prompt/confirm/alert`（见 `Shared/Skills/Attention.skill`）。
- 存储侧复核并加固：密码仍是 `PBKDF2-SHA256`（20 万次 + 16 字节盐，`FixedTimeEquals` 比对）；**会话 token 改为只存 `SHA256(token)`**，原始 token 只在登录响应的 `Set-Cookie` 出现一次，`/api/remote/sessions` 暴露的 id 也换成 token 哈希（`Kick` 按哈希删，`IsSame` 按哈希比对）。落盘文件泄露也伪造不出 Cookie。

**2. 安全：切 Tab 会话失效（`7::`）**
- 双重根因：
  - ① 指纹把 `时区 + 语言 + IP + Cookie` 一并计入等值比对 —— 这些值在正常使用中就会变（Cookie 被写入、跨时区、Wi-Fi 换出口 IP），等于随机注销用户；
  - ② `Set-Cookie` 没有 `Max-Age` → 是**会话 Cookie**，手机切 App / 浏览器回收后台标签即丢弃；`SameSite=Strict` 还会让外部链接跳进来时不带 Cookie。
- 修复：指纹**只绑 `User-Agent`**（浏览器固有、每请求必带）；Cookie 补 `Max-Age`（= 闲置上限，配 0 时给 30 天）并改 `SameSite=Lax`；指纹不符改为「拒绝本次 + 计数」，累计 10 次才销毁（否则拿到 token 配个错 UA 就能踢掉合法会话）；会话表落盘 `config_remote_sessions.json`（只有哈希），进程重启后远端不必重登，加载时丢弃已超时与用户已删除的条目。

**3. 设备状态列背景对不齐（`2::`）**
- 根因不在列宽，而是 `align-items: center`：空列（勾选框/图标列）的单元格只有内容高度并垂直居中，其 `border-bottom` 停在半高处 → 视觉上就是几截错位的短线。
- 修复：表头/数据两个 grid 都改 `align-items: stretch`，底边线上移到 `.dev-head` 整行，单元格补 `min-height`。

**4. 亮色主题适配 + 分割线加粗（`3::`、`9::`）**
- 新增 `--hairline`（亮色 1.5px / 暗色 1px），全站边框改 `var(--hairline) solid var(--border)`；新增 `.x-row`（列表行）与 `.x-vsep`（竖分隔条）收掉各页写死的 `1px` 内联样式。
- 亮色 `--border` 由 0.87 压到 0.80、`--input` 0.78；新增「亮色专项适配」块修正三处在白底不成立的取值：语义色（`good/warn/bad/info` 全部换成深色版）、`--secondary/--muted`（0.97 → 0.94，否则表头与白卡片同色）、`--muted-foreground`（0.556 → 0.44）。
- 四套 palette 的 light 版补齐 `--secondary/--muted/--border/--input/--sidebar-border`，避免 palette 覆盖把上面压暗的值又抬回接近白色。

**5. 控件风格统一（`5::`、`1::`）**
- `globals.css` 全局重绘原生控件：`checkbox`（自绘勾，`:not(.sw-input)` 排除开关内部的隐藏 input）、`range`（轨道 + 圆形拇指，Chromium/Firefox 双套伪元素）、`textarea.x-input`、`number`（去掉 spinner）、`color`（色块铺满）；输入类补 hover/focus ring（`box-shadow` 用强调色 22%）与 disabled 态。
- `XSwitch` 尺寸改为随 `--sp` 缩放并补 hover；`XSelect` 触发器与弹层统一 hairline + focus ring。
- 登录页 `RemoteLogin.vue` 重写：改用 `.x-card` + `.x-input` + `.x-btn`，加品牌区与 loading 态，遮罩与 `XDialog` 同款。

**6. 遮罩/弹窗风格（`8::`）**
- 统一为「模糊背景 + 中置内容」：`color-mix(in oklab, var(--background) 58%, transparent)` + `backdrop-filter: blur(10px) saturate(115%)`。
- 重连态直接把 spinner 与文字放在模糊层上，**不再套第二层深色卡片**（只有「放弃」后的错误面板因需承载可滚动详情才保留卡片）；`CloseDialog_web.vue` 改为复用 `XDialog`。

**7. 实时保存（`4::`）**
- 新增 `composables/useAutoSave.ts`：400ms debounce、保存中再改会重排、内部复用 `dirty.ts` 的 `markClean()`（关窗兜底仍在）、`hydrated()` 对齐基线避免回填触发保存、`flush()` 立即落盘。
- 接入 6 处并删掉「保存」按钮（改为「更改实时保存」提示）：Devices 设备策略、ApiServer、Hardware 采集设置、Pusher OSC 配置、Heartbeat 健康参数与悬浮窗、Web 监听配置（端口非法时跳过，等输完）。

**8. 文档与红线（`10::`）**
- 新增 `Shared/Skills/Attention.skill`：安全与一致性硬约束 —— 前后端禁用清单（prompt/eval/innerHTML/远端资源/localStorage 存凭据/拼接命令与路径/SQL 拼接/自动提权/反射加载）、凭据与会话规范、回环铁律、权限双层拦截、UI 一致性红线、实时保存要求、提交前必过项与自查清单。
- `lang.ts` 新增 10 个 key（`common.autosave`、`web.password2/passhint/delhint`、`web.err.*`），五语言齐全，locales 388 keys。

**验证**
- `npm run locales` 388 keys；`vue-tsc` 0 error；vitest 31/31；`dotnet build`（App + 壳）0 error。

### 步骤 56：Linux 移除 + 上限 65536 + 空值还原 + 动画总闸 + hrmcli + About（第二十三轮）
需求 7 项（用户 `1::`~`7::`）。

**1. 移除 Linux 未来计划（`1::`）**
- VRChat 是 Windows x64 原生游戏，Linux 下运行无必要性 → 删除 `Linux/Plan.md`（`git rm -r Linux`），并同步三处文档引用：`Shared/docs/about.md`（头部平台范围改「仅 Windows x64」、删目录树 Linux 两行、Engine 注释去掉 `libosc_engine.so`、删构建脚本 Linux 行）、`Shared/docs/Archives/plan3.md`（归档说明注明该规划已撤销）、`Shared/docs/about.md` 中指向 plan3 的指引。

**2. 圆角 / 密度上限 65536（`2::`，不改滑块）**
- 前端 `theme.ts`：`UiLook` 加注释范围，新增 `LOOK_MAX = 65536` 与区间常量 `CORNER_SLIDER_MAX=16 / DENSITY_SLIDER_MIN=0.8 / DENSITY_SLIDER_MAX=1.4`；`clampCorner(v)`（0~65536 整数）与 `clampDensity(v)`（0.1~65536）做合法化；`applyUiTheme` 落 `--radius/--sp` 前先 clamp（圆角保留 2px 下限）。
- 新增 `components/XRange.vue`（滑块 + 数字输入框组合）：滑块只覆盖常用区间，数字框可直填到 65536；**值一旦超出滑块区间（只能手动输入得到），滑块带宽度过渡动画收起**，只留输入框 + 单位。`Settings.vue` 与 `Settings_web.vue` 的圆角/密度行换成 XRange。
- 后端 `Config.cs` 注释与默认不变，`AppHub.ApplySettings` 的 clamp 放宽：`cornerRadius 0~65536`、`density 0.1~65536`，保证「前端填了 65536，后端起回来还是 65536」而不是被压回 16/1.4。

**3. 输入框置空 → 还原默认（`3::`，密码除外）**
- 新增 `src/emptyDefault.ts`：document 捕获阶段监听 `change`，空值输入框自动回填默认值并补发一次 `input`（让 v-model 同步；原 `change` 事件随后照常到达页面自身的保存逻辑，此时已是默认值）。
- 默认值来源优先级：`data-default` 属性 → 数字框的 `min` 属性 → 0；**密码框与 checkbox/range/color 等永不参与**；纯文本框未标 `data-default` 的不动 —— 空串对它们是有效语义（搜索=不过滤、别名=清除、令牌=不校验、品牌标题=用默认名）。
- 为全部「数值类输入框」补齐 `data-default`（出厂值）：OSC IP/端口/接收/间隔/地址、硬件采集间隔/小数位/NTP、健康四系数、曲线平滑/窗口、浮窗刷新、导出行数、设备策略五数、Web 远程监听端口/闲置分钟、API 推送/节流、Monitor 分桶数。

**4. 全局动画开关（`4::`）**
- `UiLook.animations`（默认 true）双向持久化（`stores/ui.ts` pull/push、`Config.Ui.Animations`、`UiJson()`、`ApplySettings`）。
- `applyUiTheme` 写 `<html data-anim="on|off">`；`globals.css` 新增动画总闸：`[data-anim='off'] *` 一刀切 `transition/animation/scroll-behavior: none !important`（组件 scoped 样式优先级更高，必须 `!important` 压过；Vue `<Transition>` 时长归零会立即 resolve，弹窗/换页不受影响）。
- 设置页（双份）外观卡片底部加 `XSwitch`「界面动画」（随开随存）。

**5. 独立 CLI 版本 `hrmcli.exe`（`5::`）**
- 入口分层：公共启动序列抽出为 `Core/AppBoot.cs`（编码 → 配置 → 日志 → 服务构造），`Program.cs` 与 `hrmcli.exe` 共用；`Program.cs` GUI/CLI 判定不变，CLI 分支改调 `CliApp.Run(args)`。
- 新工程 `Windows/Shells/CliHost/`（`AssemblyName=hrmcli`，`Exe`）：`Program.Main` 只做 `AppBoot.Init` + `CliApp.Run`，与 `HeartRateMonitor.exe --cli` **完全等价**；差异仅在进程天生带控制台（无需 `AttachConsole`）、不起 WinForms 消息循环（`App.GuiHosted=false`）。
- `CLI/CliUi.cs`：ASCII 五字符画标题（窄终端退化单行）+ 版本/模式/引擎行 + `hrm{bpm}{osc}>` 状态式提示符 + 行级着色（ANSI VT，Win10 起；输出重定向时自动关色）；`CLI/NativeConsole.cs` 开 VT。
- `CLI/CliApp.cs`：支持**带参一次性执行**（`hrmcli status`）与**无参 REPL**；事件流实时输出 `[发现]/[已连接]/[心率]/[BLE错误]`；`clear` 重绘横幅；`selftest` 直通。CLI 进程也会建 `AppHub` + `WebHost`（`EnsureHub`），因此配置命令、`web start`、`remote users/sessions/kick` 全部可用。
- 命令面扩展（`CommandShell` + 新增 `CommandShellApp.cs`，partial 类）：新增 `about/save/block/devcfg/hcfg/hwcfg/sysinfo/oscfg/params/webhook/api/web/remote/ui/float/logcfg`；`monitor` 支持 `export 格式`；`logs` 支持 `clear/dump/export 格式`。配置类命令统一转发 `AppHub` 同名方法 → **CLI 与前端取值口径完全一致**（clamp/落盘/日志同一条代码路径）。
- 安全边界：`remote adduser/passwd/pass` 在 CLI 中**明确拒绝**并提示到界面操作 —— 命令行历史/回显会让明文密码进 shell 历史，属凭据泄露路径。
- WebUI Console Tab 与 CLI 同源：`/api/cli` 走同一 `CommandShell`；Console 页启动与 `clear` 后重绘同款 ASCII 标题（前端复制 `CliUi.Art` 字符画），两端观感一致。
- `build.ps1` 新增 3f 段：发布 `hrmcli.exe` 并按分支同款打包（standalone/releases 单文件自包含；debug 多文件，只拷 `hrmcli*` 自身，业务程序集已由主程序发布覆盖）；SUMMARY 加 `hrmcli` 行。

**6. About Tab（`6::`，Example 占位）**
- 新增 `views/About.vue`：版本发布（版本号取真实 `app.appInfo`，通道/日期/许可 Example）、更新内容（3 条 Example）、链接（GitHub 仓库/下载页/Issues，text=Example）、运行环境（模式/启动时间/目录，真实数据）、致谢（Example）。
- 路由/导航：`router.ts` 与 `router_web.ts` 的 `NAV` 增第 15 项 `/about`，`NavMenu` 加 `Info` 图标；远端 user 角色默认白名单 `AllTabsExceptRestricted()` 加 `about`（不敏感，人人可见）；`lang.ts` 加 `tab.about` 与 12 个 `about.*` key；`i18n.spec.ts` 断言 14 → 15。

**验证**
- `npm run locales` 403 keys；`vue-tsc` 0 error；vitest 33/33（theme.spec 新增 65536 clamp 与 data-anim 用例）；三个 C# 工程 `dotnet build` 0 error；`hrmcli.exe status` 冒烟通过（一次性执行正常输出并退出）；产物构建见 build.ps1 全链。

### 步骤 57：引擎侧语言初始化 + 控制台/CLI 5 语化 + exit --force + tty 终端 + 存储位置（第二十四轮）
需求 4 项（用户 `0::`~`3::`）。

**0. 引擎侧中文硬编码目录化（`0::`）**
- 全项目扫描结论（记录）：前端「布局」残留极少（HeadSet 页一句规划文案，已修；其余为注释/运行时关键字匹配）；中文主体在 C# 引擎侧 —— 126 处 `App.Log.*` 日志 + 控制台/CLI 数百条交互文案 + WebServer/RemoteAuth/托盘等。
- **启动语言初始化**（本次核心，完整落地）：`AppBoot.ResolveLang()` —— `config.ui.lang` 有效则用之；缺省/为空按系统 UI 语言解析（zh→cn/tw、en、ja、es，`CultureInfo.CurrentUICulture`）；系统语言不受支持回退 **en**；检测/兜底结果**写回 config.json**。`Config.Ui.Lang` 默认由 `zh-cn` 改为空串（触发解析）；发货用 `Shared/config/config.json` 删掉写死的 `lang`；`App.Lang` 暴露给引擎侧，运行期经 `AppHub.ApplySettings` 改 `ui.lang` 即时跟随。
- 语言列：zh-cn / zh-tw（暂映射简体） / en / ja / es，新增 `Core/Text.cs`（`Txt.T(key, args)`，207+ key）；语言切换全程无重启。
- **控制台 + CLI 文案 5 语化（范围按用户确认）**：`CommandShell`（40 条 help 描述 + 全部回复）、`CommandShellApp`、`CliUi`（横幅/状态/着色关键词）、`CliApp`（事件标签）、`SelfTest`（头部/汇总行）、`cli_help`/`/api/cli/help` 数据源同步 `Txt.T`。命令 Usage 里的中文 token 换中性 ASCII（`<sec>/[alias]/<table>/[key value]` 等），全语言通用。
- **明确不在本轮**：126 处 `App.Log.*` 操作日志正文（日志页）—— 下一批目录化；SelfTest 步骤正文（诊断明细）暂留中文。

**1. 控制台 exit / --force 强行退出（`1::`）**
- `CommandShell.Execute` 拦截 `exit`/`quit`：**CLI REPL 无参 exit 即退出终端**（进程随之结束）；**GUI/Web 控制台无 `--force` 只提示**「需 exit --force」不误杀程序；**带 `--force` 一律强行退出整个程序**（先存配置 → 300ms 让 HTTP 响应发出 → `Application.Exit`+`Environment.Exit`）。CLI REPL 里任一带 `--force` 也直接结束。

**2. 控制台 UI 改 tty 式（`2::`）**
- `Console.vue` 重写：输入行不再是与输出分离的第二层，而是滚动流里的最后一行（`hrm>` 提示 + 内联输入框，透明无边框），回车提交并把「> cmd」+ 输出回显进历史，始终自动滚到底并重新聚焦；点击流内任意处聚焦输入；↑↓ 历史、clear 重绘横幅保持不变。观感与 hrmcli/真实终端一致。

**3. 配置文件保存位置可改（`3::`）**
- 数据目录概念落地：`App.DataDir`（默认 **%AppData%\HeartRateMonitor**）与静态资源锚点 `App.ExeDir`（webui/引擎 DLL 等仍在程序旁）分离；`App.BaseDir` 历史字段改指数据目录。
- 启动解析顺序：exe 旁 `data_location.txt`（设置里保存的选择）→ 默认 AppData；数据目录里无 config.json 而程序目录有旧文件时**一次性迁移**（config/远程用户/会话/hrm.db/logs）。
- 路径改用数据目录的落点：`ConfigPath/WebhookPath`、Logger 目录与 Dump、`hrm.db`、远程 `config_remote_*.json`、Trace、导出目录、crash_dump；保留 exe 目录的：`webui/` 静态站点、`hrm-webui.exe` 壳路径、`osc_engine.dll`。
- 后端：`AppBoot.SetDataLocation`（预设 `__appdata__`/`__exedir__`/绝对路径 → 写 `data_location.txt`）；`Settings({paths:{dataDir}})` 返回 `restart=true`；`Config()` 新增 `paths` 段。
- 前端：新增 `components/DataPathCard.vue`（当前目录显示 + AppData/程序目录/自定义三选 + 应用按钮 + 重启提示），挂进 `Settings.vue` 与 `Settings_web.vue` 顶部；lang 新增 `settings.path*`（5 语）+ `headset.plan`（修 HeadSet 残留硬编码）。About「运行目录」随 `baseDir` 语义自然变为数据目录。

**验证**
- `npm run locales` 412 keys；`vue-tsc` 0 error；vitest 33/33；三个 C# 工程 0 error；`build.ps1 -Mode debug` 全产物齐。
- 实机行为：`hrmcli about` 在无 lang 配置下按系统语言（本机 Trae 终端为 en-US）解析并**写回 config.json `"lang":"en"`**，CLI 输出全英文；数据目录自动迁到 `%AppData%\HeartRateMonitor`（config/logs 落位）；临时 data_location.txt 指到自定义目录后同样生效。

### 步骤 58：全局韩/德/法 + 悬浮窗二元开关 + 冗余文案清理（第二十五轮）
需求 2 项（用户 `1::`~`2::`）。

**1. 全局语言扩到八种：+ 한국어 / Deutsch / Français（`1::`，日语此前已具备）**
- 覆盖范围按用户确认 = **WebUI 与引擎控制台/CLI 全部翻译**。两套语料规模：WebUI 410 键、引擎 Text.cs 210 键。
- **文案架构**（避免逐行扩列的巨型改动，改用「逐 key 补充字典」）：五张基础 JSON（zh-TW/zh-CN/en/ja/es）仍由 `lang.ts` + `npm run locales` 生成；韩/德/法为手写补充字典 `src/locales/ko.json / de.json / fr.json`，`lang2json.mjs` 明确禁止把它们加入 OUT（会用兜底列清空文案），键集一致性由 `i18n.spec.ts` 保证（现在锁 8 张 JSON）；`lang.ts` 的 `LANGS/LANG_LABEL` 同步扩到 8 种（`tr()` 的 ko/de/fr 兜底 en，实际文案来自 vue-i18n JSON）。
- 前端接线：`i18n.ts`（LANGS/LANG_LABEL/messages 8 语，`document.documentElement.lang` 自动小写）、`stores/ui.ts` 前后向映射（ko/de/fr 直通）、`Settings.vue` 与 `Settings_web.vue` 语言下拉 + `theme.ts` 明暗/配色标签扩到 8 列（`theme.spec` 断言 5→8）、`i18n.spec.ts` 校验三张新字典与 lang.ts 键集完全一致（410/410，无空值）。
- 引擎接线：`AppBoot.ResolveLang` 支持集 + `SystemLang`（ko-KR/de-DE/fr-FR → ko/de/fr）、`AppHub.ApplySettings` 热切换白名单、`Config.Ui.Lang`/`App.Lang` 注释扩列；`Core/Text.cs` 基础行仍 4 列 (zh,en,ja,es)，**新增 `ExtraKo / ExtraDe / ExtraFr` 三个补充字典（各 210 键，脚本校验 210/210 无缺无重）**，`Txt.T()` 按 `App.Lang` 优先取补充字典、缺失回落英文列。
- 翻译语料：WebUI 410 键 ×3 语言 + 引擎 210 键 ×3 语言全量逐条人工翻译（含 `{0}`/`{n}`/`{bpm}` 占位符位置语义化与数量词适配）。

**2. 冗余介绍移除 + 悬浮窗统一开/关 + 未连接禁点（`2::`）**
- 「主心率（跟随悬浮窗）」类冗余括号说明移除：`hb.srcmain` 全语言简化为「主心率 / Main HR / メイン心拍 / Ritmo principal」；删除随 UI 变更不再引用的 `float.open / float.closeall` 两个 key（两处引用点均已改），保证下拉与按钮文字布局对齐。
- **悬浮窗二元布尔控制器**：新增 `components/FloatToggle.vue` —— 单个按钮只显示「开 / 关」（`sw.on/sw.off`），按 `floatCount>0` 呈现状态，点击在 `floatOpen()` / `floatCloseAll()` 之间切换，**无任何已连接设备时灰显禁用**；挂到心率页「悬浮窗」卡表头右侧（原浮动显示个数的位置）与工作台「快捷操作」面板；心率页操作行与悬浮窗卡内原来各自成对的「打开悬浮窗 / 关闭全部悬浮窗」按钮全部移除，只保留这一处一致的开/关控件。
- **未连接设备禁点**：心率页设备列表行内「为此设备开窗」按钮在 `!d.connected` 时灰显禁止点击（`:disabled="!d.connected"`）。

**验证**
- `npm run locales` 410 keys（五张基础 JSON）；`vue-tsc` 0 error；vitest 36/36（新增 8 语校验）；三个 C# 工程 0 error；`build.ps1 -Mode debug -SkipWeb` 全产物齐。
- 引擎补充字典完整性脚本校验：ExtraKo / ExtraDe / ExtraFr 均 210/210、无缺键无超集。

### 步骤 59：断连弹窗单层模糊 + 日志目录化 + 右键内置菜单/Layout Edit + 崩溃守护（第二十六轮）
需求 7 项（用户 `1::`~`7::`）。

**1. 连接错误弹窗：单层模糊 + 退出入口（`1::`）**
- `OfflineOverlay.vue`：遮罩去掉黑色半透明底（`background: transparent`），只留 `backdrop-filter` 单层模糊；错误信息面板不再套深色卡片（无自己的底色/边框/阴影），滚动详情块只保留下边线。
- 遮罩右上角新增常驻「退出程序」按钮：壳内走 `win.close`（与红灯同一确认流程，避免被遮罩盖住的标题栏按钮不可达），浏览器退化为关闭标签页。

**2. 引擎日志硬编码中文 → 目录化（`2::`，跟随设定语言）**
- 新增 `Core/LogText.cs`：`LogText.L(key, args)`，两级语言策略 —— **zh-cn/zh-tw 走中文行，其它语言（en/ja/es/ko/de/fr）统一回落英文行**（诊断日志保持单一语言，避免跨重启/共享时混排）。
- 全量迁移 127 处 `App.Log.*` 中带中文的调用（跨 21 个文件）：BLE/设备注册表、数据库、健康校准、OSC、AppBoot/Program/壳拉起、Webhook/Web/RemoteAuth、AppHub 动作日志、旧 WinForms 主窗体/托盘/悬浮窗、硬件四路采集（`硬件(wmic): CPU=... GPU0=... DIMM0=...` 等）、变量/PDH、导出，含 `SysInfoService` 硬件采集行与 `Program.OnFatal` 的 crash_dump 文本；三元中文词对全部拆成 `_on/_off` 独立完整句。
- 机器校验：代码引用 key 116 个全部命中目录；App.Log 内中文残留 0 处。

**3. 数据路径删除「自定义」（`3::`）**
- `DataPathCard.vue` 只留 AppData（默认）/ 程序目录两个预设；删除自定义路径输入与 `__custom__` 分支，并移除 `settings.pathcustom` key（lang.ts + 三张补充 JSON 同步删）。原因：自定义路径本身仍须写进 exe 旁的固定 `data_location.txt` 才能被读到，程序目录不可写时该设计自相矛盾。

**4. CLI 冗余括号解释清理（`4::`）**
- 全局清理「自指/分类式」括号：`退出（CLI 专属）`、`自检（CLI 专属）`、`清屏（由终端本地处理）` 等 → `退出 / 自检 / 清屏`；同步 zh/en/ja/es 四列与 ExtraKo/ExtraDe/ExtraFr（韩/德/法），并顺带在 desc/usage 里去掉同类写法。保留承载语义的括号（参数范围、前提条件等）。

**5. WebView 右键 → 程序内置菜单（`5::`）**
- 壳（WebView2Host）关掉默认菜单（`AreDefaultContextMenusEnabled = false`）；新增 `ContextMenu.vue` + `contextmenu.ts` 自绘菜单（跟随主题），在 App.vue / App_web.vue 全局挂载。
- 设备行统一打 `data-dev-mac/name/connected/connecting`：Overview 速览、Dashboard 设备面板、心率页列表、Devices 表（名称/操作列）右键即弹设备菜单 —— **连接/断开、为此设备开窗（未连接禁用）、前往设备页**（解决「很多 Tab 有设备列表却只有一处能连接」）；可编辑控件在浏览器保留原生菜单、壳内给复制兜底；空白右键 = 刷新 +（工作台）Layout Edit 入口。

**6. Layout Edit 布局编辑（`6::`）**
- 编辑对象 = 工作台（Dashboard）卡片网格：卡片上浮（阴影+虚线描边），支持 **移动**（拖标题交换）、**删除/隐藏**（头部隐藏钮 + 顶部恢复列表）、**修改**（头部 −/+ 调占宽 1~3 列，持久化）、**自定义**（隐藏面板加回 / 一键重置）。占宽记录 `hrm-dash-spans`。
- 三个入口：侧栏「工作台」右键菜单、Dashboard 页面空白右键菜单、页面右上角常驻「编辑布局/完成编辑」按钮；离开工作台自动退出编辑态（布局层监听路由）。
- 注：编辑态落点在 Dashboard 的面板网格（有状态化注册表支撑、删除安全可恢复）；任意页面零散 x-card 的通用编辑需要在各页做元素身份建模，本次未扩张，避免误删功能卡。

**7. Crash Dump / crash 命令 / 独立崩溃守护（`7::`）**
- 命令面：`crash <exit1|null|kill>` —— exit1 = `Environment.Exit(1)`；null = 后台线程抛 `NullReferenceException`（走 AppDomain.UnhandledException → crash_dump.txt + exit1）；kill = `Environment.FailFast`。CLI / 前端 Console / Web 终端共用（同一 CommandShell）。
- 独立守护 **hrmdump.exe**（新工程 `Shells/DumpHost`，WinExe、不引用主程序集）：`hrmdump watch <enginePid>` —— 引擎 code 0 静默；异常退出（code != 0）时若前端壳 hrm-webui 还活着 → 向其主窗口广播注册消息 `hrm-engine-crash`(wParam=退出码)；**壳也阵亡（前后端一起炸）→ 写现场到 `%TEMP%\HeartRateMonitor-crash\`**。
- 壳侧：MainForm 记录引擎路径、注册消息、WndProc 收到后 `PostToPage({type:'engine.crash',code})`，并新增页命令 `engine.restart` 重新拉起 HeartRateMonitor.exe；前端 `CrashOverlay.vue`（单层模糊，与断连遮罩同风格）展示崩溃提示，按钮 = **重启后端 / 退出程序 / 稍后**，重启后由现有 watchdog 自动重连。引擎 GUI 启动时 `ProcessInfo.LaunchCrashWatch()` 拉起守护；build.ps1 新增 3g 段发布 hrmdump（SUMMARY 加行）。

**验证**
- `npm run locales` 415 keys；`vue-tsc` 0 error；vitest 36/36（含 Dashboard.spec 回归）；三个既有 C# 工程 + hrmdump 0 error；`build.ps1 -Mode debug -SkipWeb` 全产物齐（hrmdump ok）。

### 步骤 60：重启二次校验 + 未翻译文案清零 + 右键菜单修复 + 拖拽防挂地址 + 单击详情/双击连接 + HR AVG/MIN/MAX（第二十七轮）
需求 6 项（用户 `1::`~`6::`）。

**1. Restart Engine 二次校验增强（`1::`）**
- 根因：崩溃/挂死的旧引擎仍占着单实例锁（`SingleInstance` Mutex）与 Web 端口，新实例启动即被守卫秒退 → 重启「有概率失败」。
- `WebView2Host/MainForm.cs` `RelaunchEngineAsync`：拉起前先 `KillStaleEnginesAsync()`（结束残留 HeartRateMonitor 进程并等其退干净）；重试 3→4 次、端口轮询 12s→15s；引擎路径缺失也回推 `engine.restart-failed` 而非静默。前端 `shell.ts` / `CrashOverlay.vue` 已有 restarting/restarted/failed 三态与重试 UI。

**2. 全项目扫描未翻译文案（`2::`）**
- WebUI（.vue/.ts）：正则扫全部 CJK 行，除注释与 lang 表外无残留运行时文案；`ws.ts` 的「WebSocket 连接已断开」已改走 `i18n` 键 `net.wsdown`（并修掉 vue-i18n 全局 t 的过深类型推断：模块内收敛成窄签名）。
- 壳（WebView2Host）：新增 `ShellText.cs`（镜像引擎数据目录解析读 `config.json` 的 ui.lang，非中文统一英文兜底），Program.cs「后端未运行」、MainForm「WebView2 初始化失败」「页面加载失败」错误页不再是中文硬编码。
- 引擎侧：`CliHost/Program.cs` 3 条启动日志 + `BleManager` 4 处抛错字符串迁移到 `LogText`（新增 log.cli.* / log.ble.* 键，zh/en 双行）。

**3. 右键菜单无法唤起修复（`3::`）**
- 双重根因：① 宿主此前 `AreDefaultContextMenusEnabled=false` 时部分 WebView2 版本不派发 DOM `contextmenu` → 恢复为 true，页面捕获阶段统一拦截（未 preventDefault 场合由默认菜单兜底）；② `ctxState` 是普通对象，模板 v-if/v-for 读它不触发重渲染 → 改 `reactive`，`ContextMenu.vue` 收敛到共享 `openCtx/closeCtx`（位置也存进 ctxState）。

**4. 左键拖控件把网址挂到鼠标（`4::`）**
- `dragGuard.ts`（已存在）在 document 捕获阶段对非 `[draggable=true]` 的 dragstart 一律 `preventDefault`；`globals.css` 全站 `user-drag:none`（工作台面板头 `draggable=true` 放行）。本轮复核无缺口。

**5. 设备行多触发：单击详情弹窗 / 双击连接切换（`5::`）**
- 新增 `deviceDialog.ts`（模块级 ref + 单击 240ms 延时、双击取消防抖；拖选文字时放行不弹窗）与 `components/DeviceDialog.vue`（本设备独立曲线 `deviceCurves[mac]`、窗口 MIN/AVG/MAX、信号/广播频率/报告频率/上报数、连接切换、开独立悬浮窗、保存/取消保存、前往设备页）。
- 触点：心率页设备列表、工作台 DevicesPanel、Overview 速览、Devices 表（`.dev-body` 委托，避开按钮/勾选/可复制文本）；右键设备菜单新增「设备详情」入口。DeviceDialog 挂载进 App.vue / App_web.vue。

**6. 心率 AVG/MIN/MAX（`6::`）**
- `stores/app.ts` 新增 `statsOf(source)`：对 `curveOf(source)` 的 >0 点求 min/avg/max/count（口径=可见曲线窗口，主卡/面板/详情弹窗一致）。
- 心率页主卡大字下方由单一「平均」改为 最低·平均·最高 三值行（`title` 提示窗口统计）；工作台 BpmPanel 同步换成 当前 + MIN/AVG/MAX + 已连数。
- 新增键：`hb.winstats / dev.detail / dev.reports / dev.clickhint`（lang.ts 五列 + ko/de/fr 手译）。

**验证**
- `npm run locales` 426 keys；`vue-tsc` 0 error；vitest 36/36；引擎（App）、hrm-webui、hrmcli 三个 C# 工程 0 error（仅既有 NU1903/MSB3277 警告）。

### 步骤 61：左上角指示灯胶囊化 + 内容自定义（第二十八轮）
- 需求：把左上角标题左侧的灯点改成胶囊，跟随全局圆角（`--radius`）与主题（`--secondary/--border`），并在设置里可自定义显示内容。
- `prefs.ts` 新增 `brandPill`（`hrm-pill`，默认 ping）：off / ping（延迟 ms，灯点按 ≤15ms=绿 / ≤100=橙 / 其余=红）/ avg（平均，info 蓝）/ main（主心率，跟随 hbMainSource 强调色）/ devices（设备数）/ connected（已连接数，绿）。
- 新组件 `components/BrandPill.vue`（语义灯点 + 胶囊 + tabular 数值；无数据灰 —；侧栏折叠时退化为灯点），替换 MainLayout.vue / MainLayout_web.vue 里原 `.brand-dot`（globals.css 对应样式删除）。
- 设置页（Settings.vue / Settings_web.vue 外观卡）新增「左上角指示灯」下拉与提示；新键 nav.ping / nav.pilloff / settings.pill / settings.pillhint（8 语齐，locales 430 keys）。

**验证**
- `npm run locales` 430 keys；`vue-tsc` 0 error；vitest 36/36；生产构建通过。

### 步骤 62：圆角/主题改动实时刷新边缘渲染（第二十八轮补丁）
- 现象：修改一次窗口圆角（或主题底色）后，边缘（圆角缺口 / DWM 边框交界 / 底色交界）残留旧合成帧，要再改一次才恢复正常 —— 属于「只改属性、未触发重绘」。
- `WebView2Host/MainForm.cs` 新增 `RefreshFrameNow()`：整窗失效重绘（`RedrawWindow` 含边框与全部子窗口，`RDW_UPDATENOW` 同步到当前帧）+ `SWP_FRAMECHANGED` 让系统/DWM 重新评估无边框窗口非客户区与圆角合成 + WebView/窗体 `Invalidate`。
- 调用点：`SetCorner`（改圆角即改即生效）与 `ApplyTheme`（底色/深浅/DWM 边框色变化）末尾；新增 `RDW_UPDATENOW / SWP_*` 常量与 `SetWindowPos` P/Invoke。

**验证**
- hrm-webui 工程 0 error（仅既有 MSB3277 / CS0108 警告）。

### 步骤 63：多设备上次连接全量自动重连 + 禁文字选中 + 底栏胶囊交互 + 设备卡子Tab/重命名 + CliHost 自包含引用修复（第二十九轮）
需求 4 项（用户 `1::`~`4::`）。

**1. 上次多设备连接 → 启动全部自动连接（`1::`）**
- 根因：`AutoConnectLast` 只连 `History` 首项（最近一台），多设备场景丢设备。
- `Config.Devices` 新增 `LastConnected`；`DeviceRegistry.RecordHistory` 置本次会话「已连过」标记，`SnapshotSessionDevices()` 在正常退出（BleManager.DisconnectAll 顶部）快照当前连接集到配置——会话内没连过不动旧清单（崩溃重启兜底）、连过但已全部断开则清空。
- `AutoConnectLast` 改为逐台带超时重连 LastConnected（≤8 台，每台失败不阻断后续；无 LastConnected 回退 History 首项）。log.dev.autoconnect_* 文案支持设备名参数。

**2. 禁止页面文字选中（`2::`）**
- `globals.css`：body 与全部元素 `user-select:none`（防双击设备行/胶囊时误触选中文字）；输入框/文本域/下拉/可编辑区保留 `user-select:text` 以便编辑复制。

**3. 底栏设备胶囊 + 设备卡扩展（`3::`）**
- `StatusBar.vue`：胶囊悬停上浮+辉光+wiggle 动画并弹出气泡简信（名称/MAC/当前 BPM/RSSI/报告频率/操作提示，`prefs.statusPop` 可整体开关，设置页新增 XSwitch）；**单击胶囊 = 连接/断开切换**（与设备列表双击等价）。气泡 fixed 贴底栏、随主题、reduced-motion 关闭动画。
- `DeviceDialog.vue`：新增 **重命名控件**（主题化对话框，与设备页一致）与三个子 Tab —— 概览（曲线+窗口统计）/ 连接记录（状态/识别信息/上报/自动重连）/ 心率历史（调用 `/api/monitor` devices 段取该设备 DB 聚合 count/min/avg/max/last，记录未开启时给空态提示）。

**4. CliHost publish NETSDK1151（`4::`）**
- 报错：`HeartRateMonitor.csproj` 被当作 self-contained 可执行文件被非自包含 CliHost 引用。修复：App 与 CliHost 的 csproj 显式默认 `<SelfContained>false</SelfContained>`（standalone 分支发布命令行仍以 `--self-contained true` 覆盖），消除「随 RID 默认翻 true」造成的不一致；两种方式（无参数 / self-contained true）发布均验证通过。

**i18n / 验证**
- 新键：dev.taboverview/dev.tabconn/dev.tabhr/dev.lastreport/dev.norecords、settings.statuspop/settings.statuspophint（8 语齐，`npm run locales` 437 keys）；`vue-tsc` 0 error；vitest 36/36；生产构建通过；引擎 + CliHost 0 error（仅既有 NU1903 告警）。

## 2026-09-06 第三十轮：32 项批量需求（批次一~十一，全量）
suspect.md 工作表 32 项按后端→前端→i18n 顺序分批提交（1339200 ~ 本条目对应 HEAD）。

**后端批次**
- #13 写盘比对防闪：`Config.SnapshotJson()/SaveIfChanged()`，各配置写路径无变化不落盘；前端 `useAutoSave` 同内容跳过、`markSaved()` 成功后置底栏已保存。
- #14 底栏右侧统一「已保存」徽标 + 最新引擎日志（stores/notify + StatusBar），各页就地“已保存”提示移除。
- #9 屏蔽全链路 UI（右键屏蔽/解除 + 屏蔽管理列表）；#5 右键重命名/保存；#12 全选反选；#10 批量二元按钮合并（任一已连→断开；任一未保存→保存）。
- #3 断连自动重启后端（壳重拉引擎 + 恢复探测 + 设置开关）；#19 exe 无参启动拉主程序（CliHost/DumpHost 壳拉 hrm-webui.exe 后 return 0）；#2 DWM 焦点切换重绘。
- #17 VRChat 运行状态：`AppHub.VrchatStatus()`（Process 采样：PID / CPU% 差分 / WorkingSet / Responding / StartTime），5s 定时推 WS `vrchat_status` + REST `GET /api/vrchat`；Pusher 页头部状态卡（挂载直查一次 + WS 订阅）。
- #6 数值上限移除（后端写路径放开、前端 XRange/theme 不封顶，仅保下限）；#7 ASCII 横幅换 HeartRateMonitor figlet（CLI + Console 同源，窄终端 78 列退化）；#29 断连弹窗退出钮入操作行。
- #18 构建自定义图标：`build.ps1 -IconDir`（交互可空询问），按 exe 注入 engine/webui/cli/dump.ico 的 `ApplicationIcon`，tray.ico 覆盖进输出 `image/`；`build.bat` 透传 `%*`；`TrayHost` 运行时 `image\tray.ico → image\app.ico → SystemIcons` 三级降级。
- #32 OSC 类型化写入（VRChat 库外部接口，低风险项）：C 引擎新增 `osc_engine_send_bool`（`,T/,F`）与 `osc_engine_send_float`（`,f`）导出；C# `OscEngine/OscService.SendParam` 桥接；`POST /api/osc/custom` 支持 `kind=text|float|bool`（float 先渲染 `{}` 变量再解析、bool 用 flag），覆盖 `/avatar/parameters` 参数写入；typing 常亮/脉冲联动依赖聊天框时序方案未定，记延期（bool 通道已就绪）。

**前端批次**
- #4 曲线新增 step/gradient 并全站同步（Overview/Dashboard/Heartbeat/DeviceDialog 消费 `hbMode`，选项表集中 `CURVE_MODES`）；#15 设备权重排序开关 + 表头点击排序（score 兜底）。
- #23 XSelect 收回动画（0.12s 反向 Transition）；#24 右上角明暗切换（ThemeToggle，壳标题栏尾部/浏览器浮动胶囊，自定义主题时隐藏）。
- #27 侧栏折叠态品牌行整体居中 + 指示灯；#28 双击导航空白区收起/展开 + 底部收起按钮与底栏 26px 等高。
- #25 浅色主题全局（`--elev-sm/md/lg` + `--scrim` 明暗两套语义 token，8 处硬编码阴影/遮罩改引用；设置页自定义主题独立 XSwitch）；#26 设置页重排（外观/界面行为/存储与数据/关于四卡 + 跳转 About）。
- #21 Dashboard 12 段栅格：默认 span 4/8（registry 全改 12 列值），旧 localStorage 1~3 自动 ×4 迁移，Layout Edit −/+ 步进 1~12，响应式按视口 12/6/1 列换桶（matchMedia）；#22 隐藏与调宽按钮仅 Layout Edit 态渲染，正常模式零编辑控件。
- #20 About 重设计：`src/about.ts` 本地元数据（作者/头像路径/GitHub 仓库）+ GitHub API 拉 stars/forks/license/open issues（未发布/离线静默 `—`）；头像本地文件缺失回退首字母圆形（零远端图片依赖）；EXAMPLE 占位全删，链接/更新/致谢为真实结构。
- #16 注释文案清理（移除冗余 hints、占位说明）。

**i18n / 验证**
- 新键（8 语齐）：hb.modestep/hb.modegradient、dev.weightsort、settings.custom/customhint/behavior/gotoabout、nav.themetoggle、vrc.*、dash.wider/dash.narrower、about.author/stars/forks/openissues/ghhint/notpublished、push.kind*（`npm run locales` 481 keys）。
- 门槛全绿：`vue-tsc` 0 error；vitest 36/36（Dashboard.spec 适配 #22 编辑态交互、i18n.spec span 1~12）；引擎 dotnet build 0 error（仅既有 NU1903 告警）；C 引擎 gcc 编译通过；build.ps1 语法解析通过。

## 2026-09-07 #22：结构迁移——重心回 Windows（Shared 全量并入 + 构建脚本上移 + ~3.9GB 清理）
（第三十一~三十四轮明细未单列本文件条目，见 plan.md 注记与提交历史。）

**迁移**
- `Shared/` 六项全量并入 `Windows/`：WebUI / Engine / docs / Skills / config / image——`Windows/` 成为唯一源码根（App / Shells / WebUI / Engine / config / image / Skills / docs / Release.json），git 按重命名跟踪。
- `build.bat` / `build.ps1` 上移工作区根（build.bat 以 `%~dp0` 自定位调用同目录 build.ps1）；`Built/` 产物仍在仓库根（不入库）。

**build.ps1 路径重写（根目录视角）**
- `$Root` 取脚本所在目录，`$Win = <根>/Windows`，`$Engine/$App/$WebUi` 由 `$Win` 派生；`$Cfg = Windows/config`（原 `$Shared`）；`$ImgSrc = Windows/image`（去掉 Shared 回退路径）；`$OutBase = <根>/Built`。
- 文件头「目录约定」注释整段重写；修复编辑引入的双 BOM 后 PS 5.1 解析通过。

**文档同步**
- `docs/about.md`：§3 目录树整段重写（根目录 build 脚本 + Windows 全源码 + Release.json 行）；正文 6 处 `Shared/...` 引用替换（Archives / 主前端标题 / build-webui / 引擎 API / 构建命令 / 构建顺序）。
- `Skills/Attention.skill`：3 处 `Shared/WebUI` → `Windows/WebUI`。

**删除（约 3.9GB）**
- `Shared/`（迁移后空壳）与根 `WebUI/`（空目录）。
- `Windows/RustUi/`（1.9GB 早期 egui 前端残留，git 0 跟踪）。
- `Backup/`（2GB 历史整站快照，.gitignore 已覆盖）。

**验证**
- 前端门禁：vitest 50/50、`vue-tsc --noEmit` 0 error、vite 生产构建通过。
- 根目录 `.\build.ps1 -Mode debug -SkipWeb` 全绿（config / webui / engine / shell / cli / dump / release 全 ok）；`hrmcli about` 输出 release 段正确；GUI 启动后 `/api/status` 200 冒烟通过。

## 2026-09-07 第三十五轮：图标双修 + Phase Web8 收尾批（#9/#16/#17/#18/#24/#26）
（本轮与 #30 全量校验同批；第三十一~三十四轮明细见 plan.md 注记与提交历史。）

**图标双修（用户 1::/3::）**
- 高权重设备启动后偶发默认蓝牙图标——根因是连接竞态：`Category()/HrMarked()` 只看登记表广播名+别名，启动 `AutoConnectLast` 在收到带名广播前就按 MAC 连接，而连接后 Windows 不再投递该设备广播 → 名字永远补不上 → 分类停在 generic。修复：`BleManager.TryConnectAsync` 订阅成功后调新增 `DeviceRegistry.OnGattVerified(mac, name)`——连接即证实 0x2A37（Type 补 Heart Rate Monitor）+ 连接期系统名回写登记表（回写先于 Connected 事件，BroadcastDevices 即带正确分类）。
- 构建图标逻辑全部移交 Release.json：build.ps1 移除 `-IconDir` 参数与交互询问；0.5 段读清单 icons 段（剥注释解析）注入四工程 ApplicationIcon，tray 按声明路径部署进输出；未声明/缺文件跳过不阻断。Release.json icons 五角色统一 `./image/heart.ico`；四 PE 体积各 +28160B、ExtractAssociatedIcon 96×96 验证嵌入。

**Phase Web8 收尾（用户 2:: 续做 plan.md）**
- `#9` 布局编辑扩展全 Tab：新建通用 `CardGrid.vue`（插槽版，与 Dashboard 同机制：12 列三桶响应式/长按拖动 FLIP/隐藏恢复/占宽 1~12/重置，按视图独立持久化 `hrm-cards-<view>-*`；bare 整卡/noCc/fill/head-* 自定义头），接入 14 视图；Settings 关于卡独立第二网格；右键入口泛化（About 排除）；路由切换退出编辑态。
- `#16` CLI 非阻塞扫描：`scan on` = 持续直到 `scan off`；CliApp 订阅 ScanStarted 重置去重表 + DeviceFound 每设备每轮一行 `[NEW]`（UNKNOWN→实名补打一次）。
- `#17` GUI 控制台键盘劫持：window keydown capture——Tab=命令补全（挂载拉 help 解析命令表）、Ctrl+L=清屏；焦点在其它可编辑控件时不抢。
- `#18` Safe Mode：横幅仅激活时显示（警示条+「正常重启退出」），普通模式零渲染；前端入口只放设置页（Settings_web 行为卡按钮，用户收口指示）；三连 R 应急手势保留。
- `#24` LICENSE：新建 `Windows/LICENSE`（MIT 全文）+ build.ps1 部署到产物根 + About 底部常驻许可提示行（license_context 优先）。
- `#26` 启动 Splash：新建 `SplashForm.cs`（无边框居中 GDI+ 自绘：Logo/标题/状态/进度条；Step 任意线程安全、Pump 供无消息循环段、最短 500ms、20s 超时兜底）；Program.Main 在后端探测/拉起前显示，步进 5→18→40→55→72→88→100%，NavigationCompleted/提前关闭收掉。
- `#30` Prompt history 全量校验：31 项逐项对照，机器抽查 10 项关键机制在位；本 Phase 全部完成。

**验证**
- vue-tsc 0 error；vitest 50/50；vite 生产构建通过；根目录完整 debug 构建全绿（debug-20260907_160532，含 license/tray icon 部署）。
- 引擎 `/api/status` 200 + `/webui/` 200；`hrmcli scan on/off` 实测（开始持续扫描/已停止扫描）；壳启动冒烟 8s 存活正常关闭（Splash 流程无崩溃）。
- lang2json 512 keys（新增 console.keys / safe.entryhint / safe.enter / safe.mode / about.licensefoot，8 语齐）。

## 2026-09-07 第三十六轮：五项批（作废控件/悬停气泡/箭头对齐/Prompt 复查/#11 重开）

**A~C（用户 1::/3::）**
- 移除作废拖动图标：CardGrid/Dashboard 头部 GripVertical（拖动已改整头长按）。
- 高级主题卡箭头对齐：CardGrid 新增 `head-tail-<id>` 头部尾部插槽（渲染在编辑控件之后），Settings×2 的自定义 ChevronDown 移入并按 .cc-chev 同款重制（15×15、opacity .75、折叠 -90°/展开 0°），与注入折叠箭头视觉一致。
- cardCollapse 展开高度不足（设置 > 外观卡场景）：applyState 展开分支改探针法量全高（瞬时去 transition 置 height:auto 读 offsetHeight 含 padding 再回 0 起跳）+ transitionend-once 释放内联高度，360ms 定时器兜底（后台标签页不触发 transitionend 导致高度滞留）。

**D（用户 4::，Prompt 复查项）**：14 个非 Dashboard 可编辑视图页头统一「编辑布局/完成」按钮（新建自包含 `EditLayoutBtn.vue`，复用全局 layoutEdit store 与 dash.enterEdit/exitEdit 键）；页头改 `page-head with-actions` flex 布局（globals.css）；Web 页非管理员随网格一起隐藏按钮。

**E 排查批（Prompt 尾部 4 项，四代理并行定位后修复）**
- `E①` GHz 不显示：根因在前端渲染层——HwPanel.vue KEYS 四个键名（CPU_LOAD/CPU_TEMP/GPU_LOAD/RAM_LOAD）后端变量表不存在（实际 CPU_USAGE/CPU_TEMP_MAX/GPU_USAGE0/RAM_PERCENT），且 app.spec mock 用同一虚构键掩盖错位；后端链路健康（第十七轮无回归）。修复：KEYS 与 spec mock 全部改实际键名。
- `E②` 前端数据不落盘（三处静默丢失窗口全修）：useAutoSave hydrate 5s 超时自动解封印（引擎离线窗口期打开页面不再永不保存）+ 保存失败 3s 自动重试 + visibilitychange/beforeunload flush（改完 400ms 内 F5 不丢）+ 失败打点 notify.markSaveFailed → StatusBar 常显红色「未保存（将自动重试）」徽标（成功清除）；ui store 主题/语言/品牌改动自动防抖 600ms 推送（原依赖各调用点手动 push 且失败静默），成功/失败同样走徽标；**视图偏好进后端**：prefs.ts 包装 localStorage.setItem 捕获全部 hrm-/hb- 前缀写入（含 cardCollapse/CardGrid/theme/i18n 等直接写 localStorage 的模块，零侵入）防抖 800ms 批量 POST /api/prefs（新端点，Config 新增 Prefs 字典、SaveIfChanged 写盘；远程 user 角色禁用），启动时回填本地缺失键（本地已有值优先）——布局/卡片顺序/折叠/列宽等跨设备/重装不再丢。
- `E③` MVP 客制化寻回：对照 OldPy MVPv1 源码，7 项已找回 5 项，补齐 2 项——`visible` 启动自动恢复（Program.cs tray.Load：上次会话主窗可见则恢复，安全模式跳过；开窗即记 true、关窗记 false）+ 几何自动回写（主窗 ResizeEnd/关窗回写 Window.Geometry，ApplyStyle 对已开主窗即时应用几何，原只能前端手敲）。透明度/字体/边框确认任何阶段均无（非丢弃项）；float.perwindow/float.saved/settings.float 三个无引用残留键留待清理。
- `E④` 悬浮窗开关：FloatToggle=XSwitch 包装与 floatMain 双向映射经核查已在位（风格/映射两指控已解决）；补短板——FloatWindowHost.Changed 事件 → AppHub 转 `float` WS 事件 → 前端 onWs 即时 sync（托盘/悬浮窗右键/Alt+F4 等本地关窗不再等 5s 轮询回弹）；FloatToggle api 失败 catch 回读真实状态。

**I（用户 Insert：#11 数值限制重开，原第三十四轮仅移除前端 max 属半吊子）**
- 记录时长根因：`stores/app.ts CURVE_MAX=180` 硬编码与显示窗口 hbPoints 脱钩（设 1000 只留 180 点）。按用户定案「原逻辑全部删除重写」：删常量与 slice(-179) 复制机制，曲线缓冲动态跟随 hbPoints（curveCap()，主/设备曲线同策略），原地 push/splice 避免大窗口整组复制，仅留 1e6 绝对护栏防 OOM。
- 后端 clamp 按「能放开就放开」：WebhookThrottleMs/PushIntervalMs/Hw.IntervalMs 上限放开只保下限（200ms 节流/防采集空转）、pauseMs 120s 上限删、HrmDb 导出 10 万行上限删（整库走 sqlite 格式导出）、Monitor buckets 64 上限删、AutoDetectTimeout 双上限（3~60 与 3~30）统一放开；decimals 收至 15（.NET Round 硬边界，原 6 为随意值）；reconnectIntervalSec 后端下限 3→1 对齐前端；物理/协议边界保留（端口 65535、CPU 百分比、字号、ws 退避）。
- XRange 全量迁移（用户定案「全部迁移」）：20+ 原生 type=number（Heartbeat 平滑/点数/健康四因子/悬浮窗刷新/导出行数、Devices 5 项、Hardware 2 项、ApiServer 2 项、Pusher 端口×2/间隔/暂停、Web 端口/闲置、Monitor 桶数）+ Settings_web 壳圆角原生 type=range 全部换 XRange；过时文案（平滑「1–30」、点数「30–Inf」）修正为 ≥1；Logs.vue 查询条数从硬编码 800 改 XRange 可调（1~5000 与 LogBuffer 容量对齐，持久化 hrm-logs-limit）。

**F~G（用户 2::，悬停气泡全站化）**
- 基建：`bubble.ts` reactive 单例（show/hide/cancel + 400ms 抑制窗）+ `Bubble.vue`（Teleport+fixed、水平 clamp 110px 边界、上方优先/顶部不足翻下方、实测尺寸定位、z-1200、pop 0.14s 过渡、prefers-reduced-motion 降级、pointer-events:none）+ `v-bubble` 指令（mouseenter 3s 计时、pointermove 坐标缓存、已显示后 40px 位移容忍、mouseleave/pointerdown 取消；无值回退元素 title 属性）+ 全局关闭器（wheel/scroll/Escape capture）+ 双 App 根各挂一份。
- 全量迁移（4 子代理 + 手工收尾）：14 视图 + 组件层的 A 类控件提示（44 处 hint span 删除挂对应控件）、B 类卡头/页头描述（21 处挂卡片标题/h1，含 autosave 徽标）、30+ 处原生 :title 全部换 v-bubble（数据类设备名/MAC/日志全文同样迁移）；StatusBar `.sb-pop` 悬停卡整体迁移到统一气泡（chipPop() 组装多行：名称/MAC/当前心率/RSSI/通知率/点击提示，statusPop 关→空串不弹），sb-pop 样式块与定位逻辑删除；XDialog 的 :title 为组件 prop 保留。placeholder/空态/错误警告/动态状态值按定案保留常显。

**验证**
- vue-tsc 0 error；vitest 50/50；vite 生产构建通过；dotnet build 0 error；根目录完整 debug 构建全绿。
- lang2json 513 keys（新增 common.unsaved，8 语齐）。

## 2026-09-07 第三十七轮：Windows 硬件变量 + 外链浏览器 + 壳布局交互批（#40~#42）

**#40 WindowsHWAPI.skill**
- 逐条读 `Windows/Skills/WindowsHWAPI.skill` 后按「纯 Win32/PDH、无厂商 SDK、低开销」落地：PDH 长生命周期 query 增内存可用/提交/提交率及磁盘读写/总吞吐/队列；`GlobalMemoryStatusEx` 补提交限制/已用/可用与虚拟内存总/已用/可用；`GetSystemInfo` 补 CPU 架构/类型/页大小/分配粒度，`GetSystemMetrics` 补主屏/虚拟屏尺寸和显示器数，`DriveInfo` 原生卷容量替代 WMIC 依赖。
- 自动检测修正：PDH 回退由「任一结果存在即整组可用」改逐指标（CPU/进程/线程各自判断）；WMIC 可用性从 OS build 猜测改检测实际 `wmic.exe`；不接 NVML/ADLX/IGCL，按 skill 限制不伪造 GPU 温度。

**#41 外部链接**：WebView2Host/MainForm 在首次 Source 前注册 `NewWindowRequested` + `NavigationStarting`；与 startUrl 同源放行，绝对 http/https 外链先 Handled/Cancel 再 `Process.Start(UseShellExecute=true)` 交系统默认浏览器，file/javascript/custom protocol 不交 Shell。About 等前端 target=_blank 外链无需逐个改动即可统一生效。

**#42 BATCH（29~31）**
- 29：复核 NavMenu/品牌行折叠居中规则在位；BrandPill 去掉 collapsed 强制退化，折叠态仍显示心率/延迟等数据胶囊（52px 限宽+截断防溢出）。
- 30：MainLayout_web 同步浏览器版侧栏 `nav-grip`（96~480px、132px 阈值自动折叠、`hrm-nav-width` 共享持久化）；StatusBar 顶缘 `status-grip` 鼠标左键直写 `barH`（18~96px）；TitleBar_web 底缘 `title-grip` 直写新偏好 `titleH`（24~96px、`hrm-title-h`），pointerdown.stop 防止触发窗口拖动。
- 31：ShellState 增可空 Left/Top（兼容旧 JSON），关闭时从 Bounds/RestoreBounds 同尺寸保存，启动 Manual 恢复，若与所有屏幕工作区无交集则回退居中。

**验证**：HeartRateMonitor.csproj 0 error（既有 SQLite NU1903 警告）；WebView2Host.csproj 0 error（既有 WindowsBase/Scale 警告）；vue-tsc 0、vitest 50/50、diff --check 通过。

**追加：启动后自动 OSC 推送**
- `OscSection` 新增默认关闭的 `auto_start` 持久化字段，状态与配置 API 双向映射 `autoStart`；Pusher 页 OSC 卡片标题区增加自动保存的开关和 8 语悬停说明。
- 主程序完成 OSC 接收初始化后按开关调用既有 `SetConnected(true)`，直接复用模板推送定时器，保持接收常驻与手动连接/断开语义不变。

## 2026-09-13 First Release：HeartRateMonitor Pre 0.0.1

**发布范围**
- 完成 P0–P7 当前范围与 Phase 10 Toolkit 首批能力：VRChat 配置、日志、缓存、照片索引/检索、游戏统计和进程分析；Toolkit 写操作受 Safe Mode、admin 权限、回环日志边界及路径校验保护。
- About 改为消费内嵌 `Release.json` 与后端 GitHub 缓存快照；十语言 README、`about.md`、API 类型、CLI help 和发布清单同步为 `0.0.1-pre`。
- P8 自定义 STATUS、P9 Trigger、Toolkit 高级增强及完整人工实机矩阵明确保持未完成，不纳入本次发布声明。

**发布阻断修复**
- 修复 Toolkit 前后端路由与响应结构偏差；缓存占用分析改后台快照，避免大目录枚举阻塞 HTTP 与退出链。
- `/api/shutdown` 增加独立硬超时与有界清理，子进程清理限定当前安装链；验证引擎约 8.3 秒退出且无本安装残留进程。
- 四组件版本统一为 `0.0.1.0`；SQLite 升级至 `Microsoft.Data.Sqlite 10.0.12` + `SQLitePCLRaw.bundle_e_sqlite3 3.0.5`，传递依赖漏洞扫描为零。
- 清理临时探针、损坏翻译副本及异常命名残留，修正前端尾随空格；复核 Release 产物不携带默认 config、外部 Release.json、凭据或源码路径。

**验收记录**
- 首次发布验收时间：2026-09-13 00:37:30 CST（UTC+08:00）。
- 自动门禁覆盖：C#/.NET、Vue typecheck/Vitest/Vite、C 引擎、Debug/Releases/Standalone、四组件版本、REST/WS 与退出链冒烟；人工 Win10/Win11、多 DPI/多屏、无 WebView2/离线、普通用户、只读目录和 LAN 手机矩阵后续执行。



  - 新建 `osc_engine.h` / `osc_engine.c`：
  - 编码 `osc_encode_message`（地址 + 类型标签 `,sifhdbTFN`，4 字节对齐大端，与旧版 `Osc.cs` 字节一致）；
  - 发送 `osc_engine_send`（内部持 UDP socket + 临界区，主机名回退）与快速路径 `osc_engine_send_text`；
  - 解码 `osc_decode_to_json`（单消息 + bundle 递归，输出 JSON 数组，含 base64 blob）；
  - 接收线程 `osc_engine_start_receiver / stop_receiver`（绑 127.0.0.1，回调原始字节）。
- `gcc -shared -O2 -Wall -Wextra -o osc_engine.dll osc_engine.c -lws2_32` 编译通过，导出 7 个符号（objdump 验证）。
- 修复：`osc_engine.c` 补充 `#include <stdarg.h>`（append 用 vsnprintf）。

### 步骤 3：C# 主程序（App/，WinForms 单进程）
- `HeartRateMonitor.csproj`：net10.0-windows10.0.19041.0、UseWindowsForms、默认 WinExe（debug 由脚本改 Exe）、`osc_engine.dll` 复制到输出。
- `Config.cs`：V1 精简配置（app/osc/heart_rate/webhook/logs），读/写 config.json。
- `Logger.cs`：控制台（仅 debug）+ 文件 logs/app.log + 日志 Tab 事件；`App.cs` 全局单例。
- `Osc/OscEngine.cs`：P/Invoke 包装（LPUTF8Str、委托防 GC）。
- `Osc/OscService.cs`：推送 Job（System.Threading.Timer）+ 接收（C 回调 → JSON 解析 → Received 事件）+ 去重。
  - 修复：本机回环 UDP 重复投递（纯 Python 复现的环境行为）→ 按内容哈希 100ms 窗口去重。
- `Ble/BleManager.cs`：从 blectl 移植（Windows.Devices.Bluetooth，0x180D/0x2A37，多设备）。
- `SysInfo/`：
  - `Vars.cs` 沿用旧变量表；`SysInfoService.cs` fast（2s：CPU/RAM/GPU/温度/进程/焦点/心率变量）+ full（后台多方法，带诊断日志）；
  - `Collectors/RegistryInfo.cs`（方法 4：NT CurrentVersion + BIOS 注册表）；
  - `Collectors/WmicInfo.cs`（方法 1：仅 Win10-，`/format:list` 解析 CPU/主板/BIOS/GPU/内存条/磁盘/分区/网卡）；
  - `Collectors/PsComputerInfo.cs`（方法 2：Get-ComputerInfo，UTF-16 输出解析）；
  - `Collectors/SystemInfoExe.cs`（方法 3：systeminfo，中英文键容错）。
- `Webhook/WebhookManager.cs`：从 osc_com 移植（触发 connected/disconnected/heart_rate_updated、测试、保存）。
- `UI/`：
  - `MainForm.cs`：6 页签（OSC/心率/硬件/Webhook/日志/设置）、悬浮窗控制、API 服务器（/heartrate）、保存设置；
  - `FloatingWindow.cs`：锁定=置顶+点击穿透（WS_EX_TRANSPARENT|LAYERED），{bpm}/{img} 渲染；
  - `WebhookWindow.cs`：Webhook 编辑。
- `Program.cs`：异常策略（debug → Error Trace 弹窗 + crash_dump.txt；release → 记录后继续），`#if DEBUG` 强制 debug 模式。
- 编译修复：Timer 歧义（WinForms 隐式 using）、子命名空间 using、readonly 赋值、CollectFull 公开化、Process using。

### 步骤 4：构建脚本（build.bat / build.ps1）
- `build.ps1 [-Mode standalone|releases|debug]`：gcc 编引擎 → dotnet publish → 复制配置 → 摘要；
  - standalone：--self-contained true，WinExe 免安装；releases：框架依赖 WinExe；debug：-c Debug + OutputType=Exe（控制台）+ 详细日志；
  - 无参交互：序号 + 回车（测试通过）。
- `build.bat`：把 `--standalone/--releases/--debug` 翻译为 `-Mode X` 后调 build.ps1。
- 修复：PowerShell 5.1 未把 `--releases` 绑到 param → build.ps1 `TrimStart('-')` + build.bat 显式翻译。

### 步骤 5：config.json V1 化
- 移除 `launcher / osc_com / blectl / announce` 段；新增 `app(debug)`，`osc` 增 `receive_port`，精简 `heart_rate/webhook/logs`。

### 步骤 6：构建与联调验证
- 三个分支构建通过：`Built/releases-20260902_011855`、`Built/debug-20260902_011554`、`Built/standalone-20260902_011857`。
- C 引擎 ctypes 验证：版本 100；send_text 编码 `/chatbox/input\0\0,s\0\0` + UTF-8 文本正确；解码 `[{"address":...,"args":[...]}]` 中英文正确；s/i/f/h/d 等类型解码正确；畸形输入不崩溃。
- App 实跑：debug 控制台 + 日志 Tab + 文件三路；OSC 接收 9001 绑定成功、收到 `/v1/test` 并去重正确。
- 4 种硬件采集全部成功（真实数据）：注册表（CPU Ultra 7 270K/主板 Z890/OS LTSC 19045）、wmic（GPU0/DIMM0 24GB）、systeminfo（RAM 95GB）、Get-ComputerInfo（OS 22H2）。
- releases / standalone 冒烟：启动、引擎加载、采集完成、无崩溃。
- 清理：测试脚本与 console.out/err 移除；旧会话残留 blectl 进程已杀。
