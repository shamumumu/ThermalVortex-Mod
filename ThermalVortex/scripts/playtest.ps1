[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [Alias("ProbePath")]
    [string]$ProbeSource,

    [Parameter(Mandatory = $true)]
    [string]$ProbeId,

    [switch]$IncludeResources,

    [string]$GamePath = "D:\steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe",

    [Alias("Timeout")]
    [ValidateRange(5, 3600)]
    [int]$TimeoutSeconds = 180,

    [string]$DotNetPath,

    [ValidateRange(1, 60)]
    [int]$LifecycleLockTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$modLifecycleModulePath = Join-Path $PSScriptRoot "ModLifecycle.psm1"
Import-Module -Name $modLifecycleModulePath -ErrorAction Stop

function Resolve-PlaytestPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$BasePath
    )

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Test-PathInside {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $separators = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $normalizedRoot = [IO.Path]::GetFullPath($Root).TrimEnd($separators) + [IO.Path]::DirectorySeparatorChar
    $normalizedPath = [IO.Path]::GetFullPath($Path)
    return $normalizedPath.StartsWith($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)
}

function New-PlaytestCleanupState {
    return [pscustomobject]@{
        RestoreError = $null
        RestoreRequired = $false
        FallbackError = $null
        Warnings = [Collections.Generic.List[string]]::new()
    }
}

function Add-PlaytestCleanupWarning {
    param(
        [Parameter(Mandatory = $true)][object]$CleanupState,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $CleanupState.Warnings.Add($Message)
    Write-Warning -WarningAction Continue $Message
}

function Remove-PlaytestTemporaryFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$CleanupState
    )

    try {
        if (Test-Path -LiteralPath $Path) {
            Assert-ModPathNotLinked -Path $Path
            Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
        }
    }
    catch {
        Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Could not remove temporary file '$Path': $($_.Exception.Message)"
    }
}

function Remove-PlaytestTemporaryDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][object]$CleanupState
    )

    try {
        $absolutePath = [IO.Path]::GetFullPath($Path)
        if (-not (Test-PathInside -Path $absolutePath -Root $Root)) {
            throw "Refusing recursive cleanup outside '$Root': $absolutePath"
        }
        Assert-ModPathNotLinked -Path $absolutePath
        if (Test-Path -LiteralPath $absolutePath -PathType Container) {
            $linkedItems = @(Get-ChildItem -LiteralPath $absolutePath -Recurse -Force -ErrorAction Stop |
                Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
            if ($linkedItems.Count -gt 0) {
                throw "Refusing recursive cleanup of a temporary tree containing a linked path: $($linkedItems[0].FullName)"
            }
            Remove-Item -LiteralPath $absolutePath -Recurse -Force -ErrorAction Stop
        }
    }
    catch {
        Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Could not remove temporary directory '$Path': $($_.Exception.Message)"
    }
}

function Write-SessionMarker {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$SessionId,
        [Parameter(Mandatory = $true)][string]$ExactProbeId,
        [Parameter(Mandatory = $true)][string]$ResultPath,
        [Parameter(Mandatory = $true)][object]$CleanupState
    )

    $marker = [ordered]@{
        enabled = $true
        sessionId = $SessionId
        probeId = $ExactProbeId
        resultPath = $ResultPath
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
    }
    $json = $marker | ConvertTo-Json -Depth 4
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $temporaryPath = "$Path.$SessionId.tmp"
    $stream = $null
    try {
        Assert-ModGameStopped
        Assert-ModPathNotLinked -Path $Path
        $stream = [IO.File]::Open($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
        $stream.Dispose()
        $stream = $null

        # Same-directory rename makes the visible marker either complete or absent.
        Assert-ModGameStopped
        [IO.File]::Move($temporaryPath, $Path)
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
        Remove-PlaytestTemporaryFile -Path $temporaryPath -CleanupState $CleanupState
    }
}

function Remove-OwnedSessionMarker {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$SessionId,
        [Parameter(Mandatory = $true)][object]$CleanupState
    )

    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            return
        }
        Assert-ModPathNotLinked -Path $Path
        $marker = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -ne $marker `
            -and $marker.PSObject.Properties.Name -contains "sessionId" `
            -and ([string]$marker.sessionId).Equals($SessionId, [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $Path -Force
        }
    }
    catch {
        Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Could not inspect/remove this session's marker: $($_.Exception.Message)"
    }
}

function Read-TaskProbeResult {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$SessionId,
        [Parameter(Mandatory = $true)][string]$ExactProbeId
    )

    $result = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $result) {
        throw "The task probe result is empty."
    }

    $properties = @($result.PSObject.Properties.Name)
    foreach ($required in @("sessionId", "probeId", "status", "message", "completedUtc")) {
        if ($properties -notcontains $required) {
            throw "The task probe result is missing '$required'."
        }
    }

    if (-not ([string]$result.sessionId).Equals($SessionId, [StringComparison]::Ordinal)) {
        throw "The task probe result belongs to a different session."
    }
    if (-not ([string]$result.probeId).Equals($ExactProbeId, [StringComparison]::Ordinal)) {
        throw "The task probe result belongs to a different probe."
    }

    $status = ([string]$result.status).ToUpperInvariant()
    if ($status -eq "PASS") {
        return [pscustomobject]@{ Kind = "PASS"; Result = $result; Detail = [string]$result.message }
    }
    if ($status -ne "FAIL") {
        throw "The task probe result has unsupported status '$status'."
    }

    $failureKind = if ($properties -contains "failureKind") {
        ([string]$result.failureKind).ToUpperInvariant()
    }
    else {
        ""
    }
    if ($failureKind -eq "ASSERTION") {
        return [pscustomobject]@{ Kind = "ASSERTION"; Result = $result; Detail = [string]$result.message }
    }
    if ($failureKind -eq "INFRASTRUCTURE") {
        return [pscustomobject]@{ Kind = "INFRASTRUCTURE"; Result = $result; Detail = [string]$result.message }
    }

    throw "The failed task probe result has unsupported failureKind '$failureKind'."
}

function Stop-OwnedGameProcess {
    param(
        [AllowNull()][Diagnostics.Process]$Process,
        [AllowNull()][object]$ExpectedStartTimeUtc,
        [Parameter(Mandatory = $true)][object]$CleanupState
    )

    if ($null -eq $Process) {
        return
    }

    $current = $null
    try {
        $current = Get-Process -Id $Process.Id -ErrorAction SilentlyContinue
        if ($null -eq $current) {
            return
        }

        if ($null -eq $ExpectedStartTimeUtc) {
            Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Refusing to close pid=$($Process.Id) because its start time could not be verified."
            return
        }

        # Pin this exact process identity before checking it; Kill uses the same
        # handle, never a second lookup of a possibly reused PID.
        [void]$current.Handle
        $actualStartTimeUtc = $current.StartTime.ToUniversalTime()
        if ($current.ProcessName -cne "SlayTheSpire2" `
            -or $actualStartTimeUtc.Ticks -ne ([datetime]$ExpectedStartTimeUtc).Ticks) {
            Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Refusing to close a different process identity at pid=$($Process.Id)."
            return
        }

        if ($current.HasExited) { return }
        $graceful = $current.CloseMainWindow()
        if ($graceful -and $current.WaitForExit(10000)) {
            return
        }

        if (-not $current.HasExited) {
            if ($current.ProcessName -cne "SlayTheSpire2" `
                -or $current.StartTime.ToUniversalTime() -ne [datetime]$ExpectedStartTimeUtc) {
                Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Refusing to force-close a different process identity at pid=$($Process.Id)."
                return
            }
            Write-Warning "Graceful game close failed; forcing the runner-owned pid=$($Process.Id)."
            $current.Kill()
            if (-not $current.WaitForExit(5000)) {
                Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Runner-owned game pid=$($Process.Id) did not exit after force-close."
            }
        }
    }
    catch {
        # Teardown is deliberately non-authoritative after a probe result exists.
        Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Could not close runner-owned game pid=$($Process.Id): $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $current) {
            try { $current.Dispose() }
            catch {
                Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Could not release the owned-game cleanup handle: $($_.Exception.Message)"
            }
        }
    }
}

function Invoke-TaskProbeAttempt {
    param(
        [Parameter(Mandatory = $true)][int]$Attempt,
        [Parameter(Mandatory = $true)][string]$ResolvedGamePath,
        [Parameter(Mandatory = $true)][string]$MarkerPath,
        [Parameter(Mandatory = $true)][string]$ResultPath,
        [Parameter(Mandatory = $true)][string]$SessionId,
        [Parameter(Mandatory = $true)][string]$ExactProbeId,
        [Parameter(Mandatory = $true)][int]$WaitSeconds,
        [Parameter(Mandatory = $true)][object]$CleanupState
    )

    $gameProcess = $null
    $gameStartTimeUtc = $null
    try {
        if (-not [IO.Path]::GetFileName($ResolvedGamePath).Equals("SlayTheSpire2.exe", [StringComparison]::OrdinalIgnoreCase)) {
            throw "GamePath must name SlayTheSpire2.exe."
        }
        if (Test-Path -LiteralPath $ResultPath) {
            Remove-Item -LiteralPath $ResultPath -Force -ErrorAction Stop
        }
        Remove-OwnedSessionMarker -Path $MarkerPath -SessionId $SessionId -CleanupState $CleanupState
        Assert-ModGameStopped
        Write-SessionMarker -Path $MarkerPath -SessionId $SessionId -ExactProbeId $ExactProbeId -ResultPath $ResultPath -CleanupState $CleanupState

        Write-Host "==> probe attempt $Attempt/2: launch one game"
        Assert-ModGameStopped
        $gameProcess = Start-Process `
            -FilePath $ResolvedGamePath `
            -ArgumentList "--force-steam=off" `
            -WorkingDirectory (Split-Path -Parent $ResolvedGamePath) `
            -WindowStyle Hidden `
            -PassThru
        try {
            [void]$gameProcess.Handle
            $gameStartTimeUtc = $gameProcess.StartTime.ToUniversalTime()
        }
        catch {
            $gameStartTimeUtc = $null
        }

        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($WaitSeconds)
        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            if (Test-Path -LiteralPath $ResultPath -PathType Leaf) {
                try {
                    return Read-TaskProbeResult `
                        -Path $ResultPath `
                        -SessionId $SessionId `
                        -ExactProbeId $ExactProbeId
                }
                catch {
                    return [pscustomobject]@{
                        Kind = "INFRASTRUCTURE"
                        Result = $null
                        Detail = "Invalid result JSON: $($_.Exception.Message)"
                    }
                }
            }

            $gameProcess.Refresh()
            if ($gameProcess.HasExited) {
                Start-Sleep -Milliseconds 250
                if (Test-Path -LiteralPath $ResultPath -PathType Leaf) {
                    try {
                        return Read-TaskProbeResult `
                            -Path $ResultPath `
                            -SessionId $SessionId `
                            -ExactProbeId $ExactProbeId
                    }
                    catch {
                        return [pscustomobject]@{
                            Kind = "INFRASTRUCTURE"
                            Result = $null
                            Detail = "Invalid result JSON after game exit: $($_.Exception.Message)"
                        }
                    }
                }

                return [pscustomobject]@{
                    Kind = "INFRASTRUCTURE"
                    Result = $null
                    Detail = "Game exited with code $($gameProcess.ExitCode) before producing a result."
                }
            }

            Start-Sleep -Milliseconds 200
        }

        return [pscustomobject]@{
            Kind = "INFRASTRUCTURE"
            Result = $null
            Detail = "No result was produced within $WaitSeconds seconds."
        }
    }
    catch {
        return [pscustomobject]@{
            Kind = "INFRASTRUCTURE"
            Result = $null
            Detail = "Could not launch or observe the game: $($_.Exception.Message)"
            RetryAllowed = -not (Test-ModGameRunningError -Exception $_.Exception)
        }
    }
    finally {
        Remove-OwnedSessionMarker -Path $MarkerPath -SessionId $SessionId -CleanupState $CleanupState
        if ($null -ne $gameProcess) {
            Stop-OwnedGameProcess -Process $gameProcess -ExpectedStartTimeUtc $gameStartTimeUtc -CleanupState $CleanupState
            try { $gameProcess.Dispose() }
            catch {
                Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Could not release the launched-game handle: $($_.Exception.Message)"
            }
        }
    }
}

function Invoke-ModInstall {
    param(
        [Parameter(Mandatory = $true)][string]$InstallerPath,
        [Parameter(Mandatory = $true)][string]$GameRoot,
        [Parameter(Mandatory = $true)][string]$ModsRoot,
        [string]$TaskProbeSource,
        [switch]$PackResources,
        [switch]$PrepareOnly,
        [string]$RequestedDotNetPath,
        [ValidateRange(1, 60)]
        [int]$LockTimeoutSeconds = 60
    )

    # Run synchronously in this runspace. The playtest owns the outer lifecycle
    # mutex, and the installer recursively acquires the same named mutex on this
    # thread while it commits the probe or normal build.
    $installParameters = @{
        Sts2Path = $GameRoot
        ModsPath = $ModsRoot
        LifecycleLockTimeoutSeconds = $LockTimeoutSeconds
    }
    if (-not [string]::IsNullOrWhiteSpace($TaskProbeSource)) {
        $installParameters.TaskProbeSource = $TaskProbeSource
    }
    if ($PackResources) {
        $installParameters.IncludeResources = $true
    }
    if ($PrepareOnly) {
        $installParameters.PrepareOnly = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($RequestedDotNetPath)) {
        $installParameters.DotNetPath = $RequestedDotNetPath
    }

    & $InstallerPath @installParameters
}

function Restore-InstalledBackup {
    param(
        [Parameter(Mandatory = $true)][string]$InstalledRoot,
        [Parameter(Mandatory = $true)][string]$BackupRoot,
        [Parameter(Mandatory = $true)][hashtable]$BackupState,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    foreach ($name in $Names) {
        $installedPath = Join-Path $InstalledRoot $name
        if ($BackupState.ContainsKey($name) -and [bool]$BackupState[$name]) {
            Copy-RealModFile -Source (Join-Path $BackupRoot $name) -Destination $installedPath | Out-Null
        }
        else {
            Remove-RealModFile -Path $installedPath
        }
    }
}

function Invoke-PlaytestRestore {
    param(
        [Parameter(Mandatory = $true)][string]$InstallerPath,
        [Parameter(Mandatory = $true)][string]$GameRoot,
        [Parameter(Mandatory = $true)][string]$ModsRoot,
        [Parameter(Mandatory = $true)][string]$InstalledRoot,
        [Parameter(Mandatory = $true)][string]$BackupRoot,
        [Parameter(Mandatory = $true)][hashtable]$BackupState,
        [Parameter(Mandatory = $true)][string[]]$BackupNames,
        [Parameter(Mandatory = $true)][string]$PreparedInstallRoot,
        [Parameter(Mandatory = $true)][object]$CleanupState,
        [string]$RequestedDotNetPath,
        [ValidateRange(1, 60)][int]$LockTimeoutSeconds = 60
    )

    $prepared = $null
    try {
        # This build is required for every attempted probe installation, even
        # when the probe failed to start or a live game blocks the later copy.
        Write-Host "==> prepare current source without probe (one local code build)"
        $prepared = Invoke-ModInstall `
            -InstallerPath $InstallerPath `
            -GameRoot $GameRoot `
            -ModsRoot $ModsRoot `
            -PrepareOnly `
            -RequestedDotNetPath $RequestedDotNetPath `
            -LockTimeoutSeconds $LockTimeoutSeconds
        if ($null -eq $prepared `
            -or $prepared.PSObject.Properties.Name -notcontains "PreparedDirectory" `
            -or [string]::IsNullOrWhiteSpace([string]$prepared.PreparedDirectory)) {
            throw "The no-probe preparation did not return a prepared directory."
        }

        Assert-ModGameStopped
        Write-Host "==> restore prepared no-probe files (no second build)"
        & $InstallerPath `
            -PreparedInstall $prepared.PreparedDirectory `
            -LifecycleLockTimeoutSeconds $LockTimeoutSeconds | Out-Null
    }
    catch {
        if (Test-ModGameRunningError -Exception $_.Exception) {
            $CleanupState.RestoreRequired = $true
            Write-Warning -WarningAction Continue "A game process blocks no-probe restoration. The probe outcome is preserved; close the game before a separate no-probe installation. $($_.Exception.Message)"
            # A running game is never a reason to attempt a backup write.
        }
        else {
            $CleanupState.RestoreError = $_.Exception
            Write-Warning -WarningAction Continue "No-probe restoration failed: $($_.Exception.Message)"
            try {
                Assert-ModGameStopped
                Write-Warning -WarningAction Continue "Attempting the pre-run backup fallback; this does not count as restoring current source."
                Restore-InstalledBackup `
                    -InstalledRoot $InstalledRoot `
                    -BackupRoot $BackupRoot `
                    -BackupState $BackupState `
                    -Names $BackupNames
            }
            catch {
                $CleanupState.FallbackError = $_.Exception
                Write-Warning -WarningAction Continue "Pre-run backup restoration was incomplete: $($_.Exception.Message)"
            }
        }
    }
    finally {
        if ($null -ne $prepared `
            -and $prepared.PSObject.Properties.Name -contains "PreparedDirectory" `
            -and -not [string]::IsNullOrWhiteSpace([string]$prepared.PreparedDirectory)) {
            try {
                $preparedPath = [IO.Path]::GetFullPath([string]$prepared.PreparedDirectory)
                $expectedParent = [IO.Path]::GetFullPath($PreparedInstallRoot).TrimEnd([char[]]@('\', '/'))
                if (-not (Split-Path -Parent $preparedPath).Equals($expectedParent, [StringComparison]::OrdinalIgnoreCase) `
                    -or (Split-Path -Leaf $preparedPath) -cnotmatch '^ThermalVortex-install-[0-9a-f]{32}$') {
                    throw "Refusing cleanup of an unexpected prepared directory: $preparedPath"
                }
                Remove-PlaytestTemporaryDirectory -Path $preparedPath -Root $PreparedInstallRoot -CleanupState $CleanupState
            }
            catch {
                Add-PlaytestCleanupWarning -CleanupState $CleanupState -Message "Could not remove no-probe prepared files: $($_.Exception.Message)"
            }
        }
    }
}

function Get-PlaytestCleanupStatus {
    param([Parameter(Mandatory = $true)][object]$CleanupState)

    $statuses = @()
    if ($null -ne $CleanupState.RestoreError) { $statuses += "ERROR_RESTORE_FAILED" }
    elseif ($CleanupState.RestoreRequired) { $statuses += "WARNING_RESTORE_REQUIRED" }
    if ($CleanupState.Warnings.Count -gt 0) { $statuses += "WARNING_CLEANUP_INCOMPLETE" }
    if ($statuses.Count -eq 0) { return "COMPLETE" }
    return $statuses -join ", "
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot ".."))
$invocationRoot = (Get-Location).ProviderPath
$installerPath = Join-Path $PSScriptRoot "install-mod.ps1"
$resolvedProbeSource = Resolve-PlaytestPath -Path $ProbeSource -BasePath $invocationRoot
$resolvedGamePath = Resolve-PlaytestPath -Path $GamePath -BasePath $invocationRoot
$gameRoot = Split-Path -Parent $resolvedGamePath
$modsRoot = Join-Path $gameRoot "mods"
$installedModRoot = Join-Path $modsRoot "ThermalVortex"
$markerPath = Join-Path $installedModRoot ".task-probe-session.json"
$taskProbeRoot = Join-Path $workspaceRoot ".codex-temp\task-probes"
$preparedInstallRoot = Join-Path $workspaceRoot ".codex-temp\prepared-installs"
$sessionId = [Guid]::NewGuid().ToString("N")
$sessionRoot = Join-Path $taskProbeRoot $sessionId
$resultPath = Join-Path $sessionRoot "result.json"
$stagedProbePath = Join-Path $sessionRoot "probe.cs"
$backupRoot = Join-Path $sessionRoot "installed-backup"

$testInstallAttempted = $false
$probeSourceMayBeRemoved = $false
$runError = $null
$cleanupState = New-PlaytestCleanupState
$finalOutcome = $null
$backupState = @{}
$lifecycleLease = $null
$backupNames = @("ThermalVortex.dll", "ThermalVortex.pdb", "ThermalVortex.json")
if ($IncludeResources) {
    $backupNames += @("ThermalVortex.pck", "ThermalVortex.pck.resources.manifest")
}

# Restoration and temporary cleanup cannot suppress the recorded probe result.
# Keep the outer release independent of both, including early validation errors.
try {
    try {
        if (-not (Test-PathInside -Path $resolvedProbeSource -Root $taskProbeRoot)) {
            throw "ProbeSource must be a one-off file under '$taskProbeRoot'."
        }
        if (-not $resolvedProbeSource.EndsWith(".cs", [StringComparison]::OrdinalIgnoreCase)) {
            throw "Probe source must be one C# source file: $resolvedProbeSource"
        }
        Assert-ModPathNotLinked -Path $resolvedProbeSource
        $probeSourceMayBeRemoved = $true
        if ([string]::IsNullOrWhiteSpace($ProbeId)) {
            throw "-ProbeId must not be empty."
        }
        Assert-ModGameStopped
        if (-not (Test-Path -LiteralPath $resolvedProbeSource -PathType Leaf)) {
            throw "Probe source was not found: $resolvedProbeSource"
        }
        if (-not [IO.Path]::GetFileName($resolvedGamePath).Equals("SlayTheSpire2.exe", [StringComparison]::OrdinalIgnoreCase)) {
            throw "GamePath must name SlayTheSpire2.exe."
        }
        if (-not (Test-Path -LiteralPath $resolvedGamePath -PathType Leaf)) {
            throw "Game executable was not found: $resolvedGamePath"
        }
        if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
            throw "Mod installer was not found: $installerPath"
        }
        Assert-ModPathNotLinked -Path $sessionRoot
        New-Item -ItemType Directory -Force -Path $sessionRoot | Out-Null
        New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
        Copy-Item -LiteralPath $resolvedProbeSource -Destination $stagedProbePath -Force

        Write-Host "==> Acquire real-Mod lifecycle lock for the complete playtest"
        $lifecycleLease = Enter-RealModLifecycle `
            -InstalledModRoot $installedModRoot `
            -TimeoutSeconds $LifecycleLockTimeoutSeconds

        Assert-ModGameStopped
        Assert-ModPathNotLinked -Path $installedModRoot
        if (Test-Path -LiteralPath $markerPath) {
            throw "A task probe marker already exists; refusing to overwrite it: $markerPath"
        }
        foreach ($name in $backupNames) {
            $installedPath = Join-Path $installedModRoot $name
            $backupPath = Join-Path $backupRoot $name
            Assert-ModPathNotLinked -Path $installedPath
            $exists = Test-Path -LiteralPath $installedPath -PathType Leaf
            $backupState[$name] = $exists
            if ($exists) {
                Copy-Item -LiteralPath $installedPath -Destination $backupPath -Force
            }
        }

        Write-Host "ProbeScope: exact id='$ProbeId'; one probe only; no suite expansion"
        Write-Host "==> build and install temporary probe"
        $testInstallAttempted = $true
        Invoke-ModInstall `
            -InstallerPath $installerPath `
            -GameRoot $gameRoot `
            -ModsRoot $modsRoot `
            -TaskProbeSource $stagedProbePath `
            -PackResources:$IncludeResources `
            -RequestedDotNetPath $DotNetPath `
            -LockTimeoutSeconds $LifecycleLockTimeoutSeconds | Out-Null

        for ($attempt = 1; $attempt -le 2; $attempt++) {
            $finalOutcome = Invoke-TaskProbeAttempt `
                -Attempt $attempt `
                -ResolvedGamePath $resolvedGamePath `
                -MarkerPath $markerPath `
                -ResultPath $resultPath `
                -SessionId $sessionId `
                -ExactProbeId $ProbeId `
                -WaitSeconds $TimeoutSeconds `
                -CleanupState $cleanupState

            if ($finalOutcome.Kind -ne "INFRASTRUCTURE" -or $attempt -eq 2) {
                break
            }
            if (($finalOutcome.PSObject.Properties.Name -contains "RetryAllowed" `
                    -and -not $finalOutcome.RetryAllowed) `
                -or @(Get-ModGameProcess).Count -gt 0) {
                Write-Warning "A game process blocked this attempt or remains after teardown; no retry will be launched."
                break
            }
            Write-Warning "Infrastructure failure; retrying once with the same build: $($finalOutcome.Detail)"
        }
    }
    catch {
        $runError = $_.Exception
        if ($null -eq $finalOutcome) {
            $finalOutcome = [pscustomobject]@{
                Kind = "INFRASTRUCTURE"
                Result = $null
                Detail = $runError.Message
            }
        }
    }
    finally {
        Remove-OwnedSessionMarker -Path $markerPath -SessionId $sessionId -CleanupState $cleanupState
        Remove-PlaytestTemporaryFile -Path $resultPath -CleanupState $cleanupState
        Remove-PlaytestTemporaryFile -Path $stagedProbePath -CleanupState $cleanupState
        if ($probeSourceMayBeRemoved) {
            Remove-PlaytestTemporaryFile -Path $resolvedProbeSource -CleanupState $cleanupState
        }

        if ($testInstallAttempted) {
            try {
                Invoke-PlaytestRestore `
                    -InstallerPath $installerPath `
                    -GameRoot $gameRoot `
                    -ModsRoot $modsRoot `
                    -InstalledRoot $installedModRoot `
                    -BackupRoot $backupRoot `
                    -BackupState $backupState `
                    -BackupNames $backupNames `
                    -PreparedInstallRoot $preparedInstallRoot `
                    -RequestedDotNetPath $DotNetPath `
                    -LockTimeoutSeconds $LifecycleLockTimeoutSeconds `
                    -CleanupState $cleanupState
            }
            catch {
                if ($null -eq $cleanupState.RestoreError) {
                    $cleanupState.RestoreError = $_.Exception
                }
                Write-Warning -WarningAction Continue "Unexpected no-probe cleanup failure: $($_.Exception.Message)"
            }
        }
        Remove-PlaytestTemporaryDirectory -Path $sessionRoot -Root $taskProbeRoot -CleanupState $cleanupState
    }
}
finally {
    if ($null -ne $lifecycleLease) {
        try {
            Exit-RealModLifecycle -Lease $lifecycleLease
        }
        catch {
            Add-PlaytestCleanupWarning -CleanupState $cleanupState -Message "Could not release the real-Mod lifecycle lock: $($_.Exception.Message)"
        }
        $lifecycleLease = $null
    }
}

if ($null -eq $finalOutcome) {
    $finalOutcome = [pscustomobject]@{
        Kind = "INFRASTRUCTURE"
        Result = $null
        Detail = "The task probe produced no outcome."
    }
}
if ($finalOutcome.Kind -eq "PASS") {
    Write-Host "ProbeResult: PASS - $($finalOutcome.Detail)"
}
elseif ($finalOutcome.Kind -eq "ASSERTION") {
    Write-Host "ProbeResult: FAIL ASSERTION - $($finalOutcome.Detail)"
    if ($null -eq $runError) {
        $runError = [InvalidOperationException]::new("Task probe assertion failed: $($finalOutcome.Detail)")
    }
}
else {
    Write-Host "ProbeResult: FAIL INFRASTRUCTURE - $($finalOutcome.Detail)"
    if ($null -eq $runError) {
        $runError = [InvalidOperationException]::new("Task probe infrastructure failed: $($finalOutcome.Detail)")
    }
}
$cleanupStatus = Get-PlaytestCleanupStatus -CleanupState $cleanupState
Write-Host "CleanupStatus: $cleanupStatus"

if ($null -ne $cleanupState.RestoreError) {
    $errors = @()
    if ($null -ne $runError) { $errors += $runError.Message }
    $errors += "Current-source no-probe restoration failed: $($cleanupState.RestoreError.Message)"
    if ($null -ne $cleanupState.FallbackError) {
        $errors += "Backup fallback also failed: $($cleanupState.FallbackError.Message)"
    }
    throw ($errors -join "`n")
}
if ($null -ne $runError) { throw $runError }

if ($cleanupStatus -ne "COMPLETE") {
    Write-Host "Playtest: PASS (functional result preserved; see CleanupStatus)"
}
else {
    Write-Host "Playtest: PASS"
}
