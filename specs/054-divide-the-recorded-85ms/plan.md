# Implementation Plan: Divide the span the decision is actually waiting on

**Branch**: `054-divide-the-recorded-85ms` | **Date**: 2026-08-31 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/054-divide-the-recorded-85ms/spec.md`

## Summary

Spec 053 divides the audit ingest span, and measures the Aspire test fixture. The
figure the open decision is about was recorded in **run mode**. This feature takes
the same division, of the same span, at the same paced rate, against a running
run-mode stack — so the two can sit in one table.

**The apparatus is not the work.** The stamps, the switch, the division, the clock
probe and the conditions guards are merged and verified. The work is a driver that
targets a stack it did not start, and an extraction so both runs execute the same
code rather than two codebases held in agreement by prose.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: xUnit, Shouldly, Npgsql, EF Core. No new package.

**Storage**: PostgreSQL — the run-mode `audit-db`, read over a connection the
operator supplies. Persistent and already populated, so the run isolates its own
rows by `resource_identifier`.

**Testing**: xUnit. **Deliberately without `AspireFixture`** — the fixture boots
its own stack, which is precisely what makes it not run mode.

**Target Platform**: a developer machine running the AppHost in run mode.

**Project Type**: measurement. The deliverable is a document; nothing gets faster.

**Performance Goals**: none. The run must *achieve* 100 ev/s ± 15% to be valid,
which is a condition of the measurement rather than a goal for the system.

**Constraints**: run shape identical to spec 053's in everything but environment;
excluded from CI; no change to the audit pipeline, to NFR-001's budget, or to what
Development logs at.

**Scale/Scope**: 100 warm-up + 1000 measured events, 50 concurrent writers, ≥3 runs.

## Constitution Check

*GATE: must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| §IV latency budget | **Not on the event-to-overlay path.** This is audit ingest (spec 009 NFR-001). No §IV leg is touched and no §IV figure changes. |
| §VII observability | No sink, exporter or dashboard added. Figures come from SQL over the audit table, as spec 053's do (ADR-0118 respected). |
| DDD / value objects | Test-side code. No domain types cross a boundary; no primitives enter one. |
| No cross-context references | The run reads `AuditObservabilityDbContext` from the test project, which already references it. No context-to-context reference introduced. |
| Coverage gates (ADR-0065) | No Domain or Application code is touched, so no gate is live. **Stated because the last two specs got this wrong in both directions.** |
| Rebase-only, Conventional Commits | Unchanged. |

**Locked decisions checked — ADR-0067, 0103, 0118, 0130, 0135 — and there is no
conflict.** See research.md §0. No amendment gate is triggered.

## Project Structure

### Documentation (this feature)

```text
specs/054-divide-the-recorded-85ms/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── contracts/
│   └── the-comparison.md
├── quickstart.md        # Phase 1 — the runbook, because the address trap is real
└── checklists/
    └── requirements.md
```

### Source (this feature)

```text
tests/Integration.Tests/AuditObservability/
├── IngestAttribution.cs              # unchanged, reused
├── ClockOffsetProbe.cs               # unchanged, reused
├── AttributionVerdict.cs             # unchanged, reused
├── IngestRunShape.cs                 # NEW — the constants both runs read
├── IngestRunConditions.cs            # NEW — what a run reports about itself
├── IngestSpanMeasurement.cs          # NEW — the run body + attribution SQL, extracted
├── NFR001_AuditIngestLatencyTests.cs # MODIFIED — calls the extraction
└── RunModeIngestAttributionTests.cs  # NEW — no collection attribute, no fixture
```

## Design

### The seam

The fixture supplies three things the run body needs: an authenticated client for
`system-variables`, a database context for `audit-db`, and a connection string for
the clock probe. In run mode all three come from operator-supplied configuration.

So the run body takes them as inputs rather than reaching for a fixture. That is
the entire structural change, and it is what lets both runs be the same code.

### What "identical" covers, enforced rather than promised

`IngestRunShape` holds the generator, warm-up count (100), measured count (1000),
writer count (50), target rate (100 ev/s) and the ± 15% tolerance. **Both runs read
it.** Neither can change shape without changing the other, so drift is not
expressible — which is the mechanism FR-011 asks for. Differences that remain —
the environment and the address — are *named* in the conditions block.

### Refusal, not fallback

Absent or unreachable configuration **fails the run with a message naming what it
could not reach**. It never boots a stack. A silent fallback would reproduce the
exact defect this feature removes, while reporting success — the most dangerous
outcome available here.

### The write leg

Run mode has the same host/container split, so the write leg and the requirement
span's floor **remain not established**. This is stated in the record rather than
rediscovered, and it is acceptable because this feature's question lives at the
*front* of the span, which does not depend on it.

## "Done", per user story, before any code

| Story | Done when | What this does **not** prove |
|---|---|---|
| **US1** the attribution | A run against a run-mode stack reports the breakdown, at an achieved rate within 15% of 100 ev/s, with a per-row residual of 0 and every row stamped | That the numbers resemble the fixture's. If they differ, that is the finding. |
| **US2** the run shape | Both runs read one shape constant; a change to either forces the other; each run emits a conditions block naming environment, address, rate, logging level and switch state | That a *future* operator sets the same environment — the block records what was used, it cannot enforce it |
| **US3** the record | Both breakdowns sit in one table, spread across ≥3 runs, unestablished figures named in the same table, no recommendation anywhere | That the reader draws the right conclusion. The record's job is to stop them drawing a wrong one confidently. |

**The check that cannot exist**: nothing automated can prove the driver reached
*run mode* rather than some other stack answering on that address. What
establishes it is a human comparing the reported address against the stack they
started, and the persistent store growing by exactly the measured count.

## Three things most likely to go wrong

1. **The extraction changes the fixture's figures.** It touches merged, verified
   code. Mitigation: the fixture run's numbers are recorded in ADR-0135, so a
   behaviour-preserving extraction is checkable rather than assertable — re-run it
   and compare.

2. **The run silently measures the wrong stack.** An endpoint is an endpoint. A
   figure attributed to run mode but taken against a leftover fixture stack would
   be worse than no figure. Mitigation: the conditions block reports the address
   actually used, and the runbook says to check it.

3. **A single pair of runs becomes an effect size.** One side of this measurement
   is far noisier than the other, and this repository has already published an
   overstated figure for exactly that reason. Mitigation: ≥3 runs, spread
   reported, and the asymmetry explained in the record rather than averaged away.

## Phase ordering

1. **Extract** the run body, shape and conditions; prove the fixture run is
   unchanged against its recorded figures. *Nothing new is measured yet.*
2. **The run-mode driver** — configuration, refusal, auth, conditions block.
3. **Measure** — three runs minimum, at Warning, switch on.
4. **The record** — ADR and verification note, stating what was measured and
   stopping.

Step 1 gates the rest: an extraction that changed behaviour would make every
number after it incomparable with the fixture figures it exists to be compared
against.
