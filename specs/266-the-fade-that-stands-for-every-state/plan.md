# Plan 266: The fade that stands for every state

**Spec**: [spec.md](spec.md) · **Issue**: #2336 · **Phase**: 2 (Plan)

## Constitution / ADR check

| Rule | How this plan meets it |
|---|---|
| §I, no outbound dependency | Nothing is fetched. |
| §II, value objects | N/A. No C# domain code; the one C# file is an architecture test. |
| §III, context isolation | N/A. No bounded context. `apps/shared` is already the one place both apps import from. |
| §IV, latency | N/A. No file the kiosk imports changes behaviour (spec §5). The spec-225 render-leg check is still read in Phase 5. |
| §Testing / ADR-0139 | Red first; pins declared green in advance; three old assertions rewritten by the test-writer, not the engineer (spec §6). |
| ADR-0077 / ADR-0078 | Radix Slot kept; state colours are Tailwind utilities over CSS custom properties. |
| ADR-0146 | Item 5 implemented per variant. Triad removed from affordance in `Button` and every focus indicator. No blur, no shadow, no new motion; the wall imports none of the changed components. |
| ADR-0148 | Five new roles, all semantic, all `var(role)` or `color-mix(in oklch, var(role) N%, var(x) \| transparent)`. No theme block needs them redeclared (§2). No primitive is cited outside `tokens.css`. |
| ADR-0151 | `unavailable` keeps focus and `onClick`; the disabled treatment changes colour only. `busy` composes with, and never replaces, `disabled` / `unavailable`. |

## Bounded context and layers

None. Frontend design-system work only; no backend, no contracts, no messaging, no
AppHost resource.

```
apps/shared/src/ui/tokens/tokens.css            +5 semantic roles           US1
apps/shared/src/ui/tokens/tailwindTheme.ts      +5 colour keys              US1
apps/shared/src/ui/primitives/Button.tsx        state matrix; busy          US1, US2
apps/shared/src/ui/primitives/ConfirmDialog.tsx busy on confirm             US2
apps/shared/src/ui/primitives/Input.tsx         focus outline; disabled     US3
apps/shared/src/ui/composites/DataTable.tsx     focus outline               US3
apps/shared/src/ui/composites/ChainRecoveryNotice.tsx  disabled label       US3
apps/management-web/src/features/layouts/GridDesigner.tsx  focus outline   US3
apps/management-web/src/features/**/{8 dialogs/panels}     busy={…}        US2 (§6)
tests/Architecture.Tests/InteractionStateTests.cs          NEW             all
tests/Architecture.Tests/TypeScriptSource.cs               NEW (lifted)    helper
apps/{management-web,kiosk-web}/src/styles/tokens.build.test.ts   +candidates
apps/shared/src/ui/primitives/Button.test.tsx              rewrite 2 + new
apps/shared/src/ui/primitives/ConfirmDialog.test.tsx       + busy case
apps/management-web/src/features/{layouts,overlays}/*SaveGate.test.tsx  1 line each
e2e/interaction-states.spec.ts                             NEW (chromium project)
```

---

## 1. Blast radius, measured on `7aab60de`

| Surface | Count | What changes for it |
|---|---|---|
| `<Button>` elements | **51 in 16 files**, all in `apps/management-web` + `apps/shared` (26 `secondary`, 7 `ghost`, 2 `danger`, the rest `primary`) | Every one: new hover, pressed, focus and disabled. Every `primary` turns from green (`#00c853`) to cyan (`--color-accent`). `primary`/`ghost`/`danger` gain a 1 px transparent border (§3), so they grow 2 px in height and now match `secondary`, which already had one. |
| `<Input>` elements | 25 | Focus ring colour green → cyan, drawn as an outline. Disabled: no opacity; `fg-disabled` text, `border-subtle`. |
| `<DataTable>` users | 3 | Sort-header focus ring colour and mechanism. |
| `GridDesigner` preset chips | 1 file | Focus ring colour and mechanism only; the green *selection* fill stays (§7). |
| `ChainRecoveryNotice` | 1 file (console dialogs only) | Unavailable link-buttons: `opacity-50` → `text-fg-disabled`. |
| `apps/kiosk-web` | **0 components** | Imports none of the above. Loads `tokens.css`, which gains five unreferenced properties. |
| Existing tests asserting old classes | 3 files, 4 assertions | Rewritten in 4a (spec §6). No e2e locates a Button by class or colour; all use roles and names, so none moves. |

**Functional behaviour does not change**: same handlers, same `disabled`/`aria-disabled`
semantics, same focus behaviour. **Visual behaviour changes on every console action**,
which is why the phase-4a colour is red and not characterisation.

## 2. Tokens: five derived roles (implementation detail inside ADR-0148)

### 2.1 Fault states — `danger` has its own derivation

```css
--color-accent-fault-hover:   color-mix(in oklch, var(--color-accent-fault) 88%, var(--white));
--color-accent-fault-pressed: color-mix(in oklch, var(--color-accent-fault) 92%, var(--black));
--color-fg-on-fault:          var(--gray-950);
```

- Hover **lighter**, pressed **darker** — opposite directions, as the accent does in the
  dark theme (spec 257 plan §2.2), so a press is not "more hover".
- **92 %, not the accent's 84 %.** At 84 % the dark label on the pressed fill is 3.99:1
  (fails 4.5). At 92 % it is 4.95:1 and the pressed fill is still 1.42:1 from hover —
  visibly distinct.
- **`--color-fg-on-fault` is dark in every theme**, because the fault red is the same in
  every theme (ADR-0148 pins the triad) and white on `#ff5252` is 3.19:1. It replaces
  `text-bg-base`, which only *happened* to be dark (it would be `gray-50` in light: about 3:1, like white's 3.19).
- **No theme redeclares any of the three.** Light's accent hover mixes toward black
  because the light accent is dark with a white label; fault's label is dark in every
  theme, so mixing toward black on hover in light would give 4.45:1 and fail.

### 2.2 Neutral states — a tint, not a surface step

```css
--color-bg-hover:   color-mix(in oklch, var(--color-fg-primary) 8%,  transparent);
--color-bg-pressed: color-mix(in oklch, var(--color-fg-primary) 14%, transparent);
```

`secondary` and `ghost` sit on `bg-base`, `bg-elevated` and `bg-raised` (dialogs). Any
opaque surface token as a hover fill inverts on one of them (today's `bg-bg-elevated`
hover goes *darker* inside a Dialog). A translucent tint of the foreground lightens every
dark ground and darkens every light one, and follows `data-theme` through
`--color-fg-primary` with no redeclaration.

### 2.3 Contrast, measured

Computed with a throwaway OKLCH → sRGB script (the same method as spec 257 plan §2.2);
opaque `color-mix` interpolates L and C in OKLCH; the tints are alpha-composited in sRGB,
as the browser does. **Thresholds**: label ≥ 4.5 (WCAG 1.4.3); focus indicator and
non-text boundary ≥ 3 (1.4.11); **disabled label ≥ 3 — this spec's choice**, since 1.4.3
exempts inactive components.

| Pair | Dark (base / elevated / raised) | Light (gray-50 / white) | High-contrast |
|---|---|---|---|
| `fg-on-fault` on fault / fault-hover / fault-pressed | 6.10 / 7.04 / 4.95 | same (not remapped) | same |
| fault fill vs surface (non-text ≥ 3) | 6.10 / 5.63 / 5.08 | 2.97 / 3.19 ⚠ | 6.58 (black) / 6.10 (gray-950) |
| `fg-primary` on `bg-hover` tint | 15.3 / 13.6 / 12.0 | 14.2 / 15.3 | 18.4 |
| `fg-primary` on `bg-pressed` tint | 12.7 / 11.2 / 9.90 | 12.5 / 13.4 | 15.6 |
| hover tint step vs surface | 1.19 / 1.23 / 1.26 | 1.18 / 1.18 | 1.14 |
| pressed tint step vs surface | 1.42 / 1.49 / 1.52 | 1.34 / 1.34 | 1.35 |
| `fg-disabled` on surface (≥ 3, chosen) | 3.85 / 3.55 / 3.21 | 3.26 / 3.50 | 8.47 (black) / 7.85 (gray-950) |
| `fg-disabled` on `accent-disabled` (rejected) | 1.74 | 1.80 | — |
| `focus-ring` vs surface (≥ 3) | 11.1 / 10.2 / 9.25 | 5.39 / 5.80 | 14.9 |
| `focus-ring` vs fault fill, if it touched | 1.82 | — | — |

- **The focus outline must be offset**, because against the danger fill it would be
  1.82:1. With `outline-offset: 2px` it is drawn over the surrounding surface, where it
  is ≥ 5.39:1 in every theme.
- ⚠ **The danger fill against the light ground is 2.97:1**, marginally under 1.4.11. It
  is not a regression (the light theme is unreachable — both `index.html` hard-code
  `data-theme="dark"` — and this spec does not remap the triad), and it belongs to spec
  257 §4 item 1's open question about remapping the triad per theme. The 1 px border
  the disabled state already uses would fix it if that question is answered "no".
- Tint steps of 1.2–1.5 are deliberate: hover is a hint, pressed is a commitment. Both
  are asserted *distinct* in the e2e, not asserted against a threshold.

## 3. `Button.tsx`: the matrix

Class strings are split so `busy` can drop the interactive half (US2):

```ts
const base =
  'inline-flex items-center justify-center rounded-md border border-transparent px-4 py-2 ' +
  'text-sm font-medium transition-colors ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring ' +
  'disabled:pointer-events-none disabled:border-border-subtle disabled:bg-transparent disabled:text-fg-disabled';

const rest: Record<ButtonVariant, string> = {
  primary:   'bg-accent text-fg-on-accent',
  secondary: 'border-border-strong text-fg-primary',
  ghost:     'text-fg-primary',
  danger:    'bg-accent-fault text-fg-on-fault',
};

const interactive: Record<ButtonVariant, string> = {
  primary:   'hover:bg-accent-hover active:bg-accent-pressed',
  secondary: 'hover:bg-bg-hover active:bg-bg-pressed',
  ghost:     'hover:bg-bg-hover active:bg-bg-pressed',
  danger:    'hover:bg-accent-fault-hover active:bg-accent-fault-pressed',
};

const unavailableTreatment =
  'aria-disabled:border-border-subtle aria-disabled:bg-transparent aria-disabled:text-fg-disabled';
```

| State | Mechanism | Why |
|---|---|---|
| Hover | `hover:` | Tailwind 4.3.3 emits it inside `@media (hover: hover)`, so a touch tap cannot latch it (spec §1). Nothing to add; pinned by the build test. |
| Pressed | `active:` | Emitted **after** `hover:` (measured: offsets 26551 → 28033 in the compiled sheet), so while both match, pressed wins. |
| Focus | `outline`, not `ring` | Forced-colors mode drops `box-shadow`, which is what `ring-*` is; an outline survives (spec 257 §3 handover). `outline-offset-2` keeps it off the fill (§2.3). `focus-visible:outline-none` is removed. |
| Disabled (native) | `disabled:` | Emitted after `active:` (28310), so it overrides every interactive fill. `pointer-events-none` kept. |
| Unavailable | `aria-disabled:`, only when `unavailable !== undefined` (as today) | Emitted last (29481), so it overrides hover and pressed without `pointer-events-none` — the control must stay clickable and focusable (ADR-0151). |
| Busy | `aria-busy="true"`, `cursor-progress`, `interactive[variant]` omitted | Rest fill held; nothing invites a second press. Default for open decision D1 (spec §4). |

- **Cascade order is Tailwind's, not ours, and the design depends on it**, so the build
  test pins it (§5.3): hover < active < disabled < aria-disabled.
- **The transparent border** is the only layout change: `disabled` and `unavailable` need
  a border on every variant to keep a filled button's footprint when the fill goes, and
  `secondary` already has one. Unifying makes all four variants the same height.
- **No opacity, no transform, no new transition.** `transition-colors` at
  `--duration-fast` / `--ease-out` (the spec-257 bridge) is unchanged. Press motion is
  #2334's.
- The existing `danger` comment ("Reuses the fault token…") stays; it is the recorded
  reason for spec §4's precedent.

### `busy` (US2)

```ts
/** In flight. Announces `aria-busy`; never disables — pass `disabled` or `unavailable` for that. */
busy?: boolean;
```

`busy` is destructured (never reaches the DOM), sets `aria-busy={busy || undefined}`, adds
`cursor-progress`, and drops `interactive[variant]`. It does **not** set `disabled` or
`aria-disabled`: whether an in-flight control may keep focus is ADR-0151's call per site,
and six sites currently get it wrong (§7) in a way this spec must not silently change.

## 4. The other three focus sites and one disabled site (US3)

| File | Today | After |
|---|---|---|
| `Input.tsx` | `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-active disabled:opacity-50` | `focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus-ring disabled:border-border-subtle disabled:text-fg-disabled` |
| `DataTable.tsx` (sort header) | `focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-active rounded` | the same three outline utilities + `rounded` |
| `GridDesigner.tsx` (preset chip `<label>`) | `has-[:focus-visible]:outline-none …:ring-2 …:ring-offset-2 …:ring-accent-active` | `has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-focus-ring` |
| `ChainRecoveryNotice.tsx:302` | `underline aria-disabled:opacity-50 aria-disabled:cursor-progress` | `underline aria-disabled:text-fg-disabled aria-disabled:cursor-progress` |

No shared "focus ring" constant is introduced: four sites, one of them under a
`has-[…]` variant a constant cannot express, and the architecture test (§5.1) enforces the
recipe more reliably than an import would.

## 5. Tests

### 5.1 `tests/Architecture.Tests/InteractionStateTests.cs` (NEW)

Scans the string-literal content of `apps/*/src/**/*.{ts,tsx}`, tests excluded — the same
reader `SharedUiTokenUsageTests` uses, **lifted** (below). Facts:

1. **No opacity behind a state variant.** `\b(?:hover|active|focus|focus-visible|focus-within|disabled|enabled|aria-disabled|aria-busy|group-hover|peer-disabled):opacity-\d+`.
   Red on develop: `Button.tsx` ×3, `Input.tsx`, `ChainRecoveryNotice.tsx`.
2. **No triad colour draws a focus indicator.** `\b(?:ring|outline|ring-offset)-accent-(?:active|fault|warning)\b` (any variant prefix).
   Red: `Input.tsx`, `DataTable.tsx`, `GridDesigner.tsx`.
3. **No box-shadow focus ring.** `(?:focus-visible|focus|:focus-visible\]):ring-\d`.
   Red: `Button.tsx`, `Input.tsx`, `DataTable.tsx`, `GridDesigner.tsx`.
4. **Every focus outline is the full recipe.** A literal that contains a
   `…focus-visible…:outline-` utility contains all of `outline-2`, `outline-offset-2` and
   `outline-focus-ring` under that same prefix. Red: `Button.tsx`, `Input.tsx`,
   `DataTable.tsx`, `GridDesigner.tsx` (each has `outline-none` only).
5. **The primitives use the accent, not the triad, for affordance.** No
   `bg-accent-(?:active|warning)` in `apps/shared/src/ui/primitives/**`. Red: `Button.tsx`.
6. **Fault-state contrast holds** (opaque pairs only). Resolve `--color-fg-on-fault`,
   `--color-accent-fault`, `-fault-hover`, `-fault-pressed` from `tokens.css` and assert
   ≥ 4.5; resolve `--color-fg-disabled` against `bg-base`/`-elevated`/`-raised` per theme
   and assert ≥ 3. Red: the fault roles do not exist. Needs `color-mix(in oklch, a N%, b)`
   for opaque operands (interpolate L and C; take the chromatic operand's hue).

**Counterfactuals** (phase 4a, quoted in the PR): after 4b is green, re-introduce
`hover:opacity-90` in a scratch copy of `Button.tsx` and see fact 1 fail naming it;
change `--color-accent-fault-pressed` to 84 % and see fact 6 fail with 3.99.

**Helper lift — behaviour-preserving, characterised.** `SharedUiTokenUsageTests.StringLiteralContent`
and `DesignTokenLayerTests`' OKLCH → sRGB resolver are private. Move each, unchanged, into
`internal static class TypeScriptSource` / the resolver's own internal class; both
existing test classes call them. **`SharedUiTokenUsageTests` and `DesignTokenLayerTests`
must pass unmodified in assertions before and after the move** — captured green first.
The `color-mix` extension (fact 6) is added *after* the lift, as new behaviour.

### 5.2 `Button.test.tsx`, `ConfirmDialog.test.tsx`, the two SaveGate tests (jsdom)

- Rewrite case 1: `unavailable` emits `aria-disabled:text-fg-disabled`, not
  `aria-disabled:opacity-50`. Rewrite case 4: no `disabled:opacity-50`;
  `disabled:text-fg-disabled` present. The ADR-0151 halves are kept verbatim.
- New: `busy` → `aria-busy="true"`; no `aria-busy` attribute without it; `busy` alone sets
  neither `disabled` nor `aria-disabled`; `busy` + `disabled` → native disabled; `busy` +
  `unavailable` → `aria-disabled="true"` and focusable; `busy` → no `hover:`/`active:`
  class present; `busy` never reaches the DOM as an attribute.
- `ConfirmDialog.test.tsx`: `pending` → the confirm button has `aria-busy="true"`.
- `LayoutEditorDialogSaveGate.test.tsx:227`, `OverlayEditorDialogSaveGate.test.tsx:247`:
  `toHaveClass('aria-disabled:text-fg-disabled', 'aria-disabled:cursor-progress')`.

These check props → attributes/classes. **Rendering is the e2e's to prove** (§5.4);
jsdom computes neither `:hover`, `:active` nor custom properties.

**Keeping each commit type-checking:** the red commit adds `busy?: boolean` to
`ButtonProps` with no implementation (it falls into `...rest`; React drops a boolean
unknown attribute), so the new cases are red on behaviour and `pnpm typecheck` stays
green (spec 163 left a typecheck error standing for its whole 4a; not repeated here).

### 5.3 `apps/{management-web,kiosk-web}/src/styles/tokens.build.test.ts` (+candidates)

Add to `CANDIDATES` and assert on the compiled sheet:

| Candidate | Assertion | Colour |
|---|---|---|
| `bg-bg-hover`, `bg-bg-pressed`, `bg-accent-fault-hover`, `bg-accent-fault-pressed`, `text-fg-on-fault` | each compiles to `var(--color-…)` of its role | **red** |
| `hover:bg-accent-hover` | its rule sits inside `@media (hover: hover)` | pin |
| `focus-visible:outline-focus-ring` | `outline-color: var(--color-focus-ring)` | pin |
| `hover:bg-accent-hover`, `active:bg-accent-pressed`, `disabled:bg-transparent`, `aria-disabled:bg-transparent` | appear in that order in the output | pin |

### 5.4 `e2e/interaction-states.spec.ts` (NEW, `chromium` project, seeded console sign-in)

Mirror `camera-detail.spec.ts` for sign-in and registering a camera. Colours are compared
by painting a probe element with the token (`style.background = 'var(--color-…)'`) and
reading its computed value — never a hard-coded `rgb()`, so a token change moves both
sides (the "assertion must not check its own input" rule applies the other way round:
the subject is the Button's computed style, the reference is the token).

1. Primary *Register camera*, secondary *Cancel* (Register dialog), ghost *Cancel*
   (Correct-the-address dialog), danger *Retire camera*: rest, `hover()`, and between
   `mouse.down()`/`mouse.up()` — three distinct `backgroundColor`s; `color` constant;
   `opacity` `1` throughout. Primary rest equals the `--color-accent` probe and differs
   from the `--color-accent-active` probe. Danger: hover luminance > rest > pressed.
2. Keyboard `Tab` to each: `outlineStyle` `solid`, `outlineWidth` `2px`, `outlineOffset`
   `2px`, `outlineColor` equals the `--color-focus-ring` probe, `boxShadow` draws no ring.
   Same for an `Input`, a sort header and a GridDesigner chip (US3).
3. `emulateMedia({ forcedColors: 'active' })`: focused Button's `outlineStyle` still `solid`.
4. New context `{ hasTouch: true, isMobile: true }` (primary pointer cannot hover): tap
   *Register camera* on the list page, release — fill equals rest. **Pin.**
5. Disabled: set `disabled` on a live primary Button via `evaluate`; fill transparent,
   `color` equals the `--color-fg-disabled` probe, `hover()` changes nothing. Repeat with
   `aria-disabled="true"` on a Button rendered with `unavailable` (the Layout editor's
   Save while blocked).

Waits by condition, never by timeout (ADR-0150).

## 6. `busy` adoption (US2) — additions only

`busy={isLoading}` next to the existing prop at: `EditCameraAddressDialog.tsx:95`,
`RegisterCameraDialog.tsx:147`, `RenameCameraDialog.tsx:108`, `DryRunPanel.tsx:61`,
`RuleDialog.tsx:229`, `SystemVariableDialog.tsx:182`, `LayoutEditorDialog.tsx:460`,
`OverlayEditorDialog.tsx:357`; and `busy={pending}` on `ConfirmDialog`'s confirm button.
The label swaps stay. **No `disabled` / `unavailable` is added, removed or swapped.**

## 7. Inventory for the follow-ups (not done here)

- **Raw `<button>` → `<Button>`, and the triad as affordance/selection** (32 raw buttons):
  kiosk `App.tsx:44,87`, `ReconnectingScreen.tsx:33`, `CellPage.tsx:162,180`,
  `PickerPage.tsx:63,89` (and `hover:border-accent-active` at :92); console `App.tsx:40,60,75`
  (sign-in, `bg-accent-active`), `ShellLayout.tsx:109` (`bg-accent-active`); nine
  `className="underline"` refetch links across Audit/Cameras/CameraDetail/Layouts/
  Overlays/Rules/SystemVariables pages (a `link` variant is a #2335-shaped question);
  selection chips at `LayoutsPage.tsx:108`, `OverlaysPage.tsx:101`,
  `SystemVariablesPage.tsx:94`, `GridDesigner.tsx:220` (`border/bg/text-accent-active` →
  `accent` / `accent-subtle`); `BackdropControls.tsx:178,188`, `OverlayEditor.tsx:681,690`
  (#2342 owns those files). The kiosk ones are the actual touch targets; they need
  pressed states and should be scheduled with #2337's render gate in view.
- **ADR-0151 focus loss on submit**: the six `disabled={isLoading}` sites in §6 and
  `ConfirmDialog`'s two `disabled={pending}` buttons. Each needs a guard and an Enter-key
  Playwright test per ADR-0151.

## 8. Expected visible change (the Phase-5 screenshot checklist)

- Every console `primary` Button: green `#00c853` → cyan `--color-accent` (`#6cc7d7`), label
  near-black on it.
- `primary`, `ghost`, `danger` Buttons 2 px taller (transparent border).
- `secondary` border: `fg-muted` → `border-strong` (slightly darker grey on dark).
- Hover on `secondary`/`ghost` lightens inside Dialogs instead of darkening.
- Focus: cyan 2 px outline offset 2 px on every Button, Input, sort header and preset chip,
  instead of a colourless or green ring.
- Disabled Buttons: no fill, subtle border, grey label — no longer a half-transparent green
  or red block.
- Nothing on the wall changes.

## 9. Commit sequence (each builds on its own, ADR-0087)

1. `refactor(tests): lift the string-literal reader and the OKLCH resolver` — green;
   `SharedUiTokenUsageTests` + `DesignTokenLayerTests` unmodified and green.
2. `test(interaction-states): pin every state red-first` — §5.1–§5.4 plus the `busy?`
   declaration stub; typecheck green, the new tests red.
3. `feat(tokens): derive hover, pressed and on-fault roles` — §2.
4. `feat(shared): design Button's state matrix` — §3 without `busy`.
5. `feat(shared): one focus outline on Input, DataTable and GridDesigner` — §4.
6. `feat(shared): add a busy state to Button and adopt it` — §3 `busy`, §6. **Held on D1.**

## 10. Verification (Phase 5)

Run spec §7. Read the spec-225 render-leg figure from the PR's CI run and cite it (the
wall loads `tokens.css`). Take the §8 screenshots on the dark theme; set
`data-theme='light'` and `'high-contrast'` on `<html>` once and confirm the danger label
and focus outline remain legible.

## 11. Risks

- **R1 — Tailwind variant order.** The matrix relies on hover < active < disabled <
  aria-disabled in the output. Measured on 4.3.3; pinned by §5.3 so a Tailwind upgrade
  that reorders fails loudly.
- **R2 — `color-mix` with `transparent`.** Already used by spec 257's `--color-scrim` and
  shadow tokens, so no new browser requirement. §2.3's tint figures assume the result is
  the foreground colour at alpha 0.08 / 0.14, composited in sRGB; the e2e asserts only
  that the three fills are *distinct*, so a small difference in how the browser resolves
  the mix cannot flip it.
- **R3 — `border-transparent` over a filled background** shows the fill under the border
  (`background-clip: border-box`), so there is no visible ring at rest. Checked in the §8
  screenshots.
- **R4 — the e2e's disabled case sets attributes by `evaluate`.** It proves the CSS, not
  that any call site disables correctly; the call sites' disablement is ADR-0151's
  existing tests' job.
