# Plan 386: Named-fab list reads refused end to end on /devices and /webhook-integrations

**Spec**: [spec.md](./spec.md). **Issue**: #3001.

## 1. Context and layers

Bounded context: **Identity**, Api layer only, observed through the real Aspire stack
(ADR-0103). Nothing is built: no entity, value object, invariant, domain or integration event, and
no boundary rule is touched. The tests talk HTTP to the `identity` resource like every other test in
the class.

Production path under test (unchanged):

- `src/Identity/Api/DevicesEndpoints.cs` `List` and `src/Identity/Api/WebhookRotationEndpoints.cs`
  `List` each call `IdentityFabResolution.ResolveReadFabsAsync(new FabReadRequest(user, fabId), fabGuard, ...)`.
- `src/ServiceDefaults/Authorization/FabResolution.cs` `ResolveForReadAsync`: a non-blank `fabId`
  calls `fabGuard.EnsureAccessAsync(user, fabId, ...)` (line 105), which throws
  `FabAuthorizationException`; `FabAuthorizationExceptionHandler` maps it to 403 with
  `title = "RESOURCE_FAB_NOT_AUTHORIZED"`.
- Query parameter name on both routes: `fabId` (`[FromQuery] string fabId = ""`).

## 2. The facts (file `tests/Integration.Tests/Identity/CrossFabListIntegrationTests.cs`)

Placed after the existing I6, reusing the class's private helpers (`MunichClientAsync`,
`DresdenClientAsync`, `RegisterDeviceAsync`, `CreateWebhookIntegrationAsync`, `ListAsync`,
`ClientIds`, `DistinctFabs`, `ProblemTitleAsync`). Existing I5/I6 names stay as they are (renaming
would be churn).

| # | Name | Arrange | Act | Assert |
|---|---|---|---|---|
| I5-devices | `A_munich_operator_naming_dresden_on_devices_is_still_refused` | munich client | `GET /devices?fabId=dresden` | 403 (message includes status + body, as I5); `ProblemTitleAsync == "RESOURCE_FAB_NOT_AUTHORIZED"` |
| I5-webhooks | `A_munich_operator_naming_dresden_on_webhook_integrations_is_still_refused` | munich client | `GET /webhook-integrations?fabId=dresden` | as above |
| I6-devices | `A_caller_holding_munich_and_dresden_naming_munich_on_devices_sees_only_munich_devices` | `RegisterDeviceAsync(munich, "munich")`, `RegisterDeviceAsync(dresden, "dresden")` | `ListAsync(multi, "/devices?fabId=munich")` (multi = op-multi, holds munich and dresden) | contains munich id; does **not** contain dresden id; `DistinctFabs == ["munich"]` |
| I6-webhooks | `A_caller_holding_munich_and_dresden_naming_munich_on_webhook_integrations_sees_only_munich_integrations` | `CreateWebhookIntegrationAsync` x2, same shape | `ListAsync(multi, "/webhook-integrations?fabId=munich")` | as above |

The I6 pair adds a `ShouldNotContain(dresdenId)` the kiosk I6 lacks: with the dresden row created in
the same test, it fails by construction if the named fab stops narrowing, independent of what other
tests left behind. The class `<summary>` paragraph on I5/I6 is rewritten to say they now exist per
route (spec 386) and why (each route makes its own call into the shared resolution).

**No new test class**, so no shard-filter entry: `shard-N.filter` selects by
`FullyQualifiedName~<Namespace>.<Class>.`, and
`SmartSentinelEye.Integration.Tests.Identity.CrossFabListIntegrationTests.` is already in
`tests/Integration.Tests/ci-shards/shard-2.filter`; `integration-shard-coverage` stays satisfied.

## 3. Phase 4a: characterisation, and how the lane knows they bite

Colour: **characterisation** (existing behaviour, expected green). It cannot be observed green
locally (no Docker); CI shard 2 is the first observation and the PR quotes its result from the
downloaded TRX/log, not from the run's badge.

Proof the assertions can fail, without Docker:

1. **By reading.** Spec 382 counterfactual (2), passing `string.Empty` instead of `fabId` into the
   resolution, turned the in-process guard-refusal and narrowing cases red for all three routes
   (`tests/Architecture.Tests/DeviceAndKioskEndpointCharacterisationTests.cs`,
   `WebhookRotationEndpointCharacterisationTests.cs`). The new facts observe the same branch through
   HTTP; the only extra pieces end to end are the real guard (unit-tested by `FabResolutionTests`)
   and the global 403 mapping (already exercised by kiosk I5).
2. **By a CI counterfactual (recommended: run it once).** Throwaway branch
   `throwaway/3001-counterfactual` off the test commit, one commit that, in **both**
   `DevicesEndpoints.List` and `WebhookRotationEndpoints.List`, replaces
   `new FabReadRequest(user, fabId)` with `new FabReadRequest(user, string.Empty)`, which skips
   `fabGuard.EnsureAccessAsync` for those two routes only. Dispatch with
   `gh workflow run ci.yml --ref throwaway/3001-counterfactual` (no PR). Expected in shard 2:
   **exactly the two new I5 facts and the two new I6 facts red** (I5: 403 expected, 200 got); kiosk I5 green (control: the shared
   guard and mapping still work); the new I6 pair red too (the fab-ignored counterfactual returns the dresden row to the
   multi-fab caller, so `ShouldNotContain(dresden)` and `DistinctFabs` both fail). Any other red in shard 2 means
   the counterfactual is not isolated: report, do not interpret. Delete the branch afterwards.
   Removing the guard line in `FabResolution.cs:105` instead is **not** recommended: it reaches
   every context's reads and reds other shards, which proves nothing more about these two routes.
   Cost: one full CI run (about 25 min wall clock); worth it because the new facts otherwise have no
   observed red anywhere.

## 4. Commands

Local (Docker not required): `dotnet build tests/Integration.Tests -c Release` (analyzers, IDE0007
`var`, collection expressions); `dotnet format --verify-no-changes` scoped to the file. Stop any
AppHost first (MSB3027).

CI only: shard 2 of `integration-shards` running the class; `integration-shard-coverage`.

## 5. Risks

- **Flake: Keycloak client minting.** Each I6 fact mints two real Keycloak clients (registration /
  rotation), the existing pattern of I2/I3/I4. No new kind of call.
- **Flake: token minting.** Callers use the fixture's ROPC token cache per username; munich and
  dresden tokens already mint in this class.
- **Ordering.** xUnit runs the collection's tests sequentially in no fixed order and rows are never
  wiped; every assertion is on ids the test created itself or on the fab set under an explicit
  `fabId`, so order does not matter. The 403 facts arrange nothing.
