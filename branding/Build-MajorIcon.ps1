# Generates major.ico (multi-resolution Windows icon) from the v2 monogram design.
# Draws the M-glyph directly with System.Drawing — no external tools, no SVG parser.
# Output: branding/major.ico (and a major-256.png preview).
#
# Why custom drawing: SVG → ICO conversion needs Inkscape/ImageMagick/Resvg, which we
# don't want to install on dev machines and on the GitHub Actions runner. The shape
# is simple enough (rounded square + 4 line segments) to rasterize directly per size.

param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ''),
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

Add-Type -AssemblyName System.Drawing

# Brand palette — same as the SVG monogram.
$blue = [System.Drawing.Color]::FromArgb(0xFF, 0x4F, 0x5B, 0xFF)
$white = [System.Drawing.Color]::White
$shadow = [System.Drawing.Color]::FromArgb(0x73, 0xFF, 0xFF, 0xFF) # 45% white — under-line

function New-MajorBitmap {
    param([int]$Size)

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded-square plate (corner radius scales with size — 22% of side, как у SVG rx=56/256).
    $cornerRadius = [Math]::Max(2, [int]([Math]::Round($Size * 0.219)))
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(0, 0, $cornerRadius * 2, $cornerRadius * 2, 180, 90)
    $path.AddArc($Size - $cornerRadius * 2, 0, $cornerRadius * 2, $cornerRadius * 2, 270, 90)
    $path.AddArc($Size - $cornerRadius * 2, $Size - $cornerRadius * 2, $cornerRadius * 2, $cornerRadius * 2, 0, 90)
    $path.AddArc(0, $Size - $cornerRadius * 2, $cornerRadius * 2, $cornerRadius * 2, 90, 90)
    $path.CloseFigure()

    $blueBrush = [System.Drawing.SolidBrush]::new($blue)
    $g.FillPath($blueBrush, $path)
    $blueBrush.Dispose()
    $path.Dispose()

    # M glyph — same five points as SVG (60,196) → (60,60) → (128,144) → (196,60) → (196,196),
    # scaled to current Size (SVG viewBox is 256).
    $scale = $Size / 256.0
    function P([int]$x, [int]$y) {
        return [System.Drawing.PointF]::new($x * $scale, $y * $scale)
    }

    $points = @(
        (P 60 196),
        (P 60 60),
        (P 128 144),
        (P 196 60),
        (P 196 196)
    )

    # Stroke width scales linearly. Slightly thicker at small sizes for legibility.
    $strokeWidth = [Math]::Max(2.0, $Size * 0.086)
    $pen = [System.Drawing.Pen]::new($white, $strokeWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    for ($i = 0; $i -lt $points.Count - 1; $i++) {
        $g.DrawLine($pen, $points[$i], $points[$i + 1])
    }

    $pen.Dispose()

    # Under-shelf: tiny white bar under the M (only at sizes ≥48px — too noisy on small icons).
    if ($Size -ge 48) {
        $shelfBrush = [System.Drawing.SolidBrush]::new($shadow)
        $shelfHeight = [Math]::Max(1, [int]($Size * 0.023))
        $shelfWidth = [int]($Size * 0.625)
        $shelfLeft = [int](($Size - $shelfWidth) / 2.0)
        $shelfTop = [int]($Size * 0.781)
        $g.FillRectangle($shelfBrush, $shelfLeft, $shelfTop, $shelfWidth, $shelfHeight)
        $shelfBrush.Dispose()
    }

    $g.Dispose()
    return $bmp
}

# 256×256 PNG-preview — пригодится для README, GitHub release notes и т.п.
$preview = New-MajorBitmap -Size 256
$previewPath = Join-Path $OutputDirectory 'major-256.png'
$preview.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "PNG preview: $previewPath"

# Build multi-size ICO. ICO формат: header (6 bytes) + N×directory entries (16 bytes) +
# изображения подряд (PNG-encoded для размеров ≥256, сырой BMP для 16/32 — мы используем
# PNG-режим для всех размеров: Windows >= Vista это поддерживает.).
$icoPath = Join-Path $OutputDirectory 'major.ico'

$pngStreams = @()
foreach ($size in $Sizes) {
    $bmp = New-MajorBitmap -Size $size
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngStreams += [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
}

$icoStream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($icoStream)

# ICONDIR
$writer.Write([UInt16]0)                    # reserved
$writer.Write([UInt16]1)                    # type = icon (1)
$writer.Write([UInt16]$pngStreams.Count)    # image count

# ICONDIRENTRY offsets — directory занимает 6 + 16*N байт.
$offset = 6 + ($pngStreams.Count * 16)
foreach ($entry in $pngStreams) {
    $sizeByte = if ($entry.Size -ge 256) { 0 } else { [byte]$entry.Size }
    $writer.Write([byte]$sizeByte)          # width
    $writer.Write([byte]$sizeByte)          # height
    $writer.Write([byte]0)                  # palette
    $writer.Write([byte]0)                  # reserved
    $writer.Write([UInt16]1)                # planes
    $writer.Write([UInt16]32)               # bit depth
    $writer.Write([UInt32]$entry.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $entry.Bytes.Length
}

foreach ($entry in $pngStreams) {
    $writer.Write($entry.Bytes)
}

$writer.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $icoStream.ToArray())
$writer.Dispose()
$icoStream.Dispose()

Write-Host "ICO: $icoPath ($((Get-Item $icoPath).Length) bytes, sizes: $($Sizes -join ', '))"
