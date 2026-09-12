using Microsoft.Win32;

namespace HeartRateMonitor.UI;

/// <summary>
/// Windows system theme reading: Apply a shallow mode + emphasis on colour.
/// Two of the front-end themes of "following the system" (deep / theme color) are valued here.
/// <see cref="TrayHost"/> broadcasts sys_theme events after WM_SETTINGCHANGE when the system switches themes.
/// </summary>
public static class SystemTheme
{
    /// Whether the <summary> system is a dark-colour application mode (dark-coloured when not read, consistent with the default look of this program). </summary>
    public static bool IsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (k?.GetValue("AppsUseLightTheme") is int v) return v == 0;
        }
        catch { /*Unread Default*/ }
        return true;
    }

    /// <summary>
    /// System emphasis on colour (#RRGGBB). AccentColor of DWM is a packaged DWORD of ABGR.
    /// ColorizationColor (AARRGGBB) is returned if it is missing, using the default blue.
    /// </summary>
    public static string Accent()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (k?.GetValue("AccentColor") is int abgr)
            {
                int r = abgr & 0xFF, g = (abgr >> 8) & 0xFF, b = (abgr >> 16) & 0xFF;
                return $"#{r:X2}{g:X2}{b:X2}";
            }
            if (k?.GetValue("ColorizationColor") is int argb)
                return $"#{(argb >> 16) & 0xFF:X2}{(argb >> 8) & 0xFF:X2}{argb & 0xFF:X2}";
        }
        catch { /*Unread Default*/ }
        return "#5B9DFF";
    }

    /// <summary> for front end consumption. </summary>
    public static object Snapshot() => new { dark = IsDark(), accent = Accent() };
}
 