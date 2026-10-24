# Tasks 446: An attempt-timeout test runs on virtual time

[spec](spec.md) · [plan](plan.md) · Issue #3078 (feature-level; on Project #13 — verify by `content.url`)

**Phase 4a colour: CHARACTERISATION** (behaviour-preserving, test-only). The red evidence is counterfactuals C1-C4.
**Security-sensitive:** no. **New ADR:** no. Engineer: test-writer (T001-T004), backend-engineer (T005-T007).
No foundational Kernel/Contracts/AppHost task. T001 blocks T002/T003; T002 and T003 own disjoint files and run `[P]`.

**Forbidden:** any kept change under `src/`; any assertion other than the two named facts (and their `HangingServer`
/ `Compose` harness); widening a real-time timeout or asserting a range instead of 4; `FakeTimeProvider`; new
suppressions, analyzer or threshold changes; ADRs, the constitution, CLAUDE.md.

## Phase 4a — characterisation (test-writer)

- [ ] T001 [US1] `tests/ServiceDefaults.Tests/Fakes/SchedulingTimeProvider.cs` per plan §The fake (`Change`
      reschedules; zero due time fires at once). Then the 60 ms token-observing plant (C4) in the **unchanged**
      per-authority fact: quote `should be 4 but was 3` as the pre-change baseline, revert.
- [ ] T002 [P] [US1] `PerAuthorityPipelineRiskTests`: rewrite `The_attempt_timeout_still_bounds_each_attempt_to_a_new_authority`
      per plan §Test shape, including the T−1 tick boundary; `HangingServer.AttemptArrivedAsync`. Run; quote green.
      If it hangs to its guard on the unplanted pipeline, STOP (plan §Risks, first bullet).
- [ ] T003 [P] [US1] `RetryEveryMethodAdversarialTests`: `Compose(..., TimeProvider? clock = null)` registering it as
      the sibling class does; rewrite `An_opted_in_POST_whose_attempt_timed_out_is_sent_again`; `HangingServer`
      per-attempt signal. Run; quote green.
- [ ] T004 Commit (`test(3078): count attempt-timeout attempts on a virtual clock`), each commit building on its own.

## Phase 4b — proof (backend-engineer; may not edit the tests)

- [ ] T005 [US1] Counterfactuals C1, C2, C3, C4 (plan), each failure (or C4's and C3-control's green) quoted verbatim,
      each reverted; `git diff --stat -- src` empty.
- [ ] T006 [US1] Stress loop (plan §Verification), ≥ 48 full-suite runs, 0 failures of either fact; quote the tally.
- [ ] T007 `dotnet build -c Release` clean; `dotnet format --verify-no-changes` on the three touched files; full
      `tests/ServiceDefaults.Tests` green.

## Dependencies

T001 → {T002 ∥ T003} → T004 → T005 → T006 → T007.
