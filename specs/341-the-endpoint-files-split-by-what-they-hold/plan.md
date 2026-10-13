# Plan 341: The endpoint files, split by what they hold

**Spec**: [spec.md](./spec.md). **Issue**: #2896. **Branch**: `refactor/2896-api-endpoint-shape`

**Status**: Phase 2 gate.

## 1. Constitution and ADR alignment

| Check | Result |
|---|---|
| Spec references at least one ADR | ADR-0084 (the limits), ADR-0144 (the lane), and also 0036, 0052, 0053, 0070, 0087, 0103, 0109, 0118, 0122, 0128, 0129 and 0139. |
| New ADR needed | **No.** ADR-0084 decides the limits. The two new helper classes follow an existing pattern (`EventIngestionFabResolution`). |
| Bounded contexts | Api layers only: Automation, CameraCatalog, LayoutComposition, OverlayDesigner, StreamDistribution and SystemVariables. No Domain, Application, Infrastructure or Shared.Contracts change. No cross-context reference. |
| Entities, value objects, messaging | None touched. No domain or integration event changes. |
| Latency budget (§IV) | N/A. `RecordKioskLatency` is measurement plumbing, and what it records is pinned by a test (spec FR-006). |
| Gates | No guard edited. No suppression or threshold change. Phase 4a colour: **characterisation**. |

## 2. Per-hit design

Every hit below is resolved inside its own file or files. The resolutions are independent of
each other, which is why the tasks mark them `[P]` (file-disjoint, ADR-0109). One branch means
one index, so they still land as sequential commits.

### 2.1 `RulesEndpoints.cs`: S1541 on `BuildAction` (11) and S104 (353)

- **S1541.** Each arm of `BuildAction`'s switch becomes a named private method:
  `BuildSetVariableValue(CreateRuleRequest body)`, `BuildHighlightOverlay(...)` and
  `BuildSwitchWallScene(...)`. The `??` / `throw` lines move verbatim. `BuildAction` keeps its
  name, signature and default arm, and becomes a three-arm dispatch. The exception messages stay
  byte-identical, because they reach callers as the `400` `detail`.
- **S104.** `RulesEndpoints` becomes `public static partial class`. The read handlers `List`,
  `GetOne` and `DryRun`, together with `ResolveReadFabsAsync` (used only by those three), move
  whole to a **new** `src/Automation/Api/RulesEndpoints.Reads.cs`, with a one-line
  `/// <summary>Read handlers for <see cref="RulesEndpoints"/>.</summary>` like
  `LayoutEndpoints.Commands.cs`. `MapRulesEndpoints`, the writes, `ResolveWriteFabAsync`,
  `CreateRuleServices` and `BuildAction` stay. Projected result: about 236 lines of code.

### 2.2 `CameraEndpoints.cs`: S104 (372)

`public static partial class`. The handlers `Get` and `List`, together with
`ResolveReadFabsAsync`, move whole to a **new** `src/CameraCatalog/Api/CameraEndpoints.Reads.cs`.
`ResolveOperator` and `ResolveWriteFabAsync` stay, because the writes use them. Projected result:
about 278 lines of code.

### 2.3 `SystemVariableEndpoints.cs`: S104 (424)

`public static partial class`. The handlers `GetOne`, `List`, `GetSnapshot` and `ResolveText`,
together with `ResolveReadFabsAsync`, move whole to a **new**
`src/SystemVariables/Api/SystemVariableEndpoints.Reads.cs`. The mapping keeps its registration
order (route shadowing is order-sensitive; `SystemVariableRouteShadowingTests`). Projected
result: about 266 lines of code.

### 2.4 `LayoutEndpoints.Commands.cs`: S104 (375)

The class cannot gain a fourth partial file (spec G-b), and the five If-Match handlers must stay
in this file (spec G-c). Two non-handler helpers leave it instead:

- **New** `src/LayoutComposition/Api/LayoutCompositionFabResolution.cs` is an
  `internal static class LayoutCompositionFabResolution`. It holds `ResolveWriteFabAsync`,
  `ResolveCallerFabsAsync` and `ResolveReadFabsAsync`, moved verbatim with their doc comments.
  These three are already shared across two endpoint classes (`WallEndpoints.Commands.cs:53/125/180`
  and `WallEndpoints.Queries.cs:32/63` call `LayoutEndpoints.Resolve…`), so they belong on
  neither class. The call sites in `LayoutEndpoints.Commands.cs`, `LayoutEndpoints.Queries.cs`,
  `WallEndpoints.Commands.cs` and `WallEndpoints.Queries.cs` are re-pointed. The commit removes
  about 56 lines of code.
- **New** `src/LayoutComposition/Api/LayoutRequestParsing.cs` is an
  `internal static class LayoutRequestParsing` holding `ParseGrid` and `ParseTiles`. They move
  verbatim. Their `ArgumentException`s are still caught by the handler's own `try`. The commit
  removes about 25 lines of code.
- Projected result: about 294 lines of code. **This margin is thin**, so the engineer re-measures
  after both commits.

### 2.5 `OverlayEndpoints.Commands.cs`: S104 (320)

The class is again limited to three partial files (G-b). A **new**
`src/OverlayDesigner/Api/OverlayElementParsing.cs` is an
`internal static class OverlayElementParsing`. It holds `ParseElement`, `ParseColor`,
`ParseKind`, `ParseShapeElement`, `RequireNoTextFields` and `ParseTextElement`, moved verbatim.
The two call sites (`body.Elements.Select(ParseElement)` in `CreateDraft` and `EditDraft`) become
`OverlayElementParsing.ParseElement`, and the handler `try`/`catch` stays. Projected result:
about 251 lines of code.

### 2.6 `StreamEndpoints.cs`: S1541 on `RecordKioskLatency` (14)

The method's decisions are: the kiosk gate (1), the three switch arms (3), the closed-set
refusal `if` with two `&&` (3), the empty-camera check (1), the finite check (2), and the two
routing `if`s (2), plus 1. Two extractions take only code with **no** refusal producer
(spec G-d):

- `private static LatencySegment? SegmentFor(string measurement)` returns the switch expression,
  verbatim. Keep the arms' exact text (spec G-f).
- `private static void Record(KioskLatencyReport report, LatencySegment? segment, bool isWallSkew, bool isLabelDelay)`
  takes the `WallSkew.Record` / `LabelDelay.Record` / `LatencyBudget.Record(segment!, …)`
  dispatch, verbatim. The handler then does `Record(...); return Results.Accepted();`. The name
  `Record` is the engineer's choice and may be changed, but it must stay at four parameters or
  fewer (S107).
- The kiosk gate, the `isWallSkew` / `isLabelDelay` comparisons on `report`, and all three
  `Results.ValidationProblem` refusals **stay in the handler body**, in the same order.
- Projected complexity is about 8. The doc comments stay with the code they describe.

### 2.7 `StreamDistribution/Api/Program.cs`: S1541 (12, top-level)

A **new** `src/StreamDistribution/Api/WhepAuthorizeActionPartition.cs` is an
`internal static class WhepAuthorizeActionPartition`. It holds the four constants
(`AuthorizeRoutePath`, `MediaMtxActionPartitionItemKey`, `ApiActionPartition`,
`WhepActionPartition`), `PeekMediaMtxActionAsync(HttpContext)` and
`WhepAuthorizeActionClassification(HttpContext)`, all moved verbatim and keeping their names,
together with the comment blocks that explain them. `Program.cs` calls them qualified.

**Stays in `Program.cs`** (spec G-g): both fallback reads, the `Ensure.That` startup guards,
`IsNewWhepAuthorizeThrottleTransition`, `RateLimitBudgetFor` (it captures the four locals), the
`AddRateLimiter` block with the inline partition key, and the middleware order. These removals
take out about 7 decision points. If Sonar turns out not to count the local functions toward the
top-level figure, the engineer re-measures and may also move `IsNewWhepAuthorizeThrottleTransition`,
which captures nothing. **It must not move `RateLimitBudgetFor`, and it must not touch the
fallback lines.**

## 3. What the characterisation tests pin

### 3.1 `tests/Architecture.Tests/KioskLatencyRecordingCharacterisationTests.cs` (new)

The test builds `WebApplication.CreateBuilder([])`. It may configure the test host's
`JsonOptions` with `AllowNamedFloatingPointLiterals` (spec §6.2). It runs
`app.MapStreamEndpoints()`, takes the `RouteEndpoint` for `POST /streams/kiosk-latency`, and
invokes its `RequestDelegate` on a `DefaultHttpContext` (`RequestServices = app.Services`, a JSON
body, and a `MemoryStream` response). The pattern is the one in `DefaultEndpointsTests`. A
`MeterListener` listens on `SmartSentinelEye.Latency`, `SmartSentinelEye.WallSkew` and
`SmartSentinelEye.LabelDelay`, and the test filters on a per-test camera `Guid` because the meters
are static. The kiosk principal is `ClaimsIdentity` with claim `azp` = `AuthenticationDefaults.KioskClientId`.

| Case | Status | Recorded |
|---|---|---|
| Non-kiosk principal, valid `overlay_draw` | 202 | nothing on any of the three meters |
| Kiosk, `overlay_draw`, 42.5 | 202 | `sse.latency.segment.duration` = 42.5, with tags `segment=kiosk-overlay-draw`, `leg=overlay-composite-render`, `leg.budget_ms=50`, `segment.is_whole_leg=true`, `camera=<guid>` |
| Kiosk, `receive_to_decoded` | 202 | same instrument, `kiosk-receive-to-decoded` / `sfu-to-kiosk-decode` / 120 / false |
| Kiosk, `presentation_buffer` | 202 | same instrument, `kiosk-presentation-buffer` / `presentation-buffer` / 200 / true |
| Kiosk, `wall_skew` | 202 | `sse.wall.skew` (tags `bound_ms`, `camera`) only |
| Kiosk, `label_delay` | 202 | `sse.overlay.label_delay` (tags `cap_ms`, `camera`) only |
| Kiosk, unknown name | 400 | nothing. Body: the validation problem with key `Measurement` and the exact message |
| Kiosk, empty camera | 400 | nothing. Key `Camera`, `must name the tile's camera` |
| Kiosk, unknown name **and** empty camera | 400 | key `Measurement` only, which pins the order |
| Kiosk, NaN / +Infinity | 400 | key `ElapsedMilliseconds`, `must be a finite number of milliseconds` |
| Kiosk, negative (-4) and > 60 s | 202 | nothing (dropped by the instrument, not refused) |

**Assert the whole 400 response body as captured before the change**, with `traceId` excluded
if present, so that "byte-for-byte" means exactly that. Assert the tag set exactly, not as
"contains", because a leaked or extra tag counts as behaviour.

### 3.2 `tests/Architecture.Tests/RuleActionBuildCharacterisationTests.cs` (new)

The test invokes the private static `RulesEndpoints.BuildAction(CreateRuleRequest)` by
reflection. Its name and signature are kept by T004, and the file says why reflection is used:
the method is private, and the alternative is a full-stack test. The cases are:

- each of the three valid action types, asserting the resulting `RuleAction` subtype and its
  values;
- each missing-field refusal (`VariableName`, `ValueExpression`, `OverlayIdentifier`,
  `DurationMs`, `WallIdentifier`, `SceneTarget`), asserting the exact `ArgumentException`
  message;
- an unknown `ActionType`, asserting its exact message;
- argument order, by omitting both fields of one arm and asserting the *first* field's message.

`TargetLayoutIdentifier` is null-tolerant on `SwitchWallScene`.

## 4. Commands (before = after, verbatim into the PR)

```sh
# 0. No Aspire stack running (MSB3027 and FailedToStart otherwise).

# 1. The gate. Empty after the change.
dotnet build SmartSentinelEye.slnx -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S1541)' | sort -u | grep -E 'Api[\\/]'

# 2. Guards and the two characterisation classes (in-process, no Docker).
dotnet test tests/Architecture.Tests -c Release

#    The focused subset, for quick iteration:
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~KioskLatencyRecordingCharacterisationTests|FullyQualifiedName~RuleActionBuildCharacterisationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~IdempotentKeyReuseDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~KioskMeasurementContractTests|FullyQualifiedName~WhepAuthorize|FullyQualifiedName~SystemVariable|FullyQualifiedName~HandlerDeconstructionTests|FullyQualifiedName~ServedHealthHostCompletenessTests"

# 3. The six touched contexts' integration tests (Aspire fixture, Docker; one stack per machine).
dotnet test tests/Integration.Tests -c Release --filter "(FullyQualifiedName~SmartSentinelEye.Integration.Tests.Automation|FullyQualifiedName~SmartSentinelEye.Integration.Tests.CameraCatalog|FullyQualifiedName~SmartSentinelEye.Integration.Tests.LayoutComposition|FullyQualifiedName~SmartSentinelEye.Integration.Tests.OverlayDesigner|FullyQualifiedName~SmartSentinelEye.Integration.Tests.SystemVariables|FullyQualifiedName~SmartSentinelEye.Integration.Tests.StreamDistribution)&Category!=Measurement&Category!=Disruptive&Category!=Maintenance"

# 4. Hygiene.
dotnet build SmartSentinelEye.slnx -c Release   # 0 errors
dotnet format SmartSentinelEye.slnx --verify-no-changes
```

The Integration.Tests baseline is long (about 80 classes) and CI runs the same classes in its
four shards. If the local run is impractical, **the CI run on the PR is the after-run of
record**. The before-run is then the latest green `develop` CI run whose `.trx` covers these
classes; download the artifact first, and quote its run id. The `Architecture.Tests` before and
after runs are local and mandatory.

## 5. Risks

| Risk | Mitigation |
|---|---|
| A moved handler stops resolving because the class is not marked `partial`, or a file ends in `Endpoints.cs` | Spec G-a and G-e. `Architecture.Tests` fails loudly. Redo the split. |
| Layout misses 300 by a few lines | Re-measure after T009 and T010. If it is still over, report rather than trim by formatting: blocking beats gaming the gate. |
| Program.cs is still above 10 because Sonar counts differently | §2.7's fallback (move `IsNewWhepAuthorizeThrottleTransition`), and nothing else. |
| A characterisation test arrives red | Its premise is wrong, not the code. Fix the test before any production edit, and record it. |
| `sse-2897` also claims spec 341 | Re-check before the PR. Rename the directory if needed. |
