# Quickstart: Fab-scope the camera catalogue

**Feature**: `015-camera-fab-scoping`

"Done" is the observations, not the walk. Record what you saw on the PR.

## 1. The migration, against a database that predates the feature

A fresh database makes the backfill a no-op **by design**, so it proves nothing.
Build one that predates the change, as spec 014's T043 did:

```sh
docker run -d --name cam-pre -e POSTGRES_PASSWORD=pw -p 55435:5432 \
  timescale/timescaledb:2.27.1-pg17
docker exec -e PGPASSWORD=pw cam-pre psql -U postgres \
  -c 'CREATE DATABASE "camera-catalog-db";'

CONN="Host=localhost;Port=55435;Database=camera-catalog-db;Username=postgres;Password=pw"
dotnet ef database update <the-migration-before-this-one> \
  --project src/CameraCatalog/Infrastructure --startup-project src/MigrationRunner \
  --context CameraCatalogDbContext --connection "$CONN"
```

Seed a handful of cameras, including one Decommissioned. Then run the real
`MigrationRunner` against it and **read the log**:

```
warn: ...PostgresNoticeLoggingInterceptor
      PostgreSQL: FabScopeCameras attributed N pre-existing camera(s) to fab munich. ...
```

Record N. Then confirm no row took an invalid fab — the check that vindicates
the four-step form:

```sql
SELECT count(*) FROM cameras WHERE fab IS NULL OR length(fab) < 2;  -- expect 0
```

## 2. Case-insensitivity survived the index swap

Against the migrated database, register `Line-1-North` where `line-1-north`
already exists in that fab. It must be refused. Spec 001 made this deliberate,
and a hand-corrected migration is exactly where it gets dropped.

## 3. The decision table over real HTTP

With the stack up, using the seeded accounts:

| As | Do | Expect |
|---|---|---|
| `operator` (munich only) | `POST /cameras` with no `fabId` | created in **munich**, inferred |
| `op-dresden@dresden.test` | `POST /cameras` with no `fabId` | created in **dresden** — not the default |
| `op-multi@smart-sentinel-eye.test` | `POST /cameras` with no `fabId` | **400** `CAMERA_FAB_REQUIRED` |
| `op-multi` | `POST /cameras?fabId=dresden` | created in dresden |
| `op-dresden` | `POST /cameras?fabId=munich` | **403** |
| `op-dresden` | `GET /cameras/<a munich camera>` | **404**, byte-identical to a name never used |
| `op-multi` | `GET /cameras/<name held in both>` | **400** `CAMERA_FAB_AMBIGUOUS`, naming both |

Assert the dresden inference explicitly. Everything else in the system defaults
to munich, so a broken inference that fell back to the default passes against a
munich operator and only fails here.

## 4. The management UI

As a single-fab operator: register a camera — **no fab selector appears**. The
row shows its fab.

As `op-multi`: the selector appears, and registering without choosing is refused
before the request is sent.

## 5. Downstream sees the fab

Register a camera and confirm the published event carries `Metadata.Fab`. This
is what StreamDistribution's own scoping will read; if it is null, that feature
starts by fixing this one.
