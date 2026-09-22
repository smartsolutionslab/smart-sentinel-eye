# Spec 219 — The guess a username already made

**Issue:** #2510 — "The dev realm's password policy is exhaustible within
#2285's own lockout math"
**Branch:** `2510-password-policy-exhaustibility`
**Worktree:** `D:\Github\sse-2510`
**Lane:** autonomous (ADR-0144) — **and this spec ends inside that lane's
limits on purpose.** The issue itself reserves the remedy for a human.
**ADRs:** ADR-0007 / ADR-0008 (Keycloak per fab, two browser auth flows —
`docs/adr/0000-initial-decisions.md` rows 007–008), ADR-0036 (smallest
possible change; surface assumptions), ADR-0037 (the phased workflow),
ADR-0052 / ADR-0053 / ADR-0054 (xUnit + Shouldly, sentence-style naming,
hand-written data), ADR-0080 (browser auth), ADR-0103 (integration tests
against the Aspire fixture, no Testcontainers), ADR-0109 (`[P]` means
disjoint files), ADR-0118 (one telemetry sink; no production deployment),
ADR-0139 (rules that fail the build, not the review), ADR-0144 (the
autonomous lane and what it may not do)
**Constitution:** §VIII (safe by default at trust boundaries), §Testing
(two obligations — new behaviour red, behaviour-preserving characterised
green), NFR §Security ("token-bound, short-lived credentials")

---

## Problem

`src/AppHost/Realms/smart-sentinel-eye-realm.json:20` declares

```
"passwordPolicy": "length(8) and upperCase(1) and lowerCase(1) and digits(1)"
```

and `:11-18` declare the brute-force detector spec 207 (#2285) added:
`bruteForceProtected: true`, `failureFactor: 10`, `waitIncrementSeconds: 60`,
`maxFailureWaitSeconds: 900`, `maxDeltaTimeSeconds: 43200`,
`quickLoginCheckMilliSeconds: 1000`, `minimumQuickLoginWaitSeconds: 60`,
`permanentLockout: false`.

Spec 207 closed the *unbounded* guessing hole. #2510 asks the harder question
it left open: **is what remains bounded enough to matter?** The lockout caps
the *rate*. It says nothing about the *size of the set an attacker has to get
through* — and that size is a property of the password policy, which spec 207
deliberately did not touch.

The issue's arithmetic argument ("a top-1000 list is exhaustible well within
the lockout's budget") is a plausible claim nobody has checked. This spec
replaces it with something a reader can verify.

### The finding that reframes the question

The realm does not need a top-1000 list to be attacked. **Seven of its twelve
human accounts carry a password that is a mechanical transform of that
account's own username** — public data, printed in the realm import itself
and in `README.md`:

| Username | Password | Rule |
|---|---|---|
| `wall-munich` | `Wall-munich-1234` | `Capitalise(username) + "-1234"` |
| `wall-dresden` | `Wall-dresden-1234` | same |
| `wall-berlin` | `Wall-berlin-1234` | same |
| `wall-hamburg` | `Wall-hamburg-1234` | same |
| `admin` | `Admin1234` | `Capitalise(username) + "1234"` |
| `operator` | `Operator1234` | same |
| `admin@munich.test` | `Admin1234` | `Capitalise(local-part) + "1234"` |

Read off `smart-sentinel-eye-realm.json:371-503` at `b6ddf7df`. The candidate
set an attacker needs for those seven accounts is not a thousand entries. It
is, per account, **on the order of one**, and the policy admits every one of
them: each is ≥ 8 characters with an upper, a lower and a digit.

### And the reuse makes one guess cross a fab boundary

The remaining five accounts are not independent either — they reuse a value
already derived above:

| Distinct password | Accounts it opens | Fab groups reached | Realm roles |
|---|---|---|---|
| `Operator1234` | 6 — `operator`, `op-3@munich.test`, `op-dresden@dresden.test`, `op-multi@smart-sentinel-eye.test`, `op-berlin@berlin.test`, `op-hamburg@hamburg.test` | **4** — `/fabs/munich`, `/fabs/dresden`, `/fabs/berlin`, `/fabs/hamburg` | `user` |
| `Admin1234` | 2 — `admin`, `admin@munich.test` | 1 — `/fabs/munich` | `user`, **`admin`** |
| `Wall-<fab>-1234` | 1 each | 1 each | wall display |

`smart-sentinel-eye-realm.json:459-569`. `op-multi@smart-sentinel-eye.test` is
in **two** fab groups at once (`:542`).

This corrects the issue's own blast-radius sentence. #2510 says a guessed
password yields scopes "for the caller's fab". **It does not stop at one fab.**
One guessed string — `Operator1234`, derivable from the username `operator` —
authenticates six accounts spanning four fabs, and `management-web`'s 24
default client scopes (`:173-198`) ride along on each. Fab isolation is
enforced per token (spec 183, spec 180, the `CrossFab*` suites); it is not
enforced against a password reused across fabs.

### Why this is dev-only and still worth an artefact

There is no production deployment (ADR-0118, constitution §VII), and the dev
realm's passwords are deliberately published — that is the point of a dev
realm, and spec 207 recorded it as assumption 4. So this is not an incident.

It is worth an artefact for the same reason spec 207's lockout was: **the
policy is realm configuration, and the realm import is the only realm this
repository has.** Whatever shape the policy has here is the shape it will have
when someone first stands up a fab. And the decision of what that shape should
be has a real, wide cost that has so far been quoted from memory and
understated — see §*Blast radius*, measured below.

---

## What this spec delivers, and what it deliberately does not

**It delivers an executable answer, not a fix.** Three things:

1. A **build-enforced computation** over the realm import that states, in its
   own assertion, how many seeded passwords the declared policy admits, how
   many are username-derived, and how far one guessed value reaches.
2. A **live measurement**, against a throwaway account on the real stack, of
   how many attempts per hour the lockout actually permits — burst *and*
   paced — because the issue's "well within budget" is arithmetic nobody has
   run against the running server.
3. `verification.md` multiplying (1) by (2) into a **wall-clock figure**, and
   a PR body that puts the cost of raising the policy and the cost of not
   raising it side by side **without picking one**.

**It does not change `passwordPolicy`, and it does not change any seeded
password.** That is not a shortfall; it is the issue's own instruction —
"A human decision on the UX trade-off — this is deliberately **not** something
the autonomous lane decided" — and it matches ADR-0144 §*What the lane may not
do*: "it implements decisions, it does not make them." Spec 214 (#2509) set
this shape a week ago and is the precedent followed here.

**Nothing in this spec ever guesses a real account's password.** It does not
need to: the candidate set is shown to *contain* the real password by string
comparison against values already committed to this repository, which is a
computation, not an attack. The only account that is ever sent a wrong
password is a throwaway the test creates and deletes.

---

## Locked tech choices

Nothing new is chosen. No package, no bounded context, no runtime resource,
no `src/` file.

| Concern | Choice | Where |
|---|---|---|
| Identity provider | Keycloak 26.6.4, one realm per fab | ADR-0007; `src/AppHost/AppHost.cs:143-151` |
| Realm configuration | Declarative import, `WithRealmImport("../AppHost/Realms")` | `src/AppHost/AppHost.cs:151` |
| Lockout mechanism | Keycloak's own realm-level brute-force detector | spec 207 |
| Realm-file guards | `tests/Architecture.Tests`, plain xUnit, no stack | `WallDisplayAccountTests.cs`, `RealmIdentityTests.cs`, `ScopeGrantTests.cs` — six existing classes already read this file |
| Live probing | `AspireFixture` against the real stack, no Testcontainers | ADR-0103 |
| Admin-API access from tests | master-realm `admin` / `admin-cli`, **not** `identity-admin` | spec 207 phase 6: `identity-admin` lacks `view-realm` and silently returns a partial realm representation |
| Assertions | Shouldly; sentence-style test names | ADR-0052, ADR-0053 |

---

## User stories

### US1 (P1, and the whole spec) — the guess a username already made

**As** the person who has to decide whether to raise this realm's password
policy,
**I want** the exhaustibility claim turned into a number I can check, next to
the complete list of what changing it would break,
**so that** I am weighing two measured costs instead of one remembered
sentence against one asserted arithmetic.

One vertical slice: two test files and one verification note. Buildable and
observable end to end on its own; nothing else in the repository has to move.

It splits into two halves because the question has two halves, and they are
answerable by different means:

**Half A — the set. Computed from the realm file; build-enforced.**
`tests/Architecture.Tests/SeededCredentialStrengthTests.cs`. Parses
`passwordPolicy` out of the realm import (never hard-codes it), builds the
predicate that string describes, and asserts over every seeded human
credential:

- every one satisfies the realm's own declared policy,
- how many are `Capitalise(local-part(username)) + suffix` for a named,
  three-entry suffix set,
- for each distinct password, how many accounts it opens and how many fab
  groups those accounts span,
- and a **counterfactual**: a password the predicate must reject, so the
  predicate is not vacuously true.

This is a guard that reads a design artefact, which this repository's own
lesson says proves the design was written down and not that it holds. That is
accepted deliberately: the file *is* the subject here — it is what Keycloak
will be handed — and Half B asks the running server whether it agreed.

**Half B — the rate. Measured against the running stack; excluded from CI.**
`tests/Integration.Tests/Identity/LockoutThroughputMeasurementTests.cs`,
`[Trait("Category", "Measurement")]`. Against a per-fact throwaway user
created and deleted through the Admin API, it records:

- attempts admitted before refusal in a **burst** (no delay),
- attempts admitted before refusal when **paced** above the realm's own
  `quickLoginCheckMilliSeconds`,
- the **measured** wall-clock seconds until the correct password is accepted
  again after a paced lock — not the configured value, the observed one,
- and that the **running realm's** `passwordPolicy` matches the file's, so
  Half A is not guarding a field the import silently dropped.

It asserts the structural claims (a paced attacker is admitted strictly more
attempts per lock than a bursting one; recovery is finite) and **records** the
throughput. It deliberately asserts no threshold on the figure: a threshold is
a policy, and picking one is the decision this spec refuses to make.

### Why there is no US2

A second story would be the remedy, and the remedy is the human's. Splitting
the measurement from the decision is the whole point of the slice.

---

## Acceptance scenarios

Half A runs with no stack. Half B runs against the real stack booted by
`AspireFixture` (ADR-0103).

### The observation — SC-1 and SC-2 are the reason this spec exists

**SC-1 — the policy admits every password the realm seeds.**

```gherkin
Given the realm import's declared passwordPolicy, parsed from the file
  And every password value in the realm import's human user credentials
 When each password is evaluated against that parsed policy
 Then every one of them satisfies it
  And the count of distinct passwords and of credential blocks is stated in
      the assertion message
```

**SC-2 — most of those passwords are already implied by the usernames.**

```gherkin
Given each human account's username and password from the realm import
 When the local part of the username is capitalised and each of the suffixes
      "1234", "-1234", "_1234" is appended
 Then exactly seven of the twelve accounts are matched by one of those
      candidates
  And the seven include both accounts that carry the realm role "admin"
      ("admin" and "admin@munich.test") and all four wall-display accounts
```

**SC-3 — one guessed value crosses fab boundaries.**

```gherkin
Given the distinct password values in the realm import
  And the fab group membership of each account holding each value
 When the accounts are grouped by password value
 Then "Operator1234" is held by six accounts spanning four distinct fab groups
  And "Admin1234" is held by two accounts, both carrying the realm role "admin"
```

### The controls — each rules out a way SC-1 to SC-3 could be lying

**SC-4 (counterfactual on the predicate) — the policy predicate can refuse.**

```gherkin
Given the policy predicate parsed from the realm import
 When it is evaluated against "alllowercase1", "ALLUPPERCASE1", "NoDigitsHere"
      and "Ab1"
 Then it rejects every one of them, naming the clause each failed
```

Without this, SC-1 is satisfied by a predicate that returns `true`
unconditionally. This repository has had five assertions in one week that could
not fail; this is the counterfactual that answers "can the subject change
without the assertion text changing?" — it can: change the realm's policy
string and SC-4's expectations move.

**SC-5 (control on the mint) — the throwaway's own password works first.**

```gherkin
Given a throwaway realm account newly created with a known probe password
 When the token endpoint is sent that account's correct password
 Then a token is issued
```

Without this, every refusal in Half B is indistinguishable from a broken
stack, a mistyped username, or a rejected client.

**SC-6 (the live read) — the running realm agrees with the file.**

```gherkin
Given the stack is booted and Keycloak has imported the realm
 When the realm representation is read through the master-realm admin client
 Then its passwordPolicy equals the string in the realm import file
  And its failureFactor and quickLoginCheckMilliSeconds are present, not
      defaulted
```

Read through **master-realm `admin` / `admin-cli`**, not `identity-admin`:
spec 207's phase 6 found `identity-admin` returns a partial representation
with `failureFactor` missing, and the code silently fell back to Keycloak's
default of 30. A field the import dropped is invisible in the file and visible
only here.

### The measurement

**SC-7 — a paced attacker is admitted more attempts than a bursting one.**

```gherkin
Given two throwaway accounts, each with a known probe password
 When wrong passwords are sent to the first with no delay between them
  And wrong passwords are sent to the second spaced above the realm's own
      quickLoginCheckMilliSeconds
 Then both are eventually refused their correct password
  And the paced account was admitted strictly more attempts before refusal
      than the bursting one
  And both attempt counts are recorded in the test output
```

This is the falsifiable claim. **Hypothesis, formed before the run and
explicitly not the answer:** the bursting account trips
`quickLoginCheckMilliSeconds: 1000` at **2** failures — spec 207's
`verification.md` observed exactly `numFailures: 2` — while the paced account
should reach `failureFactor: 10`. **Named falsifier:** if the paced account
also locks at 2, the quick-login check is not what spec 207 concluded it is,
and that is a finding about spec 207, recorded, not adjusted away.

**SC-8 — the lock ends, and the test measures when.**

```gherkin
Given a paced throwaway account that has just been refused its correct password
 When the correct password is retried on a poll until it is accepted
 Then it is eventually accepted
  And the elapsed wall-clock seconds are recorded
```

Spec 207 never waited a lock out — it always cleared the record through the
Admin API, so `minimumQuickLoginWaitSeconds: 60` is a configured value this
repository has never observed. This is the first task that watches it expire.
The poll is bounded by `maxFailureWaitSeconds` (900 s) plus a margin, and a
timeout is a failure, not a skip.

**SC-9 (blast-radius control) — the probe does not poison the suite.**

```gherkin
Given a throwaway account is locked out
 When a seeded account authenticates with its correct password
 Then a token is issued
```

Spec 207's SC-4 in a new file. It also protects every other test in the
`Aspire` collection: if this ever fails, the probe is locking the realm rather
than an account.

### Bad request

```gherkin
Given the realm has brute-force protection enabled
 When the token endpoint is sent a grant with no username at all
 Then it answers with error "invalid_request", never "invalid_grant"
  And no throwaway account's failure counter moves
```

Corrected in advance from spec 207's live finding: at Keycloak 26.6.4 this
shape answers **401**, not the RFC 6749 §5.2 `400`. The load-bearing claim is
the `error` value, not the status code. A malformed request must not be
counted as a failed login, or the measured throughput in SC-7 is measuring
parse errors.

### Auth / scope

```gherkin
Given the realm representation and a throwaway user's attack-detection record
 When they are read by a caller holding no realm-management role
 Then the Admin API refuses
```

Not a new guarantee — Keycloak's own — but recorded so the next reader knows
*why* SC-6 can read the realm back, and so that narrowing the master-realm
admin's roles shows up here rather than as a mystery.

### The conflict case

There is no write-conflict surface: no endpoint, no aggregate, no `If-Match`,
no `Idempotency-Key`. The nearest thing is two probes contending for the same
realm. Three mechanisms make that impossible rather than unlikely:

- every probe account is `$"policy-probe-{Guid.NewGuid():N}"`, created and
  deleted inside the fact (spec 207's pattern) — never a seeded account;
- the class carries `[Collection(AspireCollection.Name)]`, and xUnit does not
  parallelise within a collection, so every lockout-touching test in
  `Integration.Tests` is already serialised against the one realm;
- **no task in `tasks.md` is `[P]` across Half B's facts**, for the same
  reason spec 207's `tasks.md` gave: not file ownership, a shared resource.

---

## Blast radius of raising the policy — re-measured, not quoted

This is the cost side of the human decision, and it is the part #2510 and
spec 207 both understated. Both said "`README.md`, nine `specs/*/quickstart.md`,
`AspireFixture.AdminPassword`, every `e2e/support/*.ts` sign-in helper".

**Measured at `b6ddf7df` (this branch's merge base), by searching for the six
literal seeded passwords** — `Admin1234`, `Operator1234`, `Wall-munich-1234`,
`Wall-dresden-1234`, `Wall-berlin-1234`, `Wall-hamburg-1234`:

| Area | Files | Occurrences | Against the earlier claim |
|---|---|---|---|
| `src/AppHost/Realms/smart-sentinel-eye-realm.json` | 1 | 12 | 12 credential blocks, `:371-565` |
| `tests/` | **43** | **60** | Not one `AspireFixture` constant — see below |
| `e2e/` | **8** | 8 | **Only 2 are in `e2e/support/`** — see below |
| `scripts/` | **3** | 3 | **Missed entirely by both earlier lists** |
| `README.md` | 1 | 2 | Holds — lines 79 and 350, both `Admin1234` |
| `specs/**/*.md` | **41** | **73** | Not nine quickstarts — see below |
| **Total** | **97** | **158** | |

Reproduce it (PowerShell, from the repository root):

```powershell
$pat='Admin1234|Operator1234|Wall-munich-1234|Wall-dresden-1234|Wall-berlin-1234|Wall-hamburg-1234'
Get-ChildItem -Recurse -File | Select-String -Pattern $pat -AllMatches |
  ForEach-Object { $_.Matches.Count } | Measure-Object -Sum
```

**Four corrections that change the size of the decision, each measured:**

1. **`tests/` is not one constant.** `AspireFixture.AdminPassword`
   (`tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs:11`) is real and
   `public`, but it is one of **36** password constant declarations:
   **34** files independently declare their own
   `private const string OperatorPassword = "Operator1234"` — there is **no
   central constant for the operator password at all** — and `AdminPassword`
   is declared **twice** (the fixture's, plus a private copy at
   `tests/Integration.Tests/AuditObservability/RunModeStackAddress.cs:39`).
   The remaining **24** occurrences are inline literals, 13 of them in
   `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`
   alone.

2. **`e2e/` is not "`support/*.ts` sign-in helpers".** Only **2** of the 8
   affected files are under `e2e/support/` (`sign-in.ts`, `kiosk-session.ts`).
   The other **6** hold their own literals directly, because ADR-0109 file
   contention led spec 214 and its neighbours to copy helpers rather than
   import them: `wall-authority.spec.ts`,
   `wall-outlives-its-session.spec.ts`,
   `wall-survives-a-process-death.spec.ts`, `wall-survives-a-lockout.spec.ts`,
   `wall-withdrawal.spec.ts`, `kiosk-reconciliation.spec.ts`.

3. **`specs/` is 41 files, not nine quickstarts.** The "nine" is *exactly
   right* as a count of `quickstart.md` — there are 9 — but it is a count of
   the wrong category: only 9 of the 41 are quickstarts. The rest are
   `spec.md`, `plan.md`, `tasks.md` and
   `verification.md` across 30-odd features. Most are historical records that
   *should not* be rewritten when a password rotates; the point of counting
   them is that a naive find-and-replace would touch them, and a careful one
   has to decide which.

4. **`scripts/` was invisible to both earlier lists.** Three files quote
   `Wall-munich-1234` while documenting the Playwright trace-redaction
   subsystem: `scripts/scrub-playwright-artifacts.mjs`,
   `scripts/fixtures/trace-redaction/sentinels.mjs`,
   `scripts/fixtures/trace-redaction/fixture-source-literal.spec.ts`. These
   are **not live credentials** — they are sentinel values and comments — but
   a rotation that misses them leaves the redaction fixtures documenting a
   password that no longer exists, which is exactly the drift this repository
   keeps correcting.

**One further fact the decision needs:** **no seeded realm-user password is
read from an environment variable or configuration anywhere.** All six are
hard-coded literals at every one of the 158 sites. This is unlike the Keycloak
*container* admin password, which **is** an overridable AppHost parameter
(`src/AppHost/AppHost.cs:30`, default `dev-only-keycloak-admin`; the fixture
overrides it to `testkeycloak` at `AspireFixture.cs:312-318`). So "make it
configurable first" is available as a remedy and is not a thing the repository
already does — which is itself worth the human knowing.

---

## Independent end-to-end test procedure

Reproducible by hand, without the test suite, by someone who does not trust it.

1. **Compute the set, from the file.** Open
   `src/AppHost/Realms/smart-sentinel-eye-realm.json`. Read `:20`
   (`passwordPolicy`) and `:371-569` (the twelve human `credentials` blocks).
   For each account, capitalise the first letter of the username's local part
   and append `1234` or `-1234`. Compare to the stored password. **Seven
   match.** Check each stored password against the policy: ≥ 8 characters,
   ≥ 1 upper, ≥ 1 lower, ≥ 1 digit. **All six distinct values pass.**
2. **Drop the Keycloak volume.** `docker rm` the persistent `keycloak`
   container and remove the `*keycloak*` volumes. `WithRealmImport` runs only
   against a fresh volume; skipping this gives a stack that looks perfectly
   healthy and is still running the old realm.
3. **Boot.** `aspire run` from `src/AppHost`; wait for `keycloak` healthy.
4. **Read the realm back from the server, not the file.** Mint a master-realm
   token (`grant_type=password&client_id=admin-cli&username=admin&password=dev-only-keycloak-admin`
   against `/realms/master/protocol/openid-connect/token`) and
   `GET /admin/realms/smart-sentinel-eye`. Confirm `passwordPolicy` matches
   step 1's string **in the response**.
5. **Create a throwaway.** `POST /admin/realms/smart-sentinel-eye/users` with
   `username`, `enabled`, **and `firstName`, `lastName`, `email`** — all three
   are required by the realm's declarative User Profile, and a user missing
   them fails the token endpoint with `invalid_grant` / "Account is not fully
   set up", which reads exactly like a lockout. Then
   `PUT .../users/{id}/reset-password` with a policy-satisfying value.
6. **Positive control.** Password-grant the throwaway
   (`client_id=management-web`). Expect `200`.
7. **Burst.** Repeat with a wrong password as fast as the network allows until
   the *correct* password is refused. **Record the attempt count.** Read
   `GET .../attack-detection/brute-force/users/{id}` — expect
   `disabled: true`.
8. **Paced.** Make a second throwaway. Repeat step 7 with **≥ 1.1 s** between
   attempts. **Record the attempt count.** Expect it to exceed step 7's.
9. **The measurement nobody has taken.** With the paced account still locked,
   retry the correct password once every 5 s and **record the wall-clock
   seconds until it is accepted**. Do not `DELETE` the attack-detection record
   this time — that is what makes this step different from spec 207's.
10. **Blast radius.** While a throwaway is locked, password-grant `admin` /
    `Admin1234`. Expect `200`.
11. **Clean up.** `DELETE .../users/{id}` for both throwaways. Deleting the
    user disposes of its lock with it.
12. **The figure.** attempts-per-lock (step 8) ÷ (step 9's seconds + the time
    step 8's attempts took) × 3600 = **sustainable attempts per hour**. Divide
    the candidate-set size from step 1 by it.

**Negative control on the whole procedure:** run steps 5–7 with the *correct*
password every time. Step 7 must never refuse. If it does, something other
than the lockout is refusing and the procedure is not measuring what it claims.

---

## Latency budget

**N/A.** No leg of the event-to-overlay path is touched (constitution §IV).
This spec adds two test files and a note; it changes no `src/` file, no realm
field, and nothing on the
camera → SFU → decode → presentation buffer → event → overlay → composite
path. No token is minted per frame.

Half B is `[Trait("Category", "Measurement")]` and is excluded from CI's
integration job, so it adds nothing to CI wall time either — see
§*File contention*.

---

## Out of scope — each with the reason and the successor

| Not doing | Why | Where it goes |
|---|---|---|
| **Raising `passwordPolicy`** | The issue reserves it: "A human decision on the UX trade-off — this is deliberately **not** something the autonomous lane decided." ADR-0144: the lane "implements decisions, it does not make them." This spec's whole output is the input to that decision | **#2510 stays open**; a human decides, with §*Blast radius* and `verification.md` in hand |
| **Rotating any seeded password** | Same decision, same reason, and the 158 sites in §*Blast radius* are its cost | with the above |
| **Adding `passwordBlacklist(...)`** | This is arguably the *correct* remedy for the finding — a username-derived password is not caught by any length or character-class rule, and only a blacklist or `notUsername(undefined)` would refuse `Admin1234`. Recorded because it is not obvious from the issue, and because a reviewer choosing "raise length to 12" would not fix the thing this spec measured. Still a human's choice, and a blacklist file is new deployed surface | with the above; named in the PR body as a candidate |
| **Making seeded passwords configurable** | A real alternative remedy (nothing reads them from config today — §*Blast radius*). It is a harness change across 97 files and its own design decision | a new issue if the human picks it; not filed by this PR |
| **`directAccessGrantsEnabled: false` on `management-web`** | `AspireFixture.ClientId` **is** `management-web`; flipping it fails the whole integration suite | **#2511** (`agent:blocked`) |
| **The Keycloak `master` realm's missing lockout** | Higher-value target than anything here; needs a second partial import or a startup Admin API call | **#2508** (`agent:blocked`) |
| **Gateway rate limiting** | The standard mitigation for the rate half, at a different layer. Not speculative generality — the risk is real — but its own design decision | — |
| **Tuning `quickLoginCheckMilliSeconds` / `minimumQuickLoginWaitSeconds`** | Would directly weaken the control spec 207 added; ADR-0144 forbids weakening a gate | spec 214 already recorded this refusal |
| **Any production realm** | There is none (ADR-0118, constitution §VII) | — |
| **Vendoring a top-N common-password wordlist** | Considered and dropped. The issue frames the risk as "top-1000 list", but §*Problem* shows the realm's own usernames generate seven of its twelve passwords, so a wordlist would *weaken* the evidence by replacing a set of size ~1 with one of size 1000. It would also put a password wordlist in the repository, trip secret scanners, and need a provenance and licence argument for a claim already proven without it | not needed; recorded so the next reader knows it was weighed |

---

## File contention

| File | This spec | Anyone else |
|---|---|---|
| `tests/Architecture.Tests/SeededCredentialStrengthTests.cs` | **new** | none — checked against all three worktrees and the one open PR (#2535) |
| `tests/Integration.Tests/Identity/LockoutThroughputMeasurementTests.cs` | **new** | none; spec 207's `BruteForceLockoutIntegrationTests.cs` and spec 214's `LockoutSessionSurvivalIntegrationTests.cs` are **not** edited |
| `tests/Integration.Tests/Identity/RealmProbe.cs` | **not touched** | shared with `KeycloakAdminTokenProviderTests` and `MqttAudienceIntegrationTests`; specs 207 and 214 both declined to widen it, and the stated rule is "a third consumer is the moment to promote a helper, not the first". This would be the *third* lockout consumer — see `plan.md` §3, which still declines, with a reason |
| `tests/Integration.Tests/ci-shards/shard-*.filter` | **not touched, deliberately** | PR #2535 (spec 218) adds them. See below — this is the load-bearing bit |
| `src/AppHost/Realms/smart-sentinel-eye-realm.json` | **not touched** | — |
| `specs/219-the-guess-a-username-already-made/` | new directory | — |

**Why Half B needs no shard entry, and the falsifier if that is wrong.**
PR #2535 partitions `Integration.Tests` into four committed
`FullyQualifiedName~…` filter files, kept honest by an
`integration-shard-coverage` job that fails if the union stops matching the
discovered test set. Discovery is run **with** `ci.yml`'s literal
`Category!=Measurement&Category!=Disruptive&Category!=Maintenance` exclusion
(`tests/Integration.Tests/ci-shards/README.md`, and
`IntegrationTestSelectionTests.cs:765`), so a class traited `Measurement` is
not in the discovered set and needs no shard entry. **Falsifier:** if
`integration-shard-coverage` goes red naming
`LockoutThroughputMeasurementTests`, that reasoning is wrong — add the class
to the smallest shard file and say so, rather than re-traiting it into CI,
because its recovery poll can take 15 minutes of real waiting.

**Other worktrees at the time of writing:** `2517-null-fab-two-meanings`
(spec 217 — `tests/Integration.Tests/AuditObservability/`,
`tests/Architecture.Tests/EventMetadataFabDeclarationTests.cs`) and
`218-ci-shard-slow-jobs` (spec 218 — `.github/workflows/ci.yml`,
`tests/Integration.Tests/ci-shards/`). No overlap with either.

---

## Success criteria

| ID | Criterion | How it is known |
|---|---|---|
| SC-1 | Every seeded human password satisfies the realm's own declared policy; the counts are in the assertion | `SeededCredentialStrengthTests`, CI-enforced |
| SC-2 | Exactly seven of twelve accounts have a username-derived password | same |
| SC-3 | `Operator1234` opens 6 accounts across 4 fab groups; `Admin1234` opens 2, both `admin`-roled | same |
| SC-4 | The parsed policy predicate rejects four named non-conforming strings | same — the counterfactual |
| SC-5 | A throwaway's correct password mints before any wrong guess | `LockoutThroughputMeasurementTests` |
| SC-6 | The **running** realm's `passwordPolicy` equals the file's, read through the master-realm admin | same |
| SC-7 | A paced attacker is admitted strictly more attempts before refusal than a bursting one; both counts recorded | same |
| SC-8 | A lock expires on its own, and the elapsed seconds are recorded | same — first time in this repository |
| SC-9 | A seeded account authenticates while a throwaway is locked | same |
| SC-10 | The blast-radius figures in this spec reproduce from the command given | phase 5, re-run and quoted in `verification.md` |
| SC-11 | `verification.md` states a **wall-clock time to exhaust the candidate set**, derived from SC-2's set size and SC-7/SC-8's measured rate | phase 5 |
| SC-12 | The PR body presents both costs and **picks neither**, and #2510 stays open | phase 7 |
| SC-13 | The realm is left as it was found: no seeded password changed, no policy changed, no probe account or lock left behind | phase 5, re-read after the run |

---

## Assumptions, marked

1. **G1 — `Capitalise(local-part) + "1234"` is a real attacker rule, not one
   invented to fit.** It is the canonical example of a "capitalise and append
   digits" mangling rule (the shape every published wordlist-mangling ruleset
   opens with). Marked as a guess about attacker behaviour; **it does not
   affect the finding's arithmetic**, because the finding is that the
   candidate set is *derivable from public data*, and any reader may substitute
   their own rule set and recount. SC-2's suffix list is written out in the
   test so the assumption is visible rather than buried.
2. **G2 — the paced attempt interval that avoids the quick-login check is
   `quickLoginCheckMilliSeconds` + a margin.** Read off the *running* realm,
   not hard-coded. If pacing at 1.1 s still locks at 2 failures, G2 is false
   and SC-7's falsifier fires. Not verified against 26.6.4 before the run.
3. **G3 — the delivery machine may not be able to boot Aspire.** This has
   happened repeatedly (free RAM; spec 214 settled its facts on CI instead).
   `tasks.md` is written so Half A alone is a complete, CI-verifiable
   deliverable and Half B's figure can be produced in a later, separate run —
   but SC-11 is not met until that run happens, and a spec that ships without
   it must say so rather than quote the arithmetic.
4. **G4 — `identity-admin` cannot read the realm's `passwordPolicy`.** Spec
   207's phase 6 found it returns a partial representation. Assumed to apply
   to `passwordPolicy` as it did to `failureFactor`; SC-6 uses the master-realm
   client for that reason. If `identity-admin` turns out to suffice, that is a
   simplification, not a defect.
5. **G5 — the dev realm's published passwords stay published.** The point of a
   dev realm (spec 207 assumption 4). This spec measures what that costs; it
   does not propose ending it.
