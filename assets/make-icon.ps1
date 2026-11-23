# Draws the app icon (a chip with an "update" arrow) and saves it as a multi-size .ico.
param([string]$Out = (Join-Path $PSScriptRoot 'app.ico'))

Add-Type -AssemblyName System.Drawing

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $bg = New-RoundedRect 0 0 ($s - 0.5) ($s - 0.5) ($s * 0.22)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(30, 42, 54))), $bg)

    # pins
    $pin = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(201, 211, 220))
    $pinLen = $s * 0.11; $pinW = [Math]::Max(1, $s * 0.06)
    foreach ($i in 0..2) {
        $c = $s * (0.36 + 0.14 * $i) - $pinW / 2
        $g.FillRectangle($pin, $c, $s * 0.16, $pinW, $pinLen)
        $g.FillRectangle($pin, $c, $s * 0.84 - $pinLen, $pinW, $pinLen)
        $g.FillRectangle($pin, $s * 0.16, $c, $pinLen, $pinW)
        $g.FillRectangle($pin, $s * 0.84 - $pinLen, $c, $pinLen, $pinW)
    }

    # chip body
    $body = New-RoundedRect ($s * 0.26) ($s * 0.26) ($s * 0.48) ($s * 0.48) ($s * 0.06)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(242, 140, 40))), $body)

    # up arrow
    $w = [System.Drawing.Brushes]::White
    $pts = @(
        (New-Object System.Drawing.PointF ($s * 0.50), ($s * 0.33)),
        (New-Object System.Drawing.PointF ($s * 0.64), ($s * 0.49)),
        (New-Object System.Drawing.PointF ($s * 0.36), ($s * 0.49))
    )
    $g.FillPolygon($w, [System.Drawing.PointF[]]$pts)
    $g.FillRectangle($w, $s * 0.455, $s * 0.48, $s * 0.09, $s * 0.19)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @(foreach ($s in $sizes) { , (New-IconPng $s) })

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "Icon written to $Out"
