# Tasks 331 — The probe that reports nothing changed

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2672 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: the service's log stream stops carrying five
`Information` records per probe). Plan §5.3 justifies it; facts 3 and 4 are characterisation
companions — green before and after, **unmodified**.
**Engineer**: `backend-engineer` (seam refactor T001, fix T004). `test-writer` for 4a (T002–T003).
**Reviewer**: `backend-reviewer`. No security surface (no endpoint, scope, token or trust
boundary touched) — `/security-review` not required.
**Tracking**: feature-level issue #2672 (Project #13). No per-task issues.
**New ADR**: none (plan §1).
**Latency**: N/A (no §IV leg).

Format: `[ID] [P?] [Story] description`. No task needs a running stack except T006 (phase 5).

## Phase 4 prep — the seam (backend-engineer; characterisation)

- [ ] **T001 [US1]** In `src/StreamDistribution/Infrastructure/StreamDistributionInfrastructureModule.cs`,
  move lines 88-108 (comment, `AddHttpClient<IRtspGateway, MediaMtxRtspGateway>(…)`,
  `.RetryEveryMethod()`) verbatim into
  `internal static IHttpClientBuilder AddMediaMtxGateway(this IServiceCollection services)`
  returning the builder, and call it from the original position (plan §3.1). **Nothing else.**
  Before and after, unmodified and green:
  `dotnet test tests/StreamDistribution.Infrastructure.Tests`,
  `dotnet test tests/StreamDistribution.Application.Tests`,
  `dotnet test tests/Architecture.Tests`. Quote both runs' summaries.
  Commit `refactor(stream-distribution): register the MediaMTX gateway through one seam`.

Depends: none. Blocks T002 (the tests call the seam; without it they fail to compile, which is
not a valid red).

## Phase 4a — red (test-writer; return verbatim output)

- [ ] **T002 [US1]** Create
  `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxProbeLogVolumeTests.cs` per
  plan §5.1 harness and §5.2 facts 1–5: shipped `src/StreamDistribution/Api/appsettings*.json`
  loaded through `Host.CreateApplicationBuilder` with `ContentRootPath` (repo root via
  `SmartSentinelEye.slnx`), real `AddServiceDefaults()`, `ClearProviders()` + a thread-safe
  recording provider (category, level, EventId, state `EventName`), `AddMediaMtxGateway()` +
  hand-written stub primary handler; AS-5 overrides via `AddInMemoryCollection` added last.
  Assertion messages carry per-probe count and projected records/s at 250 streams / 2 s.
  Verify — do not assume — that service discovery passes `http://mediamtx.test` through
  (plan §5.1 step 5). Hand-written fakes only (ADR-0052); sentence-style names (ADR-0053);
  slash-normalised paths in messages.
- [ ] **T003 [US1]** Run on unchanged production code (T001 applied, nothing else):
  `dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~MediaMtxProbeLogVolumeTests"`.
  **Required**: fact 1 red in **both** environments reporting **1 250 records, 5 per probe,
  625 records/s**; fact 2 red in both cases at 5 per probe; fact 5 red on the `Polly`
  `ExecutionAttempt` level (`Information`, expected `Debug`) with its HttpClient half passing;
  facts 3 and 4 green; no compile error. Any other outcome — in particular a per-probe count
  other than 5 — means spec §Problem is wrong: stop and report, do not adjust the tests.
  Commit `test(stream-distribution): prove a healthy MediaMTX probe writes five Information records`.

Depends: T001 → T002 → T003.

## Phase 4b — fix (backend-engineer; may not edit T002's file)

- [ ] **T004 [US1]** Two changes, one commit (plan §3.2, §3.3):
  1. `src/StreamDistribution/Api/appsettings.json` — add
     `"System.Net.Http.HttpClient.IRtspGateway": "Warning"` under `Logging:LogLevel`. Base file
     only; do not touch `appsettings.Development.json` (AS-6 proves inheritance).
  2. In `AddMediaMtxGateway`, after `.RetryEveryMethod()`: `services.Configure<TelemetryOptions>`
     installing a `SeverityProvider` that returns `ResilienceEventSeverity.Debug` iff
     `Source.PipelineName == $"{builder.Name}-standard"` **and** `Event.EventName == "ExecutionAttempt"`
     **and** `Event.Severity == ResilienceEventSeverity.Information`; otherwise defers to the
     previously configured provider, or the event's own severity. Composes — never replaces — an
     existing provider. One *why* comment at the call site, in the style of the
     `RetryEveryMethod` comment above it.
  No new `PackageReference`; if `Polly.Telemetry` is not reachable transitively, stop and report
  (plan §4). Then:
  `dotnet test tests/StreamDistribution.Infrastructure.Tests` (all green; facts 3 and 4
  byte-identical to T003's commit), `dotnet test tests/Architecture.Tests`
  (`DatabaseCommandLogLevelTests` still green — the file still silences EF), and
  `dotnet build SmartSentinelEye.slnx -c Release` (analyzers clean). `dotnet format --verify-no-changes`
  on the touched projects.
  Commit `fix(stream-distribution): keep routine MediaMTX probe exchanges out of the Information log`.

Depends: T003 → T004.

## Phase 5 — verify (`/verify`)

- [ ] **T005 [US1]** Re-check the spec number before the PR: `git ls-tree origin/develop specs/`
  plus every remote branch for `specs/331-*` (memory: *spec number: origin/develop isn't
  enough*). Rename the directory if 331 has been taken.
- [ ] **T006 [US1]** Live measurement per spec §Independent end-to-end test procedure and plan §7:
  one stack on the machine; stream-distribution structured-log records per category over 60 s
  on `develop` and on this branch, **twice each**, with the stream count; records/s per stream
  and the extrapolation to 250 for both; spec 291's 400-line-tail wall-clock span before/after;
  a provoked outage still producing a `StreamHealthChangedV1` audit row. Write every figure into
  `specs/331-the-probe-that-reports-nothing-changed/verification.md` as observed (memory:
  *self-review catches contradictions, never omissions*). Latency: N/A.

Depends: T004 → T005, T006 [P with T005 — disjoint files].

## Phase 6–7

- [ ] **T007** `/code-review` (backend-reviewer). Points to check: the `SeverityProvider` composes
  rather than overwrites; the pipeline name comes from `builder.Name`; fact 3/4 unmodified since
  T003; no `"Polly"` category override crept into any `appsettings*.json`.
- [ ] **T008** PR to `develop` (`--base develop`): quote T003's red output verbatim and T001's
  before/after characterisation summaries; file the three plan §8 follow-up issues and link them;
  `Closes #2672`.

## Parallelism

Essentially serial: one bounded context, two contention files
(`StreamDistributionInfrastructureModule.cs`, `src/StreamDistribution/Api/appsettings.json`) and
one new test file, with a strict seam → red → fix order. The only `[P]` pair is T005/T006.
Nothing here blocks or is blocked by another in-flight spec (330 / #2286 touches MQTT publish
scopes, not StreamDistribution).
