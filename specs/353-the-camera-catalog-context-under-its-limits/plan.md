# Plan 353: The camera catalog context under its limits

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2895.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 3 of 4 in-scope hits by shape only; the 1 left and the 2 migrations are named with reasons (spec §2). |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened; no ADR, no constitution edit. |
| §II (no primitives on Domain models) | No Domain file is edited. |
| §III (no cross-context refs) | No reference added; `Shared.Contracts` untouched. |
| §IV (latency) | N/A: management-console CRUD and model build only (spec §7). |
| ADR-0050 | Both `Log.cs` files untouched; every log call keeps method, arguments and order. |
| ADR-0113 | The rename's Layer-1 version check stays the first refusal after the fab-scoped lookup. |
| ADR-0141 | Extracted refusal helpers return `Option<TError>`, not a nullable; no new nullable parameter (spec J4). |
| Spec 029 FR-006 / FR-005 | The fab-scoped lookup stays first in the rename; the listing's fab predicate stays before the count. |

**Guards that read this context, and what they tolerate.**

- `HandlerDeconstructionTests` scans bodies of `public async Task ... Handle/HandleAsync(` for the
  first `var (...) = <parameter>;`. It **does** scan `RenameCameraCommandHandler` (`var (fab,
  camera, name, expectedVersion, renamedBy) = command;`) and tolerates `_` at any position and
  locals named after their own field. It does **not** scan `ListCamerasQueryHandler` (`(var fabs,
  ...) = query;`, spec §6 oddity 3) or any `private` method. So: the rename's line may change only
  by replacing `expectedVersion` with `_` (§3.1); the listing's line stays verbatim.
- `EventMetadataFabDeclarationTests`: the only `new EventMetadata(` in this context is in
  `Application/EventHandlers/*`. None is touched; no extracted helper may construct one.
- `PrimitiveBoundaryTests`: walks Domain public state. No Domain file is touched.
- `NameMutabilityConventionTests`: reads the CameraCatalog **Api** route shape (`{camera:guid}`);
  no Api file is touched.
- `OutboxCommitTests` (IL scan for direct `SaveChanges` in the CameraCatalog Infrastructure,
  Application and Api assemblies): nothing here adds a `SaveChanges` call.
- `BoundaryTests`, `IdempotencyReservationSweepRegistrationTests`, `PostgresPoolBoundTests`,
  `Persistence/DependencyHealthCheckGuardTests`: references and composition. No reference, no
  registration and no `CameraCatalogInfrastructureModule` change, so no registration
  characterisation is needed (unlike specs 349 / 350).
- `ConcurrencyConflictDeclarationTests`, `EndpointScopeDeclarationTests`,
  `IdempotentKeyReuseDeclarationTests`: read `src/*/Api/**` only.

**Concurrency with the open siblings.** #2893 (`D:\Github\sse-2893`, spec 351) touches
`src/StreamDistribution/**`, `tests/StreamDistribution.*` and
`tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs`.
#2894 (`D:\Github\sse-2894`, spec 352) touches `src/AuditObservability/**` and three
`tests/Architecture.Tests/Persistence/Audit*` files. This plan touches only
`src/CameraCatalog/{Application,Infrastructure}` (non-migration), new files under
`tests/CameraCatalog.Application.Tests/`, and one new `CameraCatalog*`-named file in
`tests/Architecture.Tests/Persistence/`. None of the merged siblings (341, 342, 345-350) touched
`src/CameraCatalog` outside `Api/` (#2896, spec 341). Disjoint.

## 2. Shape

One bounded context, two layers edited (Application, Infrastructure). No new type, no new public
member, no project, no project reference, no migration, no messaging change, no AppHost change.

## 3. Production shape (binding on the engineer)

Every extracted method: **<= 4 parameters (CancellationToken counts), <= 30 lines (S138 counting),
complexity <= 10**, verified with the §5 grep after each commit. Statements move verbatim;
comments move with the code they explain. Method names are suggestions; the constraints are
binding. After each move, **re-read every comment listed per section** against its new position;
one that has become false is a defect a characterisation test cannot catch.

### 3.1 `RenameCameraCommandHandler.HandleAsync` (hit 1, S138 33)

`HandleAsync` keeps, in order: the guard; the deconstruction line, changed **only** to
`var (fab, camera, name, _, renamedBy) = command;` (`expectedVersion` is no longer read there:
IDE0059/S1481 and CLAUDE.md's discard rule force the `_`; the guard tolerates it); the spec 029
FR-006 comment, `GetWithinFabAsync`, the not-found block with its log call; `var renaming =
found.Value;`; then

```csharp
var refusal = await RefuseStaleVersionOrTakenNameAsync(renaming, command, cancellationToken);
if (refusal.HasValue)
{
    return Failure(refusal.Value);
}
```

then the terminal-rule comment and `try`/`catch`, the "Saved unconditionally" comment and
`SaveAsync`, the `RenamedCamera` log and `Success`, all unchanged.

`private async Task<Option<RenameCameraError>> RefuseStaleVersionOrTakenNameAsync(Camera renaming,
RenameCameraCommand command, CancellationToken cancellationToken)` (wrapped one parameter per line)
opens with `var (fab, camera, name, expectedVersion, _) = command;` (spec J1: five distinct types,
so a transposition does not compile), then holds **verbatim** the "Version before terminal state"
comment and stale check (log `(camera, expectedVersion, renaming.Version)`, error
`(camera.Value, expectedVersion, renaming.Version)`), then the "Uniqueness last ..." comment block,
`ExistsByNameAsync(fab, name, Option<CameraIdentifier>.Some(camera), cancellationToken)` and the
name-taken block (log `(camera, name)`, error `(name.Value, fab.Value)`). Each refusal returns
`Option<RenameCameraError>.Some(RenameCameraFailures.X(...))` (or the codebase's existing
`Option` idiom for this; mirror spec 350's `RefuseUnlessDraft`); the end returns `None`.

Comments to re-read: "every later step **here** would otherwise leak its existence" (stays in
`HandleAsync`; the later steps now include the helper; still true). "Version before terminal
state" (the terminal step remains after the helper call; true). "Uniqueness last" (already true
only among the handler's own checks, spec §6 oddity 1; keep verbatim). "This is a guard, not a
guarantee" (true). Expected `HandleAsync` S138 count: about 25.

### 3.2 `ListCamerasQueryHandler.HandleAsync` (hit 2, S138 45)

Two extractions, both `private`:

1. `private static Option<ListCamerasError> RefuseInvalidParameters(string sort, string order, int
   offset, int limit)` (wrapped one per line) holding the four `if` blocks **verbatim and in
   today's order**: sort field, sort order, `offset < 0 || limit <= 0` (literal message unchanged),
   `limit > ListCamerasDefaults.MaximumLimit`; `None` at the end. `HandleAsync` calls it right
   after the deconstruction line and returns `Failure(refusal.Value)` when it has a value.
2. `private IQueryable<Camera> VisibleCameras(IReadOnlyList<FabIdentifier> fabs, bool
   includeRetired)` holding the FR-005 comment and `cameras.Cameras.Where(... fabs.Contains(...))`,
   the FR-007 comment block and the `!includeRetired` filter, returning `visible`. Instance method
   (reads the `cameras` primary-constructor parameter). `HandleAsync` reads
   `var visible = VisibleCameras(fabs, includeRetired);`.

Unchanged in `HandleAsync`: the guard, the deconstruction line (verbatim), `var descending = order
== "desc";` (its position relative to the filters is free, both are pure, but keep it where it is),
the spec 055 comment block with `query.TrimmedFragment` and the `NameContains` filter (spec J4),
`SortBy`, `CountAsync`, the `Skip/Take/Select` projection (including `camera.Url.Value`, not moved,
not logged) and `Success`. `SortBy` and its comments are untouched.

Comments to re-read: the spec 055 block says "it belongs here for the same reason as **the two
above**" and "the count is taken from this query". After the move the two filters sit inside
`VisibleCameras`, called above. If the engineer judges "the two above" misleading, the **only**
permitted edit is naming them ("as the fab and retired filters in `VisibleCameras`"); the reason
stays. The FR-007 comment "filtered **here** rather than at the endpoint so the count and the page
agree" stays true (still before the count). Expected `HandleAsync` S138 count: about 26.

### 3.3 `CameraConfiguration.Configure` (hit 4, S138 63)

`Configure` keeps the guard, then calls `private static void` helpers taking
`EntityTypeBuilder<Camera> builder`, **in today's builder-call order**, then
`builder.Ignore(camera => camera.PendingEvents);` last. Suggested split (each <= 30 lines):

- `ConfigureKeyAndFab`: `ToTable("cameras")`, `HasKey`, `Id`, `Fab`.
- `ConfigureName`: the spec 033 comparer comment block and the `Name` property with its
  `ValueComparer<CameraName>` (lambdas unchanged, character for character).
- `ConfigureAddressAndStatus`: `Url`, `Status` (`HasMaxLength(32)` stays a literal).
- `ConfigureRegistration`: the "Owned reference ..." comment, `OwnsOne(... Registration ...)` and
  `builder.Navigation(camera => camera.Registration).IsRequired();` (adjacent, so "The
  Navigation(...).IsRequired() line" stays true).
- `Version` may stay inline in `Configure` or get its own helper; either keeps the order.
- `ConfigureNormalizedNameIndex`: the #1434 generated-column comment, the
  `Property<string>(NormalizedNameProperty)` shadow property, the uniqueness comment block and
  `HasIndex(CameraFabProperty, NormalizedNameProperty)` with name, `IsUnique`, `HasMethod("btree")`,
  `HasFilter("status <> 'Decommissioned'")`.

Builder calls keep their order, arguments and string literals; the two `const`s and the class
summary are unchanged. Comments to re-read: "the comment above it claimed case-insensitivity" (a
historical reference to the old index comment; stays true), "Named here rather than spelled at
each call site" on `NormalizedNameProperty` (unchanged member; true).

### 3.4 Hit 3, `Camera.Register`: left (spec J2). No Domain edit.

## 4. Characterisation tests (phase 4a, test-writer, before any production edit)

Coverage that already exists and is relied on (must pass unmodified):

| Member | Existing tests | Gap |
|---|---|---|
| Rename handler | `RenameCameraCommandHandlerTests` (12), `ExistsByNameExclusionTests` (7) | Only one two-fault case (taken vs stale, codes only, run separately); no retired+taken precedence; no repository call order; no log text (all use `NullLogger`) |
| List handler | `ListCamerasQueryHandlerTests` (14 methods), `ListCamerasPagingOrderTests` (2), `CameraNameFilterTests` (8 methods) | One fault at a time, codes only: no multi-fault precedence, no full messages; no single case combining unheld fab + retired + fragment + paging |
| `CameraConfiguration` | Integration.Tests `CameraCatalog/*` (CI only) | No local model pin |

Gaps, pinned by **new files only** (no existing test file, including the existing `Fakes/*`, is
edited). Expected values are **hand-written literals** captured by running against unrefactored
`ef604f93`; messages are pinned in full (`ShouldBe`), never by `ShouldContain`, and are literal
templates in which only fixture identifiers are interpolated, never built with the `*Failures`
factories or error records under test. Every log slot gets a **distinct** value (expected version
differs from actual; fab, name and camera differ), so a transposed argument fails.

| ID | File | Pins | Counterfactual the test-writer must show RED, then revert |
|---|---|---|---|
| C0 | `tests/CameraCatalog.Application.Tests/Fakes/RecordingCameraRepository.cs`, `.../Fakes/CapturingLogger.cs` | Support only. An `ICameraRepository` decorator over `InMemoryCameraRepository` appending `"get-within-fab"`, `"get-by-identifier"`, `"exists-by-name"`, `"add"`, `"save"` to an ordered list and recording each `ExistsByNameAsync` call's `(fab, name, excluding)`, delegating everything; `CapturingLogger<T>` copied from `OverlayDesigner.Application.Tests/Fakes` (level + rendered message) | n/a |
| C1 | `tests/CameraCatalog.Application.Tests/Commands/RenameRefusalOrderCharacterisationTests.cs` | Unknown camera (fixed literal Guid) -> `CAMERA_NOT_FOUND`, full message, calls `[get-within-fab]`, one Warning `Rejected rename for camera <guid>: not found in the caller's fab.`. Stale (expected = actual + 7) **and** name taken -> `CAMERA_VERSION_STALE`, full message with both numbers, calls `[get-within-fab]`, one Warning with `expected version <e>, actual <a>` in that order. Stale **and** retired -> `CAMERA_VERSION_STALE`. Retired **and** name taken by another active camera -> `CAMERA_NAME_TAKEN` (spec §6 oddity 1), full message naming name then fab, calls `[get-within-fab, exists-by-name]`, the recorded exclusion is `Some(<this camera>)`, one Warning naming camera and name, no `save`. Retired, name free -> `CAMERA_RETIRED`, full message, calls `[get-within-fab, exists-by-name]`, **zero** log entries (oddity 2), stored name unchanged. Success -> returns the camera's identifier, calls `[get-within-fab, exists-by-name, save]`, exactly one entry, Information, `Renamed camera <guid> to <new name>.` | Swap the stale-version check below the name-taken check: "stale and name taken" answers `CAMERA_NAME_TAKEN` and the call list gains `exists-by-name`. (Second, for the log slots: pass `(camera, renaming.Version, expectedVersion)` to `RejectedCameraRenameStaleVersion` -> the Warning text fails. Third: pass `Option<CameraIdentifier>.None` as the exclusion -> the success case answers `CAMERA_NAME_TAKEN` on a case-only rename, or the recorded-exclusion assertion fails.) |
| C2 | `tests/CameraCatalog.Application.Tests/Queries/ListCamerasRefusalOrderCharacterisationTests.cs` | Sort `"colour"` + order `"sideways"` + offset -1 + limit 500 -> `CATALOG_INVALID_SORT_FIELD`, message `Unknown sort field 'colour'. Allowed: name, registeredAt.`. Sort `"name"` + order `"sideways"` + offset -1 + limit 500 -> `CATALOG_INVALID_SORT_ORDER`, `Unknown sort order 'sideways'. Allowed: asc, desc.`. Valid sort/order + offset -1 + limit 500 -> `CATALOG_INVALID_PAGINATION`, `Offset must be non-negative and limit must be positive.`. Offset 0 + limit 201 -> `CATALOG_LIMIT_EXCEEDED`, `Requested limit 201 exceeds the maximum allowed (200).`. Limit 200 -> success. Combined filter: held fab `munich` with five live cameras (two matching fragment `inlet`, one of them by different letter case), one retired matching camera in `munich`, two matching cameras in unheld `dresden`; listing `fabs=[munich]`, `includeRetired=false`, fragment `" INLET "`, sort `name` `asc`, offset 1, limit 1 -> total **2**, one row, the literal name of the second match; same with `includeRetired=true` -> total **3** and rows in today's order (literal names, retired row's status `Decommissioned`) | Move the sort-order check above the sort-field check inside the validation: the first case answers `CATALOG_INVALID_SORT_ORDER`. (Second: in the retired filter, drop the `!includeRetired` condition's effect by removing the `Where(... != CameraStatus.Decommissioned)` -> the `includeRetired=false` total reads 3.) |
| C3 | `tests/Architecture.Tests/Persistence/CameraCatalogModelCharacterisationTests.cs` | Mirror `OverlayDesignerModelCharacterisationTests` (spec J3): `CameraCatalogDbContext` over `UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")`, the model via the **public metadata API**, per entity type (`Camera`, owned `Registration`, and any other entity this DbContext maps, e.g. the idempotency-key table): table, owned-ness, key; per property: column, store type, CLR type, nullability, max length, converter provider type, computed SQL and stored-ness, concurrency token, value generation, shadow-ness; foreign keys; indexes with name, columns, filter, method and uniqueness; navigations and required-ness; constructor binding. Equal a literal table. **Not** `Model.ToDebugString()`. Plus: the `Name` property's value comparer treats `CameraName.From("Line-3")` and `CameraName.From("line-3")` as **not** equal and two `Line-3` as equal; plus `context.Database.HasPendingModelChanges()` is false | Drop `.HasFilter("status <> 'Decommissioned'")` from `ux_cameras_fab_name_normalized_active`: the index line fails (and the snapshot check). (Second: remove the `ValueComparer<CameraName>` argument: the comparer assertion fails.) |

A characterisation test that arrives red on the unrefactored code is a test defect and is fixed in
the test, never by a production change in this spec. None of these classes is in
`Integration.Tests`, so no `ci-shards/*.filter` entry is needed. Integration.Tests need Docker and
run only in CI. The Domain layer is not touched, so no Domain characterisation is added; its 80
tests must still pass unmodified.

## 5. Measurement and run commands (before and after, quoted in the PR)

```sh
dotnet build src/CameraCatalog/Infrastructure/SmartSentinelEye.CameraCatalog.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep CameraCatalog
```

Before (`ef604f93`): 6 lines (spec §2). After: 3 lines, `Camera.cs(37,34)` S107 plus the two
migrations, unchanged.

```sh
dotnet test tests/CameraCatalog.Domain.Tests -c Release        # 80 passed on ef604f93
dotnet test tests/CameraCatalog.Application.Tests -c Release   # 76 passed on ef604f93
dotnet test tests/Architecture.Tests -c Release                # incl. HandlerDeconstructionTests, OutboxCommitTests, C3
dotnet format src/CameraCatalog/Application/SmartSentinelEye.CameraCatalog.Application.csproj --verify-no-changes
dotnet format src/CameraCatalog/Infrastructure/SmartSentinelEye.CameraCatalog.Infrastructure.csproj --verify-no-changes
```

Integration.Tests `CameraCatalog/*`: CI. Latency: not on a §IV leg (spec §7).

## 6. Analyzer-forced and convention-forced shapes (declare in the PR)

- **IDE0059 / S1481** in §3.1: `expectedVersion` becomes unread in `HandleAsync`, so its
  deconstruction slot becomes `_`. This is the only edit to a handler deconstruction line.
- **Signature wrapping**: `RefuseStaleVersionOrTakenNameAsync` and `RefuseInvalidParameters` do not
  fit on one line; one parameter per line (CLAUDE.md).
- **`static` where possible** (CA1822): `RefuseInvalidParameters` and every `CameraConfiguration`
  helper are `static`; `VisibleCameras` and the rename helper are instance methods because they
  read `cameras` / `logger`.
- **ADR-0141**: refusal helpers return `Option<TError>`; no new nullable parameter (spec J4).
- No DI change, so the S3241 / fluent-chain shapes of specs 349-350 do not arise.

## 7. Security

No new logging: every log call that moves (`RejectedCameraRenameStaleVersion`,
`RejectedCameraRenameNameTaken`) keeps its arguments, and none takes the camera's `RtspUrl`. The
projection that carries `camera.Url.Value` into `CameraSummaryDto` does not move. The fab predicate
of the listing moves into `VisibleCameras` and must still be applied on every path before the
count; C2's combined case pins it with an unheld fab holding matching cameras. A short
security-reviewer pass is warranted on that one point (fab scoping of the listing and of the
rename lookup, and "no new log of an address"); `ICameraFabGuard`'s endpoint is in `Api/` and is
not touched.

## 8. Risks

| Risk | Mitigation |
|---|---|
| Refusal order drift in rename (stale vs taken vs retired) | §3.1 moves both checks together, in order; C1 multi-fault cases |
| Rename helper uses `renaming.Id` instead of the command's `camera` in errors or logs | §3.1 binding (command's `camera`); C1 literal messages (values equal, but the binding is stated) |
| Validation precedence drift in the listing | §3.2 verbatim, in order; C2 four-fault cases |
| Fab or retired filter applied after the count, or dropped | §3.2; C2 combined total; existing `The_listing_omits_cameras_in_fabs_the_caller_does_not_hold` |
| EF configuration reorder or a dropped comparer alters the model | §3.3 order; C3 model table, comparer assertion and snapshot check |
| A comment's reason becomes false after a move | §3.1-3.3 re-read lists; reviewer reads them |
| Collision with #2893, #2894 or merged siblings | Disjoint files (§1) |
