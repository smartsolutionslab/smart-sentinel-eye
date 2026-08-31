# Data model — 054 divide the span the decision is waiting on

Phase 1. **No database schema changes.** The measurement columns exist and the
migration is applied (spec 053). Everything here is test-side.

---

## 1. `IngestRunShape` — the constants both runs read

The mechanism behind FR-011. Not a settings object: a single fixed description of
the run, referenced by the fixture run and the run-mode run alike, so the two
**cannot** drift apart without one edit changing both.

| Field | Value | Why it is fixed |
|---|---|---|
| Generator | repeated system-variable value sets | Publishes `SystemVariableValueChangedV1`, whose `OccurredAt` is stamped as the aggregate mutates. A different generator measures a different pipeline. |
| Warm-up events | 100 | Excluded from the measured population by using a separate variable, not by filtering after the fact. |
| Measured events | 1000 | The population every percentile is taken over. |
| Writers | 50 | One variable each — the version travels in an `If-Match`, so two writers on one variable collide on optimistic concurrency rather than generate load. |
| Target rate | 100 ev/s | The rate NFR-001 names. |
| Rate tolerance | ± 15% | Below it the pipeline is idle; above it the run measures overload. |

**Invariant**: measured events must divide by writers exactly. 1000 / 50 = 20.

---

## 2. `IngestRunConditions` — what a run reports about itself

A figure without these is not comparable with anything. Emitted by both runs.

| Field | Source | Why it is here |
|---|---|---|
| Environment | the run itself | Names which of the two stacks produced the figure. |
| Endpoint | the address actually connected to | **The only guard against attributing a figure to the wrong stack.** Reported, not assumed. |
| Intended rate | shape | So the achieved figure has something to be read against. |
| Achieved rate | measured | A run that meant 100 ev/s and managed 60 answered a different question. |
| Logging level | environment | At Debug this stack sustains 60–83 ev/s and the run measures the logging. |
| Measurement switch | read off a written row | Absent stamps mean there is nothing to divide. |
| Rows measured / missing stamps | the query | A run that measured 900 of 1000 must not report the 900 as the population. |

**Rule**: the conditions block is written **before** the assertions that might
fail, so a refused run still says what it was refused for.

---

## 3. `IngestSpanMeasurement` — the run body, extracted

Not new behaviour. The existing measured run lifted out of the fixture test so
both runs execute one implementation.

**Takes**: an authenticated `HttpClient` for `system-variables`, a factory for
`AuditObservabilityDbContext`, the `audit-db` connection string (for the clock
probe), and `IngestRunShape`.

**Returns**: the two `IngestAttribution` bands (typical and tail), the
`ClockOffset`, the `AttributionVerdict`, and `IngestRunConditions`.

**Deliberately does not**: assert. The two callers assert, because the fixture run
and the run-mode run have the same *conditions* but are reported separately.

---

## 4. Reused unchanged

| Type | Role | Change |
|---|---|---|
| `IngestAttribution` | divides the span; `PerRowResidualMs`, `PartsCoverEveryRow`, both spans, tail band | **none** |
| `ClockOffsetProbe`, `ClockOffset`, `RelativeSkew` | bounds the host↔container disagreement | **none** |
| `AttributionVerdict` | 10 ms threshold; `Established` / `NotEstablished` | **none** |
| Attribution SQL | percentiles filtered to stamped rows, tail band selected by rows, per-row residual | **moves**, unchanged |

A second copy of the division would be a second thing to get wrong; that is why
this section is mostly "none".

---

## 5. What the run reads from the store

Unchanged from spec 053, and it matters more here: run mode's audit store is
**persistent and holds months of history**. The query isolates this run's events by
`resource_identifier` — the variable identifiers it created — so the population is
the run's own regardless of what else is in the table.

---

## 6. Not modelled here

- **No schema change.** Columns exist, migration applied.
- **No new configuration in any service.** The switch and logging level are
  existing environment variables the operator sets before launching the AppHost.
- **No production concepts.** There is no production deployment (ADR-0130).
