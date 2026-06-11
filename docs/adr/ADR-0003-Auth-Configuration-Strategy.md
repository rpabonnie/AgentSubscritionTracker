# ADR-0003: Authentication configuration strategy — zero-config discovery, no token-entry settings page

- **Status**: Accepted
- **Date**: 2026-06-11
- **Deciders**: human (TASK-011 feedback: "Maybe we need a settings page to configure authentication?
  Make an analysis if there is a better way to handle this.") + orchestrator
- **Related**: ADR-0001 (Credential Manager), ADR-0002 (refresh hosts), SPEC-0001/0002 amendments

## Context

TASK-011 acceptance found both providers showing failure states on a machine where the user was
signed in to both services. Diagnosis (verified live on this machine, 2026-06-11):

1. **Claude**: credentials existed but the access token had expired; the OAuth refresh round-trip
   exceeded the 2 s hover budget, which cancelled the fetch and mislabeled it "Unavailable"
   (orchestration bug — fixed in SPEC-0003 §4.4 amendment, independent of configuration).
2. **Copilot**: the 2026 Copilot CLI stores **no plaintext token** in any location the
   research-verified chain covered (`~/.copilot/config.json` carries only settings; no
   `apps.json`/`hosts.json`; no `copilot-cli` Credential Manager entry). However, the **gh CLI**
   keyring entry (`gh:github.com:` in Windows Credential Manager) holds an OAuth token that
   `copilot_internal/user` accepts (probed: HTTP 200 with full quota snapshots).

The question: should the app grow a settings page where the user configures authentication?

## Options considered

### A. Token-entry settings page (user pastes a PAT / token)

**Rejected.** It would not actually work, and it weakens the security posture:

- `copilot_internal/user` does **not** accept PATs (classic or fine-grained) — it requires an OAuth
  token minted by a Copilot-enabled first-party app (gh CLI, VS Code, Copilot CLI). A PAT field
  would be a dead end users can't debug.
- Anthropic has no user-facing token for the subscription-usage endpoint at all — it is OAuth-only,
  bound to Claude Code's client id.
- CLAUDE.md already forbids prompting users to paste tokens into config files; a paste-into-UI flow
  stored via Credential Manager would be possible but, per the above, would have nothing valid to
  store.

### B. Own OAuth flows (GitHub device flow / Anthropic PKCE login inside the app)

**Deferred.** Technically the most robust: the app would mint its own tokens instead of borrowing
other clients'. But it means impersonating first-party client ids (the endpoints reject unknown
clients), which is further into unofficial territory than reading locally stored tokens, and it
adds a meaningful chunk of auth UI/state. Not justified while discovery (option C) works.

### C. Zero-config discovery, extended to cover how the CLIs actually store tokens today — **chosen**

The user's mental model ("I'm signed in, it should pick up my credentials") is the right product
contract. The gap was coverage, not configuration:

- **Copilot**: append gh CLI sources to the chain (SPEC-0002 §3 steps 6–7): Credential Manager
  `gh:github.com:` (keyring) and `%APPDATA%\GitHub CLI\hosts.yml` (keyring-less fallback).
  `gh auth login` is also the simplest sign-in instruction we can give — it is GitHub's official
  CLI and its token works for the quota endpoint.
- **Claude**: discovery was already correct (`~/.claude/.credentials.json`); the failure was the
  budget bug. No configuration needed.
- **Guidance over configuration**: every provider failure state renders a *specific, actionable*
  message in the callout ("run /login in Claude Code", "run 'gh auth login'…"). When discovery
  fails, telling the user which command to run beats giving them a form.

## Decision

1. No settings page for authentication. Discovery stays zero-config; the chain is the contract,
   and it must track where the official CLIs store tokens (re-verify when CLI majors ship).
2. Failure-state messages in the callout are the configuration UX: each names the exact sign-in
   command for its provider.
3. Option B (own device-flow auth) is recorded as the fallback path if GitHub/Anthropic ever
   close off local token reuse; revisit via a new ADR if that happens.
4. A future lightweight **"connection status" panel** (read-only: which source the token came
   from, last error per provider) is acceptable scope if diagnosis-by-message proves insufficient —
   but it shows state, it does not collect secrets.

## Consequences

- New token sources mean the app now reads gh CLI credentials (read-only, in-memory, same
  redaction rules). Documented in `docs/COPILOT_SETUP.md`.
- The discovery chain is environment-sensitive by design; integration breakage (CLI format
  changes) surfaces as `NotSignedIn` with guidance, never as a crash.
- No new attack surface from token-entry UI; Credential Manager writes remain limited to the
  app's own namespace (currently: none).
