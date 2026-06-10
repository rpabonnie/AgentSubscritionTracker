# ADR-0001 — Secret Storage: Windows Credential Manager instead of Azure Key Vault

- **Status**: Accepted (human-approved 2026-06-10)
- **Deciders**: Repository owner (human checkpoint), orchestrator

## Context

The harness default in `CLAUDE.md` mandated Azure Key Vault + Managed Identity for all secrets. AgentSubscritionTracker is a purely local Windows desktop tray widget with no cloud backend. Key Vault would require an Azure subscription, network availability at startup, and interactive Azure auth (Managed Identity does not apply to desktop apps).

The app additionally needs to *read* tokens that other tools already store locally:

- **Claude Code** OAuth credentials (to query the subscription usage endpoint that powers `/usage`).
- **GitHub Copilot** OAuth token stored by local Copilot clients (to query the internal quota endpoint).

## Decision

1. Any secret the app itself must persist (e.g. a cached/derived token, optional PAT fallback) is stored in **Windows Credential Manager** under a `AgentSubscriptionTracker/*` target name, via the native `CredRead`/`CredWrite` Win32 APIs.
2. Tokens read from Claude Code / Copilot local stores are held **in memory only** — never persisted by this app, never written to logs, repo files, or config.
3. The Azure Key Vault rule in `CLAUDE.md` remains the default for any future cloud component; this ADR covers the desktop app only.

## Consequences

- No Azure dependency; the widget works fully offline except for the two usage API calls.
- Secrets are encrypted per-user by the OS; other users on the machine cannot read them.
- The deny-list rule "never write credentials to any file" remains fully enforceable.
- `.example` config counterpart rule still applies to any config file the app introduces.
