# Spec 451: A revoked integration is never announced rotated

**Issue:** #3125 (feature-level, on Project #13) · **Branch:** `fix/3125-rotate-recheck`
**ADRs:** ADR-0185 (durable outbox; `WebhookIntegrationRotatedV1` and `WebhookClientRaceDisableFailed` are declared
types — no change to the list), ADR-0040 (domain vs integration events), ADR-0049 (the post-commit work runs on
`CancellationToken.None` / the compensation bound), ADR-0113 (two-layer concurrency; the
`DbUpdateConcurrencyException` exits are reused, not changed), ADR-0144 (autonomous lane). ADR-0142/0143 read and
**not engaged**: the rotate endpoint's `Idempotency-Key` handling and retry policy are unchanged.
**Specs built on:** 318 (#2628, the pre-flight status check), 426 (#2789, the create-branch race-disable), 434 (#3080,
the compensation bound), 443 (#3112, "a disabled client is never announced rotated").
**Lane:** autonomous · **Phase-4a colour:** **red** (behaviour-changing). Two characterisation literals that pin the
*old* order change on purpose — see plan §"Phase 4a".
**Security-sensitive:** **yes** — webhook credential lifecycle; `WebhookIntegrationRotatedV1` is the announcement that
binds EventIngestion to JWT validation. `security-reviewer` is mandatory at phase 6.
**New ADR needed:** no. **Contract change:** none (`Shared.Contracts`, HTTP shape, migrations, AppHost untouched).
**Written 2026-10-24 against `develop` at `8479dcdb`.**

---

## Premise, checked at `8479dcdb` — holds

1. **The window is real.** For an existing client the handler reads EventIngestion's status once
   (`RefuseIfRevokedAsync`, `RotateWebhookClientCommandHandler.cs:142`), then looks up, saves, rolls the secret,
   publishes `WebhookIntegrationRotatedV1` and commits (`:229-291`, `:95-118`). The post-commit re-check is gated on
   `!existing.HasValue` (`:76`), so it never runs for a rotation. Pinned by
   `RotateWebhookClientOrderCharacterisationTests.A_rotation_of_an_existing_client_never_rechecks_the_status_after_its_save` (`:335`).
2. **What closes part of it, what does not.** EventIngestion's revoke commits `RevokedAt` and outboxes
   `WebhookIntegrationRevokedV1`; Identity disables the Keycloak client and the row **asynchronously** on receipt
   (`WebhookIntegrationRevokedIntegrationEventHandler` → `DisableWebhookClientCommandHandler`: Keycloak, then row,
   then save). If that disable commits before the rotate's lookup, `GetWithinFabAsync` filters the disabled row
   (`RegisteredClientRepository.cs:52`) and the rotate is refused 404; if it commits between lookup and save, the EF
   version token fails the rotate's save (ADR-0113). **Neither covers the common case**: the revoke committed in
   EventIngestion but Identity's async disable has not yet run when the rotate saves — the whole rotate plus the
   broker's delivery latency. The rotate then announces Rotated and the async disable lands afterwards.
3. **Demonstrated, test-shaped.** A scratch unit test (deleted; it becomes T002) seeded an existing client, queued
   status `Active, Revoked`, ran the rotate, then ran `DisableWebhookClientCommandHandler` as the async consumer
   would: **passed on `develop`** — one `WebhookIntegrationRotatedV1` published, row `DisabledAt` set, Keycloak client
   disabled, status consulted once although it already read `Revoked`. With the gate removed it went red, and only
   the two characterisation tests pinning the old order also failed (249 others green).
4. **Cost is audit/consistency, not access — confirmed.** `WebhookDeliveryAuthentication` refuses a revoked
   integration (`:93`) **before** either validation mode is consulted, and `MarkAsRotated` never touches `RevokedAt`
   (`WebhookIntegration.cs:117-133`). For an existing client the Rotated message carries the *same* client id, so
   `MarkAsRotated` is normally an idempotent no-op in EventIngestion. The visible cost is AuditObservability
   (`IntegrationEventAuditHandler`) recording "rotated" after "revoked", and the operator being handed a 200 for an
   integration already revoked.

## User story (P1, the only one)

**As** a security officer, **I want** a rotation of an existing webhook client that loses a race with a revoke never
to be announced as rotated, and the client disabled by the rotation itself, **so that** the audit trail never records
a rotation after the revoke that ended the integration.

### Acceptance scenarios

```gherkin
Scenario: happy — no revoke in flight (status order changes, outcome does not)
  Given an existing webhook client at version 3 and an integration that stays Active
  When the operator rotates it with If-Match 3
  Then the status is read twice (pre-flight, then after the secret roll)
  And the call returns 200 with the new secret and version 4
  And exactly one WebhookIntegrationRotatedV1 is published and committed

Scenario: conflict — a revoke lands after the pre-flight check
  Given an existing webhook client and a status that reads Active, then Revoked
  When the rotate saves, rolls the secret and re-checks
  Then the rotation disables the client (Keycloak, then row) itself
  And no WebhookIntegrationRotatedV1 is published
  And the call returns 200 with the rolled secret (the save and roll genuinely happened)

Scenario: conflict — the async revoke handler wins the disable
  Given the same race and the inner disable finds no enabled row, or its save throws DbUpdateConcurrencyException
  Then the client is treated as already disabled; no Rotated, no retry, no second commit

Scenario: conflict — the race disable fails before Keycloak disabled the client
  Given the same race and the inner disable's Keycloak call fails
  Then one WebhookClientRaceDisableFailed is published, Rotated IS published, one commit flushes both

Scenario: bad request boundary — the re-check cannot confirm the status
  Given an existing client and a status that reads Active, then Unverifiable
  Then the client is NOT disabled, Rotated IS published and committed (unchanged from today)
  And a warning names the unconfirmed re-check

Scenario: auth (unchanged, out of scope)
  Given a caller without the rotate scope or the fab
  Then the endpoint refuses before the handler runs (WebhookRotationEndpointCharacterisationTests)
```

## Functional requirements

- **FR-001** After a successful rotation of an **existing** client (save and secret roll both done), the handler
  re-reads the integration's status before publishing, through the same compensation path the create branch uses
  (`DisableIfRevokedSinceCommitAsync`), bounded by `CompensationBound`, not the request token.
- **FR-002** A `Revoked` re-check runs `DisableWebhookClientCommand` and suppresses `WebhookIntegrationRotatedV1`
  exactly as the create branch does, including every failure exit of spec 434/443 (detach, retry publish, the
  `aggregate.DisabledAt` predicate, single commit).
- **FR-003** **Decision (recorded, not asked):** on the rotate branch an `Unverifiable` re-check does **not** disable.
  The create branch disables on `Unverifiable` because the client it would leave live is brand new; on the rotate
  branch the client is the integration's *live* credential, there is no re-enable path anywhere in Identity, and the
  defect being fixed fails closed already (premise 4). Disabling on a transient lookup fault would trade an audit
  inconsistency for an unrecoverable outage. The create branch's `Unverifiable` behaviour is unchanged.
- **FR-004** Log lines emitted on the rotate branch must not say the client was "created"; rotate-branch race
  outcomes get their own `[LoggerMessage]` texts. Create-branch texts are unchanged (characterisation-pinned).
- **FR-005** HTTP response unchanged on every exit: 200 with the rolled secret and the aggregate's current version.

## Residual risk (accepted, mirrors spec 443)

A revoke landing after the re-check and before the commit still yields Rotated then the async disable. The window
shrinks from "the whole rotate plus broker latency" to "one publish + one commit".

Two further residuals, recorded from the security review:

- **(a) The caller's token can steer the second check to `Unverifiable`.** `CallerTokenForwardingHandler` forwards the
  caller's own bearer to EventIngestion, so a token that expires inside the rotate window makes the re-check answer
  401, which maps to `Unverifiable`. On the rotate branch that now leaves the live client enabled (FR-003), so the
  consequence is only an audit inconsistency (a rotation announced although the status was not confirmed). Closing it
  means running the post-commit check with a service credential, which changes spec 318's bounded cross-context
  exception and therefore needs an owner decision.
- **(b) The `DbUpdateConcurrencyException` exit on the rotate branch can also be caused by a concurrent rotate**, not
  only by the asynchronous revoke handler. The log text says "already disabled", which is then inaccurate.

## Independent end-to-end test procedure

Unit level is the honest level: the race needs EventIngestion's status to change between two reads inside one
request, which the Aspire stack cannot schedule deterministically. `dotnet test tests/Identity.Application.Tests`:
the new tests fail on `develop` (Rotated present in `FakeEventBus.Published`, status read once) and pass after.
Phase 5 additionally runs `RotateRevokedWebhookIntegrationIntegrationTests` and
`CrossFabWebhookRotationIntegrationTests` against the Aspire stack to show the happy path still rotates with the real
`EventIngestionWebhookIntegrationStatusLookup` answering twice.

## Locked choices

No new types outside the handler file, packages, contracts, migrations, or AppHost resources.

## Latency budget

**N/A** — admin rotate path, not on the event→overlay path (constitution §IV).
