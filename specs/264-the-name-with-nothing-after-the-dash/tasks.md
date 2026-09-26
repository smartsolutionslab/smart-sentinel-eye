# Tasks 264 — The name with nothing after the dash

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2575 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: an input that registered a client now answers 400). One
green guard (integration row d) pins the happy path.
**Engineer**: `test-writer` (4a) then `backend-engineer` (4b) · **Reviewer**: `backend-reviewer`
**Tracking**: feature-level issue #2575 (on Project #13, Todo — verified 2026-09-25). No per-task
issues.

Format: `[ID] [P?] [Story] description`. **No foundational task**: nothing in Shared.Kernel,
Shared.Contracts, AppHost or Aspire resources changes. T001 and T002 own disjoint files and may be
written in parallel (ADR-0109); everything after is sequential. One production file.

Stack note: the integration run boots the Aspire fixture. One machine, one Aspire stack — check for
a running AppHost/testhost from another worktree before starting, and do not stop a stack you did
not start. If a running AppHost holds `src/*/Api/bin`, build with `--artifacts-path <scratch>`.

## Phase 4a — red (test-writer; return verbatim output)

- [ ] **T001 [P] [US1]** `tests/Identity.Application.Tests/Commands/RegisterDeviceCommandHandlerTests.cs`:
  add theory `An_absent_device_identifier_is_refused_before_anything_is_created` with rows
  (`plc`, `null`), (`plc`, `""`), (`inference`, `""`) — plan §5. Assert error type
  `RegisterDeviceError.InvalidDeviceIdentifier`, `keycloak.Created` empty, `repo.Clients` empty.
  Nothing else in the file changes.
- [ ] **T002 [P] [US1]** New `tests/Integration.Tests/Identity/AbsentDeviceIdentifierIsRefusedIntegrationTests.cs`
  per plan §5: rows a–c (omitted / `null` / `""` → 400 `DEVICE_INVALID_IDENTIFIER`, read from the
  problem `title`), row d green guard (unique identifier → 201). Anonymous-object bodies only.
  `DisposeAsync` disables any `plc-` the run created (plan §5 "Red-run residue"). Failure messages
  carry status + body.
- [ ] **T003 [US1]** Run, on unchanged production code:
  `dotnet test tests/Identity.Application.Tests --filter "FullyQualifiedName~RegisterDeviceCommandHandlerTests"`
  and
  `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AbsentDeviceIdentifierIsRefused"`.
  Capture both verbatim. **Required**: T001's three rows red because the result was a **success**
  (not a compile error, not an exception); the existing five handler tests green. T002 rows a–c red
  with `201` or `409 DEVICE_ALREADY_REGISTERED` where `400` was expected (not a fixture boot
  failure); row d green. Any T001/T002 row arriving green → stop and report, do not proceed.
  Commit `test(identity): prove an absent device identifier registers a client named plc-`.

Depends: T001, T002 → T003.

## Phase 4b — the fix (backend-engineer; may not edit T001/T002's code)

- [ ] **T004 [US1]** `src/Identity/Application/Commands/Handlers/RegisterDeviceCommandHandler.cs`:
  after the `deviceType` check, early-return
  `Failure(RegisterDeviceFailures.InvalidDeviceIdentifier("must not be empty."))` when
  `string.IsNullOrWhiteSpace(deviceIdentifier)` (plan §2). No other change — no new error variant,
  no endpoint change, no value object.
- [ ] **T005 [US1]** Re-run T003's two commands: all green, `git diff HEAD~1 --` the two test files
  empty. Then the full `Identity.Application.Tests` and the `Integration.Tests` `Identity` namespace
  green, and `dotnet build -c Release` clean (analyzers, `TreatWarningsAsErrors`). Commit
  `fix(identity): refuse a device registration with an absent identifier`, body with
  `Closes #2575`.

Depends: T003 → T004 → T005.

## Phase 5 — verify

- [ ] **T006** Counterfactual: revert T004 alone, re-run T001's filter, quote the red, restore (touch
  the restored file before re-running — MSBuild keeps its old timestamp). Verification note on the
  PR: spec §5 procedure, satisfied by T005's integration output (rows a–c 400, row d 201) set beside
  T003's red output. Latency: **N/A**, not on the §IV path.

Depends: T005 → T006.

## Bookkeeping (orchestrator)

- [ ] **T007** PR to `develop` (`--base develop`): `Closes #2575`; quote T003 verbatim (the
  reproduction the issue asked for, including the observed 201/409 collision behaviour); state the
  collision verdict in one line (moot — refusal precedes lookup and Keycloak); list plan §6's three
  candidate follow-ups for a maintainer to file or decline. Re-check the spec number (264) against
  remote branches before opening. Confirm issue state after merge.
