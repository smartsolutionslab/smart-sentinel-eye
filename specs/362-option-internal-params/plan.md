# Plan 362: Option&lt;T&gt; for the internal nullable parameters that mean an absence

**Spec**: [spec.md](spec.md) · **Issue**: #2941 · **ADRs**: 0141, 0048, 0144

## 1. Constitution / ADR alignment

- **ADR-0141**: advisory rule applied in Application only; Infrastructure/Api untouched; persisted
  nullable properties (`Tile.OverlayValue`, `PlaceholderResolution.Fab`) untouched.
- **ADR-0144 / constitution §Testing**: behaviour-preserving → characterisation, observed green
  before, unmodified after.
- **ADR-0036**: smallest change; no `Option` helper added to `Shared.Kernel`.
- **Boundary rules**: no `Shared.Contracts` change, no cross-context reference. NetArchTest unaffected.
- **Latency (§IV)**: N/A.
- **No new ADR needed.**

## 2. Bounded contexts and layers

| Context | Layer | File |
|---|---|---|
| LayoutComposition | Application | `src/LayoutComposition/Application/EventHandlers/WallSceneSwitchRequestedV1Handler.cs` |
| SystemVariables | Application | `src/SystemVariables/Application/Resolution/VariableSnapshotBuilder.cs` |

The two are disjoint and can run in parallel.

## 3. Changes

### 3.1 WallSceneSwitchRequestedV1Handler

- `ResolveAsync`: after the existing deconstruction, convert once:
  `var layout = targetLayout is { } value ? Option<Guid>.Some(value) : Option<Guid>.None;`
  and pass `layout` to `TryParseTarget`.
- `TryParseTarget(string target, Option<Guid> targetLayout, SwitchRequestTrace trace)` — body
  unchanged.
- `ParseTarget(string target, Option<Guid> targetLayout)`:
  - `NextTarget when !targetLayout.HasValue` → `new SceneTarget.Next()`
  - `LayoutTarget when targetLayout.HasValue` → `new SceneTarget.Layout(LayoutIdentifier.From(targetLayout.Value))`
  - default arm unchanged (same `ArgumentException`).
- `LayoutIdentifier.From` must stay inside `ParseTarget` (so inside `TryParseTarget`'s `catch`):
  converting to `Option<LayoutIdentifier>` in `ResolveAsync` would move the `Guid.Empty` throw
  outside the catch and escape `Handle` — a behaviour change. This is why the Option is over `Guid`.
- `TryResolveFab` unchanged (spec §1 #3).

### 3.2 VariableSnapshotBuilder

- `FindInAnyFabAsync` → `Task<Option<(Variable Variable, FabIdentifier Fab)>>`; found →
  `Option<…>.Some((found.Value, fab))`; none → `Option<…>.None`.
- `BuildAsync`: `var found = await FindInAnyFabAsync(…)`; `if (!found.HasValue) { …unchanged… }`;
  `var (variable, answeringFab) = found.Value; resolutions.Add(ResolutionOf(name, variable, answeringFab));`
- `ResolutionOf(string name, Variable variable, FabIdentifier answeringFab)` — body unchanged
  (implicit widening into `PlaceholderResolution`'s `FabIdentifier?`).
- Update the `FindInAnyFabAsync` XML summary only if it now misdescribes the return; no other comment
  churn.

### 3.3 Invariants preserved

- Exactly one warning per malformed target; no dedup reservation on a parse drop.
- Snapshot rows: Resolved/Unset carry the answering fab; Archived/Unknown carry `null`; label order and
  ordinal fab tiebreak unchanged.

## 4. Tests

| Candidate | Covering tests (existing, must stay unmodified) | New characterisation |
|---|---|---|
| `TryParseTarget` / `ParseTarget` | `WallSceneSwitchRequestedV1HandlerTests`: `A_Layout_target_switches_the_wall_with_cause_Rule`, `A_Next_target_switches_the_wall_with_cause_Rule`, `FR010f_An_unknown_target_contract_violation_is_dropped_and_logged`, `FR010f_Layout_with_no_TargetLayout_is_dropped_and_logged`, `Drops_a_through_c_never_consume_a_dedup_key`; `WallSceneSwitchRequestLogSlotCharacterisationTests` | `FR010f_Next_with_a_TargetLayout_is_dropped_and_logged`; `FR010f_Layout_with_an_empty_TargetLayout_is_dropped_and_logged` |
| `ResolutionOf` / `FindInAnyFabAsync` | `VariableSnapshotBuilderTests` (all 6, notably `A_defined_set_non_archived_variable_resolves`, `A_defined_variable_with_no_value_yet_is_Unset`, `A_name_held_in_two_fabs_resolves_in_the_first_by_ordinal_fab_name`); `VariableSnapshotBuilderOrderCharacterisationTests` (all 3); `ResolveOverlayTextQueryHandlerTests.The_entry_names_the_fab_that_answered_under_the_ordinal_tiebreak` | none needed |

The new tests go in the existing `WallSceneSwitchRequestedV1HandlerTests` class (already in a shard
filter — no `ci-shards/*.filter` edit; verify with a grep before committing).

Counterfactual for the new tests: dropping `when !targetLayout.HasValue` from the `Next` arm turns
`FR010f_Next_with_a_TargetLayout…` red; moving `LayoutIdentifier.From` out of the `try` turns
`FR010f_Layout_with_an_empty_TargetLayout…` red (exception escapes `Handle`).

## 5. Verification

- `dotnet test tests/LayoutComposition.Application.Tests` and `tests/SystemVariables.Application.Tests`
  before and after.
- `git diff <characterisation-commit>..HEAD -- tests/` is empty.
- Advisory grep 60 → 57.
- Release build clean (IDE0007 `var`, IDE0005 usings).
