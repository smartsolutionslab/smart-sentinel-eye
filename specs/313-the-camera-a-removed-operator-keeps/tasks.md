# Tasks 313: The camera a removed operator keeps

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2750

## Phase-3 declarations

- **Decision:** a human's, this session (spec §0): a 404 after a cached 200 counts as a strike,
  CameraDetailPage only.
- **Engineers:** `test-writer` (4a: Vitest only) → `frontend-engineer` (4b: `apps/shared` +
  `apps/management-web`). No backend, no infra.
- **New ADR:** no (spec §3).
- **Phase 4a colour: RED.** No existing assertion is edited.
- **Latency:** N/A.
- **Board:** feature-level issue #2750 must be on Project #13 (verify by `content.url`,
  `--limit 2000`). No per-task issues.
- **Contention:** #2762 (`sse-2762c`) — see plan §6.

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files from every other `[P]` task in its group (ADR-0109).

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4a — tests first (test-writer). Run all, plus `tsc --noEmit`; quote the red verbatim.

T001–T003 own disjoint files and may run in parallel.

- [ ] **T001** [P] [US1] `apps/shared/src/api/problemDetail.test.ts` — append `describe('isNotFound')` (plan §5 row 1).
- [ ] **T002** [P] [US1] `apps/shared/src/hooks/useRevocationFallbackNotFound.test.ts` (new) — hook cases incl. the `notFoundRevokes` false/omitted fences (plan §5 row 2).
- [ ] **T003** [P] [US1] `apps/management-web/src/features/cameras/CameraDetailFabRemoval.test.tsx` (new) — real store, stubbed fetch, 200→404×2 / 200→404×3 / first-load 404 (plan §5 row 3).

Expected red: T001 type/compile failure on the missing export; T002 the `true` cases; T003 the
three-404 case (heading stays "Line-1-Entrance"). The fence cases (omitted/false flag, first-load
404, two 404s) are expected green and must be reported as such, not as failures.

## Phase 4b — implementation (frontend-engineer). Tests above may not be edited.

Sequential: T005 depends on T004, T006 on T005.

- [ ] **T004** [US1] `apps/shared/src/api/problemDetail.ts` — add `isNotFound` beside `isForbidden` (plan §1.1).
- [ ] **T005** [US1] `apps/shared/src/hooks/useRevocationFallback.ts` — optional `notFoundRevokes` on `RevocationQueryState`; strike condition; doc comment (plan §1.2).
- [ ] **T006** [US1] `apps/management-web/src/features/cameras/CameraDetailPage.tsx` — pass `notFoundRevokes: currentData !== undefined`, with the why-comment (plan §1.3).
- [ ] **T007** [US1] Full gates: `prettier --check`, eslint, `tsc --noEmit` (both apps), full Vitest for `apps/shared` and `apps/management-web`. Existing spec 310 tests pass unmodified.

## Phase 5–7

- [ ] **T008** Phase 5 — spec §6 procedure against the Aspire stack; note on the PR.
- [ ] **T009** Phase 6 — `frontend-reviewer` (+ `security-reviewer`: touches an authorization-visible surface, FR-008 argument in spec §3).
- [ ] **T010** Phase 7 — re-check spec number 313; PR to `develop` (`--base develop`), body quotes the T001–T003 red, `Closes #2750`.
