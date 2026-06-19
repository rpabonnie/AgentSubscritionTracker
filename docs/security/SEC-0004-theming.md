# SEC-0004 — Security Gate: Callout Theming (SPEC-0004)

**Task**: TASK-018
**Agent**: security_agent
**Date**: 2026-06-19
**Scope reviewed**: `src/AgentSubscriptionTracker.App/Theming/*`, `ViewModels/ThemeEditorViewModel.cs`,
`ViewModels/ThemePickerViewModel.cs`, `Views/ThemeEditorWindow.xaml(.cs)`,
`Views/ThemePickerPopup.xaml(.cs)`, `Theming/ThemeResourceApplier.cs`, `App.xaml.cs` wiring.
**Build verified independently**: `dotnet build` — 0 warnings / 0 errors (TreatWarningsAsErrors,
AnalysisMode=All) as of this review.

**Gate status: PASS (no high/critical findings)** — with one MEDIUM finding that must be
remediated before milestone close, tracked as a new `security`-spawned task, and one process
note about TASK-017's gate state.

---

## Process note (not a code finding)

`agentTask.json` still shows **TASK-017 (QA) status = `failed`** as of this writing — the
TASK-021 follow-up that was supposed to close QA's remaining findings has not had QA re-run
against it (TASK-017's `notes` field says the orchestrator should reconcile TASK-020's status
and QA should re-verify, but no re-run entry exists in `memory.md` after the TASK-021 code
entry at 00:20 UTC). TASK-018 (security) only `depends_on: ["TASK-016"]`, so this gate is not
blocked by that, but **TASK-019 (review)** explicitly `depends_on: ["TASK-017", "TASK-018"]` and
should not proceed until QA's gate is independently re-confirmed green. Flagging for the
orchestrator — this is not something the security agent can or should resolve.

---

## Findings

### 1. MEDIUM — `theme.json` is read into memory with no file-size cap before parsing

**Location**: `src/AgentSubscriptionTracker.App/Theming/ThemeLoader.cs`,
`LoadOneFolder` (line ~194: `json = File.ReadAllText(manifestPath);`) and
`ReseedBuiltInIfNeeded` (line ~276: `var existingJson = File.ReadAllText(manifestPath);`).

**What's missing**: every other artifact in this pipeline has an explicit, enforced size cap
before it is fully read (the background PNG is capped at 2 MB via `FileInfo.Length` *before*
`File.OpenRead`/decode). `theme.json` has no equivalent check. `File.ReadAllText` reads the
**entire file into a managed string** before `JsonDocument.Parse` ever runs, and
`JsonDocument.Parse` itself has no caller-supplied `JsonDocumentOptions` (so it relies on
System.Text.Json's implicit default `MaxDepth = 64`, which is adequate for depth but does
nothing for raw byte volume).

**Concrete attack scenario**: `%LOCALAPPDATA%\AgentSubscriptionTracker\themes\` is a
user-writable, non-elevated folder (by design — that's where Save-As/fork writes go, and a
user/any other locally-running unprivileged process can drop a folder there directly without
going through the app at all). An attacker who can write to that path (malware running as the
same user, a malicious other app, or the user themselves being tricked into extracting an
archive there) drops `themes\evil\theme.json` containing a multi-hundred-MB or multi-GB file of
syntactically valid-looking JSON (e.g. a giant `"name"` string, or a deeply-but-validly-nested
wrapper before the real content — `JsonDocument.Parse` will still buffer the whole input
regardless of where the depth limit eventually trips). `ThemeLoader.LoadAll()` runs at every
app startup (`App.xaml.cs` composition root) and enumerates every folder under the themes root
unconditionally — there's no per-file size gate before `File.ReadAllText` blocks and allocates.
This is a local (not remote) denial-of-service: it can stall app startup and pressure the
process's memory on every launch until the offending folder is manually removed, and repeated
across many planted folders amplifies the effect. It does not enable code execution or data
exfiltration — `JsonDocument`/`System.Text.Json` has no known deserialization-gadget RCE primitive
here, and parse failures already quarantine cleanly — so this stays MEDIUM, not HIGH, but it is a
real, fixable gap in the same "every artifact gets a size cap" pattern the rest of SPEC-0004
explicitly implements for PNGs.

**Why this isn't already covered**: SPEC-0004 §4.1 (per the code agent's own description of the
serializer) documents "strict, bounded System.Text.Json parsing" but the bound that's actually
implemented is schema/shape strictness (unknown properties ignored, every field type-checked,
numeric ranges enforced) — not an upstream byte-size or stream-read cap. The PNG validator's
`MaxFileSizeBytes` constant has no `theme.json` analog anywhere in `ThemeManifestSerializer` or
`ThemeLoader`.

**Remediation required (root-cause, not a band-aid)**: check `FileInfo.Length` against a small
explicit cap (e.g. 64 KB is generously larger than any legitimate theme.json this schema could
ever produce) before calling `File.ReadAllText`, in both `LoadOneFolder` and
`ReseedBuiltInIfNeeded`, treating an oversized file the same as a malformed one (quarantine /
no-reseed), with a test that plants an oversized `theme.json` fixture and asserts quarantine
rather than a multi-second/multi-MB read. Do **not** "fix" this by just lowering
`JsonDocumentOptions.MaxDepth` alone — depth limiting does not bound a single giant string value
or a wide (not deep) array/object, so a depth-only fix would not close the actual hole.

---

### 2. Verified controls — no findings (adversarial checks performed, all held)

The following were each tested against a concrete exploit attempt and the control held; recorded
here so a future re-review doesn't have to re-derive the same attack attempts from scratch.

- **Path traversal in `imagePath`** — `ThemePathResolver.HasDisallowedSyntax` rejects `..`
  segments (either separator), rooted/absolute paths, UNC (`\\host\share`), and any
  scheme-shaped `x:` other than a literal Windows drive-letter-at-position-1 pattern, all at
  **syntax level before any filesystem call**. `ThemeManifestSerializer` calls
  `ThemePathResolver.IsSyntacticallySafe` at *parse* time (so a manifest with
  `"imagePath": "../../../../Windows/System32/calc.dll"` or
  `"imagePath": "C:\\Windows\\System32\\drivers\\etc\\hosts"` is rejected as
  `PathTraversalRejected` and the whole manifest fails to parse — it never reaches
  `ThemeBackgroundImageValidator`). `ThemeBackgroundImageValidator.Validate` independently
  re-derives containment via `ThemePathResolver.TryResolveContained` (canonicalizes with
  `Path.GetFullPath` and asserts a `StartsWith` against the theme folder + trailing separator,
  case-insensitive) as defense-in-depth before ever calling `File.OpenRead`. Tried and rejected:
  a folder-name-prefix bypass (e.g. theme folder `themes\foo`, candidate resolving into
  `themes\foo-evil\...`) — not possible, because `TryResolveContained` appends
  `Path.DirectorySeparatorChar` to the canonicalized theme folder before the `StartsWith` check,
  so `themes\foo-evil\x` cannot match the `themes\foo\` prefix. Held.

- **Strict/bounded JSON deserialization (shape)** — unknown manifest fields are ignored (no
  polymorphic/`$type` handling anywhere — `JsonSerializer`/`JsonDocument` reflection DTOs are
  flat POCOs with no `TypeNameHandling`-equivalent, so there is no
  insecure-deserialization/gadget-chain surface). Every required field is independently
  type-checked (`ValueKind` checks before `GetString()`/`TryGetInt32()`/`TryGetDouble()`); a
  wrong-shaped value (e.g. `"size": "ten"`, `"brushes": "not an object"`) is rejected as
  `MissingRequiredField`/`InvalidJson`, never coerced. `schemaVersion` must equal the exact
  current constant (1) — a future/unknown version is rejected outright rather than
  best-effort-parsed. Held.

- **PNG validation actually happens before full decode, not just claimed in a comment** — traced
  the real call order in `ThemeBackgroundImageValidator.Validate`: containment →
  `FileInfo.Exists` → `fileInfo.Length > 2MB` (rejects **before** any byte is read) → 8-byte PNG
  signature check (rejects before decode) → structural last-8-bytes-is-IEND check (catches
  truncation that WPF's lenient `PngBitmapDecoder` would otherwise silently zero-fill rather than
  throw on) → decode + forced full `CopyPixels` (so a corrupt-but-signature-valid file fails here,
  not later in an unguarded thumbnail renderer) → dimension check (`>1024x1536` rejected) → real
  alpha-channel `PixelFormat` check (`Bgra32`/`Pbgra32`/`Rgba64`/`Prgba64`/`Rgba128Float`/
  `Prgba128Float` only — a manifest or PNG claiming alpha via metadata alone without one of these
  decoded formats is rejected). Every check both runs in this order *and* short-circuits
  (`return` on first failure) — none of it is a no-op comment. Verified the 7 catalogued PNG
  fixture variants (no-alpha, oversized dimensions, oversized file size, truncated,
  non-PNG-named-.png) exist as real generated fixtures under
  `tests/AgentSubscriptionTracker.Tests/Fixtures/Theming/` rather than mocked assertions. Held.

- **No new network calls introduced** — grepped the entire theming surface
  (`Theming/*`, `ViewModels/ThemeEditorViewModel.cs`, `ViewModels/ThemePickerViewModel.cs`,
  `Views/ThemeEditorWindow.xaml.cs`, `Views/ThemePickerPopup.xaml.cs`) for `HttpClient`,
  `http://`, `https://`, `WebRequest`, `Socket` — zero matches. The editor's "live preview" is
  explicitly hardcoded `SamplePreviewData.Build()` per the code agent's own description and the
  spec's design decision; nothing in this feature touches `api.anthropic.com`/`api.github.com`
  or any other host. Held.

- **Font-family handling cannot load arbitrary font files** — `ThemeFontResolver.ResolveFamily`
  only constructs `new FontFamily(requestedFamily)` *after* confirming
  `string.Equals(installed.Source, requestedFamily, StringComparison.OrdinalIgnoreCase)` is true
  against the live `Fonts.SystemFontFamilies` enumeration. Tried the obvious WPF-specific
  exploit: `FontFamily`'s constructor supports a `"<pack-or-file-uri>#<family name>"` syntax that
  can load an arbitrary font file from disk/network if you control the full string — but that
  string can only reach the constructor here if it first string-equals an already-installed
  family's `Source` (which never contains a `#`/URI prefix for a normal installed font), so a
  manifest value like `"family": "file:///C:/evil.ttf#Arial"` simply fails the equality check
  and falls back to "Segoe UI" rather than ever being constructed. No embedded font file loading
  exists anywhere in this feature. Held.

- **Editor file pickers cannot write outside the themes folder** — `ThemeEditorWindow.OnPickImageClick`
  uses a standard `Microsoft.Win32.OpenFileDialog` (the OS-native picker, scoped to whatever the
  interactive local user themselves navigates to and explicitly selects — not attacker-influenced
  input) and only **reads** that file (`File.ReadAllBytes`) into an in-memory staging validation
  pass; the actual **write** path is never derived from the picked file's location.
  `ThemeStore.WriteThemeFolder` always writes to `Path.Combine(_themesRoot, themeId)` where
  `themeId` is store-generated via `Slugify` (alphanumerics + single hyphens only — verified `..`,
  `/`, `\`, and drive-letter-shaped input all collapse to `theme` or a sanitized slug, never
  surviving as path-traversal syntax) — there is no code path where a value taken from the file
  picker's selected path, or from a manifest's `Name` field, can land in the actual filesystem
  write target unsanitized. `Overwrite`/`Delete` both throw `InvalidOperationException` for a
  built-in id, so Save-As-only-for-built-ins and delete-refusal-for-built-ins are enforced at the
  store layer, not just the view-model layer (defense in depth — confirmed both layers agree).
  Held.

- **Secrets/PII** — no token, credential, or PII handling anywhere in the theming surface; theming
  only touches color/font/image data the user supplies themselves. No logging of any of it found
  (`grep` for `Console.Write`/`Debug.Write`/logging calls in the theming files returned nothing
  beyond doc-comments). N/A for this feature, not a gap.

- **TLS / CORS** — N/A, no network surface exists in this feature (see above).

---

## Findings table

| # | Finding | Severity | Status |
|---|---|---|---|
| 1 | `theme.json` read with no file-size cap before parse — local-process resource-exhaustion DoS via a planted oversized file in a user-writable themes folder | MEDIUM | Open — remediation task to be filed |
| — | Path traversal in `imagePath` (parse-time + load-time defense-in-depth) | — | Verified secure, no finding |
| — | Strict bounded JSON shape/type/range validation, no deserialization gadget surface | — | Verified secure, no finding |
| — | PNG validation order (containment→exists→size→signature→structural-truncation→decode→dimensions→alpha), all before full trust | — | Verified secure, no finding |
| — | No new network calls in theming code | — | Verified secure, no finding |
| — | Font-family resolution cannot load arbitrary font files via pack-URI injection | — | Verified secure, no finding |
| — | Editor file pickers cannot write outside the themes folder; built-ins enforced read-only at the store layer | — | Verified secure, no finding |

## Gate decision

**PASS.** No high/critical finding was found in this adversarial pass — every specifically
flagged attack surface (path traversal, PNG validation ordering, JSON deserialization safety,
new network calls, font-file loading, file-picker write containment) was tested against a
concrete exploit attempt and held. The one MEDIUM finding (theme.json size cap) does not block
the gate per the security standard (only high/critical findings block), but **must** be tracked
as a remediation task before the SPEC-0004 milestone is considered fully closed, and the
TASK-017 process gap noted above should be resolved by the orchestrator before TASK-019 proceeds.
