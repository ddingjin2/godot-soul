# Runs the ported test suite in a headless Godot. Exit code 0 = every test passed.
#
#   tools/run-tests.ps1
#   tools/run-tests.ps1 -Filter Checkpoint
#
# Unlike the Unity runners this replaces, nothing here takes a lock: a headless run owns no editor,
# so two of these can run at the same time.
param([string]$Filter)

$ErrorActionPreference = 'Stop'

# Godot writes its warnings to stderr, and Windows PowerShell 5.1 wraps every stderr line from a
# native executable in an ErrorRecord. With ErrorActionPreference = Stop that turns the first
# warning into a terminating error and the run dies mid-suite - which is exactly what happened when
# a filtered run hit the known SpriteFrameAnimator warning. The engine's own exit code is the only
# thing that decides pass or fail here, so native stderr must not be treated as a failure.
$nativeErrorAction = 'Continue'

$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'build.ps1') -Quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Compile failed - not running tests."
    & (Join-Path $PSScriptRoot 'build.ps1')
    exit 1
}

$godotArgs = @('--headless', 'res://Tests/TestMain.tscn')
if ($Filter) { $godotArgs += @('--', "--test-filter=$Filter") }

$ErrorActionPreference = $nativeErrorAction
& (Join-Path $PSScriptRoot 'godot.ps1') @godotArgs
exit $LASTEXITCODE
