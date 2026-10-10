# Plan 331 — The probe that reports nothing changed

**Spec:** [spec.md](spec.md) · **Issue:** #2672 · **Phase:** 2 (Plan)

## 1. Context, boundaries, ADR position

**Bounded context:** StreamDistribution only. Two layers touched:

| Layer | File | Change |
|---|---|---|
| Infrastructure | `src/StreamDistribution/Infrastructure/StreamDistributionInfrastructureModule.cs` | extract the gateway registration into an `internal` seam (§3.1); register the Polly severity override with it (§3.3) |
| Api (host config) | `src/StreamDistribution/Api/appsettings.json` | one `Logging:LogLevel` category override (§3.2) |

**Not touched:** Domain, Application, `Shared.Kernel`, `Shared.Contracts`, `ServiceDefaults`,
`AppHost`, any other context. No entity, value object, invariant, domain event or integration
event changes — §II (primitives on domain models) has nothing to bite on, and there is no
messaging section to write: `StreamHealthChangedDomainEvent` → `StreamHealthChangedV1` is
unchanged and is the record of a health transition before and after. No cross-context
reference is introduced; NetArchTest rules are unaffected.

**ADR position — no new ADR.**

- The *policy* (routine probe outcomes below `Information`, transitions at `Information`+) is a
  human decision recorded on #2672 (2026-10-08). The lane implements decisions; it does not make
  them, and this one is made.
- The *mechanism* is already decided: ADR-0050 (MEL + OTLP; levels are MEL levels, filtered by
  MEL's `Logging:LogLevel` rules). A per-category override in a service's `appsettings.json`
  has precedent in every service — `Microsoft.EntityFrameworkCore.Database.Command: Warning`
  (spec 081, spec 127 / #2133, guarded by `DatabaseCommandLogLevelTests`).
- The Polly override is configuration of the standard resilience handler ADR-0143 already
  installs, keyed by the same `{client}-standard` pipeline name `RetryEveryMethod`
  (`src/ServiceDefaults/Resilience/IdempotentRetry.cs:92-95`) already uses. It adds no
  dependency (`Polly.Extensions` arrives transitively through `Microsoft.Extensions.Http.Resilience`,
  which `ServiceDefaults` already references) and no new pattern.
- ADR-0118 is cited, not amended: it fixes *one* sink; this spec keeps that sink readable, in
  the reading spec 208 FR-009 already gave it.

**If phase 4 finds the Polly override cannot be expressed as §3.3 describes** (e.g. the
`TelemetryOptions` instance is not honoured by the standard handler's pipeline at runtime, despite
A1), the fallback is **not** `"Polly": "Warning"` (rejected, §2). Stop and report; a different
mechanism for Polly is a fresh decision.

## 2. Mechanism — chosen and rejected

| # | Option | Verdict | Why |
|---|---|---|---|
| H1 | `"System.Net.Http.HttpClient.IRtspGateway": "Warning"` in `appsettings.json` | **Chosen (HttpClient half)** | The handlers write nothing at `Warning`+ (pinned `Microsoft.Extensions.Http` 10.0.12, `Logging/LogHelper.cs`), so the filter removes exactly the routine records and nothing else. Scoped to the one client. Overridable by environment variable — the operator's escape hatch (AS-5). Same shape as the EF precedent. |
| H2 | Code-level `builder.Logging.AddFilter("System.Net.Http.HttpClient.IRtspGateway", Warning)` | Rejected | `AddFilter` registers a filter rule after the host's configuration binding; at equal specificity the later rule wins, so an environment override could not turn the records back on. Fails AS-5. |
| H3 | `IHttpClientBuilder.RemoveAllLoggers()` on the gateway client | Rejected | Removes the logging handlers outright; nothing can turn them back on without a rebuild. Fails AS-5. |
| P1 | `TelemetryOptions.SeverityProvider` lowering **unhandled `ExecutionAttempt` on pipeline `IRtspGateway-standard`** to `Debug` | **Chosen (Polly half)** | Moves exactly row 4 of the spec's table. Handled attempts (`Warning`), final failures (`Error`), `OnRetry`, `OnTimeout` and every circuit event keep their default severity. Other pipelines untouched (AS-4). `Logging:LogLevel:Polly=Debug` brings it back (AS-5). |
| P2 | `"Polly": "Warning"` in `appsettings.json` | Rejected | Silences `OnCircuitClosed`, which Polly writes at `Information` — the recovery transition the decision says to keep. Also silences the other two StreamDistribution clients' attempts, which nobody decided. |
| P3 | A `SeverityProvider` in `ServiceDefaults` downgrading every unhandled attempt in every service | Rejected | Correct in shape, wider than the decision: it changes all ten services' logs. A candidate for a follow-up decision, not for this slice. |
| X1 | A separate named client just for the probe, so provisioning keeps its transport log | Rejected | Speculative. Provisioning, repoint and retire already write their own `Information` records (`Infrastructure/Log.cs:34-43`); the transport records beside them add nothing an operator reads. |
| X2 | Quieten these categories in the test lane only | Rejected | Already rejected by spec 291 §1.5: it makes a service log differently under test than in production. |

**Consequence of H1 recorded explicitly:** the filter is per *client*, not per *caller*. The
reconciler's start-up `list`, and provisioning's `add` / `patch` / `delete`, lose their four
transport records too. Each of those already has a domain-level `Information` record
(`Infrastructure/Log.cs:13-43`); failures still surface through Polly (`Warning`/`Error`) and
`EnsureSuccessStatusCode`.

## 3. Design

### 3.1 The seam (behaviour-preserving, its own commit)

Move `StreamDistributionInfrastructureModule.cs:88-108` — the comment block, the
`AddHttpClient<IRtspGateway, MediaMtxRtspGateway>` call and `.RetryEveryMethod()` — verbatim
into

```csharp
internal static IHttpClientBuilder AddMediaMtxGateway(this IServiceCollection services)
```

in the same file, and call it from where the lines stood. It returns the builder so the test can
attach its stub primary handler to the *real* registration. Nothing else moves. `internal` is
reachable from `StreamDistribution.Infrastructure.Tests` (`InternalsVisibleTo` already declared,
`SmartSentinelEye.StreamDistribution.Infrastructure.csproj:8-10`).

Why a seam rather than booting the whole module: `AddStreamDistributionInfrastructure` also
registers Npgsql/EF, Wolverine with RabbitMQ and three hosted services; a test that builds all of
that to ask about one HTTP client is slower and fails for reasons unrelated to logging. Why not
re-declare the registration in the test: a test that builds its own copy of the client checks
its own input (memory: *an assertion must not check its own input*).

### 3.2 HttpClient half — one line of configuration

`src/StreamDistribution/Api/appsettings.json`, under `Logging:LogLevel`:

```json
"System.Net.Http.HttpClient.IRtspGateway": "Warning"
```

Base file only. `appsettings.Development.json` re-declares three keys but not this one, and
configuration merges by key, so Development inherits it; AS-6 proves the layered result rather
than trusting the merge rule. MEL resolves `…IRtspGateway.LogicalHandler` and
`…IRtspGateway.ClientHandler` by longest prefix, so one key covers both.

### 3.3 Polly half — a severity override registered with the client

Inside `AddMediaMtxGateway`, after `.RetryEveryMethod()`:

- derive the pipeline name as `$"{builder.Name}-standard"` — the spelling `RetryEveryMethod`
  uses, from the builder rather than a literal client name;
- `services.Configure<TelemetryOptions>(…)` that **captures any existing
  `options.SeverityProvider` and composes with it** (A1: one shared instance per container);
- the override returns `ResilienceEventSeverity.Debug` when **all three** hold —
  `arguments.Source.PipelineName == pipeline`,
  `arguments.Event.EventName == "ExecutionAttempt"`,
  `arguments.Event.Severity == ResilienceEventSeverity.Information` (i.e. unhandled; Polly
  assigns `Warning`/`Error` to handled attempts, `TelemetryUtil.cs:17,22`) — and otherwise
  defers to the captured provider, or to `arguments.Event.Severity` when there is none.

`"ExecutionAttempt"` has to be spelled: Polly's constant (`TelemetryUtil.ExecutionAttempt`) is
`internal`. AS-1 runs the real pipeline, so a rename in a Polly bump fails the build's tests
rather than silently restoring the flood.

The registration and its *why* comment stay at the call site, as `RetryEveryMethod`'s do
(ADR-0143's convention: say why where the client is configured). No `[LoggerMessage]` changes;
no new `Log.cs` entries.

### 3.4 What an operator sees afterwards

| Situation | Before (per stream, per 2 s sweep) | After |
|---|---|---|
| Healthy, `ready:false`, 404 | 5 × `Information` | nothing at `Information`+ |
| Transient MediaMTX error, recovered by retry | 5 × `Information` + `Warning` ×2 per retry | `Warning` ×2 per retry (+ 4 transport records gone) |
| MediaMTX down | retries `Warning`, final `Error`, `HealthProbeFailed` `Warning`, transport `Information` | same minus the transport `Information` — **out of scope** volume (spec, Out of scope) |
| Circuit opens / half-opens / closes | `Error` / `Warning` / `Information` | unchanged |
| Health actually changes | `StreamHealthChangedV1` + audit row | unchanged |

## 4. Boundary and convention checks for the engineer

- No `ArgumentNullException.ThrowIfNull`; if the seam guards, `Ensure.That(services).IsNotNull()`
  (ADR-0105). Private fields without underscore; `var` optional; collection expressions where a
  collection is declared.
- No new `PackageReference`. If `Polly.Telemetry` types are not visible from the Infrastructure
  project transitively, stop and report rather than adding a package — that would change the
  ADR position in §1.
- Contention files (ADR-0109): `StreamDistributionInfrastructureModule.cs` and
  `src/StreamDistribution/Api/appsettings.json`. Both are single-owner for this slice.

## 5. Tests — what is red first and what is preserved

**One new class:** `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxProbeLogVolumeTests.cs`.
Unit-test project, so no `ci-shards/shard-N.filter` entry (those govern `Integration.Tests`
only); it runs under `coverage-check.ps1` with every other unit project.

### 5.1 Harness — the real things, bound the way the host binds them

1. `Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath =
   <repo>/src/StreamDistribution/Api, EnvironmentName = <"Production" | "Development"> })` —
   loads the **shipped** `appsettings.json` (+ `appsettings.{env}.json`) and wires
   `Logging:LogLevel` exactly as the service does. Repository root found by walking up to
   `SmartSentinelEye.slnx`, as `DatabaseCommandLogLevelTests` does; paths slash-normalised in any
   message (memory: *source-scanning tests need slash normalising*).
2. AS-5 only: `builder.Configuration.AddInMemoryCollection(…)` **after** the defaults — the
   precedence position environment variables occupy.
3. `builder.AddServiceDefaults()` — the real `AddStandardResilienceHandler(IdempotentRetry.RetryIdempotentMethodsOnly)`
   via `ConfigureHttpClientDefaults`, not a copy.
4. `builder.Logging.ClearProviders()` then a recording `ILoggerProvider` whose loggers answer
   `IsEnabled = true` and record `(Category, Level, EventId.Id, EventId.Name, the state's
   "EventName" value if present)`. The last is needed because Polly writes every non-attempt
   event (`OnRetry`, `OnCircuitClosed`, …) under one EventId — `0 "ResilienceEvent"` — and
   carries the event's name only as a structured property (`Polly.Extensions` 8.4.2,
   `Telemetry/Log.cs:210-216`). MEL applies the
   configured filter rules *before* a provider's logger is called, so the recorder sees exactly
   what the sink would (the `AlwaysEnabledProvider` principle in `DatabaseCommandLogLevelTests`).
   Thread-safe collection: Polly and the handlers may log from pool threads.
5. `builder.Services.Configure<MediaMtxOptions>(o => o.ManagementUrl = "http://mediamtx.test")`,
   then `builder.Services.AddMediaMtxGateway().ConfigurePrimaryHttpMessageHandler(() => stub)`.
   The stub is a hand-written `HttpMessageHandler` (ADR-0052: hand-written fakes) returning a
   configured status/body and counting requests. Service discovery is in the pipeline; if the
   pass-through resolver does not accept `mediamtx.test`, use a configured
   `services:mediamtx:api:0` entry instead — **test-writer verifies, does not assume**.
6. AS-3 only: zero the retry delay and jitter on the gateway's pipeline options
   (`Configure<HttpStandardResilienceOptions>($"{gatewayBuilder.Name}-standard", …)`), as
   `IdempotentRetryTests.Build` does for its client.
7. Probes call `IRtspGateway.GetPathHealthAsync(MediaMtxPath.From("cam-<guid>"), …)` — the one
   call `StreamHealthWatcher.DispatchAsync` makes per stream (`StreamHealthWatcher.cs:115`).

### 5.2 Facts

"Routine records" below = level ≥ `Information` with category starting
`System.Net.Http.HttpClient.IRtspGateway` or equal to `Polly`.

| # | Fact (sentence-style, ADR-0053) | AS | Before fix | After fix |
|---|---|---|---|---|
| 1 | `A_fab_scale_sweep_of_healthy_streams_writes_no_information_record` — `[Theory]` over `Production`, `Development`; 250 probes; asserts routine records = 0, stub saw 250 requests, every result `IsReady` | AS-1, AS-6 | **red**: 1 250 (5/probe) | green |
| 2 | `A_probe_answered_not_ready_or_not_found_writes_no_information_record` — `[Theory]` over `200 {"ready":false}` and `404`; asserts routine = 0 and `IsReady == false` | AS-2 | **red**: 5/probe | green |
| 3 | `A_probe_that_needs_retries_still_logs_them_at_warning_and_its_failure_at_error` — stub 503 always; asserts ≥1 `Polly` `ExecutionAttempt` at `Warning`, ≥1 `OnRetry` at `Warning`, ≥1 `ExecutionAttempt` at `Error`, and `HttpRequestException` thrown | AS-3 | green | green, **unmodified** |
| 4 | `Another_client_in_the_same_service_still_logs_its_exchanges_at_information` — a second named client (`AddHttpClient("unrelated")` + stub 200) in the same container; asserts its 4 handler records and its `Polly` `ExecutionAttempt` at `Information` are recorded | AS-4 | green | green, **unmodified** |
| 5 | `An_environment_override_brings_the_probe_exchange_back_without_a_rebuild` — overrides `…IRtspGateway=Information`, `Polly=Debug`; one healthy probe; asserts the 4 handler records at `Information` and `ExecutionAttempt` at `Debug` (not `Information`) | AS-5 | **red** (attempt is at `Information`) | green |

**Assertion messages carry the measurement**, so a failure reads as the issue's figure, not a
bare count: `"{n} routine records over {probes} probes = {n/probes} per probe; at 250 streams and
a 2 s poll that is {n/probes*250/2} records/s"`. Fact 1's red output today should read
**1 250 … 5 per probe … 625 records/s** — that is the quoted phase-4a evidence.

**Non-vacuity, proved by counterfactual, not by comment** (memory: *prove a guard by
counterfactual*): facts 4 and 5 show the recorder *does* see these categories whenever the
configuration lets them through, so fact 1's zero cannot come from a deaf recorder; fact 1's
request count shows the probes traversed the pipeline. Fact 3 fails any implementation that
silences `Polly` wholesale; fact 4 fails one that silences `Polly` or
`System.Net.Http.HttpClient` process-wide; fact 5 fails H2/H3.

### 5.3 Colour (ADR-0139 / ADR-0144) — **red**, with characterisation companions

This is behaviour-changing. The service's log stream is an output of the system — the thing
ADR-0118's one sink receives and an operator reads — and this slice changes *which records it
contains*. A characterisation colour would require the covering tests to pass unmodified before
and after; the property being changed (five `Information` records per probe) is exactly what the
covering test asserts, so it cannot. That is the definition of new behaviour, not an ambiguity
resolved by default.

Facts 3 and 4 are the characterisation half: the behaviour that must **not** move (retries,
failures, other clients) is captured green before the fix and must pass unmodified after it.
Fact 5 is red because of its Polly half. Phase 4a's required outcome: **facts 1, 2, 5 red for
the reason in the table; facts 3, 4 green; no compile error.** Any other outcome — notably fact 1
red with a count other than 5 per probe — means the investigation in spec §Problem is wrong;
stop and report rather than adjust.

## 6. Sequencing — three commits, each building on its own (ADR-0087)

1. `refactor(stream-distribution): register the MediaMTX gateway through one seam` — §3.1 only.
   Characterisation: `StreamDistribution.Infrastructure.Tests`, `StreamDistribution.Application.Tests`
   and `Architecture.Tests` (notably `ResilienceRegistrationTests`) green before and after,
   unmodified. The diff is a move; review it as one.
2. `test(stream-distribution): prove a healthy MediaMTX probe writes five Information records` —
   §5, red as §5.3 requires. Leaves the class red until commit 3 (CLAUDE.md: "builds" is not
   "passes"; accepted).
3. `fix(stream-distribution): keep routine MediaMTX probe exchanges out of the Information log`
   — §3.2 + §3.3. Facts 1–5 green; 3 and 4 unmodified.

Conventional Commits, no `Co-Authored-By` (ADR-0030/0086).

## 7. Verification (phase 5)

On a running stack (spec §Independent end-to-end test procedure): stream-distribution's
structured-log records per category over 60 s, on `develop` and on this branch, run twice each,
with the stream count. Report records/s per stream and the extrapolation to 250 for both. Also
spec 291's own yardstick: the wall-clock span of `RecentLogs("stream-distribution", 400)`
— 46-65 ms at 40 streams before; after, it should be governed by non-probe traffic. Confirm a
provoked outage still yields a `StreamHealthChangedV1` audit row. Latency: N/A (no §IV leg).

## 8. Follow-ups to file at phase 7 (observed, not fixed here)

1. Outage-time volume: retries / final failure / `HealthProbeFailed` repeat per stream per sweep
   while MediaMTX is down — candidate for spec 208's once-per-transition discipline.
2. `BrokenCircuitException` / `TimeoutRejectedException` escape `DispatchAsync`'s
   `catch (HttpRequestException)` and abandon the rest of the sweep.
3. Whether every service's unhandled Polly attempts belong at `Debug` (P3) — a policy question
   for a human, not for the lane.
