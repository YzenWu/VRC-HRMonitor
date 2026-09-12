namespace HeartRateMonitor.Cli;

public sealed record CliEvent(string Tag, string Text);

/// <summary>Bounded discrete-event queue with per-device latest-only heart-rate frames.</summary>
public sealed class CliEventQueue
{
    readonly object _lock = new();
    readonly Queue<CliEvent> _events = new();
    readonly Dictionary<string, CliEvent> _heartRates = new(StringComparer.OrdinalIgnoreCase);
    readonly int _capacity;

    public CliEventQueue(int capacity = 128) => _capacity = Math.Max(8, capacity);

    public void Enqueue(string tag, string text)
    {
        lock (_lock)
        {
            while (_events.Count >= _capacity) _events.Dequeue();
            _events.Enqueue(new CliEvent(tag, text));
        }
    }

    public void HeartRate(string device, string tag, string text)
    {
        lock (_lock) _heartRates[device] = new CliEvent(tag, text);
    }

    public List<CliEvent> Drain()
    {
        lock (_lock)
        {
            var result = new List<CliEvent>(_events.Count + _heartRates.Count);
            while (_events.Count > 0) result.Add(_events.Dequeue());
            result.AddRange(_heartRates.Values);
            _heartRates.Clear();
            return result;
        }
    }
}