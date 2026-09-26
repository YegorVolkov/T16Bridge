#ifndef RepoRoot
  #error RepoRoot must be passed with /DRepoRoot=...
#endif
#ifndef OutputDir
  #define OutputDir RepoRoot + "\artifacts\installer"
#endif

#define AppName "T16Bridge"
#define AppVersion "0.1.0"
#define AppPublisher "T16Bridge contributors"
#define AppExe "T16Bridge.exe"

[Setup]
AppId={{E6F6505E-BE18-4B63-9E57-A9B6AF10C6D7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\T16Bridge
DefaultGroupName=T16Bridge
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
DisableFinishedPage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=T16BridgeSetup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=no

[Files]
Source: "{#RepoRoot}\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\artifacts\deps\HidHideSetup.exe"; DestDir: "{tmp}\T16BridgeDeps"; Flags: ignoreversion deleteafterinstall skipifsourcedoesntexist
Source: "{#RepoRoot}\artifacts\deps\HidHideSetup.msi"; DestDir: "{tmp}\T16BridgeDeps"; Flags: ignoreversion deleteafterinstall skipifsourcedoesntexist
Source: "{#RepoRoot}\scripts\install-hidhide.ps1"; DestDir: "{tmp}\T16BridgeDeps"; Flags: ignoreversion deleteafterinstall
Source: "{#RepoRoot}\artifacts\licenses\HIDMaestro-LICENSE.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#RepoRoot}\artifacts\licenses\HidHide-LICENSE.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}\licenses"; DestName: "T16Bridge-LICENSE.txt"; Flags: ignoreversion
Source: "{#RepoRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\T16Bridge"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\T16Bridge"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch T16Bridge"; Flags: postinstall nowait skipifsilent shellexec

[Code]
var
  ConsentCheck: TNewCheckBox;
  HIDMaestroLink: TNewStaticText;
  HidHideLink: TNewStaticText;
  InfoLabel: TNewStaticText;
  RestartRequiredByHidHide: Boolean;
  ResultCode: Integer;

procedure OpenURL(Sender: TObject);
begin
  if Sender = HIDMaestroLink then
    ShellExec('open', 'https://github.com/hifihedgehog/HIDMaestro', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode)
  else if Sender = HidHideLink then
    ShellExec('open', 'https://github.com/nefarius/HidHide', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure ConsentChanged(Sender: TObject);
begin
  WizardForm.NextButton.Enabled := ConsentCheck.Checked;
end;

procedure InitializeWizard;
begin
  WizardForm.Caption := 'T16Bridge Setup';
  WizardForm.ReadyMemo.Visible := False;

  InfoLabel := TNewStaticText.Create(WizardForm.ReadyPage);
  InfoLabel.Parent := WizardForm.ReadyPage.Surface;
  InfoLabel.Left := ScaleX(0);
  InfoLabel.Top := ScaleY(0);
  InfoLabel.Width := WizardForm.ReadyPage.SurfaceWidth;
  InfoLabel.Height := ScaleY(220);
  InfoLabel.AutoSize := False;
  InfoLabel.WordWrap := True;
  InfoLabel.Caption :=
    'The following REQUIRED components will be installed:' + #13#10 + #13#10 +
    '  ✓ T16Bridge' + #13#10 +
    '     Dual T.16000M bridge and graphical sensitivity curve editor.' + #13#10 + #13#10 +
    '  ✓ HIDMaestro v1.9.0' + #13#10 +
    '     Virtual HID runtime used by T16Bridge. MIT License.' + #13#10 + #13#10 +
    '  ✓ HidHide v1.5.230' + #13#10 +
    '     Required to hide the physical T.16000M devices from games. MIT License.' + #13#10 +
    '     After LEFT/RIGHT assignment, T16Bridge will automatically allow-list itself,' + #13#10 +
    '     hide both selected physical T.16000M devices, and enable device hiding.' + #13#10 + #13#10 +
    '  ✓ .NET runtime for T16Bridge' + #13#10 +
    '     Bundled self-contained; no separate .NET installation is required.' + #13#10 + #13#10 +
    'Administrator privileges are required. A Windows restart may be required by HidHide.';

  HIDMaestroLink := TNewStaticText.Create(WizardForm.ReadyPage);
  HIDMaestroLink.Parent := WizardForm.ReadyPage.Surface;
  HIDMaestroLink.Left := ScaleX(0);
  HIDMaestroLink.Top := ScaleY(225);
  HIDMaestroLink.Caption := 'HIDMaestro project / license';
  HIDMaestroLink.Font.Color := clBlue;
  HIDMaestroLink.Font.Style := [fsUnderline];
  HIDMaestroLink.Cursor := crHand;
  HIDMaestroLink.OnClick := @OpenURL;

  HidHideLink := TNewStaticText.Create(WizardForm.ReadyPage);
  HidHideLink.Parent := WizardForm.ReadyPage.Surface;
  HidHideLink.Left := ScaleX(210);
  HidHideLink.Top := ScaleY(225);
  HidHideLink.Caption := 'HidHide project / license';
  HidHideLink.Font.Color := clBlue;
  HidHideLink.Font.Style := [fsUnderline];
  HidHideLink.Cursor := crHand;
  HidHideLink.OnClick := @OpenURL;

  ConsentCheck := TNewCheckBox.Create(WizardForm.ReadyPage);
  ConsentCheck.Parent := WizardForm.ReadyPage.Surface;
  ConsentCheck.Left := ScaleX(0);
  ConsentCheck.Top := ScaleY(260);
  ConsentCheck.Width := WizardForm.ReadyPage.SurfaceWidth;
  ConsentCheck.Caption := 'I agree to install all components listed above.';
  ConsentCheck.Checked := False;
  ConsentCheck.OnClick := @ConsentChanged;

  WizardForm.NextButton.Caption := 'Agree && Install';
  WizardForm.NextButton.Enabled := False;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = wpSelectDir) or
     (PageID = wpSelectProgramGroup) or
     (PageID = wpSelectTasks) then
    Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then
  begin
    WizardForm.NextButton.Caption := 'Agree && Install';
    WizardForm.NextButton.Enabled := ConsentCheck.Checked;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  ScriptPath: String;
  PayloadDir: String;
begin
  if CurStep = ssPostInstall then
  begin
    PayloadDir := ExpandConstant('{tmp}\T16BridgeDeps');
    ScriptPath := PayloadDir + '\install-hidhide.ps1';

    if not Exec(
      'powershell.exe',
      '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" -PayloadDirectory "' + PayloadDir + '"',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ExitCode) then
    begin
      RaiseException('Failed to launch the HidHide installer.');
    end;

    if (ExitCode = 3010) or (ExitCode = 1641) then
      RestartRequiredByHidHide := True
    else if ExitCode <> 0 then
      RaiseException('HidHide installation failed with exit code ' + IntToStr(ExitCode) + '.');
  end;
end;

function NeedRestart(): Boolean;
begin
  Result := RestartRequiredByHidHide;
end;
