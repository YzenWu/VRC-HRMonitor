# HeartRateMonitor

專門畀 VRChat 用嘅實時 BLE 心率工具：用 OSC 將心率同硬件遙測推去聊天框——仲有懸浮視窗、遠端 Web 前端、CLI/TUI 同 VRChat 工具箱，一個 Windows 搞掂。

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [繁體中文（香港）](README.zh-HK.md) | **粵語（香港）** | [日本語](README.ja.md) | [Español](README.es.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Français](README.fr.md)

<img src="images/hero.png" alt="主介面總覽">

## 功能

### BLE 心率裝置

- 讀到任何標準低功耗藍牙心率裝置（Heart Rate Service `0x180D`）——胸帶、手環、運動手錶都得。
- **支援多個裝置**：一次過連幾個感應器，上限淨係睇藍牙協定同硬件。
- 有智能裝置評分排位、改花名、自動重連、訊號弱（RSSI）會出警告，仲可以每個裝置開獨立懸浮視窗。
- 自動偵測：按權重逐個連候選裝置，音響/智能家居同冇心率特徵嘅會自動跳過。

<img src="images/hrcurve.png" alt="心率曲線">

### VRChat OSC 聊天框推送同實時預覽

- 用自由 `{變數}` 模板，經 OSC/UDP 將「心率 + CPU / GPU / 記憶體等等」推去 VRChat 聊天框（`/chatbox/input`）。
- 改模板嗰陣每秒刷新實時預覽，有字數統計，接近聊天框 144 字上限會提示你（但唔會攔住唔畀你send）。
- 有自訂 OSC 發送（任意位址/文字）、Webhook 出去推送、OSC 接收（9001 port）睇 VRChat Avatar 參數，仲可以揀「一開就推送」。

<img src="images/pusher.png" alt="推送預覽">
<img src="images/hwinfo.png" alt="硬體訊息">

### 懸浮視窗

- 桌面置頂小工具顯示而家嘅 BPM（或者一張圖）；一個主窗 + 每個裝置各一窗。
- 可以鎖定兼點擊穿透、DPI 感知縮放，每個窗嘅位置大細各自記住。
- 每個窗揀資料來源（平均或者指定裝置）同刷新間隔，全部任你set。

<img src="images/overlay.png" alt="懸浮視窗" width="500">

### 硬件遙測變數

- 經登錄檔、WMI、PowerShell 同 `systeminfo` 收集 Windows 主機資料；實時指標（CPU/記憶體/GPU/顯存佔用、溫度、硬碟、記憶體認可）就用 PDH 攞，同工作管理員同一個來源。
- 樣樣都可以做模板變數：`{CPU_USAGE}`、`{RAM_PERCENT}`、`{TIME_ISO}`、NTP 對時……仲有自訂變數（加減乘除/駁字/正則/指令輸出），逐個變數改名/覆寫/加單位。
- 動態程序變數好似 `CPU_USAGE_VRCHAT`、`MEM_USAGE_<名|PID>` 同 `USAGE_FILE_<路徑>`。

### 健康狀態

- 用靜息心率校準同門檻系數推個狀態出嚟（瞓緊 / 靜息 / 活動 / 興奮），仲會睇 OSC 姿態參數（AFK / 坐緊 / 行走速度）。
- 變成 `{HEALTH_STATUS}` 變數，直接擺入推送模板用得。

### 記錄同匯出

- 心率、OSC 流量、健康狀態同硬件快照記落本機 SQLite，仲可以揀每日 JSONL/CSV。
- 五類記錄（Avatar 轉換、VRChat session、裝置連線、心率詳情、硬件快照），每類自己設定留幾多日。
- 統計頁（最小/平均/中位/最大/標準差、趨勢、直方圖、逐裝置、OSC 位址 Top-N）可以揀時間範範圍，匯出做 TXT/JSON/YAML/CSV。

### 遠端 Web 第二個前端（LAN/WAN 分級 + HTTPS）

- 同一份 UI 用同一個 port（預設 9460）開畀局域網入面嘅手機同平板。
- 來源分級：自己部機連就係本機管理員（唔使登入）；局域網嚟嘅要開遠端掣同登入本機帳號；公網嚟嘅仲要開 WAN 掣——開 WAN 就要求 admin 強密碼，同埋要喺風險確認視窗度明確噉按確認。
- 角色（admin/user）按區塊白名單授權、密碼用 PBKDF2 儲存、session 綁住 UA 兼有閒置過期、有稽核日誌，仲可以憑證書指紋開 HTTPS。

### CLI / TUI

- `hrmcli.exe`（同 `HeartRateMonitor.exe --cli` 一樣）：寫 script 用嘅一次性指令、純文字 REPL（`--shell`）、預設入 TestDisk 噉款嘅選單 TUI。
- 大約 40 條指令，裝置、OSC、推送模板、硬件變數、健康、記錄/匯出、Web/遠端、介面設定、懸浮視窗同日誌樣樣齊——同程式入面個 Console 頁用同一個指令引擎。

### Toolkit 工具箱

側欄左下角有個 dock，一撳就開 VRChat 工具箱：

- **config 編輯器**——VRChat `config.json` 成日用嘅欄位用表格改，JSON 類型查得好嚴。
- **日誌瀏覽**——VRChat 個日誌可以列表/睇內容/搜尋。
- **快取清理**——睇快取食咗幾多位同埋清佢，有 dry-run 預覽同要打確認字先肯刪。
- **相片索引**——相簿平行做索引，之後用關鍵字搵相（VRChat 截圖嘅 XMP 資料）。
- **遊戲分析**——打機時長/心率/硬件數據夾埋做統計同圖表。
- **程序分析**——VRChat 個 process 嘅 CPU/記憶體快照。

<img src="images/toolkit.png" alt="Toolkit" width="400">

### 安全模式

- `--safemode`（或者設定頁入面嘅掣 / 連撳三下 R）會暫停晒所有自動嘢——自動連線、自動偵測、自動重連、OSC 推送、硬件收集——方便你查問題；開咗之後頂度有條常駐警示，一撳就正常重啟。

### 介面：十種語言同主題

- 介面有十種語言：繁體中文 / 简体中文 / 繁體中文（香港） / 粵語（香港） / English / 日本語 / Español / 한국어 / Deutsch / Français；CLI 同日誌跟同一個語言。
- 明暗模式 × 配色（預設 / 森林 / 落日 / 海洋 / 紫羅蘭 / **純色自訂模式**，自己揀強調色/背景/面板），圓角同密度有得較、跟系統主題、全局動畫有得閂。
- 版面偏好（卡片次序、欄闊、曲線參數……）本地儲存之餘仲會鏡像去後端，重裝都唔驚冇咗。

## 系統需求

- Windows 10 或者 11，64 位（x64）。
- Microsoft Edge WebView2 Runtime（大部分機已經預經預裝；冇就裝返個微軟 Evergreen Runtime）。
- 一張支援低功耗藍牙嘅卡（機內置或者 USB 嘅都得）。
- 可揀：framework-dependent 版要 .NET 10 runtime——standalone 版自己帶埋。

## 點樣用 Built ZIP

1. 去 [Releases](https://github.com/yzenwu/VRC-HRMonitor/releases) 下載最新嘅 `HeartRateMonitor-*-x64.zip`，解壓去邊度都得。
2. 行 `HeartRateMonitor.exe`（或者 `hrm-webui.exe`）：引擎會入系統匣，WebView2 視窗就跟住開。
3. 第一次開會將設定寫入資料目錄 `%AppData%\HeartRateMonitor`（日誌、匯出同資料庫都喺嗰度；想搬就喺 exe 隔籬放個 `data_location.txt` 指去第二度）。
4. 開始掃描、連上你嘅 BLE 感應器、開 OSC 推送，然後入 VRChat——個聊天框就開始跳。
5. `hrmcli.exe` 係終端機版；`hrmdump.exe` 會自動做 crash 守護。
6. 想用手機遙控：開咗遠端掣（Web 頁）之後，同一個網絡用手機開 `http://<PC-IP>:9460/webui/`，登入本機帳號就用到。

## 點樣從原始碼 build

> 呢個 repo 係項目嘅原始碼快照。

要先裝：

- **gcc（MinGW-w64）**——編譯 C OSC 引擎（`Engine/`）。
- **.NET 10 SDK**——publish 四個 C# 執行檔（`HeartRateMonitor.exe`、`hrm-webui.exe`、`hrmcli.exe`、`hrmdump.exe`）。
- **Node.js + npm**——build Vue 3 前端（`WebUI/`）。

喺 repo 根目錄 build（PowerShell）：

```powershell
./build.ps1 --releases     # framework-dependent 發佈 + ZIP
./build.ps1 --debug        # Debug 版：有 console 同詳細日誌
```

個 script 會順住編 C 引擎、Web 前端（vite）同四個 .NET project，產物去 `Built/<分支>-<時間戳>/`；release 版仲會整埋個 `HeartRateMonitor-*-x64.zip` 附加 SHA-256 核對檔。`Release.json` 係發佈資料嘅唯一來源（版本、圖示、repo、build 時間、授權），build 嗰陣會嵌入個執行檔度。唔好同時開兩個 build（佢哋共用 `obj/` 中繼目錄）。

## 授權

[MIT](LICENSE) — © Yzen Wu.

---

## AIGC Context
  
**呢個項目嘅大部分內容由 ChatGPT 同 Claude Opus 生成。如果你有任何問題或者建議，請喺個 repo 開 issue 或者提交 PR。**
