#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PayloadDirectory
  #error PayloadDirectory is required
#endif
#ifndef InstallerDirectory
  #error InstallerDirectory is required
#endif
#ifndef Architecture
  #define Architecture "x64"
#endif

[Setup]
AppId={{EA5AF471-D124-4AA9-9CB5-C6077097B5BD}
AppName=CmdPal GoldenDict
AppVersion={#AppVersion}
AppVerName=CmdPal GoldenDict {#AppVersion} (Development)
AppPublisher=mshtang
AppPublisherURL=https://github.com/mshtang/cmdpal-goldendict
AppSupportURL=https://github.com/mshtang/cmdpal-goldendict/issues
AppUpdatesURL=https://github.com/mshtang/cmdpal-goldendict/releases
DefaultDirName={localappdata}\Programs\CmdPal GoldenDict
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
MinVersion=10.0.22000
#if Architecture == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
OutputDir={#InstallerDirectory}
OutputBaseFilename=CmdPal-GoldenDict-{#AppVersion}-dev-Setup-{#Architecture}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=CmdPal GoldenDict (Development installer)
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoDescription=CmdPal GoldenDict development installer
VersionInfoProductName=CmdPal GoldenDict
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[CustomMessages]
english.DevelopmentNotice=Development build — Windows Developer Mode is required. This installer does not enable it. Configuration and headword caches are kept when upgrading or uninstalling. After installation, run Reload in Command Palette and open Local Dictionary Settings.
chinesesimp.DevelopmentNotice=开发版：需要已开启 Windows 开发者模式。安装器不会开启此设置。升级和卸载会保留用户配置及词头缓存。安装后请在 Command Palette 中运行 Reload，再打开「本地词典设置」。
english.DeveloperModeRequired=This development build requires Windows Developer Mode. Enable it manually in Windows Settings, then run Setup again.
chinesesimp.DeveloperModeRequired=此开发版需要 Windows 开发者模式。请自行在 Windows 设置中开启，再运行安装器。
english.PrerequisiteFailed=Installation checks failed. Details are in:
chinesesimp.PrerequisiteFailed=安装前检查失败，详情见：
english.RegistrationFailed=Extension registration failed. Details are in:
chinesesimp.RegistrationFailed=扩展注册失败，详情见：
english.UnregistrationFailed=Extension unregistration failed. Setup will keep the installed files. Details are in:
chinesesimp.UnregistrationFailed=扩展注销失败，安装文件将予以保留。详情见：

[Messages]
english.FinishedLabel=CmdPal GoldenDict is installed. Run Reload in Command Palette, then open Local Dictionary Settings to configure your dictionaries.
chinesesimp.FinishedLabel=CmdPal GoldenDict 已安装。请在 Command Palette 中运行 Reload，再打开「本地词典设置」配置词典。

[Files]
Source: "{#PayloadDirectory}\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,PaletteDictionary.Cli.*,config.local.json,*.mdx,*.mdd,*.pdhw,*.pfx"
Source: "Manage-Extension.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Manage-Extension.ps1"; DestName: "CmdPal-GoldenDict-preflight.ps1"; Flags: dontcopy

[Code]
var
  RegistrationFailed: Boolean;

function PowerShellPath: String;
begin
  Result := ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe');
end;

function HelperArguments(Action, Script: String): String;
begin
  Result := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + Script +
    '" -Action ' + Action + ' -InstallRoot "' + ExpandConstant('{app}') +
    '" -Version "{#AppVersion}.0" -LogPath "' + ExpandConstant('{localappdata}\CmdPalGoldenDict\Logs\installer.log') + '"';
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption := CustomMessage('DevelopmentNotice');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  DeveloperMode: Cardinal;
  Code: Integer;
begin
  Result := '';
  if not RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock',
    'AllowDevelopmentWithoutDevLicense', DeveloperMode) or (DeveloperMode <> 1) then
  begin
    Result := CustomMessage('DeveloperModeRequired');
    Exit;
  end;
  ExtractTemporaryFile('CmdPal-GoldenDict-preflight.ps1');
  if not Exec(PowerShellPath, HelperArguments('Check', ExpandConstant('{tmp}\CmdPal-GoldenDict-preflight.ps1')),
    '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Result := CustomMessage('PrerequisiteFailed') + #13#10 + ExpandConstant('{localappdata}\CmdPalGoldenDict\Logs\installer.log');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    RegistrationFailed := True;
    if not Exec(PowerShellPath, HelperArguments('Install', ExpandConstant('{app}\Manage-Extension.ps1')),
      '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException(CustomMessage('RegistrationFailed') + #13#10 + ExpandConstant('{localappdata}\CmdPalGoldenDict\Logs\installer.log'));
    RegistrationFailed := False;
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if RegistrationFailed then Result := 1 else Result := 0;
end;

function InitializeUninstall: Boolean;
var
  Code: Integer;
begin
  Result := Exec(PowerShellPath, HelperArguments('Uninstall', ExpandConstant('{app}\Manage-Extension.ps1')),
    '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if not Result then
    MsgBox(CustomMessage('UnregistrationFailed') + #13#10 + ExpandConstant('{localappdata}\CmdPalGoldenDict\Logs\installer.log'), mbError, MB_OK);
end;
