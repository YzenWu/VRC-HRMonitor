# HeartRateMonitor

Real-time BLE heart rate for VRChat: push BPM and hardware telemetry to the ChatBox over OSC — with floating windows, a remote web frontend, a CLI/TUI and a VRChat toolkit, in one Windows app.

**English** | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [繁體中文（香港）](README.zh-HK.md) | [粵語（香港）](README.yue-HK.md) | [日本語](README.ja.md) | [Español](README.es.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Français](README.fr.md)

<img src="images/hero.png" alt="Hero overview">


## Features

### BLE heart-rate devices

- Reads any standard Bluetooth Low Energy heart-rate device (Heart Rate Service `0x180D`) — chest straps, smart bands, sports watches.
- **Multi-device support**: connect several sensors at once; the limit is only the Bluetooth stack and hardware.
- Smart device scoring and ranking, aliases, auto-reconnect, weak-signal (RSSI) warnings, per-device floating windows.
- Auto-detect: batch-connects candidate devices by weight, skipping audio/smart-home devices and those without a heart-rate characteristic.

<img src="images/hrcurve.png" alt="Heartbeat curve">


### VRChat OSC ChatBox push & live preview

- Pushes "heart rate + CPU / GPU / RAM and more" to the VRChat ChatBox (`/chatbox/input`) over OSC/UDP using a free-form `{variable}` template.
- Live template preview rendered once per second while you edit, with a character counter and a non-blocking warning near the 144-character ChatBox limit.
- Custom OSC send (any address/text), Webhook outbound push, OSC receiver (port 9001) to capture VRChat avatar parameter traffic, and optional "start pushing on launch".

<img src="images/pusher.png" alt="Pusher">

### Floating windows (Overlay)

- Always-on-top desktop widgets showing the current BPM (or an image); one main window plus one window per device.
- Lockable with click-through, DPI-aware resizing, per-window geometry persisted independently.
- Per-window data source (average or a specific device) and refresh interval.

<img src="images/overlay.png" alt="Overlay" width="500">

### Hardware telemetry variables

- Collects Windows host info via registry, WMI, PowerShell and `systeminfo`; real-time metrics (CPU/RAM/GPU/VRAM load, temperatures, disk, memory commit) via PDH, same source as Task Manager.
- Everything becomes a template variable: `{CPU_USAGE}`, `{RAM_PERCENT}`, `{TIME_ISO}`, NTP-synced time, … plus custom variables (arithmetic, concat, regex, command output), per-variable rename/override/unit.
- Dynamic process variables such as `CPU_USAGE_VRCHAT`, `MEM_USAGE_<name|PID>` and `USAGE_FILE_<path>`.

### Health status

- Derives a status (Sleep / Rest / Active / Excited) from resting-rate calibration and threshold factors, also reacting to OSC pose parameters (AFK / Seated / Velocity).
- Exposed as the `{HEALTH_STATUS}` variable and usable in push templates.

### Recording & export

- Records heart rate, OSC traffic, health status and hardware snapshots to a local SQLite database, with optional daily JSONL/CSV backends.
- Five recording categories (avatar changes, VRChat sessions, device connections, heart-rate details, hardware snapshots) with per-category retention.
- Statistics page (min/avg/median/max/std-dev, trend, histogram, per-device, OSC address Top-N) over selectable ranges; export to TXT/JSON/YAML/CSV.

### Remote web second frontend (LAN/WAN tiers + HTTPS)

- The same UI served on the same single port (default 9460) for phones and tablets on your network.
- Source-tiered access: loopback connections are the local administrator (no login); LAN sources require the Remote switch and a local account; public/WAN sources additionally require the WAN switch — which demands a strong admin password and an explicit risk-confirmation dialog.
- Roles (admin/user) with per-section whitelists, PBKDF2 password storage, UA-bound sessions with idle expiry, audit logging, and optional HTTPS via certificate thumbprint.  

### CLI / TUI

- `hrmcli.exe` (equivalent to `HeartRateMonitor.exe --cli`): one-shot commands for scripting, a plain REPL (`--shell`), and a TestDisk-style menu TUI by default.
- ~40 commands covering devices, OSC, pusher templates, hardware variables, health, recording/export, web/remote, UI settings, floating windows and logs — the same command engine as the in-app Console tab.

### Toolkit

A dock in the bottom-left of the sidebar opens the VRChat toolkit:

- **Config editor** — table editor for common VRChat `config.json` fields with strict JSON type validation.
- **Log browser** — list, read and search VRChat logs.
- **Cache cleaner** — cache usage analysis and cleanup with dry-run preview and a confirm phrase.
- **Photo index** — parallel photo library indexing and keyword search (VRChat screenshot XMP metadata).
- **Game stats** — aggregated playtime / heart-rate / hardware statistics with charts.
- **Process analysis** — VRChat process CPU/memory snapshots.

<img src="images/toolkit.png" alt="Toolkit" width="400">

### Safe mode

- `--safemode` (or the Settings entry / triple-R gesture) pauses all automation — auto-connect, auto-detect, auto-reconnect, OSC push, hardware collection — for troubleshooting, with a persistent banner and one-click normal restart.

### UI: ten languages & theming

- Interface in ten languages: 繁體中文 / 简体中文 / 繁體中文（香港） / 粵語（香港） / English / 日本語 / Español / 한국어 / Deutsch / Français; CLI and logs follow the same language.
- Light/dark mode × color palettes (default / forest / sunset / ocean / violet / **custom solid-color mode** with your own accent, background and panel colors), corner radius and density sliders, follow-system theme, optional global animation switch.
- Layout preferences (card order, column widths, curve settings…) are stored locally and mirrored to the backend, surviving reinstalls.

## System Requirements

- Windows 10 or 11, 64-bit (x64).
- Microsoft Edge WebView2 Runtime (preinstalled on most systems; otherwise install the Evergreen Runtime from Microsoft).
- A Bluetooth adapter with BLE support (built-in or USB dongle).
- Optional: .NET 10 runtime for the framework-dependent build — the standalone build is self-contained.

## Usage from a Built ZIP

1. Download the latest `HeartRateMonitor-*-x64.zip` from [Releases](https://github.com/yzenwu/VRC-HRMonitor/releases) and extract it anywhere.
2. Run `HeartRateMonitor.exe` (or `hrm-webui.exe`): the engine starts into the system tray and the WebView2 window opens with a splash screen.
3. On first start, configuration is written to the data directory `%AppData%\HeartRateMonitor` (logs, exports and the database live there too; the location can be redirected via a `data_location.txt` file next to the executable).
4. Start a scan, connect your BLE sensor, enable the OSC pusher and launch VRChat — the ChatBox starts updating.
5. `hrmcli.exe` is the terminal counterpart; `hrmdump.exe` runs automatically as the crash watchdog.
6. For remote access from a phone: enable the Remote switch (Web tab), then open `http://<PC-IP>:9460/webui/` on the same network and sign in with a local account.

## Building from Source

> This repository is a source snapshot of the project.

Prerequisites:

- **gcc (MinGW-w64)** — compiles the C OSC engine (`Engine/`).
- **.NET 10 SDK** — publishes the four C# executables (`HeartRateMonitor.exe`, `hrm-webui.exe`, `hrmcli.exe`, `hrmdump.exe`).
- **Node.js + npm** — builds the Vue 3 frontend (`WebUI/`).

Build from the repository root (PowerShell):

```powershell
./build.ps1 --releases     # framework-dependent release + ZIP
./build.ps1 --debug        # debug build with console and verbose logs
```

The script builds the C engine, then the web frontend (vite), then the four .NET projects, and writes everything to `Built/<branch>-<timestamp>/`; release builds additionally produce a `HeartRateMonitor-*-x64.zip` with a SHA-256 sidecar. `Release.json` is the single source of release metadata (versions, icons, repository, build time, license) and is embedded into the executables at build time. Do not run two builds in parallel (they share intermediate `obj/` directories).

## License

[MIT](LICENSE) — © Yzen Wu.

---

## AIGC Context
  
**Most Context of The project is Generated by ChatGPT & Claude Opus. If you have any questions or suggestions, please open an issue or submit PR in the repository.**