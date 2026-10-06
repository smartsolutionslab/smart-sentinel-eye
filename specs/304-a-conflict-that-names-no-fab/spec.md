# Spec 304 — A conflict that names no fab

**Issue**: #2626 · **Branch**: `fix/2626-mask-cross-fab-device-registration-enumeration`
**ADRs**: ADR-0159 (one realm per deployment, fabs as groups — why `clientId` is
realm-unique), ADR-0047/0089 (`ApiError` → problem body), ADR-0142 (idempotency
scope includes the fab), ADR-0139 (characterisation vs red-first).
**Precedent**: spec 180 US1 (`DELETE /devices/{clientId}`).
**Kind**: **behaviour-preserving — characterisation, observed green** (see §2).

## 1. The decision (human, recorded on #2626)

> Mask the 409 on device registration, mirroring the DELETE path's existing
> precedent (spec 180) — same shape 409 regardless of which fab holds the conflict.

## 2. Investigation — the premise against current code

Phase 1 was run as an investigation (the issue's premise is from spec 264's plan §6).

**The 409 is already fab-neutral.** Both conflict branches of
`RegisterDeviceCommandHandler.HandleAsync`
(`src/Identity/Application/Commands/Handlers/RegisterDeviceCommandHandler.cs:46-51`,
the realm-global `GetByClientIdAsync`; and `:79-82`, Keycloak's
`KeycloakClientAlreadyExistsException`) return
`RegisterDeviceError.DeviceAlreadyRegistered(clientId)`
(`src/Identity/Application/Commands/RegisterDeviceErrors.cs:9-13`), rendered by
`ApiErrorResults.ToProblem` (`src/ServiceDefaults/ApiErrorResults.cs:19`) as:

```
409  { "title": "DEVICE_ALREADY_REGISTERED",
       "detail": "A device with clientId '<caller's own clientId>' is already registered.",
       "status": 409, "type": ..., "traceId": ... }
```

Nothing in it depends on the holder: no fab, no `RegisteredClientIdentifier`, no
kind, no registrant. The lookup path is the same query for either holder, so
no fab-dependent branch or timing difference exists either — a fab-neutral
timing difference remains between an active-row conflict and a disabled-row-
or-Keycloak-only conflict (the latter falls through to `CreateClientAsync`,
costing an admin-token fetch plus a `GET clients?clientId=` round trip),
unrelated to which fab holds anything, out of scope here. **The decision's
stated property — the same 409 regardless of which fab holds the conflict —
already holds; it is simply unpinned.** No test today registers the same id
from two fabs.

**What the DELETE precedent actually does** (spec 180 US1):
`DisableDeviceCommandHandler` uses `GetWithinFabAsync(fab, clientId)`
(`RegisteredClientRepository.cs:39-57`, fab in the predicate) so another fab's
device is *never materialised* and answers `404 DEVICE_NOT_FOUND`, byte-identical
to "no such device". Its masking property is: **caller cannot distinguish
"other fab holds it" from the nearest same-fab outcome.** For register, the
nearest same-fab outcome to "other fab holds it" is "my fab holds it" → the same
409. That is exactly today's behaviour.

**What remains disclosed, and why this spec does not remove it.** A fab-B admin
still learns one bit: *some* client in the realm has this id (409 vs 201).
DELETE can hide existence because "not found" is a truthful no-op; register
cannot answer 201 for an id it cannot create, and `GetByClientIdAsync` must stay
realm-global because Keycloak enforces realm-wide `clientId` uniqueness
(ADR-0159). Removing that bit means a fab-scoped `clientId` namespace (e.g. fab
prefix or opaque generated ids) — that changes the MQTT ACL binding, device
provisioning and every existing device id, and **is an ADR, not this slice**.
Recorded as residual; see §6.

## 3. User story

### US1 (P1) — A registration conflict reads the same whoever holds the id

As a fab admin, when the clientId I try to register is taken, the refusal tells
me only that it is taken — never whether my fab or another holds it — and
nothing is created anywhere.

```gherkin
Scenario: same-fab conflict (pinned, unchanged)
  Given munich's admin registered plc "t304-<guid>" in fab munich
  When munich's admin registers plc "t304-<guid>" in fab munich again
  Then the response is 409 "DEVICE_ALREADY_REGISTERED"

Scenario: cross-fab conflict is indistinguishable
  Given munich's admin registered plc "t304-<guid>" in fab munich
  When dresden's operator registers plc "t304-<guid>" with ?fabId=dresden
  Then the response is 409 "DEVICE_ALREADY_REGISTERED"
  And status, title, detail and type equal the same-fab conflict's exactly
  And the set of body properties is identical (traceId excepted — per request)
  And the body does not contain "munich"

Scenario: the uniqueness check still refuses across fabs
  Given the cross-fab conflict above
  Then dresden's device list (GET /devices?fabId=dresden) does not contain the clientId
  And Keycloak holds exactly one client with that clientId, attribute sse.fab = munich

Scenario: bad request is unaffected
  When dresden's operator registers with ?fabId= (empty)
  Then the response is 400 "DEVICE_INVALID_INPUT"   # existing behaviour, not re-tested

Scenario: auth precedes lookup (existing, not re-tested)
  When dresden's operator registers with ?fabId=munich
  Then the response is 403 "RESOURCE_FAB_NOT_AUTHORIZED" before any lookup
```

## 4. Independent test procedure

Boot the Aspire stack via `AspireFixture`; run
`CrossFabRegistrationConflictIntegrationTests` (new) and the new handler unit
test. All are expected **green on first run** against unmodified production
code. A red here is a real disclosure finding: stop, quote it, and route a
production change to the backend engineer (§5 of plan).

## 5. Locked choices / latency

Identity context only; Minimal APIs (ADR-0070); xUnit + Shouldly, Aspire fixture
(ADR-0052/0103). **Latency budget: N/A** — not on the event→overlay path.

## 6. Out of scope (candidates for a maintainer)

1. Existence oracle (409 vs 201) — needs a fab-scoped clientId namespace; ADR.
2. `POST /kiosks/enroll` has the identical shape (`EnrollKioskCommandHandler.cs:24-29`,
   realm-global, `KIOSK_ALREADY_ENROLLED` holder-neutral). Same verdict; not pinned here.
