# Tasks 362: Option&lt;T&gt; for the internal nullable parameters that mean an absence

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2941 (feature-level; on
Project #13 — the Phase 3 gate)

**Phase 4a colour: CHARACTERISATION** (behaviour-preserving; observed green before, unmodified after).
Engineer: **backend-engineer**. Test author: **test-writer**, who returns verbatim green output on
develop before the engineer starts.

No foundational task: no `Shared.Kernel`, `Shared.Contracts`, AppHost or migration work. The two
contexts are disjoint, so T001/T003 and T002/T004 may run in parallel.

## US1 (P1) — internal absences say what they mean

### Phase 4a — characterisation (test-writer)

- [ ] **T001 [P] [US1]** In `tests/LayoutComposition.Application.Tests/EventHandlers/WallSceneSwitchRequestedV1HandlerTests.cs`,
  add, mirroring `FR010f_Layout_with_no_TargetLayout_is_dropped_and_logged`:
  - `FR010f_Next_with_a_TargetLayout_is_dropped_and_logged` — Target `"Next"`, TargetLayout `b`;
    wall still shows `a`, no pending events, `dedup.Calls` empty, single `Warning` entry.
  - `FR010f_Layout_with_an_empty_TargetLayout_is_dropped_and_logged` — Target `"Layout"`,
    TargetLayout `Guid.Empty`; same assertions (and `Handle` does not throw).
  Run the class plus `WallSceneSwitchRequestLogSlotCharacterisationTests` on the branch base; quote
  the green output verbatim. (Unit-test project: no `ci-shards` filter applies — those cover Integration.Tests only.)
- [ ] **T002 [P] [US1]** Run `tests/SystemVariables.Application.Tests` filtered to
  `VariableSnapshotBuilderTests`, `VariableSnapshotBuilderOrderCharacterisationTests` and
  `ResolveOverlayTextQueryHandlerTests`; quote the green output verbatim. No new test.
- [ ] Record the advisory grep on the base: expected **60**.

Commit T001 alone: `test(layout-composition): characterise malformed SwitchWallScene targets`.

### Phase 4b — refactor (backend-engineer; test files read-only)

- [ ] **T003 [P] [US1]** (after T001) `src/LayoutComposition/Application/EventHandlers/WallSceneSwitchRequestedV1Handler.cs`:
  `TryParseTarget` and `ParseTarget` take `Option<Guid> targetLayout`; `ResolveAsync` converts the
  message's `Guid?` once; `LayoutIdentifier.From` stays inside `ParseTarget` (plan §3.1).
  Leave `TryResolveFab` as is.
- [ ] **T004 [P] [US1]** (after T002) `src/SystemVariables/Application/Resolution/VariableSnapshotBuilder.cs`:
  `FindInAnyFabAsync` returns `Option<(Variable Variable, FabIdentifier Fab)>`; `ResolutionOf` takes
  `FabIdentifier` (plan §3.2). `PlaceholderResolution` untouched.
- [ ] **T005 [US1]** (after T003, T004) Re-run T001/T002 suites — green with zero test diff since the
  T001 commit; advisory grep **57**; Release build of both Application projects clean.

Commits (each builds and passes on its own):
`refactor(layout-composition): take the switch target layout as Option<Guid>` and
`refactor(system-variables): pair the answering fab with the found variable in one Option`.

## Out of scope (spec §1, with reasons)

`TryResolveFab`, `V1ResourceMap.Conventions.Identify`, `AuditCursor.TryDecode`,
`ListRulesQueryHandler.FilterBy*`, `Tile`'s EF constructor. List them in the PR body as left alone,
and put the new baseline (57 nullable parameters vs. the `Option<T>` count) in the PR body only.
