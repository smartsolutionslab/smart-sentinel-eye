# Plan — Spec 214, the session a lockout cannot reach (#2509)

Phase 2 of ADR-0037. Reviewed against `.specify/memory/constitution.md` and the
ADRs named in `spec.md`.

---

## Shape of this change

**No bounded context is touched, and that is not an omission.** This delivery
adds two tests and one documentation correction. There is no domain model, no
aggregate, no value object, no command, no query, no handler, no migration, no
endpoint, and no message.

| Usual plan section | Here | Why |
|---|---|---|
| Bounded context + layers | **N/A** | Nothing under `src/` changes. The subject is a decision Keycloak makes at runtime. |
| Entities / value objects + invariants | **N/A** | No domain model participates. Constitution §II binds domain state; there is none. |
| Messaging (domain → integration event) | **N/A** | No event is raised, consumed, or contracted. |
| Boundary rules (no cross-context refs) | **Honoured trivially** | No project reference is added. `NetArchTest` in `tests/Architecture.Tests` is unaffected; §III holds because nothing crosses. |
| Persistence / EF / Marten | **N/A** | No schema, no `DbContext`, no migration. |
| Aspire resources (§VI) | **Unchanged** | No new runtime resource. `AppHost` is not edited. The realm import file is **not** edited — see *The one temptation to refuse*. |
| Latency budget (§IV) | **N/A** | No leg of the six. Stated rather than omitted, per the §IV lesson about clerical exemptions. |
| Coverage gates (ADR-0065) | **Unaffected** | Domain ≥ 90 / Application ≥ 80 / Shared ≥ 90 measure `src/`; this adds no `src/` lines to cover and removes none. |

What *does* apply is the test design, the evidence design, and the exit
condition — which is what the rest of this plan is.

---

## Files this delivery owns

| File | Kind | Story |
|---|---|---|
| `tests/Integration.Tests/Identity/LockoutSessionSurvivalIntegrationTests.cs` | **new** | US1 |
| `e2e/wall-survives-a-lockout.spec.ts` | **new** | US2 |
| `specs/207-a-guess-that-runs-out/spec.md` | edited, one paragraph | US1 |
| `specs/214-the-session-a-lockout-cannot-reach/verification.md` | new | US1 + US2 |

Nothing else. In particular **not**
`tests/Integration.Tests/Identity/BruteForceLockoutIntegrationTests.cs` and
**not** `e2e/wall-survives-a-process-death.spec.ts`: both are existing,
passing, load-bearing evidence for specs 207 and 107, and this spec's questions
are new cases rather than changes to theirs. Two new files instead of two edits
is also what makes US1 and US2 `[P]`-parallel under ADR-0109, and it keeps
`git diff --stat` on the existing suites at zero — the check `verification.md`
will quote.

### Why a new integration test file rather than a sixth fact in the existing one

`BruteForceLockoutIntegrationTests` pins spec 207's claim that *authentication*
stops. This spec's claim is about *refresh*, which is a different endpoint
grant, a different Keycloak code path, and a different failure to guard
against. Its own class doc already draws this line for `RealmProbe` — "the user
and attack-detection helpers stay local to this file rather than widening
`RealmProbe`" — and the same reasoning applies one level up: a file that owns
two unrelated claims is a file whose red tells you less.

The cost is duplicated helpers (`CreateProbeUserAsync`, `RequestTokenAsync`,
`DeleteProbeUserAsync`, the master-realm admin client). That duplication is
accepted deliberately. Hoisting them into `RealmProbe` widens a type that
`KeycloakAdminTokenProviderTests` and `MqttAudienceIntegrationTests` also
depend on, for the benefit of exactly two callers — the "no speculative
generality" rule (ADR-0036) cuts against the abstraction, not for it. If a
third caller appears, that is the moment to hoist.

---

## US1 — the integration test

### Reused, not reinvented

| Need | Existing thing | Where |
|---|---|---|
| Real stack, no Testcontainers | `AspireFixture`, `[Collection(AspireCollection.Name)]` | ADR-0103 |
| Keycloak base address | `aspire.CreateKeycloakClient()` — `App.GetEndpoint("keycloak")`, https, dev cert accepted | `AspireFixture.Auth.cs` |
| Admin API with `manage-users` + `view-users` | `RealmProbe.AuthorisedAdminClientAsync` and the static `RealmProbe.ReadJsonAsync` | `tests/Integration.Tests/Identity/RealmProbe.cs` |
| Realm name | `RealmProbe.Realm` | same |
| Public client for the grants | `AspireFixture.ClientId` (`management-web`) | `AspireFixture.Auth.cs` |
| Second account for the blast-radius control | `AspireFixture.AdminUsername` / `AdminPassword` | same |
| Lock technique + the `numFailures` trap | `BruteForceLockoutIntegrationTests.CreateAndLockProbeAsync` | spec 207 |

**The Keycloak endpoint must come from `CreateKeycloakClient()`, never a
literal.** Keycloak is the one resource reached by `GetEndpoint("keycloak")`
without an endpoint name (it serves https only), and a token minted against the
container's mapped port carries an issuer the services reject — a failure this
repository has already paid for once.

### Why a new helper is unavoidable

`AspireFixture` has **no** refresh-token helper and **no** way to see a full
token response: `FetchAccessTokenAsync` parses the JSON, keeps `access_token`
and `expires_in`, and discards `refresh_token`. `CachedToken` is a two-field
record. A repo-wide grep over `tests/` for `refresh_token` returns zero hits.
So this file needs its own raw poster — the same shape
`BruteForceLockoutIntegrationTests.RequestTokenAsync` already uses, and for the
same stated reason: `GetAccessTokenAsync` throws on non-success, and every fact
here has to inspect the failure.

Two locals, both private to the new file:

```
private async Task<HttpResponseMessage> PostTokenAsync(
    IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)

private static async Task<(string AccessToken, string RefreshToken)> ReadTokenPairAsync(
    HttpResponseMessage response, CancellationToken cancellationToken)
```

`ReadTokenPairAsync` fails loudly if `refresh_token` is absent rather than
returning an empty string, because an absent refresh token would make SC-1
pass vacuously — the exact failure mode SC-4 exists to rule out.

### Test isolation

Every fact creates its own `lockout-session-probe-<guid:N>` user and deletes it
in a `finally`, following spec 207's shape. No fact ever locks `operator`,
`admin`, or any `wall-*` account: a leaked lock on a shared account would poison
every other integration test in the collection, and the collection is serial.
SC-5's second account is `AspireFixture.AdminUsername`, read only, never locked.

**SC-4's disable must be reversed in the same `finally` as the delete**, and
the delete is what actually guarantees cleanliness — a user that no longer
exists cannot stay disabled. Ordering: re-enable, then delete, each
status-checked (#2166's shape), neither swallowing the other's failure.

### The rate the lock needs

Spec 207 established that `quickLoginCheckMilliSeconds: 1000` trips first, so
two rapid failures suffice and `numFailures` reads `2`, below
`failureFactor: 10`. This file follows the same rule:

- **assert `disabled == true`** on the attack-detection record — the
  load-bearing claim;
- assert `numFailures >= 1` only — diagnostic, never the gate;
- additionally assert the correct password is refused (SC-2), because the
  record and the endpoint could in principle disagree and the endpoint is what
  an attacker experiences.

Loop `failureFactor + 1` times regardless, exactly as spec 207 does, so a
future realm that raises `quickLoginCheckMilliSeconds` does not silently stop
locking.

### Secrets in assertion messages

`BruteForceLockoutIntegrationTests` deliberately never interpolates a raw token
response into a Shouldly message, because a `200` body is a live token pair and
this is a public repository. **The same rule binds here, and harder**: this
file's happy path *expects* a `200` carrying an access token and a refresh
token. Assert on status, on `error`, and on the presence and difference of the
tokens — never on their text, and never include a body in a failure message.

---

## US2 — the e2e case

`e2e/wall-survives-a-process-death.spec.ts` already builds the whole rig: a
persistent Chromium profile created with `mkdtemp` **outside `test-results/`**
(CI uploads that directory on a public repo, and the profile holds
`wall-munich`'s offline refresh token), a sign-in as `wall-munich`, an
`expireStoredAccessToken(page)` that rewrites `expires_at` in place, a network
collector that records `grantTypes` and `providerPrompts`, and `claimsOf` for
reading `azp` and `typ`.

The new spec reuses that rig with one substitution: instead of killing the
browser process, it **locks the account** between sign-in and renewal.

```
sign in as wall-munich (kiosk-wall, offline_access) → open a layout
  → lock wall-munich with rapid wrong password grants at management-web
  → confirm locked (correct password refused)
  → expireStoredAccessToken(page)                     // forces automaticSilentRenew
  → assert: still on the wall, no sign-in prompt, no NotAuthorizedScreen,
            grantTypes includes 'refresh_token', providerPrompts is empty
  → always: clear the lock via attack-detection DELETE
```

**Three things this must get right.**

1. **The teardown is not optional.** `wall-munich` is a seeded realm account,
   not a throwaway — deleting it is not available as a fallback. The lock must
   be cleared in a `finally` / fixture teardown even when the test fails, or the
   next e2e project in the same CI run signs in as `wall-munich` and fails for
   an unrelated reason that reads exactly like a regression. Clearing is one
   `DELETE .../attack-detection/brute-force/users/{id}`.
2. **The assertion must distinguish the two branches, not just "not broken".**
   Under the severe branch the page renders `NotAuthorizedScreen`. Asserting
   only "the layout is visible" would time out with a generic message; asserting
   `NotAuthorizedScreen` has count `0` *and* the layout is visible names the
   finding in the failure text, which is what a later reader gets.
3. **`localStorage` is where the grant lives** (ADR-0131), so nothing may be
   written into the profile by the test. `wall-survives-a-process-death.spec.ts`
   carries this rule in its own doc comment — if a `localStorage.setItem` ever
   appears there, the test has become the reconstruction it exists to replace.
   The same rule applies here; `expireStoredAccessToken` edits in place and is
   the sanctioned exception.

---

## The one temptation to refuse

The quickest way to make SC-6 cover the *offline* grant from the integration
suite would be to add `offline_access` to `management-web`'s
`optionalClientScopes` so an ROPC call could request it. **Do not.** That
widens a public client that already carries 24 write scopes and an open
password grant, to make a test easier — a strengthening-in-reverse of the exact
control this spec is investigating, and precisely the kind of security-control
change ADR-0144 forbids the lane. The offline branch is covered where it
actually lives: in the browser, by US2.

Equally: **the realm is not edited at all by this spec.** If the severe branch
lands, the realm edit that might follow is the human's decision, filed with
evidence.

---

## Phase-4a colour — declared here, not left to the engineer

**Characterisation, observed green** (ADR-0144's second colour), with a
mandatory counterfactual.

The reasoning, stated because this case does not sit cleanly on either side.
ADR-0144's red colour is for *behaviour-changing* work: a test that arrives
green is a phase-4 failure. Nothing here changes behaviour — no production line
moves — so there is no new behaviour to see fail first, and a manufactured red
would be theatre. The characterisation colour fits: capture what the system
does, unmodified, and make it impossible to change silently.

But characterisation has its own failure mode, and this repository has recorded
it: an assertion written to match whatever it saw cannot fail, and five such
assertions turned up in a single week. **So SC-4 is not optional and is not a
nice-to-have control — it is what converts SC-1 from a transcription into an
observation.** The engineer must show SC-4 red-in-principle by construction: it
drives the same endpoint with the same kind of token into a refusal path that
demonstrably exists (`user.isEnabled() == false`), and it must be seen failing
if SC-1's assertion is inverted. Proving a guard by counterfactual is the
standing practice here.

**And the prediction is written down first.** `spec.md` states the expected
outcome before the run. If the run contradicts it, SC-1 goes red — and that red
is the finding, not a test to fix. ADR-0144: the lane may not edit a test to
pass.

---

## Risks

| Risk | Why it is real | Mitigation |
|---|---|---|
| **A leaked lock poisons the suite** | The integration collection is serial and `wall-munich` is a seeded account US2 locks by name | Throwaway users in US1, deleted in `finally`; explicit attack-detection `DELETE` in US2's teardown; SC-5 exists partly to catch this |
| **SC-1 passes vacuously** | It asserts a success; a stack that never locked would also pass | SC-2 (correct password refused *and* `disabled == true` in the same window), SC-3 (the pair was mintable), SC-4 (the instrument can refuse) |
| **The 15-minute lock outlives the test run** | `maxFailureWaitSeconds: 900`, and CI jobs are shorter than that | Never lock a shared account without clearing it; never rely on the lock expiring |
| **A token or a body leaks into CI logs** | Public repo; SC-1's happy path holds a real token pair | Assert on status / `error` / presence only; no body in any message; the Chromium profile stays outside `test-results/` |
| **Keycloak has moved past 26.6.4** | The image is unpinned (`AppHost.cs:145`) | G1 in `spec.md`: the live answer wins over the source read, and the observed version goes into `verification.md` |
| **Phase 5 cannot run on this machine** | Three deliveries this session already deferred it; a second concurrent Aspire boot yields `FailedToStart` that reads exactly like a code defect | `tasks.md` T006 defers to CI's own job log by test name, following specs 211 and 212 |
| **A stale stack answers a manual curl** | A persistent AppHost keeps serving the binaries it booted with | Check the AppHost process start time against the commit before trusting any manual observation |

---

## Exit condition

This delivery is complete when the answer is **observed, recorded and pinned**
— not when a remedy exists.

- **Benign branch (predicted).** Tests green, `verification.md` quotes the
  live `200`, spec 207's open question is amended to point here, and the PR
  says in one line that the residual risk (new sign-ins blocked; the ten-hour
  ceiling for operators with no offline grant) is the out-of-scope mitigations'
  business, naming #2510 and #2488.
- **Severe branch.** SC-1 red. **Stop.** Do not weaken the assertion, do not
  edit the realm, do not choose a mitigation. File a follow-up carrying the
  verbatim request/response, the A3 consequence (the wall goes dark at the next
  silent renew and does not come back on its own), and the candidate remedies
  from §*Out of scope* with their costs; label it for a human decision the way
  #2510 is. Ship the tests with SC-1 inverted to assert the **observed**
  behaviour *and* say plainly in both the test's doc comment and the PR body
  that the assertion encodes a defect being tracked, not an intended guarantee.

Either way the gate is the same one #2510 set: **investigated, answered,
recorded, human decides the remedy.**
