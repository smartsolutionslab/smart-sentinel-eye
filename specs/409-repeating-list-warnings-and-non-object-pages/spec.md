# Spec 409: A non-object MediaMTX page fails the read as an invalid response, and the source repair's whole-read Warnings repeat at most every 10 minutes

**Feature Branch**: `fix/3013-source-repair-followups` (cut from `origin/develop` at `f8d67354`)

**Created**: 2026-10-18

**Status**: Specified, planned and tasked. No `[NEEDS CLARIFICATION]` remains. Judgement calls the
issue does not make are marked **[ASSUMPTION]** in §6.

**Amended 2026-10-18 after the security and backend reviews of the first implementation**
(§12 records what changed and why): the wrapped parse error is no longer attached as
`InnerException` (FR-103, FR-104, A3), and every *untyped* parse failure of a list page, not only a
non-object body or element, now fails the read as an invalid response (FR-110..FR-115, US1b). That
pulls the former non-goal "`ParsePathHealth` on wrong-kind fields" into scope.

**Input**: Issue [#3013](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3013), items 2
and 3, *Source repair: a persistent 404, repeating gateway Warnings and a non-object page body are
still unhandled*. Item 1 was delivered by #3041 (spec 402), merged; verified on develop below.

**Lane scope (owner, #3013 comment 2026-10-18T11:29Z), recorded here and not re-opened:** only the
items still open (2 and 3). A fix that needs an ADR or a stored-data rewrite is a block. Neither does
(§7).

**ADRs**: ADR-0050 (MEL logging, `[LoggerMessage]`), ADR-0118 (one telemetry sink, so log volume
there is the operator's only signal), ADR-0171 (every control-API call spends the `api`
authorize-hook budget, which is why these reads repeat at all), ADR-0052 / ADR-0103 (hand-written
fakes, Aspire fixture, no Testcontainers), ADR-0084 (advisory metrics), ADR-0139 / ADR-0144 (red
first, autonomous lane). Builds on spec 388 (the per-path throttle `SourceRepairFailureLog`, whose
rule this spec reuses), spec 391 / spec 405 (`ReadPagesAsync`, now shared by all three list readers)
and spec 402 (item 1).

---

## 1. Premise check (develop at `f8d67354`)

| # | Claim in #3013 | Verified on develop | Holds? |
|---|---|---|---|
| 1 | Item 1 (persistent 404) is delivered | PR #3041 merged (spec 402); `MediaMtxSourceRepair.LogFailureAsync` (`src/StreamDistribution/Infrastructure/Reconciler/MediaMtxSourceRepair.cs:101-128`) confirms a 404 with `ReadCommittedSourceAsync` before `failureLog.Gone` | **Yes, delivered.** Out of scope |
| 2a | `MediaMtxConfiguredSourcesPageCountUnreadable` and `...PageCapReached` log once per read | `MediaMtxRtspGateway.ListConfiguredPathSourcesAsync` (`src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs:160-184`) logs on every `PageReadEnd` that is not `Complete`, with no memory. The gateway is a typed `HttpClient` (`AddHttpClient<IRtspGateway, MediaMtxRtspGateway>`, `StreamDistributionInfrastructureModule.cs:76`), **transient**, and the sweep resolves a new one per pass (`MediaMtxSourceRepairSweep.cs:61`), so it cannot remember anything across passes. Pinned today by `MediaMtxRtspGatewayConfiguredSourcesBoundsAdversarialTests.A_persistently_unreadable_page_count_warns_on_every_read` (`tests/.../Gateways/MediaMtxRtspGatewayConfiguredSourcesBoundsAdversarialTests.cs:181-192`, asserts 3 Warnings for 3 reads, its reason string citing "8,640 a day") | **Yes** |
| 2b | `SourceRepairSweepFailed` is one Warning per tick during a control-API outage | `MediaMtxSourceRepairSweep.RunPassAsync` (`MediaMtxSourceRepairSweep.cs:53-71`) catches every non-shutdown exception and logs `SourceRepairSweepFailed` (`Log.cs:58-59`, Warning, with the exception) with no memory. Spec 388 §6 accepted it explicitly as a residual ("Recommend a follow-up if it shows up in practice") | **Yes** |
| 3a | A non-object page body (`null`, `[]`, a number) makes `TryGetProperty` throw `InvalidOperationException` | `ReadPagesAsync` (`MediaMtxRtspGateway.cs:120-158`) hands the parsed `JsonElement` straight to `addPage`; all three `addPage` callbacks begin with `payload.TryGetProperty("items", …)` (`:188`, `:224`, `:293`), which throws `InvalidOperationException` for any `ValueKind` other than `Object`. Pinned today by spec 405's characterisation C6, `MediaMtxRtspGatewayConfiguredSourcesFoldCharacterisationTests.A_body_that_is_not_an_object_throws_an_invalid_operation_exception` (`…FoldCharacterisationTests.cs:103-112`); spec 405 A1 records it as "pins today's behaviour and does not endorse it" | **Yes**, for all three readers |
| 3b | `items: [1]` throws too | Each `addPage` loop calls `item.TryGetProperty("name", …)` (`:195`, `:231`, `:337`) on every element, which throws for a non-object element | **Yes**, for all three readers |
| 3c | "fails the pass every tick" | Source repair: the throw escapes `RepairAsync`; the sweep logs `SourceRepairSweepFailed` every tick (Warning). Startup reconcile: `ReconcilerStartupPassFailed` once. **Health sweep, not named in the issue and worse:** `StreamHealthWatcher.DispatchAsync` catches only `HttpRequestException` (`StreamHealthWatcher.cs:108-117`), so the `InvalidOperationException` escapes to `ExecuteAsync`'s generic catch and logs `StreamHealthWatcherPollFailed` at **Error** every 2 s (`Log.cs:88-89`), instead of the designed `HealthSweepFailed` Warning | **Yes, and wider than stated** |
| 3d | "belongs with #3010" | #3010 is **closed** (spec 391 bounded `pageCount`). Its follow-up #3021 is open and in the lane, but its open items (cap sizing, whole-read deadline, a Warning for malformed `items` on the configured-paths read, offset skipping) do not cover a non-object body or element. The owner enrolled this item here | **Stale.** It belongs here |
| 3e | "one page's size is unbounded (`ReadFromJsonAsync` of the whole body)" | True (`:131`). Bounding it needs a byte limit nobody has chosen | **Yes**, but out of scope (§5, owner decision) |
| 3f | (amendment, not in the issue) other parse failures of an object page | Backend review on the first implementation: `GetString()` on invalid UTF-8 / a lone surrogate (`name`, `source`, `readyTime`), `ReadFromJsonAsync` on an unsupported `Content-Type` charset, `GetBoolean` on a non-boolean `ready`, `TryGetProperty` on a non-object `source`, `GetString` on a non-string `source.type` all throw `InvalidOperationException`; their messages (or inner messages) quote the bytes or the charset | **Yes.** In scope as US1b |
| 3g | (amendment, security re-review) `Content-Type: application/json; charset=utf-7` | The encoding lookup in `ReadFromJsonAsync` throws `NotSupportedException` ("Support for UTF-7 is disabled", SYSLIB0001), neither `JsonException` nor `InvalidOperationException`; it escapes `ReadPageAsync` on all three readers and the watcher logs `StreamHealthWatcherPollFailed` at Error every 2 s | **Yes.** FR-110/FR-111 map it |

**Verdict: items 2 and 3 hold on develop.** Both are **behaviour-changing → phase 4a RED**. Not
mixed, so no split is required by colour; they are two user stories, each its own red/green commit
pair, so either can be reverted alone.

## 2. User stories

### US1 (P1): a MediaMTX page that is not shaped like a list fails the read as an invalid response

A proxy answering `200 []`, a MediaMTX bug answering `null`, or an element that is not an object
must fail the read with the same typed failure the readers already use for every other unreadable
answer, `HttpRequestException` with `HttpRequestError.InvalidResponse`. Today it throws
`InvalidOperationException`, which the health sweep does not expect, so a malformed page turns a
designed per-tick Warning into an unplanned Error every 2 seconds.

### US1b (P1, amendment): no other parse failure of a list page escapes untyped

A page that *is* an object of objects can still fail to parse in ways US1's shape checks do not see:
a string holding invalid UTF-8 or a lone surrogate, a response charset the runtime does not support,
or a field of the wrong kind on a ready health item. Each of these throws `InvalidOperationException`
today, and each reaches the health sweep as an Error every 2 s exactly as a non-object page did. They
fail the read with the same typed failure, carrying no part of the body and no attacker-chosen
charset name.

### US2 (P2): the source repair's whole-read Warnings repeat at most every 10 minutes

A MediaMTX that keeps reporting `pageCount: 1.5`, keeps hitting the 1,000-page cap, or a control API
that stays down, produces one Warning with its detail, again when the outcome changes, and again
every 10 minutes while it lasts. That is exactly the rule spec 388 set for a path's repair failure
(`SourceRepairFailureLog`), applied to the three Warnings that rule did not cover. Between Warnings,
each repeat is a Debug line, so the condition stays visible at Debug.

## 3. Acceptance scenarios

### US1

```gherkin
Scenario Outline: a page body that is not a JSON object fails the read as an invalid response
  Given page 0 of <reader> answers 200 with the body <body>
  When  the reader runs
  Then  it throws HttpRequestException whose HttpRequestError is InvalidResponse
  And   exactly page [0] is requested
  And   nothing at Warning or above is logged by the gateway
  Examples:
    | reader                             | body  |
    | GetAllPathsHealthAsync             | null  |
    | GetAllPathsHealthAsync             | []    |
    | GetAllPathsHealthAsync             | 5     |
    | GetAllPathsHealthAsync             | "x"   |
    | ListConfiguredPathsAsync           | (same four) |
    | ListConfiguredPathSourcesAsync     | (same four) |

Scenario Outline: an items element that is not a JSON object fails the read
  Given page 0 of <reader> answers {"pageCount":1,"items":[<element>]}
  When  the reader runs
  Then  it throws HttpRequestException whose HttpRequestError is InvalidResponse
  Examples: element = 1 | null | "cam-x" | [] (for each of the three readers)

Scenario: a malformed second page fails the whole read, not just the page (conflict)
  Given page 0 is well-formed with pageCount 2 and page 1 answers []
  When  each reader runs
  Then  it throws HttpRequestException(InvalidResponse), requests pages [0, 1], and returns nothing

Scenario: a body that is not JSON at all fails the same way (bad request from the dependency)
  Given page 0 answers 200 with "<html>proxy</html>" or an empty body
  When  each reader runs
  Then  it throws HttpRequestException(InvalidResponse) with no InnerException
  And   its Message is fixed text that may carry the parse error's line number and byte position

Scenario: the failure carries no part of the body (security)
  Given page 0 answers ["rtsp://admin:SECRET@10.0.8.1/x"]
  Or    an element "rtsp://admin:SECRET@10.0.8.1/x"
  Then  neither the exception's Message nor any InnerException's Message contains "SECRET"

Scenario: a parse error that echoes the offending literal still carries no part of the body (security, amendment)
  Given page 0 answers 200 with the body frtsp://admin:SECRET@10.0.8.1/x}]}
  # System.Text.Json reports this as "'frtsp://admin:SECRET@10.0.8.1/x}]}' is an invalid JSON literal"
  When  each reader runs
  Then  it throws HttpRequestException(InvalidResponse) with no InnerException
  And   its Message does not contain "SECRET"

Scenario: the health sweep treats a malformed page as a lost tick (the designed path)
  Given the real gateway over a MediaMTX answering [] for /v3/paths/list
  When  the health watcher dispatches one sweep
  Then  DispatchAsync returns without throwing
  And   one HealthSweepFailed Warning is logged with the HttpRequestException
  And   no health command is dispatched and no Error is logged

Scenario: the source repair fails its pass with the typed exception
  Given the real gateway over a MediaMTX answering [] for /v3/config/paths/list
  When  a repair pass runs
  Then  RepairAsync throws HttpRequestException(InvalidResponse)

Scenario: what the readers already did with well-formed shapes is unchanged (guard)
  Then  an object page with no "items" still ends the configured-paths and sources reads normally
  And   an object page with no "items" still fails the health read with today's message
  And   an object element without a string "name" is still skipped
```

### US1b (amendment)

```gherkin
Scenario Outline: a string that cannot be decoded fails the read as an invalid response
  Given page 0 of <reader> answers {"pageCount":1,"items":[<item>]}, where <field> holds <bytes>
  When  the reader runs
  Then  it throws HttpRequestException whose HttpRequestError is InvalidResponse
  And   it has no InnerException
  And   its Message contains no part of the body
  Examples:
    | reader                         | field                       | bytes                          |
    | each of the three readers      | name                        | invalid UTF-8 (0xFF)           |
    | each of the three readers      | name                        | a lone surrogate (\uD800)      |
    | ListConfiguredPathSourcesAsync | source (canonical name)     | invalid UTF-8 / lone surrogate |
    | GetAllPathsHealthAsync         | readyTime (canonical name)  | invalid UTF-8 / lone surrogate |

Scenario: an unsupported response charset fails the read without naming the charset (security)
  Given page 0 of each reader answers 200 with Content-Type "application/json; charset=x-SECRET-cs"
  When  the reader runs
  Then  it throws HttpRequestException(InvalidResponse) with no InnerException
  And   its Message does not contain "SECRET"

Scenario Outline: a wrong-kind field on a health item fails the health read
  Given page 0 of GetAllPathsHealthAsync answers one canonical item with <fields>
  When  the reader runs
  Then  it throws HttpRequestException(InvalidResponse) with no InnerException
  Examples:
    | fields                                   |
    | "ready": "yes"                           |
    | "ready": true, "source": null            |
    | "ready": true, "source": 5               |
    | "ready": true, "source": {"type": 5}     |

Scenario: the health sweep treats an undecodable string as a lost tick (the designed path)
  Given the real gateway over a MediaMTX whose /v3/paths/list item name holds invalid UTF-8
  When  the health watcher dispatches one sweep
  Then  DispatchAsync returns without throwing
  And   one HealthSweepFailed Warning is logged with the HttpRequestException
  And   nothing at Error is logged

Scenario: cancellation is still cancellation (guard)
  Given a page whose body never finishes arriving
  When  the caller's token is cancelled while the body is being read
  Then  the reader throws OperationCanceledException, not HttpRequestException

Scenario: a failure of the request itself is unchanged (guard)
  Given the handler throws while sending the request, or answers 503
  Then  the reader throws exactly what it throws today (the handler's exception; HttpRequestException 503)
```

### US2

```gherkin
Scenario: a persistently unreadable pageCount warns once, then logs Debug (happy path of the fix)
  Given one MediaMTX that answers pageCount 1.5 on every configured-sources read
  When  three reads run within 10 minutes, through gateways resolved from AddMediaMtxGateway in three scopes
  Then  exactly one MediaMtxConfiguredSourcesPageCountUnreadable Warning is logged
  And   each later read logs one Debug line saying the Warning repeats at most every 10 minutes

Scenario: the same for the page cap
  Given every page reports pageCount 2147483647 and one canonical item
  When  three reads run within 10 minutes
  Then  exactly one MediaMtxConfiguredSourcesPageCapReached Warning is logged

Scenario: the Warning repeats after 10 minutes (monotonic clock)
  Given a read that warned at t=0 for an unreadable pageCount
  When  the same outcome recurs at t=9m59s, and again at t=10m00s
  Then  the first recurrence logs Debug and the second logs the Warning again
  And   a wall-clock step alone does not shorten or lengthen the interval

Scenario: a change of outcome warns at once (conflict)
  Given a read that warned for an unreadable pageCount
  When  the next read hits the page cap instead
  Then  a PageCapReached Warning is logged at once

Scenario: a complete read resets the state
  Given reads that end unreadable, unreadable, complete, unreadable, unreadable
  Then  exactly two PageCountUnreadable Warnings are logged

Scenario: a whole-pass failure warns once per outcome, then logs Debug
  Given every sweep pass fails with the same exception type (and status / HttpRequestError, if an HttpRequestException)
  When  three passes run within 10 minutes
  Then  exactly one SourceRepairSweepFailed Warning is logged, with the exception
  And   each later pass logs one Debug line naming the exception type and status

Scenario: a different failure is a change of outcome
  Given passes that fail with A, A, B, B (different exception types)
  Then  exactly two SourceRepairSweepFailed Warnings are logged

Scenario: a successful pass resets the sweep's state (integration)
  Given passes that fail, fail, succeed, fail with the same outcome
  Then  exactly two SourceRepairSweepFailed Warnings are logged

Scenario: shutdown is still not a failure (guard)
  Given a pass in flight when the application stops
  Then  nothing at Warning or above is logged and the sweep's task is not faulted

Scenario: the per-path throttle is untouched (guard)
  Then  every existing SourceRepairFailureLog test passes unmodified
```

Auth: N/A. Both changes are inside internal background workers with no HTTP surface; the
control-API credential and the authorize hook (ADR-0171) are unchanged.

## 4. Requirements

### US1

- **FR-101** `ReadPagesAsync` fails the read with
  `new HttpRequestException(HttpRequestError.InvalidResponse, message)` when a page's parsed body is
  not `JsonValueKind.Object`, before the page reaches `addPage`. It applies to all three readers,
  because all three go through `ReadPagesAsync`.
- **FR-102** Each of the three item loops (`AddPageToSources`, `AddPageToPaths`, `AddPageToHealth`)
  fails the read the same way when an `items` element is not `JsonValueKind.Object`. An object
  element without a string `name` is still skipped, as today. **[ASSUMPTION A1]**
- **FR-103** (amended) **[ASSUMPTION A3]** A body that is not valid JSON (`ReadFromJsonAsync` throws
  `JsonException`) fails the read the same way, with **no `InnerException`**. The fixed message may
  carry the `JsonException`'s `LineNumber` and `BytePositionInLine` (numbers only), never its
  `Message`. *Was:* "with the `JsonException` as `InnerException`" — withdrawn, see §12.
- **FR-104** (amended) No exception built by FR-101..FR-103 or FR-110..FR-112 carries any part of
  the response body or of a response charset name: neither its `Message` nor the `Message` of any
  `InnerException` (there is none, FR-114). It names the list endpoint and, where known, the page
  index (spec 373 FR-008: what a log carries from MediaMTX is the origin at most; a body can echo a
  source URL with credentials).
- **FR-105** A failure under FR-101..FR-103 returns no partial result: the reader throws. The
  existing behaviour of an object page whose `items` is missing or not an array is unchanged
  (configured readers: the read ends normally; health reader: today's `HttpRequestException`).
- **FR-106** No caller changes. The health sweep's existing `catch (HttpRequestException)`, the
  sweep's `catch (Exception)`, and the startup reconcile's catch receive the typed exception.

### US1b (amendment)

- **FR-110** Any `InvalidOperationException`, `NotSupportedException` or `JsonException` thrown while a page's body is read
  and parsed (`ReadFromJsonAsync<JsonElement>`) fails the read with
  `HttpRequestException(HttpRequestError.InvalidResponse, fixed text)`. Observed triggers: invalid
  JSON (`JsonException`); an unsupported `Content-Type` charset (`InvalidOperationException` whose
  inner `ArgumentException` copies the charset string); `charset=utf-7` (`NotSupportedException`, "Support for UTF-7 is disabled", SYSLIB0001, thrown by the encoding lookup before any parse).
- **FR-111** Any `InvalidOperationException`, `NotSupportedException` or `JsonException` thrown by a page's fold callback
  (`addPage`: `AddPageToSources`, `AddPageToPaths`, the health reader's closure with
  `AddPageToHealth` / `ParsePathHealth`) fails the read the same way, naming the list and the page
  index. Observed triggers: invalid UTF-8 or a lone surrogate in a string read with `GetString()`
  (`name`, `TryReadSource`'s `source`, `TryReadIsoTimestamp`'s `readyTime`;
  `InvalidOperationException` wrapping `DecoderFallbackException`, whose message quotes the bytes);
  a non-boolean `ready` (`GetBoolean`); a ready item whose `source` is not an object
  (`TryGetProperty`); a ready item whose `source.type` is not a string (`GetString` in
  `DetectTranscodeMode`).
- **FR-112** An `HttpRequestException` thrown inside the fold (US1's `EnsureObjectItem`) passes
  through unchanged; it is not an `InvalidOperationException`.
- **FR-113** `OperationCanceledException` (and `TaskCanceledException`) passes through both wrappers
  unchanged. Neither derives from `InvalidOperationException`, so no filter is needed, but the guard
  scenario pins it.
- **FR-114** No exception built under FR-103, FR-110 or FR-111 has an `InnerException`: every message
  System.Text.Json, the decoder or the encoding lookup produces may copy body text or the charset
  name verbatim, with no length limit, and `HealthSweepFailed` logs the exception (with its chain) to
  OTLP every 2 s.
- **FR-115** The request itself is outside both wrappers: an exception from `http.GetAsync` (the
  handler, the resilience pipeline) or from `EnsureSuccessStatusCode` is unchanged. In particular a
  test stub's own `InvalidOperationException` from `SendAsync` is not remapped.

### Shared (amendment)

- **FR-301** `AddMediaMtxGateway` registers `TryAddSingleton(TimeProvider.System)` beside its
  `MediaMtxConfiguredSourcesReadWarnings` singleton, so the registration resolves on its own. In
  the module it is a no-op (`AddClockAndMediaMtxAccessServices` registers `TimeProvider` first), so
  the registration characterisation (C3) does not change.

### US2

- **FR-201** A whole-read Warning (the two configured-sources page Warnings and
  `SourceRepairSweepFailed`) is logged when its outcome is **first seen**, when it **differs** from
  the last one logged, or when **10 minutes** have passed since that Warning was last logged, on
  `TimeProvider`'s monotonic timestamp. Otherwise a Debug line is logged instead. This is spec 388
  FR-002 / FR-006's rule with the same interval.
- **FR-202** The outcome of a configured-sources read is its `PageReadEnd`
  (`PageCountUnreadable` or `PageCapReached`). The page index is not part of it. **[ASSUMPTION A2]**
- **FR-203** The outcome of a sweep failure is the exception's runtime type plus, for an
  `HttpRequestException`, its `StatusCode` and `HttpRequestError` (identical to spec 388 FR-002).
- **FR-204** A configured-sources read that ends `Complete` clears its state. A sweep pass that
  returns from `RepairAsync` clears the sweep's state. No recovery line is logged.
  **[ASSUMPTION A4]**
- **FR-205** The configured-sources state survives across gateway instances: it is held by one DI
  singleton that every `MediaMtxRtspGateway` resolved from `AddMediaMtxGateway` shares. It is
  thread-safe, because the startup reconcile's repair and the sweep's first pass can overlap. A
  gateway constructed directly with the existing two-argument constructor gets its own state (tests
  and any manual construction keep their per-instance behaviour).
- **FR-206** The sweep's state lives in the singleton `MediaMtxSourceRepairSweep`; its passes are
  sequential (`PeriodicTimer`).
- **FR-207** The three Warning templates (`Log.cs:46-50`, `:58-59`) are not reworded. Three Debug
  templates are added, one per Warning, each saying the Warning repeats at most every 10 minutes.
  The page Debug lines carry the same argument as their Warning (page index, page cap); the sweep
  Debug line carries the exception type name and status text, like
  `ReconcilerStillFailingToRepairPathSource`.
- **FR-208** Shutdown semantics are unchanged: the sweep's catch filter
  `when (!stoppingToken.IsCancellationRequested)` stays exactly as it is.
- **FR-209** `SourceRepairFailureLog`, `MediaMtxSourceRepair`, `IRtspGateway`, `Shared.Contracts`,
  `ListConfiguredPathsAsync`'s two Warnings (`MediaMtxConfiguredPathsPageCountUnreadable` /
  `...PageCapReached`, startup-only, once per boot) and `HealthSweepFailed` are not changed.

## 5. Non-goals (each recorded so the next reader does not re-derive it)

**Correction (amendment, §12).** The first version listed "`ParsePathHealth` on wrong-kind fields"
here and treated a non-boolean `ready` or a non-object `source` as the only way left for a list read
to reach `InvalidOperationException`. That was **false**: an undecodable string in `name`, `source`
or `readyTime`, an unsupported response charset, and a non-string `source.type` on a ready item all
reach it too (FR-110, FR-111). One wrapper covers every one of them, including the two formerly
listed here, so that item is now **in scope** (US1b) and removed from this list.

Non-goals of this spec's defect class, now exactly three:

- **Bounding a page's body size** (item 3's second sentence). It needs a byte limit nobody has
  chosen and interacts with #3021 item 2's cap sizing. **Owner decision**; recommend a follow-up issue.
- **Throttling `HealthSweepFailed`** (~43k a day while MediaMTX stays hostile, #3021's "Related"
  note). Same rule would fit, but it is not one of item 2's three Warnings and it is the health
  watcher, not the source repair. Recommend a follow-up.
- **`GetPathHealthAsync`** (the single-path read, `MediaMtxRtspGateway.cs:157-174`). It parses with
  the same `ParsePathHealth` and is exposed to the same triggers, but nothing in `src` calls it (only
  `IRtspGateway` declares it). Fixing an unused path is speculative; whoever brings it back into use
  inherits FR-110..FR-114.

Out of scope and owned elsewhere (unchanged from the first version, not this defect class):

- **A deadline for the confirmation read or a whole read** (#3021 item 3). Operational-budget
  decision; the lane may not make it.
- **The pre-existing post-cancel 404 escape** (#3013 comment). Shutdown only; unchanged.
- **A Warning for malformed `items` on the configured-paths read** — #3021 item 4.

## 6. Assumptions (disclosed)

- **[ASSUMPTION] A1 (FR-102).** A non-object element **fails** the read rather than being skipped,
  as the issue and the dispatch ask. For the health reader a skip would silently report a camera as
  unregistered. For the two configured readers a skip would be fail-safe (a skipped path is neither
  compared nor removed), but one rule for all three keeps `ReadPagesAsync`'s readers alike and the
  answer is not MediaMTX's shape at all.
- **[ASSUMPTION] A2 (FR-202).** A changed page index for `PageCountUnreadable` is not a new outcome:
  a MediaMTX reporting `1.5` on page 2, then on page 3, is the same fault.
- **[ASSUMPTION] A3 (FR-103, amended).** Unparseable JSON is in scope: it is the most likely real
  form of "a page that is not an object" (a proxy's HTML error page with status 200), and today it
  escapes the health sweep as `JsonException` → Error, the same defect. Strike FR-103 and R4 if the
  owner disagrees; nothing else depends on them.
  *The first version also assumed that the wrapped `JsonException` was safe to attach because its
  message names "at most the offending character and position". That is withdrawn as wrong:*
  System.Text.Json copies the offending token verbatim with no length limit (body
  `frtsp://admin:SECRET@10.0.8.1/x}]}` gives `'frtsp://admin:SECRET@10.0.8.1/x}]}' is an invalid JSON
  literal`), and the same holds for the decoder's and the charset lookup's messages. Replaced by a
  rule, not an assumption: no exception built here has an `InnerException`, and neither its
  `Message` nor any inner message may contain any part of the body or of an attacker-chosen charset
  name (FR-104, FR-114).
- **[ASSUMPTION] A4 (FR-204).** No "recovered" line, matching `SourceRepairFailureLog`. The next
  `ReconcilerRepairedPathSource` or a quiet sweep is the recovery signal today.

## 7. ADR check

**No new ADR.** US1 maps a malformed dependency answer to the typed failure the same readers already
use (`CompleteHealthOrThrow`, `itemsUnreadablePage`). US2 applies the throttle rule spec 388 already
decided (owner decision on #2994) to three more log sites, with the same interval; the owner enrolled
item 2 knowing it proposes "the same transition-plus-interval idea". No architecture, store, budget
or contract decision is made. No stored data is touched.

## 8. Accepted residuals (in writing)

| Residual | Bound | Why accepted |
|---|---|---|
| A malformed page now logs `HealthSweepFailed` (Warning) every 2 s instead of `StreamHealthWatcherPollFailed` (Error) every 2 s | Same rate, lower level, the designed message | Throttling it is a non-goal (§5); the level is now the one the code was designed for |
| Between Warnings a persistent fault logs one Debug line per read/pass | 8,640 Debug lines a day per Warning at 10 s | Same trade spec 388 accepted for a path |
| A gateway built with the two-argument constructor does not share the throttle | Tests and manual construction only; DI uses the three-argument constructor (R14 proves it) | Keeps 14 existing test construction sites unchanged |
| The configured readers' exception type changes for the startup reconcile and the sweep | Both catch `Exception`; only the logged exception's type differs | The typed failure is the point of US1 |

## 9. Independent end-to-end test procedure

Unit level (no Docker): the `StreamDistribution.Infrastructure.Tests` filters in tasks.md drive the
real gateway over a stub `HttpMessageHandler`, the real health watcher's `DispatchAsync`, the real
source repair over the real gateway, the real sweep over failing scope factories, and the real
`AddMediaMtxGateway` registration resolved in two scopes. Integration (Aspire fixture,
CI-authoritative): R18 runs the real sweep against Postgres with a list read that fails, fails,
succeeds, fails, and observes exactly two Warnings.

## 10. Latency budget

**N/A.** The source repair (10 s) and the health sweep (2 s) are background workers, not on the
event → overlay path (constitution §IV). US1 adds one `ValueKind` comparison per page and per
element; US2 one lock and one timestamp read per non-complete read or failed pass.

## 11. Acceptance

1. Every scenario in §3 has a test; each new-behaviour test is observed red first (ADR-0139), or, for
   the two tests needing new surface (R10, R15), its compile error is quoted (spec 388 R4 precedent).
2. Exactly the existing assertions listed in plan §6 (C1-C4) change, each named in the PR body with
   its reason; any other existing assertion that has to change is a **BLOCK**.

## 12. Amendment log

**2026-10-18, after the security review and the backend review of commits `27383347..c83f5132`.**
Decisions recorded here are final for this spec; they are not to be relitigated in phase 4.

| # | Change | Why |
|---|---|---|
| A | FR-103 / FR-104 / A3: no `InnerException`; the fixed text may carry `LineNumber` / `BytePositionInLine` | System.Text.Json messages copy the offending token verbatim, unbounded, and `HealthSweepFailed` ships the exception chain to OTLP every 2 s (security review) |
| B | US1b, FR-110..FR-115: every `InvalidOperationException` / `JsonException` from `ReadFromJsonAsync` and from the fold maps to `HttpRequestException(InvalidResponse)` | The first version's §5 claim that only a non-boolean `ready` or non-object `source` was left was false (backend review: undecodable strings, unsupported charset, non-string `source.type`) |
| B' | §5 shrinks to three non-goals; "`ParsePathHealth` on wrong-kind fields" moves into scope | The same wrapper covers it |
| C | FR-301: `TryAddSingleton(TimeProvider.System)` in `AddMediaMtxGateway` | Backend-review nit, decided: the registration resolves on its own; a no-op in the module |

Deviations of the first implementation from plan.md, accepted as recorded in plan §9.
