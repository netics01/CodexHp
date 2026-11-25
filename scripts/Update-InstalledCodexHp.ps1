#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$repository = 'netics01/CodexHp'
$installedExecutable = Join-Path $env:LOCALAPPDATA 'Programs\CodexHp\CodexHp.exe'
$installationValidator = Join-Path $PSScriptRoot 'Test-WindowsInstallation.ps1'
$outsidePackageInvoker = Join-Path $PSScriptRoot 'Invoke-OutsidePackage.ps1'

function Get-PublishedDigest {
    param([Parameter(Mandatory)][string]$AssetName)

    $matchesByName = @($release.assets | Where-Object name -CEQ $AssetName)
    if ($matchesByName.Count -ne 1 -or $matchesByName[0].digest -notmatch '^sha256:([a-f0-9]{64})$') {
        throw "GitHub Release $tag must provide one SHA-256 digest for '$AssetName'."
    }

    return $Matches[1]
}

function Start-InstalledApplication {
    & $outsidePackageInvoker -FilePath $installedExecutable -Detached | Out-Null
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $running = Get-Process -Name CodexHp -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $installedExecutable -and -not $_.HasExited }
        if ($running) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'Installed CodexHp did not stay running after launch.'
}

$updateLock = [Threading.Mutex]::new($false, 'Local\CodexHp.UpdateInstallation')
try {
    try {
        $ownsLock = $updateLock.WaitOne(0)
    }
    catch [Threading.AbandonedMutexException] {
        $ownsLock = $true
    }
    if (-not $ownsLock) { throw 'Another CodexHp update is already running.' }

    if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
        Write-Output "No installed CodexHp found at $installedExecutable."
        return
    }

    $installedVersion = (Get-Item -LiteralPath $installedExecutable).VersionInfo.ProductVersion
    if ($installedVersion -notmatch '^(\d+\.\d+\.\d+)(?:[.+].*)?$') {
        throw "Unable to read the installed CodexHp version from '$installedVersion'."
    }
    $currentVersion = [version]$Matches[1]
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases/latest" -TimeoutSec 30
    $tag = $release.tag_name
    if ($release.draft -or $release.prerelease -or $tag -cnotmatch '^v(\d+\.\d+\.\d+)$') {
        throw "Expected a stable CodexHp release, but GitHub returned '$tag'."
    }
    $latestVersion = [version]$Matches[1]
    if ($latestVersion -le $currentVersion) {
        Write-Output "No update needed. Installed: $currentVersion. Latest GitHub release: $tag."
        return
    }

    $setupName = "CodexHp-Setup-$latestVersion-x64.exe"
    $portableName = "CodexHp-Portable-$latestVersion-x64.exe"
    $checksumName = 'SHA256SUMS.txt'
    $setupDigest = Get-PublishedDigest $setupName
    $portableDigest = Get-PublishedDigest $portableName
    $checksumDigest = Get-PublishedDigest $checksumName

    # The installer checks the same single-instance mutex that the development build holds.
    $running = @(Get-Process -Name CodexHp -ErrorAction SilentlyContinue)
    if ($running | Where-Object { $_.Path -ne $installedExecutable }) {
        Write-Output "Update to $tag deferred. Exit the running development or portable build and run this command again."
        return
    }

    Write-Host "Downloading CodexHp $tag (installed: $currentVersion)."
    $downloadDirectory = Join-Path ([IO.Path]::GetTempPath()) ("CodexHp-update-$tag-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $downloadDirectory | Out-Null
    $downloadBase = "https://github.com/$repository/releases/download/$tag"
    $checksumPath = Join-Path $downloadDirectory $checksumName
    $setupPath = Join-Path $downloadDirectory $setupName
    Invoke-WebRequest -Uri "$downloadBase/$checksumName" -OutFile $checksumPath -TimeoutSec 30
    $expectedChecksums = @("$setupDigest  $setupName", "$portableDigest  $portableName")
    if ((Get-FileHash -LiteralPath $checksumPath).Hash -ne $checksumDigest -or
        (Compare-Object $expectedChecksums @(Get-Content -LiteralPath $checksumPath))) {
        throw "SHA256SUMS.txt does not match the published digests for $tag."
    }
    Invoke-WebRequest -Uri "$downloadBase/$setupName" -OutFile $setupPath -TimeoutSec 300
    if ((Get-FileHash -LiteralPath $setupPath).Hash -ne $setupDigest) {
        throw "Installer does not match the published SHA-256 digest for $tag."
    }

    $before = & $installationValidator -ExpectedVersion $currentVersion.ToString() -AllowMissingStartupRegistration
    $running = @(Get-Process -Name CodexHp -ErrorAction SilentlyContinue)
    if ($running | Where-Object { $_.Path -ne $installedExecutable }) {
        Write-Output "Update to $tag deferred because another CodexHp build started during the download."
        return
    }

    $previousDigest = (Get-FileHash -LiteralPath $installedExecutable).Hash
    $restartInstalledApplication = $false
    try {
        foreach ($process in $running) {
            Stop-Process -InputObject $process -Force
            $restartInstalledApplication = $true
            if (-not $process.WaitForExit(10000)) { throw 'Installed CodexHp did not exit.' }
        }

        $startupTask = if ($before.StartupEnabled) { '/TASKS=autostart' } else { '/TASKS=' }
        $installerArguments = @('/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', $startupTask)
        & $outsidePackageInvoker -FilePath $setupPath -ArgumentList $installerArguments | Out-Null

        $after = & $installationValidator -ExpectedVersion $latestVersion.ToString() -AllowMissingStartupRegistration
        if ($after.StartupEnabled -ne $before.StartupEnabled -or $after.StartupCommand -ne $before.StartupCommand) {
            throw 'Windows startup registration changed during the update.'
        }
        if ((Get-FileHash -LiteralPath $installedExecutable).Hash -ne $portableDigest) {
            throw "Installed CodexHp does not match the published executable for $tag."
        }
        if ($restartInstalledApplication) { Start-InstalledApplication }

        Write-Output "Updated CodexHp from $currentVersion to $latestVersion. Windows startup enabled: $($after.StartupEnabled)."
    }
    catch {
        if ($restartInstalledApplication -and -not (Get-Process -Name CodexHp -ErrorAction SilentlyContinue) -and
            (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
            try {
                if ((Get-FileHash -LiteralPath $installedExecutable).Hash -in @($previousDigest, $portableDigest)) {
                    Start-InstalledApplication
                }
            }
            catch {
                Write-Warning "Unable to restart installed CodexHp: $_"
            }
        }
        throw
    }
}
finally {
    if ($ownsLock) { $updateLock.ReleaseMutex() }
    $updateLock.Dispose()
}
