# Feature Specification: A wall stays up past its own session ceiling

**Feature Branch**: `052-wall-past-its-ceiling`

**Created**: 2026-08-31

**Status**: Draft

**Input**: Issues 1989 and 1992. Constitution §Availability — a wall of 20 kiosks must come up unattended.

---

## Where this sits

Spec 051 removed the **identity-outage** half: a wall now survives the sign-in
service going away and returns in about 34 seconds with nobody touching it.

This is the other half, and it is **the more frequent failure**. The sign-in
session ends at a fixed ceiling regardless of activity, so every screen drops to
a prompt **roughly twice a day** whether or not anything went wrong. A fab that
runs 24/7 walks the floor to press buttons on a schedule.

**Spec 050 attempted exactly this and was withdrawn before merge.** It is treated
here as a completed experiment rather than a failed idea: its mechanism works,
its arrangement did not, and the reason is known.

---

## The thing that makes this hard, stated first

The privilege that lets a grant outlive a session is a **role**. Spec 050 gave
that role to four wall-display accounts and called the widening contained.

**It was not.** The sign-in provider composes a default role that every account
created *after* the realm is loaded inherits — including the service account of
**every kiosk the system enrols at runtime**. So "only wall displays may mint a
credential that never expires" is true of a configuration file and false of any
running system. That is the precise claim spec 049 refused this feature over.

**Today the whole thing is inert**: the four accounts hold the role, no
application offers it, and nothing can use it. The widening is **clerical, not
real** — and this feature is the one that would make it real.

### And the fix costs authority

Narrowing that default role cannot be done in the configuration file — the
provider discards the attempt on load. It can be done through the administrative
interface, and **only by a principal holding realm-management authority**.
Measured, not assumed:

| Authority granted to a test principal | Can it narrow the default role? |
|---|---|
| none | **no** |
| everything the identity service holds today (manage users, manage clients, …) | **no** |
| view-realm | **no** |
| **manage-realm** | **yes** |

So the obvious fix means granting something that runs at startup the power to
reshape the sign-in realm — session lifetimes, roles, flows, all of it. **A
control that requires more authority than the thing it controls deserves a
decision, not a shrug.**

**The decision is taken: the narrower containment.** Rather than narrowing the
default privilege set for everyone, the privilege is removed from each account
**as this system creates it** — which needs only authority the identity service
already holds. Measured against the real service account, with no new permission
granted: enrolling a kiosk leaves it holding the privilege; removing its single
direct privilege grant is **allowed**, leaves it holding **nothing**, leaves the
kiosk **still able to obtain a token**, and is **idempotent**. Nothing in the
system authorises by that grant — access is decided by scope and fab membership —
so removing it costs the kiosk nothing it was using.

What this does **not** cover is an account created by hand in the provider's own
console. That is the price of not taking the broader authority, it is stated
here rather than discovered later, and FR-002a requires it to be filed.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Only a wall display may hold the privilege, in the running system (Priority: P1)

Someone auditing the fab asks: *who can mint a credential that never expires?*
The answer must be "the four wall-display accounts", and it must be true of the
system as it is running — not of a file that describes it.

Today the honest answer is "those four, **plus every kiosk ever enrolled**, and
anyone else the system creates from now on".

**Why this priority**: This is the gate. The feature widens who may hold a
long-lived credential, and the widening is acceptable **only if it is real**.
Spec 049 refused this feature on precisely this point and was right to. Building
US2 first would ship the cost without the containment, which is what spec 050
did.

**Independent Test**: Enrol a kiosk, then ask the running provider what that
kiosk's account effectively holds. It must not include the privilege. Ask the
same of an operator, and of a wall display — the first two must not hold it, the
third must. **Asked of the provider, not read from the file**; the file has been
right while the system was wrong for the whole life of this problem.

**Acceptance Scenarios**:

1. **Given** a kiosk enrolled after the system started, **When** its account's
   effective privileges are read from the running provider, **Then** the
   long-lived-credential privilege is absent.
2. **Given** an operator account, **Then** the same, and an attempt to mint such
   a credential is refused.
3. **Given** a wall-display account, **Then** the privilege is present and a
   long-lived credential can be minted.
4. **Given** the containment is in place, **When** the authority used to apply it
   is examined, **Then** it is recorded, justified, and no broader than the task
   requires.

---

### User Story 2 - The wall is still showing cameras at the end of a double shift (Priority: P1)

A wall is switched on Monday morning and left alone. Sixteen hours later it is
still showing cameras. Nobody has been asked for a password, and nobody has
walked to it.

Today it drops to a prompt roughly twice in that period.

**Why this priority**: It is the feature. P1 alongside US1 rather than after it
because it delivers the value — but **US1 gates it**, and shipping this without
US1 repeats spec 050 exactly.

**Independent Test**: Bring a screen up, leave it past the ceiling, and confirm
it is still showing its wall with no interaction. Demonstrable on **one** screen;
the twenty-screen claim is US4 and is not made here.

**Acceptance Scenarios**:

1. **Given** a screen showing a wall, **When** more time passes than the sign-in
   session allows, **Then** the screen is still showing its wall.
2. **Given** the same screen, **When** it is restarted after an outage longer
   than the session, **Then** it comes back without a person.
3. **Given** an operator using the management console, **Then** nothing about
   their session has changed.

---

### User Story 3 - A wall display can do no more than show a wall (Priority: P1)

A screen is stolen from a fab. What the thief holds must be worth as little as
possible, and what it is worth must be **written down accurately**.

**Why this priority**: P1 because the grant this feature creates is the
longest-lived credential in the system, and spec 050 recorded this wrong twice
in opposite directions. It is cheap to check and expensive to assume.

**Independent Test**: Attempt **every** authority the wall-display grant carries,
not a chosen three. Spec 050's test asserted refusals on three endpoints and
never attempted the one the account actually held.

**Acceptance Scenarios**:

1. **Given** a wall-display grant, **Then** every write it can attempt is either
   refused or **explicitly recorded as permitted**, with nothing untested.
2. **Given** the same grant, **Then** it cannot read another fab.
3. **Given** the record of what this costs, **Then** it states the exposure in
   both directions — what a stolen screen yields now, and what bounds it.

---

### User Story 4 - Twenty screens, and a real power cut (Priority: P3, and deliberately not claimed)

The constitution's target is twenty screens coming back from a reboot
unattended. **This story is named so that it is not quietly absorbed by the
others.** Twenty screens have never been exercised — four were, once. A real
power cut has never been tested at all.

**Why this priority**: P3 because it is not achievable in this feature's
environment, and stating it as a story is the only way to stop a green run on one
screen reading as twenty. **It is expected to remain unmet**, and the record must
say so.

**Independent Test**: There isn't one available here. That is the finding.

---

### Edge Cases

- **An account created before the containment is applied.** Narrowing the default
  role applies retroactively, because effective privileges are resolved when they
  are used rather than copied at creation. This must be confirmed, not assumed.
- **A screen switched off for more than a month.** A long-lived credential that
  goes unused is eventually removed by the provider, so a screen off for long
  enough needs a person after all. That bounds the theft **and** weakens the
  availability claim; both must be stated.
- **A stolen screen that is switched on.** The unused-credential clock does not
  run. This is the exposure.
- **The provider's own defaults change.** Every timing figure in this problem —
  the ceiling, the idle cut-off, the removal window — is a provider default this
  repository does not set. An upgrade can move all of them with every test green.
- **Someone creates an account by hand** in the provider's console. **Not
  covered**, and deliberately so: covering it means narrowing the provider's
  default privilege set, which requires realm-management authority — broader than
  the privilege it would contain, and held by nothing today. Filed rather than
  built (FR-002a).
- **The containment step runs twice**, or runs against a realm already narrowed.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: In the **running** system, the long-lived-credential privilege MUST
  be held only by wall-display accounts.
- **FR-002**: An account **the system creates** — every kiosk enrolled at
  runtime — MUST NOT retain that privilege.
- **FR-002a**: **The residual gap MUST be filed and recorded, not absorbed.**
  Containing the privilege per-account covers accounts this system creates and
  **does not** cover an account created by hand in the provider's own console.
  Closing that too would mean narrowing the provider's default privilege set,
  which needs realm-management authority nobody holds and which is broader than
  the privilege it would contain — so it is a separate decision with its own
  cost, and the record must say plainly which accounts are covered and which are
  not. *Chosen deliberately: the narrower containment, needing no new authority,
  over the total one that needs a great deal.*
- **FR-003**: FR-001 and FR-002 MUST be verified by asking the running provider,
  **not** by reading configuration.
- **FR-004**: The authority required to apply the containment MUST be recorded
  and MUST be the narrowest that achieves it. If it is broader than the privilege
  being contained, that MUST be stated as a cost and justified.
- **FR-005**: A wall display MUST keep showing its wall past the point where the
  sign-in session ends, without interaction.
- **FR-006**: A wall display MUST recover unattended from a restart that outlasts
  its sign-in session.
- **FR-007**: Operators MUST gain nothing. No account other than a wall display
  may gain any authority, and this MUST be checked directly rather than argued
  from "we did not touch it".
- **FR-008**: The application MUST NOT be prevented from signing in any account
  by this change. *An arrangement that locks out every account without the
  privilege is what made the previous attempt unshippable.*
- **FR-009**: Every authority a wall-display grant carries MUST be enumerated and
  tested — permitted ones as well as refused ones.
- **FR-010**: A wall display MUST NOT be able to read another fab.
- **FR-011**: Ending one screen's credential MUST stop that screen and leave
  others running.
- **FR-012**: The record MUST state the exposure in both directions: what a
  stolen screen yields, and what bounds it.
- **FR-013**: The record MUST NOT claim the constitution's availability target is
  met. Twenty screens and a real power cut are out of reach here.
- **FR-014**: Configuration that reads as a control but is not one MUST be
  removed or corrected rather than left in place.
- **FR-015**: The containment MUST be safe to apply more than once.

### Key Entities

- **Wall-display account**: one per fab, holds the privilege, belongs to exactly
  one fab, and does nothing else.
- **The default privilege set**: what the provider gives an account created after
  startup. The subject of US1.
- **Containment step**: whatever applies FR-002, wherever it lives, and the
  authority it needs.
- **Long-lived grant**: what a wall display holds; its exposure and its bound.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A wall left alone for longer than a full sign-in session is still
  showing cameras, with **zero** interactions.
- **SC-002**: Across a working day, the number of times a person must sign a
  screen in falls from **about two per screen to zero**.
- **SC-003**: A kiosk enrolled at runtime holds **no** long-lived-credential
  privilege, confirmed against the running provider. *An account created by hand
  in the provider's console still does — recorded, not hidden.*
- **SC-004**: Among the accounts this system creates or declares, the count able
  to mint a never-expiring credential equals the number of fabs, confirmed
  against the running provider — **not** the configuration.
- **SC-005**: Every account that could sign in before can still sign in.
- **SC-006**: Every authority a wall-display grant carries is enumerated, and
  none is untested.
- **SC-007**: The authority used to apply the containment is recorded, with the
  narrowest sufficient option chosen.
- **SC-008**: **Twenty screens and a real power cut remain unmeasured**, and the
  record says so in the same place it reports the successes.

---

## Scope

### In scope

Containing the privilege in the running system; letting a wall display outlive
its sign-in session; establishing what such a screen may do.

### Out of scope, and filed rather than implied

- **Per-device identity** — issues 1987 and 1988. Device-bound credentials cannot
  live in a browser, and a device runtime is a subsystem.
- **Enrolment, rotation and revocation as operator workflows.** A credential that
  never expires and that nobody rotates is a real liability; it is named in the
  record and not built here.
- **Setting the provider's timing defaults.** They are unset today, and pinning
  them is a separate decision with its own blast radius.
- **The identity-outage half** — spec 051, done.
- **`management-web`.**

---

## Assumptions

- The wall is unattended, has no keyboard, and runs continuously.
- One wall-display account per fab, because fab scoping comes from the account:
  a shared account would let any screen see every fab.
- A person commissions a screen once. Enrolment as an operator workflow is out of
  scope.
- The provider's timing figures are defaults this repository does not set; every
  claim resting on them is a claim about the current configuration.
- The four wall-display accounts already declared are inert and remain so until
  this feature makes them usable.
- Development and CI are the only environments. `deploy/` provisions no realm and
  there is no production deployment (ADR-0130), so whoever builds one must carry
  both the accounts **and** the containment — having one without the other is
  worse than having neither.
