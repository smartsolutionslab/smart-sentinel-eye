# Plan — Spec 171, three timing assertions that can fail

**Spec**: `specs/171-thresholds-that-can-fail/spec.md` · **Issue**: #2150 · **Phase**: 2

## Bounded contexts and layers

No production layer is touched. Only test projects change.

| Row | Test project | Context exercised | Layer of the code under test |
|---|---|---|---|
| 1 | `tests/Integration.Tests` | SystemVariables + OverlayDesigner (via HTTP) | Api → Application → Infrastructure, across two contexts by HTTP only |
| 3 | `tests/Integration.Tests` | LayoutComposition (via HTTP + SignalR) | Api → Application → Infrastructure |
| 4 | `tests/EventIngestion.Infrastructure.Tests` | EventIngestion | Infrastructure (`Ingress`), against a hand-written fake |

**Boundary rules hold trivially** (ADR: no cross-context project references). Row
1 crosses SystemVariables and OverlayDesigner only over HTTP through the Aspire
fixture; no project reference is added. Row 4 references
`SmartSentinelEye.EventIngestion.Infrastructure.Ingress` only, which its project
already does.

**No entities, value objects, invariants, domain events or integration events are
added or changed.** There is no messaging change, so no domain-event →
integration-event mapping to record. This is deliberate and is the whole shape of
the feature: the defect is in what the tests can observe, not in what the system
does.

---

## The rule that governs all three, and why it is not a formula

`ResolvedTextReachesItsFabTests.cs:121-131` is the template. It states three
things and their relationship:

1. the **threshold** the assertion uses;
2. the **observation** it is anchored to, with provenance;
3. the **arithmetic** between them, plus *why that margin* — naming both failure
   modes the margin sits between (too loose to assert anything; too tight to
   survive a cold stack).

It also fixes a second ratio the census did not name: the template's ceiling
(5 s) sits **4x below its own wait window** (`FrameWindow` = 20 s), because *"a
bound at `FrameWindow` would assert nothing at all"*.

**Two independent ratios, therefore, and #2150's prohibition on a bulk fix is
about the numerators, not the ratios:**

- **R1 — headroom above the observation.** Large enough that CI jitter and a cold
  stack do not flake it; small enough that the regression the test exists to
  catch crosses it.
- **R2 — clearance below the window.** The bound must not be the wait window, or
  it asserts nothing.

Each row below derives its own numerator from its own observation and its own
failure mode. Where a row's observation does not yet exist, the plan fixes the
derivation rule and phase 4 supplies the number — it does **not** pre-commit one.

---

## Row 1 — `NFR_VariableResolutionLatencyTests`: re-anchor

**File**: `tests/Integration.Tests/SystemVariables/NFR_VariableResolutionLatencyTests.cs`

### What is wrong

`LegBudgetMs = 800` is derived from the **budget** (200 ms x 4), not from any
observation. The assertion is `median.ShouldBeLessThan(LegBudgetMs)` against a
recorded median of 6 ms — 133x. Worse than loose: **the bound is 4x above the
very §IV budget it claims to protect**, so a run that breached the constitution's
leg by 3x would still pass. A test cannot guard a budget from above it.

### Observation

- `specs/014-.../tasks.md:238-239` — post-ADR-0115 **median 6 ms, worst 8 ms**,
  5 measured rounds after 3 warmups, budget 200 ms. Pre-change baseline 9 / 11.
- The class remarks record a later local sample of `[8, 8, 10]`.
- Phase 4 **re-measures on this machine, twice**, and records both runs.

### Threshold: 100 ms

Derivation, all three parts, to be written into the constant's doc comment:

- **10x the worst sample anyone has recorded** (10 ms, from `[8, 8, 10]`), and
  ~16x the recorded median of 6 ms.
- **More headroom than the template's 6.6x**, deliberately: the template's
  555/758 ms figures were themselves *cold-stack* figures, whereas these 6-10 ms
  figures are warm dev-box medians taken after three warmup rounds. CI is neither
  warm nor a dev box, so the margin must absorb a difference in kind, not only in
  jitter.
- **Below the 200 ms §IV leg budget, and that is the load-bearing half.** At
  100 ms a breach of the constitution's leg fails *here first*. At 800 ms the test
  sat above the thing it was named after — the defect in one sentence.
- **Robust because the statistic is a median of 5, not a max.** For a warm median
  to reach 100 ms, three of five samples must be ~10x every figure ever recorded.
  That is a regression, not jitter. A max-based bound at this tightness would
  flake; a median-based one does not, and the assertion already uses the median.
- **R2**: the measured poll's own ceiling is 10 s (`MeasureOneChangeAsync`), so
  100 ms sits 100x below the window. No risk of asserting the window.

**Rename `LegBudgetMs` → `RegressionCeilingMs`.** The name is half the defect: it
tells the next reader the number is a §IV budget, which is precisely what
licensed multiplying it by 4. After re-anchoring it is a regression ceiling
anchored to an observation, and the name must say so.

**If phase 4's re-measurement shows a median above 20 ms**, the ceiling is
re-derived at 10x the worst observed sample rounded up to a readable number, the
new figure is recorded, and the derivation comment is rewritten to match. It is
never raised to accommodate a run without that arithmetic being restated (#2119).

---

## Row 3 — `ReconnectReconcileIntegrationTests`: rewire

**File**: `tests/Integration.Tests/OverlayDesigner/ReconnectReconcileIntegrationTests.cs`

### What is wrong

One constant, `ReconcileBudgetSeconds = 5`, plays two incompatible roles:

- `:88` — the `CancellationTokenSource` that decides **how long the loop runs**;
- `:107-109` — the bound `elapsed` is **asserted against**.

So `elapsed` cannot materially exceed 5 s: the loop stops at 5 s by construction.
The assertion is measuring "did my own timeout fire", which cannot separate a
prompt reconcile from a timeout-as-designed. If the reconcile never happens, the
state assertion at `:106` fails first and the timing assertion never speaks.

There is a second, smaller defect in the same three lines: `elapsed` is computed
from `DateTime.UtcNow`, whose resolution on Windows is ~15 ms — coarse relative to
the figure being measured.

### The independent bound

Split the one constant into two, with no arithmetic relating them:

| New name | Role | Value |
|---|---|---|
| `ReconcileWindow` | How long to keep polling before concluding the reconcile is **not coming**. Governs the token only. | **30 s** |
| `ReconcileCeilingMs` | The bound `elapsed` is asserted against. Governs the assertion only. | **derived by phase 4 — see below** |

`elapsed` comes from a `Stopwatch`, not `DateTime.UtcNow`.

**Why 30 s for the window.** Its only job is to tell "slow" from "absent". The
template uses 20 s for the same job; 30 s is chosen because this path includes a
SignalR re-handshake against a possibly-cold Aspire stack, and a window that
expires during a slow-but-working reconcile produces the *wrong* failure — a state
failure naming a defect that is not there. The window is allowed to be generous
precisely because it is no longer the assertion.

### Why the ceiling cannot be pre-committed, and the rule that fixes it

No figure for this path exists anywhere in the tree, and spec 123 excluded it
on purpose. Two facts constrain it:

- **The reconcile is synchronous.** `GetLayoutQueryHandler` reads
  `ILayoutQuerySource` — EF over the same Postgres the archive command committed
  to before the archive POST returned. There is no projection and no outbox on
  this path, so the **first** GET after reconnect must already see `Archived`.
  The figure being measured is one warm HTTP round trip, not a propagation delay.
- **Therefore 5 s would still be wrong** even with the self-reference removed. A
  5 s ceiling on a warm round trip is row 1's defect relocated — which is exactly
  what #2119 warns about and the reason a mechanical "keep the number, fix the
  wiring" fix is rejected here.

**Phase 4 derives the ceiling as follows, and records every step:**

1. Run the rewired test **at least 5 times across 2 fixture boots** (memory: a
   measurement run needs repeating) and record every `elapsed` figure.
2. Set `ReconcileCeilingMs` to **6-7x the worst observed figure**, rounded up to
   a readable number — the template's own R1 ratio, which applies here because,
   unlike row 1, this observation is already taken against a cold Aspire stack
   over a real HTTP hop.
3. Check R2: the ceiling must sit **at least 4x below `ReconcileWindow`** — the
   template's ratio. At a 30 s window that caps the ceiling at 7.5 s.
4. **Hard stop at 2 s.** If step 2 produces more than 2 s, do not write it: a
   warm synchronous EF read taking over 300 ms is itself a finding. Stop, report
   the figures, and let a human decide. A ceiling above 2 s would also fail US-2's
   counterfactual, which injects a 3 s reconcile.

The constant's doc comment states the observed figures, the multiple, and why the
margin sits where it does, in the template's shape — including the note that the
reconcile is synchronous, so the next reader does not mistake the ceiling for a
propagation allowance.

---

## Row 4 — `A_reconnect_after_a_success_does_not_inherit_the_previous_backoff`: rewire

**File**: `tests/EventIngestion.Infrastructure.Tests/MqttConnectionLoopTests.cs`

### What is wrong, read off `MqttBackoff`

`LoopUnderTest.Start(refuseFirstConnects: 4)` takes the `Start(int)` overload,
which defaults the backoff to `Brisk()` — **first 1 ms, cap 4 ms**. So the
largest delay the loop can possibly be carrying is 4 ms x 1.2 jitter = **4.8 ms**,
checked against a **500 ms** window. The two populations the test claims to
separate are ~0 ms and ~4.8 ms, and 500 ms contains both. **Nothing the loop
could do fails this assertion.**

### The arrangement defect that must be fixed with it

The test drops the connection **immediately** after it comes up. `ResetIfHeld`
clears the backoff only for a connection held for at least **twice its own served
wait**. An immediate drop is therefore a connection that did *not* hold, and the
loop is **correct** to keep the backoff. Tightening the window alone would make
this test fail against correct code — a false red, which is worse than the false
green it replaces.

So the arrangement must supply what the test's own name asserts: *a success*,
meaning a connection that held.

### The design, with the arithmetic from `MqttBackoff.Next()`

Switch to `Patient()` (first 100 ms, cap 400 ms) and two refusals:

```
LoopUnderTest.Start(client => client.RefuseNextConnects(2), LoopUnderTest.Patient())
```

| Step | `Next()` returns | Outcome |
|---|---|---|
| attempt 1 | `Zero` (`attempts++ == 0`) | refused |
| attempt 2 | 100 ms x [0.8, 1.2] = **80-120 ms** | refused |
| attempt 3 | 100 x 2^1 = 200 ms x jitter = **160-240 ms** | **connects**; `servedDelay` = 160-240 ms |

Then hold the connection for **900 ms** before dropping it.

- `ResetIfHeld`'s yardstick is `servedDelay * 2` = **320-480 ms**.
- 900 ms is **1.9x above the worst-case yardstick**, so the reset happens on
  every run regardless of where the jitter lands. Nothing marginal.
- The hold is deliberately *not* minimal: a hold near 480 ms would make the test
  flake on jitter alone, which is the failure mode the template names.

Then drop, and assert the reconnect is observed within **150 ms**.

| Case | Delay before the reconnect attempt | Verdict at 150 ms |
|---|---|---|
| Backoff cleared (correct) | `Next()` after a `Reset()` returns `Zero` — the loop's own scheduling only, observed in single-digit ms | **passes**, ~30x of headroom |
| Backoff inherited (the defect) | `attempts` stays at 3, so `Next()` returns 100 x 2^2 = 400 capped at 400, jittered = **320-480 ms** | **fails**, the floor of the bad population is 2.1x above the bound |

**Why 150 ms and not 50 or 250.** It is the midpoint of the only gap that exists:
above it sits nothing the correct loop produces (the reset path is bounded by a
`TaskCompletionSource` continuation and a 5 ms `WaitUntilAsync` poll), and below
it sits nothing the broken loop produces (320 ms floor). 50 ms would still be
correct but leaves only ~10x over the poll granularity on a loaded machine;
250 ms sits only 1.3x below the bad population's floor and would go green if the
jitter landed low. 150 ms is ~3x above the poll granularity and 2.1x below the
bad floor — clear of both, which is the property the template asks for.

**Elapsed is measured and printed.** `WaitUntilAsync` returns a bool, which
discards the figure — spec 123's mechanism finding. Row 4 wraps the wait in a
`Stopwatch` and prints the elapsed milliseconds unconditionally.

### The stale sibling comment, and why correcting it is in scope

`MqttConnectionLoopTests.cs:234-239` says this test and
`A_connection_that_dies_on_arrival_is_retried_with_a_delay_rather_than_a_spin`
differ by *"repetition, not the first reconnect"*. `ResetIfHeld` does not
implement a repetition rule — it distinguishes by **hold duration**. The comment
described the old unconditional `Reset()`.

After this change the two tests differ by exactly what the code measures: this
one holds 900 ms (past the yardstick, so the backoff clears), the sibling holds
zero (inside the yardstick, so it does not). Correcting that paragraph is not a
drive-by: it is the sentence that tells the next reader why the 900 ms hold is
there, and leaving it would document the new arrangement as contradicting a
sibling test when it does not.

---

## Files phase 4 may touch

| File | Change |
|---|---|
| `tests/Integration.Tests/SystemVariables/NFR_VariableResolutionLatencyTests.cs` | Rename + re-anchor the constant, rewrite its doc comment |
| `tests/Integration.Tests/OverlayDesigner/ReconnectReconcileIntegrationTests.cs` | Split the constant in two, `Stopwatch` for `elapsed`, print the figure, rewrite the doc comments |
| `tests/EventIngestion.Infrastructure.Tests/MqttConnectionLoopTests.cs` | Re-arrange row 4 (Patient, 2 refusals, 900 ms hold, 150 ms window, `Stopwatch`), add its constants, correct the stale sibling paragraph at `:234-239` |
| `specs/171-thresholds-that-can-fail/*.md` | Record the measured figures |

**Nothing else.** In particular not
`tests/Integration.Tests/StreamDistribution/WhepHandshakeLatencyTests.cs`, not
`src/EventIngestion/**`, not `src/LayoutComposition/**`, not
`src/SystemVariables/**`, not `.specify/memory/constitution.md`, not `docs/adr/`.

Counterfactual injections **do** touch production files temporarily. Each is
reverted in the same phase-4 step that quotes its output, and phase 5 confirms
`git diff origin/develop -- src/` is empty (SC-6).

## Constitution and ADR alignment

- **ADR-0036** — smallest possible change: no production code moves; each row is
  one constant, its doc comment, and where required its arrangement.
- **ADR-0052 / 0053 / 0054** — xUnit + Shouldly, sentence-style names (all three
  test names are unchanged), hand-written fakes. No new dependency.
- **ADR-0103** — rows 1 and 3 run against the `AspireFixture`. No Testcontainers.
- **ADR-0139 / 0144** — new behaviour starts red. Each row's red is observed
  against an **injected counterfactual**, because the corrected assertion is green
  against correct code by design. See `tasks.md` for the per-row declaration.
- **§II value objects** — not engaged: no domain model changes.
- **§IV** — row 1 sits on leg 4; no leg's figure moves and the per-leg table is
  not amended (reasoning in `spec.md`).
- **ADR-0084 code metrics** — the three files stay within 300 LOC and 30 LOC per
  method; row 3's test body is the one to watch, and extracting the poll into a
  private helper is permitted if it crosses 30 LOC.

## Risks

- **Row 1's 100 ms on a shared GitHub runner.** Mitigated by the statistic being
  a median of 5 after 3 warmups, and by CI history: a runner slow enough to push
  a warm median from 6 ms to 100 ms would already be failing the neighbouring
  budgets. If CI reds on this after merge, the response is to record the CI figure
  and re-derive at 10x — not to restore 800 ms.
- **Row 3's ceiling is unknown until measured.** Handled by the hard stop at 2 s:
  the spec fails loudly rather than encoding a number nobody derived.
- **Aspire stack contention.** One stack at a time on this machine (pid 3312 is
  running). Rows 1 and 3 both need it and must run serially. Row 4 needs none and
  can be taken first, in parallel with nothing else.
