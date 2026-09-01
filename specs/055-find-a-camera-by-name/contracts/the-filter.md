# Contract — the filter

What this feature promises about finding a camera by name. The clauses are the
ones that stop a filter being worse than none.

---

## C1 — The total describes the items beside it

A response carrying filtered items carries a total counting **those matches**, at
every offset, on every page.

**Satisfied when** the count and the page come from one query, filtered before
counting. **Violated by** a total describing the fab while the items describe the
matches — which reads as authoritative and tells an operator there are 250 when
eleven matched.

This is the clause the whole feature can fail on quietly, because a wrong total
looks exactly like a right one.

---

## C2 — Absent means unchanged

No fragment, or a fragment empty after trimming, returns exactly what the list
returns today: same items, same total, same paging, same fab scoping, same
retired handling.

**Satisfied when** existing consumers need no change and observe no difference.
**Violated by** an empty fragment matching nothing, which turns a cleared search
box into an empty catalogue.

---

## C3 — The match rule is written where a miss is read

Case-insensitive, substring, trimmed, **accents not folded** — stated in the
record, not only in the code that implements it.

**Satisfied when** an operator seeing no matches can find out why without reading
source. **Violated by** a rule that is correct and undocumented: a miss then
cannot be told from an absent camera, and the operator's next move is to register
a duplicate.

---

## C4 — The fragment is input, not syntax

Characters meaningful to the underlying match are matched **literally**. A camera
named `50% Load` is found by typing `%`; a fragment of `%` matches only names
containing a per-cent sign.

**Satisfied when** the fragment is parameterised and escaped at the boundary.
**Violated by** interpolation — which is both a wrong result and a trust-boundary
failure on operator input arriving over HTTP.

---

## C5 — Nothing an operator can do today stops working

A camera reachable now by typing the start of its name stays reachable that way.
The chooser keeps its role and value announcements, arrow-key movement, and
Escape.

**Satisfied when** the whole task completes with no pointer. **Violated by**
replacing a native control with one that looks equivalent and is not — the failure
mode being invisible to anyone testing with a mouse.

---

## C6 — A miss is legible

"No camera matched" is distinguishable, on screen, from "still loading" and from
"there are no cameras at all".

**Satisfied when** the three states render differently. **Violated by** an empty
list for all three, which is the state an operator most needs to tell apart and
the one a spinner-then-nothing produces by default.

---

## C7 — One rule

There is a single implementation of "matches". Both screens ask the same
component the same question.

**Violated by** a client-side filter added for responsiveness — which agrees with
the server on the day it is written, and cannot be told apart by an operator when
it stops agreeing.

---

## C8 — The speed is measured, not assumed

The filtered query is timed at the fab-scale target and the figure recorded,
whichever way it falls. No index is added without that measurement asking for one.

**Satisfied when** the record carries a number. **Violated equally by** optimising
first and by asserting "fast enough" without measuring — the second being the
easier mistake, since 250 rows *sounds* small.

---

## C9 — What no clause here can promise

**That the operator finds the camera they meant.** Their remembered name may not
be the catalogue's. What the feature owes is C6: that a miss is unmistakably a
miss, so the next question is "what is it actually called" rather than "is it
gone".
