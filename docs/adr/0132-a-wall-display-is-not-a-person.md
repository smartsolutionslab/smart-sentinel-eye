# ADR-0132: A wall display is not a person, and only it may hold a grant that never expires

**Status:** **Superseded before it shipped — see "What review found" below**
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

> **This claim was wrong, and review caught it before merge.** A default scope is
> **mandatory, not neutral**: the provider refuses the grant for any account
> lacking the matching role. Verified by booting the realm and signing in —
> `operator` gets `not_allowed: Offline tokens not allowed for the user or
> client`, so every human and all six kiosk end-to-end specs are locked out of
> the kiosk app. The configuration does not repay spec 049's outage; it moves the
> coupling from the bundle to the account's role and reproduces it.

The original reasoning, kept because the mistake is instructive: *requesting a
scope the realm has not granted fails the entire sign-in, so a default scope
leaves nothing in the bundle to be refused, and an app build and a realm change
stop being able to break each other.* The first half is true. The second does not
follow.

Verified before any of it was written: the same sign-in yields `typ: Refresh`
expiring in half an hour, or `typ: Offline` with no expiry, **with the
application untouched.**

## What this costs

**A credential that never expires, and nothing removes it.**

| | Before | After |
|---|---|---|
| A screen drops to a prompt | twice a day, and after any outage > 30 min | no |
| What a stolen screen yields | up to **10 hours** of use, not 30 minutes | **a grant with no expiry** |
| Who can mint such a grant | nobody | wall-display accounts only |
| What the grant permits | view one fab **plus `sse.events.write`** | **unchanged — and that is the problem** |
| Withdrawing one screen | n/a | stops that screen only |
| Operator authority | — | **unchanged** |

**Rows two and three are the trade.** A stolen kiosk is worth more than it was.

> **The "before" column was flattering, which made the trade look larger than
> it is.** Thirty minutes is the *idle* timeout — it ends a grant nobody uses. A
> thief using one keeps it alive against the ten-hour session ceiling, so the
> honest before is up to ten hours of continuous access, not half an hour. The
> step being bought is ten hours → unbounded, not thirty minutes → unbounded.

> **"View-only" was wrong.** The kiosk client already carried `sse.events.write`
> before this feature, and the never-expiring grant carries it too — so a stolen
> screen can inject manual events into its fab indefinitely, feeding overlays and
> automation. Spec 050's FR-004 ("MUST NOT be able to change anything") is
> **unmet**, and its SC-003 is false. The end-to-end test asserted refusals on
> three write endpoints and did not attempt the one the account actually holds.

What does hold: ending one session stops one screen — **tested, not assumed** —
and nothing an operator holds has changed.

**What ends one, corrected.** An *unused* offline session is removed after
**30 days** (`offlineSessionIdleTimeout`), and only a session kept in use
persists indefinitely (`offlineSessionMaxLifespanEnabled` is false). So the
earlier claim cut both ways and was wrong in both: the exposure is **smaller**
than stated — a stolen powered-off screen is the unused case and expires in 30
days — and the availability guarantee is **weaker** than stated, because a screen
switched off for more than 30 days needs a person.

**No figure in this ADR is set by the repository.** `ssoSessionIdleTimeout`,
`ssoSessionMaxLifespan`, `offlineSessionIdleTimeout` and
`offlineSessionMaxLifespanEnabled` are all **absent from the realm file** —
checked, not assumed; `accessTokenLifespan` is the only session figure it sets.
So the thirty minutes and ten hours in the Context above are *also* provider
defaults. **The problem this feature exists to solve is a default nobody chose**,
and a provider upgrade can change the problem, the cost, and the availability
guarantee together, with every test still green. Rotation is still not built.

**What the audit trail says:** the wall-display account, not *screen 7*. Better
than naming an operator, and still not per-device identity (issue 1987).

## What was demonstrated, and what was not

**Demonstrated** against a running stack:

- the grant is `Offline` with no expiry;
- a screen keeps its wall after the session that issued the grant has ended;
- a screen recovers from an outage longer than the idle cut-off — the case
  ADR-0131 explicitly could not do;
- the account is refused writes to cameras, layouts and overlays — **but not
  `sse.events.write`, which it holds and which nothing tested**;
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

## What review found, before this shipped

Five things, and the first two would have broken the stack:

1. **The realm file did not import at all.** A JSON comment in the users array;
   the provider rejects unknown fields outright. Nothing would have started.
2. **A default scope locks out every account without the role**, including every
   operator and all six kiosk end-to-end specs.
3. **The design claim above is false** — see the callout.
4. **The grant is not view-only** — see the trade table.

5. **The privilege was never confined to wall displays in a running realm.**
   The provider composes `default-roles-smart-sentinel-eye`, and that composite
   **includes `offline_access`**. Accounts imported from the realm file are
   unaffected — they receive exactly the roles they name, which is why
   `operator` is refused. But **every account created after import inherits
   it**, including the service account of every kiosk that Enrol mints at
   runtime. FR-006 — "the widening reaches wall-display accounts and nobody
   else" — is the claim spec 049 refused this feature over, and it does not hold
   for any account the system creates itself.

   Four things were checked against a booted realm rather than argued:

   | Question | Answer |
   |---|---|
   | Does the composite include the privilege? | **Yes** — `offline_access`, `user`, `uma_authorization`, `view-profile`, `manage-account` |
   | Do accounts from the file inherit it? | **No** — `operator` resolves to `user` alone |
   | Does an enrolled kiosk's service account? | **Yes** — created at runtime, inherits all five |
   | Can the realm file stop it? | **No** — see below |

   It is inert *today* only because no client offers the scope. This feature is
   the one that would offer one, and at that moment every runtime-created
   account can mint a credential that never expires.

   **The realm file cannot express the fix.** Declaring
   `default-roles-smart-sentinel-eye` with a narrowed composite was tried: the
   import discarded it wholesale — the stored role came back with an empty
   description and the provider's own five composites — and a composite may only
   name roles the file declares, so `uma_authorization` could not even be
   written down. Narrowing it through the admin API **does** work, and applies
   to accounts already created, because the composite resolves at evaluation
   time. So this needs a step **after** import, which is a different shape of
   change from everything else here, and no test in this repository can guard it.

   That change is not made here. It belongs with whichever design actually grants
   the scope, and making it now would leave a control in place for a feature that
   is withdrawn.

**How this got past me.** Every probe mirrored changes into a *running* realm by
hand, because a file edit does not reach one. The verification note says exactly
that, then asserts "CI closes that gap" — and CI was never run. **The file is the
only thing this feature changes and the one thing nothing exercised.**

The fifth got past differently and is worth separating: it was not a file that
went unexercised but a **question never asked**. Every check here read the realm
file and asked who *declares* the privilege. Nothing asked who *holds* it, and
the two answers differ for every account the system creates at runtime. A guard
that reads the same artefact the design was written against can only confirm the
design was written down.

The account and scope design needs rework rather than repair. This ADR is left
in place, corrected, because the reasoning is worth keeping and the mistake is
worth more.

## Consequences

- **Positive:** a wall stays up, and comes back from a real outage.
- **Positive:** no application code changed, and no app build can break sign-in.
- **Positive:** operators gained nothing; the widening is confined to accounts
  that show cameras on a wall.
- **Negative:** a stolen screen yields a grant that does not expire.
- **Negative:** nothing rotates or cleans up these credentials.
- **Negative:** the audit trail names the account, not the screen.
- **Negative:** confining the privilege to wall displays needs a post-import
  step the realm file cannot express, and no test here can guard it.

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
