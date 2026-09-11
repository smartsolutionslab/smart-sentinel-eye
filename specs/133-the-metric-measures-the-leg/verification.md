# Verification — spec 133 (#2173)

## Phase 4a — red, observed before any source change

Commit `5ae8d1d6`, run against the tree at `92876859`.

### The span case (SC-001)

```
[xUnit.net]     ...ResolvedOverlayTextChangedV1HandlerTests.The_recorded_span_includes_the_time_spent_pushing [FAIL]
  Failed ...The_recorded_span_includes_the_time_spent_pushing [4 ms]
  Error Message:
   Shouldly.ShouldAssertException : latency.Recorded
    should have single item but had
0
    items and was
[] (System.Collections.Generic.List`1[System.Nullable`1[System.DateTimeOffset]])
```

### The relocated no-root case

```
[xUnit.net]     ...ResolvedOverlayTextChangedV1HandlerTests.An_effect_with_no_plant_floor_root_is_not_timed [FAIL]
  Error Message:
   Shouldly.ShouldAssertException : latency.Recorded
    should have single item but had
0
    items and was
[] (System.Collections.Generic.List`1[System.Nullable`1[System.DateTimeOffset]])

Failed!  - Failed:     2, Passed:    79, Skipped:     0, Total:    81 - SmartSentinelEye.LayoutComposition.Application.Tests.dll
```

### The propagation case (SC-002)

```
[xUnit.net]     ...SystemVariableValueRequestedV1HandlerTests.The_acceptance_moment_reaches_the_event_that_pushes_the_overlay [FAIL]
  Error Message:
   Shouldly.ShouldAssertException : push.Metadata.RootIngestedAt
    should be
28/05/2026 08:14:32 +00:00
    but was
null

Additional Info:
    the push is where this leg ends, so the moment it started has to arrive with it

Failed!  - Failed:     1, Passed:    81, Skipped:     0, Total:    82 - SmartSentinelEye.SystemVariables.Application.Tests.dll
```

**One of the four new cases was not red, and is recorded as a companion guard
rather than counted as evidence.** `A_frame_that_is_not_broadcast_is_not_timed`
passed before the change vacuously — the handler recorded nothing under any
condition — and guards the regression the change itself could introduce, namely
recording a drop as an arrival. `SetValue_stamps_the_causing_events_acceptance_moment_on_the_event`
was written alongside the implementation for the same reason and is likewise not
a red.

## The counterfactual — the test measures the span, not the call site

With the implementation in place and the recording moved to **before**
`broadcaster.ResolvedOverlayTextChangedAsync`, everything else identical:

```
  Failed ...The_recorded_span_includes_the_time_spent_pushing [91 ms]
  Error Message:
   Shouldly.ShouldAssertException : observed - Accepted
    should be greater than or equal to
00:00:00.7500000
    but was
00:00:00

Additional Info:
    the leg ends when the frame has been pushed, so the time the push itself took belongs inside the measurement
```

A test that only checked `RecordEventToOverlayState` had been called would have
passed in that state. This one reports `00:00:00` against a 750 ms floor, so the
assertion is on the interval. The handler was restored and the suite re-run.

## Phase 4b — green

Commit `89a2844b`.

```
dotnet build -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

| Project | Result |
|---|---|
| `LayoutComposition.Application.Tests` | Passed 81 / 81 |
| `SystemVariables.Application.Tests` | Passed 81 / 81 |
| `SystemVariables.Domain.Tests` | Passed 101 / 101 |
| `ServiceDefaults.Tests` | Passed 181 / 181 |
| `Architecture.Tests` | Passed 388 / 388 |
| `Automation.Application.Tests` | Passed 110 / 110 |

`SystemVariables.Application.Tests` totals 81 rather than 82 because
`An_effect_with_no_plant_floor_root_is_not_timed` **moved** to
`ResolvedOverlayTextChangedV1HandlerTests`, which is where the handler that owns
the measurement now lives. Its assertion is unchanged. Nothing was deleted to
reach green.

**One architecture guard failed mid-implementation and was obeyed, not
weakened.** `EventMetadataFabDeclarationTests` reads `EventMetadata`'s arguments
positionally from source text, and an inline comment plus a lambda inside the
argument list made it count seven arguments where the header takes five. The
expression was hoisted to a local above the fan-out loop — which also stops it
being recomputed per overlay — and the guard passes on its own terms.

## Latency — constitution §IV

**Leg: `event → overlay state` (≤ 200 ms).** This change is to the instrument
for that leg, not to the leg.

- **What the instrument measures now:** from `EventMetadata.RootIngestedAt` —
  EventIngestion accepting the plant-floor event — through to the server having
  pushed the overlay's new state onto `/hubs/layouts`. The same span for both
  effects that reach this leg: the highlight, unchanged since spec 025, and the
  variable, which now stops where the highlight does.
- **What it measured before, for the variable effect:** acceptance through to the
  value row being written. A prefix ending before an outbox relay, a broker hop
  and a context boundary — #2072 timed that omitted remainder at 555 ms and
  758 ms server-side.
- **No leg's actual cost changed.** Nothing is published, awaited or ordered
  differently; the added work is one interface call and a subtraction, after the
  broadcast has already returned. The threading adds one record component and one
  method parameter, no I/O.
- **The budget question is untouched.** Whether 758 ms against 200 ms is an
  ADR-class breach or a defect to optimise stays with #2072, as does whether
  §IV's *Measured* cell moves. An honest instrument makes the breach visible
  continuously rather than once, in a phase-5 note nobody re-runs — which
  sharpens that question rather than answering it.

## Where the series is read, and what was said about the step change

There is **no dashboard** — no Grafana JSON, no panel file anywhere in the repo,
and §IV's own table reads `Dashboard: no` for every leg. The only sink is the
Aspire dashboard in dev/CI, which does not outlive its process (ADR-0118 lists
*"dashboards that outlive a process"* among the reasons a production sink is
still wanted). So there is **no retained historical series** to step across, and
the issue's premise that one exists did not hold.

What does exist is where a reader learns what the number means. Both now say the
measurement point moved on 2026-09-11, what it used to stop at, and that a
figure from either side is not comparable with one from the other:

- `src/Shared.CQRS/ILatencyBudget.cs` — `RecordEventToOverlayState`
- `src/ServiceDefaults/LatencyBudget.cs` — `LatencySegment.EventToOverlayState`

Both also say the rise is the instrument lengthening, not the system slowing,
and that the budget question is #2072's.

## Not done, deliberately

`.specify/memory/constitution.md` was not edited — a constitution edit is a
blocked outcome (ADR-0144). §IV's *Measured* cell for this leg and its stale
prose (*"now also suspected broken for an already-open tile"*, confirmed and
fixed by #2012) are reported for a human to file, not changed here.

No end-to-end run: the interval under test is a handler's own, a fake clock
pins it more exactly than a wall-clock run would, and #2072 has already measured
the real remainder this instrument was missing.
