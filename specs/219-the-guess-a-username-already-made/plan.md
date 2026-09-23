# Plan 219 — The guess a username already made

**Spec:** `specs/219-the-guess-a-username-already-made/spec.md`
**Issue:** #2510 · **Branch:** `2510-password-policy-exhaustibility` ·
**Worktree:** `D:\Github\sse-2510` · **Merge base:** `b6ddf7df`

---

## 0. Where this lands in the architecture — and why that section is short

**No bounded context is touched.** No `src/` file changes. There is no
aggregate, no value object, no repository, no command, no query, no domain
event, no integration event, no migration, no Aspire resource, no DI
registration, no endpoint.

| Layer | Change |
|---|---|
| `Domain` | none |
| `Application` | none |
| `Infrastructure` | none |
| `Api` | none |
| `Shared.Kernel` / `Shared.Contracts` | none |
| `AppHost` | none — **the realm import is explicitly not edited** |
| `apps/*` | none |
| `tests/` | two new files (§2, §3) |

**Boundary rules are therefore satisfied vacuously**, but named so a reviewer
does not have to infer it: no cross-context project reference is added
(constitution §III, NetArchTest); nothing crosses a context boundary other
than through `Shared.Contracts`, because nothing crosses one at all.

Two house rules that *do* bind, because the new code is C#:

- **Primitives.** `PrimitiveBoundaryTests` binds domain models. Neither new
  file declares a domain type; both are test classes handling `string`
  usernames and passwords, which is the vocabulary of the wire and of a JSON
  file — the same reason constitution §II exempts `Shared.Contracts`. No
  value object is introduced, and introducing one (a `SeededPassword` record)
  would be speculative generality (ADR-0036) for a two-file test.
- **Guards.** `Ensure.That(x).IsNotNull()` (ADR-0105) where a helper takes a
  reference argument. Not `ArgumentNullException.ThrowIfNull`.
- **Collections** are declared `List<T> x = [];` (ADR/`.editorconfig`,
  `dotnet_style_prefer_collection_expression` at `warning` → fails Release).
- **`CancellationToken` last, mandatory** on every async helper (ADR-0049);
  no `ConfigureAwait`.
- **Private fields carry no leading underscore.**

---

## 1. The two halves, and why the split is the design

The question "is the policy exhaustible" has a numerator and a denominator,
and they are knowable by different means. Putting them in one test file would
force the cheap, deterministic half to pay the slow half's cost — a class that
needs the stack is read only by the 30-minute integration job
(`IntegrationTestSelectionTests`), and one that waits out a 15-minute Keycloak
lock cannot run in CI at all.

| | Half A — the set | Half B — the rate |
|---|---|---|
| Question | how many candidates must an attacker try? | how many may they try per hour? |
| Source of truth | the realm import file | the running Keycloak |
| Project | `tests/Architecture.Tests` | `tests/Integration.Tests` |
| Stack needed | no | yes |
| Runs in CI | **yes**, every push | **no** — `[Trait("Category","Measurement")]` |
| Runtime | milliseconds | minutes, one fact up to ~16 |
| Phase-4a colour | characterisation, observed **green** + counterfactual | characterisation, observed **green** + counterfactual |
| Answers | SC-1, SC-2, SC-3, SC-4 | SC-5 … SC-9 |

`verification.md` is where the two multiply (SC-11).

### Phase-4a colour, declared here so phase 4 does not have to decide

**Both halves are CHARACTERISATION, OBSERVED GREEN, each with a mandatory
counterfactual.** CLAUDE.md resolves ambiguity to red; this is not ambiguous,
and the reason is worth writing down rather than asserting:

- Red is the obligation for *new behaviour*. This delivery changes no
  behaviour — there is no `src/` edit at all. A test written to fail first
  would have to fail against a system nobody is going to change, which means
  writing an assertion known to be false and then correcting it. That is not
  evidence of anything.
- Characterisation's own hazard is the opposite one: a test that passes
  because it cannot fail. This repository has had five such assertions in one
  week. So each half carries an explicit counterfactual task (T004, T007)
  that *constructs* the thing the assertion claims to catch and shows it
  caught — the only form of this evidence a later reader can check.
- **Spec 214 (#2509) is the precedent**, declared identically: "Phase-4a
  colour: CHARACTERISATION, OBSERVED GREEN, with a mandatory counterfactual",
  and "Phase 4b explicitly skipped — there is no production code to write".
  Phase 4b is skipped here for the same reason, stated in the PR body per
  ADR-0037's skip rule.

---

## 2. Half A — `SeededCredentialStrengthTests`

**File:** `tests/Architecture.Tests/SeededCredentialStrengthTests.cs`
**Why `Architecture.Tests`:** six classes there already read this exact file
(`WallDisplayAccountTests`, `RealmIdentityTests`, `ScopeGrantTests`,
`RealmAudienceTests`, `WallClientDeclarationTests`, `KioskScopeParityTests`).
Mirroring them is the house rule (read before write); inventing a new home
is not.

### 2.1 Shape

```
private static JsonDocument Realm()                      // locate + parse the import
private static string DeclaredPolicy()                   // realm.passwordPolicy, verbatim
private static PolicyPredicate ParsePolicy(string)       // §2.2
private static SeededAccount[] HumanAccounts()           // §2.3
```

`SeededAccount` is a file-local `private sealed record` of
`(string Username, string Password, string[] Groups, string[] RealmRoles)`.

Six facts, sentence-style (ADR-0053), Shouldly (ADR-0052) — **8 at delivery**,
+2 added at phase-6 re-review; see `verification.md` §should-fix 4:

| Fact | SC |
|---|---|
| `Every_seeded_password_satisfies_the_realms_own_declared_policy` | SC-1 |
| `The_parsed_policy_refuses_a_password_that_breaks_each_clause` | SC-4 |
| `Seven_of_the_twelve_seeded_accounts_have_a_username_derived_password` | SC-2 |
| `One_seeded_password_authenticates_six_accounts_across_four_fabs` | SC-3 |
| `One_seeded_password_authenticates_both_realm_admin_accounts` | SC-3 |
| `The_realm_seeds_twelve_human_credential_blocks_and_six_distinct_passwords` | SC-1 (the census the others rest on) |
| `The_password_shared_across_the_most_fabs_still_spans_four_fabs_and_six_accounts` *(phase 6)* | SC-3, value-agnostic — additive control, not a new SC row (see `verification.md` §should-fix 4) |
| `The_password_shared_by_the_most_admin_accounts_still_reaches_two_of_them` *(phase 6)* | SC-3, value-agnostic — additive control, same as above |

### 2.2 The policy predicate — parsed, never hard-coded

This is the load-bearing design choice in Half A, and the reason it is not a
guard that merely restates its own input.

`ParsePolicy` splits the realm's `passwordPolicy` string on `" and "`, and
maps each clause to a predicate:

| Clause | Predicate |
|---|---|
| `length(n)` | `password.Length >= n` |
| `upperCase(n)` | at least `n` `char.IsUpper` |
| `lowerCase(n)` | at least `n` `char.IsLower` |
| `digits(n)` | at least `n` `char.IsDigit` |
| `specialChars(n)` | at least `n` non-alphanumeric |
| `notUsername(...)` | `password != username` |

**An unrecognised clause throws**, naming the clause. It does not warn, skip,
or default to `true`. If a human raises the policy to something this parser
does not model, the build fails and says which clause — that is the correct
outcome, because a silently-ignored clause would make SC-1 pass for the wrong
reason, and SC-1's whole job is to be trustworthy on the day the policy
changes.

`specialChars` and `notUsername` are modelled although today's policy uses
neither. **This is not speculative generality:** they are the two clauses a
human raising the policy is most likely to add, and their absence would turn
the first assertion after the decision into a throw the reviewer has to
diagnose. They are six lines. This is a marked judgement call — ADR-0036
would refuse a knob for a need that does not exist; this is a parser being
complete over a small, closed, published vocabulary.

**Why SC-4 exists.** A predicate built this way could still be vacuously true
if a clause mapped to `_ => true`. SC-4 asserts `alllowercase1`,
`ALLUPPERCASE1`, `NoDigitsHere` and `Ab1` are each refused, **and that each
is refused by the specific clause it breaks** — so a predicate that refused
everything would also fail. Refusing-everything and admitting-everything are
both caught.

### 2.3 Reading the accounts — the filter that matters

`HumanAccounts()` takes `realm.users[*]` and **excludes** entries whose
username starts with `service-account-`. There are five of those
(`:571-610`), they carry no `credentials` block, and including them would
make the "twelve" in SC-1's census wrong in a way nobody would notice.

The census fact asserts **12 credential blocks and 6 distinct values** so that
adding a thirteenth seeded account fails this test and forces the author to
think about it. That is the drift guard; without it every other number here
rots the first time the realm grows.

### 2.4 The derivation rule, written out in the test

```
Capitalise(LocalPart(username)) + suffix,  suffix ∈ { "1234", "-1234", "_1234" }
LocalPart(u) = u up to the first '@', else u
Capitalise(s) = char.ToUpperInvariant(s[0]) + s[1..]
```

The suffix set is a `private static readonly string[]` with a comment naming
it as assumption **G1** and pointing at `spec.md` §*Assumptions*. It is in the
test, not in prose, so a reviewer who disagrees can edit three strings and
re-run rather than re-derive the finding.

`StringComparison.Ordinal` everywhere; `ToUpperInvariant`, never `ToUpper`.

---

## 3. Half B — `LockoutThroughputMeasurementTests`

**File:** `tests/Integration.Tests/Identity/LockoutThroughputMeasurementTests.cs`
**Attributes:** `[Collection(AspireCollection.Name)]` **and**
`[Trait("Category", "Measurement")]`.

### 3.1 The trait is a design decision, not a convenience

`IntegrationTestSelectionTests` (Architecture.Tests, #2289) derives the legal
category set by text-parsing `ci.yml`'s `--filter` strings and fails the build
for a class that declares neither a `[Collection]` nor a recognised
`[Trait]`. The three excluded categories mean "needs a stack CI does not
boot". SC-8 waits out a real Keycloak lock — up to `maxFailureWaitSeconds`
(900 s) — so this class genuinely belongs there.

**Consequence, already checked:** PR #2535's `integration-shard-coverage` job
runs discovery *with* the category exclusion applied, so a `Measurement` class
is outside the discovered set and needs no `ci-shards/shard-N.filter` entry.
`spec.md` §*File contention* carries the falsifier if that is wrong.

**Consequence, stated plainly:** this half is **not** CI-verified. Its figure
is produced by a deliberate run at phase 5 and recorded in `verification.md`.
That is the honest trade and it is why assumption **G3** exists.

### 3.2 Helpers — copied, not extracted, and here is the argument

Specs 207 and 214 both copied `CreateProbeUserAsync` / `DeleteProbeUserAsync` /
`MasterRealmAdminClientAsync` / `ReadFailureFactorAsync` rather than widening
`RealmProbe.cs`, with the stated rule "a third consumer is the moment to
promote a helper, not the first". **This is the third consumer, and this plan
still declines to promote.** The reason is not the rule:

- `RealmProbe` is shared with `KeycloakAdminTokenProviderTests` and
  `MqttAudienceIntegrationTests`, neither of which touches users, passwords or
  attack detection. Promoting user-lifecycle and brute-force helpers onto it
  widens a class those two depend on, for two consumers that both already
  have working copies.
- It would edit a **shared file** while specs 217 and 218 are in flight, for
  no behaviour change — exactly the drive-by refactor ADR-0036 refuses.
- The promotion is a genuine improvement and it is a *different* change with
  a different risk profile: it touches three existing test classes, so it is
  behaviour-preserving work needing its own characterisation run.

**Action:** file it as a follow-up issue at phase 7 (a `tech-debt` issue, not
`agent:ready`), naming the three copies and the two shared consumers. Recorded
here so the next reader meets the decision rather than the duplication.

### 3.3 Facts

Six facts as originally planned here (`tasks.md` T002 already listed a
seventh, the bad-request case, omitted from this table by oversight) — **8 at
delivery**, +1 more added at phase-6 re-review; see `verification.md`
§should-fix 2 and §should-fix 3. **None is `[P]`** — not a file-ownership
constraint but a shared-resource one: there is one Keycloak and one realm,
and these facts mutate its attack-detection state. Spec 207's `tasks.md`
states the same rule for the same reason.

| Fact | SC | Shape |
|---|---|---|
| `A_fresh_probe_accounts_correct_password_mints_a_token` | SC-5 | create → `200` → delete |
| `The_running_realm_declares_the_password_policy_the_import_file_carries` | SC-6 | master-realm `GET /admin/realms/smart-sentinel-eye`, compare to the file string read the same way Half A reads it |
| `A_bursting_caller_is_refused_after_the_quick_login_check_trips` | SC-7a | no delay; record attempts; assert `disabled == true` |
| `A_paced_caller_is_admitted_more_attempts_than_a_bursting_one` | SC-7 | pace at `quickLoginCheckMilliSeconds + 100 ms`, read off the server; assert paced attempts **>** burst attempts |
| `A_temporary_lock_expires_without_administrative_intervention` | SC-8 | poll the correct password every 5 s; record elapsed seconds; bound at 900 s + 120 s margin. **Deviation, recorded at phase-6 re-review:** `spec.md`'s own SC-8 Given clause names a **paced** account; the delivered fact locks via burst instead (`plan.md` here never specified which, but `spec.md` does) — see `verification.md` §should-fix 4 for what that costs SC-11's arithmetic |
| `A_seeded_account_authenticates_while_a_probe_is_locked` | SC-9 | `admin` / `Admin1234`, read-only, never locked |
| `A_grant_with_no_username_is_refused_as_invalid_request_not_invalid_grant` | bad-request | listed in `tasks.md` T002, omitted here by oversight; collapsed to its falsifiable claim only at phase-6 re-review — see `verification.md` §should-fix 3 |
| `A_grant_naming_a_real_account_but_missing_its_password_counts_as_a_failed_login` *(phase 6)* | bad-request, honest repair | added when the originally-proposed fix to the fact above turned out to change Keycloak's own diagnosis rather than repair an unfalsifiable check — see `verification.md` §should-fix 2 |

### 3.4 The four traps, each already paid for once in this repository

1. **User Profile requires `firstName`, `lastName`, `email`.** A probe user
   created without them fails the token endpoint with `invalid_grant` /
   "Account is not fully set up" — which reads *exactly* like a lockout and
   would make SC-7 pass for the wrong reason. Spec 207 found this. All three
   fields are set at creation, and SC-5 (mint before any wrong guess) is the
   control that catches it if they are not.
2. **`identity-admin` returns a partial realm representation.** Spec 207's
   phase 6 found `ReadFailureFactorAsync` silently falling back to Keycloak's
   default of 30. Every realm read here goes through the **master realm**
   (`client_id=admin-cli`, `username=admin`, password `testkeycloak` under the
   fixture — `AspireFixture.cs:312-318` — not `dev-only-keycloak-admin`, which
   is the `aspire run` default at `AppHost.cs:30`). **A missing field is an
   escalation, not a default.**
3. **Mint from Aspire's proxied endpoint.** `aspire.CreateKeycloakClient()`
   calls `App.GetEndpoint("keycloak")` with no endpoint name, deliberately: a
   token minted at the container's mapped port carries an issuer the services
   reject. Never a literal host or port.
4. **Cleanup in `finally`, status-checked.** `DELETE .../users/{id}` disposes
   of the lock with the user. A half-created user (reset-password threw) is
   deleted in a `catch { … ; throw; }`. Leaving a lock behind fails the next
   test class for a reason that reads exactly like an unrelated regression.

### 3.5 What Half B must not do

- It must never send a wrong password to a **seeded** account. Spec 207's
  verification did (`operator`), cleared it, and got away with it; that is not
  a pattern to copy while other worktrees may be running the suite.
- It must not `DELETE` the attack-detection record in the SC-8 fact — that is
  precisely the shortcut spec 207 took, and it is why the lock's real duration
  has never been observed here.
- It must assert **no threshold** on the throughput figure. A threshold is a
  policy judgement, and ADR-0144 keeps the lane out of that.

---

## 4. The framing corrections this planning found

`spec.md` records these; they are collected here because the brief asked for
any adjustment beyond the blast-radius numbers already supplied.

1. **"For the caller's fab" is wrong — one guess crosses four fabs.** #2510
   inherits this qualifier from spec 207. `Operator1234` is held by six
   accounts in `/fabs/munich`, `/fabs/dresden`, `/fabs/berlin` and
   `/fabs/hamburg`, one of which (`op-multi@smart-sentinel-eye.test`,
   realm JSON `:542`) is in two groups at once. Fab isolation is enforced per
   token; it is not enforced against a password reused across fabs. This makes
   the exposure larger than the issue states.

2. **The exhaustibility framing understates the finding, and a length increase
   would not fix it.** #2510 argues from a top-1000 list. Seven of twelve
   accounts have a password derivable from their own public username, so the
   real candidate set is ~1 per account, not 1000. Consequence for the human
   decision: **raising `length(8)` to `length(12)` would leave every one of
   the seven still derivable** — `Wall-munich-1234` is already 16 characters
   and `Operator1234` is 12. The clause that would actually refuse them is
   `passwordBlacklist(...)` or a rotation away from the derivation, not a
   length or character-class rule. A reviewer who reads only #2510 would very
   plausibly pick the ineffective remedy. `spec.md` §*Out of scope* names the
   blacklist as a candidate for exactly this reason.

3. **The blast-radius list was understated on every line, and missed a whole
   directory.** 97 files / 158 occurrences, against "README + nine quickstarts
   + one constant + e2e/support". Full table and the reproduction command in
   `spec.md` §*Blast radius*. The single most load-bearing correction: there
   is **no central constant for `Operator1234`** — 34 files each declare their
   own — so "change the constant" is not available as a cheap remedy.

4. **"Nine `specs/*/quickstart.md`" is exactly right, and still misleading.**
   There are exactly 9 `quickstart.md` files holding a literal — the count was
   never wrong. But there are **41** `specs/**/*.md` files holding one, so the
   nine are 22% of the category that matters. This is a category error rather
   than a miscount, and it is the kind this repository has had to correct
   before: the figure survives every spot-check precisely because it is true.

5. **`scripts/` holds three occurrences nobody had listed**, all in the
   Playwright trace-redaction fixtures. They are sentinels and comments, not
   live credentials, so they are cheap to update and invisible to fail — the
   worst combination for drift.

---

## 5. Risks

| Risk | Mitigation |
|---|---|
| The delivery machine cannot boot Aspire (G3; has happened repeatedly) | Half A is complete and CI-verified on its own. Half B's facts can be settled from a CI `.trx` artefact the way spec 214 settled its six — **except SC-8**, which is `Category=Measurement` and therefore never runs in CI. If SC-8 is not run, SC-11 is **not met**, and the PR must say so rather than quote the arithmetic |
| SC-8 waits up to 900 s and looks hung | `test.setTimeout`-equivalent: an explicit bounded poll with a `Stopwatch`, logging each attempt, and a hard failure (not a skip) at 1020 s |
| The paced pacing is wrong (G2) and both cases lock at 2 | That is SC-7's named falsifier. Record it, do not adjust the assertion — an assertion edited after seeing the behaviour is the thing ADR-0144 §4a forbids |
| Half A's parser throws on a clause a human later adds | Deliberate; §2.2. The failure names the clause |
| A probe account or lock is left behind and poisons another worktree's run | `finally` + status-checked delete (§3.4.4); SC-13 re-reads the realm after the run; probe usernames are GUID-suffixed so a leak is identifiable |
| `integration-shard-coverage` (PR #2535) goes red naming the new class | Falsifier and remedy in `spec.md` §*File contention* — add to the smallest shard, do **not** re-trait it into CI |
| Two worktrees boot Aspire at once → `FailedToStart` that reads like a code defect | One machine, one stack. Confirm no other AppHost is running before phase 5 |
| A reviewer reads this PR as the policy having been decided | SC-12: the PR body presents both costs and picks neither; #2510 stays open and keeps `agent:ready` off until a human answers |

---

## 6. Phase 6 — mandatory reviewers

- **`/security-review` is mandatory**, not conditional. This change is
  test-only and adds no attack surface, but its subject is a credential
  control and its artefacts enumerate every seeded password's location. The
  specific things to review: that no new credential literal is introduced
  beyond the probe passwords; that no probe account or lock can survive a
  failed run; that the spec's own blast-radius listing does not itself become
  a convenient index for an attacker in a repository that is already public
  about these values (it does not — every value it names is already in
  `README.md`).
- **`backend-reviewer`** for the C#: guards, `CancellationToken`, collection
  expressions, no underscore fields, Shouldly assertion messages carrying the
  numbers.
- **`/code-review`** per ADR-0144's phase-6 row.

---

## 7. Definition of done

1. `SeededCredentialStrengthTests` green in CI, six facts as planned
   (**8 at delivery, +2 at phase-6**, `verification.md` §should-fix 4),
   numbers in the assertion messages.
2. T004's counterfactual output quoted in the PR — each of Half A's four
   numeric claims shown failing when its input is perturbed.
3. `LockoutThroughputMeasurementTests` run at least once against a real stack,
   its output quoted verbatim in `verification.md`.
4. `verification.md` states: burst attempts, paced attempts, measured lock
   duration in seconds, derived attempts/hour, and **the wall-clock time to
   exhaust a seven-account username-derived candidate set** (SC-11).
5. The blast-radius command re-run at phase 5 and its output quoted (SC-10).
6. The realm is byte-identical to `b6ddf7df`; `git diff` over
   `src/AppHost/Realms/` is empty (SC-13).
7. PR body: both costs, neither picked; Phase 4b recorded as skipped with its
   one-line reason; #2510 **not** closed.
8. #2510 on Project #13 (it already is — verified, status Todo).
