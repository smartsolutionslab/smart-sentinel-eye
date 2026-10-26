# Spec 464 — A disabled client replays no secret

**Issue:** #3123 (found by the #3112 security review; related #3096 / spec 454).
**ADRs:** ADR-0142 (Idempotency-Key: same key, same answer; a replay rebuilds the answer and may
refuse), ADR-0047 / ADR-0089 (`ApiError` with a named `UPPER_SNAKE` code and status), ADR-0105
(`Ensure.That`), ADR-0144 (autonomous lane). No new ADR: the owner decided the behaviour on the
issue (2026-10-26), and the refusal is one more member of the replay-refusal family ADR-0142
already admits (spec 454 added `IDEMPOTENCY_KEY_SUPERSEDED` the same way).
**Latency budget:** N/A — Identity credential endpoints are not on the event-to-overlay path.
**Phase 4a colour:** behaviour-changing → **red first**.

## Problem

`ReplayRegisteredClientQueryHandler` (src/Identity/Application/Queries/Handlers/ReplayRegisteredClientQueryHandler.cs:50)
reads the row with `GetByIdentifierAsync`, which has no `DisabledAt` filter, then reads the live secret
from Keycloak (:68) and returns it (:84). Nothing asks whether the client is disabled.

Concretely reachable: `RotateWebhookClientCommandHandler` disables the client it just rotated or
created when a revoke raced it (`disabledDuringRace`, src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:69-85),
yet still returns `Success` with the secret (:116-121); the endpoint completes the key as created
(src/Identity/Api/WebhookRotationEndpoints.cs:174-180, `IdempotentOutcome.CreatedAt`). A same-key
replay (:205-230) then hands the secret out again. The same gap exists for devices and kiosks
(`DevicesEndpoints.cs:183-198`, `KiosksEndpoints.cs:180-195`): a client disabled after its
registration still has its secret replayed. Inert while Keycloak keeps the client disabled; live if
anyone re-enables it by hand.

## Owner decision (recorded on #3123)

- The **first call is unchanged** (spec 434 FR-005): the secret is returned once, with the success answer.
- A **same-key replay for a disabled client refuses**; it never reads or returns the secret.

## User story 1 (P1) — a replay never returns a disabled client's secret

As a security owner, I want an idempotent replay to refuse when the client it names is disabled,
so that a key cannot re-issue a credential the system has turned off.

### Acceptance scenarios

```gherkin
Scenario: Replay of a key whose client has been disabled is refused (happy path of the fix)
  Given a client registered under Idempotency-Key K by operator O in fab F
  And the client has since been disabled
  When O replays K with the same request in fab F
  Then the answer is 409 with problem title "REPLAYED_CLIENT_DISABLED"
  And Keycloak is not asked for the secret
  And the problem detail names no client id, version, fab or secret

Scenario: Rotation that lost the revoke race, then replayed
  Given a webhook rotation under key K ended with disabledDuringRace
  And the first call returned the secret with its success answer (FR-005, unchanged)
  When the caller replays K
  Then the answer is 409 "REPLAYED_CLIENT_DISABLED" and carries no secret

Scenario: Replay of an active client is unchanged (conflict-free regression)
  Given a client registered under key K and still active
  When K is replayed
  Then the same secret comes back, unchanged, exactly as today

Scenario: Disabled and superseded at once (precedence)
  Given a bound webhook replay whose client is disabled and whose version also moved
  When the key is replayed
  Then the answer is "REPLAYED_CLIENT_DISABLED" (the disabled check runs first, before any read)

Scenario: Missing registration keeps its own code (bad request / gone)
  Given the key names a registration that no longer exists
  Then the answer stays 409 "REPLAYED_REGISTRATION_MISSING"

Scenario: Auth and scope unchanged
  Given a different operator, fab or request fingerprint reuses K
  Then the existing scope rules apply unchanged (another caller never reaches this replay;
       a fingerprint or fab mismatch still answers 422 IDEMPOTENCY_KEY_REUSED)
```

### Functional requirements

- **FR-001** `ReplayRegisteredClientQueryHandler` refuses with `ReplayedClientDisabled` when the
  found row has `DisabledAt` set, **before** the version check and **before** the Keycloak read.
- **FR-002** The refusal is `409 Conflict`, code `REPLAYED_CLIENT_DISABLED`, in the existing
  `ReplayRegistrationError` family; its message names no client, version, fab or secret.
- **FR-003** The first call is untouched: `RotateWebhookClientCommandHandler`, `RegisterDevice…`,
  `EnrollKiosk…` and the idempotency completion keep their current answers (spec 434 FR-005).
- **FR-004** Applies to all three replay callers (devices, kiosks, webhook rotation) because they share the handler.
- **FR-005** `IRegisteredClientRepository.GetByIdentifierAsync` is not changed: "gone" and
  "disabled" stay distinguishable codes.

### Contract note

Additive only: one new problem code on a status (409) the replay path already returns. No DTO,
route, header or first-call change. Stated here because it is a new wire string.

### Out of scope

- Withholding the secret on the first call (the rejected option on #3123).
- A disable that commits *during* an unbound replay's Keycloak read: the replay began before the
  disable and is linearisable to just before it. Bound (webhook) replays already re-check the version after the read.

## Independent end-to-end test

1. Unit (red first): in `tests/Identity.Application.Tests/Queries/ReplayRegisteredClientQueryHandlerTests.cs`,
   register a client, `Disable(clock)` it, replay → `REPLAYED_CLIENT_DISABLED`, `keycloak.CallCount` unchanged from arrange; bound variant too.
2. Endpoint (red first): in `tests/Architecture.Tests/`, through `IdentityEndpointHost`, a stored
   completed key whose held client is disabled → rotate replay answers 409 titled `REPLAYED_CLIENT_DISABLED`; device replay likewise.
3. Existing `ReplayRegisteredClientQueryHandler*Tests`, `WebhookRotationReplaySuperseded*Tests` and
   `DeviceAndKioskEndpointCharacterisationTests` pass unmodified.
