using HeartRateMonitor.Core;

namespace HeartRateMonitor.Cli;

/// <summary>Shared CLI entry point for one-shot, plain shell/batch, and interactive menu modes.</summary>
public static class CliApp
{
    public static void Run(string[] args) => Run(StartupOptions.Parse(args));

    public static void Run(StartupOptions options)
    {
        using var console = NativeConsole.Create();
        CliUi.Init(console != null);
        using var session = new CliSession();
        var oneShot = options.CommandArgs.Count > 0;
        var redirected = Console.IsInputRedirected || Console.IsOutputRedirected;
        if (!session.Start(interactive: !oneShot && !redirected)) return;

        if (oneShot)
        {
            CliUi.Print(session.Execute(options.CommandArgs));
            return;
        }

        if (options.Shell || redirected)
        {
            PlainShell.Run(session, banner: !redirected, prompt: !redirected);
            return;
        }

        if (console == null)
        {
            PlainShell.Run(session, banner: true, prompt: true);
            return;
        }
        new MenuTui(session, console).Run();
    }

    internal static List<string> ProjectInfoLines()
    {
        var lines = new List<string>
        {
            Txt.T("gh.project", ReleaseManifest.RepoName.Length > 0 ? ReleaseManifest.RepoName : "-"),
            Txt.T("gh.release", ReleaseManifest.ReleaseName, ReleaseManifest.Version, ReleaseManifest.BuildTimeUtc),
        };
        if (ReleaseManifest.BuildNote.Length > 0) lines.Add(Txt.T("gh.note", ReleaseManifest.BuildNote));
        if (ReleaseManifest.BuildTarget.Length > 0) lines.Add(Txt.T("gh.target", ReleaseManifest.BuildTarget));
        var s = GitHubProjectService.Current;
        if (s == null) return lines;
        var sha = s.LatestCommitSha.Length >= 7 ? s.LatestCommitSha[..7] : s.LatestCommitSha;
        lines.Add(Txt.T("gh.github", s.Stars, s.IssuesOpen, s.IssuesTotal, sha, s.Stale ? Txt.T("gh.stale") : ""));
        if (s.LatestRelease != null)
        {
            var rel = s.LatestRelease;
            var state = GitHubProjectService.IsCurrentRelease(rel) ? Txt.T("gh.uptodate")
                : GitHubProjectService.IsSkipped(rel) ? Txt.T("gh.skipped") : Txt.T("gh.newer", rel.Tag);
            lines.Add(Txt.T("gh.latest", rel.Tag, rel.Name, state));
        }
        return lines;
    }
}