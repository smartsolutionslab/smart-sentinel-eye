# Spec 216 — The deadline a loaded runner outran

**Issue:** [#2520](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2520)
**Duplicate of:** [#2419](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2419) — same file, same line, same element, filed six days earlier with five recorded occurrences. See §"Two issues, one defect".
**Branch:** `2520-overlay-preview-flake`
**Status:** Phase 1 complete — awaiting review. Two open questions were put to the gate and **both were answered** (2026-09-22): this delivery resolves #2419 as well, and the `apps/shared` story is dropped. See §"The objection in #2419, and the evidence that answers it" and §"Out of scope".
**ADRs:** ADR-0150 (waiting is a condition, not a count), ADR-0139 (rules that fail the build, not the review), ADR-0144 (the autonomous lane may not make architectural decisions), ADR-0036 (smallest change; define "done" up front), ADR-0074 (two React apps), ADR-0052 (xUnit/Vitest + the test stack)
**Constitution:** §Testing (`Waiting:` bullet, added by ADR-0150), §IV (latency budget — **N/A here**, see §"Latency budget impact")

---

## Problem

One test in `apps/management-web` fails in CI roughly once per twenty runs, on
branches whose diffs contain no TypeScript at all, and passes on re-run with the
same commit.

```
apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx:239

Test: "Never blocks submission on a failed resolve: Save as draft stays enabled
       and the draft is created with the text exactly as typed
       (should-fix 5, US1 scenario 15)"

TestingLibraryElementError: Unable to find an element by: [data-testid="placeholder-preview-error"]
 ❯ Proxy.waitForWrapper .../@testing-library/dom/dist/wait-for.js:163:27
 ❯ src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx:239:11
```

**The wait is already the idiom ADR-0150 sanctions.** It is a `waitFor` on a DOM
condition — a deadline poll, not a fixed count of yields. Nothing in this test
resembles the `flushConnect` defect ADR-0150 was written about. What is wrong is
the *size of the deadline*: Testing Library's default `asyncUtilTimeout` of
**1000 ms**, which nobody in this repository ever chose, bounds work that takes
**29 ms** on an idle developer machine and **319 ms** on a contended one.

ADR-0150 §1 already states the standard this falls short of:

> The deadline is a **failure bound**, not a wait: a correct implementation
> reaches the state on the first poll on a fast machine and the hundredth on a
> loaded one, and passes in both.

This one reaches the state on the first poll on a fast machine, and on a loaded
one it does not get a hundredth poll — it gets twenty, and CI has now exhausted
them seven times.

---

## Premise check — every claim re-derived at this worktree's HEAD

The issue's claims were not taken on trust. Each was measured in
`D:\Github\sse-2520` (branch `2520-overlay-preview-flake`, cut from
`origin/develop`).

### Claim 1 — the cited line is the second of two waits, and it is the one that fails. **Confirmed.**

`OverlayEditorDialogResolvePreview.test.tsx:230-241`:

```tsx
await user.type(screen.getByLabelText(/name/i), 'Line-1 Title');
fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: '{{bogus}}' } });

await waitFor(() => {                                        // 236 — the request left
  expect(fetchMock).toHaveBeenCalledTimes(1);
});
await waitFor(() => {                                        // 239 — the refusal arrived
  expect(screen.getByTestId('placeholder-preview-error')).not.toBeNull();
});
```

Line 236 covers the 250 ms debounce (`DEBOUNCE_MS`,
`apps/shared/src/hooks/useDebouncedValue.ts:12`). Line 239 covers only the
response leg: the mocked `fetch` promise, `fetchBaseQuery`'s body read, RTK
Query's rejected-thunk dispatch, and React's commit.

### Claim 2 — the element it waits for is *not* transient, and not gated on anything racy. **Confirmed.**

`PlaceholderPreviewPanel.tsx:68` sets `data-testid` to `placeholder-preview-error`
whenever `isError` is true. `isError` is `resolveFailed` —
`useResolveOverlayTextQuery(...).isError`, passed through
`OverlayEditorDialog.tsx:170,319` and `OverlayEditor.tsx:721` **ungated**: unlike
`resolvedPreview`, it is not withheld by the `settled` gate
(`OverlayEditorDialog.tsx:179-181`). Once the query rejects, the element exists
and stays. So the failure is lateness, not a missed edge.

### Claim 3 — why *this* wait and not its five siblings in the same file. **Answered; the issue does not ask precisely enough.**

Measured on an idle machine with a timing probe inserted at each step (probe
removed; the numbers are the artefact):

| Segment | Idle | Under 24-way CPU contention | Budget |
|---|---|---|---|
| `renderDialog()` | 56 ms | 358 ms | — |
| `user.type` (12 chars, name field) | 271 ms | 1073 ms | — |
| `waitFor` line 236 (debounce + request) | 292 ms | 312 ms | 1000 ms |
| **`waitFor` line 239 (response → error element)** | **29 ms** | **319 ms** | **1000 ms** |

Line 236's wait is dominated by a 250 ms `setTimeout`: a delayed timer still
*fires*, so contention barely moves it (292 → 312 ms). Line 239's wait is
dominated by **work** — promise hops, a store dispatch, a React commit — and
work is exactly what a descheduled worker thread stops doing. It inflated 11×
under moderate contention while its sibling inflated 1.07×.

**That is the whole reason this one test is the file's flake.** It is not more
chained, more debounced or more racy than its siblings. It is the only wait in
the file whose duration is pure CPU work with no timer floor, so it is the only
one whose margin contention can actually eat.

It is also, separately, the only test in the file that exercises the **error**
response (`failedResolveResponse()`, a 500), but that is incidental: there is no
retry or backoff anywhere on this path. `systemVariables.api.ts` uses
`gatewayBaseQuery` (`apps/shared/src/api/gateway.ts:112-137`), whose only
re-request is a single 401 renewal. A 500 goes straight to `isError`. The
retry-with-jitter explanation was considered and **disproved by reading the
base query**, not assumed away.

### Claim 4 — it is lateness, not absence. **Confirmed by deterministic reproduction.**

The issue reasons from "passed on the third attempt". That is consistent with
lateness *and* with a race that usually loses. The two are distinguished by
construction:

Injecting a fixed delay into the failed-resolve mock —

```tsx
fetchMock = vi.fn(async () => {
  await new Promise((r) => setTimeout(r, 1500));
  return failedResolveResponse();
});
```

— reproduces **the identical error at the identical line, on every run, on an
idle machine**:

```
TestingLibraryElementError: Unable to find an element by: [data-testid="placeholder-preview-error"]
 ❯ src/features/overlays/ZZProbeA.test.tsx:239:11
 Tests  1 failed | 4 passed (5)
```

With that injection left in place and nothing else changed, raising the deadline
makes it pass — proven twice, independently:

| Change | Result |
|---|---|
| `waitFor(..., { timeout: 10_000 })` at line 239 only | `Tests 5 passed (5)` |
| `configure({ asyncUtilTimeout: 10_000 })` in `src/test/setup.ts`, test file untouched | `Tests 5 passed (5)`, the test taking **2211 ms** |

The element arrives. It arrives at ~1.5 s in the injected case, and the 1000 ms
bound is the only thing that refuses it.

### Claim 5 — CI's failure is the same failure, not merely a similar one. **Reproduced.**

Run under 24 busy Node processes on 8 cores, the unmodified test file on the
unmodified tree produces CI's exact message and line, intermittently — 2 failures
in 5 runs in the first batch, and again in the second:

```
A-default-1000ms-deadline run2 | Tests 1 failed | 4 passed (5)
  | TestingLibraryElementError: Unable to find an element by: [data-testid="placeholder-preview-error"]
```

This matters because it is the only evidence that the *injection* harness in
Claim 4 is modelling the real thing rather than a different failure that happens
to share a message.

### Claim 6 — is this #2247's root cause, scoped to the frontend? **No. Answered, and the issue's guess is wrong.**

#2247 ("the backend CI job intermittently runs 4× its normal time") was **closed
2026-09-22** by #2409, and #2409's diagnosis is specific and local:
`StreamDistribution.Infrastructure.Tests` **hangs** — "the last line produced is
the assembly starting, and then nothing at all until the runner tears the job
down", with orphaned `dotnet` processes at teardown. A stuck test host, not a
slow one.

So there is no shared "runner resource contention" root cause to find. The two
defects share only a symptom class (intermittent, green on re-run) and a
consequence (a re-run erases the failure from CI history). Recorded explicitly
because the issue asks the question and a "probably related" answer would have
sent phase 4 looking for a runner-level fix that does not exist.

**Runner contention is nevertheless real and is this defect's trigger** — it is
just not a defect anyone can fix in this repository. What *is* in this
repository's control is the margin the tests leave for it.

### Claim 7 — the fix #2419 asks for is unavailable. **New finding; neither issue knows this.**

#2419's definition of done asks for "a condition that cannot be outrun by a slow
runner". The only wait that literally cannot be outrun is one driven by a fake
clock rather than the wall clock. **That option does not exist in this repo
today**, and the reason is mechanical:

`node_modules/@testing-library/dom@10.4.1/dist/helpers.js:14-28`

```js
function jestFakeTimersAreEnabled() {
  if (typeof jest !== 'undefined' && jest !== null) {
    return setTimeout._isMockFunction === true ||
           Object.prototype.hasOwnProperty.call(setTimeout, 'clock');
  }
  return false;                      // ← Vitest lands here, always
}
```

Measured in this repo's Vitest environment with `vi.useFakeTimers()` active:

```
ZZ hasJest=false hasClock=true
```

`setTimeout` carries Sinon's `clock`, but there is no global `jest`, so
`jestFakeTimersAreEnabled()` returns `false` and `waitFor` takes its
**real-timer** branch (`wait-for.js:83+`) — scheduling its 50 ms poll
`setInterval` and its own timeout `setTimeout` **on the faked timers nobody
advances**. The poll never runs; the timeout never fires; the test hangs.

Measured, not inferred. Two standard configurations, both hanging:

| Attempt | Result |
|---|---|
| `vi.useFakeTimers()` + `userEvent.setup({ advanceTimers: vi.advanceTimersByTime })` | `Test timed out in 5000ms.` (5023 ms) |
| the same, plus `delay: null` | `Test timed out in 5000ms.` (5188 ms) |

This also explains, retroactively, a comment spec 148's authors left in this very
file — *"fetch's own promise chain and RTK Query's dispatch are native
microtasks, not fake-timer callbacks, so flushing them exactly under fake timers
is fiddly"* — and why the two tests in the file that **do** use fake timers never
call `waitFor` inside the fake-timer block. They advance the clock explicitly and
then assert synchronously. That is not a style choice; it is the only shape that
works here, and nothing in the repo says so.

### Claim 8 — raising the deadline **alone** converts the failure rather than removing it. **Measured, and it changes the requirements.**

Two batches of five contended runs, same harness, same file, nothing else
altered between them:

| Batch | Config | Result |
|---|---|---|
| **A** | RTL default 1000 ms deadline, Vitest default 5000 ms `testTimeout` | 3 pass, **2 fail** — `TestingLibraryElementError: Unable to find an element by: [data-testid="placeholder-preview-error"]` |
| **B** | `asyncUtilTimeout: 10_000`, Vitest default 5000 ms `testTimeout` | 2 pass, **3 fail — `Error: Test timed out in 5000ms.`** |

B's failure is a *different* failure with the same consequence, and it is **more
frequent, not less**: the wait is now allowed ten seconds by a runner that kills
the test at five, so every run that would have recovered between 1 s and 5 s now
dies at 5 s instead of being refused at 1 s. The element is never named, so the
error says nothing about what the test was waiting for.

This is why FR-002 is a requirement and not a tidy-up. **Shipping FR-001 without
it makes the defect commoner and harder to diagnose.** Found by running the A/B
rather than by reasoning about it — the draft plan carried it as a risk; the
measurement makes it a fact, and a worse one than the risk described.

It also sets a floor on `testTimeout`: it must exceed `asyncUtilTimeout` plus the
test's other legs, and `user.type` alone measured **1073 ms** under contention
(§Claim 3). 30 000 clears 10 000 + every other leg with room; 5000 does not clear
10 000 at all.

---

## Two issues, one defect

| | #2419 | #2520 |
|---|---|---|
| Filed | 2026-09-16 | 2026-09-22 |
| File / line | `OverlayEditorDialogResolvePreview.test.tsx:239` | same |
| Element | `placeholder-preview-error` | same |
| Occurrences recorded | 5 (09-16 ×2, 09-16 late, 09-17, 09-18) | 2 (09-22, PR #2519) |
| Labels | `tech-debt` | `tech-debt`, `ci`, **`agent:ready`** |
| Carries a stated definition of done | **yes** | no |

Seven observations of one defect across two issues that do not reference each
other. This is the failure MEMORY records as *"check the board for the same
defect first"* and it has now happened again.

**Settled at the phase-3 gate (2026-09-22):** deliver against #2520 (it carries
`agent:ready`) and **close #2419 in the same PR** with its own closing keyword,
quoting its five occurrences into the record. The two are one defect with seven
observations.

This is recorded rather than done quietly because #2419's *scope* still governs
what a fix must answer — scope lives in comments and issue bodies, not only in
the issue the lane happens to pick up — and §"The objection in #2419" is where
that obligation is discharged.

---

## The objection in #2419, and the evidence that answers it

**#2419 says, in writing:**

> A tighter `waitFor` timeout is not a fix — it changes how often the flake is
> seen, not whether the assertion can be outrun.

**This spec proposes exactly a timeout change.** The contradiction was raised at
the phase-3 gate rather than assumed away, and **resolved there on 2026-09-22**:
proceed, and close both issues.

The reason it resolves rather than deadlocks is that the two statements are not
about the same thing, and the difference is worth writing out so a future reader
sees the reasoning instead of only the verdict.

**#2419's sentence is a starting hypothesis, and says so.** The surrounding text
reads *"Worth checking whether the error element is rendered from a debounced or
`setTimeout`-driven path that the test's fake timers or `waitFor` deadline can
outrun under load. **That is where to start, not a conclusion.**"* It was written
before any mechanism was known, and it is correct **for the defect it was
guessing at**: if the element's arrival were genuinely unbounded — a race the
assertion can lose outright — then no finite deadline is a fix, and a larger one
only lowers the observed rate.

**Phase 1 measured the mechanism, and it is the other kind.**

| | The defect #2419 hypothesised | The defect that is actually there |
|---|---|---|
| Arrival of `placeholder-preview-error` | possibly never — a lost race | **always**, bounded, CPU-work only |
| Evidence | a DOM dump taken at the moment of timeout | injection harness (Claim 4) + contention harness (Claim 5) |
| Cost of the arrival | unknown | **29 ms** idle, **319 ms** under 24-way contention |
| Does a larger bound fix it? | no — it would only change the rate | **yes** — proven two independent ways |
| Does a smaller bound break it? | — | **yes** — the counterfactual, SC-2 |

Three things make that table evidence rather than assertion:

1. **The element does arrive** (Claim 4). #2419 reasoned it "never arrives" from
   a DOM dump showing the dialog rendered without it. That dump is a snapshot
   taken *at the moment of the timeout* — it cannot distinguish "absent" from
   "not yet". The injection harness can, and shows it appearing afterwards, on
   every run.
2. **Two independent harnesses reproduce CI's exact failure and both are cured
   by the same change.** A deliberate 1500 ms delay (deterministic, idle machine)
   and genuine CPU contention (stochastic, 2-in-5) produce the identical message
   at the identical line; raising the bound clears both. A fix that only "changed
   how often the flake is seen" would not survive the deterministic harness at
   all, because there is no *how often* in it — it fails 5/5 before and passes
   5/5 after.
3. **A fake clock — the fix the objection implies — is unavailable** (Claim 7),
   for a mechanical reason in `@testing-library/dom@10.4.1` that neither issue
   knew about. So the literal DoD is not merely expensive; it is unsatisfiable
   without a library-level change that wants an ADR (ADR-0144 forbids this lane
   from writing one).

And the current wait is **already** ADR-0150's sanctioned idiom (§1) — a deadline
poll on a DOM condition. There is no "wait on a condition instead" upgrade left
to make. Only the bound is wrong, and ADR-0150 names the bound a *failure bound*,
explicitly expecting it to be generous rather than tight.

**What honesty requires stating alongside that:** a finite bound can always, in
principle, be outrun. This spec does not claim otherwise. It claims the margin
moves from ~3× the contended cost to ~31×, that the change is proven rather than
hoped, and that a guard (FR-003) plus a counterfactual (SC-2) make a future
shortfall *visible* instead of silent. If it recurs anyway, spec A-1 names the
escalation: ADR-0150's CI-budget job, not a larger number.

---

## Locked tech choices

| Concern | Choice | Source |
|---|---|---|
| Test runner | Vitest 4.1.11, `environment: 'jsdom'`, `globals: true` | `apps/management-web/vite.config.ts` |
| DOM testing | `@testing-library/react` / `@testing-library/dom@10.4.1` | existing |
| Wait idiom | `waitFor` / `findBy*` — a deadline poll on a condition | ADR-0150 §1 |
| Forbidden | a fixed-count settle immediately preceding an assertion | ADR-0150 §2, enforced in `apps/*/eslint.config.js` |
| Unchanged | every assertion in the affected file; all production code | this spec |

**No production code changes.** Nothing under `apps/*/src` outside `test/setup.ts`
and the one test file is touched.

---

## User stories

### US1 (P1) — the frontend bucket stops failing for reasons that are not the diff

*As anyone whose PR is blocked by the `frontend — lint + typecheck + test` job, I
need a red bucket to mean my change is wrong, not that the runner was busy.*

Independently shippable and independently observable: one setup file, one vitest
option, one new guard test in an existing file. Closes #2520 and #2419.

### US2 (P2, OPTIONAL — the gate's call, not the engineer's) — `kiosk-web` inherits the same bound

**Read this before starting US2.** The gate's ruling on the `apps/shared` story
(2026-09-22) was: *a different test-setup shape alone is not a reason to expand
scope into a file with no reported occurrence* (ADR-0036). **That criterion
applies to `kiosk-web` too, and this spec says so rather than quietly exempting
the story it happened to like.** There is no recorded flake in `apps/kiosk-web`
either.

What US2 has that the dropped `apps/shared` story did not is (a) a two-line edit
identical to US1's, with no new file and no new risk, and (b) ADR-0150's own
finding that the reason its defect spread was a correct idiom sitting adjacent
and un-adopted. That is an argument about drift, not about a known bug — so it is
a preference, and the reviewer decides.

**Default: do not ship US2.** Ship it only if the reviewer says so at the gate.
US1 is complete and closes both issues without it.


*As the next author of a timing-sensitive test in `kiosk-web`, I inherit the
corrected bound instead of rediscovering it.*

Separable — it ships alone, and US1 is complete without it — but ADR-0150's
central finding is that *"the correct idiom was present, adjacent, and documented
— and the next author still reached for the wrong one"*. Two packages already
hand-patched their way to the same number (`{ timeout: 10_000 }` in
`apps/kiosk-web/src/app/useSessionExpiry.test.ts:178` and seven sites in
`apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx`), each in
ignorance of the other.

`kiosk-web` is the package where this is genuinely the same two-line edit: it has
`src/test/setup.ts` and `vite.config.ts` with `setupFiles` already wired,
structurally identical to `management-web`.

### ~~US3 — `apps/shared`~~ · **dropped at the phase-3 gate (2026-09-22)**

Drafted, then dropped on the gate's ruling: **no reported occurrence in
`apps/shared`, so a different test-setup shape is not on its own a reason to
expand scope there** (ADR-0036, no speculative generality).

Left visible rather than deleted, because the *inspection* that produced it is
worth keeping: `apps/shared` has **no setup file at all**, configures Vitest in
`vitest.config.ts` (not `vite.config.ts`), and declares `environment: 'node'`
with jsdom opted into per file. So a future author who assumes the three packages
are alike — as this spec's first draft did — will be wrong. See §"Out of scope".

---

## Functional requirements

**FR-001 — the RTL async deadline is set once per package, not per call site.**
`apps/management-web/src/test/setup.ts` calls
`configure({ asyncUtilTimeout: 10_000 })` from `@testing-library/react`. It
governs every `waitFor` and every `findBy*` in the package.

*Why 10 000 and not a fresh number:* it is 340× the idle cost of the failing
wait and 31× its contended cost (§Claim 3), and it is **the value this repository
has already converged on twice, independently**, in `useSessionExpiry.test.ts`
and `CameraViewerCameraSwap.test.tsx`. Inventing a third number would be the
tuning-until-green move ADR-0150 names as the defect's origin.

**FR-002 — `testTimeout` is raised above the new deadline.**
Vitest's default per-test timeout is 5000 ms. A wait that is *allowed* 10 000 ms
but killed at 5000 ms has not been fixed; it has had its error message changed
from `Unable to find an element` to `Test timed out in 5000ms`. **Observed, not
predicted** — §Claim 8's batch B did exactly this. Set `test.testTimeout` in
`apps/management-web/vite.config.ts` to **30 000**.

**FR-001 and FR-002 ship together or not at all.** Either alone leaves the test
failing under contention; §Claim 8 is the measurement that says so.

**FR-003 — a guard that goes red if the deadline is ever reduced.**
A test in `OverlayEditorDialogResolvePreview.test.tsx` that injects a fixed delay
into the failed-resolve mock (the Claim 4 harness) and asserts the error advisory
still appears. It must be **observed failing** against today's 1000 ms default and
passing after FR-001 — the counterfactual, per ADR-0150's *"reproduction rather
than argument"* and MEMORY's *"prove a guard by counterfactual"*.

The injected delay is a **single** `await new Promise((r) => setTimeout(r, N))`
inside a mock, driving a fake forward. It is outside ADR-0150's ESLint selectors,
which match only such an await **inside a `for` / `for...of` / `for...in` loop**
(`apps/management-web/eslint.config.js`). Stated here so phase 4 does not reach
for an `eslint-disable` it does not need.

**FR-004 — the fake-timer landmine is written down where the next author will hit it.**
A comment at the `configure(...)` call recording Claim 7: under Vitest,
`@testing-library/dom`'s `jestFakeTimersAreEnabled()` returns `false` (no global
`jest`), so `waitFor` inside a `vi.useFakeTimers()` block schedules its poll on
faked timers nobody advances and hangs to `testTimeout`. Two configurations were
measured; both hang.

This is the requirement that stops the next person repeating a day of this
investigation, and it is the reason the value in FR-001 has to be generous rather
than clever.

**FR-005 (US2, optional) — `apps/kiosk-web` receives the identical FR-001,
FR-002 and FR-004 changes.** `src/test/setup.ts` gains the `configure(...)` call
and the comment; `vite.config.ts`'s `test` block gains `testTimeout`. No test
file is edited; the existing per-site `{ timeout: 10_000 }` in
`useSessionExpiry.test.ts` becomes redundant but is **left in place** — removing
it is a different change and would make this one's characterisation evidence
unreadable.

**Not started unless the reviewer says so** — see US2's own heading.

---

## Acceptance scenarios

### The observation — SC-1 is the reason this spec exists

**SC-1 (US1) — a late arrival is still an arrival**

```gherkin
Given the overlay editor dialog with a resolve endpoint that answers 500
  And the response is delayed by 1500 ms
When an operator types "{{bogus}}" into the label text
Then the placeholder advisory reports that the text could not be checked
  And "Save as draft" stays enabled
  And the draft is created with the text exactly as typed
```

**Observed RED today** — `TestingLibraryElementError: Unable to find an element
by: [data-testid="placeholder-preview-error"]`, deterministically, on an idle
machine. This is the new behaviour and its failure output is quoted in the PR
(ADR-0139, ADR-0144).

### The controls — each rules out a way SC-1 could be lying

**SC-2 (US1) — the bound, not the assertion, is what changed**

```gherkin
Given SC-1's delayed-500 scenario
  And the RTL async deadline restored to its 1000 ms default
When the suite runs
Then SC-1 fails with "Unable to find an element by: [data-testid=\"placeholder-preview-error\"]"
```

The counterfactual. A guard that cannot be made to fail is not a guard.

**SC-3 (US1) — no existing assertion moved**

```gherkin
Given the five existing tests in OverlayEditorDialogResolvePreview.test.tsx
When the suite runs before the change and again after it
Then all five pass in both runs
  And no assertion in any of them has been edited
```

Characterisation. An assertion that has to be edited is evidence the change moved
behaviour — block, do not adjust (constitution §Testing, CLAUDE.md house rules).

**SC-4 (US1) — the whole package is unmoved**

```gherkin
Given apps/management-web's full Vitest suite (334 tests, 38 files at HEAD)
When it runs before the change and again after it
Then the same count passes in both runs
```

**SC-5 (US1) — the new bound is reachable, not merely declared**

```gherkin
Given the change applied
When the failing test runs under 24-way CPU contention, ten times
Then it passes ten times
  And no run reports "Test timed out in 30000ms"
```

The second clause is FR-002's own check: without it, a cured `waitFor` failure
reappears as a `testTimeout` failure and the fix looks worse than the defect.

**SC-6 (US1) — the timing-sensitive `user.type` leg is inside the new per-test bound**

```gherkin
Given the change applied
When the failing test runs under contention
Then its reported duration is below the configured testTimeout
```

`user.type` alone measured 1073 ms under contention (§Claim 3). The per-test
budget has to cover every leg, not only the one that failed.

**SC-7 (US2, only if US2 ships) — kiosk-web is unmoved by its own bound change**

```gherkin
Given apps/kiosk-web's full suite
When it runs before the change and again after it
Then the same count passes in both runs
```

**SC-8 (US1) — the lint rule is satisfied without a suppression**

```gherkin
Given the new guard test from FR-003
When "pnpm lint" runs
Then it reports no error
  And the guard carries no eslint-disable comment
```

---

## Independent end-to-end test procedure

Runnable by a human, no Aspire stack, no Docker, no backend. Frontend only.

1. `pnpm install --frozen-lockfile` at the repo root.
2. **Establish the defect exists**, on `origin/develop`:
   ```sh
   cd apps/management-web
   npx vitest run src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx
   ```
   It passes. *That is the point* — the defect is invisible here, which is why a
   green local run is not evidence of anything and must not be cited as such.
3. **Make it visible.** Edit the failing test's mock to
   `fetchMock = vi.fn(async () => { await new Promise((r) => setTimeout(r, 1500)); return failedResolveResponse(); });`
   and re-run. Expect, every time:
   `TestingLibraryElementError: Unable to find an element by: [data-testid="placeholder-preview-error"]` at line 239.
4. **Apply the change** (`configure({ asyncUtilTimeout: 10_000 })` in
   `src/test/setup.ts`, `testTimeout: 30_000` in `vite.config.ts`). Re-run with
   the injection still in place. Expect `Tests 5 passed (5)`.
5. **Prove the guard is a guard.** Put the deadline back to 1000 ms with the
   shipped guard test in place. Expect red. Restore.
6. **Reproduce the real thing.** With the tree at `origin/develop`, run
   ```sh
   node -e "const e=Date.now()+900000;let x=0;while(Date.now()<e){for(let i=0;i<1e6;i++)x+=Math.sqrt(i)}" &   # ×24
   ```
   and run the file ten times. Expect one to three failures with CI's exact
   message. Repeat with the change applied. Expect zero.
7. Read the `frontend` bucket on the PR. **A single green run proves nothing** —
   the test already passes ~95% of the time. Step 6 is the evidence; step 7 is
   only the absence of a new break.

---

## Latency budget impact

**N/A.** This changes test-harness configuration in two React apps and one shared
package. It touches no production code path, and jsdom unit tests are not on any
of constitution §IV's six legs of `event arrival → overlay rendered ≤ 800 ms`.

§VII's dashboard obligation binds implemented legs; this spec implements none and
inherits none.

Worth one sentence because the failing test *is* in the overlay editor: the
`DEBOUNCE_MS = 250` it waits on is an **operator-typing** debounce on the
management console, not the event→overlay path the kiosk wall renders. Changing
nothing about it changes nothing about §IV either way.

---

## Out of scope

- **`apps/shared`** — dropped at the phase-3 gate. **No flake has ever been
  reported in that package**, and a different test-setup shape is not on its own
  a reason to change a file with no observed defect (ADR-0036). Its one
  timing-sensitive suite, `CameraViewerCameraSwap.test.tsx`, already carries
  seven explicit `{ timeout: 10_000 }` annotations, so it is the package that
  needs the global bound least. The inspection is kept under §US3 so the next
  author does not re-derive that the three packages differ.
- **Removing the existing per-site `{ timeout: 10_000 }` annotations** in
  `useSessionExpiry.test.ts` and `CameraViewerCameraSwap.test.tsx`. They become
  redundant under FR-005 but removing them in the same commit would destroy
  SC-7's characterisation baseline. A separate, later cleanup.
- **A global `jest` shim** (`globalThis.jest = vi`) to unlock RTL's fake-timer
  branch. It would change the branch every `waitFor` in three packages takes, and
  the two existing fake-timer tests in the affected file depend on the current
  behaviour. Named in §"Alternatives considered"; it wants an ADR, and ADR-0144
  forbids the lane from writing one.
- **Upgrading `@testing-library/dom`** to a version with first-class Vitest
  fake-timer support. Same reasoning; a dependency bump that changes wait
  semantics across three packages is its own delivery.
- **The ADR-0150 "CI budget job"** — running the frontend suite with every
  settle budget forced low, which ADR-0150 §Alternatives calls "the honest
  upgrade path". Still on the table, still rejected on cost, and this spec does
  not revive it.
- **#2247 / #2409's backend hang.** Answered in Claim 6: different defect,
  already closed, no shared cause.
- **Any production behaviour of the overlay editor.** The panel, the debounce,
  the `settled` gate and the `currentData` choice are all correct and untouched.

---

## Success criteria

| # | Criterion | How it is checked |
|---|---|---|
| **SC-A** | SC-1's guard is observed **red** before the fix, output quoted verbatim in the PR | phase 4a artefact |
| **SC-B** | SC-1 passes after the fix, with the injection unchanged | `vitest run` |
| **SC-C** | SC-2's counterfactual is run: the guard goes red with the deadline at 1000 ms | phase 5 note |
| **SC-D** | SC-3/SC-4 (and SC-7 if US2 ships): every package touched has its suite pass before and after with identical counts and **no edited assertion** | two captured runs per package |
| **SC-E** | SC-5: ten contended runs of the affected file, zero failures, no `testTimeout` | phase 5 note |
| **SC-F** | SC-8: `pnpm lint`, `pnpm typecheck`, `pnpm format:check` clean | CI `frontend` bucket |
| **SC-G** | #2419 closed by the PR with a closing keyword, and its state verified after the merge | MEMORY: a PR mention rarely auto-closes |

---

## Assumptions, marked

- **A-1 — 10 000 ms is enough margin.** It is 31× the *measured* contended cost
  of the failing wait, but nobody has measured a GitHub runner directly; the
  contention model here is 24 busy processes on 8 local cores. Unfalsifiable in
  advance — the honest position is that the guard in FR-003 and the counterfactual
  in SC-2 make a future shortfall *visible* rather than making it impossible. If
  the flake recurs after this, the next step is ADR-0150's CI-budget job, not a
  larger number.
- **A-2 — the contention harness models CI's failure and not a different one.**
  Supported by Claim 5 (identical message, identical line) and not by anything
  stronger. A CI failure is not directly reproducible from here.
- **A-3 — no test anywhere in the three packages depends on a `waitFor`
  *failing* within 1000 ms.** A test asserting that something never appears
  would now take 10 s instead of 1 s but still pass. SC-4 and SC-7's equal-count
  runs are what would catch a violation; the suite runtime delta is the signal
  to watch.
- **A-4 — `configure` from `@testing-library/react` re-exports
  `@testing-library/dom`'s.** Verified by running the injected-failure case with
  only the setup-file change and watching it pass (Claim 4's second row), so this
  is observed rather than assumed.

---

## Phase 4a colour

**Both, and the split is exact.** Ambiguity resolves to red (CLAUDE.md), but this
change is not ambiguous — it has two distinguishable parts.

**RED — FR-003's guard (SC-1).** It asserts behaviour nothing asserts today: that
the advisory survives a late refusal. It must be observed failing against the
unchanged tree, and the verbatim `TestingLibraryElementError` quoted in the PR
body. This has already been run once during phase 1 and does fail
deterministically; phase 4a repeats it as the gate artefact rather than inheriting
this spec's copy.

**CHARACTERISATION, GREEN — everything else (SC-3, SC-4, SC-7).** Raising a
deadline preserves behaviour by construction: a wait that was satisfied at 29 ms
is satisfied at 29 ms regardless of the bound. The five existing tests in the
affected file, and all three packages' suites, are captured **passing before** the
change and must pass **unmodified after**. An assertion that has to be edited is
evidence the deadline change moved behaviour — **block, do not adjust**.

Note what this rules out: "make the flaky test pass" is *not* a red-first task.
There is no failing test to fix — it passes locally, always. Treating the flake
itself as the red observation would mean waiting for CI to fail on its own
schedule, which is why the injection harness exists.

---

## What phase 5 may and may not claim

Stated here because this is the spec's hardest problem and a verification note
that gets it wrong is worse than none.

**May not:** cite a green `frontend` bucket, or a green local run, as evidence.
The test passes ~95% of the time unfixed. Both would have been green on the runs
that produced #2419 and #2520.

**Must:** carry (a) the guard's red output before and green after, (b) the SC-2
counterfactual — the guard red again with the bound restored, and (c) the
contended run counts, before and after, from step 6 of the e2e procedure.

MEMORY's *"self-review catches contradictions, never omissions"* applies: every
figure above goes into `verification.md` as an observed number, not into a
report to the orchestrator.
