# Generates the Aero Control app icon (multi-size .ico + preview PNG).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
param(
    [string]$OutIco = "$PSScriptRoot\..\src\AeroControl\Assets\AeroControl.ico",
    [string]$OutPreview = "$PSScriptRoot\..\src\AeroControl\Assets\icon-preview.png"
)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-Squircle([single]$x, [single]$y, [single]$s, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $s - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $s - $d, $y + $s - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $s - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$size) {
    # draw big, then downscale for clean anti-aliasing
    $W = 1024
    $bmp = New-Object System.Drawing.Bitmap $W, $W
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'

    $pad = $W * 0.06; $side = $W - 2 * $pad; $rad = $side * 0.225
    $shape = New-Squircle $pad $pad $side $rad

    # body: graphite, lighter at the top (like the OLED glass catching light)
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, $side, $side
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 62, 62, 68)), ([System.Drawing.Color]::FromArgb(255, 14, 14, 16)), 90
    $g.FillPath($bg, $shape)

    # soft top sheen
    $sheenRect = New-Object System.Drawing.RectangleF $pad, $pad, $side, ($side * 0.5)
    $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush $sheenRect, ([System.Drawing.Color]::FromArgb(38, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90
    $g.SetClip($shape); $g.FillRectangle($sheen, $sheenRect); $g.ResetClip()

    # hairline rim, brighter on top
    $rim = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(18, 255, 255, 255)), 90
    $g.DrawPath((New-Object System.Drawing.Pen $rim, ($W * 0.006)), $shape)

    # gauge
    $cx = $W / 2; $cy = $pad + $side * 0.58; $R = $side * 0.31
    $arcRect = New-Object System.Drawing.RectangleF ($cx - $R), ($cy - $R), (2 * $R), (2 * $R)
    $stroke = $side * 0.085
    $track = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(55, 255, 255, 255)), $stroke
    $track.StartCap = 'Round'; $track.EndCap = 'Round'
    $g.DrawArc($track, $arcRect, 150, 240)

    $accentBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $arcRect, ([System.Drawing.Color]::FromArgb(255, 100, 210, 255)), ([System.Drawing.Color]::FromArgb(255, 10, 132, 255)), 0
    $accent = New-Object System.Drawing.Pen $accentBrush, $stroke
    $accent.StartCap = 'Round'; $accent.EndCap = 'Round'
    $sweep = 165
    $g.DrawArc($accent, $arcRect, 150, $sweep)

    # needle + hub
    $ang = (150 + $sweep) * [Math]::PI / 180
    $len = $R * 0.74
    $needle = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ($side * 0.055)
    $needle.StartCap = 'Round'; $needle.EndCap = 'Round'
    $g.DrawLine($needle, [single]$cx, [single]$cy, [single]($cx + $len * [Math]::Cos($ang)), [single]($cy + $len * [Math]::Sin($ang)))
    $hub = $side * 0.06
    $g.FillEllipse([System.Drawing.Brushes]::White, ($cx - $hub), ($cy - $hub), (2 * $hub), (2 * $hub))
    $g.Dispose()

    # downscale
    $out = New-Object System.Drawing.Bitmap $size, $size
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.InterpolationMode = 'HighQualityBicubic'; $g2.PixelOffsetMode = 'HighQuality'; $g2.CompositingQuality = 'HighQuality'
    $g2.DrawImage($bmp, 0, 0, $size, $size)
    $g2.Dispose(); $bmp.Dispose()
    return $out
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    , $ms.ToArray()
}

# .ico container with PNG-compressed entries (Windows Vista and later)
New-Item -ItemType Directory -Force (Split-Path $OutIco) | Out-Null
$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $OutIco)).Path + '\' + (Split-Path $OutIco -Leaf))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()

# preview: the icon at several sizes on dark and light backgrounds
$pv = New-Object System.Drawing.Bitmap 760, 300
$g = [System.Drawing.Graphics]::FromImage($pv)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 28, 28, 30))), 0, 0, 760, 150)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 236, 236, 240))), 0, 150, 760, 150)
foreach ($row in 0, 1) {
    $x = 16
    foreach ($s in 128, 64, 48, 32, 24, 16) {
        $b = Draw-Icon $s
        $g.DrawImage($b, $x, (150 * $row) + (75 - $s / 2), $s, $s); $b.Dispose()
        $x += $s + 28
    }
}
$g.Dispose()
$pv.Save($OutPreview, [System.Drawing.Imaging.ImageFormat]::Png)
"wrote $OutIco and $OutPreview"
