# Regenerates all Bucket icon assets from a single source PNG (the 3D bucket render).
# High-quality downscale to every WinUI asset size plus a multi-resolution .ico.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$Source    = $args[0]
if (-not $Source -or -not (Test-Path $Source)) { throw "Pass the source PNG path as the first argument." }
$AssetsDir = Resolve-Path (Join-Path $PSScriptRoot '..\Assets')

$src = [Drawing.Image]::FromFile($Source)

# Square master: crop the source to a centered square so non-square sources don't distort.
$side = [Math]::Min($src.Width, $src.Height)
$master = New-Object Drawing.Bitmap $side, $side
$g = [Drawing.Graphics]::FromImage($master)
$g.InterpolationMode = 'HighQualityBicubic'
$g.PixelOffsetMode   = 'HighQuality'
$g.DrawImage($src, (New-Object Drawing.Rectangle 0,0,$side,$side),
    [int](($src.Width-$side)/2), [int](($src.Height-$side)/2), $side, $side, [Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$src.Dispose()

function Render([int]$W,[int]$H,[bool]$square){
    $bmp = New-Object Drawing.Bitmap $W,$H
    $gr = [Drawing.Graphics]::FromImage($bmp)
    $gr.InterpolationMode = 'HighQualityBicubic'
    $gr.PixelOffsetMode   = 'HighQuality'
    $gr.SmoothingMode     = 'AntiAlias'
    if ($square) {
        $gr.DrawImage($master, 0, 0, $W, $H)
    } else {
        # Wide/splash: source is on white, so fill white and center the square art.
        $gr.Clear([Drawing.Color]::White)
        $s = [Math]::Min($W,$H)
        $gr.DrawImage($master, [int](($W-$s)/2), [int](($H-$s)/2), $s, $s)
    }
    $gr.Dispose()
    return $bmp
}

function Save($bmp,[string]$name){
    $bmp.Save((Join-Path $AssetsDir $name), [Drawing.Imaging.ImageFormat]::Png)
    Write-Host ("  {0}  ({1}x{2})" -f $name, $bmp.Width, $bmp.Height)
}

Write-Host "Square assets..."
$square = @{
    'Square44x44Logo.scale-200.png' = 88
    'Square44x44Logo.targetsize-24_altform-unplated.png' = 24
    'Square44x44Logo.targetsize-48_altform-lightunplated.png' = 48
    'Square150x150Logo.scale-200.png' = 300
    'LockScreenLogo.scale-200.png' = 48
    'StoreLogo.png' = 50
}
foreach($k in $square.Keys){ $b = Render $square[$k] $square[$k] $true; Save $b $k; $b.Dispose() }

Write-Host "Wide assets..."
$w = Render 620 300 $false; Save $w 'Wide310x150Logo.scale-200.png'; $w.Dispose()
$s = Render 620 300 $false; Save $s 'SplashScreen.scale-200.png'; $s.Dispose()

Write-Host "AppIcon.ico..."
$sizes = 16,24,32,48,64,128,256
$frames = foreach($sz in $sizes){
    $b = Render $sz $sz $true
    $ms = New-Object IO.MemoryStream
    $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    @{ Size=$sz; Bytes=$ms.ToArray() }
}
$ico = Join-Path $AssetsDir 'AppIcon.ico'
$fs = [IO.File]::Open($ico, [IO.FileMode]::Create)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
$offset = 6 + 16*$frames.Count
foreach($f in $frames){
    $dim = [byte]($(if($f.Size -ge 256){0}else{$f.Size}))
    $bw.Write($dim); $bw.Write($dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$f.Bytes.Length); $bw.Write([UInt32]$offset)
    $offset += $f.Bytes.Length
}
foreach($f in $frames){ $bw.Write($f.Bytes) }
$bw.Close(); $fs.Close()
Write-Host ("  AppIcon.ico  ({0} frames)" -f $frames.Count)

# Keep a copy of the source master for future regeneration.
$master.Save((Join-Path $AssetsDir 'BucketIcon-master.png'), [Drawing.Imaging.ImageFormat]::Png)
$master.Dispose()
Write-Host "Done."
