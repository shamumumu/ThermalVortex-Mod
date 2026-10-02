[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ResourceRoot,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TemporaryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NativePackDirectory {
    param([string]$Path, [string]$Description)

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).ProviderPath
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "$Description is not a directory: $resolved"
    }
    return $resolved
}

function Assert-NativePackRun {
    param($Result, [string]$Description)

    Write-Host "$Description result: $($Result.resultPath)"
    if (-not $Result.succeeded) {
        throw "$Description failed (Godot exit code $($Result.exitCode), $($Result.exitCodeHex)): $($Result.errorMessage) Stdout: $($Result.stdoutPath) Stderr: $($Result.stderrPath)"
    }

    # The editor can finish with exit code zero after reporting a failed import
    # or script parse. Do not install resources from such an incomplete import.
    foreach ($logPath in @($Result.stdoutPath, $Result.stderrPath)) {
        $reportedErrors = @(Select-String -LiteralPath $logPath -Pattern '^\s*(?:SCRIPT )?ERROR:' | Select-Object -First 5)
        if ($reportedErrors.Count -gt 0) {
            $messages = ($reportedErrors | ForEach-Object { $_.Line.Trim() }) -join ' | '
            throw "$Description reported errors: $messages Log: $logPath Result: $($Result.resultPath)"
        }
    }
}

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$launcherPath = Join-Path $workspaceRoot 'ThermalVortex/design/yugi-character-rig/Invoke-GodotTool.ps1'
$godotPath = Join-Path $workspaceRoot '.tools/godot-rig/Godot_v4.5.1-stable_win64.exe'
$packerScriptPath = Join-Path $PSScriptRoot 'pack-native-resources.gd'
foreach ($requiredFile in @($launcherPath, $godotPath, $packerScriptPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Native resource pack dependency is missing: $requiredFile"
    }
}

$resolvedResourceRoot = Get-NativePackDirectory -Path $ResourceRoot -Description 'Staged resource root'
$resolvedTemporaryRoot = Get-NativePackDirectory -Path $TemporaryRoot -Description 'Install temporary root'
$resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$temporaryPrefix = $resolvedTemporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutputPath.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Native packing only writes its artifact inside the install temporary root: $resolvedOutputPath"
}

$projectDirectory = Join-Path $resolvedTemporaryRoot ('native-resource-project-' + [Guid]::NewGuid().ToString('N'))
$projectResources = Join-Path $projectDirectory 'ThermalVortex'
[void][IO.Directory]::CreateDirectory($projectResources)
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedOutputPath))

$resourcePrefix = $resolvedResourceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
foreach ($resourceFile in Get-ChildItem -LiteralPath $resolvedResourceRoot -Recurse -File -Force) {
    $relativePath = $resourceFile.FullName.Substring($resourcePrefix.Length)
    $stagedPath = Join-Path $projectResources $relativePath
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $stagedPath))
    Copy-Item -LiteralPath $resourceFile.FullName -Destination $stagedPath
}

$projectConfig = @'
config_version=5

[application]
config/name="ThermalVortex resource pack"

[editor]
import/use_multiple_threads=false

[debug]
file_logging/enable_file_logging=false
file_logging/enable_file_logging.pc=false

[rendering]
renderer/rendering_method="gl_compatibility"
renderer/rendering_method.mobile="gl_compatibility"
'@
[IO.File]::WriteAllText((Join-Path $projectDirectory 'project.godot'), $projectConfig, (New-Object Text.UTF8Encoding($false)))
Copy-Item -LiteralPath $packerScriptPath -Destination (Join-Path $projectDirectory 'pack-native-resources.gd')

# Logs live outside the install temporary root so installer cleanup cannot erase
# a failed Godot invocation's exact exit code, stdout, stderr, and process identity.
$logRoot = Join-Path $workspaceRoot '.codex-temp/godot-tool-runs'
Write-Host '==> Import staged resources with Godot 4.5.1'
$importResult = & $launcherPath `
    -GodotPath $godotPath `
    -WorkingDirectory $projectDirectory `
    -LogRoot $logRoot `
    -RunName 'mod-resource-import' `
    -TimeoutSeconds 600 `
    -AllowFailure `
    -Arguments @('--headless', '--editor', '--path', $projectDirectory, '--import')
Assert-NativePackRun -Result $importResult -Description 'Native resource import'

Write-Host '==> Pack staged native resources once'
$packResult = & $launcherPath `
    -GodotPath $godotPath `
    -WorkingDirectory $projectDirectory `
    -LogRoot $logRoot `
    -RunName 'mod-resource-pack' `
    -TimeoutSeconds 180 `
    -AllowFailure `
    -Arguments @('--headless', '--path', $projectDirectory, '--script', 'res://pack-native-resources.gd', '--', $resolvedOutputPath)
Assert-NativePackRun -Result $packResult -Description 'Native resource pack'

if (-not (Test-Path -LiteralPath $resolvedOutputPath -PathType Leaf) -or (Get-Item -LiteralPath $resolvedOutputPath).Length -eq 0) {
    throw "Native resource pack did not produce a nonempty PCK: $resolvedOutputPath"
}
Write-Host "PCK packed: $resolvedOutputPath"
