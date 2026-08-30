# Feature Specification: A wall that stays up

**Feature Branch**: `050-a-wall-that-stays-up`
**Created**: 2026-08-30
**Status**: Draft
**Input**: Issue 1989, deferred from spec 049. The decision to use a dedicated kiosk account is recorded on that issue.

---

## What happens today

A wall that nobody touches drops to a sign-in prompt roughly **twice a day, per
screen** — and after any outage longer than half an hour.

Spec 049 made a screen come back from a restart. It did so within a limit it
recorded honestly: recovery lasts only as long as the session behind the stored
grant, which idles out after **thirty minutes** and ends at **ten hours**
regardless of how busy the screen is. Both figures were read off the running
system.

So a fab with twenty screens still needs someone walking the floor with a
password — twice a day in normal running, and after every real power cut.
Constitution §Availability's target remains unmet, and says so.

---

## Why this is the second attempt, and what changed

Spec 049 tried to fix this and stopped, because escaping the limit needs a
long-lived grant, and the identity provider only issues one to an account
holding a particular privilege. Granting that privilege to the operator account
would let **every operator** mint long-lived credentials — authority far wider
than a wall display needs, and the thing spec 049 refused to buy recovery with.

**The decision since taken**: give that privilege to a **dedicated account that
only wall displays use**. Operators gain nothing. The widening is real but
confined to an account whose entire purpose is showing cameras on a wall.

This spec builds that. It is not a re-litigation of spec 049's refusal — the
refusal was right, and this is the narrower path it left open.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A wall runs for days without asking anyone for anything (Priority: P1)

A screen shows its cameras continuously — past ten hours, past a day, past a
week — and never displays a sign-in prompt.

**Why this priority**: It is the failure, and it happens on a schedule. Nobody
touches a wall display, so a screen going dark twice a day is not an edge case;
it is Tuesday.

**Independent Test**: Run a screen past the session limit and confirm it is
still showing cameras. **Anything shorter than the limit passes with the defect
fully present**, so the test must either wait it out or shorten the limit
deliberately and say which it did.

**Acceptance Scenarios**

1. **Given** a screen showing a wall, **When** more than ten hours pass, **Then**
   it is still showing that wall and no prompt has appeared.
2. **Given** a screen nobody interacts with for hours, **When** the idle limit
   passes, **Then** it keeps showing cameras — idleness is a wall display's
   normal state, not evidence it is unused.
3. **Given** a screen that loses power for two hours, **When** it restarts,
   **Then** it comes back on its own. *(Spec 049 covers a short restart; this is
   the outage that outlasts the session.)*

---

### User Story 2 - A wall display holds no more authority than it needs (Priority: P1)

The account a screen uses can see cameras in its own fab and do nothing else.

**Why this priority**: Equal first, and it is the reason this was deferred once
already. The whole feature is "let this credential live longer", so what the
credential *can do* is not a detail — it is the trade. A recovery story bought
with a broad grant would be a worse outcome than the twice-daily prompt.

**Independent Test**: Take the credential a screen uses and attempt something
outside its fab, and something beyond viewing. Both must fail. **Testing only
that the wall works proves nothing about what else the account could do.**

**Acceptance Scenarios**

1. **Given** the account a screen signs in with, **When** it attempts to change
   anything, **Then** it is refused.
2. **Given** that account, **When** it attempts to see another fab's cameras,
   **Then** it is refused.
3. **Given** an operator's account, **When** it is examined after this change,
   **Then** it holds exactly what it held before — the widening reaches wall
   displays and nobody else.

---

### User Story 3 - Withdrawing one screen does not take down the wall (Priority: P2)

A screen that should no longer show cameras can be stopped, and the others keep
running.

**Why this priority**: The credential now outlives sessions, so being able to
end it matters more than it did. Ranked below the first two because the wall
working and the wall being safe both come first — but a long-lived credential
with no way to withdraw it is the standing liability this repo already objects
to elsewhere.

**Acceptance Scenarios**

1. **Given** a screen whose access is withdrawn, **When** the withdrawal takes
   effect, **Then** that screen stops showing cameras.
2. **Given** the same withdrawal, **When** the other screens are checked,
   **Then** they are unaffected.

---

### Edge Cases

- **A screen restored from a backup image**, carrying a credential another
  screen is also using.
- **A credential that outlives the person who installed it.** Nobody rotates
  what nobody remembers exists.
- **Twenty screens recovering together** after fab power returns, all
  authenticating within seconds of each other.
- **A screen whose clock is far out after a power cut**, where time-based
  credentials fail in ways that read as authentication problems.
- **The identity service being unavailable when a screen restarts.** It must
  keep trying rather than settling on a prompt, because that condition clears
  itself.

---

## Requirements *(mandatory)*

### Functional Requirements

**Staying up (US1)**

- **FR-001**: A screen MUST keep showing its wall past the session limits that
  end it today — both the idle cut-off and the hard ceiling.
- **FR-002**: A screen MUST recover unattended from an outage longer than those
  limits.
- **FR-003**: A screen MUST NOT require a person on any schedule.

**Holding only what it needs (US2)**

- **FR-004**: The account a screen uses MUST be able to view cameras in its own
  fab and MUST NOT be able to change anything.
- **FR-005**: It MUST NOT be able to see another fab's cameras.
- **FR-006**: **Operator accounts MUST NOT gain any authority from this change.**
  This is the requirement that distinguishes this feature from the one that was
  refused.
- **FR-007**: A screen MUST NOT hold a credential that lets it act as anything
  other than a wall display.

**Being able to stop a screen (US3)**

- **FR-008**: A screen's access MUST be withdrawable, and withdrawal MUST stop
  that screen without waiting for it to restart.
- **FR-009**: Withdrawing one screen MUST NOT affect the others.

**Not regressing**

- **FR-010**: A restart within the current session limits MUST keep working as
  spec 049 left it.
- **FR-011**: Operator sign-in to the management application MUST be unchanged.
- **FR-012**: **Session expiry for every other client MUST be unchanged.** Making
  a wall stay up must not extend how long an operator's browser session lives.

### Key Entities

- **Wall display account** — what a screen signs in as. Sees cameras in one fab.
  Not a person, and not shared with people.
- **Long-lived grant** — what lets a screen outlast a session. The subject of
  the feature and the thing whose authority must stay narrow.
- **Fab** — what bounds a screen's view. A screen sees one.

---

## Success Criteria *(mandatory)*

- **SC-001**: A screen runs past the hard ceiling — more than ten hours — and
  has shown no prompt. Demonstrated either by waiting or by shortening the limit
  deliberately, and **the record must say which**.
- **SC-002**: A screen recovers unattended from an outage longer than the idle
  cut-off, which spec 049 explicitly could not do.
- **SC-003**: The account a screen uses is refused every write it attempts, and
  every read outside its fab.
- **SC-004**: An operator account holds exactly what it held before this change,
  checked directly rather than argued.
- **SC-005**: Withdrawing one screen stops it and leaves the rest running.

### Explicitly not claimed

- **No per-device identity.** The audit trail will name the wall-display
  account, not which screen. That needs a credential a browser cannot hold, and
  is tracked separately.
- **No latency claim.** Nothing here is on the event-to-overlay path.
- **No claim about twenty screens** unless twenty are actually exercised. The
  target names twenty and one is a weaker demonstration.

---

## Scope

### In scope

- A wall display signing in as something other than a person.
- That account holding the privilege needed for a long-lived grant, and nothing
  more.
- A screen using such a grant to stay up and to come back.

### Out of scope, and named

- **Per-device identity** — a credential belonging to *screen 7* rather than to
  wall displays generally. Needs something a page cannot hold.
- **Enrolment, rotation and revocation as an operator workflow.** This spec
  needs withdrawal to be *possible*; building a console for managing device
  credentials over their life is a different feature.
- **Raising session lifetimes for everyone.** Considered and rejected on the
  issue: it trades a bounded loosening for an unbounded one, and FR-012 forbids
  it.

---

## Assumptions

- **A wall display is not a person and should not sign in as one.** This is the
  premise the whole design rests on, and it is worth stating because today a
  screen signs in as an operator.
- **One account per fab**, because a screen must see only its own fab and fab
  scoping today comes from the account. Confirmed by reading how an existing
  account is scoped.
- **Someone signs a screen in once, at installation.** Not per restart, which is
  what the target forbids.
- **Twenty is the number to design against**, from §Availability.

---

## Dependencies

- Spec 049's work: a screen already keeps its grant across a restart and spends
  it on startup. This feature extends how long that grant remains usable; it
  does not replace the mechanism.
- The identity provider already defines the privilege in question and already
  keeps long-lived grants for thirty days with no ceiling. **Verified by query
  during spec 049**, not assumed.

---

## Open for planning, not settled here

- **How the privilege is granted without the app naming it.** Naming a
  permission the provider has not granted **fails the entire sign-in** — no
  token at all — which took every screen down during spec 049. There is a way to
  arrange this so the application never names it, removing that failure mode
  entirely; whether it produces the long-lived grant needs **verifying rather
  than assuming**, because assuming is what caused the outage last time.
- **Whether one account per fab or one per screen.** Per-fab is the assumption
  above; per-screen is closer to device identity and costs more credentials to
  manage.
- **What withdrawal actually means** for an account rather than a device, given
  FR-009 requires stopping one screen without stopping the others.
