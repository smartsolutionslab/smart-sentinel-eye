# Plan 292: The motion that has a reason

**Spec:** [spec.md](spec.md) · **Issue:** #2334 · **Base:** `origin/develop` @ `ca5d1c6c`

## Constitution / ADR check

| Rule | How this plan meets it |
|---|---|
| ADR-0146 motion stance | Every duration in 120–200 ms; every added keyframe animates `opacity`/`transform` only; wall gets nothing (spec §2, §4). |
| ADR-0148 mechanism + naming | Role tokens in `tokens.css`, `--duration-<role>` / `--ease-<role>`, cite the existing scale; keys in `tailwindTheme.ts`; prohibition by absence (`motion.css` not imported by the kiosk; stock `animate-*` removed). |
| ADR-0078 | Custom properties consumed by Tailwind through `@config`; no `@theme` (ADR-0148 rejected it). |
| §IV / ADR-0123 | No new animated property on the wall render path; `render-leg-gate` unchanged and must pass (spec §5). |
| ADR-0036 | Every token has a consumer in this spec: `state` → Button; `enter`/`exit` → Dialog, ConfirmDialog; `route` → NavItem. |
| ADR-0144 | No ADR written, no gate weakened, no existing assertion edited (see §6 on `tokens.build.test.ts`: additions only). |
| ADR-0150 | e2e waits are conditions (`expect.poll`, `waitForFunction`), not sleeps. |

## Bounded context and layers

Frontend only. No bounded context, no Domain/Application/Infrastructure/Api code, no
`Shared.Contracts`, no AppHost resource, no migration, no message. The one C# file is an
architecture **test** (`tests/Architecture.Tests`), mirroring `SharedUiTokenUsageTests` /
`InteractionStateTests` and reusing `TypeScriptSource.StringLiteralContent` and
`RepositorySource`.

"Entities/value objects/invariants" in the frontend sense — the invariants this plan encodes:

- **I1** Every motion duration a call site can reach is one of four roles, each resolving in 120–200 ms.
- **I2** Nothing in the kiosk's reachable source animates, except the named signal pulse.
- **I3** Every keyframe animates compositor properties only, except the named signal pulse.
- **I4** A transition on a paint property exists only where an allowlist entry says why.
- **I5** Under `prefers-reduced-motion: reduce`, motion is re-expressed (opacity, same duration), never deleted — except the named signal pulse, whose static ring remains.

## 1. Files

| File | Change | Story |
|---|---|---|
| `apps/shared/src/ui/tokens/tokens.css` | +8 role tokens in the motion block; the comment at `:210` updated ("the easing language is #2334" → names the roles). Bridges **unchanged**. | US1 |
| `apps/shared/src/ui/tokens/tailwindTheme.ts` | +4 keys each in `transitionDuration`, `transitionTimingFunction` (scale keys kept); new `animation` namespace (4 keys, **replaces** stock). | US1, US2 |
| `apps/shared/src/ui/primitives/Button.tsx` | `transition-colors` → `transition-colors duration-state ease-state`. | US1 |
| `apps/kiosk-web/src/features/picker/PickerPage.tsx` | Remove `transition` from both card buttons (lines 101, 119). | US1 |
| `apps/shared/src/ui/motion/motion.css` | **New.** Four keyframes + reduced-motion redefinitions; view-transition pseudo-element timing. | US2, US3 |
| `apps/shared/package.json` | `"./ui/motion/*": "./src/ui/motion/*"` export. | US2 |
| `apps/management-web/src/styles/index.css` | `@import '@smart-sentinel-eye/shared/ui/motion/motion.css';` as the **second** line (after tokens.css, before `tailwindcss`). | US2 |
| `apps/shared/src/ui/primitives/Dialog.tsx`, `ConfirmDialog.tsx` | Overlay: `data-[state=open]:animate-scrim-enter data-[state=closed]:animate-scrim-exit`; Content: `data-[state=open]:animate-surface-enter data-[state=closed]:animate-surface-exit`. | US2 |
| `apps/management-web/src/app/ShellLayout.tsx` | `NavItem` passes `viewTransition` to `NavLink`. | US3 |
| `tests/Architecture.Tests/MotionLanguageTests.cs` | **New.** Facts 1–7, §4. | US1 (+US2 facts 4/5 bite on motion.css) |
| `apps/{management-web,kiosk-web}/src/styles/tokens.build.test.ts` | **Additions only**: candidates + assertions, §6. | US1, US2 |
| `e2e/motion.spec.ts` | **New.** §7. | US2, US3 |

**Not touched:** `apps/kiosk-web/src/styles/index.css` (the pulse — byte-identical),
`DesignTokenLayerTests.cs`, `SharedUiTokenUsageTests.cs`, `InteractionStateTests.cs`, any
backend, any CI workflow.

## 2. Tokens (`tokens.css`)

Appended inside the existing `/* Motion */` block, after `--ease-in-out`:

```css
  /* Roles (#2334, spec 292). Call sites cite a role, never a scale step. */
  --duration-state: var(--duration-fast);     /* 120ms — a control changes state in place */
  --duration-enter: var(--duration-slow);     /* 200ms — a surface arrives; its origin is the information */
  --duration-exit: var(--duration-moderate);  /* 160ms — a surface leaves; the operator already chose to dismiss it */
  --duration-route: var(--duration-slow);     /* 200ms — one view replaces another */
  --ease-state: var(--ease-out);
  --ease-enter: var(--ease-out);              /* decelerate into place */
  --ease-exit: var(--ease-in);                /* accelerate away */
  --ease-route: var(--ease-in-out);           /* both views on screen at once */
```

Resolved: state 120 ms `cubic-bezier(0.2,0,0,1)`; enter 200 ms `cubic-bezier(0.2,0,0,1)`;
exit 160 ms `cubic-bezier(0.4,0,1,1)`; route 200 ms `cubic-bezier(0.4,0,0.2,1)`.

**No reduced-motion override in `tokens.css`.** Zeroing durations is the deletion the issue
forbids; reduced motion is expressed in the keyframes (§3), not in the timing.

**The two bridges stay citing `--duration-fast` / `--ease-out`.** They already equal
`--duration-state` / `--ease-state`; re-pointing them would edit an existing assertion
(`tokens.build.test.ts:262-265`, `"--default-transition-duration: var(--duration-fast)"`),
which the lane treats as evidence that behaviour moved. Fact 7 (§4) instead asserts the
bridge and the state role resolve to the same scale step, so they cannot drift apart.

Classification: `--duration-*` / `--ease-*` are existing categories, so
`DesignTokenLayerTests` facts 2, 3 and `Every_token_the_theme_cites_is_declared_and_semantic`
accept them with no edit. Verified by reading `PrimitiveName` (category prefixes excluded) and
`IsCategoryToken`.

## 3. `tailwindTheme.ts` and `motion.css`

```ts
transitionDuration: {
  fast: 'var(--duration-fast)', moderate: 'var(--duration-moderate)', slow: 'var(--duration-slow)', // kept: pinned by tokens.build.test.ts; call sites may not cite them (fact 3)
  state: 'var(--duration-state)', enter: 'var(--duration-enter)', exit: 'var(--duration-exit)', route: 'var(--duration-route)',
},
transitionTimingFunction: {
  out: 'var(--ease-out)', in: 'var(--ease-in)', 'in-out': 'var(--ease-in-out)', // kept, same reason
  state: 'var(--ease-state)', enter: 'var(--ease-enter)', exit: 'var(--ease-exit)', route: 'var(--ease-route)',
},
// Not under `extend`: REPLACES stock spin/ping/pulse/bounce (finding F2) — an ambient loop is
// not something either surface should be able to reach for by name.
animation: {
  'surface-enter': 'sse-surface-enter var(--duration-enter) var(--ease-enter) both',
  'surface-exit': 'sse-surface-exit var(--duration-exit) var(--ease-exit) both',
  'scrim-enter': 'sse-scrim-enter var(--duration-enter) var(--ease-enter) both',
  'scrim-exit': 'sse-scrim-exit var(--duration-exit) var(--ease-exit) both',
},
```

No `DEFAULT` key anywhere (finding F5, `tailwindTheme.ts:10-15`).

`apps/shared/src/ui/motion/motion.css` (unlayered, like `tokens.css`; cites role tokens only):

```css
@keyframes sse-surface-enter { from { opacity: 0; transform: translateY(var(--space-2)) scale(0.98); } }
@keyframes sse-surface-exit  { to   { opacity: 0; transform: translateY(var(--space-2)) scale(0.98); } }
@keyframes sse-scrim-enter   { from { opacity: 0; } }
@keyframes sse-scrim-exit    { to   { opacity: 0; } }

/* Reduced motion: the same relationship (arrives / leaves), told by opacity alone,
   over the same duration and curve. Not `none`, not 0 ms. */
@media (prefers-reduced-motion: reduce) {
  @keyframes sse-surface-enter { from { opacity: 0; } }
  @keyframes sse-surface-exit  { to   { opacity: 0; } }
}

::view-transition-old(root),
::view-transition-new(root) {
  animation-duration: var(--duration-route);
  animation-timing-function: var(--ease-route);
}
```

Notes that are load-bearing:

- **Animate `transform`, never `translate`/`scale` as individual properties.** Dialog content
  is centred with Tailwind 4's `-translate-x-1/2 -translate-y-1/2`, which sets the
  **individual `translate` property**. `transform` composes after it, so the keyframe adds a
  small rise without fighting the centring. A keyframe on `translate` would override the
  centring for the animation's duration.
- **The scrim is opacity only**: it is `fixed inset-0`; a travel would expose an edge.
- **`scale(0.98)` is a literal**: there is no scale token and ADR-0148 has no category for one;
  a single literal in the one keyframe file is not a scale anyone picks from.
- **Why redefinition inside `@media` rather than a zeroed travel token**: a zeroed token would
  need either a new category (`DesignTokenLayerTests` edit) or a `@media` block inside
  `tokens.css` (its parser reads only selector blocks). A same-name `@keyframes` inside a
  matching `@media` wins by document order — standard CSS, supported in Chromium. **Risk R1**
  (§9) covers a minifier deduplicating it.
- **View transitions**: the UA default for `root` is already an opacity cross-fade; only its
  timing is set. It is opacity, so reduced motion keeps it — no `@media` override.

## 4. `tests/Architecture.Tests/MotionLanguageTests.cs`

Shape: `SharedUiTokenUsageTests` (source scan, comment-stripped, string-literal content via
`TypeScriptSource`, failure messages that say what to do instead, shrink-only allowlists with
an honesty fact). Files: non-test (`*.test.*`, `*.spec.*` excluded), paths normalised to `/`
(memory: source-scanning tests need slash normalising).

**The kiosk reach set** (facts 1): every non-test `.ts/.tsx/.css` under `apps/kiosk-web/src`,
plus the transitive closure of `apps/shared/src/**` modules reached from it through import
specifiers — relative (`./`, `../`) and `@smart-sentinel-eye/shared/<sub>` (resolved through
`apps/shared/package.json` `exports`, trying `.ts`, `.tsx`, `/index.ts`). Today that reaches
`CameraViewer`, `ErrorBoundary` and their own imports (`overlayLabelStyle`, `useWhepSession`, …).
This is the "one place ambient motion could sneak in" ADR-0146's rejected-options paragraph
describes: a shared component carrying motion onto the wall.

| # | Fact | Scope | Develop |
|---|---|---|---|
| 1 | `The_wall_has_no_motion_but_its_signal`: no match for — Tailwind `\b(?:transition(?:-[a-z]+|-\[[^\]]*\])?|animate-[\w-]+|duration-[\w\[\]-]+|ease-[\w\[\]-]+|delay-[\w\[\]-]+)\b` or `motion-(?:safe|reduce):` in string-literal content; a string-valued `transition`/`animation*` key (`\b(?:transition|animation)[A-Za-z]*\s*:\s*['"\x60]`) in raw source; `\.animate\(`; in CSS: `@keyframes`, `animation`/`transition` declarations. **And** the kiosk `index.css` does not import `motion.css`. Allowlist: `apps/kiosk-web/src/styles/index.css` → `ssE-overlay-highlight-pulse` (keyframe name, the `animation:` declaration naming it, and its reduced-motion `animation: none`), reason: "ADR-0146: motion *as* the alert — the one wall animation that is itself a signal". | kiosk reach set | **red** — `PickerPage.tsx` ×2 (`transition`) |
| 2 | `A_paint_transition_is_deliberate`: string-literal content never contains bare `transition`, `transition-all`, `transition-shadow`, or `transition-[…]`; `transition-colors` only in allowlisted files. Allowlist: `apps/shared/src/ui/primitives/Button.tsx`, reason: "a colour cross-fade on one control on interaction (spec 292 §4); never rendered on the wall (fact 1)". | `apps/*/src` | **red** — `PickerPage.tsx` ×2 |
| 3 | `Every_transition_names_its_role`: a string literal containing a `transition(-*)?` utility also contains `duration-(state|enter|exit|route)`; no string literal anywhere contains `duration-(fast|moderate|slow|\d+|\[)` or `ease-(out|in|in-out|linear|\[)` (the scale and Tailwind's stock/arbitrary steps). | `apps/*/src` | **red** — `Button.tsx` |
| 4 | `Every_keyframe_composites`: every `@keyframes` frame declares only `opacity`, `transform`, `translate`, `scale`, `rotate`. Allowlist: the pulse (`box-shadow`, reason as fact 1). | `apps/*/src/**/*.css` | green (declared; counterfactual) |
| 5 | `Reduced_motion_redesigns_rather_than_deletes`: inside any `@media (prefers-reduced-motion: reduce)` block — no `animation: none`, `animation-name: none`, `transition: none`, zero `*-duration`, or `--duration-*` redeclaration; no `motion-reduce:(animate|transition)-none` utility in TS; **and** every `@keyframes` whose frames declare `transform` has a same-name redefinition inside a reduce block whose frames declare `opacity` only. Allowlist: the pulse's `animation: none` (reason: "a looping signal; the static ring stays and still marks the tile"). | `apps/*/src` | green (declared; counterfactual) |
| 6 | `Each_allowlist_entry_still_applies`: every allowlist entry's file exists and still contains its match. | allowlists | green |
| 7 | `The_transition_default_is_the_state_role`: in `tokens.css`, `--default-transition-duration` and `--duration-state` cite the same `var(--duration-…)`, and likewise `--default-transition-timing-function` / `--ease-state`; every `--duration-<role>` resolves (through one `var()`) to a scale step whose value is within 120–200 ms. | `tokens.css` | **red** — no role tokens |

The CSS reader is small and local: strip `/* */` comments, then a brace-depth walk yields
`(at-rule prelude chain, selector, declarations)` triples. It does not need to be a CSS parser
— the files are hand-written and tiny — but it must handle `@keyframes` nested in `@media`
(motion.css) and inside `@layer components` (kiosk `index.css`).

**Counterfactuals required in the PR body** (memory: prove a guard by counterfactual): plant
`transition-opacity` in `CameraViewer.tsx` (fact 1 red via the closure, not via the kiosk
tree); plant `@keyframes x { to { width: 10px } }` in `motion.css` (fact 4); plant
`animation: none` under a reduce block in `motion.css` (fact 5); delete the reduce block from
`motion.css` (fact 5, the positive half); rename the pulse keyframe (fact 6). Quote each red,
then revert.

## 5. The three existing transition sites

| Site | Today | After | Colour |
|---|---|---|---|
| `Button.tsx:96` | `transition-colors` (→ 120 ms, `--ease-out` via bridge) | `transition-colors duration-state ease-state` (→ 120 ms, `--ease-out` via role) | **characterisation**: computed timing identical |
| `PickerPage.tsx:101` | `transition` (colour, bg, border, outline, opacity, shadow, transform, filter, backdrop-filter — Tailwind 4.3.3's stock list) | removed; hover border change is instant | red (fact 1, 2) |
| `PickerPage.tsx:119` | same | same | red |

## 6. `tokens.build.test.ts` (both apps) — additions only

No existing candidate or assertion is edited or removed.

- **Candidates**: `duration-{state,enter,exit,route}`, `ease-{state,enter,exit,route}`,
  `animate-{surface-enter,surface-exit,scrim-enter,scrim-exit}`,
  `animate-{spin,ping,pulse,bounce}`.
- **Both apps** (red): each role utility cites its token; `animate-surface-enter` emits
  `animation` citing `sse-surface-enter` and `--duration-enter`/`--ease-enter` (and so on for
  the other three); `animate-spin|ping|pulse|bounce` **do not compile** (stock namespace replaced).
- **management-web only** (red): the compiled output contains `@keyframes sse-surface-enter`
  both outside and inside `@media (prefers-reduced-motion: reduce)`, and the inner one has no
  `transform`; `::view-transition-new(root)` cites `--duration-route`.
- **kiosk-web only** (red): the compiled output contains **no** `sse-surface-`/`sse-scrim-`
  keyframes (the utilities exist in the shared theme but name keyframes the kiosk never loads —
  and fact 1 forbids using them).
- **kiosk-web only — characterisation** (captured green on develop *before* any change): the
  compiled `.ssE-overlay-highlight` rule declares `outline`, `outline-offset`, `box-shadow`
  and `animation: ssE-overlay-highlight-pulse 1s ease-in-out infinite`; the reduced-motion
  block sets only `animation: none`; the keyframe's 0/50/100 % frames are unchanged.

## 7. `e2e/motion.spec.ts` (Chromium project, seeded console sign-in)

Sign-in and seeding mirrored from `e2e/interaction-states.spec.ts` (`signInAsOperator`).
Waits by condition (ADR-0150). Reads the product through
`document.getAnimations()` — `animationName`, `effect.getTiming()`, `effect.getKeyframes()`,
`effect.pseudoElement`.

1. **Enter** (no-preference): click "Register camera"; `waitForFunction` until the dialog
   content has a running animation; assert name `sse-surface-enter`, duration 200, easing
   `cubic-bezier(0.2, 0, 0, 1)`, keyframe properties ⊆ {opacity, transform}; overlay runs
   `sse-scrim-enter`, properties = {opacity}.
2. **Exit**: click Cancel; assert `sse-surface-exit`, 160 ms, `cubic-bezier(0.4, 0, 1, 1)`,
   then the dialog is detached.
3. **Reduce**: `page.emulateMedia({ reducedMotion: 'reduce' })`; repeat 1: same name and
   duration (**not** absent, **not** 0), keyframe properties = {opacity}.
4. **Route** (US3): click "Layouts"; assert animations on `::view-transition-old(root)` and
   `::view-transition-new(root)`, duration 200, easing `cubic-bezier(0.4, 0, 0.2, 1)`; URL
   `/layouts`. Under `reduce`: same.
5. **Button timing** (characterisation, written and observed green on develop first): the
   "Register camera" button's computed `transition-duration` is `0.12s` and
   `transition-timing-function` is `cubic-bezier(0.2, 0, 0, 1)`.

Capturing an animation that lasts 160–200 ms: install a `document.addEventListener('animationstart', …)`
recorder via `page.addInitScript` before the click and read the recorded
`getAnimations()` snapshot from it, rather than racing a poll against the animation's end.

## 8. Commit sequence (each builds on its own, ADR-0087)

1. `test(292): characterise Button timing and the wall pulse` — the two characterisation
   assertions only; green on develop.
2. `test(292): motion language guard, token-build and e2e expectations` — red.
3. `feat(292): motion roles and the wall rule (US1)` — tokens, theme keys, Button, PickerPage.
4. `feat(292): surfaces enter and leave, reduced motion by opacity (US2)` — motion.css, export,
   import, animation namespace, Dialog, ConfirmDialog.
5. `feat(292): route cross-fade in the console (US3)` — NavItem, view-transition timing.

Commits 3–5 each leave facts/tests for *later* stories red, which is acceptable only if CI
runs on the tip; per ADR-0087 each commit must **build** on its own — tests red for a later
story's commit are fine, compile errors are not.

## 9. Risks

- **R1 — a minifier deduplicates same-name `@keyframes`.** The Vite production build
  (Lightning CSS) could drop one of the two `sse-surface-enter` definitions. Detection: the
  `tokens.build.test.ts` assertion reads Tailwind's output, not Vite's; add a one-off check
  in Phase 5 of `vite build`'s emitted CSS. Fallback: distinct names
  (`sse-surface-enter-reduced`) with the reduce `@media` block setting `animation-name` on
  `.animate-surface-enter` — same facts hold, fact 5's positive half adjusted to accept a
  mapped name. Decide in Phase 4b by reading the built CSS; do not guess.
- **R2 — an exiting dialog coexists with the next one for 160 ms.** Chained dialogs
  (`LayoutEditorDialogChain*`, `OverlayEditorDialogChain*`) and any e2e that closes one dialog
  and immediately locates `getByRole('dialog')` may hit a Playwright strict-mode violation.
  jsdom is unaffected (no CSS → Radix `Presence` unmounts at once). Rule: run the **full** e2e
  suite in Phase 5; if an existing test fails because of the exit animation, **drop the exit
  animation from that surface** (enter only) rather than editing the test. Record which.
- **R3 — Playwright's actionability waits for stability**, so clicks inside an entering dialog
  wait out the 200 ms rise. Adds ≈ 0.2 s per dialog open across the suite; acceptable, noted.
- **R4 — the kiosk reach closure under-resolves** an import form (e.g. a barrel
  `@smart-sentinel-eye/shared/ui`). The fact asserts the closure contains `CameraViewer.tsx`
  (a known member) so a resolver regression fails loudly rather than shrinking the scope.

## 10. Verification (Phase 5)

Spec §8 steps 1–6, quoted in the PR. Step 5 (live wall `getAnimations()`) is mandatory: the
guard proves the source says so; the running wall proves it holds.
