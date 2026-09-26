using Microsoft.Win32;

namespace DevTerm.Configuration;

/// <summary>
/// Whether the OS prefers dark apps - what the <c>system</c> theme follows. On Windows that's the
/// per-user "Choose your app mode" setting (<c>AppsUseLightTheme</c> under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>, 0 = dark); elsewhere the
/// terminal's <c>COLORFGBG</c> hint (<c>"15;0"</c> = light text on a dark background) if it's set.
/// Anything undetectable means light, the pre-theming default.
/// </summary>
public static class SystemThemeDetector
{
    private const string _personalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool PrefersDark()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_personalizeKey);
                if (key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme)
                {
                    return appsUseLightTheme == 0;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                // Unreadable is "undetectable" - fall through to the other hints.
            }
        }

        return PrefersDarkFromColorFgBg(Environment.GetEnvironmentVariable("COLORFGBG"));
    }

    /// <summary>
    /// Reads <c>COLORFGBG</c> (<c>"fg;bg"</c> or <c>"fg;default;bg"</c>, ANSI color indexes): a background
    /// of 0-6 or 8 is a dark one. Unset or unparseable means not dark.
    /// </summary>
    public static bool PrefersDarkFromColorFgBg(string? colorFgBg)
    {
        var parts = colorFgBg?.Split(';');
        return parts is { Length: >= 2 }
            && int.TryParse(parts[^1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var background)
            && background is (>= 0 and <= 6) or 8;
    }
}
