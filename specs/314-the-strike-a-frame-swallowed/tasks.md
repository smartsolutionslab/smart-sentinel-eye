# Tasks 314: The strike a frame swallowed

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2762

## Phase-3 declarations

- **Engineers:** `test-writer` (4a: Vitest only) → `frontend-engineer` (4b: `apps/shared` +
  `apps/management-web`). No backend, no infra.
- **New ADR:** no (spec header).
- **Phase 4a colour: RED.** Behaviour-changing bug fix. Existing tests change **harness only**
  (Provider wrapper, inert source, listener middleware in own-store tests) — no assertion or case
  is edited; an assertion that needs editing is a block, not an adjustment.
- **Latency:** N/A (spec §5).
- **Board:** feature-level issue #2762 must be on Project #13 (verify by `content.url`,
  `--limit 2000`). No per-task issues.
- **Sequencing:** #2750 / PR #2775 edits the same hook (plan §6). T001 decides the base.

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files from every other `[P]` task in its group (ADR-0109).

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Gate before phase 4

- [ ] **T001** If PR #2775 has merged: `git fetch origin && git rebase origin/develop`, and fold
  plan §6 items 1-5 into T003/T005/T007/T009 below instead of resolving them later. If it has not
  merged, proceed on the current base and resolve §6 at rebase time.

## Phase 4a — tests first (test-writer). Run all, plus `tsc --noEmit`; quote the red verbatim.

T002 blocks T003. T004 and T005 are `[P]` with each other and with T003 (disjoint files).

- [ ] **T002** [US1] `apps/shared/src/test/revocationHarness.tsx` (new) — held-autobatch store
  (`autoBatch: { type: 'callback' }`), prepended listener middleware, scripted test `createApi`,
  Provider wrapper, render-log `Probe` (plan §4 row 1). Also a store variant **without** the
  listener middleware for the FR-006 case.
- [ ] **T003** [US1] `apps/shared/src/hooks/useRevocationFallbackCoalesced.test.tsx` (new) — every
  spec §1 scenario, each "dropped" case with its render-log precondition assertion (plan §4 row 2).
- [ ] **T004** [P] [US1] `apps/shared/src/hooks/useRevocationFallback.test.ts` — harness only:
  `renderFallback` / `renderFallbackStrict` get the Provider wrapper and an inert source (plan §4
  row 3).
- [ ] **T005** [P] [US1] `apps/management-web/src/features/cameras/CameraDetailRevocation.test.tsx`
  and `CameraDetailPageNavigation.test.tsx` — prepend the listener middleware in each
  `createStore()` (plan §4 row 4). (After #2775: also `CameraDetailFabRemoval.test.tsx` and
  `useRevocationFallbackNotFound.test.ts`, plan §6 items 3-4.)

Expected red: T003's dropped-strike, dropped-reset, fully-coalesced and missing-middleware cases,
each on its outcome assertion, with the precondition assertions **green**; `tsc --noEmit` reports
the third argument. Expected green, to be reported as green: T003's fences (outline, subject
change, StrictMode), T004, T005.

## Phase 4b — implementation (frontend-engineer). Tests from 4a may not be edited.

T006 blocks T007-T008. T007 and T008 own disjoint files and may run in parallel.

- [ ] **T006** [US1] `apps/shared/src/hooks/useRevocationFallback.ts` — `RevocationSource`,
  required third parameter, `applySettlement`, listener effect with the FR-006 throw, docblock
  (plan §1.2). Export `RevocationSource` from `apps/shared/src/hooks/index.ts`. Move `react-redux`
  from `devDependencies` to `dependencies` in `apps/shared/package.json` + lockfile (plan §3).
- [ ] **T007** [P] [US1] `apps/management-web/src/app/store.ts` — prepended
  `createListenerMiddleware().middleware`, why-comment (plan §1.3).
- [ ] **T008** [P] [US1] The six call sites in plan §1.4 — third argument, args identical to the
  page's `useXQuery` argument.
- [ ] **T009** [US1] Full gates: `prettier --check`, eslint, `tsc --noEmit` (both apps), full
  Vitest for `apps/shared` and `apps/management-web` — twice. Every pre-existing test passes with
  its assertions unmodified.

## Phase 5–7

- [ ] **T010** Phase 5 — spec §6 steps 1-3 (step 2's 8-vs-8 rAF-delay counterfactual is
  mandatory; quote both counts). Verification note on the PR.
- [ ] **T011** Phase 6 — `frontend-reviewer`. (`security-reviewer` optional: the change alters
  *when* a revoked record leaves the screen, not who may see it.)
- [ ] **T012** Phase 7 — re-check spec number 314 across `origin/develop`, remote branches and
  worktrees; PR to `develop` (`--base develop`); body quotes the T003 red and the precondition
  greens; `Closes #2762`.
