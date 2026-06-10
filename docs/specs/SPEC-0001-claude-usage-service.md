# SPEC-0001 — Claude Usage Service

> Spec writer output for TASK-003. Governs the implementation of `IClaudeUsageService`.
> Source research: `docs/CLAUDE_USAGE_API_RESEARCH.md` (verified 2026-06-10).
> Failing test stubs: `tests/AgentSubscriptionTracker.Tests/ClaudeUsage/` + fixtures under
> `tests/AgentSubscriptionTracker.Tests/Fixtures/Claude/`.

---

## 1. Scope

Provide a testable, security-hardened service that reports the signed-in user's Claude
(Anthropic) subscription usage for display in the tray tooltip.

In scope:

- Token discovery from the Claude Code credentials file
  `%USERPROFILE%\.claude\.credentials.json` (read-only; re-read fresh on every poll;
  parse the top-level `claudeAiOauth` object).
- In-memory OAuth token refresh via the documented refresh endpoints when `expiresAt`
  has passed or the usage endpoint returns `401`. The credentials file is **never written**.
- `GET https://api.anthropic.com/api/oauth/usage` with the required headers.
- Mapping `five_hour`, `seven_day`, `seven_day_opus`, `seven_day_sonnet` (all nullable)
  and `extra_usage` (all subfields optional) into a typed snapshot exposing percent
  used / percent remaining and `resets_at` per bucket.
- Provider states: `Ok` / `NotSignedIn` / `TokenExpired` (refresh failed) /
  `RateLimited` (with retry-after) / `Unavailable`.
- Minimum poll interval of 180 s with a cached snapshot and data-age reporting.
- `HttpMessageHandler`, `TimeProvider`, and credentials-reader injection for testability.

Out of scope (other specs/tasks):

- Any UI / view-model / tooltip rendering.
- GitHub Copilot quota (separate spec).
- Windows Credential Manager persistence (nothing in this service is persisted).
- Background scheduling — callers decide *when* to call `GetUsageAsync`; this service
  only enforces the minimum interval between real network polls.

## 2. Files to create (code agent)

| File | Contents |
|---|---|
| `src/AgentSubscriptionTracker.App/Services/IClaudeUsageService.cs` | `IClaudeUsageService` |
| `src/AgentSubscriptionTracker.App/Services/ClaudeUsageService.cs` | `ClaudeUsageService` |
| `src/AgentSubscriptionTracker.App/Services/ClaudeUsageModels.cs` | `ClaudeProviderState`, `ClaudeUsageBucket`, `ClaudeExtraUsage`, `ClaudeUsageSnapshot`, `ClaudeUsageServiceOptions` |
| `src/AgentSubscriptionTracker.App/Services/ClaudeCredentialsFileReader.cs` | `IClaudeCredentialsReader`, `ClaudeOAuthCredentials`, `ClaudeCredentialsFileReader` |

Internal wire DTOs (the raw JSON shapes) are an implementation detail; keep them
`private`/`internal` and deserialize with strict, source-generated or reflection-based
`System.Text.Json` into typed models. Unknown fields are ignored; every field is
treated as optional at the wire level.

## 3. Public type contracts

All types live in namespace `AgentSubscriptionTracker.App.Services`. Signatures are
binding — the test stubs compile against exactly these members.

```csharp
namespace AgentSubscriptionTracker.App.Services;

/// <summary>Connectivity/auth state of the Claude usage provider.</summary>
public enum ClaudeProviderState
{
    /// <summary>Usage data fetched (or served from a fresh cache) successfully.</summary>
    Ok,
    /// <summary>Credentials file missing/unparseable or no access token. "Sign in via Claude Code".</summary>
    NotSignedIn,
    /// <summary>Access token expired (or rejected with 401) and refresh failed. "Re-login in Claude Code".</summary>
    TokenExpired,
    /// <summary>Endpoint returned 429; retry later (see <see cref="ClaudeUsageSnapshot.RetryAfter"/>).</summary>
    RateLimited,
    /// <summary>Network error, timeout, 403, 5xx, or malformed response.</summary>
    Unavailable,
}

/// <summary>Parsed claudeAiOauth credentials. Held in memory only; ToString() is redacted.</summary>
public sealed record ClaudeOAuthCredentials
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    /// <summary>Converted from the file's epoch-milliseconds value.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
    /// <summary>"pro" / "max" when present.</summary>
    public string? SubscriptionType { get; init; }
    /// <summary>MUST NOT include token values (Security Standards: redaction).</summary>
    public override string ToString();
}

/// <summary>Reads Claude Code OAuth credentials. Returns null when not signed in.</summary>
public interface IClaudeCredentialsReader
{
    /// <summary>Fresh read every call. Null when the file is missing, unparseable,
    /// lacks claudeAiOauth, or lacks a non-empty accessToken. Never throws.</summary>
    ClaudeOAuthCredentials? Read();
}

public sealed class ClaudeCredentialsFileReader : IClaudeCredentialsReader
{
    /// <summary>Uses %USERPROFILE%\.claude\.credentials.json.</summary>
    public ClaudeCredentialsFileReader();
    /// <summary>Test/override constructor with an explicit file path.</summary>
    public ClaudeCredentialsFileReader(string credentialsFilePath);
    public ClaudeOAuthCredentials? Read();
}

/// <summary>One rate-limit window. Absolute counts are not exposed by the API.</summary>
public sealed record ClaudeUsageBucket
{
    /// <summary>API "utilization", clamped to [0, 100].</summary>
    public required double PercentUsed { get; init; }
    /// <summary>100 - PercentUsed (therefore also within [0, 100]).</summary>
    public double PercentRemaining { get; }
    /// <summary>API "resets_at" (UTC). Null when the API omits it.</summary>
    public DateTimeOffset? ResetsAt { get; init; }
    /// <summary>Countdown to reset. Null when ResetsAt is null; never negative
    /// (clamped to TimeSpan.Zero when the reset moment has passed).</summary>
    public TimeSpan? TimeUntilReset(DateTimeOffset utcNow);
}

/// <summary>extra_usage block. Every subfield is optional/version-dependent.</summary>
public sealed record ClaudeExtraUsage
{
    public bool IsEnabled { get; init; }
    /// <summary>API "used_credits", in cents. Null when absent.</summary>
    public decimal? UsedCreditsCents { get; init; }
    /// <summary>API "monthly_limit", in cents. Null when absent.</summary>
    public decimal? MonthlyLimitCents { get; init; }
    public string? Currency { get; init; }
}

/// <summary>Immutable result of a poll (or of the cache). Never contains token text.</summary>
public sealed record ClaudeUsageSnapshot
{
    public required ClaudeProviderState State { get; init; }
    /// <summary>UTC instant the underlying data was fetched (cache keeps the original instant).</summary>
    public required DateTimeOffset RetrievedAt { get; init; }
    /// <summary>True when bucket data comes from the cached previous successful poll.</summary>
    public bool IsFromCache { get; init; }
    public ClaudeUsageBucket? FiveHour { get; init; }
    public ClaudeUsageBucket? SevenDay { get; init; }
    /// <summary>Null = no Opus-specific limit reported (e.g. unlimited / not applicable).</summary>
    public ClaudeUsageBucket? SevenDayOpus { get; init; }
    /// <summary>Null = no Sonnet-specific limit reported.</summary>
    public ClaudeUsageBucket? SevenDaySonnet { get; init; }
    public ClaudeExtraUsage? ExtraUsage { get; init; }
    /// <summary>"pro"/"max" from the credentials file, when known.</summary>
    public string? SubscriptionType { get; init; }
    /// <summary>Populated only when State == RateLimited (parsed Retry-After, or fallback).</summary>
    public TimeSpan? RetryAfter { get; init; }
    /// <summary>utcNow - RetrievedAt, clamped to >= TimeSpan.Zero.</summary>
    public TimeSpan DataAge(DateTimeOffset utcNow);
}

/// <summary>Tuning knobs; defaults match docs/CLAUDE_USAGE_API_RESEARCH.md.</summary>
public sealed record ClaudeUsageServiceOptions
{
    public Uri UsageEndpoint { get; init; }              // default https://api.anthropic.com/api/oauth/usage
    /// <summary>Tried in order until one succeeds. Defaults:
    /// [0] https://platform.claude.com/v1/oauth/token, [1] https://console.anthropic.com/v1/oauth/token.</summary>
    public IReadOnlyList<Uri> TokenRefreshEndpoints { get; init; }
    /// <summary>Claude Code public OAuth client id (not a secret):
    /// "9d1c250a-e61b-44d9-88ed-5944d1962f5e".</summary>
    public string OAuthClientId { get; init; }
    /// <summary>Must be of the form "claude-code/&lt;version&gt;"; required to avoid persistent 429s.</summary>
    public string UserAgent { get; init; }               // default "claude-code/2.0.14"
    public TimeSpan MinPollInterval { get; init; }       // default 180 s
    public TimeSpan HttpTimeout { get; init; }           // default 10 s (<= 10 s per CLAUDE.md)
    /// <summary>Additional attempts after the first, for transient (5xx/network) failures only.</summary>
    public int MaxRetries { get; init; }                 // default 2
    /// <summary>Base for exponential backoff + jitter. Tests set TimeSpan.Zero.</summary>
    public TimeSpan RetryBaseDelay { get; init; }        // default 500 ms
}

public interface IClaudeUsageService
{
    /// <summary>Never throws; all failures are expressed via ClaudeUsageSnapshot.State.</summary>
    Task<ClaudeUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default);
}

public sealed class ClaudeUsageService : IClaudeUsageService, IDisposable
{
    public ClaudeUsageService(
        IClaudeCredentialsReader credentialsReader,
        HttpMessageHandler httpMessageHandler,
        TimeProvider timeProvider,
        ClaudeUsageServiceOptions? options = null);
    public Task<ClaudeUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken = default);
    public void Dispose();
}
```

Notes:

- `ClaudeUsageService` owns one `HttpClient` built over the injected handler
  (`disposeHandler: false` is acceptable; the test harness disposes its own handler),
  with `Timeout = options.HttpTimeout`.
- All time arithmetic uses the injected `TimeProvider` (`GetUtcNow()`); no
  `DateTime.Now`/`DateTimeOffset.Now` anywhere (CLAUDE.md Time correctness).
- `GetUsageAsync` must be safe to call from any thread; it performs no UI work.

## 4. Behavior

### 4.1 Poll pipeline (`GetUsageAsync`)

1. **Min-interval gate.** If a previous poll *reached the network* less than
   `MinPollInterval` ago (and, when that poll was `RateLimited`, also less than its
   `RetryAfter`), return the last snapshot with `IsFromCache = true` without any file
   read or network call. `RetrievedAt` stays the original fetch instant so
   `DataAge` grows.
2. **Credential discovery.** Call `IClaudeCredentialsReader.Read()` — a *fresh* read on
   every non-gated poll (the file may have been rotated by a running Claude Code).
   `null` → return a `NotSignedIn` snapshot (`RetrievedAt = now`). No network call is
   made and the min-interval window is **not** started (file reads are cheap).
3. **Proactive refresh.** If `ExpiresAt <= now`: if `RefreshToken` is null/empty →
   `TokenExpired`. Otherwise run the refresh flow (4.2). On failure → `TokenExpired`.
   The refreshed access token is used **in memory only**; the credentials file is never
   rewritten (race with a running Claude Code — see research doc).
4. **Usage request.**
   `GET {UsageEndpoint}` with headers:
   - `Authorization: Bearer <accessToken>`
   - `anthropic-beta: oauth-2025-04-20`
   - `User-Agent: <options.UserAgent>` (required; missing UA ⇒ persistent 429s)
   - `Accept: application/json`
5. **Response handling.**
   - `200` → parse (4.3) → `Ok` snapshot; cache it; start the min-interval window.
   - `401` → one refresh attempt (4.2) + one retried GET with the new token; if
     refresh fails or the retry is `401` again → `TokenExpired`.
   - `403` → `Unavailable` (no refresh attempt; token is valid but not authorized).
   - `429` → `RateLimited`; parse `Retry-After` (delta-seconds or HTTP-date via
     `response.Headers.RetryAfter`); when absent fall back to `MinPollInterval`.
     Honor it: next network poll no earlier than `max(MinPollInterval, RetryAfter)`.
   - `5xx` / `HttpRequestException` / timeout (`TaskCanceledException` not caused by
     the caller's token) → transient: retry up to `MaxRetries` extra attempts with
     exponential backoff + jitter (`RetryBaseDelay * 2^attempt` ± jitter). All
     attempts exhausted → `Unavailable`.
   - Malformed/empty/over-sized body on `200` → `Unavailable` (treat responses as
     untrusted input; bound the payload read to 1 MiB).
6. **Stale-data carry-over.** Any non-`Ok` outcome from steps 3–5, when a previous
   successful snapshot exists, returns that snapshot's buckets/extra-usage with
   `IsFromCache = true`, the *cached* `RetrievedAt`, and `State` set to the new error
   state — the widget shows stale data plus its age. Error polls (after retries) also
   start the min-interval window so failures don't hammer the API.
7. **Never throw.** Every failure mode maps to a state. `OperationCanceledException`
   from the caller's `CancellationToken` is the only allowed propagated exception.
8. **Concurrency.** Overlapping calls must not produce overlapping network polls
   (serialize via `SemaphoreSlim(1,1)` or equivalent); the second caller gets the
   first caller's result or the cache.

### 4.2 Token refresh flow

```
POST {TokenRefreshEndpoints[i]}            (i = 0, then 1 on any failure)
Content-Type: application/json
{ "grant_type": "refresh_token", "refresh_token": "<refreshToken>", "client_id": "<OAuthClientId>" }
```

- Success (`200` with non-empty `access_token`): keep `access_token` (+ rotated
  `refresh_token`, `expires_in` → in-memory `ExpiresAt = now + expires_in seconds`)
  for the remainder of the process lifetime; prefer the in-memory token while it is
  newer than the file's. **Never** write any of it to disk or Credential Manager.
- Each endpoint failure (non-2xx, network error, malformed body) → try the next
  endpoint; all exhausted → refresh failed.
- Refresh requests/responses are subject to the same redaction rules — no token value
  in logs, exceptions, or snapshots.

### 4.3 Response mapping

Wire shape (each bucket nullable; defensive parsing — this is an undocumented API):

```json
{
  "five_hour":        { "utilization": 42.5, "resets_at": "2026-06-10T15:00:00Z" },
  "seven_day":        { "utilization": 80,   "resets_at": "2026-06-14T00:00:00Z" },
  "seven_day_opus":   null,
  "seven_day_sonnet": null,
  "extra_usage":      { "is_enabled": true, "used_credits": 1250, "monthly_limit": 5000, "currency": "USD" }
}
```

| Wire | Model | Rule |
|---|---|---|
| `<bucket>.utilization` | `ClaudeUsageBucket.PercentUsed` | clamp to `[0,100]`; bucket JSON `null`, missing, or missing/non-numeric `utilization` ⇒ model bucket is `null` (treated as "no limit reported"/unlimited) |
| derived | `PercentRemaining` | `100 - PercentUsed` |
| `<bucket>.resets_at` | `ResetsAt` | ISO-8601 UTC; missing/unparseable ⇒ `null` (bucket still valid) |
| `extra_usage` | `ExtraUsage` | object missing/null ⇒ model `null`; every subfield optional (`is_enabled` defaults `false`) |
| n/a | `SubscriptionType` | copied from credentials file |
| unknown fields | — | ignored everywhere |

Countdown display math lives in `ClaudeUsageBucket.TimeUntilReset(utcNow)` (UTC in,
clamped at zero) so view-models never do raw date arithmetic.

## 5. Security & error handling (per CLAUDE.md Security Standards)

- **Redaction.** No token value (access or refresh) may appear in: snapshot fields,
  `ToString()` of any public record, exception messages, or logs. `ClaudeOAuthCredentials.ToString()`
  is overridden to redact. Diagnostic logging of requests must redact the
  `Authorization` header and any `*token*` field.
- **Read-only credential access.** The service opens `.credentials.json` with
  `FileAccess.Read`/`FileShare.ReadWrite` and never writes, moves, or locks it.
- **Hosts.** Usage endpoint host `api.anthropic.com` is allowlisted. ⚠️ The refresh
  hosts `platform.claude.com` and `console.anthropic.com` are **not** in the current
  CLAUDE.md allowlist (`api.anthropic.com`, `api.github.com`). **Orchestrator action
  required:** an ADR (suggested `ADR-0002-Claude-OAuth-Refresh-Hosts`) approving these
  two hosts for the token-refresh flow only must exist before the code task closes.
- **TLS.** Default handler validation untouched; never set
  `ServerCertificateCustomValidationCallback`. (Tests inject a fake handler, which is
  why the service takes `HttpMessageHandler`, not a pre-built `HttpClient`.)
- **Timeouts.** `HttpClient.Timeout = HttpTimeout (≤ 10 s)`; a timed-out request is a
  transient failure (retry/backoff), then `Unavailable`.
- **Graceful degradation.** Provider problems never throw to the caller and never
  crash the app; every state has a defined tooltip meaning (`NotSignedIn` → "Sign in
  via Claude Code", `TokenExpired` → "Re-login in Claude Code", etc.).
- **No persistence/telemetry.** Nothing written to disk, Credential Manager, or any
  endpoint other than the three above.
- **ToS posture.** Conservative polling (≥ 180 s), accurate `claude-code/*` UA, honor
  `Retry-After` — per the research doc's risk notes.

## 6. Test plan (stubs delivered with this spec)

All tests are network-free (fake `HttpMessageHandler`), time-deterministic (injected
`TimeProvider` test double), and use fixtures with fake tokens only.

| Test file | Covers |
|---|---|
| `ClaudeUsage/ClaudeCredentialsReaderTests.cs` | credentials parsing: valid file, missing file, malformed JSON, missing `claudeAiOauth`, missing token, epoch-ms conversion, redacted `ToString` |
| `ClaudeUsage/ClaudeUsageServiceTests.cs` | happy path mapping, required headers, null/unlimited buckets, optional `extra_usage` subfields, missing `resets_at`, clamping, reset-time math, fresh credential read per poll |
| `ClaudeUsage/ClaudeUsageServiceErrorTests.cs` | `NotSignedIn` (no network), proactive refresh success/failure, refresh endpoint fallback, 401→refresh→retry, 401→`TokenExpired`, 403, 429 + `Retry-After`, malformed body, network exception, transient 5xx retry, redaction |
| `ClaudeUsage/ClaudeUsageServiceCachingTests.cs` | min-poll-interval gate, cache expiry, `DataAge`, stale-data carry-over on failure |
| `TestSupport/FakeHttpMessageHandler.cs`, `TestSupport/TestTimeProvider.cs`, `TestSupport/ClaudeTestData.cs` | shared test doubles/helpers (not tests) |

Fixtures (`tests/AgentSubscriptionTracker.Tests/Fixtures/Claude/`): `credentials.valid.json`,
`credentials.expired.json`, `credentials.missing-oauth.json`, `credentials.no-access-token.json`,
`credentials.malformed.json`, `usage.full.json`, `usage.null-buckets.json`,
`usage.missing-fields.json`, `usage.out-of-range.json`, `usage.malformed.json`,
`refresh.success.json`, `error.401.json`, `error.429.json`. All token values are of the
form `fake-...-for-tests...`.

## 7. Acceptance criteria checklist

The code agent must satisfy every item before the task may be marked `done`:

- [ ] All public types/members in §3 exist with exactly those signatures in namespace
      `AgentSubscriptionTracker.App.Services`; spec test stubs compile unmodified.
- [ ] `dotnet build` succeeds with 0 warnings (TreatWarningsAsErrors, AnalysisMode=All);
      any analyzer suppression is in `.editorconfig` with a one-line justification.
- [ ] `dotnet test` passes green, including every test stub shipped with this spec,
      with no live network calls.
- [ ] Credentials are read fresh on each non-gated poll, read-only; the service never
      writes `.credentials.json` or any other file.
- [ ] Missing/unparseable credentials → `NotSignedIn` with zero HTTP requests.
- [ ] Expired `expiresAt` triggers in-memory refresh (primary then fallback endpoint,
      `grant_type=refresh_token` + client id); refresh failure → `TokenExpired`.
- [ ] `401` triggers exactly one refresh + one retry; second `401` or failed refresh →
      `TokenExpired`.
- [ ] Usage GET carries `Authorization: Bearer`, `anthropic-beta: oauth-2025-04-20`,
      and a `claude-code/<version>` User-Agent.
- [ ] All four buckets map per §4.3 (clamping, null = unlimited/absent, optional
      `resets_at`); `extra_usage` subfields all optional; unknown JSON ignored;
      malformed body → `Unavailable`, never an unhandled exception.
- [ ] `429` → `RateLimited` with `RetryAfter` from the header (fallback
      `MinPollInterval`), honored before the next network poll.
- [ ] Transient 5xx/network failures retried ≤ `MaxRetries` with exponential
      backoff + jitter; exhausted → `Unavailable`.
- [ ] Min poll interval (default 180 s) enforced via injected `TimeProvider`; gated
      calls return the cached snapshot (`IsFromCache = true`, original `RetrievedAt`,
      growing `DataAge`); failures after a prior success carry cached buckets with the
      error `State`.
- [ ] `GetUsageAsync` never throws (except caller cancellation) and is safe under
      concurrent calls.
- [ ] No token text in any snapshot, `ToString()`, exception, or log; `ClaudeOAuthCredentials.ToString()`
      redacted; TLS validation untouched; `HttpClient.Timeout ≤ 10 s`.
- [ ] No new NuGet dependency without exact-version pinning (BCL-only strongly preferred).
- [ ] Host-allowlist ADR for `platform.claude.com` / `console.anthropic.com` exists
      (orchestrator/human) before the code task is closed.
- [ ] `docs/IMPLEMENTATION_SUMMARY.md` updated after the implementation phase.
