# Spec 407: the simulator's seeding helpers under five parameters

**Feature Branch**: `refactor/3027-simulator-seeding-s107` (cut from `origin/develop` at `c8c3773c`)

**Created**: 2026-10-18

**Status**: Specified, planned and tasked (phases 1-3). No `[NEEDS CLARIFICATION]` remains.

**Input**: Issue [#3027](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3027) (no
comments; body is the scope). Templates: spec 383 (#2973, a nested `private readonly record struct`
groups a private helper's parameters; the locals keep the old names so the body is textually
unchanged), spec 380 (#2971, EventIngestion helpers under five parameters), spec 364 (simulator
under its S138/S134 limits; it left S107 out of scope and named `SeedReactionsAsync`'s covering
tests), spec 372 (simulator `BindRuntime` split).

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation,
observed green**. Engineer: **backend-engineer**. **Not security-sensitive**: the ScenarioSimulator
is dev tooling (`src/AppHost/AppHost.cs:577`), and the change reshapes four private helpers; no
authentication, authorisation, idempotency, retry or secret handling changes. Phase 6 runs
`/code-review` only.

**Spec number.** 407. 402-405 are on `origin/develop`; 406 and 408 are claimed by parallel
architects. Re-check `specs/` on `origin/develop` and open PR heads before the PR is opened.

**ADRs.** **ADR-0084** (S107, at most 4 parameters; advisory `warning`, carved out of
`TreatWarningsAsErrors`), ADR-0144 (lane; characterisation colour; no gate weakened), ADR-0036
(smallest change), ADR-0087 (each commit builds on its own), ADR-0091 (no shortcuts in names),
ADR-0113 (the `If-Match` steps the layout client sends, which must not move), ADR-0111 (simulator
seeding).

**No new ADR.** ADR-0084 states the limit; this spec changes the shape of four private helpers.

## 1. Problem: the re-measured hit list

Measured 2026-10-18 on `c8c3773c`: `dotnet build src/ScenarioSimulator/SmartSentinelEye.ScenarioSimulator.csproj
-c Release --no-incremental -flp:logfile=…/s107-3027.log;verbosity=normal` (the ScenarioSimulator
project closure only; 0 errors), unique `file(line,col): warning S[0-9]+`, migrations excluded.
**8 S107 hits under `src/ScenarioSimulator/`**, and **no other Sonar rule fires there** (no S138,
S104, S1541, S134). `Log.cs` contributes none (the accepted `[LoggerMessage]` `.editorconfig`
section); the simulator has no route handlers.

S107 counts the `CancellationToken` (proven in-file: the four-parameter `PublishAsync` overload at
`LayoutCompositionClient.cs:173`, `Guid, int, string, CancellationToken`, is not flagged; its
five-parameter sibling at :177 is).

| # | Hit | Member | Kind | n | Scope |
|---|---|---|---|---|---|
| H1 | `Seeding/LayoutCompositionClient.cs(74,36)` | `RecoverAsync` | private helper; one caller (`EnsureWallAsync`, :63) | 6 | **in** |
| H2 | `Seeding/LayoutCompositionClient.cs(138,35)` | `RetileAsync` | private helper; one caller (`RecoverAsync`, :123) | 7 | **in** |
| H3 | `Seeding/LayoutCompositionClient.cs(177,36)` | `PublishAsync` (5-param overload) | private helper; two callers (the 4-param overload :175, `RetileAsync` :168) | 5 | **in** |
| H4 | `Seeding/ScenarioSeeder.cs(313,42)` | `SeedReactionsAsync` | private helper; two callers (`SeedOverlayAndRuleAsync` :185, :204) | 6 | **in** |
| H5 | `Seeding/LayoutCompositionClient.cs(38,38)` | `EnsureWallAsync` | **public**; called by `WallSeeder`, pinned by `LayoutCompositionClientTests.cs:295` | 5 | out |
| H6 | `Seeding/AssetCorrelationTable.cs(25,30)` | `RecordOverlay` | **public**; 8 test call sites | 5 | out |
| H7 | `EventHandlers/CameraRegisteredSimHandler.cs(26,47)` | constructor | DI constructor | 5 | out |
| H8 | `Mqtt/MqttPublisher.cs(96,27)` | constructor | DI constructor | 5 | out |

The issue's four in-scope hits (H1-H4) and its out-of-scope list match the measurement exactly;
line numbers agree with the issue's `86d32918` figures. **Premise holds.**

No helper is reached by reflection: the only `BindingFlags` use in `tests/ScenarioSimulator.Tests`
is `SimulatorRuntimeBindingTests` against `Program`'s static methods.
`InternalsVisibleTo` exposes internal types to the test project, but all four helpers are
`private`, so no test calls them directly. Every path to them is through `EnsureWallAsync` or
`ScenarioSeeder.ExecuteAsync`.

## 2. User story

### US1 (P1): the four seeding helpers take at most four parameters and seed exactly as before

A maintainer reading the simulator's S107 list sees only public signatures and DI constructors
(H5-H8). The wall is still created, recovered from Draft, re-tiled and published with the same
requests, the same headers, the same bodies, in the same order, with the same log lines. Every
asset's reactions still seed, skip and refuse exactly as before.

**Why P1 / independent.** It is the whole issue; it ships alone and is observed through the
existing and new characterisation tests plus the before/after warning diff.

#### Acceptance scenarios

```gherkin
Feature: seeding helpers under ADR-0084's parameter limit

  Background:
    Given develop at c8c3773c with 8 S107 hits under src/ScenarioSimulator

  Scenario: the four helper hits are gone and nothing else moved (happy path)
    When the Release --no-incremental build of the ScenarioSimulator closure runs after the change
    Then the S107 hits under src/ScenarioSimulator are exactly H5-H8
    And no S138, S104, S1541, S134 or other new warning appears in src/ScenarioSimulator
    And the Sonar list outside src/ScenarioSimulator is unchanged

  Scenario: a wall stranded in Draft is published as before
    Given the create endpoint answers 409 and the read-back shows revision 1 in Draft at version 4
    When EnsureWallAsync runs
    Then the calls are POST /layouts, GET /layouts, POST /layouts/{id}/revisions/1/publish
    And the publish carries If-Match "4" and "Bearer stub-token"
    And "Wall layout 'rolling-mill' ({id}) was left in Draft; published it." is logged once

  Scenario: a non-square wall whose tiles changed is re-tiled with rows and columns in place
    Given a desired 2-row, 3-column wall and a published revision with other tiles
    When EnsureWallAsync runs
    Then the draft, edit and publish steps run in order, each If-Match from the read before it
    And the PATCH body's grid is rows 2, cols 3, with every tile's camera, overlay, row and col in place

  Scenario: a grid that differs only by orientation is not mistaken for a match (conflict)
    Given a desired 2x3 wall and a published 3x2 revision with identical tiles
    When EnsureWallAsync runs
    Then the wall is re-tiled
    But given a published 2x3 revision with identical tiles it is left alone and "already exists" is logged

  Scenario: an unexpected or blind read-back is left alone (bad request from the read model)
    Given the read-back has no revisions, or only an Archived one
    When EnsureWallAsync runs
    Then no publish, draft or edit request is sent
    And the warning (no revisions) or the information line (archived) is logged as before

  Scenario: every layout call is authenticated with the minted token (auth)
    When any recovery or re-tiling path runs
    Then every request carries "Authorization: Bearer stub-token"

  Scenario: reactions seed as before on both call sites
    Given the existing seeder rig tests
    When the seeder runs after the change
    Then every ScenarioSeeder* and ScenarioStoryCheckTests fact passes unmodified
```

#### Independent end-to-end test procedure

1. On unchanged code, add the characterisation class (plan §4) and observe it, and every existing
   covering test, green. Run the counterfactuals; each must go red. Commit `C0`.
2. Apply the change (plan §2). Re-run the same commands unmodified; `git diff --stat C0 -- tests/`
   is empty.
3. Re-run the §1 build; diff the sorted-unique warning list before/after: exactly H1-H4 removed.

The simulator's live behaviour (`aspire run`, wall rendered on the kiosk) is **not** required for
Phase 5: the HTTP sequence the kiosk depends on is pinned by the stub-handler tests at the wire
level, which is the evidence that can tell a reshaped helper from an unchanged one. Verification
cites the test output and the warning diff.

## 3. Requirements

- **FR-001** H1-H4 take at most four parameters (S107 counted, `CancellationToken` included).
- **FR-002** The groupings are nested `private readonly record struct`s in the file that uses them
  (plan §2). No new file under `src/`, no new public or internal type.
- **FR-003** Each reshaped helper opens with one deconstruction into locals carrying the **old
  parameter names**, so its remaining body is textually unchanged.
- **FR-004** No change to any HTTP method, path, header, body, ordering, retry, exception or log
  call. No change to `EnsureWallAsync`'s or `RecordOverlay`'s signature (H5, H6) or to H7/H8.
- **FR-005** No new S107, S138, S104, S1541 or S134 anywhere; no new warning of any rule in the
  ScenarioSimulator closure.
- **FR-006** No suppression, threshold, `.editorconfig`, `SonarLint.xml`, `Directory.Build.props`
  or csproj change; no ADR, constitution or CLAUDE.md edit.
- **FR-007** Every covering test passes byte-unmodified after the change. An assertion that would
  need editing is a **block** (`agent:blocked`), not an edit (ADR-0144).

**Constitution §II** does not apply: it binds domain models' state; these are private helpers in
dev tooling. **Latency budget (§IV): N/A** — the simulator seeds at boot and is off the
event-to-overlay path.

## 4. Characterisation, the safety net

### 4.1 Existing coverage (real classes over stub handlers)

`LayoutCompositionClientTests` (10 facts) drives the **real** `LayoutCompositionClient` and the
real `KeycloakTokenProvider` over a recording `HttpMessageHandler` stub, via public
`EnsureWallAsync`. It pins: call order on the stranded-draft, fresh, matching and re-tiling paths;
`If-Match` on the stranded publish (`"4"`), the fresh publish (`"0"`) and all three re-tiling steps
(`"7"`, `"8"`, `"9"`); the draft-revision number in the edit and publish paths (`/revisions/2`);
no publish when revisions are missing or the wall is published; throws on an unreadable conflict
and a failed publish.

The seeder facts (`ScenarioSeeder*Tests`, 30 facts over `SeederRig`/`RecordingAutomationHandler`,
plus `ScenarioStoryCheckTests`, 9) drive the **real** `ScenarioSeeder` and its real clients over
recording stub servers, with a `RecordingLogger<ScenarioSeeder>`. For `SeedReactionsAsync` they
pin each of its six inputs: the scenario prefix in every rule name (`rolling-mill-…`), the overlay
identifier on a HighlightOverlay rule (`ScenarioSeederReactionTests:36`), the `overlay: null` call
site's refusal text (`ScenarioSeederAssetCharacterisationTests:18`), the seeded-variable filter
(`ScenarioSeederVariableTests:97,148`), the manifest pre-filter (`ScenarioSeederReactionTests:122`,
`ScenarioSeederAssetCharacterisationTests:147,173,198`), and per-reaction refusal
(`ScenarioSeederRuleRefusalTests`). **No new seeder test is needed**; the grouping's three fields
are of three distinct types (`string`, `AssetDefinition`, `Guid?`), so a swap at either
construction site is a compile error. Counterfactuals (plan §4) prove the coverage before `C0`.

### 4.2 Not pinned today (the test-writer writes these first)

All on the layout client, because the existing stub records neither bodies nor `Authorization`
and every existing fact uses a **2x2** grid with a `NullLogger`:

- **G1 rows/cols transposition.** `rows` and `cols` are adjacent `int`s in all three of H1, H2 and
  the new construction site; with `rows == cols` a swap is invisible. Neither the PATCH body's grid
  nor `Matches(rows, cols, tiles)`'s argument order is pinned.
- **G2 the PATCH body.** Grid and tiles of the edit request are never read.
- **G3 `Authorization`.** The bearer token threads through all four layout helpers and is never
  asserted.
- **G4 the branch log lines.** `WallDraftPublished`, `WallRetiled` (with the revision),
  `WallAlreadyExists`, `WallRevisionsMissing` are never observed.
- **G5 the archived-chain branch** (no Published revision, no Draft first revision) is not
  exercised.

## 5. Decisions

- **D-1 New test class, not edits to `LayoutCompositionClientTests`.** G1-G3 need a stub that
  captures the body and `Authorization`; the existing file's `Call` record and `StubHandler` are
  helpers every existing fact uses. Changing them edits existing test code, which the
  characterisation rule forbids. A new class
  `tests/ScenarioSimulator.Tests/LayoutCompositionClientCharacterisationTests.cs` owns its own
  recording handler and a `RecordingLogger<LayoutCompositionClient>` (existing fake). The
  ScenarioSimulator test project is not sharded (`ci-shards/` belong to `Integration.Tests`
  only), so no shard-filter entry is owed.
- **D-2 `PublishAsync` groups `(layout, revision)`, not `(layout, expectedVersion, revision)`.**
  Separating `expectedVersion` from `revision` into different types makes their transposition, the
  likeliest mistake on this method (two adjacent `int`s), a compile error rather than a test
  failure. The three-field alternative keeps them adjacent at both construction sites.
- **D-3 `EnsureWallAsync`'s body changes by one line** (its `RecoverAsync` call constructs the
  grouping); its signature does not. H5 stays a hit.
- **D-4 No `Ensure.That` added** to the reshaped helpers: private, not a trust boundary
  (ADR-0036/ADR-0105).

## 6. Out of scope

H5-H8 (per the issue). Any change to HTTP calls, headers, retry, ordering or logging. The
non-flagged four-parameter `PublishAsync(Guid, int, string, CancellationToken)` overload keeps
its signature (its body changes by the one delegating call). `OverlayDesignerClient` and
`AutomationRulesClient` (similar shape, not flagged).

## Latency budget

N/A: dev-only seeding at boot, off the event-to-overlay path (constitution §IV).
