# ADR-0137: What matches when an operator searches for a camera

**Status:** Accepted
**Date:** 2026-09-01
**Supersedes:** (none)
**Superseded by:** (none)

## Context

Spec 048 made every camera *reachable* — the picker no longer stops at fifty and
reports what it cannot show. It did not make any camera **findable**.

The fallback until now was the browser's own type-ahead on a native list, which
matches the **start** of a name. That serves `Furnace 3` and fails `Line 2
Furnace`, and fab naming conventions routinely put the distinguishing word last:
`Bay 4 Inlet`, `Hall A Coiler`. So the operator who knows exactly which camera
they want was the one served worst.

"Search by name" reads as obvious and is not. Whether a fragment matches in the
middle, whether case matters, what happens to accents, and **what the reported
total then counts** are all decisions, and an operator who sees no matches cannot
tell an unmatched rule from a camera that is not there.

## Decision

### What matches

A camera matches a fragment when its name **contains** that fragment, comparing
**the normalised form of both**.

| Question | Answer |
|---|---|
| Position | anywhere in the name, not only the start |
| Case | ignored |
| Surrounding whitespace | ignored |
| **Accents** | **not folded** — `Fürnace` is not matched by `furn` |
| Pattern characters | matched literally: `%` finds a per-cent sign |
| Empty or blank fragment | the same as no fragment: the whole catalogue |

### Why those are one decision rather than six

**Matching reuses the normalisation the uniqueness constraint already uses.**
`cameras.name_normalized` is a stored generated column, `upper(name)`, and
`ux_cameras_fab_name_normalized_active` is built on it.

That makes *"matches"* and *"is the same name"* agree by construction. The
alternative is worse in both directions:

- A search that **folded** what uniqueness keeps would show two cameras the
  catalogue considers different as one match. An operator would pick one
  believing it was the other.
- A search that **kept** what uniqueness folds would hide a camera an operator
  knows exists, under a name the catalogue would refuse to reissue.

Case-insensitivity follows. Accents not folding follows too — `upper` maps `ü` to
`Ü`, not to `U`. **That is the answer to the question the spec required be
recorded**, and it is recorded here because an operator staring at an empty
result needs to be able to find out why.

### The reported total counts the matches

A filtered response's `count` is **the number of matches**, at every offset and
on every page.

This is not a refinement. The list contract's total is what every consumer uses
to decide whether it holds everything, so a filter returning matches beside the
catalogue's total tells an operator there are 250 when eleven matched — and tells
a caller comparing what it holds that it is missing 239 that do not exist. That is
the same defect already filed against consumers rendering one page as the whole
list; this feature would have been **creating** an instance rather than finding
one.

It is achieved by filtering where the fab and retired filters already are:
before the count, on the query the page is drawn from. The handler's existing
comments say why, and the name filter joins them rather than arguing separately.

### One rule, on the server

Both screens ask the server. The picker could have filtered in memory — it
already holds every camera and would have felt instant — and that was rejected:
it is a second implementation of "matches", in a second language, that agrees on
the day it is written and that an operator cannot tell apart when it stops
agreeing.

### A filter field, not a combobox

The picker is a native `<select>`. It supplies role and value announcement,
arrow-key movement, Escape and the start-of-name type-ahead — correctly, and for
free. A combobox is a WAI-ARIA pattern that would re-implement all of it, from
scratch, with Radix shipping none to build on.

**A field beside the list keeps every one of those properties and adds the
missing one.** The cost is two controls where a combobox is one.

## Consequences

**What this establishes.** An operator can find a camera by any distinctive part
of its name. A filtered list reports how many matched. Both screens agree because
there is one rule. Nothing an operator could do by keyboard before has been taken
away.

**A hazard the specification did not anticipate, and what it cost.** Options come
from the server's matches, so a fragment excluding a camera a tile has *already
been assigned* leaves a `<select>` whose value has no matching option: blank on
screen while the form still carries the value. An operator reads that as lost and
reassigns it — the filter silently editing their layout. Every camera seen since
the dialog opened is retained so the tile keeps showing its own camera by name.
The same shape on the list page is the offset outliving the population it was an
offset into, and is reset when the fragment changes.

**Speed was measured, not assumed.** At 259 cameras — above the 250-per-fab
target — the filtered count runs in **0.230–0.331 ms** over five runs, and the
sorted, paged query in **0.226–0.375 ms**. The unfiltered count is **0.214 ms**,
so the substring match costs a fraction of a millisecond.

**No index was added, and that is the finding.** The btree on
`(fab, name_normalized)` serves a prefix and cannot serve a substring, so the
filter scans the fab's rows. At this scale a sequential scan of 259 short rows is
not a problem, and a trigram index would mean an extension, a migration and a
second thing to keep true. **The measurement is the deliverable; the optimisation
is not needed.**

**What this does not establish:**

- **That an operator finds the camera they meant.** Their remembered name may not
  be the catalogue's. What the feature owes instead is that a miss is
  unmistakably a miss — distinguishable from a list still loading and from an
  empty catalogue — so their next question is "what is it actually called" rather
  than "is it gone".
- **That the filter is pleasant by keyboard**, only that the task completes
  without a pointer.
- **Anything at a scale beyond the fab target.** The figures above are for 259
  cameras. A substring scan grows linearly, and the decision to add no index is
  scoped to the target the constitution states.

## Alternatives Considered

**A combobox primitive.** The obvious reading of the request, and the most
expensive: a new primitive, its own accessibility contract, and a live risk of
losing behaviour an operator has today. Not ruled out forever — a combobox is the
better control once there is a second need for one. There is not.

**Client-side filtering in the picker.** Simpler and instant, and rejected for
producing two match rules. See above.

**Folding accents.** Defensible on its own, and rejected because it would put
search and uniqueness into disagreement. If fab naming turns out to use accents
meaningfully, that is a change to **both**, deliberately, not to search alone.

**A trigram index up front.** Rejected on measurement rather than on principle:
the scan costs a fraction of a millisecond at the target scale.

**A second total carrying the unfiltered size**, so a screen could say "11 of
250". Rejected: no screen needs it, and a response with two totals invites a
consumer to compare its items against the wrong one — the failure this feature
exists not to create.

## Implementation Notes

No migration and no new package. The generated `name_normalized` column and its
index already existed for uniqueness; this feature only reads them.
