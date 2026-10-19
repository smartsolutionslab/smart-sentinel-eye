# Spec 420: a health sweep that keeps failing the same way warns once, then at most every ten minutes

**Feature Branch**: `fix/3013-throttle-health-sweep-failed` (cut from `origin/develop` at `28bcec96`)

**Created**: 2026-10-19

**Status**: Specified, planned and tasked. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked **[ASSUMPTION]** in §7.

**Input**: Issue [#3013](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3013), the
last open item, under the owner's decision of 2026-10-19 recorded on the issue:

> throttle HealthSweepFailed like the source repair, reusing the spec 409 WarningRepeatGate pattern
> (10-minute repeat, Debug in between, re-armed by a successful sweep); no new number. This is a
> second slice after US1 (own spec). The page byte limit stays undecided and out.

and the later comment the same day: "Page byte limit: no limit; recorded and closed as a decision
(the 20-page cap and 5 s deadline bound the read). Remaining for this issue: the HealthSweepFailed
throttle slice (decided earlier), then it can close."

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (log volume and level). Phase 4a is **red**
(ADR-0139, constitution §Testing). The lane implements this decision; it makes none. The only
number is spec 409's 10-minute repeat, already a constant inside `WarningRepeatGate`. No ADR, no
configuration knob.

**Spec number.** 420. Checked 2026-10-19 after `git fetch`: `origin/develop` tops out at 418; 419
is held by open PR #3065 (`refactor/3013-remove-unused-path-health-probe`); no remote head holds
`specs/420-*`; the only other open PR (#2811, devcontainer) holds no spec.

**ADRs.** ADR-0144 / ADR-0139 (lane, red colour). ADR-0050 (`[LoggerMessage]` source-gen; one new
event). ADR-0118 (one telemetry sink per environment, so this Warning's volume is what an operator
reads). ADR-0171 (why the sweep reads every two seconds through the shared authorize budget, and so
why a control-API outage repeats the Warning at that rate). ADR-0052 / ADR-0053 / ADR-0054 / ADR-0103
(xUnit + Shouldly, sentence-style names, hand-written fakes, no Testcontainers). ADR-0084 (advisory
metrics; the watcher's constructor reaches 4 parameters, the limit, not past it). ADR-0105
(`Ensure.That` guards; none added). Builds on spec 388 (the throttle rule), spec 409 (the
`WarningRepeatGate` class and the `MediaMtxSourceRepairSweep` use of it this spec copies), spec 410
(the first-absence hold, which a failed sweep must keep leaving untouched), spec 415 (no remote text
in a log) and spec 418 (the 5 s deadline that is now one of the failure kinds).

---

## 1. Premise check (develop at `28bcec96`)

| # | Claim | Verified on develop | Holds? |
|---|---|---|---|
| P1 | `HealthSweepFailed` is a Warning on every failed tick, with no memory | `StreamHealthWatcher.DispatchAsync` (`src/StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs:110-121`) catches `HttpRequestException` from `GetAllPathsHealthAsync` and calls `logger.HealthSweepFailed(ex)` (`Log.cs:109-110`, Warning, carries the exception) unconditionally, then returns | **Yes** |
| P2 | The rate is one per tick | `ExecuteAsync` loops `PollOnceAsync` then `Task.Delay(2 s)` (`:28`, `:36-60`). A failed read costs at most 5 s (spec 418 deadline), so one Warning every 2-7 s: up to 43,200 a day during a control-API outage, ~12,300 a day when every read runs into the deadline | **Yes** |
| P3 | The pattern to reuse exists and is thread-safe | `WarningRepeatGate<TOutcome>` (`src/StreamDistribution/Infrastructure/WarningRepeatGate.cs`): first or changed outcome warns, same outcome repeats at most every 10 min on the monotonic timestamp, `Clear()` re-arms; locked. `MediaMtxSourceRepairSweep` (`Reconciler/MediaMtxSourceRepairSweep.cs:29, :67, :75-90`) holds one as a field of the hosted service, keyed `SweepFailure(Type, HttpStatusCode?, HttpRequestError?)`, clears it on a successful pass and logs `SourceRepairSweepStillFailing(type, status)` at Debug otherwise | **Yes** |
| P4 | A singleton is enough; no DI registration is needed for the gate | `StreamHealthWatcher` is registered once with `AddHostedService` (`StreamDistributionInfrastructureModule.cs:215`), a singleton; it already keeps cross-tick state as plain fields (`degradedSince`, `pathsPresentInLastSweep`) because ticks run strictly one after another. The gate's lock is therefore not needed for correctness here, but costs nothing | **Yes** |
| P5 | The watcher has no `TimeProvider` | Its constructor is `(IServiceScopeFactory, IClock, ILogger<StreamHealthWatcher>)`; `IClock` is wall-clock `UtcNow` only, the gate needs `TimeProvider.GetTimestamp`. `TimeProvider.System` is registered as a singleton in the composition (`StreamDistributionInfrastructureModule.cs:106`, `:167`) | **Yes**, a constructor parameter is needed (plan §2.1) |
| P6 | Which failures reach `HealthSweepFailed` | Only `HttpRequestException`: a non-success status (`EnsureSuccess`, `MediaMtxRtspGateway.PageReading.cs:69-81`, status set, `HttpRequestError.Unknown`), any malformed page (`InvalidPage`, `InvalidResponse`, no status), the page cap / unreadable `pageCount` / missing `items` (`MediaMtxRtspGateway.cs:227-249`, `InvalidResponse`), the 5 s deadline (`PageReading.cs:35-40`, `InvalidResponse`) and transport errors from `HttpClient` (`ConnectionError`, `NameResolutionError`, ...). Anything else escapes to `ExecuteAsync`'s `StreamHealthWatcherPollFailed` Error, which is out of scope | **Yes** |
| P7 | The Warning carries no remote text (spec 415) | Every message above is built by our code from a route, a page number and a status code; spec 409 FR-104 / spec 415 removed the reason phrase and the inner parse exception. The Warning keeps carrying the exception; the new Debug line carries only the exception type name and the status number | **Yes**, unchanged |
| P8 | Spec 410's hold is not affected | A failed read returns before `pathsPresentInLastSweep` is reassigned (`:123-124`) and dispatches nothing; the throttle changes only which log line that branch writes, not its control flow | **Yes**, kept (FR-006) |
| P9 | PR #3065 (spec 419, unmerged) overlaps | It touches `IRtspGateway.cs`, `MediaMtxRtspGateway.cs` and the three existing watcher test files (`StreamHealthWatcherAbsenceHold*Tests.cs`, `StreamHealthWatcherScopeTests.cs`), not `StreamHealthWatcher.cs` or `Log.cs`. This spec touches none of its files (plan §5) | **Rebases cleanly either way** |

**Verdict: the premise holds on develop.** One user story, behaviour-changing, **phase 4a RED**.

## 2. User story

### US1 (P1): a health sweep that keeps failing the same way warns once, then at most every 10 minutes

When MediaMTX's control API is down, answering 503, or answering pages the sweep cannot read, the
health sweep fails every tick. Today each failed tick is a Warning with a stack trace. After this
change, the first failure of a kind is that same Warning; while the same kind persists, each further
failed tick is a Debug line naming the kind, and the Warning repeats once every 10 minutes. A
different kind warns at once. A tick whose read succeeds re-arms the Warning, so the next failure,
however soon, warns again.

**Why P1**: it is the last open item of #3013 and the only one decided.

**Independent test**: on `28bcec96`, run the new `StreamHealthWatcherSweepFailureThrottleTests` and
see every red fact fail with one Warning per failed tick (counts in tasks.md). Apply the change, run
the same file plus every existing `HealthWatcher`, `WarningRepeatGateTests` and
`MediaMtxSourceRepairSweep*` test unmodified, and see all green.

## 3. Acceptance scenarios

```gherkin
Feature: the health sweep's failure Warning is throttled per failure kind

  Background:
    Given the stream health watcher with two Healthy streams
    And MediaMTX's path list is read through the real gateway

  Scenario: happy path, a persistent failure warns once and then logs Debug
    Given MediaMTX answers every path-list request with 503
    When three health ticks run within ten minutes
    Then exactly one Warning "MediaMTX health sweep failed; skipping this tick for every stream." is logged
    And it carries an HttpRequestException with status 503
    And two Debug lines name "HttpRequestException" and "503" and say the Warning repeats at most every 10 minutes
    And no Debug line carries an exception
    And no camera is dispatched

  Scenario: happy path, the same failure warns again once ten minutes have passed
    Given MediaMTX answers every path-list request with 503
    When a tick runs, then another 9 min 59 s later, then another 1 s after that
    Then two Warnings are logged, on the first and the third tick
    And one Debug line is logged, on the second

  Scenario: conflict, a different failure kind warns at once
    Given MediaMTX answers 503, then 503, then 500, then a page that is not a JSON object
    When four ticks run within ten minutes
    Then three Warnings are logged: status 503, status 500, and an invalid response without status
    And one Debug line is logged, on the second tick

  Scenario: conflict, a successful sweep re-arms the Warning
    Given MediaMTX answers 503, then 503, then a valid page, then 503
    When four ticks run within ten minutes
    Then two Warnings are logged, on the first and the fourth tick
    And one Debug line is logged, on the second tick
    And the third tick dispatches both cameras

  Scenario: bad request, a single failed tick is unchanged
    Given MediaMTX answers one malformed page
    When one tick runs
    Then exactly one Warning with the HttpRequestException (InvalidResponse) is logged and no camera is dispatched
    # pinned today by StreamHealthWatcherCutShortReadTests, which stays unmodified

  Scenario: auth, a refused control-API call is a failure kind like any other
    Given MediaMTX answers every path-list request with 401
    When two ticks run within ten minutes
    Then one Warning with status 401 and one Debug line naming "401" are logged
```

The auth scenario is covered by the same mechanism as 503 (the kind is the status); it is listed so
the reader does not have to infer it, and it is asserted by the 401 case of the first red fact's
theory (tasks.md T001).

## 4. Requirements

- **FR-001** The first `HttpRequestException` from the health read after start, or after a
  successful read, logs `HealthSweepFailed` exactly as today: Warning, same message, same exception.
- **FR-002** A failure of the **same kind** as the last warned one, less than 10 minutes (monotonic,
  `TimeProvider.GetElapsedTime`) after that Warning, logs a new Debug event
  `HealthSweepStillFailing(exceptionType, status)` instead. It carries no exception and no message
  text from the failure.
- **FR-003** The kind is `(exception runtime type, HttpRequestException.StatusCode, HttpRequestException.HttpRequestError)`,
  the same triple `MediaMtxSourceRepairSweep.SweepFailure` uses. A different kind warns at once.
- **FR-004** A failure of the same kind 10 minutes or more after the last Warning warns again
  (and restarts the 10 minutes).
- **FR-005** A tick whose `GetAllPathsHealthAsync` returns clears the gate (re-arms the Warning)
  before anything is dispatched.
- **FR-006** A failed tick's control flow is unchanged: it dispatches nothing, opens no per-camera
  scope, and leaves `pathsPresentInLastSweep` and `degradedSince` untouched (spec 410).
- **FR-007** The throttle state lives in the watcher instance (a singleton hosted service); no new
  DI registration other than the `TimeProvider` the composition already registers.
- **FR-008** No other log changes: `StreamHealthWatcherPollFailed`, the gateway's Polly attempt
  records, the source repair's events and #3064's deadline-vs-final-attempt question are untouched.

## 5. Success criteria

- **SC-1** Every red fact in tasks.md T001-T004 is observed failing on `28bcec96` with one Warning
  per failed tick, and passing after the change.
- **SC-2** `StreamHealthWatcherCutShortReadTests`, `StreamHealthWatcherAbsenceHoldTests`,
  `StreamHealthWatcherAbsenceHoldInvariantTests`, `StreamHealthWatcherScopeTests`,
  `WarningRepeatGateTests`, `MediaMtxSourceRepairSweepTests` and
  `MediaMtxSourceRepairSweepAdversarialTests` pass unmodified.
- **SC-3** A persistent failure of one kind produces at most 144 `HealthSweepFailed` Warnings a day
  (one per 10 minutes), down from up to 43,200.
- **SC-4** `StreamDistributionRegistrationCharacterisationTests` passes unmodified (the watcher is
  still one singleton `IHostedService`, resolvable from the composed container).

## 6. Latency budget

**N/A.** The health watcher is not on the event-to-overlay path (constitution §IV): it reads
MediaMTX path state every 2 s and reports stream health; the change alters only which log line a
failed tick writes, adds no I/O and no wait.

## 7. Assumptions and judgement calls

- **[ASSUMPTION] A1, the kind triple.** The owner said "like the source repair". The source repair's
  sweep keys on `(Type, StatusCode, HttpRequestError)`; this spec uses exactly that. Consequence:
  every `InvalidResponse` without a status (malformed page, page cap, unreadable `pageCount`,
  missing `items`, the 5 s deadline) is **one kind**, so a switch between them does not re-warn
  within 10 minutes. Telling them apart would need a key derived from the message or a new typed
  reason, which is a new design, not the decided reuse.
- **[ASSUMPTION] A2, Debug carries the kind only.** Mirrors `SourceRepairSweepStillFailing`: type
  name and status number (`-` when absent), no exception. The Warning keeps the exception.
- **[ASSUMPTION] A3, `TimeProvider` as a fourth constructor parameter**, with the current
  three-parameter constructor kept and chained to `TimeProvider.System`, the shape
  `MediaMtxRtspGateway` already uses (`MediaMtxRtspGateway.cs:30-50`). This keeps the three existing
  watcher test files (which PR #3065 edits) compiling without an edit, and lets the red tests
  construct the watcher through `ActivatorUtilities` so they compile, and fail for the behavioural
  reason, on unchanged code.
- **A4, residual (not decided, not touched).** Under #3064's 503 storm the read normally ends at the
  deadline (`InvalidResponse`, no status) every tick, a stable kind, so the throttle holds. A read
  that sometimes finishes its Polly attempts inside 5 s (a 503 with status) and sometimes not would
  alternate kinds and warn on each switch. Recorded for #3064's owner decision; this spec does not
  change the deadline or the retry schedule.

## 8. Out of scope

#3064 (deadline vs Polly's final Error record), any other log event, the page byte limit (decided:
no limit), `StreamHealthWatcherPollFailed`, `GetPathHealthAsync` (spec 419, PR #3065), spec 002
FR-008, any configuration option for the 10 minutes, a shared failure-kind type between the two
sweeps (plan §2.3).
