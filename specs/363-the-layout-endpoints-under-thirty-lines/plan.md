# Plan 363: The layout endpoints under thirty lines

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2929.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears all 10 S138 hits by shape only; S107 set must not grow, so **every new helper has at most 4 parameters**. |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| ADR-0070 | Still Minimal APIs; mapping helpers return the same `RouteHandlerBuilder` chains. |
| ADR-0113 | `If-Match` stays read in each handler body, after the parse and before fab resolution. |
| ADR-0142 | Idempotency scope keeps (route constant, caller, fab, body fingerprint), same order; pinned by test. |
| ADR-0105 | Existing `Ensure.That` guards stay first in each handler. No new public surface needs one; the new `internal` helper guards its delegate. |
| §III (no cross-context refs) | Api-only change; no reference added. |
| House rules | `var`; one parameter per line when a call wraps; lines <= 160 (`.editorconfig`); no drive-by comments; existing doc comments move with their code. |

## 2. Shape

One bounded context (LayoutComposition), one layer (Api). No new type outside the Api project, no
Application/Domain/Infrastructure edit, no migration, no messaging, no AppHost change. Files:

- `src/LayoutComposition/Api/LayoutCompositionFabResolution.cs` — one new `internal` helper (§3.1).
- `src/LayoutComposition/Api/LayoutEndpoints.Commands.cs`, `LayoutEndpoints.cs` — group A.
- `src/LayoutComposition/Api/WallEndpoints.Commands.cs`, `WallEndpoints.cs` — group B.

## 3. Production shape (binding on the engineer)

### 3.1 Shared tail: `LayoutCompositionFabResolution.ForCallerFabsAsync`

Six of the eight command handlers end in the same 7 lines (resolve caller fabs, return the failure,
take the value) followed by the handler call. One helper, 4 parameters:

```csharp
internal static async Task<IResult> ForCallerFabsAsync(
    ClaimsPrincipal user,
    IFabAuthorizationGuard fabGuard,
    Func<IReadOnlyList<FabIdentifier>, Task<IResult>> dispatch,
    CancellationToken cancellationToken)
{
    Ensure.That(dispatch).IsNotNull();

    var fabsResolution = await ResolveCallerFabsAsync(user, fabGuard, cancellationToken);

    return fabsResolution.IsFailure
        ? fabsResolution.Error
        : await dispatch(fabsResolution.Value);
}
```

Used by Publish, Archive, Revert, EditDraft, EditScenes, Switch. **Not** by BranchDraft (out of
scope, spec §8). Exceptions from the guard propagate unchanged.

### 3.2 Layout revision outcome: private `RevisionOutcome<TError>` in `LayoutEndpoints.Commands.cs`

Publish, Archive, Revert and EditDraft all render `Ok(number.Value)` or `error.ToProblem()`:

```csharp
private static IResult RevisionOutcome<TError>(Result<LayoutRevisionNumber, TError> result)
    where TError : ApiError =>
    result.Match<IResult>(
        onSuccess: number => Results.Ok(number.Value),
        onFailure: error => error.ToProblem());
```

Without it Publish lands at 29 lines (no margin); with it, 27. Walls keep their inline
`Match` (`Results.Ok(wall)`, a different success shape — no second helper for two call sites).

### 3.3 Body parses: reuse `BoundaryParse.TryParse` (spec J1)

`BoundaryParse.TryParse` (ServiceDefaults) already produces exactly the inline catch's problem.
Each inline `try { … } catch (ArgumentException ex) { return Results.Problem(title: X, detail:
ex.Message, statusCode: 400); }` becomes one `BoundaryParse.TryParse(() => …, "X", out var …, out
var …)` call. Where the parse yields several values, a private expression-bodied parser returns a
tuple and keeps the original evaluation order (left to right = the original statement order):

| Handler | Parser (private, expression-bodied) | Error code |
|---|---|---|
| CreateDraft | `ParseCreateLayout(CreateLayoutRequest body) => (LayoutName.From(body.Name), LayoutRequestParsing.ParseGrid(body.Grid), LayoutRequestParsing.ParseTiles(body.Tiles))` | `LAYOUT_INVALID_INPUT` |
| EditDraft | `ParseDraftEdit(int revisionNumber, EditDraftRequest body) => (LayoutRevisionNumber.From(revisionNumber), LayoutRequestParsing.ParseGrid(body.Grid), LayoutRequestParsing.ParseTiles(body.Tiles))` | `LAYOUT_INVALID_INPUT` |
| CreateWall | `ParseCreateWall(CreateWallRequest body) => (WallName.From(body.Name), ParseScenes(body.Scenes))` | `WALL_INVALID_INPUT` |
| EditScenes | existing `ParseScenes(body.Scenes)` | `WALL_INVALID_INPUT` |
| Switch | existing `ParseSceneTarget(body)` | `WALL_INVALID_INPUT` |

Deconstruct the tuple on the next line (`var (name, grid, tiles) = input;`).

**Kept inline, deliberately (spec FR-004):** the `Guid.Empty` refusal with its spelled
`StatusCodes.Status400BadRequest` in the six route-value handlers — it is
`RouteValueRefusalDeclarationTests`' antecedent and that guard has no count that would go red if it
vanished. Also kept in the body: `ConcurrencyHeaders.TryReadExpectedVersion` (the
`PreconditionDeclarationTests` count of 5 Layout handlers in `LayoutEndpoints.Commands.cs` goes red
if it moves — a correct tripwire), `IdempotencyHeaders.TryRead` (`StatusProducerDeclarationTests`
M10) and `IdempotentRequest.ExecuteCreateAsync` (`IdempotentKeyReuseDeclarationTests` count).

### 3.4 Creates: hoist the command, extract the execution

`CreateDraft` / `CreateWall` hoist the command to a local after fab resolution (it is a
side-effect-free positional record of already-validated value objects; constructing it before
`ExecuteCreateAsync` instead of inside the dispatch lambda changes nothing observable, including on
replay), and extract the `IdempotentExecution` construction (4 parameters):

```csharp
private static IdempotentExecution CreateExecution(
    Option<IdempotencyKey> key,
    CreateLayoutDraftCommand command,
    CreateLayoutRequest body,
    CreateLayoutServices services) =>
    new(
        key.Map(supplied => IdempotencyScope.For(
            supplied,
            CreateEndpoint,
            command.CreatedBy.Value.ToString(),
            Option<string>.Some(command.Fab.Value),
            IdempotencyFingerprint.Of(body))),
        services.Idempotency,
        services.Clock);
```

(Wall: same with `CreateWallCommand`, `command.By`, `CreateWallRequest`, `CreateWallServices`.)
The body keeps `return await IdempotentRequest.ExecuteCreateAsync(CreateExecution(...), location,
async token => (await services.Handler.HandleAsync(command, token)).Match(...), cancellationToken);`.
The `var handler = services.Handler; var fabGuard = services.FabGuard;` aliases may be dropped in
favour of `services.X`.

### 3.5 Map methods: one private helper per mapping (spec J3)

```csharp
public static IEndpointRouteBuilder MapLayoutEndpoints(this IEndpointRouteBuilder app)
{
    Ensure.That(app).IsNotNull();

    var group = app.MapGroup("/layouts")
        .WithTags("Layouts");

    // (the existing 403/404 comment, unchanged)
    MapCreateLayoutDraft(group);
    MapGetLayout(group);
    … in the existing order …
    return app;
}

private static RouteHandlerBuilder MapCreateLayoutDraft(RouteGroupBuilder group) =>
    group.MapPost("/", CreateDraft)
        … the existing chain, byte-identical …;
```

Binding constraints from the source-scanning guards (each reads the file textually):

- The helper parameter **must be named `group`**: every reader binds a mapping's receiver to its
  `MapGroup` declaration **by name across the file** (`EndpointScopeDeclarationTests`,
  `StatusProducerDeclarationTests`, `RouteValueRefusalDeclarationTests`). A different name leaves
  the mapping unbound — red, loudly.
- Exactly one `var group = app.MapGroup("…")` per file (a second declaration of `group` is refused
  by `EndpointScopeDeclarationTests`; `RouteValueRefusalDeclarationTests` requires MapGroup sites ==
  bound groups).
- Helpers sit **below** `Map…Endpoints` in the same `*Endpoints.cs` file:
  `ConcurrencyConflictDeclarationTests` takes the prefix from the nearest preceding `MapGroup`
  literal, and every reader only scans files ending `Endpoints.cs`.
- Registration order unchanged (data-source order is part of the §4.1 baseline).

Helper names = existing `WithName` values prefixed `Map`: Layout — `MapCreateLayoutDraft`,
`MapGetLayout`, `MapListLayouts`, `MapPublishRevision`, `MapArchiveRevision`,
`MapBranchDraftRevision`, `MapEditDraftRevision`, `MapRevertRevision`; Wall — `MapCreateWall`,
`MapGetWall`, `MapListWalls`, `MapEditWallScenes`, `MapSwitchWallScene`.

### 3.6 Expected S138 lines after (non-blank, non-comment; the build decides)

CreateDraft ~25, Publish/Archive/Revert ~27, EditDraft ~24, MapLayoutEndpoints ~13, CreateWall
~24, EditScenes ~26, Switch ~26, MapWallEndpoints ~10, every helper <= 15.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

All in `tests/Architecture.Tests` (spec J2). Add `<PackageReference Include="Moq" />` to
`SmartSentinelEye.Architecture.Tests.csproj` (central package version already exists for the other
test projects). Architecture.Tests is not sharded (only `Integration.Tests/ci-shards/*.filter`), so
no shard-filter entry is needed.

| ID | File | Pins |
|---|---|---|
| C1 | `LayoutCompositionRouteTableCharacterisationTests.cs` | In-process `WebApplication`, `MapLayoutEndpoints()` + `MapWallEndpoints()`; each `RouteEndpoint` in data-source order rendered as: `RoutePattern.RawText`, HTTP methods, `IEndpointNameMetadata`, `IEndpointSummaryMetadata`, `ITagsMetadata`, every `IAuthorizeData` (policy/roles/schemes) and `IAllowAnonymous`, every `IProducesResponseTypeMetadata` in metadata order (status, type full name, content types), `IAcceptsMetadata`, handler `MethodInfo.Name` and its parameter list (name:type, attributes). Asserts equality with a raw-string literal captured on `0296275e`. |
| C2 | `LayoutEndpointCharacterisationTests.cs` | Spec §4.2 rows CreateDraft, Publish, Archive, Revert, EditDraft. |
| C3 | `WallEndpointCharacterisationTests.cs` | Spec §4.2 rows CreateWall, EditScenes, Switch. |
| C0 | `LayoutCompositionEndpointHost.cs` (internal helper, optional) | Shared in-process host for C2/C3: `WebApplication.CreateBuilder([])`, handlers constructed with Moq'd `ILayoutRepository` / `IWallRepository` / `ICameraFabGuard` / `ILayoutPublicationLookup` / `IFabAuthorizationGuard` / `IIdempotencyStore`, `IClock`, `TimeProvider`, `NullLogger<T>`; request driven through the endpoint's `RequestDelegate` with `DefaultHttpContext`, `IHttpRequestBodyDetectionFeature` set (as in `KioskLatencyRecordingCharacterisationTests`). Returns (status, headers, body). |

Rules for C1-C3:

- Problem bodies asserted **exactly** (string equality of the JSON), not "contains".
- Repository calls verified with the exact identifier and fab list (Moq `Verify`), so an argument
  transposition inside a moved lambda fails.
- The keyed-create case captures the `IdempotencyScope` handed to the store and asserts route
  constant, caller, fab and fingerprint field by field.
- A case that arrives **red on `0296275e`** is a test defect; fix the test, never the product.
- If driving a delegate in process proves infeasible for one case (e.g. a binding the in-process
  host cannot satisfy), drop that case, name it in the PR, and name the CI integration test that
  observes it instead (spec §4.3). Do not substitute a weaker assertion silently.

## 5. Verification commands (orchestrator / engineer)

```sh
# S138 + S107 surface, before and after every refactor commit
dotnet build src/LayoutComposition/Api -c Release --no-incremental -nologo -v q 2>&1 \
  | grep "LayoutComposition.Api" | grep -oE "[A-Za-z.]+\([0-9]+,[0-9]+\): warning S[0-9]+[^\[]*" | sort -u

# characterisation + guards
dotnet test tests/Architecture.Tests -c Release
```

Before: 10 S138 + 10 S107. After group A: 4 S138 (walls) + 10 S107. After group B: 0 S138 + 10 S107
(same methods; line numbers may differ). Stop the Aspire stack before building (MSB3027).

## 6. Commit groups (one PR)

- **Tests**: C0-C3 (+ csproj), green on `0296275e`. One commit per test file.
- **Group A (layouts)**: (1) add `ForCallerFabsAsync` and use it in Publish/Archive/Revert/EditDraft
  with `RevisionOutcome`; (2) CreateDraft + EditDraft parses via `BoundaryParse`, `CreateExecution`;
  (3) `MapLayoutEndpoints` per-route helpers.
- **Group B (walls)**: (4) EditScenes/Switch via `ForCallerFabsAsync` + `BoundaryParse`;
  (5) CreateWall; (6) `MapWallEndpoints` per-route helpers.

Each commit builds **and passes** C1-C3 plus all Architecture.Tests on its own (rebase-merge lands
them individually, ADR-0087).

## 7. Risks

| Risk | Mitigation |
|---|---|
| A source guard silently stops checking a route because its antecedent token left the handler body | Tokens kept inline (§3.3); the two count-based guards go red if not; J1 names the one accepted shift and the stronger pin replacing it |
| Mapping helper's receiver unbound by the textual readers | Parameter named `group`; helpers below the MapGroup in the same file (§3.5); guards fail loudly otherwise |
| Route-table rendering misses a metadata kind OpenAPI uses | C1 renders the full ordered metadata of the listed kinds plus handler signature; OpenAPI is derived from these |
| In-process delegate needs services the host lacks | All `[FromServices]` types registered in C0; infeasible cases fall back to named CI tests (§4) |
| Tuple parse changes exception precedence | Tuple elements evaluate left to right in the original statement order; C2 pins "revision 0 + null grid → revision message" |
| `HandlerDeconstructionTests` flags a tuple deconstruction | It targets message/command records; a tuple local is not a record field. Run it; if it fires, deconstruct with the field names it expects rather than editing the guard |
| Collision with parallel work | No open PR touches `src/LayoutComposition/Api` or the guards (checked 2026-10-15); #2930-#2933 touch other projects |
