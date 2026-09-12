# HeartRateMonitor

Echtzeit-BLE-Herzfrequenz für VRChat: Überträgt Puls und Hardware-Telemetrie per OSC in die ChatBox — mit schwebenden Fenstern, einem Remote-Web-Frontend, CLI/TUI und einem VRChat-Toolkit, alles in einer Windows-Anwendung.

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [繁體中文（香港）](README.zh-HK.md) | [粵語（香港）](README.yue-HK.md) | [日本語](README.ja.md) | [Español](README.es.md) | [한국어](README.ko.md) | **Deutsch** | [Français](README.fr.md)

<img src="images/hero.png" alt="Hauptfenster-Übersicht">

## Funktionen

### BLE-Herzfrequenzsensoren

- Liest alle standardkonformen Bluetooth-Low-Energy-Herzfrequenzgeräte (Heart Rate Service `0x180D`) — Brustgurte, Smartbands, Sportuhren.
- **Multigeräteunterstützung**: mehrere Sensoren gleichzeitig verbinden; das Limit bestimmen nur der Bluetooth-Stack und die Hardware.
- Intelligente Gerätebewertung und -sortierung, Aliasnamen, automatische Wiederverbindung, Warnungen bei schwachem Signal (RSSI) und ein eigenes schwebendes Fenster pro Gerät.
- Autoerkennung: verbindet Kandidatengeräte gewichtsbasiert in Serie und überspringt Audio-/Smart-Home-Geräte sowie Geräte ohne Herzfrequenz-Characteristic.

<img src="images/hrcurve.png" alt="Pulskurve">

### VRChat-OSC-ChatBox-Versand mit Live-Vorschau

- Überträgt „Puls + CPU / GPU / RAM und mehr“ per OSC/UDP mit einer freien `{Variable}`-Vorlage in die VRChat-ChatBox (`/chatbox/input`).
- Live-Vorschau der Vorlage, die beim Bearbeiten jede Sekunde aktualisiert wird — mit Zeichenzähler und nicht blockierendem Hinweis nahe dem 144-Zeichen-Limit der ChatBox.
- Benutzerdefinierter OSC-Versand (beliebige Adresse/Text), ausgehende Webhook-Pushes, OSC-Empfänger (Port 9001) zum Mitschneiden des Avatar-Parameter-Traffic von VRChat sowie die Option „Beim Start sofort senden“.

<img src="images/pusher.png" alt="Versand-Vorschau">

### Schwebende Fenster

- Always-on-Top-Desktop-Widgets, die den aktuellen BPM-Wert (oder ein Bild) anzeigen; ein Hauptfenster plus eines pro Gerät.
- Sperrbar mit Klick-Durchreichen, DPI-bewusster Größenänderung und unabhängig gespeicherter Fenstergeometrie.
- Datenquelle (Durchschnitt oder bestimmtes Gerät) und Aktualisierungsintervall sind pro Fenster einstellbar.

<img src="images/overlay.png" alt="Schwebendes Fenster" width="500">

### Hardware-Telemetrie-Variablen

- Sammelt Windows-Hostinformationen über Registry, WMI, PowerShell und `systeminfo`; Echtzeitkennzahlen (CPU/RAM/GPU/VRAM-Auslastung, Temperaturen, Datenträger, Memory Commit) stammen aus PDH — derselben Quelle wie der Task-Manager.
- Alles wird zur Vorlagenvariable: `{CPU_USAGE}`, `{RAM_PERCENT}`, `{TIME_ISO}`, NTP-synchronisierte Zeit… dazu eigene Variablen (Arithmetik, Verkettung, Regex, Kommandoausgabe) sowie Umbenennen/Überschreiben/Einheit pro Variable.
- Dynamische Prozessvariablen wie `CPU_USAGE_VRCHAT`, `MEM_USAGE_<Name|PID>` und `USAGE_FILE_<Pfad>`.

### Gesundheitsstatus

- Leitet aus der Kalibrierung der Ruheherzfrequenz und Schwellenfaktoren einen Status ab (Schlafen / Ruhe / Aktiv / Erregt) und reagiert auch auf OSC-Pose-Parameter (AFK / Sitzen / Geschwindigkeit).
- Wird als Variable `{HEALTH_STATUS}` bereitgestellt und kann direkt in Push-Vorlagen verwendet werden.

### Aufzeichnung und Export

- Zeichnet Puls, OSC-Traffic, Gesundheitsstatus und Hardware-Snapshots in einer lokalen SQLite-Datenbank auf, mit optionalen täglichen JSONL/CSV-Backends.
- Fünf Aufzeichnungskategorien (Avatarwechsel, VRChat-Sitzungen, Gerätverbindungen, Pulsdetails, Hardware-Snapshots) mit eigenem Aufbewahrungszeitraum pro Kategorie.
- Statistikseite (Min/Durchschnitt/Median/Max/Standardabweichung, Trend, Histogramm, pro Gerät, OSC-Adressen Top-N) mit wählbaren Zeiträumen; Export nach TXT/JSON/YAML/CSV.

### Remote-Web-Zweitfrontend (LAN/WAN-Stufen + HTTPS)

- Dieselbe Benutzeroberfläche wird auf demselben einzelnen Port (standardmäßig 9460) für Smartphones und Tablets im Netzwerk bereitgestellt.
- Zugriffsstufen nach Quelle: Loopback-Verbindungen sind der lokale Administrator (ohne Anmeldung); LAN-Quellen erfordern den Remote-Schalter und ein lokales Konto; öffentliche/WAN-Quellen zusätzlich den WAN-Schalter — dieser verlangt ein starkes Admin-Passwort und einen ausdrücklichen Risikobestätigungsdialog.
- Rollen (Admin/Benutzer) mit Whitelists pro Bereich, PBKDF2-Passwortspeicherung, UA-gebundene Sitzungen mit Leerlauf-Ablauf, Audit-Protokoll und optionales HTTPS per Zertifikats-Fingerprint.

### CLI / TUI

- `hrmcli.exe` (identisch mit `HeartRateMonitor.exe --cli`): Einmalbefehle für Skripte, ein einfaches REPL (`--shell`) und standardmäßig ein TestDisk-artiges Menü-TUI.
- Rund 40 Befehle für Geräte, OSC, Push-Vorlagen, Hardware-Variablen, Gesundheit, Aufzeichnung/Export, Web/Remote, UI-Einstellungen, schwebende Fenster und Logs — dieselbe Befehls-Engine wie der Konsolen-Tab der App.

### Toolkit

Ein Dock unten links in der Seitenleiste öffnet das VRChat-Toolkit:

- **Config-Editor** — tabellarische Bearbeitung gängiger VRChat-`config.json`-Felder mit strenger JSON-Typprüfung.
- **Log-Browser** — VRChat-Protokolle auflisten, lesen und durchsuchen.
- **Cache-Bereinigung** — Analyse des Cache-Verbrauchs und Bereinigung mit Dry-Run-Vorschau und Bestätigungsphrase.
- **Foto-Index** — parallele Indexierung der Fotobibliothek und Stichwortsuche (XMP-Metadaten von VRChat-Screenshots).
- **Spiel-Statistiken** — aggregierte Spielzeit-/Puls-/Hardware-Statistiken mit Diagrammen.
- **Prozessanalyse** — CPU-/Speicher-Snapshots des VRChat-Prozesses.

<img src="images/toolkit.png" alt="Toolkit" width="400">

### Abgesicherter Modus

- `--safemode` (oder der Eintrag in den Einstellungen / die Dreifach-R-Geste) pausiert die gesamte Automatik — Autoverbindung, Autoerkennung, automatische Wiederverbindung, OSC-Versand, Hardware-Erfassung — für die Fehlersuche; mit dauerhaft sichtbarem Banner und Ein-Klick-Neustart im Normalmodus.

### Oberfläche: zehn Sprachen und Themes

- Oberfläche in zehn Sprachen: 繁體中文 / 简体中文 / 繁體中文（香港） / 粵語（香港） / English / 日本語 / Español / 한국어 / Deutsch / Français; CLI und Logs folgen derselben Sprache.
- Hell-/Dunkelmodus × Farbpaletten (Standard / Forest / Sunset / Ocean / Violet / **eigener Unifarben-Modus** mit frei wählbarer Akzent-, Hintergrund- und Panelfarbe), Schieberegler für Eckenradius und Dichte, Systemtheme-Folge und globaler Animations-Schalter.
- Layout-Einstellungen (Kartenreihenfolge, Spaltenbreiten, Kurvenparameter …) werden lokal gespeichert und zusätzlich zum Backend gespiegelt — sie überstehen Neuinstallationen.

## Systemvoraussetzungen

- Windows 10 oder 11, 64-Bit (x64).
- Microsoft Edge WebView2 Runtime (auf den meisten Systemen vorinstalliert; andernfalls die Evergreen-Runtime von Microsoft installieren).
- Ein Bluetooth-Adapter mit BLE-Unterstützung (intern oder USB-Stick).
- Optional: Für den frameworkabhängigen Build wird die .NET 10-Runtime benötigt — der Standalone-Build bringt sie selbst mit.

## Verwendung aus einem fertigen ZIP

1. Das neueste `HeartRateMonitor-*-x64.zip` von [Releases](https://github.com/yzenwu/VRC-HRMonitor/releases) herunterladen und an einem beliebigen Ort entpacken.
2. `HeartRateMonitor.exe` (oder `hrm-webui.exe`) starten: Die Engine legt sich in den System-Tray, und das WebView2-Fenster öffnet sich mit einem Splash-Screen.
3. Beim ersten Start wird die Konfiguration ins Datenverzeichnis `%AppData%\HeartRateMonitor` geschrieben (Logs, Exporte und die Datenbank leben ebenfalls dort; der Ort lässt sich per `data_location.txt` neben der ausführbaren Datei umleiten).
4. Einen Scan starten, den BLE-Sensor verbinden, den OSC-Versand aktivieren und VRChat betreten — die ChatBox beginnt sich zu aktualisieren.
5. `hrmcli.exe` ist das Terminal-Gegenstück; `hrmdump.exe` läuft automatisch als Absturz-Wächter.
6. Fernzugriff vom Smartphone: den Remote-Schalter (Tab „Web“) aktivieren, dann im selben Netzwerk `http://<PC-IP>:9460/webui/` öffnen und mit einem lokalen Konto anmelden.

## Aus dem Quellcode bauen

> Dieses Repository ist ein Quellcode-Snapshot des Projekts.

Voraussetzungen:

- **gcc (MinGW-w64)** — kompiliert die C-OSC-Engine (`Engine/`).
- **.NET 10 SDK** — veröffentlicht die vier C#-Programme (`HeartRateMonitor.exe`, `hrm-webui.exe`, `hrmcli.exe`, `hrmdump.exe`).
- **Node.js + npm** — baut das Vue-3-Frontend (`WebUI/`).

Build vom Repository-Stammverzeichnis (PowerShell):

```powershell
./build.ps1 --releases     # frameworkabhängiges Release + ZIP
./build.ps1 --debug        # Debug-Build mit Konsole und ausführlichen Logs
```

Das Skript baut nacheinander die C-Engine, das Web-Frontend (Vite) und die vier .NET-Projekte und schreibt alles nach `Built/<Zweig>-<Zeitstempel>/`; Release-Builds erzeugen zusätzlich ein `HeartRateMonitor-*-x64.zip` mit SHA-256-Beigabe. `Release.json` ist die einzige Quelle der Release-Metadaten (Versionen, Icons, Repository, Build-Zeit, Lizenz) und wird beim Bau in die Programme eingebettet. Nicht zwei Builds parallel ausführen (sie teilen sich die `obj/`-Zwischenverzeichnisse).

## Lizenz

[MIT](LICENSE) — © Yzen Wu.

---

## AIGC Context
  
**Der Großteil des Projektinhalts wurde von ChatGPT und Claude Opus generiert. Bei Fragen oder Vorschlägen bitte ein Issue im Repository öffnen oder einen PR einreichen.**
