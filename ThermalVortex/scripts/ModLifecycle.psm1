#Requires -Version 5.1

Set-StrictMode -Version Latest

$script:LeaseTypeName = "ThermalVortex.RealModLifecycleLease"
$script:MutexNamePrefix = "Local\ThermalVortex.RealMod.v1."

function Get-RealModIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$InstalledModRoot
    )

    $fullPath = [IO.Path]::GetFullPath($InstalledModRoot)
    $normalizedPath = $fullPath.Replace(
        [IO.Path]::AltDirectorySeparatorChar,
        [IO.Path]::DirectorySeparatorChar)
    $pathRoot = [IO.Path]::GetPathRoot($normalizedPath)

    while ($normalizedPath.Length -gt $pathRoot.Length `
        -and $normalizedPath.EndsWith(
            [IO.Path]::DirectorySeparatorChar.ToString(),
            [StringComparison]::Ordinal)) {
        $normalizedPath = $normalizedPath.Substring(0, $normalizedPath.Length - 1)
    }

    # Windows filesystem paths are case-insensitive, while kernel object names
    # are case-sensitive. Normalize case before hashing so equivalent spellings
    # select the same mutex.
    $normalizedPath = $normalizedPath.ToUpperInvariant()
    $pathBytes = [Text.Encoding]::UTF8.GetBytes($normalizedPath)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($pathBytes)
    }
    finally {
        $sha256.Dispose()
    }

    $pathHash = [BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant()
    return [pscustomobject]@{
        InstalledModRoot = $normalizedPath
        MutexName = $script:MutexNamePrefix + $pathHash
    }
}

function Enter-ModDirectoryLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [string]$InstalledModRoot,

        [ValidateRange(0, 60)]
        [int]$TimeoutSeconds = 60,

        [ValidateSet('RealMod', 'Build')]
        [string]$Kind = 'RealMod'
    )

    $identity = Get-RealModIdentity -InstalledModRoot $InstalledModRoot
    if ($Kind -eq 'Build') {
        $identity.MutexName = $identity.MutexName.Replace('ThermalVortex.RealMod.v1.', 'ThermalVortex.Build.v1.')
    }
    $mutex = $null
    $acquired = $false
    $wasAbandoned = $false

    try {
        $mutex = [Threading.Mutex]::new($false, $identity.MutexName)
        try {
            $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds))
        }
        catch [Threading.AbandonedMutexException] {
            # WaitOne transfers ownership before reporting abandonment.
            $acquired = $true
            $wasAbandoned = $true
            Write-Warning -WarningAction Continue `
                    "The $Kind mutex was abandoned. Ownership was acquired; inspect errors from the interrupted operation: $($identity.InstalledModRoot)"
        }

        if (-not $acquired) {
            throw [TimeoutException]::new(
                "Timed out after $TimeoutSeconds seconds waiting for $Kind mutex '$($identity.MutexName)' for '$($identity.InstalledModRoot)'.")
        }

        $lease = [pscustomobject]@{
            InstalledModRoot = $identity.InstalledModRoot
            Kind = $Kind
            MutexName = $identity.MutexName
            Mutex = $mutex
            OwnerThreadId = [Threading.Thread]::CurrentThread.ManagedThreadId
            WasAbandoned = $wasAbandoned
            IsHeld = $true
        }
        $lease.PSObject.TypeNames.Insert(0, $script:LeaseTypeName)
        return $lease
    }
    catch {
        $entryFailure = $_
        if ($acquired -and $null -ne $mutex) {
            try {
                $mutex.ReleaseMutex()
            }
            catch {
                Write-Warning -WarningAction Continue `
                    "Failed to release the real-Mod lifecycle mutex while cleaning up an Enter failure: $($_.Exception.Message)"
            }
        }
        if ($null -ne $mutex) {
            try {
                $mutex.Dispose()
            }
            catch {
                Write-Warning -WarningAction Continue `
                    "Failed to dispose the real-Mod lifecycle mutex while cleaning up an Enter failure: $($_.Exception.Message)"
            }
        }
        throw $entryFailure
    }
}

function Enter-RealModLifecycle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$InstalledModRoot,
        [ValidateRange(0, 60)][int]$TimeoutSeconds = 60
    )
    Enter-ModDirectoryLease -InstalledModRoot $InstalledModRoot -TimeoutSeconds $TimeoutSeconds -Kind RealMod
}

function Enter-ModBuild {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$IntermediateRoot,
        [ValidateRange(0, 60)][int]$TimeoutSeconds = 60
    )
    # Lock the actual shared intermediate root, including project.assets.json.
    # Configurations sharing that root also share restore state.
    Enter-ModDirectoryLease -InstalledModRoot $IntermediateRoot -TimeoutSeconds $TimeoutSeconds -Kind Build
}

function Exit-RealModLifecycle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0, ValueFromPipeline = $true)]
        [ValidateNotNull()]
        [object]$Lease
    )

    process {
        if ($Lease.PSObject.TypeNames -notcontains $script:LeaseTypeName) {
            throw [ArgumentException]::new(
                "Lease was not created by Enter-RealModLifecycle.",
                "Lease")
        }

        if (-not [bool]$Lease.IsHeld) {
            return
        }

        $currentThreadId = [Threading.Thread]::CurrentThread.ManagedThreadId
        if ([int]$Lease.OwnerThreadId -ne $currentThreadId) {
            throw [InvalidOperationException]::new(
                "The real-Mod lifecycle mutex must be released by its acquiring thread. Owner thread: $($Lease.OwnerThreadId); current thread: $currentThreadId.")
        }
        if ($null -eq $Lease.Mutex) {
            throw [InvalidOperationException]::new(
                "The lifecycle lease is marked as held but has no mutex handle.")
        }

        $Lease.Mutex.ReleaseMutex()
        $Lease.IsHeld = $false
        try {
            $Lease.Mutex.Dispose()
        }
        finally {
            $Lease.Mutex = $null
        }
    }
}

function Exit-ModBuild {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][object]$Lease)
    if ($Lease.PSObject.Properties.Name -notcontains 'Kind' -or $Lease.Kind -ne 'Build') {
        throw 'Lease was not created by Enter-ModBuild.'
    }
    Exit-RealModLifecycle -Lease $Lease
}

function Get-ModGameProcess {
    @(Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -ceq 'SlayTheSpire2' })
}

function Assert-ModGameStopped {
    $games = @(Get-ModGameProcess)
    if ($games.Count -gt 0) {
        $ids = ($games | ForEach-Object { $_.Id }) -join ', '
        $failure = [InvalidOperationException]::new("SlayTheSpire2 is running (PID: $ids). No further Mod write or game launch is allowed.")
        $failure.Data['ModGameRunning'] = $true
        throw $failure
    }
}

function Test-ModGameRunningError {
    param([Parameter(Mandatory = $true)][Exception]$Exception)
    $currentException = $Exception
    while ($null -ne $currentException) {
        if ($currentException.Data.Contains('ModGameRunning') -and [bool]$currentException.Data['ModGameRunning']) {
            return $true
        }
        $currentException = $currentException.InnerException
    }
    return $false
}

function Assert-ModPathNotLinked {
    param([Parameter(Mandatory = $true)][string]$Path)
    $currentPath = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($currentPath)) {
        if (Test-Path -LiteralPath $currentPath) {
            $item = Get-Item -LiteralPath $currentPath -Force -ErrorAction Stop
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a linked Mod file or ancestor: $currentPath"
            }
        }
        $parentPath = [IO.Path]::GetDirectoryName($currentPath)
        if ($parentPath -eq $currentPath) { break }
        $currentPath = $parentPath
    }
}

function Copy-RealModFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [string]$ExpectedSha256
    )
    Assert-ModPathNotLinked -Path $Source
    Assert-ModPathNotLinked -Path $Destination
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Install source is missing: $Source" }
    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256) -and $sourceHash -cne $ExpectedSha256.ToUpperInvariant()) {
        throw "Install source no longer matches the prepared SHA-256: $Source"
    }
    $temporary = "$Destination.installing-$([Guid]::NewGuid().ToString('N'))"
    $backup = "$Destination.replaced-$([Guid]::NewGuid().ToString('N'))"
    $copyFailure = $null
    try {
        Assert-ModGameStopped
        Copy-Item -LiteralPath $Source -Destination $temporary -Force -ErrorAction Stop
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -cne $sourceHash) {
            throw "Copied bytes do not match the source: $Destination"
        }
        Assert-ModPathNotLinked -Path $Destination
        Assert-ModGameStopped
        if (Test-Path -LiteralPath $Destination -PathType Leaf) {
            [IO.File]::Replace($temporary, $Destination, $backup)
        }
        else { [IO.File]::Move($temporary, $Destination) }
        $destinationHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
        if ($destinationHash -cne $sourceHash) { throw "Installed bytes do not match the source: $Destination" }
        [pscustomobject]@{ File = [IO.Path]::GetFileName($Destination); Sha256 = $destinationHash }
    }
    catch {
        $copyFailure = $_.Exception
        throw
    }
    finally {
        foreach ($workingFile in @($temporary, $backup)) {
            if (Test-Path -LiteralPath $workingFile) {
                try { Remove-RealModFile -Path $workingFile }
                catch {
                    if ($null -eq $copyFailure) { throw }
                    $copyFailure.Data['ModCleanupError'] = $_.Exception.Message
                    $gameObserved = Test-ModGameRunningError -Exception $_.Exception
                    if ($gameObserved) { $copyFailure.Data['ModGameRunning'] = $true }
                    Write-Warning -WarningAction Continue "Could not remove installer working file '$workingFile': $($_.Exception.Message)"
                    if ($gameObserved) { break }
                }
            }
        }
    }
}

function Remove-RealModFile {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    Assert-ModPathNotLinked -Path $Path
    Assert-ModGameStopped
    if (Test-Path -LiteralPath $Path) {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Refusing non-file Mod removal: $Path" }
        Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
    }
}

Export-ModuleMember -Function @(
    "Enter-RealModLifecycle",
    "Exit-RealModLifecycle",
    "Enter-ModBuild",
    "Exit-ModBuild",
    "Get-ModGameProcess",
    "Assert-ModGameStopped",
    "Test-ModGameRunningError",
    "Assert-ModPathNotLinked",
    "Copy-RealModFile",
    "Remove-RealModFile"
)
