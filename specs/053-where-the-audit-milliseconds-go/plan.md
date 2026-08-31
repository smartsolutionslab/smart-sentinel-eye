# Implementation Plan: Where the audit pipeline's milliseconds go

**Branch**: `053-where-the-audit-milliseconds-go` | **Date**: 2026-08-31 | **Spec**: [spec.md](./spec.md)

**Input**: [spec.md](./spec.md) · **Research**: [research.md](./research.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/the-attribution.md](./contracts/the-attribution.md) · **Quickstart**: [quickstart.md](./quickstart.md)

---

## Summary

Audit ingest is measured at a p99 of **85 ms** against a **50 ms** requirement.
Three levers have been applied or rejected; both recorded conclusions then reach
for the same pair of options — production topology that does not exist, or moving
the requirement — **without anyone knowing where the 85 milliseconds go.**

**This produces that breakdown and stops.** It changes no budget and recommends
no course of action.

**Two decisions carry the plan.** The clock question is *removed* rather than
bounded, because all nine databases sit on one Postgres server and that server is
a reference clock nobody had thought to use. And the attribution is carried on
the audit row rather than read from traces, because the development trace list is
effectively unsearchable and the existing percentile query already reads that
table.

---

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: existing — Wolverine, EF Core, Postgres. **Nothing new.**

**Storage**: Postgres. Nullable measurement columns on the audit row, written only when a switch is on

**Testing**: the existing `Category=Measurement` integration test, extended; excluded from CI as it already is

**Target Platform**: the development Aspire stack. **There is no production deployment** (ADR-0130)

**Project Type**: measurement, with a small production-side change to carry the timestamps

**Performance Goals**: none. This measures; it does not improve

**Constraints**: the requirement's budget is untouched; the apparatus' own cost is reported

**Scale/Scope**: 100 events per second sustained, three runs minimum

---

## Constitution Check

*GATE: passed before Phase 0, re-checked after Phase 1.*

| Principle | Assessment |
|---|---|
| **NFR-001** | Untouched. Measured, not moved — FR-010 forbids changing it and the measurement test stays excluded while it fails. |
| **§VII observability** | One sink per environment (ADR-0118) is unchanged. This adds **no** sink, dashboard or exporter. The open question about what a dashboard obligation means (issue 1940) is explicitly out of scope. |
| **§IV latency budget** | Different budget entirely. Audit ingest is not on the event-to-overlay path. |
| **DDD / no cross-context references** | AuditObservability only. |
| **ADR-0050 telemetry** | Followed; nothing new is emitted. |
| **ADR-0065 coverage gates** | **Live** — Domain and Application of AuditObservability are touched. 90% / 80% apply and a coverage number is legitimate evidence. |
| **Karpathy: smallest change** | The production change is nullable columns and a switch. The apparatus lives in the test harness. |

**No ADR contradicts this** (research §R0). ADR-0127 is the precedent for the
shape: build, measure, decline — recorded either way.

---

## Approach, and the two decisions that carry it

### 1. The clocks: removed, not bounded

`AppHost.cs` declares **one Postgres server with nine databases**. Every service
in this pipeline already shares a clock; nobody had used it as one.

Each participating process asks the database for its time and compares with its
own. The difference is that process's offset from a common reference; the
difference of two offsets is their relative skew — **measured, not assumed.**

**The existing stamps are not re-sourced.** `OccurredAt` and `ReceivedAt` keep
coming from `IClock`. Changing them to database time would alter production
behaviour to suit a measurement, and would break comparison with every figure
already recorded.

**Residual uncertainty is the round trip's**, halved by the standard correction
and reported rather than assumed away. If it cannot be brought under the 10 ms
the specification asks for, **the attribution is reported as not established** —
that is SC-003, and it is a result rather than a failure.

### 2. The attribution: carried on the row, not read from traces

The development trace list is effectively unsearchable, so attributing a thousand
events through it means hunting history rather than provoking a specific event.
The existing percentile query already reads the audit store; putting the parts
beside the total means **one query answers the whole question** and the numbers
cannot drift apart, because they come from one row.

**The honest cost**: this places measurement apparatus on the production write
path. That is why it is behind a switch, off by default, and why FR-009 requires
the apparatus' own cost to be measured rather than argued.

### 3. The parts, and which the requirement names

| Part | In the requirement? |
|---|---|
| publisher transaction → outbox | **no** — front overhang |
| outbox → broker | **no** — front overhang |
| broker → handler entry | yes |
| handler entry → row stamped | yes |
| stamp → row committed | yes, and **unmeasured today** |

The requirement names the last three. What three ADRs have quoted is the first
four. Both are reported, with the difference attributed at each end.

---

## Done, per story — before any code

| Story | Verifiable criterion |
|---|---|
| **US2** *(gates US1)* | A measured bound on the relative offset between the stamping processes, with its residual stated. Under 10 ms, or the attribution is declared not established. |
| **US1** | For three runs at ~100 ev/s: a figure per part, summing to the total, with any remainder reported as unattributed — and each part marked inside or outside the requirement's span. |
| **US3** | Someone who has not seen this work can read the record and say where the milliseconds go and how far to trust it. |

None of these is satisfied by "the pipeline got faster". Nothing here is supposed
to make it faster.

---

## What the checks will and will not prove

| Claim | Proved by | **Not** proved by |
|---|---|---|
| The clocks agree closely enough | comparing each process against a shared reference | both being on one machine |
| Where the span goes | parts summing to the total | a total and an intuition |
| The requirement's span vs the measured one | reporting both | treating them as interchangeable, as three ADRs did |
| The apparatus is cheap | running with it off and on | it looking cheap |
| The figures are stable | three runs and their spread | one run |
| **That the requirement is achievable** | **nothing — not the question** | a breakdown that suggests a lever |
| **Anything about production** | **nothing** | there is no production deployment |

**The trap specific to this feature**: a breakdown is persuasive. It will be
tempting to end with "and therefore…". FR-011 forbids it, and the reason is that
the same pull produced two recorded conclusions that skipped this step entirely.

---

## Risks

1. **The apparatus changes what it measures.** Stamping costs time on the path
   being timed. Measured both ways, and reported.
2. **The parts do not sum.** Reported as an unattributed remainder rather than
   distributed — an unexplained gap is a finding.
3. **The clock offset cannot be bounded tightly enough.** Then the honest output
   is that the attribution is not established. Written into SC-003 so that
   outcome is reportable rather than embarrassing.
4. **The run does not reach the rate.** Achieved rate is reported next to the
   intended one; a run at 60 ev/s answers a different question.
5. **The result gets read as a recommendation.** The record says what was
   measured and stops.

---

## Out of scope, named rather than implied

Moving the requirement; a fourth lever; general observability, sinks or
dashboards; the dashboard-obligation question (issue 1940); production topology;
the measurement test's exclusion from CI, which stays as it is.
