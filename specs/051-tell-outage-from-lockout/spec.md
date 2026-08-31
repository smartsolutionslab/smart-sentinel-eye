# Feature Specification: A dark wall says which failure it is, and comes back on its own when it can

**Feature Branch**: `051-tell-outage-from-lockout`

**Created**: 2026-08-31

**Status**: Draft

**Input**: Issue 1990, re-scoped out of spec 049. Constitution §Availability — 24/7 operation; a wall of 20 kiosks must come up unattended.

---

## The premise this feature was filed on is wrong, and that changes the feature

Issue 1990 states that *"no longer trusted"* and *"cannot reach the identity
service"* **both render as one "Sign-in failed" screen**. They do not. Induced
against a running provider before any requirement here was written — the account
disabled for real, the provider container stopped for real:

| What actually happened | What a person standing in front of the wall sees |
|---|---|
| Identity service **unreachable** | "Sign-in failed" · **"Failed to fetch"** · a *Try again* button |
| The screen's account is **shut out** | **The identity provider's own login form** — "Sign in to your account", username and password boxes. The application is not on screen at all. |
| The stored grant is **refused** | "Session expired" · "Automatic sign-in did not complete. Sign in again to resume the wall." |

Three outcomes, three different screens. **The merged-screen defect does not
exist.**

**What does exist is worse, and it is the opposite of what was filed.** The
transient failure — the one that resolves by itself — is the one that **never
retries**:

> The provider was stopped, the screen went to "Sign-in failed", the provider
> was started again and became healthy. **Ninety seconds later, with nobody
> touching it, the screen still read "Sign-in failed".** It waits for a person
> indefinitely, and the condition it is waiting out has already passed.

So this feature is not "tell two merged screens apart". It is **"a wall that can
come back does come back, and a wall that cannot says so in words"**. Issue 1990
is corrected rather than quietly reinterpreted — the move ADR-0129 and ADR-0131
both exist to prevent.

**A second finding that constrains every requirement below.** In the terminal
case the application has already redirected away and the provider's login form
is what is rendered. **The kiosk cannot put words on a screen it is no longer
showing.** Any requirement to "say plainly which failure this is" must either
act before the redirect or accept that the shut-out case is the provider's page.
This is scoped in FR-007 and is the single biggest constraint on US2.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The wall comes back by itself when the identity service does (Priority: P1)

The identity service is restarted during a maintenance window at 02:00. Every
screen on the wall loses its ability to renew. **Nobody is on the floor.** When
the service returns a few minutes later, the wall is showing cameras again
without anyone having walked to it.

Today the wall is still dark when the shift arrives, showing "Failed to fetch"
on every screen, and comes back only when a person presses a button on each one.

**Why this priority**: This is the constitution's unattended-operation target,
and it is the half that is measurably broken. It is also the only story whose
absence costs an operator twenty walks across a fab floor. Verified as broken —
90 seconds of healthy provider with no recovery.

**Independent Test**: Sign a screen in, expire its grant, stop the identity
service, confirm the failure screen, restart the service, and **touch nothing**.
The wall must return to showing cameras within a stated bound. Fully testable on
one screen; the twenty-screen behaviour is US3.

**Acceptance Scenarios**:

1. **Given** a screen showing its wall, **When** the identity service becomes
   unreachable and the screen's grant needs renewing, **Then** the screen shows a
   recoverable-failure state that names the cause in operator language, and
   **retries on its own**.
2. **Given** a screen in that state, **When** the identity service becomes
   healthy again, **Then** the screen returns to its wall with **no human
   interaction**, and the layout it was showing is restored.
3. **Given** a screen in that state, **When** a person is standing in front of
   it, **Then** the screen tells them it is retrying and that no action is
   needed — a silent dark screen and a retrying screen must not look the same.

---

### User Story 2 - A shut-out screen says so, instead of asking for a password (Priority: P1)

A screen's account is disabled — decommissioned, rotated, or revoked after a
theft. Today that screen displays the identity provider's **login form**. Anyone
walking past sees a username and password prompt on a wall-mounted display, which
invites them to type credentials into it and tells whoever maintains the fab
nothing about what is wrong.

**Why this priority**: Equal to US1 because it is the security-facing half. A
login box on an unattended factory wall is an invitation, and a screen that has
been deliberately shut out is exactly the screen that should not be soliciting
credentials. It is P1 rather than P2 because it is cheap next to US1 and because
leaving it undone means US1's retry logic could plausibly be aimed at a screen
that should stay dark.

**Independent Test**: Disable an account, force the screen to renew, and assert
the screen states it is no longer authorized, names the fab and screen, does not
present a credential prompt, and does not retry.

**Acceptance Scenarios**:

1. **Given** a screen whose account has been shut out, **When** its grant needs
   renewing, **Then** the screen states plainly that it is no longer authorized
   and that someone must re-commission it.
2. **Given** that state, **Then** the screen **does not** present a username and
   password prompt, and **does not** retry on a timer.
3. **Given** that state, **Then** enough detail to diagnose it is available to
   whoever is debugging, without that detail being the headline a passer-by
   reads.

---

### User Story 3 - Twenty screens coming back do not knock the service over again (Priority: P2)

The identity service returns after an outage. Twenty screens have been retrying
throughout. They must not all reconnect in the same instant and take it down
again, turning one outage into a cycle.

**Why this priority**: P2 because US1 delivers the value and this protects it.
**This is the only story here that does not exist at one screen** — a single
kiosk retrying is harmless, and twenty in lockstep against a service that has
just come back is a self-inflicted second outage. Twenty is the constitution's
number, so this is in scope rather than hypothetical.

**Independent Test**: Bring several screens to the retrying state against a
stopped service, restart it, and observe that their recovery attempts are spread
rather than simultaneous. Testable with fewer than twenty; the property is the
spread, not the count.

**Acceptance Scenarios**:

1. **Given** several screens retrying, **When** the identity service returns,
   **Then** their attempts are spread over a window rather than arriving
   together.
2. **Given** a long outage, **Then** the retry interval grows rather than
   hammering a service that is down, and stays bounded so recovery is not
   delayed for many minutes after the service returns.

---

### Edge Cases

- **The provider answers, and what it says is "try later".** `server_error` and
  `temporarily_unavailable` are refusals from a *reachable* provider that mean
  transient. Treating "the provider answered" as terminal would leave the wall
  dark through the single most likely real outage — an overloaded identity
  service. See FR-004; this is the trap this spec exists to avoid falling into.
- **A cause nobody enumerated.** FR-005 fixes the default and its justification.
- **The provider is reachable but wrong** — DNS resolves to something that is not
  the identity service, or a proxy answers. Indistinguishable from a refusal
  without inspecting the answer; treated by whatever FR-005's default says.
- **The outage outlasts the retry ceiling.** FR-006.
- **A screen shut out *during* an outage.** The screen cannot learn it is shut
  out while nothing can answer, so it retries until the provider returns and
  refuses it, then moves to the terminal state. The transition must be one-way.
- **A person presses the manual button mid-backoff.** Must attempt immediately
  and must not leave two retry loops running.
- **The provider returns but the screen's layout no longer exists.** Out of
  scope: that is a wall-content failure, not an identity one.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST classify an identity failure as **recoverable** or
  **terminal** and MUST render a visibly different screen for each.
- **FR-002**: On a recoverable failure the system MUST retry **without human
  interaction** and MUST return the screen to its wall when the identity service
  recovers, restoring the layout that was showing.
- **FR-003**: On a terminal failure the system MUST NOT retry, MUST NOT present a
  credential prompt on the wall, and MUST state that the screen is no longer
  authorized.
- **FR-004**: Classification MUST be by the **cause the provider reports**, not
  by whether the provider answered. A reachable provider reporting a temporary
  condition MUST be treated as recoverable.
- **FR-005**: An unrecognised cause MUST be classified **recoverable**, and the
  reason MUST be recorded next to the rule. *Rationale: the two failure modes are
  not symmetric. Misclassifying terminal as recoverable costs a screen that
  retries pointlessly until someone notices. Misclassifying recoverable as
  terminal costs a wall that stays dark through an outage it would have survived
  — the exact defect this feature exists to remove.*
- **FR-006**: Retrying MUST be bounded so that it does not continue
  indefinitely at full rate, and the bound MUST NOT delay recovery by more than
  a stated interval after the service returns. What happens at the bound —
  continue slowly, or stop and require a person — MUST be stated in the plan and
  justified for a 24/7 fab.
- **FR-007**: The system MUST decide the failure's class **before** handing
  control to the identity provider's own pages, or MUST state explicitly that the
  terminal case is represented by the provider's page and why that is acceptable.
  *This is a constraint discovered by observation, not a preference: today the
  shut-out case is the provider's login form and the application is not
  rendering.*
- **FR-008**: A recoverable-failure screen MUST tell a person that recovery is
  automatic and no action is needed.
- **FR-009**: Diagnostic detail for the underlying cause MUST remain available to
  whoever is debugging without being the headline text on the wall.
- **FR-010**: The system MUST NOT show the raw message from the underlying
  identity library as the primary text on any of these screens. *"Failed to
  fetch" is what it says today.*
- **FR-011**: Recovery attempts across multiple screens MUST NOT be
  simultaneous.
- **FR-012**: The existing 60-second redirect guard MUST be **reconciled, not
  duplicated**. The plan MUST state whether it is kept, generalized or replaced,
  and there MUST NOT be two mechanisms that can disagree about whether a failure
  is terminal.
- **FR-013**: A manual retry control MUST remain available on the recoverable
  screen, and using it MUST NOT leave two retry loops running.
- **FR-014**: A screen MUST NOT move from terminal back to recoverable without a
  successful sign-in.

### Key Entities

- **Failure classification**: recoverable or terminal, derived from the reported
  cause. The default for unrecognised causes is part of the rule, not an
  accident of it.
- **Retry schedule**: how long until the next attempt, how that grows, its
  bound, and the spread that keeps screens from arriving together.
- **Screen-facing failure state**: what a person reads, distinct from the
  diagnostic detail behind it.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the identity service returns from an outage, a screen that
  was showing a wall is showing it again **within 2 minutes and with no human
  interaction**. *Today: it does not recover at all — measured at 90 seconds and
  still dark.*
- **SC-002**: A person in front of a screen can tell **without training** whether
  it is waiting for a service to come back or needs someone to act.
- **SC-003**: A screen whose account has been shut out **never displays a
  credential prompt** on the wall.
- **SC-004**: With several screens recovering from one outage, their attempts
  land spread across a window rather than in the same instant.
- **SC-005**: A provider reporting a temporary condition leaves the wall
  **recovering, not dark** — the failure mode that costs a whole fab its wall.
- **SC-006**: No screen in any of these states shows text taken verbatim from the
  underlying identity library.

---

## Scope

### In scope

Classification of identity failures at the kiosk, what each state shows, and
unattended recovery from the recoverable one.

### Out of scope, and filed rather than implied

- **The ten-hour session ceiling** — issue 1989, blocked on issue 1992. A screen
  still drops out roughly twice a day for reasons this feature does not touch,
  and a wall that recovers from outages but not from its own ceiling is still not
  unattended. **This feature does not close §Availability.**
- **Crash recovery** — already built and separate.
- **Stream and wall-content outages** — a different failure with a different
  screen.
- **Per-device identity** — issues 1987 and 1988.
- **Anything about the management console.** Kiosk only; an operator at a desk
  can read an error and act on it.

---

## Assumptions

- **The wall is unattended and the screen has no keyboard.** Every requirement
  favouring automatic recovery follows from this.
- **The identity service is the only dependency in question.** A wall that cannot
  reach its own services is a different failure.
- **"Shut out" means the provider refuses this account**, not that the fab has
  been reorganised.
- Recovery is bounded by how fast the identity service returns; SC-001's two
  minutes is measured from the service being healthy, not from the outage
  starting.
- The observations in this document were taken against the development identity
  provider with outages induced by stopping the container and disabling the
  account. **They have not been reproduced against twenty screens or against a
  production deployment, which does not exist** (ADR-0130).
