<#
.SYNOPSIS
  Renders the Fluxio logo mark to the Windows icon (.ico) and PNG files used by the
  desktop app and the website. Run on Windows: powershell -File scripts/generate-icons.ps1
  The vector source is the same as src/Fluxio.Web/public/favicon.svg.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 48.0
    $x = 2 * $s; $w = 44 * $s; $r = 13 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $x, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $x, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $x + $w - $d, $d, $d, 0, 90)
    $path.AddArc($x, $x + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.RectangleF $x, $x, $w, $w
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 0x22, 0xD3, 0xEE)), ([System.Drawing.Color]::FromArgb(255, 0x3B, 0x82, 0xF6)), 45.0
    $g.FillPath($brush, $path)

    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x0B, 0x10, 0x20)), ([single](3.6 * $s))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    # Top arrow: M14 19 h18 l-5 -5  (points right)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (14 * $s), (19 * $s)),
        (New-Object System.Drawing.PointF (32 * $s), (19 * $s)),
        (New-Object System.Drawing.PointF (27 * $s), (14 * $s))))
    # Bottom arrow: M34 29 H16 l5 5  (points left)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (34 * $s), (29 * $s)),
        (New-Object System.Drawing.PointF (16 * $s), (29 * $s)),
        (New-Object System.Drawing.PointF (21 * $s), (34 * $s))))

    $pen.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

function Save-Png([int]$size, [string]$path) {
    $bmp = New-LogoBitmap $size
    [System.IO.File]::WriteAllBytes($path, (Get-PngBytes $bmp))
    $bmp.Dispose()
}

function Save-Ico([int[]]$sizes, [string]$path) {
    $images = New-Object 'System.Collections.Generic.List[byte[]]'
    foreach ($size in $sizes) {
        $b = New-LogoBitmap $size
        $images.Add([byte[]](Get-PngBytes $b))
        $b.Dispose()
    }
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
}

$desktop = Join-Path $root 'src\Fluxio.Desktop\Resources'
New-Item -ItemType Directory -Force $desktop | Out-Null
Save-Ico @(16, 20, 24, 32, 40, 48, 64, 128, 256) (Join-Path $desktop 'fluxio.ico')
Save-Png 256 (Join-Path $desktop 'fluxio-256.png')

$landing = Join-Path $root 'src\Fluxio.Landing\assets'
New-Item -ItemType Directory -Force $landing | Out-Null
Save-Png 180 (Join-Path $landing 'apple-touch-icon.png')
Save-Png 512 (Join-Path $landing 'icon-512.png')

Write-Host 'Icons generated.'
