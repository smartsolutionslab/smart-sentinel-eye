# Contract — what a kiosk screen shows when identity fails

The only interface this feature exposes is **what is on a wall-mounted display**
and **what it does without being touched**. There is no HTTP contract, no message
contract and no realm change.

Written as properties a test can assert, not as wording. Wording is an
implementation choice; these are the promises.

---

## C1. Reconnecting *(verdict: `recoverable`)*

| Must | |
|---|---|
| **Retry without interaction** | on the schedule in `data-model.md` §2 |
| **Return to the wall** | within **2 minutes** of the provider becoming healthy, restoring the layout that was showing |
| **Say recovery is automatic** | a person reading it must learn that no action is needed |
| **Offer a manual attempt** | and using it must not leave two retry loops running (FR-013) |
| **Not show library text as the headline** | "Failed to fetch" is what it says today |

**Must not**: stop retrying; show a credential prompt; look the same as a screen
that needs a person.

---

## C2. No longer authorized *(verdict: `refused`)*

| Must | |
|---|---|
| **State the screen is not authorized** | in words an operator can act on |
| **Say what to do** | the screen needs re-commissioning by someone |
| **Keep the cause available** | the OAuth code and description, reachable for diagnosis |

**Must not**: **present a username or password field** — this is the property
that most matters, because today this case renders the identity provider's own
login form on a factory wall; retry on a timer; return to *Reconnecting* without
a successful sign-in (FR-014).

---

## C3. Session expired *(verdict: `interactive`)* — unchanged

Behaviour is **exactly as today**. Listed so the contract is complete and so no
one folds it into C2: this is the ten-hour ceiling arriving (issue 1989), not a
revoked screen. Telling an operator a screen was revoked when it hit a time limit
sends them to re-commission hardware that needed a sign-in.

---

## C4. Several screens recovering together

| Must | |
|---|---|
| **Not arrive simultaneously** | attempts spread across a window |
| **Grow the interval** | while the provider is down |
| **Stay bounded** | the ceiling must not delay C1's two-minute promise |

Exercised with a handful of screens. **Twenty is the constitution's number and
twenty will not be exercised** — see `quickstart.md`.

---

## C5. What the classifier promises

| Must | |
|---|---|
| **Classify by reported cause, not by whether the provider answered** | a reachable provider saying `server_error` is **recoverable** |
| **Default unrecognised causes to `recoverable`** | FR-005, and the asymmetry is the reason |
| **Be the only decider when a cause exists** | the 60-second guard speaks only where there is no error object |

**Must not**: branch on the error's class before its code. That single ordering
mistake turns an overloaded identity service into a wall of screens announcing
they have been revoked, and it would pass any test that only ever stops the
provider.
