# Contracts: ingest acknowledgement

**Feature**: `020-durable-ingest-ack` | **Date**: 2026-08-18

One wire change, one status whose meaning is preserved by moving it, one
internal signal, and one broker setting that the rest depends on.

## `POST /events/manual` — stores before it answers

| | Before | After |
|---|---|---|
| Success | **202 Accepted**, body = event identifier | **201 Created**, body = event identifier, `Location: /events/{id}` |
| Meaning | queued; outcome unknown | stored; readable at `Location` |
| Storage unavailable | 202, then silence | **503**, and nothing stored |
| Overload | **429** when the channel is full | **429** when the write limiter is saturated |
| Fab not provisioned | 503 (spec 019) | unchanged |

**201 is the point, not a tidy-up.** 202 means "accepted for processing,
outcome unknown", which is the exact promise this feature removes. Keeping 202
while persisting first would be more compatible and less true, and would leave
the next reader believing the response still means "queued".

**This is a breaking wire change.** Any client asserting on 202 breaks. There is
no transition period and no header to opt in: two codes meaning different things
about durability, both live, would reintroduce the ambiguity being removed.

## `POST /events/webhook/{integrationName}` — the same

Identical treatment: **201** on success with `Location`, **503** when storage is
unavailable, **429** under overload. A partner system's retry logic can act on
5xx; it cannot act on a 202 followed by nothing.

## The 429 keeps its meaning, and gets a new cause

Today it fires when the bounded channel is full (spec 006 FR-022). Direct
submissions no longer use that channel, so without a replacement the endpoint
would silently become "queue and eventually time out" — a worse answer to
overload than a fast refusal, arrived at by omission rather than decision.

| | |
|---|---|
| Bound | a stated maximum of concurrent synchronous writes |
| Sized by | the database's write capacity, not the old 5 000-slot channel |
| Exceeded | **429**, immediately, as today |

## The broker path — acknowledged after the write

| | Before | After |
|---|---|---|
| When acknowledged | on arrival, automatically | after the event is committed |
| Failure to store | envelope dropped, one log line | **not acknowledged** → broker redelivers |
| Process killed | buffer lost silently | unacknowledged deliveries redelivered on reconnect |
| Grouping | per message | **per batch**: commit N, then acknowledge those N |

**`max_inflight_messages` must be raised.** It is unset in `mosquitto.conf`,
which means 20. Deferred acknowledgement turns that into the ingest ceiling —
at 5 000 events/s, twenty in flight demands an acknowledgement every 4 ms.
Batching alone cannot fix it, because the window bounds how many can be batched.
Set to a stated finite value; **not** `0`, which removes the backpressure this
design relies on and moves the unbounded buffer into the broker.

## The escape from an unstorable delivery

| | |
|---|---|
| Trigger | the same delivery fails to store a stated number of times |
| Then | written to `dead_letters` with the failure reason, and **acknowledged** |
| Why acknowledge | QoS 1 redelivers until acknowledged, forever; without this, one bad delivery fills the in-flight window and blocks everything behind it |
| Counter | in memory, keyed by event identifier; resets on restart |

**The honest limit**: when the database is down, the dead-letter write fails for
the same reason as the event write. The escape therefore covers a bad row
against a healthy database, not a total outage — during which the correct
behaviour is to keep retrying anyway. This is not "we always record it", and is
not described as such.

## Internal: the completion signal

`IIngestChannel` carries, with each envelope, the means to report the outcome:
stored, or permanently unstorable. For a broker delivery it is the
acknowledgement; direct submissions no longer travel through the channel.

One signal on the existing channel rather than a second channel type, so a
future durable buffer can be substituted without either ingress noticing.

## Unchanged

Event shape, identifier, fab resolution, partition provisioning, who may read
or write which fab's events, and the per-source FIFO ordering guarantee. Specs
018 and 019 are untouched.
