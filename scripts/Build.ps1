param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [switch]$FrameworkDependent, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishDirectory = if($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot "artifacts\$Runtime" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='1'
$env:MSBuildEnableWorkloadResolver='false'
& (Join-Path $PSScriptRoot 'New-Assets.ps1')
$selfContained = if($FrameworkDependent) { 'false' } else { 'true' }
& dotnet publish (Join-Path $projectRoot 'src\PaletteDictionary.Extension\PaletteDictionary.Extension.csproj') -c Release -r $Runtime --self-contained $selfContained -o $publishDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw '扩展构建失败。' }
& dotnet publish (Join-Path $projectRoot 'src\PaletteDictionary.Cli\PaletteDictionary.Cli.csproj') -c Release -r $Runtime --self-contained $selfContained -o $publishDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw '诊断工具构建失败。' }
$manifest = Join-Path $publishDirectory 'AppxManifest.xml'
[xml]$xml = Get-Content -Raw -LiteralPath $manifest
$xml.Package.Identity.ProcessorArchitecture = $Runtime.Replace('win-','')
$xml.Save($manifest)
& (Join-Path $PSScriptRoot 'New-PackageResources.ps1') -PublishDirectory $publishDirectory
[IO.Directory]::CreateDirectory((Join-Path $publishDirectory 'Public')) | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'config.example.json') -Destination $publishDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $publishDirectory -Recurse -Force
Write-Host "构建完成：$publishDirectory"
Write-Host '安装到当前用户：.\scripts\Install.ps1'
