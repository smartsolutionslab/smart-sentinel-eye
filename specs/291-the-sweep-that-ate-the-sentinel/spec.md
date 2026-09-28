# Feature Specification: The sweep that ate the sentinel

**Feature Branch**: `fix/2656-sentinel-scroll-off-log-tail` (cut from `origin/develop` at `2cbd044b`)

**Created**: 2026-09-28

**Status**: Draft (Phase 1 gate)

**Input**: Issue [#2656](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2656):
*WhepAuthorizeRateLimitTests: sentinel scroll-off in the 400-line log ring buffer causes a
false-positive FR-009 failure, distinct from #2537*. Related: #2537 / #2648 (spec 251, the
debounce-window defect, already fixed), spec 208 (#2284, the facts under repair), #2053 / #2054
(the fixture's log tail).

**Spec number.** 291. `origin/develop` tops out at 286; 287 (#2669), 288
(`refactor/2661-e2e-page-layer`), 289 (local branch) and 290 (#2671) are claimed by unmerged
branches. **Re-check before opening the PR** (memory: *spec number: origin/develop isn't enough*).

**ADRs and constitution sections referenced:** ADR-0103 (integration tests on the Aspire
fixture, no Testcontainers), ADR-0139 and constitution §Testing (red for new behaviour,
characterisation for preserved behaviour), ADR-0144 (autonomous lane, phase-4a colour),
ADR-0037 (phases), ADR-0036 (smallest change), ADR-0087 (each commit builds on its own),
ADR-0109 (contention files), ADR-0118 (one sink, not flooded — §"Observed, not fixed here").

**No ADR gap.** This is test infrastructure. No ADR governs the fixture's log tail; the change
is additive and does not alter `RecentLogs`' contract.

**Latency budget (constitution §IV):** N/A — test fixture and test file only; no leg touched.

---

## 1. Investigation (phase 1 was an investigation — memory: *verify the issue premise*)

### 1.1 The failing reads, from the CI logs of the failing attempts

Both first-attempt logs were downloaded before anything could re-run over them
(`gh run view <id> --attempt 1 --log-failed`):

| Run | Fact | Assertion that failed |
|---|---|---|
| 36417503136 (PR #2655) | `Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record` | `afterRepeats` should be 1 but was 0 |
| 36402665767 (PR #2654) | `A_throttled_authorize_never_reaches_the_handler` | `sentinelLogged` should be True but was False |

The #2654 failure the issue listed as "possibly related" **is the same mechanism**, one read
earlier: that fact's sentinel-presence poll reads `RecentLogs(resource)` with the **default
120 lines**, not 400.

### 1.2 What the 400-line tail actually held at the moment of failure

Both failure messages dump `RecentLogs("stream-distribution", lines: 400)`. Measured from the
dumps:

| Run | Lines | Distinct `GET /v3/paths/get/cam-…` | Wall-clock span of all 400 lines | Throttle-marker lines |
|---|---|---|---|---|
| 36402665767 | 400 | 40 | 09:31:14.385 → 09:31:14.431 (**46 ms**) | 0 |
| 36417503136 | 400 | 40 | 11:56:46.595 → 11:56:46.660 (**65 ms**) | 0 |

Each dump is **exactly one sweep of `StreamHealthWatcher`**
(`src/StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs`): every 2 s it
probes every non-retired stream through `IRtspGateway`, and each probe writes five
`Information` records (`LogicalHandler[100]`, `ClientHandler[100]`, `ClientHandler[101]`,
`Polly[3]`, `LogicalHandler[101]`), each rendered as two console lines. 40 streams × 5 records
× 2 lines = **400 lines — the ring buffer's entire capacity**
(`AspireFixture.TailResourceLogsAsync`, `while (tail.Count > 400)`).

### 1.3 Root cause, refined

The issue's diagnosis is right in direction and understated in size:

1. **The noise is not other tests running concurrently.** `AspireCollection` serialises every
   class. It is stream-distribution's own background health sweep, whose volume is proportional
   to the number of streams earlier tests in the same shard have provisioned and never retired
   (40 by the time shard 1 reaches this class).
2. **The retention horizon is not "a few hundred lines of scroll"; it is the last sweep.** Any
   line written before the most recent sweep is gone. A read that lands after a sweep cannot see
   anything older than that sweep — the sentinel, the transition record, or the throttled path.
3. **The transition record itself was evicted too** (0 marker lines in both dumps), so the
   failure is not only about the anchor. Re-anchoring on a timestamp would find nothing to count.
4. **It is a false failure in three places and a false pass in one:**
   - `Repeated…`'s `afterRepeats` read (#2656's evidence) — false failure;
   - every fact's `MarkerEverAppearsInLogsAsync(sentinelPath, …)` poll (#2654's evidence, 120
     lines) — false failure;
   - `ThrottleTransitionCountSinceAsync`'s settle poll — false failure (reads 0 until timeout);
   - `A_throttled…`'s **absence** check for the throttled path — **false pass**: a handler log
     line written and then swept away before the next 250 ms poll is never seen. This one has
     never shown up in CI because it cannot fail; it is the more serious of the four.
5. **It gets worse as the suite grows.** Every future test that registers a camera adds 10 lines
   to every sweep for the rest of the shard's run.

### 1.4 The four suggested directions, evaluated against 1.2-1.3

| # | Direction | Verdict |
|---|---|---|
| 1 | Timestamp cutoff instead of line-position anchor | **Rejected.** Feasible — DCP content carries an ISO timestamp prefix — but useless: the lines to be counted after the cutoff are themselves evicted (1.3.3). |
| 2 | Increase the ring buffer's capacity | **Rejected as the fix.** Probabilistic: at 40 streams a 4 000-line ring holds ~20 s, and the horizon shrinks with every camera a future test registers (1.3.5). It also has to be paired with every call site in this class reading the larger window. Blast radius is small, but it lowers a rate rather than closing the race. |
| 3 | Narrow the gap between the two reads | **Rejected — insufficient, provably.** A sweep landing in *any* gap wipes the tail, including the 250 ms poll gap of the *first* read (#2654 failed on its first read). No local reordering removes a gap. |
| 4 | Accept as a documented flake with retry | **Rejected** — the constitution's §Testing posture, and it would leave the false pass (1.3.4) in place. |
| **5** | **A fact-scoped, unbounded log capture** | **Chosen.** See §3. |

### 1.5 Observed, not fixed here

At production scale the same sweep writes 5 `Information` records per stream every 2 s — at the
250-camera target, **~625 records/s** of HTTP-client and Polly chatter into the one sink
(ADR-0118: "one sink, kept readable"). That is a product logging decision (the `LogLevel` of
`System.Net.Http.HttpClient.IRtspGateway.*` and `Polly` in stream-distribution) with operational
consequences, not a test-reliability fix. **Out of scope; recommend a separate issue.** Silencing
those categories in the test lane only was considered and rejected here: it would change what a
service logs under test versus production, and would still leave the ring a race under any
other noise source.

---

## User Scenarios & Testing

### User Story 1 — A log assertion sees every line its fact caused (Priority: P1)

A test author asserting on a service's log output during one fact gets a complete record of
every line that service wrote between the start of the capture and the read, regardless of how
much unrelated log traffic the service wrote in the meantime. `WhepAuthorizeRateLimitTests`
reads from that record, so its FR-002 and FR-009 facts pass or fail on the rate limiter's
behaviour alone.

**Why this priority:** the only story. The three facts have failed falsely on at least five
unrelated PRs in one day (#2652, #2654, #2655, #2666, #2669), and one of their assertions
cannot fail at all.

**Independent test:** see §"End-to-end test procedure".

**Acceptance scenarios:**

```gherkin
Scenario: a captured line survives eviction from the ring buffer (happy path)
  Given a log capture started on "camera-catalog"
  And a camera registration whose invented name reaches the camera-catalog log
  When more than 400 further lines are recorded for "camera-catalog"
  Then RecentLogs("camera-catalog", lines: 400) no longer contains the invented name
  And the capture still contains the invented name

Scenario: the repeated-refusal fact survives a health sweep between its reads (conflict)
  Given a partition that has logged its one throttle-transition record after the sentinel
  When a sweep of 400 or more unrelated lines is written between afterTransition and afterRepeats
  Then afterRepeats still equals afterTransition, which equals 1

Scenario: a genuine second transition record still fails the fact (bad behaviour stays caught)
  Given the capture holds the sentinel followed by two throttle-transition records
  When the repeated-refusal fact counts records after the sentinel
  Then the count is 2 and the FR-009 assertion fails

Scenario: a handler line for the throttled path is not missed (the false pass)
  Given a capture started before the throttled request
  When the throttled path's line is written and then evicted from the ring before the next poll
  Then the absence check still sees it, and A_throttled_authorize_never_reaches_the_handler fails

Scenario: a capture on an untailed resource fails loudly (bad request)
  Given a resource name not in AspireFixture.TailedResources
  When a test starts a capture on it
  Then the call fails immediately, naming the resource and TailedResources

Scenario: captures do not change RecentLogs (contract preserved)
  Given any test that calls RecentLogs with or without a lines argument
  Then it receives the same ring-buffer tail as before: capacity 400, default 120
```

Auth: N/A — no endpoint or scope changes; the facts' requests are unchanged.

### Edge cases

- A capture is disposed at the end of its fact; lines written afterwards are not retained.
- A capture outliving its fact (missing `using`) grows for the rest of the run. Mitigated at the
  call site by `using`; no cap is added (no speculative generality, ADR-0036).
- Lines recorded concurrently with capture registration may or may not be captured. Harmless:
  every fact starts its capture before sending the request whose line it looks for.
- A tail re-subscription after a resource restart leaves a gap in the capture exactly as it does
  in the ring; `RecentLogs` already reports such a fault, and the capture reuses that record.
  Not reachable in this class (it restarts nothing).

## Requirements

- **FR-001** `AspireFixture` offers a fact-scoped log capture for a tailed resource that retains
  every line the tail receives for that resource from creation until disposal, unbounded, in
  arrival order.
- **FR-002** `RecentLogs`' signature, ring capacity (400) and default (`lines = 120`) do not
  change. The capture is additive.
- **FR-003** The tail loop records each line through one method that feeds both the ring and any
  active captures, so the ring and the capture cannot disagree about what arrived.
- **FR-004** Starting a capture on a resource not in `TailedResources` fails immediately with a
  message naming the resource and `TailedResources`.
- **FR-005** `WhepAuthorizeRateLimitTests`' four log-reading paths — sentinel presence, throttled
  path absence, the transition count and its settle poll — read from a capture started in the
  fact before the sentinel request is sent. No assertion in the class reads `RecentLogs`.
- **FR-006** Every assertion predicate and expected value in the three log-reading facts is
  unchanged (characterisation contract, §Testing). Only the source the helpers read changes, and
  the failure-message dumps.
- **FR-007** The three facts' failure dumps print the capture's line count and the captured
  lines that contain the sentinel path, the throttled path (where one exists) or a throttle
  marker — not a 400-line tail that §1.2 shows is pure sweep noise.

## Success criteria ("done", verifiable)

- **SC-1** A new fixture fact proves FR-001 against a genuinely evicted line: it asserts the
  counterfactual first (the ring no longer holds the marker), so the capture's success is not
  vacuous. **Observed red** against a skeleton capture that reads the ring (today's semantics),
  then green. Committed; runs every CI.
- **SC-2** **The issue's failure, reproduced deterministically and then closed** (phase 5,
  uncommitted, quoted in the PR): inject 401 synthetic lines into stream-distribution's ring
  between `afterTransition` and `afterRepeats`, and between the sentinel request and its first
  poll in `A_throttled…`.
  - On the pre-fix helpers: `afterRepeats` is 0 (the issue's `-1` signature) and
    `sentinelLogged` is False (#2654's signature).
  - On the fixed helpers, the same injection: both facts green.
  - Plus the false-pass counterfactual: the throttled path's line injected after the throttled
    request, then evicted — pre-fix the absence check passes; post-fix it fails. (Then revert.)
- **SC-3** A mutation check on FR-009 still bites: injecting a second throttle-marker line after
  the sentinel in the capture makes `Repeated…` fail with a count of 2 (then revert).
- **SC-4** `grep -n RecentLogs tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs`
  returns nothing.
- **SC-5** `git diff` of the three facts shows no change to any `ShouldBe`/`ShouldBeTrue`/
  `ShouldBeFalse` predicate or expected value; `RecentLogs`' signature, `400` and `120` are
  untouched.
- **SC-6** CI green on all four integration shards, with `WhepAuthorizeRateLimitTests` (shard 1,
  the 40-stream condition) and `LogTailDeliversIntegrationTests` (shard 3) passing; the three
  existing `LogTailDelivers…` facts unmodified.

## End-to-end test procedure

1. Boot nothing by hand; run
   `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~LogTailDeliversIntegrationTests|FullyQualifiedName~WhepAuthorizeRateLimitTests"`.
2. Apply SC-2's injection patch; run the three facts on the pre-fix helpers (stash the fix) and
   on the fixed helpers; record both outputs verbatim.
3. Apply SC-3's mutation; record the failure; revert.
4. Push; read all four integration shard results (memory: *develop has no required status
   checks* — read every bucket).

## Assumptions

- **A1** Aspire's `LogLine.Content` delivery order into the tail loop is arrival order. The ring
  already relies on this; the capture relies on nothing more.
- **A2** A capture's size during one fact is bounded by the fact's duration: ~25 s × ~200 lines/s
  at 40 streams ≈ 5 000 lines, about 1 MB. Not a concern.
