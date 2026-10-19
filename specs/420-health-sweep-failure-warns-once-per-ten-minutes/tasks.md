# Tasks 420: the health sweep's failure Warning is throttled per failure kind

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3013 (feature-level;
already on Project #13; no per-task issues).

**Phase 4a colour: RED** (behaviour-changing, ADR-0139). Every red fact below must be observed
failing on unchanged code (`28bcec96`) **for the stated reason** (more Warnings than expected, one per
failed tick). A red fact that fails to compile, or arrives green, is a phase-4 failure: stop and
report. The characterisation set (T005) must be observed green on unchanged code and pass
**unmodified** after. Verbatim outputs go in the PR body.

**Roles:** test-writer T001-T005 (tests only; runs them; returns verbatim output). backend-engineer
T010-T011 (may not edit anything under `tests/`). Phase 5 T020. Phase 6 `/code-review`
(backend-reviewer). No security review (no new trust boundary; spec P7 confirms no remote text).

**Commit order (ADR-0087, each commit builds on its own):** red T001-T004 (one commit, one new test
file) → green T010+T011 (one commit). One red/green pair: the five red cases and the guard test one rule in one class.

**Forbidden in every task:** editing any existing test file; editing `WarningRepeatGate`,
`MediaMtxSourceRepairSweep`, `MediaMtxSourceRepair*`, `MediaMtxRtspGateway*`, `IRtspGateway`,
`StreamDistributionInfrastructureModule`, `ServiceDefaults`; any `IRtspGateway` test double; a
configuration option for the 10 minutes; a shared failure-kind type; changing
`HealthSweepFailed`'s message or level; touching `StreamHealthWatcherPollFailed`, the deadline or
the retry schedule (#3064); any suppression or `#pragma`; ADRs, the constitution, CLAUDE.md.

## Phase 1: Foundational

None. No Shared.Kernel, Shared.Contracts, AppHost or Aspire resource change; nothing blocks.

## Phase 2: US1 (P1), red

All in the new file
`tests/StreamDistribution.Infrastructure.Tests/HealthWatcher/StreamHealthWatcherSweepFailureThrottleTests.cs`
(harness per plan §5: real `MediaMtxRtspGateway` over a private scripted `HttpMessageHandler`;
watcher via `ActivatorUtilities.CreateInstance<StreamHealthWatcher>` with `ManualTimeProvider`,
`CapturingLogger<StreamHealthWatcher>`, a fixed `IClock` and a recording health handler; two Healthy
streams; ticks are repeated `DispatchAsync` calls on one watcher and one gateway). Let
`Warnings` = entries at `LogLevel.Warning`, `Repeats` = entries at `LogLevel.Debug` whose message
contains `"repeats at most every 10 minutes"`.

- [ ] **T001** [US1] `A_health_sweep_that_keeps_failing_the_same_way_warns_once_then_logs_debug`,
  a `[Theory]` over status `503` and `401`. Every response is that status. Three ticks, no time
  advanced. Assert:
  - `Warnings.Count.ShouldBe(1)`;
  - `Warnings[0].Message.ShouldBe("MediaMTX health sweep failed; skipping this tick for every stream.")`;
  - `Warnings[0].Exception.ShouldBeOfType<HttpRequestException>().StatusCode.ShouldBe((HttpStatusCode)status)`;
  - `Repeats.Count.ShouldBe(2)`, each message contains `"HttpRequestException"` and the status
    number as text, each `Exception.ShouldBeNull()`;
  - no entry at `LogLevel.Error` or above; the recorder handled no command.
  **Red on `28bcec96`**: `Warnings.Count` is 3 (and `Repeats` is 0).
- [ ] **T002** [US1] `The_same_failure_warns_again_once_ten_minutes_have_passed`. Every response
  503. Tick; advance 9 min 59 s; tick; advance 1 s; tick. Assert `Warnings.Count.ShouldBe(2)` and
  `Repeats.Count.ShouldBe(1)`, and that the Debug entry sits between the two Warnings in
  `Entries` order. **Red**: 3 Warnings.
- [ ] **T003** [US1] `A_different_failure_kind_warns_at_once`. Responses in order: 503, 503, 500,
  `200 "[]"` (a page that is not a JSON object). Four ticks, no time advanced. Assert
  `Warnings.Count.ShouldBe(3)` with exceptions in order: status 503; status 500; status `null` and
  `HttpRequestError.InvalidResponse`. `Repeats.Count.ShouldBe(1)`. **Red**: 4 Warnings.
- [ ] **T004** [US1] `A_successful_sweep_re_arms_the_warning`. Responses in order: 503, 503,
  `200 {"pageCount":1,"items":[]}`, 503. Four ticks, no time advanced. Assert
  `Warnings.Count.ShouldBe(2)`, `Repeats.Count.ShouldBe(1)`, and that the recorder handled exactly
  two commands (both cameras, "path not registered", on the third tick: neither was present in an
  earlier successful sweep, so spec 410 does not hold them). **Red**: 3 Warnings.
- [ ] **T005** [US1] Characterisation, observed green on `28bcec96` and after, unmodified: run
  `--filter "FullyQualifiedName~HealthWatcher|FullyQualifiedName~WarningRepeatGateTests|FullyQualifiedName~MediaMtxSourceRepairSweep"`
  in `StreamDistribution.Infrastructure.Tests` (on the before-run the filter also picks up the new
  file, whose five red cases are the expected reds; every other fact must be green) and
  `FullyQualifiedName~StreamDistributionRegistrationCharacterisationTests` in `Architecture.Tests`.
  Quote both outputs.
- [ ] **T006** [US1] `A_failure_with_the_same_exception_type_and_no_status_but_another_error_warns_at_once`:
  tick 1 throws `HttpRequestException(HttpRequestError.ConnectionError)` (no status), tick 2 answers a
  malformed page (`InvalidResponse`, no status). Assert 2 Warnings and 0 repeat Debug lines, pinning
  the `HttpRequestError` part of the kind. A guard against over-throttling: green on unchanged code (2 Warnings, every tick warns) and
  green after, so it is not a red fact. T003 also asserts the
  Warning/Debug/Warning/Warning order, so the Debug line is proven to fall on tick 2.

## Phase 3: US1, green

- [ ] **T010** [US1] `src/StreamDistribution/Infrastructure/Log.cs`: add `HealthSweepStillFailing`
  (Debug, `exceptionType`, `status`) directly after `HealthSweepFailed` (plan §2.4). Depends on
  nothing; same commit as T011.
- [ ] **T011** [US1] `src/StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs`:
  fourth constructor parameter `TimeProvider` with `[ActivatorUtilitiesConstructor]` and the
  three-parameter secondary constructor (plan §2.1); the `WarningRepeatGate<SweepFailure>` field,
  the private `SweepFailure` record struct, `failures.Clear()` after a successful read and
  `LogSweepFailure` (plan §2.2). Depends on T010. Then run T001-T005 again; all green, T005
  unmodified. Run `dotnet build -c Release` for the solution (analyzers clean, no new warning in the
  two files) and `dotnet format --verify-no-changes` on the touched project.

## Phase 4: verify

- [ ] **T020** Phase 5 (`/verify`): boot the stack, confirm `StreamHealthWatcherStarted` (the
  four-parameter constructor resolved from the real container), then stop MediaMTX (or patch its
  API to refuse) for at least three ticks and read the StreamDistribution structured logs: one
  `HealthSweepFailed` Warning, then `HealthSweepStillFailing` Debug lines (raise the category to
  Debug for the run, or count the Warnings only); restore MediaMTX, confirm a successful tick, stop
  it again, confirm a fresh Warning. Write the observation in the PR. If the stack cannot be booted
  (another stack on the machine), record that and rely on SC-4 plus T001-T004.

## Dependencies and parallelism

T001-T004 are one file, written together (no `[P]`: same file). T005 needs T001-T004 committed
only for the after-run. T010 and T011 are two files but one commit and T011 calls T010's method, so
they are sequential, not `[P]`. The whole slice is one backend engineer's; there is nothing to fan
out. Disjoint from PR #3065 (spec 419) by file, so it can land before or after it; if #3065 lands
first, rebase and re-run T005 only.
