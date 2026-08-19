# Research: An integration event is never lost after its write commits

**Feature**: `021-transactional-outbox` · **Phase 0** · 2026-08-19

Five questions. The first two decide whether the feature is possible as
specified; the rest decide what it costs.

---

## R1 — Is ADR-0088's mechanism actually available on this path, or does the spec's assumption fail?

**Decision: available, and unused. No ADR amendment to the *mechanism* is
needed — only to its stated scope.**

`WolverineDefaults.cs` configures everything ADR-0088 promises:

```csharp
opts.PersistMessagesWithPostgresql(postgresConnection, outboxSchema);
opts.UseEntityFrameworkCoreTransactions();
opts.Policies.AutoApplyTransactions();
```

`AutoApplyTransactions` wraps **Wolverine's own message handlers** in a
transaction and enrols their outgoing messages in the outbox. That is the
guarantee ADR-0088 describes, and it is real. It applies to a context reacting
to an integration event it received.

None of the nine write paths is a Wolverine handler. They are HTTP endpoints and
hosted services calling a repository directly, so nothing enrols anything, and
`WolverineEventBus.PublishAsync` → `IMessageBus.PublishAsync` goes straight to
the broker:

```csharp
public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
{
    logger.PublishingIntegrationEvent(typeof(TEvent).FullName);
    return bus.PublishAsync(integrationEvent).AsTask();   // immediate, unenrolled
}
```

`WolverineFx.EntityFrameworkCore` 6.24.2 provides the missing piece:

| Type | What it gives us |
|---|---|
| `IDbContextOutbox<T>` | a publisher whose messages are held against a specific `DbContext` |
| `SaveChangesAndFlushMessagesAsync(ct)` | saves the tracked changes **and** the outbox rows in one transaction, then releases the messages |
| `IDbContextOutbox.Enroll(DbContext)` | the untyped form, for a context resolved at runtime |

So the shape of the fix is not "build an outbox". It is "publish into the outbox
that already exists, and let one call commit both".

**Rationale**: re-deciding the mechanism would contradict a standing ADR for no
reason. The machinery is present, paid for, and running — it simply is not
reached from the place that needs it.

**Alternatives considered**: a hand-rolled outbox table plus a relay worker.
Rejected — it duplicates infrastructure ADR-0088 already mandates, adds a second
thing to operate, and would need its own delivery, retry and poison handling.
The only argument for it was independence from Wolverine, which ADR-0042/0057
already rejected at a higher level.

---

## R2 — The ordering has to invert. What does that break?

**Decision: dispatch moves *before* the commit, and the commit becomes
`SaveChangesAndFlushMessagesAsync`.**

Today, in all nine repositories:

```csharp
await dbContext.SaveChangesAsync(cancellationToken);   // 1. commit
await domainEventDispatcher.DispatchAsync(events, ct); // 2. announce  ← window
```

The window between 1 and 2 is the defect. Closing it means the announcement must
already be captured when the commit happens, which means dispatching first:

```csharp
await domainEventDispatcher.DispatchAsync(events, ct); // 1. capture into the outbox
await outbox.SaveChangesAndFlushMessagesAsync(ct);     // 2. commit rows + messages together
```

**This is the risky part of the feature, and it is not mechanical.** Handlers
that previously ran after a successful commit will now run before it. Three
consequences, each real:

1. **A handler now runs for a write that may still fail.** If the commit is
   rejected, the handler already ran. For a handler that only publishes, this is
   harmless — the message is rolled back with the row, which is the entire
   point. For a handler with any other side effect, it is not.

2. **A handler that reads sees uncommitted state.** Through the same
   `DbContext`, EF's change tracker will surface the pending write, so the
   handler sees the new value rather than the old — which is what it wants — but
   through a *different* connection it would see nothing. Any handler reading
   outside the tracked context needs checking.

3. **A handler that throws now fails the write.** Today a handler exception
   leaves the row committed. Afterwards it aborts the transaction, so the caller
   is told the write failed. That is arguably more correct and it is
   unambiguously *different*, and it changes what a caller sees — which FR-013
   says must not happen. The plan resolves this by requiring that handlers on
   this path only publish (see R3).

**Survey of the 12 domain-event handlers**: eleven publish and nothing else. One
does not:

`SystemVariables/VariableValueChangedDomainEventHandler` publishes
`SystemVariableValueChangedV1`, then reads through `variables.GetByNameAsync` to
build a snapshot and publishes a `ResolvedOverlayTextChangedV1` per referencing
overlay. It is a read plus more publishes — no writes — so it is safe under the
new ordering, and it is the one to watch in review because it is the only one
whose behaviour depends on *when* it runs.

**Alternatives considered**:

- *Keep the order and make the dispatch durable separately* — a pending-events
  table written in the same transaction, drained by a worker. This is a
  hand-rolled outbox wearing a different hat (see R1) and adds a second store to
  reconcile with Wolverine's.
- *Publish inside the aggregate's `SaveAsync` but before `SaveChangesAsync`,
  without the outbox* — closes nothing; the publish still leaves the transaction
  immediately.

---

## R3 — How does one change cover nine repositories without becoming nine changes?

**Decision: change the seam, not the call sites. `IEventBus` becomes
outbox-backed; the repositories change only in which method they call to
commit.**

The checklist flagged this as the scope risk, and the answer is the shared seam
that already exists. `IEventBus` (Shared.CQRS) is what every domain-event handler
publishes through, and `WolverineEventBus` (ServiceDefaults) is its single
implementation. Replacing that implementation with one that publishes into the
ambient `IDbContextOutbox` covers all twelve handlers at once, without any
Application code changing.

What each repository changes is one line — `SaveChangesAsync` becomes the
outbox's save — plus moving the dispatch above it. Nine near-identical edits,
mechanical, and reviewable side by side.

**The registration is the only per-context part.** `IDbContextOutbox<T>` is
generic in the `DbContext`, so each context's Infrastructure module binds its own.
That is one line per module in a file each context already has.

**FR-007 (a later write path must not silently lose the guarantee)** is the part
that cannot be solved by a seam alone, and the honest options are:

- an architecture test asserting no repository calls `SaveChangesAsync` directly
  (mechanical, catches the common case, cheap);
- making the unenrolled publish path impossible to reach by removing it.

The plan takes the first and records why not the second: `IMessageBus` remains
legitimately in use by Wolverine's own handlers, so removing it is not available.

---

## R4 — What does this cost on the latency budget?

**Decision: one extra INSERT inside the transaction that is already open; no
extra round trip on the hot path. Measured, not asserted (FR-011, FR-012).**

Today: `SaveChangesAsync` (one round trip) + a broker publish (one network hop,
outside the transaction).

Afterwards: `SaveChangesAndFlushMessagesAsync` writes the outbox rows in the
same transaction and the same round trip as the domain rows, then releases the
messages to the sending agent — which is in-memory hand-off, not a synchronous
broker round trip.

So the change *removes* a synchronous network hop from the critical path and
adds rows to a write already happening. The expectation is neutral-to-better,
and spec 020 leaves a harness that measures exactly this
(`IngestThroughputMeasurementTests`, before/after through an identical harness).
Expectation is not evidence: FR-012 requires the comparison.

**The one place to watch**: the outbox rows land in a per-context schema
(`wolverine_<context>`) in the same database. On the ingest path, a batch of 200
events becomes 200 outbox rows in the same transaction as 200 event rows. That
doubles the row count per commit on the highest-volume path in the product.

---

## R5 — What stops a permanently undeliverable message becoming a new outage?

**Decision: Wolverine's own retry and dead-letter handling, with the queue depth
made observable (FR-008, FR-009, FR-010).**

This feature moves the failure from "lost immediately and silently" to "retried
durably", which converts one problem into a different one: a message nothing can
deliver is now retried for ever, and its rows accumulate.

Wolverine has this already — retry policies and a dead-letter queue — and spec
020 fought exactly this fight one layer up, where the answer was a stated,
time-based bound plus a durable record before release. The same shape applies,
and this feature does not need to invent it.

What it does need is **visibility**, because an outbox that is quietly growing
looks identical to one that is empty until the disk fills. FR-008 asks for the
count and the age of the oldest pending message; that is a query against the
outbox tables and belongs in the health/metrics surface the contexts already
expose.

**Deliberately not solved here**: choosing the retry bound per message type.
Wolverine's defaults apply until something demonstrates they do not fit, and
spec 020's experience says the bound should be a stated duration rather than a
count when it does get chosen.

---

## Open questions for the plan

None blocking. Two flagged for the tasks phase:

1. **R2's ordering inversion is the review-critical change.** Every task that
   touches a repository should state that handlers now run pre-commit, so a
   reviewer checks side effects rather than skimming a one-line diff.
2. **The `VariableValueChangedDomainEventHandler` read.** It is safe as analysed,
   and it is the single handler whose correctness depends on the new ordering
   rather than being indifferent to it. It deserves its own test.
