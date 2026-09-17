# Spec 171 — Three timing assertions that can fail

**Issue**: #2150 · **Branch**: `fix/2150-thresholds-that-can-fail` · **Phase**: 1 (Specify)

**ADRs**: ADR-0037 (phased workflow), ADR-0144 (autonomous lane, phase-4a colour),
ADR-0139 (new behaviour starts red), ADR-0052 (xUnit + Shouldly), ADR-0053 (test
naming), ADR-0103 (integration tests against the Aspire fixture, no
Testcontainers), ADR-0115 (fab-keyed overlay resolution — the change spec 014's
figures bracket), ADR-0036 (smallest possible change).

**No new ADR is needed.** This is test-correctness work against a template that
already exists in the tree (`ResolvedTextReachesItsFabTests.cs`). No architectural
decision is made or changed. If phase 2 or 4 finds a row that needs one, stop and
say so.

---

## Why

Spec 087's census (#2141) found assertions whose threshold is unreachable by
construction: the test reports green whether or not the behaviour it names
occurs. #2150 groups four of them. **This spec takes three.**

A threshold nobody can reach is worse than no test at all, because the suite
reports a guard where there is none, and the next person reads the green as
coverage.

## Scope — three rows of the four

| # | Test | Defect | Remedy here |
|---|---|---|---|
| 1 | `NFR_VariableResolutionLatencyTests` | 800 ms bound against a 6 ms observation — 133x | **Re-anchor** to the observation |
| 3 | `ReconnectReconcileIntegrationTests` | Asserts against the very token that bounds its own loop | **Rewire** to an independent bound |
| 4 | `A_reconnect_after_a_success_does_not_inherit_the_previous_backoff` | 4 ms backoff cap checked against a 500 ms window | **Rewire** to a cap commensurate with its window |

### Out of scope, explicitly: `WhepHandshakeLatencyTests`

`tests/Integration.Tests/StreamDistribution/WhepHandshakeLatencyTests.cs` is
**not touched by this spec** — no edit, no comment change, no constant change,
nothing.

#2150 names retirement as its candidate remedy, and #2149's comment strengthens
the case with measured figures (p95 13 ms and 20 ms against a 3000 ms budget —
115-230x). **It is still not ours to act on.** Spec 077 reasoned exactly this far
and filed retirement as a P2 *human decision it deliberately did not act on*. A
human has declined once, on purpose. Retiring a test is a deletion of coverage,
which ADR-0144 places outside the autonomous lane's authority regardless.

It is named here so phase 4 does not pick it up as "obviously related" — it is
the row most likely to be swept in by a reader fixing its three neighbours.

**Row 5 is also out of scope.** #2149's comment adds
`AelInterpreterBenchmarkTests` as a fifth instance of the same class. It is not
one of #2150's four and is not taken here.

## Latency-budget impact (constitution §IV)

**Row 1 sits on leg 4, *event → overlay state* (<= 200 ms).** No production code
on that leg changes, so no leg's figure moves. What changes is whether the test
watching it can fail.

**§IV's per-leg table is not amended.** Leg 4 reads *recorded, not yet readable*,
and §IV defines that as the production metric being unreadable outside its
process (#1707, #1940) — a dashboard obligation, not a test-threshold one.
Re-anchoring a test bound does not discharge it. **Phase 4 must not edit
`.specify/memory/constitution.md`.**

Rows 3 and 4 map to no §IV leg. Row 3 is a reconcile-after-reconnect path; row 4
is a broker reconnect. Neither is on the event-to-overlay path — the same
distinction `specs/003-layout-composition/plan.md:75` draws for the SignalR
revocation path.

---

## Premise check against `develop` (a4a36ea1), by content

All citations in #2150 are by line number and several have moved. Verified by
content:

| Claim | State | Note |
|---|---|---|
| `LegBudgetMs = 800` | **Holds** | Now `NFR_VariableResolutionLatencyTests.cs:72`; census cited `:59` |
| Observed 6 ms / worst 8 ms | **Holds** | `specs/014-…/tasks.md:238-239` — post-ADR-0115 median 6 ms, worst 8 ms, budget 200 ms. Baseline pre-change was 9 / 11 |
| The assertion is on the **median** | **Holds, and #2150 does not say so** | `measured.Sort(); median = measured[count/2]; median.ShouldBeLessThan(LegBudgetMs)`. The bound is 133x a *median*, not a max |
| `CancellationTokenSource budget` self-reference | **Holds** | `ReconnectReconcileIntegrationTests.cs:88` constructs it from `ReconcileBudgetSeconds`; the loop at `:91-103` runs while `!budget.IsCancellationRequested`; the assertion at `:107-109` compares `elapsed` to that same constant |
| 4 ms cap against a 500 ms window | **Holds** | `MqttConnectionLoopTests.cs:153` takes the `Start(int)` overload, which defaults to `Brisk()` = **1 ms first / 4 ms cap**. `:161-162` waits 500 ms |
| `WhepHandshakeLatencyTests` still live at 3000 | **Holds** | `:46 P95BudgetMilliseconds = 3000`, no category trait |
| #2141 / spec 087 as described | **Holds** | #2141 is **CLOSED**; `specs/087-which-assertions-cannot-fail/census.md:242` carries §2e and `:264` the §2f template verbatim |
| #2119 is **open** | **STALE — it is CLOSED** (2026-09-10) | Filed before this issue's premise was written. Its guidance still governs: *"Do not simply raise the number to make a red run green … a budget widened to fit an observation stops being a budget."* Cited as the standard, not as a live case |
| #2149 / spec 123 recorded figures without adjusting thresholds | **Holds** | `specs/123-the-figure-beside-the-threshold` exists; `ReconnectReconcileIntegrationTests` was deliberately excluded from its nine |

### Two findings the issue does not contain

**A. Row 3's reconcile is synchronous, so its 5 s window waits for nothing.**
`GetLayoutQueryHandler` reads `ILayoutQuerySource` — EF against the same Postgres
the archive command committed to, and the archive POST's `EnsureSuccessStatusCode`
has already returned before the reconnect. There is no projection, no outbox, no
replication lag on this path. The first GET after reconnect must already see
`Archived`. So the poll loop is not waiting for propagation; and a 5 s bound on a
single warm HTTP round trip would reproduce row 1's defect in a new place even if
the self-reference were removed. **The bound must be anchored to a measured round
trip, not to a round number.**

**B. Row 4's arrangement no longer matches what "a success" means.** The test
drops the connection *immediately* after it comes up. Since `ResetIfHeld`
replaced the unconditional `Reset()`, the backoff clears only for a connection
that outlived **twice its own served wait** — so an immediate drop is a
connection that did **not** hold, and the loop is correct to leave the backoff
alone. The test's name asks for the opposite. Vacuity is currently hiding this:
the 4 ms cap makes both outcomes pass. Fixing only the window would turn the test
red against correct code.

The sibling doc comment at `MqttConnectionLoopTests.cs:234-239` is stale for the
same reason — it says the two cases differ by *"repetition, not the first
reconnect"*, but `ResetIfHeld` distinguishes by **hold duration**, not by
repetition.

---

## User stories

### US-1 (P1) — A latency regression on leg 4 fails the suite

**As** an engineer changing the system-variable resolution path, **I want** the
NFR test to fail when the leg slows by an order of magnitude, **so that** a
regression is caught in CI rather than on a kiosk.

Today a change taking the leg from 6 ms to 500 ms passes.

### US-2 (P2) — A slow reconcile is distinguishable from an absent one

**As** an engineer changing the layout read path, **I want** the reconnect
reconcile test to fail when the reconcile is slow, **so that** "slow" and
"absent" produce different failures.

Today a reconcile taking 3 s passes, and a reconcile taking 6 s fails on the
*state* assertion, naming the wrong defect.

### US-3 (P3) — A backoff that is not cleared fails the suite

**As** an engineer changing `MqttBackoff` or `MqttConnectionLoop`, **I want** the
inherit-the-backoff test to fail when the backoff is not cleared after a
connection that held, **so that** a recovery regression on the ingest leg is
caught.

Today deleting `backoff.ResetIfHeld(...)` outright leaves this test green.

---

## Acceptance scenarios (Gherkin)

### US-1

```gherkin
Scenario: happy — the leg is fast and the test passes
  Given the SystemVariables resolve path is unmodified
  When Value_change_reaches_the_resolved_overlay_text_within_the_leg_budget runs
  Then the median of 5 measured rounds is printed unconditionally
  And the median is below the regression ceiling
  And the test passes

Scenario: conflict — a regression the old bound waved through
  Given a 150 ms delay is injected into the resolve path
  When the test runs
  Then the median exceeds the regression ceiling
  And the test fails, quoting the median and the ceiling
  # Against the removed 800 ms bound this same run passes.

Scenario: bad-request — the leg never resolves
  Given the resolved text never carries the new value
  When the test runs
  Then MeasureOneChangeAsync throws a TimeoutException after 10 s
  And the failure names the overlay and the expected value
  # Unchanged; the 10 s poll ceiling is not the assertion and stays as it is.

Scenario: auth — an unauthenticated client cannot take the measurement
  Given no admin token
  When the fixture client is created
  Then the request is rejected and the test errors rather than reporting a figure
  # Covered by AspireFixture.CreateAdminClientAsync; no new assertion.
```

### US-2

```gherkin
Scenario: happy — the reconcile is prompt
  Given a Published layout archived while the client was disconnected
  When the client reconnects and refetches
  Then the observed state is Archived
  And the elapsed time is printed unconditionally
  And the elapsed time is below the reconcile ceiling

Scenario: conflict — a reconcile that is slow but arrives
  Given the archive lands 3 s after the reconnect
  When the test runs
  Then the observed state is Archived
  And the timing assertion fails, quoting the elapsed seconds
  # Against the removed self-bounded 5 s this same run passes.

Scenario: bad-request — the reconcile never happens
  Given the layout is never archived
  When the wait window expires
  Then the state assertion fails naming Archived
  And the failure is a state failure, not a timing one

Scenario: auth — the reconnected client presents its token
  Given the hub connection supplies the admin access token
  When the client re-Starts
  Then the handshake succeeds
  # Existing behaviour; no new assertion.
```

### US-3

```gherkin
Scenario: happy — a connection that held clears the backoff
  Given a loop on the patient backoff that was refused twice then connected
  And the connection is held well past twice its served wait
  When the connection drops
  Then the reconnect is observed within the prompt window
  And the elapsed milliseconds are printed unconditionally

Scenario: conflict — the backoff is inherited
  Given backoff.ResetIfHeld is not called after the drop
  When the connection drops
  Then the reconnect waits out the grown delay
  And the assertion fails, quoting the elapsed milliseconds
  # Against the removed Brisk/500 ms pairing this same run passes.

Scenario: bad-request — the loop never reconnects at all
  Given the loop is stopped
  When the connection drops
  Then the assertion fails on a reconnect that never came

Scenario: auth — each attempt mints a fresh credential
  Given the loop reconnects
  Then a freshly minted credential is presented
  # Covered by Each_attempt_presents_a_freshly_minted_credential; untouched.
```

---

## Independent end-to-end test procedure

No production behaviour changes, so "end to end" here means: **each corrected
assertion is observed failing against the defect it names, and passing without
it.** Run from the repo root, one row at a time.

1. **Row 1** — boot the Aspire fixture (one stack at a time on this machine).
   Run `NFR_VariableResolutionLatencyTests` and read the printed median off the
   output. Inject a 150 ms delay into the SystemVariables resolve path, re-run,
   and observe the failure quoting the median. Revert; re-run; observe green.
2. **Row 3** — run `ReconnectReconcileIntegrationTests` and read the printed
   elapsed figure. Move the archive POST onto a task that fires 3 s after the
   reconnect, re-run, and observe the *timing* assertion fail with the state
   assertion passing. Revert; re-run; observe green.
3. **Row 4** — no stack needed. Run
   `A_reconnect_after_a_success_does_not_inherit_the_previous_backoff` and read
   the printed elapsed figure. Comment out `backoff.ResetIfHeld(...)` in
   `MqttConnectionLoop.HoldConnectionAsync`, re-run, observe the failure. Revert;
   re-run; observe green.

Each of the three must additionally be shown **green under its own injection
with the old threshold in place** — that is the evidence the old bound was
vacuous rather than merely loose.

---

## Locked tech choices

- xUnit + Shouldly, hand-written fakes (ADR-0052). No new test dependency.
- Integration rows run against the `AspireFixture` (ADR-0103). No Testcontainers.
- Row 4 is a pure unit test against `FakeMqttClient`; it needs no stack.
- Every figure is printed with `Console.WriteLine` / `ITestOutputHelper`
  **unconditionally**, never only inside a Shouldly `customMessage` — spec 123's
  finding that six of seven budgets computed their figure on every green run and
  threw it away.
- Threshold comments follow `ResolvedTextReachesItsFabTests.cs:121-131`:
  threshold, observation, and the arithmetic between them, plus why the margin is
  the size it is.

## Success criteria

- **SC-1** Each of the three assertions is observed failing under its
  counterfactual, with verbatim output quoted in the PR.
- **SC-2** Each of the three is observed passing, unmodified, against `develop`'s
  production code.
- **SC-3** Each threshold carries an inline comment stating threshold,
  observation and arithmetic, in the template's shape.
- **SC-4** Each of the three prints its figure on a green run.
- **SC-5** `WhepHandshakeLatencyTests` is byte-identical to `develop`.
- **SC-6** No production code differs from `develop` at merge — every injection
  is reverted.
- **SC-7** No threshold is moved by a formula applied three times: each carries
  its own derivation.

## Assumptions, marked

- **A1** The 6 ms / 8 ms figures are from spec 014's machine, not this one. Phase
  4 **re-measures** before anchoring row 1 (memory: a measurement run needs
  repeating — the first run after machine churn looks exactly like a regression).
- **A2** Row 3 has no recorded figure at all. Its ceiling **cannot** be chosen
  before phase 4 measures it. This spec fixes the *rule* for deriving it, not the
  number.
- **A3** Row 4's injected-defect delay is derived from `MqttBackoff`'s own
  arithmetic, read from source, not observed. Phase 4 confirms by running.

## Out of scope

- Retiring, editing, or commenting on `WhepHandshakeLatencyTests`.
- `AelInterpreterBenchmarkTests` (#2149's fifth row).
- Any change to `MqttBackoff`, `MqttConnectionLoop`, `GetLayoutQueryHandler`, or
  any SystemVariables production code.
- Any edit to `.specify/memory/constitution.md` or `docs/adr/`.
- Closing #2141, #2119 or #2149 — all three are already closed.
