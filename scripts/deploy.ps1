<#
.SYNOPSIS
    Copies the built plugin into the Macro Deck plugins folder.

.DESCRIPTION
    Macro Deck loads plugins from %AppData%\Macro Deck\plugins\<packageId>. It
    holds the plugin DLL open while running, so Macro Deck must be closed before
    deploying. This script refuses to overwrite a locked file rather than leaving
    a half-copied plugin folder behind.

.PARAMETER Configuration
    Build configuration to deploy. Release by default.

.PARAMETER Force
    Deploy even if Macro Deck appears to be running. The copy will probably fail.

.EXAMPLE
    .\scripts\deploy.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$packageId = 'KeystoneDigital.YouTubeMusic'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "plugin\src\YouTubeMusicPlugin\bin\$Configuration"
$target = Join-Path $env:AppData "Macro Deck\plugins\$packageId"

if (-not (Test-Path (Join-Path $source 'YouTubeMusicPlugin.dll'))) {
    throw "Nothing built at '$source'. Run .\scripts\build.ps1 first."
}

$running = Get-Process -Name 'Macro Deck 2' -ErrorAction SilentlyContinue
if ($running -and -not $Force) {
    Write-Host 'Macro Deck is running. Close it and run this again.' -ForegroundColor Yellow
    Write-Host 'The plugin DLL cannot be replaced while Macro Deck has it loaded.'
    exit 1
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

$files = @(
    'YouTubeMusicPlugin.dll',
    'YouTubeMusicPlugin.deps.json',
    'Fleck.dll',
    'ExtensionManifest.json',
    'ExtensionIcon.png'
)

foreach ($file in $files) {
    $from = Join-Path $source $file
    if (-not (Test-Path $from)) { throw "Missing build output: $file" }
    Copy-Item $from -Destination $target -Force
}

# The pdb is optional but makes Macro Deck's log show real line numbers.
$pdb = Join-Path $source 'YouTubeMusicPlugin.pdb'
if (Test-Path $pdb) { Copy-Item $pdb -Destination $target -Force }

Write-Host "Installed to $target" -ForegroundColor Green
Write-Host 'Start Macro Deck, then open Extensions -> YouTube Music -> Configure to copy the token.'
