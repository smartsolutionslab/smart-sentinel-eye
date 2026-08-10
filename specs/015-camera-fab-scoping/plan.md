# Implementation Plan: Fab-scope the camera catalogue

**Branch**: `015-camera-fab-scoping` | **Date**: 2026-08-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/015-camera-fab-scoping/spec.md`

## Summary

Give `Camera` a fab, key its name on `(fab, name)`, resolve the caller's fab on
every camera endpoint, and stamp the fab on the events other contexts consume.

This is the third application of a pattern that already exists twice. Spec 013
did it for rules, spec 014 for system variables, and the mechanism — ADR-0114's
decision table, `FabResolution`, `FabClaims` — is reused **unchanged**. The plan
is therefore mostly about *not* re-deciding things, and about the two places
where this context genuinely differs from its predecessors.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs (ADR-0070), EF Core +
Npgsql, Wolverine (ADR-0042), `ServiceDefaults.Authorization` —
`FabResolution`, `FabClaims`, `IFabAuthorizationGuard`

**Storage**: PostgreSQL, `camera-catalog-db` (ADR-0009)

**Testing**: xUnit + Shouldly + Moq; integration on the Aspire fixture
(ADR-0052, ADR-0103); Playwright for the management UI (ADR-0108)

**Target Platform**: Linux containers, Aspire in dev → k3s in prod (ADR-0024,
ADR-0025)

**Project Type**: Web service (bounded context) + React management app

**Performance Goals**: No change. Camera reads are not on the event-to-overlay
path; see Constitution Check §IV.

**Constraints**: The forward migration must be safe against populated data —
the scaffolded `AddColumn(nullable: false, defaultValue: "")` produces an
invalid fab and would strand every existing row (observed in spec 014's T043
walk, and the reason for the four-step form).

**Scale/Scope**: 250-camera target (constitution §Scale). One bounded context,
5 endpoints, 1 aggregate, the management-web camera surface, and the camera
lifecycle contracts in `Shared.Contracts`.

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| I. On-Prem First | ✅ | No new infrastructure; no cloud dependency. |
| II. DDD with Value Objects | ✅ | `FabIdentifier` as CameraCatalog's own copy per ADR-0044 — a fourth, matching Identity, EventIngestion, Automation and SystemVariables grammar-for-grammar. Primitives do not cross the boundary. |
| III. Bounded Context Isolation | ✅ | No cross-context project reference. The fab reaches StreamDistribution and AuditObservability as a primitive on `EventMetadata`, which is what that field is for. |
| IV. Latency Budget | ✅ | **N/A with reason.** Camera endpoints are operator-facing CRUD, not on `event → overlay`. The one legitimate concern — the camera lookup that stream provisioning performs — gains an equality term on an already-indexed column. No leg changes; nothing to re-measure. |
| V. Spec-Driven Development | ✅ | This plan; tasks and issues follow. |
| VI. Aspire Is the Composition Root | ✅ | No new runtime resource. |
| VII. Observability | ✅ | The migration backfill announces its count (FR-011). That reaches the log only because #1395 wired the Npgsql notice handler — a dependency worth stating, since before it the warning went nowhere. |
| VIII. Safe by Default at Trust Boundaries | ✅ | This principle *is* the feature. FR-006's not-found-for-another-fab is the enumeration defence; validation happens at the endpoint, once. |
| IX. Forward-Compatible Strategy Interfaces | ✅ | None introduced; none needed. |

**Gate: PASS.** No violations, so the Complexity Tracking section is removed
rather than left empty.

## Where this differs from specs 013 and 014

Two things, and they are the parts of the plan worth reading.

### 1. A camera is referenced by other contexts; a variable was not

`CameraIdentifier` and the camera contracts appear in **StreamDistribution,
AuditObservability, LayoutComposition and ScenarioSimulator**. Spec 014's
`Variable` was referenced by nobody.

This does not widen the scope — those contexts keep working unchanged, because
a fab is *added* to the events rather than required from them. But it does mean
the events are a contract change with real consumers, and it is why FR-012
exists: without the fab on the event, StreamDistribution's own fab scoping (the
next spec) would have to call back into the catalogue per stream.

**Decision**: stamp `EventMetadata.Fab` on every camera lifecycle event.
Additive, no version bump under ADR-0073 — the field exists and is currently
null.

### 2. The placeholder-fab bridge is avoidable here

Spec 014 made `Define` require a fab before the endpoint could resolve one,
which forced seven `munich` placeholders through four phases. That was a real
cost: they had to be tracked, greppable, and deleted individually.

CameraCatalog can avoid it entirely by **ordering the work so the endpoint
resolves the fab in the same slice that makes the aggregate require it**. The
aggregate, the command, and the endpoint's resolution land together; there is
never an intermediate state where a fab is required but unobtainable.

**Consequence for the task breakdown**: no "foundational" phase that leaves the
context half-scoped. Phase 2 is domain + command + endpoint as one unit.

## Project Structure

### Documentation (this feature)

```text
specs/015-camera-fab-scoping/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── cameras-api.md   # Phase 1
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
src/CameraCatalog/
├── Domain/Camera/
│   ├── FabIdentifier.cs          # NEW — this context's own copy (ADR-0044)
│   ├── Camera.cs                 # + Fab, required at registration
│   └── ICameraRepository.cs      # GetByNameAsync takes a fab
├── Application/
│   ├── Commands/                 # + Fab on register/edit/retire commands
│   ├── Queries/                  # + Fabs on list/get
│   └── EventHandlers/
├── Infrastructure/Persistence/
│   ├── Configurations/CameraConfiguration.cs   # fab column, (fab, name) index
│   └── Migrations/               # four-step migration + announced backfill
└── Api/CameraEndpoints.cs        # FabResolution on all five endpoints

src/Shared.Contracts/CameraCatalog/   # fab stamped on lifecycle events

apps/management-web/src/features/cameras/   # fab column + selector
apps/shared/src/api/cameras.api.ts          # fabId param, fab on the DTO

tests/
├── CameraCatalog.Domain.Tests/
├── CameraCatalog.Application.Tests/
├── Integration.Tests/CameraCatalog/          # cross-fab + resolution table
└── e2e/cameras.spec.ts                       # single-fab half
```

**Structure Decision**: Existing per-context layout (ADR-0092 domain folders,
ADR-0093 application folders). No new projects. `CameraCatalog.Infrastructure.Tests`
is **not** created — unlike spec 014's T002, there is no untested infrastructure
component here that the change rewrites.

## Phase 0: Research

See [research.md](./research.md). Three questions were genuinely open; all are
resolved there, none reached the spec as `[NEEDS CLARIFICATION]`.

## Phase 1: Design & Contracts

- [data-model.md](./data-model.md) — the `Camera` change, the index swap, and
  the four-step migration form.
- [contracts/cameras-api.md](./contracts/cameras-api.md) — the five endpoints
  and their new statuses.
- [quickstart.md](./quickstart.md) — the walk, including the migration step
  against a database that predates the feature.
