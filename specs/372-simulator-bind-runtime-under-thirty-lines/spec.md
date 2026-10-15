# Spec 372: The simulator's `BindRuntime` under thirty lines

**Feature Branch**: `refactor/2963-scenario-simulator-bindruntime` (cut from `origin/develop` at `8102af6f`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. One judgement call is
marked in §5 (where the endpoint group is cut, because one "service endpoints" step is itself over
the limit).

**Input**: Issue [#2963](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2963),
*ADR-0084 S138: split ScenarioSimulator's BindRuntime*. Refs #2897 (spec 342), #2933.

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**: phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green"). Not also a bug fix: nothing in the
current behaviour is recorded as wrong.

**Spec number.** 372. Checked 2026-10-15 against `origin/develop` (highest: 370), every local and
remote branch head (`git ls-tree … specs/`) and `git ls-remote --heads origin`: 371 is claimed by
the unmerged `refactor/2962-event-endpoints-writes-split`; nothing claims 372. Re-check before the
PR is opened.

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S138 <= 30 lines, advisory `warning`),
ADR-0144 (autonomous lane, characterisation colour), ADR-0111 (the simulator is dev-only),
ADR-0036 (smallest change; a refactor changes shape, not behaviour), ADR-0105 (guards — unchanged
here). No new ADR is needed: this applies ADR-0084, it decides nothing.

**Latency budget.** N/A. The Scenario Simulator is a dev-only host (ADR-0111), gated off in CI, E2E
and production; `BindRuntime` runs once, at options construction, off every §IV leg.

---

## 1. What was measured (premise verified, not assumed)

On `8102af6f` (= `origin/develop`, 2026-10-15):

```sh
dotnet build src/ScenarioSimulator -c Release --no-incremental -p:TreatWarningsAsErrors=false
```

0 errors. The **only** S138 hit in `src/ScenarioSimulator` is the one the issue names:

```
src\ScenarioSimulator\Program.cs(125,13): warning S138: This static local function has 42 lines, which is greater than the 30 lines authorized.
```

`WolverineDefaults.cs` is not in this project (it is #2953). The 15 S107 hits in the same build
(`Log.cs`, `LayoutCompositionClient.cs`, …) are out of scope (§7).

Spec 342 (#2897) measured this method at S138 = 39 and expected the S1541 extraction to clear it
"as a side effect". It did not: extracting `RequiredSetting` cut complexity, but each of the seven
calls is a five-line statement, so the method sits at 42. That is the gap this spec closes.

## 2. User story

### US1 (P1): `BindRuntime` reads as a sequence of named steps

As a maintainer of the dev-only scenario simulator, I want `BindRuntime` split into named steps
(service endpoints, identity) so that it is under ADR-0084's 30-line limit, **without** any change
an operator or the AppHost could observe: the same configuration keys, the same Aspire-key-then-
runtime-key fallback order, the same defaults, the same exception type and messages, and the same
order in which a missing setting is reported.

**Why P1 / only story.** It is the whole issue; there is nothing smaller that is independently
shippable.

**Independent test.** §4's characterisation suite, captured green before the change and re-run
unmodified after, plus the Release build showing zero S138 hits in `src/ScenarioSimulator`.

### Acceptance scenarios

The binding has no HTTP surface, so "auth" and "conflict" scenarios do not apply; the
bad-request analogue is a missing setting.

```gherkin
Scenario: Aspire-injected keys win (happy)
  Given every services:<name>:<endpoint>:0 key and every ScenarioSimulator:Runtime:* fallback key is set
  When BindRuntime runs
  Then every URL/host is the Aspire value and KeycloakUrl is services:keycloak:http:0

Scenario: Fallback keys are used when Aspire keys are absent
  Given only the ScenarioSimulator:Runtime:* keys and services:keycloak:https:0
  Then every URL/host is the fallback value and KeycloakUrl is the https value

Scenario: Keycloak precedence
  Then ConnectionStrings:keycloak beats services:keycloak:http:0 beats services:keycloak:https:0

Scenario: Defaults
  Given Realm, ClientId and RtspHost are unset
  Then they are "smart-sentinel-eye", "scenario-simulator" and "camera-sim:8554"

Scenario: A missing setting names itself (bad request)
  Given exactly one required setting is missing (both its keys)
  Then InvalidOperationException is thrown with that setting's message, verbatim

Scenario: The first missing setting in binding order is reported
  Given two adjacent required settings are both missing
  Then the message names the earlier one in the order
       camera-catalog, camera-sim, overlay-designer, automation, layout-composition,
       system-variables, mosquitto, Keycloak, client secret

Scenario: An empty value is a value
  Given a key is present with an empty string
  Then the empty string is bound and the fallback is not consulted
```

## 3. Functional requirements

- **FR-001** `src/ScenarioSimulator/Program.cs` reports **no** S138 hit (Release build, §1 command).
- **FR-002** `BindRuntime` keeps its name and its `(IConfiguration, SimulatorOptions)` signature, and
  stays a `static` local function of the top-level program (the existing suite locates it by
  reflection on `g__BindRuntime|` and invokes it with two arguments).
- **FR-003** No configuration key, default value, exception type or exception message changes, and
  the nine required settings are resolved in the order listed in §2.
- **FR-004** `RequiredSetting`, `ResolveKeycloakUrl` and `Resolve` are reused unchanged; no new type,
  no new file, no visibility change (`Program` stays top-level; nothing becomes `internal`/`public`).
- **FR-005** No new S107/S1541/S134 hit and no new S138 hit in the extracted local functions.
- **FR-006** No test assertion is edited after the production change. Characterisation facts may be
  **added** before it (§4).

## 4. Covering tests (phase 4a: characterisation)

**Reachability.** `BindRuntime` is a static local function of top-level statements, so it compiles
to an `internal static` method `<<Main>$>g__BindRuntime|…` on the generated `Program` class. It is
already reachable without a production change: `tests/ScenarioSimulator.Tests/SimulatorRuntimeBindingTests.cs`
(spec 342) finds it through `typeof(SimulatorOptions).Assembly.EntryPoint.DeclaringType`, asserts
exactly one match, and invokes it with `BindingFlags.DoNotWrapExceptions`. No
`WebApplicationFactory`/host boot is needed and **no production visibility is widened**.

The extracted helpers must therefore not be named so that their generated name contains
`g__BindRuntime|` (e.g. `BindRuntimeEndpoints` is safe — the `|` follows the full name — but is
discouraged for readability; see plan §3).

### 4.1 Already pinned (20 cases, green on `8102af6f`)

| Behaviour | Fact |
|---|---|
| Local function found exactly once, two parameters | `The_bind_runtime_local_function_is_found_exactly_once` |
| Aspire keys win (7 endpoints + Keycloak + secret) | `Aspire_injected_keys_win_over_the_fallback_keys` |
| Realm/ClientId/RtspHost defaults | `Realm_client_id_and_rtsp_host_keep_their_defaults_when_unset` |
| Realm/ClientId/RtspHost overrides | `Realm_client_id_and_rtsp_host_are_read_from_the_runtime_section_when_set` |
| Fallback keys used | `Fallback_keys_are_used_when_the_aspire_keys_are_absent` |
| Keycloak precedence (3 rows) | `Keycloak_url_prefers_the_connection_string_then_http_then_https` |
| Empty Aspire value is a value (camera-catalog only) | `An_empty_aspire_value_is_a_value_and_does_not_fall_through_to_the_fallback` |
| Empty config fails on camera-catalog | `An_empty_configuration_fails_on_the_camera_catalog_first` |
| Each of the 7 endpoint messages, verbatim (7 rows) | `A_missing_endpoint_setting_names_itself_in_the_failure` |
| Keycloak message, verbatim | `A_missing_keycloak_url_names_itself_in_the_failure` |
| **Client-secret throw and message**, verbatim | `A_missing_client_secret_names_itself_in_the_failure` |
| system-variables before mosquitto before secret | `With_several_settings_missing_the_first_in_binding_order_is_reported` |

So the issue's two named concerns — the missing-setting messages and the client-secret throw — **are
already pinned**.

### 4.2 Unpinned, and exactly where this refactor cuts (test-writer must WRITE these first)

The split introduces seams *between* groups of settings. Reporting order is pinned only for
system-variables -> mosquitto -> secret; a step boundary placed anywhere else could reorder
reporting silently. Also, empty-is-a-value is pinned for one endpoint only, and the identity group
(where an engineer is most tempted to "tidy" `??` into `IsNullOrEmpty`) has none.

- **C1 — total reporting order.** Theory, 8 rows, one per adjacent pair of the nine required
  settings in §2's order: remove both keys of setting *n* and of setting *n+1*; assert the message is
  setting *n*'s. Rows: catalog/camera-sim, camera-sim/overlay, overlay/automation,
  automation/layout, layout/system-variables, system-variables/mosquitto, mosquitto/Keycloak
  (remove all three Keycloak keys), Keycloak/client-secret.
- **C2 — empty is a value in the identity group.** Theory, 2 rows:
  `ScenarioSimulator:Runtime:ClientSecret = ""` binds `ClientSecret == ""` (no throw);
  `ConnectionStrings:keycloak = ""` binds `KeycloakUrl == ""` (beats `services:keycloak:http:0`).

Both are added as new facts in the existing `SimulatorRuntimeBindingTests` class (reusing its
`Bind`/`Missing`/`Complete` helpers); no existing fact body changes. They must be **observed green**
on unrefactored code — a red one is a test defect, fixed in the test, never in production.

## 5. Judgement call (explicit)

The issue says "service endpoints / identity". One `BindServiceEndpoints` holding all seven
`RequiredSetting` statements is 7 x 5 = 35 lines — **still over 30**. The endpoint group is
therefore cut in two, contiguously, so the order in FR-003 cannot change: the camera endpoints
(camera-catalog, camera-sim — the provisioning path) and the seed-client endpoints
(overlay-designer, automation, layout-composition, system-variables — the M2 seeding clients,
ADR-0111). Mosquitto stays in `BindRuntime` itself, between the two endpoint steps and the identity
step, because moving it into either group would reorder it. Plan §3 has the shape. Any other
contiguous cut that clears S138 is acceptable; C1 is what makes that safe.

## 6. Success criteria

- **SC-001** §1's command shows 0 S138 hits under `src\ScenarioSimulator\` (was 1, at 42 lines).
- **SC-002** `SimulatorRuntimeBindingTests` (20 existing + 10 new = 30 cases) green before and after
  the production change, unmodified between the two runs.
- **SC-003** Full `tests/ScenarioSimulator.Tests` green (242 on `8102af6f`, + 10).
- **SC-004** `git diff` of the refactor commit touches `src/ScenarioSimulator/Program.cs` only.

## 7. Out of scope

- The 15 S107 hits in `src/ScenarioSimulator` (`Log.cs`, `LayoutCompositionClient.cs`,
  `CameraRegisteredSimHandler.cs`, `MqttPublisher.cs`, `AssetCorrelationTable.cs`,
  `ScenarioSeeder.cs`) — not this issue.
- `WolverineDefaults.cs` S138 — #2953.
- Moving `BindRuntime` out of `Program.cs` into a testable class — would remove the reflection seam,
  but it is a larger change than the issue asks for and would require editing the existing suite.
