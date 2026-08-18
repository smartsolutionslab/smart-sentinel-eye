# Verification: A plant that exists can store its events

**T026** — [quickstart.md](./quickstart.md) walked. "Done" is the observations,
so they are here rather than a tick.

Observed on 2026-08-18 against the real Aspire stack, and — where the fixture
cannot reach — against a real Keycloak and a real TimescaleDB started for the
purpose.

## 0. The loss, before anything changed

`/fabs/berlin` added to the realm with an operator in it, and no partition
anywhere. As `op-berlin@berlin.test`:

```
existing partitions: events_dresden, events_munich          (no berlin)
POST /events/manual                        -> 202 Accepted
GET  /events?kind=Baseline…                -> 200, 0 items
rows in the database for fab 'berlin'      -> 0
log: Ingest dispatch faulted for 01a014bc-… in fab berlin;
     the envelope is dropped and the loop continues.
```

Accepted, acknowledged, never stored, and a log line that does not say why.
That is the whole defect in four lines, and every assertion below is measured
against it.

## 1. Provisioning follows the realm (SC-001, SC-002)

Verified twice: inside the fixture by `FabPartitionProvisioningIntegrationTests`,
and directly against a standalone Keycloak + TimescaleDB, which is the run worth
quoting because it shows the whole tree:

```
Ensured event storage events_berlin  for fab berlin.
Ensured event storage events_dresden for fab dresden.
Ensured event storage events_hamburg for fab hamburg.
Ensured event storage events_munich  for fab munich.
All migrations applied; MigrationRunner exiting.        EXIT=0
```

```
events_berlin
events_berlin_202608     ← this month
events_berlin_202609     ← next month
events_dresden
events_hamburg
events_munich
```

**`berlin` and `hamburg` appear in no migration anywhere.** They exist because
they are groups in the realm. And they have their months in the *same pass*,
which is the ordering trap the task list called out: provisioning after the
rollover would leave a new fab with a partition and nothing beneath it, storing
exactly as little as before.

Re-running changes nothing, and the event that was lost in step 0 now lands and
reads back.

## 2. Nothing accepted that cannot be stored (SC-003, SC-004)

`FabStorageRefusalIntegrationTests`, with `events_hamburg` dropped to recreate
the state the feature is about:

| As | Request | Observed |
|---|---|---|
| `op-hamburg` | `POST /events/manual` | **503** `EVENT_FAB_NOT_PROVISIONED` |
| a machine | `POST /events/webhook/{name}?fabId=hamburg` | **503**, same code |
| `op-3@munich` | `POST /events/manual` | **202**, untouched |

And the assertion that matters: **zero rows for hamburg afterwards**. A 503 that
had already written to the channel would be the same defect with a better error
message.

## 3. Removing a plant destroys nothing (SC-006)

A fab group cannot be deleted at test time — the migration job's credential can
only read — so removal is reproduced where it lands: the fab is simply absent
from the list handed to the provisioner, which is exactly what deleting its
group produces. Provisioning for `munich` alone leaves `events_dresden` and
every row in it untouched.

The unit test is the stronger one, though: it asserts on the **statement
issued**, because an outcome test passes trivially for a fab that had nothing to
lose.

## 4. When the realm cannot be reached (FR-011)

The requirement whose regression would be invisible. Keycloak absent, migration
job run:

```
EXIT != 0
"All migrations applied"  -> never printed
"no fabs"                 -> 0 occurrences
System.Net.Http.HttpRequestException: No connection could be made because the
target machine actively refused it. (localhost:59999)
Execution attempt … Standard-Retry … Attempt: '0'
```

It fails, it names the realm, it retries first, and it never once claims the
realm is empty. "Cannot tell" and "there are none" stay distinguishable, which
is the entire point.

## 5. Ingest is untouched (SC-007)

**38/38 EventIngestion integration cases pass**, including every webhook and
broker path spec 018 left in place. The readiness check resolves to an in-memory
set lookup on the hot path; the catalog is read once per TTL, not per request.

Coverage: `EventIngestion.Domain` **96.0%** (gate ≥ 90%), `Application`
**83.4%** (gate ≥ 80%). All twenty gates pass.

**One result in this walk was worthless and is worth saying so.** An earlier run
of the same suite reported 35 of 38 failing with
`Polly.Timeout.TimeoutRejectedException` — because the coverage gate was running
concurrently and starved the stack, not because anything was broken. Re-run with
the machine to itself: 38/38 in 2 m 47 s. Two heavy runs at once produce
evidence about the scheduler, not the code.

## What went wrong on the way, and what it says

Three defects, all mine, none in the design — and every one of them was caught
by something failing closed rather than proceeding quietly.

1. **A 288-character client description** killed the realm import
   (`VARCHAR(255)`), so Keycloak exited, the migration job waited forever, and
   the fixture reported every test failing in ~1 ms. There is a memory note
   about exactly this, written two days earlier, which I did not apply.
2. **`TimeProvider` is not registered in EventIngestion.** The readiness
   singleton could not be constructed and `event-ingestion` alone failed to
   start. It now uses the repo's own `IClock`, and is registered in the
   infrastructure module rather than the persistence module that MigrationRunner
   also composes.
3. **A scoped service resolved from the root provider.** The new migrator and
   fab source were registered `Scoped`, and `Program.cs` resolved `IMigrator`
   from the root — which throws under Development scope validation. The job
   exited non-zero and **all nine services** reported `FailedToStart`, because
   every one of them does `WaitForCompletion(migrations)`. Migrators are now
   resolved from a scope.

The third is worth keeping in mind when reading this feature: a failing
migration job stops the entire system from starting. That is the cost of
FR-011's fail-closed choice, paid three times in one afternoon, and it is
exactly what the feature buys — the alternative is a stack that starts happily
and loses a plant's events.

A fourth was caught by a test rather than a run: the readiness cache returned a
**stale negative** inside its TTL, because a miss re-checked "is the snapshot
young?" instead of "did someone else refresh while I queued?". A fab provisioned
a minute earlier would have stayed refused for the rest of the window — the one
behaviour the contract explicitly forbids.

## Corrected during implementation

**`query-groups` is not sufficient.** The plan specified a credential holding
`query-groups` and nothing else. `GET /admin/realms/{realm}/group-by-path/fabs`
answers **403** with it, verified against Keycloak 26.5. The working minimum is
`query-groups` + `view-users`, which is what the realm grants — wider than
intended, still far narrower than `identity-admin`, which can create clients and
users.

**Keycloak 26.5 omits `subGroups`** from that response (`subGroupCount: 4`,
`subGroups: []`), so the second call to `/children` is not defensive padding —
it is the only way to get the names.

## Not verified

- **A fab group removed from a real realm.** The credential cannot write, so
  removal is simulated at the provisioner's input. What a real deletion does to
  Keycloak's group tree is unobserved.
- **Provisioning under concurrency** — two migration jobs racing. Every
  statement is `IF NOT EXISTS`, but two runs have never been started together.
- **The stale-positive window in production.** The readiness cache serves a
  positive for up to its TTL after a partition disappears; the refusal test
  waits that window out rather than eliminating it, because eliminating it would
  mean a catalog read per write.
