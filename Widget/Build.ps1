param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$icon = [System.Drawing.Icon]::new((Join-Path $PSScriptRoot '..\WorkTimer.ico'), 32, 32)
$source = $icon.ToBitmap()
try {
    foreach ($entry in @(@('StoreLogo.png', 50), @('Logo150.png', 150), @('Logo44.png', 44))) {
        $bitmap = [System.Drawing.Bitmap]::new([int]$entry[1], [int]$entry[1])
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height))
            $bitmap.Save((Join-Path $assets $entry[0]), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
} finally {
    $source.Dispose()
    $icon.Dispose()
}

dotnet restore (Join-Path $PSScriptRoot 'WorkTimer.Widget.csproj') --source https://api.nuget.org/v3/index.json -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Widget restore failed.' }
dotnet build (Join-Path $PSScriptRoot 'WorkTimer.Widget.csproj') -c $Configuration -p:Platform=x64 --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Widget build failed.' }
