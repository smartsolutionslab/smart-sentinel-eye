# Feature Specification: Automation rules belong to a fab

**Feature Branch**: `013-automation-fab-scoping`

**Created**: 2026-08-03

**Status**: Draft

**Input**: User description: "Fab-scope Automation rules. Today the Rule aggregate has no Fab property at all, although RuleName documents the intent that (fabId, name) be unique. Two consequences: an event ingested from one fab fires rules authored for another (bug #1252), and no fab guard can be applied to the rule endpoints because there is no fab to check (the open half of #843, part of #1155)."

## Context

Every other operator-facing area of the system is scoped to a fab. A fab is a
physical production plant; plants do not share cameras, layouts, variables or
staff, and an operator's access is granted per plant.

Automation is the exception, and not by design. Rules were built without any
notion of which plant they belong to. The naming rules already assume
otherwise — the rule-name documentation states that a name is unique *per
fab* — but nothing carries the fab, so in practice a rule name is unique
across the entire installation and every rule is visible and active
everywhere.

This has two distinct effects, and they need separating because only one of
them involves a person.

**Rules fire for plants they were not written for.** When a machine event
arrives from Munich, every active rule in the installation is considered,
including rules an operator wrote for Dresden. If a Dresden rule's condition
happens to match, its action runs and the resulting change is recorded as
though it came from Munich. Nobody is involved and nothing in the audit trail
shows a cross-plant origin. This is the more serious of the two: it corrupts
data during normal unattended operation, and it gets worse with each plant
added.

**Operators can see and change other plants' rules.** Nothing filters rules by
plant and nothing checks which plants the requester is assigned to, so an
operator assigned only to Dresden can list, edit, publish and archive
Munich's rules.

Both stem from the same gap, which is why they are fixed together.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A rule only acts on its own plant (Priority: P1)

An automation author writes a rule for their plant. Machine events from other
plants must never trigger it, and it must never change values attributed to
another plant.

**Why this priority**: This is the data-integrity defect. It happens without
any operator action, during ordinary 24/7 running, and it silently attributes
one plant's automation decisions to another. Every other story in this feature
is about who may *see* or *edit* a rule; this one is about the system
producing wrong values on its own. Shipping only this story already removes
the harm.

**Independent Test**: Author one rule in each of two plants that react to the
same kind of machine event with different outcomes. Send an event from the
first plant. Only the first plant's rule takes effect, and the second plant's
target value is untouched.

**Acceptance Scenarios**:

1. **Given** an active rule in Plant A and an active rule in Plant B that both
   react to the same event type, **When** an event arrives from Plant A,
   **Then** only Plant A's rule acts and Plant B's outcome is unchanged.
2. **Given** an active rule in Plant B only, **When** an event arrives from
   Plant A, **Then** no rule acts and no value changes anywhere.
3. **Given** a rule that acted on an event from Plant A, **When** the
   resulting change is recorded, **Then** it is attributed to Plant A.
4. **Given** rules exist in several plants, **When** an event arrives,
   **Then** the time taken to decide which rules apply does not grow with the
   number of other plants' rules.

---

### User Story 2 - An operator only works with their own plant's rules (Priority: P2)

An operator assigned to one plant opens the rules screen and sees that
plant's rules. Another plant's rules are neither listed nor reachable, and
attempts to act on them are refused.

**Why this priority**: This is the access gap. It requires a person to act,
and in the current single-plant deployment the exposure is limited — but it
must be closed before a second plant goes live, and it is the half that
#843 and #1155 track.

**Independent Test**: Sign in as an operator assigned to one plant. The rules
list shows only that plant's rules, and a direct attempt to open or change a
rule belonging to another plant is refused.

**Acceptance Scenarios**:

1. **Given** rules exist in two plants, **When** an operator assigned to one
   plant lists rules, **Then** only their plant's rules are returned.
2. **Given** a rule in another plant, **When** the operator requests it by
   name, **Then** the request is refused and the response does not reveal
   whether the rule exists.
3. **Given** a rule in another plant, **When** the operator attempts to
   publish or archive it, **Then** the attempt is refused and the rule is
   unchanged.
4. **Given** a rule in another plant, **When** the operator attempts a
   trial run against it, **Then** the attempt is refused.

---

### User Story 3 - Authoring picks up the operator's plant (Priority: P2)

An operator assigned to exactly one plant authors a rule without stating
which plant it is for; the system uses theirs. An operator assigned to
several must say which plant they mean.

**Why this priority**: Same priority as Story 2 because the two are the same
change to the authoring path, but listed separately because the multi-plant
case has its own behaviour and its own failure mode.

**Independent Test**: Author a rule as a single-plant operator and confirm it
lands in their plant. Repeat as a multi-plant operator without naming a
plant, and confirm the attempt is refused with an explanation.

**Acceptance Scenarios**:

1. **Given** an operator assigned to exactly one plant, **When** they author a
   rule without naming a plant, **Then** the rule belongs to their plant.
2. **Given** an operator assigned to several plants, **When** they author a
   rule without naming a plant, **Then** the attempt is refused and the
   message states that a plant must be chosen.
3. **Given** an operator assigned to several plants, **When** they author a
   rule naming one of their plants, **Then** the rule belongs to that plant.
4. **Given** any operator, **When** they name a plant they are not assigned
   to, **Then** the attempt is refused.

---

### User Story 4 - The same rule name can exist in different plants (Priority: P3)

Two plants can each have a rule called `high-oee` without collision, and
within one plant the name stays unique.

**Why this priority**: A consequence of the model change rather than a goal
of it, but it is the behaviour operators will notice first, and it is
currently wrong — the first plant to use a name takes it globally.

**Independent Test**: Author a rule with the same name in two plants; both
succeed. Author the same name twice in one plant; the second is refused.

**Acceptance Scenarios**:

1. **Given** a rule named `high-oee` in Plant A, **When** an operator authors
   `high-oee` in Plant B, **Then** it is accepted.
2. **Given** a rule named `high-oee` in Plant A, **When** an operator authors
   `high-oee` in Plant A again, **Then** it is refused as a duplicate.

---

### Edge Cases

- **Rules that already exist** carry no plant. They are assigned to a single
  named plant when the change is applied (see Assumptions), because leaving
  them unassigned would keep the cross-plant defect alive for exactly the
  rules that are already running.
- **An operator assigned to no plant** cannot author, list or act on any
  rule. Their requests are refused rather than returning an empty list, so
  the situation is visible rather than looking like "no rules yet".
- **An operator's plant assignment changes** while they have a rule screen
  open. The next request is judged against their current assignment, not the
  one held when the screen loaded.
- **A rule referring to something in another plant** — for example a target
  value that belongs elsewhere — is out of scope here. This feature scopes
  the rule itself; it does not validate what a rule's action points at.
- **An event arriving without a plant** must not cause any rule to act.
- **A trial run** must be judged by the same plant rules as a real change,
  so a trial cannot be used to discover another plant's rule behaviour.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every rule MUST belong to exactly one plant, recorded when the
  rule is authored and unchangeable thereafter.
- **FR-002**: The system MUST consider only the originating plant's rules
  when deciding what a machine event triggers.
- **FR-003**: A change produced by a rule MUST be attributed to the plant the
  rule belongs to.
- **FR-004**: Rule names MUST be unique within a plant, and MAY repeat across
  plants.
- **FR-005**: Listing rules MUST return only rules belonging to a plant the
  requester is assigned to.
- **FR-006**: Requesting, publishing, archiving or trial-running a rule
  belonging to a plant the requester is not assigned to MUST be refused.
- **FR-007**: A refusal under FR-006 MUST NOT reveal whether the named rule
  exists.
- **FR-008**: When an operator assigned to exactly one plant authors a rule
  without naming a plant, the system MUST use theirs.
- **FR-009**: When an operator assigned to more than one plant authors a rule
  without naming a plant, the system MUST refuse and state that a plant must
  be chosen.
- **FR-010**: Naming a plant the requester is not assigned to MUST be
  refused, whether authoring or acting on a rule.
- **FR-011**: Rules that exist before this change MUST be assigned to a
  single named plant as part of applying it, with no rule left unassigned.
- **FR-012**: An event that does not identify a plant MUST NOT trigger any
  rule.
- **FR-013**: The deviation in FR-008 — inferring the plant rather than
  requiring it on every request — MUST be recorded as a decision, because it
  conflicts with the documented rule that there is no implicit "current
  plant" and the caller states it per request.

### Key Entities

- **Rule**: An automation rule. Gains a permanent association with exactly
  one plant. Its name is unique within that plant rather than globally.
- **Plant (fab)**: An existing concept elsewhere in the system, reused here.
  An operator is assigned to zero, one or several.
- **Machine event**: Already identifies its originating plant; that
  identification now determines which rules are considered.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An event from one plant never causes a rule from another plant
  to act — verified with active rules in two plants reacting to the same
  event type.
- **SC-002**: 100% of rule changes are attributed to the plant whose rule
  produced them.
- **SC-003**: An operator assigned to one plant sees zero rules belonging to
  any other plant, in listings and in direct requests alike.
- **SC-004**: Every attempt to act on another plant's rule is refused, with
  no response distinguishing "not yours" from "does not exist".
- **SC-005**: No rule exists without a plant once the change is applied.
- **SC-006**: The same rule name can be used once in each plant.
- **SC-007**: The time to decide which rules an event triggers does not
  increase as rules are added in other plants.
- **SC-008**: Authoring effort is unchanged for a single-plant operator — no
  additional input is required of them.

## Assumptions

- **Existing rules belong to one plant.** They are assigned to `munich` when
  this change is applied. This was chosen deliberately over archiving them:
  archiving would stop live automation, and leaving them unassigned would
  preserve the defect for the rules most likely to be running.
- **Operators' plant assignments already exist** and are the authority for
  who may see what. This feature reads them; it does not manage them.
- **Machine events already identify their plant.** No change is needed on the
  ingest side.
- **A rule's plant is fixed once authored.** Moving a rule between plants is
  out of scope; an operator re-authors it.
- **What a rule's action points at is not validated** against the rule's
  plant here. That is a larger consistency question spanning variables and
  overlays, and is left to a separate feature.
- **The frontend already knows the operator's plant assignments** from
  sign-in, so no new plant-selection concept is introduced beyond the
  multi-plant authoring case.
