# Verification — 054 divide the span the decision is waiting on

Phase 5.

---

## 0. What shipped

**The 85 ms, divided.** A driver that targets a stack it did not start, an
extraction so the fixture run and the run-mode run are the same code, and three
paced measurements against run mode.

Nothing got faster. Nothing was supposed to.

The result is in ADR-0136. **The headline: run 3's tail band is 87.4 ms — the
figure the open decision is about — and 87.4 ms of it precedes the audit
handler.**

---

## 1. Automated checks

| Check | Result |
|---|---|
| Full solution build (Release) | succeeds |
| `RunModeDriverTests` | **17 pass** |
| `IngestAttributionTests` | **16 pass** |
| `AttributionVerdictTests` | **8 pass** |
| `Where_the_ingest_span_goes` (fixture) | passes — the Phase 1 gate |
| `Where_the_ingest_span_goes_in_run_mode` | **3 runs pass** |

**No coverage gate is live.** No Domain or Application code is touched, so
ADR-0065's thresholds do not apply. Stated because two recent specs got this
wrong in opposite directions.

---

## 2. The gate: did the extraction change anything

The run body and attribution SQL were lifted out of merged, verified code whose
figures are recorded. An extraction that changed behaviour would make every
number after it incomparable with the figures it exists to be compared against.

**Gate result: behaviour preserved.** Breakdown shape unchanged, per-row residual
0.000 ms on both bands, every row stamped, achieved 98.5 ev/s.

**The observed span was deliberately not part of the gate, and two runs showed
why**: 7361.9 ms then 267.4 ms, same code, same configuration, same 98.5 ev/s. A
code move cannot swing a figure 27× in *both* directions, so that variance is the
machine — which is what the gate needed to establish, and what it did.

---

## 3. The measurement

Three paced runs against run mode. Full tables in ADR-0136.

| | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Achieved | 97.2 ev/s | 99.6 ev/s | 99.8 ev/s |
| Typical span | 262.0 ms | 19.0 ms | 14.3 ms |
| Tail band | 1499.5 ms | 589.0 ms | **87.4 ms** |
| Before handler | the whole of both | the whole of both | the whole of both |
| In handler | 0.0 ms | 0.0 ms | 0.0 ms |
| Per-row residual | 0.000 ms | 0.000 ms | 0.000 ms |
| Clock verdict | Established | Established | Established |

**Run 3 is the one that answers the question.** Its tail band is 87.4 ms against a
recorded p99 of 85 ms, and the division puts all of it before the audit handler.

---

## 4. What the checks cannot prove

| Claim | Proved by | Not proved by |
|---|---|---|
| The extraction preserved behaviour | the gate, against **recorded** figures | the suite going green |
| The driver cannot boot a stack | asserted absent attribute **and** absent constructor dependency; mutation verified to fail | the class looking right |
| Missing configuration refuses | asserted; mutation verified to fail | a default that happens to be unset |
| Both runs read one shape | asserted that the run body takes **no numeric parameter** | arithmetic inside the shape, which the mutation survives |
| A refused run still explains itself | conditions emitted before every assertion | the block existing |
| Time is spent before the handler | four loads on the fixture **and** three runs in run mode, idle and saturated | any single run |
| **That the stack measured was run mode** | **nothing — a human read the reported endpoint** | the endpoint being configured |
| **The write leg** | **nothing — host/container split, and it ends at insert not commit** | the clocks being established here |
| **The requirement span's floor** | **nothing — built from the write leg** | the ceiling being sound |
| **Which of four things spends the time** | **nothing — no publisher stamp** | before-handler being one interval |
| **That this reproduces the original run** | **nothing — that driver was never committed** | the environment matching |
| **A range for either environment** | **nothing — three samples of a bistable distribution** | three runs clustering |
| **That any lever would help** | **nothing** | a dominant-looking part |

The last seven rows are the honest ones.

---

## 5. What this found about the record it was extending

**ADR-0135's fixture spread was understated, and this feature is why we know.**

Seven paced fixture runs at ~99 ev/s: **267.4, 1376.8, 1414.9, 1634.9, 2642.5,
5516.0, 7361.9 ms.** ADR-0135 records "1376.8–2642.5 ms" from three runs as though
that were the range. Those three clustered; the true spread is 27×.

That is the third time in this line of work that **three runs were taken to
characterise a distribution they cannot characterise** — and the same caution now
applies to the three run-mode figures here, which is why ADR-0136 says so about
its own numbers rather than only about its predecessor's.

The likely cause is coherent rather than mysterious: **100 ev/s sits at the
consumer's drain ceiling**, so a run either keeps up or falls behind and
accumulates backlog. Bistable at the knee. Arguably a more useful finding about
the pipeline than the breakdown itself, and recorded as such.

---

## 6. Demonstrated against the running stack, not the file

The endpoints were discovered rather than assumed, and each step was checked
against the running system:

| Step | How |
|---|---|
| Services are host processes | 13 `SmartSentinelEye.*` processes, two gateway replicas |
| system-variables' port | its own listeners — 65441 http, 65440 https |
| Keycloak's issuer | asked the realm: `https://localhost:10756/realms/smart-sentinel-eye` |
| The token is accepted | minted it and called `/system-variables` — **HTTP 200** |
| The columns exist in run mode | queried `information_schema` |
| The run isolates its rows | the store held **548 001** rows before the first run |

**The Keycloak trap did not bite, and it is worth saying why.** The recorded
warning is to mint from Aspire's proxied endpoint rather than the container's
mapped port. Here the persistent container has a fixed port and Keycloak's issuer
*is* that address — so 10756 was correct. The trap is real when the issuer differs;
the check that settles it is asking the realm for its issuer, which is what was
done rather than assuming either way.

---

## 7. Phases

- Phases 1–4: the extraction, the driver, the measurement, the record.
- Phase 5: this note.
- Phase 6: pending.
