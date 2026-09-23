using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using CodexHp.App.Application;

namespace CodexHp.App.Presentation;

internal static class ReleaseBrowser
{
    internal static ProcessStartInfo CreateStartInfo(AvailableUpdate update) =>
        new(update.ReleaseUri.AbsoluteUri) { UseShellExecute = true };

    internal static void Open(AvailableUpdate update)
    {
        try { Process.Start(CreateStartInfo(update)); }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"Could not open the default browser. Visit:\n\n{update.ReleaseUri}",
                "CodexHp", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
