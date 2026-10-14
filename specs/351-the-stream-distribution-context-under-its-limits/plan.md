# Plan 351: The stream distribution context under its limits

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2893.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 9 of 12 hits by shape only; the 3 left are named with reasons (spec §2). |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| §II (no primitives on Domain models) | No Domain file changes. No new type at all. |
| §III (no cross-context refs) | No reference added; `Shared.Contracts` untouched. |
| §IV (latency) | No leg touched (spec §7). The two per-viewer-connect members get synchronous helpers only (FR-004). |
| ADR-0050 | Both `Log.cs` untouched; every log call keeps method, arguments and order. |
| ADR-0143 | `AddMediaMtxGateway` keeps `.RetryEveryMethod()` chained on its `AddHttpClient` with the "Its two non-idempotent methods are idempotent in fact" comment directly above it; `BindStreamFabAttribution` (the token client's opt-in) is not edited. |
| ADR-0161 / ADR-0160 / ADR-0171 | Fab refusal, revocation refusal and the control-API Basic header move verbatim (or not at all); pinned by C1, C4, C5. |
| ADR-0141 | Extracted refusal helpers return `Option<TError>` (a `readonly struct`, so no allocation). |
| ADR-0051 | `AddStreamDistributionInfrastructure` and `AddMediaMtxGateway` keep their signatures; extractions are private static. |
| House rules | Fluent DI chains in extracted registration helpers; `var`; one parameter per line when wrapping; handlers keep their deconstruction line as written. |

### 1.1 Guards that read this context's source, and the shapes they tolerate

| Guard | What it reads | Constraint on this refactor |
|---|---|---|
| `HandlerDeconstructionTests` | `public async Task Handle(Async)(T x,` bodies; first `var (..) = x;` | `RepointStreamCommandHandler`'s `var (camera, rtspSourceUrl) = command;` is checked: keep it verbatim as the first statement after the guard. The other two handlers use `(var a, var b) = command;`, which the regex does not match (spec §6 oddity 4); keep verbatim anyway. Extracted helpers are `private`, not `public async Task Handle*`, so the guard ignores them. |
| `EventMetadataFabDeclarationTests` | `Handle`/`HandleAsync` bodies constructing `new EventMetadata(`; `Unscanned` fails a handler file constructing it elsewhere | None of the touched handlers constructs `EventMetadata`. `StreamHealthChangedDomainEventHandler` (which does) is not touched. Do not add one. |
| `PrimitiveBoundaryTests` | Domain model state | Domain untouched. |
| `ResilienceRegistrationTests` | every `src/**/*.cs` code line (comments stripped) for `AddStandardResilienceHandler(` | The comment in `AddMediaMtxGateway` names it in `//` prose; it stays a comment. No call added. |
| `ConcurrencyConflictDeclarationTests`, `EndpointScopeDeclarationTests`, `StatusProducerDeclarationTests`, `RouteValueRefusalDeclarationTests`, `KioskMeasurementContractTests`, `WhepAuthorizeCeilingTests`, `WhepAuthorizePartitionKeyTests` | `src/*/Api/**` (`StreamEndpoints.cs`, `Program.cs`, `appsettings.json`) | No Api file is touched. |
| `OutboxCommitTests`, `BoundaryTests` | assemblies | No reference added. |
| `PostgresComposition`-based suites (`BoundedConnectionString*`, `DependencyHealthCheckGuardTests`, `ExecutionStrategy*`, `PostgresMetrics*`, `PostgresTracing*`) | the composed container for `"stream-distribution"` | Registration order and lifetimes unchanged (C5). |

## 2. Shape

One bounded context, two layers edited (Application, Infrastructure). No new public type, no new
project reference, no migration, no messaging change, no AppHost change, no Domain change.

**Concurrency with #2891 / #2892.** Those touch only `src/SystemVariables/**` and
`src/OverlayDesigner/**` plus their own `SystemVariables*`/`OverlayDesigner*` test files. This plan
touches only `src/StreamDistribution/**` (non-Api), adds `StreamDistribution*`-named test files, and
appends one entry to `tests/Integration.Tests/ci-shards/shard-2.filter`, which neither sibling
edits (checked on both worktrees on 2026-10-14). The filter is a single line, so a concurrent edit by
any other branch conflicts textually: rebase, keep both entries. No overlap with the merged siblings
(341, 342, 345-348) or #2896's Api split.

## 3. Production shape (binding on the engineer)

Every extracted method: **<= 4 parameters (CancellationToken counts), <= 30 lines, complexity
<= 10, nesting <= 3**; every touched file stays under S104, verified with the grep in §5 after each
commit. Statements move verbatim; comments move with the code they explain (the list per section is
binding: a comment that says "above", "below" or "see below" is re-read in its new place). Method
names below are suggestions; the constraints are binding.

### 3.1 `AuthorizeWhepCommandHandler.HandleAsync` (hits 1, 2): per viewer-connect

The guard, the `(var path, var bearerToken, var action, var reportedAction) = command;` line, the
`RequiredScope` const and its doc, `RefusalFor` and `RefuseUnknownAction` stay as written. Extract:

- `private Option<AuthorizeWhepError> RefuseByAction(Option<MediaMtxAction> action, Option<ReportedMediaMtxAction> reportedAction, MediaMtxPath path)`
  holding, in today's order, the `!action.HasValue` branch (calls `RefuseUnknownAction`, returns
  `ActionUnknown()`) then the `Publish || Api` branch (`logger.RefusedWhepAction`, returns
  `ActionNotPermitted()`), else `None`. The "Nothing in this product publishes through this hook..."
  comment moves with the `Publish || Api` check.
- `private Option<AuthorizeWhepError> RefuseByStream(Option<Stream> stream, WhepAuthSubject subject, MediaMtxPath path)`
  holding the fab check (condition byte-identical, `logger.RefusedWhepFab(subject.Subject, path, stream.Value.Fab)`,
  `FabNotAuthorized()`) **then** the offline check (`StreamUnavailable()`), else `None`. The ADR-0161
  comment block (including its "(see below)", which points at its own last three lines) moves with
  the fab check.

`HandleAsync` keeps, in this order: the "The action is answered before the token, and that order is
the point" comment **directly above** the `RefuseByAction` call and its early return; the blank-token
check; `await whepAuth.ValidateAsync`; the `IsFailure -> RefusalFor` return; `subject`; the scope
check; `await streams.GetByPathAsync`; the `RefuseByStream` call and early return; `Success(path)`.
Both helpers are synchronous: **no new `await`, no lambda, no allocation** (FR-004).

### 3.2 `RepointStreamCommandHandler.HandleAsync` (hit 3)

Required: extract the existence and retirement checks into
`private Option<Stream> StreamToRepoint(Option<Stream> existing, CameraIdentifier camera)`:
`!existing.HasValue` -> `logger.NoStreamToRepoint(camera)`, `None`; `State == Retired` ->
`logger.SkippedRepointOfRetiredStream(camera)`, `None`; else `Some(existing.Value)`. Both branch
comments ("Not a failure...", "The camera's address changed after...") move with their branches.
`HandleAsync` answers `Success(Option<StreamIdentifier>.None)` when it is `None`.
Optional, if still over 30: the `StreamSourceUrl.From` try/catch into
`private static Result<StreamSourceUrl, RepointStreamError> ParseSource(string rtspSourceUrl)` with the
"Re-validated at the trust boundary" comment; the parse still happens **before** the lookup.
Stays in `HandleAsync`: `RepointTo`, the "Saved before the gateway is touched" comment directly above
`SaveAsync`, the gateway `try`/`catch (HttpRequestException)` with its comment, the success log.

### 3.3 `ReportStreamHealthCommandHandler.HandleAsync` (hit 4): ~2 s health sweep

Extract the body of the `try` (the `if (declareOffline) / else if (observation.IsReady) / else`
chain, both default-text literals verbatim) into
`private void ApplyObservation(Stream stream, RtspPathHealth observation, bool declareOffline)`
(adjust the observation type name to what the record declares) and call it **inside** the existing
`try`, so the `catch (InvalidOperationException)` still covers every transition. The `catch` body,
`DescribeTarget(command)`, `SaveAsync` after the `try` and `Success(stream.State)` stay as written.
Do not add a `catch` around `SaveAsync` (#2783, spec §6 oddity 3).

### 3.4 `WhepAuthValidator.ValidateAsync` (hit 7): per viewer-connect

Stays in `ValidateAsync`: the metadata `try { await oidc.GetConfigurationAsync } catch (InvalidOperationException)`
with its whole "Narrow on purpose..." comment, the `Interlocked.Exchange(ref realmUnreachable, 1)`
transition log and the `IdentityProviderUnavailable` return; then the `Interlocked.Exchange(ref realmUnreachable, 0)`
recovery log; then `return ValidateAgainst(configuration, bearerToken);`. Extract (both synchronous,
FR-004):

- `private Result<WhepAuthSubject, WhepAuthFailure> ValidateAgainst(OpenIdConnectConfiguration configuration, string bearerToken)`
  holding the second `try` (clone, `ValidIssuers`, `IssuerSigningKeys`, `handler.ValidateToken`,
  `return SubjectFrom(principal, validated);`) and **both catches in today's order with today's
  filters and bodies** (`SecurityTokenException` -> `RequestRefreshIfAStaleDocumentCouldExplain` ->
  `TokenRejected`; `ArgumentException` -> `TokenRejected`, comment verbatim).
- `private Result<WhepAuthSubject, WhepAuthFailure> SubjectFrom(ClaimsPrincipal principal, SecurityToken validated)`
  holding, in today's order: `sub` absent -> `TokenRejected`; the "Spec 270 (ADR-0160)" comment, `azp`,
  `revokedClients.Refuses(azp, IssuedAtOf(validated))` -> `TokenRefusedAsRevoked(azp ?? string.Empty)`
  -> `TokenRejected`; scope split (`' '`, `RemoveEmptyEntries`); the "ADR-0161" comment and
  `FabClaims.AssignedFabs(principal)`; the `Success(new WhepAuthSubject(subject, scopes, fabs))`.
  Called from inside `ValidateAgainst`'s `try`, so an exception thrown while reading claims is still
  caught by exactly the same two filters.

Placement (binding): `ValidateAgainst` and `SubjectFrom` sit **after `ValidateAsync` and before
`RequestRefreshIfAStaleDocumentCouldExplain`**, so the field comment's "the ArgumentException catch
below" and the `ArgumentException` comment's "never reaches the discriminator below" stay true.
Constructors, `MetadataSourceFor`, `CreateParameters`, `IssuedAtOf` and
`RequestRefreshIfAStaleDocumentCouldExplain` are not edited.

### 3.5 `StreamConfiguration.Configure` (hit 9)

A sequence of `private static` methods called in today's order, each taking the
`EntityTypeBuilder<Domain.Stream.Stream>`: table + key + `Id` + `Camera` + `Fab` (both Fab comments
move with it); `Path`, `SourceUrl`, `State`, `TranscodeMode`; `LastSuccessAt`, `LastError`; the owned
`Provisioning` **with** `builder.Navigation(...).IsRequired()` in the same method, after the
`OwnsOne` (its "Owned reference... `Navigation(...).IsRequired()` below is load-bearing" comment stays
above the `OwnsOne`, so "below" stays true); `Version` concurrency token; the three indexes (both
comments); then `Ignore(PendingEvents)` (may stay in `Configure`). `Ensure.That(builder)` stays first
in `Configure`. Every literal (`"streams"`, column names, `HasMaxLength(80)`, `16`, index names)
unchanged.

### 3.6 `MediaMtxReconciler.ReconcileOnceAsync` (hit 10): startup only

Stays in `ReconcileOnceAsync`: the scope, the factory and gateway resolution, the `await using`
context, the query with its "Anonymous type, not ValueTuple.Create" comment, `expected`,
`configured`, then
`var removed = await RemoveOrphanPathsAsync(gateway, configured, expected, cancellationToken);`
then `var readded = await ReaddMissingPathsAsync(gateway, configured, streams.Select(entry => (entry.Path, entry.SourceUrl)), cancellationToken);`
(spec J3), then `logger.ReconcilerStartupPassComplete(configured.Count, expected.Count, removed, readded)`
with arguments in today's order. Extract:

- `private async Task<int> RemoveOrphanPathsAsync(IRtspGateway gateway, IReadOnlyList<MediaMtxPath> configured, HashSet<MediaMtxPath> expected, CancellationToken cancellationToken)`:
  the first `foreach` verbatim (`RemovePathAsync` -> `removed++` -> `ReconcilerRemovedOrphanPath`;
  `catch (HttpRequestException)` -> `ReconcilerFailedToRemoveOrphanPath`).
- `private async Task<int> ReaddMissingPathsAsync(IRtspGateway gateway, IReadOnlyList<MediaMtxPath> configured, IEnumerable<(MediaMtxPath Path, StreamSourceUrl SourceUrl)> streams, CancellationToken cancellationToken)`:
  the "Re-add the other half" comment, `var present = configured.ToHashSet();` and the second
  `foreach` (`AddPathAsync(path, sourceUrl.Value, ...)` -> `readded++` -> `ReconcilerReaddedMissingPath(path, sourceUrl.Value)`;
  `catch (HttpRequestException)` -> `ReconcilerFailedToReaddMissingPath`).

The removal pass still completes before the re-add pass starts; both still use the scope's gateway
while the scope and the `DbContext` are alive. `StartAsync`, `StopAsync`, `RunDeferredAsync` and their
docs (including "swallowed by the catch below", which points at `RunDeferredAsync`) are not edited.

### 3.7 `StreamDistributionInfrastructureModule` (hits 11, 12)

**`AddMediaMtxGateway` (hit 12)**, its own commit first. Keep the "Typed HttpClient for MediaMTX..."
comment, the `AddHttpClient<IRtspGateway, MediaMtxRtspGateway>` lambda (options read, `BaseAddress`,
the ADR-0171 comment and the `Basic stream-distribution:{apiSecret}` header **byte-identical**), the
"Its two non-idempotent methods are idempotent in fact (ADR-0143)..." comment and `.RetryEveryMethod()`
**in `AddMediaMtxGateway`**. Move the `services.AddOptions<TelemetryOptions>().Configure<...>(...)`
block and its "Keyed on the request's target host" comment into
`private static void DemoteRoutineMediaMtxProbeLogs(IServiceCollection services)` (the name says
what it registers: a `TelemetryOptions` severity-provider configuration), called after the typed
client exactly where the block sits today, before `return builder;`.

**`AddStreamDistributionInfrastructure` (hit 11).** Keep the guard, the two persistence calls, the
four `Bind*` calls, `builder.Services.AddMediaMtxGateway();`, the `AddWolverineForContext` call and
`return builder;` where they are. Replace the registration runs with `private static` helpers on
`IServiceCollection`, called in today's order, each body **one fluent chain** (CLAUDE.md), lifetimes
and generic arguments verbatim:

1. `AddStreamPersistenceAndDomainEventDispatch(services)`: `IStreamRepository`, `IStreamQuerySource`,
   the two `IDomainEventHandler<...>` interface registrations, `IDomainEventDispatcher`.
2. `AddClockAndMediaMtxAccessServices(services)`: `IClock -> SystemClock`, `TimeProvider.System`,
   `IStreamWhepUrlBuilder`, `IWhepAuthValidator`, `IMediaMtxApiAuthenticator` (all singletons).
3. `AddCommandHandlers(services)`: the "Hand-rolled command/query handler registrations
   (ADR-0042 + ADR-0057)" comment and the six command handlers in today's order.
4. `AddQueryAndMessageHandlers(services)`: the "Query handlers are resolved as concrete classes..."
   comment, the two query handlers, the three integration-event handlers and the two concrete
   domain-event handlers, in today's order.
5. after `AddMediaMtxGateway()`: `AddStreamHostedServices(services)`: `StreamHealthWatcher`,
   `MediaMtxReconciler`, `StreamFabAttributionService`, in today's order.

Helper names are suggestions but must say what they register. Do **not** move, reorder or edit
`BindMediaMtxOptions`, `BindMediaMtxApiOptions`, `BindStreamFabAttribution` (with its `#pragma` and
the token client's `RetryEveryMethod()` and reason), `BindWhepAuthOptions`, `IsHttpsAbsolute`; place
the new helpers **after `AddMediaMtxGateway` and before `BindMediaMtxOptions`** so the existing
relative order of the `Bind*` methods (and spec §6 oddity 1) is unchanged. Re-check S104 for the file
after the commit.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

Coverage that already exists and is relied on (must pass unmodified):

| Member | Existing tests |
|---|---|
| AuthorizeWhep handler | `AuthorizeWhepCommandHandlerTests` (23: legacy bundle Forbidden, empty/invalid token, no scope, offline, publish/api refused even with valid token, publish without token refused on action, absent/unrecognised/empty/blank/truncated action logs, fab refused, second-fab success, null fab refused, offline+other fab -> fab refusal), `AuthorizeWhepIdentityUnavailableTests` (3) |
| Repoint handler | `RepointStreamCommandHandlerTests` (7) |
| ReportHealth handler | `ReportStreamHealthCommandHandlerTests` (5) |
| WhepAuthValidator | `Auth/*` (11 files: revocation 5, unreachable realm 7 incl. the two cancellation tests the comment names, refresh restraint 3, rotation 3, issuer 3, audience, metadata source/transport, resolution) |
| Reconciler | `MediaMtxReconcilerStartupOrderingTests` (3, unit); `MediaMtxReconcilerIntegrationTests` (4, CI) |
| Infrastructure module | `MediaMtxProbeLogVolumeTests` (5, calls `AddMediaMtxGateway()`), `WhepMetadataTransportTests` (`BindWhepAuthOptions`), `WhepAuthValidatorResolutionTests`; Architecture.Tests `ResilienceRegistrationTests` and the `PostgresComposition` suites |
| EF configuration | none in unit tests; Integration.Tests (CI only) |

Gaps, pinned by **new files only** (no existing test or fake edited; a recording decorator is a new
file). Every expected value is a **hand-written literal** captured on `8ded100e`, never computed by
the code under test; every log assertion compares the **rendered message** with **distinct values per
slot**; exceptions are pinned by type and full message.

| ID | File | Pins | Named mutation the test-writer must show RED (then revert) |
|---|---|---|---|
| C0 | `tests/StreamDistribution.Application.Tests/Fakes/RecordingStreamDistributionPorts.cs` | Decorators over `FakeWhepAuthValidator`, `InMemoryStreamRepository` and `FakeRtspGateway`, appending to one shared ordered `List<string>` (`"validate"`, `"lookup-path"`, `"lookup-camera"`, `"save"`, `"repoint"`), delegating everything. Infrastructure for C1-C3 | (support file; proven through C1-C3) |
| C1 | `tests/StreamDistribution.Application.Tests/Commands/AuthorizeWhepRefusalOrderCharacterisationTests.cs` | Precedence pairs, each a single test with two failing conditions: absent action + blank token -> `WHEP_ACTION_UNKNOWN`, no `"validate"`; api + valid token -> `WHEP_ACTION_NOT_PERMITTED`; blank/whitespace token -> `WHEP_UNAUTHORIZED` with **no** `"validate"`; realm down -> `WHEP_IDENTITY_PROVIDER_UNAVAILABLE` with no `"lookup-path"`; read-scope missing **and** stream in another fab -> `WHEP_FORBIDDEN`, no `"lookup-path"`; other fab **and** offline -> fab refusal (exists; keep a literal-code variant); null-fab stream -> fab refusal; unregistered path + valid token -> success with path returned; offline -> unavailable. Exact error code **and** HTTP status of each. Rendered `RefusedWhepFab` text with subject `"subject-a"`, path of a known camera, stream fab `"dresden"` (caller holds `"munich"`); rendered `RefusedWhepAction` for publish and for api | (a) Move the scope check below `GetByPathAsync` -> "no scope + other fab" answers fab refusal / records `"lookup-path"`: RED. (b) Move the blank-token check below `ValidateAsync` -> `"validate"` recorded: RED. (c) Swap the fab and offline checks inside the new helper's future position (today: swap the two `if`s) -> RED |
| C2 | `tests/StreamDistribution.Application.Tests/Commands/RepointStreamOrderCharacterisationTests.cs` | Unparsable address -> `STREAM_INVALID_RTSP_SOURCE` (literal code and message), no `"lookup-camera"`; success sequence `lookup-camera -> save -> repoint`; gateway `HttpRequestException("boom-7")` -> `STREAM_RTSP_GATEWAY_UNAVAILABLE` with message `boom-7`, save already done, warning logged with the exception; missing stream and retired stream -> success `None`, no `"save"`, rendered `NoStreamToRepoint` / `SkippedRepointOfRetiredStream` texts; rendered `RepointedStream` text with distinct stream id, camera id, path | (a) Move `SaveAsync` below the gateway call -> order RED. (b) Drop the retired check -> `"save"` recorded: RED |
| C3 | `tests/StreamDistribution.Application.Tests/Commands/ReportStreamHealthPrecedenceCharacterisationTests.cs` | `DeclareOffline=true` with a ready observation -> Offline; not-ready with `LastError=null` -> `LastError` equals literal `"no frame within the health-watcher window"`; declared offline with `LastError=null` -> `"offline (no frames within retry window)"`; supplied `LastError` wins over both defaults; rejected transition -> `STREAM_INVALID_STATE_TRANSITION` with literal from/to/reason and `SaveCallCount == 0`; `OnSave` throwing `DbUpdateConcurrencyException("race-2783")` propagates out of `HandleAsync` with that message (spec §6 oddity 3); unknown camera -> `STREAM_NOT_FOUND` naming the camera | (a) Swap the `declareOffline` and `IsReady` branches -> RED. (b) Change one default literal -> RED |
| C4 | `tests/StreamDistribution.Infrastructure.Tests/Auth/WhepValidatorClaimOrderCharacterisationTests.cs` | Over the internal metadata-seam constructor and a locally signed key (pattern of `WhepRevocationTests`, own fake registry recording calls): token without `sub` whose `azp` the registry refuses -> `TokenRejected`, registry **not called**, no `TokenRefusedAsRevoked` entry; revoked `azp` `"client-x9"` -> rendered warning naming `client-x9`; scope claim `"  a  b c"` -> scopes `["a","b","c"]` in order; two `/fabs/...` groups -> fabs in claim order; subject `"subject-7"` passed through | Move the revocation check above the `sub` check -> registry called / log present: RED |
| C5 | `tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs` | Technique of `IdentityRegistrationCharacterisationTests`: `PostgresComposition.Compose("stream-distribution", ...)`, ordered descriptors whose service/implementation type (recursively through generic arguments) is declared in a `SmartSentinelEye.StreamDistribution.*` assembly, rendered `"{Lifetime} {ServiceType} -> {Implementation|factory|instance}"`, equal to a literal list captured on `8ded100e`; plus `IClock`, `TimeProvider`, the three hosted services in order; plus exactly one named `HttpStandardResilienceOptions` configuration for `"IRtspGateway-standard"` and one for `"{CameraCatalogTokenProvider.HttpClientName}-standard"` (the `RetryEveryMethod()` opt-ins). This context's registrations only, **no total-count assertion** | (a) Swap two adjacent command-handler registrations -> RED. (b) Delete `.RetryEveryMethod()` from `AddMediaMtxGateway` -> RED |
| C6 | `tests/StreamDistribution.Infrastructure.Tests/StreamDistributionModelCharacterisationTests.cs` | Technique of `LayoutCompositionModelCharacterisationTests`: `StreamDistributionDbContext` over `UseNpgsql("Host=unused;...")`, the design-time model read through the **public metadata API** (per entity: table, key, owned; per property: column, store type, CLR type, nullability, max length, converter present, concurrency flag; FKs; indexes with name and uniqueness), equal to a literal list captured on `8ded100e`. **Not** `Model.ToDebugString`. No csproj edit | (a) Change `HasMaxLength(80)` on `mediamtx_path` -> RED. (b) Remove `builder.Navigation(...).IsRequired()` -> owned columns nullable: RED |
| C7 | `tests/Integration.Tests/StreamDistribution/MediaMtxReconcilerPassCharacterisationTests.cs` + one appended entry in `tests/Integration.Tests/ci-shards/shard-2.filter` (beside `MediaMtxReconcilerIntegrationTests`) | Aspire fixture, Postgres only: seed three non-retired streams and one retired; a recording `IRtspGateway` (in-file) reporting configured paths `[orphan-1, kept, orphan-2, retired-leftover]`, failing `RemovePathAsync` for `orphan-2` and `AddPathAsync` for one missing stream; a capturing logger. Pins: the recorded call list (`list`, every `remove` before any `add`, each in today's iteration order); per-path log texts; the rendered summary `"MediaMtxReconciler startup pass complete. Configured=4, expected=3, removed=2, readded=1."` (literals from the seed; values chosen distinct) | (a) Increment `removed` before `RemovePathAsync` -> summary RED. (b) Run the re-add loop first -> call order RED |

A characterisation test that arrives red on the unrefactored code is a test defect and is fixed in
the test, never by a production change in this spec. Every assertion must be able to fail on a
change to the subject without its own text changing; the test-writer states, per file, the named
mutation above as observed RED, with the failing line quoted.

**C7 observation.** Integration.Tests need Docker and the whole Aspire stack. Observe C7 green on
the unrefactored code **locally** only if no other AppHost or `testhost` is running on the machine
(one machine, one Aspire stack; `sse-2892` may be running one): `dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~MediaMtxReconcilerPassCharacterisationTests"`.
Otherwise push the branch at the last 4a commit and run `gh workflow run ci.yml --ref refactor/2893-stream-distribution-metrics`,
then read the result from the run's trx artifact (not the watcher's exit code). Either way the
before-run output is quoted in the PR. The refactor of `ReconcileOnceAsync` (T016) does not start
until that observation exists.

## 5. Measurement and run commands (before and after, quoted in the PR)

```sh
dotnet build src/StreamDistribution/Infrastructure/SmartSentinelEye.StreamDistribution.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep StreamDistribution
```

Before (`8ded100e`): 12 lines (spec §2). After: exactly `Application/Log.cs(57,...) S107`,
`Domain/Stream/Stream.cs(68,...) S107`, `Infrastructure/Log.cs(29,...) S107` (none of those files is
edited, so their lines do not move).

```sh
dotnet test tests/StreamDistribution.Domain.Tests -c Release          # 141 passed on 8ded100e
dotnet test tests/StreamDistribution.Application.Tests -c Release     # 86 passed on 8ded100e
dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release  # 77 passed on 8ded100e
dotnet test tests/Architecture.Tests -c Release                       # HandlerDeconstructionTests, EventMetadataFabDeclarationTests, BoundaryTests, PrimitiveBoundaryTests, ResilienceRegistrationTests, PostgresComposition suites, C5
dotnet format src/StreamDistribution/Application/SmartSentinelEye.StreamDistribution.Application.csproj --verify-no-changes
dotnet format src/StreamDistribution/Infrastructure/SmartSentinelEye.StreamDistribution.Infrastructure.csproj --verify-no-changes
```

Integration.Tests `StreamDistribution/*` (incl. C7): CI. Latency: `WhepHandshakeLatencyTests` p95
from the PR's CI trx artifact against the last green `develop` run's (spec SC-005); if the PR figure
looks worse, re-run once before calling it a regression (machine churn).

## 6. Risks

| Risk | Mitigation |
|---|---|
| WHEP refusal precedence flips (scope vs fab, token vs action, fab vs offline) | §3.1 binding order; C1 precedence pairs and call log |
| An `await`/allocation creeps onto the viewer-connect path | FR-004; §3.1/§3.4 synchronous helpers; reviewer checks the diff for `async`/lambdas in them |
| Claims read outside the validation `try` (an exception escaping as 500) | §3.4: `SubjectFrom` called inside `ValidateAgainst`'s `try`; existing malformed-token tests + C4 |
| Revocation consulted before the `sub` check | C4 |
| Save/gateway order in repoint | C2 |
| Health precedence or default text drift | C3 |
| `RetryEveryMethod()` lost or its reason separated from it | §3.7 binding; C5 named-options pin; `ResilienceRegistrationTests` |
| DI order change altering last-wins | C5 ordered list |
| EF configuration reorder altering the model | C6 |
| Reconciler counts or pass order drift | C7 (CI) + existing integration tests |
| Comment "above/below" made false | §3 per-section placement rules; reviewer re-reads every moved comment |

## 7. Phase 6 security-reviewer brief

Required (security-sensitive context). Check against `origin/develop`:

1. `AuthorizeWhepCommandHandler`: action refusals still precede any token handling; blank token
   refused before the validator is called; realm-down vs token-rejected mapping (`RefusalFor`)
   unchanged; scope check precedes the stream lookup; fab check (incl. null fab) precedes the offline
   check; the fab condition and `StringComparer.Ordinal` byte-identical; no reported action value is
   echoed into the answer.
2. `WhepAuthValidator`: parameters still cloned per call with discovery issuer and keys; `sub`
   before revocation before scopes; `RequestRefresh` only for the two curable exception types; the
   `InvalidOperationException` metadata catch not widened; both token catches keep order and
   filters; claim reading still inside the `try`; no token, `azp`-adjacent secret or claim set
   newly logged.
3. `AddMediaMtxGateway`: `.RetryEveryMethod()` still chained on the MediaMTX typed client with its
   ADR-0143 reason directly above; the Basic header built from `MediaMtxApiOptions.Secret` exactly
   as before and never logged; `BindStreamFabAttribution` and `BindMediaMtxApiOptions` (secret
   `ValidateOnStart`) untouched (`git diff` empty for those methods).
4. Fab scoping: `FabClaims.AssignedFabs` usage unchanged; `RefusedWhepFab` still the only cross-fab
   signal.
5. No file under `Api/` or `Migrations/`, no guard, no suppression changed; `Log.cs` untouched.
