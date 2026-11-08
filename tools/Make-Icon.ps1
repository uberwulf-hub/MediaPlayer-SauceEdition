#Requires -Version 5.1
<#
.SYNOPSIS
    Draws Media Player (Sauce Edition)'s icon: an orange rounded square with sauce dripping from its top edge and a
    white play button, at 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels, into src\MediaPlayerSauce\MediaPlayer.ico and
    store\icon.png (256 px, for SauceStore). Run it again after changing the drawing.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$Repo = Split-Path -Parent $PSScriptRoot

function New-IconBitmap([int]$Size) {
    $s = [double]$Size
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $m = [Math]::Max(0.5, $s * 0.04)
    $r = $s * 0.22
    $body = [System.Windows.Rect]::new($m, $m, $s - 2 * $m, $s - 2 * $m)
    $grad = [System.Windows.Media.LinearGradientBrush]::new([System.Windows.Media.Color]::FromRgb(0xFF, 0x8C, 0x3A), [System.Windows.Media.Color]::FromRgb(0xE0, 0x3E, 0x26), 90)
    $dc.DrawRoundedRectangle($grad, $null, $body, $r, $r)
    # a darker band of sauce along the top with three drips (only where there's room to see it)
    if ($Size -ge 24) {
        $sauce = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromRgb(0xB8, 0x24, 0x18))
        $g = New-Object System.Windows.Media.StreamGeometry
        $ctx = $g.Open()
        $top = $m; $band = $s * 0.16
        $ctx.BeginFigure(([System.Windows.Point]::new($m, $top + $band)), $true, $true)
        $drips = @(@(0.26, 0.20), @(0.55, 0.30), @(0.78, 0.16))
        $x = $m
        foreach ($d in $drips) {
            $cx = $s * $d[0]; $len = $s * $d[1]; $w = $s * 0.075
            $ctx.LineTo(([System.Windows.Point]::new(($cx - $w), ($top + $band))), $true, $true)
            $ctx.BezierTo(([System.Windows.Point]::new(($cx - $w), ($top + $band + $len))), ([System.Windows.Point]::new(($cx + $w), ($top + $band + $len))), ([System.Windows.Point]::new(($cx + $w), ($top + $band))), $true, $true)
        }
        $ctx.LineTo(([System.Windows.Point]::new(($s - $m), ($top + $band))), $true, $true)
        $ctx.LineTo(([System.Windows.Point]::new(($s - $m), $top)), $true, $true)
        $ctx.LineTo(([System.Windows.Point]::new($m, $top)), $true, $true)
        $ctx.Close()
        $g.Freeze()
        $dc.PushClip(([System.Windows.Media.RectangleGeometry]::new($body, $r, $r)))
        $dc.DrawGeometry($sauce, $null, $g)
        # a little shine on the sauce
        $shine = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.Color]::FromArgb(90, 255, 255, 255))
        $dc.DrawEllipse($shine, $null, ([System.Windows.Point]::new(($s * 0.55 - $s * 0.025), ($top + $band + $s * 0.17))), ($s * 0.018), ($s * 0.035))
        $dc.Pop()
    }
    # the play button: a white triangle with soft corners, a little right of centre so it looks centred
    $tri = New-Object System.Windows.Media.StreamGeometry
    $c = $tri.Open()
    $cx = $s * 0.535; $cy = $s * 0.58; $h = $s * 0.40
    $p1 = [System.Windows.Point]::new(($cx - $h * 0.42), ($cy - $h / 2))
    $p2 = [System.Windows.Point]::new(($cx + $h * 0.5), $cy)
    $p3 = [System.Windows.Point]::new(($cx - $h * 0.42), ($cy + $h / 2))
    $c.BeginFigure($p1, $true, $true); $c.LineTo($p2, $true, $true); $c.LineTo($p3, $true, $true); $c.Close()
    $tri.Freeze()
    $pen = [System.Windows.Media.Pen]::new([System.Windows.Media.Brushes]::White, [Math]::Max(1, $s * 0.07))
    $pen.LineJoin = 'Round'
    $dc.DrawGeometry([System.Windows.Media.Brushes]::White, $pen, $tri)
    $dc.Close()
    $bmp = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    return $bmp
}

function Get-Png($bmp) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($z in $sizes) { , (Get-Png (New-IconBitmap $z)) }
# an .ico with PNG frames (Windows Vista and later read them)
$ms = New-Object System.IO.MemoryStream
$w = [System.IO.BinaryWriter]::new($ms)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $z = $sizes[$i]; $data = $pngs[$i]
    $w.Write([byte]($z % 256)); $w.Write([byte]($z % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $w.Write($data) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $Repo 'src\MediaPlayerSauce\MediaPlayer.ico'), $ms.ToArray())
[IO.File]::WriteAllBytes((Join-Path $Repo 'store\icon.png'), $pngs[$sizes.Count - 1])
Write-Host "Icon written ($($sizes -join ', ') px)."
