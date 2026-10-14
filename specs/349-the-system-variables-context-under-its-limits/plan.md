# Plan 349: The system variables context under its limits

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2891.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 8 of 16 hits by shape only; the 8 left are named with reasons (spec §2). |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| §II (no primitives on Domain models) | Domain is not touched. No new type is introduced anywhere. |
| §III (no cross-context refs) | No reference added; `Shared.Contracts` untouched. |
| §IV (latency) | *Event → overlay state* touched by `SystemVariableValueRequestedV1Handler` and `VariableValueChangedDomainEventHandler`; FR-004 forbids added allocation, I/O, lock or `await` there (spec §7). |
| ADR-0050 | Both `Log.cs` files untouched; every log call keeps method, arguments and order. |
| ADR-0102 / ADR-0115 | Every `EventMetadata` keeps its arguments (fab = `fab.Value`, actor, occurred-at, root-ingested-at); siblings still resolve in the event's fab. |
| ADR-0051 | `AddSystemVariablesInfrastructure` keeps its signature; extractions are private static. |
| ADR-0143 | The seed clients' `RetryEveryMethod()` and the `#pragma` around the named client stay in `BindReverseIndexSeeder`, untouched. |
| House rules | Fluent DI chains; registration comments stay with their line; helper names say what they register; `var`; one parameter per line when wrapping; `[NotNullWhen]` on `Try*` helpers. |

**Concurrency.** #2905 (spec 348, `D:\Github\sse-2890`) touches `src/Identity/*`, adds
`Identity*`-named test files and `tests/Architecture.Tests/Persistence/IdentityRegistrationCharacterisationTests.cs`.
The merged siblings (341, 342, 345, 346, 347) touched `src/*/Api/*.cs`, `ScenarioSimulator`,
LayoutComposition, EventIngestion and Automation. This plan touches no `Api/` file and no other
context, and adds only `SystemVariables*`/`ValueRequest*`/`ValueChanged*`/`Archived*`/
`ReverseIndexSeed*`-named test files. No file overlap.

## 2. Guards that read these files, and the shapes they tolerate

| Guard | What it reads | Tolerates | This plan |
|---|---|---|---|
| `EventMetadataFabDeclarationTests` | Bodies of `public/internal [async] Task Handle/HandleAsync(` methods; counts `new EventMetadata(` **and** target-typed `Metadata: new(` in the whole file and in handler bodies; fails (`Unscanned`) when a file naming `Handle(` has a construction outside a handler body; reads the fab as positional argument 2 and rejects literal `null`/`default` and named arguments | A construction hoisted to a local inside `Handle` (`var metadata = new EventMetadata(...)`) and passed on as `Metadata: metadata` (not matched by the regex) — the shape `FabEventIngestedV1Handler` uses on `develop` | Both domain-event handlers: each `EventMetadata` becomes `var ... = new EventMetadata(...)` **inside `Handle`**, positional, fab `fab.Value`; mappers and constructors receive `Metadata: metadata`. Declared = reached = 2 per file. **No construction moves into a private method.** |
| `HandlerDeconstructionTests` | `public async Task Handle(<Type> <param>,` then the first `var (...) = <param>;` in the body; each local must be `_` or named after the field at its position | Discards; renamed locals that do not name another field | The three handlers keep their deconstruction as the first statement after the guard. Value-changed: `var (_, fab, name, _, _, changedAt, changedBy, _, rootIngestedAt) = domainEvent;`. Archived: `var (_, fab, variableName, archivedAt, archivedBy) = domainEvent;`. Value request: unchanged. |
| `PrimitiveBoundaryTests` | Domain model state | — | Domain untouched. |
| `BoundaryTests`, `OutboxCommitTests` | Assembly references; outbox commit sites | — | No reference or commit site changes. |
| `IdempotencyReservationSweepRegistrationTests`, `PostgresComposition` | `AddSystemVariablesInfrastructure()` composition | Any registration order that still registers the sweep | Order kept anyway (C6). |
| `FoundingDecisionRecordTests` | `src/SystemVariables/Domain/Variable/VariableType.cs` | — | Not touched. |

## 3. Production shape (binding on the engineer)

Every extracted method: **<= 4 parameters (an `out` and the `CancellationToken` count), <= 30
lines, complexity <= 10, nesting <= 3**, verified with the grep in §5 after each commit.
Statements move verbatim; comments move with the code they explain, and every comment that says
"above"/"below"/"here" is re-read against where it now sits (§3.4 names one that must change).

### 3.1 `SystemVariableValueRequestedV1Handler.Handle` (hit 1) — per-event path

`Handle` keeps the guard and the deconstruction line as written, then:

- `private bool TryResolveFab([NotNullWhen(true)] EventMetadata? metadata, string name, Guid causingEventIdentifier, [NotNullWhen(true)] out FabIdentifier? fab)`
  — the `IsNullOrWhiteSpace(metadata?.Fab)` check (logs `ValueRequestWithoutFab(name, causingEventIdentifier)`)
  then `FabIdentifier.From(metadata.Fab)` with the `ArgumentException` catch (logs
  `ValueRequestWithUnusableFab(ex, metadata.Fab, name, causingEventIdentifier)`). The FR-006
  comment moves with the check. `[NotNullWhen(true)]` on `metadata` keeps the compiler's
  knowledge that `metadata` is non-null afterwards, so no `!` is introduced.
- `await dedup.TryReserveAsync(...)` and the `DedupHit` branch stay in `Handle`, **after** the fab
  and **before** the name parse — today's order (an invalid name still consumes a reservation:
  spec §6 oddity c, pinned, not fixed).
- `private bool TryParseVariableName(string name, Guid causingEventIdentifier, [NotNullWhen(true)] out VariableName? variableName)`
  — the `VariableName.From` try/catch, logging `InvalidVariableName(ex, name, causingEventIdentifier)`.
- `private static SetVariableValueCommand ToSetVariableValueCommand(FabIdentifier fab, VariableName variableName, string value, EventMetadata metadata)`
  — the command exactly as today: `fab, variableName, value, AutomationOperator, Option<int>.None,`
  and the `RootIngestedAt is { } accepted ? Some(accepted) : None` expression with its #2173
  comment. `Handle` calls `await setHandler.HandleAsync(ToSetVariableValueCommand(...), cancellationToken)`.
- The result tail (`IsSuccess` return, `VariableNotFound` -> `VariableNotInFab`, else
  `SetVariableValueFailed(name, value, causingEventIdentifier, result.Error.Code)`) stays in
  `Handle` with its FR-005 comment.

All three helpers are synchronous; the two `await`s in `Handle` are today's two. No closure, no new
allocation (the command is the one already built).

### 3.2 `VariableValueChangedDomainEventHandler` (hits 5, 6) — per-event path

`Handle`, in today's order:

1. Guard; deconstruction with discards (§2).
2. `var valueChangedMetadata = new EventMetadata(Guid.CreateVersion7(), changedAt, fab.Value, changedBy.Value);`
   — four arguments, so the V1's `RootIngestedAt` stays `null` exactly as today.
3. `await events.PublishAsync(ToValueChangedV1(domainEvent, valueChangedMetadata), cancellationToken);`
   with `private static SystemVariableValueChangedV1 ToValueChangedV1(VariableValueChangedDomainEvent changed, EventMetadata metadata)`
   building `Variable: changed.Variable.Value, Name: ..., Type: ..., Value: changed.Value.ToWireString(), ChangedAt: ..., ChangedBy: ..., Metadata: metadata`
   (named arguments as today; the return type is the concrete record, so `PublishAsync<TEvent>`
   binds to `SystemVariableValueChangedV1`).
4. `var accepted = rootIngestedAt.Match<DateTimeOffset?>(...)` with its comment — unchanged, once
   per event.
5. `LookupOverlays`, the zero-overlay `NoOverlaysReferenceVariable` + `return`, the single
   `AdvanceAsync` with its #2426 comment (may become one line).
6. `foreach (var overlayId in affectedOverlays)`: `LookupLabelTexts`, `if (labelTexts is null) { continue; }`,
   `var snapshot = await BuildSnapshotAsync(labelTexts, domainEvent, cancellationToken);` with its
   spec-150 comment (its "the version advance above" still holds),
   `var texts = ResolveTexts(labelTexts, snapshot);`,
   `var metadata = new EventMetadata(Guid.CreateVersion7(), changedAt, fab.Value, changedBy.Value, accepted);`,
   `ResolvedOverlayTextChangedV3 @event = new(Overlay: overlayId, Texts: texts, Version: versionsByOverlay[overlayId], Metadata: metadata);`,
   `await events.PublishAsync(@event, cancellationToken);`.
7. `logger.PushedResolvedTextAfterChange(affectedOverlays.Count, name);`

`private IReadOnlyList<ResolvedOverlayTextV3> ResolveTexts(IReadOnlyList<string> labelTexts, IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot)`
holds today's collection expression verbatim (instance: it reads `resolver`). Synchronous; the
lambda captures nothing.

`BuildSnapshotAsync` (hit 6): replace the nine-line `VariableName parsed; try { ... } catch (ArgumentException) { continue; }`
with `if (!TryParseVariableName(name, out var parsed)) { continue; }` and
`private static bool TryParseVariableName(string name, [NotNullWhen(true)] out VariableName? parsed)`
holding the same `try`/`catch (ArgumentException)`. Everything else verbatim: the changed-variable
branch first (taken from the event, `Unset` skipped), then the sibling lookup in `changed.Fab`
with both comments, `None` and `Unset` skipped.

Per-event cost after == before: one `BuildSnapshotAsync` state machine per overlay (as today), two
`EventMetadata` and one V1 + one V3 per overlay (as today), zero new `async` methods, `await`s,
closures or locks.

### 3.3 `VariableArchivedDomainEventHandler.Handle` (hit 3) — not per event

Same skeleton as §3.2 without `accepted` and without the zero-overlay log (today it returns
silently; spec §6 oddity b):

- `var archivedMetadata = new EventMetadata(Guid.CreateVersion7(), archivedAt, fab.Value, archivedBy.Value);`
  then `await events.PublishAsync(ToArchivedV1(domainEvent, archivedMetadata), cancellationToken);`
  with `private static SystemVariableArchivedV1 ToArchivedV1(VariableArchivedDomainEvent archived, EventMetadata metadata)`.
- The inline snapshot loop moves **verbatim** (its compact one-line `try`/`catch`, both comments,
  the archived-name skip by ordinal `variableName.Value`, the sibling lookup in `fab`) to
  `private async Task<IReadOnlyDictionary<string, VariableSnapshotEntry>> BuildSnapshotAsync(IReadOnlyList<string> labelTexts, FabIdentifier fab, VariableName archivedName, CancellationToken cancellationToken)`.
  The "Build a snapshot of every OTHER variable ..." comment moves with it.
- Per overlay: labels, null skip, snapshot, `ResolveTexts` (own private copy, as today's
  expression is duplicated today), `var metadata = new EventMetadata(Guid.CreateVersion7(), archivedAt, fab.Value, archivedBy.Value);`
  (four arguments: no `RootIngestedAt`, as today), the one-line V3, publish.
- `logger.PushedResolvedTextAfterArchive(affectedOverlays.Count, variableName);`

J3: one added async call per overlay is accepted because archive is not on §IV's leg.

### 3.4 `VariableSnapshotBuilder.BuildAsync` (hit 11) — not per event

Replace the found-variable tail (`var variable = found.Value;`, the `Unset` add + `continue`, the
`Resolved` add) with `resolutions.Add(ResolutionOf(name, found.Value, answeringFab));` and
`private static PlaceholderResolution ResolutionOf(string name, Variable variable, FabIdentifier? answeringFab)`
returning the `Unset` row (answering fab, null entry) or the `Resolved` row (answering fab,
`new VariableSnapshotEntry(variable.Value, variable.BooleanLabels)`). The not-found branch, both
fab helpers and their order stay. **The comment moved with the `Unset` row says "unlike the
not-found path below"; in `ResolutionOf` that path is no longer below. Reword it to "unlike the
not-found path in `BuildAsync`"** (the reference, not the claim, changes).

### 3.5 `VariableConfiguration.Configure` (hit 13)

`Configure` keeps the guard and calls, in today's order, private static methods each taking the
`EntityTypeBuilder<Variable> builder`: `ConfigureTableAndIdentifier` (table, key, id column),
`ConfigureScalarColumns` (fab, name, type, state, value_packed), `ConfigureOwnedValues`
(`BooleanLabels`, `Creation` with its #2022 comment, `Navigation(Creation).IsRequired()`),
`ConfigureVersionAndIndexes` (version concurrency token, the FR-005 partial unique index with its
comment, `Ignore(PendingEvents)`). Builder calls keep their order and arguments.
`VariableValueColumnConverter` untouched.

### 3.6 `ReverseIndexSeederHostedService.StartAsync` (hit 15) — at boot

`StartAsync` keeps the `try`, both `using`s (the client and the response live until the end of
the `try`, as today), both `catch` clauses with their filters and the refused-mint comment.
Extract:

- `private void LogUnsuccessfulStatus(HttpStatusCode statusCode)` — 401/403 -> `SeedRefused`,
  else `SeedNonSuccessStatus`.
- `private int SeedOverlays(JsonElement published)` — the loop verbatim with the spec 150/300
  comment, returning `seeded`. Called inside the `try`, so a `GetGuid()` throw still reaches the
  `SeedFailed` catch after the earlier overlays were upserted (spec §6 oddity d).

`StartAsync` then logs `SeededOverlays(seeded)`. The class summary's `<see cref="Log.SeedRefused"/>`
stays valid.

### 3.7 `SystemVariablesInfrastructureModule.AddSystemVariablesInfrastructure` (hit 16)

Mirror `AutomationInfrastructureModule` on `develop`. `AddSystemVariablesPersistence()` and
`EnrichPostgresDbContext<SystemVariablesDbContext>()` first (one chain if the first returns the
builder), then **one** fluent chain on `builder.Services` of private static `IServiceCollection`
extensions, each itself one fluent chain, in today's registration order:

1. `AddPersistenceClockAndIdempotencyStore` — `IVariableRepository`, `IVariableQuerySource`,
   `IDomainEventDispatcher`, `IClock`, `TimeProvider.System`, then (ADR-0142 comment)
   `IIdempotencyStore` and `AddIdempotencyReservationSweep<SystemVariablesDbContext>()`. The name
   says it registers the idempotency store (house rule; spec 347's review).
2. `AddOverlayTextResolution` — reverse index and resolver (singletons, comment), then the scoped
   `VariableSnapshotBuilder` (spec 148 comment).
3. `AddDomainEventHandlers` — defined, value-changed, archived (comment).
4. `AddCommandHandlers` — define, the concrete `SetVariableValueCommandHandler`, set, archive
   (ADR-0042/0057 comment).
5. `AddValueRequestBridge` — dedup store, then `SystemVariableValueRequestedV1Handler` (spec 007
   comment).
6. `.AddScoped<IOverlayTextVersions, OverlayTextVersionStore>()` as the chain's last link, its
   #2426 comment on it (or a sixth helper named for it).

Then `BindReverseIndexSeeder(builder)` unchanged and `AddWolverineForContext` last. Registration
**order and lifetimes are unchanged** (C6). The class summary stays true.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

Coverage that already exists and is relied on (must pass unmodified):

| Member | Existing tests |
|---|---|
| `SystemVariableValueRequestedV1Handler.Handle` | `SystemVariableValueRequestedV1HandlerTests` (10) |
| `VariableValueChangedDomainEventHandler` | `VariableValueChangedDomainEventHandlerTests` (5), `VariableValueChangedPreCommitTests` (2) |
| `VariableArchivedDomainEventHandler` | `VariableArchivedDomainEventHandlerTests` (8) |
| `VariableSnapshotBuilder.BuildAsync` | `VariableSnapshotBuilderTests` (6), `ResolveOverlayTextQueryHandlerTests` |
| `ReverseIndexSeederHostedService` | `ReverseIndexSeederHostedServiceTests` (14) |
| Infrastructure module | `Architecture.Tests` via `PostgresComposition` and `IdempotencyReservationSweepRegistrationTests` |
| `VariableConfiguration` | none in a unit project; mapping in Integration.Tests (CI only) |

Gaps, pinned by **new files only** (no existing test or fake edited). Every expected value is a
**hand-written literal** captured by running against unrefactored `782c1b70`, never computed by
the code under test. Every log is pinned by level and **full formatted text**, with **a distinct
value in every slot** (two names, two Guids, two timestamps that differ) so a transposition fails.
Published messages are recorded by a **test-local** `IEventBus` that stores
`(typeof(TEvent), message)` (the shared `FakeEventBus` drops the type argument).

| ID | File | Pins | Mutation the test-writer must show RED |
|---|---|---|---|
| C1 | `tests/SystemVariables.Application.Tests/EventHandlers/ValueRequestRefusalOrderCharacterisationTests.cs` | For: no metadata, whitespace fab, unusable fab, dedup hit, invalid name, `VariableNotFound`, another failure, success — the exact log line (level + text) or none; the `TryReserveAsync` calls made (none for the two fab refusals; one, with fab/name/causing event, for the rest — **including the invalid name**); the command's operator (`AutomationOperator` literal Guid), expected version `None`, `RootIngestedAt` `Some`/`None`. Use a test-local recording dedup store; add only what `SystemVariableValueRequestedV1HandlerTests` does not already pin exactly. | Transpose `metadata.Fab` and `name` in the `ValueRequestWithUnusableFab` call. |
| C2 | `tests/SystemVariables.Application.Tests/EventHandlers/ValueChangedFanOutCharacterisationTests.cs` | Two overlays from the reverse index (one whose `LookupLabelTexts` is null — a test-local `IReverseIndex`), labels naming the changed variable, a live sibling, an unset sibling, an archived sibling and an unparseable placeholder: exactly `[SystemVariableValueChangedV1, ResolvedOverlayTextChangedV3]` by recorded type; every V1 field (wire value, type) and metadata (`OccurredAt` = changedAt, `Fab`, `Actor` = changedBy, `RootIngestedAt` **null**); the V3's overlay, texts (template -> resolved) in order, version from the fake store, metadata `RootIngestedAt` = the event's value (distinct from changedAt), event identifiers non-empty and distinct; `AdvanceAsync` called once with both overlays; log `Pushed ResolvedOverlayTextChanged to 2 overlays after '<name>' changed.`; with zero overlays, only the V1 and `No overlays reference variable '<name>'; skipping push fan-out.`; with `RootIngestedAt` `None`, V3 `RootIngestedAt` null. | Pass `accepted` as the fifth argument of the V1's `EventMetadata`. |
| C3 | `tests/SystemVariables.Application.Tests/EventHandlers/ArchivedFanOutCharacterisationTests.cs` | Same harness for the archive: `[SystemVariableArchivedV1, ResolvedOverlayTextChangedV3]`; every V1 field and metadata; the archived placeholder and the unset sibling literal, the live sibling resolved in the archiving fab (not another fab holding the same name); V3 metadata `OccurredAt` = archivedAt, `Actor` = archivedBy, `RootIngestedAt` null; V1 and V3 event identifiers distinct; log `Pushed ResolvedOverlayTextChanged to 2 overlays after '<name>' was archived.`; zero overlays -> only the V1 and **no** log. | Reuse the V1's metadata local for every V3 (same `EventIdentifier`). |
| C4 | `tests/SystemVariables.Application.Tests/Resolution/VariableSnapshotBuilderOrderCharacterisationTests.cs` | One label, two fabs (`"b-fab"`, `"a-fab"` given in that order): resolved-in-both (answers `a-fab`), unset, archived, unknown, unparseable (absent), and a duplicate placeholder: the full ordered `PlaceholderResolution` list as literals (name, outcome, fab or null, entry value/labels or null). | Return `null` instead of `answeringFab` on the `Unset` row. |
| C5 | `tests/SystemVariables.Infrastructure.Tests/Resolution/ReverseIndexSeederBranchCharacterisationTests.cs` | Via a stub `HttpMessageHandler` and a test-local recording `IReverseIndex`: 500 -> one Warning, exact text, nothing seeded; body without `published` -> exact text; a listing with an overlay missing `overlayIdentifier`, one with `elements` not an array, and two good ones -> exactly the two upserts in order with their texts and the log `... 2 ...` exact text; a non-Guid identifier after one good overlay -> that overlay upserted, `SeedFailed` logged, **no** `SeededOverlays`; a cancelled token -> `OperationCanceledException` propagates. Add only what `ReverseIndexSeederHostedServiceTests` does not already pin. | Move `seeded++` above the `elements` check. |
| C6 | `tests/Architecture.Tests/Persistence/SystemVariablesRegistrationCharacterisationTests.cs` | Mirror `AutomationRegistrationCharacterisationTests` on `develop`: via `PostgresComposition.Compose("system-variables", ...)` (or `IdempotencyReservationSweepRegistrationTests`' builder if `Compose` does not reach the seeder), the ordered descriptors mentioning a `SmartSentinelEye.SystemVariables.*` type (recursively through generic arguments) plus `IClock`, `IDomainEventDispatcher`, `TimeProvider`, rendered `"{Lifetime} {Service} -> {Implementation or factory/instance}"`, equal a literal list. No total descriptor count. | Register `VariableSnapshotBuilder` as a singleton, or move `IOverlayTextVersions` above the dedup store. |
| C7 | `tests/SystemVariables.Infrastructure.Tests/SystemVariablesModelCharacterisationTests.cs` | Mirror `AutomationModelCharacterisationTests` on `develop`: `SystemVariablesDbContext` with `UseNpgsql("Host=unused;...")`, its design-time model read through the **public metadata API** (per entity: table, schema, owned, key; per property: column, store type, CLR type, nullability, max length, converter provider type, concurrency token; foreign keys; indexes with name, filter and uniqueness; navigations and required-ness), equal a literal table. **Not** `Model.ToDebugString()`. | Drop `.HasFilter("state <> 'Archived'")` from the fab/name index. |

Each mutation is applied locally, the owning test observed red (output quoted), and reverted —
never committed. A characterisation test that arrives red on the unrefactored code is a test
defect and is fixed in the test, never by a production change in this spec. None of these classes
is in `Integration.Tests`, so no `ci-shards/*.filter` entry is needed.

## 5. Measurement and run commands (before and after, quoted in the PR)

```sh
dotnet build src/SystemVariables/Infrastructure/SmartSentinelEye.SystemVariables.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep SystemVariables
```

Before (`782c1b70`): 16 lines (spec §2). After: the 8 "Leave" rows exactly (`Log.cs:12`, `:15`,
`:42`, `:51`, `Variable.cs:55`, the two handler constructors, the migration). Compare by member.

```sh
dotnet test tests/SystemVariables.Domain.Tests -c Release          # 101 passed on 782c1b70
dotnet test tests/SystemVariables.Application.Tests -c Release     # 113 passed on 782c1b70
dotnet test tests/SystemVariables.Infrastructure.Tests -c Release  # 28 passed on 782c1b70
dotnet test tests/Architecture.Tests -c Release                    # EventMetadataFabDeclarationTests, HandlerDeconstructionTests, PrimitiveBoundaryTests, BoundaryTests, IdempotencyReservationSweepRegistrationTests, PostgresComposition suites
dotnet format src/SystemVariables/Application/SmartSentinelEye.SystemVariables.Application.csproj --verify-no-changes
dotnet format src/SystemVariables/Infrastructure/SmartSentinelEye.SystemVariables.Infrastructure.csproj --verify-no-changes
```

Stop any running AppHost before building (it holds the binaries). Integration.Tests
(`SystemVariables/*`, `Automation/EventReachesItsEffectsTests`) run in CI. Latency (SC-004):
download the TRX of the last green `develop` CI run and quote `NFR_VariableResolutionLatencyTests`'
figure as *before*; quote it from this PR's run and one re-run as *after*.

## 6. Risks

| Risk | Mitigation |
|---|---|
| An `EventMetadata` construction leaves `Handle` and the fab guard goes blind or red | §2 / FR-008: hoisted locals inside `Handle` only; `Architecture.Tests` per commit |
| Hoisted V1 metadata reused for V3 (one identifier, lost `RootIngestedAt`) | C2/C3 distinct-identifier and `RootIngestedAt` pins |
| `accepted` leaks into the V1 | C2 pins V1 `RootIngestedAt` null |
| Mapper returns `IIntegrationEvent`/`object`, so `PublishAsync<T>` binds wrongly | Concrete return types (§3.2, §3.3); C2/C3 record `typeof(TEvent)` |
| Value request: fab refusal after the reservation, or name parse before it | C1 reservation pins |
| Per-event allocation or `await` added on the value-change path | FR-004; only synchronous helpers in §3.1 and §3.2; review checks no new `async` there |
| Seeder catch no longer covers a mid-list failure, or the response disposed early | §3.6 keeps the helper call and both `using`s inside the `try`; C5 |
| `ResolutionOf` drops the answering fab | C4 |
| DI order change altering last-wins resolution or `IEnumerable<IHostedService>` order | C6 ordered list |
| EF config reorder altering the model | C7 logical model table |
| A comment that says "below" now points the wrong way | §3.4 names it; reviewer re-reads every moved comment |
| Collision with #2905 or merged siblings | No `Api/`, Identity or other-context file; distinct test file names |
