# Spec 223 — A 429 a test can provoke

**Issue**: #2441 · **Branch**: `2441-a-429-a-test-can-provoke` · **Phase**: 1 (Specify)
**Date**: 2026-09-23 · **Context**: `EventIngestion` (+ `src/AppHost`, test lane only)
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer`
**Feature bucket**: `specs/104-a-limiter-that-sheds-and-recovers/` §5.2 (where the
gap was first recorded, as **F2**), unblocked by
`specs/175-a-limit-a-test-can-reach/` (#2212, merged)
**ADRs**: ADR-0037 (the phased workflow), ADR-0103 (integration tests run
against the real Aspire stack, no Testcontainers), ADR-0052 / ADR-0053 /
ADR-0054 (xUnit + Shouldly, sentence-style names, hand-written data),
ADR-0143 (the fixture's `POST` is not retried — §5.4 row 5, and it is what makes
this test observable at all), ADR-0142 (the manual path's idempotency wrapper,
which this test deliberately does not engage — §5.5), ADR-0070 (minimal APIs),
ADR-0036 (smallest change; no per-test configuration hook is invented),
ADR-0109 (disjoint files — §11), ADR-0139 / ADR-0144 (two testing obligations;
phase 4a has two colours and no exemption)
**Constitution**: §Testing (the obligation this slice discharges), §IV (latency
budget — **N/A**, see §8), §II (**does not bind** — no domain model is touched;
this slice adds no production type at all)
**New ADR needed**: **No.** See §9.

---

## 0. Provenance: this design was done once already, and is carried, not redone

Issue #2441 was split out of **#2212** at that spec's phase-3 gate, on the
instruction of #2212's own scope note (2026-09-08):

> **Still explicitly out of scope** … the integration test the config knob
> enables. That needs its own red … and it should not ride along on a change
> that cannot demonstrate it.

`specs/175-a-limit-a-test-can-reach/tasks.md`'s out-of-scope table records where
the design went:

> **#2441** — **The integration test off the real endpoint**, with the AppHost
> `isE2ETests` override. Carries the full design: the two-step red, CF-A's
> problem-title injection, and the non-determinism framing.

**So this spec formalises a design that already exists.** The three-step
red-then-green sequence (§6), both counterfactuals (§6.3) and the honest
non-determinism framing (§7) are carried from the issue body verbatim in
substance. Nothing here is a fresh design decision, and where this spec differs
from the issue it says so explicitly and says why — §1.1.

**An earlier draft of #2212's spec carried this test as a P2 story.** It was
removed, not deferred, and the removal was recorded. That record is why this
work is a spec rather than a hole.

---

## 1. The premise, checked by content before specifying

Every claim in the issue body was re-read against the working tree at
`a40d7ad1` (`origin/develop`, fetched 2026-09-23). The issue's own instruction
was *"Verify these still hold before implementing — they were checked on
2026-09-17 and the suite moves."* Six days have passed and the suite did move.

**Every substantive claim holds. Two line numbers have drifted, and one new
repo-wide requirement has appeared that the issue's design predates.**

| Claim | Verdict | Evidence |
|---|---|---|
| The two lines that turn a refused lease into a 429 are at `EventsEndpoints.Writes.cs:344-351` | **Holds, exactly** | `:344` `using IngestWriteLease lease = limiter.TryAcquire();`, `:345` `if (!lease.Acquired)`, `:348` `title: "EVENT_INGEST_BACKPRESSURE"`, `:350` `statusCode: StatusCodes.Status429TooManyRequests`. |
| Nothing above unit level executes them | **Holds** | `StoreOrRefuseAsync` has exactly two call sites (`:105` manual, `:171` webhook) and no integration test issues concurrent writes to either — see the sweep in §5.4. |
| #2212 landed the knob | **Holds** | `src/EventIngestion/Application/Ingress/IngestWriteOptions.cs` exists with `SectionName = "EventIngestion:IngestWrite"` and `Concurrency` defaulting to `IngestWriteLimiter.DefaultConcurrency`. `EventIngestionInfrastructureModule.cs:124` registers the limiter from `IOptions<IngestWriteOptions>.Value.Concurrency`; `:149-150` binds the section. |
| `IngestWriteLimiter.TryAcquire` gates on `slots.Wait(0)` | **Holds** | `IngestWriteLimiter.cs:43-44` — `slots.Wait(0) ? new IngestWriteLease(slots) : IngestWriteLease.Refused`. This is CF-B's injection point. |
| `DefaultConcurrency` is 64 | **Holds** | `IngestWriteLimiter.cs:25`. |
| `AppHost.cs` has an `if (isE2ETests)` block already carrying two `WithEnvironment` lines | **Holds** — **line drift** | The issue cites `:501-514`. It is **`:575-588`** today: the audit retention tick (`AuditObservability__Retention__TickInterval`) and the ingest-breakdown switch (`AuditObservability__Measurement__RecordIngestBreakdown`, spec 109's precedent). Both on `auditObservability`. |
| The `eventIngestion` local is in scope at that point | **Holds** — **line drift** | The issue cites `:405`. It is **`:479`** today, and `eventIngestion` is already named at `:568` inside the `WaitForCompletion(migrations)` loop that immediately precedes the block — so the local is unambiguously in scope. |
| The fixture boots with `E2ETests=true` from a hard-coded parameter array | **Holds** | `tests/Integration.Tests/Fixtures/AspireFixture.cs:317`. The issue cites `:275-281`; it is `:317` today. |
| There is no per-test configuration hook | **Holds** | `AspireFixture` is an `ICollectionFixture`; one boot per assembly. All **136** `[Collection(...)]` declarations under `tests/Integration.Tests/` name `AspireCollection.Name` — one collection, no exceptions. |
| `event-ingestion` is in `TailedResources`, so `RecentLogs("event-ingestion")` is legal | **Holds** | `AspireFixture.cs:117-126` lists it. `LogTailCoverageTests.Every_resource_a_test_asks_for_logs_from_is_tailed` text-scans `RecentLogs("…")` call sites against that list and fails the build on a resource that is not tailed. |
| The fixture's `HttpClient` does not retry a `POST` | **Holds** | `tests/Integration.Tests/Fixtures/FixtureHttpClients.cs:28` — `http.AddStandardResilienceHandler(IdempotentRetry.RetryIdempotentMethodsOnly)` (ADR-0143). |
| `DirectWriteHonestyIntegrationTests` is the right shape to mirror | **Holds** | `tests/Integration.Tests/EventIngestion/DirectWriteHonestyIntegrationTests.cs` — `[Collection(AspireCollection.Name)]`, `aspire.CreateAuthenticatedClientAsync("event-ingestion", Operator, OperatorPassword)`, `op-hamburg@hamburg.test` / `Operator1234`, a run-unique `kind` via `$"Direct{Guid.CreateVersion7():N}"[..20]`, and the `{deviceId, kind, occurredAt, payload}` body shape. Its first `[Fact]` gets a plain `201` with no event-type pre-registration, so the burst needs none either. |

### 1.1 What has changed since the issue's design, and what it costs

**One addition, and it is load-bearing.** `specs/218-ci-shard-slow-jobs/` landed
on 2026-09-22 — *one day* before this spec, and five days after the issue's
design was checked.

`tests/Integration.Tests/ci-shards/shard-{1,2,3,4}.filter` are four committed
files that partition the integration suite by class name, and `ci.yml`'s
`integration-shard-coverage` job re-runs discovery on every push and **fails
loudly if the union stops matching the full test set exactly** — its own comment
says *"A class added later without being added to a shard file makes this job red
instead of silently never running anywhere."*

**So a new integration test class must be assigned to a shard, or it never runs
in CI.** The issue's file list (two files) is therefore **three files**. This is
not a change to the design; it is a file the design could not have known about.
Recorded here rather than discovered at phase 4, because the failure mode
without it is the exact one this whole issue exists to prevent: **a test that
runs nowhere is indistinguishable from a test wired to nothing.**

Assignment and its arithmetic are in §5.3.

### 1.2 One thing the issue's safety table under-counts, checked and harmless

The issue's table says *"Only two [tests that issue concurrent writes] exist."*
A sweep for `Task.WhenAll` / `Parallel.For` across `tests/Integration.Tests/`
returns **fourteen files**, not two. All twelve extra were checked individually
and **none reaches `StoreOrRefuseAsync`** — see §5.4, which replaces the issue's
table with the wider sweep. The conclusion is unchanged; the argument behind it
is now the one that was actually made.

---

## 2. The gap, stated precisely

`IngestWriteLimiter` is thoroughly covered at unit level — spec 104's four
`IngestWriteLimiterTests`, plus spec 175's registration tests. What no test
reaches is the **seam**: the eight lines in the API layer that take a lease and,
when refused, turn that refusal into an HTTP answer with a specific problem
title and a specific status code.

```csharp
// src/EventIngestion/Api/EventsEndpoints.Writes.cs:344-351
using IngestWriteLease lease = limiter.TryAcquire();
if (!lease.Acquired)
{
    return Result<Guid, IResult>.Failure(Results.Problem(
        title: "EVENT_INGEST_BACKPRESSURE",
        detail: "Too many events are being stored at once; please retry.",
        statusCode: StatusCodes.Status429TooManyRequests));
}
```

A unit test on the limiter cannot cover this, because the limiter does not know
what a 429 is. Until #2212 the seam was also *unreachable* from an integration
test: the limiter granted 64 concurrent writes and no fixture could lower that,
so provoking a refusal would have meant a saturating load test — which spec 104
considered and declined, for reasons §7 restates.

**#2212 made the seam reachable. This spec reaches it.**

### 2.1 Why the 201 clause is not decoration

The obvious test — *"under a burst, something answers 429"* — is satisfied by a
limiter stuck refusing **everything**. That is not hypothetical: it is precisely
the `concurrency: 0` failure #2212's `Ensure.That(concurrency).AtLeast(1)` guard
was added to prevent, and this test is the only place it would be visible *from
outside the process*.

So the assertion is two-sided, and both sides are load-bearing:

- **at least one 429** — the limiter sheds.
- **at least one 201** — the limiter is a limiter, not a wall.

---

## 3. User story

### US1 (P1) — A saturated ingest endpoint refuses in a way a caller can act on

*As an operator integrating against direct HTTP ingest, when the service is
saturated I get a `429` carrying the documented problem title
`EVENT_INGEST_BACKPRESSURE` — not a hang, not a generic 500, and not a
blanket refusal of every write — and the system proves it end to end rather
than only in a unit test of a semaphore.*

**The only story.** There is no second slice here: the AppHost line and the test
are one change, because the line exists solely to make the test reachable and
the test is the only thing that observes the line.

**Nothing production-observable changes.** The AppHost line sits inside
`if (isE2ETests)`, which is false in dev and in publish mode. The production
default stays 64.

---

## 4. Acceptance scenarios (Gherkin)

#### AS-1 — The burst is shed, and not everything is shed (happy path, both directions)

```gherkin
Given the Aspire integration fixture is running with EventIngestion write
      concurrency pinned to 1
  And an operator authenticated for the "hamburg" fab with sse.events.write
When eight POSTs to /events/manual are issued concurrently through one Task.WhenAll
Then at least one response is 429
  And that response's problem body has "title" == "EVENT_INGEST_BACKPRESSURE"
  And at least one response is 201 Created
```

**New behaviour at this level, and therefore RED** (§6). Today the AppHost
passes no concurrency, the limiter grants 64, and all eight answer 201 — so
the 429 clause fails. **That failure is the evidence**, and a green here at
step 1 means the test is wired to nothing.

#### AS-2 — An unauthenticated caller gets 401, never 429 (auth)

```gherkin
Given no bearer token
When a POST to /events/manual is issued, concurrency pinned to 1 or not
Then the response is 401 and no lease is ever taken
```

**No new test.** `AnonymousIngestIsRefusedTests` already covers this, and the
limiter sits *behind* `RequireAuthorization(Scope.Sse.Events.Write)`
(`EventsEndpoints.cs:30-31`), so the ordering is structural rather than
incidental. Recorded so the engineer does not duplicate it — **and so that a
redden there under concurrency 1 reads as a finding, not an inconvenience**
(§5.4).

#### AS-3 — A malformed body gets 400, never 429 (bad request)

```gherkin
Given an authenticated operator
  And a body with no payload
When a POST to /events/manual is issued
Then the response is 400 EVENT_INVALID_INPUT
```

**No new test.** `MissingPayloadIsRefusedIntegrationTests` covers it. Body
validation runs before `StoreOrRefuseAsync` is called
(`EventsEndpoints.Writes.cs:85-92`), so a bad request never reaches the
limiter. Same standing as AS-2: listed because it is on the watch list.

#### AS-4 — The fab scoping a write carries is unaffected by the limit (conflict / cross-tenant)

```gherkin
Given an operator authenticated for fab A
When a POST to /events/manual is issued for fab B, concurrency pinned to 1
Then the response is the existing refusal, not a 429 and not a 201
```

**No new test.** `ManualIngestFabScopingIntegrationTests` covers it. It is on
the watch list because it is a write-path test running under the new
stack-wide limit.

#### AS-5 — The rest of the suite still holds under a stack-wide concurrency of 1

```gherkin
Given the whole Aspire integration bucket
When it is run with EventIngestion write concurrency pinned to 1
Then every test that passed before still passes
```

**Characterisation, GREEN.** This is the one clause that guards *this spec's own
worst failure* — a test-lane change that quietly reddens unrelated tests. §5.4
is the argument that it holds; AS-5 is the observation that checks the argument.

**A redden here is a finding to report, never a reason to add a retry** to the
reddened test. If §5.4's reasoning turns out to be wrong, the finding is the
deliverable.

---

## 5. The mechanism, and which existing pattern each piece mirrors

Nothing here is invented. Each piece has a named precedent.

### 5.1 The override — one line, in the block that already does exactly this

There is **no per-test configuration hook and this spec does not add one**
(ADR-0036). The fixture is an `ICollectionFixture` — one AppHost boot per
assembly, from a hard-coded parameter array containing `"E2ETests=true"`
(`AspireFixture.cs:317`). The only channel by which the integration lane
configures a service differently is a `WithEnvironment` line inside
`if (isE2ETests)`, and that block already carries two, one of which
(spec 109's `RecordIngestBreakdown`) is cited as the precedent by its own
comment.

```csharp
// src/AppHost/AppHost.cs — inside the existing if (isE2ETests) block at :575
eventIngestion.WithEnvironment("EventIngestion__IngestWrite__Concurrency", "1");
```

`__` is the .NET configuration provider's section separator, matching the two
lines already in the block and the `EventIngestion__IngestRetry__MaximumRetryWindow`
line at `:492`. It binds to `IngestWriteOptions.SectionName`
(`"EventIngestion:IngestWrite"`) + `Concurrency`.

### 5.2 The test — mirrors `DirectWriteHonestyIntegrationTests`, same folder

New file: `tests/Integration.Tests/EventIngestion/IngestBackpressureIntegrationTests.cs`.

| Element | Taken from |
|---|---|
| `[Collection(AspireCollection.Name)]` | Every integration test. Also what `IntegrationTestSelectionTests` requires of a class carrying no category trait. |
| `aspire.CreateAuthenticatedClientAsync("event-ingestion", Operator, OperatorPassword)` | `DirectWriteHonestyIntegrationTests:31-32`, same operator and password. |
| Body `{deviceId, kind, occurredAt, payload}` | Same file, `:36-42`. |
| Run-unique `kind` via `$"…{Guid.CreateVersion7():N}"[..20]` | Same file, `:35`. Keeps repeated runs from colliding. |
| `aspire.RecentLogs("event-ingestion")` appended to failure messages | The fixture's own diagnostic channel; `event-ingestion` is in `TailedResources`. |
| `ITestOutputHelper` for the observed status distribution | Same file, `:49`. **Write the distribution out** — a figure reported only in conversation is invisible to every later reader. |

Eight requests through **one** `Task.WhenAll`. **No loop, no retry, no
wall-clock bound, no `Task.Delay`.**

### 5.3 The shard assignment — new since the issue, and mandatory

Per §1.1. The new class goes in **`tests/Integration.Tests/ci-shards/shard-3.filter`**,
appended as one more `|`-joined clause:

```
|FullyQualifiedName~SmartSentinelEye.Integration.Tests.EventIngestion.IngestBackpressureIntegrationTests.
```

**Why shard 3.** The four shards hold 32 / 33 / 32 / 34 classes and were balanced
by *discovered test count* at 157 / 157 / 157 / 157 of 628 (README, 2026-09-22).
Shard 3 is tied for fewest classes and is the smallest file; this class
contributes **one** test case, so the balance moves to 157/157/**158**/157 —
inside the noise of a partition whose own README says it is regenerated by hand
only when it drifts. **No rebalance is warranted and none should be attempted**;
re-partitioning four files to absorb one test would be a large, reviewable diff
in service of nothing.

Keep the trailing `.` — every clause in those files carries it, so
`…IngestBackpressureIntegrationTests.` cannot accidentally prefix-match a
future `…IngestBackpressureIntegrationTestsV2`.

**Do not fold a category clause into the filter file.** The README is explicit:
`IntegrationTestSelectionTests` text-parses `ci.yml`'s `--filter` values, and an
earlier version that put the category clause in these files broke that guard
silently.

### 5.4 Why a stack-wide concurrency of 1 is safe — the wider sweep

The issue's table is reproduced with its reasoning re-derived, and **row 2 is
widened**: the issue said two concurrent-write tests exist; a sweep for
`Task.WhenAll` / `Parallel.For` across `tests/Integration.Tests/` returns
fourteen files. All were checked.

| # | Risk | Finding |
|---|---|---|
| 1 | Other integration tests contend for the single slot | **All 136** `[Collection(...)]` declarations under `tests/Integration.Tests/` name `AspireCollection.Name`. One collection ⇒ xUnit runs the whole assembly sequentially ⇒ one in-flight HTTP write at a time ⇒ one slot suffices. |
| 2 | A test that itself issues concurrent writes **to this endpoint** | **None.** Of the fourteen `Task.WhenAll`/`Parallel` files: `EventTypeRegistryConcurrencyIntegrationTests` hits `/event-types` (never `StoreOrRefuseAsync`); `IngestThroughputMeasurementTests` publishes over **MQTT**, which does not touch the limiter at all; the four `AuditObservability` ones (`AuditHandoverLegTests`, `AuditHandoverPopulationTests`, `IngestSpanMeasurement`, `IngestThroughputTests`) drive **SystemVariables** set operations and are all `[Trait("Category","Measurement")]`; the remaining seven are `CameraCatalog`, `LayoutComposition`, `OverlayDesigner`, `SystemVariables`, `ServiceDefaults` and the fixture itself — **different services, each with no `IngestWriteLimiter`**. |
| 3 | The limiter is reached by some path other than the two HTTP writes | **No.** `grep IngestWriteLimiter\|TryAcquire src/` gives exactly: the type itself, `IngestWriteOptions`'s doc reference, the registration, and `EventsEndpoints.Writes.cs` (`:39`, `:114`, `:126`, `:339`, `:344`). `StoreOrRefuseAsync` has two call sites, `:105` (manual) and `:171` (webhook). MQTT ingest never enters it. |
| 4 | A latency test serialised by the lowered limit | `AcceptToDecideLatencyTests` is `[Trait("Category","Measurement")]` and run-mode; `ci.yml` excludes `Category!=Measurement` at every integration call site. It never runs under this stack. |
| 5 | The fixture's `HttpClient` retries the 429 away | **No — and this is what makes the test observable at all.** `FixtureHttpClients.cs:28` applies `IdempotentRetry.RetryIdempotentMethodsOnly` (ADR-0143), so a `POST` gets **one** attempt. Under the library's default outcome-based predicate the resilience handler would have retried the 429 until it succeeded, and the test would have failed for a reason with nothing to do with the limiter. |
| 6 | Dev and production affected | **No.** The line is inside `if (isE2ETests)`; `isE2ETests` is `false` unless the `E2ETests` parameter is set, which only the fixture sets. |
| 7 | The four CI shards each boot a stack and contend | **No.** Each shard is a separate runner with its own AppHost; concurrency 1 applies per stack, and each stack serves one sequential test class at a time. |

**Row 1 is the load-bearing one, and it is also why AS-5 exists**: the argument
is sound but it is an argument, and the whole-bucket run is what checks it.

### 5.5 What the manual endpoint does around the seam, and why it does not interfere

`IngestManual` wraps `StoreOrRefuseAsync` in
`IdempotentRequest.ExecuteCreateAsync` (ADR-0142,
`EventsEndpoints.Writes.cs:99-106`). Checked rather than assumed, because a
wrapper that rewrote failures would invalidate the title assertion:

`IdempotentRequest.cs:132-135` maps the inner result with
`onFailure: IdempotentOutcome.NothingCreated` — its own parameter doc says *"the
created identifier **or a problem to return as-is**"*. The 429 reaches the
caller unchanged.

**The test sends no `Idempotency-Key`**, so no key is claimed, nothing is scoped
per-caller, and the eight requests are eight independent creates. This is the
documented opt-in shape (ADR-0142: *"no key, no change"*) and keeping it that way
is deliberate — an idempotency key on a burst would introduce a second mechanism
into the one seam this test exists to isolate.

---

## 6. Phase-4a colour: **RED**, and the three-step sequence is the evidence

**RED**, unambiguously, and ADR-0144's ambiguity rule does not even need to be
invoked: AS-1 fails today.

This is the anti-pattern #2212 named by hand — *"a test that passes against a
limiter wired to nothing is exactly the shape this repository keeps having to
correct."* The order is what distinguishes the two, and it is **three separate
steps, not one**:

| Step | State of the tree | Run | Expected |
|---|---|---|---|
| **1** | `develop` + #2212, **`AppHost.cs` line absent**, test present, shard file updated | the new test | **RED.** The AppHost passes no value, the limiter grants 64, eight POSTs all answer 201, and the 429 clause fails. **Capture verbatim.** |
| **2** | the `AppHost.cs` line added | the new test | **GREEN.** **Capture verbatim.** |
| **3** | — | — | **Both captures go in the PR body** (ADR-0139). |

**A green at step 1 means the test is wired to nothing — stop and report.** It
is not a shortcut and it is not a lucky start; it means the burst provoked a
429 the limiter did not issue, or the test is asserting something that is
already true, and either way the phase-4 gate is not satisfied.

**The shard file goes in before step 1, not after.** A class absent from every
shard runs nowhere, and "nowhere" is a third outcome that looks like neither red
nor green.

### 6.1 What the step-1 red must say

Not merely *"failed"*. The captured output must show **the observed status
distribution** — eight 201s and no 429 — because that distribution is the
statement *"the limiter granted 64 slots"* in the test's own words. A red whose
message does not distinguish "no 429 appeared" from "the test threw" is a red
that proves nothing, so the assertion carries its own diagnostic:

- the count of each status code observed, and
- `aspire.RecentLogs("event-ingestion")`.

### 6.2 Then the whole bucket, not just the new test

E2E write concurrency is now **1 stack-wide**, and §5.4 is an argument that
wants checking. After step 2, run the **entire** Aspire integration bucket
(all four shards' worth). AS-5. **A redden is a finding to report, not a reason
to add a retry.**

The four the issue names as most likely — all write-path or auth-path tests on
this endpoint — are the priority read: `EventTypeRegistryConcurrencyIntegrationTests`,
`ManualIngestFabScopingIntegrationTests`, `MissingPayloadIsRefusedIntegrationTests`,
`AnonymousIngestIsRefusedTests`. They sit in shards 2, 3, 4 and 2 respectively,
so a single-shard run does not cover them.

### 6.3 Defect-injection counterfactuals (phase 5, after green)

A red from absent wiring proves the test *notices* the feature. It does not
prove the test asserts what it claims. Two injections, each applied to `src/`,
observed, then reverted with `git checkout -- src/`; then confirm
`git diff --stat src/` is empty.

| # | Injection | Predicted result | What it proves |
|---|---|---|---|
| **CF-A** | `EventsEndpoints.Writes.cs:348` — change `title:` to `"EVENT_INGEST_OVERLOAD"` | **Red on the title assertion**; the **429 status still appears** | The test asserts the **contract**, not merely a status code. Without this it would pass against any 429 from anywhere — the API gateway's own rate limiter included. The *shape* of the failure is the finding: status clause green, title clause red. |
| **CF-B** | `IngestWriteLimiter.cs:43-44` — drop the `slots.Wait(0)` gate so `TryAcquire` always grants a lease | **Red with no 429 at all** — the same shape as the step-1 red | The 429 comes from **this limiter**, not from the server shedding load some other way (Kestrel queue limits, a gateway, a proxy). |

**Predictions are written before the runs. A mismatch is reported, never edited
into agreement.**

**CF-A and CF-B fail differently, and that difference is the point.** CF-A must
leave the status clause green — if CF-A reddens the status assertion too, the
test is coupling the two clauses and the assertion needs splitting, which is a
finding. CF-B must redden the status clause — if a 429 survives CF-B, something
other than the limiter is producing it, which is a larger finding and a stop.

### 6.4 The counterfactual this spec deliberately does **not** run

Injecting `Concurrency = 0` to watch the guard fire. Spec 175's R1 covers it at
unit level, and a stack whose limiter refuses every write would redden a dozen
unrelated integration tests for no added information — the same reasoning spec
175 §6.4 recorded, and it still holds.

---

## 7. Determinism, stated honestly

**This test is not formally deterministic and must not be described as such**,
in the spec, the test's doc comment, or the PR body.

With concurrency 1, a 429 requires two requests inside `StoreOrRefuseAsync` at
the same instant. Eight `PostAsJsonAsync` calls awaited through one
`Task.WhenAll` are dispatched within microseconds of each other, and each holds
its lease across a real Postgres round-trip of single-digit milliseconds — so
overlap is **overwhelming**. It is not guaranteed.

**It is a different risk class from the load test spec 104 declined**, and that
difference is the whole justification for writing this one:

| | spec 104's `[T091]` load test | this test |
|---|---|---|
| Concurrent in-flight writes needed | **64** | **2** |
| Load generator | 5 000 ev/s for 30 s | 8 POSTs, once |
| Runtime | ~30 s | well under a second |
| Fails when | the runner is slow, the DB is fast, the burst disperses | only if the server serialises eight simultaneous requests end to end |

**If it ever flakes, raise the request count. Never loop until a 429 appears.**
A loop turns a real regression — the limiter stops shedding — into a slow pass,
which is strictly worse than the flake it removes.

**No `[Trait("Category", "Measurement")]`.** This is not a measurement test; it
asserts a contract, not a figure. Tagging it Measurement would exclude it from
every CI integration call site (`Category!=Measurement`, `ci.yml:357`, `:496`,
`:504`) and defeat the entire issue. `IntegrationTestSelectionTests` is
satisfied by the `[Collection(AspireCollection.Name)]` declaration alone.

---

## 8. Latency budget

**N/A.** No leg of constitution §IV is touched.

The event-to-overlay path's first leg is *camera → SFU*; direct HTTP ingest is
on none of the six legs — the same reading spec 104 and spec 175 both recorded
for this same component.

More to the point, **the production instruction stream is byte-for-byte
unchanged**. This spec adds no production code. Its one non-test line is inside
`if (isE2ETests)`, which is false in dev and in publish mode, so the production
limiter is still constructed with 64 and the per-request path (`TryAcquire`, the
lease, the release) is exactly what shipped yesterday.

---

## 9. Why no ADR is needed

ADR-0144 forbids the autonomous lane writing an ADR, so this was checked rather
than assumed. Every piece already has a decision on the books:

- **Integration tests against the real Aspire stack** — ADR-0103. This adds one
  test to the existing fixture; it starts no container and adds no package.
- **The `isE2ETests` environment override** — an established mechanism with two
  live call sites in that very block, spec 109's being cited as precedent by its
  own comment. No new mechanism is introduced.
- **The `POST` retry posture the test depends on** — ADR-0143, already applied
  by the fixture.
- **Test framework, naming and data** — ADR-0052 / ADR-0053 / ADR-0054.

**What would need an ADR, and is therefore excluded**: changing the production
default from 64 (§11), and introducing a per-test configuration hook into the
collection fixture (a new mechanism, ADR-0036). If the phase-4 engineer finds
itself wanting either, that is a **stop and a report**, not a judgement call.

---

## 10. Independent end-to-end test procedure

Run by a human against a stack this spec's tests did not boot. The point is to
observe the 429 contract from outside the test that asserts it.

**One machine, one Aspire stack** — stop any running AppHost first; two
concurrent boots give `FailedToStart` that reads exactly like a code defect.

1. Stop any running AppHost. Start the dev stack: `dotnet run --project src/AppHost`.
   Note that `isE2ETests` is **false** here, so the new line does not apply — which
   is itself the observation that production is untouched.
2. Mint an operator token for `hamburg` from **Aspire's proxied Keycloak
   endpoint** (not the container's mapped port, or everything 401s) and
   `POST /events/manual` once. **Expect `201`.**
3. Stop the stack. Add
   `"EventIngestion": { "IngestWrite": { "Concurrency": 1 } }` to
   `src/EventIngestion/Api/appsettings.Development.json`. Restart.
4. Issue eight concurrent `POST /events/manual`. **Expect at least one `429`**
   whose problem body carries `"title": "EVENT_INGEST_BACKPRESSURE"`, **and at
   least one `201`**. This is AS-1 observed by hand, against a stack the test
   did not configure.
5. Remove the configuration. Restart. Repeat step 2. **Expect `201`** — nothing
   was left behind.
6. Confirm the AppHost line is genuinely test-lane-only: `grep -n
   'IngestWrite__Concurrency' src/AppHost/AppHost.cs` and check the enclosing
   block is `if (isE2ETests)`.

**Record every figure observed, in the verification note, as observed** — not
only to the orchestrator. A measurement reported only in conversation is
invisible to every later grep and every later reviewer.

**Check the stack's age before trusting step 4.** A persistent AppHost serves
whatever binaries were loaded at boot; compare the process start time against
the commit before reading a manual `curl` as evidence.

---

## 11. Out of scope

| Item | Why not |
|---|---|
| **The webhook path's 429** (`EventsEndpoints.Writes.cs:171`) | Same two lines, **different auth shape** — a registered integration's bearer rather than an operator's OIDC token, so it needs its own fixture setup. Recorded so its absence reads as a decision. To file if wanted. |
| **Changing the production default from 64** | #2212 excludes it in bold and so does this. A sizing decision on a 250-camera target, wanting a measurement — spec 175 filed it as **F1**. |
| **The MQTT path's `Wait`** | **#2211.** It does not touch `IngestWriteLimiter` at all (§5.4 row 3). A design question with no ADR, and the lane may not write one. |
| **A non-zero acquisition timeout** | Spec 175 **F3**. `slots.Wait(50)` passes all four existing unit tests; changing the gate is a behaviour change. |
| **Rebalancing the four shard files** | §5.3. One added test case is inside the noise of a hand-maintained partition. |
| **A per-test configuration hook on `AspireFixture`** | ADR-0036. The `isE2ETests` block is the established channel and it suffices. |
| **`src/EventIngestion/Api/EventsEndpoints.Writes.cs`** | **Read-only. It is the code under test.** Editing it to make the test pass is editing the subject to fit the measurement. The only edits it may receive are CF-A's injection and its revert. |
