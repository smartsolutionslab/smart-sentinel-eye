# Spec 486 — Is the layout Save gate's `currentChain === undefined` term pinned?

**Feature Branch**: `486-layout-chain-gate-term`
**Created**: 2026-11-08
**Status**: Draft
**Issue:** #3232 (feature-level; follow-up recorded while probing spec 482 / #3223, merged in #3228).
**ADRs:** ADR-0036 (smallest change, no speculative generality), ADR-0139 / ADR-0144 (phase 4a), ADR-0037 (phases).
**Refs:** spec 156 (the chain read), spec 160 (`saveBlocked`, FR-002 / FR-007), spec 482 (`useSubmitGuard`), spec 485 (same method).
**No new ADR.** Probes and, at most, tests.
**Latency budget (constitution §IV): NOT on the path.** `LayoutEditorDialog` is the operator's layout authoring form in
`management-web`; no leg between event arrival and overlay render is touched. The PR says so.
**Phase 4a colour:** behaviour-preserving → **characterisation**. No production file changes unless a guard turns out to be dead or
wrong, and that is a finding reported to the user, not fixed here. Existing tests pass **unmodified**; new tests go in new files.

## Problem

`apps/management-web/src/features/layouts/layoutEditor/useSaveGate.ts` blocks Save with

```ts
const saveBlocked =
  isLoading || knownCameras.size === 0 || (isEdit && (currentChain === undefined || chainFetching || chainFailed));
```

Premise check, `origin/develop` @ `1745446a`+, 2026-11-08: removing the `currentChain === undefined ||` term (one exactly-once
substitution) leaves **all 213 tests in `src/features/layouts` passing**; restored from git afterwards. The claim in #3232 holds.

It is not simply untested. A second check for the same condition sits in the submit handler,
`useLayoutEditorForm.ts:72` (`if (currentChain === undefined) return;`), kept "as defence-in-depth alongside `saveBlocked`". The same
shape as #3226: a guard held in two places.

**Two test comments overstate the term.** `LayoutEditorDialogChainRetention.test.tsx` ("`currentChain === undefined` is the only thing
left standing between the operator and A's version…") and `LayoutEditorDialogChainRecovery.test.tsx` ("closed by the
`currentChain === undefined` half of the predicate alone — `chainFetching` could be deleted and that file would stay green") say the
term is the sole defence in the states they drive. The break above contradicts that for the tested states: in each, `chainFetching` or
`chainFailed` is also true, so the other terms cover them. Those comments are descriptions, not assertions; correcting them is out of
scope here (comment-only, and spec 486 changes no production or existing test file), and is reported, not done.

## What the issue does not settle

1. **Reachability.** `currentChain` is `useGetLayoutQuery(id ?? skipToken, { refetchOnMountOrArgChange: true }).currentData`. For
   `isEdit` (a non-skipped query), is there any real RTK Query state with `currentData === undefined`, `isFetching === false` and
   `isError === false`? The hook reports a first, uninitialised render as fetching; a rejected read is `isError`; an argument change
   keeps `currentData` undefined only while fetching. If no such state exists in the real store, the term is **equivalent** (redundant
   with `chainFetching || chainFailed`) and no test of it is warranted. A mocked `useGetLayoutQuery` can produce the state; whether that
   makes a test meaningful is the plan's call, with the real store as the evidence.
2. **Where a break is felt.** If reachable, which observable differs without the gate term: Save's `aria-disabled`, the submit guard, or
   nothing (the handler check at `:72` refuses silently)?
3. **The overlay counterpart.** `overlayDraftForm/useSaveGate.ts` types `currentChain` as `{ version: number } | undefined`; spec 477
   recorded its terms as pinned. This spec does not re-open it; it is probed only if the layout answer suggests a shared pattern.

## Scope

**In:** break the gate term alone, the handler check alone, and both, through the real `LayoutEditorDialog`; decide reachability against
the real store; read every failure message; a test in a new file for whatever is reachable and unpinned, or a recorded equivalence.

**Out (explicitly):** any production-code change; the comments in the two layout tests (reported, not edited); the `Button` primitive;
`useSubmitGuard`; `chainFetching` / `chainFailed` terms (probed only as the cover for `currentChain`); the overlay form.

## User Scenarios & Testing

### User Story 1 (P1) — The term is either pinned or proved redundant

```gherkin
Scenario: no chain to submit
  Given an edit dialog with no chain version available
  When the operator presses Save (or Enter in a text field)
  Then no PATCH is issued

Scenario: the term is redundant
  Given the real RTK Query store
  When an edit dialog has no chain version and is neither fetching nor failed
  Then the state is unreachable, and the term is recorded as equivalent with that evidence

Scenario: the term and the handler check are both gone
  Given currentChain === undefined removed from useSaveGate and from useLayoutEditorForm
  When the covering suites run
  Then a named test fails, or the state is recorded as unreachable
```

### User Story 2 (P2) — The overstated comments are on the record

The verification note quotes the two comments, shows the break that contradicts them, and says they were left unedited.

### Edge cases

- **`isEdit` false** (create): the term is inside `isEdit && (…)` and does not apply.
- **Reopen on a different layout** (`A → undefined → B` on one component): the Retention file's concern; `refetchOnMountOrArgChange: true`
  makes the first state fetching.
- **Eviction with zero subscribers** (`useLayoutChain.ts` comment): the entry is evicted before reopening, so the next mount fetches.

## Requirements

- **FR-001** No existing test or production file is modified; `git diff` on them is empty.
- **FR-002** The gate term, the handler check and both are each broken as named exactly-once substitutions, restored from git; failure
  messages (or "none") are read and recorded.
- **FR-003** Reachability of "no chain, not fetching, not failed, `isEdit`" is decided against the **real RTK Query store**, not only a mock.
- **FR-004** If reachable and unpinned: a test in a **new file**, green on today's code, proved able to fail (message read).
- **FR-005** If unreachable: recorded as equivalent with the evidence; no test of an unreachable state.
- **FR-006** `eslint src --max-warnings 0`, `tsc --noEmit`, `prettier --check` clean; `Architecture.Tests` pass or CI is named as evidence.
- **FR-007** The PR states that no §IV leg is touched, and lists the two overstating comments.

## Success Criteria

- **SC-001** A table: gate term / handler check / both × real-tree result, with messages.
- **SC-002** Reachability answered with a named observation from the real store.
- **SC-003** Any test added is shown failing under its break.

## Assumptions

- The mocked `useGetLayoutQuery` used by most layout tests can express states the real store cannot; the real store (`createApiStore`) is
  available in the same test setup.
- A break is "noticed" only by a failing test, never by a type error (the substitutions compile).

## Dependencies

Independent. Builds on specs 156, 160, 482 and 485.
