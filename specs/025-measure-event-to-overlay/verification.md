# Verification: The event-to-overlay leg can be measured

**Feature**: `025-measure-event-to-overlay` · observed 2026-08-22

**In progress.** T001–T003 are done and they settled the route. This note records
that first, because it is the part that decided what the feature is.

---

## 1. The question (T001) — trace parentage is dropped

Phase 0 suspected it from spec 024's captures. Confirmed on a running stack.

**Every publish from `event-ingestion` is its own root trace**, containing only
`event-ingestion` spans — 23 of 23 sampled, none containing a downstream
`receive`:

```
trace 6a3c5d51…   send FabEventIngestedV1   event-ingestion   ← ROOT
trace da1dec82…   send FabEventIngestedV1   event-ingestion   ← ROOT
```

**And the work it causes is a separate root trace**, with no `parentSpanId`:

```
trace 5a5ace2e…
  receive FabEventIngestedV1          automation            ← ROOT, no parent
  send    OverlayHighlightRequestedV1 automation
  receive                             layout-composition
  receive                             audit-observability
```

So the downstream half of a journey is traced well. It is simply not attached to
what caused it.

### What was not established, and is not claimed

Whether `messaging.conversation_id` survives the hop. It is present on both
sides, but full-text search over traces did not filter usefully and it was not
worth more time once the parentage answer was clear. **Recorded as unresolved
rather than assumed either way.**

## 2. The consequence (T002) — and a conclusion the plan got wrong

The route is the **timestamp**, so Phase 2 stands.

But the plan's framing of *why* was imprecise, and the correction matters:

> **available or linked** → the leg is derivable from spans and T004–T006 are
> unnecessary

**That is wrong, and would have been wrong even if context had propagated.** A
histogram is recorded at a point in time by code that must know the elapsed
duration *then*. Trace correlation would let a human — or a query — join spans
after the fact; it cannot produce a distribution inside the running system.

So the acceptance moment must reach the applying service regardless. T001's
answer determined whether a *second* problem also exists. It does.

**Recorded because the plan asserted otherwise and a reader would inherit it.**
The question was still worth asking — it found #1750 — but its answer was never
going to delete Phase 2.

## 3. The bigger finding (T003) — filed as #1750

Every cross-service "what caused this?" question is currently unanswerable for
anything crossing the outbox, which is every integration event in the system.

Spec 023 spent a day on #1655's twelve-second first event reasoning from
wall-clock timings in test output, because the traces did not join up. It ended
with **no cause identified** and four candidates refuted. A joined trace might
have shown the answer directly.

**It may also be deliberate.** A message leaving the outbox minutes after the
request that queued it should arguably not extend that request's trace; the
recommended pattern is a span *link* rather than a parent. #1750 says so rather
than assuming a defect.

---

## Still to do

T004–T022. The route is settled; the measurement is not built.
