# Data model — 053 where the audit milliseconds go

Phase 1. Two existing timestamps, some measurement-only ones beside them, and a
clock offset that is measured rather than assumed.

---

## 1. The two spans

| | From | To | Who names it |
|---|---|---|---|
| **The requirement's span** | the broker hands the event over | the row is committed | NFR-001 |
| **The observed span** | the originating change happens | the row is stamped | three ADRs, and every figure quoted so far |

They are **not the same span**. The observed one is longer at the front by the
publisher's own work and the outbox hop, and shorter at the back by the insert.
Both are reported; the difference is attributed at each end.

---

## 2. The parts

| # | Part | Ends at | In the requirement? |
|---|---|---|---|
| 1 | publisher's transaction | the outbox row exists | no |
| 2 | outbox → broker | the broker holds it | no |
| 3 | broker → handler | the handler is entered | **yes** |
| 4 | handler → stamp | `ReceivedAt` is taken | **yes** |
| 5 | stamp → committed | the row is durable | **yes**, and unmeasured today |

**Rule**: every part carries a figure, the figures sum to the total, and whatever
they do not account for is reported as an **unattributed remainder** — never
spread across the parts it might belong to. An unexplained gap is a finding.

---

## 3. Measurement-only fields on the audit row

Nullable, written **only when the switch is on**, absent otherwise.

| Field | Taken at | Gives |
|---|---|---|
| enqueued at | the publisher, as the event is handed to the outbox | part 1's end |
| handler entered at | first line of the audit handler | part 3's end |
| committed at | after the write returns | part 5's end |

`OccurredAt` and `ReceivedAt` are **unchanged and still come from `IClock`**.
Re-sourcing them would alter production behaviour to suit a measurement and would
break comparison with every figure already recorded.

**Nullable is the whole design.** With the switch off the columns are absent and
the row is what it was. This is measurement apparatus on a production write path
— a real cost, which is why it is optional and why its own price is measured
(FR-009) rather than argued.

---

## 4. The clock offset

Not a bound taken on faith. A measurement.

| | |
|---|---|
| Reference | the shared Postgres server — one server, nine databases, so every service already shares a clock |
| Per process | ask the database its time, compare with the process's own |
| Relative skew | the difference between two processes' offsets |
| Residual | the round trip's own uncertainty, halved by the standard correction, **reported** |

**Threshold**: under **10 ms**, or the attribution is reported as **not
established** (SC-003). That is an outcome, not a failure — and it is written as
a success criterion so it cannot be quietly smoothed over.

---

## 5. A run

| Field | Why |
|---|---|
| intended rate | the requirement names 100 ev/s |
| **achieved rate** | a run that intended 100 and delivered 60 answers a different question |
| event count | percentiles need a population |
| apparatus on or off | FR-009: the difference between the two is the apparatus' cost |
| the parts, and the remainder | §2 |
| clock offset and residual | §4 |

**Three runs minimum.** The existing record shows two of six runs at ~100 ev/s
spiking by an order of magnitude, so spread is substance rather than ceremony.

---

## 6. Deliberately not modelled

- **Any change to the budget.** It stays at 50 ms.
- **A proposed improvement.** Whatever the breakdown suggests, acting on it is a
  separate decision with its own evidence.
- **A new telemetry destination.** One sink per environment is unchanged.
- **Anything about production.** There is none.
