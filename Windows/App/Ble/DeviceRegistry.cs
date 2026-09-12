using System.Collections.Concurrent;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.Ble;

/// <summary>
/// Device registry: identifier caching, UI refresh throttling, name-priority and signal-strength sorting,
/// aliases, device history, automatic reconnection, and RSSI attenuation warnings.
/// BleManager handles only BLE interactions; this class contains all Devices tab policies.
/// </summary>
public sealed class DeviceRegistry
{
    public sealed class Entry
    {
        public string Mac = "";
        /// <summary>Most recent non-empty advertised name, cached for use when later advertisements have no name.</summary>
        public string Name = "";
        public string Type = "BLE Device";
        /// <summary>Most recent valid advertised signal strength in dBm; meaningful only when HasRssi is true.</summary>
        public int Rssi;
        /// <summary>Whether a valid RSSI has ever been received; WinRT's -127 sentinel for an unavailable value does not count.</summary>
        public bool HasRssi;
        /// <summary>Time of the most recent valid RSSI, as a tick count.</summary>
        public long RssiAt;
        public long FirstSeen;
        public long LastSeen;
        /// <summary>Time of the last report sent to the frontend, used for throttling.</summary>
        public long LastPush;
        /// <summary>Heart rate report count and most recent report time, used to calculate the report frequency.</summary>
        public int Reports;
        public long LastReport;
        /// <summary>Advertisement frequency in Hz: the device's advertisement rate, calculated by Observe from an EMA of advertisement intervals.</summary>
        public double AdvHz;
        /// <summary>Report frequency in Hz: the rate of heart rate notifications from a connected device, calculated by OnReport from an EMA of notification intervals.</summary>
        public double NotifyHz;
        public long LastWeakWarn;
        /// <summary>GATT has been probed but has no heart rate characteristic; auto-detection skips these devices to avoid repeated connection attempts.</summary>
        public bool NoHrChar;
    }

    /// <summary>
    /// WinRT's sentinel value when signal strength is unavailable.
    /// Once a device connects, Windows stops delivering its advertisements and RawSignalStrengthInDBm remains fixed at this value.
    /// Therefore, values at or below this threshold are treated as unavailable and are not stored in Entry.Rssi.
    /// </summary>
    public const int RssiUnavailable = -127;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _reconnecting = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Wearable and heart rate strap brand/model characteristics add weight.
    /// A match indicates a portable device likely to report heart rate.
    /// </summary>
    /// <summary>
    /// Wearable-device keywords add weight.
    /// These terms almost never occur in smart-home or audio device names, so they have high precision.
    /// </summary>
    private static readonly string[] Preferred =
    {
        // ===== Heart rate / fitness =====
        "hrm", "hrs", "heartrate", "heart-rate", "heart rate",
        "chest strap", "chest-strap", "strap", "hr strap",
        "h10", "h9", "h7",                    // Polar chest strap models
        "tickr", "tickr x",                   // Wahoo heart rate straps
        "hrm-pro", "hrm-dual", "hrm-run",     // Garmin heart rate straps
        "verity sense",                       // Polar armband
        "oh1", "oh1+",                        // Polar optical heart rate sensor

        // ===== Fitness band series (more precise than "band") =====
        "xiaomi band", "redmi band", "xiaomi smart band",   // Xiaomi
        "honor band",                         // Honor
        "amazfit band",                       // Huami
        "fitbit", "charge", "inspire", "lux", "sense", "versa",  // Fitbit
        "whoop",                              // Whoop fitness band
        "oura ring", "oura",                  // Oura ring
        "huawei band", "huawei smart band", "huawei smartband",  // Huawei
        "honor band", "honor smart band", "honor smartband", // Honor
        "smart band", "smartband", "band", // Broad match

        // ===== Watch series =====
        // Garmin
        "forerunner", "fenix", "epix", "vivoactive", "venu", "approach",
        "instinct", "descent", "enduro", "lily", "marq",
        // Huawei / Honor
        "watch gt", "watch fit", "watch d", "watch ultimate", "watch buds",
        "honor watch",
        // Samsung
        "galaxy watch", "galaxy fit", "gear s", "gear fit",
        // Apple
        "apple watch", "watch series", "watch se", "watch ultra",
        // Xiaomi
        "watch s", "watch 2", "watch 3", "watch 4", "watch 5", "watch 6", "watch 7",
        // OPPO / OnePlus
        "oppo watch", "oneplus watch",
        // Other
        "ticwatch", "t-rex", "gtr ", "gts ",  // Spaces avoid false positives
        "coros", "pace", "vertix", "apex",    // Coros
        "suunto", "suunto 9", "suunto 5", "suunto 7",
        "polar", "ignite", "vantage",         // Polar watches
        "magene", "c406", "c206",             // Magene
        "coospo", "hw606", "hw807",           // Coospo
        "decathlon", "geonaute",              // Decathlon

        // ===== Generic wearable terms (short terms last; lowest precision but broadest coverage) =====
        " band", " watch",                    // Leading spaces avoid false positives
        " fitness", " sport", " health",      // Common wearable suffixes
    };


/// <summary>Headphone and speaker characteristics reduce weight because these devices advertise but never report heart rate.</summary>
private static readonly string[] Audio =
{
    // ===== Existing =====
    "airpods", "pods", "buds", "bouds", "enco", "soundcore", "speaker", "headset", "headphone",
    "earphone", "freelace", "flipbuds", "sony wh", "sony wf", "qcy", "edifier", "jbl", "bose",

    // ===== Apple =====
    "airpods pro", "airpods max", "beats", "powerbeats", "studio", "solo", "homepod", "airplay",

    // ===== Samsung =====
    "galaxy buds", "buds+", "buds pro", "buds live", "buds fe", "buds2", "buds3",
    "level-", "samsung level",

    // ===== Huawei =====
    "freebuds", "freelace", "freeclip", "freebuds pro", "freebuds se", "freebuds lite",
    "huawei free", "cm510", "cm530", "aiyo",

    // ===== Xiaomi / Redmi =====
    "redmi buds", "mi true", "mi air", "mi basic", "flipbuds", "xiaomi buds", "xiaomi air",
    "haylou", "waner", "dot", "s1", "s3", "s5",

    // ===== OPPO / OnePlus / realme =====
    "oppo enco", "oneplus buds", "oneplus nord buds", "realme buds", "realme techlife",

    // ===== vivo/iQOO =====
    "vivo tws", "vivo wireless", "iqoo tws", "neo tws",

    // ===== Honor =====
    "honor choice", "honor earbuds", "honor x buds",

    // ===== Sony =====
    "sony wh", "sony wf", "sony linkbuds", "linkbuds", "xperia ear", "sony srs",

    // ===== Other brands =====
    "jbl", "bose", "soundcore", "qcy", "edifier", "sennheiser", "momentum", "cx-",
    "jabra", "elite", "evolve", "steelSeries", "arctis", "razer", "hammerhead",
    "skullcandy", "indy", "dime", "jlab", "tozo", "earfun", "tranya", "moondrop",
    "truthear", "7hz", "tangzu", "salnotes", "kiwi ears", "letsHuoer",

    // ===== Speakers / audio systems =====
    "speaker", "soundbar", "soundtouch", "sonos", "harman", "harman/kardon",
    "marshall", "stanmore", "willen", "roam", "move", "era", "five", "one sl",
    "echo", "alexa", "google nest", "nest audio", "nest mini", "homepod", "home mini",
    "xiaomi sound", "mi speaker", "redmi speaker", "huawei sound", "sound x",

    // ===== Generic keywords =====
    "tws", "anc ", "ldac", "aptx", "aac-", "sbc-", "le audio", "bluetooth audio",
    "audio sink", "a2dp", "avrcp", "handsfree", "hfp", "call-", "mic-",
};


/// <summary>Smart-home, peripheral, and phone characteristics reduce weight.</summary>
private static readonly string[] Home =
{
    // Existing
    "midea", "mesh", "bulb", "lamp", "plug", "sensor", "gateway", "switch", "curtain",
    "yeelight", "aqara", "tuya", "printer", "scale", "mouse", "keyboard", "tv", "projector",
    "HUAWEI Mate",

    // Lighting / switches / outlets
    "light", "lighting", "led", "strip", "dimmer", "outlet", "socket", "powerstrip", "powersrip",
    "relay", "breaker", "scene-switch", "wireless-switch", "fan-switch",

    // Curtains / doors and windows / security
    "roller", "shade", "blind", "door", "window", "contact", "vibration", "leak", "smoke",
    "gas", "alarm", "siren", "camera", "doorbell", "lock", "valve", "irrigation",

    // Environmental sensing
    "thermostat", "temp-hum", "th-sensor", "humidity", "air-quality", "pm2", "co2", "voc",

    // Major appliances / kitchen
    "refrigerator", "fridge", "washer", "washing", "dryer", "oven", "microwave", "kettle",
    "coffee", "airfryer", "purifier", "humidifier", "dehumidifier", "heater", "ac-", "aircon",
    "conditioner", "boiler", "water-heater", "toilet", "bidet", "diffuser",

    // Cleaning / pets
    "vacuum", "sweeper", "robot", "pet-feeder", "pet-water", "cat-", "dog-",

    // PC / peripherals / media
    "magic-keyboard", "magic-mouse", "trackpad", "gamepad", "controller", "headset",
    "speaker", "soundbar", "earphone", "earbuds", "airpods", "freebuds", "buds-",
    "remote", "cast", "chromecast", "mi-box", "appletv", "roku", "harman", "jbl", "bose",

    // Automotive / OBD
    "obd", "elm327", "carplay", "auto-", "vehicle", "dashcam",

    // Industrial, agricultural, and other
    "beacon", "ibeacon", "eddystone", "tag", "finder", "tile-", "airtag",
    "printer", "scanner", "pos-", "kiosk", "tester", "multimeter"
};


    // ------------------------------------------------------------------ Recording

    /// <summary>
    /// Record one advertisement. Returns true when it should be reported to the frontend after throttling.
    /// When name is empty or UNKNOWN, retain the cached identifier.
    /// </summary>
    public bool Observe(string mac, string name, int rssi, string type, out Entry entry)
    {
        var now = Environment.TickCount64;
        entry = _entries.GetOrAdd(mac, m => new Entry { Mac = m, FirstSeen = now });
        // Advertisement frequency = 1 / (current advertisement time - previous advertisement time), smoothed with an EMA.
        if (entry.LastSeen > 0)
        {
            var dt = (now - entry.LastSeen) / 1000.0;
            if (dt > 0.01)
            {
                var hz = 1.0 / dt;
                entry.AdvHz = entry.AdvHz > 0 ? entry.AdvHz * 0.7 + hz * 0.3 : hz;
            }
        }
        entry.LastSeen = now;
        // -127 is WinRT's unavailable-RSSI sentinel; Windows stops delivering advertisements after connection.
        // Storing it would leave the list stuck at -127 dBm, so accept only valid values.
        if (rssi != 0 && rssi > RssiUnavailable)
        {
            entry.Rssi = rssi;
            entry.HasRssi = true;
            entry.RssiAt = now;
        }
        var named = !string.IsNullOrWhiteSpace(name) && name != "UNKNOWN";
        if (named) entry.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(type) && type != "BLE Device") entry.Type = type;

        WarnIfWeak(entry, now);

        var throttle = Math.Max(0, App.Config.Devices.RefreshThrottleMs);
        if (now - entry.LastPush < throttle) return false;
        entry.LastPush = now;
        return true;
    }

    /// <summary>Log once when RSSI attenuation exceeds the threshold, at most once per device every five minutes; do not evaluate without a valid RSSI.</summary>
    private static void WarnIfWeak(Entry e, long now)
    {
        if (!e.HasRssi) return;
        var attenuation = Math.Abs(e.Rssi);
        if (attenuation <= App.Config.Devices.RssiWeakThreshold) return;
        if (now - e.LastWeakWarn < 300_000) return;
        e.LastWeakWarn = now;
        App.Log.Warn(LogText.L("log.dev.weak_signal", e.Display(), attenuation, App.Config.Devices.RssiWeakThreshold));
    }

    /// <summary>
    /// Record one heart rate notification and calculate report frequency from notification intervals, smoothed with an EMA.
    /// This differs from AdvHz, the advertisement frequency: this value measures the post-connection GATT notification refresh rate.
    /// </summary>
    public void OnReport(string mac)
    {
        if (!_entries.TryGetValue(mac, out var e)) return;
        var now = Environment.TickCount64;
        if (e.LastReport > 0)
        {
            var dt = (now - e.LastReport) / 1000.0;
            if (dt > 0.01)
            {
                var hz = 1.0 / dt;
                e.NotifyHz = e.NotifyHz > 0 ? e.NotifyHz * 0.7 + hz * 0.3 : hz;
            }
        }
        e.LastReport = now;
        e.Reports++;
    }

    /// <summary>
    /// Mark RSSI unavailable after a successful connection because Windows stops delivering advertisements for the device.
    /// Continuing to show the last scan value would imply false real-time data, so the UI consistently displays an em dash.
    /// </summary>
    public void OnConnected(string mac)
    {
        if (!_entries.TryGetValue(mac, out var e)) return;
        e.HasRssi = false;
        e.AdvHz = 0;
    }

    /// <summary>On disconnection, clear values that are valid only while connected, such as report frequency, to avoid leaving stale values in the list.</summary>
    public void OnDisconnected(string mac)
    {
        if (!_entries.TryGetValue(mac, out var e)) return;
        e.NotifyHz = 0;
        e.LastReport = 0;
    }

    /// <summary>
    /// Write information back after a successful GATT connection. The connection proves that the heart rate service (0x2A37) exists,
    /// and the resolved system device name is available while connected. Once connected, Windows stops delivering advertisements.
    /// If connection occurs before the first named advertisement, a common startup auto-reconnect race, the registry's Name/Type remains empty
    /// and Category/HrMarked remains generic, causing the frontend to show the default Bluetooth icon. This is the root cause of the intermittent
    /// default icon for high-weight devices after startup. The write-back must occur before the Connected event so the following BroadcastDevices
    /// call includes the correct classification.
    /// </summary>
    public void OnGattVerified(string mac, string? gattName)
    {
        var e = _entries.GetOrAdd(mac, m => new Entry { Mac = m, FirstSeen = Environment.TickCount64 });
        if (!string.IsNullOrWhiteSpace(gattName)
            && gattName != "UNKNOWN"
            && !gattName.Equals(mac, StringComparison.OrdinalIgnoreCase))
            e.Name = gattName.Trim();
        // A connection proves the heart rate service exists; set Type only when it has not already been marked, preserving an equivalent advertised value.
        if (!e.Type.Contains("heart rate", StringComparison.OrdinalIgnoreCase)) e.Type = "Heart Rate Monitor";
    }

    public Entry? Get(string mac) => _entries.TryGetValue(mac, out var e) ? e : null;

    public void Remove(string mac) => _entries.TryRemove(mac, out _);

    public IEnumerable<string> Macs => _entries.Keys;

    // ------------------------------------------------------------------ Aliases / history

    /// <summary>Display name precedence: alias, advertised name, then MAC.</summary>
    public static string DisplayName(string mac, string? fallback)
    {
        if (App.Config.Devices.Aliases.TryGetValue(mac, out var alias) && !string.IsNullOrWhiteSpace(alias))
            return alias;
        return string.IsNullOrWhiteSpace(fallback) || fallback == "UNKNOWN" ? mac : fallback!;
    }

    public static void Rename(string mac, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) App.Config.Devices.Aliases.Remove(mac);
        else App.Config.Devices.Aliases[mac] = alias.Trim();
        App.Config.Save();
        App.Log.Info(string.IsNullOrWhiteSpace(alias)
            ? LogText.L("log.dev.alias_cleared", mac)
            : LogText.L("log.dev.renamed", mac, alias));
    }

    /// <summary>Record a connected device in history, most recent first, with a maximum of 50 entries.</summary>
    public static void RecordHistory(string mac)
    {
        var h = App.Config.Devices.History;
        h.RemoveAll(m => m.Equals(mac, StringComparison.OrdinalIgnoreCase));
        h.Insert(0, mac);
        if (h.Count > 50) h.RemoveRange(50, h.Count - 50);
        App.Config.Save();
        // A connection occurred during this session, so the exit snapshot may update LastConnected by clearing or replacing it.
        _sessionHadConnect = true;
    }

    /// <summary>Whether a connection occurred during this session; set by RecordHistory and reset after the snapshot.</summary>
    private static bool _sessionHadConnect;

    /// <summary>
    /// Snapshot the devices still connected in this session to LastConnected. BleManager.DisconnectAll calls this before a normal exit,
    /// allowing AutoConnectLast to reconnect all devices on the next launch instead of only the first History entry.
    /// Rules: if no connection occurred in this session, preserve the old list for crash recovery; if devices connected but all disconnected before exit, clear it;
    /// if any devices remain connected, replace it with the current connected set.
    /// </summary>
    public void SnapshotSessionDevices()
    {
        if (!_sessionHadConnect) return;
        var macs = App.Ble.ConnectedDevices().Select(d => d.Mac).ToList();
        App.Config.Devices.LastConnected = macs;
        try { App.Config.Save(); } catch { /* Ignore read-only directories. */ }
        _sessionHadConnect = false;
    }

    // ------------------------------------------------------------------ Sorting

    /// <summary>Weights for each score tier, centralized here for tuning.</summary>
    private const int WConnected = 10000;   // Connected
    private const int WSaved = 4000;        // Saved
    private const int WHrService = 3000;    // Advertisement exposes the heart rate service
    private const int WNamed = 800;         // Advertisement includes its own identifier, not UNKNOWN
    private const int WBrand = 600;         // Matches a wearable brand/model
    private const int WHistoryTop = 300;    // Connection history, weighted by recency
    private const int WRssiMax = 60;        // Maximum signal-strength score
    private const int WLatencyMax = 80;     // Maximum advertisement-frequency latency score
    private const int PAudio = -1200;       // Headphones/speakers
    private const int PHome = -900;         // Smart-home devices/peripherals
    private const int PIdleMax = -200;      // Not seen for a long time

    /// <summary>
    /// Combined name-priority and signal-strength score; larger values sort first. Dimensions:
    /// 1. Advertisement includes its own identifier rather than UNKNOWN; 2. Name matches a wearable brand/model;
    /// 3. Low communication latency, indicated by short advertisement intervals, and strong signal; 4. No headphone/speaker traits; 5. No smart-home/peripheral traits.
    /// Connected, saved, and heart-rate-service-advertising devices occupy mandatory top tiers.
    /// </summary>
    public int Score(string mac) => Explain(mac).Sum(x => x.Value);

    /// <summary>
    /// Score breakdown as item name to value, including only nonzero items, for UI tooltips and the Console `score` command.
    /// Score() is the sum of this table, so the two cannot diverge.
    /// </summary>
    public List<KeyValuePair<string, int>> Explain(string mac)
    {
        var e = Get(mac);
        var parts = new List<KeyValuePair<string, int>>(8);
        void Add(string k, int v) { if (v != 0) parts.Add(new(k, v)); }

        // Connected and saved devices have highest priority.
        if (App.Ble.Get(mac)?.Connected == true) Add("已连接", WConnected);
        if (App.Config.HeartRate.Devices.Contains(mac, StringComparer.OrdinalIgnoreCase)) Add("已保存", WSaved);
        // Advertisement directly exposes the heart rate service.
        if ((e?.Type ?? "").Contains("heart rate", StringComparison.OrdinalIgnoreCase)) Add("心率服务", WHrService);
        // 1. Advertisement contains the device's own name, yielding an identifier rather than UNKNOWN falling back to MAC.
        if (!string.IsNullOrWhiteSpace(e?.Name) && e!.Name != mac) Add("有标识符", WNamed);
        // 2, 4, 5. Match both aliases and advertised names so changing an alias does not discard brand traits.
        var names = NameCandidates(mac, e?.Name);
        var isAudio = MatchAny(names, Audio);
        var isHome = MatchAny(names, Home);
        // Apply brand weight only to non-audio, non-home devices. Headphones such as HUAWEI FreeBuds also match vendor terms,
        // but their brand score must not cancel the penalty.
        if (!isAudio && !isHome && MatchAny(names, Preferred)) Add("可穿戴品牌", WBrand);
        // Previously connected, with more recent entries receiving higher weight.
        var hi = App.Config.Devices.History.FindIndex(m => m.Equals(mac, StringComparison.OrdinalIgnoreCase));
        if (hi >= 0) Add("历史连接", Math.Max(0, WHistoryTop - hi * 10));
        if (isAudio) Add("音频设备", PAudio);
        if (isHome) Add("智能家居", PHome);
        // 3-1. Signal strength: -40 dBm gives +60 and -100 dBm gives 0; no valid RSSI means neither bonus nor penalty.
        if (e is { HasRssi: true }) Add("信号强度", Math.Clamp(100 + e.Rssi, 0, WRssiMax));
        // 3-2. Approximate communication latency using the inverse advertisement interval, AdvHz, already EMA-smoothed.
        // Full score starts at 10 Hz. Real RTT is unavailable for disconnected devices, so advertisement frequency is the only available latency proxy.
        if (e is { AdvHz: > 0 }) Add("广播频率", Math.Clamp((int)(e.AdvHz * 8), 0, WLatencyMax));
        // Reduce weight for devices not seen recently by 5 points every 10 seconds.
        var idleSec = e == null ? 999 : (Environment.TickCount64 - e.LastSeen) / 1000;
        Add("失联时长", -(int)Math.Min(-PIdleMax, idleSec / 10 * 5));
        return parts;
    }

    /// <summary>Name candidates used for keyword matching: alias and advertised name, both converted to lowercase.</summary>
    private static string[] NameCandidates(string mac, string? advName)
    {
        var list = new List<string>(2);
        if (App.Config.Devices.Aliases.TryGetValue(mac, out var alias) && !string.IsNullOrWhiteSpace(alias))
            list.Add(alias.ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(advName) && advName != mac)
            list.Add(advName!.ToLowerInvariant());
        return list.ToArray();
    }

    private static bool MatchAny(string[] names, string[] keywords) =>
        names.Any(n => keywords.Any(k => n.Contains(k, StringComparison.Ordinal)));

    // ------------------------------------------------------------------ Display-name tokenization / signal level

    /// <summary>Name segments matching brand/model keywords have hit=true so the frontend can render them in bold italics.</summary>
    public sealed class NameSeg
    {
        public string Text = "";
        public bool Hit;
    }

    /// <summary>
    /// Split the display name on whitespace and compare each segment against Preferred keywords, for example "HUAWEI Band 9" becomes HUAWEI✓ Band✓ 9.
    /// Keywords use substring matching, so forms without spaces such as "Band9" also mark the whole segment.
    /// </summary>
    public static List<NameSeg> Segments(string name)
    {
        var segs = new List<NameSeg>();
        if (string.IsNullOrWhiteSpace(name)) return segs;
        foreach (var part in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var low = part.ToLowerInvariant();
            segs.Add(new NameSeg { Text = part, Hit = Preferred.Any(k => low.Contains(k, StringComparison.Ordinal)) });
        }
        return segs;
    }

    /// <summary>
    /// Signal level: unknown, ok, weak, or critical, using Devices.RssiWeakThreshold and RssiCriticalThreshold.
    /// When hasRssi=false because the device stopped advertising after connection or no valid value was ever received, return unknown so the frontend shows a gray dot and an em dash.
    /// </summary>
    public static string SignalLevel(int rssi, bool hasRssi = true)
    {
        if (!hasRssi || rssi == 0 || rssi <= RssiUnavailable) return "unknown";
        var attenuation = Math.Abs(rssi);
        if (attenuation > App.Config.Devices.RssiCriticalThreshold) return "critical";
        if (attenuation > App.Config.Devices.RssiWeakThreshold) return "weak";
        return "ok";
    }

    /// <summary>Device category: hr, audio, home, or generic; the frontend selects an icon from this value.</summary>
    public string Category(string mac)
    {
        var e = Get(mac);
        if ((e?.Type ?? "").Contains("heart rate", StringComparison.OrdinalIgnoreCase)) return "hr";
        var names = NameCandidates(mac, e?.Name);
        if (MatchAny(names, Audio)) return "audio";
        if (MatchAny(names, Home)) return "home";
        if (MatchAny(names, Preferred)) return "hr";
        return "generic";
    }

    /// <summary>
    /// Heart rate icon detection, which can override other categories: the advertisement exposes the heart rate service,
    /// or candidate names, alias plus advertised name, match at least two wearable keywords, the minimum signal for a sufficient weight.
    /// </summary>
    public bool HrMarked(string mac)
    {
        var e = Get(mac);
        if ((e?.Type ?? "").Contains("heart rate", StringComparison.OrdinalIgnoreCase)) return true;
        var names = NameCandidates(mac, e?.Name);
        var hits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in names)
        {
            foreach (var k in Preferred)
            {
                if (n.Contains(k, StringComparison.Ordinal)) hits.Add(k);
                if (hits.Count >= 2) return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ Automatic reconnection

    /// <summary>After a non-manual disconnect, retry at intervals until the timeout; safe mode disables automatic reconnection.</summary>
    public void ScheduleReconnect(string mac)
    {
        if (App.SafeMode) return;
        if (!App.Config.Devices.AutoReconnect) return;
        if (!_reconnecting.TryAdd(mac, Environment.TickCount64)) return;
        var intervalSec = Math.Max(1, App.Config.Devices.ReconnectIntervalSec); // #11: minimum 1, matching the frontend; previously 3
        var giveUpMs = Math.Max(1, App.Config.Devices.ReconnectGiveUpMin) * 60_000L;
        var name = DisplayName(mac, Get(mac)?.Name);
        App.Log.Info(LogText.L("log.dev.reconnect_start", name, intervalSec, App.Config.Devices.ReconnectGiveUpMin));

        _ = Task.Run(async () =>
        {
            var start = Environment.TickCount64;
            try
            {
                while (Environment.TickCount64 - start < giveUpMs)
                {
                    // Stop reconnecting after the device is blocked; BlockDevice already calls CancelReconnect, and this is a safeguard.
                    if (App.Config.HeartRate.Blocked.Contains(mac, StringComparer.OrdinalIgnoreCase)) return;
                    await Task.Delay(intervalSec * 1000);
                    if (!App.Config.Devices.AutoReconnect) break;
                    if (App.Ble.Get(mac)?.Connected == true) break;
                    // Reconnection requires the device to be advertising, so start scanning if needed.
                    if (!App.Ble.Scanning) App.Ble.StartScan();
                    if (await App.Ble.ConnectAsync(mac))
                    {
                        App.Log.Info(LogText.L("log.dev.reconnect_ok", name));
                        return;
                    }
                }
                App.Log.Warn(LogText.L("log.dev.reconnect_giveup", name, App.Config.Devices.ReconnectGiveUpMin));
            }
            finally
            {
                _reconnecting.TryRemove(mac, out _);
            }
        });
    }

    public bool IsReconnecting(string mac) => _reconnecting.ContainsKey(mac);

    public void CancelReconnect(string mac) => _reconnecting.TryRemove(mac, out _);

    // ------------------------------------------------------------------ Auto-detection / automatic connection

    private int _autoDetecting;
    /// <summary>Whether auto-detection is running, used for the frontend button state.</summary>
    public bool AutoDetecting => Volatile.Read(ref _autoDetecting) != 0;
    /// <summary>Auto-detection progress: attempted count, total count, and current device, for the frontend and logs.</summary>
    public (int Done, int Total, string Current) AutoDetectProgress { get; private set; }

    private CancellationTokenSource? _autoDetectCts;

    /// <summary>
    /// Auto-detection tries every candidate in range from highest to lowest score until all have been checked or the user stops it.
    /// Skip blocked devices, devices known to lack a heart rate characteristic, and devices whose advertisement traits indicate audio or smart-home hardware,
    /// because attempting those would only wait for a GATT timeout. Connected devices remain connected while the next candidate is tried.
    /// </summary>
    public void StartAutoDetect()
    {
        if (Interlocked.CompareExchange(ref _autoDetecting, 1, 0) != 0) return;
        _autoDetectCts = new CancellationTokenSource();
        var token = _autoDetectCts.Token;

        _ = Task.Run(async () =>
        {
            var connected = 0;
            try
            {
                // A device must be advertising to connect, so start scanning if needed.
                if (!App.Ble.Scanning) App.Ble.StartScan();
                // Allow advertisements time to populate the candidate list after scanning starts.
                await Task.Delay(1500, token);

                var candidates = Candidates();
                var timeout = TimeSpan.FromSeconds(Math.Max(3, App.Config.Devices.AutoDetectTimeoutSec)); // #11: no upper limit
                App.Log.Info(LogText.L("log.dev.autodetect_start", candidates.Count, timeout.TotalSeconds));

                for (var i = 0; i < candidates.Count; i++)
                {
                    if (token.IsCancellationRequested) break;
                    var mac = candidates[i];
                    var name = DisplayName(mac, Get(mac)?.Name);
                    AutoDetectProgress = (i, candidates.Count, name);
                    if (App.Ble.Get(mac)?.Connected == true) continue;

                    // Limit each device attempt because GATT calls may hang for a long time when BLE cannot connect.
                    var connect = App.Ble.TryConnectAsync(mac);
                    var done = await Task.WhenAny(connect, Task.Delay(timeout, token));
                    if (done != connect)
                    {
                        App.Log.Warn(LogText.L("log.dev.autodetect_timeout", name));
                        continue;
                    }
                    var result = await connect;
                    if (result == BleManager.ConnectResult.Ok)
                    {
                        connected++;
                        AutoDetectProgress = (i + 1, candidates.Count, name);
                        App.Log.Info(LogText.L("log.dev.autodetect_hit", name));
                        // Continue after a match to connect every device in range until the list is exhausted or the user stops it.
                        continue;
                    }
                    if (result == BleManager.ConnectResult.NoHeartRate)
                    {
                        // Skip permanently because this device's GATT profile has no 0x2A37 characteristic.
                        var e = Get(mac);
                        if (e != null) e.NoHrChar = true;
                    }
                }
                if (connected == 0) App.Log.Warn(LogText.L("log.dev.autodetect_none"));
                else App.Log.Info(LogText.L("log.dev.autodetect_done", connected));
            }
            catch (OperationCanceledException)
            {
                App.Log.Info(LogText.L("log.dev.autodetect_cancel"));
            }
            catch (Exception e)
            {
                App.Log.Error(LogText.L("log.dev.autodetect_error", e.Message));
            }
            finally
            {
                Volatile.Write(ref _autoDetecting, 0);
                _autoDetectCts?.Dispose();
                _autoDetectCts = null;
            }
        }, token);
    }

    public void StopAutoDetect()
    {
        try { _autoDetectCts?.Cancel(); } catch { }
    }

    /// <summary>Auto-detection candidates exclude blocked, known-no-heart-rate, audio, and smart-home devices, then sort by descending score.</summary>
    public List<string> Candidates() => _entries.Keys
        .Where(m => !App.Config.HeartRate.Blocked.Contains(m, StringComparer.OrdinalIgnoreCase))
        .Where(m => Get(m)?.NoHrChar != true)
        .Where(m => Category(m) is not ("audio" or "home"))
        .OrderByDescending(Score)
        .ToList();

    /// <summary>
    /// After startup, automatically connect devices that were still connected in the previous session, using LastConnected for multiple devices.
    /// Fall back to the first History entry when older configurations have no LastConnected list.
    /// Devices may not yet be advertising early in startup, making one attempt ineffective, so retry at least every 500 ms for up to 10 rounds
    /// as added in round 32 item #29. Stop when all devices connect or the rounds are exhausted; pass remaining devices to ScheduleReconnect
    /// for normal automatic reconnection using configured interval and give-up limits, or log a warning when disabled.
    /// </summary>
    public void AutoConnectLast()
    {
        if (!App.Config.Devices.AutoConnect) return;
        var targets = App.Config.Devices.LastConnected
            .Where(m => !App.Config.HeartRate.Blocked.Contains(m, StringComparer.OrdinalIgnoreCase))
            .Where(m => Get(m)?.NoHrChar != true)
            .Take(8)
            .ToList();
        // Older configuration or no prior normal exit: without LastConnected, fall back to the original first History entry.
        if (targets.Count == 0)
        {
            var mac = App.Config.Devices.History.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(mac)
                && !App.Config.HeartRate.Blocked.Contains(mac, StringComparer.OrdinalIgnoreCase))
                targets.Add(mac);
        }
        if (targets.Count == 0) return;

        _ = Task.Run(async () =>
        {
            var names = targets.Select(m => DisplayName(m, Get(m)?.Name));
            App.Log.Info(LogText.L("log.dev.autoconnect_last", string.Join(", ", names)));
            if (!App.Ble.Scanning) App.Ble.StartScan();
            var timeout = TimeSpan.FromSeconds(Math.Max(3, App.Config.Devices.AutoDetectTimeoutSec)); // #11: no upper limit, consistent with AutoConnect
            const int maxRounds = 10;
            // Per-device startup attempt limit: the first advertisement round may not have populated yet, and a short limit prevents one unreachable device
            // from consuming the entire window before later devices get a chance, the root cause of "only the first connects" addressed in round 32 item #32.
            var perTry = TimeSpan.FromMilliseconds(Math.Min(timeout.TotalMilliseconds, 6000));
            var remaining = targets.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var guard = new object();

            for (var round = 1; round <= maxRounds && remaining.Count > 0; round++)
            {
                // Wait at least 500 ms between rounds to allow advertisement and scan results to populate during startup.
                await Task.Delay(500);
                if (!App.Ble.Scanning) App.Ble.StartScan();
                // Try all remaining devices in parallel each round so one slow or unreachable device does not block the others.
                var pending = targets.Where(remaining.Contains).ToList();
                await Task.WhenAll(pending.Select(async mac =>
                {
                    var ok = false;
                    try
                    {
                        if (App.Ble.Get(mac)?.Connected == true) { ok = true; return; }
                        var connect = App.Ble.TryConnectAsync(mac);
                        var done = await Task.WhenAny(connect, Task.Delay(perTry));
                        if (done == connect && await connect == BleManager.ConnectResult.Ok)
                        {
                            App.Log.Info(LogText.L("log.dev.reconnect_ok", DisplayName(mac, Get(mac)?.Name)));
                            ok = true;
                        }
                    }
                    catch { /* A failure for one device must not interrupt the others. */ }
                    finally
                    {
                        if (ok) lock (guard) { remaining.Remove(mac); }
                    }
                }));
            }

            // After 10 unsuccessful rounds, hand remaining devices to normal automatic reconnection when enabled; otherwise log one warning.
            foreach (var mac in remaining)
            {
                var name = DisplayName(mac, Get(mac)?.Name);
                if (App.Config.Devices.AutoReconnect) ScheduleReconnect(mac);
                else App.Log.Warn(LogText.L("log.dev.autoconnect_fail", name));
            }
        });
    }
}

internal static class EntryExt
{
    public static string Display(this DeviceRegistry.Entry e) => DeviceRegistry.DisplayName(e.Mac, e.Name);
}
