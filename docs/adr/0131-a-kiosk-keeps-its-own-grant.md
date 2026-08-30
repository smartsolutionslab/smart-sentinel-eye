# ADR-0131: A kiosk keeps its grant across a restart, and a stolen screen is worth more than it was

**Status:** **Accepted**
**Date:** 2026-08-30
**Amends:** ADR-0080 (the kiosk flow), constitution §Availability (the "smaller than it looks" claim), spec 049 FR-004
**Relates to:** ADR-0008, ADR-0130, spec 049, issues 1976, 1987, 1988

## Context

Constitution §Availability sets a target:

> A wall of 20 kiosks rebooting must come up unattended.

It is not met, and reading the running system showed it fails in **two
independent ways** rather than one.

**A restart loses everything.** The kiosk configures no token store, so its
tokens sit in storage tied to the browser process. A reboot destroys them
unconditionally — no server-side session setting can help, because nothing on
the device remembers anything. This is the dark wall after a power cut.

**The session also ends on a clock.** Queried from the running realm:
`ssoSessionMaxLifespan` is **10 hours**, a hard ceiling regardless of activity.
A wall that never reboots still drops to a sign-in prompt roughly **twice a day,
per screen**. Issue 1976 was raised about reboots; this is the more frequent
failure and nothing in the record mentioned it.

### What ADR-0080 decided, and why it cannot be built

ADR-0080 states, as decided:

> Kiosk does **not** use `react-oidc-context`; the flow is too different.

and sketches `bootKioskToken()` loading a device credential from **"a secure
local store"** and exchanging it via `client_credentials`.

**The built kiosk does exactly what that ADR says it does not.** It uses
`react-oidc-context` as a public client with the interactive authorization-code
flow.

More importantly, the decision **cannot** be implemented as written:

- **A browser has no secure local store.** Anything the page can read, anyone
  who opens the developer tools on that screen can read. A kiosk in a fab is a
  machine strangers walk past.
- **A client secret shipped to the page is a published secret** — the same one
  on every device and every restart, granting the client's full authority, and
  compromised permanently until someone rotates it.

The credential the decision assumes **does exist**: `EnrollKioskCommandHandler`
mints a per-device confidential client and reveals its secret once. It has
nowhere to live (issue 1988). That is the whole difficulty, and it is why
§Availability's "the app simply does not use them, which makes the gap smaller
than it looks" is too optimistic.

This is the third locked decision in three features found to describe a system
that was never built — after ADR-014 (spec 045) and ADR-021 (spec 046). The
pattern is worth naming: **a decision nobody has attempted is a decision nobody
has tested.**

## Decision

**A kiosk keeps its grant in storage that survives the browser process.** That
is the whole of the change, and it is enough for the failure the target names: a
screen that loses power returns to its wall with nobody touching it.

- **Authority is unchanged.** The kiosk requests exactly the sign-in it
  requested before — view-only, one fab, the same default scopes.
- **No realm change, and no new grant to anyone.**
- Verified against the running stack: a screen carrying only what a rebooted
  device carries — what was written to disk, with no session storage and no
  sign-in cookie — reaches its wall without a prompt, **with its access token
  already expired**, by spending the refresh token it kept.

**The bound, stated here and not left to a verification note.** Recovery lasts as
long as the session behind that refresh token: it idles out after **30 minutes**
and ends at **10 hours** regardless. So this returns a screen from a restart and
**not from an outage that outlasts the session**. A long power cut still needs a
person, and the target in §Availability is therefore **not** discharged by this
decision.

**ADR-0080's kiosk paragraph is superseded** for the flow it names. Its
management-app half stands untouched, and the original text stays legible there
rather than being overwritten.

### The ten-hour ceiling is left standing, and that is a decision

The sign-in session also ends on a **10-hour** ceiling regardless of activity, so
a wall that never reboots still drops to a prompt roughly **twice a day per
screen**. This ADR does **not** fix that, and the reason is a cost found by
attempting it rather than by reasoning about it.

Escaping the ceiling needs a long-lived (`offline_access`) grant, and that needs
**three** changes, not one:

1. the client scope permitted on the kiosk client;
2. the app requesting it;
3. **an `offline_access` realm role on whoever signs the screen in** — which the
   operator account does not hold.

The third is the problem. It hands that account the power to mint long-lived
tokens **generally**, not merely for kiosks, and if it is a shared operator
account then every operator gains it. **That is a widening of authority outside
the kiosk**, and this feature declined to buy a recovery story with it.

Tracked separately so the choice stays visible rather than disappearing into an
omission.

### Two things learned by trying, which are worth carrying

**Naming a scope the realm has not granted fails the whole sign-in** —
`invalid_scope`, no token, the screen never leaves the login form. Observed: the
app was briefly changed to request the grant against a realm that had not been
updated, and every kiosk sign-in stopped. So an app build and a realm change of
this kind are **coupled and ordered**; shipping them apart would cause exactly
the outage this feature exists to prevent.

**A realm JSON edit does not reach a running identity provider.** Its volume
persists, so the file describes the next fresh import and nothing else. Anyone
verifying a realm change on a live stack must apply it through the admin API and
say which they tested.

## What this costs

**The feature is "make a credential last longer".** Recording the mechanism
without the cost would hand the next reader a weakened posture with no note that
anyone chose it.

| Situation | Before | After |
|---|---|---|
| **Device powered off, stolen** | **Yields nothing** | **Yields a usable grant** |
| Device running, stolen | Yields a grant | Yields a grant |
| Grant withdrawn centrally | Screen stops | Screen stops |
| Blast radius of one device | That screen | That screen |
| What the grant permits | View one fab | View one fab |

**Row one is the whole trade.** A powered-off kiosk becomes worth stealing in a
way it was not before. Everything else is unchanged.

What makes it survivable is **not** the storage — that is readable on the
machine it sits on — but that the grant is:

- **view-only in one fab**, so a stolen screen sees what that screen already
  showed to anyone walking past it;
- **view-only in one fab**, so a stolen screen sees what that screen already
  showed to anyone walking past it.

That bounds the loss honestly: a kiosk displays its cameras on a wall in a
factory, so a thief with its grant sees what they could have seen by standing in
front of it.

**What does *not* bound it, and an earlier draft of this ADR claimed it did:**
per-screen revocation. The grant belongs to whoever signed the screen in, so with
a shared operator account across a wall there is no recorded way to tell which
session is screen 7, and withdrawing the account signs out all twenty. Saying
otherwise made the trade read better than it is (issue 1987).

**What is given up beyond exposure:** the grant belongs to whoever authorised
the screen, not to the screen. An audit trail names that account, not *screen
7*. True device identity was available and is exactly what could not be used
(issue 1987).

**And what is not given up:** no realm role is granted, no account gains a new
power, and the ten-hour ceiling is left in place rather than bought off with
one.

## Consequences

- **Positive:** a screen that loses power comes back with nobody touching it.
- **Positive:** no new software on kiosk hardware; the kiosk stays a web page.
- **Positive:** no realm change and no new grant to any account.
- **Positive:** the record stops describing a flow that was never built.
- **Negative:** a powered-off stolen device now yields a usable grant.
- **Negative:** **the ten-hour ceiling still stands**, so a continuously-running
  wall still drops to a prompt about twice a day per screen. This is the more
  frequent failure and it is *not* fixed here.
- **Negative:** the audit trail identifies whoever signed the screen in, not the
  screen.

## Alternatives Considered

- **A device runtime holding the real device credential.** The only shape
  matching ADR-0080, and the only one giving true device identity and a
  worthless powered-off device. A subsystem, not a feature (issue 1987).
- **A shared server-side component holding one secret for all kiosks.**
  Rejected: every screen becomes the same principal, so one compromised device
  cannot be cut off without cutting off the fab.
- **A per-device secret in the bundle.** Rejected: a published secret, identical
  after every restart.
- **Amend the target and accept a person per screen.** Honest, and it costs a
  24/7 fab twenty logins after every outage plus twice-daily drop-outs. Rejected
  as a worse answer to a real operational need, not as an unreasonable one.
