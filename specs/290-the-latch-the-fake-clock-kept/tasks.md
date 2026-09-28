# Tasks: The latch the fake clock kept

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2641

**Engineer**: `frontend-engineer` · **New ADR**: no · **Phase-4a colour**: behaviour-preserving
(characterisation, test-harness only) — evidence is the counterfactual pair, not a new red test.

Format: `[ID] [P?] [Story] Description`. No `[P]` tasks: every task touches the same test file or
depends on the one before it. No foundational (Shared.Kernel / Contracts / AppHost) work.

## Phase 4a — characterise and reproduce (`test-writer`, before any edit)

- [ ] **T001** [US1] Run `OverlayEditorDialogResolvePreview.test.tsx` unmodified
  (`pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx`)
  and record the verbatim output — expected 6/6 green (characterisation baseline).
- [ ] **T002** [US1] Build the **uncommitted** stall probe per plan.md *Verification protocol* §1
  (`ZzLatchProbe.test.tsx` beside the real file: a copy of today's file plus the captured
  `realSetTimeout` and one `await new Promise((r) => realSetTimeout(r, 600));` as the last line of
  *"Never flags…"*' `try`). Run it and record the verbatim output. **Expected red**: exactly the two
  tests fail at ~10 s with `Unable to find an element by: [data-testid="placeholder-preview-error"]`.
  If it does *not* go red, stop and report — the hypothesis is wrong and the plan does not hold.
  Leave the probe file in place for T006; do not commit it.

## Phase 4b — fix (`frontend-engineer`, receives T001/T002 output as its brief)

- [ ] **T003** [US1] In `OverlayEditorDialogResolvePreview.test.tsx`, add a local `createStore()`
  (overlaysApi + systemVariablesApi, via dynamic import after `vi.stubEnv`), create a fresh store in
  `beforeEach`, render into it, and remove the `app/store.js` import and the `resetApiState` call.
  No `expect(` line may change. Depends on T002.
- [ ] **T004** [US1] Same file, comments only: replace the singleton-store rationale with the reason
  each test gets its own store (the auto-batch notification latch survives `resetApiState`), and
  drop "the reason is the deadline rather than the assertion" from the #2520 guard's doc comment.
  Depends on T003.
- [ ] **T005** [US1] `apps/management-web/src/test/setup.ts`, comment-only: correct the #2520/#2419
  causal paragraph to name spec 290's mechanism; keep `asyncUtilTimeout: 10_000` and the
  fake-timer warning. Prove comment-only by hashing comment-stripped content before/after.
  Depends on T003 (independent file, but sequenced to keep one commit per concern).

## Phase 4b/5 — prove it

- [ ] **T006** [US1] Regenerate the stall probe from the **fixed** file (same one-line stall); run it
  10 times — all 6/6 green. Record verbatim output. Delete the probe. Depends on T003.
- [ ] **T007** [US1] Run the fixed real file 20 consecutive times (all 6/6) and the full
  `management-web` suite once (green). Confirm `git diff` contains no added/removed `expect(` line.
  Depends on T004, T005, T006.

## Dependencies

T001 → T002 → T003 → T004 → T005; T003 → T006; {T004, T005, T006} → T007.

## Follow-up (not tasks here)

- File an issue to check `OverlayEditorDialog.test.tsx`, `CameraViewerLifecycle.test.tsx` and
  `CameraViewerAlignment.test.tsx` — all use the app singleton store with `vi.useFakeTimers()` —
  for the same latch.
