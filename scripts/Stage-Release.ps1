[CmdletBinding()]
param(
    [string]$PortableExecutablePath,
    [string]$InstallerPath,
    [string]$OutputDirectory,
    [switch]$AllowUnsignedRelease
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'out')).TrimEnd('\')
$projectPath = Join-Path $repositoryRoot 'src\CodexHp.App\CodexHp.App.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($null -eq $versionNode) { '' } else { $versionNode.InnerText.Trim() }
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'CodexHp.App.csproj must declare a Version.'
}

if ([string]::IsNullOrWhiteSpace($PortableExecutablePath)) {
    $PortableExecutablePath = Join-Path $outDirectory 'win-x64\CodexHp.exe'
}
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $outDirectory "installer\CodexHp-Setup-$version-x64.exe"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $outDirectory 'release'
}

function Assert-PathBelowOutDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($outDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release paths must stay below '$outDirectory'. Rejected: $fullPath"
    }

    return $fullPath
}

function Assert-ReleaseArtifact {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Release artifact was not found: $Path"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if (-not $AllowUnsignedRelease -and
        $signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Signature status for '$Path' was '$($signature.Status)'; expected Valid."
    }
    if ($AllowUnsignedRelease -and
        $signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        Write-Warning "Staging an explicitly approved unsigned release artifact: $Path"
    }
}

$portableExecutablePathFull = Assert-PathBelowOutDirectory $PortableExecutablePath
$installerPathFull = Assert-PathBelowOutDirectory $InstallerPath
$outputDirectoryFull = Assert-PathBelowOutDirectory $OutputDirectory
Assert-ReleaseArtifact $portableExecutablePathFull
Assert-ReleaseArtifact $installerPathFull

if (Test-Path -LiteralPath $outputDirectoryFull) {
    Remove-Item -LiteralPath $outputDirectoryFull -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDirectoryFull -Force | Out-Null

$portableAssetPath = Join-Path $outputDirectoryFull "CodexHp-Portable-$version-x64.exe"
$installerAssetPath = Join-Path $outputDirectoryFull "CodexHp-Setup-$version-x64.exe"
Copy-Item -LiteralPath $portableExecutablePathFull -Destination $portableAssetPath
Copy-Item -LiteralPath $installerPathFull -Destination $installerAssetPath

$checksumPath = Join-Path $outputDirectoryFull 'SHA256SUMS.txt'
$checksumLines = @($installerAssetPath, $portableAssetPath) | ForEach-Object {
    $item = Get-Item -LiteralPath $_
    $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($item.Name)"
}
$checksumLines | Set-Content -LiteralPath $checksumPath -Encoding utf8NoBOM

Get-ChildItem -LiteralPath $outputDirectoryFull -File | Sort-Object Name
