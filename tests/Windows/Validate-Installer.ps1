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
$outsidePackageInvoker = Join-Path $repositoryRoot 'scripts\Invoke-OutsidePackage.ps1'
$hkeyUsers = [uint32]2147483651
$userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$runSubKey = "$userSid\Software\Microsoft\Windows\CurrentVersion\Run"
$applicationSubKey = "$userSid\Software\netics01\CodexHp"
$uninstallSubKey = "$userSid\Software\Microsoft\Windows\CurrentVersion\Uninstall\{4B302CDD-065E-4C2F-A0CD-DC430E4B03A8}_is1"
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
    & $outsidePackageInvoker -FilePath $Path -ArgumentList $arguments | Out-Null
}

function Get-RealRegistryString {
    param(
        [Parameter(Mandatory)][string]$SubKey,
        [Parameter(Mandatory)][string]$Name
    )

    $result = Invoke-CimMethod `
        -Namespace root/default `
        -ClassName StdRegProv `
        -MethodName GetStringValue `
        -Arguments @{
            hDefKey = $hkeyUsers
            sSubKeyName = $SubKey
            sValueName = $Name
        }
    if ($result.ReturnValue -in @(1, 2)) {
        return $null
    }
    if ($result.ReturnValue -ne 0) {
        throw "Unable to read real registry value '$SubKey\\$Name'. Return code: $($result.ReturnValue)."
    }

    return $result.sValue
}

function Set-RealRegistryString {
    param(
        [Parameter(Mandatory)][string]$SubKey,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Value
    )

    $result = Invoke-CimMethod `
        -Namespace root/default `
        -ClassName StdRegProv `
        -MethodName SetStringValue `
        -Arguments @{
            hDefKey = $hkeyUsers
            sSubKeyName = $SubKey
            sValueName = $Name
            sValue = $Value
        }
    if ($result.ReturnValue -ne 0) {
        throw "Unable to write real registry value '$SubKey\\$Name'. Return code: $($result.ReturnValue)."
    }
}

function Remove-RealRegistryValue {
    param(
        [Parameter(Mandatory)][string]$SubKey,
        [Parameter(Mandatory)][string]$Name
    )

    $result = Invoke-CimMethod `
        -Namespace root/default `
        -ClassName StdRegProv `
        -MethodName DeleteValue `
        -Arguments @{
            hDefKey = $hkeyUsers
            sSubKeyName = $SubKey
            sValueName = $Name
        }
    if ($result.ReturnValue -notin @(0, 1, 2)) {
        throw "Unable to remove real registry value '$SubKey\\$Name'. Return code: $($result.ReturnValue)."
    }
}

function Read-RunValue {
    return Get-RealRegistryString $runSubKey $valueName
}

function Test-UninstallRegistration {
    return $null -ne (Get-RealRegistryString $uninstallSubKey 'DisplayName')
}

function Read-InstallPath {
    return Get-RealRegistryString $applicationSubKey 'InstallPath'
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

if ($null -ne (Read-InstallPath) -or (Test-UninstallRegistration)) {
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

    Remove-RealRegistryValue $runSubKey $valueName
    Invoke-Setup $installerPathFull
    if ($null -ne (Read-RunValue)) {
        throw 'Windows startup was re-enabled during upgrade after the user disabled it.'
    }

    if (-not (Test-Path -LiteralPath $uninstallerPath -PathType Leaf)) {
        throw "Uninstaller was not found: $uninstallerPath"
    }

    & $outsidePackageInvoker `
        -FilePath $uninstallerPath `
        -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') |
        Out-Null

    $installedByValidator = $false
    $cleanupDeadline = [DateTimeOffset]::Now.AddSeconds(10)
    while (((Test-Path -LiteralPath $installedExecutablePath) -or
            $null -ne (Read-InstallPath) -or
            (Test-UninstallRegistration)) -and
        [DateTimeOffset]::Now -lt $cleanupDeadline) {
        Start-Sleep -Milliseconds 100
    }

    if (Test-Path -LiteralPath $installedExecutablePath) {
        throw 'Uninstall did not remove CodexHp.exe.'
    }
    if ($null -ne (Read-InstallPath) -or (Test-UninstallRegistration)) {
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
        try {
            & $outsidePackageInvoker `
                -FilePath $uninstallerPath `
                -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') |
                Out-Null
        }
        catch {
            Write-Warning "Cleanup uninstaller failed: $($_.Exception.Message)"
        }
    }

    if (Test-Path -LiteralPath $testInstallDirectoryFull) {
        Remove-Item -LiteralPath $testInstallDirectoryFull -Recurse -Force
    }

    if ($originalRunValueExists) {
        Set-RealRegistryString $runSubKey $valueName $originalRunValue
    }
    else {
        Remove-RealRegistryValue $runSubKey $valueName
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
