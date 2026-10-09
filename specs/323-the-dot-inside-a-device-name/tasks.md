# Tasks 323 — The dot inside a device name

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2627 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: identifiers like `-`, `x.y`, `x_y`, `x--y` that registered a
client now answer 400). One green-guard theory pins the accepted shapes.
**Engineer**: `test-writer` (4a) then `backend-engineer` (4b) · **Reviewer**: `backend-reviewer`
**Tracking**: feature-level issue #2627. No per-task issues.
**New ADR**: none.

Format: `[ID] [P?] [Story] description`. **No foundational task**: nothing in Shared.Kernel,
Shared.Contracts, AppHost or Aspire resources changes. All tasks are sequential (one test file, one
production file). No Aspire boot needed for 4a/4b.

## Phase 4a — red (test-writer; return verbatim output)

- [ ] **T001 [US1]** `tests/Identity.Application.Tests/Commands/RegisterDeviceCommandHandlerTests.cs`:
  add theory `An_identifier_that_is_not_hyphen_separated_alphanumeric_segments_returns_InvalidDeviceIdentifier`
  with the ten rows and assertions in plan §5 (error type, exact `Reason`, `repo.Clients` and
  `keycloak.Created` empty). Add green-guard theory
  `A_hyphen_separated_alphanumeric_identifier_registers` with rows `x`, `4station`, `station-4`,
  `line-3-cell-2`. Nothing else in the file changes.
- [ ] **T002 [US1]** Run, on unchanged production code:
  `dotnet test tests/Identity.Application.Tests --filter "FullyQualifiedName~RegisterDeviceCommandHandlerTests"`.
  Capture verbatim. **Required**: nine T001 rows red because the result was a **success**; row
  `" x"` red on the `Reason` assertion (ClientId's message, not the new one); no compile error or
  unexpected exception; green-guard rows and every pre-existing test green. Any T001 row green →
  stop and report.
  Commit `test(identity): prove a device identifier with punctuation in a segment registers a client`.

Depends: T001 → T002.

## Phase 4b — implement (backend-engineer; tests are read-only)

- [ ] **T003 [US1]** `src/Identity/Application/Commands/Handlers/RegisterDeviceCommandHandler.cs`:
  add the hyphen-separated-alphanumeric early return from plan §2 directly after the empty check.
  `ClientId` and the existing `try/catch` are not touched. No other file changes.
- [ ] **T004 [US1]** Re-run T002's command: all green, test file unmodified. Run
  `dotnet format --verify-no-changes` on the Identity projects and a Release build of
  `src/Identity/Application`. Commit
  `fix(identity): refuse a device identifier that is not hyphen-separated alphanumeric segments`.

Depends: T002 → T003 → T004.

## Phase 5 — verify

- [ ] **T005 [US1]** Against the running stack, spec §Independent end-to-end test: `x.y` and `-` →
  400 `DEVICE_INVALID_IDENTIFIER`; a unique `e2e-<guid:N>` → 201, then disable it. Quote all
  responses in the PR. Latency: N/A (administrative path).
