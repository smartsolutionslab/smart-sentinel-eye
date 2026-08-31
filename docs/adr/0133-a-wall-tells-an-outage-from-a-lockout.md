# ADR-0133: A wall that can come back does, and one that cannot stops asking for a password

**Status:** **Accepted**
**Date:** 2026-08-31
**Relates to:** ADR-0080, ADR-0131, constitution §Availability, spec 051, issues 1976, 1987, 1988, 1989, 1990

## Context

A kiosk that cannot renew its grant showed one of two things, and both were
wrong in ways nobody had looked at.

**The failure that resolves by itself was the one that waited for a person.** An
unreachable identity service produced *"Sign-in failed"* above the browser's own
phrase for a request that never left the building, and a button. Measured: the
provider was stopped, the screen went dark, the provider was started and became
healthy, and **ninety seconds later, untouched, the screen still read "Sign-in
failed"**. That branch contained no retry of any kind. An identity service
restarted overnight left a shift arriving to twenty dark screens that had been
recoverable for hours.

**The failure that needs a person put a login box on a factory wall.** A screen
whose account had been shut out was redirected to the identity provider, whose
own form then rendered: username and password fields on an unattended display,
inviting anyone walking past to type credentials into it, and telling whoever
maintains the fab nothing.

**The issue this was filed under described neither.** It said the two rendered
as one merged screen. Induced against a running provider, they rendered as three
different ones. The premise was corrected in the open rather than the
requirement being reinterpreted to fit — the move ADR-0129 and ADR-0131 both
exist to prevent.

## Decision

**A failed renewal is classified by the cause the provider reports, and the
screen does what that cause deserves.**

- **Recoverable** — retry unattended, forever, and say that no action is needed.
- **Refused** — say the screen is not authorized, show no credential field, and
  do not retry.
- **Interactive** — unchanged. A session that ended is not a screen that was
  shut out.

### The rule is an allowlist of terminal codes, and that shape is the decision

Anything unrecognised is **recoverable**. The two mistakes are not equal: a
wrong *recoverable* costs one screen a request every thirty seconds; a wrong
*refused* costs a whole wall its picture through an outage it would have
survived, and sends someone to re-commission hardware that was fine.

**Nothing branches on the error's class.** `server_error` and
`temporarily_unavailable` arrive on a fully-formed error response from a
provider that is reachable and merely overloaded — the single most likely real
outage on a fab. Treating "the provider answered" as terminal would darken every
screen through exactly that, **and it would pass any test that induces failure
by stopping the provider**, because that path carries no code at all.

### A refused screen is never redirected

Deciding before the redirect is the whole mechanism, and it is possible only
because a disabled account makes the provider *answer* — `invalid_grant`, "User
disabled" — rather than fail to answer. Verified against a running provider
before the design was written.

### Retry: 2 s doubling to a 30 s ceiling, ±30% jitter, no bound

The ceiling is chosen against the two-minute recovery promise, not for comfort:
worst case the provider recovers just after an attempt fails, so the wall waits
one whole interval. **A ceiling above about sixty seconds breaks that promise
silently**, since the constant and the criterion live in different documents —
so a test asserts the relationship rather than the number.

It never gives up. There is nobody at the wall; a screen that stopped would be a
screen needing a person, which is the failure being removed.

Jitter is what keeps twenty screens from arriving together against a service
that has just come back.

## What this cost, and what it did not

| | Before | After |
|---|---|---|
| Provider restarts overnight | wall dark until someone presses each screen | **comes back unattended** |
| A screen shut out | the provider's login form, on the wall | says so; no credential field |
| Twenty screens returning | in lockstep | spread |
| Session ended at the ceiling | "Session expired" | **unchanged** |
| Kiosk authority | — | **unchanged** — no scope widened |

**This does not close §Availability.** The ten-hour session ceiling still drops
a screen roughly twice a day (issue 1989, blocked on issue 1992), and that is
the *more frequent* failure. A wall that survives outages but not its own
ceiling is still not unattended.

## What was demonstrated

Against a **real** provider — stopped, and an account disabled — not a stub:

- provider stopped → "Reconnecting"; provider restarted → **the wall returned
  with nothing touched, about 34 seconds after the restart command**, of which
  roughly twenty is the provider becoming able to serve;
- a disabled account → "no longer authorized", **zero credential inputs on the
  page**, still on the kiosk's own origin.

**Not demonstrated**, and the record must not imply otherwise: a wall over days;
twenty screens (four were exercised); a real network partition as distinct from
an aborted request — DNS failure, TLS failure and a hung connection are all
untested; anything in production, which does not exist (ADR-0130).

## Four defects the tests found only by running

Recorded because each is a place where a green suite and a working product had
come apart, and three of them were invisible to every unit test.

1. **The OAuth code is one level down.** `react-oidc-context` does not pass on
   what the identity library threw — it normalises the error and keeps the
   original under `innerError`. So `auth.error.error` is always undefined, and
   the classifier called **every refusal recoverable**. All the unit tests
   passed, because each handed the classifier an unwrapped error.
2. **`signinSilent` does not reject.** It resolves `null` and sets the error a
   commit later, so the rejection handler the design rested on barely fires.
3. **A cause-less event pre-empted the classifier.** The token-expired event
   fires on load for a stale grant and redirected a screen about to be ruled
   refused.
4. **A stale closure redirected a screen already ruled refused.** The guard
   against (3) read the verdict as it stood when the deferred caller was
   scheduled — undefined. The screen showed the right words throughout; only the
   network traffic disagreed.

**The pattern:** every one of these lived in the gap between what a hand-built
double does and what the real library does. The doubles rejected; the library
resolves null. The doubles carried the code; the library buries it.

## Consequences

- **Positive:** a wall survives an identity outage without a person.
- **Positive:** a shut-out screen no longer solicits credentials on a factory floor.
- **Positive:** no scope widened, no grant lengthened, no dependency added.
- **Negative:** a screen whose provider is gone for good retries forever, one
  request every thirty seconds.
- **Negative:** the classification is written against the codes this provider was
  observed to send plus the OAuth registry; a provider under real load may answer
  in ways nobody here has seen.
- **Negative:** the ceiling and the recovery promise are related by a test rather
  than by construction.

## Alternatives Considered

- **Classify on the error's class.** Simpler, and wrong in the most damaging
  direction: it darkens a wall through an overloaded provider.
- **Bound the retry and stop.** Reintroduces "a person must walk over" after a
  delay instead of at once.
- **Show the provider's `error_description` on the wall.** Diagnostic and
  unreadable; it is kept for the log instead.
- **Put the classification in `apps/shared`.** One consumer, and the management
  console has a person in front of it.
