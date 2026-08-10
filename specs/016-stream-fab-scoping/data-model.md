# Data Model: Fab-scope stream distribution

**Feature**: `016-stream-fab-scoping` | **Date**: 2026-08-10

## Stream

| Field | Change |
|---|---|
| `Fab` | **NEW.** `FabIdentifier`, **nullable** in the model, required by `Provision`, never settable alone. |
| `Camera` | Unchanged. It is the source of `Fab`. |
| `Path`, `SourceUrl`, `State`, `TranscodeMode`, `LastSuccessAt`, `LastError`, `ProvisionedAt`, `ProvisionedBy` | Unchanged. |

`FabIdentifier` is this context's own copy per ADR-0044 — the sixth, alongside
Identity, EventIngestion, Automation, SystemVariables and CameraCatalog. The
grammar must match all five; a test asserts it, because nothing else does.

**No `MoveToFab`, and no setter at all.** A stream's fab is its camera's, and a
camera cannot move between fabs (spec 015 FR-004), so the value can never
legitimately change after provisioning. FR-002 says the two must not be able to
differ; the way to guarantee that is to give the aggregate no way to express it.

### Why nullable, when every sibling made it NOT NULL

The other three fab-scoped contexts backfilled in SQL and tightened in the same
migration. This one cannot: **cameras and streams are in separate databases**,
so no migration here can learn a stream's fab
([research.md](./research.md) §5).

The choices were a `munich` guess or a real derivation. A guess is wrong
precisely for the deployment this feature is for — one with more than one fab —
so the column stays nullable until the value is known.

`Fab` is therefore `FabIdentifier?` on the aggregate. That is deliberate and
temporary: a follow-up migration tightens it to NOT NULL once no unattributed
rows remain, and that migration is **not** part of this feature, because it
cannot be written safely until the backfill has demonstrably run everywhere.

## Column and index

`fab VARCHAR(32) NULL` on `streams`.

**No unique index.** Unlike rules, variables and cameras, a stream has no name
to make unique — it is keyed by its camera, which is already globally unique.
Adding `(fab, camera_id)` would be redundant.

A plain index on `fab` supports the listing filter, which is the only query the
column participates in.

## Migration — one step, and no backfill

```
ALTER TABLE streams ADD COLUMN fab VARCHAR(32);
CREATE INDEX ix_streams_fab ON streams (fab);
```

That is the whole of it. **No `DO $$` block, no announced count, no `UPDATE`** —
there is nothing in this database to derive from, and this feature refuses to
guess.

This is the first fab migration in the product with no backfill, and the
absence is the interesting part rather than an omission. The count that specs
013–015 raise from SQL is raised here by the attribution service instead
(FR-008), where it can report both what it filled *and* what it could not
(FR-010) — something a SQL `UPDATE` cannot express.

**`Down`** drops the index and the column. It is safe: no data outside this
column depends on it, and a stream without a fab is exactly the state the
system is designed to tolerate.

**Not in this feature**: the follow-up migration to `SET NOT NULL`. It needs
every deployment to have completed attribution first, which cannot be asserted
from inside a migration.

## Attribution (FR-008, FR-009, FR-010)

A hosted service, separate from `MediaMtxReconciler`
([research.md](./research.md) §1), runs once at startup:

1. Select streams where `fab IS NULL`. If none, do nothing and log nothing —
   the steady state must be silent.
2. For each, ask CameraCatalog for that camera's fab over HTTP.
3. Set it, or leave it null if the camera cannot be resolved (FR-010).
4. Log the count attributed **and** the count unresolved. Both, always — an
   operator seeing an empty listing needs to distinguish "not yet run" from
   "ran and could not resolve".

**Reads treat `fab IS NULL` as visible to nobody** (FR-009), not as a wildcard.
The filter is `fab IN (caller's fabs)`, and NULL satisfies no `IN` clause — so
failing closed is the natural behaviour of the query rather than a special case
someone must remember to write. That is worth preferring on its own.

## Events

Not in scope. `StreamDistribution` publishes stream lifecycle events, but no
consumer needs the fab yet and adding it speculatively is exactly the
symmetry-driven reflex [research.md](./research.md) §4 rejects. When a consumer
needs it, `EventMetadata.Fab` is already there to carry it.
