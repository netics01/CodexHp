using System.IO;
using System.Security;
using Microsoft.Win32;

namespace CodexHp.App.Infrastructure;

internal enum TrayIconTheme
{
    Dark,
    Light,
}

internal static class WindowsShellTheme
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static TrayIconTheme? Read() => Read(name => Registry.GetValue(PersonalizeKey, name, null));

    internal static TrayIconTheme? Read(Func<string, object?> readValue)
    {
        try
        {
            // AppsUseLightTheme can differ from the taskbar in Windows custom mode.
            return readValue("SystemUsesLightTheme") switch
            {
                int value when value == 0 => TrayIconTheme.Dark,
                int value when value == 1 => TrayIconTheme.Light,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Keep the current icon if theme detection is temporarily unavailable.
            return null;
        }
    }
}
