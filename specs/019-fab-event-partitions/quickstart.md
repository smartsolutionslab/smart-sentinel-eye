# Quickstart: A plant that exists can store its events

**Feature**: `019-fab-event-partitions`

"Done" is the observations, not the walk. Record them on the PR.

## 0. Reproduce the loss first

**Before the change.** Every step below asserts that something now works or is
now refused, and neither proves anything unless the loss was real a moment
earlier.

```sh
# Add a fab group that has never existed, and an operator in it:
#   Keycloak → Groups → /fabs → new group 'berlin'
#   assign an operator to /fabs/berlin
# Then, as that operator, against the CURRENT build:
POST /events/manual        -> 202 Accepted    (berlin inferred)
GET  /events               -> 200, empty
```

Then look in the database:

```sql
SELECT count(*) FROM events WHERE fab_id = 'berlin';   -- 0
```

And in the log:

```
fail: ...PersistenceLoopHostedService
      Ingest dispatch faulted for <id> in fab berlin; the envelope is dropped
```

**That is the whole defect in four lines**: accepted, acknowledged, never
stored, one log line that does not say why. Record what you see.

## 1. Provisioning follows the realm

Restart the stack, or run the migration job alone. With `/fabs/berlin` present
and no `events_berlin` table:

```sql
SELECT relname FROM pg_class c
JOIN pg_inherits i ON i.inhrelid = c.oid
JOIN pg_class p ON i.inhparent = p.oid
WHERE p.relname = 'events'
ORDER BY relname;
```

| Expect | |
|---|---|
| `events_berlin` | **new**, created without anyone writing a migration |
| `events_munich`, `events_dresden` | unchanged |
| `events_berlin_<this month>`, `events_berlin_<next>` | created in the **same pass** |

The monthly children are the half worth checking. A fab partition with no month
beneath it can store exactly as little as no partition at all, so provisioning
must run *before* the rollover, not after.

Then repeat the whole run:

| Expect | |
|---|---|
| second run | no change, no error, no duplicate |
| every pre-existing event | still there |

## 2. The event that was lost now lands

As the berlin operator, exactly as in step 0:

```sh
POST /events/manual   -> 202
GET  /events          -> the event
```

Nothing else changed — same request, same token. Only the storage caught up.

## 3. The refusal — the step that cannot be faked

You need a fab that exists to the realm but **not** to storage, which is the
state the whole feature is about. Make it by hand:

```sql
-- with 'hamburg' as a group in the realm, and an operator in it:
DROP TABLE events_hamburg;    -- or simply add the group and do not re-run provisioning
```

| As | Request | Expect |
|---|---|---|
| `op-hamburg` | `POST /events/manual` | **503** `EVENT_FAB_NOT_PROVISIONED` |
| any machine | `POST /events/webhook/{name}?fabId=hamburg` | **503**, same code |
| `op-munich` | `POST /events/manual` | **202**, untouched |

Then confirm the refusal really refused:

```sql
SELECT count(*) FROM events WHERE fab_id = 'hamburg';   -- 0
```

**Nothing enqueued** is the assertion, not the status code. A 503 that had
already written to the channel would be the same defect with a better error
message — the event would land, late, while the caller was told it had not.

## 4. The residue, which is invisible when it works

Drop a partition *while* the service is running and its readiness cache is
warm, then file an event immediately. The check may pass on a stale cache and
the insert will fail. That race is expected — the point is that it is now
legible:

```
fail: ...PersistenceLoopHostedService
      No event storage for fab hamburg; the envelope is dropped. A partition
      for this fab is missing — provisioning has not run since it was added.
```

Compare against the step-0 line. Same drop, different sentence: the old one
could have meant anything.

**The envelope is still dropped.** That is #1546 and is deliberately not fixed
here — see `contracts/provisioning.md`. Do not record this step as a failure.

## 5. Removing a fab destroys nothing

```sh
# Remove /fabs/berlin from the realm, then re-run provisioning.
```

```sql
SELECT count(*) FROM events WHERE fab_id = 'berlin';    -- unchanged
\dt events_berlin                                        -- still there
```

If this step ever fails, it has deleted a plant's entire history and no other
step in this walk matters.

## 6. When the realm cannot be reached

Stop Keycloak and run the migration job.

| Expect | |
|---|---|
| the run | **fails**, non-zero exit, naming the realm as unreachable |
| the log | never "no fabs found" |
| services | do not start, because they wait for the job |

"Cannot tell" must not look like "there are none". That mistake would provision
nothing, report success, and restore exactly the silence this feature removes.

## 7. Ingest is untouched

A well-formed broker delivery and a well-formed webhook call for a
**provisioned** fab both ingest exactly as before — same acceptance, same
rate (SC-007). The readiness check sits on the operator write path and resolves
to a set lookup; if it ever shows up in an ingest measurement, it is wrong.
