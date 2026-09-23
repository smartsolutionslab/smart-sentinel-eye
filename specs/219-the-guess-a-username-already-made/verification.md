# Verification — Spec 219 (#2510)

Phase 4b: skipped — this delivery adds two test files and records a finding;
there is no production code to write. No `src/` file, no realm field, and no
seeded password changed anywhere in this delivery.

## Phase 4a — CHARACTERISATION, OBSERVED GREEN, both halves' counterfactuals

### Half A — `SeededCredentialStrengthTests` (build-enforced, no stack)

`dotnet build tests/Architecture.Tests -c Release`: 0 errors. Full
`Architecture.Tests` suite, **452/452 passed** (450 at first delivery, +2
from should-fix 4's phase-6 addition below), including all eight facts and
the pre-existing `IntegrationTestSelectionTests` guard that validates the
new Half B class's `[Collection]`/`[Trait]` declaration.

**T003's counterfactual — five rows, each constructed against a scratch copy
of the realm in the scratchpad (never `src/AppHost/Realms/`, per plan.md's
explicit allowance), captured failing, then reverted.** After every row, the
committed test file diffed **byte-identical** to its pre-scaffolding content
(`diff` against a saved backup: `IDENTICAL`), and the six real facts re-ran
green with a `--no-incremental` rebuild (the "restored file keeps its old
timestamp" trap).

**Row 1 — weaken a seeded password (`wall-munich` → `short1`). Targets SC-1.**

```
Shouldly.ShouldAssertException : policy.IsSatisfiedBy(account.Password, account.Username)
    should be
True
    but was
False

Additional Info:
    'wall-munich' carries a password that does not satisfy the realm's own declared policy 'length(8) and upperCase(1) and lowerCase(1) and digits(1)'; checked 12 credential blocks, 6 distinct values.
```

**Row 2 — add a 13th human account. Targets the census fact.**

```
Shouldly.ShouldAssertException : accounts.Length
    should be
12
    but was
13

Additional Info:
    expected 12 seeded human credential blocks; found 13. Every other number in this file rests on this census — a seeded account was added or removed without updating it.
```

**Row 3 — `operator`'s password no longer username-derived. Targets SC-2's "seven".**

```
Shouldly.ShouldAssertException : derived.Length
    should be
7
    but was
6

Additional Info:
    expected 7 of 12 seeded accounts to carry a password of Capitalise(local-part(username)) + one of [1234, -1234, _1234]; found 6: wall-munich, wall-dresden, wall-berlin, wall-hamburg, admin, admin@munich.test
```

**Row 4 — move `op-berlin@berlin.test` into `/fabs/munich`. Targets SC-3's "four fabs".**

```
Shouldly.ShouldAssertException : fabGroups.Length
    should be
4
    but was
3

Additional Info:
    expected 'Operator1234' to reach 4 fab groups; found 3: /fabs/munich, /fabs/dresden, /fabs/hamburg
```

**Row 5 — make `ParseClause` return a vacuous `(_, _) => true` predicate. Targets SC-4.**

```
Shouldly.ShouldAssertException : policy.IsSatisfiedBy(password, probeUsername)
    should be
False
    but was
True

Additional Info:
    'alllowercase1' should be refused by the realm's declared policy 'length(8) and upperCase(1) and lowerCase(1) and digits(1)', or the policy predicate is vacuously true
```

Every row is a defect it was supposed to catch, caught. No row was silently
green.

### Half B — `LockoutThroughputMeasurementTests` (live stack)

Phase-4a colour is the same as Half A (plan.md §1): characterisation,
observed green, with a mandatory counterfactual. Half B's counterfactual is
reported under Phase 5 below, alongside the measurement itself, because both
needed the live stack and were captured in the same session.

## Phase 5 — the live observation: OBTAINED, across five sessions against the real stack

(Three sessions below at original delivery, Runs 1–3; two more during phase-6
re-review, reported in that section further down — this heading named only
the first three until corrected here.)

Docker Desktop was confirmed stopped at delivery start (`wsl -l -v`); this
file's Half A / Half B split exists for exactly that reason (assumption G3).
Docker was confirmed running again mid-session (`docker info` responsive,
`docker ps -a` empty, no stale `*keycloak*` volume — a genuinely fresh
volume, so `WithRealmImport` was guaranteed to run rather than silently
reusing an old realm). One machine, one stack throughout: no other Aspire
process or `testhost` was found before booting (`tasklist`,
`Get-CimInstance Win32_Process` showed only pre-existing `MSBuild.dll`
node-reuse processes).

**Run 1** (`dotnet test tests/Integration.Tests --filter
"FullyQualifiedName~LockoutThroughputMeasurementTests"`, Release, 3.28 min).
**7 tests here, not 8** — the file held seven facts at this point in the
session; phase-6 re-review later added an eighth (§*The committed state,
confirmed*, further down, has the 8/8 transcript for the final delivered
file):

```
Total tests: 7
     Passed: 7
```

```
burst: 'policy-probe-2bba5de0fa2d491a8b590737cb18d3cf' reported disabled after 2 wrong-password attempt(s) with no delay between them.
attack-detection record at lock: {"failedLoginNotBefore":1790145384,"numFailures":2,"numTemporaryLockouts":0,"disabled":true,"numSecondaryAuthFailures":0,"lastIPFailure":"172.18.0.1","lastFailure":1790145324991}

'policy-probe-d522a86c61364e00b0dfbe8cdacaccd1' locked after 2 burst attempt(s); polling every 5s for natural expiry (no DELETE of the attack-detection record).
measured lock duration: 61,1s until 'policy-probe-d522a86c61364e00b0dfbe8cdacaccd1''s correct password was accepted again.

burst: 2 attempt(s) ('policy-probe-8d705892519843e69bd444430a9cb80b'); paced (+1100 ms between attempts): 10 attempt(s) ('policy-probe-2315c115cc9b48ed8bc2acc8f2bb6eee').
```

**Run 2** (same filter, immediately after Run 1, 3.43 min):

```
Total tests: 7
     Passed: 7
```

```
burst: 'policy-probe-e782798a981b44978b48d11762876f89' reported disabled after 2 wrong-password attempt(s) with no delay between them.
attack-detection record at lock: {"failedLoginNotBefore":1790145601,"numFailures":2,"numTemporaryLockouts":0,"disabled":true,"numSecondaryAuthFailures":0,"lastIPFailure":"172.18.0.1","lastFailure":1790145541471}

'policy-probe-862cdb842ca245f2addc92fa92c7cd1f' locked after 2 burst attempt(s); polling every 5s for natural expiry (no DELETE of the attack-detection record).
measured lock duration: 61,2s until 'policy-probe-862cdb842ca245f2addc92fa92c7cd1f''s correct password was accepted again.

burst: 2 attempt(s) ('policy-probe-d6ca733e687d487d9d7229789c690979'); paced (+1100 ms between attempts): 10 attempt(s) ('policy-probe-5a7f941b250846a7949a447c76c69f86').
```

**Run 3** (final confirmation, after the counterfactual scaffolding below was
added and removed, 3.40 min — proves the committed file is unchanged by that
detour):

```
Total tests: 7
     Passed: 7
```

```
burst: 'policy-probe-f34352c21e1e46e0a962fab2bbca004d' reported disabled after 2 wrong-password attempt(s) with no delay between them.
'policy-probe-48d988e6d2d84562b1f9d3184843755f' locked after 2 burst attempt(s); polling every 5s for natural expiry (no DELETE of the attack-detection record).
measured lock duration: 61,2s until 'policy-probe-48d988e6d2d84562b1f9d3184843755f''s correct password was accepted again.
burst: 2 attempt(s) ('policy-probe-7d5efc94d49a40ddadfe2d29ce815079'); paced (+1100 ms between attempts): 10 attempt(s) ('policy-probe-4c36301f3d0d40febb479b9fee5b268e').
```

**Stable across all three runs: burst locks at exactly 2 attempts, paced
locks at exactly 10 attempts, lock duration measured at 61.1–61.2s (±5s, the
poll interval this figure is quantised to — quoted at 0.1s precision
throughout only because that is what `Stopwatch.Elapsed` reports at the
moment the poll happens to observe the lock clear, not because the
underlying event is known to that precision).** This
matches spec 207's own `verification.md` observation of `numFailures: 2` for
the quick-login trip, and 10 is the realm's own `failureFactor` — SC-7's
hypothesis held on every run; the named falsifier (both locking at the same
count) never fired.

### SC-6 — the running realm agrees with the file

`The_running_realm_declares_the_password_policy_the_import_file_carries`
passed on all three runs: the master-realm read of `passwordPolicy` matched
the file's `length(8) and upperCase(1) and lowerCase(1) and digits(1)`
verbatim, and both `failureFactor` and `quickLoginCheckMilliSeconds` were
present, not defaulted.

### Half B's counterfactual — two attempts, the first one's failure is itself a finding

**Attempt 1 (removed): compare the token endpoint's own refusal body between
"locked" and "locked-AND-disabled".** This failed:

```
locked-only refusal (after 2 burst attempts): status=400 body={"error":"invalid_grant","error_description":"Invalid user credentials"}
locked-AND-disabled refusal: status=400 body={"error":"invalid_grant","error_description":"Invalid user credentials"}
Failed SmartSentinelEye.Integration.Tests.Identity.LockoutThroughputMeasurementTests.TEMPORARY_T004_DisablingAStillLockedProbeChangesTheRefusalSignature
```

**Corrected on phase-6 review — the first version of this paragraph was
wrong, and the error was caught only after it had already travelled into a
reviewer's brief.** What attempt 1 actually shows is **narrower** than "byte
identical for both": it shows only that **locked** and **locked-AND-disabled**
give the same generic refusal — the account was already locked in both legs
of that comparison, so the lockout's own check evidently short-circuits
before Keycloak ever reports the `enabled` state. It says nothing about a
never-locked, merely-disabled account, which attempt 2 (below) tested
separately and got a **different** body. "Signature" still cannot mean the
attempt-1 OAuth error text specifically for the locked-vs-locked+disabled
comparison — which is why every committed fact in this file (and in
`BruteForceLockoutIntegrationTests`, `LockoutSessionSurvivalIntegrationTests`
before it) reads the **Admin API's** `attack-detection` record instead of the
wire body — but the corrected, narrower claim is what's recorded here, not
the broader one the first draft stated.

**Attempt 2 (the real counterfactual, proves what fact 3 actually depends
on): a probe disabled while it was *never locked* is refused at the token
endpoint, yet its attack-detection record still reads `disabled: false`.**

```
never-locked probe's attack-detection record before disable: {"failedLoginNotBefore":0,"numFailures":0,"numTemporaryLockouts":0,"disabled":false,"numSecondaryAuthFailures":0,"lastIPFailure":"n/a","lastFailure":0}
disabled-only refusal: status=400 body={"error":"invalid_grant","error_description":"Account disabled"}
same probe's attack-detection record after disable+refusal: {"failedLoginNotBefore":0,"numFailures":0,"numTemporaryLockouts":0,"disabled":false,"numSecondaryAuthFailures":0,"lastIPFailure":"n/a","lastFailure":0}
Passed SmartSentinelEye.Integration.Tests.Identity.LockoutThroughputMeasurementTests.TEMPORARY_T004_ADisabledNeverLockedProbeIsRefusedWithoutTrippingTheLockoutSignature
```

This proves fact 3's `attackDetection.GetProperty("disabled").GetBoolean().ShouldBeTrue(...)`
is specific to the brute-force mechanism: a refusal from a wholly different
cause (`enabled: false`, never a single wrong guess) does **not** flip that
field, so the check is not satisfied by "any refusal at all" — same shape as
spec 214's SC-4 for the refresh grant.

### A named finding — the token endpoint is an account-state disclosure oracle for "disabled", not for "locked"

Read the two attempts side by side, not as a discarded false start plus a
kept result, but as one measurement with two legs:

| Account state | `error` | `error_description` |
|---|---|---|
| locked (attempt 1) | `invalid_grant` | `Invalid user credentials` |
| locked AND also disabled (attempt 1) | `invalid_grant` | `Invalid user credentials` |
| disabled, never locked (attempt 2) | `invalid_grant` | `Account disabled` |

**An unauthenticated caller who can reach `/realms/smart-sentinel-eye/protocol/openid-connect/token`
can already learn, for any username it names, whether that exact account is
currently disabled** — `error_description` says `"Account disabled"` only
when it is, and the generic `"Invalid user credentials"` otherwise (wrong
password, or locked). This is a real account-state oracle on the same
endpoint this file's own SC-5–SC-9 measurement uses, distinguishable from
ordinary wrong-credentials refusal without any admin privilege. It sits
directly next to SC-11's own argument that the attacker already knows which
seven usernames are worth guessing — this oracle would additionally tell
them, for any of the realm's other accounts, whether one is disabled at all,
for free, before spending a single guess.

**This is Keycloak's default behaviour, observed rather than introduced by
this delivery, and not something to fix here** — it is not this spec's
subject (no `src/` change, no realm change), and the reviewer's finding is
that it needs recording accurately, not remediated. Whether it is the
lockout state or the disabled state that a caller can distinguish this way
is exactly the kind of fact `spec.md` §*Out of scope* would want named for a
future decision, the same way `passwordBlacklist(...)` was named there
without being adopted.

Both scaffolding facts were added, built (`dotnet build`, 0 errors each
time), run, captured above, then removed. The file was rebuilt
`--no-incremental` afterward and confirmed to hold exactly its seven facts
(`grep -c "\[Fact\]"` → `7`; `grep "TEMPORARY"` → no matches) before Run 3
re-confirmed it green.

### SC-13 — the realm is left as it was found

- `git diff --stat src/AppHost/Realms/` — empty, throughout every run and
  every scaffolding round-trip.
- `git status --short` — three files, all still untracked (`??`) at this
  point, nothing committed past `b0c382a8`: the two test files
  (`tests/Architecture.Tests/SeededCredentialStrengthTests.cs`,
  `tests/Integration.Tests/Identity/LockoutThroughputMeasurementTests.cs`)
  plus this file itself, `specs/219-the-guess-a-username-already-made/verification.md`
  — which `git status` shows as untracked like any other new file, since it
  is being written in the same pass it is describing.
- A dedicated residue sweep (`TEMPORARY_T004_NoResidueFromThePriorRuns`,
  added, run once, removed the same way) read the live realm **in its own,
  separate `dotnet test` process**, immediately after Runs 1–3:

```
policy-probe-* users remaining: 0 —
'wall-munich' attack-detection record: {"failedLoginNotBefore":0,"numFailures":0,"numTemporaryLockouts":0,"disabled":false,"numSecondaryAuthFailures":0,"lastIPFailure":"n/a","lastFailure":0}
'wall-dresden' attack-detection record: {"failedLoginNotBefore":0,"numFailures":0,"numTemporaryLockouts":0,"disabled":false,"numSecondaryAuthFailures":0,"lastIPFailure":"n/a","lastFailure":0}
'wall-berlin' attack-detection record: {"failedLoginNotBefore":0,"numFailures":0,"numTemporaryLockouts":0,"disabled":false,"numSecondaryAuthFailures":0,"lastIPFailure":"n/a","lastFailure":0}
'wall-hamburg' attack-detection record: {"failedLoginNotBefore":0,"numFailures":0,"numTemporaryLockouts":0,"disabled":false,"numSecondaryAuthFailures":0,"lastIPFailure":"n/a","lastFailure":0}
'admin' attack-detection record: {"failedLoginNotBefore":0,"numFailures":0,"numTemporaryLockouts":0,"disabled":false,"numSecondaryAuthFailures":0,"lastIPFailure":"n/a","lastFailure":0}
'operator' attack-detection record: {"failedLoginNotBefore":0,"numFailures":0,"numTemporaryLockouts":0,"disabled":false,"numSecondaryAuthFailures":0,"lastIPFailure":"n/a","lastFailure":0}
Passed SmartSentinelEye.Integration.Tests.Identity.LockoutThroughputMeasurementTests.TEMPORARY_T004_NoResidueFromThePriorRuns
```

**Corrected at phase-6 re-review: this specific sweep proves less than the
paragraph above originally implied.** Investigating should-fix 3 (below)
established that `AspireFixture` tears its stack down **completely** between
separate `dotnet test` processes — `docker ps -a` and `docker volume ls` both
came back fully empty immediately after every run in this file, not merely
stopped. A residue check run as its own, later process therefore starts
against a **brand-new realm import**, which trivially shows zero probes and
zero seeded-account failures regardless of what the prior process actually
left behind — it could not have detected residue from Runs 1–3 even if there
had been some. What the sweep above legitimately shows is only that a fresh
stack starts clean, which was never in question.

**What actually established no-residue-within-a-run**: Runs 1, 2, and 3 each
reported every fact `Passed`, and every fact's own `finally` block calls
`DeleteProbeUserAsync`, whose own status-checked assertion
(`deleted.IsSuccessStatusCode.ShouldBeTrue(...)`) would have failed that run
had any delete failed. A passing run is real evidence that every probe *that
run created* was also deleted *within that same run*, against the *same*
live realm — which is the claim that matters. The cross-process sweep above
is kept in this file as a correction, not deleted, so the mistake is visible
rather than quietly fixed.

**should-fix 3 (below) is the real gap this closes**: it found and fixed a
leak path Runs 1–3 never exercised at all (a probe leaking when a helper's
own ceiling assertion throws, *before* returning to the caller's `try` at
all), proven same-process rather than through this sweep's flawed cross-process
method.

## Phase 6 re-review — should-fixes 1–5

Security review (phase 6) found five should-fix items — no blockers, since
nothing touches `src/`, no realm mutation, and no new auth-model surface.
Should-fix 1 is addressed inline above (the account-state-disclosure-oracle
finding and its correction). The other four needed the live stack; Docker was
confirmed running again (`docker info` responsive) before starting, and a
zero-residue pre-check (`TEMPORARY_SweepPolicyProbeResidueAndReport`, run and
removed the same way as every other scaffold in this file) found nothing
before this round began.

### should-fix 2 — the bad-request fact's counter check couldn't fail, and the proposed repair was itself wrong

**The finding.** `A_grant_with_no_username_is_refused_as_invalid_request_not_invalid_grant`
sent a grant with no username at all, so
`failuresAfter.ShouldBe(failuresBefore)` had no account to attribute a moved
counter to — it could not fail regardless of Keycloak's behaviour, the "an
assertion must not check its own input" trap this repository has caught
before.

**The first repair attempt was tried live and found wrong, not merely
untested.** The suggested fix — name the probe's own username, omit only the
password, still expect `invalid_request` — was implemented and run. It
observed:

```
Shouldly.ShouldAssertException : response.StatusCode
    should be
HttpStatusCode.Unauthorized
    but was
HttpStatusCode.BadRequest

Additional Info:
    a grant naming 'policy-probe-220a43e4ea9d4ae1842b78639979a2fb' but missing its password should be refused, not silently accepted. body: {"error":"invalid_grant","error_description":"Invalid user credentials"}
```

**Keycloak 26.6.4 treats a present username with a missing password as an
ordinary failed authentication attempt (`400` / `invalid_grant`), not a
malformed request.** In the one malformation actually measured (username
present, password entirely absent), structural, pre-authentication
validation answered `invalid_request` only when the *username* was absent;
once a username was present, Keycloak proceeded to authenticate, and a
missing password was handled the same as a wrong one. Not verified against
other malformations — the claim is scoped to what was actually sent. The
proposed repair therefore silently changed what the fact tested rather than
fixing it.

**Resolution: split into two honestly-labelled facts**, rather than force one
malformation to prove two different claims:

1. `A_grant_with_no_username_is_refused_as_invalid_request_not_invalid_grant`
   — kept, because the `error`-type claim (`invalid_request`, not
   `invalid_grant`) is real and worth preserving (spec 207's own live
   finding). Its counter check is now explicitly documented in the code as
   **structural, not a live check** — it cannot fail for this malformation,
   and the doc comment says so rather than leaving it to look like a real
   assertion.
2. `A_grant_naming_a_real_account_but_missing_its_password_counts_as_a_failed_login`
   — new. States what was actually observed (`400` / `invalid_grant` /
   `"Invalid user credentials"`, counter moves by exactly one) and is the
   fact that actually needed to be falsifiable.

**Phase-4a discipline for fact 2 (new behaviour, not characterisation):
proven red before green.** With the assertion deliberately inverted
(`failuresBefore`, the wrong prediction):

```
Shouldly.ShouldAssertException : failuresAfter
    should be
0
    but was
1

Additional Info:
    'policy-probe-a188c76be64d4253a63e3048d526bb7c''s failure counter should move by exactly one — Keycloak treats a missing password the same as a wrong one once a real username is present. before: 0, after: 1.
Failed SmartSentinelEye.Integration.Tests.Identity.LockoutThroughputMeasurementTests.A_grant_naming_a_real_account_but_missing_its_password_counts_as_a_failed_login
```

Corrected to `failuresBefore + 1`, both facts pass together:

```
Passed SmartSentinelEye.Integration.Tests.Identity.LockoutThroughputMeasurementTests.A_grant_with_no_username_is_refused_as_invalid_request_not_invalid_grant [1 s]
Passed SmartSentinelEye.Integration.Tests.Identity.LockoutThroughputMeasurementTests.A_grant_naming_a_real_account_but_missing_its_password_counts_as_a_failed_login [835 ms]
```

**A side finding worth keeping**: a client bug that ever sends a password
grant with an empty or absent password field is indistinguishable,
server-side, from a wrong guess — it counts toward that account's lockout the
same way. Nothing in this codebase does that today; recorded because it is
the kind of fact a future integration is likely to trip over silently.

### should-fix 3 — a probe leaks on the exact failure path this file exists to detect

**The finding.** `BurstUntilLockedAsync` and `PacedUntilLockedAsync` each
create a probe, then run their own ceiling assertion
(`disabled.ShouldBeTrue(...)`) *before* returning the probe's id to the
caller. If that assertion throws — precisely the "lockout isn't tripping as
configured" scenario this file exists to catch — the caller's own
`try { … } finally { DeleteProbeUserAsync(id) }` never starts, because the
tuple-deconstructing `await` on the right-hand side never completes. A
locked, failure-laden `policy-probe-*` account leaks into the shared realm.

**A methodological correction, found while proving this**: `AspireFixture`
tears its stack down completely between separate `dotnet test` processes —
confirmed via `docker ps -a` and `docker volume ls`, both empty immediately
after every run in this file. A leak and its check must therefore happen in
the **same process**, against the **same** live realm; a residue check run as
a separate process afterward proves nothing (see the SC-13 correction
above). The proof below uses one combined fact for exactly that reason.

**Before the fix — proven with `BurstAttemptCeiling` temporarily rigged to
1** (below the real trip point of 2, guaranteeing the ceiling assertion
throws), calling `BurstUntilLockedAsync` directly, catching the throw, and
sweeping the same live realm in the same process immediately after:

```
BurstUntilLockedAsync threw: ShouldAssertException: disabled
policy-probe-* users found immediately after, same process: 1 — policy-probe-ed120120b0c94e9a842121b7fc11c1c9
```

The probe leaked, exactly as predicted.

**The fix**: both helpers' bodies are now wrapped in
`try { … } catch { await DeleteProbeUserAsync(id, cancellationToken); throw; }`
— the same shape `CreateProbeUserAsync`'s own reset-password step already
used correctly.

**After the fix — same rig, same combined fact, rebuilt**:

```
BurstUntilLockedAsync threw: ShouldAssertException: disabled
policy-probe-* users found immediately after, same process: 0 —
```

Same assertion failure (the ceiling is still too low — the fix changes
cleanup, not behaviour), but now zero residue. `BurstAttemptCeiling` was then
restored to 50 and the file rebuilt `--no-incremental`.

### should-fix 4 — SC-3's facts pinned literal password strings, not the invariant they protect

**The finding, the highest-severity item.** `One_seeded_password_authenticates_six_accounts_across_four_fabs`
and `One_seeded_password_authenticates_both_realm_admin_accounts` filter on
`string.Equals(account.Password, SeededOperatorPassword, ...)` — a literal.
If the human decision on remediation were a password **rotation** (a new
shared value, same accounts, same four fabs) rather than de-duplication, both
facts would fail with "found 0", and the obvious repair — update the two
constants — would make them pass again while the cross-fab reuse survived
untouched, now re-certified by a green test that never had to look at the
architecture.

**Resolution: two new, additive, value-agnostic facts** (the pinned facts are
kept — they are a good census-drift guard on their own terms).
`The_password_shared_across_the_most_fabs_still_spans_four_fabs_and_six_accounts`
and `The_password_shared_by_the_most_admin_accounts_still_reaches_two_of_them`
never reference a literal password: each groups every human account by
whichever password it currently holds, picks the group that spans the most
fab groups (respectively, the most `admin`-roled accounts), and asserts its
shape directly. A lazy rotation changes nothing these facts read, so they
stay correctly green with no edit required. A genuine de-duplication fix
would drop every group's span to one, which these facts would then catch as
something to be re-examined, not silently passed.

**Also fixed, cheaply, in the same round**: `SeededOperatorPassword` and
`SeededAdminPassword` were literal constants; they are now computed
(`Capitalise("operator") + "1234"`, `Capitalise("admin") + "1234"`) from the
same derivation rule the rest of the file already uses, so there is one
fewer literal for a future "fix" to reach for backwards.

**Counterfactual, same discipline as T003**: in a scratch realm copy,
`op-hamburg@hamburg.test`'s groups were widened from `["/fabs/hamburg"]` to
`["/fabs/hamburg", "/fabs/leipzig"]` (a synthetic fifth fab). Against that
copy:

```
Shouldly.ShouldAssertException : fabGroups.Length
    should be
4
    but was
5

Additional Info:
    expected the most widely fab-shared seeded password to reach 4 fab groups, whatever it is currently spelled; the widest one ('Operator1234') reaches 5: /fabs/munich, /fabs/dresden, /fabs/berlin, /fabs/hamburg, /fabs/leipzig
```

Reverted; the committed file diffed byte-identical to its pre-scaffolding
content, rebuilt `--no-incremental`, and the full `Architecture.Tests` suite
passed at **452/452**.

### should-fix 5 — SC-11's "≈10s" and "506/hour" rested on numbers with no captured transcript

**The finding.** The original SC-11 arithmetic sourced the paced loop's own
elapsed time from prose reasoning (subtracting a sub-call's rough duration
from a fact's total reported time), not from anything actually measured. The
"conservative upper bound" framing was also unjustified — the realm's
`waitIncrementSeconds: 60` grows the wait on each *subsequent* lockout of the
same account, so 506/hour is a **first-lock** rate, not a sustained one.

**The fix**: `PacedUntilLockedAsync` now wraps its loop in a `Stopwatch` and
returns the elapsed time; the calling fact logs it. Captured fresh, in the
same live runs as should-fixes 2 and 3's final confirmation (not derived from
the original three runs, which never emitted this figure):

```
Run 1: burst: 2 attempt(s) ('policy-probe-3999c453407249238225f37504cdd196'); paced (+1100 ms between attempts): 10 attempt(s) ('policy-probe-b4e0faeeb20b45fe9226e55159fed423'), loop elapsed 10,9s (measured, Stopwatch).
Run 2: burst: 2 attempt(s) ('policy-probe-bbcb2fbee44f4a6eabcda984773ae1f2'); paced (+1100 ms between attempts): 10 attempt(s) ('policy-probe-73751a37651c444294c227bef5909e4e'), loop elapsed 10,8s (measured, Stopwatch).
```

**The revised SC-11 arithmetic, with the growth caveat, is below.**

### The committed state, confirmed

**The committed files hold exactly 8 facts each, with zero `TEMPORARY`
references** — corrects the "exactly its seven facts" line earlier in this
file, which was accurate for its own point in the session (after should-fix
1's scaffolding was removed, before should-fixes 2–5 added an eighth fact to
`LockoutThroughputMeasurementTests` and two more to
`SeededCredentialStrengthTests`) but is stale by the time of this final
state. Verified directly against the final files:

```
grep -c "\[Fact\]" tests/Architecture.Tests/SeededCredentialStrengthTests.cs
8
grep -c "\[Fact\]" tests/Integration.Tests/Identity/LockoutThroughputMeasurementTests.cs
8
grep -n "TEMPORARY" tests/Architecture.Tests/SeededCredentialStrengthTests.cs tests/Integration.Tests/Identity/LockoutThroughputMeasurementTests.cs
(no matches)
```

**And the aggregate live run, the one that actually exercises all eight of
`LockoutThroughputMeasurementTests`'s committed facts together** — should-fix
2/3/5's own first full confirmation run after every fix in this section
landed, `dotnet test tests/Integration.Tests --filter
"FullyQualifiedName~LockoutThroughputMeasurementTests"`, Release, 3.48 min:

```
Test Run Successful.
Total tests: 8
     Passed: 8
 Total time: 3,4793 Minutes
```

A second full run immediately after (3.46 min) also reported `Total tests: 8`
/ `Passed: 8`, with the same stable figures (burst 2, paced 10, lock
61.1–61.2s) already quoted per-fact in should-fixes 2, 3 and 5 above.

### A third round of review, confirmed the same way

Code review (phase 6, second pass) found six further should-fix items —
SC-11's headline figure unmeasured and contradicting its own derivation rule
(should-fix 1), the SC-8 fact's poll loop able to pass on a near-zero
duration (should-fix 2), a probe created purely to run an assertion
documented as unable to fail (should-fix 3), SC-8 measuring burst- where
`spec.md` specifies paced-locking (should-fix 4), planning docs still
stating six/seven facts against a delivered 8/8 (should-fix 5), and three
coherence snags in this file (should-fix 6) — plus cheap nits (a shared
`PostTokenFormAsync` helper, a shared `ReadRealmRepresentationAsync` +
`RequireProperty` pair removing two redundant master-realm token mints from
the paced fact, `RepositorySource.Root()` reuse in
`SeededCredentialStrengthTests.cs`, named constants for the master-realm
credentials, and comment references naming this file's sections instead of
should-fix numbers). Should-fix 2 needed its own red-first proof, same
phase-4a discipline as should-fix 2 of the prior round:

**Red — a never-locked probe's first poll assertion genuinely fails:**

```
never-locked probe's first poll: status=200
Shouldly.ShouldAssertException : firstPoll.StatusCode
    should not be
HttpStatusCode.OK
    but was
HttpStatusCode.OK
Failed SmartSentinelEye.Integration.Tests.Identity.LockoutThroughputMeasurementTests.TEMPORARY_SF2_ANeverLockedProbesFirstPollAssertionCanFail
```

**Green — the real SC-8 fact, with the same assertion wired in, against a
genuinely locked probe, run twice after every fix in this round landed**
(scaffolding removed, `--no-incremental` rebuild, 8 facts / zero
`TEMPORARY` reconfirmed the same way as above):

```
Run 1: Passed A_temporary_lock_expires_without_administrative_intervention [1 m 2 s]
       still locked at 0s (confirmed refused on first poll)
       measured lock duration: 61,4s
Run 2: Passed A_temporary_lock_expires_without_administrative_intervention [1 m 1 s]
       still locked at 0s (confirmed refused on first poll)
       measured lock duration: 61,3s

Run 1: Total tests: 8 / Passed: 8 (3.58 min)
Run 2: Total tests: 8 / Passed: 8 (3.60 min)
```

Both runs also show should-fix 3's collapsed fact running in **106ms** and
**41ms** (`A_grant_with_no_username_is_refused_as_invalid_request_not_invalid_grant`,
no probe lifecycle left to pay for) and the paced fact's figures unchanged
under the consolidated realm-read helpers (burst 2, paced 10, loop elapsed
10.8s / 10.9s) — the helper consolidations changed nothing about what this
file measures, only how many redundant HTTP calls it takes to measure it.
`docker ps -a` and `docker volume ls` both empty after, same as every prior
round.

## SC-10 — blast radius, re-measured at the delivery SHA

Reproduced with the exact command from `spec.md` §*Blast radius*:

```powershell
$pat='Admin1234|Operator1234|Wall-munich-1234|Wall-dresden-1234|Wall-berlin-1234|Wall-hamburg-1234'
Get-ChildItem -Recurse -File | Select-String -Pattern $pat -AllMatches |
  ForEach-Object { $_.Matches.Count } | Measure-Object -Sum
```

**Re-measured again after should-fix 4's phase-6 change to
`SeededCredentialStrengthTests.cs` (the two literal password constants
became computed values, so that file no longer contributes at all) and after
this file's own subsequent growth. Result at final delivery: 101 files / 205
occurrences**, against **97 files / 158 occurrences** recorded at `b6ddf7df`
(this branch's merge base).

**The drift is fully explained by this delivery's own tracked artefacts —
now four files, not three, because `verification.md` grew substantially
through the phase-6 round and now independently matches the pattern too
(quoting live server output that names the passwords, and the reproduction
command's own `$pat` string, which contains all six as substrings):**

| Area | At `b6ddf7df` | Now | Delta | Explanation |
|---|---|---|---|---|
| `src/AppHost/Realms` | 1 file / 12 occ. | 1 / 12 | none | unchanged |
| `tests/` | 43 / 60 | 43 / 60 | none | `SeededCredentialStrengthTests.cs` briefly added 2 occurrences at first delivery, then should-fix 4 replaced both literal constants with computed values — net zero |
| `e2e/` | 8 / 8 | 8 / 8 | none | unchanged |
| `scripts/` | 3 / 3 | 3 / 3 | none | unchanged |
| `README.md` | 1 / 2 | 1 / 2 | none | unchanged |
| `specs/**/*.md` | 41 / 73 | 45 / 120 | +4 files / +47 occ. | this spec's own four tracked artefacts: `spec.md` (31), `plan.md` (5), `tasks.md` (3), `verification.md` (8) |
| **Total** | **97 / 158** | **101 / 205** | **+4 / +47** | |

Excluding this delivery's own four files (`specs\219-the-guess-a-username-already-made\{spec,plan,tasks,verification}.md`
— 31 + 5 + 3 + 8 = 47 occurrences), the figures reproduce **exactly**:
41 pre-existing `specs/**/*.md` files + 43 (`tests/`) + 8 (`e2e/`) + 3
(`scripts/`) + 1 (`README.md`) + 1 (the realm) = 97 files, carrying
73 + 60 + 8 + 3 + 2 + 12 = 158 occurrences — `b6ddf7df`'s own totals exactly.
No pre-existing file drifted; the count moved only because this delivery's
own artefacts joined the corpus they measure, and `verification.md` kept
growing as later rounds of review were recorded in it.

**Re-run once more after this second code-review round's edits landed
(guardrail: re-verify whenever this file changes again, since the figure has
already been corrected three times): 101 files / 205 occurrences — unchanged
from the measurement above.** This round added substantial prose (should-fix
1's corrected SC-11 arithmetic, should-fix 4's deviation note, the "third
round of review" confirmation section) but none of it quotes any of the six
literal passwords — `verification.md`'s own count stayed at 8 occurrences,
so the total did not move this time. Confirmed, not assumed.

**A prior draft of this table (mid phase-6 round) stated 101/199 with a
three-file, +39-occurrence delta and a note explicitly declining to
re-measure `verification.md`'s own contribution "so as not to chase a total
that moves every time this file is edited."** That caution was reasonable in
isolation but produced a table that no longer reconciled once should-fix 4
zeroed out `tests/`'s contribution — re-verified here at delivery's end,
once, rather than deferred again. (Also corrects an intermediate, second-hand
reconciliation of 100/197 relayed during this same review round, which
undercounted by the same `verification.md`-contribution gap — this table is
the direct re-measurement, not a relay.)

**Structural claims reconfirmed:**

- `grep -rl "const string OperatorPassword" tests/` → **34** files (no
  central constant), matching `spec.md` exactly.
- `grep -rl "const string AdminPassword" tests/` → **2** files
  (`AspireFixture.Auth.cs`, `AuditObservability/RunModeStackAddress.cs`),
  matching `spec.md` exactly.

## SC-11 — wall-clock time to exhaust the candidate set

**Revised at phase-6 re-review (should-fix 5): the paced loop's own elapsed
time is now a captured measurement, not a number derived from prose.**
`PacedUntilLockedAsync` wraps its loop in a `Stopwatch`; the two final
confirmation runs (should-fix 2/3's own last live runs) logged
**10.9s** and **10.8s**. Using 10.85s:

```
sustainable attempts/hour (first lock) = paced attempts ÷ (paced loop's measured elapsed time + lock duration) × 3600
                                       = 10 ÷ (10.85s + 61.2s) × 3600
                                       ≈ 10 ÷ 72.05 × 3600
                                       ≈ 500 attempts/hour

time to exhaust a 7-candidate set at that rate = 7 ÷ 500 × 3600 seconds ≈ 50 seconds
```

**A deviation, named plainly (should-fix 4, phase-6 re-review): the 61.2s
lock duration was measured against a *burst*-locked probe, but `spec.md`'s
own SC-8 explicitly specifies a paced one** — its Given clause reads "a
**paced** throwaway account that has just been refused its correct
password" (`spec.md` line 324). `plan.md`'s own fact table never named
burst or paced for SC-8 (an earlier draft of this note blamed that silence
alone, which understated it — `spec.md` does specify, and the delivered
fact departs from it). The consequence is concrete: **the arithmetic above
multiplies a *paced* attempt count (10) by a *burst*-derived lock duration
(61.2s) — a quick-login-triggered lock standing in for a failure-factor-
triggered one, two mechanisms this file's own SC-7 exists to show are
different.** Both observed lockouts started from `numTemporaryLockouts: 0`,
so the same base wait plausibly applies to either trigger, but this was
never separately confirmed against a paced-locked probe. The burst
measurement is kept — it is the one this delivery actually has, and
re-measuring against a paced lock would cost another live session for a
number this section's own conclusion (below) shows does not change the
answer — but SC-11's denominator is a mix of two lock types, named here
rather than left implicit.

**This ≈500/hour figure is a *first-lock* rate, not a sustained one —
phase-6 review caught this too.** The realm's `waitIncrementSeconds: 60`
grows the wait on each subsequent lockout of the *same* account, capped at
`maxFailureWaitSeconds: 900`. This file never measured that growth (every
probe here is locked exactly once, then deleted), but the realm's own
configured values bound it: a sustained attacker facing repeated lockouts on
one account trends toward the 900s cap, not the 61s first-lock wait —

```
sustained attempts/hour (approaching the cap) ≈ 10 ÷ (10.85s + 900s) × 3600 ≈ 40 attempts/hour
```

— which is what actually makes ≈500/hour a *conservative upper bound*: not
the arithmetic itself (corrected below to not need that framing for a
different reason), but the fact that a real, sustained attacker against a
single account would face a rate closer to 40/hour than 500/hour after
repeated lockouts. Neither figure was directly measured past the first lock;
both are stated as bounds from the realm's own configured values, not
observations.

**Independent of both figures above, the mechanical formula is answering the
wrong question for this delivery's actual finding, and stating either rate
without the correction below would understate the exposure** — exactly the
miscalibration `spec.md` §*Blast radius* correction 2 already warns a
reviewer against for the "raise the length" remedy. The formula assumes an
attacker needs to spend a sustainable rate trying candidates one at a time
until the set is exhausted, drawn from a large pool — the top-1000-list
framing #2510 opened with. Half A's actual finding is that the pool is far
smaller than that: each of the seven username-derived accounts is opened by
one of **three** candidate suffixes (`SeededCredentialStrengthTests.cs`'s
own `DerivationSuffixes`), not a candidate set numbered in the hundreds —
but three is not one, and the corrected arithmetic below (should-fix 1)
accounts for the two wrong guesses an unlucky ordering could cost before the
right one lands.

**Corrected at phase-6 re-review (should-fix 1): the paragraph this replaces
claimed "no account is ever sent a second guess," which is wrong, and it
cited a "1–2 seconds of network round-trip" figure with no transcript behind
it — exactly should-fix 5's defect, reintroduced one section later.**
`SeededCredentialStrengthTests.cs`'s own `DerivationSuffixes` defines
**three** candidates per account (`"1234"`, `"-1234"`, `"_1234"`), not one —
the seven derivable accounts don't all use the same suffix (`wall-*` uses
`-1234`; `admin`/`operator` use `1234`). An attacker who does not already
know which suffix applies to a given account has up to three guesses to
make, and this file's own burst measurement trips the lockout at exactly
**two** wrong attempts. So for any account whose correct suffix is not the
first one tried, the lockout can genuinely engage.

**The corrected arithmetic, stated as cases rather than a single invented
number:**

- **Correct suffix guessed first:** 1 request, 0 failures, succeeds
  immediately. No lockout interaction.
- **Correct suffix guessed second** (one wrong guess first): the account's
  failure counter reads 1 at that point, below this file's own measured
  trip point of 2 — the second, correct guess should still succeed. 2
  requests, still no lockout interaction.
- **Correct suffix guessed third** (the worst case: both wrong suffixes
  tried first): the account locks after the second wrong guess — measured
  in this file at exactly 2 wrong attempts, every run. The third, correct
  guess is refused. The attacker must wait out the lock — measured here at
  61.1–61.2s (±5s, the poll interval) — before it succeeds.

**Worst-case wall-clock per account is therefore on the order of one lock
cycle, ≈61s** — not an invented "1–2 seconds." This file does not measure
HTTP request latency at all and makes no claim about it; the ≈61s is the
measured *lock* duration, dominant enough that a handful of round trips
beside it are noise.

**Aggregate wall-clock across all seven accounts is stated as bounds, not a
single figure, because it depends on an attack pattern nothing here
measured** — sequential guessing across accounts versus concurrent (SC-9
established one account's lock does not affect another's, so a concurrent
attacker could absorb all seven worst-case waits at once, but no fact here
sends concurrent requests across multiple probes; this is an inference from
SC-9, not an observation): **best case (every suffix guessed right first,
across all seven accounts) is a handful of HTTP requests, well under a
second in aggregate. Worst case run sequentially (every account needs its
full lock cycle, one after another) is on the order of seven minutes. The
same worst case run concurrently is on the order of one lock cycle, ≈61s.**

Neither the ≈500/hour first-lock figure nor the ≈40/hour sustained-cap
figure earlier in this section is the right one for these seven accounts —
both describe an attacker who has to keep guessing many times against one
account from a large candidate pool, which is not this finding. The
candidate pool here is three per account, not hundreds or thousands, so even
the worst per-account case is reached far sooner than either rate would
predict for a genuine brute-force search.

## Latency

**N/A.** No leg of the event-to-overlay path (constitution §IV) is touched.
This delivery adds two test files and this note; it changes no `src/` file,
no realm field, and nothing on the camera → SFU → decode → presentation
buffer → event → overlay → composite path. Half B is
`[Trait("Category", "Measurement")]` and excluded from CI's integration job,
so it adds nothing to CI wall time either.

## Summary for the PR body

- SC-1 … SC-4: `SeededCredentialStrengthTests`, CI-enforced, green
  (452/452 in the full `Architecture.Tests` suite), with a five-row
  counterfactual proving every one of Half A's original numeric claims can
  fail, plus (phase-6, should-fix 4) two additional value-agnostic facts —
  resilient to a lazy password rotation, proven falsifiable by its own
  counterfactual — guarding the same cross-fab and cross-admin reuse without
  pinning a literal password string.
- SC-5 … SC-9: `LockoutThroughputMeasurementTests`, now eight facts, run
  across five live-stack sessions total (three original + two phase-6
  confirmation runs), stable figures every time (burst 2, paced 10, lock
  61.1–61.2s), with a two-attempt counterfactual whose *first* attempt's
  failure is itself a recorded finding — Keycloak masks *locked* vs.
  *locked-and-also-disabled* behind the same wire refusal, but not
  *disabled-and-never-locked*, which gets a distinguishable
  `"Account disabled"` — and whose second attempt proves the real instrument
  specific.
- **A named finding beyond the spec's own scope**: the token endpoint is an
  unauthenticated account-state disclosure oracle for "disabled" (not for
  "locked") — a caller can learn whether any named account is currently
  disabled from the refusal text alone. Keycloak's default behaviour,
  recorded, not remediated here.
- **Phase-6 review found and fixed five issues, four needing live-stack
  proof**: should-fix 1 corrected this file's own mischaracterisation of the
  disclosure oracle (above); should-fix 2 replaced an unfalsifiable
  bad-request counter check, discovering along the way that the proposed
  repair itself didn't match Keycloak's real behaviour, and split into two
  honestly-labelled facts; should-fix 3 found and fixed a probe-leak path on
  the exact "lockout isn't tripping" failure this file exists to catch,
  proven before/after by a same-process counterfactual after discovering
  `AspireFixture` tears down completely between separate processes;
  should-fix 4 (highest severity) replaced two literal-pinned facts'
  single-point-of-failure with two additive, value-agnostic ones, proven
  falsifiable by its own counterfactual; should-fix 5 replaced a
  prose-derived "≈10s" with a captured `Stopwatch` measurement and added the
  `waitIncrementSeconds` growth caveat SC-11 was missing.
- SC-10: blast radius re-measured, drift fully explained by this delivery's
  own artefacts, structural claims (34 / 2) reconfirmed unchanged.
- SC-11: met. First-lock formula figure ≈ 500/hour; a sustained-attack
  figure, bounded by `waitIncrementSeconds`' growth toward the 900s cap, is
  ≈ 40/hour. Neither is the right figure for this delivery's actual finding.
  The candidate set per account is **three** suffixes, not one
  (`DerivationSuffixes`), and this file's own burst measurement locks at
  exactly two wrong attempts — so the lockout genuinely can engage when the
  correct suffix isn't tried first. Corrected, stated as bounds rather than
  a single invented number: best case (right suffix first, all seven
  accounts) is a handful of HTTP requests, well under a second; worst case
  is on the order of one lock cycle (≈61s) per account, run sequentially up
  to ≈7 minutes worst case, or ≈61s if an attacker runs the seven
  concurrently (an inference from SC-9, not separately measured). SC-8 also
  measured burst-, not paced-, locking — a deviation from `spec.md`'s own
  Given clause, named rather than left implicit; SC-11's denominator mixes
  the two lock types as a result.
- SC-12: this file states the human decision's two costs and picks neither
  (see the PR body).
- SC-13: realm byte-identical throughout; zero probe residue proven
  same-process (the cross-process sweep's limits are recorded, not hidden);
  zero moved seeded-account failure counters.
