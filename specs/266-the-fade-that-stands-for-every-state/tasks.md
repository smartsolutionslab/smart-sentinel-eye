# Tasks 266: The fade that stands for every state

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2336 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: every console Button's rendering). Pins declared green
in advance are listed in spec §6; their green result is not 4a evidence. **T001 alone is
characterisation** (a test-helper move): observed green before and after, assertions unmodified.
**Engineers**: `test-writer` (4a); then `frontend-engineer` (4b). The C# file is test code, so
no backend engineer. **Reviewers**: `frontend-reviewer` (+ `backend-reviewer` for the `.cs` files).
**Tracking**: feature-level issue #2336 (on Project #13, Todo). No per-task issues.

Format: `[ID] [P?] [Story] description`.

**Foundational / blocking**: T001 (helper lift) blocks T002. T010 (tokens) blocks T011–T013.
No Shared.Kernel, Shared.Contracts or AppHost work.
**Parallelism** (ADR-0109, disjoint files): in 4a, T002 ∥ T003 ∥ T004 ∥ T005 ∥ T006 (T002
after T001). In 4b, after T010: T011 ∥ T012; T013 after T011 (same file, `Button.tsx`).
T014 (the nine adoption sites) is disjoint from T012 but depends on T013.
**D1 resolved (2026-09-26): option (a).** T013–T014 are no longer held. (Previously: US1 + US3 could ship without them;
the "(b) hold" path is no longer needed.)

**Do not** touch: any raw `<button>` (plan §7 follow-up); any selection fill using
`accent-active` (plan §7); `OverlayEditor.tsx`, `BackdropControls.tsx`,
`OverlayGeometryFields.tsx`, `overlayLabelStyle.ts` (#2342); any `disabled`/`unavailable`
prop at a call site (ADR-0151 follow-up); any `prefers-reduced-motion` rule, transform or
keyframe (#2334); anything under `apps/kiosk-web/src` except `tokens.build.test.ts`.

## Phase 4a: characterise, then red (test-writer)

- [ ] **T001 [US1][US3]** Lift, unchanged, `SharedUiTokenUsageTests.StringLiteralContent`
  into `tests/Architecture.Tests/TypeScriptSource.cs` (`internal static class`) and
  `DesignTokenLayerTests`' OKLCH/hex → 8-bit sRGB resolver into its own internal class; both
  existing classes call the lifted members. **Characterisation**: run
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~SharedUiTokenUsage|FullyQualifiedName~DesignTokenLayer"`
  before and after and quote both — identical pass counts, no assertion edited. Commit
  `refactor(tests): lift the string-literal reader and the OKLCH resolver`.
- [ ] **T002 [P] [US1][US3]** `tests/Architecture.Tests/InteractionStateTests.cs`: facts 1–6
  of plan §5.1, scanning `apps/*/src/**/*.{ts,tsx}` minus tests via `TypeScriptSource`. Fact 6
  extends the lifted resolver with `color-mix(in oklch, var(a) N%, var(b))` for opaque
  operands and reads each theme block. Failure messages name the file and say what to use
  instead. Constants for the thresholds (4.5, 3) cite WCAG 1.4.3 / 1.4.11 and spec §4.
- [ ] **T003 [P] [US1][US2]** `apps/shared/src/ui/primitives/Button.test.tsx`: rewrite cases 1
  and 4 per plan §5.2 (keep the ADR-0151 halves verbatim; update the file's doc comment to
  say which spec rewrote them and why); add the `busy` cases. Add `busy?: boolean` to
  `ButtonProps` **as a declaration only** so `pnpm typecheck` stays green (plan §5.2).
- [ ] **T004 [P] [US2]** `apps/shared/src/ui/primitives/ConfirmDialog.test.tsx`: `pending` →
  confirm button `aria-busy="true"`.
- [ ] **T005 [P] [US1]** `LayoutEditorDialogSaveGate.test.tsx:227` and
  `OverlayEditorDialogSaveGate.test.tsx:247`: `aria-disabled:opacity-50` →
  `aria-disabled:text-fg-disabled`; the comment above each names spec 266.
- [ ] **T006 [P] [US1]** Both `apps/{management-web,kiosk-web}/src/styles/tokens.build.test.ts`:
  plan §5.3's candidates and assertions; the three pins marked as such in a comment.
- [ ] **T007 [P] [US1][US3]** `e2e/interaction-states.spec.ts`: plan §5.4 items 1–5, sign-in
  and camera seeding mirrored from `camera-detail.spec.ts`, colours compared against token
  probes, waits by condition (ADR-0150). Item 4 (touch) is a pin — say so in its comment.
- [ ] **T008** Run and capture **verbatim**: the architecture filter
  `FullyQualifiedName~InteractionState`; `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/ui/primitives`;
  `pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/styles/tokens.build.test.ts src/features/layouts/LayoutEditorDialogSaveGate.test.tsx src/features/overlays/OverlayEditorDialogSaveGate.test.tsx`;
  kiosk-web's build test; the e2e against a booted stack; `pnpm typecheck`.
  **Required**: InteractionState facts 1–6 red, naming exactly the files plan §5.1 lists.
  Button cases 1, 4 and every `busy` case red; case 2 green (pin). ConfirmDialog busy red.
  SaveGate lines red. Build test: the five role candidates red, three pins green. E2e: items
  1, 2, 3, 5 red; item 4 green (pin). Typecheck green.
  If anything expected red arrives green, stop and report — do not adjust it until it is red.
  Commit T002–T007 as `test(interaction-states): pin every state red-first`.

## Phase 4b: implement (frontend-engineer)

- [ ] **T010 [US1]** `tokens.css` `:root`: the five roles of plan §2.1–2.2, beside the
  existing derived accent roles, with a comment giving the 92 % reason and the tint reason.
  No theme block changes. `tailwindTheme.ts`: `bg.hover`, `bg.pressed`,
  `fg['on-fault']`, `accent['fault-hover']`, `accent['fault-pressed']`. Commit
  `feat(tokens): derive hover, pressed and on-fault roles`. T006 and fact 6 go green.
- [ ] **T011 [US1]** `Button.tsx`: plan §3's `base` / `rest` / `interactive` /
  `unavailableTreatment` split; remove every `opacity` and `ring` utility and
  `focus-visible:outline-none`; keep the `danger` comment. Update the `unavailable` doc
  comment's "only ever emits the dimming class" to name the new treatment. Commit
  `feat(shared): design Button's state matrix`.
- [ ] **T012 [P] [US3]** `Input.tsx`, `DataTable.tsx`, `GridDesigner.tsx` focus outline and
  `Input` disabled; `ChainRecoveryNotice.tsx:302` disabled label — exactly plan §4's table.
  Commit `feat(shared): one focus outline on Input, DataTable and GridDesigner`.
- [ ] **T013 [US2]** `Button.tsx`: implement `busy` per plan §3 — destructured,
  `aria-busy={busy || undefined}`, `cursor-progress`, `interactive[variant]` omitted, no
  change to `disabled`/`aria-disabled`.
- [ ] **T014 [US2]** `busy={…}` at the nine sites of plan §6, additions only;
  `ConfirmDialog.tsx`'s confirm button. Commit T013–T014 as
  `feat(shared): add a busy state to Button and adopt it`.
- [ ] **T015** Re-run T008's commands; every red test green, nothing else changed. Then the
  counterfactuals of plan §5.1 (fact 1, fact 6), quoted and reverted. `pnpm lint`,
  `pnpm format:check`, `dotnet build -c Release` clean.

## Phase 5 (verify) and after

- [ ] **T020** Spec §7 end to end on a booted stack; plan §8 screenshots before/after; the
  light/high-contrast spot check of plan §10; the spec-225 render-leg figure from the PR's
  CI run cited in the verification note.
- [ ] **T021** Re-check the spec number against every remote branch and worktree
  immediately before `gh pr create --base develop` (spec header).
- [ ] **T022** Report the two follow-ups of plan §7 (raw buttons + triad-as-selection;
  ADR-0151 focus loss on in-flight disable) to the orchestrator for filing. **Do not file
  or label them from the lane.**

## Dependencies

```
T001 ──► T002 ─┐
T003 ─────────┤
T004 ─────────┼─► T008 ──► T010 ──► T011 ──► T013* ──► T014* ──► T015 ──► T020 ──► T021
T005 ─────────┤                 └─► T012 ─────────────────────────┘
T006 ─────────┤
T007 ─────────┘                                
```
