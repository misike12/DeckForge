# Renders DeckForge app icons from the liquid "D" tile design using WPF:
#   - app-icon.png  (256px, used in the window title bar and About card)
#   - app-icon.ico  (multi-size 256/128/64/48/32/16, compiled into DeckForge.exe)
# Usage: powershell -File generate-icon.ps1
Add-Type -AssemblyName PresentationCore, PresentationFramework, System.Xaml

function New-IconVisual([int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $ctx = $visual.RenderOpen()

    $bg = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x14,0x16,0x22))
    $ctx.DrawRoundedRectangle($bg, $null, [System.Windows.Rect]::new(0,0,$size,$size), 56 * $size / 256, 56 * $size / 256)

    $gradient = New-Object System.Windows.Media.LinearGradientBrush
    $gradient.StartPoint = [System.Windows.Point]::new(0,0)
    $gradient.EndPoint = [System.Windows.Point]::new(1,1)
    $g0 = New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromRgb(0x36,0x9E,0xEA)), 0
    $g1 = New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromRgb(0x4F,0x8C,0xFF)), 0.55
    $g2 = New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromRgb(0x6A,0x4F,0xF6)), 1
    $gradient.GradientStops.Add($g0); $gradient.GradientStops.Add($g1); $gradient.GradientStops.Add($g2)

    # Two stacked tiles with a gap: the deck metaphor
    $s = $size / 256
    $ctx.DrawRoundedRectangle($gradient, $null, [System.Windows.Rect]::new(64*$s, 44*$s, 128*$s, 100*$s), 40*$s, 40*$s)
    $ctx.DrawRoundedRectangle($gradient, $null, [System.Windows.Rect]::new(64*$s, 156*$s, 128*$s, 60*$s), 30*$s, 30*$s)

    # Accent dot
    $dot = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x2D,0xD4,0xBF))
    $ctx.DrawEllipse($dot, $null, [System.Windows.Point]::new(176*$s, 94*$s), 14*$s, 14*$s)

    $ctx.Close()
    return $visual
}

function New-IconBitmap([int]$size) {
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render((New-IconVisual $size))
    return $rtb
}

function Save-Png($rtb, [string]$outPath) {
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $fs = [System.IO.File]::Create($outPath)
    $encoder.Save($fs)
    $fs.Close()
}

$root = $PSScriptRoot

# ---- app-icon.png (256) ----
$png256 = Join-Path $root "app-icon.png"
Save-Png (New-IconBitmap 256) $png256
Write-Host "Wrote $png256"

# ---- app-icon.ico (PNG-compressed entries, multi-size) ----
$sizes = @(256, 128, 64, 48, 32, 16)
$pngs = @{}
foreach ($s in $sizes) {
    $tmp = Join-Path $env:TEMP "deckforge-icon-$s.png"
    Save-Png (New-IconBitmap $s) $tmp
    $pngs[$s] = [System.IO.File]::ReadAllBytes($tmp)
    Remove-Item $tmp -ErrorAction SilentlyContinue
}

$icoPath = Join-Path $root "app-icon.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)

# ICONDIRENTRY table (16 bytes each), then image blobs.
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $bytes = $pngs[$s]
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # width (0 = 256)
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # height (0 = 256)
    $bw.Write([Byte]0)   # palette size (PNG entries: 0)
    $bw.Write([Byte]0)   # reserved
    $bw.Write([UInt16]1) # color planes
    $bw.Write([UInt16]32)# bits per pixel
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($s in $sizes) {
    $bw.Write($pngs[$s])
}
$bw.Flush(); $bw.Close()
Write-Host "Wrote $icoPath"
