# Tasks 240 — A grid nobody sent

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2480 (feature-level issue; no per-task issues; on Project #13)
**Engineer**: `backend-engineer` · **Phase 4a colour**: **RED** (behaviour-changing: plan §4 a–d), with
three green guards (e–g).

Format: `[ID] [P?] [Story] description — file(s)`.
`[P]` = owns files no other open task touches (ADR-0109). **No foundational task**: nothing in
Shared.Kernel, Shared.Contracts, AppHost or Aspire resources changes. The work is one test file and
one production file, so there is no fan-out to parallelise beyond T001.

## Phase 4a — tests first (test-writer; return verbatim output)

- [ ] **T001** [P] [US1][US2] Plan §4 a–g in a new `[Collection(AspireCollection.Name)]` class,
  mirroring `MissingPayloadIsRefusedIntegrationTests` (spec 173): raw JSON bodies (anonymous objects
  or `StringContent`, never `CreateLayoutRequest` — the test must be able to omit `grid`); token for
  `op-dresden@dresden.test`; `ResetLayoutCompositionAsync` in `InitializeAsync`; failure messages
  carry the body and `aspire.RecentLogs("layout-composition")` —
  `tests/Integration.Tests/LayoutComposition/AbsentLayoutBodyMembersAreRefusedIntegrationTests.cs`
- [ ] **T002** [US1][US2] Run
  `dotnet test tests/Integration.Tests --filter FullyQualifiedName~AbsentLayoutBodyMembersAreRefused`
  on unchanged production code. Expected verbatim: **a, b, c, d red with `500` where `400` was
  expected** (not a compile error, not a fixture boot failure); **e, f, g green**. Depends on T001.

## Phase 4b — production (engineer; T001 may not be edited)

- [ ] **T003** [US1] Add `ParseGrid(GridRequest grid)` (plan §3) and call it at both sites (`:143`,
  `:354`) — `src/LayoutComposition/Api/LayoutEndpoints.Commands.cs`. Depends on T002.
- [ ] **T004** [US2] Guard each element in `ParseTiles` with `Ensure.That(tile).IsNotNull()`
  (lambda parameter renamed `request` → `tile`; plan §3) — same file, sequential after T003.
- [ ] **T005** [US1][US2] Re-run T002's command: all seven green, T001 **unmodified**. Counterfactual
  (plan §5): revert T004 alone, quote c/d red, restore. Then `dotnet build -c Release` (analyzers,
  `TreatWarningsAsErrors`) and the LayoutComposition unit suites green. Depends on T004.

## Phase 5

- [ ] **T006** Verification note on the PR: the before/after status table from spec §1 re-observed
  over real HTTP (T002 red output vs T005 green output). Latency: N/A, not on the §IV path.
  Depends on T005.

## Dependencies

```
T001 ─→ T002 ─→ T003 ─→ T004 ─→ T005 ─→ T006
```

Strictly sequential: one test file, one production file. T003 and T004 touch the same file and may
not run in parallel.
