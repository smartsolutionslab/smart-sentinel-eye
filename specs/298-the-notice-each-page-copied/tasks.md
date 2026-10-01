# Tasks 298: The notice each page copied

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2693

## Phase-3 declarations (for `/next-issue`)

- **Engineer:** `frontend-engineer` for every phase-4b task (`apps/shared`, `apps/management-web`).
  Phase 4a (`test-writer`) owns the Vitest files **and** the one new C# architecture-test file.
  No backend, no infra.
- **New ADR:** **no** (spec §5).
- **Phase 4a colour:** **RED** for `FaultNotice` (US0), all five US1 sites, US2 and the US3
  guard — each site's rendered fill changes. **CHARACTERISATION** for `RetryBanner` (US0): its DOM
  does not change; its test is written fresh (none exists) and observed green before the edit.
  No existing test line may change (spec §7).
- **Latency:** N/A — console only, no token value changes (spec §6).
- **Feature issue on Project #13:** #2693 (already on the board, `agent:ready`).

Format: `[ID] [P?] [Story]`. `[P]` = owns files disjoint from every other `[P]` task in the same
phase (ADR-0109).

## Phase 4 prerequisites (orchestrator)

- [ ] **T001** Confirm on the branch tip: `tokens.css` still declares
  `--color-accent-fault-subtle: var(--red-900)` and `--color-accent-fault-border`; the six sites
  are still at the lines in spec §1. Drift → re-read before dispatching.
- [ ] **T002** Re-check spec number 298 across remote branches and worktrees (`sse-2692`,
  `sse-2698` are live).

## Phase 4a: characterise, then red (test-writer)

**Characterisation first — on the untouched tree, capture verbatim green output:**

- [ ] **T003 [US0]** New `apps/shared/src/ui/composites/RetryBanner.test.tsx` (plan §4.1). Green
  now.
- [ ] **T004 [US1][US2]** Run and capture green, unmodified: `LayoutsPage.test.tsx`,
  `OverlaysPage.test.tsx`, `SystemVariablesPage.test.tsx`, `RulesPage.test.tsx`,
  `WallDetailPage.test.tsx`, `WallsPage.test.tsx` (other `RetryBanner` consumer);
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~SharedUiTokenUsage|FullyQualifiedName~DesignTokenLayer"`.

**Red (after T003–T004):**

- [ ] **T005 [US0]** `FaultNotice.tsx` **signature-only stub** (`return null`) + `exports` entry;
  `FaultNotice.test.tsx` per plan §4.2. Run → red; `tsc --noEmit` green. Quote.
- [ ] **T006 [P] [US1]** `LayoutsPage.test.tsx`: plan §4.3 case. Red. Quote.
- [ ] **T007 [P] [US1]** `OverlaysPage.test.tsx`: plan §4.3 case. Red. Quote.
- [ ] **T008 [P] [US1]** `SystemVariablesPage.test.tsx`: plan §4.3 case. Red. Quote.
- [ ] **T009 [P] [US1]** `WallDetailPage.test.tsx`: plan §4.3 case (+ no button). Red. Quote.
- [ ] **T010 [P] [US1][US2]** `RulesPage.test.tsx`: the mutation case (US1) and the load-failure
  case (US2). Both red. Quote.
- [ ] **T011 [P] [US3]** `tests/Architecture.Tests/ConsoleTriadAlphaTests.cs` (plan §4.4). Red,
  naming exactly the 11 matches in 5 files. Any other set → stop. Quote.

## Phase 4b: implement (frontend-engineer)

**Foundation — blocks every page task:**

- [ ] **T012 [US0]** Fill `FaultNotice.tsx` (plan §2); rewrite `RetryBanner.tsx` through it;
  update the `tokens.css:139` comment (comment only). T005 green; **T003 green unmodified**.

**Fan out after T012 — disjoint files:**

- [ ] **T013 [P] [US1]** `LayoutsPage.tsx` → `FaultNotice`. T006 green; LayoutsPage suite green
  unmodified.
- [ ] **T014 [P] [US1]** `OverlaysPage.tsx` → `FaultNotice`. T007 green; suite unmodified.
- [ ] **T015 [P] [US1]** `SystemVariablesPage.tsx` → `FaultNotice`. T008 green; suite unmodified.
- [ ] **T016 [P] [US1]** `WallDetailPage.tsx` → `FaultNotice`. T009 green; suite unmodified.
- [ ] **T017 [P] [US1][US2]** `RulesPage.tsx`: mutation notice → `FaultNotice`; load failure →
  `RetryBanner`. T010 green; suite unmodified.

**After T013–T017:**

- [ ] **T018 [US3]** Run `ConsoleTriadAlphaTests` → green. Counterfactual: re-add
  `bg-accent-fault/10` to one page locally, observe it named, revert. Record both runs.
- [ ] **T019** `pnpm lint`, `pnpm typecheck`, `pnpm format:check`, `pnpm test`;
  `dotnet test tests/Architecture.Tests`; SC-3 grep empty.

## Phase 5: verify

- [ ] **T020** Spec §8 steps 1–5 against the running stack; screenshots; computed
  `background-color` of one refusal notice beside `RetryBanner`'s. Latency: N/A, stated.

## Dependencies

```
T001,T002 ─► T003,T004 ─► T005 ─► T006..T011 [P] ─► T012 ─► T013..T017 [P] ─► T018 ─► T019 ─► T020
```

T006–T011 need only T005's export to compile (pages import `FaultNotice` only in 4b, so they
could start right after T004; T005 first keeps one red-run order to quote). T017 is one task for
two stories because both edit `RulesPage.tsx`.
