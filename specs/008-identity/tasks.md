# Tasks: 008 — Identity

**Input:** Design documents at `specs/008-identity/`

**Prerequisites:** [spec.md](./spec.md) (Phase 1 gate approved
2026-05-29), [plan.md](./plan.md) (Phase 2 gate approved
2026-05-29).

**Status:** Draft (Phase 3 — Tasks)

## Format: `[ID] [P?] [Story] Description`

- **[P]** — independent of any task above it in the same phase; safe to parallelise.
- **[Story]** — US1 (Admin), US2 (Operator), US3 (Kiosk), US4 (Device MQTT), US5 (Webhook rotation), US6 (Cross-fab admin), FOUND, MIG (per-endpoint scope migration), POLISH.

## Path conventions

- Backend: `src/Identity/{Domain,Application,Infrastructure,Api}/`, `src/Shared.Contracts/Identity/`, `src/MigrationRunner/`, `src/AppHost/`
- ServiceDefaults: `src/ServiceDefaults/Authorization/`
- Realm + Mosquitto: `src/AppHost/Realms/sse-realm.json`, `src/AppHost/mosquitto/`
- Per-context migration: `src/*/Api/`, `tests/*.Application.Tests/` (assertions where needed)
- Tests: `tests/Identity.{Domain,Application,Integration}.Tests/`, `tests/ServiceDefaults.Tests/`, `tests/Architecture.Tests/`, `tests/Shared.Contracts.Tests/`
- ADRs: `docs/adr/0100-mosquitto-go-auth.md`

Primitives from prior specs (`Option<T>`, `Result<T,E>`, `Ensure`, `AggregateRoot<TId>`, `IValueObject<T>`, `IEventBus`, `IClock`, `AspireFixture`, etc.) are reused — not repeated.

---

## Phase 1: Foundational — Aspire + Scope catalogue + V1 contracts + ADR-0100

- [ ] **T001 [FOUND]** Draft **ADR-0100** `docs/adr/0100-mosquitto-go-auth.md`: chosen because the bare Mosquitto `password_file + acl_file` model can't validate Keycloak JWTs. JWT mode (vs HTTP-introspect mode) chosen for sub-ms per-connect latency. Alternatives rejected: per-connect HTTP introspect (RTT to Keycloak), Mosquitto password file with cron-sync (stale-credential window).
- [ ] **T002 [P] [FOUND]** Add `identity-db` to `src/AppHost/AppHost.cs`: `var identityDb = postgres.AddDatabase("identity-db");` + wire it into `migrations`.
- [ ] **T003 [FOUND]** Wire the `identity` API project in `AppHost.cs`: `builder.AddProject<Projects.SmartSentinelEye_Identity_Api>("identity").WithHttpEndpoint().WithReference(identityDb).WithReference(rabbitmq).WithReference(keycloak).WaitForCompletion(migrations).WaitFor(rabbitmq).WaitFor(keycloak)`.
- [ ] **T004 [P] [FOUND]** `Identity.Domain.csproj` mirrors EventIngestion.Domain shape (Shared.Kernel only; no framework refs).
- [ ] **T005 [P] [FOUND]** `Identity.Application.csproj`: Domain + Shared.Kernel + Shared.CQRS + Shared.Contracts + `Microsoft.EntityFrameworkCore` (IQueryable seam) + `Microsoft.Extensions.Logging.Abstractions`.
- [ ] **T006 [P] [FOUND]** `Identity.Infrastructure.csproj`: EFCore + Npgsql + WolverineFx + `Microsoft.AspNetCore.App` framework ref + ServiceDefaults + Domain + Application.
- [ ] **T007 [P] [FOUND]** `Identity.Api.csproj`: Infrastructure + Application + ServiceDefaults + Shared.CQRS + Shared.Kernel + `Microsoft.AspNetCore.OpenApi`.
- [ ] **T008 [P] [FOUND]** Add the four `Identity.*` projects + the new `Identity.Domain.Tests` / `Identity.Application.Tests` / `ServiceDefaults.Tests` to `SmartSentinelEye.slnx`.
- [ ] **T009 [P] [FOUND]** Add `MigrationRunner` reference: `builder.AddIdentityPersistence();` in `MigrationRunner/Program.cs`.
- [ ] **T010 [P] [FOUND]** `Scope` static class in `src/ServiceDefaults/Authorization/Scope.cs` covering every scope from FR-002.
- [ ] **T011 [P] [FOUND]** `RequireScopeExtensions` in `src/ServiceDefaults/Authorization/RequireScopeExtensions.cs` — `AddScopePolicies` + `RequireScope` extension methods.
- [ ] **T012 [P] [FOUND]** `IFabAuthorizationGuard` interface + `DefaultFabAuthorizationGuard` impl + `FabAuthorizationException`.
- [ ] **T013 [FOUND]** Wire `AddScopePolicies(Scope.All)` + `services.AddScoped<IFabAuthorizationGuard, DefaultFabAuthorizationGuard>()` into `AuthenticationDefaults.AddBearerAuthentication`.
- [ ] **T014 [P] [FOUND]** `DeviceRegisteredV1`, `KioskEnrolledV1`, `WebhookIntegrationRotatedV1` in `src/Shared.Contracts/Identity/`.
- [ ] **T015 [P] [FOUND]** `tests/Shared.Contracts.Tests/Identity/*Tests.cs` — 4 tests per V1 record (positional ctor, IIntegrationEvent marker, equality, JSON round-trip).
- [ ] **T016 [P] [FOUND]** `tests/ServiceDefaults.Tests/Authorization/ScopeTests.cs` — every catalogue constant matches its dotted form.
- [ ] **T017 [P] [FOUND]** `RequireScopePolicyTests` + `DefaultFabAuthorizationGuardTests` covering the happy paths + denial paths.
- [ ] **T018 [FOUND]** Extend `tests/Architecture.Tests/BoundaryTests.cs` — positive test that `Identity.Domain` has zero framework deps + that `Identity.Application` only references `Shared.*` (no other context).

**Checkpoint:** `aspire run` brings up the `identity` project resource; ADR + V1 contracts + auth helpers merged. Coverage gates unchanged (no new gated assemblies yet).

---

## Phase 2: User Story 1 — Admin signs in and authors a rule (P1)

**Goal:** Admin's JWT carries the admin scope bundle + `/fabs/munich` group; `POST /rules` accepts.

> This story exercises the foundation laid in Phase 1 + the Phase 5 realm bootstrap. Phase 2 here is **just the domain + tests** that the rest of the spec rests on.

### Tests first

- [ ] **T019 [P] [US1]** `RegisteredClientIdentifierTests`.
- [ ] **T020 [P] [US1]** `ClientIdTests` — Keycloak's client_id grammar `^[a-zA-Z0-9][a-zA-Z0-9._-]{0,254}$`.
- [ ] **T021 [P] [US1]** `ClientKindTests` — three singletons (`Device` / `Kiosk` / `WebhookIntegration`) + `From(string)` round-trip + invalid-string rejection.
- [ ] **T022 [P] [US1]** `ClientSecretTests` — write-once VO: `ToString()` redacts, equality on hash; `Reveal()` returns the plaintext exactly once and throws on second call.
- [ ] **T023 [P] [US1]** `RegisteredClientTests` aggregate-level: `Register` factory produces a non-disabled client + raises `ClientRegisteredDomainEvent`; `Disable` is idempotent; `Rotate` only valid for `WebhookIntegration` kind.
- [ ] **T024 [P] [US1]** `RegisteredClientBuilder` fluent test helper (ADR-0054).

### Domain layer

- [ ] **T025 [P] [US1]** `RegisteredClientIdentifier` Guid v7.
- [ ] **T026 [P] [US1]** `ClientId` StringValueObject.
- [ ] **T027 [P] [US1]** `ClientKind` discriminated VO.
- [ ] **T028 [P] [US1]** `ClientSecret` write-once VO.
- [ ] **T029 [P] [US1]** `FabIdentifier` in Identity.Domain (own copy per ADR-0044; not shared across contexts).
- [ ] **T030 [P] [US1]** `ClientRegisteredDomainEvent` / `ClientDisabledDomainEvent` / `ClientRotatedDomainEvent`.
- [ ] **T031 [US1]** `RegisteredClient` aggregate root with `Register`, `Disable`, `Rotate`.
- [ ] **T032 [P] [US1]** `IRegisteredClientRepository` (`GetByClientIdAsync`, `GetByIdentifierAsync`, `Add`, `SaveAsync`).

**Checkpoint:** Domain tests ≥ 90% coverage. The actual end-to-end Admin scope path is asserted in Phase 5 (realm bootstrap) + Phase 7 (per-endpoint migration).

---

## Phase 3: User Story 3 — Kiosk binds to a physical screen (P1)

**Goal:** `POST /kiosks/enroll` creates the Keycloak client, persists the `RegisteredClient` row, returns the enrollment token.

### Tests first

- [ ] **T033 [P] [US3]** `EnrollKioskCommandHandlerTests` — happy path; idempotent on `(deviceType=Kiosk, deviceId)` returns `KioskAlreadyEnrolled`; Keycloak failure surfaces as `KeycloakUnavailable`.
- [ ] **T034 [P] [US3]** `FakeKeycloakAdminClient` + `InMemoryRegisteredClientRepository` + `FakeEventBus` + `FakeClock`.

### Application — Keycloak Admin seam

- [ ] **T035 [P] [US3]** `IKeycloakAdminClient` interface — `CreateClientAsync`, `DisableClientAsync`, `RotateClientSecretAsync`, `MintEnrollmentTokenAsync`.
- [ ] **T036 [P] [US3]** `KeycloakClientRepresentation` JSON DTO mirroring Keycloak's Admin API client shape.

### Application — Enroll kiosk command

- [ ] **T037 [P] [US3]** `EnrollKioskCommand` + `EnrollKioskErrors`.
- [ ] **T038 [US3]** `EnrollKioskCommandHandler` — creates the Keycloak client via `IKeycloakAdminClient`, persists the `RegisteredClient`, raises `KioskEnrolledV1`.
- [ ] **T039 [P] [US3]** `KioskCredentialsDto` + `EnrollKioskRequest`.

### Application — Disable kiosk command

- [ ] **T040 [P] [US3]** `DisableKioskCommand` + `DisableKioskErrors`.
- [ ] **T041 [US3]** `DisableKioskCommandHandler` — calls `IKeycloakAdminClient.DisableClientAsync` + flips the aggregate to disabled.

**Checkpoint:** Application tests for kiosk-enroll + kiosk-disable green; no real Keycloak yet.

---

## Phase 4: User Story 4 — Device MQTT (P1)

**Goal:** `POST /devices/register` creates a service-account Keycloak client; the device can mint a JWT via client_credentials and publish to MQTT.

### Tests first

- [ ] **T042 [P] [US4]** `RegisterDeviceCommandHandlerTests` — happy path; idempotent on `(deviceType, deviceId)`; Keycloak failure path.
- [ ] **T043 [P] [US4]** `DisableDeviceCommandHandlerTests`.

### Application

- [ ] **T044 [P] [US4]** `RegisterDeviceCommand` + `RegisterDeviceErrors`.
- [ ] **T045 [US4]** `RegisterDeviceCommandHandler` — creates Keycloak client with `sse.events.publish` scope + `/fabs/<fabId>` group + the device's `clientId` = `<deviceType>-<deviceId>`.
- [ ] **T046 [P] [US4]** `DisableDeviceCommand` + handler.
- [ ] **T047 [P] [US4]** `DeviceCredentialsDto` + `RegisterDeviceRequest`.

### Domain event handler — V1 fan-out

- [ ] **T048 [US4]** `ClientRegisteredDomainEventHandler` — translates to `DeviceRegisteredV1` / `KioskEnrolledV1` based on `ClientKind`.

**Checkpoint:** Application tests for both device + kiosk happy paths green.

---

## Phase 5: User Story 5 — Webhook rotation (P2)

**Goal:** `POST /webhook-integrations/{name}/rotate` creates a Keycloak client, publishes `WebhookIntegrationRotatedV1`, EventIngestion subscriber flips the bearer-validation path.

### Tests first

- [ ] **T049 [P] [US5]** `RotateWebhookClientCommandHandlerTests` — happy path; unknown integration returns `IntegrationNotFound`; Keycloak failure path.

### Application

- [ ] **T050 [P] [US5]** `RotateWebhookClientCommand` + `RotateWebhookClientErrors`.
- [ ] **T051 [US5]** `RotateWebhookClientCommandHandler` — creates a Keycloak client with `sse.events.write` scope + `/fabs/<fabId>` group; persists a `RegisteredClient` row; publishes `WebhookIntegrationRotatedV1`.

### EventIngestion subscriber

- [ ] **T052 [P] [US5]** `WebhookIntegrationRotatedV1Handler` in `EventIngestion.Application.EventHandlers` — flips the integration's `BearerValidationMode` to `Jwt` (new property on `WebhookIntegration` aggregate).
- [ ] **T053 [US5]** Modify `EventIngestion.Domain.WebhookIntegration.WebhookIntegration` to add `BearerValidationMode` (enum: `StaticHash` | `Jwt`) + a `MarkAsRotated(clientId, IClock)` factory method.
- [ ] **T054 [US5]** EF migration to add the column + default to `StaticHash` for grandfathered rows.
- [ ] **T055 [P] [US5]** Update spec 006 `EventsEndpoints.IngestWebhook`: if integration is `Jwt`, validate the bearer as a Keycloak JWT (scope `sse.events.write` + fab match); otherwise fall back to legacy hash compare.
- [ ] **T056 [P] [US5]** `EventIngestion.Application.Tests` — tests for `WebhookIntegrationRotatedV1Handler` + the dual-mode validation in the endpoint.

**Checkpoint:** Webhook rotation flow works end-to-end (Application-level); existing legacy bearers still work.

---

## Phase 6: User Story 6 — Cross-fab admin (P2)

- [ ] **T057 [P] [US6]** `DefaultFabAuthorizationGuardTests.Multi_fab_user_passes_for_each_owned_fab` + `.Multi_fab_user_fails_for_unowned_fab`.
- [ ] **T058 [P] [US6]** Doc comment update on `IFabAuthorizationGuard` covering the multi-fab case explicitly (no production code change — the guard already iterates the `groups` claim).

---

## Phase 7: Infrastructure + Identity API + Aspire wiring (PR D)

### Persistence

- [ ] **T059 [P] [FOUND]** `IdentityDbContext` + `RegisteredClientConfiguration` (table `registered_clients`).
- [ ] **T060 [P] [FOUND]** Initial EF migration via `dotnet ef migrations add InitialIdentity`.
- [ ] **T061 [P] [FOUND]** `RegisteredClientRepository` + `RegisteredClientQuerySource`.
- [ ] **T062 [P] [FOUND]** `DesignTimeDbContextFactory` + `IdentityMigrator`.
- [ ] **T063 [P] [FOUND]** `IdentityPersistenceModule` slim composition for MigrationRunner.

### Keycloak Admin client (HTTP impl)

- [ ] **T064 [P] [FOUND]** `KeycloakAdminOptions` bound from `services:keycloak:*` configuration.
- [ ] **T065 [P] [FOUND]** `KeycloakAdminTokenProvider` — caches the `identity-admin` client_credentials token; refreshes on 401.
- [ ] **T066 [FOUND]** `HttpKeycloakAdminClient` — implements `IKeycloakAdminClient` against Keycloak's `/admin/realms/{realm}/...` endpoints.

### Composition root + API

- [ ] **T067 [FOUND]** `IdentityInfrastructureModule.AddIdentityInfrastructure` registers DbContext, repos, query sources, `IKeycloakAdminClient`, command handlers, query handlers, domain-event handler, `IEventBus`, `AddWolverineForContext`.
- [ ] **T068 [P] [FOUND]** `DevicesEndpoints` — `POST /devices/register`, `DELETE /devices/{clientId}`, `GET /devices`. All gated by `sse.identity.devices.write` + fab guard.
- [ ] **T069 [P] [FOUND]** `KiosksEndpoints` — `POST /kiosks/enroll`, `DELETE /kiosks/{clientId}`, `GET /kiosks`. Gated by `sse.identity.kiosks.write`.
- [ ] **T070 [P] [FOUND]** `WebhookRotationEndpoints` — `POST /webhook-integrations/{name}/rotate`. Gated by `sse.webhooks.write`.
- [ ] **T071 [FOUND]** `IdentityApiModule.AddIdentityApi` + `Program.cs` wiring (defaults, bearer auth, infrastructure, endpoints, OpenAPI).

**Checkpoint:** `aspire run` brings up `identity`; `POST /devices/register` against a real Keycloak creates a client + returns the secret. Spec 006 webhook integrations still work on their legacy bearers.

---

## Phase 8: Keycloak realm bootstrap + mosquitto-go-auth (PR E)

### Realm bootstrap

- [ ] **T072 [FOUND]** `src/AppHost/Realms/sse-realm.json` — populate the realm `smart-sentinel-eye` with: every v1 client scope (FR-002), the four persona scope bundles, `management-web` + `kiosk-web` + `identity-admin` clients, default `/fabs/munich` group, dev seed users `admin@munich.test` + `op-3@munich.test`, group → `groups` claim mapper, audience mapper.
- [ ] **T073 [P] [FOUND]** `KeycloakAdminTokenProvider` integration test against a Testcontainers Keycloak — asserts the `identity-admin` client can mint a token.
- [ ] **T074 [P] [FOUND]** Update spec 005/006/007/etc. dev seeding documentation to mention the new realm shape.

### mosquitto-go-auth

- [ ] **T075 [FOUND]** Switch the AppHost `mosquitto` image to one bundling `mosquitto-go-auth` (or document a Dockerfile fragment in `src/AppHost/mosquitto/Dockerfile`).
- [ ] **T076 [P] [FOUND]** `src/AppHost/mosquitto/mosquitto.conf` — load the plugin; remove `allow_anonymous false` line (the plugin enforces it).
- [ ] **T077 [P] [FOUND]** `src/AppHost/mosquitto/go-auth.conf` — JWT mode, JWKS URL pointed at the dev Keycloak, 24 h cache TTL, ACL expression matching topic-fab to `groups` + `clientId` to `<source>-<deviceId>`.
- [ ] **T078 [FOUND]** Update spec 006 quickstart in README + spec 006 task T004 password-seed instructions to note the new JWT path.

**Checkpoint:** A registered device can `mosquitto_pub` with `Password=<JWT>` and the message lands in `events`.

---

## Phase 9: Per-endpoint scope migration (PR F)

Each task is one PR-F commit touching one context's API.

- [ ] **T079 [P] [MIG]** CameraCatalog endpoints: `sse.cameras.{read,write}` + fab guard.
- [ ] **T080 [P] [MIG]** StreamDistribution endpoints: `sse.streams.{read,write}` + fab guard.
- [ ] **T081 [P] [MIG]** LayoutComposition endpoints: `sse.layouts.{read,write}` + fab guard.
- [ ] **T082 [P] [MIG]** OverlayDesigner endpoints: `sse.overlays.{read,write}` + fab guard.
- [ ] **T083 [P] [MIG]** SystemVariables endpoints: `sse.variables.{read,write}` + fab guard.
- [ ] **T084 [P] [MIG]** EventIngestion endpoints: `sse.events.{read,write}` + fab guard (the webhook endpoint's dual-mode validation already lands in Phase 5).
- [ ] **T085 [P] [MIG]** Automation endpoints: `sse.rules.{read,write}` + fab guard.
- [ ] **T086 [MIG]** Mark `AuthenticationDefaults.AdminPolicy` `[Obsolete("Use sse.* scope policies instead; will be removed in spec 009.")]`.

---

## Phase 10: Polish — coverage, latency, README, architecture

- [ ] **T087 [POLISH]** `NFR001_JwtValidationLatencyTests` — Testcontainers Keycloak; warm 1 000 iterations; asserts p99 ≤ 500 µs.
- [ ] **T088 [POLISH]** `NFR002_MqttConnectAuthTests` — Testcontainers Mosquitto + Keycloak; warm 100 connect cycles; asserts p99 ≤ 5 ms.
- [ ] **T089 [POLISH]** Extend `scripts/coverage-check.ps1` with `Identity.Domain >= 90` and `Identity.Application >= 80`.
- [ ] **T090 [POLISH]** Update `tests/Architecture.Tests/BoundaryTests.cs` to assert `Identity.Domain` has zero framework deps + that the `Scope` catalogue lives in `ServiceDefaults` (not in any context's Domain).
- [ ] **T091 [P] [POLISH]** README "Bind a kiosk, register a device, author a scoped rule" quickstart section.
- [ ] **T092 [P] [POLISH]** Add an OpenAPI summary block on every endpoint annotated with its required scope (helps the auto-generated client).

---

## Dependencies between phases

```
Phase 1 (Foundational + Scope catalogue + V1 contracts + ADRs)
   │
   ▼
Phase 2 (US1: Domain) ─── Phase 6 (US6: cross-fab tests, can run anytime)
   │
   ▼
Phase 3 (US3: kiosk enroll Application)
Phase 4 (US4: device register Application)
Phase 5 (US5: webhook rotation Application + EventIngestion subscriber)
   │
   ▼
Phase 7 (Infrastructure + Identity API)
   │
   ▼
Phase 8 (Realm bootstrap + mosquitto-go-auth)
   │
   ▼
Phase 9 (Per-endpoint scope migration across all prior specs)
   │
   ▼
Phase 10 (Polish + NFR-001/NFR-002 + README)
```

## Estimation

- 92 atomic tasks. **[P] = 64** (≈ 70%) parallelizable within their phase.
- Target PR cadence (per plan): 6 PRs (A–F) mapping roughly to Phases 1 / 2 / 3-5 / 7 / 8 / 9-10.
- Walking-skeleton-style critical path: T001 → T031 → T038 → T045 → T067 → T071 → T072 → T077 (register a device → mint JWT → MQTT publish).
