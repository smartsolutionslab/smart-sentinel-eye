# Plan — Spec 207, a guess that runs out

**Spec:** `specs/207-a-guess-that-runs-out/spec.md`
**Issue:** #2285
**ADRs:** ADR-0007, ADR-0008, ADR-0080, ADR-0103, ADR-0139, ADR-0144
**Constitution:** §VI (Aspire is the composition root), §VIII (safe by default
at trust boundaries), §Testing (new behaviour starts red)

---

## 1. Shape of the change — and why there is no bounded context here

There is no domain model in this feature, and saying so precisely matters
more than it looks.

The thing being changed is a **decision the identity provider makes**, and the
identity provider is Keycloak (ADR-0007). Keycloak already implements the
lockout counter, the wait schedule, the quick-login heuristic, the per-account
attack-detection record and its reset. None of that is ours to write, and
writing any of it would be the speculative generality ADR-0036 bans. The
repository's job is to **declare** the policy in the realm import — which is
where this repository keeps every other realm decision (scopes, clients,
groups, mappers) — and to prove at runtime that the declaration took effect.

So:

- **No bounded context is touched.** No `src/<Context>/` directory changes.
- **No layers.** No entities, no value objects, no invariants, no repository.
- **No messaging.** No domain event, no integration event in
  `Shared.Contracts`, no Wolverine handler. Nothing observes a lockout; if
  something ever should (an audit entry, an operator alert) that is a separate
  feature with a separate decision, and it is not this one.
- **No boundary rule is engaged**, so `BoundaryTests` and the NetArchTest
  suite are unaffected — but they run anyway, as always.
- **Aspire remains the composition root** (§VI): the realm reaches Keycloak
  only through `WithRealmImport("../AppHost/Realms")` at
  `src/AppHost/AppHost.cs:151`. No connection string, no config key, no
  environment variable is added.

### Files, in dependency order

| # | File | Change | Owner |
|---|---|---|---|
| F1 | `tests/Integration.Tests/Identity/BruteForceLockoutIntegrationTests.cs` | **new.** SC-1..SC-5 plus the bad-request fact | phase 4a (test-writer) |
| F2 | `src/AppHost/Realms/smart-sentinel-eye-realm.json` | 8 fields in the realm header; `bruteForceProtected` flips `false` → `true`, seven siblings added beside it | phase 4b (infra-engineer) |
| F3 | `specs/207-a-guess-that-runs-out/verification.md` | **new.** the phase-5 note | phase 5 |

F1 must exist and be observed **red** before F2 is written (ADR-0139). That
ordering is the whole of the dependency graph.

---

## 2. Phase 4a is **red**, not characterisation — and what red looks like

Constitution §Testing has two obligations and ADR-0144 says ambiguity
resolves to red. There is no ambiguity here: **this changes behaviour.** An
account that could be guessed at forever can no longer be. So:

- The test is written first, run against the **unmodified** realm, and
  **observed failing**.
- The verbatim failure output is what phase 4b receives as its brief, and it
  is quoted in the PR body. Nothing else satisfies the gate.
- The engineer may not edit F1.

**The red, concretely.** SC-1 ends with

```
response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "...")
```

and on today's realm the token endpoint answers `200 OK` with an
`access_token`, because `bruteForceProtected: false` means the eleven wrong
guesses left no trace. The Shouldly failure names `OK` where
`BadRequest` was expected. That is the quotable evidence.

**A red that would be an escalation, not a step.** If SC-2 (the positive
control) fails during the red run — i.e. the throwaway account's *correct*
password is refused before any wrong guesses — the harness is broken, not the
feature. Stop and report; do not proceed to 4b on that red.

**The volume trap makes a false red as easy as a false green.** See §5.

---

## 3. How the test gets an account it is allowed to lock — the central design
problem

Locking out an account is a **destructive, stateful act against a shared
realm**, and the integration suite is one xUnit collection sharing one
`AspireFixture` against one Keycloak. If the lockout test locks `operator` or
`admin`, every other test in the collection that mints a token afterwards
fails, in a way that looks like an unrelated regression. The lock is
temporary, but `minimumQuickLoginWaitSeconds: 60` is long enough to poison a
whole run.

Three options were considered:

| Option | Verdict |
|---|---|
| Lock an existing seeded account (`operator`) and clear it in cleanup | **Rejected.** Cleanup that must run is cleanup that will one day not run — a cancelled run, a crash, a `--filter` that runs only SC-1. The failure mode is a poisoned suite with no obvious cause. |
| Add a dedicated user to the realm import (`lockout-probe@munich.test`) | **Rejected**, narrowly. It works and does not break `WallDisplayAccountTests` (which enumerates users but asserts only about `offline_access` and fab-group membership). But it makes the realm carry a fixture, and it adds a second reason for the realm file to change in this PR. |
| **Create a throwaway user through the Admin API inside the test, and delete it** | **Chosen.** |

**Chosen approach.** The test creates its own user at `[Fact]` scope through
`identity-admin`, which already holds `manage-users` and `view-users`
(`smart-sentinel-eye-realm.json:579-587`):

1. `POST admin/realms/smart-sentinel-eye/users` — `{ username: "lockout-probe-<guid>", enabled: true }`.
   A fresh, unguessable username per run, so two concurrent runs (or a leaked
   residue from a previous one) cannot collide.
2. `PUT admin/realms/.../users/{id}/reset-password` — a password satisfying the
   realm policy (`length(8) and upperCase(1) and lowerCase(1) and digits(1)`).
   Anything shorter, or missing a class, is rejected by Keycloak and the test
   fails at setup rather than at the assertion — worth a setup-time assertion
   with a message saying so.
3. Group membership: **none needed.** The test never calls a fab-scoped
   endpoint; it only asks the token endpoint for a token and reads the
   attack-detection record. Adding the account to `/fabs/munich` would be
   speculative.
4. `DELETE admin/realms/.../users/{id}` in cleanup — **checked**, the way
   `RealmProbe`'s own delete is checked: an unchecked `DeleteAsync` that
   answers 404/409/500 leaves residue that the next run's sweep cannot
   distinguish from a real finding. That is issue #2166's shape and this file
   must not repeat it.

This makes SC-4 (the blast-radius control) genuinely cheap: the second account
is any seeded one — `admin` — and it is never at risk, because the locked
account is one no other test knows exists.

**On `RealmProbe`.** It is the right neighbour and the wrong container. It
already holds `AdminClientId`/`AdminClientSecret`/`Realm` and an
`AuthorisedAdminClientAsync`, and the new test should reach the Admin API
through *those constants* — three copies of a secret is how they drift. But
`RealmProbe`'s own methods are about clients and effective roles; user
creation, password reset and attack-detection are a different subject, and
widening a shared file that `KeycloakAdminTokenProviderTests` and
`MqttAudienceIntegrationTests` also read raises the contention surface for no
gain. **Decision: the new test file owns its own user/attack-detection
helpers, and reads `RealmProbe.Realm`, `RealmProbe.AdminClientId`,
`RealmProbe.AdminClientSecret`.** If a third test ever needs the same
helpers, that is the moment to promote them — not before.

---

## 4. The assertions, concretely — and the trap of asserting the number

Every test in F1 reads the realm's own `failureFactor` **from the running
server** (`GET admin/realms/smart-sentinel-eye`) rather than hard-coding `10`.
Two reasons, and the second is the important one:

1. If a reviewer overturns `failureFactor: 10` (spec §Assumptions 3 invites
   exactly that), the test does not need editing.
2. **A test that hard-codes 10 and asserts "the 11th attempt is refused" can
   pass while the realm says 30**, because Keycloak's *quick-login check*
   (`quickLoginCheckMilliSeconds: 1000`) locks an account after **two**
   failures inside one second regardless of `failureFactor`. A tight loop of
   wrong guesses trips that long before the failure factor. So:

   - The loop submits `failureFactor + 1` guesses — an upper bound, not a
     claim about which one did it.
   - SC-1 asserts only **that the correct password is refused afterwards**.
   - SC-3 asserts `numFailures >= failureFactor` **only if** the failure
     counter is the mechanism that fired; if the quick-login check fires
     first, `numFailures` will be lower and `disabled` will still be `true`.
     **Therefore SC-3's load-bearing assertion is `disabled == true`**, and
     `numFailures` is asserted as `>= 1` and reported in the failure message,
     not asserted at `>= failureFactor`. Writing it the other way produces a
     test that fails on a correctly-configured realm — the repository has
     filed that exact shape before ("the wrong red matches the filed number").

   The spec's SC-3 wording is corrected here rather than in `spec.md` because
   this is an implementation consequence, and `tasks.md` T004 carries the
   binding form.

**Waiting.** The wrong-guess loop is sequential and synchronous — each POST
completes before the next. Nothing here waits for a background condition, so
ADR-0150's poll-a-condition rule has nothing to bind. The one place a wait
could creep in is SC-5's "the lock clears": that must be an **explicit
`DELETE` of the attack-detection record**, never a `Task.Delay` until the lock
expires. A test that sleeps for `minimumQuickLoginWaitSeconds` is a test that
takes a minute and is flaky under CI contention.

**Cancellation.** Every helper takes `CancellationToken` as its last
parameter (ADR-0049), and no `ConfigureAwait` — matching
`RealmProbe.cs` and every other file in `tests/Integration.Tests/Identity/`.

---

## 5. The volume trap — the false green *and* the false red

`src/AppHost/AppHost.cs:150-155` says it plainly:

> Persistent means the keycloak-data volume survives AppHost restarts, so an
> edit to `Realms/smart-sentinel-eye-realm.json` is **NOT** re-imported —
> `WithRealmImport` only runs against a fresh volume.

This is an operational constraint on phases 4 and 5, not a footnote.

**Two distinct failures, and both look like success:**

- **False green at 4b.** The engineer edits the realm, runs the test on a
  stale volume, sees it… still red, edits something else, chases a ghost.
  Or worse — the volume happens to be fresh from an unrelated cause, the test
  goes green, and nobody records *why*.
- **False red at 4a.** The test-writer runs the red against a volume carrying
  a realm from some earlier branch. The red is real but it is not *this*
  red, and the quoted failure is evidence of nothing.

**Mitigation, and it is procedural because nothing in the repository enforces
it.** Every run that is meant to prove something about the realm — the 4a red,
the 4b green, and phase 5 — is preceded by:

```sh
# stop the AppHost first: a running host holds the container
docker rm -f keycloak 2>/dev/null
docker volume ls --format '{{.Name}}' | grep -i keycloak    # then remove each
```

`tasks.md` makes this a numbered step in three separate tasks rather than a
note, because a note is what got skipped the last three times this repository
wrote one down.

**The `isE2ETests` escape.** The `WithLifetime(ContainerLifetime.Persistent)`
+ `WithDataVolume()` block is guarded by `isRunMode && !isE2ETests`, so the
`AspireFixture` (which is neither run-mode nor the e2e profile) gets a
**fresh, volume-less Keycloak per fixture boot** and re-imports the realm every
time. That is why the integration test is trustworthy *once the fixture
actually restarts* — and why the trap bites hardest on the manual
`aspire run` verification in §Independent test procedure, where the persistent
volume is in play. Both paths get the drop step; the fixture path gets it as
insurance, the manual path gets it as a requirement.

**SC-6 is the check that does not depend on remembering any of this.**
Reading `bruteForceProtected` back off `GET /admin/realms/smart-sentinel-eye`
tells you what the *server* thinks, which is the only thing that matters. It
catches a stale volume and a silently-dropped import field with one request.

---

## 6. Sequencing

```
T001 (4a) ── write F1, observe RED, capture verbatim output
                │
T002 (4b) ── edit F2 (the realm header)
                │
T003 (4b) ── drop volume, re-run F1, observe GREEN
                │
T004 (4b) ── full Integration.Tests suite (SC-7)
                │
T005 (5)  ── manual procedure + SC-6 read-back + e2e smoke (SC-8)
                │
T006 (6)  ── code-review AND security-review
                │
T007 (7)  ── PR, two follow-up issues
```

Nothing here is `[P]`. This is deliberate and worth stating for the
orchestrator: ADR-0109's parallel marker requires **disjoint files**, and the
serial chain above is not a file-ownership constraint but a
**single-shared-resource** one — there is one Keycloak, one realm, and the
tests mutate its lockout state. Two agents running realm-mutating tests
against one machine's stack reproduce the repository's "one machine, one
Aspire stack" failure, where a `FailedToStart` reads exactly like a code
defect. The slice is small enough that the serialism costs nothing.

---

## 7. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| An import field is silently dropped by 26.6.4 | low | Only fields present since Keycloak 8 are written (26-only `bruteForceStrategy`/`maxTemporaryLockouts` deliberately omitted). SC-6 reads the server back. |
| The lockout test poisons the suite | **medium**, and the main one | A per-run throwaway account (§3) nothing else authenticates as; SC-4 detects the failure directly; SC-7 runs the whole suite. |
| A service account gets locked out by a failed `client_credentials` exchange | low | Brute-force detection counts user logins. **Unverified against 26.6.4** (spec §Assumptions 1). SC-7 covers it indirectly — the suite authenticates five service accounts constantly. A failure here is an escalation. |
| A stale Keycloak volume makes a run prove nothing | **high** without the step, near-zero with it | §5; three numbered task steps. |
| `NFR001_JwtValidationLatencyTests` regresses | very low | The detector runs at the token endpoint, not the resource server; watched at phase 5 as a guard, not claimed as a budget. |
| e2e sign-ins break | low | Every `invalid_grant` under `e2e/` and `apps/*/src` is a mocked route or a unit-test double — verified; nothing submits a deliberately wrong password to real Keycloak. SC-8 confirms. |
| `failureFactor: 10` is overturned in review | **expected, and cheap** | No test hard-codes it (§4). A reviewer changes one line in F2. |

---

## 8. What this plan will not do

- **Not disable `directAccessGrantsEnabled` anywhere.** `spec.md`
  §*Out of scope* carries the evidence: `management-web`'s ROPC is
  `AspireFixture.ClientId`, and `smart-sentinel-eye-web`'s is SC-1/SC-2 of
  spec 200. Neither is a config flip; both are somebody else's scope.
- **Not touch the password policy.** Deferred with its correct current value
  recorded, so the next reader does not inherit #2285's mis-transcription.
- **Not write or amend an ADR.** ADR-0144 forbids it to this lane. Nothing
  here needs one: ADR-0007 already locks Keycloak as the identity provider,
  and configuring a control Keycloak already implements is implementing that
  decision, not making a new one. **If a reviewer disagrees — if
  "what our lockout policy is" reads to them as an architectural decision
  rather than a configuration value — that is a block, and the right outcome
  is an ADR written by a human, not a guess written here.**
- **Not emit an audit event on lockout.** Constitution §Security says admin
  and config *writes* are audited; a refused login is neither, and Keycloak's
  own event log already records it. Wiring lockouts into this system's audit
  trail is a real feature with a real decision behind it, and it is not this
  one.

---

## 9. Phase 6 — both reviewers, and why that is not the default here

`tasks.md` would ordinarily let phase 6 pick a reviewer by the shape of the
diff, and an eight-line JSON change plus one test file reads as an
infrastructure change. **This feature gets `security-reviewer` regardless**,
because the diff's size is not its risk: it is the repository's only
account-lockout control, it changes an authentication outcome, and the
argument that both ROPC flags may stay `true` is a security argument that
deserves an adversarial reading rather than a reviewer's agreement with the
author. `infra-reviewer` covers the realm-import and Aspire half.
