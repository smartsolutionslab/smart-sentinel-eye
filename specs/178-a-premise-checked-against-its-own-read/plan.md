# Plan 178 — A premise checked against its own read

**Spec**: `specs/178-a-premise-checked-against-its-own-read/spec.md` · **Issue**: #2223
**Phase**: 2 (Plan) · **Date**: 2026-09-17
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer`
**Baseline**: `32a26c3a` (origin/develop, 2026-09-17)

---

## 1. Bounded context and layers

**None are touched.** This is the shortest section in the plan and the most
load-bearing one for the reviewer:

| Layer | Touched? |
|---|---|
| `src/AuditObservability/Domain` | **No** |
| `src/AuditObservability/Application` | **No** |
| `src/AuditObservability/Infrastructure` | **No** — `AuditObservabilityInfrastructureModule.ContextName` is **read** by the tests, as it already is today. Not modified. |
| `src/AuditObservability/Api` | **No** |
| `src/Shared.Kernel`, `src/Shared.Contracts` | **No** |
| `src/AppHost`, `src/ServiceDefaults` | **No** — `WolverineDefaults.cs` is read for evidence only. |
| `tests/Integration.Tests/AuditObservability` | **Yes — the whole change** |

**Entities, value objects, invariants: none.** No domain model exists in this
change, so constitution §II does not bind and no value object is introduced.

**Messaging: none.** No domain event, no integration event, no contract, no
Wolverine registration. The change reads RabbitMQ's *management* API over HTTP,
which it already does today — it does not publish or consume.

**Boundary rules: unaffected.** No cross-context project reference is added.
NetArchTest's existing rules pass unchanged because no `src/` project reference
graph is modified.

---

## 2. The design, and why it is not the issue's suggested shape

The issue proposes: *"Have `AuditHandoverPopulationTests` consume
`AuditQueueProbe` rather than keep its own copy."*

Taken literally that is a **regression**. Spec §1.2 established why: the
population test's private `ReadAsync` returns the matched queue **short names**
as well as the count, and prints them —

```
audit queues seen: (none)
```

— which is the **only** thing that tells a reader that a `0` came from a prefix
matching nothing rather than from an idle broker. That is exactly the trap the
issue exists to remove, and `AuditQueueProbe.UnacknowledgedAsync` returns only
the `int`.

**So the consolidation runs in the direction the issue asks (one-way: the test
consumes the probe), but the probe is widened first.** One read
implementation; two return shapes over it; no new type.

### 2.1 `AuditQueueProbe` after the change — strictly additive

```
QueuePrefix                      unchanged
ClientAsync(fixture, ct)         unchanged            (already has its token — §1.1)
ReadAsync(broker, ct)            NEW  → (int Unacknowledged, string[] Queues)
UnacknowledgedAsync(broker, ct)  unchanged signature  → int, now delegates to ReadAsync
WaitForQuiescenceAsync
        (broker, deadline, ct)   unchanged signature  → int, now delegates
WaitForQuiescenceReadingAsync
        (broker, deadline, ct)   NEW  → (int Unacknowledged, string[] Queues)
```

- **`ReadAsync` is the single implementation.** It is the body currently in
  `AuditQueueProbe.UnacknowledgedAsync:56-81`, plus the queue-name collection
  currently in `AuditHandoverPopulationTests.ReadAsync:206-222` (the
  `queues.Add(name[(name.LastIndexOf('.') + 1)..])` line and the ordered
  materialisation). The throw-on-non-2xx message is **byte-identical in both
  copies today**, so it survives unchanged.
- **`UnacknowledgedAsync` keeps its signature and becomes a one-line delegate.**
  This is deliberate: it keeps `AuditHandoverLegTests.cs:203` — the 250 ms
  sampling loop — compiling and reading exactly as it does now. **No edit to
  `AuditHandoverLegTests.cs`.**
- **`WaitForQuiescenceReadingAsync` exists because the population test needs the
  count *and* the names from the same quiescence wait.** Reading them as two
  calls would be two reads and a race.

**Why named tuples and not a record.** The folder already returns named tuples
from exactly this kind of helper (`(int Peak, int Samples, int NonZero)` at
`AuditHandoverPopulationTests.cs:154`, `(long Total, int Samples, int NonZero,
int Peak)` at `AuditHandoverLegTests.cs:190`). A new `record struct` would be
the "new abstraction" the issue says is not needed, and would be the only one
of its kind in the folder. ADR-0036: no speculative generality.

**Why not change `WaitForQuiescenceAsync`'s return type instead of adding a
member.** That would force a one-line edit at `AuditHandoverLegTests.cs:101`.
Spec §9 declares `AuditHandoverLegTests.cs` out of the edit set precisely so
that a forced edit there is a *signal* rather than a diff — the file is the
consumer whose number the premise certifies, and a refactor that reaches into
it has stopped being one-way.

### 2.2 `AuditHandoverPopulationTests` after the change — deletion only

| Member | Fate |
|---|---|
| `AuditQueuePrefix` (`:49`) | **Deleted.** Assertion text at `:121` switches to `AuditQueueProbe.QueuePrefix` — the same string from the same source, which is how `AuditHandoverLegTests.cs:129` already spells it. |
| `QuiesceDeadline`, `BurstWriters`, `BurstEventsPerWriter`, `DrainWindow`, `SampleInterval` | **Kept, unchanged.** These are the *sizing* — the mitigation for the ~5 s `collect_statistics_interval` cached-zero trap, and they belong to this fact, not to the probe. The probe must stay free of an opinion about window width. |
| `WaitForQuiescenceAsync` (`:139-151`) | **Deleted** → `AuditQueueProbe.WaitForQuiescenceReadingAsync(broker, QuiesceDeadline, cancellationToken)` |
| `SampleAsync` (`:154-181`) | **Kept**, body changed only at `:162`: `ReadAsync(broker)` → `AuditQueueProbe.ReadAsync(broker, cancellationToken)`. Its peak/samples/non-zero accounting is this fact's own and is not duplicated anywhere. |
| `ReadAsync` (`:192-223`) | **Deleted** → `AuditQueueProbe.ReadAsync` |
| `BrokerClientAsync` (`:229-241`) | **Deleted** → `AuditQueueProbe.ClientAsync(aspire, cancellationToken)` |
| `using System.Net.Http.Json; / System.Text; / System.Text.Json;` (`:1-3`) | **Deleted** — unused after the three deletions. |
| `using SmartSentinelEye.AuditObservability.Infrastructure;` (`:4`) | **Deleted** — was only for `ContextName` via `AuditQueuePrefix`. |
| **Both `ShouldBe` / `ShouldBeGreaterThan` assertions (`:119-132`)** | **UNCHANGED, byte for byte, except the `{AuditQueuePrefix}` interpolation becoming `{AuditQueueProbe.QueuePrefix}`** — which renders the identical string. This is the characterisation contract (spec §6). |

**The class doc comment (`:10-39`) stays.** It is the statement of what the
premise *is*; nothing about it changed.

### 2.3 ADR-0049 after the change

`AuditHandoverPopulationTests` currently has **three async members with no
`CancellationToken` at all**. All three are deleted. The fact declares
`CancellationToken cancellationToken = CancellationToken.None;` as its first
statement — the folder-wide pattern (`AuditHandoverLegTests.cs:90`) — and
threads it into every probe call, replacing the three bare
`CancellationToken.None` arguments at `:101`, `:108`, `:117`.

**Net ADR-0049 effect: three non-compliant signatures removed, zero added.**
Verified by inspection, not by a test — see spec §6 for why a red-then-green
test here would be an assertion that cannot fail.

---

## 3. The characterisation contract (US1)

This is the phase-4a **green** obligation, stated so phase 4 cannot satisfy it
loosely.

**Captured before the change**, by running the fact against the live stack and
keeping the verbatim output:

1. `Unacknowledged_..._zero_when_quiescent_and_not_when_handlers_run` **passes**.
2. `audit queues seen:` names the audit queues — **not** `(none)`.
3. `quiescent : messages_unacknowledged = 0`.
4. `mid-flight : ... peak messages_unacknowledged = <p>` with **`p > 0`**.

**Required after the change:** the same four, and

5. `git diff` shows **no change inside either assertion's message string**
   other than `AuditQueuePrefix` → `AuditQueueProbe.QueuePrefix`.

**Blocking condition.** If any assertion must be edited to pass, the two reads
were not equivalent — **stop, report, do not adjust the assertion** (spec §6).
Sample counts and the peak value vary run to run under load; that is not a
change in verdict. Memory: *measurement runs need repeating* — run it twice
before calling a difference real.

---

## 4. US2 — the split, and what must not be duplicated

Usage mapped at `32a26c3a`:

| Member | `Where_the_ingest_span_goes` | `Requirement_span_...` |
|---|---|---|
| `ServiceLogLevel` (`:85`) | `:148` | `:255` |
| `ServiceLogLevelWasChosen` (`:112`) | `:149` | `:256` |
| `ChosenServiceLogLevel` (`:98`) | (via the two above) | (via the two above) |
| `DrivePosition` (`:329`) | `:157` | `:266` |
| `P99BudgetMs` (`:59`) | — | (via `BudgetPlacement`) |
| `BudgetPlacement` (`:346`) | — | `:269`, `:272` |

**Three files out, one of them new twice over:**

| File | Holds |
|---|---|
| `NFR001_AuditIngestLatencyTests.cs` *(kept)* | the class doc, `Where_the_ingest_span_goes` |
| `NFR001_RequirementSpanIntervalTests.cs` *(new)* | `Requirement_span_at_100_events_per_second_is_an_interval_not_a_verdict`, plus `P99BudgetMs` and `BudgetPlacement`, which only it uses |
| `IngestMeasurementConditions.cs` *(new)* | `ServiceLogLevel`, `ChosenServiceLogLevel`, `ServiceLogLevelWasChosen`, `DrivePosition` — `internal static class`, doc comments moved **intact** |

**The constraint that matters:** each shared member is declared **exactly once
across both test files**. Copying the log-level guard into both would
reintroduce, inside the very PR that removes it, the drift class US1 exists to
remove — and the log-level guard is a *condition of the measurement*
(`:61-113` say so at length), so two copies of it drifting is the same failure
mode as two copies of the broker read.

**Both `[Fact]` bodies move byte-identically.** The only permitted edits are
`using` lines, the `[Collection]`/class declaration, and unqualified references
to the moved members becoming `IngestMeasurementConditions.X`.

---

## 5. Boundary and convention checks phase 4 must satisfy

| Rule | How it is met |
|---|---|
| ADR-0049 — token last, mandatory | §2.3. Three non-compliant signatures deleted; every new/edited member takes `CancellationToken` last. |
| ADR-0053 — sentence-style test names | No test is renamed. |
| ADR-0084 — 300 LOC/file | `AuditQueueProbe.cs` grows from 101 to ~135. All split files land well under 300. **Advisory only, and `[tests/**.cs]` sets S104 to `none`** (spec §1.3) — met on the merits, not because a gate demands it. |
| ADR-0103 — Aspire fixture, no Testcontainers | Unchanged; the fixture is the only harness. |
| ADR-0109 — `[P]` on disjoint files | US1 and US2 share no file. |
| House rule — private fields no underscore | No new field. |
| House rule — collection expressions | `List<string> queues = [];` moves across as-is. |
| House rule — handler destructuring | N/A, no handler. |
| ADR-0036 — smallest change | Nothing outside the four files. No behaviour, no measurement, no threshold moves. |

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| **The two reads turn out not to be equivalent and an assertion goes red.** | This is the finding the spec wants, not a failure to work around. Phase 4a's blocking condition (§3): stop and report. |
| **The queue-name diagnostic is silently lost.** | The counterfactual in spec §4 step 6 is the proof — bend the prefix by hand, confirm `(none)` still prints *and* the fact fails. Memory: *prove a guard by counterfactual.* A guard asserted but never provoked is not a guard. |
| **The measurement fact is `Category=Measurement` and excluded from CI**, so green CI proves nothing about US1. | Phase 5 runs it by hand against the live stack, twice, and quotes the output. Green CI is necessary and not sufficient here; the verification note must say so. |
| **The Aspire stack (pid 3312) is already running, and a second boot looks like a code defect.** | Memory: *one machine, one Aspire stack.* Phase 4/5 use the running stack; do not boot another, do not stop 3312. |
| **The split duplicates the log-level guard.** | §4's exactly-once constraint, checked by the spec §3 US2 scenario. |
