# Implementation Plan: A cross-service journey can be followed end to end

**Branch**: `026-follow-a-journey` · **Spec**: [spec.md](./spec.md) ·
**Date**: 2026-08-22 (rewritten) · **Issue**: #1750

## Summary

Give an ingested plant-floor event a cause, so the journey it starts has a
beginning.

**That is the whole change.** Everything downstream of it already works: the
library propagates the relationship across services and through the outbox
today, demonstrated over a 4.3-second store-and-forward wait. What it has
nothing to work with is the one publish that happens outside any activity.

This is the third version of this plan and much the smallest. The first two were
planned against causes that turned out to be false; see spec.md's closing
section.

## Technical Context

**Language**: C# 13 / .NET 10 · **Messaging**: Wolverine 6.24.2 over RabbitMQ
with a Postgres outbox · **Telemetry**: OpenTelemetry → the Aspire dashboard
(ADR-0118) · **Testing**: xUnit + the Aspire fixture

**Constraints**: one cause per event, never per batch (FR-006, SC-005); do not
duplicate propagation that already works (FR-007); no ingest-throughput or
latency regression, measured (FR-009); the spec-025 measurement stays untouched
and telemetry-independent (FR-010); followable by a person in the dashboard
(FR-008, SC-007).

## Constitution Check

| Principle | Status |
|---|---|
| I. On-prem first | Unaffected. |
| II. DDD with value objects | Unaffected — diagnostics, not domain state. |
| III. Bounded context isolation | Respected, and it shapes the design: the Application layer gets an interface, `ServiceDefaults` provides it. Same arrangement as `IEventBus` and `ILatencyBudget`. |
| IV. Latency budget | **Guarded, not served.** The ingest path runs this 5 000×/s at design load, so FR-009 is a measurement rather than a formality. |
| V. Spec-driven development | Followed, including stopping mid-implementation to re-spec when Phase 2 falsified the premise. |
| VI. Aspire is the composition root | Unaffected. |
| VII. Observability is non-negotiable | Directly served — this is the causality half of §VII, and the half that made spec 023's investigation fail. |
| VIII. Safe at trust boundaries | **Nothing new crosses one.** No header is added and no message changes; the change is confined to one process. This is strictly better than the previous plan. |
| IX. Forward-compatible interfaces | Respected — an event without a cause behaves as today. |

**No exception requested.**

## Approach

### 1. Give the ingestion publish an activity — one per event

`EventIngestedDomainEventHandler` translates one domain event into one
integration event. `DomainEventDispatcher` invokes handlers **sequentially, one
domain event at a time**, so an activity started there is naturally per-event
and FR-006 falls out of the structure rather than being defended by care.

That matters: the tempting place is the batch, where 200 deliveries are stored
together. A batch-level activity is less code, produces a joined trace, and
would satisfy US1 by eye while collapsing two hundred unrelated journeys onto
one parent.

### 2. Put the source where the other cross-cutting telemetry lives

The Application layer must not own an `ActivitySource` — same reasoning that put
`ILatencyBudget` in `Shared.CQRS` with its implementation in `ServiceDefaults`.
Mirror that arrangement rather than inventing a second one.

**Probably no OpenTelemetry configuration change at all**: `Extensions.cs`
already registers `AddSource(builder.Environment.ApplicationName)`, so a source
named for the application is exported today. Verify that rather than assume it —
spec 024 assumed a registered source meant visible spans and lost two days.

### 3. Add nothing to the messages

FR-007. The relationship travels on `Envelope.ParentId`, which Wolverine already
sets from the ambient activity, already persists through the outbox, and already
reads back on receive. **Measured, not assumed** — see research.md, Findings 3
and 4.

### 4. Prove it end to end, and by eye

The tests matter more here than in the previous plan, because most of FR-001's
behaviour is now **inherited rather than built** — which is exactly the kind of
thing that regresses without anyone noticing. And SC-001/SC-007 are about a
person reading the dashboard; spec 024's precedent is the reason that is a task
and not an assumption.

### 5. Measure the ingest path

FR-009 and SC-006. Starting an activity per event is cheap but not free, and
this runs 5 000 times a second at design load. Compare against the recorded
267–369 ms and against the ingest throughput the batching exists to protect.

## Project Structure

### Documentation

```
specs/026-follow-a-journey/
├── spec.md              ← rewritten; keeps what the first version got wrong
├── research.md          ← Findings 1–4; 3 and 4 are the ones that count
├── plan.md              ← this file (third version)
├── quickstart.md
├── tasks.md
└── verification.md
```

No `data-model.md` and no `contracts/`: no domain model, and **no contract
change at all** — a deliberate difference from both spec 025 (which needed a
contract field, because a metric must be computed in-process where telemetry
cannot help) and from this plan's own previous version (which was going to add a
header to every message in the system).

### Source code

```
src/Shared.CQRS/                    the interface the Application layer sees
src/ServiceDefaults/                the ActivitySource behind it + registration
src/EventIngestion/Application/EventHandlers/EventIngestedDomainEventHandler.cs
tests/ServiceDefaults.Tests/        per-event behaviour, no-cause behaviour
tests/Integration.Tests/            the journey, followed, through the outbox
```

Smaller than the previous plan, which was itself billed as small. If it grows
past this, something is wrong with the diagnosis rather than with the estimate.

## Complexity Tracking

No constitutional exception, and **nothing to declare** — the previous version
of this table carried "a header on every outgoing message"; that item is gone.

## Risks

**Doing it per batch.** The single most likely wrong turn: cheaper, looks
identical in the direction anyone checks first, and destroys US2. FR-006 and
SC-005 exist for this and the tests must assert it directly.

**Assuming the span is exported because the source is registered.** Spec 024
did exactly this and lost two days to an untrusted dev certificate. Look at the
dashboard.

**Testing it in-process.** A test that publishes and handles without going
through `wolverine_outgoing_envelopes` proves nothing about the hop, and would
have passed before this change.

**Costing the ingest path.** 5 000 events/s at design load. Measure it.

**Believing this plan because it is the third.** Three premises have already
been falsified here, each of which read as settled. The findings behind this one
are experiments rather than arguments — which is the reason to trust it, and the
standard the next claim should meet too.
