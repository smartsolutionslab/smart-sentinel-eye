# Tasks 451: A revoked integration is never announced rotated

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3125 (feature-level, Project #13)
**Agents:** phase 4a `test-writer` → phase 4b `backend-engineer` → phase 6 `backend-reviewer` + **`security-reviewer`
(mandatory)**. **Colour:** red. **Contract change:** none. **ADR:** none.

No `[P]` fan-out: every task owns `RotateWebhookClientCommandHandler.cs` or the one test class. Strictly sequential.

## Phase 4a — tests first (test-writer)

- [ ] **T001 [US1]** Capture the baseline: `dotnet test tests/Identity.Application.Tests` on the branch tip, green;
  quote the totals.
- [ ] **T002 [US1]** In `tests/Identity.Application.Tests/Commands/RotateWebhookClientOrderCharacterisationTests.cs`
  replace `A_rotation_of_an_existing_client_never_rechecks_the_status_after_its_save` with
  `A_revoke_landing_after_the_precheck_of_a_rotation_disables_the_client_and_announces_nothing` (plan §Phase 4a, first
  bullet). Red on `develop`.
- [ ] **T003 [US1]** Add the NotFound and `DbUpdateConcurrencyException` rotate-branch exits (`ScriptedDisableHandler`),
  and the Keycloak-disable-fails exit. Red on `develop` (second status call absent).
- [ ] **T004 [US1]** Add `An_unverifiable_recheck_after_a_rotation_neither_disables_nor_withholds_the_announcement`.
  Red on `develop`.
- [ ] **T005 [US1]** Change `The_rotate_branch_saves_then_rolls_the_secret_then_publishes_then_commits`: `"status"`
  after `"rotate-secret"`, `Status.Calls.Count` 2. Red on `develop`. Declared literal change.
- [ ] **T006 [US1]** Run the class; return the **verbatim** failure output (quoted in the PR body, ADR-0139).

## Phase 4b — implementation (backend-engineer; may not edit T002-T005)

- [ ] **T007 [US1]** `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`: drop the
  `!existing.HasValue` gate; introduce `RaceCheck` and thread it through the four race methods (plan §1-2).
- [ ] **T008 [US1]** Same file: `Unverifiable` disables only when `!IsRotation`; update the class and method docs.
- [ ] **T009 [US1]** `src/Identity/Application/Log.cs`: the four rotate-branch `[LoggerMessage]`s; call sites select
  by `IsRotation`.
- [ ] **T010 [US1]** `dotnet test tests/Identity.Application.Tests tests/Architecture.Tests` green with T002-T005
  unmodified; `dotnet build -c Release` clean (IDE0007/IDE0005, collection expressions); `dotnet format --verify-no-changes`.

## Phase 5 — verify

- [ ] **T011 [US1]** Run `RotateRevokedWebhookIntegrationIntegrationTests` and `CrossFabWebhookRotationIntegrationTests`
  on the Aspire stack; record the happy rotate still returns 200 and publishes Rotated with two status reads.
  Latency: N/A (admin path).

## Phase 6 — review

- [ ] **T012 [US1]** `security-reviewer`: FR-003 (no disable on `Unverifiable` for a live client), the residual window,
  no secret in new log templates. `backend-reviewer`: no parameter growth, docs match both branches.
