<#
.SYNOPSIS
    Rebuild HEAVYPOLY-Manager.exe (the standalone one-click installer).

.DESCRIPTION
    Packs config\ and scripts\ into payload.zip, then compiles
    tools\HeavypolyManager.cs with that zip AND tools\heavypoly_setup.ps1
    embedded as resources. The result is a single self-contained exe.

    Uses csc.exe from the .NET Framework that ships with Windows, so there is no
    build toolchain to install.

    Run this whenever config\, scripts\, HeavypolyManager.cs or
    heavypoly_setup.ps1 change, then commit the rebuilt exe.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutExe   = Join-Path $RepoRoot 'HEAVYPOLY-Manager.exe'
$stamp    = Get-Date -Format 'yyyyMMddHHmmss'
$Zip      = Join-Path $env:TEMP ("heavypoly_payload_$stamp.zip")
$Staging  = Join-Path $env:TEMP ("heavypoly_stage_$stamp")

$Ico = Join-Path $env:TEMP ("heavypoly_icon_$stamp.ico")

# Draw the application icon and write a multi-size .ico, so no binary icon file
# has to live in the repository. Explorer needs a real Win32 icon resource
# (/win32icon); the Form.Icon set at run time only covers the title bar.
function New-AppIcon {
    param([string]$Path)

    Add-Type -AssemblyName System.Drawing
    $sizes = @(256, 48, 32, 16)
    $pngs = @()

    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        $orange = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(237, 126, 22))
        $g.FillEllipse($orange, 0, 0, ($s - 1), ($s - 1))

        $fontPx = [int]($s * 0.60)
        if ($fontPx -lt 6) { $fontPx = 6 }
        $font = New-Object System.Drawing.Font 'Segoe UI', $fontPx, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $sf = New-Object System.Drawing.StringFormat
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
        $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
        $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
        $g.DrawString('H', $font, $white, $rect, $sf)

        $g.Dispose(); $orange.Dispose(); $white.Dispose(); $font.Dispose()

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += , ($ms.ToArray())
        $ms.Dispose(); $bmp.Dispose()
    }

    # ICONDIR + ICONDIRENTRY[] + PNG payloads (PNG-in-ICO, supported since Vista)
    $fs = [System.IO.File]::Create($Path)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([UInt16]0)                # reserved
    $bw.Write([UInt16]1)                # type = icon
    $bw.Write([UInt16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $dim = 0
        if ($s -lt 256) { $dim = $s }   # 0 means 256
        $bw.Write([byte]$dim)           # width
        $bw.Write([byte]$dim)           # height
        $bw.Write([byte]0)              # palette size
        $bw.Write([byte]0)              # reserved
        $bw.Write([UInt16]1)            # colour planes
        $bw.Write([UInt16]32)           # bits per pixel
        $bw.Write([UInt32]$pngs[$i].Length)
        $bw.Write([UInt32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($d in $pngs) { $bw.Write($d) }
    $bw.Flush(); $bw.Close(); $fs.Close()
}

$csc = Get-ChildItem 'C:\Windows\Microsoft.NET\Framework64' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'csc.exe' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
if (-not $csc) { throw 'csc.exe not found. Is the .NET Framework installed?' }
Write-Host "csc: $csc"

try {
    # --- stage the payload (config\ + scripts\, minus __pycache__) --------
    New-Item -ItemType Directory -Force -Path $Staging | Out-Null
    foreach ($sub in 'config', 'scripts') {
        $src = Join-Path $RepoRoot $sub
        if (-not (Test-Path $src)) { continue }
        Copy-Item -Path $src -Destination (Join-Path $Staging $sub) -Recurse -Force
    }
    Get-ChildItem $Staging -Recurse -Directory -Filter '__pycache__' -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }

    $count = (Get-ChildItem $Staging -Recurse -File).Count
    Write-Host ("payload: {0} files" -f $count)
    if ($count -eq 0) { throw "Nothing to pack - expected config\ and scripts\ under $RepoRoot" }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($Staging, $Zip)
    Write-Host ("payload.zip: {0} KB" -f [math]::Round((Get-Item $Zip).Length / 1KB, 1))

    # --- compile ----------------------------------------------------------
    $ps1 = Join-Path $PSScriptRoot 'heavypoly_setup.ps1'
    $cs  = Join-Path $PSScriptRoot 'HeavypolyManager.cs'

    New-AppIcon -Path $Ico
    Write-Host ("icon: {0} KB" -f [math]::Round((Get-Item $Ico).Length / 1KB, 1))

    $cscArgs = @(
        '/nologo', '/target:winexe', '/optimize+',
        ('/out:' + $OutExe),
        ('/win32icon:' + $Ico),
        '/reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll',
        ('/resource:' + $Zip + ',payload.zip'),
        ('/resource:' + $ps1 + ',heavypoly_setup.ps1'),
        $cs
    )
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

    Write-Host ''
    Write-Host ('BUILD OK -> {0}  ({1} KB)' -f $OutExe, [math]::Round((Get-Item $OutExe).Length / 1KB, 1)) -ForegroundColor Green
}
finally {
    if (Test-Path $Zip)     { Remove-Item -LiteralPath $Zip -Force }
    if (Test-Path $Ico)     { Remove-Item -LiteralPath $Ico -Force }
    if (Test-Path $Staging) { Remove-Item -LiteralPath $Staging -Recurse -Force }
}
