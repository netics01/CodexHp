[CmdletBinding()]
param(
    [ValidateSet('Development', 'Official')][string]$BuildFlavor = 'Development',
    [string]$ExpectedSdkVersion
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $repositoryRoot 'CodexHp.slnx'
$projectPath = Join-Path $repositoryRoot 'src\CodexHp.App\CodexHp.App.csproj'
$publishRoot = Join-Path $repositoryRoot 'out'
$publishDirectory = Join-Path $publishRoot 'win-x64'
$temporaryPublishDirectory = Join-Path $publishRoot ('.publish-' + [Guid]::NewGuid().ToString('N'))
$maximumPublishedExecutableBytes = 100MB

$publishRootFull = [IO.Path]::GetFullPath($publishRoot).TrimEnd('\')
$publishDirectoryFull = [IO.Path]::GetFullPath($publishDirectory)
$temporaryPublishDirectoryFull = [IO.Path]::GetFullPath($temporaryPublishDirectory)
$requiredPrefix = $publishRootFull + '\'
if (-not $publishDirectoryFull.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not $temporaryPublishDirectoryFull.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish directories must stay below the repository out directory.'
}

Push-Location $repositoryRoot
try {
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    $sdkVersion = & $dotnet --version
    if ($LASTEXITCODE -ne 0) { throw "dotnet --version failed with exit code $LASTEXITCODE." }
    $sdkVersion = $sdkVersion.Trim()
    if ($ExpectedSdkVersion -and $sdkVersion -cne $ExpectedSdkVersion) {
        throw ".NET SDK selection changed from '$ExpectedSdkVersion' to '$sdkVersion' before verification."
    }

    $installedSdks = & $dotnet --list-sdks
    if ($LASTEXITCODE -ne 0) { throw "dotnet --list-sdks failed with exit code $LASTEXITCODE." }
    $sdkPattern = '^' + [regex]::Escape($sdkVersion) + ' \[(.+)\]$'
    $sdkEntry = $installedSdks | Select-String -Pattern $sdkPattern | Select-Object -First 1
    if ($null -eq $sdkEntry) {
        throw "Could not locate the selected .NET SDK '$sdkVersion'."
    }

    # Run the selected SDK's CLI directly so each command uses the same SDK.
    $sdkCli = Join-Path $sdkEntry.Matches[0].Groups[1].Value "$sdkVersion\dotnet.dll"
    Write-Host ".NET SDK: $sdkVersion"

    & $dotnet exec $sdkCli restore $solutionPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    & $dotnet exec $sdkCli build $solutionPath --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    & $dotnet exec $sdkCli test $solutionPath --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }

    New-Item -ItemType Directory -Path $temporaryPublishDirectoryFull -Force | Out-Null
    & $dotnet exec $sdkCli publish $projectPath -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:CodexHpBuildFlavor=$BuildFlavor `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $temporaryPublishDirectoryFull
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    $publishedFiles = @(Get-ChildItem -LiteralPath $temporaryPublishDirectoryFull -File)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'CodexHp.exe') {
        throw "Single-file publish must contain only CodexHp.exe. Found: $($publishedFiles.Name -join ', ')"
    }

    if ($publishedFiles[0].Length -gt $maximumPublishedExecutableBytes) {
        $publishedSizeMiB = $publishedFiles[0].Length / 1MB
        throw "Published CodexHp.exe exceeds the 100 MiB size budget. Actual: $($publishedSizeMiB.ToString('N2')) MiB."
    }

    $publishedSizeMiB = $publishedFiles[0].Length / 1MB
    Write-Host "Published CodexHp.exe: $($publishedSizeMiB.ToString('N2')) MiB"

    if (Test-Path -LiteralPath $publishDirectoryFull) {
        Remove-Item -LiteralPath $publishDirectoryFull -Recurse -Force
    }
    Move-Item -LiteralPath $temporaryPublishDirectoryFull -Destination $publishDirectoryFull

    & git diff --check
    if ($LASTEXITCODE -ne 0) { throw "git diff --check failed with exit code $LASTEXITCODE." }
}
finally {
    if (Test-Path -LiteralPath $temporaryPublishDirectoryFull) {
        Remove-Item -LiteralPath $temporaryPublishDirectoryFull -Recurse -Force
    }
    Pop-Location
}
