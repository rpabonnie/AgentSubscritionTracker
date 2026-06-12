# Agent Subscription Tracker

A tiny Windows tray widget that shows, at a glance, how much of your **Claude** (Anthropic) and **GitHub Copilot** subscription you have left in the current period — without opening a browser, a dashboard, or another Electron app.

Hover the tray icon and a callout pops up with usage bars for:

- **Claude** — 5-hour session window, weekly all-models limit, weekly Opus/Sonnet limits, and pay-per-use extra credits when enabled.
- **GitHub Copilot** — premium requests, chat, and code completions, plus your plan and the monthly reset date.

Bars turn amber at 75 % used and red at 90 %, the footer shows how fresh the data is, and countdowns tell you when each limit resets (computed in UTC, shown in your local time).

## How it works

The app does **not** ask you for any credentials and never opens a sign-in page of its own. Instead, it reuses the sign-ins you already have on your machine:

- **Claude** — reads the OAuth token that **Claude Code** stores locally (`%USERPROFILE%\.claude\.credentials.json`) and queries Anthropic's usage endpoint with it. If the token has expired it refreshes it in memory only — the file on disk is never modified.
- **GitHub Copilot** — discovers a GitHub OAuth token from the places the official tooling stores it (Windows Credential Manager, the Copilot CLI/IDE-plugin config files, or the GitHub CLI) and queries GitHub's Copilot quota endpoint.

If you aren't signed in to one of the providers, its section simply shows a hint ("Sign in via Claude Code CLI" / "run `gh auth login`") — the other provider keeps working.

### Privacy & security posture

This app is built deliberately paranoid, because it touches other apps' tokens:

- **No telemetry, no analytics, no logging.** Nothing is collected, nothing is phoned home. The only network calls are the usage queries to `api.anthropic.com`, `api.github.com`, and Anthropic's OAuth refresh endpoint — the allowed hosts are enforced in code at startup.
- **Tokens live in memory only.** They are never written to disk, never logged, and redacted from all diagnostic output.
- **Read-only everywhere.** Credential files and the Windows Credential Manager are only ever read, never written.
- **Runs as a standard user.** No admin rights, no elevation, no listening sockets, HTTPS with full certificate validation only.
- **Gentle on the APIs.** Results are cached and refreshes are debounced (≥ 30 s for Copilot, ≥ 3 min for Claude), so hovering repeatedly never hammers anything.

## Installation

1. Go to the [**Releases page**](https://github.com/rpabonnie/AgentSubscritionTracker/releases) and download the latest `AgentSubscriptionTracker-Setup-<version>.exe`.
2. Run the installer. It installs per-user (no admin prompt) into `%LOCALAPPDATA%\Programs\Agent Subscription Tracker` and offers an optional "start with Windows" checkbox.
3. Launch **Agent Subscription Tracker** from the Start menu. A small icon appears in the notification area (you may want to drag it out of the overflow flyout so it's always visible).
4. Hover the icon — that's it. There is no main window; the callout's footer has **Refresh** and **Exit** buttons.

> **Alpha note:** the installer is not code-signed yet, so Windows SmartScreen may show "Windows protected your PC". Click **More info → Run anyway**. The app targets 64-bit Windows 10/11 and is fully self-contained — no .NET runtime install needed.

### Prerequisites for seeing data

- **Claude:** be signed in to [Claude Code](https://claude.com/claude-code) on the same machine (`claude` → `/login`).
- **Copilot:** be signed in with the GitHub CLI (`gh auth login`) or the Copilot CLI / a Copilot IDE plugin.

## Building from source

```powershell
git clone https://github.com/rpabonnie/AgentSubscritionTracker.git
cd AgentSubscritionTracker
dotnet test          # full suite, no network needed
dotnet run --project src/AgentSubscriptionTracker.App
```

To produce the installer locally (requires [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```powershell
./installer/build-release.ps1
```

This publishes a self-contained build and compiles `installer/setup.iss` into `artifacts/AgentSubscriptionTracker-Setup-<version>.exe`.

## Documentation

- [Changelog](docs/CHANGELOG.md)
- [Architecture Decision Records](docs/adr/)
- [Copilot token discovery setup](docs/COPILOT_SETUP.md)

## License

See [LICENSE](LICENSE).
