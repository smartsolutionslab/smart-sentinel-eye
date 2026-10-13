# Plan 345: The layout context under its limits

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2887.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 15 of 29 hits by shape only; the 14 left are named with reasons (spec §2). |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| §II (no primitives on Domain models) | Domain change uses the existing `Creation` value object. The one new type (J1) is private, in Application. |
| §III (no cross-context refs) | No reference added; `Shared.Contracts` untouched. |
| ADR-0050 | `Log.cs` untouched; every log call keeps method, arguments and order. |
| ADR-0113 | Stale-version checks stay before any mutation, in the same position in each handler's order. |
| ADR-0141 | Extracted refusal helpers return `Option<TError>`, not a nullable. |
| ADR-0051 | `AddLayoutCompositionInfrastructure` keeps its signature; extractions are private static. |
| House rules | Fluent DI chains in extracted registration methods; `var`; one parameter per line when wrapping; handlers keep their deconstruction line as written (`HandlerDeconstructionTests`). |

**Concurrency with #2896.** #2896 (`D:\Github\sse-2896`) edits only `src/*/Api/*.cs` plus two new
files in `tests/Architecture.Tests` (`KioskLatencyRecordingCharacterisationTests.cs`,
`RuleActionBuildCharacterisationTests.cs`). This plan touches no `Api/` file and adds a differently
named file in `tests/Architecture.Tests`. No Api file calls a member whose signature changes here
(only `internal` `Revision` factories and private methods change).

## 2. Shape

One bounded context, three layers. No new public type, no new project reference, no migration,
no messaging change, no AppHost change.

## 3. Production shape (binding on the engineer)

Every extracted method: **<= 4 parameters (CancellationToken counts), <= 30 lines, complexity
<= 10**, verified with the grep in §5 after each commit. Statements move verbatim; comments move
with the code they explain.

### 3.1 Domain: `Revision.NewDraft` / `Revision.Branch` (hits 18, 19)

`(LayoutRevisionNumber number, GridDimensions grid, IReadOnlyList<Tile> tiles, Creation creation)`.
`NewDraft` assigns `Creation = creation`. `Branch` stays a pass-through.

Callers in `Layout.cs` build **a fresh** `Creation.From(CreatedAt.From(now), createdBy)` for the
revision. **Never pass the layout's own `Creation` instance**: `Creation` is an EF owned type on
both `layouts` and `layout_revisions`, and one instance under two owners throws on save — the same
hazard `NewDraft`'s cloning comment describes for `Grid`. The T001 pin
(`ShouldNotBeSameAs`) catches this in a unit test; Integration.Tests would catch it only in CI.

### 3.2 `BranchDraftRevisionCommandHandler` (hit 1)

Extract the "no Published revision" block (current lines 41-67, with its comments) to
`private async Task<Option<BranchDraftRevisionError>> RefuseUnbranchableAsync(Layout layout, LayoutIdentifier layoutIdentifier, CancellationToken cancellationToken)`.
Order kept: not found -> stale -> (open draft | name taken) -> branch.

### 3.3 `EditDraftRevisionCommandHandler` (hit 2)

Extract (a) the cross-fab block to
`private async Task<Option<EditDraftRevisionError>> RefuseCrossFabTilesAsync(Layout layout, IReadOnlyList<Tile> tiles, CancellationToken)`
(keeps the `RefusedCrossFabTiles` log inside it) and (b) stale / missing revision / not-a-draft to
`private static Option<EditDraftRevisionError> RefuseRevision(Layout layout, EditDraftRevisionCommand command)`.
Order kept: grid -> not found -> cross-fab -> stale -> missing -> not draft.

### 3.4 `SwitchWallSceneCommandHandler` (hits 3, 4)

Extract stale + not-in-set to
`private static Option<SwitchWallSceneError> RefuseBeforeLookup(Wall wall, AggregateVersion expectedVersion, LayoutIdentifier? namedLayout)`
(use the real type of `expectedVersion`), and the closing `if/else` log to
`private void LogOutcome(Wall wall, bool switched, OperatorIdentifier by)` or equivalent. The
already-showing path keeps its `SaveAsync` call (spec §6). Order kept: not found -> stale ->
not in set -> already showing -> lookup -> not published -> switch.

### 3.5 `LayoutRevisionPublishedDomainEventHandler` (hit 5)

Extract the tile projection to `private static IReadOnlyList<LayoutTileV2> ToContractTiles(IReadOnlyList<Tile> tiles)`;
if still over, also extract the `LayoutRevisionPublishedV2` construction. Publish-then-broadcast
order kept.

### 3.6 `WallSceneSwitchRequestedV1Handler` (hits 7-10; hit 6 left)

Introduce `private readonly record struct SwitchRequestTrace(Guid Wall, Guid Rule, Guid CausingEvent)`
(J1). Built once in `Handle` from the deconstructed locals, **by name**
(`new SwitchRequestTrace(Wall: wall, Rule: rule, CausingEvent: causingEventIdentifier)`) so a
transposition is visible. Helpers become:

- `TryResolveFab(EventMetadata? metadata, SwitchRequestTrace trace)`
- `TryLoadWall(FabIdentifier fab, SwitchRequestTrace trace, CancellationToken)` (wall Guid from `trace.Wall`)
- `LogMissingWall(WallIdentifier wallIdentifier, FabIdentifier requestFab, SwitchRequestTrace trace, CancellationToken)`
- `TryResolveRuleAndEvent(SwitchRequestTrace trace)`
- `ApplySwitchAsync(Wall targetWall, SceneTarget parsedTarget, SceneSwitchCause.Rule cause, SwitchRequestTrace trace)` is still 5 with the token, so bundle the switch to apply as
  `private sealed record PlannedSwitch(Wall Wall, SceneTarget Target, SceneSwitchCause.Rule Cause)` and take `(PlannedSwitch, SwitchRequestTrace, CancellationToken)`.
  Constructing `SceneSwitchCause.Rule` earlier than today is safe: it is a value with no side
  effect; build it after the dedup reservation, where `ruleIdentifier`/`causingEvent` exist today.

`Handle` then splits: resolve (fab -> wall -> parse target -> identifiers) into one private async
method returning an `Option` of the resolved values; dedup; the two pre-lookup drops (not in set,
already showing) into a private method returning `bool`; apply. **Exact order of side effects is
binding**: fab, wall load (incl. `FindFabAsync` on a miss), target parse, identifier parse, dedup
reservation, not-in-set, already-showing, publishable lookup, not-published, `SwitchTo`,
`SaveAsync`, outcome log. `Drops_a_through_c_never_consume_a_dedup_key` and the empty-identifier
tests pin parts of this. The constructor and the deconstruction line in `Handle` stay as they are.
The `DbUpdateConcurrencyException` must still escape `Handle` uncaught (FR-011).

### 3.7 `ListLayoutsQueryHandler` (hit 16)

Extract the two shapes into `private static async Task<ListLayoutsResult> ListPublishedAsync(IQueryable<Layout> visible, CancellationToken)`
and `ListChainsAsync(...)`. The fab filter stays in `HandleAsync` so FR-005 applies to both.

### 3.8 `LayoutCompositionInfrastructureModule` (hit 22)

Extract private static methods in the existing order, e.g. `AddPersistenceAndRepositories`,
`AddDomainEventHandlers`, `AddCameraFabGuard` (the HTTP client block with its `#pragma` for
S1075/S5332 moved verbatim — an existing, narrowly scoped suppression, not a new one),
`AddCommandHandlers`, `AddIntegrationEventHandlers`, then the Wolverine call. Registration
**order and lifetimes are unchanged**. `AddCommandHandlers` alone is 27 lines today; split further if
the grep says so.

### 3.9 `LayoutConfiguration` / `WallConfiguration` (hits 23, 24)

`LayoutConfiguration.Configure` becomes a sequence of private static methods: chain scalars,
creation, version/archival, indexes, revisions (which itself delegates to revision scalars, grid,
tiles, revision creation, revision indexes). `WallConfiguration.Configure` likewise (scalars,
creation, version + index, scenes). Builder calls keep their order. Each file keeps its own
private `Creation` mapping helper; a shared helper across the two files is not required and not
in scope.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

Coverage that already exists and is relied on (must pass unmodified):

| Member | Existing tests |
|---|---|
| `Revision.NewDraft` via `CreateDraft` | `LayoutTests` (revision 0 Creation at/by), `LayoutBranchSpanTests`, `LayoutChainArchivalTests`, `LayoutGuardTests` |
| BranchDraft handler | `BranchDraftRevisionCommandHandlerTests` (6), `StaleVersionRejectionTests.BranchDraft_*` |
| EditDraft handler | `EditDraftRevisionCommandHandlerTests` (9), `StaleVersionRejectionTests.EditDraft_*` |
| SwitchWallScene handler | `SwitchWallSceneCommandHandlerTests` (8), `...AlreadyShowingTests` (1) |
| LayoutRevisionPublished handler | `LayoutRevisionPublishedDomainEventHandlerTests` (3) |
| WallSceneSwitchRequestedV1 handler | `WallSceneSwitchRequestedV1HandlerTests` (20) |
| ListLayouts handler | `ListLayoutsQueryHandlerTests` (5) |
| Infrastructure module | `Architecture.Tests` composition tests via `PostgresComposition` (idempotency sweep, health checks, pool bound, tracing/metrics, execution strategy) |
| EF configurations | none in unit tests; Integration.Tests (CI only) |

Gaps, pinned by **new files only** (no existing test edited):

| ID | File | Pins |
|---|---|---|
| C1 | `tests/LayoutComposition.Domain.Tests/Layout/RevisionCreationCharacterisationTests.cs` | Branched revision's `Creation` = (branch time, branching operator); revision 0's `Creation` is **not the same instance** as the layout's (`ShouldNotBeSameAs`); branched revision's `Creation` not the same instance as the base revision's |
| C2 | `tests/LayoutComposition.Application.Tests/Commands/DraftRefusalOrderCharacterisationTests.cs` | Edit: cross-fab refusal returns `TileCameraOutsideFab` code with the outside camera list; cross-fab + stale -> cross-fab; stale + missing revision -> stale. Branch: open draft + stale -> stale |
| C3 | `tests/LayoutComposition.Application.Tests/Walls/SwitchWallSceneOrderCharacterisationTests.cs` | stale + not in set -> `WALL_STALE`; rendered log text (`CapturingLogger`) for a real switch, an already-showing no-op and a Next that finds nothing else Published |
| C4 | `tests/LayoutComposition.Application.Tests/EventHandlers/WallSceneSwitchRequestLogSlotCharacterisationTests.cs` | With three **distinct** Guids, the **exact** rendered message for: no fab, unusable fab, unknown wall, wall in another fab, invalid identifiers, malformed target, not in set, not Published, dedup hit, switched, no-op. Existing tests use `ShouldContain`, which does not catch a transposition |
| C5 | `tests/LayoutComposition.Application.Tests/Queries/ListLayoutsOrderingCharacterisationTests.cs` | Picker ordered by Name ordinal-ignore-case (e.g. "b", "A", "c" -> A, b, c); picker DTO's RevisionNumber, GridRows, GridCols, PublishedAt; admin list ordered by CreatedAt descending. Verify `LayoutRevisionPublishedDomainEventHandlerTests` covers overlay Some **and** None; add a case to this group's sibling file `LayoutRevisionPublishedTileMappingCharacterisationTests.cs` if not |
| C6 | `tests/Architecture.Tests/Persistence/LayoutCompositionRegistrationCharacterisationTests.cs` | Via `PostgresComposition.Compose("layout-composition", builder => captured = [.. builder.Services])`: the ordered list of descriptors whose service or implementation type (or any generic argument, recursively) is declared in a `SmartSentinelEye.LayoutComposition.*` assembly, rendered as `"{Lifetime} {ServiceType} -> {ImplementationType or factory/instance}"`, equals a literal list captured on `f30dced3`; plus `TimeProvider` singleton, `IHttpContextAccessor` and SignalR's `IHubContext<>` present. Do **not** assert the total descriptor count (it moves with Wolverine/package bumps) |
| C7 | `tests/LayoutComposition.Infrastructure.Tests/LayoutCompositionModelCharacterisationTests.cs` + `LayoutCompositionModelBaseline.cs` | `new LayoutCompositionDbContext(new DbContextOptionsBuilder<…>().UseNpgsql("Host=unused").Options).GetService<IDesignTimeModel>().Model.ToDebugString(MetadataDebugStringOptions.LongDefault)`, line endings normalised to `\n` on both sides, equals a raw-string-literal baseline captured on `f30dced3` (a `.cs` const, so no csproj edit). If `Database.HasPendingModelChanges()` is `false` on `f30dced3`, assert it too; if `true`, do not assert it and record the finding in the PR body |

A characterisation test that arrives red on the unrefactored code is a test defect and is fixed in
the test, never by a production change in this spec. None of these classes is in
`Integration.Tests`, so no `ci-shards/*.filter` entry is needed.

## 5. Measurement and run commands (before and after, quoted in the PR)

```sh
dotnet build src/LayoutComposition/Infrastructure/SmartSentinelEye.LayoutComposition.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep LayoutComposition
```

Before (`f30dced3`): 29 lines (spec §2). After: the 14 "Leave" rows exactly. Line numbers of left
hits in `WallSceneSwitchRequestedV1Handler.cs` / `Revision.cs` neighbours may shift; compare by
member.

```sh
dotnet test tests/LayoutComposition.Domain.Tests -c Release          # 249 passed on f30dced3
dotnet test tests/LayoutComposition.Application.Tests -c Release     # 150 passed on f30dced3
dotnet test tests/LayoutComposition.Infrastructure.Tests -c Release  # 2 passed on f30dced3
dotnet test tests/Architecture.Tests -c Release                      # PrimitiveBoundaryTests, HandlerDeconstructionTests, BoundaryTests, PostgresComposition suites
dotnet format src/LayoutComposition/Infrastructure/SmartSentinelEye.LayoutComposition.Infrastructure.csproj --verify-no-changes
```

(The Infrastructure project's `dotnet format` covers its own files only; run it also for the
Domain and Application `.csproj`.) Integration.Tests: CI.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Shared `Creation` instance across EF owners | §3.1 rule + C1 `ShouldNotBeSameAs` |
| Guid triple transposed in `SwitchRequestTrace` | Named-argument construction + C4 exact-text pins |
| Side-effect order drift in the rule handler (dedup consumed earlier/later) | §3.6 binding order + existing dedup tests + C4 |
| DI order change altering last-wins resolution | C6 ordered list |
| EF config reorder altering the model | C7 model baseline |
| Collision with #2896 | No `Api/` file touched; distinct test file names |
