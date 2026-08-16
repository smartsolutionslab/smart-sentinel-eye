# Data Model: Fab-scope layout composition

**Feature**: `017-layout-fab-scoping` | **Date**: 2026-08-16

## Layout

| Field | Change |
|---|---|
| `Fab` | **NEW.** `FabIdentifier`, **NOT NULL** after the migration, required by `CreateDraft`, no setter. |
| `Name` | Unchanged. **Not** made unique per fab — see below. |
| `Revisions`, `CreatedAt`, `CreatedBy` | Unchanged. |

`FabIdentifier` is this context's own copy per ADR-0044 — the **seventh**,
alongside Identity, EventIngestion, Automation, SystemVariables, CameraCatalog
and StreamDistribution. The grammar must match all six; a test asserts it,
because nothing else does.

**No `MoveToFab`, and no setter.** A layout's fab is fixed at creation
(FR-002). Unlike spec 016 there is no transitional nullable phase, so the
aggregate never needs a one-way fill — the column is NOT NULL by the end of the
one migration that adds it.

### Why NOT NULL here, when spec 016's was nullable

Spec 016's column stayed nullable because cameras lived in **another
database**, so no migration could derive a stream's fab. Layouts are in this
context's own database and can simply be attributed
([research.md](./research.md) §3). Nullable-then-tightened in one migration is
the spec 013–015 pattern, and it applies cleanly.

### The name uniqueness check must become fab-scoped

**Corrected after reading `CreateLayoutDraftCommandHandler`.** A layout name
*is* unique today — but the rule lives in the **handler**, not the database:

```csharp
Option<Layout> existing = await layouts.GetByNameAsync(name, cancellationToken);
if (existing.HasValue) return Failure(CreateLayoutDraftFailures.LayoutNameTaken(name.Value));
```

`ix_layouts_name` is a plain index, not unique. So the check is global and
invisible in the schema — which is exactly how it would have been missed by
drafting against the migration alone.

Left global, it breaks fab isolation in **two** ways:

1. Dresden cannot create a layout named "Main Wall" if Munich already has one.
2. Worse, the refusal *tells* them Munich has one. `409 LAYOUT_NAME_TAKEN` on a
   name they cannot see is an enumeration oracle — the same leak FR-006 closes
   on the read path, reopened on the write path.

So `GetByNameAsync` becomes fab-scoped (FR-019), and `ix_layouts_name` becomes
`(fab, name)`. That matches what rules, variables and cameras each did.

**Not made a unique *database* index.** The existing constraint is
application-level, and promoting it is a behaviour change on data that may
already violate it — a separate decision from fab-scoping. The index stays
plain, widened to `(fab, name)` so the scoped lookup is indexed.

## Revision

**Unchanged.** A revision has no fab of its own (FR-003): it belongs to its
layout's fab, so the two cannot differ. The guarantee is structural — there is
nowhere to put a second value.

## Tile

**Unchanged in shape.** It already carries `camera_id` and a nullable
`overlay_id`. Both are load-bearing here and neither moves:

- `camera_id` is what FR-014 validates against the layout's fab.
- `overlay_id` is what Half B joins on.

**No fab on the tile.** It would be derivable from the layout and therefore a
second place for the answer to live.

## Column, index and migration

```sql
-- 1. add nullable
ALTER TABLE layouts ADD COLUMN fab VARCHAR(32);

-- 2. backfill, warning rather than failing (spec 015 precedent)
DO $$
DECLARE attributed integer;
BEGIN
    UPDATE layouts SET fab = 'munich' WHERE fab IS NULL;
    GET DIAGNOSTICS attributed = ROW_COUNT;
    IF attributed > 0 THEN
        RAISE WARNING
            'FabScopeLayouts attributed % pre-existing layout(s) to fab ''munich''. If this database belongs to another fab, those layouts are now invisible to every operator of it.',
            attributed;
    END IF;
END $$;

-- 3. now the constraint can hold
ALTER TABLE layouts ALTER COLUMN fab SET NOT NULL;

-- 4. the listing filter
CREATE INDEX ix_layouts_fab ON layouts (fab);

-- 5. widen the name lookup to the fab it is now scoped by (FR-019)
DROP INDEX ix_layouts_name;
CREATE INDEX ix_layouts_fab_name ON layouts (fab, name);
```

**Both plain, neither unique.** The name constraint is enforced in the handler
today and this feature scopes it rather than promoting it to the database —
promoting it is a behaviour change on data that may already violate it.

**`Down`** drops the index and the column. Safe: nothing outside this column
depends on it.

**FR-018 — pre-existing tiles are not retro-validated.** After step 2 guesses
`munich`, a pre-existing layout may hold a tile whose camera is in another fab
— a state FR-014 forbids going forward. The migration does **not** check for
it. The mismatch is the migration's own guess, not an operator's choice, and
failing over it would block the deployment the migration exists to fix.

## The Half B query (FR-010, FR-011, FR-013)

Which fabs are told about an overlay:

```sql
SELECT DISTINCT l.fab
FROM   layout_revision_tiles t
JOIN   layout_revisions r ON r.revision_id = t.revision_id
JOIN   layouts          l ON l.layout_id   = r.layout_id
WHERE  t.overlay_id = @overlay
  AND  r.state = 'Published';
```

Three things this shape guarantees, each mapping to a requirement:

- **FR-011** falls out: an overlay nothing references returns no rows, so the
  frame is addressed to nobody. No special case.
- **FR-013** is the `r.state = 'Published'` term, and the only place it lives.
  A draft's tiles do not count.
- **FR-012** holds because nothing is stored. The answer is recomputed per
  frame, so archiving the last referencing layout narrows the audience with
  nothing to invalidate.

Cost: one indexed query per overlay publish or archive — an authoring action,
not a per-event push.

**Supporting index**: `layout_revision_tiles(overlay_id)`. The column exists
but is unindexed today, because nothing has ever queried by it.

## The FR-014 seam

`ICameraFabGuard` in `Application/Tiles/`, implemented by
`CameraCatalogFabGuard` in `Infrastructure/Cameras/`:

> Given a fab and the camera identifiers of a tile set, answer which of them
> are **not** cameras in that fab.

Returning the offending identifiers rather than a boolean is deliberate: US5
scenario 1 requires the refusal to name the tile, and a boolean cannot.

An identifier that resolves to no camera at all comes back in the same list
(FR-015) — refused for the same reason and by the same path, so there is no
branch where "unknown" is treated more leniently than "other fab".

## Events

**Corrected during implementation.** This section previously claimed
LayoutComposition publishes no integration event carrying a layout. It does:
`LayoutRevisionPublishedV2` and the archived V1, both stamped
`EventMetadata(..., Fab: null, ...)`.

**The domain events do gain a fab**, because they must: the SignalR frames are
built from them and the handler never sees the aggregate, so
`LayoutRevisionPublishedDomainEvent` and `LayoutRevisionArchivedDomainEvent`
carry `Fab` and the aggregate stamps it when raising.

**The integration events deliberately do not, yet.** Populating
`EventMetadata.Fab` on them would give AuditObservability a fab on these audit
rows — a real improvement, and nearly free now the value is in hand — but it
is outside this spec's requirements and no consumer asks for it. Left as a
follow-up rather than a drive-by, and recorded here so the null is a decision
rather than an oversight.

## Notification records (the four frames)

Not persisted, but the shape change belongs with the model:

| Record | Change |
|---|---|
| `LayoutRevisionPublishedNotification` | `+ Fab` |
| `LayoutRevisionArchivedNotification` | `+ Fab` |
| `OverlayLifecyclePublishedNotification` | `+ Fabs` (a set) |
| `OverlayLifecycleArchivedNotification` | `+ Fabs` (a set) |

Singular for a layout frame, plural for an overlay frame, and the difference is
the whole of Half B: a layout belongs to one fab, an overlay is used by
however many.

The Application layer fills these in; the broadcaster stays a mapper
([research.md](./research.md) §2).
