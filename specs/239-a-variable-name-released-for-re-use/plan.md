# Spec 239 — Plan

**Issue:** #2446 · **Spec:** `specs/239-a-variable-name-released-for-re-use/spec.md`
**Phase:** 2 (Plan) · **Date:** 2026-09-24 · **Mirrors:** spec 177 plan (#2216)

## Bounded context and layers

**SystemVariables only.** One Application-layer query handler changes. No Domain,
Infrastructure, Api, `Shared.Contracts` or `Shared.Kernel` edit; no AppHost or
Aspire resource; no cross-context reference (NetArchTest rules untouched).

| Layer | File | Change |
|---|---|---|
| Application | `src/SystemVariables/Application/Queries/Handlers/GetVariableQueryHandler.cs` | Exclude Archived; refuse on distinct fabs |
| Application | `src/SystemVariables/Application/Queries/GetVariableErrors.cs` | **Doc comment only**, if at all — see below |
| Tests | `tests/SystemVariables.Application.Tests/Queries/GetVariableQueryHandlerTests.cs` | New reds |
| Tests | `tests/Integration.Tests/SystemVariables/SystemVariableLifecycleIntegrationTests.cs` | New HTTP red; one existing read re-routed |
| Tests | `tests/Integration.Tests/SystemVariables/VariableResidueCleanupTests.cs` | One existing read re-routed |

## Entities, value objects, invariants

No new types. The ones involved, all existing:

- `Variable` aggregate — `Fab : FabIdentifier`, `Name : VariableName`,
  `State : VariableState`, `Version`.
- `VariableState` — `Defined | Archived`; Archived terminal (spec 005 FR-005).
- **Invariant relied on, already enforced by the schema:**
  `ux_system_variables_fab_name_active` — at most one non-Archived row per
  `(Fab, Name)` (`VariableConfiguration.cs:113-116`). With Archived excluded from
  the read, *number of matches* = *number of distinct fabs*, so the ambiguity
  refusal is true by construction. The distinct-fab condition (US2) makes it true
  independently of the index as well.

## The change

```csharp
List<Variable> matches = await variables.Variables
    .Where(candidate => query.Fabs.Contains(candidate.Fab))
    .Where(candidate => candidate.Name == query.Name)
    .Where(candidate => candidate.State != VariableState.Archived)   // US1
    .ToListAsync(cancellationToken);

if (matches.Count == 0) { ... VariableNotFound ... }                  // unchanged

IReadOnlyList<string> fabsHolding =                                   // US2
    [.. matches.Select(match => match.Fab.Value)
               .Distinct(StringComparer.Ordinal)
               .Order(StringComparer.Ordinal)];
if (fabsHolding.Count > 1)
{
    return Failure(GetVariableFailures.VariableFabAmbiguous(query.Name.Value, fabsHolding));
}

return Success(Map(matches[0]));
```

- **Predicate placement:** a third `Where` after the fab scope, matching the
  handler's existing one-predicate-per-`Where` style and
  `VariableRepository.GetByNameAsync:28-30` line for line. Never reorder or merge
  with the fab scope (ADR-0114, spec 014 FR-009).
- **Value object, not `.Value`:** `VariableState` is value-converted; the
  expression is the one `VariableRepository:30` already sends to Postgres, so EF
  translation is proven. In the unit tests `TestVariableQuerySource` evaluates it
  in memory via record equality on the static `VariableState.Archived` instance.
- **Distinct-fab condition, inline.** Spec 177 extracted `RuleFabCandidates`
  because two Automation handlers shared it. Here one handler uses it; a helper
  would be speculative (ADR-0036), and Automation's cannot be referenced across
  the context boundary. `Order(StringComparer.Ordinal)` replaces the existing
  `OrderBy(name => name, StringComparer.Ordinal)` — same ordering, the spelling
  spec 177 used.
- **`matches[0]` after the check** is safe: with one distinct fab and Archived
  excluded, the index guarantees one row. If the index were ever dropped, two
  live rows in one fab would resolve arbitrarily rather than as a false
  ambiguity — identical to what spec 177 accepted for rules; the index is
  guarded by `CrossFabVariableIntegrationTests.An_archived_name_is_free_for_reuse_within_the_same_fab`.
- **`GetVariableErrors.cs`:** the record already takes
  `IReadOnlyList<string> Candidates` and joins it. **No signature, code, status or
  wording change.** The only permissible edit is a one-sentence addition to the
  existing `<summary>` saying the candidates are distinct fabs — optional, and
  only if the engineer judges the why non-obvious (ADR-0036).
- **Comments:** one *why* comment on the new predicate citing FR-005 and naming
  `VariableRepository.GetByNameAsync` as the line it now agrees with; one on the
  distinct-fab condition. Replace the existing *"Not tie-broken"* comment's
  position only if needed; keep its reasoning.

## Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` change.
Reads only.

## Boundary rules

- No project reference from SystemVariables to Automation — the fix mirrors
  spec 177 by pattern, not by code.
- `IVariableQuerySource` stays the read seam (ADR-0041); `IVariableRepository`
  untouched.

## API contract

| Case | Before | After |
|---|---|---|
| Archived + re-defined, same fab | 400 `VARIABLE_FAB_AMBIGUOUS` "(munich, munich)" | **200** live variable + `ETag` |
| Same, with `?fabId=munich` | 400, same | **200** |
| Archived, never re-used | 200 `state: "Archived"` | **404 `VARIABLE_NOT_FOUND`** (deliberate) |
| Genuine cross-fab | 400, candidates per row | 400, candidates distinct — identical for one row per fab |
| Another fab's variable | 404 | 404 (unchanged) |

OpenAPI declarations on `GetSystemVariable` already list 200/400/401/403/404 —
nothing to add. The summary text (*"400 if the name is held in more than one and
none is named"*) is already accurate after the fix.

## Existing tests that move — and why that is not a weakened gate

Two integration tests read an archived, never-re-used variable by name
(`spec.md`, *Found while checking*). Their **subject** is "the variable reached
Archived", not "archived variables are readable by name". Each keeps its positive
`ShouldBe("Archived")` assertion on the named row; only the route it reads from
changes, to `GET /system-variables?state=Archived`, the console's own route.

To keep this evidenced rather than asserted:

- The re-route is done by the **test-writer in phase 4a**, not the engineer in 4b,
  so the engineer still may not edit any test.
- The re-routed tests must be **green before the `src/` change** (the listing
  already returns archived rows today) **and green after** — i.e. characterised
  across the fix, proving the assertion is not newly satisfiable.
- The old by-name form of each is recorded in the 4a output as **going red after
  the fix** (404), which is the narrowing observed — quoted in the PR alongside
  the new reds.

A stronger assertion is acceptable; a weaker one (e.g. dropping the state check,
or asserting only membership in `includeArchived=true`) is a blocked outcome.

## Latency (§IV)

N/A — operator-console read; no leg of the event→overlay path reads
`IVariableQuerySource`.

## Constitution / ADR check

| Rule | Status |
|---|---|
| §II value objects | Comparison on `VariableState`, no primitives introduced |
| §Testing | Red declared; re-routed tests characterised green→green |
| ADR-0047 | `GetVariableFailures` factory unchanged, still builds the base type |
| ADR-0113 | Restores the `ETag` the `If-Match` layer depends on |
| ADR-0114 | Fab scope first, unchanged |
| ADR-0144 | No ADR written, no gate weakened; 4a before 4b |
| New ADR | **Not needed** — FR-005 / 014 FR-003 already decided |
