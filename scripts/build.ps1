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

Write-Host 'Packaging the Macro Deck plugin...' -ForegroundColor Cyan
$pluginOutput = Join-Path $root "plugin\src\YouTubeMusicPlugin\bin\$Configuration"

# Only the files Macro Deck loads. The build folder also holds a
# runtimeconfig.json that Macro Deck neither reads nor wants.
$pluginFiles = @(
    'YouTubeMusicPlugin.dll',
    'YouTubeMusicPlugin.deps.json',
    'Fleck.dll',
    'ExtensionManifest.json',
    'ExtensionIcon.png'
) | ForEach-Object { Join-Path $pluginOutput $_ }

$missing = $pluginFiles | Where-Object { -not (Test-Path $_) }
if ($missing) { throw "Missing build output: $($missing -join ', ')" }

$manifest = Get-Content (Join-Path $pluginOutput 'ExtensionManifest.json') -Raw | ConvertFrom-Json
$pluginZip = Join-Path $artifactDir "KeystoneDigital.YouTubeMusic-$($manifest.version).zip"
if (Test-Path $pluginZip) { Remove-Item $pluginZip -Force }
Compress-Archive -Path $pluginFiles -DestinationPath $pluginZip

Write-Host 'Packaging the icon pack...' -ForegroundColor Cyan
$iconPackDir = Join-Path $root 'iconpack\Goonsly.YoutubeIcons'
$iconManifest = Get-Content (Join-Path $iconPackDir 'ExtensionManifest.json') -Raw | ConvertFrom-Json
$iconZip = Join-Path $artifactDir "$($iconManifest.packageId)-$($iconManifest.version).zip"
if (Test-Path $iconZip) { Remove-Item $iconZip -Force }
Compress-Archive -Path (Join-Path $iconPackDir '*') -DestinationPath $iconZip

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Write-Host ("  Plugin output:    " + $pluginOutput)
Write-Host ("  Plugin bundle:    " + $pluginZip)
Write-Host ("  Extension bundle: " + $zipPath)
Write-Host ("  Icon pack bundle: " + $iconZip)
Write-Host ''
Write-Host 'Install the plugin with: .\scripts\deploy.ps1'
