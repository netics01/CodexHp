[CmdletBinding()]
param(
    [string]$InstallerPath,
    [string]$TestInstallDirectory = (Join-Path $PSScriptRoot '..\..\out\install-test\CodexHp'),
    [int[]]$ExpectedOverlayBounds,
    [switch]$SkipPixelVerification
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'out')).TrimEnd('\')
$projectPath = Join-Path $repositoryRoot 'src\CodexHp.App\CodexHp.App.csproj'
$publishedAppValidator = Join-Path $PSScriptRoot 'Validate-PublishedApp.ps1'
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$applicationKeyPath = 'HKCU:\Software\netics01\CodexHp'
$uninstallKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{4B302CDD-065E-4C2F-A0CD-DC430E4B03A8}_is1'
$valueName = 'CodexHp'
$settingsDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'CodexHp'
$settingsPath = Join-Path $settingsDirectory 'settings.json'
$settingsBackupPath = Join-Path $settingsDirectory ("settings.json.installer-validation-backup-" + [Guid]::NewGuid().ToString('N'))

function Assert-PathBelowOutDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($outDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer validation paths must stay below '$outDirectory'. Rejected: $fullPath"
    }

    return $fullPath
}

function Invoke-Setup {
    param([Parameter(Mandatory)][string]$Path)

    $arguments = @(
        '/CURRENTUSER',
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/SP-',
        "/DIR=`"$testInstallDirectoryFull`""
    )
    $process = Start-Process -FilePath $Path -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Installer exited with code $($process.ExitCode)."
    }
}

function Read-RunValue {
    if (-not (Test-Path -LiteralPath $runKeyPath)) {
        return $null
    }

    $key = Get-Item -LiteralPath $runKeyPath
    return $key.GetValue($valueName, $null)
}

function Read-InstallPath {
    if (-not (Test-Path -LiteralPath $applicationKeyPath)) {
        return $null
    }

    $key = Get-Item -LiteralPath $applicationKeyPath
    return $key.GetValue('InstallPath', $null)
}

if (@(Get-Process -Name 'CodexHp' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Close the existing CodexHp process before installer validation.'
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($null -eq $versionNode) { '' } else { $versionNode.InnerText.Trim() }
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'CodexHp.App.csproj must declare a Version.'
}

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $outDirectory "installer\CodexHp-Setup-$version-x64.exe"
}

$installerPathFull = Assert-PathBelowOutDirectory $InstallerPath
$testInstallDirectoryFull = Assert-PathBelowOutDirectory $TestInstallDirectory
if (-not (Test-Path -LiteralPath $installerPathFull -PathType Leaf)) {
    throw "Installer was not found: $installerPathFull"
}

if ($null -ne (Read-InstallPath) -or
    (Test-Path -LiteralPath $uninstallKeyPath)) {
    throw 'Installer validation cannot run while CodexHp is already registered as an installed application.'
}

$originalRunValue = Read-RunValue
$originalRunValueExists = $null -ne $originalRunValue
$uninstallerPath = Join-Path $testInstallDirectoryFull 'unins000.exe'
$installedExecutablePath = Join-Path $testInstallDirectoryFull 'CodexHp.exe'
$installedByValidator = $false
$settingsOriginallyExisted = Test-Path -LiteralPath $settingsPath -PathType Leaf
$settingsTemporarilyMoved = $false

try {
    if ($settingsOriginallyExisted) {
        Move-Item -LiteralPath $settingsPath -Destination $settingsBackupPath
        $settingsTemporarilyMoved = $true
    }

    Invoke-Setup $installerPathFull
    $installedByValidator = $true

    if (-not (Test-Path -LiteralPath $installedExecutablePath -PathType Leaf)) {
        throw "Installed executable was not found: $installedExecutablePath"
    }

    $productVersion = (Get-Item -LiteralPath $installedExecutablePath).VersionInfo.ProductVersion
    if (-not $productVersion.StartsWith($version + '.', [StringComparison]::OrdinalIgnoreCase) -and
        -not $productVersion.StartsWith($version + '+', [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals($productVersion, $version, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installed product version was '$productVersion'; expected '$version'."
    }

    $expectedRunValue = '"' + $installedExecutablePath + '"'
    if (-not [string]::Equals((Read-RunValue), $expectedRunValue, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The first install did not register CodexHp for Windows startup.'
    }

    $validatorArguments = @{
        PublishDirectory = $testInstallDirectoryFull
        AllowInstallerFiles = $true
        SkipPixelVerification = $SkipPixelVerification
    }
    if ($PSBoundParameters.ContainsKey('ExpectedOverlayBounds')) {
        $validatorArguments.ExpectedOverlayBounds = $ExpectedOverlayBounds
    }

    & $publishedAppValidator @validatorArguments | Out-Host

    Remove-ItemProperty -LiteralPath $runKeyPath -Name $valueName -ErrorAction Stop
    Invoke-Setup $installerPathFull
    if ($null -ne (Read-RunValue)) {
        throw 'Windows startup was re-enabled during upgrade after the user disabled it.'
    }

    if (-not (Test-Path -LiteralPath $uninstallerPath -PathType Leaf)) {
        throw "Uninstaller was not found: $uninstallerPath"
    }

    $uninstallProcess = Start-Process -FilePath $uninstallerPath `
        -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($uninstallProcess.ExitCode)."
    }

    $installedByValidator = $false
    $cleanupDeadline = [DateTimeOffset]::Now.AddSeconds(10)
    while (((Test-Path -LiteralPath $installedExecutablePath) -or
            $null -ne (Read-InstallPath) -or
            (Test-Path -LiteralPath $uninstallKeyPath)) -and
        [DateTimeOffset]::Now -lt $cleanupDeadline) {
        Start-Sleep -Milliseconds 100
    }

    if (Test-Path -LiteralPath $installedExecutablePath) {
        throw 'Uninstall did not remove CodexHp.exe.'
    }
    if ($null -ne (Read-InstallPath) -or
        (Test-Path -LiteralPath $uninstallKeyPath)) {
        throw 'Uninstall did not remove CodexHp installer registration.'
    }
    if ($null -ne (Read-RunValue)) {
        throw 'Uninstall did not remove the CodexHp Windows startup entry.'
    }

    [pscustomobject]@{
        Installer = $installerPathFull
        Version = $version
        FirstInstallStartup = $true
        UpgradePreservedDisabledStartup = $true
        UninstallClean = $true
    }
}
finally {
    if ($installedByValidator -and (Test-Path -LiteralPath $uninstallerPath -PathType Leaf)) {
        $cleanupProcess = Start-Process -FilePath $uninstallerPath `
            -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
            -WindowStyle Hidden `
            -Wait `
            -PassThru
        if ($cleanupProcess.ExitCode -ne 0) {
            Write-Warning "Cleanup uninstaller exited with code $($cleanupProcess.ExitCode)."
        }
    }

    if (Test-Path -LiteralPath $testInstallDirectoryFull) {
        Remove-Item -LiteralPath $testInstallDirectoryFull -Recurse -Force
    }

    if ($originalRunValueExists) {
        New-Item -Path $runKeyPath -Force | Out-Null
        Set-ItemProperty -LiteralPath $runKeyPath -Name $valueName -Value $originalRunValue -Type String
    }
    elseif (Test-Path -LiteralPath $runKeyPath) {
        Remove-ItemProperty -LiteralPath $runKeyPath -Name $valueName -ErrorAction SilentlyContinue
    }

    if ($settingsTemporarilyMoved) {
        if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
            Remove-Item -LiteralPath $settingsPath -Force
        }

        Move-Item -LiteralPath $settingsBackupPath -Destination $settingsPath
    }
    elseif (-not $settingsOriginallyExisted -and
        (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
        Remove-Item -LiteralPath $settingsPath -Force
    }
}
