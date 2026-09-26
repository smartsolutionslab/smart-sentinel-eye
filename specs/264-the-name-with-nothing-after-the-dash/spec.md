# Spec 264 — The name with nothing after the dash

**Issue:** [#2575](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2575)
— *`RegisterDeviceRequest.DeviceIdentifier` accepts null/empty and silently
registers a client named `plc-`*. Labels `bug`, `agent:ready`. On Project #13,
status **Todo** — verified 2026-09-25 via
`gh issue view 2575 --json projectItems`. No `item-add` needed.

**Spec number.** Re-checked on 2026-09-26 against `origin/develop` and every
unmerged remote branch (`git ls-tree -r <ref> --name-only | grep specs/`). 256,
257 and 258 are each already claimed by merged or in-flight work; the highest
claimed number found is 263 (`specs/263-a-wall-that-changes-its-scene`,
unmerged branch `feat/2608-event-driven-scene-rotation`). This spec is **264**.
Re-check before the PR is opened.

**ADRs referenced:**

- **ADR-0047 / ADR-0089** — `Result<T, Error>` and `ApiError(Code, Message, Status)`;
  the refusal reuses the existing `RegisterDeviceError.InvalidDeviceIdentifier`.
- **ADR-0142** — this endpoint is one of the nine keyed creates; a refusal is
  `IdempotentOutcome.NothingCreated`, which releases the key. Unchanged.
- **ADR-0143** — the Keycloak **admin** client that creates the client is *not*
  retried; only the admin **token** mint opts back in with `RetryEveryMethod()`.
  `POST /devices/register` is not one of the token mints. Unchanged.
- **ADR-0141** — nullable/primitive wire shapes are native at the Api boundary.
- **ADR-0105** — argument guards (none added; see plan §2 for why the check is
  an early return, not an `Ensure` throw).
- **ADR-0139 / ADR-0144** — behaviour-changing, so phase 4a is **red**.
- **ADR-0103** — integration tests run against the Aspire fixture.
- **ADR-0036** — smallest change; the fix changes the bug and nothing else.

**No new ADR.** A missing input check on an existing refusal path. No contract,
error code, constitution clause or architectural decision changes.

**Latency budget (§IV): N/A.** Identity's device-registration control plane is
not on the event → overlay path.

---

## 1. The premise, re-checked on this tree (`0d349ce8`, 2026-09-25)

Confirmed by reading; the red run in phase 4a is the reproduction over real HTTP.

| Step | Location | What happens to `deviceIdentifier` = null / `""` |
|---|---|---|
| Wire | `src/Identity/Api/Requests/RegisterDeviceRequest.cs:3` | `record RegisterDeviceRequest(string DeviceType, string DeviceIdentifier)`. No `RespectNullableAnnotations` / `RespectRequiredConstructorParameters` anywhere in `src/`, so System.Text.Json binds an omitted or explicit-null member as `null` — the NRT annotation is not enforced at runtime. |
| Endpoint | `src/Identity/Api/DevicesEndpoints.cs:146` | Passed straight into `new RegisterDeviceCommand(body.DeviceType, body.DeviceIdentifier, fab, actingOperator)`. No check. |
| Handler | `src/Identity/Application/Commands/Handlers/RegisterDeviceCommandHandler.cs:26-39` | `deviceType` is checked against `["plc","inference"]`. `deviceIdentifier` is **not** checked; it is interpolated: `ClientId.From($"{deviceType}-{deviceIdentifier}")` → `"plc-"`. |
| VO | `src/Identity/Domain/RegisteredClient/ClientId.cs:21-47` | `"plc-"` is non-blank, ≤ 255, starts with a letter, contains only letters and `-` — **valid**. The `catch (ArgumentException)` that produces `InvalidDeviceIdentifier` never fires. |
| Keycloak | handler `:48-71` | Client `plc-` created, name `"plc "`, attribute `sse.deviceIdentifier` = `null` / `""`. |
| Persist + event | handler `:82-85`; `ClientRegisteredDomainEventHandler.cs:73-80` | `RegisteredClient` row saved. `SplitDeviceClientId("plc-")` hits its `sep == Length - 1` fallback and publishes `DeviceRegisteredV1` with **`DeviceType = "plc-"`** and `DeviceIdentifier = ""` — a device type that is not in the allowed set. |
| Answer | endpoint `:152-153` | `201 Created` with credentials. |

**Whitespace is already refused.** `"   "` composes `"plc-   "`, which fails
`ClientId`'s grammar (a space is not letter/digit/`.`/`_`/`-`) → 400
`DEVICE_INVALID_IDENTIFIER`. The existing test
`Invalid_device_identifier_returns_InvalidDeviceIdentifier` (`"has space"`) covers
that grammar path. The defect is **exactly null and `""`**, because only those
make the composed id end at the dash.

## 2. The collision the issue asks about — verdict

**Today it is not silent; it is a misleading 409.** A second registration with
an empty identifier (any fab, either type's own prefix) composes the same
`plc-`, and `clients.GetByClientIdAsync` (`:41-46`) finds the first row →
`409 DEVICE_ALREADY_REGISTERED`. If the row were missing but the Keycloak client
existed, `KeycloakClientAlreadyExistsException` (`:73-76`) gives the same 409. So
no duplicate client is ever created; the second caller is told a device they
never named "already exists".

**After this fix the question is moot.** The refusal happens before the
repository lookup and before any Keycloak call, so an empty identifier can never
reach the code that would collide. No separate collision fix is needed.

**Valid-but-duplicate identifiers are out of scope and not a defect.** They
answer 409 by design (spec 008 US4; pinned by
`Re_registration_returns_DeviceAlreadyRegistered`). Candidate follow-ups noticed
while reading — **not implemented, not filed by this spec** — are listed in
plan §6.

## 3. User story

### US1 (P1) — A registration without a device identifier is refused

As an admin registering a PLC or inference device, when I omit
`deviceIdentifier`, send it as `null`, or send `""`, I get `400` naming the
field, and nothing is created — no Keycloak client, no `RegisteredClient` row, no
`DeviceRegisteredV1`. A valid identifier still registers exactly as before.

```gherkin
Feature: POST /devices/register refuses an absent device identifier

  Background:
    Given an admin holding sse.identity.devices.write with access to fab "munich"

  Scenario Outline: absent or empty identifier is bad input
    When they POST /devices/register?fabId=munich with <body>
    Then the response is 400
    And the problem title is "DEVICE_INVALID_IDENTIFIER"
    And no Keycloak client "<type>-" is created
    And no RegisteredClient row is saved

    Examples:
      | body                                            | type      |
      | {"deviceType":"plc"}                            | plc       |
      | {"deviceType":"plc","deviceIdentifier":null}    | plc       |
      | {"deviceType":"plc","deviceIdentifier":""}      | plc       |
      | {"deviceType":"inference","deviceIdentifier":""}| inference |

  Scenario: a valid identifier still registers (happy path, unchanged)
    When they POST /devices/register?fabId=munich with a fresh unique identifier
    Then the response is 201 with clientId "plc-<identifier>" and a client secret

  Scenario: a duplicate valid identifier still conflicts (unchanged)
    Given "plc-station-4" is already registered
    When they register deviceType "plc", deviceIdentifier "station-4" again
    Then the response is 409 "DEVICE_ALREADY_REGISTERED"

  Scenario: an invalid device type is still reported first (unchanged)
    When they POST with {"deviceType":"webhook","deviceIdentifier":""}
    Then the response is 400 "DEVICE_INVALID_TYPE"

  Scenario: authorisation still precedes input validation (unchanged)
    Given a caller without sse.identity.devices.write
    When they POST with {"deviceType":"plc"}
    Then the response is 403
```

The type-first ordering is deliberate and unchanged: the handler already checks
`deviceType` before composing the id, and the new check sits after it.
Authorisation (scope policy, then `IFabAuthorizationGuard`) runs in the endpoint
before the handler, so an unauthorised caller learns nothing about input shape.

## 4. Success criterion ("done")

1. Handler unit tests: `null` and `""` identifiers (both device types) return
   `RegisterDeviceError.InvalidDeviceIdentifier`, and the fake Keycloak's
   `Created` list and the in-memory repository are both empty. **Observed red
   before the fix** (today: success, `ClientId == "plc-"`).
2. Aspire integration test over real HTTP: omitted / `null` / `""` identifier →
   `400` with title `DEVICE_INVALID_IDENTIFIER`. **Observed red before the fix**
   (today: `201`, or `409` once a `plc-` exists).
3. Every existing `RegisterDeviceCommandHandlerTests` and Identity integration
   test passes unmodified; Release build clean.

## 5. Independent end-to-end test procedure (phase 5)

On a booted stack (check first that no other stack is running — one machine,
one Aspire stack), with an admin token for fab `munich`:

1. `POST /devices/register?fabId=munich` body `{"deviceType":"plc"}` → expect 400
   `DEVICE_INVALID_IDENTIFIER`.
2. Same with `"deviceIdentifier":""` → 400.
3. `GET /devices?fabId=munich` → no row with `clientId == "plc-"` created by
   steps 1–2.
4. `POST` with a fresh unique identifier → 201 (control).

The integration test from criterion 2 is this procedure, automated; its green
output is sufficient evidence if the stack is not booted separately.

## 6. Locked tech choices

Minimal API endpoint (ADR-0070) unchanged; hand-rolled command handler returning
`Result<DeviceCredentialsDto, RegisterDeviceError>` (ADR-0042/0047); xUnit +
Shouldly + hand-written fakes (ADR-0052/0054); Aspire fixture for integration
(ADR-0103); sentence-style test names (ADR-0053).
