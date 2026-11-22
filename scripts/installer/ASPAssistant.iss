; Compile through scripts/publish.ps1 after the self-contained payload has passed validation.
#define AppExe "ASPAssistant.App.exe"
#define ProtocolKey "Software\Classes\aspassistant"

[Setup]
AppId=Moonfair.ASPAssistant
AppName=ASP Assistant
AppVersion={#AppVersion}
AppPublisher=Moonfair
AppPublisherURL=https://github.com/Moonfair/ASP_Assistant
VersionInfoVersion={#GetFileVersion(PayloadDir + "\" + AppExe)}
DefaultDirName={localappdata}\Programs\ASPAssistant
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
AppMutex=ASPAssistant.App.InUse
SetupMutex=ASPAssistant.Setup.InUse
SetupIconFile=..\..\src\ASPAssistant.App\Resources\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern
DisableWelcomePage=no
ChangesAssociations=yes
Compression=lzma2/fast
SolidCompression=yes

[Languages]
; From jrsoftware/issrc tag is-6_7_3, Files/Languages/Unofficial.
Name: "zhcn"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\ASP Assistant"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{userdesktop}\ASP Assistant"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "{#ProtocolKey}"; ValueType: string; ValueData: "URL:ASP Assistant"
Root: HKCU; Subkey: "{#ProtocolKey}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "{#ProtocolKey}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --lineup-uri ""%1"""

[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,ASP Assistant}"; Flags: postinstall nowait skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  { A portable copy can re-register the protocol after this installation. }
  if (CurUninstallStep = usPostUninstall) and
     RegQueryStringValue(HKCU, '{#ProtocolKey}\shell\open\command', '', Command) and
     (CompareText(Command, ExpandConstant('"{app}\{#AppExe}" --lineup-uri "%1"')) = 0) then
    RegDeleteKeyIncludingSubkeys(HKCU, '{#ProtocolKey}');
end;
