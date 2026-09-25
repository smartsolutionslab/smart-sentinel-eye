# Spec 254 — The copies 248 left behind

**Issue**: #2589 · **Branch**: `fix/2589-realm-probe-drift-followup` · **Phase**: 1 (Specify)
**Date**: 2026-09-25 · **Base**: `d451cc32` (5 behind `origin/develop` `0d349ce8`; rebase before PR)
**Context**: test code only — `tests/Integration.Tests/`. No `src/`, `apps/`, contract, AppHost,
csproj, CI-workflow, shard-filter or realm-import change.
**Engineer**: `test-writer` (4a) only; no 4b (spec 248 §6 reasoning holds unchanged) ·
**Reviewer**: `backend-reviewer`
**Lane**: autonomous (ADR-0144)
**Phase 4a colour**: **red, by counterfactual** (§5) — identical reasoning to spec 248 §2.
**ADRs**: ADR-0037, ADR-0144, ADR-0139 (§Testing), ADR-0103, ADR-0052/0053, ADR-0036, ADR-0084
(file-size advisory — why the new facts go in partial-class files)
**Constitution**: §IV — **N/A** (no leg touched). §II — N/A. §Testing — red first.
**New ADR needed**: **No.** Extends spec 248's guard by adding rows, exactly as 248 §1.1 anticipated.

**Why phases 1-3 were not skipped.** CLAUDE.md allows a skip only for trivial changes. This one
makes five design choices (§4) and edits two existing test files; they are written down here so
phase 4a does not improvise them.

---

## 1. The premise, re-checked against `d451cc32`

| # | Issue claim | Status |
|---|---|---|
| 1 | `AuthenticationDefaults.cs:37` realm default is an untracked copy | **Holds.** `string realm = "smart-sentinel-eye"`; all nine `AddBearerAuthentication()` call sites pass no argument, so the default **is** every API's JWT authority realm. |
| 2 | ~15 test-side literal secret copies | **Holds: 16 sites.** 14 `private const` pairs (`…ClientId` + `…Secret`) and 2 inline literal pairs — table §4.2. |
| 3 | `migration-runner` / `event-ingestion` secrets have test-side copies | **No** — beyond `AppHost.cs:41,52` and `AppHostParameterOverrideTests.cs:33,51,54`, none. |
| 4 | Every copy agrees with `src/AppHost/Realms/smart-sentinel-eye-realm.json` today | **Holds.** Hence red by counterfactual. |

### 1.1 In scope

- `AuthenticationDefaults.AddBearerAuthentication`'s realm default (issue item 1).
- The 16 test-side client-credential pairs (issue item 2).
- The five other `AppHost.cs` secret-parameter defaults (`:41,47,52,58,64`) — the production
  counterpart of the test-side copies, same class as #2466's site 2, recorded by spec 248 §1.1.

### 1.2 Out of scope, recorded for a follow-up

- **Test-side realm-name copies** (~25): inline `"/realms/smart-sentinel-eye/…"` URL literals
  (e.g. `PlantFloor.cs:92`, `FabGroupClaimIntegrationTests.cs:146,183`, `AspireFixture.cs:1366`,
  `AspireFixture.Auth.cs:129`, `NFR001_JwtValidationLatencyTests.cs:59`), `RunModeStackAddress.cs:36`,
  `StreamFabAttributionIntegrationTests.cs:301`. The right fix is substituting the already-guarded
  `RealmProbe.Realm` — a refactor, a different change shape, not named by the issue.
- **`src/` realm and client-id defaults**: `MosquittoOptions.cs:31,40,49`, `SimulatorOptions.cs:43,46`,
  `ScenarioSimulator/Program.cs:162-163`, `StreamFabAttributionOptions.cs:15,17`,
  `StreamDistributionInfrastructureModule.cs:190`, `ReverseIndexSeederOptions.cs:23,25`,
  `AppHost.cs:352` (`Keycloak__AdminClientId=migration-runner`). Not named by the issue.
- **Not realm-derived**: `PostgresUser`, `PostgresPassword`, `KeycloakPassword`, `RabbitMqPassword`.
- **Unit-test fakes** (exempt per the issue): `Identity.Infrastructure.Tests/KeycloakAdmin/*`,
  `ScenarioSimulator.Tests/*Client*Tests`/`ScenarioSeederResilienceTests`,
  `ServiceDefaults.Tests/ClientCredentialsTokenProviderTests`,
  `EventIngestion.Infrastructure.Tests/MosquittoConnectionFactoryTests` — all talk to fakes.
- **Consolidating the 14 scenario-simulator copies into one constant** — the better end state, but
  a refactor of 14 Docker-only test classes; the issue prescribes a guard per copy.

---

## 2. User story

### US-1 (P1) — A realm edit not mirrored into any of these copies fails the Docker-free build, naming the stale copy.

```gherkin
Scenario: happy — every copy agrees with the realm import
  Given the realm import as committed
  When the FixtureLogic tests run
  Then every new RealmImportMirrorTests case passes

Scenario: conflict — the import's scenario-simulator secret changes, no copy does
  Then all 14 scenario-simulator test-site rows and the ScenarioSimulatorClientSecret AppHost row fail,
    each naming its own copy, both values and the realm file path

Scenario: conflict — one test-side copy changes, the import does not
  Then exactly that site's row fails, naming its class and field

Scenario: conflict — AuthenticationDefaults' realm default changes
  Then the AuthenticationDefaults fact fails, naming the composed Authority and the realm's 'realm' field

Scenario: bad input — a guarded const is renamed/removed, or the import lacks the client or its secret
  Then the guard fails naming the type+field or the path+missing element — never a NullReferenceException, never a pass

Scenario: auth — N/A (compares values; no network call, no credential presented)
```

**Independent test procedure**: `dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj
-c Release --filter "Category=FixtureLogic"` — green, trx shows the new cases (§6 SC-1). Then §5's
counterfactuals, each reverted to an empty `git diff`. No Docker.

---

## 3. Latency budget

N/A — test code only.

---

## 4. Design — the five decisions

Expected values always come **from the realm file** via the existing private helpers
(`ReadRealmImport`, `DeclaredRealm`, `ClientsNamed`, `SecretForClient`); actual values from the copy.
No fact compares a copy with a copy (spec 248 §4).

### D1. Where: two new partial-class files, same class

`RealmImportMirrorTests.cs` gains the `partial` keyword and **nothing else** — its six facts stay
byte-identical. New files beside it:

- `tests/Integration.Tests/Identity/RealmImportMirrorTests.ProductionDefaults.cs` — §4.1 rows.
- `tests/Integration.Tests/Identity/RealmImportMirrorTests.IntegrationTestCopies.cs` — §4.2 rows.

Why partial rather than a sibling class: the helpers are private and reused unchanged (no extraction
refactor of a reviewed file); the class name and shard assignment (`ci-shards/shard-1.filter`) are
unchanged, so no shard edit. Why not one file: 270 lines today; ADR-0084's 300 LOC/file advisory.
Precedent: `AspireFixture` / `AspireFixture.Auth.cs`. No issue numbers in code comments (CLAUDE.md).

**Correction, found in implementation**: `Architecture.Tests/IntegrationTestSelectionTests.cs`'s
`Every_integration_test_class_declares_where_it_runs` fact scans **each source file** under
`tests/Integration.Tests` independently, not the merged partial class — it has no concept of a
partial spanning files. `AspireFixture.Auth.cs`, this section's cited precedent, never exercises
that path because it declares zero `[Fact]`/`[Theory]` methods, so the guard's population gate
excludes it. Both new files here *do* declare test methods, so — contrary to what this section
said before implementation — each carries its own `[Trait("Category","FixtureLogic")]`. `TraitAttribute`
allows repetition; the merged class simply carries the trait three times, which changes no
selection behaviour (44 cases still resolve under `Category=FixtureLogic`, confirmed by phase 6).

### D2. Rows, not a fact per site

Each copy is one `[Theory]` row — spec 248 §1.1: "the guard's shape extends to them by adding rows".
Each row is its own test case and its failure names its own site, which is what "a fact per copy"
buys. Rows are `TheoryData<...>` of serialisable values (`Type`, `string`); if the pinned xUnit cannot
serialise `Type`, pass the type's full name and resolve it from the test assembly.

### D3. Test-side sites are read by reflection on `private const` fields

`type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue()` —
precedent `Architecture.Tests/EndpointScopeDeclarationTests.cs:1849`,
`Integration.Tests/Fixtures/LogTailDeliversIntegrationTests.cs:240`. A missing/renamed field or a
non-string value throws `InvalidOperationException` naming type and field (bad-input scenario) —
never a null passed on to `ShouldBe`.

The two **inline** sites cannot be reflected, so each is lifted to a pair of `private const` fields
in a separate `refactor(test):` commit, **before** the guard commit:

- `FabGroupClaimIntegrationTests.cs:178-179` → `SimulatorClientId` / `SimulatorClientSecret`
  (the names the sibling sites use).
- `StreamFabAttributionIntegrationTests.cs:302-303` → `AttributionClientIdentifier` /
  `AttributionClientSecret`. `Realm = "smart-sentinel-eye"` at `:301` stays inline (§1.2).

Behaviour-preserving by construction: a `const string` use compiles to the same `ldstr` as the
literal it replaced, so no method body's IL changes. The lift touches only those four lines plus the
four declarations. Source-text scanning was rejected: a reformat would red a guard whose copy never
moved.

**Pairing** mirrors spec 248 sites 1b/1c: each site's own client-id const is what that test
presents, so theory A asserts it names exactly one client in the import, and theory B looks the
secret up under **that** client id (the `likelyCauseFact` argument names theory A).

### D4. `AuthenticationDefaults` is read through composition, not `ParameterInfo.DefaultValue`

Mirror `ServiceDefaults.Tests/BearerAudienceTests.cs:95-104`: `Host.CreateEmptyApplicationBuilder(null)`,
`Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid"`, `AddBearerAuthentication()`
with **no arguments** (exactly as all nine APIs call it), resolve
`IOptionsMonitor<JwtBearerOptions>.Get(JwtBearerDefaults.AuthenticationScheme).Authority`, assert it
equals `"https://keycloak.invalid/realms/" + DeclaredRealm(import)`. This is the value every API
validates tokens against, and it survives the default moving into a constant or being ignored — the
same reason spec 248 read site 2 by composition. `DefaultValue` reflection is the **fallback only**
if `JwtBearerOptions` is not compile-visible from `Integration.Tests` (it reaches `ServiceDefaults`
transitively — `OutboxBacklogIsVisibleTests.cs:4`); record it in the PR if used.

### D5. AppHost rows pair by a literal client id in the row

Rows: `(parameter name, realm clientId)`. Unlike Identity's site 2 (paired via
`KeycloakAdminOptions`' default), the five services' presented client ids live in further defaults
(§1.2) — pairing through them would widen scope. A stale row literal cannot pass silently:
`SecretForClient` throws when no such client exists. `DeclaredDefaults`' five entries (and
`MigrationRunnerClientSecretDefault`, `AppHostParameterOverrideTests.cs:33`) are **covered
transitively**, exactly as spec 248 §4 site 3: `A_parameter_with_no_argument_keeps_its_default`
already loops all ten parameters asserting composed == transcription; the new row asserts
composed == realm. Composition via `DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>([])`
and the existing `ValueAsync` helper, one composition per row.

### 4.1 Production-default rows (file `...ProductionDefaults.cs`)

| Row | Copy (actual) | Realm import (expected) |
|---|---|---|
| P1 | composed `MigrationRunnerClientSecret` (`AppHost.cs:41`) | `migration-runner`.secret (`:285`) |
| P2 | composed `ScenarioSimulatorClientSecret` (`:47`) | `scenario-simulator`.secret (`:319`) |
| P3 | composed `EventIngestionMqttClientSecret` (`:52`) | `event-ingestion`.secret (`:344`) |
| P4 | composed `StreamDistributionAttributionClientSecret` (`:58`) | `stream-distribution-attribution`.secret (`:301`) |
| P5 | composed `SystemVariablesSeederClientSecret` (`:64`) | `system-variables-seeder`.secret (`:360`) |
| J  | `AddBearerAuthentication()` composed `Authority` (`AuthenticationDefaults.cs:37`) | root `realm` (`:2`) — a `[Fact]` |

### 4.2 Integration-test copy rows (file `...IntegrationTestCopies.cs`)

Two theories over the same 16 rows: **A** the client-id const names exactly one client; **B** the
secret const equals that client's `secret`.

| Row | Type (namespace `SmartSentinelEye.Integration.Tests.*`) | ClientId field | Secret field | Client |
|---|---|---|---|---|
| T1 | `Fixtures.PlantFloor` | `SimulatorClientId` :31 | `SimulatorClientSecret` :32 | scenario-simulator |
| T2 | `Identity.TokenAudienceIntegrationTests` | `SimulatorClientId` :43 | `SimulatorClientSecret` :44 | scenario-simulator |
| T3 | `Identity.FabGroupClaimIntegrationTests` | `SimulatorClientId` (lifted) | `SimulatorClientSecret` (lifted) | scenario-simulator |
| T4 | `EventIngestion.DeadLetterFabScopingIntegrationTests` | `SimulatorClientId` :49 | `SimulatorClientSecret` :50 | scenario-simulator |
| T5 | `EventIngestion.DeadLetterReasonIntegrationTests` | `SimulatorClientId` :42 | `SimulatorClientSecret` :43 | scenario-simulator |
| T6 | `EventIngestion.IngestThroughputMeasurementTests` | `SimulatorClientId` :55 | `SimulatorClientSecret` :56 | scenario-simulator |
| T7 | `EventIngestion.MissingPayloadIsDeadLetteredIntegrationTests` | `SimulatorClientId` :37 | `SimulatorClientSecret` :38 | scenario-simulator |
| T8 | `EventIngestion.MqttResubscribeAfterBrokerOutageIntegrationTests` | `SimulatorClientId` :100 | `SimulatorClientSecret` :102 | scenario-simulator |
| T9 | `EventIngestion.OutageRecoveryIntegrationTests` | `SimulatorClientId` :38 | `SimulatorClientSecret` :39 | scenario-simulator |
| T10 | `EventIngestion.PoisonDeliveryEscapeIntegrationTests` | `SimulatorClientId` :36 | `SimulatorClientSecret` :37 | scenario-simulator |
| T11 | `EventIngestion.RestartLosesNothingIntegrationTests` | `SimulatorClientId` :49 | `SimulatorClientSecret` :50 | scenario-simulator |
| T12 | `EventIngestion.WebhookBearerValidationIntegrationTests` | `ScopeLessClientId` :89 | `ScopeLessClientSecret` :90 | scenario-simulator |
| T13 | `SystemVariables.ResolveOverlayTextTests` | `ServiceAccountClientId` :25 | `ServiceAccountSecret` :26 | scenario-simulator |
| T14 | `SystemVariables.VariableReadScopeIntegrationTests` | `ServiceAccountClientId` :47 | `ServiceAccountSecret` :48 | scenario-simulator |
| T15 | `SystemVariables.ReverseIndexSeedCredentialTests` | `SeederClientId` :49 | `SeederClientSecret` :50 | system-variables-seeder |
| T16 | `StreamDistribution.StreamFabAttributionIntegrationTests` | `AttributionClientIdentifier` (lifted) | `AttributionClientSecret` (lifted) | stream-distribution-attribution |

New test cases: 16 (A) + 16 (B) + 5 (P) + 1 (J) = **38**.

---

## 5. Counterfactuals — the red evidence (quoted verbatim in the PR)

Each: mutate, rebuild with `--no-incremental` (a restored file keeps its old timestamp), run
`--filter "Category=FixtureLogic"`, read the trx, revert, confirm the revert leaves no diff from the
mutation. Mutations use **distinct values per site** (`counterfactual-<Type|client|parameter>`) so
each red message proves which site its row read — this is what lets one batch stand in for 16
single-site runs: a row reading the wrong site would quote another site's value.

| # | Mutation | Must go red — exactly |
|---|---|---|
| C1 | the 16 test-side **secret** consts | 16 rows of B, each quoting its own site's value |
| C2 | the 16 test-side **client-id** consts | 16 rows of A, and 16 rows of B (lookup throws naming theory A) |
| C3 | the realm file's 5 non-identity client `secret`s | 16 rows of B + P1-P5 = 21 |
| C4 | the realm file's root `realm` | J + existing `RealmProbe_Realm_is_the_realm_the_import_defines` + existing `KeycloakAdminOptions_default_Realm_is_the_realm_the_import_defines` = 3 |
| C5 | `AppHost.cs:41,47,52,58,64` defaults | P1-P5 + existing `A_parameter_with_no_argument_keeps_its_default` + existing `An_empty_parameter_argument_keeps_its_default` (P1's parameter) = 7 |
| C6 | `AppHostParameterOverrideTests.cs:53` (`ScenarioSimulatorClientSecret`) alone | existing `A_parameter_with_no_argument_keeps_its_default` only — proves D5's transitive cover |
| C7 | `AuthenticationDefaults.cs:37` default | J only |

Any mutation that stays green, or reds beyond those listed, stops phase 4a and returns to
`test-writer`. C5 and C7 touch `src/` transiently; the final diff must not.

---

## 6. Success criteria

- SC-1: 38 new test cases in `RealmImportMirrorTests` (trx count), all green on the honest tree; the
  existing six unchanged and green.
- SC-2: C1-C7 each produce exactly §5's reds, quoted verbatim in the PR body.
- SC-3: `git diff --stat origin/develop` touches only: the two new partial files, the `partial`
  keyword in `RealmImportMirrorTests.cs`, the two lifted test files (D3), and `specs/254-*`. No
  shard-filter change.
- SC-4: Release build clean (analyzers, collection-expression rule; ADR-0084 advisory, each file
  under 300 LOC); `IntegrationTestSelectionTests` green.
