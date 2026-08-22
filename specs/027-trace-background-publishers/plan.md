# Implementation Plan: Every journey has a beginning, not just the ones from the plant floor

**Branch**: `027-trace-background-publishers` · **Spec**: [spec.md](./spec.md) ·
**Date**: 2026-08-22 · **Issue**: #1781

## Summary

Give the two remaining background publishers a cause, so nothing in this system
announces anything as an orphan.

Two call sites, and **they take opposite-looking placements from the same rule** —
which is the whole of the design and the thing a reader will trip on. See
research.md, Findings 1 and 2.

No new machinery: `IJourneyOrigin` shipped in spec 026 and is registered for
every context by `AddWolverineForContext`. If this grows past two call sites and
their tests, the diagnosis is wrong rather than the estimate.

## Technical Context

**Language**: C# 13 / .NET 10 · **Messaging**: Wolverine over RabbitMQ with a
Postgres outbox · **Telemetry**: OpenTelemetry → the Aspire dashboard (ADR-0118)
· **Testing**: xUnit + the Aspire fixture

**Constraints**: one journey per announcement, never per loop **and never for
work that did not happen** (FR-003, FR-006); failures marked at both sites
(FR-004); the Application layer must not own an `ActivitySource`; no regression
in poll cadence or retention duration, **measured twice** (FR-007); followable by
a person in the dashboard (FR-008, SC-007).

## Constitution Check

| Principle | Status |
|---|---|
| I. On-prem first | Unaffected. |
| II. DDD with value objects | Unaffected — diagnostics, not domain state. |
| III. Bounded context isolation | Respected. Both contexts get the behaviour through `Shared.CQRS`; neither references `ServiceDefaults`. |
| IV. Latency budget | **Not on the path.** Neither publisher is on the event-to-overlay path. FR-007 guards the poll cadence, which is a different budget and worth not eroding. |
| V. Spec-driven development | Followed. Phase 0 checked the two things the checklist flagged, and one of them changed the design. |
| VI. Aspire is the composition root | Unaffected. |
| VII. Observability is non-negotiable | Directly served — this completes the causality half for every publisher in the system. |
| VIII. Safe at trust boundaries | **Nothing crosses one.** No message changes, no header added; both changes are confined to their own process. |
| IX. Forward-compatible interfaces | Respected — no contract touched. |

**No exception requested.**

## Approach

### 1. Stream health: the domain event handler, not the loop

`StreamHealthChangedDomainEventHandler`, mirroring
`EventIngestedDomainEventHandler` exactly.

**The loop is wrong twice over here**, and only the first was anticipated: it
merges every camera in a sweep onto one origin, *and* it would create a journey
for every camera on every poll, because `PollOnceAsync` calls the command handler
unconditionally and the change detection lives in the aggregate
(`if (previous != State)` guards each raise).

So FR-003 and FR-006 hold **because of where the code goes**, not because
anything defends them. The tests assert that.

### 2. Audit retention: inside the loop, per chunk

There is no domain event handler here — `AuditRetentionHostedService` publishes
inline. The journey goes around one chunk's work in `ArchiveAndDropAsync`, which
already has the `try` that FR-004's failure marking needs.

**Say the asymmetry out loud in the code.** Two sites, same rule, opposite
placements: one journey per *announcement*, and in the watcher an iteration is
usually no announcement while here an iteration is exactly one. A reader arriving
from spec 026 will pattern-match on "domain event handler" and be wrong.

### 3. Mark failures at both

`IJourney.Failed(Exception)`, added by spec 026's code review three commits ago.
Omitting it would regress a fix younger than the feature it fixed — a journey
that ends unmarked is indistinguishable from a healthy one nothing subscribed to.

### 4. Record the survey where it will be found

FR-009 and SC-008 want the classification of every publisher written down, not
sampled. It goes in `verification.md` as a table. **Finding the orphans was the
expensive part of this feature**; leaving it undocumented means the next person
repeats the search.

### 5. Close the one remaining inference

Nine publishers are classified as fine. Message-driven inheritance is observed
directly; HTTP-driven is observed one layer short — a request Server span with
Client children, but no `send` captured under one. The verification walk boots
the stack anyway, so registering one camera and looking costs nothing.

**A task with an owner, not a footnote.** Nothing here depends on the answer: if
an HTTP publish turned out to be an orphan, that is a new finding and a new
issue.

### 6. Measure twice

FR-007 and SC-006. The health watcher runs continuously against every camera and
the retention run is periodic; neither should slow. **Twice**, because spec 026
nearly reported a regression that did not exist from one contaminated run, and
the recorded lesson was to repeat it.

## Project Structure

### Documentation

```
specs/027-trace-background-publishers/
├── spec.md
├── research.md          ← Findings 1–3; Finding 1 changed the design
├── plan.md              ← this file
├── quickstart.md
├── tasks.md
└── verification.md      ← carries the publisher survey (FR-009)
```

No `data-model.md` and no `contracts/`: no domain model, no contract change,
nothing added to any message.

### Source code

```
src/StreamDistribution/Application/EventHandlers/StreamHealthChangedDomainEventHandler.cs
src/AuditObservability/Application/Retention/AuditRetentionHostedService.cs
tests/StreamDistribution.Application.Tests/
tests/AuditObservability.Application.Tests/
```

Two files. That is the estimate and also the check on it.

## Complexity Tracking

No constitutional exception and nothing to declare.

## Risks

**Fixing the camera case and stopping.** Retention publishes inline, so it does
not look like the thing spec 026 fixed. US2 exists at P2 specifically so it
cannot be quietly dropped, and SC-008 makes the survey a deliverable.

**Putting the journey in the watcher's loop.** It is right there, it is fewer
lines, and it is wrong twice. Research Finding 1 is the reason this plan names
the handler instead.

**Shipping without failure marking.** A defect this programme has already shipped
once, in the feature this one extends.

**Believing the nine are fine because the spec says so.** The spec says so on
inference. Step 5 closes it, and the honest position until then is that it is
inferred.
