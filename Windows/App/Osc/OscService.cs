using System.Diagnostics;
using System.Text.Json;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.Osc;

/// <summary>XQ1QXZ service: Push Job (time)+ Receive (C engine echo)+ Connection status. </summary>
public class OscService
{
    private System.Threading.Timer? _pushTimer;
    private volatile bool _connected;
    public long SentCount;
    public long FailCount;
    public long RecvCount;

    public bool Connected => _connected;

    /// <summary> received OSC messages (address, args json text). </summary>
    public event Action<string, List<Dictionary<string, object?>>>? Received;
    public event Action<bool>? ConnectionChanged;

    public int ReceivePort => int.TryParse(App.Config.Osc.ReceivePort, out var p) ? p : 9001;

    public void Start()
    {
        if (App.SafeMode) return;
        if (!OscEngine.Available)
        {
            App.Log.Error(LogText.L("log.osc.dll_load_fail"));
            return;
        }
        // Receive 9001 (Permanent)
        if (OscEngine.StartReceiver(ReceivePort, OnPacket))
            App.Log.Info(LogText.L("log.osc.listen_start", ReceivePort));
        else
            App.Log.Error(LogText.L("log.osc.listen_fail", ReceivePort));
    }

    private void OnPacket(byte[] data)
    {
        // This return ring UDP may repeat the same package (environmental behaviour) and weigh it by content Hashi (100ms window)
        var hash = 2166136261u;
        foreach (var b in data) hash = (hash ^ b) * 16777619u;
        var now = Environment.TickCount64;
        lock (_dedupLock)
        {
            if (_dedup.TryGetValue(hash, out var last) && now - last < 100) return;
            _dedup[hash] = now;
        }
        try
        {
            var json = OscEngine.DecodeToJson(data);
            if (string.IsNullOrEmpty(json) || json == "[]") return;
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("address", out var a)) continue;
                var address = a.GetString() ?? "";
                var args = new List<Dictionary<string, object?>>();
                if (el.TryGetProperty("args", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var arg in arr.EnumerateArray())
                    {
                        var t = arg.TryGetProperty("t", out var te) ? te.GetString() : "";
                        object? v = null;
                        // T/F is a type label (the decoder does not produce "v") for a data segment, which is to be valued by the label itself.
                        // Otherwise, AFK / Seated such Boolean parameters are taken to null and the health determination is constant false.
                        if (t == "T") v = true;
                        else if (t == "F") v = false;
                        else if (arg.TryGetProperty("v", out var ve))
                        {
                            v = t switch
                            {
                                "i" or "h" => ve.TryGetInt64(out var l) ? l : null,
                                "f" or "d" => ve.TryGetDouble(out var dbl) ? dbl : null,
                                "s" or "b" => ve.GetString(),
                                _ => null
                            };
                        }
                        var dict = new Dictionary<string, object?> { ["t"] = t, ["v"] = v };
                        args.Add(dict);
                    }
                }
                RecvCount++;
                // Do not write the log by article: VRChat parameters can eject dozens of frames and flood the log buffer.
                // Statistics and displays to AppHub by address aggregate buffer.
                Received?.Invoke(address, args);
            }
        }
        catch (Exception e)
        {
            App.Log.Debug(LogText.L("log.osc.parse_fail", e.Message));
        }
    }

    /// <summary> connection/disconnect OSC push. </summary>
    public void SetConnected(bool v)
    {
        if (App.SafeMode && v) return;
        if (_connected == v) return;
        _connected = v;
        App.Log.Info(LogText.L(v ? "log.osc.conn_on" : "log.osc.conn_off"));
        ConnectionChanged?.Invoke(v);
        if (v)
        {
            var ms = int.TryParse(App.Config.Osc.IntervalMs, out var m) ? m : 1000;
            _pushTimer ??= new System.Threading.Timer(_ => Tick(), null, 200, ms);
        }
        else
        {
            _pushTimer?.Dispose();
            _pushTimer = null;
        }
    }

    public void UpdateInterval()
    {
        var ms = int.TryParse(App.Config.Osc.IntervalMs, out var m) ? Math.Max(20, m) : 1000;
        if (_connected && _pushTimer != null)
            _pushTimer.Change(ms, ms);
    }

    private System.Threading.Timer? _pauseResume;

    /// Temporary suspension of <summary> (defined for sending): stop the watch and automatically resume it after the specified millisecond (jump if the connection is disconnected). </summary>
    public void PausePush(int ms)
    {
        if (ms <= 0 || !_connected) return;
        try { _pushTimer?.Dispose(); } catch { }
        _pushTimer = null;
        try { _pauseResume?.Dispose(); } catch { }
        _pauseResume = new System.Threading.Timer(_ =>
        {
            try { _pauseResume?.Dispose(); _pauseResume = null; } catch { }
            if (!_connected) return;
            var interval = int.TryParse(App.Config.Osc.IntervalMs, out var m) ? m : 1000;
            _pushTimer ??= new System.Threading.Timer(_ => Tick(), null, interval, interval);
            App.Log.Debug(LogText.L("log.osc.push_resume"));
        }, null, ms, System.Threading.Timeout.Infinite);
    }

    /// <summary>
    /// `/chatbox/input` accepts three arguments: text, immediate, and sound; other addresses receive one string.
    /// The immediate flag sends the message immediately instead of only updating the input box.
    /// The sound flag plays a notification sound and must be retriggered for repeated sounds.
    /// </summary>
    public static bool SendOne(string ip, int port, string address, string text)
    {
        if (App.SafeMode) return false;
        if (address.StartsWith("/chatbox/input", StringComparison.OrdinalIgnoreCase))
            return OscEngine.SendChatbox(ip, port, address, text, immediate: true, sound: false);
        return OscEngine.SendText(ip, port, address, text);
    }

    private void Tick()
    {
        if (!_connected) return;
        var sw = Stopwatch.StartNew();
        try
        {
            var ip = App.Config.Osc.Ip;
            var port = int.TryParse(App.Config.Osc.Port, out var p) ? p : 9000;
            var address = App.Config.Osc.Address;
            var text = App.SysInfo.FormatTemplate(App.Config.Osc.Template);
            if (SendOne(ip, port, address, text)) SentCount++;
            else FailCount++;
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.osc.push_fail", e.Message));
        }
        sw.Stop();
        HrmTrace.Perf("osc.tick", sw.ElapsedMilliseconds, 30);
    }

    /// <summary> sends a custom OSC message (test for). </summary>
    public bool SendTest(string ip, string port, string address, string text)
    {
        if (!OscEngine.Available) return false;
        return SendOne(ip, int.TryParse(port, out var p) ? p : 9000, address, text);
    }

    /// <summary>Sends a typed parameter: `bool` uses a T/F argument, `float` uses an OSC float, and other kinds send text.</summary>
    public bool SendParam(string ip, string port, string address, string kind, bool flag, float number)
    {
        if (App.SafeMode || !OscEngine.Available) return false;
        var p = int.TryParse(port, out var x) ? x : 9000;
        try
        {
            if (kind == "bool") return OscEngine.SendBool(ip, p, address, flag);
            if (kind == "float") return OscEngine.SendFloat(ip, p, address, number);
            return SendOne(ip, p, address, "");
        }
        catch { return false; }
    }

    public void Stop()
    {
        _connected = false;
        _pushTimer?.Dispose();
        _pushTimer = null;
        _pauseResume?.Dispose();
        _pauseResume = null;
        OscEngine.StopReceiver();
    }

    private readonly Dictionary<uint, long> _dedup = new();
    private readonly object _dedupLock = new();
}
