# ADR-0131: A kiosk keeps its own long-lived grant, and a stolen screen is worth more than it was

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

**A kiosk acquires a long-lived grant once and keeps it across restarts.**

- The grant is obtained through the interactive flow **once per device, at
  installation** — not per restart, which is what the target forbids.
- It is kept in storage that survives the browser process, so a reboot recovers
  without a person.
- It is not bound by the ten-hour ceiling, so a running wall does not drop out.
- **Authority is unchanged**: view-only, one fab, exactly the scopes the kiosk
  holds today.

**ADR-0080's kiosk paragraph is superseded** for the flow it names. Its
management-app half stands untouched. The original text stays legible in that
ADR rather than being overwritten — what was decided is not the same record as
what happened.

**The device-runtime shape is deferred, not rejected** (issue 1987). It is the
only shape matching ADR-0080 as written, and nothing runs on a kiosk device
except a browser: it means building, signing, distributing and updating software
across 20+ screens per fab, which is a subsystem rather than a feature.

### Spec 049's FR-004 is refined, because it cannot be met

FR-004 asked that nothing the kiosk uses to prove itself be readable from the
page it displays. **No browser-only design can satisfy that**, including this
one — the app already keeps tokens in the browser today.

The promise becomes:

> The delivered bundle carries no credential. What a device acquires is that
> device's alone, independently revocable, and no broader than view-only.

That is **weaker than FR-004 as written**, and saying so is the point. The same
move as ADR-0129, which withdrew a frame-matching claim rather than reinterpret
it into something that sounded satisfied.

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

- **that device's alone**, so one screen can be cut off without touching the
  other nineteen;
- **independently revocable**, and revocation stops the screen without waiting
  for a restart;
- **view-only in one fab**, so a stolen screen sees what that screen already
  showed to anyone walking past it.

That last point bounds the loss honestly: a kiosk displays its cameras on a wall
in a factory. A thief with its grant sees what they could have seen by standing
in front of it — for as long as it takes someone to withdraw it.

**What is given up beyond exposure:** the grant belongs to whoever authorised
the screen, not to the screen. An audit trail names that account, not *screen
7*. True device identity was available and is exactly what could not be used
(issue 1987).

## Consequences

- **Positive:** both failures addressed — the dramatic one and the frequent one.
- **Positive:** no new software on kiosk hardware; the kiosk stays a web page.
- **Positive:** the record stops describing a flow that was never built.
- **Negative:** a powered-off stolen device now yields a usable grant.
- **Negative:** the audit trail identifies the enroller, not the device.
- **Negative:** one human step per device at installation. Once, ever — not per
  restart.

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
