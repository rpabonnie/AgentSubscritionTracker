# Implementation Summary

> Updated after each major implementation phase (CLAUDE.md Documentation Convention).

## Phase: TASK-006 — SPEC-0001 Claude usage service (2026-06-10)

### Delivered

| File | Contents |
|---|---|
| `src/AgentSubscriptionTracker.App/Services/IClaudeUsageService.cs` | `IClaudeUsageService` |
| `src/AgentSubscriptionTracker.App/Services/ClaudeUsageModels.cs` | `ClaudeProviderState`, `ClaudeUsageBucket`, `ClaudeExtraUsage`, `ClaudeUsageSnapshot`, `ClaudeUsageServiceOptions` |
| `src/AgentSubscriptionTracker.App/Services/ClaudeCredentialsFileReader.cs` | `IClaudeCredentialsReader`, `ClaudeOAuthCredentials` (redacted `ToString`), `ClaudeCredentialsFileReader` |
| `src/AgentSubscriptionTracker.App/Services/ClaudeUsageService.cs` | `ClaudeUsageService` — poll pipeline, in-memory OAuth refresh, retry/backoff, min-poll-interval cache |

Behavior per SPEC-0001: fresh read-only credential discovery from
`%USERPROFILE%\.claude\.credentials.json` each non-gated poll (never written, opened with
`FileShare.ReadWrite`); proactive + 401-triggered in-memory token refresh via
`platform.claude.com` then `console.anthropic.com` fallback; `GET api.anthropic.com/api/oauth/usage`
with Bearer + `anthropic-beta: oauth-2025-04-20` + `claude-code/<version>` UA; defensive
mapping of the four buckets (clamped utilization, null = unlimited/absent) and optional
`extra_usage`; five provider states; 180 s min-poll gate with cached snapshot, growing
data age, Retry-After honoring, and stale-data carry-over on failure; exponential
backoff + jitter for transient failures; full token redaction; polls serialized via
`SemaphoreSlim`; all time via injected `TimeProvider`; HTTP timeout 10 s.

### Compile-only skeletons for later tasks

So the whole test project compiles (SPEC-0002/0003 stubs reference their contract types),
minimal skeletons throwing `NotImplementedException` were added — to be replaced by
TASK-007/TASK-008:

- `src/AgentSubscriptionTracker.App/Models/CopilotQuotaModels.cs` (trivial pure model math implemented)
- `src/AgentSubscriptionTracker.App/Services/CopilotQuotaContracts.cs`
- `src/AgentSubscriptionTracker.App/ViewModels/TrayViewModelContracts.cs`

### Build/test infrastructure fixes

- `tests/AgentSubscriptionTracker.Tests.csproj`: added `System.IO` / `System.Net.Http`
  global usings (UseWPF removes them from the SDK implicit-usings set; the spec-writer
  stubs relied on them).
- `tests/.editorconfig`: suppressed CA1515 (shared public test doubles) and CA2000
  (in-memory test doubles, no-op disposal) with justifications.
- `src/AgentSubscriptionTracker.App/.editorconfig`: suppressed CA1515 (spec contract
  types are public by binding contract and compiled against by the test assembly).

### Verification

- `dotnet build` — 0 warnings, 0 errors (TreatWarningsAsErrors, AnalysisMode=All).
- `dotnet test --filter FullyQualifiedName~AgentSubscriptionTracker.Tests.ClaudeUsage`
  — 37/37 passed, no live network.
- Full `dotnet test` — 152 total: all 106 failures belong to SPEC-0002/SPEC-0003
  (NotImplementedException from skeletons, expected red until TASK-007/008); zero
  SPEC-0001 failures.

### Open items

- ⚠️ Host-allowlist ADR for `platform.claude.com` / `console.anthropic.com`
  (suggested `ADR-0002-Claude-OAuth-Refresh-Hosts`) is required by SPEC-0001 §5 before
  TASK-006 can be closed — orchestrator/human action.
