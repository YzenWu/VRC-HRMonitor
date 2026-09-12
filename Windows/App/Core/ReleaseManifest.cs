using System.Reflection;
using System.Text.Json;

namespace HeartRateMonitor.Core;

/// <summary>
/// Release manifest (Windows/Release.json, #23/#34, final-polish P0): the single source of truth for
/// release metadata. The build embeds the repo manifest into the assembly as an embedded resource, so
/// the product no longer ships the file. Every string falls back to empty when absent, keeping
/// downstream features functional.
/// </summary>
public static class ReleaseManifest
{
    /// <summary>Embedded resource name declared by the csproj (LogicalName).</summary>
    const string ResourceName = "HeartRateMonitor.Release.json";

    /// <summary>Lenient parsing with // comments and trailing commas because the manifest begins with an MIT declaration comment.</summary>
    private static readonly JsonDocumentOptions Opts = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static JsonElement? _root;

    private static JsonElement? Root
    {
        get
        {
            if (_root != null) return _root;
            _root = LoadRoot();
            return _root;
        }
    }

    /// <summary>
    /// Load the manifest: embedded resource first (product layout), then Release.json beside the
    /// executable as a dev/pinned override. A damaged or missing source degrades to an empty manifest.
    /// </summary>
    static JsonElement? LoadRoot()
    {
        try
        {
            using var s = typeof(ReleaseManifest).Assembly.GetManifestResourceStream(ResourceName);
            if (s != null)
            {
                using var doc = JsonDocument.Parse(ReadAll(s), Opts);
                // Clone keeps JsonElement readable after doc is disposed by detaching it from the document lifetime.
                return doc.RootElement.Clone();
            }
        }
        catch { }
        try
        {
            var file = Path.Combine(App.ExeDir, "Release.json");
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), Opts);
                return doc.RootElement.Clone();
            }
        }
        catch { }
        return null;
    }

    static string ReadAll(Stream s)
    {
        using var r = new StreamReader(s, leaveOpen: true);
        return r.ReadToEnd();
    }

    /// <summary>Get a top-level string field; return null when undeclared, not a string, or the manifest is missing.</summary>
    public static string? String(string key)
    {
        var v = Root;
        if (v == null || v.Value.ValueKind != JsonValueKind.Object) return null;
        return v.Value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }

    /// <summary>Declared version for a component role (engine/webui/cli/dump); null when undeclared.</summary>
    public static string? ComponentVersion(string role)
    {
        var v = Root;
        if (v == null || v.Value.ValueKind != JsonValueKind.Object) return null;
        return v.Value.TryGetProperty("components", out var comp)
            && comp.ValueKind == JsonValueKind.Object
            && comp.TryGetProperty(role, out var p)
            && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }

    /// <summary>Declared relative icon path for a role (icons section); null when undeclared.</summary>
    public static string? Icon(string role)
    {
        var v = Root;
        if (v == null || v.Value.ValueKind != JsonValueKind.Object) return null;
        return v.Value.TryGetProperty("icons", out var icons)
            && icons.ValueKind == JsonValueKind.Object
            && icons.TryGetProperty(role, out var p)
            && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }

    /// <summary>Product display name, such as HeartRateMonitor.</summary>
    public static string ProjectName => String("project_name") ?? "";
    /// <summary>Repository home page, such as https://github.com/yzenwu/VRC-HRMonitor.</summary>
    public static string Repo => String("repo") ?? "";
    /// <summary>Repository name in owner/name form, such as yzenwu/VRC-HRMonitor.</summary>
    public static string RepoName => String("repo_name") ?? "";
    /// <summary>Author display name.</summary>
    public static string Author => String("author") ?? "";
    /// <summary>Author profile URL (GitHub profile).</summary>
    public static string AuthorUrl => String("author_url") ?? "";
    /// <summary>Thumbnail relative to the executable directory, such as image/icon.svg; consumers handle missing files.</summary>
    public static string Thumbnail => String("thumbnail") ?? "";
    /// <summary>Release display name from Release.json.release_name (the local value for update comparisons).</summary>
    public static string ReleaseName => String("release_name") ?? "";
    /// <summary>Release version from Release.json.release_version.</summary>
    public static string Version => String("release_version") ?? "";
    /// <summary>Build note edited in Release.json; shown by the CLI banner and About page.</summary>
    public static string BuildNote => String("build_note") ?? "";
    /// <summary>Build target/platform declared in the manifest (default x64).</summary>
    public static string BuildTarget => String("build_target") ?? "";
    /// <summary>Release asset name pattern used to match download assets, such as HeartRateMonitor-*-x64.zip.</summary>
    public static string AssetPattern => String("asset_pattern") ?? "";

    /// <summary>
    /// UTC build time in ISO-8601. The AssemblyMetadata value injected by build.ps1 wins (the manifest
    /// stores the literal "auto"); build.ps1 replaces pinned values at build time.
    /// </summary>
    public static string BuildTimeUtc
    {
        get
        {
            try
            {
                var v = typeof(ReleaseManifest).Assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "ReleaseBuildTimeUtc")?.Value;
                if (!string.IsNullOrWhiteSpace(v) && v != "auto") return v;
            }
            catch { }
            return String("release_build_time_utc") ?? "";
        }
    }

    /// <summary>License identifier, such as MIT.</summary>
    public static string License => String("license") ?? "";
    /// <summary>License text location (repository LICENSE link or local file).</summary>
    public static string LicenseContext => String("license_context") ?? "";

    /// <summary>Component versions declared in the manifest (P2 runtime integrity check).</summary>
    public static string? EngineVersion => ComponentVersion("engine");
    /// <summary>Web shell component version.</summary>
    public static string? WebUiVersion => ComponentVersion("webui");
    /// <summary>CLI component version.</summary>
    public static string? CliVersion => ComponentVersion("cli");
    /// <summary>Crash watchdog component version.</summary>
    public static string? DumpVersion => ComponentVersion("dump");

    /// <summary>Frontend /api payload used by the About page and update checks.</summary>
    public static object Json() => new
    {
        projectName = ProjectName,
        repo = Repo,
        repoName = RepoName,
        author = Author,
        authorUrl = AuthorUrl,
        thumbnail = Thumbnail,
        releaseName = ReleaseName,
        releaseVersion = Version,
        buildTimeUtc = BuildTimeUtc,
        buildNote = BuildNote,
        buildTarget = BuildTarget,
        assetPattern = AssetPattern,
        license = License,
        licenseContext = LicenseContext,
        components = new
        {
            engine = EngineVersion ?? "",
            webui = WebUiVersion ?? "",
            cli = CliVersion ?? "",
            dump = DumpVersion ?? "",
        },
    };
}
