# Spec 388: The source repair logs a failing path on change, bounds its list read, and reports its own startup failure

**Feature Branch**: `fix/2994-source-repair-log-volume` (cut from `origin/develop` at `b932aa70`)

**Created**: 2026-10-16

**Status**: Ready for review. No `[NEEDS CLARIFICATION]` remains. The owner's decisions on #2994
settle all three items. Three judgement calls that those decisions presupposed but did not make are
marked **[ASSUMPTION]** in §5: when a path's state is dropped, which clock times the 10-minute
re-log, and whether the page cap is logged.

**Input**: Issue [#2994](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2994),
*Source repair: a refused PATCH Warns every interval, pageCount is unbounded, and a failed repair
list is logged as a failed startup pass*. These are the residuals that spec 378 (#2960) accepted in
writing (its plan §9 and §4).

**Owner decisions (2026-10-16, on #2994), recorded here and not re-opened:**

1. **Log volume.** A path's repair failure is logged at Warning **with the exception** on its first
   failure and whenever the failure's outcome changes (a different exception type or HTTP status).
   While the failure persists it is re-logged at most once every **10 minutes** per path. In
   between, nothing is logged at Warning; a Debug line is fine. A 404 from a retire race is logged
   at Debug as "path gone" and never as a Warning. There is no shared mutable static: the per-path
   state lives inside the repair pass object or the sweep, and it stays bounded because paths no
   longer in the database are pruned.
2. **`pageCount`.** The read stops on an empty page and reads at most **1,000** pages per list call.
   A non-integer or overflowing `pageCount` ends the read with one logged Warning for the pass,
   instead of a `FormatException` that fails the pass. One page enumerator may be shared between
   `ListConfiguredPathSourcesAsync`, `ListConfiguredPathsAsync` and `GetAllPathsHealthAsync`
   **only** if the two older readers' behaviour stays the same. Otherwise only the new reader is
   fixed and the rest is filed.
3. **Startup log pair.** When the repair's list GET fails inside `ReconcileOnceAsync`, the failure
   is caught and logged with its own message. It is no longer reported as
   `ReconcilerStartupPassFailed` after `ReconcilerStartupPassComplete`.

Log lines render sources only through `UrlRedaction.ToOrigin` (spec 373).

**Shared enumerator: not shared. The two older readers are filed as #3010.** No enumerator can bound
the new reader without also bounding the two older ones, and a bound changes their behaviour on
exactly the inputs this spec is about. One of those changes would be unsafe without a decision of
its own. `StreamHealthWatcher` reads a path missing from `GetAllPathsHealthAsync`'s result as "path
not registered" (`StreamHealthWatcher.cs:133-135`). A truncated health read would therefore show
live streams as degraded. A truncated repair read fails safe, because a path it does not see is not
repaired (spec 378 FR-001/FR-005). So the owner's condition fails, and only
`ListConfiguredPathSourcesAsync` changes. #3010 (tech-debt, on Project #13, not `agent:ready`)
carries the other two and the decision they need.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (fewer Warnings, a Debug line, a bounded
read, a new log message), so phase 4a is **red** (ADR-0139). An **adversarial pass is required**:
outage and flood. A **security review is required**: stream attribution and the control-API budget.

**Spec number.** 388. Checked 2026-10-16. `origin/develop`'s highest spec is 384. 385 is on
`fix/2991-repoint-ordered-by-changedat` and 386 is on `test/3001-crossfab-devices-webhook-403`, both
unmerged. 387 is held for #3006 (`fix/3006-detailchanged-ipv6-zone`,
`specs/387-detail-changed-unknown-for-an-ipv6-zone`). Re-checked just before the commit: no other
local or remote head and no open PR branch has a `specs/388-*` directory. Re-check before the PR is opened.

**ADRs.** ADR-0050 (logging: `[LoggerMessage]`, structured fields), ADR-0118 (one sink per
environment, which must stay readable during a flood; `LogThrottleTransition` cites it for the same
reason), ADR-0171 (every MediaMTX control-API call authorizes through `/streams/authorize` and spends
the shared `api` rate partition), ADR-0143 (`RetryEveryMethod()` on the MediaMTX gateway, which is
why a refused PATCH is not retried away), ADR-0052/ADR-0054 (hand-written fakes; there is no
`FakeTimeProvider` in this repository), ADR-0103 (integration through the Aspire fixture), ADR-0105
(`Ensure.That`), ADR-0139/ADR-0144 (red first; the lane implements and does not decide), ADR-0036
(smallest change). **No new ADR.** The owner made the decisions, and the mechanism is a private
detail of one infrastructure class.

**Latency budget.** N/A. This is control plane only: the background reconcile against MediaMTX's
config API and its logging. It is on none of constitution §IV's legs.

---

## 1. Premise, verified (not assumed)

Read on `origin/develop` at `b932aa70`. All four defects are reproduced by reading the code path to
its end. Each one also gets a red test in phase 4a (tasks.md).

| # | Claim in #2994 | Code path | Holds? |
|---|---|---|---|
| P1 | A permanently refused PATCH logs a Warning with the exception every interval | `MediaMtxSourceRepairSweep.RunPassAsync` builds **a new** `MediaMtxSourceRepair(logger)` on every tick (`MediaMtxSourceRepairSweep.cs:63`). `TryRepairPathAsync`'s catch calls `ReconcilerFailedToRepairPathSource(ex, path)` (Warning, with the exception, `MediaMtxSourceRepair.cs:86`; `Log.cs:34-35`) and remembers nothing. The default interval is 10 s, so one refused path logs about 8,640 Warnings a day. During an outage of the control API every diverged path does this on every tick. A 404 takes the same branch, so a retire race also logs a Warning. | **Yes** |
| P2 | A hostile `pageCount` loops | `ListConfiguredPathSourcesAsync` (`MediaMtxRtspGateway.cs:122-150`): `while (page < pageCount)` and nothing else. An empty `items` array does not end the loop. With `pageCount = 2147483647` that is 2^31 GETs in one pass, each spending `api` budget (ADR-0171). The sweep cannot tick while the pass runs (`PeriodicTimer` is sequential), so the repair also stops. | **Yes** |
| P3 | A non-integer `pageCount` throws `FormatException` and fails the pass | Same lines: `pageCountElement.ValueKind == JsonValueKind.Number ? pageCountElement.GetInt32() : 1`. `GetInt32` throws `FormatException` for a JSON number that is not an Int32 (`1.5`, `3e9`, `1e400`). The exception escapes `RepairAsync`, so the sweep logs `SourceRepairSweepFailed` on every tick and the startup pass logs `ReconcilerStartupPassFailed`. A non-number `pageCount` (`"5"`, `null`) reads as one page and does not throw. | **Yes**, for JSON numbers |
| P4 | A failed repair list is logged as `ReconcilerStartupPassFailed` after `ReconcilerStartupPassComplete` | `ReconcileOnceAsync` logs `ReconcilerStartupPassComplete` (`MediaMtxReconciler.cs:169`) and then awaits `RepairAsync` (`:171-172`) without a catch. A throw from `ListConfiguredPathSourcesAsync` reaches `RunDeferredAsync`'s catch (`:113-130`), which logs `ReconcilerStartupPassFailed`. Removal and re-add have already finished by then. | **Yes** |

**`LogThrottleTransition`** is at `src/StreamDistribution/Api/WhepAuthorizeActionPartition.cs:98-111`
(spec 208 US2, FR-009; review S5). It logs one Warning per partition when the partition first
becomes throttled, and logs again only after a full window has passed. The state is an `IMemoryCache`
entry per partition with absolute expiry, chosen because a hand-rolled dictionary keyed on
**attacker-influenced** input never evicted anything. This spec takes the idea of logging on a
transition. It does not take the store. Here the keys are `MediaMtxPath` values read from **this
service's own database**, so they are bounded by the stream count (250 at the target) and pruned on
every pass (FR-005). The decision also re-logs on an *outcome change*, which a presence-only cache
entry cannot express. And the state must live inside the pass object, not in a DI-scoped
`IMemoryCache`.

**Older readers.** `ListConfiguredPathsAsync` (`:84-120`) and `GetAllPathsHealthAsync` (`:229-268`)
share P2 and P3 verbatim. Their pinned tests, `MediaMtxRtspGatewayPaginationTests` (5 facts), cover
only well-formed `pageCount`s. No test pins either reader's behaviour on hostile input. **They are
not changed here** (see the header and #3010).

## 2. User stories

### US1 (P1) — A path that keeps failing is announced, not repeated

*As the operator reading the one telemetry sink (ADR-0118), I want a source MediaMTX keeps refusing
to show up as one Warning with its exception, again whenever the failure changes, and again every 10
minutes while it lasts, so that the only signal for a tile stuck on the wrong address stays readable
and does not drown everything else.*

```gherkin
Scenario: a permanently refused PATCH warns once, then at most every 10 minutes
  Given a stream whose database source differs from MediaMTX's configured source
  And   MediaMTX answers every PATCH for that path with 400
  When  the sweep runs a pass at t=0
  Then  one Warning "Reconciler failed to repair MediaMTX path {Path}; the next sweep retries." is logged with the HttpRequestException
  When  passes run at t=10s, 20s ... 9m59s
  Then  no Warning is logged for that path
  And   each of those passes logs one Debug line naming the path and the outcome "HttpRequestException 400"
  When  a pass runs at t=10m00s
  Then  the same Warning is logged again, with the exception

Scenario: a change of outcome is announced at once
  Given the path's last failure was a 400
  When  the next pass fails with 401 (the control-API partition is exhausted)
  Then  a Warning is logged at once, with the new exception
  When  the pass after that fails with 401 again
  Then  only a Debug line is logged

Scenario: a different exception type is a different outcome
  Given the path's last failure was HttpRequestException 400
  When  the next pass fails with TimeoutRejectedException
  Then  a Warning is logged with that exception

Scenario: a recovery resets the path
  Given a path that warned and then failed quietly
  When  a pass repairs it successfully
  And   a later pass fails with the same 400 again
  Then  that failure is a first failure and logs a Warning

Scenario: retire race — a 404 is "path gone", never a Warning
  Given a diverged path whose stream is retired between the MediaMTX read and the PATCH
  When  the PATCH answers 404
  Then  one Debug line naming the path and "path gone" is logged
  And   no Warning is logged
  And   the other paths in the pass are still repaired

Scenario: per-path state stays bounded
  Given a path that failed and warned
  When  its stream is retired, so the path is no longer in the database, for one completed pass
  And   it later reappears diverged and fails the same way, within 10 minutes
  Then  a Warning is logged, because its state was pruned

Scenario: the caller's cancellation still ends the pass (spec 378 FR-009)
  Given a pass whose token is cancelled while a PATCH is in flight
  Then  the pass throws OperationCanceledException
  And   nothing is logged at Warning or above
  And   no per-path state is changed
```

### US2 (P1, same slice) — A hostile or broken `pageCount` cannot stall or fail the repair read

```gherkin
Scenario: an empty page ends the read
  Given page 0 reports pageCount=2147483647 with one canonical item
  And   page 1 reports pageCount=2147483647 with "items": []
  When  ListConfiguredPathSourcesAsync runs
  Then  exactly pages [0, 1] are requested
  And   the item from page 0 is returned

Scenario: the page cap ends a read that never runs out of items
  Given every page reports pageCount=2147483647 and one canonical item
  When  ListConfiguredPathSourcesAsync runs
  Then  exactly 1,000 pages (0..999) are requested
  And   the 1,000 items read are returned
  And   one Warning naming the cap is logged by the gateway

Scenario Outline: an unreadable pageCount ends the read with one Warning, not an exception
  Given page 0 reports pageCount=<raw> with one canonical item
  When  ListConfiguredPathSourcesAsync runs
  Then  it does not throw
  And   exactly page [0] is requested and its item is returned
  And   one Warning is logged by the gateway, carrying the page index and not the raw value
  Examples: | 1.5 | 3e9 | 2147483648 | -2147483649 | 1e400 |

Scenario: what the read did before stays the same (characterisation; green before and after)
  Then  an absent or non-number pageCount ("5", null) still reads one page and logs no Warning
  And   a pageCount that shrinks mid-read and an empty last page still end at [0, 1]
  And   three full pages with pageCount=3 still read [0, 1, 2]
```

### US3 (P1, same slice) — A startup repair failure says what failed

```gherkin
Scenario: the repair list GET fails at startup
  Given removal and re-add have completed and "MediaMtxReconciler startup pass complete. ..." has been logged
  When  ListConfiguredPathSourcesAsync throws HttpRequestException
  Then  ReconcileOnceAsync does not throw
  And   the next log line is a Warning "MediaMtxReconciler source repair after the startup pass failed; the periodic sweep retries." with that exception
  And   "MediaMtxReconciler startup pass failed; continuing without reconcile." is not logged

Scenario: shutdown during the startup repair is still not a failure
  Given ApplicationStopping is cancelled while the startup repair runs
  Then  OperationCanceledException propagates out of ReconcileOnceAsync as it does today
  And   no Warning is logged
```

**Independent end-to-end test procedure.** (1) Local, no Docker: `dotnet test
tests/StreamDistribution.Infrastructure.Tests -c Release --filter
"FullyQualifiedName~MediaMtxSourceRepair|FullyQualifiedName~MediaMtxRtspGateway"`. US1 and US2 are
driven over the real `MediaMtxSourceRepair` and the real `MediaMtxRtspGateway` behind a stub
`HttpMessageHandler`, with a hand-written virtual clock. (2) CI (Aspire, real Postgres):
`MediaMtxSourceRepairLogVolumeIntegrationTests`. The real `MediaMtxSourceRepairSweep` runs at a
50 ms interval over a seeded stream whose PATCH is always refused, and the test counts exactly one
Warning across at least four passes. `ReconcileOnceAsync` runs with a failing repair list, and the
test asserts the US3 log order. (3) Phase 5, if a stack is running: repoint a stream to
`RTSP://10.0.8.1/x` (an upper-case scheme, which `StreamSourceUrl` accepts and MediaMTX refuses, per
spec 378 §9). The `stream-distribution` structured logs should then show one Warning, and the Debug
lines only at Debug level.

## 3. Functional requirements

- **FR-001** A `MediaMtxSourceRepair` instance keeps per-path failure state across the passes it
  runs. `MediaMtxSourceRepairSweep` holds **one** instance for its lifetime and no longer builds one
  per tick. The startup pass in `MediaMtxReconciler` keeps building its own instance, which runs
  once. No `static` mutable state and no DI registration.
- **FR-002** A path's failure **outcome** is the exception's runtime type plus, for an
  `HttpRequestException`, its `StatusCode` (which may be null). A failure is logged at Warning with
  the exception, using the existing `ReconcilerFailedToRepairPathSource` template unchanged, when:
  (a) the path has no state, (b) the outcome differs from the path's last recorded outcome, or
  (c) at least 10 minutes have passed since that path's last Warning. Otherwise it is logged at
  Debug with the path and the outcome, without the exception. The outcome is recorded on every
  failure. The Warning time is recorded only when a Warning is written.
- **FR-003** A failure whose outcome is `HttpRequestException` with `StatusCode == 404` is logged at
  **Debug** as "path gone" (path only, no exception). It is never a Warning, it clears that path's
  state, and it does not count as repaired. This applies to both the first PATCH and the re-patch.
- **FR-004** A successful repair of a path clears its state. The `Repaired` Information line is
  unchanged.
- **FR-005** When a pass **completes**, the state holds only the paths whose repair failed (not
  404) in that pass. Every path no longer in the database is therefore pruned, and so is every path
  that converged or was repaired. A pass that throws (the list GET fails, or the caller cancels)
  leaves the state as it was.
- **FR-006** The 10-minute interval is measured on `TimeProvider`'s monotonic timestamp
  (`GetTimestamp` / `GetElapsedTime`), not on wall-clock `GetUtcNow`, so a wall-clock step cannot
  suppress or flood Warnings. The sweep passes its injected `TimeProvider`. The startup pass and the
  existing one-argument constructor use `TimeProvider.System`. The 10 minutes is a constant, not
  configuration.
- **FR-007** The caller's cancellation semantics are unchanged (spec 378 FR-009): the catch filter
  stays `when (!cancellationToken.IsCancellationRequested)`, and a cancelled pass propagates without
  logging and without touching state.
- **FR-008** `ListConfiguredPathSourcesAsync` reads pages `0..999` at most. It stops after a page
  whose `items` is absent, not an array, or of length zero. It stops when `page + 1 >= pageCount`.
  It reads only one page when `pageCount` is absent or not a JSON number (today's behaviour). It
  stops with **one** gateway Warning (page index only) when `pageCount` is a JSON number that
  `TryGetInt32` rejects. It stops with **one** gateway Warning (the cap) when it has read 1,000
  pages and the last page still reported more. It never throws `FormatException`. The paths it read
  are returned.
- **FR-009** `ListConfiguredPathsAsync` and `GetAllPathsHealthAsync` are **not** changed (#3010).
  No shared enumerator is introduced.
- **FR-010** In `ReconcileOnceAsync`, the trailing repair is wrapped in
  `catch (Exception ex) when (!cancellationToken.IsCancellationRequested)`, which logs a new Warning,
  `ReconcilerStartupSourceRepairFailed(ex)`. Removal, re-add, their log lines and the summary line
  are unchanged (C7).
- **FR-011** No new log line carries a source URL, a raw `pageCount` token, or an exception message
  in its template. The Debug line names the path, the exception type name and the status. The
  gateway Warnings name the page index or the cap. Exceptions travel only as the `exception`
  argument of a Warning, as they do today (spec 373 FR-008).
- **FR-012** No change to `Shared.Contracts`, to any other context, to `IRtspGateway`, to DI
  registrations, to `AppHost`, to `MediaMtxSourceRepairOptions`, or to `SourceRepairSweepFailed`.

## 4. Non-goals

- Bounding `ListConfiguredPathsAsync` and `GetAllPathsHealthAsync` (#3010).
- Throttling the sweep's whole-pass `SourceRepairSweepFailed`, which during a full control-API
  outage is still one Warning per tick (see §6). The decision covers a *path's* failure.
- Sharing failure state between the startup pass and the sweep.
- Making the 10 minutes or the 1,000 pages configurable.

## 5. Judgement calls

- **[ASSUMPTION] A1: what gets pruned (FR-005).** The owner's floor is "prune paths no longer in the
  database". This spec keeps only the paths that *failed in the last completed pass*. That is a
  subset of the floor and gives the simplest invariant: |state| ≤ the live stream count, and in
  practice ≤ the paths failing right now. It also gives the transition semantics the decision asks
  for: a path that converged or was repaired in between is no longer "the same failure persisting",
  so its next failure is a first failure. The cost is one extra Warning if a path is skipped for a
  pass (for example, cut off by a truncated list read) and then fails again.
- **[ASSUMPTION] A2: the clock (FR-006).** Monotonic `TimeProvider.GetTimestamp`, not
  `GetUtcNow`. The fab runs PTP/NTP (ADR-0128 context), and a wall-clock step back would otherwise
  silence a path for the size of the step. The sweep already has `TimeProvider` injected, so no
  `IClock` is needed.
- **[ASSUMPTION] A3: the cap is logged (FR-008).** The decision names a Warning only for an
  unreadable `pageCount`. A cap that silently drops pages would hide the same class of fault, so
  reaching the cap with more pages reported also logs one Warning. Stop-on-empty logs nothing,
  because that is MediaMTX's own normal end of a list (the shrinking-`pageCount` case spec 378
  already pins).
- **Re-log reuses the first-failure template.** "Re-logged" means logging the same event again,
  which keeps one searchable message per failure. The Debug lines in between carry the repetition.
- **Two Warnings at boot are accepted.** The startup pass and the sweep's first tick (one interval
  later) keep separate state, so a refused path logs at most one extra Warning per process start.
  Sharing state would need a DI singleton and a registration-characterisation edit, which is a
  larger change for one line.
- **The startup-repair failure message names the sweep as the retry.** In production the sweep's
  first tick is 10 s later. In the integration lane the sweep is effectively off (spec 378 §5), but
  that lane never logs this line against a real failure.

## 6. Accepted residuals (in writing)

| Residual | Bound | Why accepted |
|---|---|---|
| Flapping outcomes (for example 400 ↔ timeout every pass) still Warn every pass | One Warning per *change*. This is the owner's rule taken literally. | Each Warning reports a real change. A flap is itself a signal. |
| A burst of N Warnings when a control-API outage starts with N diverged paths | N ≤ the diverged path count, then 0 for 10 minutes | This is the decision's "first failure" rule. It is what an operator must see. |
| `SourceRepairSweepFailed` (list GET failed) is still one Warning per tick during a full outage | One per tick, as today | Outside the decision (non-goal). Recommend a follow-up if it shows up in practice. |
| Hostile non-empty pages: 1,000 GETs per pass | At a 10 s interval that is up to 6,000 calls/min against the 2,000/min `api` partition, which the limiter then refuses with 401. The pass then fails as a list failure. | The owner chose the cap. Reaching it needs a MediaMTX that reports more than 100k paths or lies about having them. It is now logged (A3). |
| A Debug-only interval of up to 10 minutes during which a stuck tile has no Warning | ≤ 10 min, after a first Warning that already stated the fault | This is the trade-off the decision made. |

## 7. Acceptance

1. Every Gherkin scenario in §2 has a test, and each new-behaviour test was observed red first, or
   its compile error quoted where it needs the new constructor (tasks.md).
2. **No existing assertion is edited** (plan §8 lists every pinned assertion and why none changes).
3. `MediaMtxRtspGatewayPaginationTests`, `MediaMtxReconcilerPassCharacterisationTests` (C7),
   `MediaMtxReconcilerStartupOrderingTests` and every spec 378 test file pass unmodified.
4. `test-adversary` has run the outage/flood pass. `security-reviewer` has reviewed stream
   attribution and the control-API budget.
