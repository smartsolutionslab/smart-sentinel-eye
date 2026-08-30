# Verification — 050 a wall that stays up

Phase 5.

---

## 0. What shipped — **nothing**

**Read section 7 first.** This note was written before review, and its tables
below describe a configuration that was withdrawn rather than merged: the realm
file did not import, and the scope arrangement locked every operator out of the
kiosk app. The sections are kept unedited because what they got wrong is the
useful part; section 7 says which rows are false.

---

## 0b. What was built

A screen signs in as a **wall-display account** rather than as a person, holding
a grant that does not expire. It keeps its wall past the session limits that
ended it before, and comes back from an outage that outlasts them.

**Operators gained nothing.** That is the requirement the feature was refused
over once, and it is checked directly rather than argued.

---

## 1. Automated checks

| Check | Result |
|---|---|
| `pnpm format:check` | pass |
| lint / typecheck / test across the app packages | pass |
| Architecture tests | **94 pass** |
| Backend | no C# changed |

Read on exit codes. A grep-based check reported a false pass two features ago.

---

## 2. Demonstrated against a running stack

| Claim | Result |
|---|---|
| The grant is `Offline` and carries no expiry | **pass** — decoded, not counted |
| The account reads its own fab's cameras | **pass** |
| It is refused writes to cameras, layouts and overlays | **pass** |
| A screen keeps its wall after the issuing session ended | **pass** |
| A screen recovers from an outage longer than the idle cut-off | **pass** — the case ADR-0131 could not do |
| Ending one screen's session leaves a sibling refreshing | **pass** |
| No operator holds the privilege | **pass, and it does not mean what it says** — see §7 |

**The refusals are the assertions.** Showing the wall renders proves the account
can read and says nothing about what else it could do, which is the entire reason
this was refusable once.

---

## 3. What was *not* demonstrated

- **Ten real hours.** The ceiling was shortened to **60s idle / 90s max** on the
  running realm. That shows the mechanism and **not** the production
  configuration. Three places say so — the test header, the task, and here —
  because a green run is easy to read as "a wall was watched for ten hours".
- **Twenty screens together.** Two were exercised, which is what made the
  withdrawal test meaningful. The target names twenty.
- **A real power cycle.** A fresh browser context carrying only disk state.
- **That any grant is ever cleaned up.** Nothing does it, and nothing tests it.
- **Anything about production.** The accounts live in the realm the composition
  root imports; `deploy/` provisions no realm. There is no production deployment
  to verify against (ADR-0130), and whoever builds one has to carry these
  accounts or a real fab still drops out while dev and CI look solved.

**One environment interaction worth passing on:** shortening the ceiling
**breaks the e2e seeds**, which drive a long operator session that expires
mid-run. Running with `--no-deps` worked here *only* because the dev database
already carried published layouts; on a clean stack it would not, so that is a
caveat rather than a recipe.

---

## 4. Six harness misreports, and the one that nearly mattered

**A test artefact of mine reported a defect that was not there six times across
specs 049 and 050.** Listing them because the pattern is the finding, not any one
of them:

| # | What the test did | What it looked like |
|---|---|---|
| 1 | Hardcoded a storage key embedding the provider's host | the grant was not persisted |
| 2 | Rerendered without the `Provider`, remounting and resetting a ref | the restart guard did not work |
| 3 | Created a context that did not inherit `ignoreHTTPSErrors` | the refresh exchange failed |
| 4 | Asserted an un-awaited promise, in a test that never touched the config it guarded | a passing mutation |
| 5 | Asserted the picker when the app had restored the wall | the wall dropped to a prompt |
| 6 | Posted to a hardcoded host while tokens came from the proxied endpoint | **withdrawing one screen took down its sibling** |

**The common thread:** hand-constructing a failure condition and then asserting
the wrong success state, or building the environment wrong and reading the
result as the product being wrong.

**Number six is the one that mattered.** The others cost time. That one produced
a *finding* — that withdrawal takes down a whole fab's wall — which would have
been written into the record, filed as an issue, and possibly used to re-scope
US3. What stopped it was running a **control**: two sessions, nothing withdrawn,
both should refresh. The control failed identically, and the explanation
collapsed.

There is a note in memory about that exact issuer trap. It was not applied.

**The lesson worth keeping is the control, not the vigilance.** "Be more careful"
would not have caught it; a cheap experiment that should obviously pass did.

---

## 5. What the checks prove, and what they do not

| Claim | Proved by | Not proved by |
|---|---|---|
| The grant outlives its session | decoding the token | asserting a token exists |
| A wall survives the ceiling | a shortened ceiling | anything under it, which passes with the defect |
| Recovery past the idle cut-off | ageing the grant first | a quick restart, which ADR-0131 already did |
| The account may do nothing else | the refusals | the wall working |
| **Operators gained nothing** | checked directly | not having touched them |
| One screen withdrawn alone | both directions, plus a control | reasoning about the provider |
| **Ten hours in production** | **nothing** | a shortened ceiling |
| **Twenty screens** | **nothing** | two |
| **That a grant is ever removed** | **nothing — nothing removes one** | — |

---

## 6. The environment, left as found

Every probe here modified the running realm, and each was reverted:

- the wall-display account and the default scope, created and removed;
- session limits shortened to 60/90/60 and restored to **1800/36000/3600**.

The realm **file** is the source of truth; a JSON edit does not reach a running
provider, so runtime verification meant mirroring changes in by hand. **CI closes
that gap**: it boots a fresh stack from the file, so if the import fails to
create the accounts or grant the scope by default, the e2e tests fail there
rather than passing on a hand-built realm.

---

## 7. What review found, after this note was written

Five findings; the first two would have stopped the stack, and the fifth is the
one this note was most confident about.

| # | Finding | What this note says above |
|---|---|---|
| 1 | The realm file did not import at all — a JSON comment among the users | §6 says "CI closes that gap"; CI was never run |
| 2 | A default scope is mandatory: every account without the role is locked out of the kiosk app | §2 records six passes obtained on a hand-built realm |
| 3 | The design claim — a default scope decouples app from realm — is false | — |
| 4 | The grant is not view-only; `sse.events.write` was never attempted | §2 row 3 and §5 "the account may do nothing else" are **false** |
| 5 | The privilege reaches every account created at runtime | §2 row 7 and §5 "**Operators gained nothing**" are **true of the realm file and of no running realm** |

**Finding 5 is the one worth dwelling on**, because §5 above lists "Operators
gained nothing — checked directly" against "not having touched them", exactly the
distinction it was proud of drawing. It checked the realm file. In a running
realm the provider composes `default-roles-smart-sentinel-eye`, that composite
includes the privilege, and **every account created after import inherits it** —
including the service account of every kiosk that enrolment mints. Accounts
declared in the file are unaffected, which is why `operator` really is refused,
and why the check passed while the claim it stood for did not hold.

Verified against a booted realm, in both directions and with the fix attempted:

| Asked | Answer |
|---|---|
| Does the default composite include the privilege? | yes |
| `operator`, from the file? | `user` alone — refused an offline grant |
| A kiosk service account created at runtime? | inherits it |
| Can the realm file narrow the composite? | **no** — the declaration is discarded on import |
| Can the admin API? | yes, and it applies to accounts already created |

It is inert only because no client currently offers the scope. This feature is
the one that would offer one.

**The lesson is narrower than "test more".** Every check here read the artefact
the design was written against, so it could only confirm the design had been
written down. Nothing asked the running system a question the file could not
answer. The control that caught misreport six in §4 was the same move — a cheap
question put to the system rather than to the record — and it was not repeated
here.

### Two corrections to the tables above

- **§2's "before" figure is flattering.** Thirty minutes is the *idle* timeout.
  A thief using a grant holds it against the ten-hour ceiling, so the step being
  bought is ten hours → unbounded, not thirty minutes → unbounded.
- **Every session figure quoted anywhere in this note is a provider default.**
  The realm file sets `accessTokenLifespan` and nothing else — the 30 minutes,
  10 hours and 30 days are unstated and unguarded.

### What changed on the branch as a result

- the scope was withdrawn, and the guard that asserted it was **inverted** to
  fail if it returns as a default without the rework;
- the end-to-end specs that need it are gated, not deleted;
- `Every_fab_has_a_wall_display_account_that_holds_it` was rewritten against the
  fabs the realm declares — the previous version **passed with one fab's wall
  dark**, which was confirmed by mutation before and after;
- the refusal assertions now require 401 or 403, so a mistyped path can no
  longer read as proof of safety, and they now attempt `sse.events.write` and a
  cross-fab read — the two authorities never tested;
- the withdrawal test asserts the provider's reason instead of printing it.
