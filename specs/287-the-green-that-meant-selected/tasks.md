# Tasks: Spec 287, the green that meant "selected"

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2623 · **Lane:** autonomous (ADR-0144)
**Engineers:** `test-writer` (4a), then `frontend-engineer` (4b).

**Phase-4a colour: SPLIT, declared per task** (the #2488 precedent; ambiguity resolves to red).
- **Characterisation, observed green** — handlers, labels, roles, `aria-current`, filter and preset behaviour, the Healthy badge (T001, T002). Must pass **unmodified** after 4b; an edited assertion is a block.
- **Red** — the accent colour and Button's state matrix at the nine sites (T003, T004, T005). Must be observed failing on develop; failures quoted verbatim in the PR.
- **Pin** — accent-on-accent-subtle contrast (T006). Expected green; red means **block** (a design decision).

**Board.** The feature issue is #2623; confirm it is on Project #13 (`gh project item-list 13 --owner smartsolutionslab --limit 2000`, match on `content.url`). No per-task issues.

**Contention files (ADR-0109):** `e2e/interaction-states.spec.ts` (T004 only), `apps/management-web/src/app/ShellLayout.tsx` (T008 then T009, sequential).

**Foundational / blocking:** none. No Shared.Kernel, Contracts, AppHost or Aspire resource changes. Phase 4a (T001–T006) blocks 4b.

**Per-commit rule (ADR-0087).** `pnpm --filter @smart-sentinel-eye/management-web typecheck` and `lint` pass on every commit. Conventional Commits, no `Co-Authored-By` trailer (ADR-0086).

**Hard boundary:** no path under `apps/kiosk-web/` or `apps/shared/src/ui/` is written (spec FR-003, SC-003).

---

## Phase 4a — tests first (`test-writer`)

- [ ] **T001** [Characterisation] Run and record, on unmodified develop, green: `App.test.tsx`, `ShellLayout.test.tsx`, `LayoutsPage.test.tsx`, `OverlaysPage.test.tsx`, `SystemVariablesPage.test.tsx`, `GridDesignerKeyboard.test.tsx`, `GridDesignerRetainedOption.test.tsx`, `GridDesignerSpans.test.tsx`, `StreamHealthBadge.test.tsx`. Output quoted in the PR. (plan §5a)
- [ ] **T002** [Characterisation] [US1] New `apps/management-web/src/app/ShellLayoutCrashPanel.test.tsx`: a child route that throws once → panel shows → *Try again* → child re-renders. Observed **green** on develop. Commit 1. (plan §5a)
- [ ] **T003** [P] [Red] [US1] New `apps/management-web/src/App.affordance.test.tsx` (own controllable `react-oidc-context` mock) + one red case appended to `ShellLayoutCrashPanel.test.tsx`: the four US1 buttons carry Button's classes per plan §5b and no `/accent-active/` class. Observed red. (plan §5b)
- [ ] **T004** [P] [Red] [US1][US2] `e2e/interaction-states.spec.ts`: new `describe` with the four scenarios of plan §5b (signed-out *Sign in* in a fresh context; active nav link; selected *Archived* chip on `/layouts`; checked grid preset). Reuse `probeToken`; no existing test edited. Observed red (quote the CI or local Playwright output). (plan §5b, R2, R3)
- [ ] **T005** [Red] Confirm `pnpm typecheck` / `typecheck:e2e` green with T003–T004 red (tests red on behaviour, not on compile).
- [ ] **T006** [P] [Pin] `tests/Architecture.Tests/InteractionStateTests.cs`: fact 7, `--color-accent` on `--color-accent-subtle` ≥ 4.5:1 in dark, light, high-contrast, via fact 6's resolver. Existing class — no shard-filter entry. Expected green; **red → block**. (plan §4)
- [ ] **T007** Commit 2 (`test(console): …`) containing T003, T004, T006.

## Phase 4b — implementation (`frontend-engineer`; may not edit T001–T006's tests)

### US1 — sign-in and crash actions are real Buttons

- [ ] **T008** [US1] `apps/management-web/src/App.tsx`: three raw `<button>` → `<Button>` (`primary`, `secondary`, `primary`), import from `@smart-sentinel-eye/shared/ui/primitives/Button`, no `className`; `onClick` bodies verbatim (the `returnTo` state included). `ShellLayout.tsx` `CrashPanel` *Try again* → `<Button>` `primary`, no `className`. Commit 3. Depends on T007.

### US2 — selection in the accent

- [ ] **T009** [US2] `apps/management-web/src/app/ShellLayout.tsx` `NavItem` active: `bg-accent-active/10 … text-accent-active` → `bg-accent-subtle … text-accent`. Depends on T008 (same file).
- [ ] **T010** [P] [US2] `LayoutsPage.tsx`, `OverlaysPage.tsx`, `SystemVariablesPage.tsx` selected chip → `border-accent bg-accent-subtle text-accent`. Unselected string and the *Reload* links untouched.
- [ ] **T011** [P] [US2] `GridDesigner.tsx` active preset `<label>` → `border-accent bg-accent-subtle text-accent`; focus-recipe suffix unchanged.
- [ ] **T012** Commit 4 (`fix(management-web): draw selection in the accent, not the status triad`) with T009–T011.

## Phase 4 exit

- [ ] **T013** Re-run T001's list **unmodified** → green; T003/T004 → green; T006 → green; `InteractionStateTests` all facts green; management-web `lint`, `typecheck`, `test`; `typecheck:e2e`.
- [ ] **T014** SC-002 grep and SC-003 `git diff --name-only origin/develop` recorded in the PR.

## Phase 5 — verify

- [ ] **T015** Spec §3 end-to-end procedure on the running stack; screenshots per plan §8; the three themes. Latency: N/A (console only) — state it.

## Phase 7 / follow-ups

- [ ] **T016** Orchestrator files D1 and D2 (plan §10) **without** `agent:ready`, adds them to Project #13, and references them in the PR body. Decide `Refs #2623` vs `Closes #2623` per plan R1.

## Dependencies

T001 → T002 → {T003, T004, T006} [P] → T005 → T007 → T008 → T009; T010 [P] and T011 [P] after T007, alongside T008/T009 → T012 → T013 → T014 → T015 → T016.
