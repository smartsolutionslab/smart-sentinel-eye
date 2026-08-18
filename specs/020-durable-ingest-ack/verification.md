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

## 3. The sender is told the truth (SC-003)

`DirectWriteHonestyIntegrationTests`, both halves:

```
POST /events/manual                      -> 201 /events/01a0…
GET  /events/01a0…                       -> 200, kind matches
POST /events/manual with storage away    -> 503, stored: 0
```

The `Location` is followed rather than parsed. A 201 pointing at a 404 is the
same lie in a better costume, and it is exactly what the old accept-then-buffer
would have produced.

## 4. One bad event does not stop the rest (SC-004)

`PoisonDeliveryEscapeIntegrationTests` — quickstart step 4, the step it calls
the one that cannot be faked. One delivery for a fab whose partition is gone,
and a hundred for a healthy fab, published together:

<!-- FILL: healthy stored / took / dead letter line -->

The healthy fab is asserted first and on a deadline far shorter than the retry
window, so the hundred can only be on time if the loop moved past the poisoned
delivery instead of waiting it out.

**This is where the feature could have reintroduced spec 018's defect**, and the
first implementation did. The loop held its batch and retried it to exhaustion
before reading the channel again — with a five-minute window, one unstorable
delivery would have stopped ingestion for every fab for five minutes. It is
fixed by carrying the failure into the next cycle rather than blocking on it,
and `An_event_arriving_behind_a_failing_one_does_not_wait_for_it` fails against
the previous design.

## 5. Throughput, latency and order (SC-005, SC-006)

<!-- FILL: before/after table -->

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
