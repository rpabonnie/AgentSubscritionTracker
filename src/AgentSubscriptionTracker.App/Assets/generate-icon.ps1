# SPEC-0003 §5.6 — one-time generator for Assets\AppIcon.ico.
#
# Draws a dual-bar gauge glyph (two stacked usage bars on a dark circle) at
# 16/20/24/32/48/256 px with GDI+ and packs a multi-image ICO (all images stored
# PNG-compressed, supported since Windows Vista).
#
# The build never runs this script. Regenerate by hand when the glyph changes:
#   powershell.exe -ExecutionPolicy Bypass -File generate-icon.ps1
# (Windows PowerShell 5.1: GDI+ available in-box; PowerShell 7 on Windows also works.)
#
# The script writes only AppIcon.ico next to itself and reads nothing else.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconPngBytes {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        # Dark circular backdrop.
        $backdrop = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 31, 36, 48))
        $g.FillEllipse($backdrop, 0, 0, $Size - 1, $Size - 1)
        $backdrop.Dispose()

        # Two stacked usage bars: track full width, fill at 70 % / 40 %.
        $pad   = [Math]::Max(3, [int]($Size * 0.24))
        $barH  = [Math]::Max(2, [int]($Size * 0.14))
        $gap   = [Math]::Max(2, [int]($Size * 0.12))
        $width = $Size - (2 * $pad)
        $top1  = [int](($Size - (2 * $barH) - $gap) / 2)
        $top2  = $top1 + $barH + $gap

        $track  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 70, 78, 96))
        $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 110, 139, 255))
        $green  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 63, 185, 80))

        $g.FillRectangle($track, $pad, $top1, $width, $barH)
        $g.FillRectangle($track, $pad, $top2, $width, $barH)
        $g.FillRectangle($accent, $pad, $top1, [int]($width * 0.7), $barH)
        $g.FillRectangle($green, $pad, $top2, [int]($width * 0.4), $barH)

        $track.Dispose(); $accent.Dispose(); $green.Dispose()
    }
    finally {
        $g.Dispose()
    }

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

$sizes = 16, 20, 24, 32, 48, 256
$images = foreach ($s in $sizes) { ,(New-IconPngBytes -Size $s) }

$outPath = Join-Path $PSScriptRoot 'AppIcon.ico'
$stream = [System.IO.File]::Create($outPath)
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    # ICONDIR
    $writer.Write([uint16]0)              # reserved
    $writer.Write([uint16]1)              # type: icon
    $writer.Write([uint16]$sizes.Count)   # image count

    # ICONDIRENTRY table (16 bytes each); image data follows the table.
    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))  # width  (0 = 256)
        $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))  # height (0 = 256)
        $writer.Write([byte]0)             # palette colors
        $writer.Write([byte]0)             # reserved
        $writer.Write([uint16]1)           # color planes
        $writer.Write([uint16]32)          # bits per pixel
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }

    foreach ($image in $images) {
        $writer.Write($image)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Host "Wrote $outPath ($((Get-Item $outPath).Length) bytes, sizes: $($sizes -join ', '))"
