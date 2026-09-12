using System.Net;
using System.Text.Json;

namespace HeartRateMonitor.Core;

/// <summary>
/// GitHub project information service (P3): fetches repository stats, the latest commit, issue counts,
/// and the latest release from api.github.com for the About page and the startup update check.
/// Every request is timeout-bounded and runs in the background; failures keep serving the last
/// successful cache (marked stale) and never block startup. ETags from the previous fetch turn
/// unchanged endpoints into free 304s, keeping the unauthenticated rate budget healthy.
/// </summary>
public static class GitHubProjectService
{
    /// <summary>Latest release as reported by GitHub.</summary>
    public sealed class Release
    {
        public string Tag = "";
        public string Name = "";
        public string Body = "";
        public string Url = "";
        /// <summary>Asset names in this release.</summary>
        public List<string> AssetNames = new();
        /// <summary>Browser download URLs by asset name.</summary>
        public Dictionary<string, string> AssetUrls = new();
        public string PublishedAt = "";
    }

    /// <summary>Aggregated project snapshot for the frontend and the update check.</summary>
    public sealed class Snapshot
    {
        public string Repo = "";
        public int Stars;
        public int Forks;
        public string LatestCommitSha = "";
        public string LatestCommitTime = "";
        public int IssuesTotal;
        public int IssuesOpen;
        public Release? LatestRelease;
        /// <summary>ISO-8601 UTC time of the successful fetch this data came from.</summary>
        public string FetchedAt = "";
        /// <summary>Empty on success; otherwise the failure that kept the cache in use.</summary>
        public string Error = "";
        /// <summary>True when this snapshot is the cache served after a failed refresh.</summary>
        public bool Stale;
    }

    /// <summary>Raised on a background thread after every refresh attempt (GUI uses it for the update prompt).</summary>
    public static event Action? Updated;

    /// <summary>Cache file layout: per-endpoint bodies plus their ETags, written atomically to the data directory.</summary>
    sealed class CacheFile
    {
        public string FetchedAt { get; set; } = "";
        public Dictionary<string, string> Etags { get; set; } = new();
        public Dictionary<string, string> Bodies { get; set; } = new();
    }

    enum FetchResult { Success, NotFound, Failed }

    static readonly HttpClient Http;
    static readonly object Lock = new();
    static readonly SemaphoreSlim RefreshGate = new(1, 1);
    static Snapshot? _current;
    static CacheFile _cache = new();
    static int _started;

    const string ApiRoot = "https://api.github.com/";

    static GitHubProjectService()
    {
        Http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("HeartRateMonitor-ProjectInfo");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Current snapshot (possibly stale or null before the first fetch).</summary>
    public static Snapshot? Current { get { lock (Lock) return _current; } }

    /// <summary>Repository slug (owner/name) from the manifest; empty when unpublished.</summary>
    public static string Slug()
    {
        var name = ReleaseManifest.RepoName.Trim();
        if (name.Length > 0) return name;
        var url = ReleaseManifest.Repo.Trim();
        var m = System.Text.RegularExpressions.Regex.Match(url, @"github\.com/([^/]+/[^/]+?)(?:\.git)?/?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>Load the cache once and optionally start the background refresh.</summary>
    public static void Start(bool allowNetwork = true)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
        LoadCache();
        if (allowNetwork) _ = Task.Run(RefreshAsync);
    }

    /// <summary>
    /// Refresh every endpoint sequentially (rate-limit friendly), honouring ETags. A 404 on the
    /// latest-release endpoint means "no releases" rather than an error; failed endpoints reuse their
    /// cached bodies, or preserve the corresponding field from the previous snapshot when uncached.
    /// </summary>
    public static async Task<Snapshot> RefreshAsync()
    {
        await RefreshGate.WaitAsync();
        try
        {
            var slug = Slug();
            var snap = SnapshotForRefresh(slug);
            if (slug.Length == 0)
            {
                snap.Error = "no repository";
                snap.Stale = true;
                Store(snap);
                return snap;
            }

            var bodies = new Dictionary<string, string>();
            var etags = new Dictionary<string, string>();
            var errors = new List<string>();

            var repo = await Fetch("repo", $"repos/{slug}", bodies, etags, errors);
            var latest = await Fetch("latest", $"repos/{slug}/releases/latest", bodies, etags, errors, notFoundIsEmpty: true);
            var commit = await Fetch("commit", $"repos/{slug}/commits?per_page=1", bodies, etags, errors);
            var issues = await Fetch("issues", $"search/issues?q=repo:{slug}+type:issue", bodies, etags, errors);
            var issuesOpen = await Fetch("issues_open", $"search/issues?q=repo:{slug}+type:issue+state:open", bodies, etags, errors);

            Parse(bodies, snap);
            if (latest == FetchResult.NotFound) snap.LatestRelease = null;

            var complete = repo != FetchResult.Failed
                && latest != FetchResult.Failed
                && commit != FetchResult.Failed
                && issues != FetchResult.Failed
                && issuesOpen != FetchResult.Failed;
            string cachedAt;
            lock (Lock) cachedAt = _cache.FetchedAt;
            snap.FetchedAt = complete
                ? DateTime.UtcNow.ToString("o")
                : (cachedAt.Length > 0 ? cachedAt : snap.FetchedAt);
            snap.Error = complete ? "" : string.Join("; ", errors.Count > 0 ? errors : new[] { "incomplete data" });
            snap.Stale = !complete;

            lock (Lock)
            {
                _cache = new CacheFile { FetchedAt = snap.FetchedAt, Etags = etags, Bodies = bodies };
            }
            SaveCache();
            Store(snap);
            return snap;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    /// <summary>One endpoint fetch with If-None-Match; failures prefer a validated cached body.</summary>
    static async Task<FetchResult> Fetch(string key, string path, Dictionary<string, string> bodies,
        Dictionary<string, string> etags, List<string> errors, bool notFoundIsEmpty = false)
    {
        string? previousEtag;
        lock (Lock) _cache.Etags.TryGetValue(key, out previousEtag);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ApiRoot + path);
            if (!string.IsNullOrEmpty(previousEtag)) req.Headers.IfNoneMatch.ParseAdd(previousEtag);

            using var resp = await Http.SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.NotModified)
            {
                if (UseCached(key, bodies, etags)) return FetchResult.Success;
                errors.Add($"{key}: 304 without cache");
                return FetchResult.Failed;
            }
            if (resp.StatusCode == HttpStatusCode.NotFound && notFoundIsEmpty)
                return FetchResult.NotFound;
            if (!resp.IsSuccessStatusCode)
            {
                errors.Add($"{key}: {(int)resp.StatusCode}");
                UseCached(key, bodies, etags);
                return FetchResult.Failed;
            }

            var text = await resp.Content.ReadAsStringAsync();
            if (text.Length == 0 || !IsValidBody(key, text))
            {
                errors.Add($"{key}: invalid response");
                UseCached(key, bodies, etags);
                return FetchResult.Failed;
            }
            bodies[key] = text;
            var etag = resp.Headers.ETag?.Tag;
            if (!string.IsNullOrEmpty(etag)) etags[key] = etag;
            return FetchResult.Success;
        }
        catch (Exception e)
        {
            errors.Add($"{key}: {e.Message}");
            UseCached(key, bodies, etags);
            return FetchResult.Failed;
        }
    }

    static bool UseCached(string key, Dictionary<string, string> bodies, Dictionary<string, string> etags)
    {
        string? body;
        string? etag;
        lock (Lock)
        {
            _cache.Bodies.TryGetValue(key, out body);
            _cache.Etags.TryGetValue(key, out etag);
        }
        if (string.IsNullOrEmpty(body) || !IsValidBody(key, body)) return false;
        bodies[key] = body;
        if (!string.IsNullOrEmpty(etag)) etags[key] = etag;
        return true;
    }

    static Snapshot SnapshotForRefresh(string slug)
    {
        lock (Lock)
        {
            if (_current == null) return new Snapshot { Repo = slug };
            return new Snapshot
            {
                Repo = slug,
                Stars = _current.Stars,
                Forks = _current.Forks,
                LatestCommitSha = _current.LatestCommitSha,
                LatestCommitTime = _current.LatestCommitTime,
                IssuesTotal = _current.IssuesTotal,
                IssuesOpen = _current.IssuesOpen,
                LatestRelease = _current.LatestRelease,
                FetchedAt = _current.FetchedAt,
            };
        }
    }

    static bool IsValidBody(string key, string text)
        => TryDoc(text, out var value) && ParseEndpoint(key, value, new Snapshot());

    static void Parse(Dictionary<string, string> bodies, Snapshot snap)
    {
        foreach (var pair in bodies)
        {
            if (TryDoc(pair.Value, out var value)) ParseEndpoint(pair.Key, value, snap);
        }
    }

    static bool ParseEndpoint(string key, JsonElement value, Snapshot snap)
    {
        if (key == "repo")
        {
            if (!TryGetInt(value, "stargazers_count", out var stars)
                || !TryGetInt(value, "forks_count", out var forks)) return false;
            snap.Stars = stars;
            snap.Forks = forks;
            return true;
        }
        if (key == "commit")
        {
            if (value.ValueKind != JsonValueKind.Array) return false;
            if (value.GetArrayLength() == 0)
            {
                snap.LatestCommitSha = "";
                snap.LatestCommitTime = "";
                return true;
            }
            var first = value[0];
            var sha = GetString(first, "sha");
            if (sha.Length == 0) return false;
            snap.LatestCommitSha = sha;
            snap.LatestCommitTime = GetString(first, "commit", "author", "date");
            return true;
        }
        if (key == "issues" || key == "issues_open")
        {
            if (!TryGetInt(value, "total_count", out var count)) return false;
            if (key == "issues") snap.IssuesTotal = count;
            else snap.IssuesOpen = count;
            return true;
        }
        if (key != "latest" || value.ValueKind != JsonValueKind.Object) return false;
        var tag = GetString(value, "tag_name");
        if (tag.Length == 0) return false;
        var release = new Release
        {
            Tag = tag,
            Name = GetString(value, "name"),
            Body = GetString(value, "body"),
            Url = GetString(value, "html_url"),
            PublishedAt = GetString(value, "published_at"),
        };
        if (value.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = GetString(asset, "name");
                var url = GetString(asset, "browser_download_url");
                if (name.Length == 0) continue;
                release.AssetNames.Add(name);
                if (url.Length > 0) release.AssetUrls[name] = url;
            }
        }
        snap.LatestRelease = release;
        return true;
    }

    static void Store(Snapshot snap)
    {
        lock (Lock) _current = snap;
        try { Updated?.Invoke(); } catch { }
    }

    static string CachePath => Path.Combine(App.DataDir, "github_cache.json");

    static void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return;
            var cfg = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath));
            if (cfg == null) return;
            cfg.Etags ??= new();
            cfg.Bodies ??= new();
            lock (Lock) _cache = cfg;
            var snap = new Snapshot { Repo = Slug(), FetchedAt = cfg.FetchedAt, Stale = true, Error = "cached" };
            Parse(cfg.Bodies, snap);
            lock (Lock) _current = snap;
        }
        catch { }
    }

    static void SaveCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string json;
            lock (Lock) json = JsonSerializer.Serialize(_cache);
            var tmp = CachePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(CachePath))
            {
                try { File.Replace(tmp, CachePath, CachePath + ".bak"); }
                catch (IOException) { File.Move(tmp, CachePath, true); }
            }
            else
            {
                File.Move(tmp, CachePath);
            }
        }
        catch { }
    }

    // ---- Update comparison ----

    /// <summary>True when the GitHub latest release matches the local manifest identity:
    /// the tag equals the manifest version (a leading v is ignored) and the name matches the manifest
    /// release name (an empty GitHub name counts as matching).</summary>
    public static bool IsCurrentRelease(Release rel)
    {
        var tag = rel.Tag.Trim();
        if (tag.StartsWith('v') || tag.StartsWith('V')) tag = tag[1..];
        var tagOk = string.Equals(tag, ReleaseManifest.Version.Trim(), StringComparison.OrdinalIgnoreCase);
        var nameOk = rel.Name.Trim().Length == 0
            || string.Equals(rel.Name.Trim(), ReleaseManifest.ReleaseName.Trim(), StringComparison.OrdinalIgnoreCase);
        return tagOk && nameOk;
    }

    /// <summary>True when the user skipped exactly this release (tag and name).</summary>
    public static bool IsSkipped(Release rel)
        => string.Equals(rel.Tag.Trim(), App.Config.App.SkippedReleaseTag.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(rel.Name.Trim(), App.Config.App.SkippedReleaseName.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The asset whose name matches both the manifest asset_pattern and non-empty build_target, or null.</summary>
    public static string? MatchAsset(Release rel)
    {
        var pattern = ReleaseManifest.AssetPattern.Trim();
        if (pattern.Length == 0 || rel.AssetNames.Count == 0) return null;
        var rx = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\*", ".*") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var target = ReleaseManifest.BuildTarget.Trim();
        foreach (var name in rel.AssetNames)
        {
            if (rx.IsMatch(name) && (target.Length == 0 || name.Contains(target, StringComparison.OrdinalIgnoreCase)))
                return name;
        }
        return null;
    }

    // ---- JSON helpers ----

    static bool TryDoc(string text, out JsonElement el)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            el = doc.RootElement.Clone();
            return true;
        }
        catch { el = default; return false; }
    }

    static string GetString(JsonElement el, params string[] path)
    {
        var cur = el;
        foreach (var p in path)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out var next)) return "";
            cur = next;
        }
        return cur.ValueKind == JsonValueKind.String ? cur.GetString() ?? "" : "";
    }

    static bool TryGetInt(JsonElement el, string name, out int value)
    {
        value = 0;
        return el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(name, out var item)
            && item.ValueKind == JsonValueKind.Number
            && item.TryGetInt32(out value);
    }
}
