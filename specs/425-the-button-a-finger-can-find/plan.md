# Plan 425 — The button a finger can find

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2667

## 1. Constitution and ADR check

| Authority | How this plan complies |
|---|---|
| ADR-0146 (wall subtractions; console "transform and opacity only") | M2 removes the only paint transition in `apps/*/src`. Kiosk variants avoid bright static fills and translucency over video (spec §1.1). |
| ADR-0148 (prohibition by absence) | No token gets a kiosk-specific value; M1, which would have, is rejected on this ground too (spec §1.2 V4). |
| ADR-0077 / ADR-0078 | Sizes are a lookup inside the design-system `Button`; values from existing scales; no new token. |
| ADR-0151 | `unavailable` / `disabled` / `busy` behaviour untouched; their treatments stay in their own `clsx` slots. |
| ADR-0036 | Two sizes, both with consumers in this PR (`md`: every console site; `touch`: seven kiosk sites). No `sm`. |
| Constitution §IV / ADR-0123 | Composite + render leg cited (spec §4); nothing new animates or composites. |
| ADR-0139 / ADR-0144 | Phase 4a red; pins declared green in advance; no gate weakened — the one allowlist edit is a *removal* demanded by fact 6. |
| ADR-0087 | Five commits, each builds and keeps `typecheck` green (§6). |

**No ADR is written. No constitution section changes.** Bounded contexts / Shared.Contracts / AppHost:
untouched — this is frontend-only (`apps/shared`, `apps/kiosk-web`) plus one architecture-test file
edit and one new architecture-test class.

## 2. Layers touched

| File | Change |
|---|---|
| `apps/shared/src/ui/primitives/Button.tsx` | `export type ButtonSize = 'md' \| 'touch'`; `size?: ButtonSize` on `ButtonProps` (doc comment: why a lookup, not `className`); destructure `size = 'md'`; move `px-4 py-2 text-sm` out of `base` into `const sizing: Record<ButtonSize, string> = { md: 'px-4 py-2 text-sm', touch: 'px-6 py-3 text-base' }`; delete `transition-colors duration-state ease-state`. Update the spec-268 comment block only where it now states something false. |
| `apps/shared/src/ui/primitives/Button.test.tsx` | New cases (§5 R1-R3). Existing cases unmodified. |
| `tests/Architecture.Tests/MotionLanguageTests.cs` | `PaintTransitionAllowlist` becomes `[]`; its doc comment says spec 425 / #2667 emptied it (M2). The class-level "Measured on develop" paragraph naming `Button.tsx` for fact 3 is historical — leave it. **Check before editing:** an empty array must still compile and fact 6 must handle an empty list (it iterates; it does). |
| `tests/Architecture.Tests/KioskButtonPrimitiveTests.cs` (new) | §5 R6. |
| `e2e/motion.spec.ts` | The spec-292 T002 characterisation (`0.12s` / `cubic-bezier(0.2, 0, 0, 1)`) is replaced by R5. Rewriting it is the point of the change (the owner decided hover becomes instant), not a weakened gate; the PR body says so and quotes spec 292 §4's named fallback. |
| `e2e/interaction-states.spec.ts` | Comment at `:131` ("fills are `transition-colors` (120 ms)") corrected — comment only; the helper's logic is unchanged and stays correct. |
| `e2e/kiosk-touch-target.spec.ts` (new) | R7. |
| `apps/kiosk-web/src/App.tsx` | S1, S2. |
| `apps/kiosk-web/src/features/auth/ReconnectingScreen.tsx` | S3. |
| `apps/kiosk-web/src/features/cell/LayoutGrid.tsx` | S4, S5 (§IV citation in the PR). |
| `apps/kiosk-web/src/features/picker/PickerPage.tsx` | S6 only. S8/S9 untouched. |
| `apps/kiosk-web/src/features/wall/WallPage.tsx` | S7. |

Every kiosk site imports `{ Button } from '@smart-sentinel-eye/shared/ui/primitives/Button'` — the deep
specifier, never `@smart-sentinel-eye/shared/ui` (spec §1.1). Each site keeps its `onClick`, its text
and `type="button"` (which `Button` defaults anyway — drop the explicit attribute only if eslint/prettier
want it; behaviour is identical).

## 3. Entities / invariants (design-system level; no domain model)

- `ButtonSize` is a closed union; `sizing` is a total `Record<ButtonSize, string>` — the compiler enforces
  a class string for every size.
- Exactly one sizing class string applies per render (it is one `clsx` slot), so no two padding/type
  utilities can race in the compiled sheet (the defect class the spec-268 comment at `Button.tsx:87-93`
  records for border colours).
- `size` never reaches the DOM (destructured, like `unavailable` and `busy`).
- `Button`'s class string contains no motion utility; `MotionLanguageTests` fact 1 holds with `Button.tsx`
  in the kiosk reach set.

## 4. Messaging

N/A — no domain or integration event, no endpoint, no realtime message.

## 5. Tests

All of R1-R6 and the pins run **without Docker**. R5 and R7 are Playwright and CI-only on this
machine (Docker down). Commands: `pnpm --filter @smart-sentinel-eye/shared exec vitest run
src/ui/primitives/Button.test.tsx`; `pnpm --filter @smart-sentinel-eye/shared typecheck`;
`dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~MotionLanguage|FullyQualifiedName~KioskButton"`;
`pnpm --filter @smart-sentinel-eye/kiosk-web test`; `typecheck`; `lint`; `pnpm format:check`.
Run `tsc --noEmit` alongside vitest: a missing named import binds to `undefined` silently in vitest.

### 5a. Red (observed failing before the source change; failure quoted verbatim in the PR)

| # | Where | Asserts | Why red on the branch before C3/C5 |
|---|---|---|---|
| R1 | `Button.test.tsx` | `size="touch"` → `px-6`, `py-3`, `text-base` present; `px-4`, `py-2`, `text-sm` absent | No lookup exists; base carries `px-4 py-2 text-sm`. |
| R2 | `Button.test.tsx` | `size="touch"` → `button.hasAttribute('size')` is `false` | Declared-only prop falls into `...rest` and React writes `size="touch"` onto `<button>`. **If this arrives green** (React declining the attribute), stop and report it as spec 268's `unavailable` case did — do not force it. |
| R3 | `Button.test.tsx` | For each variant × size: no class starts with `transition`, `duration-` or `ease-` | `transition-colors duration-state ease-state` in `base`. |
| R4 | `MotionLanguageTests.cs` | `PaintTransitionAllowlist = []` | Fact 2 (`A_paint_transition_is_deliberate`) red: `Button.tsx: 'transition-colors' outside the paint-transition allowlist`. (Shrinks the gate; does not weaken it.) |
| R5 | `e2e/motion.spec.ts` | Register camera button: `toHaveCSS('transition-duration', '0s')` | Today `0.12s`. **CI-only** — red is argued from the T002 characterisation that currently passes at `0.12s`, and observed on the PR's first CI run only if the red commit is pushed alone; otherwise state "not observed red locally (Docker down)" in the PR, per ADR-0139's honesty clause. |
| R6 | `KioskButtonPrimitiveTests.cs` (new) | (a) no `<button` JSX opening tag in non-test `apps/kiosk-web/src/**/*.tsx` outside a shrink-only allowlist of `(path, match, reason)` — the two picker cards in `PickerPage.tsx`, matched by a distinctive substring of their `onClick` (`onOpen(layout.layoutIdentifier)` / `onOpenWall(wall.wallIdentifier)`) in the element's opening tag; (b) honesty: every allowlist entry's file exists and still contains its match; (c) the scan is non-empty (≥ 1 `.tsx` file scanned) so a moved tree cannot pass vacuously | (a) red on S1-S7, naming seven `file:line`s. (b), (c) green in advance — prove each by a planted counterfactual quoted in the PR. Paths via `RepositorySource.RelativePath`; comment-stripped like `MotionLanguageTests`. |
| R7 | `e2e/kiosk-touch-target.spec.ts` (new) | Fresh context, `baseURL http://localhost:5174`, no storage (mirrors `kiosk-comes-back.spec.ts:80`) → `getByRole('button', { name: /^sign in$/i })`: `boundingBox().height >= 48`; after `hover()`, `el.getAnimations().length === 0` | Height today is 48 px exactly, so the **height half is a pin** (green on develop; say so). The animation half: today a hover on a raw `<button>` with no transition is also 0 — **also a pin**. R7 is therefore *not* 4a evidence; it is the real-browser check of SC-1/SC-2 on the kiosk, CI-only. |

### 5b. Characterisation / pins (green before and after, unmodified)

- `Button.test.tsx` existing 14 cases (none asserts a motion class or padding).
- A new pin in `Button.test.tsx`: no `size` and `size="md"` both carry `px-4`, `py-2`, `text-sm` — green on
  develop for the no-prop case (written first, observed green, then must stay green).
- `e2e/interaction-states.spec.ts` (all cases unmodified; CI).
- Kiosk component tests that find buttons by role and name: `App.test.tsx:61,223`,
  `PickerPage.test.tsx:140`, and any `CellPage` / `WallPage` tests that click Back / Retry — unmodified.
- `MotionLanguageTests` facts 1, 3-7 and both apps' `tokens.build.test.ts` — unmodified, green.

## 6. Commit sequence (each builds alone; `typecheck` green at every commit)

| # | Commit | Content | Suite state |
|---|---|---|---|
| C1 | `docs(2667): specify, plan and task the Button touch size and instant hover` | this folder | green |
| C2 | `test(2667): Button has a touch size and no state animation (red)` | R1-R5 + the md pin; `ButtonSize` and `size?` **declared only** in `ButtonProps` so `typecheck` passes (spec 268 T003 precedent) | R1-R4 red |
| C3 | `fix(2667): Button sizes by lookup and changes state without a transition` | `Button.tsx` lookup + motion removal; `interaction-states.spec.ts:131` comment | green |
| C4 | `test(2667): the kiosk's action buttons are the shared Button (red)` | R6 + R7 | R6(a) red |
| C5 | `fix(2667): the kiosk's action buttons use Button at touch size` | S1-S7 | green |

C2/C3 and C4/C5 are phase-4a red/green pairs; a bisect landing on C2 or C4 should re-run one commit over
(CLAUDE.md). C2 and C4 may be swapped with their pairs' order preserved; C5 depends on C3 (the `touch`
size must exist).

## 7. One PR, two pairs

One PR: the slice is small (≈ 8 source files, ≈ 60 changed lines) and US2 cannot ship without US1.
The split the brief asks for is preserved *inside* the PR as two independent red/green pairs, so if
review wants US1 alone, C1-C3 is a complete, mergeable prefix.

## 8. Risks

- **R-a: `size` collides with an HTML attribute name.** `<button>` has no `size` attribute and React's
  `ButtonHTMLAttributes` has no `size` property, so `ButtonProps` can declare it without an `Omit`. If tsc
  disagrees (a React types bump), use `Omit<ComponentPropsWithRef<'button'>, 'size'>` — note it in the PR.
- **R-b: kiosk reach set grows.** `Button.tsx` imports only `@radix-ui/react-slot`, `clsx` and React
  types; fact 1 stays green (spec §1.2 V5). If a later edit makes `Button.tsx` import another shared
  module, fact 1 scans it too — that is intended.
- **R-c: CellPage header grows** from ~52 px to ~74 px (S5 at touch size), covering more of the top of a
  single-camera view. Accepted: a 28 px target is the defect the issue names. Flag in the PR.
- **R-d: console look of the kiosk-only variants.** None — console call sites pass no `size`.

## 9. Follow-up to file (text for the orchestrator, not `agent:ready`)

> **`WallForm` Up/Down are raw buttons smaller than `Button`'s `md` size.** `apps/management-web/src/features/walls/WallForm.tsx:143,152`
> (`px-2 py-0.5 text-xs`, inline row controls). Spec 425 gave `Button` `size: 'md' | 'touch'`; moving these
> needs either an `sm` size (a design call: does the console want a compact button?) or a documented reason
> they stay raw. Related: #2667, #2623.
