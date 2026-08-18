# Quickstart: An event is never accepted until it is stored

**Feature**: `020-durable-ingest-ack`

"Done" is the observations, not the walk. Record them on the PR.

## 0. Reproduce the loss first

**Before the change**, and in two ways, because they fail differently.

```sh
# (a) Storage away, machine sending.
docker pause <postgres container>
mosquitto_pub -t 'fab/munich/plc/station-4' -m '<valid event>'   # x20
docker unpause <postgres container>
```

```sql
SELECT count(*) FROM events WHERE kind = '<the kind you sent>';   -- expect 0
```

```
fail: ...PersistenceLoopHostedService
      Ingest dispatch faulted for … ; the envelope is dropped and the loop continues.
```

Twenty events published, twenty acknowledged to the broker, none stored, and the
broker has discarded its copies because we told it we had them.

```sh
# (b) Service killed mid-burst.
# publish a few thousand, then, while they drain:
docker kill <event-ingestion>
```

Restart, count what arrived against what was sent. The difference is the buffer,
and note what the log says about it: **nothing**. Case (a) at least complains.

Record both numbers. Everything below is measured against them.

## 1. An outage no longer loses anything

Repeat (a) exactly.

| Expect | |
|---|---|
| during the pause | events are **not** acknowledged; the broker's queue depth grows |
| after unpause | every event published is stored, **exactly once each** |
| the log | says the interruption happened, how long it lasted, and how many events it covered |

```sql
SELECT count(*), count(DISTINCT event_id) FROM events WHERE kind = '<kind>';
-- the two numbers must be equal, and equal to what you published
```

The second column is the assertion that matters. Redelivery is now an ordinary
event rather than a rarity, so "every event arrived" and "no event arrived
twice" are two different claims and both need checking.

## 2. A kill loses nothing either

Repeat (b) exactly. After restart, the count must match what was published —
the unacknowledged deliveries come back on reconnect.

**This is the step that proves the in-memory buffer is no longer a hole.** It
did not become durable; it stopped holding anything we had promised to keep.

## 3. The sender is told the truth

```sh
docker pause <postgres container>
POST /events/manual        # as any operator
```

| Expect | |
|---|---|
| response | **5xx**, promptly — not 202, not a hang until timeout |
| stored | nothing |
| after unpause, retry | **201 Created**, with `Location: /events/{id}` |

`GET` that `Location`. If it 404s, the 201 was a lie and this step failed.

## 4. One bad event does not stop the rest — the step that cannot be faked

You need a delivery that can never be stored while the database is **healthy**,
so the escape is actually reachable. The simplest is a fab whose partition has
been dropped (spec 019 leaves this reachable by hand):

```sql
DROP TABLE events_hamburg;
```

```sh
mosquitto_pub -t 'fab/hamburg/plc/dev-1' -m '<valid event>'      # the bad one
mosquitto_pub -t 'fab/munich/plc/station-4' -m '<valid event>'   # x100, the rest
```

| Expect | |
|---|---|
| the bad delivery | retried a bounded number of times, then written to `dead_letters` and acknowledged |
| the broker | stops redelivering it |
| the other 100 | all stored, at the normal rate, throughout |

```sql
SELECT topic, error FROM dead_letters ORDER BY rejected_at DESC LIMIT 5;
```

**If the other hundred are delayed or missing, the feature has reintroduced the
defect spec 018 fixed** — one bad row wedging ingestion — and the bound is
wrong or in the wrong place.

## 5. Throughput and order survive

The requirement this change is most likely to break quietly.

```sh
# sustained publish at the rate spec 006 sized for
5 000 events/s for 30 s
```

| Expect | |
|---|---|
| stored | all of them |
| rate | **no lower than the figure recorded before the change** — measure both |
| order | per source, the order published is the order stored |
| in-flight | the broker's queue depth rises and falls; it does not sit pinned at the window size |

**Check `max_inflight_messages` first.** Unset, it is 20, and this step will cap
at a fraction of the target while every other step still passes. That is the one
number in this feature that makes it look finished when it is not.

## 6. The latency leg

Measure arrival-to-visible before and after, and cite it against the ≤ 200 ms
share of the end-to-end budget. Batching adds at most one batch window; the
requirement is the measurement, not the argument.
