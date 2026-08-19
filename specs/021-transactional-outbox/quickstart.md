# Quickstart: An integration event is never lost after its write commits

**Feature**: `021-transactional-outbox`

"Done" is the observations, not the walk. Record them on the PR.

**Read this first.** This feature is invisible when it works. Every happy-path
test passes identically before and after, so a walk that only exercises success
proves nothing at all. Every step below breaks something on purpose.

## 0. Reproduce the loss first

**Before the change.** Stop the broker, then make a write that announces:

```sh
docker stop <rabbitmq container>
```

```sh
POST /events/manual        # as any operator
```

| Expect | |
|---|---|
| response | **201 Created** — the write succeeded |
| `GET` the `Location` | **200** — the event is there |
| Automation | never evaluated a rule for it |
| AuditObservability | has no record of it |
| the log | one line, at most |

```sql
-- the event exists
SELECT count(*) FROM events WHERE kind = '<the kind you sent>';   -- expect 1

-- and nothing is pending anywhere
SELECT count(*) FROM wolverine_event_ingestion.wolverine_outgoing_envelopes;  -- expect 0
```

**That pair of numbers is the defect.** The event is stored, the caller was told
so truthfully, and the announcement is not in flight, not queued, and not
recorded as owed. Restart the broker and wait as long as you like — nothing
arrives, because nothing is holding a copy.

Do the same on a second context (register a camera) so it is clear this is not
an ingest problem.

## 1. The write and its announcement share a fate

Repeat step 0 exactly.

| Expect | |
|---|---|
| response | 201, unchanged |
| `wolverine_outgoing_envelopes` | **one row, waiting** |
| after the broker returns | the row disappears and the consumers act |

```sql
SELECT count(*) FROM wolverine_event_ingestion.wolverine_outgoing_envelopes;
-- expect 1 while the broker is down, 0 after it returns
```

The row is the whole feature. Before, there was nothing to point at.

## 2. A rolled-back write announces nothing (SC-003)

The opposite failure, and the one that a naive fix gets wrong — capturing the
message before the write is committed is only safe if it is discarded when the
write is not.

Force a write to fail after its domain event has been raised (drop the partition
for a fab, or use a payload the database will refuse), then:

```sql
SELECT count(*) FROM wolverine_event_ingestion.wolverine_outgoing_envelopes;  -- expect 0
```

**If this is not zero, the feature has traded a lost announcement for a false
one**, which is worse: consumers would act on a write that never happened.

## 3. A kill between commit and flush loses nothing (SC-002)

```sh
# publish a burst, then, while it drains:
docker kill <event-ingestion>
```

Restart. Every committed event's announcement must arrive. The messages were in
the outbox, in the same transaction as the rows, so the recovery agent has them.

## 4. The guarantee is not only where the defect was found (SC-004)

Repeat step 1 in a different context — register a camera with the broker down,
confirm the pending row appears in `wolverine_camera_catalog`, restart the
broker, confirm it drains.

Nine write paths were changed. Testing one proves the seam, not the coverage.

## 5. A backlog is visible before it is a disk problem (SC-006)

With the broker still down and a few hundred writes made:

| Expect | |
|---|---|
| pending count | readable without attaching a debugger |
| oldest pending age | readable, and growing |

**An outbox quietly growing looks exactly like an empty one** until the disk
fills. If you cannot answer "how many, and how old is the oldest?" from the
health or metrics surface, FR-008 is not met however well the rest works.

## 6. Throughput and latency (SC-005)

Spec 020 left the harness; run it before and after, the same way.

```
IngestThroughputMeasurementTests   — offered, sustained, p50/p95/p99, ordering
```

The expectation is neutral-to-better: the change removes a synchronous broker
hop from the write path and adds rows to a transaction already open. **The
expectation is not the deliverable — the two numbers are.**

Watch the row volume: 200 events per batch becomes 200 event rows plus 200
outbox rows in one commit, on the highest-throughput path in the product.

## 7. The record says what is true (SC-007)

Read the amended ADR-0088 and answer, for a write path you have not seen: is it
covered? If the answer needs the source, the amendment has not done its job —
that ambiguity is what let this defect sit behind a document claiming the
opposite.
