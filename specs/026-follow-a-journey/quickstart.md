# Quickstart: follow a journey end to end

**Feature**: `026-follow-a-journey` · 2026-08-22 (rewritten)

The walk that decides whether the feature works. Deliberately manual in places,
because SC-001 and SC-007 are about **a person reading the dashboard**, not a
test asserting in memory.

---

## Before

Recorded on 2026-08-22, and worth repeating because it is the comparison:

1. Start the stack: `dotnet run --project src/AppHost`.
2. Open the Aspire dashboard → **Traces**.
3. Let the scenario simulator run, or publish to `fab/munich/+/+` by hand.

**What you should see, today:**

- Every `event-ingestion` trace is a **root `send`** containing only
  `event-ingestion` spans. No consumer anywhere in it.
- The `automation` work it causes is a **separate root**, titled `receive`.
- Two traces. No relationship. *(Observed: message
  `08df004c-5fa4-6f2b-…` published in `1d701bae…`, received in `b8e1234c…`.)*

**And, importantly, what already works** — the thing the first two versions of
this spec missed:

```
automation          receive  FabEventIngestedV1            42 ms
  ├─ automation     send     OverlayHighlightRequestedV1    0 ms
  ├─ audit-obs      receive  OverlayHighlightRequestedV1   58 ms   (+0.7 s)
  └─ layout-comp    receive  OverlayHighlightRequestedV1    1 ms   (+4.3 s)
```

*(trace `195d91230e630d835afd39ffc1132890`)* One trace, three services, through
RabbitMQ and through the outbox, across a 4.3-second wait. **Look at this one
first.** If you don't, the change below looks far bigger than it is.

---

## After

Same walk, same event.

1. Find the trace containing the plant-floor event's arrival.
2. **The automation work should now be inside it**, not in a trace of its own —
   and so should everything the diagram above already showed.
3. Follow it the other way: from an applied effect, reach the originating event
   **without** using timestamps to guess.

---

## The four things most likely to be wrong

**One cause for the whole batch.** The failure mode this feature is most likely
to ship. Ingestion batches up to 200 deliveries, so a batch-level activity gives
a joined trace that looks right from the effect end — and merges two hundred
unrelated journeys at the event end. **Check two events from the same batch have
different traces**, not just that some trace joined up.

**It emits but does not arrive.** Spec 024's precedent: a source registered, no
spans visible for two days. If the dashboard shows nothing, check
`dotnet dev-certs https --trust` before concluding the code is wrong.

**It joins in-process but not through the outbox.** Confirm the message was
written to and read back from `wolverine_outgoing_envelopes`. A publish-and-
handle in one process proves nothing here.

**A span got longer.** SC-003. Each span should measure its own work; the queue
wait belongs in the **trace's** elapsed time. The before-trace above already
shows the right shape — 4305 ms overall, spans of 42/0/58/1 — so this is a
regression check against a known-good reading, not an open question.

---

## The measurements

Two, and the second is new to this version:

```sh
dotnet test tests/Integration.Tests --filter "Category=Measurement"
```

**Latency (SC-006):** arrival-to-effect against the 267–369 ms recorded by specs
022 and 024. Compare with `specs/024-latency-budget-visible/verification.md`.

**Ingest throughput (FR-009):** the ingest path is sized for 5 000 events/s and
its batching exists to keep the database round trip amortised. An activity per
event runs five thousand times a second at that load. **Know the cost; don't
assume it rounds to nothing.**

---

## Recording it

Both trace IDs, both measurements, and a screenshot of the joined trace go in
`verification.md`. SC-007 is about someone being able to follow the journey, and
an image is the only evidence of that which survives the session.

Record the **batch check** explicitly too — "two events from one batch, two
traces" is one line, and it is the line that says the cheap version was not
shipped.
