param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference='Stop'
$Destination=[IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory($Destination) | Out-Null
$installer=Join-Path $Destination 'innosetup-7.1.1-x64.exe'
Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -OutFile $installer
$expected='8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0'
if((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'Inno Setup SHA256 verification failed.'}
$compilerDirectory=Join-Path $Destination 'compiler'
$process=Start-Process -FilePath $installer -ArgumentList @('/PORTABLE=1','/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$compilerDirectory+'"')) -WindowStyle Hidden -PassThru -Wait
if($process.ExitCode -ne 0){throw "Portable Inno Setup extraction failed: $($process.ExitCode)"}
Write-Output (Join-Path $compilerDirectory 'ISCC.exe')
