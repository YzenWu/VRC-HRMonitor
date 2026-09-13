using HeartRateMonitor.Core;

namespace HeartRateMonitor.Web;

/// <summary>
/// Hosts the Web UI. It starts only when requested by `--web start` or a frontend action.
/// `OpenUi()` brings the built-in WebView shell process to the foreground.
/// </summary>
public sealed class WebHost
{
    private readonly AppHub _hub;
    private readonly int _port;
    private readonly bool _autoOpen;

    public WebServer? Server { get; private set; }
    public bool Running => Server != null;
    public string Url => App.WebUrl(port: _port);

    /// <summary>权限拒绝触发回环回退时转发（每进程至多一次）；GUI 宿主据此提供“以管理员身份重启”确认。</summary>
    public event Action? AccessDeniedFallback;

    public WebHost(AppHub hub, int port, bool autoOpen)
    {
        _hub = hub;
        _port = port;
        _autoOpen = autoOpen;
    }

    /// <summary>
    /// Starts Web service and returns service availability (returns false in failure such as port occupancy).
    /// <paramref name="openUi"/>, when null is config.web.open_browser, decides whether or not to open the interface hand in hand;
    /// The main front-end start-up path (ProcessInfo.LaunchFrontend) is responsible for opening the window itself and will be visible to false to avoid opening two.
    /// </summary>
    public bool Start(bool? openUi = null)
    {
        var open = openUi ?? _autoOpen;
        if (Server == null)
        {
            var srv = new WebServer(_port, _hub, scheme: App.WebScheme);
            srv.AccessDeniedFallback += () => AccessDeniedFallback?.Invoke();
            // Server has to keep null: Otherwise Running will falsely report success.
            // The shell will be pulled up repeatedly.
            if (!srv.Start())
            {
                try { srv.Dispose(); } catch { }
                return false;
            }
            Server = srv;
        }
        // Internal WebView2 shell load page; internal LaunchWebUi regression system browser when shell is missing
        if (open) ProcessInfo.LaunchWebUi(_port);
        return true;
    }

    public void Stop()
    {
        if (Server == null) return;
        try { Server.Dispose(); } catch { }
        Server = null;
    }
}
