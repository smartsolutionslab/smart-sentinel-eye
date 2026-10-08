# Tasks 317: The tab left alone

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2751

## Phase-3 declarations

- **Engineers:** `test-writer` (4a: Vitest only) → `frontend-engineer` (4b: `apps/management-web`
  production code; `apps/shared` gets test code only). No backend, no infra.
- **New ADR:** no — implements the user's 2026-10-08 focus-refresh decision with RTK Query's
  documented built-in mechanism inside ADR-0075 (spec header).
- **Phase 4a colour: RED.** Behaviour-changing: six pages that never refetched on focus now do.
  Row 2 of plan §4 is a set of **green fences** (composition with #2762) and must be reported as
  such — it is evidence that nothing in the hook needs to change, not the red. The red is rows 3-9.
  No existing assertion or case is edited; one existing comment is reworded (T014).
- **Latency:** N/A (spec §6).
- **Board:** feature-level issue #2751 must be on Project #13 (verify by `content.url`,
  `--limit 2000`). No per-task issues.
- **Sequencing:** T001 (contention with spec 316) gates phase 4. T002 blocks T004-T009 (shared
  helper). T003 is independent of T002.

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files from every other `[P]` task in its group (ADR-0109).

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Gate before phase 4

- [ ] **T001** `git fetch origin`. If `feat/316-operator-mfe-shell-and-first-remote` (spec 316) has
  merged, rebase and re-locate `main.tsx` and the two `features/cameras/*` call sites per plan §6
  before writing tests. Re-check the spec number (317) against `origin/develop` and every remote
  branch.

## Phase 4a — tests first (test-writer). Run all, plus `tsc --noEmit`; quote the output verbatim.

T002 blocks T004-T009. T003, and T004-T009 with each other, are `[P]` (disjoint files).

- [ ] **T002** [US1] `apps/management-web/src/test/focusRefetch.ts` (new) — the plan §4 row 1
  helper. `installFocusListeners` MUST register its own `afterEach` unsubscribe (module-global
  `initialized` flag in RTK's `setupListeners`).
- [ ] **T003** [P] [US1] `apps/shared/src/test/revocationHarness.tsx` — **additive** `subscribe(args,
  { refetchOnFocus })` helper (a real subscription; `Probe` uses `useQueryState`, which does not
  subscribe and so is invisible to focus refetch). Existing exports unchanged. Then
  `apps/shared/src/hooks/useRevocationFallbackFocus.test.tsx` (new) — plan §4 row 2 fences, each
  "held notifications" case with its render-log precondition.
- [ ] **T004** [P] [US1] `apps/management-web/src/app/focusRefetch.test.ts` (new) — plan §4 row 3,
  including the opted-out subscription and the install-twice case.
- [ ] **T005** [P] [US1] `features/cameras/CameraDetailFocusRefetch.test.tsx` (new) — plan §4 row 4,
  including all three dialogs' suspend/resume.
- [ ] **T006** [P] [US1] `features/cameras/CamerasPageFocusRefetch.test.tsx` (new) — plan §4 row 5;
  streams-poll requests excluded from counts.
- [ ] **T007** [P] [US1] `features/audit/AuditPageFocusRefetch.test.tsx` (new) — row 6.
- [ ] **T008** [P] [US1] `features/layouts/LayoutsPageFocusRefetch.test.tsx` (new) — row 7.
- [ ] **T009** [P] [US1] `features/overlays/OverlaysPageFocusRefetch.test.tsx` (new) — row 8.
- [ ] **T009a** [P] [US1] `features/systemVariables/SystemVariablesPageFocusRefetch.test.tsx` (new) — row 9.

Report per row: red/green, and for every "vacuously green" row the red row beside it that proves
the mechanism engaged (plan §4 anti-tautology).

## Phase 4b — implementation (frontend-engineer). May not edit any 4a test.

T010 is foundational for the app (the option is inert without it) but touches files disjoint from
T011-T013, so all four may run in parallel; none is observable green until T010 lands.

- [ ] **T010** [US1] `apps/management-web/src/app/store.ts` — export the listener-install function
  (plan §1.2, FR-001; no module-level side effect). `apps/management-web/src/main.tsx` — call it
  once before render.
- [ ] **T011** [P] [US1] `features/cameras/CameraDetailPage.tsx` — `refetchOnFocus: !(editing ||
  renaming || retiring)` + why-comment citing D2 (FR-003). `features/cameras/CamerasPage.tsx` —
  `refetchOnFocus: true` on `useListCamerasQuery` only (FR-004).
- [ ] **T012** [P] [US1] `features/audit/AuditPage.tsx`, `features/layouts/LayoutsPage.tsx` —
  `refetchOnFocus: true` + why-comment.
- [ ] **T013** [P] [US1] `features/overlays/OverlaysPage.tsx`,
  `features/systemVariables/SystemVariablesPage.tsx` — `refetchOnFocus: true` + why-comment.
- [ ] **T014** [US1] `features/cameras/CameraDetailRevocation.test.tsx:96-98` — reword the
  "never calls `setupListeners`" comment (plan §1.4). Comment-only; no code or assertion change.
- [ ] **T015** [US1] Full `apps/shared` + `apps/management-web` Vitest **twice**, `tsc --noEmit`,
  eslint, `prettier --check` for both (memory: eslint clean ≠ prettier clean). Quote the 4a rows
  now green.

## Phase 5 — verify

- [ ] **T016** [US1] spec §7 step 3 on the Aspire stack: one request per tab return per page (not
  two); none while a camera dialog is open; three returns after revocation + token renewal → refusal
  surface. Write the observation on the PR.

## Phase 6–7

- [ ] **T017** `/code-review` (frontend-reviewer). Not security-sensitive beyond spec 310's existing
  surface; `/security-review` optional.
- [ ] **T018** PR to `develop` (`--base develop`), body quotes the 4a red verbatim and labels the
  T003 fences, closes #2751.
