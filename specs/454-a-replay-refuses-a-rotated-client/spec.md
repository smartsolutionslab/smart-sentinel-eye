# Spec 454: A replay refuses a client that has changed since its key was answered

**Issue:** #3096 (feature-level, on Project #13) · **Branch:** `fix/3096-replay-superseded`
**ADRs:** **ADR-0142** (idempotency keys: "the same key returns the same answer", and a secret
"revealed once per idempotency key" — the premise this spec enforces), spec 302's binding
(caller, fab, request fingerprint — kept, not widened), ADR-0143 (why a `POST` retry reaches the
endpoint at all; a 4xx is never retried), ADR-0113 / ADR-0043 (the aggregate version the marker
is), ADR-0047 / ADR-0089 (named `UPPER_SNAKE` code plus status), ADR-0067 (migrations through
`MigrationRunner`), ADR-0103 (integration tests on the Aspire fixture), ADR-0105 (`Ensure.That`),
ADR-0141 (`Option<T>`), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane).
ADR-0185 read and **not engaged**: the owner ruled *no new event*.
**Lane:** autonomous · **Phase-4a colour:** **red** (behaviour-changing), with named
characterisation tests that must stay green unmodified (§5).
**Engineer:** backend only (`backend-engineer`). **Security-sensitive: yes** — a credential
endpoint's replay path. `security-reviewer` is **mandatory** at phase 6.
**New ADR needed: no** — see §7. **Latency budget:** N/A (Identity admin path, no leg of §IV).
**Written 2026-10-25 against `develop` at `a9eaef85`.**

---

## 1. Premise, checked at `a9eaef85`

1. **Holds.** `ReplayRegisteredClientQueryHandler.HandleAsync` reads the registration row and then
   `IKeycloakAdminClient.ReadClientSecretAsync` — the **current** secret — and returns the row's
   **current** `Version`. Its own doc (*"What a replay does not promise"*) records this as
   deliberate.
2. **Holds.** The key row stores nothing that identifies *which* answer was given:
   `idempotency_key` holds `key, endpoint, caller, fab, request_fingerprint, resource_identifier,
   reserved_at, completed_at` (`IdempotencyKeyTable.Create` + `AddRequestBinding`). No version,
   no secret hash.
3. **Holds.** Keys are never swept once completed (ADR-0142 §Consequences); the reclaim sweep
   touches only unfinished reservations.
4. **"Rotated since" is detectable from the aggregate version.** `RollSecretAsync` calls
   `aggregate.Rotate` and `SaveAsync` — which **commits** (`RegisteredClientRepository.SaveAsync`
   ends in `ITransactionalCommit.CommitAsync`) — *before* rolling the secret in Keycloak, and
   `AggregateVersionInterceptor` bumps `Version` on every modified root. So every rotation is a
   committed version bump that precedes the new secret's existence. `Disable` bumps it too.
5. **Not derivable without storage.** The rotate fingerprint binds the request's `If-Match` *N*,
   but the version the answer carried is not reliably *N*+1: a create answers 0, and a create that
   is race-disabled (`disabledDuringRace`, #3112) answers 1 or 0 depending on which disabler won.
   Deriving would silently decide #3123. The answered version must be **stored**.
6. **Scope.** Three endpoints share the replay query. Only
   `POST /webhook-integrations/{name}/rotate` can rotate (`RegisteredClient.Rotate` throws for any
   other kind). Devices and kiosks are out of scope (§6).

## 2. User story

**US1 (P1) — A replay after a rotation is refused, not answered with someone else's secret.**
An operator who replays a completed `Idempotency-Key` on the rotate endpoint gets the original
answer while the client is still at the version that answer carried, and a stable
`IDEMPOTENCY_KEY_SUPERSEDED` refusal — carrying no secret — once the client has moved on.

## 3. Acceptance scenarios

```gherkin
Background:
  Given an operator with sse.webhooks.write and access to fab F
  And   a webhook integration "line-7" in fab F

Scenario: happy — a replay with nothing in between is unchanged
  Given the operator rotated "line-7" with Idempotency-Key K1 and received version V and secret S1
  When  the operator repeats the identical request with K1
  Then  the answer is 200 with version V and secret S1

Scenario: conflict — a replay after a later rotation is refused
  Given the operator rotated "line-7" with K1 and received version V and secret S1
  And   "line-7" was then rotated with K2 to version V+1 and secret S2
  When  the operator repeats the K1 request
  Then  the answer is 409 with title IDEMPOTENCY_KEY_SUPERSEDED
  And   the body contains neither S1 nor S2, and names no version
  And   Keycloak's secret was not read for this request

Scenario: conflict — the newer key still replays
  Given the K1/K2 history above
  When  the operator repeats the K2 request
  Then  the answer is 200 with version V+1 and secret S2

Scenario: conflict — a create-then-rotate supersedes the create's key
  Given the operator created "line-7" with If-None-Match: * and K0, answered version 0
  And   "line-7" was then rotated with K1
  When  the operator repeats the K0 request
  Then  the answer is 409 IDEMPOTENCY_KEY_SUPERSEDED

Scenario: conflict — a rotation that lands while the replay reads the secret
  Given a replay of K1 whose first version check passes
  And   a rotation commits before the replay's Keycloak read returns
  When  the replay re-checks the version after the read
  Then  the answer is 409 IDEMPOTENCY_KEY_SUPERSEDED and the secret it read is discarded

Scenario: bad request — a completed key with no recorded version fails closed
  Given a completed rotate key whose stored version is NULL (written before this change)
  When  the operator repeats that request
  Then  the answer is 409 IDEMPOTENCY_KEY_SUPERSEDED

Scenario: bad request — the spec 302 binding still comes first
  Given the K1 history above
  When  the operator sends K1 with a different body, fab or If-Match
  Then  the answer is 422 IDEMPOTENCY_KEY_REUSED (unchanged; superseded is never evaluated)

Scenario: auth — refusals keep their order
  Given a caller without sse.webhooks.write, or without access to fab F
  When  it sends K1
  Then  the answer is 403 as today; the key row is never read for it
```

## 4. Independent end-to-end test

On the Aspire stack (AppHost, `MigrationRunner` applied): mint an operator token with
`sse.webhooks.write` for fab F from Aspire's proxied Keycloak endpoint. (1) `POST
/webhook-integrations/t454-{guid}/rotate` with `If-None-Match: *`, `Idempotency-Key: K0`; record
version and secret. (2) Repeat with K0: identical 200. (3) Rotate with `If-Match: "0"`, K1:
200, new secret. (4) Repeat K0: **409 `IDEMPOTENCY_KEY_SUPERSEDED`**, body without either
secret. (5) Repeat K1: 200 with step 3's secret. (6) `SELECT resource_version FROM
identity.idempotency_key` shows 0 and 1 for K0 and K1.

## 5. Characterisation (must pass unmodified)

- `tests/Identity.Application.Tests/Queries/ReplayRegisteredClientQueryHandlerTests.cs` — every
  existing case (they exercise the unbound path devices and kiosks keep).
- `tests/ServiceDefaults.Tests/Idempotency/IdempotentRequestTests.cs` and
  `IdempotencyStoreCharacterisationTests.cs` — existing assertions; a test double may gain the new
  interface member and nothing else.
- `tests/Architecture.Tests/DeviceAndKioskEndpointCharacterisationTests.cs`,
  `WebhookRotationEndpointCharacterisationTests.cs`, `IdentityRouteTableCharacterisationTests.cs`
  (the route-table baseline is **not expected to move** — the 409 is already declared; if it moves,
  stop and report), `IdempotentKeyReuseDeclarationTests.cs`, `StatusProducerDeclarationTests.cs`.
- Integration: `IdempotencyKeyReuseIdentityIntegrationTests`,
  `IdempotencyRequestBindingStoreIntegrationTests`, `IdempotencyFencingIntegrationTests`, and every
  other context's idempotency integration test (they prove the six non-Identity migrations).

## 6. Scope, and what this does not decide

- **In:** the rotate endpoint's replay; the shared store records an optional answered version.
- **Out — devices and kiosks.** Their replays stay unbound (today's behaviour): they cannot be
  rotated, so the only version change they see is a disable — the territory of **#3123**.
- **#3123 is not decided.** The rotate endpoint stores the version **its own answer carried**. On
  #3123's main path (this call disabled the client it created) the answer already carries the
  post-disable version, so a replay still matches and still returns the secret — exactly as today.
  Two side effects, both refusals and both fail-closed, are named rather than hidden: (a) on the
  sub-path where the *asynchronous* revoke handler won the race, the answer carried the
  pre-disable version, so the replay is refused; (b) a disable after the original call (a revoke)
  bumps the version, so a later replay is refused. Neither case gets a test pinning it; #3123 owns
  the contract for disabled clients.
- **Out:** a TTL (owner: none), an audit event on replay (owner: no new event), storing a secret
  hash (a version is sufficient and non-sensitive), `POST /webhook-integrations` (no key, ADR-0142),
  editing ADR-0142, the constitution, CLAUDE.md or agent briefs.

## 6a. Accepted residuals

- **A secret changed outside this app is still replayed.** A secret changed in the Keycloak admin
  console does not move the version, which only sees writes made through this app, so the replay
  still hands the original caller the secret as it now stands.
- **A replay between Keycloak-disable and the row save can return a dead secret.** Pre-existing: the
  revoke path disables the client in Keycloak before saving the row, and a replay in that window
  still passes the version check.

## 7. Why no new ADR

ADR-0142's Decision is that "the same key returns the same answer" and that a secret is
"revealed once per idempotency key". Today a replay after a rotation returns a **different**
secret — the defect, not the decision. Refusing it enforces the premise inside the existing state
machine: it adds one refusal row to the ADR's table, as spec 302 added `Mismatched` and #2290 added
reclaim, neither with an ADR. The stored column is a non-sensitive integer, consistent with
§Decision.3 ("never a response body and never a secret"); keys stay durable and unswept
(§Consequences). The policy itself (refuse, no TTL, no event) is the owner's ruling of
2026-10-23/24 on #3096. A dated one-line clarification on ADR-0142 is a reasonable human
follow-up; the lane may not write it.

## 8. Locked choices

.NET 10 minimal API (ADR-0070); `Result<T, ApiError>` (ADR-0047/0089); raw-SQL `idempotency_key`
(ADR-0142); `AggregateVersion` from `Shared.Kernel` as the marker; `Option<T>` for the absent
marker (ADR-0141); xUnit + Shouldly + Moq / hand-written fakes; Aspire fixture (ADR-0103).
