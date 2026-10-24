# Tasks 443: A disabled client is never announced as rotated

[spec](spec.md) · [plan](plan.md) · Issue #3112 (feature-level; on Project #13 — verify by `content.url`)

Single story, single production file, so no `[P]` fan-out: T002-T003 own the same test file and T004 owns the
handler. Strictly sequential. No foundational (Kernel/Contracts/AppHost) task.

## Phase 4a — tests first (test-writer)

- [ ] T001 [US1] Characterisation: run C1 (existing
      `When_the_race_disable_itself_fails_the_create_still_returns_success_and_a_warning_is_logged`) and the C2 set
      on the unchanged handler. **Observe green; record counts.** No new characterisation test is written.
- [ ] T002 [US1] Red R1 in the same file: race-disable save throws a non-`DbUpdateException` → no Rotated, retry
      single, `commit.Snapshots` single containing the retry. **Observe red; quote the failure.**
- [ ] T003 [US1] Red R2 in the same file: same with `DbUpdateException`. **Observe red; quote the failure.**

## Phase 4b — implementation (backend-engineer; may not edit any test)

- [ ] T004 [US1] `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`: extract the OCE
      exit's tail into one private method; call it from both the OCE catch and the catch-all (after `Detach`);
      update the comments at `:83-92` and `:390-395`. R1, R2 green; C1, C2 green **unmodified**.
- [ ] T005 Counterfactuals per plan, failures quoted in the PR.
- [ ] T006 `dotnet build -c Release` clean (analyzers, IDE0007, metrics advisory); `dotnet format --verify-no-changes`
      on the touched files; Identity.Application coverage ≥ 80%.

## Phase 5 — verify

- [ ] T007 Verification note: R1/R2 red-then-green output, C-set unmodified, and the production path read
      (`DisableWebhookClientCommandHandler` → `RegisteredClientRepository.SaveAsync` → same scoped `ITransactionalCommit`).
      Latency: N/A.

## Phase 6 — review

- [ ] T008 backend-reviewer
- [ ] T009 **security-reviewer** (mandatory): no exit with `DisabledAt` set publishes Rotated; the retry is published
      before every commit that must flush it; no commit replays a failed `DbUpdateException` UPDATE; no exit leaves a
      Keycloak-enabled client without a retry.

## Phase 7

- [ ] T010 PR to `develop` (`--base develop`), body quotes T002/T003 red output and closes #3112.
