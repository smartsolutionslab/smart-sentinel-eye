# Research — 049 a wall comes back on its own

Phase 0. Everything here was read out of the source or **queried from the
running identity service**. Where a claim is a judgement it says so.

---

## R1. There are two failures, not one, and they need different treatment

The spec names both stories. Research shows they have **separate causes**, and
this is the single most useful finding here — a fix for either one alone leaves
the other entirely in place.

### Cause A — a restart destroys the tokens

The kiosk configures no token store, so the OIDC library uses its default:
**`sessionStorage`**. The e2e contract spec confirms it, reading `oidc.user:`
straight out of `window.sessionStorage`.

`sessionStorage` does not survive the browser process ending. A reboot therefore
loses every token unconditionally — **no session lifetime on the server matters,
because nothing on the device remembers anything.** Recovery then falls to the
identity provider's own sign-in cookie, which for a freshly started browser is
also gone.

That is the dark wall after a power cut, and it is a **client-side storage**
problem.

### Cause B — the sign-in session expires on a clock

Queried from the running realm:

| Setting | Value |
|---|---|
| `ssoSessionMaxLifespan` | **36000 s — 10 hours** |
| `ssoSessionIdleTimeout` | 1800 s — 30 minutes |
| `accessTokenLifespan` | 3600 s — 1 hour |
| `offlineSessionIdleTimeout` | 2592000 s — **30 days** |
| `offlineSessionMaxLifespanEnabled` | **false — offline grants have no ceiling** |

`ssoSessionMaxLifespan` is a hard ceiling **regardless of activity**. A wall
running continuously hits it about **2.4 times a day, per screen**. No storage
choice fixes this; the session ends server-side.

**Only a mechanism outside the ordinary sign-in session addresses cause B**, and
the last two rows say the realm is already configured to keep such grants for
thirty days with no ceiling.

---

## R2. Nothing runs on a kiosk device except a browser

**Decision: the "hold the credential outside the page" shape is out of scope,
and this is why.**

`kiosk-web` is an npm app in the composition root and a **static Vite build** in
deployment — `docs/deployment-frontend-env.md` says both SPAs are static builds.
`deploy/` contains one Helm chart, for a message broker. There is no shell, no
agent, no device image, no provisioning tooling: a kiosk is a browser pointed at
a URL.

So that shape does not mean "put the secret somewhere else". It means
**inventing a device runtime** — something to build, sign, distribute and update
across twenty-plus screens per fab, for every fab. That is a subsystem with its
own lifecycle, not a feature; and shipping software to industrial hardware is a
different kind of commitment from shipping a web build.

It remains the only shape that matches the kiosk-auth decision **as written**,
which is exactly why that decision needs amending rather than satisfying.

**Alternatives considered**

- *A shared server-side component holding one secret for all kiosks.* Rejected:
  it makes every screen the same principal, so one compromised device cannot be
  cut off without cutting off the fab — it fails FR-006 by construction.
- *A per-device secret in the bundle.* Rejected: it is a published secret, and
  it would be the same one after every restart.

---

## R3. The requirement about secrets cannot be met literally, and pretending otherwise would be the real failure

**FR-004** says nothing the kiosk uses to prove itself may be readable from the
page it displays.

**No browser-only design can satisfy that**, including the one this plan
recommends. The app already keeps tokens in browser storage today; anything that
survives a restart must be readable by the page that reads it back.

Two things are true at once and both belong in the record:

- **A client secret in the bundle is categorically worse.** It is the same
  credential on every device and every restart, it grants the client's full
  authority, and stealing it once is permanent until someone rotates it. This is
  what makes the decision-as-written unbuildable, not merely awkward.
- **A device-acquired grant is a different risk.** It is obtained by that screen,
  belongs to that screen, is revocable on its own, and carries view-only
  authority in one fab. It is still readable on the machine it sits on.

**So FR-004 needs refining rather than ticking**, and the plan proposes: *the
delivered bundle contains no credential; anything a device acquires is that
device's alone, independently revocable, and no broader than view-only.* That is
a weaker promise than FR-004 as written, and stating it plainly is the point —
this is the same move as ADR-0129, where a requirement that could not be built
was withdrawn rather than quietly reinterpreted.

**Storage is a real trade, recorded honestly.** Surviving a restart means moving
tokens from `sessionStorage` to storage that persists. That genuinely widens
exposure: today a stolen device yields tokens only while powered on. The
mitigations are revocability, view-only scope and one fab — not the storage.

---

## R4. What the middle shape costs, beyond the obvious

A long-lived grant obtained once per device meets both causes. Its costs are
worth naming before anyone commits.

- **The principal is not the device.** A grant of this kind belongs to whoever
  authorised it, so an audit trail says *the account that enrolled the screen*,
  not *screen 7*. The per-device confidential identity the system already mints
  would have given true device identity — and cannot be used, because its secret
  cannot live in a page. **This is the concrete thing given up.**
- **One human step per device, once.** Not per restart, which is what the target
  forbids. Whether "once, at installation" is acceptable is a judgement the plan
  should put in front of a reader rather than assume.
- **It needs a small change to what the kiosk client may request.** The realm
  already defines the long-lived grant type, but the kiosk client currently
  cannot ask for it — it is neither a default nor an optional scope. Verified by
  querying the client's scope lists.

---

## R5. What a green suite will not prove

Recorded because the last three features each shipped something the tests
missed, and twice for the same reason: **the setup that makes a test easy to
write is the one that hides the bug.**

The blind spots here are **time** and **number**:

- **Nothing in CI can run for ten hours**, so the story about a wall not dropping
  out cannot be proven by the suite. It can be proven by shortening the ceiling
  on a test realm — which demonstrates the mechanism, not the production
  configuration.
- **Nothing in CI reboots twenty devices.** A single simulated restart is a
  different claim from twenty at once, and the target names twenty.
- **A test that starts from a signed-in state proves nothing here**, exactly as
  a resolved-at-first-render fixture proved nothing in the last feature. Every
  check must begin from *no tokens at all*.

The verification note must say which of these actually happened and which did
not, rather than narrowing the claim to what was convenient to test.
