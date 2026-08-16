# Data Model: Fab-scope event ingestion

**Feature**: `018-event-fab-scoping` | **Date**: 2026-08-16

## Event

**Unchanged.** It already carries a `FabIdentifier`, and both read handlers
already filter on it. This feature changes *where the fab in the query comes
from*, not the model.

That is the whole reason the leak survived five features aimed at it: every
question you would ask of the model answers correctly.

## DeadLetter

| Field | Change |
|---|---|
| `Fab` | **NEW.** `FabIdentifier`, **nullable**, set at capture from the parsed topic. |
| `Topic`, `RawPayload`, `Error`, `RejectedAt` | Unchanged. |

### Nullable, and this time the null is the point

Spec 016's stream fab was nullable as a *transitional* state — something a
follow-up migration would tighten away. This one is nullable **permanently and
by design**: a delivery whose topic cannot be parsed has no plant, and FR-010
forbids attributing it to one.

`NULL` is the honest answer, and it also does the work: `fab IN (caller's
fabs)` excludes it without a special case, so FR-011 falls out of the query
rather than needing to be remembered. Same property spec 016 relied on.

**There will be no follow-up NOT NULL migration**, unlike #1467 for streams.
Saying so here stops someone filing one.

### The two ways a capture can lack a fab

Verified in `MqttSubscriberHostedService.TryParseEnvelope`:

```csharp
string[] segments = topic.Split('/');
if (segments.Length != 4 || segments[0] != "fab")   // (a) address malformed
{
    return new ParseResult(null, $"Unexpected MQTT topic shape: '{topic}'.");
}
// ... then FabIdentifier / Source / DeviceIdentifier / Payload parsing   // (b) content malformed
```

| Failure | Topic | Fab |
|---|---|---|
| **(a)** shape wrong — not four segments, or not `fab/…` | unusable | **NULL** |
| **(b)** shape right, something inside it wrong | `fab/{fabId}/…` | **recoverable** |

**(b) is the common case.** Conflating the two — treating every dead letter as
orphaned — would make the whole list invisible and look like correct scoping.
That is the failure mode this table exists to prevent.

A subtlety in (b): the fab segment itself may be present but not a valid
`FabIdentifier` (uppercase, too short, non-ASCII). That is still case (a) for
our purposes — there is no fab to store — so the capture path must attempt
`FabIdentifier.From` and fall back to NULL rather than assume a four-segment
topic yields a usable fab.

## Column and migration

```sql
-- 1. add nullable, and it stays nullable
ALTER TABLE dead_letters ADD COLUMN fab VARCHAR(32);

-- 2. backfill from the address already stored. No guess, no other context:
--    the topic is right there. A row whose topic does not have the four-part
--    shape simply stays NULL, which is the correct answer rather than a
--    fallback.
UPDATE dead_letters
SET    fab = split_part(topic, '/', 2)
WHERE  fab IS NULL
  AND  split_part(topic, '/', 1) = 'fab'
  AND  array_length(string_to_array(topic, '/'), 1) = 4
  AND  split_part(topic, '/', 2) ~ '^[a-z][a-z0-9-]{1,31}$';

-- 3. the listing filter
CREATE INDEX ix_dead_letters_fab ON dead_letters (fab);
```

**The regex is the `FabIdentifier` grammar**, and it is there so the backfill
cannot write a value the domain would reject on read — the defect spec 015 hit
when a scaffolded `defaultValue: ""` produced unparseable rows. A topic
segment that is not a legal fab name leaves `NULL`.

**No `RAISE WARNING` and no announced count**, unlike specs 015 and 017. Those
backfills *guessed* `munich` and the warning existed to flag the guess. This
one derives from data already present, so there is nothing to warn about. Its
absence is the design, not an omission.

**`Down`** drops the index and the column. Safe: a dead letter without a fab
is exactly the state the system is built to tolerate.

## Queries

| Query | Change |
|---|---|
| `ListEventsQuery` | `Fab` (one) → `Fabs` (set) |
| `GetEventQuery` | `Fab` (one) → `Fabs` (set) |
| `ListDeadLettersQuery` | **+ `Fabs`** — it has no fab at all today |

The two event handlers change one predicate each:

```csharp
events.Where(e => e.Fab == query.Fab)        // before
events.Where(e => fabs.Contains(e.Fab))      // after
```

`ListDeadLettersQuery` gains the same term, and `NULL` satisfying no `IN` is
what implements FR-011.

## What is deliberately not modelled

- **`WebhookIntegration` gains nothing** (FR-016). Whether an integration
  belongs to a plant is a real question, and answering it here would widen a
  feature whose job is closing a live leak.
- **No new value object.** `FabIdentifier` already exists in this context —
  the only one of the six fab features where it did.
