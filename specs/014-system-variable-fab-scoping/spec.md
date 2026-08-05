# Feature Specification: Fab-scope system variables

**Feature Branch**: `014-system-variable-fab-scoping`

**Created**: 2026-08-05

**Status**: Draft

**Input**: #1310 — "System variables are globally named, so two fabs' rules overwrite each other's values"

## Why this exists

Spec 013 stopped an event from one fab firing another fab's rules (#1252). It
did not stop the *result* of those rules colliding.

A system variable is one row for the whole installation. Two fabs that both
track `oeeLine1` share it. Munich's rule sets it to 60, Dresden's sets it to
42, one value survives, and both fabs' kiosks display it. Evaluation is
fab-scoped; the write it produces is not.

The originating fab already travels with the request — Automation stamps it on
every value-change message it publishes. Nothing on the receiving side reads
it, because there is nowhere in the variable model to put it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Two fabs keep their own values (Priority: P1)

An operator in Munich and an operator in Dresden each track overall equipment
effectiveness for their line, and each calls it `oeeLine1` because that is what
it is called on the shop floor.

**Why this priority**: This is the defect. Until it is fixed, one fab's
production figure silently overwrites another's, and the number on a kiosk may
belong to a factory hundreds of kilometres away. It is wrong in a way nobody
sees, which is what makes it the priority.

**Independent Test**: Define `oeeLine1` in both fabs, drive an event in each,
and read both back. Each holds its own value.

**Acceptance Scenarios**:

1. **Given** `oeeLine1` exists in Munich and in Dresden, **When** a Munich
   event sets it to 60, **Then** Munich's reads 60 and Dresden's is unchanged.
2. **Given** the same, **When** a Dresden event sets it to 42 immediately
   afterwards, **Then** Dresden's reads 42 and Munich's still reads 60.
3. **Given** two fabs each hold a variable of the same name, **When** one is
   archived, **Then** the other is untouched and still readable.

---

### User Story 2 - A kiosk shows only its own fab's values (Priority: P1)

An overlay in Munich referencing `oeeLine1` resolves Munich's value, on first
paint and on every live update.

**Why this priority**: P1 alongside US1, because without it the defect is only
half closed — the stored values would be right and the screen would still be
wrong, which is worse than an obvious failure.

**Independent Test**: Publish an overlay in each fab referencing the same
variable name, change one fab's value, and watch both screens.

**Acceptance Scenarios**:

1. **Given** overlays in two fabs referencing the same variable name, **When**
   a kiosk in Munich loads its overlay, **Then** it shows Munich's value.
2. **Given** the same, **When** Munich's value changes, **Then** only Munich's
   kiosk updates.
3. **Given** the same, **When** Dresden's value changes, **Then** Munich's
   kiosk does not update.

---

### User Story 3 - An operator cannot see or change another fab's variables (Priority: P2)

An operator assigned to Dresden lists and edits variables. Munich's are neither
listed nor reachable, including by guessing a name.

**Why this priority**: The variable endpoints have no fab check at all today —
any authenticated operator can read and change every fab's variables. It ranks
below the P1s because it takes someone acting, where those happen on their own.

**Independent Test**: As a Dresden-only operator, list variables and request a
Munich variable by name.

**Acceptance Scenarios**:

1. **Given** variables exist in both fabs, **When** a Dresden-only operator
   lists them, **Then** only Dresden's appear.
2. **Given** a Munich variable's name, **When** a Dresden-only operator
   requests it, **Then** the response is indistinguishable from a name that was
   never used.
3. **Given** the same, **When** they attempt to change or archive it, **Then**
   it is refused and the variable is unchanged.

---

### User Story 4 - Authoring picks up the operator's fab (Priority: P2)

Defining a variable does not make a single-fab operator state which fab it is
for; an operator holding several must choose.

**Why this priority**: Matches how rules already behave, so an operator meets
one rule rather than two. Same size as US3 and independent of it.

**Independent Test**: Define a variable as a single-fab operator, then as a
multi-fab one.

**Acceptance Scenarios**:

1. **Given** an operator assigned to one fab, **When** they define a variable
   without naming a fab, **Then** it is created in theirs.
2. **Given** an operator assigned to several, **When** they define one without
   naming a fab, **Then** they are asked to choose and nothing is created.
3. **Given** any operator, **When** they name a fab they do not hold, **Then**
   it is refused.

---

### User Story 5 - A rule pointing at another fab's variable is visibly ignored (Priority: P3)

A rule in Munich whose action names a variable that exists only in Dresden
changes nothing, and the fact that it changed nothing is discoverable.

**Why this priority**: Lowest because it takes a misconfiguration to reach. It
is in scope because the alternative is a rule that silently does nothing — the
precise shape of #1252, which went unnoticed for a release.

**Independent Test**: Author a Munich rule targeting a Dresden-only variable,
drive a matching Munich event, and look for the record of the drop.

**Acceptance Scenarios**:

1. **Given** a Munich rule naming a variable that exists only in Dresden,
   **When** a matching Munich event arrives, **Then** Dresden's variable is
   unchanged.
2. **Given** the same, **Then** the ignored request is recorded with the fab
   and the variable name, so an operator can tell a misconfigured rule from a
   rule that never matched.

---

### Edge Cases

- A value-change request arriving with no fab: nothing changes anywhere, and
  the drop is recorded. Silence would be indistinguishable from success.
- Variables that exist before this feature: they belong to the one fab that was
  live, and the number reassigned is stated at the moment it happens rather
  than assumed.
- The same name defined in a second fab while the first is live: accepted.
- A name freed by archiving: reusable within that fab only.
- An operator assigned to no fab: refused rather than shown an empty list, so a
  misconfigured account does not read as "there is nothing here".
- A kiosk whose overlay references a variable absent from its fab: renders the
  literal placeholder, exactly as it does for a name that exists nowhere.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every system variable MUST belong to exactly one fab.
- **FR-002**: A variable name MUST be unique within a fab, and MUST be usable
  in another fab at the same time.
- **FR-003**: Archiving a variable MUST release its name for reuse within its
  own fab, and MUST NOT affect any other fab.
- **FR-004**: A value-change request MUST apply only to the variable of that
  name in the fab the request came from.
- **FR-005**: A value-change request naming a variable that does not exist in
  its own fab MUST change nothing, and MUST be recorded with the fab and the
  variable name.
- **FR-006**: A value-change request carrying no fab MUST change nothing, and
  MUST be recorded.
- **FR-007**: Duplicate delivery of the same request MUST be suppressed within
  a fab, and MUST NOT suppress a distinct request in another fab that shares a
  variable name.
- **FR-008**: Reads MUST return only variables in fabs the caller is assigned
  to.
- **FR-009**: A variable in a fab the caller does not hold MUST be reported as
  not found, in a response indistinguishable from a name that was never used.
- **FR-010**: Defining a variable MUST place it in the caller's fab when they
  are assigned to exactly one, without requiring them to name it.
- **FR-011**: Defining a variable MUST be refused when the caller is assigned
  to several fabs and names none. Nothing is created.
- **FR-012**: Naming a fab the caller is not assigned to MUST be refused, on
  every operation.
- **FR-013**: An operator assigned to no fab MUST be refused rather than shown
  an empty result.
- **FR-014**: An overlay MUST resolve variables only from its own fab, on first
  render and on live updates.
- **FR-015**: A live update to a variable MUST reach only screens in that
  variable's fab.
- **FR-016**: Variables existing before this feature MUST be assigned to the
  single fab that was live, MUST end with a fab set, and the number so assigned
  MUST be stated where an operator applying the change will see it.

### Key Entities

- **System variable**: a named, typed value belonging to one fab. Its name is
  unique among the non-archived variables of that fab.
- **Fab**: the site a variable belongs to. Already how rules, events and audit
  entries are divided.
- **Value-change request**: an instruction to set a variable, carrying the fab
  it originated in and the event that caused it. Already carries the fab today.
- **Overlay reference**: a link from an overlay to a variable name, resolved to
  a value for display. Must resolve within the overlay's own fab.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Two fabs holding a variable of the same name can each be driven
  independently, and neither reads back the other's value — 100% of attempts.
- **SC-002**: An operator assigned to one fab can reach no variable belonging
  to another, by listing or by naming it directly — zero reachable.
- **SC-003**: Every variable has a fab after the change, including every one
  that existed before it — no exceptions.
- **SC-004**: A kiosk displays only its own fab's value for a shared variable
  name, on first render and after a change in either fab — 100% of attempts.
- **SC-005**: The time from a value change to the screen showing it stays
  within the existing event-to-overlay budget, measured the same way as before
  the change, with no measurable regression.
- **SC-006**: A value-change request that cannot be applied leaves a record
  naming the fab and the variable — 100% of cases, none dropped in silence.
- **SC-007**: An operator defining a variable is asked which fab only when they
  are assigned to more than one.

## Assumptions

- **One live fab today.** Munich is the only fab in service, so every existing
  variable belongs to it. This is what makes FR-016 answerable at all. It is
  recorded as an assumption because it stops being true the moment a second fab
  goes live, and anyone reading this later needs to know it was true when the
  change was made.
- **The fab is already on the wire.** Value-change requests already carry the
  originating fab. This feature gives the receiving side somewhere to put it;
  it does not add a new piece of information to the system.
- **Cross-fab references are a misconfiguration, not a use case.** A rule in
  one fab naming a variable in another is treated as an error to surface, not a
  capability to support. Spec 013 left this open by design; this closes it.
- **Refusal happens where the value is applied**, not where the rule is
  authored. Checking at authoring time would require one bounded context to
  call another synchronously, which the constitution forbids. The consequence
  is that a misconfigured rule is discovered when it first fires rather than
  when it is written — which is why FR-005 requires the drop to be recorded.
- **Consistent with how rules already ask for a fab.** An operator meets one
  rule across the product, not one per screen. This extends a decision that was
  deliberately scoped narrowly when it was made, so that decision record needs
  amending as part of this work.

## Out of Scope

- Giving the remaining unguarded contexts a fab. This closes the system
  variables slice; the wider programme is tracked separately.
- Changing how rules are authored, or how a rule names the variable it targets.
- Any mechanism for deliberately sharing a variable between fabs. If that turns
  out to be wanted, it is a new capability with its own decision, not a
  loosening of this one.
- Moving an existing variable from one fab to another.

## Dependencies

- Rules already carry a fab (spec 013) and stamp it on the requests they emit.
- Callers' fab assignments are already available from their sign-in.
- The existing event-to-overlay latency measurement, which SC-005 compares
  against.
