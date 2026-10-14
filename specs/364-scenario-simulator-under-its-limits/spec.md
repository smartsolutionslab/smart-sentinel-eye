# Spec 364: The scenario simulator under its method-length limit

**Feature Branch**: `refactor/2933-scenario-s138` (cut from `origin/develop` at `0296275e`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked explicitly (§6).

**Input**: Issue [#2933](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2933),
*ADR-0084 S138: split the long methods in ScenarioSimulator*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green"). The covering tests are captured
passing before the first production edit and must pass **unmodified** after it.

**Spec number.** 364. Checked on 2026-10-15: `origin/develop` (`0296275e`) highest 361; every
remote head via `git ls-tree` highest 362 (`origin/refactor/2941-option-internal-params`); 363 is
reserved for the parallel #2929 architect. Re-check before the PR is opened.

**ADRs.** ADR-0084 (code metrics: S138 <= 30 lines/method, S134 nesting <= 3, S107 <= 4
parameters; advisory `warning`), ADR-0111 (the scenario simulator: a **dev-only** worker), ADR-0144
(autonomous lane; phase 4a colours; the lane may not weaken a gate), ADR-0036 (smallest change),
ADR-0050 (`[LoggerMessage]` templates unchanged), ADR-0105 (`Ensure.That` guards stay where they
are), ADR-0141 (`Option<T>` for absences - advisory; no signature churn here).

---

## 1. What was measured (premise verified, not assumed)

```sh
dotnet build src/ScenarioSimulator/SmartSentinelEye.ScenarioSimulator.csproj -c Release --no-incremental
```

On `0296275e`, filtered to `src\ScenarioSimulator\` and to S138/S134 (S107 shown in §8 only):

| # | File (line) | Rule | Member | Size |
|---|---|---|---|---|
| 1 | `CameraCatalog/CameraCatalogClient.cs(73)` | S138 | `ReadBackAsync` | 36 |
| 2 | `CameraSim/CameraSimPathClient.cs(25)` | S138 | `GetAsync` | 34 |
| 3 | `Cues/ClipManifestLoader.cs(25)` | S138 | `Load` | 35 |
| 4 | `Cues/ClipManifestValidation.cs(26)` | S138 | `Validate` | 34 |
| 5 | `Seeding/AutomationRulesClient.cs(24)` | S138 | `EnsureRuleAsync` | 36 |
| 6 | `Seeding/ReactionRuleSeed.cs(31)` | S138 | `From` | 32 |
| 7 | `Seeding/ScenarioSeeder.cs(28)` | S138 | `ExecuteAsync` | 55 |
| 8 | `Seeding/ScenarioSeeder.cs(96)` | **S134** | `ExecuteAsync`: `if (camera.HasValue)` inside foreach/foreach/try | depth 4 |
| 9 | `Seeding/ScenarioSeeder.cs(142)` | S138 | `SeedOverlayAndRuleAsync` | 38 |
| 10 | `Seeding/ScenarioSeeder.cs(266)` | S138 | `SeedReactionsAsync` | 39 |
| 11 | `Program.cs(125)` | S138 | static local function `BindRuntime` | 42 |

Rows 1-7, 9, 10 are exactly the issue's nine, with the issue's sizes: the premise holds. Row 8 is the
one S134 in this project; it sits inside row 7 and the row-7 extraction clears it (§6 J1). Row 11 is
**not** in the issue (§6 J2).

## 2. What the simulator is

ADR-0111: "ScenarioSimulator — a new dev-only worker", gated `isRunMode && !isE2ETests &&
isScenarioSimulatorEnabled`, off under E2ETests/CI/prod. CI boots the stack with
`ScenarioSimulator=false` (`ci.yml`). It seeds demo plants (cameras, overlays, rules, variables,
walls) over the contexts' public REST APIs and publishes simulated MQTT samples. It is **not**
production code and owns no bounded context, no `Shared.Contracts` type and no database.

Consequence: the refactor is allowed (ADR-0084 applies to every hand-written file) but kept
**minimal** - extract private helpers inside the same type, nothing more.

## 3. User story

### US1 (P1): ScenarioSimulator's listed methods meet ADR-0084 without any observable change

As a maintainer, the nine listed methods and the one S134 hit fall under the limits, **without** any
change a running stack, a log reader or an HTTP peer could observe: the same requests in the same
order, the same log events with the same fields, the same refusals, the same failure accounting.

**Why P1 / independently shippable.** It is the whole issue: one dev-only project, no contract, no
route, no migration.

#### Acceptance scenarios

```gherkin
Scenario: The listed warnings are gone (happy path)
  Given develop at 0296275e reports rows 1-10 of the §1 table
  When the refactor is applied
  Then the Release build of ScenarioSimulator reports none of rows 1-10
  And it reports no S138, S134 or S107 warning that was not already reported on 0296275e

Scenario: Behaviour is preserved (conflict: the refactor must not move behaviour)
  Given the characterisation tests of plan.md §4 observed green on the unrefactored code
  When each refactor commit lands
  Then every test in tests/ScenarioSimulator.Tests passes with no test file edited after Phase 2

Scenario: A refusal path still refuses, it does not throw (bad request)
  Given a reaction whose trigger Source, Kind or Predicate is blank
  Or a sidecar that is JSON "null"
  Or a camera-sim body that does not parse
  When the refactored code handles it
  Then it returns the same refusal / None / violation, with the same message, as before

Scenario: The simulator's bearer use is unchanged (auth)
  Given AutomationRulesClient and CameraCatalogClient send a bearer token from KeycloakTokenProvider
  When a rule is published or the catalog is paged
  Then each request still carries "Authorization: Bearer <token>" and the publish still carries If-Match
```

**Auth scenario.** No authorisation boundary moves: no Api file, no scope, no `RequireScope`, no
Keycloak client or realm change. The simulator remains a client of the existing scopes.

#### Independent test procedure (end to end)

1. `dotnet build src/ScenarioSimulator/SmartSentinelEye.ScenarioSimulator.csproj -c Release --no-incremental`,
   filter as in §1: rows 1-10 absent; row 11 and the S107 set of §8 unchanged.
2. `dotnet test tests/ScenarioSimulator.Tests -c Release`: all green, test files byte-identical to
   the end of Phase 2 (`git diff <phase-2-tip> -- tests/` empty).
3. Optional, dev only: boot the AppHost with the simulator enabled and observe "Scenario '...'
   seeded." per active scenario and no "Seeding finished INCOMPLETE" on a healthy stack. Not
   required by the gate (the simulator is off in CI), recorded if done.

## 4. Functional requirements

- **FR-001** Each listed method is split by extracting **private** helpers in the same type. No new
  type, file, interface or abstraction.
- **FR-002** No public or internal signature changes (`RegisterCameraAsync`, `GetAsync`, `Load`,
  `LoadManifestsByClip`, `Validate`, `EnsureRuleAsync`, `ReactionRuleSeed.From`, the seeder's
  constructor). `SeedReactionsAsync`'s six parameters stay (S107 out of scope).
- **FR-003** Every new helper has **at most four parameters**, nests at most three levels and is at
  most 30 lines, so it raises no new S107/S134/S138.
- **FR-004** Order of effects is preserved: HTTP requests, log events, `failed` counting, and the
  point at which an exception escapes or is caught (`when (ex is not OperationCanceledException)`
  filters move with their `try`).
- **FR-005** Comments that explain a moved block move with it; none is rewritten or deleted.
- **FR-006** `Program.cs` and every file not named in plan.md §3 are untouched.

## 5. Success criteria

- **SC-001** Rows 1-10 of §1 absent from the Release build; total ScenarioSimulator warning set
  otherwise identical (diffed, not eyeballed).
- **SC-002** `tests/ScenarioSimulator.Tests` green; the Phase-2 characterisation tests were observed
  green **before** the first production commit, and their output is quoted in the PR.
- **SC-003** No test file changes after Phase 2.

## 6. Judgement calls (marked)

- **J1 - S134 is in scope.** It is inside `ExecuteAsync` (row 7) and is cleared by the same
  extraction; leaving it would mean deliberately keeping the nesting the split removes.
- **J2 - `Program.cs` `BindRuntime` (row 11) is out of scope.** The issue's list omits it (Sonar
  phrases it "static local function", which the issue's "method" list did not pick up). It is a
  composition-root configuration binder covered by `SimulatorRuntimeBindingTests`; folding it in
  would widen the diff beyond the issue. Recorded here so it is not lost; a follow-up issue is the
  orchestrator's call.
- **J3 - No bug fix folded in.** Oddities pinned as-is: `ReactionRuleSeed.From` validates the name
  before the trigger fields, so a bad name masks a blank Source; `SeedOverlayAndRuleAsync` returns
  early for an asset with an overlay but no tile, losing its HighlightOverlay reactions (by design,
  S2); a scenario whose manifest load fails is counted once and logs no "seeded" line.

## 7. Latency budget impact (constitution §IV)

**N/A.** The simulator is dev-only (ADR-0111) and nothing touched runs on the event-to-overlay path;
the seeding pass runs once at startup. `CameraSimPathClient.GetAsync` feeds the cue scheduler's
anchor (spec 289) in dev only, and its request count and order are unchanged.

## 8. Out of scope

- S107 in ScenarioSimulator (15 hits on `0296275e`, incl. `SeedReactionsAsync`, `Log.cs`, `LayoutCompositionClient`):
  the issue excludes S107.
- `Program.cs` `BindRuntime` (J2). Every other project's warnings.

## 9. Locked tech choices

.NET 10, SonarAnalyzer metric rules at `warning` (ADR-0084), xUnit + Shouldly + hand-written fakes
(ADR-0052), sentence-style test names (ADR-0053). No new package.
