# Quickstart — trying the camera filter

How to exercise the feature by hand, and the two checks worth making that an
automated test does not make for you.

---

## 1. Get cameras with awkward names

The filter's whole point is names whose distinguishing word is **not first**. A
catalogue of `Camera 1`, `Camera 2` will not show whether it works.

Register a handful by hand on the cameras page, or drive run mode's simulator,
and make sure at least these shapes exist:

| Name | What it tests |
|---|---|
| `Line 2 Furnace` | the distinguishing word last — the case that fails today |
| `Furnace 3` | still reachable by the start of the name (C5) |
| `Bay 4 Inlet` | a middle fragment (`4 in`) |
| `50% Load` | the fragment is input, not syntax (C4) |

---

## 2. Type a middle fragment

In the layout editor's camera picker, and again on the cameras list page, type
`furn`.

**Both `Line 2 Furnace` and `Furnace 3` must appear.** Today only the second is
reachable, and only by typing `furn` with the list focused.

---

## 3. Check the total against the rows — this is the one worth doing by hand

With a filter applied, read what the screen says about how many cameras there
are, and count the rows.

**They must agree.** A screen saying "250 cameras" above eleven rows is the defect
this feature exists not to create, and it is invisible unless someone looks at
both numbers at once.

Then clear the filter and confirm the total returns to the catalogue size.

---

## 4. Put the mouse down

Complete the whole task using only the keyboard: reach the chooser, type a
fragment, move through the matches, choose one, and dismiss.

**This is the check most likely to be skipped and most likely to fail**, because
the feature is built with a pointer in hand and looks finished either way. The
native control gives this for free today — the filter must not take it back.

If a screen reader is available, confirm the control announces its role and value,
and that the number of matches is announced when it changes rather than the list
silently shrinking.

---

## 5. Try a fragment that matches nothing

Type something no camera contains.

**The screen must say so plainly**, and it must be distinguishable from a list
still loading. An operator who cannot tell those apart will conclude the camera is
gone and register a duplicate — and duplicate names are refused, so they will then
be stuck.

---

## 6. Try `%`

Type a per-cent sign.

It must match **only** `50% Load`, not everything. If it matches everything, the
fragment is being treated as a pattern rather than as text, which is both a wrong
answer and a trust-boundary failure on input arriving over HTTP.
