# Plan 304 — A conflict that names no fab

Spec: `spec.md`. Context: **Identity** (Application + Api; no Domain,
Infrastructure, Contracts or migration change).

## 1. Production code — no change expected

| Target | Current | Change |
|---|---|---|
| `src/Identity/Application/Commands/Handlers/RegisterDeviceCommandHandler.cs:46-51` | realm-global `GetByClientIdAsync` → `DeviceAlreadyRegistered(clientId.Value)` | none |
| same file `:79-82` | `KeycloakClientAlreadyExistsException` → `DeviceAlreadyRegistered(ex.ClientId)` | none |
| `src/Identity/Application/Commands/RegisterDeviceErrors.cs:9-13` | holder-neutral title/detail | none |
| `src/Identity/Infrastructure/Persistence/RegisteredClientRepository.cs:24-37` | `GetByClientIdAsync` realm-global, excludes Disabled | **must stay global** — uniqueness is a realm invariant (ADR-0159); switching it to `GetWithinFabAsync` would let fab B create a Keycloak call that 409s at Keycloak instead (`:79-82`), or worse, split the DB row from the provider |
| `src/Identity/Api/DevicesEndpoints.cs:104-183` | fab parsed → `fabGuard.EnsureAccessAsync` → handler | none; order already matches DELETE (`:205-220`) |

**Contingency (only if a characterisation test arrives red):** the fix is to
make the conflicting branch produce the same `DeviceAlreadyRegistered(clientId.Value)`
— use the caller's `clientId.Value`, not `ex.ClientId`, at `:81` if those ever
differ. No new error type, no status change. Backend engineer.

## 2. Invariants pinned

- I1: cross-fab conflict body ≡ same-fab conflict body (status, title, detail,
  type, property set; `traceId` excluded — `AddProblemDetails` adds one per request,
  `src/ServiceDefaults/AuthenticationDefaults.cs:133`).
- I2: a taken id creates nothing — no row in the caller's fab, no second Keycloak
  client; the holder's client is untouched.
- I3: the body never names the holder's fab.

## 3. Boundaries / messaging

No integration event emitted on refusal (handler returns before `SaveAsync`).
No cross-context reference. NetArchTest unaffected.

## 4. Tests (test-writer; characterisation — observed green, ADR-0139)

1. `tests/Integration.Tests/Identity/CrossFabRegistrationConflictIntegrationTests.cs` (new),
   modelled on `CrossFabDisableIntegrationTests.cs` (same principals:
   `admin@munich.test`/`Admin1234`, `op-dresden@dresden.test`/`Operator1234`;
   same `RealmProbe` Keycloak read-back; ids `t304-{Guid.CreateVersion7():N}`).
   - C1 `A_cross_fab_registration_conflict_answers_exactly_what_a_same_fab_conflict_answers` (I1, I3)
   - C2 `A_cross_fab_registration_conflict_creates_nothing_in_either_fab` (I2: dresden
     `GET /devices?fabId=dresden` lacks it; Keycloak `clients?clientId=` returns exactly
     one, `attributes["sse.fab"] == "munich"`)
   - **Add the class to `tests/Integration.Tests/ci-shards/shard-3.filter`** (same
     shard as `CrossFabDisableIntegrationTests`); a missing entry fails CI.
2. `tests/Identity.Application.Tests/Commands/RegisterDeviceCommandHandlerTests.cs` (extend):
   - U1 `A_clientId_held_by_another_fab_is_refused_with_the_same_error_as_one_held_by_the_callers_fab`
     — register in munich; then same id from dresden and again from munich; the two
     errors are record-equal; `repo.Clients` single (fab munich); `keycloak.Created` single.

Existing tests that must pass **unmodified**: `Re_registration_returns_DeviceAlreadyRegistered`,
`CrossFabDisableIntegrationTests`, `IdempotentRegistrationIntegrationTests`,
`AbsentDeviceIdentifierIsRefusedIntegrationTests`.

## 5. ADR

None needed. The decision is recorded on #2626 and the precedent (spec 180) is
existing practice. The only ADR-worthy question — removing the existence bit via
a fab-scoped clientId namespace — is explicitly out of scope (spec §6.1).
