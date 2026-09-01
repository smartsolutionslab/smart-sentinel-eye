# Feature Specification: Find a camera by name

**Feature Branch**: `055-find-a-camera-by-name`

**Created**: 2026-09-01

**Status**: Draft

**Input**: User description: "An operator cannot find a camera by name, and prefix type-ahead fails exactly where fab naming puts the distinguishing word"

## Context

Spec 048 made every camera *reachable* — the picker no longer stops at fifty, and
it reports what it could not show. It did not make any camera **findable**.

Today an operator relies on the browser's own type-ahead on a native list, which
matches the **start** of a name. That works for `Furnace 3` and fails for
`Line 2 Furnace` — and fab naming conventions routinely put the distinguishing
word last: `Bay 4 Inlet`, `Line 2 Furnace`, `Hall A Coiler`.

So the operator who knows exactly which camera they want, and knows its name, is
the one the system serves worst. They must either know how the name begins, or
page through up to 250 of them.

Nothing in the system can answer "the cameras called *furnace*" — there is no
name filter on any camera list, and no screen offers a search box.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The operator knows the name and still cannot get to it (Priority: P1)

An operator building a wall needs the camera they think of as "the furnace one".
Its full name is `Line 2 Furnace`. They type `furn` into the picker and nothing
happens, because the match is on the start of the name. They do not know whether
the camera is missing, retired, in another fab, or simply named differently — the
system gives them no way to tell those apart.

They need to type part of the name, in any position, and see what matches.

**Why this priority**: This is the feature. Everything else here exists to make
this trustworthy.

**Independent Test**: With cameras whose distinguishing word is not first, type a
middle fragment and confirm the matching cameras appear and can be chosen.

**Acceptance Scenarios**:

1. **Given** a camera named `Line 2 Furnace`, **When** the operator types `furn`,
   **Then** that camera appears among the matches.
2. **Given** cameras whose names differ only in case from what was typed,
   **When** the operator types any casing, **Then** the matches are the same.
3. **Given** a fragment that matches nothing, **When** the operator types it,
   **Then** the screen says plainly that nothing matched — distinguishable from
   "still loading" and from "there are no cameras at all".
4. **Given** a fragment typed with surrounding spaces, **When** the operator
   types it, **Then** the result is the same as without them.
5. **Given** the operator clears the fragment, **When** the field is empty,
   **Then** the full list returns, exactly as it behaves today.

---

### User Story 2 - A filtered list that says how much it is showing (Priority: P1)

An operator types a fragment, sees a handful of cameras, and needs to know
whether that handful is all of them. The list already reports a total, and every
consumer uses that total to decide whether it has everything.

If a filtered list reports the **unfiltered** total, the operator is told there
are 250 when eleven matched, and any caller comparing what it holds against that
total concludes it is missing 239 that do not exist.

**Why this priority**: Equal first. A filter that lies about its own population is
worse than no filter, because it reads as authoritative. This is the same defect
class already filed against consumers that render one page as though it were the
whole list.

**Independent Test**: Filter to a known subset and confirm the reported total
equals the number of matches, not the catalogue size.

**Acceptance Scenarios**:

1. **Given** a filter matching eleven of 250 cameras, **When** the list is
   returned, **Then** the total it reports is eleven.
2. **Given** a filter whose matches exceed one page, **When** the operator moves
   through pages, **Then** every page is drawn from the matches only, and the
   total stays the count of matches.
3. **Given** no filter, **When** the list is returned, **Then** the total is the
   catalogue size, unchanged from today.

---

### User Story 3 - Typing is not the only way in (Priority: P2)

An operator working by keyboard, or with a screen reader, uses the camera chooser
today and gets the platform's own behaviour for free: arrow keys move through
options, typing jumps, Escape closes, and the control announces itself and its
selection.

Replacing that chooser with one that filters must not take any of that away.

**Why this priority**: Second only because it constrains *how* stories 1 and 2 are
delivered rather than adding capability. It is not optional — a chooser that
requires a mouse removes a way of working that exists today.

**Independent Test**: Complete the whole task — open, filter, choose — using only
the keyboard, and confirm the control's role, its current selection, and the
number of matches are announced.

**Acceptance Scenarios**:

1. **Given** the chooser is focused, **When** the operator uses only the
   keyboard, **Then** they can open it, filter, move between matches, choose one,
   and close it without a pointer.
2. **Given** a screen reader, **When** the chooser is focused, **Then** its role
   and its current value are announced.
3. **Given** a filter has been typed, **When** the matches change, **Then** the
   number of matches is announced rather than changing silently.
4. **Given** the operator presses Escape, **When** the chooser is open, **Then**
   it closes and the previous selection is unchanged.

---

### Edge Cases

- **A fragment that matches everything** — e.g. a single common letter. The list
  must behave exactly as an unfiltered one, including its paging.
- **A fragment matching more than one page of cameras.** Filtering and paging
  must compose; neither may quietly cap the other.
- **Names differing only by accent or diacritic.** Whether `Fürnace` matches
  `furn` is a decision, not an accident, and an operator cannot tell an
  unmatched rule from a missing camera.
- **A retired camera whose name matches.** Retired cameras are already excluded
  unless asked for; filtering must not change that.
- **Cameras in another fab whose names match.** Fab scoping already applies and
  the filter must not widen it.
- **The operator types faster than results return.** A stale result set arriving
  after a newer one must not replace it.
- **An operator relying on the current start-of-name type-ahead.** That behaviour
  is being replaced; typing a name's beginning must still find it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An operator MUST be able to filter cameras by a fragment of the
  name, matching anywhere in the name rather than only at the start.
- **FR-002**: Matching MUST be case-insensitive.
- **FR-003**: Leading and trailing whitespace in the fragment MUST be ignored.
- **FR-004**: The matching rule MUST be stated in the record — including how
  accents are treated — so an operator who sees no matches can tell an unmatched
  rule from an absent camera.
- **FR-005**: A filtered list MUST report a total describing **the matches**, not
  the unfiltered catalogue.
- **FR-006**: Filtering MUST compose with paging: every page is drawn from the
  matches, and the total remains the count of matches.
- **FR-007**: An empty or absent fragment MUST behave exactly as the list behaves
  today.
- **FR-008**: Filtering MUST NOT widen the fab scoping or the retired-camera
  handling that already apply.
- **FR-009**: "No camera matched" MUST be distinguishable, on screen, from "still
  loading" and from "there are no cameras".
- **FR-010**: The chooser MUST remain fully operable by keyboard alone — open,
  filter, move, choose, dismiss.
- **FR-011**: The chooser MUST announce its role, its current value, and the
  number of matches to assistive technology.
- **FR-012**: A camera findable today by typing the beginning of its name MUST
  remain findable that way.
- **FR-013**: Results arriving out of order MUST NOT replace newer results.
- **FR-014**: Whether filtering is fast enough at the fab-scale target MUST be
  **measured and recorded**, not assumed — in either direction.

### Out of scope

- **General search.** Audit has its own search, its own screen and its own
  pagination. This is one field on one list.
- **Filtering by anything but name** — fab, retired state, address, registration
  date. Sorting is likewise unchanged.
- **Changing the list's page-size ceiling**, or the reason it refuses rather than
  clamps above it. That is tracked separately and the reasoning is recorded.
- **Making this a performance feature.** FR-014 asks for a measurement. If the
  answer is "plainly fast enough at this scale", that is the answer.

### Key Entities

- **Name fragment**: what the operator typed, after trimming. Not a pattern
  language — an operator types words, not expressions.
- **Filtered page**: the matches for a fragment, with a total that counts the
  matches and nothing else.
- **Camera chooser**: the control an operator picks a camera with. Today it
  offers the platform's list behaviour; it must gain filtering without losing
  that.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can find any camera by typing any distinctive part of
  its name, including a part that is not at the beginning.
- **SC-002**: **100% of filtered results report a total equal to the number of
  matches.** No screen or caller can be told a filtered list is larger than it is.
- **SC-003**: The whole task — open the chooser, narrow to one camera, choose it
  — is completable **using only the keyboard**.
- **SC-004**: An operator who types a fragment that matches nothing is told so
  explicitly, and can distinguish it from a list still loading.
- **SC-005**: The matching rule is written down where someone deciding whether a
  camera is missing will find it.
- **SC-006**: The time to return matches at the fab-scale target is measured and
  recorded, whatever it turns out to be.
- **SC-007**: No camera reachable today by typing the start of its name becomes
  unreachable.

## Assumptions

- **The fab-scale target is 250 cameras per fab**, per the constitution. The
  filter is specified against that, not against a hypothetical larger estate.
- **Both the picker and the cameras list page get the filter.** The pain was
  found in the picker, but an operator managing 250 cameras on the list page has
  the same problem, and shipping it to one screen would invite the question
  immediately. If that proves to double the work rather than share it, the picker
  is the one that must ship.
- **Names are unique per fab, case-insensitively, 1–200 characters**, per spec
  001. The filter inherits that; it does not restate it.
- **An operator searching means to find a camera they believe exists.** The
  design favours telling them plainly when nothing matched over guessing at what
  they meant. Suggestions, fuzzy matching and "did you mean" are not in scope.
- **The existing list contract is extended, not replaced.** Consumers that send
  no fragment see exactly what they see today.
