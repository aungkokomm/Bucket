# Builds the Bucket portable installer end to end:
#   1. Publishes an unpackaged, self-contained Release build (app + WinAppSDK + .NET runtime)
#   2. Compiles installer\Bucket.iss with Inno Setup into dist\
#
# Usage:  pwsh -File tools\build_installer.ps1
# Requires: Visual Studio MSBuild (the .NET 10 dotnet CLI lacks the WinUI PRI task)
#           and Inno Setup 6 (ISCC.exe).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# --- locate tools ---
$msbuild = $null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -prerelease -find MSBuild\**\Bin\MSBuild.exe 2>$null | Select-Object -First 1
}
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    # Fall back to scanning the standard install roots.
    $msbuild = Get-ChildItem "C:\Program Files\Microsoft Visual Studio","C:\Program Files (x86)\Microsoft Visual Studio" `
        -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue |
        Where-Object FullName -like "*\Bin\MSBuild.exe" | Select-Object -Expand FullName -First 1
}
if (-not $msbuild -or -not (Test-Path $msbuild)) { throw "MSBuild not found. Install Visual Studio with the .NET desktop workload." }
Write-Host "Using MSBuild: $msbuild"

$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { throw "Inno Setup 6 (ISCC.exe) not found. Install from https://jrsoftware.org/isdl.php" }

$publishDir = Join-Path $root 'publish'

Write-Host "==> Publishing unpackaged self-contained build..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
& $msbuild (Join-Path $root 'Bucket.csproj') /t:Publish /restore `
    /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
    /p:WindowsPackageType=None /p:WindowsAppSDKSelfContained=true /p:SelfContained=true `
    /p:PublishTrimmed=false /p:PublishReadyToRun=false /p:PublishSingleFile=false `
    /p:AppxPackage=false /p:GenerateAppxPackageOnBuild=false `
    "/p:PublishDir=$publishDir\" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $publishDir 'Assets\AppIcon.ico'))) { throw "Assets were not copied to publish output." }

# Tidy the payload: only remove debug symbols and the crash-dump helper.
#
# IMPORTANT: do NOT delete the WinAppSDK localization (.mui) folders. The app's
# resources.pri still declares those languages, so on a localized Windows (e.g.
# German) the resource loader looks for the matching .mui and throws
# 0x80073B01 ("no MUI entry loaded") if it's missing — crashing the app on
# tooltips, min/maximize, etc. Keeping them costs a few MB but works everywhere.
Write-Host "==> Tidying payload (debug files only)..." -ForegroundColor Cyan
$before = (Get-ChildItem $publishDir -Recurse -File).Count
Get-ChildItem $publishDir -Recurse -Include *.pdb | Remove-Item -Force
Remove-Item (Join-Path $publishDir 'createdump.exe') -Force -ErrorAction SilentlyContinue
$after = (Get-ChildItem $publishDir -Recurse -File).Count
Write-Host ("    files {0} -> {1}; folders now {2}" -f $before, $after, (Get-ChildItem $publishDir -Directory).Count)

Write-Host "==> Compiling installer with Inno Setup..." -ForegroundColor Cyan
& $iscc (Join-Path $root 'installer\Bucket.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed (exit $LASTEXITCODE)." }

$setup = Get-ChildItem (Join-Path $root 'dist') -Filter 'Bucket-Setup-*.exe' | Sort-Object LastWriteTime | Select-Object -Last 1
Write-Host ("==> Done: {0} ({1} MB)" -f $setup.FullName, [math]::Round($setup.Length/1MB,1)) -ForegroundColor Green
