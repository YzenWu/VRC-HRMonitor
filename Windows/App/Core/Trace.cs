namespace HeartRateMonitor.Core;

/// <summary>
/// Diagnostic tracing controlled by App.TraceEnabled, which snapshots logs.traceEnabled at startup and takes effect after restart.
/// When disabled, each call site performs only one static Boolean check, making hot-path overhead negligible.
/// Writes to logs/trace.log and also enters the in-app log through App.Log.Trace at TRACE level for filtering on the logs page.
/// </summary>
public static class HrmTrace
{
    private static readonly object Lock = new();
    private static readonly string FilePath = Path.Combine(App.BaseDir, "logs", "trace.log");
    private static int _seq;

    /// <summary>Record an event with timestamp, thread ID, and sequence number.</summary>
    public static void Event(string tag, string msg = "")
    {
        if (!App.TraceEnabled) return;
        try
        {
            var body = $"{tag} {msg}".TrimEnd();
            var line = $"{DateTime.Now:HH:mm:ss.fff} [T{Thread.CurrentThread.ManagedThreadId,3}] {body}";
            lock (Lock)
            {
                var full = $"{++_seq,6} {line}";
                try { System.IO.File.AppendAllText(FilePath, full + "\n"); } catch { }
            }
            try { App.Log?.Trace(body); } catch { }
        }
        catch { }
    }

    /// <summary>Performance marker recorded only when elapsed time exceeds warnMs, avoiding log spam.</summary>
    public static void Perf(string tag, long elapsedMs, long warnMs = 50)
    {
        if (!App.TraceEnabled || elapsedMs < warnMs) return;
        Event("PERF", $"{tag} {elapsedMs}ms");
    }
}
