# Spec 369: The bearer authentication composition under thirty lines

**Feature Branch**: `refactor/2952-servicedefaults-bearer-authentication` (cut from `origin/develop` at `128df804`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Four judgement calls
are marked (§6).

**Input**: Issue [#2952](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2952),
split out of #2932 by spec 367 §8 B. Sibling of spec 368 (#2951, PR #2956), which takes §8 A.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving, so Phase 4a is **characterisation, observed
green** (constitution §Testing). Engineer: **backend-engineer**. **Security-sensitive**: this is
the bearer pipeline every one of the nine APIs runs (`src/*/Api/Program.cs:8-10`), and a mistake
here **fails open silently** (a revocation hook that is no longer chained, a source a test double
no longer overrides, a lost-update reported as a name collision). Phase 6 runs `/security-review`
as well as `/code-review`. Not a bug fix: §6 J4 records two oddities that are pinned, not endorsed,
and neither is a defect.

**Spec number.** 369. Checked 2026-10-15 after `git fetch`: `origin/develop` highest 367; across
every remote head (`git ls-tree` over `refs/remotes/origin/*`) highest 368, held by
`origin/refactor/2951-servicedefaults-parsing-and-store` (PR #2956); no local branch holds 369+.
Re-check before the PR is opened.

**ADRs.** ADR-0084 (S138 <= 30 lines, advisory), ADR-0144 (autonomous lane, phase 4a colours, no
gate weakened), ADR-0160 (revocation checked at request time: the hook, registry, source, refresher
and `revocation-snapshot` check), ADR-0143 (retry only idempotent methods; the token client's
stated opt-in), ADR-0154 (readiness is liveness: the health check's status and tags), ADR-0113
(the concurrency handler's precedence over the unique-constraint one), ADR-0159 (one realm per
deployment), ADR-0050 (`[LoggerMessage]`), ADR-0105 (`Ensure.That`), ADR-0036 (smallest change),
ADR-0052/ADR-0103 (xUnit + Shouldly + Moq; no Testcontainers). Initial decisions rows 007-008
(Keycloak OIDC). **No new ADR**: this changes shape, not a decision.

**Prior art reused.** The empty-builder harness of `BearerAudienceTests` / `MetadataTransportTests`
(`Host.CreateEmptyApplicationBuilder`, `ConnectionStrings:keycloak` set in memory, environment
self-check); spec 347/348's ordered descriptor render
(`tests/Architecture.Tests/Persistence/AutomationRegistrationCharacterisationTests.cs`:
`"{Lifetime} {Service} -> {Implementation}"`, generic names unrolled, `instance of`, `factory`);
`IdempotentRetryTests`' `CountingHandler` and its production-ordered resilience defaults;
`CapturingLogger<T>` in `tests/ServiceDefaults.Tests/Fakes`.

---

## 1. What was measured (premise verified, not assumed)

```sh
dotnet build src/ServiceDefaults -c Release --no-incremental
```

Result on `128df804`: **both of the issue's S138 hits are present, same methods, same counts.**

| # | File:line | Method | Lines | This spec |
|---|---|---|---|---|
| 1 | `AuthenticationDefaults.cs:29` | `AddBearerAuthentication` | 46 | split (G1) |
| 2 | `AuthenticationDefaults.cs:183` | `AddRevocationCheck` | 32 | split (G2) |

The whole `src/ServiceDefaults` S138/S107 picture on `128df804`: 7 S138 (the two above; the four of
spec 368: `TryReadUpsertPrecondition` 31, `IdempotencyHeaders.TryRead` 31,
`OutboxBacklogHealthCheck.CheckHealthAsync` 41, `IdempotencyStore.BeginAsync` 61;
`WolverineDefaults.AddWolverineForContext` 56) and 4 S107 (`ClientCredentialsTokenProvider` ctor 5,
`FabResolution` 5, `IdempotencyScope.For` 5, `AddWolverineForContext` 9). **Out of scope and must
not change**: all of them except rows 1-2, plus `RequiresHttpsMetadata`, `AddMetadataTransportCheck`,
`RefuseIfRevoked`, `IssuedAtOf`, `KioskClientId`, `ApiAudience`.

After this spec: 5 S138 on `128df804`'s base; **1** (`AddWolverineForContext`) once spec 368 has
also merged. 4 S107 either way.

**Issue's coverage claims, checked.** The named tests exist: `BearerAudienceTests` (3),
`MetadataTransportTests` (4 members, theories), `BearerRevocationHookTests` (6),
`RegisteredPolicyTests` (2), `RevocationListOptionsValidationTests` (2),
`RevokedClientRefresherWiringTests` (1), `DefaultEndpointsTests`. A search of `tests/` confirms the
gaps: nothing asserts the `services:…` fallback keys, the exception's exact message (only
`ComposedHostCaptureTests` checks `ShouldContain("Keycloak base URL not found")`), `Authority`,
`MapInboundClaims`, the exception-handler order (`StatusProducerDeclarationTests` is a census of
the five types, not their order), `TryAdd` precedence beyond the registry stub, either named
`HttpClient`, the `revocation-snapshot` registration, the `client revoked` failure message or the
refusal log's category.

## 2. User story

### US1 (P1): The bearer composition fits ADR-0084's 30-line limit with no observable change

Nine services authenticate through these two methods. A maintainer reading them should meet one
concern per method, and nothing a caller, a token, the container or an orchestrator can observe may
move.

**Independent test.** Run §4's characterisation on `128df804` (green), apply each refactor commit,
re-run byte-unmodified (green); the §1 build no longer lists rows 1-2 and lists nothing new.

**Acceptance scenarios**

```gherkin
Scenario: Happy - each method splits and its characterisation stays green unmodified
  Given §4's tests observed green on 128df804
  When a refactor commit of plan §3 lands
  Then the same test files, byte-unmodified, pass in Release
  And the §1 build no longer reports that method under S138, and reports no new ADR-0084 warning

Scenario: Conflict - a characterisation needs an edit after a refactor
  Given a refactor commit has landed
  When any §4 assertion fails
  Then the behaviour moved: the refactor is reverted or fixed, and the assertion is not touched

Scenario: Bad request - a host with no Keycloak address refuses to compose
  Given a builder with none of ConnectionStrings:keycloak, services:keycloak:http:0, services:keycloak:https:0
  When AddBearerAuthentication runs
  Then it throws InvalidOperationException with exactly today's message
  And nothing has been added to the service collection

Scenario: Auth - a revoked client is still refused, and a test double still wins
  Given a registry, a source or a time provider registered before AddBearerAuthentication
  When the container resolves them
  Then the pre-registered instances win over the defaults
  And a token whose client the registry refuses fails with "client revoked", chained after any
    OnTokenValidated a caller set first, logged at Debug under today's category
  And the concurrency exception handler is still registered before the unique-constraint one
```

## 3. Functional requirements

- **FR-001** `AddBearerAuthentication` and `AddRevocationCheck` each <= 30 lines (S138) after the
  change; every new helper <= 30 lines and <= 4 parameters; no other ADR-0084 rule (S104, S107,
  S134, S1541, S3776) reports anything new in `src/ServiceDefaults`.
- **FR-002** Public surface of `AuthenticationDefaults` unchanged (`AddBearerAuthentication`'s
  signature and defaults, `RequiresHttpsMetadata`, `KioskClientId`, `ApiAudience`). New members are
  `private static` in the same class; no new type, no new file in `src/`.
- **FR-003** The Keycloak base URL is resolved **before any registration**, with the same lookup
  order (`GetConnectionString(name)` → `services:{name}:http:0` → `services:{name}:https:0`), the
  same null-only fallthrough (`??`), and the byte-identical `InvalidOperationException` message. The
  authority stays `{baseUrl.TrimEnd('/')}/realms/{realm}`; `RevocationListOptions.KeycloakUrl` still
  receives the **untrimmed** base URL.
- **FR-004** Every descriptor the call adds is added in the same order with the same lifetime,
  service type and implementation kind (§4.3). In particular the five `IExceptionHandler`s stay
  `BadHttpRequest`, `FabAuthorization`, `UnattributableOperator`, `ConcurrencyConflict`,
  `UniqueConstraint`, and every `TryAdd` stays a `TryAdd`.
- **FR-005** The resolved `JwtBearerOptions` (`Authority`, `RequireHttpsMetadata`,
  `TokenValidationParameters.ValidAudiences`, `Audience` left unset, `MapInboundClaims`) and the
  default authentication scheme are unchanged. The revocation hook is still a DI-aware
  `Configure<IRevokedClientRegistry, IServiceProvider>` on the `Bearer` named options, still chains
  the previous `OnTokenValidated` first, and still creates its logger from an optional
  `ILoggerFactory` under the category string `SmartSentinelEye.ServiceDefaults.AuthenticationDefaults`.
- **FR-006** `RevocationListOptions`: same bind of `RevocationList`, then the same two overrides
  (`KeycloakUrl`, `Realm`), the same `Validate<IServiceProvider>` predicate and byte-identical
  message, still `ValidateOnStart`.
- **FR-007** The named clients `revocation-list-token` (retry every method, ADR-0143) and
  `revocation-list-fetch` (base address `http://identity`, `RevocationListAuthorizationHandler`,
  no retry opt-in) are unchanged; the `#pragma warning disable/restore S1075, S5332` pair and every
  comment move **verbatim** with the code they explain; the number of `#pragma warning disable`
  lines in the file is unchanged.
- **FR-008** The `revocation-snapshot` health check keeps its name, `HealthStatus.Degraded` failure
  status, tags exactly `["ready"]`, and its `RevokedClientSnapshotHealthCheck` factory; the hosted
  `RevokedClientRefresher` is still the singleton instance.
- **FR-009** No change to `AddMetadataTransportCheck`, `RequiresHttpsMetadata`, `RefuseIfRevoked`,
  `IssuedAtOf`, the constants, or any file outside `AuthenticationDefaults.cs`.

## 4. Characterisation (phase 4a, observed green on `128df804` before any production edit)

All in `tests/ServiceDefaults.Tests` (runs in CI's `coverage-check.ps1` unit pass; no shard filter
applies there). No existing test file is edited. Every builder is
`Host.CreateEmptyApplicationBuilder` with in-memory configuration, so no `appsettings.json` or
ambient variable can supply a value. **Expected values are hand-written literals in the test**
(`"revocation-list-fetch"`, `"http://identity/"`, `"revocation-snapshot"`, the exception and
validation messages, the logger category), never read from the production constants: an assertion
must not check its own input.

### 4.1 The Keycloak address (gap: the fallback chain and the message)

- Each key alone resolves: connection string; `services:keycloak:http:0`; `services:keycloak:https:0`
  → `Authority` = that value + `/realms/smart-sentinel-eye`.
- Precedence: connection string beats both `services:` keys; `http:0` beats `https:0`.
- A trailing slash is trimmed once for `Authority` (`https://kc.invalid/` →
  `https://kc.invalid/realms/smart-sentinel-eye`).
- Custom arguments: `AddBearerAuthentication("kc", "other-realm")` reads `ConnectionStrings:kc` and
  yields `…/realms/other-realm`; the default resource name does not read `ConnectionStrings:kc`.
- None of the three → `InvalidOperationException` whose `Message` is exactly
  `"Keycloak base URL not found. Looked for ConnectionStrings:keycloak, services:keycloak:http:0, services:keycloak:https:0."`
  (and the `kc` variant), thrown by the call itself, with `builder.Services.Count` unchanged.
- An **empty** connection string is taken as found (no fallthrough): `Authority` =
  `/realms/smart-sentinel-eye` (J4).
- Whitespace resource name or realm → the `Ensure` guard throws (pin the exception type only).

### 4.2 The resolved bearer options and the hook's observable edges (gap)

From `IOptionsMonitor<JwtBearerOptions>.Get("Bearer")`: `Authority`; `RequireHttpsMetadata`
false under `Development` (true elsewhere is `MetadataTransportTests`'); `ValidAudiences` exactly
`["smart-sentinel-eye-api"]`; `Audience` null; `MapInboundClaims` false; `Events.OnTokenValidated`
not null. `IOptions<AuthenticationOptions>.Value.DefaultScheme == "Bearer"`, and the `Bearer`
scheme's handler type is `JwtBearerHandler`.

Driving the hook (the `BearerRevocationHookTests` technique: a pre-registered registry that refuses
the token's `azp`): `context.Result.Failure.Message == "client revoked"`; with an `ILoggerFactory`
registered whose provider captures records, exactly one record at `Debug` under category
`SmartSentinelEye.ServiceDefaults.AuthenticationDefaults` with today's rendered message; an admitted
token produces no record.

### 4.3 Registration snapshot (gap: order is observable and nothing pins it)

`BearerAuthenticationRegistrationCharacterisationTests`: capture `builder.Services` before and after
`AddBearerAuthentication()`, take the **slice the call added**, keep the pinned families, render
each as spec 348 does, and compare to a hand-written ordered array copied once from the green run.
Pinned families: a service or implementation type from a `SmartSentinelEye.*` assembly (recursively
through generic arguments); service type `TimeProvider`, `IHostedService`, `IExceptionHandler`; and
`IConfigureOptions<>`, `IPostConfigureOptions<>`, `IValidateOptions<>` closed over
`JwtBearerOptions`, `RevocationListOptions`, `AuthenticationOptions`, `AuthorizationOptions`,
`HttpClientFactoryOptions`, `HttpStandardResilienceOptions`, `HealthCheckServiceOptions`,
`StartupValidatorOptions`. No total count.

Plus one dedicated, separately named assertion: the `IExceptionHandler` implementation types in
registration order are exactly the five of FR-004 (the load-bearing order, readable without the
snapshot).

### 4.4 The revocation composition (gap)

- **`TryAdd` precedence**: each of `TimeProvider` (the double `ClientCredentialsTokenProviderTests` already uses), `RevokedClientRegistry`,
  `IRevokedClientRegistry`, `IRevokedClientSource`, `RevocationListTokenProvider`,
  `RevokedClientRefresher` pre-registered → the pre-registered instance resolves. Without
  pre-registration, `IRevokedClientRegistry` resolves to the same instance as `RevokedClientRegistry`,
  and `IRevokedClientSource` is `HttpRevokedClientSource`.
- **Options**: with `RevocationList:KeycloakUrl`, `:Realm`, `:ClientId`, `:ClientSecret` in
  configuration and connection string `https://kc.invalid/`: `KeycloakUrl == "https://kc.invalid/"`
  (configuration overridden, **not trimmed**), `Realm == "smart-sentinel-eye"` (overridden),
  `ClientId`/`ClientSecret` bound. Missing secret with the HTTP source → `OptionsValidationException`
  whose failure message is today's text exactly.
- **Named clients** (primary handlers replaced by counting stubs after the call; resilience defaults
  configured as `Extensions.AddServiceDefaults` does, before the call, with zero delay):
  `revocation-list-fetch` has `BaseAddress == http://identity/`; a GET through it carries
  `Authorization: Bearer <token>` minted through `revocation-list-token` by a stubbed token response;
  a 503 `POST` through `revocation-list-token` is attempted 4 times; a 503 `POST` through
  `revocation-list-fetch` is attempted once (it has no opt-in).
- **Health check**: `IOptions<HealthCheckServiceOptions>.Value.Registrations` holds exactly one
  `revocation-snapshot`, `FailureStatus == Degraded`, `Tags` exactly `{"ready"}`, factory output a
  `RevokedClientSnapshotHealthCheck`.

### 4.5 Existing coverage (stays green, unedited; the second observer)

`BearerAudienceTests`, `MetadataTransportTests`, `BearerRevocationHookTests`,
`RegisteredPolicyTests`, `RevocationListOptionsValidationTests`, `RevokedClientRefresherWiringTests`,
`DefaultEndpointsTests`; `tests/Architecture.Tests` (`ComposedHostCaptureTests`,
`StatusProducerDeclarationTests`, the spec 348 registration snapshots); and the CI integration
shards, which authenticate real Keycloak tokens through all nine services.

## 5. Success criteria

- **SC-001** §1 build: rows 1-2 gone; 5 S138 on this base (1 if spec 368 merged first), 4 S107, no
  new warning of any ADR-0084 rule.
- **SC-002** §4.1-§4.4 observed green on `128df804` (output quoted in the PR), green and
  byte-unmodified after each refactor commit.
- **SC-003** Counterfactuals (plan §5) observed red, then reverted, quoted in the PR.
- **SC-004** `tests/ServiceDefaults.Tests` and `tests/Architecture.Tests` green; CI integration
  shards green.
- **SC-005** `git diff --stat origin/develop` touches only `src/ServiceDefaults/AuthenticationDefaults.cs`,
  the new test files, and this spec directory.

## 6. Judgement calls (marked)

- **J1: two commit groups, one PR.** G1 splits `AddBearerAuthentication`, G2 `AddRevocationCheck`.
  The characterisation (§4.1-§4.4) is all written and observed green before either. One PR, as the
  issue asks: same file, same review.
- **J2: the snapshot includes some framework descriptors** (`IConfigureOptions<…>` of the pinned
  option types). That trades package-bump stability for catching a reordered `Configure`, which is
  exactly where a fail-open would hide. Only the slice the call added is rendered, so unrelated
  framework growth does not move it. If two consecutive runs on the same SHA render differently, the
  unstable family is dropped and recorded in the PR body.
- **J3: the hook stays inside `AuthenticationDefaults`.** Moving it to a new type would change the
  logger category unless the literal is carried over, and adds a file the issue did not ask for. The
  extraction is a private static method in the same class.
- **J4: two oddities are pinned, not endorsed, and neither is a bug fix.** An empty connection string
  is taken as an address (FR-003); it fails closed at startup outside Development because
  `JwtBearerPostConfigureOptions` refuses a non-https authority under `RequireHttpsMetadata`.
  `RevocationListOptions.KeycloakUrl` is untrimmed; `ClientCredentialsTokenProvider` trims it when it
  builds the token URL (`ClientCredentialsTokenProvider.cs:139`). Changing either is a separate issue.

## 7. Latency budget impact (constitution §IV)

**N/A.** Composition runs once at startup. Token validation happens per request, but no leg of the
event → overlay path is a bearer-authenticated request in this change's reach, and the hook's
per-request work (one registry lookup) is untouched. The refactor adds no round-trip and no
allocation beyond a method call at startup.

## 8. Locked tech choices

.NET 10, ASP.NET Core JWT bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`), Keycloak OIDC
(initial decisions rows 007-008, ADR-0159), `Microsoft.Extensions.Http.Resilience` with the
ADR-0143 opt-in, health checks (ADR-0154), `Ensure.That` (ADR-0105), `[LoggerMessage]`
(ADR-0050), xUnit + Shouldly + Moq (ADR-0052), no Testcontainers (ADR-0103).
