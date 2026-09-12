# HeartRateMonitor

面向 VRChat 嘅即時 BLE 心率工具：透過 OSC 將心率同硬件遙測推送至聊天框——附設懸浮視窗、遠端 Web 前端、CLI/TUI 同 VRChat 工具箱嘅單體 Windows 應用程式。

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | **繁體中文（香港）** | [粵語（香港）](README.yue-HK.md) | [日本語](README.ja.md) | [Español](README.es.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Français](README.fr.md)

<!-- BADGES PLACEHOLDER: 喺度插入 release / license / platform 徽章（shields.io） -->

![主介面總覽](docs/images/hero.png)
<!-- IMAGE PLACEHOLDER: 主視窗總覽——側欄、心率頁、底欄裝置膠囊 -->

## 功能特性

### BLE 心率裝置

- 讀取任何標準低功耗藍牙心率裝置（Heart Rate Service `0x180D`）——胸帶、手環、運動手錶。
- **多裝置支援**：同時連線多個感應器，上限只受藍牙協定棧同硬件限制。
- 智能裝置評分排序、別名、自動重連、弱訊號（RSSI）警示、每個裝置獨立懸浮視窗。
- 自動偵測：按權重批次連線候選裝置，自動跳過音訊/智能家居裝置同冇心率特徵嘅裝置。

![心率曲線](docs/images/heartbeat.png)
<!-- IMAGE PLACEHOLDER: 心率頁——大 BPM 讀數、即時曲線（主顯示/平均/每裝置）、裝置列表 -->

### VRChat OSC 聊天框推送同即時預覽

- 用自由 `{變數}` 模板經 OSC/UDP 將「心率 + CPU / GPU / 記憶體等」推送至 VRChat 聊天框（`/chatbox/input`）。
- 編輯時每秒刷新嘅模板即時預覽，附字數統計，接近聊天框 144 字上限時會提示（唔會攔截傳送）。
- 自訂 OSC 傳送（任意位址/文字）、Webhook 外送推送、OSC 接收（9001 埠）擷取 VRChat Avatar 參數流量、可揀「啟動即推送」。

![推送預覽](docs/images/pusher.png)
<!-- IMAGE PLACEHOLDER: 推送頁——OSC 模板編輯器同即時預覽、字數統計 -->

### 懸浮視窗

- 置頂桌面小工具顯示目前 BPM（或圖片）；一個主窗 + 每裝置一窗。
- 可鎖定並且點擊穿透、DPI 感知縮放、每窗幾何獨立保存。
- 每窗資料來源（平均或指定裝置）同刷新間隔都可以設定。

![懸浮視窗](docs/images/float-window.png)
<!-- IMAGE PLACEHOLDER: 懸浮喺遊戲/桌面上面嘅懸浮視窗——主窗同裝置窗 -->

### 硬件遙測變數

- 經登錄檔、WMI、PowerShell 同 `systeminfo` 收集 Windows 主機資訊；經 PDH 攞即時指標（CPU/記憶體/GPU/顯存佔用、溫度、磁碟、記憶體認可），同工作管理員同一來源。
- 全部都可以做模板變數：`{CPU_USAGE}`、`{RAM_PERCENT}`、`{TIME_ISO}`、NTP 校時……仲有自訂變數（四則運算/串接/正則表示式/命令輸出）、每個變數改名/覆寫/單位。
- 動態程序變數例如 `CPU_USAGE_VRCHAT`、`MEM_USAGE_<名稱|PID>` 同 `USAGE_FILE_<路徑>`。

### 健康狀態

- 由靜息心率校準同閾值係數推導狀態（睡眠 / 靜息 / 活動 / 興奮），並且回應 OSC 姿態參數（AFK / 坐姿 / 移動速度）。
- 以 `{HEALTH_STATUS}` 變數暴露，可以直接喺推送模板用。

### 記錄同匯出

- 心率、OSC 流量、健康狀態同硬件快照記錄去本機 SQLite，可揀按日 JSONL/CSV 後端。
- 五類記錄（Avatar 變更、VRChat 工作階段、裝置連線、心率詳情、硬件快照），每類獨立保留日數。
- 統計頁（最小/平均/中位/最大/標準差、趨勢、直方圖、每裝置、OSC 位址 Top-N）支援區間選擇，可匯出 TXT/JSON/YAML/CSV。

### 遠端 Web 第二前端（LAN/WAN 分級 + HTTPS）

- 同一份 UI 經同一個埠（預設 9460）提供畀區域網絡入面嘅手機同平板。
- 來源分級存取：回環連線就係本機管理員（唔使登入）；區域網絡來源要開遠端掣同登入本機帳號；公網來源額外要 WAN 掣——後者要求 admin 強密碼同明確嘅風險確認對話框。
- 角色（admin/user）按區塊白名單授權、PBKDF2 密碼儲存、綁定 UA 嘅工作階段同閒置逾時、稽核日誌，仲有基於證書指紋嘅可揀 HTTPS。

![遠端手機端](docs/images/remote-mobile.png)
<!-- IMAGE PLACEHOLDER: 手機開啟嘅遠端 Web 前端——流動版版面嘅心率頁 -->

### CLI / TUI

- `hrmcli.exe`（同 `HeartRateMonitor.exe --cli` 等價）：畀 script 用嘅一次性命令、純文字 REPL（`--shell`）、預設入 TestDisk 風格選單 TUI。
- 約 40 條命令涵蓋裝置、OSC、推送模板、硬件變數、健康、記錄/匯出、Web/遠端、介面設定、懸浮視窗同日誌——同程式入面嘅主控台頁共用同一命令引擎。

### Toolkit 工具箱

側欄左下角嘅 dock 開啟 VRChat 工具箱：

- **config 編輯器**——VRChat `config.json` 常用欄位嘅表格化編輯，嚴格 JSON 類型校驗。
- **日誌瀏覽**——VRChat 日誌嘅列表/讀取/搜尋。
- **快取清理**——快取佔用分析同清理，有 dry-run 預覽同確認字句。
- **相片索引**——相片庫平行索引同關鍵字搜尋（VRChat 截圖 XMP 中繼資料）。
- **遊戲分析**——遊戲時長/心率/硬件數據嘅匯整統計同圖表。
- **程序分析**——VRChat 程序 CPU/記憶體快照。

![Toolkit](docs/images/toolkit.png)
<!-- IMAGE PLACEHOLDER: Toolkit 頁——展開嘅 dock 選單同快取分析工具 -->

### 安全模式

- `--safemode`（或者設定頁入口 / 三連 R 手勢）暫停一切自動化——自動連線、自動偵測、自動重連、OSC 推送、硬件收集——方便排障；啟用時常駐警示條，可以一鍵正常重新啟動。

### 介面：十種語言同主題

- 介面支援十種語言：繁體中文 / 简体中文 / 繁體中文（香港） / 粵語（香港） / English / 日本語 / Español / 한국어 / Deutsch / Français；CLI 同日誌跟同一語言。
- 明暗模式 × 配色（預設 / 森林 / 落日 / 海洋 / 紫羅蘭 / **純色自訂模式**，自揀強調色/背景/面板），圓角同密度滑桿、跟隨系統主題、全域動畫開關。
- 版面偏好（卡片次序、欄寬、曲線參數……）本機儲存並且鏡像去後端，重裝都唔會跌。

## 系統需求

- Windows 10 或 11，64 位元（x64）。
- Microsoft Edge WebView2 Runtime（大部分系統已預預裝；冇嘅話請安裝微軟 Evergreen Runtime）。
- 支援低功耗藍牙嘅介面卡（內置或 USB dongle）。
- 可揀：framework-dependent build 要 .NET 10 runtime——standalone build 自帶 runtime。

## 從 Built ZIP 使用

1. 喺 [Releases](https://github.com/yzenwu/VRC-HRMonitor/releases) 下載最新嘅 `HeartRateMonitor-*-x64.zip`，解壓去任何位置。
2. 行 `HeartRateMonitor.exe`（或 `hrm-webui.exe`）：引擎入系統匣，WebView2 視窗跟住啟動畫面開。
3. 第一次啟動會將設定寫入資料目錄 `%AppData%\HeartRateMonitor`（日誌、匯出同資料庫都喺嗰度；可以喺 exe 旁邊放 `data_location.txt` 重新導向）。
4. 開始掃描、連線 BLE 感應器、啟用 OSC 推送然後入 VRChat——聊天框開始刷新。
5. `hrmcli.exe` 係終端機版本；`hrmdump.exe` 會自動做當機守護。
6. 手機遠端存取：開啟遠端掣（Web 頁），同一網絡用手機開 `http://<PC-IP>:9460/webui/` 再登入本機帳號。

## 從原始碼建置

> 呢個 repository 係專案嘅原始碼快照。

前置需求：

- **gcc（MinGW-w64）**——編譯 C OSC 引擎（`Engine/`）。
- **.NET 10 SDK**——發佈四個 C# 執行檔（`HeartRateMonitor.exe`、`hrm-webui.exe`、`hrmcli.exe`、`hrmdump.exe`）。
- **Node.js + npm**——建置 Vue 3 前端（`WebUI/`）。

喺 repository 根目錄建置（PowerShell）：

```powershell
./build.ps1                # 互動揀：standalone / releases / debug
./build.ps1 --standalone   # 自包含單檔執行檔
./build.ps1 --releases     # framework-dependent 發佈 + ZIP
./build.ps1 --debug        # Debug build：主控台 + 詳細日誌
```

script 會依次建置 C 引擎、Web 前端（vite）同四個 .NET 專案，產物輸出至 `Built/<分支>-<時間戳>/`；release build 額外產出附 SHA-256 校驗檔嘅 `HeartRateMonitor-*-x64.zip`。`Release.json` 係發佈中繼資料單一來源（版本、圖示、repository、建置時間、授權），建置時嵌入執行檔。唔好同時行兩個 build（共享中繼 `obj/` 目錄）。

## 授權條款

[MIT](LICENSE) — © Yzen Wu
