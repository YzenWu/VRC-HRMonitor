using HeartRateMonitor;
using HeartRateMonitor.Cli;
using HeartRateMonitor.Core;

namespace HrmCli;

/// <summary>
/// hrmcli.exe is a standalone CLI executable.
/// It is fully equivalent to `HeartRateMonitor.exe --cli`: the same <see cref="AppBoot"/> initialization,
/// <see cref="CliApp"/> REPL, and <see cref="CommandShell"/> command implementation.
/// The only differences are that this process has its own console (no AttachConsole needed) and does not start a WinForms message loop.
/// </summary>
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // P2: cheap component probe used by the version check; exits before any service starts.
        if (VersionJson.TryRun(args, "cli")) return 0;

        var options = StartupOptions.Parse(args);
        AppBoot.Init(args);
        App.Log.Info(LogText.L("log.cli.start"));
        try
        {
            CliApp.Run(options);
            return Environment.ExitCode;
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.cli.unhandled", e));
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        finally
        {
            try { App.Ble.StopScan(); } catch { }
            try { App.Ble.DisconnectAll(); } catch { }
            try { App.Web?.Stop(); } catch { }
            try { App.Hub?.Stop(); } catch { }
            try { App.Osc.Stop(); } catch { }
            App.Log.Info(LogText.L("log.cli.exit"));
        }
    }
}
