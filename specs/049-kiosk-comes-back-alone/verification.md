# Verification — 049 a wall comes back on its own

Phase 5.

---

## 0. What shipped, in one line

A kiosk that restarts comes back to its wall with nobody touching it, **for as
long as the session behind its stored grant is still alive.**

That bound is the honest limit and it is not small: the sign-in session idles out
after **30 minutes** and ends at **10 hours** regardless. So this recovers a
screen from a restart, and **not from an outage that outlasts the session** — a
long power cut still needs a person. Escaping that needs the deferred work in
issue 1989.

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
| A screen restarted with an **expired** access token recovers unattended | **pass** |
| No sign-in prompt or button appears on the restarted screen | **pass** |
| The wall itself renders after recovery | **pass** |

**The expiry is induced, and that is the whole value of the test.** An earlier
version restored a *fresh* grant and restarted instantly, so it never crossed the
boundary that matters and passed against a genuine defect. A power cut outlasts
an access token; a test that does not expire one is testing nothing.

**How a restart is simulated, stated plainly.** A fresh browser context carrying
**only what was written to disk** — no session storage, no in-memory state, no
sign-in cookie. That is a faithful model of what a rebooted device keeps, and it
is a model: **no machine was power-cycled.**

### What the strengthened test found

**A race in this feature's own code.** The screen reached a login form while its
token refresh was returning **200**. The silent attempt and the interactive
fallback run in the same commit, so the fallback saw the attempt flagged and the
session still absent, and redirected before the exchange resolved. A successful
refresh and a failed one looked identical from outside.

**Two wrong diagnoses came first**, and both are worth recording because the
pattern is the lesson. The rebooted context not inheriting the project's TLS
setting was plausible and wrong. Concluding the feature was *unbuildable without
a realm role* was worse: an inference drawn from a symptom whose cause was mine,
stated with more confidence than the evidence carried.

Instrumenting the page — console output, failed requests, and the token
endpoint's status — found it in a single run. **That should have been the second
step, not the fourth.** Four times in this feature a test artifact of mine
reported a product defect; the common thread is hand-constructing failure
conditions and getting the environment wrong.

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

Mutations across the record guard, the unit tests and the recovery path. Each
had to kill at least one test.

| Mutation | Killed |
|---|---|
| Tokens back in process-bound storage | 2 |
| Drop the persistent store entirely | 1 |
| Quietly widen the kiosk's scopes | 2 |
| Stop renewing before expiry | 1 |
| The decision stops recording what it cost | 1 |
| The superseded flow is overwritten rather than kept | 1 |
| Never try the stored grant on startup | 1 |
| The "has signed in" marker read from process storage | 3 |
| That marker *written* to process storage | 1 |
| The silent attempt also runs on a mid-run expiry | 1 |
| Never try the stored grant on startup | 1 |
| The "has signed in" marker read from process storage | 3 |
| That marker *written* to process storage | 1 |
| The silent attempt also runs on a mid-run expiry | 1 |

**Two survived their first pass and both taught something.**

*The storage test asserted the store's **type**, not which storage it wrapped* —
so reverting to process-bound storage, the exact defect this feature removes,
kept it green. It now writes through the configured store and reads back through
fresh stores over each storage in turn.

*The scope guard bounded authority above and not below.* A subset check passes
when a scope is **removed**, so the long-lived grant could have been silently
dropped. That mattered less once the grant was scoped out — but the same check
now fails in both directions.

*The marker's **write** path was never exercised* — every test seeded it
directly, so writing it to storage that dies with the process kept the whole
suite green. It is what tells a restarted screen it has signed in before, and
written to the wrong place a kiosk holding a usable grant comes back showing a
first-boot button for someone to press.

*The restart-versus-mid-run distinction rested on nothing.* Removing the guard
that tells them apart changed no test, so the behaviour this feature deliberately
preserved was unprotected until a test covered it.

*The marker's **write** path was never exercised* — every test seeded it
directly, so writing it to storage that dies with the process kept the whole
suite green. It is what tells a restarted screen it has signed in before, and
written to the wrong place a kiosk holding a usable grant comes back showing a
first-boot button for someone to press.

*The restart-versus-mid-run distinction rested on nothing.* Removing the guard
that tells them apart changed no test, so the behaviour this feature deliberately
preserved was unprotected until a test covered it.

---

## 5. What the checks prove, and what they do not

| Claim | Proved by | Not proved by |
|---|---|---|
| A restarted kiosk returns to its wall unattended | live run with an **expired** token | any test restoring a fresh one, which passes against the defect |
| Recovery after an outage **longer than the session** | **nothing — and it does not** | the live test restarts within the session's life |
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
