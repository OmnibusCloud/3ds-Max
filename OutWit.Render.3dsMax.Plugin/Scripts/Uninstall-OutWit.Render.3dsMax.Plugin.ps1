param(
    [string]$TargetRoot = $(Join-Path $env:ProgramData "Autodesk\ApplicationPlugins")
)

$ErrorActionPreference = "Stop"

$destinationRoot = Join-Path $TargetRoot "OutWit.Render.3dsMax.Plugin"

if (-not (Test-Path $destinationRoot))
{
    Write-Host "OutWit 3ds Max plugin package is not installed at: $destinationRoot"
    exit 0
}

Remove-Item $destinationRoot -Recurse -Force
Write-Host "Removed OutWit 3ds Max plugin package from: $destinationRoot"
Write-Host "Restart 3ds Max to unload any previously cached UI state for the package."
