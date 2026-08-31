# Research — 053 where the audit milliseconds go

Phase 0. Everything read in source or in the existing record. This feature
**does not re-measure what is already recorded**; it measures what is not.

---

## R0. Is there a locked decision contradicting this?

**Checked, and there is not.** Three ADRs govern this area and all three point
here rather than away.

| Decision | Says | Conflict? |
|---|---|---|
| ADR-0124 parallel listeners | *"NFR-001 is still not met, and this ADR does not claim it is"* | No — it names moving the requirement as an open alternative |
| ADR-0126 settle at the broker | left the 50 ms unmet | No |
| ADR-0127 batching | **Rejected on measurement** | No — and it is the precedent for this feature's shape: build, measure, decline |
| ADR-0050 logging / OpenTelemetry | telemetry is MEL-native, OTLP, `[LoggerMessage]` source-gen | No — constrains *how*, not *whether* |
| ADR-0118 one sink per environment | the development dashboard is the sink; no production sink | No, but see R3 |

**No amendment gate applies.** ADR-0127's shape is the precedent worth naming:
it built a lever, measured it, and rejected it in the record. That is the
posture here, minus the lever.

---

## R1. The clock problem, and why it is solvable rather than merely boundable

The specification's hardest requirement is FR-005: bound the disagreement
between the two stamping clocks, **or remove the dependency on them agreeing**.

**The dependency can be removed, and cheaply.**

`AppHost.cs` declares **one Postgres server with nine databases** — audit,
system-variables and the rest are separate databases on the same server. So
every service in the pipeline already shares a single reference clock, whether or
not anybody has used it that way.

**Decision: measure each process's offset against the shared database clock.**

Each participating service asks the database for its time and compares it with
its own. The difference is that process's offset from the common reference; the
difference between two processes' offsets is their relative skew, measured rather
than assumed.

**Why this rather than changing what the pipeline stamps.** `OccurredAt` comes
from the publisher's domain clock and `ReceivedAt` from the consumer's, both
through `IClock`. Re-sourcing either from the database would change production
behaviour to suit a measurement — the tail wagging the dog, and it would also
destroy the ability to compare against every figure already recorded.

**What it costs**: a round trip, so the measured offset carries the round trip's
own uncertainty. Halving the round trip is the standard correction and its
residual is small relative to the 10 ms bound SC-003 asks for. **The residual is
reported, not assumed away.**

**Alternatives considered**: bounding by inspecting host clock synchronisation —
rejected, it describes the host rather than the processes, and containers may
differ. Assuming a single machine implies a single clock — rejected; that is
exactly the assumption the specification exists to stop being load-bearing.

---

## R2. What the parts are, and which the requirement actually names

The span in question runs from an event happening to its record existing.
Named parts, and their standing against the requirement:

| # | Part | In the requirement's span? |
|---|---|---|
| 1 | The publisher's own transaction, up to the outbox row | **No** — front overhang |
| 2 | Outbox → broker | **No** — front overhang |
| 3 | Broker → the audit handler being entered | **Yes** |
| 4 | Handler entry → the moment the row is stamped | **Yes** |
| 5 | Stamp → the row committed | **Yes**, and **not measured today** |

The requirement names **3 + 4 + 5**. What has been measured for three ADRs is
**1 + 2 + 3 + 4** — longer at the front, shorter at the back.

**Prior work already bounds part 2 to near zero**: through a 158 ev/s burst the
publisher's outgoing-envelope table held **0 rows at every sample** while the
audit queue backed up to 468 and then 643. That is strong evidence the front
overhang is small, and it is evidence rather than proof — this feature measures
it rather than inheriting it.

---

## R3. How the attribution is gathered

Three candidates, and the second wins on a property the others lack.

| | Reading spans from the development dashboard | Timestamps carried to the audit store | A separate measurement sink |
|---|---|---|---|
| Idiomatic | **yes** | no | partly |
| Survives the run | no | **yes** | yes |
| Queryable beside the existing percentiles | no | **yes** | no |
| Correlating 1 000 events | **poor** | trivial | moderate |
| Touches the production write path | no | **yes** | no |

**Decision: carry the measurement timestamps to the audit store, behind a switch
that is off by default.**

**Why not the dashboard.** Its trace list is effectively unsearchable — a known
trap in this project — so an attribution over a thousand events would mean
hunting history rather than provoking a specific event. The existing percentile
query already reads the audit store in SQL; putting the parts beside the total
means one query answers the whole question, and the numbers cannot drift apart
because they come from one row.

**Why a switch.** These columns exist for a measurement, not for the product. Off
by default, they cost a nullable column and nothing else; on, they cost the
stamps themselves — which is exactly what FR-009 requires be reported.

**The honest cost, stated rather than buried**: this puts measurement apparatus
on the production write path. That is a real objection, it is the reason the
switch exists, and the plan does not pretend otherwise.

---

## R4. Reaching the load, and the observer problem

**The rate the requirement names is 100 events per second sustained.** Prior work
reached 99–113 ev/s with the same generator, so the rate is achievable.

**The achieved rate is reported alongside the intended one** (FR-007). A run that
intended 100 and delivered 60 answers a different question, and three of the
recorded figures are rate bands rather than points precisely because the
generator does not hit a number exactly.

**The observer problem is measured, not argued** (FR-009): the same run shape is
taken with the switch off and on, and the difference in the total is the
apparatus' own cost. If that cost is large relative to the parts being
attributed, the attribution says so.

**Three runs minimum** (FR-008, SC-005). One run is an anecdote; the existing
record already shows two of six runs at ~100 ev/s spiking by an order of
magnitude, so spread is not a formality here.

---

## R5. What this feature must not do

- **Not move the budget.** A passing number obtained by moving the line reports
  the requirement as met when it is not.
- **Not propose a fourth lever.** Even if the breakdown makes one obvious. That
  is a separate decision with its own evidence, and the specification forbids it
  precisely because the pull is strong.
- **Not re-litigate the three existing ADRs.** They are measured and recorded.
- **Not become general observability.** No new sink, no dashboard, no tracing of
  other contexts.

---

## R6. Where the code goes

| Change | Where |
|---|---|
| Measurement timestamps on the audit row | `AuditObservability` Domain + Infrastructure, behind a switch |
| Clock-offset probe | the measurement test's own harness, not production code |
| Attribution query and its run | `tests/Integration.Tests/AuditObservability/` |
| The record | ADR + `verification.md` |

House rules apply: `Ensure.That` guards, `Result<T, Error>` for expected
failures, collection expressions with explicit types, no leading underscore on
fields.

**Coverage gates**: the Domain and Application layers of AuditObservability are
touched, so ADR-0065's thresholds are live — 90% Domain, 80% Application.
