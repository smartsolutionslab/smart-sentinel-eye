# Spec 348: The identity context under its limits

**Feature Branch**: `refactor/2890-identity-metrics` (cut from `origin/develop` at `ed702aa5`)

**Created**: 2026-10-13

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked explicitly (§6).

**Input**: Issue [#2890](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2890),
*Clear the ADR-0084 metric warnings in Identity's Domain, Application and Infrastructure (16)*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving: Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green").

**Security-sensitive context.** Identity owns Keycloak client creation, secret rotation, the
create-branch revoke-race disable (#2789, `RotateRevokeRaceIntegrationTests`), the orphaned-client
sweep and the inherited-privilege strip. Every change here is shape-only, and the characterisation
pins in plan §4 exist to prove that the order of Keycloak calls, the compensation paths and the
exception filters did not move. Phase 6 includes a **security-reviewer** pass (plan §7).

**Spec number.** 348. Checked on 2026-10-13: `origin/develop` highest 345; every remote branch via
`git ls-tree` highest 345; worktrees `sse-2888` (346, #2888), `sse-2889` (347, #2889), `sse-1545`
(nothing above 338, no diff against `develop`); open PRs #2811 and #2837 carry no spec. Highest
visible 347, so 348. Re-check before the PR is opened.

**ADRs.** ADR-0084 (code metrics, amended 2026-09-17: S104 <= 300 lines/file, S138 <= 30
lines/method, S107 <= 4 parameters, S1541 <= 10; advisory `warning`), ADR-0144 (autonomous lane;
phase 4a colours; the lane may not weaken a gate), ADR-0036 (smallest change), ADR-0050
(`[LoggerMessage]` fields come from the template), ADR-0113 (two-layer concurrency; the Layer-2
`DbUpdateConcurrencyException` must reach the middleware), ADR-0142/0143 (idempotency and retry
safety; `RetryEveryMethod` opt-ins stay at their call sites), ADR-0141 (`Option<T>` for absences in
Application), ADR-0051 (per-context DI module), ADR-0134 (sweeps), ADR-0128 not involved.

---

## 1. What was measured (premise verified, not assumed)

The issue's command does not run as written (`src/Identity` holds no project file). The
equivalent, which compiles Domain and Application transitively:

```sh
dotnet build src/Identity/Infrastructure/SmartSentinelEye.Identity.Infrastructure.csproj \
  -c Release --no-incremental -p:TreatWarningsAsErrors=false 2>&1 \
  | grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning (S104|S107|S138|S1541|S134)' | sort -u \
  | grep -vE 'Api[\\/]' | grep Identity
```

Result on `ed702aa5`: 0 errors, **16 hits, exactly the 16 the issue lists, at the same lines.
None is stale.** No hit sits in `Migrations/`.

Baseline test counts on `ed702aa5` (`dotnet test -c Release`): Identity.Domain.Tests 54,
Identity.Application.Tests 157, Identity.Infrastructure.Tests 63, all green.

## 2. Decision per hit

| # | File:line | Member | Rule (measured) | Decision |
|---|---|---|---|---|
| 1 | `Application/Commands/Handlers/EnrollKioskCommandHandler.cs:18` | `HandleAsync` | S138 (68) | **Clear** by extraction |
| 2 | `Application/Commands/Handlers/RegisterDeviceCommandHandler.cs:20` | `HandleAsync` | S138 (76) | **Clear** by extraction |
| 3 | `Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:37` | primary constructor (8 DI dependencies) | S107 (8) | **Leave**: every test in `RotateWebhookClientCommandHandlerTests` and `StaleVersionRejectionTests` constructs it with eight arguments, so any change fails "tests unmodified"; folding dependencies is a DI design change |
| 4 | same `:48` | `HandleAsync` | S138 (115) | **Clear** by extraction |
| 5 | same `:48` | `HandleAsync` | S1541 (12) | **Clear** by the same extraction |
| 6 | same `:279` | `DisableIfRevokedSinceCommitAsync` | S138 (41) | **Clear** by extraction |
| 7 | `Application/KeycloakAdmin/OrphanedClientSweep.cs:100` | `ClassifyAsync` | S138 (38) | **Clear** by extraction |
| 8 | same `:100` | `ClassifyAsync` | S107 (5) | **Clear** by a private parameter object (J1) |
| 9 | `Application/Log.cs:61` | `RegisteredDevice` `[LoggerMessage]` | S107 (6) | **Leave**: parameters are the structured template's fields (ADR-0050) |
| 10 | `Domain/RegisteredClient/RegisteredClient.cs:39` | `RegisteredClient.Register` (public) | S107 (5) | **Leave**: public Domain factory called by three Application handlers and at least nine test call sites; a parameter object changes every caller (J2) |
| 11 | `Infrastructure/IdentityInfrastructureModule.cs:43` | `AddIdentityInfrastructure` | S138 (76) | **Clear** by extraction (public signature unchanged) |
| 12 | `Infrastructure/KeycloakAdmin/HttpKeycloakAdminClient.cs:1` | file | S104 (332) | **Clear** by splitting the class into `partial` files (J3) |
| 13 | same `:52` | `CreateClientAsync` | S138 (40) | **Clear** by extraction |
| 14 | same `:310` | `GetSubGroupNamesAsync` | S138 (34) | **Clear** by extraction |
| 15 | `Infrastructure/Log.cs:105` | `WebhookIntegrationStatusUnverifiable` `[LoggerMessage]` | S107 (5) | **Leave**: ADR-0050, as #9 |
| 16 | `Infrastructure/Persistence/Configurations/RegisteredClientConfiguration.cs:18` | `Configure` | S138 (52) | **Clear** by extraction |

**Cleared: 12** (9 S138, 1 S1541, 1 S107, 1 S104). **Left: 4** (2 `[LoggerMessage]`, 1 public
Domain factory, 1 DI constructor). Expected after-count: **4**, every one named in the PR body with
its reason.

## 3. User story

### US1 (P1): Identity's non-Api layers meet ADR-0084 wherever that is mechanical, with every security behaviour pinned first

As a maintainer, I want the 12 clearable hits gone, each by an extraction, a parameter object that
names one concept, or a `partial` split, **without** any change a Keycloak realm, an HTTP caller,
another context, a log reader or the database could observe.

**Why P1 / independently shippable.** It is the whole issue: one context, no contract, no
migration, no runtime resource, no Api file.

#### Acceptance scenarios

Characterisation (all must hold **before** the refactor and pass **unmodified** after):

```gherkin
Scenario: Every existing Identity test passes unmodified (happy path)
  Given Identity.Domain.Tests (54), Identity.Application.Tests (157), Identity.Infrastructure.Tests (63)
  When the refactor commits land
  Then all pass and `git diff <last 4a commit>..HEAD -- tests/` is empty

Scenario: Rotation keeps its call order on both branches (happy path, security)
  Given an Active integration and an existing webhook client at version 3
  When it is rotated with If-Match 3
  Then the calls are: status lookup, fab-scoped lookup, SaveAsync, RotateClientSecretAsync, publish, commit
  And the status lookup is called exactly once
  Given an Active integration and no client
  When it is rotated with If-None-Match *
  Then the calls are: status lookup, fab-scoped lookup, CreateClientAsync, Add, SaveAsync, status lookup, publish, commit
  And CreateClientAsync receives today's exact representation and fab group path

Scenario: The create-branch revoke race still disables and stays silent (conflict, security)
  Given the post-commit status answer is Revoked or Unverifiable
  Then DisableWebhookClientCommand runs, nothing is published, nothing is committed, and the 200 is returned
  Given the inner disable answers a failure other than WebhookClientNotFound
  Then the warning carries today's exact exception message, and the rotation is still published and committed

Scenario: Exception filters are unchanged (bad request / dependency failure, security)
  Given Keycloak throws InvalidOperationException or OperationCanceledException
  Then it propagates out of HandleAsync
  Given Keycloak throws any other exception
  Then the answer is KEYCLOAK_UNAVAILABLE carrying the exception message
  Given the rotate-branch save throws DbUpdateConcurrencyException
  Then it propagates and no secret is rolled

Scenario: The Keycloak admin client sends exactly today's requests, in today's order (security)
  Given a stub realm recording every request
  When a client is created fresh, replaces a disabled same-kind same-fab client, or fails after the POST
  Then the ordered (method, path and query) list equals today's, including the compensating DELETE
  And the original exception is rethrown after compensation
  When a secret is rotated or read, or sub-groups span more than one page
  Then the ordered requests and their query strings equal today's

Scenario: Enrolment and registration refuse in today's order with today's text (bad request)
  Given input that fails more than one validation
  Then the first check today wins, with today's message
  And a Keycloak conflict names the clientId the exception carries

Scenario: The orphan sweep reports the same unreachable clients in the same order
  Given a pass mixing a malformed clientId, an unreadable age, an absent service account and a failed disable
  Then Unreachable lists them in today's order and each is logged exactly as today

Scenario: The DI composition is unchanged
  Then AddIdentityInfrastructure registers the same Identity-owned services, in the same order, with the same lifetimes

Scenario: The EF model is unchanged
  Then IdentityDbContext's logical model, read through the public metadata API, equals the baseline captured on ed702aa5
```

**Auth scenario.** No authorisation boundary moves: no Api file, no scope, no `RequireScope`, no
fab resolution is touched. Fab scoping inside the handlers (the fab-scoped lookup, the fab group
path, the `sse.fab` attribute, the fab-match in `IsReplaceableDisabledClient`) is pinned above,
not changed.

#### Independent test procedure (end to end)

1. The `dotnet test` runs of plan §5, green before and after.
2. The grep of §1 prints exactly the 4 "Leave" rows after the change.
3. CI's Integration.Tests `Identity/*` green, notably `RotateRevokeRaceIntegrationTests`,
   `RotateRevokedWebhookIntegrationIntegrationTests`, `CrossFabWebhookRotationIntegrationTests`,
   `KioskInheritedPrivilegeIntegrationTests`, `OrphanedClientSweepIntegrationTests`,
   `IdempotentRegistrationIntegrationTests`, `IdempotencyKeyReuseIdentityIntegrationTests`:
   they run the real realm and Postgres. Docker-only, so observed in CI, not locally.

## 4. Functional requirements

- **FR-001** The 12 "Clear" hits of §2 report nothing after the change. If an extraction leaves a
  hit, the PR says so with the measured value; it is never suppressed.
- **FR-002** No public or `protected` signature changes. `ICommandHandler` implementations and
  their constructors, `IKeycloakAdminClient` and every `HttpKeycloakAdminClient` public method,
  `OrphanedClientSweep.SweepAsync`/`GraceWindow`/`SweptKinds`/`OrphanedClientSweepOutcome`,
  `AddIdentityInfrastructure`, `AddKeycloakAdminClient`, `ConfigureMessageHandling`,
  `RegisteredClient.Register`, everything in `Shared.Contracts` and every `Log.cs` partial keep
  their signatures.
- **FR-003** For every input: the order of Keycloak calls, repository calls, status lookups,
  publishes and commits; each `try` block's extent and each `catch` filter; error values, codes and
  messages; log calls (method, arguments, order) are identical.
- **FR-004** Each `HttpKeycloakAdminClient` method sends the same requests (verb, path, query,
  body, serializer options) in the same order. The resilience and authorisation wiring in
  `AddKeycloakAdminClient` (the token client's `RetryEveryMethod()` with its stated reason; the
  admin client's `KeycloakAdminAuthorizationHandler` and no retry opt-in) is not edited.
- **FR-005** Secrets: no new log call, no new field and no new exception message carries a secret
  value; the webhook token is never persisted (only `RegisteredClient` rows are; the
  `ClientSecret` VO stays unmapped).
- **FR-006** DI registrations keep their order and lifetimes, including the `Replace` of
  `IRevokedClientSource`; the EF configuration produces the same model.
- **FR-007** No new primitive on a Domain type (constitution §II): no Domain file changes.
- **FR-008** No suppression, `#pragma`, threshold, `.editorconfig` or `SonarLint.xml` change; the
  two existing narrowly scoped `#pragma` blocks in `IdentityInfrastructureModule.cs` move verbatim
  with their lines if their method moves; no guard edited; no file under any `Api/` or `Migrations/`
  directory touched; no existing test file edited.

## 5. Success criteria

- **SC-001** Before/after hit lists quoted in the PR body; after = the 4 "Leave" rows.
- **SC-002** Characterisation observed green on unrefactored code, green and unmodified after; both
  outputs quoted.
- **SC-003** `dotnet build -c Release` 0 errors; `dotnet format --verify-no-changes` clean on the
  Application and Infrastructure projects; coverage gates hold (Domain >= 90 %, Application >= 80 %).
- **SC-004** Phase 6 security-reviewer pass with no unresolved finding.

## 6. Judgement calls (marked)

- **J1. A private parameter object in Application.** `OrphanedClientSweep.ClassifyAsync` and
  `DisableAllAsync` both carry the pass's two running lists (`orphans`, `unreachable`): one concept,
  the pass's tally. They become one private nested type built once per `SweepAsync`. The
  `unreachable` list instance handed to `OrphanedClientSweepOutcome` stays the same list. Private,
  in Application, outside §II's Domain scope.
- **J2. Leaving the public Domain factory.** `RegisteredClient.Register` could take a parameter
  object, but every caller, tests included, would change. "Tests unmodified" decides it.
- **J3. A `partial` split for S104.** `HttpKeycloakAdminClient` stays one type (one DI registration,
  one typed `HttpClient`, the same primary-constructor captures); only its source spreads across
  files grouped by what they hold. Precedent: spec 341 split endpoint files the same way.

**No bug fix is folded in.** Three oddities were found and are **pinned as-is, not changed**:

1. On the rotate branch, a non-concurrency failure of `clients.SaveAsync` (e.g. a
   `DbUpdateException`) is caught by the generic filter and answered `502 KEYCLOAK_UNAVAILABLE`,
   although Keycloak was never called. Misleading, not unsafe (no secret is rolled). If it is to
   change, that is a separate issue.
2. `KioskPrivilegeSweepRegistrationTests`' failure message cites `HttpKeycloakAdminClient.cs:363`
   for `TryDeleteClientAsync`, which is already at line 534 on `ed702aa5`. Test text; tests are not
   edited here. Follow-up candidate.
3. `ClientRow` and `ClientDetailRow` are identical private records. Not a metric hit; out of scope.

## 7. Latency budget impact (constitution §IV)

**N/A.** None of the touched members is on the event-to-overlay path: Keycloak administration,
client registration and the startup/hourly sweeps are control-plane operations. No leg of §IV is
affected.

## 8. Out of scope

Every `Api/` file, migrations, tests' own metrics, `ServiceDefaults` hits shown by the same build,
the 4 left hits, and the three oddities of §6.

## 9. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052, ADR-0054), sentence-style names (ADR-0053),
`Ensure.That` guards (ADR-0105), `Option<T>` for absences in Application (ADR-0141), `var` wherever
it compiles, collection expressions, fluent DI chains, one parameter per line when wrapping, no
leading underscores (CLAUDE.md house rules). No Testcontainers (ADR-0103).
