# Tasks 319: The gray that forgot the video

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2734

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2734 carries `agent:ready`. The design decision was a human's
  (issue comment, 2026-10-08); this spec names the token and picks its value (spec §5).
- **Engineers:** `test-writer` (4a: C# architecture facts + Vitest); `frontend-engineer` (4b:
  `tokens.css`, `tailwindTheme.ts`, `CameraViewer.tsx`). **No backend or infra.**
- **New ADR:** **no**, and no ADR note (spec §6) — ADR-0148 decision 1 latitude; ADR-0146 item 4 does
  not speak to a neutral role.
- **Phase 4a colour: RED** (spec §7). One declared assertion edit: spec 308's connecting case in
  `CameraViewerOverlayTone.test.tsx` moves from `text-fg-muted` to `text-fg-muted-on-video`; its two
  triad `not.toContain` lines stay byte-identical.
- **Latency:** N/A (spec §8).
- **Board:** #2734 is on Project #13 (In Progress, verified 2026-10-08). No per-task issues.
- **Shard filters:** none — no new Integration.Tests class.
- **Spec number:** 319; 317 is claimed by three unpushed worktrees (2077, 2170, 2325) — not this
  spec's to settle, but the orchestrator should know before any of them opens a PR.

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files from every other `[P]` task in its group (ADR-0109).

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4 prerequisites (orchestrator) — block everything below

- [ ] **T001** Re-check spec number 319 across remote branches, open PRs and sibling worktrees.
- [ ] **T002** Build `management-web` on `ba37ca5a`; keep the compiled CSS as the phase-5 baseline
  (spec §8 step 2).

## US1 (P1) — A camera's neutral status and hint read over its video in every theme

### Phase 4a — tests first (test-writer), all `[P]`: disjoint files

- [ ] **T010** [P] [US1] `tests/Architecture.Tests/StatusTintTests.cs`: add
  `The_neutral_on_video_role_is_the_muted_role_except_in_light` and
  `The_neutral_label_is_legible_on_video` (plan §5.1); class-remarks paragraph for spec 319.
- [ ] **T011** [P] [US1] `apps/shared/src/ui/tokens/tailwindTheme.test.ts`: new `textColor.fg` `it`
  with the cast (plan §5.2); header sentence. Existing `toEqual` untouched.
- [ ] **T012** [P] [US1] `apps/shared/src/ui/composites/CameraViewerOverlayTone.test.tsx`: declared
  edit of the connecting case; new hint-line `it`; header comment (plan §5.3).
- [ ] **T013** [US1] Run T010–T012 on `ba37ca5a` (`dotnet test tests/Architecture.Tests`; `npm test` +
  `tsc --noEmit` in `apps/shared`). Return verbatim output: the five new/edited tests red for the stated
  reason (not declared / class missing), every characterisation test (spec §7) green. A new test
  arriving green is a phase-4 failure. Depends on T010–T012.

### Phase 4b — implementation (frontend-engineer; may not edit tests). Depends on T013.

Sequential — three small edits, one engineer.

- [ ] **T020** [US1] `tokens.css`: `:root` role + comment; light-block line (plan §2). High-contrast
  block not touched.
- [ ] **T021** [US1] `tailwindTheme.ts`: `textColor.fg['muted-on-video']` + comment sentence (plan §3).
- [ ] **T022** [US1] `CameraViewer.tsx`: neutral tone arm and hint span → `text-fg-muted-on-video`
  (plan §4).
- [ ] **T023** [US1] Re-run T013's commands: all green, tests unmodified. `prettier --check`, eslint,
  `typecheck` for `apps/shared`, `management-web`, `kiosk-web`; Release build of
  `tests/Architecture.Tests`.

## Phase 5 — verify (spec §8). Depends on T023.

- [ ] **T024** Compiled-CSS diff against T002 — only the three permitted additions.
- [ ] **T025** Headless-Chromium contrast read per theme (new vs old class, scrim composited); live
  `CameraDetailPage` read if the stack is free. Figures into `verification.md` as observed.

## Phase 6–7

- [ ] **T026** `frontend-reviewer`, running plan §5.1's three counterfactuals and quoting each failure.
- [ ] **T027** PR to `develop` (`--base develop`); body quotes T013's red output; `Closes #2734`
  (closing keyword — memory: *a PR mention rarely auto-closes the issue*); check the issue state after
  merge.
