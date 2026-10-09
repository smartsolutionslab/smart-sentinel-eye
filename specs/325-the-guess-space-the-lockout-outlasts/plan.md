# Plan 325 — The guess space the lockout outlasts

**Spec:** [spec.md](spec.md) · **Issue:** #2510 · **Phase:** 2 (Plan)

## 1. Context, boundaries, ADR position

- **Bounded context:** none. Realm configuration (`src/AppHost/Realms/`) plus test, e2e and doc
  consumers. No Domain/Application/Infrastructure/Api code, no entity, no value object, no
  message, no `Shared.Contracts` change, no cross-context reference.
- **No new ADR.** The direction is a recorded human decision (issue comment 2026-10-08); this
  plan only computes values. Precedent: spec 207 (lockout) and spec 324 (master-realm lockout)
  changed realm security configuration without an ADR. If this policy is later adopted as the
  *production* fab template, that adoption is a decision and wants an ADR — noted, not made.
- **Lane limits respected:** no clause of the current policy is removed (removing composition
  rules, though NIST SP 800-63B-4 discourages them, would read as weakening a gate); the lockout
  block is untouched; no test is deleted without an assertion of equal or greater strength
  replacing it (§5).

## 2. The arithmetic — why `length(15)` + classes + username/email clauses

### 2.1 The attacker's budget (measured, spec 219 verification.md)

| Regime | Attempts per lock | Lock | Rate per account |
|---|---|---|---|
| burst (< `quickLoginCheckMilliSeconds`) | 2 | ≈61 s | ≈110/h |
| paced, first lock | 10 | ≈61 s (+10.85 s loop) | ≈500/h |
| paced, sustained (wait grows +60 s/lock to 900 s) | 10 | 900 s | **≈40/h** |

`maxDeltaTimeSeconds: 43200` only forgets failures after 12 h of *silence*, so a continuing
attacker sits at the sustained rate indefinitely:

| Window | Guesses / account | × 12 accounts in parallel |
|---|---|---|
| 12 h | ≈475 | ≈5 700 |
| 30 days | ≈28 000 | ≈340 000 |
| 1 year | **≈346 000** | **≈4.2 M** |

A secret shared by *k* accounts faces *k* independent counters: `Operator1234` (k = 6) faces
≈2.1 M guesses/year.

### 2.2 The target

Probability that a sustained, undetected attacker compromises **any** of the twelve accounts in
one year ≤ 10⁻³. With distinct secrets: required per-account search space for the cheapest
*common* compliant shape S ≥ 12 × 346 000 / 10⁻³ ≈ **4 × 10⁹**.

### 2.3 What each policy forces the cheapest common shape to be

Model: users pick the shortest shape the policy admits; the attacker enumerates that shape from a
cracking dictionary. Dictionary sizes are order-of-magnitude assumptions (A2): ~2 × 10⁴ common
words; ~10 common specials; digit tails of 1–4 digits.

| Policy | Cheapest common shape | Space S | Years to exhaust, 1 account (346 k/yr) |
|---|---|---|---|
| **current** `length(8)+U+L+D` | `Word` (≥ 7 letters, capitalised) + 1–4 digits; the head is `Word + {1,12,123,1234,2024}` | 2 × 10⁴ × 5 ≈ **10⁵** (head) … 2 × 10⁸ (all tails) | **0.3** (head) … 600 |
| current, seeded values | `Capitalise(username) + 1234` | **≈1–3** per account | seconds (spec 219: one lock cycle) |
| `length(12)+U+L+D+S` | `Word + "-" + digits` or `Word + "!"` with a 9–10-letter word | ≈ 2 × 10⁴ × 10 × 10 ≈ 2 × 10⁶ | ≈6 → fails 4 × 10⁹ |
| **`length(15)+U+L+D+S`** | either two words (`Word-Word7`, 13 letters across two words) or one ≥ 12-letter word + special + digit | two-word: (2 × 10⁴)² × 10 × 10 ≈ **4 × 10¹⁰**; single long word: see below | two-word: ≈10⁵ years |
| new, seeded values | 3 dictionary words + 2 digits, `-` separated, published-but-random choice | ≈ (2 × 10⁴)³ × 100 ≈ 10¹⁵ | n/a — dev values are published by design (spec 207 assumption 4); what matters is the *shape* the realm accepts |

**The residual case, stated rather than hidden:** a single ≥ 13-letter dictionary word
(`Semiconductor-1`, which a fab will think of) remains a ~10⁶–10⁷ space at length(15). No
length or composition rule removes a determined single-word choice — only a blocklist does, and
`passwordBlacklist` is out of scope (spec §Out of scope, needs a file under the shadowed
`/opt/keycloak/data`). It is therefore filed as the named follow-up, not claimed closed.

**Why not length(20)?** It does not fix the residual case either (the ≥ 18-letter single-word
space is *smaller*, not larger), and it doubles typing cost for kiosk/wall sign-in. 15 is the
smallest length at which the cheapest *common* shape becomes multi-word, and it matches NIST SP
800-63B-4's single-factor minimum.

**Why `specialChars(1)`:** removes the entire `Word+digits` family — the head of every
policy-filtered breach list — at zero cost to any seeded value (`-` is already in all of them).
Reproduction method for the breach-list proportion (A2): filter any public breach list through
the predicate `SeededCredentialStrengthTests.ParsePolicy` already builds, once with the old
string and once with the new, and count survivors; not run here, and not claimed as measured.

**Why `notContainsUsername` and `notEmail`:** they are the only Keycloak clauses that touch
spec 219's finding directly — the attacker's cheapest candidate is derived from public username
data. `notContainsUsername` (verified: lower-cases both, then `contains`) refuses
`Wall-munich-1234` for `wall-munich` and `Admin1234` for `admin`. It does **not** refuse
`Admin1234` for `admin@munich.test` (the username is the whole email); `length(15)` does. It
**supersedes** `notUsername`, so `notUsername` is not added.

## 3. The seeded values

Alphabet `[A-Za-z0-9-]` only: safe unquoted in `curl -d password=…` (quickstart 029), JSON,
C# and TS string literals, PowerShell and bash. No word names a role, fab or username.

| Username | New password |
|---|---|
| `wall-munich` | `Copper-Lantern-Drift-47` |
| `wall-dresden` | `Maple-Orbit-Canvas-82` |
| `wall-berlin` | `Quartz-Harbor-Ember-19` |
| `wall-hamburg` | `Velvet-Summit-Prism-63` |
| `admin` | `Granite-Willow-Beacon-58` |
| `operator` | `Cobalt-Meadow-Ripple-24` |
| `admin@munich.test` | `Saffron-Glacier-Tundra-71` |
| `op-3@munich.test` | `Hazel-Compass-Thistle-36` |
| `op-dresden@dresden.test` | `Indigo-Falcon-Pebble-90` |
| `op-multi@smart-sentinel-eye.test` | `Juniper-Anchor-Mosaic-15` |
| `op-berlin@berlin.test` | `Marble-Cinder-Lagoon-43` |
| `op-hamburg@hamburg.test` | `Amber-Trellis-Nimbus-68` |

All 21–25 chars, ≥ 1 upper / lower / digit / special, none contains its username
(case-insensitive) or equals its email — checked at phase 2 by script, enforced by §5 test 1.

## 4. Blast radius (measured at `dc88224c`, repo-wide search for the six retired literals)

| Area | Files | Change |
|---|---|---|
| `src/AppHost/Realms/smart-sentinel-eye-realm.json` | 1 (policy `:20` + 12 credential blocks `:367-553`) | values |
| `tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs:11` | 1 — `AdminPassword` (referenced by ~29 files through the constant; they need no edit) | value |
| **New** `tests/Integration.Tests/Fixtures/SeededCredentials.cs` | 1 — one `public const string` per seeded account | new |
| Other `tests/Integration.Tests/**/*.cs` holding a literal | **54** files (list: `git grep -lE 'Admin1234\|Operator1234\|Wall-[a-z]+-1234' -- tests`) | replace the local literal/const with the account's `SeededCredentials` member. **Files that share one `OperatorPassword` const between two users (e.g. `op-dresden` + `op-multi`) must split it** — D1 makes them differ |
| `tests/Integration.Tests/Identity/{BruteForceLockout,LockoutSessionSurvival,LockoutThroughputMeasurement}IntegrationTests.cs` | 3 — probe passwords (17/17/20 chars, already compliant under the new policy); comments and the reset-password failure messages quote the old policy string | text only |
| `tests/Architecture.Tests/SeededCredentialStrengthTests.cs` | 1 | test logic (§5) |
| `e2e/support/sign-in.ts:16`, `e2e/support/kiosk-session.ts:22`, `e2e/kiosk-reconciliation.spec.ts:71` (`operator`), `e2e/support/wall-session.ts:20` (`WALL_PASSWORD`, `wall-munich`) | 4 | values |
| `README.md:79`, `:352` (both `Admin1234`, for `admin` and `admin@munich.test` — now **different** values) | 1 | values + one sentence: an existing persistent stack must drop `keycloak-data` to get the new credentials |
| `specs/{013,014,029,030,041,042,043,044,051}-*/quickstart.md` | 9 (13 occurrences) | values |
| **Not touched:** historical `specs/*/{spec,plan,tasks,verification}.md` (≈60 files), `scripts/scrub-playwright-artifacts.mjs:451`, `scripts/fixtures/trace-redaction/{sentinels.mjs:16,fixture-source-literal.spec.ts:17}` (comments quoting a deleted file; the scrubber matches by shape, never by value) | — | — |

Beyond the issue's list: 54 integration-test files, 2 non-`support/` e2e sites
(`kiosk-reconciliation.spec.ts`), the architecture test, and three probe-comment sites. No hit in
`src/` outside the realm, `apps/`, `docs/`, `.github/`, `deploy/`.

**Why a `SeededCredentials` class, not 54 literal swaps:** D1 makes each account's value
different, so most files' single `OperatorPassword` const becomes wrong *by construction* and has
to be split anyway; pointing at one catalogue costs the same edits and makes the next rotation a
one-file change. It mirrors the existing pattern (`AspireFixture.AdminPassword`,
`RealmProbe.AdminClientSecret` — spec 191 decision 4: constants on a test-infra type, not a
realm-file reader). `AspireFixture.AdminPassword` stays (29 referrers) and is re-pointed to
`SeededCredentials.Admin`.

## 5. Tests — what is red-first and what is data

**Colour: red** (behaviour-changing: the realm refuses passwords it admitted). But most of the
diff is data; only these assertions are new behaviour.

**Red first (phase 4a, test-writer):**

1. `tests/Architecture.Tests/SeededCredentialStrengthTests.cs` — spec 219's characterisation of
   the defect, **inverted** (each replaced fact is at least as strong as the one it replaces, so
   this is not gate-weakening):
   - census: 12 credential blocks, **12** distinct passwords (red today: 6);
   - `Seven_of_the_twelve…` → **zero** username-derived passwords (red today: 7);
   - both literal-pinned SC-3 facts and both value-agnostic SC-3 facts → one fact: **no seeded
     password is held by more than one account** (red today: one held by 6);
   - new: **every retired value is refused by the declared policy** — the six old values, built
     by the file's existing `Capitalise(...) + suffix` rule, not typed as literals (red today:
     the current policy admits all six);
   - new: **declared minimum length ≥ 15**, and the policy contains `specialChars`,
     `notContainsUsername`, `notEmail` (red today);
   - `ParseClause` models `notContainsUsername` (case-insensitive contains) and `notEmail`
     (case-insensitive equality; `SeededAccount` gains `Email`), and `notUsername` is corrected
     to `OrdinalIgnoreCase` to match the jar's `equalsIgnoreCase`;
   - SC-4 counterfactuals rebuilt so each is ≥ 15 chars and fails exactly one clause (today's
     `"Ab1"`-style probes would all fail `length` first under the new string);
   - SC-1 (every seeded password satisfies the declared policy) **kept unmodified** — green
     before and after; it is the guard for A1.
2. New `tests/Integration.Tests/Identity/PasswordPolicyEnforcementIntegrationTests.cs`
   (`[Collection(AspireCollection.Name)]`, master-realm `admin`/`admin-cli`, reusing `RealmProbe`
   and the probe-lifecycle pattern of `BruteForceLockoutIntegrationTests`): AS-1, AS-2 (three
   refusals + the control that the compliant probe password still mints), AS-3. Red today: AS-1
   and all three AS-2 resets answer 204 under the current policy (each AS-2 value is built to
   satisfy every *current* clause, so its only possible refusal is a new clause). **AS-3 is green
   before and after** — a drift control, not new behaviour: file and running realm agree on the
   old string today and must agree on the new one after. Add the class to
   `tests/Integration.Tests/ci-shards/shard-2.filter` (beside the other lockout classes).
3. New `tests/Architecture.Tests/RetiredSeededPasswordTests.cs` (AS-6): scans `README.md`,
   `specs/*/quickstart.md`, `tests/`, `e2e/`, `src/` (excluding `bin/`, `obj/`, `node_modules/`;
   paths normalised to `/`) for the six retired values, built from the same rule. Red today.

**Data, no new test (phase 4b):** realm credential values, `SeededCredentials` + 54 test files,
4 e2e sites, README, 9 quickstarts, probe comments. Their correctness is proven by the existing
suites going green on a **fresh** `keycloak-data` volume (a missed consumer is a 401 in its own
test), plus test 3's zero count.

**Counterfactuals (phase 5, prove the guards):** revert one seeded value to `Operator1234` →
tests 1 (policy refuses / duplicate) and 3 fail; set the policy back to `length(8)…` → test 1's
length and retired-value facts fail.

## 6. Sequencing — one atomic change

Policy, seeded values and every consumer land in **one `fix(2510)` commit**: a realm whose
seeded passwords disagree with its consumers fails every integration and e2e sign-in, and a
policy stricter than its seeded values may break the realm import (A1). Commit order in the PR:

1. `test(identity): prove the dev realm admits passwords its lockout cannot outlast` (4a, red; builds).
2. `fix(2510): raise the dev realm password policy and reseed distinct credentials` — realm,
   `SeededCredentials`, all test/e2e consumers, probe comments.
3. `docs(2510): update seeded dev credentials in README and quickstarts` (build-neutral; may fold
   into 2).

## 7. Fallback if D1 is overruled (rotate, keep reuse)

Six values instead of twelve; `SeededCredentials` collapses to six members; the
"no password held by more than one account" fact is replaced by spec 219's value-agnostic
"widest password spans 4 fabs / 6 accounts", left asserting the defect — which spec 219's
phase-6 review identified as the trap. Not recommended.

## 8. Verification (phase 5)

Fresh volume; AS-1–AS-6 quoted; manual procedure from spec §Independent e2e; record A1
(does the import validate?) by importing once with a deliberately non-compliant seeded value on a
throwaway volume and observing whether Keycloak starts — then discard. Full Integration.Tests and
e2e green. Latency: N/A.
