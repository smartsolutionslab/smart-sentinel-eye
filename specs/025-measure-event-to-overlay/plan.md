# Implementation Plan: The event-to-overlay leg can be measured

**Branch**: `025-measure-event-to-overlay` · **Spec**: [spec.md](./spec.md) ·
**Date**: 2026-08-22 · **Discharges**: constitution §VII for one leg (ADR-0117)

## Summary

Make `event → overlay state ≤ 200 ms` measurable, so `develop` stops violating
the constitution amended yesterday.

Phase 0 changed the shape of the answer. The spec assumed the acceptance moment
must be carried on a message, and sized that: **one optional field, non-breaking,
15 call sites untouched.** But re-reading spec 024's own trace captures shows the
trace context is lost at the same hop, which means there may be a general
mechanism that removes the need for a private one.

**So the first task is a question, not a change**, and it costs one event and one
trace to answer.

## Technical Context

**Language**: C# 13 / .NET 10 · **Messaging**: Wolverine over RabbitMQ with a
Postgres outbox · **Telemetry**: OpenTelemetry, Wolverine's activity source
registered by spec 023, metrics meter by spec 024 · **Testing**: xUnit + the
Aspire fixture

**Performance goal**: instrumentation under 5% of the 200 ms budget (SC-005);
steady state no worse than 267–369 ms (SC-004).

**Constraints**: no existing field's meaning may change (FR-002); a missing
moment records nothing (FR-005); a negative duration records nothing (FR-006);
the figure is reconciled against spec 022's independent test before use (FR-007).

## Constitution Check

| Principle | Status |
|---|---|
| I. On-prem first | Unaffected. |
| II. DDD with value objects | The carried moment, if any, is transport metadata rather than domain state. |
| III. Bounded context isolation | Respected — `Shared.Contracts` is the sanctioned channel, and the instrument stays in `ServiceDefaults`. |
| IV. **Latency budget is sacred** | Directly served: this makes one leg enforceable rather than asserted. |
| V. Spec-driven development | Followed. |
| VI. Aspire is the composition root | Unaffected. |
| VII. **Observability is non-negotiable** | **This feature exists to discharge it** for the leg it covers. It delivers the measurement; the dashboard half stays open on #1707, and SC-008 makes §IV's table say so. |
| VIII. Safe at trust boundaries | Unaffected. |
| IX. Forward-compatible interfaces | Respected — an optional field, if used, is additive in both directions. |

**No exception requested.** One thing for the reviewer: this feature will leave
§VII **half**-discharged for this leg — measured, not dashboarded — and that is
deliberate, because building a dashboard would settle ADR-0026 (Locked) by
implementation.

## Approach

### 1. Ask the question before making the change

Send one event, read one trace, and establish what Wolverine does with trace
context across the outbox: propagates it, links it, or drops it.

The answer decides the feature:

- **Context available** → derive the leg from spans. No contract change.
- **Linked, not parented** → follow the link. Probably no contract change.
- **Dropped** → carry the moment on the message, as the spec assumed, and file
  Finding 2 as its own issue.

Building the timestamp without asking would risk adding a private mechanism
beside a general one that already works.

### 2. Make the leg measurable, by whichever route step 1 indicates

Record acceptance-to-application into spec 024's `LatencyBudget` as a
distribution, with `is_whole_leg` set **true** — and true.

Two silent failure modes are guarded explicitly rather than left to chance: a
missing moment records nothing rather than zero, and a negative duration records
nothing rather than a sub-zero latency. Both would flatter the dashboard, which
is the direction this programme has been caught by four times.

### 3. Reconcile before quoting

Compare the instrument against spec 022's `EventReachesItsEffectsTests`, which
measures the same journey from outside. Agreement within 20% (SC-003), or the
discrepancy explained.

**This is the step that catches measuring a fragment**, and spec 024 showed the
value of having it: its equivalent check fired before any code was written.

### 4. Establish the cost

Before and after, same method. The exporter is attached in the fixture — spec 024
T002 established that, retiring a caveat spec 023 had to leave open — so the
comparison means something.

### 5. Update the constitution's table

§IV's leg-state table gains a true row for this leg: measured yes, dashboard no.
ADR-0117 warned that a stale row exempts a leg by clerical error; the first
chance to prove that discipline is the feature that changes a row.

## Project Structure

### Documentation

```
specs/025-measure-event-to-overlay/
├── spec.md
├── research.md          ← Phase 0, complete
├── plan.md              ← this file
├── quickstart.md        ← Phase 1
├── tasks.md
├── verification.md
└── checklists/requirements.md
```

No `data-model.md` — no model changes. `contracts/` only if step 1 says the
timestamp route is needed, in which case the added field is documented there.

### Source code

Contingent on step 1:

```
src/Shared.Contracts/EventMetadata.cs      only on the timestamp route
src/Automation/Application/EventHandlers/  forwards the moment, or nothing
src/SystemVariables/…, src/LayoutComposition/…   record on apply
src/ServiceDefaults/LatencyBudget.cs       already exists; gains a whole-leg segment
tests/Integration.Tests/                   reconciliation + cost
```

## Complexity Tracking

No constitutional exception. One judgement for the reviewer:

| Item | Why | Why proportionate |
|---|---|---|
| Possibly a field on shared `EventMetadata` | The leg spans four services and no other carrier exists | Optional, additive, non-breaking both ways; 15 call sites compile untouched — established in Phase 0, not assumed |

## Risks

**Measuring a fragment and calling it the leg.** The failure this spec is most
exposed to, and the reason US2 is P1 rather than a refinement. Guarded by step 3.

**The timestamp route gets taken because it is familiar.** Step 1 exists to stop
that. It is one event and one trace; skipping it to "just add the field" would be
choosing the private mechanism without looking at the general one.

**The p99 will be ugly.** #1655's twelve-second first event lands in the
distribution by design. Someone will want to exclude it; the spec's position is
that a flatter, less true dashboard defeats the purpose.

**Half a principle discharged reads as a whole one.** Measured-but-undashboarded
must show in §IV's table as exactly that, or the next reader concludes §VII is
satisfied here when it is not.
