# Tasks 293: The editor the theme forgot

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2342

## Phase-3 declarations (for `/next-issue`)

- **Engineer:** `frontend-engineer` for every phase-4b task. Phase 4a (`test-writer`) owns
  the two C# architecture-test edits as well as the Vitest/Playwright ones.
- **New ADR:** **no.** Mechanical application of ADR-0148/0146/0077/0151. The one
  interpretation (content roles pinned across themes) rests on existing precedent
  (`--color-bg-video`, `--color-fg-on-fault`) — spec §5. If a reviewer rules it a new
  decision, the lane **blocks**; US2 is then deferred and US1 ships alone.
- **Phase 4a colour: RED for both stories** (ambiguity resolves to red). Each story also
  carries a characterisation set that must be observed green on `34d46a23` **before** any
  edit and pass **unmodified** after (plan §5). The only test lines allowed to change are the
  superseded ones enumerated in spec §6 — quote the `*.test.*` diff in the PR.
- **Rnd position/size:** no new characterisation test; already pinned by
  `OverlayEditorCharacterisation.test.tsx:103-120` plus the drag/resize/keyboard suites,
  all in the characterisation set.
- **Latency:** US1 N/A. US2 → Overlay composite + render leg; cite the CI figure in phase 5.

Format: `[ID] [P?] [Story]`. `[P]` = owns disjoint files from every other `[P]` task in the
same phase (ADR-0109).

## Phase 3 (architect — done here)

- [x] **T000** Filed #2685: *A `Slider` primitive, when a second range control appears*
  (spec §4.1). Not `agent:ready`.
- [x] **T000b** Filed #2686: *The overlay editor's camera picker cannot tell two
  same-named cameras apart* (spec §4.2). Not `agent:ready`.

## Phase 4a: characterise, then red (test-writer)

**Foundational, blocks everything:**

- [ ] **T001 [US1][US2]** Characterisation baseline on the untouched tree: run
  `pnpm --filter @smart-sentinel-eye/shared test`, `pnpm --filter management-web test`
  (overlay suites) and `e2e/overlays.spec.ts`; capture verbatim green output. This is the
  "observed green before" evidence for plan §5's characterisation list.

**US1 red (after T001):**

- [ ] **T002 [P] [US1]** `tests/Architecture.Tests/SharedUiTokenUsageTests.cs`: remove the
  `OverlayEditor.tsx`, `BackdropControls.tsx`, `OverlayGeometryFields.tsx` carve-out entries;
  generalise `Each_carve_out_…`'s `"#2342"` check to `#\d+`; rewrite the "exactly four
  files" summary. Run → `No_colour_literal` red naming the three files. Quote it.
- [ ] **T003 [P] [US1]** `tests/Architecture.Tests/DesignTokenLayerTests.cs`: new fact
  `Content_roles_are_pinned_across_themes` over `--color-bg-video`,
  `--color-bg-video-inverse` (plan §4.2), reusing the existing declaration parser. Run →
  red (`--color-bg-video-inverse` not in `:root`). Then run the counterfactual once the
  token exists (T010) and quote both.
- [ ] **T004 [P] [US1]** `OverlayEditorCharacterisation.test.tsx:63-71,92-101` and
  `OverlayEditorBackdrop.test.tsx:217,228,239-240,273`: rewrite **only** these assertions
  to expect `var(--color-border-subtle)`/`var(--color-bg-elevated)` (checkerboard, same
  angle/stops), `var(--color-bg-video-inverse)` (white), `var(--color-bg-video)` (black,
  letterbox). Run → red. If jsdom reads a `var()` value back as `''`, **stop** (plan §7 R1).
- [ ] **T005 [P] [US1]** `e2e/overlays.spec.ts`: new test — open the editor, read the
  canvas's computed `background-image` under `data-theme="dark"` and `"light"` (set via
  `page.evaluate` on `document.documentElement`); assert they differ. Red today (literals).
- [ ] **T006 [P] [US1]** `apps/management-web/src/styles/tokens.build.test.ts`: add
  `accent-accent` to the candidate list and a case asserting it compiles to
  `accent-color: var(--color-accent)`. Record honestly whether it arrives red or green
  (plan §5).

**US2 red (after T002/T003 — same two files, so sequential with them):**

- [ ] **T007 [US2]** `SharedUiTokenUsageTests.cs`: remove the `overlayLabelStyle.ts`
  carve-out (list now empty). `DesignTokenLayerTests.cs`: append `--color-bg-label`,
  `--color-fg-on-label` to the fact's list. Run → both red. Quote.
- [ ] **T008 [P] [US2]** `OverlayLabelCharacterisation.test.tsx:73,81,82,96` and
  `OverlayLabelParity.test.tsx:80`: expect `var(--color-bg-label)`,
  `var(--color-fg-on-label)`, `var(--font-weight-semibold)`, `0 var(--space-1)`. Parity
  (editor == wall) lines untouched. Run → red.
- [ ] **T009 [US2]** (after T005, same file) `e2e/overlays.spec.ts`: extend T005's test —
  the label's computed `background-color` and `color` are equal across dark / light /
  high-contrast. Green today; record as characterisation (plan §7 R2: compare themes to each
  other, never to a literal).

## Phase 4b: implement (frontend-engineer) — receives 4a's verbatim output; may not edit tests

**US1** (T010 blocks T011-T013; T011-T013 are `[P]`, disjoint files):

- [ ] **T010 [US1]** `tokens.css`: add `--color-bg-video-inverse: var(--white)` with the
  content-roles comment (plan §2), `:root` only. → T003 green; run the counterfactual.
- [ ] **T011 [P] [US1]** `OverlayEditor.tsx`: plan §3.1, every row. Keep exactly the inline
  styles in spec §3. → T004, T005 green; Keyboard/Undo/Characterisation suites green
  unmodified.
- [ ] **T012 [P] [US1]** `BackdropControls.tsx`: plan §3.2, every row; `{camera.name}`
  untouched. → FrameCapture/Backdrop suites green unmodified.
- [ ] **T013 [P] [US1]** `OverlayGeometryFields.tsx`: plan §3.3, every row; keep the
  `ReservedMessageSlot` same-class invariant. → OverlayGeometryFields suite and the two
  #2366 e2e tests green unmodified.
- [ ] **T014 [US1]** (after T011-T013) → T002 green. Full local gate: `pnpm -r typecheck`,
  `pnpm -r lint`, `pnpm -r test`, `dotnet test tests/Architecture.Tests` (Release). Commit
  US1 — must build and test green on its own.

**US2** (after T014):

- [ ] **T015 [US2]** `tokens.css`: add `--color-bg-label`, `--color-fg-on-label` (plan §2).
  `overlayLabelStyle.ts`: plan §3.4. → T007, T008 green; T009 still green; parity green
  unmodified. Same full gate as T014. Commit US2.

## Phase 5: verify

- [ ] **T020 [US1][US2]** Spec §7 steps 1-6. Real-browser screenshots of the editor in
  dark / light / high-contrast (canvas checkerboard, White/Black fields, a refused Width
  field, focused label ring, font-size slider). US2: the composite + render figure from the
  PR's CI run beside develop's latest green (`render-leg-summary`), quoted.

## Dependencies

```
T001 ─┬─> T002 ─> T007 ─┐
      ├─> T003 ─────────┤ (T007 edits T002's and T003's files)
      ├─> T004 [P]      │
      ├─> T005 [P] ─> T009
      ├─> T006 [P]      │
      └─> T008 [P] <────┘ (independent file; ordered after T001 only)

T010 ─> T011 [P] ┐
        T012 [P] ├─> T014 ─> T015 ─> T020
        T013 [P] ┘
```

Foundational: **T001** (baseline) and **T010** (the token) block the rest of their phase.
Fan-out: T004/T005/T006/T008 in 4a; T011/T012/T013 in 4b — three disjoint composite files.
