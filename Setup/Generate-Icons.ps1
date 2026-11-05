$source = Get-Content 'C:\repos\WorkTimer\Setup\IconBuilder.cs' -Raw
Add-Type -TypeDefinition $source -ReferencedAssemblies System.Drawing

$assetsDir = 'C:\repos\WorkTimer\Widget\Assets'
if (-not (Test-Path $assetsDir)) { New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null }

# Render master sizes
$img256 = [IconBuilder]::RenderMaster(256)
$img150 = [IconBuilder]::RenderMaster(150)
$img50  = [IconBuilder]::RenderMaster(50)
$img48  = [IconBuilder]::RenderMaster(48)
$img44  = [IconBuilder]::RenderMaster(44)
$img32  = [IconBuilder]::RenderMaster(32)
$img16  = [IconBuilder]::RenderMaster(16)

# Save App Assets PNGs
$img150.Save((Join-Path $assetsDir 'Logo150.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$img50.Save((Join-Path $assetsDir 'StoreLogo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$img44.Save((Join-Path $assetsDir 'Logo44.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# Save multi-size WorkTimer.ico
$icoPath = 'C:\repos\WorkTimer\WorkTimer.ico'
[IconBuilder]::SaveIco(@($img256, $img48, $img32, $img16), $icoPath)

Write-Host "Icons generated successfully!"

