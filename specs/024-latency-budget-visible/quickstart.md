# Quickstart: Every leg of the latency budget can be watched

**Feature**: `024-latency-budget-visible`

"Done" is the observations, not the walk. Record them on the PR.

**Read this first.** The deliverable is **not six instrumented legs.** Phase 0
found two legs that can be measured, three that do not exist to measure, and one
that is arithmetic. Step 4 — writing down what cannot be measured and why — is
the largest part of the output and the one most likely to be skipped because it
produces no code.

## 1. Confirm the leg is unmeasurable today

Before adding anything, try to answer the question the feature exists to make
answerable: **what is the p99 of the event-to-overlay leg right now?**

| Expect | |
|---|---|
| the answer | that it cannot be obtained without writing code |

If it can be obtained, stop — the premise has changed and that is the finding.

## 2. Measure the one leg that is implemented

| Expect | |
|---|---|
| a distribution | not a most-recent value; a budget is about the tail |
| a percentile | obtainable without writing code (SC-001) |
| the budget alongside it | 200 ms, so a reader who does not know the constitution can tell a pass from a breach (FR-003) |
| what it excludes | delivery to a kiosk — legs 2, 3 and 5 — stated, not implied |

**The measurement must span what ADR-0015 says the leg spans.** A histogram
around one handler produces a number that looks like the budget and is not.

## 3. Turn on the SFU's metrics

MediaMTX supports them and the AppHost does not enable them.

| Expect | |
|---|---|
| camera → SFU | visible against its 80 ms budget |
| the media path | unaffected — a camera still streams |

Cheapest win in the feature: config, not code.

## 4. Write down the legs that cannot be measured — the step that will get skipped

Four of six. Three because nothing implements them; one because it is a
subtraction.

| Leg | Why not |
|---|---|
| SFU → kiosk decode | the kiosk renders no video — no `<video>`, no `RTCPeerConnection` |
| Presentation buffer | PTP is a "future-add" per spec 002; nothing implements it |
| Composite + render | overlays render, but over nothing |
| Headroom | the remainder of the other five against 800 ms |

| Expect | |
|---|---|
| each | a recorded reason, what was tried, and what would unblock it (FR-007) |
| the reader | able to tell "not built" from "built but unmeasured" — different problems |

**This is not an apology section.** That the 800 ms path is not assembled end to
end is the most consequential thing this feature learned.

## 5. Establish what the instrumentation cost

| Expect | |
|---|---|
| before and after | on the warm path, same method both times |
| the exporter's state | **confirmed first** |
| the overhead | under 5% of the measured leg's budget (SC-004) |

Spec 023 ran this comparison and then had to record that it might be vacuous —
the OTLP exporter only attaches when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and
nobody had checked whether the fixture sets it. A before/after over a pipeline
that exports nothing measures nothing. **Check first, then compare.**

## 6. Say what a PR author should do

§IV requires every PR on this path to demonstrate the budget still holds, which
nobody has been able to do.

| Expect | |
|---|---|
| a procedure | someone who did not build this can follow it (SC-005) |
| its output | a figure, with what it does not establish attached |

## 7. Say where this leaves §VII

| Expect | |
|---|---|
| the honest summary | two legs measured, four not, §VII still unmet |
| the options | amend §VII, accept the gap with a reason, or treat the unbuilt legs as blocking |
| the decision | **the reviewer's** |

A feature that closes an issue by explaining why it cannot be closed has to say
so at the end, plainly. Discovering it at review is worse.
