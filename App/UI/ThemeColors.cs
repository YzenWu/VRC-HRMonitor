using System.Drawing.Drawing2D;

namespace HeartRateMonitor.UI;

/// <summary>
/// Theme colour for tray/text menu: Same caliber (mode/palette/accent+ system theme with Web frontend).
/// mode = system takes Windows deep and palette = system takes DWM emphasised colours.
/// </summary>
public static class ThemeColors
{
    /// <summary> Forest / Sunset Two Preset Colored Colours (corresponding to front-end CSS view). </summary>
    private const string ForestAccent = "#4CC38A";
    private const string SunsetAccent = "#E8853A";
    private const string DefaultAccent = "#5B9DFF";

    public static bool IsDark()
    {
        var m = App.Config.Ui.Mode;
        if (string.Equals(m, "dark", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(m, "light", StringComparison.OrdinalIgnoreCase)) return false;
        return SystemTheme.IsDark();
    }

    public static Color Accent()
    {
        var p = App.Config.Ui.Palette?.ToLowerInvariant() ?? "system";
        var hex = p switch
        {
            "custom" => App.Config.Ui.Accent,
            "forest" => ForestAccent,
            "sunset" => SunsetAccent,
            "default" => DefaultAccent,
            _ => SystemTheme.Accent(),
        };
        return Parse(hex) ?? Parse(DefaultAccent)!.Value;
    }

    /// <summary>Returns the menu background color. Custom palettes use the configured panel color; other palettes use neutral light or dark colors.</summary>
    public static Color Background()
    {
        if (string.Equals(App.Config.Ui.Palette, "custom", StringComparison.OrdinalIgnoreCase)
            && Parse(App.Config.Ui.Panel) is { } panel) return panel;
        return IsDark() ? Color.FromArgb(0x21, 0x21, 0x21) : Color.FromArgb(0xFA, 0xFA, 0xFA);
    }

    public static Color Foreground() => IsDark() ? Color.FromArgb(0xF2, 0xF2, 0xF2) : Color.FromArgb(0x1A, 0x1A, 0x1A);

    public static Color Border() => IsDark() ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xDC, 0xDC, 0xDC);

    public static Color Muted() => IsDark() ? Color.FromArgb(0x9A, 0x9A, 0x9A) : Color.FromArgb(0x6B, 0x6B, 0x6B);

    /// <summary> hangs the background: emphasises the colour to be mixed in a low proportion to the bottom of the menu (a semi-transparent supersing of the front-end --accent). </summary>
    public static Color Hover() => Blend(Background(), Accent(), 0.22);

    /// <summary> emphasises the text colour on the color (select one by brightness, Windows emphasizes the dark and shallow colour). </summary>
    public static Color OnAccent()
    {
        var a = Accent();
        var lum = (0.299 * a.R + 0.587 * a.G + 0.114 * a.B) / 255.0;
        return lum > 0.62 ? Color.FromArgb(0x11, 0x14, 0x18) : Color.White;
    }

    /// Round corner of <summary> menu (agreement for front end control round corner +4). </summary>
    public static int MenuRadius() => Math.Clamp(App.Config.Ui.CornerRadius + 4, 0, 20);

    public static Color? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v)) return null;
        return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t),
        (int)Math.Round(a.G + (b.G - a.G) * t),
        (int)Math.Round(a.B + (b.B - a.B) * t));
}
