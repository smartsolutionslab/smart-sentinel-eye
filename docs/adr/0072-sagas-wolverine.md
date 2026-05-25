# ADR-0072: Sagas — Wolverine State Machines with Compensating Actions

**Status:** Accepted
**Date:** 2026-05-25

## Context

Automation rules trigger multi-step sequences: a scheduled camera
rotation switches the displayed camera every 10 s for 5 minutes; a
fault-trigger rule may switch layout, change cells, raise an overlay,
and schedule a reset. These long-running sequences need durability
across service restarts and compensation on failure.

## Decision

**Wolverine sagas** model multi-step automation sequences.

- Each saga is a class deriving from Wolverine's `Saga` base.
- State persisted in Postgres via Wolverine.
- Transitions are explicit handler methods.
- Failure of any step triggers compensating actions in reverse order
  (where defined).
- Sagas use `bus.ScheduleAsync(...)` for delayed steps.

```csharp
public sealed class ScheduledCameraRotationSaga : Saga
{
    public Guid Id { get; set; }
    public RotationState State { get; set; } = RotationState.NotStarted;
    public List<CameraId> Sequence { get; set; } = new();
    public int Index { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    public async Task Handle(StartRotation msg, IMessageBus bus, CancellationToken ct) { ... }
    public async Task Handle(AdvanceRotation msg, IMessageBus bus, CancellationToken ct) { ... }
    public async Task Handle(RotationFailed msg, IMessageBus bus, CancellationToken ct) { ... }
}
```

- **Single-step automation rules do NOT use sagas** — the saga
  overhead is reserved for actual sequences.
- **Compensation logic explicit per step.** No magical rollback.

## Consequences

- **Positive:** resumable across restarts.
- **Positive:** explicit state machine is debuggable and reviewable.
- **Negative:** saga overhead (persistence I/O per transition) — only
  apply where multi-step durability matters.

## Alternatives Considered

- **Choreography (events fire next steps)** — tangled debugging; no
  central state.
- **Hand-rolled orchestrator** — re-implements Wolverine's mechanism.
- **Workflow engine (Temporal)** — too heavy for our scale.
