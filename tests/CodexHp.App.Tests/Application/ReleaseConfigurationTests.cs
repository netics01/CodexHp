using Xunit;

namespace CodexHp.App.Tests.Application;

public sealed class ReleaseConfigurationTests
{
    [Fact]
    public void Local_release_is_the_only_official_binary_source_and_enforces_publication_safeguards()
    {
        var repositoryRoot = FindCodexHpRoot();
        var localRelease = ReadRequiredRepositoryFile("scripts", "Publish-LocalRelease.ps1");
        var actionsReleasePath = Path.Combine(repositoryRoot, ".github", "workflows", "release.yml");

        Assert.False(File.Exists(actionsReleasePath));
        Assert.Contains("[switch]$AllowUnsignedRelease", localRelease, StringComparison.Ordinal);
        Assert.Contains("git status --porcelain=v1 --untracked-files=all", localRelease, StringComparison.Ordinal);
        Assert.Contains("refs/remotes/origin/main", localRelease, StringComparison.Ordinal);
        Assert.Contains("Build-Installer.ps1", localRelease, StringComparison.Ordinal);
        Assert.Contains("Stage-Release.ps1", localRelease, StringComparison.Ordinal);
        Assert.Contains("gh release create", localRelease, StringComparison.Ordinal);
        Assert.Contains("gh release download", localRelease, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", localRelease, StringComparison.Ordinal);
        Assert.Contains("ProductVersion", localRelease, StringComparison.Ordinal);
        Assert.Contains("CodexHp-Setup-$version-x64.exe", localRelease, StringComparison.Ordinal);
        Assert.Contains("CodexHp-Portable-$version-x64.exe", localRelease, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", localRelease, StringComparison.Ordinal);
        Assert.Contains("/VERYSILENT", localRelease, StringComparison.Ordinal);
        Assert.DoesNotContain("WINDOWS_SIGNING_CERTIFICATE_BASE64", localRelease, StringComparison.Ordinal);
        Assert.DoesNotContain("signtool.exe", localRelease, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Local_release_pins_the_build_toolchain_and_derives_assets_from_the_project_version()
    {
        var sdk = ReadRequiredRepositoryFile("global.json");
        var localRelease = ReadRequiredRepositoryFile("scripts", "Publish-LocalRelease.ps1");

        Assert.Contains("\"version\": \"10.0.203\"", sdk, StringComparison.Ordinal);
        Assert.Contains("$requiredInnoSetupVersion = '6.7.3'", localRelease, StringComparison.Ordinal);
        Assert.Contains("$tag = \"v$version\"", localRelease, StringComparison.Ordinal);
        Assert.Contains("CodexHp-Setup-{version}-x64.exe", localRelease, StringComparison.Ordinal);
        Assert.Contains("CodexHp-Portable-{version}-x64.exe", localRelease, StringComparison.Ordinal);
        Assert.Contains(".Replace('{version}', $version)", localRelease, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_release_marks_process_shutdown_before_installation_so_failures_restore_the_installed_build()
    {
        var localRelease = ReadRequiredRepositoryFile("scripts", "Publish-LocalRelease.ps1");

        Assert.Contains(
            "$applicationProcessesStopped = $true\r\n    Stop-CodexHpProcesses\r\n    $installerProcess",
            localRelease,
            StringComparison.Ordinal);
        Assert.Contains(
            "if ($applicationProcessesStopped) {\r\n        Restore-InstalledApplicationAfterFailure",
            localRelease,
            StringComparison.Ordinal);
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
        var localRelease = ReadRequiredRepositoryFile("scripts", "Publish-LocalRelease.ps1");

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
        Assert.DoesNotContain("New-WinGetManifest.ps1", localRelease, StringComparison.Ordinal);
        Assert.DoesNotContain("winget validate", localRelease, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_pair_discloses_unsigned_0_3_0_and_no_winget_submission()
    {
        var english = ReadRequiredRepositoryFile("README.md");
        var korean = ReadRequiredRepositoryFile("README.ko.md");

        Assert.Contains("Version 0.3.0 is published without Authenticode code signing", english, StringComparison.Ordinal);
        Assert.Contains("This release is not submitted to WinGet", english, StringComparison.Ordinal);
        Assert.Contains("버전 0.3.0은 Authenticode 코드 서명 없이 공개", korean, StringComparison.Ordinal);
        Assert.Contains("이 릴리스는 WinGet에 제출하지 않습니다", korean, StringComparison.Ordinal);
    }

    [Fact]
    public void Continuous_integration_runs_the_repository_verification_entrypoint()
    {
        var workflow = ReadRequiredRepositoryFile(".github", "workflows", "verify.yml");

        Assert.Contains("scripts/Verify-Core.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("windows-latest", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release create", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("contents: write", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_pair_documents_the_single_local_release_command()
    {
        var english = ReadRequiredRepositoryFile("README.md");
        var korean = ReadRequiredRepositoryFile("README.ko.md");

        Assert.Contains("Publish-LocalRelease.ps1 -AllowUnsignedRelease", english, StringComparison.Ordinal);
        Assert.Contains("official release assets are built only by this local command", english, StringComparison.Ordinal);
        Assert.Contains("Publish-LocalRelease.ps1 -AllowUnsignedRelease", korean, StringComparison.Ordinal);
        Assert.Contains("공식 릴리스 자산은 이 로컬 명령으로만 빌드", korean, StringComparison.Ordinal);
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
