; Inno Setup script for Agent Subscription Tracker.
; Compiled locally by installer/build-release.ps1 — no CI involved.
; Per-user install (no elevation), matching the app's standard-user security posture.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef PublishDir
  #define PublishDir "..\src\AgentSubscriptionTracker.App\bin\Release\net10.0-windows\win-x64\publish"
#endif

#define AppName "Agent Subscription Tracker"
#define AppExeName "AgentSubscriptionTracker.App.exe"
#define AppPublisher "Ray Pabonnie"
#define AppUrl "https://github.com/rpabonnie/AgentSubscritionTracker"

[Setup]
; Fixed AppId so upgrades replace the existing install instead of duplicating it.
AppId={{7E1B3C7A-92D4-4A14-B7A4-3F4D5C0A9E21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
; Per-user: no admin prompt, installs under the user profile only.
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=AgentSubscriptionTracker-Setup-{#AppVersion}
SetupIconFile=..\src\AgentSubscriptionTracker.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Ask the running instance to close before replacing files (single-instance app).
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#AppName} automatically when I sign in to Windows"; \
    GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName} now"; \
    Flags: nowait postinstall skipifsilent
