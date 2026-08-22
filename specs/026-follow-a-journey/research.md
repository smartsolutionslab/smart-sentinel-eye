# Research: A cross-service journey can be followed end to end

**Feature**: `026-follow-a-journey` · **Phase 0** · 2026-08-22

Two findings. The first is a supported extension point that makes this much
cheaper than expected. **The second is that the spec's central argument is
wrong**, and correcting it changes which option is preferred.

---

## Finding 1 — Wolverine has a first-class hook for this

`WolverineOptions.MetadataRules` is a `List<IEnvelopeRule>`, applied to outgoing
envelopes:

```
Wolverine.IEnvelopeRule
    void Modify(Envelope)
    void ApplyCorrelation(IMessageContext, Envelope)

shipped implementations include:
    PropagateHeadersRule, PropagateOneHeaderRule, LambdaEnvelopeRule,
    MessageTypeRule, TenantIdRule, DeliverWithinRule, …
```

And `Envelope.Headers` is a `Dictionary<string, string>` that serialises with the
message.

So the send side needs **no invention**: a rule stamps whatever the far side
needs onto every outgoing envelope, using the mechanism Wolverine already uses
for tenancy, delivery windows and header propagation. FR-005 — "not a private
mechanism invented here" — is satisfiable directly.

Note `PropagateHeadersRule` exists at all: propagating context across messages is
an anticipated use, not something being bent to fit.

---

## Finding 2 — the spec's argument against parentage does not hold

The spec asserts, and I wrote it:

> Making the far side a direct continuation of the near side would produce a
> single unit of work whose duration is dominated by queue time — a
> twenty-millisecond journey reported as eight minutes, **in every percentile it
> appears in**.

**That is not how span duration works.** A span's duration is its own start to
its own end. A parent-child relationship does not extend the parent: the publish
span still ends when publishing ends, and the handling span still measures
handling. What grows is the **trace's** total elapsed time, which is the honest
statement that the journey really did take eight minutes.

And percentiles are computed over **span** durations, not trace durations. So the
claimed corruption of "every percentile" would not occur.

**The spec is wrong on this point and the plan does not inherit it.** US3's
outcome — that no *reported duration* grows — remains a correct and testable
requirement (SC-003). Its stated justification does not.

### What the real trade-off is

Having removed the wrong argument, three genuine ones remain, and they are
smaller:

| | Parentage | Link |
|---|---|---|
| Fan-in — one handler, many causes | expresses **one** parent only | expresses all of them |
| Sampling | the decision travels from a minutes-old context | decided locally |
| Trace listing | traces sorted by duration are dominated by queue time, making the list hard to read | unaffected |
| Cost to build | possibly **zero** — see below | a custom span with a link at each receive |

None of those is the dramatic failure the spec described. The strongest is
fan-in, and this system does not currently batch multiple causes into one
handler.

### And parentage may be free

`Envelope.CorrelationId` and `Envelope.ParentId` already exist, and
`WolverineTracing.StartReceiving(Envelope)` already builds the receive activity
from the envelope. If a metadata rule stamps those two fields into headers that
survive the outbox, **Wolverine's own tracing may reconstruct the relationship
with no custom span code at all.**

That is worth trying first, because it is the smallest change that could work and
it uses the library as designed.

---

## What this means for the plan

1. **Try the cheap route first**: a metadata rule that carries the context the
   outbox currently loses, and see whether Wolverine's own receive tracing joins
   the journey up. If it does, the feature is a rule and a test.
2. **Measure what happens to trace listings**, since that is now the strongest
   remaining argument for links. If traces become unreadable because every one is
   minutes long, links are worth the extra code.
3. **Correct the spec** rather than leaving a wrong justification in a document
   people will read. The requirement survives; the reasoning does not.
4. **Keep SC-003 exactly as written.** "No reported duration grows" is still the
   right check — it is just now a guard against an unlikely failure rather than
   the expected one, and it costs nothing to verify.

**The lesson is the same one this programme keeps relearning.** The spec's
argument was plausible, internally consistent, and written with confidence by
someone who had not checked how span duration is defined. It survived a
checklist. What caught it was looking at the mechanism before building on the
claim.

---

## Finding 3 (T003) — the outbox does not lose the context

`EnvelopeSerializer.Serialize(Envelope)` → `Deserialize(byte[])` round-trips
`ParentId`, `CorrelationId` **and** custom `Headers` intact. Measured, not read
off a schema:

```
BEFORE ParentId=00-707f5a290bbf0a1fb3fe68fda020c2af-748c3c53a0b086c4-00  Headers=1
AFTER  ParentId=00-707f5a290bbf0a1fb3fe68fda020c2af-748c3c53a0b086c4-00  Headers=1
       header custom-key=custom-value
```

The outgoing table's seven columns are not the whole story: `body` holds the
**serialised envelope**, not the message payload, and the envelope carries its
own metadata. The wire format even has a `ReservedHeaderKeys` set governing
which header keys get promoted back into typed properties on read.

**The spec's stated cause is wrong.** "The outbox table has seven columns and
none of them can hold that context" reasons from a column list to a conclusion
the mechanism contradicts.

---

## Finding 4 (T001/T006) — the chain already works, except at one hop

Observed in the running stack, and this is the finding that reshapes the
feature. Trace `195d91230e630d835afd39ffc1132890`:

```
automation          receive  FabEventIngestedV1            42 ms
  ├─ automation     send     OverlayHighlightRequestedV1    0 ms
  ├─ audit-obs      receive  OverlayHighlightRequestedV1   58 ms   (+0.7 s)
  └─ layout-comp    receive  OverlayHighlightRequestedV1    1 ms   (+4.3 s)
```

**One trace. Three services. Through RabbitMQ and through the outbox** — the
layout-composition receive lands 4.3 seconds after the send, so this is a real
store-and-forward hop, not an in-process shortcut. Parent-child is intact.

Meanwhile the hop this feature was filed about is broken. Message
`08df004c-5fa4-6f2b-c85b-763a4fb00000` is published in trace `1d701bae…` by
`event-ingestion` and received in trace `b8e1234c…` by `automation`. Two
traces, no relationship.

### Why that one hop and not the others

The parent Wolverine propagates is **the ambient activity at publish time**, not
the send span. Look at where the downstream receives attach above: their parent
is `55b373fc021fcd9a`, the automation **receive** span — not either `send` span
beside them.

- In `automation`, publishing happens inside a message handler, so
  `Activity.Current` is the receive activity. Downstream joins.
- In `event-ingestion`, publishing happens from `PersistenceLoopHostedService`
  — a `BackgroundService` draining a channel. **There is no ambient activity at
  all**, so there is no parent to carry, and every publish is its own root.

The `send` spans in event-ingestion confirm it: every one is a trace root.

**Nothing is lost in the outbox. There was never anything to lose.** The
ingestion path is untraced, so the journey has no beginning to point back at.

### What this means for the feature

The requirement stands and is still unmet: from an effect you still cannot find
the plant-floor event that caused it. But:

- **FR-001 is already satisfied** — the causal relationship does survive the
  outbox, demonstrated across two services and a 4.3-second wait.
- **T004/T005's metadata rule is not needed.** There is nothing to stamp;
  Wolverine already carries and restores what exists. A rule would have added a
  header on every message in the system to fix something that was not broken.
- **The fix is to give the ingestion path an activity**, so an ingested event
  has a cause worth propagating.

### And US3 is confirmed empirically, in the direction research predicted

That joined trace reports `durationMs: 4305` — the trace really did span 4.3
seconds of queue wait. Its spans report 42, 0, 58 and 1 ms: each measures its
own work. **Parentage across a delayed hop inflates the trace and not the
spans**, exactly as Finding 2 argued and exactly opposite to the spec's original
claim. SC-003 holds for the route we are taking.

---

## Where that leaves the plan

The plan said "try the cheap route first, and see whether Wolverine's own
tracing joins the journey up". It does — for every hop that has a cause to
propagate. So the feature is smaller again than the plan's cheap outcome:

1. **No metadata rule.** T004, T005 and T007 have nothing to do.
2. **One activity in the ingestion path**, so the journey has a root.
3. **The tests still matter**, and more than before: FR-001 is now a claim about
   behaviour we inherited rather than built, which makes it exactly the kind of
   thing that regresses unnoticed.

**Three wrong premises found by looking at the mechanism instead of arguing from
it** — first the percentile claim, then the seven-column claim, then the outbox
itself. Each was plausible and each survived a checklist.
