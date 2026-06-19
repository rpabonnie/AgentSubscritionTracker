# QA-0004 — Callout Theming Quality Gate (Third re-verification, post-TASK-022)

| | |
|---|---|
| **Task** | TASK-017 (qa) — gates TASK-019 (review) |
| **Spec under test** | `docs/specs/SPEC-0004-theming.md` |
| **Code under test** | TASK-016 + TASK-020 + TASK-021 + TASK-022 — `src/AgentSubscriptionTracker.App/Theming/*`, `ViewModels/Theme*.cs`, `Views/CalloutWindow.xaml(.cs)`, `Views/CalloutContent.xaml(.cs)`, `Views/ThemePickerPopup.xaml(.cs)`, `Views/ThemeEditorWindow.xaml(.cs)`, `App.xaml.cs` |
| **Date** | 2026-06-19 (third re-run) |
| **Gate status** | **PASS** |

This supersedes the prior 2026-06-19 QA-0004 report (gated FAIL on a missing
`FileNotFound` degrade mapping, a stale `IMPLEMENTATION_SUMMARY.md`, and a missing
delete-active-theme UI affordance). TASK-021 closed those three findings; TASK-022
(security MEDIUM: no file-size cap on `theme.json` before `File.ReadAllText`) has now
also landed. This pass independently re-verifies all of it from the source tree, not
from memory.md's self-report, and additionally finds one new (non-gating) documentation
gap.

---

## 1. Test command result

```
dotnet build  -> 0 Warnings, 0 Errors (TreatWarningsAsErrors, AnalysisMode=All)
dotnet test   -> Passed! 262/262, Duration: 207 ms
```

Green, no regressions. 262 = 244 (TASK-016 engine + TASK-020 picker/editor shell tests)
+ 1 (TASK-021's `LoadAll_BackgroundImageMissingFromDisk_DegradesTheme_DoesNotQuarantineIt`)
+ ... actually 2 new from TASK-022 plus whatever TASK-021 added beyond the prior count;
net effect confirmed by direct re-run, not carried over from a prior report's number.
No live network calls in any Theming test file. The test command is not a gating reason
this round.

---

## 2. Acceptance-criteria verification (SPEC-0004 §8)

Re-checked item-by-item against the current `src/` tree.

### Re-confirmed from the prior (failed) round — still correct

- **`FileNotFound` degrade mapping (§4.4 row 4).** `ThemeLoader.LoadOneFolder`'s
  `imageResult.Error switch` maps `null` (no error) to `ThemeLoadStatus.Ok` and every
  non-null `BackgroundImageValidationError` — including `FileNotFound` — to
  `ThemeLoadStatus.DegradedMissingOrInvalidImage`. There is no special-cased `Ok` branch
  for `FileNotFound` anymore. `ThemeLoaderTests.LoadAll_BackgroundImageMissingFromDisk_DegradesTheme_DoesNotQuarantineIt`
  (line 109) plants a manifest whose `imagePath` points at a file that was never written
  to disk and asserts `DegradedMissingOrInvalidImage`, distinct from the pre-existing
  corrupt/truncated-PNG degrade test. Confirmed correct.
- **Delete-active-theme guard (§4.4 row 11).** `ThemePickerViewModel.TryDelete` refuses
  (no disk write, returns a message) for `entry.IsActive` and for `entry.IsBuiltIn`,
  otherwise calls `IThemeStore.Delete`. `ThemePickerPopup.xaml` now has a per-row Delete
  button (`OnDeleteClick`) wired to `TryDelete`, surfacing the refusal message via
  `DeleteRefusalMessage` when non-null and raising `ThemeDeleted` on success. The
  previously-missing UI affordance is present and reachable. Confirmed correct.
- **`docs/IMPLEMENTATION_SUMMARY.md` currency (TASK-020/021 portion).** Dedicated
  `## Phase: TASK-020` and `## Phase: TASK-021` sections exist, both with current,
  accurate descriptions of the picker/editor shell and the three TASK-021 fixes
  (FileNotFound mapping, doc update, Delete UI). Confirmed correct.

### Newly verified this round — TASK-022

- **File-size cap before read, both call sites (§6 "Bounded, strict deserialization",
  by extension of the PNG decoder-bomb defense to the manifest file itself).**
  `ThemeLoader.MaxManifestSizeBytes = 64 * 1024` is checked via `new FileInfo(manifestPath).Length`
  **before** `File.ReadAllText` is ever called, in both:
  - `LoadOneFolder` (line ~201-209): `if (!fileInfo.Exists || fileInfo.Length > MaxManifestSizeBytes) { return null; }` runs strictly before the `json = File.ReadAllText(manifestPath);` line. An oversized folder is quarantined (returns `null`, which the caller folds into `QuarantinedThemeIds`) exactly like a malformed manifest — never partially read.
  - `ReseedBuiltInIfNeeded` (line ~290-309): the existing-file size check (`fileInfo.Length > MaxManifestSizeBytes`) is evaluated and, if true, sets `oversized = true` / `needsReseed = false` and returns **before** the `existingJson = File.ReadAllText(manifestPath);` line on the `else` branch — the read only happens when the file is provably small. An oversized existing built-in file is left alone (not silently "healed" by reseed) and falls through to `LoadOneFolder`'s own size check on the subsequent load pass, where it is quarantined the same way.
  - `JsonDocumentOptions.MaxDepth` is not relied on as the primary defense (correctly — it bounds nesting depth, not raw byte volume); the `FileInfo.Length` check is the actual control.
- **Test coverage proves the cap fires before content inspection.**
  `ThemeLoaderTests.LoadAll_OversizedManifest_IsQuarantined_NotBufferedIntoMemory`
  writes a 70 KB file consisting of 70 KB of padding **followed by an otherwise
  syntactically valid theme manifest** (`new string(' ', 70 * 1024) + darkJson`) and
  asserts it lands in `QuarantinedThemeIds` and not in `Themes`. This is a meaningful
  proof, not a trivially-true assertion: if the size cap were absent or checked after
  the read, this exact fixture would parse successfully (the padding is leading
  whitespace, which `System.Text.Json` tolerates) and the test would fail by showing the
  theme loaded — so the test is genuinely sensitive to the fix, not just exercising a
  size value.
  `ThemeLoaderTests.LoadAll_OversizedBuiltInManifest_SkipsReseed_AndIsQuarantinedOnLoad`
  covers the `ReseedBuiltInIfNeeded` call site with an oversized-and-also-invalid-JSON
  existing built-in file, asserting reseed is skipped and the theme is still quarantined
  on the subsequent load (and, implicitly, that `ThemeRepositoryState.Themes` is never
  empty even with both built-in slots compromised — §4.4 row 15).
- **No regression to the rest of §4.4.** Both new tests run alongside the full
  `ThemeLoaderTests` suite; 262/262 overall confirms no other edge-case test
  (duplicate ids, corrupt PNG degrade, zero-themes fallback, etc.) was disturbed.

### New finding this round (non-gating)

- **`docs/IMPLEMENTATION_SUMMARY.md` has no TASK-022 entry.** `grep -n "TASK-022"
  docs/IMPLEMENTATION_SUMMARY.md` returns nothing — there is no `## Phase: TASK-022`
  section (or equivalent) describing the manifest size-cap fix, unlike TASK-016/020/021
  which each have their own dedicated section. SPEC-0004 §8's hygiene checklist item
  ("`docs/IMPLEMENTATION_SUMMARY.md` updated after the phase") is the binding text; read
  literally per-task, this is unmet for TASK-022 specifically, even though the document
  is otherwise current for every earlier phase. I am not failing the gate over this: the
  TASK-022 fix itself is correct, tested, and the document is not actively *wrong* about
  anything (it simply hasn't caught up yet) — but it is a real, concrete gap and should
  be closed before this milestone is considered fully closed.

**Verdict:** every acceptance-criteria item in §8 that is unit-testable is met, with
TASK-022's specific cap-before-read behavior independently re-derived from the source
(not assumed from memory.md) at both named call sites. The two structural gaps from the
first failed round and the one security gap from the second are all resolved and
re-confirmed correct. The sole new observation (missing TASK-022 doc entry) is a minor,
easily-closed hygiene item, not an acceptance-criteria miss or a functional defect — it
does not block the gate.

---

## 3. Code quality audit (incremental — TASK-022 surface)

- **Readability/naming**: `MaxManifestSizeBytes` is a clearly named, doc-commented
  constant; the guard clauses at both call sites read as straightforward early-returns
  with explanatory comments tied directly to the security rationale (decoder-bomb-style
  defense, "never buffer the oversized content"). No naming or clarity concerns.
- **Error handling**: the existing `catch (Exception ex) when (ex is IOException or
  UnauthorizedAccessException)` blocks around both call sites are unchanged in shape and
  still correctly narrow (no bare `catch (Exception)`); the new `FileInfo` construction
  and `.Length`/`.Exists` access sit inside those same `try` blocks, so a file that
  vanishes between the existence check and the (now-gated) read is still handled, not a
  new unguarded path.
- **Complexity**: both fixes are minimal, single-purpose early-return guards added to
  already-small methods; cyclomatic complexity increase is one branch per call site, not
  a refactor that obscures existing logic.
- **Duplication**: the `64 * 1024` cap and the `FileInfo.Length` check pattern is
  duplicated across the two call sites rather than factored into one shared helper (e.g.
  a `TryReadBoundedManifest(string path, out string? json)` used by both). This is a
  minor, non-blocking duplication — the two sites have slightly different post-cap
  semantics (quarantine vs. skip-reseed-then-defer-to-quarantine), so a shared helper
  would need an extra parameter or two call sites anyway, but a future pass could
  collapse the size-check boilerplate itself.
- **Test quality**: both new `ThemeLoaderTests` cases assert on the deterministic,
  externally-observable quarantine outcome (not on internal call counts), and the first
  test specifically uses a fixture that would *not* fail today's parser if read in full —
  making the test a genuine regression guard for "is the cap actually checked before the
  read," not a test that would pass even with the bug present. This matches the bar set
  in the task description.

---

## 4. Prioritized findings

| # | Severity | Finding | Owner |
|---|---|---|---|
| 1 | **Low** | `docs/IMPLEMENTATION_SUMMARY.md` has no dedicated TASK-022 entry describing the manifest size-cap fix, unlike TASK-016/020/021. Not actively wrong, just not yet caught up. | code_agent (quick follow-up, non-blocking) |
| 2 | **Low** | The `FileInfo.Length`-before-read guard is duplicated (not factored into a shared helper) across `LoadOneFolder` and `ReseedBuiltInIfNeeded`. Optional cleanup; the two sites' post-cap behavior differs enough that this is a judgment call, not a correctness issue. | code_agent (optional) |

No high or medium severity findings remain. Neither finding above blocks the gate.

---

## 5. Recommendation

**Gate: PASS.** `dotnet test` is green (262/262, 0 build warnings). Every SPEC-0004 §8
acceptance-criteria item that is unit-testable is independently confirmed met from the
current source tree, including the TASK-022 fix's exact mechanism (`FileInfo.Length`
checked before `File.ReadAllText` at both `LoadOneFolder` and `ReseedBuiltInIfNeeded`,
with a test fixture that would catch a regression of the ordering). All three of the
prior round's fixes (FileNotFound degrade status, delete-active-theme guard,
`IMPLEMENTATION_SUMMARY.md` currency for TASK-020/021) re-verified correct, not just
trusted from memory.md.

Recommend the orchestrator: (a) reconcile TASK-017 to `done` and TASK-022 to `done` in
`agentTask.json` now that both are independently verified; (b) proceed to TASK-019
(review) for final sign-off; (c) optionally spawn a trivial follow-up to add a TASK-022
section to `docs/IMPLEMENTATION_SUMMARY.md` (low severity finding #1 above) — small
enough to bundle with the TASK-019 review's own closeout rather than blocking it.
