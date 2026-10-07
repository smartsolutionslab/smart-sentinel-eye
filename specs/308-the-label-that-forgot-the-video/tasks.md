# Tasks 308: The label that forgot the video

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2709

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2709 carries `agent:ready`. The design decision was a human's
  (issue comment 6028912437); this spec only applies it.
- **Engineers:** `test-writer` (4a: C# architecture facts + Vitest); `frontend-engineer` (4b:
  `tokens.css`, `tailwindTheme.ts`, `CameraViewer.tsx`). **No backend or infra.**
- **New ADR:** **no.** One dated inline note on ADR-0146's 2026-10-04 amendment records the human
  decision (spec §5). If ruled an ADR write under ADR-0144, block T003 alone.
- **Phase 4a colour: RED** (spec §6). One declared assertion edit: spec 299's `toEqual` in
  `tailwindTheme.test.ts` gains two entries; existing entries byte-identical; stays `toEqual`.
- **Latency:** N/A (plan §1).
- **Board:** #2709 is on Project #13 (In Progress, verified 2026-10-07). No per-task issues.
- **Shard filters:** none — no new Integration.Tests class.

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files from every other `[P]` task in its group (ADR-0109).

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4 prerequisites (orchestrator) — block everything below

- [ ] **T001** Re-check spec number 308 across remote branches and open PRs (307 is held by
  `fix/2563-throttle-kiosk-telemetry`).
- [ ] **T002** Build `management-web` and `kiosk-web` on `362ffbeb`; keep both compiled CSS bundles
  as the phase-5 baseline (spec §7 step 2).
- [ ] **T003** Add the ADR-0146 inline note (plan §5) — or block it alone per spec §5.

## US1 (P1) — A camera's status reads over its video in every theme

### Phase 4a — tests first (test-writer), all `[P]`: disjoint files

- [ ] **T010** [P] [US1] `tests/Architecture.Tests/StatusTintTests.cs`: add
  `Each_on_video_text_role_is_its_signal_in_every_theme` and `A_triad_label_is_legible_on_video`
  (plan §6.1). Update the class remarks' phase-4a paragraph to name them.
- [ ] **T011** [P] [US1] `apps/shared/src/ui/tokens/tailwindTheme.test.ts`: new on-video `it`;
  extend spec 299's `toEqual` expected object by the two keys (declared edit).
- [ ] **T012** [P] [US1] `apps/shared/src/ui/composites/CameraViewerOverlayTone.test.tsx` (new):
  four tone cases (plan §6.3), reusing `CameraViewer.test.tsx`'s harness shape.
- [ ] **T013** [US1] Run T010–T012 on `362ffbeb` (`dotnet test tests/Architecture.Tests`;
  `npm test` + `tsc --noEmit` in `apps/shared`). Return verbatim output: the new facts red for the
  stated reason, every characterisation fact (spec §6) green. A new test arriving green is a
  phase-4 failure. Depends on T010–T012.

### Phase 4b — implementation (frontend-engineer; may not edit tests). Depends on T013.

Sequential — three small edits, one engineer; parallelising them buys nothing.

- [ ] **T020** [US1] `tokens.css`: the two `-on-video` roles + one comment (plan §2). No theme block
  touched.
- [ ] **T021** [US1] `tailwindTheme.ts`: two `textColor.accent` keys + one comment sentence (plan §3).
- [ ] **T022** [US1] `CameraViewer.tsx`: `ViewerOverlay` tone → `-on-video` classes (plan §4).
- [ ] **T023** [US1] Re-run T013's commands: all green, tests unmodified. `prettier --check`,
  eslint, `typecheck` for `apps/shared`, `management-web`, `kiosk-web`; Release build of
  `tests/Architecture.Tests`.

## Phase 5 — verify (spec §7). Depends on T023.

- [ ] **T024** Compiled-CSS diff against T002's baseline — only the permitted additions.
- [ ] **T025** Live: `CameraDetailPage` + `OverlayDraftForm` preview, fault and reconnecting, in
  dark / light / high-contrast; computed colour + canvas read-back contrast; figures into
  `verification.md` as observed.

## Phase 6–7

- [ ] **T026** `frontend-reviewer`; include the plan §6.1 counterfactual (light value →
  `-text` must fail `A_triad_label_is_legible_on_video`).
- [ ] **T027** PR to `develop` (`--base develop`), body quotes T013's red output, closes #2709 with
  a closing keyword.

## Follow-up (orchestrator, not this PR)

- [ ] **T030** File an issue: `ViewerOverlay`'s neutral label and hint (`text-fg-muted`, light
  `--gray-600`) are 4.15 : 1 on video / 4.02 : 1 with the scrim — below 4.5. Needs a human call on
  a neutral on-video role (spec §1, §3.2). Not `agent:ready`. Add to Project #13.
