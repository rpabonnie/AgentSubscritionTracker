# SPEC-0004 — Callout Theming

| | |
|---|---|
| **Task** | TASK-015 (spec) → TASK-016 (code) |
| **Author** | spec-writer |
| **Date** | 2026-06-19 |
| **Status** | Draft — failing stubs landed; awaiting TASK-016 |
| **Depends on** | SPEC-0003 (`CalloutWindow.xaml`, `SeverityToBrushConverter`, `Themes/Dark.xaml`/`Light.xaml`, `ThemeDetector`) — consumes/extends its DynamicResource keys, does not redefine them |
| **Related** | CLAUDE.md Security Standards (untrusted input, supply chain), memory.md TASK-015 design-phase entry (2026-06-19, 8-agent workflow) and its 20-edge-case catalogue |
| **Test stubs** | `tests/AgentSubscriptionTracker.Tests/Theming/` + fixtures under `tests/AgentSubscriptionTracker.Tests/Fixtures/Theming/` |

---

## 1. Scope

### In scope

- A **custom JSON theme manifest** (`theme.json`) format, parsed with strict, bounded
  `System.Text.Json` deserialization into typed models — `name`, `background`
  (`imagePath`, `fallbackColor`), `fonts` (`header`/`body`/`footer`, each
  `family`/`size`/`weight`), `brushes` (the 7 keys already consumed by
  `CalloutWindow.xaml`: `CalloutBackgroundBrush`, `CalloutBorderBrush`,
  `TextPrimaryBrush`, `TextSecondaryBrush`, `BarTrackBrush`, `SeparatorBrush`,
  `AccentBrush`), `severityBands` (`ok`/`warn`/`critical`), and a `schemaVersion`
  integer for forward-compatibility.
- **Storage**: one folder per theme under
  `%LOCALAPPDATA%\AgentSubscriptionTracker\themes\<theme-id>\` containing `theme.json`
  and an optional `background.png`. `<theme-id>` is a filesystem-safe slug, distinct
  from the user-facing `name`.
- **Background image validation pipeline**: PNG-only, mandatory real alpha channel
  (verified by decoding and inspecting `PixelFormat`, never by trusting the file
  extension or a manifest claim), ≤ 2 MB file size, ≤ 1024×1536 px decoded dimensions,
  composited at a canonical 340×480 reference size (stretch width to 340, anchor-top /
  cover height, no tiling, no 9-slice). The existing root `Border`
  (`CornerRadius="8"`, SPEC-0003 §5.2) continues to own corner clipping; the PNG is
  never expected to pre-bake rounded corners.
- **`imagePath` containment**: resolved relative to the *theme's own folder*,
  canonicalized (`Path.GetFullPath`), and verified to still be a descendant of that
  folder before the file is opened. Absolute paths, URLs, and `..` escapes are
  rejected — never opened, regardless of how the canonicalized path resolves.
- **Font resolution**: `fonts.*.family` validated at load against
  `System.Windows.Media.Fonts.SystemFontFamilies`; an unknown family falls back to
  `Segoe UI` and records a local, redaction-safe warning — it never throws and never
  blocks the rest of the theme from loading.
- **Severity/gauge palette resolution**: `SeverityToBrushConverter` (existing,
  SPEC-0003) is extended to resolve `ok`/`warn`/`critical` brushes plus
  `BarTrackBrush` from the **active theme's** `DynamicResource`s instead of
  static converter-owned colors. The numeric severity thresholds
  (`UsageSeverity.Normal/Warning/Critical` from SPEC-0003 §4) remain app-code and are
  **not** themeable.
- **Theme loader**: discovers all theme folders under the themes root at startup,
  parses each manifest independently (one bad theme does not break the others),
  validates background image and fonts, and exposes an ordered, de-duplicated list of
  loaded themes plus the active theme. Handles missing/duplicate/corrupt themes per
  §4.4.
- **Exactly 2 built-in themes** ("Light", "Dark") shipped as embedded resources,
  re-seeded into the themes folder at launch if missing or corrupted, and marked
  **read-only** — any edit forces Save-As/fork into a new theme id; the shipped files
  are never overwritten in place.
- **Theme button** in the callout footer (`CalloutWindow.xaml`, alongside the existing
  Refresh/Exit buttons, SPEC-0003 §5.2 amendment) opening a **non-activating popup
  menu** (same `ShowActivated=False`/`AllowsTransparency=True` family of behavior as
  `CalloutWindow` itself) listing installed themes: live `RenderTargetBitmap`
  thumbnail + name + Edit button per row, active theme first then alphabetical with a
  checkmark/border indicator, then a separated "Add new theme" entry that opens the
  editor seeded from a clone of a designated built-in.
- **Theme editor** window — a normal, activatable window (it needs real keyboard
  focus) that pins/suppresses the callout's hide-on-blur/auto-hide behavior while
  open. Live preview hosts the real `CalloutWindow` content (the shared
  `UserControl`/`DataTemplate`) bound to the same `DynamicResource` keys, populated
  with static hardcoded sample `UsageBarViewModel` data (never live API calls,
  never a real `TrayViewModel`). Edits are applied to a `ResourceDictionary` scoped to
  the preview element's own `Resources` only — **never** `Application.Current.Resources`
  — until an explicit Save. Controls: background image file picker + Clear, background
  fallback color picker, font family dropdown (`SystemFontFamilies` only) + size, one
  color picker per brush key, one color picker per severity band. Save validates, then
  persists, then updates the live running callout if the edited theme is the active
  one; Cancel discards with **zero disk writes**.
- All 20 edge cases catalogued in `memory.md` under the TASK-015 design-phase entry
  (2026-06-19) — enumerated in §4.4 and folded into the acceptance criteria (§8).

### Out of scope

- Raw WPF XAML `ResourceDictionary` reuse / `XamlReader.Load` of user-edited files —
  **rejected** (design-phase decision, memory.md 2026-06-19): `ObjectDataProvider`/
  `x:Code` in an untrusted, user-editable file is a code-execution vector. This spec's
  manifest format never contains XAML or any executable payload.
  Severity *percent* thresholds (`UsageSeverity` boundaries) — themeable colors only,
  not the math that picks a severity bucket.
- Embedded/bundled font files — fonts are resolved against already-installed system
  fonts only.
- Cloud sync / sharing / import-export of themes beyond local file pickers.
- Animation/transition styling between themes (swap is instantaneous, per §4.4 edge
  case "rapid theme switching").

### Cross-spec contract

This spec **consumes** (and must not redefine) these SPEC-0003 types/resources:

- `AgentSubscriptionTracker.App.Views.SeverityToBrushConverter` — extended in place
  (same class, same namespace), not replaced.
- `AgentSubscriptionTracker.App.ViewModels.UsageBarViewModel`, `UsageSeverity`,
  `ProviderViewModel` — reused verbatim for the editor's sample preview data; this spec
  does not add new severity values or view-model members.
- `CalloutWindow.xaml`'s existing `DynamicResource` keys
  (`CalloutBackgroundBrush`, `CalloutBorderBrush`, `TextPrimaryBrush`,
  `TextSecondaryBrush`, `BarTrackBrush`, `SeparatorBrush`, `AccentBrush`) — this spec's
  manifest `brushes` section is keyed by exactly these 7 names; no renaming.
- `Themes/Dark.xaml` / `Themes/Light.xaml` — become the **embedded source** the two
  built-in `theme.json`/manifests are generated from / re-seeded from; the existing
  `ThemeDetector` OS dark/light heuristic (SPEC-0003 §5.4) is preserved as the
  *initial* theme selection on first run, but the user can override it via the theme
  picker thereafter.

---

## 2. Decision recap (final — do not relitigate)

See memory.md, `[2026-06-19 00:00 UTC] [orchestrator] [DECISION]` entry, for the full
8-agent design-phase rationale. Binding for this spec:

1. Custom JSON manifest via `System.Text.Json`, not XAML `ResourceDictionary` reuse —
   code-execution risk via `ObjectDataProvider`/`x:Code` in untrusted files.
2. Storage layout: `%LOCALAPPDATA%\AgentSubscriptionTracker\themes\<theme-id>\theme.json`
   + `background.png`.
3. Background PNG: real alpha mandatory, 2 MB / 1024×1536 px caps, 340×480 canonical
   size, stretch-width/cover-height/anchor-top, no tiling/9-slice; rounded corners stay
   owned by the existing root `Border`.
4. Fonts validated against installed `SystemFontFamilies`; unknown → `Segoe UI` +
   redaction-safe warning, never throw; no embedded font files.
5. Severity/gauge colors fully themeable (`ok`/`warn`/`critical` + `BarTrackBrush`) via
   `SeverityToBrushConverter` resolving from the active theme's `DynamicResource`s;
   severity *percent thresholds* stay app-code.
6. Exactly 2 built-in themes, read-only, fork-on-edit, re-seeded from embedded
   resources if missing/corrupt at launch.
7. Theme button in the callout footer opens a non-activating picker popup (thumbnail +
   name + Edit per theme, active-first-then-alphabetical with indicator, "Add new
   theme" entry).
8. Theme editor is a real (activatable) window that suppresses the callout's
   auto-hide while open; live preview uses the real `CalloutWindow` content bound to a
   preview-scoped `ResourceDictionary`, sample data only, no live API calls; Save
   validates+persists+live-updates, Cancel writes nothing.

---

## 3. Files to create (code agent)

| File | Contents |
|---|---|
| `src/AgentSubscriptionTracker.App/Theming/ThemeManifest.cs` | `ThemeManifest`, `ThemeBackground`, `ThemeFontSet`, `ThemeFont`, `ThemeBrushSet`, `ThemeSeverityBands` (record models) |
| `src/AgentSubscriptionTracker.App/Theming/ThemeManifestSerializer.cs` | `IThemeManifestSerializer`, `ThemeManifestSerializer`, `ThemeManifestParseException`/`ThemeManifestParseResult` |
| `src/AgentSubscriptionTracker.App/Theming/ThemeBackgroundImageValidator.cs` | `IThemeBackgroundImageValidator`, `ThemeBackgroundImageValidator`, `BackgroundImageValidationResult`, `BackgroundImageValidationError` |
| `src/AgentSubscriptionTracker.App/Theming/ThemeFontResolver.cs` | `IThemeFontResolver`, `ThemeFontResolver`, `ResolvedThemeFont` |
| `src/AgentSubscriptionTracker.App/Theming/ThemePathResolver.cs` | `ThemePathResolver` (static) — `imagePath` containment/canonicalization helper |
| `src/AgentSubscriptionTracker.App/Theming/ThemeLoader.cs` | `IThemeLoader`, `ThemeLoader`, `LoadedTheme`, `ThemeLoadStatus`, `ThemeRepositoryState` |
| `src/AgentSubscriptionTracker.App/Theming/ThemeStore.cs` | `IThemeStore`, `ThemeStore` (filesystem read/write/Save-As/fork) |
| `src/AgentSubscriptionTracker.App/Views/SeverityToBrushConverter.cs` (modified) | severity → brush resolution reads `ok`/`warn`/`critical`/`BarTrackBrush` from active theme `DynamicResource`s |
| `src/AgentSubscriptionTracker.App/Views/ThemePickerPopup.xaml(.cs)` | non-activating theme picker popup |
| `src/AgentSubscriptionTracker.App/Views/ThemeEditorWindow.xaml(.cs)` | theme editor window + live preview |
| `src/AgentSubscriptionTracker.App/ViewModels/ThemePickerViewModel.cs` | `ThemePickerViewModel`, `ThemeListEntryViewModel` |
| `src/AgentSubscriptionTracker.App/ViewModels/ThemeEditorViewModel.cs` | `ThemeEditorViewModel` |
| `src/AgentSubscriptionTracker.App/Assets/Themes/light.theme.json`, `Assets/Themes/dark.theme.json` | embedded built-in manifests (no background image required for the built-ins) |

Namespace for non-view types: `AgentSubscriptionTracker.App.Theming`. View-models:
`AgentSubscriptionTracker.App.ViewModels` (existing namespace, extended). Views:
`AgentSubscriptionTracker.App.Views` (existing namespace, extended).

This spec writer does **not** create or modify any file under `src/`; the table above
is binding guidance for TASK-016.

---

## 4. Public type contracts (binding — test stubs compile against exactly these)

```csharp
namespace AgentSubscriptionTracker.App.Theming;

/// <summary>Strict, bounded deserialization target for theme.json. Immutable.</summary>
public sealed record ThemeManifest
{
    public required int SchemaVersion { get; init; }
    public required string Name { get; init; }
    public required ThemeBackground Background { get; init; }
    public required ThemeFontSet Fonts { get; init; }
    public required ThemeBrushSet Brushes { get; init; }
    public required ThemeSeverityBands SeverityBands { get; init; }

    /// <summary>Current manifest schema version this build writes/expects. Loader accepts
    /// only this exact value for v1; unknown/future versions are treated as unsupported
    /// (skip-whole-file, never a partial/best-effort parse).</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary><paramref name="ImagePath"/> is relative to the theme's own folder, or null
/// for "no image, fallback color only".</summary>
public sealed record ThemeBackground
{
    public string? ImagePath { get; init; }
    /// <summary>"#AARRGGBB" or "#RRGGBB" hex. Required even when ImagePath is set
    /// (used while the image loads and if it later degrades).</summary>
    public required string FallbackColor { get; init; }
}

public sealed record ThemeFontSet
{
    public required ThemeFont Header { get; init; }
    public required ThemeFont Body { get; init; }
    public required ThemeFont Footer { get; init; }
}

public sealed record ThemeFont
{
    public required string Family { get; init; }
    /// <summary>Points. Must be in (0, 96] after deserialization-time range validation.</summary>
    public required double Size { get; init; }
    /// <summary>"Normal" | "SemiBold" | "Bold" | "Light" (maps to FontWeights.*).</summary>
    public required string Weight { get; init; }
}

/// <summary>Exactly the 7 brush keys CalloutWindow.xaml already consumes via DynamicResource.
/// Each value is a "#AARRGGBB"/"#RRGGBB" hex string.</summary>
public sealed record ThemeBrushSet
{
    public required string CalloutBackgroundBrush { get; init; }
    public required string CalloutBorderBrush { get; init; }
    public required string TextPrimaryBrush { get; init; }
    public required string TextSecondaryBrush { get; init; }
    public required string BarTrackBrush { get; init; }
    public required string SeparatorBrush { get; init; }
    public required string AccentBrush { get; init; }
}

public sealed record ThemeSeverityBands
{
    public required string Ok { get; init; }
    public required string Warn { get; init; }
    public required string Critical { get; init; }
}

/// <summary>Why ThemeManifestSerializer.TryParse failed. Never includes file content
/// or any path beyond the theme id (redaction-safe).</summary>
public enum ThemeManifestParseError
{
    InvalidJson,
    UnsupportedSchemaVersion,
    MissingRequiredField,
    OutOfRangeValue,
    InvalidColorFormat,
    PathTraversalRejected,
}

public readonly record struct ThemeManifestParseResult(
    ThemeManifest? Manifest,
    ThemeManifestParseError? Error);

/// <summary>Strict, bounded JSON parsing for theme.json. Never throws on malformed
/// input — failures are reported via <see cref="ThemeManifestParseResult"/>.</summary>
public interface IThemeManifestSerializer
{
    /// <summary>Parses theme.json content. Unknown JSON properties are ignored;
    /// every documented field is required (missing ⇒ MissingRequiredField) except
    /// <see cref="ThemeBackground.ImagePath"/> which may be null. Hex colors are
    /// validated for syntax (not yet resolved to a Brush — that is ThemeLoader's job).
    /// imagePath containing ".." segments, a rooted/absolute path, or a URI scheme
    /// (e.g. "file:", "http:") is rejected at parse time with PathTraversalRejected —
    /// it is never handed to the filesystem.</summary>
    ThemeManifestParseResult TryParse(string json);

    /// <summary>Serializes a manifest back to canonical JSON (used by the editor's Save
    /// path and by built-in re-seeding). Round-trips TryParse(Serialize(m)) == m.</summary>
    string Serialize(ThemeManifest manifest);
}

/// <summary>Resolves and opens a theme's background image safely. Never throws;
/// failures are reported via <see cref="BackgroundImageValidationResult"/>.</summary>
public enum BackgroundImageValidationError
{
    PathOutsideThemeFolder,
    FileNotFound,
    FileTooLarge,        // > 2 MB
    NotAPng,
    CorruptOrTruncated,
    DimensionsTooLarge,  // > 1024x1536 decoded
    NoAlphaChannel,
}

public readonly record struct BackgroundImageValidationResult(
    System.Windows.Media.Imaging.BitmapSource? Image,
    BackgroundImageValidationError? Error);

public interface IThemeBackgroundImageValidator
{
    /// <summary>
    /// <paramref name="themeFolder"/> is the theme's own absolute folder;
    /// <paramref name="imagePath"/> is the manifest's (already syntax-validated,
    /// non-traversal) relative path. Resolves+canonicalizes
    /// <paramref name="imagePath"/> against <paramref name="themeFolder"/>, verifies
    /// containment (defense-in-depth — duplicates the serializer's syntax check
    /// against a real filesystem path), checks file size, decodes, checks
    /// PixelFormat for a real alpha channel, checks decoded pixel dimensions.
    /// Never opens a file whose canonicalized path escapes <paramref name="themeFolder"/>.
    /// </summary>
    BackgroundImageValidationResult Validate(string themeFolder, string imagePath);
}

/// <summary>A font family resolved against installed system fonts, with fallback.</summary>
public readonly record struct ResolvedThemeFont(
    System.Windows.Media.FontFamily Family,
    double Size,
    System.Windows.FontWeight Weight,
    /// <summary>True when the manifest's requested family was not installed and
    /// "Segoe UI" was substituted.</summary>
    bool FellBackToDefault);

public interface IThemeFontResolver
{
    /// <summary>Looks up <paramref name="font"/>.Family in
    /// System.Windows.Media.Fonts.SystemFontFamilies (case-insensitive). Not found ⇒
    /// substitutes "Segoe UI" and sets FellBackToDefault; never throws. Unknown
    /// Weight string ⇒ FontWeights.Normal (does not affect FellBackToDefault).</summary>
    ResolvedThemeFont Resolve(ThemeFont font);
}

/// <summary>Pure path-containment helper (no I/O beyond Path.GetFullPath's string math).</summary>
public static class ThemePathResolver
{
    /// <summary>True and outputs the canonicalized absolute path only when
    /// <paramref name="relativeImagePath"/>, resolved against
    /// <paramref name="themeFolder"/> and canonicalized, is still a descendant of
    /// <paramref name="themeFolder"/>. Rejects absolute/rooted paths, ".." escapes,
    /// and any path containing a URI scheme (":" other than a Windows drive letter
    /// at position 1) outright — they never reach Path.GetFullPath's resolution
    /// against the theme folder.</summary>
    public static bool TryResolveContained(
        string themeFolder, string relativeImagePath, out string canonicalAbsolutePath);
}

/// <summary>Outcome of loading one theme folder.</summary>
public enum ThemeLoadStatus
{
    Ok,
    /// <summary>Manifest parsed but the background image failed validation; the theme
    /// loads with FallbackColor only and this status, instead of being discarded.</summary>
    DegradedMissingOrInvalidImage,
    /// <summary>Manifest itself failed to parse; the theme is not loaded at all.</summary>
    Quarantined,
}

/// <summary>One successfully-or-degraded-loaded theme, ready for binding.</summary>
public sealed class LoadedTheme
{
    public required string ThemeId { get; init; }           // filesystem-safe slug
    public required string DisplayName { get; init; }       // manifest Name, disambiguated if duplicate (§4.4)
    public required ThemeManifest Manifest { get; init; }
    public required ThemeLoadStatus Status { get; init; }
    public required bool IsBuiltIn { get; init; }            // true for the 2 shipped themes
    /// <summary>Null when Status != Ok or Background.ImagePath is null.</summary>
    public System.Windows.Media.Imaging.BitmapSource? BackgroundImage { get; init; }
    public required string FolderPath { get; init; }
}

/// <summary>Snapshot of everything ThemeLoader discovered on one load pass.</summary>
public sealed record ThemeRepositoryState
{
    public required IReadOnlyList<LoadedTheme> Themes { get; init; }   // never empty (§4.4 "zero themes installed")
    public required string ActiveThemeId { get; init; }
    /// <summary>Theme ids skipped entirely (Quarantined) on this pass, e.g. duplicate
    /// ids (first-loaded wins, later ones land here) or unparseable manifests.</summary>
    public required IReadOnlyList<string> QuarantinedThemeIds { get; init; }
}

public interface IThemeLoader
{
    /// <summary>Discovers all theme folders under the themes root, re-seeds the 2
    /// built-ins from embedded resources if missing/corrupt, loads/validates every
    /// manifest, and returns the resulting state. Never throws; a folder that cannot
    /// be loaded is Quarantined, not fatal to the call. If literally nothing loads
    /// (e.g. the themes root itself is inaccessible), returns a state containing only
    /// the two built-ins from an absolute hardcoded in-code fallback (not even reading
    /// the embedded resource files) so <see cref="ThemeRepositoryState.Themes"/> is
    /// never empty.</summary>
    ThemeRepositoryState LoadAll(string? activeThemeIdHint = null);
}

public interface IThemeStore
{
    /// <summary>True when <paramref name="themeId"/> is one of the 2 read-only built-ins.</summary>
    bool IsBuiltIn(string themeId);

    /// <summary>Persists <paramref name="manifest"/> (+ optional background image bytes)
    /// under a new theme id derived from <paramref name="manifest"/>.Name, guaranteed
    /// unique even when the name duplicates an existing theme's display name (ids never
    /// collide; duplicate display names are allowed and disambiguated for display only,
    /// per §4.4). Always used instead of direct overwrite when the source is a built-in.</summary>
    string SaveAsNew(ThemeManifest manifest, byte[]? backgroundPngBytes);

    /// <summary>Overwrites an existing non-built-in theme in place. Throws
    /// InvalidOperationException if <paramref name="themeId"/> IsBuiltIn — callers
    /// must route built-ins through SaveAsNew.</summary>
    void Overwrite(string themeId, ThemeManifest manifest, byte[]? backgroundPngBytes);

    /// <summary>Deletes a non-built-in theme folder. Throws InvalidOperationException
    /// for a built-in id.</summary>
    void Delete(string themeId);
}
```

### 4.1 Color and font-weight parsing rules

- Hex colors accept `#RRGGBB` (alpha defaults to `FF`) and `#AARRGGBB`; any other
  syntax (named colors, `rgb()`, missing `#`, wrong length, non-hex digits) is
  `InvalidColorFormat` at parse time.
- **Skip-whole-file vs clamp-single-value** (memory.md edge case): a *syntactically*
  invalid color value (`InvalidColorFormat`) fails the **entire** manifest parse — the
  theme is `Quarantined`, not partially loaded with a substituted color. There is no
  "out-of-range" numeric color concept once hex syntax is valid (every well-formed hex
  byte is in range by construction); `OutOfRangeValue` is reserved for numeric fields
  (`ThemeFont.Size` outside `(0, 96]`) which similarly fail the whole file.
- `Weight` strings outside `{Normal, SemiBold, Bold, Light}` (case-insensitive) do
  **not** fail the parse — `ThemeFontResolver` maps unknown weights to
  `FontWeights.Normal` (§4 `IThemeFontResolver.Resolve`), matching the "graceful font
  fallback" decision; this is a resolution-time fallback, not a manifest-parse error,
  because weight is cosmetic, unlike colors which directly drive every brush.

### 4.2 `imagePath` containment (§4 `ThemePathResolver`, edge case "path traversal")

1. Reject outright (no filesystem call) if `relativeImagePath` is null/empty, is
   `Path.IsPathRooted`, contains a `:` other than at index 1 with a preceding drive
   letter pattern, or contains a literal `..` path segment.
2. Otherwise combine with `themeFolder`, call `Path.GetFullPath` on the combined path,
   and call `Path.GetFullPath` on `themeFolder` itself; succeed only when the combined
   canonical path starts with the canonical theme-folder path plus a directory
   separator (or equals it — though a folder is never a valid image).
3. `ThemeBackgroundImageValidator.Validate` re-derives containment itself from the raw
   `themeFolder`/`imagePath` pair rather than trusting a path handed in by a caller —
   defense in depth against a future caller skipping step 1–2.

### 4.3 Background image validation order (§4 `IThemeBackgroundImageValidator`)

Checks run in this order, short-circuiting on the first failure (cheapest/safest
checks first — never decode a file before its size and path are known-safe):

1. Path containment (§4.2) → `PathOutsideThemeFolder`.
2. File exists → `FileNotFound`.
3. File size ≤ 2 MB (`FileInfo.Length`, checked **before** any decode) → `FileTooLarge`.
4. PNG signature/decoder accepts it → `NotAPng` (wrong signature) or
   `CorruptOrTruncated` (right signature, decode fails/throws internally — caught,
   never propagated).
5. Decoded `PixelWidth`/`PixelHeight` ≤ 1024×1536 → `DimensionsTooLarge`.
6. Decoded `Format` has a real alpha channel (`Format32bppArgb`/`Format32bppPArgb` or
   equivalent — checked via the decoded `PixelFormat`, never the manifest's claim or
   the file extension) → `NoAlphaChannel`.

Any failure here means the **theme still loads** (`ThemeLoadStatus.DegradedMissingOrInvalidImage`)
using `Background.FallbackColor` as a solid background — it is never a reason to
quarantine the whole manifest, and never crashes the thumbnail renderer or the editor's
import-time preview (memory.md edge cases: "missing/moved image path", "invalid/oversized
PNG at import time in the editor").

### 4.4 Loader-level edge cases (binding — memory.md TASK-015 catalogue, all 20)

| # | Edge case | Required behavior |
|---|---|---|
| 1 | Corrupt/truncated PNG | `CorruptOrTruncated` → degrade to fallback color (§4.3). |
| 2 | Oversized PNG (file size) | `FileTooLarge` → degrade to fallback color (checked before decode). |
| 3 | PNG lacking real alpha despite manifest claim | `NoAlphaChannel` decided from decoded `PixelFormat` only → degrade. |
| 4 | Missing/moved image path | `FileNotFound` → degrade to fallback color, theme still usable. |
| 5 | Duplicate theme IDs | First-loaded (folder enumeration order) wins; later folder(s) with the same id land in `QuarantinedThemeIds`, never merged. |
| 6 | Invalid JSON | `InvalidJson` → whole file `Quarantined` (not partial). |
| 7 | Out-of-range color value | Hex-syntax-invalid color is `InvalidColorFormat` → whole file `Quarantined` (no per-value clamping; see §4.1). |
| 8 | Path traversal in `imagePath` | Rejected at parse time (`PathTraversalRejected`) — never reaches the filesystem; manifest itself is `Quarantined` (traversal is treated as a hostile/corrupt manifest, not a degrade-the-image case). |
| 9 | Source file deleted/renamed mid-edit in the editor | Editor operates on an in-memory copy of the manifest from the moment it opens; on Save, if the original theme folder no longer exists, the editor prompts Save-As instead of failing the save. |
| 10 | Missing font family | `ThemeFontResolver` substitutes `Segoe UI`, sets `FellBackToDefault`, records one redaction-safe local warning; load continues. |
| 11 | Deleting/renaming the active theme | Deleting the active theme is rejected (`IThemeStore.Delete` is not the place this is enforced — the *editor/picker view-model* must refuse the action with a message) unless another theme is selected first; the loader, if it ever observes the active theme id missing from `Themes` on a fresh `LoadAll`, falls back to whichever built-in matches the current OS dark/light setting. |
| 12 | Duplicate display names | Allowed; never merged. `LoadedTheme.DisplayName` carries the raw manifest `Name`; the picker UI is responsible for disambiguating visually (e.g. suffixing) — ids remain the source of truth for selection. |
| 13 | Built-in edit forcing fork | `IThemeStore.Overwrite` throws `InvalidOperationException` for a built-in id; the editor view-model must route a built-in's Save through `SaveAsNew` instead, never reach `Overwrite` for a built-in. |
| 14 | Built-ins missing/corrupted at launch | `ThemeLoader.LoadAll` re-seeds both built-in folders from embedded resources before the load pass; a re-seed that itself fails to parse falls through to the absolute hardcoded in-code fallback (no file I/O) so the app never has zero usable themes. |
| 15 | Zero themes installed | Guaranteed impossible: `ThemeRepositoryState.Themes` is never empty — worst case is the in-code fallback pair from #14. |
| 16 | Editor-open-while-callout-visible focus/auto-hide interaction | Opening `ThemeEditorWindow` must pin/suppress `CalloutController`'s hide-on-blur/auto-hide watch for the duration the editor is open (shell-level behavior, verified manually per §6; not unit-testable without a window). |
| 17 | Rapid theme switching | Theme application is an idempotent `DynamicResource` swap on the dispatcher thread; the last selection click always wins (no queuing/locking needed because each swap fully replaces the prior one), and an in-flight data refresh (SPEC-0003 `RequestRefreshAsync`) is never cancelled by a theme switch — the two are orthogonal. |
| 18 | Low-contrast warning | Non-blocking: the editor computes a relative-luminance contrast ratio between `TextPrimaryBrush`/`TextSecondaryBrush` and `CalloutBackgroundBrush` and shows an inline warning when below a documented threshold, but never refuses to Save. |
| 19 | Very long theme names | Picker/editor display truncates with an ellipsis and shows the full name via tooltip; storage/manifest `Name` itself is not length-limited beyond ordinary JSON string bounds. |
| 20 | Invalid/oversized PNG at import time in the editor | The editor's file-picker import path reuses `IThemeBackgroundImageValidator.Validate` against the *picked* file before accepting it into the in-memory draft; a failure shows an inline error in the editor and never corrupts the on-disk theme or crashes the live thumbnail/preview renderer (the draft simply keeps its previous background state). |

---

## 5. Shell behavior (not unit-tested; verified at TASK-019 review / human checkpoint)

### 5.1 Theme button + picker popup (`ThemePickerPopup`)

Added to `CalloutWindow.xaml`'s existing footer command row (SPEC-0003 §5.2
amendment), styled consistently with the Refresh/Exit buttons. Opens a
`Popup`/borderless window with `AllowsTransparency=True`, non-activating
(`ShowActivated=False` equivalent), positioned adjacent to the button. Rows: live
`RenderTargetBitmap` thumbnail (rendered from the same preview `UserControl` used by
the editor, with the row's theme resources applied) + `DisplayName` + small Edit
button; active theme rendered first with a checkmark/border indicator, remaining
themes alphabetical by `DisplayName`; a separator then "Add new theme" which opens
`ThemeEditorWindow` seeded from a clone of a designated built-in's manifest.

### 5.2 Theme editor window (`ThemeEditorWindow`)

A real, activatable `Window` (not `ShowActivated=False` — it must accept keyboard
focus for text/numeric inputs and the font-family combo). On open, it signals
`CalloutController` to suspend its hide-on-blur/auto-hide timer (edge case #16);
on close (Save or Cancel) it un-suspends it. Hosts the shared callout content
`UserControl` bound to `DynamicResource`s resolved from a `ResourceDictionary` scoped
to the preview element's `Resources` — edits to any control update that dictionary
live, never `Application.Current.Resources`. Sample data is a hardcoded
`UsageBarViewModel`/`ProviderViewModel` fixture (mirrors realistic Claude/Copilot
shapes) — never a real service call. Save pipeline: validate (font/weight/color
syntax already guaranteed by the editor's own typed controls; background image via
§4.3) → persist via `IThemeStore` (`SaveAsNew` for built-ins, `Overwrite` otherwise) →
if the saved theme id is the currently active theme, swap the live application's
`DynamicResource`s in place (§4.4 edge case #17 semantics). Cancel discards the
in-memory draft; zero disk writes occur.

### 5.3 Severity brush resolution (`SeverityToBrushConverter`, modified)

Existing converter (SPEC-0003) is extended: instead of static converter-owned
severity colors, `Convert` looks up `ok`/`warn`/`critical` (mapped from
`UsageSeverity.Normal/Warning/Critical`) as a resource lookup against the bound
element's resource scope (the `IValueConverter.Convert` `parameter`/binding-target
element, consistent with how `FrameworkElement.TryFindResource` walks scopes), so the
same converter instance automatically reflects whichever theme's resources are
currently merged — including the editor's preview-scoped dictionary when used inside
the preview, and the application-wide merged dictionary when used inside the live
`CalloutWindow`. The converter additionally exposes a small public helper,
`ResolveBarTrackBrush(DependencyObject scope)`, used by `CalloutWindow.xaml`'s bar
`Background` binding (replacing its current static `BarTrackBrush` `DynamicResource`
reference) so track color resolution goes through the same theme-aware lookup as the
severity colors. Resolution is never cached per-converter-instance — repeated theme
swaps (edge case #17) are reflected on the very next `Convert`/`ResolveBarTrackBrush`
call with no reconstruction needed.

---

## 6. Security & error handling (CLAUDE.md Security Standards)

- **No code execution from theme files.** The manifest format is JSON only, parsed
  with `System.Text.Json` into closed-shape records — no `XamlReader`, no
  `ObjectDataProvider`, no reflection-driven type resolution from file content.
- **Path containment.** `imagePath` is always resolved relative to its own theme
  folder and verified as a descendant before any `File.Open`/decode call — covers
  `..` traversal, absolute paths, and URL-like schemes (§4.2). This is checked twice
  (serializer-level syntax rejection, validator-level canonical-path containment) —
  defense in depth.
- **Bounded, strict deserialization.** Every manifest field is a fixed, typed shape;
  unknown JSON properties are ignored (not rejected — forward-compatible with
  `schemaVersion` bumps that add fields a future build doesn't know about yet, though
  v1 of this build only accepts `schemaVersion == 1`); numeric/string fields are
  range/syntax-validated before becoming a `ThemeManifest`; a `2 MB`+ background PNG
  is rejected by file size **before** a single byte is decoded (decoder-bomb defense).
- **No new network surface.** Nothing in this spec issues HTTP requests, opens a
  socket, or reads from a URL; file pickers operate only on the local filesystem via
  standard WPF `OpenFileDialog`/`SaveFileDialog`.
- **Redaction.** Font-fallback warnings and any other diagnostic text record theme
  ids/family names only — never full file paths beyond what's already user-visible in
  the picker, never file content, never anything resembling a token (this feature
  never touches tokens at all).
- **Fail closed, degrade gracefully.** A bad background image degrades that one
  theme's background to a solid fallback color; a bad manifest quarantines that one
  theme; the app guarantees at least the 2 built-ins are always available (§4.4
  #14/#15) — theming failures never crash the app and never fall back to an unthemed
  blank callout.
- **Supply chain.** No new NuGet packages — `System.Text.Json` and WPF imaging
  (`BitmapDecoder`/`PngBitmapDecoder`) are both BCL/WPF, already implicitly available.

---

## 7. Test plan (stubs shipped with this spec — failing/non-compiling until TASK-016, which is expected)

Theming-model and validation logic only — **no window is ever shown** by these stubs
(picker/editor shell behavior is reviewed manually per §5/§6, consistent with how
SPEC-0003 treats `TrayIconHost`/`CalloutController`/`CalloutWindow`). No live network
calls. Deterministic where time matters (none of this layer depends on `TimeProvider`
directly, unlike SPEC-0003's refresh orchestration).

| File | Covers |
|---|---|
| `Theming/ThemeManifestSerializerTests.cs` | valid round-trip (dark/light fixtures), malformed/truncated JSON, missing required field, invalid color syntax, out-of-range font size, unsupported schema version, path-traversal `imagePath` rejected at parse time (`..`, absolute path, URI scheme), unknown JSON properties ignored, `Serialize` → `TryParse` round-trip equality |
| `Theming/ThemeBackgroundImageValidatorTests.cs` | valid 340×480 PNG with real alpha passes; PNG without alpha → `NoAlphaChannel`; oversized dimensions (2000×2000) → `DimensionsTooLarge`; oversized file size (>2 MB) → `FileTooLarge` (checked before decode); truncated/corrupt PNG → `CorruptOrTruncated`; non-PNG content named `.png` → `NotAPng`; missing file → `FileNotFound`; path traversal / absolute path → `PathOutsideThemeFolder`; check ordering (size check short-circuits before a would-be-slow decode) |
| `Theming/ThemePathResolverTests.cs` | plain relative path resolves and is contained; `..` escape rejected; absolute Windows path rejected; UNC/URL-like (`\\host\share`, `file:///`, `http://`) rejected; case/separator normalization still resolves correctly; symlink-style escape via a crafted relative path that canonicalizes outside the folder is rejected |
| `Theming/ThemeFontResolverTests.cs` | known installed family resolves with `FellBackToDefault=false`; unknown family falls back to "Segoe UI" with `FellBackToDefault=true` and never throws; known weight strings map correctly (`Normal`/`SemiBold`/`Bold`/`Light`, case-insensitive); unknown weight string maps to `FontWeights.Normal` without affecting `FellBackToDefault` |
| `Theming/ThemeLoaderTests.cs` | loads the 2 built-ins when the themes root is empty (re-seed); duplicate theme id keeps first-loaded and quarantines the later one; malformed manifest in one folder does not block loading the others; missing/corrupt built-in folder is re-seeded; background-image failure degrades that theme (`DegradedMissingOrInvalidImage`) without quarantining it; zero-themes-installed / inaccessible themes root still yields the in-code fallback pair (`Themes` never empty); active-theme-hint missing from the loaded set falls back to an OS-appropriate built-in |
| `Theming/SeverityToBrushConverterThemeTests.cs` | `Normal`/`Warning`/`Critical` resolve to the `ok`/`warn`/`critical` `DynamicResource` keys of whichever resource scope the converter is evaluated against; `BarTrackBrush` resolves the same way; switching the active resource dictionary changes the converter's output without re-constructing the converter (idempotent swap, edge case #17) |
| `Theming/ThemeTestSupport.cs` | fixture path helpers (`FixturePath`/`ReadFixture`/`ReadFixtureBytes` under `Fixtures/Theming/`), a minimal in-memory `ThemeManifest` builder for tests that don't need a fixture file, sample `UsageBarViewModel`/`ProviderViewModel` data matching what the editor's live preview will use |

Fixtures (`tests/AgentSubscriptionTracker.Tests/Fixtures/Theming/`):

- `theme_valid_dark.json`, `theme_valid_light.json` — well-formed manifests mirroring
  the shipped built-ins.
- `theme_malformed_json.json` — truncated/invalid JSON.
- `theme_out_of_range_color.json` — syntactically invalid color value.
- `theme_path_traversal.json` — `imagePath` containing a `..` escape.
- `theme_absolute_path.json` — `imagePath` is a rooted absolute path.
- `theme_missing_required_field.json` — `name` omitted.
- `theme_unknown_font_family.json` — a font family that does not exist on any
  Windows install, to exercise the fallback path.
- `valid_with_alpha_340x480.png`, `valid_with_alpha_512x768.png` — real
  `Format32bppArgb` PNGs within both caps.
- `no_alpha_340x480.png` — valid, in-range PNG but opaque (no real alpha channel).
- `oversized_2000x2000.png` — valid PNG exceeding the 1024×1536 decoded-dimension cap.
- `oversized_filesize.png` — exceeds the 2 MB file-size cap (padded past the limit
  deliberately; must be rejected by the size check before any decode is attempted).
- `truncated.png` — a PNG file cut off mid-stream (decode must fail cleanly, not
  throw uncaught).
- `not_a_png.png` — arbitrary non-image bytes with a `.png` name (extension must
  never be trusted).

The test csproj already glob-copies `Fixtures\**\*.json` (`CopyToOutputDirectory`);
the code agent must extend that `None` item (or add a sibling one) to also copy
`Fixtures\**\*.png` so the PNG fixtures reach the test output directory.

---

## 8. Acceptance criteria checklist (code agent — all must be true before TASK-016 closes)

**Contracts & tests**
- [ ] All §4 types/members exist with exactly those signatures in
      `AgentSubscriptionTracker.App.Theming` (and the `SeverityToBrushConverter`
      modification in `.Views`); the SPEC-0004 test stubs compile unmodified and pass.
- [ ] `dotnet build` 0 warnings (TreatWarningsAsErrors, AnalysisMode=All) for the whole
      solution.
- [ ] `dotnet test` green for the whole solution, including all existing SPEC-0001/
      0002/0003 suites (no regressions); SPEC-0004 tests show no window, make no
      network call.
- [ ] PNG fixtures copy to the test output directory (csproj `None`/`Content` glob
      updated) and are referenced by path, never embedded as base64 in test code.

**Manifest & parsing**
- [ ] Strict bounded `System.Text.Json` parsing per §4/§4.1; malformed JSON, missing
      required fields, invalid color syntax, out-of-range font size, and unsupported
      schema version each fail the **whole** manifest (`Quarantined`), never a partial
      load with substituted values.
- [ ] `imagePath` syntax validation (traversal/`..`, absolute path, URI scheme)
      happens at parse time and never reaches the filesystem; the loader's image
      validator independently re-verifies containment against the real folder.
- [ ] `Serialize`/`TryParse` round-trip exactly for both built-in manifests.

**Background image pipeline**
- [ ] Validation order matches §4.3 (containment → exists → size → decode → dimensions
      → alpha), short-circuiting on first failure; size is checked before any decode.
- [ ] Alpha-channel presence is determined from the **decoded `PixelFormat`**, never
      the file extension or a manifest claim.
- [ ] Any validation failure degrades that theme to `Background.FallbackColor`
      (`DegradedMissingOrInvalidImage`) rather than quarantining the manifest or
      crashing any caller (loader, picker thumbnail renderer, editor import preview).
- [ ] Composited background follows the 340×480 canonical sizing rule (stretch width,
      anchor-top/cover height, no tiling/9-slice); the root `Border`'s
      `CornerRadius="8"` still owns corner clipping — the PNG is never expected to
      pre-round its own corners.

**Fonts & severity colors**
- [ ] Font family resolution validates against `Fonts.SystemFontFamilies`; unknown
      family falls back to `Segoe UI`, sets `FellBackToDefault`, logs one
      redaction-safe local warning, never throws.
- [ ] `SeverityToBrushConverter` resolves `ok`/`warn`/`critical`/`BarTrackBrush` from
      the active theme's `DynamicResource`s; severity percent thresholds
      (`UsageSeverity` enum boundaries) are unchanged from SPEC-0003 and remain
      app-code.

**Theme loader & storage**
- [ ] Exactly 2 built-in themes ship, are marked `IsBuiltIn`, and are re-seeded from
      embedded resources whenever missing or corrupted at launch.
- [ ] `IThemeStore.Overwrite` throws for a built-in id; saving an edited built-in is
      only reachable via `SaveAsNew`, which never overwrites the shipped files.
- [ ] Duplicate theme ids: first-loaded wins; later duplicates land in
      `QuarantinedThemeIds`, never merged.
- [ ] `ThemeRepositoryState.Themes` is never empty under any failure combination
      (corrupt built-ins, inaccessible themes root, zero user themes) — guaranteed by
      the absolute hardcoded in-code fallback pair.
- [ ] Deleting/renaming the active theme is guarded (rejected, or the loader falls
      back to an OS-appropriate built-in on next load) — never leaves the app with no
      resolvable active theme.
- [ ] Duplicate display names are permitted and never silently merged; ids remain the
      unique selection key.

**Theme button, picker, editor (manual verification + code review — §5/§6)**
- [ ] Callout footer theme button opens a non-activating popup listing installed
      themes with live thumbnails, active-theme-first-then-alphabetical ordering and
      indicator, and an "Add new theme" entry.
- [ ] Theme editor is a real activatable window; opening it suspends the callout's
      hide-on-blur/auto-hide while it is open and restores it on close.
- [ ] Editor's live preview renders the real `CalloutWindow` content/`UserControl`
      bound to a `ResourceDictionary` scoped to the preview element only — edits never
      touch `Application.Current.Resources` until Save.
- [ ] Editor preview uses hardcoded sample `UsageBarViewModel`/`ProviderViewModel`
      data only — zero live API calls from the editor.
- [ ] Save validates, persists via `IThemeStore`, and — only when the saved theme is
      currently active — live-updates the running callout's resources in place;
      Cancel performs zero disk writes.
- [ ] Importing a background image in the editor reuses the same validation pipeline
      as the loader; a failure shows an inline error and never corrupts the on-disk
      theme or crashes the thumbnail/preview renderer.
- [ ] Rapid repeated theme switching is idempotent (last click wins) and never cancels
      an in-flight SPEC-0003 data refresh.
- [ ] Low-contrast text/background combinations show a non-blocking inline warning in
      the editor; Save is never refused because of it.
- [ ] Very long theme names are ellipsis-truncated in the picker/editor with a tooltip
      showing the full name.

**Hygiene**
- [ ] No code execution path exists from a theme file (no `XamlReader`,
      `ObjectDataProvider`, or dynamic type loading from manifest content).
- [ ] No new NuGet packages; no new network calls/hosts; no secrets or tokens touched
      anywhere in this feature.
- [ ] No new top-level folders; spec files untouched by the code agent;
      `docs/IMPLEMENTATION_SUMMARY.md` updated after the phase.
