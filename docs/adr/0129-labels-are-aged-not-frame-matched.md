# ADR-0129: A label is aged to match its picture, not matched to a frame

**Status:** **Accepted**
**Date:** 2026-08-29
**Amends:** ADR-021 (event time reference — the frame-matching clause), ADR-015 and constitution §IV (the SLO's *frame-synced* wording)
**Relates to:** ADR-0122, ADR-0128, ADR-0130, spec 045, spec 046, issue 1967

## Context

Constitution §IV states the end-to-end SLO as:

> event arrival → overlay rendered, **frame-synced** ≤ 800 ms

**Nothing is frame-synced.** The overlay is an absolutely-positioned DOM label
layered over the `<video>`, not composited into the frame. It updates the instant
its value changes, while the picture beneath it is `buffer + processing` old — so
an operator reads a number describing *now* over a frame from slightly before.

Measured on a real two-tile wall (spec 045 verification §3a): frame age
**~25–40 ms**, of which the presentation-buffer leg contributes **10–26 ms**.

### Why now

**Spec 045 made the gap grow rather than shrink.** Aligning a wall works by
*adding buffer*, so the better the tiles agree with each other, the further the
whole wall sits behind the label describing it. On this hardware that is a few
milliseconds; on a fab network needing real buffer it is however much buffer it
needs, with no ceiling short of the leg's 200 ms budget.

So a feature that just shipped set the direction, and the SLO makes a claim the
system does not honour.

### ADR-021 cannot be implemented as written

> *Overlay engine renders at the frame whose presentation timestamp matches
> `used_ts`.*

**Both halves are missing.** ADR-0128 withdrew presentation timestamps — nothing
we own is in the media path, the SFU is an unmodified MediaMTX — and recorded
ADR-021 as *"left unsettled … needs its own amendment when a feature attempts
it."* **This is that amendment.** And `used_ts` never reaches the kiosk: the
realtime message is `{ overlay, resolvedText, version }`, carrying no timestamp
of any kind.

### The probe that closed the alternative

Spec 046 was first scoped to **make the claim true** rather than correct it, on
one candidate route that would have avoided ADR-0128's rejection: per-frame
metadata carrying a capture instant. **It was probed against a real MediaMTX and
a real Chromium, and it is closed** (`specs/046-.../research.md`):

1. **The `abs-capture-time` extension does not survive.** Chromium does not offer
   it, MediaMTX does not offer it, and **forcing it into the offer SDP does not
   work** — the SFU declines to echo it, and per-frame metadata still carries no
   capture instant.
2. **The browser cannot relate its clocks.** `requestVideoFrameCallback` gives an
   RTP timestamp — *which* frame — while every wall-clock field it carries is
   local. The RTCP repair is unavailable: `getStats` exposes the sender report's
   NTP instant with **no accompanying RTP timestamp**.
3. **And this one survives fixing both.** A capture instant would be on the
   *camera's* clock; a value's instant is on a *server's*. Relating them is PTP
   across the OT network — ADR-014's deployment prerequisite, absent here.

**So frame accuracy is blocked by the absence of a shared clock between the thing
that saw the event and the thing that filmed it.** It needs hardware, not
software.

## Decision

**1. The overlay is deliberately not frame-matched, and the SLO stops saying it
is.** §IV's *frame-synced* clause and ADR-015's leg description are corrected.
ADR-021's frame-matching clause is withdrawn.

**2. A label is aged to match its picture.** Each label is held back by **that
tile's own measured frame age**, so the label and the picture beneath it describe
about the same moment. Spec 045 already computes that figure per tile.

**3. This is not frame accuracy and must never be called it** — not in code,
comments, UI, metric names or documents. **It makes a label as old as the
picture; it does not pair a value with the frame its instant belongs to.**
Restating the withdrawn overclaim in a new form is the specific failure this
decision exists to prevent, and it would be easy to do by accident.

**4. The delay is bounded and counted against the 800 ms budget.** A held label
is a later label, and lateness is what the budget bounds.

**5. ADR-0128's rejection of media-path ownership stands** and is not reopened.
If PTP hardware ever exists in a fab, frame accuracy becomes a different feature
with a different ADR — this one does not pre-empt it.

**6. Nobody can perceive this.** ~30 ms is below the threshold at which an eye
distinguishes a label from the frame under it. **The benefit is correctness, not
experience**, and no claim is made that an operator notices. That is why the
record correction (1) is shippable on its own and is the half with the certain
benefit.

## Consequences

**Positive — the SLO stops overclaiming.** A reader of §IV learns what the system
does. That is worth having whether or not the mechanism is built.

**Positive — the gap stops growing silently.** Spec 045 widened it and only a
code reading noticed. Aged labels move *with* the buffer, so adding buffer no
longer widens the mismatch.

**Negative — it costs latency on the data half of the budget.** Labels arrive
later by their tile's frame age. Bounded, counted, and unnoticeable at current
figures — but real, and spent for a benefit nobody can see.

**Negative — for an alarm, later may be wrong.** Holding a safety-relevant value
back to agree with a picture makes an operator see it later. **This decision does
not settle that**; the spec records it as an open question, and a blanket
"everything is aged" may need revisiting if such an overlay appears.

**Negative — the record now says frame accuracy is not done.** Anyone who wanted
it must read why, and the three blockers are recorded so they do not re-run the
probe.

**Neutral — ADR-021's ingestion-time half is untouched.** `used_ts`, `time_basis`
and the `source.ts` fallback stand. Only the frame-matching clause is withdrawn.

## Alternatives Considered

**Make the claim true — REJECTED, after probing it.** The chosen scope until the
probe. Blocked by all three findings above, the third irreducibly.

**Leave the wording and do nothing — REJECTED.** It is the cheapest option and it
leaves §IV promising a synchronisation nothing delivers, which is the defect
ADR-0130 spent a whole feature correcting elsewhere.

**Age-match without correcting the wording — REJECTED.** It would swap one
overclaim for a subtler one: age-matching is not frame accuracy, and calling it
that would be worse than the honest gap.

**Composite the overlay into the video — REJECTED as unnecessary.** A label
layered over the right frame is as aged as one drawn into it, and compositing
carries performance and accessibility consequences this decision does not need.

## Implementation Notes

- Constitution §IV and ADR-015: the *frame-synced* wording.
- ADR-021: the frame-matching clause withdrawn; the time-reference half kept.
- The relationship between a label and its frame recorded where a reader will
  find it, **including the direction playout buffering moves it** — without that,
  the next feature to add buffer will not know it widened the gap, which is how
  this one came to exist.
- Guarded in `tests/Architecture.Tests/`, following `FoundingDecisionRecordTests`'
  consistency-check shape rather than a text pin.
