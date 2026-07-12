param(
    [string]$Configuration = "Debug",
    [string]$TargetRoot = $(Join-Path $env:ProgramData "Autodesk\ApplicationPlugins")
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$packageTemplateRoot = Join-Path $projectRoot "ApplicationPlugins\OmnibusCloud.3dsMax.Plugin"
$outputRoot = Join-Path $projectRoot ("bin\x64\{0}\net10.0-windows" -f $Configuration)
$destinationRoot = Join-Path $TargetRoot "OmnibusCloud.3dsMax.Plugin"
$destinationBinRoot = Join-Path $destinationRoot "Contents\bin"

if (-not (Test-Path $packageTemplateRoot))
{
    throw "Package template root was not found: $packageTemplateRoot"
}

if (-not (Test-Path $outputRoot))
{
    throw "Build output was not found: $outputRoot. Build OmnibusCloud.3dsMax.Plugin first."
}

New-Item -ItemType Directory -Force -Path $TargetRoot | Out-Null

if (Test-Path $destinationRoot)
{
    Remove-Item $destinationRoot -Recurse -Force
}

Copy-Item $packageTemplateRoot $destinationRoot -Recurse -Force
New-Item -ItemType Directory -Force -Path $destinationBinRoot | Out-Null
Copy-Item (Join-Path $outputRoot "*") $destinationBinRoot -Recurse -Force

Write-Host "Installed OutWit 3ds Max plugin package to: $destinationRoot"
Write-Host "Restart 3ds Max, then bind macro category 'OutWit' -> 'OutWit Export' to a menu, quad, or toolbar if needed."
