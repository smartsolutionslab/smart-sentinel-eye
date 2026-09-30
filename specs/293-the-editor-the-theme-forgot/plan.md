# Plan 293 — The editor the theme forgot

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2342

## 1. Where this lives

No bounded context, no backend, no messaging, no `Shared.Contracts` change. Frontend only,
in the shared UI package consumed by both apps:

| Layer | Path | Role here |
|---|---|---|
| Tokens (semantic layer) | `apps/shared/src/ui/tokens/tokens.css` | +3 pinned content roles (spec §5) |
| Primitives | `apps/shared/src/ui/primitives/{Input,Button}.tsx` | **Consumed, not modified** |
| Composites | `apps/shared/src/ui/composites/{OverlayEditor,BackdropControls,OverlayGeometryFields}.tsx` | US1 conversion |
| Composites (wall + editor) | `apps/shared/src/ui/composites/overlayLabelStyle.ts` | US2 conversion |
| Guards | `tests/Architecture.Tests/{SharedUiTokenUsageTests,DesignTokenLayerTests}.cs` | carve-outs emptied; one new fact |
| e2e | `e2e/overlays.spec.ts` | one new theme test |

`tailwindTheme.ts` is **not** modified: every class used below already resolves through it
(`bg-*`, `text-*`, `border-*`, `accent-*` from `extend.colors`; spacing 1/2/3/4; `rounded-lg`;
`text-xs`/`text-sm`; `font-medium`). The three new roles are cited only from inline style
strings, so they get no Tailwind key (ADR-0036).

Boundary rules unchanged: `apps/shared` imports nothing from either app (this is exactly
why the camera-label fix is out of scope, spec §4.2).

## 2. Tokens added (`tokens.css`, `:root` semantic block, after `--color-bg-video`)

```css
/* Content roles — pinned: no theme block may redeclare these (spec 293 §5,
   DesignTokenLayerTests.Content_roles_are_pinned_across_themes). They name
   what the wall paints or what a test field is, not console chrome. */
--color-bg-video-inverse: var(--white);                                   /* US1 */
--color-bg-label: color-mix(in oklch, var(--white) 85%, transparent);    /* US2 */
--color-fg-on-label: var(--gray-900);                                    /* US2 */
```

US1 adds only the first; US2 adds the other two. Nothing is added to `[data-theme='light']`
or `[data-theme='high-contrast']`.

## 3. The mapping — every converted value

### 3.1 `OverlayEditor.tsx`

| Line(s) | Today | Becomes |
|---|---|---|
| 202 | `outline: '2px solid #ffffff'` | `'2px solid var(--color-bg-video-inverse)'` (inline, kept) |
| 204 | `boxShadow: 'inset 0 0 0 4px #000000'` | `'inset 0 0 0 4px var(--color-bg-video)'` (inline, kept) |
| 209 | `#1f2937` ×2, `#111827` ×2 | `var(--color-border-subtle)` ×2, `var(--color-bg-elevated)` ×2 — lighter stripe first, as today |
| 218 | `backgroundColor: '#ffffff'` | `'var(--color-bg-video-inverse)'` |
| 219, 227 | `backgroundColor: '#000000'` | `'var(--color-bg-video)'` — the wall's own letterbox role (`CameraViewer.tsx:393`) |
| 605, 608, 609 | `position: 'relative'`, `overflow: 'hidden'`, `borderRadius: 8` | `className="relative overflow-hidden rounded-lg"` (`--radius-lg` = 8px); `width`/`height` + `canvasBackgroundStyle` stay inline |
| 680 | `{ display: 'flex', gap: 8, marginTop: 12 }` | `mt-3 flex gap-2` |
| 681-698 | two bare `<button aria-disabled={!canX}>` | `<Button variant="secondary" unavailable={!canX} …>` — keeps `type="button"` (Button's default), `data-testid`, `aria-keyshortcuts`, `onClick`. `Button` emits `aria-disabled={unavailable}` → the same `"true"`/`"false"` as today. |
| 707 | `{ display: 'grid', gap: 12, marginTop: 12 }` | `mt-3 grid gap-3` |
| 708, 721 | `{ display: 'flex', flexDirection: 'column', gap: 4 }` | `flex flex-col gap-1` |
| 709, 722 | bare `<span>` captions | `text-sm font-medium text-fg-primary` (FormField's label classes) |
| 710-719 | `<input type="text" style={{ padding: 8, fontSize: 14 }}>` | `<Input …>` from `../primitives/Input.js`, no `style`; every other prop unchanged |
| 723-732 | `<input type="range">` (unstyled) | same element + `className="w-full accent-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring"` |

The FR-003 comment at 390-393 ("the file is inline-styled throughout …") is updated to say
the ring stays inline because it is state-driven and observed via `element.style`, not
because the file is inline-styled. The `CHECKERBOARD_BACKGROUND` comment (207-208) is
updated to name spec 293 instead of "byte-for-byte".

### 3.2 `BackdropControls.tsx`

| Line | Today | Becomes |
|---|---|---|
| 29-31 | header comment "Inline styles … #2342 converts this whole file" | deleted (now false) |
| 32 `FIELDSET_STYLE` | flex, gap 16, no border/padding/margin | `m-0 flex gap-4 border-0 p-0` |
| 33 `LEGEND_STYLE` | 14px, padding 0, mb 4 | `mb-1 p-0 text-sm font-medium text-fg-primary` |
| 34 `RADIO_LABEL_STYLE` | flex, center, gap 4, 14px | `flex items-center gap-1 text-sm text-fg-primary`; the radio `<input>` gets `accent-accent` |
| 35 `SELECT_STYLE` | padding 8, 14px | Input's class string verbatim: `block w-full rounded-md border border-fg-muted bg-bg-elevated px-3 py-2 text-sm text-fg-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring disabled:border-border-subtle disabled:text-fg-disabled` (native `<select>` kept, spec §4.3) |
| 36 `BUTTON_STYLE` | `8px 12px`, 14px | `<Button variant="secondary">` (its own `px-4 py-2 text-sm`); Capture keeps native `disabled` (spec §4.4) |
| 37 `NOTICE_STYLE` | 12px, `#6b7280`, m 0 | `m-0 text-xs text-fg-muted` |
| 38 `ALERT_STYLE` | 13px, `#b91c1c`, m 0 | `m-0 text-sm text-accent-fault` |
| 69 | grid, gap 12, mt 12 | `mt-3 grid gap-3` |
| 154 | grid, gap 8 | `grid gap-2` |
| 177 | flex, gap 8 | `flex gap-2` |

All seven `*_STYLE` constants and the `CSSProperties` import are removed. The option text
at line 165 (`{camera.name}`) is **not touched** (spec §4.2).

### 3.3 `OverlayGeometryFields.tsx`

| Line | Today | Becomes |
|---|---|---|
| 80 `FIELD_INPUT_STYLE` | padding 8, 14px, width 100%, border-box | `<Input …>` (`w-full`; preflight is already border-box) |
| 81 `FIELD_ALERT_STYLE` | `#dc2626`, 12px | `text-xs text-accent-fault` |
| 82 `FIELD_STATUS_STYLE` | `#b45309`, 12px | `text-xs text-accent-warning` |
| 85 `FIELDSET_STYLE` | no border/padding/margin | `m-0 border-0 p-0` |
| 86 `LEGEND_STYLE` | 14px, p 0, mb 4 | `mb-1 p-0 text-sm font-medium text-fg-primary` |
| 118 | `display: 'grid'` | `grid` |
| 120 | `…textStyle, gridArea: '1 / 1', visibility: 'hidden'` | `clsx(textClassName, 'col-start-1 row-start-1 invisible')` |
| 124 | `…textStyle, gridArea: '1 / 1'` | `clsx(textClassName, 'col-start-1 row-start-1')` |
| 241 | grid, gap 12, mt 12 | `mt-3 grid gap-3` |
| 244 | grid, `repeat(4, 1fr)`, gap 12 | `grid grid-cols-4 gap-3` |
| 257, 258 | flex column gap 4 | `flex flex-col gap-1` |
| 259 `<span>{spec.label}</span>` | unstyled | `text-sm font-medium text-fg-primary` |

`ReservedMessageSlot`'s `textStyle: CSSProperties` prop becomes `textClassName: string`.
**Invariant it must keep:** the hidden candidates and the live child carry the *same* text
class, or the reserved height stops equalling the live message's height and the Save button
moves again (#2366). The two e2e "does not move the Save button" tests are the check.

### 3.4 `overlayLabelStyle.ts` (US2)

| Property | Today | Becomes |
|---|---|---|
| `background` | `'rgba(255, 255, 255, 0.85)'` | `'var(--color-bg-label)'` |
| `color` | `'#111827'` | `'var(--color-fg-on-label)'` |
| `fontWeight` | `600` | `'var(--font-weight-semibold)'` |
| `padding` | `'0 4px'` | `'0 var(--space-1)'` |
| `display`/`alignItems`/`justifyContent`/`fontSize` | — | unchanged |

Still pure, allocation-only, DOM-free — the doc comment's render-leg paragraph stays true.

## 4. Guards

### 4.1 `SharedUiTokenUsageTests`

- US1 removes the three entries for `OverlayEditor.tsx`, `BackdropControls.tsx`,
  `OverlayGeometryFields.tsx`; US2 removes `overlayLabelStyle.ts`. Removing an entry
  **first** is the red: `No_colour_literal` (and `No_stock_palette…` is unaffected) then
  names exactly the file(s) still to convert.
- With the list empty, `Each_carve_out_still_exists_and_still_violates` has nothing to
  iterate. It is **kept** (a future carve-out is still allowed with a reason), and its
  hard-coded `"#2342"` check is generalised to "names an issue" (`#\d+`), since #2342 will
  be closed. With an empty list both forms check nothing today — this is not a weakening.
  The class `<summary>` "exactly four files" paragraph is rewritten to record that the list
  was emptied by spec 293.

### 4.2 `DesignTokenLayerTests.Content_roles_are_pinned_across_themes` (new fact)

For each name in a static list: declared in `:root`; **absent** from every
`[data-theme=…]` block. US1's list: `--color-bg-video`, `--color-bg-video-inverse`. US2
appends `--color-bg-label`, `--color-fg-on-label`. Reuse the file's existing block parser
(the one `A_theme_redeclares_only_semantic_names_root_already_has` uses) — no new parser.
Existing class, so no shard-filter entry is needed.

**Counterfactual (must be run and quoted):** temporarily add `--color-bg-video-inverse` to
the `light` block → the fact fails naming it.

## 5. Tests — what is characterisation, what is red

**Characterisation (observe green on `34d46a23` before any edit; must pass unmodified
after):**

- `OverlayEditorKeyboard.test.tsx` (incl. 681-709 focus-ring presence/absence — this is
  also the jsdom `var()` canary, §7 R1), `OverlayEditorUndo.test.tsx`,
  `OverlayGeometryFields.test.tsx`, `FrameCapture.test.tsx`, `PlaceholderPreviewPanel.test.tsx`,
  `useOverlayEditHistory.test.ts`, `overlayLabelStyle.test.ts`.
- `OverlayEditorCharacterisation.test.tsx` **except** the checkerboard test (92-101) and its
  `GRADIENT_RENDERED` constant (63-71).
- `OverlayEditorBackdrop.test.tsx` **except** lines 217, 228, 239-240, 273.
- `OverlayLabelParity.test.tsx` **except** line 80 (US2); the editor == wall assertions are
  the parity net and must not move.
- `OverlayLabelCharacterisation.test.tsx` **except** 73, 81, 82, 96 (US2).
- `apps/management-web/src/features/overlays/OverlayEditorDialog*.test.tsx`,
  `OverlayEditorReseedRegression.test.tsx`.
- `e2e/overlays.spec.ts` — all existing tests, notably the White-field computed
  `rgb(255, 255, 255)` (62-74: the real-browser proof `--color-bg-video-inverse` resolves to
  white) and the two #2366 Save-button-stability tests (the `ReservedMessageSlot` net).

**Red (observed failing before the implementation, failure quoted in the PR):**

- US1: `SharedUiTokenUsageTests` with three carve-outs removed; `Content_roles_…` with
  `--color-bg-video-inverse`; the superseded jsdom assertions rewritten to expect the `var(...)`
  strings (checkerboard, white, black, letterbox); a new `e2e/overlays.spec.ts` test —
  checkerboard `background-image` differs between `dark` and `light`; a new
  `apps/management-web/src/styles/tokens.build.test.ts` case — `accent-accent` compiles to
  `accent-color: var(--color-accent)` (if it is green on arrival because the harness
  generates on demand, record that and treat it as a compile pin, not red evidence).
- US2: `SharedUiTokenUsageTests` with the fourth carve-out removed; the fact's list extended;
  the superseded label assertions expecting `var(...)`; the e2e theme test extended — label
  computed `background-color` and `color` identical across all three themes (this part is
  green today and must stay green: record it as characterisation, not red).

## 6. Delivery shape

One PR, two stories, commits in order US1 → US2, **each commit building and testing green
on its own** (ADR-0087 rebase-merge lands them individually). US2 is separable: if §5's
interpretation is challenged, US1 ships alone and `overlayLabelStyle.ts` keeps its
(`#2342`-tagged) carve-out until it is settled.

## 7. Risks

- **R1 — jsdom and `var()`.** jsdom 30.1.1's CSSOM must keep `var(--x)` in
  `background`, `background-color`, `outline`, `box-shadow`, `color`, `font-weight`,
  `padding`. If it drops any (reads back `''`), the Keyboard suite's ring tests go red on a
  characterisation run — that is the canary. **Do not fall back to literals.** Stop and
  report; the alternative (asserting `getAttribute('style')`) is a test-shape decision for
  the orchestrator, not a silent workaround.
- **R2 — computed `color-mix` serialisation.** A browser serialises a `color-mix(in oklch …)`
  result in OKLCH, not as `rgba(…)`. The e2e label assertion therefore compares themes to
  each other, never to a literal.
- **R3 — `ReservedMessageSlot` height drift** (§3.3 invariant).
- **R4 — `Button` inside `OverlayEditorDialog`'s `<form>`.** `Button` defaults
  `type="button"`, so Undo/Redo/Capture/Cancel still never submit.
