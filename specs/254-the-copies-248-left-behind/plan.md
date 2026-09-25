# Plan 254 — The copies 248 left behind

**Spec**: [spec.md](spec.md) · **Issue**: #2589 · **Extends**: spec 248 (#2466)

## Constitution / ADR check

| Gate | Result |
|---|---|
| Bounded context / layers | None — `tests/Integration.Tests` only. No domain, no contract, no cross-context reference added (reflection reads types already in the test assembly). |
| §II primitives | N/A — no domain model. |
| §IV latency | N/A — no leg touched. |
| §Testing | Guard: new behaviour, red by counterfactual (spec §5). D3's two const lifts: behaviour-preserving by construction (IL-identical `ldstr`), separate `refactor(test):` commit. |
| ADR-0103 | No Testcontainers; AppHost composition only (`CreateAsync`, no resources started). |
| ADR-0084 | Partial-class split keeps each file under 300 LOC. |
| ADR-0105 | `Ensure.That` not needed — no argument guards in test helpers; failures are `InvalidOperationException` with a message, matching the existing helpers. |
| New ADR | No — spec 248's recorded design, extended by rows. |

## Files

| File | Change |
|---|---|
| `tests/Integration.Tests/Identity/RealmImportMirrorTests.cs` | `public class` → `public partial class`. Nothing else. |
| `tests/Integration.Tests/Identity/RealmImportMirrorTests.ProductionDefaults.cs` | new — P1-P5 `[Theory]` + J `[Fact]` (spec §4.1). |
| `tests/Integration.Tests/Identity/RealmImportMirrorTests.IntegrationTestCopies.cs` | new — theories A and B over T1-T16 + a private `ConstantOf(Type, string)` reflection helper (spec §4.2, D3). |
| `tests/Integration.Tests/Identity/FabGroupClaimIntegrationTests.cs` | lift `:178-179` literals to `SimulatorClientId` / `SimulatorClientSecret`. |
| `tests/Integration.Tests/StreamDistribution/StreamFabAttributionIntegrationTests.cs` | lift `:302-303` literals to `AttributionClientIdentifier` / `AttributionClientSecret`. |

Failure messages follow the existing facts' shape: name the copy (type + field, or parameter, or
`AddBearerAuthentication`'s authority), both values, `FullRealmImportPath()`, and the
"delete the Keycloak volume" hint when the realm file is what changed.

## Order

1. Lift commit (T001) — builds on its own.
2. Guard commit (T002-T004) — builds on its own; green on the honest tree.
3. Counterfactuals (T005) — no commit; output quoted in the PR.
