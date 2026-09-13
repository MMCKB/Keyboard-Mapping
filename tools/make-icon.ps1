# Generate flat-design keyboard icon: Assets/app.ico (256/48/32/16) + Assets/icon-256.png preview
# Run: powershell -ExecutionPolicy Bypass -File tools/make-icon.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot "..\Assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
$outIco = Join-Path $assetsDir "app.ico"
$outPng = Join-Path $assetsDir "icon-256.png"

# Flat palette: dark blue-grey body, white keys, coral Enter key
$bodyColor   = [System.Drawing.Color]::FromArgb(255, 52, 73, 94)
$keyColor    = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
$accentColor = [System.Drawing.Color]::FromArgb(255, 255, 112, 67)

function New-RoundRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-RoundRect($graphics, $brush, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-RoundRectPath $x $y $w $h $r
    $graphics.FillPath($brush, $path)
    $path.Dispose()
}

function New-KeyboardBitmap([int]$size) {
    $s = $size / 256.0
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $bodyBrush   = New-Object System.Drawing.SolidBrush($bodyColor)
    $keyBrush    = New-Object System.Drawing.SolidBrush($keyColor)
    $accentBrush = New-Object System.Drawing.SolidBrush($accentColor)

    try {
        # keyboard body
        Draw-RoundRect $g $bodyBrush (16*$s) (72*$s) (224*$s) (112*$s) (16*$s)

        # keys on a 12-column grid; each row is a list of column spans
        $row1 = @(1,1,1,1,1,1,1,1,1,1,1,1)
        $row2 = @(1,1,1,1,1,1,1,1,1,1,1,1)
        $row3 = @(1,1,1,1,1,1,1,1,1,1,2)   # wide key = Enter (accent)
        $row4 = @(1,1,8,1,1)               # wide key = space
        $rows = @($row1, $row2, $row3, $row4)

        $pad = 14; $kh = 14; $gap = 5; $cols = 12
        $innerX = (16 + $pad) * $s
        $innerW = (224 - 2*$pad) * $s
        $kw = ($innerW - $gap*$s*($cols-1)) / $cols
        $rowYs = @(91, 111, 131, 151)

        for ($r = 0; $r -lt 4; $r++) {
            $y = $rowYs[$r] * $s
            $col = 0
            foreach ($span in $rows[$r]) {
                $x = $innerX + $col * ($kw + $gap*$s)
                $w = $span * $kw + ($span - 1) * $gap*$s
                $isEnter = ($r -eq 2 -and $span -eq 2)
                if ($isEnter) { $brush = $accentBrush } else { $brush = $keyBrush }
                Draw-RoundRect $g $brush $x $y $w ($kh*$s) (3.5*$s)
                $col += $span
            }
        }
    }
    finally {
        $g.Dispose()
        $bodyBrush.Dispose(); $keyBrush.Dispose(); $accentBrush.Dispose()
    }
    return ,$bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

# 32bpp BMP (DIB) entry: sizes 48/32/16 use BMP encoding in the ICO (256 uses PNG)
function Get-DibBytes($bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $andRow = [int]([Math]::Ceiling($w / 32.0) * 4)
    $dibSize = 40 + $stride*$h + $andRow*$h
    $dib = New-Object byte[] $dibSize    # AND mask stays all zero (alpha decides transparency)

    [BitConverter]::GetBytes([int]40).CopyTo($dib, 0)       # biSize
    [BitConverter]::GetBytes([int]$w).CopyTo($dib, 4)       # biWidth
    [BitConverter]::GetBytes([int]($h*2)).CopyTo($dib, 8)   # biHeight = XOR + AND
    [BitConverter]::GetBytes([uint16]1).CopyTo($dib, 12)    # biPlanes
    [BitConverter]::GetBytes([uint16]32).CopyTo($dib, 14)   # biBitCount

    for ($y = 0; $y -lt $h; $y++) {
        $srcOff = $y * $stride
        $dstOff = 40 + ($h - 1 - $y) * $stride
        [Array]::Copy($pixels, $srcOff, $dib, $dstOff, $stride)
    }
    return ,$dib
}

$master = New-KeyboardBitmap 256
$master.Save($outPng, [System.Drawing.Imaging.ImageFormat]::Png)

$entries = @()
$e = @{ Size = 256; Data = Get-PngBytes $master }
$entries += $e

foreach ($sz in @(48, 32, 16)) {
    $small = New-Object System.Drawing.Bitmap($sz, $sz)
    $g = [System.Drawing.Graphics]::FromImage($small)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $dstRect = New-Object System.Drawing.Rectangle(0, 0, $sz, $sz)
    $g.DrawImage($master, $dstRect)
    $g.Dispose()
    $e = @{ Size = $sz; Data = Get-DibBytes $small }
    $entries += $e
    $small.Dispose()
}

# assemble ICO: ICONDIR + ICONDIRENTRY list + image data
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$count = $entries.Count
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$count)
$offset = 6 + 16 * $count
foreach ($entry in $entries) {
    $sz = [int]$entry.Size
    if ($sz -ge 256) { $dimByte = 0 } else { $dimByte = [byte]$sz }
    $bw.Write([byte]$dimByte)        # width
    $bw.Write([byte]$dimByte)        # height
    $bw.Write([byte]0)               # color count
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # planes
    $bw.Write([uint16]32)            # bit count
    $bw.Write([uint32]$entry.Data.Length)
    $bw.Write([uint32]$offset)
    $offset += $entry.Data.Length
}
foreach ($entry in $entries) {
    $bw.Write($entry.Data)
}
[System.IO.File]::WriteAllBytes($outIco, $ms.ToArray())
$bw.Dispose()
$master.Dispose()

Write-Host "OK: $outIco"
Write-Host "OK: $outPng"
