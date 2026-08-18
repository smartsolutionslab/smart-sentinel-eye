# Implementation Plan: An event is never accepted until it is stored

**Branch**: `020-durable-ingest-ack` | **Date**: 2026-08-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/020-durable-ingest-ack/spec.md`

## Summary

Ingest answers "accepted" before it has stored anything, so an event can be
lost after the sender has been told otherwise — during a brief database
interruption, which discards every event in the window, or on a restart, which
discards the in-memory buffer without logging anything at all.

The fix is to move the acknowledgement to after the write, using each ingress's
own mechanism:

1. **Broker deliveries** are acknowledged only once stored. MQTTnet's manual
   acknowledgement lets the envelope carry its acknowledgement through the
   existing channel; the persistence loop commits a **batch** and then
   acknowledges that batch. An unacknowledged delivery is redelivered — which
   is what QoS 1 is for and what the system currently discards.
2. **Direct submissions** persist before answering, and answer **201 Created**
   instead of 202. These are control-plane writes; the channel exists for
   plant-floor bursts, not for them.
3. **A bounded escape** stops one permanently unstorable delivery from
   redelivering forever: after a stated number of attempts it goes to
   `dead_letters` and is acknowledged.

**The unglamorous part is the important one**: mosquitto's
`max_inflight_messages` is unset and therefore 20. Deferring acknowledgement
makes that the ingest ceiling. It has to be raised, in the broker config, or
this feature caps production ingest at a fraction of its requirement while
looking correct in a test — see [research.md](./research.md) §R1.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: MQTTnet 4.3 (manual acknowledgement), EF Core 10 +
Npgsql, ASP.NET Core Minimal APIs, mosquitto (broker configuration)

**Storage**: PostgreSQL — `events` (partitioned per fab, spec 019),
`dead_letters` (spec 006/018)

**Testing**: xUnit + Shouldly, hand-written fakes; integration against the
Aspire fixture (ADR-0103), including deliberate database interruption and
process kill

**Target Platform**: Linux containers — Aspire in dev, k3s + Helm in prod

**Project Type**: Backend bounded context plus a broker configuration change

**Performance Goals**: sustained **5 000 events/s** ingest (spec 006), per-source
FIFO preserved, arrival-to-visible within its share of the 800 ms end-to-end
budget (constitution §IV, "event → overlay state ≤ 200 ms")

**Constraints**: no per-message database round trip; the in-flight window is a
hard ceiling on unacknowledged deliveries; one unstorable event must never block
the rest

**Scale/Scope**: single-digit fabs, 250 cameras, ~1k events/s nominal with 5k/s
bursts

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. On-prem first** | No change. No new dependency; the broker is already deployed. |
| **II. DDD with value objects** | No change. The envelope and its identifier are untouched; only the timing of the acknowledgement moves. |
| **III. Bounded context isolation** | Entirely within EventIngestion plus AppHost's broker config. No cross-context reference. |
| **IV. The latency budget is sacred** | **The gate that shapes this design.** This path is the "event → overlay state ≤ 200 ms" leg. Batched acknowledgement adds at most one batch window; the requirement is that it is **measured before and after**, not asserted — FR-012, and the reason R1 exists at all. |
| **V. Spec-driven development** | Phase 2 of the loop; spec and checklist complete, no open clarifications. |
| **VI. Aspire is the composition root** | The broker's `max_inflight_messages` is set in the AppHost-mounted config, alongside the ACL and password file. |
| **VII. Observability** | An interruption, its recovery, the count of affected events, and every poison escape are logged through `[LoggerMessage]`. FR-006 makes the count part of the requirement, not a nicety. |
| **VIII. Safe by default at trust boundaries** | Improved: a delivery is no longer confirmed until it is durably ours. Nothing new is trusted. |
| **IX. Forward-compatible interfaces** | `IIngestChannel` gains a completion signal rather than a second channel type, so a future durable buffer can be substituted without touching either ingress. |

**Result: PASS**, with §IV as the live risk rather than a formality —
Complexity Tracking is omitted because there is no violation, not because
there is no risk.

**Re-check after Phase 1: PASS, unchanged.** The design adds a batch commit and
a completion callback; neither crosses a boundary, and the latency requirement
is carried into the tasks as a measurement rather than a claim.

## Project Structure

### Documentation (this feature)

```text
specs/020-durable-ingest-ack/
├── plan.md              # This file
├── research.md          # Phase 0 — six decisions, one of which changes the cost
├── data-model.md        # Phase 1 — no schema change; the acknowledgement's lifetime
├── quickstart.md        # Phase 1 — how to observe it, including a kill mid-burst
├── contracts/
│   └── ingest.md        # Phase 1 — 201 vs 202, the 429 replacement, the escape
├── checklists/
│   └── requirements.md  # Phase 1 output of /speckit-specify
└── tasks.md             # Phase 3 — NOT created by this command
```

### Source Code (repository root)

```text
src/
  EventIngestion/
    Application/
      Ingress/
        IIngestChannel.cs                  # CHANGED — envelopes carry a completion signal
        BoundedIngestChannel.cs            # CHANGED — same channel, richer item
        EventEnvelope.cs                   # unchanged
    Infrastructure/
      Ingress/
        MqttSubscriberHostedService.cs     # CHANGED — AutoAcknowledge off; ack travels with the envelope
        PersistenceLoopHostedService.cs    # CHANGED — batch, commit, then acknowledge; bounded retry; poison escape
    Api/
      EventsEndpoints.Writes.cs            # CHANGED — persist before answering; 201
      EventsEndpoints.cs                   # CHANGED — declare 201, keep 429, declare 503

  AppHost/
    mosquitto/mosquitto.conf               # CHANGED — max_inflight_messages, the ceiling this feature exposes

tests/
  EventIngestion.Infrastructure.Tests/     # batch acknowledgement, retry bound, poison escape
  Integration.Tests/EventIngestion/        # outage recovery, kill mid-burst, duplicate collapse, 201
```

**Structure Decision**: No new projects and no new tables. The change is
concentrated in the two ingress paths and the loop between them, plus one line
of broker configuration that the rest of the design depends on.
