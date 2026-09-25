# Tasks 254: The race the catch mislabels

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2570 (feature-level issue; no per-task issues)
**Engineer**: `backend-engineer` · **Phase 4a colour**: **RED FIRST (behaviour-changing)**.
AS-1 and AS-4 must be observed red on unchanged `src/`, quoted verbatim.

Format: `[ID] [P?] [Story] description, file(s)`.
`[P]` = owns files no other open task touches (ADR-0109). No foundational task: nothing in
Shared.Kernel/Contracts, AppHost or Aspire resources changes.

## Phase 4a: tests (test-writer, then test-adversary; return verbatim output)

- [ ] **T001** [P] [US1] AS-4: add the `FailNextSaveWith` one-shot knob to the fake repository and the
  handler-contract test per plan §3 T-B:
  `tests/Identity.Application.Tests/Fakes/InMemoryRegisteredClientRepository.cs`,
  `tests/Identity.Application.Tests/Commands/RotateWebhookClientCommandHandlerTests.cs`.
- [ ] **T002** [P] [US1] AS-1: add the genuine Layer-2 race test per plan §3 T-A, and update the class
  doc's first paragraph:
  `tests/Integration.Tests/Identity/RegisteredClientConcurrencyIntegrationTests.cs`.
- [ ] **T003** [US1] Run on unchanged `src/` and quote verbatim:
  `dotnet test tests/Identity.Application.Tests --filter "FullyQualifiedName~RotateWebhookClientCommandHandlerTests"`
  (expected: T001's test red, reporting that a `KeycloakUnavailable` result was returned instead of a
  throw; all others green) and
  `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~RegisteredClientConcurrencyIntegrationTests"`
  (expected: T002 red with a **502 `KEYCLOAK_UNAVAILABLE`** among a round's answers; the 7 existing facts
  green). **Stop and report** if T002's red is a 500 (spec A1: wrapped shape, the fix belongs
  elsewhere) or *inconclusive* (spec A2: harness never reached Layer 2). Neither confirms the issue.
  Depends on T001, T002. One Aspire stack on the machine (stop any other first; ask the user if it is
  shared).
- [ ] **T004** [US1] test-adversary: review T002's harness per plan §3 "Adversary brief". Run the
  `Racers = 1` counterfactual, quote the inconclusive failure, and revert. Tighten T002 only if
  it can pass on buggy code (re-run T003's integration command if changed). Depends on T003.

## Phase 4b: fix (backend-engineer; T001/T002 tests may not be edited)

- [ ] **T005** [US1] Add `and not DbUpdateConcurrencyException` to the filter at
  `RotateWebhookClientCommandHandler.cs:139-140`, plus `using Microsoft.EntityFrameworkCore;` and one
  *why* comment line, per plan §4:
  `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`. Depends on T004.
- [ ] **T006** [US1] Re-run both T003 commands, all green (T001/T002 tests plus every existing test
  **unmodified**, FR-002). Then run the counterfactual in plan §4: drop the new clause, quote T001 and T002
  red, revert, and check `git diff`. Depends on T005.
- [ ] **T007** [US1] `dotnet build -c Release` (analyzers as errors), `dotnet format --verify-no-changes`,
  full `dotnet test tests/Identity.Application.Tests` and `dotnet test tests/Architecture.Tests`
  (`ConcurrencyConflictDeclarationTests`, `HandlerDeconstructionTests`, boundary rules). Depends on T006.

## Phase 5

- [ ] **T008** Verification note: T002 is itself the end-to-end observation (real HTTP, real Postgres,
  real Keycloak through the Aspire stack). Quote its pre-fix 502 and post-fix `AGGREGATE_VERSION_STALE`
  answer sets, and T006's counterfactual. No latency figure (not on the §IV path). Depends on T007.

## Orchestrator (no code): any time

- [ ] **T009** [P] Board gate: verify #2570 on Project #13 by `content.url`, `--limit 2000`.
- [ ] **T010** [P] Optional: file spec §6 O1 as its own issue (board-checked first; no `agent:ready`).

## Dependencies

```
T001 ─┐
      ├→ T003 → T004 → T005 → T006 → T007 → T008
T002 ─┘
T009, T010 independent
```

T001 and T002 own disjoint files (unit-test project vs integration-test project) and may run in
parallel. Everything after T003 is strictly sequential.
