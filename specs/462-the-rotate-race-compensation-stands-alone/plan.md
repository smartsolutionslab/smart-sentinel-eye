# Plan 462: The rotate race compensation stands alone

**Spec**: [spec.md](./spec.md) · **Issue**: #3138 · **Colour**: characterisation (behaviour-preserving)

## 1. Context and layer

Identity bounded context, **Application layer only**. No Domain, Infrastructure, Api, Shared.Contracts, migration,
AppHost or DI change. No cross-context reference is introduced (NetArchTest unaffected).

## 2. Target shape

### New: `src/Identity/Application/Commands/Handlers/WebhookClientRaceCompensation.cs`

`internal sealed class WebhookClientRaceCompensation(IWebhookIntegrationStatusLookup integrationStatus,
ICommandHandler<DisableWebhookClientCommand, Result<RegisteredClientIdentifier, DisableWebhookClientError>>
disableWebhookClient, IEventBus events, ITransactionalCommit commit, TimeProvider timeProvider, ILogger logger)`

Moved verbatim (bodies unchanged except the flag→data substitution in §3):

| Moved member | Visibility after |
|---|---|
| `CompensationBound` (30 s) | private static |
| `DisableIfRevokedSinceCommitAsync(aggregate, check)` | **internal** — the handler's single entry point. Name kept so the doc comments in the existing tests (which name it) stay true without edits. |
| `DisableIfRevokedAsync`, `ShouldDisableAsync`, `RetryAfterFailedDisableAsync`, `Detach`, `PublishRetryAsync`, `AcknowledgeRaceDisableAsync`, `LogAlreadyDisabled`, `LogCouldNotDisable` | private |
| The `<summary>`/`<returns>` doc on `DisableIfRevokedSinceCommitAsync` and `RetryAfterFailedDisableAsync`, and every inline *why* comment | moved with the code |

Six parameters: S107 fires on this internal constructor too (> 4). Accepted as advisory (ADR-0084); it is the
dependency list the issue itself names. Do not bundle parameters into a record to dodge the metric.

The `ILogger` passed is **the handler's own `ILogger<RotateWebhookClientCommandHandler>` instance**, so the log
category is unchanged — `RotateWebhookClientOrderCharacterisationTests` asserts on a
`CapturingLogger<RotateWebhookClientCommandHandler>`. Do not create a logger for the new type.

### New: `src/Identity/Application/Commands/Handlers/WebhookClientRaceLogging.cs`

`internal sealed record WebhookClientRaceLogging(Action<string, ClientId> Disabled, Action<string, ClientId>
AlreadyDisabled, Action<Exception, string, ClientId> CouldNotDisable)` — the same shape as
`EventHandlers/WebhookClientDisablingLogging`, built from method groups on the existing `Log.cs` extensions:

| Branch | Disabled | AlreadyDisabled | CouldNotDisable |
|---|---|---|---|
| create (new client) | `DisabledClientCreatedDuringRevokeRace` | `AlreadyDisabledConcurrentlyDuringRevokeRace` | `CouldNotDisableClientCreatedDuringRevokeRace` |
| rotate (existing) | `DisabledClientRotatedDuringRevokeRace` | `AlreadyDisabledConcurrentlyDuringRotateRevokeRace` | `CouldNotDisableClientRotatedDuringRevokeRace` |

`RotationRecheckStatusUnverifiable` stays a direct `logger.` call inside `ShouldDisableAsync` — it exists on one
branch only.

### `RaceCheck` (moves to the new file, `internal readonly record struct`)

`RaceCheck(ClientId ClientId, FabIdentifier Fab, string IntegrationName, bool DisablesOnUnverifiable,
WebhookClientRaceLogging Logging)`. **Invariant: `DisablesOnUnverifiable == !existing.HasValue`.** Built in
`HandleAsync` from `existing.HasValue` exactly where today's `new RaceCheck(..., existing.HasValue)` is.

### Handler after

Keeps: the public primary constructor **byte-identical** (9 parameters, same order, same types),
`HandleAsync`, `PrepareAsync`, `ToClientId`, `RefuseIfRevokedAsync`, `RefuseOnVersionMismatch`,
`RotateOrRegisterAsync`, `CallKeycloakAsync`, `RollSecretAsync` (**save-before-roll order and its spec 454 comment
untouched**), `RegisterAsync`, `WebhookRepresentation`, `CredentialsOf`, `PreparedRotation`, `RotationOutcome`, the
class `<summary>`, and the unconditional `logger.RotatedWebhookIntegration(...)` (#3139 is not this issue).

**CS9124 trap.** `events`, `commit`, `integrationStatus` and `logger` are used by the handler's own members *and*
would be passed to the compensation. Initialising a field from a captured primary-constructor parameter raises CS9124,
which `TreatWarningsAsErrors` turns into a Release failure. So **construct the compensation at its call site in
`HandleAsync`** (`new WebhookClientRaceCompensation(integrationStatus, disableWebhookClient, events, commit,
timeProvider, logger)`, hoisted into a named local per the wrapping convention). It is stateless across calls — the
bound's `CancellationTokenSource` is per-call today — so per-call construction preserves behaviour, including the
single pending timer the cancellation tests wait for. Do not convert the handler to an explicit constructor.

## 3. Flag → data substitution (the only non-verbatim edits)

| Today | After | Equivalence |
|---|---|---|
| `catch (OperationCanceledException) when (isExistingClient)` | `when (!check.DisablesOnUnverifiable)` | invariant |
| `if (isExistingClient && status is Unverifiable)` | `if (!check.DisablesOnUnverifiable && status is Unverifiable)` | invariant |
| `if (isExistingClient) logger.DisabledClientRotated… else logger.DisabledClientCreated…` | `check.Logging.Disabled(integrationName, clientId)` | table above |
| `LogAlreadyDisabled` if/else | `check.Logging.AlreadyDisabled(integrationName, clientId)` | table above |
| `LogCouldNotDisable` if/else | `check.Logging.CouldNotDisable(exception, integrationName, clientId)` | table above |

The two `Log*` helpers may be inlined into the delegate call or kept as one-liners — engineer's choice; argument order
into the `[LoggerMessage]` methods must not change (`HandlerDeconstructionTests` guards record deconstruction; update
`var (...) = check;` deconstructions to the new positional order, discarding unused fields).

## 4. Messaging

Unchanged. `WebhookIntegrationRotatedV1` (integration event) is still published by the handler;
`WebhookClientRaceDisableFailed` is still published by the compensation via `IEventBus` into the outbox
(ADR-0185 durable list unchanged). Both on `CancellationToken.None`.

## 5. Boundary and metric checks (run at every refactor commit)

```sh
dotnet build SmartSentinelEye.slnx -c Release --no-incremental 2>&1 | tee metrics.log
grep -E 'RotateWebhookClientCommandHandler.cs.*warning S104|WebhookClientRace.*warning S10[47]|warning CS9124' metrics.log
dotnet test tests/Identity.Application.Tests
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~Identity|FullyQualifiedName~Rotate|FullyQualifiedName~WebhookRotation|FullyQualifiedName~HandlerDeconstruction|FullyQualifiedName~Boundary"
git diff --stat origin/develop -- tests/        # only the T002/T003 file may appear
git diff origin/develop -- src/Identity/Application/Log.cs src/Identity/Infrastructure   # must be empty
```

Done = S104 gone for the handler file; no S104 on the new files; no CS9124; every §spec covering test green and
unmodified; the only `tests/` diff is the new characterisation file. S107 on the handler constructor **remains**
(spec "Out of scope") — report it, do not chase it.

## 6. Phase 4a

Characterisation, observed green. T002/T003 are written and run **against `c683329c` unmodified production code**
and must pass there; a red result is a test defect, fixed in the test. They live in a **new** file so no existing test
file is touched: `tests/Identity.Application.Tests/Commands/RotateWebhookClientRaceCompensationCharacterisationTests.cs`
(Identity.Application.Tests is not sharded — no `ci-shards/*.filter` entry needed). Each must be shown red by
counterfactual once (G1: wrap the re-check read in a catch-all → red; G2: drop the `when` filter → red), counterfactual
reverted, and the output quoted in the PR.

## 7. Phase 6

`code-review` + `security-reviewer` (credential lifecycle): confirm every `catch` filter, the bound, the commit/no-commit
decision and the publish order moved without change, by reading the moved bodies side-by-side with `c683329c`.
