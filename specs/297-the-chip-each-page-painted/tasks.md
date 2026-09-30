# Tasks 297: The chip each page painted

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2635

## Phase-3 declarations (for `/next-issue`)

- **Engineer:** `frontend-engineer` for every phase-4b task — `apps/shared`, `apps/management-web`
  **and `apps/kiosk-web` (the wall)**, plus the one e2e edit. Phase 4a (`test-writer`) owns the
  two new C# architecture-test files as well as the Vitest/Playwright tests. No backend, no infra.
- **New ADR:** **no** (spec §4). Two conditional blocks, each a stop rather than a judgement:
  (a) a reviewer rules the `composites/` placement an evasion of `InteractionStateTests` fact 5
  (spec §4.3); (b) V1 does not reproduce the hue drift (plan §8 V1). ADR-0148's derivation
  premise is raised for a human in #2695, not decided here.
- **Phase 4a colour: RED** for F, US1, US2, US3 (every site's rendered fill changes). Each story
  carries a **characterisation set** observed green on `ab030e5a` before any source edit and
  passing **unmodified** after (spec §6). The only existing test lines allowed to change are
  `StreamHealthBadge.test.tsx`'s two tone-class cases (`:20-40`) — quote that diff in the PR.
- **Latency:** US2 → **overlay composite + render** leg (spec §5). F/US1/US3 are console-only,
  but F changes tokens the wall reads, so V3 runs once, on the PR, covering all of it.
- **Collateral:** F changes `RetryBanner`'s fill with no code change (declared, spec §6).

Format: `[ID] [P?] [Story]`. `[P]` = owns files disjoint from every other `[P]` task in the same
phase (ADR-0109).

## Phase 3 (architect — done here)

- [x] **T000a** Filed #2692 — the two identical revision-summary formatters (spec §3.2). Not `agent:ready`.
- [x] **T000b** Filed #2693 — five console pages hand-roll `RetryBanner`'s box. Not `agent:ready`.
- [x] **T000c** Filed #2694 — the wall's action buttons use the triad as affordance (FR-010's allowlist cites it).
- [x] **T000d** Filed #2695 — light-theme triad contrast + ADR-0148's `color-mix(in oklch)` hue drift (FR-008's exclusion cites it).
- [x] All four added to Project #13 (`gh project item-add`; verified via `gh issue view --json projectItems`).

## Phase 4 prerequisites (orchestrator)

- [ ] **T001 [F]** V1, plan §8: on `develop`'s tip, in Chromium against the kiosk's own CSS, read
  `--color-accent-fault-subtle` through a 1×1 canvas. Expect slate `rgb(20,24,37)` ±2. **Red →
  stop** (premise wrong). Record the reading in `verification.md`.
- [ ] **T002** Re-check spec number 297 across remote branches/worktrees; re-check spec 296's
  branch still touches nothing under `apps/kiosk-web` or `apps/shared/src/ui` (plan R9).

## Phase 4a: characterise, then red (test-writer)

**Characterisation first — on the untouched tree, capture verbatim green output (blocks the red tasks of its story):**

- [ ] **T003 [P] [US2]** New `apps/kiosk-web/src/features/cell/TileAlignmentBadge.test.tsx`:
  renders `role="status"`, `data-testid="tile-out-of-alignment"`, `data-camera` equal to the prop,
  text "Not in sync with the wall". Green now.
- [ ] **T004 [P] [US3]** `RulesPage.test.tsx`: the State cell's text is exactly the rule's state,
  for each of Draft/Active/Archived. `CameraDetailPage.test.tsx`: "Retired" present iff the
  camera is retired. Green now.
- [ ] **T005 [US1][US2]** Run and capture green: `StreamHealthBadge.test.tsx` (the three popover
  cases), `CellPage.test.tsx` (`live-updates-degraded` cases `:522-532`, `:1220`;
  `unavailableBadgeIn` cases `:2270-2320`), `PickerPage.test.tsx:153-180`, and the full
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~DesignTokenLayer|FullyQualifiedName~InteractionState|FullyQualifiedName~SharedUiTokenUsage|FullyQualifiedName~MotionLanguage"`.

**Foundation red (after T005):**

- [ ] **T006 [F]** `apps/shared/src/ui/composites/Badge.tsx` **signature-only stub** (plan §6) +
  `exports` entry in `apps/shared/package.json`; `Badge.test.tsx` per plan §5.3. Run → red on
  classes/element shape; `tsc --noEmit` green. Quote.
- [ ] **T007 [P] [F]** New `tests/Architecture.Tests/StatusTintTests.cs`, five facts per plan §5.1.
  Run → `Each_triad_tint_is_a_literal_at_its_triad_hue` and `A_triad_label_is_legible_on_its_tint`
  red (roles absent; fault-subtle a mix); `No_theme_redeclares_a_triad_role` and
  `The_neutral_label_is_legible_on_its_fill` green (declared pins). (No shard filter: the
  `ci-shards/*.filter` files belong to `Integration.Tests` only.) Quote.

**Story red (after T006 — stories in parallel):**

- [ ] **T008 [P] [US1]** `StreamHealthBadge.test.tsx`: rewrite **only** the `it.each` tone-class
  case and the Unknown case to the Badge classes; add "no `/` or `border` class" and
  "unrecognised state is neutral". Run → red. Popover cases untouched and still green.
- [ ] **T009 [P] [US2]** `TileAlignmentBadge.test.tsx`: add the class case (warning, `md`, no `/`).
  `CellPage.test.tsx`: add the *Overlay unavailable* class case and a `LiveUpdatesBadge` class
  case. New `tests/Architecture.Tests/WallStatusChipTests.cs` per plan §5.2.
  Run → red; the guard names exactly `LiveUpdatesBadge.tsx`, `TileAlignmentBadge.tsx`,
  `LayoutGrid.tsx` (warning matches only). Quote.
- [ ] **T010 [P] [US2]** `e2e/kiosk-live-updates.spec.ts`: opacity, chip-height and tint-hue checks
  per plan §5.5, after the existing `toBeVisible()`; recovery assertion stays last. Run against
  the stack → red on opacity (alpha ≈ 38) and on hue (slate). Quote.
- [ ] **T011 [P] [US3]** `RulesPage.test.tsx` tone-per-state case; `CameraDetailPage.test.tsx`
  neutral-Badge case. Run → red. Quote.

**4a exit:** every red case red, every characterisation and declared pin green, verbatim output
handed to the engineer. Anything expected red arriving green → stop and report.

## Phase 4b: implement (frontend-engineer — may not edit any test from 4a)

**Foundation (serial, blocks US1–US3):**

- [ ] **T012 [F]** `tokens.css`: six primitive stops (plan §2.1) with the rewritten triad-stops
  comment; the three `-subtle` roles in `:root` and `[data-theme='light']` (plan §2.2);
  `--color-accent-fault-subtle`'s comment block rewritten. `tailwindTheme.ts`: two entries
  (plan §2.3). → T007 green.
- [ ] **T013 [F]** `Badge.tsx` implementation (plan §3; `Slot` for `asChild`, `clsx`). → T006 green.
  Commit F (plan §7 commit 2).

**Stories (after T013; disjoint files, run in parallel):**

- [ ] **T014 [P] [US1]** `StreamHealthBadge.tsx` per plan §4. → T008 green; T005's popover
  cases still green, unedited. If they fail (plan R2), **stop** — do not touch them.
- [ ] **T015 [P] [US2]** `LiveUpdatesBadge.tsx`, `TileAlignmentBadge.tsx`, `LayoutGrid.tsx:443-450`
  per plan §4 (`LayoutGrid.tsx:210` untouched). → T009 green, T010 green (run e2e against the
  stack), T003/T005 characterisation still green, `MotionLanguageTests` still green (the kiosk
  reach closure now includes `Badge.tsx`).
- [ ] **T016 [P] [US3]** `RulesPage.tsx` `StateBadge`, `CameraDetailPage.tsx` *Retired* per
  plan §4. → T011 green; T004 still green.

**Gate before PR:** `pnpm lint && pnpm typecheck && pnpm format:check && pnpm test`;
`dotnet test tests/Architecture.Tests` (all); SC-3's grep clean.

## Phase 5: verify (orchestrator / verifier)

- [ ] **T017 [US1][US2][US3]** V2 (plan §8): spec §7 steps 1–7 on a live stack, before/after for
  the three wall chips, screenshots and canvas-read fills + box heights into
  `specs/297-the-chip-each-page-painted/verification.md`. Step 3 (out of alignment): observed,
  or recorded *not observed live*.
- [ ] **T018 [US2]** V3 (plan §8): re-check whether `render-leg-gate` is wired; if not, scratch
  baseline from the latest nine `develop` runs, three complete PR records (download before each
  re-run), `render-leg-check.mjs` on each, ADR-0123 triage on `regressed`/`unmeasured`, and the
  step-5 statement of whether any tile was badged during the span test. Figures with run ids
  and full SHAs into `verification.md`. §IV's table is **not** edited.

## Phase 6: review

- [ ] **T019** `frontend-reviewer` on the diff; explicitly ask for a ruling on spec §4.3
  (`composites/` placement). No `/security-review` — no auth, input or trust boundary is touched.

## Dependencies

```
T001, T002 ─┐
T003, T004, T005 (characterise) ─┬─ T006 ─┬─ T008 ─────────────┐
                                 └─ T007  ├─ T009, T010 ────────┤
                                          └─ T011 ──────────────┤
T006, T007 ─ T012 ─ T013 ─┬─ T014 (needs T008) ─────────────────┤
                          ├─ T015 (needs T009, T010) ───────────┼─ T017, T018 ─ T019
                          └─ T016 (needs T011) ─────────────────┘
```

Foundational: **T012 → T013** (tokens, theme, Badge, `exports`) blocks every story task in 4b;
after it, T014/T015/T016 fan out. `apps/shared/package.json` is touched only by T006 so no story
contends on it.
