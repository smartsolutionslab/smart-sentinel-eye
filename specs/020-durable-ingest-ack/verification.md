# Verification: An event is never acknowledged until it is stored

**T026** — [quickstart.md](./quickstart.md) walked. "Done" is the observations,
so they are here rather than a tick.

Observed on 2026-08-19 against the real Aspire stack.

## 0. The loss, before anything changed

`AcknowledgedThenLostBaselineTests` (T001), against the pre-feature build. One
event published over the broker for a fab whose partition had been dropped, so
the write fails while the database itself stays healthy:

```
dropped events_hamburg — writes for this fab will now fail
published one event over the broker for fab hamburg
stored after the failed write: 0
  log: No event storage for fab hamburg for 01a0…; the envelope is dropped
restored events_hamburg — storage is possible again
stored after storage was restored: 0
```

The decisive line is the last one. The obstacle is gone and **the event does not
come back**, because the broker was told we had it on arrival and discarded its
copy. There is no retry, no redelivery, and no record beyond one log line.

The same run recorded the direct path: `POST /events/manual` with storage
unavailable answered **202 Accepted** and stored nothing.

> **A correction to that baseline.** Its MQTT half never actually delivered
> anything. The broker's dev ACL granted the scenario simulator
> `fab/munich/plc/#` alone, so every publish to hamburg was refused by the
> broker and the "stored: 0" it reported had a second, uninteresting cause. The
> defect is real — the direct-path half of the same run shows it without any
> broker involved, and §1 below reproduces the MQTT half properly — but the
> baseline as first written could not have told the difference. The ACL now
> carries one grant per fab in the realm.

## 1. An outage no longer loses anything (SC-001)

`OutageRecoveryIntegrationTests`. Twenty events published while hamburg's
storage is away, then the partition restored:

```
dropped events_hamburg — writes for this fab now fail
published 20 events while storage was away
stored during the outage: 0
restored events_hamburg — storage is possible again
after recovery: count=20 distinct=20
```

Both equalities, because they are different claims. Redelivery used to be a
rarity and is now the ordinary way an interruption ends, so "every event
arrived" and "no event arrived twice" can fail independently.

## 2. A restart loses nothing either (SC-002)

`RestartLosesNothingIntegrationTests`. 500 events published, the service
restarted while they were still draining:

```
published 500 events
restarted event-ingestion mid-drain
after restart: count=500 distinct=500
```

**Nothing was implemented for this.** It passes because an envelope sitting in
the channel is no longer something anyone was promised — the broker has not been
acknowledged, so it still holds its copy. The buffer did not become durable; it
stopped holding anything we had claimed to keep.

The service is restarted through Aspire rather than killed outright, because a
project resource is a local process the fixture would not get back. The
substitution is sound only because nothing is acknowledged before it is stored,
so a graceful stop and a crash leave the broker holding the same set.

> **This case does not run in CI**, and that is a real gap rather than a
> formality. The Aspire restart command fails outright on the CI runner
> ("Failed to stop resource"), and on its first CI run the failure left the
> service down — so eleven later EventIngestion tests failed with socket errors
> and the one real failure was buried. The test now restores the resource in a
> `finally` whatever happens, and carries `Category=Disruptive` so CI skips it.
> **SC-002 therefore has no CI coverage**; it is verified locally, by the run
> above, and by hand.

## 3. The sender is told the truth (SC-003)

`DirectWriteHonestyIntegrationTests`, both halves:

```
POST /events/manual                      -> 201 /events/01a0173a-22c9-7e38-bbe2-75b541190abd
GET  /events/01a0173a-22c9-7e38-bbe2-75b541190abd  -> 200, kind matches
POST /events/manual with storage away    -> 503, stored: 0
```

The `Location` is followed rather than parsed. A 201 pointing at a 404 is the
same lie in a better costume, and it is exactly what the old accept-then-buffer
would have produced.

## 4. One bad event does not stop the rest (SC-004)

`PoisonDeliveryEscapeIntegrationTests` — quickstart step 4, the step it calls
the one that cannot be faked. One delivery for a fab whose partition is gone,
and a hundred for a healthy fab, published together:

```
dropped events_hamburg — one delivery can now never be stored
published 1 poisoned + 100 healthy
healthy stored: 100 after 7.1s
dead letter: not storable after 00:01:30 of retrying
```

The hundred landed in 7.1 seconds against a retry window of ninety, so they can
only have been stored by the loop moving past the poisoned delivery rather than
waiting it out. The poisoned one was recorded before it was released, and the
dead letter names the bound it exhausted rather than merely reporting a failure.

**This is where the feature could have reintroduced spec 018's defect**, and the
first implementation did. The loop held its batch and retried it to exhaustion
before reading the channel again — with a five-minute window, one unstorable
delivery would have stopped ingestion for every fab for five minutes. It is
fixed by carrying the failure into the next cycle rather than blocking on it,
and `An_event_arriving_behind_a_failing_one_does_not_wait_for_it` fails against
the previous design.

## 5. Throughput, latency and order (SC-005, SC-006)

`IngestThroughputMeasurementTests`, run twice through the identical harness:
once on this branch, once in a worktree at `32ef1bb` — the commit this branch
was cut from. Forty publishing clients, each sending sequentially on its own
topic so per-source order is a real claim, capped at thirty seconds each.

| | before (`32ef1bb`) | after |
|---|---|---|
| offered | 129 040 in 148.9 s = **866/s** | 60 175 in 151.4 s = **398/s** |
| stored | **11 225** of 129 040 | **60 175** of 60 175 |
| sustained end to end | **46/s** | **398/s**, and never behind |
| arrival→visible p50 | **105 665 ms** | **164 ms** |
| p95 / p99 | 144 836 / 149 400 ms | 6 968 / 10 371 ms |
| per-source order | 0 inversions / 40 sources | 0 inversions / 40 sources |

Three things in that table need saying rather than leaving to be read.

**The before column stored 11 225 of the 129 040 it accepted.** Not "stored them
slowly" — the drain *stopped*: ten consecutive one-second samples with the count
unmoved. Every one of those events had been acknowledged to the broker on
arrival, so nothing was going to bring them back. That is issue #1546 in one
line, at scale, and it is the number every other claim here is measured against.

**The offered rate falling from 866/s to 398/s is the feature, not a
regression.** The old subscriber acknowledged on arrival, so the broker's
in-flight window never filled and it kept handing over events the system could
not store. Deferring the acknowledgement fills that window and the publishers
are slowed — which is FR-013 in as many words: senders capable of being slowed
are slowed rather than having their events dropped. The rate that matters for
SC-005 is what actually reaches storage, and that went from **46/s to 398/s**.

**398/s is a floor, not a ceiling.** Everything published had landed before the
drain window opened, so ingest was never the constraint in the after run — the
figure is what forty sequentially-acknowledging publishers could offer. The
harness cannot reach the 5 000/s spec 006 sizes this path for, and that number
is therefore **not established by this feature either way**. What is established
is the comparison SC-005 asks for.

**Latency (SC-006).** p50 of 164 ms is inside the ≤ 200 ms "event → overlay
state" leg of the end-to-end budget (constitution §IV). The tail is not, and it
is queueing under a deliberately saturating burst rather than per-event cost —
under the same burst before the change the *median* was 105 seconds. The
uncontended figure is the one the budget is about, and it is the p50.

**`max_inflight_messages`** is `2000` in `src/AppHost/mosquitto/mosquitto.conf`
(T004). It matters only now: while the subscriber acknowledged on arrival the
window never filled and mosquitto's default of 20 was invisible. The before
column was measured without it, which is what "before" means.

## What this feature does not do

**The escape cannot record a failure during a total outage** (research §R4). The
dead-letter write goes to the same database as the event write, so when the
database is away it fails for the same reason. The delivery then stays
unacknowledged and keeps being retried — which during an outage is the right
answer — but there is no record of it until storage returns. The escape is for a
bad row against a healthy database, not for an outage, and it is bounded
accordingly.

**The retry window is in memory.** A restart gives every failing delivery a
fresh window. Persisting it would mean a durable write per failed attempt, on
the path that is failing because writes are failing.

## 5a. Which 503 a caller sees first (a change spec 019 did not expect)

Spec 019 refuses a write for a fab with no storage, up front, with
`EVENT_FAB_NOT_PROVISIONED`. That answer is cached and the cache is allowed to
serve a stale *positive* — deliberately, because a wrong "yes" cost one logged
envelope while a wrong "no" would refuse a fab that can store perfectly well.

That trade changes here. The write is synchronous now, so a caller who slips
past a stale positive is refused **by the write itself**, with `EVENT_NOT_STORED`
— a 503 that arrives sooner and says less. Before this feature the same caller
got a 202 and the envelope was dropped inside the loop, which is exactly the
cost spec 019 was accepting.

Both refusals are 503 and both mean "not stored, retry", so no caller is misled.
`FabStorageRefusalIntegrationTests` had to change: it polled for *any* 503 and
then asserted the title, which now catches the earlier, vaguer one. It polls for
the provisioning refusal specifically, so it still proves what spec 019 FR-007
asks — that the refusal eventually names its cause — rather than being satisfied
by whichever 503 happens to arrive first.

## 6. Coverage (ADR-0065)

`scripts/coverage-check.ps1 -Configuration Release`. All twenty gates pass; the
two this feature moves:

```
SmartSentinelEye.EventIngestion.Application   83.1%   (gate >= 80%)  PASS
SmartSentinelEye.EventIngestion.Domain        96.4%   (gate >= 90%)  PASS
```

Run under Windows PowerShell 5.1 via a BOM-prefixed copy of the script — without
the byte-order mark 5.1 mis-decodes the script's own UTF-8 characters and fails
to parse. The copy is gitignored; the fix is pwsh 7, which is not on this
machine.

## 7. The suites

After the code review's fixes, on this branch:

```
Domain           102 passed
Application       46 passed
Infrastructure    25 passed
Architecture      23 passed
Integration      224 passed, 1 skipped, 0 failed   (measurement cases excluded)
```

The measurement cases carry `Category=Measurement` and are excluded from CI. A
saturating burst on a shared runner measures the runner, and a number that
measures the runner would later be quoted as if it measured this code.
