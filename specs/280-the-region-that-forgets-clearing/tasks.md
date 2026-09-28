# Tasks 280

- [ ] T001 Confirm the full `OverlayGeometryFields.test.tsx` suite is green against current `develop`
      (the documented "before").
- [ ] T002 Always-mount the error span in `OverlayGeometryFields.tsx`; add the per-field testid.
- [ ] T003 Rescope the four `getByRole('alert').textContent` assertions (refusal messages) to the new
      testid.
- [ ] T004 Rescope `Escape discards a refused draft…` and `A successful commit clears a standing
      error…` — content assertion becomes present+empty, not absent.
- [ ] T005 Rescope `queryAllByRole('alert')).toHaveLength(0)` and the blur-refusal
      `getByRole('alert')).toBeVisible()` assertions.
- [ ] T006 Add the new fact: all four error regions are mounted (present, empty) before any refusal.
- [ ] T007 Leave the off-edge-accepted `queryByRole('alert')).toBeNull()` untouched (genuine never-shown
      case).
- [ ] T008 Full file green; full `apps/shared` suite green; typecheck; lint; grep other apps for an
      unscoped consumer.
- [ ] T009 Self-review the diff against spec §3; open the PR.
