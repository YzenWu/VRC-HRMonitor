using Microsoft.Win32;

namespace HeartRateMonitor.Core;

/// <summary>Login autostart registration methods (P1). Users may enable any combination; every
/// registration launches <c>--gui --autostart</c> (plus <c>--silent</c> when selected) and duplicate
/// launches are absorbed by the single-instance guard.</summary>
public enum AutoStartMethod
{
    /// <summary>Task Scheduler logon task for the current user (per-user, no elevation).</summary>
    Task,
    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run value.</summary>
    Run,
    /// <summary>Shortcut in the user's Startup folder.</summary>
    Startup,
}

/// <summary>
/// Applies and inspects the three Windows login-autostart registrations (P1). Reads the actual
/// system state rather than trusting configuration, so moved installations and stale entries are
/// visible and repairable. Every method targets the current executable with absolute paths.
/// </summary>
public static class AutoStartManager
{
    /// <summary>Run-value name under HKCU (also the Task Scheduler task name).</summary>
    public const string Name = "HeartRateMonitor";
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string TaskFolder = "\\";

    // Task Scheduler COM constants.
    const int TASK_ACTION_EXEC = 0;
    const int TASK_TRIGGER_LOGON = 9;
    const int TASK_CREATE_OR_UPDATE = 6;
    const int TASK_LOGON_INTERACTIVE_TOKEN = 3;
    const int ERROR_FILE_NOT_FOUND = 2;

    static string ProcessExe => Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];

    static bool TryEngineExe(out string exe, out string? error)
    {
        var processExe = Path.GetFullPath(ProcessExe);
        if (Path.GetFileName(processExe).Equals("HeartRateMonitor.exe", StringComparison.OrdinalIgnoreCase))
        {
            exe = processExe;
            error = null;
            return true;
        }
        var candidate = Path.Combine(Path.GetDirectoryName(processExe) ?? App.ExeDir, "HeartRateMonitor.exe");
        if (File.Exists(candidate))
        {
            exe = Path.GetFullPath(candidate);
            error = null;
            return true;
        }
        exe = candidate;
        error = $"Main engine executable not found: {candidate}";
        return false;
    }

    static bool IsTaskMissing(Exception e) => (e.HResult & 0xffff) == ERROR_FILE_NOT_FOUND;

    /// <summary>Command-line arguments every registration uses; --silent keeps the start tray-only.</summary>
    public static string BuildArguments(bool silent) => "--gui --autostart" + (silent ? " --silent" : "");

    public static string MethodKey(AutoStartMethod m) => m switch
    {
        AutoStartMethod.Task => "task",
        AutoStartMethod.Run => "run",
        AutoStartMethod.Startup => "startup",
        _ => "",
    };

    /// <summary>Parse a config key back to a method; null for unknown values.</summary>
    public static AutoStartMethod? ParseMethod(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "task" => AutoStartMethod.Task,
        "run" => AutoStartMethod.Run,
        "startup" => AutoStartMethod.Startup,
        _ => null,
    };

    /// <summary>All methods, in settings-page display order.</summary>
    public static AutoStartMethod[] All() => new[] { AutoStartMethod.Task, AutoStartMethod.Run, AutoStartMethod.Startup };

    /// <summary>Actual registered target for a method (executable path + arguments), or null when absent.</summary>
    public static (string Path, string Args)? RegisteredTarget(AutoStartMethod m)
    {
        try
        {
            switch (m)
            {
                case AutoStartMethod.Run:
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                    if (key?.GetValue(Name) is not string v || string.IsNullOrWhiteSpace(v)) return null;
                    return ParseCommand(v);
                }
                case AutoStartMethod.Startup:
                {
                    var lnk = ShortcutPath();
                    if (!File.Exists(lnk)) return null;
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                    dynamic sc = shell.CreateShortcut(lnk);
                    var path = (string)sc.TargetPath;
                    var args = (string)sc.Arguments;
                    if (string.IsNullOrWhiteSpace(path)) return null;
                    return (path, args ?? "");
                }
                case AutoStartMethod.Task:
                {
                    dynamic svc = ConnectScheduler();
                    dynamic folder = svc.GetFolder(TaskFolder);
                    try
                    {
                        dynamic task = folder.GetTask(Name);
                        dynamic act = task.Definition.Actions.Item(1);
                        var path = (string)act.Path;
                        if (string.IsNullOrWhiteSpace(path)) return null;
                        return (path, (string?)act.Arguments ?? "");
                    }
                    catch (Exception e) when (IsTaskMissing(e))
                    {
                        return null;
                    }
                }
            }
        }
        catch when (m == AutoStartMethod.Task) { throw; }
        catch { }
        return null;
    }

    /// <summary>Read a target without hiding errors; errors remain serializable through Status.</summary>
    public static ((string Path, string Args)? Target, string? Error) TryRegisteredTarget(AutoStartMethod m)
    {
        try { return (RegisteredTarget(m), null); }
        catch (Exception e) { return (null, e.Message); }
    }

    /// <summary>True when the registration exists and matches the main engine with the desired arguments.</summary>
    public static bool IsCurrent(AutoStartMethod m, bool silent)
    {
        var target = TryRegisteredTarget(m);
        if (target.Error != null || target.Target == null || !TryEngineExe(out var exe, out _)) return false;
        return PathsEqual(target.Target.Value.Path, exe)
            && string.Equals(target.Target.Value.Args.Trim(), BuildArguments(silent).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Register a method with the current executable; returns an error message on failure.</summary>
    public static string? Register(AutoStartMethod m, bool silent)
    {
        try
        {
            if (!TryEngineExe(out var exe, out var targetError)) return targetError;
            var args = BuildArguments(silent);
            var work = Path.GetDirectoryName(exe) ?? App.ExeDir;
            switch (m)
            {
                case AutoStartMethod.Run:
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                    key.SetValue(Name, $"\"{exe}\" {args}");
                    return null;
                }
                case AutoStartMethod.Startup:
                {
                    var lnk = ShortcutPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(lnk)!);
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                    dynamic sc = shell.CreateShortcut(lnk);
                    sc.TargetPath = exe;
                    sc.Arguments = args;
                    sc.WorkingDirectory = work;
                    sc.Description = "HeartRateMonitor autostart";
                    sc.Save();
                    return null;
                }
                case AutoStartMethod.Task:
                {
                    dynamic svc = ConnectScheduler();
                    dynamic folder = svc.GetFolder(TaskFolder);
                    dynamic def = svc.NewTask(0);
                    def.RegistrationInfo.Description = "HeartRateMonitor autostart at logon";
                    dynamic trig = def.Triggers.Create(TASK_TRIGGER_LOGON);
                    trig.UserId = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                    dynamic act = def.Actions.Create(TASK_ACTION_EXEC);
                    act.Path = exe;
                    act.Arguments = args;
                    act.WorkingDirectory = work;
                    def.Settings.StartWhenAvailable = true;
                    def.Settings.DisallowStartIfOnBatteries = false;
                    def.Settings.StopIfGoingOnBatteries = false;
                    def.Settings.ExecutionTimeLimit = "PT0S"; // No time limit: this is a resident app.
                    folder.RegisterTaskDefinition(Name, def, TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN);
                    return null;
                }
            }
        }
        catch (Exception e)
        {
            return e.Message;
        }
        return null;
    }

    /// <summary>Remove a registration; absent entries count as success.</summary>
    public static string? Unregister(AutoStartMethod m)
    {
        try
        {
            switch (m)
            {
                case AutoStartMethod.Run:
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                    key?.DeleteValue(Name, throwOnMissingValue: false);
                    return null;
                }
                case AutoStartMethod.Startup:
                {
                    var lnk = ShortcutPath();
                    if (File.Exists(lnk)) File.Delete(lnk);
                    return null;
                }
                case AutoStartMethod.Task:
                {
                    dynamic svc = ConnectScheduler();
                    dynamic folder = svc.GetFolder(TaskFolder);
                    try { folder.DeleteTask(Name, 0); }
                    catch (Exception e) when (IsTaskMissing(e)) { }
                    return null;
                }
            }
        }
        catch (Exception e)
        {
            return e.Message;
        }
        return null;
    }

    /// <summary>
    /// Apply the desired method combination: register missing entries, remove unselected ones, and
    /// rewrite entries whose path or arguments changed (moved installation, silent toggle).
    /// Returns the first error, or null when every operation succeeded.
    /// </summary>
    public static string? Apply(IEnumerable<AutoStartMethod> methods, bool silent)
    {
        var want = methods.ToHashSet();
        string? firstError = null;
        if (want.Count > 0 && !TryEngineExe(out _, out var targetError)) firstError = targetError;
        foreach (var m in All())
        {
            try
            {
                var read = TryRegisteredTarget(m);
                if (read.Error != null)
                {
                    firstError ??= $"{MethodKey(m)}: {read.Error}";
                    continue;
                }
                var cur = read.Target;
                if (want.Contains(m))
                {
                    if (!IsCurrent(m, silent))
                    {
                        var err = Register(m, silent);
                        if (err != null && firstError == null) firstError = $"{MethodKey(m)}: {err}";
                    }
                }
                else if (cur != null)
                {
                    var err = Unregister(m);
                    if (err != null && firstError == null) firstError = $"{MethodKey(m)}: {err}";
                }
            }
            catch (Exception e)
            {
                if (firstError == null) firstError = $"{MethodKey(m)}: {e.Message}";
            }
        }
        return firstError;
    }

    /// <summary>Methods actually registered against the current main engine and desired arguments.</summary>
    public static List<AutoStartMethod> CurrentMethods(bool silent) => All().Where(m => IsCurrent(m, silent)).ToList();

    /// <summary>Status snapshot for the settings UI: per-method actual registration state.</summary>
    public static object Status(bool silent)
    {
        var hasExe = TryEngineExe(out var exe, out var exeError);
        return new
        {
            exe,
            error = exeError,
            silent,
            methods = All().Select(m =>
            {
                var read = TryRegisteredTarget(m);
                var t = read.Target;
                return new
                {
                    key = MethodKey(m),
                    registered = t != null,
                    current = read.Error == null && hasExe && t != null && PathsEqual(t.Value.Path, exe)
                        && string.Equals(t.Value.Args.Trim(), BuildArguments(silent).Trim(), StringComparison.OrdinalIgnoreCase),
                    command = t == null ? "" : $"\"{t.Value.Path}\" {t.Value.Args}".Trim(),
                    error = read.Error,
                };
            }).ToArray(),
        };
    }

    static string ShortcutPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Start Menu", "Programs", "Startup", $"{Name}.lnk");

    static dynamic ConnectScheduler()
    {
        dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        svc.Connect();
        return svc;
    }

    /// <summary>Split a Run-value command into (path, arguments); tolerates unquoted paths without spaces.</summary>
    static (string, string) ParseCommand(string v)
    {
        v = v.Trim();
        if (v.StartsWith('"'))
        {
            var end = v.IndexOf('"', 1);
            if (end > 0)
                return (v[1..end], v[(end + 1)..].Trim());
        }
        var sp = v.IndexOf(' ');
        return sp < 0 ? (v, "") : (v[..sp], v[(sp + 1)..].Trim());
    }

    static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }
}
