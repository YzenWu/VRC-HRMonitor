using System.Text;
using HeartRateMonitor.Core;

namespace HeartRateMonitor;

/// <summary> Global Log: Console (debug)+ File+ Apply Internal Log Tab (incident). </summary>
public class Logger
{
    private readonly string _file;
    private readonly object _lock = new();
    public event Action<string>? OnLog;

    public Logger(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            _file = Path.Combine(dir, "app.log");
        }
        catch
        {
            _file = Path.Combine(App.DataDir, "app.log");
        }
    }

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg) => Write("ERROR", msg);
    public void Debug(string msg)
    {
        if (App.DebugMode) Write("DEBUG", msg);
    }

    /// <summary> diagnostic level: effective only when logs.traceEnabled is on and restarted (see App.TraceEnabled). </summary>
    public void Trace(string msg)
    {
        if (App.TraceEnabled) Write("TRACE", msg);
    }

    private void Write(string level, string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}][{level}] {msg}";
        if (App.DebugMode)
        {
            try { Console.WriteLine(line); } catch { }
        }
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { }
        try { OnLog?.Invoke(line); } catch { }
    }

    /// <summary> transfers the current cumulative log to auto directory. </summary>
    public string Dump(List<string> buffer)
    {
        try
        {
            var dir = Path.Combine(App.DataDir, App.Config.Logs.DumpDir);
            Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, $"hrm_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(f, string.Join(Environment.NewLine, buffer), Encoding.UTF8);
            return f;
        }
        catch (Exception e)
        {
            return LogText.L("log.logger.dump_fail", e.Message);
        }
    }
}
