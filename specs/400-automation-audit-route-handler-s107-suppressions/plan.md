# Plan 400: the Automation and AuditObservability route handlers carry ADR-0084's S107 suppression

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2974.

Written for the recommended options **D-1a** and **D-2a** (spec §6). Both must be confirmed before phase 4.

## Constitution and ADR check

| Check | Result |
|---|---|
| ADR-0084 (2026-10-16 amendment, accepted) | Applies the adopted mechanism verbatim to the 8 handler hits; helpers stay subject (none is a hit). |
| ADR-0144 (no weakened gate) | The owner authorised this suppression on #2974 (2026-10-17). No threshold, analyzer, `.editorconfig` or existing-test change. The one new test *adds* a gate. |
| §Testing / phase 4a | Behaviour-preserving: characterisation observed green before, unmodified after. The new baseline is captured on unchanged code before any `src/` edit. |
| §II (no primitives on the domain model) | Not touched — Api layer and a test. |
| No cross-context references | None added. Architecture.Tests already references both Api projects. |
| §IV latency | N/A. |

## Bounded contexts and layers

Automation and AuditObservability, **Api layer only**, plus one Architecture test.

| File | Change |
|---|---|
| `tests/Architecture.Tests/AuditObservabilityRouteTableCharacterisationTests.cs` (new, D-1a) | Literal route-table baseline for `MapAuditEndpoints` (3 routes). |
| `src/Automation/Api/RulesEndpoints.Actions.cs` (new, D-2a) | `public static partial class RulesEndpoints` holding the four `Build*` helpers moved verbatim. |
| `src/Automation/Api/RulesEndpoints.cs` | Helpers removed (D-2a); `using`; attribute on `Create`, `Publish`, `Archive`. |
| `src/Automation/Api/RulesEndpoints.Reads.cs` | `using`; attribute on `List`, `GetOne`, `DryRun`. |
| `src/AuditObservability/Api/AuditEndpoints.cs` | `using`; attribute on `Search`, `GetTimeline`. |

No entities, value objects, invariants, messages or migrations. No Shared.Kernel / Shared.Contracts /
AppHost change, so there is no foundational task.

## Design

### The new baseline (D-1a)

Mirror `SystemVariablesRouteTableCharacterisationTests` exactly (the smallest existing shape; no host class):

```csharp
[Fact]
public async Task The_audit_route_table_is_unchanged()
{
    await using var app = WebApplication.CreateBuilder([]).Build();
    app.MapAuditEndpoints();

    var rendered = RouteTableRendering.RenderAll(InProcessEndpointDriver.Endpoints(app));

    rendered.ShouldBe(RouteTableRendering.Normalise(Baseline));
}
```

Plus a route count fact (`ShouldBe(3)`) as the Automation file has. The literal is captured by running the
renderer on `f950cb7c` and pasting its output; the XML doc says so and names the SHA. It must contain, for
`Search`, `GetTimeline` and `GetSingle`, a `handler Name(...)` line with every parameter, which is what
makes the justification true. Nothing in the doc comment may claim more than that (no OpenAPI or
binding-failure coverage; spec 383's open gaps stay open).

### The helper move (D-2a)

Cut lines 326-357 of `RulesEndpoints.cs` (from `private static RuleAction BuildAction` to the closing brace of
`BuildSwitchWallScene`, plus the blank line before it) and paste them unchanged into the new partial file,
whose usings are exactly what the moved code needs (`SmartSentinelEye.Automation.Api.Requests`,
`SmartSentinelEye.Automation.Domain.Rule`; verify with IDE0005 on both files). Same namespace, `public
static partial class RulesEndpoints`. No reordering, no rename, no edit inside a body. The three string
constants stay in `RulesEndpoints.cs`.

### The attributes

Spec §5, verbatim, on the 8 rows of spec §1.

### Covering evidence

1. **Contract** — both route-table baselines; were the attribute emitted, each would print
   `other System.Diagnostics.CodeAnalysis.SuppressMessageAttribute` and fail.
2. **Helper move** — `RuleActionBuildCharacterisationTests` (reflection on `RulesEndpoints.BuildAction`) and
   `RuleCreateEndpointCharacterisationTests` (drives `Create`, which calls `BuildAction`).
3. **Source readers** — the six declaration guards.
4. **Analyzer** — the two scoped Release builds (spec §1): S107 8 -> 0, remainder identical, no S104.

Phase 5 extra observation: `grep -c SuppressMessageAttribute` on both built Api DLLs prints 0.

## Commits (each builds and passes on its own, ADR-0087)

1. `docs(specs): spec 400 - ...` (this artifact set).
2. `test(2974): pin the AuditObservability route table as it stands` — the new baseline, green on unchanged code.
3. `refactor(2974): move the rule-action builders into RulesEndpoints.Actions.cs` — D-2a, no warning change
   except S104 headroom.
4. `chore(2974): suppress S107 on the eight Automation and AuditObservability route handlers per ADR-0084` —
   the three files.

3 precedes 4 so no commit carries an S104. Every commit leaves every test green (characterisation lane: no
red/green pair).

## Risks

| Risk | Mitigation |
|---|---|
| Attribute emitted after all (`CODE_ANALYSIS` defined somewhere) | Both baselines fail loudly; that is a block, not an edit. Probe already observed 0 in the DLL. |
| `RulesEndpoints.cs` crosses S104 | D-2a; probe measured 305 without it. SC-2 compares the whole list. |
| Baseline captured after a `src/` edit (would encode the change) | Commit 2 is test-only and precedes every `src/` commit; T003 records the SHA it ran on. |
| #3036 merges first and shifts AuditEndpoints lines | Hunks are 75+ lines apart; #3036 changes no metadata, so the baseline holds. Re-run T001/T021 after rebase. |
| Wording drifts from specs 397/398 | Copied byte for byte; SC-4 greps the rule-ID line. |
| Stale build measurement | `--no-incremental`; before and after on the same command; Windows-form logfile path. |
