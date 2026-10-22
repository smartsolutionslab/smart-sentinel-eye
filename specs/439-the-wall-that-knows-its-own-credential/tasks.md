# Tasks 439 — The wall that knows its own credential

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #2781 (feature-level, on Project #13)
**Colour:** red — every behaviour test is observed failing before its implementation (ADR-0139).
**Engineer:** frontend-engineer only. Single slice: US1 cannot be observed without detection, renewal and both states.

Format: `[ID] [P?] [Story] description — files`

## Phase 4a — tests first (test-writer), observed red

- [ ] T001 [P] [US1] Extend `CameraViewerRefusal.test.tsx`: `onRefused` kind per 401/403, absent for
      stream-unavailable/network; existing cases unmodified — `apps/shared/src/ui/composites/CameraViewerRefusal.test.tsx`
- [ ] T002 [P] [US1] `renewSession` single-flight test — `apps/shared/src/api/gateway.test.ts`
- [ ] T003 [P] [US1] Evaluator edge cases (plan §5) — `apps/kiosk-web/src/features/cell/wallCredential.test.ts`
- [ ] T004 [P] [US1] State machine cases (plan §5) — `apps/kiosk-web/src/features/cell/useWallCredential.test.ts`
- [ ] T005 [P] [US1] Notice wording + no motion/bright fill — `apps/kiosk-web/src/features/cell/WallCredentialNotice.test.tsx`
- [ ] T006 [US1] App precedence: `invalid_grant` after entry → `NotAuthorizedScreen` — `apps/kiosk-web/src/App.test.tsx`
- [ ] T007 [P] [US1] e2e four variants (recover, refused, unreachable, mixed 401/403), interception counts asserted,
      (Playwright `--shard` picks it up; the `ci-shards/*.filter` files are .NET-only) — `e2e/kiosk-credential-refused.spec.ts` (seeded 9-tile wall)

## Phase 4b — implementation (frontend-engineer; may not edit T001–T007)

- [ ] T010 [P] [US1] C1+C2 `onRefused` — `useWhepSession.ts`, `CameraViewer.tsx` (depends T001)
- [ ] T011 [P] [US1] C3 export `renewSession` — `apps/shared/src/api/gateway.ts` (depends T002)
- [ ] T012 [P] [US1] C4 evaluator — `wallCredential.ts` (depends T003)
- [ ] T013 [US1] C5 hook — `useWallCredential.ts` (depends T004, T011, T012)
- [ ] T014 [P] [US1] C6 notice — `WallCredentialNotice.tsx` (depends T005)
- [ ] T015 [US1] C7 wiring + tile key generation — `LayoutGrid.tsx`, `CameraTile.tsx` (depends T010, T013, T014)
- [ ] T016 [US1] Run counterfactuals (plan §5), quote outputs in the PR; `prettier --check`, eslint, `tsc --noEmit`,
      vitest, `MotionLanguageTests` (depends T015)

## Phase 5 — verify

- [ ] T020 [US1] Live: 2-tile wall, provoke all-401 (block the authorize hook / expire the token), screenshot both
      states and the recovery — `specs/439-*/verification.md`
- [ ] T021 [US1] Composite + render leg, three records per plan §6 — `specs/439-*/verification.md`

## Phase 6 — review

- [ ] T030 frontend-reviewer; T031 security-reviewer (renewal triggered from a stream signal; single-flight;
      no login form on the wall; no token in logs)

## Dependencies and parallelism

No foundational blocker. T001–T005 and T007 own disjoint files and run in parallel; T006 touches `App.test.tsx`
alone. In 4b, T010/T011/T012/T014 are disjoint `[P]`; T013 then T015 serialise on the kiosk wiring.
