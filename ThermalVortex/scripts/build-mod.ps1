[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$Configuration = 'Debug',
    [string]$DotNetPath,
    [string]$Sts2Path,
    [string]$OutputPath,
    [string]$TaskProbeSource,
    [switch]$Restore,
    [switch]$IncludeResources,
    [string]$ResourceRoot,
    [string]$ResourceTemporaryRoot,
    [ValidateRange(1, 60)][int]$BuildLockTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ModLifecycle.psm1') -ErrorAction Stop

function Get-BuildAbsolutePath {
    param([string]$Path)
    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Invoke-BuildDotNet {
    param([string[]]$Arguments, [switch]$Quiet)

    # Native stderr is data here: classify the exit code and specific SDK
    # dependency failures before deciding whether one restore is appropriate.
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = -1
        $output = @(& $resolvedDotNetPath @Arguments 2>&1)
        $exitCode = $global:LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedPreference
    }
    $lines = @($output | ForEach-Object { [string]$_ })
    if (-not $Quiet) {
        $lines | ForEach-Object { Write-Host $_ }
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Lines = $lines }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$projectPath = Join-Path $projectRoot 'ThermalVortex.csproj'
$packagesPath = Join-Path $workspaceRoot '.tools/nuget-packages'
if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
    $DotNetPath = Join-Path $workspaceRoot '.tools/dotnet/dotnet.exe'
    if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
        $DotNetPath = Join-Path $workspaceRoot '.tools/dotnet/dotnet'
    }
}
$resolvedDotNetPath = (Resolve-Path -LiteralPath $DotNetPath -ErrorAction Stop).ProviderPath
if (-not (Test-Path -LiteralPath $resolvedDotNetPath -PathType Leaf)) {
    throw "Dotnet host is not a file: $resolvedDotNetPath"
}

$commonProperties = @('-p:DeployMod=false', "-p:RestorePackagesPath=$packagesPath")
if (-not [string]::IsNullOrWhiteSpace($Sts2Path)) {
    $commonProperties += "-p:Sts2Path=$(Get-BuildAbsolutePath $Sts2Path)"
}
if (-not [string]::IsNullOrWhiteSpace($TaskProbeSource)) {
    $resolvedProbe = (Resolve-Path -LiteralPath $TaskProbeSource -ErrorAction Stop).ProviderPath
    if (-not (Test-Path -LiteralPath $resolvedProbe -PathType Leaf) -or [IO.Path]::GetExtension($resolvedProbe) -ne '.cs') {
        throw "TaskProbeSource must be an existing C# file: $TaskProbeSource"
    }
    $commonProperties += "-p:TaskProbeSource=$resolvedProbe"
}
else {
    # Reject ambient probe state without passing TaskProbeSource on ordinary
    # builds. Explicit probes are owned only by the requesting workflow.
    if (-not [string]::IsNullOrWhiteSpace($env:TaskProbeSource)) {
        throw 'An ambient TaskProbeSource is set. Clear it or explicitly pass the intended probe source.'
    }
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Get-BuildAbsolutePath $OutputPath
}
if (-not $IncludeResources -and ($PSBoundParameters.ContainsKey('ResourceRoot') -or $PSBoundParameters.ContainsKey('ResourceTemporaryRoot'))) {
    throw 'ResourceRoot and ResourceTemporaryRoot require -IncludeResources.'
}

# MSBuildProjectExtensionsPath is shared by restore and every configuration's
# build. Query the evaluated path, rather than assuming the SDK's default obj.
$query = Invoke-BuildDotNet -Quiet -Arguments (@('msbuild', $projectPath, '-nologo', '-getProperty:MSBuildProjectExtensionsPath', "-p:Configuration=$Configuration", '-p:PckPackerEnabled=false') + $commonProperties)
if ($query.ExitCode -ne 0) {
    $query.Lines | ForEach-Object { Write-Host $_ }
    throw "Could not resolve the shared build directory (exit code $($query.ExitCode))."
}
$queryLines = @($query.Lines | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($queryLines.Count -eq 0) { throw 'MSBuild did not provide MSBuildProjectExtensionsPath.' }
$intermediateRoot = $queryLines[$queryLines.Count - 1]
if (-not [IO.Path]::IsPathRooted($intermediateRoot)) { $intermediateRoot = Join-Path $projectRoot $intermediateRoot }
$intermediateRoot = [IO.Path]::GetFullPath($intermediateRoot)
$buildLease = $null
$buildArguments = @('build', $projectPath, '--configuration', $Configuration, '--no-restore', '--nologo') + $commonProperties
if ($OutputPath) { $buildArguments += @('--output', $OutputPath) }

$useNativeResourcePacker = $false
if ($IncludeResources) {
    if (-not $OutputPath -or -not $ResourceTemporaryRoot) {
        throw 'Resource builds require OutputPath and ResourceTemporaryRoot in a prepared workspace directory.'
    }
    if (-not $ResourceRoot) { $ResourceRoot = Join-Path $projectRoot 'ThermalVortex' }
    $resolvedResourceRoot = (Resolve-Path -LiteralPath $ResourceRoot).ProviderPath
    if (-not (Test-Path -LiteralPath $resolvedResourceRoot -PathType Container)) { throw "Resource root is not a directory: $ResourceRoot" }
    $ResourceTemporaryRoot = Get-BuildAbsolutePath $ResourceTemporaryRoot
    $resourcePrefix = $resolvedResourceRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $stagedResourceRoot = Join-Path $ResourceTemporaryRoot 'resources'
    [void][IO.Directory]::CreateDirectory($stagedResourceRoot)
    foreach ($resourceFile in Get-ChildItem -LiteralPath $resolvedResourceRoot -Recurse -File -Force) {
        $relativePath = $resourceFile.FullName.Substring($resourcePrefix.Length)
        $normalizedRelativePath = $relativePath.Replace('\', '/')
        if ($resourceFile.Name -match '\.bak(?:-|$)' -or $normalizedRelativePath.StartsWith('images/charui/character_select_bg_yugi_loop/', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $stagedPath = Join-Path $stagedResourceRoot $relativePath
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $stagedPath))
        Copy-Item -LiteralPath $resourceFile.FullName -Destination $stagedPath
        if ($resourceFile.Extension -in @('.tscn', '.gdshader', '.gdscript', '.gdextension')) { $useNativeResourcePacker = $true }
    }
    if (-not $useNativeResourcePacker) {
        $buildArguments += @("-p:PckPackerSourceDir=$stagedResourceRoot", '-p:PckPackerResPrefix=ThermalVortex', "-p:PckPackerOutputPath=$(Join-Path $OutputPath 'ThermalVortex.pck')")
    }
}
$managedPacker = $IncludeResources.IsPresent -and -not $useNativeResourcePacker
$buildArguments += "-p:PckPackerEnabled=$($managedPacker.ToString().ToLowerInvariant())"

try {
    $buildLease = Enter-ModBuild -IntermediateRoot $intermediateRoot -TimeoutSeconds $BuildLockTimeoutSeconds
    $restoreArguments = @('restore', $projectPath, '--packages', $packagesPath, '--nologo', "-p:Configuration=$Configuration", '-p:PckPackerEnabled=false') + $commonProperties
    $didRestore = $false
    if ($Restore -or -not (Test-Path -LiteralPath (Join-Path $intermediateRoot 'project.assets.json') -PathType Leaf)) {
        Write-Host '==> Restore (explicit dependency change or missing project assets)'
        $restoreResult = Invoke-BuildDotNet -Arguments $restoreArguments
        if ($restoreResult.ExitCode -ne 0) { throw "dotnet restore failed with exit code $($restoreResult.ExitCode)." }
        $didRestore = $true
    }

    Write-Host '==> Build (--no-restore; DeployMod=false)'
    $buildResult = Invoke-BuildDotNet -Arguments $buildArguments
    if ($buildResult.ExitCode -ne 0 -and -not $didRestore -and ($buildResult.Lines -join "`n") -match '\berror\s+(?:NETSDK1004|NETSDK1005|NETSDK1047|NETSDK1064)\b') {
        Write-Host '==> Restore once (build reported an explicit dependency-state error)'
        $restoreResult = Invoke-BuildDotNet -Arguments $restoreArguments
        if ($restoreResult.ExitCode -ne 0) { throw "dotnet restore failed with exit code $($restoreResult.ExitCode)." }
        $buildResult = Invoke-BuildDotNet -Arguments $buildArguments
    }
    if ($buildResult.ExitCode -ne 0) { throw "dotnet build failed with exit code $($buildResult.ExitCode)." }
}
finally {
    if ($null -ne $buildLease) { Exit-ModBuild -Lease $buildLease }
}
if ($useNativeResourcePacker) {
    Write-Host '==> Import and pack Godot scene/shader resources once'
    & (Join-Path $PSScriptRoot 'pack-native-resources.ps1') -ResourceRoot $stagedResourceRoot -OutputPath (Join-Path $OutputPath 'ThermalVortex.pck') -TemporaryRoot $ResourceTemporaryRoot | ForEach-Object { Write-Host $_ }
}
