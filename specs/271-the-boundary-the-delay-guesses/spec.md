# Spec 271 — The boundary the delay guesses

**Issue**: #2537 · **Branch**: `fix/2537-whep-rate-limit-boundary` · **Phase**: 1 (Specify)
**Date**: 2026-09-27 · **Base**: `41ab72ec` (`origin/develop`, fetched 2026-09-27) — renumbered from the
original investigation's spec 251 (worktree `sse-2526`, branch `investigate-2537-whep-rate-limit-flake`,
left untouched at the author's request); §1's investigation and §1.3's measurement are carried over
unchanged, only the base commit and spec number are updated.
**Context**: integration test code only — `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs`.
No `src/`, `apps/`, contract, AppHost, fixture, CI-workflow or configuration change.
**Engineer**: `backend-engineer` (4b) after `test-writer` (4a) · **Reviewer**: `backend-reviewer`
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**Phase 4a colour**: **both, split exactly** (§7) — one red adversarial-phase fact; the six existing
facts are characterisation.
**ADRs**: ADR-0150 (waiting is a condition, not a count — §Testing), ADR-0103 (integration against the
real Aspire stack), ADR-0139 (new behaviour observed red first), ADR-0144 (lane; 4a/4b split), ADR-0037
(phases), ADR-0036 (smallest change). Behaviour under test: spec 208 (#2284).
**Constitution**: §IV — **N/A**. `/streams/authorize` is MediaMTX's WHEP admission hook, not a leg of
the event→overlay path, and nothing in `src/` changes. §Testing — see §7.
**New ADR needed**: **No.** ADR-0150 already decides this: a test waits for a condition, never for a
count. This spec applies it to a window boundary the class currently *infers* from a fixed delay.

---

## 1. What was investigated, and what was found

### 1.1 The failure record — 8 of the last ~28 `develop` CI runs

Every failure of this class on `develop` since sharding, read from the job logs and TRX artifacts
(test start/end times) rather than from the issue:

| Run | Failing fact | Shape | Preceded by (same shard, ms apart) |
|---|---|---|---|
| 35661421599 | `A_throttled_authorize_never_reaches_the_handler` | sentinel never logged (10 s) | `Health_and_readiness_are_never_throttled` |
| 35679604506 | `A_throttled_…` | **request 51 answered `403`, not `429`** (287 ms) | `Health_…` |
| 35845632059 | `A_throttled_…` **and** `Repeated_refusals_…` | sentinel never logged, both | `Health_…`, then the failed `A_throttled` |
| 35862914514 | `A_throttled_…` | sentinel never logged | `Health_…` |
| 35918329596 | `A_throttled_…` | sentinel never logged | `Health_…` |
| 35925050239 | `A_throttled_…` | sentinel never logged | `Health_…` |
| 35932737838 | `Repeated_refusals_…` | sentinel never logged | `A_throttled_…` (**passed**) |
| 35981316098 | `A_throttled_…` | **request 51 answered `403`** (307 ms) | `Health_…` |

(35988620124 is excluded: ~100 facts across every context failed together — a stack collapse, not this
class.) The class order is identical in every run: `A_partition…` → `Health…` → `A_throttled…` →
`Repeated…` → `Authorize_declares…` → `Authorize_refuses…`. **Every failure is a fact that begins
milliseconds after a fact ending in `WaitUntilAdmittedAgainAsync`.** `A_partition…`, the fact that
starts cold, has never failed.

### 1.2 The mechanism — the next fact's window phase is guessed, not observed

`Program.cs:86-101` builds `RateLimitPartition.GetFixedWindowLimiter` (50 / 10 s in the integration
lane, `AppHost.cs:438-443`). Read against the pinned runtime's source
(`dotnet/runtime` `release/10.0`, `FixedWindowRateLimiter.cs:278-299`,
`DefaultPartitionedRateLimiter.cs:16,37`):

- A partitioned fixed-window limiter does **not** own a timer. One heartbeat, **every 100 ms**, calls
  `TryReplenish`, which resets the window only when `now − lastReplenishment ≥ Window`, then sets
  `lastReplenishment = now`. So a boundary lands on the first heartbeat *at or after* 10 s, and each
  window is 10 s **plus** up to one heartbeat (more if the heartbeat is late on a loaded host).
- Every caller of this service collapses into one partition (`ip:127.0.0.1`) through Aspire's DCP proxy
  (`ApiGateway/Program.cs:64-83`; the class doc says the same).

`WaitUntilAdmittedAgainAsync` (`:459-509`) observes an admission at time **A** (≥ boundary B, by up to
one 250 ms poll + a round trip), then `Task.Delay(Window)`, and returns. The next fact starts at
**A + 10 s**. The next boundary is at **B + 10 s + h**, h ∈ [0, 100 ms] plus heartbeat lateness. Its own
comment (S9) claims this "reliably lands the next fact just past the following boundary". It does only
when `A − B > h`. When the poll happened to land close to B and the heartbeat drifted, the next fact
starts **before** the boundary, in a window that already holds the recovery probe's permit:

- the boundary lands *during* the next fact's 51-request sequence → the counter resets mid-sequence and
  **request 51 is admitted** (`403` from the handler, the action-less body) — runs 35679604506 and
  35981316098, exactly;
- the boundary lands *after* the fact's first 50 requests (a late heartbeat) → 1 probe + 49 fillers =
  50, and **the sentinel, request 50, is refused**. The sentinel's status is never read, so this surfaces
  as "never appeared in the log tail" after a 10 s grace — the five `A_throttled` sentinel failures, and
  35845632059's `Repeated` cascade (the failed `A_throttled` threw before recovering, leaving the window
  full for the next fact's first request).

`A_throttled` is also the fact whose own ending most distorts the next phase: its 10 s absence grace
(`:196`) is itself one `Window`, so its recovery probe is admitted at an arbitrary offset from the
boundary rather than just after it.

**This is ADR-0150's defect on the backend.** Each individual wait here *is* a deadline poll; what is
not a condition is the *premise every fixed count rests on* — "this fact begins in a fresh window with
all 50 permits". That premise is produced by a fixed delay standing in for an unobserved boundary.

### 1.3 Reproduced outside the stack, with the real limiter

A scratch harness drove the exact limiter `Program.cs` builds (`PartitionedRateLimiter` +
`GetFixedWindowLimiter`, 50 / 10 s, queue 0, the runtime's own heartbeat) with this class's client
protocol — `Health`'s exhaust + `WaitUntilAdmittedAgainAsync`, then `A_throttled`'s 49 + sentinel +
throttled, then `Repeated`'s first request — 40 independent limiters in parallel, 2 rounds:

| Protocol | `A_throttled` | `Repeated` sentinel |
|---|---|---|
| today's (delay one `Window` after an observed admission) | **8 of 75 failed**, all "request 51 admitted" | **3 of 75 refused** (cascades) |
| aligned (§5 FR-001: exhaust, poll for the reset, then run) | **0 of 66** | **0 of 66** |

The harness ran at ~25 ms per request (Windows timer granularity); CI runs at ~5 ms. It reproduces the
straddle shape and the cascade, not the heartbeat-lateness share — that part is inferred, not measured.

### 1.4 What this does **not** explain — stated, not buried

**Run 35932737838's standalone `Repeated` failure.** `A_throttled` *passed* immediately before it
(20.288 s: sequence + one sentinel poll + 10 s grace + probe admitted on the first try + 10 s), so
`Repeated`'s sentinel was at most the second request in its window. The limiter cannot refuse it. That
failure is a **log-tail miss** (sentinel admitted, its line not found in `RecentLogs`' 120-line tail
within 10 s), cause unknown: the job log carries no service output and the assertion printed no tail.
One occurrence in ~28 runs; `A_partition`'s identical sentinel poll has never failed.

This spec does not guess at it. It makes the next occurrence decisive (FR-004, FR-005): the sentinel's
status is asserted before any log is read, and every sentinel failure message carries the full
retained tail.

### 1.5 Cross-test interference — ruled out for this class

- `AspireCollection` serialises every class; nothing else runs concurrently against the stack.
- The partition *is* shared with `WhepHandshakeLatencyTests` (21 calls), `WhepAuthIntegrationTests` (6)
  and MediaMTX's own hook calls. In every failing run those classes ran **before or after** this one,
  never between its facts, and nothing in the integration lane keeps a background WHEP/RTSP reader open
  that would call the hook (pull sources such as `fixture-video` are not authorised through it).
- The throttle-transition `IMemoryCache` (`Program.cs:55-64`) keys logging, not admission.
- No collection-isolation change is needed; #2451's `DisableParallelization` precedent does not apply.

## 2. Scope

**In**: `WhepAuthorizeRateLimitTests.cs` — observe the window boundary instead of inferring it; send each
fact's limiter-sensitive requests back to back; assert the preconditions the counts rest on; carry the
log tail on sentinel failures; one adversarial-phase fact as the regression guard.

**Out** — do not do:
- Raise `PermitLimit`, `Window`, `PollInterval`, `LogAbsenceGraceWindow` or any timeout. The defect is a
  phase assumption; a bigger number moves it.
- `AppHost.cs`, `Program.cs`, `AspireFixture*.cs` (`RecentLogs`' default of 120 lines included) — the
  log-tail question (§1.4) is diagnosed first, not fixed blind.
- Retrying a fact on failure.
- Other classes sharing the partition.

## 3. User story

### US1 (P1) — A rate-limit class whose counts hold at any window phase

As the maintainer reading red integration runs, I want `WhepAuthorizeRateLimitTests` to begin each
limiter-counting fact at an **observed** window boundary, so that the class stops failing ~1 run in 4
on `develop` for reasons unrelated to the change under test, and a failure that remains names its real
cause instead of "never appeared in the log tail".

**Why P1 / only story**: it is the whole defect. The diagnostics (FR-004/005) are part of the same
slice — without them a residual failure is as unreadable as today's.

**Independent test**: the adversarial-phase fact (§7) fails against today's protocol and passes after;
the six existing facts pass unmodified in their assertions.

## 4. Acceptance scenarios

```gherkin
Feature: rate-limit facts synchronise on an observed window boundary

  Scenario: happy — a fact aligned to a fresh window sees exactly its budget
    Given the fact has observed a refusal followed by an admission
    Then a window boundary lies between those two requests
    And exactly PermitLimit - 1 further requests are admitted
    And the next request is refused with 429

  Scenario: conflict — a fact that starts just before a boundary
    Given the previous activity leaves the window one heartbeat short of its boundary
    When a limiter-counting fact starts
    Then it aligns to the next observed boundary before sending its counted requests
    And none of its "must be admitted" requests is refused
    And its "must be refused" request is refused

  Scenario: bad request — a precondition the count rests on does not hold
    Given a request the fact's arithmetic requires to be admitted is answered 429
    Then the fact fails naming that request's index and status
    And it does not report "never appeared in the log tail"

  Scenario: auth / anonymous — the sentinel's own answer is the handler's
    Given the sentinel carries no token and no action
    Then it is answered 403 (WHEP_ACTION_UNKNOWN) before any log is read
    And a sentinel that is answered 403 but not found in the tail fails with the retained tail attached
```

## 5. Functional requirements

- **FR-001 Align, don't infer.** Before sending any request whose outcome it counts, every
  limiter-counting fact MUST align: send until a `429` is observed (bounded — at most
  `PermitLimit + 1` sends, else fail "window never refused"), then poll until a non-`429` answer
  (deadline `AdmissionRecoveryTimeout`, interval short enough that the boundary is known to lie within
  it — 50 ms). The admitted probe consumed one permit, so the fact's **budget is `PermitLimit − 1`**, and
  the next boundary is ≥ ~9.9 s away.
- **FR-002 Back to back.** After alignment, a fact MUST send all of its counted requests before it
  polls any log. Log assertions (sentinel presence, transition count, throttled-marker absence) run
  afterwards. Log-line order is unaffected: each request's handler has logged before its response
  returned.
- **FR-003 No phase guess.** `WaitUntilAdmittedAgainAsync`'s trailing `Task.Delay(Window)` and its S9
  rationale MUST be removed. Post-fact recovery (condition only: poll until admitted) moves to
  `IAsyncLifetime.DisposeAsync`, so it also runs when a fact fails — today a failed fact skips it and
  poisons the next one (35845632059).
- **FR-004 Preconditions asserted.** Every request a fact's arithmetic requires to be admitted MUST be
  asserted not `429`, naming its index; every sentinel MUST be asserted `403` before its log is polled.
  (Extends spec 208 review S7, which did this for the refused side only.)
- **FR-005 Tail on failure.** Every sentinel-presence failure message MUST include
  `aspire.RecentLogs(StreamDistributionResource, lines: 400)`, as the transition-count assertions
  already do.
- **FR-006 Assertions unchanged.** The spec-208 claims each fact asserts (429 at the ceiling, handler not
  reached, 429 declared, health untouched, exactly one transition record, no second record) MUST be
  asserted exactly as strongly after the change: same expected statuses, same expected counts.
- **FR-007 No numbers moved.** `PermitLimit`, `Window`, `PollInterval` (for log polls),
  `AdmissionRecoveryTimeout` and `LogAbsenceGraceWindow` keep their values.
- **FR-008 Class documentation.** The class `<summary>` and `WaitUntilAdmittedAgainAsync`'s comment MUST
  describe the aligned protocol and why the delay was removed (the heartbeat arithmetic of §1.2), so the
  next reader does not reintroduce "one `Window` after admission".

## 6. Success criteria

- **SC-1** The adversarial-phase fact is observed red against today's protocol and green after.
- **SC-2** The six existing facts pass locally before and after (4a capture, 4b re-run).
- **SC-3** Prediction, recorded in the PR as a prediction, not an observation: no `WhepAuthorizeRateLimitTests`
  failure of the §1.1 shapes on `develop` in the following runs; any residual failure names a status or
  carries a log tail.

## 7. Phase 4a colour — both, split exactly

**RED — the adversarial-phase fact.** New fact
`A_fact_that_starts_just_before_a_window_boundary_still_counts_one_whole_window`: pre-position
deterministically (exhaust; poll at 50 ms until admitted; `Task.Delay(Window − 150 ms)`), so the fact
body starts one to two heartbeats **before** the next boundary; then run the throttled sequence through
the class's alignment seam (`BeginCountingAsync`, plan §3) and assert every admitted-expected request
is admitted and the next is refused. In 4a the seam is today's behaviour made explicit — a no-op
returning `PermitLimit` — so the body is today's 49 + sentinel + throttled, and the fact fails by
straddle. **Quote the verbatim failure in the PR.** Run it three times in 4a; a straddle at ~150 ms
before the boundary is expected every time, but if any run is green, record it rather than tune the
offset silently.

**CHARACTERISATION, GREEN — the six existing facts.** 4a restructures them onto the seam (no-op, budget
`PermitLimit`: arithmetic identical to today), adds FR-002's reordering and FR-004/FR-005's
precondition assertions, and captures them **passing** against the live stack. 4b then implements the
seam (FR-001, FR-003) and **must not edit a fact body or an assertion** — only `BeginCountingAsync`,
`WaitUntilAdmittedAgainAsync`/`DisposeAsync` and the documentation (FR-008). An assertion that has to
change in 4b is evidence the behaviour moved: block, don't adjust.

Why this is honest: the thing that changes in 4b is the harness's synchronisation, and the adversarial
fact is what observes it; the spec-208 behaviour the other six assert is unchanged by construction.

## 8. Independent end-to-end test procedure

1. Stop any running AppHost first (one machine, one stack) — **ask the user**; a run-mode stack from
   `D:\Github\sse-2526` was up at phase 1.
2. `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~WhepAuthorizeRateLimitTests"` — 7
   facts green. Then the same filter plus `WhepAuthIntegrationTests|WhepHandshakeLatencyTests` — the
   partition's other consumers still green after this class.
3. Counterfactual: set `BeginCountingAsync` back to the no-op → the adversarial fact red, the other six
   green; restore; `git diff` empty.
4. Wall time for the class recorded in the PR (expected roughly unchanged: the removed trailing
   `Window` delay is traded for an alignment wait of ≤ one `Window`, plus ~25 s for the new fact).

## 9. Contention and spec number

No open PR or remote branch (other than stale `origin/main`) touches `WhepAuthorizeRateLimitTests.cs`,
`AspireFixture*.cs` or `StreamDistribution/Api/Program.cs` (checked 2026-09-25 against all 14 open PRs).
Re-verified 2026-09-27 against `origin/develop` and every active local worktree: **271** is free. No
open PR or remote branch touches `WhepAuthorizeRateLimitTests.cs`, `AspireFixture*.cs` or
`StreamDistribution/Api/Program.cs` other than this one.

## 10. Phase 4b finding — the aligned seam's own probe is a real, debounced transition (2026-09-27)

PR #2648's Docker-integration CI run (job 108665467074, run 36335155612) failed exactly two facts —
`A_partition_entering_the_throttled_state_is_logged_once` (`transitionLogCount` expected `1`, was `0`)
and `Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record` (`afterTransition`
expected `1`, was `0`). All other facts, including the four other characterisation facts and the
adversarial fact, passed (233/235 total). The scratch harness in §1.3 could not have caught this: it
drove a bare `PartitionedRateLimiter` directly and never modelled the mechanism below, which lives
entirely in `Program.cs` and is orthogonal to the limiter's own counting.

**The mechanism** (`StreamDistribution/Api/Program.cs:36-52`,
`IsNewWhepAuthorizeThrottleTransition`): the FR-009 "log once per transition" guard is an
`IMemoryCache` entry per partition, set with an **absolute expiration of `whepAuthorizeWindow`**
(10 s in the integration lane) the moment a transition is first logged. It is a pure time debounce —
keyed only on partition (IP), with no awareness of the rate limiter's own window generation and no
reset on an intervening admission. Any further transition on the same partition inside that 10 s is
silently suppressed, however it was caused.

**Why the seam trips it.** `BeginCountingAsync()` (plan.md §3.1 4b) opens by sending until refused —
a *real* `429`, which is a real, first-of-its-kind transition on the shared partition and therefore
logs and caches the debounce entry — then polls until admitted and returns. Every fact that calls it
now causes exactly this event, milliseconds before running its own counted sequence. For most facts
that is invisible: they do not read the transition log. The two FR-009 facts do, and their own
throttled request lands well inside the 10 s the alignment step's own transition just started, so
`IsNewWhepAuthorizeThrottleTransition` correctly returns `false` for the fact's *own* refusal and
nothing new is logged.

**This did not exist before this spec.** Under the pre-271 protocol, the analogous event — a recovery
probe forcing a transition — happened in the *previous* fact's `WaitUntilAdmittedAgainAsync` teardown,
fully decoupled from the next fact's own transition-log assertions. Moving boundary observation into a
seam called at the *start* of the fact that reads the log is what brings the two events inside one
fact's own execution for the first time.

**Disposition.** `Program.cs`'s debounce is out of scope (§2) and is correct, intentional production
behaviour on its own terms — a flood guard is not obliged to survive a test deliberately manufacturing
a transition on the same IP moments earlier. `A_partition_entering_the_throttled_state_is_logged_once`
and `Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record`'s own assertions were
built on a premise — that the alignment step is invisible to the transition-log signal — that phase 4b
disproved.

**Resolution (escalated to and decided by the product owner directly, not by an implementer).** An
anchor-based fix (repositioning what the two facts count "since") was designed and found insufficient
on closer analysis: the debounce's absolute TTL starts ticking at `BeginCountingAsync()`'s own forced
refusal regardless of where a later reader anchors, and the fact's own action always lands well inside
that same 10 s window under the fast-aligned protocol — no anchor position changes whether the
debounce is still hot. The decision taken: **restore the precondition these two facts' assertions were
always written against, rather than change the assertions.** A second alignment helper,
`BeginCountingWithClearDebounceAsync()`, does the same forced-refusal alignment as
`BeginCountingAsync()` but then waits `Window + 2s` — comfortably longer than both the limiter's own
window and the debounce's TTL (numerically the same duration) — before returning. The wait is passive
(no request sent): the limiter's heartbeat replenishes on its own schedule independent of traffic, so
by the time the wait ends the window is fully fresh (all `PermitLimit` permits, hence this helper
returns `PermitLimit` rather than `BeginCountingAsync`'s `PermitLimit - 1`) and the debounce has fully
expired. This mirrors what the pre-271 protocol provided by accident (the previous fact's own trailing
`Task.Delay(Window)` incidentally left the debounce cold by the time the next fact ran) and provides it
on purpose, only for the two facts that read the transition-log signal directly.

**Not a fact-body edit.** Only these two facts' first line changes — which alignment helper they call.
No assertion, expected value, or failure message in either fact changed. The two facts pay a ~12 s
alignment cost the other four do not; everything else in this spec (the seam, the adversarial fact, the
other four facts) is unaffected.
