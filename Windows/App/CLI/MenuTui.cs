using HeartRateMonitor.Core;

namespace HeartRateMonitor.Cli;

internal sealed class MenuTui
{
    sealed record Item(string Label, string[] Commands);
    static readonly Item[] Items =
    {
        new("Status / About", new[] { "status", "about" }),
        new("Devices", new[] { "devices", "scan 10" }),
        new("Health / Records", new[] { "health", "record get" }),
        new("OSC", new[] { "osc", "osc on", "osc off" }),
        new("Hardware", new[] { "info fast", "hw" }),
        new("Web / API", new[] { "web status", "web start", "web stop", "api" }),
        new("Settings", new[] { "config" }),
        new("Diagnostics", new[] { "logs 30", "selftest" }),
        new("Command Shell", Array.Empty<string>()),
        new("Exit", Array.Empty<string>()),
    };
    readonly CliSession _session;
    readonly NativeConsole _console;
    readonly List<string> _output = new();
    int _selected;
    int _command;
    int _scroll;
    bool _commandPage;
    bool _running = true;
    bool _dirty = true;
    int _width;
    int _height;

    public MenuTui(CliSession session, NativeConsole console) { _session = session; _console = console; }

    public void Run()
    {
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; _running = false; };
        Console.CancelKeyPress += cancel;
        _session.SubscribeEvents();
        _console.EnterAlternateScreen();
        CliUi.Banner();
        CliUi.Print(CliApp.ProjectInfoLines());
        Thread.Sleep(700);
        try
        {
            while (_running)
            {
                Resize();
                DrainEvents();
                if (_dirty) Draw();
                if (!Console.KeyAvailable) { Thread.Sleep(50); continue; }
                Handle(Console.ReadKey(intercept: true));
            }
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    void Resize()
    {
        int w, h;
        try { w = Math.Max(30, Console.WindowWidth); h = Math.Max(10, Console.WindowHeight); }
        catch { w = 80; h = 25; }
        if (w != _width || h != _height) { _width = w; _height = h; _dirty = true; }
    }

    void DrainEvents()
    {
        foreach (var e in _session.Events.Drain()) { _output.Add($"[{e.Tag}] {e.Text}"); _dirty = true; }
        while (_output.Count > 500) _output.RemoveAt(0);
    }

    void Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape || (_commandPage && key.Key == ConsoleKey.LeftArrow))
        { if (_commandPage) { _commandPage = false; _scroll = 0; } else _running = false; _dirty = true; return; }
        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
        {
            var delta = key.Key == ConsoleKey.UpArrow ? -1 : 1;
            if (_commandPage && Items[_selected].Commands.Length > 0)
                _command = (_command + delta + Items[_selected].Commands.Length) % Items[_selected].Commands.Length;
            else if (!_commandPage) _selected = (_selected + delta + Items.Length) % Items.Length;
            _dirty = true; return;
        }
        if (_commandPage && key.Key is ConsoleKey.PageUp or ConsoleKey.PageDown)
        { _scroll = Math.Max(0, _scroll + (key.Key == ConsoleKey.PageUp ? -5 : 5)); _dirty = true; return; }
        if (key.Key is ConsoleKey.Enter or ConsoleKey.RightArrow) Activate();
    }

    void Activate()
    {
        var item = Items[_selected];
        if (!_commandPage)
        {
            if (item.Label == "Exit") { _running = false; return; }
            if (item.Label == "Command Shell") { ShellPage(); return; }
            _commandPage = true; _command = 0; _output.Clear(); _scroll = 0; _dirty = true; return;
        }
        if (item.Commands.Length == 0) return;
        var command = item.Commands[_command];
        if (Dangerous(command) && !Confirm(command)) return;
        _output.Add($"> {command}");
        _output.AddRange(_session.Execute(command, _console.LeaveAlternateScreen, () => _console.EnterAlternateScreen()));
        _scroll = Math.Max(0, _output.Count - Math.Max(1, _height - 10));
        _dirty = true;
    }

    void ShellPage()
    {
        _console.LeaveAlternateScreen();
        try { PlainShell.Run(_session, banner: false, prompt: true); }
        finally { _console.EnterAlternateScreen(); _dirty = true; }
    }

    bool Confirm(string command)
    {
        _output.Add($"确认执行危险操作“{command}”？按 Y 确认"); _dirty = true; Draw();
        return Console.ReadKey(true).Key == ConsoleKey.Y;
    }

    static bool Dangerous(string command) => command is "osc on" or "osc off" or "web start" or "web stop" || command.StartsWith("selftest");

    void Draw()
    {
        _dirty = false;
        Console.Write("\u001b[H\u001b[2J");
        Line("HeartRateMonitor · Menu TUI");
        Line($"{ReleaseManifest.RepoName}  {ReleaseManifest.ReleaseName} v{ReleaseManifest.Version}  {ReleaseManifest.BuildTimeUtc}");
        if (ReleaseManifest.BuildNote.Length > 0) Line(ReleaseManifest.BuildNote);
        if (ReleaseManifest.BuildTarget.Length > 0) Line($"Target: {ReleaseManifest.BuildTarget}");
        var github = GitHubProjectService.Current;
        Line(github == null ? "GitHub: cache unavailable" : $"GitHub: ★{github.Stars}  issues {github.IssuesOpen}/{github.IssuesTotal}  {(github.Stale ? "stale" : "cached")}");
        Line(new string('─', Math.Max(1, _width - 1)));
        if (!_commandPage) DrawMain(); else DrawCommands();
        Console.SetCursorPosition(0, Math.Max(0, _height - 1));
        Line($"BPM {App.CurrentBpm} | OSC {(App.Osc.Connected ? "ON" : "OFF")} | ↑↓ 选择  Enter/→ 打开  Esc/← 返回", clear: true);
    }

    void DrawMain()
    {
        for (var i = 0; i < Items.Length && Console.CursorTop < _height - 1; i++) Line($" {(i == _selected ? '>' : ' ')} {Items[i].Label}");
    }

    void DrawCommands()
    {
        var item = Items[_selected]; Line($"[{item.Label}]");
        for (var i = 0; i < item.Commands.Length && Console.CursorTop < _height - 5; i++) Line($" {(i == _command ? '>' : ' ')} {item.Commands[i]}");
        var room = Math.Max(1, _height - Console.CursorTop - 2);
        foreach (var line in _output.Skip(_scroll).Take(room)) Line("  " + line);
    }

    void Line(string text, bool clear = false)
    {
        var value = NativeConsole.Crop(text, Math.Max(1, _width - 1));
        Console.Write(value);
        if (clear) Console.Write("\u001b[K");
        else Console.WriteLine();
    }
}