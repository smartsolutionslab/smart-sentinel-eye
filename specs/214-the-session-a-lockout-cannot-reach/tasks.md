# Tasks — Spec 214, the session a lockout cannot reach (#2509)

Phase 3 of ADR-0037. Read `spec.md` and `plan.md` first; this file assumes both.

Format: `[ID] [P?] [Story]` — `[P]` means the task owns files disjoint from
every other `[P]` task at the same step (ADR-0109).

**Phase-4a colour:** **CHARACTERISATION, OBSERVED GREEN**, with a mandatory
counterfactual (T003). Declared by the architect at phase 3 and not the
engineer's to change after seeing the code (ADR-0144). The reasoning is in
`plan.md` §*Phase-4a colour*: no production line moves, so there is no new
behaviour to see fail — and because a characterisation test can be written so
it cannot fail, T003 is what makes T001 evidence rather than transcription.

**Phase 4b:** *skipped — this delivery adds tests and records a finding; there
is no production code to write.* Say exactly that in the PR body, per
ADR-0037's skip rule. **This is not a licence to skip 4a**, which ADR-0144
exempts nothing from.

**Standing check before trusting any manual observation:** compare the AppHost
process's start time against the commit under test. A persistent stack keeps
serving the binaries it booted with.

**Standing check on secrets:** this is a public repository and T001's happy
path holds a live token pair. Assert on status, `error`, and token
presence/difference — never on token text, and never put a response body in a
failure message. `BruteForceLockoutIntegrationTests` already carries this rule;
follow it.

---

## US1 (P1) — a live session's refresh, put to the running server

**[T001] [P] [US1] Write `tests/Integration.Tests/Identity/LockoutSessionSurvivalIntegrationTests.cs` covering SC-1 through SC-5 and the bad-request case, run it, and report the output verbatim.**

Agent: `test-writer`. Owns one new file; touches nothing else.

New file, `[Collection(AspireCollection.Name)]`, namespace
`SmartSentinelEye.Integration.Tests.Identity`, primary constructor
`(AspireFixture aspire)` — mirror
`BruteForceLockoutIntegrationTests.cs` for shape, naming (sentence-style with
underscores, ADR-0053), Shouldly assertions (ADR-0052), and cleanup discipline.
Do **not** edit that file, and do **not** widen `RealmProbe` (`plan.md` says
why).

Facts to write, one per scenario:

| Fact | Scenario | The claim |
|---|---|---|
| `A_fresh_account_is_issued_both_an_access_token_and_a_refresh_token` | SC-3 | control on the mint — the pair exists before anything is locked |
| `A_locked_out_account_can_still_spend_its_refresh_token` | **SC-1** | **the observation.** Expect `200` and a new access token |
| `The_locked_out_account_is_refused_its_correct_password_in_the_same_window` | SC-2 | control on the lock: `400` + `error == "invalid_grant"`, **and** the attack-detection record reads `disabled == true` |
| `A_second_account_is_unaffected_while_the_first_is_locked` | SC-5 | blast radius; second account is `AspireFixture.AdminUsername`, read only, never locked |
| `A_malformed_refresh_token_is_refused_without_moving_the_failure_counter` | bad request | read `numFailures` before and after; a malformed refresh is not a failed login |

Reuse, do not reinvent: `aspire.CreateKeycloakClient()` for the base address
(**never** a literal host or port — Keycloak is reached via
`GetEndpoint("keycloak")` with no endpoint name, and a token minted at the
container's mapped port carries an issuer the services reject),
`RealmProbe.Realm`, `RealmProbe.AuthorisedAdminClientAsync`,
`RealmProbe.ReadJsonAsync`, `AspireFixture.ClientId`.

Two new private helpers, local to this file (`plan.md` gives the signatures):
`PostTokenAsync` for raw form posts, and `ReadTokenPairAsync` which **fails
loudly when `refresh_token` is absent** rather than returning an empty string —
an absent refresh token would make SC-1 pass vacuously.

Locking: create a per-fact throwaway `lockout-session-probe-<guid:N>` user,
read the realm's `failureFactor`, post `failureFactor + 1` wrong guesses in a
tight loop. Assert `disabled == true`; assert `numFailures >= 1` **only** —
never `>= failureFactor`. `quickLoginCheckMilliSeconds: 1000` trips first and
`numFailures` reads `2`; spec 207 recorded this after its own `spec.md` got it
wrong live. Delete the probe user in a `finally`, status-checked.

*Expected colour: **green**. This characterises behaviour that already exists.*

**If SC-1 comes out red — `400 invalid_grant` — stop and report. That is the
severe finding, not a test to fix.** Do not weaken the assertion, do not edit
the realm, do not pick a mitigation. Report the verbatim request and response
and hand off to T006.

**If SC-2 cannot get the account locked at all**, report that rather than
looping harder in new ways: a realm whose detector no longer trips is itself a
finding about spec 207, and it invalidates SC-1 either way.

---

**[T002] [P] [US2] Write `e2e/wall-survives-a-lockout.spec.ts` covering SC-6, run it, and report the output verbatim.**

Agent: `test-writer`. Owns one new file. Disjoint from T001 — different
language, different runner, different directory.

Copy the rig from `e2e/wall-survives-a-process-death.spec.ts`: `mkdtemp`
persistent Chromium profile **outside `test-results/`** (CI uploads that on a
public repo and the profile holds `wall-munich`'s offline refresh token),
sign-in as `wall-munich` / `Wall-munich-1234` at `http://localhost:5175/`,
`expireStoredAccessToken(page)`, the network collector for `grantTypes` and
`providerPrompts`, `claimsOf` for `azp`. Do **not** edit that spec.

Sequence: sign in → open a layout → lock `wall-munich` with rapid wrong
password grants at `management-web`'s token endpoint → confirm locked (correct
password refused) → `expireStoredAccessToken` to force `automaticSilentRenew` →
assert.

Assert all four, because "the layout is still visible" alone times out with a
message that names nothing:

- `[data-testid="identity-not-authorized"]` (the `NotAuthorizedScreen`, at
  `apps/kiosk-web/src/features/auth/NotAuthorizedScreen.tsx:23`) has count `0`
  — **this is the one that names the severe branch in the failure text**;
- no sign-in prompt anywhere (`/sign in/i` count `0`, no `#username`);
- `grantTypes` includes `'refresh_token'` and `providerPrompts` has length `0`
  — it renewed without being handed to the provider;
- the renewed `access_token` differs from the spent one, and `azp` is
  `kiosk-wall`.

**Teardown is mandatory and is not a `finally` you may omit.** `wall-munich` is
a seeded realm account — deleting it is not available as a fallback. Clear the
lock with `DELETE /admin/realms/{realm}/attack-detection/brute-force/users/{id}`
even when the test fails, or the next e2e project in the same CI run signs in
as `wall-munich` and fails for a reason that reads exactly like a regression.

**Write nothing into the profile.** `localStorage` is where the grant lives
(ADR-0131); `expireStoredAccessToken` edits in place and is the only sanctioned
touch. A `localStorage.setItem` here would make the test the reconstruction it
exists to replace — the sibling spec's own doc comment says so.

*Expected colour: **green**.* Red here, showing `NotAuthorizedScreen`, is A3
observed: hand off to T006.

*If the stack cannot be booted in this pass, say so explicitly and hand `T002`
on as unexecuted rather than reporting a colour it did not observe. `T001`'s
result is sufficient to unblock `T003` and `T004`; `T002` is required before
the PR can claim SC-6.*

---

**[T003] [US1] Prove SC-1's instrument by counterfactual — add SC-4 and demonstrate it refuses.**

Agent: `test-writer`. Same file as T001, so **not** `[P]` with it; depends on
T001.

Add `A_disabled_account_cannot_spend_its_refresh_token`: mint a pair for a
throwaway user, `PUT /admin/realms/{realm}/users/{id}` with `{"enabled": false}`
(a different mechanism from a brute-force lockout — it takes the
`if (!user.isEnabled())` branch of Keycloak's own refresh path), post
`grant_type=refresh_token`, expect `400` + `error == "invalid_grant"`.
Re-enable, then delete, both status-checked, neither swallowing the other's
failure.

**Then demonstrate the counterfactual and quote it.** Temporarily invert SC-1's
assertion (expect `400`), run, and show it failing; revert. Report both outputs
verbatim. Without this, SC-1 is an assertion that checks its own input — five
of those turned up in this repository in a single week, and a characterisation
test is exactly where they hide.

*Expected colour: SC-4 **green**; the inverted SC-1 **red**, quoted, then
reverted.* If inverted SC-1 passes, something is wrong with the instrument —
report, do not proceed.

---

**[T004] [US1] Amend spec 207's open question in place.**

Agent: whoever runs T005. Depends on the answer being **observed** (T001 plus
T005 or CI), not merely predicted.

`specs/207-a-guess-that-runs-out/spec.md` §US1 currently reads "**Open
question, not resolved here:** … Recorded as unresolved rather than assumed
either way." Replace the last sentence with the observed answer and a pointer
to `specs/214-the-session-a-lockout-cannot-reach/verification.md`. Keep the
question text — the record should show what was open and what closed it.

This is SC-D, and it is not bookkeeping: a question recorded as open after it
is answered is the same clerical defect as §IV recording a built leg as
unbuilt, which this repository has had to correct.

**Do not write this until the answer is observed.** Writing the predicted
answer into spec 207 would be the exact failure this spec exists to end.

---

## Delivery

**[T005] Phase 5 — verify (`/verify`), and write `verification.md`.**

Depends on T001, T002, T003.

Walk `spec.md` §*Independent end-to-end test procedure* against a booted stack,
using `operator` rather than a wall account, and quote step 6's request and
response verbatim. Record the running Keycloak version (G1). Follow specs 211
and 212 for the note's shape: phase 4a/4b sections stating what was
independently re-confirmed, then a phase-5 section, then an explicit
`Latency: **N/A** —` line with the reason rather than an omission.

**If Aspire cannot be booted here, defer to CI rather than skipping — and say
which.** Three deliveries in this session already did; a second concurrent boot
on this machine produces `FailedToStart` that reads exactly like a code defect.
Use 212's wording pattern: state plainly it was not attempted locally, give the
measured resource evidence, name the job, and commit to reading the job's own
log rather than its conclusion.

The two jobs and the exact strings to look for:

- `integration tests (Docker)` — the five/six facts of
  `LockoutSessionSurvivalIntegrationTests`, by name, especially
  `A_locked_out_account_can_still_spend_its_refresh_token`.
- `e2e (Playwright, full stack)` — `wall-survives-a-lockout.spec.ts`.

**`develop` has no required status checks, so this reading is the actual gate,
not a formality.** A skipped, cancelled, or never-allocated job does not merge
on the unit evidence alone. A green run keeps its `--logger trx` artifact;
download it rather than calling the result unknown. And a re-run erases a
failure from CI history — download the log before re-running anything.

---

**[T006] Severe branch only — file the follow-up, label it for a human, and stop.**

Runs **only** if T001's SC-1 or T002's SC-6 came out red. Skip it entirely on
the benign branch and say so in the PR body.

File an issue carrying: the verbatim request and response; the A3 consequence
(`spec.md`) — the wall goes dark at the next silent renew, not at token expiry,
and does not come back on its own because `invalid_grant` classifies as
`refused`, unmounts the router and calls `stopSilentRenew`; and the candidate
remedies from `spec.md` §*Out of scope* with their costs. Label it the way
#2510 is, and state in the body that the lane did not choose a remedy because
ADR-0144 forbids it to weaken or strengthen a security control on its own
judgement.

Then ship the tests with SC-1 (or SC-6) asserting the **observed** behaviour,
and say plainly — in the test's doc comment *and* the PR body — that the
assertion encodes a defect being tracked, not an intended guarantee. **Do not
edit the realm. Do not pick a mitigation.**

---

**[T007] Phase 6 + 7 — review, then PR.**

`security-reviewer` first (this touches the auth trust boundary and handles
live tokens), then `/code-review`, then the PR against `develop`
(`--base develop`, ADR-0028).

The PR body must carry: the phase-4a colour and why it is characterisation
rather than red; T003's counterfactual output quoted, both directions;
`Phase 4b: skipped — investigation delivers tests and a finding, no production
code`; `git diff --stat` showing the two existing suites unchanged (SC-F); the
observed answer in one sentence; and, on the benign branch, one line naming the
residual risk (new sign-ins blocked; the ten-hour ceiling for operators with no
offline grant) as #2510's and #2488's business rather than this issue's.

Close #2509 with a closing keyword — and check the issue state after the merge,
because a mention alone usually does not close it.

---

## Ordering

```
T001 [P] ──┬─► T003 ──┐
           │          ├─► T005 ──┬─► T006 (severe branch only) ──► T007
T002 [P] ──┴──────────┘          └─► T004 ────────────────────────┘
```

- T001 and T002 are parallel: disjoint files, disjoint runners.
- T003 shares T001's file — sequential with it, never `[P]`.
- T004 and T006 both need the **observed** answer, so both sit after T005 (or
  after CI's log has been read, if T005 deferred).

## Parallelism (ADR-0109)

| Task | Owns | Shares with |
|---|---|---|
| T001 | `tests/Integration.Tests/Identity/LockoutSessionSurvivalIntegrationTests.cs` (new) | T003 only |
| T002 | `e2e/wall-survives-a-lockout.spec.ts` (new) | nothing |
| T003 | same file as T001 | T001 |
| T004 | `specs/207-a-guess-that-runs-out/spec.md` | nothing |
| T005 | `specs/214-the-session-a-lockout-cannot-reach/verification.md` (new) | nothing |

No task edits `src/`, the realm, `BruteForceLockoutIntegrationTests.cs`, or
`wall-survives-a-process-death.spec.ts`. If a task finds itself wanting to,
that is the signal to stop and report, not to proceed.

## Task table

| ID | [P] | Story | Agent | File(s) | Depends on |
|---|---|---|---|---|---|
| T001 | [P] | US1 | test-writer | `LockoutSessionSurvivalIntegrationTests.cs` | — |
| T002 | [P] | US2 | test-writer | `e2e/wall-survives-a-lockout.spec.ts` | — |
| T003 | | US1 | test-writer | `LockoutSessionSurvivalIntegrationTests.cs` | T001 |
| T004 | | US1 | verifier | `specs/207-…/spec.md` | T005 |
| T005 | | — | verifier | `specs/214-…/verification.md` | T001, T002, T003 |
| T006 | | — | orchestrator | GitHub issue | T005 (severe branch only) |
| T007 | | — | reviewer + orchestrator | PR | T004, T006 |
