param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'

$developerMode = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
if ($developerMode.AllowDevelopmentWithoutDevLicense -ne 1) {
    throw 'Enable Windows Developer Mode before registering this local widget build.'
}
$manifest = Join-Path $PSScriptRoot "bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml"
if (-not (Test-Path -LiteralPath $manifest)) { throw 'Run Widget\Build.ps1 first.' }

Add-AppxPackage -Register $manifest
Write-Output 'WorkTimer widget registered. Open WidBar > Layout and add WorkTimer to a free taskbar space.'
