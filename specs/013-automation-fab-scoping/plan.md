# Implementation Plan: Automation rules belong to a fab

**Branch**: `013-automation-fab-scoping` | **Date**: 2026-08-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/013-automation-fab-scoping/spec.md`

## Summary

`Rule` gains a `Fab`, and every path that selects a rule — evaluation, cache
lookup, reads, and the mutating commands — is narrowed to it.

The two halves land in one slice because they share a root cause, but they
are sequenced so the unattended defect (#1252) is closed first: the domain
field, the migration, and the cache/evaluator scoping are enough to stop
cross-fab firing without touching authorization at all. The endpoint guard
and read filtering follow on top.

The one design decision that is not a straight application of existing
patterns is inferring the fab from the caller when they belong to exactly
one. It contradicts a documented position and gets ADR-0114.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs, EF Core (Npgsql),
Wolverine, .NET Aspire

**Storage**: PostgreSQL — `automation` database, `rules` table

**Testing**: xUnit + Shouldly + Moq; hand-written fakes; integration via
`AspireFixture` (ADR-0103); Playwright for e2e

**Target Platform**: Linux containers under k3s (prod), Aspire AppHost (dev)

**Project Type**: Web service within a modular monolith of bounded contexts,
plus two React SPAs

**Performance Goals**: Rule selection must remain a single dictionary lookup;
its cost must not grow with the number of rules in other fabs (SC-007)

**Constraints**: On the event-to-overlay path — the automation leg sits
inside the ≤ 200 ms *event → overlay state* budget, so selection must not
gain a scan. 24/7 operation: the migration runs against a live database with
rules already active.

**Scale/Scope**: ~10 files in `src/Automation`, 1 EF migration, 1 shared
helper promoted in `ServiceDefaults`, 1 ADR, plus test updates across the
Automation unit suite, two integration suites and the rules e2e.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **II. DDD with value objects** | PASS — `Fab` is a `FabIdentifier` value object, not a `string`. It does not cross a boundary as a primitive. |
| **III. Bounded context isolation** | PASS — Automation defines its own `FabIdentifier` rather than referencing Identity's (R1). No new cross-context project reference. |
| **IV. The latency budget is sacred** | PASS — the affected leg is *event → overlay state* (≤ 200 ms). Keying the cache on `(fab, source, kind)` keeps selection O(1); the rejected alternative (filter after lookup) would have degraded it (R3). |
| **V. Spec-driven development** | PASS — spec written and gated before this plan; no code yet. |
| **VII. Observability** | PASS — refusals use the existing `RESOURCE_FAB_NOT_AUTHORIZED` problem shape; no new log formats. |
| **VIII. Safe by default at trust boundaries** | PASS — this *adds* a trust-boundary check where none existed. Inference (FR-008) applies only after the caller's assignments are read from a validated token. |
| **Testing (ADR-0052/0103)** | PASS — unit via fakes, integration via `AspireFixture`, e2e via Playwright. |
| **Coverage gates (ADR-0065)** | WATCH — `Automation.Domain` currently sits at 90.8% against a 90% gate, the tightest margin in the solution. Adding a VO and an aggregate field without matching tests would breach it. Tasks must add `FabIdentifier` tests alongside. |

No violations requiring justification. The Complexity Tracking table is
therefore omitted.

**Post-design re-check**: unchanged. The design adds one value object, one
aggregate field, one widened cache key, one promoted helper and one ADR — no
new projects, no new abstractions, no speculative generality.

## Project Structure

### Documentation (this feature)

```text
specs/013-automation-fab-scoping/
├── plan.md              # This file
├── spec.md              # Phase 1 gate output
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── rules-api.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Automation/
├── Domain/Rule/
│   ├── FabIdentifier.cs                 # NEW — per-context VO (R1)
│   ├── Rule.cs                          # + Fab; Create takes it
│   └── IRuleRepository.cs               # GetByNameAsync gains fab
├── Application/
│   ├── Commands/                        # Create/Publish/Archive carry Fab
│   ├── Queries/                         # List/Get/DryRun carry Fab
│   └── Evaluation/
│       ├── IRuleCache.cs                # LookupActive(fab, source, kind)
│       ├── RuleEvaluator.cs             # threads fab through
│       └── EventHandlers/
│           └── FabEventIngestedV1Handler.cs   # passes message.Fab
├── Infrastructure/
│   ├── Cache/InMemoryRuleCache.cs       # key becomes a triple
│   ├── Persistence/
│   │   ├── Configurations/RuleConfiguration.cs   # fab column, index swap
│   │   └── Migrations/                  # NEW migration
│   └── AutomationInfrastructureModule.cs
└── Api/
    ├── RulesEndpoints.cs                # guard + fab resolution
    └── RulesEndpoints.*.cs

src/ServiceDefaults/Authorization/
└── FabClaims.cs                         # NEW — promoted ExtractFabSet (R2)

src/AuditObservability/Api/AuditEndpoints.cs   # uses the promoted helper

docs/adr/0114-fab-inferred-for-single-fab-operators.md   # NEW (R5)

tests/
├── Automation.Domain.Tests/Rule/        # FabIdentifier tests; RuleBuilder.WithFab
├── Automation.Application.Tests/        # handler + evaluator fab cases
└── Integration.Tests/Automation/        # cross-fab isolation, read filtering

e2e/rules.spec.ts                        # authoring picks up the operator's fab
```

**Structure Decision**: Existing per-context layout under `src/Automation`,
unchanged. The only file outside that context is the promoted claims helper in
`ServiceDefaults` (R2) and its adoption in `AuditObservability`, both of which
remove duplication rather than adding structure.

## Implementation Sequencing

The order matters and is not the order the spec lists the stories in.

1. **`FabIdentifier` + `Rule.Fab` + migration.** Nothing else can proceed
   without a fab on the aggregate. The migration is written and applied here
   so subsequent steps run against the real shape.
2. **Cache and evaluator scoping.** Closes #1252. At the end of this step the
   unattended defect is gone, with no authorization work done yet — this is
   the point at which the branch is already worth shipping.
3. **Repository and command/query threading.** `GetByNameAsync` takes a fab;
   the fab check precedes the `If-Match` comparison in each handler (R6).
4. **Endpoint guard and fab resolution.** Infer-or-require, plus
   `EnsureAccessAsync` on every rule endpoint including dry-run.
5. **Read filtering.** List and get narrowed to the caller's fabs, with the
   404-not-403 shape required by FR-007.
6. **ADR-0114** and the correction to `IFabAuthorizationGuard`'s doc comment.
7. **Test updates** across unit, integration and e2e.

Steps 1–2 satisfy User Story 1 alone. Steps 3–5 satisfy Stories 2 and 3.
Step 5 also delivers Story 4 implicitly via the uniqueness change in step 1.

## Risks

| Risk | Mitigation |
|---|---|
| Migration runs against live rules | Three-step column addition; index swap inside one migration so uniqueness is never absent (R4) |
| `Automation.Domain` coverage is 90.8% against a 90% gate | `FabIdentifier` tests land with the VO, not after |
| Existence leak across fabs via status codes | Fab refusal precedes the version check and returns the not-found shape (R6, FR-007) |
| Cache seeder rebuilds without a fab | `RuleCacheSeederHostedService` reads `Rule.Fab`; covered by the cross-fab integration test |
| Inference surprises multi-fab operators | Refuse with an explicit message rather than guessing (FR-009) |
