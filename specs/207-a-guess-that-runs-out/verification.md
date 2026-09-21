# Verification — Spec 207, a guess that runs out (#2285)

Phase 5 (ADR-0037), executed against the Aspire AppHost this worktree
(`2285-realm-brute-force-protection` @ `c71e7e53`) had already booted in
run mode, with the Keycloak volume freshly created for this delivery
(no volume drop performed in this session — it was already correct,
post-fix, from an earlier boot in the same session; see *Setup* below
for how that was confirmed rather than assumed). Following `spec.md`
§*Independent end-to-end test procedure* / `tasks.md` T005, 2026-09-21.

## Setup

The orchestrator drove SC-6 and steps 4–8 of the procedure by hand
against this running stack before this note was written; that evidence
is reproduced verbatim below rather than redone, per `spec.md`'s own
note that a literal negative-control re-run against unpatched code in
the same worktree is not practical. This session ran the two pieces
`tasks.md` T005 still required — the SC-8 e2e smoke and the NFR001
regression figure — and wrote this note.

**One thing this session found and fixed before it could run the e2e
smoke test:** this worktree's `node_modules` did not exist (`pnpm
install` had never run here). Aspire had already tried to start
`management-web`, `kiosk-web` and `kiosk-wall` in run mode and all
three had crashed within seconds of boot —

```
[sys] Starting process...: Cmd = C:\Program Files\nodejs\npm.cmd, Args = ["run", "dev"]
> vite
'vite' is not recognized as an internal or external command,
operable program or batch file.
```

— confirmed via `mcp__aspire__list_console_logs` on `management-web`,
and via each resource's state (`Finished`, stop timestamps 4–15 s after
start) and `curl` to `:5173`/`:5174`/`:5175` all returning connection
refused. This is unrelated to the brute-force change — a missing
workspace install in a fresh worktree — but it would have made the e2e
smoke silently untestable rather than green, so it is recorded rather
than quietly worked around. Fixed with `pnpm install --frozen-lockfile`
at the worktree root, then `mcp__aspire__execute_resource_command`
`restart` on all three resources. All three then served `200` on their
ports within ~8 s.

## SC-6 — the running realm, not the file (two attempts, both quoted)

**Attempt 1 — `identity-admin` (`client_credentials`,
`dev-only-identity-admin-secret`) against
`GET /admin/realms/smart-sentinel-eye`:**

```json
{"realm":"smart-sentinel-eye","registrationEmailAsUsername":false,"bruteForceProtected":true,"supportedLocales":[]}
```

**Partial.** Confirms `bruteForceProtected: true` but not
`failureFactor`. This is a genuine correction to both `spec.md` step 3
and `tasks.md`'s SC-6, which both assumed `identity-admin` alone would
show `failureFactor` in the response. It does not, for a documented
permissions reason, not a bug: `identity-admin`'s service account
(`realm-management` client roles `manage-clients`, `manage-users`,
`view-clients`, `view-users`, `query-clients`, `query-users`,
`query-groups` — confirmed directly in
`src/AppHost/Realms/smart-sentinel-eye-realm.json:582-591`) does not
include `view-realm`, so Keycloak silently degrades the admin response
to the fields that role set can see.

**Attempt 2 — the master realm's bootstrap admin**
(`POST /realms/master/protocol/openid-connect/token`,
`grant_type=password`, `client_id=admin-cli`, `username=admin`,
`password=dev-only-keycloak-admin` — this password is Aspire's
`KeycloakPassword` parameter default, confirmed at
`src/AppHost/AppHost.cs:30`) against the same
`GET /admin/realms/smart-sentinel-eye`:

```
bruteForceProtected: true
permanentLockout: false
maxFailureWaitSeconds: 900
minimumQuickLoginWaitSeconds: 60
waitIncrementSeconds: 60
quickLoginCheckMilliSeconds: 1000
maxDeltaTimeSeconds: 43200
failureFactor: 10
```

All eight fields present and matching `spec.md` §US1's table exactly.
This session independently re-read the committed realm file
(`src/AppHost/Realms/smart-sentinel-eye-realm.json:11-18`) and confirms
the response matches the file byte-for-byte on values — the import was
not silently dropped.

## Steps 4–8 of the independent procedure, walked live against `operator`

Run via `management-web`'s password grant at
`https://localhost:16307/realms/smart-sentinel-eye/protocol/openid-connect/token`
(Aspire's proxied Keycloak endpoint, not the container's mapped port —
the issuer the services validate against).

- **Step 4, positive control.** Correct password (`Operator1234`)
  before any wrong guesses → `200`, valid `access_token`.
- **Step 5, eleven wrong guesses**
  (`password=WrongPassword1` … `WrongPassword11`) → `400 invalid_grant`,
  all eleven.
- **Step 6, the observation — repeat the correct password.**

  ```json
  {"error":"invalid_grant","error_description":"Invalid user credentials"}
  ```

  Refused, `400`, no `access_token`. **This is the feature.** The
  negative-control baseline for this step is T001's own earlier red-test
  evidence: SC-1 observed `200 OK` with a full token after eleven wrong
  guesses, against a volume-dropped, unpatched realm (phase 4a, quoted
  in the PR). A literal re-run of steps 1–6 against `develop` in this
  same worktree was not performed, per `spec.md`'s own note that doing
  so is not practical once the branch has moved on; T001's captured red
  output stands in for it.
- **Step 7, diagnose and recover.**
  `GET /admin/realms/smart-sentinel-eye/attack-detection/brute-force/users/231fa228-ee38-47e4-8aca-9fa4fc01174e`
  (operator's user id, via the master admin token):

  ```json
  {"failedLoginNotBefore":1789999447,"numFailures":2,"numTemporaryLockouts":0,"disabled":true,"numSecondaryAuthFailures":0,"lastIPFailure":"172.18.0.1","lastFailure":1789999387473}
  ```

  **`disabled: true` — the load-bearing assertion, confirmed.**
  `numFailures: 2`, **not** `>= 10` as `spec.md`'s own step 7 literally
  states. This is exactly the trap `tasks.md`'s T001 section warns about
  at length — `quickLoginCheckMilliSeconds: 1000` locks the account
  after two failures inside one second, independent of `failureFactor`
  — but `spec.md`'s own end-to-end procedure text asserts
  `numFailures >= 10` regardless. **This is a real, found-live
  inconsistency inside `spec.md` itself, recorded here as a correction
  rather than papered over**: read `disabled`, not `numFailures`, as the
  proof of lockout; `numFailures` is diagnostic, not the gate.

  Then `DELETE` the same attack-detection path → `204`, followed by the
  correct password again → `200`, valid token. Recovery confirmed.
- **Step 8, blast radius.** Re-locked `operator` (three more wrong
  guesses, `400` each), confirmed `operator` refused (`400`) while still
  locked, then authenticated `admin`/`Admin1234` via the same
  `management-web` grant → `200`, valid token. The lock is per-account;
  `admin` unaffected. `operator`'s lockout was then cleared again
  (`DELETE` the attack-detection record → `204`, followed by a
  successful `200` login), leaving the stack in a clean, unlocked state.

**Re-verified in this session, before starting the e2e smoke test**, in
case anything else had touched `operator` since:

```
GET .../attack-detection/brute-force/users/231fa228-ee38-47e4-8aca-9fa4fc01174e
→ {"failedLoginNotBefore":0,"numFailures":0,"numTemporaryLockouts":0,"disabled":false,"numSecondaryAuthFailures":0,"lastIPFailure":"n/a","lastFailure":0}
```

`disabled: false`, `numFailures: 0` — still clean.

## SC-8 — e2e smoke test

Ran after the three dev-server resources were restarted (see *Setup*).
`e2e/management-identity.spec.ts` (`chromium` project) plus the `cleanup`
teardown project, and `e2e/wall-authority.spec.ts` (`seed` + `wall` +
`cleanup` projects).

**First attempt** (immediately after the three-way restart):

```
ok  1 [chromium] management-identity.spec.ts — the console carries management-web scopes, not the legacy bundle (5.4s)
ok  2 [cleanup]  retire-e2e-cameras.teardown.ts (9.9s)
ok  3 [cleanup]  archive-e2e-layouts.teardown.ts (11.2s)
x   4 [cleanup]  archive-e2e-overlays.teardown.ts — FAILED
    Error: expect(locator getByRole('heading', {name:'Cameras'})).toBeVisible() timed out (15000ms)
    at signInAsOperator (e2e/support/sign-in.ts:20)
3 passed, 1 failed (32.2s)
```

Before treating this as a regression, confirmed `operator` was not
locked out (see the re-check above — `disabled: false, numFailures: 0`
at the time), ruling out this spec's own change as the cause. Consistent
with this repository's standing lesson that the first run after machine
churn (here: three dev servers restarted seconds earlier, Vite
on-demand-compiling routes not yet warm) looks exactly like a
regression — re-ran the identical command:

**Second attempt (warm):**

```
ok 1 [chromium] management-identity.spec.ts — the console carries management-web scopes, not the legacy bundle (3.6s)
ok 3 [cleanup]  retire-e2e-cameras.teardown.ts (7.8s)
ok 4 [cleanup]  archive-e2e-layouts.teardown.ts (8.1s)
ok 2 [cleanup]  archive-e2e-overlays.teardown.ts (8.4s)
4 passed (15.4s)
```

Clean, all four green.

**`wall-authority.spec.ts`, one run, all green:**

```
ok  2 [seed]    seed-published-layout.setup.ts (9.2s)
ok  3 [seed]    seed-bound-overlay-wall.setup.ts (17.9s)
ok  1 [seed]    seed-live-video-wall.setup.ts (19.9s)
ok  4 [wall]    A wall display may only show a wall (spec 052 US3) › receives no write authority at all (3.8s)
ok  5 [wall]    ... is refused every write it can attempt, whatever it was granted (5.2s)
ok  6 [wall]    ... can still read its own fab (2.8s)
ok  7 [wall]    ... cannot read another fab (2.7s)
ok  8 [cleanup] archive-e2e-overlays.teardown.ts (13.8s)
ok  9 [cleanup] archive-e2e-layouts.teardown.ts (14.0s)
ok 10 [cleanup] retire-e2e-cameras.teardown.ts (14.7s)
10 passed (55.7s)
```

**SC-8 satisfied.** Nothing under `e2e/` submits a deliberately wrong
password to real Keycloak (confirmed at phase 1 — every `invalid_grant`
there is a mocked route), so both runs are a regression guard against
the realm change, and both pass.

## NFR001 — `Per_request_JWT_validation_median_stays_under_the_500us_budget`

**Regression guard only — constitution §IV is N/A for this feature.**
No leg of the event-to-overlay path is touched; this feature changes
realm configuration, and brute-force detection runs at the token
endpoint, not at the resource server that validates JWTs. The figure
below is recorded to show nothing regressed, not as a latency-budget
discharge.

Run via `dotnet test
tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj
--filter "FullyQualifiedName~NFR001_JwtValidationLatencyTests"
-c Release`. Per ADR-0103, this boots its own dedicated, independent
Aspire test stack (not the manually-verified run-mode stack above); the
run took ~3 minutes end to end, most of it stack boot.

```
JWT validation over 1000 calls: p50 = 137.4 µs, p99 = 3882.4 µs, max = 9644.8 µs
Test Run Successful. Total tests: 1. Passed: 1.
```

p50 (137.4 µs) is comfortably inside the 500 µs hot-path budget the test
gates on. p99 (3882.4 µs) is well inside the 50,000 µs catastrophe
ceiling; the p50→p99 spread is the OS-scheduler jitter the test's own
class remarks document as expected on a shared/dev runner, not something
this change could plausibly cause (no code in the JWT-validation path —
`ConfigurationManager<OpenIdConnectConfiguration>`, `JwtSecurityTokenHandler`
— is touched by this spec; the change is realm configuration read by the
*token issuance* path, not the *token validation* path this test
exercises). No regression.

## Latency-budget statement (constitution §IV)

**N/A.** Restated from `spec.md`: no leg of
`camera → SFU → decode → presentation buffer → event → overlay →
composite` is touched. This feature adds no code and no runtime
resource; it flips eight fields in the Keycloak realm import, and the
only endpoint whose behaviour changes is the token endpoint
(`POST .../protocol/openid-connect/token`), which is not on that path.

## What this note does not re-derive

- **SC-1 through SC-5, and the bad-request / auth-scope facts** — proved
  by `BruteForceLockoutIntegrationTests` (T001/T003), not re-walked here
  by hand beyond steps 4–8 above, which are the independent,
  test-suite-free procedure `spec.md` asks for.
- **SC-7 (full `Integration.Tests` suite green)** — T004's responsibility,
  not repeated in this note as a fresh run here. Historical evidence: a full
  `Integration.Tests` run (604 tests) at commit `c71e7e53` (2026-09-21)
  passed 604/604. That commit predates this PR's phase-6 fix round (the
  `ReadFailureFactorAsync` master-realm-admin fix and the should-fix items
  from `infra-reviewer`/`security-reviewer`), so it does not cover the code
  as it now stands — cited here as history, not as current proof. **This
  PR's own CI `integration` job is the authoritative, up-to-date record**;
  do not read SC-7 as re-verified until that job is observed green.
- **The negative control against unpatched `develop`** — not run
  literally in this worktree (see *Steps 4–8* above); T001's captured
  red output is cited in its place, per `spec.md`'s own acknowledgement
  that the literal re-run is impractical this far into delivery.

## Phase-6 fix round — `ReadFailureFactorAsync` blocker, re-verified

`infra-reviewer` found `BruteForceLockoutIntegrationTests`'
`ReadFailureFactorAsync` read `failureFactor` through `identity-admin`
(`RealmProbe.AuthorisedAdminClientAsync`), whose realm-management roles are
`manage-users`/`view-users` only — no `view-realm`. A `GET
admin/realms/{realm}` through that account silently returns a *partial*
representation that omits `failureFactor` entirely, and the method's
fallback (Keycloak's own built-in default, `30`) silently substituted the
wrong number. Every run before this fix locked the probe account with
`failureFactor + 1` = **31** wrong guesses, not the intended 11, and SC-1 /
SC-3 stayed green only because `quickLoginCheckMilliSeconds: 1000` trips the
lockout after two rapid failures regardless of `failureFactor`.

**Fix:** read `failureFactor` through a new `MasterRealmAdminClientAsync`
helper local to the test file — the master realm's bootstrap `admin` /
`admin-cli` account (`grant_type=password`, `client_id=admin-cli`,
`username=admin`, password = the `KeycloakPassword` Aspire parameter, which
is `testkeycloak` under the `AspireFixture` per `AspireFixture.cs:279` — the
same value every other AppHost-boot test in this project already passes).
`identity-admin`'s realm-management roles were **not** widened — that would
be a real permission escalation on a service account the Identity API uses
in production, for a test-only need.

**Re-verified, temporarily, with a diagnostic throw** inserted after
`ReadFailureFactorAsync`'s call site in `CreateAndLockProbeAsync` (reverted
immediately after, never committed):

```
System.InvalidOperationException : TEMP-DIAGNOSTIC failureFactor=10
```

Confirms the fix genuinely observes the realm's real value (**10**), not the
silently-substituted default (30).

**Full class re-run, clean, after reverting the diagnostic** (Keycloak
container + `*keycloak*` volumes dropped first;
`dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj
-c Release --filter "FullyQualifiedName~BruteForceLockoutIntegrationTests"`,
2026-09-21):

```
Test Run Successful.
Total tests: 6
     Passed: 6
 Total time: 2,5554 Minutes
```

All six facts (SC-1 through SC-5, and the bad-request fact) green, now
genuinely exercising `failureFactor: 10` and eleven wrong guesses, not
thirty-one. This re-run also carries the phase-6 should-fix items applied to
the same file: SC-5 now asserts the account is still locked immediately
before the `DELETE` (so the `DELETE` is load-bearing); the no-username fact
now compares the attack-detection failure count before and after the
malformed grant, through the same status-checking helper the rest of the
file uses, instead of a vacuous post-hoc check gated on
`IsSuccessStatusCode`; SC-1's failure message no longer interpolates the raw
response body (which would carry a live access/refresh token pair if this
fact ever regresses to green on a public repository); and
`CreateProbeUserAsync` deletes the partially-created probe user if the
password-reset step throws, instead of leaking it.

## Stack

The Aspire stack (this worktree's AppHost, PID confirmed via
`mcp__aspire__list_apphosts`) was stopped cleanly after this note was
written and before the commit below.
