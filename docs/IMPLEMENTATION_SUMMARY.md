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
  → **Resolved 2026-06-11**: `docs/adr/ADR-0002-Claude-Token-Refresh-Host-Allowlist.md`;
  CLAUDE.md and copilot-instructions.md allowlists updated.

## Phase: TASK-007 — SPEC-0002 Copilot quota service (2026-06-10, verified 2026-06-11)

### Delivered

| File | Contents |
|---|---|
| `src/AgentSubscriptionTracker.App/Services/CopilotQuotaContracts.cs` | `ICopilotQuotaService`, `ICopilotTokenProvider`, `ICopilotCredentialStore`, `CopilotToken`, options records |
| `src/AgentSubscriptionTracker.App/Services/CopilotTokenProvider.cs` | research-verified discovery chain: Credential Manager → apps/hosts.json (LocalAppData, ~/.config) → ~/.copilot/config.json |
| `src/AgentSubscriptionTracker.App/Services/WindowsCredentialStore.cs` | read-only `CredReadW`/`CredFree` P/Invoke (`CRED_TYPE_GENERIC`) |
| `src/AgentSubscriptionTracker.App/Services/CopilotQuotaService.cs` | `GET api.github.com/copilot_internal/user` (`token` scheme + editor headers), state mapping, 30 s debounce, Retry-After, retry/backoff, last-good fallback |
| `docs/COPILOT_SETUP.md` | sign-in paths, unofficial-API/ToS note, 403 troubleshooting |

### Verification

- All 30 `Tests.Copilot` tests green (request shape, fixture mapping, provider states,
  caching/debounce, redaction); no live network, fake tokens only.

## Phase: TASK-008 — SPEC-0003 tray icon + tooltip callout UI (2026-06-11)

### Delivered — view-models (unit-tested, replaces `TrayViewModelContracts.cs` skeleton)

| File | Contents |
|---|---|
| `src/AgentSubscriptionTracker.App/ViewModels/RefreshPolicy.cs` | `RefreshTrigger`, `RefreshPolicy` (180 s / 30 s / 2 s defaults) |
| `src/AgentSubscriptionTracker.App/ViewModels/UsageBarViewModel.cs` | `UsageSeverity`, `UsageBarViewModel` |
| `src/AgentSubscriptionTracker.App/ViewModels/UsageFormatting.cs` | invariant countdown / data-age / monthly-reset / percent strings (§4.2 exact) |
| `src/AgentSubscriptionTracker.App/ViewModels/ProviderViewModel.cs` | `ProviderDisplayState`, snapshot→presentation mapping for both providers (§4.1/§4.3) |
| `src/AgentSubscriptionTracker.App/ViewModels/TrayViewModel.cs` | refresh orchestration (§4.4): single-flight, per-provider hover gating + 2 s budget (`CancellationTokenSource(timeout, TimeProvider)`), fail-closed Unavailable presentation with cached carry-over, `INotifyPropertyChanged` |

### Delivered — shell (not unit-tested; TASK-011 human checkpoint)

| File | Contents |
|---|---|
| `src/AgentSubscriptionTracker.App/Tray/TrayIconHost.cs` | raw `Shell_NotifyIcon` v4 (`LibraryImport`, no WinForms): NIM_ADD/SETVERSION, TaskbarCreated re-add, NIN_POPUPOPEN/CLOSE, dwell fallback, NIM_DELETE on dispose, `Shell_NotifyIconGetRect` |
| `src/AgentSubscriptionTracker.App/Tray/CalloutController.cs` | hover open + background refresh, 300 ms-grace pointer watch, DPI-aware taskbar-edge positioning clamped to the working area |
| `src/AgentSubscriptionTracker.App/Tray/ThemeDetector.cs` | `AppsUseLightTheme` registry read + theme dictionary swap (live via WM_SETTINGCHANGE) |
| `src/AgentSubscriptionTracker.App/Views/CalloutWindow.xaml(.cs)` | borderless rounded topmost callout, provider sections with severity-colored bars, 1 s data-age ticker, "(cached)" stale marker |
| `src/AgentSubscriptionTracker.App/Themes/Dark.xaml`, `Light.xaml` | theme brushes |
| `src/AgentSubscriptionTracker.App/App.xaml.cs` | composition root: single-instance mutex + show-callout event, service wiring (`SocketsHttpHandler`, TLS defaults untouched), tray context menu (Refresh now / Exit; Start-with-Windows deferred with TODO), disposal on all exit paths |
| `src/AgentSubscriptionTracker.App/app.manifest` | PerMonitorV2 DPI, `asInvoker` |
| `src/AgentSubscriptionTracker.App/Assets/AppIcon.ico` + `generate-icon.ps1` | multi-size icon (16–256 px, PNG-compressed ICO) + committed one-time generator; build never runs the script |

`MainWindow.xaml(.cs)` (scaffold-only) deleted; `ShutdownMode=OnExplicitShutdown`.

### Verification

- `dotnet build` — 0 warnings, 0 errors (TreatWarningsAsErrors, AnalysisMode=All).
- `dotnet test` — **152/152 passed** (37 ClaudeUsage, 30 Copilot, 82 Tray, 3 infra); no window
  shown, no live network, no real credential stores.
- Smoke test: app launches and stays running (tray icon registered); second launch exits 0
  (single-instance mutex) and signals the first instance.
- New `.editorconfig` suppression: CA1031 scoped to `ViewModels/TrayViewModel.cs` only
  (SPEC-0003 §4.4 mandates fail-closed catch-all in refresh orchestration).

### Open items

- TASK-011 human checkpoint: verify hover callout, positioning, themes, Explorer-restart
  re-add, and live provider data on a signed-in machine.
- "Start with Windows" context-menu toggle deferred (spec-optional for v1, code TODO in App.xaml.cs).
