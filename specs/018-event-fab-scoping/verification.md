# Verification: Fab-scope event ingestion

**T028** — [quickstart.md](./quickstart.md) walked. "Done" is the observations,
so they are here rather than a tick.

Observed against the real Aspire stack on 2026-08-17, at commit `906a610`.

## The baseline it is measured against

T001, recorded before anything changed, as `op-dresden@dresden.test`:

```
GET  /events?fabId=munich        -> 200   reads another plant's events
POST /events/manual?fabId=munich -> 202   files an event INTO another plant
GET  /events/dead-letters        -> 200   every plant's raw payloads, verbatim
```

Every observation below is a refusal, and a refusal is only evidence because
those three were permitted a moment earlier.

## What was observed, and how

| Quickstart step | Observed by | Result |
|---|---|---|
| 1. The reads | `EventFabScopingIntegrationTests` | ✅ |
| 2. The write | `ManualIngestFabScopingIntegrationTests` | ✅ |
| 3. The rejected deliveries | `DeadLetterFabScopingIntegrationTests` + a walk over the broker | ✅ **see the ACL note** |
| 4. The migration | walked against a populated table | ✅ |
| 5. Ingest untouched | walked — broker and webhook | ✅ |

**24/24** EventIngestion integration cases pass, plus 102 domain and 38
application unit tests. Coverage: `EventIngestion.Domain`
**96.0%** (gate ≥ 90%), `Application` **83.0%** (gate ≥ 80%); all twenty gates
pass.

## 1 and 2 — the reads and the write

Both are asserted rather than walked by hand, in the two integration files
above: a Dresden operator naming Munich is **403** on the reads and on the
manual write; another plant's event by identifier is **404**, compared field by
field with one that never existed and differing only in `traceId` and the
identifier the caller supplied; a refused write leaves Munich's stream
unchanged, checked by listing it afterwards rather than by trusting the status
code.

## 3 — the rejected deliveries

Three deliveries published over the real broker as the seeded
`scenario-simulator` client, and read back as each operator:

| Delivery | Address | Stored fab |
|---|---|---|
| (a) payload rejected | `fab/munich/plc/dev-1` | `munich` |
| (b) payload rejected | `fab/dresden/plc/dev-1` | `dresden` |
| (c) address names no plant | `fab/NOT-A-FAB/plc/dev-1` | **NULL** |

```
op-dresden sees: (b)
op-multi   sees: (b), (a)
nobody     sees: (c)
```

The database agrees, which is the assertion that matters — (c) is invisible
either way, so its absence from a listing proves nothing on its own:

```
fab/NOT-A-FAB/plc/dev-1 -> NULL
fab/dresden/plc/dev-1   -> dresden
fab/munich/plc/dev-1    -> munich
```

If (a) and (b) had also been NULL, the capture path would be treating every
rejection as orphaned — hiding the whole list while looking exactly like
correct scoping. They are not.

FR-012, observed in the service log:

```
warn: ...MqttSubscriberHostedService
      Rejected delivery on 'fab/NOT-A-FAB/plc/dev-1' names no fab;
      it is visible to no operator. 1 such deliveries since start.
```

### Two corrections to the quickstart, recorded rather than glossed

**`garbage/topic` cannot reach the subscriber at all.** The subscriber
subscribes to `fab/+/+/+`, so a two-segment topic is never delivered and can
never be dead-lettered. The reachable form of case (c) is a four-segment topic
whose fab segment is not a legal `FabIdentifier` — which
`data-model.md` anticipated ("still case (a) for our purposes") without
noticing it is the *only* form the broker path can produce. The shape guard in
`TryParseFab` is still reachable through a non-default `SubscribeTopic`.

**The ACL note.** The dev broker grants `scenario-simulator` writes under
`fab/munich/` only, so (b) and (c) could not be published without widening a
fab-scoped grant — the thing this feature exists to prevent. The walk above was
run with a temporary local `topic write fab/#` line that is **not committed**;
`acl.txt` is unchanged in this PR. The committed integration test therefore
publishes (a) over the broker and captures (b) and (c) through the aggregate
directly, which is the honest split: the leg at risk was attribution from the
address, and that is the one exercised over the wire.

## 4 — the migration, on a populated table

The fixture migrates before any row exists, so nine pre-existing rows were
inserted with a NULL fab and the migration's own `UPDATE` was then run verbatim
against them:

```
rows attributed by the backfill: 3

fab/munich/plc/station-4     -> munich
fab/dresden/inference/cam-12 -> dresden
fab/munich-2/plc/x           -> munich-2
fab/MUNICH/plc/x             -> NULL     uppercase: not a legal fab name
fab/m/plc/x                  -> NULL     one character: below the minimum
fab/munich/plc               -> NULL     three segments
fab/munich/plc/a/b           -> NULL     five segments
garbage/topic                -> NULL     two segments
notfab/munich/plc/x          -> NULL     first segment is not 'fab'

fab IS NULL: 6   topics without a legal fab/a/b/c address: 6
```

**The two counts match**, which is SC-005. Neither failure mode is present: not
every row is NULL (the guard is not too strict), and not every row was written
(it is not too loose). The three rows the grammar rejects are the point of the
regex — without it the backfill would have written `MUNICH` and `m`, and the
domain would have thrown on the next read, which is the defect spec 015 hit.

## 5 — ingest is untouched (SC-006)

```
broker : well-formed delivery on fab/munich/plc/station-4 -> 1 event listed
webhook: POST /webhook-integrations                       -> 201
         POST /events/webhook/{name}?fabId=munich         -> 202
```

The webhook is the one to watch: it lives in the same file as the manual write
and takes the same `?fabId=`, and only the manual write was meant to change.
Its `"/fabs/" + fabId` check is byte-identical to before.

**A timing trap worth knowing about.** The broker leg first reported *zero*
events, and it was not a regression: the publish landed 12 seconds before the
subscriber connected, so the broker had nobody to deliver it to. Both
MQTT-publishing tests now republish until the delivery is seen. A single
publish at the start of a fixture run is a coin toss, and it would have failed
in CI as a mystery rather than as this.

## One thing a reviewer should look at

EF does not translate `fabs.Contains(deadLetter.Fab)` into a plain `IN`. The
generated SQL is:

```sql
WHERE d.fab = ANY (@fabs) OR (d.fab IS NULL AND array_position(@fabs, NULL) IS NOT NULL)
```

So "NULL satisfies no `IN`", the reasoning `data-model.md` leans on for FR-011,
holds one step less directly than it reads: an unattributed row *would* be
returned if `@fabs` ever contained a NULL element. It cannot —
`ResolveReadFabsAsync` builds the list by parsing each group and adding only
what parses, so a null can never enter it — and the behaviour is asserted from
the multi-fab operator's side, who holds every fab there is. Worth knowing
that the guarantee rests on the resolver rather than on SQL semantics alone.

## What the review found (phase 6 QA)

`/code-review` and `/security-review` were run on the branch. Both landed
independently on the same finding, and it is the one worth reading:

**FR-014's exemption is narrower than the spec says.**
`POST /events/webhook/{name}?fabId=` checks the fab against the caller's own
credentials in `BearerValidationMode.Jwt` only. In `StaticHash` — the enum
default, and the mode of every integration until it is rotated — the token hash
is matched and `fabId` is never consulted, because `WebhookIntegration` carries
no fab to compare it against. A token issued for one plant can therefore file an
event against another.

Three things follow, and none of them is "fix it here":

1. **It is pre-existing.** FR-014 held this endpoint unchanged and FR-016
   deferred the registry question; closing it means giving `WebhookIntegration`
   a fab, which *is* the deferred question. It is now recorded on #1545 with
   the security framing, and that issue cannot be answered without it.
2. **This feature made it reachable.** `events` had no partition for any fab
   but munich, so a cross-fab webhook write previously failed at the insert
   with `23514`. Adding `events_dresden` — which had to happen, it was a live
   defect — removed an accidental backstop that was never an authorization
   control. Said plainly rather than left for someone to discover.
3. **The claim is corrected in the code.** `ResolveWriteFabAsync`'s doc comment
   asserted the webhook "already checks the fab against them". It now names the
   `Jwt`-only scope, so nothing in the codebase reads as though this is covered.

Also filed: **#1546** (the persistence loop drops an envelope it has already
`202`-accepted when the dispatch throws) and **#1547** (adding a fab needs a
hand-written partition migration and nothing enforces it — the two compound
into "accepted, then silently gone"). Both are on this branch's diff and both
are trade-offs made deliberately in `ed33dc4`; neither is closed here.

Fixed in place: the manual write's `CancellationToken` is last again,
`GET /events/{id}` declares the 400 it can now return, and the inference test
reads its event back out of dresden — it asserted only `202`, which the
inferred branch returns whether it inferred dresden or fell back to the munich
default, so the regression it exists to catch could not have failed it.

## Not verified

- The **partition** path for a second fab under load. `events_dresden` was
  added on this branch (`505b226`) because Dresden had no partition at all and
  every prior test named munich; it is exercised by the manual-write tests, not
  by a throughput run.
- The backfill on a **real** populated database. Nine synthetic rows covering
  each shape is the closest the fixture allows.
