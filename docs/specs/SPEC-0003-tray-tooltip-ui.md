# SPEC-0003 — Tray Icon + Tooltip Callout UI

| | |
|---|---|
| **Task** | TASK-005 (spec) → TASK-008 (code) |
| **Author** | spec-writer |
| **Date** | 2026-06-10 |
| **Status** | Ready for implementation |
| **Depends on** | SPEC-0001 (`IClaudeUsageService`), SPEC-0002 (`ICopilotQuotaService`) — consumes their public contracts verbatim |
| **Related** | CLAUDE.md Security Standards + Engineering Best Practices; ADR-0001 |
| **Test stubs** | `tests/AgentSubscriptionTracker.Tests/Tray/` + fixtures under `tests/AgentSubscriptionTracker.Tests/Fixtures/Tray/` |

---

## 1. Scope

### In scope

- A Windows notification-area (tray) icon for the app, implemented with **raw `Shell_NotifyIcon` P/Invoke** (decision + justification in §2), including: re-add on Explorer restart (`TaskbarCreated`), guaranteed removal on exit (no ghost icon), context menu (*Refresh now* / *Start with Windows* / *Exit*), and a checked-in generated `.ico` asset.
- A **borderless, topmost, rounded-corner WPF callout window** that opens on tray mouse-hover (short delay) and closes when the mouse leaves both the icon and the callout. DPI-aware positioning next to the tray icon, correct on multi-monitor setups. Dark/light styling following the Windows app theme.
- Callout content: two provider sections —
  - **Claude**: 5-hour / weekly / Opus / Sonnet usage bars, reset countdowns, extra-usage credits;
  - **GitHub Copilot**: premium requests / chat / completions bars (or "Unlimited"), monthly reset date —
  plus provider-state messages ("Sign in via Claude Code CLI", …) and a data-age footer.
- **MVVM**: all displayable logic lives in view-models (`TrayViewModel`, `ProviderViewModel`, `UsageBarViewModel`, `UsageFormatting`) that are fully testable **without showing any window**: bar percentages, value/countdown/data-age strings, severity, state messages, and refresh orchestration over `IClaudeUsageService` / `ICopilotQuotaService` with an injected `TimeProvider`.
- Hover-triggered background refresh: cached data shown instantly, refresh respects per-service minimum intervals and a ≤ 2 s hover budget (CLAUDE.md Hover refresh budget).
- Single-instance enforcement via a named mutex.

### Out of scope

- Fetching/parsing/caching usage data, retry/backoff against the network, and token handling — entirely SPEC-0001/SPEC-0002. The UI layer makes **no network calls** and never sees a token.
- A settings window, localization (UI strings are invariant English), animations beyond what styles give for free.
- UI automation tests. The shell layer (§5) is verified by the TASK-011 human checkpoint; everything that computes or formats is in view-models and unit-tested.

### Cross-spec contract

This spec **consumes** (and must not redefine) these SPEC-0001/0002 types:

- `AgentSubscriptionTracker.App.Services`: `IClaudeUsageService`, `ClaudeUsageSnapshot`, `ClaudeUsageBucket`, `ClaudeExtraUsage`, `ClaudeProviderState`, `ICopilotQuotaService`.
- `AgentSubscriptionTracker.App.Models`: `CopilotQuotaSnapshot`, `CopilotQuotaBucket`, `CopilotProviderState`.

Both services already implement min-interval gating, caching, stale-data carry-over (`IsFromCache`), retry/backoff, `Retry-After`, and never-throw semantics. The view-model layer builds on those guarantees and adds its own UI-side gating (§4.4) so a mouse hovering repeatedly does not even invoke the services.

---

## 2. Decision: raw `Shell_NotifyIcon` P/Invoke (not WinForms `NotifyIcon`)

Two BCL-only options exist (third-party tray packages such as H.NotifyIcon are excluded by the supply-chain rule). **Chosen: raw `Shell_NotifyIcon` via P/Invoke**, because:

1. **Purpose-built hover callbacks.** With `NOTIFYICON_VERSION_4` (`NIM_SETVERSION`) the shell sends `NIN_POPUPOPEN` / `NIN_POPUPCLOSE` exactly when a custom rich tooltip should open/close — precisely this feature. WinForms `NotifyIcon` exposes neither; hover would need `MouseMove` polling anyway.
2. **Precise placement.** `Shell_NotifyIconGetRect` returns the icon's screen rectangle for DPI-aware callout positioning. `NotifyIcon` does not expose it (same P/Invoke would be needed regardless).
3. **No second UI framework.** `<UseWindowsForms>true</UseWindowsForms>` loads all of WinForms into a WPF process for one icon — larger footprint and WinForms/WPF DPI + menu-theming mismatches (its `ContextMenuStrip` ignores the WPF theme).
4. **Same obligations either way.** `TaskbarCreated` re-registration and disposal discipline must be hand-written even with `NotifyIcon`.

Cost: ~200 lines of contained interop (`NOTIFYICONDATAW`, `Shell_NotifyIconW`, `Shell_NotifyIconGetRect`, `RegisterWindowMessageW`, cursor/monitor helpers) in one class on a hidden `HwndSource` window. Accepted; it is not unit-tested (no UI automation) and is exercised by the TASK-011 human checkpoint.

---

## 3. Files to create (code agent)

| File | Contents |
|---|---|
| `src/AgentSubscriptionTracker.App/ViewModels/TrayViewModel.cs` | `TrayViewModel` |
| `src/AgentSubscriptionTracker.App/ViewModels/ProviderViewModel.cs` | `ProviderViewModel`, `ProviderDisplayState` |
| `src/AgentSubscriptionTracker.App/ViewModels/UsageBarViewModel.cs` | `UsageBarViewModel`, `UsageSeverity` |
| `src/AgentSubscriptionTracker.App/ViewModels/RefreshPolicy.cs` | `RefreshPolicy`, `RefreshTrigger` |
| `src/AgentSubscriptionTracker.App/ViewModels/UsageFormatting.cs` | `UsageFormatting` |
| `src/AgentSubscriptionTracker.App/Tray/TrayIconHost.cs` | `Shell_NotifyIcon` interop host (§5.1) |
| `src/AgentSubscriptionTracker.App/Tray/CalloutController.cs` | hover open/close + positioning (§5.2–5.3) |
| `src/AgentSubscriptionTracker.App/Tray/ThemeDetector.cs` | dark/light detection (§5.4) |
| `src/AgentSubscriptionTracker.App/Views/CalloutWindow.xaml(.cs)` | the callout window (§5.2) |
| `src/AgentSubscriptionTracker.App/Themes/Dark.xaml`, `Themes/Light.xaml` | theme resource dictionaries |
| `src/AgentSubscriptionTracker.App/Assets/AppIcon.ico` + `Assets/generate-icon.ps1` | icon asset + its generator (§5.6) |

`App.xaml(.cs)` becomes the composition root (§5.5); `MainWindow` is no longer shown and may be deleted by the code agent (it is scaffold-only).

Namespaces: view-models in `AgentSubscriptionTracker.App.ViewModels`; shell classes in `AgentSubscriptionTracker.App.Tray` / `.Views`. Types referenced by tests or XAML bindings are `public` — if analyzer CA1515 ("make public types internal") fires for the WinExe project, suppress it in `.editorconfig` with the one-line justification *"public surface required for WPF data binding and the test assembly"*.

---

## 4. Public type contracts (binding — test stubs compile against exactly these)

```csharp
namespace AgentSubscriptionTracker.App.ViewModels;

/// <summary>What triggered a refresh request.</summary>
public enum RefreshTrigger
{
    /// <summary>Tray mouse-hover: honors per-service minimum intervals.</summary>
    Hover,
    /// <summary>Context-menu "Refresh now": bypasses the view-model gate (services may still serve cache).</summary>
    Manual,
}

/// <summary>UI-side refresh gating. Defaults mirror the service-side minimums.</summary>
public sealed record RefreshPolicy
{
    public TimeSpan ClaudeMinInterval { get; init; }     // default 180 s (SPEC-0001 MinPollInterval)
    public TimeSpan CopilotMinInterval { get; init; }    // default 30 s  (SPEC-0002 MinRefreshInterval)
    /// <summary>Per-provider hover budget; a provider exceeding it keeps its previous data. Default 2 s (CLAUDE.md).</summary>
    public TimeSpan PerProviderTimeout { get; init; }
    public static RefreshPolicy Default { get; }
}

/// <summary>Severity bucket for bar coloring.</summary>
public enum UsageSeverity
{
    Normal,    // PercentUsed <  75
    Warning,   // 75 <= PercentUsed < 90
    Critical,  // PercentUsed >= 90
}

/// <summary>One usage/quota progress bar. Immutable.</summary>
public sealed record UsageBarViewModel(
    string Label,
    double PercentUsed,        // clamped to [0, 100]; 0 when IsUnlimited
    bool IsUnlimited,
    string ValueText,          // "63% used" | "945 of 1500 left" | "35% left" | "Unlimited"
    string? ResetText,         // "Resets in 2 h 30 m" | null
    UsageSeverity Severity);

/// <summary>Connectivity presentation of one provider section (drives icon/color).</summary>
public enum ProviderDisplayState
{
    Loading,            // no refresh attempted yet
    Ok,
    SignedOut,          // NotSignedIn → "Sign in via …"
    AttentionRequired,  // Claude TokenExpired / Copilot Forbidden → re-login guidance
    RateLimited,
    Unavailable,
}

/// <summary>Immutable presentation of one provider section. Rebuilt on every refresh.</summary>
public sealed class ProviderViewModel
{
    public string ProviderName { get; }                       // "Claude" | "GitHub Copilot"
    public ProviderDisplayState DisplayState { get; }
    /// <summary>Null when DisplayState == Ok; otherwise the user-facing state message (§4.3). Never contains a token.</summary>
    public string? StateMessage { get; }
    /// <summary>"Pro" / "Max" / "Individual" / "Business" / raw plan string; null when unknown.</summary>
    public string? PlanText { get; }
    public bool HasData { get; }                              // Bars.Count > 0
    /// <summary>True when the numbers shown come from a cached/stale snapshot.</summary>
    public bool IsStale { get; }
    public IReadOnlyList<UsageBarViewModel> Bars { get; }
    /// <summary>Claude: extra-usage credits line. Copilot: monthly reset + overage line. Null when nothing to show.</summary>
    public string? FooterText { get; }
    /// <summary>UTC instant of the underlying snapshot; null only for CreateEmpty.</summary>
    public DateTimeOffset? RetrievedAtUtc { get; }

    /// <summary>Pre-first-refresh placeholder: Loading, "Loading…", no bars.</summary>
    public static ProviderViewModel CreateEmpty(string providerName);
    public static ProviderViewModel ForClaude(Services.ClaudeUsageSnapshot snapshot, DateTimeOffset utcNow);
    public static ProviderViewModel ForCopilot(Models.CopilotQuotaSnapshot snapshot, DateTimeOffset utcNow);
}

/// <summary>Root view-model bound by the callout window. Not thread-safe: shell calls it on the dispatcher thread.</summary>
public sealed class TrayViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public TrayViewModel(
        Services.IClaudeUsageService claudeService,
        Services.ICopilotQuotaService copilotService,
        TimeProvider timeProvider,
        RefreshPolicy? policy = null);                        // null → RefreshPolicy.Default

    public ProviderViewModel Claude { get; }                  // starts as CreateEmpty("Claude")
    public ProviderViewModel Copilot { get; }                 // starts as CreateEmpty("GitHub Copilot")
    public bool IsRefreshing { get; }
    /// <summary>"No data yet" | "Updated just now" | "Updated 42 s ago" | … (§4.2). Recomputed on read via TimeProvider.</summary>
    public string DataAgeText { get; }

    /// <summary>Refresh orchestration (§4.4). Never throws except caller-token cancellation.</summary>
    public Task RequestRefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken = default);

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Pure, culture-invariant display formatting. All strings are English/invariant by design (no localization in scope).</summary>
public static class UsageFormatting
{
    /// <summary>"Resets soon" (&lt;= 0) | "Resets in &lt;1 m" | "Resets in 47 m" | "Resets in 2 h 05 m" | "Resets in 3 d 19 h" (§4.2).</summary>
    public static string FormatCountdown(TimeSpan timeUntilReset);
    /// <summary>"No data yet" (null) | "Updated just now" | "Updated 42 s ago" | "Updated 5 m ago" | "Updated 3 h ago" | "Updated 2 d ago" (§4.2).</summary>
    public static string FormatDataAge(DateTimeOffset? lastRetrievedUtc, DateTimeOffset utcNow);
    /// <summary>"Resets Jul 1" — invariant "MMM d".</summary>
    public static string FormatMonthlyReset(DateOnly resetDate);
    /// <summary>"63% used" — rounded to the nearest integer, MidpointRounding.AwayFromZero.</summary>
    public static string FormatPercentUsed(double percentUsed);
}
```

### 4.1 Snapshot → `ProviderViewModel` mapping

**Claude** (`ForClaude`): bars in fixed order, `null` bucket ⇒ bar omitted.

| Bucket | Label |
|---|---|
| `FiveHour` | `5-hour session` |
| `SevenDay` | `Weekly (all models)` |
| `SevenDayOpus` | `Weekly (Opus)` |
| `SevenDaySonnet` | `Weekly (Sonnet)` |

- `PercentUsed` = bucket value clamped to `[0,100]` (defensive re-clamp; services already clamp). `ValueText = FormatPercentUsed(clamped)`.
- `ResetText` = `FormatCountdown(bucket.TimeUntilReset(utcNow)!.Value)` when `ResetsAt` is non-null, else `null` (SPEC-0001 owns the date arithmetic; the view-model never subtracts dates itself).
- `Severity` from the **unrounded** clamped value per the `UsageSeverity` thresholds.
- `FooterText` (extra usage): when `ExtraUsage is { IsEnabled: true, UsedCreditsCents: not null }` → `"Extra usage: $4.12 of $25.00"` (cents ÷ 100, invariant `0.00`; `of …` part only when `MonthlyLimitCents` non-null). Currency `null`/`"USD"` ⇒ `$` prefix; any other code ⇒ `"4.12 EUR"`-style suffix. Otherwise `null`.
- `PlanText`: `"pro"` → `Pro`, `"max"` → `Max` (case-insensitive); other non-empty value verbatim; null/empty → `null`.
- `DisplayState` map: `Ok→Ok`, `NotSignedIn→SignedOut`, `TokenExpired→AttentionRequired`, `RateLimited→RateLimited`, `Unavailable→Unavailable`.
- `IsStale = snapshot.IsFromCache`; `RetrievedAtUtc = snapshot.RetrievedAt`. Stale carry-over snapshots (error state + cached buckets) therefore keep their bars **and** show the state message.

**Copilot** (`ForCopilot`): bars in fixed order `PremiumInteractions`, `Chat`, `Completions`; `null` bucket ⇒ omitted; `Label = bucket.DisplayName`.

- `Unlimited == true` ⇒ `UsageBarViewModel(label, 0, true, "Unlimited", null, Normal)` — numeric fields ignored per SPEC-0002.
- Else if `PercentRemaining` non-null ⇒ `PercentUsed = clamp(100 − PercentRemaining)`; `ValueText` = `"945 of 1500 left"` when both `Remaining` and `Entitlement` are non-null, otherwise `"35% left"` (`percent remaining` rounded AwayFromZero).
- Else if `Remaining` and `Entitlement` non-null and `Entitlement > 0` ⇒ `PercentUsed = clamp(100 × (1 − Remaining/Entitlement))`, counts-form `ValueText`.
- Else ⇒ bucket omitted (nothing displayable).
- `ResetText = null` (the monthly reset is global → footer).
- `FooterText`: parts joined with `" · "` — `FormatMonthlyReset(QuotaResetDate)` when non-null; `"{n} overage used"` when `PremiumInteractions?.OverageCount > 0`. No parts ⇒ `null`.
- `PlanText`: `"individual"` → `Individual`, `"individual_pro"` → `Pro`, `"business"` → `Business` (case-insensitive); other non-empty verbatim; null → `null`.
- `DisplayState` map: `Ok→Ok`, `NotSignedIn→SignedOut`, `Forbidden→AttentionRequired`, `RateLimited→RateLimited`, `Unavailable→Unavailable`.
- `StateMessage`: prefer `snapshot.StatusMessage` when non-empty (SPEC-0002 guarantees it is redacted and user-displayable); otherwise the fallback in §4.3.

### 4.2 Formatting rules (invariant culture, exact strings)

`FormatCountdown(t)` (input already clamped ≥ 0 by `TimeUntilReset`; re-clamp defensively):

| Range | Output |
|---|---|
| `t <= 0` | `Resets soon` |
| `< 1 min` | `Resets in <1 m` |
| `< 1 h` | `Resets in {totalMinutes,floor} m` |
| `< 24 h` | `Resets in {h} h {mm:00} m` (e.g. `2 h 05 m`) |
| `>= 24 h` | `Resets in {d} d {h} h` (e.g. `3 d 19 h`, `1 d 0 h`) |

`FormatDataAge(last, now)` with `age = now − last` (negative ⇒ treat as zero):

| Range | Output |
|---|---|
| `last == null` | `No data yet` |
| `< 10 s` | `Updated just now` |
| `< 60 s` | `Updated {s} s ago` |
| `< 60 m` | `Updated {m,floor} m ago` |
| `< 24 h` | `Updated {h,floor} h ago` |
| else | `Updated {d,floor} d ago` |

`FormatMonthlyReset(2026-07-01)` → `Resets Jul 1`. `FormatPercentUsed(41.5)` → `42% used`.

`TrayViewModel.DataAgeText` = `FormatDataAge(newest RetrievedAtUtc among providers with HasData, timeProvider.GetUtcNow())`; no provider has data ⇒ `No data yet`.

### 4.3 Provider-state messages (exact strings)

| Provider | State | `StateMessage` |
|---|---|---|
| both | `Loading` (CreateEmpty) | `Loading…` |
| Claude | `NotSignedIn` | `Sign in via Claude Code CLI` |
| Claude | `TokenExpired` | `Claude session expired — run /login in Claude Code` |
| Claude | `RateLimited` | `Rate limited — will retry later` |
| Claude | `Unavailable` | `Claude usage temporarily unavailable` |
| Copilot | `NotSignedIn` (no service message) | `Sign in via GitHub Copilot CLI` |
| Copilot | `Forbidden` (no service message) | `Copilot token rejected — sign in again via Copilot CLI` |
| Copilot | `RateLimited` (no service message) | `Rate limited — will retry later` |
| Copilot | `Unavailable` (no service message) | `Copilot quota temporarily unavailable` |

For Copilot, a non-empty `snapshot.StatusMessage` wins over the fallback. `DisplayState == Ok` ⇒ `StateMessage == null`. The callout renders the message under the provider header; when `HasData` is also true (stale carry-over) it renders bars **and** message.

### 4.4 Refresh orchestration (`TrayViewModel.RequestRefreshAsync`)

1. **Caller cancellation**: if `cancellationToken` is already cancelled, throw `OperationCanceledException` (the only exception that may escape). Cancellation mid-flight likewise propagates after the in-flight work observes it.
2. **Single-flight**: if a refresh is in flight, return the in-flight `Task` — no additional service calls, for either trigger.
3. **Gating** (per provider, via `timeProvider.GetUtcNow()`):
   - `Manual`: always eligible.
   - `Hover`: eligible when never attempted, or when `now − lastAttemptCompletedUtc >= MinInterval` for that provider (`ClaudeMinInterval` / `CopilotMinInterval`).
   - Neither provider eligible ⇒ return a completed task; `IsRefreshing` never becomes true; no service call.
4. **Execution**: eligible providers run concurrently. Each call gets a linked `CancellationTokenSource` = caller token + `CancelAfter(policy.PerProviderTimeout, timeProvider)`.
5. **Result handling** per provider:
   - Snapshot returned ⇒ remember it as `lastSnapshot` and publish `ForClaude/ForCopilot(snapshot, now)`.
   - Budget timeout or any unexpected exception (services contractually never throw, but the UI fails closed): when a `lastSnapshot` exists, publish `For…(lastSnapshot with { State = Unavailable, IsFromCache = true }, now)` — keep the cached numbers, show the unavailable message; with no `lastSnapshot`, publish a minimal `Unavailable` snapshot (no bars). Never crash, never log snapshot contents.
   - Record `lastAttemptCompletedUtc = now` for the provider (success **and** failure — failures must not cause hover-hammering either).
6. **Notifications**: raise `PropertyChanged` for `Claude`/`Copilot` (when rebuilt), and for `IsRefreshing` and `DataAgeText` around the refresh. `IsRefreshing` is `true` while any provider call is outstanding.
7. The view-model performs **no UI work** (no Dispatcher); the shell guarantees dispatcher-thread calls.

---

## 5. Shell behavior (not unit-tested; verified at TASK-011)

### 5.1 Tray icon lifecycle (`TrayIconHost : IDisposable`)

- Hidden `HwndSource` message window owns the icon. `NIM_ADD` with `NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP` (tip text: `"AgentSubscriptionTracker"`), then `NIM_SETVERSION` with `NOTIFYICON_VERSION_4`. Identity: fixed `uID = 1` + the message window handle.
- Callback message handling: `NIN_POPUPOPEN` → raise `CalloutRequested`; `NIN_POPUPCLOSE` → `CalloutDismissRequested`; `WM_CONTEXTMENU` (and right-button-up fallback) → `ContextMenuRequested(screenPoint)`; left-click → toggle callout. Fallback for shells that never deliver popup notifications: `WM_MOUSEMOVE` dwell timer (~400 ms) opens the callout.
- `RegisterWindowMessageW("TaskbarCreated")`: on receipt (Explorer restarted), re-run `NIM_ADD` + `NIM_SETVERSION`.
- Disposal: `NIM_DELETE` in `Dispose()`, called from `App.OnExit` **and** a `try/finally` around `Application.Run` semantics plus `AppDomain.CurrentDomain.ProcessExit` — under no normal or crash-exit path may a ghost icon remain. `DestroyIcon` on the loaded `HICON`.

### 5.2 Callout window (`CalloutWindow`)

`WindowStyle=None`, `AllowsTransparency=True`, transparent background with a single root `Border` (`CornerRadius=8`, 1-px theme border, drop shadow), `Topmost=True`, `ShowActivated=False`, `ShowInTaskbar=False`, `ResizeMode=NoResize`, fixed width ~340 DIP. Content: app title row; Claude section (header + `PlanText`, bars as slim progress bars colored by `Severity` — accent/amber/red — with `ValueText` right-aligned and `ResetText` subtext, or `StateMessage` when present); separator; Copilot section (same template); footer row bound to `DataAgeText` (+ `IsStale` ⇒ "(cached)" suffix). `DataContext = TrayViewModel`; a `DispatcherTimer` (1 s, running only while the callout is visible) re-raises `DataAgeText` so the age ticks.

### 5.3 Open/close + positioning (`CalloutController`)

- **Open**: on `CalloutRequested` (or dwell fallback), show immediately with current (cached) view-model state, then fire-and-forget `RequestRefreshAsync(RefreshTrigger.Hover)` (it cannot throw apart from cancellation, which is not used here) — hover shows cached data instantly and updates in place ≤ 2 s later.
- **Close**: on `NIN_POPUPCLOSE` *only if* the cursor is not inside the callout; additionally a 100 ms `DispatcherTimer` while open: when `GetCursorPos` is outside both the icon rect (`Shell_NotifyIconGetRect`) and the callout bounds for a 300 ms grace period → hide. Also hide on `Deactivated`-equivalent loss (e.g. full-screen app), and on Exit.
- **Position**: icon rect in device pixels from `Shell_NotifyIconGetRect`; convert with the callout's `HwndSource.CompositionTarget.TransformFromDevice` (Per-Monitor V2 manifest, §5.5); place adjacent to the icon on the taskbar side (above for bottom taskbar, below/left/right accordingly by comparing the icon rect with `MonitorFromPoint` working area via `GetMonitorInfoW`); clamp fully inside that monitor's working area. Re-query on every open (multi-monitor / DPI change safe).

### 5.4 Theme (`ThemeDetector`)

Read `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` value `AppsUseLightTheme` (`0` ⇒ dark; missing ⇒ light). Swap `Themes/Dark.xaml` / `Themes/Light.xaml` merged dictionaries at startup and on `WM_SETTINGCHANGE` with `lParam == "ImmersiveColorSet"` (received by the message window). Registry access is read-only and contains no secrets.

### 5.5 App composition, single instance, autostart

- `App.OnStartup`: acquire `new Mutex(initiallyOwned: true, "Local\\AgentSubscriptionTracker.SingleInstance", out createdNew)`. Not owner ⇒ signal the named `EventWaitHandle "Local\\AgentSubscriptionTracker.ShowCallout"` and exit 0; the first instance listens on a background wait and opens the callout (dispatcher-marshalled) when signalled.
- `ShutdownMode = OnExplicitShutdown`; no main window. Composition root wires: `ClaudeCredentialsFileReader` + `ClaudeUsageService` and `CopilotTokenProvider`/`WindowsCredentialStore` + `CopilotQuotaService` (each over a production `SocketsHttpHandler` — TLS defaults untouched), `TimeProvider.System`, `TrayViewModel`, `TrayIconHost`, `CalloutController`. Constructor injection throughout; `ArgumentNullException.ThrowIfNull` in every public constructor.
- App manifest: `PerMonitorV2` DPI awareness, no elevation (`asInvoker`).
- **Context menu** (WPF `ContextMenu`, themed; `SetForegroundWindow` on the message hwnd before opening so it dismisses correctly):
  - *Refresh now* → `RequestRefreshAsync(RefreshTrigger.Manual)` (+ open callout if closed);
  - *Start with Windows* (checkable, optional — may ship disabled-hidden in v1 with a code TODO): toggles `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `AgentSubscriptionTracker` = quoted exe path (read/write of our own Run value only; no secrets involved);
  - *Exit* → dispose `TrayIconHost`, close windows, `Shutdown()`.

### 5.6 Icon asset

`Assets/AppIcon.ico` is **checked in** and consumed both as `<ApplicationIcon>` and (copied to output) as the tray icon source, loaded at `SM_CXSMICON` size via `LoadImageW(LR_LOADFROMFILE)` (BCL/Win32 only — avoids a System.Drawing.Common dependency at runtime). It is produced **once, manually** by the committed script `Assets/generate-icon.ps1`: Windows PowerShell 5.1 (GDI+ available in-box) draws a simple dual-arc gauge glyph (two stacked rounded bars, accent on dark transparent circle) at 16/20/24/32/48/256 px and packs a multi-image ICO (256 px stored PNG-compressed per the ICO format). The build never runs the script; regenerating requires re-running it by hand. The script writes only this asset file and reads nothing sensitive.

---

## 6. Security & error handling (CLAUDE.md Security Standards)

- **No network, no tokens.** The UI layer issues no HTTP requests, opens no sockets/listeners, and never receives a token: snapshots are token-free by SPEC-0001/0002 contract. Timeouts/retry/backoff/`Retry-After` are service concerns; the view-model adds only the 2 s hover budget and min-interval gating.
- **Redaction.** All strings shown or raised by view-models are either fixed invariant literals (§4.3), numeric formatting of snapshot values, or `CopilotQuotaSnapshot.StatusMessage` (redacted by SPEC-0002 contract). View-models and shell never log snapshot contents, exception payloads from services, or registry values.
- **Graceful provider-unavailable state.** Every provider state has a defined rendering (§4.3); stale data keeps showing with its age; a refresh failure can never crash the app or surface an exception dialog (`RequestRefreshAsync` never throws except caller cancellation; the fire-and-forget hover path passes no cancellable token).
- **Untrusted input.** Snapshot strings rendered as plain text only (`TextBlock.Text`, no markup/`Run` injection); numeric inputs clamped before display.
- **Process hygiene.** Standard user, no elevation manifest, no telemetry; mutex/event names are `Local\` session-scoped; registry writes limited to our own HKCU Run value via the explicit user toggle.
- **Supply chain.** No new runtime NuGet packages. Tray = Win32 P/Invoke; UI = WPF. (No test-project packages added by this spec either.)

---

## 7. Test plan (stubs shipped with this spec — failing/non-compiling until TASK-008, which is expected)

View-model only — **no window is ever shown, no UI automation, no live network, no real credential stores**. Deterministic time via the existing `TestSupport.TestTimeProvider`. Service fakes implement `IClaudeUsageService` / `ICopilotQuotaService` and replay golden-file snapshot fixtures. HTTP-level edge cases (malformed wire JSON, 401/403/429, expired token) reach this layer as provider **states** and stale-cache snapshots per the SPEC-0001/0002 contracts; their HTTP handling is tested by those specs' suites with fake `HttpMessageHandler`s. Fixtures contain **no token fields at all** (snapshots are token-free by design).

| File | Covers |
|---|---|
| `Tray/ProviderViewModelClaudeTests.cs` | bucket→bar order/labels, percent + countdown text, severity thresholds, out-of-range clamping, null buckets omitted, extra-usage footer (incl. disabled/missing), plan text, NotSignedIn/TokenExpired/RateLimited/Unavailable messages, stale carry-over keeps bars, malformed fixture fails strict deserialization |
| `Tray/ProviderViewModelCopilotTests.cs` | bucket order/`DisplayName` labels, unlimited rendering, percent-remaining → percent-used, counts vs percent `ValueText`, derived percent from counts, monthly-reset + overage footer, plan mapping, `StatusMessage` precedence + fallbacks, Forbidden keeps cached bars, malformed fixture |
| `Tray/TrayViewModelRefreshTests.cs` | initial Loading state, manual refresh calls both, hover gating per service (never-attempted / inside / past each interval), manual bypass, single-flight, service exception → unavailable presentation, failure keeps previous data, caller-cancellation propagation, `PropertyChanged` set, data-age aggregation |
| `Tray/UsageFormattingTests.cs` | countdown boundaries (0 / <1 m / minutes / h+mm / 24 h / days), data-age boundaries (null / just-now / s / m / h / d / negative), monthly reset, percent rounding |
| `Tray/TrayTestSupport.cs` | `FakeClaudeUsageService`, `FakeCopilotQuotaService` (scripted queues + call counts), `TrayFixtures` loader, shared instants |

Fixtures (`tests/AgentSubscriptionTracker.Tests/Fixtures/Tray/`, JSON shaped as the SPEC-0001/0002 snapshot models, camelCase + string enums): `claude_snapshot_ok.json`, `claude_snapshot_partial.json`, `claude_snapshot_not_signed_in.json`, `claude_snapshot_token_expired_cached.json`, `claude_snapshot_malformed.json`, `copilot_snapshot_ok.json`, `copilot_snapshot_low_premium.json`, `copilot_snapshot_overage.json`, `copilot_snapshot_missing_fields.json`, `copilot_snapshot_not_signed_in.json`, `copilot_snapshot_forbidden_cached.json`, `copilot_snapshot_malformed.json`.

The test csproj gains `<None Update="Fixtures\**\*.json" CopyToOutputDirectory="PreserveNewest" />` so all three specs' fixtures reach the output directory (restore will refresh `packages.lock.json`; no package changes here).

---

## 8. Acceptance criteria checklist (code agent — all must be true before TASK-008 closes)

**Contracts & tests**
- [ ] All §4 types/members exist with exactly those signatures in `AgentSubscriptionTracker.App.ViewModels`; the SPEC-0003 test stubs compile unmodified and pass.
- [ ] `dotnet build` 0 warnings (TreatWarningsAsErrors, AnalysisMode=All); any suppression (e.g. CA1515 for XAML-bound public types) lives in `.editorconfig` with a one-line justification.
- [ ] `dotnet test` green for the whole solution; SPEC-0003 tests show no window, make no network call, and read no real credential store.

**View-model behavior**
- [ ] Mapping, formatting, state messages, and thresholds match §4.1–§4.3 exactly (exact strings, invariant culture, AwayFromZero rounding, clamping).
- [ ] Refresh orchestration matches §4.4: single-flight; hover honors per-service min intervals; manual bypasses the gate; per-provider 2 s budget via the injected `TimeProvider`; failure keeps cached numbers with an unavailable/state message; never throws except caller cancellation; all timing through `TimeProvider` (no `DateTime.Now`/`UtcNow`, no `Stopwatch`).

**Tray icon (manual verification + code review)**
- [ ] Icon added via raw `Shell_NotifyIcon` (`NOTIFYICON_VERSION_4`); **no WinForms reference** (`UseWindowsForms` not enabled) and no new NuGet packages.
- [ ] Icon survives an Explorer restart (re-added on `TaskbarCreated`) and never persists after exit (NIM_DELETE on every exit path; `DestroyIcon` called).
- [ ] Context menu works: Refresh now (manual refresh), optional Start-with-Windows toggle writing only the app's own HKCU Run value, Exit (clean shutdown, no ghost icon).
- [ ] `Assets/AppIcon.ico` checked in with multi-size images; `Assets/generate-icon.ps1` committed and documented in the file header; build does not execute it.
- [ ] Second app launch does not duplicate the icon: named mutex per §5.5; second instance signals the first (callout opens) and exits 0.

**Callout (manual verification + code review)**
- [ ] Opens on tray hover after a short delay showing cached data instantly; hover triggers a background refresh; closes only when the pointer has left both icon and callout (grace ≈ 300 ms); left-click toggles it.
- [ ] Borderless, rounded (8 px), topmost, non-activating, not in taskbar/Alt-Tab; positioned adjacent to the icon, clamped to the working area, correct under Per-Monitor V2 DPI and on a second monitor.
- [ ] Dark/light theme follows `AppsUseLightTheme`, including live switch on `WM_SETTINGCHANGE`.
- [ ] Both provider sections render bars with severity colors, value/reset texts, plan text, state messages, and the data-age footer that ticks while open; stale data is visibly marked.

**Hygiene**
- [ ] UI layer performs no HTTP calls, holds no tokens, logs no snapshot/exception contents; strings rendered as plain text only.
- [ ] No new top-level folders; spec files untouched by the code agent; `docs/IMPLEMENTATION_SUMMARY.md` updated after the phase.
