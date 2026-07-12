# Regenerates the WixUI branding bitmaps from the OmnibusCloud brand mark (OmnibusCloud-512.png,
# a copy of the portal favicon source; OmnibusCloud.ico is the portal favicon.ico verbatim).
#   Banner.bmp  493x58   top strip of the inner installer dialogs (title text is drawn by MSI UI)
#   Dialog.bmp  493x312  welcome/exit dialog background (navy brand band on the left)
# Run from anywhere: paths are resolved relative to this script. Requires Windows (System.Drawing).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = $PSScriptRoot
$src = [System.Drawing.Image]::FromFile((Join-Path $root 'OmnibusCloud-512.png'))
$navy = [System.Drawing.Color]::FromArgb(0x1A, 0x2B, 0x4C)

# Remaps the navy mark to solid white (keeps alpha) for drawing on the navy band
$toWhite = New-Object System.Drawing.Imaging.ColorMatrix
$toWhite.Matrix00 = 0; $toWhite.Matrix11 = 0; $toWhite.Matrix22 = 0
$toWhite.Matrix40 = 1; $toWhite.Matrix41 = 1; $toWhite.Matrix42 = 1
$whiteAttrs = New-Object System.Drawing.Imaging.ImageAttributes
$whiteAttrs.SetColorMatrix($toWhite)

function New-Canvas([int]$w, [int]$h)
{
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::White)
    return $bmp, $g
}

# --- Banner.bmp: white strip, navy cloud on the right ---
$bmp, $g = New-Canvas 493 58
$size = 36
$g.DrawImage($src, (New-Object System.Drawing.Rectangle((493 - $size - 18), ([int]((58 - $size) / 2)), $size, $size)))
$g.Dispose()
$bmp.Save((Join-Path $root 'Banner.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()

# --- Dialog.bmp: navy left band with the white cloud + wordmark, white text area ---
$bmp, $g = New-Canvas 493 312
$band = 164
$g.FillRectangle((New-Object System.Drawing.SolidBrush($navy)), 0, 0, $band, 312)
$logo = 92
$dest = New-Object System.Drawing.Rectangle([int](($band - $logo) / 2), 70, $logo, $logo)
$g.DrawImage($src, $dest, 0, 0, $src.Width, $src.Height, [System.Drawing.GraphicsUnit]::Pixel, $whiteAttrs)
$font = New-Object System.Drawing.Font('Segoe UI Semibold', 11.5, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
$text = 'OmnibusCloud'
$measured = $g.MeasureString($text, $font)
$g.DrawString($text, $font, [System.Drawing.Brushes]::White, [single](($band - $measured.Width) / 2), 172)
$font.Dispose(); $g.Dispose()
$bmp.Save((Join-Path $root 'Dialog.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()
$src.Dispose()

Write-Host "Regenerated Banner.bmp and Dialog.bmp in $root"
