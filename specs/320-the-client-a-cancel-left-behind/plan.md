# Plan: Spec 320, the client a cancel left behind

**Spec:** [`spec.md`](./spec.md) · **Tasks:** [`tasks.md`](./tasks.md) · **Issue:** #2181

## 0. Constitution and ADR check

| Principle | Status | Note |
|---|---|---|
| II. DDD with value objects | ✅ | No domain-model change. The repository read returns Identity's existing `ClientId` value object. `IKeycloakAdminClient` is an Application port that already speaks `string` client ids (`GetEnrolledKioskClientIdsAsync`). The new members follow it. `PrimitiveBoundaryTests`' scope is unaffected. |
| III. Bounded-context isolation | ✅ | Identity only. No contract, no message, no cross-context call. |
| IV. Latency budget | N/A | Background task, off the event path (spec §7). |
| VIII. Safe by default | ✅ | Stamped-only filter, enabled-only, grace window, mass-disable guard, disable not delete (spec §4). |
| ADR-0134 D1 / spec 092 | ✅ | Pass/wrapper split, failure isolation and logging discipline mirrored (§5). |
| ADR-0142 / 0143 | ✅ | No new endpoint. The sweep's Keycloak reads are `GET`s, retried by default. `DisableClientAsync`'s `PUT` is idempotent and already in use. No `RetryEveryMethod()` is added. |
| ADR-0141 / 0105 / 0049 / 0050 | ✅ | `Option<DateTimeOffset>` for "no service account" in Application, `Ensure.That` guards, token last, `[LoggerMessage]`. |

**Gate: PASS.**

## 1. Bounded context and layers

**Identity only.** No AppHost, realm, migration, Shared.* or frontend change.

```
src/Identity/
  Domain/RegisteredClient/
    IRegisteredClientRepository.cs                 EDIT +GetActiveClientIdsAsync
  Application/
    KeycloakAdmin/
      IKeycloakAdminClient.cs                      EDIT +GetStampedClientsAsync, +GetServiceAccountCreatedAtAsync
      StampedClient.cs                             NEW  record (ClientId, Kind, Enabled)
      OrphanedClientSweep.cs                       NEW  the pass + OrphanedClientSweepOutcome
      KioskPrivilegeSweep.cs                       EDIT doc only (names the new sweep)
    Log.cs                                         EDIT +4 [LoggerMessage]
  Infrastructure/
    KeycloakAdmin/
      HttpKeycloakAdminClient.cs                   EDIT the two reads; ClientDetailRow +Enabled;
                                                        ServiceAccountUser +CreatedTimestamp;
                                                        TryDeleteClientAsync doc
      OrphanedClientSweepHostedService.cs          NEW  BackgroundService, startup + hourly
    Persistence/RegisteredClientRepository.cs      EDIT +GetActiveClientIdsAsync
    IdentityInfrastructureModule.cs                EDIT AddScoped<OrphanedClientSweep>, AddHostedService<…>
    Log.cs                                         EDIT +1 [LoggerMessage] (pass failed)
```

The pass lives next to `KioskPrivilegeSweep` in `Application/KeycloakAdmin/`, its precedent.
The repository read is on the Domain interface because that is where
`IRegisteredClientRepository` lives (ADR-0092). It is a read, so no aggregate changes.

## 2. Entities, value objects, invariants

No aggregate changes and no domain events. The sweep writes nothing to Postgres. It writes to
Keycloak, which is not an aggregate. Its invariants are the pass's own:

| # | Invariant | Enforced by |
|---|---|---|
| S1 | Only clients with `sse.kind ∈ {device, kiosk}` are ever candidates | `OrphanedClientSweep.SweptKinds`, a set in one place; unit U3 |
| S2 | Only **enabled** clients are candidates | filter in the pass; unit U5 |
| S3 | A client with an **active** row of the same id (any kind) is never a candidate | `GetActiveClientIdsAsync` filters `DisabledAt == null`; unit U2, integration I3 |
| S4 | A candidate whose service account is younger than `GraceWindow` (10 min), or has none, is not disabled | `GetServiceAccountCreatedAtAsync` + `IClock`; units U4, U7; integration I2 |
| S5 | Keycloak is read **before** Postgres | statement order in `SweepAsync`; unit U9 asserts the call order on the fakes |
| S6 | If `candidates > stamped-enabled / 2` **and** `candidates ≥ 2`, nothing is disabled | the guard, evaluated **after** the age filter; units U8a, U8b |
| S7 | One candidate failing does not stop the others | per-candidate `try/catch (… when not OperationCanceledException)`; unit U6 |
| S8 | A pass failure never stops the host, and `StartAsync` does not wait on Keycloak | hosted service; tests H1–H3 |

**S6 counts only orphans past the grace window**, so a burst of in-flight registrations cannot
trip it. The denominator is the enabled stamped clients of the swept kinds, the population the
pass is allowed to act on.

## 3. Port changes, exact shape

```csharp
// Application/KeycloakAdmin/StampedClient.cs
/// A Keycloak client carrying an sse.kind attribute, as listed by the admin API.
public sealed record StampedClient(string ClientId, string Kind, bool Enabled);

// IKeycloakAdminClient — two additions
/// Every client carrying an sse.kind attribute, whatever its value. The caller filters;
/// the adapter does not decide policy. One GET /clients, like GetEnrolledKioskClientIdsAsync.
Task<IReadOnlyList<StampedClient>> GetStampedClientsAsync(CancellationToken cancellationToken);

/// When the client's service-account user was created, by Keycloak's clock — the only
/// creation time Keycloak records for a client. None when the client or its service
/// account does not exist. Any other failure throws.
Task<Option<DateTimeOffset>> GetServiceAccountCreatedAtAsync(string clientId, CancellationToken cancellationToken);

// IRegisteredClientRepository — one addition
/// The client ids of every row not disabled, of any kind.
Task<IReadOnlySet<ClientId>> GetActiveClientIdsAsync(CancellationToken cancellationToken);
```

**Adapter (`HttpKeycloakAdminClient`):**

- `GetStampedClientsAsync`: `GET admin/realms/{realm}/clients`. This is the same request
  `GetEnrolledKioskClientIdsAsync` makes, and Keycloak returns all clients for it, as the
  existing kiosk query already relies on. Deserialised into `ClientDetailRow`, which gains
  `bool Enabled`. Rows with an `sse.kind` attribute are kept. `EnsureSuccessStatusCode`
  throws on failure: an unanswered listing is not an empty realm.
- `GetServiceAccountCreatedAtAsync`: `TryGetClientUuidAsync`, then `None` if the client is
  absent. `GET …/clients/{uuid}/service-account-user`: **404 → `None`**, other non-2xx
  throws. `ServiceAccountUser` gains `long? CreatedTimestamp`. A null timestamp means
  `None`, so the client is never treated as old by default.
  `DateTimeOffset.FromUnixTimeMilliseconds`.
- **Pinned-version check.** `createdTimestamp` on the service-account user is asserted
  against the pinned Keycloak 26.6.4 by integration fact **I0b** before anything relies on
  it. This repository has been burned by a plan's SDK claim that was false of the pinned
  version. If I0b fails, **stop and escalate**. The fallback, stamping an `sse.createdAt`
  attribute in both handlers, changes the handlers, so it needs the gate.

**Repository:** `context.RegisteredClients.Where(c => c.DisabledAt == null).Select(c => c.ClientId)`,
materialised into a `HashSet<ClientId>` (`ClientId` is a record, so it has value equality).
`AsNoTracking`.

## 4. The pass: `OrphanedClientSweep.SweepAsync`

```
1. stamped   = keycloak.GetStampedClientsAsync()                         // S5: Keycloak first
2. eligible  = stamped.Where(kind ∈ {device, kiosk} && Enabled)          // S1, S2
3. active    = clients.GetActiveClientIdsAsync()
4. noRow     = eligible.Where(c => !active.Contains(ClientId.From(c.ClientId)))   // S3
5. for each in noRow (try/catch per client, S7):
       createdAt = keycloak.GetServiceAccountCreatedAtAsync(c)
       None              → unverifiable += c; log CouldNotDetermineOrphanAge   // S4
       now - createdAt < GraceWindow → skip silently (in flight)              // S4
       else              → orphans += (c, age)
6. if orphans.Count >= 2 && orphans.Count * 2 > eligible.Count            // S6
       log OrphanSweepRefused(orphans.Count, eligible.Count); return outcome(refused)
7. for each orphan (try/catch per client, S7):
       keycloak.DisableClientAsync(c); log DisabledOrphanedClient(c, kind, age)
8. if disabled > 0: log SweptOrphanedClients(disabled, eligible.Count)   // silent otherwise, as spec 132
9. return OrphanedClientSweepOutcome(Examined: eligible.Count, Disabled, Refused, Unreachable)
```

- A client id Keycloak holds that is not a valid `ClientId` (`ClientId.From` throws) cannot
  have a row. It goes to `unreachable` and gets a Warning, and is **not disabled**. A
  malformed stamped client is something to look at, not something to act on silently.
- `GraceWindow = TimeSpan.FromMinutes(10)` and `SweptKinds = {"device", "kiosk"}` are
  `public static` on the pass, so tests and the hosted service's doc can name them. They are
  not configuration.
- The constructor is `(IKeycloakAdminClient keycloak, IRegisteredClientRepository clients, IClock clock, ILogger<OrphanedClientSweep> logger)`.
  All four collaborators are already registered.
- **Log messages** (Application `Log.cs`, `[LoggerMessage]`, structured fields):
  `DisabledOrphanedClient` (Warning: `ClientId`, `Kind`, `Age`): disabling a live credential
  is security-relevant and must stand out. `SweptOrphanedClients` (Information: `Disabled`,
  `Examined`). `OrphanedClientSweepRefused` (Error: `Candidates`, `Examined`).
  `CouldNotSweepOrphanedClient` (Warning: `ClientId`, exception), shared by the age-read and
  disable failures and by the malformed id.

## 5. The hosted service: `OrphanedClientSweepHostedService`

`BackgroundService(IServiceScopeFactory, TimeProvider, ILogger<…>)`, the shape of
`IdempotencyReservationSweepHostedService`, with `KioskPrivilegeSweepHostedService`'s scope
handling:

```csharp
internal static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    await Task.Yield();                       // StartAsync must never wait on Keycloak (spec 317's lesson)
    using PeriodicTimer timer = new(TickInterval, timeProvider);
    try
    {
        await RunOnceSafelyAsync(stoppingToken);            // startup pass
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceSafelyAsync(stoppingToken);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
}
// RunOnceSafelyAsync: scope → GetRequiredService<OrphanedClientSweep>() → SweepAsync;
// catch (Exception) when not OCE → logger.OrphanedClientSweepFailed(ex). Never rethrows:
// BackgroundService's default StopHost behaviour must not apply to a janitor.
public Task RunOnceAsync(CancellationToken) // public, as IdempotencyReservationSweep's, so tests drive one pass
```

**There is no per-pass `Bound` like `KioskPrivilegeSweepHostedService`'s.** That bound exists
because `IHostedService.StartAsync` blocks the boot. Here `StartAsync` returns at the
`Task.Yield()`, and each Keycloak call is already bounded by the resilience handler's 30 s
total. Shutdown cancels through `stoppingToken`. Test H1 pins `StartAsync` returning
promptly against a Keycloak that never answers.

**Scope:** `IKeycloakAdminClient` and `IRegisteredClientRepository` are scoped, so the pass is
resolved per tick from a fresh scope. A `DbContext` per tick means no tracked-entity growth.

**Multiple replicas** (none today; the Helm chart is unbuilt): two passes racing disable the
same client twice. `DisableClientAsync` is idempotent, so the cost is a duplicate Warning.
Accepted.

## 6. Messaging

**None.** No domain event: no aggregate changes. No integration event: no other context has an
interest in an Identity credential nobody knew existed. `GET /devices` and `GET /kiosks` never
listed the orphan, so nothing downstream changes.

## 7. Boundary rules

- No new project reference. `NetArchTest` `BoundaryTests` stay green unchanged.
- Application does not take `Microsoft.Extensions.Hosting`. That is the reason for the
  pass/wrapper split, `KioskPrivilegeSweepHostedService`'s doc.

## 8. Tests

### 8.1 Integration (`tests/Integration.Tests/Identity/OrphanedClientSweepIntegrationTests.cs`, shard-1)

The pass is driven the way `KioskPrivilegeSweepStartupIntegrationTests` drives its pass. The
test composes `AddIdentityInfrastructure` in-process against the fixture's real Keycloak
**and real `identity-db`**, with `IClock` replaced by a fixed clock set *creation + 11 min*.
It resolves `OrphanedClientSweep` and runs one `SweepAsync`. Clients are planted with
`RealmProbe.PlantAsync` (stamp: `sse.kind`, `sse.fab=munich`; it already enables a service
account) and cleaned up in `finally` via `RealmProbe.DeleteAsync`. Identifiers carry a
`Guid.CreateVersion7()` suffix.

| # | Fact | On `develop` |
|---|---|---|
| I0a | **Control, defect live:** a planted enabled `sse.kind=device` client `plc-orphan-<n>` makes `POST /devices/register` for `orphan-<n>` answer 409 `DEVICE_ALREADY_REGISTERED` | **green** (HTTP only), proves the premise |
| I0b | **Control, pinned-version premise:** a planted client's service-account user carries `createdTimestamp` within ±2 min of the test's wall clock | **green** (raw admin HTTP), proves plan §3's premise on Keycloak 26.6.4 |
| I1 | **The red:** after I0a's planting, one pass past the grace window leaves the client `enabled=false`, and `POST /devices/register` for `orphan-<n>` answers **201** with a secret that a `client_credentials` grant accepts | **compile red** (no pass type); behaviourally 409 |
| I1k | The same for a kiosk via `POST /kiosks/enroll` | as I1 |
| I2 | A planted orphan with the clock at *creation + 2 min* stays enabled | compile red |
| I3 | A device registered through `POST /devices/register` stays enabled after a pass at *+1 day* | compile red |
| I4 | A planted **unstamped** client and a planted `sse.kind=webhook` client with no row stay enabled after a pass at *+1 day* | compile red |

**Note on the shared stack.** The stack's own Identity process runs this sweep too, against
real time. Every planted client is younger than 10 minutes for the length of a test, so the
stack's pass never races the test's. A test that dies without cleanup leaves residue that the
stack's next pass disables, which is the feature working. The `finally` still deletes it.

### 8.2 Application unit (`tests/Identity.Application.Tests/KeycloakAdmin/OrphanedClientSweepTests.cs`)

Against `FakeKeycloakAdminClient` (gains `StampedClients`, `ServiceAccountCreatedAt`, a
recorded call order for U9, and per-client failure hooks for U6/U6b; it already records
`Disabled` and has `FailNextDisableWith`),
`InMemoryRegisteredClientRepository` (gains `GetActiveClientIdsAsync`), a fixed `IClock`,
and the existing capturing logger pattern.

| # | Fact |
|---|---|
| U1 | An enabled device orphan and an enabled kiosk orphan, both past grace, are disabled. The outcome is `Disabled == 2`. One Warning per client names id, kind and age. One Information summary |
| U2 | A client with an active row is not disabled. A client whose only row is **disabled** **is** disabled (the #2728 re-register population) |
| U3 | Clients of kind `webhook`, of an unknown kind, and listed with no `sse.kind` (not returned by the port at all) are never disabled |
| U4 | Age exactly `GraceWindow - 1 s` is not disabled. Age `GraceWindow` is disabled (boundary) |
| U5 | An already-disabled stamped client with no row: `DisableClientAsync` is not called, and nothing is logged |
| U6 | Two orphans. The age read for the first throws. The second is still disabled, and the first is in `Unreachable` with a Warning |
| U6b | The disable for the first throws. The second is still disabled |
| U7 | `GetServiceAccountCreatedAtAsync` answers `None`: not disabled, in `Unreachable`, Warning |
| U8a | 3 orphans among 4 enabled stamped clients: nothing disabled, `Refused == true`, one Error |
| U8b | 1 orphan among 1, and 2 among 4: disabled (floor and exactly-half are below the guard) |
| U9 | `GetStampedClientsAsync` is called before `GetActiveClientIdsAsync` (S5) |
| U10 | A pass with nothing to do logs nothing |
| U11 | `OperationCanceledException` from any port propagates and is not swallowed into `Unreachable` |
| U12 | A stamped client id that `ClientId.From` rejects: not disabled, in `Unreachable`, Warning |

### 8.3 Infrastructure unit (`tests/Identity.Infrastructure.Tests/KeycloakAdmin/`)

- `StampedClientQueryTests.cs` (mirrors `EnrolledKioskQueryTests`, stub `HttpMessageHandler`):
  rows with and without `sse.kind` (only stamped rows returned). `enabled` is read. A 500
  throws.
- `ServiceAccountCreatedAtTests.cs`: a timestamp is mapped. A client absent → `None`. A
  service-account 404 → `None`. A null `createdTimestamp` → `None`. A 500 throws.
- `OrphanedClientSweepHostedServiceTests.cs` (mirrors `KioskPrivilegeSweepBoundTests` /
  `…StartupTests`, `ManualTimeProvider`, `CapturingLogger`): **H1** `StartAsync` returns
  promptly while the pass hangs on a Keycloak that never answers. **H2** a throwing pass logs
  `OrphanedClientSweepFailed` and the service keeps ticking: advance the time provider one
  `TickInterval` and a second pass runs. **H3** stopping cancels a running pass without an
  unhandled exception.

### 8.4 Architecture (`tests/Architecture.Tests/OrphanedClientSweepRegistrationTests.cs`)

Declaration only, labelled as such in its doc exactly as `KioskPrivilegeSweepRegistrationTests`
is. `AddIdentityInfrastructure` registers `OrphanedClientSweepHostedService` as an
`IHostedService`, and `OrphanedClientSweep` resolves from a scope.

### 8.5 Existing tests

`FakeKeycloakAdminClient` and `InMemoryRegisteredClientRepository` gain members. The other
implementations — `Identity.Infrastructure.Tests/Fakes/{EnrolledKiosks,Silent,Unreachable}KeycloakAdminClient.cs`,
the inline one in `Identity.Application.Tests/Commands/DisableWebhookClientCommandHandlerTests.cs`,
and `MigrationRunner.Tests/KeycloakProvisionedFabSourceTests.cs` — get members that throw
`NotSupportedException` (or mirror the fake's existing behaviour for that file, e.g. the
unreachable one throws as its other members do). **No existing assertion changes.**

## 9. Risks

| Risk | Mitigation |
|---|---|
| `createdTimestamp` absent on 26.6.4's service-account user | I0b, a control, run first. Escalate if red. Never default to "old" (§3). |
| A wrong or restored database makes the fleet look orphaned | Guard S6, and disable (reversible) rather than delete (spec §4.1, §4.3) |
| A disable `PUT` drops `sse.kind`/`sse.fab`, so #2728's replace no longer matches | It is the same `DisableClientAsync` revocation uses, and `DisabledClientReregistrationTests` already proves revoke → re-register. I1 proves it again end to end. |
| Hourly `GET /clients` load | One listing per hour over a realm of ~250 to 500 clients. Negligible next to token traffic. |
