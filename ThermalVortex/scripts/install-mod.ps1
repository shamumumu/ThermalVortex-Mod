[CmdletBinding(DefaultParameterSetName = 'Prepare')]
param(
    [Parameter(ParameterSetName = 'Prepare')][switch]$PrepareOnly,
    [Parameter(Mandatory = $true, ParameterSetName = 'Commit')]
    [ValidateNotNullOrEmpty()][string]$PreparedInstall,
    [Parameter(ParameterSetName = 'Prepare')][switch]$IncludeResources,
    [Parameter(ParameterSetName = 'Prepare')][switch]$Restore,
    [Parameter(ParameterSetName = 'Prepare')][string]$TaskProbeSource,
    [Parameter(ParameterSetName = 'Prepare')][ValidateNotNullOrEmpty()][string]$Configuration = 'Debug',
    [Parameter(ParameterSetName = 'Prepare')][string]$DotNetPath,
    [Parameter(ParameterSetName = 'Prepare')][string]$ModsPath,
    [Parameter(ParameterSetName = 'Prepare')][string]$Sts2Path,
    [ValidateRange(1, 60)][int]$LifecycleLockTimeoutSeconds = 60,
    [ValidateRange(1, 60)][int]$BuildLockTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ModLifecycle.psm1') -ErrorAction Stop

$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$projectPath = Join-Path $projectRoot 'ThermalVortex.csproj'
$preparationRoot = Join-Path $workspaceRoot '.codex-temp/prepared-installs'

function Get-InstallAbsolutePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $fullPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    if ($fullPath.Length -gt [IO.Path]::GetPathRoot($fullPath).Length) { $fullPath = $fullPath.TrimEnd('\', '/') }
    return $fullPath
}

function Assert-PreparedDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = Get-InstallAbsolutePath $Path
    $expectedParent = Get-InstallAbsolutePath $preparationRoot
    if (-not (Split-Path -Parent $fullPath).Equals($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $fullPath) -cnotmatch '^ThermalVortex-install-[0-9a-f]{32}$') {
        throw "Prepared installation must be a generated directory directly inside '$expectedParent': $fullPath"
    }
    Assert-ModPathNotLinked -Path $fullPath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) { throw "Prepared installation directory is missing: $fullPath" }
    # Do not follow links in directories that will later be removed recursively.
    $pending = New-Object 'System.Collections.Generic.Queue[string]'
    $pending.Enqueue($fullPath)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Dequeue() -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Prepared installation contains a link: $($item.FullName)" }
            if ($item.PSIsContainer) { $pending.Enqueue($item.FullName) }
        }
    }
    return $fullPath
}

function Remove-PreparedDirectory {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        $safePath = Assert-PreparedDirectory -Path $Path
        Remove-Item -LiteralPath $safePath -Recurse -Force -ErrorAction Stop
    }
}

function Assert-InstallTarget {
    param([string]$TargetModsPath, [string]$TargetModRoot)
    if (-not [IO.Path]::IsPathRooted($TargetModsPath) -or -not [IO.Path]::IsPathRooted($TargetModRoot)) { throw 'Prepared target paths must be absolute.' }
    $expectedTarget = Join-Path (Get-InstallAbsolutePath $TargetModsPath) 'ThermalVortex'
    if (-not $TargetModRoot.Equals($expectedTarget, [StringComparison]::OrdinalIgnoreCase)) { throw 'Prepared ModsPath and InstalledModRoot do not match.' }
    $resolvedTarget = Get-InstallAbsolutePath $TargetModRoot
    if (-not $resolvedTarget.Equals($TargetModRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Prepared target path is not normalized.' }
    foreach ($protectedRoot in @($projectRoot, $preparationRoot)) {
        $protectedPath = Get-InstallAbsolutePath $protectedRoot
        if ($resolvedTarget.Equals($protectedPath, [StringComparison]::OrdinalIgnoreCase) -or $resolvedTarget.StartsWith($protectedPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $protectedPath.StartsWith($resolvedTarget + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Installation target overlaps project sources or prepared artifacts: $resolvedTarget"
        }
    }
    Assert-ModPathNotLinked -Path $resolvedTarget
    foreach ($fileName in @('ThermalVortex.dll', 'ThermalVortex.pdb', 'ThermalVortex.json', 'ThermalVortex.pck')) {
        Assert-ModPathNotLinked -Path (Join-Path $resolvedTarget $fileName)
    }
}

function Read-PreparedInstall {
    param([string]$Path)
    $directory = Assert-PreparedDirectory -Path $Path
    $preparedManifestPath = Join-Path $directory 'prepared-install.json'
    $sealPath = Join-Path $directory 'prepared-install.sha256'
    foreach ($required in @($preparedManifestPath, $sealPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Prepared installation metadata is missing: $required" }
    }
    $sealedHash = [IO.File]::ReadAllText($sealPath).Trim()
    if ($sealedHash -cnotmatch '^[A-F0-9]{64}$' -or $sealedHash -cne (Get-FileHash -LiteralPath $preparedManifestPath -Algorithm SHA256).Hash.ToUpperInvariant()) {
        throw 'Prepared installation metadata changed after preparation. Prepare again.'
    }
    $manifest = Get-Content -LiteralPath $preparedManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.SchemaVersion -ne 1 -or -not ([string]$manifest.ProjectPath).Equals($projectPath, [StringComparison]::OrdinalIgnoreCase) -or -not ([string]$manifest.PreparedDirectory).Equals($directory, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Prepared installation does not belong to this project and directory.'
    }
    if ($manifest.IncludeResources -isnot [bool] -or $manifest.HasTaskProbe -isnot [bool] -or $manifest.Configuration -isnot [string] -or [string]::IsNullOrWhiteSpace($manifest.Configuration)) {
        throw 'Prepared installation has invalid build attributes.'
    }
    if ($manifest.HasTaskProbe -ne (-not [string]::IsNullOrWhiteSpace($manifest.TaskProbeSource))) { throw 'Prepared probe attributes do not match.' }
    Assert-InstallTarget -TargetModsPath $manifest.ModsPath -TargetModRoot $manifest.InstalledModRoot
    $expectedNames = @('ThermalVortex.dll', 'ThermalVortex.pdb', 'ThermalVortex.json')
    if ($manifest.IncludeResources) { $expectedNames += 'ThermalVortex.pck' }
    $files = @($manifest.Files)
    if ($files.Count -ne $expectedNames.Count) { throw 'Prepared installation has an incomplete or unexpected file set.' }
    $payload = Join-Path $directory 'build'
    if (-not (Test-Path -LiteralPath $payload -PathType Container)) { throw 'Prepared installation payload is missing.' }
    $payloadItems = @(Get-ChildItem -LiteralPath $payload -Force)
    if ($payloadItems.Count -ne $expectedNames.Count -or @($payloadItems | Where-Object { $_.PSIsContainer -or $_.Name -cnotin $expectedNames }).Count -gt 0) {
        throw 'Prepared payload has an incomplete or unexpected file set.'
    }
    foreach ($name in $expectedNames) {
        $entries = @($files | Where-Object { $_.Name -ceq $name })
        if ($entries.Count -ne 1) { throw "Prepared installation must contain exactly one '$name'." }
        $entry = $entries[0]
        if ($entry.RelativePath -cne "build/$name" -or $entry.Sha256 -cnotmatch '^[A-F0-9]{64}$') { throw "Prepared file metadata is invalid: $name" }
        $source = Join-Path $directory $entry.RelativePath
        if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToUpperInvariant() -cne $entry.Sha256) {
            throw "Prepared install file is missing or changed: $name. Prepare again."
        }
    }
    return $manifest
}

function New-PreparedInstall {
    if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
        $DotNetPath = Join-Path $workspaceRoot '.tools/dotnet/dotnet.exe'
        if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) { $DotNetPath = Join-Path $workspaceRoot '.tools/dotnet/dotnet' }
    }
    $resolvedDotNetPath = (Resolve-Path -LiteralPath $DotNetPath -ErrorAction Stop).ProviderPath
    if (-not (Test-Path -LiteralPath $resolvedDotNetPath -PathType Leaf)) { throw "Dotnet host is not a file: $resolvedDotNetPath" }
    $resolvedProbeSource = $null
    if (-not [string]::IsNullOrWhiteSpace($TaskProbeSource)) {
        $resolvedProbeSource = (Resolve-Path -LiteralPath $TaskProbeSource -ErrorAction Stop).ProviderPath
        if (-not (Test-Path -LiteralPath $resolvedProbeSource -PathType Leaf) -or [IO.Path]::GetExtension($resolvedProbeSource) -ne '.cs') { throw 'TaskProbeSource must be an existing C# file.' }
    }
    $resolvedSts2Path = $null
    if (-not [string]::IsNullOrWhiteSpace($Sts2Path)) { $resolvedSts2Path = Get-InstallAbsolutePath $Sts2Path }
    if ([string]::IsNullOrWhiteSpace($ModsPath)) {
        $queryArguments = @('msbuild', $projectPath, '-nologo', '-getProperty:ModsPath', "-p:Configuration=$Configuration", '-p:DeployMod=false', '-p:PckPackerEnabled=false')
        if ($resolvedSts2Path) { $queryArguments += "-p:Sts2Path=$resolvedSts2Path" }
        $savedPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $global:LASTEXITCODE = -1
            $queryOutput = @(& $resolvedDotNetPath @queryArguments 2>&1)
            $queryExitCode = $global:LASTEXITCODE
        }
        finally { $ErrorActionPreference = $savedPreference }
        if ($queryExitCode -ne 0) {
            $queryOutput | ForEach-Object { Write-Host $_ }
            throw "Could not resolve ModsPath (exit code $queryExitCode)."
        }
        $queryLines = @($queryOutput | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })
        if ($queryLines.Count -eq 0) { throw 'The project did not provide ModsPath. Pass -ModsPath explicitly.' }
        $ModsPath = $queryLines[$queryLines.Count - 1]
    }
    $resolvedModsPath = Get-InstallAbsolutePath $ModsPath
    $installedModRoot = Join-Path $resolvedModsPath 'ThermalVortex'
    Assert-InstallTarget -TargetModsPath $resolvedModsPath -TargetModRoot $installedModRoot
    Assert-ModPathNotLinked -Path $preparationRoot
    $directory = Join-Path $preparationRoot ('ThermalVortex-install-' + [Guid]::NewGuid().ToString('N'))
    $prepared = $false
    try {
        $buildOutput = Join-Path $directory 'build-output'
        $payload = Join-Path $directory 'build'
        [void][IO.Directory]::CreateDirectory($buildOutput)
        [void][IO.Directory]::CreateDirectory($payload)
        $buildParameters = @{ Configuration = $Configuration; DotNetPath = $resolvedDotNetPath; OutputPath = $buildOutput; BuildLockTimeoutSeconds = $BuildLockTimeoutSeconds }
        if ($Restore) { $buildParameters.Restore = $true }
        if ($resolvedSts2Path) { $buildParameters.Sts2Path = $resolvedSts2Path }
        if ($resolvedProbeSource) { $buildParameters.TaskProbeSource = $resolvedProbeSource }
        if ($IncludeResources) { $buildParameters.IncludeResources = $true; $buildParameters.ResourceTemporaryRoot = $directory }
        & (Join-Path $PSScriptRoot 'build-mod.ps1') @buildParameters | ForEach-Object { Write-Host $_ }

        $sources = [ordered]@{
            'ThermalVortex.dll' = Join-Path $buildOutput 'ThermalVortex.dll'
            'ThermalVortex.pdb' = Join-Path $buildOutput 'ThermalVortex.pdb'
            'ThermalVortex.json' = Join-Path $projectRoot 'ThermalVortex.json'
        }
        if ($IncludeResources) { $sources['ThermalVortex.pck'] = Join-Path $buildOutput 'ThermalVortex.pck' }
        $files = @(
            foreach ($source in $sources.GetEnumerator()) {
                if (-not (Test-Path -LiteralPath $source.Value -PathType Leaf)) { throw "Required install source is missing: $($source.Value)" }
                Assert-ModPathNotLinked -Path $source.Value
                $sourceHash = (Get-FileHash -LiteralPath $source.Value -Algorithm SHA256).Hash.ToUpperInvariant()
                $frozenPath = Join-Path $payload $source.Key
                Copy-Item -LiteralPath $source.Value -Destination $frozenPath
                if ((Get-FileHash -LiteralPath $frozenPath -Algorithm SHA256).Hash.ToUpperInvariant() -cne $sourceHash) { throw "Failed to freeze prepared bytes: $($source.Key)" }
                [pscustomobject]@{ Name = $source.Key; RelativePath = "build/$($source.Key)"; Sha256 = $sourceHash }
            }
        )
        $manifest = [ordered]@{
            SchemaVersion = 1
            PreparedDirectory = $directory
            ProjectPath = $projectPath
            Configuration = $Configuration
            ModsPath = $resolvedModsPath
            InstalledModRoot = $installedModRoot
            Sts2Path = $resolvedSts2Path
            IncludeResources = $IncludeResources.IsPresent
            HasTaskProbe = ($null -ne $resolvedProbeSource)
            TaskProbeSource = $resolvedProbeSource
            Files = $files
        }
        $preparedManifestPath = Join-Path $directory 'prepared-install.json'
        [IO.File]::WriteAllText($preparedManifestPath, ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
        # Integrity check only: this is not a freshness database or authorization.
        [IO.File]::WriteAllText((Join-Path $directory 'prepared-install.sha256'), (Get-FileHash -LiteralPath $preparedManifestPath -Algorithm SHA256).Hash.ToUpperInvariant(), (New-Object Text.UTF8Encoding($false)))
        $null = Read-PreparedInstall -Path $directory
        $prepared = $true
        Write-Host "Prepared installation: $directory"
        return [pscustomobject]@{ PreparedDirectory = $directory; ManifestPath = $preparedManifestPath; InstalledModRoot = $installedModRoot }
    }
    finally {
        if (-not $prepared -and (Test-Path -LiteralPath $directory)) {
            try { Remove-PreparedDirectory -Path $directory }
            catch { Write-Warning "Preparation cleanup failed: $($_.Exception.Message)" }
        }
    }
}

function Install-PreparedFiles {
    param([object]$Manifest)
    $directory = $Manifest.PreparedDirectory
    $targetRoot = $Manifest.InstalledModRoot
    $lifecycleLease = $null
    try {
        $lifecycleLease = Enter-RealModLifecycle -InstalledModRoot $targetRoot -TimeoutSeconds $LifecycleLockTimeoutSeconds
        Assert-ModGameStopped
        # Each copy also checks the frozen hash against source changes after
        # this validation. Locks do not freeze other tasks' source edits.
        $Manifest = Read-PreparedInstall -Path $directory
        $pckPath = Join-Path $targetRoot 'ThermalVortex.pck'
        $pckBefore = $null
        if (-not $Manifest.IncludeResources -and (Test-Path -LiteralPath $pckPath -PathType Leaf)) { $pckBefore = (Get-FileHash -LiteralPath $pckPath -Algorithm SHA256).Hash.ToUpperInvariant() }
        Assert-ModGameStopped
        [void][IO.Directory]::CreateDirectory($targetRoot)
        $backupRoot = Join-Path $directory 'installed-backup'
        [void][IO.Directory]::CreateDirectory($backupRoot)
        $backupState = @{}
        foreach ($entry in $Manifest.Files) {
            $target = Join-Path $targetRoot $entry.Name
            Assert-ModPathNotLinked -Path $target
            $exists = Test-Path -LiteralPath $target -PathType Leaf
            $backupState[$entry.Name] = @{ Existed = $exists; Sha256 = $null }
            if ($exists) {
                $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToUpperInvariant()
                $backupPath = Join-Path $backupRoot $entry.Name
                Copy-Item -LiteralPath $target -Destination $backupPath
                if ((Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToUpperInvariant() -cne $hash) { throw "Could not create a verified rollback copy: $target" }
                $backupState[$entry.Name].Sha256 = $hash
            }
        }
        Write-Host "==> Install to $targetRoot"
        try {
            $installed = @(
                foreach ($entry in $Manifest.Files) {
                    Copy-RealModFile -Source (Join-Path $directory $entry.RelativePath) -Destination (Join-Path $targetRoot $entry.Name) -ExpectedSha256 $entry.Sha256
                }
            )
            if (-not $Manifest.IncludeResources) {
                $pckAfter = $null
                if (Test-Path -LiteralPath $pckPath -PathType Leaf) { $pckAfter = (Get-FileHash -LiteralPath $pckPath -Algorithm SHA256).Hash.ToUpperInvariant() }
                if ($pckAfter -cne $pckBefore) { throw 'Code-only installation unexpectedly changed ThermalVortex.pck.' }
            }
        }
        catch {
            $installFailure = $_.Exception
            if (Test-ModGameRunningError -Exception $installFailure) {
                # A game observed during this transaction ends the write
                # attempt, even if it exits before rollback could begin.
                $stopped = [InvalidOperationException]::new("Installation stopped after a running game was observed; rollback was not attempted and restoration may be required. Install error: $($installFailure.Message)", $installFailure)
                $stopped.Data['ModGameRunning'] = $true
                throw $stopped
            }
            try {
                foreach ($entry in $Manifest.Files) {
                    $target = Join-Path $targetRoot $entry.Name
                    if ($backupState[$entry.Name].Existed) {
                        $null = Copy-RealModFile -Source (Join-Path $backupRoot $entry.Name) -Destination $target -ExpectedSha256 $backupState[$entry.Name].Sha256
                    }
                    else { Remove-RealModFile -Path $target }
                }
            }
            catch {
                $rollbackFailure = $_.Exception
                $combined = [InvalidOperationException]::new("Installation failed and rollback is incomplete. Install error: $($installFailure.Message) Rollback error: $($rollbackFailure.Message)", $installFailure)
                $combined.Data['RollbackError'] = $rollbackFailure.Message
                if ((Test-ModGameRunningError -Exception $installFailure) -or (Test-ModGameRunningError -Exception $rollbackFailure)) { $combined.Data['ModGameRunning'] = $true }
                throw $combined
            }
            throw [InvalidOperationException]::new("Installation failed; this install set was rolled back: $($installFailure.Message)", $installFailure)
        }
        $installed | Format-Table -AutoSize | Out-String | Write-Host
        if (-not $Manifest.IncludeResources) { Write-Host 'PCK: preserved (not built or copied)' }
        Write-Host 'Install complete.'
    }
    finally {
        if ($null -ne $lifecycleLease) { Exit-RealModLifecycle -Lease $lifecycleLease }
    }
}

if ($PSCmdlet.ParameterSetName -eq 'Prepare') {
    # Compatibility: ordinary install refuses a live game before any build.
    # Explicit preparation is artifact-only and may run while the game is open.
    if (-not $PrepareOnly) { Assert-ModGameStopped }
    $preparation = New-PreparedInstall
    if ($PrepareOnly) { return $preparation }
    $PreparedInstall = $preparation.PreparedDirectory
}
$preparedManifest = Read-PreparedInstall -Path $PreparedInstall
try {
    Install-PreparedFiles -Manifest $preparedManifest
}
finally {
    try { Remove-PreparedDirectory -Path $preparedManifest.PreparedDirectory }
    catch { Write-Warning "Prepared installation cleanup failed: $($_.Exception.Message)" }
}
