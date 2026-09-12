namespace HeartRateMonitor.Core;

/// <summary>
/// Centralized startup option parsing (final-polish P1): one authoritative model shared by every
/// entry point instead of scattered <c>args.Any(...)</c> checks. Mode resolution policy:
/// default is GUI; only an explicit <c>--cli</c> selects the CLI (the legacy parent-console
/// heuristic was removed), and <c>--gui</c>/<c>--web</c> explicitly force the GUI.
/// </summary>
public sealed class StartupOptions
{
    /// <summary>Explicit GUI request (--gui or the legacy --web alias).</summary>
    public bool Gui;
    /// <summary>Explicit CLI request (--cli).</summary>
    public bool Cli;
    /// <summary>Old WinForms UI request (--winforms), also counts as GUI.</summary>
    public bool WinForms;
    /// <summary>Silent start (--silent): engine, tray, and services only; the frontend is not launched.</summary>
    public bool Silent;
    /// <summary>Launched by an autostart registration (--autostart): yield silently when another instance runs.</summary>
    public bool AutoStart;
    /// <summary>Safe mode (--safemode): skip every automatic startup action.</summary>
    public bool SafeMode;
    /// <summary>Reboot relay (--reboot): reconnect the last session's devices regardless of AutoConnect.</summary>
    public bool Reboot;
    /// <summary>Relaunched after a crash (--after-crash), for startup logging.</summary>
    public bool AfterCrash;
    /// <summary>Debug mode (--debug / -d).</summary>
    public bool Debug;
    /// <summary>Trace level (--trace), read once at startup.</summary>
    public bool Trace;
    /// <summary>Allow multiple instances (--multi).</summary>
    public bool Multi;
    /// <summary>Force the plain command shell instead of the menu TUI (--shell).</summary>
    public bool Shell;

    /// <summary>The command token and every argument following it, preserved verbatim.</summary>
    public List<string> CommandArgs = new();
    /// <summary>Compatibility alias for older callers.</summary>
    public List<string> Rest => CommandArgs;

    /// <summary>True when any explicit GUI selection was given.</summary>
    public bool ForceGui => Gui || WinForms;

    /// <summary>Parse startup options until the first command token. Everything after that token is command input.</summary>
    public static StartupOptions Parse(string[] args)
    {
        var o = new StartupOptions();
        var options = true;
        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i] ?? "";
            if (!options)
            {
                o.CommandArgs.Add(raw);
                continue;
            }
            var a = raw.Trim();
            if (a.Length == 0) continue;
            if (a == "--")
            {
                options = false;
                continue;
            }
            switch (a.ToLowerInvariant())
            {
                case "--gui":
                case "--web":
                    o.Gui = true; break;
                case "--cli":
                    o.Cli = true; break;
                case "--shell":
                    o.Shell = true; break;
                case "--winforms":
                    o.WinForms = true; break;
                case "--silent":
                case "--minimized":
                    o.Silent = true; break;
                case "--autostart":
                case "--startup":
                    o.AutoStart = true; break;
                case "--safemode":
                    o.SafeMode = true; break;
                case "--reboot":
                    o.Reboot = true; break;
                case "--after-crash":
                    o.AfterCrash = true; break;
                case "--debug":
                case "-d":
                    o.Debug = true; break;
                case "--trace":
                    o.Trace = true; break;
                case "--multi":
                    o.Multi = true; break;
                default:
                    // The first unrecognized token is the command, even when it begins with '-'.
                    // This preserves one-shot compatibility and ensures all following arguments remain verbatim.
                    options = false;
                    o.CommandArgs.Add(raw);
                    break;
            }
        }
        return o;
    }
}
