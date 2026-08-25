# Feature Specification: A fab identifier can be sorted, in every context that has one

**Feature Branch**: `039-comparable-fab-identifier`

**Created**: 2026-08-25

**Status**: Draft

**Issue**: 1849 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: A fab identifier cannot be ordered, in any of the eight contexts that
define one, while the camera name beside it can. The camera listing breaks ties
on the fab, so a test whose rows tie throws where the deployed listing sorts
correctly. The decision taken on 1849 is that **all eight** gain the ability, and
that a convention check keeps them in step.

---

## Why this exists

Someone writes a perfectly ordinary test: two cameras, registered at the same
instant, list them. It throws — with a message naming neither the field being
sorted nor the query doing the sorting.

Diagnosing it takes half an hour. You have to notice that the listing breaks ties
on the fab, that the test's data source is not the database, and that a fab
identifier cannot be ordered while the camera name beside it can. Then you learn
that the obvious repair is worse than the problem, back out, and give your two
cameras different timestamps for a reason that has nothing to do with what you
were testing.

That half-hour is the whole cost, and it is paid again by whoever writes the next
tying test. **Nothing an operator can reach is affected** — the deployed listing
translates the whole sort to the database and has always been correct.

### The asymmetry

Eight bounded contexts each define their own fab identifier. That is deliberate:
value objects are not shared across contexts. The grammar is identical in all
eight, deliberately too.

**What is not deliberate is that none of them can be ordered**, while the camera
name sitting in the same folder can. Nobody decided that. It fell out of nobody
needing it until a sort reached for it.

### Why it stays hidden

Every existing listing test gives its rows distinct primary sort keys, so the
tie-break is never consulted and the suite looks healthy. The failure waits for
the first test that ties — which is a natural thing to write and says nothing
about fabs at all.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A tying test can be written (Priority: P1)

Someone writes a listing test where two rows share the primary sort key, because
the timestamp is irrelevant to what they are asserting. It runs. The rows come
back ordered by fab, which is what the deployed listing does, so the test is
testing the real behaviour rather than a fixture arranged to avoid it.

**Why this priority**: This is the feature. Everything else either extends it to
the other seven contexts or stops it regressing.

**Independent Test**: A listing test whose rows tie on the primary sort key
returns them in fab order.

**Acceptance Scenarios**:

1. **Given** two cameras that tie on the primary sort key and differ by fab,
   **When** the listing is requested,
   **Then** they are returned **ordered by fab** — not merely returned without
   error.
2. **Given** the same, sorted by the other sortable field,
   **When** the listing is requested,
   **Then** the tie is broken by fab there too.
3. **Given** two fabs whose relative order differs between an ordinal comparison
   and a culture-sensitive one,
   **When** they are ordered,
   **Then** the ordinal answer is the one produced.

---

### User Story 2 — The eight stay identical (Priority: P1)

Someone adds a ninth context with its own fab identifier, or edits one of the
eight. If the new or edited one cannot be ordered, a check fails and tells them
why — before the gap can wait for a sort to find it.

**Why this priority**: Equal to US1, and the reason for doing all eight rather
than the one. Without it the fix restores the symmetry and nothing preserves it,
which is how the asymmetry arose in the first place.

**Independent Test**: Removing the ability from one copy fails the check.

**Acceptance Scenarios**:

1. **Given** every fab identifier can be ordered,
   **When** the check runs,
   **Then** it passes.
2. **Given** one of them has lost the ability,
   **When** the check runs,
   **Then** it fails, **names the offending context**, and explains what breaks —
   because the runtime failure it prevents names neither the sort field nor the
   query.

---

### User Story 3 — The workaround comment goes (Priority: P2)

The comment warning the next author about this trap is deleted, because the trap
is gone.

**Why this priority**: P2 because it changes no behaviour. But a warning that
outlives its hazard is worse than no warning: it costs a reader time and teaches
them something false about the code.

**Independent Test**: No test carries a comment explaining how to avoid a tie.

**Acceptance Scenarios**:

1. **Given** the fix has landed,
   **When** the tests are read,
   **Then** no comment explains that ties must be avoided, and the test that
   carried one still passes.

---

### Edge Cases

- **Comparing against nothing.** Ordering a fab identifier against an absent one
  must give a definite, conventional answer. No ordinary sort reaches this, which
  is exactly why it is the case implementers forget.
- **Two identical fabs.** They compare equal, and the surrounding sort leaves
  their relative order to whatever came before — which is the same as the
  database's behaviour and is why the tie-break exists at only one level.
- **A context whose fab is never sorted.** Seven of the eight are in this
  position today. They gain the ability anyway; see Assumptions.
- **Fabs differing only by case.** Cannot occur: the grammar admits lowercase
  letters, digits and hyphens only, and is enforced when one is created. This is
  what makes the comparison simpler than the camera name's, which must
  deliberately ignore case because it preserves display casing.
- **A sort that reaches the database versus one that does not.** Both must give
  the same order. That equivalence is the point — the reason the current
  behaviour is a test-only trap rather than a defect is that the database path
  was always right.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A fab identifier MUST be orderable, in **every** context that
  defines one.
- **FR-002**: The ordering MUST be **ordinal** — a byte-by-byte comparison of the
  identifier, not one that varies with culture or locale.
- **FR-003**: The ordering MUST NOT apply any normalisation step. The grammar
  admits exactly one spelling of any identifier, so there is nothing to
  normalise, and a normalisation step would be a rule with no case that exercises
  it.
- **FR-004**: Ordering a fab identifier against an absent one MUST give the same
  conventional answer the camera name already gives, so the two behave alike
  where they are used alike.
- **FR-005**: The camera listing MUST return rows that tie on the primary sort
  key **ordered by fab**, on every sort path that breaks ties that way.
- **FR-006**: The deployed listing's behaviour MUST NOT change. It is correct
  today, and this feature exists so that the same behaviour is reachable from a
  test.
- **FR-007**: A check MUST fail when any context defines a fab identifier that
  cannot be ordered.
- **FR-008**: That check's failure MUST name the offending context and state what
  it prevents. The failure it exists to stop names neither the field being sorted
  nor the query doing the sorting, so a check that merely says "assertion failed"
  reproduces the original half-hour.
- **FR-009**: The comment warning authors away from ties MUST be removed, and the
  test carrying it MUST still pass.
- **FR-010**: The tie-break itself MUST NOT change. It is load-bearing: without
  it, two rows sharing a sort key have no defined relative order, and a page
  boundary can show one of them twice and the other never.

### Key Entities

- **Fab identifier**: the plant an entity belongs to. Defined independently in
  each bounded context, with a grammar that is identical across all of them by
  design. Currently equatable but not orderable.
- **Camera name**: the value object beside it that is already orderable, and the
  model for what this adds. Differs in one respect: it preserves display casing
  and so must ignore case when comparing, which a fab identifier need not.
- **Tie-break**: the second-level ordering the camera listing applies when its
  primary sort key is equal. The thing that consults the comparison, and the
  reason it cannot simply be removed.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A listing test whose rows tie on the primary sort key can be
  written and passes, **with no workaround** — the case that cannot be written
  today.
- **SC-002**: Rows that tie come back in a **specified order**, demonstrated by
  the order itself. An ordering that treats everything as equal also raises no
  error, and would leave the paging defect the tie-break exists to prevent.
- **SC-003**: **Zero** contexts define a fab identifier that cannot be ordered.
- **SC-004**: Removing the ability from one context fails the check, demonstrated
  by doing it rather than by trusting it would.
- **SC-005**: The deployed listing behaves exactly as before — the tests covering
  it pass untouched, apart from the one that loses its workaround comment.
- **SC-006**: No comment anywhere explains how to avoid a tie.

---

## Assumptions

- **All eight, not the one that needs it.** Seven of the eight gain an ability no
  current caller uses, which normally reads as generality for a need that does
  not exist. It is admitted deliberately, and the argument is that this is not a
  new abstraction but a **gap between copies that are meant to be identical**.
  The grammar is already the same in all eight by design; letting the ordering
  differ where the grammar does not is how copies stop being copies. The camera
  name beside them already has it. The alternative — fixing one and leaving seven
  different — introduces an asymmetry that is invisible until someone sorts, and
  the failure when they do is the one this feature exists to remove.
- **The check is worth its keep for the same reason.** It is what makes "keep
  them in step" structural rather than a habit, and this repo already keeps
  several conventions honest that way.
- **The comparison operators come with it.** The camera name defines them; a fab
  identifier that could be ordered but not compared with the ordinary operators
  would be a second, smaller asymmetry in place of the one being removed.
- **The three call sites that order fabs through their raw value stay as they
  are.** They are explicit about wanting an ordinal comparison, they are correct,
  and none of them is inside a query that must reach the database. Changing them
  for consistency would churn working code; the one call site that *cannot* take
  that approach is precisely the one that breaks, which is why the value object
  is where this belongs. Recorded here because "now make them consistent" is the
  obvious follow-on.
- **No stored data changes**, no query changes, and nothing an operator can
  observe changes.

---

## Out of Scope

- **The tie-break itself** (FR-010). Removing it is the tempting way to make a
  failing test pass, and it trades a test-only error for a real paging defect.
- **The three call sites that order fabs through their raw value.** See
  Assumptions.
- **Any other value object.** Nothing else is in this position, and giving one an
  ordering it does not need would be the speculative generality this feature is
  at pains not to be.
- **Any change to how data is stored, queried or translated.**
