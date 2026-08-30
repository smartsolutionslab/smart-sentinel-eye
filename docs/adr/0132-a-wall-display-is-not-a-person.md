# ADR-0132: A wall display is not a person, and only it may hold a grant that never expires

**Status:** **Accepted**
**Date:** 2026-08-30
**Amends:** constitution §Availability (the unattended-reboot target)
**Relates to:** ADR-0080, ADR-0131, spec 049, spec 050, issues 1976, 1987, 1988, 1989

## Context

A wall that nobody touches dropped to a sign-in prompt roughly **twice a day per
screen**, and after any outage longer than half an hour. Both figures were read
off the running provider: the sign-in session idles out at **30 minutes** and
ends at **10 hours** regardless of activity.

ADR-0131 made a screen come back from a restart and recorded honestly that
recovery lasted only as long as the session behind the grant. **Escaping the
session entirely needs a privilege the provider grants to an account**, and spec
049 refused to take it, because giving it to the operator account would let
**every operator** mint credentials that never expire. That refusal was right,
and this ADR is the narrower path it left open.

## Decision

**A screen signs in as a wall-display account, not as a person.** One such
account per fab, holding the offline privilege and nothing else.

- **Operators gain nothing.** The privilege reaches wall-display accounts only,
  which is what makes it acceptable at all.
- **One per fab**, because fab scoping comes from the account rather than the
  client. A shared account would let any screen see every fab's cameras.
- **The application is unchanged.** It never names the privilege.

### The application never asks, and that is the design

The scope is a **default** on the kiosk client rather than an optional one.

**This is what repays spec 049's outage.** Requesting a scope the realm has not
granted fails the *entire* sign-in — `invalid_scope`, no token, the screen never
leaves the login form. Spec 049 changed the app to ask for it against a realm
that had not caught up, and every kiosk sign-in stopped. A default scope leaves
nothing in the bundle to be refused, so **an app build and a realm change stop
being able to break each other.**

Verified before any of it was written: the same sign-in yields `typ: Refresh`
expiring in half an hour, or `typ: Offline` with no expiry, **with the
application untouched.**

## What this costs

**A credential that never expires, and nothing removes it.**

| | Before | After |
|---|---|---|
| A screen drops to a prompt | twice a day, and after any outage > 30 min | no |
| What a stolen screen yields | a grant lasting ≤ 30 min | **a grant with no expiry** |
| Who can mint such a grant | nobody | wall-display accounts only |
| What the grant permits | view one fab | **view one fab — unchanged** |
| Withdrawing one screen | n/a | stops that screen only |
| Operator authority | — | **unchanged** |

**Rows two and three are the trade.** A stolen kiosk is worth more than it was,
and the mitigations are that the grant is view-only in one fab, that ending one
session stops one screen — **tested, not assumed** — and that nothing an
operator holds has changed.

**What nothing does:** clean these up. `offlineSessionMaxLifespanEnabled` is
false, so a session persists until someone ends it or the account is disabled.
Disabling an account stops **every screen in that fab**. Rotation is not built,
and a credential nobody remembers is a credential nobody rotates (issue 1988
makes the same objection about a different unused credential).

**What the audit trail says:** the wall-display account, not *screen 7*. Better
than naming an operator, and still not per-device identity (issue 1987).

## What was demonstrated, and what was not

**Demonstrated** against a running stack:

- the grant is `Offline` with no expiry;
- a screen keeps its wall after the session that issued the grant has ended;
- a screen recovers from an outage longer than the idle cut-off — the case
  ADR-0131 explicitly could not do;
- the account is refused every write and reads outside its fab;
- ending one screen's session leaves a sibling running.

**Not demonstrated**, and the record must not imply otherwise:

- **Ten real hours.** The ceiling was shortened to seconds. That shows the
  mechanism and **not** the production configuration.
- **Twenty screens together.** Two were exercised; the target names twenty.
- **That any grant is ever cleaned up.** Nothing does it.

### Where these accounts exist, and where they do not

**Dev and CI only, today.** The accounts and the scope are declared in the realm
the composition root imports. **`deploy/` provisions no realm at all** — it holds
one hand-written broker chart, and the Kubernetes publisher has never been run
(ADR-0130, issue 1015). So there is no production realm for these to be missing
from *yet*, and there is also nothing that would carry them there.

**Whoever builds production provisioning must carry both**, or a wall in a real
fab still drops out twice a day while dev and CI say the problem is solved. That
is the same shape as ADR-0131's ordering hazard: a change split across an
application and a realm, where having only one half is worse than having
neither.

## Consequences

- **Positive:** a wall stays up, and comes back from a real outage.
- **Positive:** no application code changed, and no app build can break sign-in.
- **Positive:** operators gained nothing; the widening is confined to accounts
  that show cameras on a wall.
- **Negative:** a stolen screen yields a grant that does not expire.
- **Negative:** nothing rotates or cleans up these credentials.
- **Negative:** the audit trail names the account, not the screen.

## Alternatives Considered

- **Grant the privilege to operators.** Widest blast radius, and the option spec
  049 declined on exactly these grounds.
- **Raise the session lifetimes for the realm.** No new authority, but it
  loosens expiry for every client including the management app — trading a
  bounded loosening for an unbounded one.
- **A device runtime holding a real per-device credential.** The only shape
  giving true device identity and a worthless powered-off screen. A subsystem
  rather than a feature (issue 1987).
- **Leave the ceiling.** Honest, and leaves a 24/7 fab walking the floor twice a
  day.
