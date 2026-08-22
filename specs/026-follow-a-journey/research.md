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
