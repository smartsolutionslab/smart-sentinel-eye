# Plan: Spec 318, the rotate that forgot the revoke

**Spec:** [`spec.md`](./spec.md) · **Tasks:** [`tasks.md`](./tasks.md) · **Issue:** #2628

## 0. Constitution and ADR check

| Principle | Status | Note |
|---|---|---|
| II. DDD with value objects | ✅ | No domain-model change. The new port lives in Application and takes Identity's own `FabIdentifier` (`Domain/RegisteredClient/FabIdentifier.cs`). The integration name stays the `string` that `RotateWebhookClientCommand` already carries. `PrimitiveBoundaryTests` scope is unaffected. |
| III. Bounded-context isolation | ⚠️ **bounded exception, as spec 017 plan §III** | This is Identity's first synchronous HTTP call to another context. It goes to EventIngestion's **published** endpoint by Aspire resource name. No `ProjectReference`, no `using SmartSentinelEye.EventIngestion.*`, and no type from EventIngestion. `BoundaryTests` (theory over `SmartSentinelEye.Identity`) stays the enforcement. See spec §5. |
| IV. Latency budget | N/A | Operator write path only. |
| VIII. Safe by default at trust boundaries | ✅ | Fails closed (spec §4). The forwarded token cannot widen access: it is the caller's own (§3). |
| ADR-0113 | ✅ | The check runs **before** Layer 1. Layer 1 and Layer 2 are otherwise untouched. |
| ADR-0142 / 0143 | ✅ | `GET` is retried by default. A refusal is `NothingCreated`. |
| ADR-0105 / 0141 / 0049 | ✅ | `Ensure.That` guards. No nullable parameter is added in Application. `CancellationToken` is the last parameter. |

**Gate: PASS**, with §III recorded as the same bounded exception spec 017 accepted.

## 1. Bounded context and layers

**Identity only, plus one AppHost line.** EventIngestion is **not changed**.

```
src/Identity/
  Application/
    WebhookIntegrations/
      IWebhookIntegrationStatusLookup.cs        NEW  port
      WebhookIntegrationStatus.cs               NEW  enum
    Commands/
      RotateWebhookClientCommand.cs             EDIT +2 error variants, +2 factory methods
      Handlers/RotateWebhookClientCommandHandler.cs  EDIT +1 ctor dependency, +1 check
    Log.cs                                      EDIT +2 [LoggerMessage]
  Infrastructure/
    WebhookIntegrations/
      EventIngestionWebhookIntegrationStatusLookup.cs  NEW  typed HttpClient adapter
      CallerTokenForwardingHandler.cs           NEW  copy of LayoutComposition's (spec 017)
    IdentityInfrastructureModule.cs             EDIT registrations
    Log.cs                                      EDIT +1 [LoggerMessage] (adapter's Unverifiable reason)
src/AppHost/AppHost.cs                          EDIT identity.WithReference(eventIngestion)
```

The folder is named for the concept (`WebhookIntegrations`), not for the other context.
`LayoutComposition/Infrastructure/Cameras/` sets the same precedent.

## 2. The cross-context call: exact shape

**Endpoint, already published by EventIngestion and unchanged:**

```
GET http://event-ingestion/webhook-integrations?fabId={fab}&includeRevoked=true
Authorization: <the incoming rotate request's header, forwarded verbatim>
→ 200 application/json: [ { "identifier", "version", "name", "fab", "defaultKind",
                            "registeredAt", "revokedAt": <ISO-8601 | null> }, … ]
```

- `fabId` is the rotation's fab: the one the rotate endpoint has **already** checked with
  `IFabAuthorizationGuard.EnsureAccessAsync` (`WebhookRotationEndpoints.cs:130`). EventIngestion
  resolves it again under its own guard (`EventIngestionFabResolution.ResolveReadFabsAsync`),
  so a fab the caller does not hold is a 403 there and `Unverifiable` here. That cannot
  happen for a caller who passed Identity's guard. If it does, the rotation is refused.
- `includeRevoked=true` is required. Without it, a revoked row is filtered out
  (`ListWebhookIntegrationsQueryHandler.cs:21-23`) and would read as `NotRegistered`, which
  is the defect in a new form. **A test pins the query string** (§8.2, A1).
- **Mapping**, by ordinal `name` match against the command's integration name:

  | Response | `WebhookIntegrationStatus` |
  |---|---|
  | 200, a row with `name == integrationName` and `revokedAt == null` | `Active` |
  | 200, such a row with `revokedAt` non-null | `Revoked` |
  | 200, no such row | `NotRegistered` |
  | non-2xx, transport failure, a resilience timeout, invalid JSON, not an array, a row missing `name` or `revokedAt` | `Unverifiable` (and a Warning naming the reason) |
  | `OperationCanceledException` **while the caller's token is cancelled** | propagates |

  Names are globally unique, revoked rows included, so at most one row matches. The adapter
  does not try to reconcile duplicates. A second match is impossible by EventIngestion's
  unique index, and if it ever happened the adapter picks `Revoked` if any matching row is
  revoked, which is still the closed direction.

- **No shared DTO.** As `CameraCatalogFabGuard` does, the adapter reads `JsonElement` by
  camelCase property name (`GetProperty("name")`, `GetProperty("revokedAt")`). A missing
  property throws inside the adapter and becomes `Unverifiable`, never a default. Putting
  `WebhookIntegrationDto` into `Shared.Contracts` would be a broader contract change than
  this bug needs (spec §3). The JSON coupling is covered by the integration facts, which
  run the real EventIngestion.
- **Why not a new endpoint?** The list answers the question under exactly the scope
  (`sse.webhooks.write`) and fab guard the rotate caller already has. A fab holds tens of
  integrations, so the payload is small. The DTO's own doc says EventIngestion
  "exposes no single-resource read — the list is the only way in"
  (`WebhookIntegrationDto.cs:9-11`). Adding one would grow EventIngestion's surface for no
  new capability.

**How the call is made, and why it is not a project reference.** The call is a typed
`HttpClient` registered in Identity.Infrastructure:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CallerTokenForwardingHandler>();
builder.Services.AddHttpClient<IWebhookIntegrationStatusLookup, EventIngestionWebhookIntegrationStatusLookup>(client =>
{
#pragma warning disable S1075, S5332
    client.BaseAddress = new Uri("http://event-ingestion");
#pragma warning restore S1075, S5332
}).AddHttpMessageHandler<CallerTokenForwardingHandler>();
```

`"event-ingestion"` is the Aspire resource name (`AppHost.cs:468`). Service discovery
rewrites the whole URI, as it does for `http://camera-catalog` in LayoutComposition
(`LayoutCompositionInfrastructureModule.cs:73-80`). The standard resilience handler from
`ServiceDefaults/Extensions.cs:44-52` applies, and a `GET` is retried with no opt-in
(ADR-0143). Identity's compile-time graph is unchanged: Domain, Application, Shared.Kernel,
Shared.Contracts, ServiceDefaults.

## 3. Credentials: the caller's own token

`CallerTokenForwardingHandler` is copied **verbatim in behaviour** from
`src/LayoutComposition/Infrastructure/Cameras/CallerTokenForwardingHandler.cs`, with its
namespace changed and its XML doc kept. It derives from
`ServiceDefaults.Authentication.AuthorizingHandler` and reads
`IHttpContextAccessor.HttpContext?.Request.Headers.Authorization`.

- No service account and no new Keycloak client. The realm and the AppHost secrets are
  unchanged.
- **No header means the call goes out unauthenticated.** EventIngestion answers 401, the
  adapter returns `Unverifiable`, and the rotation is refused. Closed by construction.
- A rotation is always handled inside an HTTP request (`WebhookRotationEndpoints.Rotate`).
  No background path issues `RotateWebhookClientCommand`, so an `HttpContext` is always
  present. `grep` confirms one construction site of the command in `src/`.

## 4. Application: port, enum, handler change

```csharp
// Application/WebhookIntegrations/WebhookIntegrationStatus.cs
public enum WebhookIntegrationStatus { Active, Revoked, NotRegistered, Unverifiable }

// Application/WebhookIntegrations/IWebhookIntegrationStatusLookup.cs
public interface IWebhookIntegrationStatusLookup
{
    Task<WebhookIntegrationStatus> GetStatusAsync(
        FabIdentifier fab, string integrationName, CancellationToken cancellationToken);
}
```

`Unverifiable` is a value, not an exception. Turning failure into a value inside the
adapter lets the fail-closed decision appear **in the handler's own code**, as a `switch`
that a unit test can drive. Otherwise it would be buried in a catch filter that has to tell
a Polly `TimeoutRejectedException` apart from a caller cancellation.

**Handler**: one new constructor parameter, `IWebhookIntegrationStatusLookup integrationStatus`,
placed after `keycloak`. One new block, placed **after** the `ClientId` parse (`:43-51`) and
**before** `GetWithinFabAsync` (`:56`):

```csharp
WebhookIntegrationStatus status = await integrationStatus
    .GetStatusAsync(fab, integrationName, cancellationToken);
switch (status)
{
    case WebhookIntegrationStatus.Revoked:
        logger.RefusedRotationOfRevokedIntegration(integrationName, fab);
        return Failure(RotateWebhookClientFailures.WebhookIntegrationRevoked(integrationName));
    case WebhookIntegrationStatus.Unverifiable:
        logger.RefusedRotationStatusUnavailable(integrationName, fab);
        return Failure(RotateWebhookClientFailures.WebhookIntegrationStatusUnavailable());
}
// Active and NotRegistered proceed exactly as before (spec §3, "Not registered").
```

Why this position:

- **After the parse**: a 400 must not cost an outbound call (spec §2, bad-request scenario).
- **Before the lookup and Layer 1**: a revoked integration answers *revoked* whatever the
  caller's precondition is (spec §2, "precedes the version checks"). It also means the
  rotate branch's `aggregate.Rotate(clock)` + `SaveAsync` (`:96-98`) can never run for a
  revoked integration, which closes spec §1's third mode.
- **Outside the existing try/catch** (`:81-167`): the lookup cannot throw except by
  cancellation, which must propagate.

The handler comment explains *why* the check runs before Layer 1, in one or two lines
(the no-drive-by-comments rule). Issue and spec numbers go in the PR, not in the code.

**Error variants**, added to `RotateWebhookClientError` in the existing doc-comment style:

| Variant | Code | Status | Message |
|---|---|---|---|
| `WebhookIntegrationRevoked(string IntegrationName)` | `WEBHOOK_INTEGRATION_REVOKED` | 409 Conflict | `Webhook integration '{IntegrationName}' has been revoked and cannot be rotated. Register a new integration under a different name.` |
| `WebhookIntegrationStatusUnavailable()` | `WEBHOOK_INTEGRATION_STATUS_UNAVAILABLE` | 502 Bad Gateway | `Could not confirm with EventIngestion that this webhook integration is not revoked, so it was not rotated. The current credential still works; retry later.` |

- 409 is the repo's status for a terminal-state refusal (`CAMERA_RETIRED`, see
  `apps/shared/src/api/problemDetail.ts:102-103`). Naming the integration discloses nothing:
  the caller named it, and the lookup is scoped to a fab the caller holds.
- The 502 message is fixed text. Unlike `KeycloakUnavailable`, it does not echo an exception
  message, because the reason is logged and an upstream's error text has no business in a
  response.
- `"Register a new integration under a different name"`: names stay taken after revocation
  (`ux_webhook_integrations_name`), so this is the only correct remedy to suggest.
- The rotate endpoint already declares `ProducesProblem` 409 and 502
  (`WebhookRotationEndpoints.cs:47, 52`). **No endpoint change.**

**Log messages** (`Application/Log.cs`, next to `RotatedWebhookIntegration`):
`RefusedRotationOfRevokedIntegration` at Information and `RefusedRotationStatusUnavailable`
at Warning, both with `{IntegrationName}` and `{Fab}`. The adapter logs its own Warning in
`Infrastructure/Log.cs` with the **reason** (status code or exception type). The handler's
Warning then needs no detail.

## 5. Entities, value objects, invariants

**None change.** `RegisteredClient` and its `Rotate` / `Disable` invariants are untouched. The
new invariant is an application-level precondition, not a domain one:

> A webhook client is never created, re-created or secret-rolled for an integration that
> EventIngestion reports revoked, or whose state EventIngestion cannot report.

It belongs in the Application layer because it depends on another context's state. The
aggregate cannot know it without I/O.

## 6. Messaging

**No new messages, no changes to existing ones.** `WebhookIntegrationRotatedV1` is still
published only on success, so a refusal publishes nothing. `WebhookIntegrationRevokedV1`
(spec 264) and its Identity subscriber are unchanged. The new check is a synchronous query,
not an event. The architecture tests' audit-coverage pair is unaffected.

## 7. AppHost

```csharp
var identity = builder
    .AddProject<Projects.SmartSentinelEye_Identity_Api>("identity")
    …
    .WithReference(keycloak)
    // comment: why, and why not WaitFor (an EventIngestion outage must stop rotation only)
    .WithReference(eventIngestion)
    .WithEnvironment("Keycloak__AdminClientSecret", identityAdminClientSecret)
    …
```

- **Reference, not `WaitFor`**, for the same reason as LayoutComposition→CameraCatalog
  (`AppHost.cs:456-464`). Identity's start, its token-adjacent endpoints, revocation-list
  serving (ADR-0160) and kiosk/device flows must not depend on EventIngestion.
- **Mutual reference.** `event-ingestion` already references `identity` through the
  ADR-0160 loop (`AppHost.cs:540-549`), so this makes the reference bidirectional. Aspire
  `WithReference` injects service-discovery configuration and imposes no start order (only
  `WaitFor` does), so a cycle of references is expected to be legal. **Unverified
  assumption.** T009 must boot the stack once and confirm it. **Fallback, if the AppHost
  rejects the cycle:** inject only the endpoint, with
  `.WithEnvironment("services__event-ingestion__http__0", eventIngestion.GetEndpoint("http"))`.
  That is the resolution service discovery reads. It is the same idea as the
  `ReferenceExpression` use at `AppHost.cs:443-446`. Record which one shipped in the PR.

## 8. Tests

### 8.1 Integration (Aspire fixture), the load-bearing reds

New class `tests/Integration.Tests/Identity/RotateRevokedWebhookIntegrationIntegrationTests.cs`.
It is modelled on `WebhookRevocationDisablesClientIntegrationTests` (register on
event-ingestion, rotate on identity, `RealmProbe`, the 20 s / 500 ms poll). Add it to a CI
shard filter (`tests/Integration.Tests/ci-shards/shard-3.filter`, next to the spec 264 class).

| # | Fact | develop | after |
|---|---|---|---|
| I1 | `An_active_integration_still_rotates` (control) | green | green |
| I2 | `A_revoked_integration_that_was_never_rotated_is_refused_and_no_client_is_created`: register, revoke, rotate `If-None-Match: *`. Assert 409 + code, then Keycloak `clients?clientId=` is empty | **red: 200** | green |
| I3 | `A_revoked_and_disabled_integration_is_not_recreated_by_a_create_intent_rotation`: register, rotate, read the Keycloak `id`, revoke, poll until `enabled=false` (control), rotate `If-None-Match: *`. Assert 409 + code, Keycloak `enabled=false`, same `id` | **red: 200, new id, enabled** | green |
| I4 | `A_revoked_and_disabled_integration_answers_revoked_to_a_rotate_intent_rotation`: as I3, but `If-Match: "<rotated version>"`. Assert 409 + code | **red: 412 WEBHOOK_CLIENT_NOT_FOUND** | green |

Assert on `title` / the problem's code, the way the existing Identity integration tests read
problem details. Use unique names per fact (`UniqueName`).

### 8.2 Infrastructure, adapter (`tests/Identity.Infrastructure.Tests/WebhookIntegrations/EventIngestionWebhookIntegrationStatusLookupTests.cs`)

The adapter gets a stub `HttpMessageHandler`, in the shape of `Fakes/StubKeycloakHandler.cs`
(or a local one if that stub is Keycloak-specific). It captures the request and answers a
canned response.

- A1 the request is `GET /webhook-integrations?fabId=munich&includeRevoked=true`;
- A2 a matching row with `revokedAt: null` gives `Active`;
- A3 a matching row with a timestamp gives `Revoked`;
- A4 no matching row, including an empty array, and a row whose name differs only by
  suffix, gives `NotRegistered`;
- A5 401, 403, 404 and 500 each give `Unverifiable`;
- A6 a body that is not JSON, not an array, or a row missing `revokedAt` gives `Unverifiable`;
- A7 the handler throws `HttpRequestException` → `Unverifiable`;
- A8 a cancelled caller token propagates `OperationCanceledException`;
- A9 a non-caller `TaskCanceledException` (a timeout, token not cancelled) gives `Unverifiable`.

`CallerTokenForwardingHandler` (copy): two facts. A header present is forwarded unchanged.
No `HttpContext`, or no header, means no `Authorization` on the outbound request. There is
no LayoutComposition test for it to mirror, so these are new.

### 8.3 Application, handler (`tests/Identity.Application.Tests/Commands/RotateWebhookClientCommandHandlerTests.cs`, added facts)

New fake `tests/Identity.Application.Tests/Fakes/FakeWebhookIntegrationStatusLookup.cs`. It
has a settable `Status` (default `Active`) and records its calls (fab, name).

- H1 `Revoked`, create intent → `WebhookIntegrationRevoked`. `keycloak.Created` is empty, the
  repository has no rows, and the bus has nothing published.
- H2 `Revoked`, an existing enabled row at V, rotate intent with V →
  `WebhookIntegrationRevoked`. No `RotateClientSecretAsync`, the row's `Version` stays V and
  `LastRotatedAt` is unchanged (spec §1's third mode).
- H3 `Revoked` with a stale version, and `Revoked` with `None` against an existing row →
  `WebhookIntegrationRevoked`, not Stale or AlreadyExists (the ordering).
- H4 `Unverifiable` → `WebhookIntegrationStatusUnavailable` (502). Same nothing-happened
  assertions as H1.
- H5 `NotRegistered`, create intent → success, as today.
- H6 invalid name → `InvalidIntegrationName`, and the lookup recorded **zero** calls.
- H7 the lookup is called with the command's fab and integration name.
- An error-shape fact for the two variants (code + status), in `RegisteredClientStaleErrorTests`'
  style if one exists for these errors, or else alongside H1/H4.

**Existing construction sites (constructor plumbing only, no assertion changes).** There
are 10:
`RotateWebhookClientCommandHandlerTests.cs` (8), `StaleVersionRejectionTests.cs:182` (1) and
`KeycloakAdmin/RuntimeClientAudienceTests.cs:196` (1). Each gains `new FakeWebhookIntegrationStatusLookup()`.
This is a test-file edit, so it belongs to **phase 4a**. Phase 4b may not touch test files.

### 8.4 Architecture

`BoundaryTests` and `PrimitiveBoundaryTests` must stay green **unmodified**. In
`ConcurrencyConflictDeclarationTests.cs:375-377`, the rotate route's mechanism text gains
"; refusal (WebhookIntegrationRevoked)". It is descriptive, and kept accurate because the
class doc counts refusal rows. Its count claims ("25 of the 33") **must be re-measured, not
edited by arithmetic**: the rotate row already says `refusal`, so the count should not
move. Verify with the three methods the doc names.

## 9. Risks

| Risk | Mitigation |
|---|---|
| The mutual `WithReference` is rejected by Aspire | The §7 fallback, decided at T009 against a real boot. |
| EventIngestion's JSON is renamed later | The parse fails closed. I2–I4 run the real JSON. The cost of drift is refused rotations, never minted ones. |
| A slow EventIngestion makes rotation slow | Bounded by the standard resilience total timeout. This is an operator action, off every SLO path. |
| An operator token forwarded to a second service | It is the same token, already presented to the gateway, with the same audience. EventIngestion is already a target of this operator's calls (`DELETE /webhook-integrations`). Nothing new is disclosed or escalated. |
