# Spec 331 — The probe that reports nothing changed

**Issue:** #2672 — "StreamHealthWatcher's per-probe logging will flood the one log sink at fab
scale (~625 records/s at 250 cameras)"
**Branch:** `fix/2672-stream-watcher-log-verbosity` · **Worktree:** `D:\Github\sse-2672`
(cut from `origin/develop` at `540236cf`)
**Lane:** autonomous (ADR-0144). **The remedy was decided by a human** — issue comment,
2026-10-08: *"drop successful/expected probe outcomes below Information level in
StreamHealthWatcher's per-probe logging; reserve Information+ for actual state transitions."*
This spec locates the records that decision applies to and says how to move them; it does not
re-open the decision.
**Predecessor:** spec 291 (#2656, `specs/291-the-sweep-that-ate-the-sentinel/`) — measured the
sweep and recorded it in §1.5 as "observed, not fixed here".
**ADRs:** ADR-0050 (MEL + OTLP, no Serilog — the mechanism whose levels are tuned here), ADR-0118
(one telemetry sink per environment), ADR-0143 (the standard resilience handler every client
gets, and `RetryEveryMethod`'s `{client}-standard` pipeline naming this spec reuses), ADR-0036
(smallest change), ADR-0037 (phases), ADR-0052/0053 (xUnit + Shouldly, sentence names),
ADR-0103 (no Testcontainers), ADR-0109 (`[P]` = disjoint files), ADR-0139 (red first), ADR-0144
(lane limits, 4a colours).
**Constitution:** §VII (observability), §Testing (new behaviour starts red; preserved behaviour
stays green unmodified).
**New ADR needed:** no — see plan §1.
**Latency budget (§IV):** N/A. `StreamHealthWatcher` polls MediaMTX's control API on its own
2 s timer; no hop of the event → overlay path runs through it, and log-level filtering changes
no code path on any leg.

**Spec number.** 331. `origin/develop` tops out at 329
(`329-the-latch-three-files-still-shared`); 330 is claimed by the unmerged
`origin/fix/2286-mqtt-publish-scope-enforcement` (`330-the-scope-the-broker-never-read`).
Every remote branch and every worktree (`sse-2286`, `sse-2782`) was scanned for `specs/33x`;
none claims 331. **Re-check before opening the PR** (memory: *spec number: origin/develop isn't
enough*).

---

## Problem (re-verified at `540236cf`)

### Where the five records come from — none of them is the watcher's own

`StreamHealthWatcher` (`src/StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs`)
writes **no** record for a probe that succeeds or answers "not ready". Its own log statements
(`src/StreamDistribution/Infrastructure/Log.cs:52-59`) are one `Information` at start-up, one
`Error` per failed sweep, and one `Warning` per probe that throws `HttpRequestException`. The
five records spec 291 §1.2 measured per probe are all written by the framework underneath
`IRtspGateway.GetPathHealthAsync` (`MediaMtxRtspGateway.cs:117-145`, one `GET
/v3/paths/get/{path}`), through the typed client registered at
`StreamDistributionInfrastructureModule.cs:94-108`:

| # | Category | EventId | Level (pinned assembly) | Text |
|---|---|---|---|---|
| 1 | `System.Net.Http.HttpClient.IRtspGateway.LogicalHandler` | 100 `RequestPipelineStart` | Information | Start processing HTTP request GET … |
| 2 | `System.Net.Http.HttpClient.IRtspGateway.ClientHandler` | 100 `RequestStart` | Information | Sending HTTP request GET … |
| 3 | `System.Net.Http.HttpClient.IRtspGateway.ClientHandler` | 101 `RequestEnd` | Information | Received HTTP response headers after … ms - 200 |
| 4 | `Polly` | 3 `ExecutionAttempt` | Information when unhandled | Execution attempt. Source: 'IRtspGateway-standard//Standard-Retry' … Handled: 'False' |
| 5 | `System.Net.Http.HttpClient.IRtspGateway.LogicalHandler` | 101 `RequestPipelineEnd` | Information | End processing HTTP request after … ms - 200 |

Levels read off the pinned assemblies, not documentation:
`Microsoft.Extensions.Http` 10.0.12 (`Logging/LogHelper.cs` — every event in both handlers is
`Information` or `Trace`; **none is `Warning` or above**, including `RequestFailed` 104) and
`Polly.Core` 8.6.5 (`Telemetry/TelemetryUtil.cs:17,22` — `ExecutionAttempt` is `Information`
when unhandled, `Warning` when handled, `Error` when the final attempt is handled). Polly's
`PipelineExecuted` (EventId 2) is always written at `Debug` (`Polly.Extensions` 8.4.2,
`TelemetryListenerImpl.LogEvent`), which is why it is not in the table.

40 streams × 5 records × 2 console lines = 400 lines per sweep, matching spec 291's two CI dumps
exactly. At the 250-camera target: 250 × 5 / 2 s = **625 records/s**, every one of them saying
"nothing changed".

### What already carries the transitions

The decision reserves `Information`+ for state transitions. On this path those are carried by:

- **Retry and failure** — Polly `ExecutionAttempt` handled (`Warning`), `OnRetry` (`Warning`),
  final handled attempt (`Error`), `OnTimeout` (`Error`).
- **Circuit transitions** — `OnCircuitOpened` (`Error`), `OnCircuitHalfOpened` (`Warning`),
  **`OnCircuitClosed` (`Information`)**. The recovery is a transition logged at `Information`,
  which rules out silencing the whole `Polly` category (plan §2).
- **A probe that could not complete** — the watcher's own `HealthProbeFailed` (`Warning`).
- **A stream's health actually changing** — the aggregate's `StreamHealthChangedDomainEvent` →
  `StreamHealthChangedV1` → audit row (`StreamHealthChangedDomainEventHandler.cs`). Not a log
  record; unchanged by this spec.

None of the five records in the table is any of these: a 200 with `ready:false` and a 404 both
log as a routine `Information` exchange, so moving them loses no transition.

### The established pattern this mirrors

`src/StreamDistribution/Api/Program.cs:39-84` + `Api/Log.cs:9-16` (spec 208, FR-009): the
`whep-authorize` limiter logs **once per transition into the throttled state, never per
refusal**, with the comment *"ADR-0118 gives this service one OTLP sink, and a record per
refused request would flood it at exactly the moment an operator needs it readable."* The same
discipline is in `WhepAuthValidator` (realm unreachable: one `Warning` per outage, one
`Information` on recovery, `Infrastructure/Log.cs:64-67`). Routine repetition stays out of the
sink; transitions go in.

**On ADR-0118's wording.** ADR-0118 decides *one sink per environment* and that it must be
sufficient "for a human answering 'what happened'". It does not use the word "flooded"; that is
this repository's established reading of it (spec 208 FR-009, the comment above). This spec
relies on that reading, and cites it as a reading.

## Decision encoded by this spec

| Records | Today | After | Mechanism |
|---|---|---|---|
| `System.Net.Http.HttpClient.IRtspGateway.*` (rows 1, 2, 3, 5) | Information | not written (filtered at `Warning`) | `Logging:LogLevel` category override in `src/StreamDistribution/Api/appsettings.json` |
| `Polly` `ExecutionAttempt`, unhandled, pipeline `IRtspGateway-standard` (row 4) | Information | Debug | Polly `TelemetryOptions.SeverityProvider`, registered with the gateway client |
| Every other `Polly` event, every other client, every watcher record | unchanged | unchanged | — |

Two mechanisms because the two sources differ: the HTTP-client handlers write only routine
events (no `Warning`+ exists to keep), so a category filter is exact; Polly's category mixes
routine attempts with circuit transitions, so only a per-event severity override is exact.
Plan §2 records the rejected alternatives.

**Debuggability is kept, not removed.** An operator who needs the per-probe exchange back sets
`Logging__LogLevel__System.Net.Http.HttpClient.IRtspGateway=Information` and/or
`Logging__LogLevel__Polly=Debug` in the environment — no rebuild. AS-5 asserts this works, which
is also why the HTTP-client override lives in configuration and not in a code-level `AddFilter`
(a code filter registered after configuration wins over it, and would make the override
inert).

## Out of scope

- **Outage-time volume.** While MediaMTX is down every probe still writes its retries (`Warning`),
  its final attempt (`Error`) and `HealthProbeFailed` (`Warning`) — per stream, per sweep. That
  is transition-level information repeated every 2 s, the same shape spec 208 collapsed to
  once-per-transition, but the decision on #2672 covers successful/expected outcomes only.
  Recommend a follow-up issue at phase 7.
- **A non-`HttpRequestException` probe failure** (`BrokenCircuitException`,
  `TimeoutRejectedException`) escapes `DispatchAsync`'s `catch` (`StreamHealthWatcher.cs:117`)
  and abandons the rest of the sweep. Observed while reading; not a verbosity question. Recommend
  a follow-up issue at phase 7.
- **Adding an `Information` record for a health transition.** None exists today, none of the
  five records carried one, and the transition is already recorded as an integration event and
  an audit row. Adding one is new behaviour the decision did not ask for.
- The other two StreamDistribution clients (`CameraCatalogTokenProvider`,
  `ICameraFabLookup`) and every other service's clients. Called once per start-up or per
  attribution pass, not per 2 s sweep; the same transport chatter there is not a flood.
- Changing `RecentLogs`' 400-line ring or spec 291's `CaptureLogs` — the test fixture is fixed;
  this spec removes the production cause it worked around.

---

## User stories

### US1 (P1, the whole slice) — a sweep that changed nothing writes nothing at Information

**As** whoever reads stream-distribution's log on the one sink (an operator on the Aspire
dashboard today, a fab operator on the production sink later),
**I want** a health sweep in which every stream answered as expected to write no `Information`
record, while every retry, failure, circuit transition and probe failure still reaches the sink
at the level it does today,
**so that** the sink holds what happened rather than 625 records/s saying nothing did.

Independently shippable: one settings file and one registration, observable end to end by
watching stream-distribution's log on a running stack.

## Acceptance scenarios

In-process facts run in `StreamDistribution.Infrastructure.Tests` against the real gateway
registration, the real `AddServiceDefaults` resilience handler and the **shipped**
`appsettings*.json` files bound as the host binds them (plan §5). No stack, no
Testcontainers (ADR-0103); the stub is the primary `HttpMessageHandler` only.

**AS-1 — a fab-scale sweep of healthy streams writes no Information record (happy path).**
```gherkin
Given stream-distribution's logging configured from its shipped appsettings.json
  And MediaMTX answering 200 {"ready": true} for every path
 When 250 health probes run (one sweep at the 250-camera target)
 Then no record at Information or above is written under
      "System.Net.Http.HttpClient.IRtspGateway" or "Polly"
  And the failure message reports records per probe and the projected records/s at a 2 s poll
```
Today: 1 250 records — 625 records/s. This is the measurement the issue cites, reproduced.

**AS-2 — the expected unhealthy answers are routine too (bad request from the camera's side).**
```gherkin
Given the same configuration
 When probes are answered 200 {"ready": false} and 404 (path not registered)
 Then no record at Information or above is written under those categories
  And GetPathHealthAsync still returns IsReady = false for both
```

**AS-3 — retries and failures still reach the sink at their own level (preserved).**
```gherkin
Given MediaMTX answering 503 to every attempt (retry delay zeroed for the test)
 When one probe runs
 Then Polly writes handled ExecutionAttempt and OnRetry records at Warning
  And the final attempt at Error
  And the probe throws HttpRequestException — the type DispatchAsync catches and logs as
      HealthProbeFailed at Warning (StreamHealthWatcher.cs:117-120)
```
Green today and after, unmodified — the guard against over-suppression.

**AS-4 — the override is scoped to the gateway, not the process (preserved).**
```gherkin
Given a second, unrelated client registered in the same container
 When it completes one successful request
 Then its own HttpClient handler records and its Polly ExecutionAttempt are still written
      at Information
```
Green today and after. Fails an implementation that silences `Polly` or
`System.Net.Http.HttpClient` wholesale.

**AS-5 — an operator can turn the probe exchange back on without a rebuild (preserved).**
```gherkin
Given the shipped configuration plus an environment-level override
      "Logging:LogLevel:System.Net.Http.HttpClient.IRtspGateway" = "Information"
      and "Logging:LogLevel:Polly" = "Debug"
 When one healthy probe runs
 Then the four HttpClient records are written at Information
  And the Polly ExecutionAttempt is written at Debug
```
The HttpClient half is green today; the Debug half is red today (the attempt is at Information).

**AS-6 — the Development file does not undo it.**
```gherkin
Given appsettings.json with appsettings.Development.json layered over it (what Aspire runs)
 When AS-1 runs
 Then it holds identically
```

**Auth / conflict.** No endpoint, aggregate, scope, `If-Match` or `Idempotency-Key` is touched;
there is no authorization or write-conflict surface. The MediaMTX control API the probe calls is
unauthenticated on the internal network today and stays so.

## Independent end-to-end test procedure

1. Stop any running stack (memory: *one machine, one Aspire stack*). Boot the AppHost from
   `develop`, let the Scenario Simulator provision its cameras, then read stream-distribution's
   structured logs for 60 s (Aspire dashboard or `list_structured_logs`): count records per
   category; record N streams and records/s. Expect ≈ 2.5 records/s per stream.
2. Boot from this branch (same volumes) and repeat. Expect zero records under
   `System.Net.Http.HttpClient.IRtspGateway.*` and zero `Polly` `ExecutionAttempt` at
   `Information`.
3. Patch one MediaMTX path to an unreachable source (memory: *provoking a stream outage*) and
   confirm the stream's health change still arrives as a `StreamHealthChangedV1` audit row.
4. Run each measurement twice (memory: *measurement runs need repeating*). Extrapolate both to
   250 streams and quote all four figures in the verification note.

## Assumptions (marked, to be checked at phase 4/5)

- **A1 — VERIFIED (decompiled, `Polly.Extensions` 8.4.2 `PollyServiceCollectionExtensions.cs:221-230`):**
  `TelemetryOptions` is a single unnamed options instance shared by every pipeline in the
  container, so the severity override must select on `SeverityProviderArguments.Source.PipelineName`
  and must **compose** with any provider already configured (none is, at `540236cf`).
- **A2 — VERIFIED (decompiled, `Microsoft.Extensions.Http.Resilience` 10.10.0,
  `PipelineNameHelper.GetName`):** the standard handler's pipeline is named
  `"{clientName}-standard"`; `RetryEveryMethod` (`IdempotentRetry.cs:92-95`) already relies on
  the same spelling. AS-1 fails if either drifts, because it runs the real pipeline.
- **A3 — UNVERIFIED:** that a typed `AddHttpClient<IRtspGateway, MediaMtxRtspGateway>` is
  named `IRtspGateway`. Spec 291's CI dumps show the category
  `System.Net.Http.HttpClient.IRtspGateway.*`, so it is true at runtime; AS-1 checks it
  directly.
- **A4:** the Polly change affects the metric tag `event.severity` on the
  `resilience.polly.strategy.attempt.duration` instrument as well as the log level
  (`TelemetryListenerImpl.Write` feeds one severity to both). No meter named `Polly` is exported
  (`ServiceDefaults/Extensions.cs` `AddMeter` list), so nothing observable changes; recorded so a
  later spec that exports it is not surprised.
