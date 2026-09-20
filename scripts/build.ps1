<#
.SYNOPSIS
    Builds the Macro Deck plugin, runs the protocol tests, and packages the
    Chrome extension.

.PARAMETER Configuration
    Build configuration. Release by default.

.PARAMETER SkipTests
    Skips the protocol tests. They take about ten seconds, most of it the
    handshake-timeout case.

.EXAMPLE
    .\scripts\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$pluginProject = Join-Path $root 'plugin\src\YouTubeMusicPlugin\YouTubeMusicPlugin.csproj'
$testProject = Join-Path $root 'plugin\test\ProtocolTests\ProtocolTests.csproj'
$extensionDir = Join-Path $root 'extension'
$artifactDir = Join-Path $root 'artifacts'

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

Write-Host "Building the plugin ($Configuration)..." -ForegroundColor Cyan
dotnet build $pluginProject -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed with exit code $LASTEXITCODE." }

if (-not $SkipTests) {
    Write-Host 'Running the protocol tests...' -ForegroundColor Cyan
    dotnet run --project $testProject -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Protocol tests failed with exit code $LASTEXITCODE." }
}

Write-Host 'Packaging the Chrome extension...' -ForegroundColor Cyan
$zipPath = Join-Path $artifactDir 'youtube-music-for-macro-deck-extension.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $extensionDir '*') -DestinationPath $zipPath

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Write-Host ("  Plugin output:    " + (Join-Path $root "plugin\src\YouTubeMusicPlugin\bin\$Configuration"))
Write-Host ("  Extension bundle: " + $zipPath)
Write-Host ''
Write-Host 'Install the plugin with: .\scripts\deploy.ps1'
