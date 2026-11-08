# Spec 485 — Double-break `OverlayLayerList`'s add/remove/move guards

**Feature Branch**: `485-layerlist-double-break`
**Created**: 2026-11-08
**Status**: Draft
**Issue:** #3226 (feature-level; follow-up recorded in spec 477's verification note, "Caveat on E5–E9", via #3191).
**ADRs:** ADR-0036 (smallest change), ADR-0139 / ADR-0144 (phase 4a), ADR-0037 (phases).
**Refs:** spec 441 (the layer list and `useElementSelection`), spec 477 (the draft-form split, probes E5–E9), specs 482–484 (same lane).
**No new ADR.** Tests only.
**Latency budget (constitution §IV): NOT on the path.** `OverlayLayerList` and `useElementSelection` are the operator's overlay
authoring form in `management-web`; no leg between event arrival and overlay render is touched. The PR says so.
**Phase 4a colour:** behaviour-preserving → **characterisation**. No production file changes unless a guard turns out to be
dead, and that would be a finding reported to the user, not fixed here. Existing tests pass **unmodified**; gap tests go in new files.

## Problem

Spec 477 pinned the draft form's element guards (probes E5–E9) **through a stub seam**, calling the form's callbacks directly. Its
note says: "In the real tree `OverlayLayerList` repeats the guards (`readOnly`, `canAdd`, `canRemove`), so these breaks are not
visible through it. No double break was run against that shared file." Both layers hold the same rule:

| Operation | `OverlayLayerList` (`apps/shared/.../OverlayLayerList.tsx`)         | `useElementSelection` (`apps/management-web/.../overlayDraftForm/useElementSelection.ts`) | Call site (`OverlayDraftForm.tsx`)      |
| --------- | ------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- | --------------------------------------- |
| add       | `if (readOnly \|\| !canAdd) return` in `add`; the menu not rendered | `if (readOnly \|\| fields.length >= MAX_ELEMENTS) return`                                 | `canAdd={fields.length < MAX_ELEMENTS}` |
| remove    | `if (readOnly \|\| !canRemove) return` in `remove`                  | `if (readOnly \|\| fields.length <= 1) return`                                            | `canRemove={fields.length > 1}`         |
| move      | `if (readOnly) return`; target bounds `target < 0 \|\| >= count`    | `if (readOnly) return`                                                                    | `readOnly={readOnly}`                   |

`Button`'s `unavailable` renders `aria-disabled`, **not** the native `disabled`, so a click on an unavailable button still reaches
the handler: the in-handler guards are live defences, not decoration. Each is therefore "jointly defended" (breaking one leaves the
other holding) or it is not; nobody has checked.

## Scope

**In:** for each guard in the table, a deliberate break in **each layer alone** and **in both at once**, run against the covering
suites through the real tree (`OverlayDraftForm` rendering the real `OverlayLayerList`); the failure message of every break read; a
gap test in a new file for any guard no test notices when broken in both; the rest recorded as equivalent or jointly defended, with
evidence.

**Out (explicitly):** any production-code change; the `Button` primitive; the keyboard/announcement behaviour of the list; the
stub-seam tests of spec 477 (kept as they are); the call site's `canAdd`/`canRemove` expressions (they are a third layer and are
probed as such, not changed).

## User Scenarios & Testing

### User Story 1 (P1) — Every element guard is provably defended

As the maintainer, for add / remove / move under `readOnly`, at the element cap and with a single element left, I can say which test
fails when each layer's guard is removed, or why none can.

```gherkin
Scenario: a read-only form refuses to add, remove or move an element
  Given the draft form is read-only
  When the operator activates Add, Remove or Move on the (aria-disabled) buttons
  Then the element set does not change and no announcement is made

Scenario: the element cap holds
  Given the draft already has the maximum number of elements
  Then no element can be added

Scenario: the last element cannot be removed
  Given the draft has one element
  Then it cannot be removed

Scenario: a guard broken in both layers at once is noticed
  Given a guard removed from OverlayLayerList and from useElementSelection
  When the covering suites run
  Then a named test fails with a message that names the guard
```

### User Story 2 (P2) — The record replaces the caveat

The verification note lists every guard, the break in each layer alone and in both, and the result, so spec 477's caveat is closed.

### Edge cases

- **Move bounds** (`target < 0 || target >= count`) exist in the list only; the hook has no counterpart. It is probed alone.
- **`readOnly` makes the Add menu not render at all**; adding is then unreachable through the DOM, so the add `readOnly` guards may be
  equivalent in the real tree. The probe decides; do not assume.
- **A guard defended by a third layer** (the call site's `canAdd` / `canRemove`, or the hidden menu) is recorded as such.

## Requirements

- **FR-001** No existing test is modified; `git diff` on them is empty.
- **FR-002** Every guard in the table is broken in layer one alone, layer two alone, and both, each as a named exactly-once
  substitution restored from git; the failure message of each is read.
- **FR-003** A guard whose double break no test notices gets a new test in a **new file**, green on today's code, proved able to
  fail (message read).
- **FR-004** A guard that cannot be pinned (unreachable, or equivalent) is recorded with the evidence, not given a test that asserts
  an unreachable state.
- **FR-005** No production file changes. If a guard is found dead or wrong, stop and report to the user (a bug fix is a separate issue).
- **FR-006** `eslint src --max-warnings 0`, `tsc --noEmit`, `prettier --check` clean for `shared` and `management-web`;
  `Architecture.Tests` pass or CI is named as the evidence.
- **FR-007** The PR states that no §IV leg is touched.

## Success Criteria

- **SC-001** A matrix in `verification.md`: every guard × {list, hook, both} with the result.
- **SC-002** Every "both" break is either noticed by a named test (message quoted) or recorded as equivalent / unreachable with evidence.
- **SC-003** Tests added are counted and each is shown failing under its break.

## Assumptions

- The double break goes through `OverlayDraftForm` with the real list, since the stub seam is what hid the shared file.
- `OverlayLayerList.test.tsx` tests the list in isolation with its own props; those tests defend the list's guards alone and are not
  affected by a break in the hook.

## Dependencies

Independent of #3225 (PR #3230, parked). Builds on #3191, spec 441 and spec 477.
