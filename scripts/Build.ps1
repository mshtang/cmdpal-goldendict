param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [switch]$FrameworkDependent, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishDirectory = if($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot "artifacts\$Runtime" }
if($publishDirectory -eq $projectRoot -or $projectRoot.StartsWith($publishDirectory.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'OutputDirectory must not contain the project.'}
# Only replace an empty directory or a recognizable publish output, never source or user data.
if(Test-Path -LiteralPath $publishDirectory) {
    if(!(Test-Path -LiteralPath $publishDirectory -PathType Container)){throw 'OutputDirectory must be a directory.'}
    if((Get-Item -LiteralPath $publishDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'OutputDirectory must not be a link.'}
    if(Get-ChildItem -LiteralPath $publishDirectory -Force | Select-Object -First 1) {
        $oldManifest=Join-Path $publishDirectory 'AppxManifest.xml'
        if(!(Test-Path -LiteralPath $oldManifest) -or !(Test-Path -LiteralPath (Join-Path $publishDirectory 'PaletteDictionary.Extension.runtimeconfig.json'))){throw 'OutputDirectory is not an extension publish directory. Choose an empty directory.'}
        [xml]$oldPackage=Get-Content -LiteralPath $oldManifest -Raw
        if($oldPackage.Package.Identity.Name -ne 'PaletteDictionary'){throw 'OutputDirectory belongs to a different package.'}
        if(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Force | Where-Object {$_.Extension -in '.mdx','.mdd','.pdhw','.pfx' -or $_.Name -in 'config.local.json','config.json'}){throw 'OutputDirectory contains user data; move it before rebuilding.'}
    }
}
# Publish into a fresh staging directory so failed builds leave the previous output intact.
$stagingRoot=Join-Path $projectRoot 'artifacts'
$stagingDirectory=Join-Path $stagingRoot ('.publish-'+[Guid]::NewGuid().ToString('N'))
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='1'
$env:MSBuildEnableWorkloadResolver='false'
& (Join-Path $PSScriptRoot 'New-Assets.ps1')
$selfContained = if($FrameworkDependent) { 'false' } else { 'true' }
try {
    $trimmed = if($FrameworkDependent) { 'false' } else { 'true' }
    & dotnet publish (Join-Path $projectRoot 'src\PaletteDictionary.Extension\PaletteDictionary.Extension.csproj') -c Release -r $Runtime --self-contained $selfContained "-p:PublishTrimmed=$trimmed" -p:CopyOutputSymbolsToPublishDirectory=false -o $stagingDirectory --nologo -warnaserror
    if ($LASTEXITCODE -ne 0) { throw '扩展构建失败。' }
    $manifest = Join-Path $stagingDirectory 'AppxManifest.xml'
    [xml]$xml = Get-Content -Raw -LiteralPath $manifest
    $xml.Package.Identity.ProcessorArchitecture = $Runtime.Replace('win-','')
    $xml.Save($manifest)
    & (Join-Path $PSScriptRoot 'New-PackageResources.ps1') -PublishDirectory $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'config.example.json') -Destination $stagingDirectory -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $stagingDirectory -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $stagingDirectory -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $stagingDirectory -Recurse -Force
    # Dependencies may publish symbols even when this project's symbols are disabled.
    Get-ChildItem -LiteralPath $stagingDirectory -Recurse -File -Force -Filter '*.pdb' | ForEach-Object {Remove-Item -LiteralPath $_.FullName -Force}
    & (Join-Path $PSScriptRoot 'Test-Publish.ps1') -PublishDirectory $stagingDirectory -Runtime $Runtime -FrameworkDependent:$FrameworkDependent
    if(Test-Path -LiteralPath $publishDirectory){Remove-Item -LiteralPath $publishDirectory -Recurse -Force}
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($publishDirectory)) | Out-Null
    Move-Item -LiteralPath $stagingDirectory -Destination $publishDirectory
} finally {
    if(![IO.Path]::GetFullPath($stagingDirectory).StartsWith($stagingRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe staging directory.'}
    if(Test-Path -LiteralPath $stagingDirectory){Remove-Item -LiteralPath $stagingDirectory -Recurse -Force}
}
Write-Host "构建完成：$publishDirectory"
Write-Host '安装到当前用户：.\scripts\Install.ps1'
