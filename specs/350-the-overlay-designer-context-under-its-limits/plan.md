# Plan 350: The overlay designer context under its limits

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2892.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 5 of 6 in-scope hits by shape only; the 1 left and the 6 migrations are named with reasons (spec §2). |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| §II (no primitives on Domain models) | The one new Domain type (J1) is a private nested record struct of value objects; `PrimitiveBoundaryTests` walks public properties from aggregate roots and cannot reach it. |
| §III (no cross-context refs) | No reference added; `Shared.Contracts` untouched. |
| §IV (latency) | N/A: editor-time members only (spec §7). |
| ADR-0050 | `Log.cs` untouched; every log call keeps method, arguments and order. |
| ADR-0113 | Each handler's Layer-1 version check stays before any mutation, at today's position in the refusal order. |
| ADR-0141 | Extracted refusal helpers return `Option<TError>`, not a nullable. |
| ADR-0051 | `AddOverlayDesignerInfrastructure` keeps its signature; extractions are private static. |
| ADR-0165 | `OverlayElement` stays non-positional with `private init` properties; construction remains locked to its factories; `RequireConsistent` still called by every factory. |

**Guards that read this context, and what they tolerate.**

- `HandlerDeconstructionTests` scans `public async Task ... Handle/HandleAsync(` bodies for
  `var (...) = <parameter>;`. Both handlers here use `(var a, ...) = command;`, which it does not
  match (spec §6 oddity 2). **Keep each deconstruction line verbatim**, first after the guard.
  Extracted helpers are `private`, so the signature regex never selects them.
- `EventMetadataFabDeclarationTests`: the only `new EventMetadata(` in this context is in
  `Application/EventHandlers/OverlayRevision{Published,Archived}DomainEventHandler.cs`. Neither is
  touched. No extracted helper may construct `EventMetadata`.
- `PrimitiveBoundaryTests`: walks public instance properties of exported Domain types reachable from
  aggregate roots. The nested private struct (J1) is not exported and not reachable.
- `OutboxCommitTests` (IL scan for direct `SaveChanges` in Infrastructure and Application
  assemblies, nested types included): nothing here adds a `SaveChanges` call.
- `IdempotencyReservationSweepRegistrationTests`, `PostgresPoolBoundTests`, `BoundaryTests`:
  composition and references; satisfied as long as the registrations themselves are unchanged
  (C4 pins them).
- Endpoint/precondition/route/idempotent-key/concurrency declaration guards read `src/*/Api/**`
  only. No Api file is touched. No guard pins a partial-file count for this context.

**Concurrency with #2891 (`D:\Github\sse-2891`, spec 349).** It touches only
`src/SystemVariables/**`, `tests/SystemVariables.*`, and
`tests/Architecture.Tests/Persistence/SystemVariablesRegistrationCharacterisationTests.cs`. This
plan touches only `src/OverlayDesigner/{Domain,Application,Infrastructure}` (non-migration),
`tests/OverlayDesigner.*`, and two `OverlayDesigner*`-named files in
`tests/Architecture.Tests/Persistence/`. No overlap with the merged siblings (341, 342, 345-348)
either: none touched `src/OverlayDesigner` outside `Api/` (#2896, spec 341).

## 2. Shape

One bounded context, three layers edited (Domain, Application, Infrastructure). No new public
type, no new project, no project reference, no migration, no messaging change, no AppHost change.

## 3. Production shape (binding on the engineer)

Every extracted method: **<= 4 parameters (CancellationToken counts), <= 30 lines (S138 counting),
complexity <= 10**, verified with the §5 grep after each commit. Statements move verbatim;
comments move with the code they explain. Method names below are suggestions; the constraints are
binding. After each move, **re-read every comment that says "above", "below", "this branch", "the
line", "here"** against its new position (listed per section); one that has become false is a
defect, and a characterisation test cannot catch it.

### 3.1 `BranchDraftRevisionCommandHandler.HandleAsync` (hit 1)

Keep the guard, the deconstruction line, the lookup + not-found, `var overlay = found.Value;`, the
ADR-0113 comment and stale check, then call
`private async Task<Option<BranchDraftRevisionError>> RefuseUnrecoverableAsync(Overlay overlay, OverlayIdentifier overlayIdentifier, CancellationToken cancellationToken)`
and return `Failure(refusal.Value)` when it has a value. The helper holds **the whole**
`if (overlay.Revisions.All(... != Published))` block verbatim, including the spec 037 comment above
it, the open-draft refusal and the `GetByNameAsync` check with its long comment. Error factories
keep `overlayIdentifier.Value` (the command's identifier, passed in), **not** `overlay.Id.Value`.
The name lookup stays **inside** the all-not-Published branch: the comment "this check MUST stay
inside this branch" stays true only if it does. Then `BranchDraft`, `SaveAsync`, the log call
(`branched.Number, overlay.Id, branchedBy`) and `Success` stay in `HandleAsync`, in today's order.

### 3.2 `EditDraftRevisionCommandHandler.HandleAsync` (hit 2)

Keep the guard, the deconstruction line, `ValidateElements` (first, before any repository call),
lookup + not-found, the ADR-0113 comment and stale check in `HandleAsync`. Extract the revision
lookup, `OVERLAY_REVISION_NOT_FOUND` and `OVERLAY_REVISION_NOT_DRAFT` into
`private static Option<EditDraftRevisionError> RefuseUnlessDraft(Overlay overlay, OverlayIdentifier overlayIdentifier, OverlayRevisionNumber revisionNumber)`
(verbatim, `SingleOrDefault` kept, `revision.State.Value` kept). Then `EditDraft`, `SaveAsync`, log,
`Success`. A signature wraps one parameter per line.

### 3.3 `OverlayElement` private constructor (hit 3, J1)

- Add `private readonly record struct ElementParts(ElementKind Kind, NormalizedPosition Position, NormalizedSize Size, OverlayColor Color, TextContent? Text);`
  nested in `OverlayElement` (positional: a missing part is a compile error; the five types are
  distinct, so a transposition is too). No `string`/`int`/`Guid` member. Nothing secret, so the
  generated `ToString` is acceptable.
- The 5-parameter constructor becomes `private OverlayElement(ElementParts parts)` assigning the
  same five properties, in the same order, from `parts`.
- The EF constructor `private OverlayElement(ElementKind kind, OverlayColor color)` keeps its
  signature and parameter names exactly (EF binds `kind` and `color` by name); it chains to
  `this(new ElementParts(kind, null!, null!, color, null))`.
- The copy constructor `(OverlayElement source, int ordinal)` chains to
  `this(new ElementParts(source.Kind, source.Position, source.Size, source.Color, source.Text))`
  and still assigns `this.ordinal = ordinal`.
- `TextElement`, `Shape` and `DeepClone` pass `new ElementParts(...)` with today's arguments in
  today's order (`DeepClone` keeps every `with { }` copy and the `Text is null ? null : Text with { }`
  conditional). Guards, the legibility refusal (message and `nameof(color)`), `TextContent.From`
  after the legibility check, and `RequireConsistent()` stay exactly where they are.
- **Comments to rewrite because they become false**: the summary on the 5-parameter constructor
  ("The one constructor that sets every property; every other constructor below delegates to this
  one" stays true; the caller list may add `DeepClone`, spec §6 oddity 3), and the EF constructor's
  summary, which says EF refuses "the five-parameter constructor above outright": restate it as
  refusing the parts constructor above, for the same reason (a constructor parameter binds only to a
  mapped scalar). The class summary's "the private constructors they and EF's materialisation path
  use" stays true. `TextElement` is not changed (hit 4, Leave).
- EF consequence: the set of constructors EF can bind is unchanged (`ElementParts parts` binds to
  nothing, exactly as the 5-parameter list did not), so materialisation still goes through
  `(kind, color)`. C5 and CI's Integration.Tests observe it.

### 3.4 `OverlayDesignerInfrastructureModule.AddOverlayDesignerInfrastructure` (hit 5)

Keep the guard, `AddOverlayDesignerPersistence()` and `EnrichPostgresDbContext<...>()` first and
`AddWolverineForContext` last, unchanged. Between them one fluent chain on `builder.Services` over
three `private static IServiceCollection X(this IServiceCollection services) =>` helpers (the
`AutomationInfrastructureModule` precedent), each one fluent chain, in today's registration order:

1. `AddOverlayStoreAndDomainEventHandlers`: `IOverlayRepository`, `IOverlayQuerySource`, the two
   `IDomainEventHandler<...>` registrations, `IDomainEventDispatcher`.
2. `AddClockAndIdempotencyStore`: `IClock` (singleton), `TimeProvider.System`, then the
   **ADR-0142 comment** mid-chain directly above `IIdempotencyStore` and
   `AddIdempotencyReservationSweep<OverlayDesignerDbContext>()`. TimeProvider stays in the same
   chain, above the comment, so "TimeProvider above drives ..." stays true.
3. `AddCommandHandlers`: the six `ICommandHandler<...>` registrations, generic arguments wrapped
   one per line as today.

Helper names say everything they register (the second names the idempotency store). Order and
lifetimes unchanged (C4). The class summary stays true.

### 3.5 `OverlayConfiguration.Configure` (hit 6)

`Configure` keeps the guard, then calls private static methods in today's order, then
`builder.Ignore(overlay => overlay.PendingEvents)` last. Suggested split (each <= 30 lines):

- `ConfigureOverlayKeyAndName(EntityTypeBuilder<Overlay>)`: `ToTable`, `HasKey`, `Id`, `Name`.
- `ConfigureOverlayCreation(EntityTypeBuilder<Overlay>)`: owned `Creation` + its comment +
  `Navigation(...).IsRequired()` ("The Navigation(...).IsRequired() line" stays adjacent).
- `ConfigureOverlayVersionAndArchival(EntityTypeBuilder<Overlay>)`: `Version`, `ArchivedAt` with its
  comment, the `ux_overlays_name_active` index with its comment.
- `builder.OwnsMany(overlay => overlay.Revisions, ConfigureRevisions)`, where
  `ConfigureRevisions(OwnedNavigationBuilder<Overlay, Revision>)` keeps `ToTable`, `WithOwner`,
  `HasKey`, `Id`, `Number`, `State`, the element comment block followed by
  `revisions.OwnsMany(revision => revision.Elements, ConfigureElements)`, then calls
  `ConfigureRevisionCreationTimestampsAndIndexes` (owned `Creation` with "The nested case" comment +
  `Navigation`, `PublishedAt`, `ArchivedAt`, both revision indexes) if still over 30 lines.
- `ConfigureElements(OwnedNavigationBuilder<Revision, OverlayElement>)` and, if needed,
  `ConfigureElementGeometryAndText(...)`: table, owner FK, the `ordinal` field-backed property +
  key + `Ignore(Ordinal)` with its comment, `Kind`, `Color`, `Position`/`Size` with their comment
  and `Navigation(...).IsRequired()`, `Text` with its comment.

Builder calls keep their order, arguments and string literals. Declare the methods in call order
so "the scalar mapped below" (element comment) and "the Navigation(...).IsRequired() below"
(Position/Size comment) stay true; re-read both, plus "#2022's Position/Size comment" (Text) and
"The nested case ... one level deeper" (revision `Creation`). Whitespace-only re-indentation of the
two mis-indented `.HasConversion(v => v!.Value, ...)` lines is permitted; nothing else changes.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

Coverage that already exists and is relied on (must pass unmodified):

| Member | Existing tests | Gap |
|---|---|---|
| Branch handler | `BranchDraftRevisionCommandHandlerTests` (6), `StaleVersionRejectionTests` (1 branch case) | No multi-fault refusal order, no repository call order, no log text (all use `NullLogger`) |
| Edit handler | `EditDraftRevisionCommandHandlerTests` (6), `StaleVersionRejectionTests` (1 edit case) | Same |
| `OverlayElement` | `OverlayElementTests` (9), `OverlayElementSetTests`, `OverlayTests` | No instance-identity pins, no `DeepClone` (internal, no `InternalsVisibleTo`) pins through the public API |
| Infrastructure module | `IdempotencyReservationSweepRegistrationTests`, `PostgresPoolBoundTests`, `PostgresComposition` suites | No ordered registration list |
| `OverlayConfiguration` | Integration.Tests `OverlayDesigner/*` (CI only) | No local model pin |

Gaps, pinned by **new files only** (no existing test file edited). Expected values are
**hand-written literals** captured by running against unrefactored `8ded100e`; messages are pinned
in full (`ShouldBe`), never by `ShouldContain`; error messages are literal templates in which only
fixture identifiers are interpolated, never built with the `*Failures` factories under test.

| ID | File | Pins | Counterfactual the test-writer must show RED, then revert |
|---|---|---|---|
| C0 | `tests/OverlayDesigner.Application.Tests/Fakes/RecordingOverlayRepository.cs`, `.../Fakes/CapturingLogger.cs` | Support only. An `IOverlayRepository` decorator over `InMemoryOverlayRepository` appending `"get-by-identifier"`, `"get-by-name"`, `"add"`, `"save"` to an ordered list, delegating everything; `CapturingLogger<T>` copied from `LayoutComposition.Application.Tests/Fakes` (level + rendered message) | n/a |
| C1 | `tests/OverlayDesigner.Application.Tests/Commands/BranchDraftRefusalOrderCharacterisationTests.cs` | Unknown overlay (fixed literal Guid) -> `OVERLAY_NOT_FOUND`, full message, calls `[get-by-identifier]`. Open draft + stale version (expected 7) -> `OVERLAY_REVISION_STALE` with `... since version 7 (now 1) ...`, no `get-by-name`, no `save`. Fully archived + name held elsewhere + stale -> `OVERLAY_REVISION_STALE`, no `get-by-name`. Open draft at the right version -> `OVERLAY_NO_PUBLISHED_REVISION` naming draft 1, no `get-by-name`, no `save`. Fully archived + name held -> `OVERLAY_NAME_TAKEN` full message, calls `[get-by-identifier, get-by-name]`, no `save`. Fully archived, name free -> success, `[get-by-identifier, get-by-name, save]`. Healthy Published chain at revision 1 -> success revision **2**, calls `[get-by-identifier, save]`, exactly one Information entry reading `Branched draft revision 2 on overlay <overlay guid> by <operator guid>.` with operator Guid distinct from the overlay's; no log on any refusal | Move the stale-version check below the all-not-Published block: the "open draft + stale" case answers `OVERLAY_NO_PUBLISHED_REVISION`. (Second, for the log: log `overlay.Revisions[0].Number` instead of `branched.Number` -> `revision 1`.) |
| C2 | `tests/OverlayDesigner.Application.Tests/Commands/EditDraftRefusalOrderCharacterisationTests.cs` | Empty set + unknown overlay -> `OVERLAY_ELEMENTS_EMPTY`, **zero** repository calls; 9 elements + unknown overlay -> `OVERLAY_ELEMENTS_TOO_MANY`, `... 9 were submitted.`, zero calls; unknown overlay -> `OVERLAY_NOT_FOUND`; stale + missing revision -> `OVERLAY_REVISION_STALE`; stale + Published revision -> `OVERLAY_REVISION_STALE`; missing revision -> `OVERLAY_REVISION_NOT_FOUND` full message; Published revision -> `OVERLAY_REVISION_NOT_DRAFT` `Revision is in state 'Published'; only Draft revisions can be edited in place.`, no `save`, elements unchanged; success on draft **2** of a branched chain -> returns 2, calls `[get-by-identifier, save]`, one Information entry `Edited draft revision 2 on overlay <overlay guid>.` | Move `ValidateElements` below the lookup: "empty set + unknown overlay" answers `OVERLAY_NOT_FOUND`. |
| C3 | `tests/OverlayDesigner.Domain.Tests/Overlay/OverlayElementConstructionCharacterisationTests.cs` | Public API only. `TextElement`/`Box`/`Ellipse`: `Kind`, and `Position`/`Size`/`Color` **the same instances** passed in (`ShouldBeSameAs`); `Text` value and font size for text, null for shapes; `Ordinal` 0. Refusal order: null position with an illegible colour -> the position guard's exception (type and `ParamName`); illegible colour with invalid text -> the legibility `ArgumentException` (`ParamName` `color`, full message for one fixed colour) before any text refusal. Deep copy via `Overlay.CreateDraft` with [text, box, ellipse]: each stored element value-equal per member to its source, `Position`/`Size`/`Color`/`Text` **not** the same instances, `Kind` carried, ordinals `[0, 1, 2]`; same after `BranchDraft` against the Published revision's elements; a box's `Text` stays null | In `DeepClone`, pass `Color` instead of `Color with { }`: the not-same-instance assertion fails. |
| C4 | `tests/Architecture.Tests/Persistence/OverlayDesignerRegistrationCharacterisationTests.cs` | Mirror `LayoutCompositionRegistrationCharacterisationTests`: via `PostgresComposition.Compose("overlay-designer", ...)`, the ordered descriptors whose service or implementation type (recursively through generic arguments) is declared in a `SmartSentinelEye.OverlayDesigner.*` assembly, plus `IClock`, `IDomainEventDispatcher`, `TimeProvider`, `IIdempotencyStore`, rendered `"{Lifetime} {Service} -> {Implementation or factory/instance}"`, equal a literal list. **No total descriptor count** | Swap the `IOverlayRepository` and `IOverlayQuerySource` registrations (or make `IClock` scoped). |
| C5 | `tests/Architecture.Tests/Persistence/OverlayDesignerModelCharacterisationTests.cs` | Mirror `AutomationModelCharacterisationTests` (J3: no Infrastructure.Tests project here): `OverlayDesignerDbContext` over `UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")`, the design-time model via the **public metadata API**: per entity type (incl. owned `Creation`, `overlay_revisions`, `overlay_revision_elements`, `Position`, `Size`, `Text`): table, owned-ness, key; per property: column, store type, CLR type, nullability, max length, converter provider type, concurrency token, value generation; foreign keys; indexes with name, filter and uniqueness; navigations and required-ness; and `OverlayElement`'s constructor binding parameter names `[kind, color]` where the metadata API exposes them. Equal a literal table. **Not** `Model.ToDebugString()` | Drop `.HasFilter("archived_at IS NULL")` from `ux_overlays_name_active`. (Second: rename the EF constructor's `color` parameter to `colour` -> the binding pin or model build fails.) |

A characterisation test that arrives red on the unrefactored code is a test defect and is fixed in
the test, never by a production change in this spec. None of these classes is in
`Integration.Tests`, so no `ci-shards/*.filter` entry is needed. Integration.Tests need Docker and
run only in CI.

## 5. Measurement and run commands (before and after, quoted in the PR)

```sh
dotnet build src/OverlayDesigner/Infrastructure/SmartSentinelEye.OverlayDesigner.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep OverlayDesigner
```

Before (`8ded100e`): 12 lines (spec §2). After: 7 lines, `OverlayElement.cs` (`TextElement`, line
moved) plus the six migrations, unchanged.

```sh
dotnet test tests/OverlayDesigner.Domain.Tests -c Release        # 297 passed on 8ded100e
dotnet test tests/OverlayDesigner.Application.Tests -c Release   # 49 passed on 8ded100e
dotnet test tests/Architecture.Tests -c Release                  # incl. PrimitiveBoundaryTests, HandlerDeconstructionTests, EventMetadataFabDeclarationTests, OutboxCommitTests, IdempotencyReservationSweepRegistrationTests, C4, C5
dotnet format src/OverlayDesigner/Domain/SmartSentinelEye.OverlayDesigner.Domain.csproj --verify-no-changes
dotnet format src/OverlayDesigner/Application/SmartSentinelEye.OverlayDesigner.Application.csproj --verify-no-changes
dotnet format src/OverlayDesigner/Infrastructure/SmartSentinelEye.OverlayDesigner.Infrastructure.csproj --verify-no-changes
```

Integration.Tests `OverlayDesigner/*`: CI. Latency: not on a §IV leg (spec §7), so no latency test
is required.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Name-taken lookup leaks onto the Published path (refuses every healthy branch) | §3.1 moves the whole `if` block; C1 "healthy Published -> no `get-by-name`" |
| Refusal order drift in branch or edit | C1, C2 multi-fault cases |
| `overlay.Id.Value` substituted for the command's identifier in error messages | §3.1 binding; C1 literal messages |
| EF binds a different `OverlayElement` constructor, or none | §3.3 keeps `(kind, color)` names; C5; CI round-trip tests |
| `DeepClone` loses a `with { }` copy (EF re-keys one owned graph onto two rows, FR-007 of spec 300) | §3.3; C3 instance pins; CI `OverlayRevisionLifecycleIntegrationTests` |
| A comment saying "above"/"below" becomes false after a move | §3 per-section re-read list; reviewer reads them |
| DI order or lifetime change | C4 ordered list |
| EF configuration reorder alters the model | C5 logical model table |
| Collision with #2891 or merged siblings | Disjoint files (§1) |
