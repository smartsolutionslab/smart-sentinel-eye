# Plan 231 — The stale sentence and the sticky fab error

**Spec**: [spec.md](./spec.md) · **Issue**: #2433 · **Phase**: 2 (Plan)
**Engineer**: `frontend-engineer` · **Colour**: RED, both items · **One PR**

## 1. Constitution / ADR alignment

| Check | Result |
|---|---|
| §IV latency | N/A — no leg touched. |
| §II primitives | Does not bind — no domain model. |
| §Testing | New behaviour → red first (ADR-0139). |
| Boundaries | Frontend only; no bounded context, no `Shared.Contracts`, no NetArchTest surface. |
| Reuse | Uses existing `CONFLICT_FALLBACK` / `isStaleConflict` from `@smart-sentinel-eye/shared/api/problemDetail`; no new code in `apps/shared`. |
| ADR-0036 | Five small edits; no shared abstraction introduced (none exists to reuse — spec §1). |

## 2. Layers and files

No backend layers. Only `apps/management-web/src/features/`.

### Item 1 (US1) — two list pages

| File | Change |
|---|---|
| `layouts/LayoutsPage.tsx` | Import `CONFLICT_FALLBACK`, `isStaleConflict` alongside `isConflict`, `problemDetail` (`:11`). At `:129` replace the fallback with `isStaleConflict(mutationError) ? CONFLICT_FALLBACK : 'Could not apply that change.'`, formatted as `OverlaysPage.tsx:127-130`. |
| `systemVariables/SystemVariablesPage.tsx` | Same import change at `:8`; same fallback at `:115`. |

### Item 2 (US2) — three dialogs

| File | Change |
|---|---|
| `cameras/RegisterCameraDialog.tsx` | Select `onChange` (`:101`) → `setFabId(event.target.value); setFabError(null);` |
| `rules/RuleDialog.tsx` | Same at `:124`. |
| `systemVariables/SystemVariableDialog.tsx` | Same at `:118`. |

### Tests (written first by `test-writer`)

| File | Addition |
|---|---|
| `layouts/LayoutsPage.test.tsx` | **Harness**: the mutation mocks at `:35-38` return fixed `{ isLoading: false }` states — no way to inject an error. Make one state mutable (a module-level `let publishState` reset in `beforeEach`), mirroring `OverlaysPage.test.tsx` (`publishState`/`archiveState`/…). Existing tests' assertions stay unmodified. Then: stale-without-detail → `CONFLICT_FALLBACK` + Reload; non-stale 409 without detail (`LAYOUT_NAME_TAKEN`) → generic, no "someone else"; detail present → detail wins. |
| `systemVariables/SystemVariablesPage.test.tsx` | Uses mutable `setValueState` already. Add: `VARIABLE_STALE` without detail → `CONFLICT_FALLBACK` + Reload; 400 without detail → generic. |
| `cameras/RegisterCameraDialog.test.tsx` | After the "Refuses to submit … no fab chosen" setup: `selectOptions(fab, 'dresden')` → `queryByText(/choose which fab/i)` is null; `registerMock` not called. |
| `rules/RuleDialog.test.tsx` | Same pattern with `fillValidRule` + `createMock`. |
| `systemVariables/SystemVariableDialog.test.tsx` | Set `assignedGroups.current = ['/fabs/munich','/fabs/dresden']` (first multi-fab test in the file; reset to single-fab in `beforeEach` if not already); fill valid name, submit → message shown; select `dresden` → message gone; `defineMock` not called. |

Red expectation (quote verbatim in PR): US1 tests fail with the alert text "Could not apply that
change."; US2 tests fail with the message still found in the DOM.

Assertion rule (memory: *an assertion must not check its own input*): US1 tests assert the
rendered alert against the imported `CONFLICT_FALLBACK` constant **or** a regex on its wording
(`/someone else changed this/i`), and must also assert the generic sentence is **absent** — so the
test fails if the page renders either wrong string.

## 3. Entities, messaging, boundaries

None. No domain model, no domain or integration event, no cross-context reference, no API or
contract change. Auth/scope unchanged.

## 4. Commits (ADR-0030, rebase-merge per ADR-0087; each builds and passes on its own)

1. `test(management-web): pin the stale-conflict fallback on layouts and variables pages` — US1 tests (red).
2. `fix(management-web): use the stale-conflict fallback on layouts and variables pages` — US1 production.
3. `test(management-web): pin clearing the fab error when a fab is chosen` — US2 tests (red).
4. `fix(management-web): clear the fab error when the operator picks a fab` — US2 production.

Note: commits 1 and 3 are red by design; "each commit builds" means typecheck/lint pass, and the
red is the documented evidence. If the lane's CI rule requires every commit's tests green, fold
each test commit into its fix commit and quote the red output in the PR body instead — the
orchestrator decides at phase 7.

## 5. Verification

- `pnpm --filter management-web test`, `lint`, `typecheck` green.
- Phase 5: live check of US2 in the three dialogs with a two-fab user (spec §7). US1 is
  component-test-verified only; say so in the note.

## 6. Risks

- `LayoutsPage.test.tsx` harness change could perturb existing tests if the mutable state is not
  reset in `beforeEach` — reset it to `{ isLoading: false, error: undefined }`.
- None on the latency path; none on security (wording only, no new data rendered — the conflict
  sentence is a constant).
