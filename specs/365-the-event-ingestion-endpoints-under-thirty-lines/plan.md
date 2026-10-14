# Plan 365: The event-ingestion endpoints under thirty lines

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2930.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears all 12 S138 hits by shape only. The S107 set (14) must not grow, so **every new helper takes at most 4 parameters**. |
| ADR-0144 | Phase 4a is characterisation, observed green. No gate is weakened, and there is no ADR and no constitution edit. |
| ADR-0070 | Still Minimal APIs. The mapping helpers return the same `RouteHandlerBuilder` chains. |
| ADR-0113 | `Change` still reads `If-Match` in its own body, before the parse and before the lookup. |
| ADR-0142 | Each idempotency scope keeps (route constant, caller, fab, body fingerprint) in the same order, pinned by a test. Webhook registration stays the one create without a key. |
| ADR-0105 | The existing `Ensure.That` guards stay first in each handler. The new helpers are private and receive non-null values from their callers. |
| §III (no cross-context refs) | Api-only change, and no reference is added. |
| §IV latency | Only the ingest writes are on the path. Their awaits, store and order are unchanged (spec §7). |
| House rules | `var`; one parameter per line when a call wraps; lines <= 160 characters; no drive-by comments. Existing comments move with the code they explain. |

## 2. Shape

One bounded context (EventIngestion), one layer (Api). There is no new type outside the six files
of spec §1, no new file under `src/`, no Application/Domain/Infrastructure edit, no migration, no
messaging and no AppHost change.

## 3. Production shape (binding on the engineer; the build decides the counts)

### 3.1 EventSourcesEndpoints.cs (group A)

**`Declare` (40 → ~24).**
- Replace the inline parse with
  `BoundaryParse.TryParse(() => (Source.From(body.Source), EventTypeMode.From(body.Mode)), "SOURCE_MODE_INVALID_INPUT", out var parsed, out var invalid)`.
  The tuple evaluates left to right, so the source is still parsed first. Deconstruct it with
  `var (source, mode) = parsed;`.
- Extract the scope (4 parameters):

```csharp
private static Option<IdempotencyScope> DeclareScope(
    Option<IdempotencyKey> key,
    OperatorIdentifier declaredBy,
    FabIdentifier fab,
    DeclareSourceModeRequest body) =>
    key.Map(supplied => IdempotencyScope.For(
        supplied,
        DeclareEndpoint,
        declaredBy.Value.ToString(),
        Option<string>.Some(fab.Value),
        IdempotencyFingerprint.Of(body)));
```

- The body keeps `IdempotencyHeaders.TryRead` and `return await IdempotentRequest.ExecuteCreateAsync(new IdempotentExecution(DeclareScope(...), services.Idempotency, services.Clock), …)`.
- `DeclareAsync` is untouched: it already takes 6 parameters and is one of the 14 S107 hits.

**`Change` (32 → ~29).** The route-value handler, so its inline `try/catch` with
`StatusCodes.Status400BadRequest` **stays** (spec FR-005). Extract the outcome instead, mirroring
spec 363's `RevisionOutcome`:

```csharp
private static IResult ChangeOutcome(Result<SourceModeIdentifier, ChangeSourceModeError> result) =>
    result.Match<IResult>(
        onSuccess: identifier => Results.Ok(identifier.Value),
        onFailure: error => error.ToProblem());
```

and `return ChangeOutcome(await handler.HandleAsync(changeSourceModeCommand, cancellationToken));`,
with the command hoisted into a named local (CLAUDE.md: a multi-line `new Command(...)` argument is
hoisted). If the build still reports more than 30 lines, also fold `changedBy` into the command
local. Do **not** move the `If-Match` read or the parse.

**`MapEventSourcesEndpoints` (54 → ~10).** One private helper per mapping, in the same order:
`MapDeclareSourceMode(RouteGroupBuilder group)`, `MapListSourceModes(group)`,
`MapChangeSourceMode(group)`, `MapUndeclareSourceMode(group)`. These routes have no `WithName`, so
each name is verb + noun. The existing "No scope at the group level" comment stays above the calls.

### 3.2 EventTypesEndpoints.cs (group B)

**`Register` (38 → ~24).** Use `BoundaryParse.TryParse(() => Kind.From(body.Kind), "EVENT_TYPE_INVALID_INPUT", …)`
**before** `IdempotencyHeaders.TryRead`, which is the current order. Add `RegisterScope(key, registeredBy, fab, body)`,
shaped like `DeclareScope`, with `RegisterEndpoint`. `RegisterAsync` is untouched.

**`MapEventTypesEndpoints` (42 → ~9).** `MapRegisterEventType`, `MapListEventTypes`,
`MapRetireEventType`, each taking `RouteGroupBuilder group`.

### 3.3 WebhookIntegrationsEndpoints.cs (group C)

**`Register` (31 → ~24).** Use `BoundaryParse.TryParse(() => (WebhookIntegrationName.From(body.Name), Kind.From(body.DefaultKind)), "WEBHOOK_INVALID_INPUT", …)`.
The name is still parsed first. **No idempotency is added.** The response construction (plaintext
token) is unchanged.

**`MapWebhookIntegrationsEndpoints` (32 → ~10).** `MapRegisterWebhookIntegration`,
`MapListWebhookIntegrations`, `MapRevokeWebhookIntegration`, each taking `RouteGroupBuilder group`.
`group` carries `RequireAuthorization(Scope.Sse.Webhooks.Write)` at group level, and the readers
inherit it **by the receiver's name**. Any other name leaves these routes looking unauthorized,
and `StatusProducerDeclarationTests`/`EndpointScopeDeclarationTests` go red.

### 3.4 EventsEndpoints (groups D and E)

**`MapEventsEndpoints` (70 → ~14), in `EventsEndpoints.cs` (group D).** It keeps both
`var writes = app.MapGroup("/events")…` and `var reads = app.MapGroup("/events")…`, and calls
`MapIngestManualEvent(writes)`, `MapIngestWebhookEvent(writes)`, `MapListEvents(reads)`,
`MapGetEvent(reads)`, `MapListDeadLetters(reads)` in the existing order. **The helper parameters
must be named `writes` and `reads` respectively**, because the readers bind a mapping to its group
by that name. The helpers stay in `EventsEndpoints.cs`, because the guards scan only files ending
`Endpoints.cs` and `EventsEndpoints.Writes.cs` does not. The long `// 409 is the idempotency
guard's…` comments move with their chain lines.

**`ListEvents` (40 → ~22), in `EventsEndpoints.Reads.cs` (group D).** Add a private expression-bodied
parser (3 parameters):

```csharp
private static (Source? Source, DeviceIdentifier? Device, Kind? Kind) ParseListFilters(
    string? source,
    string? deviceId,
    string? kind) =>
    (
        string.IsNullOrEmpty(source) ? null : Source.From(source),
        string.IsNullOrEmpty(deviceId) ? null : DeviceIdentifier.From(deviceId),
        string.IsNullOrEmpty(kind) ? null : Kind.From(kind));
```

Call it via `BoundaryParse.TryParse(..., "EVENT_LIST_INVALID_INPUT", …)` **before** the fab
resolution, which is the current order. The `ListEventsQuery` construction line is unchanged. The
three value objects are distinct types, so a transposition in the deconstruction is a compile error.

**`IngestManual` (57 → ~28), in `EventsEndpoints.Writes.cs` (group E, security).**
- Drop the five alias locals (`var limiter = services.Limiter;` …) in favour of `services.X`.
- Extract the admission preface (4 parameters), which keeps today's order exactly: revoked-webhook
  caller → fab resolution → storage readiness.

```csharp
private static async Task<Result<FabIdentifier, IResult>> AdmitManualCallerAsync(
    ClaimsPrincipal user,
    string? fabId,
    IngestManualServices services,
    CancellationToken cancellationToken)
```

  It returns `Results.Unauthorized()`, the fab refusal, or `FabNotProvisioned(fab)` as the
  failure. The three existing comments (ADR-0160 / #2814, spec 018 FR-006/FR-007, spec 019 FR-007)
  move into it with their checks.
- Extract `ManualEnvelope(FabIdentifier fab, IngestManualEventRequest body)`. It returns the
  `new EventEnvelope(...)` exactly as written and throws as it does today. The handler keeps the
  `try { envelope = ManualEnvelope(fab, body); } catch (ArgumentException ex) { return Results.Problem(... Status400BadRequest); }`.
- Extract `ManualScope(Option<IdempotencyKey> key, ClaimsPrincipal user, FabIdentifier fab, IngestManualEventRequest body)`,
  shaped like `DeclareScope`, with `IngestManualEndpoint` and `user.ToOperatorIdentifier().Value.ToString()`.
- The body keeps, in order: the `Ensure` guards → `IdempotencyHeaders.TryRead` → `AdmitManualCallerAsync` →
  envelope `try/catch` → `return await IdempotentRequest.ExecuteCreateAsync(...)`.

**`IngestWebhook` (34 → ~28), group E.** Extract `WebhookEnvelope(WebhookIntegration integration,
IngestWebhookEventRequest body, string fabId, IClock clock)` (4 parameters), copied verbatim
(including `FabIdentifier.From(fabId)`, see spec §8). The handler keeps its inline `try/catch` with
`StatusCodes.Status400BadRequest`, because it is a route-value handler. Authentication stays first
and storage readiness second.

**`AuthenticateWebhookAsync` (39 → ~22), group E.** The signature is unchanged (spec J4). Inside it:

- the `Bearer ` prefix check and token slice **stay first and inline**;
- extract `FindIntegrationAsync(string integrationName, IWebhookIntegrationRepository integrations, CancellationToken cancellationToken)`
  → `Task<Option<WebhookIntegration>>`. It holds the name parse (an `ArgumentException` gives `None`)
  and the lookup;
- extract `IsRevokedLoggingOnce(WebhookIntegration integration, ILogger logger, IMemoryCache cache)`
  → `bool`. It holds the `RevokedAt` pattern, the dedup check and the log call, unchanged;
- the order stays prefix → find → revoked → `IsIntegrationsOwnFab` → mode switch
  (`ValidateJwtAsync` / `TokenHash.Matches`);
- every refusal still returns `null`.

`ValidateJwtAsync`, `IsIntegrationsOwnFab`, `IsNewRevokedWebhookDeliveryLog`, `FabNotProvisioned`
and `StoreOrRefuseAsync` are untouched.

### 3.5 Helper names

These must be unique within each class (`PreconditionDeclarationTests` A1 refuses a handler name
declared twice in the class, and `EventsEndpoints` spans three files). Every name above is new and
none of them overloads a handler name (`Register`, `List`, `Change`, `Declare`, `ListEvents` …).
Do not add a fourth `EventsEndpoints` partial file (A2 pins 3), and do not add a `*Endpoints.cs`
file (the file counts pin 16).

### 3.6 Source-scanning guards: what must stay where

Each guard reads the source as text, and each constraint below is binding:

| Guard | Reads | Constraint |
|---|---|---|
| `PreconditionDeclarationTests` | `ConcurrencyHeaders.TryReadExpectedVersion` in the resolved handler body; a 22-mapping / 10-file count; the `EventsEndpoints` partial count of 3 | Stays in `Change`'s body (and in `Undeclare`/`Retire`/`Revoke`, which are untouched). |
| `IdempotentKeyReuseDeclarationTests` | `IdempotentRequest.Execute(Create)?Async(` in the handler body; the EventIngestion count is 3 | Stays in `IngestManual`, `Declare` and `Register` (event types). Webhook `Register` gets none. |
| `StatusProducerDeclarationTests` | `receiver.MapX(` bound to `receiver = x.MapGroup(`; `AllowAnonymous` in the mapping's own chain; M10 `IdempotencyHeaders.TryRead` (handler body, register) | Receivers `group`/`writes`/`reads`; `.AllowAnonymous()` stays in the webhook chain; `TryRead` stays in the three bodies. |
| `EndpointScopeDeclarationTests` | the same binding; one declaration per group name per file; exactly one `AllowAnonymous` mapping at `POST /events/webhook/{integrationName}` | No new `var x = …MapGroup` and no renamed group local. |
| `RouteValueRefusalDeclarationTests` | `StatusCodes.Status400BadRequest` (or `.BadRequest(`/`.ValidationProblem(`) in the handler body ⇒ a 400 on the chain; MapGroup sites == bound groups | Stays spelled in `Change` and `IngestWebhook` (spec J1 covers the other four handlers). |
| `ConcurrencyConflictDeclarationTests` | the prefix from the nearest preceding `MapGroup` literal in the same file; its 409 register lists EventIngestion's routes by verb + prefix + pattern | Helpers sit **below** `Map…Endpoints` in the same file. In `EventsEndpoints.cs` both groups are `/events`, so any position below is correct. |
| `HandlerDeconstructionTests` | locals named after a *different* field of the same record | Tuple locals are not record fields. Name them after what they hold. |

### 3.7 Expected S138 lines after (non-blank, non-comment)

Declare ~24, Change ~29, MapEventSourcesEndpoints ~10, Register (types) ~24, MapEventTypesEndpoints
~9, Register (webhook) ~24, MapWebhookIntegrationsEndpoints ~10, MapEventsEndpoints ~14,
ListEvents ~22, IngestManual ~28, IngestWebhook ~28, AuthenticateWebhookAsync ~22. Every helper is
at most 25 lines; the longest is `MapIngestManualEvent`'s chain, about 21.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

All tests go in `tests/Architecture.Tests` (spec J2). Add `<PackageReference Include="Moq" />` to
`SmartSentinelEye.Architecture.Tests.csproj`, after `Shouldly`, the same line and position as
#2929. The central version already exists, and the project already references all four
EventIngestion projects. `Architecture.Tests` is not sharded (only `Integration.Tests/ci-shards`),
so no shard-filter entry is needed.

| ID | File | Pins |
|---|---|---|
| C0 | `EventIngestionEndpointHost.cs` (internal) | In-process host, the same shape as #2929's `LayoutCompositionEndpointHost`: `WebApplication.CreateBuilder([])`, the four `Map*Endpoints` calls, and `SendAsync(method, pattern, user, route, bodyJson, headers, query)` → `(Status, Body, Location)` with `IHttpRequestBodyDetectionFeature`. **Real** handlers (`IngestEventCommandHandler` registered scoped, because `StoreOrRefuseAsync` resolves it from a scope; Declare/Change/RegisterEventType/RegisterWebhookIntegration/ListEvents handlers; `EventTypeAdmission`; a real `IngestWriteLimiter`; a real `MemoryCache`). **Moq** for `IFabAuthorizationGuard`, `IFabStorageReadiness`, `IIdempotencyStore`, `IWebhookIntegrationRepository`, `IEventRepository`, `IDeadLetterRepository`, `ISourceModeRepository`, `IRegisteredEventTypeRepository`, `IEventTypeAdmissionSource`, `IEventQuerySource`, `IClock` and `IAuthenticationService` (the JWT mode). `TimeProvider.System`. A hand-written capturing `ILoggerProvider` for the revoked-delivery log. `Operator(subject, fabs…)`, plus a variant with an `azp` claim. Keep it under ~250 lines. |
| C1 | `EventIngestionRouteTableCharacterisationTests.cs` | Renders the 15 `RouteEndpoint`s in data-source order: `RoutePattern.RawText`, HTTP methods, `IEndpointNameMetadata`, `IEndpointSummaryMetadata`, `ITagsMetadata`, every `IAuthorizeData` (policy/roles/schemes), `IAllowAnonymous`, every `IProducesResponseTypeMetadata` in order (status, type full name, content types), `IAcceptsMetadata`, and the handler `MethodInfo.Name` with its parameters (name:type + attribute type names). Compares against a raw-string literal captured on `c5cde739`. The renderer is a private copy of #2929's C1 approach (spec J2). |
| C2 | `EventSourceAndTypeEndpointCharacterisationTests.cs` | Spec §4.2 rows Declare, Change and Register (event types). |
| C3 | `EventIngestEndpointCharacterisationTests.cs` | Spec §4.2 rows IngestManual and ListEvents. |
| C4 | `WebhookEndpointCharacterisationTests.cs` | Spec §4.2 rows IngestWebhook + AuthenticateWebhookAsync and Register (webhook). **Security-sensitive.** Every 401 case asserts the body equals the unknown-integration body byte for byte, and verifies the `Times.Never` the order claims. |

Rules for C1-C4:

- Problem bodies are asserted exactly (string equality), never "contains".
- Collaborator calls are verified with exact arguments. Captured envelopes and scopes are asserted
  field by field, so a transposition inside a moved helper fails.
- A case that arrives **red on `c5cde739`** is a test defect: fix the test, never the product.
- If one case cannot be driven in process (a binding the host cannot satisfy), drop it, name it in
  the PR, and name the §4.3 CI test that observes it instead. Never substitute a weaker assertion.
- The fab-guard refusal is pinned exactly as `c5cde739` produces it, whether that is a problem
  result or a `FabAuthorizationException` escaping the delegate (the global handler is not in an
  in-process drive).
- `ListEvents`'s happy path is **not** required in process: `ToListAsync` needs an async query
  provider, and the filter arguments are distinct types (spec §4.2; `ListEventsFilteringTests`
  observes it in CI).

## 5. Verification commands

```sh
# S138 + S107 surface, before and after every refactor commit (stop the Aspire stack first: MSB3027)
dotnet build src/EventIngestion/Api -c Release --no-incremental -nologo -v q 2>&1 \
  | grep -E "warning S(138|107)" | grep -F 'EventIngestion\Api\' | grep -vF '\Migrations\' \
  | sed -E 's| \[.*||; s|^.*EventIngestion.Api.||' | sort -u

# characterisation + guards
dotnet test tests/Architecture.Tests -c Release
```

Before: 12 S138 + 14 S107. After each group, the S138 lines for that group's file(s) are gone and
the 14 S107 stay the same (same methods; the line numbers may move). After E: 0 S138 + 14 S107.

## 6. Commit groups (one PR)

- **Tests**: C0-C4 (+ csproj), green on `c5cde739`, one commit per file.
- **A** `EventSourcesEndpoints.cs`: Declare + Change, then the map helpers.
- **B** `EventTypesEndpoints.cs`: Register, then the map helpers.
- **C** `WebhookIntegrationsEndpoints.cs`: Register, then the map helpers.
- **D** `EventsEndpoints.cs` + `EventsEndpoints.Reads.cs`: ListEvents, then the map helpers.
- **E** `EventsEndpoints.Writes.cs` (**security**): IngestManual, then IngestWebhook +
  AuthenticateWebhookAsync.

Groups A-D are independent of each other (disjoint files) and could be authored in parallel
worktrees, but on one branch they land in sequence (one branch, one index). Each commit builds
**and passes** C1-C4 and all of `Architecture.Tests` on its own (rebase-merge lands them
individually, ADR-0087).

## 7. Risks

| Risk | Mitigation |
|---|---|
| A webhook refusal becomes distinguishable, or a lookup moves before the prefix check (security) | C4 pins identical 401 bodies and `Times.Never` per step. Group E is last and separately reviewable; phase 6 runs `/security-review`. |
| A source guard silently stops checking because its antecedent left the handler body | Tokens stay inline per §3.6. The count-based guards go red if one moves. J1 names the one accepted narrowing and the stronger pin that replaces it. |
| A mapping helper's receiver is unbound by the textual readers (spec 363 has not landed its own map helpers yet, so the pattern is unproven in this repo) | Parameters are named `group`/`writes`/`reads` and the helpers sit below the `MapGroup` in the same file. `EndpointScopeDeclarationTests`' parameter regex (`RouteGroupBuilder name`) recognises them. The guards fail loudly, not silently, if not. |
| A new helper adds an S107 | Every helper has at most 4 parameters (§3); the §5 build compares the S107 set. |
| The route-table rendering misses a metadata kind | C1 renders the full ordered metadata of the listed kinds plus the handler signature. |
| The csproj edit collides with #2929 | It is the identical line at the identical position, which git merges cleanly; check on rebase. |
| Line counts land at 31 after the build | §3 names the next lever per method. The build decides, not this table. |
| Collision with parallel work | No open PR touches `src/EventIngestion/Api`; #2929/#2933 touch other projects. Re-check at PR time. |
