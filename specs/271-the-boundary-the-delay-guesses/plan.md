# Plan 271 — The boundary the delay guesses

**Spec**: [spec.md](spec.md) · **Issue**: #2537 · **Phase**: 2 (Plan)
**ADRs**: ADR-0150, ADR-0103, ADR-0139, ADR-0144, ADR-0036 · **New ADR**: none

## 1. Constitution / ADR check

| Gate | Result |
|---|---|
| §II primitives, §IX forward-compat | N/A — test code only, no domain model touched. |
| No cross-context references | Unchanged — the class already reaches the service only over HTTP through `AspireFixture`. |
| §IV latency budget | **N/A** — `/streams/authorize` is not on the event→overlay path; no `src/` change. |
| §Testing / ADR-0150 | **Applied.** The fresh-window premise becomes an observed condition (refusal then admission). The one remaining fixed wait, `LogAbsenceGraceWindow`, waits for an *absence*, which has no condition to poll; it is sound and was already documented as such. |
| ADR-0103 | Integration stays against the real Aspire stack; the scratch limiter harness in spec §1.3 was investigation evidence and is **not** committed. |
| ADR-0139 / 0144 | 4a/4b split per spec §7: red adversarial fact + characterisation of six. |

## 2. Where it lives

One file: `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs`.
Bounded context: StreamDistribution (integration tests). Layers: none — no Domain / Application /
Infrastructure / Api file changes, no entities, no value objects, no messaging.

## 3. Design

### 3.1 The seam: `BeginCountingAsync()`

```csharp
/// Returns the number of permits this fact may still spend before the next boundary.
private async Task<int> BeginCountingAsync()
```

- **4a (today's premise, made explicit):** `return PermitLimit;` — no requests, no waits. Every fact's
  arithmetic is then identical to today's, which is what makes 4a's capture a characterisation.
- **4b (FR-001):**
  1. Send `NoTokenBody` requests until one is `429`; fail if `PermitLimit + 1` sends never saw one
     ("the window never refused — is another limiter configuration live?").
  2. Poll with a dedicated `AlignmentPollInterval = 50 ms` until a non-`429` answer, against
     `AdmissionRecoveryTimeout`; fail on the deadline with the same message family as today's recovery.
  3. `return PermitLimit - 1;` — the admitted probe spent one permit in the fresh window.

  A refusal followed by an admission proves a reset between them; with a 50 ms poll the boundary is
  known to within ~50 ms + one round trip, and the next one is ≥ `Window` after it (spec §1.2: windows
  only ever *lengthen*, by heartbeat drift). The facts need < 1 s of that runway.

### 3.2 Fact shapes after 4a (bodies fixed from 4a on)

| Fact | Counted sequence, back to back after `budget = await BeginCountingAsync()` | Then |
|---|---|---|
| `Authorize_refuses_…_with_429` | `budget` × `NoTokenBody` (each asserted not `429`, FR-004), then one more | assert that one `429` |
| `A_throttled_authorize_never_reaches_the_handler` | `budget − 1` fillers (each not `429`), sentinel (assert `403`), throttled (assert `429`) | poll sentinel present (FR-005 tail on failure); poll throttled marker absent for `LogAbsenceGraceWindow` |
| `Health_and_readiness_are_never_throttled` | `budget` admitted + one (assert `429`) | the five non-authorize routes, unchanged |
| `A_partition_entering_the_throttled_state_is_logged_once` | sentinel (assert `403`), `budget − 1` admitted, one refused | poll sentinel present; `ThrottleTransitionCountSinceAsync` == 1 |
| `Repeated_refusals_…` | sentinel (`403`), `budget − 1` admitted, one refused, **five more (each asserted `429`)** | poll sentinel present; count settles at 1; re-read count still 1 (the existing short `PollInterval` re-read stays) |
| `Authorize_declares_the_429_it_can_answer` | none — does not call the seam | unchanged |
| **new** `A_fact_that_starts_just_before_a_window_boundary_still_counts_one_whole_window` | pre-position (exhaust; poll 50 ms until admitted; `Task.Delay(Window − 150 ms)`), then the `A_throttled` sequence through the seam | status assertions only — no log reads, so it is fast and its red is unambiguous |

Notes:
- `ExhaustWindowAsync` becomes `ExhaustWindowAsync(int budget)` — sends `budget + 1`, asserts each of the
  first `budget` is not `429` (FR-004), returns the last status. The helper stays; the literal
  `PermitLimit + 1` leaves it.
- FR-002's reorder in `A_partition…` and `Repeated…` is safe for the sentinel-position anchor (S6): the
  sentinel's handler logs before its response returns, and the exhaust starts after that response.
- `Repeated…`'s five repeats asserted `429` close the vacuous pass spec §1.2 exposes: today, repeats that
  crossed a boundary are admitted, log nothing, and the "no second record" assertion passes proving
  nothing.
- The adversarial fact's `Window − 150 ms` is a *pre-position*, not a synchronisation: it drives the
  harness into the hazardous phase on purpose (the "driving" use ADR-0150 §1 keeps legal). Its
  assertions do not depend on the offset being exact — with the 4b seam any starting phase passes.

### 3.3 Recovery (FR-003)

- The class implements `IAsyncLifetime` (xUnit 2.9.3 — `Task InitializeAsync()` returns
  `Task.CompletedTask`; precedent: `RuleLifecycleIntegrationTests` and others).
- `DisposeAsync()` = `WaitUntilAdmittedAgainAsync()` **without** the trailing `Task.Delay(Window)`: poll
  until a request is admitted, then return. It runs after passing *and failing* facts, so a failure no
  longer leaves the window exhausted for the next fact or class (35845632059's cascade).
- The per-fact trailing `await WaitUntilAdmittedAgainAsync();` calls are removed in 4a (DisposeAsync is
  introduced in 4a too, with today's body *including* the delay, so 4a remains characterisation of the
  present protocol; 4b removes the delay).
- Why the other partition consumers are still safe: after an admitted recovery probe the window has
  `PermitLimit − 1 = 49` left, and the next window is fresh; `WhepHandshakeLatencyTests` needs 21,
  `WhepAuthIntegrationTests` 6.

### 3.4 Documentation (FR-008)

Rewrite the class `<summary>` paragraph "Every test that exhausts the window…" and the S9 block in
`WaitUntilAdmittedAgainAsync` to state: the limiter's boundary is heartbeat-driven (100 ms, first tick at
or after `Window`), so "one `Window` after an observed admission" lands within a heartbeat of the next
boundary on either side; the class therefore observes a boundary at the start of each counting fact.
Keep the §1.4 caveat out of code comments (it is a record, and belongs in the spec/PR).

## 4. Runtime cost

Per counting fact, before: fact + recovery (≤ 10 s) + 10 s delay. After: alignment (≤ 51 requests +
≤ 10 s) + fact + recovery (the fact leaves the window exhausted, so recovery waits for the next
boundary, ≤ 10 s). Net ≈ unchanged; the new fact adds ~20–25 s. Shard 1/4's budget absorbs it; record the measured class wall time in the PR.

## 5. Risks

| Risk | Mitigation |
|---|---|
| The adversarial fact is green in 4a on some run (the heartbeat drift made the pre-position land after the boundary). | Spec §7: run it 3× in 4a, record every outcome; a green run is data, not a reason to tune the offset silently. |
| 4b's engineer "fixes" a characterisation assertion. | Spec §7 forbids it; reviewer diffs 4a→4b and rejects any change to a fact body. |
| The §1.4 log-tail miss recurs. | Out of scope by design; FR-004/005 make it a sentinel-`403`-with-tail failure — the input a follow-up issue needs. |
