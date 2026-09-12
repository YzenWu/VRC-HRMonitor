using System.Runtime.InteropServices;
using System.Text;

namespace HeartRateMonitor.Cli;

/// <summary>Restorable Windows console VT/alternate-screen scope.</summary>
internal sealed class NativeConsole : IDisposable
{
    const int StdOutputHandle = -11;
    const uint EnableVirtualTerminalProcessing = 0x0004;
    readonly IntPtr _handle;
    readonly uint _originalMode;
    readonly bool _modeChanged;
    bool _alternate;
    bool _disposed;

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetConsoleMode(IntPtr handle, uint mode);

    NativeConsole(IntPtr handle, uint originalMode, bool changed)
    {
        _handle = handle;
        _originalMode = originalMode;
        _modeChanged = changed;
    }

    public static NativeConsole? Create()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected) return null;
        try
        {
            var handle = GetStdHandle(StdOutputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1) || !GetConsoleMode(handle, out var mode)) return null;
            var next = mode | EnableVirtualTerminalProcessing;
            if (!SetConsoleMode(handle, next)) return null;
            return new NativeConsole(handle, mode, mode != next);
        }
        catch { return null; }
    }

    public static bool EnableVirtualTerminal()
    {
        using var scope = Create();
        return scope != null;
    }

    public void EnterAlternateScreen()
    {
        if (_alternate || _disposed) return;
        Console.Write("\u001b[?1049h\u001b[?25l");
        _alternate = true;
    }

    public void LeaveAlternateScreen()
    {
        if (!_alternate) return;
        Console.Write("\u001b[?25h\u001b[?1049l");
        _alternate = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        LeaveAlternateScreen();
        if (_modeChanged) try { SetConsoleMode(_handle, _originalMode); } catch { }
        _disposed = true;
    }

    public static string Crop(string text, int width)
    {
        if (width <= 0) return "";
        var result = new StringBuilder();
        var used = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cells = IsWide(rune) ? 2 : 1;
            if (used + cells > width) break;
            result.Append(rune.ToString());
            used += cells;
        }
        return result.ToString();
    }

    static bool IsWide(Rune rune)
    {
        var v = rune.Value;
        return v is >= 0x1100 and <= 0x115f
            or >= 0x2e80 and <= 0xa4cf
            or >= 0xac00 and <= 0xd7a3
            or >= 0xf900 and <= 0xfaff
            or >= 0xfe10 and <= 0xfe6f
            or >= 0xff00 and <= 0xff60
            or >= 0x1f300 and <= 0x1faff
            or >= 0x20000 and <= 0x3fffd;
    }
}