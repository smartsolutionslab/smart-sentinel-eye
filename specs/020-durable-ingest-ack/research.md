# Research: An event is never accepted until it is stored

**Feature**: `020-durable-ingest-ack` | **Date**: 2026-08-18

The mechanism was chosen before the spec — stop acknowledging early, each
ingress using its own means. Six questions had to be answered before that could
be written down as a design, and one of them changes what the feature costs.

## R1 — The broker's in-flight window is the throughput constraint

**Decision**: acknowledge in **batches**, and raise mosquitto's
`max_inflight_messages`. Both, because either alone fails.

MQTTnet 4.3 supports deferred acknowledgement: set `AutoAcknowledge = false` on
the received-message args and call `AcknowledgeAsync` later, from anywhere. So
the envelope and its acknowledgement can travel together through the existing
channel, and the persistence loop can commit a batch and then acknowledge the
whole batch.

**What nearly got missed.** `mosquitto.conf` does not set
`max_inflight_messages`, so it is at mosquitto's default of **20**. That number
is invisible today because the subscriber acknowledges on arrival — the window
never fills. The moment acknowledgement waits for storage, it becomes the hard
ceiling on ingest: at most 20 unacknowledged messages at a time.

Spec 006 sized this path for sustained bursts of **5 000 events/s**. Twenty
in flight at 5 000/s means every message must be acknowledged within **4 ms**,
which no per-message database round trip will do. With a batch commit of 20 and
a 2–5 ms transaction, it is borderline — and it stops being borderline the
moment the database is slower than usual, which is exactly the condition this
feature exists to survive.

So the broker's window has to grow. Mosquitto accepts `max_inflight_messages`
up to 65535, and `0` for unlimited; the plan sets it to a stated finite value
rather than 0, because unlimited in-flight simply moves the unbounded buffer
from our memory into the broker's.

**This is the cost of the feature, and it is a config change in the broker
rather than a line of C#.** Anyone reading this design later should know that
deferring the acknowledgement without raising the window would look like it
worked in a test and cap production ingest at a fraction of its requirement.

| Alternative | Why rejected |
|---|---|
| Acknowledge per message | 5 000/s × one round trip each. Fails FR-010 outright. |
| Keep early acknowledgement, add a durable buffer | Was option 3 in the decision; the user chose otherwise. It also makes ingest two writes on the hottest path. |
| `max_inflight_messages 0` (unlimited) | Removes the ceiling and the backpressure with it. The queue depth this feature relies on for FR-013 is the same mechanism. |

## R2 — Direct submissions store first, and answer 201

**Decision**: `POST /events/manual` and `POST /events/webhook/{name}` persist
synchronously and answer **201 Created** with a `Location` of the event's own
read route. Today they answer **202 Accepted** the moment the envelope is
queued.

202 means "accepted for processing, outcome unknown", which is exactly the
promise this feature removes. Once the row is committed before the response,
201 is the truthful code and `GET /events/{eventId}` already exists to point at.

**Why this is affordable here and not on the broker path.** These are
control-plane actions — an operator filing an event, a partner system posting a
webhook. The 5 000-slot channel exists to absorb plant-floor bursts, not these.
Trading a few milliseconds of latency for a truthful answer costs nothing that
matters on this path, and buys the whole of Story 1's update.

**A wire change, and it must be called that.** Any client asserting on 202 will
break. The alternative — keeping 202 while persisting first — would be more
compatible and less honest, and it would leave the next reader believing the
response still means "queued".

## R3 — What replaces the 429

**Decision**: a bounded concurrency limiter on the synchronous write path,
answering **429** when saturated. Same code, same meaning, different thing
being bounded.

Today's 429 comes from `TryWrite` failing on a full channel (spec 006 FR-022).
Once direct submissions no longer use the channel, that trigger disappears — and
if nothing replaces it, the endpoint silently becomes "queue indefinitely and
time out", which is a worse answer to overload than a fast refusal.

FR-013 exists to stop that happening by omission. The limiter's size is a
stated number, sized to the database's write capacity rather than to the old
channel's 5 000 slots, which measured nothing about direct submissions.

## R4 — The poison escape, and where the count lives

**Decision**: count attempts per event identifier in memory; after a stated
bound, write the delivery to `dead_letters` and **acknowledge it**, so the
broker stops redelivering.

QoS 1 redelivers until acknowledged, forever. Without a stopping rule, one
permanently unstorable event blocks the in-flight window and everything behind
it — which is precisely the "one bad row wedges ingestion" defect spec 018
fixed by adding the loop guard. This feature removes that guard's drop
behaviour, so it must supply the bound itself.

`dead_letters` already exists, already carries a fab (spec 018), and is already
the place an operator looks for deliveries that could not be processed. Reusing
it keeps one answer to "what could not be stored, and why".

**The counter is in memory and resets on restart, deliberately.** A restart
re-tries the event a few more times before giving up again, which is harmless;
persisting the counter would mean a durable write per failed attempt, on the
path that is failing because writes are failing.

**The honest hole**: when the database is down, the dead-letter write fails for
the same reason as the event write. So the escape only works for events that
are unstorable *specifically* — a bad row against a healthy database — and not
for a total outage. During an outage the correct behaviour is to keep retrying
anyway, so the two do not conflict; but the bound cannot be described as "we
always record it", and this plan does not describe it that way.

## R5 — Redelivery becomes an ordinary path

**Decision**: rely on the existing identifier-keyed idempotency (spec 006
FR-002) and prove it under redelivery rather than assume it.

Nothing new is required for exactly-once storage: the event carries its own
identifier and the ingest handler already treats a repeat as a no-op. What
changes is frequency. Today a duplicate is a rarity; after this feature every
outage produces a burst of them, and every unacknowledged in-flight message at
restart produces one.

A path that runs constantly and has only ever been reasoned about is the kind
that turns out to have a hole in it, so this gets a test that redelivers
deliberately — including the same event twice concurrently, which is what a
restart with a full in-flight window actually produces.

## R6 — What Story 2 costs, which is nothing

**Decision**: no durable buffer, no outbox, no change to the channel.

A crash is already handled by the two mechanisms above, and this is the part of
the design worth noticing: an envelope sitting in the in-memory channel is by
definition **not yet acknowledged**, because acknowledgement now happens after
storage. So a crash loses the buffer, the broker never received an
acknowledgement, and it redelivers. Direct submissions are not in the channel at
all any more — they are either committed or answered with an error.

The in-memory buffer stops being a durability hole not by being made durable,
but by no longer holding anything the system has promised to keep. That is why
the chosen approach is cheaper than the durable-buffer alternative it was chosen
over, and it is the single most important sentence in this document.
