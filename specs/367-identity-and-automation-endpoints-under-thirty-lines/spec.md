# Spec 367: The identity and automation endpoints under thirty lines

**Feature Branch**: `refactor/2932-identity-automation-servicedefaults-s138` (cut from `origin/develop` at `3b10a210`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Four judgement calls
are marked explicitly (§6). **Scope is narrowed from the issue**: this spec keeps the seven
`*.Api` methods and moves the seven `ServiceDefaults` methods to follow-up issues (§8, J1).

**Input**: Issue [#2932](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2932),
*ADR-0084 S138: split the long methods in Identity and Automation Api, and ServiceDefaults*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving, so Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green"). Engineer: **backend-engineer**.
**Security-sensitive**: device registration, kiosk enrolment and webhook secret rotation mint and
return Keycloak client secrets, and each runs a fab guard, an idempotency scope and (rotation) an
upsert precondition in a fixed order. Phase 6 runs `/security-review` as well as `/code-review`.

**Spec number.** 367. Checked 2026-10-15 after `git fetch`: `origin/develop` highest 365; highest
across every remote head (`git ls-tree` over `refs/remotes/origin/*`) 365; worktree
`D:\Github\sse-2931` (unpushed, #2931) holds 366. Re-check before the PR is opened.

**ADRs.** ADR-0084 (S138 <= 30 lines, S107 <= 4 parameters, advisory `warning`), ADR-0144
(autonomous lane; phase 4a colours; no gate weakened), ADR-0070 (Minimal APIs), ADR-0036 (smallest
change), ADR-0105 (`Ensure.That`), ADR-0113 (`If-Match` / upsert precondition), ADR-0142
(idempotency scope = route, caller, fab, fingerprint; replay never re-runs the command), ADR-0114
(write-fab resolution), ADR-0089 (`ApiError` → problem), ADR-0052/ADR-0103 (xUnit + Shouldly + Moq;
no Testcontainers). No new ADR: this changes shape, not a decision.

**Prior art reused.** Spec 363 (#2929, LayoutComposition.Api) and spec 365 (#2930,
EventIngestion.Api), both merged: in-process endpoint host + route-table baseline in
`tests/Architecture.Tests`, `BoundaryParse.TryParse` for body parses, a `…Scope(…)` helper per keyed
create, and `private static void Map…(RouteGroupBuilder <group-local>)` per route. Spec 366 (#2931)
is unmerged and is **not** depended on (J2).

---

## 1. What was measured (premise verified, not assumed)

```sh
dotnet build src/Identity/Api -c Release --no-incremental
dotnet build src/Automation/Api -c Release --no-incremental
```

Result on `3b10a210`: **all 14 of the issue's S138 hits are present, same methods, same counts.**
Nothing is stale.

| # | File:line | Method | Lines | This spec |
|---|---|---|---|---|
| 1 | `Identity/Api/DevicesEndpoints.cs:22` | `MapDevicesEndpoints` | 33 | **keep** |
| 2 | `Identity/Api/DevicesEndpoints.cs:101` | `Register` | 58 | **keep** (security) |
| 3 | `Identity/Api/KiosksEndpoints.cs:22` | `MapKiosksEndpoints` | 33 | **keep** |
| 4 | `Identity/Api/KiosksEndpoints.cs:101` | `Enroll` | 56 | **keep** (security) |
| 5 | `Identity/Api/WebhookRotationEndpoints.cs:91` | `Rotate` | 63 | **keep** (security) |
| 6 | `Automation/Api/RulesEndpoints.cs:30` | `MapRulesEndpoints` | 62 | **keep** |
| 7 | `Automation/Api/RulesEndpoints.cs:138` | `Create` | 52 | **keep** |
| 8 | `ServiceDefaults/ConcurrencyHeaders.cs:74` | `TryReadUpsertPrecondition` | 31 | moved, §8 A |
| 9 | `ServiceDefaults/Idempotency/IdempotencyHeaders.cs:49` | `TryRead` | 31 | moved, §8 A |
| 10 | `ServiceDefaults/OutboxBacklogHealthCheck.cs:77` | `CheckHealthAsync` | 41 | moved, §8 A |
| 11 | `ServiceDefaults/Idempotency/IdempotencyStore.cs:34` | `BeginAsync` | 61 | moved, §8 A |
| 12 | `ServiceDefaults/AuthenticationDefaults.cs:29` | `AddBearerAuthentication` | 46 | moved, §8 B |
| 13 | `ServiceDefaults/AuthenticationDefaults.cs:183` | `AddRevocationCheck` | 32 | moved, §8 B |
| 14 | `ServiceDefaults/WolverineDefaults.cs:40` | `AddWolverineForContext` | 56 | deferred, §8 C |

S107 in the two Api projects' hand-written files (out of scope, **must not grow**): Identity.Api 4
(`DevicesEndpoints.Register`, `KiosksEndpoints.Enroll`, `WebhookRotationEndpoints.Rotate`,
`IdentityFabResolution.ResolveReadFabsAsync`), Automation.Api 6 (`RulesEndpoints.Create`,
`Publish`, `Archive`; `RulesEndpoints.Reads.cs` ×3). Migrations are excluded by the issue.

## 2. User story

### US1 (P1): Identity.Api's and Automation.Api's endpoint methods fit ADR-0084's 30-line limit with no observable change

Operators, the management console, the gateway and the generated OpenAPI client see exactly the
same API before and after: the same 14 routes (Identity 8, Automation 6), verbs, names, summaries,
tags, authorization policies and declared statuses. Every request gets the same status, problem
body, `Location` and response body, in the same refusal order. The fab guard, the idempotency
store, the Keycloak admin port and the repositories are called with the same arguments, or not at
all, exactly as before. A replayed key still returns the stored client's secret **without**
re-running the command.

**Why P1 / why one slice.** Seven methods in four files of two projects, one layer (Api). Below the
~10-method mark, so **one PR in four commit groups** (plan §6). The three security-sensitive
Identity handlers form their own groups and are separately reviewable.

**Independent test.** Characterisation tests (§4) are observed green on `3b10a210` before any
production edit, then stay green **unmodified** after each refactor commit. Afterwards the §1
builds print zero S138 hits in `Identity.Api` and `Automation.Api` and the same 10 S107 hits.

#### Acceptance scenarios

```gherkin
Scenario: The route tables are identical after the refactor
  Given MapDevicesEndpoints, MapKiosksEndpoints, MapWebhookRotationEndpoints and MapRulesEndpoints mapped in process on 3b10a210
  And their canonical route-table rendering captured as literal baselines
  When the same rendering is taken after the refactor
  Then each equals its baseline line for line
    # pattern, verbs, name, summary, tags, authorization data, produces metadata in order,
    # accepts metadata, handler method name and parameter list (names, types, attributes)

Scenario: An unkeyed device registration creates a client in the named fab
  Given an operator holding fab "dresden" and Keycloak accepting the client
  When they POST /devices/register?fabId=dresden with a deviceType and deviceIdentifier and no Idempotency-Key
  Then the response is 201 with Location "/devices/{clientId}" and the DeviceCredentialsDto body exactly
  And the Keycloak port was asked to create that client and the repository saved it once

Scenario: A rule is parsed field by field in declaration order
  When a caller POSTs /rules with an invalid name and an invalid predicate
  Then the response is 400 RULE_INVALID_INPUT carrying the name's message, not the predicate's
```

Bad request and refusal order:

```gherkin
Scenario: Every keyed create reads Idempotency-Key first
  When a caller sends a malformed Idempotency-Key and an invalid fab to Register, Enroll, Rotate or Create
  Then the response is 400 IDEMPOTENCY_KEY_MALFORMED
  And the fab guard and the idempotency store were never called

Scenario: Rotation runs the fab guard before it reads the upsert precondition
  Given a caller the fab guard refuses
  When they POST /webhook-integrations/{name}/rotate with no If-Match and no If-None-Match
  Then the guard's refusal is what escapes, not 428 IF_MATCH_REQUIRED

Scenario: Rule creation resolves the fab before it parses the body
  Given a caller holding two fabs
  When they POST /rules with no fabId and an invalid body
  Then the response is the RULE_FAB_REQUIRED refusal
```

Conflict / precondition:

```gherkin
Scenario Outline: Rotation's upsert precondition is unchanged
  When the rotation carries <headers>
  Then the response is <status> <code> and the idempotency store was never called
  Examples:
    | headers                         | status | code               |
    | neither header                  | 428    | IF_MATCH_REQUIRED  |
    | If-Match and If-None-Match      | 400    | IF_MATCH_MALFORMED |
    | If-None-Match: "1"              | 400    | IF_MATCH_MALFORMED |
```

Auth and idempotency (the security-relevant core):

```gherkin
Scenario Outline: Each keyed create is scoped to route, caller, fab and fingerprint
  Given an operator in "dresden" sending <route> with Idempotency-Key "k1"
  Then the idempotency store is begun with (<endpoint>, that operator, Some("dresden"), <fingerprint>)
  Examples:
    | route                                            | endpoint                                  | fingerprint                       |
    | POST /devices/register                           | POST /devices/register                    | Of(body)                          |
    | POST /kiosks/enroll                              | POST /kiosks/enroll                       | Of(body)                          |
    | POST /webhook-integrations/line-7/rotate, If-None-Match * | POST /webhook-integrations/{name}/rotate | Of(body, "line-7", "*")  |
    | POST /webhook-integrations/line-7/rotate, If-Match "3"    | POST /webhook-integrations/{name}/rotate | Of(body, "line-7", "3")  |
    | POST /rules                                      | POST /rules                               | Of(body)                          |

Scenario: A replayed rotation returns the stored secret without rotating again
  Given the idempotency store answers CompletedWith(client) for the key
  When the retry arrives carrying the same (now stale) If-Match
  Then the response is 200 with the replayed client's version, clientId, the route's name, its fab and its secret
  And the rotation command handler's Keycloak calls (create, rotate secret) were never made

Scenario: A refused registration releases the key
  Given a keyed registration the command handler refuses
  Then the response is the handler's problem exactly and the reservation was released, not completed
```

Scope policies (401/403 from `RequireAuthorization`) are middleware. They are pinned by the
route-table baseline and the existing CI integration tests (§4.3), not driven in process.

## 3. Functional requirements

- **FR-001** Each method kept in §1 is at most 30 S138-lines. The §1 builds print no S138 hit in
  `src/Identity/Api` or `src/Automation/Api` and exactly the same 10 S107 hits, by file and method
  (line numbers may move). No other new warning anywhere. Every new helper therefore takes **at
  most 4 parameters**.
- **FR-002** No route, verb, name, summary, tag, authorization policy, `Produces*`/`Accepts`
  metadata, handler method name or handler parameter list changes; registration order is unchanged
  (§4.1 baselines equal).
- **FR-003** Every request gets the same status, problem body, `Location` and body as before, in the
  same refusal order:
  - `Register` (devices), `Enroll` (kiosks): key → fab parse (`?fabId=`) → fab guard → idempotent dispatch (command | replay).
  - `Rotate`: key → fab parse (`body.FabId`) → fab guard → upsert precondition → idempotent dispatch (command | replay), with the fingerprint over (body, route `name`, precondition text `"*"` or the version).
  - `Create` (rules): key → write-fab resolution (ADR-0114) → body parse (name → predicate → action → trigger source → trigger kind) → idempotent create.
- **FR-004** (security) A replay never runs the command and never calls Keycloak's create or rotate.
  The secret reaches the response exactly where it does today and nowhere else (not logged, not
  stored, not echoed in a problem). No refusal moves before or after a guard, lookup or store call
  it currently follows or precedes. The idempotency scope's five components and their order are
  unchanged.
- **FR-005** The tokens the source-scanning guards read stay in the handler bodies (plan §3.5):
  `IdempotencyHeaders.TryRead` and `IdempotentRequest.Execute(Create)?Async(` in all four;
  `ConcurrencyHeaders.TryReadUpsertPrecondition` in `Rotate`. Mappings stay in the four
  `*Endpoints.cs` files on receivers named exactly as their `MapGroup` local (`group`, `reads`). No
  new `*Endpoints.cs` file and no new `RulesEndpoints` partial file.
- **FR-006** No guard, existing test, suppression, threshold, `.editorconfig` or `SonarLint.xml` is
  edited. No `ServiceDefaults`, Application, Domain, Infrastructure, migration, `Shared.Contracts`
  or AppHost change.

## 4. Characterisation (phase 4a, observed green before any production edit)

All new tests live in `tests/Architecture.Tests`, which already references both Api projects and
Moq. Problem bodies are asserted **exactly** (string equality of the JSON). Collaborator calls are
verified with exact arguments, or `Times.Never` where order is the claim. Captured scopes and
commands are asserted field by field.

### 4.1 Route tables (in process): proves FR-002

- `IdentityRouteTableCharacterisationTests`: 8 routes (devices 3, kiosks 3, webhook rotation 2).
  Covers methods 1 and 3 completely and the handler signatures of 2, 4 and 5.
- `AutomationRouteTableCharacterisationTests`: 6 routes (both `RulesEndpoints` partials). Covers
  method 6 completely and `Create`'s signature.

### 4.2 Endpoint behaviour (in process): proves FR-003 and FR-004

Real command and replay handlers, Moq behind them, so both the extracted code and its dispatch
tail run.

| Method | Cases (exact status + body; verified calls) |
|---|---|
| Register (devices) | malformed key → key refusal, guard + store never. Malformed key + invalid fabId → key refusal. Invalid fabId → 400 `DEVICE_INVALID_INPUT` (VO message), guard never. Guard refuses → its refusal as `3b10a210` produces it, store never. Keyed → scope field by field. Unkeyed → 201, `Location`, body, Keycloak create args, repo add+save. Keyed + handler refusal → handler's problem, `ReleaseAsync`. Replay (`CompletedWith`) → 201 with deviceType/deviceIdentifier **from the retry body** and secret from `ReadClientSecretAsync`; `CreateClientAsync` never. Replay of a missing client → the replay handler's problem. |
| Enroll (kiosks) | The same rows with `KIOSK_INVALID_INPUT`, `/kiosks/{clientId}`, body `KioskCredentialsDto` (all fields server-held on replay). |
| Rotate (security) | Malformed key → key refusal, guard never. Invalid `body.FabId` → 400 `WEBHOOK_INVALID_INPUT`, guard never. Guard refuses with no precondition headers → guard's refusal, not 428. The three precondition rows of §2, store never. Keyed with `*` and with `"3"` → scope with fingerprint `Of(body, name, "*")` / `Of(body, name, "3")`. Replay with a stale `If-Match` → 200, body field by field (version from replay, name from route), handler's Keycloak create/rotate never. One command path through the real handler (a refusal or a create), exact. |
| Create (rules) | Malformed key → key refusal, guard never. Multi-fab no fabId → `RULE_FAB_REQUIRED`, store never. Multi-fab no fabId + invalid body → `RULE_FAB_REQUIRED`. Parse order: name+predicate → name's message; predicate+unknown action → predicate's; action-field missing+trigger source → the action's; source+kind → source's. Unknown `ActionType` → exact message. Keyed → scope (`POST /rules`, caller, fab, `Of(body)`). Unkeyed → 201, `Location: /rules/{name}`, body the identifier, the rule added with every field. Replay → 201 with the stored identifier, repository never. |

A case that cannot be driven in process is dropped, named in the PR, and the §4.3 CI test that
observes it named instead. Never substitute a weaker assertion.

### 4.3 Existing coverage (CI integration, Docker; the second observer)

Not runnable in the lane's local loop; CI's integration shards observe them on the PR.

- **Devices / kiosks:** `IdempotentRegistrationIntegrationTests`,
  `IdempotencyKeyReuseIdentityIntegrationTests`, `AbsentDeviceIdentifierIsRefusedIntegrationTests`,
  `CrossFabRegistrationConflictIntegrationTests`, `CrossFabDisableIntegrationTests`,
  `CrossFabListIntegrationTests`, `KioskInheritedPrivilegeIntegrationTests`,
  `RegisteredClientConcurrencyIntegrationTests`.
- **Webhook rotation:** `CrossFabWebhookRotationIntegrationTests`, `RotateRevokeRaceIntegrationTests`,
  `RotateRevokedWebhookIntegrationIntegrationTests`, `WebhookRevocationDisablesClientIntegrationTests`.
- **Rules:** `RuleLifecycleIntegrationTests`, `RuleFabResolutionIntegrationTests`,
  `RuleReadIntegrationTests`, `IdempotencyKeyReuseAcrossContextsIntegrationTests` (rule case).
- **In-process, already on develop:** `RuleActionBuildCharacterisationTests` (`BuildAction` messages,
  by reflection; `BuildAction` keeps its name and signature).

**Gaps they leave, which 4.2 closes:** key-vs-fab and guard-vs-precondition order; the
`Times.Never` facts; the scope's argument order and the rotation fingerprint's three inputs; the
replay's field mapping (retry body vs server-held) and "no Keycloak create/rotate on replay"; the
rule parse order across all five fields.

## 5. Success criteria

- **SC-001** §1 builds: 0 S138 in `Identity.Api` and `Automation.Api` hand-written files. S107 set
  unchanged (10). No new warning.
- **SC-002** §4.1 and §4.2 observed green on `3b10a210` (output quoted in the PR), green and
  byte-unmodified after each refactor commit.
- **SC-003** All of `tests/Architecture.Tests` green; CI integration shards green on the PR.
- **SC-004** `git diff --stat origin/develop` touches only the 4 Api files of §1 rows 1-7, the new
  test files, and this spec directory.
- **SC-005** The follow-up issues of §8 are filed and on Project #13 before the PR is opened, and
  the PR body says this issue's scope was narrowed (so it closes #2932 honestly).

## 6. Judgement calls (marked)

- **J1: ServiceDefaults moves out of this PR (risk triage).** Those seven methods run in every one
  of the nine services: bearer authentication and the revocation hook, the at-most-once claim, the
  Wolverine wiring, the outbox readiness signal and the two header parsers. Their characterisation
  uses different techniques in a different project (`tests/ServiceDefaults.Tests`: DB interceptors,
  service-collection snapshots, options resolution), and two of them need a net that does not exist
  yet. Bundling them with the Api handlers would make one security review cover two unrelated blast
  radii. §8 gives each a verdict and the net it needs.
- **J2: independent hosts, not a stack on #2931.** Spec 366's `RouteTableRendering.cs` and
  `InProcessEndpointDriver.cs` are unmerged. Rule, decided at the start of phase 4a: if both files
  exist on `origin/develop`, rebase and use them; otherwise each route-table test carries a private
  renderer copy (as specs 363/365 did) and each host its own `SendAsync`. File names here
  (`Identity…`, `Automation…`) collide with nothing in #2931.
- **J3: `BoundaryParse.TryParse` for the fab parse in Register/Enroll/Rotate and the five-field
  parse in Create.** Its refusal is byte-identical to the inline catch (spec 365 J1). Consequence:
  those bodies no longer spell `StatusCodes.Status400BadRequest`, so
  `RouteValueRefusalDeclarationTests` stops *demanding* their chains' 400. None of them parses a
  route value (`Rotate`'s `{name}` is passed through unparsed), and §4.1 pins every 400 declaration
  exactly, which is strictly stronger.
- **J4: the command stays constructed inside the dispatch lambda in the Identity handlers.**
  Hoisting it is harmless (the commands are validation-free records) but moves code across the
  idempotency boundary for no line gain; the replay lambda is what gets extracted. `Create`
  (rules) hoists `createRuleCommand` per the CLAUDE.md convention, since its parse already ran.

## 7. Latency budget impact (constitution §IV)

**N/A.** Client registration, enrolment, secret rotation and rule authoring are administrative
writes, on no leg of the event → overlay path. Rule *evaluation* (the "event → overlay state" leg)
is in Automation.Application and is untouched.

## 8. Moved out of this issue: ServiceDefaults verdicts (to file as follow-up issues)

Measured on `3b10a210`. Each row says what covers it today, the gap, and the verdict.

### A. Request parsing and store/health reads: **refactor, new issue, agent:ready** (one PR)

| Method | Covered today | Gap to characterise first | Notes |
|---|---|---|---|
| `ConcurrencyHeaders.TryReadUpsertPrecondition` (31) | `UpsertPreconditionTests` (7), `ConcurrencyHeadersTests` (12), unit | none significant; confirm each branch's exact body | One line over. Extract the `If-None-Match` branch. Pure. |
| `IdempotencyHeaders.TryRead` (31) | **No direct unit test**; only through endpoint characterisations (specs 363/365) | `IdempotencyHeadersTests`: absent; present-but-zero-values; two headers; empty; whitespace; a value `IdempotencyKey.From` refuses; valid → `Some`; exact problem bodies | Pure, but every keyed create in nine contexts calls it. |
| `OutboxBacklogHealthCheck.CheckHealthAsync` (41) | `OutboxBacklogHealthCheckTests` (3): only the catch paths | The four read outcomes: pending 0; below both thresholds; attempts at 5; pending at 5,000 (and 4/4,999); exact description, data dictionary, log record. Driven with the existing interceptor technique: suppress the connection open, return canned readers | Readiness is liveness (ADR-0154): a wrong `Degraded`/`Healthy` changes routing. Extract the post-read assessment; catch clauses untouched. |
| `IdempotencyStore<T>.BeginAsync` (61) | 23 Docker integration tests: `IdempotencyFencingIntegrationTests` (6), `IdempotencyRequestBindingStoreIntegrationTests` (9), `StaleIdempotencyReservationIntegrationTests` (8) | An in-process characterisation via `DbConnectionInterceptor` + `DbCommandInterceptor`: **pin both SQL texts byte for byte and the parameter values/order**, and the five outcomes (`ReservedAs` with the returned timestamp; `InProgress` on an empty read; `Mismatched` on fab, fingerprint or NULL-fingerprint mismatch; `InProgress` on a matching unfinished row; `CompletedWith`) | **Highest-risk method in scope**: the at-most-once claim (ADR-0142, #2290 reclaim, #2491 fencing). Atomicity lives in the SQL text, which the pin freezes. Last commit group, separately reviewed; `/security-review`. Extract `ClaimAsync`, `ReadBindingAsync`, and the binding classification; comments stay with their SQL. |

### B. Bearer authentication composition: **refactor, new issue, agent:ready, security** (one PR)

| Method | Covered today | Gap to characterise first |
|---|---|---|
| `AuthenticationDefaults.AddBearerAuthentication` (46) | `BearerAudienceTests` (3), `MetadataTransportTests` (4), `BearerRevocationHookTests` (6, including chaining before and after), `RegisteredPolicyTests`, `DefaultEndpointsTests` | The Keycloak URL fallback chain (connection string → `services:…:http:0` → `…:https:0`) and the exact exception message; the resolved `JwtBearerOptions` (`Authority`, `RequireHttpsMetadata`, `ValidAudiences`, `MapInboundClaims`); the **order of the five `IExceptionHandler` registrations** (load-bearing: the concurrency handler must precede the unique-constraint handler); an ordered service-descriptor snapshot (spec 348's `…RegistrationCharacterisationTests` render) |
| `AuthenticationDefaults.AddRevocationCheck` (32) | `RevocationListOptionsValidationTests` (2), `RevokedClientRefresherWiringTests` (1) | `TryAdd` precedence (a pre-registered registry/source wins); the two named `HttpClient`s (base address `http://identity`, the authorization handler, retry-every-method); the `revocation-snapshot` health check's name, failure status and tags |

Why separate from A: a different review (authentication, every service), a different net
(composition snapshots, not request/response), and a change here fails open silently if wrong.

### C. `WolverineDefaults.AddWolverineForContext` (56): **defer; new issue, NOT agent:ready**

- **Why.** The listener configuration, conventional routing and additive local routing are applied
  by Wolverine at startup against discovered message types, so they are not observable in process
  without a broker. Every past regression here was silent: two `ConfigureListeners` calls reverted
  the listener count to 1; exclusive local routing dropped the external copy of `*V1` events;
  `ServiceLocationPolicy.NotAllowed` dropped a handler while `/health` stayed green. The method also
  carries S107 (9 parameters), and the natural fix (an options record across nine call sites) is a
  public-signature convention decision, which the issue puts out of scope and the lane may not make.
  Splitting S138 now would likely be redone by that decision.
- **Safety net needed before any change.** (1) A `WolverineOptions` snapshot from a built,
  non-started host: `ServiceLocationPolicy`, `AutoBuildMessageStorageOnStartup`, the message-store
  schema, transaction policies, `ConventionalLocalRoutingIsAdditive`, discovery including the
  `*.Application` assembly, RabbitMQ transport present. (2) A descriptor snapshot of the
  post-`UseWolverine` registrations (`IEventBus`, `ITransactionalCommit`, `ILatencyBudget`,
  `IJourneyOrigin`, the `outbox-{prefix}` health check; the spec 348 snapshots pin only the first
  two). (3) A broker-level CI observation of `listenerCount > 1` (queue consumers), native acks and
  the durable-inbox opt-in.
- **Needs a maintainer decision** on the S107 options record first, then it can be `agent:ready`.

## 9. Locked tech choices

.NET 10 Minimal APIs (ADR-0070), `Result<T,E>` + `ApiError.ToProblem` (ADR-0047/0089),
`Ensure.That` (ADR-0105), `Option<T>` (ADR-0141), xUnit + Shouldly + Moq (ADR-0052), no
Testcontainers (ADR-0103).
