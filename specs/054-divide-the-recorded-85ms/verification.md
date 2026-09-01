# Verification — 054 divide the span the decision is waiting on

Phases 5 and 6.

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

## 6a. A cost this feature adds, found while reviewing it

**The measurement leaves 51 system variables behind per run** — one warm-up plus
one per writer — and run mode keeps them. On the fixture this cost nothing,
because the stack is torn down with its database.

Counted on the dev stack after these runs: **1468 of 1559 system variables are
measurement residue**, 94% of the table.

**It is filed rather than fixed**, because neither available remedy is obviously
right. There is no delete endpoint and that looks deliberate — `archive` marks
rather than removes — so cleanup would either publish 51 more events into the
pipeline the run just measured, or reach around the domain from a test. Which of
those the repository wants is a decision, not a tidy-up.

Worth knowing because a dev stack with 1468 variables makes the management UI's
variable list unusable, and the count only grows.

## 7. Phases

- Phases 1–4: the extraction, the driver, the measurement, the record.
- Phase 5: this note.
- Phase 6: the review round — thirteen findings, all confirmed, recorded in §8.

---

## 8. What review found

**Thirteen findings, all confirmed, none a false positive.** Recorded because two
of them contradict comments this feature wrote about itself, and that pattern is
worth more than the individual fixes.

1. **The conditions block did not survive the failure it exists for.** Its own
   comment says the endpoint line "must survive a failure rather than being lost
   with it" — and it printed only after the drive returned. A wrong address or an
   expired token throws inside the drive, and the output would carry no
   environment, no endpoint, no rate. **The justification was written and then the
   opposite was built.**

2. **The row-count assertion had drifted after the breakdown print.** A run where
   nine hundred rows landed would emit a complete, well-formed division — the
   output shape a good run produces — and only then fail.

   Together these two settle the ordering: conditions, drive, assert the
   population, breakdown.

3. **Two senses of "established" were run together.** The verdict is about the
   *clocks*, which in run mode agree closely. The write leg is still not
   established, because it ends at insert rather than commit — something no clock
   agreement can fix. Every passing run printed two adjacent contradictory lines,
   and ADR-0136's table repeated it.

4. **The refusal guard killed one third of its named mutation**, clearing only the
   system-variables setting; a default for either of the other two survived
   untouched.

5. **That same guard mutated process-wide state** while xUnit runs collectionless
   classes in parallel — able to refuse a correctly configured measurement with a
   message naming a cause that was not true. The decision is now a pure function
   the test calls directly.

6. **The runbook's Keycloak instruction was the opposite of what worked.** It
   insisted on the proxied address; the measurement used the container's fixed
   port, because that is what the realm names as its `issuer`. An operator
   following it literally would produce the 401 it warns about. **It now says to
   ask the realm rather than to follow either rule** — a fact settles this, a
   heuristic cannot.

7. **`IngestDeadline` was duplicated**, with the surviving copy feeding only a
   failure message while the other governed the wait. Precisely the drift
   `IngestRunShape`'s doc comment says this spec exists to make inexpressible.

8. **The certificate bypass covered Keycloak but not the service the load goes
   to**, while the runbook told operators to paste a dashboard address and the
   service offers both http and https.

9. **A 401 mid-run was diagnosed as an If-Match failure**, reporting version
   numbers for a token-expiry problem — sending the reader after the one thing
   that cannot be wrong, since the version is tracked locally.

10. **A dead `EveryRowStamped`** on the conditions, duplicating the one on the
    attribution that both callers actually assert.

11. **The cancellation token reached almost nothing** — not the HTTP calls, not the
    three-minute wait loop, not the queries.

12. **Two ADR passages read as recommendations.** The lever paragraph's bolding and
    juxtaposition argued that the three shipped levers were aimed at the wrong
    side; and a rejected alternative closed by calling itself "the obvious way".
    Both neutralised: the measurement is stated, the argument is not made.

13. **A write figure contradicted its own table** — 12.4 ms in prose against 5.3 ms
    in a row whose band was unlabelled.

**The pattern.** Findings 1, 3, 6 and 7 are all the same defect: **a claim written
about the work, and the work doing something else.** This note's §5 says the same
thing happened to ADR-0135's spread. The discipline that catches it is not care
while writing — it is a second reader, or a run.
