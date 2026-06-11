# ADR-0002: Allowlist Claude OAuth token-refresh hosts

- **Status**: Accepted
- **Date**: 2026-06-11
- **Deciders**: orchestrator (per CLAUDE.md network rule: "No other endpoints without an ADR")
- **Related**: SPEC-0001, docs/CLAUDE_USAGE_API_RESEARCH.md, ADR-0001

## Context

CLAUDE.md restricts outbound calls to `api.anthropic.com` and `api.github.com`. The Claude usage
service (SPEC-0001) reads the Claude Code OAuth credentials and queries
`https://api.anthropic.com/api/oauth/usage` — covered by the existing allowlist.

However, when the locally stored access token has expired, the only way to obtain a fresh one
without forcing the user back into Claude Code is the OAuth refresh-token grant. The verified
research (docs/CLAUDE_USAGE_API_RESEARCH.md §Token refresh) confirms the refresh endpoint lives on
a different host than the usage endpoint:

- `https://platform.claude.com/v1/oauth/token` (current)
- `https://console.anthropic.com/v1/oauth/token` (older deployments; tried as fallback)

Both are first-party Anthropic-operated hosts serving the same OAuth token service that Claude Code
itself uses.

## Decision

Add `platform.claude.com` and `console.anthropic.com` to the outbound host allowlist, restricted to
the single path `/v1/oauth/token` via HTTPS POST for the refresh-token grant only.

Constraints (enforced by `ClaudeUsageService` / SPEC-0001):

- Refresh runs only when `expiresAt` has passed; never proactively.
- The refreshed token is held **in memory only**; `.credentials.json` is never rewritten (avoids
  racing a running Claude Code instance).
- On refresh failure the provider degrades to a "re-login in Claude Code" state — no retry storm.
- All existing TLS, timeout, and redaction rules apply unchanged.

## Consequences

- The full outbound allowlist is now: `api.anthropic.com`, `api.github.com`,
  `platform.claude.com` (refresh only), `console.anthropic.com` (refresh fallback only).
- CLAUDE.md and `.github/copilot-instructions.md` Network sections updated to reference this ADR.
- If Anthropic consolidates the refresh endpoint onto one host, the fallback host can be removed by
  superseding this ADR.

## Alternatives considered

1. **No refresh — re-read `.credentials.json` and show "expired" until Claude Code refreshes it.**
   Rejected: the widget would frequently show stale/unavailable data for users who don't keep
   Claude Code running; the refresh grant is the same mechanism Claude Code uses.
2. **Proxy refresh through `api.anthropic.com`.** Not possible: the token service is not exposed on
   that host.
