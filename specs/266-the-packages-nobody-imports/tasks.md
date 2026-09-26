# Tasks 266: The packages nobody imports

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2335 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing, ADR-0139). Declared green pins are listed in spec §6;
their green result is not 4a evidence.
**Engineers**: `test-writer` (4a, including the C# guard, which is test code only); then
`frontend-engineer` (4b). No backend or infra engineer. **Reviewers**: `frontend-reviewer`
(+ `backend-reviewer` for the one `.cs` file).
**Tracking**: feature-level issue #2335 (on Project #13, Todo). No per-task issues.
**Blocked on**: spec §0 Q2–Q5 answered. Q1 (command palette) blocks nothing here.

Format: `[ID] [P?] [Story] description`.

**Foundational / blocking**: T001–T003 (shared workspace test enablement and the four
`exports` entries). They own `apps/shared/package.json`, both `setup.ts` files and
`pnpm-lock.yaml`, which every story would otherwise contend on. No Shared.Kernel,
Shared.Contracts or AppHost work.

**Parallelism** (ADR-0109, disjoint files — plan §1): after T003, the four stories are
disjoint end to end and can fan out as four agents: **US1 ∥ US2 ∥ US3 ∥ US4**. Within 4a,
T010 ∥ T020 ∥ T030 ∥ T040 ∥ T005. Within 4b, T013 ∥ T023 ∥ T033 ∥ T043; each story's
consumer task follows its own primitive task. Commits still land in the plan §8 order
(one index per branch — fan-out is of work, not of commits).

**Do not touch**: `Button.tsx`, `Input.tsx` (#2336); `OverlayEditor.tsx`,
`BackdropControls.tsx`, `OverlayGeometryFields.tsx`, `overlayLabelStyle.ts` (#2342);
`tokens.css`, `tailwindTheme.ts` (no new token — spec SC-3); any other `<select>` or row of
buttons (spec §3.1); anything in `apps/kiosk-web`.

## Phase 0: foundation (frontend-engineer or test-writer; green, no behaviour)

- [ ] **T001** `apps/shared/package.json`: add `"@testing-library/user-event": "14.6.6"` to
  `devDependencies` (the version both apps pin); add `exports` entries
  `./ui/primitives/Select`, `./ui/primitives/DropdownMenu`, `./ui/primitives/Popover`,
  `./ui/primitives/Tabs` (pointing at the `.tsx` files T010/T020/T030/T040 create) and
  `./test/radixJsdom`. `pnpm install` → commit `pnpm-lock.yaml`.
- [ ] **T002** `apps/shared/src/test/radixJsdom.ts` (new): install-if-absent no-ops for
  `hasPointerCapture` / `setPointerCapture` / `releasePointerCapture` / `scrollIntoView`
  on `Element.prototype` and a `ResizeObserver` class (plan §7), with the header comment
  plan §7 describes. Import it from `apps/shared/src/test/setup.ts` and, via
  `@smart-sentinel-eye/shared/test/radixJsdom`, from `apps/management-web/src/test/setup.ts`.
- [ ] **T003** Run `pnpm --filter @smart-sentinel-eye/shared test` and
  `pnpm --filter @smart-sentinel-eye/management-web test`: both green and unchanged in count
  (the shim is behaviour-preserving for every existing test). Commit as plan §8 item 2.

## Phase 4a: red (test-writer)

Each story's 4a task commits its tests **with a signature-only stub** (plan §6): the exact
plan §3 interface, body `return null;`, no Radix import — so `pnpm typecheck` passes and the
red lands on content.

- [ ] **T005 [P] [US5]** `tests/Architecture.Tests/SharedUiDependencyUsageTests.cs`: plan §5.1.
  Mirror `SharedUiTokenUsageTests` (`RepositoryRoot()`, comment stripping, **slash-normalised**
  relative paths, a failure message that says what to do).
- [ ] **T010 [P] [US1]** `apps/shared/src/ui/primitives/Select.test.tsx` + stub `Select.tsx`:
  plan §5.2 row "Select".
- [ ] **T011 [US1]** `apps/management-web/src/features/rules/RuleDialog.test.tsx`: the new red
  case and the `chooseAction` helper replacing lines 98, 246, 247, 264 (plan §5.3). The
  helper drives the Radix listbox, so **those four existing cases also go red** against the
  native select — record that, it is expected. Their assertions stay byte-identical.
- [ ] **T020 [P] [US2]** `apps/shared/src/ui/primitives/DropdownMenu.test.tsx` + stub:
  plan §5.2 row "DropdownMenu", including the ConfirmDialog focus/pointer-events case.
- [ ] **T021 [US2]** `apps/management-web/src/features/layouts/LayoutsPage.test.tsx`: new red
  cases and the row-menu helper for lines 338, 764, 780, 784, 792 (plan §5.3).
- [ ] **T030 [P] [US3]** `apps/shared/src/ui/primitives/Popover.test.tsx` + stub: plan §5.2.
- [ ] **T031 [US3]** `apps/management-web/src/features/cameras/StreamHealthBadge.test.tsx`: new
  red cases; delete *"Surfaces the error string in the tooltip content"* (plan §5.3 — it cannot
  fail, and it sleeps 250 ms).
- [ ] **T040 [P] [US4]** `apps/shared/src/ui/primitives/Tabs.test.tsx` + stub: plan §5.2.
  *(If Q3 = defer: skip; see T044.)*
- [ ] **T050** Run and capture **verbatim**:
  `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~SharedUiDependencyUsage"`,
  `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/ui/primitives`,
  `pnpm --filter @smart-sentinel-eye/management-web exec vitest run src/features/rules/RuleDialog.test.tsx src/features/layouts/LayoutsPage.test.tsx src/features/cameras/StreamHealthBadge.test.tsx`,
  and `pnpm typecheck`.
  **Required**: guard red naming exactly `@radix-ui/react-dropdown-menu`,
  `@radix-ui/react-popover`, `@radix-ui/react-select`, `@radix-ui/react-tabs`; every
  primitive case red **on content**; every new consumer case red on content; declared pins
  green (spec §6); `typecheck` green. Any expected-red case arriving green: **stop and
  report**, do not adjust it until it is red.
- [ ] **T051** Counterfactual for T005 (plan §5.1): scratch-add `@radix-ui/react-switch`,
  run, quote the failure, revert. Both T050 and T051 output go in the PR body.

## Phase 4b: green (frontend-engineer — receives T050's output as its brief; may not edit tests)

### US5 — nothing to implement; closes when US1–US4 land.

### US1 (P1) — Select

- [ ] **T013 [P] [US1]** `apps/shared/src/ui/primitives/Select.tsx`: plan §3.1 and §2. Only
  semantic classes; no `hover:`, `opacity-`, `transition-`, `animate-`, `backdrop-blur`.
- [ ] **T014 [US1]** `RuleDialog.tsx`: plan §4.1 (Controller, `ACTION_OPTIONS`, add `control`).
  Depends on T013.
- [ ] **T015 [US1]** `e2e/rules.spec.ts:157-158`: plan §5.4. Run `rules.spec.ts` against the
  stack. Depends on T014.

### US2 (P2) — DropdownMenu

- [ ] **T023 [P] [US2]** `apps/shared/src/ui/primitives/DropdownMenu.tsx`: plan §3.2
  (`modal={false}`, deferred `onSelect`) and §2.
- [ ] **T024 [US2]** `LayoutsPage.tsx`: plan §4.2 (Q4 default). Depends on T023.
- [ ] **T025 [US2]** `e2e/kiosk-reconciliation.spec.ts:90-93` and
  `e2e/support/archive-e2e-layouts.teardown.ts:166-178`: plan §5.4. Run `layouts.spec.ts`,
  `kiosk-reconciliation.spec.ts` and a teardown pass against the stack. Depends on T024.

### US3 (P3) — Popover

- [ ] **T033 [P] [US3]** `apps/shared/src/ui/primitives/Popover.tsx`: plan §3.3 and §2.
- [ ] **T034 [US3]** `StreamHealthBadge.tsx`: plan §4.3. Depends on T033.

### US4 (P4) — Tabs

- [ ] **T043 [P] [US4]** `apps/shared/src/ui/primitives/Tabs.tsx`: plan §3.4 and §2.
- [ ] **T044 [US4] (alternative, only if Q3 = defer)** Remove `@radix-ui/react-tabs` from
  `apps/shared/package.json` `dependencies` and its `exports` entry from T001; `pnpm install`.

## Phase 4 close / Phase 5 / Phase 6

- [ ] **T060** Full gates: `pnpm lint`, `pnpm typecheck`, `pnpm format:check`, `pnpm test`,
  `dotnet test tests/Architecture.Tests` (incl. `SharedUiTokenUsageTests` and
  `DesignTokenLayerTests` **unchanged and green, no new carve-out** — spec SC-3). Guard T005
  now green.
- [ ] **T061** Phase 5 (`/verify`): spec §7 per story, all three themes, screenshots of every
  floating surface, keyboard-only walk with the accessibility tree. Verification note on the PR.
  Latency: **N/A** (spec §5) — say so in the note.
- [ ] **T062** Phase 6: `/code-review`; `frontend-reviewer` (+ `backend-reviewer` for T005).
  Security review not required (no trust boundary touched).
- [ ] **T063** Before `gh pr create --base develop`: re-check the spec number across every
  branch and worktree (spec header). File the follow-up issues spec §3.2 names — Badge (build
  next), and the Toast / combobox / command-palette decision issue(s) as the user directs.
