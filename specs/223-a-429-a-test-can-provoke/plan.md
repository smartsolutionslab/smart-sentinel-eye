# Spec 223 — Plan

**Phase**: 2 (Plan) · **Date**: 2026-09-23 · **Issue**: #2441
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer` (phase 6)
**Scope**: US1 only. Three files, one of them new, **no production code**.

---

## 1. Bounded context and layers

One context, `EventIngestion` — and it is **not modified**. This slice touches
the composition root's test lane and the integration test project.

| Layer | Change |
|---|---|
| **Domain** | **None.** |
| **Application** | **None.** `IngestWriteLimiter` and `IngestWriteOptions` are used as #2212 shipped them. |
| **Infrastructure** | **None.** The options binding at `EventIngestionInfrastructureModule.cs:149-150` is the mechanism this slice exercises; it is not edited. |
| **Api** | **None — and explicitly read-only.** `EventsEndpoints.Writes.cs` is the code under test. |
| **AppHost** | **One line**, inside the existing `if (isE2ETests)` block. Test lane only. |
| **Tests** | One new integration test class; one shard filter file appended to. |

**This is the whole architectural statement of the slice**: it adds no type, no
interface, no registration, no configuration key that ships. What it adds is
**reach** — an existing production seam becomes observable from outside the
process for the first time.

### 1.1 Why the AppHost is the right place, and the only place

`AspireFixture` is an `ICollectionFixture`: one AppHost boot per test assembly,
driven by a hard-coded parameter array (`AspireFixture.cs:317`). A test cannot
influence the configuration of a service in that stack — there is no hook, and
adding one would be a new mechanism in a shared fixture to serve one test
(ADR-0036).

The `if (isE2ETests)` block at `AppHost.cs:575-588` is the established channel
and already carries two lines of exactly this kind. Spec 109's
`RecordIngestBreakdown` override is the closest precedent and its own comment
states the reasoning this slice reuses: *"a run that has to remember a shell
export is a run whose breakdown silently reports zeros, so the fixture turns it
on rather than asking."*

---

## 2. Entities, value objects, invariants

**None.** No domain model is touched, so constitution §II does not bind and
`PrimitiveBoundaryTests` has nothing new to see. The one configuration value is
an `int` on `IngestWriteOptions`, shipped by #2212 and unchanged here.

**The invariant this slice observes** — it does not add it — is the one
`EventsEndpoints.Writes.cs:344-351` encodes:

> A write that cannot take a lease is answered `429` with problem title
> `EVENT_INGEST_BACKPRESSURE`, and a write that can take one proceeds.

Stated as an invariant because both halves are asserted (spec §2.1): a limiter
that refuses everything satisfies the first clause and violates the second.

---

## 3. Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` change.
The manual ingest path publishes what it always published; this slice observes a
request that is refused *before* anything is stored, so nothing is published on
the path under test at all.

---

## 4. Boundary rules

- **No cross-context project reference.** `Integration.Tests` already references
  the EventIngestion assemblies it needs; this class needs **none of them** —
  it speaks HTTP and reads JSON. No `.csproj` edit.
- **No `Shared.Contracts` change.**
- **NetArchTest** — unaffected; `tests/Architecture.Tests/` is not modified.
  It is still run (T009) because a redden there would be a finding.
- **The read-only boundary is itself a rule here.** Any edit under
  `src/EventIngestion/` is out of scope and a stop-and-report. The only
  exception is CF-A's transient injection at phase 5, which is reverted and
  verified reverted.

---

## 5. Files

### Production (test lane only)

| File | Change |
|---|---|
| `src/AppHost/AppHost.cs` | **One line** appended inside the existing `if (isE2ETests)` block at `:575-588`: `eventIngestion.WithEnvironment("EventIngestion__IngestWrite__Concurrency", "1");` plus a comment saying *why* (the seam is otherwise unreachable), per the house rule that comments say why. |

### Tests

| File | Change |
|---|---|
| `tests/Integration.Tests/EventIngestion/IngestBackpressureIntegrationTests.cs` | **New.** One `[Fact]`. |
| `tests/Integration.Tests/ci-shards/shard-3.filter` | **One clause appended.** Mandatory — spec §1.1 / §5.3. |

### Explicitly read-only

| File | Why |
|---|---|
| `src/EventIngestion/Api/EventsEndpoints.Writes.cs` | **The code under test.** CF-A injects into `:348` at phase 5 and reverts. |
| `src/EventIngestion/Application/Ingress/IngestWriteLimiter.cs` | **The code under test.** CF-B injects into `:43-44` at phase 5 and reverts. |
| `src/EventIngestion/Application/Ingress/IngestWriteOptions.cs`, `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs` | #2212's deliverables. Used, never edited. |
| `tests/Integration.Tests/Fixtures/AspireFixture.cs`, `FixtureHttpClients.cs` | The shared fixture. A change here would affect 136 test classes to serve one. |
| The other three `shard-*.filter` files | §5.3 — no rebalance. |

**Anything outside the three files above is a stop-and-report.**

---

## 6. Test design

### 6.1 The one `[Fact]`

```
A_burst_past_the_write_limit_is_shed_with_the_documented_problem_title
```

Sentence-style with underscores (ADR-0053). The name carries both clauses,
because the test asserts both.

Shape, mirroring `DirectWriteHonestyIntegrationTests` in the same folder:

1. One authenticated client:
   `await aspire.CreateAuthenticatedClientAsync("event-ingestion", "op-hamburg@hamburg.test", "Operator1234")`.
   **One client for all eight requests** — eight clients would each carry their
   own connection pool and their own token mint, adding dispersion for nothing.
2. A run-unique `kind`: `$"Backpressure{Guid.CreateVersion7():N}"[..20]`.
3. Eight `PostAsJsonAsync("/events/manual", …)` tasks built **first**, then one
   `await Task.WhenAll(...)`. Building the task array in a `Select` and awaiting
   the whole thing is what dispatches them together; awaiting inside a loop would
   serialise them and the test would assert nothing.
4. **No `Idempotency-Key` header** (spec §5.5).
5. Assert: at least one `429`; that response's `title` is
   `EVENT_INGEST_BACKPRESSURE`; at least one `201`.
6. `output.WriteLine` the observed status distribution — always, pass or fail.

**No `Task.Delay`, no retry loop, no wall-clock assertion, no `Category` trait.**

### 6.2 The failure message is part of the design

Spec §6.1: a red that says only "failed" proves nothing. Both assertions carry a
Shouldly `customMessage` containing:

- the **observed status distribution** (e.g. `201×8, 429×0`), which is the
  statement *"the limiter granted 64 slots"* in the test's own words, and
- `aspire.RecentLogs("event-ingestion")`.

`event-ingestion` is in `AspireFixture.TailedResources`, which
`LogTailCoverageTests` requires (spec §1 evidence table). A resource that is not
tailed yields the string `"(not tailed — add … )"` instead of a log, and that
guard fails the build rather than letting it through.

**Build the distribution string from the actual responses**, not from a
constant — an assertion whose message cannot change when the subject changes is
an assertion checking its own input.

### 6.3 Why the title is read from the body, not inferred

`Results.Problem(title: …)` emits `application/problem+json` with a `title`
member. The test reads
`(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()`.

This is the assertion CF-A exists to prove is real. **Asserting only
`StatusCode == 429` would pass against any 429 in the stack** — the API gateway
has its own rate limiter (`WhepAuthorizeRateLimiting` is configured in the very
same `isE2ETests` region of `AppHost.cs`), and Kestrel can shed. The title is
what makes the assertion name *this* seam.

### 6.4 What is deliberately not tested

| Not tested | Why |
|---|---|
| The **webhook** path's identical 429 | Spec §11. Different auth shape. |
| The exact **number** of 429s | Non-deterministic by construction (spec §7). Asserting "exactly seven" would be a flake generator that also fails when the server gets faster. |
| The 429's `detail` string | Not a contract anyone dispatches on; pinning prose invites a churn failure with no defect behind it. |
| **Recovery** — that the limiter grants again afterwards | The 201 clause already observes a grant *during* the burst, which is stronger. Spec 104's unit tests cover release. |
| **401 / 400 / fab scoping** under the new limit | Existing tests (AS-2, AS-3, AS-4). Observed via the whole-bucket run (AS-5), not duplicated. |

---

## 7. CI placement

- The new class runs in the **sharded Aspire integration job** via
  `shard-3.filter`. It is excluded by none of `Category!=Measurement`,
  `Category!=Disruptive`, `Category!=Maintenance` — it declares no category.
- `integration-shard-coverage` (`ci.yml:456`) re-derives the partition on every
  push. **Without the shard-file edit this job goes red**, which is the
  intended behaviour and the reason §5 lists three files rather than two.
- **No new project, no new package, no `.csproj` edit.**
- **Runtime cost**: one `[Fact]` issuing eight POSTs against an already-booted
  stack — sub-second. The shard's ~157-test budget is unaffected in any way a
  runner could measure.

---

## 8. Coverage gate (ADR-0065)

Domain ≥ 90 %, Application ≥ 80 %, Shared ≥ 90 %.

**Unmoved.** `coverage-check.ps1` excludes `Integration.Tests` by project name
(`ci.yml:66`), and this slice adds no production code to any gated project. A
*change* in these figures would be a finding — it would mean something outside
the three planned files moved.

---

## 9. Constitution and ADR alignment

| Rule | How this plan meets it |
|---|---|
| **§Testing**, two obligations | **RED.** AS-1 fails today; the three-step sequence (spec §6) is the evidence, and the step-1 red is captured verbatim before the AppHost line exists. AS-5 is the characterisation half and is observed green. |
| **§II** value objects | Does not bind. No domain model, no production type. |
| **§IV** latency | **N/A**, argued from the instruction stream: no production code changes and the one line is behind `if (isE2ETests)` (spec §8). |
| **§III / boundaries** | No cross-context reference; the test speaks HTTP only and needs no EventIngestion assembly. |
| **ADR-0103** | Integration test against the real Aspire stack via the existing fixture. No Testcontainers, no new container. |
| **ADR-0143** | The test **depends** on `RetryIdempotentMethodsOnly` being in force (`FixtureHttpClients.cs:28`). Stated as a dependency, not assumed: under the library default the handler would retry the 429 away and the test would fail for an unrelated reason. |
| **ADR-0142** | No `Idempotency-Key` is sent. "No key, no change" — the burst is eight independent creates, keeping one mechanism in the seam under test. |
| **ADR-0052 / 0053 / 0054** | xUnit + Shouldly; sentence-style name; hand-written data inline (no builder needed — the body is four fields). |
| **ADR-0036** | Three files, one line of non-test code. No fixture hook, no abstraction, no config knob that ships. |
| **ADR-0084** | The new file is well under 300 LOC; the single test method stays under 30 LOC by keeping the distribution helper separate. |
| **ADR-0109** | One `[P]` pair; see tasks.md. Nothing foundational. |
| **ADR-0144** | No weakened gate: nothing is deleted, no threshold lowered, no suppression added, no analyzer narrowed. The suite is *run wider*, not filtered. Phase 4a is not skipped — it is the point of the slice. |
| **ADR-0139** | Both verbatim captures (step-1 red, step-2 green) go in the PR body. |

---

## 10. Risks, and what each costs

| Risk | Mitigation |
|---|---|
| **The test arrives green at step 1** | The single worst outcome, and the one the whole issue exists around. It means the 429 came from something other than this limiter, or the assertion is already true. **Stop and report** (spec §6). CF-B is the same question asked from the other side. |
| **The shard file is forgotten** | Then the class runs nowhere and `integration-shard-coverage` goes red — loudly, which is the design. It is called out in spec §1.1, plan §5 and §7, and it is **T002**, ordered before the first run. |
| **A stack-wide concurrency of 1 reddens an unrelated test** | §5.4's seven-row sweep is the argument; **AS-5 / T008 is the check**. If it happens, it is a finding: report it with the reddened test's verbatim output. **Do not add a retry to the reddened test** — that would be weakening a gate to reach green (ADR-0144). The escape hatch, if one is needed, is a *narrower* override, and that is a design change wanting a decision, not a patch. |
| **The test flakes later** | Spec §7: **raise the request count, never loop.** Written into the test's own doc comment so the next person meets the instruction before they reach for a loop. |
| **The engineer edits `EventsEndpoints.Writes.cs` to make it pass** | Named read-only in the issue, spec §11, plan §5 and tasks.md's declarations. Any diff there outside CF-A's transient injection is a stop. |
| **CF-A reddens the status clause too** | A finding, not a pass: it would mean the two clauses are coupled and the status assertion is not independent. Report and split the assertion. |
| **Line drift between now and phase 4** | Spec §1 records today's numbers (`AppHost.cs:479` / `:575`, `Writes.cs:344-351`, `AspireFixture.cs:317`) **with the surrounding text**, so the engineer can locate each by content if a merge moves it. Locate by content, not by number. |
| **A live Aspire stack is already running** | One machine, one Aspire stack. Two concurrent boots give `FailedToStart` that reads exactly like a code defect. T003/T007 and phase 5 all need the stack; check before booting.
