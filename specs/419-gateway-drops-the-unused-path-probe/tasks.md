# Tasks 419: The MediaMTX gateway drops its unused single-path health probe

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3013 (feature-level, on
Project #13; no per-task issues).

| Story | Phase 4a colour | Engineer |
|---|---|---|
| US1 | **Characterisation** (behaviour-preserving, dead-code removal): C1-C4 observed **green** on develop before the removal; they and every test outside D1-D7 pass **unmodified** after | backend-engineer |

**Parallelism.** None worth taking. T003-T004 own disjoint files and could be `[P]`, but all depend
on T002 to compile and are a few lines each; one engineer, sequential. Nothing foundational outside
the context.

**Commands**

```sh
dotnet test tests/StreamDistribution.Infrastructure.Tests
dotnet test tests/StreamDistribution.Application.Tests
dotnet test tests/Architecture.Tests
dotnet build SmartSentinelEye.slnx -c Release   # includes Integration.Tests
grep -rn "GetPathHealthAsync" src tests          # must be empty
```

No task needs Docker.

**Authorised deletions (owner, #3013, 2026-10-19), and nothing else:** D1-D4 (four facts in
`MediaMtxProbeLogVolumeTests`), D5 (spec 415 R9), D6/D7 (the two `PerPathCalls.ShouldBe(0)`
assertions). Spec FR-004 names each.

**Forbidden in every task:** editing or deleting any assertion, fact or test name outside D1-D7;
changing C1-C4 after T001; touching `StreamHealthWatcher.cs`, `MediaMtxSourceRepair*.cs`,
`MediaMtxReconciler.cs`, `MediaMtxRtspGateway.PageReading.cs`, `ParsePathHealth` and its helpers,
`Shared.*`, AppHost, other contexts, ScenarioSimulator; the `HealthSweepFailed` throttle, a body-size
limit, a read deadline, `MaxListPages`; ADRs, constitution, CLAUDE.md; suppressions, thresholds,
`.editorconfig`, analyzer configuration. Any of these is a **BLOCK**, not a judgement call.

---

## US1: remove the unused probe

- [ ] **T001 [US1] test-writer**: in `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxProbeLogVolumeTests.cs` add C1-C4 (plan §4) plus a one-page list responder helper, reusing `BuildGatewayHost`, `CountRoutine`, `Respond` and the category constants. Do not touch the existing facts. Run the class on develop: **C1-C4 all green**; quote the verbatim output. Commit `test(3013): pin the gateway client's log volume through the health sweep`. A red C-fact is a stop and a report, never an adjusted assertion.
- [ ] **T002 [US1] backend-engineer** (depends T001): S1-S4 (plan §2): remove `GetPathHealthAsync` from `IRtspGateway.cs:46` and `MediaMtxRtspGateway.cs:171-188`, remove `NotRegistered` (`:280-284`), reword `IRtspGateway.cs:6`, `:50-51`, `:68-70`.
- [ ] **T003 [US1]** (depends T002): mechanical double edits T1-T4, T9-T11 members (plan §2): `FakeRtspGateway.cs`, `RecordingStreamDistributionPorts.cs`, `SourceRepairFakes.cs`, `StreamHealthWatcherAbsenceHoldInvariantTests.cs`, `MediaMtxReconcilerPassCharacterisationTests.cs`, `MediaMtxSourceRepairLogVolumeIntegrationTests.cs` (two doubles), `ProvisionRetireRaceIntegrationTests.cs`; comments T11 (`:49-50`) and T12 (`StreamRepositoryVersionAssertionIntegrationTests.cs:100-102`).
- [ ] **T004 [US1]** (depends T002): T5/T6: `StreamHealthWatcherAbsenceHoldTests.cs` (D7 assertion `:162`, `PerPathCalls`, member); `StreamHealthWatcherScopeTests.cs` (D6 assertion `:78-81`, `Sweep` record field and its constructor argument, `PerPathCalls`, member, class doc `:31-35` and stub doc `:296-302`). Fact names stay.
- [ ] **T005 [US1]** (depends T002): T7/T8: delete D1-D4 from `MediaMtxProbeLogVolumeTests.cs` and any helper only they used (spec B2); delete D5 `A_refused_single_path_health_probe_carries_no_reason_phrase` from `MediaMtxRtspGatewayReasonPhraseTests.cs`. C1-C4 byte-identical to T001.
- [ ] **T006 [US1]** (depends T003-T005): Release build clean (no new warning); the commands above green; SC-1 greps empty; SC-3 `git diff <T001 sha>..HEAD -- tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxProbeLogVolumeTests.cs` deletions only; SC-4 `git diff --stat <T001 sha>..HEAD` lists only plan §2's files. Commit T002-T005 as one `refactor(3013): remove the unused single-path health probe`. PR body names D1-D7 each with "exists only for the removed member" and cites the owner comment.
- [ ] **T007 [US1]** (depends T006): tick these boxes; add one line to spec 415's status and SC-1 pointing at spec 419 as US1's delivery. Commit `docs(3013): tick the tasks of spec 419`.

## Review (phase 6)

- [ ] **T020** backend-reviewer: SC-3/SC-4 diffs (no edit outside D1-D7 and plan §2's mechanical lines), C1-C4 vs D1-D4 pairwise equivalence, no dead member left on a double.
