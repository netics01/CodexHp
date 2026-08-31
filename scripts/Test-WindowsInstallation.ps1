[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$ExpectedVersion,
    [switch]$RequireStartupEnabled,
    [ValidateRange(1, 120)][int]$StartMenuTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$hkeyUsers = [uint32]2147483651
$missingRegistryValueReturnCodes = @(1, 2)
$userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$installedDirectory = Join-Path $env:LOCALAPPDATA 'Programs\CodexHp'
$installedExecutablePath = Join-Path $installedDirectory 'CodexHp.exe'
$startMenuShortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\CodexHp\CodexHp.lnk'
$runSubKey = "$userSid\Software\Microsoft\Windows\CurrentVersion\Run"
$startupApprovedSubKey = "$userSid\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
$applicationSubKey = "$userSid\Software\netics01\CodexHp"
$uninstallSubKey = "$userSid\Software\Microsoft\Windows\CurrentVersion\Uninstall\{4B302CDD-065E-4C2F-A0CD-DC430E4B03A8}_is1"

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
    if ($result.ReturnValue -in $missingRegistryValueReturnCodes) {
        return $null
    }
    if ($result.ReturnValue -ne 0) {
        throw "Unable to read real registry value '$SubKey\\$Name'. Return code: $($result.ReturnValue)."
    }

    return $result.sValue
}

function Get-RealRegistryBinary {
    param(
        [Parameter(Mandatory)][string]$SubKey,
        [Parameter(Mandatory)][string]$Name
    )

    $result = Invoke-CimMethod `
        -Namespace root/default `
        -ClassName StdRegProv `
        -MethodName GetBinaryValue `
        -Arguments @{
            hDefKey = $hkeyUsers
            sSubKeyName = $SubKey
            sValueName = $Name
        }
    if ($result.ReturnValue -in $missingRegistryValueReturnCodes) {
        return $null
    }
    if ($result.ReturnValue -ne 0) {
        throw "Unable to read real registry value '$SubKey\\$Name'. Return code: $($result.ReturnValue)."
    }

    return [byte[]]$result.uValue
}

function Test-RealFileExists {
    param([Parameter(Mandatory)][string]$Path)

    $escapedPath = $Path.Replace('\', '\\').Replace("'", "''")
    return $null -ne (Get-CimInstance CIM_DataFile -Filter "Name='$escapedPath'" -ErrorAction Stop)
}

if (-not (Test-RealFileExists $installedExecutablePath)) {
    throw "Windows service cannot see the installed executable: $installedExecutablePath"
}

$installedVersion = (Get-Item -LiteralPath $installedExecutablePath).VersionInfo.ProductVersion
if (-not [string]::Equals($installedVersion, $ExpectedVersion, [StringComparison]::OrdinalIgnoreCase) -and
    -not $installedVersion.StartsWith($ExpectedVersion + '.', [StringComparison]::OrdinalIgnoreCase) -and
    -not $installedVersion.StartsWith($ExpectedVersion + '+', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Installed CodexHp.exe reports version '$installedVersion'; expected '$ExpectedVersion'."
}

$expectedRunValue = '"' + $installedExecutablePath + '"'
$actualRunValue = Get-RealRegistryString $runSubKey 'CodexHp'
if (-not [string]::Equals($actualRunValue, $expectedRunValue, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Real Windows startup path is '$actualRunValue'; expected '$expectedRunValue'."
}

$actualInstallPath = Get-RealRegistryString $applicationSubKey 'InstallPath'
if (-not [string]::Equals($actualInstallPath.TrimEnd('\'), $installedDirectory.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Real CodexHp install path is '$actualInstallPath'; expected '$installedDirectory'."
}

$actualDisplayName = Get-RealRegistryString $uninstallSubKey 'DisplayName'
if (-not [string]::Equals($actualDisplayName, "CodexHp $ExpectedVersion", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Real uninstall registration is '$actualDisplayName'; expected 'CodexHp $ExpectedVersion'."
}

$approval = Get-RealRegistryBinary $startupApprovedSubKey 'CodexHp'
$startupEnabled = $null -eq $approval -or ($approval.Length -gt 0 -and $approval[0] -eq 2)
if ($RequireStartupEnabled -and -not $startupEnabled) {
    $renderedApproval = [BitConverter]::ToString($approval)
    throw "Real Windows startup approval is disabled or invalid: $renderedApproval"
}

if (-not (Test-RealFileExists $startMenuShortcutPath)) {
    throw "Windows service cannot see the Start menu shortcut: $startMenuShortcutPath"
}

$deadline = [DateTimeOffset]::Now.AddSeconds($StartMenuTimeoutSeconds)
do {
    $startApp = Get-StartApps | Where-Object { $_.Name -eq 'CodexHp' } | Select-Object -First 1
    if ($null -eq $startApp) {
        Start-Sleep -Milliseconds 250
    }
} while ($null -eq $startApp -and [DateTimeOffset]::Now -lt $deadline)
if ($null -eq $startApp) {
    throw 'Windows Start menu does not expose CodexHp.'
}

[pscustomobject]@{
    InstalledExecutable = $installedExecutablePath
    ProductVersion = $installedVersion
    StartupCommand = $actualRunValue
    StartupEnabled = $startupEnabled
    StartMenuShortcut = $startMenuShortcutPath
    StartAppId = $startApp.AppID
    UninstallDisplayName = $actualDisplayName
}
