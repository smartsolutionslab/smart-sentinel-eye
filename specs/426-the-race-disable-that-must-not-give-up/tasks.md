# Tasks 426 — The race disable that must not give up

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2789 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: a failed race-path disable is retried durably instead of
only being logged). T001–T002 are behaviour-preserving and characterised green. Test C (in T004)
is a declared characterisation fact, green before and after.
**Engineer**: `backend-engineer` (T001–T002, T008–T012). **Test-writer**: T003–T007 (4a; returns
verbatim output).
**Reviewers**: `backend-reviewer` (Wolverine/EF/DDD) + `security-reviewer` (revoke/disable
semantics, ADR-0160).
**Tracking**: feature-level issue #2789 (already on Project #13). No per-task issues.
**New ADR**: none (spec §3c; recorded trigger in plan §1).
**Latency leg**: none.

Format: `[ID] [P?] [Story] description`. No task needs Docker except T014.

## Phase 4-prep — seams (backend-engineer, green)

- [ ] **T001 [US1]** Create `src/Identity/Application/EventHandlers/WebhookClientRaceDisableFailed.cs`
  (record, plan §2.1) and `WebhookClientRaceDisableFailedHandler.cs` with a `Handle` that only
  guards and returns (plan §5.0). Run `dotnet test tests/Identity.Application.Tests` and
  `tests/Identity.Infrastructure.Tests` before and after: green, unmodified. Release build clean.
  Commit 1.
- [ ] **T002 [US1]** Extract the shared disable-or-throw step (plan §2.2) out of
  `WebhookIntegrationRevokedIntegrationEventHandler`. That handler's constructor stays as it is,
  and so does its single throw site. Same two test runs, green and **unmodified**, especially
  `WebhookIntegrationRevokedIntegrationEventHandlerTests` and
  `WebhookIntegrationRevokedFailurePolicyTests`. Commit 2.

Depends: T001 → T002. **Blocks everything below.**

## Phase 4a — red (test-writer; verbatim output)

- [ ] **T003 [P] [US1]** `tests/Identity.Application.Tests/Commands/RotateWebhookClientCommandHandlerTests.cs`:
  add Test A and Test B (plan §5.1). Add the `SnapshottingTransactionalCommit` double under
  `tests/Identity.Application.Tests/Fakes/`. **Do not edit any existing fact.**
- [ ] **T004 [P] [US1]** Same file: add Test C (characterisation theory, plan §5.1). Declare it
  green-by-design in its doc comment.
  *(T003 and T004 share a file; one agent writes both. `[P]` is relative to T005/T006.)*
- [ ] **T005 [P] [US1]** Create `tests/Identity.Application.Tests/EventHandlers/WebhookClientRaceDisableFailedHandlerTests.cs`
  (Test D, five facts).
- [ ] **T006 [P] [US1]** `tests/Identity.Infrastructure.Tests/WebhookIntegrations/WebhookIntegrationRevokedFailurePolicyTests.cs`:
  add Test E's three facts. Extend `No_other_discovered_chain_gained_a_failure_rule`'s filter by
  the one clause plan §5.1 declares, and change nothing else in that fact.
- [ ] **T007 [US1]** Run
  `dotnet test tests/Identity.Application.Tests --filter "FullyQualifiedName~RotateWebhookClientCommandHandlerTests|FullyQualifiedName~WebhookClientRaceDisableFailedHandlerTests"`
  and
  `dotnet test tests/Identity.Infrastructure.Tests --filter "FullyQualifiedName~WebhookIntegrationRevokedFailurePolicyTests"`.
  **Required**: Tests A, B, D.1–D.5 and E.1–E.3 fail **on assertion**. Test C and every existing
  fact pass. Zero compile errors. Any other outcome means stop and report. Commit 3.

Depends: T002 → {T003+T004, T005, T006} (disjoint files) → T007.

## Phase 4b — implement (backend-engineer; T003–T006 files are read-only)

- [ ] **T008 [US1]** `RotateWebhookClientCommandHandler`: publish `WebhookClientRaceDisableFailed`
  on both failure exits, and detach `DbUpdateException.Entries` first on the thrown exit through a
  helper shared with the concurrency exit (plan §2.4 table). Leave the concurrency, NotFound and
  success exits untouched. Update the `<summary>` of `DisableIfRevokedSinceCommitAsync` (it
  currently says "it is still best-effort, same as before").
- [ ] **T009 [US1]** Fill `WebhookClientRaceDisableFailedHandler.Handle` (plan §2.3): deconstruct,
  parse the fab, then call the shared step.
- [ ] **T010 [US1]** `WebhookIntegrationRevokedFailurePolicy` selects both chains with the same
  ladder. Add `WebhookClientRaceDisableFailed` to `durableInboxMessageTypes` in
  `IdentityInfrastructureModule`. Change the text of `Log.cs` `CouldNotDisableClientCreatedDuringRevokeRace`
  (FR-008).
- [ ] **T011 [US1]** Re-run T007's commands: everything green, test files unmodified. Then run both
  full test projects, `tests/Architecture.Tests` (HandlerDeconstruction, Boundary,
  AuditPayloadRedactionCoverage, EventMetadataFabDeclaration) and a Release build with
  `dotnet format --verify-no-changes`. Run plan §5.2's five counterfactuals, quote each failure,
  and revert. Commit 4.
- [ ] **T012 [US1]** Routing check (plan §4). Against the pinned Wolverine, establish the routes for
  `WebhookClientRaceDisableFailed`: expected a RabbitMQ route to `identity.<FullName>` plus an
  additive local route. Use decompiling, or a throwaway host with `StubAllExternalTransports()` and
  `IWolverineRuntime.RoutingFor`. Record the finding in the PR. If there is **no** RabbitMQ route,
  stop and report BLOCKED (it would need a ServiceDefaults change).

Depends: T007 → T008 → T009 → T010 → T011; T012 may run in parallel with T008–T011 (read-only).

## Phase 5 — verify

- [ ] **T013 [US1]** Check that every one of the four commits builds on its own, and that commit 3
  fails only the named facts.
- [ ] **T014 [US1]** (Docker; skip and say so if unavailable). Boot the one Aspire stack. Confirm
  queue `identity.SmartSentinelEye.Identity.Application.EventHandlers.WebhookClientRaceDisableFailed`
  exists, that Identity's listener for it is durable (`wolverine_identity` node/listener tables or
  the Wolverine dashboard), and that `RotateRevokeRaceIntegrationTests` passes. State plainly that
  the failing-disable path is **not** observed live: there is no fault injection for a
  millisecond-window Keycloak failure. Latency: N/A.

## Phase 6–7

- [ ] **T015** `/code-review` + `/security-review`. Reviewers check by eye: the `durableInboxMessageTypes`
  line (no test covers it), FR-004's detach, and that the concurrency exit still never commits.
  PR to `develop` with `Closes #2789`. Quote T007's red output, T011's counterfactuals and T012's
  routing finding. Note option C as still deferred, and §2b ("rotated" for a revoked integration)
  as a recorded, unchanged decision of spec 318.
