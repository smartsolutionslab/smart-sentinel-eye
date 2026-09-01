# Verification — 055 find a camera by name

Phase 5.

---

## 0. What shipped

An operator can type any part of a camera's name — including a part that is not
at the beginning — and find it, from both screens where cameras are chosen.

The match rule is in ADR-0137. This note records how it was built, what it cost,
and the three things that were nearly wrong.

**No migration, no index, no new package.** The column matching needs already
existed for uniqueness, and the picker already had the accessibility.

---

## 1. Automated checks

| Check | Result |
|---|---|
| Full solution build (Release) | succeeds |
| `CameraCatalog.Application` (incl. 14 new) | **74 pass** |
| `ListCamerasByNameIntegrationTests` | **7 pass**, against real Postgres |
| `management-web` | **227 pass** (12 new) |
| `apps/shared` | **142 pass** |
| management-web lint, typecheck | clean |

**The coverage gate is live and cited.** CameraCatalog's Application layer is
touched, so ADR-0065's ≥80% Application threshold applies.

---

## 2. Mutations, and the three that were misnamed

Seven verified by running them, not by reasoning about them.

| Mutation | Killed by | Failures |
|---|---|---|
| Count the population **before** the name filter | handler tests | 3 |
| Stop trimming the fragment | handler tests | 3 |
| Drop the case folding | handler tests | 9 |
| Interpolate the fragment, so `%` is a wildcard | **integration** | 2 |
| Drop the offset reset when the fragment changes | page tests | 1 |
| Remove the retained camera | designer tests | 1 |

Six rows above; a seventh (moving the count above `SortBy`) is the first entry in
the list below, which is where it belongs — it survived.

**Three were misnamed, and running them is the only reason that is known.**

1. **The first "count before filtering" moved the count above `SortBy`**, not
   above the filter. It survived, and it should have — sorting does not change the
   row set. Recorded as killed, it would have been a false positive on this
   feature's most important guard.
2. **"Treat an empty fragment as match nothing" is not expressible here.** A blank
   fragment trims to the empty string and `Contains("")` is true of every name, so
   the code returns everything however the null check is spelled. The mutation
   that exists is dropping the trim.
3. **Mutation 6 was attributed to a handler test.** It survives every one of
   them: whether the fragment reaches the database as text or as a pattern is
   decided below the in-memory fake. Only the integration test catches it — which
   is also the answer to "why an integration test when the handler is covered".

---

## 3. The measurement FR-014 required

Taken against the dev catalogue at **259 cameras**, above the 250-per-fab target.

| Query | Runs | Time |
|---|---|---|
| Filtered count | 5 | **0.230 – 0.331 ms** |
| Filtered, sorted, paged | 3 | **0.226 – 0.375 ms** |
| Unfiltered count (baseline) | 1 | 0.214 ms |

**No index was added, and that is the result rather than an omission.** The btree
on `(fab, name_normalized)` serves a prefix and cannot serve a substring, so the
filter scans the fab's rows — and at this scale that costs a fraction of a
millisecond. A trigram index would mean an extension, a migration and a second
thing to keep true.

The measurement was the deliverable. It came back "plainly fast enough", which is
a real answer and the one the spec said to accept if it arrived.

---

## 4. What the checks cannot prove

| Claim | Proved by | Not proved by |
|---|---|---|
| A middle fragment finds the camera | handler + integration tests | the screen having a search box |
| The total counts the matches | asserted at the handler **and** through HTTP | the number looking plausible |
| The fragment is text, not syntax | integration only | it working for ordinary names |
| Accents do not fold | an **integration** test against the real column | a handler test, which normalises both sides the .NET way |
| An assigned tile survives a filter | designer tests, including one that fails **without** the retention | the tile looking right unfiltered |
| Filtering resets the page | a page test that fails without the reset | filtering from page one |
| The task completes with no pointer | a test that never clicks | the feature working with a mouse |
| The query is fast enough | §3's measurement | 259 sounding like a small number |
| **That an operator finds the camera they meant** | **nothing** | matches being returned correctly |
| **That the filter is pleasant by keyboard** | **nothing** | it being possible |

The last two are the honest ones. Their remembered name may not be the
catalogue's, which is why a legible miss matters more than any refinement of the
match.

---

## 5. Three things that were nearly wrong

1. **A filter that blanks a tile it has already filled.** Options come from the
   server's matches, so excluding a camera some tile already holds leaves a
   select whose value has no option: blank on screen while the form still carries
   it. An operator reads that as lost and reassigns it. **The spec did not name
   this**; it was found while building, and the guard is a test that fails when
   the retention is removed.

2. **Deleting spec 048's truncation notice.** While editing around it, the
   replacement simply did not carry it. Three of that spec's tests failed within
   the minute. A feature another spec built was removed and only its tests
   noticed — which is the argument for those tests, stated by their working.

3. **A ref read during render.** The retention was first written as a ref mutated
   and read during render, which the hooks linter refused. It was right: a ref
   read during render and a `setState` inside an effect both produce the
   cascading-render bug those rules exist to stop. It is now state adjusted
   during render.

---

## 6. One test was changed, and why that is not weakening it

`LayoutsPage.test.tsx` counted camera controls with
`getAllByLabelText(/camera/i)` and expected two — the tile pickers of a 1×2 grid.
The new "Find a camera" field matches that pattern as truly as the pickers do, so
it found three.

The assertion's claim was always about the **pickers**, so it now counts by
combobox role — which is how the sibling assertions in
`LayoutEditorDialog.test.tsx` already spell the same thing. That is strictly more
specific than what it replaced, not looser.

---

## 7. Phase 6 — ten findings, and what they were about

All ten confirmed by reading the code; eight fixed, two recorded as decisions.

| # | Finding | Outcome |
|---|---|---|
| 1 | The retention was dead after a close and reopen | fixed — merge converges on contents |
| 2 | A search matching nothing disabled Save | fixed — reads "ever seen a camera" |
| 3 | The picker's placeholder said "No cameras in this fab" under a filter | fixed |
| 4 | Two `upper` implementations, nothing pinning them together | fixed — integration test |
| 5 | No debounce on a query that walks five pages | fixed — shared hook |
| 6 | Retained options escaped fab-qualification | fixed |
| 7 | The status could print a sentinel as a match count | fixed |
| 8 | `name` is the only list parameter without validation | **recorded as a decision**, ADR-0137 |
| 9 | Two ADR claims stated more firmly than established | fixed |
| 10 | This note's §4 overstated the accent row | fixed |

**Seven of the ten are one mistake.** Findings 2, 3 and 7 are all the same shape
as the defect this feature exists to prevent — treating "nothing matched" as
"nothing exists" — committed three more times in the very change that fixes it,
in the disabled Save, in the placeholder a screen reader announces, and in a
status line contradicting the notice above it. Finding 1 is the fix for the
*other* hazard, shipped broken.

**The tests did not catch any of them, and the reason is worth keeping.** The
retention tests handed the designer a map that already held the camera, so they
exercised the half that worked. The accent claim was asserted against a fake that
normalises both sides in .NET, so it could not have failed. Both were tests
pointed one layer away from where the mistake was.

### Two things found while fixing

1. **A crash, not a misbehaviour.** The original merge keyed on the response
   array's identity, so any response that is not reference-stable set state every
   render and React aborted the component — *too many re-renders*. Found because
   a mock returned a fresh empty array, which is what a filter matching nothing
   produces.

2. **A test that proved nothing, for a whole hour.** Adding the debounce made the
   reopen test assert before the filter was in force, so it passed with the
   retention entirely unwired. Caught only by running that mutation and checking
   the mutation had applied — the second time this spec that a mutation had to be
   verified before its result could be read.

## 8. Phases

- Phases 1–4: the query, the contract, the screens, the record.
- Phase 5: this note.
- Phase 6: §7 above.
