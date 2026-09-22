# Plan 216 — The deadline a loaded runner outran

**Spec:** [`spec.md`](./spec.md) · **Issue:** [#2520](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2520) (dup: [#2419](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2419))
**Phase:** 2 — **re-planned 2026-09-22 after R2 materialised in phase 4.** Awaiting review of §"R2 materialised".
**Phase 4a colour:** **RED for the new guard, CHARACTERISATION (green) for everything else** — spec §"Phase 4a colour"

> **Read §"R2 materialised" before anything else in this document.** Phase 4 ran
> T001–T007; T007, this plan's own pre-declared R2 check, **did not pass its own
> gate** — 3 of 20 contended runs still reproduce the target defect with the fix
> applied. The T004/T005 work **stands and ships**; it is now recorded as
> necessary and not sufficient, and a second mechanism (T014–T017) joins it.

---

## Bounded context and layers

**None.** This is the first plan in the series with no bounded context, no
aggregate and no layer, and saying so is the decision rather than an omission.

The change lives entirely in the **test harness** of one frontend package (two
if the reviewer opts into US2) — plus, since the re-plan, **one line of the
workspace root's test script**:

```
apps/management-web/src/test/setup.ts        ← FR-001, FR-004      (US1)
apps/management-web/vite.config.ts           ← FR-002              (US1)
apps/management-web/src/features/overlays/
    OverlayEditorDialogResolvePreview.test.tsx   ← FR-003, one added test (US1)
package.json  (workspace root, `test` script)      ← FR-006        (US1b, re-plan)
apps/kiosk-web/src/test/setup.ts + vite.config.ts  ← FR-005        (US2, optional)
```

**The root `package.json` is a shared file, and putting it on this list is a
scope judgement, not a drift.** It is argued in full at §"R2 materialised" §7,
in both directions, and it is the gate's to overturn.

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

**Runtime cost:** ~1.7 s added to a suite that currently runs 329 tests. Stated
so the reviewer can price it; the alternative (an unguarded configuration line)
is the shape ADR-0150 records as having been fixed once already and then
silently undone.

### 3b. `package.json` (workspace root) — FR-006, added by the re-plan

```json
-  "test": "pnpm -r --filter \"./apps/**\" test && pnpm test:guards",
+  "test": "pnpm -r --workspace-concurrency=1 --filter \"./apps/**\" test && pnpm test:guards",
```

One token. It stops `apps/kiosk-web`'s and `apps/management-web`'s Vitest
processes — each with its own fork pool sized from the machine's core count —
running on one CI runner at the same time. That overlap is **observed in the CI
log, not inferred**: run 35725455764's `frontend` job shows both suites starting
within a second of each other, with `kiosk-web`'s entire 18 s inside
`management-web`'s 44 s.

It goes in `package.json` rather than as a CI-only flag or an
`NPM_CONFIG_WORKSPACE_CONCURRENCY` job env deliberately: **a CI-only scheduling
setting is invisible to the person trying to reproduce the failure locally**, and
this defect has already cost a day to an environment difference nobody could see.
Local `pnpm test` gets ≈ 17 s slower and matches what CI does.

**The comment that goes with it** must say *why* — that the concurrency is what
starved the runner, and that the number is a resource decision rather than a
performance one — or the next person optimising CI removes it. Same obligation
as FR-004's comment, same reason.

**Cost, priced from the CI figures rather than estimated:** 16.49 + 17.99 + 44.40
≈ 79 s serial against ≈ 62 s overlapped. The `frontend` job completes in 2 m 04 s
against `timeout-minutes: 15`.

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

At HEAD, from CI run
[35725455764](https://github.com/smartsolutionslab/smart-sentinel-eye/actions/runs/35725455764)
(green, `develop`, 2026-09-22) — **read off the job log, not taken on trust**:

| Package | Files | Tests | Vitest-reported duration |
|---|---|---|---|
| `apps/shared` | 33 | **414** | 16.49 s |
| `apps/kiosk-web` | 12 | **168** | 17.99 s |
| `apps/management-web` | **38** | **329** | 44.40 s |

**`management-web` is 329, not the 334 this plan asserted through two
revisions.** Phase 4's T002 measured 329 twice; CI's own log says 329; the static
count of `it(` / `test(` call sites in the package is 313, which `it.each`
expansion carries the rest of the way. Three independent readings, one number,
and 334 was never one of them — it was carried from a phase-1 draft and never
re-read. Corrected here rather than deferred, because a baseline nobody checked
is precisely the defect §IV, §II and the Phase 3 board gate each drifted on.

T002 still re-measures locally — a CI figure and a local figure are different
observations — but it now has all three packages' CI figures to compare against
instead of a blank.

### Contention — the only evidence that means anything

Ten runs of the affected file under load, before and after. Before: phase 1
observed 2/5 and 2/5 in two batches at 24-way contention; phase 4 observed 4/10
in the same harness. After: the bar is **zero, and no `Test timed out`**.

**Phase 4 did not clear that bar.** See §"R2 materialised" below — the harness,
the numbers, what they mean and what changes as a result.

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
| R2 | 10 000 ms is still not enough, and the flake recurs | ~~Low~~ — **MATERIALISED 2026-09-22, measured twice** | The escalation is no longer a plan; it is the delivery. **3 of 20 contended runs still reproduce the exact target failure with the fix applied** (down from 4/10 unfixed). §"R2 materialised" below carries both batches' raw numbers, the diagnosis they force, and the mechanism that replaces "a larger number". Spec A-1 is **falsified** and marked as such. |
| R3 | A test somewhere asserts a `waitFor` *fails* quickly, and now takes 10 s | Low | SC-4/SC-7's equal-count runs catch a behaviour change; the suite-runtime delta catches a silent slowdown. Capture both packages' wall-clock before and after, not only the counts. |
| R4 | The guard's 1.5 s sleep is itself read as a new fixed-count settle | Medium (as a review finding) | It drives a fake forward and is followed by a `waitFor`, not by a synchronous assertion — the distinction ADR-0150 §2 draws explicitly. The docblock says so at the call site. |
| R5 | `testTimeout: 30_000` masks a genuinely hung test for 30 s | Low | A hung test still fails; it fails later. The `frontend` job's `timeout-minutes: 15` is the outer bound and is untouched. |
| R6 | FR-005 lands and `kiosk-web` goes red for an unrelated reason | Low | US2 is a separate, optional story precisely so it can be dropped without touching US1. If the package is red **before** the change, stop — that is a pre-existing break (MEMORY: `typecheck:e2e` fails on a clean `develop`), not this work. |
| R8 | ~~`apps/shared`'s new `setupFiles` entry breaks its `environment: 'node'` tests~~ | **retired** | US3 was dropped at the gate, so the risk has no subject. Kept as a struck row because the risk is real for whoever picks that work up later. |
| R7 | #2419 is not closed by the merge | Medium | MEMORY: a PR mention auto-closes about 1-in-3. Use a closing keyword **and** verify state after the merge (SC-G). |

---

## R2 materialised

**Written 2026-09-22, after phase 4 ran T001–T007 and T007 failed its own
pre-declared gate.** This section is the re-plan. It is long because the
temptation here is to accept a 40 %→15 % improvement and move on, and the only
defence against that is the evidence in full rather than a summary of it.

### 1. What T007 measured, in full

T007 is this plan's own R2 risk check: ten runs of
`OverlayEditorDialogResolvePreview.test.tsx` under genuine CPU saturation (24
busy Node processes on 8 logical cores), with the T004/T005 diff applied. The
bar this plan set for it, verbatim, was:

> *"With T004/T005 applied, run it **ten times**. Expect zero failures **and no
> `Test timed out`**."* — tasks.md T007 step 2
>
> *"If step 2 shows any failure: stop and report. A partial cure is spec A-1
> failing, and the escalation is ADR-0150's CI-budget job, **not** a larger
> number."* — tasks.md T007

**Before the fix** (unchanged tree, same harness): **4 failures in 10**, each
`TestingLibraryElementError: Unable to find an element by:
[data-testid="placeholder-preview-error"]`. Phase 1 had separately observed 2/5
and 2/5 in two earlier batches — 8/20 across all three before-batches.

**After the fix, batch 1 (10 runs):**

| Outcome | Count | Detail |
|---|---|---|
| Clean pass | 7 | — |
| **Target defect reproduced** | **1** | Run 6. **Both** the pre-existing should-fix-5 test **and** the new #2520 guard failed, same message, same `[data-testid="placeholder-preview-error"]`, at **10371 ms and 10630 ms** — i.e. just past the new 10 000 ms bound. |
| `[vitest-pool-runner]: Timeout waiting for worker to respond` | 2 | Vitest's own fork-worker liveness timeout. **Never seen in any before-batch.** Not an `asyncUtilTimeout` failure and not a `testTimeout` failure — the pool gave up on a worker process. |

**After the fix, batch 2 (10 runs)** — ordered specifically to rule out the
first-run-after-machine-churn artefact this repo has been bitten by before
(MEMORY: *measurement runs need repeating*):

| Outcome | Count | Detail |
|---|---|---|
| Clean pass | 8 | — |
| **Target defect reproduced** | **2** | Runs 1 and 6. Identical signature to batch 1's run 6 — same test, same element, **10.4 s to 14.5 s**. |
| Worker-response timeout | 0 | The batch-1 noise did not repeat. |

**Combined, after the fix: 3 genuine recurrences in 20 contended runs (15 %),
against 4 in 10 (40 %) unfixed.**

The diff was verified untouched between and after the batches — `git diff --stat`
shows **63 insertions across exactly the three expected files, additive only**, no
stash, nothing else modified.

### 2. What those numbers actually say, which is not "nearly there"

A 40 %→15 % reduction is real and it is worth having. It is also **not the
result the model predicted**, and the gap matters more than the improvement.

The model behind `asyncUtilTimeout: 10_000` was proportional slowdown: the
advisory costs **29 ms idle** and **319 ms under 24-way contention** — an 11×
inflation — so a bound of 10 000 ms carries **345× the idle cost** and **31× the
contended cost**, and was declared ample on that arithmetic (§"The number").

For the observed failures to happen, the advisory must have taken **longer than
10 000 ms** — an inflation of **345× or more over idle**, thirty times worse than
the 11× the same harness produced when it was measured. **That is not the same
mechanism scaled up.** Proportional slowdown does not produce a 345× tail from an
11× median. Something is stalling the work outright for seconds at a time rather
than merely slowing it down.

**The two worker-response timeouts are the independent corroboration.** They are
a *different instrument* reporting the *same* condition: Vitest's pool decided a
fork had stopped answering at all. A starved process and a slow process are
distinguishable, and the harness produced the starved kind — under exactly the
load where the target test also fails, and never under lighter load.

**The conclusion this forces:** the failing wait needs **CPU**, not **clock**.
The work is pure CPU-bound render-and-diff with no timer floor (spec §Claim 3);
a process that is not scheduled makes no progress no matter how generous its
deadline. Raising the deadline further — 30 s, 60 s — would buy tail coverage at
the cost of turning every genuine failure into a minutes-long hang, which is
precisely the tuning habit ADR-0150 was written against, and which T007's own
instruction forbids in the same sentence that sent this back to phase 2.

### 3. "ADR-0150's CI-budget job" does not mean what R2 used it to mean

R2 and spec A-1 both name *"ADR-0150 §Alternatives' CI-budget job"* as the
escalation. **Read against the ADR, that citation is a category error, and it is
corrected here rather than carried forward.**

ADR-0150 §Alternatives' job is:

> *"Run the suite in CI at a reduced settle budget, permanently — a job that
> executes the frontend tests with every fixed-count settle forced to 1 … it
> catches the defect *class* by behaviour rather than the *shape* by syntax"*

That job **tightens** budgets in order to *detect* fixed-count-settle defects. It
is a detector for a defect class this spec does not have — #2520 is not a
fixed-count settle, it is a `waitFor` whose bound was outrun. Adopting it here
would make this flake **more** frequent, not less. The phrase was reached for
because it was the nearest thing in the ADR to "escalate to CI"; nobody checked
what it did.

So R2's escalation has to be named from scratch, grounded in this repo's actual
CI, which is what §4 does. **This is not an ADR amendment and does not need one**
— ADR-0150's alternative stays exactly as written and unadopted; what changes is
this plan's wrong reference to it.

### 4. The escalation, grounded in the CI this repo actually runs

`.github/workflows/ci.yml` has **one** `frontend` job — `frontend — lint +
typecheck + test`, `runs-on: ubuntu-latest`, `timeout-minutes: 15` — whose whole
test step is `run: pnpm test`. The root script is:

```json
"test": "pnpm -r --filter \"./apps/**\" test && pnpm test:guards"
```

`pnpm -r` runs workspace packages **concurrently** (its `--workspace-concurrency`
default is greater than one), in topological order. **The CI log proves this is
not theoretical.** From run 35725455764's `frontend` job:

| Package | Vitest-reported duration | Wall-clock window (from the log timestamps) |
|---|---|---|
| `apps/shared` | 16.49 s | ends 12:10:01 |
| `apps/kiosk-web` | 17.99 s | ends 12:10:20 → started ≈ 12:10:02 |
| `apps/management-web` | 44.40 s | ends 12:10:46 → started ≈ 12:10:02 |

`shared` is a workspace dependency of both apps, so it runs first, alone. Then
**`kiosk-web` and `management-web` run at the same time** — the two start within
a second of each other and `kiosk-web`'s entire 18 s sits inside
`management-web`'s 44 s. Roughly **40 % of the management-web suite's runtime is
contended by a sibling package's suite**, on the same runner, every single CI
run.

And each of those two Vitest processes sizes its own fork pool from the machine:
`getDefaultThreadsCount` in the pinned `vitest@4.1.11` returns
`max(availableParallelism() - 1, 1)` for a non-watch run. So the runner carries
**two main processes plus both packages' full fork pools simultaneously**.

**The frontend bucket oversubscribes its own runner, by construction, and always
has.** That is the CPU starvation the §2 diagnosis points at — not an unlucky
GitHub neighbour, not an unbounded race, but a scheduling decision in this
repository's own root `package.json`.

**Decision — the escalation is to stop the frontend bucket contending with
itself, not to add a job.** Concretely, and in this order of preference:

1. **Serialise the workspace test run** (T014/T015 below): the root `test` script
   becomes `pnpm -r --workspace-concurrency=1 --filter "./apps/**" test`. One
   token. It removes the only source of contention this repo controls, it is
   visible in the same file a developer runs locally, and the CI cost is bounded
   by the figures above — the three suites total ≈ 79 s serial against ≈ 62 s
   overlapped, so **≈ +17 s on a `frontend` job that currently completes in 2 m
   04 s against a 15-minute limit**. There is no budget question here.

2. **Do not cap Vitest's internal fork pool.** `availableParallelism() - 1`
   workers plus one main is not oversubscription *within* a package; it only
   becomes one when a second package runs beside it. Capping it would slow every
   suite to fix a problem (1) already removes. Smallest change, ADR-0036.

3. **Do not add a separate job, and do not exclude the file from the main run.**
   Both were considered (§"Alternatives", E2 and E3). A per-file exclusion list
   is a thing that rots silently — the file would stop being covered by the
   ordinary run and nobody would notice; a second job costs a runner, a second
   `pnpm install`, and a new bucket in the four-bucket manual read that is this
   repo's only CI gate (MEMORY: *`develop` has no required status checks*). Both
   are **the next escalation if (1) does not reach zero**, and both are broad
   enough to want their own issue — see §6.

**Nothing here is adopted on the argument alone.** T016 re-runs the contended
reproduction, and T014 first makes that harness mean something — see §5.

### 5. The harness has to be calibrated before "zero failures" is a claim

Spec A-2 says the 24-on-8 harness *"models CI's failure and not a different
one"*, supported only by the matching error message. §1 and §4 together now make
the stronger statement: **the local harness is substantially harsher than the CI
job it stands in for.**

- Local: 24 busy processes **plus** Vitest's own 7 forks on 8 logical cores — of
  the order of 4× oversubscription.
- CI: two Vitest main processes plus two fork pools, sized from the runner's own
  core count, on a `ubuntu-latest` hosted runner — of the order of 2×.

**The runner's core count has not been measured and must not be guessed.** It
determines both fork-pool sizes and therefore the real ratio, and this plan has
already shipped one number nobody read. T014 measures it with `nproc` in the job
itself.

**Measured 2026-09-22, PR #2534, CI run 35743075866, job 106797612860** (the
`nproc && free -m` diagnostic step, run before `pnpm test`):

```
4
              total        used        free      shared  buff/cache   available
Mem:          15989        1229       12005          47        3148       14759
Swap:          3071           0        3071
```

`ubuntu-latest` carries **4 logical cores** and ~16 GiB RAM — not 8, and not
"twice CI's load" as guessed. Recomputing the pre-fix ratio from the measured
figure, not the guess: each package's fork pool is
`max(availableParallelism() - 1, 1)` = `max(4 - 1, 1)` = **3 workers + 1 main =
4 processes**; with `apps/kiosk-web` and `apps/management-web` running
concurrently (the pre-T015 state), that is **8 processes demanded on 4 cores —
2.0× oversubscription**, exactly the "order of 2×" estimate, now confirmed
rather than guessed.

**The local harness is recalibrated to the same 2.0× ratio, on this machine's
own core count.** The local worktree machine has **8 logical cores** (`nproc` /
`os.cpus().length`, checked directly, not assumed from phase 1's unstated
figure). A single Vitest run of **the target package's full suite** demands 1
main + `max(8 - 1, 1)` = 7 workers = **8 processes**. To reach the measured
2.0× ratio on 8 cores, total demand must be 16 processes, so the busy-process
count is **16 − 8 = 8** (not phase 1's 24, which produced ≈ 3.9× — nearly
double the measured CI ratio).

**Correction, post phase-6 review (PR #2534): "the target package's full
suite" is load-bearing, and T016's first pass did not honour it.** It ran a
*single test file* (`npx vitest run <one file>`), not the package's full
suite. Measured directly via `Get-Process` sampling on this machine: a
single-file invocation spawns **one** fork worker (Vitest forks per file, and
there was one file) — **2 processes total**, not 8; a full-package run (38
files, enough to saturate the pool) spawns all 7 forks as this section
assumed, confirmed by the same sampling. Two compounding defects followed: (a)
the busy-process count above was calibrated against a demand figure the actual
command never produced, landing the harness at ≈1.25× instead of 2.0×; (b)
`npx vitest run <file>` run directly from `apps/management-web` never goes
through the root `pnpm -r` script at all, so T015's `--workspace-concurrency=1`
was not exercised by it in any way — the run could not have told the
difference between the fix present and absent. T016's corrected re-run
(tasks.md) runs the real root command against both packages' full suites, so
the fork pool genuinely saturates and the concurrency flag is genuinely
exercised. **T016 now runs at 8 busy Node processes on 8 logical cores,
against the full `kiosk-web` + `management-web` suites through `pnpm -r
--workspace-concurrency=1`** — the busy-process number is unchanged from the
first pass, but what it is added to, and what it therefore tests, both
changed.

Also observed on the same run, as a check on T015 (§6 below, not part of the
calibration): the `frontend` job completed in **2 m 04 s**
(`14:50:22Z`→`14:52:26Z`), matching the plan's ≈ 2 m 04 s prediction exactly, and
the three suites now run serially and back-to-back — `shared` 16.06 s
(`14:51:11`), `kiosk-web` 12.38 s (`14:51:27`), `management-web` 36.61 s
(`14:51:40`, ending `14:52:17`) — no overlap between any two.

This cuts both ways and both halves must be said:

- It means **15 % under this harness is not 15 % in CI** — the real rate is
  lower, and the real before-rate was lower than 40 % too (CI produced 7
  occurrences in a week, not four in ten runs).
- It does **not** license accepting the result. The gate was pre-committed in
  writing, and a criterion revised downward after it fails is not a criterion.
  The bar stays **zero**; what T014 changes is that the bar is finally being
  measured at the load the bar is supposed to describe.

### 6. Does the T004/T005 diff still ship? **Yes — kept, and it ships together with T015.**

**Kept, not reverted.** Four reasons, in descending strength:

1. **T005's guard cannot exist without T004.** The guard injects
   `SLOW_RESPONSE_MS = 1500`, deliberately past RTL's 1000 ms default. Revert the
   bound and the guard is permanently red. The two are one change and tasks.md
   already commits them as one commit; there is no "revert T004 only" option that
   leaves anything standing.
2. **It is measured, in the direction claimed** — 40 %→15 % on the same harness,
   with a diff verified untouched between the batches. This repo does not discard
   measured improvements because they are partial.
3. **The two mechanisms are orthogonal, and each covers what the other cannot.**
   The deadline is a *failure bound*; serialisation is a *resource fix*.
   Serialisation removes the contention this repo owns; it cannot remove a noisy
   neighbour on a shared hosted runner, and against that the 1000 ms default has
   only 3× headroom over the **already measured** 319 ms contended cost. ADR-0150
   §1 frames the deadline as a bound that should be generous, independently of
   this defect.
4. **Reverting would forfeit the RED artefact** T005 already produced and the
   ADR-0139 gate already consumed.

**What changes is the claim attached to it.** T004/T005 is now recorded — in
plan, tasks and the PR body — as **necessary and not sufficient**. The delivery's
"the flake stops" criterion (SC-E) transfers from T007 to **T016**, measured
after T015. A PR that shipped T004/T005 alone, on a green `frontend` bucket,
would read as a fix and would not be one; that reading is the failure mode this
section exists to prevent.

**If T016 still does not reach zero:** stop again. Do not raise the number, do
not accept, do not silently add a job. That outcome is the §4-item-3 escalation,
it is a separate issue, and the PR says so.

### 7. Scope — what belongs to #2520 and what does not

Stated explicitly in both directions, because silent expansion and silent
contraction are equally bad here.

**Inside #2520/#2419:**

- T004/T005 — unchanged, three files, one package. Plainly in scope.
- **T015's one-token change to the root `package.json` `test` script.** It is a
  shared file and it changes how every PR's frontend bucket runs, so the judgement
  is not automatic. It lands here because: it is the *measured cause* of the
  defect the issues describe; it is additive and reversible in one token; it adds
  no job, no runner, no bucket and no artefact; the entire blast radius is inside
  the `frontend` bucket this spec already owns; and its cost is +17 s against a
  13-minute margin. A fix that is in the right file is not out of scope for being
  in a shared one.
- T014's `nproc` measurement — a diagnostic step, removed or kept by the
  reviewer's preference at T017.

**Outside, and needing its own issue if it is ever wanted:**

- **A dedicated CI job** for the frontend suite, or for this file — a new bucket
  in the manual four-bucket read, a second checkout and install, and a standing
  runner cost. Broad enough to deserve filing, discussion, and possibly an
  ADR-0150 amendment, none of which this lane may do (ADR-0144).
- **Excluding any test file from the ordinary run.** Same reasoning, plus the
  rot risk in §4.
- **Adopting ADR-0150's actual reduced-settle-budget job.** A different defect
  class; unrelated to this spec; still unadopted and still worth doing on its own
  merits some day.
- **`vitest` `retry`**, at any level. Masks the defect and erases the evidence —
  the same objection as re-running a red CI job (MEMORY: *a re-run erases the
  failure from CI history*).

**Recommendation to the gate:** keep T014–T017 inside #2520. File the dedicated-job
escalation only if T016 fails, and file it then with T016's numbers attached
rather than pre-emptively.

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

**E. ~~ADR-0150's CI-budget job~~ — misread, and struck.** This entry said the
ADR's job was *"run the frontend suite with deadlines forced low, permanently"*
and named it the escalation if R2 materialised. R2 materialised, the ADR was
re-read, and **that job is a detector for a different defect class** — it forces
every *fixed-count settle* to 1 in order to catch settle-based flakes by
behaviour. It tightens budgets. Adopting it here would make #2520 more frequent,
not less. Struck rather than deleted, in ADR-0150's own practice of leaving a
wrong record visible; §"R2 materialised" §3 carries the correction and §4 names
what the escalation actually is.

**E2. A dedicated CI job for the frontend suite, or for this one file** — its own
runner, unsharded, no sibling package beside it. It would work, and it is the
next escalation if E1 (below) does not reach zero. **Not taken now:** it costs a
new bucket in the four-bucket manual read that is this repo's only CI gate
(MEMORY: *`develop` has no required status checks*), a second checkout and
install, and standing runner minutes — for a cause that E1 removes with one
token. Broad enough to want its own issue (§"R2 materialised" §7).

**E3. Exclude the affected file from the parallel run and run it separately.**
Rejected on rot: an exclusion list is a place tests go to stop being noticed, and
the excluded file would keep passing in isolation while nobody re-checks whether
it still belongs there. Same objection as E2 plus that one.

**E1. Serialise the workspace test run** — `--workspace-concurrency=1` on the
root `test` script. **Adopted** (T015). The CI log shows `kiosk-web` and
`management-web` suites running simultaneously on one runner, so roughly 40 % of
`management-web`'s runtime is contended by a sibling suite on every CI run. One
token removes it, in a file developers run locally, for ≈ +17 s on a job with a
13-minute margin. Reasoning in full at §"R2 materialised" §4.

**F. Do nothing; re-run the job.** What has happened seven times. Each re-run
also erases the failure from CI history (MEMORY), so the evidence for a diagnosis
gets harder to collect with every occurrence rather than easier.
