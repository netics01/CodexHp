using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace CodexHp.App.Presentation;

internal static class RepositoryBrowser
{
    internal const string Url = "https://github.com/netics01/codexhp";

    internal static ProcessStartInfo CreateStartInfo() => new(Url) { UseShellExecute = true };

    internal static void Open()
    {
        try
        {
            Process.Start(CreateStartInfo());
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"Could not open the default browser. Visit:\n\n{Url}",
                "CodexHp", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
