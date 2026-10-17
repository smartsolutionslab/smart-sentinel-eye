# Spec 391: The two older MediaMTX list readers bound their page read, and a cut-short health read fails the tick

**Feature Branch**: `fix/3010-mediamtx-list-readers-bound` (cut from `origin/develop` at `e4f3eab5`)

**Created**: 2026-10-17

**Status**: Phases 1-4 done (T001-T003 red, T010-T012 green); phases 5-7 pending. No `[NEEDS CLARIFICATION]` remains. The recorded decision on #3010
settles the open question. Two choices the decision did not make are marked **[ASSUMPTION]** in §5:
which exception carries the failed health read, and that the shared loop is not extended to
spec 388's reader in this slice.

**Input**: Issue [#3010](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3010),
*MediaMTX list readers: bound pageCount in ListConfiguredPathsAsync and GetAllPathsHealthAsync*.
It was split out of #2994 (spec 388), which bounded only `ListConfiguredPathSourcesAsync`.

**Decision (recorded on #3010 as the lane recommendation under the owner's delegation for open
items; the owner may overrule). Recorded here and not re-opened:**

1. **`GetAllPathsHealthAsync`.** If the page bound or an unreadable `pageCount` cuts the read short,
   the read **fails the tick** through `StreamHealthWatcher`'s existing failure handling. It returns
   neither a partial result nor the previous tick's result. The reason: the watcher reads a missing
   path as "not registered", so a partial result would announce outages that are not happening.
2. **`ListConfiguredPathsAsync`.** A read cut short returns the **partial result with one Warning**.
   This is safe because orphan removal only removes the paths it lists, and a path left unlisted
   gets an `add/` that spec 309 makes idempotent.
3. **The bound is the same as spec 388's.** An empty page ends the read. At most 1,000 pages are
   read. `pageCount` is read with `TryGetInt32`.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing**: a read that used to loop or throw
`FormatException` now ends, and a new Warning and a new exception are introduced. Phase 4a is
therefore **red** (ADR-0139). An **adversarial pass is recommended** (hostile `pageCount` against
the 2 s watcher). A **security review is required**, because the change touches the control-API
rate budget (ADR-0171) and whether outage announcements are true.

**Spec number.** 391. Checked 2026-10-17. `origin/develop`'s highest spec is 390. No local head,
remote head or worktree has a `specs/39[1-9]-*` directory. Re-check before the PR is opened.

**ADRs.** ADR-0171 (every control-API GET authorizes through `/streams/authorize` and spends the
shared `api` partition, 2000/min). ADR-0050 (`[LoggerMessage]`, structured fields). ADR-0118 (one
sink per environment, which must stay readable). ADR-0052 (hand-written stub handler). ADR-0103 (no
in-memory provider, so the watcher is tested through `DispatchAsync`). ADR-0036 (smallest change).
ADR-0139/ADR-0144 (red first; the lane implements decisions and does not make them). **No new ADR.**
The decision chooses a failure outcome inside one infrastructure class, using a failure path that
already exists. It adds no pattern, contract or cross-context effect.

**Latency budget.** N/A. This is control plane only: the 2 s health poll and the startup reconcile
against MediaMTX's control API. It is on none of constitution §IV's legs. Health announcements are
not part of the event→overlay path.

---

## 1. Premise, verified against `e4f3eab5` (not assumed)

| # | Claim in #3010 | Code path today | Holds? |
|---|---|---|---|
| P1 | `ListConfiguredPathsAsync` loops `pageCount` times, and an empty `items` does not end it | `MediaMtxRtspGateway.cs:97-119`: `while (hasMorePages)`, `hasMorePages = page < pageCount`. `AddPageToPaths` (`:202-223`) returns nothing, so an empty page cannot stop the loop | **Yes** |
| P2 | `GetAllPathsHealthAsync` does the same | `:264-286`, the same loop. `AddPageToHealth` (`:291-313`) returns nothing | **Yes** |
| P3 | A non-Int32 JSON number throws `FormatException` | Both readers use `pageCountElement.ValueKind == JsonValueKind.Number ? pageCountElement.GetInt32() : 1` (`:112-115`, `:279-282`). A non-number (`"5"`, `null`) or an absent value reads as one page and does not throw | **Yes**, for JSON numbers |
| P4 | Each GET spends the shared `api` budget | Every control-API call goes through the rate-limited hook (ADR-0171; `IRtspGateway.cs:55-64`). With `pageCount = int.MaxValue` and empty pages, one health tick never ends, because `StreamHealthWatcher.ExecuteAsync` (`:35-58`) awaits the poll before it delays | **Yes** |
| P5 | The watcher reads a missing path as "not registered" | `StreamHealthWatcher.DispatchAsync:133-135` | **Yes** |
| P6 | A redundant `add/` is idempotent (spec 309) | `AddPathAsync:42-50`: a 400 is followed by `GET /v3/config/paths/get/{name}`, and a 200 there means success with `MediaMtxPathAlreadyRegistered`. **No unit test pins this branch at the gateway**. Only spec 309's integration tests cover it. This spec adds a guard (§2, US2) | **Yes** in code, now pinned |
| P7 | The loops are unchanged since #2994 | Spec 388 changed only `ListConfiguredPathSourcesAsync` and `AddPageToSources`, which are now bounded at `:124-188`. The two older readers and their `AddPage*` helpers are as #3010 describes | **Yes** |

**What the watcher does today with each input** (this determines red versus guard):

- `FormatException` from the health read is **not** an `HttpRequestException`, so `DispatchAsync`'s
  catch (`:111-117`) misses it. It escapes `DispatchAsync`, and `ExecuteAsync`'s catch-all
  (`:45-48`) logs `StreamHealthWatcherPollFailed` (**Error**). The tick is already lost and no
  camera is dispatched. The defect in this case is the exception type and which path handles it,
  not a false outage.
- `int.MaxValue` with empty or non-empty pages: the tick **never ends**. No dispatch happens, the
  `api` partition is drained, and the control plane (provisioning, repoint, retire, source repair) is refused with 401 (ADR-0171). Viewers are not affected: they sit in the separate `whep` partition.

**Older readers' callers.** `GetAllPathsHealthAsync` has one production caller,
`StreamHealthWatcher.DispatchAsync:109`. `ListConfiguredPathsAsync` has one production caller,
`MediaMtxReconciler.ReconcileOnceAsync:155`, which feeds `RemoveOrphanPathsAsync` (iterates only
`configured`) and `ReaddMissingPathsAsync` (adds every live path not in `configured`).

## 2. User stories

### US1 (P1): A hostile or broken `pageCount` cannot stall the health watcher or announce false outages

*As the operator watching stream health on the wall, I want a MediaMTX list I cannot read
completely to skip one 2 s tick, so that a broken `pageCount` neither freezes the watcher and
drains the control-API budget, nor reports live cameras as "not registered".*

```gherkin
Scenario: an empty page ends the health read
  Given page 0 of /v3/paths/list reports pageCount=2147483647 with one canonical ready path
  And   page 1 reports pageCount=2147483647 with "items": []
  When  GetAllPathsHealthAsync runs
  Then  exactly pages [0, 1] are requested
  And   the one path is returned and nothing is logged at Warning by the gateway

Scenario: the page cap fails the health read rather than returning part of it
  Given every page reports pageCount=2147483647 and one distinct canonical path
  When  GetAllPathsHealthAsync runs
  Then  exactly 1,000 pages (0..999) are requested
  And   it throws HttpRequestException with HttpRequestError.InvalidResponse and no status code
  And   the exception message names the cap (1000)
  And   the gateway logs nothing at Warning (the watcher logs the failure)

Scenario Outline: an unreadable pageCount fails the health read
  Given page 0 reports pageCount=<raw> with one canonical path
  When  GetAllPathsHealthAsync runs
  Then  exactly page [0] is requested
  And   it throws HttpRequestException with HttpRequestError.InvalidResponse, not FormatException
  And   the exception message names the page index and does not contain <raw>
  Examples: | 1.5 | 3e9 | 2147483648 | -2147483649 | 1e400 |

Scenario Outline: a health tick whose read is cut short fails the tick and dispatches no camera
  Given two Healthy streams, cam-A and cam-B
  And   MediaMTX's runtime list reports cam-A ready on page 0 with pageCount=<raw>
  When  StreamHealthWatcher.DispatchAsync runs over the real MediaMtxRtspGateway
  Then  it does not throw
  And   no per-camera scope is opened and no ReportStreamHealthCommand is handled, so cam-B is not reported as "path not registered"
  And   exactly one Warning "MediaMTX health sweep failed; skipping this tick for every stream." is logged with an HttpRequestException (InvalidResponse)
  And   nothing is logged at Error
  Examples: | 1.5 | 3e9 | 2147483648 | -2147483649 | 1e400 |

Scenario: a health tick that hits the page cap fails the tick and dispatches no camera
  Given every page reports pageCount=2147483647 and one distinct canonical path
  When  StreamHealthWatcher.DispatchAsync runs over the real gateway
  Then  1,000 pages are requested, no camera is dispatched, and one HealthSweepFailed Warning is logged

Scenario: an empty page ends the health read and the tick dispatches every stream
  Given page 0 reports cam-A ready with pageCount=2147483647 and page 1 is empty
  When  StreamHealthWatcher.DispatchAsync runs over the real gateway
  Then  both streams are dispatched: cam-A ready, cam-B "path not registered" (it is genuinely absent)
  And   nothing is logged at Warning
```

### US2 (P1, same slice): The startup reconcile's path list is bounded and still safe when it is partial

*As the operator restarting the service, I want a broken `pageCount` on MediaMTX's config list
to end the reconcile's read with one Warning, so that the startup pass finishes and removes
orphans among the paths it did read, instead of looping or failing.*

```gherkin
Scenario: an empty page ends the configured-paths read
  Given page 0 of /v3/config/paths/list reports pageCount=2147483647 with one canonical path
  And   page 1 reports pageCount=2147483647 with "items": []
  When  ListConfiguredPathsAsync runs
  Then  exactly pages [0, 1] are requested, the one path is returned, and no Warning is logged

Scenario: the page cap ends the configured-paths read with one Warning
  Given every page reports pageCount=2147483647 and one distinct canonical path
  When  ListConfiguredPathsAsync runs
  Then  exactly 1,000 pages are requested and the 1,000 paths read are returned
  And   exactly one gateway Warning is logged, and its message contains "1000"

Scenario Outline: an unreadable pageCount ends the configured-paths read with one Warning
  Given page 0 reports pageCount=<raw> with one canonical path
  When  ListConfiguredPathsAsync runs
  Then  it does not throw, exactly page [0] is requested, and page 0's path is returned
  And   exactly one gateway Warning is logged; it names the page index and not <raw>
  Examples: | 1.5 | 3e9 | 2147483648 | -2147483649 | 1e400 |

Scenario: a path the partial list missed gets a redundant add that succeeds (guard; spec 309 FR-002)
  Given MediaMTX answers POST /v3/config/paths/add/{name} with 400
  And   GET /v3/config/paths/get/{name} answers 200
  When  AddPathAsync runs for that path
  Then  it does not throw
  And   one Information "MediaMTX path {Path} was already registered; treating add as success (spec 309)." is logged
```

### Characterisation (both readers; green before and after, unmodified)

```gherkin
Scenario Outline: today's well-formed reads stay the same
  Then  a pageCount that is absent or not a JSON number ("5", null) reads exactly one page with no Warning
  And   a page of only non-canonical names is not an empty page: with pageCount=2, page 1 is still read
  And   pageCount=0 with an empty page 0 reads exactly [0], with no Warning and no throw
  And   two full pages with pageCount=2 read [0, 1] (MediaMtxRtspGatewayPaginationTests, unmodified)
  Examples: | GetAllPathsHealthAsync | ListConfiguredPathsAsync |
```

**Independent end-to-end test procedure.** (1) Local, no Docker:
`dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter
"FullyQualifiedName~MediaMtxRtspGateway|FullyQualifiedName~StreamHealthWatcher"`. US1 and US2 run
over the real `MediaMtxRtspGateway` behind a stub `HttpMessageHandler`. The watcher scenarios use the
real `StreamHealthWatcher.DispatchAsync` with a recording command handler. (2) CI: the unmodified
`MediaMtxReconcilerPassCharacterisationTests` (C7), `MediaMtxReconcilerIntegrationTests` and
`StreamHealthTransitionTests` against the real MediaMTX, whose `pageCount` is well-formed. They
prove that the real list reads are unchanged. (3) Phase 5, if a stack is running: the
`stream-distribution` structured logs show no `HealthSweepFailed` and no new gateway Warning over a
minute of normal sweeps, and the reconciler summary line still counts every configured path. A
hostile MediaMTX cannot be provoked on the real stack without patching the container, so the
hostile cases are observed in the unit layer only. The PR says so.

## 3. Functional requirements

- **FR-001** `GetAllPathsHealthAsync` and `ListConfiguredPathsAsync` read pages `0..999` at most
  and apply spec 388's termination rules (its FR-008). They stop after a page whose `items` is
  absent, not an array, or empty. This test uses the raw item count, *before* the canonical-name
  filter. They stop when `page + 1 >= pageCount`. They read one page when `pageCount` is absent or
  not a JSON number. They never call `GetInt32`.
- **FR-002** `GetAllPathsHealthAsync`, when `TryGetInt32` rejects `pageCount` on page *p*, throws
  `HttpRequestException(HttpRequestError.InvalidResponse, message)` with no status code and no inner
  exception. The message names *p*. It does the same when 1,000 pages have been read and the last
  one still reported more pages, with a message that names the cap. It returns nothing partial and
  logs nothing itself.
- **FR-003** `ListConfiguredPathsAsync`, in those two cases, returns the paths it read and logs
  **one** gateway Warning: `MediaMtxConfiguredPathsPageCountUnreadable(page)` or
  `MediaMtxConfiguredPathsPageCapReached(cap)` (plan §4). Neither throws.
- **FR-004** `StreamHealthWatcher` is **not changed**. Its existing
  `catch (HttpRequestException)` in `DispatchAsync` handles FR-002's exception: `HealthSweepFailed`
  (Warning), no camera dispatched, and the next tick 2 s later.
- **FR-005** `MediaMtxReconciler` is **not changed**. A partial `configured` list removes orphans
  only among the listed paths, and re-adds unlisted live paths through the idempotent `add/`
  (spec 309 FR-002). The guard in US2 pins this.
- **FR-006** No log template or exception message carries the raw `pageCount` token, a source URL,
  or MediaMTX response text. Messages name the page index or the cap only (spec 388 FR-011).
- **FR-007** `ListConfiguredPathSourcesAsync` and `AddPageToSources` (spec 388) are
  **byte-identical** after this change. Every spec 388 test passes unmodified.
- **FR-009** `GetAllPathsHealthAsync`, when a page's `items` is absent, `null` or not an array,
  throws `HttpRequestException(HttpRequestError.InvalidResponse, message)`; the message names only
  the page index, never response content. `"items": []` remains a normal end. The configured-paths
  read is unchanged on malformed `items`. (This narrows FR-001 for the health read.)
- **FR-008** No change to `Shared.Contracts`, to any other context, to DI registrations, to
  `AppHost`, to `StreamHealthWatcher.cs`, to `MediaMtxReconciler.cs`, or to any existing `Log.cs`
  template. `IRtspGateway.cs` changes only in the doc comment of `GetAllPathsHealthAsync`
  (plan §5). Its signatures are unchanged.

## 4. Non-goals

- Folding spec 388's `ListConfiguredPathSourcesAsync` onto the new shared page loop. That is a
  behaviour-preserving refactor and gets its own issue (§5 A2). It is to be filed, not done here.
- Throttling the per-tick `HealthSweepFailed` Warning while MediaMTX stays hostile (see §6).
- A non-object page body (`[]`, `"x"`), which throws `InvalidOperationException` from
  `TryGetProperty`. That is spec 388's residual, tracked in #3013.
- Making the page cap configurable, or giving the health read a lower cap.
- Inter-page consistency while MediaMTX's list shifts between requests. This is unchanged and
  outside the decision.

## 5. Judgement calls

- **[ASSUMPTION] A1: the exception is `HttpRequestException(HttpRequestError.InvalidResponse, …)`.**
  "The watcher's existing failure handling" covers two paths. `DispatchAsync`'s
  `catch (HttpRequestException)` logs `HealthSweepFailed` at Warning, with the text *"skipping this
  tick for every stream"*. `ExecuteAsync`'s catch-all logs `StreamHealthWatcherPollFailed` at Error,
  *"poll iteration failed"*. The first path was written for exactly this case ("the batch call
  failed"), and using it needs no watcher change. `HttpRequestError.InvalidResponse` is the BCL's
  own name for "the server's response could not be used". The consequence: an unreadable
  `pageCount` that today logs at **Error** through the catch-all will log at **Warning** through
  `HealthSweepFailed`. A custom exception type would instead go to the catch-all (Error), or would
  need the watcher's catch widened. That is a larger change, and nothing asks for it.
- **[ASSUMPTION] A2: one shared private page loop for the two older readers, but spec 388's reader
  is not moved onto it in this slice.** The two older readers apply the same termination rules and
  differ only in what a cut-short read does (throw versus warn and return). A private helper that
  returns *how* the read ended, with each caller acting on that, removes a duplicated loop without
  a switch. The helper keeps spec 388's semantics exactly, and the older readers change only on
  the decided inputs (the characterisation set proves the rest). Moving
  `ListConfiguredPathSourcesAsync` onto it as well would be a behaviour-preserving refactor of a
  different method inside a behaviour-changing fix. The house rule makes that **two issues**. Until
  the follow-up lands, two copies of the rule exist: spec 388's inline loop and the new helper.
- **No gateway Warning on the health path.** The watcher's `HealthSweepFailed` already carries the
  exception, and a gateway Warning would log the same fault twice per tick.
- **An empty page is a normal end on both readers, and logs nothing.** This follows spec 388 A3.
  It is how MediaMTX's own list ends when it shrinks mid-read. On the health path a stream past an
  empty page is genuinely absent, so "not registered" is true.
- **The cap, an unreadable `pageCount`, and (health read only) a page without a readable `items`
  array count as "cut short"** (FR-009). The decision named the first two; a malformed page on the
  health read is added after review, because ending it as complete would report every later path
  as not registered. The configured read keeps spec 388's treatment (an empty page ends it).

## 6. Accepted residuals (in writing)

| Residual | Bound | Why accepted |
|---|---|---|
| A MediaMTX that stays hostile logs one `HealthSweepFailed` Warning every tick | About 43,200 per day | Each one reports a real, ongoing fault, and the decision chose failing the tick. The same class of volume as #3013. Recommend a follow-up if it is seen in practice |
| Hostile non-empty pages: up to 1,000 GETs per health tick | The tick is sequential, so the rate is limited by request latency. It exceeds the 2000/min `api` partition within the first tick, the limiter answers 401, `EnsureSuccessStatusCode` throws, and the tick fails. The control plane sharing the `api` partition (provisioning, repoint, retire, source repair) is starved for that window; viewers are in the separate `whep` partition and are unaffected | This is the owner's cap, the same as spec 388. Reaching it needs a MediaMTX that reports more than 100k paths or lies about having them. It is logged at Warning on every tick |
| A partial reconcile list leaves orphans beyond the cut until the next restart | Paths past page *p* or past 100k | This is decision (2). Orphan removal fails safe |
| A partial reconcile list costs one `add/` plus one `get/` per unlisted live path | ≤ 2 × the live stream count (500 at the target), once per start | Idempotent (spec 309). It is a one-off spend against the partition |
| `ReconcilerStartupPassComplete`'s `Configured=` reports the partial count | That one line | The gateway Warning just before it explains the shortfall |
| No whole-read deadline: a slow hostile MediaMTX can stall one health tick for up to 1000 x 10 s, with nothing dispatched or logged until it ends | One tick; the 10 s is the per-request timeout | A whole-read deadline is new behaviour outside this decision; recommend a follow-up |
| The cap of 1,000 is not sized against the 2000/min `api` budget | See the hostile-pages row | Owner-chosen in spec 388 and kept here for consistency |
| Offset pagination can skip a path when paths retire mid-sweep | One sweep | The skipped camera can produce one false `StreamHealthChangedV1`, reverted by the next tick |
| The configured-paths read is unchanged on malformed `items` | It ends as complete (spec 388 behaviour) | FR-009 applies to the health read only |
| Two copies of the bounded-loop rule (spec 388's inline loop and the new helper) | Until the A2 follow-up lands | The house rule: a refactor is its own issue |

## 7. Acceptance

1. Every Gherkin scenario in §2 has a test. Each new-behaviour test was observed **red first**, with
   the failure quoted in the PR (tasks.md T001-T002). The guard and characterisation tests were
   observed green before and after.
2. **No existing test assertion is edited.** `MediaMtxRtspGatewayPaginationTests` (5),
   `StreamHealthWatcherScopeTests`, every `MediaMtxRtspGatewayConfiguredSources*` file and every
   spec 388 file pass unmodified.
3. `git diff` shows no hunk in `ListConfiguredPathSourcesAsync`, `AddPageToSources`,
   `StreamHealthWatcher.cs` or `MediaMtxReconciler.cs`.
4. `security-reviewer` has reviewed the control-API budget (ADR-0171) and the truth of outage
   announcements. Optionally, `test-adversary` has run the hostile-`pageCount` pass.
