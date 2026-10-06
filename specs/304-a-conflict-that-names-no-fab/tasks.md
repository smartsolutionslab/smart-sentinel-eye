# Tasks 304 — A conflict that names no fab

Phase 4a colour: **characterisation — observed green** (behaviour-preserving;
spec §2). A red result is a finding, not a starting point.

## US1 — A registration conflict reads the same whoever holds the id

- [ ] **T001 [P] [US1]** (test-writer) Add U1 to
  `tests/Identity.Application.Tests/Commands/RegisterDeviceCommandHandlerTests.cs`
  (plan §4.2). Run; quote output.
- [ ] **T002 [P] [US1]** (test-writer) New
  `tests/Integration.Tests/Identity/CrossFabRegistrationConflictIntegrationTests.cs`
  with C1, C2 (plan §4.1) + entry in `tests/Integration.Tests/ci-shards/shard-3.filter`.
  Run against the Aspire fixture; quote output.
- [ ] **T003 [US1]** (orchestrator) Gate on T001+T002:
  all green → no production change, go to T005.
  any red → T004.
- [ ] **T004 [US1]** (backend-engineer, contingency only) Apply plan §1 contingency
  in `RegisterDeviceCommandHandler.cs`; tests from T001/T002 unmodified must go green.
- [ ] **T005 [US1]** Verify: run the full Identity unit suite + plan §4's unmodified
  list; confirm `git diff --stat src/` empty (if T004 skipped).
- [ ] **T006** PR to `develop` (`--base develop`), `Closes #2626`; quote T001/T002
  output; state that the 409 was already holder-neutral and is now pinned; list
  spec §6 residuals (existence oracle → ADR candidate; kiosk enroll sibling) for a
  maintainer. Re-check spec number 304 against remote branches before opening.

T001 and T002 own disjoint files → parallel. No foundational task.
