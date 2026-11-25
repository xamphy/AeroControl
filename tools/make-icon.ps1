# Generates the Aero Control icon from the reference logo (Desktop\appicon.png): a plain white "A" on
# transparent, rebuilt as a vector outline so it stays sharp from 16 px (tray) to 256 px.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
param(
    [string]$OutIco = "$PSScriptRoot\..\src\AeroControl\Assets\AeroControl.ico",
    [string]$OutPreview = "$PSScriptRoot\..\src\AeroControl\Assets\icon-preview.png",
    [string]$Reference = "$env:USERPROFILE\Desktop\appicon.png"
)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

# Outline measured from the 80x98 reference (its pixel units). Each vertex: x, y, corner radius.
# Legs run at slope 1:2; left leg is cut flat on the baseline; the right leg turns into a foot pointing back left.
$logo = @(
    @(3.25, 87.5, 3.5),   # left leg, outer bottom corner
    @(42.5, 9.0, 6.5),    # apex (outer), softly rounded
    @(77.5, 79.0, 1.5),   # right leg meets the foot's right side
    @(77.5, 87.5, 4.5),   # foot, bottom right
    @(49.5, 87.5, 1.0),   # foot, bottom left (squared end)
    @(49.5, 76.5, 1.0),   # foot, top left
    @(65.25, 76.5, 0.0),  # foot meets right leg inner edge
    @(42.5, 31.0, 0.0),   # apex (inner), sharp
    @(14.25, 87.5, 1.5)   # left leg, inner bottom corner
)
$minX = 3.25; $maxX = 77.5; $minY = 17.0; $maxY = 87.5   # visual bounds (rounded apex top is at y=17)

function New-LogoPath([single]$size, [single]$pad) {
    $scale = ($size - 2 * $pad) / [Math]::Max($maxX - $minX, $maxY - $minY)
    $ox = ($size - ($maxX - $minX) * $scale) / 2 - $minX * $scale
    $oy = ($size - ($maxY - $minY) * $scale) / 2 - $minY * $scale
    $pts = foreach ($v in $logo) { , @(($v[0] * $scale + $ox), ($v[1] * $scale + $oy), ($v[2] * $scale)) }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $n = $pts.Count
    $first = $true; $last = $null
    for ($i = 0; $i -lt $n; $i++) {
        $p = $pts[($i - 1 + $n) % $n]; $v = $pts[$i]; $q = $pts[($i + 1) % $n]
        $ax = $p[0] - $v[0]; $ay = $p[1] - $v[1]; $la = [Math]::Sqrt($ax * $ax + $ay * $ay); $ax /= $la; $ay /= $la
        $bx = $q[0] - $v[0]; $by = $q[1] - $v[1]; $lb = [Math]::Sqrt($bx * $bx + $by * $by); $bx /= $lb; $by /= $lb
        $r = $v[2]
        if ($r -le 0.01) {
            $pt = New-Object System.Drawing.PointF $v[0], $v[1]
            if ($first) { $start = $pt; $first = $false } else { $path.AddLine($last, $pt) }
            $last = $pt; continue
        }
        $theta = [Math]::Acos([Math]::Max(-1.0, [Math]::Min(1.0, [double]($ax * $bx + $ay * $by))))
        $t = $r / [Math]::Tan($theta / 2)
        $t1 = New-Object System.Drawing.PointF ($v[0] + $ax * $t), ($v[1] + $ay * $t)
        $t2 = New-Object System.Drawing.PointF ($v[0] + $bx * $t), ($v[1] + $by * $t)
        $k = (4.0 / 3.0) * [Math]::Tan(([Math]::PI - $theta) / 4) * $r   # cubic arc approximation
        $c1 = New-Object System.Drawing.PointF ($t1.X - $ax * $k), ($t1.Y - $ay * $k)
        $c2 = New-Object System.Drawing.PointF ($t2.X - $bx * $k), ($t2.Y - $by * $k)
        if ($first) { $start = $t1; $first = $false } else { $path.AddLine($last, $t1) }
        $path.AddBezier($t1, $c1, $c2, $t2)
        $last = $t2
    }
    $path.AddLine($last, $start)
    $path.CloseFigure()
    return $path
}

function Draw-Icon([int]$size, [System.Drawing.Color]$color) {
    $W = 1024
    $bmp = New-Object System.Drawing.Bitmap $W, $W
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    # small sizes get a touch less padding so the strokes stay thick enough
    $pad = if ($size -le 24) { $W * 0.04 } else { $W * 0.07 }
    $g.FillPath((New-Object System.Drawing.SolidBrush $color), (New-LogoPath $W $pad))
    $g.Dispose()
    $out = New-Object System.Drawing.Bitmap $size, $size
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.InterpolationMode = 'HighQualityBicubic'; $g2.PixelOffsetMode = 'HighQuality'; $g2.CompositingQuality = 'HighQuality'
    $g2.DrawImage($bmp, 0, 0, $size, $size)
    $g2.Dispose(); $bmp.Dispose()
    return $out
}

$white = [System.Drawing.Color]::White
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = Draw-Icon $s $white
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    , $ms.ToArray()
}

# .ico with PNG-compressed entries
New-Item -ItemType Directory -Force (Split-Path $OutIco) | Out-Null
$icoPath = Join-Path (Resolve-Path (Split-Path $OutIco)).Path (Split-Path $OutIco -Leaf)
$w = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($icoPath))
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length; $d = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()

# preview: reference (left) vs. rebuilt outline in the same grey, then white at icon sizes on a dark taskbar
$pv = New-Object System.Drawing.Bitmap 760, 220
$g = [System.Drawing.Graphics]::FromImage($pv)
$g.Clear([System.Drawing.Color]::FromArgb(255, 245, 245, 247))
if (Test-Path $Reference) { $ref = [System.Drawing.Image]::FromFile($Reference); $g.DrawImage($ref, 20, 10, 80, 98); $ref.Dispose() }
$cmp = Draw-Icon 98 ([System.Drawing.Color]::FromArgb(255, 58, 58, 58)); $g.DrawImage($cmp, 120, 10, 98, 98); $cmp.Dispose()
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 32, 34))), 0, 120, 760, 100)
$x = 20
foreach ($s in 64, 48, 32, 24, 20, 16) {
    $b = Draw-Icon $s $white
    $g.DrawImage($b, $x, 170 - $s / 2, $s, $s); $b.Dispose()
    $x += $s + 40
}
$g.Dispose()
$pv.Save($OutPreview, [System.Drawing.Imaging.ImageFormat]::Png)
"wrote $icoPath and $OutPreview"
