param(
    [string]$Configuration = 'Release',
    [string]$Version,
    [ValidateSet('None', 'Patch', 'Minor', 'Major')]
    [string]$Bump = 'None'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$mainCsProj = Join-Path $repoRoot 'WorkTimer.csproj'
$widgetCsProj = Join-Path $repoRoot 'Widget\WorkTimer.Widget.csproj'
$manifestXml = Join-Path $repoRoot 'Widget\Package.appxmanifest'

# 1. Resolve current version from WorkTimer.csproj
$csprojContent = Get-Content $mainCsProj -Raw
if ($csprojContent -match '<Version>(\d+\.\d+\.\d+)</Version>') {
    $currentVersion = $matches[1]
} else {
    $currentVersion = '1.0.0'
}

# 2. Determine target version
if ($Version) {
    $targetVersion = $Version
} elseif ($Bump -ne 'None') {
    $parts = $currentVersion.Split('.') | ForEach-Object { [int]$_ }
    switch ($Bump) {
        'Major' { $parts[0]++; $parts[1] = 0; $parts[2] = 0 }
        'Minor' { $parts[1]++; $parts[2] = 0 }
        'Patch' { $parts[2]++ }
    }
    $targetVersion = $parts -join '.'
} else {
    $targetVersion = $currentVersion
}

# 3. Synchronize version in projects & manifest if changed
if ($targetVersion -ne $currentVersion) {
    Write-Host "Bumping version: $currentVersion -> $targetVersion" -ForegroundColor Yellow
    # Update WorkTimer.csproj
    $updatedCsProj = $csprojContent -replace '<Version>\d+\.\d+\.\d+</Version>', "<Version>$targetVersion</Version>"
    Set-Content -Path $mainCsProj -Value $updatedCsProj -NoNewline

    # Update Widget/WorkTimer.Widget.csproj
    if (Test-Path $widgetCsProj) {
        $widgetContent = (Get-Content $widgetCsProj -Raw) -replace '<WidBarPluginVersion>\d+\.\d+\.\d+</WidBarPluginVersion>', "<WidBarPluginVersion>$targetVersion</WidBarPluginVersion>"
        Set-Content -Path $widgetCsProj -Value $widgetContent -NoNewline
    }

    # Update Widget/Package.appxmanifest (Appx requires 4 digits e.g. 1.0.1.0)
    if (Test-Path $manifestXml) {
        $manifestContent = (Get-Content $manifestXml -Raw) -replace 'Version="\d+\.\d+\.\d+\.\d+"', "Version=""$targetVersion.0"""
        Set-Content -Path $manifestXml -Value $manifestContent -NoNewline
    }
}

Write-Host "Building WorkTimer Tracker ($Configuration, v$targetVersion, Self-Contained)..." -ForegroundColor Cyan
dotnet publish $mainCsProj -c $Configuration -r win-x64 --self-contained true -p:Version=$targetVersion

Write-Host "Building WorkTimer Widget ($Configuration, v$targetVersion)..." -ForegroundColor Cyan
& (Join-Path $repoRoot 'Widget\Build.ps1') -Configuration $Configuration -Version $targetVersion

Write-Host "Building MSI Installer (Version $targetVersion)..." -ForegroundColor Cyan
$setupDir = Join-Path $repoRoot 'Setup'
$distDir = Join-Path $repoRoot 'dist'
$outputMsi = Join-Path $distDir "WorkTimer-$targetVersion.msi"

Push-Location $setupDir
try {
    wix build -arch x64 -ext WixToolset.UI.wixext -d Version=$targetVersion Package.wxs -out $outputMsi
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
