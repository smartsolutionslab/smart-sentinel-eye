# Spec 362: Option&lt;T&gt; for the internal nullable parameters that mean an absence

**Issue**: #2941 · **ADRs**: ADR-0141 (`Option<T>` over a nullable parameter in Domain and
Application — advisory), ADR-0048 (explicit-absence type), ADR-0144 (characterisation first for a
behaviour-preserving change) · **Status**: Draft

**Kind**: behaviour-preserving refactor. **Phase 4a colour: characterisation (observed green).**
**Latency budget**: N/A — no leg of constitution §IV is touched. `WallSceneSwitchRequestedV1Handler`
is on the rule → wall-switch path, not the event → overlay path; `VariableSnapshotBuilder` is a
query-side read. Neither change alters allocation-relevant work beyond one struct wrap.

## 1. Premise check (develop `bb496416`)

The issue lists seven candidates. Each was re-read against ADR-0141's scope: Infrastructure and Api
keep nullable parameters because they translate wire shapes; persisted absences stay nullable because
EF does not map `Option<T>`. Application code that is *itself* the parse step for a wire value is the
same translation in a different folder.

| # | Candidate | Verdict | Reason |
|---|---|---|---|
| 1 | `WallSceneSwitchRequestedV1Handler.TryParseTarget(string, Guid? targetLayout, …)` (`:112`) | **Change** | `targetLayout` absent *means* something — "a `Next` target, which carries no layout". Pure internal pass-through to #2. |
| 2 | `WallSceneSwitchRequestedV1Handler.ParseTarget(string, Guid? targetLayout)` (`:277`) | **Change** | Same value; the switch branches on its presence. |
| 3 | `WallSceneSwitchRequestedV1Handler.TryResolveFab(EventMetadata? metadata, …)` (`:151`) | **Drop** | Wire record straight off the message, and absent metadata is deliberately not distinguished from a blank `Fab` (`string.IsNullOrWhiteSpace(metadata?.Fab)` → one log). An `Option<EventMetadata>` would still need the `string?` check on `Fab`; it states nothing new. |
| 4 | `VariableSnapshotBuilder.ResolutionOf(…, FabIdentifier? answeringFab)` (`:63`) | **Change — to non-nullable, not to `Option`** | `ResolutionOf` is called only when `FindInAnyFabAsync` found a variable, and that path always returns the fab. The null is unreachable. `Option<FabIdentifier>` would encode an absence that cannot occur; the honest shape is `FindInAnyFabAsync` returning `Option<(Variable, FabIdentifier)>` and `ResolutionOf` taking `FabIdentifier`. Same ADR-0141 intent (the type says what absence means — here, that the pair is absent together). |
| 5 | `V1ResourceMap.Conventions.Identify(object? raw)` (`:127`) | **Drop** | `raw` is a reflected contract property (`Func<TEvent, object?>`), and the result feeds `V1MappingEntry.PickIdentifier : Func<object, ResourceIdentifier?>`, which the reflection-built convention picker (`V1ResourceMap.cs:115`) also produces. Changing `Identify` alone forces a conversion straight back to nullable; changing the delegate ripples through the convention picker and the audit writer — wider than an internal helper. |
| 6 | `AuditCursor.TryDecode(string? cursor)` (`:21`) | **Drop** | `cursor` is the raw query-string value — this helper *is* the parse at the boundary, like `MediaMtxAction.TryFrom(string?)`. Its nullable-tuple *return* is not what ADR-0141 or the grep measures. |
| 7 | `ListRulesQueryHandler.FilterByState/Source/Kind(…, string?)` (`:49`, `:78`, `:103`) | **Drop** | Same as #6: the `string?` fields of `ListRulesQuery` are query-string values and these methods are where they are parsed into `RuleState` / `TriggerSource` / `TriggerKind`. The boundary has not been crossed yet. |
| 8 | `Tile` private constructor (`Tile.cs:71`, `OverlayIdentifier? overlayValue`) | **Drop** | EF materialisation constructor (bound by reflection to the `OverlayValue` column). Persisted absence — nullable by rule. |

Three grep hits change across two files; five candidates (seven hits) are dropped with reasons.

## 2. User story

### US1 (P1) — internal absences say what they mean

As a maintainer reading LayoutComposition and SystemVariables Application code, I want the two
internal helpers whose nullable parameter carries a real meaning (or an impossible one) to say so in
their type, so that the absence is handled at the one place it arises.

**Why P1 / independently shippable**: two private-method signature changes in two contexts, no
contract, persistence or wire shape moves; covered by existing unit tests plus two new
characterisation tests.

#### Acceptance scenarios (Gherkin)

```gherkin
Scenario: Happy — a Layout target still switches the wall
  Given a wall in fab "munich" showing scene A with scenes [A, B], both Published
  When WallSceneSwitchRequestedV1 arrives with Target "Layout" and TargetLayout B
  Then the wall shows B with cause Rule
  # existing: A_Layout_target_switches_the_wall_with_cause_Rule

Scenario: Happy — a Next target still advances
  When WallSceneSwitchRequestedV1 arrives with Target "Next" and no TargetLayout
  Then the wall advances with cause Rule
  # existing: A_Next_target_switches_the_wall_with_cause_Rule

Scenario: Bad request — Layout without a TargetLayout is dropped as malformed
  When Target is "Layout" and TargetLayout is absent
  Then nothing switches, no dedup key is reserved, one warning is logged
  # existing: FR010f_Layout_with_no_TargetLayout_is_dropped_and_logged

Scenario: Bad request — Next carrying a TargetLayout is dropped as malformed
  When Target is "Next" and TargetLayout is present
  Then nothing switches, no dedup key is reserved, one warning is logged
  # NEW characterisation test (uncovered today)

Scenario: Bad request — Layout with an empty TargetLayout is dropped as malformed
  When Target is "Layout" and TargetLayout is Guid.Empty
  Then nothing switches, no dedup key is reserved, one warning is logged
  # NEW characterisation test: LayoutIdentifier.From throws inside TryParseTarget's catch,
  # which the refactor must not move outside it

Scenario: Snapshot — Resolved and Unset rows still name the answering fab
  Given variable "shift" defined in fab "munich"
  When the snapshot builder resolves "{{shift}}" for [munich]
  Then the row's Fab is munich (Unset and Resolved alike); Archived/Unknown rows carry no fab
  # existing: VariableSnapshotBuilderTests, VariableSnapshotBuilderOrderCharacterisationTests
```

Conflict and auth scenarios: **N/A** — no endpoint, no write path, no authorisation decision moves.
The handler's concurrency (FR-011) and dedup (FR-012) behaviour is covered by existing tests that must
stay green unmodified.

#### Independent test procedure

1. On develop, add the two new characterisation tests; run
   `LayoutComposition.Application.Tests` and `SystemVariables.Application.Tests` — all green.
2. Apply the refactor; re-run — all green with **no test file edited** after step 1.
3. Run the CLAUDE.md advisory grep: **60 → 57**.

## 3. Requirements

- **FR-001** `TryParseTarget` and `ParseTarget` take `Option<Guid> targetLayout`; `ResolveAsync`
  converts the message's `Guid?` once. `LayoutIdentifier.From` stays inside `TryParseTarget`'s
  `try` (an empty Guid remains a logged malformed target, not an escaped exception).
- **FR-002** `FindInAnyFabAsync` returns `Option<(Variable Variable, FabIdentifier Fab)>`;
  `ResolutionOf` takes a non-nullable `FabIdentifier`. `PlaceholderResolution.Fab` stays
  `FabIdentifier?` (it is the DTO-facing record; Archived/Unknown rows have none).
- **FR-003** No change to any `Shared.Contracts` type, EF-mapped type, endpoint, log message or
  log level.
- **FR-004** Covering tests pass unmodified after the change.

## 4. Success criteria

- **SC-001** Advisory grep (CLAUDE.md, ADR-0141) drops by exactly 3: 60 → 57.
- **SC-002** All tests in the covering classes green before and after; zero test-file diff between
  the characterisation commit and the refactor commit.
- **SC-003** The baseline sentence is updated in the PR body only — not in CLAUDE.md or any ADR (issue
  rule).

## 5. Assumptions

- The grep baseline is 60 on develop `bb496416` (the issue measured ~56 on `0b6940a3`; the difference
  is drift since, not a re-scope).
- `Option<T>` has no `FromNullable` helper in `Shared.Kernel`; the conversion is written inline at the
  one call site, mirroring `Tile.Overlay`. Adding a helper is out of scope (no speculative generality).
