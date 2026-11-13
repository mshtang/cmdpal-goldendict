param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetsPath=Join-Path $projectRoot 'src\PaletteDictionary.Extension\obj\project.assets.json'
$assets=Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$toolsPackage=$assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'Microsoft.Windows.SDK.BuildTools/*' } | Select-Object -First 1
$tool=$null
foreach($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $packagePath=Join-Path $folder $toolsPackage.Value.path
    $tool=Get-ChildItem -Path (Join-Path $packagePath 'bin\*\x64\makepri.exe') -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if($tool){break}
}
if(!$tool){throw 'MakePRI was not found in the restored Windows SDK BuildTools package.'}
$intermediate=Join-Path $projectRoot ('src\PaletteDictionary.Extension\obj\package-resources-'+(Split-Path $PublishDirectory -Leaf))
[IO.Directory]::CreateDirectory($intermediate) | Out-Null
$config=Join-Path $intermediate 'priconfig.xml'
$log=Join-Path $intermediate 'makepri.log'
& $tool.FullName createconfig /cf $config /dq en-US /pv 10.0.0 /o *> $log
if($LASTEXITCODE -ne 0){throw "MakePRI createconfig failed. See $log"}
# Keep both languages in the base package; do not generate language resource packages.
[xml]$xml=Get-Content -LiteralPath $config -Raw
$xml.resources.RemoveChild($xml.resources.packaging) | Out-Null
$xml.Save($config)
& $tool.FullName new /pr (Join-Path $PublishDirectory 'Strings') /cf $config /in PaletteDictionary /of (Join-Path $PublishDirectory 'resources.pri') /o *> $log
if($LASTEXITCODE -ne 0){throw "MakePRI indexing failed. See $log"}
