# ADR-0134: A wall display holds a grant nobody else can, and the containment lands before the widening

**Status:** **Accepted**
**Date:** 2026-08-31
**Supersedes:** ADR-0132 (which was superseded before it shipped)
**Amends:** ADR-0131 (the kiosk's requested scopes), ADR-0133 (the classification rule), constitution §Availability
**Relates to:** ADR-0080, spec 049, spec 050, spec 051, spec 052, issues 1976, 1987, 1988, 1989, 1992, 1995

## Context

A wall drops to a sign-in prompt roughly **twice a day per screen**, because the
sign-in session ends on a clock regardless of activity. Spec 051 removed the
*identity-outage* half of the availability problem — a wall now survives the
provider going away and returns in about 34 seconds untouched. This is the other
half, and it is the more frequent failure.

Escaping the ceiling needs a privilege the provider grants to an account: one
that mints credentials which never expire. **Spec 049 refused to take it**,
because giving it to the operator account would let every operator mint such
credentials. That refusal was right.

**Spec 050 tried to confine it to four wall-display accounts and was withdrawn
before merge.** Its containment was true of a configuration file and false of
every running system, and ADR-0132 recorded the attempt honestly enough that
this one could start from evidence rather than from scratch.

## The thing that made the previous attempt wrong

The provider composes a default privilege set for **every account created after
the realm is imported**, and that set includes the long-lived-credential
privilege. Accounts *declared* in the realm file are unaffected — they receive
exactly the roles they name.

So the file showed four wall displays holding it and looked contained, while
**every kiosk the system enrols was born holding it too** and the file said
nothing about them. An architecture guard reading that file stayed green for the
whole of spec 050 while the claim it stood for was false.

## Decision

**Contain first, then use.** In that order, and the order is the decision.

### 1. The privilege is taken back as each account is created

Enrolment removes it as part of creating a kiosk, and a startup sweep covers the
kiosks enrolled before this existed.

**It needs no new authority.** Measured against the real identity service
account: allowed, leaves the account holding nothing, leaves the kiosk still able
to obtain a token, idempotent. Nothing in the system authorises on what is
removed — access is decided by scope and fab membership.

**If the removal fails, the enrolment fails**, and the half-made client is
deleted. Reporting success over an account that kept the privilege is the
outcome the whole containment exists to prevent; leaving the client behind would
also block every retry, since the existence probe would answer "already
enrolled" for something never finished.

### 2. A wall display signs in as a different client

One deployment flag picks both the client and the scopes, so there is no half
configuration.

| Mode | Client | Requests | Carries |
|---|---|---|---|
| default | `kiosk-web` | `openid` | today's scopes, including a write scope |
| wall | `kiosk-wall` | `openid offline_access` | five **read** scopes, no write |

**The second client is the point, not a workaround for the scope.** Scopes belong
to clients, so this is the only place a wall display's authority can be narrowed.
Spec 050 recorded that such an account could change nothing while its grant
carried `sse.events.write`; here the authority is not in the grant.

**The scope is optional, never default.** A default scope is mandatory: the
provider refuses the entire sign-in for any account without the matching
privilege. That is exactly what made spec 050 unshippable — every operator, and
six kiosk end-to-end specs, locked out of the app.

**And it cannot simply be added to `kiosk-web`.** An optional scope refuses
nobody *only while nobody asks for it*: an account without the privilege that
requests it is refused the whole sign-in. The application cannot decide per
account either, because the scope is requested before anyone has signed in.

### 3. `not_allowed` becomes a refusal (amending ADR-0133)

A wall-mode screen signed in as an operator receives that code. ADR-0133 treats
unrecognised codes as *recoverable* — deliberately, because a wrong "terminal"
darkens a wall. Here that default is wrong in the other direction: the screen
would retry forever behind "Reconnecting", telling whoever reads it that the
problem will clear. **This feature makes the code reachable, so this feature
fixes it.**

## What this costs

| | Before | After |
|---|---|---|
| A wall drops to a prompt | about twice a day per screen | **no** |
| What a stolen screen yields | up to **ten hours** of use — thirty minutes is the *idle* timeout | **a grant with no expiry while it is used** |
| What that grant may do | read and **write** its fab | **read only** |
| Who may hold the privilege | every account created after import | accounts this system creates: **wall displays only** |
| Accounts created by hand | — | **still inherit it** (issue 1995) |
| Operator authority | — | **unchanged** |

**What bounds the theft**: an unused offline session is removed after thirty
days. That cuts both ways, and the previous attempt got both wrong — the
exposure is smaller than "never expires" suggests, and a screen legitimately
switched off for longer than thirty days needs a person, so the availability
guarantee is weaker than "it never drops out" suggests.

**Neither figure is set by this repository.** Every session timing here is a
provider default; the realm sets one lifetime and nothing else.

## What the containment does not cover

**An account created by hand in the provider's console still inherits the
privilege.** Closing that means narrowing the default set itself, which requires
realm-management authority — measured, one permission at a time: nothing the
identity service holds today suffices, and the permission that does is authority
over session lifetimes, roles and authentication flows alike.

**That is broader than the privilege it would contain**, for a case this system
does not drive. It was judged not worth taking while the driven path can be
covered for free. Filed as issue 1995 rather than absorbed, and the requirement
was narrowed in the open rather than quietly satisfied.

## What was demonstrated

Against the running stack, not the realm file:

- an enrolled kiosk holds **nothing**, and an account created directly **does**
  hold it — the control, without which the first assertion proves nothing;
- an operator holds nothing; a wall display holds it;
- a wall display's token carries `kiosk-wall`, the five read scopes, **no write
  scope**, its fab group, and a refresh token of type **Offline with no expiry**;
- it opens a wall, is refused every write it attempts, and cannot read another
  fab;
- withdrawing one screen stops it and leaves a sibling running;
- the ordinary kiosk is unchanged — sixteen of its tests pass untouched.

**Not demonstrated**, and the record must not imply otherwise: twenty screens
(four is the most ever exercised, once); a real power cut; ten hours in
production; that anything rotates a wall-display credential; anything about
production at all, which does not exist.

## Three defects only running it found

Recorded because each was invisible to every static check, and two of them made a
working sign-in look like a broken provider.

1. **The app ignored the port the host injects.** A second instance bound the
   same port and exited at once — reported as *running*, because the process had
   started.
2. **The wall client did not name its own origin.** With `webOrigins: "+"`,
   allowed origins come from the redirect URIs, so sign-in completed and the
   token exchange was then blocked by CORS. A wildcard entry was present and did
   not help.
3. **The gateway did not allow the new origin**, so a wall display signed in
   perfectly and could not load a single layout.

A fourth was caught by booting rather than by running: **a 733-character client
description failed the entire realm import**, because the provider stores it in
a 255-character column. There is now a test for it.

## Consequences

- **Positive:** a wall stays up through its own session ceiling.
- **Positive:** a wall display is read-only by construction rather than by
  assertion.
- **Positive:** the widening is real rather than clerical, and it landed second.
- **Negative:** a stolen screen that stays powered yields a grant that does not
  expire.
- **Negative:** nothing rotates or cleans up a wall-display credential.
- **Negative:** an account created by hand is not covered.
- **Negative:** the audit trail names the wall-display account, not the screen.
- **Negative:** **§Availability is still not discharged** — twenty screens and a
  real power cut remain unmeasured.

## Alternatives Considered

- **Narrow the provider's default privilege set.** Total, and needs authority
  broader than the privilege it contains. Filed.
- **One client with a mode flag.** Cannot narrow authority at all, so the
  never-expiring grant would keep its write scope.
- **Give the privilege to operators.** What spec 049 refused, on these grounds.
- **Raise the session lifetimes for the realm.** No new authority, but it loosens
  expiry for every client including the management console — trading a bounded
  widening for an unbounded one.
- **Leave the ceiling.** Honest, and leaves a 24/7 fab walking the floor twice a
  day.
