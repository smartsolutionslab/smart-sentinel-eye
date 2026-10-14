# Spec 353: The camera catalog context under its limits

**Feature Branch**: `refactor/2895-camera-catalog-metrics` (cut from `origin/develop` at `ef604f93`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked explicitly (§6).

**Input**: Issue [#2895](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2895),
*Clear the ADR-0084 metric warnings in Camera Catalog's Domain, Application and Infrastructure (6)*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Spec number.** 353. Checked on 2026-10-14: `origin/develop` (`ef604f93`) highest 350; every
remote branch via `git ls-tree` highest 351 (`origin/refactor/2893-stream-distribution-metrics`);
worktree `sse-2893` holds 351 (#2893, stream distribution); worktree `sse-2894` holds unpushed 352
(#2894, audit observability); the two open PRs (a Dependabot bump and
`claude/beautiful-mendel-jz9d5x`) carry nothing above 350. Highest visible 352, so 353. Re-check
before the PR is opened.

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S138 <= 30 lines/method, S107 <= 4
parameters; advisory `warning`), ADR-0144 (autonomous lane; phase 4a colours; the lane may not
weaken a gate), ADR-0036 (smallest change), ADR-0050 (`[LoggerMessage]` fields come from the
template), ADR-0113 (Layer-1 expected-version check before any mutation), ADR-0120 (a camera is
addressed by identifier, so its name is renameable), ADR-0141 (`Option<T>` for absences in
Application), constitution §II (no primitives on a Domain model).

---

## 1. What was measured (premise verified, not assumed)

The issue's command does not run as written (`src/CameraCatalog` holds no project file). The
equivalent, which compiles Domain and Application transitively:

```sh
dotnet build src/CameraCatalog/Infrastructure/SmartSentinelEye.CameraCatalog.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep CameraCatalog
```

Result on `ef604f93`: 0 errors, **6 hits, exactly the 6 the issue lists, at the same lines. None
is stale.** Two sit in generated `Migrations/` (out of scope, §8). The Api layer
(`CameraEndpoints`) was already split by merged #2896 and is excluded by the grep.

Baseline test counts on `ef604f93` (`dotnet test -c Release`): CameraCatalog.Domain.Tests **80**,
CameraCatalog.Application.Tests **76**, all green. There is **no** CameraCatalog.Infrastructure.Tests
project; Infrastructure is reached from `Architecture.Tests` (which composes
`CameraCatalogDbContext` through `Persistence/PostgresComposition.cs`) and from `Integration.Tests`
(Docker, CI only).

## 2. Decision per hit

| # | File:line | Member | Rule (measured) | Decision |
|---|---|---|---|---|
| 1 | `Application/Commands/Handlers/RenameCameraCommandHandler.cs:14` | `HandleAsync` | S138 (33) | **Clear** by extraction |
| 2 | `Application/Queries/Handlers/ListCamerasQueryHandler.cs:15` | `HandleAsync` | S138 (45) | **Clear** by extraction |
| 3 | `Domain/Camera/Camera.cs:37` | `Camera.Register` (public static factory) | S107 (5) | **Leave**: called by `RegisterCameraCommandHandler` and at 14 call sites in 9 test files; a parameter object changes every caller and fails "tests unmodified" (J2) |
| 4 | `Infrastructure/Persistence/Configurations/CameraConfiguration.cs:28` | `Configure` | S138 (63) | **Clear** by extraction |
| 5 | `.../Migrations/20260810164633_FabScopeCameras.cs:10` | `Up` | S138 (40) | **Out of scope**: generated migration |
| 6 | `.../Migrations/20260823194632_CaseInsensitiveCameraNames.cs:11` | `Up` | S138 (41) | **Out of scope**: generated migration |

**Cleared: 3** (all S138). **Left: 1** (public Domain factory). **Out of scope: 2** (migrations).
Expected after-count of the §1 grep: **3** (hit 3 plus the two migrations); with
`| grep -v Migrations` appended, **1**. Hit 3's file is not edited, so its line stays 37.

No `[LoggerMessage]` hit exists in this context (`Application/Log.cs` and `Infrastructure/Log.cs`
report nothing).

## 3. User story

### US1 (P1): Camera Catalog's non-Api layers meet ADR-0084 wherever that is mechanical

As a maintainer, I want the 3 clearable hits gone, each by extracting cohesive private methods,
**without** any change an HTTP caller, another context, a log reader or the database could observe.

**Why P1 / independently shippable.** It is the whole issue: one context, no contract, no
migration, no runtime resource, no Api file, no Domain file.

#### Acceptance scenarios

Characterisation (all must hold **before** the refactor and pass **unmodified** after):

```gherkin
Scenario: Every existing Camera Catalog test passes unmodified (happy path)
  Given CameraCatalog.Domain.Tests (80) and CameraCatalog.Application.Tests (76)
  When the refactor commits land
  Then all pass and `git diff <last 4a commit>..HEAD -- tests/` is empty

Scenario: Renaming refuses in today's order (conflict)
  Given a camera at its registration version whose target name another active camera in the fab holds
  When it is renamed with an expected version 7 higher than its own
  Then the answer is CAMERA_VERSION_STALE with today's exact message, the name is never looked up and nothing is saved
  Given a retired camera whose target name another active camera in the fab holds
  When it is renamed at the right version
  Then the answer is CAMERA_NAME_TAKEN, not CAMERA_RETIRED, after exactly one name lookup that excludes the camera itself
  Given a retired camera and a free name
  Then the answer is CAMERA_RETIRED, nothing is saved, and nothing is logged
  Given a free name
  Then the repository is saved once and the log reads exactly today's text

Scenario: Listing refuses bad parameters in today's order (bad request)
  Given an unknown sort field, an unknown sort order, a negative offset and a limit over the maximum together
  Then the answer is CATALOG_INVALID_SORT_FIELD with today's exact message
  Given a valid sort field with the other three faults
  Then the answer is CATALOG_INVALID_SORT_ORDER
  Given valid sorting, a negative offset and a limit over the maximum
  Then the answer is CATALOG_INVALID_PAGINATION, not CATALOG_LIMIT_EXCEEDED

Scenario: Listing filters before it counts (auth: fab scoping)
  Given cameras in a held fab and an unheld fab, some retired, some matching a name fragment
  When the caller lists without retired cameras, filtered by the fragment, one page at a time
  Then the total and the rows describe exactly the live, matching cameras of the held fab, in today's order

Scenario: The EF model is unchanged
  Then CameraCatalogDbContext's logical model, read through the public metadata API, equals the baseline captured on ef604f93
  And the Name property's comparer still tells "Line-3" from "line-3"
  And the committed model snapshot still describes the model
```

**Auth scenario.** No authorisation boundary moves: no Api file, no scope, no `RequireScope`, no
fab resolution, no idempotency-key handling, no `ICameraFabGuard` endpoint is touched. The two
fab-scoping rules inside touched members keep their position and are pinned: the rename lookup
stays fab-scoped and first (spec 029 FR-006), and the listing's fab predicate stays applied before
the count (FR-005), now inside an extracted private method.

#### Independent test procedure (end to end)

1. The `dotnet test` runs of plan §5, green before and after.
2. The grep of §1 prints exactly the 3 rows of §2's "Leave" and "Out of scope" after the change.
3. CI's Integration.Tests `CameraCatalog/*` green, notably `RegisterCameraIntegrationTests` and
   `CrossFabCameraIntegrationTests`: they round-trip cameras through real Postgres (the EF
   configuration hit 4 touches) and exercise the fab-scoped listing over HTTP. Docker only, so
   observed in CI, not locally.

## 4. Functional requirements

- **FR-001** The 3 "Clear" hits of §2 report nothing after the change. If an extraction leaves a
  hit, the PR says so with the measured value; it is never suppressed.
- **FR-002** No public or `protected` signature changes: both handlers and their constructors,
  `Camera.Register`, `CameraConfiguration.Configure` and `CameraConfiguration.NormalizedNameProperty`,
  `ListCamerasQuery`, `RenameCameraCommand`, the error hierarchies, `Shared.Contracts`, `Log.cs`.
- **FR-003** For every input: the order of refusals and of repository calls (`GetWithinFabAsync`,
  `ExistsByNameAsync`, `SaveAsync`), the exclusion passed to `ExistsByNameAsync`, error codes and
  messages, the log calls (method, arguments, order), the returned value and, for the listing, the
  total, the rows and their order are identical.
- **FR-004** The EF configuration produces the same model, including the `Name` property's
  case-sensitive value comparer.
- **FR-005** No Domain file changes (so no new primitive on a Domain model, constitution §II).
- **FR-006** No suppression, `#pragma`, threshold, `.editorconfig` or `SonarLint.xml` change; no
  guard edited; no file under `Api/` or `Migrations/` touched; no existing test file edited.

## 5. Success criteria

- **SC-001** Before/after hit lists quoted in the PR body; after = the 3 rows of §2.
- **SC-002** Characterisation observed green on unrefactored code, green and unmodified after; both
  outputs quoted, each with its observed counterfactual (plan §4).
- **SC-003** `dotnet build -c Release` 0 errors; `dotnet format --verify-no-changes` clean on the
  Application and Infrastructure projects; coverage gates hold (Domain >= 90 %,
  Application >= 80 %).

## 6. Judgement calls (marked)

- **J1. The rename helper takes the command, not its fields.** The stale-version and name-taken
  refusals need the camera, fab, name and expected version plus the loaded aggregate and a
  `CancellationToken`: six values, over S107's four. Passing the `RenameCameraCommand` keeps the
  helper at three parameters. Its fields have five distinct types, so the helper's own
  deconstruction cannot transpose silently (a swap is a compile error), which is the risk
  `HandlerDeconstructionTests` exists for and does not scan private methods.
- **J2. Leaving the public Domain factory.** `Camera.Register(fab, name, url, registeredBy, clock)`
  could take a parameter object, but `RegisterCameraCommandHandler` and 14 test call sites would
  change. "Tests unmodified" decides it. Precedent: spec 348 J2, spec 350 J2.
- **J3. The model characterisation lives in `Architecture.Tests`.** This context has no
  Infrastructure test project, and creating one is out of proportion for one class.
  `Architecture.Tests` already references `CameraCatalog.Infrastructure` and composes its DbContext.
  Precedent: spec 350 J3 (`OverlayDesignerModelCharacterisationTests`).
- **J4. The name-fragment filter stays in `ListCamerasQueryHandler.HandleAsync`.** Extracting it
  too would mean a new `string? fragment` parameter in Application, adding to the ADR-0141
  nullable-parameter baseline CLAUDE.md measures. Extracting validation plus the fab and retired
  filters already brings the method under 30 lines.

**No bug fix is folded in.** Oddities found and **pinned as-is, not changed**:

1. A **retired** camera renamed onto a name another active camera holds answers
   `CAMERA_NAME_TAKEN`, not `CAMERA_RETIRED`: the handler's uniqueness check runs before the
   aggregate's terminal rule. The handler's comment "Uniqueness last" is therefore true only among
   the handler's own checks. Pinned; the comment is kept verbatim.
2. The `CAMERA_RETIRED` refusal is the only rename refusal that **logs nothing**; the other three
   each log a warning. Pinned (C1 asserts zero entries).
3. `ListCamerasQueryHandler` deconstructs with the `(var a, var b, ...) = query;` spelling, which
   `HandlerDeconstructionTests`' pattern (`var (...) = x;`) does not match, so the guard does not
   scan this handler today (the same gap spec 350 oddity 2 recorded). Not changed here.

## 7. Latency budget impact (constitution §IV)

**N/A, with reason.** None of the touched members is on a §IV leg. Renaming a camera and listing
cameras are management-console CRUD; `CameraConfiguration` runs once per `DbContext` model build.
*Camera -> SFU* belongs to Stream Distribution and does not read this context's code at runtime;
the *Event -> overlay state* leg runs through Event Ingestion, Automation and LayoutComposition.

## 8. Out of scope

Every `Api/` file (`CameraEndpoints*`, already split by #2896), the two migrations, tests' own
metrics, hit 3 (J2), the oddities of §6, and the pre-existing `ChangedCameraAddress` log line in
`Application/Log.cs` that writes `{Url}` (not touched here; `RtspUrl` refuses a `user:password@`
segment, so no credential reaches it today).

## 9. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052, ADR-0054), sentence-style names (ADR-0053),
`Ensure.That` guards (ADR-0105), `Option<T>` for absences in Application (ADR-0141), `var` wherever
it compiles, collection expressions, one parameter per line when wrapping, no leading underscores
(CLAUDE.md house rules). No Testcontainers (ADR-0103).
