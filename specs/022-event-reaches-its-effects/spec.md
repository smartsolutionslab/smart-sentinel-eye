# Feature Specification: A plant-floor event reaches the things it is supposed to drive

**Feature Branch**: `022-event-reaches-its-effects`

**Created**: 2026-08-19

**Status**: Draft

**Input**: Issue #1635, raised from the code review of spec 021 (PR #1634).

## Why this exists

The product's purpose is that something happening on the plant floor changes
what an operator sees. Nothing verifies that it does.

Every part of that journey is tested. The receiving of the event, the deciding
what it means, the setting of the value, the showing on the screen — each has
tests, and each passes. What has never been tested is the journey itself, and
the parts are joined by messages passing between four separate services.

That is not a hypothetical gap. It was open, something broke exactly there, and
**every test in the repository passed** — 228 integration tests, twenty coverage
gates, a green build. The break was found by a person reading the code. Nothing
that runs could see it, because nothing that runs follows the whole path.

This feature adds the test that would have caught it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An event changes what the operator sees (Priority: P1)

A machine on the line reports something. A rule says that when this happens, a
number on the operator's screen should change. The operator watches the screen
and the number changes.

Today, if that stopped working, the system would report no error, log nothing
unusual, and every test would still pass. The event would be stored and
readable, so anyone checking would find it right where it should be.

**Why this priority**: It is the product working. Everything else here supports
proving it.

**Independent Test**: Set up such a rule, make the machine report, and watch the
number — through the real path, not by inspecting anything in between.

**Acceptance Scenarios**:

1. **Given** an active rule that sets a value when a particular event arrives,
   **When** that event arrives from the plant floor, **Then** the value is
   changed.
2. **Given** an active rule that highlights something on screen when an event
   arrives, **When** that event arrives, **Then** the highlight happens.
3. **Given** an event that no rule matches, **When** it arrives, **Then**
   nothing is changed and nothing is highlighted.
4. **Given** the journey is broken anywhere along it, **When** the event
   arrives, **Then** this test fails — which is the entire point, and is the
   thing the existing tests could not do.

---

### User Story 2 - The proof is of the effect, not of the attempt (Priority: P1)

A test that checks "the instruction to change the value was sent" would have
passed while the value was never changed — because that is precisely what the
broken system did: it sent the instruction somewhere nothing was listening.

**Why this priority**: Also P1, and not a refinement of US1. A test of this
journey that asserts the wrong thing is worse than no test, because it reports
that the journey is covered.

**Independent Test**: Read the test and ask what it would have done against the
known break. If it would have passed, it is the wrong test.

**Acceptance Scenarios**:

1. **Given** the test, **When** it is compared against the failure that
   prompted it, **Then** it would have failed against that failure.
2. **Given** the test, **When** it is read, **Then** what it asserts is the
   changed value and the visible highlight, not any intermediate step.

---

### User Story 3 - It runs where it will be noticed (Priority: P2)

A test that only runs when someone remembers to run it protects nothing. This
journey has no automated coverage at all today, so a test excluded from the
routine build leaves it exactly where it was.

**Why this priority**: The two previous features each excluded one test from the
routine build for defensible reasons. A third exclusion, on the one path that
most needs watching, would make the pattern the problem.

**Independent Test**: Confirm the test runs in the routine build without special
arrangement, and passes repeatedly.

**Acceptance Scenarios**:

1. **Given** the routine build, **When** it runs, **Then** this test runs with
   it.
2. **Given** the test runs repeatedly, **When** the results are compared,
   **Then** it passes consistently rather than sometimes.
3. **Given** it cannot be made reliable enough to run routinely, **When** that
   is concluded, **Then** the reason is recorded and the cost stated plainly,
   rather than the exclusion being made quietly.

---

### Edge Cases

- **A rule exists but is not active.** A rule that has been written but not put
  into service should not fire. A test that seeded an inactive rule and saw
  nothing happen would pass for entirely the wrong reason, and would keep
  passing after the journey broke.
- **The same event arrives twice.** The plant floor can redeliver, and the
  system deliberately tolerates that. The effect should be as though it arrived
  once.
- **The effect takes longer than expected.** Four services and a message broker
  are involved. The test must distinguish "slower than the budget" from "never
  happened", because those need different responses.
- **Two rules match the same event.** Both effects should happen.
- **A rule belonging to another plant.** An event from one plant must not
  trigger another plant's rules.

## Requirements *(mandatory)*

### The journey

- **FR-001**: A plant-floor event that matches an active rule MUST be shown to
  produce the effect that rule describes — the value changed, the highlight
  shown — end to end, without inspecting anything in between.
- **FR-002**: The event MUST enter by the same route a real machine uses, so
  that the whole path is exercised rather than a convenient shortcut into the
  middle of it.
- **FR-003**: An event matching no active rule MUST be shown to produce no
  effect, so the test can tell "it worked" from "everything looks the same".

### What the proof must be

- **FR-004**: The assertion MUST be on the observable effect. An assertion that
  an instruction was issued is explicitly insufficient, because the known
  failure issued instructions into a void.
- **FR-005**: The test MUST fail if any part of the journey is broken, including
  the joins between services, which is where the failure that prompted this
  occurred.
- **FR-006**: A rule used in the test MUST be genuinely active, so that a
  passing result cannot be explained by the rule never having been eligible.

### Where it runs

- **FR-007**: The test MUST run as part of the routine build, unless it is
  demonstrated that it cannot be made reliable there.
- **FR-008**: If it is excluded, the exclusion MUST be recorded with its reason
  and its cost — namely that this journey returns to having no automated
  coverage.
- **FR-009**: Waiting for a slow effect MUST be bounded and MUST distinguish a
  late effect from an absent one.

### Deliberately unchanged

- **FR-010**: No behaviour changes. This feature adds proof of existing
  behaviour and MUST NOT alter it. If the effect turns out not to be observable
  without changing the product, that is a finding to raise rather than a change
  to make quietly.

## Key Entities

- **Rule**: a statement that when a particular kind of event arrives, a
  particular effect should follow. Has a lifecycle; only an active one applies.
- **Event**: something reported from the plant floor.
- **Effect**: the observable consequence — a changed value, a highlight on a
  screen. What this feature asserts.
- **The journey**: event → decision → effect, crossing four services. The thing
  that has never been tested as a whole.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With an active rule in place, an event arriving from the plant
  floor results in the described effect **100%** of the time across repeated
  runs.
- **SC-002**: The test fails when the journey is broken at any join — verified
  by breaking it deliberately, not by assuming.
- **SC-003**: An event matching no rule produces **zero** effects.
- **SC-004**: The test runs in the routine build and passes on **at least three
  consecutive runs**, or its exclusion is recorded with the coverage cost stated.
- **SC-005**: The time from the event arriving to the effect being observable is
  measured and compared against the budget that covers this path; if the test
  environment cannot measure it meaningfully, that is stated rather than
  omitted.
- **SC-006**: Someone reading the test can tell what it would have done against
  the failure that prompted it, without running it.

## Assumptions

- **The journey is currently working.** Spec 021 fixed the break. This feature
  proves the state of things rather than repairing it; if it finds the journey
  broken, that is a defect to raise.
- **A rule can be created and activated through the ordinary interface.** The
  test seeds its own rule rather than depending on fixture data, so it is
  self-contained and cannot pass because of something another test left behind.
- **Effects are observable from outside.** A changed value can be read back. If
  the on-screen highlight turns out not to be observable without changing the
  product, FR-010 applies: raise it rather than build it.
- **Slowness is expected.** Four services and a broker; a generous bound with a
  clear distinction between late and absent is the intended approach.
- **The existing per-part tests stay.** This adds the journey; it does not
  replace the tests of the parts, which fail faster and localise better.

## Out of scope

- **Performance under load.** One event through the journey, not many. Sustained
  throughput is spec 020's and spec 021's ground.
- **Every rule action type.** Enough to prove the journey for the effects that
  exist, not an exhaustive matrix.
- **The screen itself.** The highlight is asserted where it becomes observable,
  not by driving a browser — that is the end-to-end suite's job.
