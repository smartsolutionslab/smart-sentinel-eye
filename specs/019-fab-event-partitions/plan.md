# Implementation Plan: A plant that exists can store its events

**Branch**: `019-fab-event-partitions` | **Date**: 2026-08-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/019-fab-event-partitions/spec.md`

## Summary

`events` is `PARTITION BY LIST (fab_id)` and a fab with no `events_<fab>`
partition can store nothing. Partitions are created only by hand-written
migrations, so "a fab exists" and "a fab can store events" are two facts with
nothing connecting them — and since spec 018 made the fab come from the
caller's groups, adding an operator to a new fab group is enough to start
losing that fab's events silently.

The fix has two halves, matching the spec's two P1 stories:

1. **Provisioning follows the realm.** MigrationRunner reads the `/fabs/*`
   groups through Identity's Keycloak admin client, and ensures
   `events_<fab>` exists for each before the existing monthly rollover runs.
   The rollover then discovers the new partition and creates its months in the
   same pass. EventIngestion declares the port; MigrationRunner supplies the
   adapter, because no bounded context may reference another.

2. **Nothing is accepted that cannot be stored.** Both write paths ask whether
   storage exists for the resolved fab *before* enqueuing, and refuse with
   **503** if it does not — the same before-the-channel ordering spec 018's
   FR-007 imposed on the authorization check. The persistence loop additionally
   logs `23514` distinguishably, for the race the check cannot close.

Full reasoning for every choice: [research.md](./research.md).

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: EF Core 10 + Npgsql (DDL and the catalog query),
Identity's `IKeycloakAdminClient` (composed in MigrationRunner only), ASP.NET
Core Minimal APIs

**Storage**: PostgreSQL — `events`, list-partitioned by `fab_id`, each fab
partition itself range-partitioned by `ingested_at`

**Testing**: xUnit + Shouldly; hand-written fakes for the ports; integration
against the Aspire fixture (ADR-0103); NetArchTest for the boundary

**Target Platform**: Linux containers — Aspire in dev, k3s + Helm in prod

**Project Type**: Backend services in a modular monolith of nine bounded
contexts plus a one-shot migration worker

**Performance Goals**: No measurable cost on the ingest path. The readiness
check is an in-memory set lookup on the hot path; the catalog read happens on
cache miss or refresh, not per request.

**Constraints**: MigrationRunner runs to completion before any Api starts and
nightly in prod; every step must be idempotent and safe to run concurrently
with itself. No partition is ever dropped.

**Scale/Scope**: Single-digit fabs per environment, 250 cameras, ~1k events/s
peak ingest. Partition count grows by one per fab plus two per fab per month.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. On-prem first** | No change. No new external dependency; Keycloak is already in every deployment. |
| **II. DDD with value objects** | **Strengthened.** Fab names crossing from Keycloak into DDL are parsed to `FabIdentifier` at the boundary and only the value object is used. No primitive fab crosses a domain boundary. |
| **III. Bounded context isolation** | **The gate that shaped the design.** `AllowedCrossContext` is empty and must stay empty. EventIngestion declares `IProvisionedFabSource`; the Keycloak-backed implementation lives in MigrationRunner, which is not a bounded context and already references all nine. NetArchTest sees no new cross-context reference. |
| **IV. Latency budget** | **Not on the path.** Provisioning is deploy-time. The readiness check is on the operator-driven write, which is control plane; the broker ingest path gains a set lookup and no I/O. |
| **V. Spec-driven development** | This plan is phase 2 of the loop; spec and checklist are complete with no open clarifications. |
| **VI. Aspire is the composition root** | AppHost gains `.WaitFor(keycloak)` on the migrations resource and passes the new client's credentials. No connection wiring by hand. |
| **VII. Observability** | Every provisioning action, every skipped name, and every `23514` refusal is logged through `[LoggerMessage]` source-gen. The skip and the refusal are the two things that must never be silent. |
| **VIII. Safe by default at trust boundaries** | Group names are validated against the `FabIdentifier` grammar before reaching DDL — an allow-list, replacing the provenance argument that this feature invalidates (research §R3). The new credential holds `query-groups` and nothing else. |
| **IX. Forward-compatible interfaces** | `IProvisionedFabSource` is exactly such a seam: if the fab registry ever stops being Keycloak groups, only MigrationRunner's adapter changes. |

**Result: PASS.** No violations, so Complexity Tracking is omitted.

**Re-check after Phase 1 design: PASS, unchanged.** The design added two ports
and one adapter; neither introduces a cross-context reference, and the
architecture test that would catch one is extended rather than exempted.

## Project Structure

### Documentation (this feature)

```text
specs/019-fab-event-partitions/
├── plan.md              # This file
├── research.md          # Phase 0 — the six decisions and what they cost
├── data-model.md        # Phase 1 — no schema change; the partition tree
├── quickstart.md        # Phase 1 — how to observe it, including the case that cannot be faked
├── contracts/
│   └── provisioning.md  # Phase 1 — the two ports and the refused write
├── checklists/
│   └── requirements.md  # Phase 1 output of /speckit-specify
└── tasks.md             # Phase 3 — NOT created by this command
```

### Source Code (repository root)

```text
src/
  EventIngestion/
    Application/
      Ingress/
        IProvisionedFabSource.cs        # NEW — port: the fabs that exist
        IFabStorageReadiness.cs         # NEW — port: can this fab store yet
    Infrastructure/
      Persistence/
        EventPartitionRolloverMigrator.cs   # CHANGED — provision per fab, then roll months
        FabPartitionProvisioner.cs          # NEW — CREATE TABLE IF NOT EXISTS events_<fab>
        CatalogFabStorageReadiness.cs       # NEW — cached catalog lookup behind the port
      Ingress/
        PersistenceLoopHostedService.cs     # CHANGED — 23514 logged distinguishably
    Api/
      EventsEndpoints.Writes.cs             # CHANGED — readiness refused before the channel
      EventsEndpoints.cs                    # CHANGED — declare 503

  Identity/
    Infrastructure/
      KeycloakAdmin/
        HttpKeycloakAdminClient.cs      # CHANGED — list groups under a path
        IKeycloakAdminClient.cs         # CHANGED — one new read

  MigrationRunner/
    KeycloakProvisionedFabSource.cs     # NEW — the adapter; the only place both halves meet
    Program.cs                          # CHANGED — register it

  AppHost/
    AppHost.cs                          # CHANGED — WaitFor(keycloak) + credentials
    Realms/smart-sentinel-eye-realm.json # CHANGED — the query-groups-only client

tests/
  EventIngestion.Application.Tests/     # port contracts against fakes
  Architecture.Tests/                   # the boundary rule, extended to name the adapter
  Integration.Tests/EventIngestion/     # a fab with no partition, end to end
```

**Structure Decision**: The existing per-context layout is unchanged. The only
structurally novel piece is `MigrationRunner/KeycloakProvisionedFabSource.cs`,
which exists precisely because it is the one component allowed to know both
Identity and EventIngestion — see research §R1.
