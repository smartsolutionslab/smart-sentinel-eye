# Tasks — Spec 207, a guess that runs out

**Spec:** `specs/207-a-guess-that-runs-out/spec.md`
**Plan:** `specs/207-a-guess-that-runs-out/plan.md`
**Issue:** #2285 (on Project #13, status **Todo** — verified, no `item-add`
needed)
**Branch:** `2285-realm-brute-force-protection`
**Lane:** autonomous (ADR-0144)

**Phase 4a colour: RED.** This is behaviour-changing security hardening
(constitution §Testing; ADR-0139). A test arriving green is a phase-4 failure,
not a shortcut.

**No task is `[P]`.** Not a file-ownership constraint — a shared-resource one:
there is one Keycloak, one realm, and these tests mutate its lockout state.
See `plan.md` §6.

---

## Phase 4a — tests first (`test-writer`). The engineer may not edit any file below.

### T001 [US1] Write `BruteForceLockoutIntegrationTests` and observe it RED

**File (new, sole owner):**
`tests/Integration.Tests/Identity/BruteForceLockoutIntegrationTests.cs`

**Steps, in order. Step 1 is not optional.**

1. **Drop the Keycloak volume first.** Stop any running AppHost (a running
   host holds the container and MSB3027 looks like a broken build), then
   `docker rm -f keycloak` and remove every `*keycloak*` volume
   (`docker volume ls --format '{{.Name}}'`). A red produced against a stale
   realm is evidence of nothing (`plan.md` §5).
2. Write the file. `[Collection(AspireCollection.Name)]`, constructor-injected
   `AspireFixture aspire`, sentence-style test names with underscores
   (ADR-0053), Shouldly (ADR-0052), `CancellationToken` last on every helper,
   no `ConfigureAwait` (ADR-0049).
3. Run it. **Capture the verbatim output** — that output is phase 4b's brief
   and is quoted in the PR body. Nothing else satisfies the gate.

**The account.** Create a throwaway realm user per run through the Admin API
(`plan.md` §3), never `operator` or `admin`:

- `POST admin/realms/{realm}/users` → `{ username: "lockout-probe-<Guid.NewGuid():N>", enabled: true }`
- `PUT admin/realms/.../users/{id}/reset-password` with a password satisfying
  `length(8) and upperCase(1) and lowerCase(1) and digits(1)` — assert the
  reset succeeded, with a message naming the policy, so a policy change
  fails at setup rather than at the assertion.
- `DELETE admin/realms/.../users/{id}` in cleanup, **status-checked**. An
  unchecked delete leaves residue indistinguishable from a real finding —
  #2166's shape; do not repeat it.
- No group membership. Nothing here calls a fab-scoped endpoint.

**Reach the Admin API through `RealmProbe`'s constants** — `RealmProbe.Realm`,
`RealmProbe.AdminClientId`, `RealmProbe.AdminClientSecret` — but keep the
user / attack-detection helpers **in this new file**. Do not widen
`RealmProbe.cs`: it is shared with `KeycloakAdminTokenProviderTests` and
`MqttAudienceIntegrationTests`, and a third consumer is the moment to promote
a helper, not the first.

**The facts to write:**

| ID | Name (sentence-style) | Asserts |
|---|---|---|
| SC-2 | `A_fresh_account_authenticates_with_its_correct_password` | the positive control, run **before** any wrong guess, same account. Its failure is an **escalation**, not a phase-4a red — stop and report |
| SC-1 | `The_correct_password_is_refused_after_too_many_wrong_ones` | **the reason this spec exists.** `failureFactor + 1` wrong grants, then the correct one → `400`, `error == "invalid_grant"`, and **no `access_token` in the body** |
| SC-3 | `Keycloak_records_the_account_as_temporarily_disabled` | `GET admin/realms/{realm}/attack-detection/brute-force/users/{id}` → **`disabled == true`** is the load-bearing assertion. `numFailures` is asserted `>= 1` and **reported in the failure message**, *not* asserted at `>= failureFactor` — see the trap below |
| SC-4 | `A_second_account_is_unaffected_while_the_first_is_locked` | with the probe locked, `admin` / `Admin1234` still mints a token. This is the blast-radius guard for the whole suite |
| SC-5 | `Clearing_the_lockout_restores_authentication` | `DELETE` the attack-detection record → the correct password is accepted again. **An explicit DELETE, never a `Task.Delay` until the lock expires** |
| — | `A_grant_with_no_username_is_a_bad_request_not_a_lockout` | `401` with error `invalid_request` (Keycloak 26.6.4's actual status for this shape, not the RFC 6749 §5.2 `400` the error name suggests — the load-bearing check is the `error` value, `invalid_request` never `invalid_grant`); the probe account's failure counter does not move |

**The trap that makes SC-3 lie, stated once so it is not rediscovered.**
`quickLoginCheckMilliSeconds: 1000` locks an account after **two** failures
inside one second, independently of `failureFactor`. A tight loop trips that
first, so `numFailures` on a correctly-configured realm will often be **below**
`failureFactor`. A test asserting `numFailures >= failureFactor` therefore
fails on exactly the realm it was written to prove — the repository has filed
that shape before. Assert `disabled`, report `numFailures`.

**Read `failureFactor` from the running server**
(`GET admin/realms/{realm}`), never hard-code `10`. The value is explicitly
open to reversal in review (`spec.md` §Assumptions 3) and no test should have
to change when it is.

**Expected red:** SC-1's `ShouldBe(HttpStatusCode.BadRequest)` reports `OK` —
today's realm issues a token after eleven wrong guesses, because
`bruteForceProtected: false` means nothing was counted. SC-3 will also fail,
with a `404` from attack-detection (no record exists). Quote SC-1's.

**Done when:** the file exists, the run is red for the stated reason, and the
verbatim output is in hand.

---

## Phase 4b — implementation (`infra-engineer`). May not edit T001's file.

### T002 [US1] Enable brute-force protection in the realm import

**File (sole owner):** `src/AppHost/Realms/smart-sentinel-eye-realm.json`

Replace line 11's `"bruteForceProtected": false,` with the eight-field block,
**in the realm header, above `"verifyEmail"`** — deliberately far from the
`clients` array so this change cannot collide textually with #2488 when that
is released:

```json
  "bruteForceProtected": true,
  "permanentLockout": false,
  "failureFactor": 10,
  "waitIncrementSeconds": 60,
  "maxFailureWaitSeconds": 900,
  "maxDeltaTimeSeconds": 43200,
  "quickLoginCheckMilliSeconds": 1000,
  "minimumQuickLoginWaitSeconds": 60,
```

Each value's justification is the table in `spec.md` §US1. Only
`failureFactor` departs from Keycloak's default, and only downward.

**Do not add `bruteForceStrategy` or `maxTemporaryLockouts`** — 25+/26-only
fields whose defaults already give the behaviour asked for, and a field this
server version does not know can be dropped at import in silence.

**Touch nothing else in this file.** In particular `directAccessGrantsEnabled`
stays `true` on both `smart-sentinel-eye-web` (`:131`) and `management-web`
(`:156`), and `passwordPolicy` (`:13`) is unchanged. Both exclusions are
argued in `spec.md` §Out of scope; changing either here is out of scope and a
review block.

**Preserve the file's BOM.** `WallDisplayAccountTests` and `RealmAudienceTests`
strip it explicitly (`TrimStart('﻿')`); an editor that rewrites the
encoding is a diff nobody asked for.

### T003 [US1] Drop the volume, re-run T001's tests, observe GREEN

1. Stop the AppHost. `docker rm -f keycloak`; remove every `*keycloak*`
   volume. **The realm edit does not take without this** — the stack comes up
   looking perfectly healthy and still running the old realm.
2. Run `BruteForceLockoutIntegrationTests`. All six facts green.
3. If SC-1 is still red: **check the volume before touching the code.** That
   is the first hypothesis, not the last.

### T004 [US1] Run the full `Integration.Tests` suite (SC-7)

The suite authenticates constantly, through `AspireFixture`'s
`grant_type=password` on `management-web`. This task is the proof that the
lockout test does not poison it and that no service account is collaterally
lockable (`spec.md` §Assumptions 1).

Run after a volume drop. A failure in an unrelated Identity test is a **signal
about this change**, not an unrelated flake — investigate before re-running.

---

## Phase 5 — verification (`/verify`)

### T005 [US1] Observe it end to end and write the note

**File (new):** `specs/207-a-guess-that-runs-out/verification.md`

1. **Drop the volume.** Third and last time it is said; it is the step that
   makes everything below mean something.
2. `aspire run`; wait for `keycloak` healthy.
3. **SC-6 — read the realm back off the server, not the file.** Mint an
   `identity-admin` token and `GET /admin/realms/smart-sentinel-eye`. Record
   the observed `bruteForceProtected` and `failureFactor` **as quoted response
   values**. This is the one check that catches a silently-dropped import
   field and a stale volume in a single request.
4. Walk `spec.md` §*Independent end-to-end test procedure* steps 4–8 by hand
   against `operator` — positive control, eleven guesses, the refusal, the
   attack-detection record, the `DELETE`, the per-account blast radius.
   Record the actual status codes and bodies.
5. **The negative control.** The same steps against `develop` (volume dropped)
   must answer `200` at step 6. Without it the procedure does not distinguish
   this change from any other reason a token might be refused.
6. **SC-8 — e2e smoke.** Run at least `e2e/management-identity.spec.ts` and
   one `wall-*` spec. Nothing under `e2e/` submits a deliberately wrong
   password to real Keycloak (verified at phase 1 — every `invalid_grant`
   there is a mocked route), so this is a guard, not an expected break.
7. Note `NFR001_JwtValidationLatencyTests`' figure as a regression guard. **Do
   not present it as a latency-budget discharge** — §IV is N/A for this
   feature and the note must say so.
8. **Write every figure down in the note.** A measurement reported only to the
   orchestrator is invisible to every later grep and reviewer.

---

## Phase 6 — QA

### T006 `code-review` **and** `security-review` — both, not either

`security-reviewer` is **mandatory here regardless of diff size**
(`plan.md` §9). The diff is eight JSON lines and one test file; its risk is
not its size. It is this repository's only account-lockout control, it changes
an authentication outcome, and the argument that both `directAccessGrantsEnabled`
flags may stay `true` is a security argument that needs an adversarial reader
rather than agreement with its author.

Point the security reviewer at, specifically:

- the ROPC deferral in `spec.md` §Out of scope — is the evidence sufficient,
  or is leaving a public-client password grant open unacceptable even
  temporarily?
- `failureFactor: 10` and `permanentLockout: false` — a fab floor that cannot
  self-recover is an availability incident; a 15-minute cap that is too
  generous is a weak control. This is the trade-off to second-guess.
- whether a lockout should reach the audit trail (`plan.md` §8 argues not, and
  that argument is the thing to test).

`infra-reviewer` covers the realm-import and Aspire half.

**Standing constraint (ADR-0144):** no finding may be answered by weakening a
gate — no deleted test, no lowered threshold, no new suppression. If a finding
says an ADR is needed, that is a **block** for a human, not something to write.

---

## Phase 7 — PR

### T007 Open the PR against `develop` and file the two follow-ups

- `gh pr create --base develop` (explicitly, per CLAUDE.md), template body.
- **Quote T001's verbatim red output** in the body. It is the only form of the
  phase-4 evidence a later reader can check.
- Closing keyword for **#2285** — and check the issue state after the merge; a
  mention alone closes roughly one time in three.
- Say plainly in the body that **#2285's second bullet is not closed by this
  PR**, and why, so nobody reads the issue closing as ROPC being disabled.
- Commit style: Conventional Commits, **no `Co-Authored-By` footer**
  (ADR-0030, ADR-0086).

**Two follow-up issues to file** (`spec.md` §Out of scope):

1. **`AspireFixture`'s ROPC dependency on `management-web`.** Cite
   `tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs:12,114-126`. Until
   the harness mints tokens another way, `directAccessGrantsEnabled` cannot be
   turned off on the operator console's client, and #2285's second bullet
   cannot be closed. Link #2285 and #2488.
2. **The password policy.** State the **correct current value** —
   `length(8) and upperCase(1) and lowerCase(1) and digits(1)`, not the
   `length(8)` #2285 records — and the blast radius of raising it
   (`README.md`, nine `specs/*/quickstart.md`, `AspireFixture.AdminPassword`,
   every `e2e/` sign-in helper). A human weighs the UX trade-off.

Both go on Project #13:
`gh project item-add 13 --owner smartsolutionslab --url <issue-url>`.

---

## Gates and flags for the orchestrator

| | |
|---|---|
| **Phase 4a colour** | **RED** — behaviour-changing. A green arrival is a failure |
| **Parallelism** | none; one Keycloak, one realm, mutated lockout state |
| **Foundational blocker** | T001 blocks T002 absolutely (ADR-0139) |
| **Operational constraint** | drop the Keycloak volume before T001, T003 and T005. Three tasks, three explicit steps, because a note gets skipped |
| **Phase 6** | `security-review` **mandatory**, not diff-shape-selected |
| **Board gate** | satisfied — #2285 already on Project #13, status Todo |
| **Escalations** (stop, do not adjust) | SC-2 red during phase 4a; a service account locked out by T004; a review finding that says this needs an ADR |
| **Latency budget** | **N/A** — no leg of §IV is touched. Phase 5 records a JWT-validation figure as a regression guard only |
