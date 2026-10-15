# Tasks 375: Each upstream authority gets its own circuit breaker

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2959 (feature-level; no
per-task issues).

**Phase 4a colour: RED** (behaviour-changing). T001-T002's red rows (spec §4.1, §4.2) must be
**observed failing on unfixed production code** and the failure output quoted verbatim in the PR. A
red-marked fact that arrives green is a phase-4 failure. Declared guards are green before and after and
are not red evidence. The engineer may not edit T001-T002 to pass.

**Adversarial pass required** (T003, failure mode: outage). **Engineer:** backend-engineer.
**Security-sensitive:** Phase 6 runs `security-reviewer` (revocation refresh is fail-static, ADR-0160
§3; focus list in plan §6).

**The one existing assertion expected to change:** the hazard fact in
`RetryEveryMethodAdversarialTests` (spec §4.1), declared here and quoted in the PR. Any other existing
assertion that has to change is a block, not an adjustment.

**Forbidden in every task:** ADRs, the constitution, CLAUDE.md; any production behaviour change outside
`src/ServiceDefaults/Extensions.cs`; a second `AddStandardResilienceHandler(`; `ResilienceRegistrationTests.cs`;
any other assertion in `tests/ServiceDefaults.Tests`, the `*RegistrationCharacterisationTests`, or
`MediaMtxProbeLogVolumeTests.cs`; new suppressions, thresholds, `.editorconfig`, csproj files.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No foundational
task: nothing touches Shared.Kernel, Shared.Contracts or AppHost.

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Re-check the spec number (spec header). Record verbatim for the PR body on the base SHA:
  `dotnet test tests/ServiceDefaults.Tests -c Release` and
  `dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~ResilienceRegistration|FullyQualifiedName~RegistrationCharacterisation"`.

## Phase 2: Red tests (test-writer)

- [ ] **T001** [P] [US1] `tests/ServiceDefaults.Tests/Resilience/RetryEveryMethodAdversarialTests.cs` —
  invert and rename the hazard fact (spec §4.1); replace its doc's "needs its own ADR" sentence; update
  the class doc's "Hazard" paragraph (FR-009). No other fact touched. Observe red:
  `BrokenCircuitException` where 200 is expected.
- [ ] **T002** [P] [US1] `tests/ServiceDefaults.Tests/Resilience/PerAuthorityCircuitBreakerTests.cs`
  (new) — the seven facts of spec §4.2 through real `AddServiceDefaults()`. Observe the three red rows
  red and the four guards green.
  Run: `dotnet test tests/ServiceDefaults.Tests --filter "FullyQualifiedName~Resilience"`.
  Commit T001-T002: `test(service-defaults): one upstream's failures do not open another's breaker`.

## Phase 3: Fix (backend-engineer; depends on T001-T002)

- [ ] **T010** [US1] `src/ServiceDefaults/Extensions.cs` — chain `.SelectPipelineByAuthority()` on the
  one `AddStandardResilienceHandler(...)` call, with the comment of plan §2 (FR-001, FR-007).
  `src/ServiceDefaults/Resilience/IdempotentRetry.cs` — class doc sentence only (FR-009). T001-T002 green
  **unmodified**; spec §4.3 list green unmodified; Release build clean.
  Commit: `fix(service-defaults): each upstream authority gets its own circuit breaker`.

## Phase 4: Adversarial (test-adversary; depends on T010)

- [ ] **T003** [US1] Spec §4.4 hunt, outage failure mode. New facts only, in
  `PerAuthorityCircuitBreakerTests.cs` or a new file under `tests/ServiceDefaults.Tests/Resilience/`.
  Each finding: a failing test (back to T010's engineer, one retry) or a written "not reproducible"
  with the attempt, for the PR body.

## Phase 5: Documentation at the nesting sites (backend-engineer; depends on T010)

- [ ] **T011** [P] [US2] Comment at each nesting registration (spec §5, plan §5):
  `src/ServiceDefaults/AuthenticationDefaults.cs` (`RevocationListAuthorizationHandler`),
  `src/Identity/Infrastructure/IdentityInfrastructureModule.cs` (`KeycloakAdminAuthorizationHandler`),
  `src/StreamDistribution/Infrastructure/StreamDistributionInfrastructureModule.cs`
  (`CameraCatalogAuthorizationHandler`),
  `src/SystemVariables/Infrastructure/SystemVariablesInfrastructureModule.cs`
  (`OverlayDesignerAuthorizationHandler`).
- [ ] **T012** Prove T011 comment-only: strip comments, hash each of the four files before and after;
  identical hashes quoted in the PR; counterfactual (a one-token code change alters the hash) recorded.
  Commit T011: `docs(resilience): nested token mints say why their retries multiply`.

## Phase 6: Verify (orchestrator)

- [ ] **T020** Full `dotnet test tests/ServiceDefaults.Tests -c Release` and
  `dotnet test tests/Architecture.Tests -c Release` green; quote the T001-T002 red output and the green
  re-run; record the counterfactual (remove `.SelectPipelineByAuthority()` → the inverted hazard fact
  and the three red rows fail; restore). Latency: N/A (spec §7).

## Dependencies

T000 → {T001, T002} (parallel, disjoint files) → T010 → {T003, T011} (parallel: T003 writes tests only,
T011 writes comments in `src` only) → T012 → T020. Every commit builds on its own; the T001-T002 commit
leaves `ServiceDefaults.Tests` red until T010 lands (accepted red/green pair, CLAUDE.md).
