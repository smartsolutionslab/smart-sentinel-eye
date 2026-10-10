# Feature Specification: The latch three files still shared

**Feature Branch**: `fix/2670-camera-overlay-store-isolation` (cut from `origin/develop` at `540236cf`)

**Created**: 2026-10-10

**Status**: Draft (Phase 1 gate)

**Input**: Issue [#2670](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2670):
*OverlayEditorDialog and camera-viewer tests share the singleton-store-plus-fake-timers shape that
caused #2641*. Split from #2641 / spec 290 (`specs/290-the-latch-the-fake-clock-kept/`), whose
own *Follow-up* section named these three files.

**Scope decision (user, 2026-10-08, on the issue):** one fix — per-test store isolation, mirroring
#2641's fix. The separate jsdom rAF frame-loop-death hazard the issue also describes is **out of
scope** and is not revisited here.

**Spec number.** 329. `origin/develop` tops out at 327; 328 is claimed by open PR #2815
(`fix/2629-webhook-revoke-retry-policy`, `specs/328-the-outage-the-revoke-must-outlast`). No
remote branch carries a `specs/329-*` directory (all remote branches checked 2026-10-10).
**Re-check before opening the PR** (memory: *spec number: origin/develop isn't enough*).

**ADRs and constitution sections referenced:** ADR-0075 (one Redux store per app — unchanged; the
fix is in the test harness), ADR-0139 and constitution §Testing (characterisation for preserved
behaviour), ADR-0144 (autonomous lane; phase-4a colour), ADR-0150 (no real sleep in a committed
test — why the counterfactual probe stays uncommitted), ADR-0037 (phases), ADR-0036 (smallest
change).

**No ADR gap.** The pattern is spec 290's, already accepted and merged (`1d536690`). No
production file changes.

---

## The issue's premise, re-checked against `develop` (2026-10-10)

The issue was filed on 2026-10-08. **None of the three paths it names exists any more**; the
files moved, and one of them changed shape:

| Issue names | Today | What changed |
|---|---|---|
| `management-web/src/features/overlays/OverlayEditorDialog.test.tsx` | `apps/management-web/src/features/overlays/OverlayDraftForm.test.tsx` (860 lines) | Renamed by `d0e7f1b0` (#2350, editor dialog deleted). Still imports the **app singleton** `store` from `../../app/store.js` (line 6). |
| `management-web/src/features/cameras/CameraViewerLifecycle.test.tsx` | `apps/management-cameras/src/features/cameras/CameraViewerLifecycle.test.tsx` (109 lines) | Moved into the federated remote by `07066da1` (spec 316). No longer the app singleton: a **module-scope** `const store = createApiStore([camerasApi, streamsApi])` (line 9) — still one store shared by every test in the file. |
| `management-web/src/features/cameras/CameraViewerAlignment.test.tsx` | `apps/management-cameras/src/features/cameras/CameraViewerAlignment.test.tsx` (120 lines) | Same move, same module-scope `createApiStore` (line 10). |

**The issue's claim about fake-timer placement is stale for the overlay file.** It said
`beforeEach`/`afterEach` at line ~556 make *every* test fake-clocked. Today only the last
`describe` — *Frame capture (spec 147)*, three tests, `beforeEach` at line 727-764 with
`vi.useFakeTimers()` at line 730, `afterEach` at 766-770 — runs on the fake clock. The other 18
tests run real-clocked and before it in file order.

Fake-timer placement in the camera files:

- **Lifecycle** — only test 2 (*"Reconnects automatically…"*, line 74) calls
  `vi.useFakeTimers()`, inside the test; file-level `afterEach` restores real timers (line 47-49).
  Test 3 runs after it on the same store.
- **Alignment** — `beforeEach` (line 73-77) fakes timers for both tests; `afterEach` (79-81)
  restores.

**The shape is the same in all three, and it is the shape that matters**: one store instance
shared across the tests of a file, plus fake-timer windows ended by `vi.useRealTimers()`. Whether
that one store is the app singleton or a module-scope `createApiStore` is irrelevant to the latch,
which lives in the store's closure (spec 290, *The mechanism*).

### Completeness — the full set, measured

A grep of every `*.test.ts(x)` under `apps/` that calls `useFakeTimers` **and** builds or imports
a store (`app/store`, `createApiStore(`, `configureStore(`, `Provider store`) returns six files:

| File | Store | In scope |
|---|---|---|
| `apps/management-web/src/features/overlays/OverlayDraftForm.test.tsx` | app singleton | **yes** |
| `apps/management-cameras/src/features/cameras/CameraViewerLifecycle.test.tsx` | module-scope | **yes** |
| `apps/management-cameras/src/features/cameras/CameraViewerAlignment.test.tsx` | module-scope | **yes** |
| `apps/management-web/src/features/overlays/OverlayDraftFormResolvePreview.test.tsx` | per-test (spec 290's fix, renamed) | already fixed |
| `apps/kiosk-web/src/features/cell/CellPage.test.tsx` | kiosk app singleton (line 14) | **no — see Edge Cases** |
| `apps/kiosk-web/src/features/cell/LayoutGridLabelPairing.test.tsx` | kiosk app singleton (line 7) | **no — see Edge Cases** |

The issue's own grep was scoped to `apps/management-web`, so it could not see the kiosk pair, and
the camera files have since left that directory.

### The latch reproduces in all three, deterministically (architect probe, 2026-10-10)

RTK 2.12's `autoBatchEnhancer` (`@reduxjs/toolkit/dist/redux-toolkit.modern.mjs:475-534`) sets
`notificationQueued` on an auto-batched dispatch and clears it only from the queued
`notifyListeners` callback (rAF + 100 ms `setTimeout` fallback). Vitest 4.1.11 fakes both. A
dispatch under the fake clock whose callback `vi.useRealTimers()` then discards leaves the latch
set for the life of that store.

An **uncommitted** scratch copy (`ZzLatchProbe.test.tsx`, same directory) of each file, with two
additions — one auto-batched `store.dispatch` at the end of the first fake-clocked test, and an
appended last test that subscribes a listener, makes one auto-batched dispatch on real time, waits
250 ms and expects the listener called — gave:

| Probe target | Result |
|---|---|
| `CameraViewerLifecycle` (today) | `× PROBE: a later test still receives store notifications` — `AssertionError: expected "vi.fn()" to be called at least once` — `Tests 1 failed \| 3 passed (4)` |
| `CameraViewerLifecycle` (today), **control**: injected dispatch removed | `Tests 4 passed (4)` |
| `CameraViewerLifecycle` with a store created per test in `beforeEach`, injected dispatch kept | `Tests 4 passed (4)`, three runs of three |
| `CameraViewerAlignment` (today) | same assertion — `Tests 1 failed \| 2 passed (3)` |
| `OverlayDraftForm` (today) | same assertion — `Tests 1 failed \| 21 passed (22)` |

The control shows the probe can pass, so its red is the latch and not a broken probe. The
unmodified files pass today: `CameraViewerLifecycle` + `CameraViewerAlignment` `Tests 5 passed
(5)`, `OverlayDraftForm` `Tests 21 passed (21)`. **No CI failure is on record for any of the
three** — this fix is preventive, removing a channel proven to exist, not a response to an
observed flake. The probe files were deleted; the worktree is clean.

---

## User Scenarios & Testing

### User Story 1 — A fake-timer window in one test cannot silence the store for the tests after it (Priority: P1)

As a maintainer, I want each test in `OverlayDraftForm.test.tsx`, `CameraViewerLifecycle.test.tsx`
and `CameraViewerAlignment.test.tsx` to render into a store of its own, so that an auto-batched
dispatch stranded on a discarded fake clock in one test cannot stop store notifications reaching
the tests after it — the failure that cost #2641 ten CI runs.

**Why this priority**: it is the whole issue.

**Independent Test**: the counterfactual in *Success Criteria* — the probe is red against each
file today and green against each fixed file — plus 20 consecutive clean runs of each fixed file.

**Acceptance Scenarios**:

1. **Given** each fixed file, **When** it runs unmodified, **Then** every test passes
   (`OverlayDraftForm` 21/21, `CameraViewerLifecycle` 3/3, `CameraViewerAlignment` 2/2), with
   every `expect(` line byte-identical to today's.
2. **Given** each fixed file with the latch probe applied (auto-batched dispatch stranded in the
   first fake-clocked test; appended test asserting a later subscriber is notified), **When** it
   runs, **Then** every test, the probe included, passes — the latch, if it forms, dies with that
   test's store.
3. **Given** each file **as on `develop` today** with the same probe, **When** it runs, **Then**
   exactly the probe test fails with `expected "vi.fn()" to be called at least once` (the red half;
   observed by the architect above and re-observed in phase 4a, not assumed).
4. **Given** the full `management-web` and `management-cameras` Vitest suites, **When** each runs,
   **Then** it is green.

*Conflict / bad-request / auth scenarios:* not applicable — no endpoint, command or authorisation
surface is touched.

### Edge Cases

- **The store must be built while real timers are active.** RTK captures
  `window.requestAnimationFrame` once, at store creation
  (`createRafWithFallbackTimer(window.requestAnimationFrame, 100)`, evaluated in the enhancer, not
  per dispatch). Today every store in scope is built at module load on real timers. A per-test
  store built *after* `vi.useFakeTimers()` would capture the fake rAF — a different notification
  path during the test, i.e. a behaviour change, not a harness move. FR-002 forbids it.
- **The latch can still form inside a fake-clocked test.** Accepted, as in spec 290: the fresh
  store bounds the damage to the test that created it. Draining timers before `useRealTimers()`
  instead would fire pending work under the fake clock — the hazard moved, not removed.
- **Kiosk-web has the same shape** — `CellPage.test.tsx` and `LayoutGridLabelPairing.test.tsx`
  import the kiosk app singleton and use `vi.useFakeTimers()`. Not probed here and **out of scope**
  (the user's decision named three management files; smallest change). The orchestrator should
  file a follow-up issue naming both, citing this spec's probe recipe.
- **The jsdom rAF frame-loop-death hazard** (issue body, second section) — out of scope by the
  user's decision of 2026-10-08. Not addressed, not re-litigated.

## Requirements

- **FR-001**: Each test in the three files MUST render into a Redux store created for that test,
  using the existing `createApiStore` (`apps/shared/src/store/createApiStore.ts`) — the same
  construction each file uses today, so the slice set and middleware are unchanged:
  `createApiStore(apiSlices)` for `OverlayDraftForm` (the exact slice array behind the app
  singleton, exported from `apps/management-web/src/app/store.ts`), and
  `createApiStore([camerasApi, streamsApi])` for the two camera files.
- **FR-002**: Each per-test store MUST be created while real timers are active — before any
  `vi.useFakeTimers()` that applies to the same test.
- **FR-003**: No `expect(` line in any of the three files may change, be added or be removed.
- **FR-004**: No production file changes. `apps/management-web/src/app/store.ts` (ADR-0075's
  singleton) and `createApiStore.ts` are untouched.
- **FR-005**: The one-line store comment each file carries is replaced with the reason the store is
  per test. No spec, task or issue reference in the comment (house rule; cf. `540236cf`).

## Success Criteria (definition of done)

- **SC-001 (counterfactual, red)**: the latch probe applied to each file **as on `develop`** fails
  exactly the probe test with the assertion above — output quoted verbatim in the PR, all three
  files.
- **SC-002 (counterfactual, green)**: the same probe applied to each **fixed** file passes every
  test in **10 of 10** runs per file — output quoted.
- **SC-003 (no regression)**: each fixed, unprobed file passes in **20 consecutive** runs, and the
  full `management-web` and `management-cameras` Vitest suites are green, as are `prettier --check`,
  ESLint and `tsc --noEmit` for both packages.
- **SC-004 (characterisation)**: `git diff` of the three files shows no added, changed or removed
  `expect(` line; the probe files are absent from the diff.

## Latency budget impact

N/A — test harness only; no leg of constitution §IV is touched.

## Assumptions

- The probe's injected dispatch stands in for whatever auto-batched action might, on a slow runner,
  land in a fake window naturally (spec 290's was RTK Query's real 500 ms `subscriptionsUpdated`).
  Which action it would be in these files is not established and does not matter: the fix removes
  the cross-test channel regardless of the trigger.
