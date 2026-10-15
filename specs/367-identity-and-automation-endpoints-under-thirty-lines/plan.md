# Plan 367: The identity and automation endpoints under thirty lines

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2932 (scope narrowed, spec §8).

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears the 7 Api S138 hits by shape only. S107 (10) must not grow: **every new helper takes at most 4 parameters**. |
| ADR-0144 | Phase 4a is characterisation, observed green. No gate weakened; no ADR; no constitution edit. ServiceDefaults is moved to follow-up issues, not skipped silently (spec §8). |
| ADR-0070 | Still Minimal APIs; the mapping helpers build the same `RouteHandlerBuilder` chains. |
| ADR-0113 | `Rotate` still reads `TryReadUpsertPrecondition` in its own body, after the fab guard. |
| ADR-0142 | Each scope keeps (key, route constant, caller, `Some(fab)`, fingerprint) in that order; rotation's fingerprint stays `Of(body, name, preconditionText)`. A replay still bypasses the command. Pinned by tests. |
| ADR-0114 | `Create` still calls `ResolveWriteFabAsync` before parsing; `ResolveWriteFabAsync` is untouched. |
| ADR-0105 | The existing `Ensure.That` guards stay first in each handler; new helpers are private and get non-null values. |
| §III (no cross-context refs) | Api-only; no reference added. |
| §IV latency | N/A (spec §7). |
| House rules | `var`; one parameter per line when wrapping; a multi-line `new Command(...)` argument is hoisted; no drive-by comments; existing comments move with the code they explain. |

## 2. Shape

Two bounded contexts (Identity, Automation), one layer (Api) each. No new type, no new file under
`src/`, no Application/Domain/Infrastructure/ServiceDefaults edit, no migration, no messaging, no
AppHost change.

## 3. Production shape (binding on the engineer; the build decides the counts)

### 3.1 `Identity/Api/DevicesEndpoints.cs` (group A, security)

**`Register` (58 → ~26).** In order, keeping every existing statement's position relative to the
others:
1. `Ensure` guards unchanged.
2. Drop the two alias locals (`fabGuard`, `handler`); use `services.FabGuard` / `services.Handler`.
3. `IdempotencyHeaders.TryRead(http.Request, …)` stays inline, first.
4. Fab parse: `if (!BoundaryParse.TryParse(() => FabIdentifier.From(fabId), "DEVICE_INVALID_INPUT", out var fab, out var invalid)) { return invalid; }`.
5. `await services.FabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken);` unchanged.
6. `var actingOperator = user.ToOperatorIdentifier();` unchanged.
7. `return await IdempotentRequest.ExecuteAsync(new IdempotentExecution(RegisterScope(key, actingOperator, fab, body), services.Idempotency, services.Clock), <create lambda unchanged>, (client, token) => ReplayDeviceAsync(client, body, services.Replay, token), cancellationToken);`
   The create lambda keeps `new RegisterDeviceCommand(...)` inside it (spec J4) and its comment.

New helpers (below the handler):

```csharp
private static Option<IdempotencyScope> RegisterScope(
    Option<IdempotencyKey> key,
    OperatorIdentifier actingOperator,
    FabIdentifier fab,
    RegisterDeviceRequest body) =>
    key.Map(supplied => IdempotencyScope.For(
        supplied,
        RegisterEndpoint,
        actingOperator.Value.ToString(),
        Option<string>.Some(fab.Value),
        IdempotencyFingerprint.Of(body)));

private static async Task<IResult> ReplayDeviceAsync(
    Guid client,
    RegisterDeviceRequest body,
    ReplayRegisteredClientQueryHandler replay,
    CancellationToken cancellationToken)
```

`ReplayDeviceAsync`'s body is the current replay lambda verbatim (same query, same `Match`, same
`DeviceCredentialsDto` field order, the "device type and identifier come from the retry's own
body" comment moves with it). The lambda passed to `ExecuteAsync` is a plain (non-`async`) lambda
returning the helper's task; `services.Replay` is still read inside the lambda, at replay time.

Next lever if the build says 31+: extract the create lambda's `result.Match(...)` into
`private static IdempotentOutcome RegistrationOutcome(Result<DeviceCredentialsDto, RegisterDeviceError> result)`
(spec 363's `RevisionOutcome` shape), keeping the "A refusal is recorded…" comment with it.

**`MapDevicesEndpoints` (33 → ~12).** Keep `var group = …`, then `MapRegisterDevice(group);
MapDisableDevice(group);`, then `var reads = …`, then `MapListDevices(reads);`. Each helper is
`private static void Map…(RouteGroupBuilder group|reads)` with its chain byte-identical. The
"403 is declared on all three…" comment stays above the first two calls; the "409 because…"
comment moves into `MapDisableDevice`.

### 3.2 `Identity/Api/KiosksEndpoints.cs` (group B, security)

Identical to 3.1 with: `"KIOSK_INVALID_INPUT"`, `EnrollScope(key, actingOperator, fab, body)` over
`EnrollKioskRequest` and `EnrollEndpoint`, and
`ReplayKioskAsync(Guid enrolled, ReplayRegisteredClientQueryHandler replay, CancellationToken cancellationToken)`
(3 parameters; the "every field of a kiosk's answer is server-held" comment moves with it).
Map helpers: `MapEnrollKiosk(group)`, `MapDisableKiosk(group)`, `MapListKiosks(reads)`.

### 3.3 `Identity/Api/WebhookRotationEndpoints.cs` (group C, security; after A and B)

**`Rotate` (63 → ~28).** Order, unchanged: key → fab parse → guard → upsert precondition → dispatch.
1. `Ensure` guards unchanged; drop the two alias locals.
2. `IdempotencyHeaders.TryRead(request, …)` inline, first.
3. `BoundaryParse.TryParse(() => FabIdentifier.From(body.FabId), "WEBHOOK_INVALID_INPUT", out var fab, out var invalid)`.
4. The "guard runs before the precondition" comment, then `await services.FabGuard.EnsureAccessAsync(…)`.
5. The upsert comment, then `ConcurrencyHeaders.TryReadUpsertPrecondition(request, out var expectedVersion, out var precondition)` **inline**.
6. `var actingOperator = user.ToOperatorIdentifier();`
7. The long "A replay never reaches the precondition…" comment stays here, above the dispatch.
8. `return await IdempotentRequest.ExecuteAsync(new IdempotentExecution(RotateScope(key, actingOperator, fab, RotateFingerprint(body, name, expectedVersion)), services.Idempotency, services.Clock), <create lambda>, (rotated, token) => ReplayRotationAsync(rotated, name, services.Replay, token), cancellationToken);`

New helpers:

```csharp
private static IdempotencyFingerprint RotateFingerprint(
    RotateWebhookClientRequest body,
    string name,
    Option<int> expectedVersion) =>
    IdempotencyFingerprint.Of(
        body,
        name,
        expectedVersion.Match(
            some: version => version.ToString(CultureInfo.InvariantCulture),
            none: () => "*"));

private static Option<IdempotencyScope> RotateScope(
    Option<IdempotencyKey> key,
    OperatorIdentifier actingOperator,
    FabIdentifier fab,
    IdempotencyFingerprint fingerprint) =>
    key.Map(supplied => IdempotencyScope.For(
        supplied,
        RotateEndpoint,
        actingOperator.Value.ToString(),
        Option<string>.Some(fab.Value),
        fingerprint));

private static async Task<IResult> ReplayRotationAsync(
    Guid rotated,
    string name,
    ReplayRegisteredClientQueryHandler replay,
    CancellationToken cancellationToken)
```

**Evaluation-time note (security, checked in review):** today `preconditionText` and the
fingerprint are computed eagerly, before `ExecuteAsync`, and the scope inside `key.Map`. After the
change, `RotateFingerprint(...)` is evaluated eagerly as an argument, before `RotateScope`; the
fingerprint is therefore computed even when no key was sent. `IdempotencyFingerprint.Of` is pure
(`Ensure` on a body already guarded non-null, `JsonSerializer` over the request DTO, SHA-256), so
the only effect is one unneeded hash on an unkeyed rotation. It is called out so a reviewer checks
it rather than finds it. Passing the three inputs through `RotateScope` instead would need 6
parameters (an S107); the plan's choice is the eager fingerprint.

The create lambda keeps `new RotateWebhookClientCommand(name, fab, actingOperator, expectedVersion)`
inside it. Next lever if 31+: `RotationOutcome(Result<WebhookClientCredentialsDto, RotateWebhookClientError>)`.

`MapWebhookRotationEndpoints` and `List` are untouched (under the limit).

### 3.4 `Automation/Api/RulesEndpoints.cs` (group D)

**`Create` (52 → ~28).** Order unchanged: key → fab → parse → create.
1. `Ensure` guards; drop alias locals.
2. `IdempotencyHeaders.TryRead(http.Request, …)` inline.
3. `ResolveWriteFabAsync(user, fabId, services.FabGuard, cancellationToken)` and its failure return, unchanged.
4. `if (!BoundaryParse.TryParse(() => ParseRule(body), "RULE_INVALID_INPUT", out var parsed, out var invalid)) { return invalid; }`
   then `var (name, predicate, action, triggerSource, triggerKind) = parsed;`. The five value
   objects are distinct types, so a transposition is a compile error at the command constructor.
5. `var actingOperator = …; var createRuleCommand = new CreateRuleCommand(fab, name, triggerSource, triggerKind, predicate, action, actingOperator);`
6. The "location names the rule…" comment, then
   `return await IdempotentRequest.ExecuteCreateAsync(new IdempotentExecution(CreateScope(key, actingOperator, fab, body), services.Idempotency, services.Clock), _ => $"/rules/{name.Value}", token => CreateAsync(services.Handler, createRuleCommand, token), cancellationToken);`

New helpers:

```csharp
private static (RuleName Name, RulePredicate Predicate, RuleAction Action, TriggerSource TriggerSource, TriggerKind TriggerKind) ParseRule(
    CreateRuleRequest body) =>
    (
        RuleName.From(body.Name),
        RulePredicate.From(body.Predicate),
        BuildAction(body),
        TriggerSource.From(body.TriggerSource),
        TriggerKind.From(body.TriggerKind));
```

The tuple literal evaluates left to right, which is today's statement order. `CreateScope` is
shaped like `RegisterScope` with `CreateEndpoint` and `CreateRuleRequest`.
`CreateAsync(CreateRuleCommandHandler handler, CreateRuleCommand command, CancellationToken cancellationToken)`
returns `Task<Result<Guid, IResult>>` with today's `Match` verbatim. `BuildAction` and its three
`Build…` helpers keep their names and signatures (`RuleActionBuildCharacterisationTests` binds by
reflection).

**`MapRulesEndpoints` (62 → ~14).** `var group = …`; `MapCreateRule(group); MapPublishRule(group);
MapArchiveRule(group);`; the "Reads are a separate group" comment; `var reads = …`;
`MapListRules(reads); MapGetRule(reads); MapDryRunRule(reads);`. The dry-run comment moves into
`MapDryRunRule`. All helpers stay in `RulesEndpoints.cs` (the guards scan only files ending
`Endpoints.cs`; `RulesEndpoints.Reads.cs` does not), below `MapRulesEndpoints`.

### 3.5 Source-scanning guards: what must stay where

| Guard | Reads | Constraint |
|---|---|---|
| `IdempotentKeyReuseDeclarationTests` | `IdempotentRequest.Execute(Create)?Async(` in the handler body; endpoint count 12, file count 12 | Stays in `Register`, `Enroll`, `Rotate`, `Create`. |
| `StatusProducerDeclarationTests` | `receiver.MapX(` bound to `receiver = x.MapGroup(`; M10 `IdempotencyHeaders.TryRead` in the handler body | Helper parameters named `group` / `reads`; `TryRead` stays in the four bodies. |
| `EndpointScopeDeclarationTests` | the same binding; one declaration per group name per file | No new or renamed `MapGroup` local. |
| `PreconditionDeclarationTests` | `ConcurrencyHeaders.TryRead(ExpectedVersion|UpsertPrecondition)` in the resolved handler body; handler-name uniqueness per class | `TryReadUpsertPrecondition` stays in `Rotate`. Helper names are new per class and never a handler name (`Register`, `Enroll`, `Rotate`, `Disable`, `List`, `Create`, `Publish`, `Archive`, `GetOne`, `DryRun`). Per-class names differ (`ReplayDeviceAsync` / `ReplayKioskAsync` / `ReplayRotationAsync`). |
| `RouteValueRefusalDeclarationTests` | `StatusCodes.Status400BadRequest` in the handler body ⇒ 400 on the chain | Stops demanding for the four bodies (spec J3); §4.1 pins the 400s. `Disable` (route value) is untouched. |
| `ConcurrencyConflictDeclarationTests` | prefix from the nearest preceding `MapGroup` literal in the same file | Helpers sit below `Map…Endpoints`; both groups per file share the prefix. |
| `HandlerDeconstructionTests` | locals named after a different field of the same record | The `ParseRule` tuple is not a record; locals named after what they hold. |

### 3.6 Expected S138 after (non-blank, non-comment)

Register ~26, MapDevicesEndpoints ~12, Enroll ~25, MapKiosksEndpoints ~12, Rotate ~28,
MapRulesEndpoints ~14, Create ~28. Every helper at most ~20 lines. The build decides; §3 names
the next lever per method.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

All in `tests/Architecture.Tests` (already references both Api projects and Moq; not sharded, so no
shard-filter entry). Before writing, apply spec J2: if `RouteTableRendering.cs` and
`InProcessEndpointDriver.cs` exist on `origin/develop`, rebase and reuse them; otherwise private
copies with no shared names.

| ID | File | Pins |
|---|---|---|
| C0 | `IdentityEndpointHost.cs` (internal) | In-process host shaped like `EventIngestionEndpointHost`: `WebApplication.CreateBuilder([])`, `MapDevicesEndpoints`/`MapKiosksEndpoints`/`MapWebhookRotationEndpoints`, `SendAsync(method, pattern, user, route, bodyJson, headers, query)` → `(Status, Body, Location)`. **Real** `RegisterDeviceCommandHandler`, `EnrollKioskCommandHandler`, `RotateWebhookClientCommandHandler`, `ReplayRegisteredClientQueryHandler` and the three list/two disable handlers (route table). **Moq**: `IRegisteredClientRepository`, `IKeycloakAdminClient`, `IWebhookIntegrationStatusLookup`, `IEventBus`, `ITransactionalCommit`, `IClock`, `ICommandHandler<DisableWebhookClientCommand, …>`, `IFabAuthorizationGuard`, `IIdempotencyStore`; `TimeProvider.System`; `NullLogger<>`. `Operator(subject, fabs…)`. |
| C0' | `AutomationEndpointHost.cs` (internal) | Same shape for `MapRulesEndpoints`. Real `CreateRuleCommandHandler` (Moq `IRuleRepository`, `IClock`); the other rule handlers registered with Moq'd ports for the route table; Moq `IFabAuthorizationGuard`, `IIdempotencyStore`. |
| C1 | `IdentityRouteTableCharacterisationTests.cs` | 8 `RouteEndpoint`s in data-source order: `RoutePattern.RawText`, HTTP methods, `IEndpointNameMetadata`, `IEndpointSummaryMetadata`, `ITagsMetadata`, every `IAuthorizeData`, `IAllowAnonymous`, every `IProducesResponseTypeMetadata` in order, `IAcceptsMetadata`, handler `MethodInfo.Name` + parameters (name:type + attribute type names). Raw-string literal captured on `3b10a210`. |
| C2 | `AutomationRouteTableCharacterisationTests.cs` | The same rendering for the 6 rule routes. |
| C3 | `DeviceAndKioskEndpointCharacterisationTests.cs` | Spec §4.2 rows Register and Enroll. **Security.** |
| C4 | `WebhookRotationEndpointCharacterisationTests.cs` | Spec §4.2 row Rotate. **Security.** Every precondition and guard-order case verifies `IIdempotencyStore` `Times.Never`; the replay case verifies `IKeycloakAdminClient.CreateClientAsync` and `RotateClientSecretAsync` `Times.Never`. |
| C5 | `RuleCreateEndpointCharacterisationTests.cs` | Spec §4.2 row Create. |

Rules for C1-C5:

- Problem bodies asserted exactly (string equality), never "contains".
- Scopes captured from `IIdempotencyStore.BeginAsync` and asserted field by field; the rotation
  fingerprint is compared to `IdempotencyFingerprint.Of(body, name, "*")` / `Of(body, name, "3")`
  computed in the test, so a transposed input fails.
- A case red on `3b10a210` is a test defect: fix the test, never the product.
- The fab-guard refusal is pinned as `3b10a210` produces it in process (a `FabAuthorizationException`
  escaping the delegate, or a problem), whichever it is.
- A case that cannot be driven in process is dropped, named in the PR with its §4.3 CI observer.

## 5. Verification commands

```sh
# stop any running Aspire stack first (MSB3027)
for p in Identity/Api Automation/Api; do
  dotnet build src/$p -c Release --no-incremental -nologo -v q 2>&1 \
    | grep -E "warning S(138|107)" | sed -E 's# \[.*##'
done | grep -v Migrations | grep -E '(Identity|Automation)[\\/]Api[\\/]' \
  | sed -E 's#^.*[\\/]src[\\/]##' | sort -u

dotnet test tests/Architecture.Tests -c Release
```

Before: 7 S138 + 10 S107 in the two Api projects. After each group, that group's S138 lines are
gone and the S107 set is unchanged (same methods; line numbers may move). After D: 0 S138 + 10 S107.

## 6. Commit groups (one PR)

- **Tests**: C0, C0', C1-C5, green on `3b10a210`, one commit per file (hosts first).
- **A** `DevicesEndpoints.cs` (security): Register, then the map helpers.
- **B** `KiosksEndpoints.cs` (security): Enroll, then the map helpers.
- **C** `WebhookRotationEndpoints.cs` (security): Rotate.
- **D** `RulesEndpoints.cs`: Create, then the map helpers.

A, B, C and D own disjoint files. On one branch they land in sequence: D, A, B, C — the
non-security group first, rotation (the most delicate) last. Each commit builds **and passes**
all of `Architecture.Tests` on its own (ADR-0087).

## 7. Risks

| Risk | Mitigation |
|---|---|
| A replay starts re-running the command, or a secret reaches a new place (security) | C3/C4 replay cases verify the Keycloak create/rotate `Times.Never` and assert the response body field by field; FR-004; `/security-review`. |
| Rotation's guard and precondition swap, or the fingerprint's inputs change | C4 pins guard-before-428 and both fingerprints against independently computed values. |
| Eager fingerprint evaluation in `Rotate` (§3.3 note) | Pure function; called out for the reviewer. |
| A guard silently stops checking because its token left the handler body | Tokens stay inline per §3.5; the count-based guards go red if one moves. |
| A mapping helper's receiver is unbound by the textual readers | Parameters named `group` / `reads`; helpers below the `MapGroup` in the same file (proven by specs 363/365 on develop). |
| A new helper adds an S107 | Every helper ≤ 4 parameters; §5 compares the S107 set. |
| The route-table rendering misses a metadata kind | C1/C2 render the full listed metadata plus the handler signature. |
| Collision with #2931 | Different `src/` projects; test file names distinct; J2 decides the shared helpers at 4a start. Re-check at PR time. |
| Scope narrowing is lost | SC-005: the three follow-up issues are filed before the PR; the PR body states the narrowing. |
