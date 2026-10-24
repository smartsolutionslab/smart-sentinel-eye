# Tasks 451: A revoked integration is never announced rotated

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3125 (feature-level, Project #13)
**Agents:** phase 4a `test-writer` → phase 4b `backend-engineer` → phase 6 `backend-reviewer` + **`security-reviewer`
(mandatory)**. **Colour:** red. **Contract change:** none. **ADR:** none.

No `[P]` fan-out: every task owns `RotateWebhookClientCommandHandler.cs` or the one test class. Strictly sequential.

## Phase 4a — tests first (test-writer)

- [x] **T001 [US1]** Capture the baseline: `dotnet test tests/Identity.Application.Tests` on the branch tip, green;
  quote the totals.
- [x] **T002 [US1]** In `tests/Identity.Application.Tests/Commands/RotateWebhookClientOrderCharacterisationTests.cs`
  replace `A_rotation_of_an_existing_client_never_rechecks_the_status_after_its_save` with
  `A_revoke_landing_during_the_rotation_disables_the_client_and_neither_publishes_nor_commits` (plan §Phase 4a, first
  bullet). Red on `develop`.
- [x] **T003 [US1]** Add the NotFound and `DbUpdateConcurrencyException` rotate-branch exits (`ScriptedDisableHandler`),
  and the Keycloak-disable-fails exit. Red on `develop` (second status call absent).
- [x] **T004 [US1]** Add `An_unverifiable_status_after_the_rotation_leaves_the_client_enabled_and_still_announces_the_rotation`.
  Red on `develop`.
- [x] **T005 [US1]** Change `The_rotate_branch_saves_then_rolls_the_secret_then_publishes_then_commits`: `"status"`
  after `"rotate-secret"`, `Status.Calls.Count` 2. Red on `develop`. Declared literal change.
- [x] **T006 [US1]** Run the class; return the **verbatim** failure output (quoted in the PR body, ADR-0139).
- [x] **T006a [US1]** In `RotateWebhookClientCancellationTests` add `A_bound_that_expires_during_the_rotate_recheck_leaves_the_live_client_enabled`
  (cancellation while the status is read on the rotate branch is treated as `Unverifiable`, FR-003). Red before the fix.

## Phase 4b — implementation (backend-engineer; may not edit T002-T005)

- [x] **T007 [US1]** `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`: drop the
  `!existing.HasValue` gate; introduce `RaceCheck` (flag `IsExistingClient`) and thread it through the four race methods (plan §1-2).
- [x] **T008 [US1]** Same file: `Unverifiable`, or a cancelled status read, disables only when `!IsExistingClient`; update the class and method docs.
- [x] **T009 [US1]** `src/Identity/Application/Log.cs`: the four rotate-branch `[LoggerMessage]`s; call sites select
  by `IsExistingClient`.
- [x] **T010 [US1]** `dotnet test tests/Identity.Application.Tests tests/Architecture.Tests` green with T002-T005
  unmodified; `dotnet build -c Release` clean (IDE0007/IDE0005, collection expressions); `dotnet format --verify-no-changes`.

## Phase 5 — verify

- [ ] **T011 [US1]** Run `RotateRevokedWebhookIntegrationIntegrationTests` and `CrossFabWebhookRotationIntegrationTests`
  on the Aspire stack; record the happy rotate still returns 200 and publishes Rotated with two status reads.
  Latency: N/A (admin path).

## Phase 6 — review

- [ ] **T012 [US1]** `security-reviewer`: FR-003 (no disable on `Unverifiable` for a live client), the residual window,
  no secret in new log templates. `backend-reviewer`: no parameter growth, docs match both branches.
