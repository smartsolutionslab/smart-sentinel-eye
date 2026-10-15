# Tasks 370: `RetryEveryMethod()` reaches the pipeline `ConfigureHttpClientDefaults` builds

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2958 (feature-level;
no per-task issues).

**Phase 4a colour: RED** (behaviour-changing). T001-T003 must be **observed failing on unfixed
production code (`a1554465`)** for the rows spec §4.1-§4.3 mark red, and the failure output is
quoted verbatim in the PR. A red-marked fact that arrives green is a phase-4 failure. The
engineer may not edit T001-T003 to pass.

**Engineer:** backend-engineer (T010). **Test-writer:** T001-T003. **Security-sensitive:** Phase 6
runs `/security-review` (retry safety: a predicate shared by every client).

**Forbidden in every task:** any production file other than
`src/ServiceDefaults/Resilience/IdempotentRetry.cs`; the seven call sites (spec §1); `Extensions.cs`;
`ResilienceRegistrationTests.cs`, `ResilienceHandlerNestingTests.cs`; any assertion in
`RevocationCompositionCharacterisationTests.cs`, `FixtureRetryPolicyTests.cs` or
`MediaMtxProbeLogVolumeTests.cs`; ADRs, the constitution, CLAUDE.md; new suppressions, thresholds,
`.editorconfig`, csproj files.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No
foundational task: nothing here touches Shared.Kernel, Shared.Contracts or AppHost.

---

## Phase 1: Baseline (orchestrator; no files)

- [ ] **T000** Record verbatim for the PR body: `dotnet test tests/ServiceDefaults.Tests -c Release`
  and `dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~RegistrationCharacterisation|FullyQualifiedName~ResilienceRegistration"`
  summary lines on `a1554465`.

## Phase 2: Red tests (test-writer; all three disjoint)

- [ ] **T001** [P] [US1] `tests/ServiceDefaults.Tests/Resilience/IdempotentRetryTests.cs` — `Build`
  composes through `ConfigureHttpClientDefaults` (spec §4.1, FR-007); add opted-in PATCH (4), opted-in
  thrown POST (4), opted-in POST answered 400 (1), sibling-without-opt-in POST (1). Existing seven
  fact bodies unmodified. Observe: the three opt-in rows red at 1, everything else green.
- [ ] **T002** [P] [US1] `tests/ServiceDefaults.Tests/Resilience/RetryEveryMethodCompositionTests.cs`
  (new; spec §4.2) — real `AddServiceDefaults()`, zero delay on `-standard`, opted-in POST -> 4.
  Observe red at 1.
- [ ] **T003** [P] [US2] `tests/Architecture.Tests/Persistence/EventIngestionRegistrationCharacterisationTests.cs`
  and `tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs` —
  rewrite the three `*_opts_back_in_to_retrying_every_method` facts to walk the named client's handler
  chain for `RetryEveryMethodMarker` (spec §4.3, plan §5); update the StreamDistribution class doc.
  Observe red (marker absent).
  Commit T001-T003: `test(service-defaults): an opted-in client retries its POSTs under the client defaults`.

## Phase 3: Fix (backend-engineer; depends on T001-T003)

- [ ] **T010** [US1] `src/ServiceDefaults/Resilience/IdempotentRetry.cs` — marker handler + request
  option; `RetryEveryMethod` adds the marker and drops the named-options configure; predicate honours
  the marker (plan §3, invariants I1-I3). T001-T003 green **unmodified**; spec §4.4 list green
  unmodified. Release build clean (no new S138).
  Commit: `fix(service-defaults): RetryEveryMethod reaches the pipeline the client defaults build`.

## Phase 4: Stale comments (backend-engineer; depends on T010)

- [ ] **T011** [P] `tests/ServiceDefaults.Tests/Revocation/RevocationCompositionCharacterisationTests.cs`
  lines 263-270 and `tests/Integration.Tests/Fixtures/FixtureRetryPolicyTests.cs` lines 107-111 —
  comment-only; prove by the strip-comments hash (code hash identical before/after).
  Commit: `test(service-defaults): the harness comments describe where the opt-in is read`.

## Phase 5: Verify (orchestrator)

- [ ] **T020** Full `tests/ServiceDefaults.Tests` and `tests/Architecture.Tests` green; quote T001-T003
  red output and the green re-run in the PR; record the counterfactual (remove `.RetryEveryMethod()`
  from `AddMediaMtxGateway` -> gateway fact red; restore). Report spec §8's three out-of-scope
  findings in the PR body.

## Dependencies

T000 -> {T001, T002, T003} (parallel) -> T010 -> T011 -> T020. Every commit builds on its own; the
T001-T003 commit leaves its suites red until T010 lands (accepted red/green pair, CLAUDE.md).
