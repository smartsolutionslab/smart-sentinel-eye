# Feature Specification: The race disable that must not give up

**Feature Branch**: `fix/2789-race-disable-retry`

**Created**: 2026-10-21

**Status**: Draft — phases 1-3, autonomous lane (ADR-0144).

**Issue**: #2789 (*a revoke racing a webhook rotation's create branch can leave a live client if
the race-path disable itself fails*)

**ADRs**: ADR-0160 (a revoked client is refused at request time, from a local snapshot of
`DisabledAt`), ADR-0088 (Wolverine defaults: per-module queues, Postgres outbox), ADR-0042 /
ADR-0057 (Wolverine sits behind the handler interfaces and supplies outbox and retries), ADR-0040
(domain vs integration events), ADR-0043 / ADR-0113 (two-layer optimistic concurrency), ADR-0142 /
ADR-0143 (retry safety, `Idempotency-Key`), ADR-0139 / constitution §Testing (new behaviour starts
red), ADR-0144 (the lane; it may not write an ADR).

**Predecessors**: spec 264 (the async revoke disable), spec 318 (#2628, the create-branch TOCTOU
re-check this spec completes), spec 328 (#2629, `WebhookIntegrationRevokedFailurePolicy`, the
1-2-5x12 ladder), spec 337 (#2797, the sweep — option C, deferred).

> Constructs are named, not line-numbered, except where a line pins a premise on `5d148e64`.

---

## 1. The binding decision

Owner, 2026-10-21, comment on #2789:

> **Option B.** When the race-path disable fails, publish through the outbox (IEventBus,
> ITransactionalCommit) so the existing durable retry ladder (WebhookIntegrationRevokedFailurePolicy)
> re-attempts the disable; reuse `WebhookIntegrationRevokedV1` if its metadata allows, else a
> purpose-named Identity-internal message. Must not publish on the DbContext that just failed on
> concurrency. Phase 4a red: a fake Keycloak that fails the first disable; assert the outbox
> message. Option C (OrphanedClientSweep extension) is deferred.

This spec takes the **second** branch of that sentence — a purpose-named, Identity-internal
message — for the reasons in §3. It needs no `Shared.Contracts` change, no ADR and no change to
the ladder's semantics (§3c).

---

## 2. Premise, re-verified against `develop` (`5d148e64`)

| # | Claim in the issue | Verified | Where |
|---|---|---|---|
| P1 | The post-commit re-check disables the just-created client through `DisableWebhookClientCommand`'s own handler | **Yes** | `RotateWebhookClientCommandHandler.DisableIfRevokedSinceCommitAsync` (`:390-432`), `disableWebhookClient.HandleAsync` at `:406` |
| P2 | A failure of that disable is logged and never retried | **Yes.** Two failure exits both return `false`: a thrown non-cancellation exception (`:425-429`) and a non-`NotFound` `Result` failure in `AcknowledgeRaceDisable` (`:455-459`). Both log `CouldNotDisableClientCreatedDuringRevokeRace` (`Log.cs:94-95`, Warning, "manual intervention is needed"). Nothing else is published or scheduled. | as cited |
| P3 | After a failed race disable the caller still announces the rotation | **Yes.** `disabledDuringRace == false` → `WebhookIntegrationRotatedV1` published (`:86-90`) and `commit.CommitAsync` (`:100`). Pinned deliberately by spec 318's test `When_the_race_disable_itself_fails_the_create_still_returns_success_and_a_warning_is_logged` (`RotateWebhookClientCommandHandlerTests.cs:727-729`: "the client is still enabled, so the rotation must still be announced"). |
| P4 | The concurrency exit must not commit | **Yes.** `DbUpdateConcurrencyException` → entries detached, returns `true` (`:409-424`), so the caller skips publish and commit (`:72-82`). |
| P5 | `DisableWebhookClientCommand` is idempotent and NotFound-tolerant | **Idempotent: yes.** Keycloak first, then `client.Disable` + `SaveAsync` (`DisableWebhookClientCommandHandler.cs:34-45`). A second run finds nothing, because `GetWithinFabAsync` filters `DisabledAt == null` (`RegisteredClientRepository.cs:49-53`), and returns `WebhookClientNotFound`. **NotFound-tolerant: in its callers**, not in itself — both `WebhookIntegrationRevokedIntegrationEventHandler` (`:54-58`) and `AcknowledgeRaceDisable` (`:449-453`) read `NotFound` as "already disabled". |
| P6 | Redelivering the same `WebhookIntegrationRevokedV1` for an already-handled revoke does the right thing | **Yes** — by P5: NotFound → `NoWebhookClientToDisable` (Information), returns, no retry. |
| P7 | The ladder is keyed on the message type | **Yes.** `WebhookIntegrationRevokedFailurePolicy.Apply` selects `chain.MessageType == typeof(WebhookIntegrationRevokedV1)` and attaches `OnException<WebhookClientDisableFailedException>().ScheduleRetry(1m, 2m, 5m x12)`. Any other message type gets **no** rule. The durable inbox the reschedule needs is likewise per type: `durableInboxMessageTypes: [typeof(WebhookIntegrationRevokedV1)]` (`IdentityInfrastructureModule.cs:69`, applied in `WolverineDefaults` `:163-165`). |
| P8 | The outbox plumbing is already in the handler | **Yes.** `IEventBus` + `ITransactionalCommit` are constructor dependencies; outside a Wolverine handler `OutboxEventBus` captures into `IDbContextOutbox<IdentityDbContext>` and `OutboxTransactionalCommit.CommitAsync` is `SaveChangesAndFlushMessagesAsync` on the **same** scoped `IdentityDbContext` the inner disable saved through. |
| P9 | A replayed rotate (same `Idempotency-Key`) cannot double-publish | **Yes.** `IdempotentRequest.ExecuteAsync` replays a completed key from the stored resource identifier without calling the handler (`IdempotentRequest.cs:93-97`). After a stale-reservation reclaim the work re-runs, but the integration is revoked by then, so `RefuseIfRevokedAsync` refuses before anything is minted or published. Even a double publish would be harmless by P5. |
| P10 | `WebhookIntegrationRevokedV1` is EventIngestion's event | **Yes.** Raised by `WebhookIntegrationRevokedDomainEventHandler` in EventIngestion; consumed by Identity and by AuditObservability (`IntegrationEventAuditHandler.cs:43`, `V1ResourceMap.Conventions.cs:69`). |

### 2a. A gap the issue does not name

The generic `catch (Exception)` at `:425` also receives a **non-concurrency** `DbUpdateException`
from the inner handler's `SaveAsync` — which runs **after** `keycloak.DisableClientAsync`
succeeded. In that case the Keycloak client is off but the row's `DisabledAt` is not set, and
ADR-0160 refuses tokens from the `DisabledAt` snapshot, not from Keycloak. So tokens already
minted stay accepted. Today the caller then calls `CommitAsync` on the same `DbContext`, which
still tracks the failed entity as `Modified` and replays the same failing `UPDATE`. That is the
"DbContext that just failed" the owner warned about, reached through a different exception type.
This spec covers it (FR-004) because the new publish would otherwise ride on that context.

### 2b. Is announcing "rotated" for a revoked integration correct?

Spec 318 decided it on purpose (P3), and it is harmless. `WebhookIntegration.MarkAsRotated` does
not un-revoke, and `EventsEndpoints.Writes.cs` refuses deliveries to a revoked integration in
either validation mode. The audit trail gains a rotation whose client is disabled a little later:
the same pairing spec 318 avoided for the *same call*, now spread across two moments. Changing it
is a separate behaviour change with its own test to move. **Out of scope; unchanged** (§7).

---

## 3. Which message — and why not `WebhookIntegrationRevokedV1`

### 3a. Reuse is rejected

"If its metadata allows" — the **shape** would allow it: Identity's handler reads only
`IntegrationName` and `Metadata.Fab`. The **meaning** does not.

1. **It forges another context's fact.** `WebhookIntegrationRevokedV1` is EventIngestion's
   announcement that an operator revoked an integration (P10). If Identity published it, Identity
   would be asserting a domain fact it does not own. An integration event names its producer, and
   ADR-0040 keeps those events as the owning context's vocabulary.
2. **It reaches every subscriber, not just Identity.** `IEventBus.PublishAsync` uses conventional
   routing to the type's exchange, and AuditObservability's queue is bound to it. The result
   would be a **second "revoked" audit row** for one revocation, under a new
   `EventMetadata.EventIdentifier` ("the audit idempotency key", `EventMetadata.cs`) that Identity
   would have to invent, and with an actor Identity does not know. `IEventBus` has no
   point-to-point send to narrow this.
3. **It hides a different cause.** A replayed revocation is indistinguishable from a real one in
   logs and dead letters. An operator reading a dead-lettered `WebhookIntegrationRevokedV1` would
   look in EventIngestion for a revoke that happened once, at a different time.

### 3b. The purpose-named Identity-internal message

`WebhookClientRaceDisableFailed(string IntegrationName, string Fab)`. It is declared in
`Identity.Application`, **not** `Shared.Contracts`, and is **not** an `IIntegrationEvent`. Only
Identity can reference it, because NetArchTest forbids cross-context references, so it can never
become a cross-context contract by accident. Consequences:

- `BoundaryTests.V1ResourceMap_covers_every_IIntegrationEvent`,
  `Every_integration_event_has_an_audit_handler` and `AuditPayloadRedactionCoverageTests` scan
  `Shared.Contracts` only, so they are untouched. No `V<N>` suffix applies (ADR-0073 governs
  integration events).
- It carries the same two inputs the revocation handler reads, so the client id is derived the
  same way (`webhook-{IntegrationName}`) and the fab is parsed the same way.
- Its primitives are a wire shape (Wolverine JSON), the same reasoning that exempts
  `Shared.Contracts`. Constitution §II and `PrimitiveBoundaryTests` bind `*.Domain` only.

### 3c. Why this needs no ADR and no change of retry semantics

- **Mechanism**: everything comes from decisions already taken. ADR-0042 has Wolverine supply the
  outbox and retries, ADR-0088 gives per-module queues, the Postgres outbox and the
  `identity.<FullName>` queue naming, and spec 328 supplies the durable inbox and the ladder.
  Nothing new is configured in `ServiceDefaults`.
- **Ladder**: the **same** ladder (1 min, 2 min, 5 min x12, dead letter on attempt 15) and the
  **same** exception type (`WebhookClientDisableFailedException`) are attached to one more chain,
  in the same context, for the same operation (disable a revoked webhook client). Spec 328 plan §1
  named what *would* trigger an ADR-0088 "failure policies" amendment: "a **second context**
  adopting a retry ladder, or a repo-wide default". Neither happens here.
- **Precedent, recorded rather than decided**: this is the first context-internal message on the
  Wolverine bus. The owner's decision sanctions it explicitly. If a second one appears, ADR-0088
  should then gain a note on context-internal messages. Recorded in plan §1, not written (ADR-0144).

---

## 4. User stories

### US1 (P1) — A race-created client whose disable failed is disabled once Keycloak is back

**As** a security operator, **when** a revoke lands while a webhook rotation is creating that
integration's client, and the race-path disable then fails, **I need** the disable to be
re-attempted durably until it succeeds or the existing ladder dead-letters it. A revoked
integration must not keep a live, unrevoked credential because Keycloak was down for one
millisecond.

**Why P1**: this is the whole issue. Today, if the race-path disable fails, the client can mint
tokens for up to an hour (the realm token lifetime). Its row is never marked disabled, so ADR-0160
accepts those tokens until someone acts on a Warning log.

**Independent test**: Identity.Application.Tests only (§6). No Docker.

#### Acceptance scenarios

```gherkin
Scenario: AS-1 (happy) a failed race disable is handed to the outbox
  Given an integration "qa" in fab "munich" whose status is Active at the pre-flight check
    And the integration is revoked before the post-commit re-check runs
    And Keycloak fails the first DisableClientAsync call
  When the operator rotates "qa" with a create intent
  Then the response is success with the new client secret (unchanged from spec 318)
    And exactly one WebhookClientRaceDisableFailed("qa", "munich") is captured on the event bus
    And it is captured before the single CommitAsync that flushes it
    And WebhookIntegrationRotatedV1 is still announced (unchanged, §2b)
    And CouldNotDisableClientCreatedDuringRevokeRace is logged once, saying a retry is scheduled

Scenario: AS-2 (happy) the retry disables the client
  Given a WebhookClientRaceDisableFailed("qa", "munich") is delivered
    And an enabled webhook client "webhook-qa" exists in fab "munich"
  When Identity handles it
  Then DisableWebhookClientCommand("webhook-qa", "munich") is sent
    And no exception is thrown

Scenario: AS-3 (happy) Keycloak is still down — the ladder takes over
  Given a WebhookClientRaceDisableFailed is delivered
    And DisableWebhookClientCommand fails with KEYCLOAK_UNAVAILABLE
  When Identity handles it
  Then WebhookClientDisableFailedException is thrown
    And the message's chain retries it after 1 min, 2 min, then 5 min x12, and dead-letters attempt 15

Scenario: AS-4 (conflict) the async revoke handler got there first
  Given a WebhookClientRaceDisableFailed is delivered
    And DisableWebhookClientCommand answers WEBHOOK_CLIENT_NOT_FOUND (row already disabled)
  When Identity handles it
  Then it returns without throwing, logs that there was nothing left to disable, and is not retried

Scenario: AS-5 (conflict) the race disable lost Layer 2 on its save
  Given the race-path disable's SaveAsync throws DbUpdateConcurrencyException
  When the operator rotates with a create intent
  Then no WebhookClientRaceDisableFailed is published and CommitAsync is not called (unchanged, P4)

Scenario: AS-6 (conflict) the race disable failed on a non-concurrency save error
  Given Keycloak disabled the client but the race-path disable's SaveAsync throws DbUpdateException
  When the operator rotates with a create intent
  Then the failed entries are detached before anything else is committed on that DbContext
    And exactly one WebhookClientRaceDisableFailed("qa", "munich") is captured

Scenario: AS-7 (bad request) an unusable message is dropped, not retried
  Given a WebhookClientRaceDisableFailed whose Fab is null, blank or unparsable,
     or whose IntegrationName cannot form a ClientId
  When Identity handles it
  Then no DisableWebhookClientCommand is sent, a Warning names the integration, and nothing is thrown

Scenario: AS-8 (auth / replay) a replayed rotate does not publish again
  Given a rotate with Idempotency-Key K completed via AS-1
  When the same caller replays it with key K and the same body
  Then the stored answer is replayed and the handler does not run (P9, unchanged)
```

AS-8 is a property of the existing idempotency layer and the endpoint's authorization
(`sse.webhooks.write` + fab), which this spec does not touch. No new endpoint, scope or claim.

### Edge cases (decided)

- **`Unverifiable` after commit.** Spec 318 already disables on `Unverifiable` (fail-closed), so a
  failed disable on that path is retried too. The retry does **not** re-ask EventIngestion: it
  makes the decision spec 318 already took durable, and does not take a new one.
- **Outbox commit fails** (Postgres down). This propagates, as `CommitAsync` failures already do
  on every rotation path today. No new catch is added (no drive-by error handling); the
  idempotency layer releases the key (`IdempotentRequest.cs:192-210`). Option C remains the
  backstop.
- **Two copies delivered.** If Wolverine routes the message both locally and over RabbitMQ
  (`ConventionalLocalRoutingIsAdditive`), the second run gets NotFound (AS-4), or loses Layer 2 and
  is retried into NotFound. Both are benign by P5. Plan §4 makes phase 4 check the actual routes
  against the pinned Wolverine.

---

## 5. Requirements

- **FR-001** On either failure exit of the race-path disable — a thrown exception other than
  `OperationCanceledException` and `DbUpdateConcurrencyException`, or a non-`WebhookClientNotFound`
  `Result` failure — the rotate handler publishes exactly one
  `WebhookClientRaceDisableFailed(integrationName, fab.Value)` through `IEventBus`, before the
  `CommitAsync` that already follows on that path.
- **FR-002** No message is published on the success, `WebhookClientNotFound` or
  `DbUpdateConcurrencyException` exits, and those exits keep skipping `CommitAsync` (P4 unchanged).
- **FR-003** `WebhookIntegrationRotatedV1` publication and the 200 response are unchanged on every
  path.
- **FR-004** On the thrown-exception exit, if the exception is a `DbUpdateException`, its
  `Entries` are detached before publishing — the same treatment the concurrency exit gives them —
  so the commit cannot replay the failed `UPDATE`.
- **FR-005** An Identity Wolverine handler consumes `WebhookClientRaceDisableFailed` with the
  revocation handler's exact semantics: unusable fab or name → Warning and drop; `NotFound` →
  Information and return; any other failure → throw `WebhookClientDisableFailedException`. There
  is **one** implementation of that disable-or-throw step, shared by both handlers.
- **FR-006** `WebhookIntegrationRevokedFailurePolicy` attaches its unchanged rule and ladder to the
  `WebhookClientRaceDisableFailed` chain as well. No other chain gains a rule.
- **FR-007** `WebhookClientRaceDisableFailed` is added to Identity's `durableInboxMessageTypes`, so
  the reschedule is durable (spec 328's reasoning, verbatim).
- **FR-008** `CouldNotDisableClientCreatedDuringRevokeRace` keeps its name and level. Its text
  changes from "manual intervention is needed" to "a durable retry has been scheduled".
- **FR-009** No change to `Shared.Contracts`, `ServiceDefaults`, the ladder, the realm or any
  endpoint.

## 6. Independent end-to-end test procedure

**Unit (no Docker; the red evidence):**
1. `dotnet test tests/Identity.Application.Tests --filter "FullyQualifiedName~RotateWebhookClientCommandHandlerTests|FullyQualifiedName~WebhookClientRaceDisableFailedHandlerTests"`
2. `dotnet test tests/Identity.Infrastructure.Tests --filter "FullyQualifiedName~WebhookIntegrationRevokedFailurePolicyTests"`

AS-1 uses the existing `FakeKeycloakAdminClient.FailNextDisableWith`, `FakeEventBus` and
`FakeWebhookIntegrationStatusLookup.EnqueueStatuses(Active, Revoked)`. The test case is spec 318's
own failure test, with the new message asserted.

**Live (Docker; CI and phase 5 only):** `RotateRevokeRaceIntegrationTests` and
`WebhookRevocationDisablesClientIntegrationTests` stay green (regression). On a booted stack,
RabbitMQ shows queue `identity.SmartSentinelEye.Identity.Application.EventHandlers.WebhookClientRaceDisableFailed`,
and Identity's Wolverine node lists it as a durable listener. The stack has no fault injection
that can fail one `DisableClientAsync` inside a millisecond window, so the live retry path itself
is **not** observed end to end. Say so in the verification note (§8).

## 7. Out of scope

- Option C (extend `OrphanedClientSweep`, spec 337) — deferred by the owner.
- Whether "rotated" should be announced for a revoked integration (§2b).
- Re-checking integration status inside the retry (edge case 1).
- Any change to `WebhookIntegrationRevokedV1`, the ladder or the dead-letter behaviour.

## 8. Declarations

- **Engineer**: `backend-engineer`. **Test-writer** for 4a.
- **Colour**: **red** — behaviour-changing (a failed disable is retried instead of only being
  logged). One preceding behaviour-free seam commit, characterised green (tasks T001).
- **New ADR**: none (§3c).
- **Security review**: **yes**. It changes how a revoked integration's credential is disabled.
- **Latency leg**: **none.** Identity's rotate endpoint and its Wolverine handlers are not on the
  event→overlay path (constitution §IV).
- **Not verifiable here (Docker down)**: the RabbitMQ queue and durable-listener wiring of the new
  type (FR-007); the Integration test suite; any live observation of the retry.
