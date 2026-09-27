# Spec 270 — The token that outlives its revocation

**Issue:** [#2241](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2241)
— *Revocation stops new mints but not live tokens, and never touches the webhook
client at all*. **This spec delivers the primary ask only**: a request-time
`DisabledAt` check across the nine REST APIs and the WHEP media-setup path.

**Branch:** `fix/2241-request-time-disabledat-check` (cut from `origin/develop` at
`2f477474`). **Lane:** supervised (ADR-0037). Feature issue #2241 already exists. No
new issue is needed (tasks.md §Board).

**Spec number.** 270. On 2026-09-27, 261–269 were claimed across in-flight worktrees
and branches (269 collided with issue #2324's spec, so this spec moved to 270; the
orchestrator checked every local worktree and branch). **Re-check before opening the
PR** (memory: *spec number: origin/develop isn't enough*).

**Decision (already made, recorded here — not re-litigated).** The product owner
decided on 2026-09-26, in a comment on #2241:

> Close via a DisabledAt check at request time, not introspection or a shortened
> token lifespan — cheap, no new live dependency on the WHEP media-setup path.

**ADR-0160** records that decision together with the mechanism this spec builds
(plan.md §0 explains why it gets an ADR).

**ADRs and constitution sections referenced:** ADR-0160 (this decision), ADR-0007 /
ADR-0008 / ADR-0023 (Keycloak, scopes), ADR-0106 (the gateway does no auth offload),
ADR-0116 (service account for a cross-context read), ADR-0143 (retry safety),
ADR-0153 (one instance per service), ADR-0154 (degrade rather than fail),
ADR-0103 (Aspire fixture), ADR-0139 / ADR-0144 (red first), ADR-0109 (`[P]`).
Constitution §III (no cross-context references), §VIII and §Security (short-lived,
token-bound credentials), §IV (N/A, see §6).

---

## 1. The issue's premise, re-checked on this tree (`2f477474`, 2026-09-27)

| Claim in #2241 | On this tree |
|---|---|
| `DisableClientAsync` disables the Keycloak client, so no new token can be minted | **True.** Called by `DisableKioskCommandHandler.cs:32` and `DisableDeviceCommandHandler.cs:32` (and, since spec 264 / PR #2630, `DisableWebhookClientCommandHandler.cs:37`). Each handler calls it *before* `client.Disable(clock)` stamps `DisabledAt`. |
| `accessTokenLifespan` is 3600 | **True.** `src/AppHost/Realms/smart-sentinel-eye-realm.json:5`. |
| Nothing in `src/Identity` or `src/ServiceDefaults` calls logout, sessions or introspection | **True.** |
| `WhepAuthValidator` validates locally against realm keys | **True.** `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs`. `ValidateAsync` uses `JwtSecurityTokenHandler.ValidateToken` against discovery-document keys and nothing else. |
| No request-time check of `DisabledAt` anywhere | **True.** Outside the domain (`RegisteredClient.cs:29,74-80,92`, `DisabledAt.cs`), `DisabledAt` appears only in the projection (`RegisteredClientProjection.cs:40`), the DTO (`RegisteredClientSummaryDto.cs:23`), the EF configuration (`RegisteredClientConfiguration.cs:66-68,88`) and the reuse filters (`RegisteredClientRepository.cs:32,52`). |
| "A device revoked at T keeps watching streams until T + 60 min" | **Imprecise, and the defect is real anyway.** The `Device` scope bundle (`KeycloakScopeBundles.cs:72-76`) has no `sse.streams.read`, so a device token is refused by `/streams/authorize` for lack of scope whether or not it is revoked. An **enrolled kiosk** client holds `sse.streams.read` (`:59-67`), and so do the realm's static clients. A revoked device keeps `sse.cameras.read` and `sse.events.publish` for 60 minutes. A revoked kiosk client keeps streams, cameras, layouts, overlays, variables and `events.write`. |
| The webhook client is never disabled | **Fixed, out of scope.** It duplicated #2206 and landed as PR #2630 (spec 264: `WebhookIntegrationRevokedV1` plus Identity's subscriber). This spec does not touch that path. The check built here is kind-agnostic, so it also refuses a revoked webhook client's leftover Keycloak token on `POST /events/manual`. That is defence in depth, not the fix. |

**What is revocable.** Every `RegisteredClient` (`Kiosk`, `Device`,
`WebhookIntegration`) is a Keycloak **service-account** client. `EnrollKioskCommandHandler`
builds `ServiceAccountsEnabled: true, StandardFlowEnabled: false`, and the others do
the same. Such a client's token carries its client id in `azp`. None of them have
refresh tokens. So "the client that minted this token" is `azp`, and "when" is
`iat`.

## 2. User stories

### US1 (P1): A revoked kiosk client cannot open a stream with the token it already holds

An operator disables an enrolled kiosk (`DELETE /kiosks/{clientId}`). A WHEP
authorisation using an access token minted **before** the disable is refused within
seconds, not at the token's expiry.

**Why P1:** this is the headline of #2241. It is the media path, and there the
product owner explicitly excluded a new live dependency.

**Independent test:** enrol a kiosk. Mint a token. `POST /streams/authorize` answers
200. Disable the kiosk. The same token is answered 401 within the bound in FR-006.

### US2 (P1): A revoked client cannot call any of the nine REST APIs with the token it already holds

The same, across every API that validates bearer tokens: AuditObservability,
Automation, CameraCatalog, EventIngestion, Identity, LayoutComposition,
OverlayDesigner, StreamDistribution and SystemVariables. All nine share
`AuthenticationDefaults.AddBearerAuthentication`, so one hook covers all nine.

**Why P1:** a revoked kiosk client holds read scopes on five of the nine, and
`events.write` on a sixth.

**Independent test:** as US1, against `GET /cameras` (CameraCatalog). CameraCatalog
is not Identity, so the test proves the check crosses the context boundary. The
test also asserts that a **newly registered** client using the same client id,
minted after the disable, is admitted.

US1 and US2 share one foundation (§4, tasks.md Phase 2) and then split into disjoint
files. They ship together in one PR, because #2241 asks for both, but each can be
observed on its own.

## 3. Acceptance scenarios

```gherkin
Feature: A revoked client's already-issued token is refused at request time

  Background:
    Given an enrolled kiosk client "kiosk-269" in fab "munich"
    And an access token T1 minted for "kiosk-269" by client_credentials

  # --- Happy path (the fix) ---
  Scenario: WHEP refuses a token minted before the revocation
    Given POST /streams/authorize with T1 answers 200
    When an operator disables "kiosk-269"
    Then within 20 s POST /streams/authorize with T1 answers 401

  Scenario: A REST API refuses a token minted before the revocation
    Given GET /cameras on CameraCatalog with T1 answers 200
    When an operator disables "kiosk-269"
    Then within 20 s GET /cameras with T1 answers 401
    And the response carries WWW-Authenticate: Bearer error="invalid_token"

  # --- Conflict: the client id is reused ---
  # Today a same-id re-enrolment is refused by Keycloak itself: the disabled
  # client still exists, so CreateClientAsync throws KeycloakClientAlreadyExists
  # (HttpKeycloakAdminClient.cs:57-72). The local table permits reuse
  # (RegisteredClientRepository.cs:26-36), so the rule is written for it anyway,
  # and this scenario is proved at unit level with controlled clocks.
  Scenario: A client re-registered under the same id is not refused for its predecessor's revocation
    Given "kiosk-269" was disabled at D
    And a client "kiosk-269" exists again whose token T2 has iat > D + 5 min
    Then the revocation check admits T2
    And it still refuses T1 (iat < D)

  # --- Bad request: tokens the check must not accept or crash on ---
  Scenario: A token without azp is judged by the existing pipeline only
    Given a validly signed token with no azp claim
    Then the revocation check admits it
    And the outcome is exactly today's outcome for that token

  Scenario: A revoked client's token with no iat is refused
    Given "kiosk-269" is disabled
    And a validly signed token for azp "kiosk-269" with no iat claim
    Then it is refused

  # --- Auth: who may read the revocation list ---
  Scenario: The revocation list requires its own scope
    When GET /registered-clients/revoked is called without a token
    Then it answers 401
    When it is called with a token lacking sse.identity.revocations.read
    Then it answers 403
    When it is called by revocation-list-reader
    Then it answers 200 with every disabled client id and its latest DisabledAt, across all fabs and kinds

  # --- Unaffected populations ---
  Scenario: Human operators are unaffected
    Given an operator token (azp "management-web")
    Then every call the operator could make before this change it can still make

  # --- Degraded, not down ---
  Scenario: Identity is unreachable when a service starts
    Given Identity does not answer GET /registered-clients/revoked
    When CameraCatalog starts
    Then CameraCatalog serves requests
    And its "revocation-snapshot" health check reports Degraded
    And one Warning is logged, not one per refresh attempt
    When Identity starts answering
    Then the snapshot loads, the health check reports Healthy, and one Information is logged
```

## 4. Functional requirements

- **FR-001** Every enforcement point (the nine APIs' `JwtBearer` pipeline and
  `WhepAuthValidator`) refuses a token when a disabled `RegisteredClient` with
  `ClientId == azp` has `DisabledAt >= iat - 5 min`. The latest `DisabledAt` per
  client id wins.
- **FR-002** The refusal takes the existing rejection shape: REST `401` (the bearer
  handler's `invalid_token` challenge), WHEP `WhepAuthFailure.TokenRejected`. It
  reveals nothing more than an expired token would.
- **FR-003** The check performs **no I/O on the request path**. It reads an
  in-process snapshot.
- **FR-004** Each service refreshes its snapshot every **5 s** from Identity,
  replacing it on success. Identity builds its own snapshot in process, not over
  HTTP.
- **FR-005** A failed refresh keeps the previous snapshot. Before the first
  successful load, the check admits (today's behaviour), and the
  `revocation-snapshot` health check reports `Degraded` (ADR-0154). Transitions log
  once each way.
- **FR-006** Revocation takes effect on every enforcement point within **one refresh
  period plus one refresh round-trip** of the disable committing. That is ≤ 10 s
  under normal operation. The integration tests poll with a 20 s ceiling, the
  precedent spec 264 set for an eventual effect.
- **FR-007** Identity exposes `GET /registered-clients/revoked` under the new scope
  `sse.identity.revocations.read`. The endpoint is not fab-scoped: every consumer
  needs every fab. The realm gains that client scope and one confidential client,
  `revocation-list-reader`, holding only it. The AppHost seeds its secret.
- **FR-008** The response lists client id and latest `DisabledAt` for **every**
  disabled row, across all kinds and fabs (plan.md §3 covers bounding it).
- **FR-009** The hook **chains** with any `JwtBearerEvents` a service already sets
  (LayoutComposition's `OnMessageReceived` at `src/LayoutComposition/Api/Program.cs:18-22`).

## 5. Out of scope

- **The webhook-client disable** (#2206, PR #2630, spec 264). Already merged.
- **Tearing down live sessions.** An established WebRTC session or SignalR
  connection is not closed. It is refused at its next setup. Recommended as a
  follow-up issue.
- **Human principals** (operators, wall accounts, `offline_access`). They are
  Keycloak users, not `RegisteredClient`s (#1995, and the 2026-09-13 comment on
  #2241).
- **Introspection, and a shorter `accessTokenLifespan`.** Rejected by the product
  owner.
- **The webhook ingest route's own bearer** (`/events/webhook/{name}`,
  `AllowAnonymous`). It already refuses a revoked integration
  (`EventsEndpoints.Writes.cs:211`).

## 6. Latency budget (constitution §IV): N/A

The event-to-overlay path is not touched. `/streams/authorize` runs once at WHEP
session setup, before media flows. It is not one of the six legs. On the REST
pipeline and at WHEP setup the check adds one `FrozenDictionary` lookup (plan.md §4).
`NFR001_JwtValidationLatencyTests` already bounds bearer validation and must stay
green.

## 7. Independent end-to-end test procedure

1. Boot the stack (Aspire). **Delete the Keycloak volume first**, because the realm
   gained a client and a scope (memory: *realm edits need the volume deleted*).
2. As an operator with `sse.identity.kiosks.write`, `POST /kiosks/enroll` for
   `kiosk-269`, and keep the secret.
3. `client_credentials` grant for `kiosk-269` gives T1.
4. `GET /cameras` (CameraCatalog, direct and via the gateway) with T1 answers 200.
   `POST /streams/authorize` with T1 for a provisioned camera answers 200.
5. `DELETE /kiosks/kiosk-269`.
6. Poll both calls every 500 ms. Both turn 401 within ≤ 10 s. Record the observed
   delay in the verification note.
7. Stop Identity. Restart CameraCatalog. `/health` reports `revocation-snapshot`
   Degraded, and the service keeps serving an operator token. Start Identity. The
   check turns Healthy within 5 s.

## 8. Phase-4a colour

**Behaviour-changing → red.** Every new-behaviour test in tasks.md must be observed
failing on `develop` before the implementation, and the failure must be quoted in the
PR (ADR-0139).
