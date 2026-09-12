# HeartRateMonitor

面向 VRChat 的即時 BLE 心率工具：透過 OSC 將心率與硬體遙測推送至聊天框——附帶懸浮視窗、遠端 Web 前端、CLI/TUI 與 VRChat 工具集的單體 Windows 應用程式。

[English](README.md) | [简体中文](README.zh-CN.md) | **繁體中文** | [繁體中文（香港）](README.zh-HK.md) | [粵語（香港）](README.yue-HK.md) | [日本語](README.ja.md) | [Español](README.es.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Français](README.fr.md)

<!-- BADGES PLACEHOLDER: 在此插入 release / license / platform 徽章（shields.io） -->

![主介面總覽](docs/images/hero.png)
<!-- IMAGE PLACEHOLDER: 主視窗總覽——側欄、心率頁、底欄裝置膠囊 -->

## 功能特性

### BLE 心率裝置

- 讀取任何標準低功耗藍牙心率裝置（Heart Rate Service `0x180D`）——胸帶、手環、運動手錶。
- **多裝置支援**：同時連線多個感測器，上限只受藍牙協定棧與硬體約束。
- 智慧裝置評分排序、別名、自動重連、弱訊號（RSSI）警示、依裝置獨立懸浮視窗。
- 自動偵測：按權重批次連線候選裝置，自動跳過音訊/智慧家庭裝置與無心率特徵的裝置。

![心率曲線](docs/images/heartbeat.png)
<!-- IMAGE PLACEHOLDER: 心率頁——大號 BPM 讀數、即時曲線（主顯示/平均/依裝置）、裝置列表 -->

### VRChat OSC 聊天框推送與即時預覽

- 以自由 `{變數}` 模板經 OSC/UDP 將「心率 + CPU / GPU / 記憶體等」推送至 VRChat 聊天框（`/chatbox/input`）。
- 編輯時每秒刷新的模板即時預覽，附字數統計，接近聊天框 144 字上限時提示（不攔截傳送）。
- 自訂 OSC 傳送（任意位址/文字）、Webhook 外送推送、OSC 接收（9001 連接埠）擷取 VRChat Avatar 參數流量、可選「啟動即推送」。

![推送預覽](docs/images/pusher.png)
<!-- IMAGE PLACEHOLDER: 推送頁——OSC 模板編輯器與即時預覽、字數統計 -->

### 懸浮視窗

- 置頂桌面小工具顯示目前 BPM（或圖片）；一個主窗 + 每裝置一窗。
- 可鎖定並點擊穿透、DPI 感知縮放、每窗幾何獨立持久化。
- 每窗資料來源（平均或指定裝置）與刷新間隔皆可設定。

![懸浮視窗](docs/images/float-window.png)
<!-- IMAGE PLACEHOLDER: 懸浮於遊戲/桌面上的懸浮視窗——主窗與裝置窗 -->

### 硬體遙測變數

- 經登錄檔、WMI、PowerShell 與 `systeminfo` 採集 Windows 主機資訊；經 PDH 取得即時指標（CPU/記憶體/GPU/顯存佔用、溫度、磁碟、記憶體認可），與工作管理員同源。
- 一切皆可為模板變數：`{CPU_USAGE}`、`{RAM_PERCENT}`、`{TIME_ISO}`、NTP 校時……另有自訂變數（四則運算/串接/正規表示式/命令輸出）、依變數重新命名/覆寫/單位。
- 動態程序變數如 `CPU_USAGE_VRCHAT`、`MEM_USAGE_<名稱|PID>` 與 `USAGE_FILE_<路徑>`。

### 健康狀態

- 由靜息心率校準與閾值係數推導狀態（睡眠 / 靜息 / 活動 / 興奮），並回應 OSC 姿態參數（AFK / 坐姿 / 移動速度）。
- 以 `{HEALTH_STATUS}` 變數暴露，可直接用於推送模板。

### 記錄與匯出

- 心率、OSC 流量、健康狀態與硬體快照記錄至本機 SQLite，可選按日 JSONL/CSV 後端。
- 五類記錄（Avatar 變更、VRChat 工作階段、裝置連線、心率詳情、硬體快照），各類獨立保留天數。
- 統計頁（最小/平均/中位/最大/標準差、趨勢、直方圖、依裝置、OSC 位址 Top-N）支援區間選擇，可匯出 TXT/JSON/YAML/CSV。

### 遠端 Web 第二前端（LAN/WAN 分級 + HTTPS）

- 同一份 UI 經同一連接埠（預設 9460）提供給區域網路內的手機與平板。
- 來源分級存取：回環連線即本機管理員（無需登入）；區域網路來源需開啟遠端開關並登入本機帳號；公網來源額外需要 WAN 開關——後者要求 admin 強密碼與明確的風險確認對話框。
- 角色（admin/user）依區塊白名單授權、PBKDF2 密碼儲存、綁定 UA 的工作階段與閒置逾時、稽核日誌，以及基於憑證指紋的可選 HTTPS。

![遠端手機端](docs/images/remote-mobile.png)
<!-- IMAGE PLACEHOLDER: 手機上開啟的遠端 Web 前端——行動版版面的心率頁 -->

### CLI / TUI

- `hrmcli.exe`（與 `HeartRateMonitor.exe --cli` 等價）：供指令碼呼叫的一次性命令、純文字 REPL（`--shell`）、預設進入 TestDisk 風格選單 TUI。
- 約 40 條命令涵蓋裝置、OSC、推送模板、硬體變數、健康、記錄/匯出、Web/遠端、介面設定、懸浮視窗與日誌——與應用程式內主控台頁共用同一命令引擎。

### Toolkit 工具集

側欄左下角的 dock 開啟 VRChat 工具集：

- **config 編輯器**——VRChat `config.json` 常用欄位的表格化編輯，嚴格 JSON 型別校驗。
- **日誌瀏覽**——VRChat 日誌的列表/讀取/搜尋。
- **快取清理**——快取佔用分析與清理，含 dry-run 預覽與確認詞組。
- **相片索引**——相片庫平行索引與關鍵字搜尋（VRChat 截圖 XMP 中繼資料）。
- **遊戲分析**——遊戲時長/心率/硬體資料的彙整統計與圖表。
- **程序分析**——VRChat 程序 CPU/記憶體快照。

![Toolkit](docs/images/toolkit.png)
<!-- IMAGE PLACEHOLDER: Toolkit 頁——展開的 dock 選單與快取分析工具 -->

### 安全模式

- `--safemode`（或設定頁入口 / 三連 R 手勢）暫停一切自動化——自動連線、自動偵測、自動重連、OSC 推送、硬體採集——便於排障；啟動時常駐警示條，可一鍵正常重新啟動。

### 介面：十種語言與主題

- 介面支援十種語言：繁體中文 / 简体中文 / 繁體中文（香港） / 粵語（香港） / English / 日本語 / Español / 한국어 / Deutsch / Français；CLI 與日誌跟隨同一語言。
- 明暗模式 × 配色（預設 / 森林 / 落日 / 海洋 / 紫羅蘭 / **純色自訂模式**，自選強調色/背景/面板），圓角與密度滑桿、遵循系統主題、全域動畫開關。
- 版面偏好（卡片順序、欄寬、曲線參數……）本機儲存並鏡像至後端，重灌不遺失。

## 系統需求

- Windows 10 或 11，64 位元（x64）。
- Microsoft Edge WebView2 執行階段（多數系統已預裝；否則請安裝微軟 Evergreen 執行階段）。
- 支援低功耗藍牙的介面卡（內建或 USB 介面卡）。
- 可選：框架相依建置需 .NET 10 執行階段——standalone 建置自帶執行階段。

## 從 Built ZIP 使用

1. 從 [Releases](https://github.com/yzenwu/VRC-HRMonitor/releases) 下載最新的 `HeartRateMonitor-*-x64.zip` 並解壓縮至任意位置。
2. 執行 `HeartRateMonitor.exe`（或 `hrm-webui.exe`）：引擎進入系統匣，WebView2 視窗隨啟動畫面開啟。
3. 首次啟動會將組態寫入資料目錄 `%AppData%\HeartRateMonitor`（日誌、匯出與資料庫也在該目錄；可在 exe 旁放置 `data_location.txt` 重新導向）。
4. 開始掃描、連線 BLE 感測器、啟用 OSC 推送並進入 VRChat——聊天框開始刷新。
5. `hrmcli.exe` 是終端機等價物；`hrmdump.exe` 作為當機守護自動執行。
6. 手機遠端存取：開啟遠端開關（Web 頁），在同一網路下以手機開啟 `http://<PC-IP>:9460/webui/` 並登入本機帳號。

## 從原始碼建置

> 本儲存庫是專案的原始碼快照。

前置需求：

- **gcc（MinGW-w64）**——編譯 C OSC 引擎（`Engine/`）。
- **.NET 10 SDK**——發行四個 C# 可執行程式（`HeartRateMonitor.exe`、`hrm-webui.exe`、`hrmcli.exe`、`hrmdump.exe`）。
- **Node.js + npm**——建置 Vue 3 前端（`WebUI/`）。

於儲存庫根目錄建置（PowerShell）：

```powershell
./build.ps1                # 互動選擇：standalone / releases / debug
./build.ps1 --standalone   # 自包含單檔可執行程式
./build.ps1 --releases     # 框架相依發行 + ZIP
./build.ps1 --debug        # Debug 建置：主控台 + 詳細日誌
```

指令碼依序建置 C 引擎、Web 前端（vite）與四個 .NET 專案，產物輸出至 `Built/<分支>-<時間戳記>/`；release 建置額外產出附 SHA-256 校驗檔的 `HeartRateMonitor-*-x64.zip`。`Release.json` 是發行中繼資料單一來源（版本、圖示、儲存庫、建置時間、授權條款），建置時嵌入可執行程式。請勿平行執行兩個建置（共享中繼 `obj/` 目錄）。

## 授權條款

[MIT](LICENSE) — © Yzen Wu
