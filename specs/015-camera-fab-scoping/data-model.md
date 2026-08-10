# Data Model: Fab-scope the camera catalogue

**Feature**: `015-camera-fab-scoping` | **Date**: 2026-08-10

## Camera

| Field | Change |
|---|---|
| `Fab` | **NEW.** `FabIdentifier`, private setter, required at registration, never mutated (FR-004). |
| `Name` | Unchanged in type. Unique only within `Fab` now (FR-002). |
| `Url`, `Status`, `RegisteredAt`, `RegisteredBy` | Unchanged. |

`FabIdentifier` is CameraCatalog's own copy per ADR-0044 — the fourth. The
grammar must match Identity's, EventIngestion's, Automation's and
SystemVariables' exactly: 2–32 chars, lowercase letters/digits/`-`, starting
with a letter. Nothing keeps the copies in step but a test that asserts it.

No `MoveToFab`. A camera is bolted to a wall in one building; relocating the
device means registering it afresh.

## Column and index

`fab VARCHAR(32) NOT NULL` on `cameras`.

`ux_cameras_name_lower` is replaced by `ux_cameras_fab_name_active` — UNIQUE on
`(fab, name)` `WHERE status <> 'Decommissioned'`.

**The partial filter is new behaviour, not a carry-over.** The shipped index has
none, so a decommissioned camera holds its name forever today; rules and
variables both release theirs. Adopting the filter was decided at the Phase 2
gate ([research.md](./research.md) §3) and is what makes FR-003 true.

It is safe against existing data by construction: a partial unique index is
strictly **weaker** than the unfiltered one it replaces, so nothing already
stored can violate it. The forward migration cannot fail here.

**Correction found during implementation.** The shipped index is *named*
`ux_cameras_name_lower` but is a plain btree on `name` — there is no `lower()`
expression, and EF's `HasIndex` cannot express one. Case-insensitivity is
enforced in the **domain**, by `CameraName.NormalizedValue`, which is what
equality and comparison use. The index name is misleading and predates this
feature.

That does not change what T015 must assert — registering `Line-1-North` where
`line-1-north` exists in the same fab must still be refused — only *where* the
guarantee lives. It is
exactly the property a hand-corrected migration drops without anyone noticing,
so it gets a test that registers `Line-1-North` against an existing
`line-1-north`.

## Migration — four steps, not one

`dotnet ef` will generate a single
`AddColumn(nullable: false, defaultValue: "")`. On a populated table that sets
every camera's fab to the empty string, which is not a valid `FabIdentifier`
(minimum length 2, must start with a letter) — so every existing row would fail
to materialise on the next read. Spec 014's T043 walk observed this failure
mode directly; it is not hypothetical.

Hand-correct to:

1. add `fab` **nullable**, so it can be added to a populated table;
2. backfill to `munich`, capturing `ROW_COUNT` and raising a warning naming the
   count (FR-011);
3. `ALTER ... SET NOT NULL` — the constraint can now hold;
4. drop `ux_cameras_name_lower`, create the new index.

The fab literal is not configuration: a migration must produce the same result
everywhere, and a config-driven backfill would assign different fabs in dev and
prod.

The warning reaches the MigrationRunner log only because #1395 wired the Npgsql
notice handler. Before that it went nowhere.

**`Down`** drops the column and restores `ux_cameras_name_lower`. It discards
each camera's fab, and rolling forward re-attributes everything to munich —
unrecoverable from inside the database, so a rollback after cameras exist across
fabs wants a dump first. `Down` can also legitimately fail where `Up` succeeded:
two fabs may each hold a live camera of one name, which the name-only index
cannot represent. That is correct — the data genuinely does not fit the old
shape.

## Events

Every camera lifecycle event stamps `EventMetadata.Fab` with the camera's fab
(FR-012). Additive — the field exists and is currently `null`, so no version
bump under ADR-0073. See [research.md](./research.md) §2.
