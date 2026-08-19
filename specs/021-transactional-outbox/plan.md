# Implementation Plan: An integration event is never lost after its write commits

**Branch**: `021-transactional-outbox` | **Date**: 2026-08-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/021-transactional-outbox/spec.md`

## Summary

Nine repositories commit a write and then announce it. The window between the
two is the defect: if the announcement fails, the row stays and the announcement
is gone, with nothing to retry it and no trace that it was owed.

The fix is not to build durability — ADR-0088 already mandates a Postgres outbox
and `WolverineDefaults.cs` genuinely configures it. The fix is to **reach it**.
That machinery enrols messages published from inside a Wolverine message
handler; every one of these writes originates from an HTTP endpoint or a hosted
service, so nothing is enrolled and the publish goes straight to the broker.

Three moves, in order of how much they can break:

1. **`IEventBus` becomes outbox-backed.** One implementation swap in
   `ServiceDefaults` covers all twelve domain-event handlers with no Application
   code changing.
2. **The repositories dispatch before they commit**, and commit through
   `SaveChangesAndFlushMessagesAsync` so the domain rows and the outbox rows
   land in one transaction. Nine near-identical edits.
3. **ADR-0088's scope is corrected**, because its consequence section reads as a
   broader guarantee than it gives, and that wording is why nobody looked.

The ordering inversion in (2) is the risky part and is treated as such
throughout: handlers that used to run after a successful commit will run before
it.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: WolverineFx 6.24.2 + `WolverineFx.EntityFrameworkCore`
(`IDbContextOutbox<T>`, `SaveChangesAndFlushMessagesAsync`), EF Core 10,
Npgsql, RabbitMQ

**Storage**: PostgreSQL — outbox rows in the per-context `wolverine_<context>`
schema already provisioned by `AutoBuildMessageStorageOnStartup`

**Testing**: xUnit + Shouldly + hand-written fakes; integration against the real
Aspire stack (ADR-0103); NetArchTest for the boundary and the new
no-direct-`SaveChangesAsync` rule

**Target Platform**: Linux containers (k3s in prod, Aspire in dev)

**Project Type**: Multi-context backend; this feature touches the shared seam
(`Shared.CQRS`, `ServiceDefaults`) and nine Infrastructure repositories

**Performance Goals**: no regression against spec 020's measured ingest figures;
the outbox write shares the transaction and round trip the domain write already
makes

**Constraints**: constitution §IV latency budget — the ingest path's
`event → overlay state ≤ 200 ms` leg is the binding one; write-path caller
behaviour must not change (FR-013)

**Scale/Scope**: 9 repositories, 12 domain-event handlers, 8 bounded contexts,
1 shared seam, 1 ADR amendment

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1.*

| Principle | Assessment |
|---|---|
| **§I DDD, value objects** | No domain change. Domain events and their handlers keep their shapes; only when the dispatch happens moves. |
| **§II Bounded contexts, no cross-context references** | Unchanged and reinforced: the seam is `Shared.CQRS`/`ServiceDefaults`, which every context already depends on. No context gains a reference to another. NetArchTest still enforces it. |
| **§III Contracts versioned** | No contract in `Shared.Contracts` changes shape or version. What changes is *when* a message is released, not what it is. |
| **§IV Latency budget** | The binding leg is `event → overlay state ≤ 200 ms`. The change removes a synchronous broker hop from the write path and adds rows to a transaction already open, so the expectation is neutral-to-better — and FR-012 requires it measured through spec 020's harness rather than argued. |
| **§V Observability** | Extended, not eroded: FR-008/009/010 add pending-count, oldest-age and repeated-failure reporting where today there is nothing to see. |
| **§IX No speculative generality** | The only new abstraction is the outbox-backed `IEventBus`, replacing an existing implementation one-for-one. No new interface, no new configuration knob. |
| **Governance** | **ADR-0088 is amended** (its consequence section overstates scope). That is a recorded amendment with its reasoning, not a silent reinterpretation — and it is a gate. |

**No exceptions required.** The one governance action is the ADR amendment,
which is in scope by FR-014 and is a deliverable rather than a workaround.

## Project Structure

### Documentation (this feature)

```text
specs/021-transactional-outbox/
├── plan.md              # this file
├── research.md          # Phase 0 — five questions, R1/R2 decide feasibility
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── event-bus.md     # Phase 1 — the seam's contract, before and after
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source code

```text
src/
  Shared.CQRS/
    IEventBus.cs                       # unchanged — the seam is already right
  ServiceDefaults/
    WolverineEventBus.cs               # REPLACED: publishes into the ambient outbox
    DomainEventDispatcher.cs           # unchanged
    WolverineDefaults.cs               # registration of the outbox-backed bus
  <Context>/Infrastructure/
    <Context>InfrastructureModule.cs   # binds IEventBus to this context's DbContext
    Persistence/<Aggregate>Repository.cs   # dispatch before commit; commit via the outbox

tests/
  Architecture.Tests/                  # new rule: no repository calls SaveChangesAsync directly
  <Context>.Infrastructure.Tests/      # per-repository ordering tests
  Integration.Tests/EventIngestion/    # the message-survives-a-failed-publish case
```

## Approach

### Step 1 — the seam (US1, foundational)

`WolverineEventBus` currently resolves `IMessageBus` and publishes immediately.
It becomes a publisher into the `IDbContextOutbox` bound to the calling
context's `DbContext`. `IEventBus` itself does not change — the Application
layer stays Wolverine-free (ADR-0057), and no domain-event handler is touched.

Registration is per context because `IDbContextOutbox<T>` is generic in the
`DbContext`; each `<Context>InfrastructureModule` gains one line binding its own.

### Step 2 — the repositories (US1 + US2)

Each of the nine:

```csharp
// before
await dbContext.SaveChangesAsync(cancellationToken);
foreach (var aggregate in tracked) { ...; await dispatcher.DispatchAsync(events, ct); }

// after
foreach (var aggregate in tracked) { ...; await dispatcher.DispatchAsync(events, ct); }
await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
```

EventIngestion's `EventRepository` keeps spec 020's failure-collection: every
event is offered to the dispatcher and failures are raised together, so one bad
handler cannot strand the other 199 in a batch.

**The ordering inversion is stated in every task that touches a repository**, so
a reviewer checks for side effects rather than skimming a one-line diff. R2
surveyed all twelve handlers: eleven only publish; `VariableValueChangedDomain
EventHandler` also reads, which is safe under the new ordering and gets its own
test because it is the only handler whose correctness depends on *when* it runs.

### Step 3 — the guard (US2, FR-007)

A NetArchTest rule: no type under a `Persistence` namespace calls
`DbContext.SaveChangesAsync` directly. A repository added later either goes
through the outbox or fails the build. Removing the unenrolled path entirely is
not available — `IMessageBus` is legitimately used by Wolverine's own handlers.

### Step 4 — visibility (FR-008, FR-009, FR-010)

Pending count and oldest-message age, exposed where the contexts already expose
health. An outbox quietly growing looks identical to an empty one until the disk
fills.

### Step 5 — the record (US3, FR-014)

ADR-0088 amended: what is guaranteed, for which publishes, and how a new write
path joins. Its current consequence line — "transactional outbox guarantees no
message loss on crash mid-handler" — is true and reads as though it covers
everything.

## Verification strategy

Spec 020 established that the observations are the deliverable, and the same
applies here. The test that matters is the one the issue named: **commit, make
the publish fail, assert the message is eventually delivered** — not that the
row exists, which is what passes today.

| Requirement | How it is shown |
|---|---|
| FR-001 / SC-003 | a rolled-back write produces no message — asserted against the outbox tables, not inferred |
| FR-002 / SC-001 | broker made unreachable for 60 s under load; every committed write's message arrives afterwards |
| FR-003 / SC-002 | service killed between commit and flush; message present after restart |
| FR-005 / SC-004 | one integration case in a second context, so the guarantee is not demonstrated only where it was found |
| FR-011/012 / SC-005 | spec 020's `IngestThroughputMeasurementTests`, before and after, identical harness |
| FR-008 / SC-006 | the pending count and oldest age readable without a debugger |

## Risks

| Risk | Handling |
|---|---|
| **The ordering inversion changes behaviour somewhere unsurveyed** | R2 surveyed all twelve handlers; the survey is in research.md and each repository task repeats the warning. A handler that throws now fails the write — acceptable only because every handler on this path publishes and nothing else. |
| **Nine near-identical edits drift** | They are one shared change plus nine one-line commits, reviewable side by side, guarded afterwards by the NetArchTest rule. |
| **Outbox rows double the write volume on the ingest path** | 200 events per batch become 200 outbox rows in the same transaction. Measured by FR-012 rather than assumed; the rows are short-lived and deleted on successful delivery. |
| **A permanently undeliverable message becomes a new outage** | Wolverine's retry and dead-letter handling, with the queue depth made visible. Spec 020 fought this one layer up; the same shape applies and does not need reinventing. |
| **The feature is invisible when it works** | Which is why FR-008 exists. The failure mode this closes leaves no trace today; the replacement must leave one. |

## Phase gates

- **Phase 0 complete** — research.md answers R1–R5. R1 confirms the spec's
  central assumption holds: the mechanism exists and is unreached.
- **Phase 1 complete** — data-model.md, contracts/event-bus.md, quickstart.md.
- **Gate before Phase 2**: this plan aligns with the constitution and ADR-0088
  as amended. The amendment itself is a deliverable, not a prerequisite.
