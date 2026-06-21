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
  → **Failed 2026-06-11**; findings fixed in the TASK-012 phase below.
- "Start with Windows" context-menu toggle deferred (spec-optional for v1, code TODO in App.xaml.cs).

## Phase: TASK-012/013 — TASK-011 acceptance fixes + auth-config analysis (2026-06-11)

Human acceptance found Claude falsely "unavailable", Copilot falsely "not signed in", and no
way to operate the app from the callout. Diagnosis was verified live (see ADR-0003 and
memory.md 21:40 entry).

### Delivered

| Change | Files |
|---|---|
| **Budget semantics fix** (SPEC-0003 §4.4 amendment): the 2 s per-provider budget no longer cancels in-flight fetches; on elapse the refresh pass returns (previous presentation kept) and the fetch publishes itself on completion. Fixes the expired-token OAuth-refresh path that produced a sticky false "Unavailable". | `ViewModels/TrayViewModel.cs` |
| **gh CLI token discovery** (SPEC-0002 §3 steps 6–7): Credential Manager `gh:github.com:` keyring entry (verified: raw gh OAuth token, accepted by `copilot_internal/user` with HTTP 200) and `%APPDATA%\GitHub CLI\hosts.yml` line-scan fallback. New `CopilotTokenSource.GhCliCredentialManager/GhCliHostsFile`, `CopilotTokenProviderOptions.RoamingAppDataPath`. | `Services/CopilotTokenProvider.cs`, `Services/CopilotQuotaContracts.cs` |
| **Callout command row** (SPEC-0003 §5.2 amendment): footer Refresh (manual refresh) + Exit (clean shutdown) buttons; the overlay alone operates the app, the tray context menu stays as a secondary affordance. | `Views/CalloutWindow.xaml(.cs)`, `App.xaml.cs` |
| Sign-in guidance now names `gh auth login` in the Copilot failure messages. | `Services/CopilotQuotaService.cs` |
| **ADR-0003**: no token-entry settings page (PATs are rejected by `copilot_internal`; Claude usage is OAuth-only) — zero-config discovery + actionable messages instead; device-flow OAuth deferred. | `docs/adr/ADR-0003-Auth-Configuration-Strategy.md`, `docs/COPILOT_SETUP.md` |

### Verification

- `dotnet build` — 0 warnings; `dotnet test` — **158/158 passed** (6 new: 4 gh-discovery,
  2 budget-semantics regression tests).
- Live probe on the failing machine: gh keyring token == `gh auth token`; `copilot_internal/user`
  → HTTP 200, `individual_pro`, full `quota_snapshots`.
- Smoke test: app launches, second instance forces the callout open (triggering a live
  refresh incl. the real Claude OAuth refresh path) and the app stays running.

### Open items

- TASK-014 human re-acceptance: hover the tray icon — Claude data may take a few seconds on
  the first open (token refresh) and should appear while the callout is open; Copilot should
  show quota via the gh token; Refresh/Exit buttons in the callout footer.
  → **Approved 2026-06-19**.

## Phase: TASK-016 — SPEC-0004 callout theming engine (2026-06-19)

### Delivered

| File | Contents |
|---|---|
| `src/AgentSubscriptionTracker.App/Theming/ThemeManifest.cs` | `ThemeManifest`, `ThemeBackground`, `ThemeFontSet`, `ThemeFont`, `ThemeBrushSet`, `ThemeSeverityBands` records |
| `src/AgentSubscriptionTracker.App/Theming/ThemeManifestSerializer.cs` | `IThemeManifestSerializer`/`ThemeManifestSerializer` — strict, bounded `System.Text.Json` parse/serialize; whole-file quarantine on malformed JSON/missing field/invalid color/out-of-range size/unsupported schema version/path-traversal `imagePath` |
| `src/AgentSubscriptionTracker.App/Theming/ThemePathResolver.cs` | static `imagePath` containment helper (`TryResolveContained`, syntax-only `IsSyntacticallySafe`) — rejects `..`, absolute paths, UNC, URI schemes |
| `src/AgentSubscriptionTracker.App/Theming/ThemeBackgroundImageValidator.cs` | `IThemeBackgroundImageValidator`/`ThemeBackgroundImageValidator` — containment → exists → size (≤30 MB, configurable) → PNG signature → structural IEND-completeness → header dimensions (≤8192×8192, configurable; read before decode so an oversized image degrades instead of OOM-crashing) → real-alpha `PixelFormat` check → bounded full pixel-buffer decode (truncation guard), in that short-circuiting order |
| `src/AgentSubscriptionTracker.App/Theming/ThemeFontResolver.cs` | `IThemeFontResolver`/`ThemeFontResolver` — `SystemFontFamilies` lookup with Segoe UI fallback; weight-string mapping defaulting to `FontWeights.Normal` |
| `src/AgentSubscriptionTracker.App/Theming/ThemeLoader.cs` | `IThemeLoader`/`ThemeLoader` — discovery, built-in re-seed from embedded resources, duplicate-id quarantine (slug-collision detection), degrade-vs-quarantine policy, absolute in-code fallback pair |
| `src/AgentSubscriptionTracker.App/Theming/ThemeStore.cs` | `IThemeStore`/`ThemeStore` — Save-As (unique-slug fork), in-place `Overwrite` (throws for built-ins), `Delete` |
| `src/AgentSubscriptionTracker.App/Views/CalloutWindow.xaml(.cs)` (modified) | `SeverityToBrushConverter` now resolves `ok`/`warn`/`critical`/`BarTrackBrush` from the bound element's resource scope (`IValueConverter` + `IMultiValueConverter`) instead of static converter-owned colors; new `BarTrackBrushMultiConverter`; progress bar binds via `MultiBinding`+`RelativeSource Self` so the converter can resolve from its own scope |
| `src/AgentSubscriptionTracker.App/Themes/Dark.xaml`, `Light.xaml` (modified) | added `ok`/`warn`/`critical` resource keys consumed by the modified converter |
| `src/AgentSubscriptionTracker.App/Assets/Themes/light.theme.json`, `dark.theme.json` | embedded built-in manifests (`EmbeddedResource`, no background image) |

### Deferred to TASK-017/018/019 (shell-level, no unit test stub exists)

Per SPEC-0004 §5/§7, the theme button, `ThemePickerPopup`, `ThemeEditorWindow`,
`ThemePickerViewModel`, and `ThemeEditorViewModel` are shell/UI surface — "no window is
ever shown" by the SPEC-0004 test stubs, consistent with how SPEC-0003 treats
`TrayIconHost`/`CalloutController`/`CalloutWindow`. TASK-016 delivers the full theming
*engine* (manifest model, parsing, image/font validation, loader, store, theme-aware
severity/track-brush resolution) that those views will bind to; the views themselves are
explicitly out of this task's automated-test surface and are called out as an open item
for QA/security/review (TASK-017/018/019) to confirm before sign-off.

### Notable implementation decisions beyond the literal spec text

- **Duplicate-theme-id slug rule**: `ThemeLoaderTests` pins a folder-name-derived id that
  must collide for two *different* folder names ("custom-1" vs.
  "custom-1-duplicate-marker"). Implemented as: lowercase the folder name, and if it
  matches `^[a-z0-9]+(-[a-z0-9]+)*?-\d+`, truncate to that match; otherwise use the full
  lowercased folder name unchanged. Ordinary folder names (no numeric suffix) are
  unaffected.
- **Missing background file vs. corrupt background file**: a manifest referencing a
  `background.png` that was never written, or one that *was* present but is now
  corrupt/moved/deleted, both load as `DegradedMissingOrInvalidImage` (FallbackColor
  only) per SPEC-0004 §4.4 row 4 — `BackgroundImageValidationError.FileNotFound` maps to
  the same degraded status as every other validation error (corrupt, oversized, no-alpha,
  wrong format, path-escape). An earlier revision of `ThemeLoader` special-cased
  `FileNotFound` to `Ok`/no-image instead; TASK-021 (QA-0004 follow-up) corrected this and
  added `ThemeLoaderTests.LoadAll_BackgroundImageMissingFromDisk_DegradesTheme_DoesNotQuarantineIt`
  to pin the literal missing-file scenario, distinct from the existing
  corrupt/truncated-PNG degrade test.
- **Truncated-PNG detection**: WPF's `PngBitmapDecoder` silently zero-fills missing
  scanlines for a mid-stream-truncated file rather than throwing, even after a forced
  `CopyPixels`. Added a structural check (file's last 8 bytes must be the canonical
  zero-length `IEND` chunk type+CRC) before decode, which reliably catches the
  `truncated.png` fixture while leaving well-formed PNGs (including the oversized-file-size
  fixture, which fails the earlier size check before ever reaching this one) unaffected.
- **`ThemeManifest.Fonts.Size` round-trip of `NaN`**: one out-of-range test serializes a
  manifest containing `double.NaN` and re-parses it. `System.Text.Json` cannot write raw
  JSON `NaN`; `FontDto.Size` opts into
  `JsonNumberHandling.AllowNamedFloatingPointLiterals` (writes the JSON string `"NaN"`),
  and the manual `JsonDocument`-based parser accepts that one string shape for `size`
  before applying the same `OutOfRangeValue` rule used for in-range numeric values.

### Test/build infrastructure changes

- `tests/AgentSubscriptionTracker.Tests.csproj`: added
  `NoWarn>CA1031;CA1054;CA1062;CA1307;CA1308;CA1859` — these design/globalization analyzer
  rules fire on `[Theory]` test methods and test-only factory helpers (public-API-shaped
  rules with no product-code value inside the test assembly); `TreatWarningsAsErrors`
  stays on for genuine compiler diagnostics.
- `tests/AgentSubscriptionTracker.Tests/Theming/ThemeTestSupport.cs`: added
  `RunOnSta(Action)` — WPF `FrameworkElement` construction requires an STA thread; xUnit's
  default test thread is MTA. `SeverityToBrushConverterThemeTests` wrap their bodies in
  `RunOnSta` rather than constructing a `FrameworkElement` directly on the test thread.
- Filled in the `CreateSerializer`/`CreateValidator`/`CreateResolver`/`CreateLoader`
  `NotImplementedException` factory stubs in each `*Tests.cs` file to return the new
  concrete implementations — the only edits made to spec-writer's test files; no
  assertions were changed.
- `src/AgentSubscriptionTracker.App.csproj`: added `EmbeddedResource` entries for the two
  built-in `*.theme.json` assets (logical names under
  `AgentSubscriptionTracker.App.Assets.Themes.*`).

### Verification

- `dotnet build` — 0 warnings, 0 errors (TreatWarningsAsErrors, AnalysisMode=All), whole solution.
- `dotnet test` — **225/225 passed** (73 new SPEC-0004 theming tests; zero regressions in
  the existing 152 SPEC-0001/0002/0003 tests); no live network, no window shown.

### Open items (as of TASK-016, superseded — see TASK-020/021 below)

- `ThemePickerPopup`, `ThemeEditorWindow`, `ThemePickerViewModel`, `ThemeEditorViewModel`
  (callout theme button, non-activating picker, live-preview editor) are not yet
  implemented — SPEC-0004 §5/§8 explicitly scopes these to manual verification/code
  review rather than automated test stubs. Recommend a follow-up task before TASK-019
  review closes if the UI surface is required for this milestone, or an explicit
  `human_checkpoint`/scope note if it is deferred past this phase.

## Phase: TASK-020 — SPEC-0004 theme picker/editor shell (2026-06-19)

### Delivered

The shell/UI surface deferred above is now wired up end to end:

- `App.xaml.cs` composition root: loads/re-seeds the theme repository and applies the
  active theme at startup (`ApplyInitialTheme`), re-applies the OS-appropriate built-in on
  `WM_SETTINGCHANGE` when the user has not made an explicit choice yet
  (`ApplyOsThemeIfNoUserChoiceYet`), and owns the picker/editor wiring
  (`OnThemeButtonClicked`, `OnThemeSelected`, `OnAddNewTheme`, `OpenEditor`/`OpenEditorAsNew`,
  `ShowEditor`).
- `CalloutContent.xaml`/`CalloutWindow` footer Theme button raises `ThemeRequested`,
  opening a real `ThemePickerPopup` (non-activating, same `ShowActivated=False`/
  `AllowsTransparency=True` family as `CalloutWindow`) backed by `ThemePickerViewModel`.
- `ThemePickerPopup`/`ThemePickerViewModel`: active-theme-first-then-alphabetical
  ordering, duplicate-display-name disambiguation, ellipsis truncation for long names,
  Select/Edit per row, separated "Add new theme" entry.
- `ThemeEditorWindow`/`ThemeEditorViewModel`: real (non-activating-suppressing) window
  hosting the live `CalloutContent` preview bound to a `ResourceDictionary` scoped to the
  preview element only; background image picker + Clear; font family dropdown restricted
  to `SystemFontFamilies`; one color picker per brush key and per severity band;
  non-blocking low-contrast warning; Save (validate → persist via `IThemeStore` → live-update
  the running callout if the saved theme is active) / Cancel (zero disk writes).
- 7 of the 9 SPEC-0004 §4.4 edge cases that were blocked pending this shell are now
  implemented: #9 (source folder deleted mid-edit falls through to Save-As), #12
  (duplicate display names disambiguated, never merged), #16 (editor pins/suppresses the
  callout's hide-on-blur/auto-hide while open), #17 (idempotent resource-scope swap on
  theme switch), #18 (low-contrast warning), #19 (long-name ellipsis + tooltip), #20
  (invalid/oversized PNG at import time surfaces an inline `ImportError`, never corrupts
  the on-disk theme or crashes the thumbnail renderer).
- `ThemeStoreTests`: 16 direct unit tests covering `SaveAsNew`/`Overwrite`/`Delete`,
  unique-slug generation, and the built-in-route-through-`SaveAsNew` rule.

### Verification

- `dotnet build`/`dotnet test` green, no regressions.

## Phase: TASK-021 — QA-0004 follow-up (2026-06-19)

Targeted fixes for the findings in `docs/qa/QA-0004-theming.md`'s second (failed) gate run:

- **High**: `ThemeLoader.LoadOneFolder` now maps a missing/moved background image
  (`BackgroundImageValidationError.FileNotFound`) to `ThemeLoadStatus.DegradedMissingOrInvalidImage`
  per SPEC-0004 §4.4 row 4, instead of `Ok`. Added
  `ThemeLoaderTests.LoadAll_BackgroundImageMissingFromDisk_DegradesTheme_DoesNotQuarantineIt`
  to pin the literal missing-file scenario (distinct from the pre-existing
  corrupt/truncated-PNG degrade test).
- **Medium**: this document updated to close out TASK-020 instead of still listing the
  picker/editor as a future open item (see the TASK-020/TASK-021 sections above).
- **Medium**: SPEC-0004 §4.4 edge case #11 (refuse deleting the active theme, with a
  message) now has a real UI affordance — `ThemePickerPopup` gained a per-row Delete
  button; `ThemePickerViewModel.TryDelete` refuses (no disk write, message returned) for
  the active theme and for built-ins, otherwise calls `IThemeStore.Delete` and the shell
  (`App.xaml.cs`) re-opens the picker against a fresh `LoadAll` afterward.
- **Low**: added `ThemePickerViewModelTests` (ordering, disambiguation, truncation, delete
  refusal rules) and `ThemeEditorViewModelTests` (Save routing to `SaveAsNew` vs.
  `Overwrite`, low-contrast computation, background-image import validation) as direct
  unit coverage of view-model logic that was previously only exercised indirectly via the
  engine layer.

### Verification

- `dotnet build`/`dotnet test` green, no regressions (see test run recorded in `memory.md`'s
  TASK-021 entry for the exact pass count).
