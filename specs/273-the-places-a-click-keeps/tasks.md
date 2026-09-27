# Tasks 273: The places a click keeps

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2632 (feature-level; no per-task issues;
on Project #13) · **Rule**: ADR-0151 · **Pattern**: #2624 / PR #2631
**Engineers**: `test-writer` (4a), then `frontend-engineer` (4b). **Reviewer**: `frontend-reviewer`.
**Phase 4a colour**: **RED** (behaviour-changing, ADR-0139). This is the same call #2624 made. On
`develop`, focus falls to `<body>` at every site. **An expected-red test that arrives green is a 4a
failure: stop and report it, and do not adjust it. A green pin's result is not 4a evidence.**

Format: `[ID] [P?] [Story] description, file(s)`. Every task implements ADR-0151 for issue #2632.

**Foundational / blocking**: T000 only, which is read-only. There is no Shared.Kernel,
Shared.Contracts, AppHost or Aspire work.
**Parallelism (ADR-0109)**: each unit-test task owns one test file, and each fix task owns one
production file, so every `[P]` inside a phase can fan out. The e2e file (T010) has **one** writer.
T011 gates 4b.
**No shared hook, helper or module** may be introduced (plan §1, ADR-0036, the PR #2631 precedent).

## Pre-4a

- [ ] **T000** Re-verify spec §1 against the worktree tip. Run
  `git grep -nE '(^|[^-])disabled[=:]' -- 'apps/**/*.tsx'` and confirm every row of the in-scope
  table is still at its line with today's expression. If a site moved or vanished, or a new one
  appeared, **stop and report**. Re-check that spec number 273 is free across every remote branch and
  worktree.

## Phase 4a: tests first (test-writer; run them and return the output verbatim)

Per control, write the (a) attribute, (b) guard and (c) available cases from plan §4.1. The mocked
hook state must be mutable per test. **Edit no existing case**, except the one declared rewrite in
T004.

- [ ] **T001** [P] [US1] S1 (Set value, which also needs (a) for the empty and undefined buffer) and S2
  (Archive). File: `apps/management-web/src/features/systemVariables/SystemVariablesPage.test.tsx`
- [ ] **T002** [P] [US2] L1 (Publish), L2 (Edit (new draft)), and L3 (the More actions trigger). For
  L3, (b) means: with the row `disabled`, open the menu and select Revert, and `revertRevision` is not
  called (plan §3.3). File: `apps/management-web/src/features/layouts/LayoutsPage.test.tsx`
- [ ] **T003** [P] [US3] O1, O2, O3, O4 and O5. Also add a **pin** that `:209` Edit draft still
  carries native `disabled` while a row mutation is in flight. It is green on `develop`, and it records
  [J1] so that a silent flip shows. File:
  `apps/management-web/src/features/overlays/OverlaysPage.test.tsx`
- [ ] **T004** [P] [US4] C1 (Previous) and C2 (Next), both for the `isFetching` state and for the
  boundary. **Declared rewrite**: `:152` `toBeDisabled()` becomes
  `toHaveAttribute('aria-disabled','true')` plus `not.toHaveAttribute('disabled')` (plan §4.1). File:
  `apps/management-web/src/features/cameras/CamerasPage.test.tsx`
- [ ] **T005** [P] [US5] A1. Its (b) must assert that the applied query **does not reset to the first
  page**, meaning the search hook is not re-called with `cursor: undefined` (plan §3.6). File:
  `apps/management-web/src/features/audit/AuditPage.test.tsx`
- [ ] **T006** [P] [US6] W1 (Next) and W2 (Show), for both the in-flight state and the showing scene.
  File: `apps/management-web/src/features/walls/WallDetailPage.test.tsx`
- [ ] **T007** [P] [US7] W3, mirroring `RegisterCameraDialog.test.tsx`'s three cases. `:124`'s
  `aria-busy` case stays **unmodified**. File:
  `apps/management-web/src/features/walls/WallForm.test.tsx`
- [ ] **T010** [US1–US7] Add the ten tests in plan §4.2's table, one `test.describe` per story. Reuse
  the file-local `holdWrites`; activate with `keyboard.press('Enter')`, never `.click()` (plan §5).
  Release every hold in `finally`. Waits go by condition only (ADR-0150), and there is no
  `waitForTimeout`. Reuse the seeding in `e2e/support/`. File: `e2e/in-flight-focus.spec.ts`
- [ ] **T011** On **unchanged production code**, run the following and capture the output **verbatim**:
  - `pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/features/systemVariables src/features/layouts src/features/overlays src/features/cameras src/features/audit src/features/walls`
  - `pnpm test:e2e e2e/in-flight-focus.spec.ts` against a booted stack (one stack per machine; stop it
    before building)
  - `pnpm typecheck` (and `pnpm typecheck:e2e`; a known `@types/node` gap fails that on a clean
    `develop`, so check `develop` first)

  **Required red**: every (a) case in T001–T007, the T004 rewrite, and every new e2e `toBeFocused`
  (received `<body>`). **Required green (pins)**: every (b) and (c) case, the T003 `:209` pin, every
  pre-existing case, every e2e count or state-unchanged assertion that is reached, and typecheck. If any
  required-red case arrives green, stop and report.
  Commit: `test(ui): pin focus through the remaining ADR-0151 sites`. Depends on T001–T007 and T010.

## Phase 4b: production (frontend-engineer; tests may not be edited)

Every task follows plan §2: `disabled=` becomes `unavailable=` with the expression unchanged, and the
same condition is guarded first in the handler. No `busy`, cursor class, hook or helper is added.

- [ ] **T012a** [P] [US1] Plan §3.1 (S1, S2). File:
  `apps/management-web/src/features/systemVariables/SystemVariablesPage.tsx`. Depends on T011.
- [ ] **T012b** [P] [US2] Plan §3.2 and §3.3 (L1–L3). The menu entries' `disabled` stay. File:
  `apps/management-web/src/features/layouts/LayoutsPage.tsx`. Depends on T011.
- [ ] **T012c** [P] [US3] Plan §3.4 (O1–O5). `:209` is untouched. File:
  `apps/management-web/src/features/overlays/OverlaysPage.tsx`. Depends on T011.
- [ ] **T012d** [P] [US4] Plan §3.5 (C1, C2). File:
  `apps/management-web/src/features/cameras/CamerasPage.tsx`. Depends on T011.
- [ ] **T012e** [P] [US5] Plan §3.6 (A1). File: `apps/management-web/src/features/audit/AuditPage.tsx`.
  Depends on T011.
- [ ] **T012f** [P] [US6] Plan §3.7 (W1, W2). File:
  `apps/management-web/src/features/walls/WallDetailPage.tsx`. Depends on T011.
- [ ] **T012g** [P] [US7] Plan §3.7 (W3, `handleFormSubmit`). File:
  `apps/management-web/src/features/walls/WallForm.tsx`. Depends on T011.

  Commit: `fix(ui): keep focus on row actions, pagination and wall controls while unavailable`. One
  commit is fine; per-file commits are also fine, as long as each one builds on its own.
- [ ] **T013** Re-run T011's commands. Every test must be green and **unmodified** since T011's commit:
  `git diff <T011-sha> -- '*.test.tsx' e2e/in-flight-focus.spec.ts` must be empty. Also run
  `pnpm lint` (`--max-warnings 0`, with no suppression added), `pnpm format:check` and
  `pnpm typecheck`. Depends on T012a–g.
- [ ] **T014** **Counterfactuals** (ADR-0151 Implementation Notes). Quote each result, then revert it.
  1. Remove W3's `isLoading` guard in `handleFormSubmit` and run its e2e. Required: `Expected: 1,
     Received: 2`, and T007 (b) goes red.
  2. Remove A1's guard. Required: T005 (b) goes red, because the page resets to page one.
  3. Remove the Layouts menu entries' `disabled`. Required: T002's L3 (b) goes red. If it stays green,
     plan §3.3's claim is false: **stop**.
  4. Remove one direct click guard (O1 Publish). Required: T003's O1 (b) goes red.

  Depends on T013.
- [ ] **T015** Search again. `git grep -nE '(^|[^-])disabled=' -- 'apps/management-web/src/features/{systemVariables,layouts,overlays,cameras,audit,walls}/*.tsx' ':!*.test.tsx'`
  must return only `OverlaysPage.tsx:209` and `WallForm.tsx:133,142`. The Layouts menu entries use
  the shorthand `disabled,` and do not match. List spec §1's "reported, not in scope" rows in the PR body. Depends on
  T013.

## Phase 5

- [ ] **T016** Carry out spec §6 steps 1–6 by hand on a booted stack, and record the
  `document.activeElement` reading per mechanism in the verification note. Quote the T011 and T013 e2e
  runs. There is no latency figure (§IV N/A). Depends on T014.

## Phase 7 notes

- Run `gh pr create --base develop`. The body closes `#2632` with a closing keyword and quotes T011's
  red output (ADR-0139).
- Report these to the orchestrator for follow-up filing, and **do not file them from this run**: the
  WallForm Up/Down reorder focus defect (spec §1, [J5]), and optionally the unmount-after-success
  focus loss (spec §1).

## Dependencies

```
T000
T001 T002 T003 T004 T005 T006 T007 T010 ─→ T011 ─→ T012a…g ─→ T013 ─→ T014 ─→ T016
                                                                  └──→ T015
```
