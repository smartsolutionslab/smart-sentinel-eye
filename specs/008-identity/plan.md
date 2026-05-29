# Implementation Plan: 008 — Identity

**Branch:** `008-identity` | **Date:** 2026-05-29 | **Spec:** [spec.md](./spec.md)

**Status:** Draft (Phase 2 — Plan)

**Input:** Feature specification from `specs/008-identity/spec.md`
(Phase 1, ten Q&A clarifications resolved across two rounds, zero
`[NEEDS CLARIFICATION]` markers). Phase-1 gate approved 2026-05-29.

## Summary

Lights up the **Identity** bounded context — one shared Keycloak
realm with resource-shaped scopes gating every endpoint, four
personas (admin, operator, kiosk, device), `groups` claim
carrying fab membership, device JWTs validated by Mosquitto via
`mosquitto-go-auth`, and an admin REST surface for runtime
device/kiosk client provisioning.

- **Keycloak realm `smart-sentinel-eye`** bootstrapped via
  `src/AppHost/Realms/sse-realm.json`: 19 v1 scopes (FR-002),
  scope bundles per persona, `management-web` + `kiosk-web`
  OIDC clients, `identity-admin` service-account client, and the
  seed `/fabs/munich` group.
- **`ServiceDefaults` upgrades:**
  - `Scope` static class — single source of truth for scope
    names (`Scope.SSE.Rules.Write`, etc.). Used at every endpoint
    `.RequireAuthorization(...)` call.
  - `RequireScope(string)` policy extension wrapping
    Keycloak's space-separated `scope` claim.
  - `IFabAuthorizationGuard` — central group-claim check
    (`groups` contains `/fabs/<fabId>`); throws
    `FabAuthorizationException` (mapped to 403 globally).
  - The existing `AdminPolicy` stays configured but is annotated
    `[Obsolete]`; per-endpoint scope checks layer on top.
- **Identity context (new):** single `RegisteredClient` aggregate
  with a `ClientKind` discriminator (`Device` | `Kiosk` |
  `WebhookIntegration`). The aggregate is the local audit/dedup
  record; Keycloak remains the system of record.
- **Identity API:**
  - `POST /devices/register`, `DELETE /devices/{clientId}`,
    `GET /devices`.
  - `POST /kiosks/enroll`, `DELETE /kiosks/{clientId}`,
    `GET /kiosks`.
  - `POST /webhook-integrations/{name}/rotate` (publishes
    `WebhookIntegrationRotatedV1` consumed by EventIngestion to
    flip its bearer-validation path from static-hash to JWT).
- **Mosquitto auth:** swap the bare auth-and-acl-file model for
  `mosquitto-go-auth` in **JWT mode** with a 24 h JWKS cache.
  Devices connect with `Username=<clientId>`, `Password=<JWT>`;
  the ACL is computed from the JWT claims
  (`groups` ∩ topic-fab, `clientId` ends with
  `<source>-<deviceId>`, scope `sse.events.publish`).
- **Per-endpoint scope migration** across **all eight prior
  specs**. Every `.RequireAuthorization(AdminPolicy)` call site
  becomes `.RequireAuthorization(Scope.SSE.<Resource>.<Verb>)` +
  `fabGuard.EnsureAccessAsync(user, fabId)` on fab-scoped
  endpoints. Migration is mechanical but spans ~30 endpoints.

## Technical Context

| Concern | Decision | Source |
|---|---|---|
| Backend language | C# / .NET 10 | ADR-0001, ADR-0024 |
| Persistence | EF Core on Postgres (per-context DB `identity-db`) | ADR-0009 |
| Messaging | RabbitMQ via Wolverine; per-module queue isolation; Postgres outbox | ADR-0088 |
| Realm bootstrap | Single shared realm via `WithRealmImport(Realms/sse-realm.json)` | spec FR-001 |
| Scope taxonomy | Resource-shaped `sse.<resource>.<verb>` | spec FR-002 |
| Tenant claim | Keycloak group `/fabs/<fabId>` → standard `groups` claim | spec FR-003 |
| Mosquitto auth | **`mosquitto-go-auth` plugin** in JWT mode against cached JWKS | spec FR-007 + **ADR-0100 (this plan)** |
| Token TTLs | human 15 min / kiosk 15 min / device 24 h; refresh 8 h human / 7 d kiosk / 30 d device | spec FR-005 |
| Keycloak Admin client | Hand-rolled `HttpClient` + JSON DTOs (no external SDK) | locked in Q&A 2 |
| API style | Minimal APIs only | ADR-0070 |
| Errors | `Result<T, ApiError>` with sealed-record error hierarchies | ADR-0047, ADR-0089 |
| Performance | JWT validation ≤ 500 µs p99; MQTT connect-time auth ≤ 5 ms p99 | spec NFR-001/NFR-002 |
| Scale | 1 000 device clients / 50 kiosks / 500 humans per realm | spec NFR-005 |

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| §I On-prem first | Keycloak + Mosquitto run per-environment on the fab host. | ✅ |
| §II DDD + VOs | `ClientId`, `ClientKind`, `ClientSecret` (write-once VO that scrubs in `ToString`), `RegisteredClientIdentifier`. Maximalist. | ✅ |
| §III Bounded-context isolation | All new code in `SmartSentinelEye.Identity.*`. The `Scope` constants + `IFabAuthorizationGuard` live in `ServiceDefaults` (shared infra, ADR-0051). **No new `AllowedCrossContext` entries.** | ✅ |
| §IV Latency budget | Cached JWKS keeps per-request auth ≤ 500 µs (well under any context budget). MQTT auth runs **per-connect, not per-message** — spec 006's 50 ms ingest budget is preserved. | ✅ |
| §V Spec-driven | Spec gate approved 2026-05-29. This plan. Tasks follow. | ✅ |
| §VI Aspire composition root | New AppHost resources: `identity` (project) + `identity-db` (database). Mosquitto picks up the plugin via bind-mounted config (same pattern as today). | ✅ |
| §VII No event sourcing without justification | `RegisteredClient` is CRUD; Keycloak is the system of record for the credentials themselves. | ✅ |
| §VIII Safe at trust boundaries | JWT validation + scope check + fab guard at every endpoint, all in `ServiceDefaults` middleware (no per-context duplication). | ✅ |
| §IX Forward-compat | `Scope` catalogue is additive. New scopes don't break callers; missing scopes return typed 403. | ✅ |

**Result:** No violations. No Complexity Tracking entries.

**Tech-stack additions requiring ADR before Phase 4:**
- **ADR-0100** — `mosquitto-go-auth` as the Mosquitto auth
  plugin (drafted in PR A).

## Project Structure

### Documentation

```
specs/008-identity/
├── spec.md          ← Phase 1 (approved 2026-05-29)
├── plan.md          ← this file (Phase 2)
└── tasks.md         ← Phase 3 (next; created by /speckit-tasks)
```

### Source code — files added / modified

```
src/Identity/Domain/                                ← scaffold exists; populated here
└── RegisteredClient/
    ├── RegisteredClient.cs                         ← aggregate root
    ├── RegisteredClientIdentifier.cs               ← Guid v7
    ├── ClientId.cs                                 ← StringVO; matches Keycloak's client_id grammar
    ├── ClientSecret.cs                             ← write-once VO; ToString() redacts; equality on hash
    ├── ClientKind.cs                               ← discriminated VO (Device | Kiosk | WebhookIntegration)
    ├── FabIdentifier.cs                            ← StringVO; mirrors EventIngestion shape (we don't share VOs across contexts per ADR-0044)
    ├── IRegisteredClientRepository.cs
    └── Events/
        ├── ClientRegisteredDomainEvent.cs
        ├── ClientDisabledDomainEvent.cs
        └── ClientRotatedDomainEvent.cs

src/Identity/Application/
├── Commands/
│   ├── RegisterDeviceCommand.cs                    ← + RegisterDeviceErrors.cs
│   ├── DisableDeviceCommand.cs                     ← + DisableDeviceErrors.cs
│   ├── EnrollKioskCommand.cs                       ← + EnrollKioskErrors.cs
│   ├── DisableKioskCommand.cs                      ← + DisableKioskErrors.cs
│   ├── RotateWebhookClientCommand.cs               ← + RotateWebhookClientErrors.cs
│   └── Handlers/
│       └── …
├── Queries/
│   ├── ListDevicesQuery.cs / ListKiosksQuery.cs
│   ├── IRegisteredClientQuerySource.cs
│   └── Handlers/
│       └── …
├── DTOs/
│   ├── RegisteredClientDto.cs
│   ├── DeviceCredentialsDto.cs
│   └── KioskCredentialsDto.cs
├── EventHandlers/
│   └── ClientRegisteredDomainEventHandler.cs        ← publishes DeviceRegisteredV1 / KioskEnrolledV1
└── KeycloakAdmin/
    ├── IKeycloakAdminClient.cs                      ← seam for unit tests
    ├── KeycloakClientRepresentation.cs              ← JSON DTOs mirroring Keycloak's Admin API
    └── KeycloakAdminScopes.cs                       ← static catalogue used by the admin client

src/Identity/Infrastructure/
├── Persistence/
│   ├── IdentityDbContext.cs                         ← DbSet<RegisteredClient>
│   ├── Configurations/RegisteredClientConfiguration.cs
│   ├── RegisteredClientRepository.cs
│   ├── RegisteredClientQuerySource.cs
│   ├── DesignTimeDbContextFactory.cs
│   ├── IdentityMigrator.cs
│   └── Migrations/2026xxxx_InitialIdentitySchema.cs
├── KeycloakAdmin/
│   ├── HttpKeycloakAdminClient.cs                   ← IKeycloakAdminClient impl; hand-rolled HttpClient
│   ├── KeycloakAdminOptions.cs                      ← bound from configuration
│   └── KeycloakAdminTokenProvider.cs                ← caches the identity-admin client_credentials token
└── IdentityInfrastructureModule.cs

src/Identity/Api/
├── DevicesEndpoints.cs                              ← /devices register / list / disable
├── KiosksEndpoints.cs                               ← /kiosks enroll / list / disable
├── WebhookRotationEndpoints.cs                      ← /webhook-integrations/{name}/rotate
├── Requests/                                        ← per-endpoint request records
└── IdentityApiModule.cs

src/ServiceDefaults/
├── Authorization/
│   ├── Scope.cs                                     ← static catalogue (Scope.SSE.Rules.Write, etc.)
│   ├── RequireScopeExtensions.cs                    ← AuthorizationOptions + IEndpointConventionBuilder helpers
│   └── IFabAuthorizationGuard.cs                    ← + DefaultFabAuthorizationGuard impl
└── AuthenticationDefaults.cs                        ← MODIFIED — adds scope policies for the v1 catalogue; AdminPolicy gets [Obsolete]

src/AppHost/AppHost.cs                               ← adds identity project + identity-db
src/AppHost/Realms/sse-realm.json                    ← MODIFIED — scopes, scope bundles, default groups, OIDC clients
src/AppHost/mosquitto/mosquitto.conf                 ← MODIFIED — load mosquitto-go-auth, point at Keycloak JWKS
src/AppHost/mosquitto/go-auth.conf                   ← NEW — plugin configuration (JWT mode, JWKS URL, cache TTL)

src/Shared.Contracts/Identity/
├── DeviceRegisteredV1.cs
├── KioskEnrolledV1.cs
└── WebhookIntegrationRotatedV1.cs

src/EventIngestion/Application/EventHandlers/
└── WebhookIntegrationRotatedV1Handler.cs            ← NEW — flips the integration's bearer-validation path

src/CameraCatalog/Api/, src/StreamDistribution/Api/, src/LayoutComposition/Api/,
src/OverlayDesigner/Api/, src/SystemVariables/Api/, src/EventIngestion/Api/,
src/Automation/Api/                                  ← MODIFIED — every endpoint swaps AdminPolicy for the matching Scope constant + IFabAuthorizationGuard call

tests/Identity.Domain.Tests/                         ← new test project
tests/Identity.Application.Tests/                    ← new test project; includes a FakeKeycloakAdminClient
tests/Shared.Contracts.Tests/                        ← MODIFIED — adds tests for the three new V1 records
tests/ServiceDefaults.Tests/                         ← new test project for Scope catalogue + IFabAuthorizationGuard

docs/adr/
└── 0100-mosquitto-go-auth.md                        ← NEW
```

## Domain Model

### RegisteredClient (aggregate root)

```csharp
public sealed class RegisteredClient : AggregateRoot<RegisteredClientIdentifier>
{
    public ClientId ClientId { get; private set; }
    public ClientKind Kind { get; private set; }
    public FabIdentifier Fab { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
    public OperatorIdentifier RegisteredBy { get; private set; }
    public DateTimeOffset? DisabledAt { get; private set; }

    public static RegisteredClient Register(...);  // raises ClientRegisteredDomainEvent
    public void Disable(IClock clock);              // idempotent on disabled
    public void Rotate(IClock clock);               // only valid for ClientKind.WebhookIntegration
}
```

`ClientSecret` lives outside the aggregate — it's returned ONCE
from the `Register` factory (and from `Rotate`) and never
persisted by Identity. Keycloak is the system of record.

### ClientKind (discriminated VO)

```csharp
public sealed record ClientKind(string Value) : IValueObject<string>
{
    public static ClientKind Device { get; } = new("Device");
    public static ClientKind Kiosk { get; } = new("Kiosk");
    public static ClientKind WebhookIntegration { get; } = new("WebhookIntegration");
}
```

## ServiceDefaults additions

### `Scope` catalogue

```csharp
public static class Scope
{
    public static class SSE
    {
        public static class Cameras { public const string Read = "sse.cameras.read"; public const string Write = "sse.cameras.write"; }
        public static class Rules   { public const string Read = "sse.rules.read";   public const string Write = "sse.rules.write";   }
        // …one nested class per resource
        public static class Events
        {
            public const string Read    = "sse.events.read";
            public const string Write   = "sse.events.write";
            public const string Publish = "sse.events.publish";
        }
        public static class Identity
        {
            public static class Devices { public const string Write = "sse.identity.devices.write"; }
            public static class Kiosks  { public const string Write = "sse.identity.kiosks.write";  }
        }
    }
}
```

### `RequireScope` policy extension

```csharp
public static class RequireScopeExtensions
{
    public static AuthorizationBuilder AddScopePolicies(
        this AuthorizationBuilder builder, IEnumerable<string> scopes);

    public static RouteHandlerBuilder RequireScope(
        this RouteHandlerBuilder builder, string scope);
}
```

Each scope is registered as a policy whose `RequireAssertion`
splits the `scope` claim and looks for the target string. Same
mechanic as today's `AdminPolicy` (FR-018) but per-scope.

### `IFabAuthorizationGuard`

```csharp
public interface IFabAuthorizationGuard
{
    Task EnsureAccessAsync(ClaimsPrincipal user, string fabId, CancellationToken cancellationToken);
}

public sealed class DefaultFabAuthorizationGuard : IFabAuthorizationGuard
{
    // Reads the `groups` claim; checks for `/fabs/<fabId>`;
    // throws FabAuthorizationException on miss (mapped 403).
}
```

A single global exception handler in `ServiceDefaults.MapDefaultEndpoints()`
maps `FabAuthorizationException` to a 403 + `RESOURCE_FAB_NOT_AUTHORIZED`
error envelope.

## Keycloak realm bootstrap

`src/AppHost/Realms/sse-realm.json` (currently empty / minimal)
gets:

- Realm `smart-sentinel-eye`.
- **Client scopes:** all 19 v1 scopes from spec FR-002.
- **Client-scope bundles** (default optional scopes per persona):
  `admin-bundle`, `operator-bundle`, `kiosk-bundle`, `device-bundle`.
- **OIDC clients:**
  - `management-web` (public, PKCE, redirect URIs).
  - `kiosk-web` (public, PKCE).
  - `identity-admin` (confidential, service-account, scopes:
    `realm-admin` only — for Keycloak Admin API access).
- **Default groups:** `/fabs/munich` (additional fab groups
  created at provisioning time via Keycloak Admin API).
- **Default users (dev only):**
  `admin@munich.test` / `password` in `/fabs/munich` with the
  admin bundle; `op-3@munich.test` similarly with operator
  bundle.
- **Group → scope mapper:** `groups` claim populated from group
  membership (Keycloak builtin protocol mapper).
- **Audience mapper:** so every emitted JWT carries `aud:
  ["smart-sentinel-eye"]` for `ValidateAudience = true` (future
  hardening; current setup has audience validation off per
  `AuthenticationDefaults.cs:51`).

## mosquitto-go-auth

`src/AppHost/mosquitto/go-auth.conf`:

```ini
auth_plugin /mosquitto/go-auth.so

auth_opt_backends   jwt
auth_opt_jwt_mode   local
auth_opt_jwt_secret  ""                              # ignored in local mode
auth_opt_jwt_jwks_uri  http://keycloak:8080/realms/smart-sentinel-eye/protocol/openid-connect/certs
auth_opt_jwt_jwks_cache_ttl  86400                   # 24 h

auth_opt_jwt_userfield_clientid  azp
auth_opt_jwt_aclquery            local

# ACL: a publish to fab/<fabId>/<source>/<device> is allowed iff
#   scope contains "sse.events.publish"
#   groups contains "/fabs/<fabId>"
#   clientId equals "<source>-<deviceId>"
# Implemented via the plugin's expression language; specifics in PR E.
```

The plugin reads JWKS on first connect, caches for 24 h, refreshes
on signature-validation failure (handles `kid` rotation
transparently — FR-006).

## Per-endpoint scope migration (PR F)

Every `RequireAuthorization(AdminPolicy)` call site is migrated.
Approximate count by context:

| Context | Approx. endpoints | Pattern |
|---|---|---|
| CameraCatalog | 4 | `RequireAuthorization(...)` → `RequireScope(Scope.SSE.Cameras.Read/Write)` |
| StreamDistribution | 5 | same |
| LayoutComposition | 6 | same; fab guard added on `fabId`-bearing endpoints |
| OverlayDesigner | 6 | same |
| SystemVariables | 5 | `sse.variables.{read,write}` |
| EventIngestion | 7 | `sse.events.{read,write}`; `/events/webhook/{name}` becomes JWT-only on rotated integrations |
| Automation | 3 | `sse.rules.{read,write}` |
| Identity (new) | 7 | `sse.identity.{devices,kiosks}.write`, `sse.webhooks.write` |

Each existing context's API endpoints gets one PR-F commit. Tests
in each context's Application.Tests are updated only where the
scope check is asserted; behaviour unchanged.

## Cross-context wire-in — surfaces created

### New V1 contracts

```csharp
// In Shared.Contracts/Identity/
public sealed record DeviceRegisteredV1(
    Guid RegisteredClientIdentifier,
    string ClientId,
    string DeviceType,
    string DeviceIdentifier,
    string Fab,
    DateTimeOffset RegisteredAt) : IIntegrationEvent;

public sealed record KioskEnrolledV1(
    Guid RegisteredClientIdentifier,
    string ClientId,
    string Fab,
    DateTimeOffset EnrolledAt) : IIntegrationEvent;

public sealed record WebhookIntegrationRotatedV1(
    string IntegrationName,
    string ClientId,
    DateTimeOffset RotatedAt) : IIntegrationEvent;
```

### EventIngestion subscriber

`WebhookIntegrationRotatedV1Handler` (new) flips the integration's
bearer-validation path from `hash-compare` to `JWT-validate`. This
is a small change to `EventsEndpoints.IngestWebhook` (spec 006):
on a registered+rotated integration, validate the bearer as a
Keycloak JWT (with `sse.events.write` scope + fab match); otherwise
fall back to the legacy hash compare. Migration is hard-cut on
rotation (FR-016).

## Performance Validation

Plan-phase commitment: two integration tests gate the polish PR.

- `NFR001_JwtValidationLatencyTests` — warm 1 000 iterations of
  the auth pipeline against a Testcontainers Keycloak; asserts
  p99 ≤ 500 µs.
- `NFR002_MqttConnectAuthTests` — warm 100 device-connect
  cycles against a Testcontainers Mosquitto + Keycloak; asserts
  p99 ≤ 5 ms.

NFR-001/NFR-002 are smaller than spec 006/007's latency tests
and don't need a full Aspire fixture — Testcontainers per service
is enough.

## Out of Scope (deferred — re-stated for the plan)

- **In-app onboarding UI** (spec 008a).
- **Hard-cut of Mosquitto password file** (spec 008b).
- **Dual-validate window for webhook migration** — hard-cut only.
- **Per-fab realms** — single shared realm in v1.
- **SCIM / external IdP federation.**
- **Self-service human signup.**
- **Audit log of authentication events surfaced in management-web**
  — spec 009 (AuditObservability) territory.

## PR shape (Phase 7 preview — drives the task breakdown)

Six PRs against `develop`, in dependency order:

| PR | Title | Scope | Gate |
|---|---|---|---|
| A | `feat(identity): scaffold + Aspire + ADR-0100 + Scope catalogue + V1 contracts` | Empty projects, identity Aspire resource, ADR-0100 (mosquitto-go-auth), ServiceDefaults Scope catalogue + IFabAuthorizationGuard, three V1 contracts + tests. | Aspire boots; scope policies registered; V1 contract tests pass |
| B | `feat(identity): Domain — RegisteredClient aggregate + value objects` | All Domain VOs (incl. `ClientSecret` write-once), aggregate, state machine (Register / Disable / Rotate), domain tests. | Domain tests ≥ 90% coverage |
| C | `feat(identity): Keycloak Admin REST client + Application command handlers` | `HttpKeycloakAdminClient`, command handlers (RegisterDevice, EnrollKiosk, RotateWebhookClient, Disable*), Application tests with a `FakeKeycloakAdminClient`. | Application tests ≥ 80% coverage |
| D | `feat(identity): Infrastructure + Identity API endpoints + Aspire wiring` | EF persistence, repository, query source, full Infrastructure module, `/devices` + `/kiosks` + `/webhook-integrations/.../rotate` endpoints, Program.cs, AppHost wiring of `identity` + `identity-db`. | Integration test: register a device → JWT round-trip works |
| E | `feat(identity): Keycloak realm bootstrap + mosquitto-go-auth` | `Realms/sse-realm.json` populated with scopes/bundles/clients/groups; `mosquitto-go-auth` plugin + `go-auth.conf`; AppHost mosquitto resource updated. | `aspire run` boots a working realm + Mosquitto-JWT auth; manual MQTT publish with a Keycloak JWT works |
| F | `feat(identity): per-endpoint scope migration + EventIngestion JWT path + polish` | Every context's API swaps `AdminPolicy` for the matching `Scope` constant + fab guard. `WebhookIntegrationRotatedV1Handler` in EventIngestion flips the bearer-validation path. Coverage gates, NFR-001/NFR-002 latency tests, README quickstart. | All coverage gates pass; latency tests pass |

Phase-5 verification (run the spec 008 release end-to-end with
real Keycloak credentials) is the release-PR gate.

## Gate (Phase 2 → Phase 3)

This plan is ready for the Tasks phase once the architect lead
confirms:

1. The PR shape above (A–F) matches the team's preferred review
   cadence.
2. The scope catalogue + bundle mapping in FR-002 are the final
   v1 set (changes after Phase 3 require a spec amendment).
3. The hand-rolled Keycloak Admin REST client (no `Keycloak.Net`
   dep) is acceptable.
4. The grandfathered Mosquitto password file + legacy webhook
   bearers staying valid until rotation is acceptable.
5. The ≤ 500 µs / ≤ 5 ms NFR budgets are plausible to verify in
   CI under Testcontainers.

When the gate is approved, Phase 3 (`/speckit-tasks`) decomposes
this plan into atomic tasks (~85–105 tasks), each ≤ 30 minutes
of work, with `[P]` markers on parallelizable tasks and `[US-N]`
cross-references back to the spec's user stories.
