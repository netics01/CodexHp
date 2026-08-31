using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class InstallerConfigurationTests
{
    [Fact]
    public void Installer_uses_a_stable_per_user_application_identity_and_location()
    {
        var installer = ReadRequiredRepositoryFile("installer", "CodexHp.iss");

        Assert.Contains("AppId={{4B302CDD-065E-4C2F-A0CD-DC430E4B03A8}", installer, StringComparison.Ordinal);
        Assert.Contains("PrivilegesRequired=lowest", installer, StringComparison.Ordinal);
        Assert.Contains(@"DefaultDirName={localappdata}\Programs\CodexHp", installer, StringComparison.Ordinal);
        Assert.Contains("AppPublisher=netics01", installer, StringComparison.Ordinal);
        Assert.Contains("AppVersion={#AppVersion}", installer, StringComparison.Ordinal);
        Assert.Contains("Flags: uninsdeletevalue uninsdeletekeyifempty", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_enables_startup_only_on_the_first_install_and_cleans_it_on_uninstall()
    {
        var installer = ReadRequiredRepositoryFile("installer", "CodexHp.iss");

        Assert.Contains("Name: \"autostart\"", installer, StringComparison.Ordinal);
        Assert.Contains("Flags: checkedonce", installer, StringComparison.Ordinal);
        Assert.Contains("Subkey: \"Software\\Microsoft\\Windows\\CurrentVersion\\Run\"", installer, StringComparison.Ordinal);
        Assert.Contains("ValueName: \"CodexHp\"", installer, StringComparison.Ordinal);
        Assert.Contains("Tasks: autostart", installer, StringComparison.Ordinal);
        Assert.Contains("Flags: uninsdeletevalue", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_is_a_single_x64_offline_setup_with_silent_install_support()
    {
        var installer = ReadRequiredRepositoryFile("installer", "CodexHp.iss");
        var buildScript = ReadRequiredRepositoryFile("scripts", "Build-Installer.ps1");

        Assert.Contains("ArchitecturesAllowed=x64compatible", installer, StringComparison.Ordinal);
        Assert.Contains("OutputBaseFilename=CodexHp-Setup-{#AppVersion}-x64", installer, StringComparison.Ordinal);
        Assert.Contains("skipifsilent", installer, StringComparison.Ordinal);
        Assert.Contains("Verify-Core.ps1", buildScript, StringComparison.Ordinal);
        Assert.Contains("ISCC.exe", buildScript, StringComparison.Ordinal);
        Assert.Contains("${env:ProgramFiles(x86)}", buildScript, StringComparison.Ordinal);
        Assert.Contains(".InnerText.Trim()", buildScript, StringComparison.Ordinal);
        Assert.Contains("Installer output directory must stay below", buildScript, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $outputDirectoryFull -Recurse -Force", buildScript, StringComparison.Ordinal);
        Assert.Contains("CodexHp-Setup-$version-x64.exe", buildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_uses_the_effective_startup_registration_and_disables_unsafe_enabling()
    {
        var appSource = ReadRequiredRepositoryFile("src", "CodexHp.App", "App.xaml.cs");
        var settingsViewModel = ReadRequiredRepositoryFile(
            "src",
            "CodexHp.App",
            "Presentation",
            "Settings",
            "SettingsWindowViewModel.cs");
        var settingsView = ReadRequiredRepositoryFile(
            "src",
            "CodexHp.App",
            "Presentation",
            "Settings",
            "SettingsWindow.xaml");

        Assert.Contains("StartWithWindows = startupRegistration.IsEnabled()", appSource, StringComparison.Ordinal);
        Assert.Contains("CanStartWithWindows", settingsViewModel, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanStartWithWindows}\"", settingsView, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_lifecycle_validation_preserves_machine_state_and_checks_upgrade_choice()
    {
        var validator = ReadRequiredRepositoryFile("tests", "Windows", "Validate-Installer.ps1");

        Assert.Contains("Assert-PathBelowOutDirectory", validator, StringComparison.Ordinal);
        Assert.Contains("$originalRunValue", validator, StringComparison.Ordinal);
        Assert.Contains("$outsidePackageInvoker", validator, StringComparison.Ordinal);
        Assert.Contains("StdRegProv", validator, StringComparison.Ordinal);
        Assert.Contains("& $outsidePackageInvoker -FilePath $Path", validator, StringComparison.Ordinal);
        Assert.DoesNotContain("$process = Start-Process -FilePath $Path", validator, StringComparison.Ordinal);
        Assert.Contains("was re-enabled during upgrade", validator, StringComparison.Ordinal);
        Assert.Contains("unins000.exe", validator, StringComparison.Ordinal);
        Assert.Contains("finally", validator, StringComparison.Ordinal);
        Assert.Contains("SetStringValue", validator, StringComparison.Ordinal);
        Assert.Contains("DeleteValue", validator, StringComparison.Ordinal);
        Assert.Contains(".InnerText.Trim()", validator, StringComparison.Ordinal);
        Assert.Contains("$settingsBackupPath", validator, StringComparison.Ordinal);
        Assert.Contains(
            "Move-Item -LiteralPath $settingsPath -Destination $settingsBackupPath",
            validator,
            StringComparison.Ordinal);
        Assert.Contains(
            "Move-Item -LiteralPath $settingsBackupPath -Destination $settingsPath",
            validator,
            StringComparison.Ordinal);
    }

    private static string ReadRequiredRepositoryFile(params string[] segments)
    {
        var path = Path.Combine([FindCodexHpRoot(), .. segments]);
        Assert.True(File.Exists(path), $"Required distribution file is missing: {path}");
        return File.ReadAllText(path);
    }

    private static string FindCodexHpRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexHp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the CodexHp repository root.");
    }
}
