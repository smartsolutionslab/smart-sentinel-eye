# Research: The event-to-overlay leg can be measured

**Feature**: `025-measure-event-to-overlay` · **Phase 0** · 2026-08-22

Two findings. The first sizes the change the spec anticipated. **The second says
there may be a better change**, and it comes from re-reading evidence spec 024
had already collected without noticing what it showed.

---

## Finding 1 — carrying the moment is cheaper than feared

`EventMetadata` is a positional record shared by every integration event:

```
EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt, string? Fab, Guid? Actor)
```

- **15 construction sites** across 8 contexts.
- **Almost nothing reads it.** The only consumers found are `Metadata.Fab`,
  used by the SystemVariables and LayoutComposition handlers to scope an effect,
  and one read in CameraCatalog.

Adding a **fifth optional parameter with a default** is:

- **Source-compatible** — the 15 existing call sites compile untouched.
- **Wire-compatible both ways** — a message serialised before the change
  deserialises with the property absent, and a consumer that predates the change
  ignores an unknown property.

So FR-011's question — whether this is breaking under ADR-0073's `V<N>` scheme —
has a concrete answer: **it is not**, and no `V2` is required. That is worth
having established rather than assumed, because the assumption could have gone
either way and the wrong one costs a versioned duplicate of two contracts.

**What it does not settle** is whether `EventMetadata` is the right *home*. A
field that is meaningful only inside the automation chain would be present, and
null, on every other event in the system.

---

## Finding 2 — the trace already crosses the leg, except at one hop

Spec 024 verified that spans cross service boundaries and used it to close T006.
Re-reading the traces it captured shows something it did not remark on.

**A complete downstream journey is one trace:**

```
trace 5a5ace2e…
  receive  FabEventIngestedV1          automation            7 ms   ← ROOT, no parent
  send     OverlayHighlightRequestedV1 automation            0 ms
  receive                              layout-composition    0 ms
  receive                              audit-observability  13 ms
```

**And EventIngestion's publish of that same message is a different trace
entirely:**

```
trace 97ed651c…
  send     FabEventIngestedV1          event-ingestion       0 ms   ← ROOT
```

The automation `receive` has **no `parentSpanId`**. It is a root span. So the
causal link from *the event being accepted* to *everything that follows* is
broken at exactly one hop — the one that goes through the outbox.

### Why this matters more than the timestamp

It is the same root cause as the spec's problem, seen from the other side. The
acceptance moment is lost downstream **and** the trace context is lost
downstream, and both are lost at the same place.

If context survived that hop:

- the leg would be measurable from the trace itself, with no contract change;
- every other cross-service question — "what caused this?", "what did this
  cause?" — would become answerable, which it currently is not for anything
  that crosses the outbox;
- spec 023's cold-start investigation, which had to reason from timings because
  the traces did not join up, would have been a five-minute job.

**This is a much larger prize than one leg's latency.**

### What is not established

Whether Wolverine persists W3C trace context with the outbox envelope and fails
to restore it, or does not persist it at all, and whether that is configurable
or a defect. The outbox table is `wolverine_outgoing_envelopes`; nothing in this
repository reads or writes its columns directly, so this is a question about
Wolverine's behaviour, not ours.

**It may also be intentional and correct.** A message that leaves the outbox
minutes after the request that queued it arguably should not extend that
request's trace — an 8-minute span is not useful. Wolverine may be creating a
new trace with a *link* to the original rather than a parent, which is the
recommended pattern for exactly this case and would still make the leg
measurable, just not by looking at parent chains.

**That question must be answered before choosing an approach**, and it is cheap
to answer: send one event and read one trace.

---

## The choice this sets up

| | Carry the moment (Finding 1) | Restore causal context (Finding 2) |
|---|---|---|
| Blast radius | one optional field, 8 contexts unaffected | unknown until the behaviour is understood |
| Measures the leg | yes | yes, and every other cross-service span |
| Contract change | yes, non-breaking | possibly none |
| Risk | low, well understood | unknown — may be a Wolverine setting, may be a defect, may be by design |
| Delivers §VII | yes | yes |

**The plan's position:** answer Finding 2's open question first, because it is one
event and one trace, and because choosing the timestamp without asking it would
be building a private mechanism next to a general one that may already work.

If the answer is "context is available and we are not using it", the timestamp
becomes unnecessary. If it is "genuinely lost", the timestamp is the right change
and Finding 2 becomes an issue to file.

**Either way this feature ships a measurement.** The question decides which
change is smaller, not whether the obligation is discharged.
