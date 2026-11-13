param(
    [Parameter(Mandatory)][ValidateSet('Check','Install','Uninstall')][string]$Action,
    [Parameter(Mandatory)][string]$InstallRoot,
    [version]$Version = '0.0.0.0',
    [string]$LogPath = (Join-Path $env:TEMP 'CmdPal-GoldenDict-install.log')
)
$ErrorActionPreference='Stop'
$identityName='PaletteDictionary'
$publisher='CN=PaletteDictionary'
$applicationDirectory=Join-Path ([IO.Path]::GetFullPath($InstallRoot)) 'App'
$manifest=Join-Path $applicationDirectory 'AppxManifest.xml'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($LogPath))) | Out-Null

function Get-OwnedPackages {
    @(Get-AppxPackage -Name $identityName -ErrorAction Stop | Where-Object { $_.Publisher -eq $publisher })
}
function Stop-OwnedServer([string[]]$Directories) {
    foreach($process in Get-Process -Name PaletteDictionary.Extension -ErrorAction SilentlyContinue) {
        if($process.Path -and $Directories -contains [IO.Path]::GetDirectoryName($process.Path)) {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            if(!$process.WaitForExit(5000)){throw 'The previous extension process did not exit.'}
        }
    }
}
function Assert-Prerequisites {
    $mode=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
    if(!$mode -or $mode.AllowDevelopmentWithoutDevLicense -ne 1) {
        throw 'This development build requires Windows Developer Mode. Enable it manually in Windows Settings, then run Setup again.'
    }
    foreach($package in Get-OwnedPackages) {
        if([version]$package.Version -gt $Version) { throw "A newer version ($($package.Version)) is already registered. Downgrades are not supported." }
    }
}

try {
    if($Action -eq 'Check') {
        Assert-Prerequisites
        # Run before Inno Setup checks file locks and starts copying an upgrade.
        Stop-OwnedServer (@($applicationDirectory)+@(Get-OwnedPackages | ForEach-Object InstallLocation))
    }
    elseif($Action -eq 'Install') {
        Assert-Prerequisites
        [xml]$xml=Get-Content -LiteralPath $manifest -Raw
        if($xml.Package.Identity.Name -ne $identityName -or $xml.Package.Identity.Publisher -ne $publisher -or [version]$xml.Package.Identity.Version -ne $Version) {
            throw 'The extension manifest identity or version does not match the installer.'
        }
        $registeredBefore=@(Get-OwnedPackages)
        Stop-OwnedServer (@($applicationDirectory)+@($registeredBefore | ForEach-Object InstallLocation))
        # Appx cmdlets resolve wildcard paths; escape literal square brackets.
        Add-AppxPackage -Register ([Management.Automation.WildcardPattern]::Escape($manifest)) -ForceApplicationShutdown -ErrorAction Stop
        $registered=@(Get-OwnedPackages | Where-Object { [IO.Path]::GetFullPath($_.InstallLocation).TrimEnd('\') -eq $applicationDirectory.TrimEnd('\') -and [version]$_.Version -eq $Version })
        if($registered.Count -ne 1) { throw 'Windows did not register the expected extension at the installation directory.' }
    }
    else {
        # Only unregister the copy owned by this installation, never a newer copy elsewhere.
        foreach($package in Get-OwnedPackages) {
            if([IO.Path]::GetFullPath($package.InstallLocation).TrimEnd('\') -eq $applicationDirectory.TrimEnd('\')) {
                Stop-OwnedServer @($applicationDirectory)
                Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
            }
        }
    }
    [IO.File]::WriteAllText($LogPath, "$Action completed successfully.`r`n")
    exit 0
}
catch {
    [IO.File]::WriteAllText($LogPath, ($_ | Out-String))
    exit 1
}
