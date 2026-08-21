# Quickstart: The event-to-overlay leg can be measured

**Feature**: `025-measure-event-to-overlay`

"Done" is the observations. Record them on the PR.

**Read this first.** Step 1 is a question, not a change, and answering it may
delete most of the rest of this feature. Skipping it to "just add the field"
would build a private mechanism beside a general one that might already work.

## 1. What does the trace do at the outbox?

Send one event; read the trace for it.

| Expect | one of |
|---|---|
| context propagates | the automation `receive` has a `parentSpanId` from event-ingestion's `send` |
| context is linked | separate traces, joined by a span link |
| context is dropped | separate traces, no link — the automation `receive` is a root |

Spec 024's captures suggest the third, but were taken for another purpose and
never examined for this. **Confirm before choosing.**

| If | then |
|---|---|
| available or linked | derive the leg from spans; no contract change |
| dropped | carry the moment on the message, and file the trace gap separately |

## 2. Measure the leg

| Expect | |
|---|---|
| a distribution | with a high percentile, from the running system |
| `is_whole_leg` | **true**, and true |
| a missing moment | records **nothing** — never zero |
| a negative duration | records **nothing** — PTP steps clocks |

Both of those last two flatter the dashboard, which is the direction of error
this codebase has been caught by four times.

## 3. Reconcile — the step that catches a fragment

Compare against spec 022's `EventReachesItsEffectsTests`, which measures the same
journey from outside.

| Expect | |
|---|---|
| agreement | within 20% on the same events |
| disagreement | explained **before** either number is quoted anywhere |

An instrument reporting a plausible number for the wrong span cannot detect its
own error, and someone will cite it.

## 4. What it cost

| Expect | |
|---|---|
| before and after | same method both times |
| overhead | under 5% of the leg's 200 ms budget |

The exporter is attached in the fixture (spec 024 T002), so unlike spec 023's
first attempt this comparison is not vacuous.

## 5. Tell the truth in §IV

| Expect | |
|---|---|
| the leg's row | measured **yes**, dashboard **no** |
| §VII | half-discharged for this leg, and visibly so |

ADR-0117 warned that a stale row exempts a leg by clerical error. This is the
first feature to change a row; if the discipline does not hold now it never will.
