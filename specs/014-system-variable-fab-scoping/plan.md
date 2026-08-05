# Implementation Plan: Fab-scope system variables

**Branch**: `014-system-variable-fab-scoping` | **Date**: 2026-08-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/014-system-variable-fab-scoping/spec.md`

## Summary

Give `Variable` a fab, make its name unique per fab rather than globally, and
carry that fab through the two places a value is looked up: the value-change
consumer and the overlay reverse index. The fab already travels on the wire —
Automation stamps it on every request it publishes — so this is about giving
the receiving side somewhere to put it, not adding information to the system.

The work splits into three slices that ship in order, because the third lands
on code with no tests and no latency baseline:

1. **Model and migration** — `Fab` on the aggregate, `(fab, name)` uniqueness,
   backfill that announces itself.
2. **Boundaries** — the value-change consumer resolves `(fab, name)`, the dedup
   key gains the fab, the endpoints get the guard.
3. **Resolution** — the reverse index keys on `(fab, name)`, preceded by the
   tests and the measurement that should already exist.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: EF Core + Npgsql (persistence), Wolverine (the
value-change subscriber), ASP.NET Core Minimal APIs, SignalR (the live push to
kiosks)

**Storage**: PostgreSQL — `system-variables-db`

**Testing**: xUnit + Shouldly + Moq; hand-written fakes; integration against
the real Aspire stack via `AspireFixture` (ADR-0103); Playwright for the kiosk

**Target Platform**: Linux containers under Aspire (dev) / k3s (prod)

**Project Type**: Backend bounded context + a kiosk-facing read path

**Performance Goals**: The event → overlay leg stays within **200 ms**
(constitution §IV). Resolution is inside that leg.

**Constraints**: No cross-context project references — SystemVariables and
Automation communicate only through `Shared.Contracts` (§III). The wire
contract does not change.

**Scale/Scope**: One aggregate gains a field; two lookup keys gain a
dimension; five endpoints gain a guard; one migration.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **II. DDD with value objects** | PASS. `Fab` is a `FabIdentifier` value object, not a string. Local to the context per ADR-0044, mirroring the three that exist. |
| **III. Bounded context isolation** | PASS, and it is the constraint that shaped the design. Refusal happens where the value is applied, not where the rule is authored, precisely because validating at authoring would require Automation to call SystemVariables synchronously. No new cross-context reference; the contract is unchanged. |
| **IV. The latency budget is sacred** | **AT RISK — see Complexity Tracking.** Resolution sits inside the 200 ms event → overlay leg, and there is no existing measurement of that leg to compare against. Mitigated by taking the baseline before changing the key. |
| **VII. Observability** | PASS, and load-bearing. FR-005/FR-006 exist because a silently dropped value change is indistinguishable from a rule that correctly did not match — the shape #1252 hid behind. |
| **VIII. Safe by default at trust boundaries** | PASS. The context is currently unguarded; this adds the guard at the boundary, reusing `FabResolution` rather than re-deriving it. |
| **V. Spec-driven** | PASS. Spec, plan, tasks, then code. ADR-0114 amended as part of the work rather than after. |

**Re-check after Phase 1**: no change. The design added no cross-context
dependency and no new resolution mechanism, and the latency risk is unchanged —
it is managed by sequencing, not removed.

## Project Structure

### Documentation (this feature)

```text
specs/014-system-variable-fab-scoping/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions, and two findings that reshaped this plan
├── data-model.md        # Phase 1 — the aggregate, migration, keys
├── quickstart.md        # Phase 1 — how to see it by hand
├── contracts/
│   └── system-variables-api.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 — NOT created by /speckit-plan
```

### Source Code (repository root)

```text
src/SystemVariables/
├── Domain/Variable/
│   ├── FabIdentifier.cs                    # new — mirrors the other three
│   └── Variable.cs                         # Fab, required by Define
├── Application/
│   ├── Commands/                           # fab threaded into define/set/archive
│   ├── Queries/                            # fab threaded into list/get/snapshot
│   ├── EventHandlers/
│   │   ├── SystemVariableValueRequestedV1Handler.cs   # reads Metadata.Fab
│   │   ├── IVariableValueRequestDedupStore.cs         # fab in the key
│   │   └── VariableValueChangedDomainEventHandler.cs  # fab-scoped fan-out
│   ├── Resolution/IReverseIndex.cs         # keyed on (fab, name)
│   └── Log.cs                              # distinct message for a cross-fab miss
├── Infrastructure/
│   ├── Persistence/
│   │   ├── Configurations/VariableConfiguration.cs    # (fab, name) partial unique
│   │   ├── VariableValueRequestDedupStore.cs
│   │   └── Migrations/                                # add → backfill+warn → NOT NULL → swap
│   └── Resolution/
│       ├── InMemoryReverseIndex.cs
│       └── ReverseIndexSeederHostedService.cs         # records each overlay's fab
└── Api/SystemVariableEndpoints.cs          # FabResolution on all five

src/ServiceDefaults/Authorization/          # unchanged — reused as-is

tests/
├── SystemVariables.Domain.Tests/
├── SystemVariables.Application.Tests/
├── SystemVariables.Infrastructure.Tests/   # NEW — the shipped reverse index has no test today
└── Integration.Tests/SystemVariables/      # cross-fab, resolution, and the NFR baseline

docs/adr/0114-fab-inferred-for-single-fab-operators.md   # amended, not superseded
```

**Structure Decision**: The existing per-context layout, unchanged. One new
test project, `SystemVariables.Infrastructure.Tests`, for the same reason
`Automation.Infrastructure.Tests` was added in spec 013 — the shipped
`InMemoryReverseIndex` is currently verified only against a hand-written double,
and this feature changes its key.

## Delivery order

Ordering is not cosmetic here. Slice 3 changes a component that has no tests,
on a path that has no baseline.

| # | Slice | Delivers | Gate before starting |
|---|---|---|---|
| 1 | Model + migration | US1's storage half. Two fabs can hold the same name. | — |
| 2 | Boundaries | US1 complete, US3, US4, US5. The defect is closed for stored values. | Slice 1 merged |
| 3 | Resolution | US2. The screen agrees with the store. | **Baseline + shipped-class tests exist** |

Slices 1 and 2 are independently shippable and close the data half of #1310.
Slice 3 is where the latency risk lives and is deliberately last, so the
measurement can be taken against code that is otherwise final.

**Do not reorder so that resolution lands first.** It is the visible half, but
the invisible half is the one corrupting data.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| §IV: changing code inside the 200 ms event → overlay leg with no existing measurement of that leg | US2 is unachievable without it — a munich kiosk resolves a dresden variable until the index is fab-keyed | *Leave resolution global and ship slices 1–2 only*: stored values would be correct and screens still wrong, which is worse than an obvious failure. *Take the baseline after the change*: measures the new code against itself and passes trivially. So the baseline is established first, as the opening task of slice 3 — work that #749 already calls for and that the product's load-bearing NFR has been running without. |
| A fourth `FabIdentifier` value object | ADR-0044: value objects are not shared across contexts, and the boundary tests enforce it | *Promote one to `Shared.Kernel`*: breaks the boundary rule and the architecture tests. The grammar must match the other three exactly, which is a test, not a shared type. |
| A new test project for one class | The shipped `InMemoryReverseIndex` has no test, and this feature changes its key | *Test it from `SystemVariables.Application.Tests`*: would drag EF Core and Wolverine into a pure unit-test project, the same trade rejected in spec 013. *Rely on the existing fake*: that is the finding, not the fix. |
