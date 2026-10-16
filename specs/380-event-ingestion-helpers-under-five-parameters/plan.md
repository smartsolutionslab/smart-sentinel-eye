# Plan 380: the EventIngestion Api helpers under five parameters

**Spec**: [spec.md](./spec.md). **Issue**: #2971. **Base**: `c0f4969d`.

## 1. Context and layers

EventIngestion, **Api layer only** (`src/EventIngestion/Api`). No Domain, Application,
Infrastructure, Shared.Kernel, Shared.Contracts or AppHost file changes. No entity, value object,
invariant, domain event or integration event is added, removed or changed. No messaging change.
Boundary rules: no new project reference; the two new records reference only types the Api project
already uses (`HttpRequest`, `IWebhookIntegrationRepository`, `ILogger`, `IMemoryCache`).

**Latency budget (constitution §IV).** Leg *Event → overlay state* (≤ 200 ms) starts at the webhook
ingest. The change adds two small record allocations and two deconstructions per webhook delivery,
and moves one command allocation in `Declare`/`Register` (admin routes, off the SLO path) from one
call site to another inside the same lambda. Nanoseconds; no re-measurement owed. All other legs: N/A.

**Constitution §II** (no primitives on domain models) does not apply: Api-layer records carrying
wire values, as `IngestManualServices` and the `Requests/` DTOs already do.

## 2. Shapes

Counted parameters after the change: H6 3, H10 3, H13 3. Every method body keeps its statements,
in order; only how the values arrive changes.

### 2.1 H6 `EventSourcesEndpoints.DeclareAsync` (6 → 3)

```csharp
private static async Task<Result<Guid, IResult>> DeclareAsync(
    DeclareSourceModeCommandHandler handler,
    DeclareSourceModeCommand command,
    CancellationToken cancellationToken)
```

Body: `handler.HandleAsync(command, cancellationToken)`, then the existing `result.Match(...)`
unchanged. Call site in `Declare` (line 163), **one line, inside the lambda**:

```csharp
token => DeclareAsync(services.Handler, new DeclareSourceModeCommand(fabResolution.Value, source, mode, declaredBy), token),
```

The command is still constructed only when `IdempotentRequest` runs the operation (not on a
replay), exactly as today. **Not hoisted into a local**: `Declare` sits at about 30 counted lines
(S138's limit) and one more line risks a new S138 hit; the line is 136 characters against
`.editorconfig`'s `max_line_length = 160`, so the house wrapping rule does not trigger.

### 2.2 H10 `EventTypesEndpoints.RegisterAsync` (5 → 3)

Same shape: `(RegisterEventTypeCommandHandler handler, RegisterEventTypeCommand command,
CancellationToken cancellationToken)`; call site (line 134):

```csharp
token => RegisterAsync(services.Handler, new RegisterEventTypeCommand(fabResolution.Value, kind, registeredBy), token),
```

Both command records are positional with no validation (`Application/Commands/*.cs`), so the move
from inside the helper to its argument list is not observable.

### 2.3 H13 `WebhookDeliveryAuthentication.AuthenticateWebhookAsync` (7 → 3) — security-sensitive

Two records, declared in `WebhookDeliveryAuthentication.cs` after the class (top-level `internal`,
as `StreamEndpoints.cs` and `LayoutEndpoints.cs` declare records beside their class):

```csharp
/// <summary>What a webhook caller presented: the route's integration name, the request carrying its bearer, and the delivery's fab.</summary>
internal sealed record WebhookDelivery(string IntegrationName, HttpRequest Request, string FabId);

/// <summary>The collaborators <see cref="WebhookDeliveryAuthentication.AuthenticateWebhookAsync"/> reads, bundled so the check keeps a readable signature (ADR-0084).</summary>
internal sealed record WebhookAuthenticationServices(
    IWebhookIntegrationRepository Integrations,
    ILogger Logger,
    IMemoryCache Cache);
```

Why two records and not one: one record of three removes two parameters (7 → 5, still a hit); the
values the caller presented and the collaborators that judge them are different things, and mixing
them in one record of six would be a bag, not a concept.

Signature and the first two body lines:

```csharp
internal static async Task<WebhookIntegration?> AuthenticateWebhookAsync(
    WebhookDelivery delivery,
    WebhookAuthenticationServices services,
    CancellationToken cancellationToken)
{
    var (integrationName, request, fabId) = delivery;
    var (integrations, logger, cache) = services;
    // ... every following line byte-identical to c0f4969d lines 36-72
}
```

The locals keep the old parameter names, so the rest of the body — the `Bearer ` ordinal prefix
check, `FindIntegrationAsync`, `IsRevokedLoggingOnce`, `IsIntegrationsOwnFab`, the mode switch to
`ValidateJwtAsync` or `TokenHash.Matches`, and every `return null` — is **textually unchanged**,
which is what the security review checks (`git diff` must show only the signature and the two
deconstruction lines in this method). The `<summary>` on the method is unchanged. No `Ensure.That`
is added (the method is `internal`, its caller is the handler; not a trust boundary — ADR-0036,
no drive-by guards).

Call site in `EventsEndpoints.IngestWebhook` (lines 189-192), replacing the `logger` local and the
two-line call:

```csharp
var integration = await WebhookDeliveryAuthentication.AuthenticateWebhookAsync(
    new WebhookDelivery(integrationName, request, fabId),
    new WebhookAuthenticationServices(integrations, loggerFactory.CreateLogger(typeof(EventsEndpoints)), cache),
    cancellationToken);
```

The logger is created in the same place in evaluation order (before the call), with the **same
category** `typeof(EventsEndpoints)`, so the revoked-delivery log record (spec 302) is unchanged.
`logger` had no other use in `IngestWebhook`. Net +1 line in `IngestWebhook` (about 27 → 28 counted
lines, under S138's 30). `IngestWebhook`'s own signature is **not** touched (FR-004).

### 2.4 What is not touched

The ten route-handler signatures (spec D-1); `IngestManualServices`, `DeclareSourceModeServices`,
`RegisterEventTypeServices`; `FindIntegrationAsync`, `IsRevokedLoggingOnce`,
`IsNewRevokedWebhookDeliveryLog`, `IsIntegrationsOwnFab`, `ValidateJwtAsync`,
`RevokedWebhookDeliveryLogWindow`; `EventIngestStore`; `Log.cs`; every Map method and chain.

## 3. Boundary and guard check

See spec §5 D-2. Summary: the only guard that reads signatures literally is
`EventIngestionRouteTableCharacterisationTests`, and it reads route handlers only, none of which
changes. The six declaration guards resolve route handlers' bodies by method-group name; in
`Declare`/`Register` the only body change is the lambda's argument list on one line, which contains
no status producer, no `If-Match` helper and keeps `IdempotentRequest.ExecuteCreateAsync`. In
`IngestWebhook` the only change is the auth call's arguments. No file is added or renamed under
`src/*/Api` (`EndpointFileCount = 16`, `EventsEndpoints` at 3 files unchanged).

## 4. Covering tests and commands (Phase 4a characterisation, observed green before; unmodified after)

All commands from `D:\Github\sse-2971`. Stop any running AppHost first (MSB3027).

**In-process (no Aspire; the primary safety net):**

```sh
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~EventSourceAndTypeEndpointCharacterisationTests|FullyQualifiedName~WebhookEndpointCharacterisationTests|FullyQualifiedName~EventIngestEndpointCharacterisationTests|FullyQualifiedName~EventIngestionRouteTableCharacterisationTests"
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~IdempotentKeyReuseDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~HandlerDeconstructionTests|FullyQualifiedName~BoundaryTests"
dotnet test tests/Architecture.Tests -c Release
```

Each `--filter` run must report a non-zero test count (a filter matching nothing exits 0, #2289).
Expected before: 28 + 32 + 28 + 2 test methods in the first run (theories expand further).

**Aspire fixture (CI's integration shards are authoritative; run locally only if the stack is
free — one machine, one stack):**

```sh
dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~SmartSentinelEye.Integration.Tests.EventIngestion.WebhookBearerValidationIntegrationTests|FullyQualifiedName~WebhookRevocationRefusesDeliveryIntegrationTests|FullyQualifiedName~RevokedWebhookDeliveryIsLoggedIntegrationTests|FullyQualifiedName~CrossFabWebhookRotationEffectIntegrationTests|FullyQualifiedName~AnonymousIngestIsRefusedTests|FullyQualifiedName~EventSourceModeIntegrationTests|FullyQualifiedName~EventSourceModeAuthorizationIntegrationTests|FullyQualifiedName~EventTypeRegistryIntegrationTests|FullyQualifiedName~EventTypeRegistryIdempotencyIntegrationTests|FullyQualifiedName~EventTypeRegistryAuthorizationIntegrationTests|FullyQualifiedName~IdempotencyKeyReuseEventIngestionIntegrationTests|FullyQualifiedName~DiscoverySourceHoldsIntegrationTests|FullyQualifiedName~StrictSourceIngestIntegrationTests"
```

No new test class is added, so no shard-filter entry is owed.

**Gate measurement** (the spec §1 command, then):

```sh
grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning S[0-9]+' metrics.log | sort -u | tr '\\' '/' | grep 'src/EventIngestion/Api'
git diff --stat origin/develop -- tests/        # must be empty
git diff origin/develop -- src/EventIngestion/Api/WebhookDeliveryAuthentication.cs   # signature + 2 lines + 2 records only
```

Before: 14 S107 under `src/EventIngestion/Api` (spec §1), no other Sonar warning there. After: 11 S107
(H1-H5, H7-H9, H11, H12, `Log.cs`), **no S138, S104, S1541 or S134** in the Api project, no new
warning of any rule.

## 5. Risks

- **A new S138 on `Declare` or `IngestWebhook`.** Mitigated by §2.1 (no hoist) and §2.3 (logger
  inlined, net +1). If one appears anyway, that hit's helper change is dropped from the slice — not
  restructured, not suppressed.
- **A security regression hidden in the webhook diff.** Mitigated by the textual-identity rule in
  §2.3 and `/security-review`; `WebhookEndpointCharacterisationTests` pins the refusal order, the
  byte-identical 401 and the log-once window.
- **Transposed fields in a deconstruction.** `WebhookDelivery` holds two `string`s
  (`IntegrationName`, `FabId`); swapping them compiles. The locals take exactly the old parameter
  names in record order, and every 201 case in `WebhookEndpointCharacterisationTests` fails on a
  swap: the lookup then runs on the fab string (no integration, or the wrong one), and if an
  integration is found its own-fab check runs against the integration name — 401 either way.
- **Spec-number race.** Re-check `specs/` on `origin/develop` and open PR heads before the PR.
