# Tasks 236 — The zero the editor allows

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2361 (feature-level issue; no per-task issues)
**Engineer**: `frontend-engineer` · **Phase 4a colour**: **RED** (behaviour-changing: plan §4 a, b, c, f),
with two green guards (d, e) proven by counterfactual.

Format: `[ID] [P?] [Story] description — file(s)`.
`[P]` = owns files no other open task touches (ADR-0109). No foundational task: nothing in
Shared.Kernel/Contracts, AppHost or Aspire resources changes.

## Phase 4a — tests first (test-writer; return verbatim output)

- [ ] **T001** [P] [US1] Plan §4 a–d: move `:156` to `toBe(0.005)` and rename its test / update its `:154-155` comment; add b (zero width → `0.005`, emitted label passes `overlayLabelSchema.safeParse`), c (`NaN` height → `0.005`), d (1 px width → exactly `1 / 800`) — `apps/shared/src/ui/composites/OverlayEditorCharacterisation.test.tsx`
- [ ] **T002** [P] [US1] Plan §4 e: width `0.003` label, `ArrowRight` → emitted width `0.003` — `apps/shared/src/ui/composites/OverlayEditorKeyboard.test.tsx`
- [ ] **T003** [P] [US1] Plan §4 f: via `ControlledOverlayEditor`, `onResize` offsetHeight `-100` → Height field `"0.5"` in flight — `apps/shared/src/ui/composites/OverlayGeometryFields.test.tsx`
- [ ] **T004** [US1] Run `pnpm --filter @smart-sentinel-eye/shared test -- OverlayEditor OverlayGeometryFields` on unchanged production code. Expected verbatim: **a, b, c, f red** on the emitted/displayed value (`0`, `"0"`), not on a compile/import error; **d, e green**; every pre-existing test green. Depends on T001–T003.

## Phase 4b — production (engineer; T001–T003 may not be edited)

- [ ] **T005** [US1] Add `clampSize` (plan §2) after `MIN_NORMALIZED_SIZE`; use it for width/height at `emitNormalized` and `geometryFromPixels` only; correct the three now-false comments (plan §3); leave `clamp01`, `nudgePosition`, `resizeSize`, `handleGeometryCommit` byte-for-byte. Then **counterfactual**: temporarily make `clampSize` `Math.min(Math.max(v, MIN_NORMALIZED_SIZE), 1)`, run T004's command, quote d and e red, revert — `apps/shared/src/ui/composites/OverlayEditor.tsx`. Depends on T004.
- [ ] **T006** [US1] `pnpm --filter @smart-sentinel-eye/shared test` (full), `pnpm -r lint`, `pnpm -r typecheck` green; T001–T003 green **unmodified**. Depends on T005.

## Phase 5

- [ ] **T007** Verification note: component-test-verified (spec §6); record A1 (real-pointer reachability of `≤ 0`) as not established. No latency figure — not on the §IV path. Depends on T006.

## Dependencies

```
T001 ┐
T002 ┼─→ T004 ─→ T005 ─→ T006 ─→ T007
T003 ┘
```

T001–T003 own disjoint test files and can run in parallel; T005 is the only production task.
