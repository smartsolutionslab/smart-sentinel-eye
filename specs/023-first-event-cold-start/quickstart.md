# Quickstart: The first event after a restart reaches its effect in time

**Feature**: `023-first-event-cold-start`

"Done" is the observations, not the walk. Record them on the PR.

**Read this first.** The deliverable is an **explanation**, not a smaller number.
A warm-up applied before the measurement would make the figure drop while leaving
nobody able to say what the seconds had been — and an unexplained improvement is
indistinguishable from a hidden one. **Step 3 is what this feature is for.**
Steps 1 and 2 exist to make it possible, and step 5 is only worth doing once
step 3 has an answer.

## 1. Reproduce the gap

Start from a cold stack and send one event, then two more.

```sh
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
  -c Release --filter "FullyQualifiedName~EventReachesItsEffects" \
  --logger "trx;LogFileName=cold.trx"
```

| Expect | |
|---|---|
| first event | 12–14 s from publish to effect |
| second | seconds, not tenths |
| third | ~0.3 s |

If the curve is not there, **stop** — the premise of the feature is gone and that
is the finding. Note the test execution order: whichever runs first pays, so the
order is part of the observation, not noise.

## 2. Split it without changing anything

Three marks, all through interfaces that already exist:

| Mark | Observable |
|---|---|
| t0 | the publish returns |
| t1 | the event is readable through the EventIngestion read API |
| t2 | the variable is readable with its new value |

| Expect | |
|---|---|
| a verdict | whether the seconds are before or after the event is durable |

Two halves, entirely different suspects. This does not satisfy SC-001 — it cannot
say which of announce, decide or apply owns t2−t1 — and that is fine. It costs
nothing, it needs no production change, and **it stays afterwards as the
cross-check on the spans.** If the buckets and the spans disagree later, one of
them is wrong.

## 3. Attribute the seconds — the step the feature exists for

Only now, with the hops made observable, read the spans for the first three
events after a restart.

| Expect | |
|---|---|
| attribution | **≥ 80%** of the elapsed time landed on named stages (SC-001) |
| the largest share | owned by a stage identified by name |
| the decay | explained as specific stages getting cheaper, not an aggregate falling (SC-002) |
| every candidate in #1655 | confirmed or refuted **in writing**, including the refuted ones (SC-003) |

**"It is diffuse" is not an answer.** If 80% cannot be attributed, the
instrumentation is not yet good enough and that is the next task, not a
conclusion.

Test the hypothesis that predicts the curve — first-publish-per-message-type
cost — directly: send each message type once at startup and see whether the curve
collapses. **If it does not, the hypothesis is wrong and the note says so.** A
prediction that is only reported when it succeeds is not evidence.

## 4. Answer the rule-cache question

Cheap, and one of its two answers is not a latency problem at all.

`InMemoryRuleCache` is populated by publish commands. Restart Automation with a
rule already Active in the database and send a matching event.

| Outcome | What it means |
|---|---|
| the rule fires | something hydrates the cache at startup — that cost is in scope here |
| the rule does not fire | **a correctness bug**: rules stop working after a restart. File it immediately, separately, and do not fold it into this feature |

## 5. Close the gap, if step 3 says how

| Expect | |
|---|---|
| first event after restart | under 1 s (SC-004) |
| steady state | no worse than 267–348 ms (SC-005) |
| startup | the added time stated, and readiness still honest (FR-006, FR-007) |
| the suite | passes, nothing excluded or weakened (FR-008, SC-006) |

If the cause is not addressable, record the reason and the residual risk. **That
is a permitted ending**, not a failure — the obligation was that the number stops
being unexplained.

## 6. Say what it does not establish

The fixture runs nine services and a broker on one host. Spec 020 was explicit
that a figure taken there is not a figure about a fab, and spec 022 repeated it.
It applies here unchanged.

State the numbers, what they establish, and what they do not. If the cold cost
turns out to be contention for one machine rather than anything the system does,
**say that plainly** — it is a real result, it means the gap likely does not
reproduce in a fab, and the observability it took to find it is worth keeping
either way.
