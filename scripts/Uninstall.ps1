$ErrorActionPreference='Stop'
Get-AppxPackage -Name 'PaletteDictionary' | ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop }
Write-Host '已卸载扩展。%LOCALAPPDATA%\PaletteDictionary 中的配置和词头缓存予以保留。'
