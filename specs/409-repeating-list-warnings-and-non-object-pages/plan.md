# Plan 409: A non-object MediaMTX page fails the read as an invalid response, and the source repair's whole-read Warnings repeat at most every 10 minutes

**Spec**: [spec.md](./spec.md). **Issue**: #3013 (items 2 and 3; item 1 delivered by #3041).

## 1. Context and layers

- **Bounded context**: StreamDistribution. **Layer**: Infrastructure only. No Domain, Application,
  Api, `Shared.Contracts`, AppHost or other-context change.
- **Files that change** (`src/StreamDistribution/Infrastructure/`):
  - US1: `Gateways/MediaMtxRtspGateway.cs` and, as built, its new partial
    `Gateways/MediaMtxRtspGateway.PageReading.cs` (§9 D1).
  - US1b (amendment): `Gateways/MediaMtxRtspGateway.PageReading.cs` only.
  - FR-301 (amendment): `StreamDistributionInfrastructureModule.cs` (one line).
  - US2: `Gateways/MediaMtxRtspGateway.cs`, new `WarningRepeatGate.cs`, new
    `Gateways/MediaMtxConfiguredSourcesReadWarnings.cs`, `Reconciler/MediaMtxSourceRepairSweep.cs`,
    `Log.cs` (three Debug templates added; no existing template touched),
    `StreamDistributionInfrastructureModule.cs` (one singleton registration in `AddMediaMtxGateway`).
- **Files that must not change**: `IRtspGateway.cs`, `MediaMtxSourceRepair.cs`,
  `SourceRepairFailureLog.cs`, `StreamHealthWatcher.cs`, `MediaMtxReconciler.cs`,
  `IStreamSourceSnapshot.cs` / `StreamSourceSnapshot.cs`, every existing `Log.cs` template, AppHost.

**Concurrent work on the same file.** Worktree `D:\Github\sse-3021` (branch
`fix/3021-list-read-followups`, #3021 items 2-5) is cut from the same `develop` and is likely to touch
`MediaMtxRtspGateway.cs` (`ReadPagesAsync`, `MaxListPages`). Whichever PR lands second rebases;
US1's change is a few lines at the top of `ReadPagesAsync`'s loop body and in three item loops, so a
conflict is mechanical, but the second branch must re-run the gateway test filter after rebasing.

## 2. Types and invariants

### Reused
`MediaMtxPath`, `PageReadEnd` / `PageRead` (private to the gateway), `TimeProvider`,
`ManualTimeProvider` (tests), `CapturingLogger<T>`, the `PagesHandler` stub pattern, the sweep's
`ScopeProbe` / `SlowContextFactory`, the integration class's `ListFailingGateway`.

### New
- **`WarningRepeatGate<TOutcome>`** — `internal sealed`, `where TOutcome : struct, IEquatable<TOutcome>`,
  constructed with a `TimeProvider`. One slot: the last warned outcome and its monotonic timestamp.
  - `bool ShouldWarn(TOutcome outcome)`: true (and records outcome + `GetTimestamp()`) when the slot
    is empty, holds a different outcome, or `GetElapsedTime(last) >= 10 min`; false otherwise.
  - `void Clear()`.
  - Thread-safe with a private `lock` (`System.Threading.Lock`), because one instance is a DI
    singleton shared by every gateway (FR-205). No `static` mutable state.
  - Interval: a `private static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(10)` —
    the same value as `SourceRepairFailureLog`, deliberately **not** shared with it (that type is
    out of scope; folding it onto the gate is a separate behaviour-preserving refactor, not this PR).
- **`MediaMtxConfiguredSourcesReadWarnings`** — `public sealed` (it is a public constructor's
  parameter type, and `ActivatorUtilities` only sees public constructors), constructed with a
  `TimeProvider`. Wraps one `WarningRepeatGate<PageReadEnd-equivalent>`; its members that take the
  outcome are `internal`. The engineer may move `PageReadEnd` to an `internal` type in the
  `Gateways` namespace so the gate can be keyed on it; it stays non-public.
- **Sweep outcome** — `private readonly record struct SweepFailure(Type ExceptionType, HttpStatusCode? Status, HttpRequestError? Error)`
  inside `MediaMtxSourceRepairSweep`, the same shape as `SourceRepairFailureLog.Outcome`
  (spec 388 FR-002). `record struct` gives the `IEquatable` the gate needs.

### Invariants
- A malformed page (non-object body, non-object element, unparseable JSON, an undecodable string,
  an unsupported charset, a wrong-kind health field) never surfaces as anything but
  `HttpRequestException` with `HttpRequestError.InvalidResponse` (US1, US1b).
- No exception built here contains response-body content or a charset name, and none has an
  `InnerException` (FR-104, FR-114).
- Cancellation and request-level failures are never remapped (FR-113, FR-115).
- Each throttled Warning fires on first / changed outcome or after 10 monotonic minutes; a complete
  read or a returned pass clears the state (US2).

## 3. Design

### 3.1 US1 — `MediaMtxRtspGateway`

1. **Body kind** — in `ReadPagesAsync`, immediately after the payload is read and before `addPage`:
   ```
   if payload.ValueKind != Object
       throw new HttpRequestException(InvalidResponse,
           $"MediaMTX list {listPath} page {page} is not a JSON object.")
   ```
2. **Unparseable JSON** (FR-103, [ASSUMPTION A3]) — *superseded by §3.5 (amendment)*. As first
   built, the `ReadFromJsonAsync` call caught `JsonException` and attached it as the inner exception,
   on the belief that its message names at most a character and a position. It copies the token
   verbatim (spec §6 A3), so §3.5 drops the inner exception and widens the catch.
3. **Element kind** — in each of `AddPageToSources`, `AddPageToPaths`, `AddPageToHealth`, the first
   statement of the `foreach` body: a non-`Object` element throws
   `HttpRequestException(InvalidResponse, "... has an items element that is not a JSON object.")`.
   One private static helper (e.g. `EnsureObjectItem(JsonElement item, string listName)`) is called
   from all three, so the rule and the wording exist once. These methods do not know the page index;
   the message names the list, which is enough (FR-104).
4. Nothing else in the readers moves: `CompleteHealthOrThrow`, the health reader's
   `itemsUnreadablePage` check, the configured readers' "missing `items` ends the read" and the
   skip of a nameless element are unchanged (FR-105, guards G1-G3).

Constraints: no new `catch` of anything but `JsonException`; no `Ensure.That` added on private
methods; each touched method stays within ADR-0084's advisory limits (≤ 30 LOC, depth ≤ 3).
`ReadPagesAsync` is closest; if the `try/catch` pushes it over, extract a `ReadPageAsync(listPath,
page, ct) : Task<JsonElement>` that does the GET, `EnsureSuccessStatusCode`, the parse and the
two US1 checks.

### 3.5 US1b (amendment) — one wrapper for every untyped parse failure

All in `Gateways/MediaMtxRtspGateway.PageReading.cs` (the partial, plan §9 D1).

1. **Parse** — in `ReadPageAsync`, the `try` around `ReadFromJsonAsync<JsonElement>` only (the
   `GetAsync` and `EnsureSuccessStatusCode` stay **outside** it, FR-115):
   ```
   catch (JsonException ex)        -> throw InvalidPage(listPath, page,
                                         $"is not valid JSON (line {ex.LineNumber ?? "-"}, byte {ex.BytePositionInLine ?? "-"})")
   catch (InvalidOperationException) -> throw InvalidPage(listPath, page, "could not be decoded")
   ```
   No `ex` is passed on. `OperationCanceledException` / `TaskCanceledException` are not
   `InvalidOperationException`, so they pass through with no filter (FR-113). The second catch also
   covers `ObjectDisposedException` (an `InvalidOperationException`); that is a malformed read too and
   is accepted.
2. **Fold** — in `ReadPagesAsync`, `addPage(payload)` is called through a small private helper (e.g.
   `static bool FoldPage(Func<JsonElement, bool> addPage, JsonElement payload, string listPath, int page)`)
   so `ReadPagesAsync` stays within ADR-0084's depth and length limits:
   ```
   try   { return addPage(payload); }
   catch (InvalidOperationException) -> throw InvalidPage(listPath, page, "has an item field of the wrong kind or an undecodable string")
   catch (JsonException)             -> same
   ```
   An `HttpRequestException` from `EnsureObjectItem` or anything else in the fold is not caught
   (FR-112). This covers `ParsePathHealth`, `TryReadSource`, `TryReadIsoTimestamp`,
   `DetectTranscodeMode` and every `name.GetString()` without touching them, which is the point: no
   per-field check is added, so a future `GetXxx()` call is covered too.
3. **One builder** — `static HttpRequestException InvalidPage(string listPath, int page, string reason)`
   returns `new HttpRequestException(HttpRequestError.InvalidResponse, $"MediaMTX list {listPath} page {page} {reason}.")`.
   US1's non-object-body throw moves onto it. `EnsureObjectItem` keeps its own text (it does not
   know the page index) and already has no inner exception.
4. **Not touched**: `GetPathHealthAsync` (spec §5), `ParsePathHealth` and its helpers, the item
   loops beyond US1's `EnsureObjectItem`, every caller.

Constraints: no `catch (Exception)`; no `when` filter needed; no logging added in the gateway.

### 3.6 FR-301 — `TimeProvider` in `AddMediaMtxGateway`

`services.TryAddSingleton(TimeProvider.System);` immediately before
`services.AddSingleton<MediaMtxConfiguredSourcesReadWarnings>();`
(`Microsoft.Extensions.DependencyInjection.Extensions`). The module registers `TimeProvider` in
`AddClockAndMediaMtxAccessServices` (`StreamDistributionInfrastructureModule.cs:52`, `:165`) before it
calls `AddMediaMtxGateway` (`:56`), so `TryAdd` adds no descriptor there and C3's ordered list is
**unchanged** — that characterisation passing unmodified is the evidence.

### 3.2 US2a — configured-sources page Warnings

- `MediaMtxRtspGateway` gains a public constructor
  `(HttpClient http, ILogger<MediaMtxRtspGateway> logger, MediaMtxConfiguredSourcesReadWarnings readWarnings)`
  marked **`[ActivatorUtilitiesConstructor]`** so the typed-client factory picks it unambiguously.
  The existing two-argument constructor stays and chains with
  `new MediaMtxConfiguredSourcesReadWarnings(TimeProvider.System)` — the same overload pattern as
  `MediaMtxSourceRepair(ILogger)`. The primary constructor syntax may have to become an explicit
  constructor pair; the engineer chooses, mirroring the file's style.
- `ListConfiguredPathSourcesAsync`'s `switch (read.End)`:
  ```
  Complete           -> readWarnings.Clear()
  PageCountUnreadable-> ShouldWarn(...) ? logger.MediaMtxConfiguredSourcesPageCountUnreadable(page)
                                         : logger.MediaMtxConfiguredSourcesPageCountStillUnreadable(page)
  PageCapReached     -> same shape with ...PageCapReached / ...PageCapStillReached(MaxListPages)
  ```
  `ListConfiguredPathsAsync`'s switch is **not** touched (FR-209).
- `AddMediaMtxGateway` registers `services.AddSingleton<MediaMtxConfiguredSourcesReadWarnings>()`
  (its `TimeProvider` comes from the `TimeProvider.System` singleton the module already registers).
  Place it right after the `AddHttpClient` call so the registration-order characterisation gains one
  line next to `Transient IRtspGateway -> factory` (C3).

### 3.3 US2b — `SourceRepairSweepFailed`

- `MediaMtxSourceRepairSweep` gets a field `WarningRepeatGate<SweepFailure> failures = new(timeProvider)`
  (it already receives `TimeProvider`; constructor unchanged).
- `RunPassAsync`: after `await repair.RepairAsync(...)` returns, `failures.Clear()`. In the existing
  `catch (Exception ex) when (!stoppingToken.IsCancellationRequested)`:
  ```
  outcome = new SweepFailure(ex.GetType(), (ex as HttpRequestException)?.StatusCode, (ex as HttpRequestException)?.HttpRequestError)
  if failures.ShouldWarn(outcome) logger.SourceRepairSweepFailed(ex)
  else logger.SourceRepairSweepStillFailing(outcome.ExceptionType.Name, status text or "-")
  ```
  The status text is formatted as in `SourceRepairFailureLog.StatusText` (invariant culture, `-` for
  none). The catch filter is unchanged (FR-208).

### 3.4 `Log.cs` — three Debug templates, appended next to their Warnings

| Method | Level | Message |
|---|---|---|
| `MediaMtxConfiguredSourcesPageCountStillUnreadable(int page)` | Debug | `MediaMTX config path list still reports a pageCount that is not a 32-bit integer (page {Page}); the source repair compares only the paths read so far, and the Warning repeats at most every 10 minutes.` |
| `MediaMtxConfiguredSourcesPageCapStillReached(int pageCap)` | Debug | `MediaMTX config path list still reports more pages after {PageCap} pages; the source repair compares only the paths read so far, and the Warning repeats at most every 10 minutes.` |
| `SourceRepairSweepStillFailing(string exceptionType, string status)` | Debug | `MediaMtxSourceRepairSweep pass still failing ({ExceptionType} {Status}); the Warning repeats at most every 10 minutes.` |

The test wording to assert on is "repeats at most every 10 minutes" plus the event name; exact
wording beyond that is the engineer's, provided it carries no response content.

## 4. Messaging

None. No domain or integration event; no `Shared.Contracts` change.

## 5. Boundary rules

Infrastructure-internal. No cross-context reference; NetArchTest green unmodified.
`PrimitiveBoundaryTests` unaffected (no domain model). `StreamDistributionRegistrationCharacterisationTests`
changes by exactly the one line in C3. `HandlerDeconstructionTests` unaffected (no handler).

## 6. Existing assertions this change contradicts

Found by `grep -rn "InvalidOperationException\|not_an_object\|warns_on_every_read\|ExpectedRegistrations" tests/StreamDistribution.Infrastructure.Tests tests/Architecture.Tests tests/Integration.Tests/StreamDistribution`
and by reading every test that reads the configured sources more than once on one gateway or runs
more than one failing sweep pass. Phase 4a re-runs that grep, plus a search for any stub body whose
`items` holds a non-object element (`"items":[1`, `"items":[null`, `"items":["`), before writing
tests; a hit not listed here is a **BLOCK** to report, not an edit to make.

| # | Test (file:line) | Assertion | Under this change | Retire justified? |
|---|---|---|---|---|
| C1 | `MediaMtxRtspGatewayConfiguredSourcesFoldCharacterisationTests.A_body_that_is_not_an_object_throws_an_invalid_operation_exception` (`tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayConfiguredSourcesFoldCharacterisationTests.cs:103-112`) | `Should.ThrowAsync<InvalidOperationException>` | **Fails** (US1) | **Yes.** Spec 405 A1 says it "pins today's behaviour and does not endorse it", and names this exact change as a separate behaviour issue. Rename to `A_body_that_is_not_an_object_fails_the_read_as_an_invalid_response`, assert `HttpRequestException` with `HttpRequestError.InvalidResponse`; `RequestedPages.ShouldBe([0])` holds unchanged. Update the class summary's last sentence to say the body case now pins the typed failure (spec 409) |
| C2 | `MediaMtxRtspGatewayConfiguredSourcesBoundsAdversarialTests.A_persistently_unreadable_page_count_warns_on_every_read` (`…/MediaMtxRtspGatewayConfiguredSourcesBoundsAdversarialTests.cs:180-192`) | `Warnings().Count.ShouldBe(3, "one Warning per read: at a 10 s interval that is 8,640 a day …")` | **Fails** (US2: 1 Warning) | **Yes.** It pins the exact defect #3013 item 2 names, and its reason string says so. Rename to `A_persistently_unreadable_page_count_warns_once_then_logs_debug`; expect `Warnings().Count.ShouldBe(1)` and two Debug entries containing "repeats at most every 10 minutes". This edited test **is** R11, and it is red against today's code |
| C3 | `StreamDistributionRegistrationCharacterisationTests.ExpectedRegistrations` (`tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs:62-106`) | Exact ordered list | **Fails** once §3.2's registration lands | **Yes.** It characterises spec 351's refactor; a new registration is additive. Insert exactly `"Singleton MediaMtxConfiguredSourcesReadWarnings -> MediaMtxConfiguredSourcesReadWarnings"` immediately after `"Transient IRtspGateway -> factory"`. No other line moves. Lands in the **fix** commit (it is green before the fix and fails only after it) |

**Guards that must pass unmodified** (they pin what this change keeps):
- G1 `MediaMtxRtspGatewayConfiguredSourcesBoundsAdversarialTests` `:150-178` (a cut read then a whole
  read on one gateway: 1 Warning). After US2 still 1 (unreadable, then complete).
- G2 Every test in `MediaMtxRtspGatewayHealthReadCharacterisationTests`,
  `MediaMtxRtspGatewayListReadersCharacterisationTests`, `MediaMtxRtspGatewayListReadersBoundsTests`,
  `MediaMtxRtspGatewayPaginationTests`, `MediaMtxRtspGatewayConfiguredSourcesTests`,
  `MediaMtxRtspGatewayConfiguredSourcesBoundsTests` and the rest of the fold characterisation: all
  feed object bodies with object elements, or a single read per gateway.
- G3 `MediaMtxSourceRepairSweepTests.A_failing_pass_is_logged_and_the_next_tick_still_runs` and every
  `MediaMtxSourceRepairSweepAdversarialTests` fact: they assert `ShouldContain` a Warning, or
  nothing ≥ Warning on shutdown, or tick counts — all hold with one Warning.
- G4 Every `SourceRepairFailureLog` / `MediaMtxSourceRepair*` test and
  `MediaMtxSourceRepairLogVolumeIntegrationTests`' existing facts.
- G5 `StreamHealthWatcherCutShortReadTests`' existing facts.

**Amendment row** (the assertion is this branch's own, unmerged, but it changes because the spec
changed, so it is recorded like the others):

| # | Test (file:line) | Assertion | Under this change | Retire justified? |
|---|---|---|---|---|
| C4 | `MediaMtxRtspGatewayPageShapeTests.A_body_that_is_not_json_fails_the_read_as_an_invalid_response_wrapping_the_parse_error` (`tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayPageShapeTests.cs:93-110`) | `failure.InnerException.ShouldBeOfType<JsonException>()` | **Fails** (FR-103 amended) | **Yes.** It pins exactly the decision §12 A withdraws. Rename to `A_body_that_is_not_json_fails_the_read_as_an_invalid_response_without_an_inner_exception`; assert `InnerException.ShouldBeNull()`. This edited test **is** R20 |

C1-C4 are the **only** permitted edits to existing assertions. Each is named in the PR body with this
table's reason. C3 is **not** touched again by §3.6.

Amendment grep, re-run in phase 4a before writing tests: `grep -rn "InnerException\|InvalidOperationException\|charset\|GetBoolean" tests/StreamDistribution.Infrastructure.Tests/Gateways tests/StreamDistribution.Infrastructure.Tests/HealthWatcher`.
Known hits that are **not** contradictions: every stub handler's own
`throw new InvalidOperationException("unbounded read" | "request had no URI")` is thrown from
`SendAsync`, outside both wrappers (FR-115), so those tests keep their behaviour. A hit beyond C4
and those is a **BLOCK** to report.

## 7. Tests (phase 4a: RED)

Specified in tasks.md: R1-R7 (US1), R10-R18 (US2), and for the amendment R20-R28 (US1b, FR-301;
R25-R26 are guards). All observed failing on assertions against
unchanged production code, except R10 and R15, which need new surface (`WarningRepeatGate`, the
three-argument gateway constructor): they are written in the working tree, their compile error is
quoted, and they land with the fix (spec 388 R4 precedent). Guards G1-G5 above pass before and after.

## 8. Risks

| Risk | Mitigation |
|---|---|
| The typed-client factory picks the two-argument constructor and the throttle silently never shares state | `[ActivatorUtilitiesConstructor]`; R14 resolves two gateways from the real `AddMediaMtxGateway` in two scopes and requires one Warning across both |
| A shared singleton gate races between the startup repair and the first sweep pass | Lock inside `WarningRepeatGate` (FR-205) |
| An exception message echoes a credential from the body | FR-104 / FR-114: no inner exception at all; R5, R21 and R23 assert "SECRET" is absent from the whole chain, R20-R23 that `InnerException` is null |
| The new `catch (InvalidOperationException)` swallows a cancellation | It cannot (`OperationCanceledException` is not one); R25 pins it |
| The wrapper remaps a request failure or a test stub's own exception | The `try` covers only `ReadFromJsonAsync` and `addPage`, not `GetAsync` (FR-115); R26 plus every existing "unbounded read" test pin it |
| Losing the inner exception loses diagnosis | The message keeps the list, page, and (for JSON) line and byte position; the body itself is what must not be logged |

## 9. Deviations of the first implementation (accepted, recorded)

| # | Deviation from this plan | Why | Effect |
|---|---|---|---|
| D1 | US1's page reading moved into a new partial, `Gateways/MediaMtxRtspGateway.PageReading.cs` | Sonar S104 (300 LOC per file, ADR-0084) on `MediaMtxRtspGateway.cs` | Shape only; `public sealed partial class`, no member visibility changed |
| D2 | `WarningRepeatGate`'s interval is an instance `readonly` field, not `private static readonly` | Sonar S2743: a static field in a generic type is one copy per closed type, which reads as shared but is not | Same value (10 min) per instance; no behaviour difference |
| D3 | `MediaMtxConfiguredSourcesReadWarnings` keys its gate on a private `readonly record struct Outcome(PageReadEnd End)` rather than on `PageReadEnd` | An enum does not implement `IEquatable<T>`, which the gate's constraint requires | Equality is the enum's; no behaviour difference |
| D4 | `ReadPageAsync` (`using var response`) now disposes the response before `addPage` runs | Extraction of `ReadPageAsync` (§3.1's own fallback) | Harmless: `ReadFromJsonAsync<JsonElement>` returns an element that does not reference the response stream |
| Rebase conflict with #3021's branch in `MediaMtxRtspGateway.cs` | §1 note; second lander re-runs the gateway filter |
| A Debug line per tick is noise at Debug | Same trade spec 388 accepted for a path's repeats (`ReconcilerStillFailingToRepairPathSource`) |
