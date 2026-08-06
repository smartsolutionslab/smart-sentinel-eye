# ADR-0067: Database Migrations — Dedicated MigrationRunner Worker

**Status:** Accepted
**Date:** 2026-05-25

## Context

Multiple Api services starting in parallel against a shared Postgres
would race to run migrations. Yumney solves this with a dedicated
worker project that runs migrations once before any Api service
starts.

## Decision

Add **`SmartSentinelEye.MigrationRunner`** — a
`Microsoft.Extensions.Hosting` worker project that:

- Boots a minimal host with access to every context's `IMigrator`
  (per-context migrator interface in `<Context>.Infrastructure`).
- Runs migrators sequentially in a defined order.
- Logs each applied migration (and skips already-applied ones).
- Exits with status 0 on success, non-zero on failure.

```csharp
// Aspire AppHost
builder.AddProject<Projects.SmartSentinelEye_MigrationRunner>("migrations")
    .WaitFor(postgres);

builder.AddProject<Projects.SmartSentinelEye_CameraCatalog_Api>("camera-catalog")
    .WaitFor("migrations");
// ... every other Api service waits on "migrations"
```

- **Api services never run migrations themselves.**
- Production deployment (k3s) runs `MigrationRunner` as an `initContainer`
  or a separate Helm hook before any Api Deployment.

## Consequences

- **Positive:** no startup race; migrations run exactly once.
- **Positive:** Api startup is faster (no migration check on the hot
  path).
- **Positive:** single audit point for schema changes.
- **Negative:** an extra project to maintain. Small.

## Alternatives Considered

- **Migrate on Api startup** — race conditions when multiple services
  share Postgres.
- **Manual `dotnet ef database update`** — operational burden, easy
  to forget.

## Addendum (2026-08-06) — dev no longer gates on the runner

The blanket "every other Api service waits on `migrations`" above now
holds only under **E2E** (`E2ETests=true`). In dev the services start in
parallel with the runner.

**Why.** Dev reuses a persistent, already-migrated data volume, so the
gate costs startup time and buys nothing. E2E drops the persistent
lifetime and the data volume, so every run starts from an empty
database — and each service builds its Wolverine outbox storage during
startup (`AutoBuildMessageStorageOnStartup = CreateOrUpdate`), which is
real DDL against a schema the runner may not have created yet.

**Consequence worth stating plainly.** `WaitForCompletion(migrations)`
was also the services' *only* ordering against the data tier: the
runner waits for all nine databases, `WithReference` injects a
connection string but waits for nothing. In dev the services now have
no ordering against Postgres at all — they are protected only by the
persistent container already being up. A cold start with no Postgres
running would have nine services racing a database that is not
listening. Accepted as a dev-only ergonomics trade.

Production is unaffected: k3s still runs `MigrationRunner` as an
initContainer / Helm hook before any Api Deployment, which is a
stronger guarantee than the AppHost gate ever was.
