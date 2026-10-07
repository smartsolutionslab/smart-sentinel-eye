# Tasks 310: The record a revoked operator keeps

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2725

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2725 carries `agent:ready`. The threshold (3) and the reset
  rule (any non-403) are a human's decision (issue comment); this spec only applies them.
- **Engineers:** `test-writer` (4a: Vitest only) → `frontend-engineer` (4b: `apps/shared` +
  `apps/management-web`). **No backend, no infra.** The backend already answers 403 for a missing
  scope (`RequireAuthorization(Scope…)`) and 404 for another fab's camera by design.
- **New ADR:** **no** (spec §3, "Why FR-005 keeps FR-008 intact").
- **Phase 4a colour: RED.** No existing assertion is edited.
- **Latency:** N/A.
- **Board:** #2725 is on Project #13 (In Progress, verified 2026-10-07). No per-task issues.
- **Follow-ups to file (need a human decision, not this slice):** counting 404 on the camera detail
  page (spec §1.2.1); refreshing a passive tab (spec §1.2.2).

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files from every other `[P]` task in its group (ADR-0109).

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4a — tests first (test-writer). Run all; quote the red verbatim.

T001–T005 own disjoint files and may run in parallel.

- [ ] **T001** [P] [US1] `apps/shared/src/api/problemDetail.test.ts` — `isForbidden` cases (plan §5 row 1).
- [ ] **T002** [P] [US1] `apps/shared/src/hooks/useRevocationFallback.test.ts` — hook cases (plan §5 row 2).
- [ ] **T003** [P] [US1] `apps/management-web/src/features/cameras/CameraDetailPage.test.tsx` — add a
      `describe('revocation fallback')`: r1–r3 403 → refusal surface identical to a first-load 404
      render (`innerHTML`), no controls, no viewer; r1–r2 → record kept (plan §5 row 3).
- [ ] **T004** [P] [US1] Add one `describe('revocation fallback')` to each of `CamerasPage.test.tsx`,
      `audit/AuditPage.test.tsx`, `layouts/LayoutsPage.test.tsx`, `overlays/OverlaysPage.test.tsx`,
      `systemVariables/SystemVariablesPage.test.tsx` (plan §5 row 4).
- [ ] **T005** [P] [US1] `apps/management-web/src/features/cameras/CameraDetailRevocation.test.tsx` (new) —
      real store, stubbed `fetch`, three Retry clicks, `findBy…` only (plan §5 row 5).
- [ ] **T006** [US1] Run `pnpm --filter @smart-sentinel-eye/shared test` and
      `pnpm --filter @smart-sentinel-eye/management-web test`; capture the verbatim failures for the PR body. Every
      existing test must still pass. Depends on T001–T005.

## Phase 4b — implement (frontend-engineer). Tests may not be edited.

- [ ] **T007** [US1] `apps/shared/src/api/problemDetail.ts` — add `isForbidden` beside `isConflict`
      (plan §1.1). **Blocks T008.**
- [ ] **T008** [US1] `apps/shared/src/hooks/useRevocationFallback.ts` (new) + export from
      `hooks/index.ts` (plan §1.2). **Blocks T009–T014.**
- [ ] **T009** [US1] `CameraDetailPage.tsx` — wire the hook; mask `record` (plan §1.3).
- [ ] **T010** [US1] `CamerasPage.tsx` — hoist `listArgs`; wire; mask `data`.
- [ ] **T011** [US1] `AuditPage.tsx` — wire with `JSON.stringify(applied)`; mask `data`.
- [ ] **T012** [US1] `LayoutsPage.tsx` — wire with `'layouts'`; mask `data`.
- [ ] **T013** [US1] `OverlaysPage.tsx` — wire with `'overlays'`; mask `data`.
- [ ] **T014** [US1] `SystemVariablesPage.tsx` — hoist `variablesArgs`; wire; mask `data`.
- [ ] **T015** [US1] Green run of both workspaces, plus `prettier --check`, `eslint`, `tsc --noEmit`
      (and `typecheck:e2e` if touched — memory: it can fail on a clean develop) for both apps.

T009–T014 touch disjoint files and are formally `[P]` after T008, but each is a one-line wiring;
run them in one engineer pass.

## Phase 5 — verify

- [ ] **T016** Spec §6 procedure against the Aspire stack (detail page and `/cameras`); write the
      verification note on the PR. Latency: N/A.

## Phase 6 — review

- [ ] **T017** `frontend-reviewer` + `security-reviewer` (auth-adjacent). Check FR-005 with
      `grep -rn "error\.status\|isForbidden" apps/management-web/src/features --include=*Page.tsx`
      — empty on develop @ `3f209556`; must stay empty.

## Dependencies

T001–T005 → T006 → T007 → T008 → T009–T014 → T015 → T016 → T017.
