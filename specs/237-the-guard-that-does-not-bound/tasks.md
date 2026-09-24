# Tasks 237 — The guard that does not bound

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2385 · **Phase**: 3 (Tasks)
**Colour**: **red** for T004–T006 (new behaviour). T001–T003 are a characterised move (green before and after).
**Engineer**: `test-writer` (4a) then `infra-engineer` (4b) · **Reviewer**: `infra-reviewer`
**Tracking**: feature-level issue #2385 (already `agent:ready` on Project #13). No per-task issues.

Format: `[ID] [P?] [Story] description`. No foundational (Shared.Kernel / Contracts / AppHost) work.
No `[P]` markers: every task touches `e2e/support/` or `scripts/` in one sequence, and the files
overlap across commits — one agent per phase, no fan-out.

## Phase 4a-0 — the move, characterised (infra-engineer, before test-writer)

- [ ] **T001 [US1]** Run `pnpm test:guards` on unmodified `develop`; capture verbatim (expect the two
  `recoverAndRetry` cases green among the rest).
- [ ] **T002 [US1]** `git mv e2e/support/retire-e2e-cameras.recovery.ts e2e/support/sweep-recovery.ts`;
  `git mv scripts/retire-e2e-cameras-recovery.test.mjs scripts/sweep-recovery.test.mjs`. Update: the
  import at `e2e/support/retire-e2e-cameras.teardown.ts:3`; the import and header path in
  `sweep-recovery.test.mjs`; the header comment of `sweep-recovery.ts` (name both consumers, new test
  path). Function body and test assertions **unmodified**. Do not edit `specs/186-*/tasks.md`.
- [ ] **T003 [US1]** Re-run `pnpm test:guards` (same count, green), `pnpm typecheck:e2e`,
  `pnpm lint:e2e`, `pnpm exec playwright test --list --project=cleanup` (exactly three teardowns).
  Commit `refactor(e2e): move recoverAndRetry to a neutral sweep-recovery module`.

Depends: T001 → T002 → T003.

## Phase 4a — red (test-writer)

- [ ] **T004 [US1]** Create `e2e/support/archive-e2e-layouts.recovery.ts` exporting
  `recoverLayout(now, deadline, timeoutMs, looksSignedOut, signIn, retry)` in **today's unbounded
  shape** (spec §6): `if (await looksSignedOut()) await signIn(); return { attempted: true, result: await retry() };`
  typed as `Promise<RecoveryOutcome<T>>` imported from `./sweep-recovery`. Not wired into the teardown.
- [ ] **T005 [US1]** Create `scripts/archive-e2e-layouts-recovery.test.mjs` with R1, R2, R3, G1 exactly
  as spec §6, each `{ timeout: 2_000 }`, mirroring `scripts/sweep-recovery.test.mjs`'s style. Run
  `pnpm test:guards`; capture verbatim. **Required**: R1 and R2 fail by assertion, R3 fails by the
  2000 ms test timeout, G1 passes. Any other pattern → stop and report. `typecheck:e2e` and
  `lint:e2e` must be clean. Commit
  `test(e2e): guard the layout teardown's re-sign-in against running unbounded` (T004 + T005).

Depends: T003 → T004 → T005.

## Phase 4b — the fix (infra-engineer; may not edit T005's test file)

- [ ] **T006 [US1]** `archive-e2e-layouts.recovery.ts`: body becomes
  `recoverAndRetry(now, deadline, timeoutMs, async () => { if (await looksSignedOut()) await signIn(); }, retry)`.
  One-line header *why* (the seam exists so the call site's guard+bound composition is testable).
- [ ] **T007 [US1]** `e2e/support/archive-e2e-layouts.teardown.ts`: add `RE_SIGN_IN_TIMEOUT_MS = 30_000`
  with its *why*; replace `:80-82` with the plan's call-site shape (`!attempted` → `outOfTime = true`,
  skip, `break`); update the `:77-79` comment (both halves bounded; bound-and-skip, not in-place
  retry; cite #2382's mechanism). Leave `cleanup.setTimeout(600_000)`, `DEADLINE_MS`, `archiveByName`,
  `looksSignedOut`, `disposableNames` untouched.
- [ ] **T008 [US1]** `pnpm test:guards` all green (T005's file unmodified since its commit),
  `typecheck:e2e`, `lint:e2e`, `playwright test --list --project=cleanup` (three teardowns).
  Counterfactual: revert T006's body to the T004 shape → R1–R3 red, G1 green, captured verbatim;
  restore; `git diff` empty. Commit `fix(e2e): bound the layout teardown's re-sign-in to its own budget`.

Depends: T005 → T006 → T007 → T008.

## Bookkeeping (orchestrator)

- [ ] **T009** PR body: `Closes #2385`; quote T005's red and T008's counterfactual verbatim; state the
  `cleanup.setTimeout` finding (600 s, 3 × 600 = 30 min < 45-min job; unchanged); record the CI
  duration effect as a prediction. Re-check spec number 237 against unmerged branches before merge.
  After merge, confirm #2385 closed.

## Out of scope — do not do

- `e2e/support/sign-in.ts` · `archive-e2e-overlays.teardown.ts` · any `cleanup.setTimeout` value ·
  `recoverAndRetry`'s body or signature · `page.goto` navigation timeouts · `specs/186-*`.
