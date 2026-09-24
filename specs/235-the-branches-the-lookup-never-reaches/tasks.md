# Tasks 235 — The branches the lookup never reaches

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2359 · **Phase**: 3 (Tasks)
**Colour**: **characterisation, observed green** — every task. No red task exists in this spec.
**Engineer**: `backend-engineer` (4b) after `test-writer` (4a) · **Reviewer**: `backend-reviewer`
**Tracking**: feature-level issue #2359 (already on Project #13). No per-task issues.

Format: `[ID] [P?] [Story] description`. No foundational (Shared.Kernel / Contracts / AppHost) work.

## Phase 4a — characterisation (test-writer)

- [ ] **T001 [US1]** Add C1 `A_sibling_archived_and_defined_again_resolves_to_the_live_one` to
  `tests/SystemVariables.Application.Tests/EventHandlers/VariableValueChangedDomainEventHandlerTests.cs`
  per plan.md.
- [ ] **T002 [US1]** Add C2 `A_sibling_archived_and_defined_again_resolves_to_the_live_one` and C3
  `The_variable_being_archived_stays_literal_even_while_its_row_is_still_readable` to
  `tests/SystemVariables.Application.Tests/EventHandlers/VariableArchivedDomainEventHandlerTests.cs`
  per plan.md.
- [ ] **T003 [US1]** Run `tests/SystemVariables.Application.Tests` against **unmodified** production
  code. C1–C3 must be **green**; capture verbatim output. Any red here means the premise is wrong —
  stop and report, do not proceed to T005.
- [ ] **T004 [US1]** Counterfactuals (spec §5), each reverted with `git diff` empty afterwards and a
  `--no-incremental` rebuild: (a) remove `VariableArchivedDomainEventHandler.cs:70` name-skip → C3 red;
  (b) remove the Archived predicate from `InMemoryVariableRepository.GetByNameAsync` → C1 and C2 red.
  Capture both red outputs verbatim. Commit T001–T002 only (`test(system-variables): …`).

Depends: T001, T002 → T003 → T004. (T001 ∥ T002 touch disjoint files, but they are two small edits by
one agent; no fan-out.)

## Phase 4b — the deletion (backend-engineer)

- [ ] **T005 [US1]** `src/SystemVariables/Application/EventHandlers/VariableArchivedDomainEventHandler.cs`:
  delete the `variable.State == VariableState.Archived` block (`:88-91`); add the one-line *why*
  comment above `if (!other.HasValue)` (plan.md). Leave `:70` untouched.
- [ ] **T006 [US1]** `src/SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs`:
  delete the same block (`:138-141`); same one-line comment above `if (!other.HasValue)`. Leave `:109`
  untouched.
- [ ] **T007 [US1]** Re-run `tests/SystemVariables.Application.Tests` — all green, **test files
  unmodified since T004's commit**. `dotnet build -c Release` clean. `grep VariableState.Archived` in
  `src/SystemVariables/Application/EventHandlers/` → none. An assertion needing an edit = behaviour
  moved: **block**, do not adjust. Commit (`refactor(system-variables): …`); commit must build on its
  own.

Depends: T004 → T005, T006 → T007.

## Bookkeeping (orchestrator)

- [ ] **T008** Comment on **#2200** linking spec 235: #2359's `SetVariableValueCommandHandler:43`
  branch was deliberately left for #2200's 404-vs-409 decision. Comment on **#2359** stating the same,
  so its closing PR is not read as having dealt with all three sites. Use a closing keyword for #2359
  only in the PR body.

## Out of scope — do not do

- Any edit to `SetVariableValueCommandHandler.cs` or `SetVariableValueErrors.cs` (#2200).
- Any edit to `IVariableRepository`, `VariableRepository`, `GetByNameAsync`'s contract, or the fake's
  filter (except T004's temporary, reverted counterfactual).
- `GetVariableQueryHandler` archive-then-redefine ambiguity — already **#2446**; do not re-file.
- Consolidating the push-side copies of the skip policy with `VariableSnapshotBuilder` — a refactor
  of its own, not this one.
