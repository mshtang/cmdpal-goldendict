param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [ValidateSet('win-x64','win-arm64')][string]$Runtime='win-x64',
    [switch]$FrameworkDependent
)
$ErrorActionPreference='Stop'
$PublishDirectory=(Resolve-Path -LiteralPath $PublishDirectory).Path
[xml]$manifest=Get-Content -LiteralPath (Join-Path $PublishDirectory 'AppxManifest.xml') -Raw
if($manifest.Package.Identity.ProcessorArchitecture -ne $Runtime.Replace('win-','')){throw 'Publish architecture mismatch.'}
$runtimeConfig=Get-Content -LiteralPath (Join-Path $PublishDirectory 'PaletteDictionary.Extension.runtimeconfig.json') -Raw | ConvertFrom-Json
$selfContained=@($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').Count -eq 1
if($selfContained -eq $FrameworkDependent.IsPresent){throw 'Publish runtime mode mismatch.'}
foreach($file in @('PaletteDictionary.Extension.exe','PaletteDictionary.Core.dll','Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll','zh\PaletteDictionary.Core.resources.dll','resources.pri')) {
    if(!(Test-Path -LiteralPath (Join-Path $PublishDirectory $file))){throw "Publish file missing: $file"}
}
$files=@(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File -Force)
if($files | Where-Object {$_.Name -like 'PaletteDictionary.Cli.*' -or $_.Extension -in '.pdb','.mdx','.mdd','.pdhw','.pfx' -or $_.Name -in 'config.local.json','config.json'}){throw 'Publish contains diagnostics, symbols or user data.'}
if($selfContained) {
    foreach($file in @('coreclr.dll','hostfxr.dll','System.Private.CoreLib.dll')) {
        if(!(Test-Path -LiteralPath (Join-Path $PublishDirectory $file))){throw "Self-contained runtime file missing: $file"}
    }
    # Size budgets catch accidental untrimmed publication and dependency growth in CI.
    if((Get-Item -LiteralPath (Join-Path $PublishDirectory 'Microsoft.Windows.SDK.NET.dll')).Length -gt 1MB){throw 'Windows SDK projection exceeds the 1 MiB trimmed size budget.'}
    if(($files | Measure-Object Length -Sum).Sum -gt 35MB){throw 'Self-contained payload exceeds the 35 MiB size budget.'}
}
Write-Host ('PASS publish contents ({0}): {1:N2} MiB' -f $Runtime,(($files | Measure-Object Length -Sum).Sum/1MB))
