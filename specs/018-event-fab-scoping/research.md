# Research: Fab-scope event ingestion

**Feature**: `018-event-fab-scoping` | **Date**: 2026-08-16

The spec left one mechanism open — how a rejected delivery acquires its plant
(FR-008) — and one requirement without an obvious home (FR-012, surfacing the
orphan count). Those are §1 and §2. Three further findings are recorded
because reading the code changed the design rather than confirming it.

---

## 1. How does a rejected delivery get its fab?

**Decision**: a **stored `fab` column on `dead_letters`, nullable**, written at
capture time from the parsed topic, and backfilled in SQL for existing rows.

**Rationale**:

1. **The listing has to filter on it.** FR-009 scopes the list to the caller's
   fabs. Deriving the fab at read time means either pulling every dead letter
   into memory to filter it, or pushing a string function into the predicate —
   both of which turn "show me my plant's failures" into a scan of every
   plant's.
2. **Nullable expresses the thing that matters.** FR-010 says an
   unestablishable plant must not be attributed to any. `NULL` says exactly
   that, and `fab IN (caller's fabs)` excludes it without a special case —
   the same property spec 016 relied on for FR-009.
3. **The backfill needs no guess and no other context.** The topic is already
   stored, so the fab is recoverable from data this table already holds:
   `split_part(topic, '/', 2)` where the topic has the four-segment shape.
   Unlike spec 016, nothing has to be derived at runtime from another
   database, and unlike spec 015's, nothing is guessed — a row whose topic
   does not parse simply stays `NULL`, which is the correct answer rather than
   a fallback.

**Alternatives considered**:

- ***Derive at read time from the topic string.*** No column, no migration, no
  backfill — and that is the whole appeal. Rejected on the filter: the scoping
  predicate would run on a computed value, so either the database scans every
  row or the application does. It also puts the parse in two places once the
  capture path starts recording it.
- ***Store the fab only when the topic parses, and delete the rest.*** Rejected
  outright. A dead letter exists to be diagnosed; deleting the ones that are
  hardest to diagnose is the opposite of the requirement.
- ***Make the column NOT NULL with a sentinel.*** Rejected. A sentinel fab is a
  fab, so it would be visible to whoever holds that name, and FR-010 forbids
  attributing an unestablishable plant to any plant.

### The parse has two failure modes, and only one produces a NULL

Verified in `MqttSubscriberHostedService.TryParseEnvelope`:

```csharp
string[] segments = topic.Split('/');
if (segments.Length != 4 || segments[0] != "fab")
{
    return new ParseResult(null, $"Unexpected MQTT topic shape: '{topic}'.");
}
```

A delivery is dead-lettered when **either** the topic shape is wrong **or**
something inside a well-formed topic is — a bad fab name, source, device, or
payload. The second is the common case and **has a recoverable fab**. Only the
first is genuinely orphaned.

This distinction is the whole of FR-010's scope, and it is easy to get wrong
in the direction that hides too much: treating every dead letter as orphaned
would make the entire list invisible.

---

## 2. Where does the orphan count surface? (FR-012)

**Decision**: a **log record at capture time**, emitted when a delivery is
dead-lettered with no establishable fab, plus the count in the existing
startup/periodic ingest logging — not a new endpoint.

**Rationale**: FR-011 makes these rows unreadable through the API by anyone.
FR-012 exists so that "invisible" does not become "unnoticed". A log line
carries the *fact* and the *count* without carrying the payload, which is
exactly the split the two requirements draw. Constitution §VII already routes
these to the Aspire dashboard and Grafana.

**Alternatives considered**:

- ***A dedicated endpoint returning only a count.*** Rejected as speculative
  generality — no operator has asked for it, and a count with no way to act on
  it is a worse answer than a log an on-call engineer already reads.
- ***Include orphans in the list with their payload redacted.*** Tempting, and
  rejected on FR-010: a redacted row still tells every operator that a
  delivery arrived and failed, which is information about another plant's
  ingest whenever the topic was *nearly* right.

---

## 3. `?fabId=` is currently **required** on three endpoints

**Finding**, and it changes the wire compatibility story. Verified:

| Endpoint | Today |
|---|---|
| `GET /events` | `[FromQuery] string fabId` — **required** |
| `GET /events/{eventId}` | `[FromQuery] string fabId` — **required** |
| `POST /events/manual` | `[FromQuery] string fabId` — **required** |

So this feature makes a **required parameter optional**, not the reverse. A
caller that passes a fab it holds keeps working unchanged; a caller that
passes one it does not hold starts being refused, which is the point; and a
caller that omits it now gets its own fabs rather than a 400.

That is a strictly widening change for legitimate callers and a strictly
narrowing one for illegitimate ones — the best shape available, and worth
stating because "we made a parameter optional" usually implies the opposite.

---

## 4. The webhook ingress already does this correctly

**Finding**, and the reason FR-014 is an exemption rather than an oversight.
`EventsEndpoints.Writes.cs` validates the JWT path against the caller's own
groups:

```csharp
string targetGroup = "/fabs/" + fabId;
```

So the webhook already refuses a fab its caller does not hold. Its caller is a
machine presenting its own credentials, and there is no operator session to
resolve. Same shape as spec 016's `POST /streams/authorize` exemption —
recorded rather than left as an unexamined endpoint.

**The manual write is the one that looks like it and is not.** Both take
`?fabId=`; only one checks it. That symmetry is presumably how the gap
survived.

---

## 5. The read handlers need almost nothing

**Finding**: `ListEventsQueryHandler` and `GetEventQueryHandler` already filter
on a fab — they just take one rather than a set:

```csharp
events.Where(eventEntity => eventEntity.Fab == query.Fab)
```

Widening that to `fabs.Contains(eventEntity.Fab)` is the whole change, and it
is the same shape specs 015–017 already use. **The work in this feature is
almost entirely at the endpoint boundary**, which is where the missing check
belongs.

---

## Not researched, deliberately

**Whether the fab mechanism is right.** ADR-0114 settled it and five specs have
applied it. This is the sixth application.

**Whether a webhook integration should carry a fab.** Out of scope by FR-016,
recorded there as a real question with two coherent answers rather than an
omission.
