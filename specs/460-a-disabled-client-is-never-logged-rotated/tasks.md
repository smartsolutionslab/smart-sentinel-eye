# Tasks 460: A disabled client is never logged as rotated

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3139
**Agents:** phase 4a `test-writer` → phase 4b `backend-engineer` → phase 6 `backend-reviewer`.
**Colour:** red. **Contract change:** none. **ADR:** none. **Security review:** not required (log content only).

No `[P]` fan-out: every task owns either `RotateWebhookClientCommandHandler.cs` or the one test class. Strictly
sequential. No foundational (Shared/AppHost) task.

## Phase 4a — tests first (test-writer)

- [ ] **T001 [US1]** Baseline: `dotnet test tests/Identity.Application.Tests` on the branch tip, green; quote totals.
- [ ] **T002 [US1]** In `RotateWebhookClientOrderCharacterisationTests.cs` change the create-branch race test's
  `:313-314` assertion to `Named("RotatedWebhookIntegration").ShouldBeEmpty()` (plan §Phase 4a item 1). Declared
  literal change. Red on `develop`.
- [ ] **T003 [US1]** Add the four rotate-branch disabled-exit tests (plan items 2-5). Red on `develop`.
- [ ] **T004 [US1]** Add the create-branch NotFound test (plan item 6). Red on `develop`.
- [ ] **T005 [US1]** Add the two green guard assertions (failed-disable-still-announced, Unverifiable-on-rotate).
  Observed green before the fix.
- [ ] **T006 [US1]** Run the class; return the **verbatim** output (6 red, rest green) for the PR body (ADR-0139).

## Phase 4b — implementation (backend-engineer; may not edit T002-T005)

- [ ] **T007 [US1]** `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`: move
  `logger.RotatedWebhookIntegration(...)` inside the `if (!disabledDuringRace)` block, after `CommitAsync`
  (plan §Design). One-clause comment update; nothing else.
- [ ] **T008 [US1]** `dotnet test tests/Identity.Application.Tests` all green; Release build clean
  (`dotnet build -c Release` for the Identity projects); `dotnet format --verify-no-changes` on touched files.

## Phase 5-7

- [ ] **T009** Verification note: unit-level observation is the end-to-end evidence here (log content of one handler;
  no wire change). Quote T006 red and T008 green in the PR body; PR to `develop` with `Fixes #3139`.

**Dependencies:** T001 → T002-T005 → T006 → T007 → T008 → T009.
