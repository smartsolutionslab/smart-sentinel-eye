# Tasks — Spec 171, three timing assertions that can fail

**Spec**: `spec.md` · **Plan**: `plan.md` · **Issue**: #2150 · **Phase**: 3

---

## Declarations required at phase 3 (ADR-0144)

### Engineer per row

| Row | Agent | Why |
|---|---|---|
| 1 | `backend-engineer` | .NET integration test against two backend contexts over HTTP; the injection is C# in `src/SystemVariables` |
| 3 | `backend-engineer` | .NET integration test against LayoutComposition; the injection is test-side C# |
| 4 | `backend-engineer` | .NET unit test against `EventIngestion.Infrastructure`; the injection is one line in `MqttConnectionLoop` |

**All three are `backend-engineer`.** No frontend or infra work. The rows span
three test projects and four bounded contexts, but every change is C# in `tests/`
and every injection is C# — no row crosses into a different agent's territory.

**Phase 4a is `test-writer`, and it is the substance of this feature.** The
deliverable *is* the tests. The phase-4b engineer pass has **no production code to
write**; its job is to apply nothing and confirm nothing is left behind (SC-6).
That is unusual and is stated here so it is not read as an omission.

### Phase 4a colour: RED on every row, by counterfactual

**Every row is RED, not characterisation.** Each is a genuine behavioural
correction to what the test can observe: before, it could not fail under either
the good or the bad case; after, it must fail under the bad case specifically.

**The red is observed against an injected defect, not against `develop`.** A
corrected assertion is green against correct production code — that is the point
of it. So "observed red first" is discharged the way this repo already discharges
it for guards: construct what the assertion claims to catch and watch it catch it
(memory: *prove a guard by counterfactual* — it disproved three guards' own claims
in a day). ADR-0139's gate is satisfied by the **verbatim failure output under the
injection**, quoted in the PR.

Each row must produce **four** verbatim outputs, in this order:

1. old threshold + injection → **green** (proves the old bound was vacuous, not
   merely loose);
2. new threshold + injection → **red**, quoting the figure and the bound;
3. injection reverted, new threshold → **green**, with the figure printed;
4. the figure from (3) recorded in this file.

Output (1) is what makes this evidence rather than assertion. Without it the PR
claims the old bound could not fail; with it, the PR shows it.

---

#### Row 1 counterfactual — a real regression in variable-resolution latency

**Defect to reintroduce**: a latency regression on the *event → overlay state*
leg, of a size the old bound waved through.

**Injection**: add `await Task.Delay(150, cancellationToken);` at the top of
`src/SystemVariables/Application/Queries/Handlers/GetOverlaySnapshotQueryHandler.cs`
— the handler behind `GET /system-variables/snapshot`
(`SystemVariableEndpoints.cs:67`), which is the path `MeasureOneChangeAsync`
polls. One line, one file.

**Why 150 ms and not a smaller number**: it is chosen to sit *between* the two
bounds, and that is the whole demonstration.

| | old bound 800 ms | new bound 100 ms |
|---|---|---|
| median with injection ~156 ms | **passes** | **fails** |

**Why a delay rather than reverting ADR-0115**: spec 014 records global-keyed at
median 9 ms against fab-keyed at 6 ms. Reverting the real optimisation moves the
figure by 1.5x — not past either bound. It would demonstrate nothing. The
injection must be an order-of-magnitude regression because that is the class of
defect this ceiling exists to catch, and the plan's derivation says so.

**Marked as a stand-in.** A `Task.Delay` is not a real regression; it is a
faithful *stand-in for the magnitude* of one. It proves the assertion fires at the
right size, which no run of the 800 ms bound could.

**What the old test could not do**: fail. At 800 ms a leg breaching its own 200 ms
§IV budget by 3x still passes.

---

#### Row 3 counterfactual — a reconcile that is slow, not absent

**Defect to reintroduce**: a reconcile that eventually succeeds but takes far
longer than a warm round trip.

**Injection**: move the archive POST off the synchronous path — fire it on a task
that awaits ~3 s *after* `client.StartAsync()` returns, instead of before the
reconnect. Test-side only; no production file changes.

**Why an eventually-successful delay and not a missing archive**: omitting the
archive makes `observedState.ShouldBe("Archived")` fail — which the *old* test
already does, at `:106`, before the timing assertion speaks. That is the census's
own observation (§2e) and it would prove nothing about the timing bound. The
defect that separates the two versions must be one where the **state assertion
passes and only the timing assertion can object**.

**Why 3 s specifically**:

| | old self-bounded 5 s | new ceiling (<= 2 s by the plan's hard stop) |
|---|---|---|
| elapsed ~3 s, state Archived | **passes** — 3 < 5 | **fails**, quoting ~3.0 s |

3 s is also below the new 30 s window, so the state assertion passes on both
versions. The only thing that changes verdict is the timing bound — which is
exactly the isolation this counterfactual needs.

**What the old test could not do**: distinguish 3 s from 30 ms. Its bound *was*
its timeout, so `elapsed` could not materially exceed 5 s however slow the
reconcile got.

---

#### Row 4 counterfactual — a backoff that does inherit the previous window

**Defect to reintroduce**: the backoff is not cleared after a connection that
held, so the next reconnect waits out the delay the outage grew to.

**Injection**: comment out `backoff.ResetIfHeld(Stopwatch.GetElapsedTime(connectedAt));`
in `MqttConnectionLoop.HoldConnectionAsync`
(`src/EventIngestion/Infrastructure/Ingress/MqttConnectionLoop.cs`). One line.

**Why this is the right injection**: it is precisely the production line the test
is named after. No stand-in needed — this row's counterfactual is the real defect,
not a proxy for its magnitude.

| | old (Brisk 1/4 ms, 500 ms window) | new (Patient 100/400 ms, 900 ms hold, 150 ms window) |
|---|---|---|
| reset removed | reconnect waits ~4.8 ms → **passes** | reconnect waits 320-480 ms → **fails** |
| reset present | ~0 ms → passes | ~0 ms → passes |

**What the old test could not do**: fail with the reset deleted outright. Its
whole observable range (0 ms to 4.8 ms) fitted inside its own 500 ms window.

**Check the injection stays local**: with `ResetIfHeld` removed, the sibling spin
tests back off *more*, not less, so they do not go red and mask the signal. Phase
4 runs the target test alone under injection and confirms this claim rather than
assuming it.

### New ADR

**None expected.** This is test-correctness work matching an existing template
(`ResolvedTextReachesItsFabTests.cs:121-131`); no architectural decision is made.
**If any row turns out to need one, stop and report** — ADR-0144 forbids the lane
writing an ADR or amending the constitution.

The nearest thing to a decision is row 3's hard stop at 2 s: if the measured
reconcile demands a ceiling above it, that is a finding about the read path and
goes back to a human, not into a constant.

### `WhepHandshakeLatencyTests` — untouched, and this is the confirmation

`tests/Integration.Tests/StreamDistribution/WhepHandshakeLatencyTests.cs` **is not
in scope and phase 4 must not modify it in any way** — not the
`P95BudgetMilliseconds = 3000` constant, not a category trait, not a comment, not
a whitespace change.

It is row 2 of #2150's four. #2150's own remedy for it is *retirement*, and
#2150 records that spec 077 already reasoned that far and **deliberately filed the
retirement as a human decision it did not act on**. A human has declined to act
once, on purpose. Retiring a test also deletes coverage, which ADR-0144 places
outside the lane's authority independently of that.

**It is the row most likely to be swept in by accident**, because a reader fixing
three timing thresholds will find a fourth in the same category one directory
away. T010 verifies by `git diff` that it was not.

---

## Tasks

`[P]` marks tasks that own disjoint files and may run in parallel (ADR-0109).

### Foundational — blocks everything

- [ ] **T001** Read `ResolvedTextReachesItsFabTests.cs:121-131` and
  `specs/087-which-assertions-cannot-fail/census.md` §2e and §2f before touching a
  constant. The template's *shape* — threshold, observation, arithmetic, why this
  margin — is the acceptance criterion for T003, T005 and T008, and it must be
  read rather than paraphrased from this plan.

### US-3 (row 4) — no Aspire stack required, so it goes first

Taken first because it is the only row that needs no stack, so it cannot be
blocked by contention and its evidence lands while the stack is in use elsewhere.

- [x] **T002** `[P]` `[US-3]` Re-arrange
  `A_reconnect_after_a_success_does_not_inherit_the_previous_backoff` in
  `tests/EventIngestion.Infrastructure.Tests/MqttConnectionLoopTests.cs`:
  `Start(client => client.RefuseNextConnects(2), LoopUnderTest.Patient())`, a
  900 ms hold before `DropAsync()`, a `Stopwatch` around the wait, an
  unconditional print of the elapsed ms, and the window tightened to a new
  `PromptReconnectWindow` constant of 150 ms. Depends on T001.
- [x] **T003** `[P]` `[US-3]` Write the derivation into the new constants' doc
  comments per `plan.md` row 4: the two populations (~0 ms vs 320-480 ms), where
  320-480 comes from (`100 x 2^2` capped at 400, jittered [0.8, 1.2]), why the hold
  is 900 ms (1.9x the worst-case `servedDelay * 2` yardstick), and why 150 ms
  rather than 50 or 250. Depends on T002.
- [x] **T004** `[P]` `[US-3]` Correct the stale paragraph at
  `MqttConnectionLoopTests.cs:234-239`: the two tests differ by **hold duration**,
  which is what `ResetIfHeld` measures, not by "repetition". Depends on T002.
- [x] **T005** `[US-3]` Counterfactual, four outputs in order: (1) old arrangement
  + `ResetIfHeld` commented out → green; (2) new arrangement + injection → red,
  quoting the elapsed ms; (3) injection reverted → green with the figure printed;
  (4) record the figure in this file. Confirm the sibling spin tests do not also
  go red under the injection. Revert the injection. Depends on T002-T004.

### US-1 (row 1) — needs the Aspire stack

- [x] **T006** `[US-1]` Re-measure `NFR_VariableResolutionLatencyTests` against
  unmodified `develop` code, **twice, on separate fixture boots**, and record both
  medians and worsts in this file. Anchoring before measuring is the defect this
  spec exists to remove. Depends on T001. **Serialises with T009 on the stack.**
- [x] **T007** `[US-1]` Rename `LegBudgetMs` → `RegressionCeilingMs` and set it to
  **100 ms**, or re-derive at 10x the worst observed sample if T006's median
  exceeds 20 ms. Rewrite the doc comment in the template's shape: the 6 ms / 8 ms
  provenance (`specs/014-.../tasks.md:238-239`), the `[8, 8, 10]` local sample,
  T006's figures, the 10x arithmetic, why the margin exceeds the template's 6.6x
  (warm dev-box medians vs the template's cold-stack figures), and the load-bearing
  half — that 100 ms sits **below** the 200 ms §IV leg it guards, where 800 ms sat
  above it. Depends on T006.
- [x] **T008** `[US-1]` Counterfactual, four outputs in order: (1) 800 ms bound +
  a 150 ms delay injected into the SystemVariables resolve path → green; (2)
  100 ms bound + the same injection → red, quoting the median; (3) injection
  reverted → green with the median printed; (4) record the figure here. Mark the
  delay explicitly as a stand-in for a regression's magnitude, not a real
  regression. Revert the injection. Depends on T007.

### US-2 (row 3) — needs the Aspire stack

- [ ] **T009** `[US-2]` Rewire `ReconnectReconcileIntegrationTests`: split
  `ReconcileBudgetSeconds` into `ReconcileWindow` (30 s, governing the token only)
  and `ReconcileCeilingMs` (governing the assertion only), replace
  `DateTime.UtcNow` with a `Stopwatch`, and print the elapsed figure
  unconditionally. Depends on T001. **Serialises with T006 on the stack.**
- [ ] **T010** `[US-2]` Measure: run the rewired test **at least 5 times across 2
  fixture boots**, record every `elapsed` figure here, then set
  `ReconcileCeilingMs` to 6-7x the worst, rounded up, checking it stays at least
  4x below the 30 s window. **Hard stop at 2 s** — if the derivation exceeds it,
  do not write the number: stop, report the figures, and escalate, because a warm
  synchronous EF read over 300 ms is a finding about the read path. Depends on
  T009.
- [ ] **T011** `[US-2]` Write the derivation into `ReconcileCeilingMs`'s doc
  comment in the template's shape, including the fact that the reconcile is
  **synchronous** (`GetLayoutQueryHandler` reads `ILayoutQuerySource` over the same
  Postgres the archive already committed to), so the next reader does not mistake
  the ceiling for a propagation allowance. Also correct the class summary, which
  says "within 5 seconds of reconnect". Depends on T010.
- [ ] **T012** `[US-2]` Counterfactual, four outputs in order: (1) the old
  self-bounded 5 s + the archive delayed ~3 s past the reconnect → green; (2) the
  new ceiling + the same injection → red on the **timing** assertion with the
  state assertion passing; (3) injection reverted → green with the figure printed;
  (4) record the figure here. Depends on T011.

### Gate checks

- [ ] **T013** `[P]` Confirm `git diff origin/develop --
  tests/Integration.Tests/StreamDistribution/WhepHandshakeLatencyTests.cs` is
  **empty** (SC-5). Depends on T005, T008, T012.
- [ ] **T014** `[P]` Confirm `git diff origin/develop --stat -- src/` is **empty**
  (SC-6) — every counterfactual injection reverted. Depends on T005, T008, T012.
- [ ] **T015** `[P]` Run the three test classes together, green, and confirm each
  prints its figure on the green run (SC-4). Depends on T005, T008, T012.
- [ ] **T016** Record all measured figures in this file and confirm each of the
  three derivations is independent — no single multiplier applied three times
  (SC-7, #2141's prohibition on the bulk fix). Depends on T013-T015.

---

## Dependencies and parallelism

```
T001 (read the template)
 ├─ T002 → T003 ┐
 │           T004 ┤→ T005   (row 4, no stack)
 ├─ T006 → T007 → T008      (row 1, STACK)
 └─ T009 → T010 → T011 → T012  (row 3, STACK)
                                  ↓
                     T013 [P] T014 [P] T015 [P] → T016
```

- **T002/T003/T004 are `[P]` against T006 and T009**: row 4 owns
  `MqttConnectionLoopTests.cs`, rows 1 and 3 own files in `tests/Integration.Tests`.
  Disjoint files (ADR-0109).
- **T006 and T009 are NOT `[P]` with each other**, and neither is `[P]` with T008
  or T012, despite owning disjoint files. **This machine runs one Aspire stack at
  a time** (memory: two concurrent boots give `FailedToStart` that reads exactly
  like a code defect). The stack is the shared resource, not the files.
- **T013-T015 are `[P]`**: three independent read-only checks.

### Aspire stack

Phases 1-3 needed **no stack** — everything above is reading source, the census,
and the two closed issues. **The stack was not stopped** (pid 3312 stays up).

Phase 4 needs it for T006, T008, T009, T010, T012 (rows 1 and 3), **serially**.
Row 4 (T002-T005) needs none and should be taken while the stack is busy or
before it is claimed.

---

## Board (ADR-0037 phase-3 gate)

The gate is the **feature-level issue on Project #13**. #2150 is already on the
board (project *Smart Sentinel Eye*, status **Todo**, label `agent:ready`), so no
`item-add` is needed. No per-task issues: `[TNNN]` issues stopped after spec 028.

---

## Measured figures — filled in by phase 4

| Row | Run | Figure | Threshold set | Multiple |
|---|---|---|---|---|
| 1 | boot 1 (T006) | median 19 ms, worst 23 ms, [9, 14, 19, 20, 23] | | |
| 1 | boot 2 (T006) | median 34 ms, worst 69 ms, [18, 26, 34, 34, 69] | | |
| 1 | boot 3 (T006, extra — see note) | median 19 ms, worst 34 ms, [11, 13, 19, 30, 34] | | |
| 1 | boot 4 (T006, extra — see note) | median 46 ms, worst 99 ms, [16, 28, 46, 66, 99] | | |
| 1 | counterfactual (1): 800 ms bound + 150 ms injection (T008) | median 219 ms, worst 276 ms | 800 ms | green (vacuous) |
| 1 | counterfactual (2): 100 ms bound + 150 ms injection (T008) | median 182 ms, worst 208 ms | 100 ms | red |
| 1 | counterfactual (3): injection reverted, 100 ms bound (T008) | median 59 ms, worst 102 ms | 100 ms | green |

**Note on T006's 4 boots instead of 2**: the plan asked for two; boots 1-2 disagreed enough (median 19 vs 34 ms, worst 23 vs 69 ms) to warrant more evidence before anchoring, so 2 more were taken. All four medians stayed well under the chosen 100 ms ceiling (worst case 46 ms, >2x margin), so no further boots were taken after 4. See the constant's doc comment in `NFR_VariableResolutionLatencyTests.cs` for the full reasoning, including a conflict this measurement exposed between the template's "10x worst observed" rule and the "must stay below the 200 ms §IV budget" rule — resolved in favour of the budget constraint, which is load-bearing.
| 3 | 5 runs / 2 boots | _pending T010_ | | |
| 4 | reset path (green, unmodified code, 3 runs) | 12, 13, 12 ms | 150 ms | ~12x headroom |
| 4 | old arrangement + injection (counterfactual output 1) | passes regardless — 4.8 ms worst case fits inside the 500 ms window | 500 ms | vacuous, as before |
| 4 | new arrangement + injection (counterfactual output 2) | did not reconnect within the window (backoff inherited, floor 320-480 ms per `MqttBackoff.Next()`) | 150 ms | correctly red |
