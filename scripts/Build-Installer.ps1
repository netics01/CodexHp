[CmdletBinding()]
param(
    [string]$CompilerPath,
    [switch]$UseExistingVerifiedPublish
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repositoryRoot 'src\CodexHp.App\CodexHp.App.csproj'
$verifyScript = Join-Path $repositoryRoot 'scripts\Verify-Core.ps1'
$installerDefinition = Join-Path $repositoryRoot 'installer\CodexHp.iss'
$sourceExe = Join-Path $repositoryRoot 'out\win-x64\CodexHp.exe'
$outputDirectory = Join-Path $repositoryRoot 'out\installer'
$outDirectoryFull = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'out')).TrimEnd('\')
$outputDirectoryFull = [IO.Path]::GetFullPath($outputDirectory)
if (-not $outputDirectoryFull.StartsWith(
        $outDirectoryFull + '\',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Installer output directory must stay below '$outDirectoryFull'."
}

if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $compilerCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $compilerCandidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $compilerCandidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
    }

    $CompilerPath = $compilerCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw "ISCC.exe was not found at '$CompilerPath'. Install Inno Setup 6 or pass -CompilerPath."
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($null -eq $versionNode) { '' } else { $versionNode.InnerText.Trim() }
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'CodexHp.App.csproj must declare a Version.'
}

if ($UseExistingVerifiedPublish) {
    if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
        throw "The verified publish executable was not found: $sourceExe"
    }
}
else {
    & $verifyScript
    if ($LASTEXITCODE -ne 0) {
        throw "Verify-Core.ps1 failed with exit code $LASTEXITCODE."
    }
}

if (Test-Path -LiteralPath $outputDirectoryFull) {
    Remove-Item -LiteralPath $outputDirectoryFull -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDirectoryFull -Force | Out-Null
& $CompilerPath `
    "/DAppVersion=$version" `
    "/DSourceExe=$sourceExe" `
    "/DOutputDirectory=$outputDirectoryFull" `
    $installerDefinition
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputDirectoryFull "CodexHp-Setup-$version-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Expected installer was not created: $installerPath"
}

Get-Item -LiteralPath $installerPath
