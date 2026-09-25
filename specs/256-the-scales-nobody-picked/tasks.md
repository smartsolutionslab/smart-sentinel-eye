# Tasks 256: The scales nobody picked

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2332 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing). Pins that are declared green in advance are listed in
spec §6; their green result is not 4a evidence.
**Engineers**: `test-writer` (4a); then `frontend-engineer` (4b). The C# guards are test code
only, so no backend engineer is needed. **Reviewer**: `frontend-reviewer` (+ `backend-reviewer`
for the two `.cs` files).
**Tracking**: feature-level issue #2332 (on Project #13, Todo). No per-task issues.

Format: `[ID] [P?] [Story] description`.

**Foundational / blocking**: T010 (`tokens.css`) blocks every 4b task. No Shared.Kernel,
Shared.Contracts or AppHost work.
**Parallelism** (ADR-0109, disjoint files): in 4a, T001 ∥ T002 ∥ T003 ∥ T004. In 4b, after
T010: T011 ∥ T014, then T012 ∥ T013. US3's tasks T020 ∥ T021 ∥ T022 are disjoint but only
three to five lines each, so fan them out only if the orchestrator is already fanning.

**Do not** touch: `Button.tsx`, `Input.tsx` (#2336); `OverlayEditor.tsx`,
`BackdropControls.tsx`, `OverlayGeometryFields.tsx`, `overlayLabelStyle.ts` (#2342); the
`ssE-overlay-highlight` block in `apps/kiosk-web/src/styles/index.css`; any font file or
`@font-face` (#2333); any `prefers-reduced-motion` rule (#2334).

## Phase 4a: red (test-writer; commit 2 of plan §8)

- [ ] **T001 [P] [US1][US2]** `tests/Architecture.Tests/DesignTokenLayerTests.cs`: facts 1–12 of
  plan §5.1. Follow `ContainerImagePinTests` for the shape: a private `RepositoryRoot()`, a
  comment-stripped read, and failure messages that say what to do. Locate the token file **by
  following each app's first `@import`**, not by name. Include the private OKLCH/hex → 8-bit sRGB
  resolver (plan §5.1 fact 7). Constants `#00c853`, `#ff5252`, `#ffab40`, `#0b0d10`, `#14171c`
  cite ADR-0146 in a comment.
- [ ] **T002 [P] [US3]** `tests/Architecture.Tests/SharedUiTokenUsageTests.cs`: plan §5.2.
  Strip comments, then apply the rules to string literals only. The carve-out list (4 files,
  each tagged `#2342`) sits in the test with its reason, plus the fact that each carve-out
  still exists and still violates.
- [ ] **T003 [P] [US2]** `apps/management-web/src/styles/tokens.build.test.ts`: plan §5.3 and
  F9 (`@vitest-environment node`; `postcss` + `@tailwindcss/postcss` from the app; virtual
  `src/styles/__probe__.css` importing `./index.css` + `@source inline(...)`; `base` = app
  root). The assertion list is exactly plan §5.3, with `.rounded-md` marked as a green pin
  in a comment.
- [ ] **T004 [P] [US2]** `apps/kiosk-web/src/styles/tokens.build.test.ts`: the same as T003,
  for kiosk-web.
- [ ] **T005** Run and capture **verbatim**:
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~DesignTokenLayer|FullyQualifiedName~SharedUiTokenUsage"`,
  `pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/styles/tokens.build.test.ts`,
  and the same for kiosk-web.
  **Required**:
  - DesignTokenLayer: facts 1, 7, 8 and 9 green (pins). Fact 10 green (vacuous). Every other
    fact red **on content**. Fact 12 alone may be red on a missing `tailwindTheme.ts`.
  - SharedUiTokenUsage: the three rule facts red, naming exactly `CameraViewer.tsx`,
    `DataTable.tsx`, `Dialog.tsx`, `ConfirmDialog.tsx` and `Tooltip.tsx`. The carve-out fact
    green.
  - Build tests: every assertion red except `.rounded-md`.

  If any fact expected red arrives green, stop and report. Do not adjust it until it is red.
- [ ] **T006** Counterfactual for fact 10 (plan §5.1). In a scratch copy, add
  `var(--gray-900)` to one `apps/*/src` CSS file and run the fact against it. Quote the
  failure, then revert. It goes in the PR body with T005's output. Commit T001–T004 as
  `test(tokens): pin the token layers, the shared theme and the shared UI's token use red-first`.

Depends: T001–T004 → T005 → T006.

## Phase 4b: implement (frontend-engineer; must not edit T001–T004)

### US1 + US2 (commit 3 of plan §8, one commit)

- [ ] **T010 [US1]** Create `apps/shared/src/ui/tokens/tokens.css` exactly per plan §2–§3. That
  is: primitives (§2.1); the semantic dark defaults and non-colour categories (§2.2–§2.8);
  the bridges `--default-transition-duration: var(--duration-fast)` and
  `--default-transition-timing-function: var(--ease-out)`; the `light` and `high-contrast`
  blocks (§2.2, §2.6); and `html { font-variant-numeric: var(--font-numeric); }`. The header
  comment covers ADR-0148, the layer rule, the `<html>` caveat, the absence of `--blur-*`,
  and the fact that the file stays unlayered. Copy `--font-sans`/`--font-mono` verbatim from
  `node_modules/tailwindcss/theme.css`. **Blocks T011–T022.**
- [ ] **T011 [P] [US2]** Create `apps/shared/src/ui/tokens/tailwindTheme.ts` per plan §4.1.
  Put no `DEFAULT` in `transitionDuration`/`transitionTimingFunction` (F5). Every value is
  `'var(--semantic)'`.
- [ ] **T012 [P] [US2]** `apps/management-web/tailwind.config.ts`: `theme: tailwindTheme` via
  `../shared/src/ui/tokens/tailwindTheme`, with no local `var(--`. In
  `src/styles/index.css`, change the first import to `…/tokens/tokens.css`.
- [ ] **T013 [P] [US2]** `apps/kiosk-web/tailwind.config.ts` and `src/styles/index.css`: the same
  as T012. Leave the pulse block byte-identical.
- [ ] **T014 [P] [US1]** Delete `apps/shared/src/ui/tokens/colors.css`.
- [ ] **T015** Run T005's three commands. Every fact and assertion should be green. Run the
  full `Architecture.Tests` project, `pnpm typecheck`, `pnpm lint` and `pnpm test`
  (workspace). Run `pnpm build` for both apps. Commit
  `feat(tokens): two-layer OKLCH token file consumed by both apps through one theme`.
  The DesignTokenLayer and build tests are green at this commit. SharedUiTokenUsage is still
  red, and that is expected until T023.

Depends: T010 → (T011 ∥ T014) → (T012 ∥ T013) → T015.

### US3 (commit 4 of plan §8)

- [ ] **T020 [P] [US3]** `apps/shared/src/ui/primitives/Dialog.tsx` and `ConfirmDialog.tsx`:
  - The overlay goes `bg-black/60` → `bg-scrim` and gains `z-overlay`. Keep
    `backdrop-blur-sm` (console-only; ADR-0148 allows it at the call site).
  - The content goes `bg-bg-elevated` → `bg-bg-raised`, `border-fg-muted` →
    `border-border-subtle`, `shadow-xl` → `shadow-overlay`, and gains `z-overlay`.
  - Leave `rounded-lg` as is.
- [ ] **T021 [P] [US3]** `apps/shared/src/ui/primitives/Tooltip.tsx`:
  - The content goes `bg-bg-elevated` → `bg-bg-raised`, `border-fg-muted/40` →
    `border-border-subtle`, `shadow-md` → `shadow-popover`, and gains `z-tooltip`.
  - The arrow goes `fill-bg-elevated` → `fill-bg-raised`.
- [ ] **T022 [P] [US3]** `apps/shared/src/ui/composites/DataTable.tsx`: `border-fg-muted/30`
  and `border-fg-muted/20` → `border-border-subtle`. Leave the sort button's
  `ring-accent-active` (#2336). `CameraViewer.tsx`: `bg-black` → `bg-bg-video`,
  `bg-black/60` → `bg-scrim`. The resolved colours are the same, which is the render-leg
  argument in spec §5.
- [ ] **T023** `SharedUiTokenUsageTests` green; the full Architecture.Tests suite and `pnpm test`
  green. Commit
  `refactor(ui): cite semantic tokens in the shared floating primitives, table and viewer`.

Depends: T015 → (T020 ∥ T021 ∥ T022) → T023.

## Phase 5: verify

- [ ] **T030** Spec §7 steps 3–5, in a real browser, for both apps. Read these computed values
  and record them in the verification note:
  - `bg-base` → `rgb(11, 13, 16)`
  - a triad badge → `rgb(0, 200, 83)`
  - Dialog: its background, border and shadow
  - `transition-duration` on a Button → `0.12s`
  - `font-variant-numeric` on a table cell → `tabular-nums`

  Toggle `data-theme` to `light` and to `high-contrast`, and screenshot each. Check the
  result against plan §7's expected-change list, item by item.
- [ ] **T031** Read the spec-225 render-leg check from the PR's CI run. Cite the figure against
  the 50 ms leg (SC-005).

## Phase 6: review

- [ ] **T040** `frontend-reviewer` on the diff. `backend-reviewer` on the two `.cs` files. Check
  three things specifically:
  - no primitive is cited outside `tokens.css`;
  - no wall prohibition is loosened (no blur token; no shadow reaches kiosk-web);
  - the deferred list in spec §3 is honoured.
