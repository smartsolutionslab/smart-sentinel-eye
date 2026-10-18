# Spec 405: The configured-sources read shares ReadPagesAsync

**Feature Branch**: `refactor/3021-fold-configured-sources-loop` (cut from `origin/develop` at `b7001237`)

**Created**: 2026-10-18

**Status**: Phases 1-3 written; phase 4 not started. No `[NEEDS CLARIFICATION]` remains. Two
assumptions are marked **[ASSUMPTION]** in §6.

**Input**: Issue [#3021](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3021), item 1
only, as the lane scope comment on the issue limits it: *fold spec 388's
`ListConfiguredPathSourcesAsync` loop onto the shared private `ReadPagesAsync` (spec 391),
behaviour-preserving, characterisation first.* Items 2 (cap sizing against the rate budget), 3
(whole-read deadline), 4 (warn on malformed configured-paths `items`) and 5 (offset pagination
skipping a path retired mid-sweep) are **out of scope**. Each is a decision or a behaviour change.

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving.** Phase 4a is **characterisation, observed
green** (ADR-0139, constitution §Testing). No ADR. No gate weakened. No security review needed: no
trust boundary, scope, secret or rate budget changes. The request sequence is identical (§3).

**Spec number.** 405. Checked 2026-10-18 against `origin/develop`, every remote head and every
worktree. 402 is open PR #3041. 403 and 404 are reserved by two parallel architects. Re-check
before the PR is opened.

**ADRs.** ADR-0139 / ADR-0144 (characterisation colour, the lane's limits). ADR-0050
(`[LoggerMessage]` warnings stay where they are, with the same text). ADR-0052 / ADR-0053 (xUnit,
Shouldly, hand-written stub handler, sentence-style names). ADR-0087 (each commit builds on its own).
ADR-0171 (context only: every control-API GET spends the shared `api` partition. The request count
is unchanged, so the budget is untouched). ADR-0084 (context only: see §7 on #3029).

## 1. Premise: verified on `b7001237`

`src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs` has two private constants,
both `1000`: `MaxConfiguredSourcePages` (L20, spec 388) and `MaxListPages` (L21, spec 391). It has
two pagination loops:

- `ReadPagesAsync` (L121-159, spec 391), used by `ListConfiguredPathsAsync` (L92, startup
  reconciler `MediaMtxReconciler.cs:155`) and `GetAllPathsHealthAsync` (L283,
  `StreamHealthWatcher.cs:109`).
- A hand-written loop inside `ListConfiguredPathSourcesAsync` (L161-201, spec 388), used only by
  `MediaMtxSourceRepair.cs:40` (startup repair and the periodic `MediaMtxSourceRepairSweep`).

Spec 391 plan §3 deliberately left the second loop alone ("a refactor of spec 388's method inside a
behaviour-changing fix ... is two issues") and named this follow-up: move the method onto
`ReadPagesAsync`, fold `MaxConfiguredSourcePages` into `MaxListPages`, and keep every spec 388 test
unmodified and green. **The premise holds.**

## 2. The two loops compared

Each row is something a caller, a log reader or MediaMTX can observe.

| # | Observable | Hand loop (`ListConfiguredPathSourcesAsync`) | `ReadPagesAsync` + caller switch, as `ListConfiguredPathsAsync` uses it | Same? |
|---|---|---|---|---|
| 1 | Request | `GET /v3/config/paths/list?page={n}&itemsPerPage=100`, `n` = 0, 1, ... | `GET {listPath}?page={n}&itemsPerPage=100` with `listPath = "/v3/config/paths/list"` | yes |
| 2 | Non-success status | `EnsureSuccessStatusCode` throws `HttpRequestException`. Nothing returned, nothing logged | same call, same place | yes |
| 3 | Body read | `ReadFromJsonAsync<JsonElement>(cancellationToken)` | same | yes |
| 4 | Order of checks per page | items first (`AddPageToSources`), then `pageCount` | `addPage(payload)` first, then `pageCount` | yes |
| 5 | Absent or non-array `items`, or empty array | `AddPageToSources` returns `false`: the read ends, partial result, no Warning | `addPage` returns `false`: `Complete`, so no Warning | yes |
| 6 | Malformed items (no or non-string `name`, non-canonical name, no or non-string `source`) | skipped inside `AddPageToSources`. A page of only such items is *not* empty | `AddPageToSources` unchanged, so the same | yes |
| 7 | Duplicate name across pages | later value wins (`sources[path] = source`) | unchanged | yes |
| 8 | `pageCount` absent or not a JSON number | one page, then stop. No Warning | `Complete`, so no Warning | yes |
| 9 | `pageCount` a number but not `Int32` | `MediaMtxConfiguredSourcesPageCountUnreadable(page)` once, partial result | `PageCountUnreadable` with the same `page`. The caller logs the same method with `read.Page` | yes, if the caller logs the **Sources** method (not the **Paths** one) |
| 10 | `page + 1 >= pageCount` (including 0 and negative) | stop, no Warning | `Complete` | yes |
| 11 | Cap | 1,000 requests (pages 0-999), then `MediaMtxConfiguredSourcesPageCapReached(1000)`, partial result | 1,000 requests, `PageCapReached`. The caller logs with `MaxListPages` (= 1000) | yes, if the caller logs the **Sources** method |
| 12 | Cancellation | the token flows to `GetAsync` and `ReadFromJsonAsync`. OCE propagates, nothing logged | same | yes |
| 13 | Non-object page body (`[]`, `5`) | `payload.TryGetProperty` throws `InvalidOperationException` from `AddPageToSources` | same call, now inside the lambda. Propagates unchanged | yes |
| 14 | Result | `Dictionary<MediaMtxPath, string>`, returned as `IReadOnlyDictionary` | the same dictionary, filled by the lambda | yes |
| 15 | Log text, level, EventId | `Log.cs:46-50`, Warning, the two **Sources** messages ("...the source repair compares only the paths read so far.") | unchanged. The caller picks which method to call | yes, if row 9 and row 11 hold |

**Verdict: no observable difference.** Every stop rule, the request sequence and count, the
partial-result semantics, the Warning names and text, the malformed-item handling and the ordering
are the same. The fold is behaviour-preserving, provided the new caller switch calls the **Sources**
logger methods (rows 9, 11, 15). Calling the **Paths** methods is the one plausible copy-paste
mistake, and the existing tests would **not** catch it (§4).

`ReadPagesAsync` itself is not edited. So `ListConfiguredPathsAsync` and `GetAllPathsHealthAsync`
cannot change.

## 3. User stories

### US1 (P1): one pagination loop for the three list reads

As a maintainer, I want the configured-sources read to use the same `ReadPagesAsync` loop and the
same page cap as the other two list reads. Then a future change to the stop rules, the cap (#3021
item 2) or a deadline (#3021 item 3) is made once and reaches all three.

**Independent test.** Run the characterisation set (tasks.md T001) on `b7001237` and see it green.
Apply the fold. Run the same set unmodified and see it green. Confirm that
`git grep -n "MaxConfiguredSourcePages" src` is empty, and that `MediaMtxRtspGateway.cs` has exactly
one `for (var page = 0;` loop.

#### Acceptance scenarios

```gherkin
Scenario: happy path, an unchanged multi-page read
  Given MediaMTX's /v3/config/paths/list answers pages 0..2 with pageCount 3
  When ListConfiguredPathSourcesAsync runs
  Then it requests exactly page=0,1,2 with itemsPerPage=100 on /v3/config/paths/list
  And it returns every canonical path's source verbatim, and logs nothing

Scenario: conflict, a cut-short read keeps its own Warning
  Given page 2 reports a pageCount that is not a 32-bit integer
  When ListConfiguredPathSourcesAsync runs
  Then it returns the paths of pages 0..2 and stops
  And it logs exactly one Warning whose text names page 2 and "the source repair"
  And not the startup reconcile's text

Scenario: conflict, the cap keeps its own Warning
  Given every page reports pageCount 2147483647 and one item
  When ListConfiguredPathSourcesAsync runs
  Then it issues 1000 requests and returns 1000 sources
  And it logs exactly one Warning naming 1000 pages and "the source repair"

Scenario: bad request, MediaMTX refuses a page
  Given page 1 answers 503
  When ListConfiguredPathSourcesAsync runs
  Then it throws HttpRequestException with StatusCode 503 after requests page=0,1
  And the gateway logs nothing

Scenario: bad request, a page that is not a JSON object
  Given page 0's body is a JSON array
  When ListConfiguredPathSourcesAsync runs
  Then it throws InvalidOperationException (pinned as today's behaviour, not endorsed)
```

**Auth.** N/A. The gateway's HTTP client authenticates to MediaMTX's control API exactly as before
(ADR-0171). No endpoint, scope or caller changes.

## 4. Covering tests: what exists, what is missing

**Existing, all in `tests/StreamDistribution.Infrastructure.Tests/Gateways/`, against the real
gateway over a stub `HttpMessageHandler`:**

- `MediaMtxRtspGatewayConfiguredSourcesTests` (spec 378). Verbatim source, endpoint path,
  non-canonical filtering, multi-page merge, missing/non-string `source`, absent `pageCount`.
- `MediaMtxRtspGatewayConfiguredSourcesBoundsTests` (spec 388). Empty page vs huge `pageCount`, the
  1,000 cap with one Warning containing `"1000"`, unreadable `pageCount` (5 forms) with one Warning,
  a non-canonical-only page, a non-number `pageCount`.
- `MediaMtxRtspGatewayConfiguredSourcesBoundsAdversarialTests` (spec 388). Cap at 999/1000/1001,
  zero/negative `pageCount`, absent/non-array `items` (4 forms), early empty page, duplicate names,
  a 10,000-digit `pageCount`, cancellation, cut-then-whole reads, one Warning per read, and two
  repair-level tests through the real gateway.
- `MediaMtxSourceRepairOverRealGatewayTests`. Round-trip, shifting pages, shrinking `pageCount`,
  secret redaction.
- For the untouched siblings: `MediaMtxRtspGatewayPaginationTests`,
  `MediaMtxRtspGatewayListReadersBoundsTests`, `MediaMtxRtspGatewayListReadersCharacterisationTests`,
  and `HealthWatcher/StreamHealthWatcherCutShortReadTests`.

**Missing.** A plausible wrong fold would still pass the existing tests in these cases, so they must
be pinned first:

| Gap | Why it matters | Row |
|---|---|---|
| C1. Which Warning on an unreadable `pageCount`, and for which page | existing tests count Warnings and only cut at page 0. The **Paths** method, or a wrong `Page`, would pass | 9, 15 |
| C2. Which Warning at the cap | existing test asserts `Contains("1000")`, which the **Paths** cap text also satisfies | 11, 15 |
| C3. Exact request URI including `itemsPerPage=100` | existing tests parse only `page=` and check the path. A different `listPath` or query would pass | 1 |
| C4. A non-success page throws `HttpRequestException` with its status, after exactly the requests made, with no gateway log | not covered for this reader | 2 |
| C5. An empty page whose `pageCount` is unreadable ends with **no** Warning | pins the order of the checks (items first) | 4, 5 |
| C6. A non-object page body throws `InvalidOperationException` | pins today's exception. A fold that adds a `ValueKind` check would change it | 13 |

The test-writer writes C1-C6 **first**, on unchanged code, and observes them **green**. A C test that
is red on `b7001237` means the test or this table is wrong: stop and report. Do not adjust the
production code.

## 5. Locked tech choices

xUnit + Shouldly, hand-written `HttpMessageHandler` stub and the existing `CapturingLogger<T>`
(ADR-0052). No Moq needed. No Aspire fixture: these are unit tests in
`StreamDistribution.Infrastructure.Tests`, which has no CI shard filter. `[LoggerMessage]` methods
are unchanged (ADR-0050).

## 6. Assumptions

- **[ASSUMPTION] A1.** C6 pins the `InvalidOperationException` for a non-object body as current
  behaviour, not as a decision that it is right. The sweep's own failure handling absorbs it today.
  If the owner wants it to warn or end quietly, that is a separate behaviour issue, like #3021 item 4.
- **[ASSUMPTION] A2.** The comment above `ListConfiguredPathSourcesAsync` is reworded to say it shares
  `ReadPagesAsync`, the same way spec 391 reworded `ListConfiguredPathsAsync`'s. The comment in
  `ListConfiguredPathsAsync` that says it "shares ReadPagesAsync with GetAllPathsHealthAsync" may
  name all three. Comment-only. Neither changes behaviour.

## 7. Latency, metrics and neighbours

- **Latency budget (constitution §IV): N/A.** The configured-sources read serves the source repair
  (startup, and the periodic sweep at a 10 s interval). That is control plane, not on the
  event-to-overlay path. `GetAllPathsHealthAsync` (the 2 s health sweep) is not edited.
- **#3029 (S138 on `GetAllPathsHealthAsync`, 33 lines against 30).** This fold does **not** touch
  that method, so it does **not** remove the warning. #3029 stays open. Per its lane comment it
  rebases on develop after this merges. Its cited line number (L283) shifts up by about 20.
  `ListConfiguredPathSourcesAsync` emits no ADR-0084 warning today: the whole-solution count in
  #3029 is 2 S138, and the other one is `AddWolverineForContext`. It shrinks from 41 lines to about
  22.
- **PR #3041 (spec 402, #3013)** edits `MediaMtxSourceRepair.cs` and its tests, not the gateway. No
  file overlap with this slice.

## 8. Out of scope (the lane must not take these)

- Changing `MaxListPages`'s value, or splitting it per reader (#3021 item 2: a decision).
- Any whole-read deadline or linked `CancellationTokenSource` (#3021 item 3: a decision).
- Warning on malformed configured-paths `items` (#3021 item 4), or changing C6's exception (A1).
- Any edit to `ReadPagesAsync`, `PageRead`, `PageReadEnd`, `AddPageToSources`, `AddPageToPaths`,
  `AddPageToHealth`, `GetAllPathsHealthAsync`, `ListConfiguredPathsAsync`'s logic, or `Log.cs`.
- #3029's S138 extraction. It waits for this slice, then rebases.
- Editing any existing test file. Adding new test files is allowed only in phase 4a.
