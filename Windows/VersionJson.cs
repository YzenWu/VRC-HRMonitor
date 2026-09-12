// Shared source file (P2): linked into all four executables (engine / webui / cli / dump) so the
// main program can verify every component's build version before starting any service.
// Global namespace on purpose - each host compiles it into its own assembly.

using System.Diagnostics;

internal static class VersionJson
{
    /// <summary>Handle the --version-json early exit: print one-line component metadata and return true.
    /// Must run before any service, window, or single-instance guard so probes stay cheap and side-effect free.</summary>
    public static bool TryRun(string[] args, string component)
    {
        if (!args.Any(a => a.Equals("--version-json", StringComparison.OrdinalIgnoreCase))) return false;
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        var vi = FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? typeof(VersionJson).Assembly.Location);
        Console.WriteLine(
            "{\"component\":\"" + component +
            "\",\"exe\":\"" + Path.GetFileName(Environment.ProcessPath ?? "") +
            "\",\"version\":\"" + Esc(vi.ProductVersion) +
            "\",\"fileVersion\":\"" + Esc(vi.FileVersion) + "\"}");
        return true;
    }

    static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
