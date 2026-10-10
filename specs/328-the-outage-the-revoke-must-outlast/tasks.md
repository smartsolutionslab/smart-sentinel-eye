# Tasks 328 — The outage the revoke must outlast

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2629 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: a Keycloak failure on the revocation chain is retried on a
ladder instead of dead-lettering after three immediate attempts). T001 is the only
behaviour-preserving step and is characterised green (plan §5.0).
**Engineer**: `backend-engineer` (T001 seams, then T005–T007). `test-writer` for 4a (T002–T004).
**Reviewers**: `backend-reviewer` (Wolverine/DDD) + `security-reviewer` (credential revocation path).
**Tracking**: feature-level issue #2629 (already on Project #13, In Progress). No per-task issues.
**New ADR**: none (plan §1; the trigger for an ADR-0088 amendment is recorded there).
**Gate questions**: D1 `ScheduleRetry` over `RetryWithCooldown`, D2 the 1-2-5×12 ladder — both
recommendations taken (spec §Decision); fallback for D1 in plan §7.

Format: `[ID] [P?] [Story] description`. No task needs the Aspire stack except T009.

## Phase 4-prep — behaviour-free seams (backend-engineer)

- [ ] **T001 [US1]** Per plan §5.0, one commit
  `refactor(identity): add the seams for a webhook-revocation failure policy`:
  - create `src/Identity/Application/EventHandlers/WebhookClientDisableFailedException.cs` (C1,
    complete — ctor `(string integrationName, string errorCode)`, today's message verbatim; not yet thrown);
  - create `src/Identity/Infrastructure/WebhookIntegrationRevokedFailurePolicy.cs` (C3) with an
    **empty** `Apply` body;
  - add `public static void ConfigureMessageHandling(WolverineOptions options)` to
    `IdentityInfrastructureModule` registering that policy, and pass
    `configureMore: ConfigureMessageHandling` at `:162-165` (C4).
  Run `dotnet test tests/Identity.Application.Tests` and `tests/Identity.Infrastructure.Tests`
  before and after: green, unmodified. Release build clean.

Depends: none. **Blocks everything below** (the 4a tests reference these three symbols).

## Phase 4a — red (test-writer; return verbatim output)

- [ ] **T002 [P] [US1]** Create
  `tests/Identity.Infrastructure.Tests/WebhookIntegrations/WebhookIntegrationRevokedFailurePolicyTests.cs`
  per plan §5.1 Test 1: discovery via the real Identity.Application assembly +
  `IdentityInfrastructureModule.ConfigureMessageHandling(opts)` + `StubAllExternalTransports()`;
  five facts (ladder 1..14 with hard-coded `[1m, 2m, 5m×12]` read through `Delay` by reflection;
  attempt 15 → `MoveToErrorQueue`; match-nothing-wider; handler-throws-what-the-rule-matches;
  no-other-chain). Class doc names the counterfactuals of plan §5.2.
- [ ] **T003 [P] [US1]** Append
  `KeycloakUnavailable_throws_the_disable_failure_the_retry_policy_matches` to
  `tests/Identity.Application.Tests/EventHandlers/WebhookIntegrationRevokedIntegrationEventHandlerTests.cs`
  (plan §5.1 Test 2). **Do not edit any existing fact.**
- [ ] **T004 [US1]** Run
  `dotnet test tests/Identity.Infrastructure.Tests --filter "FullyQualifiedName~WebhookIntegrationRevokedFailurePolicyTests"`
  and `dotnet test tests/Identity.Application.Tests --filter "FullyQualifiedName~WebhookIntegrationRevokedIntegrationEventHandlerTests"`.
  **Required**: the four red facts of Test 1 and the new Test 2 fact fail **on assertion** for the
  reasons in plan §5.1; `No_other_discovered_chain_gained_a_failure_rule` and every existing fact
  green; zero compile errors. Any other outcome → stop and report. Commit
  `test(identity): prove a Keycloak outage dead-letters the webhook revocation within the default budget`.

Depends: T001 → T002, T003 (disjoint files) → T004.

## Phase 4b — implement (backend-engineer; T002/T003 are read-only)

- [ ] **T005 [US1]** `WebhookIntegrationRevokedIntegrationEventHandler.cs:62` — throw
  `WebhookClientDisableFailedException(integrationName, result.Error.Code)`; update the one
  `<summary>` sentence about retries to name the policy (C2).
- [ ] **T006 [US1]** Fill `WebhookIntegrationRevokedFailurePolicy.Apply`:
  `chain?.OnException<WebhookClientDisableFailedException>().ScheduleRetry(1m, 2m, 5m ×12)` scoped
  to `typeof(WebhookIntegrationRevokedV1)`; doc comment with plan §3's reasoning in brief, the
  post-ladder dead letter, and why `ScheduleRetry` (C3).
- [ ] **T007 [US1]** Re-run T004's commands (all green, test files unmodified), then both full test
  projects and a Release build. Run plan §5.2's four counterfactuals, quote each failure, revert.
  Commit `fix(2629): retry the webhook-client disable on a 1-2-5 minute ladder before dead-lettering`.

Depends: T004 → T005 → T006 → T007 (T005/T006 are one small change; not worth splitting across agents).

## Phase 5 — verify

- [ ] **T008 [US1]** Confirm the three commits each build on their own (`git rebase -x "dotnet build -c Release"`
  or equivalent) and that commit 2 fails only the named facts.
- [ ] **T009 [US1]** Spec §Independent end-to-end procedure steps 2–7 on the one Aspire stack
  (`docker pause` / `unpause` Keycloak; check no other run is using the stack first). Record A1
  (old budget, from the counterfactual), A2 (which slots fired), time from unpause to
  `DisabledWebhookClient`, the revoked-client-list entry, and an empty dead-letter store for this
  message. If the live run is not possible, say so in the verification note — do not substitute
  the unit tests for it. Latency: N/A.

## Phase 6–7

- [ ] **T010** `/code-review` + `security-review`. Reviewer checks the `configureMore:` call-site
  line by eye (plan §8: the one escape no unit test covers). PR to `develop` with `Closes #2629`;
  quote T004's red output and T007's counterfactuals; reference spec 264 §3's residual-risk row as
  now narrowed (outages > ≈ 1 h still dead-letter; option 2 not taken).
