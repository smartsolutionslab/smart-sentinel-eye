# Plan 291: The sweep that ate the sentinel

**Spec:** `spec.md` · **Issue:** #2656 · **Lane:** autonomous (ADR-0144)
**Phase-4a colour:** **split, by design.** The fixture's new capture is **red** (new behaviour:
a line the ring has evicted must still be readable). The three `WhepAuthorizeRateLimitTests`
facts and the three existing `LogTailDeliversIntegrationTests` facts are **characterisation**
(their assertions must not move). See §4.

## 0. Shape of the change

Test infrastructure only. One new fixture partial and one small class, one extracted method in
`AspireFixture.TailResourceLogsAsync`, one new fact in an existing test class, helpers and doc
comments in one test file. No production C#, no AppHost wiring, no configuration, no frontend,
no shard-filter entries (no new test class — memory: *new test classes need a shard-filter
entry*).

## Constitution / ADR check

| Check | Result |
|---|---|
| Bounded context / layers | None touched. `tests/Integration.Tests` only. |
| Cross-context references | None added. |
| §II primitives | No domain model touched. |
| §IV latency | N/A (spec header). |
| ADR-0103 | Stays on the Aspire fixture; the capture feeds from the same `ResourceLoggerService` tail. No Testcontainers. |
| ADR-0139 / §Testing | Two colours, separated by commit (§4, §5). |
| ADR-0144 | No ADR written; no gate weakened — no assertion deleted, loosened, retried or re-thresholded. The one false-pass path is made able to fail. |
| ADR-0036 | Additive API; `RecentLogs` untouched; no capacity knob, no cap on the capture. |
| ADR-0109 contention | `tests/Integration.Tests/Fixtures/AspireFixture.cs` is shared by every integration test. Parked PRs #2669 and #2671 do not touch it or the other two files (checked 2026-09-28). Re-check before opening the PR. |
| ADR gap | None. |

## 1. Research (done at phase 1 — spec §1)

- The ring holds 400 lines; at 40 provisioned streams one `StreamHealthWatcher` sweep writes
  exactly 400 lines in ~50 ms every 2 s. Retention horizon = the last sweep.
- `RecentLogs` has no cursor, timestamp filter or configurable capacity. Its `lines` argument is
  only a `TakeLast` over the fixed ring.
- `aspire.App` is public, so a test-local `ResourceLoggerService.WatchAsync` subscription was
  possible. **Rejected:** it duplicates the resource-id resolver the fixture deliberately keeps
  single (`TryResolveResourceId`'s doc: "A second copy would make that claim false"), and it would
  depend on the Aspire backlog replay semantics nothing here has observed.

## 2. Decisions

### D1: A fact-scoped capture fed by the existing tail loop

New file `tests/Integration.Tests/Fixtures/LogCapture.cs`:

```csharp
public sealed class LogCapture : IDisposable
{
    // Lines in arrival order since creation. Unbounded by design: lives for one fact.
    public IReadOnlyList<string> Lines { get; }        // snapshot
    public bool Contains(string marker);                // Ordinal
    internal void Record(string line);
    public void Dispose();                              // unregisters from the fixture
}
```

New partial `tests/Integration.Tests/Fixtures/AspireFixture.LogCapture.cs`:

```csharp
public LogCapture CaptureLogs(string resourceName);                  // FR-001, FR-004
internal void RecordLogLine(string resourceName, string content);   // FR-003
```

- **Storage:** `ConcurrentQueue<string>` inside `LogCapture`; the fixture keeps active captures
  per resource in a `ConcurrentDictionary<string, ConcurrentDictionary<LogCapture, byte>>`.
  New fields follow the house rule (no leading underscore) even though the older fields in
  `AspireFixture.cs` carry one; do not rename the old ones (ADR-0036).
- **FR-004:** `CaptureLogs` on a name absent from the tailed set throws
  `InvalidOperationException` naming the resource and `AspireFixture.TailedResources` — the same
  wording `RecentLogs`' placeholder uses. It throws rather than returning a placeholder because a
  capture is read by assertions, and an empty capture would read as "absent" — exactly the
  false pass this spec removes. Argument guard: `Ensure.That(resourceName).IsNotNull()` (ADR-0105).
- **Failure record:** `LogCapture` does not duplicate `_logTailFailures`. A tail fault during a
  fact is already reported by `RecentLogs` and by the fixture's other diagnostics; the capture
  stays minimal.

### D2: One recording path for the ring and the captures (FR-003)

Extract the loop body in `TailResourceLogsAsync` —

```csharp
tail.Enqueue(line.Content);
while (tail.Count > 400) { tail.TryDequeue(out _); }
```

— into `RecordLogLine(resourceName, line.Content)`, which does exactly that **and then** calls
`Record` on every active capture for the resource. `400` stays where it is (as a literal or a
private const in the same file; no configuration). The extraction is behaviour-preserving for
the ring and is characterised by `LogTailDeliversIntegrationTests`' three existing facts.

`RecordLogLine` is `internal` and is also the seam SC-1 and SC-2 use to force eviction
deterministically. That is its second reason to exist, not its first.

### D3: The red fact — `A_capture_keeps_a_line_the_tail_has_already_evicted`

In `tests/Integration.Tests/Fixtures/LogTailDeliversIntegrationTests.cs` (shard 3; no filter
change). Shape:

1. `using LogCapture capture = aspire.CaptureLogs(CameraCatalogResource);`
2. Register a camera whose name is an invented token (reuse test A's shape and
   `InventedToken`); poll `capture` until it contains the token (proves **real delivery** reaches
   the capture, not only the seam).
3. `RecordLogLine(CameraCatalogResource, …)` 401 times with lines that are recognisably
   synthetic (e.g. `"synthetic eviction filler {i} for {token}"`) — they land in the shared
   camera-catalog ring and in any later dump, so they must say what they are.
4. **Counterfactual first:** `aspire.RecentLogs(CameraCatalogResource, lines: 400)` does
   **not** contain the token — the eviction happened, so step 5 is not vacuous (memory:
   *prove a guard by counterfactual*; *an assertion must not check its own input*).
5. `capture.Contains(token)` is true.

**Red:** the test-writer lands a skeleton `LogCapture` whose `Lines` reads
`RecentLogs(resource, lines: 400)` on demand — today's semantics, so the fact compiles and fails
at step 5 by assertion, not by compile error. Step 2 passes on the skeleton (the token is fresh);
step 4 passes (eviction is real). The red therefore isolates the one missing property.

### D4: `WhepAuthorizeRateLimitTests` reads a capture

- In each of `A_throttled_authorize_never_reaches_the_handler`,
  `A_partition_entering_the_throttled_state_is_logged_once` and
  `Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record`: add
  `using LogCapture capture = aspire.CaptureLogs(StreamDistributionResource);` **after** the
  `BeginCounting…` call and **before** the sentinel request. After the alignment, so the
  alignment's own forced transition is outside the capture as well as before the sentinel.
- `MarkerEverAppearsInLogsAsync`, `ThrottleTransitionCountSinceAsync` and
  `ThrottleTransitionCountSince` take the `LogCapture` as their first parameter and read
  `capture.Lines` / `capture.Contains`. The sentinel-position anchor (`Array.FindLastIndex`) and
  its "sentinel absent → 0" rule stay: inside a capture the sentinel cannot scroll away, so the
  rule now only covers "not delivered yet", which is what it was written for.
- Dumps (FR-007): one private helper, e.g. `CapturedEvidence(LogCapture, params string[] paths)`,
  returns `"{n} lines captured; relevant:"` plus the captured lines containing any given path or
  a `ThrottleTransitionMarkers` entry. Replaces every `aspire.RecentLogs(…, lines: 400)` in the
  class. SC-4: no `RecentLogs` left in the file.
- Doc comments that describe the ring (the S6 comments in both counting facts,
  `ThrottleTransitionCountSince`'s summary, `MarkerEverAppearsInLogsAsync`'s summary) are
  rewritten to describe the capture and cite spec 291 §1.2 in one line. No other comments move.
- **Unchanged, byte for byte:** every `ShouldBe` / `ShouldBeTrue` / `ShouldBeFalse` /
  `ShouldNotBe` predicate and expected value; `LogAbsenceGraceWindow`, `PollInterval`, the
  `Task.Delay(PollInterval)` between `afterTransition` and `afterRepeats`, the request sequence,
  `ThrottleTransitionMarkers`. Only message text that embedded a dump may change.

### D5: What is deliberately not done

- `RecentLogs`' capacity and default stay (FR-002). Other callers use it for diagnostics, and
  its dumps for stream-distribution remain a sweep of noise — a known, recorded limitation, not
  this spec's.
- No capture cap, no capture for other resources' tests, no migration of other `RecentLogs`
  users. `LogTailCoverageTests` (Architecture.Tests) is **not** extended to `CaptureLogs(...)`:
  FR-004 fails the first run loudly, which is the property that guard exists to give. Named in
  the PR as a possible follow-up.
- The production log volume (spec §1.5) — separate issue.

## 3. Entities / messaging / boundaries

N/A — no domain, no messages, no context boundaries. NetArchTest rules untouched.

## 4. Phase 4a: two colours

| Fact | File | Colour | Observed before | After |
|---|---|---|---|---|
| `A_capture_keeps_a_line_the_tail_has_already_evicted` | `LogTailDeliversIntegrationTests` | **red** | **red** on the ring-backed skeleton (step 5) | green |
| `A_camera_registration_reaches_…`, `Every_tailed_resource_…`, `A_restarted_resource_…` | `LogTailDeliversIntegrationTests` | characterisation (D2 extraction) | green | green, **unmodified** |
| `A_throttled_authorize_never_reaches_the_handler` | `WhepAuthorizeRateLimitTests` | characterisation | green (baseline on develop) | green, predicates unmodified |
| `A_partition_entering_the_throttled_state_is_logged_once` | same | characterisation | green | green, predicates unmodified |
| `Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record` | same | characterisation | green | green, predicates unmodified |
| The other three facts in the class | same | untouched | green | green |

**Why characterisation is honest for the Whep facts.** The product (the rate limiter and its
transition log) does not change. What changes is the facts' *evidence source*, from a lossy one
to a complete one. Their predicates must hold unchanged against the correct implementation; a
predicate that has to be edited would mean the protected behaviour moved, and blocks.

**Why the false-pass closure is not a separate red.** Making `A_throttled…`'s absence check
able to fail is a property of the capture, which D3 reds. A per-fact red would need the limiter
to be broken; SC-2's counterfactual (phase 5) demonstrates it instead, uncommitted.

Local runs of the Whep facts in isolation will not see the 40-stream noise, so a local green
there is not evidence of the fix — SC-2's injection is. Say so in the PR.

## 5. Commits (Conventional Commits, ADR-0030; no `Co-Authored-By`, ADR-0086; each builds, ADR-0087)

1. `test(integration): red a log capture that keeps lines the tail evicted` — D2 extraction, the
   ring-backed skeleton, D3's fact. Builds; the new fact fails at step 5; the three existing
   `LogTailDelivers…` facts pass.
2. `fix(integration): keep every line a log capture was open for` — real `LogCapture` storage and
   fan-out in `RecordLogLine`. The new fact passes.
3. `fix(integration): read the WHEP rate-limit log evidence from a capture` — D4. All green.

## 6. Risks and residuals

- **Shared fixture file.** The D2 extraction touches the loop every integration test's
  diagnostics depend on. Mitigation: the extraction is mechanical, characterised by the three
  delivery facts, and `RecordLogLine` must not throw (a throw inside the loop is caught, recorded
  as a tail fault and re-subscribed — it would drop lines for every test). `ConcurrentQueue`
  enqueue and dictionary enumeration cannot throw here; keep it that way.
- **Synthetic lines in camera-catalog's ring** after D3 runs: labelled; they only appear in later
  diagnostic dumps within shard 3.
- **Other stream-distribution log users** (`StreamFab*` classes) print dumps that are all sweep
  noise; diagnostics only, out of scope.
- **Production log volume** (spec §1.5) — separate issue recommended.

## 7. Phase 5 evidence to collect

- SC-2: the injection patch (`aspire.RecordLogLine("stream-distribution", …)` × 401 between
  `afterTransition` and `afterRepeats`, and between `A_throttled…`'s sentinel request and its
  first poll; plus the throttled-path counterfactual) run against commit 2's tree with commit 3
  stashed, then against commit 3. Both outputs verbatim. Revert.
- SC-3: a second marker line injected after the sentinel → `Repeated…` fails with count 2. Revert.
- SC-4 grep, SC-5 diff, SC-6 shard results.
