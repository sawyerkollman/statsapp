# Generates src/Stats.App/Assets/app.ico (16/32/48/256 PNG frames): dark rounded square, orange rising bars.
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "src\Stats.App\Assets\app.ico"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function Draw([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [int]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2; $w = $s - 1
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x2B, 0x2B, 0x2F))
    $g.FillPath($bg, $path)
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xE6, 0x8A, 0x2E))
    $pad = [Math]::Max(2, [int]($s * 0.18)); $inner = $s - 2 * $pad
    $bw = [Math]::Max(1, [int]($inner / 4.2)); $gap = [Math]::Max(1, [int](($inner - 3 * $bw) / 2))
    $heights = @(0.45, 0.7, 1.0)
    for ($i = 0; $i -lt 3; $i++) {
        $bh = [int]($inner * $heights[$i])
        $x = $pad + $i * ($bw + $gap); $y = $pad + ($inner - $bh)
        $g.FillRectangle($accent, $x, $y, $bw, $bh)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 32, 48, 256
$frames = @()
foreach ($s in $sizes) { $frames += ,(Draw $s) }
if ($frames.Count -ne $sizes.Count) { throw "Expected $($sizes.Count) frames, got $($frames.Count)" }

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $frames[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))   # width (0 = 256)
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))   # height
    $bw.Write([byte]0); $bw.Write([byte]0)                    # palette, reserved
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)               # planes, bpp
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Flush(); $fs.Close()
Write-Host "Wrote $out ($((Get-Item $out).Length) bytes)"
