# GitHub Copilot Setup — AgentSubscriptionTracker

How the widget reads your GitHub Copilot quota, how to sign in so it can find a token,
and what to do when it shows an error state. Companion to
`docs/specs/SPEC-0002-copilot-quota-service.md` and
`docs/COPILOT_QUOTA_API_RESEARCH.md`.

---

## Important: unofficial API / Terms-of-Service note

The widget reads quota data from `GET https://api.github.com/copilot_internal/user`,
an **undocumented internal GitHub endpoint**. There is no documented REST endpoint for
an individual to read their own premium-request usage, so this is effectively the only
programmatic option for a personal subscription.

- The schema is **unversioned and can change without notice**; the widget degrades to an
  "Unavailable" state instead of crashing when it does.
- The widget **reuses a token minted by another Copilot client** (Copilot CLI, JetBrains,
  or Neovim plugin). Reusing another client's token is unofficial and not covered by
  GitHub's documented API terms — use at your own discretion.
- The widget is strictly **read-only**: it never writes to Credential Manager or to any
  credential file, never persists your token, and only ever talks to `api.github.com`
  over HTTPS.

## How the widget finds your token

The token discovery chain (first hit wins, all in memory only):

1. **Windows Credential Manager**, generic credential with service name `copilot-cli`
   (written by the modern Copilot CLI).
2. `%LOCALAPPDATA%\github-copilot\apps.json` (CLI / JetBrains / Neovim sign-ins).
3. `%LOCALAPPDATA%\github-copilot\hosts.json`.
4. `%USERPROFILE%\.config\github-copilot\apps.json`, then `hosts.json` (older plugins).
5. `%USERPROFILE%\.copilot\config.json` (Copilot CLI plaintext fallback).

VS Code stores its Copilot token in encrypted secret storage; the widget does **not**
(and cannot) read it. Signing in to Copilot inside VS Code alone is not enough.

## Sign-in paths

Pick any one of these; afterwards the widget will discover the token automatically on
its next refresh (≤ 30 s debounce):

- **Copilot CLI (recommended)** — install the GitHub Copilot CLI and run its login flow
  (`copilot` → `/login`). It stores the token in Windows Credential Manager under
  `copilot-cli`. See
  <https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/authenticate-copilot-cli>.
- **JetBrains IDE plugin** — sign in to GitHub Copilot in any JetBrains IDE; it writes
  `%LOCALAPPDATA%\github-copilot\apps.json`.
- **Neovim plugin** — `:Copilot auth` writes `apps.json`/`hosts.json` under
  `%LOCALAPPDATA%\github-copilot\` (or `~/.config/github-copilot/`).

## Troubleshooting

| Widget state | Meaning | Fix |
|---|---|---|
| Not signed in | No token found anywhere in the chain, or the token was rejected (401: expired/revoked). | Sign in via one of the paths above (Copilot CLI recommended). |
| Forbidden (403) | The token authenticated but was refused for this endpoint. **Known cause: stale tokens from old Neovim `hosts.json` sign-ins.** | Re-sign-in with the Copilot CLI or a JetBrains IDE — tokens they mint are reliable for this endpoint. Optionally delete the stale `hosts.json`. |
| Rate limited | GitHub returned 429. | Nothing to do — the widget honors `Retry-After` and refreshes automatically afterwards. |
| Unavailable | Network error, timeout, 5xx, or an unexpected response shape (the internal API may have changed). | Check connectivity; if persistent, the endpoint schema may have changed — file an issue. |

The widget shows last-known-good numbers (marked as cached/stale) alongside any error
state, so a transient failure never blanks the display.
