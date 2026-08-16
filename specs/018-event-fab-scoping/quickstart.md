# Quickstart: Fab-scope event ingestion

**Feature**: `018-event-fab-scoping`

"Done" is the observations, not the walk. Record them on the PR.

## 0. Reproduce the leak first

**Do this before the change, or the rest proves less than it appears.** Every
step below asserts a refusal, and a refusal is only evidence if the thing was
permitted a moment earlier.

```sh
# As op-dresden@dresden.test, against the CURRENT build:
GET /events?fabId=munich          # returns munich's events
GET /events/{a munich event}?fabId=munich
POST /events/manual?fabId=munich  # ingests INTO munich
GET /events/dead-letters          # every plant's raw payloads
```

Record what comes back. That is the baseline the feature removes.

## 1. The reads

| As | Request | Expect |
|---|---|---|
| `op-dresden` | `GET /events` (no `fabId`) | dresden's only — **not a 400** |
| `op-dresden` | `GET /events?fabId=munich` | **403** |
| `op-dresden` | `GET /events/{munich event}` | **404** |
| `op-dresden` | `GET /events/{never existed}` | **404**, byte-identical to the above |
| `op-multi` | `GET /events` | both plants |
| `op-multi` | `GET /events?fabId=dresden` | narrowed to dresden |

Compare the two 404s **field by field** with `traceId` and the requested
identifier normalised out. A difference in `title` or `type` lets an operator
confirm another plant's event exists.

## 2. The write — the one that changes another plant's state

```sh
# As op-dresden:
POST /events/manual?fabId=munich   -> 403
```

Then **check munich's stream**: `GET /events?fabId=munich` as `op-multi`. The
refused event must not be there. A 403 that still enqueued would be worse than
no check at all, because the response would say it had been stopped.

| As | Request | Expect |
|---|---|---|
| `op-dresden` | `POST /events/manual` (no `fabId`) | **201**, filed against dresden |
| `op-multi` | `POST /events/manual` (no `fabId`) | **400** `EVENT_FAB_REQUIRED` |
| `op-multi` | `POST /events/manual?fabId=dresden` | **201** |
| `op-dresden` | `POST /events/manual?fabId=munich` | **403**, and nothing ingested |

The `op-dresden` inference is the case that cannot be faked: everything else in
the system defaults to munich, so a broken inference falling back to the
default passes against a munich operator and only fails here.

## 3. The rejected deliveries — the step that cannot be faked

You need **three** rejected deliveries, and producing the third is the point.

```sh
# (a) munich, payload rejected — topic is well formed
mosquitto_pub -t 'fab/munich/manual/dev-1' -m 'not json'

# (b) dresden, payload rejected
mosquitto_pub -t 'fab/dresden/manual/dev-1' -m 'not json'

# (c) TOPIC malformed — no fab can be established
mosquitto_pub -t 'garbage/topic' -m '{}'
```

Then:

| As | Expect |
|---|---|
| `op-dresden` | sees **(b)** only |
| `op-multi` | sees **(a)** and **(b)** |
| anyone at all | **never (c)** |

**(c) is the assertion that matters and the one that is invisible when it
works** — nothing appears either way. Check the database directly to confirm
it is really there and really unattributed:

```sql
SELECT topic, fab FROM dead_letters WHERE fab IS NULL;
-- expect the 'garbage/topic' row, and only that one
```

If (a) and (b) are also NULL, the capture path is treating every dead letter as
orphaned — which hides the whole list and looks exactly like correct scoping.

Then confirm FR-012 — that invisible did not become unnoticed:

```
warn: ...MqttSubscriberHostedService
      Rejected a delivery with no establishable fab (topic 'garbage/topic').
```

## 4. The migration

```sql
SELECT count(*) FROM dead_letters WHERE fab IS NULL;
```

Compare against how many stored topics do **not** have the `fab/a/b/c` shape.
Those two numbers must match. If every row is NULL the backfill's guard is too
strict; if none is, it is too loose and has written a fab the domain will
reject on read.

## 5. Ingest is untouched

Publish a well-formed delivery on the broker and a well-formed webhook call.
Both must ingest exactly as before — same success, same rate (SC-006). These
are the throughput paths and this feature must not have touched them.

`POST /events/webhook/{name}` in particular: it takes the same `?fabId=` as the
manual write and already checks it. If webhook ingest breaks, the wrong one of
the two endpoints in that file was edited.
