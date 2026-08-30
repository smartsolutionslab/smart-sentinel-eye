# Verification — 049 a wall comes back on its own

Phase 5.

---

## 0. What shipped, in one line

A kiosk that loses power comes back to its wall with nobody touching it. **The
ten-hour session ceiling still stands**, so a wall that never reboots still
drops out about twice a day per screen.

---

## 1. Automated checks — run the way CI runs them

| Check | Result |
|---|---|
| `pnpm format:check` | pass |
| `pnpm -r --filter "./apps/**" lint` | pass |
| `pnpm -r --filter "./apps/**" typecheck` | pass |
| `pnpm -r --filter "./apps/**" test` | pass |
| Architecture tests | **90 pass** |
| Backend build | not affected — no C# changed |

**Read on exit codes, not on matching output lines.** Counting lines reported a
false pass two features ago.

**I committed once on a failing typecheck.** The checks and the commit were in
one command, so nothing stopped it. The cause is fixed, the commit was amended
before pushing, and the checks are now gated on `$?` — but it should not have
happened, and the reason it did is that verification and commit were not
separated.

---

## 2. Live verification — against a running stack

Not a fixture. The stack was booted in run mode and a browser drove a real
kiosk.

| Claim | Result |
|---|---|
| The grant is kept where a restart cannot destroy it | **pass** |
| Nothing that matters lives only with the browser process | **pass** |
| A restarted screen shows its wall with no prompt | **pass** |
| No sign-in button appears on the restarted screen | **pass** |

**How a restart was simulated, stated plainly.** A fresh browser context
carrying **only what was written to disk** — no session storage, no in-memory
state, no sign-in cookie. That is a faithful model of what a rebooted device
carries over, and it is a model: **no machine was power-cycled.**

---

## 3. What attempting the ceiling cost, and what it taught

The plan was to build both halves. The second was abandoned on evidence, and the
evidence is worth keeping.

**A long-lived grant needs three changes, not one.** The client scope, the app
requesting it, **and an `offline_access` realm role on whoever signs the screen
in** — which the operator account does not hold. The third grants that account
the power to mint long-lived tokens *generally*. On a shared operator account,
every operator gains it. FR-005 forbids buying recovery with a broader grant, so
it was not bought (issue 1989).

**Naming a scope the realm has not granted fails the entire sign-in.**
`invalid_scope`, no token, the screen never leaves the login form. **Observed, not
theorised**: the app was changed to request the grant against an un-updated realm
and every kiosk sign-in stopped for three test runs before I understood why. The
file's own comment warned of exactly this and I walked into it.

The consequence outlives this feature: **an app build and a realm change of that
kind are coupled and ordered.** Shipping them apart causes precisely the outage
the feature exists to prevent. Making the scope a *default* on the client would
remove the failure mode entirely, since the app would never name it — recorded in
issue 1989 for whoever picks it up.

**A realm JSON edit does not reach a running identity provider.** Its volume
persists, so the file describes the next fresh import and nothing else. The live
check required applying the change through the admin API, and the dev realm was
restored afterwards.

---

## 4. Mutation testing

Nine mutations across the record guard and the unit tests. Each had to kill at
least one test.

| Mutation | Killed |
|---|---|
| Tokens back in process-bound storage | 2 |
| Drop the persistent store entirely | 1 |
| Quietly widen the kiosk's scopes | 2 |
| Stop renewing before expiry | 1 |
| The decision stops recording what it cost | 1 |
| The superseded flow is overwritten rather than kept | 1 |

**Two survived their first pass and both taught something.**

*The storage test asserted the store's **type**, not which storage it wrapped* —
so reverting to process-bound storage, the exact defect this feature removes,
kept it green. It now writes through the configured store and reads back through
fresh stores over each storage in turn.

*The scope guard bounded authority above and not below.* A subset check passes
when a scope is **removed**, so the long-lived grant could have been silently
dropped. That mattered less once the grant was scoped out — but the same check
now fails in both directions.

---

## 5. What the checks prove, and what they do not

| Claim | Proved by | Not proved by |
|---|---|---|
| A restarted kiosk returns to its wall unattended | live run + unit tests | any test starting from stored tokens |
| The grant survives where a restart cannot reach | mutation-killed test | asserting the store's type |
| Authority is unchanged | exact scope assertion | reasoning about the flow |
| The record no longer describes a flow nobody built | architecture guard | — |
| **Twenty screens recovering together** | **nothing** | one screen, which is a weaker claim |
| **A real power cycle** | **nothing** | a fresh context, which is a model of one |
| **Surviving ten hours** | **nothing — and it does not** | the ceiling is unmet, not untested |

---

## 6. What is not verified, and what is not built

- **Twenty at once.** The target names twenty; one was verified. Nothing
  available reboots a wall.
- **A real power cycle.** Simulated by a fresh browser context carrying only
  disk state.
- **The ten-hour ceiling is not fixed**, and this is a decision rather than a
  gap in testing (issue 1989). A continuously-running wall still drops out about
  twice a day per screen, and the constitution now says so.
- **Per-device identity.** The grant belongs to whoever signed the screen in, so
  an audit trail names that account and not the screen (issue 1987).
- **The failure states** were re-scoped out (issue 1990): two of the three
  described nothing in the design that shipped.
