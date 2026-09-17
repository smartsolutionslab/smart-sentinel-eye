# Plan 172 — Readiness is liveness, by choice

**Spec:** `specs/172-readiness-is-liveness-by-choice/spec.md`
**Issue:** #2125
**ADRs of record:** ADR-0154 (new, drafted here), ADR-0106, ADR-0153, ADR-0118,
ADR-0144, ADR-0037, ADR-0109, ADR-0086
**Phase 4a colour:** **green characterisation** (spec §8)

---

## 1. Shape of the change

This feature has **no bounded context, no aggregate, no entity, no value object,
no message, and no endpoint**. Said explicitly because the plan template asks
for those and their absence here is a fact about the work, not an omission in
the plan: the deliverable is a decision record plus one characterisation test
plus four comment corrections.

The only *code* artefact is a test. The only *source* edits are comments and one
log description string.

Everything lives in **`src/ServiceDefaults`**, which is not a bounded context —
it is the shared host-composition layer every one of the nine contexts consumes.
That is why one comment fix here is worth a spec: the property being recorded is
a property of **all ten hosts** (nine context APIs plus the gateway), decided in
one method.

### Layers touched

| Layer | Touched | What |
|---|---|---|
| Domain | **No** | — |
| Application | **No** | — |
| Infrastructure | **No** | — |
| Api | **No** | — |
| `ServiceDefaults` (shared host composition) | Yes | Three comments, one string |
| `docs/adr` | Yes | One new file |
| `.specify/memory` | Conditional | One citation (spec §9.3) |
| `tests/ServiceDefaults.Tests` | Yes | One new file |
| `tests/Integration.Tests` | Yes | One doc comment |

### Boundary rules (ADR-0109 / NetArchTest)

Unaffected and worth stating: nothing crosses a context boundary, nothing new is
referenced, and `Shared.Contracts` is not touched. `ServiceDefaults` is already a
dependency of every host, so no new edge appears in the reference graph and
`BoundaryTests` has nothing new to see.

---

## 2. The invariant this spec makes checkable

There is one, and it is the whole design:

> **No health check registered on `/health` may fail into `Unhealthy`, and none
> may name or reach an external dependency.**

Two halves, two different reasons, and both must be stated or the next reader
keeps one and drops the other:

- **The availability half.** An external dependency is shared. Failing readiness
  on it drains every replica of every service at once, for a condition none of
  them caused, and takes with it the HTTP surface an operator would use to
  diagnose it. While ADR-0153 holds every service at one instance, "drain every
  replica" and "drain the service" are the same sentence.
- **The exposure half (spec 078).** The security argument for mapping `/health`
  in Production holds *because* the check set is two members and neither name
  encodes a dependency. `/health` is forwarded unauthenticated through the
  gateway's `/{context}/{**catch-all}` routes, so a check named `postgres` would
  put a dependency name one `ResponseWriter` away from an anonymous caller.

The invariant is **partially** checkable today, and the plan is honest about
which part:

| Half | Mechanism after this spec | Gap |
|---|---|---|
| The unreachable-database path returns `Healthy` | `OutboxBacklogHealthCheckTests` (new) — drives the real class against a closed port | None |
| `Degraded` answers 200 | `DefaultEndpointsTests.A_production_readiness_probe_answers_two_hundred_for_a_degraded_ready_tagged_check` (exists) | None |
| `Unhealthy` answers 503 | `DefaultEndpointsTests.A_development_readiness_probe_reports_a_failing_ready_tagged_check_as_one_word` (exists) | None |
| The default writer emits one word | `DefaultEndpointsTests`, two exact-string assertions (exists) | None |
| **A future check registered with `failureStatus: Unhealthy`** | **Nothing** | Spec §6 item 3 — follow-up issue, T007 |
| **A future check whose name encodes a dependency** | **Nothing** | Same follow-up |

The last two rows are the reason ADR-0154 has to exist as prose: the constraint
is currently carried by a comment and a decision record, not by a test. Writing
that down is the difference between a gap and a surprise.

---

## 3. Why a comment fix needs a plan at all

Because the comment has already been wrong once, in a way that survived for
months and was found only by a security review of an unrelated change.

The sequence matters:

1. `b0b54067` introduced the check with the swallow and the reason *"the database
   being unreachable is already reported by the connection's own check."*
2. There was never a connection check. The sentence was false the day it was
   written.
3. Because it read as *deduplication* — a defensible engineering choice — nobody
   questioned the `Healthy`.
4. `588e1e5e` (2026-09-06) corrected the comment to say there is no such check
   and the `Healthy` is deliberate, and forwarded the open question to #2125.
5. **The description string it hands back still says `"the database check owns
   this."`** The comment was fixed; the string, six lines below it, was not.

So the failure mode this plan guards against is precise: **a justification
carried only in prose drifts, and the drifted copy is indistinguishable from the
original.** That is why the deliverable is not "fix the comment" but "fix the
comment, put the reason somewhere numbered and citable, and make the *behaviour*
the reason justifies fail a test if it moves."

---

## 4. The test, in detail

`tests/ServiceDefaults.Tests/OutboxBacklogHealthCheckTests.cs`

**Mechanism.** `OutboxBacklogHealthCheck<TDbContext>` takes a `TDbContext`, an
`ILogger<>` and an outbox schema string. The check's first act is
`database.Database.SqlQueryRaw<long>(…).SingleAsync(…)`. A `DbContext` configured
with `UseNpgsql` against a **closed loopback port** makes that throw
`NpgsqlException` — a `DbException` whose `SqlState` is empty, which is exactly
what `IsUnreachable` tests for. No Docker, no fixture, no Postgres.

**Shape** (the engineer writes it; this is the contract, not the file):

- A private `sealed class` `DbContext` in the test file with no model —
  `SqlQueryRaw` needs a connection, not entities. `Timeout=1` in the connection
  string so a host that black-holes rather than refuses still fails fast.
- `NullLogger<OutboxBacklogHealthCheck<…>>.Instance` for the logger: the
  unreachable path logs nothing, so a `Mock<ILogger<…>>` would assert nothing
  and `Moq` is not needed here.
- Schema `"wolverine_probe"` — lower-case letters and underscore only, so
  `Identifier()` accepts it. A schema that failed that guard would throw
  `ArgumentException` **before** the connection is attempted and the test would
  be green for the wrong reason.
- One `[Fact]`, sentence-style name (ADR-0053):
  `A_database_that_cannot_be_reached_is_reported_healthy`.
- Assertions: `status.ShouldBe(HealthStatus.Healthy)` with a `because` message
  **naming ADR-0154**, plus that the result carries the exception type under
  `"error"`.

**The port choice is load-bearing and must be justified in the test's own doc
comment.** `127.0.0.1:1` refuses immediately on Windows and Linux. If a future
CI host has something listening there, the test would fail for an unrelated
reason — so the assertion message says what the test believes about the port, and
the `Timeout=1` bounds the damage either way.

**What makes this an honest test and not an assertion on its own input.** The
subject is the production type, unmodified, and the outcome can change without
the assertion text changing: flip `Healthy` to `Unhealthy` in the `catch` and the
test goes red with the same source. Step 3 of spec §7 is that counterfactual,
and it is a mandatory task (T004), not a suggestion — three guards in this
repository have failed their own claim when someone finally constructed what they
said they caught.

**Coverage.** `ServiceDefaults` sits under the Shared ≥ 90% gate (ADR-0065). This
adds covered lines to a previously uncovered branch; it cannot lower the figure.

---

## 5. Dependencies and sequencing

```
T001 (premise re-verify at branch tip)
  │
  ├─► T002 (write the test, observe GREEN, capture verbatim output)   ← phase 4a
  │      │
  │      └─► T004 (counterfactual: invert the catch, observe RED, revert)
  │
  └─► T003 (draft ADR-0154)          ── independent of the test
         │
         ├─► T005 (comment + string in OutboxBacklogHealthCheck)
         │      └─► T008 (Integration.Tests doc comment — quotes the string)
         ├─► T006a (WolverineDefaults registration pointer)
         ├─► T006b (Extensions.cs MapDefaultEndpoints citation)
         └─► T006c (constitution citation — CONDITIONAL, spec §9.3)

T007 (file the follow-up issue for the registration guard)  ── independent
T009 (board gate)                                           ── last
T010 (re-run the test unmodified; Release build)             ── after T005–T006
```

**T003 blocks T005–T006c** for one reason only: every one of those edits *cites*
ADR-0154 by number and title. Writing the citation before the file exists is how
a repository ends up with four pointers to a document nobody wrote. It is a
cheap serialisation — the ADR is one file — and it is not negotiable.

**T002 does not block T003.** The test characterises behaviour that exists on
`develop` today; it does not depend on the ADR's text. Running them in parallel
is the only real parallelism this slice has.

---

## 6. Parallelism (ADR-0109)

Genuinely small. Marked honestly rather than inflated:

| Parallel set | Files owned | Why disjoint |
|---|---|---|
| **T002** ∥ **T003** | `tests/ServiceDefaults.Tests/OutboxBacklogHealthCheckTests.cs` ∥ `docs/adr/0154-*.md` | Different trees, and different agents: `test-writer` owns the first, `infra-engineer` the second. ADR-0144's phase-4 split makes this the natural cut. |
| **T005** ∥ **T006a** ∥ **T006b** | `OutboxBacklogHealthCheck.cs` ∥ `WolverineDefaults.cs` ∥ `Extensions.cs` | Three files, one comment each, no shared line. **But all three are one agent's work and one commit's worth**, so running them concurrently buys nothing and risks three commits where one belongs. Marked `[P]` for the disjointness record; execute together. |
| **T007** | GitHub only | No file. |

**Foundational / blocking:** T003 (the ADR) is the foundation — four tasks cite
it. T001 precedes everything, because an issue a few weeks old had already been
60% fixed by the time this spec was written (spec §2 row 8), and that discovery
changed the scope.

Nothing here warrants fanning out to more than the two phase-4 agents ADR-0144
already prescribes.

---

## 7. Risks

| Risk | Handling |
|---|---|
| **The test is red on first run** — the `catch` guard does not catch what a closed port throws | **Stop and report.** Spec §4.5: do not edit the test, do not edit the `catch`. It would mean the swallow never worked and the decision record would describe behaviour the code lacks — a new finding needing its own issue. |
| Something is listening on `127.0.0.1:1` on a CI host | `Timeout=1` bounds it; the assertion message states the premise. If it ever bites, the fix is a different port, not a different mechanism. |
| The ADR is read as the lane making a decision | Spec §9.2, and the ADR's own Decision section, attribute it to the repository owner explicitly. |
| The constitution edit is refused | It is isolated in its own task (T006c) and its own commit. Dropping it leaves a shippable spec (spec §9.3). |
| `MSB3027` on the Release build | The Aspire stack (pid 3312) holds the service binaries. Stop it before building; do not stop it before the live `curl` step. |
| A restored file after the §7-step-2 stash does not rebuild | Known: a restored file keeps its old timestamp and MSBuild skips it. Use `--no-incremental` or touch the file. |

---

## 8. Definition of done

1. `docs/adr/0154-readiness-is-liveness-by-choice.md` exists, `Status: Accepted`,
   dated, attributing the decision to the repository owner, naming ADR-0106,
   ADR-0153 and ADR-0118, and recording option 2 as declined with its three
   costs.
2. `grep -rn "database check owns this" src/ tests/` → **no hits**.
3. No comment or string in `src/ServiceDefaults` forwards to #2125 as an open
   question.
4. `OutboxBacklogHealthCheckTests` passes, **was observed passing before any
   source edit**, passes **unmodified** after, and its counterfactual (T004) was
   observed red.
5. `dotnet build SmartSentinelEye.slnx -c Release` clean; `dotnet test` on
   `ServiceDefaults.Tests` green.
6. `GET /health` on the running stack still answers `200 Healthy` — unchanged,
   which is the point.
7. The follow-up issue for the registration guard exists and is linked from spec
   §6 item 3.
8. #2125 is on Project #13 (it already is — verify with `--limit 2000`).
