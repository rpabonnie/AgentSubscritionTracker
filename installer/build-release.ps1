# Local release packaging for Agent Subscription Tracker — no CI, runs entirely on this machine.
#   ./installer/build-release.ps1               -> test + publish + compile installer into ./artifacts
#   ./installer/build-release.ps1 -SkipTests    -> skip the test run (not recommended)
# Requires: .NET SDK 10, Inno Setup 6 (winget install -e --id JRSoftware.InnoSetup)

[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\AgentSubscriptionTracker.App\AgentSubscriptionTracker.App.csproj'
$publishDir = Join-Path $repoRoot 'src\AgentSubscriptionTracker.App\bin\Release\net10.0-windows\win-x64\publish'
$artifactsDir = Join-Path $repoRoot 'artifacts'

# Version comes from the single source of truth: the <Version> tag in the csproj.
$version = ([xml](Get-Content $appProject)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $appProject" }
Write-Host "Packaging version $version" -ForegroundColor Cyan

if (-not $SkipTests) {
    Write-Host '== dotnet test ==' -ForegroundColor Cyan
    dotnet test $repoRoot --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed - aborting release build.' }
}

Write-Host '== dotnet publish (self-contained win-x64) ==' -ForegroundColor Cyan
dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup 6 not found. Install it with: winget install -e --id JRSoftware.InnoSetup'
}

New-Item -ItemType Directory -Force $artifactsDir | Out-Null

Write-Host '== Compiling installer (Inno Setup) ==' -ForegroundColor Cyan
& $iscc "/DAppVersion=$version" "/DPublishDir=$publishDir" (Join-Path $PSScriptRoot 'setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

$setupExe = Join-Path $artifactsDir "AgentSubscriptionTracker-Setup-$version.exe"
Write-Host "Done: $setupExe" -ForegroundColor Green
