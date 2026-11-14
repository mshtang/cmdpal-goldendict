param([string]$PublishDirectory)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$PublishDirectory){$PublishDirectory=Join-Path $projectRoot 'artifacts\installer-payload\win-x64'}
& (Join-Path $PSScriptRoot 'Test-Publish.ps1') -PublishDirectory $PublishDirectory
$tests=Join-Path $projectRoot 'artifacts\trimmed-tests\win-x64'
& dotnet publish (Join-Path $projectRoot 'tests\PaletteDictionary.Tests\PaletteDictionary.Tests.csproj') -c Release -r win-x64 --self-contained true -p:PublishTrimmed=true -p:TrimMode=full -p:TrimmerSingleWarn=false -o $tests --nologo -warnaserror
if($LASTEXITCODE -ne 0){throw 'Trimmed core test publication failed.'}
& (Join-Path $tests 'PaletteDictionary.Tests.exe')
if($LASTEXITCODE -ne 0){throw 'Trimmed core tests failed.'}
$config=Join-Path $projectRoot 'artifacts\smoke-matrix\config.json'
& (Join-Path $tests 'PaletteDictionary.Tests.exe') --write-smoke-matrix $config
if($LASTEXITCODE -ne 0){throw 'Smoke matrix generation failed.'}
& dotnet build (Join-Path $projectRoot 'tests\PaletteDictionary.Smoke\PaletteDictionary.Smoke.csproj') -c Release --nologo -warnaserror
if($LASTEXITCODE -ne 0){throw 'Smoke client build failed.'}
$previous=$env:PALETTEDICTIONARY_SMOKE_NO_SHARED_RUNTIME
try {
    $env:PALETTEDICTIONARY_SMOKE_NO_SHARED_RUNTIME='1'
    & (Join-Path $projectRoot 'tests\PaletteDictionary.Smoke\bin\Release\net10.0-windows10.0.26100.0\PaletteDictionary.Smoke.exe') (Join-Path $PublishDirectory 'PaletteDictionary.Extension.exe') $config en
    if($LASTEXITCODE -ne 0){throw 'Published extension COM smoke tests failed.'}
} finally {$env:PALETTEDICTIONARY_SMOKE_NO_SHARED_RUNTIME=$previous}
