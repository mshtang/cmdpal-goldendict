param([string]$RealMdx)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='1'
$env:MSBuildEnableWorkloadResolver='false'
$arguments=@('run','--project',(Join-Path $projectRoot 'tests\PaletteDictionary.Tests\PaletteDictionary.Tests.csproj'),'--configuration','Release','--')
if ($RealMdx) { $arguments += (Resolve-Path -LiteralPath $RealMdx).Path }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
