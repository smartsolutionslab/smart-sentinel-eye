# Research — 055 find a camera by name

Phase 0. Probed against the source and the running dev database, not reasoned
from documentation.

---

## 0. Locked decisions: checked, and there is no conflict

| ADR | Why it was a candidate | Finding |
|---|---|---|
| 0070 Minimal APIs only | the list endpoint gains a parameter | No conflict — a query parameter on an existing minimal API. |
| 0077 Radix headless + custom design system | this may need a new primitive | No conflict, and see §4: the cheaper design needs no new primitive at all. |
| 0043 / 0113 concurrency | none — this is a read | No conflict. |
| 0091 / 0094 naming | a new query field | No conflict; the field is named for what it is. |

**No amendment gate is triggered.**

---

## 1. The count discipline this feature needs already exists

**Decision**: put the name filter where the fab and retired filters already go —
on `visible`, **before** `CountAsync`.

**Evidence**: `ListCamerasQueryHandler` filters twice before counting, and says
why both times:

> *"only cameras in fabs the caller holds, and filtered before the count so the
> total reflects what they can actually page through"*

> *"filtered here rather than at the endpoint so the count and the page agree: a
> total that included retired cameras while the rows excluded them would page past
> the end of the list"*

**So US2 costs almost nothing if the filter goes in the right place**, and the
right place is already marked out and reasoned about. The risk is not building it
wrong — it is building it somewhere else: at the endpoint, or after `Skip`/`Take`,
or in the client over a page it already holds.

**That risk is the whole of US2**, and it is why the story is P1 rather than a
detail of US1.

---

## 2. Case-insensitivity is already computed, and matching should reuse it

**Decision**: match against the existing `name_normalized` column.

**Evidence**, from the running database:

```
 name             | character varying(200)  | not null
 name_normalized  | character varying(200)  | generated always as (upper(name::text)) stored
Indexes:
    "ux_cameras_fab_name_normalized_active" UNIQUE, btree (fab, name_normalized)
        WHERE status::text <> 'Decommissioned'::text
```

`name_normalized` is a **generated stored column** already used by the uniqueness
constraint.

**Rationale, and it is not merely convenience**: reusing it makes *"matches"* and
*"is the same name"* agree by construction. A search that folded differences the
uniqueness rule keeps would show two distinct cameras as one match; a search that
kept differences uniqueness folds would hide a camera an operator knows exists.
One normalisation, one meaning.

---

## 3. Accents do not fold, and that follows from §2

**Decision**: accents are **not** folded. `Fürnace` is not matched by `furn`.

**Evidence**: `upper()` under the database's default collation maps `ü` to `Ü`,
not to `U`. So the existing normalisation is case-folding only.

**Rationale**: this is the answer FR-004 demands be *recorded* rather than left
implied, and it falls out of §2 rather than being chosen independently. Folding
accents in search while uniqueness does not would let two cameras that the
catalogue considers different appear as the same match — which is worse than a
miss, because the operator would pick one believing it was the other.

**Alternative considered**: an unaccent-based match. Rejected here, not
permanently: it needs an extension and a second normalisation, and it would put
search and uniqueness into disagreement. If fab naming turns out to use accents
meaningfully, that is a change to *both*, deliberately, not to search alone.

---

## 4. The picker is a native `<select>`, and that is worth keeping

**Decision**: **add a filter field beside the existing native list rather than
replacing it with a combobox.**

**Evidence**: `GridDesigner` renders a plain `<select>` with `<option>` children.
`@radix-ui/react-select` is a declared dependency and is **used nowhere** in
`apps/` — only `package.json` mentions it.

**Rationale**: the native control supplies, for free and correctly, everything US3
requires — role, value announcement, arrow-key movement, Escape, and the
start-of-name type-ahead FR-012 must preserve. A combobox is a WAI-ARIA pattern
that must re-implement all of it, and Radix ships none to build on, so it would be
written here from scratch.

A filter field that narrows the options **keeps every one of those properties**
and adds the missing one. It is two controls where a combobox is one — a real
cost, and the reason the plan must say how they are associated and how the match
count is announced.

**Alternatives considered**:

- *Build a combobox primitive.* The obvious reading of the issue, and the most
  expensive: a new primitive, its own accessibility contract, and a live risk of
  losing behaviour an operator has today. Not ruled out forever — a combobox is
  the better control once there is a second need for one. There is not.
- *Adopt a third-party combobox.* Contradicts ADR-0077's headless-plus-own-design
  posture and adds a dependency for one screen.

---

## 5. One match rule, on the server — not two

**Decision**: filtering happens **server-side only**, and both screens use it.

**The temptation**: the picker already fetches every camera through the shared
client, so it could filter in memory with no server change and instant response.
The cameras list page pages fifty at a time and cannot.

**Rejected** because it produces **two implementations of "matches"** — one in
TypeScript over a loaded array, one in SQL — and an operator cannot tell which one
they hit. The two would agree on the day they were written and drift on the first
change to either. This repository has spent real time this week on figures that
differed because two things that should have been one were two.

**Consequence, stated because it is a cost**: the picker gains a round trip per
filter change where in-memory filtering would have none. §6 is about whether that
matters.

---

## 6. The index question, and what "measured" means here

**Decision**: no new index until a measurement asks for one.

**Evidence**: the btree on `(fab, name_normalized)` supports a prefix match and
**cannot** serve `%fragment%`. A substring filter scans the fab's rows.

**Rationale**: the fab-scale target is 250 cameras. A sequential scan of 250 short
rows is not a performance problem, and adding a trigram index would mean an
extension, a migration and a second thing to keep true.

**What FR-014 requires**: measure the filtered query at the target scale and
record the figure, whichever way it comes out. If it is plainly fast, the record
says so and the feature stops. **The measurement is the deliverable, not the
optimisation.**

---

## 7. What no check here can establish

**That an operator finds the camera they meant.** The tests can show that a
fragment returns the cameras containing it. Whether the operator's mental name
matches the catalogue's is outside this — and is precisely why FR-009 requires
"nothing matched" to be plainly distinguishable from "still loading" and from
"there are no cameras". The feature's honesty about a miss matters more than its
cleverness about a hit.
