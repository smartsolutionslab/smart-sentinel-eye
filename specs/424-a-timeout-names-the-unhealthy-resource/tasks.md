# Tasks: A timeout names the unhealthy resource

**Feature**: 424 | **Issue**: #2146 | **Spec**: `./spec.md` | **Plan**: `./plan.md`
**Branch**: `test/2146-fixture-timeout-snapshot`
**Worktree**: `D:/Github/sse-2146` (cut from `origin/develop` at `7909dd20`)

---

## The three declarations (ADR-0144)

### 1. Engineer

**`infra-engineer`.** The change is `AspireFixture.cs` and its Docker-free test
classes — the Aspire test fixture and how it reports a failed boot. Specs 062,
084 and 098 changed the same file for the same reason and were all
`infra-engineer`. There is no bounded context, value object, EF, Wolverine or
migration work, so `backend-engineer`'s brief does not fit.

### 2. Behaviour-changing or -preserving

**Behaviour-changing. Phase 4a is RED.** The timeout message says something it
does not say today: a health per running resource, bounded report lines, and a
cause sentence naming an unhealthy resource.

**How red is shown, without Docker** — `Category=FixtureLogic`, run against the
Release build:

- **One red needs no seam at all**: the 2026-09-07 sighting (US2, T003's first
  fact) calls the **existing** two-argument `FormatLikelyCause` with
  `mediamtx = RuntimeUnhealthy`, `stream-distribution = FailedToStart` and
  asserts `ShouldStartWith("Likely cause: ")` + names `mediamtx`. Today it
  returns `string.Empty` — a runtime assertion failure on the merge base.
- **The health reds need a seam first** (T001). A test passing a `health`
  argument to a function that has no such parameter is a **compile error, not a
  red** — and a missing-overload error in one test file can suppress the
  project's other diagnostics (recorded in this repo's memory). So T001 adds the
  parameter, accepted and ignored, as a shape-only change; the reds then compile
  and fail on their assertions. Precedent: spec 084's
  `refactor(integration): the vocabulary of a startup death, with no call site`.

**T001 is behaviour-preserving and carries its own colour**: characterisation,
observed green — the existing `Category=FixtureLogic` suite, unmodified, before
and after. Its output is quoted in the PR beside the red.

**A compile-error "red" does not satisfy the gate.** If a red test only fails to
compile, phase 4a is not done.

### 3. Does this need a new ADR?

**No.** The owner decided option (c) on 2026-10-21 and said "no ADR". It is a
diagnostic inside the fixture's existing timeout `catch`: it applies ADR-0103 and
ADR-0068 without amending them, as spec 084's sweep did. The watchdog that
**would** have needed one (the supervised boot) is explicitly not built.

### Security review / latency

- **Security review: not needed.** No endpoint, auth, secret, scope or trust
  boundary; test-process-only text.
- **Latency leg: none** (constitution §IV) — test fixture only.

### Board

#2146 is on Project #13 (enrolled by the owner 2026-10-21; card move pending the
GraphQL rate limit, per the lane's comment). No per-task issues.

**The PR closes #2146** — the owner's decision defines (c) as its resolution and
leaves the watchdog as a future option, not as this issue's remainder. Use a
closing keyword (`Closes #2146`) and check the state after merge.

---

## Dependencies and parallelism

```
T001 ── T002 ── T003 ── T004 ── T005 ── T006
                          └──── T007 ── T008      (US3, P2, deferrable)
```

- **T001 is foundational** — the shape the reds compile against.
- **Nothing is `[P]`.** Every task but T008 touches `AspireFixture.cs` or
  `AspireFixtureReportSelectionTests.cs`; ADR-0109 parallelism needs disjoint
  files, and there are none. No `Shared.Kernel` / `Shared.Contracts` / AppHost /
  Aspire-resource work exists to fan out. **One engineer, one file, by
  construction** — say so in the PR.
- **US3 (T007–T008) is deferrable** if assumption A2 fails (spec §8); US1/US2
  ship without it.

---

## Phase 4a-0 — the seam (infra-engineer, characterisation green)

### T001 [US1] Add the health parameter, accepted and ignored

**File**: `tests/Integration.Tests/Fixtures/AspireFixture.cs`

1. Run `dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release --filter "Category=FixtureLogic"`
   and capture the summary **verbatim** (characterisation, before).
2. Add `internal sealed record ResourceHealth(HealthStatus? Status, ImmutableArray<HealthReportSnapshot> Reports);`
3. Add the trailing optional parameter
   `IReadOnlyDictionary<string, ResourceHealth>? health = null` to
   `FormatResourceStates`, `FormatLikelyCause` and `FormatTimeoutMessage`;
   thread it from `FormatTimeoutMessage` into the other two. **Do not read it.**
4. Re-run step 1. Same count, all green, **no test file edited** — capture it.

Commit: `refactor(2146): the timeout formatters accept a health map they do not yet read`.
Must build on its own.

---

## Phase 4a — the red (test-writer)

### T002 [US1] State-list facts, observed red

**File**: `tests/Integration.Tests/Fixtures/AspireFixtureReportSelectionTests.cs`
(existing class — **no new shard entry needed**)

Sentence-style names (ADR-0053), Shouldly (ADR-0052). Build health with
`new HealthReportSnapshot(name, status, description, exceptionText)` and
`ImmutableArray.Create(...)`. Facts, one per spec §3 US1 scenario:

- `A_running_resource_with_a_failing_health_check_shows_its_health_in_the_state_list`
  — `rabbitmq: Running (health Unhealthy)` and `Connection refused` both present.
- `A_healthy_running_resource_shows_its_health_and_no_report_lines`
- `Exit_code_and_health_share_one_parenthetical` — exactly
  `automation: Running (exit code 137, health Unhealthy)`.
- `A_health_check_that_has_not_run_is_shown_as_pending` — contains `pending`;
  the report line does not contain `Unhealthy`.
- `At_most_three_health_reports_are_listed_per_resource` — `(+2 more)` present,
  the fourth and fifth report names absent.
- `A_long_health_description_is_truncated` — the 2 000-character description
  does not appear whole; no line exceeds a stated bound.
- `The_timeout_message_carries_each_resources_health` — over
  `FormatTimeoutMessage`.

**Assertion hygiene** (memory: *an assertion must not check its own input*): each
asserted string must be one the formatter has to *produce* — e.g. assert
`"(health Unhealthy)"` with the parenthesis, never the bare `"Unhealthy"` that is
also the enum name in the input. Ask, for each: can the formatter be wrong while
this passes?

### T003 [US2] Cause-line facts, observed red

**File**: same class.

- `A_RuntimeUnhealthy_resource_is_named_as_the_likely_cause` — the 2026-09-07
  sighting, **two-argument call, no health**. Names `mediamtx`, not
  `stream-distribution`. Red on the merge base without T001.
- `A_running_resource_that_fails_its_health_check_is_named_as_the_likely_cause`
  — rabbitmq shape; names none of the nine `Waiting` services.
- `A_health_check_that_has_not_run_is_not_named_as_a_cause` — **expected green**
  on red-run (the arm does not exist yet); it is the guard for FR-005's pending
  rule, and must be **observed failing by counterfactual** in T004 (temporarily
  read the aggregate instead of report statuses) — record the observation.
- `Death_is_named_before_unhealth_and_nothing_twice` — migrations 134 + rabbitmq
  unhealthy; `IndexOf("migrations") < IndexOf("rabbitmq")`, each once.
- `The_unhealthy_sentence_does_not_claim_a_death` — does not contain `exited`
  or `died` for the unhealthy resource.

**Run** the trait, capture output **verbatim**, fix nothing:

```sh
cd D:/Github/sse-2146
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
  -c Release --filter "Category=FixtureLogic"
```

Expected: every new fact red **on an assertion**, except the declared-green
guard; the 23 pre-existing facts green. A new fact arriving green (other than
that guard) is a phase-4 failure.

---

## Phase 4b — implementation (infra-engineer)

### T004 [US1][US2] Render health and name the unhealthy resource

**File**: `tests/Integration.Tests/Fixtures/AspireFixture.cs`. Brief: T002/T003's
verbatim output. **Do not edit the tests.**

- `healthByResource` field; in `CaptureResourceStateMapAsync`, record
  `new ResourceHealth(evt.Snapshot.HealthStatus, evt.Snapshot.HealthReports)`
  when `HealthStatus` is non-null; pass it in the `catch`.
- `FormatResourceStates`: plan §3 rendering, two `private const` bounds.
- `FormatLikelyCause`: plan §4 third arm. `IsHealthy`, `FatalStartupStates`,
  `EndedStates`, `SelectResourcesToReport` and `ThrowIfAnyGatedResourceDiedAsync`
  **unchanged** (FR-007).
- Update `EndedStates`' doc paragraph that says "#2146 owns that case" so it
  says where the case is now handled — a stale pointer is the drift this file's
  comments keep correcting.
- Perform and record the T003 counterfactual.
- Re-run the trait: all green, nothing edited. Capture verbatim.
- `dotnet format --verify-no-changes` on the test project; Release build clean.

Commit: `test(2146): the startup-timeout report names an unhealthy resource` —
behaviour change in test infrastructure; Conventional Commits (ADR-0030), no
`Co-Authored-By` (ADR-0086). Split red facts and implementation into separate
commits only if each builds on its own (CLAUDE.md, stacked-commit rule).

### T005 Full test-project build and Architecture.Tests

`dotnet build -c Release` for the solution; run `tests/Architecture.Tests`
(`IntegrationTestSelectionTests` guards trait/filter wiring). No Docker needed.

### T006 Phase 5 note — what was and was not observed

Write the verification note for the PR: the FixtureLogic output, the T003
counterfactual, and **explicitly** that no booted stack was observed (Docker
down; and a healthy boot never reaches this path). If Docker is available by
phase 5, an optional provocation: `docker pause` the `rabbitmq` container after
`StartAsync` on a throwaway run with `StartupTimeout` lowered **locally only**,
read the real message, then revert — never committed.

---

## US3 — capture test (P2, deferrable)

### T007 [US3] Red: the capture records health from an in-process service

**Seam first** (infra-engineer, behaviour-preserving, part of this task): extract
the capture into `internal static Task<…> CaptureResourceStatesAsync(ResourceNotificationService, TimeSpan)`
per plan §5; the instance method delegates; the FixtureLogic suite unmodified
and green before/after (characterise as T001).

**Then the red** (test-writer), new file
`tests/Integration.Tests/Fixtures/AspireFixtureResourceCaptureTests.cs`,
`[Trait("Category", "FixtureLogic")]`: construct
`new ResourceNotificationService(NullLogger<ResourceNotificationService>.Instance, new FakeHostApplicationLifetime())`,
publish `rabbitmq` Running with an `Unhealthy` report and `camera-catalog`
`Waiting`, call the static capture with a ≤ 500 ms window, assert rabbitmq's
captured health and no entry for camera-catalog. Red if the seam returns no
health.

**If A2 fails** (no replay, constructor throws, `PublishUpdateAsync` needs
DCP-only state): stop, revert the extraction, record the finding in the PR and
spec §8, ship US1/US2.

### T008 [US3] Shard-filter entry for the new class

**File**: one of `tests/Integration.Tests/ci-shards/shard-{1..4}.filter` — append
`|FullyQualifiedName~SmartSentinelEye.Integration.Tests.Fixtures.AspireFixtureResourceCaptureTests.`
to the lightest shard. The `integration-shard-coverage` job fails
deterministically without it.

---

## Phase 7 follow-up

File one issue (not part of this PR): *the three 60-attempt boot probes throw
`TimeoutException`, which the timeout catch does not match, so they produce no
resource snapshot* (spec §6). Reference #2146; do not label `agent:ready`.
