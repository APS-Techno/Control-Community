#ifndef BuildStage
  #error BuildStage must select the checked release stage.
#endif
#ifndef MyAppVersion
  #error MyAppVersion must come from the built Control executable.
#endif

[Setup]
AppId={{FA8C4B3C-8892-443F-A276-245581B2C41C}
AppName=APS Control Community
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppVerName=APS Control Community {#MyAppVersion}
AppPublisher=APS Technology
DefaultDirName={localappdata}\Programs\APS Control Community
DefaultGroupName=APS Control Community
AllowNoIcons=yes
PrivilegesRequired=lowest
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
LicenseFile={#BuildStage}\LICENSE
SetupIconFile=..\Control\DepositionControl2_Icon.ico
UninstallDisplayIcon={app}\bin\APS.ControlCommunity.exe
OutputBaseFilename=ControlCommunitySetup-{#MyAppVersion}
Compression=lzma2/ultra
SolidCompression=yes
UsePreviousTasks=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#BuildStage}\bin\*"; DestDir: "{app}\bin"; Excludes: "\AgentServer.xml,\RootConfig.xml"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BuildStage}\bin\AgentServer.xml"; DestDir: "{app}\bin"; Flags: onlyifdoesntexist
Source: "{#BuildStage}\bin\RootConfig.xml"; DestDir: "{app}\bin"; Flags: onlyifdoesntexist
Source: "{#BuildStage}\mcp\*"; DestDir: "{app}\mcp"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BuildStage}\Configs\*"; DestDir: "{app}\Configs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BuildStage}\tools\*"; DestDir: "{app}\tools"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#BuildStage}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildStage}\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildStage}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildStage}\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\APS Control Community"; Filename: "{app}\bin\APS.ControlCommunity.exe"; Comment: "APS Control Community - deposition system control"
Name: "{group}\APS Emulator Community"; Filename: "{app}\bin\APS.EmulatorCommunity.exe"; Comment: "APS Emulator Community - simulated deposition hardware"
Name: "{group}\Enable agent access"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -NoExit -File ""{app}\tools\Enable-AgentAccess.ps1"""; Comment: "Turn on the agent server and write the MCP bridge configuration"
Name: "{group}\README"; Filename: "{app}\README.md"
Name: "{group}\{cm:UninstallProgram,APS Control Community}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\APS Control Community"; Filename: "{app}\bin\APS.ControlCommunity.exe"; Tasks: desktopicon
Name: "{autodesktop}\APS Emulator Community"; Filename: "{app}\bin\APS.EmulatorCommunity.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\bin\APS.EmulatorCommunity.exe"; Description: "Start the Emulator"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\mcp\apsmcp.json"
Type: files; Name: "{app}\bin\AgentServer.pfx"

[Code]
var
  BackupRoot: string;

function IsDotNet10DesktopRuntimeInstalled(): Boolean;
var
  Subkey, V: string;
  Names: TArrayOfString;
  i: Integer;
begin
  Result := False;
  Subkey := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  if RegGetValueNames(HKLM32, Subkey, Names) then
  begin
    for i := 0 to GetArrayLength(Names) - 1 do
    begin
      V := Names[i];
      if (Length(V) >= 3) and (Copy(V, 1, 3) = '10.') then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  if not IsDotNet10DesktopRuntimeInstalled() then
  begin
    if SuppressibleMsgBox('APS Control Community requires the .NET 10 Desktop Runtime, which was not found.' + #13#10 + #13#10 +
              'Download it from:' + #13#10 +
              '  https://dotnet.microsoft.com/download/dotnet/10.0' + #13#10 +
              'Pick "Desktop Runtime", not the base "Runtime".' + #13#10 + #13#10 +
              'Continue with installation anyway?',
              mbConfirmation, MB_YESNO, IDYES) = IDYES then
      Result := True
    else
      Result := False;
  end
  else
    Result := True;
end;

procedure CopyTree(const Source, Target: string; const ProfileRoot: Boolean);
var
  Rec: TFindRec;
begin
  ForceDirectories(Target);
  if FindFirst(Source + '\*', Rec) then
  try
    repeat
      if (Rec.Name <> '.') and (Rec.Name <> '..') then
      begin
        if (Rec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if not (ProfileRoot and ((CompareText(Rec.Name, 'AuditLog') = 0) or (CompareText(Rec.Name, 'Snapshots') = 0))) then
            CopyTree(Source + '\' + Rec.Name, Target + '\' + Rec.Name, False);
        end
        else if not CopyFile(Source + '\' + Rec.Name, Target + '\' + Rec.Name, False) then
          RaiseException('Could not back up ' + Source + '\' + Rec.Name);
      end;
    until not FindNext(Rec);
  finally
    FindClose(Rec);
  end;
end;

procedure BackupProfile(const Name: string);
var
  Source: string;
begin
  Source := ExpandConstant('{app}\Configs\') + Name;
  if not DirExists(Source) then
    Exit;
  if BackupRoot = '' then
    BackupRoot := ExpandConstant('{app}\Backups\Configs-') + GetDateTimeString('yyyymmdd-hhnnss', '-', '-');
  CopyTree(Source, BackupRoot + '\' + Name, True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    BackupProfile('Sputter');
    BackupProfile('ADS');
    BackupProfile('Cluster');
    if BackupRoot <> '' then
      Log('Previous profiles were saved to ' + BackupRoot);
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and (BackupRoot <> '') then
  begin
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 +
      'Previous profiles were saved to ' + BackupRoot + '.';
    WizardForm.FinishedLabel.AdjustHeight();
  end;
end;
