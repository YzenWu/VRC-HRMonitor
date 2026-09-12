using System.Security.Cryptography;
using System.Text.Json;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.Web;

/// <summary>
/// Second frontend (tele-Web) authentication: Local user library +Session.
/// Full local validation, not dependent on external services; covering three types of demand:
///   • User library: user name + password (PBKDF2XQ1QXZ, 20 + 16 bytes random salt) with a drop-down disk only salt/hash;
///   • Session: Login to issue 32 bytes random token (HttpOnly Cookie). ** The service only saves token of SHA256**,
///     The original token appeared only once in the response, and the leak of the landing file could not be used to forge Cookie;
///     UA fingerprinting (inherent to the browser, necessary for each request) and UA changes reject the request;
///   • Management: listing/destruction of other Session; adding, deleting, decrypting, changing roles (only admin can do).
/// The return ring (127.0.0.1) request is considered to be a trusted machine and automatically skips login - the local UI experience is consistent with the old version.
/// </summary>
public sealed class RemoteAuth
{
    private readonly string _usersPath;
    private readonly string _sessionsPath;
    private bool _usersLoadFailed;
    private readonly object _lock = new();
    private List<UserRow> _users = new();
    /// <summary> key = token (old token does not exist in memory and does not fall on disk). </summary>
    private readonly Dictionary<string, Session> _sessions = new();

    public static readonly string CookieName = "hrm_session";

    /// <summary> does not match the number of tolerances for token ' s continuous fingerprints, more than they are destroyed (avoiding to be kicked off a legitimate session with token+ wrong UA). </summary>
    private const int MaxFingerprintFails = 10;

    /// <summary> is not admin (user role) default non-accessible plates (Console/API Server/ Settings can modify or check sensitive content). </summary>
    public static readonly string[] RestrictedTabs = { "console", "apiserver", "settings" };

    public RemoteAuth(string baseDir)
    {
        _usersPath = Path.Combine(baseDir, "config_remote_users.json");
        _sessionsPath = Path.Combine(baseDir, "config_remote_sessions.json");
        LoadUsers();
        LoadSessions();
    }

    public sealed class UserRow
    {
        public string Username { get; set; } = "";
        public string Salt { get; set; } = "";
        public string Hash { get; set; } = "";
        /// Role of <summary>: admin (all plate + management authorization)/ user (limited by white list of plates). </summary>
        public string Role { get; set; } = "user";
        /// <summary> white list (empty = default value for the role; admin ignored). </summary>
        public List<string> Tabs { get; set; } = new();
        /// <summary>P6: the current password was set through the strength gate (>=8 chars with letters and digits). WAN stays blocked until an admin satisfies this.</summary>
        public bool Strong { get; set; }
    }

    public sealed class Session
    {
        /// <summary>SHA-256 hash of the session token. The original token is returned only once in the login response so the client can store it.</summary>
        public string TokenHash { get; set; } = "";
        public string Username { get; set; } = "";
        public string Role { get; set; } = "user";
        /// <summary>
        /// Fingerprint derived from the User-Agent header.
        /// Time zone, language, IP address, and cookies can change during normal use, so binding to them would invalidate valid sessions.
        /// User-Agent is stable for the browser and is included in every request, making it the most suitable binding value.
        /// </summary>
        public string Fingerprint { get; set; } = "";
        public DateTime Created { get; set; } = DateTime.Now;
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public string RemoteIp { get; set; } = "";
        /// <summary> does not match the number of consecutive fingerprints (no drop, process count). </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int FingerprintFails { get; set; }
    }

    // ---------------------------------------------------------------- Library

    private void LoadUsers()
    {
        try
        {
            if (File.Exists(_usersPath))
            {
                var rows = JsonSerializer.Deserialize<List<UserRow>>(File.ReadAllText(_usersPath),
                    JsonOpts());
                if (rows is { Count: > 0 }) { _users = rows; return; }
            }
        }
        catch (Exception e)
        {
            // Preserve a damaged credential store for recovery. Treating it as an empty store and overwriting
            // it would reopen first-user initialization while Remote may still be enabled.
            _usersLoadFailed = true;
            App.Log.Error(LogText.L("log.auth.users_load_fail", e.Message));
            return;
        }
        // P6: no auto-available admin/admin. An absent/empty store is valid; first Remote setup
        // runs the initialize-admin flow (Manage "init") instead of shipping default credentials.
        if (!File.Exists(_usersPath)) SaveUsers();
        App.Log.Info(LogText.L("log.auth.no_users", _usersPath));
    }

    public List<UserRow> Users() { lock (_lock) return _users.Select(u => Clone(u)).ToList(); }

    static UserRow Clone(UserRow u) => new()
    {
        Username = u.Username,
        Salt = u.Salt,
        Hash = u.Hash,
        Role = u.Role,
        Tabs = new List<string>(u.Tabs),
        Strong = u.Strong,
    };

    public void SaveUsers()
    {
        lock (_lock)
        {
            try { File.WriteAllText(_usersPath, JsonSerializer.Serialize(_users, JsonOpts())); }
            catch (Exception e) { App.Log.Error(LogText.L("log.auth.users_save_fail", e.Message)); }
        }
    }

    private static JsonSerializerOptions JsonOpts() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    // ---------------------------------------------------------------- Password

    static (string salt, string hash) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 200_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToHexString(salt), Convert.ToHexString(hash));
    }

    static bool Verify(string password, string saltHex, string hashHex)
    {
        try
        {
            var salt = Convert.FromHexString(saltHex);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 200_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(hashHex));
        }
        catch { return false; }
    }

    static UserRow CreateUser(string username, string password, string role, List<string>? tabs)
    {
        var (salt, hash) = HashPassword(password);
        return new UserRow
        {
            Username = username,
            Salt = salt,
            Hash = hash,
            Role = role,
            Tabs = tabs ?? new(),
            Strong = PasswordStrong(password),
        };
    }

    /// <summary>P6 strength gate for the WAN flow: at least 8 characters containing both a letter and a digit.</summary>
    public static bool PasswordStrong(string password)
        => password.Length >= 8
           && password.Any(char.IsLetter)
           && password.Any(char.IsDigit);

    /// <summary>P6: whether any account exists (empty store = first Remote enable must run admin initialization).</summary>
    public bool HasUsers { get { lock (_lock) return _users.Count > 0; } }

    /// <summary>Credential parsing failed; remote access must fail closed until the store is repaired.</summary>
    public bool UsersLoadFailed { get { lock (_lock) return _usersLoadFailed; } }

    /// <summary>P6: an admin account still verifies against the shipped default password "admin" and must be migrated before WAN.</summary>
    public bool AdminDefaultPassword
    {
        get
        {
            lock (_lock)
            {
                return _users.Any(u => u.Role == "admin" && Verify("admin", u.Salt, u.Hash));
            }
        }
    }

    /// <summary>P6 WAN precondition: every administrator has a strong, non-default password.</summary>
    public bool WanReady
    {
        get
        {
            lock (_lock)
            {
                var admins = _users.Where(u => u.Role == "admin").ToList();
                return admins.Count > 0 && admins.All(u => u.Strong && !Verify("admin", u.Salt, u.Hash));
            }
        }
    }

    /// <summary>Invalidates sessions for one account, optionally preserving the current request.</summary>
    public void InvalidateUserSessions(string username, string? keepTokenHash = null)
    {
        lock (_lock)
        {
            var stale = _sessions.Values.Where(s =>
                string.Equals(s.Username, username, StringComparison.OrdinalIgnoreCase)
                && s.TokenHash != keepTokenHash).ToList();
            foreach (var s in stale) _sessions.Remove(s.TokenHash);
            if (stale.Count > 0) SaveSessions();
        }
    }

    /// <summary>Invalidates every admin session except the given token hash; used after an admin password change so stale logins cannot enable WAN.</summary>
    public void KickAdminSessions(string? keepTokenHash)
    {
        lock (_lock)
        {
            var stale = _sessions.Values.Where(s => s.Role == "admin" && s.TokenHash != keepTokenHash).ToList();
            foreach (var s in stale) _sessions.Remove(s.TokenHash);
            if (stale.Count > 0) SaveSessions();
        }
    }

    // ---------------------------------------------------------------- Organisation

    /// <summary>
    /// The session list drops on the disc (re-entry is not required at a remote end after restart). Only SHA256 and UA fingerprints are in the document.
    /// There was no direct use of any evidence: neither was the document available nor could Cookie be forged.
    /// </summary>
    private void LoadSessions()
    {
        try
        {
            if (!File.Exists(_sessionsPath)) return;
            var rows = JsonSerializer.Deserialize<List<Session>>(File.ReadAllText(_sessionsPath), JsonOpts());
            if (rows == null) return;
            var idle = App.Config.Remote.IdleMinutes;
            lock (_lock)
            {
                foreach (var s in rows)
                {
                    if (string.IsNullOrEmpty(s.TokenHash)) continue;
                    // I'm out of time.
                    if (idle > 0 && DateTime.Now - s.LastSeen > TimeSpan.FromMinutes(idle)) continue;
                    // User deleted/renamed session cancelled
                    if (!_users.Any(u => string.Equals(u.Username, s.Username, StringComparison.OrdinalIgnoreCase))) continue;
                    s.FingerprintFails = 0;
                    _sessions[s.TokenHash] = s;
                }
            }
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.auth.sessions_load_fail", e.Message));
        }
    }

    /// The <summary> caller must have been holding _lock. </summary>
    private void SaveSessions()
    {
        try { File.WriteAllText(_sessionsPath, JsonSerializer.Serialize(_sessions.Values.ToList(), JsonOpts())); }
        catch (Exception e) { App.Log.Error(LogText.L("log.auth.sessions_save_fail", e.Message)); }
    }

    // ---------------------------------------------------------------- Session

    /// <summary> return ring request: Local UI is directly considered admin (lettered) and does not require login. Compatible with IPv6 mapping. </summary>
    public static bool IsLoopback(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        if (ip.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (ip.Equals("::ffff:127.0.0.1", StringComparison.OrdinalIgnoreCase)) return true;
        return System.Net.IPAddress.TryParse(ip, out var a) && System.Net.IPAddress.IsLoopback(a);
    }

    /// <summary>
    /// This machine (reciprocal ring) is subject to correspondence: it is not subject to any authentication and white list and is not included in a session form.
    /// Authentication only works as a non-returning ring source at the second front end.
    /// </summary>
    public static Session LocalSession(string ip) => new()
    {
        TokenHash = "",
        Username = "local",
        Role = "admin",
        RemoteIp = ip,
    };

    /// <summary>XQ1XZ hexadecimal (token shared with fingerprints). </summary>
    static string Sha(string s) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s ?? "")));

    /// <summary>Public SHA-256 hex helper: the WebServer derives the current session's token hash to preserve it while kicking others.</summary>
    public static string Sha256(string s) => Sha(s);

    /// <summary>
    /// Fingerprint of session: ** Only User-Agent**.
    /// Cookie/Time Zone/Language/IP was taken into account, and the result was "Cutting a Tab session is invalid" -
    /// Those values would have changed in their normal use (Cookie written, time zone, Wi-Fi for export IP).
    /// Matching them with the equivalent is equivalent to a random write-off. UA is the value inherent in the browser and necessary for each request.
    /// </summary>
    static string Fingerprint(string ua) => Sha(ua ?? "");

    /// <summary>
    /// Username + password new session. The returned Session.TokenHash is the storage key,
    /// Original token writes Cookie only once to the caller through out parameters, without remaining on any persistent layer.
    /// </summary>
    public Session? Login(string username, string password, string ua, string ip, out string token)
    {
        token = "";
        lock (_lock)
        {
            var u = _users.FirstOrDefault(x =>
                string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            if (u == null || !Verify(password, u.Salt, u.Hash))
            {
                App.Log.Warn(LogText.L("log.auth.login_fail", username, ip));
                return null;
            }
            // Repeat login of the same browser (same as UA): Destroy old sessions to avoid accumulation
            var fp = Fingerprint(ua);
            foreach (var old in _sessions.Values.Where(s => s.Username == u.Username && s.Fingerprint == fp).ToList())
                _sessions.Remove(old.TokenHash);
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var sess = new Session
            {
                TokenHash = Sha(token),
                Username = u.Username,
                Role = u.Role,
                Fingerprint = fp,
                RemoteIp = ip,
            };
            _sessions[sess.TokenHash] = sess;
            SaveSessions();
            App.Log.Info(LogText.L("log.auth.login_ok", u.Username, ip));
            return sess;
        }
    }

    /// <summary>
    /// Parse session by Cookie token. Hit condition: token Hash exists + UA consistent fingerprints + no idle timeout.
    /// Fingerprints do not match this request and are counted and cumulatively <see cref="MaxFingerprintFails"/> is destroyed several times -
    /// Otherwise anyone who gets token with the wrong UA can kick a legitimate session off the line.
    /// Only for the second front end (telephone listening); this machine does not listen with authentication, directly using <see cref="LocalSession"/>.
    /// </summary>
    public Session? Resolve(string? token, string ua, string ip)
    {
        if (string.IsNullOrEmpty(token)) return null;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(Sha(token), out var s)) return null;
            var user = _users.FirstOrDefault(u => string.Equals(u.Username, s.Username, StringComparison.OrdinalIgnoreCase));
            if (user == null)
            {
                _sessions.Remove(s.TokenHash);
                SaveSessions();
                return null;
            }
            // Role changes take effect on the next request instead of leaving stale admin sessions privileged.
            s.Role = user.Role;
            var idle = App.Config.Remote.IdleMinutes;
            if (idle > 0 && DateTime.Now - s.LastSeen > TimeSpan.FromMinutes(idle))
            {
                _sessions.Remove(s.TokenHash);
                SaveSessions();
                return null;
            }
            if (!string.Equals(s.Fingerprint, Fingerprint(ua), StringComparison.Ordinal))
            {
                s.FingerprintFails++;
                App.Log.Warn(LogText.L("log.auth.fingerprint_mismatch", s.Username, ip, s.FingerprintFails));
                if (s.FingerprintFails >= MaxFingerprintFails)
                {
                    _sessions.Remove(s.TokenHash);
                    SaveSessions();
                    App.Log.Warn(LogText.L("log.auth.fingerprint_destroy", MaxFingerprintFails, s.Username));
                }
                return null;
            }
            s.FingerprintFails = 0;
            // Heart beats will be very frequent, only once over 1 minutes.
            var stale = DateTime.Now - s.LastSeen > TimeSpan.FromMinutes(1);
            s.LastSeen = DateTime.Now;
            s.RemoteIp = ip;
            if (stale) SaveSessions();
            return s;
        }
    }

    /// <summary> log out by original token (internal HS delete). </summary>
    public void Logout(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        lock (_lock)
        {
            if (_sessions.Remove(Sha(token))) SaveSessions();
        }
    }

    /// <summary>
    /// Can not open message The returned <see cref="Session.TokenHash"/> is the id for the management end
    /// (Hashi values, leaks cannot be used when Cookie is used; original token does not exist here.
    /// </summary>
    public List<Session> Sessions()
    {
        lock (_lock)
        {
            return _sessions.Values
                .OrderByDescending(s => s.LastSeen)
                .Select(s => new Session
                {
                    TokenHash = s.TokenHash,
                    Username = s.Username,
                    Role = s.Role,
                    Created = s.Created,
                    LastSeen = s.LastSeen,
                    RemoteIp = s.RemoteIp,
                })
                .ToList();
        }
    }

    /// <summary> destroys specified sessions (parameters are <see cref="Session.TokenHash"/>, not original token). </summary>
    public bool Kick(string tokenHash)
    {
        lock (_lock)
        {
            var ok = _sessions.Remove(tokenHash ?? "");
            if (ok) SaveSessions();
            return ok;
        }
    }

    /// Whether the session currently requested by <summary> is this one (by token Hashimbi, without revealing the original token). </summary>
    public static bool IsSame(Session s, string? token)
        => !string.IsNullOrEmpty(token) && string.Equals(s.TokenHash, Sha(token!), StringComparison.Ordinal);

    /// <summary> role-accessible whiteboard list (for front-end filtering navigation/routing). admin = full volume (null). </summary>
    public List<string>? AllowedTabs(Session s)
    {
        if (s.Role == "admin") return null;
        lock (_lock)
        {
            var u = _users.FirstOrDefault(x => string.Equals(x.Username, s.Username, StringComparison.OrdinalIgnoreCase));
            var tabs = u is { Tabs.Count: > 0 } ? new List<string>(u.Tabs)
                : AllTabsExceptRestricted();
            return tabs;
        }
    }

    static List<string> AllTabsExceptRestricted()
    {
        var all = new List<string>
        {
            "overview", "dashboard", "heartbeat", "devices", "osc", "pusher",
            "hwinfo", "headset", "logs", "monitor", "about",
        };
        return all;
    }

    // ---------------------------------------------------------------- User Management (admin only)

    /// <summary>XQ1QXZ: add / remove / pass / role / tabs / init Returns the list of new users (when successful).
    /// P6: "init" creates the first admin and is only legal on an empty store; admin passwords must pass the strength gate.</summary>
    public List<UserRow>? Manage(string action, string username, string? password, string? role, List<string>? tabs)
    {
        lock (_lock)
        {
            var idx = _users.FindIndex(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            switch (action)
            {
                case "init":
                    // First-enable initialization: exactly one strong admin account, refused once any user exists.
                    if (_users.Count > 0 || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)
                        || !PasswordStrong(password)) return null;
                    _users.Add(CreateUser(username, password, "admin", null));
                    App.Log.Info(LogText.L("log.auth.admin_initialized", username));
                    break;
                case "add":
                    if (idx >= 0 || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;
                    var nr = CreateUser(username, password, role is "admin" or "user" ? role : "user", tabs);
                    // New admins enter through the strength gate so WAN preconditions cannot be sidestepped.
                    if (nr.Role == "admin" && !nr.Strong) return null;
                    _users.Add(nr);
                    break;
                case "remove":
                    if (idx < 0) return null;
                    // Keep at least one admin
                    if (_users[idx].Role == "admin" && _users.Count(x => x.Role == "admin") <= 1) return null;
                    _users.RemoveAt(idx);
                    break;
                case "pass":
                    if (idx < 0 || string.IsNullOrEmpty(password)) return null;
                    // P6: admin password changes must satisfy the strength gate (WAN readiness relies on it).
                    if (_users[idx].Role == "admin" && !PasswordStrong(password)) return null;
                    var (s2, h2) = HashPassword(password);
                    _users[idx].Salt = s2;
                    _users[idx].Hash = h2;
                    _users[idx].Strong = PasswordStrong(password);
                    break;
                case "role":
                    if (idx < 0 || role is not ("admin" or "user")) return null;
                    if (role == "user" && _users[idx].Role == "admin" && _users.Count(x => x.Role == "admin") <= 1) return null;
                    // Promoting a weak account must not create a WAN-capable weak administrator.
                    if (role == "admin" && (!_users[idx].Strong || Verify("admin", _users[idx].Salt, _users[idx].Hash))) return null;
                    _users[idx].Role = role;
                    break;
                case "tabs":
                    if (idx < 0) return null;
                    _users[idx].Tabs = tabs ?? new();
                    break;
                default:
                    return null;
            }
            var list = _users.Select(u => Clone(u)).ToList();
            SaveUsers();
            return list;
        }
    }

    /// <summary> public payload: does not include Hash. </summary>
    public static object PublicUser(UserRow u) => new
    {
        username = u.Username,
        role = u.Role,
        tabs = u.Tabs,
    };
}
