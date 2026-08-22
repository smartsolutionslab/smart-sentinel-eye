# Quickstart: follow a journey end to end

**Feature**: `026-follow-a-journey` · 2026-08-22

This is the walk that decides whether the feature works. It is deliberately
manual in places, because two of its success criteria (SC-001, SC-007) are about
**a person reading the dashboard** rather than a test asserting in memory.

---

## Before

Establish what today looks like, so "it joined up" is a comparison rather than an
impression.

1. Start the stack: `dotnet run --project src/AppHost`.
2. Open the Aspire dashboard, **Traces**.
3. Publish one plant-floor event (the `PlantFloor` fixture helper does this, or
   publish to `fab/munich/+/+` by hand).
4. Find the publishing trace. **Expect: it contains only `event-ingestion`
   spans.** The handling work in `automation` is a *separate* root trace whose
   receive span has no parent.

**Write down both trace IDs.** They are the two ends that are currently
unconnected, and the same two are what you check afterwards.

---

## After

Same walk, same event.

1. Find the trace containing the plant-floor event's arrival.
2. **The handling work should be reachable from it** — as a child, or via a link,
   depending on which route step 2/3 of the plan landed on.
3. Follow it the other way: from an applied effect, get to the originating event
   **without** using timestamps to guess.

**Both directions, or it is half a feature** — US1 is the one people ask, but
US2 falls out of the same relationship and should be checked rather than assumed.

---

## The three things most likely to be wrong

**It emits but does not arrive.** Spec 024's precedent: a source was registered
and nobody could see spans for two days. If the dashboard shows nothing, check
the dev certificate is trusted (`dotnet dev-certs https --trust`) before
concluding the code is wrong.

**It joins up in-process but not through the outbox.** The whole feature is about
the store-and-forward hop. A test that publishes and handles in one process
proves nothing here — the message must have been **written to and read back from
`wolverine_outgoing_envelopes`**. Confirm the path taken, don't infer it from a
green test.

**A duration got longer.** SC-003. Look at the publish span and the handling
span: each should measure its own work, and neither should have grown to include
the wait. If a span now spans the queue time, the feature has misrepresented a
delay as work and has failed regardless of how well the trace reads.

---

## The measurement

SC-006 — steady-state arrival-to-effect no worse than 267–369 ms, measured the
way specs 022 and 024 measured it:

```sh
dotnet test tests/Integration.Tests --filter "Category=Measurement"
```

Compare against `specs/024-latency-budget-visible/verification.md`. **Headers on
every message are not free**; the point is to know the cost, not to assume it is
negligible.

---

## Recording it

The numbers and the two trace IDs go in `verification.md`, not in a commit
message. A screenshot of the joined trace is worth more than a sentence saying it
joined — SC-007 is about someone being able to follow it, and an image is the
only evidence of that which survives the session.
