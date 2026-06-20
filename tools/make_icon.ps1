# Generates the Bucket app icon (a Fluent gradient tile with a white pail and
# items dropping into it) and exports every WinUI asset size plus a multi-res .ico.
# Pure GDI+ so it needs no external tooling (ImageMagick/Inkscape not required).
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$AssetsDir = Join-Path $PSScriptRoot '..\Assets' | Resolve-Path

function New-RoundedPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r){
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x,        $y,        $d,$d,180,90) | Out-Null
    $p.AddArc($x+$w-$d,  $y,        $d,$d,270,90) | Out-Null
    $p.AddArc($x+$w-$d,  $y+$h-$d,  $d,$d,0,90)   | Out-Null
    $p.AddArc($x,        $y+$h-$d,  $d,$d,90,90)  | Out-Null
    $p.CloseFigure()
    return $p
}

# Draws one card/file dropping into the bucket.
function Draw-Card($g,[float]$cx,[float]$cy,[float]$size,[float]$angle,[int]$alpha){
    $state = $g.Save()
    $g.TranslateTransform($cx,$cy)
    $g.RotateTransform($angle)
    $half = $size/2
    $card = New-RoundedPath (-$half) (-$half) $size $size ($size*0.16)
    $fill = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb($alpha,255,255,255))
    $g.FillPath($fill,$card)
    # thin accent bar so it reads as a file/card, not a blank square
    $accent = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb([int]($alpha*0.9),6,182,212))
    $bar = New-RoundedPath (-$half*0.55) (-$half*0.35) ($size*0.55) ($size*0.16) ($size*0.06)
    $g.FillPath($accent,$bar)
    $card.Dispose(); $fill.Dispose(); $accent.Dispose(); $bar.Dispose()
    $g.Restore($state)
}

# Renders the full icon at WxH. $rounded => rounded gradient tile (square logos);
# otherwise full-bleed gradient (wide/splash). Content is centered & scaled to min(W,H).
function Render-Icon([int]$W,[int]$H,[bool]$rounded){
    $bmp = New-Object Drawing.Bitmap $W,$H
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode   = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)

    # --- background gradient (indigo -> teal) ---
    $rect = New-Object Drawing.RectangleF 0,0,$W,$H
    $c1 = [Drawing.Color]::FromArgb(255,79,70,229)    # indigo
    $c2 = [Drawing.Color]::FromArgb(255,6,182,212)    # teal/cyan
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush $rect,$c1,$c2,45
    if($rounded){
        $tile = New-RoundedPath 0 0 $W $H ($W*0.20)
        $g.FillPath($grad,$tile); $tile.Dispose()
    } else {
        $g.FillRectangle($grad,$rect)
    }

    # --- bucket geometry, based on the smaller dimension ---
    $S  = [Math]::Min($W,$H)
    $cx = $W/2.0
    $cy = $H/2.0
    $rxT = $S*0.225          # top rim radius
    $rxB = $S*0.165          # base radius
    $yTop = $cy + $S*0.02    # rim center y
    $yBot = $cy + $S*0.255   # base center y
    $ryT = $S*0.052          # rim ellipse half-height
    $ryB = $S*0.038          # base ellipse half-height

    # soft contact shadow
    $sh = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(55,0,0,0))
    $g.FillEllipse($sh, ($cx-$rxB*1.15), ($yBot-$ryB*0.2), ($rxB*2.3), ($ryB*2.6)); $sh.Dispose()

    # bucket body (trapezoid + front curves) with a subtle vertical gradient
    $body = New-Object Drawing.Drawing2D.GraphicsPath
    $body.AddLine(($cx-$rxT),$yTop,($cx-$rxB),$yBot) | Out-Null
    $body.AddArc(($cx-$rxB),($yBot-$ryB),($rxB*2),($ryB*2),180,-180) | Out-Null   # bottom front curve
    $body.AddLine(($cx+$rxB),$yBot,($cx+$rxT),$yTop) | Out-Null
    $body.AddArc(($cx-$rxT),($yTop-$ryT),($rxT*2),($ryT*2),0,180) | Out-Null       # front lip
    $body.CloseFigure()
    $bodyRect = New-Object Drawing.RectangleF ($cx-$rxT),($yTop-$ryT),($rxT*2),($yBot-$yTop+$ryT*2)
    $bodyBrush = New-Object Drawing.Drawing2D.LinearGradientBrush $bodyRect, ([Drawing.Color]::White), ([Drawing.Color]::FromArgb(255,225,232,240)), 90
    $g.FillPath($bodyBrush,$body)
    $body.Dispose(); $bodyBrush.Dispose()

    # opening (darker inner ellipse = the hole)
    $inner = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255,206,216,229))
    $g.FillEllipse($inner,($cx-$rxT),($yTop-$ryT),($rxT*2),($ryT*2)); $inner.Dispose()
    $rim = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255,255,255,255)),($S*0.012)
    $g.DrawEllipse($rim,($cx-$rxT),($yTop-$ryT),($rxT*2),($ryT*2)); $rim.Dispose()

    # items dropping in
    Draw-Card $g ($cx+$S*0.015) ($yTop-$S*0.085) ($S*0.155) -16 255
    Draw-Card $g ($cx-$S*0.145) ($yTop-$S*0.235) ($S*0.115)  18 210

    $g.Dispose()
    return $bmp
}

function Save-Png($bmp,[string]$name){
    $path = Join-Path $AssetsDir $name
    $bmp.Save($path,[Drawing.Imaging.ImageFormat]::Png)
    Write-Host "  $name  ($($bmp.Width)x$($bmp.Height))"
}

Write-Host "Rendering square assets..."
$master = Render-Icon 1024 1024 $true
$square = @{
    'Square44x44Logo.scale-200.png' = 88
    'Square44x44Logo.targetsize-24_altform-unplated.png' = 24
    'Square44x44Logo.targetsize-48_altform-lightunplated.png' = 48
    'Square150x150Logo.scale-200.png' = 300
    'LockScreenLogo.scale-200.png' = 48
    'StoreLogo.png' = 50
}
foreach($k in $square.Keys){
    $sz = $square[$k]
    $b = New-Object Drawing.Bitmap $master,$sz,$sz
    Save-Png $b $k; $b.Dispose()
}

Write-Host "Rendering wide assets..."
$wide  = Render-Icon 620 300 $false
Save-Png $wide 'Wide310x150Logo.scale-200.png'
$splash = Render-Icon 620 300 $false
Save-Png $splash 'SplashScreen.scale-200.png'
$wide.Dispose(); $splash.Dispose()

# --- multi-resolution .ico (PNG-compressed frames) ---
Write-Host "Building AppIcon.ico..."
$sizes = 16,24,32,48,64,128,256
$frames = @()
foreach($s in $sizes){
    $b = New-Object Drawing.Bitmap $master,$s,$s
    $ms = New-Object IO.MemoryStream
    $b.Save($ms,[Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size=$s; Bytes=$ms.ToArray() }
    $b.Dispose(); $ms.Dispose()
}
$icoPath = Join-Path $AssetsDir 'AppIcon.ico'
$fs = [IO.File]::Open($icoPath,[IO.FileMode]::Create)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)  # ICONDIR
$offset = 6 + 16*$frames.Count
foreach($f in $frames){
    $dim = [byte]($(if($f.Size -ge 256){0}else{$f.Size}))
    $bw.Write($dim); $bw.Write($dim)          # width,height (0 => 256)
    $bw.Write([byte]0); $bw.Write([byte]0)    # palette, reserved
    $bw.Write([UInt16]1); $bw.Write([UInt16]32) # planes, bpp
    $bw.Write([UInt32]$f.Bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $f.Bytes.Length
}
foreach($f in $frames){ $bw.Write($f.Bytes) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Host "  AppIcon.ico  ($($frames.Count) frames)"

$master.Dispose()
Write-Host "Done."
