# Tasks: A republished overlay must not leave its tile blank

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Issue**: #3275, on Project #13 (In Progress, checked 2026-11-15
through the issue's `projectItems`). No per-task issues.

**Engineer**: frontend (kiosk-web only). **No** .NET, contract, AppHost, realm or e2e-spec change.

**Phase 4a colour: red (behaviour-changing).** T002 must be observed failing on the unfixed code, its output quoted verbatim in the PR. A
T002 that arrives green is a phase-4 failure: stop and report, do not adjust it. T003 is a guard expected green before and after.

**Never touched**: `e2e/kiosk-paints-a-reordered-overlay.spec.ts` (its header: a red result is a finding, not a thing to edit),
`playwright.config.ts` and its `retries`, existing tests in `CellPageLiveUpdates.test.tsx` (must pass **unmodified**), the backend.
Prettier and eslint are separate checks; run both.

**§IV: leg touched: Event → overlay state.**

Format: `[ID] [P?] [Story] Description`. Single story, strictly sequential: every task touches one of the same two files, so nothing is `[P]`.

## Phase 1 — Baseline

- [x] **T001** [US1] On the branch, clean tree: run the kiosk-web unit suite and record the pass count for
      `CellPageLiveUpdates.test.tsx`. If anything is already red, stop and report it.

## Phase 2 — Red first (test-writer)

- [x] **T002** [US1] In `apps/kiosk-web/src/features/cell/CellPageLiveUpdates.test.tsx`, inside
      `describe('Pushed overlay archive and publish frames (spec 229 US1, #2321)')`, add
      *Does not flag a tile when the superseded revision's archive frame arrives after the new revision's publish frame*:
      `aTwoTileWall()`; `act` → `capturedCallbacks.onOverlayPublished({ overlay: 'ov-1', revisionNumber: 2, … })`; `act` →
      `capturedCallbacks.onOverlayArchived({ overlay: 'ov-1', revisionNumber: 1, … })`; assert `unavailableBadgeIn(tileA)` is not in the
      document. Reuse the existing helpers and the publish-frame body shape of the neighbouring test. Run it; capture the **verbatim red
      output**. Depends on T001.
- [x] **T003** [US1] Same file and `describe`: add *Still flags a tile when the archive frame names the revision last pushed as published*
      (`Published(ov-1, 2)` then `Archived(ov-1, 2)`; badge present). Run it; expected **green** on unfixed code; record that. Depends on T002.

## Phase 3 — Fix (frontend-engineer; receives T002's output as its brief, may not edit T002/T003)

- [x] **T004** [US1] In `apps/kiosk-web/src/features/cell/useOverlayHubHandlers.ts`: add a per-overlay newest-published-revision ref
      (`useRef<Map<string, number>>`), set it to the max in `onOverlayPublished` before the bound-overlay guard, and in `onOverlayArchived`
      return without flagging when `message.revisionNumber` is strictly lower than it (plan "Where the change lives"). One short
      why-comment citing ADR-0088; no spec/task numbers in source. Depends on T003.
- [x] **T005** [US1] Run the kiosk-web suite: T002 and T003 green, every pre-existing test green and unmodified. Then the counterfactual:
      reintroduce `Math.max` in the Published handler, confirm the two sibling-file tests go red, revert, re-run green; record both outputs
      for the PR. (The original `<` to `<=` counterfactual applied to the superseded max design.) Depends on T004.
- [x] **T006** [US1] `prettier --check`, `eslint --max-warnings 0`, `tsc --noEmit` for kiosk-web; all clean. Depends on T005.

## Phase 4 — Verify (CI)

- [x] **T007** [US1] After the PR's CI run completes, download the Playwright report artifact holding the `kiosk` project and record in the
      PR's verification note the outcome of `kiosk-paints-a-reordered-overlay` (expected: passed, first attempt; SC-002) with the run id.
      If flaky or failed, read that attempt's `error-context.md` for the *Overlay unavailable* badge and report; do not re-run to green.
      Depends on T006 and the PR.
      Recorded: run 43730216915 (PR #3276, head `69cd5bc1`, success). `kiosk-paints-a-reordered-overlay` passed on its first attempt in 5.7 s
      (shard 4/4); no shard reported a flaky test (48, 42, 32 and 40 passed). SC-002 holds for that run; one clean first-attempt pass is
      evidence, not proof, and the frame order stays inferred (traces do not record WebSocket frames).

## Dependencies

T001 → T002 → T003 → T004 → T005 → T006 → (PR) → T007. No foundational/shared work; nothing to fan out.
