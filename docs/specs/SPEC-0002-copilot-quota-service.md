# SPEC-0002 — Copilot Quota Service

| | |
|---|---|
| **Task** | TASK-004 (spec) → TASK-007 (code) |
| **Author** | spec-writer |
| **Date** | 2026-06-10 |
| **Status** | Ready for implementation |
| **Research basis** | `docs/COPILOT_QUOTA_API_RESEARCH.md` (verified 2026-06-10) |
| **Related** | ADR-0001 (Windows Credential Manager), CLAUDE.md Security Standards |

---

## 1. Scope

### In scope
- `ICopilotQuotaService` + `CopilotQuotaService`: fetch the signed-in user's GitHub Copilot quota from the (undocumented, verified) internal endpoint `GET https://api.github.com/copilot_internal/user` and map it to a `CopilotQuotaSnapshot`.
- `ICopilotTokenProvider` + `CopilotTokenProvider`: discover a locally stored Copilot OAuth token via the exact research-verified chain (Credential Manager → `apps.json`/`hosts.json` in `%LOCALAPPDATA%` → same files under `~/.config` → `~/.copilot/config.json`). Token values are held **in memory only**.
- `ICopilotCredentialStore`: thin abstraction over Windows Credential Manager `CredRead` (P/Invoke, `CRED_TYPE_GENERIC`) so tests can fake it. Production implementation `WindowsCredentialStore` lives behind this interface.
- Provider states `Ok / NotSignedIn / Forbidden / RateLimited / Unavailable`, caching + debounce (≥ 30 s), retry/backoff, `Retry-After` handling, strict-but-tolerant deserialization, secret redaction, `TimeProvider` injection.

### Out of scope
- Any UI (SPEC-0003 consumes the snapshot).
- Claude/Anthropic usage (SPEC-0001).
- `copilot_internal/v2/token` exchange (enterprise/chat-token flows — not needed for individual quota reads).
- Reading VS Code's encrypted secret storage (explicitly forbidden by research — do not attempt).
- Writing anything to Credential Manager or to disk.

### Source layout (code agent)
- `src/AgentSubscriptionTracker.App/Models/CopilotQuotaModels.cs` (or split per type) — namespace `AgentSubscriptionTracker.App.Models`
- `src/AgentSubscriptionTracker.App/Services/CopilotQuotaService.cs`, `CopilotTokenProvider.cs`, `WindowsCredentialStore.cs` — namespace `AgentSubscriptionTracker.App.Services`

---

## 2. Public type contracts

Tests in `tests/AgentSubscriptionTracker.Tests/Copilot/` bind to these signatures **exactly**. The code agent must implement them verbatim (additional `internal` helpers are free).

```csharp
namespace AgentSubscriptionTracker.App.Models;

public enum CopilotProviderState
{
    Ok,
    NotSignedIn,
    Forbidden,
    RateLimited,
    Unavailable,
}

public sealed record CopilotQuotaBucket
{
    public required string Key { get; init; }          // "chat" | "completions" | "premium_interactions"
    public required string DisplayName { get; init; }  // "Chat" | "Code completions" | "Premium requests"
    public int? Entitlement { get; init; }             // quota_snapshots.<key>.entitlement
    public int? Remaining { get; init; }               // quota_snapshots.<key>.remaining
    public double? PercentRemaining { get; init; }     // quota_snapshots.<key>.percent_remaining (0–100)
    public bool Unlimited { get; init; }               // quota_snapshots.<key>.unlimited (missing → false)
    public int OverageCount { get; init; }             // quota_snapshots.<key>.overage_count (missing → 0)
    public bool OveragePermitted { get; init; }        // quota_snapshots.<key>.overage_permitted (missing → false)

    // Computed, not serialized: null when Unlimited or when Entitlement/Remaining is null;
    // otherwise Entitlement - Remaining.
    public int? Used { get; }
}

public sealed record CopilotQuotaSnapshot
{
    public required CopilotProviderState State { get; init; }
    public string? Plan { get; init; }                       // copilot_plan ("individual" | "individual_pro" | "business" | unknown future values)
    public DateOnly? QuotaResetDate { get; init; }           // quota_reset_date "YYYY-MM-DD"; null when absent/unparseable
    public CopilotQuotaBucket? Chat { get; init; }
    public CopilotQuotaBucket? Completions { get; init; }
    public CopilotQuotaBucket? PremiumInteractions { get; init; }
    public DateTimeOffset RetrievedAt { get; init; }         // timeProvider.GetUtcNow() at fetch (or original fetch when IsFromCache)
    public bool IsFromCache { get; init; }
    public string? StatusMessage { get; init; }              // user-displayable, ALWAYS redacted (see §6); null when State == Ok

    // Reset moment is QuotaResetDate at 00:00:00 UTC.
    // Returns null when QuotaResetDate is null; clamps negative results to TimeSpan.Zero.
    public TimeSpan? GetTimeUntilReset(TimeProvider timeProvider);
}
```

```csharp
namespace AgentSubscriptionTracker.App.Services;

using AgentSubscriptionTracker.App.Models;

public interface ICopilotQuotaService
{
    Task<CopilotQuotaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public enum CopilotTokenSource
{
    CredentialManager,    // Windows Credential Manager, service "copilot-cli"
    AppsJson,             // %LOCALAPPDATA%\github-copilot\apps.json
    HostsJson,            // %LOCALAPPDATA%\github-copilot\hosts.json
    UserConfigAppsJson,   // %USERPROFILE%\.config\github-copilot\apps.json
    UserConfigHostsJson,  // %USERPROFILE%\.config\github-copilot\hosts.json
    CopilotCliConfig,     // %USERPROFILE%\.copilot\config.json
}

public sealed record CopilotToken(string Value, CopilotTokenSource Source);

public interface ICopilotTokenProvider
{
    // null when the whole discovery chain comes up empty (→ NotSignedIn).
    Task<CopilotToken?> GetTokenAsync(CancellationToken cancellationToken = default);
}

// Abstraction over Windows Credential Manager CredRead (CRED_TYPE_GENERIC).
// Production impl: WindowsCredentialStore (P/Invoke advapi32 CredReadW/CredFree).
// Returns the credential blob decoded as UTF-16/UTF-8 string, or null when the
// credential does not exist. The value must never be logged or persisted.
public interface ICopilotCredentialStore
{
    string? ReadSecret(string serviceName);
}

public sealed record CopilotTokenProviderOptions
{
    // Test overrides; null → Environment.GetFolderPath defaults.
    public string? LocalAppDataPath { get; init; }   // default: LocalApplicationData
    public string? UserProfilePath { get; init; }    // default: UserProfile
}

public sealed class CopilotTokenProvider : ICopilotTokenProvider
{
    public CopilotTokenProvider(ICopilotCredentialStore credentialStore,
                                CopilotTokenProviderOptions? options = null);

    public Task<CopilotToken?> GetTokenAsync(CancellationToken cancellationToken = default);
}

public sealed record CopilotQuotaServiceOptions
{
    public Uri BaseAddress { get; init; }          // default: https://api.github.com/  (ctor rejects any other host)
    public TimeSpan HttpTimeout { get; init; }     // default: 10 s (CLAUDE.md cap; must be ≤ 10 s)
    public TimeSpan MinRefreshInterval { get; init; } // default: 30 s (CLAUDE.md hover-refresh debounce)
    public int MaxRetries { get; init; }           // default: 2 (transient 5xx/network only)
}

public sealed class CopilotQuotaService : ICopilotQuotaService, IDisposable
{
    // httpMessageHandler injected for testability; service wraps it in a single
    // HttpClient (disposeHandler: false) with Timeout = options.HttpTimeout.
    public CopilotQuotaService(ICopilotTokenProvider tokenProvider,
                               HttpMessageHandler httpMessageHandler,
                               TimeProvider timeProvider,
                               CopilotQuotaServiceOptions? options = null);

    public Task<CopilotQuotaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    public void Dispose();
}
```

---

## 3. Token discovery chain (`CopilotTokenProvider`)

Exactly the research-verified order. First hit with a non-empty token wins; every failure (missing file, IO error, malformed JSON, empty/whitespace token) is swallowed and the chain continues. Nothing is ever written; file contents and token values are never logged.

1. **Windows Credential Manager**, service name `"copilot-cli"`, via `ICopilotCredentialStore.ReadSecret("copilot-cli")`.
   - If the blob parses as a JSON object containing a non-empty string `oauth_token`, use that value; otherwise use the trimmed raw blob.
   - Source: `CredentialManager`.
2. `{LocalAppDataPath}\github-copilot\apps.json` — JSON object keyed like `"github.com:Iv1.<appid>"`; take the **first** entry whose key starts with `"github.com"` and read its `oauth_token`. Source: `AppsJson`.
3. `{LocalAppDataPath}\github-copilot\hosts.json` — JSON object; take the entry whose key is `"github.com"` (or starts with `"github.com"`) and read its `oauth_token`. Source: `HostsJson`.
4. `{UserProfilePath}\.config\github-copilot\apps.json` then `hosts.json` — same parsing as 2/3. Sources: `UserConfigAppsJson` / `UserConfigHostsJson`.
5. `{UserProfilePath}\.copilot\config.json` — Copilot CLI plaintext fallback. Accept a top-level non-empty string `oauth_token`, or `github.com` → object → `oauth_token` (tolerant: first match wins). Source: `CopilotCliConfig`.

If the chain is empty → return `null`. The service maps this to `NotSignedIn` with a `StatusMessage` that **must contain the phrase `"Copilot CLI"`** (sign-in guidance per research: "Sign in via Copilot CLI (or JetBrains/Neovim plugin)"). Do **not** attempt VS Code's encrypted secret storage.

The file-path roots come from `CopilotTokenProviderOptions` overrides when set (tests point them at temp directories), otherwise `Environment.GetFolderPath(LocalApplicationData / UserProfile)`.

---

## 4. HTTP contract (`CopilotQuotaService`)

One request shape, verbatim from research — **`token` scheme, not `Bearer`**, and no `X-GitHub-Api-Version` header:

```
GET {BaseAddress}copilot_internal/user
Authorization: token <oauth_token>
Editor-Version: vscode/1.104.1
Editor-Plugin-Version: copilot-chat/0.26.7
User-Agent: GitHubCopilotChat/0.26.7
Copilot-Integration-Id: vscode-chat
Accept: application/json
```

- HTTPS only; TLS validation never relaxed. `BaseAddress` host must be `api.github.com` (CLAUDE.md allowlist) — constructor throws `ArgumentException` otherwise.
- Response body read with a bounded size (≤ 1 MiB); larger → treat as `Unavailable`.
- Deserialization: `System.Text.Json`, strict-but-tolerant — unknown fields ignored, missing/null fields → null model properties, a missing bucket → null `CopilotQuotaBucket`, **never throw on absent fields**. `quota_reset_date` parsed as invariant `yyyy-MM-dd`; unparseable → `null`.

### Response mapping (HTTP 200)

| JSON | Model |
|---|---|
| `copilot_plan` | `Snapshot.Plan` |
| `quota_reset_date` (`"YYYY-MM-DD"`) | `Snapshot.QuotaResetDate` |
| `quota_snapshots.chat` | `Snapshot.Chat` (`Key="chat"`, `DisplayName="Chat"`) |
| `quota_snapshots.completions` | `Snapshot.Completions` (`Key="completions"`, `DisplayName="Code completions"`) |
| `quota_snapshots.premium_interactions` | `Snapshot.PremiumInteractions` (`Key="premium_interactions"`, `DisplayName="Premium requests"`) |
| per bucket: `entitlement`, `remaining`, `percent_remaining`, `unlimited`, `overage_count`, `overage_permitted` | corresponding bucket properties |

`unlimited == true` ⇒ numeric fields are display-irrelevant: `Used` must be `null` (UI renders "Unlimited"). `used = entitlement - remaining` only when not unlimited and both present.

### Provider-state mapping

| Condition | `State` | Notes |
|---|---|---|
| 200 + parseable body | `Ok` | `StatusMessage = null` |
| Token chain empty | `NotSignedIn` | **no HTTP request issued**; message contains `"Copilot CLI"` |
| 401 | `NotSignedIn` | token expired/revoked; message contains `"Copilot CLI"` |
| 403 | `Forbidden` | known for stale Neovim `hosts.json` tokens; suggest re-sign-in with Copilot CLI/JetBrains |
| 429 | `RateLimited` | honor `Retry-After` (delta-seconds or HTTP-date); no new request until it elapses |
| 404, 5xx (after retries), network error, timeout, malformed/oversized JSON | `Unavailable` | schema is unversioned and may change without notice |

The service must **never throw** from `GetSnapshotAsync` for any provider/network/parse condition — only `OperationCanceledException` (caller cancellation) and `ObjectDisposedException` may escape.

---

## 5. Caching, debounce, retry

All timing decisions use the injected `TimeProvider` (no `DateTime.Now`/`DateTimeOffset.UtcNow`, no `Stopwatch`).

- **Debounce (≥ 30 s)**: after any completed fetch attempt (success or failure), calls within `MinRefreshInterval` return the cached snapshot with `IsFromCache = true` and **no HTTP request and no credential-chain walk**. `RetrievedAt` keeps the original fetch time so the UI can show data age.
- **Rate limiting**: on 429, record the `Retry-After` deadline (default 60 s when the header is missing/unparseable). Until that deadline (even past `MinRefreshInterval`), return cached `RateLimited` state without a request.
- **Retry/backoff**: transient failures only (5xx, network errors) — up to `MaxRetries` extra attempts with exponential backoff + jitter (e.g. base 500 ms ×2, ±25 %). Delays must run through the injected `TimeProvider` (`Task.Delay(delay, timeProvider, ct)`). Never retry 401/403/404/429 within a call.
- **Last-good fallback**: when a refresh fails after a previous `Ok` fetch, the returned snapshot carries the **error `State`** but preserves the last good `Plan`, `QuotaResetDate`, and buckets, with `IsFromCache = true` — so the UI can show stale numbers plus the problem state.
- **Concurrency**: `GetSnapshotAsync` is thread-safe; concurrent callers must not produce parallel requests (coalesce on a single in-flight fetch, e.g. `SemaphoreSlim`).
- `Dispose()` disposes the internal `HttpClient` only (injected handler is owned by the caller — `disposeHandler: false`).

---

## 6. Security & error handling (CLAUDE.md Security Standards)

- **Memory-only tokens**: token values from any source live in locals/fields for the duration of use; never written to disk, cache files, or Credential Manager; never placed in `CopilotQuotaSnapshot` or any model.
- **Redaction**: `StatusMessage`, exception messages, and any log output must never contain the token value, the `Authorization` header, or raw credential-file contents. Response bodies of error statuses must not be echoed verbatim into `StatusMessage` (they could echo the token); use fixed, friendly texts.
- **Fail closed / degrade gracefully**: every failure path lands on a well-defined `CopilotProviderState`; the app never crashes over a fetch failure and never relaxes TLS or falls back to HTTP.
- **Allowlist**: outbound host strictly `api.github.com`; constructor-enforced (see §4).
- **Timeout**: `HttpTimeout` ≤ 10 s, enforced on the `HttpClient`; a timeout yields `Unavailable`.
- **No telemetry**, no extra endpoints, runs as standard user.
- **ToS note** (research): reading another client's token is unofficial. `docs/COPILOT_SETUP.md` (code-agent deliverable, see checklist) must state this and describe the sign-in paths (Copilot CLI / JetBrains / Neovim).

---

## 7. Test plan (authored with this spec — currently failing/non-compiling, which is expected)

| File | Covers |
|---|---|
| `tests/AgentSubscriptionTracker.Tests/Copilot/CopilotTokenProviderTests.cs` | discovery-chain order, Credential Manager precedence + JSON-blob form, each file fallback, malformed-file skip, empty chain → null |
| `tests/AgentSubscriptionTracker.Tests/Copilot/CopilotQuotaServiceTests.cs` | exact request shape/headers, fixture mapping, unlimited buckets, missing bucket, null fields, malformed JSON, 401/403/429 (+`Retry-After`), no-token short-circuit, debounce/caching, last-good fallback, redaction |
| `tests/AgentSubscriptionTracker.Tests/Copilot/CopilotQuotaSnapshotTests.cs` | `Used` math, unlimited semantics, `GetTimeUntilReset` with injected `TimeProvider` (future date, past date → zero, null date) |
| `tests/AgentSubscriptionTracker.Tests/Copilot/CopilotTestSupport.cs` | fakes: `FakeTimeProvider`, `StubHttpMessageHandler`, `FakeCopilotTokenProvider`, `FakeCopilotCredentialStore`, temp-dir + fixture helpers |
| `tests/AgentSubscriptionTracker.Tests/Fixtures/Copilot/*.json` | golden-file fixtures with **fake tokens only** (`fake-token-for-tests*`) |

No test performs a live network call (stubbed `HttpMessageHandler` only) or reads real credential stores (fakes + temp dirs only).

---

## 8. Acceptance criteria checklist (code agent — all must be true before TASK-007 closes)

- [ ] All public types/members in §2 exist with the exact signatures, namespaces `AgentSubscriptionTracker.App.Models` / `AgentSubscriptionTracker.App.Services`.
- [ ] `WindowsCredentialStore : ICopilotCredentialStore` implemented via `CredReadW`/`CredFree` P/Invoke (`CRED_TYPE_GENERIC`); returns `null` when absent; secret never logged.
- [ ] `CopilotTokenProvider` walks the chain in §3 order, in memory only, tolerating missing/malformed files, honoring `CopilotTokenProviderOptions` path overrides.
- [ ] Request shape matches §4 exactly (`token` scheme, all four editor headers, `Accept: application/json`, no `X-GitHub-Api-Version`).
- [ ] Response mapping + provider-state table in §4 implemented; `GetSnapshotAsync` never throws on provider/network/parse failures.
- [ ] Tolerant deserialization: unknown fields ignored; missing/null fields → null; missing bucket → null bucket with `State == Ok`.
- [ ] Caching/debounce, `Retry-After`, retry/backoff, last-good fallback per §5, all timed via the injected `TimeProvider`.
- [ ] Redaction guarantees of §6 hold (no token in `StatusMessage`/exceptions/logs); `NotSignedIn` messages contain `"Copilot CLI"`.
- [ ] Constructor rejects non-`api.github.com` `BaseAddress`; `HttpTimeout` default 10 s; TLS validation untouched.
- [ ] All SPEC-0002 tests pass; `dotnet build` and `dotnet test` green for the whole solution with `TreatWarningsAsErrors=true` (suppressions only via `.editorconfig` with one-line justification where genuinely inapplicable).
- [ ] No live network calls in tests; no real tokens anywhere in the repo (fixtures use `fake-token-for-tests*`).
- [ ] `docs/COPILOT_SETUP.md` created: sign-in paths, unofficial-API/ToS note, troubleshooting for 403 (stale Neovim token).
- [ ] Spec file not modified by the code agent.
