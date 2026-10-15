# Plan 366: Four Api projects' endpoints under thirty lines

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2931.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears all 10 S138 hits by shape only; the 15 S107 hits stay; **every new helper has <= 4 parameters**. |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| ADR-0070 | Still Minimal APIs; every mapping chain moves byte-identical into a `void` helper. |
| ADR-0113 | `If-Match` stays read in `EditDraft` / `Patch` bodies, at the same point in the order. |
| ADR-0142 | Each create's scope keeps (route constant, caller, fab, body fingerprint), same order; pinned by test. |
| ADR-0114 | `ResolveWriteFabAsync` call sites and their position in `Define`, `Register`, `Patch` unchanged. |
| ADR-0105 | Existing `Ensure.That` guards stay first in each handler; the Overlay parsers carry the elements `Ensure` that used to sit inside the `try`. |
| §III | Api-only change; no project reference added. |
| §VIII | `RecordKioskLatency` (untrusted kiosk input) keeps its discriminator and all three validations, same order. |
| House rules | `var`; one parameter per line when a call wraps; lines <= 160; no drive-by comments; comments move with their code. |

## 2. Shape

Four bounded contexts, Api layer only. No Domain/Application/Infrastructure edit, no messaging, no
migration, no AppHost, no csproj change. Production files (7):

| Context | Files |
|---|---|
| StreamDistribution | `src/StreamDistribution/Api/StreamEndpoints.cs` |
| SystemVariables | `src/SystemVariables/Api/SystemVariableEndpoints.cs` |
| OverlayDesigner | `src/OverlayDesigner/Api/OverlayEndpoints.Commands.cs`, `OverlayEndpoints.cs` |
| CameraCatalog | `src/CameraCatalog/Api/CameraEndpoints.cs` |

No new production file (and no new partial file of an endpoint class: `PreconditionDeclarationTests`
pins `OverlayEndpoints` at 3 files).

## 3. Production shape (binding on the engineer)

### 3.1 Map methods: one `void` helper per mapping (spec 363 §3.5)

Each `Map…Endpoints` keeps `Ensure.That(app)`, its `var <name> = app.MapGroup("…")…;`
statement(s) **unchanged**, any comment that concerns every mapping, then one call per mapping in
the existing order, then `return app;`. Each helper:

```csharp
private static void MapCreateOverlayDraft(RouteGroupBuilder group)
{
    // (the comment that sat above this mapping, unchanged)
    group.MapPost("/", CreateDraft)
        … chain byte-identical …;
}
```

- Named `Map` + the existing `WithName` value (`MapCreateOverlayDraft`, `MapRecordKioskLatency`,
  `MapAuthorizeWhep`, `MapRegisterCamera`, …).
- Returns `void` (returning `RouteHandlerBuilder` triggers S3241 — spec 363).
- **The parameter carries the same name as the group local it receives**, because
  `EndpointScopeDeclarationTests`, `StatusProducerDeclarationTests` and
  `RouteValueRefusalDeclarationTests` bind a mapping's receiver to a `MapGroup` local **by name in
  the same file**: `group` in OverlayDesigner, SystemVariables, StreamDistribution; in CameraCatalog
  **`writes`** for `MapRegisterCamera` / `MapRetireCamera` / `MapPatchCamera` and **`reads`** for
  `MapGetCamera` / `MapListCameras`. This is load-bearing in CameraCatalog: its scope policies are on
  the *groups*, so a helper parameter named anything else makes `EndpointScopeDeclarationTests`
  report the route as unauthorized-and-unreadable.
- Declared **after** the Map method in the same file (`ConcurrencyConflictDeclarationTests` takes
  the prefix from the nearest *preceding* `MapGroup` literal; CameraCatalog's two groups share
  `/cameras`, so either is correct, but keep the order).
- The route literal stays the first argument on the `.MapX(` line (`NameMutabilityConventionTests`
  reads `Map(?:Get|Post|…)\("…{param}`).

Group-wide comments that stay in the Map method: OverlayDesigner's 403 note, SystemVariables'
"Reads — the read scope…" and "Writes — admin policy…" notes (each moves to the first helper of
its block if it reads better there; either is fine), StreamDistribution's 403 note,
CameraCatalog's 403/400 note above `Register`. Per-mapping comments (MediaMTX hook, spec 040
kiosk-latency note, CameraCatalog's retire/patch/get rationale) move into their helper.

Expected: Overlay ~14 lines, Variables ~13, Stream ~10, Camera ~15.

### 3.2 Creates: `BoundaryParse` + tuple parser, `CreateExecution` (spec 363 §3.3-3.4)

Each inline `try { … } catch (ArgumentException ex) { return Results.Problem(title: X, detail:
ex.Message, statusCode: 400); }` becomes one `BoundaryParse.TryParse(() => Parse…(body), "X", out
var input, out var problem)` followed by `var (…) = input;`. Parsers are private static methods in
the **same file** as the handler; evaluation order is the original statement order.

| Handler | Parser | Code |
|---|---|---|
| Overlay `CreateDraft` | block-bodied `(OverlayName Name, List<OverlayElement> Elements) ParseCreateOverlay(CreateOverlayRequest body)`: `Ensure.That(body.Elements).IsNotNull();` then `return (OverlayName.From(body.Name), [.. body.Elements.Select(OverlayElementParsing.ParseElement)]);` | `OVERLAY_INVALID_INPUT` |
| Overlay `EditDraft` | block-bodied `(OverlayRevisionNumber Number, List<OverlayElement> Elements) ParseDraftEdit(int revisionNumber, EditDraftRequest body)`: `Ensure` elements, then `(OverlayRevisionNumber.From(revisionNumber), [.. …])` | `OVERLAY_INVALID_INPUT` |
| Variables `Define` | block-bodied `(VariableName Name, VariableType Type, VariableValue? InitialValue, BooleanLabels? BooleanLabels) ParseDefinition(DefineVariableRequest body)` — the existing body of the `try`, statements in the same order, returning the tuple | `VARIABLE_INVALID_INPUT` |
| Camera `Register` | `(CameraName Name, RtspUrl Url) ParseRegistration(RegisterCameraRequest request)` deconstructing `request` (its `Deconstruct` does the parse, name then url) | `CAMERA_INVALID_REQUEST` |

`Ensure.That(…).IsNotNull()` throws an `ArgumentException` subtype, which `BoundaryParse` catches
exactly as the inline `catch` did.

Each create then hoists the command (already a local in `Define` / `Register`; hoist it in
`CreateDraft`) and extracts its `IdempotentExecution` (4 parameters):

```csharp
private static IdempotentExecution CreateExecution(
    Option<IdempotencyKey> key,
    CreateOverlayDraftCommand command,
    CreateOverlayRequest body,
    CreateOverlayServices services) =>
    new(
        key.Map(supplied => IdempotencyScope.For(
            supplied,
            CreateEndpoint,
            command.CreatedBy.Value.ToString(),
            Option<string>.None,
            IdempotencyFingerprint.Of(body))),
        services.Idempotency,
        services.Clock);
```

- Variables: `DefineVariableCommand command`, `command.DefinedBy`, `Option<string>.Some(command.Fab.Value)`,
  `DefineVariableRequest body`, `DefineVariableServices services`, `DefineEndpoint`.
- Camera: `RegisterCameraCommand command`, `command.RegisteredBy`, `Option<string>.Some(command.Fab.Value)`,
  `RegisterCameraRequest request` (fingerprint **of the request**, as today), `RegisterCameraServices services`,
  `RegisterEndpoint`.

(`Option<IdempotencyKey>` is what `IdempotencyHeaders.TryRead` yields; LayoutComposition's
`CreateExecution` is the model.) The body keeps
`return await IdempotentRequest.ExecuteCreateAsync(CreateExecution(...), <location>, async token =>
(await services.Handler.HandleAsync(command, token)).Match(...), cancellationToken);` with the
location lambda and `Match` unchanged. The `var handler = services.Handler; var fabGuard =
services.FabGuard;` aliases may be dropped for `services.X`.

The command is a side-effect-free positional record of validated value objects; building it before
`ExecuteCreateAsync` rather than inside the dispatch lambda changes nothing observable, replay
included. **Order note for `CreateDraft`:** `user.ToOperatorIdentifier()` already runs before
`ExecuteCreateAsync` today; keep it after the parse.

### 3.3 `RecordKioskLatency` (spec J2)

Extract only the unknown-measurement refusal:

```csharp
private static IResult UnknownMeasurement() =>
    Results.ValidationProblem(new Dictionary<string, string[]>
    {
        [nameof(KioskLatencyReport.Measurement)] =
            [
                "must be 'overlay_draw', 'receive_to_decoded', 'presentation_buffer', "
                + "'wall_skew' or 'label_delay'",
            ],
    });
```

`nameof(report.Measurement)` and `nameof(KioskLatencyReport.Measurement)` both yield
`"Measurement"`. The body keeps: `Ensure`, the `IsBrowserKiosk` early return (first), `isWallSkew`
/ `isLabelDelay` with `report.Measurement == "…"` spellings (`KioskMeasurementContractTests`), the
`segment` lookup, `if (…) { return UnknownMeasurement(); }`, the camera and finiteness refusals
inline, `Record(…)`, `return Results.Accepted();`. Expected ~27 lines. `SegmentFor` is untouched.

### 3.4 Camera `Patch` (spec J3; security-sensitive order)

Body, in this order, every step where it is today:

1. `Ensure.That(request).IsNotNull();`
2. the FR-007 comment, `var fabResolution = await ResolveWriteFabAsync(…);` (one line), failure
   return, `var fab = fabResolution.Value;`
3. `if (!ConcurrencyHeaders.TryReadExpectedVersion(httpContext.Request, out var expectedVersion, out var precondition))`
   (one line) `{ return precondition; }`
4. `changingAddress` / `changingName`; the "Exactly one" comment; the refusal **inline** with its
   `StatusCodes.Status400BadRequest`, its detail moved to
   `private static string ExactlyOneAttribute(bool changingAddress) => changingAddress ? "Send either rtspUrl or name, not both: each is applied under its own version." : "Send either rtspUrl or name.";`
5. `var identifier = CameraIdentifier.From(camera);` `var actor = ResolveOperator(httpContext);`
6. one dispatch:

```csharp
return changingName
    ? await PatchAttributeAsync(
        () => CameraName.From(request.Name),
        name => renameHandler.HandleAsync(new RenameCameraCommand(fab, identifier, name, expectedVersion, actor), cancellationToken))
    : await PatchAttributeAsync(
        () => RtspUrl.From(request.RtspUrl),
        url => addressHandler.HandleAsync(new ChangeCameraAddressCommand(fab, identifier, url, expectedVersion, actor), cancellationToken));
```

with

```csharp
private static async Task<IResult> PatchAttributeAsync<TValue, TError>(
    Func<TValue> parse,
    Func<TValue, Task<Result<CameraIdentifier, TError>>> apply)
    where TError : ApiError
{
    if (!BoundaryParse.TryParse(parse, "CAMERA_INVALID_REQUEST", out var value, out var problem))
    {
        return problem;
    }

    var result = await apply(value);

    return result.Match<IResult>(
        onSuccess: _ => Results.NoContent(),
        onFailure: error => error.ToProblem());
}
```

The two "204 rather than 200" comments merge into one above `PatchAttributeAsync`'s `Match` only
if they say the same thing; otherwise keep the address one there verbatim and drop nothing.
Exception precedence is unchanged: `CameraIdentifier.From` and `ResolveOperator` still run before
any attribute parse, and only the value-object `From` sits inside `BoundaryParse` (the commands are
built outside it, as today). Expected ~29 lines; if the build says more, shorten in this order:
inline `fab` as `fabResolution.Value` in the two lambdas; nothing else in steps 1-5 moves.

### 3.5 Camera `Register`

`BoundaryParse` + `ParseRegistration` (§3.2), then fab resolution and `ResolveOperator` in today's
order, the existing `RegisterCameraCommand command`, `CreateExecution`. Expected ~27 lines.

### 3.6 Tokens that must stay put (source guards read them as text)

| File | Must remain in the named handler's body / the file |
|---|---|
| `OverlayEndpoints.Commands.cs` | `CreateDraft`: `IdempotencyHeaders.TryRead`, `IdempotentRequest.ExecuteCreateAsync`. `EditDraft`: the `overlayIdentifier == Guid.Empty` refusal with `StatusCodes.Status400BadRequest`, `ConcurrencyHeaders.TryReadExpectedVersion`. Untouched handlers unchanged. |
| `OverlayEndpoints.cs` | exactly one `var group = app.MapGroup("/overlays")`; every mapping on `group.` |
| `SystemVariableEndpoints.cs` | `Define`: `IdempotencyHeaders.TryRead`, `IdempotentRequest.ExecuteCreateAsync`. One `var group = app.MapGroup("/system-variables")` with its `.RequireAuthorization()`; mappings on `group.`. `SetValue`/`Archive` untouched. |
| `StreamEndpoints.cs` | one `var group = app.MapGroup("/streams")`; `.AllowAnonymous()`, `.RequireRateLimiting("whep-authorize")` and `.ProducesProblem(StatusCodes.Status429TooManyRequests)` on the `/authorize` chain; `RecordKioskLatency`: `.ValidationProblem(`; file: `report.Measurement == "wall_skew"` / `"label_delay"` and `"…" => LatencySegment.` |
| `CameraEndpoints.cs` | `var writes = app.MapGroup("/cameras")…RequireAuthorization(Scope.Sse.Cameras.Write)` and `var reads = …Read)` unchanged; `Register`: `IdempotencyHeaders.TryRead`, `IdempotentRequest.ExecuteCreateAsync`; `Patch`: `ConcurrencyHeaders.TryReadExpectedVersion`, `StatusCodes.Status400BadRequest`. |

Accepted shift (spec J1): `CreateDraft`, `Define`, `Register` no longer spell
`StatusCodes.Status400BadRequest`; their 400 declaration is pinned by §4 C1 instead.

## 4. Characterisation design

All new files in `tests/Architecture.Tests/` (namespace `SmartSentinelEye.Architecture.Tests`). No
csproj change: Moq and all four contexts' Api/Application/Domain/Infrastructure projects are already
referenced.

| ID | File | Content |
|---|---|---|
| H1 | `RouteTableRendering.cs` | `internal static` — `Render(RouteEndpoint)`, `IsContract`, `RenderParameter`, `RenderType`, `Normalise`, copied from `LayoutCompositionRouteTableCharacterisationTests` (incl. the `AsyncStateMachineAttribute` / `DebuggerStepThroughAttribute` / `ParameterBindingMetadata` filter) plus one case: `EnableRateLimitingAttribute limit => "rate-limit " + limit.PolicyName`. |
| H2 | `InProcessEndpointDriver.cs` | `internal static` — `SendAsync(WebApplication app, string method, string pattern, ClaimsPrincipal user, InProcessRequest request)` (4 params; `InProcessRequest` = route values, body JSON, headers, query, all optional) returning `InProcessResponse(int Status, string Body, string Location)`; `Endpoints(WebApplication)`; `Operator(Guid subject, params string[] fabs)`; the private `BodyPresent` feature. Logic copied from `LayoutCompositionEndpointHost.SendAsync`. |
| C1a-d | `OverlayDesignerRouteTableCharacterisationTests.cs`, `SystemVariablesRouteTableCharacterisationTests.cs`, `StreamDistributionRouteTableCharacterisationTests.cs`, `CameraCatalogRouteTableCharacterisationTests.cs` | bare `WebApplication.CreateBuilder([]).Build()`, the context's `Map…Endpoints()`, every endpoint rendered via H1, equal to a raw-string literal captured on `39b0ef64`. One `[Fact]` each. |
| H3 | `OverlayDesignerEndpointHost.cs` | Moq `IOverlayRepository`, `IClock`, `IIdempotencyStore`; `TimeProvider.System`; real `CreateOverlayDraftCommandHandler`, `EditDraftRevisionCommandHandler` (plus the other four write handlers so every mapped `[FromServices]` resolves); `MapOverlayEndpoints()`. |
| H4 | `SystemVariablesEndpointHost.cs` | Moq `IVariableRepository`, `IFabAuthorizationGuard`, `IClock`, `IIdempotencyStore`; real `DefineVariableCommandHandler` (+ others mapped); `MapSystemVariableEndpoints()`. |
| H5 | `CameraCatalogEndpointHost.cs` | Moq `ICameraRepository`, `IFabAuthorizationGuard`, `IClock`, `IIdempotencyStore`; real `RegisterCameraCommandHandler`, `RenameCameraCommandHandler`, `ChangeCameraAddressCommandHandler` (+ `RetireCameraCommandHandler`); `MapCameraCatalogEndpoints()`. |
| C2 | `OverlayDraftEndpointCharacterisationTests.cs` | spec §4.2 rows `CreateDraft`, `EditDraft`. |
| C3 | `SystemVariableDefineEndpointCharacterisationTests.cs` | spec §4.2 row `Define`. |
| C4 | `CameraRegisterEndpointCharacterisationTests.cs` | spec §4.2 row `Register`. |
| C5 | `CameraPatchEndpointCharacterisationTests.cs` | spec §4.2 row `Patch`. |

`RecordKioskLatency`: `KioskLatencyRecordingCharacterisationTests` (existing, unmodified) is its C.

Rules for C1-C5 (spec 363 §4, unchanged):

- Problem bodies asserted **exactly** (JSON string equality); repository calls verified with exact
  arguments (Moq `Verify`); keyed creates capture the `IdempotencyScope` and assert each field.
- Every class observed green on `39b0ef64` in **Release and Debug** before any production commit. A
  case that arrives red on `39b0ef64` is a test defect; fix the test, never the product.
- If a case cannot be driven in process (a binding the bare host cannot satisfy, or a fab refusal
  thrown as `FabAuthorizationException` rather than returned), pin what the delegate does (the
  thrown type and message) — or, if even that is infeasible, drop the case, name it in the PR and
  name the CI integration test of spec §4.3 that observes it. No silent weakening.
- The fab mock's behaviour for "caller names a fab they do not hold" follows
  `LayoutCompositionEndpointHost`'s and the existing fab-resolution tests'; pin whatever
  `FabResolution.ResolveForWriteAsync` returns.

## 5. Verification commands (orchestrator / engineer)

```sh
# S138 + S107, before and after every refactor commit (stop any running AppHost first: MSB3027)
for p in OverlayDesigner SystemVariables StreamDistribution CameraCatalog; do
  echo "== $p"
  dotnet build src/$p/Api -c Release --no-incremental -nologo -v q 2>&1 | grep "$p.Api" \
    | grep -oE "[A-Za-z.]+\([0-9]+,[0-9]+\): warning S[0-9]+[^\[]*" | sort -u
done

dotnet test tests/Architecture.Tests -c Release
dotnet test tests/Architecture.Tests -c Debug --filter "FullyQualifiedName~RouteTableCharacterisation"
```

Before: 10 S138 + 15 S107 (spec §1). After Stream: 8 S138. After Variables: 6. After Overlay: 3.
After Camera: 0 S138, still 15 S107 (same files + methods). The `warning S[0-9]+` grep also shows
any *other* new Sonar warning (e.g. S3241 from a non-`void` helper) — there must be none.

## 6. Commit groups (one PR)

- **Tests** (one commit per file group, all green on `39b0ef64`): H1+H2; C1a-d; H3+C2; H4+C3;
  H5+C4; C5.
- **G1 StreamDistribution**: (1) `RecordKioskLatency` → `UnknownMeasurement`; (2)
  `MapStreamEndpoints` per-route helpers.
- **G2 SystemVariables**: (3) `Define` → `ParseDefinition` + `CreateExecution`; (4)
  `MapSystemVariableEndpoints` helpers.
- **G3 OverlayDesigner**: (5) `CreateDraft` + `EditDraft` parsers, `CreateExecution`; (6)
  `MapOverlayEndpoints` helpers.
- **G4 CameraCatalog**: (7) `Register`; (8) `Patch`; (9) `MapCameraCatalogEndpoints` helpers
  (`writes` / `reads`).

Each commit builds **and passes** all of `tests/Architecture.Tests` on its own (rebase-merge lands
them individually, ADR-0087). Groups own disjoint production files; they still land sequentially on
one branch.

**Split fallback.** If phase 6 or CI stalls on one context, cut the PR after G3: PR A = tests +
G1-G3, PR B = G4 (CameraCatalog: the only security-ordered handler). Not recommended up front — 10
methods in 4 contexts is at, not over, the split threshold, and the test commits are shared.

## 7. Risks

| Risk | Mitigation |
|---|---|
| CameraCatalog helper parameter not named `writes`/`reads` → `EndpointScopeDeclarationTests` reads the route as unauthorized | §3.1 binds the names; the guard fails loudly, not silently |
| A guard silently stops checking a route because its antecedent left a body | §3.6 table; `EditDraft`/`Patch` keep a spelled 400; J1 names the three creates' accepted shift and the stronger route-table pin |
| `Patch` order (fab → If-Match → exactly-one → identifier → operator → parse) shifts, re-opening FR-007 enumeration | §3.4 binding order; C5 pins every adjacent pair; `CameraNonEnumerationIntegrationTests` in CI |
| `Patch` helper turns a non-`ArgumentException` throw into 400 or vice versa | only the VO `From` runs inside `BoundaryParse`; commands built outside it as today |
| Overlay null-elements precedence changes when the `try` becomes a parser | parser keeps `Ensure` first; C2 pins elements-before-name and elements-before-revision |
| `RecordKioskLatency` evidence drifts (§IV) | spec 341's 16 exact tests unmodified; `KioskMeasurementContractTests` spelling preserved |
| `whep-authorize` rate limit or `AllowAnonymous` dropped while moving the `/authorize` chain | H1 renders the rate-limit policy name and `allow-anonymous`; `StatusProducerDeclarationTests` M15 reads the 429 on that chain |
| Route table differs Debug vs Release | H1 keeps 363's `DebuggerStepThroughAttribute`/`AsyncStateMachineAttribute` filter; C1 run in both configurations |
| New helper with 5 parameters grows S107 | all helpers here have 1-4; §5 build lists S107 by method |
| `S3241` from a helper returning `RouteHandlerBuilder` | `void` helpers; §5 grep shows every Sonar warning |
| Collision with #2930 (spec 365, worktree `sse-2930`) | disjoint production files and new test file names; no csproj change here, so no merge on the Moq line |
| Stale baseline after an unrelated develop merge touching these files | rebase before PR; if C1 goes red after a rebase, recapture **only** on a pre-refactor commit and say so in the PR |
