# Feature Specification: The latch the fake clock kept

**Feature Branch**: `fix/2641-resolve-preview-waitfor-timeout` (cut from `origin/develop`)

**Created**: 2026-09-28

**Status**: Draft (Phase 1 gate)

**Input**: Issue [#2641](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2641):
*management-web: OverlayEditorDialogResolvePreview waitFor timeout recurs (was #2419/#2520)*.
Prior attempts: #2419 and #2520 (both closed), spec 216 (the `asyncUtilTimeout` raise and the
`--workspace-concurrency=1` change).

**Spec number.** 288. `origin/develop` tops out at 286; 287 is claimed on an unmerged remote
branch (`287-the-green-that-meant-selected`). **Re-check before opening the PR** (memory: *spec
number: origin/develop isn't enough*).

**ADRs and constitution sections referenced:** ADR-0150 (waiting is a condition, not a count —
§1 the deadline is a failure bound; this spec shows the bound was never the defect), ADR-0075
(one Redux store per app — unchanged; the fix is in the test harness), ADR-0139 and constitution
§Testing (characterisation for preserved behaviour), ADR-0144 (autonomous lane; phase-4a colour),
ADR-0037 (phases), ADR-0036 (smallest change).

**No ADR gap.** Nothing here decides architecture. No production file changes.

---

## What was measured (2026-09-28)

| Fact | Source |
|---|---|
| On `develop` run 36285820840 (SHA `6ff71f12`) the two failing tests took **10 504 ms** and **10 374 ms**: the whole 10 000 ms `asyncUtilTimeout` plus render. The element did not arrive *late*; it **never arrived**. | job 108526280037 log, `OverlayEditorDialogResolvePreview.test.tsx:244:11` and `:294:11` |
| In that run the dialog **stopped re-rendering** ~0.4 s into each failing test: the `AuthProvider context is undefined` warning that every render of `OverlayEditorDialog` emits stops at `01:33:56.98` (test fails at ~`01:34:07`) and at `01:34:07.35` (fails at ~`01:34:17`). The slow-response test should have re-rendered 1.5 s after its fetch; it never did. | same log, stderr blocks |
| Both failures are the **last two tests in the file**, and the only two after the one test that installs fake timers **mid-test, after real RTK Query work** (*"Never flags a second, still-settling placeholder…"*, `:193-225`). Every failure on record (#2419 ×5, #2520 ×2, #2641 ×3, PR #2664) is one of these two. | test file order; issue histories |
| RTK 2.12 `configureStore` installs `autoBatchEnhancer({type:'raf'})`. Its `notificationQueued` flag is a **latch**: an auto-batched dispatch sets it and schedules `notifyListeners` via `requestAnimationFrame` + a 100 ms `setTimeout` fallback, **read from the globals at dispatch time**; only that callback clears it. While it is set, every further auto-batched dispatch updates state but **notifies no subscriber**. | `@reduxjs/toolkit/dist/redux-toolkit.modern.mjs:475-534` |
| Query `pending`/`fulfilled`/`rejected` carry `SHOULD_AUTOBATCH` (`addShouldAutoBatch`), as do `subscriptionsUpdated`, `removeQueryResult`, `queryResultPatched`. | `dist/query/rtk-query.modern.mjs:770-775,1352,1425,1661-1665` |
| RTK Query's subscription middleware schedules `subscriptionsUpdated` on a **real `setTimeout(…, 500)`** from the query's `pending`. | `dist/query/rtk-query.modern.mjs:2165-2172` |
| Vitest 4.1.11 fakes every timer except `nextTick`/`queueMicrotask` — **including `requestAnimationFrame`**. `vi.useRealTimers()` discards pending fake callbacks. | `vitest/dist/chunks/test.DNmyFkvJ.js:3299` |
| The store is the **app singleton**, shared by all six tests; `beforeEach` resets its RTK Query *cache* (`resetApiState`) but cannot reach the enhancer's closure. | test file `:12`, `:99-103` |

### The mechanism

1. *"Never flags…"* types `{{temperature}}`; the settled query's `pending` schedules the **real**
   500 ms `subscriptionsUpdated` timer.
2. It waits for the resolve, then calls `vi.useFakeTimers()` and works inside a fake window.
3. **If real time inside that window crosses the 500 ms mark** — which happens only when the
   first resolve and the window itself are slow, i.e. on a contended runner — the timer fires,
   `subscriptionsUpdated` is dispatched, and the enhancer queues `notifyListeners` on the
   **fake** `requestAnimationFrame` and **fake** `setTimeout`.
4. `vi.useRealTimers()` in `finally` drops those callbacks. `notificationQueued` stays `true`
   for the life of the singleton store.
5. The next two tests dispatch `pending` and then `rejected`. Both are auto-batched; neither
   notifies. The dialog never re-renders into the error state; `waitFor` burns its whole
   10 s deadline.

The two tests before *"Never flags…"* cannot be hit (they run before the latch exists);
*"Never flags…"* itself asserts nothing store-driven after its window. That is why exactly two
tests fail, always together, always these two.

### Reproduced locally, deterministically (2026-09-28)

A scratch copy of the test file with **one line** added inside *"Never flags…"*' fake window,
immediately before `vi.useRealTimers()` — a real 600 ms wait via a `setTimeout` captured before
faking, i.e. "this window was slow" — fails exactly the CI way, every run:

```
 × Never blocks submission on a failed resolve: … 10608ms
 × Still reports a failed resolve when the response is slow enough to outrun the old deadline (#2520) 10361ms
      Tests  2 failed | 4 passed (6)
```

The same copy with the store swapped for a fresh one per test: `Tests 6 passed (6)`, three runs
of three. The unmodified file passes locally. The scratch file was deleted; it is not part of
this change.

### Why the earlier fixes could not work

- **#2520 / spec 216 raised `asyncUtilTimeout` 1 000 → 10 000 ms.** That treats a *late* element.
  This element never comes. The raise did change the symptom — from ~1 s to ~10.4 s — which is
  the tell.
- **`--workspace-concurrency=1`** reduced runner load, so the 500 ms mark lands in the fake
  window less often. It made the race rarer, not impossible, and its comment in `setup.ts`
  ("3 of 20 contended runs still outran it") is the residue.
- The #2520 guard test's own comment (*"the reason is the deadline rather than the assertion"*)
  records the wrong cause, and the guard itself is a victim: it runs after the latch.

---

## User Scenarios & Testing

### User Story 1 — A slow runner cannot silence the store for the rest of the file (Priority: P1)

As a maintainer, I want `OverlayEditorDialogResolvePreview.test.tsx`'s tests not to share a
Redux store, so that a fake-timer window in one test cannot swallow store notifications in the
tests after it, however slowly the runner executes.

**Why this priority**: it is the whole issue. Ten recorded CI failures, including on `develop`'s
own head.

**Independent Test**: run the counterfactual in *Verification* below — the stall-injected harness
is red against today's file and green against the fixed one — plus 20 consecutive clean runs of
the fixed file.

**Acceptance Scenarios**:

1. **Given** the fixed test file, **When** it runs unmodified, **Then** all six tests pass, with
   every `expect(...)` line byte-identical to today's.
2. **Given** the fixed file with a real ≥ 600 ms stall injected immediately before
   `vi.useRealTimers()` in *"Never flags…"* (the CI-slow window), **When** it runs, **Then** all six
   tests pass — the latch, if it forms, dies with that test's store.
3. **Given** today's file with the same stall, **When** it runs, **Then** exactly the two tests
   fail with `Unable to find an element by: [data-testid="placeholder-preview-error"]` after
   ~10 s (the red half of the counterfactual; observed, not assumed).
4. **Given** the full `management-web` suite, **When** it runs, **Then** it is green.

*Conflict / bad-request / auth scenarios:* not applicable — no endpoint, command or
authorisation surface is touched.

### Edge Cases

- **The latch still forms inside *"Never flags…"*.** Accepted: that test asserts nothing
  store-driven after its window, and a fresh store per test bounds the damage to the test that
  created it. Preventing the latch *inside* a test would mean draining fake timers before
  `useRealTimers()`, which fires the pending debounce and starts a new query under the fake
  clock — the same hazard moved, not removed.
- **Sibling files with the same shape** — `OverlayEditorDialog.test.tsx`,
  `CameraViewerLifecycle.test.tsx`, `CameraViewerAlignment.test.tsx` import the app singleton
  store and use `vi.useFakeTimers()`. Not investigated here and **out of scope** (smallest
  change); named so a follow-up issue can check each one.

## Requirements

- **FR-001**: Each test in `OverlayEditorDialogResolvePreview.test.tsx` MUST render into a
  Redux store created for that test, mirroring the existing `createStore()` pattern in
  `OverlayEditorDialogChainRetention.test.tsx:96-101` (only the RTK Query APIs the dialog's
  un-mocked hooks need).
- **FR-002**: No `expect(...)` assertion in the file may change. `resetApiState` in `beforeEach`
  is removed only because it becomes redundant.
- **FR-003**: No production file changes. `src/app/store.ts` (ADR-0075's singleton) is untouched.
- **FR-004**: Comments that record the wrong cause are corrected: the file header's "the store is
  the app's real singleton" note, and the #2520 guard's "the reason is the deadline". `setup.ts`'s
  `asyncUtilTimeout: 10_000` stays (ADR-0150 §1: a failure bound), with its causal paragraph
  corrected to name this mechanism. Comment-only there.

## Success Criteria (definition of done)

- **SC-001 (counterfactual, red)**: the stall-injected copy of **today's** file fails exactly the
  two tests with the CI signature — output quoted verbatim in the PR.
- **SC-002 (counterfactual, green)**: the same stall injected into the **fixed** file passes 6/6
  in **10 of 10** runs — output quoted.
- **SC-003 (no regression)**: the fixed, unstalled file passes 6/6 in **20 consecutive** runs, and
  the full `management-web` Vitest suite is green.
- **SC-004 (characterisation)**: `git diff` of the test file shows no changed or removed
  `expect(` line.

"It passed once on CI" is not a criterion: the race needed contention to show, which is exactly
why SC-001/SC-002 inject the slow window instead of waiting for one.

## Latency budget impact

N/A — test harness only; no leg of constitution §IV is touched.

## Assumptions

- The 500 ms `subscriptionsUpdated` timer is the natural trigger. The stall reproduction proves
  *a* real-scheduled auto-batched dispatch landing in the fake window latches the store; it does
  not rule out another auto-batched action being the one on a given CI run. The fix does not
  depend on which one it is — it removes the cross-test channel.
