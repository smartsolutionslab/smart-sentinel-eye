# Spec 345: The layout context under its limits

**Feature Branch**: `refactor/2887-layout-composition-metrics` (cut from `origin/develop` at `f30dced3`)

**Created**: 2026-10-13

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Two judgement calls are
marked explicitly (§6).

**Input**: Issue [#2887](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2887),
*Clear the ADR-0084 metric warnings in Layout Composition's Domain, Application and Infrastructure (29)*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Spec number.** 345. Checked on 2026-10-13 against `origin/develop` (highest 342), every remote
branch via `git ls-tree` (highest 342), every worktree (`sse-2896` holds unpushed 341, `sse-1545`
holds nothing above 338) and the open PRs (#2811, #2837; neither carries a spec). Highest visible
is 342, so this takes highest + 3 to leave room for siblings choosing at the same moment.
Re-check before the PR is opened.

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S138 <= 30 lines, S107 <= 4 parameters,
S1541 <= 10, advisory `warning`), ADR-0144 (autonomous lane; phase 4a colours; the lane may not
weaken a gate), ADR-0036 (smallest change), ADR-0141 (`Option<T>` for absences in Application),
ADR-0050 (`[LoggerMessage]` fields come from the template), ADR-0113 (check order of the stale
refusal is part of the contract), ADR-0051 (per-context DI module).

---

## 1. What was measured (premise verified, not assumed)

The issue's command does not run as written: `src/LayoutComposition` holds no project file
(`MSB1003`). The equivalent, which compiles Domain and Application transitively:

```sh
dotnet build src/LayoutComposition/Infrastructure/SmartSentinelEye.LayoutComposition.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep LayoutComposition
```

Result on `f30dced3`: 0 errors, **29 hits, exactly the 29 the issue lists, at the same lines.
None is stale.** (The build also prints 11 `ServiceDefaults` hits; a referenced project, out of
scope.)

## 2. Decision per hit

| # | File:line | Member | Rule | Decision |
|---|---|---|---|---|
| 1 | `Application/Commands/Handlers/BranchDraftRevisionCommandHandler.cs:14` | `HandleAsync` | S138 | **Clear** by extraction |
| 2 | `Application/Commands/Handlers/EditDraftRevisionCommandHandler.cs:16` | `HandleAsync` | S138 | **Clear** by extraction |
| 3 | `Application/Commands/Handlers/SwitchWallSceneCommandHandler.cs:25` | `HandleAsync` | S138 | **Clear** by extraction |
| 4 | same | `HandleAsync` | S1541 | **Clear** by the same extraction |
| 5 | `Application/EventHandlers/LayoutRevisionPublishedDomainEventHandler.cs:30` | `Handle` | S138 | **Clear** by extraction |
| 6 | `Application/EventHandlers/WallSceneSwitchRequestedV1Handler.cs:30` | primary constructor (5 DI dependencies) | S107 | **Leave**: `WallSceneSwitchRequestedV1HandlerTests.Handler(...)` constructs it with five arguments, so any change fails "tests unmodified"; folding dependencies together is a DI design change, not a mechanical one |
| 7 | same `:37` | `Handle` | S138 | **Clear** by extraction |
| 8 | same `:122` | `TryLoadWall` | S107 | **Clear** by parameter object (private, §3) |
| 9 | same `:149` | `LogMissingWall` | S107 | **Clear** by parameter object |
| 10 | same `:181` | `ApplySwitchAsync` | S107 | **Clear** by parameter object |
| 11-15 | `Application/Log.cs:79, 85, 91, 94, 97` | five `[LoggerMessage]` partials | S107 | **Leave**: parameters are the structured log template's fields (ADR-0050); clearing changes the log fields |
| 16 | `Application/Queries/Handlers/ListLayoutsQueryHandler.cs:12` | `HandleAsync` | S138 | **Clear** by extraction |
| 17 | `Domain/Layout/Layout.cs:129` | `Layout.CreateDraft` (public, 6) | S107 | **Leave**: public Domain factory called by `CreateLayoutDraftCommandHandler` and by five test files (`LayoutBuilder`, `LayoutTests`, `LayoutGuardTests`, `LayoutGridInvariantTests`, `LayoutNameUniquenessIntegrationTests`); changing it breaks "tests unmodified" |
| 18 | `Domain/Layout/Revision.cs:47` | `Revision.NewDraft` (internal, 5) | S107 | **Clear** by the existing `Creation` value object (§3) |
| 19 | `Domain/Layout/Revision.cs:76` | `Revision.Branch` (internal, 5) | S107 | **Clear** by the existing `Creation` value object |
| 20 | `Domain/Layout/Tile.cs:71` | private EF materialisation constructor (6) | S107 | **Leave**: EF Core binds constructor parameters by name to the mapped scalar columns; a parameter object cannot be bound |
| 21 | `Domain/Wall/Wall.cs:100` | `Wall.Create` (public, 5) | S107 | **Leave**: public Domain factory called by `CreateWallCommandHandler`, `WallBuilder` and `WallTests`, same reason as #17 |
| 22 | `Infrastructure/LayoutCompositionInfrastructureModule.cs:35` | `AddLayoutCompositionInfrastructure` | S138 | **Clear** by extraction (public signature unchanged) |
| 23 | `Infrastructure/Persistence/Configurations/LayoutConfiguration.cs:34` | `Configure` | S138 | **Clear** by extraction |
| 24 | `Infrastructure/Persistence/Configurations/WallConfiguration.cs:36` | `Configure` | S138 | **Clear** by extraction |
| 25-29 | `Infrastructure/Persistence/Migrations/*` (5) | generated `Up`/`Down` | S138 | **Leave**: the issue forbids touching migrations; generated and already applied |

**Cleared: 15** (9 S138, 1 S1541, 5 S107). **Left: 14** (5 `[LoggerMessage]`, 5 migrations,
2 public Domain factories, 1 EF constructor, 1 DI constructor). Expected after-count: **14**, every
one named in the PR body with its reason.

## 3. User story

### US1 (P1): Layout Composition's non-Api layers meet ADR-0084 wherever that is mechanical

As a maintainer, I want the 15 clearable hits gone, each by an extraction or a parameter object that
names one concept, **without** any change an HTTP caller, another context, a log reader or the
database could observe.

**Why P1 / independently shippable.** It is the whole issue: one context, no contract, no
migration, no runtime resource, no Api file.

#### Acceptance scenarios

Characterisation (all must hold **before** the refactor and pass **unmodified** after):

```gherkin
Scenario: Every existing LayoutComposition test passes unmodified (happy path)
  Given LayoutComposition.Domain.Tests (249), Application.Tests (150), Infrastructure.Tests (2)
  When the refactor commits land
  Then all pass and `git diff <last 4a commit>..HEAD -- tests/` is empty

Scenario: A branched revision records its own creation, never the chain's instance
  Given a layout created at T0 by A and published
  When B branches a draft at T1
  Then the new revision's Creation is (T1, B)
  And the first revision's Creation is not the same instance as the layout's Creation

Scenario: Refusal order on the edit path is unchanged (conflict)
  Given an edit that is both cross-fab and stale
  Then the answer is TileCameraOutsideFab, naming the outside cameras
  Given an edit that is stale and names a missing revision
  Then the answer is LayoutRevisionStale

Scenario: Refusal order on the branch path is unchanged (conflict)
  Given a chain with an open draft and a stale expected version
  Then the answer is LayoutRevisionStale, not NoPublishedRevisionToBranchFrom

Scenario: Refusal order and log outcome on the manual switch are unchanged (conflict)
  Given a stale version naming a scene outside the set
  Then the answer is WALL_STALE
  And a real switch logs SwitchedWallScene; an already-showing or empty Next logs WallSceneSwitchWasNoOp

Scenario: Rule-driven drops keep each identifier in its own slot (bad request)
  Given a WallSceneSwitchRequestedV1 whose wall, rule and causing event are three distinct Guids
  When it is dropped as unknown wall / wall in another fab / invalid identifiers / not Published
  Then the rendered log message is exactly today's text with each Guid in its own position

Scenario: Listing order and picker mapping are unchanged
  Then the published picker is ordered by Name, ordinal ignore-case, with revision, grid and PublishedAt mapped
  And the admin list is ordered by CreatedAt descending

Scenario: The EF model is unchanged
  Then LayoutCompositionDbContext's Model.ToDebugString(LongDefault) equals the baseline captured on f30dced3

Scenario: The DI composition is unchanged
  Then AddLayoutCompositionInfrastructure registers the same LayoutComposition-owned services, in the same order, with the same lifetimes
```

**Auth scenario: N/A.** No authorisation boundary moves: every change is inside private methods,
an `internal` Domain factory, or a public method whose signature is kept. Fab scoping on the edit
path (FR-014 cross-fab tiles) and on listings (FR-005) is pinned above, not changed.

#### Independent test procedure (end to end)

1. The four `dotnet test` runs of plan §5, green before and after.
2. The grep of §1 prints exactly the 14 "Leave" rows after the change.
3. CI's Integration.Tests (`LayoutComposition/*`, `Automation/RuleSwitchesAWallIntegrationTests`)
   green: they boot the real stack, so they cover the EF mapping and DI module against Postgres
   and RabbitMQ. Docker-only, so observed in CI, not locally.

## 4. Functional requirements

- **FR-001** The 15 "Clear" hits of §2 report nothing after the change. If an extraction leaves a
  hit, the PR says so with the measured value; it is never suppressed.
- **FR-002** No public or `protected` signature changes. `ICommandHandler`/`IQueryHandler`
  implementations, `AddLayoutCompositionInfrastructure`, `Layout.CreateDraft`, `Wall.Create`,
  everything in `Shared.Contracts` and every `Log.cs` partial keep their signatures.
- **FR-003** Check order, error values, log calls (method, arguments, order) and repository calls
  (including the `SaveAsync` on the manual already-showing path) are identical for every input.
- **FR-004** No new primitive on a Domain type (constitution §II). The Domain parameter object is
  the existing `Creation` value object.
- **FR-005** DI registrations keep their order and lifetimes; EF configuration produces the same
  model.
- **FR-006** No suppression, `#pragma`, threshold, `.editorconfig` or `SonarLint.xml` change; no
  guard edited; no file under any `Api/` or `Migrations/` directory touched.

## 5. Success criteria

- **SC-001** Before/after hit lists quoted in the PR body; after = the 14 "Leave" rows.
- **SC-002** Characterisation observed green on unrefactored code, green and unmodified after; both
  outputs quoted.
- **SC-003** `dotnet build -c Release` 0 errors; `dotnet format --verify-no-changes` clean on the
  three touched projects; coverage gates hold (Domain >= 90 %, Application >= 80 %).

## 6. Judgement calls (marked)

- **J1. A private parameter object in Application.** `WallSceneSwitchRequestedV1Handler`'s private
  helpers all carry the same `(Guid wall, Guid rule, Guid causingEventIdentifier)` triple, purely as
  the log-correlation fields every drop is logged with. They form one concept, so they become one
  private nested type. It carries `Guid`s because the values *are* the unvalidated wire values
  (the handler logs them precisely when they fail to parse); it lives in Application and is
  private, outside §II's Domain scope and outside `PrimitiveBoundaryTests`' walk of Domain
  assemblies. Three same-typed fields make positional transposition possible, which is why the
  slot-order pin in §3 is required before the change.
- **J2. Leaving public Domain factories.** `Layout.CreateDraft` and `Wall.Create` could take a
  parameter object, but every existing caller, tests included, would change. The issue's "tests
  unmodified" rule decides it: leave, and list.

No defect was found while reading the touched members, so this is not a refactor-plus-fix. One
oddity is recorded and **not** changed: `SwitchWallSceneCommandHandler` calls `walls.SaveAsync` on
the already-showing no-op path, where nothing has mutated. It is harmless today and pinned as-is
(FR-003); if anyone thinks it wrong, that is a separate issue.

## 7. Latency budget impact (constitution §IV)

**N/A.** None of the touched members is on the event-to-overlay path. The nearest is
`WallSceneSwitchRequestedV1Handler` (rule-driven scene switch); it is not one of §IV's legs, and
the refactor adds no I/O, no await and no allocation beyond one small struct per message.

## 8. Out of scope

Every `Api/` file (issue #2896 is editing them concurrently), migrations, tests' own metrics,
`ServiceDefaults` hits shown by the same build, and the 14 left hits.

## 9. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052), sentence-style names (ADR-0053), `Ensure.That`
guards (ADR-0105), `Option<T>` for an absent refusal (ADR-0141), `var` wherever it compiles,
collection expressions, fluent DI chains, one parameter per line when wrapping, no leading
underscores (CLAUDE.md house rules). No Testcontainers (ADR-0103).
