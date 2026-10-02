<#
.SYNOPSIS
Runs one standalone Godot tool process with bounded waiting and durable logs.
.DESCRIPTION
Use the actual Godot executable, not its _console launcher. Each invocation gets
its own log directory. Success requires exit code zero and complete log capture.
This script never changes the calling process's environment or targets an
already-running Godot/game process. AppDataDirectory affects only its child and
defaults to this run's writable appdata directory. This avoids Godot's user-data
and logger initialization failure when a restricted child cannot write to the
inherited Windows roaming profile.
On failure, result.json is written before throwing. AllowFailure returns that
same result object so a caller can inspect an expected failure explicitly.
#>
[CmdletBinding()]
param(
    [string[]]$Arguments = @(),
    [string]$GodotPath,
    [string]$WorkingDirectory,
    [string]$LogRoot,
    [string]$RunName = 'godot-tool',
    [ValidateRange(1, 3600)][int]$TimeoutSeconds = 120,
    [string]$AppDataDirectory,
    [switch]$AllowFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-WindowsArgument {
    param([AllowEmptyString()][string]$Value)

    # Windows argv quoting: double backslashes before a quote or the closing
    # delimiter; ordinary backslashes are preserved. Quote even empty arguments.
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append([char]34)
    $slashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq [char]92) {
            $slashes++
            continue
        }
        if ($character -eq [char]34) {
            [void]$builder.Append(('\' * (2 * $slashes + 1)))
        }
        else {
            [void]$builder.Append(('\' * $slashes))
        }
        [void]$builder.Append($character)
        $slashes = 0
    }
    [void]$builder.Append(('\' * (2 * $slashes)))
    [void]$builder.Append([char]34)
    return $builder.ToString()
}

function Stop-OwnedGodotProcess {
    param([int]$ProcessId, [datetime]$StartTimeUtc)

    $outcome = [ordered]@{
        attempted = $false
        identityMatched = $false
        exited = $false
        message = $null
    }
    $current = $null
    try {
        try {
            $current = [Diagnostics.Process]::GetProcessById($ProcessId)
        }
        catch [ArgumentException] {
            $outcome.exited = $true
            $outcome.message = 'The started process has already exited.'
            return [pscustomobject]$outcome
        }
        # Keep this process handle open across the identity check and Kill;
        # termination never resolves the PID a second time.
        [void]$current.Handle
        if ($current.StartTime.ToUniversalTime().Ticks -ne $StartTimeUtc.Ticks) {
            $outcome.message = 'PID was reused; termination refused.'
            return [pscustomobject]$outcome
        }
        $outcome.identityMatched = $true
        if ($current.HasExited) {
            $outcome.exited = $true
            return [pscustomobject]$outcome
        }
        $outcome.attempted = $true
        $current.Kill()
        $outcome.exited = $current.WaitForExit(5000)
        if (-not $outcome.exited) {
            $outcome.message = 'The owned process did not exit within five seconds after termination.'
        }
    }
    catch {
        $outcome.message = $_.Exception.Message
    }
    finally {
        if ($null -ne $current) { $current.Dispose() }
    }
    return [pscustomobject]$outcome
}

function Write-RunResult {
    param($Value, [string]$Path)
    $json = $Value | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}

$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if ([string]::IsNullOrWhiteSpace($GodotPath)) {
    $GodotPath = Join-Path $workspace '.tools/godot-rig/Godot_v4.5.1-stable_win64.exe'
}
if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) { $WorkingDirectory = $workspace }
if ([string]::IsNullOrWhiteSpace($LogRoot)) { $LogRoot = Join-Path $workspace '.codex-temp/godot-tool-runs' }
$LogRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogRoot)
$safeName = [regex]::Replace($RunName, '[^\p{L}\p{Nd}_-]', '-')
if ([string]::IsNullOrWhiteSpace($safeName)) { $safeName = 'godot-tool' }
if ($safeName.Length -gt 60) { $safeName = $safeName.Substring(0, 60) }
$runId = [Guid]::NewGuid().ToString('N')
$runDirectory = Join-Path $LogRoot ((Get-Date -Format 'yyyyMMdd-HHmmssfff') + '-' + $safeName + '-' + $runId)
[void][IO.Directory]::CreateDirectory($runDirectory)
$stdoutPath = Join-Path $runDirectory 'stdout.log'
$stderrPath = Join-Path $runDirectory 'stderr.log'
$resultPath = Join-Path $runDirectory 'result.json'
if ([string]::IsNullOrWhiteSpace($AppDataDirectory)) {
    $AppDataDirectory = Join-Path $runDirectory 'appdata'
}
$result = [ordered]@{
    schemaVersion = 1
    runId = $runId
    runName = $RunName
    status = 'starting'
    succeeded = $false
    startedUtc = [DateTime]::UtcNow.ToString('o')
    completedUtc = $null
    durationMs = $null
    processId = $null
    processStartTimeUtc = $null
    processExited = $false
    exitCode = $null
    exitCodeHex = $null
    timedOut = $false
    timeoutSeconds = $TimeoutSeconds
    termination = $null
    executable = $GodotPath
    arguments = @($Arguments)
    commandLine = $null
    workingDirectory = $WorkingDirectory
    appDataDirectory = $AppDataDirectory
    runDirectory = $runDirectory
    stdoutPath = $stdoutPath
    stderrPath = $stderrPath
    resultPath = $resultPath
    stdoutComplete = $false
    stderrComplete = $false
    cleanupErrors = @()
    errorMessage = $null
}
Write-RunResult -Value $result -Path $resultPath
$watch = [Diagnostics.Stopwatch]::StartNew()
$process = $null
$stdoutStream = $null
$stderrStream = $null
$stdoutCopy = $null
$stderrCopy = $null
$copyCancellation = New-Object Threading.CancellationTokenSource
$processStarted = $false
$identity = $null

try {
    $stdoutStream = [IO.File]::Open($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $stderrStream = [IO.File]::Open($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $resolvedExe = (Resolve-Path -LiteralPath $GodotPath -ErrorAction Stop).ProviderPath
    if (-not (Test-Path -LiteralPath $resolvedExe -PathType Leaf) -or [IO.Path]::GetExtension($resolvedExe) -ine '.exe') {
        throw 'GodotPath must be an existing executable file.'
    }
    if ([IO.Path]::GetFileName($resolvedExe) -match '_console\.exe$') {
        throw 'Use the actual Godot .exe, not the _console launcher, so PID and exit code belong to the renderer.'
    }
    $resolvedWorkingDirectory = (Resolve-Path -LiteralPath $WorkingDirectory -ErrorAction Stop).ProviderPath
    if (-not (Test-Path -LiteralPath $resolvedWorkingDirectory -PathType Container)) {
        throw 'WorkingDirectory must be an existing directory.'
    }
    $quotedArguments = @($Arguments | ForEach-Object { ConvertTo-WindowsArgument -Value $_ })
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $resolvedExe
    $startInfo.Arguments = $quotedArguments -join ' '
    $startInfo.WorkingDirectory = $resolvedWorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if (-not [string]::IsNullOrWhiteSpace($AppDataDirectory)) {
        $resolvedAppData = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($AppDataDirectory)
        [void][IO.Directory]::CreateDirectory($resolvedAppData)
        $startInfo.EnvironmentVariables['APPDATA'] = $resolvedAppData
        $startInfo.EnvironmentVariables['LOCALAPPDATA'] = $resolvedAppData
        $result.appDataDirectory = $resolvedAppData
    }
    $result.executable = $resolvedExe
    $result.workingDirectory = $resolvedWorkingDirectory
    $result.commandLine = (ConvertTo-WindowsArgument -Value $resolvedExe) + ' ' + $startInfo.Arguments
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw 'Godot process could not be started.' }
    $processStarted = $true
    [void]$process.Handle
    $result.processId = $process.Id
    $identity = $process.StartTime.ToUniversalTime()
    $result.processStartTimeUtc = $identity.ToString('o')
    # Raw stream copies avoid PowerShell event callbacks and drain both pipes
    # concurrently, including while WaitForExit waits for a busy renderer.
    $stdoutCopy = $process.StandardOutput.BaseStream.CopyToAsync($stdoutStream, 65536, $copyCancellation.Token)
    $stderrCopy = $process.StandardError.BaseStream.CopyToAsync($stderrStream, 65536, $copyCancellation.Token)
    $result.status = 'running'
    Write-RunResult -Value $result -Path $resultPath
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $result.timedOut = $true
        $result.status = 'timed_out'
        $result.errorMessage = "Godot exceeded the $TimeoutSeconds second time limit."
        $result.termination = Stop-OwnedGodotProcess -ProcessId $process.Id -StartTimeUtc $identity
    }
    $process.Refresh()
    $result.processExited = $process.HasExited
    if ($result.processExited) {
        $result.exitCode = [int]$process.ExitCode
        $unsignedExitCode = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$process.ExitCode), 0)
        $result.exitCodeHex = '0x' + $unsignedExitCode.ToString('X8')
    }
    $copies = [Threading.Tasks.Task[]]@($stdoutCopy, $stderrCopy)
    $captureFinished = [Threading.Tasks.Task]::WaitAll($copies, 5000)
    $result.stdoutComplete = $stdoutCopy.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion
    $result.stderrComplete = $stderrCopy.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion
    if (-not $captureFinished) { throw 'Log capture did not finish within five seconds after waiting for the process.' }
    if (-not $result.timedOut) {
        if ($result.processExited -and $result.exitCode -eq 0 -and $result.stdoutComplete -and $result.stderrComplete) {
            $result.status = 'succeeded'
            $result.succeeded = $true
        }
        else {
            $result.status = 'failed'
            $result.errorMessage = "Godot exited with code $($result.exitCode) ($($result.exitCodeHex))."
        }
    }
}
catch {
    if ($null -eq $result.errorMessage) { $result.errorMessage = $_.Exception.Message }
    else { $result.errorMessage += ' ' + $_.Exception.Message }
    if (-not $result.timedOut) { $result.status = 'failed' }
    $result.succeeded = $false
    # An infrastructure error after launch must not orphan our child. No
    # termination is attempted without its recorded identity, or twice.
    if ($processStarted -and $null -ne $identity -and $null -eq $result.termination) {
        $result.termination = Stop-OwnedGodotProcess -ProcessId $process.Id -StartTimeUtc $identity
    }
    elseif ($processStarted -and $null -eq $identity) {
        $result.errorMessage += ' Process identity could not be recorded; no termination was attempted.'
    }
}
finally {
    $copyCancellation.Cancel()
    if ($processStarted) {
        try {
            $process.Refresh()
            $result.processExited = $process.HasExited
            if ($result.processExited) {
                $result.exitCode = [int]$process.ExitCode
                $result.exitCodeHex = '0x' + ([BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$process.ExitCode), 0)).ToString('X8')
            }
        }
        catch { }
        foreach ($streamProperty in @('StandardOutput', 'StandardError')) {
            try { ($process.$streamProperty).Dispose() }
            catch { $result.cleanupErrors += $_.Exception.Message }
        }
    }
    foreach ($resource in @($stdoutStream, $stderrStream, $process, $copyCancellation)) {
        if ($null -ne $resource) {
            try { $resource.Dispose() }
            catch { $result.cleanupErrors += $_.Exception.Message }
        }
    }
    if ($result.cleanupErrors.Count -gt 0) {
        $result.succeeded = $false
        if (-not $result.timedOut) { $result.status = 'failed' }
        $result.errorMessage += ' Cleanup error: ' + ($result.cleanupErrors -join ' ')
    }
    $watch.Stop()
    $result.durationMs = $watch.ElapsedMilliseconds
    $result.completedUtc = [DateTime]::UtcNow.ToString('o')
    Write-RunResult -Value $result -Path $resultPath
}

if (-not $result.succeeded -and -not $AllowFailure) {
    throw "$($result.errorMessage) Result: $resultPath"
}
[pscustomobject]$result
