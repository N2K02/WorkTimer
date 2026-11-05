param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'

$assets = Join-Path $PSScriptRoot 'Assets'
$generator = Join-Path $PSScriptRoot '..\Setup\Generate-Icons.ps1'
if (-not (Test-Path (Join-Path $assets 'StoreLogo.png')) -and (Test-Path $generator)) {
    & $generator
}

dotnet restore (Join-Path $PSScriptRoot 'WorkTimer.Widget.csproj') --source https://api.nuget.org/v3/index.json -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Widget restore failed.' }
dotnet build (Join-Path $PSScriptRoot 'WorkTimer.Widget.csproj') -c $Configuration -p:Platform=x64 --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Widget build failed.' }
