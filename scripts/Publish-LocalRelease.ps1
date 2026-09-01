[CmdletBinding()]
param(
    [switch]$AllowUnsignedRelease,
    [string]$CompilerPath
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repositoryRoot 'src\CodexHp.App\CodexHp.App.csproj'
$verifyScript = Join-Path $repositoryRoot 'scripts\Verify-Core.ps1'
$buildInstallerScript = Join-Path $repositoryRoot 'scripts\Build-Installer.ps1'
$stageReleaseScript = Join-Path $repositoryRoot 'scripts\Stage-Release.ps1'
$outsidePackageInvoker = Join-Path $repositoryRoot 'scripts\Invoke-OutsidePackage.ps1'
$installationValidator = Join-Path $repositoryRoot 'scripts\Test-WindowsInstallation.ps1'
$outDirectory = Join-Path $repositoryRoot 'out'
$releaseDirectory = Join-Path $outDirectory 'release'
$logDirectory = Join-Path $outDirectory 'release-logs'
$installedExecutablePath = Join-Path $env:LOCALAPPDATA 'Programs\CodexHp\CodexHp.exe'
$requiredRepository = 'netics01/CodexHp'
$requiredInnoSetupVersion = '6.7.3'
$minimumGitHubCliVersion = [version]'2.86.0'
$transcriptStarted = $false
$applicationProcessesStopped = $false

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter()][string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$FailureMessage
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE."
    }
}

function Get-CheckedCommandOutput {
    param(
        [Parameter(Mandatory)][string]$Command,
        [Parameter()][string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$FailureMessage
    )

    $output = @(& $Command @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE."
    }

    return ($output -join "`n").Trim()
}

function Assert-VersionMatches {
    param(
        [Parameter(Mandatory)][string]$ActualVersion,
        [Parameter(Mandatory)][string]$ExpectedVersion,
        [Parameter(Mandatory)][string]$ArtifactName
    )

    $ActualVersion = $ActualVersion.Trim()

    if (-not [string]::Equals($ActualVersion, $ExpectedVersion, [StringComparison]::OrdinalIgnoreCase) -and
        -not $ActualVersion.StartsWith($ExpectedVersion + '.', [StringComparison]::OrdinalIgnoreCase) -and
        -not $ActualVersion.StartsWith($ExpectedVersion + '+', [StringComparison]::OrdinalIgnoreCase)) {
        throw "$ArtifactName reports version '$ActualVersion'; expected '$ExpectedVersion'."
    }
}

function Assert-ExactAssetSet {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string[]]$ExpectedNames
    )

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "Release asset directory was not found: $Directory"
    }

    $actualNames = @(Get-ChildItem -LiteralPath $Directory -File | ForEach-Object Name | Sort-Object)
    $expectedSorted = @($ExpectedNames | Sort-Object)
    if ($actualNames.Count -ne $expectedSorted.Count -or
        [string]::Join('|', $actualNames) -cne [string]::Join('|', $expectedSorted)) {
        throw "Release assets must be exactly '$([string]::Join(', ', $expectedSorted))'. Found: '$([string]::Join(', ', $actualNames))'."
    }
}

function Assert-Checksums {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string[]]$ExecutableNames
    )

    $checksumPath = Join-Path $Directory 'SHA256SUMS.txt'
    $lines = @(Get-Content -LiteralPath $checksumPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -ne $ExecutableNames.Count) {
        throw "SHA256SUMS.txt must contain exactly $($ExecutableNames.Count) entries."
    }

    foreach ($name in $ExecutableNames) {
        $matchingLines = @($lines | Where-Object { $_ -match "^([0-9a-fA-F]{64})  $([regex]::Escape($name))$" })
        if ($matchingLines.Count -ne 1) {
            throw "SHA256SUMS.txt must contain exactly one entry for '$name'."
        }

        $expectedHash = ([regex]::Match($matchingLines[0], '^([0-9a-fA-F]{64})')).Groups[1].Value
        $actualHash = (Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash
        if (-not [string]::Equals($actualHash, $expectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "SHA-256 verification failed for '$name'."
        }
    }
}

function Get-InnoSetupCompiler {
    if (-not [string]::IsNullOrWhiteSpace($CompilerPath)) {
        $candidate = [IO.Path]::GetFullPath($CompilerPath)
    }
    else {
        $candidates = @((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
        if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
            $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
            $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
        }

        $candidate = $candidates |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Select-Object -First 1
    }

    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw 'Inno Setup compiler ISCC.exe was not found.'
    }

    $uninstallKeys = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )
    $installation = $uninstallKeys |
        ForEach-Object { Get-ItemProperty -LiteralPath $_ -ErrorAction SilentlyContinue } |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.InstallLocation) -and
            $candidate.StartsWith([IO.Path]::GetFullPath($_.InstallLocation), [StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1
    if ($null -eq $installation -or $installation.DisplayVersion -ne $requiredInnoSetupVersion) {
        $actualVersion = if ($null -eq $installation) { 'unknown' } else { $installation.DisplayVersion }
        throw "Inno Setup version '$actualVersion' was found; required version is '$requiredInnoSetupVersion'."
    }

    Write-Host "Inno Setup: $($installation.DisplayVersion) ($candidate)"
    return $candidate
}

function Stop-CodexHpProcesses {
    $processes = @(Get-Process -Name 'CodexHp' -ErrorAction SilentlyContinue)
    foreach ($process in $processes) {
        $path = try { $process.Path } catch { '<unavailable>' }
        Write-Host "Stopping CodexHp process $($process.Id): $path"
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
        $process.WaitForExit(10000) | Out-Null
    }
}

function Start-And-VerifyInstalledApplication {
    param(
        [Parameter(Mandatory)][string]$ExpectedVersion,
        [Parameter(Mandatory)][string]$ExpectedPortablePath
    )

    if (-not (Test-Path -LiteralPath $installedExecutablePath -PathType Leaf)) {
        throw "Installed CodexHp executable was not found: $installedExecutablePath"
    }

    $installedVersion = (Get-Item -LiteralPath $installedExecutablePath).VersionInfo.ProductVersion
    Assert-VersionMatches $installedVersion $ExpectedVersion 'Installed CodexHp.exe'

    $installedHash = (Get-FileHash -LiteralPath $installedExecutablePath -Algorithm SHA256).Hash
    $portableHash = (Get-FileHash -LiteralPath $ExpectedPortablePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($installedHash, $portableHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Installed CodexHp.exe does not match the downloaded portable executable.'
    }

    & $outsidePackageInvoker -FilePath $installedExecutablePath -Detached | Out-Null
    $deadline = [DateTimeOffset]::Now.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 200
        $runningProcess = Get-Process -Name 'CodexHp' -ErrorAction SilentlyContinue |
            Where-Object {
                [string]::Equals($_.Path, $installedExecutablePath, [StringComparison]::OrdinalIgnoreCase)
            } |
            Select-Object -First 1
    } while ($null -eq $runningProcess -and [DateTimeOffset]::Now -lt $deadline)

    if ($null -eq $runningProcess -or $runningProcess.HasExited) {
        throw 'Installed CodexHp did not remain running after launch.'
    }

    $runningPath = $runningProcess.Path
    if (-not [string]::Equals($runningPath, $installedExecutablePath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Running CodexHp path was '$runningPath'; expected '$installedExecutablePath'."
    }

    Write-Host "Running installed CodexHp $installedVersion from $runningPath"
}

function Restore-InstalledApplicationAfterFailure {
    Stop-CodexHpProcesses
    if (Test-Path -LiteralPath $installedExecutablePath -PathType Leaf) {
        Write-Warning 'Release failed. Restarting the currently installed CodexHp build.'
        & $outsidePackageInvoker -FilePath $installedExecutablePath -Detached | Out-Null
    }
}

Push-Location $repositoryRoot
try {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $logPath = Join-Path $logDirectory ("local-release-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Start-Transcript -LiteralPath $logPath | Out-Null
    $transcriptStarted = $true

    if (-not $AllowUnsignedRelease) {
        throw 'Unsigned publication requires the explicit -AllowUnsignedRelease acknowledgement.'
    }

    $initialStatus = @(& git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the Git working tree.' }
    if ($initialStatus.Count -ne 0) {
        throw "The Git working tree must be clean before a release:`n$($initialStatus -join "`n")"
    }

    Invoke-CheckedCommand git @('fetch', '--prune', '--tags', 'origin') 'git fetch failed.'

    $repositoryTopLevel = Get-CheckedCommandOutput git @('rev-parse', '--show-toplevel') 'Unable to resolve the repository root.'
    if (-not [string]::Equals([IO.Path]::GetFullPath($repositoryTopLevel), $repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release command must run in '$repositoryRoot'."
    }

    $branch = Get-CheckedCommandOutput git @('branch', '--show-current') 'Unable to resolve the current branch.'
    if ($branch -cne 'main') {
        throw "Releases must be created from main. Current branch: '$branch'."
    }

    $headCommit = Get-CheckedCommandOutput git @('rev-parse', 'HEAD') 'Unable to resolve HEAD.'
    $originMainCommit = Get-CheckedCommandOutput git @('rev-parse', 'refs/remotes/origin/main') 'Unable to resolve origin/main.'
    if ($headCommit -cne $originMainCommit) {
        throw "Local main '$headCommit' must exactly match origin/main '$originMainCommit'."
    }

    $postFetchStatus = @(& git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $postFetchStatus.Count -ne 0) {
        throw 'The Git working tree changed during release preflight.'
    }

    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
    $version = if ($null -eq $versionNode) { '' } else { $versionNode.InnerText.Trim() }
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Project Version '$version' must use numeric major.minor.patch format."
    }
    $tag = "v$version"

    $sdkConfiguration = Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json
    $requiredSdkVersion = $sdkConfiguration.sdk.version
    $actualSdkVersion = Get-CheckedCommandOutput dotnet @('--version') 'Unable to read the .NET SDK version.'
    if ($actualSdkVersion -cne $requiredSdkVersion) {
        throw ".NET SDK version '$actualSdkVersion' was found; required version is '$requiredSdkVersion'."
    }
    Write-Host ".NET SDK: $actualSdkVersion"

    $resolvedCompilerPath = Get-InnoSetupCompiler

    $ghVersionText = Get-CheckedCommandOutput gh @('--version') 'Unable to read the GitHub CLI version.'
    if ($ghVersionText -notmatch '^gh version ([0-9]+\.[0-9]+\.[0-9]+)') {
        throw "Unable to parse GitHub CLI version from '$ghVersionText'."
    }
    $actualGhVersion = [version]$Matches[1]
    if ($actualGhVersion -lt $minimumGitHubCliVersion) {
        throw "GitHub CLI version '$actualGhVersion' is older than required '$minimumGitHubCliVersion'."
    }
    Invoke-CheckedCommand gh @('auth', 'status', '--hostname', 'github.com') 'GitHub CLI authentication failed.'
    $repositoryName = Get-CheckedCommandOutput gh @('repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner') 'Unable to verify the GitHub repository.'
    if ($repositoryName -cne $requiredRepository) {
        throw "GitHub CLI resolved '$repositoryName'; expected '$requiredRepository'."
    }

    & git show-ref --verify --quiet "refs/tags/$tag"
    $tagExists = $LASTEXITCODE -eq 0
    if ($LASTEXITCODE -notin @(0, 1)) {
        throw "Unable to inspect tag '$tag'."
    }
    if ($tagExists) {
        $tagCommit = Get-CheckedCommandOutput git @('rev-list', '-n', '1', $tag) "Unable to resolve tag '$tag'."
        if ($tagCommit -cne $headCommit) {
            throw "Tag '$tag' points to '$tagCommit', not current HEAD '$headCommit'."
        }
    }

    $releaseView = @(& gh release view $tag --repo $requiredRepository --json tagName 2>&1)
    $releaseViewExitCode = $LASTEXITCODE
    if ($releaseViewExitCode -eq 0) {
        $releaseExists = $true
    }
    elseif (($releaseView -join "`n") -match 'release not found') {
        $releaseExists = $false
    }
    else {
        throw "Unable to determine whether GitHub Release '$tag' exists:`n$($releaseView -join "`n")"
    }

    $setupName = "CodexHp-Setup-$version-x64.exe"
    $portableName = "CodexHp-Portable-$version-x64.exe"
    $checksumName = 'SHA256SUMS.txt'
    $expectedAssetNames = @($setupName, $portableName, $checksumName)
    $executableNames = @($setupName, $portableName)

    if (-not $releaseExists) {
        & $verifyScript
        if ($LASTEXITCODE -ne 0) { throw "Verify-Core.ps1 failed with exit code $LASTEXITCODE." }

        & $buildInstallerScript -CompilerPath $resolvedCompilerPath -UseExistingVerifiedPublish
        if ($LASTEXITCODE -ne 0) { throw "Build-Installer.ps1 failed with exit code $LASTEXITCODE." }

        & $stageReleaseScript -AllowUnsignedRelease
        if ($LASTEXITCODE -ne 0) { throw "Stage-Release.ps1 failed with exit code $LASTEXITCODE." }

        Assert-ExactAssetSet $releaseDirectory $expectedAssetNames
        Assert-Checksums $releaseDirectory $executableNames

        $portablePath = Join-Path $releaseDirectory $portableName
        $portableVersion = (Get-Item -LiteralPath $portablePath).VersionInfo.ProductVersion
        Assert-VersionMatches $portableVersion $version $portableName
        if (-not $portableVersion.Contains($headCommit, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$portableName product version '$portableVersion' does not contain HEAD commit '$headCommit'."
        }
        $setupVersion = (Get-Item -LiteralPath (Join-Path $releaseDirectory $setupName)).VersionInfo.ProductVersion
        Assert-VersionMatches $setupVersion $version $setupName

        if (-not $tagExists) {
            Invoke-CheckedCommand git @('tag', '-a', $tag, '-m', "CodexHp $version") "Unable to create tag '$tag'."
        }
        Invoke-CheckedCommand git @('push', 'origin', "refs/tags/$tag") "Unable to push tag '$tag'."

        $notes = @'
> [!WARNING]
> This release is not code-signed. Windows SmartScreen or Smart App Control may warn about or block these files. Download them only from this GitHub Release and verify the SHA-256 checksums.

CodexHp {version} for Windows 11.

- `CodexHp-Setup-{version}-x64.exe` is the recommended per-user installer.
- `CodexHp-Portable-{version}-x64.exe` is the secondary portable build.
- `SHA256SUMS.txt` contains the SHA-256 digest for both executables.
- This release is not submitted to WinGet.
'@.Replace('{version}', $version)
        $notesPath = Join-Path $outDirectory "release-notes-$tag.md"
        $notes | Set-Content -LiteralPath $notesPath -Encoding utf8NoBOM

        $assetPaths = $expectedAssetNames | ForEach-Object { Join-Path $releaseDirectory $_ }
        & gh release create $tag @assetPaths `
            --repo $requiredRepository `
            --verify-tag `
            --latest `
            --title "CodexHp $tag" `
            --notes-file $notesPath
        if ($LASTEXITCODE -ne 0) {
            throw "GitHub Release creation failed with exit code $LASTEXITCODE. Re-run this command to verify or resume the release."
        }
    }
    else {
        Write-Warning "GitHub Release '$tag' already exists. Its published assets will be verified and installed; no assets will be replaced."
    }

    $downloadDirectory = Join-Path $outDirectory ("release-download-$tag-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $downloadDirectory | Out-Null
    & gh release download $tag --repo $requiredRepository --dir $downloadDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to download GitHub Release '$tag'. Exit code: $LASTEXITCODE."
    }

    Assert-ExactAssetSet $downloadDirectory $expectedAssetNames
    Assert-Checksums $downloadDirectory $executableNames

    if (Test-Path -LiteralPath $releaseDirectory -PathType Container) {
        foreach ($name in $expectedAssetNames) {
            $localPath = Join-Path $releaseDirectory $name
            if (Test-Path -LiteralPath $localPath -PathType Leaf) {
                $localHash = (Get-FileHash -LiteralPath $localPath -Algorithm SHA256).Hash
                $downloadedHash = (Get-FileHash -LiteralPath (Join-Path $downloadDirectory $name) -Algorithm SHA256).Hash
                if (-not [string]::Equals($localHash, $downloadedHash, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Downloaded release asset '$name' does not match the locally staged artifact."
                }
            }
        }
    }

    $downloadedPortablePath = Join-Path $downloadDirectory $portableName
    $downloadedVersion = (Get-Item -LiteralPath $downloadedPortablePath).VersionInfo.ProductVersion
    Assert-VersionMatches $downloadedVersion $version $portableName
    if (-not $downloadedVersion.Contains($headCommit, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Downloaded portable product version '$downloadedVersion' does not contain HEAD commit '$headCommit'."
    }

    $applicationProcessesStopped = $true
    Stop-CodexHpProcesses
    $downloadedInstallerPath = Join-Path $downloadDirectory $setupName
    & $outsidePackageInvoker -FilePath $downloadedInstallerPath -ArgumentList @(
        '/CURRENTUSER',
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/SP-'
    ) | Out-Null

    & $installationValidator -ExpectedVersion $version -RequireStartupEnabled | Out-Host

    Start-And-VerifyInstalledApplication $version $downloadedPortablePath
    Write-Host "Local release $tag completed successfully. Verification log: $logPath"
}
catch {
    Write-Error $_ -ErrorAction Continue
    if ($applicationProcessesStopped) {
        Restore-InstalledApplicationAfterFailure
    }
    throw
}
finally {
    if ($transcriptStarted) {
        Stop-Transcript | Out-Null
    }
    Pop-Location
}
