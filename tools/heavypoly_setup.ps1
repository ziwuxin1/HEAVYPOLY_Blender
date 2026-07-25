<#
.SYNOPSIS
    Install or uninstall the HEAVYPOLY Blender config.

.DESCRIPTION
    Install copies config/ and scripts/ into a Blender user-config directory
    (%APPDATA%\Blender Foundation\Blender\<version>\) and records exactly what it
    wrote into a manifest.

    Uninstall reads that manifest and removes ONLY the files the installer wrote,
    then restores anything it had backed up. It never deletes a directory
    wholesale, so your own scripts in scripts/startup/ are left alone.

.PARAMETER Action
    install (default) or uninstall.

.PARAMETER BlenderVersion
    e.g. "5.2". Omit to auto-detect; if several are present the newest is used
    unless -Interactive is given.

.PARAMETER ConfigRoot
    Override the Blender config root. Mainly for testing.

.PARAMETER Interactive
    Prompt to choose the Blender version when more than one is found.

.EXAMPLE
    .\heavypoly_setup.ps1 -Action install
    .\heavypoly_setup.ps1 -Action uninstall -BlenderVersion 5.2
#>
[CmdletBinding()]
param(
    [ValidateSet('install', 'uninstall')]
    [string]$Action = 'install',
    [string]$BlenderVersion = '',
    [string]$ConfigRoot = (Join-Path $env:APPDATA 'Blender Foundation\Blender'),
    [switch]$Interactive
)

$ErrorActionPreference = 'Stop'
$MANIFEST_NAME = '.heavypoly-manifest.json'

# Repo root is the parent of tools/
$RepoRoot = Split-Path -Parent $PSScriptRoot

function Write-Step { param([string]$m) Write-Host "  $m" }
function Write-Ok   { param([string]$m) Write-Host "  [OK] $m" -ForegroundColor Green }
function Write-Warn { param([string]$m) Write-Host "  [!]  $m" -ForegroundColor Yellow }
function Write-Err  { param([string]$m) Write-Host "  [X]  $m" -ForegroundColor Red }

function Get-BlenderVersions {
    if (-not (Test-Path $ConfigRoot)) { return @() }
    Get-ChildItem $ConfigRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } |
        Select-Object -ExpandProperty Name
}

function Resolve-TargetVersion {
    if ($BlenderVersion) {
        $p = Join-Path $ConfigRoot $BlenderVersion
        if (-not (Test-Path $p)) {
            throw "Blender config directory not found: $p`nStart Blender $BlenderVersion once so it creates its config folder, then re-run."
        }
        return $BlenderVersion
    }
    $found = @(Get-BlenderVersions)
    if ($found.Count -eq 0) {
        throw "No Blender config directory found under:`n  $ConfigRoot`nStart Blender once so it creates one, then re-run."
    }
    if ($found.Count -eq 1) { return $found[0] }

    if ($Interactive) {
        Write-Host ''
        Write-Host 'Multiple Blender versions found:'
        for ($i = 0; $i -lt $found.Count; $i++) { Write-Host ("  [{0}] {1}" -f ($i + 1), $found[$i]) }
        while ($true) {
            $sel = Read-Host ("Choose 1-{0} (Enter = newest, {1})" -f $found.Count, $found[-1])
            if ([string]::IsNullOrWhiteSpace($sel)) { return $found[-1] }
            $n = 0
            if ([int]::TryParse($sel, [ref]$n) -and $n -ge 1 -and $n -le $found.Count) { return $found[$n - 1] }
        }
    }
    Write-Warn ("Multiple versions found ({0}); using newest: {1}" -f ($found -join ', '), $found[-1])
    return $found[-1]
}

function Get-PayloadFiles {
    # Everything the installer ships, as paths relative to the repo root.
    $items = @()
    foreach ($sub in 'config', 'scripts') {
        $dir = Join-Path $RepoRoot $sub
        if (-not (Test-Path $dir)) { continue }
        Get-ChildItem $dir -Recurse -File |
            Where-Object { $_.FullName -notmatch '\\__pycache__\\' } |
            ForEach-Object { $items += $_.FullName.Substring($RepoRoot.Length).TrimStart('\') }
    }
    return $items
}

function Invoke-Uninstall {
    param([string]$TargetOverride = '', [switch]$Quiet)

    if ($TargetOverride) {
        $target  = $TargetOverride
        $version = Split-Path -Leaf $target
    } else {
        $version = Resolve-TargetVersion
        $target  = Join-Path $ConfigRoot $version
    }

    if (-not $Quiet) {
        Write-Host ''
        Write-Host "Uninstalling HEAVYPOLY <- Blender $version" -ForegroundColor Cyan
        Write-Step "target: $target"
    }

    $manifestPath = Join-Path $target $MANIFEST_NAME
    if (-not (Test-Path $manifestPath)) {
        Write-Warn 'No HEAVYPOLY manifest found here - nothing is recorded as installed.'
        Write-Warn 'Not guessing which files to delete. If you installed by hand, remove them manually.'
        return
    }

    $m = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

    $removed = 0
    foreach ($rel in @($m.files)) {
        if (-not $rel) { continue }
        $p = Join-Path $target $rel
        if (Test-Path $p) { Remove-Item -LiteralPath $p -Force; $removed++ }
    }

    $restored = 0
    foreach ($b in @($m.backups)) {
        if (-not $b) { continue }
        $orig = Join-Path $target $b.original
        $bak  = Join-Path (Split-Path -Parent $orig) $b.backup
        if (Test-Path $bak) { Move-Item -LiteralPath $bak -Destination $orig -Force; $restored++ }
    }

    Remove-Item -LiteralPath $manifestPath -Force

    # Drop directories we may have created, but only when now empty. Never recursive.
    foreach ($d in 'scripts\startup', 'scripts\addons', 'scripts', 'config') {
        $p = Join-Path $target $d
        if ((Test-Path $p) -and -not (Get-ChildItem $p -Force)) { Remove-Item -LiteralPath $p -Force }
    }

    if (-not $Quiet) {
        Write-Ok ("removed {0} file(s)" -f $removed)
        if ($restored) { Write-Ok ("restored {0} backed-up file(s)" -f $restored) }
        Write-Host ''
        Write-Host 'Done. Restart Blender to get the stock keymap back.' -ForegroundColor Green
    }
}

function Invoke-Install {
    $version = Resolve-TargetVersion
    $target  = Join-Path $ConfigRoot $version
    Write-Host ''
    Write-Host "Installing HEAVYPOLY -> Blender $version" -ForegroundColor Cyan
    Write-Step "target: $target"

    $payload = @(Get-PayloadFiles)
    if ($payload.Count -eq 0) {
        throw "No files found to install under $RepoRoot (expected config/ and scripts/)."
    }

    $manifestPath = Join-Path $target $MANIFEST_NAME
    if (Test-Path $manifestPath) {
        Write-Warn 'HEAVYPOLY is already installed here; removing the previous installation first.'
        Invoke-Uninstall -TargetOverride $target -Quiet
    }

    $stamp   = Get-Date -Format 'yyyyMMdd_HHmmss'
    $backups = @()
    $written = @()

    foreach ($rel in $payload) {
        $src    = Join-Path $RepoRoot $rel
        $dst    = Join-Path $target  $rel
        $dstDir = Split-Path -Parent $dst
        if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }

        if (Test-Path $dst) {
            # Never silently clobber a pre-existing file (e.g. your own userpref.blend).
            $bak = "$dst.pre-heavypoly.$stamp"
            Copy-Item -LiteralPath $dst -Destination $bak -Force
            $backups += [pscustomobject]@{ original = $rel; backup = (Split-Path -Leaf $bak) }
            Write-Warn ("backed up existing {0}" -f $rel)
        }
        Copy-Item -LiteralPath $src -Destination $dst -Force
        $written += $rel
    }

    $commit = ''
    try { $commit = (& git -C $RepoRoot rev-parse --short HEAD 2>$null) } catch { }

    [pscustomobject]@{
        installedAt    = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        blenderVersion = $version
        sourceCommit   = "$commit".Trim()
        files          = $written
        backups        = $backups
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    Write-Ok ("installed {0} files" -f $written.Count)
    if ($backups.Count) { Write-Ok ("backed up {0} pre-existing file(s)" -f $backups.Count) }
    Write-Step "manifest: $manifestPath"
    Write-Host ''
    Write-Host 'Done. Restart Blender.' -ForegroundColor Green
    Write-Host '  Try Ctrl+Space in the 3D viewport - the Select pie menu should appear.'
}

try {
    if ($Action -eq 'install') { Invoke-Install } else { Invoke-Uninstall }
    exit 0
} catch {
    Write-Host ''
    Write-Err $_.Exception.Message
    exit 1
}
