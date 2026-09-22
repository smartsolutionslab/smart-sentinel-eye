# Tasks 216 — The deadline a loaded runner outran

**Spec:** [`spec.md`](./spec.md) · **Plan:** [`plan.md`](./plan.md)
**Issue:** [#2520](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2520) · **Same defect, closed by the same PR:** [#2419](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2419)
**Phase:** 3 — **re-issued 2026-09-22.** T001–T007 are done; **T007 failed its own gate** and T014–T017 are the replan. Frontend-only: no Aspire stack, no Docker, no backend build.
**Phase 4a colour:** **RED** for T005's guard · **CHARACTERISATION (green)** for everything else — §"The two colours"

> ## Status after the first phase-4 pass (2026-09-22)
>
> | Task | State |
> |---|---|
> | T001 reproduction | done — red as predicted |
> | T002 baseline | done — **329 / 38**, not the 334 this file asserted; corrected below |
> | T003 fake-timer audit | done |
> | T004 the two edits | **done, in the worktree, and it stays** — do not revert it |
> | T005 RED gate | **done and satisfied** — red before, green after, output captured |
> | T006 counterfactual | not reached |
> | **T007 contended validation** | **RUN, AND FAILED ITS OWN GATE — 3 recurrences in 20 runs, twice measured** |
> | T008–T013 | not reached |
>
> T007's instruction on failure was *"stop and report"*, and it was followed.
> **plan.md §"R2 materialised" is the answer**: the diff stands, the claim
> attached to it narrows, and a second mechanism (T014–T017) joins it before
> anything ships. Read that section before starting T006.

---

## What the gate settled (2026-09-22), so no task re-opens it

1. **#2520 and #2419 are one defect** (same file, line, element; 5 + 2 = 7
   occurrences). The PR closes both. #2419's *"a tighter `waitFor` timeout is not
   a fix"* was a starting hypothesis — its own text says *"that is where to
   start, not a conclusion"* — written before the mechanism was measured. Spec
   §"The objection in #2419" sets out the comparison; **do not re-litigate it,
   and do not drop it from the PR body either**, because the next reader needs
   the reasoning and not just the verdict.
2. **`apps/shared` is out of scope.** No flake has ever been reported there; a
   different test-setup shape is not on its own a reason to change it
   (ADR-0036). There is no task for it below.
3. **US2 (`apps/kiosk-web`) is optional and defaults to OFF**, by the same
   criterion applied consistently — it has no reported flake either. **Do not
   start T010 unless the reviewer explicitly asks for it.**

US1 alone closes both issues.

---

## Parallelism, stated once

**`[P]` is the disjoint-files rule (ADR-0109), and here it marks almost
nothing.** Every US1 task touches one of three files in one package, in an order
fixed by a red gate. Marking them `[P]` would tell the orchestrator to fan out
work that cannot be fanned out.

The genuine parallelism is **T001 ∥ T002** — two independent measurements, no
writes.

**Do not fan out US1.** It is three small file edits gated on one red
observation; a second agent costs more in coordination than the whole change is
worth.

---

## Dependency order

```
T001 [P] ─┐
T002 [P] ─┴─> T003 ─> T004 ─> T005 (RED gate) ─> T007 ✗ ──> [RE-PLAN]
                                                              │
                     ┌────────────────────────────────────────┘
                     │
        T014 [P] ────┴──> T015 ──> T016 (⟨GATE⟩ zero, or stop) ──> T017
                                        │
                                        └──> T006 ──> T008 ──> T009 ─┬─> T012 ─> T013
                                                                     │
                                    (only if the reviewer asks)      │
                                    T010 ─> T011 ────────────────────┘
```

**Two hard gates now, not one.**

- **T005 (RED)** — satisfied. Its verbatim output is the artefact the PR quotes
  (ADR-0139, ADR-0144).
- **T016 (ZERO)** — new, and the one that decides whether this delivers. It is
  T007 re-run after T015, at a load calibrated by T014. **Zero failures, or
  stop and escalate.** Do not soften it; it is the second time this criterion
  has been put in writing and the first time it was put in writing it was
  believed rather than measured.

T014 is `[P]` against nothing useful — it is one command — but it must precede
T016, because an uncalibrated harness makes "zero" meaningless.

**T005 is a hard gate.** Its verbatim red output is the artefact phase 4b
consumes and the PR body quotes (ADR-0139, ADR-0144). Do not start T006 without
it.

---

## The two colours

| Task | Colour | Why |
|---|---|---|
| **T005** | **RED** | Asserts behaviour nothing asserts today — that the advisory survives a late refusal. Must be seen failing against the unchanged tree. |
| T002, T009, T011 | **CHARACTERISATION, green** | Existing suites captured passing before, passing **unmodified** after. An assertion that has to be edited is evidence the change moved behaviour — **block, do not adjust**. |
| T001, T003, T004, T006–T008, T010, T012, T013, **T014–T017** | neither | Measurements, edits and bookkeeping. |

**T014–T017 add no test and change no assertion.** T015 changes only *how* the
existing suites are scheduled, so the characterisation obligation it carries is
T009's: the same counts, unmodified. If serialising the workspace changes a
single test's outcome, that is a test with an undeclared dependency on a sibling
package's process, and it is a finding — **block, do not adjust**.

**Why "make the flaky test pass" is not the red task:** there is no failing test
to fix — it passes locally, always. Treating the flake itself as the red
observation would mean waiting for CI to fail on its own schedule. That is the
whole reason T001's injection harness exists.

---

## US1 (P1) — the frontend bucket stops failing for reasons that are not the diff

### T001 [P] [US1] — reproduce the defect deterministically, before changing anything

**Files:** none written. Work in a scratch copy or revert afterwards.

In `apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx`,
temporarily replace the failing test's mock with

```tsx
fetchMock = vi.fn(async () => {
  await new Promise((r) => setTimeout(r, 1500));
  return failedResolveResponse();
});
```

and run `npx vitest run src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx`.

**Done when:** the run fails with
`TestingLibraryElementError: Unable to find an element by: [data-testid="placeholder-preview-error"]`
at line 239, and the verbatim output is saved. **Revert the edit.**

**If it passes:** the premise is wrong — stop, comment on #2520 with the observed
output, and hand back to phase 1. Do not fix a defect the tree does not have.
(Spec 214's premise check found an issue's claim stale; ~11 of ~20 board issues
were stale by delivery. MEMORY: *verify the issue premise before planning*.)

### T002 [P] [US1] — capture the characterisation baseline

**Files:** none written.

```sh
pnpm -r --filter "./apps/**" test
```

**Done when:** the exact pass count **and** wall-clock for each package —
`management-web`, `kiosk-web`, `shared` — is written down as an *observed* figure
in the phase-5 note, not reported only to the orchestrator (MEMORY:
*self-review catches contradictions, never omissions*).

At HEAD, from CI run 35725455764's `frontend` job log (green, `develop`,
2026-09-22): `management-web` **329 / 38 files**, `kiosk-web` **168 / 12**,
`shared` **414 / 33**.

**This task previously said 334 for `management-web`. It was wrong** — carried
from a phase-1 draft and never re-read. Phase 4 measured 329 twice, CI's log says
329, and the static `it(` / `test(` call-site count of 313 plus `it.each`
expansion is consistent with 329. Corrected 2026-09-22.

Measure all three anyway — a CI figure and a local figure are different
observations, and the local wall-clock delta is the signal for spec A-3. The
numbers above are what you compare against, not a substitute for running it.

**If any package is red before the change:** stop. That is a pre-existing break,
not this work (MEMORY: *`typecheck:e2e` fails on a clean `develop`*).

### T003 [US1] — read the three files being changed

**Files read:**
`apps/management-web/src/test/setup.ts` (one line today),
`apps/management-web/vite.config.ts`,
`apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx` **in full**,
`apps/management-web/eslint.config.js`'s `no-restricted-syntax` block.

**Done when:** you can state (a) which two tests in the file already use
`vi.useFakeTimers()` and why neither calls `waitFor` inside the fake-timer block,
and (b) why a single non-looping `await new Promise((r) => setTimeout(r, N))` is
outside both shipped ADR-0150 selectors.

*Read before write; mirror the file's existing patterns rather than inventing one
(CLAUDE.md).*

### T004 [US1] — add the deadline and the comment

**File:** `apps/management-web/src/test/setup.ts` — add
`import { configure } from '@testing-library/react';` and
`configure({ asyncUtilTimeout: 10_000 });` with the comment plan §1 specifies.
**Both halves of that comment:** the ADR-0150 reasoning **and** the fake-timer
landmine (FR-004).

**File:** `apps/management-web/vite.config.ts` — add `testTimeout: 30_000` to the
`test` block with plan §2's comment (FR-002).

**Done when:** both files are edited and `npx vitest run` passes for the package
with the **same count** as T002. The guard does not exist yet; do not look for it.

**Both edits, one commit.** FR-001 without FR-002 converts the failure into
`Test timed out in 5000ms` — and *more* often, not less: spec §Claim 8 measured
3-in-5 that way against 2-in-5 unchanged.

### T005 [US1] — write the guard, and observe it RED ⟨GATE⟩

**File:** `apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx`

Add the test plan §3 sketches, **after** the existing should-fix-5 test. Named
constant `SLOW_RESPONSE_MS = 1500`. **Do not edit any existing test.**

The red observation, in this order:

1. With T004's two edits **temporarily reverted** (`git stash` them), run the
   file. **Expect red**, with `Unable to find an element by:
   [data-testid="placeholder-preview-error"]` naming the **new** test.
2. Save the verbatim output. This is what the PR body quotes.
3. Restore T004. Run again. **Expect green.**

**Done when:** both outputs are saved and the guard is green with T004 applied.

**Do not adjust the guard to make step 1 red.** If step 1 is green, the guard
guards nothing — construct the counterfactual until it fails, or report that the
mechanism is not what the spec claims (MEMORY: *prove a guard by counterfactual*).

Note on shape: this guard asserts on a **DOM element the component renders**, not
on the configured timeout value. Asserting `asyncUtilTimeout === 10_000` would be
an assertion checking its own input (MEMORY) and would pass whether or not the
bound does anything.

### T006 [US1] — verify the guard cannot be satisfied by a smaller bound

**Files:** none permanently written.

Set `asyncUtilTimeout` to 1000 (RTL's default) with the guard in place. Run.
**Expect red.** Restore 10 000.

**Done when:** SC-2's counterfactual output is saved. This is the only evidence
that distinguishes a guard from a test that happens to pass.

### T007 [US1] — run the contended reproduction, before and after

**Files:** none written.

With busy Node processes saturating the machine — phase 1 used 24 on 8 cores:

```sh
node -e "const e=Date.now()+900000;let x=0;while(Date.now()<e){for(let i=0;i<1e6;i++)x+=Math.sqrt(i)}" &
```

1. On the tree **without** T004/T005, run the affected file **ten times**. Record
   failures and their messages. Phase 1 observed 2/5 and 2/5 in two batches.
2. With T004/T005 applied, run it **ten times**. Expect zero failures **and no
   `Test timed out`**.

**Done when:** both counts are recorded.

**If step 2 shows any failure:** stop and report. A partial cure is spec A-1
failing, and the escalation is **not** a larger number.

*This is the only evidence in the whole delivery that speaks to the actual
defect. A green local run and a green CI bucket both say nothing — the test
already passes ~95 % of the time.*

#### T007 outcome — **RUN 2026-09-22. FAILED ITS OWN GATE.**

| Batch | Runs | Clean | Target defect reproduced | Other |
|---|---|---|---|---|
| Before (unfixed tree) | 10 | 6 | **4** — `Unable to find … [data-testid="placeholder-preview-error"]` | — |
| After, batch 1 | 10 | 7 | **1** (run 6) — **both** the should-fix-5 test **and** the new guard, same message, at **10371 ms / 10630 ms** | 2 × `[vitest-pool-runner]: Timeout waiting for worker to respond` |
| After, batch 2 | 10 | 8 | **2** (runs 1 and 6) — identical signature, **10.4 s – 14.5 s** | 0 |
| **After, combined** | **20** | **15** | **3 (15 %)** | 2 |

Batch 2 was run specifically to rule out the first-run-after-machine-churn
artefact (MEMORY: *measurement runs need repeating*). It reproduced the failure
rather than dissolving it. The diff was verified untouched throughout — `git diff
--stat`: 63 insertions, three files, additive only.

**Do not re-run T007 to try for a better ten.** The number is measured twice and
the escalation is T014–T017. Re-rolling a measured result until it comes out
right is the habit this whole spec exists to argue against.

**Read plan.md §"R2 materialised" now.** It carries the diagnosis (the failures
sit at 345× the idle cost, which proportional slowdown does not produce — the
process is being *starved*, and the two worker-response timeouts are a second
instrument saying so), the correction that *"ADR-0150's CI-budget job"* was a
misread citation, and the mechanism that replaces it.

---

## US1b (P1) — added by the re-plan: stop the frontend bucket starving itself

**These four tasks are the delivery's actual fix for the flake.** T004/T005 stay
and ship with them; the claim attached to T004/T005 narrows to *necessary, not
sufficient* (plan §"R2 materialised" §6).

### T014 [US1b] — measure the CI runner, so "contended" means something ⟨precedes T016⟩

**Files:** `.github/workflows/ci.yml` — one temporary diagnostic step in the
`frontend` job, before `Test`:

```yaml
      - name: Runner capacity (diagnostic — see spec 216 §R2 materialised)
        run: nproc && free -m
```

**Why:** `vitest@4.1.11`'s `getDefaultThreadsCount` returns
`max(availableParallelism() - 1, 1)` for a non-watch run, so the runner's core
count determines both packages' fork-pool sizes and therefore the real
oversubscription ratio the `frontend` job runs at. **This plan has not measured
it and must not guess it** — it has already shipped one number nobody read
(334). The local harness is 24 busy processes plus 7 Vitest forks on 8 logical
cores; whether that is twice CI's load or the same as it is not currently known.

**Done when:** the runner's core count is read off a real CI run and **written
down as an observed figure** in plan §"R2 materialised" §5, not reported only to
the orchestrator (MEMORY: *self-review catches contradictions, never omissions*).
The local harness's process count for T016 is then set to reproduce that ratio,
and the chosen ratio is written down with it.

**Note:** this needs a pushed branch to observe. If the delivery is not yet on a
PR, run T015 first and read T014's figure off that PR's own `frontend` job.

### T015 [US1b] — serialise the workspace test run

**File:** `package.json` (workspace root), the `test` script only:

```json
-  "test": "pnpm -r --filter \"./apps/**\" test && pnpm test:guards",
+  "test": "pnpm -r --workspace-concurrency=1 --filter \"./apps/**\" test && pnpm test:guards",
```

Add the comment plan §3b requires — **why** the concurrency is capped (it is a
resource decision, not a performance one) — or the next person optimising CI
removes it. `package.json` takes no comments, so it goes in the `frontend` job's
`Test` step in `.github/workflows/ci.yml` **and** as one line in
`apps/management-web/src/test/setup.ts` beside FR-004's comment, which is where
someone debugging this test will actually be looking.

**Grounding, not to be re-derived:** CI run 35725455764's `frontend` job log
shows `apps/kiosk-web` (18 s) and `apps/management-web` (44 s) starting within a
second of each other on one runner — roughly 40 % of `management-web`'s runtime
is contended by a sibling suite, on every CI run, by construction.

**Done when:** the script is changed, `pnpm test` passes locally with the counts
from T002, and the serial wall-clock is recorded. Expected ≈ +17 s (16.49 +
17.99 + 44.40 ≈ 79 s serial against ≈ 62 s overlapped); the `frontend` job
completes in 2 m 04 s against `timeout-minutes: 15`.

**If the wall-clock delta is much worse than +17 s:** record it and report. The
cost argument above is what justifies the scope call in plan §"R2 materialised"
§7, and a cost that turns out to be wrong reopens that call.

**Do not** reach for `NPM_CONFIG_WORKSPACE_CONCURRENCY` as a CI-only job env. A
scheduling setting only CI applies is invisible to whoever tries to reproduce the
failure locally, and that is how this defect cost a day in the first place.

### T016 [US1b] — re-run the contended reproduction at a calibrated load ⟨GATE⟩

**Files:** none written.

Re-run T007's procedure with T004, T005 **and** T015 applied, at the load T014
calibrated, **ten runs**, and then **ten more**. Twenty, because ten is what
produced a 1-in-10 reading that turned out to be 3-in-20.

**Done when:** **zero** reproductions of
`Unable to find an element by: [data-testid="placeholder-preview-error"]`, **zero**
`Test timed out`, and **zero** `[vitest-pool-runner]: Timeout waiting for worker
to respond` across all twenty. Record the raw per-run outcomes, not a summary.

**The worker-response timeout counts as a failure here even though it is not the
target defect.** It appeared only in the after-batches and only under load; plan
§"R2 materialised" §2 reads it as a second instrument reporting the same
starvation. If T015 is the right fix, it should go away too — and if it does not,
that is information worth having rather than noise to discount.

**If any failure remains: STOP. Do not raise `asyncUtilTimeout`. Do not accept
the improvement. Do not add a CI job on your own initiative.** That outcome is
the plan §"R2 materialised" §4-item-3 escalation — a dedicated job or a per-file
isolation — which is **out of #2520's scope and needs its own issue**, filed with
T016's numbers attached (ADR-0144: the lane does not decide this).

### T017 [US1b] — remove or keep T014's diagnostic step

**File:** `.github/workflows/ci.yml`

With T014's figure written down in the plan, the `nproc` step has served its
purpose. Default: **remove it** — a permanent diagnostic nobody reads is
clutter, and the figure is now in the artefact where it belongs. Keep it only if
the reviewer asks.

**Done when:** the step is removed (or the reviewer's decision to keep it is
recorded in the PR body), and `git diff` on `.github/workflows/ci.yml` shows
either nothing or one intentional line.

---

## US1 (P1), continued — the checks that close it out

T006, T008 and T009 were never reached in the first phase-4 pass. They run
**after** T016's gate, so that T009's characterisation covers T015's scheduling
change as well as T004's bound.

### T008 [US1] — lint, typecheck, format

**Files:** none written.

`pnpm lint && pnpm typecheck && pnpm format:check` at the repo root.

**Done when:** all three are clean **and the guard carries no `eslint-disable`**
(SC-8). If ADR-0150's selectors flag it, that is a finding about the selector —
report it, do not suppress it.

*`pnpm typecheck` runs `typecheck:e2e`, which has failed on a clean `develop`
before (MEMORY). If it fails, confirm against a stash before attributing it here.*

### T009 [US1] — characterisation, after

**Files:** none written.

Re-run `pnpm -r --filter "./apps/**" test`. Compare against T002.

**Done when:** `management-web`'s count matches T002's **plus exactly one** (the
guard) — i.e. **330 / 38** — the other packages match exactly (`kiosk-web` 168,
`shared` 414), and `git diff` on the test file shows **only an addition**.

**T015 is in the tree by now, so this run is also T015's characterisation.** The
suites are scheduled differently; the counts must not care. Record the serial
wall-clock alongside the counts.

**If an existing assertion had to change:** block. That is evidence the deadline
change moved behaviour, and it is a different spec (constitution §Testing;
CLAUDE.md house rules).

---

## US2 (P2) — `apps/kiosk-web`, **only if the reviewer asks**

**Default: skip T010 and T011 entirely.** `kiosk-web` has no reported flake, and
the gate's criterion for dropping `apps/shared` applies here identically. These
two tasks exist so that *if* the reviewer wants the consistency, the work is
specified — not because the spec recommends doing it.

### T010 [US2] — the same two edits in `apps/kiosk-web`

**Files:** `apps/kiosk-web/src/test/setup.ts`, `apps/kiosk-web/vite.config.ts`

Identical `configure({ asyncUtilTimeout: 10_000 })` + comment, identical
`testTimeout: 30_000`. **No test file edited.** Leave
`useSessionExpiry.test.ts:178`'s per-site `{ timeout: 10_000 }` exactly as it is
— removing it is a separate change and would destroy T011's baseline.

Blocked on T009.

### T011 [US2] — kiosk-web characterisation

`pnpm --filter ./apps/kiosk-web test`. **Done when:** the count matches T002's
kiosk figure exactly.

---

## Wrap-up

### T012 — commits and the PR

**Commits** (ADR-0030 Conventional Commits; **no `Co-Authored-By`** per ADR-0086;
each commit must build on its own, because rebase-merge lands them individually —
ADR-0087):

| Commit | Contents |
|---|---|
| `test(management-web): bound waitFor by a deadline a loaded runner cannot outrun` | T004 + T005 — the guard and the bound it needs are one change |
| `ci(frontend): run the workspace suites in series so they stop starving the runner` | T015 (+ T014/T017's workflow churn, if any survives) — a **separate** commit, because it is a different mechanism with a different justification, and rebase-merge lands them individually (ADR-0087) |
| `test(kiosk-web): adopt the same async deadline` | T010, **only if US2 was authorised** |

**PR to `develop`** — `gh pr create --base develop` (CLAUDE.md; never `main`).

The body must carry:

- T005's **verbatim red output**, and the green after (ADR-0139 phase-4 gate).
- T006's counterfactual output.
- **T007's contended counts in full — all three batches, including the 3-in-20
  that did not clear its gate.** Not the 40 %→15 % summary alone. A PR that
  reports only the improvement is a PR whose reader cannot tell that the plan's
  own Definition of Done was missed and re-planned.
- **T016's calibrated counts**, with T014's runner figure beside them so the
  reader can see what load "zero" was measured at.
- **One sentence stating that T004/T005 is necessary and not sufficient**, and
  that T015 is the mechanism the flake-stops claim rests on. Without it the
  reader will attribute the fix to the timeout raise, which the measurements do
  not support.
- T002/T009's characterisation counts for every package touched — **329 → 330**
  for `management-web` (this spec asserted 334 for two revisions; say so, since
  the PR is the last place a wrong baseline can still be caught).
- The serial-vs-overlapped wall-clock delta from T015, so the CI cost is priced
  in the record rather than asserted.
- The spec §"The objection in #2419" comparison, in short: why *"a timeout is not
  a fix"* was the right caution against an unbounded race and why the measured
  defect is the bounded kind — **and that #2419's caution was partly vindicated**:
  the timeout raise alone did not carry it.
- One line stating plainly that **a green `frontend` bucket is not evidence
  here**, and why.
- `Closes #2520` and `Closes #2419` — a closing keyword for **each**.

**After the merge, verify both issues actually closed.** A PR mention auto-closes
roughly one time in three (MEMORY), and this PR is asking for two.

### T013 — the board

Add the feature issue to Project #13 by hand; `/speckit-tasks` adds nothing:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2520
```

Needs the `project` scope (`gh auth refresh -s project,read:project`).
`item-add` prints nothing on success, and `item-list` defaults to 30 items —
verify with `--limit 2000`, or a filled board reads as empty (CLAUDE.md; MEMORY).

**Feature-level only. No per-task issues** — that stopped after spec 028.

---

## What is explicitly not a task

- **`apps/shared`.** Dropped at the gate; no reported occurrence (ADR-0036).
  Spec §US3 keeps the inspection so nobody re-derives that the packages differ.
- **Removing the redundant per-site `{ timeout: 10_000 }` annotations.** Out of
  scope; it would destroy T002/T011's baselines.
- **Shimming `globalThis.jest = vi`, or bumping `@testing-library/dom`.** Plan
  §Alternatives C and D — each wants an ADR, which ADR-0144 forbids this lane
  from writing.
- **Touching any production file.** If the change needs one, spec §Claim 2 is
  wrong and phase 1 reopens.
- **Chasing #2247 / #2409.** Answered in spec §Claim 6: different defect, already
  closed, no shared cause.
- **Raising `asyncUtilTimeout` above 10 000.** Forbidden by T007's own
  instruction and by plan §"R2 materialised" §2 — the failures sit at 345× the
  idle cost, which is not proportional slowdown, so more clock does not reach
  them. It is the tuning habit ADR-0150 exists to stop.
- **Adding a dedicated CI job, or excluding any test file from the ordinary
  run.** These are the *next* escalation if T016 fails, and they are **outside
  #2520** — a new CI bucket changes the four-bucket manual read that is this
  repo's only gate on `develop` (MEMORY). File an issue with T016's numbers;
  ADR-0144 does not let this lane decide it.
- **`vitest` `retry`, at any level.** Masks the defect and erases the evidence —
  the same objection as re-running a red CI job (MEMORY: *a re-run erases the
  failure from CI history*).
- **Capping Vitest's per-package fork pool.** `availableParallelism() - 1`
  workers plus one main is not oversubscription *within* a package; T015 removes
  the overlap that makes it one. Smallest change (ADR-0036).
