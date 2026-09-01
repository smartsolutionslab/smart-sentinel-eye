# Tasks — 055 find a camera by name

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/the-filter.md](./contracts/the-filter.md) · **By hand**: [quickstart.md](./quickstart.md)

Fourteen tasks. **No migration, no index, no new package** — the column matching
needs already exists and the picker already has the accessibility.

---

## Do not

- **Do not filter anywhere the count cannot see it** — not at the endpoint, not
  after `Skip`/`Take`, not in the browser over a page it already holds. Each gives
  a total describing a different population than the rows beside it.
- **Do not build a combobox.** The picker is a native `<select>` and already has
  the keyboard and screen-reader behaviour. A filter field goes beside it.
- **Do not add a second match rule** for the picker's responsiveness. One
  implementation, on the server.
- **Do not interpolate the fragment into the query.** It is operator input over
  HTTP; `%` must match a per-cent sign, not everything.
- **Do not add an index or an extension** before the measurement asks for one.
- **Do not fold accents**, and do not leave that unwritten.
- **Do not widen fab scoping or retired handling.**
- **Do not turn this into general search**, or into a performance feature.
- **Do not write bare `#NNNN` issue numbers** in committed docs.

---

## Phase 1 — The query *(the gate)*

- [x] T001 Add an optional `NameFragment` to `ListCamerasQuery` in `src/CameraCatalog/Application/Queries/ListCamerasQuery.cs`, per data-model §2. Absent and empty-after-trim are the same thing.
- [x] T002 Apply the filter in `src/CameraCatalog/Application/Queries/Handlers/ListCamerasQueryHandler.cs` **on `visible`, before `CountAsync`** — beside the fab and retired filters, which already carry the reason in their comments. Match case-insensitively against `name_normalized`, escaped so the fragment is text rather than pattern.
- [x] T003 [P] **Test that the total counts the matches**, in `tests/CameraCatalog.Application.Tests/`: filter a known subset and assert the reported total equals the match count, not the catalogue size. Contract C1, and the clause this feature can fail quietly.
- [x] T004 [P] **Test that filtering and paging compose**, in `tests/CameraCatalog.Application.Tests/`: a match set spanning more than one page, every page drawn from the matches, total unchanged across pages.
- [x] T005 [P] **Test that absent means unchanged**, in `tests/CameraCatalog.Application.Tests/`: no fragment, and a whitespace-only fragment, both return exactly what the list returns today. Contract C2 — an empty search box must not empty the catalogue.
- [x] T006 [P] **Test the match rule itself**, in `tests/CameraCatalog.Application.Tests/`: a middle fragment matches; case is ignored; surrounding whitespace is ignored; **an accented name is not matched by its unaccented fragment**; `%` matches only a name containing one. Contracts C3 and C4.

**Checkpoint — this is the gate.** T003 is the one that matters: a filtered total
describing the unfiltered catalogue reads as authoritative and is wrong. Settle it
here, where the handler already counts the query it pages, or it is not settled at
all.

---

## Phase 2 — The contract

- [ ] T007 Accept `?name=` on the list endpoint in `src/CameraCatalog/Api/CameraEndpoints.cs`, optional, passed through unchanged. No new failure code — a fragment matching nothing is an empty page, not an error.
- [ ] T008 Thread the fragment through the shared client in `apps/shared/src/api/cameras.api.ts`, leaving every existing caller's behaviour identical when it is absent.
- [ ] T009 [P] **Test the endpoint end to end** in `tests/Integration.Tests/`: a fragment returns matching cameras with a total counting them, through the real HTTP contract rather than the handler alone.

---

## Phase 3 — US1 + US2: the screens *(P1)*

- [ ] T010 [US1] Add a filter field beside the camera `<select>` in `apps/management-web/src/features/layouts/GridDesigner.tsx`. **The native list stays** — it carries the keyboard and screen-reader behaviour. Associate the field with the list it filters, and discard responses older than one already shown (FR-013).
- [ ] T011 [US1] Add the same filter to `apps/management-web/src/features/cameras/CamerasPage.tsx`, reusing whatever T010 produces rather than writing a second one.
- [ ] T012 [US2] **Show a miss as a miss** in `apps/management-web/src/features/layouts/GridDesigner.tsx` and `apps/management-web/src/features/cameras/CamerasPage.tsx`: "nothing matched" distinguishable from "still loading" and from "there are no cameras". Contract C6 — the state an operator most needs to tell apart, and the one a spinner-then-empty-list produces by default.

---

## Phase 4 — US3 + the record *(P2)*

- [ ] T013 [US3] **Test the keyboard path**, in `apps/management-web/src/features/layouts/` and `e2e/`: reach the chooser, filter, move, choose, dismiss — with no pointer — and assert the match count is announced when it changes. The check most likely to be skipped, because the feature looks finished either way.
- [ ] T014 Write the record in `docs/adr/` and `specs/055-find-a-camera-by-name/verification.md`: **the match rule where an operator will find it** (substring, case-insensitive, trimmed, accents not folded, and why that follows from the uniqueness normalisation), and **the measured time** of the filtered query at 250 cameras in one fab — whichever way it falls. If it is plainly fast, say so and add no index.

---

## Mutations that must each kill a test

| # | Mutation | Must be killed by | Verified |
|---|---|---|---|
| 1 | Count the population **before** the name filter narrows it | T003 | **killed** — 3 failures |
| 2 | Filter after `Skip`/`Take` | T004 | |
| 3 | Stop trimming the fragment | T005, T006 | **killed** — 3 failures |
| 4 | Match on prefix instead of substring | T006 | |
| 5 | Drop the case folding | T006 | **killed** — 9 failures |
| 6 | Interpolate the fragment, so `%` matches everything | T006 | |
| 7 | Fold accents | T006 | |
| 8 | Render a miss as an empty list with no message | T012 | |

**Mutation 1 first.** It is the only one whose survival produces a *plausible*
wrong answer — a filtered list with a confident, authoritative, wrong total —
rather than an obviously broken one.

**Two corrections found by running these rather than reasoning about them**, and
both are the same lesson:

- The first attempt at mutation 1 moved the count above `SortBy` instead of above
  the filter. **It survived, and it should have** — sorting does not change the
  row set, so the two counts are equal. A mutation that is not the mutation you
  named proves nothing, and this one would have been recorded as "killed".
- Mutation 3 was originally written as *"treat an empty fragment as match
  nothing"*. **That is not expressible against this implementation**: a blank
  fragment trims to the empty string, and `Contains("")` is true of every name, so
  the code returns everything however the null check is spelled. The mutation that
  does exist is dropping the trim — which makes a blank fragment match only names
  containing whitespace, and is killed.

---

## Dependencies

```
T001 ─▶ T002 ─▶ T003, T004, T005, T006     Phase 1 (GATE)
                      │
                      ▼
             T007 ─▶ T008 ─▶ T009           Phase 2
                      │
                      ▼
             T010 ─▶ T011                   Phase 3 (US1)
                └──▶ T012                   Phase 3 (US2)
                      │
                      ▼
             T013 ─▶ T014                   Phase 4 (US3)
```

**Phase 1 gates everything**: US2 is settled in the handler or nowhere, and every
screen above it inherits whatever the total means.

---

## Parallel opportunities

- **T003–T006 are parallel** — four separate claims about one handler, different
  test methods.
- **T010 and T012 are parallel** once the filter field exists; T011 depends on
  T010 only because it reuses it.
- **T001 → T002 are strictly sequential**: a field, then the query that reads it.

---

## Implementation strategy

**Phase 1 first, and it is a gate.** The handler already filters twice before
counting and comments why both times. Joining those two is the whole of US2; doing
it anywhere else is the defect this feature must not create.

**The feature is smaller than the issue implies**, and the tasks reflect that: no
migration, no index, no new package, and no combobox. Matching reuses the
generated `name_normalized` column the uniqueness constraint already uses, which
settles case-insensitivity and accent handling together rather than as separate
choices.

**Coverage gates apply and may be cited.** CameraCatalog's Application layer is
touched, so ADR-0065's **≥80% Application** threshold is live. Stated because two
recent specs got this wrong in opposite directions — one claiming a gate that did
not apply, one missing one that did.

**The feature's issue must be added to Project #13 by hand.** `/speckit-tasks`
adds nothing to the board.

```sh
gh project item-add 13 --owner smartsolutionslab --url <issue-url>
```

---

## Three things most likely to go wrong

1. **The filter lands where the count cannot see it.** The endpoint and the client
   are both tempting and both wrong. The handler's existing comments say so
   already; the risk is not reading them.

2. **A combobox gets built anyway.** It is what "search" implies and what the
   issue's title suggests. It replaces a working accessibility contract with one
   written from scratch. If it turns out to be needed, that is a finding to
   record — not a default to drift into.

3. **A second match rule appears in the browser**, added for responsiveness. It
   will agree with the server on the day it is written.

---

## What the automated checks do and do not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| A middle fragment finds the camera | T006 | the feature having a search box |
| The total describes the matches | T003, at the handler; T009, through HTTP | the number looking plausible |
| Filtering and paging compose | T004 | one page of results |
| An empty fragment changes nothing | T005 | the box being empty on load |
| The fragment is text, not syntax | T006 | it working for ordinary names |
| Accents do not fold | T006 | nobody having tried one |
| The keyboard path works | T013 | the feature working with a mouse |
| A miss is legible | T012 | an empty list |
| **That the query is fast enough** | **T014's measurement** | 250 sounding like a small number |
| **That the operator finds the camera they meant** | **nothing** | matches being returned correctly |

The last row is the honest one. Their remembered name may not be the catalogue's
— which is why a legible miss (T012) matters more than any refinement of the
match.
