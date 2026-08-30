# Research — 050 a wall that stays up

Phase 0. Every finding below was **observed on the running system**. The one
that mattered most is R1, and it was the question spec 049's outage made
non-negotiable: verify, do not assume.

---

## R1. A default scope yields an offline grant without the app naming it

**This is the finding the feature turns on, and it was tested rather than
reasoned about.**

Two sign-ins through the real kiosk, decoding the refresh token each time:

| | `typ` | expiry | granted scope |
|---|---|---|---|
| **Before** | `Refresh` | ~30 min out | no `offline_access` |
| **After** | **`Offline`** | **none** | includes `offline_access` |

The "after" configuration was:

- `offline_access` as a **default** client scope on the kiosk client;
- the `offline_access` realm role on the signing account;
- **the application unchanged — it never names the scope.**

**So the application does not have to ask.** That matters far more than it
sounds:

Spec 049 changed the app to request the scope against a realm that had not
granted it. The result was not a degraded mode — it was `invalid_scope`, no
token, every kiosk sign-in stopped, and three test runs spent before the cause
was understood. An app build and a realm change were coupled and ordered, and
getting the order wrong would have caused the very outage the work exists to
prevent.

**A default scope removes that failure mode entirely**, because there is no
scope name in the bundle to be refused. The app build and the realm change become
independent: neither can break the other.

**Alternatives considered**

- *Optional scope, requested by the app.* Works, and reintroduces exactly the
  coupling that caused the outage. Rejected on evidence rather than taste.
- *Raising the session lifetimes for the realm.* No new authority, but it
  loosens expiry for every client including the management app — FR-012 forbids
  it, and it trades a bounded loosening for an unbounded one.

---

## R2. Fab scoping lives on the account, so the account must be per fab

Read from the realm: an existing account carries `groups: ["/fabs/munich"]` and
`realmRoles: ["user"]`. Nothing about the *client* scopes a session to a fab —
the account does.

So a wall-display account sees exactly the fabs its groups name. **One account
per fab** follows directly, and it is not a preference: a single account across
fabs would let any screen see every fab's cameras, breaking FR-005.

**Cost, stated:** one credential per fab to install and, eventually, to rotate.
The alternative — one per screen — is closer to true device identity and
multiplies that cost by twenty. It is not needed for anything this spec claims.

---

## R3. The account is declared, not minted

The identity admin client exposes `CreateClientAsync`, `RotateClientSecretAsync`
and group reads. **It has no user operations at all.**

So a wall-display account cannot be created by enrolment without new capability
in a bounded context this feature has no other reason to touch. It is declared in
the realm alongside the accounts that already exist.

**This is what keeps the feature small.** Minting accounts would drag in
lifecycle, rotation and revocation as an operator workflow — which the spec
already puts out of scope, and which would be the third feature in a row to grow
past what was asked.

---

## R4. What withdrawal means for an account rather than a device

FR-008 and FR-009 want one screen stopped without stopping the others, and that
is **harder with an account than it was with a device credential**.

An offline grant is a *session*, and the identity provider tracks offline
sessions individually. So a single screen's grant can be ended without touching
another screen signed in as the same account — the unit of revocation is the
session, not the account.

**But disabling the account stops every screen using it**, which with one account
per fab means an entire fab's wall. That is a real operational hazard and it
belongs in the plan as something to state plainly rather than discover.

**Not yet verified**: that ending one offline session leaves a sibling session
working. R1's probe established the token shape; this is a different claim and
**must be tested before FR-009 is called met.** Recording it as unverified rather
than assuming it, which is the discipline this feature exists to practise.

---

## R5. What a green suite will not prove

The blind spots are the same three as last time, and naming them again is not
ceremony — spec 049 shipped a claim that was true only within a bound nobody had
written down.

- **Ten hours.** Nothing in CI runs that long. The ceiling can be shortened on a
  test realm, which demonstrates the mechanism and **not** the production
  configuration, and any note claiming otherwise is overstating.
- **Twenty screens.** The target names twenty; a test proves one.
- **A real power cycle.** A fresh browser context carrying only disk state is a
  faithful model and is not a machine losing power.

**And one new one.** This feature makes a credential that already outlived a
restart now outlive a session entirely. **Nothing tests that it is ever cleaned
up** — an offline grant with no expiry is exactly the kind of thing that
accumulates silently. The plan should say what ends one, and the verification
should say whether anyone checked.
