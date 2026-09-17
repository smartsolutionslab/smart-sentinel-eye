# Tasks 178 — A premise checked against its own read

**Spec**: `spec.md` · **Plan**: `plan.md` · **Issue**: #2223
**Phase**: 3 (Tasks) · **Date**: 2026-09-17
**Baseline**: `32a26c3a` · **Branch**: `fix/2223-a-premise-checked-against-its-own-read`

---

## Declarations required at the phase-3 gate

| Declaration | Value |
|---|---|
| **Engineer** | `backend-engineer` (C# / test infrastructure) |
| **Reviewer** | `backend-reviewer` |
| **Phase 4a colour** | **Per task, below. No task is red.** US1 → **green characterisation**; US2 → **green characterisation**; the `CancellationToken` item → **no test, and no task** — subsumed by T003's deletion (spec §6). |
| **New ADR needed** | **No.** ADR-0049, ADR-0084 and ADR-0036 are applied, not amended. Spec §8 names the one thing that *would* need an ADR and excludes it. |
| **Security review (phase 6)** | **Not required.** No endpoint, no auth, no trust boundary, no secret. The RabbitMQ management credentials are read from the AppHost-parameterised connection string exactly as they are today — unchanged. |
| **Files phase 4 may touch** | The six in the table below. **No file under `src/`.** |
| **Latency budget** | **N/A** — no §IV leg is touched (spec §7). |

### The complete edit set

| # | File | Task |
|---|---|---|
| 1 | `tests/Integration.Tests/AuditObservability/AuditQueueProbe.cs` | T002 |
| 2 | `tests/Integration.Tests/AuditObservability/AuditHandoverPopulationTests.cs` | T003 |
| 3 | `tests/Integration.Tests/AuditObservability/IngestMeasurementConditions.cs` **(new)** | T007 |
| 4 | `tests/Integration.Tests/AuditObservability/NFR001_RequirementSpanIntervalTests.cs` **(new)** | T008 |
| 5 | `tests/Integration.Tests/AuditObservability/NFR001_AuditIngestLatencyTests.cs` | T009 |
| 6 | `specs/178-a-premise-checked-against-its-own-read/*` | T011 |

**Read-but-not-edited:** `AuditHandoverLegTests.cs`. Plan §2.1 makes the probe
change strictly additive so this file does not move. **If phase 4 finds itself
editing it, the change stopped being one-way — stop and report.**

---

## Parallelism (ADR-0109)

**US1 and US2 are `[P]` against each other** — disjoint files, disjoint
verification, either can ship without the other.

**Within each story, tasks are sequential.** There is no foundational
Shared.Kernel / Shared.Contracts / AppHost task here to fan out from: nothing in
`src/` is touched, so the usual "foundation blocks the rest" shape does not
apply. The orchestrator can fan out at most two workers, one per story.

```
US1:  T001 → T002 → T003 → T004 → T005
US2:  T006 → T007 → T008 → T009 → T010
                                      ↘
                                       T011 (both stories done)
```

---

## US1 (P1) — The premise is certified against the read the conclusion uses

**Phase 4a colour: GREEN — characterisation.** Behaviour-preserving. The two
assertions in `AuditHandoverPopulationTests` must pass **unmodified**.

### `[T001] [US1]` Capture the characterisation, before any edit

Run against the **already-running** Aspire stack (pid 3312 — do not stop it, do
not boot a second; memory: *one machine, one Aspire stack*).

```sh
dotnet test tests/Integration.Tests \
  --filter "FullyQualifiedName~AuditHandoverPopulationTests" \
  --logger "console;verbosity=detailed"
```

**Run it twice** (memory: *measurement runs need repeating* — the first run
after machine churn reads exactly like a regression).

**Record verbatim, both runs:** the `audit queues seen:` line, the `quiescent :`
line, the `mid-flight :` line, and the pass/fail summary.

**Done when:** both runs pass; `audit queues seen` names the audit queues and is
**not** `(none)`; `quiescent = 0`; `peak > 0`. **If the fact is red before any
edit, stop — there is nothing to characterise and the premise is already
broken.**

**Depends on:** nothing. **Blocks:** T002.

### `[T002] [US1]` Widen `AuditQueueProbe` — strictly additive

Per plan §2.1.

- Add `ReadAsync(HttpClient broker, CancellationToken cancellationToken)` →
  `(int Unacknowledged, string[] Queues)`. Body = the current
  `UnacknowledgedAsync:56-81`, plus the queue short-name collection from
  `AuditHandoverPopulationTests.ReadAsync:206-222`. The non-2xx throw message is
  already byte-identical in both copies — keep it exactly.
- `UnacknowledgedAsync` keeps its signature; becomes a delegate to `ReadAsync`.
- Add `WaitForQuiescenceReadingAsync(broker, deadline, cancellationToken)` →
  `(int Unacknowledged, string[] Queues)`.
- `WaitForQuiescenceAsync` keeps its signature; becomes a delegate.
- Extend the class doc to say the read now answers the matched queue names, and
  **why**: a wrong prefix and an idle broker both produce `0`, and the names are
  what tells them apart.

**Done when:** `dotnet build tests/Integration.Tests` succeeds and
`AuditHandoverLegTests.cs` is **unmodified** (`git status` shows it untouched).

**Depends on:** T001. **Blocks:** T003.

### `[T003] [US1]` Delete the duplicate helpers and consume the probe

Per plan §2.2. In `AuditHandoverPopulationTests.cs`:

- Delete `ReadAsync`, `WaitForQuiescenceAsync`, `BrokerClientAsync`, the
  `AuditQueuePrefix` field, and the four now-unused `using` lines.
- Declare `CancellationToken cancellationToken = CancellationToken.None;` as the
  fact's first statement and thread it into every call — replacing the three
  bare `CancellationToken.None` arguments at `:101`, `:108`, `:117`, and passing
  it to `SampleAsync`'s `AuditQueueProbe.ReadAsync` call.
- `BrokerClientAsync()` → `AuditQueueProbe.ClientAsync(aspire, cancellationToken)`.
- `WaitForQuiescenceAsync(broker)` →
  `AuditQueueProbe.WaitForQuiescenceReadingAsync(broker, QuiesceDeadline, cancellationToken)`.
- `SampleAsync`'s `ReadAsync(broker)` →
  `AuditQueueProbe.ReadAsync(broker, cancellationToken)`.
- `{AuditQueuePrefix}` → `{AuditQueueProbe.QueuePrefix}` in the first assertion.

**Keep unchanged:** the class doc, `QuiesceDeadline`, `BurstWriters`,
`BurstEventsPerWriter`, `DrainWindow`, `SampleInterval`, `SampleAsync`'s
peak/sample/non-zero accounting, and **both assertion message strings** apart
from that one interpolation.

**Done when:** it builds, and `git diff` shows no edit inside either
`ShouldBe` / `ShouldBeGreaterThan` other than the `AuditQueueProbe.QueuePrefix`
interpolation.

**BLOCKING CONDITION:** if an assertion has to change to make the fact pass,
the two reads were **not** equivalent. **Stop and report. Do not adjust the
assertion** (spec §6, plan §3).

**Depends on:** T002. **Blocks:** T004.

### `[T004] [US1]` Re-run the characterisation and prove the duplication is gone

```sh
dotnet test tests/Integration.Tests \
  --filter "FullyQualifiedName~AuditHandoverPopulationTests" \
  --logger "console;verbosity=detailed"          # twice

grep -rn "api/queues"              tests/Integration.Tests/AuditObservability/
grep -rn "messages_unacknowledged" tests/Integration.Tests/AuditObservability/
```

**Done when:** the fact passes both runs with the same four characterised
properties (T001); `api/queues` returns **exactly one** hit, in
`AuditQueueProbe.cs`; `messages_unacknowledged` appears only in
`AuditQueueProbe.cs` plus assertion/comment prose. Sample counts and peak values
differing run to run is load, not regression.

**Depends on:** T003. **Blocks:** T005.

### `[T005] [US1]` Counterfactual — prove the wrong-prefix tell survived

Memory: *prove a guard by counterfactual.* A diagnostic asserted but never
provoked is not a diagnostic.

Temporarily point `AuditQueueProbe.QueuePrefix` at a prefix matching nothing
(e.g. `"wolverine_audit."` — the exact wrong value the issue names), re-run the
fact, then **revert immediately and confirm the revert with `git diff`**.

**Done when:** the fact **fails on the peak assertion** *and* prints
`audit queues seen: (none)`. A failure without `(none)` means the diagnostic
did **not** survive the consolidation — **that is a blocker, not a note.**
Record the verbatim output; it is the evidence for the PR body.

**Depends on:** T004.

---

## US2 (P2) `[P]` — Two verdict-shaped facts, two files

**Phase 4a colour: GREEN — characterisation.** Pure file movement; no new test.

**Priority note, recorded honestly (spec §1.3).** The issue justified this by
ADR-0084's 300-LOC limit. That justification does not hold: the file is 368
lines, not 485, and `.editorconfig`'s `[tests/**.cs]` section sets S104 to
`none` for test projects **deliberately** (spec 174 / #2209, one day old),
restating ADR-0084's "Test projects exempt" in the file that outranks `NoWarn`.
**There is no gate here.** US2 is kept on the merits — 368 lines carrying two
independent verdict-shaped facts, with a shared measurement-conditions guard
between them, is a real reading cost — but it is **tidiness, and the PR must
say so rather than claim a limit.** *If the phase-3 reviewer would rather not
spend a PR on tidiness, drop US2 and close #2223 on US1 alone; US1 is complete
without it.*

### `[T006] [US2]` Capture the characterisation of both facts

```sh
dotnet test tests/Integration.Tests --list-tests \
  | grep -E "Where_the_ingest_span_goes|Requirement_span_at_100_events_per_second"
git log -1 --format=%H -- tests/Integration.Tests/AuditObservability/NFR001_AuditIngestLatencyTests.cs
```

Record both `[Fact]` method bodies as they stand (they must move byte-identically).

**Done when:** exactly two test names are listed, and the pre-split bodies are
recorded for the byte-identity check in T010.

**Depends on:** nothing. **Blocks:** T007.

### `[T007] [US2]` Extract the shared measurement conditions

New `IngestMeasurementConditions.cs` — `internal static class` holding
`ServiceLogLevel`, `ChosenServiceLogLevel`, `ServiceLogLevelWasChosen` and
`DrivePosition`, **with their doc comments moved intact** (they are the record
of *why* the log level is a condition of the measurement, not a harness detail).

**Done when:** it builds and each of the four members is declared **exactly
once** in the folder.

**Depends on:** T006. **Blocks:** T008.

### `[T008] [US2]` Create `NFR001_RequirementSpanIntervalTests.cs`

Move `Requirement_span_at_100_events_per_second_is_an_interval_not_a_verdict`
with its doc comment, plus `P99BudgetMs` and `BudgetPlacement` — which only it
uses (plan §4). Body byte-identical apart from unqualified shared-member
references becoming `IngestMeasurementConditions.X`.

**Done when:** it builds and carries `[Collection(AspireCollection.Name)]` and
`[Trait("Category", "Measurement")]` exactly as before.

**Depends on:** T007. **Blocks:** T009.

### `[T009] [US2]` Trim `NFR001_AuditIngestLatencyTests.cs`

Remove everything now living elsewhere; keep the class doc and
`Where_the_ingest_span_goes`. Fix the `<see cref="..."/>` in the moved fact's
doc so the cross-reference still resolves.

**Done when:** it builds with no unused `using` and no member duplicated in
either new file.

**Depends on:** T008. **Blocks:** T010.

### `[T010] [US2]` Verify the split changed nothing

```sh
dotnet test tests/Integration.Tests --list-tests \
  | grep -E "Where_the_ingest_span_goes|Requirement_span_at_100_events_per_second"
dotnet build -c Release 2>&1 | grep "S104"
git diff -M --stat          # expect renames/moves, not rewrites
```

**Done when:** exactly two test names, one each; both `[Fact]` bodies
byte-identical to T006's record; each shared member declared exactly once
across the three files; **no S104 diagnostic naming any file under `tests/`**
(before or after — the exemption is the point, not a result); Release build
clean.

**Depends on:** T009.

---

## Close-out

### `[T011]` Board and artefacts

- `specs/178-.../{spec,plan,tasks}.md` committed, Conventional Commits,
  **no `Co-Authored-By`** (ADR-0086 overrides any session attribution reminder).
- Issue **#2223** added to **Project #13** by hand — `/speckit-tasks` adds
  nothing to the board, and per-task issues are **not** created (the repo
  stopped after spec 028):

  ```sh
  gh project item-add 13 --owner smartsolutionslab \
    --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2223
  ```

  `item-add` prints nothing on success; verify with `--limit 2000`, never the
  30-item default.

**Depends on:** T005 and T010.

---

## What phase 5 must observe, and what green CI will *not* prove

Both `AuditHandoverPopulationTests` and the two NFR-001 facts are
`Category=Measurement` and **excluded from CI**. A green CI run therefore
proves that everything still **compiles** and that the non-measurement suite is
unaffected — **it proves nothing about US1's characterisation.**

Phase 5 must run T004 and T005 by hand against the live stack and quote the
output. The verification note must say explicitly that CI did not cover the
measurement facts, so a later reader does not mistake the green tick for the
evidence. Memory: *self-review catches contradictions, never omissions* — write
the measured result down as observed, not only into the orchestrator's summary.
