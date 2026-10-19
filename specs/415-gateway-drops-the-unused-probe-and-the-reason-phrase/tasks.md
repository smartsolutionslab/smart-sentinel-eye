# Tasks 415: The MediaMTX gateway drops its unused single-path probe, and a refused call carries no reason phrase

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3013 (feature-level; no
per-task issues; already on Project #13).

**Delivered: PR-B (US2) only. PR-A (US1) is DEFERRED to the owner** (it deletes tests, spec A1); its boxes stay unticked. See plan §7.

| PR | Story | Phase 4a colour | Engineer |
|---|---|---|---|
| PR-A | US1 | **Characterisation** (behaviour-preserving): C1-C4 observed **green** on develop before the removal, pass **unmodified** after | backend-engineer |
| PR-B | US2 | **RED** (behaviour-changing): R1-R9 (ten tests with R8) observed **failing on assertions** on unchanged code; G1-G5 pass before and after | backend-engineer |

**Parallelism.** None worth taking: both PRs edit `MediaMtxRtspGateway.cs`, and PR-B's site list
depends on PR-A. Within PR-A, T003-T006 own disjoint files and may run `[P]`, but they all depend on
T002 (the interface change) to compile, so the practical gain is nil.

**Commands**

```sh
# PR-A
dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~Gateways.MediaMtxProbeLogVolumeTests|FullyQualifiedName~HealthWatcher.StreamHealthWatcher"
dotnet test tests/StreamDistribution.Application.Tests
dotnet build tests/Integration.Tests -c Release
dotnet test tests/Architecture.Tests
# PR-B
dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~Gateways.MediaMtxRtspGateway|FullyQualifiedName~Gateways.MediaMtxSourceRepairOverRealGateway|FullyQualifiedName~Reconciler"
```

No task needs Docker; Integration.Tests only has to **build** (doubles change, no new fact).

**Forbidden in every task:** editing any existing assertion or fact except those plan §2 names for
deletion (an edit elsewhere is a **BLOCK**); `Shared.Contracts`, `Shared.Kernel`, other contexts,
`AppHost`; `StreamHealthWatcher.cs`, `MediaMtxSourceRepair.cs`, `SourceRepairFailureLog.cs`,
`MediaMtxSourceRepairSweep.cs`, `Log.cs` templates; the `HealthSweepFailed` throttle, a body-size
limit, a read deadline, `MaxListPages` (spec §5); `CameraCatalogFabLookup.cs` and every
`EnsureSuccessStatusCode` outside the MediaMTX gateway; ADRs, constitution, CLAUDE.md; suppressions,
thresholds, `.editorconfig`, analyzer configuration.

---

## PR-A: US1, remove the unused probe (characterisation)

- [ ] **T001 [US1] test-writer**: in `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxProbeLogVolumeTests.cs`, add C1-C4 (plan §5) driving `GetAllPathsHealthAsync`, plus a list responder helper. Do not touch the existing facts. Run them on develop: **all green**; quote the output. Commit `test(3013): pin the gateway client's log volume through the health sweep`.
- [ ] **T002 [US1] backend-engineer** (depends T001): remove `GetPathHealthAsync` from `IRtspGateway.cs:46` and `MediaMtxRtspGateway.cs:157-174`, remove `NotRegistered` (`:266-270`), reword the doc comments (`IRtspGateway.cs:6`, `:50-51`, `:68-70`).
- [ ] **T003 [P] [US1]** (depends T002): drop the member from `FakeRtspGateway.cs`, `RecordingStreamDistributionPorts.cs`, `SourceRepairFakes.cs`.
- [ ] **T004 [P] [US1]** (depends T002): `StreamHealthWatcherAbsenceHoldInvariantTests.cs` (member); `StreamHealthWatcherAbsenceHoldTests.cs` (member, `PerPathCalls`, the `:162` assertion); `StreamHealthWatcherScopeTests.cs` (member, `PerPathCalls`, the `Sweep` field, the `:78-81` assertion, doc text). Spec FR-104 / A1: name each removed assertion in the PR body.
- [ ] **T005 [P] [US1]** (depends T002): delete the four probe-driven facts in `MediaMtxProbeLogVolumeTests.cs` (`A_fab_scale_sweep_of_healthy_streams_writes_no_information_record`, `A_probe_answered_not_ready_or_not_found_writes_no_information_record`, `A_probe_that_needs_retries_still_logs_them_at_warning_and_its_failure_at_error`, `An_environment_override_brings_the_probe_exchange_back_without_a_rebuild`) and any helper only they use. C1-C4 stay byte-identical.
- [ ] **T006 [P] [US1]** (depends T002): Integration.Tests doubles in `MediaMtxReconcilerPassCharacterisationTests.cs`, `MediaMtxSourceRepairLogVolumeIntegrationTests.cs` (two), `ProvisionRetireRaceIntegrationTests.cs`; comments at `ProvisionRetireRaceIntegrationTests.cs:49` and `StreamRepositoryVersionAssertionIntegrationTests.cs:101`.
- [ ] **T007 [US1]** (depends T003-T006): Release build of the solution; the PR-A commands green; `grep -rn GetPathHealthAsync src tests` empty; C1-C4 unmodified since T001 (`git diff <T001 commit> -- <file>` shows deletions only). Commit T002-T006 as one `refactor(3013): remove the unused single-path health probe`.

## PR-B: US2, no reason phrase (red)

All in a new class `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayReasonPhraseTests.cs`
unless marked. Stub: hand-written `HttpMessageHandler`, routes by method and path, answers
`new HttpResponseMessage(status) { ReasonPhrase = "rtsp://admin:SECRET@10.0.8.1/x" }`. Each R asserts
the thrown `HttpRequestException`'s `Message` **and** `ToString()` do not contain `SECRET`
(`Case.Sensitive`), and its `StatusCode` equals the stub's.

- [x] **T010 [US2] test-writer**, red tests (must fail on develop on the `SECRET` assertion; quote the failures):
  - **R9** `A_refused_single_path_health_probe_carries_no_reason_phrase` (`GetPathHealthAsync`, the fifth site; deleted by PR-A with the method). Ten red tests delivered: R1-R7 (R3 is a Theory of two), R8, R9.
  - **R1** `A_refused_add_carries_no_reason_phrase` (add/ 500).
  - **R2** `An_add_refused_with_400_for_a_path_that_does_not_exist_carries_no_reason_phrase` (add/ 400, get/ 404).
  - **R3** `A_refused_repoint_carries_no_reason_phrase` (Theory: 500, 404).
  - **R4** `A_refused_remove_carries_no_reason_phrase` (delete/ 500).
  - **R5** `A_refused_configured_paths_page_carries_no_reason_phrase` (`ListConfiguredPathsAsync`, page 0, 500).
  - **R6** `A_refused_second_configured_sources_page_carries_no_reason_phrase` (`ListConfiguredPathSourcesAsync`, page 0 a non-empty page with `pageCount: 2`, page 1 502).
  - **R7** `A_refused_health_list_page_carries_no_reason_phrase` (`GetAllPathsHealthAsync`, page 0, 503).
  - **R8** in `MediaMtxSourceRepairOverRealGatewayTests.cs`: `A_refused_patch_whose_reason_phrase_echoes_the_secret_does_not_carry_it_into_any_log_or_exception` (PATCH 400 with the hostile phrase; mirror of the existing body-echo fact; repair and gateway log entries and their exceptions carry no `SECRET`).
- [x] **T011 [US2] test-writer**, guards (must **pass** on develop and after; any red here is a stop):
  - **G1** `A_refused_call_keeps_the_error_kind_and_has_no_inner_exception`: for R1, R3 (500), R7: exact type `HttpRequestException`, `HttpRequestError` equals the value observed on develop (expected `Unknown`, spec A3; if not, record the value and use it), `InnerException` null.
  - **G2** `A_404_on_remove_is_still_tolerated_with_a_hostile_reason_phrase`.
  - **G3** `An_add_refused_with_400_for_a_path_that_exists_still_succeeds` (add/ 400, get/ 200, hostile phrase on the 400).
  - **G4** in `MediaMtxSourceRepairOverRealGatewayTests.cs`: a repair PATCH answering 404 with the hostile phrase for a stream the snapshot no longer holds takes the "path gone" branch as today (assert the same outcome the existing 404-over-real-gateway coverage asserts; reuse its setup).
  - **G5** `A_cancelled_call_still_throws_operation_cancelled`: a cancelled token before the response passes through unchanged.
  Landed as two commits: `ff3b5d3a` (guards, T011) then `3855c926` (red tests, T010). G5 cannot fail because of this change (cancellation never reaches the helper); it pins the passthrough, it is not a guard of the change. Follow-up: `ProvisionStreamGatewayFailureMessageTests` (Application, green on arrival) guards the API-response and log channel (spec P5).
- [x] **T012 [US2] backend-engineer** (depends T010-T011): add `EnsureSuccess(HttpResponseMessage response, string route)` to `MediaMtxRtspGateway.PageReading.cs` (plan §3); replace the five sites: `MediaMtxRtspGateway.cs:76`, `:92`, `:105`, `:167` and `MediaMtxRtspGateway.PageReading.cs:50`. Do not edit the tests. Commit `fix(3013): a refused MediaMTX call carries no reason phrase`.
- [x] **T013 [US2]**: PR-B commands green; R1-R9 green, G1-G5 green; quote T010's red output in the PR body.

## Review (phase 6)

- [ ] **T020** backend-reviewer on both PRs; security-reviewer (light) on PR-B: message construction
  from a dependency's response (spec 409 FR-104).
