# Verification — 053 where the audit milliseconds go

Phases 5 and 6.

---

## 0. What shipped

**A breakdown, and the machinery to take it again.** Two nullable timestamps on
the audit row behind a switch that defaults to off, a clock-offset probe that
reports its own residual, and a measurement run that divides the span, reports
both spans, states the achieved rate beside the intended one, and refuses to
report a breakdown it cannot stand behind.

Nothing got faster. Nothing was supposed to.

The result is in ADR-0135. This note records how it was obtained and what went
wrong on the way. The defects found en route — first in the harness (§5), then in
the harness's own checks (§6) — would each have produced
a confident, specific, wrong attribution.

---

## 1. Automated checks

| Check | Result |
|---|---|
| Full solution build (Release) | succeeds |
| `AttributionVerdictTests` (pure) | **8 pass** |
| `IngestAttributionTests` (pure) | **16 pass** — six added in review: the medians-do-not-add distinction, and which leg is labelled as crossing clocks |
| AuditObservability Application (incl. `AuditMeasurementSwitchTests`) | **43 pass** |
| `ClockOffsetIntegrationTests` | **2 pass** |
| `Where_the_ingest_span_goes` | **3 paced runs passed** pre-review; the first run after the verdict was wired in **failed the clock gate** (§3) — the gate working, not a regression |
| `Ingest_p99_...` (apparatus cost) | runs off and on; **still fails its budget**, as it has throughout |

**Coverage gates apply and are cited.** AuditObservability's Domain and
Application layers are touched, so ADR-0065's ≥90% Domain / ≥80% Application
thresholds are live.

---

## 2. The gate: are the clocks close enough

The spec's worry was that `occurred_at` and `received_at` are stamped in
different processes. **That framing turned out to be wrong**, and the probe built
to answer it was measuring something else the whole time.

Those two processes run on one machine and read one OS clock, so their
disagreement is small — it would not be across machines, and nothing here would
notice. The pair that genuinely crosses two clocks is `received_at` (host) and
`written_at` (Postgres container), which is exactly what the probe measures. So
the table below bounds **the write leg**, not the front of the span.

| | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Offset from the shared Postgres server | −1.96 ms | +1.27 ms | −7.20 ms |
| Residual | ± 0.95 ms | ± 1.03 ms | ± 1.01 ms |
| Worst case | 2.91 ms | 2.30 ms | **8.21 ms** |

Threshold 10 ms. **Established** — but run 3 came within 1.8 ms of firing it, and
the spread across runs (8.5 ms) is eight times any single residual. The readings
are more precise than they are accurate; the spread is what should be quoted.

**The answer splits by leg.** Against the observed span it is immaterial — 8 ms
on ~1500 ms. Against the write leg it is fatal: 8 ms of uncertainty on an 8.5–10.1
ms figure establishes nothing, and a later run proved it by returning a negative
write leg outright.

It would also not be immaterial against the 85 ms this work set out to divide,
which is recorded in ADR-0135 as a reason the two must not be conflated.

**And the instrument's own noise sits at the threshold.** One process measured
against itself — true skew zero by construction — returns readings spanning
~10 ms with residuals of 1–2 ms. So the verdict separates a badly skewed stack
from a healthy one and not much finer.

---

## 3. The breakdown

Three paced runs taken **before** review, 1000 events each, achieved 98.7 / 99.0 /
98.8 ev/s against a target
of 100. See ADR-0135 for the full tables.

| | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Observed span | 1634.9 ms | 2642.5 ms | 1376.8 ms |
| Before handler | 1634.9 ms | 2642.4 ms | 1376.8 ms |
| In handler | 0.0 ms | 0.0 ms | 0.0 ms |
| Write — **not established** | 8.5 ms | 10.1 ms | 9.0 ms |

**The write leg is reported and not established.** It subtracts a host-process
stamp from a container one, and the measured disagreement between those clocks
(±8 ms) is the same size as the leg. It also ends at insert rather than commit,
where NFR-001 says "committed". Both are recorded in ADR-0135; neither was
noticed until Phase 6.

**The gate then fired on a real run, which is better evidence than the argument
was.** With the verdict wired in, the next run measured the host–container offset
at **−21.85 ms ± 1.05 ms** (worst case 22.90 ms) and reported the tail band's
write leg as **−9.2 ms**. A negative duration is not something a pipeline can
produce, and it is the exact failure the review predicted. The run was refused.

The same offset ranged from **−21.85 to +2.31 ms** across this session, so it is
drift rather than a fixed bias that could be subtracted out. The three runs in §3
sat under the threshold and would have passed — luck, not control.

**The per-row residual came back 0.000 ms** on both bands, 1000 rows, none
missing stamps. That is the check the completeness claim now rests on.

**The spread is wide** — 1.4 to 2.6 seconds across three runs of the same shape
at the same rate. That is substance, not noise to be averaged away, and it is why
SC-005 asked for three.

---

## 4. What the checks cannot prove

| Claim | Proved by | Not proved by |
|---|---|---|
| The host and container clocks agree closely enough | measured against a shared reference, residual reported, **verdict applied in the run** | printing an offset without judging it, which is what this did until Phase 6 |
| The parts cover the span | the **per-row** residual, computed row by row | medians reconciling, which they need not do even when sound |
| The apparatus is off by default | asserted on a **row**, not on the option | the default looking right |
| The run reached the rate | asserted, bracketed ±15% | intending 100 ev/s |
| Time is spent before the handler | the **idle** run, where there is no backlog to explain it | the paced runs alone, which are in backlog |
| **The write leg** | **nothing — it crosses two clocks and ends at insert, not commit** | it being a small, plausible-looking number |
| **The requirement span's floor** | **nothing — it is built from the write leg** | the ceiling being sound, which it is |
| **Which of four things spends the rest** | **nothing — no publisher stamp exists** | before-handler being one interval |
| **That the recorded 85 ms divides this way** | **nothing — fixture only** | the shape being stable on the fixture |
| **That any lever would help** | **nothing** | an obvious-looking dominant part |
| **Anything about production** | **nothing** | there is no production deployment |

The last six rows are the honest ones — and two of them only appeared after
review, having been published as measurements first.

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

Also found and fixed en route, outside this feature's scope: **the migration did
not apply.** `audit_events` is a TimescaleDB hypertable with columnstore enabled
and refuses `ADD COLUMN` with a non-constant default (`SqlState 0A000`). The
column default moved into the repository's raw INSERT.

> **Corrected 2026-08-31.** This paragraph originally said the MigrationRunner
> "reported Finished regardless", and an issue was filed against the runner on
> that basis. **It was wrong, and the issue is closed as not reproducible.**
> Tested by giving AuditObservability a migration whose `Up()` is `SELECT 1 / 0`
> and running the real runner: it exits **non-zero**, logs the
> `PostgresException`, never logs "All migrations applied", and leaves nothing in
> `__EFMigrationsHistory`. There is no `try`/`catch` anywhere on that path.
>
> What was actually misread was a **stale Aspire resource state**: the AppHost
> was already running, its `migrationrunner` had reached *Finished* on the
> earlier boot before the migration existed, and it never saw it. The `0A000` was
> real — it surfaced when the migration was applied by hand, which is also where
> it was loud.

---

---

## 6. What review found, after all of the above

**Seven findings, all confirmed, none a false positive.** Recorded at this length
because §5 was written as a list of things caught by good instruments, and the
honest sequel is that the instruments themselves had defects the same discipline
did not catch.

1. **The write leg crosses a clock boundary the code said it did not.**
   `received_at` is a host-process stamp, `written_at` a container one, and the
   code asserted in a comment that only "before handler" crossed clocks. Working
   it through, the truth is sharper: `occurred_at` and `received_at` are *both*
   host processes reading one OS clock, so **the leg the spec worried about is
   the safe one and the leg nobody worried about is not**. The probe built to
   guard the front of the span was, all along, measuring the back.

2. **The run measured a clock offset, printed it, and never judged it.** The
   `AttributionVerdict` type — built in Phase 1 as *the gate* — was applied
   nowhere in the attribution run. A stack drifted 40 ms passes every assertion
   and has its breakdown recorded as fact. The gate existed, was tested, and was
   not wired to the thing it gated.

3. **The migration dropped and recreated a primary key and unique index it does
   not change**, and its `Down()` could never have run. Stripped to two
   `AddColumn`s.

   **Corrected 2026-08-31.** This item first claimed the churn would be *refused*
   on compressed chunks. Tested: the chunk was compressed and both the
   `DROP INDEX` and the `DROP CONSTRAINT` **succeeded**. The churn is untidy, not
   dangerous, and removing it was tidiness rather than a fix.

   **The `Down()` half does hold**, and was tested rather than reasoned:
   `CREATE UNIQUE INDEX ... (event_identifier)` on the hypertable is refused with
   *cannot create a unique index without the column "occurred_at" (used in
   partitioning)*. So the generated rollback was genuinely impossible, which is
   the part worth having caught.

4. **The self-versus-self clock test was flaky against data recorded ten lines
   above it.** True skew zero, observed spread ~10 ms, residuals 1–2 ms. Its
   replacement asserts a bound the instrument supports and records the real
   finding: **the noise floor is the same order as the threshold the verdict
   decides on.**

5. **`written_at` is stamped at insert, not commit**, where NFR-001 says
   "committed".

6. **Unstamped rows were `COALESCE`d to zero into the medians** while keeping
   their real totals — which would drag the parts toward zero and print a
   remainder that reads as a pipeline property.

7. **A duplicated `<summary>` tag**, inert until doc generation is enabled.

**Found before the review returned, and worth its own line:** `UnattributedMs` is
median arithmetic, and **medians do not add**. "0.0 ms unattributed on every run"
— cited in an earlier draft of ADR-0135 as proof the breakdown was complete —
held only because in-handler is degenerate at ~0. A pipeline that actually spent
time in its handler would have failed that assertion for a reason having nothing
to do with the stamps. There is now a per-row residual, computed row by row, and
tests for both directions: figures that reconcile over broken stamps, and sound
stamps whose figures do not reconcile.

**The pattern across all eight.** Every one is an instrument that reported
confidently about something it was not measuring. That is the same failure the
spec's Phase 1 gate was written to prevent, and it recurred inside the apparatus
built to enforce it.

---

## 7. Phases

- Phase 5: §0–5.
- Phase 6: §6. Findings addressed; the two figures that could not be rescued are
  published as **not established** rather than dropped.
