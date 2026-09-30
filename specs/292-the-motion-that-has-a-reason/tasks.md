# Tasks 292: The motion that has a reason

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2334 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing). **T001 and T002 are characterisation** — observed
green on `develop` before any product change, and must pass **unmodified** after. Facts 4, 5, 6
of `MotionLanguageTests` are declared green in advance (spec §7) and need quoted
counterfactuals, not a red.
**Engineers**: `test-writer` (4a: T001–T005, including the C# test file); then
`frontend-engineer` (4b: T010–T016). No backend or infra engineer: the only `.cs` file is an
architecture test. **Reviewers**: `frontend-reviewer` (+ `backend-reviewer` for
`MotionLanguageTests.cs`).
**ADR**: none needed (spec §4). The one interpretation of ADR-0146 (Button's colour
transition as an allowlisted paint transition) is marked in spec §4 with a one-line fallback.
**Tracking**: feature-level issue #2334 (Project #13). No per-task issues.

Format: `[ID] [P?] [Story] description — colour`.

**Foundational / blocking**: T010 (tokens + theme keys) blocks T011–T016. No Shared.Kernel,
Shared.Contracts, AppHost or CI work.
**Parallelism** (ADR-0109, disjoint files): 4a — T001 ∥ T003 ∥ T005, and T002 ∥ T003.
**Two file collisions**: T001 and T004 both edit the kiosk `tokens.build.test.ts` (run T004
after T001), and T002 and T005 both create `e2e/motion.spec.ts` (run T005 after T002).
4b — after T010: T011 ∥ T012 ∥ T013 ∥ T014 (owners: `Button.tsx`; `PickerPage.tsx`;
`motion.css` + `apps/shared/package.json` + management `index.css`; `Dialog.tsx` +
`ConfirmDialog.tsx`). T015 after T013 (both edit `motion.css`).

**Do not** touch: `apps/kiosk-web/src/styles/index.css` (the pulse, byte-identical);
`DesignTokenLayerTests.cs`, `SharedUiTokenUsageTests.cs`, `InteractionStateTests.cs`; any
existing assertion in either `tokens.build.test.ts` (additions only); the two bridges in
`tokens.css`; Popover/DropdownMenu/Select/Tooltip/CommandPalette (follow-up).

## Phase 4a: characterise, then red (test-writer)

- [ ] **T001 [P] [US1]** Characterise the wall pulse. In
  `apps/kiosk-web/src/styles/tokens.build.test.ts`, add one `it` pinning the compiled
  `.ssE-overlay-highlight` rule (`outline`, `outline-offset`, `box-shadow`,
  `animation: ssE-overlay-highlight-pulse 1s ease-in-out infinite`), its reduced-motion block
  (`animation: none` and nothing else), and the keyframe's three frames. **Characterisation**:
  run on `develop` first, quote green; it must stay green unmodified to the end.
- [ ] **T002 [P] [US1]** Characterise Button's timing. In a new `e2e/motion.spec.ts`, plan
  §7 item 5 only (computed `transition-duration: 0.12s`, `transition-timing-function:
  cubic-bezier(0.2, 0, 0, 1)` on "Register camera"). **Characterisation**: observe green on
  `develop` before T011, quote it. Also record `e2e/interaction-states.spec.ts` and
  `Button.test.tsx` green on develop — they must pass unmodified after T011.
- [ ] **T003 [P] [US1][US2]** `tests/Architecture.Tests/MotionLanguageTests.cs`: facts 1–7 of
  plan §4, the kiosk reach closure (with the R4 membership assertion), the local CSS
  brace-depth reader, the two shrink-only allowlists with reasons. **Measure first**: the
  develop violator list must be exactly `PickerPage.tsx` ×2 (facts 1, 2), `Button.tsx`
  (fact 3), missing role tokens (fact 7). Any other hit is a matcher defect (e.g. prose
  containing the word "transition") — fix the matcher (whole-token match inside class-shaped
  strings), **never** allowlist it. Quote the red. Then quote each plan §4 counterfactual for
  facts 4, 5, 6 (planted in a scratch copy, reverted). Paths normalised to `/`. — **red**
- [ ] **T004 [US1][US2]** (after T001) Both `apps/{management-web,kiosk-web}/src/styles/tokens.build.test.ts`:
  plan §6 candidates and assertions, **additions only**, including the kiosk absence
  assertion and the management reduced-motion keyframe assertion. Quote the red. — **red**
- [ ] **T005 [US2][US3]** (after T002) `e2e/motion.spec.ts`: plan §7 items 1–4 (enter, exit, reduce,
  route), the `animationstart` recorder via `addInitScript`, condition waits (ADR-0150).
  Quote the red (no animation found). — **red**

## Phase 4b: implement (frontend-engineer)

- [ ] **T010 [US1]** `tokens.css`: the eight role tokens (plan §2) and the motion-block
  comment; bridges untouched. `tailwindTheme.ts`: role keys in `transitionDuration` /
  `transitionTimingFunction` (scale keys kept), and the `animation` namespace (four keys,
  **not** under `extend`, no `DEFAULT`) (plan §3). **Blocks T011–T016.** — red → green:
  fact 7, T004's role-utility and stock-`animate-*` assertions.
- [ ] **T011 [P] [US1]** `Button.tsx:96`: `transition-colors` → `transition-colors
  duration-state ease-state`. — **characterisation** (T002, `interaction-states.spec.ts`,
  `Button.test.tsx` green, unmodified); red → green: fact 3.
- [ ] **T012 [P] [US1]** `PickerPage.tsx:101,119`: remove `transition`. — red → green:
  facts 1, 2.
- [ ] **T013 [P] [US2]** New `apps/shared/src/ui/motion/motion.css` (four keyframes + the
  reduce redefinitions, plan §3); `apps/shared/package.json` export `./ui/motion/*`;
  `apps/management-web/src/styles/index.css` imports it as line 2. Kiosk `index.css` **not**
  touched. — red → green: T004 management keyframe/kiosk absence; facts 4, 5 stay green.
- [ ] **T014 [P] [US2]** `Dialog.tsx` and `ConfirmDialog.tsx`: Overlay
  `data-[state=open]:animate-scrim-enter data-[state=closed]:animate-scrim-exit`; Content
  `data-[state=open]:animate-surface-enter data-[state=closed]:animate-surface-exit`. Existing
  jsdom dialog tests must stay green unmodified. — red → green: T005 items 1–3.
- [ ] **T015 [US3]** (after T013) `motion.css`: the `::view-transition-old/new(root)` timing
  block. `ShellLayout.tsx` `NavItem`: pass `viewTransition` to `NavLink`. `ShellLayout.test.tsx`
  green unmodified (jsdom has no `startViewTransition`; React Router falls back). — red →
  green: T005 item 4.
- [ ] **T016 [US1][US2][US3]** Full local gate: `pnpm -r typecheck`, `pnpm -r lint`,
  `pnpm -r test`, `dotnet test tests/Architecture.Tests`, and the **full** Playwright suite
  (plan R2: if an existing e2e fails because an exiting dialog coexists with the next one,
  drop the exit animation from that surface and record it — do not edit the test). Read
  `vite build`'s emitted management CSS for R1 (both `sse-surface-enter` definitions present)
  and quote the lines.

## Phase 5: verify

- [ ] **T020** Spec §8 steps 1–6, quoted in the PR: dialog/route animations read from
  `getAnimations()` under both reduced-motion settings; **the live four-tile wall's
  `document.getAnimations().length` is 0 with no highlight and only the pulse with one**;
  the `render-leg-gate` job's verdict on the PR run.

## Dependencies

```
T001 ─► T004 ─┐
T002 ─► T005 ─┼─(4a done, reds/greens quoted)─► T010 ─┬─► T011
T003 ─────────┘                                        ├─► T012
                                                       ├─► T013 ─► T015
                                                       └─► T014
                                  T011..T015 ─► T016 ─► T020
```
