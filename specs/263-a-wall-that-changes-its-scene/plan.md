# Plan 263: A wall that changes its scene

**Spec**: [spec.md](spec.md) · **Issue**: #2608 · **Phase**: 2 (Plan), drafted and **not past
the gate**. The plan assumes the **proposed** answers to PD-1..PD-6. If a human overrides
one, the sections tagged with that PD change. Each such section names its PD so the
blast radius is visible.

## Constitution / ADR check

| Rule | How this plan holds it |
|---|---|
| §II no primitives on domain models (ADR-0139/0140) | Every `Wall` field is a value object. `SceneVersion`, `ShowingSince`, `WallName` and `WallIdentifier` are new. `Shared.Contracts` keeps primitives, as exempted. |
| §III no cross-context references | Automation declares its own `WallIdentifier`, `LayoutIdentifier` and `SceneTarget`, as it already does `OverlayIdentifier`. The two contexts talk only through `WallSceneSwitchRequestedV1`. |
| §IV latency budget | No leg spent (spec header). FR-V1 is a Phase-5 measurement. |
| §VII observability | Structured `[LoggerMessage]` for dropped requests. The switch is traced across Automation → LayoutComposition → hub by the existing Wolverine/OTel propagation. No budget-leg dashboard is required, because it is not a leg. |
| §VIII trust boundaries | Scope checks at every endpoint. The fab is part of the lookup, not a check afterwards (the `GetLayoutQueryHandler` pattern). The rule path re-checks the fab against `Metadata.Fab`. |
| §IX no speculative generality | No hold/pause (PD-1). No schedule trigger. No kiosk switching. |
| ADR-0043/0113 | `If-Match` on edit and manual switch. EF concurrency token via `AggregateVersion`. |
| ADR-0142/0143 | `POST /walls` takes `Idempotency-Key`. Switch and edit are protected by `If-Match` instead, and are not retried by default. |
| ADR-0088/0126 | Outbox for both new events. LayoutComposition keeps the durable inbox, which R5 depends on. |
| ADR-0092/0093 | Folder layout per §2 and §3 below. |

## Bounded contexts touched

| Context / surface | Layers | Story |
|---|---|---|
| **Shared.Contracts** | `LayoutComposition/WallConfiguredV1`, `WallSceneChangedV1`, `WallSceneSwitchRequestedV1` | US1 (first two), US2 (third) |
| **LayoutComposition** | Domain `Wall/`; Application Commands/Queries/EventHandlers; Infrastructure persistence, migration, broadcaster; Api `/walls` | US1, plus US2's handler |
| **Automation** | Domain `RuleAction.SwitchWallScene`; Application evaluator/effect/fan-out/DTO/dry-run; Infrastructure column converter; Api request parsing | US2 |
| **AuditObservability** | `IntegrationEventAuditHandler` + the V1 resource map | US1 (2 events), US2 (1 event) |
| **ApiGateway** | Route `/walls/**` → layout-composition | US1 |
| **apps/shared** | `walls.api.ts` + schema; `layoutHub.ts` `WallSceneChanged` subscription; `rules.api.ts` / `rules.schema.ts` action variant | US1, US2 |
| **management-web** | `features/walls/` (list, create/edit, detail with switch buttons); rule editor action option | US1, US2 |
| **kiosk-web** | `/walls/:wallIdentifier` route, `WallPage`, picker section; `LayoutGrid` extracted from `CellPage` | US1 |

**Foundational (blocks all fan-out):** the `Shared.Contracts` records and the gateway
route. Everything else fans out per context once they exist (tasks.md).

## 1. Contracts (`src/Shared.Contracts/LayoutComposition/`)

Primitives are used at the wire (ADR-0040). The field order below is load-bearing for
`HandlerDeconstructionTests`.

```csharp
public sealed record WallConfiguredV1(
    Guid Wall, string Name, IReadOnlyList<Guid> Scenes, Guid Showing,
    DateTimeOffset ConfiguredAt, EventMetadata Metadata) : IIntegrationEvent;

public sealed record WallSceneChangedV1(
    Guid Wall,
    Guid PreviousLayout,
    Guid CurrentLayout,
    long SceneVersion,
    string Cause,                    // "Operator" | "Rule" | "Reconfigured"
    Guid? Rule,                      // set iff Cause == "Rule"
    Guid? CausingEventIdentifier,    // set iff Cause == "Rule"
    DateTimeOffset ChangedAt,
    EventMetadata Metadata)          // Fab = wall's fab; Actor = operator, null for Rule
    : IIntegrationEvent;

public sealed record WallSceneSwitchRequestedV1(      // Automation → LayoutComposition (US2)
    Guid Wall,
    string Target,                   // "Next" | "Layout"
    Guid? TargetLayout,              // set iff Target == "Layout"
    Guid Rule,
    DateTimeOffset RequestedAt,
    Guid CausingEventIdentifier,
    EventMetadata Metadata) : IIntegrationEvent;      // RootIngestedAt forwarded as the highlight does
```

**Why two events for the rule path.** The split mirrors `OverlayHighlightRequestedV1`.
Automation cannot reach into LayoutComposition, so it publishes a *request*.
LayoutComposition owns the state and publishes the *fact*. Audit records both, so a
dropped request (FR-010) is still visible as a request with no matching change.

**Why `Cause` is a string plus two nullable fields**, and not a polymorphic payload:
the audit payload serialiser and the contract tests handle flat records, and this is the
shape `RuleActionDto` already uses. A Shared.Contracts test pins the rule that the pair
is set iff `Cause == "Rule"`.

## 2. LayoutComposition Domain: `src/LayoutComposition/Domain/Wall/` (PD-2, PD-3, PD-5, PD-6)

```
Wall/
  Wall.cs                     aggregate root
  WallIdentifier.cs           Guid v7 record (ADR-0090)
  WallName.cs                 StringValueObject, MaximumLength = LayoutName's
  SceneVersion.cs             long VO, Initial = 0, Next()
  ShowingSince.cs             DateTimeOffset VO
  SceneTarget.cs              abstract record: Next | Layout(LayoutIdentifier)   (mirrors RuleAction)
  SceneSwitchCause.cs         abstract record: Operator(OperatorIdentifier) | Rule(RuleIdentifier, CausingEventIdentifier) | Reconfigured(OperatorIdentifier)
  RuleIdentifier.cs           context-local copy (Automation's rule; no reference)
  CausingEventIdentifier.cs   context-local Guid VO
  SceneSetViolation.cs        enum-like VO: TooFew | TooMany | Duplicate  (mirrors GridViolation)
  IWallRepository.cs          Option<Wall> FindAsync(WallIdentifier, IReadOnlyList<FabIdentifier>, ct); Add
  ILayoutPublicationLookup.cs Task<IReadOnlySet<LayoutIdentifier>> PublishedAmong(IEnumerable<LayoutIdentifier>, FabIdentifier, ct)
  Events/
    WallConfiguredDomainEvent.cs
    WallSceneSwitchedDomainEvent.cs   (Wall, Previous, Current, SceneVersion, Cause, At)
```

**`Wall` behaviour.** Illegal state transitions are programmer errors that throw.
Operator input fails as a `Result` at the handler, following `Layout`'s two-tier
pattern (`ValidateGrid` + `RequireValidGrid`).

- `static Option<SceneSetViolation> ValidateScenes(IReadOnlyList<LayoutIdentifier>)` is
  the single source of truth for count and duplicates. Handlers call it first, and the
  aggregate calls it again as a backstop.
- `static Wall Create(FabIdentifier, WallName, IReadOnlyList<LayoutIdentifier> scenes, OperatorIdentifier by, IClock)`
  sets `Showing = scenes[0]` and `SceneVersion.Initial`, and raises
  `WallConfiguredDomainEvent`.
- `void EditScenes(IReadOnlyList<LayoutIdentifier>, OperatorIdentifier by, IClock)`
  replaces the set. If `Showing ∉ new`, `Showing` becomes `new[0]` and the method raises
  `WallSceneSwitched(Reconfigured)`. It always raises `WallConfigured`.
- `Option<WallSceneSwitchedDomainEvent> SwitchTo(SceneTarget target, IReadOnlySet<LayoutIdentifier> publishable, SceneSwitchCause cause, IClock)`
  works as follows:
  - `Layout(x)`: `x ∉ Scenes` throws. The handler pre-checks and maps that to 400.
  - `x ∉ publishable` throws. The handler maps that to 409, or drops it on the rule path.
  - `x == Showing` returns `None` (a no-op).
  - `Next` walks cyclically from `Showing`, skipping scenes not in `publishable`. It
    returns `None` if it comes back to `Showing` (PD-6).
  - On a change it sets `Showing`, `SceneVersion.Next()` and `ShowingSince`, then raises
    and returns the event.

  The domain stays free of I/O: publishability is computed by the handler through
  `ILayoutPublicationLookup` and passed in.

**Why pass `publishable` in and not load Layouts into the aggregate.** `Layout` is
another aggregate. One transaction touches one aggregate, and the lookup is a read.
Between that read and the commit, the layout can be archived. That window is the same
one PD-6's "showing scene became unpublished" case already covers, so no new race is
introduced.

## 3. LayoutComposition Application (ADR-0093)

```
Commands/
  CreateWallCommand.cs + CreateWallErrors.cs          (Fab, Name, Scenes, By)       → Result<WallIdentifier, CreateWallError>
  EditWallScenesCommand.cs + EditWallScenesErrors.cs  (Fabs, Wall, ExpectedVersion, Scenes, By)
  SwitchWallSceneCommand.cs + SwitchWallSceneErrors.cs(Fabs, Wall, ExpectedVersion, Target, By) → Result<WallDto, SwitchWallSceneError>
  Handlers/...
Queries/
  GetWallQuery.cs + GetWallErrors.cs, ListWallsQuery.cs + ListWallsErrors.cs, Handlers/...
EventHandlers/
  WallConfiguredDomainEventHandler.cs       → WallConfiguredV1
  WallSceneSwitchedDomainEventHandler.cs    → WallSceneChangedV1 + broadcaster.WallSceneChangedAsync
  WallSceneSwitchRequestedV1Handler.cs      (US2) → SwitchTo with Cause.Rule; drop + log on FR-010 cases
DTOs/
  WallDto.cs (Wall, Version, Fab, Name, Scenes, Showing, SceneVersion, ShowingSince)
```

**Error codes.** Everything is an `ApiError` subtype, and every failure goes through a
`*Failures` factory (the `PublishRevisionErrors.cs` pattern).

| Code | Status |
|---|---|
| `WALL_NOT_FOUND` | 404 |
| `WALL_TOO_FEW_SCENES`, `WALL_TOO_MANY_SCENES`, `WALL_DUPLICATE_SCENE`, `WALL_SCENE_OTHER_FAB`, `WALL_SCENE_NOT_FOUND`, `WALL_SCENE_NOT_IN_SET` | 400 |
| `WALL_SCENE_NOT_PUBLISHED`, `WALL_NAME_TAKEN`, `WALL_STALE` | 409 |

`WALL_SCENE_OTHER_FAB` and `WALL_SCENE_NOT_FOUND` stay distinct because the caller's own
fab is being checked, so nothing is disclosed. `WALL_STALE` is 409, not 412, consistent
with `LAYOUT_REVISION_STALE`.

**Handlers destructure their input first** (CLAUDE.md). Every command above has at
least two fields.

**The rule path and concurrency (US2-6).** On an EF concurrency conflict,
`WallSceneSwitchRequestedV1Handler` lets Wolverine's retry re-deliver, which re-reads
the wall and re-applies. That is correct under PD-1, because a rule switch applies to
whatever is current. It is not "retry-on-conflict" in ADR-0113's sense: that ADR
forbids retrying an **HTTP** request whose caller asserted a version, and the rule path
asserts none. The manual path returns `WALL_STALE` and does not retry. **Check at 4b**
that the context's Wolverine error policy retries `DbUpdateConcurrencyException`. If it
does not, add a narrowly scoped retry for this handler and explain why at the call site.

**Dedup (R5).** A duplicate delivery of a "Next" request is absorbed by the durable
inbox. US2-10 pins this at integration level. Do not add an application-level dedup
table on top.

## 4. LayoutComposition Infrastructure and Api

### 4.1 Persistence and migration (ADR-0067)

- A `walls` table with these columns: `wall_id uuid pk`, `fab`, `name`,
  `showing_layout_id uuid`, `scene_version bigint`, `showing_since timestamptz`,
  `created_at`, `created_by`, and `version` (the concurrency token).
- There is deliberately **no `archived_at` column**. Archiving is a follow-up feature
  (spec §3), and adding the column now would be speculative generality.
- Scenes live in an **owned table**, `wall_scenes(wall_id fk, ordinal int, layout_id
  uuid, pk(wall_id, ordinal))`. The alternative was a `uuid[]` column. The owned table
  was chosen to match `layout_revision_tiles` (ADR-0112) and to keep the EF mapping of a
  value-object list consistent within the context.
- A partial unique index on `(fab, lower(name))`. Walls cannot be archived in v1, so the
  index has no predicate yet. It mirrors spec 086's name rule.
- `IdempotencyKeyTable` already exists in LayoutComposition (`POST /layouts` uses it). No
  new key table is needed. Confirm this at 4b.

### 4.2 `ILayoutPublicationLookup`

The implementation lives in Infrastructure: one query over `layout_revisions` with
`state = Published` and `layout_id = any(@ids)`, joined to the layout's fab.

### 4.3 Broadcaster

Add `Task WallSceneChangedAsync(WallSceneChangedNotification, CancellationToken)` to
`ILayoutLifecycleBroadcaster`. The notification is `(FabIdentifier Fab, WallIdentifier
Wall, LayoutIdentifier Showing, SceneVersion SceneVersion)`. It has a hub message
`WallSceneChangedHubMessage` and a new method `WallSceneChanged` on
`ILayoutLifecycleClient`, and it is sent to the fab group. Delivery is best-effort,
like every other frame: FR-008's reconnect re-read is the safety net.

### 4.4 Api (`WallEndpoints.cs`, `WallEndpoints.Commands.cs`, `WallEndpoints.Queries.cs`, `Requests/`) (PD-4)

| Method + route | Scope | Headers | Returns |
|---|---|---|---|
| `POST /walls` | `sse.layouts.write` | `Idempotency-Key` (optional) | 201 + id |
| `GET /walls` | `sse.layouts.read` | | 200 `WallDto[]` for caller fabs |
| `GET /walls/{wallIdentifier:guid}` | `sse.layouts.read` | | 200 `WallDto` + `ETag` |
| `PUT /walls/{wallIdentifier:guid}/scenes` | `sse.layouts.write` | `If-Match` required (428) | 200 `WallDto` |
| `POST /walls/{wallIdentifier:guid}/switch` | `sse.layouts.write` | `If-Match` required (428) | 200 `WallDto` |

The switch request body is `{ target: "next" | "layout", layout?: guid }`. It is parsed
at the edge into `SceneTarget`. An unknown `target`, or `layout` without a target, is
rejected as a 400 through the existing request-validation `ApiError`.

### 4.5 Gateway

Add a YARP route `/walls/{**catch-all}` → layout-composition, mirroring the `/layouts`
route.

## 5. Automation (US2, PD-1)

- **Domain.** Add `RuleAction.SwitchWallScene(WallIdentifier Wall, SceneTarget Target)`,
  with `From(Guid wall, string target, Guid? layout)`, plus these context-local value
  objects in `Domain/Rule/`: `WallIdentifier`, `LayoutIdentifier`, and `SceneTarget`
  (`Next | Layout(LayoutIdentifier)`). The latter is a *separate* type from
  LayoutComposition's, on purpose: that is what §III requires.
- **Persistence.** `RuleActionColumnConverter` gains `SwitchWallScene|<wall>|Next` and
  `SwitchWallScene|<wall>|Layout|<layout>`. No migration is needed if `action_packed` is
  unbounded text. **Check this at 4b.** If the column has a length limit, the longest
  new form is about 100 characters.
- **Evaluation.**
  - `RuleActionEffect.SwitchWallScene(Guid Wall, string Target, Guid? Layout, Guid Rule)`.
  - `RuleEvaluator` fills in `Rule` from `CompiledRule.Identifier`.
  - `FabEventIngestedV1Handler` fans out to `WallSceneSwitchRequestedV1`, with metadata
    built exactly as the highlight branch builds it.
  - The existing effects are **not** retro-fitted with a rule identifier. That is a
    separate improvement, and doing it here would widen the diff.
- **API and DTO.**
  - `CreateRuleRequest` gains `WallIdentifier?`, `SceneTarget?` and
    `TargetLayoutIdentifier?`.
  - `RulesEndpoints` parses `ActionType == "SwitchWallScene"` using the existing
    required-field style.
  - `RuleDto` / `RuleActionDto` gain `ForSwitchWallScene`, and `RuleMapper` maps it.
  - `DryRunRuleQueryHandler` treats the action like `HighlightOverlay`: it fires and
    produces no value.

## 6. Frontend

### 6.1 `apps/shared`

- `api/walls.api.ts` + `walls.schema.ts` (Zod): list, get, create, editScenes, switch.
  The switch sends `If-Match` from the cached `version`.
- `realtime/layoutHub.ts`: an `onWallSceneChanged(handler)` subscription.
- `api/rules.schema.ts` / `rules.api.ts` (US2): the `SwitchWallScene` action variant.

### 6.2 management-web: `features/walls/`

- A list page.
- A create/edit form (React Hook Form + Zod): name, plus an ordered multi-select of
  Published layouts with up/down reordering.
- A detail page. It shows the scene list with a **Showing** badge, updated live through
  the hub subscription. It has a **Next** button and a **Show** button per scene. On
  `WALL_STALE` it shows a "wall changed, refreshed" toast and re-fetches, without
  retrying automatically.
- Nav entry "Walls".
- US2: the rule editor's action-type select gains "Switch wall scene", with a wall
  select and a target select (Next / a scene of that wall).

### 6.3 kiosk-web

- **Refactor first (characterisation, observed green before the change):** extract the
  grid rendering out of `features/cell/CellPage.tsx` into `features/cell/LayoutGrid.tsx`,
  a component that takes `layoutIdentifier`. `CellPage` becomes route param →
  `<LayoutGrid>`. Its existing tests (`CellPage.test.tsx`, `kiosk-shows-a-wall.spec.ts`)
  must pass **unmodified**.
- **New:** a `features/wall/WallPage.tsx` at `/walls/:wallIdentifier`. It does
  `GET /walls/{id}` → `<LayoutGrid layoutIdentifier={showing} key={showing}>`. The key
  forces a full remount, so the old peer connections are torn down deterministically
  (see FR-V1). It subscribes to `WallSceneChanged`, keeps the highest `sceneVersion`,
  and re-reads on hub reconnect.
- The picker gains a "Walls" section.
- The kiosk's wall mode, fab scoping (spec 067) and session handling (spec 052) are
  unchanged.

## 7. Tests (phase 4a: red, except the §6.3 characterisation)

| Layer | Project / file | Covers |
|---|---|---|
| Domain | `tests/LayoutComposition.Domain.Tests/Wall/WallTests.cs`, `SceneTargetTests.cs`, and one test file per new VO | FR-001..004, PD-6 walk/skip/wrap, the no-op, the reconfigure pointer move, invariant backstops |
| Domain | `tests/Automation.Domain.Tests/Rule/RuleActionSwitchWallSceneTests.cs` | `From` guards, variant equality |
| Application | `tests/LayoutComposition.Application.Tests/Walls/*HandlerTests.cs` | every error code, the fab-in-lookup rule, destructuring, event emission, FR-010 drops |
| Application | `tests/Automation.Application.Tests/Evaluation/…SwitchWallScene…` | effect carries the rule id; fan-out metadata incl. RootIngestedAt; dry-run |
| Infrastructure | `tests/Automation.Infrastructure.Tests/…RuleActionColumnConverterTests` | round-trip of both packed forms |
| Contracts | `tests/Shared.Contracts.Tests/LayoutComposition/Wall*V1Tests.cs` | shape pins; the "set iff" rule |
| Audit | `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs` (extend) | three new events mapped |
| Integration (Aspire fixture) | `tests/Integration.Tests/LayoutComposition/WallEndpointsTests.cs`, `…/Automation/SwitchWallSceneRuleTests.cs` | US1-1..15 over HTTP; US2-2..10 over the real bus incl. duplicate delivery; audit rows |
| e2e (Playwright) | `e2e/wall-changes-its-scene.spec.ts` (US1-3, US1-17), `e2e/rules.spec.ts` extension (US2-2) | kiosk follows; reconnect reconcile |
| Frontend unit | `WallPage.test.tsx`, `walls.api.test.ts`, `walls` form tests, `layoutHub.test.ts` extension | out-of-order discard, If-Match, stale toast |

## 8. Commit sequence

Each commit must build on its own (ADR-0087).

1. `docs(258): …` contains spec, plan and tasks. This is the Phase 1–3 artifact.
2. **US1 PR.** Each item is one commit:
   1. contracts
   2. characterisation tests for the `CellPage` extraction (green)
   3. red tests
   4. the `LayoutGrid` extraction
   5. Domain
   6. Application
   7. Infrastructure and the migration
   8. Api and gateway
   9. audit
   10. apps/shared
   11. management-web
   12. kiosk-web
   13. e2e
3. **US2 PR**, after PD-1 is confirmed and US1 is merged:
   1. the contract
   2. red tests
   3. Automation Domain, Infrastructure and Application
   4. LayoutComposition handler
   5. audit
   6. frontend rule editor
   7. e2e

## 9. Verification (Phase 5)

Run spec §7 (FR-V1) and write the figures to `verification.md`. Then walk through US1's
and US2's independent tests on the running stack. Use the Aspire trace view to confirm
one trace spanning ingest → Automation → LayoutComposition → hub for US2-2.

## 10. Risks

The spec's R1–R5 apply here too. In addition:

- **P1 — Kiosk remount cost.** Remounting with `key` is the simplest way to get correct
  teardown, but it is also the stutter FR-V1 measures. Do not optimise it pre-emptively.
- **P2 — The shared `ILayoutLifecycleBroadcaster` grows a seventh method.** That is
  acceptable, because it is the one hub (ADR-0152). Splitting the interface is not
  justified by one method.
