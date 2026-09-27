# Tasks 274: The banner every page repeats

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2523 (feature-level; no
per-task issues)
**Engineer**: `frontend-engineer` for both 4a and 4b (this is characterisation, not a red/green
split across two agents the way new behaviour is — see Phase 4a colour below). **Reviewer**:
`frontend-reviewer`.
**Phase 4a colour**: **CHARACTERISATION (behaviour-preserving). NOT red.** The seven call sites
render the same DOM, same text, same click behaviour before and after. The only new-file,
new-test work is `RetryBanner.test.tsx` (pins the new component's own contract — nothing to
characterise, since the component has no prior behaviour) and `WallsPage.test.tsx` (a
characterisation test for a site that had none — written first, captured green on the
pre-change inline banner, then must pass unmodified after the swap). **An existing test that has
to be edited to pass after a swap means the DOM moved: stop and report, do not adjust it.**

Format: `[ID] [P?] [Story] description, file(s)`.

**Board.** Issue #2523 is feature-level. Add it to Project #13 by hand
(`gh project item-add 13 --owner smartsolutionslab --url <issue-url>`) — orchestrator's step, not
a task here (CLAUDE.md Phase 3).

**Contention files**: none. Every task below owns a file no other task in this set writes.

**Fan-out (ADR-0109).** T1 (the new composite + its test) blocks every call-site task, because
each imports it. Once T1 lands, T2a–T2g (the seven call-site swaps) are fully disjoint and can
run in parallel — each edits exactly one page file (T2f also adds one new test file no other task
touches).

## Pre-work

- [ ] **T0** Re-check the spec number is still free: `git worktree list` from
  `D:\Github\smart-sentinel-eye`, then `ls specs` in every worktree it lists, plus
  `git ls-tree -d --name-only origin/<branch> -- specs` for every `origin/*` branch not checked
  out. Confirm nothing claims `274-*`. If something does, renumber before continuing (memory:
  *spec number: origin/develop isn't enough*).

## Phase 4a: characterisation baseline + the one new test (frontend-engineer)

- [ ] **T1** [P] New file `apps/shared/src/ui/composites/RetryBanner.tsx` (plan §2) and
  `apps/shared/src/ui/composites/RetryBanner.test.tsx`: assert the `role="alert"`, the exact
  class string (plan §2), that `message` renders verbatim, and that clicking the "Retry" button
  calls `onRetry` exactly once with no arguments. Run
  `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/ui/composites/RetryBanner.test.tsx`
  and quote the result verbatim.
  - **Depends on:** T0. **Blocks:** T2a–T2g.
- [ ] **T2-baseline** [P] Before touching any call site, run each of the six existing covering
  tests below and quote the pass line verbatim (this is the "before" for FR-004):
  - `CamerasPage.test.tsx` — "Shows a retry control when the list query fails"
  - `AuditPage.test.tsx` — "Shows a retry control when the search query fails"
  - `LayoutsPage.test.tsx` — "Shows a retry control when the list query fails"
  - `OverlaysPage.test.tsx` — "Shows a retry control when the list query fails"
  - `SystemVariablesPage.test.tsx` — "Shows a retry control when the list query fails"
  - `CameraDetailPage.test.tsx` — "Retries the refresh when the operator presses Retry"
  - **Depends on:** T0. **Blocks:** T2a–T2e, T2g (each site's own "after" run is compared against
    this quote).
- [ ] **T2f-baseline** [US-Walls] `apps/management-web/src/features/walls/WallsPage.tsx` has no
  test file (confirmed absent, spec §1.2). Write **new** `WallsPage.test.tsx` with a "Shows a
  retry control when the list query fails" case, mirroring `CamerasPage.test.tsx`'s mock shape
  (`data`, `isLoading`, `isFetching`, `error`, `refetch`). Capture it passing against the
  **current, pre-change** inline banner and quote the result verbatim. This is new-test-for-an-
  uncovered-site work, not new behaviour — the banner it tests already exists and is unchanged by
  writing the test.
  - **Depends on:** T0. **Blocks:** T2f.

## Phase 4b: the seven call-site swaps (frontend-engineer; existing tests may not be edited)

- [ ] **T2a** [P] [US2] Swap `CamerasPage.tsx:123-131` (plan §3) to
  `<RetryBanner message="Could not load cameras." onRetry={() => void refetch()} />`. Add the
  import from `@smart-sentinel-eye/shared/ui/composites/RetryBanner`. Re-run T2-baseline's
  `CamerasPage.test.tsx` case; it must pass **unmodified**.
  - **Depends on:** T1, T2-baseline.
- [ ] **T2b** [P] [US2] Same for `AuditPage.tsx:165-173`, message "Could not load the audit
  trail." Re-run its baseline case unmodified.
  - **Depends on:** T1, T2-baseline.
- [ ] **T2c** [P] [US2] Same for `LayoutsPage.tsx:118-127` (the `error` banner only — leave the
  `mutationError` banner at :130-146 untouched, spec §1.3), message "Could not load layouts."
  Re-run its baseline case unmodified.
  - **Depends on:** T1, T2-baseline.
- [ ] **T2d** [P] [US2] Same for `OverlaysPage.tsx:110-119` (the `error` banner only, leave
  `mutationError` at :122-138 untouched), message "Could not load overlays." Re-run its baseline
  case unmodified.
  - **Depends on:** T1, T2-baseline.
- [ ] **T2e** [P] [US2] Same for `SystemVariablesPage.tsx:103-112` (the `error` banner only,
  leave `mutationError` at :115-127 untouched), message "Could not load variables." Re-run its
  baseline case unmodified.
  - **Depends on:** T1, T2-baseline.
- [ ] **T2f** [P] [US-Walls] Swap `WallsPage.tsx:34-43`, message "Could not load walls." Re-run
  T2f-baseline's new test; it must pass **unmodified** from what T2f-baseline captured.
  - **Depends on:** T1, T2f-baseline.
- [ ] **T2g** [P] [US3] Swap `CameraDetailPage.tsx:128-137`, message "Could not refresh this
  camera — what you see may be out of date." Re-run its baseline case unmodified, and confirm the
  record's fields (name, address controls, back link) still render alongside the banner (spec
  §1, US3).
  - **Depends on:** T1, T2-baseline.

## Verification (frontend-engineer, after T2a–T2g)

- [ ] **T3** Run the full frontend suite: `pnpm lint`, `pnpm typecheck`, `pnpm test`,
  `pnpm format:check`, `pnpm build`. All green, no suppression added anywhere (spec FR-006).
- [ ] **T4** `git diff origin/develop -- '**/*.test.tsx'` — confirm the only content difference
  is the **addition** of `WallsPage.test.tsx`; every other test file byte-identical to
  `origin/develop`.
- [ ] **T5** `grep -rn "border-accent-fault/40" apps/management-web apps/shared` — confirm the
  seven sites now show `RetryBanner` usage, not the inline class string, and that the six sites
  in spec §1.3 are unchanged (still inline, still out of scope). Quote the result in the PR body.

## Phase 5 (orchestrator / verify agent)

- [ ] **T6** Boot the stack, force each of the seven queries to fail (DevTools request blocking
  or a stopped dependency), and confirm banner text, role and Retry behaviour match spec §3's
  table exactly. Confirm CameraDetailPage's record still renders beneath its banner. Record in
  the verification note (plan §6). No latency figure needed (§IV N/A).

## Phase 6 (orchestrator / reviewers)

- [ ] **T7** `frontend-reviewer` on the diff: confirm no file outside plan §0's list changed, no
  test was edited (only `WallsPage.test.tsx` added), and that the six sites in spec §1.3 were
  correctly left alone.

## Phase 7 (orchestrator)

- [ ] **T8** `gh pr create --base develop`. Body cites: the T2-baseline and T2f-baseline "before"
  quotes, the T2a–T2g "after" quotes (all unmodified), T4's diff confirmation, T5's grep
  confirmation, and `Closes #2523`. Note in the body that `WallsPage.tsx` (site 6) was found by
  grep, not named in the issue, and included per spec §1.2/§5 A1 — flag it for the reviewer to
  confirm or drop.

## Dependencies

```
T0 ──→ T1 ─────────────────────────────────────────┐
   └─→ T2-baseline ──────────────────────┐          │
   └─→ T2f-baseline ──┐                  │          │
                       ▼                  ▼          ▼
                      T2f          T2a T2b T2c T2d T2e T2g
                       │                  │
                       └────────┬─────────┘
                                ▼
                          T3 → T4 → T5 → T6 → T7 → T8
```
