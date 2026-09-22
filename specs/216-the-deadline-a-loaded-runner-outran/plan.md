# Plan 216 — The deadline a loaded runner outran

**Spec:** [`spec.md`](./spec.md) · **Issue:** [#2520](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2520) (dup: [#2419](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2419))
**Phase:** 2 — awaiting review
**Phase 4a colour:** **RED for the new guard, CHARACTERISATION (green) for everything else** — spec §"Phase 4a colour"

---

## Bounded context and layers

**None.** This is the first plan in the series with no bounded context, no
aggregate and no layer, and saying so is the decision rather than an omission.

The change lives entirely in the **test harness** of one frontend package (two
if the reviewer opts into US2):

```
apps/management-web/src/test/setup.ts        ← FR-001, FR-004      (US1)
apps/management-web/vite.config.ts           ← FR-002              (US1)
apps/management-web/src/features/overlays/
    OverlayEditorDialogResolvePreview.test.tsx   ← FR-003, one added test (US1)
apps/kiosk-web/src/test/setup.ts + vite.config.ts  ← FR-005        (US2, optional)
```

**`apps/shared` is not in this list, and the reason is a ruling, not an
oversight.** Dropped at the phase-3 gate: no flake has ever been reported there,
and a different test-setup shape is not on its own a reason to change a file with
no observed defect (ADR-0036). Spec §US3 keeps the inspection — `apps/shared` has
no setup file, configures Vitest in `vitest.config.ts`, and runs
`environment: 'node'` with per-file jsdom opt-ins — so that the next author does
not re-derive it, and does not assume the three packages are alike as this plan's
first draft did.

**US2 is optional by the same criterion**, applied consistently: `kiosk-web` has
no reported flake either. What it has is a two-line edit identical to US1's and
ADR-0150's drift argument. That is a preference, so it is the reviewer's call —
spec §US2. **Default: do not ship it.**

Nothing under a `Domain/`, `Application/`, `Infrastructure/` or `Api/` folder is
opened. No C# project is built. No Aspire resource, no migration, no message
contract.

**Consequence for the gates that normally apply:** the ADR-0065 coverage gates
(Domain ≥ 90 %, Application ≥ 80 %, Shared ≥ 90 %) are backend gates and are not
engaged. `NetArchTest` boundary rules are not engaged. `PrimitiveBoundaryTests`
and `HandlerDeconstructionTests` are not engaged. The only CI bucket this can
move is `frontend — lint + typecheck + test`.

---

## Entities and value objects

**None.** No domain model is touched, so constitution §II's primitive ban has no
subject here. Recorded because every other plan in this series has this heading
filled in, and an empty one should read as deliberate rather than forgotten.

The one *value* being chosen is a configuration constant, and its justification
is convergence rather than derivation — see §"The number, and why it is not
invented".

---

## Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` change, no
Wolverine handler. The `V<N>`-suffixed contract discipline (ADR-0040, ADR-0073)
has no subject.

---

## Boundary rules

The one boundary that *is* engaged is the frontend package boundary, and this
change respects it in a way worth stating:

- `apps/shared` is consumed by both `apps/management-web` and `apps/kiosk-web`.
  **Its test setup is not shared** — each package configures Vitest for itself
  and there is no common preset. So the bound is a per-package line, not one
  line in one place.
- **No shared Vitest preset is introduced.** Extracting one to hold a single
  option would be the speculative generality ADR-0036 forbids, and it would
  couple three packages' test configuration for the first time in this repo's
  history in order to save one line. If a fourth shared option ever appears,
  *that* is the moment.
- If US2 ships, the duplication is made safe by the comment FR-004 requires:
  each copy carries the same explanation, so a reader in either package meets
  the reasoning without having to find the other.

No `apps/shared` file imports from either app; no app imports another app. That
is unchanged and untested by this work.

---

## The change, precisely

### 1. `apps/management-web/src/test/setup.ts` — FR-001 + FR-004

Today the file is a single import. After:

```ts
import '@testing-library/jest-dom/vitest';
import { configure } from '@testing-library/react';

// ADR-0150 §1: the deadline is a FAILURE BOUND, not a wait. Testing Library's
// 1000 ms default is not a bound this repository ever chose, and it is the
// whole of #2520/#2419: the resolve-preview error advisory takes 29 ms to
// appear on an idle machine and 319 ms under contention, so twenty polls ran
// out on a loaded runner seven times in a week. 10_000 is the value
// `useSessionExpiry.test.ts` and `CameraViewerCameraSwap.test.tsx` each
// reached independently; a third number would be tuning, which is the habit
// ADR-0150 was written against.
//
// Do NOT reach for `vi.useFakeTimers()` to make a `waitFor` deterministic
// instead. Under Vitest it HANGS: `@testing-library/dom`'s
// `jestFakeTimersAreEnabled()` requires a global `jest`, which Vitest does not
// define, so `waitFor` takes its real-timer branch and schedules its own poll
// and timeout on faked timers nobody advances. Measured: two standard
// `userEvent.setup({ advanceTimers })` configurations, both dying at
// `testTimeout` with no `waitFor` message at all (spec 216 §Claim 7).
configure({ asyncUtilTimeout: 10_000 });
```

The comment is load-bearing, not decoration: FR-004 exists because the
alternative fix is a trap that costs a day to fall into, and nothing else in the
repo says so.

### 2. `apps/management-web/vite.config.ts` — FR-002

```ts
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // A wait allowed 10 s (src/test/setup.ts) that is killed at Vitest's 5 s
    // default has not been fixed — its error message has been changed from
    // "Unable to find an element" to "Test timed out in 5000ms", which names
    // nothing. Measured, not predicted: with the deadline raised and this
    // default left alone, a contended run failed exactly that way (spec 216
    // §Claim 8), and the failing test consumed 4630 ms under contention even
    // while still capped at the old 1000 ms deadline.
    testTimeout: 30_000,
  },
```

**Both halves or neither.** Shipping FR-001 without FR-002 converts the defect
rather than fixing it, and the converted form is harder to diagnose because it
names no element. This was measured in phase 1, not reasoned about: five
contended runs at `asyncUtilTimeout: 10_000` with the 5000 ms default left in
place failed **three times**, every one with `Error: Test timed out in 5000ms.`
— *more* often than the two-in-five the unchanged tree produced in the same
harness (spec §Claim 8).

### 3. `OverlayEditorDialogResolvePreview.test.tsx` — FR-003, one added test

Placed immediately after the existing should-fix-5 test it guards, mirroring the
file's established structure: a docblock naming the issue and the reasoning, then
the test.

Shape (not final text — phase 4a writes it):

```tsx
/**
 * #2520/#2419 guard. The test above is the one CI outran seven times, and the
 * reason is the deadline rather than the assertion: the error advisory does
 * arrive, ~29 ms after the response on an idle machine and ~319 ms under
 * contention, and Testing Library's 1000 ms default is the only thing that
 * ever refused it. This test injects a delay the old default cannot survive,
 * so a future reduction of `asyncUtilTimeout` (src/test/setup.ts) fails the
 * build here instead of on someone else's unrelated pull request.
 *
 * The delay is a single `setTimeout` driving a fake forward — not a fixed-count
 * settle, and not inside a loop, so it is outside ADR-0150 §2's selectors.
 */
it('Still reports a failed resolve when the response is slow enough to outrun the old deadline (#2520)', async () => {
  fetchMock = vi.fn(async () => {
    await new Promise((resolve) => setTimeout(resolve, SLOW_RESPONSE_MS));
    return failedResolveResponse();
  });
  vi.stubGlobal('fetch', fetchMock);
  renderDialog();

  fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: '{{bogus}}' } });

  await waitFor(() => {
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
  await waitFor(() => {
    expect(screen.getByTestId('placeholder-preview-error')).not.toBeNull();
  });
  expect(screen.getByRole('button', { name: /save as draft/i })).toHaveAttribute('aria-disabled', 'false');
});
```

Four deliberate choices in that sketch:

- **`SLOW_RESPONSE_MS` is a named constant, ≥ 1500.** It must exceed the old
  1000 ms default by a clear margin, or the counterfactual in SC-2 becomes a coin
  toss rather than a proof. 1500 is what phase 1 measured red on, every run.
- **No `user.type` of the name field.** The original test types 12 characters
  because it goes on to submit; this guard does not submit, so the 271 ms (idle)
  / 1073 ms (contended) typing leg is cost with no assertion behind it. The
  `aria-disabled` check is kept — it is the one line that keeps this a test about
  the product's "advisory, never blocking" claim (spec 148 decision 3, ADR-0115)
  rather than a test about a timeout.
- **It reuses the file's existing `beforeEach`** (store reset, camera/stream
  mocks, `fetch` stub) rather than introducing a second harness.
- **It is added, not substituted.** The existing should-fix-5 test stays exactly
  as it is. Editing it would forfeit SC-3's characterisation evidence.

**Runtime cost:** ~1.7 s added to a suite that currently runs 334 tests. Stated
so the reviewer can price it; the alternative (an unguarded configuration line)
is the shape ADR-0150 records as having been fixed once already and then
silently undone.

### 4. `apps/kiosk-web` — FR-005 (US2, optional)

The identical two edits (`src/test/setup.ts` + `vite.config.ts`’s `test` block),
identical comment, **no test file touched**.

**Do not start this without the reviewer saying so.** `kiosk-web` has no reported
flake, and the gate’s criterion for dropping `apps/shared` — no observed defect,
no change — applies here identically. US1 closes both issues without it.

### ~~5. `apps/shared`~~ — dropped at the phase-3 gate

Not delivered: no flake has ever been reported in that package (ADR-0036). The
inspection behind the drafted story is kept in spec §US3 rather than here, because
its value is the warning that the packages differ, not the change it proposed.

---

## The number, and why it is not invented

| Measurement | Value | Ratio to 10 000 ms |
|---|---|---|
| `waitFor` line 239, idle | 29 ms | 345× |
| `waitFor` line 239, 24-way contention | 319 ms | 31× |
| The bound that failed in CI | 1000 ms | — |
| `useSessionExpiry.test.ts:178`, chosen independently | 10 000 ms | 1× |
| `CameraViewerCameraSwap.test.tsx`, ×7, chosen independently | 10 000 ms | 1× |

Two authors, two packages, no contact, same number. This plan adopts it rather
than deriving a third, because "derive a number from your own machine's
measurements" is precisely how the 1000 ms default came to be wrong for CI.

---

## Test strategy

### The guard — `OverlayEditorDialogResolvePreview.test.tsx`

One added test (§3). **Red first**, against the unmodified tree, output quoted.
Then green after FR-001. Then **red again** with the bound restored to 1000 ms —
the counterfactual, which is the only thing that distinguishes a guard from a
test that happens to pass.

### Characterisation — every package touched

`pnpm -r --filter "./apps/**" test` captured **before** any edit and again after.
Identical pass counts, no edited assertion. Capture the counts; a summary line is
the artefact.

At HEAD: `apps/management-web` is 334 tests / 38 files. Capture the other two
packages' figures in T001 rather than quoting them here — this plan has not
measured them, and a number nobody read is the defect §IV and the Phase 3 board
gate both drifted on.

### Contention — the only evidence that means anything

Ten runs of the affected file under 24 busy Node processes, before and after.
Before: expect 1–3 failures with CI's exact message (phase 1 observed 2/5 and
2/5 in two separate batches). After: expect zero, and **no `Test timed out`**.

### Lint / typecheck / format

`pnpm lint`, `pnpm typecheck`, `pnpm format:check` at the repo root. The guard
must pass ADR-0150's `no-restricted-syntax` selectors **without** an
`eslint-disable` — it is a single non-looping `await new Promise(...)`, which the
selectors (`ForStatement[test]` / `ForOfStatement` / `ForInStatement` +
`AwaitExpression > NewExpression[callee.name='Promise']`,
`apps/*/eslint.config.js`) cannot match. If phase 4 finds it flagged, that is a
finding about the selector, not a licence to suppress.

### Nothing else

No integration test, no Playwright e2e, no Aspire fixture, no Docker. The e2e
suite under `e2e/` is `*.spec.ts` and outside all of this (ADR-0150 §3 records
that glob gap; it is unchanged here).

---

## Risk register

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| R1 | ~~The reviewer rejects a deadline change outright, per #2419’s written DoD~~ | **Retired 2026-09-22 — answered at the gate** | Raised as a blocking decision with the two facts #2419 lacked (the element does arrive; a fake clock is unavailable under Vitest); the gate ruled *proceed, and close both issues*. Spec §"The objection in #2419" carries the comparison. Kept as a struck row because the objection was reasonable on the evidence it had, and the PR body must reproduce the answer rather than assume it. |
| R2 | 10 000 ms is still not enough, and the flake recurs | Low | The guard (FR-003) and the counterfactual (SC-2) make a shortfall visible. Escalation path is ADR-0150 §Alternatives' CI-budget job, **not** a larger number. Recorded in spec A-1. |
| R3 | A test somewhere asserts a `waitFor` *fails* quickly, and now takes 10 s | Low | SC-4/SC-7's equal-count runs catch a behaviour change; the suite-runtime delta catches a silent slowdown. Capture both packages' wall-clock before and after, not only the counts. |
| R4 | The guard's 1.5 s sleep is itself read as a new fixed-count settle | Medium (as a review finding) | It drives a fake forward and is followed by a `waitFor`, not by a synchronous assertion — the distinction ADR-0150 §2 draws explicitly. The docblock says so at the call site. |
| R5 | `testTimeout: 30_000` masks a genuinely hung test for 30 s | Low | A hung test still fails; it fails later. The `frontend` job's `timeout-minutes: 15` is the outer bound and is untouched. |
| R6 | FR-005 lands and `kiosk-web` goes red for an unrelated reason | Low | US2 is a separate, optional story precisely so it can be dropped without touching US1. If the package is red **before** the change, stop — that is a pre-existing break (MEMORY: `typecheck:e2e` fails on a clean `develop`), not this work. |
| R8 | ~~`apps/shared`'s new `setupFiles` entry breaks its `environment: 'node'` tests~~ | **retired** | US3 was dropped at the gate, so the risk has no subject. Kept as a struck row because the risk is real for whoever picks that work up later. |
| R7 | #2419 is not closed by the merge | Medium | MEMORY: a PR mention auto-closes about 1-in-3. Use a closing keyword **and** verify state after the merge (SC-G). |

---

## Constitution and ADR alignment

| Rule | How this complies |
|---|---|
| **§Testing — "a test waits for a condition, never for a count"** (ADR-0150 §4) | The wait already does, and stays. Only its failure bound changes — which ADR-0150 §1 names as a bound rather than a wait, and expects to be generous. |
| **ADR-0150 §2 — no fixed-count settle before an assertion** | The guard's single `setTimeout` is not in a loop and is followed by a `waitFor`. Outside both shipped selectors; no suppression. |
| **ADR-0139 — new behaviour starts red** | FR-003's guard, observed failing first, output quoted. |
| **§Testing — refactors stay green** | Everything else is characterisation: three suites captured green before and passing unmodified after. An edited assertion blocks. |
| **ADR-0036 — smallest change; no speculative generality** | Two lines per package plus one test. No shared Vitest preset, no dependency bump, no `jest` shim, no lint rule. |
| **ADR-0144 — the lane may not make architectural decisions** | The conflict with #2419's DoD, the scope of US2/US3, and all three fake-clock alternatives were escalated to the gate rather than decided here. The gate answered the first two; the fake-clock options remain untaken and would each want an ADR. |
| **ADR-0109 — `[P]` means disjoint files** | Applied in `tasks.md`, where it marks almost nothing — and the plan says why rather than marking tasks to look parallel. |
| **§IV — latency budget** | N/A; no production path. Stated in the spec rather than left implicit. |
| **ADR-0030 — Conventional Commits** | `test(overlays)` / `chore(frontend)` scopes; each commit builds on its own (ADR-0087 rebase-merge). |

---

## Alternatives considered

**A. Per-site `waitFor(..., { timeout: 10_000 })` at line 239.** Smallest
possible edit, and it works (measured). **Rejected as the primary fix.** It fixes
the unluckiest wait and leaves its five siblings and the other 333 tests on the
default. It is also the precise move ADR-0150 diagnoses: a budget tuned at the
one call site that happened to fail, in a file whose neighbours keep the old
shape — *"the correct idiom was present, adjacent, and documented — and the next
author still reached for the wrong one"*. The per-site annotations already in
`useSessionExpiry.test.ts` and `CameraViewerCameraSwap.test.tsx` are that
prediction already coming true twice.

**B. Convert the test to a fake clock** (`vi.useFakeTimers()` +
`userEvent.setup({ advanceTimers })`). The fix #2419's DoD implies.
**Rejected: it does not work.** Measured twice, both configurations hanging to
`testTimeout` — spec §Claim 7. `@testing-library/dom@10.4.1`'s
`jestFakeTimersAreEnabled()` requires a global `jest` that Vitest does not
define, so `waitFor` schedules its poll on faked timers nobody advances.

**C. Shim `globalThis.jest = vi` in the setup files** to unlock RTL's fake-timer
branch, then convert. Makes B possible. **Rejected for this delivery.** It
changes the branch every `waitFor` in three packages takes — including the two
fake-timer tests already in the affected file, which currently work *because*
`waitFor` is never called inside their fake-timer blocks. That is a
wait-semantics change across the whole frontend; it wants an ADR, and ADR-0144
forbids the lane from writing one.

**D. Upgrade `@testing-library/dom`** to a release with first-class Vitest
fake-timer support. Same objection as C, plus a dependency bump whose blast
radius nobody here has measured. A separate delivery if the gate prefers the
fake-clock direction.

**E. ADR-0150's CI-budget job** — run the frontend suite with deadlines forced
low, permanently. ADR-0150 §Alternatives already weighed and deferred this,
calling it "the honest upgrade path, not a discarded idea". **Not revived here**;
it is the escalation if R2 materialises.

**F. Do nothing; re-run the job.** What has happened seven times. Each re-run
also erases the failure from CI history (MEMORY), so the evidence for a diagnosis
gets harder to collect with every occurrence rather than easier.
