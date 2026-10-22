# Tasks 437 — The decision point every scope asks

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #2225 · ADR-0183
**Phase 4a colour:** characterisation for existing scope/endpoint tests (captured green first, pass unmodified);
**red** for the new decision-point and handler facts. Ambiguity resolves to red.
**Engineer:** backend. **Phase 6:** `security-reviewer` mandatory + `backend-reviewer`.
Single PR; no `[P]` fan-out — every task touches `src/ServiceDefaults/Authorization` or its tests, and T004/T005 must not be split.

## Phase A — characterisation baseline (before any src change)

- [ ] T001 [US1] Run and record green, verbatim: `tests/ServiceDefaults.Tests` (`RequireScopePolicyTests`, `RegisteredPolicyTests`,
  `DefaultFabAuthorizationGuardTests`, `FabResolutionTests`), `tests/Architecture.Tests` (`FoundingDecisionRecordTests`,
  `EndpointScopeDeclarationTests`, `StatusProducerDeclarationTests`, `ScopeGrantTests`, `LegacyBundleGrantTests`,
  `KioskScopeParityTests`). These files are not edited by this PR.

## Phase B — red (test-writer)

- [ ] T002 [US1] `tests/ServiceDefaults.Tests/Authorization/DefaultAuthorizationDecisionPointTests.cs`: scope held/no fab → Allow,
  guard not called; scope absent → Deny(MissingScope), no throw; space-split multi-scope claim and repeated `scope` claims;
  prefix/case near-miss (`sse.cameras.rea`, `SSE.CAMERAS.READ`) → Deny; fab held → Allow, guard called once with the fab;
  guard throws `FabAuthorizationException` → Deny(FabNotAuthorized) carrying the fab, no throw; scope absent + fab → Deny(MissingScope),
  guard not called; a non-`FabAuthorizationException` from the guard propagates (not swallowed); null principal / blank scope → guard exception.
  Guard is a `Mock<IFabAuthorizationGuard>`.
- [ ] T003 [US1] `tests/ServiceDefaults.Tests/Authorization/ScopeRequirementHandlerTests.cs`: via `IAuthorizationService` built from
  `AddAuthorizationBuilder().AddScopePolicies(Scope.All)` + a **substituted** `IAuthorizationDecisionPoint` returning Deny for a
  principal that *does* hold the scope → policy fails (proves the policy routes through the seam, not a surviving lambda);
  Allow for a principal that does *not* → succeeds; asked with `Option.None` fab; every policy in `Scope.All` carries a `ScopeRequirement`.
- [ ] T004 [US1] Red commit, compiling: declare `IAuthorizationDecisionPoint`, `AuthorizationDecision`, `ScopeRequirement`,
  `ScopeRequirementHandler`, `DefaultAuthorizationDecisionPoint` (bodies `NotImplementedException`) **plus** constitution §IX line 380
  set to ADR-0183's exact row text and version 1.11.0 → 1.12.0 with an amendment-history entry. Run: T002/T003 red (quote output),
  `FoundingDecisionRecordTests` green. New test classes need a shard-filter entry only if in `Integration.Tests` (they are not).

## Phase C — green (backend-engineer; may not edit T002/T003 or T001's files)

- [ ] T005 [US1] Implement `DefaultAuthorizationDecisionPoint` and `ScopeRequirementHandler` per plan.md.
- [ ] T006 [US1] `RequireScopeExtensions.AddScopePolicies`: replace `RequireAssertion` with `AddRequirements(new ScopeRequirement(scope))`;
  self-register guard / decision point (`TryAddSingleton`) and handler (`TryAddEnumerable`) on `builder.Services`. Policy names unchanged.
  Update the class's doc comment only where it describes the lambda. Do not touch `RequireScope` (ADR-0183 §5).
- [ ] T007 [US1] Re-run T001's suites + T002/T003: all green, T001 files unmodified (`git diff --stat` on them empty).

## Phase D — counterfactuals (record each in the PR)

- [ ] T008 Revert T006 only (keep the lambda) → T003's "Deny for a principal holding the scope" goes red: proves T003 observes the seam.
- [ ] T009 Revert only the §IX row → `FoundingDecisionRecordTests` red ("now exists… stop recording… absent"); restore.
  Rename the interface in a scratch build → red in the other direction; restore.
- [ ] T010 Make the decision point catch `Exception` instead of `FabAuthorizationException` → T002's propagation fact red; restore.

## Phase E — verify (phase 5)

- [ ] T011 Boot stack; one scoped endpoint: 401 no token, 403 token without scope, 200 with scope; event-ingestion latency tests unmodified and green.
- [ ] T012 PR body: flag ADR-0183 edits 2 (§Security, constitution line 367) and 3 (row 023) as human-owned and still stating "does not exist".

Dependencies: T001 → T002, T003 → T004 → T005 → T006 → T007 → T008–T010 → T011 → T012.
