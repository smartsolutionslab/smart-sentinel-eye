# Verification — 053 where the audit milliseconds go

Phase 5.

---

## 0. What shipped

**A breakdown, and the machinery to take it again.** Two nullable timestamps on
the audit row behind a switch that defaults to off, a clock-offset probe that
reports its own residual, and a measurement run that divides the span, reports
both spans, states the achieved rate beside the intended one, and refuses to
report a breakdown it cannot stand behind.

Nothing got faster. Nothing was supposed to.

The result is in ADR-0135. This note records how it was obtained and what went
wrong on the way, because six separate harness defects would each have produced
a confident, specific, wrong attribution.

---

## 1. Automated checks

| Check | Result |
|---|---|
| Full solution build (Release) | succeeds |
| `AttributionVerdictTests` (pure) | **8 pass** |
| `IngestAttributionTests` (pure) | **10 pass** |
| `AuditMeasurementSwitchTests` | **5 pass** |
| `ClockOffsetIntegrationTests` | **2 pass** |
| `Where_the_ingest_span_goes` | **3 paced runs pass** |
| `Ingest_p99_...` (apparatus cost) | runs off and on; **still fails its budget**, as it has throughout |

**Coverage gates apply and are cited.** AuditObservability's Domain and
Application layers are touched, so ADR-0065's ≥90% Domain / ≥80% Application
thresholds are live.

---

## 2. The gate: are the clocks close enough

`occurred_at` and `received_at` are stamped in different processes. Until that
disagreement is bounded, any attribution is a confident number resting on an
unmeasured assumption.

| | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Offset from the shared Postgres server | −1.96 ms | +1.27 ms | −7.20 ms |
| Residual | ± 0.95 ms | ± 1.03 ms | ± 1.01 ms |
| Worst case | 2.91 ms | 2.30 ms | **8.21 ms** |

Threshold 10 ms. **Established** — but run 3 came within 1.8 ms of firing it, and
the spread across runs (8.5 ms) is eight times any single residual. The readings
are more precise than they are accurate; the spread is what should be quoted.

At these magnitudes it is immaterial — 8 ms against a ~1500 ms span. **Against
the 85 ms this work set out to divide it would not be**, which is recorded in
ADR-0135 as a reason the two must not be conflated.

---

## 3. The breakdown

Three paced runs, 1000 events, achieved 98.7 / 99.0 / 98.8 ev/s against a target
of 100. See ADR-0135 for the full tables.

| | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Observed span | 1634.9 ms | 2642.5 ms | 1376.8 ms |
| Before handler | 1634.9 ms | 2642.4 ms | 1376.8 ms |
| In handler | 0.0 ms | 0.0 ms | 0.0 ms |
| **Unattributed** | **0.0 ms** | **0.0 ms** | **−0.0 ms** |
| Write | 8.5 ms | 10.1 ms | 9.0 ms |

**The spread is wide** — 1.4 to 2.6 seconds across three runs of the same shape
at the same rate. That is substance, not noise to be averaged away, and it is why
SC-005 asked for three.

---

## 4. What the checks cannot prove

| Claim | Proved by | Not proved by |
|---|---|---|
| The clocks agree closely enough | measured against a shared reference, residual reported | both processes being on one machine |
| The parts cover the span | 0.0 ms unattributed on every run at every load | the parts looking plausible |
| The apparatus is off by default | asserted on a **row**, not on the option | the default looking right |
| The run reached the rate | asserted, bracketed ±15% | intending 100 ev/s |
| Time is spent before the handler | the **idle** run, where there is no backlog to explain it | the paced runs alone, which are in backlog |
| **Which of four things spends it** | **nothing — no publisher stamp exists** | before-handler being one interval |
| **That the recorded 85 ms divides this way** | **nothing — fixture only** | the shape being stable on the fixture |
| **That any lever would help** | **nothing** | an obvious-looking dominant part |
| **Anything about production** | **nothing** | there is no production deployment |

The last four rows are the honest ones.

---

## 5. Six things that went wrong, each of which would have produced a wrong answer

Recorded because the spec's Phase 1 gate was written to stop exactly this class
of error, and the error arrived six times from directions the gate did not cover.

1. **The measurement switch never reached the test process.** Each shell is
   fresh; the export did not survive. Caught by `EveryRowStamped`, which failed
   rather than dividing a span over unstamped rows. **The apparatus caught the
   apparatus.**

2. **The fixture boots its own stack.** Four minutes were spent migrating and
   switching on an externally-booted AppHost the test never touched. The clue was
   a Postgres container on a port nothing had asked for.

3. **The driver was capped at 15.6 ev/s** by its own sequential round trip.
   Caught by reporting the achieved rate beside the intended one — the first
   thing that reporting did on its first outing.

4. **`string[]` bound as the params array itself.** `SqlQueryRaw(sql,
   identifiers)` passes eight identifiers as eight parameters via array
   covariance, so only `{0}` would have been read and one writer's events would
   have been reported as the population. Found while fixing an analyzer error,
   **not by any test** — it compiles, runs, and returns a plausible number.

5. **Debug SQL logging was throttling ingress to 79 ev/s.** Both services set
   `"Default": "Debug"` in Development. Turning it down gave 244 ev/s in an
   otherwise identical run. Found by running a control rather than writing a
   caveat.

6. **Adding writers was the wrong instrument.** "Sustained 100 ev/s" is a paced
   rate; flat out reached 244 ev/s and a 5.5 s span. That number would have been
   quoted against a 50 ms budget if nothing had checked which load produced it.
   The run is now paced and asserts the rate it landed at.

Also found and fixed en route, outside this feature's scope: **the migration
silently did not apply.** `audit_events` is a TimescaleDB hypertable with
columnstore enabled and refuses `ADD COLUMN` with a non-constant default
(`SqlState 0A000`). The MigrationRunner reported "Finished" regardless. The
column default moved into the repository's raw INSERT; **the runner's silence on
a failed migration is not fixed and is worth its own issue.**

---

## 6. Phases

- Phase 5: this note.
- Phase 6: pending.
