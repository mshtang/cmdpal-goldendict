param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [string]$ConfigurationFile)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest=Join-Path $projectRoot "artifacts\$Runtime\AppxManifest.xml"
if (!(Test-Path -LiteralPath $manifest)) { throw '尚未构建。请先运行 scripts\Build.ps1。' }
$manifest= (Resolve-Path -LiteralPath $manifest).Path
if ($ConfigurationFile) {
    $configurationSource=(Resolve-Path -LiteralPath $ConfigurationFile).Path
    $configurationDirectory=Join-Path $env:LOCALAPPDATA 'PaletteDictionary'
    $configurationDestination=Join-Path $configurationDirectory 'config.json'
    [IO.Directory]::CreateDirectory($configurationDirectory) | Out-Null
    if (!(Test-Path -LiteralPath $configurationDestination)) {
        Copy-Item -LiteralPath $configurationSource -Destination $configurationDestination
        Write-Host "已导入初始配置：$configurationDestination"
    } else { Write-Host "已保留现有配置：$configurationDestination" }
}
Write-Host '将为当前 Windows 用户注册本地开发扩展。注册后请保留 artifacts 目录的位置。'
Add-AppxPackage -Register $manifest -ErrorAction Stop
Write-Host '已注册本地词典。请在 Command Palette 中运行 Reload，然后打开「本地词典」。'
