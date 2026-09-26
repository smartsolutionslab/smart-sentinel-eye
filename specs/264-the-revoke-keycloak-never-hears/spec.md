# Spec 264 — The revoke Keycloak never hears

**Issue:** [#2206](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2206)
— *A rotated webhook credential is refused by the only endpoint it exists for, and
revoking it leaves the Keycloak client enabled*. **This spec delivers the issue's
second defect only**: revocation does not cross the context boundary. The first
defect (the rotated client's scope bundle lacked `sse-groups`) was fixed by
`2c3d19b7` (#2619) and is not touched here. The maintainer comment of 2026-09-26
scopes this piece of work and keeps #2241 open for its own ADR-blocked ask
(a request-time revocation check across the REST/WHEP auth paths).

**Branch:** `fix/2206-webhook-revocation-disables-client` (cut from `origin/develop`
at `7aab60de`). **Lane:** supervised (ADR-0037). Feature issue #2206 already exists
and is assigned. No `item-add` belongs to this spec.

**Spec number.** 264. On 2026-09-26 `origin/develop`'s highest was 261. Remote
branches `feat/2607-*` and `feat/2608-*` (open PRs #2616, #2617) claim 262 and 263.
259 and 260 were left free by spec 261 for renumbering elsewhere. **Re-check before
opening the PR.**

**ADRs and constitution sections referenced:**

- **Constitution §III** (bounded-context isolation): EventIngestion and Identity may
  not reference each other. The revocation crosses only through `Shared.Contracts`.
- **ADR-0040** (domain vs integration events) and **ADR-0073** (`V<N>` suffix,
  versioning): the aggregate already raises `WebhookIntegrationRevokedDomainEvent`.
  A domain-event handler translates it into a new `WebhookIntegrationRevokedV1`.
- **ADR-0088** (Wolverine defaults: per-module queue isolation, Postgres outbox, eager
  transactions): Identity gets its own queue for the new contract. The audit
  subscriber gets another. Neither competes with the other.
- **ADR-0102** (common `EventMetadata` envelope): the contract carries the fab in
  `Metadata.Fab`, as `WebhookIntegrationRotatedV1` does.
- **ADR-0042 / ADR-0057** (hand-rolled `ICommandHandler`, Wolverine dispatch): the
  Identity subscriber translates the message into a command, the shape
  StreamDistribution's `CameraRetiredIntegrationEventHandler` established.
- **ADR-0047** (`Result<T, Error>`), **ADR-0105** (`Ensure.That`), **ADR-0141**
  (`Option<T>` for absences).
- **ADR-0103** (integration tests on the Aspire fixture), **ADR-0139 / ADR-0144**
  (red first; phase-4a colour), **ADR-0036** (smallest change), **ADR-0109** (`[P]`).

**No new ADR.** See §4.

**Latency budget (§IV): N/A.** See §5.

---

## 1. The issue's premise, re-checked on this tree (`7aab60de`, 2026-09-26)

| Claim in #2206 | On this tree |
|---|---|
| `DELETE /webhook-integrations/{name}` only flips a local flag | **True.** `RevokeWebhookIntegrationCommandHandler.cs:64-65` calls `integration.Revoke(clock)` and `integrations.SaveAsync`. Nothing else happens. The handler takes no `IEventBus`. |
| No integration event is published | **True, and more precisely:** `WebhookIntegration.Revoke` (`WebhookIntegration.cs:94-104`) *does* raise `WebhookIntegrationRevokedDomainEvent`, and `WebhookIntegrationRepository.SaveAsync` (`:47-72`) *does* dispatch it. **No `IDomainEventHandler<WebhookIntegrationRevokedDomainEvent>` is registered**, so dispatch resolves an empty set and the event goes nowhere. The only registered EventIngestion domain-event handler is `EventIngestedDomainEventHandler` (`EventIngestionInfrastructureModule.cs:70-72`). |
| Kiosk and device revocation call `DisableClientAsync` | **True.** `DisableKioskCommandHandler.cs:32` and `DisableDeviceCommandHandler.cs:79` call `keycloak.DisableClientAsync(clientId.Value, …)` and then `client.Disable(clock)` + `SaveAsync`. Both are invoked by **Identity's own** HTTP endpoints (`DELETE /kiosks/{clientId}`, `DELETE /devices/{clientId}`), because Identity owns those registrations end to end. A webhook integration is **registered and revoked by EventIngestion** but its Keycloak client is **created and rotated by Identity** (`RotateWebhookClientCommandHandler.cs:104-143`, `ClientId = "webhook-{name}"` at `:46`). So the kiosk/device *mechanism* applies, but the *trigger* has to cross a context boundary. |
| Identity has a way to hear about it | **No.** `src/Identity/Application/EventHandlers/` holds one class, `ClientRegisteredDomainEventHandler`, which is a domain-event handler. **Identity subscribes to no integration event today.** This spec makes it a subscriber for the first time. The machinery needs no new wiring (plan §4): `AddWolverineForContext` already runs for Identity (`IdentityInfrastructureModule.cs:109-112`), and `WolverineDefaults` discovers handlers in `*.Application` and names queues by convention (`WolverineDefaults.cs:66, 93-96, 133-136`). |
| The gap is live | **True since `2c3d19b7`.** That commit added `KeycloakScopeBundles.GroupsScope` to the rotate handler's `DefaultClientScopes` (`RotateWebhookClientCommandHandler.cs:117`). A client **created** after it carries `groups`, passes the fab guard, and so can write events after revocation. |
| `POST /events/manual` ignores the revoked flag | **True, out of scope.** It is #2241's request-time check, which is ADR-blocked. This spec closes the credential at its source instead, which also covers `/events/manual`, because a disabled Keycloak client cannot mint a token for it. |

**Population exposed today.** `2c3d19b7` changed only the scopes a client is *created*
with. A secret roll does not change them. So only webhook clients **first created** on
or after `2c3d19b7` (2026-09-26) carry `groups`. There is no production deployment
(CLAUDE.md, "Observability"). This is why §3 defers a back-fill for integrations
revoked before this fix lands, and does not build one.

## 2. User stories

### US1 (P1): Revoking a webhook integration disables its Keycloak client

When an operator revokes a webhook integration, EventIngestion announces the
revocation on the bus. Identity hears it and disables the integration's Keycloak client
the same way it disables a kiosk or a device: `DisableClientAsync`, then
`RegisteredClient.Disable`. Once that happens, the client can mint no new token. The
integration therefore stops writing events through **every** path that trusts a
Keycloak token, `/events/manual` included. An integration that was never rotated has
no Keycloak client, and its revocation is a no-op on the Identity side.

**Why P1:** this is the defect. It can be observed on its own. Revoke, then ask
Keycloak whether the client is enabled and whether it will still issue a token.

**Eventual, not synchronous.** The `DELETE` still answers `200` from EventIngestion
alone. The disable follows asynchronously over RabbitMQ, usually within about a
second. A token minted *before* the disable stays valid until it expires. Keycloak's
access-token lifespan bounds that window. Closing it needs a request-time check, which
is #2241's (§3).

```gherkin
Background:
  Given the running Aspire stack
  And an operator holding /fabs/munich with sse.webhooks.write

Scenario: happy path — a rotated integration's client is disabled on revoke
  Given a webhook integration "w" registered in munich via POST /webhook-integrations
  And "w" rotated via POST /webhook-integrations/w/rotate {fabId: munich} (If-None-Match: *)
  And Keycloak reports client "webhook-w" enabled=true                       # control
  And a client_credentials grant for "webhook-w" with the rotated secret succeeds  # control
  When the operator sends DELETE /webhook-integrations/w with If-Match: "<version>"
  Then the response is 200
  And within 20 s Keycloak reports client "webhook-w" enabled=false
  And a client_credentials grant for "webhook-w" with the same secret is refused (non-2xx, no access_token)
  And Identity's registered_clients row for "webhook-w" has DisabledAt set

Scenario: happy path — the announcement carries what Identity needs
  Given an unrevoked integration "w" in fab munich
  When it is revoked at T
  Then exactly one WebhookIntegrationRevokedV1 is published
       with IntegrationName "w", RevokedAt T and Metadata.Fab "munich"
  And it is captured in EventIngestion's outbox in the same transaction as the RevokedAt write

Scenario: happy path — a never-rotated integration revokes cleanly
  Given an integration "s" still on StaticHash validation (never rotated)
  When it is revoked
  Then the response is 200
  And WebhookIntegrationRevokedV1 is still published
  And Identity finds no webhook client for ("webhook-s", munich), logs it, and settles the message
  And DisableClientAsync is not called

Scenario: conflict — a repeated revoke announces nothing twice
  Given "w" is already revoked
  When DELETE /webhook-integrations/w is sent again
  Then the response is 200 (unchanged, idempotent short-circuit at RevokeWebhookIntegrationCommandHandler.cs:50-53)
  And no second WebhookIntegrationRevokedV1 is published

Scenario: conflict — redelivery of the same message is harmless
  Given Identity has already disabled "webhook-w" for a WebhookIntegrationRevokedV1
  When the same message is delivered again (at-least-once)
  Then DisableClientAsync is called again and is a no-op in Keycloak
  And RegisteredClient.Disable does not move DisabledAt and raises no second event

Scenario: conflict — Keycloak is unreachable when the message arrives
  Given Keycloak's Admin API fails for DisableClientAsync
  When Identity handles WebhookIntegrationRevokedV1 for "w"
  Then the registered_clients row is NOT marked disabled
  And the handler throws, so Wolverine redelivers the message (retry, not swallow)

Scenario: conflict — a stale revoke is still refused before anything is announced
  Given "w" at version 3
  When DELETE /webhook-integrations/w is sent with If-Match: "2"
  Then the response is 409 WEBHOOK_INTEGRATION_STALE (unchanged)
  And no WebhookIntegrationRevokedV1 is published

Scenario: bad request — a malformed announcement is dropped, not retried forever
  Given a WebhookIntegrationRevokedV1 whose Metadata.Fab is null, blank or unparsable,
        or whose IntegrationName does not form a valid ClientId
  When Identity handles it
  Then it logs a warning naming the integration and the reason
  And it settles the message without calling Keycloak
       (mirrors WebhookIntegrationRotatedV1Handler.ParseFab, EventIngestion side)

Scenario: auth — the disable is fab-scoped
  Given a webhook client "webhook-w" registered in fab dresden
  When a WebhookIntegrationRevokedV1 for "w" arrives with Metadata.Fab "munich"
  Then Identity does not disable the dresden client (lookup is GetWithinFabAsync(munich, …))
  And it logs the not-found outcome at the same level as a never-rotated integration

Scenario: auth — only a webhook client is disabled by this path
  Given a registered client with ClientId "webhook-w" whose Kind is not WebhookIntegration
  When a WebhookIntegrationRevokedV1 for "w" arrives
  Then that client is not disabled (Kind check, as DisableKiosk/DisableDevice check theirs)

Scenario: auth — the revoke endpoint's own guard is unchanged
  Given an operator who does not hold the integration's fab
  When they send DELETE /webhook-integrations/w
  Then the response is 404 WEBHOOK_INTEGRATION_NOT_FOUND (unchanged, #1545)
  And nothing is published
```

## 3. Scope

### In this PR

- `Shared.Contracts`: a new `WebhookIntegrationRevokedV1` (plan §2).
- EventIngestion: `WebhookIntegrationRevokedDomainEvent` gains `Fab`. A new
  `WebhookIntegrationRevokedDomainEventHandler` publishes the V1. It is registered in
  `EventIngestionInfrastructureModule`.
- Identity: a new `DisableWebhookClientCommand` + handler, mirroring
  `DisableKioskCommandHandler`. A new Wolverine subscriber,
  `WebhookIntegrationRevokedIntegrationEventHandler`, translates the V1 into that
  command. Both are registered in `IdentityInfrastructureModule`.
- AuditObservability: the new V1 gets its `Handle` entry and its resource-map hand
  tweak. The architecture tests `Every_integration_event_has_an_audit_handler` and
  `V1ResourceMap_covers_every_IIntegrationEvent` make this mandatory. Without them the
  build goes red.
- Tests per plan §6, and the new integration class added to a CI shard filter.

### Deferred, and to where

| Item | Where | Why not here |
|---|---|---|
| `POST /events/manual` / REST / WHEP request-time revocation check | #2241 | ADR-blocked, per the maintainer comment. It would also close the "token minted before the disable" window, which this spec only bounds by token lifespan. |
| Scope bundle lacking `sse-groups` (issue defect 1) | Done, `2c3d19b7` / #2619 | Already fixed. |
| Back-fill: integrations revoked **before** this lands whose client is still enabled | A follow-up issue, if a deployment has any | Revoke is idempotent, and a repeat `DELETE` short-circuits without raising the domain event (`RevokeWebhookIntegrationCommandHandler.cs:50-53`), so re-revoking does not repair them. The exposed population is clients *created* on or after `2c3d19b7` and revoked before this merges (§1). That population is at most hours old and has no production deployment. Remedy if found: disable the client in the Keycloak admin console. **Stated assumption, for the gate reviewer to accept or reject.** |
| **Rotate after revoke.** Identity's rotate does not know EventIngestion revoked the integration. A *first* rotation of an already-revoked, never-rotated integration creates a fresh, enabled client. A rotation of a revoked integration whose client this spec disabled hits `RegisteredClient.Rotate`'s `InvalidOperationException` (`RegisteredClient.cs:124-128`), which the rotate handler deliberately does not catch (`RotateWebhookClientCommandHandler.cs:146`). | A follow-up issue (**to be filed at the gate**) | Needs a sse.webhooks.write operator acting deliberately, not a leaked credential. Fixing it needs Identity to hold revocation state or EventIngestion to gate rotation, which is a design choice this bug fix should not make silently. |
| Actor attribution on the audit row | Nowhere now | `RevokeWebhookIntegrationCommand` carries no operator. The V1's `Metadata.Actor` is `null`, so the audit row reads *System*. That is the precedent `ClientRegisteredDomainEventHandler` set for `DeviceRegisteredV1` / `KioskEnrolledV1` (`:49, :63`). Before this spec, revocation produced **no** V1 audit row at all. |
| Sharing the `"webhook-{name}"` derivation between rotate and disable | Nowhere now | Extracting it would refactor `RotateWebhookClientCommandHandler`, which would mix a refactor into a bug fix. The integration test in US1 goes through the **real** rotate path, so if the two derivations ever diverged it would fail (plan §6). |

## 4. Why no new ADR

Every piece is established machinery used as it already is:

- The end state (Keycloak client disabled plus the local row marked) is exactly
  `DisableKioskCommandHandler` / `DisableDeviceCommandHandler`, with the same
  `IKeycloakAdminClient.DisableClientAsync`, the same `RegisteredClient.Disable`, the
  same fab-scoped `GetWithinFabAsync` and the same Kind check.
- The crossing (domain event, then domain-event handler, then `IEventBus.PublishAsync`
  into the outbox) is `EventIngestedDomainEventHandler` and the other ten
  domain-event handlers (ADR-0040, spec 021).
- The subscription (a Wolverine handler in `*.Application`, discovered by
  `WolverineDefaults`, with a per-module queue by convention, that translates to a
  command and throws on a retryable failure) is
  `CameraRetiredIntegrationEventHandler` (StreamDistribution, spec 028), and the
  reverse-direction twin `WebhookIntegrationRotatedV1Handler` already runs between
  these same two contexts.
- The one novelty is that **Identity becomes a message subscriber for the first time**.
  That is a use of ADR-0088 as written, not an extension of it: no transport,
  exchange, queue or listener is configured by hand.

Nothing found while reading changes the orchestrator's conclusion. The two design
choices that *would* need one (a request-time revocation check, and making Identity
authoritative for revocation) are the deferred rows in §3.

## 5. Latency budget (§IV)

**N/A.** No leg of event arrival → overlay rendered is touched. `POST /events/*`
handling, `FabEventIngestedV1` fan-out and every downstream leg are unchanged. The
new message goes to Identity and the audit context only, and it is published once per
revocation, which is an operator action. §VII's dashboard rule: N/A.

## 6. Phase-4a colour: **red**

This is behaviour-changing. Today a revoked, rotated integration's client stays
enabled and keeps minting tokens indefinitely. After the fix it is disabled. Every
new-behaviour assertion is observed red on this branch before phase 4b. The
load-bearing red is the **integration test's `enabled=false` assertion against the
real Keycloak** (plan §6.1). Unit reds on types that do not yet exist are compile
failures. They count, but on their own they would be a weak red, and the integration
red is what makes the claim.

The existing `RevokeWebhookIntegrationCommandHandler` facts
(`tests/EventIngestion.Application.Tests/Commands/WebhookIntegrationCommandHandlerTests.cs`)
and `WebhookIntegrationTests.Revoke_*` must pass **unmodified**. The endpoint's
status codes, idempotency and stale refusal do not change.

## 7. Independent end-to-end test procedure

1. `aspire run` (one stack per machine). Get an operator token for `admin`
   (`/fabs/munich`, `sse.webhooks.write`) from `management-web`.
2. `POST {event-ingestion}/webhook-integrations` `{ "name": "e2e-2206", "defaultKind": "WebhookAlarm" }`, which returns `201`.
3. `POST {identity}/webhook-integrations/e2e-2206/rotate` with body `{ "fabId": "munich" }`
   and `If-None-Match: *`. This returns `200` with `clientId: "webhook-e2e-2206"` and
   `clientSecret`.
4. Keycloak admin: `GET /admin/realms/smart-sentinel-eye/clients?clientId=webhook-e2e-2206`
   shows `enabled: true`. A `client_credentials` grant with the secret returns an
   `access_token`.
5. `GET {event-ingestion}/webhook-integrations`, read `version`, then send
   `DELETE /webhook-integrations/e2e-2206` with `If-Match: "<version>"`. This returns `200`.
6. Within a few seconds, the same Keycloak query shows **`enabled: false`**, and the
   same `client_credentials` grant is **refused**: a non-2xx response with an OAuth
   `error` and no `access_token`. Record the exact status and `error` code as
   observed. This spec does not pin them, because Keycloak's answer for a disabled
   client is not documented here. On `develop` the client stays `enabled: true` and the
   grant succeeds.
7. Aspire dashboard, *Structured logs*, `identity`: one Information entry for the
   disable naming `webhook-e2e-2206`. In *Traces*, the revoke request's trace
   continues into Identity's handling of `WebhookIntegrationRevokedV1`.
8. Audit: the audit context's query endpoint shows one `WebhookIntegrationRevokedV1`
   row for resource kind `WebhookIntegration`, identifier `e2e-2206`, fab `munich`.
9. Repeat step 5 on an integration that was **never** rotated. It returns `200`.
   Identity logs "no webhook client", and no RabbitMQ message is left in
   `identity.SmartSentinelEye.Shared.Contracts.EventIngestion.WebhookIntegrationRevokedV1`
   or its error queue.

## 8. Success criteria

- **SC1:** after a `200` revoke of a rotated integration, Keycloak reports its client
  `enabled=false` within 20 s, and a `client_credentials` grant with its secret is
  refused (integration test, red on `develop`).
- **SC2:** exactly one `WebhookIntegrationRevokedV1`, carrying name, `RevokedAt` and
  `Metadata.Fab`, is published per first revoke. None is published for a repeat or a
  stale or not-found revoke (unit).
- **SC3:** Identity's handler disables only a `WebhookIntegration`-kind client in the
  message's fab, rethrows on Keycloak failure without marking the row, and settles
  not-found and malformed messages without retrying (unit).
- **SC4:** `Architecture.Tests` are green, including the audit-coverage pair and
  `BoundaryTests` (no EventIngestion↔Identity reference).
- **SC5:** the existing revoke-endpoint and domain facts pass unmodified.
- **SC6:** Release build (`TreatWarningsAsErrors`) and `dotnet format --verify-no-changes`
  are clean on the touched projects.
