# Contract: the corrected record, in all four places

**Feature**: `040-kiosk-latency-legs` · **Plan**: [plan.md](../plan.md)

The false claim reached **four documents**. This is a contract rather than four
task notes because **they must agree afterwards**, and four separate paraphrases
of one correction is precisely how they came to disagree with the code.

Each correction states **what is true**, and **why the error happened**
(**FR-004**) — the mechanism generalises, the correction does not.

---

## The error, stated once

> A search for video in `apps/kiosk-web` found none and concluded the kiosk
> decodes nothing. The kiosk renders `CameraViewer`, a **shared** composite in
> `apps/shared`, which owns the `<video>` element, drives the peer connection,
> and draws the overlay onto the live frame. The search was scoped to one
> directory; the capability lives in another.

---

## 1. `.specify/memory/constitution.md` §IV — the load-bearing one

**This is the fix.** The obligation in §VII is conditional on this table, so
until it is right the two legs carry no obligation at all.

| Leg | Implemented | Measured | Dashboard |
|---|---|---|---|
| Camera → SFU | yes | yes (SFU metrics) | no |
| **SFU → kiosk decode** | **yes** | **in part** — receive-to-decoded only; see below | no |
| Presentation buffer (PTP) | **no** | — | — |
| Event → overlay state | yes | recorded, not yet readable | no |
| **Overlay composite + render** | **yes** | **yes** | no |
| Headroom | n/a — arithmetic remainder | n/a | n/a |

The prose beneath must gain:

- **"in part"**, defined the way "recorded, not yet readable" already is: the
  budget spans SFU-sends → kiosk-decoded, and the browser cannot see the sending
  end without a clock shared with the SFU. Establishing one **is** the unbuilt PTP
  leg. So the recorded figure covers receive-to-decoded and carries no budget.
- The correction and its cause, from *The error, stated once*.
- That the sentence warning about this table — *"a leg left recorded as unbuilt
  after it is built would exempt itself from §VII by clerical error"* — **describes
  something that happened**, and stays.

**The warning sentence is not removed.** It was right. It should now be able to
point at an instance.

---

## 2. `CLAUDE.md` — the latency section

Currently: *"Three of these legs are not built (#1714): the kiosk renders no
video, so decode and composite-and-render have no code path, and PTP is still a
future-add."*

Replace with the true count — **one** unbuilt leg, PTP — and the true state of
the other two. Keep the existing instruction to keep §IV current, and keep the
pointer to the constitution as the authority; this file summarises, it does not
compete.

---

## 3. `specs/024-latency-budget-visible/verification.md` §6 — where it started

The table there records decode as *"Not built"* and composite as *"Half built"*,
on the evidence *"apps/kiosk-web has no `<video>`…"*.

**Corrected in place, not rewritten.** Spec 024's verification note is a record of
what was found at the time, and deleting the error would remove the only trace of
how it propagated. Add a dated correction stating what is true, what the search
missed, and that the claim reached three other documents from here.

That is the same treatment the repo gives a wrong finding elsewhere: correct it,
keep the reasoning visible, do not pretend it was never said.

---

## 4. Issue 1714 — the premise

Its title and table say three legs are unbuilt. Correct it in a comment rather
than editing the body: the body is what someone wrote, and a silently-edited issue
loses the same trace §3 preserves.

The comment states the true count, the two legs' real state, that PTP remains and
stays filed, and what this spec does about the obligation the correction creates.

---

## What must be asserted

| Assert | Because |
|---|---|
| §IV's table says **implemented** for both legs | The whole failure was a document saying something false about the code, and nothing noticing |
| §IV distinguishes **four** states across six legs — watched, in part, recorded-not-readable, unbuilt | Rounding any of them up repeats the failure. **SC-007** |
| The decode row does **not** claim a measured budget | See [the-two-measurements.md](./the-two-measurements.md) |
| All four documents agree | **FR-002**. One corrected and three not is the same defect with a smaller blast radius |
| Each correction says **why** | **FR-004**. The next search scoped to one directory is the thing worth preventing |
