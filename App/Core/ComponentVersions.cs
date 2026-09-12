using System.Diagnostics;
using System.Text.Json;

namespace HeartRateMonitor.Core;

/// <summary>
/// Runtime component version check (P2): compares the four shipped executables against the embedded
/// release manifest before any business service starts. This is install-integrity only and is never
/// confused with the online update check, which compares against GitHub releases.
/// </summary>
public static class ComponentVersions
{
    /// <summary>Per-component check outcome; Ok requires the file to exist, probe cleanly, and match the manifest.</summary>
    public sealed class Report
    {
        /// <summary>Component role: engine / webui / cli / dump.</summary>
        public required string Role;
        /// <summary>Executable file name beside the engine.</summary>
        public required string ExeName;
        /// <summary>Version declared by the embedded manifest (components.role).</summary>
        public required string Declared;
        /// <summary>Reported product version ("" when the probe failed).</summary>
        public string Actual = "";
        /// <summary>Reported file version, for diagnostics.</summary>
        public string FileVersion = "";
        /// <summary>Whether the executable exists beside the engine.</summary>
        public bool Exists;
        /// <summary>Probe failure description (missing / timeout / parse error).</summary>
        public string? Error;

        public bool Ok => Exists && Error == null && Normalize(Actual) == Normalize(Declared);
    }

    static readonly (string Role, string Exe)[] Components =
    {
        ("engine", "HeartRateMonitor.exe"),
        ("webui", "hrm-webui.exe"),
        ("cli", "hrmcli.exe"),
        ("dump", "hrmdump.exe"),
    };

    /// <summary>Check every component in parallel; the engine reads its own version, the others are probed.</summary>
    public static List<Report> CheckAll()
    {
        var tasks = Components.Select(c => Task.Run(() => Check(c.Role, c.Exe))).ToArray();
        Task.WaitAll(tasks);
        return tasks.Select(t => t.Result).ToList();
    }

    /// <summary>Check one component: existence, probe (--version-json subprocess), and manifest comparison.</summary>
    public static Report Check(string role, string exeName)
    {
        var r = new Report { Role = role, ExeName = exeName, Declared = ReleaseManifest.ComponentVersion(role) ?? "" };
        var exe = Path.Combine(App.ExeDir, exeName);
        if (!File.Exists(exe))
        {
            r.Error = "missing";
            return r;
        }
        r.Exists = true;

        if (role == "engine")
        {
            // This process is the engine: read the version directly instead of spawning a subprocess.
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exe);
                r.Actual = vi.ProductVersion ?? "";
                r.FileVersion = vi.FileVersion ?? "";
            }
            catch (Exception e) { r.Error = e.Message; }
            return r;
        }

        try
        {
            var psi = new ProcessStartInfo(exe, "--version-json")
            {
                WorkingDirectory = App.ExeDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            var exitTask = p.WaitForExitAsync();
            if (Task.WhenAny(exitTask, Task.Delay(5000)).GetAwaiter().GetResult() != exitTask)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                try { p.WaitForExit(); } catch { }
                r.Error = "timeout";
                return r;
            }
            exitTask.GetAwaiter().GetResult();
            var json = stdoutTask.GetAwaiter().GetResult();
            _ = stderrTask.GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var actualRole = GetString(root, "component");
            var actualExe = GetString(root, "exe");
            r.Actual = GetString(root, "version");
            r.FileVersion = GetString(root, "fileVersion");
            if (!actualRole.Equals(role, StringComparison.OrdinalIgnoreCase))
                r.Error = $"component mismatch: {actualRole}";
            else if (!actualExe.Equals(exeName, StringComparison.OrdinalIgnoreCase))
                r.Error = $"exe mismatch: {actualExe}";
            else if (r.Actual.Length == 0)
                r.Error = "no version reported";
        }
        catch (Exception e)
        {
            r.Error = e.Message;
        }
        return r;
    }

    static string GetString(JsonElement root, string key)
        => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>ProductVersion without the SDK's "+githash" suffix, for display and comparison.</summary>
    static string Strip(string v) => (v ?? "").Trim().Split('+')[0];

    /// <summary>Trailing ".0" segments are insignificant when comparing manifest declarations with file versions.</summary>
    public static string Normalize(string v)
    {
        // The .NET SDK appends the source revision (git hash) to ProductVersion as "+hash"; drop it.
        var plain = (v ?? "").Trim().Split('+')[0];
        var list = plain.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        while (list.Count > 2 && list[^1] == "0") list.RemoveAt(list.Count - 1);
        return string.Join(".", list);
    }
}
