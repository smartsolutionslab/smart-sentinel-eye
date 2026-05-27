# Implementation Plan: 005 — SystemVariables

**Branch:** `005-system-variables` | **Date:** 2026-05-27 | **Spec:** [spec.md](./spec.md)

**Status:** Draft (Phase 2 — Plan)

**Input:** Feature specification from `specs/005-system-variables/spec.md`
(Phase 1 merged in PR #424, zero `[NEEDS CLARIFICATION]` markers, eight
Q&A clarifications resolved).

## Summary

Lights up the first end-to-end slice through the **SystemVariables**
bounded context. Manual-only variable definition + value setting in
v1; the resolution-and-push pipeline is designed so that spec 006's
event-driven values plug in as just a new write path.

- **Backend (SystemVariables):** new `Variable` aggregate (CRUD per
  ADR-0009; no event sourcing). Discriminated `VariableValue` VO
  with four cases: `Unset`, `String`, `Number`, `Boolean`. Three
  commands (`DefineVariable`, `SetVariableValue`, `ArchiveVariable`),
  two queries (`GetVariable`, `ListVariables`). New context DB
  `system-variables-db`. Standard Wolverine outbox + per-module
  queue isolation.
- **Reverse-index:** an in-memory `ConcurrentDictionary<VariableName,
  HashSet<OverlayIdentifier>>` maintained inside
  `SystemVariables.Infrastructure`. Seeded on startup by HTTP-calling
  the existing `GET /overlays?state=Published` endpoint and parsing
  every label's text for `{{name}}` tokens. Kept current by
  subscribing to `OverlayRevisionPublishedV1` and
  `OverlayRevisionArchivedV1` from the integration bus (already
  emitted by spec 004's broadcaster).
- **Resolver:** pure string-substitution function in
  `SystemVariables.Application` — takes label text + a `(VariableName
  → VariableValue snapshot)` map, returns resolved text. Unknown,
  archived, and `Unset` variables resolve to the literal
  `{{name}}` per FR-011.
- **Resolved-text snapshot endpoint:** new `GET /system-variables/snapshot?overlayIdentifier=X`
  returns `{ overlayIdentifier, resolvedText, version }` where
  `version` is a monotonic per-overlay counter. Kiosks call this on
  every Layout/overlay fetch to get the cold-start resolved text;
  subsequent updates arrive via SignalR.
- **SignalR push:** extends the existing
  `ILayoutLifecycleBroadcaster` (LayoutComposition.Domain) with
  `ResolvedOverlayTextChangedAsync(ResolvedOverlayTextChangedNotification, ct)`.
  `SignalRLayoutLifecycleBroadcaster` implements it; the frame goes
  out on `/hubs/layouts` as `ResolvedOverlayTextChanged`. **No new
  hub.** This is the second documented cross-context allow-rule
  (alongside spec 004).
- **Frontend (management-web):** new **System variables** nav entry,
  new `SystemVariablesPage` (DataTable with state filter + value
  inline edit per row), new `SystemVariableDialog` for define +
  archive. New `systemVariablesApi` RTK Query slice in `apps/shared`.
- **Frontend (kiosk-web):** `CellPage` learns to call
  `useGetOverlaySnapshotQuery` for the resolved text and to render
  it instead of the raw label. `useLayoutLifecycle` grows
  `onResolvedOverlayTextChanged`; on push, it replaces the rendered
  text without touching geometry or font.

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Backend language | C# / .NET 10 | ADR-0001, ADR-0024 |
| Frontend language | TypeScript / React 19 | ADR-0074 |
| Persistence | EF Core on Postgres (per-context DB `system-variables-db`) | ADR-0009, ADR-0071 |
| Messaging | RabbitMQ via Wolverine; per-module queue isolation; Postgres outbox | ADR-0042, ADR-0088 |
| Real-time push | **Existing** `/hubs/layouts` SignalR hub. New typed-client method `ResolvedOverlayTextChanged`. No new hub. | spec FR-013 + ADR-0076 |
| Identity | Keycloak — same realm; `sse.management` scope on writes; reads allow any authenticated user (kiosk role + admin) | spec FR-021 + ADR-0007 |
| API style | Minimal APIs only | ADR-0070 |
| Errors | `Result<T, ApiError>` with sealed-record error hierarchies (`DefineVariableError`, `SetVariableValueError`, `ArchiveVariableError`) | ADR-0047, ADR-0089 |
| Frontend state | RTK Query — new `systemVariablesApi` slice in shared | ADR-0075 |
| Tests | xUnit + Shouldly + Moq + Testcontainers via `AspireFixture` (extended) | ADR-0052, ADR-0068 |
| Performance | Variable update → kiosk render ≤ 200 ms p95 (NFR-001). Reverse-index lookup O(1). Server-side resolution ≤ 50 ms for up to 50 affected overlays. | spec NFR-001 |
| Scale | 1 000 variables / 100 overlays-with-variables per fab (NFR-003). Plenty of headroom; reconsider above this. | spec |

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| §I On-prem first | SystemVariables runs on the same fab host as everything else. No cloud calls. | ✅ |
| §II DDD + VOs | `VariableIdentifier`, `VariableName`, `VariableType`, `VariableValue` (discriminated), `VariableState`. Maximalist hand-written; `IValueObject<T>` markers. | ✅ |
| §III Bounded-context isolation | All new code in `SmartSentinelEye.SystemVariables.*`. Cross-context contracts via `Shared.Contracts/SystemVariables/`. The bridge into `LayoutComposition.Domain.ILayoutLifecycleBroadcaster` is the second documented allow-rule; added to `BoundaryTests.AllowedCrossContext` table. | ✅ |
| §IV Latency budget | 200 ms variable-push p95 (spec NFR-001) is a strict subset of the 800 ms event-to-overlay budget. Spec 006 spends the remaining ≤ 600 ms. | ✅ |
| §V Spec-driven | Spec PR #424 merged. This plan. Tasks follow. | ✅ |
| §VI Aspire composition root | `system-variables` is a new Aspire project resource; `system-variables-db` is a `postgres.AddDatabase` per-context resource. | ✅ |
| §VII No event sourcing without justification | Variables are CRUD. The reverse-index is an in-memory read projection rebuilt from the `*V1` event stream on start — same pattern as every other read projection. | ✅ |
| §VIII Safe at trust boundaries | Admin policy on writes; authenticated-user on reads. Value-object validation rejects malformed input at the API edge AND at construction. | ✅ |
| §IX Forward-compatible interfaces | `ICommandHandler<,>` / `IQueryHandler<,>` framework-agnostic. Adding spec 006's event-driven `SetVariableValueCommand` invocation requires zero changes to the resolver, push, or reverse-index — they're already write-path-agnostic. | ✅ |

**Result:** No violations. No Complexity Tracking entries.

## Project Structure

### Documentation

```
specs/005-system-variables/
├── spec.md          ← Phase 1 (PR #424, merged)
├── plan.md          ← this file (Phase 2)
└── tasks.md         ← Phase 3 (next; created by /speckit-tasks)
```

### Source code — files added / modified

```
src/SystemVariables/Domain/                          ← scaffold exists; populated here
└── Variable/
    ├── Variable.cs                                  ← aggregate root
    ├── VariableIdentifier.cs                        ← Guid v7 strongly-typed id
    ├── VariableName.cs                              ← StringValueObject; `^[A-Za-z][A-Za-z0-9_]{0,63}$`
    ├── VariableType.cs                              ← enum-backed VO (String|Number|Boolean)
    ├── VariableValue.cs                             ← discriminated VO (4 cases — see Domain Model)
    ├── VariableState.cs                             ← enum-backed VO (Defined|Archived)
    ├── BooleanLabels.cs                             ← VO for `(TruthyLabel, FalsyLabel)`
    ├── IVariableRepository.cs
    └── Events/
        ├── VariableDefinedDomainEvent.cs
        ├── VariableValueChangedDomainEvent.cs
        └── VariableArchivedDomainEvent.cs

src/SystemVariables/Application/
├── Commands/
│   ├── DefineVariableCommand.cs + Errors + Handler
│   ├── SetVariableValueCommand.cs + Errors + Handler
│   └── ArchiveVariableCommand.cs + Errors + Handler
├── Queries/
│   ├── GetVariableQuery.cs + Errors + Handler
│   ├── ListVariablesQuery.cs + Errors + Handler
│   ├── GetOverlaySnapshotQuery.cs + Errors + Handler   ← resolves a specific overlay
│   └── IVariableQuerySource.cs
├── DTOs/
│   ├── VariableDto.cs
│   └── ResolvedOverlaySnapshotDto.cs                ← { overlayIdentifier, resolvedText, version }
├── EventHandlers/
│   ├── VariableValueChangedDomainEventHandler.cs    ← V1 + resolver fan-out + broadcaster
│   ├── VariableArchivedDomainEventHandler.cs        ← V1 + reverse-index removal + broadcaster
│   ├── OverlayRevisionPublishedV1Handler.cs         ← Wolverine subscriber, updates reverse-index
│   └── OverlayRevisionArchivedV1Handler.cs          ← Wolverine subscriber, updates reverse-index
└── Resolution/
    ├── IReverseIndex.cs                             ← scoped read; thread-safe
    ├── IResolver.cs                                 ← LabelText × VariableSnapshot → string
    ├── PlaceholderParser.cs                         ← parses `{{name}}` tokens; pure function
    └── Resolver.cs                                  ← implements IResolver

src/SystemVariables/Infrastructure/
├── SystemVariablesPersistenceModule.cs
├── SystemVariablesInfrastructureModule.cs
├── Persistence/
│   ├── SystemVariablesDbContext.cs
│   ├── Configurations/VariableConfiguration.cs
│   ├── VariableRepository.cs
│   ├── VariableQuerySource.cs
│   ├── SystemVariablesMigrator.cs
│   ├── DesignTimeDbContextFactory.cs
│   └── Migrations/<ts>_InitialSystemVariables.cs
└── Resolution/
    ├── InMemoryReverseIndex.cs                      ← ConcurrentDictionary-backed; singleton
    └── ReverseIndexSeederHostedService.cs           ← startup; HTTP-calls overlay-designer

src/SystemVariables/Api/
├── SystemVariablesApiModule.cs
├── SystemVariableEndpoints.cs                       ← POST/GET/PUT/POST /archive endpoints + snapshot
├── Program.cs                                       ← wires everything
└── Requests/
    ├── DefineVariableRequest.cs
    └── SetVariableValueRequest.cs

src/LayoutComposition/Domain/Layout/
└── ILayoutLifecycleBroadcaster.cs                   ← + ResolvedOverlayTextChangedAsync(...)
    + ResolvedOverlayTextChangedNotification record  ← primitive-typed; { overlayId, resolvedText, version }

src/LayoutComposition/Infrastructure/Broadcasting/
├── SignalRLayoutLifecycleBroadcaster.cs             ← + ResolvedOverlayTextChangedAsync impl
└── ILayoutLifecycleClient.cs                        ← + ResolvedOverlayTextChanged + hub message

src/Shared.Contracts/SystemVariables/
├── SystemVariableDefinedV1.cs
├── SystemVariableValueChangedV1.cs
└── SystemVariableArchivedV1.cs

src/MigrationRunner/Program.cs                        ← + AddSystemVariablesPersistence()

src/AppHost/AppHost.cs                                ← + system-variables-db + system-variables project resource

apps/shared/src/api/
├── systemVariables.api.ts                           ← RTK Query slice (list/get/define/setValue/archive/snapshot)
└── systemVariables.schema.ts                        ← Zod validation matching VariableName grammar

apps/shared/src/realtime/layoutHub.ts                 ← + onResolvedOverlayTextChanged callback

apps/management-web/src/features/systemVariables/
├── SystemVariablesPage.tsx + .test.tsx
└── SystemVariableDialog.tsx + .test.tsx

apps/kiosk-web/src/features/cell/CellPage.tsx         ← consumes snapshot endpoint + push events
apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts ← + onResolvedOverlayTextChanged callback
```

### Tests added

```
tests/SystemVariables.Domain.Tests/                  ← new project
tests/SystemVariables.Application.Tests/             ← new project
tests/Integration.Tests/SystemVariables/
├── VariableLifecycleIntegrationTests.cs             ← define + set + archive E2E through API
├── VariableResolutionIntegrationTests.cs            ← GET /snapshot resolves correctly
└── VariablePushIntegrationTests.cs                  ← variable change → kiosk frame ≤ 200 ms
```

## Domain Model

### `Variable` aggregate

```
Variable
├── Id            : VariableIdentifier              (Guid v7, strongly typed)
├── Name          : VariableName                    (immutable post-create)
├── Type          : VariableType                    (String|Number|Boolean; immutable)
├── Value         : VariableValue                   (4-case discriminated VO)
├── State         : VariableState                   (Defined|Archived)
├── BooleanLabels : Option<BooleanLabels>           (Some when Type=Boolean, else None)
├── CreatedAt     : DateTimeOffset
├── CreatedBy     : OperatorIdentifier
└── Version       : int                             (optimistic concurrency, ADR-0043)
```

### `VariableValue` — discriminated VO

```
public abstract record VariableValue : IValueObject
{
    public sealed record Unset                : VariableValue;
    public sealed record StringValue(string Value)  : VariableValue;
    public sealed record NumberValue(double Value)  : VariableValue;
    public sealed record BooleanValue(bool Value)   : VariableValue;
}
```

- `From(type, rawString)` factory does type-checked parsing on the wire boundary.
- `Render(BooleanLabels)` is the only place the value becomes a string. Number formatting is culture-invariant; Boolean uses the variable's configured truthy/falsy labels.
- `Unset` carries no payload; `Render(...)` is **never called on it** — the resolver checks the state first and substitutes the literal placeholder.

### State machine

```
            ┌──────────────────┐
            │                  │
   ── Define ─▶ Defined  ── Archive ──▶ Archived
                  │                       (terminal; name freed)
                  └─ SetValue ─▶ Defined
```

- Only `Defined` accepts `SetValue`. `Archive` is one-way; recreating with the same name is allowed (the original keeps its Archived row in the table for audit but doesn't gate uniqueness).

### Invariants

- Name unique across non-Archived rows.
- Type is immutable.
- `BooleanLabels` is set ⇔ `Type == Boolean`.
- Once a value is set, the variable's value can only transition between `String`/`Number`/`Boolean` cases that match its `Type`, never back to `Unset` (except via `Archive` → recreate cycle).

## Resolution pipeline

### Reverse-index lifecycle

```
Cold start:
   1. ReverseIndexSeederHostedService spins up.
   2. Authenticates with Keycloak (service account).
   3. GET /overlays?state=Published from overlay-designer.
   4. For each overlay: parse label text; for each {{name}} token,
      reverseIndex[name].Add(overlayId).
   5. Marks itself ready.

Live updates:
   • OverlayRevisionPublishedV1 → re-parse label; reconcile the
     reverse-index entries for the affected overlay (add new
     references, remove stale ones in one pass).
   • OverlayRevisionArchivedV1 → remove the overlay from every
     index entry.
   • VariableValueChangedDomainEvent → for each overlayId in
     reverseIndex[name], compute resolved text + push.
```

The index is **never persisted**. Cold start always replays.
SystemVariables is the source of truth; OverlayDesigner is the
source of truth for overlay text. The index is a fast cache of
"which overlays reference which variables right now".

### Resolver

```
public string Resolve(string labelText, Func<string, VariableValue> lookup,
                      Func<string, BooleanLabels> labelsLookup)
```

- Walks the label, regex-matches `{{[A-Za-z][A-Za-z0-9_]{0,63}}}`.
- For each match, calls `lookup(name)`:
  - `null` (unknown variable) → leave literal `{{name}}`
  - `Unset` → leave literal `{{name}}`
  - `StringValue(s)` → substitute `s`
  - `NumberValue(d)` → substitute `d.ToString(CultureInfo.InvariantCulture)`
  - `BooleanValue(b)` → substitute `labelsLookup(name).Truthy` or `.Falsy`
- Returns the resolved text. **Pure function, fully unit-testable
  without infrastructure.**

### Push fan-out

`VariableValueChangedDomainEventHandler`:
1. Publishes `SystemVariableValueChangedV1` (Wolverine outbox).
2. Looks up `reverseIndex[variableName]` → `[overlayId₁, overlayId₂, …]`.
3. For each overlayId:
   - Fetches the current Published label text from a cached
     overlay snapshot (held in the reverse-index entries — we cache
     the parsed label alongside the affected variable names).
   - Calls `Resolver.Resolve(...)` against the current variable
     snapshot.
   - Increments the per-overlay `version` (monotonic per-process
     counter).
   - Calls
     `broadcaster.ResolvedOverlayTextChangedAsync(new(overlayId, resolvedText, version), ct)`.
4. Logs structured `{ variableName, overlayCount, totalElapsedMs }`
   on completion. If `totalElapsedMs > 50` raise a warning (sets up
   NFR-001 alerting later).

## Cross-context allow-rules

`tests/Architecture.Tests/BoundaryTests.cs::AllowedCrossContext` gains
one entry:

```
{ ("SmartSentinelEye.SystemVariables", "Application"),    ["SmartSentinelEye.LayoutComposition"] },
{ ("SmartSentinelEye.SystemVariables", "Infrastructure"), ["SmartSentinelEye.LayoutComposition"] },
```

The first entry is the broadcaster bridge (Domain abstraction
consumed by the event handlers). The second is identical to
OverlayDesigner — Infrastructure references the same abstraction
when wiring DI (no concrete coupling). `SystemVariables.Domain`
remains free of all foreign references; the explicit
`OverlayDesigner_Domain_has_no_infrastructure_framework_dependencies`-
style test gets a sibling for SystemVariables in the polish phase.

## Cross-cutting reach

| Reach | Direction | Why | Justification |
|---|---|---|---|
| SystemVariables.Infrastructure → overlay-designer HTTP | runtime call, no compile dep | reverse-index seeding | One read at startup; alternative (replay outbox events) is overkill for v1 |
| SystemVariables.Application ← OverlayRevisionPublishedV1 / Archived | Wolverine subscriber | reverse-index maintenance | Standard integration-bus pattern; no project ref needed |
| SystemVariables.Application → LayoutComposition.Domain.ILayoutLifecycleBroadcaster | project ref | SignalR push reuses the existing hub | Documented allow-rule; mirrors spec 004 |

## Phase 3 task seeds

Mirrors the spec 004 task structure: ~7-PR sequence (A foundational, B domain, C application + infra + broadcaster, D api + management-web, E kiosk render + push, F polish — no B' equivalent since no cross-context schema extension this round).

Tentative grouping (Phase 3 will lay them out properly):

- **PR A — Phase 1 foundational (~8 tasks):** Aspire wiring, project plumbing, V1 integration-event records, Architecture allow-rule extension.
- **PR B — Domain (~12 tasks):** Variable aggregate + tests, value object hierarchy + tests, state-machine tests.
- **PR C — Application + Infrastructure + broadcaster bridge (~22 tasks):** Commands + handlers + tests, Wolverine subscribers for the reverse-index, resolver + tests, DbContext + EF migration, hosted-service seeder, broadcaster bridge extension.
- **PR D — Api + management-web (~12 tasks):** SystemVariableEndpoints, ApiModule, Program.cs, RTK Query slice, SystemVariablesPage + Dialog + tests.
- **PR E — Kiosk consume (~8 tasks):** CellPage snapshot fetch + push subscription, useLayoutLifecycle extension, vitest tests.
- **PR F — Polish (~6 tasks):** Coverage gates, V1 contract tests, NFR-001 latency integration test, README quickstart, Phase-5 verification gate.

Final task IDs land in `tasks.md` after Phase 3 review.

## Gate (Phase 2 → Phase 3)

Ready for `/speckit-tasks` once the architect lead confirms:

1. Domain model (especially the `VariableValue` discriminated VO + the `Unset` state) is acceptable.
2. The in-memory reverse-index is not over-engineered for v1 — the alternative of "resolve on every kiosk fetch by walking the variables table" is rejected as too slow for NFR-001.
3. The cross-context HTTP call from `SystemVariables.Infrastructure` → `overlay-designer` at startup is acceptable. Alternatives:
   - **Replay outbox events on start** (cleaner separation but slower cold-start with empty index; vulnerable to outbox truncation).
   - **OverlayDesigner pushes a snapshot to SystemVariables on its startup** (couples OverlayDesigner to SystemVariables; rejected).
   - The HTTP call is the lowest-coupling option.
4. The two cross-context allow-rule entries are acceptable. They sit alongside the spec 004 entries; total documented exceptions = 2 contexts × 2 layers each.
5. The phase-3 PR sequence (A→F) matches the prior spec rhythm.
