# Phase 0 Research: Properties that travel together

**Feature**: 058 | **Date**: 2026-09-02 | **Spec**: [spec.md](./spec.md)

Two unknowns carried the feature's only real risk, and both are about
persistence rather than design. FR-004 forbids a schema change, and a composite
that silently moves a column or makes it nullable would breach that **without
failing a single test** — the same shape as issue #2022, where `version` became
nullable in the model and nobody noticed until a migration was generated.

Both were settled by building the model and reading it, not by reasoning.
Neither needed a database.

---

## R1 — Does an owned reference map onto the existing columns, unchanged?

**Decision**: Yes, with one line that must not be omitted. Each composite is an
owned reference with explicit column names on its components, plus
`Navigation(...).IsRequired()` on the owner.

**Why this was in doubt**: An owned reference is EF's table-splitting feature,
and its defaults are all wrong for this feature. Left alone it names columns
`Creation_At`, not `created_at`; it treats the navigation as optional, which
makes both columns nullable against a `NOT NULL` schema; and two of the nine
sites are inside an owned *collection* (`Revision`), so the composite nests one
level deeper than anything the codebase does today.

**Experiment**: A scratch model mirroring the real shapes — a record-class
timestamp value object, a **struct** actor identifier, grouped into a composite,
placed both on a plain aggregate and inside an owned collection. The relational
model was then read directly.

```text
--- ACTUAL TABLE COLUMNS (relational model) ---
  revisions: revision_id, created_at, created_by, root_id
  roots:     root_id, created_at, created_by
```

Exactly the columns the loose properties occupy today, in the same tables,
non-nullable, at both nesting depths. Value converters work on the composite's
components, including the **struct** one — `OperatorIdentifier` is a
`readonly record struct`, and `Tile`'s comment that "EF cannot own a struct
value object" is about owning a struct *as* the owned type, not about a struct
property inside one.

**The one load-bearing line**: `root.Navigation(e => e.Creation).IsRequired()`.
Without it the owned reference is optional and both columns become nullable —
a model/schema divergence that compiles, passes every unit test, and only
surfaces when someone generates an unrelated migration. That is issue #2022
exactly, and this feature would be creating nine more of it.

**A false alarm worth recording.** The first reading of this experiment
reported an `RootId` column that does not exist:

```text
owned nav 'Creation' required=True
    RootId       col=RootId       null=False table=roots
```

That came from `IProperty.GetColumnName()`, the overload with no table context,
which is meaningless for a table-splitting owned type. The relational model
(`GetRelationalModel().Tables`) shows the truth: no such column. **Use the
relational model, not property metadata, to answer "what columns exist".** Had
the first reading been believed, this feature would have been redesigned around
a problem it does not have.

**Alternatives rejected**:

- *A complex type instead of an owned reference.* Complex types map to the same
  table and would suit, but EF does not support them everywhere an owned
  reference works, and this codebase already uses owned references for exactly
  this (`Revision.Grid` → `GridDimensions` across `grid_rows`/`grid_cols`).
  Introducing a second mechanism for one feature is not worth it.
- *Keeping the loose properties and adding a computed composite over them.*
  Rejected: it doubles the surface instead of replacing it, and leaves the two
  fields independently settable, which is the defect.

---

## R2 — Do queries still translate when they filter on a composite's component?

**Decision**: Yes. Filters and ordering on a composite component translate to
the same SQL as the loose property.

**Why this was in doubt**: The audit context queries by actor and orders by
timestamp — `ix_audit_actor_occurred` exists for exactly that — and its read
path projects directly against the `DbContext` rather than loading aggregates.
If access through an owned reference had fallen back to client evaluation, the
index would have stopped being used and a hot path would have quietly become a
table scan. Nothing in a unit test would show it.

**Experiment**: The same scratch model, filtered on the composite's actor
component and ordered by its timestamp component.

```sql
SELECT r.root_id
FROM roots AS r
WHERE r.created_by = @p
ORDER BY r.created_at DESC
```

Server-side, against the same columns, so the same indexes apply. No client
evaluation, no join — the owned reference shares the owner's table.

**Consequence for the design**: read paths change only in how they *name* the
property (`audit.Actor` → `audit.Actor.Identifier`). No query needs
restructuring, and no index needs revisiting.

---

## What was deliberately not researched

- **Whether the composites are the right shape.** Settled before the spec:
  per-context named types, no shared or generic composite (FR-002, FR-003).
  Re-opening it in Phase 0 would be re-litigating a decision, not resolving an
  unknown.
- **The audit context's hand-written write path.** It reads properties and
  writes columns; renaming what it reads is mechanical. There is no unknown
  here, only work — and the spec already records it as the larger slice.
- **Whether `PublishedAt`/`ArchivedAt` should gain an actor.** Out of scope by
  FR-010, and a schema change besides.
