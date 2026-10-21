# Tasks 427: The link and the pressed chip

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2668 (feature-level, on
Project #13, autonomous lane; no per-task issues, per CLAUDE.md Phase 3)

**One PR, `feat/2668-link-button-toggle-chip`, `--base develop`. Phase 4a colour: red**
(behaviour-changing: two new primitives, `aria-pressed` on four filter groups, the design-system focus
ring on seven text actions, hover feedback on unpressed chips), with characterisation pins declared green
in advance (plan §5b). **Not security-sensitive.** **§IV leg: none** (console only; spec §4).
**Assumption A1** (Rules filters → `ToggleChip`) is surfaced in the PR body and droppable (spec §6).

**Roles.** Tests: `test-writer` (T010, T030, T040). Implementation: `frontend-engineer` (T020, T035,
T045). Review: `frontend-reviewer` (T050). The engineer receives the verbatim red output as its brief and
may not edit the red tests. `TextActionAndChipPrimitiveTests.cs` is C# but test-only and owned by
`test-writer`; no `backend-engineer` pass.

**Where tests run.** Local, no Docker: vitest (+ `tsc --noEmit` every time), eslint, `pnpm format:check`,
`Architecture.Tests`. CI-only: Playwright (`e2e/interaction-states.spec.ts`, `e2e/motion.spec.ts`,
unmodified). Docker is down on the authoring machine.

**No `[P]` markers.** One branch, one index (ADR-0109): every task commits in sequence. Nothing
foundational in Shared.Kernel/Contracts/AppHost. US2 (T030/T035) and US3 (T040/T045) touch the same four
page files, so they are ordered, not parallel, even though logically independent.

Format: `[ID] [P?] [Story] description, files`.

## Phase 0: Setup

- [ ] **T001** Re-verify spec §1 on the branch tip: `grep -rn "underline" apps --include=*.tsx | grep -v
  "\.test\."` → the 6 lines of spec §1.1 L1-L7; `grep -rn "bg-accent-subtle" apps --include=*.tsx | grep
  -v "\.test\."` → C1-C3 + `ShellLayout.tsx` + `GridDesigner.tsx` + three `data-[highlighted]:` uses;
  `grep -rn "aria-pressed" apps e2e` → empty; `@radix-ui/react-toggle` absent from
  `apps/shared/package.json`. Confirm `427` is still free on `origin/develop`, every remote branch and
  every worktree. Baseline: `dotnet test tests/Architecture.Tests --filter
  "FullyQualifiedName~MotionLanguage|FullyQualifiedName~KioskButtonPrimitive"` green. No file change.

## Phase 1: US1 — the primitives exist (P1)

- [ ] **T010 [US1]** (`test-writer`) Red commit C2 — plan §5a R1-R8, §5b P1-P6:
  - `apps/shared/src/ui/primitives/LinkButton.test.tsx`, `ToggleChip.test.tsx` (jsdom header,
    `classList`/`getAttribute`, no jest-dom — mirror `Button.test.tsx`).
  - Stubs `apps/shared/src/ui/primitives/LinkButton.tsx` and `ToggleChip.tsx`: final exported types
    (`LinkButtonProps = ComponentPropsWithRef<'button'>`; `ToggleChipProps extends
    Omit<ComponentPropsWithRef<'button'>, 'aria-pressed'> { pressed: boolean }`), body `<button {...rest} />`.
  - `apps/shared/package.json` `exports` entries for both.
  - Run and capture **verbatim**: `pnpm --filter @smart-sentinel-eye/shared exec vitest run
    src/ui/primitives/LinkButton.test.tsx src/ui/primitives/ToggleChip.test.tsx`, `pnpm --filter
    @smart-sentinel-eye/shared typecheck`, `pnpm --filter @smart-sentinel-eye/shared lint`,
    `pnpm format:check`.
  - **Required:** R1-R5, R7, R8 red; R6 red on its unpressed half; P1-P6 green (declared); typecheck
    green; a `@ts-expect-error` case proves `pressed` is required and `aria-pressed` is rejected (tsc).
    Anything expected red that arrives green: stop and report.

- [ ] **T020 [US1]** (`frontend-engineer`, depends on T010) Green commit C3: both primitives exactly per
  plan §3a/§3b (short doc comments: inherited colour = #2668 owner decision / spec 287 D2; cursor stays at
  the call site per ADR-0151). Re-run T010's commands plus `dotnet test tests/Architecture.Tests --filter
  FullyQualifiedName~MotionLanguage`: all green; quote output. `git diff --stat` touches only the two
  primitive files.

## Phase 2: US2 — the seven text actions are `LinkButton` (P2)

- [ ] **T030 [US2]** (`test-writer`, depends on T020) Red commit C4:
  - New `tests/Architecture.Tests/TextActionAndChipPrimitiveTests.cs` with G1, G3, G4 (plan §5a);
    helpers mirrored from `KioskButtonPrimitiveTests.cs` (comment stripping with preserved line numbers,
    `RepositorySource.RelativePath`). G2 is **not** added yet.
  - `apps/shared/src/ui/composites/RetryBanner.test.tsx`: R9. Existing cases untouched.
  - Capture verbatim: `dotnet test tests/Architecture.Tests --filter
    FullyQualifiedName~TextActionAndChipPrimitive`, the RetryBanner vitest run + `typecheck`.
  - **Required:** G1 red naming exactly the six `file:line`s of plan §5a; R9 red; G3 and G4 green and
    each proved by a planted, reverted counterfactual quoted in the PR.
  - Check whether `Architecture.Tests` is sharded in `.github/workflows/ci.yml`; if it is, add the class
    to its filter (a missing entry fails deterministically).

- [ ] **T035 [US2]** (`frontend-engineer`, depends on T030) Green commit C5: L1-L7 per plan §2 —
  four pages (deep specifier `@smart-sentinel-eye/shared/ui/primitives/LinkButton`), `RetryBanner.tsx`
  and `ChainRecoveryNotice.tsx` (relative import; `ref`/`aria-disabled`/`onClick` unchanged;
  `className="aria-disabled:cursor-progress"`; delete `controlClassName`). Run: G1/G3/G4; `pnpm --filter
  @smart-sentinel-eye/shared test`; `pnpm --filter @smart-sentinel-eye/management-web test`; `typecheck`
  and `lint` for shared, management-web, management-cameras, management-event-ingestion;
  `pnpm format:check`. Every plan §5b suite passes **unmodified**; quote output.

## Phase 3: US3 — the state filters are `ToggleChip` (P3)

- [ ] **T040 [US3]** (`test-writer`, depends on T035) Red commit C6:
  - `TextActionAndChipPrimitiveTests.cs`: add G2 and its two-entry allowlist (ShellLayout NavLink,
    GridDesigner radio label), each with its reason; G3 now also covers these entries.
  - R10 in `LayoutsPage.test.tsx`, `OverlaysPage.test.tsx`, `SystemVariablesPage.test.tsx`,
    `RulesPage.test.tsx` (the last is A1 — keep it in its own `describe` so dropping A1 is one block).
  - Capture verbatim; **required:** G2 red naming `LayoutsPage.tsx:138`, `OverlaysPage.tsx:126`,
    `SystemVariablesPage.tsx:129`; R10 red on all four pages; G1/G3/G4 green.

- [ ] **T045 [US3]** (`frontend-engineer`, depends on T040) Green commit C7: C1-C3 and C4 (A1) per plan
  §2. Remove only the classes the chip now owns; keep `key`, `onClick`, `STATE_FILTERS` and the Rules
  `role="group"` wrapper. Re-run T035's command set + G2: all green; plan §5b unmodified; quote output.
  `git diff --name-only` (whole PR) satisfies spec SC-5.

## Phase 4: Verify, review, PR

- [ ] **T050** (`frontend-reviewer`) Review against plan §3 invariants (no colour on `LinkButton`; one
  fill slot on `ToggleChip`; no hover fill when pressed; no motion; no `asChild`/variant creep; deep
  imports; `Button.tsx` unchanged). Phase 5 note: spec §7 steps 1-3 locally; steps 4-5 named as CI/browser
  evidence, not claimed. PR body: the three red outputs verbatim, the G3/G4 counterfactuals, "§IV: N/A",
  A1 flagged, plan §8 follow-up text for the orchestrator. `Closes #2668`.

## Dependencies

T001 → T010 → T020 → T030 → T035 → T040 → T045 → T050. C1-C3 is a mergeable prefix if review wants the
primitives alone.
