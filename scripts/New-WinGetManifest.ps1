[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][ValidatePattern('^https://')][string]$InstallerUrl,
    [string]$OutputRoot,
    [switch]$AllowUnsignedDevelopmentBuild
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'out')).TrimEnd('\')
$projectPath = Join-Path $repositoryRoot 'src\CodexHp.App\CodexHp.App.csproj'
$packageIdentifier = 'netics01.CodexHp'
$manifestVersion = '1.12.0'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($null -eq $versionNode) { '' } else { $versionNode.InnerText.Trim() }
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'CodexHp.App.csproj must declare a Version.'
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $outDirectory 'winget\manifests'
}

$installerPathFull = [IO.Path]::GetFullPath($InstallerPath)
$outputRootFull = [IO.Path]::GetFullPath($OutputRoot)
if (-not $installerPathFull.StartsWith($outDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not $outputRootFull.StartsWith($outDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "WinGet generation paths must stay below '$outDirectory'."
}
if (-not (Test-Path -LiteralPath $installerPathFull -PathType Leaf)) {
    throw "Installer was not found: $installerPathFull"
}

$signature = Get-AuthenticodeSignature -LiteralPath $installerPathFull
if (-not $AllowUnsignedDevelopmentBuild -and
    $signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Installer signature status was '$($signature.Status)'; expected Valid."
}
if ($AllowUnsignedDevelopmentBuild -and
    $signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    Write-Warning 'Generating development-only manifests for an installer without a valid signature.'
}

$packageDirectory = Join-Path $outputRootFull "n\netics01\CodexHp\$version"
if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
$installerHash = (Get-FileHash -LiteralPath $installerPathFull -Algorithm SHA256).Hash.ToUpperInvariant()

$versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$manifestVersion.schema.json

PackageIdentifier: $packageIdentifier
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $manifestVersion
"@

$installerManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$manifestVersion.schema.json

PackageIdentifier: $packageIdentifier
PackageVersion: $version
InstallerType: inno
Scope: user
UpgradeBehavior: install
ProductCode: '{4B302CDD-065E-4C2F-A0CD-DC430E4B03A8}_is1'
MinimumOSVersion: 10.0.22000.0
InstallerSwitches:
  Silent: /CURRENTUSER /SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
  SilentWithProgress: /CURRENTUSER /SP- /SILENT /SUPPRESSMSGBOXES /NORESTART
Installers:
  - Architecture: x64
    InstallerUrl: $InstallerUrl
    InstallerSha256: $installerHash
    AppsAndFeaturesEntries:
      - DisplayName: CodexHp
        Publisher: netics01
        DisplayVersion: $version
        ProductCode: '{4B302CDD-065E-4C2F-A0CD-DC430E4B03A8}_is1'
        InstallerType: inno
    InstallationMetadata:
      DefaultInstallLocation: '%LocalAppData%\Programs\CodexHp'
ManifestType: installer
ManifestVersion: $manifestVersion
"@

$defaultLocaleManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$manifestVersion.schema.json

PackageIdentifier: $packageIdentifier
PackageVersion: $version
PackageLocale: en-US
Publisher: netics01
PublisherUrl: https://github.com/netics01
PublisherSupportUrl: https://github.com/netics01/CodexHp/issues
PackageName: CodexHp
PackageUrl: https://github.com/netics01/CodexHp
License: Apache-2.0
LicenseUrl: https://github.com/netics01/CodexHp/blob/main/LICENSE
ShortDescription: A Windows 11 companion that keeps Codex usage and limits visible at a glance.
Tags:
  - codex
  - openai
  - taskbar
  - usage-monitor
  - windows-11
ManifestType: defaultLocale
ManifestVersion: $manifestVersion
"@

$koreanLocaleManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.$manifestVersion.schema.json

PackageIdentifier: $packageIdentifier
PackageVersion: $version
PackageLocale: ko-KR
Publisher: netics01
PackageName: CodexHp
ShortDescription: Codex 사용량과 한도를 한눈에 보여 주는 Windows 11 컴패니언 프로그램입니다.
ManifestType: locale
ManifestVersion: $manifestVersion
"@

$manifests = @{
    "$packageIdentifier.yaml" = $versionManifest
    "$packageIdentifier.installer.yaml" = $installerManifest
    "$packageIdentifier.locale.en-US.yaml" = $defaultLocaleManifest
    "$packageIdentifier.locale.ko-KR.yaml" = $koreanLocaleManifest
}
foreach ($entry in $manifests.GetEnumerator()) {
    $path = Join-Path $packageDirectory $entry.Key
    $entry.Value.TrimStart() + [Environment]::NewLine |
        Set-Content -LiteralPath $path -Encoding utf8NoBOM
}

Get-ChildItem -LiteralPath $packageDirectory -File | Sort-Object Name
