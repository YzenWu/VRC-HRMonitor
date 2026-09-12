using HeartRateMonitor.Core;

namespace HeartRateMonitor.Health;

/// <summary>
/// Health status determination: Heart rate + OSC physical attitude sleep / silent / active / excavated (team variable {HEALTH_STATUS}).
/// For the first time, the calibration is required: stand still 30 seconds to benchmark, and then cross-check 10 seconds.
/// Print Warn once when heart rate fluctuations are excessive (same fluctuations do not repeat screen).
/// </summary>
public sealed class HealthService
{
    public enum Status { Unknown, Sleep, Rest, Active, Excited }

    /// <summary> status change event (new state key, e.g. "hb.status.active " ). </summary>
    public event Action<string>? StatusChanged;

    private readonly object _lock = new();
    private readonly List<(long ts, int bpm)> _window = new();
    private Status _status = Status.Unknown;
    private long _lastSpikeWarn;
    private int _lastBpm;

    // OSC Posture (transmitted by AppHub/ avatar/ parameters/*)
    private volatile bool _afk;
    private volatile bool _seated;
    /// <summary> speed of movement (m/s). float: double does not allow volatile. </summary>
    private volatile float _velocity;
    private long _lastMotion;

    // Calibration
    private volatile bool _calibrating;
    private long _calibStart;
    private readonly List<int> _calibBase = new();
    private readonly List<int> _calibRef = new();

    /// <summary> benchmark acquisition time (sec). </summary>
    public const int BaseSeconds = 30;
    /// <summary> cross-collection time (sec). </summary>
    public const int RefSeconds = 10;

    public Status Current => _status;
    public bool Calibrating => _calibrating;

    /// <summary> status i18n key (for front end tr()). </summary>
    public string StatusKey => _status switch
    {
        Status.Sleep => "hb.status.sleep",
        Status.Rest => "hb.status.rest",
        Status.Active => "hb.status.active",
        Status.Excited => "hb.status.excited",
        _ => "hb.status.unknown",
    };

    /// Readable text for <summary> template variables (fixed to the backend in Chinese and displayed locally at the front end). </summary>
    public string StatusText => _status switch
    {
        Status.Sleep => "睡眠",
        Status.Rest => "静息",
        Status.Active => "活跃",
        Status.Excited => "兴奋",
        _ => "未知",
    };

    // ------------------------------------------------------------------ Enter

    /// <summary> fed a heart rate sample. </summary>
    public void OnHeartRate(int bpm)
    {
        if (bpm <= 0) return;
        var now = Environment.TickCount64;
        lock (_lock)
        {
            _window.Add((now, bpm));
            // Only the latest 5 minutes are reserved
            var cut = now - 300_000;
            _window.RemoveAll(x => x.ts < cut);

            if (_calibrating) CollectCalibration(now, bpm);

            // Volatility alarm: adjacent sample jumps above threshold, 60 reported only once in seconds
            if (_lastBpm > 0 && Math.Abs(bpm - _lastBpm) >= App.Config.Health.SpikeDelta
                && now - _lastSpikeWarn > 60_000)
            {
                _lastSpikeWarn = now;
                App.Log.Warn(LogText.L("log.health.spike_warn", _lastBpm, bpm, App.Config.Health.SpikeDelta));
            }
            _lastBpm = bpm;
        }
        Evaluate();
    }

    /// <summary> feeds a OSC attitude parameter (only AFK / Seated / VelocityMagnitude). </summary>
    public void OnOscParam(string address, object? value)
    {
        switch (address)
        {
            case "/avatar/parameters/AFK":
                _afk = Truthy(value);
                break;
            case "/avatar/parameters/Seated":
                _seated = Truthy(value);
                break;
            case "/avatar/parameters/VelocityMagnitude":
                _velocity = (float)ToDouble(value);
                if (_velocity > 0.1) _lastMotion = Environment.TickCount64;
                break;
            default:
                return;
        }
        Evaluate();
    }

    private static bool Truthy(object? v) => v switch
    {
        bool b => b,
        long l => l != 0,
        double d => Math.Abs(d) > 0.001,
        string s => s is "T" or "true" or "True" or "1",
        _ => false,
    };

    private static double ToDouble(object? v) => v switch
    {
        double d => d,
        long l => l,
        string s when double.TryParse(s, out var p) => p,
        _ => 0,
    };

    // ------------------------------------------------------------------ Calibration

    /// <summary>Starts calibration by collecting a resting baseline, followed by a reference sample period.</summary>
    public void StartCalibration()
    {
        lock (_lock)
        {
            _calibBase.Clear();
            _calibRef.Clear();
            _calibStart = Environment.TickCount64;
            _calibrating = true;
        }
        App.Log.Info(LogText.L("log.health.calib_start", BaseSeconds, RefSeconds));
    }

    public void CancelCalibration()
    {
        lock (_lock) _calibrating = false;
        App.Log.Info(LogText.L("log.health.calib_cancel"));
    }

    /// <summary> calibrates the remaining seconds (0 = not available). </summary>
    public int CalibrationRemainSec()
    {
        if (!_calibrating) return 0;
        var passed = (Environment.TickCount64 - _calibStart) / 1000;
        var total = BaseSeconds + RefSeconds;
        return (int)Math.Max(0, total - passed);
    }

    /// <summary> caller is locked. </summary>
    private void CollectCalibration(long now, int bpm)
    {
        var passed = (now - _calibStart) / 1000;
        if (passed < BaseSeconds) _calibBase.Add(bpm);
        else if (passed < BaseSeconds + RefSeconds) _calibRef.Add(bpm);
        else FinishCalibration();
    }

    /// <summary> caller is locked. </summary>
    private void FinishCalibration()
    {
        _calibrating = false;
        if (_calibBase.Count < 3)
        {
            App.Log.Warn(LogText.L("log.health.calib_insufficient", _calibBase.Count));
            return;
        }
        var mean = _calibBase.Average();
        var sd = Math.Sqrt(_calibBase.Sum(x => (x - mean) * (x - mean)) / _calibBase.Count);
        // Contrasts are used to correct: weight both if there is a marked deviation from the baseline against the average (baseline 0.7 / against 0.3)
        if (_calibRef.Count >= 3)
        {
            var refMean = _calibRef.Average();
            mean = mean * 0.7 + refMean * 0.3;
        }
        App.Config.Health.RestingBpm = Math.Round(mean, 1);
        App.Config.Health.RestingSd = Math.Round(sd, 2);
        App.Config.Health.CalibratedAt = $"{DateTime.Now:O}";
        App.Config.Save();
        App.Log.Info(LogText.L("log.health.calib_done", App.Config.Health.RestingBpm, App.Config.Health.RestingSd, _calibBase.Count, _calibRef.Count));
    }

    // ------------------------------------------------------------------ Decision

    private void Evaluate()
    {
        var cfg = App.Config.Health;
        var resting = cfg.RestingBpm;
        int recent;
        lock (_lock)
        {
            if (_window.Count == 0) return;
            // Use the average of the latest 30 seconds against noise
            var cut = Environment.TickCount64 - 30_000;
            var vals = _window.Where(x => x.ts >= cut).Select(x => x.bpm).ToList();
            recent = vals.Count > 0 ? (int)Math.Round(vals.Average()) : _window[^1].bpm;
        }

        Status next;
        if (resting <= 0)
        {
            // Non-correction: rough sentence only in absolute terms, no sleep (sleep relative benchmark)
            next = recent switch
            {
                < 60 => Status.Rest,
                < 100 => Status.Active,
                _ => Status.Excited,
            };
        }
        else
        {
            var idle = _afk || (Environment.TickCount64 - _lastMotion > 600_000 && _velocity < 0.1);
            if (recent <= resting * cfg.SleepFactor && idle) next = Status.Sleep;
            else if (recent >= resting * cfg.ExcitedFactor) next = Status.Excited;
            else if (recent >= resting * cfg.ActiveFactor) next = Status.Active;
            else next = Status.Rest;
            // Sitting down will keep your heart down and avoid miscalculating sitting as active.
            if (_seated && next == Status.Active && recent < resting * cfg.ExcitedFactor) next = Status.Rest;
        }

        if (next == _status) return;
        _status = next;
        App.SysInfo.Vars["HEALTH_STATUS"] = StatusText;
        if (cfg.Record) HrmDb.InsertHealth(StatusText);
        App.Log.Info(resting > 0
            ? LogText.L("log.health.status_calibrated", StatusText, recent, resting.ToString("F1"))
            : LogText.L("log.health.status_uncalibrated", StatusText, recent));
        StatusChanged?.Invoke(StatusKey);
    }

    /// <summary> status snapshot (for status/health command). </summary>
    public object Snapshot() => new
    {
        status = StatusText,
        statusKey = StatusKey,
        restingBpm = App.Config.Health.RestingBpm,
        restingSd = App.Config.Health.RestingSd,
        calibratedAt = App.Config.Health.CalibratedAt,
        calibrating = _calibrating,
        calibrateRemain = CalibrationRemainSec(),
        afk = _afk,
        seated = _seated,
        velocity = _velocity,
    };
}
