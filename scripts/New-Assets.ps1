param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\PaletteDictionary.Extension\Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
foreach ($asset in @(@('Square44x44Logo.png',44),@('Square150x150Logo.png',150),@('StoreLogo.png',50))) {
    $size=[int]$asset[1]
    $bitmap=[Drawing.Bitmap]::new($size,$size)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $pen=[Drawing.Pen]::new([Drawing.Color]::White,[single]($size/24))
    try {
        $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::FromArgb(28,75,99))
        $graphics.DrawRectangle($pen,[single]($size*.20),[single]($size*.24),[single]($size*.60),[single]($size*.52))
        $graphics.DrawLine($pen,[single]($size*.50),[single]($size*.24),[single]($size*.50),[single]($size*.76))
        $bitmap.Save((Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $asset[0]),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
