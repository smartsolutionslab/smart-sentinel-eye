# Tasks 356: A webhook rotation's save failure is not a Keycloak failure

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2913 (feature-level; no
per-task issues).

**Phase 4a colour: RED** (behaviour-changing). T002's tests must be observed **failing** before T003
exists, and pass **unmodified** after T003. A test arriving green is a phase-4 failure.

**Engineer:** backend-engineer. **Test-writer:** T002.

**Commits.** One per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). The red commit
(T002) leaves `Identity.Application.Tests` failing until T003 lands (accepted per CLAUDE.md "Builds
is not passes"); every commit builds on its own.

**Forbidden:** any file outside the two below plus this spec directory; `RotateWebhookClientError`,
`Shared.Contracts`, the endpoint, ServiceDefaults; any ADR or the constitution; editing any test other
than those named in T002.

Legend: `[P]` = disjoint files. Single slice, all `[US1]`; strictly sequential (T003 depends on T002's
red), so nothing fans out.

## Phase 1: Baseline (orchestrator)

- [ ] **T001** [US1] Re-check spec number 356 against `origin/develop` and every remote head. Run
  `dotnet test tests/Identity.Application.Tests --filter "FullyQualifiedName~RotateWebhookClient"`
  green on the untouched branch.

## Phase 2: Red tests (test-writer; blocks Phase 3)

- [ ] **T002** [US1] In `tests/Identity.Application.Tests/Commands/RotateWebhookClientOrderCharacterisationTests.cs`
  (plan §4):
  - replace `A_non_concurrency_save_failure_on_rotate_is_answered_keycloak_unavailable_without_calling_keycloak`
    with `A_non_concurrency_save_failure_on_rotate_propagates_and_never_calls_keycloak`;
  - add `A_save_failure_that_is_not_an_ef_exception_on_rotate_propagates_and_never_calls_keycloak`;
  - replace `A_non_concurrency_save_failure_on_create_is_answered_keycloak_unavailable_after_the_client_was_created`
    with `A_non_concurrency_save_failure_on_create_propagates_after_keycloak_created_the_client`;
  - update the class summary's "pinned as it is" paragraph and the comment above the rotate test.
  Run the filter from T001; return the **verbatim** output showing exactly these three failing
  (nothing thrown) and every other test passing.

## Phase 3: Fix (backend-engineer; depends on T002)

- [ ] **T003** [US1] In `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`,
  narrow the `try` to the Keycloak Admin calls (plan §3); drop the now-dead
  `DbUpdateConcurrencyException` exclusion and its comment. Tests from T002 must pass **unmodified**;
  all other `RotateWebhookClient*` tests unmodified and green. `dotnet build -c Release` clean;
  `dotnet format --verify-no-changes` clean.

## Phase 5: Verify

- [ ] **T004** [US1] Full `Identity.Application.Tests` green; Application coverage gate holds
  (ADR-0065). Verification note: unit-level proof only (plan §5) — state it.
