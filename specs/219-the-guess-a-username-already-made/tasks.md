# Tasks 219 — The guess a username already made

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2510
**Branch:** `2510-password-policy-exhaustibility` · **Worktree:** `D:\Github\sse-2510`
**Merge base:** `b6ddf7df` (`origin/develop`)

**Phase-4a colour: CHARACTERISATION, OBSERVED GREEN, with a mandatory
counterfactual** — both halves. Declared in `plan.md` §1; the reasoning is
there and phase 4 does not re-decide it. **A test in this spec arriving red is
a defect in the test, not the evidence** — there is no `src/` change for it to
be red against.

**Phase 4b: skipped** — `Phase 4b: skipped — this delivery adds two test files
and records a finding; there is no production code to write.` Goes in the PR
body per ADR-0037's skip rule. Same shape as spec 214 (#2509).

**No task is `[P]` inside Half B**, and that is a shared-resource constraint,
not a file-ownership one (ADR-0109): there is one Keycloak, one realm, and
these facts mutate its attack-detection state. Spec 207's `tasks.md` states
the same rule for the same reason.

---

## Dependency graph

```
T001 [P] ──┬─→ T003 ──┐
T002 [P] ──┴─→ T004 ──┼─→ T006 ─→ T007 ─→ T008
T005 [P] ─────────────┘
```

`T001`, `T002` and `T005` own disjoint files (different projects; T005 writes
nothing) and may run in parallel. Everything downstream of them is serial.

---

## Phase 4a — the tests

### T001 [P] [US1] Half A — `SeededCredentialStrengthTests`

**Owns:** `tests/Architecture.Tests/SeededCredentialStrengthTests.cs` (new).
**Touches nothing else.**

Write six facts, per `plan.md` §2. Design constraints that are *not*
negotiable at implementation time, because each is the reason a number in
`spec.md` is trustworthy:

- **Parse `passwordPolicy` out of the realm JSON.** Never hard-code the
  policy string or its clause values. An unrecognised clause **throws**,
  naming the clause — it does not warn, skip, or default to `true`.
- **Model `specialChars(n)` and `notUsername(...)` too**, though today's
  policy uses neither. `plan.md` §2.2 carries the justification and marks it
  as a weighed judgement against ADR-0036.
- **Exclude `service-account-*` users** from `HumanAccounts()`. Five exist;
  including them makes the census wrong invisibly.
- **Assert the census — 12 credential blocks, 6 distinct values** — as its own
  fact. It is the drift guard every other number rests on.
- **Put the numbers in the Shouldly assertion message**, not only in the
  expected value, so a failure reads as "7 of 12 expected, 6 found: …" rather
  than "Expected 7".
- The derivation suffix set is a `private static readonly string[]` with a
  comment naming it assumption **G1** and citing `spec.md` §*Assumptions*.
- `StringComparison.Ordinal`; `ToUpperInvariant`, never `ToUpper`.

House rules: `Ensure.That(x).IsNotNull()` for reference arguments (ADR-0105,
never `ArgumentNullException.ThrowIfNull`); `List<T> x = [];` collection
expressions; no leading-underscore fields; sentence-style test names
(ADR-0053); Shouldly (ADR-0052).

**Done when:** `dotnet test tests/Architecture.Tests` green, six facts, run
locally and quoted.

**Covers:** SC-1, SC-2, SC-3, SC-4.

---

### T002 [P] [US1] Half B — `LockoutThroughputMeasurementTests`

**Owns:** `tests/Integration.Tests/Identity/LockoutThroughputMeasurementTests.cs`
(new). **Does not edit** `BruteForceLockoutIntegrationTests.cs`,
`LockoutSessionSurvivalIntegrationTests.cs`, `RealmProbe.cs`,
`AspireFixture*.cs`, the realm, or any `ci-shards/*.filter`.

Attributes: `[Collection(AspireCollection.Name)]` **and**
`[Trait("Category", "Measurement")]`. The trait is load-bearing — `plan.md`
§3.1 — and it is what keeps this class out of CI and out of PR #2535's shard
partition.

Seven facts:

| # | Fact | SC |
|---|---|---|
| 1 | `A_fresh_probe_accounts_correct_password_mints_a_token` | SC-5 |
| 2 | `The_running_realm_declares_the_password_policy_the_import_file_carries` | SC-6 |
| 3 | `A_bursting_caller_is_refused_after_the_quick_login_check_trips` | SC-7a |
| 4 | `A_paced_caller_is_admitted_more_attempts_than_a_bursting_one` | SC-7 |
| 5 | `A_temporary_lock_expires_without_administrative_intervention` | SC-8 |
| 6 | `A_seeded_account_authenticates_while_a_probe_is_locked` | SC-9 |
| 7 | `A_grant_with_no_username_is_refused_as_invalid_request_not_invalid_grant` | bad-request |

**The four traps, each already paid for once — `plan.md` §3.4:**

1. Create every probe user with `firstName`, `lastName` **and** `email`. The
   realm's declarative User Profile requires all three; without them the token
   endpoint answers `invalid_grant` / "Account is not fully set up", which
   reads exactly like a lockout. Fact 1 is the control that catches it.
2. Read the realm through the **master realm**
   (`client_id=admin-cli`, `username=admin`, password `testkeycloak` under the
   fixture — `AspireFixture.cs:312-318`). `identity-admin` returns a partial
   representation; spec 207's phase 6 lost a whole run to that. **A missing
   field is an escalation, not a default.**
3. `aspire.CreateKeycloakClient()` only — `App.GetEndpoint("keycloak")` with
   no endpoint name. A token minted at the container's mapped port carries an
   issuer the services reject.
4. `finally` + status-checked `DELETE .../users/{id}`; a `catch { delete;
   throw; }` around reset-password so a half-created user cannot leak.
   Usernames `$"policy-probe-{Guid.NewGuid():N}"`.

**Three prohibitions:**

- **Never** send a wrong password to a seeded account.
- **Never** `DELETE` the attack-detection record inside fact 5 — waiting the
  lock out *is* the measurement, and clearing it is the shortcut spec 207 took
  that left this number unobserved.
- **Assert no threshold** on the throughput figure. A threshold is a policy
  judgement; ADR-0144 keeps the lane out of it. Facts 3–5 assert structure
  (`disabled == true`; paced attempts **>** burst attempts; the lock ends) and
  *record* the numbers via test output.

Fact 5 uses a `Stopwatch` and a bounded poll (correct password every 5 s,
hard failure at 1020 s = `maxFailureWaitSeconds` 900 + 120 margin). A timeout
is a failure, not a skip.

**Done when:** the file compiles and `dotnet test tests/Integration.Tests
--filter "FullyQualifiedName~LockoutThroughputMeasurementTests"` has been
*attempted*; a green run is T004's job, not this task's — see G3.

**Covers:** SC-5 … SC-9, bad-request, auth/scope.

---

### T003 [US1] Half A's counterfactual — prove each number can fail

**Depends:** T001. **Owns:** no committed file; produces captured output.

Characterisation observed green is worthless without this. For **each** of
Half A's four numeric claims, construct what the assertion claims to catch and
show it caught. Work against a **scratch copy** of the realm JSON in the
scratchpad, or a temporarily-edited working copy reverted immediately —
`git diff src/AppHost/Realms/` must be empty when this task ends.

| Perturbation | Must fail |
|---|---|
| change one seeded password to `short1` | SC-1 (policy admits all) |
| add a 13th human account | the census fact |
| change `operator`'s password to something not username-derived | SC-2's "seven" |
| move `op-berlin@berlin.test` to `/fabs/munich` | SC-3's "four fabs" |
| make `ParsePolicy` return `_ => true` | SC-4 |

**Capture the verbatim failure output of every row.** It is quoted in the PR
body — the only form of this evidence a later reader can check. **A row that
does not fail is a defect in T001**, found here rather than by the reviewer:
fix T001 and redo the row.

Beware the repository's own trap: *a restored file keeps its old timestamp*,
so MSBuild skips the rebuild and the test still fails after you revert. Touch
the file or `dotnet build --no-incremental` before the confirming green run.

**Covers:** the phase-4a evidence gate.

---

### T004 [US1] Half B's live run — the measurement

**Depends:** T002. **Owns:** captured output only.

1. **Stop any other Aspire stack first.** One machine, one stack — a second
   concurrent boot gives `FailedToStart` that reads exactly like a code
   defect. Check for a `testhost` process naming another worktree; a
   container count is not a liveness test.
2. `docker rm` the persistent `keycloak` container and remove the
   `*keycloak*` volumes. `WithRealmImport` runs only against a fresh volume;
   skipping this gives a stack that looks healthy and runs the old realm.
3. Run the class. Capture **verbatim**: burst attempt count, paced attempt
   count, the measured lock duration in seconds, and the attack-detection
   record JSON at the moment of each lock.
4. **Run it twice.** The first run after machine churn looks exactly like a
   regression. Record both.
5. **Counterfactual for Half B:** with a probe locked, set that same probe
   `enabled: false` through the Admin API and confirm the refusal carries a
   *different* signature — proving fact 3's refusal is a lockout and not
   "any refusal at all" (spec 214's SC-4 shape). Re-enable in a nested
   `finally`.
6. Confirm afterwards: no `policy-probe-*` user remains, no seeded account has
   a non-zero `numFailures`, `git diff src/AppHost/Realms/` empty.

**If the machine cannot boot Aspire (G3):** facts 1–4, 6 and 7 can be settled
from a CI `.trx` artefact the way spec 214 settled its six — **but fact 5
cannot**, because `Category=Measurement` is excluded from every CI job. If
fact 5 does not run, **SC-11 is not met**, and T006 and T008 must say so in
those words rather than quote the arithmetic.

**Covers:** SC-5 … SC-9, and the input to SC-11.

---

### T005 [P] [US1] Re-measure the blast radius at the delivery SHA

**Owns:** nothing; read-only. Parallel with T001–T004.

Re-run `spec.md` §*Blast radius*'s PowerShell command at the branch tip and
capture the output. Confirm the six category rows and the totals (97 files /
158 occurrences at `b6ddf7df`). **Any drift is recorded, not silently
corrected** — a number that moved between planning and delivery is itself
worth the reviewer knowing, and this repository has had three wrong figures in
one day from exactly this.

Also re-confirm the two structural claims a find-and-replace remedy depends
on: **34** independent `private const string OperatorPassword` declarations
(no central constant), and **2** `AdminPassword` declarations.

**Covers:** SC-10.

---

## Phase 4b — skipped

`Phase 4b: skipped — this delivery adds two test files and records a finding;
there is no production code to write.`

---

## Phase 5 — verify

### T006 [US1] Write `verification.md`

**Depends:** T003, T004, T005. **Owns:**
`specs/219-the-guess-a-username-already-made/verification.md` (new).

Must contain, each as an observed figure with the command that produced it,
never as an inference:

1. T003's verbatim counterfactual failures, one per row.
2. T004's verbatim output, both runs.
3. **The multiplication (SC-11):** candidate-set size (7 username-derived
   accounts, ~1 candidate each) ÷ measured sustainable attempts/hour =
   **wall-clock time to certainty**. State it as one sentence with a number.
   If T004's fact 5 did not run, write **"SC-11 not met — the rate was not
   measured"** and leave the arithmetic out entirely.
4. T005's blast-radius output.
5. **SC-13's evidence:** `git diff` over `src/AppHost/Realms/` empty; the
   realm re-read showing no probe user and no non-zero failure counter.
6. **Latency:** `N/A` with the reason (no §IV leg touched), not omitted.
7. Anything that contradicted `spec.md` — written down as observed, in the
   file. A measurement reported only to the orchestrator is invisible to every
   grep and every later reviewer.

---

## Phase 6 — review

### T007 [US1] Reviews

**Depends:** T006.

- **`/security-review` — mandatory**, not conditional. `plan.md` §6 names what
  to look at: no new credential literal beyond the probe passwords; no probe
  account or lock can survive a failed run; the blast-radius listing names
  nothing not already in `README.md`.
- `backend-reviewer` on the two C# files: guards (`Ensure.That`),
  `CancellationToken` last, collection expressions, no underscore fields,
  assertion messages carrying the numbers.
- `/code-review`.
- Every finding fixed or refused **in writing**.
- **Verify the state the reviewers left behind** — issue labels, board
  position, and that nothing closed #2510. A subagent has closed an issue
  unasked in this repository before; do not trust a subagent's own report.

---

## Phase 7 — PR

### T008 [US1] Open the PR; file one follow-up

**Depends:** T007.

`gh pr create --base develop` **explicitly** (CLAUDE.md), template body.
Conventional Commits; **no `Co-Authored-By` footer** (ADR-0030, ADR-0086) —
this overrides the session attribution reminder. PR body keeps the Claude Code
line.

**The PR body is a deliverable, not a formality.** It must:

1. Quote T003's counterfactual output verbatim (the phase-4a evidence).
2. Quote T004's measured figures verbatim.
3. State the decision in two columns and **pick neither**:
   - **What raising the policy costs:** the 97-file / 158-occurrence table,
     with the four corrections — 34 independent `OperatorPassword` constants
     and no central one; 6 of 8 affected `e2e/` files are outside `support/`;
     41 `specs/**/*.md`, of which 9 are quickstarts; 3 `scripts/` files nobody
     had listed. Plus: **no seeded password is read from config anywhere**, so
     "make it configurable first" is a real, unbuilt alternative.
   - **What not raising it leaves exposed:** 7 of 12 accounts username-derived;
     one guessed value (`Operator1234`) opening 6 accounts across **4 fabs**,
     including a two-fab account — which corrects #2510's own "for the
     caller's fab"; and the measured wall-clock time to certainty.
   - **The remedy warning:** raising `length(8)` to `length(12)` would fix
     **none** of the seven — `Wall-munich-1234` is 16 characters and
     `Operator1234` is 12. Only a blacklist, a `notUsername`-style rule, or a
     rotation away from the derivation would. Say this plainly; a reviewer
     reading only #2510 would very plausibly pick the ineffective remedy.
4. Say **"this PR does not change `passwordPolicy` and does not rotate any
   password"**, with ADR-0144's reason and the issue's own sentence.
5. Record `Phase 4b: skipped — …`.
6. **Do not use a closing keyword for #2510.** It stays open for the human.
   Reference it (`Refs #2510`). Check the issue state after the merge anyway —
   a mention alone closes roughly one time in three.
7. If SC-11 was not met, say so in the body in those words.

**One follow-up issue to file** (`plan.md` §3.2): promote the duplicated
Keycloak-user helpers (`CreateProbeUserAsync`, `DeleteProbeUserAsync`,
`MasterRealmAdminClientAsync`, `ReadFailureFactorAsync`) out of the now-**three**
copies — `BruteForceLockoutIntegrationTests`,
`LockoutSessionSurvivalIntegrationTests`, `LockoutThroughputMeasurementTests`
— naming `RealmProbe.cs`'s two existing unrelated consumers
(`KeycloakAdminTokenProviderTests`, `MqttAudienceIntegrationTests`) as the
reason it is not a free move. Label `tech-debt`. **Not `agent:ready`** — it is
a behaviour-preserving refactor across three live test classes and wants its
own characterisation run.

```sh
gh project item-add 13 --owner smartsolutionslab --url <issue-url>
```

(needs `gh auth refresh -s project,read:project`; `item-add` prints nothing on
success, and `item-list` defaults to 30 items — verify with `--limit 2000`.)

**#2510 is already on Project #13, status Todo** — verified at planning time
via `gh issue view 2510` (`projects: Smart Sentinel Eye (Todo)`). No board
action needed for the feature issue itself; the phase-3 gate is met.

---

## Traceability

| SC | Task |
|---|---|
| SC-1, SC-2, SC-3, SC-4 | T001, evidenced by T003 |
| SC-5, SC-6, SC-7, SC-8, SC-9 | T002, evidenced by T004 |
| SC-10 | T005 |
| SC-11 | T006 (needs T004 fact 5) |
| SC-12 | T008 |
| SC-13 | T004 step 6, recorded by T006 |
