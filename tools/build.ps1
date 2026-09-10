# Builds the C# assembly and reports the error count.
# Godot itself is not needed for this - it is the fastest way to find compile breakage.
param([switch]$Quiet)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    $output = & dotnet build 2>&1
    $errors = $output | Select-String -Pattern ': error '
    if ($errors) {
        if (-not $Quiet) { $errors | Select-Object -First 40 | ForEach-Object { $_.Line } }
        Write-Host ""
        Write-Host ("BUILD FAILED - {0} error line(s)" -f $errors.Count)
        exit 1
    }

    Write-Host "BUILD OK"
    exit 0
}
finally {
    Pop-Location
}
