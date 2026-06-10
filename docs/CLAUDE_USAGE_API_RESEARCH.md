# Claude (Anthropic) Subscription Usage API — Verified Research

> Produced by the research + adversarial-verification workflow, 2026-06-10.
> Status: core findings CONFIRMED by multiple independent sources; corrections from the verifier are merged below.
> This is an **undocumented internal API** (powers Claude Code's `/usage`). Parse defensively; expect change without notice.

## Token source (Windows)

- Path: `%USERPROFILE%\.claude\.credentials.json` (plaintext on Windows — Claude Code does not use Credential Manager there, see anthropics/claude-code#29049).
- Shape: top-level `claudeAiOauth` object with keys: `accessToken`, `refreshToken`, `expiresAt` (epoch **ms**), `scopes` (array), `subscriptionType` (`pro`/`max`), and possibly `rateLimitTier`.
- `%USERPROFILE%\.claude.json` holds account metadata (`oauthAccount`, email, org) but **no tokens**.
- ⚠️ **Not present on this machine at research time.** The widget must degrade to a "Sign in via Claude Code" state when missing.
- Token lifetime varies by version/plan (reports range 1h–24h). Rely **only** on `expiresAt`, never an assumed TTL.

## Usage endpoint

```
GET https://api.anthropic.com/api/oauth/usage
Authorization: Bearer <accessToken>
anthropic-beta: oauth-2025-04-20
User-Agent: claude-code/<version>        ← REQUIRED; without it: persistent 429s
Content-Type: application/json
```

## Response shape

Each bucket = `{ "utilization": number 0-100, "resets_at": ISO-8601 UTC string }`; buckets may be `null`.

| Field | Meaning |
|---|---|
| `five_hour` | current 5-hour session window |
| `seven_day` | weekly limit, all models |
| `seven_day_opus` | weekly Opus limit (nullable) |
| `seven_day_sonnet` | weekly Sonnet limit (nullable) |
| `extra_usage` | `{ is_enabled: bool, used_credits: number?, monthly_limit: number?, currency: string? }` — credits in cents. Any other subfields are version-dependent; parse all subfields as optional. |

Mapping: `percent_used = utilization`; `percent_remaining = 100 - utilization`; absolute counts are **not** exposed. Countdown = `resets_at - utcNow`.

## Token refresh

```
POST https://platform.claude.com/v1/oauth/token   (newer; older form: https://console.anthropic.com/v1/oauth/token — support both)
Content-Type: application/json
{ "grant_type": "refresh_token", "refresh_token": "<refreshToken>", "client_id": "9d1c250a-e61b-44d9-88ed-5944d1962f5e" }
```

Returns new `access_token`, `refresh_token`, `expires_in`. **Prefer NOT rewriting `.credentials.json`** (race with a running Claude Code clobbering a fresher token): re-read the file fresh on each poll; only refresh in-memory when `expiresAt` has passed, and surface "re-login in Claude Code" if refresh fails.

## Operational rules

- Poll no more often than **~180 s per token** (rate limiting is per-token). Cache + show data age.
- Handle `401` → attempt refresh → on failure show provider-unavailable state.
- Known open issue with persistent 429s on this endpoint (anthropics/claude-code#31021) — treat 429 as a soft, retry-later state, honoring `Retry-After`.
- ToS note: reusing the OAuth token outside Claude Code is unofficial; conservative polling + accurate User-Agent reduce risk.

## Sources

- https://github.com/anthropics/claude-code/issues/29049 · #31021 · #34306
- https://github.com/Maciek-roboblog/Claude-Code-Usage-Monitor/issues/202
- https://github.com/robinebers/openusage (docs/providers/claude.md)
- https://gist.github.com/cedws/3a24b2c7569bb610e24aa90dd217d9f2
- https://code.claude.com/docs/en/authentication
