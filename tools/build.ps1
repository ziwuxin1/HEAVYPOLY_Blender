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

    $cscArgs = @(
        '/nologo', '/target:winexe', '/optimize+',
        ('/out:' + $OutExe),
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
    if (Test-Path $Staging) { Remove-Item -LiteralPath $Staging -Recurse -Force }
}
