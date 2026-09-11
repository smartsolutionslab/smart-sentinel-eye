# Spec 133 — The event-to-overlay metric measures the leg it is named after

**Issue:** #2173 (split from #2072) · **Branch:** `fix/2173-the-metric-measures-the-leg`
**ADRs:** ADR-0015 (latency budget), ADR-0117 (dashboards bind implemented legs),
ADR-0102 (`EventMetadata`), ADR-0141, ADR-0105, ADR-0049
**Constitution:** §IV (the latency budget), §VII (observability)

## The defect

`ILatencyBudget.RecordEventToOverlayState` names §IV's *event → overlay state*
leg — *"from the plant-floor event being accepted to its effect being applied"*.
Two handlers feed it, and they measure two different spans.

| Handler | Fires at | Measures the leg? |
|---|---|---|
| `LayoutComposition/…/OverlayHighlightRequestedV1Handler.cs:49` | **after** `broadcaster.OverlayHighlightedAsync` | yes |
| `SystemVariables/…/SystemVariableValueRequestedV1Handler.cs:100` | after the **value write**, before any push | no |
| `LayoutComposition/…/ResolvedOverlayTextChangedV1Handler.cs` | — records nothing | no |

For the variable effect the tile is pushed by `ResolvedOverlayTextChangedV1Handler`,
downstream of the write across an outbox relay, RabbitMQ and a context boundary.
#2072 timed that remainder server-side:

```
PUT returning → frame on a subscribed munich hub client:
  run 1: 555 ms
  run 2: 758 ms      (run twice; the repeat was the slower one)
```

So the instrument stops roughly 750 ms short of where the leg ends, under the
leg's own name. That is why a 200 ms budget could be breached by a factor of
three without the instrument saying so.

## Establishment — what was checked, and what moved

### 1. Which handlers publish to this leg

**Three, not two, and the third is the one with the problem.**

`RecordEventToOverlayState` has exactly two call sites in `src/` (grep:
`OverlayHighlightRequestedV1Handler.cs:49`, `SystemVariableValueRequestedV1Handler.cs:100`).
But the *pushes* that end this leg are three, because Automation fans one
ingested event out into two effects (`Automation/…/FabEventIngestedV1Handler.cs:76–105`)
and the variable effect reaches the wall by a longer route:

- **highlight** — `OverlayHighlightRequestedV1` → `OverlayHighlightRequestedV1Handler` → push. Measured correctly.
- **variable** — `SystemVariableValueRequestedV1` → value write → `VariableValueChangedDomainEvent` →
  `VariableValueChangedDomainEventHandler` → `ResolvedOverlayTextChangedV1` →
  `ResolvedOverlayTextChangedV1Handler` → push. **Measured at the first hop.**
- **variable, archived** — `VariableArchivedDomainEventHandler` also publishes
  `ResolvedOverlayTextChangedV1`. It has no plant-floor root: archiving is an
  operator action, never an Automation effect. It stays unmeasured, and that is
  correct rather than an omission — `EventMetadata.RootIngestedAt` null means
  *not measurable*, never *instant*.

**This changes the shape of the fix.** The recording cannot simply move a few
lines down, because `ResolvedOverlayTextChangedV1` does not carry the root:
both publishers construct `EventMetadata` with four positional arguments and
leave the optional fifth defaulted to `null`
(`VariableValueChangedDomainEventHandler.cs:68–72`,
`VariableArchivedDomainEventHandler.cs:101–105`). Grep for `RootIngestedAt` in
`src/` returns five hits and none of them is a propagation through
SystemVariables. **The leg's start is discarded at the write.** Moving the
recording therefore requires carrying it the rest of the way first.

### 2. Should `ResolvedOverlayTextChangedV1Handler` record at all?

**Yes — it is the only place that can, and it is the place the sibling handler
already records from.**

- It is where the variable effect is *applied* in the sense §IV means: the last
  moment the server owns before the frame is on the wire. `OverlayHighlightRequestedV1Handler`
  records at exactly this point for the sibling effect; leaving this one silent
  is what makes one leg report two different spans.
- The cost is one virtual call and a subtraction after an `await` that has
  already crossed a process boundary. Nothing is published, awaited or ordered
  differently — the recording goes *after* the broadcast, so it cannot delay it.
- It records **once per overlay**, as the highlight handler records once per
  highlight. A variable feeding four tiles is four arrivals on four tiles; one
  averaged figure would hide a slow arrival behind a fast one.

The corollary is that `SystemVariableValueRequestedV1Handler` must **stop**
recording, and loses its `ILatencyBudget` dependency. Keeping both would
double-count the leg with two different spans under one name — worse than today.

### 3. What the metric's consumers expect

**There is no dashboard and no retained historical series, so no reader is
holding a number this change will silently invalidate.** Checked:

- No dashboard definition exists anywhere in the repo — no Grafana JSON, no
  panel file. §IV's own table reads `Dashboard: no` for every leg, and #1940
  (is the column satisfied by a readable figure or a purpose-built view?) is
  open and not settled here.
- The only sink is the Aspire dashboard in dev/CI (ADR-0118). Its telemetry does
  not outlive the process — ADR-0118 lists *"dashboards that outlive a process"*
  among the reasons a production sink is still wanted. A production sink is
  deferred until there is a production deployment.
- So the series a reader would step across does not exist. What does exist, and
  what this spec must correct, is **where a reader learns what the number
  means**: the XML documentation on `ILatencyBudget.RecordEventToOverlayState`
  (`src/Shared.CQRS/ILatencyBudget.cs`) and on `LatencySegment.EventToOverlayState`
  (`src/ServiceDefaults/LatencyBudget.cs`). Both currently describe the leg
  correctly and were, before this change, describing something the code did not
  do for the variable effect.

**The step change is therefore a documentation obligation, not a migration
one.** Figures recorded from this instrument before this change measured the
write; figures after it measure the push. A reader comparing the two is
comparing two instruments. That sentence goes in both XML comments, in
`verification.md`, and in the PR body.

### 4. Premises checked against the tree

- The line numbers in the issue are right: `:49` and `:100` are the two call sites.
- **The issue's third bullet under-describes the work.** *"a third would change
  the shape of the fix"* — there is a third, and it does. The fix is not a
  moved line; it is a propagation plus a moved line.
- **The issue's third establishment item assumes a historical series exists.**
  It does not (above). The obligation survives in a different form.

## What is explicitly not in scope

#2072 keeps four decisions and none of them is taken here:

1. whether §IV's *Measured* cell moves, and to what;
2. whether 758 ms against a 200 ms budget is an ADR-class breach or a defect to
   optimise;
3. §IV's stale prose for this leg (*"now also suspected broken for an
   already-open tile"* — confirmed and fixed by #2012);
4. whether fixing the instrument should wait on any of the above.

**Fixing the instrument does not settle the budget question and must not be read
as settling it.** It sharpens it: an honest instrument makes the breach visible
continuously instead of once, in a phase-5 note nobody re-runs.

`.specify/memory/constitution.md` is **not edited** — a constitution edit is a
blocked outcome under ADR-0144. What §IV's cell should say is reported for a
human to file.

## Functional requirements

- **FR-001** The `event → overlay state` leg for the variable effect is recorded
  when the resolved-text frame has been pushed, not when the value is written.
- **FR-002** `ResolvedOverlayTextChangedV1` carries the plant-floor acceptance
  moment of the event that caused it, when it had one.
- **FR-003** `SystemVariableValueRequestedV1Handler` records nothing to the
  latency budget; the leg is not terminated at the write.
- **FR-004** An effect with no plant-floor root records nothing — the absent
  moment is handed to the budget, which declines it. Never a zero.
- **FR-005** A frame that is not broadcast (no fab) records nothing: a journey
  that never arrived is not a completed journey.
- **FR-006** A variable change affecting *n* overlays records *n* measurements,
  one per arrival.
- **FR-007** No change to what is published, what is awaited, or in what order.
  The recording is after the push and cannot delay it.
- **FR-008** Both places that document what this number means say that the
  measurement point moved and that older figures measured the write.

## Success criteria

- **SC-001** A handler run whose push takes *D* records a duration that includes
  *D*. Red before the change (nothing is recorded at all); green after.
  Counterfactual: with the recording placed *before* the broadcast the same
  assertion fails, so the test discriminates the span and not the call site.
- **SC-002** The acceptance moment survives from `SystemVariableValueRequestedV1`
  to `ResolvedOverlayTextChangedV1.Metadata.RootIngestedAt`. Red before.
- **SC-003** `SystemVariableValueRequestedV1Handler` reports no measurement. Red before.
- **SC-004** `dotnet build -c Release` clean; Domain ≥ 90 %, Application ≥ 80 %
  coverage gates unaffected.
