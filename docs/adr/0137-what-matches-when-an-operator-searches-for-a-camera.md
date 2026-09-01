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

**That last sentence is contingent, and the contingency is worth naming.** The
fragment is upper-cased by .NET (`ToUpperInvariant`) and the column by Postgres
(`upper`), and the two agree on characters outside ASCII only under a suitable
`LC_CTYPE`. This database is `en_US.utf8`, where they do. Under a `C` ctype
Postgres would leave `ü` alone while .NET would not, and an accented camera would
become unfindable by the very fragment that names it. Nothing in the code or the
deployment pins the locale, so the agreement is asserted by an integration test
against the real column rather than assumed — the handler tests cannot settle it,
because the in-memory fake normalises both sides the .NET way.

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

### The fragment is settled before it is sent

Both boxes debounce at 250 ms, sharing one hook so they cannot feel different.

Not a performance flourish. The picker's query is a **page walk** — up to five
sequential requests of 200 rows to assemble the whole choice list — so keying it
on the keystroke meant about thirty-five round trips to type `furnace`, and a
cache entry per prefix that every camera mutation would then refetch. It is also
an accessibility argument: the status beside the field is a polite live region,
and one tied to a per-keystroke query re-announces on every letter, chattering at
exactly the person it was added for.

The input still reads the raw value, so nothing is dropped while typing; only the
query and the status read the settled one. A fragment typed but not yet sent
reports as *searching*, because reporting the previous fragment's match count
beside a box that no longer contains it is its own small untruth.

### The fragment's length is not validated, and that is a decision

Every other list parameter is rejected when out of range; `name` is not. A
fragment longer than the 200-character column cannot match anything, so it buys a
scan that returns nothing.

Left unguarded because the cost is the measured one — a scan of a fab's rows, a
fraction of a millisecond — and because the fragment reaches Postgres as a
parameter rather than as syntax, so length buys no leverage. Adding a guard means
adding a failure an API consumer must handle, to prevent an outcome that is
already correct and already cheap. Recorded so the omission reads as a decision
rather than as the one parameter nobody thought about.

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
reassigns it — the filter silently editing their layout. Every camera seen is
retained so the tile keeps showing its own camera by name. The same shape on the
list page is the offset outliving the population it was an offset into, and is
reset when the fragment changes.

**The retention itself then shipped broken, and review caught it rather than a
test.** The map was cleared when the dialog closed, and the merge that refilled
it keyed on the identity of the response array. Both dialogs stay mounted, so a
reopen with nothing typed re-read the same cache entry, the merge saw no change,
and the map stayed empty — leaving the retention absent at precisely the moment
it was needed. The merge now asks which cameras are not yet known, which
converges on contents rather than on a reference. That also removed a second
failure the identity check carried: against any response that is not
reference-stable it set state on every render, and React aborts the component
with *too many re-renders* — a dialog that crashes rather than misbehaves.

**What that says about the guards.** The first retention tests handed the
designer a map that already held the camera, so they proved the designer consults
one and nothing about the dialog filling it. The gap was one layer below where
the tests were pointed.

**The database query was measured, not assumed.** At 259 cameras — the whole dev
catalogue, above the 250-per-fab target — the filtered count runs in
**0.230–0.331 ms** over five runs, and the sorted, paged query in
**0.226–0.375 ms**. The unfiltered count is **0.214 ms**, so the substring match
costs a fraction of a millisecond.

**That is the statement, and it is narrower than "the feature is fast".** It
measures one statement against the database, not the path an operator drives:
the picker assembles its choice list by walking up to five pages, so what a
keystroke costs is a question about the client, answered by settling the fragment
before it reaches the query rather than by this figure.

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
