# Data Model: A plant that exists can store its events

**Feature**: `019-fab-event-partitions` | **Date**: 2026-08-18

## No schema change, and that is the point

No table gains a column. No entity gains a field. Nothing is migrated.

Every fab feature since spec 013 has ended in an `ALTER TABLE`, so it is worth
saying plainly why this one does not: the fab is already modelled, already
stored, and already indexed. What is missing is not data about a fab — it is
the *storage* for a fab, which in Postgres is a table rather than a row. The
gap this closes lives in the catalog, not in the schema.

That also means there is no backfill, nothing to guess, and no `RAISE WARNING`
to hear. The three preceding features each had one; this one has nothing to
warn about, because it invents no value.

## The partition tree, as it stands

```text
events                              PARTITION BY LIST (fab_id)
├── events_munich                   PARTITION BY RANGE (ingested_at)   ← created by hand (spec 006)
│   ├── events_munich_202608
│   └── events_munich_202609
└── events_dresden                  PARTITION BY RANGE (ingested_at)   ← created by hand (spec 018)
    ├── events_dresden_202608
    └── events_dresden_202609
```

Two levels, two owners today:

| Level | Created by | Covers |
|---|---|---|
| `events_<fab>` | a hand-written migration, once per fab, **remembered or not** | the gap |
| `events_<fab>_<yyyyMM>` | `EventPartitionRolloverMigrator`, automatically | already solved |

The second level was automated in spec 006 and has never lost an event. The
first was left to a human and has lost them twice — once for dresden, and once
for whatever fab is added next. This feature moves the first level to the same
footing as the second.

## What changes in the tree

Nothing structural. After this feature the tree looks identical; only the
*author* of the middle row changes:

| Level | Created by |
|---|---|
| `events_<fab>` | **`FabPartitionProvisioner`, from the realm's `/fabs/*` groups** |
| `events_<fab>_<yyyyMM>` | `EventPartitionRolloverMigrator`, unchanged |

The two run in one pass, in that order, so a fab provisioned at 09:00 has its
current and next month by 09:00 as well. Running provisioning *after* the
rollover would leave a new fab with a partition and no months — able to store
nothing, which is the bug wearing a different hat.

## Naming, and why it is a contract rather than a convention

`events_<fab>` is not decoration. `EventPartitionRolloverMigrator` discovers
fab partitions by asking the catalog for children of `events`, and derives the
monthly child's name by appending to whatever it finds. The provisioner must
produce exactly the shape the discovery expects, or the two halves silently
stop meeting.

The existing hand-written partitions set that shape and cannot be renamed
without moving data, so the provisioner conforms to them:

```sql
CREATE TABLE IF NOT EXISTS events_<fab> PARTITION OF events
    FOR VALUES IN ('<fab>')
    PARTITION BY RANGE (ingested_at);
```

`<fab>` appears twice and means two different things — a table-name fragment
and a literal value. Both come from the same validated `FabIdentifier`.

## Entities, such as they are

- **Fab** — exists in Keycloak as a group under `/fabs/`. Already the authority
  for who may read and write which events (spec 018); this feature gives that
  same list a second consequence. It has no row in any EventIngestion table and
  gains none here.
- **Fab event storage** — the `events_<fab>` partition. Identified by the fab,
  created by provisioning, **never dropped**. Its existence is the precondition
  the readiness check reads.
- **Event** — unchanged in every respect.

## Reads this introduces

Two, both against the Postgres catalog rather than any table of ours:

| Read | Where | Why not cached forever |
|---|---|---|
| Which fab partitions exist | `FabPartitionProvisioner`, once per run | Deploy-time; no cache. |
| Does this fab's partition exist | `CatalogFabStorageReadiness`, per write | Cached with a short TTL and re-read on a miss, so a fab provisioned a minute ago is not refused by a stale answer. |

The second is the only one on a request path. It resolves to a set lookup in
the common case, and the set changes about as often as a plant is built.

## Deliberately not modelled

- **A fabs table in EventIngestion.** It would be a copy of the realm with its
  own staleness, and it would be empty on the first deploy — exactly when the
  partitions are first needed. Rejected in research §R1.
- **A record of provisioning runs.** The catalog already answers "does this
  exist"; a log of attempts would be a second source of truth about the first.
- **Anything about dropping a partition.** FR-006 puts removal out of scope,
  and the model reflects that by having no representation for it at all.
