# Implementation Plan: The two Save gates adopt the shared `useSubmitGuard`

**Branch**: `482-submit-guard-save-gates` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/482-submit-guard-save-gates/spec.md`

## Summary

Replace two hand-written copies of the submit guard (`overlayDraftForm/useSaveGate.ts`, `layoutEditor/useSaveGate.ts`) with the
**existing** `useSubmitGuard` from `@smart-sentinel-eye/shared/hooks` (ADR-0151), passing each gate's own `saveBlocked`. **First
probe, then pin, then move**: break each concern of the guard once in today's gates (blocked prevents; blocked does not submit; open
submits with the event; open does not prevent; the click and Enter routes), see whether the existing SaveGate tests notice, and write
characterisation tests in **new files** for what they do not. Then switch the two gates, one per commit. Behaviour-preserving, so
**characterisation** (ADR-0139/0144 phase 4a). Same method as specs 476–481.

**Sizing, stated before building.** About 12 lines fall out of the two gates (the 7-line handler and the `FormEvent` import, twice) and
two lines come in. Reported as measured (SC-004).

## Technical Context

- **Stack**: TypeScript, React 19.3, Vitest + Testing Library (jsdom). `apps/management-web`, `apps/shared` (`./hooks` is an export;
  `useSubmitGuard` is already in the barrel).
- **Baseline (2026-11-03, `develop` @ `e9b973a3`)**: two hand-written guard bodies; six `useSubmitGuard` callers; covering tests
  `OverlayDraftFormSaveGate.test.tsx`, `LayoutEditorDialogSaveGate.test.tsx`, `LayoutEditorDialogChainRecovery.test.tsx` (the gates)
  and `useFormDialog.test.tsx` (the shared hook, three cases).
- **Touches**: the two `useSaveGate.ts` files; the shared hook's parameter name and doc line (D1); new test files for gaps. No
  existing test file, no config, no call site of the gates (their returned shape is unchanged).
- **Latency budget (§IV)**: **not on the path** (operator-console form submit, no kiosk, stream or overlay leg). Stated in the PR.

## Constitution Check

| Principle                                             | Verdict                                                                                                                 |
| ----------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| §Testing — refactors stay green                       | Characterisation: existing suites unmodified, plus gap tests written first and seen green on today's code.              |
| §IV latency budget is sacred                          | Not touched. No kiosk, SFU, overlay or render code is in the diff.                                                      |
| ADR-0036 — smallest change, no speculative generality | No new hook: the existing one is adopted. The two gates stay two (their `saveBlocked` terms differ).                    |
| ADR-0151 — the submit guard                           | Same mechanism, wider caller set: `unavailable` keeps the button focusable, the guard on the form's `onSubmit` refuses. |

## Decisions

**D1. Rename the shared hook's parameter `isLoading` → `blocked`, and widen its doc line.** The doc says "refuses a second submit
while one is in flight"; fed `saveBlocked` (also: colour refused, no known camera, chain stale or failed) that reads as a lie.
Alternatives: (a) pass as-is, leave `isLoading`: the code is right and the name is wrong at two of eight callers, which is how a next
reader assumes the gate only guards in-flight; (b) a second hook for the wider meaning: a second abstraction for a body that is
identical. Chosen: rename and widen. A parameter name is not part of a TypeScript call contract (all eight callers are positional),
the three `useFormDialog.test.tsx` cases do not mention the name in code (one title says "Follows isLoading"; a title is data, not an
assertion) and stay **unmodified**. The doc keeps the ADR-0151 reason and adds that the six dialogs pass their request state, the two
Save gates their whole gate.

**D2. Both gates keep their own `saveBlocked` and return shape.** Each gate computes `saveBlocked` (unchanged terms) and then
`const handleFormSubmit = useSubmitGuard(saveBlocked, onSubmit)`. The returned `{ saveRef, saveBlocked, handleFormSubmit }` (overlay:
plus `onColourValidityChange`) is identical, so `OverlayDraftForm` and `LayoutEditorDialog` are untouched.

**D3. Types.** The gates' `onSubmit: (event?: BaseSyntheticEvent) => Promise<void>` is assignable to the hook's
`(event: FormEvent) => unknown` (a function accepting an optional wider event accepts the narrower one). `tsc` is the check; if it
says otherwise that is a finding, not an excuse to loosen a type.

**D4. Identity.** The old handler is a fresh function per render, and so is the shared one (it does not memoise). Nothing depends on
either; a probe that memoises the shared handler is not a break of this spec and is not run. If the probe shows a test that would
notice a changed identity, that is recorded.

**D5. Order: pin, switch, delete (nothing to add).** The shared hook already exists, so there is no "add" commit. Each switch is a
pure substitution; the rename (D1) is its own commit before the switches so they stay pure.

## Phases and commits

| Step    | Commit                                                                                                                                                             | §IV legs |
| ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------- |
| T-probe | Break each concern in both gates (named, exactly-once substitutions, `probe482.cjs`); run the SaveGate/ChainRecovery tests; record the matrix in `verification.md` | none     |
| T-gap   | Gap tests in **new** files for what nothing noticed, green on today's code, proved able to fail (message read)                                                     | none     |
| 1       | Shared hook: parameter `blocked`, doc widened (D1); `useFormDialog.test.tsx` re-run unmodified                                                                     | none     |
| 2       | `overlayDraftForm/useSaveGate` adopts the hook; the overlay tests re-run unchanged                                                                                 | none     |
| 3       | `layoutEditor/useSaveGate` adopts the hook; the layout tests re-run unchanged                                                                                      | none     |
| 4       | Verification note, tick tasks, QA dispositions                                                                                                                     | none     |

After each switch, the matching break is applied to the **shared** hook and the gate's tests must fail there (counterfactual,
SC-003). New untracked files are staged before counterfactuals.

## Risks and how they are covered

- **A gate's guard only worked because of the hand-written copy** (a test asserts something the shared hook does not do): the probe
  and the first switch find it; an edited assertion is a **block**, reported, not adjusted.
- **Enter-route coverage is thin**: an `aria-disabled` button still lets Enter submit; if only the click route is tested, the probe
  shows a surviving break and a gap test is written.
- **A `saveBlocked` read goes stale**: the hook closes over the render's flag exactly as the hand-written function did; probed with a
  rerender from blocked to open.
- **The rename hides a caller**: all eight are positional; `tsc` and the six callers' suites re-run.

## Project Structure

```text
apps/shared/src/hooks/useFormDialog.ts                                   (parameter name + doc line)
apps/management-web/src/features/overlays/overlayDraftForm/useSaveGate.ts
apps/management-web/src/features/layouts/layoutEditor/useSaveGate.ts
  new: gap tests, one file per gate, only where the probe finds an unnoticed break
```
