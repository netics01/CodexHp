using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class ReleaseConfigurationTests
{
    [Fact]
    public void Release_workflow_requires_explicit_manual_approval_for_unsigned_publication()
    {
        var workflow = ReadRequiredRepositoryFile(".github", "workflows", "release.yml");
        var buildInstaller = ReadRequiredRepositoryFile("scripts", "Build-Installer.ps1");

        Assert.DoesNotContain("\n  push:", workflow, StringComparison.Ordinal);
        Assert.Contains("allow_unsigned:", workflow, StringComparison.Ordinal);
        Assert.Contains("type: boolean", workflow, StringComparison.Ordinal);
        Assert.Contains("if ($env:ALLOW_UNSIGNED -ne 'true')", workflow, StringComparison.Ordinal);
        Assert.Contains("Build-Installer.ps1 -UseExistingVerifiedPublish", workflow, StringComparison.Ordinal);
        Assert.Contains("Stage-Release.ps1 -AllowUnsignedRelease", workflow, StringComparison.Ordinal);
        Assert.Contains("This release is not code-signed", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release create", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("WINDOWS_SIGNING_CERTIFICATE_BASE64", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("signtool.exe", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[switch]$UseExistingVerifiedPublish", buildInstaller, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_staging_requires_signatures_by_default_with_an_explicit_unsigned_override()
    {
        var staging = ReadRequiredRepositoryFile("scripts", "Stage-Release.ps1");

        Assert.Contains("[switch]$AllowUnsignedRelease", staging, StringComparison.Ordinal);
        Assert.Contains("Get-AuthenticodeSignature", staging, StringComparison.Ordinal);
        Assert.Contains("if (-not $AllowUnsignedRelease", staging, StringComparison.Ordinal);
        Assert.Contains("Signature status", staging, StringComparison.Ordinal);
        Assert.Contains("Staging an explicitly approved unsigned release", staging, StringComparison.Ordinal);
        Assert.Contains("CodexHp-Setup-$version-x64.exe", staging, StringComparison.Ordinal);
        Assert.Contains("CodexHp-Portable-$version-x64.exe", staging, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", staging, StringComparison.Ordinal);
    }

    [Fact]
    public void WinGet_generation_uses_a_signed_inno_user_installer_and_paired_locales()
    {
        var generator = ReadRequiredRepositoryFile("scripts", "New-WinGetManifest.ps1");
        var workflow = ReadRequiredRepositoryFile(".github", "workflows", "release.yml");

        Assert.Contains("Get-AuthenticodeSignature", generator, StringComparison.Ordinal);
        Assert.Contains("[switch]$AllowUnsignedDevelopmentBuild", generator, StringComparison.Ordinal);
        Assert.Contains("if (-not $AllowUnsignedDevelopmentBuild", generator, StringComparison.Ordinal);
        Assert.Contains("$packageIdentifier = 'netics01.CodexHp'", generator, StringComparison.Ordinal);
        Assert.Contains("$manifestVersion = '1.12.0'", generator, StringComparison.Ordinal);
        Assert.Contains("InstallerType: inno", generator, StringComparison.Ordinal);
        Assert.Contains("Scope: user", generator, StringComparison.Ordinal);
        Assert.Contains("{4B302CDD-065E-4C2F-A0CD-DC430E4B03A8}_is1", generator, StringComparison.Ordinal);
        Assert.Contains("PackageLocale: en-US", generator, StringComparison.Ordinal);
        Assert.Contains("PackageLocale: ko-KR", generator, StringComparison.Ordinal);
        Assert.DoesNotContain("New-WinGetManifest.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("winget validate", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_pair_discloses_unsigned_0_2_0_and_no_winget_submission()
    {
        var english = ReadRequiredRepositoryFile("README.md");
        var korean = ReadRequiredRepositoryFile("README.ko.md");

        Assert.Contains("Version 0.2.0 is published without Authenticode code signing", english, StringComparison.Ordinal);
        Assert.Contains("This release is not submitted to WinGet", english, StringComparison.Ordinal);
        Assert.Contains("버전 0.2.0은 Authenticode 코드 서명 없이 공개", korean, StringComparison.Ordinal);
        Assert.Contains("이 릴리스는 WinGet에 제출하지 않습니다", korean, StringComparison.Ordinal);
    }

    [Fact]
    public void Continuous_integration_runs_the_repository_verification_entrypoint()
    {
        var workflow = ReadRequiredRepositoryFile(".github", "workflows", "verify.yml");

        Assert.Contains("scripts/Verify-Core.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("windows-latest", workflow, StringComparison.Ordinal);
    }

    private static string ReadRequiredRepositoryFile(params string[] segments)
    {
        var path = Path.Combine([FindCodexHpRoot(), .. segments]);
        Assert.True(File.Exists(path), $"Required release file is missing: {path}");
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
