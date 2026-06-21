# Theme-management UI design system

The theme **picker popup**, theme **editor window**, and **RGBA color picker** share one
hand-written WPF design system (no third-party UI packages — per the supply-chain rules in
`CLAUDE.md`). This document records the structure so future changes stay consistent.

## Files

| File | Role |
|---|---|
| `src/.../Themes/EditorDesignSystem.xaml` | **Theme-independent** tokens (spacing, radii, type ramp) + **all keyed control templates**. Merged into **each window's** `Window.Resources` (see contract 4). |
| `src/.../Themes/EditorChromeDark.xaml` | Dark **Editor\*** brush palette (Catppuccin Mocha–derived). |
| `src/.../Themes/EditorChromeLight.xaml` | Light **Editor\*** brush palette (Catppuccin Latte–derived). |
| `src/.../Views/UiState.cs` | Attached `HoverBrush`/`PressedBrush` so one button template serves every variant. |
| `src/.../Utils/WindowTitleBarTheme.cs` | Opts the native title bar into Win11 dark/light (dwmapi `LibraryImport`). |

## Hard contracts (do not break)

1. **`EditorDesignSystem.xaml` is keyed-only.** It contains no implicit (`TargetType`-only)
   styles, so it never bleeds into the live `CalloutContent` preview — that must keep rendering
   the *edited* theme. Windows opt in via `Style="{StaticResource ...}"` or a scoped implicit
   style (in the editor's left `ScrollViewer.Resources`) `BasedOn` these keys.
2. **The two chrome files must define the IDENTICAL `Editor*` key set.** The editor swaps
   `Window.Resources.MergedDictionaries[0]` wholesale on the Light/Dark toggle; a key present in
   only one file renders nothing after a swap. All chrome brushes are consumed via
   `DynamicResource` so the swap re-resolves them.
3. **Hex is alpha-first** (`#AARRGGBB` / `#RRGGBB`) — WPF's `ColorConverter` does not accept
   alpha-last `#RRGGBBAA`. The `HexBox` style uses `MaxLength=9` / `Width=116` accordingly. The
   color fields use the existing code-behind `.Text` pipeline (the `HexToBrush` converter is
   one-way), not bindings.
4. **Merge the design system PER-WINDOW, never into `Application.Resources`.** Startup applies
   the active theme via `ThemeResourceApplier.Apply(Application.Resources, …)`, which calls
   `MergedDictionaries.Clear()` — so anything merged at app scope is wiped on the first theme
   apply (this caused every theme window to crash with "Cannot find resource 'RadiusOverlay'").
   Each window therefore merges `EditorDesignSystem.xaml` itself. In the editor it sits at
   `MergedDictionaries[1]` (chrome stays at `[0]` so the Light/Dark swap doesn't touch it); the
   color picker merges it via XAML and the caller inserts chrome at `[0]` after `InitializeComponent`.
5. **A theme's background image must be exposed pre-composed, not as a raw `ImageSource`.**
   `CalloutBackgroundImage` (the raw decoded image) existed for a long time with nothing in
   `CalloutContent.xaml` ever consuming it — themes with a background image silently rendered
   the solid fallback color only. `ThemeResourceApplier` now also writes `CalloutBackgroundPaint`
   (an `ImageBrush` over the image, or the same solid `CalloutBackgroundBrush` when there's no
   image), and `CalloutContent.xaml`'s root `Border.Background` binds to that single key via
   plain `DynamicResource`. Do **not** reach for a `MultiBinding`+`RelativeSource Self` converter
   here (the pattern `SeverityToBrushConverter`/`BarTrackBrushMultiConverter` use elsewhere in
   this file) — that only re-evaluates when one of its own `Binding` sources changes, not on a
   resource-dictionary write, so it would not refresh on theme/preview updates. The editor's own
   live-preview pane (`ThemeEditorWindow.UpdatePreview`) separately needs
   `ThemeEditorViewModel.BackgroundImagePreview` (decoded from the just-imported bytes, or the
   source theme's existing image) wired into the preview `LoadedTheme.BackgroundImage` — it
   used to hardcode `null` there too.

## Tokens

- **Spacing** (`Thickness`): `LabelToFieldGap` 0,0,0,4 · `RowGap` 0,0,0,10 · `CardPad` 20 ·
  `CardGap` 0,0,0,16 · `WindowPad` 20 · `FieldPad` 10,0,10,0 · `ButtonPad` 16,0,16,0.
- **Radii** (`CornerRadius`): `RadiusCtl` 6 · `RadiusCard` 10 · `RadiusOverlay` 12 · `RadiusSwatch` 5.
- **Type ramp** (keyed `TextBlock` styles, Segoe UI Variable → Segoe UI): `TextCaption` 12 ·
  `TextBody` 14 · `TextBodyStrong` 14 SemiBold · `TextSubtitle` 16 SemiBold · `TextTitle` 20
  SemiBold · `TextSectionHeader` (accent subtitle).

## Control styles

`Card` · `ButtonBase` / `AccentButton` / `SubtleButton` / `DangerButton` / `IconSwatchButton`
(28px chip over an `AlphaCheckerBrush`) · `TextBoxBase` / `HexBox` (96px, mono, upper) /
`FontSizeBox` (64px) · `ThemedComboBox` + `ThemedComboBoxItem` · `ChannelSlider` (rail
`Background` is painted per-channel from `ColorPickerWindow` code-behind) · `SegmentItemLeft` /
`SegmentItemRight` (the editor Light|Dark segmented toggle) · `ThinScrollBar`.

Every interactive control has rest / hover / keyboard-focus states (4–8px radii, accent focus
ring). `DropShadowEffect` is reserved for floating layers only (picker popup root, combo popup).

## Window layouts

- **Editor** — title + segmented Light|Dark toggle; a scrolling column of section **cards**
  (Identity, Background, Typography, Brush colors, Severity colors) with one shared label column
  (`Grid.IsSharedSizeScope`); color rows are `label · swatch · 96px hex`; a framed live-preview
  pane on the right; a sticky Cancel / Save action bar. Window sizing lives in the code-behind
  (`Width/Height` clamps), not XAML.
- **Picker popup** — rounded, shadowed dark flyout; a `WrapPanel` grid of theme **tiles** (live
  preview + name + ✓ + palette-dot strip), active tile shown with an accent ring + tint, a dashed
  "+ Add new theme" ghost tile, and a header ✕ close button. Edit / Delete are revealed on
  hover/focus on a **solid floating bar** (not transparent buttons) so they stay legible over light
  previews; while hidden the bar is click-through so it never blocks "click to use this theme".
  Dismissal: ✕ button, `Esc`, or click-away — the popup activates itself (deferred, after the shell
  positions it next to the callout) so it can receive those.
- **Color picker** — dark chrome (mirrors the editor's current Light/Dark), rounded swatch over a
  checkerboard, per-channel tinted slider tracks, accent OK button.

## Verification status

Build is clean under `TreatWarningsAsErrors` + `AnalysisMode=All`; `dotnet test` is green (262);
the app starts without error; and all three windows were confirmed to construct + lay out + render
without exceptions (verified by rendering each `Window` to a PNG via `RenderTargetBitmap`).
