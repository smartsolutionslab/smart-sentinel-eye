# Spec 482 — The two Save gates adopt the shared `useSubmitGuard`

**Feature Branch**: `482-submit-guard-save-gates`
**Created**: 2026-11-03
**Status**: Draft
**Issue:** #3223 (feature-level; follow-up recorded by the frontend refactoring survey, #3190–#3198, via #3192 / #3193).
**ADRs:** ADR-0036 (smallest change, no speculative generality), ADR-0075 (RTK Query), ADR-0139 / ADR-0144 (phase 4a), ADR-0151 (the
submit guard), ADR-0037 (phases).
**No new ADR.** An adoption of an existing convention.
**Latency budget (constitution §IV): NOT on the path.** Both gates guard an operator-console form's submit event
(`management-web`, overlay draft and layout editor). Nothing here sits between event arrival and overlay render; no leg is touched.
The PR says so rather than leaving the section blank.
**Phase 4a colour:** behaviour-preserving → **characterisation**. Existing suites pass **unmodified**; an assertion that has to be
edited means behaviour moved: block (CLAUDE.md). A refactor that is also a bug fix is two issues.

## Problem

Issue #3223 asks: extract `useSubmitGuard(saveBlocked, onSubmit)` **only if the two call sites really match**, otherwise close as not
worth it with the diff that shows why.

**Premise check on `develop` @ `e9b973a3` (2026-11-03): the hook already exists, and the two sites do match it.**

- `apps/shared/src/hooks/useFormDialog.ts` exports `useSubmitGuard(isLoading, onSubmit): (event: FormEvent) => void` (ADR-0151),
  re-exported from the `hooks` barrel and used by six other dialogs (`RenameCameraDialog`, `EditCameraAddressDialog`,
  `RegisterCameraDialog`, `WallForm`, `RuleDialog`, `SystemVariableDialog`). Its body: when the flag is set, `preventDefault()` and
  return; otherwise `void onSubmit(event)`.
- The two Save gates, `overlayDraftForm/useSaveGate.ts` and `layoutEditor/useSaveGate.ts`, each hand-write that body **verbatim**,
  with `saveBlocked` where the shared hook has `isLoading`:

  ```ts
  function handleFormSubmit(event: FormEvent) {
    if (saveBlocked) {
      event.preventDefault();
      return;
    }
    void onSubmit(event);
  }
  ```

So the answer to the issue's question is **yes, they match**, and the work is **adopting** the existing hook, not writing a new one.
Each gate keeps computing its own `saveBlocked` (the terms differ: `colourRefused` vs `knownCameras.size === 0`) and still returns it
for the Save button's `unavailable`; only the handler is replaced.

### What the issue does not settle

1. **Naming.** The shared hook's first parameter is `isLoading` and its doc says it "refuses a second submit while one is in flight".
   Fed `saveBlocked` (which also covers a stale chain, a refused colour and no known camera) that reads wrongly. The plan decides
   between renaming the parameter and widening the doc line, or passing as-is. A parameter name is not part of the contract, and
   `useFormDialog.test.tsx` stays unmodified either way.
2. **Types.** The gates' `onSubmit` is `(event?: BaseSyntheticEvent) => Promise<void>`; the hook takes `(event: FormEvent) => unknown`.
   Assignable in principle; `tsc` confirms it.
3. **Does the existing suite notice a broken guard?** `OverlayDraftFormSaveGate.test.tsx` and `LayoutEditorDialogSaveGate.test.tsx`
   exist; whether each pins both halves (blocked: prevented and not submitted; unblocked: submitted and not prevented) and both
   routes (click, Enter) is for the probe to answer, not for this spec to assume.

## Scope

**In:** `overlayDraftForm/useSaveGate.ts` and `layoutEditor/useSaveGate.ts` call the shared `useSubmitGuard`; their hand-written
`handleFormSubmit` bodies and the `FormEvent` import they alone needed are removed; the shared hook's parameter name and doc line if
the plan so decides.

**Out (explicitly):** merging the two `useSaveGate` hooks (their `saveBlocked` terms and returns differ); any change to a
`saveBlocked` term; the `saveRef` / focus handling; the six dialogs that already use the hook; any new suppression; any new
abstraction.

## User Scenarios & Testing

### User Story 1 (P1) — Both Save buttons guard exactly as before

As an operator editing an overlay draft or a layout, a Save that is blocked (request in flight, colour refused, no camera known, chain
being re-read or failed) does nothing when I click it or press Enter, and a Save that is open submits once. As the maintainer I can
prove neither half moved.

**Independent test:** the existing gate tests pass with no line edited.

```gherkin
Scenario: a blocked gate refuses the submit
  Given a Save gate whose saveBlocked is true
  When the form's submit event fires (button click or Enter in a field)
  Then the event's default is prevented
  And onSubmit is not called

Scenario: an open gate submits
  Given a Save gate whose saveBlocked is false
  When the form's submit event fires
  Then onSubmit is called once with that event
  And the event's default is not prevented by the gate

Scenario: the existing tests are unchanged
  Given OverlayDraftFormSaveGate, LayoutEditorDialogSaveGate, LayoutEditorDialogChainRecovery and useFormDialog tests
  When they are run after each switch
  Then every test passes and git diff shows none of them edited
```

### User Story 2 (P2) — One guard to maintain

```gherkin
Scenario: one implementation of the guard
  When the change is done
  Then the "if blocked, preventDefault, else onSubmit" body exists in exactly one module
```

### Edge cases

- **Hook order** inside each gate is unchanged in effect: the shared hook adds no state or effect, and is called after `saveBlocked`
  is computed (it needs the value).
- **Handler identity.** The hand-written handler is a new function each render; the shared one is too. Nothing depends on it (the form
  `onSubmit` prop); the probe confirms rather than assumes.
- **`onSubmit` result.** Both discard it (`void`); a rejected promise is the caller's, as today.
- **Event argument.** The gate passes the same event object through; the hook does too.

## Requirements

- **FR-001** Every existing test passes after each commit; none is modified.
- **FR-002** Each of the two gates obtains its `handleFormSubmit` from the shared `useSubmitGuard`, passing its own `saveBlocked`; the
  hand-written bodies are removed.
- **FR-003** Behaviour is unchanged: blocked → `preventDefault()` and `onSubmit` not called; open → `onSubmit(event)` called once and
  nothing prevented by the gate; both routes (click, Enter).
- **FR-004** Each gate's returned shape is unchanged (`saveRef`, `saveBlocked`, `handleFormSubmit`, and for the overlay gate
  `onColourValidityChange`).
- **FR-005** No `saveBlocked` term, `saveRef` or focus behaviour changes; no suppression is added.
- **FR-006** Each commit builds and passes the affected suites on its own (bisect); one gate per commit.
- **FR-007** `eslint src --max-warnings 0`, `tsc --noEmit`, `prettier --check` clean for `management-web` and `shared`;
  `Architecture.Tests` pass (they scan `.tsx`).
- **FR-008** No change outside the two gates, the shared hook's parameter name and doc line (if the plan so decides),
  `specs/482-…` and `.specify/feature.json`.
- **FR-009** The PR states that no §IV leg is touched.

## Success Criteria

- **SC-001** The covering suites: same test names before and after, all passing, `git diff` on them empty.
- **SC-002** The hand-written `if (saveBlocked) { event.preventDefault(); return; }` body appears in no `useSaveGate`; `grep` finds
  the guard body once, in `useFormDialog.ts`.
- **SC-003** For each concern (blocked prevents; blocked does not call `onSubmit`; open calls `onSubmit` with the event; open does
  not prevent; click route; Enter route), per gate, a deliberate break is noticed by a named test; where nothing notices, a new test
  in a **new file** is written first, green on today's code, proved able to fail (the message read), and kept; equivalents are
  recorded with evidence.
- **SC-004** Line counts reported before/after for the touched files, with the total.

## Assumptions

- Passing `saveBlocked` as the hook's flag is the intended use: the guard's job is "refuse while the gate is shut", of which "a
  request is in flight" is one case (ADR-0151 introduced it for the in-flight case; the six existing callers pass `isLoading`).
- The existing SaveGate tests cover the blocked and open halves per gate; the probes decide where they do not.

## Dependencies

Independent of the other follow-ups (#3222 merged; #3224–#3226 open). Builds on #3192 / #3193, which created the two `useSaveGate`
hooks, and ADR-0151, which created the shared guard.
