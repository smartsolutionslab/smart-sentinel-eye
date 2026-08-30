# Feature Specification: A wall comes back on its own

**Feature Branch**: `049-kiosk-comes-back-alone`
**Created**: 2026-08-30
**Status**: Draft
**Input**: Issue 1976, filed by spec 047's review. Constitution §Availability already records the target as not achievable today.

---

## The target, and what actually happens

Constitution §Availability:

> A wall of 20 kiosks rebooting must come up unattended.

Today each screen needs a person. The kiosk signs in the way a human does —
through an interactive redirect — so after a power event twenty screens sit on a
sign-in prompt until somebody walks the floor with a password.

**And it is not only after a power event.** The sign-in session has a hard
ceiling of **ten hours**, independent of how busy the screen is, plus a
thirty-minute idle cut-off. A wall that never reboots at all still drops out
roughly **twice a day, per screen**. The issue was raised about reboots; the
same mechanism fails on a schedule during ordinary running, which makes this
worse than the record currently says rather than better.

*(Both figures were read off the running system, not assumed.)*

---

## Two notions of kiosk identity, and they never meet

The system already mints a **per-device credential**: enrolling a kiosk creates
its own confidential identity, scoped to one fab, with its secret revealed
exactly once. That is the mechanism §Availability names in its own parenthetical.

**Nothing uses it.** The screen authenticates as a shared browser identity
instead, interactively, as a person would.

So the pieces exist and do not connect — which is why the record calls the gap
"smaller than it looks".

### Why that is too optimistic

**A browser cannot keep a secret.** Anything shipped to the screen is readable by
anyone who opens the developer tools on it, and a kiosk in a fab is a machine
strangers walk past. The decision this target rests on assumes the device
credential lives in "a secure local store"; **a web page has no such place.**

So "use the credentials that already exist" is not a small wiring job. The
credential cannot live where the code that would use it runs. That is the actual
difficulty, and any plan that skips it is planning to publish a secret.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A screen that lost power comes back by itself (Priority: P1)

Power returns to a fab. Every wall shows its cameras again, with nobody
touching them.

**Why this priority**: The stated target, and the operational failure. A fab
recovering from an outage has people doing more urgent things than typing
passwords into twenty screens.

**Independent Test**: Cut power to a screen showing a wall, restore it, and
watch without touching anything. Do the same to twenty. **The twenty-screen run
is the real test** — one screen recovering proves the mechanism, not that it
scales to a wall, and the target names twenty.

**Acceptance Scenarios**

1. **Given** a kiosk that was showing a wall, **When** it loses power and
   restarts, **Then** it shows that wall again with no human input.
2. **Given** twenty kiosks in one fab, **When** they all restart together,
   **Then** all twenty come back with no human input, and none is left behind
   because the others were recovering at the same time.
3. **Given** a kiosk that has never been enrolled, **When** it starts, **Then**
   it says so plainly — an unenrolled screen is a setup task, not a fault, and
   must not look like one.

---

### User Story 2 - A screen running for days does not drop out (Priority: P1)

A wall runs continuously and keeps showing cameras, past ten hours, past a day,
past a week.

**Why this priority**: Equal first, and it is the half the issue did not name.
A ten-hour ceiling means a 24/7 wall fails roughly twice a day *without any
outage at all*. Fixing only the reboot case would leave the more frequent
failure in place while letting the record claim the target is met.

**Independent Test**: Run a kiosk past the session ceiling — over ten hours —
and confirm it is still showing cameras and never displayed a sign-in prompt.
**A short test cannot detect this**; anything under the ceiling passes with the
defect fully present.

**Acceptance Scenarios**

1. **Given** a kiosk showing a wall, **When** it has been running for more than
   ten hours, **Then** it is still showing the wall and no sign-in prompt has
   appeared.
2. **Given** a kiosk with no operator interaction for hours, **When** the idle
   cut-off passes, **Then** it keeps showing cameras — nobody touches a wall
   display, so idleness is its normal state, not a signal that it is unused.

---

### User Story 3 - A screen that cannot authenticate says so usefully (Priority: P2)

When a screen genuinely cannot come back, what it shows tells whoever is
standing in front of it what to do.

**Why this priority**: Recovery will not always succeed — a revoked device, a
clock so far out that tokens are rejected, an identity service that is down.
Ranked below the recovery stories because it matters only when they fail, and
above nothing because a wall of identical "something went wrong" screens sends
an engineer hunting the wrong problem.

**Acceptance Scenarios**

1. **Given** a kiosk whose credential has been revoked, **When** it starts,
   **Then** it says the device is no longer trusted rather than showing a
   generic failure.
2. **Given** the identity service is unreachable, **When** the kiosk starts,
   **Then** it says so and keeps retrying without a person, because that
   condition resolves itself.
3. **Given** a kiosk that recovers after such a failure, **When** the cause
   clears, **Then** it returns to its wall without anyone visiting it.

---

### Edge Cases

- **Twenty screens recovering at once**, all authenticating within the same few
  seconds after fab power returns. The identity service sees the whole wall
  arrive together.
- **A device whose access is withdrawn while it is running.** It must stop
  showing cameras — the point of revoking is that it stops, and a screen that
  keeps working until its next restart is not revoked in any useful sense.
- **A clock that is far out after a power cut.** Devices without a battery-backed
  clock come up at an epoch date, and time-based credentials fail in ways that
  read as authentication problems.
- **A screen that recovers while nobody is watching, and again, and again.** If
  recovery quietly consumes something finite, a wall works for weeks and then
  stops, and nothing will connect the failure to its cause.
- **A device replaced by a new one carrying a copy of the old credential.**

---

## Requirements *(mandatory)*

### Functional Requirements

**Coming back (US1)**

- **FR-001**: A kiosk MUST resume showing its wall after a restart with no human
  input, provided it was enrolled before the restart.
- **FR-002**: Twenty kiosks in one fab MUST do this simultaneously.
- **FR-003**: A kiosk MUST NOT require a human to re-establish access on any
  schedule, including after the ten-hour ceiling.

**Not publishing the secret (US1, US2)**

- **FR-004**: Whatever a kiosk uses to prove it is that kiosk MUST NOT be
  readable from the page it displays. A credential in a web page is a published
  credential.
- **FR-005**: A kiosk MUST hold no more authority than it does today — view-only,
  its own fab, nothing more. Unattended recovery must not be bought with a
  broader grant.
- **FR-006**: Each kiosk MUST authenticate as itself, so one device's access can
  be withdrawn without affecting the other nineteen.

**Saying what happened (US3)**

- **FR-007**: A kiosk that cannot authenticate MUST distinguish *not enrolled*,
  *no longer trusted*, and *cannot reach the identity service*.
- **FR-008**: A kiosk MUST keep retrying a condition that resolves itself,
  without a human.
- **FR-009**: A kiosk whose access is withdrawn MUST stop showing cameras
  without waiting for a restart.

**Not regressing**

- **FR-010**: An operator signing in to a kiosk for control actions MUST keep
  working as it does now.
- **FR-011**: The change MUST NOT weaken what a kiosk is allowed to see or
  extend how long a lost credential stays useful.

### Which of these shipped

Recorded here rather than left to a reader to work out, because a spec whose
requirements all read as satisfied is how a record starts describing a system
nobody has.

| Requirement | Status |
|---|---|
| FR-001 a kiosk resumes its wall after a restart | **Met**, verified against a running stack |
| FR-002 twenty kiosks do so simultaneously | **Not demonstrated.** One screen was verified; nothing available reboots twenty |
| FR-003 no human on any schedule, including the ten-hour ceiling | **Not met.** The ceiling stands (issue 1989) |
| FR-004 no credential readable from the page | **Refined, not met as written** — no browser-only design can meet it (ADR-0131) |
| FR-005 no more authority than today | **Met**, and it is why FR-003 was not bought |
| FR-006 each kiosk authenticates as itself | **Not met.** The grant belongs to whoever signed the screen in (issue 1987) |
| FR-007–FR-009 failure states | **Re-scoped out** (issue 1990) |
| FR-010 operator sign-in unaffected | **Met** |
| FR-011 no weakening of what a kiosk may see | **Met.** Scopes are identical; what changed is how long a device-held grant lasts |

**FR-005 and FR-003 are in tension, and that tension is the story of this
feature.** Meeting FR-003 required a grant that would have broken FR-005, so
FR-005 was kept and FR-003 was left unmet and tracked.

### Key Entities

- **Kiosk device** — one physical screen. Enrolled once, identified thereafter,
  belongs to exactly one fab.
- **Device credential** — what proves a screen is that screen. Exists today and
  is unused. Where it can live is the central question, not what it contains.
- **Wall** — what the screen shows. Unchanged by this feature; it is only the
  getting-there that changes.

---

## Success Criteria *(mandatory)*

- **SC-001**: Twenty kiosks restart together and all twenty show their walls
  with nobody touching them. **The target is twenty, so twenty is what is
  demonstrated** — one screen recovering is a different, weaker claim.
- **SC-002**: A kiosk runs for more than twenty-four hours, spanning at least
  two session ceilings, without ever showing a sign-in prompt.
- **SC-003**: No credential that identifies a kiosk can be read from the screen
  it displays.
- **SC-004**: Withdrawing one kiosk's access stops that screen and leaves the
  other nineteen running.
- **SC-005**: A person standing in front of a failed screen can tell from it
  alone whether the device needs enrolling, has been withdrawn, or is waiting
  for something to come back.

### Explicitly not claimed

- **No latency claim.** Coming back is not on the event-to-overlay path, and
  "how fast a wall returns" is bounded by hardware boot time this feature does
  not touch.
- **No claim about a kiosk that was never enrolled.** That is a setup task; the
  target is about screens that already worked.

---

## Scope

**The gate**: the decision that says the kiosk uses a device-bound credential
flow, and that the kiosk app does *not* use the interactive library it in fact
uses, must be amended before this is built. The built system contradicts it
today, and this feature is what forces the question — the same shape as the two
decisions amended by the last two features.

### In scope — as shipped

- **A kiosk resuming its wall unattended after a restart.** Built and verified
  against a running stack.
- Where the device credential lives, which is the whole difficulty. Answered:
  **not in the page**, so the credentials that exist stay unused.

### Narrowed during implementation, and why

- **Running indefinitely (US2) was deferred** — issue 1989. The ten-hour session
  ceiling needs a long-lived grant, and that needs a realm role on whoever signs
  the screen in, widening what that account may do generally. FR-005 and FR-011
  forbid buying recovery with a broader grant, so it was not bought. **The
  ceiling still stands**: a wall that never reboots drops out about twice a day
  per screen.
- **What a screen shows when it cannot come back (US3) was re-scoped out** —
  issue 1990. Its three states assumed per-device credentials; two of them
  describe nothing in the design that shipped.

### Deferred, and to be named as issues during planning

- **Enrolment, revocation and rotation as an operator workflow.** Enrolment
  exists; managing devices over their life is a separate feature.
- **Operator→kiosk binding for control scopes.** Recorded as unbuilt in the
  founding decisions; unrelated to a screen coming back on its own.
- **Whether device credentials should keep being minted while nothing consumes
  them.** An endpoint handing out secrets nobody uses is a standing liability,
  and it is a question about that endpoint rather than about recovery.

---

## Assumptions

- **A kiosk is a fixed screen in a fab**, not a personal device. It has one job
  and shows one wall.
- **Nobody is at the screen.** Anything requiring a person defeats the target,
  including "just once, at setup" *per restart*. Once per device, at enrolment,
  is a different matter and may be acceptable.
- **Twenty is the number to design against**, from §Availability. Not a guess.
- **The screen keeps no more authority than it has now.** This feature is about
  how it authenticates, not what it may do.
- **A fab power event restarts screens together**, so the recovery path is
  exercised twenty times at once rather than one at a time.

---

## Dependencies

- Enrolment already mints a per-device credential. This feature depends on that
  and does not change it.
- **It depends on somewhere to keep that credential that is not the page.** That
  place does not exist today, and identifying it is the first thing planning
  must settle.

---

## Open decision for planning

Three shapes. This spec deliberately does not pick one — the choice turns on how
much the kiosk is allowed to stop being a plain web page.

| Shape | Meets the target? | Secret exposed? | Cost |
|---|---|---|---|
| Something on the device outside the page holds the credential and hands the page short-lived access | Yes, both stories | No | Largest — the kiosk stops being only a web page |
| The device keeps a long-lived grant obtained once at setup, with no secret in the page | Yes, both stories | No | Moderate — one human step per device, ever |
| Amend the target and accept a person per screen | No — it removes the promise instead | n/a | Smallest, and it costs a 24/7 fab twenty logins after every outage and twice-daily drop-outs |

**The middle option looks better than it did before this spec was written.** The
identity service is already configured to keep long-lived grants for thirty days
with no hard ceiling, which is the property the ten-hour limit lacks. That is a
finding, not a recommendation — planning should confirm it rather than take it
from here.
