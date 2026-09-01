using System.Xml.Linq;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class PublishConfigurationTests
{
    [Fact]
    public void Application_project_declares_release_version_0_3_1()
    {
        var properties = LoadApplicationProjectProperties();

        Assert.Equal("0.3.1", properties["Version"]);
    }

    [Fact]
    public void Installer_default_version_matches_the_application_version()
    {
        var codexHpRoot = FindCodexHpRoot();
        var installer = File.ReadAllText(Path.Combine(codexHpRoot, "installer", "CodexHp.iss"));

        Assert.Contains("#define AppVersion \"0.3.1\"", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_project_targets_windows_11_or_later()
    {
        var properties = LoadApplicationProjectProperties();

        Assert.Equal("net10.0-windows10.0.22000.0", properties["TargetFramework"]);
        Assert.Equal("10.0.22000.0", properties["TargetPlatformMinVersion"]);
        Assert.Equal("10.0.22000.0", properties["SupportedOSPlatformVersion"]);
    }

    [Fact]
    public void Self_contained_single_file_publish_omits_unneeded_satellites_and_compresses_runtime()
    {
        var properties = LoadApplicationProjectProperties();

        Assert.Equal("true", properties["EnableCompressionInSingleFile"]);
        Assert.Equal("ko", properties["SatelliteResourceLanguages"]);
        Assert.Equal("false", properties["PublishTrimmed"]);
    }

    [Fact]
    public void Core_verification_rejects_a_published_executable_over_the_size_budget()
    {
        var codexHpRoot = FindCodexHpRoot();
        var scriptPath = Path.Combine(codexHpRoot, "scripts", "Verify-Core.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("$maximumPublishedExecutableBytes = 100MB", script, StringComparison.Ordinal);
        Assert.Contains("Published CodexHp.exe exceeds the 100 MiB size budget.", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Core_verification_resolves_the_repository_root_from_the_scripts_directory()
    {
        var codexHpRoot = FindCodexHpRoot();
        var scriptPath = Path.Combine(codexHpRoot, "scripts", "Verify-Core.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains(
            "$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Published_app_validation_derives_default_bounds_from_the_runtime_dpi_and_taskbar()
    {
        var codexHpRoot = FindCodexHpRoot();
        var script = File.ReadAllText(Path.Combine(
            codexHpRoot,
            "tests",
            "Windows",
            "Validate-PublishedApp.ps1"));

        Assert.Contains("GetWindowDpi", script, StringComparison.Ordinal);
        Assert.Contains("$defaultOverlayWidthDip = 144", script, StringComparison.Ordinal);
        Assert.Contains("$defaultOverlayHeightDip = 34", script, StringComparison.Ordinal);
        Assert.DoesNotContain("($monitorBounds[1] + $monitorBounds[3] - 12 - 68)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_release_uses_an_interactive_scheduled_task_to_escape_packaged_host_virtualization()
    {
        var codexHpRoot = FindCodexHpRoot();
        var releaseScript = File.ReadAllText(Path.Combine(
            codexHpRoot,
            "scripts",
            "Publish-LocalRelease.ps1"));
        var invokerPath = Path.Combine(codexHpRoot, "scripts", "Invoke-OutsidePackage.ps1");

        Assert.True(File.Exists(invokerPath), $"Missing outside-package invoker: {invokerPath}");

        var invoker = File.ReadAllText(invokerPath);
        Assert.Contains("Schedule.Service", invoker, StringComparison.Ordinal);
        Assert.Contains("$taskLogonInteractiveToken = 3", invoker, StringComparison.Ordinal);
        Assert.Contains("RegisterTaskDefinition", invoker, StringComparison.Ordinal);
        Assert.Contains("DeleteTask", invoker, StringComparison.Ordinal);
        Assert.Contains("$successfulDetachedExitCodes = @(0, 1)", invoker, StringComparison.Ordinal);

        Assert.Contains(
            "$outsidePackageInvoker = Join-Path $repositoryRoot 'scripts\\Invoke-OutsidePackage.ps1'",
            releaseScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "& $outsidePackageInvoker -FilePath $downloadedInstallerPath",
            releaseScript,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "-FilePath (Join-Path $downloadDirectory $setupName)",
            releaseScript,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Start-Process -FilePath $installedExecutablePath",
            releaseScript,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Local_release_verifies_installation_through_the_real_windows_service_view()
    {
        var codexHpRoot = FindCodexHpRoot();
        var releaseScript = File.ReadAllText(Path.Combine(
            codexHpRoot,
            "scripts",
            "Publish-LocalRelease.ps1"));
        var validatorPath = Path.Combine(codexHpRoot, "scripts", "Test-WindowsInstallation.ps1");

        Assert.True(File.Exists(validatorPath), $"Missing Windows installation validator: {validatorPath}");

        var validator = File.ReadAllText(validatorPath);
        Assert.Contains("StdRegProv", validator, StringComparison.Ordinal);
        Assert.Contains("CIM_DataFile", validator, StringComparison.Ordinal);
        Assert.Contains("Get-StartApps", validator, StringComparison.Ordinal);
        Assert.Contains("StartupApproved", validator, StringComparison.Ordinal);
        Assert.Contains("$missingRegistryValueReturnCodes = @(1, 2)", validator, StringComparison.Ordinal);
        Assert.Contains("$startupEnabled = $null -eq $approval -or", validator, StringComparison.Ordinal);

        Assert.Contains(
            "$installationValidator = Join-Path $repositoryRoot 'scripts\\Test-WindowsInstallation.ps1'",
            releaseScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "& $installationValidator -ExpectedVersion $version",
            releaseScript,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Application_project_and_sources_do_not_reference_windows_forms()
    {
        var codexHpRoot = FindCodexHpRoot();
        var properties = LoadApplicationProjectProperties();
        var violations = new List<string>();
        if (properties.TryGetValue("UseWindowsForms", out var useWindowsForms) &&
            string.Equals(useWindowsForms, "true", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add("CodexHp.App.csproj enables UseWindowsForms.");
        }

        var sourceRoot = Path.Combine(codexHpRoot, "src", "CodexHp.App");
        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            var segments = relativePath.Split(Path.DirectorySeparatorChar);
            if (segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
                segments.Contains("obj", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.ReadAllText(sourcePath).Contains("System.Windows.Forms", StringComparison.Ordinal))
            {
                violations.Add(relativePath);
            }
        }

        Assert.Empty(violations);
    }

    private static IReadOnlyDictionary<string, string> LoadApplicationProjectProperties()
    {
        var codexHpRoot = FindCodexHpRoot();
        var projectPath = Path.Combine(codexHpRoot, "src", "CodexHp.App", "CodexHp.App.csproj");
        var document = XDocument.Load(projectPath);

        return document.Root!
            .Elements("PropertyGroup")
            .Elements()
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value.Trim(),
                StringComparer.Ordinal);
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
