# Contract: what the archive confirmations say now

**Feature**: `037-recover-archived-revision` · **Plan**: [plan.md](../plan.md)

Spec 036 shipped four archive confirmations. Two of them say something this
feature makes false. This document gives their replacements **verbatim**, because
spec 036 recorded what happens otherwise: four confirmations written in one
sitting converge on one sentence that is true of everything and useful for
nothing.

**Two of the four change. The other two are untouched.**

| Surface | Changes? | Why |
|---|---|---|
| Rules | **No** | A rule is still terminal; replacement still means cloning |
| System variables | **No** | Still terminal; the value is still cleared for good |
| **Layouts** | **Yes** | Says the layout can never be edited or published again |
| **Overlays** | **Yes** | Same sentence, same reason |

---

## What is being removed, and what must not replace it

**Removed**, from both:

> **this layout can never be edited or published again**

Spec 036's FR-007 forbade softening that sentence, and its T014 asserts it
verbatim in both page test files. It is being removed because it stops being
true — not because it is inconvenient.

**Must not replace it**: *"This cannot be undone."*

That is the sentence spec 036 built T018 to prevent, and it is now false in the
other direction as well. Archiving **can** be undone: the operator edits the
layout again. A confirmation that overstates is one operators learn to click
through, which costs more than the warning buys.

**What survives unchanged**: the subject's name and revision number, and the
kiosk sentence, conditional on the revision being Published (spec 036 FR-008).
Archiving still takes the wall out of service and still sends kiosks away
immediately. That is still worth confirming, and it is now the *whole* reason the
confirmation exists.

---

## Layouts — the replacement, verbatim

Title (unchanged):

> Archive revision {n} of {name}?

Body, first paragraph — **replaces** the removed sentence:

> This takes the layout out of service. You can bring it back later by editing
> it, and the tiles are kept.

Body, second paragraph — **unchanged**, rendered only when the revision being
archived is `Published`:

> Kiosks showing this layout will be sent away from it immediately.

Action: `Archive`, danger-styled. Cancel keeps focus. All unchanged.

### Why this wording

- **"takes the layout out of service"** is the consequence that is still real and
  still immediate. It is the reason to ask.
- **"You can bring it back later by editing it"** names the recovery in the words
  of the button the operator will actually press — *Edit (new draft)* — rather
  than as a concept. An operator told "this is recoverable" still does not know
  how.
- **"and the tiles are kept"** is the part they cannot see and would otherwise
  assume the worst about. It is also precisely what this feature added, so it is
  the claim most worth asserting.
- It does not say *permanent*, *irreversible* or *cannot be undone*, all of which
  are now false.
- It does not promise the layout keeps serving kiosks, because it does not.

---

## Overlays — the replacement, verbatim

Title (unchanged):

> Archive revision {n} of {name}?

Body, first paragraph:

> This takes the overlay out of service. You can bring it back later by editing
> it, and the label is kept.

Body, second paragraph — **unchanged**, rendered only when the revision being
archived is `Published`:

> Kiosks using this overlay will stop showing it.

The two differ in exactly two words — *tiles* / *label* — because the payloads
differ and the recovered thing is the payload. They keep their existing
difference in the kiosk sentence, which reflects a real difference in what
happens: a layout's kiosks are navigated away, an overlay's simply stop drawing
it.

---

## What to assert, and what asserting badly looks like

| Assert | Because |
|---|---|
| The confirmation contains **"tiles are kept"** (layout) / **"label is kept"** (overlay) | This is the claim the feature earned. Asserting only that a confirmation appeared passes against any wording at all. |
| The confirmation says the layout can be **brought back** | The removed sentence's replacement, asserted as specifically as spec 036 asserted the original |
| The confirmation does **not** contain "never be edited or published again" | The claim that became false. A test that stops asserting the old sentence but never asserts its absence passes against a page that still says it. |
| The confirmation does **not** contain "cannot be undone" | The soft landing. It is false now, and it is where this wording drifts under any future edit. |
| The kiosk sentence still appears for a **Published** revision and still does **not** for a **Draft** | Spec 036 FR-008, asserted in both directions in one test. Asserting only the published case passes against a confirmation that always warns. |

The two replaced tests are `LayoutsPage.test.tsx` *Says the layout can never be
edited or published again* and `OverlaysPage.test.tsx` *Says the overlay can never
be edited or published again*. They are **rewritten**, not deleted: the same test
now asserts the new claim and the absence of both false ones. Deleting them would
remove the only check on this wording at the exact moment the wording changed —
the mistake spec 036's T017 exists to name.
