# Tasks 468 — The test helpers every API test rebuilds live in one place

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3196 (feature-level issue, on Project #13).
Engineer: **frontend**. Phase 4a colour: **new module → red first (T001–T002); migrations →
behaviour-preserving, characterisation (the migrated files are the covering tests)**.

Three PRs, one app each (plan §Delivery). Every PR runs the same **before/after record** (T-A, T-B,
T-C below) and the same gates (T-D). Frontend scripts, per app:
`npm run -w <app> test | lint | typecheck`; repo root `npm run format:check`.

## Phase 1 — PR 1: the module + management-cameras

### User story 1 (P1) — `jsonResponse` / `problemResponse` from one import (FR-001, FR-002, FR-005–FR-009)

- [x] **T001 [US1]** (test-writer) Create `apps/shared/src/test/responses.test.ts` **before** the module
  exists. Cases: `jsonResponse(body)` → status 200; `jsonResponse(body, 404)` → status 404; body text equals
  `JSON.stringify(body)`; `Content-Type` is `application/json`; `problemResponse(404)` → status 404 and JSON
  body `{ "title": "PROBLEM", "status": 404 }`. Run `npm run -w shared test -- responses`; **quote the red
  output verbatim** (expected: module not found). Test-only commit, `test(468): … (red)`.
- [x] **T002 [US1]** (frontend) Create `apps/shared/src/test/responses.ts` (`jsonResponse`, `problemResponse`,
  the exact code of the 22 identical copies — plan §R1) and add `"./test/responses": "./src/test/responses.ts"`
  after `./test/focusRefetch` in `apps/shared/package.json`. Do **not** touch `src/index.ts` (FR-005).
  T001's tests pass **unmodified**. Depends on T001.
- [x] **T003 [US1]** (test-writer) **Counterfactual, FR-006/SC-004.** Break `responses.ts` three ways, one at
  a time — status default `200`→`201`, drop the `Content-Type` header, drop `status` from the problem
  body — run `responses.test.ts` each time, **quote each failure**, restore the file (`git diff` empty).
  Not committed. Depends on T002.
- [x] **T004 [US1]** (frontend) Record the **before** state for management-cameras *on the commit before any
  migration* (**T-A**): `npm run -w management-cameras test` green; write the sorted test-name list and
  the sorted assertion-line list (`expect(` … lines) of the 8 files below to the scratchpad.
  Depends on T002.
- [x] **T005 [US1]** (frontend) Migrate the 8 `jsonResponse` files in `apps/management-cameras/src/features/cameras/`
  — `CameraDetailFabRemoval`, `CameraDetailFocusRefetch`, `CameraDetailPageNavigation`,
  `CameraDetailPageRetryRecovery`, `CameraDetailRevocation`, `CamerasPageFocusRefetch`,
  `CamerasPageRetryRecovery`, `EditCameraAddressDialog` (`.test.tsx`): delete the local
  `jsonResponse` (and the local `problemResponse` in `CameraDetailFocusRefetch` and `CamerasPageFocusRefetch`),
  import from `@smart-sentinel-eye/shared/test/responses`. **One commit per file**, each green on its
  own. A file whose helper differs from the shared one keeps it with a comment (FR-008). Depends on T004.

### User story 2 (P2) — `createApiStore` instead of a hand-built store (FR-003)

- [x] **T006 [US2]** (frontend) Migrate the 4 hand-built stores in management-cameras (`CameraDetailFocusRefetch`,
  `CameraDetailPageRetryRecovery`, `CamerasPageFocusRefetch`, `CamerasPageRetryRecovery` `.test.tsx`) to
  `createApiStore([...])` from `@smart-sentinel-eye/shared/store`; delete the local
  `createListenerMiddleware`. All four already prepend one, so they are the same store (plan §R2).
  One commit per file. **Run each focus-refetch file alone and in the full suite**
  (`setupListeners` is module-global — plan §Risks). Depends on T005 (same files).

### Gates for PR 1

- [x] **T007** (frontend) **T-A after:** repeat T004's two lists; they must diff **empty** (SC-003, SC-005).
  Then **T-D**: `npm run -w management-cameras lint|typecheck|test`, `npm run -w shared lint|typecheck|test`,
  `npm run format:check`, and the .NET architecture tests (they scan `.tsx`). Depends on T006.
- [x] **T008** (orchestrator) PR to `develop` (`gh pr create --base develop`): body quotes T001's red output,
  T003's three failures, the T-A empty diff; `Phase 5` verification note = T007 results. Park; do not wait on CI.

## Phase 2 — PR 2: management-web (after PR 1 merges and the branch is rebased)

- [x] **T009 [US1]** (frontend) **T-B before:** record test names and assertion lines of the files in T010–T011
  on a rebased branch. Depends on T008 merged.
- [x] **T010 [US1]** (frontend) Migrate the 9 `jsonResponse` files — `apps/management-web/src/app/focusRefetch.test.ts`;
  `features/audit/AuditPageFocusRefetch`, `AuditPageRetryRecovery`; `features/layouts/LayoutEditorDialogChainRetention`;
  `features/overlays/OverlayEditPageNavigation`; `features/rules/RulesPageRetryRecovery`;
  `features/systemVariables/SystemVariablesPageFocusRefetch`, `SystemVariablesPageRetryRecovery`;
  `features/walls/WallsPageRetryRecovery`. Also `problemResponse` in `AuditPageFocusRefetch` and
  `SystemVariablesPageFocusRefetch`. One commit per file. **Do not touch** `OverlaysPage*` / `LayoutsPage*`
  `FocusRefetch`/`RetryRecovery` (plan §R4). Depends on T009.
- [x] **T011 [US2]** (frontend) Migrate the 8 stores to `createApiStore`: `app/subjectChangeResetsCache`,
  `audit/AuditPageFocusRefetch`, `audit/AuditPageRetryRecovery`, `layouts/LayoutEditorDialogChainRetention`,
  `overlays/OverlayDraftFormResolvePreview`, `overlays/OverlayEditPageNavigation`,
  `systemVariables/SystemVariablesPageFocusRefetch`, `systemVariables/SystemVariablesPageRetryRecovery`.
  The four without a listener today (`subjectChangeResetsCache`, `LayoutEditorDialogChainRetention`,
  `OverlayDraftFormResolvePreview`, `OverlayEditPageNavigation`) gain one: **run each and read the result**; revert
  and comment any file whose outcome changes (spec US2, scenario 2). Depends on T010.
- [x] **T012** (frontend) **T-B after** (empty diff) + **T-D** for management-web. Depends on T011.
- [x] **T013** (orchestrator) PR, parked. Depends on T012.

## Phase 3 — PR 3: management-event-ingestion + kiosk-web

- [x] **T014 [US1]** (frontend) **T-C before**, then migrate `HeldEventTypesPageRetryRecovery.test.tsx`
  (`apps/management-event-ingestion/src/features/eventTypes/`) to the shared `jsonResponse`.
  Depends on T013 merged.
- [x] **T015 [US2]** (frontend) Migrate `apps/kiosk-web/src/features/cell/CameraTile.test.tsx`'s store to
  `createApiStore` (it has no listener today: run and read). Its `jsonResponse(body)` and `errorResponse`
  **stay local** with a comment: no status parameter / bodyless (plan §R1). Depends on T014.
- [x] **T016** (frontend) **T-C after** (empty diff) + **T-D** for both apps. Depends on T015.
- [ ] **T017** (orchestrator) PR, parked. Depends on T016.

## Phase 4 — closing

- [ ] **T018** (orchestrator) After PR 3 merges: confirm SC-001/SC-002 by search (only the divergent helpers
  and the 2 stand-in `shared` stores and R4's four files remain), close #3196 with a closing keyword (a
  mention alone rarely closes it), and comment on #3197 that its blocker is gone. Add a note on #3190 that
  `Overlays|Layouts` `FocusRefetch`/`RetryRecovery` still carry local helpers (R4).

## Dependencies and parallelism

Strictly sequential across PRs (each rebases on the previous merge). Within a PR, per-file migrations
are independent but share one branch index, so they are done in order (one branch, one index). T001→T002→T003
cannot be reordered: T001 must be seen red before T002 exists.

## Explicitly not tasks

No fetch-stub helper (plan §R3). No change to `createApiStore.ts`. No production file. No ADR. No
`OverlaysPage*`/`LayoutsPage*` test migration. No `shard-N.filter` entry (Vitest).
