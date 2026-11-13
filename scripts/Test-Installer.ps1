param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Installer=(Resolve-Path -LiteralPath $Installer).Path
$testRoot=Join-Path $projectRoot 'artifacts\installer-tests\安装 & spaces [1]'
if(![IO.Path]::GetFullPath($testRoot).StartsWith((Join-Path $projectRoot 'artifacts\installer-tests\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe installer test directory.'}
$previous=@(Get-AppxPackage -Name PaletteDictionary | Where-Object Publisher -eq 'CN=PaletteDictionary')
if($previous | Where-Object {$_.InstallLocation -eq (Join-Path $testRoot 'App')}){throw 'The installer test directory is already registered. Uninstall that test copy first.'}
$configuration=Join-Path $env:LOCALAPPDATA 'PaletteDictionary\config.json'
$hash=if(Test-Path -LiteralPath $configuration){(Get-FileHash -LiteralPath $configuration -Algorithm SHA256).Hash}else{$null}
$cache=Join-Path $env:LOCALAPPDATA 'PaletteDictionary\cache'
$cacheBefore=@{}
if(Test-Path -LiteralPath $cache){Get-ChildItem -LiteralPath $cache -File | ForEach-Object {$cacheBefore[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}}
$smoke=Join-Path $projectRoot 'tests\PaletteDictionary.Smoke\bin\Release\net10.0-windows10.0.26100.0\PaletteDictionary.Smoke.exe'
function Assert-Preserved {
    if($hash -and (!(Test-Path -LiteralPath $configuration) -or (Get-FileHash -LiteralPath $configuration -Algorithm SHA256).Hash -ne $hash)){throw 'User configuration changed during installer test.'}
    foreach($file in $cacheBefore.Keys){if(!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $cacheBefore[$file]){throw "Existing cache changed: $file"}}
}
try {
    foreach($pass in @(1,2)) {
        $log=Join-Path $projectRoot "artifacts\installer-test-$pass.log"
        $process=Start-Process -FilePath $Installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS',('/DIR="'+$testRoot+'"'),('/LOG="'+$log+'"')) -WindowStyle Hidden -PassThru -Wait
        if($process.ExitCode -ne 0){throw "Installer failed ($($process.ExitCode)); see $log"}
        $package=@(Get-AppxPackage -Name PaletteDictionary | Where-Object {$_.InstallLocation -eq (Join-Path $testRoot 'App')})
        if($package.Count -ne 1){throw 'Installer returned success without the expected package registration.'}
        & $smoke --installed
        if($LASTEXITCODE -ne 0){throw 'Installed extension activation failed.'}
        Assert-Preserved
        Write-Host "PASS installer pass $pass`: registration, real COM activation, configuration and cache preservation"
    }
    $nativePowerShell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $helper=Join-Path $testRoot 'Manage-Extension.ps1'
    & $nativePowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $helper -Action Check -InstallRoot $testRoot -Version 0.0.0.0
    if($LASTEXITCODE -eq 0){throw 'Downgrade preflight did not reject an older version.'}
    & $nativePowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $helper -Action Uninstall -InstallRoot (Join-Path $projectRoot 'artifacts\installer-tests\different copy')
    if($LASTEXITCODE -ne 0){throw 'Uninstaller ownership test failed.'}
    if(!(Get-AppxPackage -Name PaletteDictionary | Where-Object {$_.InstallLocation -eq (Join-Path $testRoot 'App')})){throw 'An unrelated uninstaller removed the registered extension.'}
    Write-Host 'PASS downgrade rejection and uninstaller registration ownership'
}
finally {
    $uninstaller=Join-Path $testRoot 'unins000.exe'
    if(Test-Path -LiteralPath $uninstaller) {
        $process=Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru -Wait
        if($process.ExitCode -ne 0){throw "Uninstaller failed: $($process.ExitCode)"}
        if(Get-AppxPackage -Name PaletteDictionary | Where-Object {$_.InstallLocation -eq (Join-Path $testRoot 'App')}){throw 'Uninstaller left the test extension registered.'}
        Assert-Preserved
        Write-Host 'PASS uninstall: registration removed, user configuration and existing caches kept'
    }
    foreach($package in $previous) {
        $manifest=Join-Path $package.InstallLocation 'AppxManifest.xml'
        if(Test-Path -LiteralPath $manifest){Add-AppxPackage -Register ([Management.Automation.WildcardPattern]::Escape($manifest)) -ForceApplicationShutdown -ErrorAction Stop}
    }
}
