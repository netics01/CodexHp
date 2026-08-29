[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repositoryRoot 'src\CodexHp.App\CodexHp.App.csproj'

Push-Location $repositoryRoot
try {
    & dotnet run --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw "CodexHp development run failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
