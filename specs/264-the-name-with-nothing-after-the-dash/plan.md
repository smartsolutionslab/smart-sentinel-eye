# Plan 264 — The name with nothing after the dash

**Spec**: [spec.md](spec.md) · **Issue**: #2575 · **Phase**: 2 (Plan)

## 1. Where it lives

Bounded context **Identity**. Layers touched: **Application** (one handler, the
fix) and **tests** (one unit file, one new integration file). Nothing in Domain,
Infrastructure, Api, Shared.Kernel, Shared.Contracts or AppHost changes. No
messaging change: `DeviceRegisteredV1` simply stops being published for an id
that should never have been composed. No cross-context reference is introduced.

## 2. The mechanism, and why this one

**An early-return refusal in `RegisterDeviceCommandHandler`, reusing the existing
`RegisterDeviceFailures.InvalidDeviceIdentifier(...)` (400
`DEVICE_INVALID_IDENTIFIER`).** Placed immediately after the `deviceType` check
(`:26-29`) and before `ClientId.From` (`:34`):

```csharp
if (string.IsNullOrWhiteSpace(deviceIdentifier))
{
    return Failure(RegisterDeviceFailures.InvalidDeviceIdentifier("must not be empty."));
}
```

Detail on the wire: `deviceIdentifier rejected: must not be empty.`

Why the handler and not the endpoint:

- **The refusal for this field already lives there.** `InvalidDeviceIdentifier`
  exists, is mapped to 400, and is produced by the handler when the composed id
  fails `ClientId`'s grammar. The handler owns the `<type>-<identifier>`
  composition, so it is the one place that knows an empty identifier is what
  makes the composed id degenerate. An endpoint check would need a second code
  (`DEVICE_INVALID_INPUT`, the endpoint's fab-parse code) for the same field —
  two codes for one fault.
- **It mirrors #2480's principle, not its spelling.** #2480
  (`77efcb82`) added the missing null guard *inside the existing refusal path*
  and reused the existing error shape — no new catch, no new code. Here the
  existing refusal path is the handler's, so that is where the check goes.
- **Early return, not `Ensure.That(...).IsNotNullOrWhiteSpace()` in the `try`.**
  Both would work and produce the same error variant. The early return is
  chosen because the adjacent `deviceType` check is already an early return —
  the two input checks then read as a pair — and because it uses no exception
  for control flow where nothing exceptional happened. (#2480 used `Ensure` in a
  `try` because the refusal path there *was* a `catch (ArgumentException)`; here
  the handler has both shapes, and the input-check one is the early return.)
  `string.IsNullOrWhiteSpace` is used, not `IsNullOrEmpty`, so the rule states
  itself even though whitespace would also be caught downstream by the grammar.
  Note the NRT picture: the command declares `string DeviceIdentifier`, so the
  compiler treats the local as not-null and will not warn on either spelling —
  which is exactly why the runtime null from System.Text.Json went unnoticed.

**No value object.** §II binds *domain models' state* (constitution §II,
ADR-0139/0140). `RegisterDeviceRequest` is an Api wire record (ADR-0141: nullable
and primitive shapes are native there) and `RegisterDeviceCommand` is an
Application message; neither is a domain model, and the domain never holds the
identifier on its own — it holds the composed `ClientId` VO, which already
enforces its grammar. A `DeviceIdentifier` VO would ripple through the command,
`DeviceCredentialsDto`, the log message and the event split to fix one missing
check — a refactor mixed into a bug fix (ADR-0036). Not introduced.

**No `Ensure` guard added** (ADR-0105 is about argument preconditions; a user
supplying an empty field is input validation that must answer 400, not an
`ArgumentException` escaping as 500).

## 3. Invariant, stated

A device registration composes `ClientId = "<deviceType>-<deviceIdentifier>"`
only when `deviceType ∈ {plc, inference}` **and** `deviceIdentifier` is not
null, empty or whitespace. Order of refusals: scope policy (403) → fab parse
(400 `DEVICE_INVALID_INPUT`) → fab guard (403) → device type (400
`DEVICE_INVALID_TYPE`) → device identifier (400 `DEVICE_INVALID_IDENTIFIER`,
now including empty) → duplicate (409) → Keycloak (502). Only the empty case is
new.

## 4. Unaffected, checked

- **Idempotency (ADR-0142).** Refusal → `IdempotentOutcome.NothingCreated(...)`
  (`DevicesEndpoints.cs:154`), which releases the key so a corrected retry with
  the same key proceeds. Same path `DEVICE_INVALID_TYPE` already takes.
- **Retry policy (ADR-0143).** `HttpKeycloakAdminClient` (the client-creating
  POST) is not on `RetryEveryMethod()`; only `KeycloakAdminTokenProvider`'s
  mint is (`IdentityInfrastructureModule.cs:157-158`). The fix is upstream of
  both and changes neither.
- **OpenAPI.** `ProducesProblem(400)` is already declared on `RegisterDevice`
  (`DevicesEndpoints.cs:46`).

## 5. Tests

**Unit (red)** — `tests/Identity.Application.Tests/Commands/RegisterDeviceCommandHandlerTests.cs`,
one new theory mirroring the file's existing shape (`HappyCommand`, fakes,
`NullLogger`):

`An_absent_device_identifier_is_refused_before_anything_is_created(string deviceType, string? deviceIdentifier)`
rows: (`plc`, `null`), (`plc`, `""`), (`inference`, `""`). Pass `null!` through
`HappyCommand` for the null row. Assert: `IsSuccess` false; error is
`RegisterDeviceError.InvalidDeviceIdentifier`; `keycloak.Created` empty;
`repo.Clients` empty. Today each row returns success with `ClientId` `"<type>-"`
— red for the right reason.

**Integration (red + guards)** — new
`tests/Integration.Tests/Identity/AbsentDeviceIdentifierIsRefusedIntegrationTests.cs`,
`[Collection(AspireCollection.Name)]`, shaped after
`RegisteredClientConcurrencyIntegrationTests` (`aspire.CreateAdminClientAsync("identity")`,
`PostAsJsonAsync` with **anonymous objects** so a member can be omitted — never
`RegisterDeviceRequest`), fab `munich`.

- a. `{"deviceType":"plc"}` (omitted) → 400, title `DEVICE_INVALID_IDENTIFIER`.
- b. `{"deviceType":"plc","deviceIdentifier":null}` → 400, same title.
- c. `{"deviceType":"plc","deviceIdentifier":""}` → 400, same title.
- d. (green guard) fresh unique identifier → 201. Pins that the fix did not
  over-refuse.

Rows a–c are the reproduction the issue asks for. Expected red output: `201` for
whichever row runs first on a stack without a `plc-` client, `409
DEVICE_ALREADY_REGISTERED` for the others — which also **observes the collision
behaviour of spec §2 over real HTTP**; quote it in the PR. Failure messages
include the response body (existing `DiagnoseAsync` pattern).

**Red-run residue.** The red run genuinely registers `plc-` in fab `munich`
(Keycloak client + row). `DisposeAsync` must, for any row that answered 201,
disable it via `DELETE /devices/plc-?fabId=munich` and write the status to
`ITestOutputHelper`. The Keycloak client itself remains (disable is a soft
operation) — acceptable: CI boots a fresh realm, and on a local persistent
volume a leftover `plc-` only turns later red runs from 201 into 409, both still
not 400. Do not delete the Keycloak client directly: it would desynchronise it
from the row (the reason `RegisteredClientConcurrencyIntegrationTests` avoids
resets).

No test for the 403 / type-first / duplicate scenarios: they are existing
behaviour, already pinned (`Invalid_device_type_returns_InvalidDeviceType`,
`Re_registration_returns_DeviceAlreadyRegistered`, scope-policy tests), and the
fix does not touch their code.

## 6. Out of scope — candidate follow-ups (not implemented, not filed here)

1. **Cross-fab disclosure on register.** `GetByClientIdAsync` is realm-global
   (Keycloak client ids are realm-unique, so uniqueness must be), so an admin of
   fab B registering `plc-x` learns via 409 that fab A has it. DELETE was hardened
   against exactly this enumeration in spec 180 US1; register was not. Whether
   to mask it is a security decision, possibly an ADR — not a bug fix.
2. **Identifier grammar beyond non-empty.** `"-"`, `"."`, `"-x"` are accepted
   (`plc--`, `plc-.`), because `ClientId` checks only its own first character
   (the type's). FR-008's MQTT ACL binding may or may not care; there is no
   stated grammar for the identifier part. Needs a requirements decision.
3. **Existing `plc-` / `inference-` rows.** Any environment that hit the defect
   holds a client whose `DeviceRegisteredV1` carried `DeviceType = "plc-"`.
   There is no production deployment (ADR-0118), and `DELETE /devices/plc-`
   still parses, so no migration or cleanup is planned.

## 7. Constitution / ADR alignment

§II: no domain state added or changed. §IV: N/A. §Testing: behaviour-changing →
red first (ADR-0139). No cross-context references (NetArchTest unaffected).
Coverage: Application gate (≥ 80 %) gains one branch plus its test.
