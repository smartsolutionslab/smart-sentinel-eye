# Tasks 425: The button a finger can find

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2667 (feature-level, on
Project #13, autonomous lane; no per-task issues, per CLAUDE.md Phase 3)

**One PR, `fix/2667-button-touch-size`, `--base develop`. Phase 4a colour: red** (behaviour-changing:
a new prop and size, hover becomes instant on both surfaces, seven kiosk buttons change look and size),
with characterisation pins declared green in advance (plan §5b). **Motion option: M2** (spec §1.2 — M1
verified to fail `MotionLanguageTests` fact 1 and ADR-0148). **Not security-sensitive.** **§IV leg:
composite + render** (`LayoutGrid.tsx`), cited in the PR body.

**Roles.** Tests: `test-writer` (T010, T030). Implementation: `frontend-engineer` (T020, T040). Review:
`frontend-reviewer` (T050). The engineer receives the verbatim red output as its brief and may not edit
R1-R6. `MotionLanguageTests.cs` / `KioskButtonPrimitiveTests.cs` are C#, but test-only and owned by
`test-writer`; no `backend-engineer` pass is needed.

**Where tests run.** Local, no Docker: vitest, `tsc --noEmit`, eslint, prettier, `Architecture.Tests`.
CI-only: Playwright (`e2e/motion.spec.ts`, `e2e/interaction-states.spec.ts`, `e2e/kiosk-touch-target.spec.ts`)
and the `render-leg-gate` summary. Docker was down on the authoring machine.

**No `[P]` markers.** Every task commits to one branch and one index (ADR-0109; one branch, one index),
and the pairs are ordered for per-commit builds (plan §6). Nothing foundational in
Shared.Kernel/Contracts/AppHost.

Format: `[ID] [P?] [Story] description, files`.

## Phase 0: Setup

- [ ] **T001** Re-verify spec §1 P1-P6 and the §1.1 site list on the branch tip (`grep -rn "<button"
  apps/kiosk-web/src --include=*.tsx | grep -v test`: expect exactly S1-S9). Confirm `425` is still free
  on `origin/develop`, every remote branch and every local worktree. Re-run the baseline
  `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~MotionLanguage` (expect 9/9). No
  file change.

## Phase 1: US1 — `Button` has a touch size and no state animation (P1)

- [ ] **T010 [US1]** (`test-writer`) Red commit C2, per plan §5a R1-R5 and the §5b md pin:
  - `apps/shared/src/ui/primitives/Button.test.tsx`: R1, R2, R3, md pin. Existing cases untouched.
  - `apps/shared/src/ui/primitives/Button.tsx`: **declaration only** — `export type ButtonSize = 'md' |
    'touch'` and `size?: ButtonSize` on `ButtonProps`, not destructured, so `typecheck` stays green.
  - `tests/Architecture.Tests/MotionLanguageTests.cs`: R4, `PaintTransitionAllowlist = []` with its doc
    comment updated (spec 425 / #2667 emptied it, M2).
  - `e2e/motion.spec.ts`: R5 replaces the spec-292 T002 characterisation; its comment says why (owner
    decision on #2667, spec 292 §4's named fallback).
  - Run and capture **verbatim**: the vitest command, `pnpm --filter @smart-sentinel-eye/shared
    typecheck`, the `MotionLanguage` filter, `pnpm lint`, `pnpm format:check`.
  - **Required:** R1, R3 red; R2 red (if green, stop and report — plan §5a); R4 red on fact 2 naming
    `Button.tsx`, every other fact green; md pin green; existing 14 cases green; typecheck green.
    R5: state "CI-only, not observed red locally (Docker down)".
  - If anything expected red arrives green, stop and report — do not adjust it until it is red.

- [ ] **T020 [US1]** (`frontend-engineer`, depends on T010) Green commit C3: `Button.tsx` per plan §2
  (size lookup, destructure `size = 'md'`, delete `transition-colors duration-state ease-state`; correct
  only the comment text that the change makes false). `e2e/interaction-states.spec.ts:131` comment
  corrected. Re-run T010's commands: everything green, R1-R4 and the md pin included; quote the output.
  No console call-site file changes (`git diff --stat` shows none under `apps/management-*`).

## Phase 2: US2 — the kiosk's action buttons are the shared Button (P2)

- [ ] **T030 [US2]** (`test-writer`, depends on T020) Red commit C4:
  - `tests/Architecture.Tests/KioskButtonPrimitiveTests.cs` (new): R6 (a) no raw `<button>` outside the
    allowlist; (b) allowlist honesty; (c) non-empty scan. Mirror `MotionLanguageTests`' comment
    stripping and `RepositorySource.RelativePath` (forward slashes on every OS).
  - `e2e/kiosk-touch-target.spec.ts` (new): R7, declared a pin in its own comment.
  - Run and capture **verbatim** `dotnet test tests/Architecture.Tests --filter
    FullyQualifiedName~KioskButton`. **Required:** (a) red naming exactly the seven S1-S7 `file:line`s;
    (b), (c) green. Prove (b) and (c) by planted counterfactuals (rename an allowlist match; point the
    scan root at an empty folder), observe each red, revert, and quote both in the PR.
  - `pnpm typecheck:e2e` and `pnpm lint:e2e` green for the new spec file (if `typecheck:e2e` fails, stash
    and re-run on clean develop first — it has failed there before for a missing `@types/node`).

- [ ] **T040 [US2]** (`frontend-engineer`, depends on T030) Green commit C5: migrate S1-S7 per spec §1.1
  (variant per row; `size="touch"`; deep import `@smart-sentinel-eye/shared/ui/primitives/Button`).
  S8, S9 untouched. Re-run: the `KioskButton` and `MotionLanguage` filters, `pnpm --filter
  @smart-sentinel-eye/kiosk-web test`, `typecheck`, `lint`, and root `pnpm format:check`. All green; the
  kiosk tests that find these buttons by role and name pass **unmodified** (plan §5b).

## Phase 3: Verify and review

- [ ] **T050** (`frontend-reviewer`, then phase 5 `/verify`) Review C2-C5. Phase 5 per spec §7: local
  commands; on CI, R5 and R7 green, `interaction-states.spec.ts` green unmodified, and the
  `render-leg-gate` job summary read and stated beside develop run `39493830176`. PR body: the §IV
  citation (composite + render; no new animation, translucency removed at S5; spec §4), the M1
  counterfactual (spec §1.2 V3), the verbatim red outputs of T010 and T030, and the plan §9 follow-up
  issue text (file it, without `agent:ready`, and add it to Project #13).

## Dependencies

T001 → T010 → T020 → T030 → T040 → T050. Strictly sequential: one branch, one index; T040 needs the
`touch` size from T020. C1-C3 form a complete mergeable prefix if review wants US1 alone (plan §7).
