# Regenerates the WixUI branding bitmaps from the OmnibusCloud brand assets:
#   OmnibusCloud-512.png      brand cloud mark (portal favicon source)
#   OmnibusCloud-Vertical.png device-mosaic vertical banner (desktop client login pane asset)
#   OmnibusCloud.ico          portal favicon.ico verbatim (Programs and Features icon)
# Outputs:
#   Banner.bmp  493x58   top strip of the inner installer dialogs (title text is drawn by MSI UI)
#   Dialog.bmp  493x312  welcome/exit dialog background; the left band replicates the desktop
#                        client login window's brand pane: navy #1C2A48, the vertical banner
#                        cropped UniformToFill at 0.88 opacity, bottom edge fading into the navy
# Run from anywhere: paths are resolved relative to this script. Requires Windows (System.Drawing).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = $PSScriptRoot
$cloud = [System.Drawing.Image]::FromFile((Join-Path $root 'OmnibusCloud-512.png'))
$banner = [System.Drawing.Image]::FromFile((Join-Path $root 'OmnibusCloud-Vertical.png'))
$paneNavy = [System.Drawing.Color]::FromArgb(0x1C, 0x2A, 0x48)

function New-Canvas([int]$w, [int]$h)
{
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::White)
    return $bmp, $g
}

# --- Banner.bmp: white strip, navy cloud on the right ---
$bmp, $g = New-Canvas 493 58
$size = 36
$g.DrawImage($cloud, (New-Object System.Drawing.Rectangle((493 - $size - 18), ([int]((58 - $size) / 2)), $size, $size)))
$g.Dispose()
$bmp.Save((Join-Path $root 'Banner.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()

# --- Dialog.bmp: client-login brand band on the left, white text area ---
$bmp, $g = New-Canvas 493 312
$band = 164
$g.FillRectangle((New-Object System.Drawing.SolidBrush($paneNavy)), 0, 0, $band, 312)

# Vertical banner, width-fit at the top of the band (the full cloud + wordmark stay visible,
# the excess height is cropped by the band clip), 0.88 opacity like the client's login pane
$fade = New-Object System.Drawing.Imaging.ColorMatrix
$fade.Matrix33 = 0.88
$fadeAttrs = New-Object System.Drawing.Imaging.ImageAttributes
$fadeAttrs.SetColorMatrix($fade)
$scale = $band / $banner.Width
$dw = [int][Math]::Ceiling($banner.Width * $scale)
$dh = [int][Math]::Ceiling($banner.Height * $scale)
$dest = New-Object System.Drawing.Rectangle(0, 0, $dw, $dh)
$g.SetClip((New-Object System.Drawing.Rectangle(0, 0, $band, 312)))
$g.DrawImage($banner, $dest, 0, 0, $banner.Width, $banner.Height, [System.Drawing.GraphicsUnit]::Pixel, $fadeAttrs)

# Soft fade of the banner's busy bottom edge into the navy band (mirrors the client's gradient):
# anchored to the banner's bottom edge so the mosaic dissolves fully before the flat navy
$fadeRect = New-Object System.Drawing.Rectangle(0, ($dh - 96), $band, 96)
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($fadeRect, ([System.Drawing.Color]::FromArgb(0, $paneNavy)), $paneNavy, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
$g.FillRectangle($grad, $fadeRect)
$g.ResetClip()
$g.Dispose()
$bmp.Save((Join-Path $root 'Dialog.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()
$cloud.Dispose(); $banner.Dispose()

Write-Host "Regenerated Banner.bmp and Dialog.bmp in $root"
