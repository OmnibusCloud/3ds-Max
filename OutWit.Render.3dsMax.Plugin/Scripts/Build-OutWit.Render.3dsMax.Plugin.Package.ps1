<#
.SYNOPSIS
    Builds the distributable 3ds Max ApplicationPlugins bundle and zips it for release.

.DESCRIPTION
    Produces the same on-disk layout the Install script lays down, staged into a folder and zipped:

        OmnibusCloud.3dsMax.Plugin/
            PackageContents.xml          (AppVersion/FriendlyVersion stamped to -Version)
            Contents/
                macroscripts/ scripts/   (from the ApplicationPlugins template)
                bin/                     (the built x64 net10.0-windows output)

    Users install by unzipping into %ProgramData%\Autodesk\ApplicationPlugins\ (all users) or
    %APPDATA%\Autodesk\ApplicationPlugins\ (per user). Builds via the solution so the x64-only
    plugin project lands in bin/x64 (a bare `dotnet build` of the csproj defaults to AnyCPU).

    Code signing is optional and gated on environment variables (see -Sign): when the signing
    certificate is not configured the bundle ships UNSIGNED with a warning, so debug releases
    keep working before a certificate is issued.

.PARAMETER Configuration
    Build configuration (Release for distribution).

.PARAMETER Version
    Version stamped into PackageContents.xml AppVersion/FriendlyVersion and used in the zip name.

.PARAMETER OutputDirectory
    Directory the final zip is written to (created if missing).

.PARAMETER SkipBuild
    Package the existing build output without rebuilding.
#>
param(
    [string]$Configuration = "Release",
    [string]$Version = "0.0.0-dev",
    [string]$OutputDirectory = "dist",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Invoke-OptionalSigning
{
    # Authenticode-signs the bundled OutWit assemblies when a certificate is configured via
    # WINDOWS_CERT_PFX_BASE64 (+ optional WINDOWS_CERT_PASSWORD). Third-party assemblies (e.g.
    # Autodesk.Max.dll) are left as their vendors signed them. Without a certificate this is a
    # no-op + warning so unsigned debug releases keep working.
    param([string]$BinRoot)

    $pfxBase64 = $env:WINDOWS_CERT_PFX_BASE64
    if ([string]::IsNullOrWhiteSpace($pfxBase64))
    {
        Write-Warning "WINDOWS_CERT_PFX_BASE64 not set - shipping the plugin bundle UNSIGNED. Configure WINDOWS_CERT_PFX_BASE64/WINDOWS_CERT_PASSWORD to Authenticode-sign the assemblies."
        return
    }

    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source }
    if (-not $signtool) { throw "A signing certificate was provided but signtool.exe was not found." }

    $tempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [System.IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
    $pfxPath = Join-Path $tempRoot "outwit_sign.pfx"
    [System.IO.File]::WriteAllBytes($pfxPath, [System.Convert]::FromBase64String($pfxBase64))

    try
    {
        $targets = @(Get-ChildItem -Path $BinRoot -Recurse -Include "OmnibusCloud.*.dll", "OutWit.*.dll", "OutWit.*.exe" | Select-Object -ExpandProperty FullName)
        if ($targets.Count -eq 0)
        {
            Write-Warning "No OutWit assemblies found to sign under $BinRoot."
            return
        }

        $signArgs = @("sign", "/fd", "SHA256", "/tr", "http://timestamp.digicert.com", "/td", "SHA256", "/f", $pfxPath)
        if (-not [string]::IsNullOrWhiteSpace($env:WINDOWS_CERT_PASSWORD)) { $signArgs += @("/p", $env:WINDOWS_CERT_PASSWORD) }
        $signArgs += $targets

        Write-Host "Signing $($targets.Count) assemblies with $signtool..."
        & $signtool @signArgs
        if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE." }
    }
    finally
    {
        Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue
    }
}

$projectRoot = Split-Path -Parent $PSScriptRoot                       # ...\OutWit.Render.3dsMax.Plugin
$repoRoot = Split-Path -Parent $projectRoot                          # repo root
$solutionPath = Join-Path $repoRoot "OutWit.slnx"
$packageTemplateRoot = Join-Path $projectRoot "ApplicationPlugins\OmnibusCloud.3dsMax.Plugin"
$buildOutputRoot = Join-Path $projectRoot ("bin\x64\{0}\net10.0-windows" -f $Configuration)

if (-not (Test-Path $packageTemplateRoot))
{
    throw "Package template root was not found: $packageTemplateRoot"
}

# 1. Build (via the solution so the x64-only plugin project produces bin/x64 output).
#    -p:Version stamps the assemblies (AssemblyVersion from the numeric part, InformationalVersion
#    with the full suffix) so the About tab / sidebar footer show the real tag version.
if (-not $SkipBuild)
{
    Write-Host "Building $solutionPath ($Configuration, version $Version)..."
    dotnet build $solutionPath -c $Configuration --nologo -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path $buildOutputRoot))
{
    throw "Build output was not found: $buildOutputRoot. Build the solution first (or drop -SkipBuild)."
}

# 2. Stage the bundle: template + built binaries.
$stagingRoot = Join-Path $repoRoot "obj\plugin-package"
$bundleRoot = Join-Path $stagingRoot "OmnibusCloud.3dsMax.Plugin"
$bundleBinRoot = Join-Path $bundleRoot "Contents\bin"

if (Test-Path $stagingRoot) { Remove-Item $stagingRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
Copy-Item $packageTemplateRoot $bundleRoot -Recurse -Force
New-Item -ItemType Directory -Force -Path $bundleBinRoot | Out-Null
Copy-Item (Join-Path $buildOutputRoot "*") $bundleBinRoot -Recurse -Force

# 3. Stamp the version into the staged PackageContents.xml (source is left untouched).
$packageContentsPath = Join-Path $bundleRoot "PackageContents.xml"
$packageContents = Get-Content $packageContentsPath -Raw
$packageContents = $packageContents -replace 'AppVersion="[^"]*"', ('AppVersion="{0}"' -f $Version)
$packageContents = $packageContents -replace 'FriendlyVersion="[^"]*"', ('FriendlyVersion="{0}"' -f $Version)
Set-Content -Path $packageContentsPath -Value $packageContents -Encoding utf8
Write-Host "Stamped PackageContents.xml to version $Version."

# 4. Optional Authenticode signing of the bundled assemblies (gated on a configured certificate).
Invoke-OptionalSigning -BinRoot $bundleBinRoot

# 5. Zip the staged bundle (bundle folder at the zip root).
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$zipPath = Join-Path $OutputDirectory ("OmnibusCloud.3dsMax.Plugin-{0}.zip" -f $Version)
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $bundleRoot -DestinationPath $zipPath -Force

Write-Host "Built plugin package: $zipPath"
