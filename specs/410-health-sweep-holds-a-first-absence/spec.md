# Spec 410: The health sweep holds a path's first absence for one tick

**Feature Branch**: `fix/3021-list-read-followups` (cut from `origin/develop` at `f8d67354`)

**Created**: 2026-10-18

**Status**: Implemented (phase 4 done): red `d436ebc5`, fix `e57b7213`, invariant tests `bd795ab3`.
Two residuals found at review are recorded in §4. No `[NEEDS CLARIFICATION]` remains. Three
assumptions are marked **[ASSUMPTION]** in §7. Four owner questions are in §8. They are excluded
from this slice, not open inside it.

**Input**: Issue [#3021](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3021), items
2-5, under the owner's enrolment comment (2026-10-18 11:29): *"Scope: items 2-5 still open (item 1
delivered). Anything needing an ADR or an operational-budget decision is a block, not a decision for
the lane."* §1 checks every item against `f8d67354` and sorts it. **Only item 5 is in this slice.**
Items 2, 3 and 4 are owner decisions (§8).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing.** Phase 4a is **red** (ADR-0139,
constitution §Testing). No ADR. No gate weakened. No rate budget, timeout or cap is changed: the
slice issues **no** extra MediaMTX request (§5). No security review: no trust boundary, scope,
secret or rate budget changes.

**Spec number.** 410. Checked 2026-10-18 against `origin/develop` (highest is 408), every remote head
(no 409 or 410) and every worktree (`sse-3013` holds 409 for #3013, unwritten on disk at the time of
checking). Re-checked 2026-10-18 after phase 4 (`git fetch --all`): `origin/develop` at `f8d67354`
still tops out at 408; 409 exists only on `fix/3013-source-repair-followups` (`sse-3013`); 410
exists only on this branch; no other branch or worktree (`sse-3031b` included) holds 409-419.

**ADRs.** ADR-0139 / ADR-0144 (red colour; the lane's limits). ADR-0171 (context: every MediaMTX
control-API call spends the shared `api` partition of `/streams/authorize`; this slice adds none).
ADR-0040 / ADR-0073 (the false transition is a real `StreamHealthChangedV1` integration event, which
is why item 5 is a defect and not noise). ADR-0050 (no new `[LoggerMessage]`). ADR-0052 / ADR-0053
(xUnit, Shouldly, hand-written stubs, sentence-style names). ADR-0087 (each commit builds on its
own). ADR-0103 (`DispatchAsync` is internal precisely so the watcher is testable without a database).

## 1. Premise check, every item, on `f8d67354`

| # | Item | Holds on develop? | Detail | Class |
|---|---|---|---|---|
| 1 | Fold `ListConfiguredPathSourcesAsync` onto `ReadPagesAsync` | **Delivered** | `c8c3773c` (#3042, spec 405). `MediaMtxRtspGateway.cs` has one constant `MaxListPages = 1000` (L20) and one loop, `ReadPagesAsync` (L120-158), used by all three list reads (L102, L168, L288). | done |
| 2 | Size the page cap against the rate budget | **Holds** | `MaxListPages = 1000`. The `api` partition is `MediaMtxApiAuthorizeRateLimiting:PermitLimit`, default 2000 per 1 min (`StreamDistribution/Api/Program.cs:41-44`). A hostile MediaMTX answering non-empty pages with a huge `pageCount` costs up to 1,000 GETs per 2 s health tick (`StreamHealthWatcher.PollInterval`) plus up to 1,000 per 10 s source-repair pass (`MediaMtxSourceRepairOptions.Interval`). Spec 388 §6 and spec 391 §6 both record the 1,000 as **"The owner chose the cap"**; spec 388 §5 lists "making the 1,000 pages configurable" as out of scope. | **owner** (operational budget) |
| 3 | A deadline for a whole list read | **Holds, worse than stated** | No whole-read bound exists. The issue says "10 s per attempt". That is the attempt timeout, but each page GET runs under the standard resilience handler with no overrides (`ServiceDefaults/Extensions.cs:56`), so its defaults apply: 10 s per attempt, up to 3 retries for an idempotent GET, **30 s total per request**. A slow but successful MediaMTX does not trip the breaker. The worst case for one health tick is therefore about 1,000 x 30 s, not 1,000 x 10 s. Every retry also spends an `api` permit, which couples item 3 to item 2. | **owner** (operational budget) |
| 4 | Configured-paths read with malformed `items` should warn? | **Holds** | `AddPageToPaths` / `AddPageToSources` return `false` on absent or non-array `items` (L224, L188). `ReadPagesAsync` maps that to `Complete`, so neither caller logs. Both consumers are fail-safe: the startup reconciler removes orphans only among paths it read and re-adds the rest idempotently (`AddPathAsync`'s 400-then-GET); the source repair skips any path it did not observe (`MediaMtxSourceRepair.cs:44-50`). A sibling `pageCount`-unreadable case **does** warn (`Log.cs:46,52`), so the silence is inconsistent. | **owner** (warning volume; see §8 Q3) |
| 5 | Offset pagination can skip a path retired mid-sweep | **Holds** | `ReadPagesAsync` requests `?page={n}&itemsPerPage=100`; MediaMTX sorts by name on every request. A path removed after page *k* is read shifts every later name one place left, so the first name of the old page *k+1* is never returned. `DispatchAsync` maps any stream absent from the result to `"path not registered"` (`StreamHealthWatcher.cs:133-135`) and dispatches it; `ReportStreamHealthCommandHandler` calls `ReportDegraded` on the first not-ready observation, and `Stream.ReportDegraded` raises the transition (`Stream.cs:281`). One false `Healthy -> Degraded` `StreamHealthChangedV1`, reverted by the next tick. A camera *added* mid-sweep cannot cause a skip: `cam-{Guid v7}` names sort by creation time, so a new path lands after the cursor. A remove **plus** an add between two page reads leaves `itemCount` unchanged and still skips. | **lane** (this slice) |

Item 5 only shifts names left by one per concurrent removal, so the skipped path is a path that was
present in the previous sweep. That is the fact this slice uses.

**Found while checking, not in #3021's list (owner question §8 Q4).** Spec 002 FR-008 says
`Healthy -> Degraded` happens "on sustained RTSP read failure (≥ 10 seconds)", and
`StreamState.cs:11` documents "10 s without a frame". The watcher and the handler degrade on the
**first** not-ready observation. If FR-008 were built as written, item 5's false transition would be
absorbed with no change here. Building it changes every outage's announcement time; that is a
behaviour decision on a locked spec, so it is not taken in this slice.

## 2. Behaviour-changing vs behaviour-preserving, per item

| # | Part | Colour |
|---|---|---|
| 2 | Lowering or splitting `MaxListPages` | behaviour-changing. The 1,000-page assertions in specs 388/391's tests (`exactly 1,000 pages (0..999)`) would have to be edited, which the lane may not do without the owner. |
| 3a | A linked CTS bounding the whole read | behaviour-changing (a new failure mode on every list read) |
| 3b | Mapping that timeout to `HttpRequestException(InvalidResponse)` | behaviour-changing, and it lands in `HealthSweepFailed`, one Warning per tick (the #3013 volume) |
| 4a | Decide **not** to warn | behaviour-preserving; a recorded decision and at most a comment |
| 4b | Decide to warn | behaviour-changing (a new Warning, up to 8,640 a day at the 10 s repair interval unless throttled) |
| 5 | Hold a first absence one tick | **behaviour-changing → red.** This slice. |

## 3. User story

### US1 (P1): a path retired mid-sweep does not announce a false outage for its neighbour

As an operator watching the wall and the camera list, I want a camera whose MediaMTX path was
present in the previous health sweep and is missing from this one to be reported only if it is still
missing on the next sweep. Then a retire that shifts MediaMTX's pages under a sweep does not flash a
healthy neighbour to Degraded and back, or write two audit rows for an outage that did not happen.

**The rule.** For each non-retired stream in a sweep whose health read succeeded:

- present in the read → dispatched as today;
- absent, and **not** present in the last successful sweep (never seen, or already absent) →
  dispatched as `"path not registered"`, as today;
- absent, and present in the last successful sweep → **not dispatched this tick**. Nothing else about
  the camera changes (`degradedSince` is untouched).

"Last successful sweep" is the most recent tick whose `GetAllPathsHealthAsync` returned. A failed
tick (`HttpRequestException`, the `HealthSweepFailed` branch) neither updates nor clears it.

**Independent test.** On `f8d67354`, run the new `StreamHealthWatcherAbsenceHoldTests` and see the
red scenarios fail with the camera dispatched as `"path not registered"` on the tick it should be
held. Apply the change. Run the same file plus every existing `HealthWatcher` and gateway list-read
test unmodified and see all green.

#### Acceptance scenarios

```gherkin
Scenario: happy path, a retire mid-sweep does not degrade the neighbour (real gateway)
  Given cameras A..K are healthy and their paths fill pages 0 and 1 of /v3/paths/list
  And the watcher has completed one sweep in which every path was present
  When on the next sweep a path on page 0 is removed between the page-0 and page-1 requests
  So the first path of the old page 1 is returned by neither page
  Then that camera is not dispatched on this sweep
  And every other present camera is dispatched as Healthy

Scenario: a path that stays missing is reported on the next sweep
  Given camera B was present in the last successful sweep
  When B is absent on two consecutive successful sweeps
  Then B is not dispatched on the first of them
  And B is dispatched with LastError "path not registered" on the second

Scenario: a path that comes back is never reported missing
  Given camera B was present, then absent on one sweep
  When B is present again on the next sweep
  Then no "path not registered" observation is ever dispatched for B

Scenario: a path never seen is reported at once (provisioning, unchanged)
  Given the watcher has never seen camera C's path
  When C is absent on the first sweep
  Then C is dispatched with LastError "path not registered" on that sweep

Scenario: conflict, a failed tick between does not reset the hold
  Given camera B was present in the last successful sweep
  And the next tick's health read throws HttpRequestException
  When B is absent on the sweep after that
  Then B is not dispatched on that sweep
  And B is dispatched as "path not registered" if it is still absent on the following sweep

Scenario: bad request, the read fails
  Given GetAllPathsHealthAsync throws HttpRequestException
  Then no camera is dispatched and exactly one HealthSweepFailed Warning is logged (unchanged)

Scenario: no extra MediaMTX call
  Given 250 cameras, one of them held
  When a sweep runs
  Then exactly one GetAllPathsHealthAsync call and zero GetPathHealthAsync calls are made
```

**Auth.** N/A. No endpoint, scope or caller changes; the gateway authenticates to MediaMTX's control
API exactly as before (ADR-0171).

## 4. Edge cases

| Case | Behaviour | Accepted because |
|---|---|---|
| A path genuinely removed from MediaMTX (lost config, an operator delete) | reported one tick (2 s) later than today | FR-008 asks for ≥ 10 s of sustained failure anyway; this moves toward it, not away |
| MediaMTX restarts and returns every path absent | every camera held one tick, then all reported | one tick of delay on a fab-wide event that is already reported by `HealthSweepFailed` while MediaMTX is down |
| A path that alternates present/absent every tick | never reported absent | needs a remove + re-add every 2 s; nothing in the system re-adds a path between ticks (the reconciler runs at startup only, the source repair only patches). Recorded, not handled |
| **Residual.** The first sweep after a process restart | assumption A1 is false there: the set starts empty, so a pagination skip on that sweep is still reported as a false Degraded (and reverted next tick) | rare (needs a concurrent removal inside the first 2 s tick); the set is in-memory by design (§5). Accepted |
| **Residual.** Two consecutive skips of one path by two separate concurrent removals | the first skip is held, the second is reported, so a false Degraded is still announced | needs two removals landing between page reads on two consecutive ticks, each shifting the same path; rare. Accepted |
| The stream retired mid-sweep itself | held this tick; excluded by the next tick's query | removes today's dispatch onto a just-retired aggregate, which `EnsureNotRetired` rejects with a `RejectedHealthTransition` Warning (`Application/Log.cs:20`) |
| Two removals before page *k+1* | two neighbours skipped, both held | the rule is per path |
| Memory | one set of the last successful sweep's present paths, replaced each successful tick | bounded by the path count (250-camera target) |

## 5. Locked tech choices

- `src/StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs` only. No change to
  `IRtspGateway`, `MediaMtxRtspGateway`, `ReadPagesAsync`, `MaxListPages`, `Log.cs`, the handler or
  the domain.
- The watcher is a singleton `BackgroundService` that already keeps cross-tick state
  (`degradedSince`); the held set lives beside it, same lifetime.
- No new MediaMTX request, so the `api` partition spend is unchanged (ADR-0171).
- No new log event. A Debug line for the hold was considered and left out: nothing reads it, and
  #3013 / spec 409 is deciding the gateway's Warning policy in parallel.
- Tests: xUnit + Shouldly, hand-written stub gateway and the real gateway over a stub
  `HttpMessageHandler` (ADR-0052), as `StreamHealthWatcherScopeTests` and
  `StreamHealthWatcherCutShortReadTests` already do. `StreamDistribution.Infrastructure.Tests` has no
  CI shard filter (shard files exist only under `tests/Integration.Tests/ci-shards/`).

## 6. Latency budget (constitution §IV)

**N/A.** The health sweep is not on the event-to-overlay path: it reports stream state, which the
kiosk shows as tile status, not overlay. No leg of §IV is touched. The one timing effect is that an
absence previously seen as present is announced one poll interval (2 s) later; no NFR binds that
figure below FR-008's 10 s.

## 7. Assumptions

- **[ASSUMPTION] A1.** A skipped path was always present in the previous successful sweep. True for
  a path that existed across two ticks. A path added and removed inside one tick cannot be skipped
  in a way that matters: it was never dispatched as present. **False on the first sweep after a
  process restart** (the set starts empty); recorded as a residual in §4.
- **[ASSUMPTION] A2.** The hold applies whatever the stream's current state. For a stream already
  Degraded the held tick would have raised no transition anyway (`Stream.cs:281` raises only on a
  state change); keeping one rule is simpler than a state-dependent one.
- **[ASSUMPTION] A3.** Phase 5 cannot stage the millisecond race on the live stack. Verification is
  the real-gateway scenario in §3 plus one live observation: deleting a healthy camera's MediaMTX path
  still yields its Degraded `StreamHealthChangedV1`, timestamped at least one poll interval after the
  delete (tasks.md T030).

## 8. Owner questions (excluded; each is a block for the lane, per the enrolment comment)

- **Q1 (item 2).** Keep `MaxListPages` at 1,000, or lower it (the issue suggests 10-20; 250 cameras
  need 3 pages)? One value for all three reads, or a smaller cap for the 2 s health read only?
  Spec 388 records the 1,000 as your choice and configurability as out of scope.
- **Q2 (item 3).** Add a whole-read deadline? If so: its value (a figure against the 2 s tick and the
  30 s per-request total), whether it covers all three reads, and whether expiry should fail the
  read (health: `HealthSweepFailed`, one Warning per tick) or end it as partial (configured reads,
  like the cap). Q1 and Q2 interact: the deadline bounds time, the cap bounds permits.
- **Q3 (item 4).** Should an absent or non-array `items` on a configured-paths page warn? If yes,
  after #3013 / spec 409 lands, through its throttle, so the 10 s repair does not add another
  8,640-a-day Warning. If no, the decision is recorded and the item closes with no code.
- **Q4 (found here).** Build spec 002 FR-008's "sustained ≥ 10 s" before `Healthy -> Degraded`? It
  would subsume this slice's hold and every other one-tick flap, at the cost of announcing every real
  outage up to 10 s later. A separate issue either way.

## 9. Neighbours

- **#3013 / spec 409** (parallel, `sse-3013`): source repair and gateway Warning volume. This slice
  touches no file it is expected to touch (gateway, source repair, `Log.cs`). If 409 throttles
  `HealthSweepFailed` inside `StreamHealthWatcher.DispatchAsync`, the two edit the same method; the
  later one rebases. No semantic overlap.
- **#3029** (S138 on `GetAllPathsHealthAsync`): untouched by this slice.
