param(
    [ValidateSet('win-x64','win-arm64')][string]$Runtime='win-x64',
    [string]$IsccPath=$env:ISCC_PATH,
    [switch]$SkipBuild
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$properties=Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$version=[string]$properties.Project.PropertyGroup.Version
$payload=Join-Path $projectRoot "artifacts\installer-payload\$Runtime"
$output=Join-Path $projectRoot 'artifacts\installers'
if(!$IsccPath) {
    $command=Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if($command){$IsccPath=$command.Source}
    else {
        foreach($directory in @(${env:ProgramFiles(x86)},$env:ProgramFiles)) {
            foreach($major in @(7,6)) {
                $candidate=Join-Path $directory "Inno Setup $major\ISCC.exe"
                if(Test-Path -LiteralPath $candidate){$IsccPath=$candidate;break}
            }
            if($IsccPath){break}
        }
    }
}
if(!$IsccPath -or !(Test-Path -LiteralPath $IsccPath)){throw 'Install Inno Setup 6/7 or pass -IsccPath to ISCC.exe.'}
if(!$SkipBuild){& (Join-Path $PSScriptRoot 'Build.ps1') -Runtime $Runtime -OutputDirectory $payload}
[xml]$manifest=Get-Content -LiteralPath (Join-Path $payload 'AppxManifest.xml') -Raw
if($manifest.Package.Identity.Version -ne "$version.0"){throw 'Payload version does not match Directory.Build.props.'}
$architecture=$Runtime.Replace('win-','')
if($manifest.Package.Identity.ProcessorArchitecture -ne $architecture){throw 'Payload architecture does not match requested runtime.'}
[IO.Directory]::CreateDirectory($output) | Out-Null
& $IsccPath "/DAppVersion=$version" "/DPayloadDirectory=$payload" "/DInstallerDirectory=$output" "/DArchitecture=$architecture" (Join-Path $projectRoot 'installer\Setup.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$installer=Join-Path $output "CmdPal-GoldenDict-$version-dev-Setup-$architecture.exe"
if(!(Test-Path -LiteralPath $installer)){throw 'Installer output was not created.'}
$hash=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($installer+'.sha256',$hash+'  '+[IO.Path]::GetFileName($installer)+[Environment]::NewLine)
Write-Host "Development installer: $installer"
