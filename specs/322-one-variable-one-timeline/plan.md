# Plan 322: One variable, one timeline

**Spec:** [spec.md](./spec.md) · **Issue:** #2502 · **ADRs:** 0040, 0073, 0101

## 1. Bounded context and layer

**AuditObservability / Application** only — `src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs`.
No Domain, Infrastructure, Api, Shared.Contracts or frontend file changes. No cross-context reference
is introduced; the map already references `Shared.Contracts.SystemVariables`.

## 2. Mechanism

`V1ResourceMap` resolves a contract's pivot by: `Conventions.HandTweaks` first, then the namespace →
kind dictionary plus the "first `Guid` property" picker. The three contracts currently fall through
to the picker, which selects `Variable`. Add three hand-tweaks next to the existing
`SystemVariableValueRequestedV1` entry, replacing its comment with one covering all four:

```csharp
// SystemVariables addresses every variable by name (GET /system-variables/{name}), and
// SystemVariableValueRequestedV1 carries no variable guid at all, so all four variable
// contracts pivot on Name — one kind, one identifier space (spec 322, #2502).
Add<SystemVariableDefinedV1>(map, DomainResourceKind.Variable, defined => defined.Name);
Add<SystemVariableArchivedV1>(map, DomainResourceKind.Variable, archived => archived.Name);
Add<SystemVariableValueChangedV1>(map, DomainResourceKind.Variable, changed => changed.Name);
Add<SystemVariableValueRequestedV1>(map, DomainResourceKind.Variable, requested => requested.Name);
```

Entities/value objects: none new. `ResourceKind.Variable` and `ResourceIdentifier` (≤ its existing
max length; `VariableName` caps at 64) are reused. Invariant relied on: `Name` is non-null on every
published instance (publishers build it from `VariableName`), so `Identify` never yields null for these.

## 3. Messaging

Unchanged. No domain event, integration event, queue, or contract version is added or altered
(ADR-0073 not engaged — spec §3).

## 4. Tests (phase 4a, red)

In `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs`:

- Rows 17–19 (`SystemVariableArchivedCase`, `SystemVariableDefinedCase`, `SystemVariableValueChangedCase`):
  expected identifier becomes the `Name` sentinel; `MustNotBeIdentifier` becomes the `Variable` guid.
  Update the row comments (`-> variable / Name (hand-tweak)`). Keep every other field a distinct sentinel.
- New `[Fact]` `Every_variable_contract_for_one_variable_pivots_on_the_same_identifier`: build all four
  contracts for one variable (one guid G, one name N, distinct actor guids), resolve each, assert the
  set of identifiers is exactly `{N}`. This is the defect stated as a test, independent of the table.

Expected red: the three table rows (identifier is G, expected N) and the new fact (set is `{G, N}`).
`V1ResourceMapFallbackReachabilityTests` must stay green unchanged (it only constrains
convention-mapped contracts; these leave that population).

No Integration.Tests class is added → no shard-filter entry. Live proof is phase 5 (spec §8 step 2).

## 5. Boundary rules

NetArchTest boundaries unaffected. No new project reference.

## 6. Risks

- **Coverage gate** (Application ≥ 80%): three lambdas, all executed by the table test.
- **File contention:** `V1ResourceMap.Conventions.cs` and `V1ResourceMapTests.cs` are both shared with
  any concurrent audit-map change; none in flight at the time of writing.
