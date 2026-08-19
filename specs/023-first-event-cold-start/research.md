# Research: The first event after a restart reaches its effect in time

**Feature**: `023-first-event-cold-start` · **Phase 0** · 2026-08-19

Six questions, answered by reading the code before running anything. Two of the
four candidate causes from #1655 are already weakened, one is strengthened, and a
seventh finding turned up that changes the shape of the whole feature.

---

## The finding that reorders everything: the journey is not traced

**Decision**: the path must be made observable before the seconds can be
attributed. That is the first work item, not a side quest.

`ServiceDefaults/Extensions.cs` configures tracing with exactly three sources:

```csharp
tracing.AddSource(builder.Environment.ApplicationName)
    .AddAspNetCoreInstrumentation(...)
    .AddHttpClientInstrumentation();
```

Referenced instrumentation packages: `AspNetCore`, `Http`, `Runtime`. That is
all.

So **every hop this feature is about is invisible**: the Wolverine publish, the
RabbitMQ transit, the handler execution in the receiving service, and every
database call. What is traced is inbound HTTP and outbound HTTP — which is the
part of this system that is *not* on the event-to-overlay path.

The consequence is blunt: the system has an observability stack, an ADR
committing to it (0026), and a constitutional latency budget on this exact
journey (§IV) — and no way to see where time goes on it. Spec 022 could measure
the total because it watched the two ends. Nothing can currently say which hop
owns the middle.

**Rationale**: attribution is the P1 deliverable (FR-001). Without spans across
the message hops the only alternatives are coarse bucketing or hand-placed
stopwatches, and hand-placed stopwatches are production changes that exist only
to answer one question and then rot.

**Alternatives considered**: hand-rolled timing logs at each handler — rejected
as instrumentation with a shelf life, and it would have to be threaded through
three services to cross the same joins. Reading existing logs and correlating by
timestamp — viable as a cross-check but the clocks are per-process and the log
lines were not written to be joined. The coarse split below is kept precisely
because it needs nothing at all.

**Cost**: Wolverine emits its own OTel activities, and the package is already
referenced — this is a source registration, not a new dependency. Whether
database spans are wanted too is deliberately deferred: `Npgsql` instrumentation
*would* be a new package, and it should not ride in on the back of this unless
the measurement shows the database owns time.

**Risks to respect**: tracing has its own cost, and the first export can itself
be slow. FR-005 (no steady-state regression) applies to the instrumentation as
much as to any fix, and the observer effect must be checked rather than assumed
away — if adding spans changes the number, that is itself a finding.

---

## Q1 — Does the ingest loop simply sleep?

**Answer: no, not on the path that matters.** The obvious explanation for a
multi-second first event is a poll loop, and spec 020 rewrote this loop, so it
was the first thing to check.

`PersistenceLoopHostedService.RunCycleAsync` reads with
`channel.ReadBatchAsync(BatchSize, cancellationToken)`, which blocks until at
least one delivery is available and returns immediately when one is. The
`Task.Delay(backoff, …)` is on the **retry** branch only — reached when
something previously failed to store. A first, healthy event takes the arrival
branch and waits for nothing.

**Ruled out** as the cause of the cold cost, and worth having ruled out: it is
the explanation most people would reach for first.

---

## Q2 — Is Wolverine's first publish per message type expensive?

**Answer: the leading hypothesis, and the only one that explains the shape.**

`WolverineDefaults` configures:

```csharp
opts.UseRabbitMq(new Uri(rabbitConnection))
    .AutoProvision()
    .UseConventionalRouting(routing => routing.QueueNameForListener(...));
```

`AutoProvision` declares exchanges, queues and bindings. Conventional routing
resolves a route **per message type**. Both are the kind of work that happens
once per type, involves broker round trips, and is invisible until the first
message of that type is sent.

**Why this is the strongest candidate**: it is the only one that predicts a
*staged decay* rather than a single step. The journey publishes three distinct
integration events —

| Message | From → To |
|---|---|
| `FabEventIngestedV1` | EventIngestion → Automation |
| `SystemVariableValueRequestedV1` | Automation → SystemVariables |
| `OverlayHighlightRequestedV1` | Automation → LayoutComposition |

— and the four tests in `EventReachesItsEffectsTests` exercise different
subsets. A per-type cost paid by whichever test happens to send that type first
produces exactly the ~13 s → ~4 s → ~0.3 s curve observed, and also explains why
the *order* of the tests changed which one was slowest between runs.

**This is a prediction, not a conclusion.** It is falsifiable and should be
falsified or confirmed explicitly (FR-003): if it holds, publishing each message
type once at startup collapses the curve; if the curve survives that, the
hypothesis is wrong.

---

## Q3 — Is the outbox schema built on the first message?

**Answer: no.** `opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate`
runs during host start, not on first publish. Schema creation is therefore
already paid before any event arrives, and is not in the first-event path.

**Ruled out**, though it does mean startup already carries some of this cost —
relevant to US2's constraint that warming must not push a service past its
readiness gate.

---

## Q4 — Is Automation's rule cache loaded lazily on first evaluation?

**Answer: not in the measured scenario, and the wider question is a separate
concern.**

`IRuleCache` is registered as a singleton `InMemoryRuleCache`, and
`PublishRuleCommandHandler` upserts into it when a rule is published.
`RuleEvaluator` reads `cache.LookupActive(...)`. The measurement publishes its
rule seconds before the event, so the cache is already warm when the event
arrives.

**Weakened as a cause here.** But the reading surfaces a different question worth
recording and *not* chasing in this feature: if the cache is only ever populated
by publish commands, what fills it when Automation restarts with rules already
Active in the database? Either something hydrates it at startup — which is a real
cost in the restart path and squarely in scope — or nothing does, and that is a
correctness bug considerably more serious than latency. **Establish which before
assuming either.**

---

## Q5 — Does the MQTT subscriber back off before its first delivery?

**Answer: no fixed backoff exists.** `MqttSubscriberHostedService` contains no
`Task.Delay`, retry interval or reconnect timer — the only match for the search
was a log call on a failed reconnect token.

**Ruled out** as a source of a fixed multi-second delay, though connection and
subscription setup could still cost time and will show up in the coarse split
below.

---

## Q6 — How is the journey split without changing anything?

**Decision**: bucket the elapsed time with observables that already exist, as a
first cut and a permanent cross-check on the span data.

Three timestamps, all available through existing interfaces:

| Mark | Observable | Bounds |
|---|---|---|
| t0 | the MQTT publish returns | — |
| t1 | the event is readable via the EventIngestion read API | ingress + store |
| t2 | the variable is readable with its new value | announce + decide + apply |

This cannot say *which* of announce/decide/apply owns t2−t1, so it does not
satisfy SC-001 on its own. What it does is decide, before any instrumentation is
added, whether the seconds are before or after the event is durable — and those
two halves have entirely different suspects.

**Rationale**: it costs nothing, needs no production change, and gives an
independent number to check the spans against. If the spans and the buckets
disagree, one of them is wrong and it is better to find that out here than in a
conclusion.

---

## What this means for the plan

1. **Make the journey observable**, then measure. Not the other way round.
2. **Take the free coarse split first** — it narrows the search before any change
   exists to argue about, and it survives as a cross-check.
3. **Test the per-type publish hypothesis directly**, because it is the only one
   that explains the decay, and record the result either way.
4. **Answer the rule-cache-on-restart question**, because one of its two possible
   answers is a correctness bug rather than a latency one.
5. **Treat the instrumentation as subject to FR-005** like any other change:
   measure the warm path before and after.

The observability gap is the finding most likely to outlive this feature. A
latency budget on a path nothing can see is a budget nobody can enforce, and it
is worth deciding whether that deserves its own ADR amendment rather than a
line in a spec.
