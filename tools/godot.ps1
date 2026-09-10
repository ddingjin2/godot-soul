# Locates the Godot 4.7 .NET binary and runs it against this project.
#
# The winget install of Godot cannot create a PATH alias without admin rights, so the path is
# resolved here once instead of at every call site. Set $env:GODOT_BIN to override.
#
#   tools/godot.ps1 --headless --import      # import assets, no window
#   tools/godot.ps1 --headless --quit-after 3 res://Scenes/GameplayScene.tscn
#   tools/godot.ps1                          # open the editor

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Resolve-GodotBin {
    if ($env:GODOT_BIN -and (Test-Path $env:GODOT_BIN)) { return $env:GODOT_BIN }

    $wingetRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path $wingetRoot) {
        $found = Get-ChildItem -Path $wingetRoot -Recurse -Filter 'Godot_v4*_mono_win64_console.exe' -ErrorAction SilentlyContinue |
                 Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    $onPath = Get-Command godot -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    throw "Godot not found. Install with 'winget install GodotEngine.GodotEngine.Mono' or set GODOT_BIN."
}

$bin = Resolve-GodotBin
& $bin --path $root @args
exit $LASTEXITCODE
