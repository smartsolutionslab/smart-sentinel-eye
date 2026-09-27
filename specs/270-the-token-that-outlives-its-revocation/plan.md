# Plan 270: The token that outlives its revocation

**Spec**: [spec.md](spec.md) · **Issue**: #2241 (primary ask only) · **ADR**: [ADR-0160](../../docs/adr/0160-revocation-is-checked-at-request-time.md) · **Phase**: 2 (Plan)

## 0. Why this gets an ADR

The product owner made the decision: a request-time `DisabledAt` check, not
introspection or a shorter lifespan. ADR-0160 **records** that decision. It does not
make it. It is an ADR and not a paragraph here because the mechanism sets precedent,
the same way ADR-0142 and ADR-0143 did. This is the first time the system refuses a
correctly signed, unexpired token, and the first time every service pulls shared
state from one context on a timer. Several choices will be copied by the next
"refuse this token for a reason Keycloak can't encode" need: the source of truth,
the freshness bound, fail-static versus fail-closed, a shared service account, and
the `iat` comparison. A plan is not the place a future reader looks for them. The
ADR lists introspection and a shorter lifespan only as rejected, and cites the
product owner.

## Constitution / ADR check

| Rule | How this plan satisfies it |
|---|---|
| §III: no cross-context references | Consumers reach Identity over HTTP with a DTO in `Shared.Contracts/Identity/`. No project references Identity. `ServiceDefaults` gains a reference to `Shared.Contracts` (§5). Both depend only on `Shared.Kernel`, and no architecture rule forbids the edge (checked: `tests/Architecture.Tests` has no ServiceDefaults dependency rule). |
| §II primitives | Nothing in Domain changes. The snapshot lives in `ServiceDefaults` (not a domain model). The query projects `ClientId`/`DisabledAt` value objects to primitives at the DTO boundary, exactly as `RegisteredClientProjection` does. |
| ADR-0106 | The gateway does no auth offload, so enforcement is in every service. The gateway is not touched. |
| ADR-0116 | A dedicated read-only service account. One, not eight (ADR-0160 §4). |
| ADR-0143 | The refresh is a `GET`, retried by the default. The token mint uses the existing `ClientCredentialsTokenProvider` wrapper pattern, and its `HttpClient` opts back in with `.RetryEveryMethod()` and the stated reason, as the five existing mints do. |
| ADR-0153 | In-process snapshot per service instance. That is correct at one instance, and still correct at N, because each replica polls. |
| ADR-0154 | The new health check reports `Degraded`, never `Unhealthy`. |
| ADR-0047 / 0105 / 0141 | `Ensure.That` guards. `Option<T>` for "no snapshot yet". The query handler returns `Result<…, ListClientsError>` like its siblings. |
| ADR-0050 | `[LoggerMessage]` source-gen logs, transition-only, mirroring `WhepAuthValidator`'s `realmUnreachable` exchange. |
| House rules | No `_` fields. Collection expressions. Handlers destructure when they read ≥ 2 fields. |

## 1. Where things are (for phase 4, so nobody re-discovers them)

| What | Path |
|---|---|
| The nine REST APIs' shared bearer wiring (**the choke point**) | `src/ServiceDefaults/AuthenticationDefaults.cs`, `AddBearerAuthentication` → `.AddJwtBearer(options => …)` |
| Callers of it (one per API) | `src/{AuditObservability,Automation,CameraCatalog,EventIngestion,Identity,LayoutComposition,OverlayDesigner,StreamDistribution,SystemVariables}/Api/Program.cs` |
| Existing `JwtBearerEvents` user to chain with | `src/LayoutComposition/Api/Program.cs:18-22` (`Configure<JwtBearerOptions>`, wraps `OnMessageReceived`) |
| WHEP validator | `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs`. Interface: `src/StreamDistribution/Application/Auth/IWhepAuthValidator.cs`. Registered in `src/StreamDistribution/Infrastructure/StreamDistributionInfrastructureModule.cs` |
| `DisabledAt` shape | `src/Identity/Domain/RegisteredClient/DisabledAt.cs`: `sealed record DisabledAt(DateTimeOffset Value) : IValueObject<DateTimeOffset>`, comparable, with an implicit conversion to `DateTimeOffset`. On `RegisteredClient` it is `DisabledAt? DisabledAt` (null = active). It is set once by `Disable(IClock)`, which is idempotent, and nothing clears it. |
| Who sets it | `DisableKioskCommandHandler.cs:32`, `DisableDeviceCommandHandler.cs:32`, `DisableWebhookClientCommandHandler.cs:37`. Each calls `keycloak.DisableClientAsync` **first**, then `client.Disable(clock)` + `SaveAsync`. So the Keycloak client is disabled *before* `DisabledAt` is stamped (§2 relies on this). |
| Read path to reuse | `src/Identity/Application/Queries/IRegisteredClientQuerySource.cs` (`IQueryable<RegisteredClient>`, `AsNoTracking`), used by `ListDevicesQueryHandler` / `ListKiosksQueryHandler` / `ListWebhookClientsQueryHandler`. EF mapping: `RegisteredClientConfiguration.cs:66-68` (`HasConversion(v => v!.Value, …)`). The comment at `DisabledAt.cs:42` warns that `.Value` member access does not translate, so compare/project the property itself. |
| Client-id reuse | `RegisteredClientRepository.cs:26-36`: disabled rows release the client id for re-registration. That is why FR-001 compares with `iat` rather than refusing the client id outright. |
| Client-credentials token plumbing | `src/ServiceDefaults/Authentication/ClientCredentialsTokenProvider.cs`, `AuthorizingHandler.cs`. Nearest consumer precedent: `src/StreamDistribution/Infrastructure/Attribution/CameraCatalog{TokenProvider,AuthorizationHandler,FabLookup}.cs` |
| Scope catalogue | `src/ServiceDefaults/Authorization/Scope.cs` (+ `Scope.All`). Realm: `src/AppHost/Realms/smart-sentinel-eye-realm.json` |
| Secret parameter precedent | `src/AppHost/AppHost.cs:53-58` (`StreamDistributionAttributionClientSecret`) |

## 2. The rule, precisely

```
refuse(token) ⇔ azp ≠ null
             ∧ snapshot.TryGet(azp, out latestDisabledAt)
             ∧ (iat = null ∨ latestDisabledAt ≥ iat − 5 min)
```

- **Why `iat`, not just `azp`.** The client id is reusable after a disable (§1). A
  re-registered client's tokens are minted after the old `DisabledAt` and must pass.
- **Why the tolerance.** Keycloak stamps `iat` and Identity's `IClock` stamps
  `DisabledAt`. Because `DisableClientAsync` runs first, every token of the revoked
  client was minted before Keycloak's disable, and so before `DisabledAt`, **if the
  clocks agree**. With skew δ, a last-second token could carry `iat` up to δ later
  than `DisabledAt`. The bearer pipeline already assumes clocks agree to within its
  default `ClockSkew` of 5 min (it extends `exp` by that). Reusing that figure adds no
  new assumption. The cost falls on the refusal side: a same-id re-registration
  within 5 min of a revoke is refused until then.
- **No `iat`: refuse, if the client is listed.** Keycloak always emits `iat`. A token
  without it is anomalous, and a listed client is exactly where "anomalous" should
  not pass.
- **No `azp`: admit.** Such a token was not minted by a `RegisteredClient`, and the
  existing pipeline has already judged it.
- **The latest `DisabledAt` per client id** (disable → re-register → disable).

The rule is a pure function in `ServiceDefaults`
(`RevokedClientSnapshot.Refuses(string? azp, DateTimeOffset? issuedAt)`), so both
enforcement points share one implementation and one set of unit tests.

## 3. Data source and freshness (the caching decision)

**Decision: a refresh-ahead, whole-list snapshot, replaced every 5 s. No read-through
per-key cache and no per-request call.**

| Option | Request-path I/O | Revocation bound | Identity outage | Verdict |
|---|---|---|---|---|
| No cache: ask Identity per request | every request | ~0 | every client refused or admitted blind | No. It is the live dependency the product owner excluded, at introspection's cost. |
| Read-through per-`azp` cache, TTL *t* | on every miss/expiry | *t* | misses fail | No. A revoked client's *first* request after expiry blocks on Identity, and operator `azp`s (never listed) miss forever unless negative-cached. |
| **Refresh-ahead snapshot, period 5 s** | **none** | **≤ 5 s + round-trip** | **last snapshot kept (still correct)** | **Chosen** |
| Long TTL (minutes) | none | minutes | — | No. It re-opens the window the fix exists to close. |

**Why 5 s.** It has to be short, because this is a security check and the status quo
is 60 min. It does not have to be sub-second: revocation is a human action, and the
operator's own UI round-trip is of the same order. Cost: eight services × 0.2 rps,
each a single indexed-or-not scan of a table of a few hundred rows (§ADR-0160
Consequences states the re-measure trigger). It is a **constant** in code, not a
configuration option. There is no need for a knob today (ADR-0036), and tests drive
the refresher by calling its single-refresh step directly and reading staleness from a hand-written `TimeProvider` (the `AdvanceableClock` precedent in `ClientCredentialsTokenProviderTests.cs:183`, ADR-0054; no `FakeTimeProvider` package is referenced), not by shortening the period.

**Why the whole list, unbounded.** One `WHERE disabled_at IS NOT NULL`, no coupling to
`accessTokenLifespan`. Bounding it to `DisabledAt ≥ now − (lifespan + 5 min)` is
correct, but it adds a realm-file pairing test and saves nothing at today's size. It
is deferred with an explicit trigger in the ADR.

**Fail-static.** On a failed refresh, keep the snapshot: revocations only accumulate,
and a revoke needs the same Identity the refresh couldn't reach. Before the first
success the snapshot is `Option.None`, the check admits, and health is `Degraded`
(ADR-0160 §3 has the reasoning, and the reason it is not fail-closed).

**Identity reads its own table.** `IRevokedClientSource` has two implementations:
the default `HttpRevokedClientSource` (ServiceDefaults), and
`LocalRevokedClientSource` (Identity.Infrastructure), which runs the same query
handler in process. Identity's module replaces the registration, and
`src/Identity/Api/Program.cs:8-9` calls `AddBearerAuthentication` before
`AddIdentityInfrastructure`, so a `Replace` there wins. The HTTP source is then never
constructed in Identity, which therefore needs no reader credential.

## 4. Components

### 4.1 Shared.Contracts

- **New** `src/Shared.Contracts/Identity/RevokedClientsResponse.cs`:
  `sealed record RevokedClientsResponse(IReadOnlyList<RevokedClientEntry> Clients)`.
  Also `sealed record RevokedClientEntry(string ClientId, DateTimeOffset DisabledAt)`.
  These are HTTP DTOs. Primitives are allowed, since `Shared.Contracts` is exempt
  from §II.

### 4.2 Identity (Application → Infrastructure → Api)

- **New** `Application/Queries/ListRevokedClientsQuery.cs` (no parameters; not
  fab-scoped) and `Queries/Handlers/ListRevokedClientsQueryHandler.cs`. It reads
  `IRegisteredClientQuerySource.RegisteredClients` where `DisabledAt != null`, groups
  by `ClientId`, and takes the max `DisabledAt`. It returns
  `Result<IReadOnlyList<RevokedClientEntry>, ListClientsError>`, reusing the
  sibling error type. Sibling handlers show how to map values without `.Value` inside
  the expression tree.
- **New** `Infrastructure/Revocation/LocalRevokedClientSource.cs` implements
  `IRevokedClientSource` over that handler, resolving it from a scope because the
  source is a singleton and the DbContext is scoped.
- **Edit** `Infrastructure/IdentityInfrastructureModule.cs`: register the handler and
  `services.Replace(ServiceDescriptor.Singleton<IRevokedClientSource, LocalRevokedClientSource>())`.
- **New** `Api/RevocationEndpoints.cs`: `GET /registered-clients/revoked`,
  `.RequireAuthorization(Scope.Sse.Identity.Revocations.Read)`, `Produces<RevokedClientsResponse>`,
  401/403 problems. **Edit** `Api/Program.cs` to map it. There is no fab resolution:
  the caller is a platform service account in no fab group, and every consumer needs
  every fab. The summary says this so `EndpointScopeDeclarationTests` and any reviewer
  see the reason.

### 4.3 ServiceDefaults (the shared enforcement)

All of these are new files under `src/ServiceDefaults/Revocation/` unless noted.

- `RevokedClientSnapshot`: immutable, wraps
  `FrozenDictionary<string, DateTimeOffset>` (ordinal). Holds the §2 rule as
  `Refuses(string? azp, DateTimeOffset? issuedAt)` and the `ClockTolerance = 5 min`
  constant.
- `IRevokedClientSource`: `Task<IReadOnlyList<RevokedClientEntry>> FetchAsync(CancellationToken)`.
- `IRevokedClientRegistry`: `bool Refuses(string? azp, DateTimeOffset? issuedAt)`,
  implemented by `RevokedClientRegistry` (singleton) over a `volatile`
  `Option<RevokedClientSnapshot>`. `Refuses` reads the field once. Before the first
  load it returns `false`.
- `RevokedClientRefresher : BackgroundService`: the loop is `PeriodicTimer(5 s)`
  around an `internal Task RefreshOnceAsync(CancellationToken)`, which the tests call
  directly. There is no overlap. The first fetch is immediate on start. The
  last-success time comes from the injected `TimeProvider`. On success it swaps the
  snapshot. On failure (non-cancellation) it keeps it. It logs transitions only,
  using the `Interlocked.Exchange` pattern from `WhepAuthValidator`. It swallows no
  `OperationCanceledException` on shutdown.
- `RevokedClientSnapshotHealthCheck`: `Degraded` until the first load, and when the
  last success is older than 6 periods (30 s). Otherwise `Healthy`. Tag `ready`,
  `failureStatus: Degraded` (ADR-0154).
- `HttpRevokedClientSource` (typed `HttpClient`, base `https+http://identity`) and
  `RevocationListTokenProvider` / `RevocationListAuthorizationHandler` over
  `ClientCredentialsTokenProvider` / `AuthorizingHandler`, mirroring
  `CameraCatalogTokenProvider` / `CameraCatalogAuthorizationHandler`. Credentials
  come from configuration `RevocationList:ClientId` / `RevocationList:ClientSecret`,
  bound through an options class with `ValidateOnStart` in the HTTP source's
  registration only. The token `HttpClient` gets `.RetryEveryMethod()` with the
  justification comment the other five mints carry.
- `Log.cs` (edit): four `[LoggerMessage]`s: snapshot unavailable (Warning, with the
  exception), snapshot restored (Information), token refused as revoked
  (Information, `ClientId`), first snapshot loaded (Information, count).
- **Edit** `AuthenticationDefaults.AddBearerAuthentication`:
  1. Register the registry, refresher, health check and HTTP source (`TryAdd`), so
     Identity's `Replace` wins.
  2. Wire `options.Events.OnTokenValidated`, chaining any existing delegate and
     running it first. Read `azp` from `context.Principal` and `iat` from
     `context.SecurityToken` (`JsonWebToken.IssuedAt`; `DateTime.MinValue` means
     absent). If the registry refuses, log and call `context.Fail("client revoked")`.
     The handler then emits the standard 401 `invalid_token` challenge (FR-002).
     `Events` must be created if null, and set in a way that survives
     LayoutComposition's later `Configure<JwtBearerOptions>`, which does
     `options.Events ??= new JwtBearerEvents()` and replaces only
     `OnMessageReceived`. So setting `OnTokenValidated` on the object created inside
     `AddJwtBearer` is preserved.
- **Edit** `SmartSentinelEye.ServiceDefaults.csproj`: `ProjectReference` to
  `Shared.Contracts`.

### 4.4 StreamDistribution (WHEP)

- **Edit** `WhepAuthValidator`: both constructors take `IRevokedClientRegistry`
  (guarded). The internal test seam (#2099) gains it too, keeping it `internal`.
  After `handler.ValidateToken(bearerToken, validationParameters, out SecurityToken validated)`
  and the `sub` check, read `azp` from the principal and `IssuedAt` from the
  `JwtSecurityToken`. If the registry refuses, return
  `Failure(WhepAuthFailure.TokenRejected)` with no metadata refresh, because a fresher
  document cannot cure a revocation. Registration is unchanged, since the registry is
  already in DI via `AddBearerAuthentication` in `StreamDistribution/Api/Program.cs:12`.
- `IWhepAuthValidator`, `AuthorizeWhepCommandHandler` and `StreamEndpoints`:
  **unchanged**. `TokenRejected` already maps to 401.

### 4.5 Keycloak realm and AppHost

- **Edit** `smart-sentinel-eye-realm.json`:
  - Add the client scope `sse.identity.revocations.read`, shaped like its siblings.
  - Add the client `revocation-list-reader`: confidential, service accounts only,
    `secret: "dev-only-revocation-list-reader-secret"`, default scopes
    `sse-audience` + `sse.identity.revocations.read`, **no `sse-groups`**, in no fab
    group.
  - Keep `description` **≤ 255 characters**. A longer one kills the realm import and
    hangs the Aspire fixture (memory).
- **Edit** `Scope.cs`: `Sse.Identity.Revocations.Read` plus a `Scope.All` entry.
- **Edit** `AppHost.cs`:
  - Add `AddOverridableParameter("RevocationListReaderClientSecret", "dev-only-revocation-list-reader-secret", secret: true)`.
  - On each of the eight non-Identity APIs, add `.WithReference(identity)` (**not**
    `WaitFor`: start must not gate on Identity, ADR-0160 §3) and
    `WithEnvironment("RevocationList__ClientId", "revocation-list-reader")` /
    `("RevocationList__ClientSecret", secret)`.
  - `identity` is declared at `:505`. Services declared earlier get the reference
    added after that line (resource builders are mutable), or through a small local
    helper that applies all three. Pick one and comment why.

## 5. Messaging

**None.** No domain event and no integration event is added. The existing
`ClientDisabledDomainEvent` is untouched. Push over RabbitMQ was considered and
rejected in ADR-0160: restart amnesia, and a five-second snapshot event would land in
the mandatory audit fan-out.

## 6. Latency (§IV)

**N/A: not on the event-to-overlay path.** The REST check is one frozen-dictionary
lookup after signature validation. The WHEP check runs at session setup
(`/streams/authorize`), before any media leg. `NFR001_JwtValidationLatencyTests` stays
green unmodified. The refresher runs off the request path.

## 7. Test strategy (all new-behaviour → **red first**, ADR-0139)

| Level | Project / file | Proves |
|---|---|---|
| Unit | `tests/ServiceDefaults.Tests/Revocation/RevokedClientSnapshotTests.cs` | §2 truth table: listed + `iat` before / within tolerance / after tolerance; unlisted; no `azp`; no `iat`; latest-wins; ordinal client-id match |
| Unit | `tests/ServiceDefaults.Tests/Revocation/RevokedClientRefresherTests.cs` | Drives `RefreshOnceAsync` directly plus a hand-written `TimeProvider` (ADR-0054): first load immediate; replace on success; keep on failure; `Degraded` before first load and after 6 missed periods; one log per transition; cancellation stops cleanly |
| Unit | `tests/ServiceDefaults.Tests/Revocation/BearerRevocationHookTests.cs` | `OnTokenValidated` fails a refused principal; admits an unlisted one; **chains** a pre-existing `OnTokenValidated`; survives a later `Configure<JwtBearerOptions>` that sets `OnMessageReceived` (LayoutComposition's shape) |
| Unit | `tests/StreamDistribution.Infrastructure.Tests/Auth/WhepRevocationTests.cs` | Through the internal seam with in-memory metadata: a revoked `azp` gives `TokenRejected`; a re-registered (later `iat`) gives success; no `RequestRefresh` on a revocation refusal |
| Unit | `tests/Identity.Application.Tests/Queries/ListRevokedClientsQueryHandlerTests.cs` | Only disabled rows; all kinds; all fabs; max `DisabledAt` per client id |
| Arch | existing `ScopeGrantTests`, `RealmIdentityTests`, `EndpointScopeDeclarationTests`, `RealmImportMirrorTests` (+ one row for the new secret) | Scope ↔ realm ↔ AppHost pairing |
| Integration (Aspire) | `tests/Integration.Tests/Identity/RevokedTokenRefusedIntegrationTests.cs` | US1 + US2 end to end: enrol a kiosk, mint T1, 200 on `GET /cameras` and `POST /streams/authorize`, disable, 401 on both within 20 s (poll 500 ms). Includes an unrevoked control kiosk that stays 200 throughout. The revocation endpoint's 401/403/200 by scope. **Needs a `shard-N.filter` entry** (memory) |

The re-registration case has no integration fact, deliberately. A same-id re-enrolment
is refused by Keycloak today, because the disabled client persists and
`CreateClientAsync` throws `KeycloakClientAlreadyExistsException`
(`HttpKeycloakAdminClient.cs:57-72`). Proving the admit side would also need
`iat > DisabledAt + 5 min`, which means five minutes of wall clock in CI. The `iat`
rule is proved at unit level with controlled clocks. It is kept because the local
table explicitly allows reuse, and a manually deleted Keycloak client makes reuse
real.

The integration class needs a **control**: a second enrolled kiosk, not revoked,
whose token stays 200 across the same window. Without it, a 401 caused by anything
else (an expired token, a broken realm) would pass for the fix (memory: *an assertion
must not check its own input*).

## 8. Deliberately unchanged

`RegisteredClient`, `DisabledAt`, the three Disable handlers, `IKeycloakAdminClient`,
`WolverineDefaults`, the gateway, `IWhepAuthValidator`, `AuthorizeWhepCommandHandler`,
`EventsEndpoints*`, and `accessTokenLifespan`.

## 9. Risks

- **Realm volume.** Local runs keep the old realm until the Keycloak volume is
  deleted. CI boots fresh.
- **Fixture boot.** Eight services gain an Identity reference but not `WaitFor`. The
  refresher tolerates Identity arriving late (Degraded → Healthy), so no boot
  ordering changes.
- **`ScopeGrantTests`' "granted to nobody" guard.** The new scope is granted to
  `revocation-list-reader`, so the guard is satisfied. Do not grant it to
  `management-web`.
