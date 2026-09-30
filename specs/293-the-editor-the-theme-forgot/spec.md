# Spec 293 — The editor the theme forgot

**Issue:** [#2342](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2342)
— "The overlay editor is 100% inline styles, so it is outside the design system entirely".
Gate #2332 (the token system, spec 256/257) confirmed **closed** before pickup.
**Branch:** `tech-debt/2342-overlay-editor-tokens` (cut from `origin/develop` @ `34d46a23`)
**Created:** 2026-09-30
**Lane:** autonomous (ADR-0144)

**ADRs this spec is bound by (and applies; it decides nothing they left open):**

- **ADR-0148** — *Two-layer tokens in OKLCH*. Components cite semantic roles only; a
  theme remaps roles, never primitives; roles are `--<category>-<role>-<variant>`; a
  component layer (`--button-primary-bg-hover`) is rejected.
- **ADR-0146** — *One discipline, two surfaces*. A value is picked from a scale or it is
  wrong; the design language reaches components through tokens.
- **ADR-0078** — tokens as CSS custom properties consumed by Tailwind via `tailwindTheme.ts`.
- **ADR-0077** — Radix headless + custom design system; the primitives in
  `apps/shared/src/ui/primitives/` are the design system's controls.
- **ADR-0151** — `aria-disabled` via `Button`'s `unavailable` for a control that can
  become unavailable while focused (Undo/Redo are its named reference implementation).
- **ADR-0036** — no speculative generality (drives the slider decision, §4).
- **ADR-0123** / constitution **§IV** — the ≤ 50 ms composite + render leg (US2 only).
- **ADR-0144** — the lane; phase 4a colours are declared per task in `tasks.md`.

**No new ADR is needed.** §5 records the one interpretation this spec makes (content
roles pinned across themes), with the precedent it rests on.

---

## 1. The issue's premise, re-checked on this tree (`34d46a23`)

| Issue says | Tree says | Consequence |
|---|---|---|
| "`OverlayEditor.tsx` uses no Tailwind classes and no design tokens." | True for `OverlayEditor.tsx` (only `sr-only` classes). **But #2342 owns four files, not one**: `SharedUiTokenUsageTests.CarveOuts` (`tests/Architecture.Tests/SharedUiTokenUsageTests.cs`) exempts exactly `OverlayEditor.tsx`, `BackdropControls.tsx`, `OverlayGeometryFields.tsx` and `overlayLabelStyle.ts`, each tagged `#2342`, and `Each_carve_out_still_exists_and_still_violates` makes the list shrink-only. | Scope is the four files. "Done" is mechanically checkable: **the carve-out list is empty** (§7). |
| Quotes `background: 'rgba(255, 255, 255, 0.92)'`, `color: '#111827'`, `border: '1px solid rgba(17, 24, 39, 0.4)'`, `borderRadius: 8, padding: '0 8px'` as the label's style. | Stale. Spec 146 (#2339, closed) moved the label's surface into `overlayLabelSurfaceStyle` (`overlayLabelStyle.ts:31-44`), shared with the wall: `rgba(255, 255, 255, 0.85)`, `#111827`, `padding: '0 4px'`, `fontWeight: 600`, no border. | The label is **wall paint**, not editor chrome. It is US2, on the render leg, and must stay identical on both surfaces (`OverlayLabelParity.test.tsx`). |
| "uses the `Input` primitive and whatever slider primitive #2335 produces" | #2335 (closed) was *"Four Radix packages are installed and imported by nothing"* — Select/Tabs/Popover/DropdownMenu. It never mentions a slider. No `Slider` exists in `apps/shared/src/ui/primitives/`, and `@radix-ui/react-slider` is not a dependency (`apps/shared/package.json:70-77`). | Stale cross-reference. Resolved in §4. |
| Comment (notonlywhite): `cameraLabel`/`ambiguousNamesOf` "already exist and are importable" into `BackdropControls.tsx`. | They are **module-private** functions in `apps/management-web/src/features/layouts/GridDesigner.tsx:95-108` — not exported, and in an app `apps/shared` may not import from. | Not importable. Resolved in §4. |
| Checkerboard "should just be drawn from tokens." | Pinned byte-for-byte by `OverlayEditorCharacterisation.test.tsx:70-71,97-101` and `OverlayEditorBackdrop.test.tsx:239-240`. | Converting it is a **declared behaviour change** (it must now follow the theme), not a refactor. Those assertions are superseded by name (§6). |

## 2. User stories

### US1 (P1) — The editor's chrome follows the theme

As an **operator authoring an overlay in the console**, I want the editor's canvas
backdrop, controls, messages and spacing drawn from the design tokens, so that it reads as
part of the product in every theme — including high-contrast, which I may have turned on
because I need it.

Files: `OverlayEditor.tsx`, `BackdropControls.tsx`, `OverlayGeometryFields.tsx` (all
`apps/shared/src/ui/composites/`), plus `tokens.css` (one new role).

**Acceptance scenarios**

```gherkin
Scenario: the checkerboard follows the theme (happy)
  Given the overlay editor is open with the Checkerboard backdrop
  When the document's data-theme changes from "dark" to "light"
  Then the canvas's computed background-image changes
  And in each theme it is a 45deg repeating gradient of var(--color-border-subtle) and var(--color-bg-elevated)

Scenario: the test fields stay the fields they claim to be, in every theme (conflict: theme vs content)
  Given the overlay editor is open
  When the operator picks "White field" under data-theme "dark", "light" or "high-contrast"
  Then the canvas's computed background-color is rgb(255, 255, 255) in all three
  And "Black field" computes rgb(0, 0, 0) in all three
  And a captured frame is letterboxed over the same colour as the wall's bg-bg-video

Scenario: controls are the design system's controls
  Given the overlay editor is open
  Then "Label text" and the four geometry fields are the Input primitive
  And Undo, Redo, "Capture frame" and "Cancel capture" are the Button primitive (secondary)
  And Undo at the floor still announces aria-disabled="true", stays focusable, and still says "Nothing to undo"

Scenario: an invalid geometry value still refuses with a message (bad request)
  Given the Width field
  When the operator types "0" and blurs
  Then the Width alert reads "Width must be greater than 0% and at most 100%."
  And it is coloured var(--color-accent-fault)
  And the Save button does not move (the reserved-slot mechanism is unchanged)

Scenario: authorisation is unchanged (auth)
  Given an operator without a token source (no getToken)
  Then the camera picker and "Capture frame" are still not offered
  And no Redux store is required to render the editor
```

### US2 (P2) — The label's colours come from the token file, identically on both surfaces

As **the maintainer of the design system**, I want the overlay label's surface and type
values to be named roles, so the last inline colour literal in `apps/shared/src/ui`
disappears — while the label stays pixel-identical between editor and wall and does **not**
follow the console theme (it is paint over video, not chrome).

Files: `overlayLabelStyle.ts`, `tokens.css` (two new roles).

```gherkin
Scenario: the label cites roles (happy)
  Given a label rendered by the wall or the editor
  Then its background is var(--color-bg-label)
  And its colour is var(--color-fg-on-label)
  And its padding is 0 var(--space-1) and its weight var(--font-weight-semibold)

Scenario: the label does not theme (conflict: chrome theme vs wall paint)
  Given the same label
  When data-theme changes between dark, light and high-contrast
  Then the label's computed background-color and color are identical in all three

Scenario: editor and wall agree (parity)
  Then OverlayLabelParity.test.tsx's editor == wall assertions pass unmodified

Scenario: the authored font size is data, not a design value (bad request guard)
  Given fontSizePx = 48
  Then fontSize is still clamp(12px, 3vw, 48px) — computed from authored data, stays inline
```

(No auth scenario: US2 changes a pure style function with no I/O.)

## 3. What stays inline, and why (exhaustive)

The issue's rule: inline only where geometry genuinely demands it. Everything **not** in
this list becomes a Tailwind class citing a token.

| Kept inline | File | Why |
|---|---|---|
| Canvas `width` / `height` | `OverlayEditor.tsx:606-607` | Computed pixel values from `canvasWidthPx`/`canvasHeightPx` props. |
| `canvasBackgroundStyle(...)` output (colours now `var(...)`) | `OverlayEditor.tsx:217-235` | State-dependent, and the captured frame is a runtime `data:` URL — cannot be a class. |
| `<Rnd size position>` | `OverlayEditor.tsx:614-615` | Props, not styles; computed pixels. **Untouched.** |
| `<Rnd style>`: `overlayLabelSurfaceStyle(value)`, `cursor`, `userSelect`, `FOCUS_RING_STYLE` (colours now `var(...)`) | `OverlayEditor.tsx:634-639` | The surface is the function shared with the wall (spec 146) and its `fontSize` is computed from authored data; the ring is state-driven (FR-003, onFocus) and `OverlayEditorKeyboard.test.tsx:681-709` observes it via `element.style`. `cursor`/`userSelect` carry no design value and sit in the same object. |
| Checkerboard gradient geometry (`45deg`, `12px`, `24px`) and the focus ring's `2px` / `-2` / `4px` | `OverlayEditor.tsx:201-209` | Pattern and ring geometry, not layout rhythm; there is no token category for either. Only their **colours** change. |
| Label `fontSize: clamp(...)` | `overlayLabelStyle.ts:40` | Computed from authored `fontSizePx`. |

The `Rnd` position/size needs **no new characterisation test**: it is already pinned by
`OverlayEditorCharacterisation.test.tsx:103-120` ("Fixes the canvas at 800 by 450 pixels
by default", "Maps the label's authored normalized geometry onto the draggable in pixel
space") and by the drag/resize/keyboard suites, all of which must pass **unmodified**.

## 4. Scoping decisions

### 4.1 Slider — style the native `<input type="range">`; no `Slider` primitive

**Decision: option (b).** The font-size control stays a native range input, themed with
`w-full accent-accent` plus the Input primitive's focus-visible outline classes. A reusable
`Slider` primitive is deferred to a follow-up issue with an explicit trigger.

Reasoning:

1. **One consumer in the whole repo.** `grep 'type="range"' apps/**/*.tsx` → exactly
   `OverlayEditor.tsx:725`. A primitive with one caller is the speculative generality
   ADR-0036 names; the abstraction's shape would be guessed from a sample of one.
2. **A real `Slider` means a new dependency.** ADR-0077 builds primitives on Radix;
   `@radix-ui/react-slider` is not installed. A token-conversion issue should not grow a
   dependency, a primitive, its tests and its a11y review.
3. **It still meets the issue's goal.** `accent-color: var(--color-accent)` themes the
   thumb and the filled track in Chromium, Firefox and WebKit, and follows `data-theme`
   through the cascade. The native control keeps its native keyboard/AT semantics, which
   a custom slider would have to re-earn.
4. **Known limitation, accepted in writing:** the *unfilled* track stays UA-drawn neutral.
   It is legible in all three themes; pixel-level control of it is exactly what the
   follow-up would buy.

Follow-up: **#2685** *"A `Slider` primitive, when a second range control appears"* — filed at
phase 3, not `agent:ready`.

### 4.2 `BackdropControls.tsx` camera-name disambiguation — out of scope, filed separately

`BackdropControls.tsx` **is** in scope for the token conversion (it is one of #2342's four
carve-outs and a child of `OverlayEditor`). The **camera-label fix is not**:

1. **Not importable as claimed.** `cameraLabel`/`ambiguousNamesOf` are private to
   `apps/management-web/.../GridDesigner.tsx`; `apps/shared` cannot import from an app.
   The fix means moving both into `apps/shared`, re-pointing `GridDesigner.tsx`, and
   changing `BackdropControls`' option text — three files, two apps.
2. **It is a behaviour change of a different kind** (what an operator reads in a picker),
   with its own red test, landing in a PR whose other red tests are about colour. CLAUDE.md:
   "a refactor that is also a bug fix is two issues"; the same logic applies to two
   unrelated behaviour changes sharing one review.
3. **Low harm, separately deliverable.** Two same-named cameras in different fabs in a
   *preview-only* picker (spec 147: nothing authored depends on it).

Follow-up: **#2686** *"The overlay editor's camera picker cannot tell two same-named cameras apart"*
— filed at phase 3, citing the comment on #2342.

### 4.3 The native `<select>` in `BackdropControls` stays native

The `Select` primitive (Radix) changes the interaction model (portal, typeahead, no
`change` event), which `FrameCapture.test.tsx` drives. It is styled with the Input
primitive's class string instead. Swapping it is a behaviour change and not this issue.

### 4.4 `Capture frame` keeps native `disabled`

It becomes `<Button variant="secondary" disabled=…>` with the attribute unchanged. Whether
it should move to `unavailable` (ADR-0151) is a focus-behaviour question spec 234 already
handles with its own mechanism; not reopened here.

## 5. The one interpretation: content roles are pinned across themes

ADR-0148: "A theme redefines the semantic layer only." It does not say every role must be
redefined, and two already are not: `--color-bg-video` (black in all three themes — no
theme block redeclares it) and `--color-fg-on-fault` ("Dark in every theme").

This spec adds three roles that follow the same rule, because they name **what the wall
paints or what a test field is**, not console chrome:

| New role | Value | Declared in | Cited by |
|---|---|---|---|
| `--color-bg-video-inverse` | `var(--white)` | `:root` only | White field; focus ring inner ring (US1) |
| `--color-bg-label` | `color-mix(in oklch, var(--white) 85%, transparent)` | `:root` only | `overlayLabelSurfaceStyle` (US2) |
| `--color-fg-on-label` | `var(--gray-900)` | `:root` only | `overlayLabelSurfaceStyle` (US2) |

These are roles, not ADR-0148's rejected component layer: `--color-bg-label` names a
domain surface (the overlay label, rendered by two components through one function), in
the same way the triad names domain states. None is added to `tailwindTheme.ts` — no class
cites them (ADR-0036).

A new architecture fact makes "pinned" a checked claim rather than a comment (plan §4).

**If a reviewer judges §5 to be a new decision rather than an application of ADR-0148,
the lane must block (it may not write an ADR), and US2 can ship later on its own.**

## 6. Declared visible changes (everything else must be pixel- and behaviour-identical)

| Change | Where | Observable in |
|---|---|---|
| Checkerboard: Tailwind gray-800/900 → `--color-border-subtle` / `--color-bg-elevated`; now themes | `OverlayEditor.tsx:209` | Supersedes `OverlayEditorCharacterisation.test.tsx:63-71,92-101` and `OverlayEditorBackdrop.test.tsx:239-240` |
| White/black/letterbox literals → `var(...)` (rendered colour identical) | `OverlayEditor.tsx:218-227` | Supersedes `OverlayEditorBackdrop.test.tsx:217,228,273`. **Amended, see below**: `e2e/overlays.spec.ts:62-74`'s hardcoded `rgb(255, 255, 255)` also needed to change |
| **Amendment, found during Phase 6 review, accepted in writing by the orchestrator (2026-09-30):** two `e2e/overlays.spec.ts` characterisation assertions could not stay unmodified as originally declared above and in §7 item 4 — both are genuine consequences of the token conversion, not scope creep, confirmed live: (1) Chromium's `getComputedStyle` serialises a colour in the notation of its *declared* value — a literal hex/rgb normalises to `rgb(...)`, but `--color-bg-video-inverse: var(--white)` resolves through `--white: oklch(100% 0 0)` and stays `oklch(...)`; the rendered pixel is unchanged. Observed failure: `Expected: "rgb(255, 255, 255)"`, `Received: "oklch(1 0 0)"`. (2) Converting `OverlayGeometryFields.tsx` from raw-px inline styles to rem-based Tailwind utilities introduces a genuine, reproducible sub-pixel Chromium layout-rounding difference once the Width field's refusal message renders. Observed failure: `Expected: 1068.712646484375`, `Received: 1068.9849853515625` (Δ ≈ 0.27px). Both fixes and their required follow-up corrections are tracked in this issue's PR; §7 item 4 is superseded by this row for the White-field test. | `e2e/overlays.spec.ts:62-74,~761` | See PR body for the exact diff and live re-confirmation |
| Capture failure alert 13 px → 14 px (`text-sm`), `#b91c1c` → `--color-accent-fault` | `BackdropControls.tsx:38` | No test pins it |
| Notices `#6b7280` → `--color-fg-muted`; field alert `#dc2626` → `--color-accent-fault`; advisory `#b45309` → `--color-accent-warning` | `BackdropControls.tsx:37`, `OverlayGeometryFields.tsx:81-82` | No test pins them |
| Legends / field captions gain `font-medium text-fg-primary` (the `FormField`/`GridDesigner` legend pattern) | all three | No test pins them |
| Undo/Redo/Capture/Cancel become `Button` secondary (border, hover/pressed fills, focus ring) | `OverlayEditor.tsx:681-698`, `BackdropControls.tsx:178-197` | Behaviour tests must pass unmodified |
| **US2:** label text `#111827` → `--gray-900` (`#14171c`-pinned); bg/padding/weight become `var(...)` with identical resolved values | `overlayLabelStyle.ts:38-42` | Supersedes `OverlayLabelCharacterisation.test.tsx:73,81,82,96` and `OverlayLabelParity.test.tsx:80`; parity assertions (editor == wall) pass **unmodified** |

**Pre-existing, not introduced here, recorded so it is not mistaken for this spec's
regression:** the triad is pinned across themes (ADR-0148), so `--color-accent-warning` /
`--color-accent-fault` as small text on the light theme's white fall below 4.5:1. The same
is already true of `FormField`'s error and every kiosk warning badge. Both apps ship
`data-theme="dark"` today.

## 7. Independent end-to-end test procedure

1. `SharedUiTokenUsageTests` with `CarveOuts` **empty** → all four facts green
   (`Each_carve_out_still_exists_and_still_violates` becomes vacuous; see plan §4.1).
2. `DesignTokenLayerTests.Content_roles_are_pinned_across_themes` green.
3. `pnpm --filter @smart-sentinel-eye/shared test` and `pnpm --filter management-web test`
   → green, with **only** the assertions listed in §6 changed (diff of `*.test.*` files
   quoted in the PR).
4. `e2e/overlays.spec.ts` against the Aspire stack: the existing White-field test passes
   unmodified; the new theme test shows the checkerboard's computed `background-image`
   differs between `dark` and `light`, and the label's computed `background-color`/`color`
   are identical across all three themes.
5. Phase 5, in a real browser: open the editor under each theme (set
   `document.documentElement.dataset.theme`), screenshot canvas + controls + a refused
   Width field; quote in the PR.
6. US2 only: cite the composite + render leg (below) with the figure from the PR's CI
   run beside develop's latest.

## 8. Latency-budget impact

- **US1: N/A.** The overlay editor is a console authoring surface; it is not on the
  event → overlay path.
- **US2: Overlay composite + render (≤ 50 ms, constitution §IV; recorded over budget,
  ADR-0123).** `overlayLabelSurfaceStyle` stays pure and allocation-identical: the same
  object literal, the same eight properties, four constant strings changed from literals to
  `var(...)`. Per-label style resolution of three custom properties is below measurement
  resolution. Phase 5 still cites the leg's figure from the PR's CI run
  (`e2e/support/render-leg.ts` → `render-leg-summary.mjs`) beside develop's latest green run
  — a citation, not a claim of improvement.

## 9. Locked tech choices applied

React + TypeScript (ADR-0074), Tailwind classes from `tailwindTheme.ts` citing
`tokens.css` (ADR-0078/0148), `Input`/`Button` primitives (ADR-0077), `Button.unavailable`
(ADR-0151), Vitest + Testing Library (jsdom 30.1.1), xUnit + Shouldly architecture tests
(ADR-0052), Playwright e2e against the Aspire stack (ADR-0103).

## 10. Out of scope

- A `Slider` primitive (§4.1, follow-up filed).
- Camera-name disambiguation in the capture picker (§4.2, follow-up filed).
- Swapping the native `<select>` for the Radix `Select` (§4.3).
- `Capture frame` → `unavailable` (§4.4).
- Light-theme contrast of the pinned triad (§6, pre-existing).
- The label's `vw`-derived font clamp (spec 146 already filed it).
