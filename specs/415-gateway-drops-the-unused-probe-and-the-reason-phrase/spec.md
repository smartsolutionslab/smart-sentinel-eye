# Spec 415: The MediaMTX gateway drops its unused single-path probe, and a refused control-API call no longer carries the server's reason phrase

**Feature Branch**: `fix/3013-source-repair-nongoals` (cut from `origin/develop` at `98834b73`)

**Created**: 2026-10-19

**Status**: Specified, planned and tasked. **Delivered on this branch: US2 only (PR-B). US1 (removing `GetPathHealthAsync`) was DEFERRED to the owner** because it needs deleted tests (A1), which the lane may not do; it was then delivered by spec 419 (#3013), which meets SC-1. US2 replaced the probe's `:167` site too. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked **[ASSUMPTION]** in §6; the one point the owner may want to veto is §6 A1.

**Input**: Issue [#3013](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3013), the
non-goals left open by spec 409 (§5) and recorded in PR #3050's body: "`GetPathHealthAsync`
(nothing in `src` calls it; it still echoes body text) and the reason-phrase echo from
`EnsureSuccessStatusCode` (pre-existing)".

**Lane scope (owner, #3013 comment 2026-10-19T06:29Z), recorded and not re-opened:** the listed
non-goals only where they are mechanical and decision-free. The `HealthSweepFailed` throttle and a
byte limit on one page's body need a limit chosen; dropped unless an existing ADR or spec fixes the
number. **None does** (§5), so both are dropped and reported.

**ADRs**: ADR-0050 (MEL + OTLP: an exception's message reaches the one telemetry sink), ADR-0118
(one sink per environment), ADR-0052 / ADR-0053 / ADR-0103 (hand-written fakes, sentence-style
names, no Testcontainers), ADR-0105 (`Ensure.That` guards), ADR-0084 (advisory metrics; the gateway
file is over 300 LOC today), ADR-0139 / ADR-0144 (red first or characterisation; the lane
implements, it does not decide), ADR-0036 (smallest change). Decision this spec implements rather
than makes: **spec 409 FR-104** ("what a log carries from MediaMTX is the origin at most").
Neighbours: spec 331 (the probe's log volume), 388, 391, 402, 405,
409, 410.

**No new ADR is needed.** `IRtspGateway` is a StreamDistribution Domain port, not a
`Shared.Contracts` type, and not one of constitution §IX's strategy interfaces (its doc comment
cites "ADR-0061 forward-compatible interfaces", but ADR-0061 is *Moq confirmed*; the citation is
stale and is corrected in passing, FR-105). No other context references it.

**Latency budget (constitution §IV)**: **N/A.** Every touched call is the control plane
(provisioning, repoint, retire, startup reconcile, source repair, health sweep), off the
event-to-overlay path.

---

## 1. Premise check (develop at `98834b73`)

| # | Claim | Verified on develop | Holds? |
|---|---|---|---|
| P1 | `GetPathHealthAsync` is unused | Declared `IRtspGateway.cs:46`, implemented `MediaMtxRtspGateway.cs:157-174`. `grep -rn GetPathHealthAsync src` finds only those two plus two doc `cref`s (`IRtspGateway.cs:50`, `:69`). No production caller: `StreamHealthWatcher` uses `GetAllPathsHealthAsync` (spec 410 option C rejected per-path confirmation on the `api` budget). Test-only callers: `MediaMtxProbeLogVolumeTests.cs:74, :111, :140, :218` (spec 331). Ten test doubles implement it (plan §2) | **Yes** |
| P2 | It still echoes body text | `:171` calls `ReadFromJsonAsync<JsonElement>` with no catch, so an invalid body throws System.Text.Json's `JsonException`, whose message copies the offending token verbatim (PR #3050's finding: `'frtsp://admin:SECRET@…' is an invalid JSON literal`). `ParsePathHealth` (`:274` `GetBoolean`) and `TryReadIsoTimestamp` (`:296` `GetString` on invalid UTF-8) throw `InvalidOperationException`s that quote bytes. Spec 409 FR-110..FR-114 wrapped exactly these for the list readers and declared this method a non-goal (§5) because nothing calls it | **Yes, latent**: no production path reaches it, so nothing leaks today |
| P3 | `EnsureSuccessStatusCode` echoes the reason phrase | The framework builds `"Response status code does not indicate success: {code} ({ReasonPhrase})."`; `ReasonPhrase` is the server's status-line text when it differs from the standard one (MediaMTX is Go `net/http` and sends the standard text; a proxy or a different server in front of `mediamtx:api` may send anything, bounded only by the response-header limit). Five sites in the gateway: `MediaMtxRtspGateway.cs:76` (add), `:92` (repoint), `:105` (remove), `:167` (the probe), `MediaMtxRtspGateway.PageReading.cs:50` (every list page: health sweep every 2 s, source repair, startup reconcile). The exception is logged with its message by `HealthSweepFailed`, `SourceRepairSweepFailed`, the source-repair failure log and the reconciler, all to OTLP | **Yes** |
| P4 | Removing the probe makes P2 moot | P2 lives only in `:157-174`. `ParsePathHealth` stays (used by `AddPageToHealth`, `:260`) and is already wrapped there by spec 409's `FoldPage` | **Yes**: P2 needs no fix of its own once P1 is done; the probe's `:167` site is replaced by the helper like the others (this branch ships US2 only, so the probe is still there) |
| P5 | Nothing reads the exception message | **Corrected.** The message is read and surfaced: `ProvisionStreamCommandHandler.cs:88, :123, :142`, `RepointStreamCommandHandler.cs:71` and `RetireStreamCommandHandler.cs:55` pass `ex.Message` into `RtspGatewayUnavailable(Detail)`, which becomes `ApiError.Message` (the API response), and the Warning templates in `Application/Log.cs` (`:23`, `:49`, `:52`) log the exception. So the reason phrase also reached API responses, not only OTLP. The fix closes that channel too (guard: `ProvisionStreamGatewayFailureMessageTests`). The classification path is unchanged: `MediaMtxSourceRepair` classifies on `StatusCode`; `SourceRepairFailureLog.cs:33` and `MediaMtxSourceRepairSweep.cs:80` key throttles on runtime type + `StatusCode` + `HttpRequestError`. Test doubles that build their own "does not indicate success" exceptions never call the real gateway | **Partly**: the wording is free to change (nothing branches on it), but it is an output, so it must stay free of server-chosen text; type, `StatusCode`, `HttpRequestError` and a null `InnerException` must not change |

**Verdict.** Two changes, two colours:

- **US1 (P1/P2): remove `GetPathHealthAsync`. Behaviour-preserving (dead code) → characterisation.**
- **US2 (P3): the five sites stop echoing the reason phrase. Behaviour-changing → RED.**

They are separate changes, not one refactor-that-is-also-a-fix, so they ship as **two PRs, US1
first** (plan §7).

## 2. User stories

### US1 (P1): the gateway carries no unused single-path probe

A method nothing calls cannot be reviewed by use, and this one carries exactly the parse leak spec
409 closed everywhere else. Removing it removes the leak with it and makes "the health sweep never
probes per path" (spec 410's guard) a property the compiler checks.

**Independent test.** Build the solution and run the StreamDistribution unit projects and the
architecture tests: green, with the spec-331 log-volume properties still pinned through the method
the sweep actually calls.

### US2 (P1): a refused MediaMTX call says what was refused and nothing the server chose to say

A non-success answer from the control API raises the same `HttpRequestException` it does today -
same type, same `StatusCode`, same `HttpRequestError`, no inner exception - whose message names the
endpoint and the numeric status only.

**Independent test.** A stub `HttpMessageHandler` answers each gateway call with a non-success
status whose `ReasonPhrase` is `rtsp://admin:SECRET@10.0.8.1/x`; no exception message, `ToString()`
or captured log entry contains `SECRET`, and the status and error kind are unchanged.

## 3. Acceptance scenarios

```gherkin
Feature: the MediaMTX gateway's failures carry no server-chosen text

  # US1 - characterisation (green before and after)
  Scenario: the gateway client's exchanges stay quiet through the method the sweep calls
    Given the shipped appsettings and the real resilience handler
    When the health sweep reads a one-page healthy list 250 times
    Then no Information record is written by the gateway's HttpClient or Polly categories

  Scenario: a retried list read still reports its retries
    Given MediaMTX answers 503 on every attempt
    When GetAllPathsHealthAsync runs
    Then Polly logs the handled attempts and the retry at Warning and the last attempt at Error

  Scenario: the probe no longer exists (build)
    Given IRtspGateway
    Then it declares no single-path health read

  # US2 - red
  Scenario Outline: a refused call carries no reason phrase (security)
    Given MediaMTX answers <call> with <status> and the reason phrase "rtsp://admin:SECRET@10.0.8.1/x"
    When the gateway runs <call>
    Then it throws HttpRequestException with StatusCode <status>
    And neither its Message nor its ToString() contains "SECRET"
    Examples:
      | call                                        | status |
      | AddPathAsync (add/ refused)                 | 500    |
      | AddPathAsync (add/ 400, get/ 404)           | 400    |
      | RepointPathAsync                            | 500    |
      | RepointPathAsync                            | 404    |
      | RemovePathAsync                             | 500    |
      | ListConfiguredPathsAsync, page 0            | 500    |
      | ListConfiguredPathSourcesAsync, page 1      | 502    |
      | GetAllPathsHealthAsync, page 0              | 503    |

  Scenario: the failure's shape is unchanged (guard)
    Given the same refusals
    Then HttpRequestError is what EnsureSuccessStatusCode produced, and InnerException is null

  Scenario: a 404 on the repair PATCH is still a 404 (conflict path, guard)
    Given the repair PATCH answers 404 with a hostile reason phrase and the stream is retired
    When the source repair runs over the real gateway
    Then it takes the "path gone" branch exactly as today, and no log entry contains "SECRET"

  Scenario: a refused repair PATCH logs nothing the server chose (security)
    Given the repair PATCH answers 400 with a hostile reason phrase
    When the source repair runs over the real gateway
    Then no repair or gateway log entry, nor its exception, contains "SECRET"

  Scenario: a 404 on remove is still tolerated (guard)
    Given MediaMTX answers DELETE with 404 and a hostile reason phrase
    Then RemovePathAsync completes without throwing
```

Auth: N/A. The control-API client authenticates as today (ADR-0171's authorize hook); nothing about
who may call changes. Bad request: covered by the 400 rows.

## 4. Functional requirements

### US1

- **FR-101** `IRtspGateway.GetPathHealthAsync` and `MediaMtxRtspGateway.GetPathHealthAsync` are
  removed, with the gateway's `NotRegistered` field (`MediaMtxRtspGateway.cs:266-270`), whose only
  reader is the removed method. `ParsePathHealth` and its helpers stay.
- **FR-102** Every test double drops the member (plan §2 lists the ten). No double gains anything.
- **FR-103** The spec-331 properties pinned through the probe are pinned through
  `GetAllPathsHealthAsync` instead, by **new** facts written and observed green **before** the
  removal (plan §5). The four probe-driven facts are deleted in the removal commit, not edited.
- **FR-104** The two `PerPathCalls.ShouldBe(0)` assertions
  (`StreamHealthWatcherScopeTests.cs:78-81`, `StreamHealthWatcherAbsenceHoldTests.cs:162`) and their
  counters go with the member: the call they count can no longer be written. **[ASSUMPTION A1]**
- **FR-105** Doc comments that name the removed method are reworded to the behaviour they meant
  (`IRtspGateway.cs:50`, `:69`; the stale "ADR-0061" citation at `:6`; test comments at
  `ProvisionRetireRaceIntegrationTests.cs:49` and `StreamRepositoryVersionAssertionIntegrationTests.cs:101`,
  which should point at `StreamHealthWatcher`'s own "path not registered", `StreamHealthWatcher.cs:150`).
- **FR-106** No behaviour change: every existing test other than FR-103/FR-104's passes unmodified.

### US2

- **FR-201** The five `EnsureSuccessStatusCode` calls in the gateway
  (`MediaMtxRtspGateway.cs:76`, `:92`, `:105`, `:167` (the probe, while it exists); `MediaMtxRtspGateway.PageReading.cs:50`) are replaced
  by one private helper that, for a non-success status, throws
  `new HttpRequestException(<same HttpRequestError>, <fixed message>, null, response.StatusCode)`.
- **FR-202** The message names the control-API route **without** the path name or query (e.g.
  `/v3/config/paths/patch`) and the numeric status, and nothing else from the response.
  **[ASSUMPTION A2]**
- **FR-203** Unchanged: the exception's runtime type (`HttpRequestException`, not a subclass),
  `StatusCode`, `HttpRequestError` (the value `EnsureSuccessStatusCode` produces today, captured by a
  guard, A3), a null `InnerException`; the add/ 400 idempotence branch (spec 309); remove's 404
  tolerance; where the check runs relative to the response's disposal and the page parse
  (spec 409 FR-115: the request and the status check stay outside the parse wrappers).
- **FR-204** No caller, no log template, no throttle key changes.
- **FR-205** Scope is the MediaMTX gateway only. Other `EnsureSuccessStatusCode` sites in the repo
  are not touched (§5).

## 5. Non-goals (dropped; owner decisions or other owners)

- **A throttle for `HealthSweepFailed`** (~43k Warnings a day while MediaMTX stays hostile).
  Spec 388 FR-002/FR-006 and spec 409 FR-201 fix a 10-minute interval **for the source repair's**
  Warnings; nothing fixes one for the health watcher, and spec 409 FR-209 excluded it explicitly.
  Its Warning is also the operator's camera-health signal, so the interval is an operational
  choice. **Owner decision.**
- **A byte limit on one page's body** (`ReadFromJsonAsync` of the whole body). No ADR or spec names
  a number (spec 409 §1 row 3e, §5). **Owner decision.**
- **Hardening `GetPathHealthAsync` instead of removing it** (applying FR-110..FR-114 to it). Spec 409
  §5 already called that speculative; removal closes the same leak with less code.
- **The reason phrase elsewhere**: `CameraCatalogFabLookup.cs:68` (a sibling service via service
  discovery, not MediaMTX) and the other `EnsureSuccessStatusCode` sites across the solution, some
  pinned verbatim by characterisation tests (`KeycloakRequestSequenceCharacterisationTests`).
  Spec 409 FR-104 is a MediaMTX rule. Recommend a follow-up if the owner wants it generalised.
- Unchanged from spec 409: a read deadline (#3021 item 3), the post-cancel 404 escape, `MaxListPages`.

## 6. Assumptions (disclosed)

- **[ASSUMPTION] A1 (FR-103/FR-104), the veto point.** Removing a member forces test changes the
  lane normally treats as gate-weakening: four spec-331 facts deleted and two `PerPathCalls == 0`
  assertions removed. Neither weakens a gate: the spec-331 properties are a property of the
  `IRtspGateway` HttpClient registration, not of the method, and are re-pinned first through the
  method production calls (green before the removal, unmodified after); the per-path-call guard
  becomes unrepresentable, which is stronger than an assertion. The one property that goes without
  a replacement is "a 404 probe writes no Information record" - the 404 branch belongs to the removed
  method alone (`GetAllPathsHealthAsync` throws on 404). If the owner reads ADR-0144's "a deleted
  test" literally here, US1 is blocked and US2 still ships, then including the `:167` site.
- **[ASSUMPTION] A2 (FR-202).** Message wording is free (P5). Route-only keeps the message bounded
  and free of path names, which are canonical `cam-{guid}` and harmless but not needed.
- **[ASSUMPTION] A3 (FR-203).** `EnsureSuccessStatusCode` constructs its exception without an
  explicit `HttpRequestError`, so the value is expected to be `HttpRequestError.Unknown`. The
  test-writer confirms the value on the pinned runtime with a guard that passes today; if it is not
  `Unknown`, the helper uses whatever it is.

## 7. Success criteria

- **SC-1** (MET by spec 419, which delivered US1) US1: `grep -rn GetPathHealthAsync src tests` returns nothing; the solution builds in
  Release; the new spec-331 twins pass before and after the removal; no other test changed.
- **SC-2** US2: the R-tests (tasks.md) fail on develop with `SECRET` in the message and pass after;
  every guard passes before and after.
