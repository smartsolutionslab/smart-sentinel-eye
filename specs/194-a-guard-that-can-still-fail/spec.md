# Spec 194 — A guard that can still fail

**Issue:** [#2293](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2293)
(`bug`, `tech-debt`, `agent:ready`; Project #13, status Todo)
**Branch:** `2293-persistence-loop-window-fix`
**Phase:** 1 (Specify) — ADR-0037
**ADRs:** ADR-0150 (primary), ADR-0139, ADR-0144, ADR-0052, ADR-0036, ADR-0037
**Constitution:** §Testing ("New behaviour", "Behaviour-preserving refactors", "Waiting")

---

## Summary

`tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs`
contains a guard against head-of-line blocking in the ingest persistence loop
that **passes whether or not the loop head-of-line blocks**. It takes the
harness's default 500 ms retry window, so a defective loop is unblocked by the
poison delivery being *abandoned* — at 500 ms, twenty times inside the test's own
10 s deadline. The test then observes the healthy event stored and goes green,
having proved nothing.

The same harness default makes a second test race a wall clock it has no reason
to race, and it flaked once under parallel load on 2026-09-11.

Both are fixed by giving each test the retry window its own claim requires. **No
production file changes.**

### What was verified before this spec was written

The issue's line numbers and claims were checked against the current tree
(all still accurate) and its central claim was **established by reproduction, not
by argument**, in the manner ADR-0150 §Context sets as the standard.

Four runs of `PersistenceLoopHostedServiceTests`, on this branch at `87ef03e5`:

| | loop mutated to head-of-line block | current (unmodified) loop |
|---|---|---|
| **test as it stands** (500 ms default) | **PASS** — 8/8, 5 s | PASS — 8/8, 1 s |
| **test with `Window = 30 s`** | **FAIL** — 1 failed, 7 passed, 11 s | **PASS** — 8/8, 1 s |

The top-left cell is the defect the issue reports, now demonstrated rather than
inferred: a loop with the exact defect this test names is waved through by it.
The bottom-right cell is the finding that shapes everything below.

> **There is no production bug.** The loop genuinely does move past a poisoned
> delivery. Changing only the window to 30 s leaves the test **green** against
> the current tree. The red this change needs therefore cannot come from the
> tree as it stands — it can only come from a counterfactual, and §Phase-4a
> colour below is about how that discharges the red-first obligation.

The mutation used was head-of-line blocking in `RetryAsync`
(`src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs:204`):
stop attempting deliveries after the first `Outcome.Failed` and carry the
remainder untried. It is the shape the test's own docstring names —
*"Spec 018 fixed that defect; this is where it could come back."*

Worth recording: under that mutation the **sibling** test
`An_event_arriving_behind_a_failing_one_does_not_wait_for_it` (`:104`) still
**passes**. The two tests are not redundant. The sibling guards the
arrivals-versus-retries interleaving across cycles; `:81` guards the fallback
*within* a failed batch. Only `:81` can see this defect, which is exactly why it
mattering is worth the change.

---

## User stories

### US-1 (P1) — The head-of-line guard can fail for the reason it names

**As** a maintainer of the ingest persistence loop,
**I want** `One_delivery_that_never_stores_does_not_hold_up_the_others` to fail
when the loop holds healthy deliveries behind a poisoned one,
**so that** its green is evidence that spec 018's defect has not come back,
rather than a restatement of the retry window.

Today the test cannot distinguish the two. Its sibling at `:104` was given
`Window = 30 s` with a comment stating precisely this reasoning (`:116-119`) and
the correction was never carried next door.

### US-2 (P2) — The retry test does not race a clock it has no claim on

**As** a maintainer running the suite under parallel load,
**I want** `Retries_a_failed_delivery_and_acknowledges_nothing_until_it_lands`
to be decided by whether the loop retries, not by whether the machine was busy,
**so that** a red in this test means a regression.

It is the only test in the repo that must complete
`FailuresBeforeSuccess = 2` retries inside a **wall-clock** 500 ms budget —
`AdvancingClock.UtcNow => Now + Stopwatch.Elapsed` (`:430-436`) is real elapsed
time, not a `TimeProvider`. Its `Task.Delay`-based siblings sleep *past* a
window; load makes them more reliable. This one is alone in its shape, and load
makes it worse.

### US-3 (P3) — The harness default stops being a trap

**As** the next person to add a test to this harness,
**I want** the 500 ms default to say which tests may take it,
**so that** the mistake this spec corrects is not made a third time.

The root cause is not the two tests. It is that `RetryWindow = 500 ms`
(`:247-252`) is the *default*, so a test inherits abandonment-as-unblocker by
saying nothing at all, and the opt-out (`Window`) is what needs remembering.

### Why one slice and not three

The slicing rule asks for the smallest independently-shippable vertical. Here
**all three stories edit one file**, so splitting them produces branches that
collide on `PersistenceLoopHostedServiceTests.cs` and cannot be parallelised
anyway (ADR-0109). US-2 and US-3 are one line each. They ship together.

---

## Acceptance scenarios

### AS-1 (US-1) — happy: the corrected guard passes against the correct loop

```gherkin
Given the persistence loop as it stands on develop
  And One_delivery_that_never_stores_does_not_hold_up_the_others sets Window = 30 s
When the test runs
Then the healthy delivery is stored
  And it is stored without the poisoned delivery having been abandoned first
  And the test passes
```

### AS-2 (US-1) — the conflict case, and the whole point: red under the defect

```gherkin
Given PersistenceLoopHostedService.RetryAsync is mutated to stop attempting
      deliveries after the first Outcome.Failed and carry the remainder untried
  And One_delivery_that_never_stores_does_not_hold_up_the_others sets Window = 30 s
When the test runs
Then the healthy delivery is never stored inside the 10 s deadline
  And the test fails on healthy.Stored
  And the verbatim failure is quoted in the PR body
```

### AS-3 (US-1) — the counterfactual that justifies the change at all

```gherkin
Given the same mutated loop
  And the test as it stands today, taking the 500 ms default
When the test runs
Then it passes
  And that pass is the defect this spec closes
```

AS-3 is not decoration. Without it, AS-2 proves only that the new test can fail;
it does not prove the **old** one could not. Together they are the claim.

### AS-4 (US-2) — the flake, made deterministic

```gherkin
Given Retries_a_failed_delivery_and_acknowledges_nothing_until_it_lands
  And the retry window is squeezed to 1 ms
When the test runs
Then the delivery is abandoned before its third attempt
  And the test fails with "completion.Stored should be 1 but was 0"
```

Already observed on this branch — that is the exact signature of the
2026-09-11 flake, reached deterministically instead of by waiting for load.

### AS-5 (US-2) — happy: widened, it is decided by the loop

```gherkin
Given the same test with Window = 30 s
When the test runs
Then Attempts is 3 — two failures then the write that succeeded
  And Stored is 1 and Abandoned is 0
  And no assertion has been changed
```

### AS-6 (all) — the neighbours are untouched

```gherkin
Given the six tests in this file that this spec does not edit
When the suite runs after the change
Then all six pass unmodified
  And Records_and_releases_a_delivery_that_never_stores still completes fast,
      because it is the case the 500 ms default exists for
```

### AS-7 (bad-request analogue) — no production file is modified in the delivered diff

```gherkin
Given the counterfactual mutation of AS-2 and AS-3 has been applied and reverted
When `git diff HEAD --stat -- src/` is run before committing
Then it reports nothing
  And the restored baseline is re-run after touching the reverted file,
      so a skipped rebuild cannot pass off stale binaries as a clean revert
```

### Auth / scope

**N/A.** No endpoint, no token, no scope, no fab authorization. The change is
confined to an xUnit test class whose collaborators are all hand-written fakes
(ADR-0052, ADR-0054); nothing crosses a trust boundary.

---

## Independent end-to-end test procedure

A reviewer can reproduce every claim above without reading the diff. From the
worktree root:

1. **Baseline.**
   `dotnet test tests/EventIngestion.Infrastructure.Tests/SmartSentinelEye.EventIngestion.Infrastructure.Tests.csproj --filter "FullyQualifiedName~PersistenceLoopHostedServiceTests"`
   → 8 passed, ~1 s.
2. **Apply the mutation** of AS-2 to `RetryAsync`
   (`src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs:204`).
3. **Run again on the delivered tests** → `One_delivery_that_never_stores_does_not_hold_up_the_others` fails.
4. **`git stash` the test file only**, keeping the mutation, and run again →
   it passes. That pass is the bug.
5. **Restore both files**, `touch` them, run once more → 8 passed.

Step 5's `touch` is not superstition: restoring a file can leave it with an
older timestamp than the compiled output, MSBuild skips the rebuild, and the
run then reports the state of binaries nobody has.

No Docker, no Aspire fixture, no database. The whole class is in-memory
(ADR-0103 does not apply — there is nothing to integrate against).

---

## Locked technical choices

| Concern | Choice | Authority |
|---|---|---|
| Test framework | xUnit + Shouldly, hand-written fakes | ADR-0052 |
| Test naming | unchanged — sentence-style with underscores | ADR-0053 |
| Time seam in the loop | **`IClock`, unchanged**; no `TimeProvider` introduced | see below |
| Waiting | deadline poll on the condition; one failure bound per test | ADR-0150 |
| Fix for US-2 | widen the window, **not** a `TimeProvider` | see below |
| Production code | **untouched** | this spec |

### Why the window and not a `TimeProvider`

The issue offers both. The window wins on evidence, not preference:

- **There is no fake `TimeProvider` in this repository.** `Microsoft.Extensions.TimeProvider.Testing`
  is referenced by no project. The three tests that mention `TimeProvider` pass
  `TimeProvider.System` — real time
  (`MqttConnectionLoopTests.cs:529`, `MosquittoConnectionFactoryTests.cs:224`,
  `AuditRetentionHostedServiceTests.cs:65`). The repo's time seam in tests is
  ~15 hand-written `IClock` fakes (ADR-0052, ADR-0054). A fake `TimeProvider`
  would be new infrastructure for one test.
- **The loop takes `IClock`, not `TimeProvider`.** Swapping it is a production
  signature change made for a test's convenience — ADR-0036's "no speculative
  generality" and "smallest possible change" both point the other way.
- **It would not even remove the race.** The budget is spent in
  `clock.UtcNow` *and* in `await Task.Delay(backoff, cancellationToken)`
  (`PersistenceLoopHostedService.cs:143`), which is real time regardless of the
  clock. Virtualising one half leaves the race intact; virtualising both means
  `Task.Delay(backoff, timeProvider, ct)` in production plus a pump in the test.
- **It would break three neighbours.** `AdvancingClock`'s docstring
  (`:425-429`) records why the clock advances at all: *"a frozen clock would
  make the bound unreachable and the abandon test would hang rather than fail —
  which is how a bound that never fires gets shipped."* A fake time source
  re-opens exactly that.

**Widening is not weakening a gate.** ADR-0144 forbids reaching green by
raising a threshold, and this must be shown not to be that. The test asserts
`Abandoned == 0`; the window's only effect is to *cause* abandonment. Widening
it therefore removes a way for the test to fail **for a reason it does not
assert**, and touches none of the three assertions — `Stored == 1`,
`Abandoned == 0`, `Attempts == 3`. `Attempts == 3` is the load-bearing one and
is untouched. Per ADR-0150, the test keeps exactly one failure bound: the 10 s
deadline poll it already had.

A second, smaller gain: today a genuine retry regression fails this test as
`Stored == 0, Abandoned == 1`, which reads as an abandonment problem. After the
change it fails as `Stored == 0, Abandoned == 0` — "it never stored", which is
what actually went wrong.

---

## Phase-4a colour — both findings are RED

ADR-0144 gives two colours and says ambiguity resolves to red. This case is
nuanced enough to be worth arguing rather than asserting.

**Characterisation is unavailable here, by construction.** The
behaviour-preserving obligation requires covering tests that pass **unmodified**
before and after, and states that an assertion which has to be edited is
evidence the behaviour moved. In this spec the tests *are* the artefact being
edited. A test cannot characterise itself.

**US-1 is behaviour-changing on the guard.** Nothing in `src/` changes, but
after the change the test asserts something it did not assert before: that the
healthy delivery is stored *without* the poison having been abandoned out of the
way. That is a new claim, and ADR-0139 and constitution §Testing say a new claim
must be **observed failing** before it is trusted.

**The red comes from a counterfactual, and that is the honest form of it.** The
tree is correct, so the test cannot go red against it — proved above. The red is
produced by mutating the loop into the defect the test names, and is quoted in
the PR. This follows the method ADR-0150 §Context itself used ("established by
reproduction rather than argument"; the budget ladder). **Both halves must be
quoted**: the new test red under the mutation, and the old test green under the
same mutation. The second is what makes the change worth making.

**US-2 is also red, by a cheaper counterfactual.** Its assertions do not change,
which argues for green — but the test is still edited, so characterisation's
contract cannot be met, and ADR-0144's tie-break applies. Fortunately the red is
deterministic and costs nothing: squeeze the window to 1 ms, observe
`completion.Stored should be 1 but was 0`, restore. That is strictly better
evidence than a green would have been, because it shows the failure mode the
change removes.

**The characterisation obligation still applies — to the other six tests.** They
are not edited and must pass unmodified before and after. Both obligations hold
at once, over different populations.

**US-3 is a comment.** No colour; covered by the suite staying green.

---

## Latency budget impact

**N/A.** No file under `src/` is modified, so no leg of the
`event arrival → overlay rendered ≤ 800 ms` budget is touched. The
`PersistenceLoopHostedService` is the durable-ingest path and this spec does not
change a line of it; §VII's dashboard obligation for implemented legs
(ADR-0117) is unaffected because nothing about any leg changes.

---

## Out of scope

- Any change to `PersistenceLoopHostedService.cs`. The counterfactual mutation
  is applied and reverted inside phase 4a and must not appear in the diff.
- Introducing `TimeProvider` or a fake time source to this context. Argued
  against above; if it is ever wanted, it is a decision, not a test fix.
- Inverting the harness default so 30 s is the default and 500 ms the opt-in.
  Considered and rejected: it churns four call sites and obsoletes two accurate
  docstrings to save one line. Recorded here so the next reader knows it was
  weighed. **Residual risk accepted:** a future test still inherits 500 ms by
  saying nothing; US-3's comment is the mitigation, and it is a mitigation by
  documentation, which this repository knows is the weak kind.
- The `S107` analyzer warning on `PersistenceLoopHostedService`'s 5-parameter
  constructor. Advisory (ADR-0084), pre-existing, not this spec's business.

---

## Assumptions and flags

- **Assumption (marked):** the 2026-09-11 flake in US-2's test was abandonment
  by window expiry. It was observed once and not captured. The 1 ms
  reproduction shows that mechanism produces *exactly* the reported failure
  signature, which is strong but not proof that it was the mechanism on the
  day. Widening the window is correct regardless: the test has no claim on that
  window either way.
- **Flagged for a human decision — not blocking this spec.** ADR-0144's two
  colours both assume the test's *subject* is what changes. Neither covers "a
  test-only change whose red can only be produced by temporarily mutating
  correct production code". This spec resolves it as red-by-counterfactual,
  which satisfies the intent of ADR-0139 and constitution §Testing (a failure
  was observed and is quoted). If that reading is to become the rule, it wants
  an ADR — and **the autonomous lane may not write one** (ADR-0144). Raised
  here for Heiko; this delivery does not depend on the answer.

## Gate — phase 1

No `[NEEDS CLARIFICATION]` remains. Hand back for review before phase 2.
