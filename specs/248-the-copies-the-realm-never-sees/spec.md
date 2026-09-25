# Spec 248 — The copies the realm never sees

**Issue**: #2466 · **Branch**: `fix/2466-realm-probe-drift-guard` · **Phase**: 1 (Specify)
**Date**: 2026-09-25 · **Base**: `a54b11d0` (`origin/develop`, fetched 2026-09-25)
**Context**: test code only — one new file under `tests/Integration.Tests/Identity/`. No `src/`,
`apps/`, contract, AppHost, csproj, CI-workflow or realm-import change.
**Engineer**: `test-writer` (4a) only; there is no 4b (§6) · **Reviewer**: `backend-reviewer`
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**Phase 4a colour**: **red, by counterfactual** (§5) — the guard is new behaviour of the suite; its
red is observed by mutating each guarded copy, not by arriving red on an honest tree.
**ADRs**: ADR-0037 (phases), ADR-0144 (lane; colours; "implements decisions, does not make them"),
ADR-0139 (§Testing — new behaviour observed red first), ADR-0103 (integration tests via Aspire, no
Testcontainers), ADR-0052 / ADR-0053 (Shouldly; sentence-style names), ADR-0036 (smallest change)
**Constitution**: §IV — **N/A**. No leg of the event→overlay path is touched; a test that composes
the AppHost model without starting it. §II — N/A (no domain model touched). §Testing — red first.
**New ADR needed**: **No.** No production behaviour, contract, or architectural choice changes. The
decision to build a guard rather than a reader was taken and recorded by spec 191 (#2275) §Scope
decision and US-2; this spec delivers that recorded US-2.

---

## 1. The premise, re-checked against `a54b11d0`

| # | Issue claim | Status |
|---|---|---|
| 1 | `RealmProbe.Realm`/`.AdminClientId`/`.AdminClientSecret` are literal copies | **Holds.** `tests/Integration.Tests/Identity/RealmProbe.cs:54-56`. |
| 2 | …of values defined in `Realms/smart-sentinel-eye-realm.json` | **Holds, path corrected**: the file is `src/AppHost/Realms/smart-sentinel-eye-realm.json` — `"realm"` at `:2`, client `"identity-admin"` at `:261`, its `"secret"` at `:269`. All three constants match today. |
| 3 | `AppHost.cs:35` holds a fourth copy (what Identity presents) | **Holds.** `AddOverridableParameter("IdentityAdminClientSecret", "dev-only-identity-admin-secret", secret: true)`; flows to Identity as `Keycloak__AdminClientSecret` at `AppHost.cs:529`. |
| 4 | `AppHostParameterOverrideTests.cs:50` holds a fifth | **Holds.** `DeclaredDefaults["IdentityAdminClientSecret"]`, a hand transcription of site 2 that `A_parameter_with_no_argument_keeps_its_default` asserts against the composed model. |
| 5 | `KeycloakAdminOptions.cs:13,26` defaults are copies | **Holds, and they are more than defaults**: the AppHost sets **no** `Keycloak__Realm` or `Keycloak__AdminClientId` for the Identity API (grep of `AppHost.cs`), so in every dev and CI boot these two defaults **are** the realm and client Identity authenticates as. `AdminClientSecret` defaults to `string.Empty` by design (always supplied by configuration) and is not a copy. |
| 6 | Nothing in the csproj makes the realm JSON visible to the test project | **Holds but is not a blocker — premise superseded.** The repo already reads files from source by walking up from `AppContext.BaseDirectory` to `SmartSentinelEye.slnx`. `tests/Integration.Tests/AppHostE2ESwitchTests.cs:218` does exactly that, carries `Trait("Category","FixtureLogic")`, and runs green in CI's Docker-free step (`ci.yml:91-96`) against the checkout. `Architecture.Tests` reads this very realm file the same way (`RealmIdentityTests.cs:189`, `RealmAudienceTests.cs:152`, `SeededCredentialStrengthTests.cs:427`). Spec 191 reason (2)'s worry that a root walk "differs between a local run and CI's checkout layout" is contradicted by that green CI step. **No csproj `Content`/`None`/`Link` item is needed or added.** |

### 1.1 Further copies found, deliberately out of scope

The realm name `"smart-sentinel-eye"` is also a literal default in `MosquittoOptions.cs:40`,
`SimulatorOptions.cs:43`, `ScenarioSimulator/Program.cs:162`, `AuthenticationDefaults.cs:37`,
`StreamFabAttributionOptions.cs:15`, `StreamDistributionInfrastructureModule.cs:190`,
`ReverseIndexSeederOptions.cs:23` and `RunModeStackAddress.cs:36`; and the five other AppHost
client-secret parameters (`AppHost.cs:41-64`) mirror realm-import secrets exactly as site 2 does.
Same defect class, not in #2466's four named sites. **Recorded here, not swept** — widening an
autonomous-lane issue past its stated scope is the drift ADR-0144 exists to stop. The guard's shape
(§4) extends to them by adding rows; the orchestrator may file a follow-up. Unit-test fakes
(`Identity.Infrastructure.Tests/*` `const Realm`) talk to a fake server and carry no drift risk.

---

## 2. The one judgement: guard, reader, and colour

**A reader** substitutes the file for the constant — the file goes on the runtime path of every
integration test. Spec 191 refused that (its reasons 1 and 3 still stand: new failure modes on every
test; and the file can be *ahead* of the Keycloak volume, so reading it would present a secret the
live realm does not yet hold — the same 401 from the other direction). **Not built.**

**A guard** leaves every constant where it is and adds one test class that fails when a copy
disagrees with the file. Nothing else reads the file; no existing test changes path.

**Colour: behaviour-changing → red.** Reasoning:

1. *Behaviour-preserving* (characterisation) means: something moves, and covering tests captured
   green before must pass unmodified after. Nothing under `src/` or any existing test moves here.
   There is no change for a characterisation to hold still around — the colour does not fit.
2. The suite gains a behaviour it does not have today: **a realm edit not mirrored into a copy fails
   the build, naming the copy.** Today the same edit passes the build and surfaces as a 401 inside an
   unrelated test. That is new behaviour; ADR-0139 requires it observed red first.
3. ADR-0144: ambiguity resolves to red.
4. **How the red is observed.** A guard of an invariant that holds today cannot arrive red on the
   honest tree without falsifying the tree. Its red is the **counterfactual**: mutate each guarded
   copy, run, capture the failure verbatim, revert. This is spec 191 US-2's own stated requirement
   ("mutate one constant, watch it fail") and the repo's established practice for guards (spec 219
   SC-4; spec 237 T008). **The guard arriving green on the unmutated tree is expected and is not a
   phase-4a failure; the phase-4a failure is any counterfactual that stays green.**

---

## 3. User stories

### US-1 (P1) — A realm edit that is not mirrored fails the build, naming the stale copy.

One slice, independently shippable: one new test class in the Docker-free `FixtureLogic` CI step.

**Acceptance scenarios**

```gherkin
Scenario: happy — every copy agrees with the realm import
  Given src/AppHost/Realms/smart-sentinel-eye-realm.json as committed
  When the FixtureLogic tests run
  Then every fact in RealmImportMirrorTests passes

Scenario: conflict — the realm import is edited and a copy is not
  Given the realm import's identity-admin "secret" is changed
  And RealmProbe.AdminClientSecret and the AppHost default are not
  When the FixtureLogic tests run
  Then the RealmProbe secret fact fails naming RealmProbe.AdminClientSecret, both values, and the realm file path
  And the AppHost-default fact fails naming the IdentityAdminClientSecret parameter

Scenario: conflict — a copy is edited and the realm import is not
  Given KeycloakAdminOptions' Realm default is changed
  When the FixtureLogic tests run
  Then the KeycloakAdminOptions realm fact fails naming KeycloakAdminOptions.Realm

Scenario: conflict — site 3 drifts from site 2
  Given AppHostParameterOverrideTests.DeclaredDefaults["IdentityAdminClientSecret"] is changed alone
  When the FixtureLogic tests run
  Then the existing A_parameter_with_no_argument_keeps_its_default fails (site 3 is guarded transitively, §4)

Scenario: bad input — the realm import cannot be read as expected
  Given the realm file is absent, or has no client whose clientId is the one being checked, or that client has no "secret"
  When the guard runs
  Then it fails with a message naming the path and the missing element — never a bare KeyNotFoundException, never a pass

Scenario: auth — N/A
  The guard makes no network call and presents no credential; it compares values.
```

**Independent end-to-end test procedure**: `dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release --filter "Category=FixtureLogic"` — green, with the new class's six facts in the count (read the trx). Then the five counterfactuals of §5, each red with the named message, each reverted to an empty `git diff`. No Docker, no running stack; safe beside a live `aspire run` (composition only — `AppHostParameterOverrideTests` precedent).

---

## 4. What the guard compares, per site

Expected values always come **from the realm file**; actual values from the copy. No fact compares a
value with itself (memory: an assertion must not check its own input).

| Site | Copy (actual) | Realm import (expected) | Shape |
|---|---|---|---|
| 1a | `RealmProbe.Realm` | root `"realm"` | const equality |
| 1b | `RealmProbe.AdminClientId` | exactly one `clients[]` entry with that `clientId` | existence (a clientId *is* its lookup key) |
| 1c | `RealmProbe.AdminClientSecret` | `"secret"` of the client in 1b | const equality |
| 4a | `new KeycloakAdminOptions().Realm` | root `"realm"` | **fresh, un-initialised instance** |
| 4b | `new KeycloakAdminOptions().AdminClientId` | exactly one `clients[]` entry with that `clientId` | **fresh, un-initialised instance** |
| 2 | composed `ParameterResource` `IdentityAdminClientSecret`, no `Parameters:` argument | `"secret"` of the client named by `new KeycloakAdminOptions().AdminClientId` | AppHost model composition, not a source scan |
| 3 | `AppHostParameterOverrideTests.DeclaredDefaults["IdentityAdminClientSecret"]` | — | **transitive, no new fact** |

**Site 4's different shape.** `KeycloakAdminTokenProviderTests` populates `Realm`/`AdminClientId`
explicitly, so it never exercises the defaults. The guard therefore constructs `new
KeycloakAdminOptions()` with **no object initialiser and no binding** and reads the two properties —
the object exactly as production has it before configuration, which (§1 row 5) is also exactly what
the Identity API runs with in dev and CI. `AdminClientSecret` is excluded: its default is
`string.Empty` by design.

**Site 2's pairing.** The secret is looked up under the client **Identity actually presents** — the
`KeycloakAdminOptions` default client id — rather than under `RealmProbe.AdminClientId` or a literal,
because that is the production pairing (`AppHost.cs:529` supplies only the secret). If 4b is broken
the lookup fails too; its message names the 4b fact as the likely cause.

**Site 2 via composition, not source text.** `DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>([])`
builds the model without starting resources (spec 188's T001 spike; `AppHostParameterOverrideTests`
precedent), so the guard sees the value `AddOverridableParameter` actually resolves, including any
future change to how the fallback is computed.

**Site 3 is guarded transitively — no edit to `AppHostParameterOverrideTests`.** The existing
FixtureLogic fact `A_parameter_with_no_argument_keeps_its_default` asserts composed site 2 == site 3.
The new fact asserts composed site 2 == realm. Every single-copy or two-copy drift among {realm, 2, 3}
is caught by one of the two: realm alone → new fact; site 2 alone → both; site 3 alone → existing;
realm+2 without 3 → existing; realm+3 without 2 → new. Counterfactual C3 (§5) proves the existing fact
still bites. Making `DeclaredDefaults` non-private to assert it directly would add a second assertion
of a fact already asserted.

**The file read.** A private `RepositoryRoot()` walk to `SmartSentinelEye.slnx`, byte-for-byte the
shape of `AppHostE2ESwitchTests.cs:218`, then `src/AppHost/Realms/smart-sentinel-eye-realm.json`,
parsed with `System.Text.Json.JsonDocument`. It raises the Integration.Tests root-walk count spec 190
recorded (3 → 4); extracting a shared reader is spec 190's recorded follow-up, not this issue.

---

## 5. Counterfactuals — the red evidence (quoted verbatim in the PR)

Each: mutate one value, rebuild (touch/`--no-incremental`: a restored file keeps its old timestamp),
run the FixtureLogic filter, capture, revert, confirm `git diff` empty.

| # | Mutation | Must go red |
|---|---|---|
| C1 | `RealmProbe.AdminClientSecret` → `"counterfactual"` | 1c only |
| C2 | `AppHost.cs:35` default → `"counterfactual"` | new site-2 fact **and** existing `A_parameter_with_no_argument_keeps_its_default` |
| C3 | `AppHostParameterOverrideTests.cs:50` → `"counterfactual"` | existing `A_parameter_with_no_argument_keeps_its_default` only (proves site 3's transitive cover) |
| C4 | `KeycloakAdminOptions.Realm` default → `"counterfactual"` | 4a only |
| C5 | realm file `"realm"` → `"counterfactual"` (the issue's headline case: import edited, nothing mirrored) | 1a and 4a |

Any other pattern — a mutation that stays green, or reds beyond those listed — stops phase 4a.

---

## 6. Why there is no 4b

The deliverable is the guard. There is no production fix: every copy agrees today. The lane's usual
4a (tests) → 4b (implementation) split collapses to 4a plus counterfactuals. If a counterfactual stays
green, that is a defect in the guard and goes back to `test-writer`, not forward to an engineer.

## 7. Success criteria

- SC-1: six new facts in one class, `Trait("Category","FixtureLogic")`, all green on the tree.
- SC-2: C1–C5 each produce exactly the reds in §5, quoted verbatim in the PR body.
- SC-3: `git diff --stat origin/develop` touches only the new test file and `specs/248-*`.
- SC-4: Release build clean (analyzers, collection-expression rule); `IntegrationTestSelectionTests` green.

## 8. Assumptions

- A1: `ParameterResource.GetValueAsync` resolves promptly in composition mode (verified by spec 188
  T001; relied on by `AppHostParameterOverrideTests`). A hang is a finding, bounded by a 10 s token.
- A2: the file is guarded as a design artefact. The guard proves copies agree with the **file**, not
  with a running Keycloak whose volume may predate an edit — that live check already happens in every
  Aspire integration test that authenticates with `RealmProbe`'s constants.
