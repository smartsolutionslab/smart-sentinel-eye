# Implementation Plan: The first event after a restart reaches its effect in time

**Branch**: `023-first-event-cold-start` · **Spec**: [spec.md](./spec.md) ·
**Date**: 2026-08-19 · **Issue**: #1655

## Summary

Find out where twelve to fourteen seconds go on the first event after a restart,
then close the gap if the cause is addressable. **The attribution is the
deliverable**; the fix is conditional on it.

Phase 0 turned up the thing that decides the shape of this work: **the journey is
not traced at all**. Tracing covers inbound and outbound HTTP, which is the part
of this system that is not on the event-to-overlay path. So the sequence is
forced — make the path observable, measure it, attribute the seconds, and only
then consider changing anything.

## Technical Context

**Language**: C# 13 / .NET 10 · **Testing**: xUnit + Shouldly, Aspire fixture
(ADR-0103) · **Messaging**: Wolverine 6.24.2 over RabbitMQ · **Observability**:
OpenTelemetry → OTLP (ADR-0026)

**Performance goal**: first event after restart under 1 s (SC-004), steady state
no worse than 267–348 ms (SC-005).

**Constraints**: no production change may precede the measurement that justifies
it; no test may be weakened to improve a figure (FR-008); warming must not let a
service report ready before it can serve (FR-006).

**Scale**: one event, one restart. Not a load feature.

## Constitution Check

| Principle | Status |
|---|---|
| I. On-prem first | Unaffected. |
| II. DDD with value objects | Unaffected — no domain changes anticipated. |
| III. Bounded context isolation | Unaffected. Instrumentation is per-service, in ServiceDefaults; no cross-context reference. |
| IV. **The latency budget is sacred** | **The reason this feature exists.** It serves §IV directly. |
| V. Spec-driven development | Followed. |
| VI. Aspire is the composition root | Respected — nothing wired by hand. |
| VII. **Observability is non-negotiable** | **Currently violated, and this feature is the correction.** See below. |
| VIII. Safe at trust boundaries | Unaffected. |
| IX. Forward-compatible interfaces | Unaffected. |

### §VII is not merely relevant — it is already broken

The constitution says, in terms:

> Latency-budget dashboards (per ADR-015) are mandatory. **A leg without a
> dashboard cannot ship.**

The `event → overlay state ≤ 200 ms` leg has no dashboard and, as Phase 0 found,
**no traces on any of its hops**. Wolverine publishes, RabbitMQ transit, handler
execution and database calls are all uninstrumented. The leg shipped anyway,
across specs 006, 007, 013, 020, 021 and 022.

Two things follow. First, adding the instrumentation is **not scope creep into
this feature — it is overdue compliance**, and it happens to be the only way to
satisfy FR-001. Second, this is worth an explicit decision rather than a quiet
fix: whether the gap deserves an ADR amendment, or a follow-up issue for the
dashboard half, is a judgement for the reviewer. **Recorded rather than
resolved here**, because "we noticed a constitutional principle was unmet and
silently patched a corner of it" is not an outcome anyone should be happy with.

**No exception is requested.** Nothing in this plan asks to bend a principle.

## Approach

Four steps, in an order chosen so that no change is made before the evidence
that justifies it exists.

### 1. Split the journey for free, before changing anything

Bucket the elapsed time with observables that already exist — publish → event
readable through the read API → variable readable with its new value. Two
buckets: *ingress and store* against *announce, decide and apply*.

This cannot satisfy SC-001 on its own; it decides which half to look at, and it
survives afterwards as an independent cross-check on the span data. If the spans
and the buckets ever disagree, one is wrong, and that is much better found here
than inside a conclusion.

### 2. Make the hops observable

Register Wolverine's activity source in `ServiceDefaults` so publishes, transit
and handler execution appear as spans with context propagated across services.
Wolverine is already referenced, so this is a source registration rather than a
new dependency.

Database spans are **deliberately deferred**: Npgsql instrumentation would be a
new package, and it should not ride in on the back of this feature unless step 1
or 3 shows the database owns time. If it does, that is evidence, and the package
comes in on the strength of it.

Subject to FR-005 like anything else: measure the warm path before and after,
because instrumentation is not free and an observer effect here would be a
finding rather than an inconvenience.

### 3. Attribute the seconds, and test the hypothesis that explains the shape

Restart, send three events, read the spans. Attribute at least 80% of the
elapsed time to named stages (SC-001) and explain the decay across the three
events rather than only the peak (SC-002).

The leading hypothesis from Phase 0 — first-publish-per-message-type cost from
`AutoProvision` plus conventional routing — is the only candidate that predicts a
staged curve rather than a single step, because the journey carries three
distinct message types and different tests send them first. **It is falsifiable
and must be recorded either way** (FR-003): publishing each type once at startup
should collapse the curve, and if it does not, the hypothesis is wrong and says
so.

Every candidate in #1655 gets a verdict, including the two Phase 0 already
weakened.

### 4. Then, and only then, decide what to change

If the cause is addressable — most likely by warming the path during startup
rather than lazily on the first real event — do it, and state where the cost
lands. If a service now takes longer to start, say how much, and confirm it does
not report ready before it can serve (FR-006, FR-007).

If it is not addressable, record why, with the residual risk to the budget
(FR-010). **This is a permitted ending**, and the plan does not treat it as
failure.

### One question to settle on the way

Phase 0 found that `InMemoryRuleCache` is only populated by publish commands.
What fills it when Automation restarts with rules already Active in the
database? If something hydrates it at startup, that cost is in the restart path
and belongs to this feature. If nothing does, **that is a correctness bug
materially more serious than this latency one** — rules silently stop firing
after a restart — and it should be filed immediately rather than folded in here.

Cheap to answer, so it gets answered early.

## Project Structure

### Documentation

```
specs/023-first-event-cold-start/
├── spec.md
├── research.md          ← Phase 0, complete
├── plan.md              ← this file
├── quickstart.md        ← Phase 1
├── tasks.md             ← /speckit-tasks
├── verification.md      ← Phase 5
└── checklists/requirements.md
```

No `data-model.md`: there is no model here. No `contracts/`: no interface
changes are anticipated, and if the outcome turns out to need one, that is a
finding worth raising rather than a file to fill in now.

### Source code

Expected to be touched:

```
src/ServiceDefaults/Extensions.cs         trace source registration (step 2)
src/ServiceDefaults/WolverineDefaults.cs  startup warming, only if step 3 justifies it
tests/Integration.Tests/Automation/       the existing measurement, extended (FR-011)
```

Everything beyond that is contingent on what the measurement says, and is
deliberately not predicted here.

## Complexity Tracking

No constitutional exception requested. One item worth a reviewer's eye:

| Item | Why it is here | Why it is not scope creep |
|---|---|---|
| Registering a trace source in `ServiceDefaults` | FR-001 cannot be met without it; the hops are invisible | §VII already mandates it, and the leg shipped without it. Compliance, not expansion. |

## Risks

**The measurement changes the thing measured.** Tracing costs something and its
first export may itself be slow. Handled by measuring the warm path before and
after, and by keeping step 1's instrument-free buckets as a cross-check.

**The answer is "the fixture, not the system".** Nine services and a broker on
one host is not a fab. If the cold cost turns out to be contention for one
machine's CPU, that is a real finding and the honest conclusion is that this does
not reproduce in production — which SC-004's second clause already permits. It
would still leave the observability gap closed, which is worth having.

**The fix is tempting before the evidence.** Warming the path is a small, obvious
change that would make the number drop. Doing it first would leave nobody able to
say what the seconds had been. The spec puts the attribution at P1 for this
reason and the task order must not quietly invert it.
