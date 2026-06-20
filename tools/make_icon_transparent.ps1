# Removes the white background from the source bucket render (flood-fill from the
# edges so the white document/arrow INSIDE the bucket are preserved), feathers the
# boundary to avoid a halo, then exports every WinUI asset size plus a multi-res .ico
# — all with a transparent background.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$Source = $args[0]
if (-not $Source -or -not (Test-Path $Source)) { throw "Pass the source PNG path as arg 1." }
$AssetsDir = Resolve-Path (Join-Path $PSScriptRoot '..\Assets')

# --- load + crop to a centered square master at 512 ---
$src = [Drawing.Image]::FromFile($Source)
$M = 512
$master = New-Object Drawing.Bitmap $M, $M, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [Drawing.Graphics]::FromImage($master)
$g.InterpolationMode = 'HighQualityBicubic'
$g.PixelOffsetMode   = 'HighQuality'
$side = [Math]::Min($src.Width, $src.Height)
$g.DrawImage($src, (New-Object Drawing.Rectangle 0,0,$M,$M),
    [int](($src.Width-$side)/2), [int](($src.Height-$side)/2), $side, $side, [Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()

# --- flood-fill the white background to transparent ---
$rect = New-Object Drawing.Rectangle 0,0,$M,$M
$data = $master.LockBits($rect, [Drawing.Imaging.ImageLockMode]::ReadWrite, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $data.Stride
$buf = New-Object byte[] ($stride * $M)
[Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)

$thr = 236      # a pixel is "background white" when R,G,B all >= thr
$visited = New-Object 'bool[]' ($M * $M)
$stack = New-Object System.Collections.Generic.Stack[int]

function Idx([int]$x,[int]$y) { return $y*$stride + $x*4 }

# seed the four borders
$last = $M - 1
for ($x=0; $x -lt $M; $x++) {
    foreach ($y in @(0, $last)) { $stack.Push($y*$M + $x) }
}
for ($y=0; $y -lt $M; $y++) {
    foreach ($x in @(0, $last)) { $stack.Push($y*$M + $x) }
}

while ($stack.Count -gt 0) {
    $p = $stack.Pop()
    if ($visited[$p]) { continue }
    $visited[$p] = $true
    $x = $p % $M; $y = [int]($p / $M)
    $i = Idx $x $y
    if ($buf[$i+2] -lt $thr -or $buf[$i+1] -lt $thr -or $buf[$i] -lt $thr) { continue } # not background
    $buf[$i+3] = 0  # make transparent
    if ($x -gt 0)      { $n = $p-1;  if (-not $visited[$n]) { $stack.Push($n) } }
    if ($x -lt $M-1)   { $n = $p+1;  if (-not $visited[$n]) { $stack.Push($n) } }
    if ($y -gt 0)      { $n = $p-$M; if (-not $visited[$n]) { $stack.Push($n) } }
    if ($y -lt $M-1)   { $n = $p+$M; if (-not $visited[$n]) { $stack.Push($n) } }
}

# --- feather: soften light pixels that touch the now-transparent background ---
for ($y=0; $y -lt $M; $y++) {
    for ($x=0; $x -lt $M; $x++) {
        $i = Idx $x $y
        if ($buf[$i+3] -eq 0) { continue }
        $minc = [Math]::Min([Math]::Min($buf[$i+2], $buf[$i+1]), $buf[$i])
        if ($minc -lt 212) { continue }   # only very light pixels can be halo
        $touchesBg = $false
        if ($x -gt 0      -and $buf[(Idx ($x-1) $y)+3] -eq 0) { $touchesBg = $true }
        if (-not $touchesBg -and $x -lt $M-1 -and $buf[(Idx ($x+1) $y)+3] -eq 0) { $touchesBg = $true }
        if (-not $touchesBg -and $y -gt 0    -and $buf[(Idx $x ($y-1))+3] -eq 0) { $touchesBg = $true }
        if (-not $touchesBg -and $y -lt $M-1 -and $buf[(Idx $x ($y+1))+3] -eq 0) { $touchesBg = $true }
        if (-not $touchesBg) { continue }
        # lighter => more transparent (minc 212 -> opaque, 255 -> fully clear)
        $a = [int]([Math]::Round((255 - $minc) * (255.0 / 43.0)))
        if ($a -lt 0) { $a = 0 } elseif ($a -gt 255) { $a = 255 }
        $buf[$i+3] = [byte]$a
    }
}

[Runtime.InteropServices.Marshal]::Copy($buf, 0, $data.Scan0, $buf.Length)
$master.UnlockBits($data)

# --- export assets (all transparent) ---
function Render([int]$w,[int]$h){
    $b = New-Object Drawing.Bitmap $w, $h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gr = [Drawing.Graphics]::FromImage($b)
    $gr.InterpolationMode = 'HighQualityBicubic'; $gr.PixelOffsetMode='HighQuality'; $gr.SmoothingMode='AntiAlias'
    $gr.Clear([Drawing.Color]::Transparent)
    $s = [Math]::Min($w,$h)
    $gr.DrawImage($master, [int](($w-$s)/2), [int](($h-$s)/2), $s, $s)
    $gr.Dispose(); return $b
}
function Save($b,[string]$n){ $b.Save((Join-Path $AssetsDir $n), [Drawing.Imaging.ImageFormat]::Png); Write-Host "  $n ($($b.Width)x$($b.Height))" }

$square = @{
    'Square44x44Logo.scale-200.png'=88; 'Square44x44Logo.targetsize-24_altform-unplated.png'=24
    'Square44x44Logo.targetsize-48_altform-lightunplated.png'=48; 'Square150x150Logo.scale-200.png'=300
    'LockScreenLogo.scale-200.png'=48; 'StoreLogo.png'=50
}
foreach($k in $square.Keys){ $b=Render $square[$k] $square[$k]; Save $b $k; $b.Dispose() }
$w=Render 620 300; Save $w 'Wide310x150Logo.scale-200.png'; $w.Dispose()
$s2=Render 620 300; Save $s2 'SplashScreen.scale-200.png'; $s2.Dispose()

# multi-res ico
$sizes = 16,24,32,48,64,128,256
$frames = foreach($sz in $sizes){ $b=Render $sz $sz; $ms=New-Object IO.MemoryStream; $b.Save($ms,[Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); @{Size=$sz;Bytes=$ms.ToArray()} }
$fs=[IO.File]::Open((Join-Path $AssetsDir 'AppIcon.ico'),[IO.FileMode]::Create); $bw=New-Object IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count); $off=6+16*$frames.Count
foreach($f in $frames){ $d=[byte]($(if($f.Size -ge 256){0}else{$f.Size})); $bw.Write($d);$bw.Write($d);$bw.Write([byte]0);$bw.Write([byte]0);$bw.Write([UInt16]1);$bw.Write([UInt16]32);$bw.Write([UInt32]$f.Bytes.Length);$bw.Write([UInt32]$off);$off+=$f.Bytes.Length }
foreach($f in $frames){ $bw.Write($f.Bytes) }
$bw.Close(); $fs.Close()
Write-Host "  AppIcon.ico ($($frames.Count) frames)"

$master.Save((Join-Path $AssetsDir 'BucketIcon-master.png'), [Drawing.Imaging.ImageFormat]::Png)
$master.Dispose()
Write-Host "Done (transparent)."
