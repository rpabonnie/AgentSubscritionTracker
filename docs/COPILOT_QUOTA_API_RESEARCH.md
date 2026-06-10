# GitHub Copilot Quota API — Verified Research

> Produced by the research + adversarial-verification workflow, 2026-06-10.
> Verifier verdict: **confirmed** (field names corroborated verbatim, e.g. zed-industries/zed#44499).
> This is an **undocumented internal API**; there is NO documented REST endpoint for an individual to read their own premium-request usage — this is effectively the only programmatic option for a personal subscription.

## Token source (Windows) — discovery chain, in order

1. **Windows Credential Manager**, service name `copilot-cli` (the modern Copilot CLI default store).
2. `%LOCALAPPDATA%\github-copilot\apps.json` — keys like `"github.com:Iv1.<appid>"` → `{ user, oauth_token, githubAppId }` (written by CLI/JetBrains/Neovim sign-ins).
3. `%LOCALAPPDATA%\github-copilot\hosts.json` — key `"github.com"` → `{ user, oauth_token }`.
4. `%USERPROFILE%\.config\github-copilot\apps.json|hosts.json` (older plugins).
5. `%USERPROFILE%\.copilot\config.json` (Copilot CLI plaintext fallback when keychain unavailable).

- VS Code stores its token in its own **encrypted secret storage** — not readable; do not attempt.
- ⚠️ **None of these exist on this machine at research time** (verified independently by two agents). Widget must show a "Sign in via Copilot CLI (or JetBrains/Neovim plugin)" state when the chain comes up empty.
- Tokens from old Neovim `hosts.json` sometimes get `403 forbidden` on this endpoint; CLI/JetBrains-minted tokens are more reliable.

## Quota endpoint (individual / individual_pro)

```
GET https://api.github.com/copilot_internal/user
Authorization: token <oauth_token>        ← 'token' scheme for this endpoint, NOT 'Bearer'
Editor-Version: vscode/1.104.1
Editor-Plugin-Version: copilot-chat/0.26.7
User-Agent: GitHubCopilotChat/0.26.7
Copilot-Integration-Id: vscode-chat
Accept: application/json
```

No token exchange needed for individual plans (`copilot_internal/v2/token` is for minting short-lived chat tokens / enterprise flows — out of scope for quota reads). Do not send `X-GitHub-Api-Version` (unverified for this endpoint).

## Response shape

Top-level: `copilot_plan` (`individual`/`individual_pro`/`business`), `chat_enabled`, `quota_reset_date` (`YYYY-MM-DD`), `quota_reset_date_utc`, `endpoints.api`, and `quota_snapshots`.

`quota_snapshots` keys: `chat`, `completions`, `premium_interactions`. Each snapshot:

| Field | Meaning |
|---|---|
| `entitlement` | monthly allowance (int) |
| `remaining` | remaining this month (int) |
| `quota_remaining` | remaining (float) |
| `percent_remaining` | 0–100 (float) |
| `unlimited` | bool — when true, ignore numeric fields and display "Unlimited" |
| `overage_count` / `overage_permitted` | overage state |
| `quota_id`, `timestamp_utc` | metadata |

Mapping: display names `premium_interactions` → "Premium requests", `completions` → "Code completions", `chat` → "Chat". `used = entitlement - remaining`; refill date = `quota_reset_date`.

## Operational rules

- Handle `401`/`403`/`404` and a missing credential gracefully → provider-unavailable state with sign-in guidance.
- Conservative polling with caching (mouseover-driven, debounced ≥30 s; honor `Retry-After` on 429).
- Schema is unversioned and can change without notice — strict-but-tolerant deserialization (unknown fields ignored, missing fields → null, never throw on absent bucket).
- ToS note: reading another client's token is unofficial; surface this in docs/setup.

## Sources

- https://github.com/zed-industries/zed/discussions/44499 (response schema verbatim)
- https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/authenticate-copilot-cli (keychain `copilot-cli`)
- https://docs.litellm.ai/docs/providers/github_copilot (headers)
- https://github.com/kasuken/vscode-copilot-insights · https://github.com/caozhiyuan/copilot-api
- https://docs.github.com/en/billing/concepts/product-billing/github-copilot-premium-requests
