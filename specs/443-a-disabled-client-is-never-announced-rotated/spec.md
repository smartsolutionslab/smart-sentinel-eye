# Spec 443: A disabled client is never announced as rotated

**Issue:** #3112 (feature-level, on Project #13) · **Branch:** `fix/3112-rotate-tracked-disabled`
**ADRs:** ADR-0185 (durable outbox; `WebhookClientRaceDisableFailed` and `WebhookIntegrationRotatedV1` are declared
types, rows 3-4 of its table), ADR-0040 (domain vs integration events), ADR-0049 (cancellation; the
`CancellationToken.None` post-commit work), ADR-0113 (two-layer concurrency; the `DbUpdateConcurrencyException` exit
is untouched), ADR-0144 (autonomous lane). ADR-0142/0143 read and **not engaged**: the rotate endpoint's
`Idempotency-Key` and retry policy are unchanged.
**Specs built on:** 426 (#2789, the race-disable and its outbox retry), 434 (#3080, the compensation bound and the rule
"Keycloak has disabled the client ⇒ report disabled, announce no rotation"), 442 (#3102, the durable outbox).
**Lane:** autonomous · **Phase-4a colour:** **red** for the two changed exits; characterisation for the exits that
must not move (plan §"Phase 4a").
**Security-sensitive:** **yes** — webhook credential lifecycle and the announcement that flips EventIngestion's
bearer validation. `security-reviewer` is mandatory at phase 6.
**New ADR needed:** no. The rule being enforced is already in the code (the `OperationCanceledException` exit,
`RotateWebhookClientCommandHandler.cs:467-476`); this spec extends it to the sibling exit.
**Written 2026-10-24 against `develop` at `99516703`.**

---

## Premise, checked at `99516703`

1. **Holds.** `RotateWebhookClientCommandHandler.DisableIfRevokedAsync`'s catch-all
   (`catch (Exception ex) when (disabling)`, `:478-490`) returns `false` for every exception, whatever state the
   disable reached. `DisableWebhookClientCommandHandler` runs Keycloak **then** `client.Disable(clock)` **then**
   `SaveAsync`; the aggregate it disables is the same tracked instance the rotate handler holds
   (`GetWithinFabAsync` is a tracking query on the scoped context — the `:461` OCE exit already relies on this). So a
   save failure leaves `aggregate.DisabledAt` set and the client disabled in Keycloak.
2. **Non-`DbUpdateException` (the issue's case).** Nothing detaches the entry; it stays `Modified`. `false` sends
   the caller (`:93-116`) to publish `WebhookIntegrationRotatedV1` and `CommitAsync`, which on a recovered
   connection writes the disabled row **and** the Rotated announcement in one transaction. **Nuance since #3102:**
   both publishes are now durable (ADR-0185), so on a connection that is *still* dead the retry publish at `:488`
   or the commit at `:115` throws and nothing is announced. The defect needs the failure to be transient, or not a
   connection failure at all (e.g. a domain-event handler throwing inside `SaveAsync`, `RegisteredClientRepository.cs:82-87`).
3. **`DbUpdateException` (not named in the issue, same defect).** The entry is detached, so the disabled row is not
   saved — but Keycloak has disabled the client and the caller still announces Rotated for it. The #3080 rule keys
   on Keycloak's state ("Keycloak has disabled the client … report disabled, so no rotation is announced", `:467-473`),
   not on the row. **Decision (recorded, not asked):** this exit is fixed too; one predicate, `aggregate.DisabledAt`,
   decides both. The existing tests on this exit assert only the retry publish and stay green unmodified.
4. **Characterisation in place:** `RotateWebhookClientRaceDisableOutboxCharacterisationTests` pins the retry
   publish-before-commit on all three failure exits; none asserts on `WebhookIntegrationRotatedV1`. Nothing in
   `tests/` asserts that Rotated **is** published on either exit this spec changes. The boundary the fix must keep
   — Keycloak disable failed ⇒ Rotated **is** announced — is already pinned by
   `RotateWebhookClientCommandHandlerTests.When_the_race_disable_itself_fails_the_create_still_returns_success_and_a_warning_is_logged`
   (`:766`).
5. **Spec number 443.** 440 (`fix/1995-narrow-default-roles`), 441, 442 taken; open PRs #3091, #3119 are ADR-only.
   **Collision risk:** worktree `sse-3101` (`test/3101-scope-binding`) is in flight at the same base and may also
   claim 443 — re-check before push. **Resolved:** the #3101 spec was renumbered to 444, so 443 is uncontested.

## User story (P1, the only one)

**As** a security officer, **I want** a webhook client that the rotate call's revoke-race check disabled in Keycloak
never to be announced as rotated, **so that** audit history never pairs "rotated" with a disabled client and
EventIngestion is never told to flip a revoked integration to JWT validation.

### Acceptance scenarios

```gherkin
Scenario: happy — the race disable succeeds (unchanged)
  Given an integration revoked between the rotate's pre-flight check and its create commit
  When the rotate call runs the post-commit check and the disable succeeds
  Then the call returns 200 with the credentials
  And no WebhookIntegrationRotatedV1 is published
  And no WebhookClientRaceDisableFailed is published

Scenario: conflict — the race-disable save fails after Keycloak disabled the client (non-DbUpdateException)
  Given the same race, and the disable's save throws an exception that is not a DbUpdateException
  When the rotate call completes
  Then it returns 200 with the credentials
  And no WebhookIntegrationRotatedV1 is published
  And exactly one WebhookClientRaceDisableFailed is published, before exactly one commit

Scenario: conflict — the race-disable save fails with a DbUpdateException (not a concurrency conflict)
  Given the same race, and the disable's save throws DbUpdateException
  Then it returns 200, no WebhookIntegrationRotatedV1 is published,
  And exactly one WebhookClientRaceDisableFailed is published, before exactly one commit

Scenario: conflict — the async revoke handler won the race (unchanged, ADR-0113)
  Given the disable's save throws DbUpdateConcurrencyException
  Then it returns 200, no Rotated and no retry are published

Scenario: bad request boundary — Keycloak never disabled the client (unchanged)
  Given the disable's Keycloak call fails, so the aggregate was never disabled
  Then it returns 200, one WebhookClientRaceDisableFailed is published,
  And WebhookIntegrationRotatedV1 IS published (the client is still live; EventIngestion must bind it)

Scenario: auth (unchanged, out of scope)
  Given a caller without the rotate scope or the fab
  Then the endpoint refuses before the handler runs (403/404 as today; WebhookRotationEndpointCharacterisationTests)
```

## Functional requirements

- **FR-001** When the race-disable fails **after** Keycloak disabled the client (`aggregate.DisabledAt` set), for any
  exception other than `DbUpdateConcurrencyException`, the handler publishes the retry, commits once on
  `CancellationToken.None`, and reports *disabled* so the caller publishes no `WebhookIntegrationRotatedV1` and does
  not commit again.
- **FR-002** When the race-disable fails **before** Keycloak disabled the client, behaviour is unchanged: retry
  published, Rotated published, the caller's single commit flushes both.
- **FR-003** A `DbUpdateException` still detaches its entries before anything commits (`:482-485`).
- **FR-004** The `OperationCanceledException` and `DbUpdateConcurrencyException` exits are unchanged.
- **FR-005** The HTTP response is unchanged on every exit: the create committed, so the caller gets 200 and its secret.

## Independent end-to-end test procedure

Unit level is the honest level: the defect needs a save to fail *between* Keycloak's disable and the row's commit, and
nothing in the Aspire stack can inject that deterministically. `dotnet test tests/Identity.Application.Tests` —
the two red tests fail on `develop` (Rotated present in `FakeEventBus.Published`) and pass after; the
characterisation set passes unmodified both times. Phase 5 records that, plus a review of the production
`SaveAsync` → `CommitAsync` path showing the in-branch commit runs on the same scoped context.

## Locked choices

No new types, packages, contracts, migrations, or AppHost resources. `Shared.Contracts` untouched.

## Latency budget

**N/A** — admin rotate path, not on the event→overlay path (constitution §IV).
