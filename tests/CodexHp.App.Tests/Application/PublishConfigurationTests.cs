using System.Xml.Linq;
using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class PublishConfigurationTests
{
    [Fact]
    public void Application_project_declares_release_version_0_3_0()
    {
        var properties = LoadApplicationProjectProperties();

        Assert.Equal("0.3.0", properties["Version"]);
    }

    [Fact]
    public void Installer_default_version_matches_the_application_version()
    {
        var codexHpRoot = FindCodexHpRoot();
        var installer = File.ReadAllText(Path.Combine(codexHpRoot, "installer", "CodexHp.iss"));

        Assert.Contains("#define AppVersion \"0.3.0\"", installer, StringComparison.Ordinal);
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
