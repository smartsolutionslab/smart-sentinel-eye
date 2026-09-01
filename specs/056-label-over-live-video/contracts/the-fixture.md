# Contract — what the fixture and the measurement guarantee

Phase 1. Eight contracts. Each is stated so that a change breaking it is a
visible change rather than a quiet one.

---

## C1 — A tile is evidence only when both halves are present

A check that reports the product working MUST have observed, **on the same
tile**:

- video **decoding on an ongoing basis** (C2), **and**
- the overlay's **resolved text** displayed.

**Neither half alone is evidence.** This is the whole defect: today a tile that
draws its label *only when the video fails* passes the entire suite. A check
satisfying one half reproduces the hole it was written to close.

---

## C2 — "Ongoing" means a delta, and the numbers are fixed here

Decoding is evidenced by **two samples 1000 ms apart** with a delta of **≥ 10
frames**.

A single non-zero `framesDecoded` MUST NOT satisfy this. It is true of a source
that emitted one frame and stopped — a frozen wall, which looks identical to a
working one in any screenshot.

Sampling MUST reuse `decodeSampleFrom` / `decodeElapsedBetween`. A second
reader of the same statistics is forbidden.

---

## C3 — Both failure directions are demonstrated, not asserted

The suite MUST contain a check that **fails when video is removed** and a check
that **fails when the label is removed**, and both MUST be **observed to fail**
by running them.

"It would fail" is not evidence. A guard that passes with and without the
mechanism it guards proves nothing, and the only way to know which one you have
is to break it on purpose.

---

## C4 — A failure names the half that broke

Every assertion MUST report which half it was inspecting and what it found
instead — frames decoded, or the text present.

A message reading *"the wall was not ready"* is one nobody can act on. The
message is the deliverable when a check fails unattended.

---

## C5 — The span is one subtraction on one clock, or it is refused

`elapsedMilliseconds` MUST be `observedAt - submittedAt`, both stamped **by the
test process on one machine**.

If that cannot be established, the run MUST report the span **unmeasured**,
name what it could not establish, and report **no figure**.

The browser's clock MUST NOT be mixed with the test process's. This is the
shape spec 053 found safe; the shape it found *not established* — a host stamp
minus a container stamp — MUST NOT be introduced here.

---

## C6 — No figure describing the span is produced by addition

Per-leg figures MUST NOT be summed to produce an end-to-end number, under any
circumstances, including when the span is refused under C5.

ADR-0135 established that medians do not add. A summed figure would close the
question while leaving the risk exactly where it is, and it would be
indistinguishable from a measurement to anyone reading it later.

---

## C7 — A figure travels with the legs it does not cover

Any reported span MUST carry `legsNotCovered`: **camera → SFU**, **SFU →
decode**, **presentation buffer**.

A figure under 800 ms does **not** establish the budget holds — three legs are
absent from it. The list is structural rather than prose so the number and its
scope cannot travel apart.

---

## C8 — The record separates observed from measured, and admits what is neither

The record MUST state, per leg, what is now known, and MUST NOT claim that this
feature discharges:

- **inter-display synchronisation** — still unbuilt, not one of the six legs;
- **representative-hardware figures** — a CI runner is not a fab kiosk;
- **the dashboard obligation** — a live disagreement with its own issue;
- **that a person has watched a wall align** — nothing automated can.

The record MUST also state that the measured span **includes the label hold**,
since every prior end-to-end run had a null frame age and never engaged it.

---

## What these contracts do not cover

- Whether the figure is *good*. A breach is a finding, not a failure of this
  feature.
- Whether the fixture's clip resembles a fab camera. It is H.264 over RTSP,
  pulled by the SFU exactly as a camera's stream is; the picture's content is
  irrelevant to every assertion here.
