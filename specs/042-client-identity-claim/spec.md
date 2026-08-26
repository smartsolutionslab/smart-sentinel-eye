# Feature Specification: Every identity can say who it is

**Feature Branch**: `042-client-identity-claim`

**Created**: 2026-08-26

**Status**: Draft

**Issue**: 1885 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: Six of the eight identities in the development directory issue
credentials that do not say who holds them. Anything they do therefore cannot be
attributed to anyone, and the system — correctly — refuses to act rather than
guess.

---

## Why this exists

The system will not record an action against someone it cannot name. That is a
deliberate safety property, written down where it is enforced: attributing a
change to a fabricated person *"would corrupt the audit trail"*, so an
unattributable request is refused outright.

The property is right. The problem is that **most identities in the development
directory cannot be named**, so the safety net fires as a fault. Six of eight
issue credentials carrying nothing that identifies the holder. **Seventeen
places** in the product refuse a request on exactly that basis, and a second
place refuses to show video for it.

### Two of the eight work, and both by accident

One inherits the identifying piece from a broad administrative permission it
happens to hold. The other carries a hand-added copy, put there last week as a
deliberately narrow fix for a single screen that could not show a picture.

Neither is the result of anyone deciding that identities should be
identifiable. **Nothing in the configuration expresses that idea at all.**

### The configuration says something untrue about itself

Every one of the eight identities begins its permission list with four names
that **do not exist** in this directory. The sign-in service discards them and
says so — one warning per name per identity, thirty-two on every start — and the
file keeps listing them. A reader sees four things applied that are not.

That is what hid this. The identifying piece would normally arrive with one of
those four.

### It is already waiting to happen again

The replacement identity for the operator console — created for that purpose,
described in the configuration as replacing the one in use, still unused — is in
the group that cannot be named. Adopting it would refuse **every** operator
change, on a screen whose whole job is making changes.

That is the same trap, one identity over, as the one the kiosk fell into. It was
left documented rather than hidden precisely so this could be fixed on purpose.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — An operator's change is recorded against them (Priority: P1)

An operator makes a change. The system knows who made it and records that.

**Why this priority**: It is the defect. Everything else here either stops it
recurring or stops the configuration lying about it.

**Independent Test**: Sign in, change something, see it attributed to the person
who changed it.

**Acceptance Scenarios**:

1. **Given** an operator signed in with any identity the product offers them,
   **When** they change something,
   **Then** the change succeeds and is recorded against them.
2. **Given** every identity in the directory,
   **When** each issues a credential,
   **Then** **each one** says who holds it — checked one by one, not sampled.
   Two work today by coincidence, and a sample would probably have found one.
3. **Given** the replacement identity for the operator console,
   **When** the console is pointed at it,
   **Then** changes are attributed rather than refused. *(Pointing it there is
   not part of this feature; being able to is.)*

---

### User Story 2 — The configuration describes what actually happens (Priority: P1)

Reading the configuration tells you what the system does. Nothing in it is
discarded on the way in.

**Why this priority**: Equal to US1. The lie is what hid the defect, and it will
hide the next one. Fixing the identities while leaving four fictional entries on
each of them would repair the instance and keep the mechanism.

**Independent Test**: Start the system and read the log. Nothing is being
ignored.

**Acceptance Scenarios**:

1. **Given** the system starts,
   **When** the directory is loaded,
   **Then** **nothing** is reported as named-but-missing.
2. **Given** the configuration,
   **When** an identity's permissions are read,
   **Then** every one of them exists.

---

### User Story 3 — The next identity cannot be quietly unnameable (Priority: P1)

Adding an identity that cannot say who it is fails a check.

**Why this priority**: Equal to the others. This failure is invisible three
times over — the warning at startup goes unread, signing in works perfectly, and
the fault only appears at the first change or the first video frame. Nothing
would catch it.

**Independent Test**: Add an identity that cannot name itself. The check goes
red.

**Acceptance Scenarios**:

1. **Given** the identities as they should be,
   **When** the check runs,
   **Then** it passes.
2. **Given** an identity added without the means to name itself,
   **When** the check runs,
   **Then** it **fails** — demonstrated by causing it, not by assuming it.
3. **Given** an identity naming a permission the directory does not define,
   **When** the check runs,
   **Then** it **fails**, rather than being ignored at startup as it is today.

---

### User Story 4 — One notion of identity, not several (Priority: P2)

There is a single place that makes an identity nameable, and every identity uses
it.

**Why this priority**: P2 because the system works either way. But the two that
work today do so by two different accidents, and a third mechanism would be a
third thing to keep in step. Two copies of one idea is how they drift apart.

**Independent Test**: Exactly one mechanism supplies it, and nothing carries a
private copy.

**Acceptance Scenarios**:

1. **Given** the change is complete,
   **When** the configuration is read,
   **Then** one shared definition makes identities nameable and no identity
   carries its own duplicate of it.

---

### Edge Cases

- **Identities that act for no person** — the background workers. They attribute
  nothing today, so they arguably do not need this. They get it anyway: *which*
  identities need naming is a judgement that would have to be made again every
  time one is added, and getting it wrong is silent. A rule with no exceptions is
  the one that can be checked.
- **The identity carrying a hand-added copy.** Folded into the shared
  definition, or it becomes the second source of the same fact.
- **The identity that inherits it from a broad administrative permission.** It
  keeps working, but no longer *because* of that permission — otherwise
  narrowing that permission later would silently un-name it.
- **An operator who signs in successfully and then cannot do anything.** Today's
  symptom, and the reason this is hard to spot: signing in proves nothing about
  whether the credential can be attributed.
- **Adding a permission name with a typo.** Currently ignored with a warning.
  Should fail.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every identity in the directory MUST issue credentials that say
  who holds them.
- **FR-002**: An operator's change MUST be recorded against the operator who
  made it, for every identity the product offers a person.
- **FR-003**: The means of naming an identity MUST be defined **once** and
  shared, not repeated per identity.
- **FR-004**: No identity may name a permission the directory does not define.
- **FR-005**: Loading the directory MUST report nothing as named-but-missing.
- **FR-006**: Something MUST detect an identity that cannot name itself, and
  fail. The failure is silent at startup, silent at sign-in, and surfaces only at
  the first change.
- **FR-007**: Something MUST detect an identity naming a permission that does not
  exist, and fail rather than ignore it.
- **FR-008**: The identity that currently carries a private copy MUST use the
  shared definition instead.
- **FR-009**: Nothing beyond the identifier may be added. A display name is read
  by nothing in the product today, and adding one would be inventing a need.
- **FR-010**: Nothing about what any identity is *permitted to do* may change.
  This makes identities nameable; it does not make them more or less capable.

### Key Entities

- **Identity**: what a person or a background worker signs in as. Determines
  what may be done and, when it works, who is doing it.
- **Naming piece**: the part of a credential that says who holds it. Absent from
  six of eight identities, which is the whole defect.
- **Permission list**: what each identity is allowed to do. Currently begins,
  on every identity, with four entries that do not exist.
- **Attribution**: recording an action against the person who took it. Refused
  outright when the holder cannot be named — deliberately, so the record is never
  wrong.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: **Zero** identities issue a credential that cannot be attributed —
  counted across all of them, not sampled.
- **SC-002**: **Zero** warnings on startup naming something that does not exist,
  down from thirty-two.
- **SC-003**: An identity that cannot name itself **fails** a check —
  demonstrated by causing it.
- **SC-004**: An identity naming a permission that does not exist **fails** a
  check — demonstrated by causing it.
- **SC-005**: An operator's change is attributed to that operator, observed end
  to end rather than inferred from configuration.
- **SC-006**: **One** shared definition makes identities nameable, and **zero**
  identities carry a private copy.
- **SC-007**: What every identity is permitted to do is unchanged.

---

## Assumptions

- **Only the identifier is needed.** The product reads nothing else about who
  the holder is: a display name appears once, as an unused setting. Restoring the
  other three discarded groups of information would be building for needs that do
  not exist.
- **Every identity gets it, including the background workers.** They attribute
  nothing today. Uniformity is chosen over precision because the alternative is a
  per-identity judgement made repeatedly, whose errors are invisible.
- **The refusal behaviour is correct and stays.** Refusing an unattributable
  change is a deliberate safety property, and this feature removes the reason it
  fires — it does not soften it.
- **No production deployment exists**, so changing every identity in the
  directory coordinates with nothing. The directory is rebuilt from this
  configuration on a developer's machine.
- **The narrow fix that came before was right to be narrow.** It made one screen
  work without touching seven other identities. This is the general version, done
  deliberately.

---

## Out of Scope

- **Pointing the operator console at its replacement identity.** A separate
  decision and a separate change. This makes it possible; it does not do it.
- **What any identity is permitted to do.** Naming is not permission.
- **The operator console's missing video surface**, filed separately.
- **The overlay-timing measurement**, filed separately.
- **Anything the kiosk does.**
- **Any production rollout.** There is no production deployment.
