# Plan 180 — A disable that checks the fab

**Spec:** `specs/180-a-disable-that-checks-the-fab/spec.md`
**Issue:** #2240

---

## 1. Bounded context and layers

One context: **Identity**. No cross-context project reference is added; nothing
new goes into `Shared.Contracts`. `IFabAuthorizationGuard` already lives in
`ServiceDefaults`, which every context's Api references today — Identity's Api
already injects it into four other handlers in these same two files.

| Layer | Change |
|---|---|
| **Domain** | `IRegisteredClientRepository` gains one method. **No aggregate change** — `RegisteredClient.Fab` already exists (`RegisteredClient.cs:25`) and is already a `FabIdentifier`. No new value object. |
| **Application** | `DisableKioskCommand` / `DisableDeviceCommand` each gain a `FabIdentifier Fab` member. Both handlers switch their lookup to the fab-scoped one. No new error variant. |
| **Infrastructure** | `RegisteredClientRepository` implements the new method. **No migration** — `fab` is an existing, populated column. |
| **Api** | Both `Disable` handlers gain `ClaimsPrincipal user`, `IFabAuthorizationGuard fabGuard` and a required `[FromQuery] string fabId`, parse the fab, call the guard, and pass the `FabIdentifier` into the command. The two stale route-table comments are corrected. |

## 2. The fix shape, confirmed against existing call sites

Measured on `develop`, 2026-09-18, excluding `src/ServiceDefaults/` itself:

- **9** direct `fabGuard.EnsureAccessAsync(...)` calls — 3 in `AuditEndpoints`,
  2 in `KiosksEndpoints`, 2 in `DevicesEndpoints`, 2 in `WebhookRotationEndpoints`.
- **28** endpoint-level calls into per-context wrappers
  (`ResolveWriteFabAsync` / `ResolveReadFabsAsync` and their siblings), whose
  **11** bodies call `FabResolution.Resolve*`, which calls the guard.
- **37** endpoint-level guard invocations in total, across **8** of 9 contexts.

The issue says 29; the nearest measured figure is the 28 wrapper call sites, so
"29" was a count of one of these families rather than of all of them. Recorded
because a number nobody re-measures is how §IV's leg table drifted.

The call sites split into **two shapes**, and the difference is exactly the
open question this plan had to settle.

### Shape A — the fab comes from the request

`KiosksEndpoints.Enroll:147`, `DevicesEndpoints.Register:145`,
`WebhookRotationEndpoints:141`, both `List` handlers, `AuditEndpoints:78,113`:

```csharp
fab = FabIdentifier.From(fabId);                       // parse, 400 on failure
await fabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken);   // 403
```

### Shape B — the target is materialised first, then guarded against its fab

`AuditEndpoints.GetSingle:183` — the query runs, then
`await fabGuard.EnsureAccessAsync(user, row.Fab, cancellationToken)`.

### Shape C — the fab is named by the caller *and* put in the lookup predicate

`CameraEndpoints.Retire:204-225` + `RetireCameraCommandHandler:23` +
`ICameraRepository.GetWithinFabAsync`. The interface's own doc comment says why
it is a separate read rather than a check after a by-identifier load:

> *"so the guarantee is structural: a caller cannot forget the check, and
> another plant's camera is never loaded in the first place"* (#1397)

### Which one applies here, and why

**Shape C.** Shape A alone is insufficient and Shape B alone is wrong:

- **Shape A alone fails AS-3.** The attacker names *their own* fab, the guard
  passes honestly, and `GetByClientIdAsync` still finds the victim's client. The
  guard would be decoration. This is the single most important conclusion in
  this plan and the reason the fix touches the handlers at all.
- **Shape B alone gives the wrong status and the wrong shape.** Guarding against
  the target's own fab answers **403** for another plant's kiosk, which confirms
  it exists — the enumeration `CameraEndpoints.cs:104-106` deliberately refuses
  to allow. It also means the *victim's* fab, not the caller's stated intent,
  decides what is checked.
- **Shape C gives both answers correctly**: 403 when you name a fab you do not
  hold (AS-2), 404 when you name one you do hold and the target is not in it
  (AS-3) — indistinguishable from AS-6's "no such client".

`fabId` is **required**, not inferred. `FabResolution.ResolveForWriteAsync` is
deliberately not used: ADR-0114 scopes inference to Automation's rule endpoints,
and `FabResolution`'s own doc comment calls using it elsewhere *"a new decision
rather than an application of that one"*. The autonomous lane may not make
decisions (ADR-0144). The two sibling routes in these same files already take a
required `fabId`, so this is the consistent choice as well as the permitted one.

## 3. Entities, value objects, invariants

Nothing new. What the change relies on, all pre-existing:

| Type | Where | Relevance |
|---|---|---|
| `RegisteredClient` (aggregate) | `src/Identity/Domain/RegisteredClient/RegisteredClient.cs` | already carries `Fab` (`FabIdentifier`), `Kind`, `DisabledAt` |
| `FabIdentifier` | `src/Identity/Domain/RegisteredClient/FabIdentifier.cs` | Identity's own, per ADR-0044 — no shared type is introduced |
| `ClientId` | same folder | unchanged |
| `ClientKind` | same folder | unchanged; the kind check stays and is still checked first |

**Invariant made structural:** *a registered client may be disabled only through
a request that names the fab it belongs to, by a caller who holds that fab.*
Before this change the invariant was stated in prose in constitution §VIII and
enforced nowhere on these two routes.

**Invariant deliberately preserved:** `GetByClientIdAsync` ignores rows with
`DisabledAt != null`, which is what releases a `clientId` for re-registration.
The new method must keep that `Where` clause — dropping it would make a second
disable of an already-disabled client succeed instead of 404, changing behaviour
this issue does not authorise.

## 4. Domain and integration events

**Unchanged.** `RegisteredClient.Disable(clock)` already raises its domain event
and `SaveAsync` already dispatches into the outbox before commit
(`RegisteredClientRepository.cs:53-69`). A refused request never reaches
`Disable`, so it raises nothing — which is correct: a refusal is not an event.

No `Shared.Contracts` message changes. No `V<N>` bump.

## 5. Boundary rules

- No cross-context project reference is added; NetArchTest's existing rules are
  unaffected.
- `ServiceDefaults` is not modified. `IFabAuthorizationGuard` and
  `FabAuthorizationException` are used exactly as the other 37 sites use them,
  including the global 403 mapping in `FabAuthorizationExceptionHandler`.
- `FabIdentifier` stays per-context. `EnsureAccessAsync` takes a `string`
  precisely so `ServiceDefaults` need not reference any context's VO; the call
  passes `fab.Value`, as every other site does.
- Domain stays pure: the guard is called in Api, never in Application or Domain.
  The command carries an already-authorised `FabIdentifier`.

## 6. Ordering, statuses, and the one subtlety

The endpoint must run its checks in this order, and the order is load-bearing:

1. **Scope policy** (already, via `RequireAuthorization` on the group) → 401/403.
2. **Parse `fabId`** → 400 `KIOSK_INVALID_INPUT` / `DEVICE_INVALID_INPUT`.
3. **`EnsureAccessAsync`** → 403 `RESOURCE_FAB_NOT_AUTHORIZED`.
4. **Parse `clientId`** → 400.
5. **Handler**, fab-scoped lookup → 404 / 200 / 502.

Steps 2-4 are the ordering `CameraEndpoints.Patch:243-249` states explicitly:
*"Fab first — before If-Match is read, before the body is parsed… Answering 428
or 400 for another fab's camera would confirm that camera exists."* The same
reasoning applies to `clientId` parsing here, which is why step 4 sits below
step 3 rather than above it as it does today.

**409 is untouched.** `Architecture.Tests/ConcurrencyConflictDeclarationTests`
registers `Identity DELETE /kiosks/{clientId}` and
`Identity DELETE /devices/{clientId}` as *"lost update ONLY"*, keyed by route
**path**. Adding a query parameter does not change the path and adding no new
`HttpStatusCode.Conflict` variant does not change the classification, so that
register needs no edit. Phase 4 runs `Architecture.Tests` to confirm rather than
assume.

## 7. Repository method

```csharp
// IRegisteredClientRepository
Task<Option<RegisteredClient>> GetWithinFabAsync(
    ClientId clientId, FabIdentifier fab, CancellationToken cancellationToken);
```

Named `GetWithinFabAsync` to match `ICameraRepository` exactly (ADR-0091: no
shortcuts, and no near-synonym for an established name). Implementation mirrors
`CameraRepository.GetWithinFabAsync:21-33` — fab in the predicate, plus the
existing `DisabledAt == null` clause from `GetByClientIdAsync`.

`GetByClientIdAsync` is **kept**: `EnrollKioskCommandHandler` and
`RegisterDeviceCommandHandler` use it for the duplicate check, where the question
genuinely is global (`ux_registered_clients_clientid_active` is not fab-scoped).
Removing or narrowing it is a different change with a different blast radius.

## 8. Test plan (phase 4a writes these; colour declared in `tasks.md`)

### Integration — the counterfactual that carries the whole claim

New: `tests/Integration.Tests/Identity/CrossFabDisableIntegrationTests.cs`,
modelled on `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`
and using the same fixture helpers.

Each test enrols its own client over HTTP (registration mints a **real** Keycloak
client, so rows must not be wiped — the reasoning
`IdempotentRegistrationIntegrationTests` records at its head applies unchanged).

| # | Test | Before fix | After fix |
|---|---|---|---|
| I1 | A dresden operator naming munich cannot disable a munich kiosk | **200** — the attack succeeds | 403 `RESOURCE_FAB_NOT_AUTHORIZED` |
| I2 | A dresden operator naming dresden cannot disable a munich kiosk | **200** — the attack succeeds | 404 `KIOSK_NOT_FOUND` |
| I3 | Same two, for `/devices` | **200**, **200** | 403, 404 |
| I4 | A munich operator disables a munich kiosk | 200 | 200 (unchanged) |
| I5 | The victim's Keycloak client is still enabled after I1/I2 | **fails** — it is disabled | passes |

I5 is the assertion that makes I1/I2 more than a status-code check. Read back
through the Keycloak Admin API, not from our own row: our row is what spec 121
proved can disagree with Keycloak.

Tokens: `aspire.CreateAuthenticatedClientAsync("identity", "op-dresden@dresden.test",
"Operator1234")` and `"admin@munich.test"` / `"Admin1234"`. Both go through
`smart-sentinel-eye-web` with `openid sse.management`, which the legacy bundle
accepts for `sse.identity.kiosks.write` — so **no fixture change is needed**, and
the attacker principal is an ordinary seeded operator rather than one invented
for the test.

### Unit — Application

Extend `tests/Identity.Application.Tests/Commands/DisableKioskCommandHandlerTests.cs`
and `DisableDeviceCommandHandlerTests.cs` (both already exist, 3 cases each; all
6 constructions need the new command member):

- a client in another fab is `KioskNotFound` / `DeviceNotFound`;
- a client in the named fab still disables;
- the kind check still refuses a device through the kiosk route, and the
  assertion names *which* check refused it.

### Unit — Infrastructure

`tests/Identity.Infrastructure.Tests` — `GetWithinFabAsync` returns `None` for a
row in another fab and `None` for a disabled row in the right fab.

### Not written

No e2e spec: neither route has a UI. No new architecture test — a "every write
endpoint guards a fab" rule would be worth having and is **out of scope**; it
belongs with #2280/#2281, which are the other members of the same defect class.

## 9. Risks

| Risk | Mitigation |
|---|---|
| `fabId` becomes required and silently breaks a caller | Measured: zero callers outside backend tests (spec §7 A2). The three backend test files that call these routes are listed in `tasks.md`. |
| The guard is added but the lookup is not, closing AS-2 and leaving AS-3 | I2 exists precisely to catch this, and it is the test that must be watched red. |
| A test is written that never observed the vulnerability | Phase 4a runs I1-I3 against unfixed `src/` and quotes the output. A green-on-arrival security test is a phase-4 failure (ADR-0139). |
| The stale route-table comments survive the change | SC-004, and a named task. |
| `Architecture.Tests` route register goes stale | Path unchanged; phase 4 runs the suite rather than reasoning about it. |
