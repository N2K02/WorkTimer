param(
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

Write-Host "Building WorkTimer Tracker ($Configuration)..." -ForegroundColor Cyan
dotnet publish (Join-Path $repoRoot 'WorkTimer.csproj') -c $Configuration

Write-Host "Building WorkTimer Widget ($Configuration)..." -ForegroundColor Cyan
& (Join-Path $repoRoot 'Widget\Build.ps1') -Configuration $Configuration

Write-Host "Building MSI Installer (Version $Version)..." -ForegroundColor Cyan
$setupDir = Join-Path $repoRoot 'Setup'
$distDir = Join-Path $repoRoot 'dist'
$outputMsi = Join-Path $distDir "WorkTimer-$Version.msi"

Push-Location $setupDir
try {
    wix build -arch x64 -ext WixToolset.UI.wixext Package.wxs -out $outputMsi
}
finally {
    Pop-Location
}

if (-not (Test-Path $outputMsi)) {
    throw "MSI build failed. Output file not found: $outputMsi"
}

$file = Get-Item $outputMsi
$sizeMb = [math]::Round($file.Length / 1MB, 2)
Write-Host "MSI package created successfully: $outputMsi ($sizeMb MB)" -ForegroundColor Green

