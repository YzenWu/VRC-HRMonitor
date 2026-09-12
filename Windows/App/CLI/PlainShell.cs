using HeartRateMonitor.Core;

namespace HeartRateMonitor.Cli;

internal static class PlainShell
{
    public static void Run(CliSession session, bool banner, bool prompt)
    {
        session.SubscribeEvents();
        if (banner)
        {
            CliUi.Banner();
            CliUi.Print(CommandShell.Execute("status"));
            CliUi.Print(CliApp.ProjectInfoLines());
            Console.WriteLine();
        }
        while (true)
        {
            FlushEvents(session);
            if (prompt) CliUi.Prompt();
            var line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();
            if (line.Length == 0) continue;
            if (CliSession.IsExit(line)) break;
            if (line.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                if (prompt) { try { Console.Clear(); } catch { } if (banner) CliUi.Banner(); }
                continue;
            }
            CliUi.Print(session.Execute(line));
        }
        FlushEvents(session);
    }

    static void FlushEvents(CliSession session)
    {
        foreach (var e in session.Events.Drain()) CliUi.Event(e.Tag, e.Text);
    }
}