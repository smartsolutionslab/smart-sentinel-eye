# Data model — 048 the camera picker finds every camera

No persisted entity changes. No domain code. Nothing crosses a bounded context.
What follows is the shape the picker consumes and the states it must
distinguish.

---

## The choosable set

What the picker offers is **not** "all cameras". It is:

> cameras in the fabs the operator's permissions cover, excluding retired ones.

Both filters are applied by the source **before** it counts, so the total and
the rows already agree. This matters for FR-007: a count that included cameras
the operator cannot select would be a different lie in the same place.

---

## `CameraChoices` — what the paging endpoint returns

| Field | Meaning |
|---|---|
| `items` | The cameras the picker offers, alphabetical by name |
| `count` | How many exist in the operator's choosable set |
| `complete` | Whether `items` is all of them |

**`complete` is carried, not recomputed by the consumer.** `items.length < count`
happens to be the same test today, but the producer is the one that knows *why*
it stopped — the bound was hit, or a page came back short. A consumer deriving
it re-implements a decision it cannot see, and would silently disagree the day
the producer gains another reason to stop.

**`count` is the source's total, passed through untouched.** It is not
`items.length`, and the distinction is the entire feature: the gap between them
is what the operator is being told about.

---

## States the picker must distinguish (FR-003)

Four, and today three of them render identically as an empty dropdown:

| State | What the operator sees today | What they must see |
|---|---|---|
| Loading | "Loading cameras…" | unchanged — this one is already right |
| Fab genuinely has no cameras | empty list | told the fab has none |
| Retrieval failed | empty list | told the list could not be retrieved |
| Complete list | the cameras | unchanged, **and no notice** |
| Truncated list | the first 50, silently | the cameras **and** how many exist |

The middle two are the ones that mislead: an operator who cannot tell "no
cameras" from "the request failed" will go looking for the wrong problem. This
is the same class of defect as the truncation itself — a state rendered as a
different, more innocent state.

---

## Ordering

Alphabetical by name, ascending.

Not cosmetic (research R2): it is what makes the native `<select>`'s built-in
prefix type-ahead usable, which is the mitigation that lets search be deferred.
On a list ordered by registration date an operator cannot predict where anything
is, so type-ahead helps only by luck.

---

## Invariants

1. **`count` ≥ `items.length`.** If the source ever returns a count below the
   rows delivered, something is wrong upstream and the picker must not paper
   over it by showing a negative "N not shown".
2. **No camera appears twice.** Paging by offset over a list being edited can
   deliver a boundary camera twice (research R4); de-duplication by identifier
   is what keeps this true.
3. **`complete` false ⇒ the notice is shown. `complete` true ⇒ it is not.**
   A notice that is always present carries no information, which is why its
   absence is tested rather than assumed.
