# Plan 348: The identity context under its limits

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2890.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 12 of 16 hits by shape only; the 4 left are named with reasons (spec §2). |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| §II (no primitives on Domain models) | No Domain file changes. The one new type (J1) is private, in Application. |
| §III (no cross-context refs) | No reference added; `Shared.Contracts` untouched. |
| ADR-0050 | `Log.cs` (both) untouched; every log call keeps method, arguments and order. |
| ADR-0113 | Rotate-branch Layer-1 checks stay before any mutation; the Layer-2 `DbUpdateConcurrencyException` keeps escaping `HandleAsync`. |
| ADR-0142 / ADR-0143 | Idempotency lives in the Api layer (untouched). `AddKeycloakAdminClient` is not edited, so the token client's `RetryEveryMethod()` and its stated reason stay at their call site and the admin client stays non-retrying for `POST`. |
| ADR-0141 | Extracted refusal helpers return `Option<TError>` or `Result<T, TError>`, not a nullable. |
| ADR-0051 | `AddIdentityInfrastructure` keeps its signature; extractions are private static. |
| House rules | `HandlerDeconstructionTests`: each handler's deconstruction line stays the first statement after the guard, unchanged. `EventMetadataFabDeclarationTests`: the `new EventMetadata(...)` in `RotateWebhookClientCommandHandler` **must stay inside `HandleAsync`** (the guard scans `Handle`/`HandleAsync` bodies only, and its `Unscanned` check fails the build when a file naming a handler constructs `EventMetadata` outside one). |

**Concurrency with #2888 / #2889.** Those touch only `src/EventIngestion/**` and
`src/Automation/**` plus their own test files (`*RegistrationCharacterisationTests.cs` for their own
contexts in `tests/Architecture.Tests/Persistence/`). This plan touches only `src/Identity/**`
(non-Api) and adds differently named test files. No overlap with merged #2887/#2896 either.

## 2. Shape

One bounded context, two layers edited (Application, Infrastructure). No new public type, no new
project reference, no migration, no messaging change, no AppHost change, no Domain change.

## 3. Production shape (binding on the engineer)

Every extracted method: **<= 4 parameters (CancellationToken counts), <= 30 lines, complexity
<= 10**; every file <= 300 lines (S104 counting), verified with the grep in §5 after each commit.
Statements move verbatim; comments move with the code they explain. Method names below are
suggestions; the constraints are binding.

### 3.1 `EnrollKioskCommandHandler.HandleAsync` (hit 1)

Extract (a) the two name checks plus the `ClientId.From` try/catch into
`private static Result<ClientId, EnrollKioskError> ToClientId(string name, FabIdentifier fab)`
(same check order and messages), and (b) the representation into
`private static KeycloakClientRepresentation KioskRepresentation(ClientId clientId, FabIdentifier fab)`.
The `CreateClientAsync` try/catch may move into
`private async Task<Result<KeycloakClientCredentials, EnrollKioskError>> CreateInKeycloakAsync(KeycloakClientRepresentation representation, FabIdentifier fab, CancellationToken)`
with both catches verbatim (`KioskAlreadyEnrolled(ex.ClientId)` keeps the exception's clientId).
Order kept: empty -> segments -> clientId -> fab-scoped lookup -> create -> register -> add -> save -> log.

### 3.2 `RegisterDeviceCommandHandler.HandleAsync` (hit 2)

Same pattern as 3.1: device-type, empty, segment checks and `ClientId.From` into one static
validator returning `Result<ClientId, RegisterDeviceError>` (3 parameters: `deviceType`,
`deviceIdentifier`, `fab`); representation into a static builder (3 parameters); Keycloak call
optionally extracted. Order kept: device type -> empty -> segments -> clientId -> lookup -> create ->
register -> add -> save -> log.

### 3.3 `RotateWebhookClientCommandHandler.DisableIfRevokedSinceCommitAsync` (hit 6)

Extract the `if (result.IsFailure)` block into
`private bool AcknowledgeFailedRaceDisable(DisableWebhookClientError error, string integrationName, ClientId clientId)`
returning `true` for `WebhookClientNotFound` (logging `AlreadyDisabledConcurrentlyDuringRevokeRace`)
and `false` otherwise (logging `CouldNotDisableClientCreatedDuringRevokeRace` with the identical
`InvalidOperationException` message). The `try` around `disableWebhookClient.HandleAsync`, both
catches (the `DbUpdateConcurrencyException` detach loop first, then the
`when (ex is not OperationCanceledException)` filter) and the success log stay in place.

### 3.4 `RotateWebhookClientCommandHandler.HandleAsync` (hits 4, 5; hit 3 left)

Binding: the constructor, the guard and the deconstruction line stay as written. **The
`events.PublishAsync(new WebhookIntegrationRotatedV1(..., Metadata: new EventMetadata(...)))` call,
the `CommitAsync` after it and the `if (!disabledDuringRace)` around them stay inside
`HandleAsync`** (guard, §1). Extract:

- `private async Task<Option<RotateWebhookClientError>> RefuseIfRevokedAsync(FabIdentifier fab, string integrationName, CancellationToken)`
  holding the status lookup and the `switch` with both log calls (the first-check comment moves with it).
- `private static Option<RotateWebhookClientError> RefuseOnVersionMismatch(Option<RegisteredClientAggregate> existing, Option<int> expectedVersion, ClientId clientId)`
  holding both ADR-0113 Layer-1 refusals, in today's order (branch mismatch, then stale).
- The whole `try { if (existing) {...} else {...} } catch ... catch ...` into
  `private async Task<Result<RotationOutcome, RotateWebhookClientError>> RotateOrRegisterAsync(RotateWebhookClientCommand command, ClientId clientId, Option<RegisteredClientAggregate> existing, CancellationToken)`
  where `RotationOutcome` is a private `readonly record struct (RegisteredClientAggregate Aggregate, string ClientSecret)`.
  **The `try` must still cover both branches entirely, including both `SaveAsync` calls**, and
  the two catches keep their order and filters verbatim (`KeycloakClientAlreadyExistsException`
  -> `WebhookClientNameConflict()`; generic `when (ex is not OperationCanceledException and not
  InvalidOperationException and not DbUpdateConcurrencyException)` -> `KeycloakUnavailable(ex.Message)`).
  The branch bodies may each move into a helper called from inside that `try`
  (`RollSecretAsync(aggregate, clientId, ct)`: `Rotate(clock)` -> `SaveAsync` -> `RotateClientSecretAsync`;
  `RegisterAsync(command, clientId, ct)`: representation -> `CreateClientAsync` -> `Register` -> `Add` -> `SaveAsync`),
  with the two order comments moving with them. Inside a helper taking `command`, read the fields
  by member access or deconstruct with discards whose names match the record's own fields
  (`HandlerDeconstructionTests` reads only handler bodies, but keep the names truthful anyway).
- The webhook representation into
  `private static KeycloakClientRepresentation WebhookRepresentation(ClientId clientId, string integrationName, FabIdentifier fab)`,
  identical members and order.

Resulting `HandleAsync` order (binding): clientId parse -> revoked refusal -> fab-scoped lookup ->
`isCreate` -> Layer-1 refusal -> rotate-or-register -> (create only) post-commit re-check ->
(not disabled) publish -> commit -> `RotatedWebhookIntegration` log -> success DTO built from the
aggregate *after* `SaveAsync` (version read there).

### 3.5 `OrphanedClientSweep.ClassifyAsync` (hits 7, 8)

Introduce `private sealed class SweepTally` (J1) holding
`List<(StampedClient Client, TimeSpan Age)> Orphans` and `List<string> Unreachable`, constructed
once in `SweepAsync`; `SweepAsync` keeps `orphans.Count`/`eligible.Count` logic verbatim and passes
`tally.Unreachable` (the same list) to every `OrphanedClientSweepOutcome`.
`ClassifyAsync(StampedClient candidate, IReadOnlySet<ClientId> active, SweepTally tally, CancellationToken)`
(4). Extract the service-account read (try/catch + absent check, each branch adding to
`Unreachable` **before** logging, absent case logging with `exception: null`) into
`private async Task<Option<DateTimeOffset>> TryReadCreatedAtAsync(string clientId, SweepTally tally, CancellationToken)`.
`DisableAllAsync` may take `(SweepTally, CancellationToken)`. Order kept: parse -> active (S3) ->
age read -> grace (S4) -> append; Keycloak before Postgres (S5) untouched.

### 3.6 `IdentityInfrastructureModule.AddIdentityInfrastructure` (hit 11)

Extract private static methods in the existing order, e.g. `AddPersistenceAndClock`
(lines 47-54), the existing `AddKeycloakAdminClient()` call stays where it is,
`AddWebhookIntegrationStatusLookup` (the `HttpContextAccessor`, `CallerTokenForwardingHandler`,
typed client block with its `#pragma warning disable/restore S1075, S5332` **moved verbatim** and its
comment), `AddStartupSweeps` (both sweeps, both comments), `AddCommandHandlers` (domain event handler
+ six command-handler pairs; split if over 30 lines), `AddQueryHandlers` (**including the
idempotency store and reservation sweep exactly where they sit today, between
`ListDevicesQueryHandler` and its interface registration**), `AddRevocationSource` (the
`ListRevokedClientsQueryHandler` pair and the `Replace`), then the `AddWolverineForContext` call.
Registration **order and lifetimes are unchanged**. `AddKeycloakAdminClient`,
`ConfigureMessageHandling` and `ConfigureHttpClient` are not edited.

### 3.7 `HttpKeycloakAdminClient` (hits 12, 13, 14)

Three commits, in this order:

1. **`CreateClientAsync` (hit 13).** Extract the existence probe and replace-or-refuse block into
   `private async Task RefuseOrReplaceExistingAsync(string realm, KeycloakClientRepresentation representation, CancellationToken)`
   (probe `GET`, `IsReplaceableDisabledClient`, throw, `ReplacedDisabledClient` log,
   `TryDeleteClientAsync`; the security comment moves with it). Optionally extract the `POST` +
   409 mapping + `EnsureSuccessStatusCode` into `PostClientAsync(realm, representation, ct)`.
   **The `try`/compensation block stays in `CreateClientAsync` and still covers exactly
   group-join -> strip -> secret read**, not the `POST` and not the uuid re-probe; its filter
   (`when (exception is not OperationCanceledException)`) and the bare `throw;` are unchanged.
2. **`GetSubGroupNamesAsync` (hit 14).** Extract the paging loop (with `const int pageSize = 100`
   and its comment) into
   `private async Task<List<string>> ReadAllChildGroupNamesAsync(string realm, string parentId, CancellationToken)`.
   URL `admin/realms/{realm}/groups/{id}/children?first={names.Count}&max={pageSize}` unchanged;
   the 404 -> `None` and the "Some even when empty" behaviour unchanged.
3. **S104 (hit 12).** Mark the class `sealed partial` and move members, verbatim with their doc
   comments, into files grouped by what they hold, e.g.:
   - `HttpKeycloakAdminClient.cs`: class doc, primary constructor, `JsonOptions`, client lifecycle
     (`CreateClientAsync`, `RotateClientSecretAsync`, `ReadClientSecretAsync` both, `DisableClientAsync`)
     and their private helpers (`TryGetClientUuidAsync`, `TryGetClientRowAsync`,
     `IsReplaceableDisabledClient`, `AssignServiceAccountToGroupAsync`, `TryDeleteClientAsync`).
   - `HttpKeycloakAdminClient.Realm.cs`: `GetSubGroupNamesAsync` and its paging helper.
   - `HttpKeycloakAdminClient.Sweeps.cs`: `GetEnrolledKioskClientIdsAsync`, both
     `StripInheritedRealmRolesAsync`, `GetStampedClientsAsync`, `GetServiceAccountCreatedAtAsync`.
   - `HttpKeycloakAdminClient.WireRows.cs`: the six private records.
   Each file under 300 counted lines. No method body changes in this commit, so `git diff -M`
   shows moves only. The DI registration (`AddHttpClient<HttpKeycloakAdminClient>`) and the
   handler chain are untouched because the type is unchanged.

### 3.8 `RegisteredClientConfiguration.Configure` (hit 16)

A sequence of private static methods in today's call order: table + key + scalar properties
(`Id`, `ClientId`, `Kind`, `Fab`), the owned `Registration` (with `Navigation(...).IsRequired()` and
the #2022 comment), lifecycle columns (`DisabledAt`, `LastRotatedAt`, `Version` concurrency token),
indexes (both, with their comments), then `Ignore(PendingEvents)`.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

Coverage that already exists and is relied on (must pass unmodified):

| Member | Existing tests |
|---|---|
| EnrollKiosk handler | `EnrollKioskCommandHandlerTests`, `RuntimeClientAudienceTests` (scope subset/contains) |
| RegisterDevice handler | `RegisterDeviceCommandHandlerTests`, `RuntimeClientAudienceTests` |
| Rotate handler | `RotateWebhookClientCommandHandlerTests` (21: revoked/unverifiable refusals before lookup, cross-fab, name conflict, Layer-2 propagation with `CallCount == 0`, status called twice on create, race disable, concurrent disable, NotFound), `StaleVersionRejectionTests` (7) |
| Orphaned sweep | `OrphanedClientSweepTests` (16, incl. `Keycloak_is_read_before_postgres` via shared `CallLog`), `OrphanedClientSweepHostedServiceTests` |
| Infrastructure module | `Architecture.Tests`: `OrphanedClientSweepRegistrationTests`, `KioskPrivilegeSweepRegistrationTests`, `ResilienceRegistrationTests`, `PostgresComposition`-based suites; `WebhookIntegrationRevokedFailurePolicyTests` |
| HttpKeycloakAdminClient | `Identity.Infrastructure.Tests/KeycloakAdmin/*` over `StubKeycloakHandler` (records method, path+query, body): `CreateClientReplacementGuardTests` (5), `DisabledClientReregistrationTests` (2), `HalfEnrolledClientCompensationTests` (4, `ShouldContain` only), `KeycloakAdminSerializationTests` (3, POST/PUT bodies), `FabGroupTreeTests` (4, single page), `EnrolledKioskQueryTests` (3), `StampedClientQueryTests` (2), `ServiceAccountCreatedAtTests` (5) |
| EF configuration | none in unit tests; Integration.Tests (CI only) |

Gaps, pinned by **new files only** (no existing test or fake edited; a new recording decorator is a
new file):

| ID | File | Pins |
|---|---|---|
| C1 | `tests/Identity.Application.Tests/Fakes/RecordingIdentityPorts.cs` | Decorators over `FakeKeycloakAdminClient`, `InMemoryRegisteredClientRepository`, `FakeWebhookIntegrationStatusLookup`, `FakeEventBus` and the commit double, appending to one shared ordered `List<string>` (e.g. `"status"`, `"lookup"`, `"create"`, `"add"`, `"save"`, `"rotate-secret"`, `"publish"`, `"commit"`), delegating everything. Test infrastructure for C2-C4 |
| C2 | `tests/Identity.Application.Tests/Commands/RotateWebhookClientOrderCharacterisationTests.cs` | Rotate branch full sequence and status called once; create branch full sequence; exact `KeycloakClientRepresentation` (all members, scope list order, attribute set) and `fabGroupPath`; race-disabled path: no publish, no commit, success, `RotatedWebhookIntegration` still logged; inner disable non-NotFound failure: exact warning exception message `DisableWebhookClientCommand failed for '{name}': {code}`, then publish + commit; inner disable throwing `OperationCanceledException` propagates; Keycloak `InvalidOperationException` and `OperationCanceledException` propagate on both branches; other exception -> `KEYCLOAK_UNAVAILABLE` with message `Keycloak Admin API call failed: <msg>`; create branch Keycloak failure -> no `add`, no `save`; rotate-branch non-concurrency save failure -> `KEYCLOAK_UNAVAILABLE` and no `rotate-secret` (oddity 1, pinned as-is); success DTO fields incl. post-save version and secret |
| C3 | `tests/Identity.Application.Tests/Commands/ClientRegistrationOrderCharacterisationTests.cs` | Enrol and register: multi-fault input -> first check wins with exact message; exact representation (all members) and `fabGroupPath`; `KeycloakClientAlreadyExistsException("<other id>")` -> already-enrolled/registered naming the exception's clientId; Keycloak failure -> no `add`/`save`; success sequence lookup -> create -> add -> save; exact rendered `EnrolledKiosk` / `RegisteredDevice` log text with distinct values (`CapturingLogger`) |
| C4 | `tests/Identity.Application.Tests/KeycloakAdmin/OrphanedClientSweepTallyCharacterisationTests.cs` | One pass mixing: malformed clientId, age read throwing, absent service account, two disable outcomes (one throwing) below the mass-disable guard -> exact `Unreachable` order; per-client log entries in order with exception present/absent as today; refused pass returns the classify-phase `Unreachable` |
| C5 | `tests/Identity.Infrastructure.Tests/KeycloakAdmin/KeycloakRequestSequenceCharacterisationTests.cs` | Ordered `(Method, PathAndQuery)` list via `StubKeycloakHandler.Requests` for: fresh create; replace of a disabled same-kind same-fab client (DELETE before POST); failure after POST (group-join refused) -> compensating DELETE last, original exception type rethrown; create POST answered 409 -> `KeycloakClientAlreadyExistsException`, no further request; `RotateClientSecretAsync` (probe, `POST .../client-secret` with empty body) and its not-found and empty-body exceptions with exact message; public `ReadClientSecretAsync`; `DisableClientAsync` on an unknown client sends no PUT; `GetSubGroupNamesAsync` over two pages (100 then fewer: `first=0&max=100`, `first=100&max=100`); probe escaping (`clients?clientId=` with `Uri.EscapeDataString`, e.g. a clientId containing a space or `+`) |
| C6 | `tests/Architecture.Tests/Persistence/IdentityRegistrationCharacterisationTests.cs` | Technique of `LayoutCompositionRegistrationCharacterisationTests` (on `develop`): `PostgresComposition.Compose("identity", ...)`, ordered descriptors whose service/implementation type (recursively through generic arguments) is declared in a `SmartSentinelEye.Identity.*` assembly, rendered `"{Lifetime} {ServiceType} -> {Implementation|factory|instance}"`, equal to a literal list captured on `ed702aa5`; plus the `IRevokedClientSource` descriptors as observed on `ed702aa5` (the `Replace` leaves `LocalRevokedClientSource` as a singleton); `IHttpContextAccessor` and `TimeProvider` present. No total-count assertion |
| C7 | `tests/Identity.Infrastructure.Tests/IdentityModelCharacterisationTests.cs` | Technique of `LayoutCompositionModelCharacterisationTests` (on `develop`): `IdentityDbContext` over `UseNpgsql("Host=unused;...")`, the design-time model read through the **public metadata API** (per entity: table, key, owned; per property: column, store type, CLR type, nullability, max length, converter, concurrency flag; FKs; indexes with filter and uniqueness), equal to a literal list captured on `ed702aa5`. **Not** `Model.ToDebugString`. No csproj edit |

A characterisation test that arrives red on the unrefactored code is a test defect and is fixed in
the test, never by a production change in this spec. None of these classes is in
`Integration.Tests`, so no `ci-shards/*.filter` entry is needed. Every assertion must be able to
fail on a change to the subject without its own text changing (counterfactual: the test-writer
states, per file, one production mutation it was observed to catch, e.g. swapping two calls or
widening a `try`).

## 5. Measurement and run commands (before and after, quoted in the PR)

```sh
dotnet build src/Identity/Infrastructure/SmartSentinelEye.Identity.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep Identity
```

Before (`ed702aa5`): 16 lines (spec §2). After: the 4 "Leave" rows exactly (compare by member;
line numbers may not shift since none of the four files is edited).

```sh
dotnet test tests/Identity.Domain.Tests -c Release          # 54 passed on ed702aa5
dotnet test tests/Identity.Application.Tests -c Release     # 157 passed on ed702aa5
dotnet test tests/Identity.Infrastructure.Tests -c Release  # 63 passed on ed702aa5
dotnet test tests/Architecture.Tests -c Release             # HandlerDeconstructionTests, EventMetadataFabDeclarationTests, BoundaryTests, PrimitiveBoundaryTests, ResilienceRegistrationTests, *SweepRegistrationTests, PostgresComposition suites
dotnet format src/Identity/Application/SmartSentinelEye.Identity.Application.csproj --verify-no-changes
dotnet format src/Identity/Infrastructure/SmartSentinelEye.Identity.Infrastructure.csproj --verify-no-changes
```

Integration.Tests `Identity/*`: CI.

## 6. Risks

| Risk | Mitigation |
|---|---|
| `try` extent shrinks or grows (rotate/create, compensation) | §3.4/§3.7 binding + C2/C5 exception cases |
| Keycloak call order drifts (save-then-roll vs create-then-save; probe-delete-post) | C1/C2 shared sequence; C5 ordered request list |
| Post-commit re-check moved, skipped on create or added on rotate | Existing "status called twice" + C2 "called once" on rotate |
| `EventMetadata` moved out of `HandleAsync` | §1 binding; `EventMetadataFabDeclarationTests` fails the build |
| Unreachable list instance or order changes | §3.5 + C4 |
| DI order change altering last-wins or `Replace` | C6 ordered list |
| EF configuration reorder altering the model | C7 |
| Partial split changes a request shape | Commit 3 of §3.7 is moves only; C5 + existing stub tests |

## 7. Phase 6 security-reviewer brief

Required (security-sensitive context). Check against `origin/develop`:

1. `RotateWebhookClientCommandHandler`: the revoked/unverifiable refusal still precedes the
   fab-scoped lookup; the Layer-1 refusals still precede any mutation; the `try` covers both
   branches entirely; catch order and filters byte-identical; save-before-roll on rotate,
   Keycloak-before-save on create; the TOCTOU re-check runs on create only, after the commit, and
   suppresses publish + commit when it disabled; `DbUpdateConcurrencyException` detach loop intact.
2. `HttpKeycloakAdminClient`: per method, identical verb/path/query/body/serializer options;
   `IsReplaceableDisabledClient` unchanged (no-`sse.kind` clients never deleted, kind and fab
   matched); compensation covers only post-create steps and rethrows; no request moved outside or
   inside a `using`.
3. `AddKeycloakAdminClient` and `ConfigureHttpClient` untouched (`git diff` empty for those
   methods): the token client keeps `RetryEveryMethod()` and its reason, the admin client gets no
   retry opt-in and keeps `KeycloakAdminAuthorizationHandler`.
4. No secret (`ClientSecret`, `credentials.ClientSecret`, payload `Value`) reaches a log call, an
   exception message or a persisted column; `RegisteredClientConfiguration` still maps no secret.
5. Fab scoping: `GetWithinFabAsync` call sites, `/fabs/{fab}` group paths and `sse.fab`
   attributes unchanged.
6. No file under `Api/` or `Migrations/`, no guard, no suppression changed.
