[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FilePath,
    [Parameter()][string[]]$ArgumentList = @(),
    [Parameter()][string]$WorkingDirectory,
    [ValidateRange(1, 3600)][int]$TimeoutSeconds = 300,
    [switch]$Detached
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$taskActionExecute = 0
$taskCreateOrUpdate = 6
$taskLogonInteractiveToken = 3
$taskStateQueued = 2
$taskStateRunning = 4
$successfulDetachedExitCodes = @(0, 1)

function ConvertTo-CommandLineArgument {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
        return $Value
    }

    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

$filePathFull = [IO.Path]::GetFullPath($FilePath)
if (-not (Test-Path -LiteralPath $filePathFull -PathType Leaf)) {
    throw "Executable was not found: $filePathFull"
}

if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $workingDirectoryFull = Split-Path -Parent $filePathFull
}
else {
    $workingDirectoryFull = [IO.Path]::GetFullPath($WorkingDirectory)
}
if (-not (Test-Path -LiteralPath $workingDirectoryFull -PathType Container)) {
    throw "Working directory was not found: $workingDirectoryFull"
}

$taskName = 'CodexHp-OutsidePackage-' + [Guid]::NewGuid().ToString('N')
$scheduler = New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
$rootFolder = $scheduler.GetFolder('\')
$definition = $scheduler.NewTask(0)
$definition.RegistrationInfo.Description = 'Temporary CodexHp outside-package process launcher'
$definition.Principal.UserId = "$env:USERDOMAIN\$env:USERNAME"
$definition.Principal.LogonType = $taskLogonInteractiveToken
$definition.Principal.RunLevel = 0
$definition.Settings.Enabled = $true
$definition.Settings.AllowDemandStart = $true
$definition.Settings.Hidden = $true
$definition.Settings.DisallowStartIfOnBatteries = $false
$definition.Settings.StopIfGoingOnBatteries = $false
$definition.Settings.ExecutionTimeLimit = "PT$($TimeoutSeconds)S"

$action = $definition.Actions.Create($taskActionExecute)
if ($Detached) {
    if ($ArgumentList.Count -ne 0) {
        throw 'Detached outside-package launches do not support application arguments.'
    }

    $action.Path = Join-Path $env:SystemRoot 'explorer.exe'
    $action.Arguments = ConvertTo-CommandLineArgument $filePathFull
    $action.WorkingDirectory = $workingDirectoryFull
}
else {
    $action.Path = $filePathFull
    $action.Arguments = [string]::Join(' ', @(
        $ArgumentList | ForEach-Object { ConvertTo-CommandLineArgument $_ }
    ))
    $action.WorkingDirectory = $workingDirectoryFull
}

$registeredTask = $null
try {
    $registeredTask = $rootFolder.RegisterTaskDefinition(
        $taskName,
        $definition,
        $taskCreateOrUpdate,
        $null,
        $null,
        $taskLogonInteractiveToken,
        $null)
    $runningTask = $registeredTask.Run($null)
    $deadline = [DateTimeOffset]::Now.AddSeconds($TimeoutSeconds)

    do {
        Start-Sleep -Milliseconds 100
        try {
            $runningTask.Refresh()
            $state = $runningTask.State
        }
        catch {
            $state = 3
        }
    } while ($state -in @($taskStateQueued, $taskStateRunning) -and
        [DateTimeOffset]::Now -lt $deadline)

    if ($state -in @($taskStateQueued, $taskStateRunning)) {
        throw "Outside-package process did not finish within $TimeoutSeconds seconds: $filePathFull"
    }

    $registeredTask = $rootFolder.GetTask($taskName)
    $exitCode = [int64]$registeredTask.LastTaskResult
    if (($Detached -and $exitCode -notin $successfulDetachedExitCodes) -or
        (-not $Detached -and $exitCode -ne 0)) {
        throw "Outside-package process exited with code $exitCode`: $filePathFull"
    }

    [pscustomobject]@{
        FilePath = $filePathFull
        Detached = [bool]$Detached
        ExitCode = $exitCode
    }
}
finally {
    try {
        $rootFolder.DeleteTask($taskName, 0)
    }
    catch {
        Write-Warning "Unable to remove temporary scheduled task '$taskName': $($_.Exception.Message)"
    }
}
